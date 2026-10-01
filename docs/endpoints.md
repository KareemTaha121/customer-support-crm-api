# Endpoint catalog

Every business endpoint, grouped by route group and feature. Conventions (envelope, errors, paging, correlation id) are in [api-contract.md](api-contract.md); request/response shapes are in the OpenAPI document (`/openapi/v1.json`, Swagger UI at `/swagger` in Development) and in `src/CustomerSupportCrm.Contracts`.

Route groups (`src/CustomerSupportCrm.Api/Endpoints/EndpointExtensions.cs`):

| Prefix | Callers | Authentication |
|--------|---------|----------------|
| `/api/v1` | Staff web app | Staff JWT (`Authorization: Bearer`), permission policies named by code |
| `/api/v1/portal` | Customer portal | Customer JWT (8 h, not refreshable); every request checks the account is still active (401 `ACCOUNT_DISABLED` after access is revoked) |
| `/api/v1/public` | Anonymous visitors | None (rate limited; some endpoints behind feature toggles) |
| `/api/v1/external` | Integrations | API key (`X-Api-Key`) with scopes |
| `/hubs/staff`, `/hubs/chat` | SignalR | Staff JWT via `access_token` query / per-conversation token |

"Permission" is the code checked in addition to the group's authentication; "—" means any caller of the group.

## Staff — `/api/v1`

### Authentication (`Features/Authentication`)

| Method | Route | Permission | Notes |
|--------|-------|-----------|-------|
| POST | `/auth/login` | anonymous | Sets the `crm_refresh` HttpOnly cookie |
| POST | `/auth/refresh` | anonymous + `X-CSRF-Protection` | Rotates the refresh cookie |
| POST | `/auth/logout` | anonymous + `X-CSRF-Protection` | Revokes the session |
| GET | `/auth/me` | — | Current user, roles, permissions |
| POST | `/auth/change-password` | — | Revokes other sessions |

### Users and roles (`Features/Users`, `Features/Roles`)

| Method | Route | Permission |
|--------|-------|-----------|
| GET | `/users` | `users.manage` |
| GET | `/users/{id}` | `users.manage` |
| POST | `/users` | `users.manage` |
| PUT | `/users/{id}` | `users.manage` |
| PUT | `/users/{id}/roles` | `users.manage` |
| PUT | `/users/{id}/scopes` | `users.manage` |
| POST | `/users/{id}/reset-password` | `users.manage` |
| POST | `/users/{id}/enable`, `/users/{id}/disable` | `users.manage` |
| GET | `/users/lookup` | — (assignee pickers) |
| GET | `/roles`, `/roles/{id}`, `/permissions` | `roles.manage` or `users.manage` (`RolesRead` policy) |
| POST | `/roles` | `roles.manage` |
| PUT | `/roles/{id}` | `roles.manage` |
| DELETE | `/roles/{id}` | `roles.manage` |

### Organization, branches, departments, settings (`Features/Organization`, `Branches`, `Departments`, `Settings`)

| Method | Route | Permission |
|--------|-------|-----------|
| GET | `/organization` | — |
| PUT | `/organization`, `/organization/branding` | `settings.manage` |
| POST | `/organization/logo` | `settings.manage` (multipart `file`) |
| GET | `/branches` | — |
| POST | `/branches` | `organization.manage` |
| PUT | `/branches/{id}` | `organization.manage` |
| POST | `/branches/{id}/activate`, `/branches/{id}/deactivate` | `organization.manage` |
| POST | `/branches/{branchId}/departments` | `organization.manage` |
| PUT | `/branches/{branchId}/departments/{id}` | `organization.manage` |
| POST | `/departments/{id}/activate`, `/departments/{id}/deactivate` | `organization.manage` |
| GET | `/settings` | — |
| PUT | `/settings` | `settings.manage` |

### Audit (`Features/AuditLogs`)

| Method | Route | Permission |
|--------|-------|-----------|
| GET | `/audit-logs` | `audit.view` |
| GET | `/audit-logs/export.csv` | `audit.export` |

### Customers (`Features/Customers`, `Features/CustomerPortal`)

