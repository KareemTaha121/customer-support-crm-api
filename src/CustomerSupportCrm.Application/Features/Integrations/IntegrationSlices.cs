using System.Text.Json;
using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Integrations;
using CustomerSupportCrm.Application.Abstractions.Messaging;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Integrations;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Integrations;

public sealed record CreateApiKeyRequest(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt);

public sealed record ApiKeyResponse(Guid Id, string Name, string DisplayPrefix, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, DateTimeOffset CreatedAt);

/// <param name="Key">The plaintext key. Shown only in this response.</param>
public sealed record CreatedApiKeyResponse(ApiKeyResponse ApiKey, string Key);

public sealed record WebhookRequest(string Name, string Url, IReadOnlyList<string> Events, bool IsActive = true);

public sealed record WebhookResponse(Guid Id, string Name, string Url, IReadOnlyList<string> Events, bool IsActive, DateTimeOffset CreatedAt);

/// <param name="Secret">Signing secret. Shown only when created or rotated.</param>
public sealed record WebhookWithSecretResponse(WebhookResponse Webhook, string Secret);

public sealed record WebhookDeliveryResponse(Guid Id, string EventType, bool Delivered, bool Failed, int Attempts, int? LastStatusCode, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt);

public sealed record IntegrationCatalogResponse(IReadOnlyList<string> ApiScopes, IReadOnlyList<string> WebhookEvents);

internal static class IntegrationMapping
{
    public const string ApiKeyNotFound = "API_KEY_NOT_FOUND";
    public const string WebhookNotFound = "WEBHOOK_NOT_FOUND";

    public static ApiKeyResponse ToResponse(ApiKey k) => new(k.Id, k.Name, k.DisplayPrefix, k.Scopes, k.ExpiresAt, k.LastUsedAt, k.RevokedAt, k.CreatedAt);

    public static WebhookResponse ToResponse(WebhookSubscription w) => new(w.Id, w.Name, w.Url, w.Events, w.IsActive, w.CreatedAt);
}

// ---------- Administration (integrations.manage) ----------

