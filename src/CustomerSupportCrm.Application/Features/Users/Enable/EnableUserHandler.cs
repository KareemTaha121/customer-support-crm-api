using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Users.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Users.Enable;

internal sealed class EnableUserHandler(IApplicationDbContext db, IAuditTrail audit, TimeProvider time)
    : IRequestHandler<EnableUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(EnableUserCommand request, CancellationToken cancellationToken)
    {
        var userId = new UserId(request.UserId);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(UserErrors.UserNotFound, "The user was not found.");

        var wasActive = user.IsActive;
        var wasLockedOut = user.IsLockedOut(time.GetUtcNow());
        if (!wasActive || wasLockedOut)
        {
            user.Enable();
            audit.Record(AuditActions.UserEnabled, AuditEntityTypes.User, user.Id.ToString(), oldValues: new { wasActive, wasLockedOut });
            await db.SaveChangesAsync(cancellationToken);
        }

        return await UserQueries.GetResponseAsync(db, user.Id, time.GetUtcNow(), cancellationToken);
    }
}
