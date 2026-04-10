# Idea: Multi-RDBMS Support

**Context:** TeamWare currently uses SQLite as its sole database backend. This document explores adding support for additional RDBMS systems, starting with PostgreSQL, and providing a data migration path from SQLite to the new target.

---

## Problem Statement

SQLite is an excellent zero-configuration database for small teams and single-server deployments. However, as TeamWare deployments grow, SQLite's limitations become relevant:

- **Concurrent write throughput** — SQLite uses a single-writer model. Under moderate concurrent write load (e.g., multiple agents polling and updating tasks simultaneously), write contention can become a bottleneck.
- **Operational tooling** — Production PostgreSQL (or SQL Server) deployments benefit from mature backup strategies, replication, monitoring, and point-in-time recovery that SQLite lacks.
- **Organizational requirements** — Some teams have existing PostgreSQL or SQL Server infrastructure and prefer to consolidate data management rather than introduce a standalone SQLite file.

### Current Architecture

TeamWare uses Entity Framework Core with the SQLite provider (`Microsoft.EntityFrameworkCore.Sqlite`). Key characteristics:

- **No raw SQL anywhere** — All data access goes through LINQ-to-Entities. No `FromSqlRaw`, `ExecuteSqlRaw`, or inline SQL strings exist in the codebase.
- **Code-first migrations** — All schema changes are managed through EF Core migrations (currently 20+ migration files under `TeamWare.Web/Data/Migrations`).
- **Single connection string** — `Program.cs` calls `options.UseSqlite(...)` with a connection string from `appsettings.json`.
- **`DateTime.UtcNow` throughout** — All timestamp assignments use `DateTime.UtcNow` consistently. No `DateTime.Now` or `DateTime.Today` usage in service code.
- **String comparisons via `.ToLower().Contains()`** — Search methods in `TaskService`, `AdminService`, and `UserDirectoryService` normalize strings in LINQ queries using `.ToLower().Contains()`.
- **Enum-to-string conversions** — Several entities store enums as strings (e.g., `ProjectStatus`, `ActivityChangeType`, `NotificationType`, `InvitationStatus`) via `.HasConversion<string>()`.
- **In-memory SQLite for tests** — `TeamWareWebApplicationFactory` creates an in-memory SQLite connection (`DataSource=:memory:`) for integration tests.
- **Hangfire uses `MemoryStorage`** — The background job system is not database-backed, so no Hangfire schema compatibility concern exists.

---

## Approach: Provider-Switchable DbContext via Configuration

EF Core is designed to be provider-agnostic. The general approach is:

1. Add the target database provider NuGet package (e.g., `Npgsql.EntityFrameworkCore.PostgreSQL`).
2. Read a configuration value that determines which provider to use.
3. Call the appropriate `Use*` method (`UseSqlite`, `UseNpgsql`, etc.) based on that configuration.
4. Generate a separate set of migrations per provider, or use a single migration set with conditional logic.
5. Provide a one-time data migration utility to copy data from SQLite to the new target.

### Why PostgreSQL First

- Open-source and widely deployed; commonly paired with .NET applications.
- Excellent EF Core provider (`Npgsql.EntityFrameworkCore.PostgreSQL`) that is mature and actively maintained.
- Rich date/time, JSON, and full-text search capabilities that could benefit future features.
- Readily available as managed services (Azure Database for PostgreSQL, AWS RDS, etc.) and easy to self-host.

---

## Known Nuances and Open Questions

While EF Core abstracts most database differences, several areas require careful handling. Each is presented as a question for the team.

---

### Nuance 1: DateTime Handling

SQLite stores `DateTime` values as text strings (ISO 8601 format by default in the EF Core SQLite provider). PostgreSQL has native `timestamp` and `timestamptz` types with proper timezone semantics.

The codebase consistently uses `DateTime.UtcNow` (not `DateTime.Now`), which is good. However:

