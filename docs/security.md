# Security

The API is the security boundary. The Angular client hides what a user cannot do, but every rule below is enforced server-side.

## Authentication

| Token | Lifetime | Where it lives | Stored server-side |
|---|---|---|---|
| Access token (JWT, HS256) | 15 min (`Jwt:AccessTokenLifetimeMinutes`) | Client memory; sent as `Authorization: Bearer` | No |
| Refresh token (256-bit random) | 14 days (`Jwt:RefreshTokenLifetimeDays`) | `crm_refresh` cookie: `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth` | SHA-256 hash only |

### Endpoints

| Endpoint | Auth | Notes |
|---|---|---|
| `POST /api/v1/auth/login` | anonymous, rate-limited | Returns access token + profile; sets refresh cookie. |
| `POST /api/v1/auth/refresh` | refresh cookie + `X-CSRF-Protection` header, rate-limited | Rotates the refresh token; returns a new access token with current roles/permissions. |
| `POST /api/v1/auth/logout` | refresh cookie + `X-CSRF-Protection` header | Revokes the session; clears the cookie. Idempotent. |
| `GET /api/v1/auth/me` | bearer | Profile with roles and permissions read from the database. |
| `POST /api/v1/auth/change-password` | bearer, rate-limited | Requires the current password; revokes every other session. |

### Access token claims

`sub` (user id), `email`, `name`, `sid` (session id), `jti`, `role` (repeated), `permission` (repeated). Inbound claim mapping is disabled, so these names are used verbatim.

### Refresh-token rotation and reuse detection

Every sign-in starts a session (`refresh_tokens.session_id`). Each refresh consumes the presented token and issues its successor in the same session. Presenting a token that was already used or revoked is treated as theft: the whole session is revoked and the request fails with `INVALID_REFRESH_TOKEN`. Concurrent refreshes of one token are serialized by optimistic concurrency (`xmin`); the loser gets `INVALID_REFRESH_TOKEN`, so clients must run a single refresh at a time.

### Passwords

- Hashed with ASP.NET Core Identity's `PasswordHasher` (PBKDF2-HMAC-SHA512, per-password salt, versioned). Hashes are upgraded transparently at sign-in when the format changes.
- Policy: 12–128 characters, no composition rules (NIST SP 800-63B).
- Never logged, never audited, never returned.

### Sign-in protection

- Unknown email and wrong password return the same `INVALID_CREDENTIALS` error, and unknown emails still run a hash verification to equalize timing.
- 5 consecutive failures lock the account for 15 minutes (`ACCOUNT_LOCKED`). An administrator can clear a lockout with `POST /users/{id}/enable`.
- Disabled accounts cannot sign in (`ACCOUNT_DISABLED`) and their refresh tokens are revoked immediately.
- Login, refresh and change-password share a fixed-window limit per client IP (`RateLimiting:Authentication`, default 10/min) → `429 RATE_LIMITED` with `Retry-After`.

### Revocation latency

Access tokens are not revoked individually. After a user is disabled or loses a permission, an already-issued access token remains valid until it expires (≤ 15 minutes); refresh picks up the change immediately.

## CSRF

Bearer-authenticated endpoints are not exposed to CSRF (browsers never attach the header automatically). The two cookie-authenticated endpoints, refresh and logout, additionally require the `X-CSRF-Protection` header: a cross-site form cannot set custom headers, and a cross-origin script needs a CORS preflight that only allowed origins pass. `SameSite=Strict` is a second layer.

## CORS

Only origins listed in `Cors:AllowedOrigins` are allowed, with credentials. The list is empty by default; wildcard origins are never used, and outside Development (and the `Test` host) startup fails when the list is empty. Exposed headers: `X-Correlation-Id`, `Content-Language`, `Retry-After`.

## Response hardening

- `SecureHeadersMiddleware` adds `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` and `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` to every response. The Swagger UI (Development only) is served before it and keeps working.
- HSTS is enabled outside Development. Kestrel does not send a `Server` header.
- Request bodies are capped by `RequestLimits:MaxRequestBodyBytes` (default 25 MB); larger requests get 413 `PAYLOAD_TOO_LARGE`.
- A global rate limit (`RateLimiting:Global`, default 300 requests/minute per user, or per IP when anonymous) applies to everything except `/health`, on top of the stricter per-endpoint policies.
- `GET /api/v1/auth/me` returns 401 `ACCOUNT_DISABLED` as soon as the account is disabled, even with a still-valid access token.

## Authorization

- Every `/api/v1` endpoint requires an authenticated user unless it explicitly opts out (`AllowAnonymous`: login, refresh, logout).
- Each permission code (`Domain/Roles/Permissions.cs`) is also a policy name: `.RequireAuthorization(Permissions.UsersManage)`.
- Composite policies live in `PolicyNames` (e.g. `policy:roles.read` = `roles.manage` or `users.manage`).
- A user's permissions are the union of their roles' permissions. The `Administrator` system role always holds every permission (resynchronized at each database initialization) and cannot be edited or deleted.
- The last active administrator cannot be disabled or lose the Administrator role (`LAST_ADMINISTRATOR`); users cannot disable themselves.
- Resource-level rules (branch/department scope) arrive with Phase 3.

| Permission | Grants |
|---|---|
| `users.manage` | User administration; reading roles and the permission catalog |
| `roles.manage` | Role administration |
| `audit.view` | Reading the audit log |
| `tickets.*`, `customers.*`, `reports.view`, `settings.manage` | Reserved for their feature phases |

## Audit log

`audit_logs` is append-only and written in the same transaction as the change it describes. Each entry records actor, action, entity type/id, JSON old/new values, time, correlation id, IP and user agent. Actions: see `Domain/Audit/AuditActions.cs`. Read with `GET /api/v1/audit-logs` (`audit.view`), filterable by action, entity, actor and time range. Values never contain passwords, hashes or tokens.

## Secrets and configuration

| Setting | Development | Other environments |
|---|---|---|
| `Jwt:SigningKey` (≥ 32 chars) | dev-only value in `appsettings.Development.json` | **Required**; secret store / environment variable `Jwt__SigningKey`. Startup fails if missing. |
| `Database:ConnectionString` | docker-compose database | **Required**; secret store. |
| `Bootstrap:AdminEmail` / `AdminPassword` | dev-only values | Set once for the first `--init-database`, then remove. Change the password after first sign-in. |
| `Cors:AllowedOrigins` | `http(s)://localhost:4200` | The deployed frontend origin(s). |

## Deployment notes

- Run `dotnet CustomerSupportCrm.Api.dll --init-database` as a release step to apply migrations and seed; the API does not migrate on startup outside Development.
- Behind a reverse proxy, configure forwarded headers so rate limiting and audit IPs see the client address, not the proxy's.
- Expired and revoked refresh tokens accumulate; a cleanup job arrives with background processing.
