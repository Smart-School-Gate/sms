# Smart School API — architecture

Status: foundation split and Homework build-time selection verified on 26 October 2026 with .NET SDK `10.0.403`.

The new school database has not been supplied. This work did not connect to a school, Neon, or production database, and it did not apply a migration. School-database integration is not complete.

`IncludeHomework` includes or excludes `SmartSchool.Modules.Homework` at compile time. The default, when the property is omitted, is foundation-only. That switch was verified by separate Release publishes and by the existing API tests in both configurations. It is a module scaffold plus a build switch. It is not a homework product, and it does not isolate one school database from another.

## 1. Projects and dependency direction

`SmartSchool.slnx` contains these projects:

```text
SmartSchool.Core
SmartSchool.Contracts --> SmartSchool.Core
SmartSchool.Infrastructure --> SmartSchool.Core
SmartSchool.Modules.Homework
SmartSchool.Api --> SmartSchool.Core
             --> SmartSchool.Contracts
             --> SmartSchool.Infrastructure
             --> SmartSchool.Modules.Homework    only when IncludeHomework=true
SmartSchool.Api.Tests --> SmartSchool.Core
                       --> SmartSchool.Infrastructure
                       --> SmartSchool.Api
SmartSchool.ModuleSelection.Tests --> SmartSchool.Core
                                   --> SmartSchool.Infrastructure
                                   --> SmartSchool.Modules.Homework
                                   --> SmartSchool.Api with IncludeHomework=true
```

| Project | Responsibility |
| --- | --- |
| `src/SmartSchool.Core` | Domain entities, role names, and role parsing. No ASP.NET Core, EF Core, Npgsql, or connection strings. It does not reference Homework. |
| `src/SmartSchool.Contracts` | Auth and student request/response records, plus the FluentValidation rules that belong to those requests. No Infrastructure reference. |
| `src/SmartSchool.Infrastructure` | `AppDbContext`, the row-level session interceptor, connection-string parsing, `DatabaseSecurityCheck`, `MigrationRunner`, the design-time factory, and the existing migration. |
| `src/SmartSchool.Modules.Homework` | Optional registration scaffold. It references the ASP.NET Core shared framework only. It does not reference the API, Core, Contracts, or Infrastructure, and it has no database types. |
| `src/SmartSchool.Api` | HTTP host: endpoints, JWT, rate limiting, CORS, health checks, problem responses, and the composition root. Auth and student services stay here because they combine HTTP results, identity hashing, and persistence. |
| `tests/SmartSchool.Api.Tests` | Connection-string tests and the temporary local PostgreSQL workflow. The same project passes with `IncludeHomework` omitted and with `IncludeHomework=true`. |
| `tests/SmartSchool.ModuleSelection.Tests` | Publish-artifact checks, invalid selection values, and the enabled host's registration probe. |

There is no circular project reference. Core references no application project. Contracts does not reference Infrastructure. Infrastructure does not reference Contracts or the API.

Attendance and timetable reads stay in `StudentService` and in the foundation migration. They are not optional modules.

These types stayed in the API on purpose:

- `AuthService`, `TokenService`, `ActorLoader`, and `RequestActor` depend on ASP.NET Core identity, JWT, and the current HTTP actor.
- `ProblemResponses` and `ServiceResult` depend on MVC Problem Details.
- `ProvisionAdminCommand` is a host command, not a request endpoint.
- `DatabaseHealthCheck` is mapped by the host. It uses `AppDbContext` from Infrastructure.

## 2. Composition root

`Program.cs` reads `DATABASE_URL` from the process environment, converts it with `PostgresConnectionStrings.ToNpgsql`, and calls `AddSchoolPersistence`. That registration adds `IDbSession`, `TenantCommandInterceptor`, `AppDbContext`, and `DatabaseSecurityCheck`.

The host then registers `TokenService`, `AuthService`, `ActorLoader`, `RequestActor`, and `StudentService`. Validators are loaded from the Contracts assembly through `AddValidatorsFromAssemblyContaining<LoginRequestValidator>`. JWT, CORS, rate limiting, and the health checks stay in `Program.cs`.

