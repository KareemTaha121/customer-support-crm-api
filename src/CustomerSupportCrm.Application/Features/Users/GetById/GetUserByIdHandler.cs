using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Features.Users.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Users;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Users.GetById;

internal sealed class GetUserByIdHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<GetUserByIdQuery, UserResponse>
{
    public Task<UserResponse> Handle(GetUserByIdQuery request, CancellationToken cancellationToken) =>
        UserQueries.GetResponseAsync(db, new UserId(request.UserId), time.GetUtcNow(), cancellationToken);
}
