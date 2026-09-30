# API Contract

The machine-readable contract is the OpenAPI document at `/openapi/v1.json` (Development: Swagger UI at `/swagger`). This page defines the conventions every endpoint follows.

## Base path and versioning

All business endpoints live under `/api/v1`. A new version is introduced only for a breaking change. Health endpoints (`/health/live`, `/health/ready`) are unversioned and return plain text.

## Response envelope

Every `/api/v1` response, success or failure, uses the same envelope (`Contracts/Common/ApiResponse.cs`):

```json
{
  "success": true,
  "data": {},
  "message": null,
  "errors": [],
  "meta": null,
  "correlationId": "4f1c0c8e2b6d4a0e9f3a7b1c2d3e4f5a"
}
```

| Field | Notes |
|---|---|
| `success` | `true` for 2xx, `false` otherwise. |
| `data` | Payload; `null` on failure. |
| `message` | Optional on success. On failure: localized summary of the error category. |
| `errors` | Always an array; empty on success. |
| `meta` | Pagination metadata for list endpoints, otherwise `null`. |
| `correlationId` | Same value as the `X-Correlation-Id` response header. |

### Errors

```json
{
  "success": false,
  "data": null,
  "message": "The requested resource was not found.",
  "errors": [
    { "code": "TICKET_NOT_FOUND", "message": "Ticket was not found.", "field": null }
  ],
  "meta": null,
  "correlationId": "..."
}
```

- `code` is stable and language-neutral. Clients branch on `code`, never on `message` or on HTTP status alone.
- `message` is localized from `Accept-Language` (`en`, `ar`; default `en`). The response carries `Content-Language`.
- `field` is the camelCase request path (`name`, `items[0].quantity`) for validation errors, otherwise `null`.
- Stack traces and exception details are never returned.

### Status codes and categories

| HTTP | Category code | Raised by |
|---|---|---|
| 400 | `VALIDATION_ERROR` | FluentValidation failure (one entry per field error) |
| 400 | `BAD_REQUEST` | Malformed body or unbindable parameters |
| 401 | `UNAUTHORIZED` | Missing/invalid access token, `UnauthorizedException` |
| 403 | `FORBIDDEN` | `ForbiddenException`, authorization failure |
| 404 | `NOT_FOUND` | `NotFoundException`, unknown route |
| 405 | `METHOD_NOT_ALLOWED` | Wrong HTTP method |
| 409 | `CONFLICT` | `ConflictException`; concurrent edit (`xmin`) or unique-index race |
| 413 | `PAYLOAD_TOO_LARGE` | Body exceeds limit |
| 415 | `UNSUPPORTED_MEDIA_TYPE` | Wrong content type |
| 422 | `BUSINESS_RULE_VIOLATION` | `DomainException` (invariant or state transition) |
| 429 | `RATE_LIMITED` | Rate limiter |
| 500 | `INTERNAL_ERROR` | Anything unhandled |

For `NotFoundException`, `ConflictException`, `ForbiddenException` and `DomainException`, `errors[0].code` is the feature-specific code the exception carries (e.g. `TICKET_NOT_FOUND`); `message` at the top level is the localized category text.

### Field-level validation codes

| Code | FluentValidation rule |
|---|---|
| `REQUIRED` | `NotEmpty`, `NotNull` |
| `INVALID_LENGTH` | `Length`, `MinimumLength`, `MaximumLength` |
| `INVALID_FORMAT` | `Matches` |
| `INVALID_EMAIL` | `EmailAddress` |
| `OUT_OF_RANGE` | `GreaterThan`, `LessThan`, `InclusiveBetween`, … |
| `INVALID_VALUE` | `IsInEnum`, `IsEnumName` |
| `INVALID` | Any other rule |

A validator can set a custom stable code with `.WithErrorCode("TICKET_TITLE_TOO_LONG")`; UPPER_SNAKE_CASE codes pass through unchanged.

### Feature error codes (Phase 2)

| Code | HTTP | Meaning |
|---|---|---|
| `INVALID_CREDENTIALS` | 401 | Unknown email or wrong password (deliberately indistinguishable) |
| `ACCOUNT_LOCKED` | 401 | Too many failed sign-ins; retry later |
| `ACCOUNT_DISABLED` | 401 | The account is disabled |
| `INVALID_REFRESH_TOKEN` | 401 | Missing, expired, revoked or reused refresh token: sign in again |
| `CSRF_VALIDATION_FAILED` | 403 | `X-CSRF-Protection` header missing on refresh/logout |
| `INVALID_CURRENT_PASSWORD` | 400 | Field error on `currentPassword` |
| `EMAIL_TAKEN` | 409 | Another user has this email |
| `UNKNOWN_ROLE` | 400 | Field error on `roleIds` |
| `USER_NOT_FOUND` / `ROLE_NOT_FOUND` | 404 | |
| `CANNOT_DISABLE_SELF` | 409 | |
| `LAST_ADMINISTRATOR` | 409 | The change would leave no active administrator |
| `ROLE_NAME_TAKEN` | 409 | Case-insensitive name clash |
| `ROLE_IN_USE` | 409 | Unassign users before deleting |
| `ROLE_IS_SYSTEM` | 422 | System roles are immutable |
| `UNKNOWN_PERMISSION` | 400 | Field error on `permissions` |

## Pagination

List endpoints accept `page` (1-based), `pageSize`, `search`, `sortBy`, `sortDirection` (`asc`/`desc`) and feature-specific filters, and return:

```json
{
  "success": true,
  "data": [],
  "meta": { "page": 1, "pageSize": 25, "totalCount": 250, "totalPages": 10 },
  "...": "..."
}
```

## Correlation ID

Clients may send `X-Correlation-Id` (1–64 chars of `A-Z a-z 0-9 . _ -`). A well-formed value is reused; anything else is replaced by a generated 32-char hex ID. The ID is returned in the `X-Correlation-Id` header and the `correlationId` body field, and is attached to every log event for the request.

## Serialization

JSON, camelCase properties, enums as strings, `null` values included.
