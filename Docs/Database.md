# Database and storage

Earnings Tracker stores personal sessions, payouts, adjustments, audit history, and game settings in a per-user SQLite database. The production file is `%LOCALAPPDATA%\EarningsTracker\EarningsTracker.db`. Unpackaged Debug uses `%LOCALAPPDATA%\EarningsTracker.Development\EarningsTracker.db`.

## Storage selection

- Ordinary Release without test-package metadata uses the production profile.
- Versioned Local packages and RemoteTest builds require an explicit isolated directory through `NERVE_TRACKER_DATA_DIRECTORY` before they open a database.
- Overrides must be absolute paths. Debug cannot select production storage; Release cannot select the development profile; test-package isolation also rejects protected profile directories and their backups.
- EF design-time commands require an explicit scratch database path and reject the protected production database filenames.
- The former `NerveEarningsTracker` directory and `nerve_earnings_tracker.db` filename remain compatibility identifiers. Do not rename or reuse them for new storage.

See `Utilities/AppDataPaths.cs` and `Utilities/DataLocationMigration.cs` for the enforced path rules. See `Tests/README.md` for isolated test setup.

## Schema and startup

Startup applies EF Core migrations and initializes bundled game/catalog data before constructing financial views. The current baseline is `20261013055107_InitialCreate`. Preserve migrations and the model snapshot together; never replace migrations with `EnsureCreated` or `EnsureDeleted` in application startup.

New development and scratch profiles initialize from migrations and bundled assets; they do not copy a personal production database. Production startup preserves the established profile transition behavior. Database migration and recovery must use the existing validated application paths and backups; never treat personal databases as fixtures.

Automated persistence tests must pass explicit isolated `DbContextOptions<AppDbContext>` using `TestDatabase` or equivalent. Do not run destructive initialization against the default context.

For the migration and recovery audit trail, consult the local-only `.local-docs/DATA-LOCATION-MIGRATION.md`, `.local-docs/DATABASE-STORAGE-ISOLATION.md`, and `.local-docs/LEGACY-DATABASE-RECOVERY.md` when available.
