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
StatusCodePages                envelope for empty-body 4xx/5xx (unknown route, 405, 401, 403, 413, 429)
Hsts / HttpsRedirection        HSTS outside Development
SecureHeadersMiddleware        nosniff, X-Frame-Options DENY, Referrer-Policy, CSP (not on Swagger UI)
Cors                           configured frontend origins only, with credentials
Authentication                 JWT bearer (see docs/security.md)
Authorization                  /api/v1 requires a user; per-endpoint permission policies
RateLimiter                    global per-user/IP limit (health excluded) + per-endpoint policies
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

Handlers throw `NotFoundException` / `ConflictException` / `ForbiddenException` / `UnauthorizedException` (Application) or entities throw `DomainException` (Domain); they never catch to build responses.

When several slices of one feature share logic, it lives in that feature's `Common/` folder (e.g. `Authentication/Common/UserSessionService`, `Users/Common/UserQueries`), never in a global service. Tiny slices without orchestration (the static permission catalog) map an endpoint without a MediatR request.

### Current features

| Feature | Slices |
|---|---|
| Authentication | Login, Refresh, Logout, GetCurrentUser, ChangePassword |
| Users | Create, List, GetById, SetRoles, Disable, Enable |
| Roles | List, GetById, Create, Update, Delete, ListPermissions |
| AuditLogs | List |

### Application abstractions

| Abstraction | Implementation (Infrastructure) |
|---|---|
| `IApplicationDbContext` | `ApplicationDbContext` (handlers use EF Core directly) |
| `ICurrentUser` | `HttpCurrentUser`: JWT claims of the current request |
| `IRequestContext` | `HttpRequestContext`: correlation id, IP, user agent |
| `IPasswordHasher` | `IdentityPasswordHasher` |
| `ITokenService` | `TokenService`: JWT creation, refresh-token generation and hashing |
| `IAuditTrail` | `AuditTrail`: adds `AuditLog` rows to the current unit of work |
| `TimeProvider` (BCL) | `TimeProvider.System` |

## Persistence

Single `ApplicationDbContext` (Infrastructure/Persistence) on PostgreSQL via Npgsql, with snake_case naming (`EFCore.NamingConventions`). Entity configurations are `IEntityTypeConfiguration<T>` classes in `Persistence/Configurations`, applied automatically. No generic repository or unit of work.

- Strongly typed ids (`UserId`, `RoleId`) are stored as `uuid` through value converters registered in `ConfigureConventions`. New ids are UUIDv7 (time-ordered, index-friendly).
- Aggregates that can be edited concurrently use PostgreSQL `xmin` as the optimistic concurrency token (`HasXminConcurrencyToken`). A lost race surfaces as `409 CONFLICT`.
- `AuditableEntityInterceptor` stamps `created_at/by` and `updated_at/by` on `IAuditableEntity`, including when only child rows changed.
- Migrations live in `Persistence/Migrations` and are treated as generated code by analyzers.
- `DatabaseInitializer` applies migrations and seeds reference data: the Administrator system role (resynced with the permission catalog every run), Manager/Agent defaults on first run, and the first administrator from `Bootstrap:*` when no users exist.

## Configuration

Options classes bound from configuration sections and validated at startup (`ValidateOnStart`). Precedence: `appsettings.json` → `appsettings.{Environment}.json` → user secrets (Development) → environment variables.

| Section | Options | Notes |
|---|---|---|
| `Database` | `DatabaseOptions` | `ConnectionString` is required; empty in `appsettings.json` so production must supply it. |
| `Database` | `DatabaseOptions.InitializeOnStartup` | Migrate + seed at startup. `true` only in Development; elsewhere run `--init-database` as a deployment step. |
| `Jwt` | `JwtOptions` | Issuer, audience, signing key (required, at least 32 chars), token lifetimes. |
| `Cors` | `CorsSettings` | Allowed frontend origins. Required outside Development (startup fails when empty). |
| `RateLimiting:Authentication` | `RateLimitOptions` | Permits per window for sign-in endpoints. |
| `RateLimiting:Global` | `GlobalRateLimitOptions` | Permits per window per signed-in user (fallback IP) for every request except `/health`. Default 300/min. |
| `RequestLimits` | `RequestLimitOptions` | `MaxRequestBodyBytes` for Kestrel (default 25 MB, above the 20 MB attachment limit). Larger bodies get 413 `PAYLOAD_TOO_LARGE`. |
| `Bootstrap` | `BootstrapOptions` | First administrator (only used while the users table is empty). |
| `Serilog` | Serilog | Levels/properties from config; console sink is text in Development, compact JSON elsewhere. |

## Decisions log

- ADR 0001 — Vertical slices; endpoints live in Application.
- ADR 0002 — Authentication: own user model, JWT + rotating refresh cookie, permissions in code.
- ADR 0003 — API response contract.
- MediatR pinned to 12.5.0 (last Apache-2.0 release). 13+ requires a commercial license key.
- `TimeProvider` (BCL) is used for time instead of a custom clock abstraction.
- Domain events and their dispatch arrive with the first feature that reacts to them (tickets). Phase 2 audits explicitly through `IAuditTrail` inside the same transaction.
- The permission catalog endpoint lives in the Roles feature: a `Features.Permissions` namespace would shadow the domain `Permissions` class in sibling features.
- Resources: `Messages.resx` is the neutral (English) resource; `Messages.ar.resx` holds Arabic.