internal sealed class IntegrationAdministrationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/integrations").WithTags("Integrations").RequireAuthorization(Permissions.IntegrationsManage);

        group.MapGet("/catalog", () => ApiResults.Ok(new IntegrationCatalogResponse(ApiScopes.All, WebhookEvents.All)))
            .WithName("GetIntegrationCatalog")
            .Produces<ApiResponse<IntegrationCatalogResponse>>();

        group.MapGet("/api-keys", async (IApplicationDbContext db, CancellationToken ct) =>
                ApiResults.Ok((await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct)).Select(IntegrationMapping.ToResponse).ToList()))
            .WithName("ListApiKeys")
            .Produces<ApiResponse<List<ApiKeyResponse>>>();

        group.MapPost("/api-keys", async (CreateApiKeyRequest request, IApplicationDbContext db, IAuditTrail audit, CancellationToken ct) =>
            {
                var (key, plaintext) = ApiKey.Create(request.Name, request.Scopes ?? [], request.ExpiresAt);
                db.ApiKeys.Add(key);
                audit.Record("integrations.api_key_created", "ApiKey", key.Id.ToString(), newValues: new { key.Name, key.Scopes, key.DisplayPrefix });
                await db.SaveChangesAsync(ct);
                return ApiResults.Created($"/api/v1/integrations/api-keys/{key.Id}", new CreatedApiKeyResponse(IntegrationMapping.ToResponse(key), plaintext));
            })
            .WithName("CreateApiKey")
            .Produces<ApiResponse<CreatedApiKeyResponse>>(StatusCodes.Status201Created);

        group.MapPost("/api-keys/{id:guid}/revoke", async (Guid id, IApplicationDbContext db, IAuditTrail audit, TimeProvider time, CancellationToken ct) =>
            {
                var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == id, ct) ?? throw new NotFoundException(IntegrationMapping.ApiKeyNotFound, "The API key was not found.");
                key.Revoke(time.GetUtcNow());
                audit.Record("integrations.api_key_revoked", "ApiKey", key.Id.ToString(), newValues: new { key.Name });
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(IntegrationMapping.ToResponse(key));
            })
            .WithName("RevokeApiKey")
            .Produces<ApiResponse<ApiKeyResponse>>();

        group.MapGet("/webhooks", async (IApplicationDbContext db, CancellationToken ct) =>
                ApiResults.Ok((await db.WebhookSubscriptions.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct)).Select(IntegrationMapping.ToResponse).ToList()))
            .WithName("ListWebhooks")
            .Produces<ApiResponse<List<WebhookResponse>>>();

        group.MapPost("/webhooks", async (WebhookRequest request, IApplicationDbContext db, ISecretProtector protector, IAuditTrail audit, CancellationToken ct) =>
            {
                var secret = WebhookSubscription.NewSecret();
                var webhook = WebhookSubscription.Create(request.Name, request.Url, request.Events ?? [], protector.Protect(secret));
                webhook.Update(request.Name, request.Url, request.Events ?? [], request.IsActive);
                db.WebhookSubscriptions.Add(webhook);
                audit.Record("integrations.webhook_created", "Webhook", webhook.Id.ToString(), newValues: new { webhook.Name, webhook.Url, webhook.Events });
                await db.SaveChangesAsync(ct);
                return ApiResults.Created($"/api/v1/integrations/webhooks/{webhook.Id}", new WebhookWithSecretResponse(IntegrationMapping.ToResponse(webhook), secret));
            })
            .WithName("CreateWebhook")
            .Produces<ApiResponse<WebhookWithSecretResponse>>(StatusCodes.Status201Created);

        group.MapPut("/webhooks/{id:guid}", async (Guid id, WebhookRequest request, IApplicationDbContext db, IAuditTrail audit, CancellationToken ct) =>
            {
                var webhook = await db.WebhookSubscriptions.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException(IntegrationMapping.WebhookNotFound, "The webhook was not found.");
                webhook.Update(request.Name, request.Url, request.Events ?? [], request.IsActive);
                audit.Record("integrations.webhook_updated", "Webhook", webhook.Id.ToString(), newValues: new { webhook.Name, webhook.Url, webhook.Events, webhook.IsActive });
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(IntegrationMapping.ToResponse(webhook));
            })
            .WithName("UpdateWebhook")
            .Produces<ApiResponse<WebhookResponse>>();

        group.MapPost("/webhooks/{id:guid}/rotate-secret", async (Guid id, IApplicationDbContext db, ISecretProtector protector, IAuditTrail audit, CancellationToken ct) =>
            {
                var webhook = await db.WebhookSubscriptions.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException(IntegrationMapping.WebhookNotFound, "The webhook was not found.");
                var secret = WebhookSubscription.NewSecret();
                webhook.RotateSecret(protector.Protect(secret));
                audit.Record("integrations.webhook_secret_rotated", "Webhook", webhook.Id.ToString());
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(new WebhookWithSecretResponse(IntegrationMapping.ToResponse(webhook), secret));
            })
            .WithName("RotateWebhookSecret")
            .Produces<ApiResponse<WebhookWithSecretResponse>>();

        group.MapDelete("/webhooks/{id:guid}", async (Guid id, IApplicationDbContext db, IAuditTrail audit, CancellationToken ct) =>
            {
                var webhook = await db.WebhookSubscriptions.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException(IntegrationMapping.WebhookNotFound, "The webhook was not found.");
                db.WebhookSubscriptions.Remove(webhook);
                audit.Record("integrations.webhook_deleted", "Webhook", webhook.Id.ToString(), oldValues: new { webhook.Name, webhook.Url });
                await db.SaveChangesAsync(ct);
                return ApiResults.Success();
            })
            .WithName("DeleteWebhook")
            .Produces<ApiResponse<object?>>();

        group.MapPost("/webhooks/{id:guid}/test", async (Guid id, IApplicationDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var webhook = await db.WebhookSubscriptions.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException(IntegrationMapping.WebhookNotFound, "The webhook was not found.");
                var payload = JsonSerializer.Serialize(new { type = "webhook.test", occurredAt = time.GetUtcNow(), data = new { message = "Test delivery" } }, WebhookPublisher.Json);
                db.WebhookDeliveries.Add(WebhookDelivery.Queue(webhook.Id, "webhook.test", payload, time.GetUtcNow()));
                await db.SaveChangesAsync(ct);
                return ApiResults.Success();
            })
            .WithName("TestWebhook")
            .Produces<ApiResponse<object?>>();

        group.MapGet("/webhooks/{id:guid}/deliveries", async (Guid id, IApplicationDbContext db, CancellationToken ct) =>
                ApiResults.Ok(await db.WebhookDeliveries.AsNoTracking().Where(d => d.SubscriptionId == id).OrderByDescending(d => d.CreatedAt).Take(100)
                    .Select(d => new WebhookDeliveryResponse(d.Id, d.EventType, d.Delivered, d.Failed, d.Attempts, d.LastStatusCode, d.LastError, d.CreatedAt, d.DeliveredAt))
                    .ToListAsync(ct)))
            .WithName("ListWebhookDeliveries")
            .Produces<ApiResponse<List<WebhookDeliveryResponse>>>();

        group.MapPost("/deliveries/{id:guid}/retry", async (Guid id, IApplicationDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var delivery = await db.WebhookDeliveries.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("WEBHOOK_DELIVERY_NOT_FOUND", "The delivery was not found.");
                delivery.Retry(time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                return ApiResults.Success();
            })
            .WithName("RetryWebhookDelivery")
            .Produces<ApiResponse<object?>>();
    }
}

