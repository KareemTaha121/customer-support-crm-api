# 0003 — Uniform API response envelope

- Status: Accepted
- Date: 2026-09-30

## Context

The Angular client needs one predictable shape for success, errors, validation failures and pagination, with stable machine-readable codes independent of the UI language.

## Decision

- All `/api/v1` responses use `ApiResponse<T>` (`success`, `data`, `message`, `errors[]`, `meta`, `correlationId`). See `docs/api-contract.md`.
- Success bodies are produced only through `ApiResults.Ok/Created/Paged`.
- Error bodies are produced only by `ErrorResponseWriter`, fed by `GlobalExceptionHandler` (exceptions) and `StatusCodePages` (empty-body framework responses). Handlers do not catch exceptions to build responses.
- Error `code` values are stable UPPER_SNAKE_CASE strings; `message` is localized via `IStringLocalizer<Messages>` keyed by code.
- RFC 7807 ProblemDetails is not used, to keep one shape for the client.

## Consequences

- Clients branch on `errors[].code`.
- Health endpoints are exempt and return plain `Healthy`/`Unhealthy` for orchestrators.
- Changing an existing error code is a breaking change.
