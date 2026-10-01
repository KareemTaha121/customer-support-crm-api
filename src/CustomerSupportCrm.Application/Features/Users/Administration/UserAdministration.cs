using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Application.Features.Users.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Users.Administration;

public sealed record UpdateUserCommand(Guid UserId, string DisplayName) : IRequest<UserResponse>;

internal sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator() => RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(User.DisplayNameMaxLength);
}

internal sealed class UpdateUserHandler(IApplicationDbContext db, IAuditTrail audit, TimeProvider time) : IRequestHandler<UpdateUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var userId = new UserId(request.UserId);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(UserErrors.UserNotFound, "The user was not found.");

        var before = new { user.DisplayName };
        user.Rename(request.DisplayName);
        audit.Record("users.updated", AuditEntityTypes.User, user.Id.ToString(), before, new { user.DisplayName });
        await db.SaveChangesAsync(cancellationToken);

        return await UserQueries.GetResponseAsync(db, user.Id, time.GetUtcNow(), cancellationToken);
    }
}

public sealed record SetUserScopesCommand(Guid UserId, IReadOnlyList<UserScopeRequest> Scopes) : IRequest<UserResponse>;

internal sealed class SetUserScopesValidator : AbstractValidator<SetUserScopesCommand>
{
    public SetUserScopesValidator() => RuleFor(c => c.Scopes).NotNull();
}

internal sealed class SetUserScopesHandler(IApplicationDbContext db, IAuditTrail audit, TimeProvider time) : IRequestHandler<SetUserScopesCommand, UserResponse>
{
    public async Task<UserResponse> Handle(SetUserScopesCommand request, CancellationToken cancellationToken)
    {
        var userId = new UserId(request.UserId);
        var user = await db.Users.Include(u => u.Scopes).SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(UserErrors.UserNotFound, "The user was not found.");

        var current = user.Scopes.Select(s => (s.BranchId, s.DepartmentId)).ToList();
        var scopes = await UserQueries.ResolveScopesAsync(db, request.Scopes, cancellationToken, current);
        var before = user.Scopes.Select(s => new { s.BranchId, s.DepartmentId }).ToList();

        user.SetScopes(scopes);
        audit.Record(
            "users.scopes_changed",
            AuditEntityTypes.User,
            user.Id.ToString(),
            new { scopes = before },
            new { scopes = scopes.Select(s => new { s.BranchId, s.DepartmentId }) });
        await db.SaveChangesAsync(cancellationToken);

        return await UserQueries.GetResponseAsync(db, user.Id, time.GetUtcNow(), cancellationToken);
    }
}

/// <summary>Administrator sets a new password; every session of the user is revoked.</summary>
public sealed record ResetUserPasswordCommand(Guid UserId, string NewPassword) : IRequest;

internal sealed class ResetUserPasswordValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordValidator() => RuleFor(c => c.NewPassword).ValidPassword();
}

internal sealed class ResetUserPasswordHandler(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    UserSessionService sessions,
    IAuditTrail audit)
    : IRequestHandler<ResetUserPasswordCommand>
{
    public async Task Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = new UserId(request.UserId);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(UserErrors.UserNotFound, "The user was not found.");

        user.ChangePasswordHash(passwordHasher.Hash(request.NewPassword));
        await sessions.RevokeAsync(token => token.UserId == userId, RefreshTokenRevocationReason.PasswordChanged, cancellationToken);
        audit.Record("users.password_reset", AuditEntityTypes.User, user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <param name="DepartmentId">Only users scoped to this department (or its whole branch).</param>
public sealed record LookupUsersQuery(string? Search = null, Guid? DepartmentId = null, string? Permission = null) : IRequest<IReadOnlyList<UserLookupResponse>>;

/// <summary>Active users for pickers (assignee, mentions). Any staff member may call it.</summary>
internal sealed class LookupUsersHandler(IApplicationDbContext db) : IRequestHandler<LookupUsersQuery, IReadOnlyList<UserLookupResponse>>
{
    public async Task<IReadOnlyList<UserLookupResponse>> Handle(LookupUsersQuery request, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpperInvariant();
#pragma warning disable CA1304, CA1311, CA1862 // Translated to SQL upper().
            query = query.Where(u => u.DisplayName.ToUpper().Contains(term) || u.Email.ToUpper().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (request.DepartmentId is { } departmentId)
        {
            var branchId = await db.Departments.Where(d => d.Id == departmentId).Select(d => (Guid?)d.BranchId).FirstOrDefaultAsync(cancellationToken);
            query = query.Where(u => u.Scopes.Any(s => s.DepartmentId == departmentId || (s.DepartmentId == null && s.BranchId == branchId)));
        }

        if (!string.IsNullOrWhiteSpace(request.Permission))
        {
            var permission = request.Permission;
            query = query.Where(u => db.Roles.Any(r => u.Roles.Any(m => m.RoleId == r.Id) && r.Permissions.Any(p => p.Permission == permission)));
        }

        var rows = await query.OrderBy(u => u.DisplayName).Take(50).Select(u => new { u.Id, u.DisplayName, u.Email }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new UserLookupResponse(r.Id.Value, r.DisplayName, r.Email))];
    }
}

internal sealed class UserAdministrationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users");

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new UpdateUserCommand(id, request.DisplayName), ct)))
            .RequireAuthorization(Permissions.UsersManage)
            .WithName("UpdateUser")
            .Produces<ApiResponse<UserResponse>>();

        group.MapPut("/{id:guid}/scopes", async (Guid id, SetUserScopesRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SetUserScopesCommand(id, request.Scopes ?? []), ct)))
            .RequireAuthorization(Permissions.UsersManage)
            .WithName("SetUserScopes")
            .Produces<ApiResponse<UserResponse>>();

        group.MapPost("/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ResetUserPasswordCommand(id, request.NewPassword), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.UsersManage)
            .WithName("ResetUserPassword")
            .Produces<ApiResponse<object?>>();

        group.MapGet("/lookup", async ([AsParameters] LookupUsersQuery query, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(query, ct)))
            .WithName("LookupUsers")
            .Produces<ApiResponse<IReadOnlyList<UserLookupResponse>>>();
    }
}
