# AppDbContext SQLite startup design

## Goal

Register `AppDbContext` in the web application with SQLite backed by a local `smartops.db` file, and ensure the database is created automatically when the application starts.

## Current context

- `SmartOps.Infrastructure\Data\AppDbContext.cs` already defines the EF Core model, seed data, and the `Transaction -> Customer` foreign key.
- `SmartOps.Web\Program.cs` currently registers Razor components only and does not register `AppDbContext`.
- `SmartOps.Web\appsettings.json` does not yet define any database configuration.
- `SmartOps.Infrastructure` already references the EF Core SQLite provider.

## Chosen approach

Use configuration-driven registration from `SmartOps.Web\appsettings.json`, with a default SQLite connection string of `Data Source=smartops.db`, and run `Database.EnsureCreated()` during startup inside a scoped service resolution.

This keeps the database file name explicit and easy to change later without touching code, while still matching the requested default behavior.

## Design details

### Configuration

- Add a `ConnectionStrings` section in `SmartOps.Web\appsettings.json`.
- Define `DefaultConnection` as `Data Source=smartops.db`.

### Service registration

- In `SmartOps.Web\Program.cs`, add the EF Core imports needed for `AppDbContext` and SQLite.
- Register `AppDbContext` with `builder.Services.AddDbContext<AppDbContext>(...)`.
- Read the connection string from configuration and pass it to `UseSqlite(...)`.

### Startup initialization

- After `builder.Build()`, create a service scope from `app.Services`.
- Resolve `AppDbContext` from the scoped provider.
- Call `Database.EnsureCreated()` before the middleware pipeline is configured.

This ensures the SQLite database file and schema exist before the application begins serving requests. The existing `HasData(...)` seed entries will be applied when the database is first created.

### Error handling

- Do not swallow startup database errors.
- If SQLite configuration is invalid or database creation fails, application startup should fail clearly so the problem is visible immediately.

### Testing and verification

- Build the solution after the changes.
- Run the web application once and confirm startup completes with the SQLite database configured.
- Confirm that `smartops.db` is created locally on first startup.

## Scope

Included in scope:

- `SmartOps.Web\Program.cs`
- `SmartOps.Web\appsettings.json`

Out of scope:

- Migrations
- Additional repositories or services
- Entity or seed data changes
- Alternate database providers