// ---------- Event publication ----------

/// <summary>Queues a delivery per active subscription listening to an event (same transaction as the change).</summary>
public sealed class WebhookPublisher(IApplicationDbContext db, TimeProvider time)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(string eventType, object data, CancellationToken cancellationToken)
    {
        var subscriptions = await db.WebhookSubscriptions.AsNoTracking()
            .Where(w => w.IsActive && w.Events.Contains(eventType))
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        foreach (var id in subscriptions)
        {
            var deliveryId = Guid.CreateVersion7();
            var payload = JsonSerializer.Serialize(new { id = deliveryId, type = eventType, occurredAt = now, data }, Json);
            db.WebhookDeliveries.Add(WebhookDelivery.Queue(id, eventType, payload, now));
        }
    }
}

internal sealed class WebhookEventHandlers(IApplicationDbContext db, WebhookPublisher publisher)
    : INotificationHandler<DomainEventNotification<TicketCreatedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketStatusChangedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketAssignedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketMessageAddedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketFeedbackSubmittedDomainEvent>>,
      INotificationHandler<DomainEventNotification<CustomerCreatedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketCreatedDomainEvent> notification, CancellationToken cancellationToken) =>
        await PublishTicketAsync(WebhookEvents.TicketCreated, notification.DomainEvent.TicketId, new { channel = notification.DomainEvent.Channel.ToString() }, cancellationToken);

    public async Task Handle(DomainEventNotification<TicketStatusChangedDomainEvent> notification, CancellationToken cancellationToken) =>
        await PublishTicketAsync(WebhookEvents.TicketStatusChanged, notification.DomainEvent.TicketId, new { from = notification.DomainEvent.PreviousStatus.ToString(), to = notification.DomainEvent.Status.ToString() }, cancellationToken);

    public async Task Handle(DomainEventNotification<TicketAssignedDomainEvent> notification, CancellationToken cancellationToken) =>
        await PublishTicketAsync(WebhookEvents.TicketAssigned, notification.DomainEvent.TicketId, new { agentId = notification.DomainEvent.AgentId?.Value }, cancellationToken);

    public async Task Handle(DomainEventNotification<TicketMessageAddedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        if (e.IsInternal)
        {
            return;
        }

        var message = db.TicketMessages.Local.FirstOrDefault(m => m.Id == e.MessageId);
        await PublishTicketAsync(WebhookEvents.TicketMessageAdded, e.TicketId, new { messageId = e.MessageId, author = e.AuthorType.ToString(), body = message?.Body }, cancellationToken);
    }

    public async Task Handle(DomainEventNotification<TicketFeedbackSubmittedDomainEvent> notification, CancellationToken cancellationToken) =>
        await PublishTicketAsync(WebhookEvents.TicketFeedback, notification.DomainEvent.TicketId, new { rating = notification.DomainEvent.Rating }, cancellationToken);

    public async Task Handle(DomainEventNotification<CustomerCreatedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var customer = db.Customers.Local.FirstOrDefault(c => c.Id == notification.DomainEvent.CustomerId);
        await publisher.PublishAsync(WebhookEvents.CustomerCreated, new { customerId = notification.DomainEvent.CustomerId, number = customer?.Number, name = customer?.Name, email = customer?.PrimaryEmail }, cancellationToken);
    }

    private async Task PublishTicketAsync(string eventType, Guid ticketId, object details, CancellationToken cancellationToken)
    {
        var ticket = await TicketQueries.FindTrackedAsync(db, ticketId, cancellationToken);
        if (ticket is null)
        {
            return;
        }

        await publisher.PublishAsync(
            eventType,
            new { ticketId, number = ticket.Number, subject = ticket.Subject, status = ticket.Status.ToString(), priority = ticket.Priority.ToString(), customerId = ticket.CustomerId, details },
            cancellationToken);
    }
}