When the API project is compiled with `INCLUDE_HOMEWORK`, `Program.cs` also calls `AddHomeworkModule` and `MapHomeworkModule`. Those calls are inside `#if INCLUDE_HOMEWORK`. The symbol is defined only for `SmartSchool.Api`, and only when `IncludeHomework` is exactly `true`. A foundation compile does not mention the module types, so it does not require the module project to exist.

`dotnet run -- migrate` still calls `MigrationRunner` and exits. `dotnet run -- provision-admin` still calls `ProvisionAdminCommand` and exits.

## 3. Migration discovery

The only migration is `20261025143006_InitialFoundation`. Its `[Migration]` id and `Up` SQL are unchanged. `Down` still removes the same policies, function, grants, and tables. It now drops `smart_school_app` after those tables, not before them. The model snapshot and designer now live in `SmartSchool.Infrastructure` and name the moved CLR types as `SmartSchool.Core.*`. Table and column mappings are unchanged. No replacement migration was generated.

Every runtime, migrate, provision, design-time, and test `DbContext` is configured through `SchoolDatabase`, which sets the migrations assembly explicitly:

`SmartSchool.Infrastructure`

EF Core discovers `InitialFoundation` and `AppDbContextModelSnapshot` from that assembly. The design-time factory remains `AppDbContextFactory`. It always uses the non-secret local placeholder: host `127.0.0.1`, database `smart_school`, user `smart_school_app`, password `unused`. It ignores `DATABASE_URL`, including a remote school URL. There is no design-time switch that selects a remote database. The web host does not use that placeholder.

