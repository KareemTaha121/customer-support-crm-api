# customer-support-crm-api

Backend for the Customer Support CRM.

**Stack:** .NET 10 / ASP.NET Core · PostgreSQL · EF Core · MediatR · FluentValidation
**Architecture:** Vertical Slice + DDD + Clean Architecture boundaries + CQRS

## Layout

| Path | Purpose |
|---|---|
| `src/CustomerSupportCrm.Domain` | Entities, aggregates, value objects, domain events. No infrastructure dependencies. |
| `src/CustomerSupportCrm.Application` | Feature slices (`Features/<Feature>/<UseCase>`), pipeline behaviors, abstractions. |
| `src/CustomerSupportCrm.Infrastructure` | EF Core/PostgreSQL, identity, providers (email/SMS/WhatsApp/AI/files), background jobs. |
| `src/CustomerSupportCrm.Contracts` | Public API request/response contracts. |
| `src/CustomerSupportCrm.Api` | Host: middleware, auth wiring, OpenAPI, health checks. |
| `tests/` | Domain, Application, Integration (real PostgreSQL), and API tests. |
| `docs/` | Architecture, API contract, security, development guides, ADRs. |
| `deploy/` | Dockerfile and Kubernetes manifests. |

Dependency direction: `Api → Application → Domain`, `Infrastructure → Application/Domain`, `Api → Infrastructure`.

## Getting started

```bash
docker compose up -d        # PostgreSQL
dotnet build
dotnet test
dotnet run --project src/CustomerSupportCrm.Api
```

## Conventions

- Branches: `main`, `develop`, `feature/<ticket-id>-<short-name>`, `bugfix/...`, `hotfix/...`
- Commits: Conventional Commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`)
