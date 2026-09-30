using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Integrations;
using CustomerSupportCrm.Domain.Integrations;
using CustomerSupportCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportCrm.Infrastructure.Integrations;

/// <summary>
/// Authenticates external systems by the X-Api-Key header. Issues an identity with
/// actor=api_client and one "scope" claim per granted scope.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApplicationDbContext db,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(presented))
        {
            return AuthenticateResult.NoResult();
        }

        var hash = ApiKey.Hash(presented);
        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.KeyHash == hash, Context.RequestAborted);
        var now = time.GetUtcNow();
        if (key is null || !key.IsUsable(now))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        key.RecordUse(now);
        await db.SaveChangesAsync(Context.RequestAborted);

        var identity = new ClaimsIdentity(
            [
                new Claim(CrmClaimTypes.Actor, ActorTypes.ApiClient),
                new Claim(CrmClaimTypes.Subject, key.Id.ToString()),
                new Claim(CrmClaimTypes.Name, key.Name),
                .. key.Scopes.Select(scope => new Claim(CrmClaimTypes.Scope, scope)),
            ],
            SchemeName,
            CrmClaimTypes.Name,
            CrmClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

internal sealed class HttpCurrentApiClient(IHttpContextAccessor accessor) : ICurrentApiClient
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.HasClaim(CrmClaimTypes.Actor, ActorTypes.ApiClient) == true;

    public Guid ApiKeyId => IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(CrmClaimTypes.Subject), out var id) ? id : Guid.Empty;

    public string Name => Principal?.FindFirstValue(CrmClaimTypes.Name) ?? string.Empty;
}

internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("CustomerSupportCrm.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}

/// <summary>
/// Posts signed JSON (X-Crm-Signature: sha256=HMAC(secret, "{timestamp}.{body}")) and refuses to
/// connect to private, loopback, link-local or otherwise internal addresses, checked on the
/// resolved IP at connect time so DNS rebinding cannot bypass it.
/// </summary>
internal sealed class SafeWebhookSender(HttpClient http, TimeProvider time) : IWebhookSender
{
    public const string ClientName = "webhooks";

    public async Task<WebhookSendResult> SendAsync(string url, string eventType, Guid deliveryId, string payload, string secret, CancellationToken cancellationToken)
    {
        var timestamp = time.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url)) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Crm-Event", eventType);
        request.Headers.Add("X-Crm-Delivery", deliveryId.ToString());
        request.Headers.Add("X-Crm-Timestamp", timestamp);
        request.Headers.Add("X-Crm-Signature", $"sha256={signature}");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? new WebhookSendResult(true, (int)response.StatusCode, null)
                : new WebhookSendResult(false, (int)response.StatusCode, $"HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException exception)
        {
            return new WebhookSendResult(false, null, exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookSendResult(false, null, "Timed out.");
        }
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var target = addresses.FirstOrDefault(a => !IsInternal(a))
                ?? throw new HttpRequestException("The webhook host resolves to a private or internal address.");

            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    private static bool IsInternal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast;
        }

        var b = address.GetAddressBytes();
        return b[0] == 10
            || b[0] == 127
            || b[0] == 0
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || b[0] >= 224;
    }
}
