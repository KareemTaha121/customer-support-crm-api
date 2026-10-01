using FluentValidation;
using FluentValidation.Results;

namespace CustomerSupportCrm.Application.Common.Validation;

/// <summary>Parent checks shared by ticket and knowledge-base categories (small tables, walked in memory).</summary>
internal static class CategoryHierarchy
{
    public const string CycleCode = "CATEGORY_CYCLE";

    /// <summary>
    /// True when <paramref name="parentId"/> is the category itself or one of its descendants, i.e. the
    /// category would become its own ancestor. <paramref name="parents"/> maps every category id to its parent.
    /// A loop already present in the data also counts, so it is never extended.
    /// </summary>
    public static bool CreatesCycle(Guid categoryId, Guid parentId, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        ArgumentNullException.ThrowIfNull(parents);
        var visited = new HashSet<Guid>();
        for (Guid? current = parentId; current is { } id; current = parents.GetValueOrDefault(id))
        {
            if (id == categoryId || !visited.Add(id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>400 field error on <c>parentId</c>, so the category form shows it next to the parent picker.</summary>
    public static ValidationException CycleError() =>
        new([new ValidationFailure("ParentId", "A category cannot be moved under itself or one of its subcategories.") { ErrorCode = CycleCode }]);
}