- EF Core's SQLite provider stores `DateTime` as `TEXT`. The Npgsql provider maps `DateTime` to `timestamp without time zone` by default (Npgsql 6.0+ issues warnings when mixing `DateTimeKind.Utc` with `timestamp without time zone`).
- Npgsql recommends mapping UTC timestamps to `timestamptz` (timestamp with time zone) for correct semantics.

> **Question:** Should we:
> - (A) Explicitly configure all `DateTime` properties to map to `timestamptz` on PostgreSQL via Fluent API or a convention? This is the PostgreSQL best practice.
> - (B) Accept the default `timestamp without time zone` mapping and suppress Npgsql warnings? Simpler but semantically less correct.
> - (C) Consider migrating to `DateTimeOffset` throughout the model (substantial refactor but most portable)?

> **Decision: Option A.** Explicitly configure all `DateTime` properties to map to `timestamptz` on PostgreSQL. This follows the PostgreSQL best practice for UTC timestamps and avoids Npgsql warnings.

---

### Nuance 2: String Comparison and Case Sensitivity

SQLite's `LIKE` operator is case-insensitive for ASCII characters by default. PostgreSQL's `LIKE` is case-sensitive.

The codebase uses `.ToLower().Contains()` in LINQ queries for search (e.g., `TaskService.SearchTasks`, `AdminService.GetUsersAsync`, `UserDirectoryService`). EF Core translates `.ToLower()` to the SQL `LOWER()` function, which works on both SQLite and PostgreSQL. However:

- On PostgreSQL, using `LOWER()` prevents index usage. PostgreSQL has `ILIKE` (case-insensitive LIKE) which is more idiomatic and can use trigram indexes.
- `EF.Functions.ILike()` is available in the Npgsql provider but not in the SQLite provider. Using it would require provider-conditional code.

> **Question:** Should we:
> - (A) Keep the current `.ToLower().Contains()` pattern — it works on both providers, even if not optimal for PostgreSQL? Simplest approach.
> - (B) Use `EF.Functions.ILike()` on PostgreSQL and fall back to `.ToLower().Contains()` on SQLite? Requires a provider-aware abstraction or conditional branching.
> - (C) Accept the current approach for now and add PostgreSQL-specific search optimizations (e.g., trigram GIN indexes, `ILIKE`) as a later enhancement?

> **Decision: Option A.** Keep the current `.ToLower().Contains()` pattern. It works correctly on both providers. PostgreSQL-specific search optimizations (e.g., `ILIKE`, trigram indexes) can be explored as a future enhancement.

---

### Nuance 3: Migration Strategy

EF Core migrations are provider-specific — the SQL generated by `dotnet ef migrations add` depends on which provider is configured at migration-generation time. A migration generated for SQLite will produce `TEXT` column types; one generated for PostgreSQL will produce `text`, `integer`, `timestamp with time zone`, etc.

> **Question:** How should we manage migrations across providers?
> - (A) **Separate migration folders per provider** — e.g., `Migrations/Sqlite/` and `Migrations/Postgres/`. Each provider gets its own migration history. This is the EF Core-recommended approach for multi-provider scenarios.
> - (B) **Single migration set targeting one provider, with `MigrationBuilder.ActiveProvider` checks** — more fragile and harder to maintain as the schema grows.
> - (C) **Use `EnsureCreated()` for non-SQLite providers** and skip migrations entirely for new installs (only use migrations for upgrades). Not recommended for production.

> **Decision: Option A.** Use separate migration folders per provider (e.g., `Migrations/Sqlite/` and `Migrations/Postgres/`). This is the EF Core-recommended approach and keeps each provider's migration history independent.

---

### Nuance 4: Boolean Handling

SQLite has no native `BOOLEAN` type; EF Core stores `bool` properties as `INTEGER` (0/1). PostgreSQL has a native `boolean` type. EF Core handles this transparently, but existing data in SQLite (0/1 integers) must be correctly converted during data migration.

