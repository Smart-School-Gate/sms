# School deployment and database boundary

One school is one Neon project, one database, and one API deployment. The shared codebase can include optional modules at build time. This document does not authorize creating a project, a role, or a migration. Do not use the live demo-booking database, `neondb`, or `postgres`.

## 1. Selected architecture

- Each school gets its own Neon project. Do not put two schools on one branch.
- The school database name is neither `neondb` nor `postgres`. The API and the migrator refuse those names.
- The running API uses `DATABASE_URL` for `smart_school_app` only. That role is not a superuser and does not have `BYPASSRLS`.
- Schema changes use `MIGRATION_DATABASE_URL` for the project owner. The migrator does not fall back to `DATABASE_URL`. The owner URL must never be the API's `DATABASE_URL`.
- `EXPECTED_DATABASE_NAME` is not a secret. It must equal `current_database()` before the API serves, before `provision-admin`, and before `dotnet run -- migrate`.
- `dotnet ef` design-time tools always use a local placeholder. They ignore `DATABASE_URL`. There is no hidden switch that points them at a school database.
- `IncludeHomework=true` adds the homework registration probe at build time. It does not add homework tables or a business API.
- Cloud provisioning and the first migration stay manual. They require a separate approval for that project and database name.

The API does not revoke `CONNECT` on other databases, and startup does not require that revoke. Changing `neondb`, `postgres`, or a live database is outside a single-school deployment. Cluster-wide isolation is therefore not claimed.

Set `LOGIN` and a password on `smart_school_app` outside the application, with SQL, not through the Neon Console, CLI, or API. Those tools add `neon_superuser` membership, which startup rejects.

## 2. What the code isolates

The API process holds one connection string for its lifetime. No request header selects a database. School rows inside that database are additionally filtered by forced row-level security. That filter does not replace a separate Neon project. Local tests prove role and database checks on `127.0.0.1`. They do not prove that two Neon projects are isolated.

## 3. Configuration keys the code actually reads

The web host reads the database URL only from the process environment, through `PostgresConnectionStrings.RequireFromEnvironment`. A `DATABASE_URL` value in `appsettings.json`, user secrets, or the command line is not the connection string.

| Key | Required | Where a real value belongs |
| --- | --- | --- |
| `DATABASE_URL` | Yes | Process environment or the server secret store. Not in `appsettings.json`. |
| `EXPECTED_DATABASE_NAME` | Yes | Process environment. Non-secret. Must exactly equal `current_database()`. |
| `MIGRATION_DATABASE_URL` | Only for `dotnet run -- migrate` | Process environment. It does not fall back to `DATABASE_URL`. |
| `AUTH_SIGNING_KEY` | Yes, at least 32 characters, and it must not contain `CHANGE_ME` | Process environment. `Program.cs` reads it through configuration, and it is not in `appsettings.json`. |
| `Auth:Issuer` | No. Default in `appsettings.json` is `smart-school-api` | Override with environment variable `Auth__Issuer` |
| `Auth:Audience` | No. Default is `smart-school` | Override with `Auth__Audience` |
| `Cors:AllowedOrigins` | Empty in `appsettings.json`. Development lists the local web apps on ports 8080, 8081, and 8091, for both `localhost` and `127.0.0.1` | School-specific origins in that deployment's environment or untracked local settings |

`WebApplication.CreateBuilder` loads `appsettings.json`, then `appsettings.{Environment}.json`, then environment variables, then command-line arguments. An environment variable overrides a non-secret default. `AUTH_ISSUER` and `AUTH_AUDIENCE` are not configuration keys and are ignored.

These commands read process environment variables only. They do not read `appsettings.json`:

| Command | Variables |
| --- | --- |
| `dotnet run -- migrate` | `MIGRATION_DATABASE_URL` and `EXPECTED_DATABASE_NAME` |
| `dotnet run -- provision-admin` | `DATABASE_URL`, `EXPECTED_DATABASE_NAME`, `BOOTSTRAP_SCHOOL_NAME`, `BOOTSTRAP_ADMIN_USERNAME`, `BOOTSTRAP_ADMIN_PASSWORD`. `BOOTSTRAP_BRANCH_NAME` is optional. The login must be `smart_school_app` and must not be a superuser or have `BYPASSRLS` |

No endpoint accepts a connection string, a signing key, or a database name. `PUT /api/v1/me/context` accepts a school id only as a choice among memberships stored for the authenticated user. The student route id must match the student id already stored in the token context.

`.env.example` contains placeholders. `.gitignore` excludes `.env`, `.env.*` except `.env.example`, and `appsettings.*.local.json`.

## 4. How to configure one school

