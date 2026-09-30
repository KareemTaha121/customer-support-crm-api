using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Files;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Common.Files;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Organization;
using CustomerSupportCrm.Domain.Roles;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OrganizationEntity = CustomerSupportCrm.Domain.Organizations.Organization;

namespace CustomerSupportCrm.Application.Features.Organization;

internal static class OrganizationStore
{
    public const string NotConfigured = "ORGANIZATION_NOT_CONFIGURED";
    public const string PublicLogoPath = "/api/v1/public/branding/logo";

    /// <summary>The single organization of this deployment (created by database initialization).</summary>
    public static async Task<OrganizationEntity> GetAsync(IApplicationDbContext db, bool tracking, CancellationToken cancellationToken) =>
        await (tracking ? db.Organizations : db.Organizations.AsNoTracking()).FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(NotConfigured, "The organization has not been configured.");

    public static OrganizationResponse ToResponse(OrganizationEntity o) => new(
        o.Id,
        o.Name,
        o.SupportEmail,
        o.SupportPhone,
        o.DefaultCulture,
        o.TimeZone,
        o.PrimaryColor,
        o.AccentColor,
        o.LogoStorageKey is null ? null : $"{PublicLogoPath}?v={o.UpdatedAt?.ToUnixTimeSeconds() ?? 0}");
}

public sealed record GetOrganizationQuery : IRequest<OrganizationResponse>;

internal sealed class GetOrganizationHandler(IApplicationDbContext db) : IRequestHandler<GetOrganizationQuery, OrganizationResponse>
{
    public async Task<OrganizationResponse> Handle(GetOrganizationQuery request, CancellationToken cancellationToken) =>
        OrganizationStore.ToResponse(await OrganizationStore.GetAsync(db, tracking: false, cancellationToken));
}

public sealed record UpdateOrganizationCommand(string Name, string? SupportEmail, string? SupportPhone, string DefaultCulture, string TimeZone)
    : IRequest<OrganizationResponse>;

internal sealed class UpdateOrganizationValidator : AbstractValidator<UpdateOrganizationCommand>
{
    public UpdateOrganizationValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(OrganizationEntity.NameMaxLength);
        RuleFor(c => c.SupportEmail).EmailAddress().MaximumLength(OrganizationEntity.ContactMaxLength);
        RuleFor(c => c.SupportPhone).MaximumLength(32);
        RuleFor(c => c.DefaultCulture).NotEmpty().Must(c => OrganizationEntity.SupportedCultures.Contains(c)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.TimeZone).NotEmpty().MaximumLength(OrganizationEntity.TimeZoneMaxLength)
            .Must(tz => TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)).WithErrorCode(ErrorCodes.InvalidValue);
    }
}

internal sealed class UpdateOrganizationHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<UpdateOrganizationCommand, OrganizationResponse>
{
    public async Task<OrganizationResponse> Handle(UpdateOrganizationCommand request, CancellationToken cancellationToken)
    {
        var organization = await OrganizationStore.GetAsync(db, tracking: true, cancellationToken);
        var before = new { organization.Name, organization.SupportEmail, organization.SupportPhone, organization.DefaultCulture, organization.TimeZone };

        organization.UpdateProfile(request.Name, request.SupportEmail, request.SupportPhone, request.DefaultCulture, request.TimeZone);
        audit.Record("organization.updated", "Organization", organization.Id.ToString(), before, request);
        await db.SaveChangesAsync(cancellationToken);

        return OrganizationStore.ToResponse(organization);
    }
}

public sealed record UpdateBrandingCommand(string PrimaryColor, string AccentColor) : IRequest<OrganizationResponse>;

internal sealed class UpdateBrandingValidator : AbstractValidator<UpdateBrandingCommand>
{
    public UpdateBrandingValidator()
    {
        RuleFor(c => c.PrimaryColor).NotEmpty().Matches("^#[0-9A-Fa-f]{6}$");
        RuleFor(c => c.AccentColor).NotEmpty().Matches("^#[0-9A-Fa-f]{6}$");
    }
}