> **Question:** Is this a concern the data migration tool should explicitly handle, or will a straightforward row-by-row EF Core copy (read from SQLite context, write to PostgreSQL context) handle the conversion automatically?

> **Decision:** Handle the details at implementation time. PostgreSQL accepts character representations of `'0'` for `false` and `'1'` for `true` with its boolean type, and EF Core mediates the conversion during a row-by-row copy. Specific edge cases will be addressed during implementation.

---

### Nuance 5: Auto-Increment / Identity Columns

SQLite uses `AUTOINCREMENT` on `INTEGER PRIMARY KEY` columns. PostgreSQL uses `IDENTITY` columns or `SERIAL` (deprecated). EF Core handles this per-provider, but during data migration, inserting rows with explicit ID values into PostgreSQL requires temporarily enabling identity insert or resetting sequences afterward.

> **Question:** For data migration, should we:
> - (A) Preserve original IDs — insert with explicit IDs and reset PostgreSQL sequences after migration? Preserves foreign key relationships naturally.
> - (B) Let PostgreSQL assign new IDs — requires remapping all foreign keys during migration. Significantly more complex.

> **Decision: Option A.** Preserve original IDs. External links and task comments referencing IDs must remain valid after migration. The migration tool will insert with explicit IDs and reset PostgreSQL sequences to `MAX(Id) + 1` afterward.

---

### Nuance 6: String Length Enforcement

SQLite does not enforce `VARCHAR(N)` length constraints; all text is stored as `TEXT` regardless of declared length. PostgreSQL enforces `varchar(N)` constraints strictly. If existing SQLite data contains values that exceed the `HasMaxLength()` declarations in the model (unlikely but possible if constraints were added after data was written), the PostgreSQL insert will fail.

> **Question:** Should the data migration tool include a validation pass that checks string lengths before inserting into PostgreSQL? Or is this an acceptable risk given that the application has always enforced `HasMaxLength()` in the model?

> **Decision:** No validation pass needed. The `HasMaxLength()` constraint has always been in place in the model — no data has ever been written without it. This is an acceptable risk.

---

### Nuance 7: Connection String and Provider Configuration

The application needs a way to determine which provider to use at startup. Options:

> **Question:** How should the provider be selected?
> - (A) **Dedicated configuration key** — e.g., `"DatabaseProvider": "Sqlite"` or `"DatabaseProvider": "PostgreSQL"` in `appsettings.json`, alongside provider-specific connection strings.
> - (B) **Connection string convention** — infer the provider from the connection string format (e.g., `Data Source=` implies SQLite, `Host=` implies PostgreSQL). Fragile and implicit.
> - (C) **Separate `appsettings.{Provider}.json` profiles** — use environment-specific configuration files. Aligns with ASP.NET Core configuration layering.

> **Decision: Option A.** Use a dedicated configuration key (`"DatabaseProvider": "Sqlite"` or `"DatabaseProvider": "PostgreSQL"`) in `appsettings.json`, alongside the appropriate connection string.

---

### Nuance 8: Data Protection Keys

ASP.NET Core Data Protection (used by Identity for cookie encryption, anti-forgery tokens, etc.) stores keys to the file system by default. This is provider-independent. However, some deployments store data protection keys in the database. If TeamWare ever adds database-backed data protection key storage, it would need provider-aware configuration.

> **Question:** Is this a concern for the initial implementation, or should it be deferred? Currently, TeamWare uses the default file-system key storage.

> **Decision: Deferred.** This is not a concern for the initial implementation. TeamWare uses file-system key storage, which is provider-independent. Database-backed data protection key storage can be revisited if needed in the future.

---

### Nuance 9: Test Infrastructure

The integration test factory (`TeamWareWebApplicationFactory`) creates an in-memory SQLite database. If multi-provider support is added, the test suite could:

- Continue using in-memory SQLite for fast tests (current behavior).
- Add an optional test configuration to run the same test suite against a real PostgreSQL instance for provider-specific regression testing.