| Method | Route | Permission |
|--------|-------|-----------|
| GET | `/customers`, `/customers/duplicates`, `/customers/{id}` | `customers.view` |
| POST | `/customers` | `customers.create` |
| PUT | `/customers/{id}` | `customers.update` |
| DELETE | `/customers/{id}` | `customers.delete` (soft delete) |
| POST | `/customers/{id}/contacts` | `customers.update` |
| PUT, DELETE | `/customers/{id}/contacts/{contactId}` | `customers.update` |
| POST | `/customers/{id}/contacts/{contactId}/primary` | `customers.update` |
| GET | `/customers/{id}/notes` | `customers.view` |
| POST | `/customers/{id}/notes` | `customers.notes_manage` |
| PUT, DELETE | `/customers/{id}/notes/{noteId}` | `customers.notes_manage` |
| GET | `/customers/{id}/attachments`, `/customers/{id}/attachments/{attachmentId}` | `customers.view` |
| POST | `/customers/{id}/attachments` | `customers.attachments_manage` |
| DELETE | `/customers/{id}/attachments/{attachmentId}` | `customers.attachments_manage` |
| GET | `/customers/{id}/history` | `customers.view` |
| POST, DELETE | `/customers/{id}/portal-access` | `customers.update` |

### Tickets (`Features/Tickets`)

| Method | Route | Permission |
|--------|-------|-----------|
| GET | `/tickets`, `/tickets/{id}`, `/tickets/{id}/messages`, `/tickets/{id}/history` | `tickets.view` |
| POST | `/tickets` | `tickets.create` |
| PUT | `/tickets/{id}` | `tickets.update` |
| POST | `/tickets/{id}/assign` | `tickets.update` |
| POST | `/tickets/{id}/transfer` | `tickets.assign` |
| POST | `/tickets/{id}/status` | `tickets.update` |
| POST | `/tickets/{id}/escalate` | `tickets.escalate` |
| DELETE | `/tickets/{id}` | `tickets.delete` |
| POST | `/tickets/{id}/messages` | `tickets.update` (public reply or internal note) |
| GET | `/tickets/{id}/attachments`, `/tickets/{id}/attachments/{attachmentId}` | `tickets.view` |
| POST | `/tickets/{id}/attachments` | `tickets.update` |
| DELETE | `/tickets/{id}/attachments/{attachmentId}` | `tickets.update` |
| GET | `/ticket-categories` | — |
| POST | `/ticket-categories` | `tickets.categories_manage` |
| PUT | `/ticket-categories/{id}` | `tickets.categories_manage` |

### Agent workspace (`Features/Dashboard`, `Features/Notifications`)

| Method | Route | Permission |
|--------|-------|-----------|
| GET | `/dashboard/agent` | `tickets.view` |
| GET, POST | `/tasks` | — (own tasks; `ticketId`/`customerId` must be in scope, else 404; with `tickets.assign` they list every assignee) |
| PUT, DELETE | `/tasks/{id}` | — |
| POST | `/tasks/{id}/complete`, `/tasks/{id}/reopen` | — |
| GET, POST | `/quick-replies` | — (shared replies need `quickreplies.manage`) |
| PUT, DELETE | `/quick-replies/{id}` | — |
| POST | `/quick-replies/{id}/render` | — |
| GET | `/notifications`, `/notifications/unread-count` | — |
| POST | `/notifications/{id}/read`, `/notifications/read-all` | — |

### SLA and automation (`Features/Sla`)

| Method | Route | Permission |
|--------|-------|-----------|
| GET | `/sla-policies` | — |
| POST | `/sla-policies` | `sla.manage` |
| PUT, DELETE | `/sla-policies/{id}` | `sla.manage` |
| GET, POST | `/automation/assignment-rules` | `automation.manage` |
| PUT, DELETE | `/automation/assignment-rules/{id}` | `automation.manage` |
| GET, POST | `/automation/escalation-rules` | `automation.manage` |
| PUT, DELETE | `/automation/escalation-rules/{id}` | `automation.manage` |

### Knowledge base (`Features/KnowledgeBase`)

| Method | Route | Permission |
|--------|-------|-----------|
| GET | `/kb/categories`, `/kb/articles`, `/kb/articles/{id}` | `kb.view` |
| POST | `/kb/categories`, `/kb/articles` | `kb.manage` |
| PUT, DELETE | `/kb/categories/{id}`, `/kb/articles/{id}` | `kb.manage` |
| POST | `/kb/articles/{id}/publish`, `/kb/articles/{id}/unpublish` | `kb.publish` |
| POST | `/kb/articles/{id}/archive` | `kb.manage` |
| GET | `/kb/suggestions` | `tickets.view` |

### Channels and live chat (`Features/Channels`)