List migrations without opening a connection:

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DATABASE_URL = $null
$env:MIGRATION_DATABASE_URL = $null
dotnet tool restore
dotnet ef migrations list --project src/SmartSchool.Infrastructure/SmartSchool.Infrastructure.csproj --startup-project src/SmartSchool.Infrastructure/SmartSchool.Infrastructure.csproj --no-connect
```

Do not run `dotnet ef database update`, `dotnet run -- migrate`, or `provision-admin` until the new database checks in section 6 are approved.

## 4. HTTP contracts

JSON property names remain camelCase. Routes, status codes, and response records were not redesigned.

| Method and path | Auth |
| --- | --- |
| `POST /api/v1/auth/login` | Anonymous, rate limited |
| `POST /api/v1/auth/refresh` | Anonymous, rate limited |
| `POST /api/v1/auth/logout` | Anonymous. Success is `204` |
| `POST /api/v1/auth/change-password` | Bearer, rate limited |
| `GET /api/v1/me` | Bearer |
| `PUT /api/v1/me/context` | Bearer |
| `GET /api/v1/students/{studentId}/profile` | Bearer, student or parent context |
| `GET /api/v1/students/{studentId}/home` | Bearer, student or parent context |
| `GET /api/v1/students/{studentId}/attendance` | Bearer, student or parent context |
| `GET /api/v1/students/{studentId}/timetable` | Bearer, student or parent context |
| `GET /health` | Anonymous. Includes a database connectivity check |
| `GET /health/live` | Anonymous. Process liveness only |
| `GET /openapi/v1.json` | Development, or when `OpenApi:Enabled` is true |

`/api/v1/fees` and `/api/v1/homework` are not implemented in either configuration. Unknown paths return the API's not-found Problem Details. The enabled build adds only the diagnostic route `GET /api/v1/modules/homework/registration`. That route is absent from the foundation build.

Access tokens remain HMAC-SHA256 JWTs with issuer, audience, signing key, lifetime, and 30 seconds of clock skew. Claims remain `sub`, `ver`, and `ctx`. Refresh tokens remain hashed, rotated, and revoked as a family. `X-School-Id` and `X-Role` are still ignored.

## 5. School deployment and configuration

Each school gets its own backend process, its own PostgreSQL database, and its own credentials. There is no school-id-to-connection-string map and no client-selected database.

| Key | Who reads it | Source that works |
| --- | --- | --- |
| `DATABASE_URL` | Web host and `DatabaseSecurityCheck`, through `PostgresConnectionStrings.RequireFromEnvironment` | Process environment variable only. `appsettings.json` cannot supply it |
| `EXPECTED_DATABASE_NAME` | Web host startup, `provision-admin`, and `MigrationRunner` | Process environment variable. Non-secret. It must equal `current_database()` exactly |
| `MIGRATION_DATABASE_URL` | `MigrationRunner` only | Process environment variable only. It does not fall back to `DATABASE_URL`. Its database name must equal `EXPECTED_DATABASE_NAME` |
| `AUTH_SIGNING_KEY` | `Program.cs` through configuration | Process environment variable. It is not in `appsettings.json` |
| `Auth:Issuer`, `Auth:Audience`, token lifetimes, login rate limit | Options binding | `appsettings.json`. Override with `Auth__Issuer`, `Auth__Audience`, and the matching `Auth__*` variables |
| `Cors:AllowedOrigins`, `OpenApi:Enabled` | `Program.cs` | `appsettings.json`, overridable by environment variables |
| `BOOTSTRAP_SCHOOL_NAME`, `BOOTSTRAP_BRANCH_NAME`, `BOOTSTRAP_ADMIN_USERNAME`, `BOOTSTRAP_ADMIN_PASSWORD` | `provision-admin` only | Process environment variables |

`AUTH_ISSUER` and `AUTH_AUDIENCE` are not read. The API does not load `.env`.

A PostgreSQL URI may contain only `sslmode` and `channel_binding`. `sslmode` accepts `disable`, `allow`, `prefer`, `require`, `verify-ca`, and `verify-full`. `channel_binding` accepts only `require`. Any other query key is rejected, and the error does not include the URL or password. `channel_binding=disable` is rejected. For a host other than `localhost`, `127.0.0.1`, or `::1`, the connection must select `sslmode=require`, `verify-ca`, or `verify-full`. Missing TLS, `disable`, `allow`, and `prefer` are rejected for those hosts. The same rule applies to a keyword connection string. Loopback may use `Ssl Mode=Disable` so the disposable local fixture can run. Database names `neondb` and `postgres` are still rejected for both runtime and migration URLs. A keyword connection string is still passed to Npgsql after that same database-name check; it is not a way to select `neondb` or `postgres`.

The runtime role created by the migration is `smart_school_app`, with `NOLOGIN`, `NOINHERIT`, and `NOBYPASSRLS`, until an administrator grants `LOGIN` outside this application. The migration role and the runtime role must be different. The runtime role must not be a superuser, must not have `BYPASSRLS`, and must not own privileges that reach another school's database. The inspected Neon owner `neondb_owner` has `BYPASSRLS`, so it cannot be `DATABASE_URL`.

Startup `DatabaseSecurityCheck` runs as the runtime login, before the process accepts requests. `EXPECTED_DATABASE_NAME` must be present and equal to `current_database()`. The login must be `smart_school_app` and must not be a superuser, must not have `BYPASSRLS`, `CREATEROLE`, `CREATEDB`, `INHERIT`, or replication, must not be a member of another role, must not own `public` tables, and must not create objects in `public`. Startup does not inspect `CONNECT` on other databases in the same cluster. That check would require changing databases this deployment is not authorized to modify, and it would claim cluster-wide isolation that has not been enforced. Startup also requires forced row-level security and only the named policies from `InitialFoundation` on the school-scoped tables. An extra policy is rejected because PostgreSQL ORs permissive policies. Each approved expression must match the session equality from that migration, not merely contain `current_setting`. `USING (true)`, `school_id = school_id`, and `school_id IS NOT NULL` are rejected. `users` and `refresh_tokens` must exist and must not have row-level security until a reviewed lookup function is deployed with the application code that calls it. `app_school_admin_exists()` must be security-definer, owned by another role, fixed to `search_path=public`, and not executable by `PUBLIC`. `users` and `refresh_tokens` must exist. They are authentication metadata and this check does not treat them as school-scoped. The log line includes the database name and not the connection string. The check does not prove that another Neon project is unreachable, and it does not prove that `PUBLIC` lacks `TEMP`.

`provision-admin` uses `DATABASE_URL` and refuses to continue unless that login is `smart_school_app`, is not a superuser, does not have `BYPASSRLS`, and the connected database matches `EXPECTED_DATABASE_NAME`.

No background worker, cache, file store, notification sender, or report database exists. Any of those added later must be reviewed for cross-school leakage before it is shared between deployments.

## 6. Dedicated school database

Read-only inspection on 27 October 2026, with no schema change: host `ep-young-sunset-b55ei02y-pooler.c-7.us-east-2.aws.neon.tech`, database `neondb`, login `neondb_owner`, PostgreSQL 18.6. That database had no application relations and no `smart_school_app` role. It remains unchanged. `neondb` is still rejected by the runtime parser and by `MigrationRunner`. The live demo-booking database was not opened.

`neondb_owner` is not a superuser. A read-only catalog check on 27 October 2026 showed `rolcreaterole`, `rolcreatedb`, `rolcanlogin`, and `rolbypassrls` are true, and the role is a member of `neon_superuser`. It can create objects in `public`. That catalog result does not prove Neon will accept `CREATE ROLE`; the statement was not executed. `neondb_owner` cannot be `DATABASE_URL` because it has `BYPASSRLS`.

The inspected database ACL grants `CONNECT` and `TEMP` to `PUBLIC` (`Tc`). `InitialFoundation` does not revoke those privileges. An administrator should revoke them on the dedicated database before the runtime login is given `LOGIN`. That revoke is not part of this change and was not run.

### Prerequisites

1. Create a new database on this Neon project, or on another dedicated project, with a name other than `neondb` or `postgres`. One school owns that database. Do not migrate `neondb`.
2. Use a migration identity that can create objects in that database and is not the API login. Do not reuse it for another school.
3. After `InitialFoundation`, set `LOGIN` and a password on `smart_school_app` outside the application. That login is `DATABASE_URL`. It must not be a superuser, must not have `BYPASSRLS`, and must not be `neondb_owner`.
4. Store the two URLs only in the process environment or a secret store. The URI form is `sslmode=require&channel_binding=require` on the dedicated database name. Do not commit either URL.

### Admin actions that are not executed here

```sql
CREATE DATABASE school_database_name;
```

Run that only in the Neon SQL editor or dashboard, as an administrator, after the database name is approved. Then point `MIGRATION_DATABASE_URL` at that database, not at `neondb`. The application will not create the database.

`dotnet run -- migrate` applies `20261025143006_InitialFoundation` and its SQL in `RlsSql`. That SQL, and only that SQL, would:

- Create `smart_school_app` with `NOLOGIN`, `NOINHERIT`, and `NOBYPASSRLS` when the role is absent.
- Grant `CONNECT` on the current database, `USAGE` on `public`, and `SELECT`, `INSERT`, `UPDATE`, `DELETE` on the foundation tables.
- Enable and force row-level security, and create the tenant policies, on every foundation table except `users` and `refresh_tokens`.
- Create `app_school_admin_exists()` as `SECURITY DEFINER`, revoke it from `PUBLIC`, and grant execute to `smart_school_app`.

Tables are owned by the migration role. `FORCE ROW LEVEL SECURITY` does not constrain a role with `BYPASSRLS`, which is why the owner must not be the runtime login.

After that migration is authorized and has succeeded, an administrator still has to run the login change outside the app:

```sql
ALTER ROLE smart_school_app WITH LOGIN PASSWORD 'set-outside-the-app';
```

Do not put that password in source or chat. `provision-admin` is a separate later approval. It inserts the first school and administrator.

### Before the first migration

`InitialFoundation` can build this schema on an empty dedicated database. It does not need a rewritten migration for that first empty apply. Do not apply it to `neondb`, to a database that already has objects, or before the migration role's `CREATEROLE` capability is confirmed. These gaps stay open after a successful apply:

- `users` and `refresh_tokens` have DML grants and no row-level policies.
- `Down` drops policies, the function, and grants, then the tables, and only then `smart_school_app`. An open session or an extra dependency can still make `DROP ROLE` fail after the tables are gone.
- The migration does not revoke `CONNECT` or `TEMP` from `PUBLIC`. A new database receives `PUBLIC` `CONNECT` and `TEMP` again. Startup does not fail because the runtime role can `CONNECT` to another database in the same cluster. Revoking those privileges is a separate operation and must not be done against an unauthorized database.
- Startup proves the expected database name, the runtime role, forced row-level security, and the session predicate inside each named policy. It does not prove that another database in the same cluster, or another Neon project, is unreachable.

Automated tests do not read an ambient school URL. `PostgresFixture` always starts PostgreSQL on `127.0.0.1` with a generated data directory, then revokes `CONNECT` on every other connectable database from `PUBLIC` and from `smart_school_app`. That revoke is test setup, not a change to `InitialFoundation`. `ApiFactory` replaces `DATABASE_URL` with that local connection before the host starts. `MIGRATION_DATABASE_URL` is read only by `MigrationRunner`, and the migration test points it at refused names on `127.0.0.1` without opening a remote connection. `dotnet ef database update` is not part of the tests. Design-time EF tools stay on the local placeholder even when `DATABASE_URL` names a school database. `ApiFactory` also sets `EXPECTED_DATABASE_NAME` from that local connection.

### Authentication tables, proposed and not applied

`users` has no `school_id`. A person is attached to a school only through `user_memberships`, and to a student or guardian row that does carry `school_id`. Login selects `users` by normalized username before `app.user_id` or `app.school_id` is set. Refresh and logout select `refresh_tokens` by token hash in that same empty session. There is no email lookup. A school-scoped policy would hide the row during those lookups. A policy that allows every row while the session is empty would expose every account. Neither policy is safe to add by itself.

That follow-up is not a migration file in this repository. Adding it would make the local fixture apply it, and the application would still query the tables directly until the login, refresh, logout, and password-change code called the functions first. The unexecuted shape, after a later approval, is:

```sql
CREATE OR REPLACE FUNCTION auth_user_by_username(p_username text)
RETURNS SETOF public.users
LANGUAGE sql
SECURITY DEFINER
SET search_path = pg_catalog
AS $$
  SELECT * FROM public.users WHERE username = p_username