internal sealed class UpdateBrandingHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<UpdateBrandingCommand, OrganizationResponse>
{
    public async Task<OrganizationResponse> Handle(UpdateBrandingCommand request, CancellationToken cancellationToken)
    {
        var organization = await OrganizationStore.GetAsync(db, tracking: true, cancellationToken);
        var before = new { organization.PrimaryColor, organization.AccentColor };

        organization.UpdateBranding(request.PrimaryColor, request.AccentColor);
        audit.Record("organization.branding_updated", "Organization", organization.Id.ToString(), before, request);
        await db.SaveChangesAsync(cancellationToken);

        return OrganizationStore.ToResponse(organization);
    }
}

public sealed record UploadLogoCommand(string FileName, Stream Content, long Length) : IRequest<OrganizationResponse>;

internal sealed class UploadLogoHandler(IApplicationDbContext db, IFileStorage storage, IAuditTrail audit, TimeProvider time)
    : IRequestHandler<UploadLogoCommand, OrganizationResponse>
{
    public async Task<OrganizationResponse> Handle(UploadLogoCommand request, CancellationToken cancellationToken)
    {
        var upload = await FileUploadRules.ValidateAsync(request.FileName, request.Content, request.Length, FileUploadRules.MaxImageBytes, FileUploadRules.Images, cancellationToken);
        var organization = await OrganizationStore.GetAsync(db, tracking: true, cancellationToken);

        var key = FileUploadRules.CreateStorageKey("branding", upload.Extension, time.GetUtcNow());
        await storage.SaveAsync(key, request.Content, upload.ContentType, cancellationToken);

        var previous = organization.LogoStorageKey;
        organization.SetLogo(key, upload.ContentType);
        audit.Record("organization.logo_updated", "Organization", organization.Id.ToString(), newValues: new { upload.FileName, upload.Size });
        await db.SaveChangesAsync(cancellationToken);

        if (previous is not null)
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return OrganizationStore.ToResponse(organization);
    }
}

internal sealed class OrganizationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/organization").WithTags("Organization");

        group.MapGet("/", async (ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new GetOrganizationQuery(), ct)))
            .WithName("GetOrganization")
            .Produces<ApiResponse<OrganizationResponse>>();

        group.MapPut("/", async (UpdateOrganizationRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new UpdateOrganizationCommand(request.Name, request.SupportEmail, request.SupportPhone, request.DefaultCulture, request.TimeZone), ct)))
            .RequireAuthorization(Permissions.SettingsManage)
            .WithName("UpdateOrganization")
            .Produces<ApiResponse<OrganizationResponse>>();

        group.MapPut("/branding", async (UpdateBrandingRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new UpdateBrandingCommand(request.PrimaryColor, request.AccentColor), ct)))
            .RequireAuthorization(Permissions.SettingsManage)
            .WithName("UpdateBranding")
            .Produces<ApiResponse<OrganizationResponse>>();

        group.MapPost("/logo", async (IFormFile file, ISender sender, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return ApiResults.Ok(await sender.Send(new UploadLogoCommand(file.FileName, stream, file.Length), ct));
            })
            .RequireAuthorization(Permissions.SettingsManage)
            .DisableAntiforgery()
            .WithName("UploadLogo")
            .Produces<ApiResponse<OrganizationResponse>>();
    }
}

/// <summary>Anonymous branding for the login page and customer portal.</summary>
internal sealed class PublicBrandingEndpoints : IPublicEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/branding", async (IApplicationDbContext db, CancellationToken ct) =>
            {
                var organization = OrganizationStore.ToResponse(await OrganizationStore.GetAsync(db, tracking: false, ct));
                return ApiResults.Ok(new PublicBrandingResponse(organization.Name, organization.DefaultCulture, organization.PrimaryColor, organization.AccentColor, organization.LogoUrl));
            })
            .WithName("GetPublicBranding")
            .Produces<ApiResponse<PublicBrandingResponse>>();

        app.MapGet("/branding/logo", async (IApplicationDbContext db, IFileStorage storage, HttpContext http, CancellationToken ct) =>
            {
                var organization = await OrganizationStore.GetAsync(db, tracking: false, ct);
                var stream = organization.LogoStorageKey is null ? null : await storage.OpenReadAsync(organization.LogoStorageKey, ct);
                if (stream is null)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.Stream(stream, organization.LogoContentType ?? "application/octet-stream");
            })
            .WithName("GetPublicLogo");
    }
}
