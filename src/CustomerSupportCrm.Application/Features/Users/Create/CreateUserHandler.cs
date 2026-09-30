using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Users.Common;
using CustomerSupportCrm.Application.Resources;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CustomerSupportCrm.Application.Features.Users.Create;

internal sealed class CreateUserHandler(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    IAuditTrail audit,
    IStringLocalizer<Messages> localizer,
    TimeProvider time)
    : IRequestHandler<CreateUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email.Value, cancellationToken))
        {
            throw new ConflictException(UserErrors.EmailTaken, "A user with this email already exists.");
        }

        var roleIds = await UserQueries.ResolveRoleIdsAsync(db, request.RoleIds, localizer, cancellationToken);
        var user = User.Create(email, request.DisplayName, passwordHasher.Hash(request.Password), roleIds);
        if (request.Scopes is { Count: > 0 } scopes)
        {
            user.SetScopes(await UserQueries.ResolveScopesAsync(db, scopes, cancellationToken));
        }

        db.Users.Add(user);
        audit.Record(
            AuditActions.UserCreated,
            AuditEntityTypes.User,
            user.Id.ToString(),
            newValues: new { user.Email, user.DisplayName, roleIds = roleIds.Select(id => id.Value) });
        await db.SaveChangesAsync(cancellationToken);

        return await UserQueries.GetResponseAsync(db, user.Id, time.GetUtcNow(), cancellationToken);
    }
}