$$;
REVOKE ALL ON FUNCTION auth_user_by_username(text) FROM PUBLIC;

CREATE OR REPLACE FUNCTION auth_refresh_token_by_hash(p_hash text)
RETURNS SETOF public.refresh_tokens
LANGUAGE sql
SECURITY DEFINER
SET search_path = pg_catalog
AS $$
  SELECT * FROM public.refresh_tokens WHERE token_hash = p_hash
$$;
REVOKE ALL ON FUNCTION auth_refresh_token_by_hash(text) FROM PUBLIC;
```

The application would call those functions for the unscoped lookup, then set `app.user_id` before any later read or update. Only after that code exists should forced RLS be enabled, with policies limited to `user_id = current_setting('app.user_id', true)::uuid`. Logout must set the session from the token's user before it revokes the family. This SQL has not been added to a migration and has not been executed.

`app_school_admin_exists()` is the only security-definer function today. `SECURITY DEFINER` is required: `user_memberships` has forced row-level security, and the bootstrap check runs before `app.user_id` is set. A caller without definer rights would see no memberships and could treat an existing administrator as absent. The function is SQL, `PUBLIC` cannot execute it, and its body does not concatenate caller input. Startup rejects a body that is not the fixed membership lookup. Its owner is the migration role. Because that role has `BYPASSRLS` on Neon, the function sees every membership in the connected database. That is acceptable only while the database contains one school. `search_path` remains `public` because changing it would change the `Up` SQL of `InitialFoundation`. The runtime role cannot `CREATE` in `public`, which blocks the usual shadow-table attack. A later migration, not this one, should set `search_path = pg_catalog` and qualify `public.user_memberships`. `Down` drops this function and its policies, revokes grants, drops tables, and only then drops `smart_school_app`.

## 7. Isolation gaps that remain open

| Risk | Current behavior | Still unproven |
| --- | --- | --- |
| Cross-school credentials | `neondb` and `postgres` remain rejected names. Startup does not treat `CONNECT` on another database in the same cluster as a failure | Cluster-wide isolation is not claimed. A credential for another database, or a Neon project permission, can still reach another database |
| Superuser or `BYPASSRLS` | Startup fails closed for superuser, `BYPASSRLS`, `CREATEROLE`, `CREATEDB`, `INHERIT`, replication, role membership, table ownership, and `CREATE` on `public` | Catalog flags do not prove a hosted control-plane permission |
| Policies missing or rewritten | Startup requires forced row-level security, the `InitialFoundation` policy names, and the session predicate inside each expression | `users` and `refresh_tokens` are intentionally outside that set until a reviewed authentication lookup exists |
| Several schools in one database | The schema and the local tests allow multiple `schools` rows | One deployment per school is a deployment rule, not a schema constraint |
| `users` and `refresh_tokens` | No row-level security | Safe only when the connected database contains one school |
| Client school id | Context selection checks membership and the student or guardian link | A second school row in the same database is still available to a member of that school |
| Session variable leakage | The interceptor rewrites `app.user_id` and `app.school_id` on EF commands | A raw connection can observe a pooled session value |
| Design-time tools | The factory always uses the local placeholder and ignores `DATABASE_URL` | `dotnet run -- migrate` can still target the database named by `MIGRATION_DATABASE_URL` when that name matches `EXPECTED_DATABASE_NAME` |
| Shared future services | None are implemented | Cache keys, files, queues, and reports must stay inside one deployment |

`ConnectionStringTests` does not open a connection. `Phase1WorkflowTests` starts a temporary PostgreSQL cluster with `.tools/pgsql`, on `127.0.0.1`, in a unique temp directory, and deletes it afterward. `PostgresFixture` does not read `DATABASE_URL` or `MIGRATION_DATABASE_URL`. Those tests show row-level behavior inside one local database. They do not prove that two school servers are isolated.

## 8. Homework module selection

Verified on 26 October 2026. This is compile-time project selection for one scaffold. It is not a plugin loader, and it is not the homework feature.

`Directory.Build.props` accepts only the exact strings `true` and `false` for `IncludeHomework`. The comparison is ordinal and case-sensitive, so `TRUE`, `yes`, `1`, and a value with surrounding space fail the build and the error includes the rejected value. An omitted property becomes `false`. A non-empty `SmartSchoolModules` value still fails. That old property is not an include list.

`SmartSchool.Api` has a conditional project reference:

```xml
<ProjectReference
  Include="..\SmartSchool.Modules.Homework\SmartSchool.Modules.Homework.csproj"
  Condition="'$(_IncludeHomeworkIsTrue)' == 'true'" />
