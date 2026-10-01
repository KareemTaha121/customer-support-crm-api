using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Users;
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

        return _cached = await LoadAsync(db, currentUser.UserId, currentUser.HasPermission(Permissions.DataAllBranches), cancellationToken);
    }

    /// <summary>
    /// Builds a user's scope outside an HTTP request (e.g. a SignalR hub, which must not rely on
    /// <c>IHttpContextAccessor</c>). <paramref name="allBranches"/> is the <c>data.all_branches</c> permission.
    /// </summary>
    internal static async Task<AccessScope> LoadAsync(IApplicationDbContext db, UserId userId, bool allBranches, CancellationToken cancellationToken)
    {
        if (allBranches)
        {
            return AccessScope.Everything;
        }

        var scopes = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Scopes)
            .Select(s => new { s.BranchId, s.DepartmentId })
            .ToListAsync(cancellationToken);

        return new AccessScope(
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

/// <summary>
/// Checks for writes that place data in a branch/department: the unit must exist (404) and be
/// active (422 <see cref="OrganizationErrors.InactiveUnit"/>). Callers check only units that change,
/// so records already in a deactivated unit stay editable.
/// </summary>
internal static class OrganizationUnits
{
    /// <summary>Branch must exist and be active; department (if any) must belong to it and be active.</summary>
    public static async Task EnsureValidAsync(IApplicationDbContext db, Guid branchId, Guid? departmentId, CancellationToken cancellationToken)
    {
        var branchActive = await db.Branches.Where(b => b.Id == branchId).Select(b => (bool?)b.IsActive).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(OrganizationErrors.BranchNotFound, "The branch was not found.");

        var departmentActive = true;
        if (departmentId is { } id)
        {
            departmentActive = await db.Departments.Where(d => d.Id == id && d.BranchId == branchId).Select(d => (bool?)d.IsActive).SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException(OrganizationErrors.DepartmentNotFound, "The department was not found in this branch.");
        }

        if (!branchActive || !departmentActive)
        {
            throw Inactive();
        }
    }

    /// <summary>For department-only references (category default, SLA policy, assignment rule target).</summary>
    public static async Task EnsureDepartmentActiveAsync(IApplicationDbContext db, Guid departmentId, CancellationToken cancellationToken)
    {
        var department = await db.Departments
            .Where(d => d.Id == departmentId)
            .Select(d => new { d.IsActive, BranchActive = db.Branches.Any(b => b.Id == d.BranchId && b.IsActive) })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(OrganizationErrors.DepartmentNotFound, "The department was not found.");

        if (!department.IsActive || !department.BranchActive)
        {
            throw Inactive();
        }
    }

    public static UnprocessableException Inactive() =>
        new(OrganizationErrors.InactiveUnit, "The branch or department is inactive.");
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
