using CustomerSupportCrm.Contracts.Roles;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Roles.List;

/// <summary>All roles, unpaged: the set is small and curated by administrators.</summary>
public sealed record ListRolesQuery : IRequest<IReadOnlyList<RoleResponse>>;
