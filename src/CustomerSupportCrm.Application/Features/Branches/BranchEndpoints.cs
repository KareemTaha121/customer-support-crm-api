using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Organization;
using CustomerSupportCrm.Domain.Organizations;
using CustomerSupportCrm.Domain.Roles;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Branches;

internal static class BranchQueries
{
    public static async Task<IReadOnlyList<BranchResponse>> ListAsync(IApplicationDbContext db, bool includeInactive, Guid? onlyBranchId, CancellationToken cancellationToken)
    {
        var branches = await db.Branches.AsNoTracking()
            .Where(b => includeInactive || b.IsActive)
            .Where(b => onlyBranchId == null || b.Id == onlyBranchId)
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var branchIds = branches.Select(b => b.Id).ToList();
        var departments = await db.Departments.AsNoTracking()
            .Where(d => branchIds.Contains(d.BranchId) && (includeInactive || d.IsActive))
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        return
        [
            .. branches.Select(b => new BranchResponse(
                b.Id,
                b.Code,
                b.Name,
                b.Address,
                b.Phone,
                b.IsActive,
                [.. departments.Where(d => d.BranchId == b.Id).Select(ToResponse)])),
        ];
    }

    public static DepartmentResponse ToResponse(Department d) => new(d.Id, d.BranchId, d.Code, d.Name, d.Email, d.IsActive);

    public static async Task<BranchResponse> GetAsync(IApplicationDbContext db, Guid branchId, CancellationToken cancellationToken) =>
        (await ListAsync(db, includeInactive: true, branchId, cancellationToken)).SingleOrDefault()
        ?? throw new NotFoundException(OrganizationErrors.BranchNotFound, "The branch was not found.");
}

/// <param name="IncludeInactive">Include deactivated branches and departments (administration screens).</param>
public sealed record ListBranchesQuery(bool IncludeInactive = false) : IRequest<IReadOnlyList<BranchResponse>>;

internal sealed class ListBranchesHandler(IApplicationDbContext db) : IRequestHandler<ListBranchesQuery, IReadOnlyList<BranchResponse>>
{
    public Task<IReadOnlyList<BranchResponse>> Handle(ListBranchesQuery request, CancellationToken cancellationToken) =>
        BranchQueries.ListAsync(db, request.IncludeInactive, onlyBranchId: null, cancellationToken);
}

public sealed record SaveBranchCommand(Guid? BranchId, string Code, string Name, string? Address, string? Phone) : IRequest<BranchResponse>;

internal sealed class SaveBranchValidator : AbstractValidator<SaveBranchCommand>
{
    public SaveBranchValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(Branch.CodeMaxLength).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Branch.NameMaxLength);
        RuleFor(c => c.Address).MaximumLength(500);
        RuleFor(c => c.Phone).MaximumLength(32);
    }
}

internal sealed class SaveBranchHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<SaveBranchCommand, BranchResponse>
{
    public async Task<BranchResponse> Handle(SaveBranchCommand request, CancellationToken cancellationToken)
    {
        var code = Branch.NormalizeCode(request.Code);
        if (await db.Branches.AnyAsync(b => b.Code == code && b.Id != request.BranchId, cancellationToken))
        {
            throw new ConflictException(OrganizationErrors.BranchCodeTaken, "A branch with this code already exists.");
        }

        Branch branch;
        if (request.BranchId is { } id)
        {
            branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
                ?? throw new NotFoundException(OrganizationErrors.BranchNotFound, "The branch was not found.");
            var before = new { branch.Code, branch.Name, branch.Address, branch.Phone };
            branch.Update(request.Code, request.Name, request.Address, request.Phone);
            audit.Record("branches.updated", "Branch", branch.Id.ToString(), before, new { branch.Code, branch.Name, branch.Address, branch.Phone });
        }
        else
        {
            branch = Branch.Create(request.Code, request.Name, request.Address, request.Phone);
            db.Branches.Add(branch);
            audit.Record("branches.created", "Branch", branch.Id.ToString(), newValues: new { branch.Code, branch.Name });
        }

        await db.SaveChangesAsync(cancellationToken);
        return await BranchQueries.GetAsync(db, branch.Id, cancellationToken);
    }
}

public sealed record SetBranchActiveCommand(Guid BranchId, bool Active) : IRequest<BranchResponse>;

internal sealed class SetBranchActiveHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<SetBranchActiveCommand, BranchResponse>
{
    public async Task<BranchResponse> Handle(SetBranchActiveCommand request, CancellationToken cancellationToken)
    {
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == request.BranchId, cancellationToken)
            ?? throw new NotFoundException(OrganizationErrors.BranchNotFound, "The branch was not found.");

        if (branch.IsActive != request.Active)
        {
            if (request.Active)
            {
                branch.Activate();
            }
            else
            {
                branch.Deactivate();
            }

            audit.Record(request.Active ? "branches.activated" : "branches.deactivated", "Branch", branch.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
        }

        return await BranchQueries.GetAsync(db, branch.Id, cancellationToken);
    }
}

internal sealed class BranchEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/branches").WithTags("Organization");

        // Any staff member may read the structure (pickers, filters).
        group.MapGet("/", async (bool? includeInactive, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new ListBranchesQuery(includeInactive ?? false), ct)))
            .WithName("ListBranches")
            .Produces<ApiResponse<IReadOnlyList<BranchResponse>>>();

        group.MapPost("/", async (BranchRequest request, ISender sender, CancellationToken ct) =>
            {
                var branch = await sender.Send(new SaveBranchCommand(null, request.Code, request.Name, request.Address, request.Phone), ct);
                return ApiResults.Created($"/api/v1/branches/{branch.Id}", branch);
            })
            .RequireAuthorization(Permissions.OrganizationManage)
            .WithName("CreateBranch")
            .Produces<ApiResponse<BranchResponse>>(StatusCodes.Status201Created);

        group.MapPut("/{id:guid}", async (Guid id, BranchRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveBranchCommand(id, request.Code, request.Name, request.Address, request.Phone), ct)))
            .RequireAuthorization(Permissions.OrganizationManage)
            .WithName("UpdateBranch")
            .Produces<ApiResponse<BranchResponse>>();

        group.MapPost("/{id:guid}/activate", async (Guid id, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SetBranchActiveCommand(id, true), ct)))
            .RequireAuthorization(Permissions.OrganizationManage)
            .WithName("ActivateBranch")
            .Produces<ApiResponse<BranchResponse>>();

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SetBranchActiveCommand(id, false), ct)))
            .RequireAuthorization(Permissions.OrganizationManage)
            .WithName("DeactivateBranch")
            .Produces<ApiResponse<BranchResponse>>();
    }
}
