using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Branches;
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

namespace CustomerSupportCrm.Application.Features.Departments;

public sealed record SaveDepartmentCommand(Guid? DepartmentId, Guid BranchId, string Code, string Name, string? Email) : IRequest<DepartmentResponse>;

internal sealed class SaveDepartmentValidator : AbstractValidator<SaveDepartmentCommand>
{
    public SaveDepartmentValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(Department.CodeMaxLength).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Department.NameMaxLength);
        RuleFor(c => c.Email).EmailAddress().MaximumLength(254);
    }
}

internal sealed class SaveDepartmentHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<SaveDepartmentCommand, DepartmentResponse>
{
    public async Task<DepartmentResponse> Handle(SaveDepartmentCommand request, CancellationToken cancellationToken)
    {
        if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId, cancellationToken))
        {
            throw new NotFoundException(OrganizationErrors.BranchNotFound, "The branch was not found.");
        }

        var code = Branch.NormalizeCode(request.Code);
        if (await db.Departments.AnyAsync(d => d.BranchId == request.BranchId && d.Code == code && d.Id != request.DepartmentId, cancellationToken))
        {
            throw new ConflictException(OrganizationErrors.DepartmentCodeTaken, "A department with this code already exists in the branch.");
        }

        Department department;
        if (request.DepartmentId is { } id)
        {
            department = await db.Departments.SingleOrDefaultAsync(d => d.Id == id && d.BranchId == request.BranchId, cancellationToken)
                ?? throw new NotFoundException(OrganizationErrors.DepartmentNotFound, "The department was not found.");
            var before = new { department.Code, department.Name, department.Email };
            department.Update(request.Code, request.Name, request.Email);
            audit.Record("departments.updated", "Department", department.Id.ToString(), before, new { department.Code, department.Name, department.Email });
        }
        else
        {
            department = Department.Create(request.BranchId, request.Code, request.Name, request.Email);
            db.Departments.Add(department);
            audit.Record("departments.created", "Department", department.Id.ToString(), newValues: new { department.BranchId, department.Code, department.Name });
        }

        await db.SaveChangesAsync(cancellationToken);
        return BranchQueries.ToResponse(department);
    }
}

public sealed record SetDepartmentActiveCommand(Guid DepartmentId, bool Active) : IRequest<DepartmentResponse>;

internal sealed class SetDepartmentActiveHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<SetDepartmentActiveCommand, DepartmentResponse>
{
    public async Task<DepartmentResponse> Handle(SetDepartmentActiveCommand request, CancellationToken cancellationToken)
    {
        var department = await db.Departments.SingleOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken)
            ?? throw new NotFoundException(OrganizationErrors.DepartmentNotFound, "The department was not found.");

        if (department.IsActive != request.Active)
        {
            if (request.Active)
            {
                department.Activate();
            }
            else
            {
                department.Deactivate();
            }

            audit.Record(request.Active ? "departments.activated" : "departments.deactivated", "Department", department.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
        }

        return BranchQueries.ToResponse(department);
    }
}

internal sealed class DepartmentEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/branches/{branchId:guid}/departments", async (Guid branchId, DepartmentRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Created($"/api/v1/branches/{branchId}", await sender.Send(new SaveDepartmentCommand(null, branchId, request.Code, request.Name, request.Email), ct)))
            .RequireAuthorization(Permissions.OrganizationManage)
            .WithTags("Organization")
            .WithName("CreateDepartment")
            .Produces<ApiResponse<DepartmentResponse>>(StatusCodes.Status201Created);

        app.MapPut("/branches/{branchId:guid}/departments/{id:guid}", async (Guid branchId, Guid id, DepartmentRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveDepartmentCommand(id, branchId, request.Code, request.Name, request.Email), ct)))
            .RequireAuthorization(Permissions.OrganizationManage)
            .WithTags("Organization")
            .WithName("UpdateDepartment")
            .Produces<ApiResponse<DepartmentResponse>>();

        app.MapPost("/departments/{id:guid}/activate", async (Guid id, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SetDepartmentActiveCommand(id, true), ct)))
            .RequireAuthorization(Permissions.OrganizationManage)
            .WithTags("Organization")
            .WithName("ActivateDepartment")
            .Produces<ApiResponse<DepartmentResponse>>();

        app.MapPost("/departments/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SetDepartmentActiveCommand(id, false), ct)))
            .RequireAuthorization(Permissions.OrganizationManage)
            .WithTags("Organization")
            .WithName("DeactivateDepartment")
            .Produces<ApiResponse<DepartmentResponse>>();
    }
}
