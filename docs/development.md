# Development

## Prerequisites

- .NET SDK 10.0.302+ (pinned in `global.json`)
- Docker (local PostgreSQL and integration tests)

## Run locally

```bash
docker compose up -d
dotnet tool restore
dotnet run --project src/CustomerSupportCrm.Api --launch-profile https
```

In Development the API migrates and seeds the database at startup, creating the administrator from `Bootstrap:AdminEmail` / `Bootstrap:AdminPassword` in `appsettings.Development.json`. Sign in through `POST /api/v1/auth/login`, then use the Swagger UI **Authorize** button with the returned `accessToken`. Refresh and logout need the refresh cookie and the `X-CSRF-Protection` header, so exercise them from the web app or the tests rather than Swagger.

- Swagger UI: https://localhost:5001/swagger
- OpenAPI: https://localhost:5001/openapi/v1.json
- Health: `/health/live`, `/health/ready`

`appsettings.Development.json` points at the docker-compose database. To use another database without editing tracked files:

```bash
dotnet user-secrets --project src/CustomerSupportCrm.Api set "Database:ConnectionString" "Host=...;Database=...;Username=...;Password=..."
```

## Tests

```bash
dotnet test
```

| Project | Needs |
|---|---|
| Domain.Tests, Application.Tests | nothing |
| Api.Tests | nothing (in-memory host, unreachable DB on purpose) |
| IntegrationTests | a running Docker daemon (Testcontainers starts `postgres:17-alpine`), **or** `CRM_TEST_POSTGRES` set to a connection string for an existing server; a throwaway `crm_it_<guid>` database is created and dropped |

```bash
CRM_TEST_POSTGRES="Host=localhost;Username=postgres;Password=..." dotnet test tests/CustomerSupportCrm.IntegrationTests
```

## Migrations

```bash
dotnet ef migrations add <MeaningfulName> --project src/CustomerSupportCrm.Infrastructure --startup-project src/CustomerSupportCrm.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/CustomerSupportCrm.Infrastructure --startup-project src/CustomerSupportCrm.Api
```

Outside Development, apply migrations and seed data with the built API:

```bash
dotnet CustomerSupportCrm.Api.dll --init-database
```

## Code quality

- Warnings are errors (`Directory.Build.props`), analyzers at `latest-recommended`.
- `dotnet format --verify-no-changes` runs in CI.
- Line endings are LF (`.editorconfig`, `.gitattributes`).
- Package versions are central (`Directory.Packages.props`).

## Adding a user-facing message

Add the key (a stable code) to both `Application/Resources/Messages.resx` and `Messages.ar.resx`, then resolve it with `IStringLocalizer<Messages>`.