public sealed record DispatchWebhooksCommand : IRequest;

internal sealed class DispatchWebhooksHandler(IApplicationDbContext db, IWebhookSender sender, ISecretProtector protector, TimeProvider time) : IRequestHandler<DispatchWebhooksCommand>
{
    public async Task Handle(DispatchWebhooksCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.WebhookDeliveries
            .Where(d => !d.Delivered && !d.Failed && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        var subscriptionIds = due.Select(d => d.SubscriptionId).Distinct().ToList();
        var subscriptions = await db.WebhookSubscriptions.AsNoTracking().Where(w => subscriptionIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, cancellationToken);

        foreach (var delivery in due)
        {
            if (!subscriptions.TryGetValue(delivery.SubscriptionId, out var subscription) || !subscription.IsActive)
            {
                delivery.MarkAttemptFailed(null, "Subscription inactive.", now);
                continue;
            }

            var result = await sender.SendAsync(subscription.Url, delivery.EventType, delivery.Id, delivery.Payload, protector.Unprotect(subscription.ProtectedSecret), cancellationToken);
            if (result.Succeeded)
            {
                delivery.MarkDelivered(result.StatusCode ?? 200, time.GetUtcNow());
            }
            else
            {
                delivery.MarkAttemptFailed(result.StatusCode, result.Error ?? "Unknown error", time.GetUtcNow());
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

// ---------- External API (API keys) ----------

public sealed record ExternalCustomerUpsertRequest(string Name, string? Type, string? CompanyName, string? Language, string? Email, string? Phone, string? BranchCode);

public sealed record ExternalCustomerResponse(Guid Id, string Number, string Name, string? Email, string? Phone, string? ExternalSystem, string? ExternalId, DateTimeOffset CreatedAt);

/// <param name="CustomerId">Or identify the customer by <paramref name="CustomerEmail"/> (created when unknown).</param>
public sealed record ExternalCreateTicketRequest(Guid? CustomerId, string? CustomerEmail, string? CustomerName, string Subject, string Description, string? Priority, Guid? CategoryId);

public sealed record ExternalTicketResponse(Guid Id, string Number, string Subject, string Status, string Priority, Guid CustomerId, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? ResolvedAt);

public sealed record ExternalMessageRequest(string Body);

public sealed record UpsertExternalCustomerCommand(string System, string ExternalId, ExternalCustomerUpsertRequest Customer) : IRequest<ExternalCustomerResponse>;

internal sealed class UpsertExternalCustomerValidator : AbstractValidator<UpsertExternalCustomerCommand>
{
    public UpsertExternalCustomerValidator()
    {
        RuleFor(c => c.System).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9_.-]+$");
        RuleFor(c => c.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Customer.Name).NotEmpty().MaximumLength(Customer.NameMaxLength);
        RuleFor(c => c.Customer.Type).Must(t => t is null || Enum.TryParse<CustomerType>(t, true, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Customer.Language).Must(l => l is null or "en" or "ar").WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Customer.Email).EmailAddress().MaximumLength(254);
    }
}

/// <summary>ERP/CRM sync: create or update the customer identified by (system, externalId).</summary>
internal sealed class UpsertExternalCustomerHandler(IApplicationDbContext db, ISequenceGenerator sequences, Channels.CustomerResolver resolver)
    : IRequestHandler<UpsertExternalCustomerCommand, ExternalCustomerResponse>
{
    public async Task<ExternalCustomerResponse> Handle(UpsertExternalCustomerCommand request, CancellationToken cancellationToken)
    {
        var input = request.Customer;
        var customer = await db.Customers.Include(c => c.Contacts)
            .SingleOrDefaultAsync(c => c.ExternalSystem == request.System && c.ExternalId == request.ExternalId, cancellationToken);
        var type = input.Type is null ? CustomerType.Individual : Enum.Parse<CustomerType>(input.Type, true);

        if (customer is null)
        {
            Guid branchId;
            if (input.BranchCode is { } code)
            {
                var normalized = Domain.Organizations.Branch.NormalizeCode(code);
                branchId = await db.Branches.Where(b => b.Code == normalized && b.IsActive).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException(Common.Authorization.OrganizationErrors.BranchNotFound, "The branch was not found.");
            }
            else
            {
                branchId = (await resolver.DefaultUnitAsync(null, cancellationToken)).BranchId;
            }

            var number = Customer.FormatNumber(await sequences.NextValueAsync(Sequences.CustomerNumbers, cancellationToken));
            customer = Customer.Create(number, type, input.Name, input.CompanyName, input.Language ?? "en", [], branchId, null);
            customer.LinkExternal(request.System, request.ExternalId);
            db.Customers.Add(customer);
        }
        else
        {
            customer.UpdateProfile(type, input.Name, input.CompanyName, input.Language ?? customer.PreferredLanguage, customer.Tags);
        }

        if (!string.IsNullOrWhiteSpace(input.Email) && !customer.Contacts.Any(c => c.Type == ContactType.Email && c.Value == Domain.Shared.EmailAddress.Normalize(input.Email)))
        {
            customer.AddContact(ContactType.Email, input.Email, request.System, isPrimary: false);
        }

        if (!string.IsNullOrWhiteSpace(input.Phone) && Domain.Shared.PhoneNumber.TryCreate(input.Phone, out var phone) && !customer.Contacts.Any(c => c.Value == phone!.Value))
        {
            customer.AddContact(ContactType.Phone, phone!.Value, request.System, isPrimary: false);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ExternalCustomerResponse(customer.Id, customer.Number, customer.Name, customer.PrimaryEmail, customer.PrimaryPhone, customer.ExternalSystem, customer.ExternalId, customer.CreatedAt);
    }
}

public sealed record ExternalCreateTicketCommand(ExternalCreateTicketRequest Ticket) : IRequest<ExternalTicketResponse>;

internal sealed class ExternalCreateTicketValidator : AbstractValidator<ExternalCreateTicketCommand>
{
    public ExternalCreateTicketValidator()
    {
        RuleFor(c => c.Ticket.Subject).NotEmpty().MaximumLength(Ticket.SubjectMaxLength);
        RuleFor(c => c.Ticket.Description).NotEmpty().MaximumLength(Ticket.DescriptionMaxLength);
        RuleFor(c => c.Ticket.Priority).Must(p => p is null || Enum.TryParse<TicketPriority>(p, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Ticket).Must(t => t.CustomerId is not null || !string.IsNullOrWhiteSpace(t.CustomerEmail)).WithErrorCode(ErrorCodes.Required).WithMessage("Provide customerId or customerEmail.");
        RuleFor(c => c.Ticket.CustomerEmail).EmailAddress().When(c => c.Ticket.CustomerEmail is not null);
    }
}

internal sealed class ExternalCreateTicketHandler(IApplicationDbContext db, Channels.CustomerResolver resolver, Tickets.TicketFactory tickets)
    : IRequestHandler<ExternalCreateTicketCommand, ExternalTicketResponse>
{
    public async Task<ExternalTicketResponse> Handle(ExternalCreateTicketCommand request, CancellationToken cancellationToken)
    {
        var input = request.Ticket;
        Guid customerId;
        string? replyAddress = null;
        if (input.CustomerId is { } id)
        {
            customerId = await db.Customers.AnyAsync(c => c.Id == id, cancellationToken) ? id : throw Customers.Common.CustomerQueries.NotFound();
        }
        else
        {
            var customer = await resolver.ResolveAsync(ContactType.Email, input.CustomerEmail!, input.CustomerName, null, cancellationToken);
            customerId = customer.Id;
            replyAddress = Domain.Shared.EmailAddress.Normalize(input.CustomerEmail);
        }

        var categoryId = input.CategoryId is { } category && await db.TicketCategories.AnyAsync(c => c.Id == category && c.IsActive, cancellationToken) ? category : (Guid?)null;
        var ticket = await tickets.CreateAsync(
            new Tickets.NewTicket(customerId, input.Subject, input.Description, categoryId, input.Priority is null ? TicketPriority.Medium : Enum.Parse<TicketPriority>(input.Priority), TicketChannel.Api, null, null, [], replyAddress),
            scope: null,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ExternalApi.ToResponse(ticket);
    }
}

internal static class ExternalApi
{
    public static ExternalTicketResponse ToResponse(Ticket t) => new(t.Id, t.Number, t.Subject, t.Status.ToString(), t.Priority.ToString(), t.CustomerId, t.CreatedAt, t.UpdatedAt, t.ResolvedAt);
}

internal sealed class ExternalApiEndpoints : IExternalEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/customers", async (string? externalSystem, string? externalId, string? email, IApplicationDbContext db, CancellationToken ct) =>
            {
                var query = db.Customers.AsNoTracking();
                if (externalSystem is not null && externalId is not null)
                {
                    query = query.Where(c => c.ExternalSystem == externalSystem && c.ExternalId == externalId);
                }
                else if (email is not null)
                {
                    var normalized = Domain.Shared.EmailAddress.Normalize(email);
                    query = query.Where(c => c.Contacts.Any(x => x.Type == ContactType.Email && x.Value == normalized));
                }
                else
                {
                    throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("Email", "Filter by externalSystem + externalId or by email.") { ErrorCode = ErrorCodes.Required }]);
                }

                return ApiResults.Ok(await query.Take(20).Select(c => new ExternalCustomerResponse(c.Id, c.Number, c.Name, c.PrimaryEmail, c.PrimaryPhone, c.ExternalSystem, c.ExternalId, c.CreatedAt)).ToListAsync(ct));
            })
            .RequireAuthorization(PolicyNamesFor.CustomersRead)
            .WithName("ExternalFindCustomers")
            .Produces<ApiResponse<List<ExternalCustomerResponse>>>();

        app.MapPut("/customers/{system}/{externalId}", async (string system, string externalId, ExternalCustomerUpsertRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new UpsertExternalCustomerCommand(system, externalId, request), ct)))
            .RequireAuthorization(PolicyNamesFor.CustomersWrite)
            .WithName("ExternalUpsertCustomer")
            .Produces<ApiResponse<ExternalCustomerResponse>>();

        app.MapPost("/tickets", async (ExternalCreateTicketRequest request, ISender sender, CancellationToken ct) =>
            {
                var ticket = await sender.Send(new ExternalCreateTicketCommand(request), ct);
                return ApiResults.Created($"/api/v1/external/tickets/{ticket.Number}", ticket);
            })
            .RequireAuthorization(PolicyNamesFor.TicketsWrite)
            .WithName("ExternalCreateTicket")
            .Produces<ApiResponse<ExternalTicketResponse>>(StatusCodes.Status201Created);

        app.MapGet("/tickets/{number}", async (string number, IApplicationDbContext db, CancellationToken ct) =>
            {
                var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Number == number, ct) ?? throw TicketQueries.NotFound();
                return ApiResults.Ok(ExternalApi.ToResponse(ticket));
            })
            .RequireAuthorization(PolicyNamesFor.TicketsRead)
            .WithName("ExternalGetTicket")
            .Produces<ApiResponse<ExternalTicketResponse>>();

        app.MapPost("/tickets/{number}/messages", async (string number, ExternalMessageRequest request, IApplicationDbContext db, Tickets.TicketMessageWriter writer, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 20_000)
                {
                    throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("Body", "The message is empty or too long.") { ErrorCode = ErrorCodes.Required }]);
                }

                var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Number == number, ct) ?? throw TicketQueries.NotFound();
                await writer.AddAsync(ticket, MessageAuthorType.Customer, null, ticket.CustomerId, request.Body, false, TicketChannel.Api, null, [], [], ct);
                await db.SaveChangesAsync(ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(PolicyNamesFor.TicketsWrite)
            .WithName("ExternalAddTicketMessage")
            .Produces<ApiResponse<object?>>();
    }
}

internal static class PolicyNamesFor
{
    public static readonly string CustomersRead = Abstractions.Authorization.PolicyNames.ForScope(ApiScopes.CustomersRead);
    public static readonly string CustomersWrite = Abstractions.Authorization.PolicyNames.ForScope(ApiScopes.CustomersWrite);
    public static readonly string TicketsRead = Abstractions.Authorization.PolicyNames.ForScope(ApiScopes.TicketsRead);
    public static readonly string TicketsWrite = Abstractions.Authorization.PolicyNames.ForScope(ApiScopes.TicketsWrite);
}
