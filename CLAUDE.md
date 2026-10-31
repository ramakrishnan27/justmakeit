# CLAUDE.md

Multi-tenant email campaign platform for marketing agencies. An agency has client workspaces; each workspace has
contacts, templates and campaigns. Only the **Email** channel is built; Facebook, Instagram, SMS, WhatsApp and YouTube
exist as empty stubs so they can be added later as their own projects.

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
Platform.sln  Directory.Build.props  global.json  docker-compose.yml  .env.example
src/    Platform.Api  Platform.Auth  Platform.Email  Platform.Facebook  Platform.Instagram
        Platform.Sms  Platform.WhatsApp  Platform.YouTube  Platform.Helpers  Platform.Infrastructure
tests/  Platform.Helpers.Tests  Platform.Auth.Tests  Platform.Email.Tests  Platform.IntegrationTests
web/    vanilla JavaScript frontend (Phase 6+)
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
dotnet build Platform.sln
dotnet test  Platform.sln                              # needs Docker (Testcontainers starts postgres:16-alpine)
dotnet test  Platform.sln --filter "Category!=Docker"  # everything that does not need Docker
docker compose up -d                                   # local PostgreSQL 16 (copy .env.example to .env first)
dotnet run --project src/Platform.Api                  # http://localhost:5080, Development, applies migrations
node --test web/tests                                  # frontend unit tests (from Phase 6)
```

### Secrets (never committed)

The API refuses to start without `Jwt:SigningKey` (>= 32 bytes) and `ConnectionStrings:App`. Set them with user-secrets:

```
dotnet user-secrets --project src/Platform.Api set "Jwt:SigningKey" "<random, 32+ bytes>"
dotnet user-secrets --project src/Platform.Api set "ConnectionStrings:Migrator" "Host=localhost;Port=5433;Database=platform;Username=platform_owner;Password=<POSTGRES_PASSWORD from .env>"
dotnet user-secrets --project src/Platform.Api set "ConnectionStrings:App" "Host=localhost;Port=5433;Database=platform;Username=app_user;Password=<pick one>"
```

`ConnectionStrings:Migrator` is the database owner (runs migrations). `ConnectionStrings:App` logs in as `app_user`; the
migrator sets that role's password from this string, so it is configured in one place. Environment variables work too
(`Jwt__SigningKey`, `ConnectionStrings__App`).

## Database

- Scripts: `src/Platform.Infrastructure/Migrations/NNN_name.sql`, embedded, idempotent, applied in file-name order by
  `DbMigrator` (every start, one transaction, advisory lock). **Never edit a script that has run; add a new one.**
  `001_init.sql` is released: do not change it. `002_email.sql` is written in Phase 2.
- The API connects as `app_user` (not superuser, no BYPASSRLS, never to be given either). `NpgsqlConnectionFactory`
  sets `app.agency_id` and `app.user_id` on every connection it opens; `app_agency_id()` / `app_user_id()` read them and
  return NULL when unset or empty, so access fails closed.
- Every tenant table: `id uuid` PK, `agency_id uuid NOT NULL REFERENCES agencies(id) ON DELETE CASCADE`, `created_at
  timestamptz`, RLS **enabled and forced**, policy `tenant_isolation` (`USING` and `WITH CHECK` on `agency_id =
  app_agency_id()`). Status/type columns are `text` + `CHECK` with the lower-cased C# enum names. Foreign keys are
  indexed. snake_case names.
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
| 1 | Auth endpoints, workspaces, authorization policies, rate limiting on /v1/auth/* |
| 2 | `002_email.sql`, senders, contacts, lists, suppressions, CSV import |
| 3 | Templates + renderer, campaigns + state machine |
| 4 | Scheduler/dispatcher workers, providers, tracking endpoints, webhooks |
| 5 | Reports |
| 6 | Frontend foundation |
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
  Npgsql stays on 8.0.9 and Swashbuckle on 7.3.2 (10.x changes the OpenApi model): both run on net10, and bumping them
  waits until the Docker tests can run here.
