# 0002 — Authentication strategy

- Status: Accepted
- Date: 2026-09-30

## Context

Staff sign in to an Angular SPA served from a different origin than the API. Tokens must resist XSS theft, sessions must be revocable, and the domain must own users, roles and permissions rather than an external identity schema.

## Decision

- **Own the user model.** `User`, `Role` and `RefreshToken` are domain entities. From ASP.NET Core Identity only the `PasswordHasher` is used; Identity's user store and tables are not.
- **Short-lived JWT access tokens** (15 min, HS256) carrying roles and permissions, held in memory by the SPA and sent as a bearer header.
- **Rotating opaque refresh tokens** in an `HttpOnly; Secure; SameSite=Strict` cookie scoped to `/api/v1/auth`, stored hashed, grouped into sessions with reuse detection.
- **CSRF**: custom header required on the cookie-authenticated endpoints (refresh, logout) plus strict CORS.
- **Permissions in the token**, re-read from the database at every refresh.
- **Permission catalog in code** (`Domain/Roles/Permissions.cs`); roles store permission codes (`role_permissions`). There is no `permissions` table: codes are referenced by endpoint policies at compile time.

## Consequences

- No token ever reaches `localStorage`; an XSS payload can use the in-memory access token only while the page is open.
- Permission and disable changes take effect within one access-token lifetime without per-request database lookups.
- A single signing key is shared by issuer and validator (the API). Moving to asymmetric keys (RS256/ES256) is required before a second service validates tokens.
- An external identity provider (SSO) can be added later as another way to start a session; the session/refresh model stays the same.