```

`_IncludeHomeworkIsTrue` is true only for the exact value `true`. The same condition defines `INCLUDE_HOMEWORK` before `CoreCompile`. `Program.cs` registers the module only inside that symbol. There is no runtime feature flag, reflection scan, or directory loader. Removing the module project while `IncludeHomework=true` fails the API build, because the reference and the registration calls are real.

The module exposes one diagnostic route, `GET /api/v1/modules/homework/registration`. The response is `{"module":"homework","state":"registered"}`. It is excluded from the OpenAPI description. It is not `/api/v1/homework` and it does not read school data. `GET /api/v1/homework` stays not found in both configurations, with the existing not-found Problem Details.

An enabled build writes `bin\homework\` and `obj\homework\` instead of the foundation `bin\` and `obj\` folders. `Directory.Build.targets` drops leftover files from the foundation `obj` folder so the enabled compile does not pick them up. Publish with an explicit `-o` directory as well. Reusing one output folder for both configurations can leave a stale `SmartSchool.Modules.Homework.dll` behind.

`dotnet build SmartSchool.slnx` compiles the module project, because the solution lists it and the selection tests reference it. That is not the same as the foundation host including it. `dotnet publish` of `SmartSchool.Api` with `IncludeHomework` omitted or set to `false` does not compile, copy, or reference `SmartSchool.Modules.Homework.dll`. The enabled publish does, and the API assembly's IL calls `AddHomeworkModule` and `MapHomeworkModule`.

Homework still has no approved data model. The module has no `DbContext`, table, or migration. `InitialFoundation` is unchanged, and `SchoolDatabase.MigrationsAssembly` remains `SmartSchool.Infrastructure`. A future homework model should use its own context and migration assembly after the data requirements are approved. Do not add that schema just to demonstrate this switch.

`module-manifest.json` is not generated. The repository has no Git metadata, so a source revision cannot be recorded from this working tree.

CI should run both variants, in separate output directories:

```powershell
dotnet test SmartSchool.slnx
dotnet test tests/SmartSchool.Api.Tests/SmartSchool.Api.Tests.csproj -p:IncludeHomework=true
dotnet publish src/SmartSchool.Api/SmartSchool.Api.csproj -c Release -p:IncludeHomework=false -o artifacts/foundation-only
dotnet publish src/SmartSchool.Api/SmartSchool.Api.csproj -c Release -p:IncludeHomework=true -o artifacts/with-homework
```

Compare the folders directly. The foundation folder has no `SmartSchool.Modules.Homework.dll` and its `SmartSchool.Api.deps.json` does not name that assembly. The enabled folder contains the DLL, the deps file names it, and the running host returns 200 for the registration probe. `artifacts/` is gitignored.

## 9. Build and test

The `dotnet` command on the default `PATH` may be SDK 9. This solution requires SDK 10.

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
dotnet --version
dotnet build SmartSchool.slnx
dotnet test SmartSchool.slnx
```

