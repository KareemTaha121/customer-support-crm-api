using System.Text.Json;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Notifications;
using CustomerSupportCrm.Domain.Notifications;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Notifications;

/// <summary>
/// Creates in-app notifications inside the caller's unit of work; they are pushed to connected
/// clients after commit. Used by domain-event handlers across features.
/// </summary>
public sealed class NotificationSender(IApplicationDbContext db, TimeProvider time)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Notify(UserId recipient, string type, string title, string? link = null, object? data = null, string? body = null) =>
        db.Notifications.Add(Notification.Create(
            recipient,
            type,
            title,
            body,
            link,
            data is null ? null : JsonSerializer.Serialize(data, JsonOptions),
            time.GetUtcNow()));

    public void NotifyMany(IEnumerable<UserId> recipients, string type, string title, string? link = null, object? data = null, UserId? except = null)
    {
        foreach (var recipient in recipients.Distinct().Where(r => r != except))
        {
            Notify(recipient, type, title, link, data);
        }
    }
}

public sealed record ListNotificationsQuery(int Page = 1, int PageSize = PaginationExtensions.DefaultPageSize, bool UnreadOnly = false)
    : IRequest<PagedResult<NotificationResponse>>;

internal sealed class ListNotificationsValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsValidator()
    {
        RuleFor(q => q.Page).ValidPage();
        RuleFor(q => q.PageSize).ValidPageSize();
    }
}

internal sealed class ListNotificationsHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListNotificationsQuery, PagedResult<NotificationResponse>>
{
    public async Task<PagedResult<NotificationResponse>> Handle(ListNotificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var page = await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == userId && (!request.UnreadOnly || n.ReadAt == null))
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Select(n => new { n.Id, n.Type, n.Title, n.Body, n.Link, n.Data, n.CreatedAt, n.ReadAt })
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        return page.Map(n => new NotificationResponse(n.Id, n.Type, n.Title, n.Body, n.Link, ParseJson(n.Data), n.CreatedAt, n.ReadAt));
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed record GetUnreadCountQuery : IRequest<UnreadCountResponse>;

internal sealed class GetUnreadCountHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<GetUnreadCountQuery, UnreadCountResponse>
{
    public async Task<UnreadCountResponse> Handle(GetUnreadCountQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return new UnreadCountResponse(await db.Notifications.CountAsync(n => n.RecipientId == userId && n.ReadAt == null, cancellationToken));
    }
}

/// <param name="NotificationId">Null marks every unread notification as read.</param>
public sealed record MarkNotificationsReadCommand(Guid? NotificationId) : IRequest<UnreadCountResponse>;

internal sealed class MarkNotificationsReadHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<MarkNotificationsReadCommand, UnreadCountResponse>
{
    public async Task<UnreadCountResponse> Handle(MarkNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var now = time.GetUtcNow();

        var unread = await db.Notifications
            .Where(n => n.RecipientId == userId && n.ReadAt == null && (request.NotificationId == null || n.Id == request.NotificationId))
            .ToListAsync(cancellationToken);
        foreach (var notification in unread)
        {
            notification.MarkRead(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new UnreadCountResponse(await db.Notifications.CountAsync(n => n.RecipientId == userId && n.ReadAt == null, cancellationToken));
    }
}

internal sealed class NotificationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications").WithTags("Notifications");

        group.MapGet("/", async ([AsParameters] ListNotificationsQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .WithName("ListNotifications")
            .Produces<ApiResponse<IReadOnlyList<NotificationResponse>>>();

        group.MapGet("/unread-count", async (ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new GetUnreadCountQuery(), ct)))
            .WithName("GetUnreadNotificationCount")
            .Produces<ApiResponse<UnreadCountResponse>>();

        group.MapPost("/{id:guid}/read", async (Guid id, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new MarkNotificationsReadCommand(id), ct)))
            .WithName("MarkNotificationRead")
            .Produces<ApiResponse<UnreadCountResponse>>();

        group.MapPost("/read-all", async (ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new MarkNotificationsReadCommand(null), ct)))
            .WithName("MarkAllNotificationsRead")
            .Produces<ApiResponse<UnreadCountResponse>>();
    }
}
