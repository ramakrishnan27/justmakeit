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
| Frontend | Vanilla JavaScript (ES modules, Web Components), HTML, CSS. No framework, no jQuery, no CSS framework, no build step |
| Auth | HS256 JWT access tokens (15 min, in browser memory only) + rotating refresh tokens (HttpOnly cookie scoped to /v1/auth) |
| Email sending | `IEmailProvider`: fake provider for development, Amazon SES for production |
| Tests | xUnit, Testcontainers.PostgreSql, Microsoft.AspNetCore.Mvc.Testing, NSubstitute; frontend: `node --test` |

Allowed packages: Dapper, Npgsql, Microsoft.AspNetCore.Authentication.JwtBearer, Swashbuckle.AspNetCore, CsvHelper,
AWSSDK.SimpleEmailV2, and the test packages above (plus Microsoft.NET.Test.Sdk and xunit.runner.visualstudio, which xUnit
needs to run). **Ask before adding any other.**

## Layout and the dependency rule

```
JustMakeIt.sln  Directory.Build.props  global.json  docker-compose.yml  .env.example
src/    Platform.Api  Platform.Auth  Platform.Email  Platform.Facebook  Platform.Instagram
        Platform.Sms  Platform.WhatsApp  Platform.YouTube  Platform.Helpers  Platform.Infrastructure
tests/  Platform.Helpers.Tests  Platform.Auth.Tests  Platform.Email.Tests  Platform.IntegrationTests
web/    vanilla JavaScript frontend: site admin login and dashboard so far (see Decisions log); more in Phases 6-7
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
node --test "web/tests/**/*.test.js"                     # frontend unit tests (Node 22+ takes a glob, not a folder)
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

`PLATFORM_TEST_POSTGRES` must name a server that holds no real data: the migration creates the cluster-wide role
`app_user` and the tests create and drop databases. Without Docker, `initdb`/`pg_ctl` from any local PostgreSQL install can
start a private cluster on a spare port for this.

`ConnectionStrings:Migrator` is the database owner (runs migrations). `ConnectionStrings:App` logs in as `app_user`; the
migrator sets that role's password from this string, so it is configured in one place. Environment variables work too
(`Jwt__SigningKey`, `ConnectionStrings__App`).

## Database

- Scripts: `src/Platform.Infrastructure/Migrations/NNN_name.sql`, embedded, idempotent, applied in file-name order by
  `DbMigrator` (every start, one transaction, advisory lock). **Never edit a script that has run; add a new one.**
  `001_init.sql`, `002_site_admin.sql` and `003_business_models.sql` are released: do not change them. Phase 2 writes
  `004_email.sql`. Every script runs on every start, so a later one may never change what an earlier one re-creates (for
  example the return type of a function that `CREATE OR REPLACE` makes again): add a new function instead.
- The API connects as `app_user` (not superuser, no BYPASSRLS, never to be given either). `NpgsqlConnectionFactory`
  sets `app.agency_id` and `app.user_id` on every connection it opens; `app_agency_id()` / `app_user_id()` read them and
  return NULL when unset or empty, so access fails closed.
- Every tenant table: `id uuid` PK, `agency_id uuid NOT NULL REFERENCES agencies(id) ON DELETE CASCADE`, `created_at
  timestamptz`, RLS **enabled and forced**, policy `tenant_isolation` (`USING` and `WITH CHECK` on `agency_id =
  app_agency_id()`). Status/type columns are `text` + `CHECK` with the lower-cased C# enum names. Foreign keys are
  indexed. snake_case names. `agencies` and `agency_members` also carry `business_model` (see Business models and roles).
- Work that must span agencies (workers, login, refresh) uses narrow `SECURITY DEFINER` functions that return only ids
  or the one row asked for. Never grant BYPASSRLS.
- Dapper rules: row models are **classes with settable properties** (underscore matching does not apply to constructor
  parameters, so no positional records). Write enums with `.ToDb()` (Dapper ignores enum type handlers). Write jsonb with
  `JsonbParameter`. `DateTimeOffset` is handled by `DateTimeOffsetTypeHandler`. Explicit column lists, parameterised SQL only.

## Working rules

- C#: file-scoped namespaces, `sealed` by default, constructor injection, async with a `CancellationToken` on every I/O
  method, no static mutable state, no `async void`.
- Controllers are thin: validate, call one service method, return. Business rules live in services, SQL in repositories.
  Services throw `AppException` subclasses (400/401/403/404/409); middleware turns them into problem responses and never
  returns stack traces. Validate DTOs at the controller and re-check rules in the service.
- Structured logging. Never log passwords, tokens, full email bodies, or paths that carry tokens (/t/c/{token}, /u/{token}).
- Read configuration lazily (options pattern), not while registering services, so test overrides apply.
- Tests: every service rule has a unit test; every query on a tenant table has an integration test that connects as
  `app_user` and proves isolation. Tests are independent: one PostgreSQL container per run, a fresh database per test class.
  Tests that need Docker carry `[Trait("Category", "Docker")]`.
- Frontend: no `innerHTML` with API data; email previews in a sandboxed iframe; 360 px first; every drag action has a tap
  alternative; tokens only in memory; never queue or send a campaign while offline.
- Ask before: changing an existing table, adding a package, weakening a security rule.
- Do not: use EF Core, a frontend framework or JS build tool, edit an applied migration, give `app_user` BYPASSRLS,
  commit secrets, write logic in the five channel stubs.

## Build process

Build one phase at a time and **stop for approval** after each. At the end of a phase: `dotnet build`, `dotnet test`
(and `node --test` for frontend phases), commit as `phase N: <short summary>`, then report what was built, what changed
or was skipped, and any open question.

| Phase | Scope |
|---|---|
| 0 | Foundations: this file, compose, Helpers, Infrastructure, Api host + /health, test projects. **Done** |
| 1 | Auth endpoints, workspaces, authorization policies, rate limiting on /v1/auth/*. **Done** |
| 2 | `004_email.sql` (was 002; `002_site_admin.sql` and `003_business_models.sql` took those numbers), senders, contacts, lists, suppressions, CSV import |
| 3 | Templates + renderer, campaigns + state machine |
| 4 | Scheduler/dispatcher workers, providers, tracking endpoints, webhooks |
| 5 | Reports |
| 6 | Frontend foundation. **Started early:** site admin login and dashboard (API in `/v1/site/*`, pages in `web/`); the rest (workspace switcher, agency login and register, PWA) is still to do |
| 7 | Frontend email screens |
| 8 | CI, README, seed data, security review |

## Decisions log

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
  later, see Business models and roles. The policy names stayed.)* The **fallback policy requires a
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
  `003` and it is now `004_email.sql`.
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
- Open: no endpoint adds or removes members yet, so "the last organizationadmin cannot be removed" is a rule for when they
  are built. A creator will need a way to connect their own accounts later; `CanManage` is admin-only, so that needs its own policy.