> **Question:** Should the initial implementation include a PostgreSQL test configuration, or is SQLite-only testing sufficient for CI? PostgreSQL-specific tests could be a follow-up or a separate CI pipeline stage.

> **Decision:** Stick with in-memory SQLite for testing. The integration test suite will continue using `TeamWareWebApplicationFactory` with in-memory SQLite. PostgreSQL-specific test configurations can be added as a future enhancement or separate CI pipeline stage.

---

### Nuance 10: Data Migration Tool — Scope and UX

A one-time data migration tool is needed to copy data from an existing SQLite database to a new PostgreSQL database. This could be:

> **Question:** What form should the migration tool take?
> - (A) **CLI tool** — A standalone console application or a `dotnet tool` that reads from SQLite and writes to PostgreSQL. Run once, outside the web application.
> - (B) **Admin UI page** — A page in the TeamWare admin panel that triggers migration. Convenient but risky if the migration is long-running.
> - (C) **Built-in migration command** — A custom `IHostedService` or startup hook that detects a "migration mode" configuration flag and runs the copy on startup.
> - (D) **Script-based** — Provide SQL scripts or `pg_dump`/`pgloader` instructions rather than a custom tool. Least development effort but least user-friendly.

> **Decision: Option C.** Use a built-in migration command — a custom `IHostedService` or startup hook that detects a "migration mode" configuration flag and runs the data copy on application startup.

---

### Nuance 11: Encrypted Fields

The `AgentConfiguration`, `AgentRepository`, and `AgentMcpServer` entities contain encrypted fields (e.g., `EncryptedRepositoryAccessToken`, `EncryptedCodexApiKey`, `EncryptedClaudeApiKey`, `EncryptedAccessToken`, `EncryptedAuthHeader`, `EncryptedEnv`). These are encrypted by `AgentSecretEncryptor` using ASP.NET Core Data Protection.

Since Data Protection keys are machine/deployment-specific, encrypted values from one deployment cannot be decrypted in another unless the same Data Protection key ring is shared. During data migration:

- If migrating on the **same machine** (SQLite to local PostgreSQL), the Data Protection keys remain valid and encrypted fields can be copied as-is.
- If migrating to a **different server**, the encrypted fields become unreadable. The migration tool would need to either decrypt-then-re-encrypt (requiring access to the source key ring), or flag these fields for manual re-entry.

> **Question:** Should the data migration tool:
> - (A) Copy encrypted fields as-is (works for same-machine migration; document the key-ring requirement)?
> - (B) Provide a decrypt-and-re-encrypt option that requires both the source and target Data Protection key rings?
> - (C) Clear encrypted fields during migration and require the admin to re-enter secrets after migration?

> **Decision: Option A.** Copy encrypted fields as-is, assuming same-machine migration where the Data Protection key ring remains valid. **Documentation must explicitly state** that the migration assumes the same machine and Data Protection key ring. Cross-machine migration requires manual re-entry of secrets.

---

### Nuance 12: Future Providers

If PostgreSQL is the first additional provider, others may follow (SQL Server, MySQL/MariaDB).

> **Question:** Should the architecture be designed with a generic "provider plugin" pattern from the start (e.g., a factory method that registers the correct provider and migration assembly), or should we build for just SQLite + PostgreSQL and generalize later?

> **Decision:** Build for SQLite + PostgreSQL only. Do not over-architect with a generic provider plugin pattern. If additional providers (SQL Server, MySQL/MariaDB) are needed in the future, the architecture can be generalized at that time.

---

## Implementation Sketch

This section outlines a possible implementation at a high level. Actual work items would be defined in a specification and implementation plan after the team resolves the questions above.

### 1. NuGet Package Addition

Add `Npgsql.EntityFrameworkCore.PostgreSQL` to `TeamWare.Web.csproj`.

### 2. Provider Selection in `Program.cs`

