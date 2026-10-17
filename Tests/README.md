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

`UserDataMigrationTests` uses GUID-owned synthetic filesystem profiles to cover the copy-only install/data separation, full-table fingerprints, EF migration IDs, SQLite integrity/FKs, frozen committed WAL, existing-backup bytes, verified snapshot recovery, retry, conflicts, locks and linked backup rejection. Its synthetic install-folder deletion tests filesystem containment only; it does not execute Velopack install/update/repair/uninstall. Native lifecycle validation must use a disposable Windows profile/VM with the old data-bearing install layout, as specified in [Build and release](../Docs/Release.md).

## Scope and limits

Session imports preserve explicit game selection and unique name matches. Duplicate names can be resolved from positive Verified earnings only when exactly one rate agrees with the source duration precision and monetary rounding. Minute-only durations allow one minute of uncertainty in either direction; explicit seconds remain exact. Matching assumes Nerve pays the full recording when it crosses a cap, so positive earnings are compared against the full duration without clipping to remaining cap minutes. Missing/zero earnings, unknown rates, overlapping evidence and historical amounts that disagree with every rate remain unlinked. Reported earnings are never recalculated.

Unresolved game matches are saved with a null game link, retain their verification status, and appear in Sessions' Ambiguous filter for audited manual linking. They contribute to overall accounting, but not a particular game's totals. Recording-identity conflicts remain excluded by the existing duplicate safeguards and can be inspected in the preview's View conflicts section. Re-import cannot erase or replace an existing game association. Tests cover mixed imports, manual resolution, re-import, source precision, caps and offscreen preview/filter/editor behavior.

At a minute-only timestamp, a zero-duration, zero-earnings NotUploaded recording and a recording lasting at least a minute are distinct attempts. Import and re-import keep both, even when pasted separately. Differing earnings or game links for the same recording category, indistinguishable database matches, and collisions at precise timestamps still require resolution.

Automated coverage includes accounting and reconciliation, import parsing and idempotence, audit history, SQLite migrations and recovery, reporting, settings, update configuration, and offscreen WPF presentation. Passing tests do not verify visible interaction, OS-level durability, installed-package behavior, every legacy schema, or full startup against a real user profile.

`ApplicationBackupTests` uses GUID-owned synthetic file-backed profiles for standalone backup, committed WAL, preferences and audit preservation, restore recovery, incompatible files, locks, and interrupted retry. `ApplicationBackupViewTests` checks the Settings actions at narrow and wide widths with offscreen renderings. Manual keyboard, confirmation, close/reopen, and OS-level interruption checks still require an isolated application profile; do not rehearse restore against a personal database.

For UI or release changes, add the relevant manual keyboard, DPI, scrolling, dialog, package lifecycle, and isolated-storage checks. Do not launch the default application just to verify a build.

## Concurrent editing and imports

`ImportConcurrencyTests` uses GUID-owned, file-backed synthetic databases with separate connections. It covers competing previews/parallel commits, stale manual edits, force-verification changes, deleted targets, newly ambiguous matches, writer-lock failure/retry and unrelated writes. `InstanceIdentityTests` covers equivalent folder spellings, production compatibility identity and independent scratch profiles. `SessionsPresentationTests.ImportWhileInlineDraftIsOpenRejectsStaleSaveAndCanRestartEditing` exercises retained drafts, refreshed source data, rendered conflict feedback and restarting an edit through the actual view model. Existing import, audit, accounting, source-precision, rollback and lifetime cases remain the regression baseline.

PRO-11's local-only two-process harness bypasses application startup deliberately to reproduce writers outside the mutex. It verifies one save per competing import identity, rejected stale editors, accounting, SQLite integrity and foreign keys. Normal same-profile startup refusal reuses PRO-10's actual-app evidence; the equivalent-folder mutex bypass has separate before/after process receipts. See `.local-docs/PRO-11-CONCURRENCY.md` and `artifacts/pro11/evidence` when available. This is representative concurrency coverage, not exhaustive scheduling, multiple Windows-session certification, cross-process live refresh or visible keyboard/DPI testing.