Publish each variant into its own directory:

```powershell
dotnet publish src/SmartSchool.Api/SmartSchool.Api.csproj -c Release -p:IncludeHomework=false -o artifacts/foundation-only
dotnet publish src/SmartSchool.Api/SmartSchool.Api.csproj -c Release -p:IncludeHomework=true -o artifacts/with-homework
```

Run the existing host tests against the enabled compile as well:

```powershell
dotnet test tests/SmartSchool.Api.Tests/SmartSchool.Api.Tests.csproj -p:IncludeHomework=true
```

These requests must fail before compile:

```powershell
dotnet build src/SmartSchool.Api/SmartSchool.Api.csproj -p:IncludeHomework=yes
dotnet build src/SmartSchool.Api/SmartSchool.Api.csproj -p:SmartSchoolModules=Homework
```

## 10. Rollback

This refactor changes source layout only. No school database was migrated, so there is no database rollback.

To return the source to the single-project layout, restore the previous `src/SmartSchool.Api` tree, `SmartSchool.slnx`, and `docs/ARCHITECTURE.md`, and remove `SmartSchool.Core`, `SmartSchool.Contracts`, `SmartSchool.Infrastructure`, `SmartSchool.Modules.Homework`, `tests/SmartSchool.ModuleSelection.Tests`, `Directory.Build.props`, and `Directory.Build.targets`. This working tree has no Git repository, so that restore is a file restore rather than a recorded commit.