| Method | Route | Permission |
|--------|-------|-----------|
| GET | `/channels/status`, `/channels/outbox` | `channels.manage` |
| POST | `/channels/test`, `/channels/outbox/{id}/retry` | `channels.manage` |
| GET | `/chat/conversations` | `chat.handle` |
| GET | `/chat/conversations/{id}/messages` | `chat.handle` (public transcript of the linked ticket, staff scope) |
| POST | `/chat/conversations/{id}/accept`, `/messages`, `/close` | `chat.handle` |

### AI (`Features/Ai`) — all require `ai.use`; ticket actions also need the `ai.agent_assist_enabled` setting

| Method | Route |
|--------|-------|
| GET | `/ai/status` |
| POST | `/ai/tickets/{id}/summary`, `/reply`, `/categorize`, `/solutions` |
| POST | `/ai/suggestions/{id}/feedback` |

### Reports (`Features/Reports`) — `reports.view`; CSV exports need `reports.export`

| Method | Route |
|--------|-------|
| GET | `/reports/ticket-volume`, `/sla-performance`, `/agent-performance`, `/customer-satisfaction`, `/management-dashboard` |
| GET | `/reports/export/ticket-volume.csv`, `/sla-performance.csv`, `/agent-performance.csv`, `/customer-satisfaction.csv` |

### Integrations (`Features/Integrations`) — `integrations.manage`

| Method | Route |
|--------|-------|
| GET | `/integrations/catalog`, `/integrations/api-keys`, `/integrations/webhooks`, `/integrations/webhooks/{id}/deliveries` |
| POST | `/integrations/api-keys` (secret returned once), `/integrations/api-keys/{id}/revoke` |
| POST | `/integrations/webhooks`, `/integrations/webhooks/{id}/rotate-secret`, `/integrations/webhooks/{id}/test`, `/integrations/deliveries/{id}/retry` |
| PUT, DELETE | `/integrations/webhooks/{id}` |

## Customer portal — `/api/v1/portal`

| Method | Route | Notes |
|--------|-------|-------|
| GET, PUT | `/me` | Profile (name, language) |
| POST | `/me/change-password` | |
| GET | `/categories` | Ticket categories for the create form |
| GET | `/history` | Customer activity timeline |
| GET, POST | `/tickets` | Own tickets |
| GET | `/tickets/{id}`, `/tickets/{id}/messages` | No internal notes |
| POST | `/tickets/{id}/messages`, `/tickets/{id}/attachments` | |
| GET | `/tickets/{id}/attachments/{attachmentId}` | Download |
| POST | `/tickets/{id}/feedback`, `/tickets/{id}/close` | CSAT 1–5 |

## Public — `/api/v1/public`

| Method | Route | Toggle |
|--------|-------|--------|
| GET | `/branding`, `/branding/logo`, `/features` | — |
| POST | `/portal/register` | `portal.registration_enabled` |
| POST | `/portal/verify`, `/portal/resend-verification`, `/portal/login` | — |
| GET | `/kb/categories`, `/kb/articles`, `/kb/articles/{slug}` | — |
| POST | `/kb/articles/{id}/feedback` | — |
| POST | `/chat/conversations` | `chat.enabled` |
| GET | `/chat/conversations/{id}` | per-conversation token |
| POST | `/chat/conversations/{id}/messages`, `/close` | per-conversation token |
| GET | `/web-forms/categories` | — (active ticket categories for the contact form) |
| POST | `/web-forms/tickets` | `webform.enabled` |
| POST | `/chatbot/messages` | `chatbot.enabled` |
| GET, POST | `/channels/{channel}/webhook` | Provider webhooks (email, SMS, WhatsApp), signature-checked |

## External API — `/api/v1/external` (API key scopes)

| Method | Route | Scope |
|--------|-------|-------|
| GET | `/customers` | customers read |
| PUT | `/customers/{system}/{externalId}` | customers write (upsert by external id) |
| POST | `/tickets` | tickets write |
| GET | `/tickets/{number}` | tickets read |
| POST | `/tickets/{number}/messages` | tickets write |

## Realtime

| Hub | Events (server → client) | Client methods |
|-----|--------------------------|----------------|
| `/hubs/staff` | `notificationCreated`, `ticketUpdated`, `chatMessage`, `chatUpdated` | `JoinConversation(id)` (needs `chat.handle` and the ticket in scope), `LeaveConversation(id)` |
| `/hubs/chat` | `chatMessage`, `chatUpdated` | `JoinConversation(id, accessToken)` |