1. Create that school's PostgreSQL database and a login role that can use only that database.
2. Put `DATABASE_URL` in the school server's secret store or an untracked local environment file. Use a PostgreSQL URI or a keyword string. A non-local host must use `sslmode=require`, `verify-ca`, or `verify-full`. Set `EXPECTED_DATABASE_NAME` to that database name.
3. Put a different owner URL in `MIGRATION_DATABASE_URL` only on the machine that applies migrations. Point it at the same school database. Do not point it at `postgres` or `neondb`.
4. Generate a unique `AUTH_SIGNING_KEY` for that deployment.
5. Start with `dotnet run --launch-profile http` only for local Chrome. The profile listens on `http://localhost:5155`. The HTTPS profile also exposes `https://localhost:7154`.
6. Apply migrations only after the checks in section 5 pass: `dotnet run -- migrate`.
7. Create the first school admin with `dotnet run -- provision-admin` and the bootstrap variables. The password is not stored in source.

The new school database has not been supplied. Do not copy another environment's `DATABASE_URL` into this deployment.

## 5. Migration prerequisites

Startup does not migrate. `dotnet ef` design-time always uses the local placeholder and does not follow `DATABASE_URL`. Adding a migration does not connect to a school database.

`dotnet run -- migrate` now refuses to open a connection when the URL names `neondb`, `postgres`, or no database. It does not create a database. The previous behavior created a database named `smart_school` whenever the URL pointed at `postgres`. That is removed so a new school database cannot be created by assumption.

Before the first migration of the new database, record without printing credentials:

- Database name and host, and that the URL is not the marketing database.
- Whether `\dt` in `public` is empty or already has school tables.
- Whether `public."__EFMigrationsHistory"` exists and which `migration_id` values it contains.
- `rolsuper` and `rolbypassrls` for the role in `DATABASE_URL`. Both must be false. The API exits on startup otherwise. `EXPECTED_DATABASE_NAME` must match `current_database()`.
- Grants for that role: it needs DML on the school tables after migration, and it must not be able to connect to another school's database.
- That the owner role used for migration is not the role the API uses at runtime.

The only versioned migration is `20261025143006_InitialFoundation`. It creates the identity and academic tables and installs row-level security. It is safe to apply only to an empty database, or to a database whose history already contains this migration. It has not been verified against an unknown existing school schema.

## 6. Security and isolation

Separate databases and separate credentials are the isolation boundary between schools. Row-level security is an additional control inside one database. It does not, by itself, prove that two deployments cannot see each other.

Verified in code:

- The API role check rejects `rolsuper` and `rolbypassrls`.
- School-scoped tables have forced row-level security and an EF filter on the current school id.
- `TenantCommandInterceptor` sets `app.user_id` and `app.school_id` before each EF command.
- Student and parent routes require a bearer token. The route student id must equal the context student id. Other ids return 404.
- Login does not accept a school id. A role the account does not hold returns 403.
- There is no shared cache, file store, notification bus, or background worker that reads another database. The only hosted services are the startup role check and the database health check, both using `DATABASE_URL`.

Gaps that a connection string does not close:

- The schema can still store more than one `schools` row in one database. A deployment is supposed to contain one school. The schema does not enforce that.
- `users` and `refresh_tokens` are not under row-level security because login looks up a username before a school is known. Anyone with the application role's password can read password hashes and refresh-token hashes in that database.
- Migration SQL creates a cluster-wide role named `smart_school_app` and grants it connect on the current database. Running that migration for a second school on the same PostgreSQL cluster reuses the role and grants it the second database as well. Each school needs its own PostgreSQL instance, or a later change must give each deployment its own role name. That change is not made here.
- The database owner bypasses row-level security. `MIGRATION_DATABASE_URL` must not be used as `DATABASE_URL`.
- Tests prove these rules on a temporary local PostgreSQL cluster. They do not prove isolation of a hosted school database that has not been connected.

Tests to run after the new URL is approved, against that database only:

- Startup with the application role succeeds, and startup with its owner role fails.
- The application role's `pg_database` privileges do not include another school's database.
- A session without `app.school_id` returns no rows from `students`.
- A parent token for one child receives 404 for another child id.
- `dotnet run -- migrate` reports the expected database name and does not connect to any other name.

## 7. Modularization

The solution is already split into `SmartSchool.Core`, `SmartSchool.Contracts`, `SmartSchool.Infrastructure`, `SmartSchool.Api`, and the optional `SmartSchool.Modules.Homework` project. Build-time selection is `IncludeHomework=true` or `IncludeHomework=false`. Do not add a plugin loader. Homework business behavior is not implemented. The layout and dependency direction are in `docs/ARCHITECTURE.md`.

## 8. When the new database is supplied

The URL must arrive through the school server's secret store or an untracked local environment file. It must not be pasted into chat or committed.

Then verify section 5 before `dotnet run -- migrate`. Until that verification, the API has no approved school database. The checked-in configuration does not contain one.
