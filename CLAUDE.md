# CLAUDE.md

Multi-tenant email campaign platform. Each customer is an **organization** of one of three business models: a company,
a marketing agency that serves several clients, or an individual creator. An organization has workspaces (one per client
in an agency); each workspace has contacts, templates and campaigns. Only the **Email** channel is built; Facebook,
Instagram, SMS, WhatsApp and YouTube exist as empty stubs so they can be added later as their own projects.

**Naming:** in the tables, code, routes and the token an organization is still called an **agency** (`agencies`,
`agency_id`, `app.agency_id`, `/v1/auth/switch-agency`). `agency` is also one of the three business models, so read
"agency" as the tenant unless it says business model. Anything a person sees says "organization".

## Stack

| Layer | Choice |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, C# 14, nullable on (nullable warnings are errors) |
| Database | PostgreSQL 16 only. No other database, **no EF Core** |
| Data access | Dapper over Npgsql, plain SQL in repository classes |
| Frontend | `web-react/`: React 19, TypeScript (strict), Vite, React Router, plain CSS. No CSS framework, no jQuery. The older vanilla JavaScript app in `web/` stays until `web-react/` has everything it has (the Site admin's menu and permission editors are not ported yet) |
| Auth | One HS256 JWT per sign-in, **30 days** (`Jwt:AccessTokenDays`), kept in an **HttpOnly cookie** that scripts cannot read. **No refresh token**: when it runs out, or the Site admin changes a permission, the person signs in again (see Sessions without refresh tokens) |
| Email sending | `IEmailProvider`: fake provider for development, Amazon SES for production |
| Tests | xUnit, Testcontainers.PostgreSql, Microsoft.AspNetCore.Mvc.Testing, NSubstitute; frontend: Vitest + React Testing Library (`web-react/`), `node --test` (the older `web/`) |

Allowed packages: Dapper, Npgsql, Microsoft.AspNetCore.Authentication.JwtBearer, Swashbuckle.AspNetCore, CsvHelper,
AWSSDK.SimpleEmailV2, and the test packages above (plus Microsoft.NET.Test.Sdk and xunit.runner.visualstudio, which xUnit
needs to run). **Ask before adding any other.**

## Layout and the dependency rule

```
JustMakeIt.sln  Directory.Build.props  global.json  docker-compose.yml  .env.example
src/    Platform.Api  Platform.Auth  Platform.Email  Platform.Facebook  Platform.Instagram
        Platform.Sms  Platform.WhatsApp  Platform.YouTube  Platform.Helpers  Platform.Infrastructure
tests/  Platform.Helpers.Tests  Platform.Auth.Tests  Platform.Email.Tests  Platform.IntegrationTests
web-react/  React frontend (see React frontend in the decisions log): the member app, and the Site admin sign-in and dashboard
web/    older vanilla JavaScript frontend: site admin app, and the member app (sign in, create an account, role dashboard)
```

Feature projects (Auth, Email, each channel) share one layout: `Controllers/ DTOs/ Models/ Interfaces/ Services/
Repositories/`, plus `Providers/ Workers/ Settings/` in Email and `Settings/` in Auth, and a module class
(`EmailModule.AddEmailChannel()` etc.) that registers everything.

**Dependencies point one way.** Helpers references nothing. Infrastructure references Helpers. A channel references only
Helpers and Infrastructure; channels never reference each other or Platform.Auth. Auth references Helpers and
Infrastructure. Only Platform.Api references everything. No catch-all "Common" or "Shared" project.

One deployable (modular monolith): no microservices, no broker, no separate worker process. Background work is hosted
services inside Platform.Api. Adding a channel = fill in its project + one `builder.Services.Add<Name>Channel()` line in
`Program.cs`.

## Commands

```
dotnet build JustMakeIt.sln
dotnet test  JustMakeIt.sln                              # needs Docker (Testcontainers starts postgres:16-alpine)
dotnet test  JustMakeIt.sln --filter "Category!=Docker"  # everything that does not need Docker
PLATFORM_TEST_POSTGRES="Host=...;Port=...;Username=<superuser>;Password=...;Pooling=false"  dotnet test JustMakeIt.sln
                                                         # Docker-tagged tests against a throwaway PostgreSQL instead of a container
docker compose up -d                                     # local PostgreSQL 16 (copy .env.example to .env first)
dotnet run --project src/Platform.Api                    # http://localhost:5080, Development, applies migrations; opens Swagger (/swagger)
node --test "web/tests/**/*.test.js"                     # unit tests of the older web/ app (Node 22+ takes a glob, not a folder)
cd web-react && npm install                              # once
cd web-react && npm run dev                              # http://localhost:5173, forwards /v1 to the API on 5080 (VITE_API_TARGET to change)
cd web-react && npm test                                 # Vitest: logic, components, and the contracts with the server's source
cd web-react && npm run build                            # typecheck + production build into web-react/dist (git-ignored)
```

Visual Studio keeps `src/Platform.Api/bin` locked while it debugs the API, so a terminal build of the same output fails.
Build somewhere else with a custom configuration instead, for example `dotnet test tests/Platform.Auth.Tests -c Verify`,
or `dotnet build src/Platform.Api -c Verify -o <folder>` to run a second copy on another port.

### Secrets (never committed)

The API refuses to start without `Jwt:SigningKey` (>= 32 bytes) and `ConnectionStrings:App`. Set them with user-secrets:

```
dotnet user-secrets --project src/Platform.Api set "Jwt:SigningKey" "<random, 32+ bytes>"
dotnet user-secrets --project src/Platform.Api set "ConnectionStrings:Migrator" "Host=localhost;Port=5433;Database=platform;Username=platform_owner;Password=<POSTGRES_PASSWORD from .env>"
dotnet user-secrets --project src/Platform.Api set "ConnectionStrings:App" "Host=localhost;Port=5433;Database=platform;Username=app_user;Password=<pick one>"
```

To have a site admin (optional), name the account. If the email is not registered yet it is created from the password;
if it is, it is promoted and its own password is kept. This runs when the API starts (so restart it after setting these),
and only where migrations run (Development, or `Database:MigrateOnStartup`). Once the account exists the password can be
removed.

```
dotnet user-secrets --project src/Platform.Api set "SiteAdmin:Email" "<you@example.com>"
dotnet user-secrets --project src/Platform.Api set "SiteAdmin:Password" "<12 to 128 characters>"
```

Then sign in at `/admin` (http://localhost:5080/admin).

To offer **Continue with Google** on the member pages (optional; without these the button is not shown and email and password
still work), create an OAuth client of type "Web application" in Google Cloud Console (APIs and Services, Credentials), add the
authorized redirect URI `http://localhost:5080/v1/auth/google/callback` (and your real address later), then:

```
dotnet user-secrets --project src/Platform.Api set "Google:ClientId" "<id>.apps.googleusercontent.com"
dotnet user-secrets --project src/Platform.Api set "Google:ClientSecret" "<client secret>"
```

Set both or neither: the API refuses to start with only one. `Google:RedirectUri` is optional and defaults to the address of
the request; set it when the API runs behind a proxy, so it matches Google Cloud Console exactly.

`PLATFORM_TEST_POSTGRES` must name a server that holds no real data: the migration creates the cluster-wide role
`app_user` and the tests create and drop databases. Without Docker, `initdb`/`pg_ctl` from any local PostgreSQL install can
start a private cluster on a spare port for this.

`ConnectionStrings:Migrator` is the database owner (runs migrations). `ConnectionStrings:App` logs in as `app_user`; the
migrator sets that role's password from this string, so it is configured in one place. Environment variables work too
(`Jwt__SigningKey`, `ConnectionStrings__App`).

How long a sign-in lasts is `Jwt:AccessTokenDays` (default 30, allowed 1 to 90). The old `Jwt:AccessTokenMinutes` and
`Jwt:RefreshTokenDays` no longer exist; leftover values for them in user-secrets or the environment are ignored.

How long an invitation link lasts is `Auth:InvitationDays` (default 7, allowed 1 to 30).

## Database

- Scripts: `src/Platform.Infrastructure/Migrations/NNN_name.sql`, embedded, idempotent, applied in file-name order by
  `DbMigrator` (every start, one transaction, advisory lock). **Never edit a script that has run; add a new one.**
  `001_init.sql`, `002_site_admin.sql`, `003_business_models.sql`, `004_external_identities.sql` and
  `005_menus_permissions.sql` are released: do not change them. `006_remove_refresh_tokens.sql` drops the two refresh token
  tables and their lookups (released). `007_team_members.sql` adds the team tables and functions (released). `008_team_admin.sql` adds deactivating members, activating an invited person by hand and the name on an invitation (released). `009_account_security.sql` adds `auth_end_own_sessions()`, which a password change calls (released once committed). Phase 2 writes `010_email.sql`. Every script runs on every start, so a
  later one may never change what an earlier one re-creates (for example the return type of a function that
  `CREATE OR REPLACE` makes again): add a new function instead. (`001` and `002` still create the refresh token tables on
  every start and `006` drops them again in the same transaction; that is harmless, and nothing may use those tables.)
- The API connects as `app_user` (not superuser, no BYPASSRLS, never to be given either). `NpgsqlConnectionFactory`
  sets `app.agency_id` and `app.user_id` on every connection it opens; `app_agency_id()` / `app_user_id()` read them and
  return NULL when unset or empty, so access fails closed.
- Every tenant table: `id uuid` PK, `agency_id uuid NOT NULL REFERENCES agencies(id) ON DELETE CASCADE`, `created_at
  timestamptz`, RLS **enabled and forced**, policy `tenant_isolation` (`USING` and `WITH CHECK` on `agency_id =
  app_agency_id()`). Status/type columns are `text` + `CHECK` with the lower-cased C# enum names. Foreign keys are
  indexed. snake_case names. `agencies` and `agency_members` also carry `business_model` (see Business models and roles).
- Work that must span agencies (workers, login) uses narrow `SECURITY DEFINER` functions that return only ids
  or the one row asked for. Never grant BYPASSRLS.
- **Global configuration tables** (`permissions`, `role_permissions`, `permission_state`, `menu_pages`, `menu_items`) are not
  tenant tables: no `agency_id`, RLS still enabled and forced, `app_user` has `SELECT` only (none at all on
  `permission_state`) and only when a user is set, and every change goes through a `site_admin_*` `SECURITY DEFINER`
  function that checks `site_admins` inside the database. Their seeds are `INSERT ... ON CONFLICT DO NOTHING` and never
  `UPDATE` a grant or an item, because the script runs on every start and must not undo what the Site admin set. A page or
  permission added later is inserted the same way and followed by `SELECT menu_apply_default_items();`.
- Dapper rules: row models are **classes with settable properties** (underscore matching does not apply to constructor
  parameters, so no positional records). Write enums with `.ToDb()` (Dapper ignores enum type handlers). Write jsonb with
  `JsonbParameter`. `DateTimeOffset` is handled by `DateTimeOffsetTypeHandler`. Explicit column lists, parameterised SQL only.

## Working rules

- C#: file-scoped namespaces, `sealed` by default, constructor injection, async with a `CancellationToken` on every I/O
  method, no static mutable state, no `async void`.
- Controllers are thin: validate, call one service method, return. Business rules live in services, SQL in repositories.
  Services throw `AppException` subclasses (400/401/403/404/409); middleware turns them into problem responses and never
  returns stack traces. Validate DTOs at the controller and re-check rules in the service.
- **Authorization is by permission, and the permissions are in the access token.** An endpoint names a permission with
  `[Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.X)]` (a constant, so channels can use it without
  referencing Auth); `CanRead`, `CanWrite` and `CanManage` stand for the baseline permissions `data.read`, `data.write` and
  `organization.manage`. Which role holds which permission is data the Site admin sets (`role_permissions`); `RolePermissions`
  is only the factory default and the test oracle. A new permission is a constant in `PermissionKeys`, a seed row with its
  default grants, a policy (registered from `PermissionKeys.All`) and a test. Services re-check with `tenant.HasPermission`.
- Structured logging. Never log passwords, tokens, full email bodies, or paths that carry tokens (/t/c/{token}, /u/{token}).
- Read configuration lazily (options pattern), not while registering services, so test overrides apply.
- Tests: every service rule has a unit test; every query on a tenant table has an integration test that connects as
  `app_user` and proves isolation. Tests are independent: one PostgreSQL container per run, a fresh database per test class.
  Tests that need Docker carry `[Trait("Category", "Docker")]`.
- Frontend: no `innerHTML` with API data (in React: no `dangerouslySetInnerHTML`); email previews in a sandboxed iframe; 360 px
  first; every drag action has a tap alternative; **the sign-in token is never readable by a script** (an HttpOnly cookie: the
  page holds no token, sends no Authorization header and stores nothing); never queue or send a campaign while offline.
- Ask before: changing an existing table, adding a package, weakening a security rule.
- Do not: use EF Core, a CSS framework or jQuery, a second frontend framework next to React, edit an applied migration, give `app_user` BYPASSRLS,
  commit secrets, write logic in the five channel stubs.

## Build process

Build one phase at a time and **stop for approval** after each. At the end of a phase: `dotnet build`, `dotnet test`
(and `node --test` for frontend phases), commit as `phase N: <short summary>`, then report what was built, what changed
or was skipped, and any open question.

| Phase | Scope |
|---|---|
| 0 | Foundations: this file, compose, Helpers, Infrastructure, Api host + /health, test projects. **Done** |
| 1 | Auth endpoints, workspaces, authorization policies, rate limiting on /v1/auth/*. **Done** |
| 2 | `010_email.sql` (was 002; the site admin, business model, Google sign-in, menus-and-permissions, refresh-token-removal, the two team scripts and the account security script took 002 to 009), senders, contacts, lists, suppressions, CSV import |
| 3 | Templates + renderer, campaigns + state machine |
| 4 | Scheduler/dispatcher workers, providers, tracking endpoints, webhooks |
| 5 | Reports |
| 6 | Frontend foundation. **Started early:** site admin login and dashboard (API in `/v1/site/*`, pages in `web/`); **Also started early:** the member app (`/login`, `/register`, `/dashboard`) with Continue with Google; **and a third:** the app shell with a sidebar from the database, permissions in the token, and the Site admin's menu and permission editors (see Menus and permissions); invitations and the team page are built (see Team members and invitations); the rest (organization switcher, PWA) is still to do. **The frontend is being rebuilt in React** (`web-react/`, see React frontend): the member app and the Site admin sign-in and dashboard are there, the Site admin's menu and permission editors are next, then `web/` can go |
| 7 | Frontend email screens (in `web-react/`) |
| 8 | CI, README, seed data, security review |

## Decisions log

> **Read this first.** Sessions changed after most of the entries below were written: there are **no refresh tokens**, a sign-in
> is one 30-day JWT in an HttpOnly cookie, and the page holds no token. Wherever an entry below speaks of a refresh token or
> cookie, `refresh_tokens`, `site_admin_refresh_tokens`, `/v1/auth/refresh`, `/v1/site/refresh`, a 15-minute access token, a token
> "in memory only", `api.onTokenRefreshed` or a client that "refreshes and retries", it is history. The current rules are in
> **Sessions without refresh tokens** at the end of this log.

**Phase 0**
- The repo was empty, so `001_init.sql` was written from the brief's description. Users are global (one person, many
  agencies). `agencies` is visible when it is the current agency or the user is a member; `users` only for the user
  themselves; `agency_members` writable in the current agency and readable for the caller's own memberships;
  `refresh_tokens` need both agency and user to match. Plan values: `trial`, `paid`.
- Login and refresh need a row before any tenant is known, so `001_init.sql` also has two `SECURITY DEFINER` lookups:
  `auth_find_user_by_email(text)` and `auth_find_refresh_token(text)`. Registration instead picks the new agency and user
  ids first and opens its connection for them, so every policy passes without bypassing anything.
- `FORCE ROW LEVEL SECURITY` also binds the table owner, which would blind `SECURITY DEFINER` functions unless the owner
  is a superuser. `001_init.sql` adds an `owner_lookup` SELECT policy for the migrating role on `users` and
  `refresh_tokens`. Phase 2 must do the same for the tables the worker functions read.
- The migration fails if `app_user` is a superuser or has BYPASSRLS. It creates the role without a password; `DbMigrator`
  sets it from `ConnectionStrings:App`.
- `DbMigrator` keeps no journal table: it runs every script every start (per the brief). Migrations run in Development, or
  where `Database:MigrateOnStartup` is true.
- Module methods take no arguments: `AddInfrastructure()`, `AddAuthModule()`, `AddEmailChannel()`. The five channel stubs
  are not called from `Program.cs` yet.
- JWT validation (scheme, the 32-byte key rule) lives in `Platform.Auth`, because channels cannot reference Auth but still
  need `[Authorize]`. Token issuing arrives in Phase 1.
- `Platform.Helpers.Tests` was added (the brief lists three test projects) so helper rules have unit tests.
- Docker Compose publishes PostgreSQL on host port **5433** by default, because 5432 is often taken locally.
- Target framework was net8.0 as specified; retargeted to **net10.0** right after Phase 0 at the user's request (.NET 8
  leaves support on 10 Nov 2026). `global.json` pins SDK 10.0.100 (rolls forward to the latest feature band), the
  `LangVersion` pin to 12 was dropped (C# 14 is the default), JwtBearer and Mvc.Testing moved to 10.0.14, and
  Testcontainers.PostgreSql to 4.16.0 (4.10.0 pulled in SSH.NET 2025.1.0, which has known high-severity advisories).
  Npgsql stays on 8.0.9 and Swashbuckle on 7.3.2 (10.x changes the OpenApi model): both run on net10; bumping them is a
  deliberate upgrade pass (Phase 8), not something to do in passing.

**Phase 1**
- Endpoints (all JSON, camelCase, errors are `application/problem+json` with `code` and, for 400s, `errors` per field):
  `POST /v1/auth/register|login|refresh|logout|switch-agency`, `GET /v1/auth/me`, `GET|POST /v1/workspaces`. Token
  responses are `{accessToken, tokenType: "Bearer", expiresIn}`; the refresh token is never in a body.
- Refresh cookie `refresh_token`: HttpOnly, **Secure always**, SameSite=Strict, Path=/v1/auth, lifetime `Jwt:RefreshTokenDays`
  (30). Chrome and Firefox accept Secure cookies on http://localhost; Safari does not, so develop in Chrome or Firefox.
  A failed refresh does not try to clear the cookie (the exception middleware calls `Response.Clear()`); logout always does.
- Rotation is atomic: the replacement is inserted and the old token revoked only `WHERE revoked_at IS NULL` in one
  transaction, so of N simultaneous refreshes with one cookie exactly one wins. Presenting a revoked token (or losing that
  race) revokes every live token of that user for that agency and logs a warning. The role in a refreshed token comes from
  the database, and a user who left the agency cannot refresh.
- Policies live in Platform.Auth (`AuthPolicies`), their names in `Platform.Helpers.Constants.PolicyNames`: `CanRead` (any
  role), `CanWrite` (owner/admin/manager), `CanManage` (owner/admin: members and workspaces). *(The roles were replaced
  later, see Business models and roles. The policy names stayed. They became permission-based in Menus and permissions.)*
  The **fallback policy requires a
  signed-in user with an agency**, so anything not marked `[AllowAnonymous]` is closed; as a side effect a request for an
  unknown route answers 401, not 404, when anonymous. Policies read the token's role claim, so a role change takes up to 15
  minutes to reach an existing access token; services re-check the role, and `/me` and refresh read it from the database.
- Passwords: PBKDF2-SHA256, 600,000 iterations, `pbkdf2$iterations$salt$hash`, iteration count read from the stored value,
  constant-time compare. Registration needs 10 to 128 characters. Login verifies against a dummy hash when the email is
  unknown, so timing does not reveal which emails exist.
- Registration answers 409 `email_taken`, which does reveal that an address is registered. Accepted for usability; the
  credentials rate limit is the mitigation.
- Registration chooses the new agency and user ids first and opens its connection for them, so `agencies`, `users`,
  `agency_members` and `workspaces` rows all pass their own policies in one transaction. Login and refresh find their rows
  through `auth_find_user_by_email()` / `auth_find_refresh_token()`; `001_init.sql` was not touched.
- Rate limits (`RateLimiting:Auth`): fixed window per client address, 10 requests/min for register+login (one shared
  budget), 60/min for refresh, logout, switch-agency and me; counted before authentication. 429 is problem+json with
  `Retry-After`. **Behind a reverse proxy every client shares the proxy's address**, so Phase 8 must add forwarded-header
  handling that trusts only configured proxies, or the limits will lock everyone out together.
- Error responses and token responses carry `Cache-Control: no-store`.
- `refresh_tokens` rows are never deleted; expired and revoked ones need a cleanup job (open item for a later phase).
- Tests: all 74 Phase 0 Docker-tagged tests and every Phase 1 one were run against a throwaway PostgreSQL 18.4 through
  `PLATFORM_TEST_POSTGRES`; CI will run them on PostgreSQL 16 through Testcontainers.

**Site admin (an early slice of Phase 6, requested by the user: "site admin login and a dashboard after login")**
- A **site admin** is a platform-wide super admin above all agencies, not an agency role. Chosen by the user over reusing
  the agency owner/admin roles. Agency roles are unchanged.
- Storage: `002_site_admin.sql` adds `site_admins` (user_id) and `site_admin_refresh_tokens`, both RLS enabled and forced.
  `users` is not touched: the role is a separate table on purpose. `app_user` can only **SELECT** `site_admins`, and only
  its own row, so no bug or injection in the API can promote anybody. Rows are written by the database owner: the startup
  bootstrap (`SiteAdmin:Email` / `SiteAdmin:Password`, see Secrets) or SQL. The table is not a tenant table (no agency).
- Cross-agency data comes only through `SECURITY DEFINER` functions (`site_admin_overview()`,
  `site_admin_list_agencies(limit, offset)`, plus the `auth_find_site_refresh_token()` lookup). Each checks inside the
  database that `app_user_id()` is in `site_admins` and otherwise raises 42501, which the API turns into 403 `not_site_admin`.
  A site admin has no direct access to any other table: with no agency the policies show nothing.
- `002_site_admin.sql` adds `owner_lookup` SELECT policies for the migrating role on `agencies`, `agency_members`,
  `workspaces` and `site_admin_refresh_tokens` (existing tables, approved with the choice above). Without them a
  non-superuser owner is blind under FORCE RLS and the functions would report zeros instead of failing. A test runs the
  migrations as a non-superuser owner to prove this (`SiteAdminWithLimitedOwnerTests`).
- Token: `POST /v1/site/login` issues `user_id` + `role: siteadmin` and **no `agency_id`**. So it passes no agency policy
  and fails the fallback policy (403 on every agency endpoint and on unknown routes), and an agency token never passes the
  new `SiteAdmin` policy (`PolicyNames.SiteAdmin`, `RoleNames.SiteAdmin`). A person who is both uses two separate sessions.
  Login answers 401 `invalid_credentials` identically for an unknown email, a wrong password and a user who is not a site
  admin, so the endpoint does not reveal who is one.
- Sessions: own cookie `site_refresh_token` (HttpOnly, Secure, SameSite=Strict, **Path=/v1/site**), own table, same rotation
  and reuse detection as the agency sessions. Refresh re-checks `site_admins`, so a demoted admin cannot refresh, and
  their tokens are revoked. The token policy is per command: only INSERT requires being a site admin, so a demoted user can
  still revoke what they hold (this was a real bug found by a test before release).
- Endpoints: `POST /v1/site/login|refresh|logout`, `GET /v1/site/me|overview|agencies?page=&pageSize=` (page size 1 to 100,
  default 20). They share the `/v1/auth/*` rate limit budgets (credentials for login, session for the rest).
- `/v1/site/me` and the data endpoints re-check the database, so removing a site admin takes effect at once even though
  their access token stays valid for up to 15 minutes.
- Web client (vanilla JS, ES modules, Web Components, no build step): `web/index.html` shell, `js/core` (api client with
  single-flight refresh, in-memory session, History API router with guard, formatters, messages), `js/pages`
  (`admin-login`, `admin-dashboard`), `css/app.css`. Routes `/admin/login` and `/admin`; `MapPlatformWebFallback` makes
  `/admin` and `/admin/*` answer with the shell so a reload works, and nothing else falls back (an API typo stays a real
  error). `web/tests` is never served. The page has a strict CSP (no inline script or style) and no `innerHTML`; both
  rules, and "tokens only in memory", are enforced by `web/tests/rules.test.js`. The access token lives only in the page;
  a reload restores the session from the cookie.
- Migration numbering: this took `002`, so Phase 2's email script moved to `003_email.sql`; business models then took
  `003`, Google sign-in took `004` and menus and permissions took `005`, so it is now `006_email.sql`.
- Verified in a real browser (headless Edge over the DevTools protocol, throwaway profile): redirect to sign-in, wrong
  password message, dashboard at 360 px and 1280 px, paging, reload restores the session, sign out, not-found page, nothing
  in local/session storage, no horizontal scroll, no script errors (the only console entries are the expected 401s).

**Business models and roles (requested by the user: "3 business models with 7 role types")**
- Scope: roles and business models only. The social, AI and approval wording in the role descriptions belongs to later
  features and is not built; phases 2 to 5 (email) are unchanged. The request said 7 role types, but its tables name 4
  different roles (3 + 4 + 1 = 8 role slots); the user chose **4 shared roles**.
- Business models (`BusinessModel`, stored lower-case in `agencies.business_model`): `company`, `agency` (a marketing
  agency with several clients), `creator` (one person). Chosen at registration (`businessModel`, required, exact
  lower-case name) and **never changed**: `agency_members` repeats it and references `(id, business_model)` of the
  agency, so the foreign key refuses a change while the organization has members.
- Roles (`OrganizationRole`, replacing `AgencyRole`), stored and in the token as `organizationadmin`, `clientmanager`,
  `marketingmanager`, `contentwriter`. The JSON the API sends spells them the same way (`OrganizationRoleJsonConverter`),
  because the general enum converter would write `organizationAdmin`.

  | Model | Roles allowed |
  |---|---|
  | company | organizationadmin, marketingmanager, contentwriter |
  | agency | organizationadmin, clientmanager, marketingmanager, contentwriter |
  | creator | contentwriter (exactly one member) |

  The founder is an organizationadmin (company, agency) or the contentwriter (creator). One CHECK
  (`ck_agency_members_role_model`) holds the table, `BusinessModelRules` in Platform.Auth holds the same one, and
  `BusinessModelMigrationTests` compares them for every pair. A partial unique index limits a creator organization to one member.
- Permissions (`RolePermissions`, policy names unchanged): `CanRead` every role; `CanWrite` organizationadmin,
  marketingmanager, contentwriter (not clientmanager); `CanManage` organizationadmin only. There is no read-only role
  any more, so `clientmanager` is the least-privileged one. Finer rules (approve, publish, connect accounts) get their own
  policy when those features exist. A creator therefore cannot create workspaces by role, and `WorkspaceService` also refuses
  a second workspace for a creator (409 `workspace_limit`); the database does not enforce that cap.
- API: `POST /v1/auth/register` takes `organizationName` (was `agencyName`) and `businessModel`. `/v1/auth/me` and its
  memberships carry `businessModel`. Error `code` values (`no_agency`, `not_a_member`) and routes are unchanged.
- `003_business_models.sql` changes `agencies` and `agency_members` (approved with the plan): existing rows become the
  `agency` model and their roles move owner/admin to organizationadmin, manager to marketingmanager, viewer to
  contentwriter. An UPDATE under forced RLS changes nothing for an owner that is not a superuser, so the script creates a
  policy `owner_migrate` for the migrating role, runs the remap and drops the policy again in the same block (RLS stays
  enabled and forced, nothing is left behind). A test shows the policy is needed: with it removed the superuser upgrade still
  passes and the limited-owner upgrade fails. No column default is kept, so an insert that forgets the model fails.
- The site admin list needs the model, but 002 re-creates `site_admin_list_agencies()` on every start, so its return type is
  frozen. `003` adds `site_admin_list_agencies_v2()` (same checks, adds `business_model`) and the API uses that; the old
  function stays, unused. The dashboard shows a business model badge and says "Organizations".
- Access tokens issued before this change carry the old role names and get 403 until refreshed (15 minutes at most).
  Nothing was deployed, and the development database had no organizations.
- The last organizationadmin cannot be removed or demoted (the team functions enforce it, see Team members and invitations).
  Open: a creator will need a way to connect their own accounts later; `CanManage` is admin-only, so that needs its own policy.

**Member app: sign-in, sign-up and a dashboard for each role (requested by the user; the second early slice of Phase 6)**
- Addresses: `/login`, `/register`, `/dashboard` (the site admin app stays under `/admin`). `MapPlatformWebFallback` answers
  those exact addresses with the shell, so a reload works; nothing else falls back. `main.js` starts the app that belongs to
  the address (`appForPath`). The two apps have their own routes, session (`memberEndpoints` or `siteAdminEndpoints`) and
  refresh cookie, and share the api client, the router and the styles.
- Sign-up: pick the kind of organization (company, marketing agency, individual creator), give its name, then choose
  **Continue with Google** or **email and password**; the email fields appear only when email is chosen. Google is offered
  only when the server has it set up (`GET /v1/auth/providers` answers `{google: bool}`), so no button leads nowhere.
- **Google is a server-side redirect flow with PKCE** (chosen by the user over Google's own script, because the page's content
  security policy allows scripts from this origin only and stays unchanged). `GET /v1/auth/google/start` makes a random
  `state`, `nonce` and PKCE verifier and keeps them, with the intent, business model and organization name, in the encrypted
  `google_oauth` cookie (ASP.NET Core Data Protection; HttpOnly, Secure, **SameSite=Lax** because Google returns the browser
  with a top-level GET from another site; Path=/v1/auth/google; 10 minutes), then redirects to Google.
  `GET /v1/auth/google/callback` first checks the cookie's state against the address, so a callback this browser did not
  start is refused before Google is asked anything. It trades the code for an ID token over the back channel with the client
  secret and the verifier, and checks the token: signature against Google's published keys (RS256 only), issuer, audience,
  expiry, our nonce, a subject and an email. Then it signs the person in (refresh cookie, redirect to `/dashboard`) or
  redirects back to the form with `?error=<code>`. No token or code is ever in an address we send on, the callback answers
  `Referrer-Policy: no-referrer`, nothing secret is logged, and the client secret never leaves the server. Start and callback
  share the credentials rate limit. With several instances the data-protection key ring must be shared (Phase 8).
- **An existing email is never linked to Google** (chosen by the user). Password sign-ups are not email-verified, so linking by
  address would let whoever registered it first keep access. A Google sign-in whose verified email already has an account is
  refused with `google_email_taken`, from either form; a login with no account gets `google_no_account`; an unverified Google
  email is refused. A known Google user who uses the register form is simply signed in.
- `004_external_identities.sql` (approved with the choice above): `users.password_hash` may be empty; the new `user_identities`
  (provider, subject, email) has unique `(provider, subject)` and one identity per user per provider, RLS enabled and forced,
  and `app_user` may only SELECT and INSERT, only for the user it is acting as. `auth_find_user_by_identity()` is the narrow
  definer lookup before any user is known (with an `owner_lookup` policy, so it works for a non-superuser owner; tested).
  "Every user has a password or an identity" spans two tables, so it is a **deferred constraint trigger** `ck_users_credential`
  checked at commit, not a CHECK. It runs when a user is created or their password is cleared, not when an identity is
  deleted (nothing deletes one). A person with no password gets the same 401 as a wrong password at password login, and
  `UserLoginRecord.PasswordHash` is nullable so that path cannot be skipped.
- Dashboard: one page that changes with the person's role in their kind of organization. It shows the organization and its
  model, the role and what it is responsible for (the user's own wording per model and role, in `web/js/core/roles.js`), what
  the role may do (mirrors the server's policies; the server still decides), the workspaces (client workspaces in an agency;
  an organization admin of a company or agency can add one, a creator has the one made at sign-up), what is still to come for
  that role (labelled "Coming soon": none of it exists), and the token's user id and role.
- Not built then: inviting people into an organization (built since, see Team members and invitations), an organization
  switcher for people in several
  organizations, linking Google to an existing account (needs email verification first), changing a password, and the real
  Google round trip, which needs your own Google credentials and was checked here only against a stand-in for Google.

**Menus and permissions (requested by the user: "menu should load based on roles and permission, configured by the Site admin";
an early slice of Phase 6). Status: steps 1 to 7 of 8 are built and tested (the API, the web shell with the sidebar, the
`/workspaces` page and the two admin pages); step 8, the final review and the commit, waits for approval.** The approved plan has
a build order; the starred steps (3, 5, 7) stop for approval.
- Scope chosen by the user: both the member app and the site admin app get a sidebar from the tables; permissions are
  **named permissions per business model and role** (the 8 allowed slots); links point to **built-in pages only** (a registry,
  no external addresses); and the API itself authorizes through the permissions, which **changes the Phase 1 policies**
  (approved by the user, who also asked that the JWT carry the permissions and that the menu come from a cache, not a
  database read per page or call).
- `005_menus_permissions.sql` (new tables only, no existing table changed): `permissions` (the catalog, `is_protected`),
  `role_permissions` (one row per slot and key, **never deleted**: a revoke is `is_granted = false`, so a restart cannot undo
  it; `ck_role_permissions_role_model` repeats the role and model table), `permission_state` (one row, the **permission
  version**), `menu_pages` (the registry of built-in pages; primary key `(app, key)` because both apps have a `dashboard`) and
  `menu_items` (a composite key puts a page only in its own app's menu; a locked item cannot be hidden or deleted).
  Seeds are `ON CONFLICT DO NOTHING`; `menu_apply_default_items()` gives a page its default item once
  (`menu_pages.default_item_created`), so a deleted item stays deleted and a page added by a later script appears once.
  Catalog: `dashboard.view` (protected), `workspaces.view`, `workspaces.create`, `data.read`, `data.write`,
  `organization.manage`; the seeded grants equal the old hard-coded rules, and an integration test compares them with
  `RolePermissions.GrantedByDefault` for every slot and key (48 rows).
- The version is a counter raised by a row-level trigger when a grant is inserted or its `is_granted` changes. Row-level, so
  the seed that runs on every start does not raise it; a no-op update does not either. Writes go only through five
  `site_admin_*` definer functions; `permission_snapshot()` (every grant and the version, in one statement) and
  `menu_snapshot()` are the only reads the caches use, and need no signed-in user.
- **The token carries the permissions.** A member's access token has one `permission` claim per key their role holds and `pv`,
  the version they were read under; a site admin token has neither. `AuthService.BuildResult` is the one place a member token
  is made. The grants are read **before** any write (a refresh rotates the old refresh token only after them), because
  failing afterwards would lose the cookie and look like a stolen token being reused.
- **Every request is checked in memory.** `PermissionVersionMiddleware` (after authentication, before authorization) answers a
  member token whose `pv` is not the current version, or has none, with 401 `permissions_changed`; the web client already
  refreshes and retries on a 401. Site admin tokens and `[AllowAnonymous]` endpoints are skipped. If the version cannot be read
  the request fails (500), never passes. `PermissionAuthorizationHandler` then needs a user, an agency, a role this build
  knows **and** the endpoint's permission claim. `ITenantContext.HasPermission` is how services re-check.
- `PermissionService` (singleton) holds the matrix and the version in memory: 30 seconds, one load at a time, a change made
  here calls `Invalidate()` at once, and a load that began before an `Invalidate()` is never served as current. Another
  instance learns of a change within the 30 seconds. Roles in a token still lag a membership change by up to 15 minutes, as in
  Phase 1 (unchanged). The first run of this version refuses every token issued before it, once.
- `CanRead`, `CanWrite` and `CanManage` keep their names and stand for `data.read`, `data.write` and `organization.manage`;
  `WorkspacesController` uses `workspaces.view` and `workspaces.create` (`insufficient_role` became `insufficient_permission`).
  `RolePermissions` stays as the factory default and the test oracle (`NamesWhere` was removed).
- **The menu endpoints.** `GET /v1/menu` (any signed-in member) and `GET /v1/site/menu` (site admin) answer `{items:[{id, pageKey,
  label, icon, path}], permissions:[...]}` from `MenuCache` (60 seconds; a menu edit made here calls `Invalidate()`) and, for a
  member, the permissions **in the token**: no database read per call, proved by a test that wraps the repositories in counters.
  An item shows when it is visible and either needs no permission or the token carries it (`MenuVisibility`, pure). Hiding an
  item is not access control; the endpoints check the same permissions. The member menu never holds an admin page and the other
  way round. A site admin who was demoted can still read the admin sidebar until their token expires (configuration, not
  secret); every other `/v1/site` route re-checks `site_admins`.
- **The Site admin's editors** (`/v1/site/menus/{menu}` and `/v1/site/permissions`, policy `SiteAdmin`, session rate limit):
  `GET` a menu (all items, the pages that could still be added, the icons), `POST .../items`, `PUT .../items/{id}`, `DELETE
  .../items/{id}`, `PUT .../order` (the full list of ids, once each), `GET /v1/site/permissions` (the matrix), `PUT
  /v1/site/permissions/{key}/grants`. Errors: `menu_not_found`, `menu_item_not_found`, `page_not_found`, `page_already_in_menu`,
  `menu_item_locked`, `menu_changed` (a stale order), `permission_not_found`, `permission_protected`, `invalid_slot`. An item
  addressed through the wrong menu is "not found". Reads check `site_admins` on their connection and writes are the definer
  functions, so a demoted site admin is refused at once. Label: trimmed, 1 to 60 characters, no control characters; the icon
  must be in `MenuIcons.All` (a test compares it with the seeded icons, step 6 will compare it with the web client's drawings).
  After every write attempt the matching cache is invalidated, even when the attempt failed.
- `SnapshotCache<T>` is the one implementation of "hold a small snapshot for a while" under both caches (see its summary for the
  rules). Hosts without a database get `DefaultGrantsPermissionService` and `DefaultMenuCache` from `ApiHostFactory`, so the
  authorization tests stay database-free.
- **The web client.** `<app-shell>` (`web/js/components`) is the frame of every page behind sign-in: the header, the sidebar the
  session holds, and the page. It is made once in `main.js` and reused; sign-in and sign-up stay bare, and the shell leaves the
  document (and stops listening to the session) when they show. The pages no longer build their own header; the dashboards
  render content only. Narrower than 720 px the sidebar is a drawer (menu button, Close button, Escape, a tap outside, or
  choosing a link closes it; while open the header and the page are `inert`, and while closed the drawer is, so Tab never
  reaches an off-screen link); from 720 px it is a column and the button is gone. The sidebar colours are tokens built from the
  page's own, so dark mode and the cream palette need nothing of their own. Icons are drawn with `createElementNS` from a fixed
  list (`core/icons.js`); a test compares it with `MenuIcons.cs`.
- **The client holds the menu and the permissions in memory** (`session.state.menu` and `.permissions`), read when the person
  signs in or the page restores the session, and again each time a refresh gives the page a new token (`api.onTokenRefreshed`),
  which is when a change the Site admin made reaches them. Moving between pages reads memory, never the server; nothing is
  written to storage. A member whose menu cannot be read cannot sign in (without permissions the page cannot tell which pages
  are theirs); the site admin app falls back to the three pages that repair a menu, so a broken menu can always be fixed.
  `parseMenu` drops any item whose address is not a plain address of this site (`isSafePath`: no scheme, no host, no `//`, no dot).
- **Route guard.** A route may name a `permission`; `resolveNavigation` answers `forbidden` unless the session's list holds it (a
  missing list holds nothing). The router re-checks when the permissions change after a renewed token, and re-renders only if
  that changes what the open address shows, so nothing a person is typing is thrown away. A person who loses a permission sees
  "You do not have access to this page" (inside the shell) the next time their page makes a request, and the link leaves
  the sidebar. The page's own API calls are refused by the server in any case.
- **Pages.** `/workspaces` (the dashboard's workspaces list and add form moved there; the form needs `workspaces.create`),
  `/admin/menus` (one menu at a time: Up and Down buttons, never only a drag; Hide/Show; Edit with a draft that survives a failed
  save; Delete with a confirmation; an add form with the pages not yet in the menu; one change at a time, all buttons off while
  it runs) and `/admin/permissions` (a fieldset per permission, a checkbox per role grouped by kind of organization, saved at
  once, rolled back on failure; the protected permission is ticked and disabled). `core/permission-keys.js` is a leaf module with
  the keys and `hasPermission`; a test compares the keys with `PermissionKeys.cs` and another compares the seeded pages with the router.
- Checked in a real browser (headless Edge over the DevTools protocol, real mouse and key events, a throwaway database and API
  copy, never the development database): the drawer at 360 px (open, Escape, tap outside, Close, link), the column at 1280 px,
  dark mode, every editor action, the permission toggles and a member losing a permission, sign-out and sign-in again, no
  horizontal scroll at 360 px, controls at least 44 px, no script errors. The headless browser shows a 15 px scrollbar, so a tap
  point meant for the page beside the drawer must stay clear of the right edge.
- Defaults chosen in the plan: both Dashboard items are locked, an item's page cannot be changed (delete and add again), and a
  rename or reorder reaches other people when they next load the page or renew their token, not instantly.

**React frontend (requested by the user: "create new React application for front, I don't want to use the existing plain
JavaScript frontend; everything should be component based, and based on the user's role permissions components will load").
Status: the member app and the Site admin sign-in and dashboard are built, tested and checked in a real browser; the Site admin's menu and permission editors are not ported yet.**
- Choices made by the user: **TypeScript** (over plain JavaScript), and **the member app plus the permission system first**,
  with the Site admin app (login, dashboard, menu editor, permission editor) as the next step. This **replaces two rules** that
  said "vanilla JavaScript, no framework, no build step": React 19, Vite and TypeScript are now the frontend stack, and the "Do
  not" list now bans a CSS framework, jQuery and a second framework instead. `web/` is untouched and still what the API serves.
- New frontend packages (all under `web-react/`, none touch the .NET side): `react`, `react-dom`, `react-router-dom`; dev:
  `vite`, `@vitejs/plugin-react`, `typescript`, `@types/react`, `@types/react-dom`, `@types/node`, `vitest`, `jsdom`,
  `@testing-library/react`, `@testing-library/dom`. `npm install` found no vulnerabilities.
- Layout of `web-react/src`: `api/` (the one client that calls fetch, DTO types), `session/` (the store: token in memory, user,
  organization, menu, permissions; and the React context over it), `access/` (permission keys, `<Can>`, route guards),
  `navigation/` (menu parsing, the page table), `components/` (`ui`, `layout`, `icons`), `features/` (`auth`, `dashboard`,
  `workspaces`, `errors`: one folder per screen, each made of small components), `lib/` (wording, roles, formatting),
  `test/` (doubles and helpers). Path alias `@/` is `src/`.
- **How components load by permission.** The session holds the permission list from the access token (as in the old app: read
  at sign-in or restore, and again whenever a renewed token arrives). `pages.ts` is the table of pages behind sign-in: path,
  title, the permission the page needs, and a `lazy()` import of its component. `App.tsx` makes one route per row, wrapped in
  `RequirePermission`, which renders the "no access" page **instead of** the page, so a page the token does not allow is never
  mounted, makes no request and its code is never downloaded (a test asserts the missing request; a mutation check confirmed the
  tests fail when the guard is removed). Inside a page, `<Can permission|anyOf|allOf fallback>` does the same for one part (the
  add-workspace form needs `workspaces.create`); a rule that asks for nothing, or for an empty list, allows nothing. The sidebar is
  the reusable `<Menu>` (`components/layout/Menu.tsx`: a navigation landmark with one link per item, the open page marked,
  an `onNavigate` callback for the drawer) over the list the server sent; only the app shell draws it, and the shell is never
  drawn for sign-in, sign-up or the not-found page of a visitor (a test checks it), and neither does the Site admin
  sign-in page. A change of permissions (a renewed token) re-renders everything that depends on it, so a page the
  person just lost becomes "no access" while they are on it (checked in a real browser after revoking a grant in the database).
  **Role never authorizes**: only permissions do, as on the server; the role only chooses wording (the dashboard's text).
- Session store: `createSession(api)` is plain TypeScript (testable without React) and React reads it with
  `useSyncExternalStore`. `restore()` shares one attempt between overlapping callers (the refresh token rotates), and
  `main.tsx` starts it before drawing, so the routes show a loading line until the status is known and a page is never drawn
  and then replaced by the sign-in form. The Site admin app has its own session over the same store (see Site admin sign-in and dashboard).
- Dev and production. `npm run dev` serves on 5173 and forwards `/v1` to the API (5080, or `VITE_API_TARGET`), so page and API
  share one origin and the Secure, HttpOnly refresh cookie behaves as in production (Chrome and Firefox). The strict CSP
  (own origin only, no inline script or style) is added to the **built** page only, because the dev server needs an inline script;
  `rules.test.ts` checks the policy and that the page has no inline script, style or handler. The API can serve the build
  without a code change (`Web:RootPath` = `web-react/dist`; it answers `/login`, `/register`, `/dashboard`, `/workspaces` with
  the page, as before), but **do not switch it until the Site admin's menu and permission editors are ported**, or `/admin/menus` and `/admin/permissions` would show "not found" (`/admin/login` and `/admin` work in the React app already). An
  unknown address typed into the address bar is still answered by the server (401 when signed out), not by the app, as before.
- Google sign-in through the dev server: Google sends the person back to the address the request came from, so register
  `http://localhost:5173/v1/auth/google/callback` in Google Cloud Console (or set `Google:RedirectUri`). Still never run against
  real Google credentials.
- Tests (`npm test`, 218 in 16 files when this was written, 320 in 21 after the Team slice): the API client and session ported from the old suite, wording and roles, the permission
  rules, `<Can>`, the route guards and drawer, the sign-in, sign-up, dashboard and workspaces screens, and **contracts with
  the server's own source** (permission keys against `PermissionKeys.cs`, menu icons against `MenuIcons.cs`, page table against
  the seeds in `005_menus_permissions.sql`). `rules.test.ts` ports the old rules: no storage, no markup from strings (including
  `dangerouslySetInnerHTML`), no eval, fetch only in the client, no inline style, `lazy()` only in the page table.
- Checked in a real browser (headless Edge over the DevTools protocol, real mouse and key events; the built app served by a
  throwaway copy of the API on a private PostgreSQL cluster, never the development database): redirect to sign-in, sign-up as a
  company, the dashboard, reload restoring the session from the cookie, nothing in storage, the workspaces page and adding a
  workspace, in-app "not found", the drawer at 360 px (open, Escape), no horizontal scroll, sign-out, wrong password, and a
  permission revoked in the database reaching the open app (page turns into "no access", link leaves the sidebar). No script or
  CSP errors; the only console entries are the expected 401 and 403 answers.
- Not built: the Site admin's menu and permission editors, the organization switcher, linking Google to an existing account. The legacy
  `web/` app and its tests are unchanged (291 tests still pass).

**Site admin sign-in and dashboard in React (requested by the user: "admin login page is missing", after the Menu component was
added "but not in admin login"). Status: built, tested and checked in a real browser. The menu and permission editors are next.**
- Scope chosen: the login page (`/admin/login`) and the page it leads to (`/admin`, the dashboard), so signing in goes
  somewhere and the menu rule is visible. The two editors (`/admin/menus`, `/admin/permissions`) stay in `web/` for now.
- **Two apps, one bundle.** `navigation/apps.ts` defines the member app and the Site admin app (name, home and sign-in
  addresses, the pages behind sign-in); `appForPath` gives `/admin` and everything under it to the Site admin app and the rest to
  the member app (`/administrator` is the member app). `main.tsx` picks the app from the address the page was opened at and
  starts **that app's own API client and session**: different endpoints (`/v1/site/*`) and a different refresh cookie
  (`site_refresh_token`, Path=/v1/site), so the apps never see each other's session (checked in a real browser: a signed-in Site
  admin asking for `/dashboard` gets the member sign-in page, and the Site admin stays signed in). `AppProvider`/`useApp()` tell
  the guards, the page titles and the shell which app they are in. Links between the apps are full page loads, never in-app.
- **Session store.** `createSession(api, endpoints)` takes `memberEndpoints` (default) or `siteAdminEndpoints`. The signed-in
  state is a union with a `kind`: a member has an organization and memberships, the Site admin is a user and the role
  `siteadmin` with no organization and no permissions. Pages read it with `useMember()` or `useSiteAdmin()`, which throw in the
  wrong app. The Site admin has no sign-up, and if its menu cannot be read it gets a fallback (the dashboard link) instead of
  being locked out; a member whose menu cannot be read still cannot sign in.
- **Pages.** `navigation/adminPages.ts` is the Site admin's page table (lazy, like the member one) with **no permission**: the
  token of a Site admin carries none, and the server lets only a Site admin through every `/v1/site` route and checks the database
  each time. The page is bare: `AdminLoginPage` has no shell, no header, no menu and no sign-up; the dashboard (totals, a page of
  organizations with paging, the token's claims) is drawn inside the shell. `SignInForm` is shared by the member and the Site
  admin sign-in pages. Same wording for a wrong password, an unknown email and an account that is not a Site admin, as the
  server's.
- **The menu leaves out links to pages the app does not have** (`AppShell` filters the server's list by the app's page paths),
  so the Site admin's server menu (Dashboard, Menus, Permissions) shows only Dashboard until the editors are ported, and none of
  its links opens "not found". A test lists the seeded Site admin pages the client still lacks, so it must change as they arrive.
- Tests added: the Site admin session (ported from the old suite), the two apps and `appForPath`, the Site admin sign-in page
  (bare, no menu, validation, wrong password, never calls a member endpoint, redirects both ways), its route guards, the shell and
  the dashboard (totals, list, paging, empty, refresh, error, session card, menu filtering), and the seeded-pages contract for the
  Site admin. Mutation checks: a menu added to the admin login page, and the link filter removed, both make tests fail.
- Checked in a real browser (21 checks; the built app served by a throwaway copy of the API with a bootstrap Site admin on a
  private PostgreSQL cluster): redirect from `/admin` to `/admin/login`, no menu/header/sign-up there, tab named
  "Sign in · Site admin", wrong password and unknown account (same message), sign-in to the dashboard, header badge, one-link
  menu, four totals, reload restoring the session, nothing in storage, the member app not seeing the Site admin session, the
  drawer at 360 px, no horizontal scroll, a signed-in Site admin sent away from the login page, and sign-out. No script or CSP
  errors; the only console entries are the expected 401 answers.

**Sessions without refresh tokens (requested by the user: "no need of refresh token; if the token expires just redirect to the login
page; token expire time is 30 day limit"). Current; it supersedes every refresh-token entry above.**
- A sign-in is **one JWT that lasts 30 days** (`Jwt:AccessTokenDays`, default 30, allowed 1 to 90; `AccessTokenMinutes` and
  `RefreshTokenDays` are gone). Nothing is stored for a session. When the token runs out, or is refused, the person signs in
  again: the client sends them to the sign-in form. The Site admin has the same 30 days (the user's choice; one setting).
- Choices the user made, in plan mode: the token lives in an **HttpOnly cookie** (not localStorage, not memory only); **any
  permission change signs everybody out** (no silent re-issue); the two refresh tables are **dropped**; the Site admin gets
  the same 30 days.
- **Why a cookie.** A reload loses anything held in page memory, and Google sign-in ends with a redirect and puts no token in an
  address, so the token has to reach the browser as a cookie. As an HttpOnly cookie no script on the page can read it (so an
  injected script cannot steal a 30-day token), and the page holds no token at all: no Authorization header, no JWT decoding, no
  storage. `rules.test.ts` and `web/tests/rules.test.js` fail if a token, an Authorization/Bearer header or a refresh call
  appears in the page's source.
- **The cookies.** Member `access_token`, **Path=/v1**; Site admin `site_access_token`, **Path=/v1/site**; both HttpOnly,
  **Secure always**, **SameSite=Strict**, `Expires` = the token's own expiry (`AccessTokenCookie`). Sign-in, sign-up, switch-agency
  and the Google callback set the cookie and answer with `{expiresAt}` only: **the token is never in a response body or an
  address**. `POST /v1/auth/refresh` and `POST /v1/site/refresh` are gone; logout (`/v1/auth/logout`, `/v1/site/logout`, still
  `[AllowAnonymous]`) only clears the cookie at the path it was set at. `GET /v1/auth/me` and `GET /v1/site/me` gain
  `sessionExpiresAt` (the token's `exp`), which the dashboards show as "Signed in until".
- **Reading the token.** `ConfigureJwtBearerOptions` takes the token from an `Authorization: Bearer` header when the request has
  one (tools, tests, Swagger), and otherwise from the cookie **by path**: a request under `/v1/site` reads only
  `site_access_token`, every other request only `access_token`. A browser sends the member cookie to `/v1/site` too (its path is
  `/v1`), so this is what keeps the two sessions apart: a member cookie never authenticates a Site admin route, and the Site admin
  cookie is never read anywhere else. A header always wins over a cookie. Public endpoints (`[AllowAnonymous]`) answer for
  themselves whatever stale cookie the browser still holds; `PermissionVersionMiddleware` skips them.
- **A permission change.** The token still carries the permissions and `pv`. When the Site admin changes any grant every older member
  token is answered 401 `permissions_changed` ("Your permissions changed. Sign in again."); the client treats any 401 on a call that
  needs a session as the end of the session and shows the sign-in form. Nobody is renewed in the background.
- **Database.** `006_remove_refresh_tokens.sql` drops `refresh_tokens`, `site_admin_refresh_tokens`, `auth_find_refresh_token()` and
  `auth_find_site_refresh_token()` (changes existing tables, approved). `001` and `002` are released and still create them on every
  start; `006` drops them again in the same transaction, which is harmless and tested for a superuser and for an owner that
  row-level security binds (`RefreshTokenRemovalMigrationTests`). The migration count in `MigrationTests` is 11 tables. Phase 2's
  script was `007_email.sql`; the team script took 007, the team admin script 008 and the account security script 009, so it is `010_email.sql`.
- **Code removed.** `IRefreshTokenRepository`/`RefreshTokenRepository` and the Site admin pair, their models, `RefreshAsync` and
  `LogoutAsync` in both services, the refresh branch of `SwitchAgencyAsync`, `TokenResponse`, `RefreshTokenCookie`. `AuthResult` is
  now `(AccessToken, ExpiresAt)`. In both clients: `refresh()`, `getAccessToken/setAccessToken`, `onTokenRefreshed`, `jwt.js`/`jwt.ts`
  and `session.getClaims()`. The session store asks `me` (then the menu) to restore after a reload, one call after the other so
  a visitor costs one 401, and it removes the cookie again (logout) if sign-in succeeded but the page could not go on.
- **What this costs, on purpose.** (1) **No revocation:** logout clears the browser's cookie, but a copied token works until it
  expires (a test records this, `Logout_OnlyClearsTheBrowsersCookie_...`). This holds for the Site admin too; a demoted Site admin
  is still cut off at once on every `/v1/site` data route because those check `site_admins` in the database on each call.
  (2) **Roles and permissions in a token lag up to 30 days** where a policy reads them from the token (it was 15 minutes);
  `/me` and the services still read the database. Any permission change ends every session, so permissions cannot be stale
  that way; a **role or membership change can**. Changing a role or removing a member now ends that person's sessions (see Team members and invitations: `session_revocations` and
  `SessionRevocationMiddleware`); without that a removed member would have kept access for up to 30 days. (3) **Cookie authentication puts CSRF in scope** (a header token did not). SameSite=Strict keeps cross-site
  requests from carrying the cookie; a sibling subdomain counts as same-site, so no site that serves user-supplied content may
  share the parent domain. There are no state-changing GET endpoints. (4) A cookie is limited to about 4 KB; a token is well
  under 1 KB, but a very long permission list would need a rethink. (5) Everyone signs in once after this change: the old
  cookies and tokens are not read. (6) A permission edit is now disruptive (everyone signs in again); the editor says so.
- **Tests.** C#: the issuer lifetime and settings validator, the services (no refresh work any more), cookie attributes and
  expiry on every sign-in route, "the body holds no token", cookie-only and header-only authentication, garbage / expired /
  wrongly signed / wrongly named cookies refused, the member and Site admin cookies never standing in for each other (also when the browser sends
  both, and in other letter case), a stale permission version through the cookie, public endpoints ignoring a stale cookie,
  `006` on a database that held the tables. Frontend: the API client (a 401 ends the session without any renewal, the sign-in's
  own 401 does not), the session store (restore, sign-out removes the cookie, a failed start removes it), the dashboards, and the
  source rules above.

**Team members and invitations (requested by the user: "for company and marketing agency give option to add employees and login to site member"). Status: built, tested and checked in two real browsers; Google on the invite page is not built.**
- Choices the user made: an **invite link** (no email is sent, `Platform.Email` is still an empty stub: the admin copies the link and the employee sets their own password); an email that **already has an account is refused** (409 `email_taken`; joining a second organization waits for the organization switcher); scope is invite, list, cancel an invitation, **change role, remove**; removing a person's **last membership deletes their account** (a Site admin's account is kept).
- **Who.** New permission `members.manage`, granted by default only to organizationadmin of a company or an agency (a creator works alone: `team_not_available`). Whoever holds it can also make organization admins. Seeding it raised the permission version once, so everyone signed in again once.
- `007_team_members.sql` (new tables, plus owner-only policies on three existing ones, approved with the plan): `member_invitations` (a tenant table: email in lower case, role, `token_hash` unique, `invited_by`, `expires_at`, `accepted_at`, `revoked_at`; the role/model CHECK of `agency_members`, never a creator; one pending invitation per address) and `session_revocations (agency_id, user_id, valid_after)` (no foreign key on `user_id`, so a row outlives a deleted account; `app_user` has no privileges on it). The migrating role gets `owner_team_*` policies on `users`, `agency_members` and `user_identities` and `owner_manage` on the two new tables, as in `002` and `005` (a test runs everything as a non-superuser owner too). The menu page `team` (`/team`, `members.manage`, icon `users`, sort 30) is seeded like the others.
- Everything that crosses a tenant goes through narrow `SECURITY DEFINER` functions (`search_path = public, pg_temp`): `team_list_members()`, `team_change_role(user, role)`, `team_remove_member(user)`, `auth_email_registered(email)`, `invitation_find(token_hash)`, `invitation_accept(...)` (atomic: it re-checks the invitation, inserts the user, the optional identity and the membership, and marks the invitation accepted) and `session_revocations_snapshot()`; two internal helpers (`org_caller_holds`, `team_end_sessions`) are not callable by `app_user`. They raise 42501 when the caller lacks the permission and P0001 rule codes that the repository turns into `AppException`s. Change and remove take a per-organization advisory lock first and check the permission second, so two simultaneous removals cannot both pass the last-admin rule, and a caller removed a moment ago is refused.
- **Endpoints** (`MembersController`, `v1/members`, policy `members.manage`, session rate limit, no-store): `GET /` (the people and the waiting invitations), `POST invitations` (201; the answer carries the secret of the link, the only time it is ever sent), `DELETE invitations/{id}`, `PUT {userId}/role`, `DELETE {userId}`. Anonymous, credentials rate limit, the secret in the **body**: `POST /v1/auth/invitations/preview` and `POST /v1/auth/invitations/accept` (name and password; it sets the cookie and answers `{expiresAt}` like sign-in; the address is the invited one, never taken from the request). Error codes: `email_taken`, `invitation_exists`, `invitation_not_found`, `invitation_invalid` (an unknown, used, cancelled or expired invitation all answer the same), `member_not_found`, `cannot_change_self` (nobody changes or removes themselves), `last_organization_admin`, `role_not_allowed`, `team_not_available`.
- **The invitation secret.** `TokenGenerator.Create()` (43 characters of base64url); only its SHA-256 is stored. The link is `/accept-invite#<secret>`: after the `#`, so no server log or Referer holds it. The page reads it, removes it from the address bar and sends it only in the body of a POST. It lasts `Auth:InvitationDays` (default 7, allowed 1 to 30). A test captures the logs and asserts that no secret is in them.
- **Ending sessions** (what the "Sessions without refresh tokens" note below asked for). A role change or a removal writes `session_revocations.valid_after`; `SessionRevocationService` holds the rows in memory (a 30 s `SnapshotCache`, invalidated after every team change made here) and `SessionRevocationMiddleware`, after `PermissionVersionMiddleware`, answers 401 `session_ended` to a member token whose `iat` is not after `valid_after` (whole seconds) or that has no `iat`. Site admin tokens and `[AllowAnonymous]` endpoints are skipped. Costs: a person who signs in within the same second as a change is refused once (it fails closed), and another instance learns of a change within 30 s.
- **React.** The Team page (`/team`, needs `members.manage`, never mounted without it): a pill search box, filter chips ("role is Content Writer", a "+" to add one), a table with tick boxes (people and waiting invitations together; the role changed in place; Remove with an inline confirmation; Cancel invite; remove the ticked rows after one confirmation) and a **New Employee** button that opens the invite form in a modal dialog (`components/ui/Modal.tsx`: a portal, the page behind inert, Tab kept inside, Escape, × or a tap outside close it, focus returns to the button; while the link is on screen or a request is on its way Escape and the tap outside do nothing, because the link is shown once). Below 1024 px each row is a card. Layout from the user's reference screenshot (an orders table). `/accept-invite` is a public route outside the sign-in guards; `session.acceptInvitation` signs the person in as sign-up does. `rolesFor(model)` mirrors `BusinessModelRules.AllowedRoles` (a test reads the server source).
- **Palette.** The sign-in page's cream and green (`#fbfaf6`, `#1f7a55`, `#1a1d21`, with a dark variant) is now the palette of the whole React app, as tokens on `:root`, including the Site admin app. `web/` is unchanged and still has it on login and register only: the "dashboard and the site admin app still use the blue palette" note above describes `web/`.
- **Running it.** The API on 5080 serves the older `web/` app, which has no Team page (its sidebar shows a Team link that opens "not found") and no `/accept-invite`. Use the React app: `cd web-react && npm run dev` (port 5173, forwards `/v1` to the API), including for the invite link.
- Tests: the `007` migration (77 tests, once as a superuser owner and once as a limited one: isolation, each function against a non-member, a member without the permission and another tenant, the last admin under concurrency, orphan deletion, atomic accept, the race on an email), 19 end-to-end API tests (`TeamApiTests`), the services, validation and the revocation comparison (Auth.Tests, 831), the middleware, and React (320 tests in 21 files, with contracts against `PermissionKeys.cs`, `007_team_members.sql` and `BusinessModelRules.cs`). Checked in two real browsers at 360, 1024 and 1280 px and in the dark scheme: invite, copy, accept, a role change and a removal ending the other person's session, the same address invited again.
- Not built: Google on the invite page (an invited person can use only a password), an email to the invitee, an organization switcher. (Changing a password was built afterwards: see Changing your own password.) Never run against real Google, nor on PostgreSQL 16 (CI does that).
- **Name, activating by hand, deactivating, created date (requested by the user as a second slice of the Team page: "member can add, invite and he can activate it manual (by setting default password) in team page list. give button to active and deactive and delete. and also show created date in list. And while adding ask for Name also").** `008_team_admin.sql` (the user approved the two column changes: `agency_members.is_active boolean NOT NULL DEFAULT true`, an existing table, and `member_invitations.full_name`, my own table). Choices the user made: an **Activate button on invited rows** (not a second way in the form); the password is **set only, with the risk flagged** (a forced change at first sign-in was offered and not chosen); the finished invite work was **committed first** (`603097a`), this is its own commit.
- **The name.** Adding someone asks for their name (`fullName`, required, at most 200). It is stored on the invitation (`NULL` for an older one; the list then shows the address and an activation falls back to the part of the address before the `@`), shown in the list, and offered on the accept page, where the person may write it their own way.
- **Activating an invitation by hand.** `POST /v1/members/invitations/{id}/activate {password}` answers 204 with an empty body. The admin types a default password (10 to 128 characters); `TeamService` hashes it (`IPasswordHasher`) and `team_activate_invitation` creates the user (name from the invitation), the active membership, and marks the invitation used so its link stops working, in one transaction under the per-organization lock. An expired invitation can be activated (the admin is the one acting). `email_taken` when the address got an account meanwhile; `invitation_not_found` for a used, deleted or other organization's invitation. The password is never echoed, logged or kept in the page (a test captures the logs). **Accepted risk:** the admin knows that password until the person changes it themselves under Account (see Changing your own password; the dialog says so). A forced change at first sign-in was offered and not chosen.
- **Deactivating and activating a member.** `PUT /v1/members/{userId}/status {isActive}` answers 204. Deactivating keeps the row, the role and the data, **ends the person's sessions at once** (`team_end_sessions`, the same revocation as a role change) and refuses their sign-in with 403 `account_deactivated`. That is said only after the password (or Google) is right, so it tells nobody that an address is a member; `SignInToMembershipAsync` skips inactive memberships (so does the Google path, which ends there), `SwitchAgency` refuses one, and `me` leaves it out. Activating needs no new session end: the person signs in again (a sign-in within the same second as the deactivation is refused, the known cost). Nobody can deactivate themselves, and **the organization keeps one ACTIVE organization admin**: the same rule now also governs demoting and deleting an admin, and an inactive admin is not counted.
- **Functions.** A later script may not change what an earlier one re-creates, so 008 adds `team_members_v2`, `team_change_role_v2`, `team_remove_member_v2`, `team_set_member_active`, `team_activate_invitation` and `invitation_find_v2`, and takes `EXECUTE` on the 007 ones (`team_list_members`, `team_change_role`, `team_remove_member`) away from `app_user`: the `_v2` ones call them. Every one checks `org_caller_manages()` (holds `members.manage` **and is an active member**, so a deactivated admin with a token issued a moment ago is refused), takes the per-organization lock first, and raises the 007 rule codes plus `invitation_not_found`. The 007 migration tests were ported to the `_v2` names and a second part of the same base class (`TeamAdminMigrationTests.cs`) runs on both a superuser owner and a limited one.
- **React.** The list shows the name over the address, a **Created** column (when the person was added, or the invitation made; an invited row also says when its link ends), and per row **Activate / Deactivate / Delete**: Deactivate and Delete ask inline first, Activate on an inactive person is one tap, Activate on an invited row opens a dialog (`ActivateDialog`, built on `Modal`) for the default password. The bar for ticked rows says **Delete selected**; Inactive is a status and a filter. The table has six columns, so it is a card list below 76 rem (1216 px). "Delete" is what the user called remove: the same endpoints as before (a deleted member's account goes when they belong to no other organization; a deleted invitation is revoked). A sign-in refused as deactivated says so on the sign-in form.
- Tests for this slice: Auth.Tests 865 (service, requests, sign-in of a deactivated member), the 008 migration (on both owners: columns, every function against a non-member, a member without the permission, a deactivated admin and another tenant, the last active admin, activation including two at once), `TeamApiTests` end to end (name, activation then sign-in with the default password, a deactivated member signed out and refused and let back in, a log capture that no password is logged), the endpoint tests, and React 347. Checked by mutation (the caller-must-be-active check, the session end on deactivation, the sign-in refusal) and in two real browsers: add with a name, activate by hand, the employee signing in with the default password, deactivate (signed out, told why), activate, delete, 360 / 1100 / 1280 px.

**Changing your own password (requested by the user: yes to "a change-password screen", chosen over Google on the invite page; the answer to the default-password risk of the Team slice above). Status: built, tested and checked in two real browsers.**
- Choice the user made: **change password only, not forced at first sign-in** (a must-change flag on `users` was offered and not chosen). Nothing here changes an existing table: `009_account_security.sql` adds one function.
- **Endpoint.** `POST /v1/auth/change-password {currentPassword, newPassword}`, for a signed-in member, on the **credentials rate limit** (it checks a password, so it shares the budget of sign-in and sign-up). Both passwords travel in the body only and are never logged. Answers 200 `{expiresAt}` and sets a **fresh cookie** for the session that asked, as sign-in does.
- **Rules (`AuthService.ChangePasswordAsync`).** The current password is checked again. A wrong one is a **400 field error on `currentPassword`, never a 401**: the web client reads any 401 on a signed-in call as the end of the session. The new one is 10 to 128 characters and not the one in use (`newPassword`). An account with no password (a Google account) answers 409 `no_password`. The write is compare-and-set on the stored hash (409 `password_changed` if it changed in the meantime). The membership and the permissions are read before anything is written, as wherever a token is made; a deactivated member is refused.
- **Other sessions end.** In one transaction the API updates the user's own row (the `self_access` policy lets `app_user` change only that row) and calls `auth_end_own_sessions()` (`SECURITY DEFINER`, acts for `app_user_id()` only), which writes a `session_revocations` row for each organization the person belongs to, then `SessionRevocationService.Invalidate()`. `valid_after` is **one second before now**: a token states when it was issued in whole seconds, so with "now" the fresh token issued right after the change would be void as well. Cost: a session started within the last second before the change can survive it (a person takes longer than that), and the API and the database are assumed to agree on the time to about a second, as every revocation already assumes. A row only moves forward (`GREATEST`).
- **React.** `/account` (members only; behind sign-in, needs no permission, is not a menu page): the change-password form (current, new, the new one again; the repeat is checked in the page and never sent; fields are cleared on success; password-manager hints; a hidden username field). Reached from an **Account** link at the bottom of the sidebar and of the drawer (`AppShell`, members only: the Site admin app has none). The API serves `/account` with the shell on a reload (`MemberAppPages`). The default-password dialog of the Team page now says the person can change it under Account.
- Tests: Auth.Tests 889 (the service, the request), the migration on both owners (every membership, only forward, a user required, the application role changes only its own row and cannot write revocations), `AccountApiTests` end to end (two devices, the older sessions void and the fresh one kept, the new password signs in and the old one does not, a wrong current password is a 400 and changes nothing, a Google account, nothing logged), the rate limit shared with sign-in, React 370. Checked by mutation (the verification, the one second back, the session ending) and with two real browsers.
- Not built: a forced change at first sign-in, setting a first password for a Google account, a password change for the Site admin, an email to the person when it happens.
