# Automated tests

The test project uses xUnit v3, the Visual Studio test runner, and real SQLite databases. It targets `net10.0-windows` because it references the WPF application. Run on Windows with the .NET 10 SDK and Windows Desktop runtime.

## Run

From the repository root:

```powershell
dotnet test EarningsTracker.slnx
dotnet test EarningsTracker.slnx --configuration Release
dotnet test EarningsTracker.slnx --filter "Category=Regression"
```

For a saved report:

```powershell
dotnet test EarningsTracker.slnx --logger "trx;LogFileName=tests.trx" --results-directory TestResults
```

The first restore downloads test packages; subsequent runs can use `--no-restore`. Test output and TRX reports are ignored by Git.

## Database isolation

`TestDatabase` owns an SQLite `:memory:` connection for each fixture, enables foreign keys, and applies the real application migrations. Tests pass explicit `DbContextOptions<AppDbContext>` so they bypass default path selection, legacy copying, and production initialization. File-backed tests use unique scratch paths. The test assembly also sets a process-only scratch data directory for incidental default contexts and artwork caches.

Tests do not start `App.OnStartup`, open the user's database, call `EnsureDeleted`, or disable migration checks. Persistence and migration tests use synthetic fixtures only. See [Database and storage](../Docs/Database.md) for profile isolation and migration rules.

## Scope and limits

Automated coverage includes accounting and reconciliation, import parsing and idempotence, audit history, SQLite migrations and recovery, reporting, settings, update configuration, and offscreen WPF presentation. Passing tests do not verify visible interaction, OS-level durability, installed-package behavior, every legacy schema, or full startup against a real user profile.

For UI or release changes, add the relevant manual keyboard, DPI, scrolling, dialog, package lifecycle, and isolated-storage checks. Do not launch the default application just to verify a build.
