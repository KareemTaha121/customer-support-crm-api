# 0001 — Vertical slice architecture with endpoints in Application

- Status: Accepted
- Date: 2026-09-30

## Context

The plan requires organizing code by business capability (vertical slices) inside Clean Architecture dependency boundaries, and prefers "endpoint definitions close to their feature slice". A classic Api project with controllers would split every use case across two projects.

## Decision

- Each use case is a folder under `Application/Features/<Feature>/<UseCase>` containing its command/query, validator, handler, response and endpoint.
- Endpoints implement `IEndpoint` and are thin: bind input, `ISender.Send`, return `ApiResults.*`. No business rules.
- `CustomerSupportCrm.Application` references the ASP.NET Core shared framework to make this possible.
- The Api project hosts the pipeline and discovers endpoints; it contains no feature code.
- Domain references nothing.

## Consequences

- A feature is added or removed by touching one folder.
- Application is not HTTP-agnostic. If a second host (e.g. a worker) needs the same handlers, it can reference Application and ignore the endpoints; handlers themselves must not use `HttpContext`.
- The Application layer reads the current user through `ICurrentUser` (Phase 2), never `HttpContext.User`.
