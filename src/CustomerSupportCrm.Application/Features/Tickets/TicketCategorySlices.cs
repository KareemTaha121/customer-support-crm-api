using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Tickets;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Tickets;

public sealed record ListTicketCategoriesQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<TicketCategoryResponse>>;

internal sealed class ListTicketCategoriesHandler(IApplicationDbContext db) : IRequestHandler<ListTicketCategoriesQuery, IReadOnlyList<TicketCategoryResponse>>
{
    public async Task<IReadOnlyList<TicketCategoryResponse>> Handle(ListTicketCategoriesQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.TicketCategories.AsNoTracking()
            .Where(c => request.IncludeInactive || c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(TicketCategoryMapping.ToResponse)];
    }
}

internal static class TicketCategoryMapping
{
    public static TicketCategoryResponse ToResponse(TicketCategory c) =>
        new(c.Id, c.Name, c.NameAr, c.ParentId, c.DefaultDepartmentId, c.DefaultPriority?.ToString(), c.IsActive, c.SortOrder);
}

public sealed record SaveTicketCategoryCommand(Guid? CategoryId, TicketCategoryRequest Category) : IRequest<TicketCategoryResponse>;

internal sealed class SaveTicketCategoryValidator : AbstractValidator<SaveTicketCategoryCommand>
{
    public SaveTicketCategoryValidator()
    {
        RuleFor(c => c.Category).NotNull();
        RuleFor(c => c.Category.Name).NotEmpty().MaximumLength(TicketCategory.NameMaxLength);
        RuleFor(c => c.Category.NameAr).MaximumLength(TicketCategory.NameMaxLength);
        RuleFor(c => c.Category.DefaultPriority).Must(p => p is null || Enum.TryParse<TicketPriority>(p, out _)).WithErrorCode(ErrorCodes.InvalidValue);
    }
}

internal sealed class SaveTicketCategoryHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<SaveTicketCategoryCommand, TicketCategoryResponse>
{
    public async Task<TicketCategoryResponse> Handle(SaveTicketCategoryCommand request, CancellationToken cancellationToken)
    {
        var input = request.Category;
        if (input.ParentId is { } parentId && !await db.TicketCategories.AnyAsync(c => c.Id == parentId, cancellationToken))
        {
            throw new NotFoundException(TicketErrors.CategoryNotFound, "The parent category was not found.");
        }

        var priority = input.DefaultPriority is null ? (TicketPriority?)null : Enum.Parse<TicketPriority>(input.DefaultPriority);
        TicketCategory category;
        if (request.CategoryId is { } id)
        {
            category = await db.TicketCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new NotFoundException(TicketErrors.CategoryNotFound, "The category was not found.");
            if (input.DefaultDepartmentId is { } department && department != category.DefaultDepartmentId)
            {
                await OrganizationUnits.EnsureDepartmentActiveAsync(db, department, cancellationToken);
            }

            category.Update(input.Name, input.NameAr, input.ParentId, input.DefaultDepartmentId, priority, input.SortOrder);
        }
        else
        {
            if (input.DefaultDepartmentId is { } department)
            {
                await OrganizationUnits.EnsureDepartmentActiveAsync(db, department, cancellationToken);
            }

            category = TicketCategory.Create(input.Name, input.NameAr, input.ParentId, input.DefaultDepartmentId, priority, input.SortOrder);
            db.TicketCategories.Add(category);
        }

        category.SetActive(input.IsActive);
        audit.Record(request.CategoryId is null ? "ticket_categories.created" : "ticket_categories.updated", "TicketCategory", category.Id.ToString(), newValues: input);
        await db.SaveChangesAsync(cancellationToken);
        return TicketCategoryMapping.ToResponse(category);
    }
}

internal sealed class TicketCategoryEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ticket-categories").WithTags("Tickets");

        group.MapGet("/", async (bool? includeInactive, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new ListTicketCategoriesQuery(includeInactive ?? false), ct)))
            .WithName("ListTicketCategories")
            .Produces<ApiResponse<IReadOnlyList<TicketCategoryResponse>>>();

        group.MapPost("/", async (TicketCategoryRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Created("/api/v1/ticket-categories", await sender.Send(new SaveTicketCategoryCommand(null, request), ct)))
            .RequireAuthorization(Permissions.TicketCategoriesManage)
            .WithName("CreateTicketCategory")
            .Produces<ApiResponse<TicketCategoryResponse>>(StatusCodes.Status201Created);

        group.MapPut("/{id:guid}", async (Guid id, TicketCategoryRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveTicketCategoryCommand(id, request), ct)))
            .RequireAuthorization(Permissions.TicketCategoriesManage)
            .WithName("UpdateTicketCategory")
            .Produces<ApiResponse<TicketCategoryResponse>>();
    }
}
