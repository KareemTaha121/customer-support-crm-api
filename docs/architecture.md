# Architecture

Vertical Slice Architecture inside Clean Architecture dependency boundaries, with DDD and CQRS. The full blueprint is `customer-support-crm-implementation-plan.md`; this page records how it is realized in code.

## Projects and dependencies

```text
Api ──────────► Application ──► Domain
 │                   │
 │                   └────────► Contracts
 └──► Infrastructure ─► Application, Domain
```

| Project | Owns | May reference |
|---|---|---|
| Domain | Entities, aggregates, value objects, domain events, `DomainException` | nothing |
| Contracts | Public request/response types, `ApiResponse<T>`, `ErrorCodes` | nothing |
| Application | Feature slices, MediatR behaviors, abstractions, exceptions, localized messages, feature endpoints | Domain, Contracts, ASP.NET Core |
| Infrastructure | EF Core/PostgreSQL, providers, background jobs, options | Application, Domain |
| Api | Host: middleware pipeline, logging, localization, OpenAPI, health | all |

Application references the ASP.NET Core shared framework so each slice can own its thin HTTP endpoint (ADR 0001). Domain stays free of every framework.

## Request pipeline

```text
CorrelationIdMiddleware        read/generate X-Correlation-Id, push to log context
RequestLocalization            en/ar from query, cookie, Accept-Language
Serilog request logging        one event per request
ExceptionHandler               GlobalExceptionHandler → standard error envelope
StatusCodePages                envelope for empty-body 4xx/5xx (unknown route, 405, 401)
Endpoint                       IEndpoint → ISender.Send(command/query)
  MediatR ValidationBehavior   FluentValidation → ValidationException
  Handler                      orchestrates; domain enforces invariants
```

Correlation and culture are async-local, so their middleware must wrap logging and exception handling.

## Feature slices

```text
Application/Features/Tickets/Create/
  CreateTicketCommand.cs     IRequest<CreateTicketResponse>
  CreateTicketValidator.cs   AbstractValidator<CreateTicketCommand>
  CreateTicketHandler.cs     IRequestHandler<...>
  CreateTicketEndpoint.cs    IEndpoint; maps POST /tickets, returns ApiResults.Created(...)
  CreateTicketResponse.cs
```

`IEndpoint` implementations, handlers and validators are discovered from the Application assembly automatically. Endpoints are mapped under `/api/v1`.

Handlers throw `NotFoundException` / `ConflictException` / `ForbiddenException` (Application) or entities throw `DomainException` (Domain); they never catch to build responses.

## Persistence

Single `ApplicationDbContext` (Infrastructure/Persistence) on PostgreSQL via Npgsql, with snake_case naming (`EFCore.NamingConventions`). Entity configurations are `IEntityTypeConfiguration<T>` classes in `Persistence/Configurations`, applied automatically. No generic repository or unit of work.

## Configuration

Options classes bound from configuration sections and validated at startup (`ValidateOnStart`). Precedence: `appsettings.json` → `appsettings.{Environment}.json` → user secrets (Development) → environment variables.

| Section | Options | Notes |
|---|---|---|
| `Database` | `DatabaseOptions` | `ConnectionString` is required; empty in `appsettings.json` so production must supply it. |
| `Serilog` | Serilog | Levels/properties from config; console sink is text in Development, compact JSON elsewhere. |

## Decisions log

- ADR 0001 — Vertical slices; endpoints live in Application.
- ADR 0003 — API response contract.
- MediatR pinned to 12.5.0 (last Apache-2.0 release). 13+ requires a commercial license key.
- `TimeProvider` (BCL) will be used for time instead of a custom clock abstraction.
- Resources: `Messages.resx` is the neutral (English) resource; `Messages.ar.resx` holds Arabic.
