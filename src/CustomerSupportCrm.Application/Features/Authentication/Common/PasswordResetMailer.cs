using System.Globalization;
using CustomerSupportCrm.Application.Abstractions.Channels;
using CustomerSupportCrm.Application.Features.Channels;
using Microsoft.Extensions.Options;

namespace CustomerSupportCrm.Application.Features.Authentication.Common;

/// <summary>
/// Queues password reset links for staff users and portal accounts through the outbox. In
/// Development the Log email provider (<c>Channels:Email:Provider = Log</c>) writes the email,
/// link included, to the API log.
/// </summary>
internal sealed class PasswordResetMailer(CustomerMessenger messenger, IOptions<StaffAppOptions> staffApp, IOptions<CustomerPortalOptions> portal)
{
    public Task QueueStaffAsync(string email, string token, CancellationToken cancellationToken) =>
        messenger.QueuePasswordResetAsync(email, CurrentLanguage(), $"{staffApp.Value.BaseUrl.TrimEnd('/')}/login/reset-password?token={Uri.EscapeDataString(token)}", cancellationToken);

    public Task QueuePortalAsync(string email, string language, string token, CancellationToken cancellationToken) =>
        messenger.QueuePasswordResetAsync(email, language, $"{portal.Value.BaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}", cancellationToken);

    /// <summary>Staff users have no language setting: use the request culture (Accept-Language).</summary>
    private static string CurrentLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "ar" : "en";
}