```csharp
var provider = builder.Configuration.GetValue<string>("DatabaseProvider") ?? "Sqlite";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    switch (provider.ToLowerInvariant())
    {
        case "postgresql":
            options.UseNpgsql(connectionString);
            break;
        case "sqlite":
        default:
            options.UseSqlite(connectionString);
            break;
    }
});
```

### 3. Migration Assemblies

Create separate migration folders:
- `Data/Migrations/Sqlite/` (move existing migrations here)
- `Data/Migrations/Postgres/` (generate fresh migrations against PostgreSQL)

Configure the migration assembly in the provider switch:
```csharp
options.UseNpgsql(connectionString, o => o.MigrationsAssembly("..."));
```

### 4. `appsettings.json` Changes

```json
{
  "DatabaseProvider": "Sqlite",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=TeamWare.db"
  }
}
```

For PostgreSQL:
```json
{
  "DatabaseProvider": "PostgreSQL",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=teamware;Username=teamware;Password=secret"
  }
}
```

### 5. Data Migration Tool (CLI)

A console application or command-line mode that:
1. Opens a read-only SQLite `ApplicationDbContext`.
2. Opens a write PostgreSQL `ApplicationDbContext`.
3. Ensures the PostgreSQL schema is created (applies migrations).
4. Copies data table-by-table in dependency order, preserving IDs.
5. Resets PostgreSQL sequences to `MAX(Id) + 1` for each table.
6. Validates row counts match.

### 6. Documentation

- Update `README.md` with PostgreSQL setup instructions.
- Document the `DatabaseProvider` configuration key.
- Provide data migration instructions.

---

## Affected Areas

| Area | Impact |
|------|--------|
| `Program.cs` | Provider selection logic |
| `appsettings.json` | New `DatabaseProvider` key, provider-specific connection strings |
| `TeamWare.Web.csproj` | New NuGet package reference |
| `Data/Migrations/` | Reorganize into provider-specific folders |
| `ApplicationDbContext.OnModelCreating` | Possible provider-conditional configuration (e.g., `timestamptz` for PostgreSQL) |
| `TeamWareWebApplicationFactory` | Possibly add PostgreSQL test support |
| `SeedData` | Should work unchanged (pure EF Core) |
| `README.md` | Setup documentation |
| Services with `.ToLower().Contains()` | May benefit from PostgreSQL-specific optimization later |

### Areas NOT Affected

- **All service classes** — Pure LINQ-to-Entities; no raw SQL.
- **All controller/view code** — No database-specific logic.
- **MCP tools** — Operate through service layer; unaffected.
- **CopilotAgent** — Communicates via MCP/HTTP; does not use the database directly.
- **Hangfire** — Uses in-memory storage; no database dependency.
- **SignalR hubs** — No direct database access.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| DateTime conversion issues during data migration | Medium | Medium | Validate timestamps in migration tool; all source data is UTC |
| String length violations on PostgreSQL | Low | Low | Validation pass in migration tool |
| Sequence reset errors after ID-preserving migration | Low | High | Test migration tool thoroughly; reset sequences programmatically |
| Encrypted field portability across machines | Medium | Medium | Document key-ring requirement; offer clear-and-re-enter option |
| Migration set divergence over time | Medium | Medium | CI pipeline that generates and validates migrations for both providers |
| Performance difference in search queries | Low | Low | `.ToLower().Contains()` works on both; optimize later if needed |

---

## Next Steps

1. ~~**Team reviews this document** and provides feedback on the open questions (Nuances 1-12).~~ **Done.**
2. ~~**Answers are documented inline** in this idea document via comments or direct edits.~~ **Done.** All 12 decisions recorded.
3. **Create a formal specification** based on the resolved decisions.
4. **Break down implementation** into phased work items.
5. **Implement, test, deploy** incrementally.

---

**Document Version:** 1.1
**Date:** 2025-07-18
**Status:** Decisions Recorded — Ready for Specification
