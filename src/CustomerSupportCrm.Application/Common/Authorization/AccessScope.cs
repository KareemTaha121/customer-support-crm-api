using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Common.Authorization;

/// <summary>
/// The branches and departments whose data the current user may access. A branch id grants
/// every department in it; a department id grants only that department.
/// </summary>
public sealed record AccessScope(bool AllBranches, IReadOnlyList<Guid> BranchIds, IReadOnlyList<Guid> DepartmentIds)
{
    public static AccessScope Everything { get; } = new(true, [], []);

    public bool CanAccess(Guid branchId, Guid? departmentId) =>
        AllBranches
        || BranchIds.Contains(branchId)
        || (departmentId is { } department && DepartmentIds.Contains(department));

    public bool CanAccess(IScopedEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return CanAccess(entity.BranchId, entity.DepartmentId);
    }
}

public interface IAccessScopeProvider
{
    Task<AccessScope> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Resolves the scope once per request from the user's permissions and scope rows.</summary>
internal sealed class AccessScopeProvider(IApplicationDbContext db, ICurrentUser currentUser) : IAccessScopeProvider
{
    private AccessScope? _cached;

    public async Task<AccessScope> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        if (!currentUser.IsAuthenticated)
        {
            return _cached = new AccessScope(false, [], []);
        }

        if (currentUser.HasPermission(Permissions.DataAllBranches))
        {
            return _cached = AccessScope.Everything;
        }

        var userId = currentUser.UserId;
        var scopes = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Scopes)
            .Select(s => new { s.BranchId, s.DepartmentId })
            .ToListAsync(cancellationToken);

        return _cached = new AccessScope(
            false,
            [.. scopes.Where(s => s.DepartmentId == null).Select(s => s.BranchId).Distinct()],
            [.. scopes.Where(s => s.DepartmentId != null).Select(s => s.DepartmentId!.Value).Distinct()]);
    }
}

public static class AccessScopeExtensions
{
    /// <summary>Filters branch/department-owned rows to the caller's scope, in the database.</summary>
    public static IQueryable<T> WhereInScope<T>(this IQueryable<T> query, AccessScope scope)
        where T : class, IScopedEntity
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.AllBranches)
        {
            return query;
        }

        var branchIds = scope.BranchIds.ToList();
        var departmentIds = scope.DepartmentIds.ToList();
        return query.Where(e => branchIds.Contains(e.BranchId) || (e.DepartmentId != null && departmentIds.Contains(e.DepartmentId.Value)));
    }

    /// <summary>
    /// Throws 404 (not 403) for out-of-scope records so their existence is not disclosed.
    /// </summary>
    public static void EnsureAccess(this AccessScope scope, IScopedEntity entity, string notFoundCode, string notFoundMessage)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!scope.CanAccess(entity))
        {
            throw new NotFoundException(notFoundCode, notFoundMessage);
        }
    }

    /// <summary>For writes that place data in a branch/department: the caller must own that unit.</summary>
    public static void EnsureCanAssign(this AccessScope scope, Guid branchId, Guid? departmentId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!scope.CanAccess(branchId, departmentId))
        {
            throw new ForbiddenException(OrganizationErrors.OutOfScope, "You do not have access to this branch or department.");
        }
    }
}

public static class OrganizationErrors
{
    public const string OutOfScope = "OUT_OF_SCOPE";
    public const string BranchNotFound = "BRANCH_NOT_FOUND";
    public const string DepartmentNotFound = "DEPARTMENT_NOT_FOUND";
    public const string BranchCodeTaken = "BRANCH_CODE_TAKEN";
    public const string DepartmentCodeTaken = "DEPARTMENT_CODE_TAKEN";
    public const string DepartmentNotInBranch = "DEPARTMENT_NOT_IN_BRANCH";
    public const string InactiveUnit = "ORGANIZATION_UNIT_INACTIVE";
}
