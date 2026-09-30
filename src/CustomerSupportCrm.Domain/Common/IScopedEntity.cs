namespace CustomerSupportCrm.Domain.Common;

/// <summary>
/// Business data owned by a branch and, optionally, one of its departments. Access is
/// filtered by the caller's branch/department scope.
/// </summary>
public interface IScopedEntity
{
    Guid BranchId { get; }

    Guid? DepartmentId { get; }
}