For a future deployment, roll back by publishing the previous build. Do not run a downward migration unless that migration was applied and the downward script has been reviewed for the school database.

## 11. Implemented and not implemented

Implemented:

- Core, Contracts, Infrastructure, and the API host, with the dependency direction above.
- Explicit migrations assembly `SmartSchool.Infrastructure`.
- The original `InitialFoundation` migration id and `Up` SQL. `Down` drops the runtime role after the tables.
- Existing routes, JSON contracts, authentication, and the temporary local test database.
- Compile-time inclusion and exclusion of `SmartSchool.Modules.Homework`, verified by separate publish folders and by the API tests in both configurations.
- A build error for `IncludeHomework` values other than exact `true` or `false`, and for any non-empty `SmartSchoolModules`.

Not implemented:

- Homework behavior, DTOs, tables, authorization, or `GET /api/v1/homework`.
- A homework `DbContext` or migration assembly.
- The module manifest file.
- A multi-school connection router.
- Integration of the new school database.
- Closure of the `DatabaseSecurityCheck` gaps in section 7.
- Exams, fees, transport, and library behavior.

## 12. Recommended next phase

Approve the homework data requirements before adding persistence. The next implementation should give Homework its own `DbContext` and migration assembly, registered only in the enabled build, without changing `InitialFoundation` or the foundation connection rules. `GET /api/v1/homework` should stay unimplemented until that contract is approved. The registration probe should not be turned into the business API.

Keep attendance and timetable in the foundation. Do not treat this build switch as cross-school isolation. The gaps in section 7 remain open.

The dedicated school database is not created yet. `neondb` on the inspected Neon host was empty on 27 October 2026 and was not migrated. The next remote action needs an approved database name other than `neondb` or `postgres`, confirmation that the migration role can create `smart_school_app`, and a separate runtime login. No administrator or student seed is authorized.
