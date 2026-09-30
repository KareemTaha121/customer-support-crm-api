using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Roles;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Api.Tests;

internal sealed record Widget(string Id, string Name);

internal sealed record CreateWidgetCommand(string? Name, int Quantity) : IRequest<Widget>;

internal sealed class CreateWidgetValidator : AbstractValidator<CreateWidgetCommand>
{
    public CreateWidgetValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
        RuleFor(command => command.Quantity).InclusiveBetween(1, 100);
    }
}

internal sealed class CreateWidgetHandler : IRequestHandler<CreateWidgetCommand, Widget>
{
    public Task<Widget> Handle(CreateWidgetCommand request, CancellationToken cancellationToken) =>
        Task.FromResult(new Widget("w-42", request.Name!));
}

/// <summary>Endpoints that exercise each response path of the platform.</summary>
internal sealed class TestEndpoints : IEndpoint
{
    public const string SecretDetail = "connection string with password=hunter2";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/_test").AllowAnonymous();

        group.MapGet("/ok", () => ApiResults.Ok(new Widget("w-1", "Widget")));

        group.MapGet("/paged", () => ApiResults.Paged<Widget>(
            [new Widget("w-2", "Second")],
            PaginationMeta.Create(page: 2, pageSize: 1, totalCount: 3)));

        group.MapPost("/widgets", async (CreateWidgetCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var widget = await sender.Send(command, cancellationToken);
            return ApiResults.Created($"/api/v1/_test/widgets/{widget.Id}", widget);
        });

        group.MapGet("/not-found", () =>
        {
            throw new NotFoundException("WIDGET_NOT_FOUND", "Widget was not found.");
        });

        group.MapGet("/domain-error", () =>
        {
            throw new DomainException("WIDGET_LOCKED", "Widget is locked.");
        });

        group.MapGet("/unhandled", () =>
        {
            throw new InvalidOperationException(SecretDetail);
        });

        // Separate group: AllowAnonymous on a parent would override these requirements.
        var secure = app.MapGroup("/_test-secure");
        secure.MapGet("/authenticated", () => ApiResults.Ok("hello"));
        secure.MapGet("/users-manage", () => ApiResults.Ok("managed")).RequireAuthorization(Permissions.UsersManage);
    }
}
