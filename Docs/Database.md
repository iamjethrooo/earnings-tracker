# Database and storage

Earnings Tracker stores personal sessions, payouts, adjustments, audit history, and game settings in a per-user SQLite database. The production file is `%LOCALAPPDATA%\EarningsTracker.UserData\EarningsTracker.db`. Backups and cached artwork are under `Backups` and `Artwork` in that same data folder. Unpackaged Debug uses `%LOCALAPPDATA%\EarningsTracker.Development\EarningsTracker.db`.

The Velopack package ID is `EarningsTracker.App`, with program files in `%LOCALAPPDATA%\EarningsTracker.App\current`. The executable/assembly remain `EarningsTracker.exe` / `EarningsTracker`. No supported install directory may contain or overlap a data profile. Use the default install location; do not point `Setup.exe --installto` at a data folder. Velopack repair replaces its install root and uninstall removes its contents; an application uninstall callback cannot reliably prevent that removal. See [Velopack's Windows layout](https://docs.velopack.io/packaging/operating-systems/windows) and [uninstall behavior](https://docs.velopack.io/integrating/uninstalling).

## Storage selection

- Ordinary Release without test-package metadata uses the production profile.
- Versioned Local packages and RemoteTest builds require an explicit isolated directory through `NERVE_TRACKER_DATA_DIRECTORY` before they open a database.
- Overrides must be absolute paths. Debug cannot select production storage; Release cannot select the development profile. Both reject former production/install folders. Test-package isolation rejects all protected profiles, the new install folder, and their backups.
- EF design-time commands require an explicit scratch database path and reject the protected production database filenames.
- The former `NerveEarningsTracker` directory and `nerve_earnings_tracker.db` filename remain compatibility identifiers. Do not rename or reuse them for new storage.

See `Utilities/AppDataPaths.cs` and `Utilities/UserDataMigration.cs` for enforced path rules and the current transition. `DataLocationMigration` retains the former rename procedure and its regression tests. See `Tests/README.md` for isolated test setup.

## Automatic transition from the colliding installation

Close the former tracker, install the new `EarningsTracker.App` package alongside it, then launch the new app **before uninstalling or repairing the old installation**. No folder operations or data-directory overrides are needed for ordinary Production users. Local/RemoteTest packages intentionally bypass personal-data migration and require explicit scratch storage.

On first Production launch, while holding the same production instance mutex as the former app, the tracker searches `%LOCALAPPDATA%\EarningsTracker`, `%LOCALAPPDATA%\NerveEarningsTracker`, and the executable directory for saved data. A single unambiguous source is copied; the whole installation is never moved. Both database filenames and interrupted filename/sidecar renames are recognized. Orphan/conflicting sidecars, multiple data locations, existing incomplete destinations, linked paths, active database handles, invalid SQLite, or foreign-key violations stop startup and preserve the source. An existing new database is authoritative and never overwritten or re-imported on reinstall.

The transition freezes the database and sidecars, copies them into a unique sibling `EarningsTracker.UserData.preparing-<GUID>` folder, verifies copied bytes, and creates a standalone SQLite recovery snapshot with integrity and foreign-key checks. It then creates the working database from that snapshot and copies every existing backup and artwork file with byte verification. Only the complete folder is published as `EarningsTracker.UserData`. Recovery files are `Backups\BeforeInstallSeparation\Snapshot.db` and `Original\EarningsTracker.db` plus captured sidecars. Original filenames may be normalized inside that recovery copy; source files are unchanged. No schemas, financial values, audit entries or migration IDs are rewritten by this transition. Ordinary supported EF migrations still run afterward with their existing backup rules.

The first successful transition displays the new data location. Former files remain as an additional fallback. Stop using the former app: its subsequent edits would create a separate history. The existing instance mutex prevents concurrent normal tracker launches but cannot prevent a later launch of already-distributed old code. Allow space for the retained source, raw recovery copy, standalone snapshot, working database, all backups and artwork. No automatic cleanup deletes these copies.

**Transition boundary:** an old installer/uninstaller already on disk cannot be retroactively fixed. Running it before the new app completes the copy can still delete the only old database/backups. After a successful copy, the new profile is outside the old removal root. Do not publish new-identity packages on the old update channel. This is a side-by-side installer transition, not an in-app package-identity bridge. Old shortcuts and uninstall entries are retained until separately authorized removal. For v1.1.0 personal use and limited sharing, the owner accepted completed real-account Stages 1–4A and explicitly deferred legacy removal and interrupted installer/updater fault injection on 2026-10-19. Legacy removal is not required to use the new app and remains unverified; leave the old app dormant. The migration-before-uninstall warning remains mandatory. See the current [release acceptance](Release.md) for verified scope, deferred checks and remaining release gates.

## Recovery

1. On any transition error, leave both installations and all data/preparation folders intact. Close tracker instances and database tools, resolve permissions/disk-space issues, then retry the new app. Each retry uses a new preparation folder; failed folders may contain useful verified recovery data. Never treat a failure as permission to uninstall the former app.
2. Conflicting databases, orphan sidecars and incomplete final folders require review. Do not merge histories, delete sidecars, rename folders, or select a source by newest timestamp alone. Preserve evidence and obtain explicit approval before any data repair or replacement.
3. For a recovery rehearsal, use only disposable synthetic profiles. Open the standalone `Snapshot.db` read-only and compare `PRAGMA integrity_check`, `PRAGMA foreign_key_check`, `__EFMigrationsHistory` and canonical all-table row fingerprints against the source receipt. Use SQLite's backup API to restore to a **new** scratch database; verify again. The raw `Original` copy must retain its matching WAL/journal files. Never copy a live SQLite base file alone.
4. Manual recovery or replacement outside the application's confirmed Settings workflow still needs separate owner approval with a chosen verified snapshot and a backup of the current profile first. Reinstalling the new app reuses the new profile and is not a database reset or restore procedure.

### User backup and restore in Settings

Settings can create a standalone `.db` backup in a user-selected location (defaulting to `Backups` under the active data folder). SQLite's backup API captures committed WAL transactions without copying a live base file alone. The app checks SQLite integrity, foreign keys, migration history, and required application tables before publishing the file. An existing destination is never overwritten. Keep backup files private: they contain personal financial records in unencrypted SQLite form.

The database backup includes all tables: sessions, payouts, adjustments, audit history and source evidence, games/catalog data, bookmarks/ownership, rates, and saved settings/preferences. This includes the linked Steam account ID, cached owned app IDs and sync timestamps in `CatalogSettings`. Steam ownership is combined with manual ownership when games load; it does not overwrite `GameOwnership`, purchase costs or history. Cached `Artwork` is excluded because it can be fetched again. IsThereAnyDeal and Steam API keys are stored separately in Windows Credential Manager and are excluded; they remain unchanged by restore. Relink Steam if a restored account has no matching saved key. Existing backup files are not nested into a new backup.

Restore accepts a selected `.db` with the current migration history, validates it, and asks for explicit confirmation before scheduling replacement. The app then closes. On the next launch, under the normal instance mutex and before constructing views, it revalidates the staged file, creates a verified snapshot of the current database in `Backups/BeforeUserRestore`, and uses SQLite's backup API to replace the database. It checks the result before opening financial views and reports the recovery path on success. A failure or interrupted operation stops startup and retains the pending file and recovery snapshots for review; it never removes the current database to make a restore work. Close other database tools and resolve locks or file permissions before retrying. No automatic cleanup deletes recovery snapshots. Restoring an older application schema or a future migration history is rejected; use a matching application version and a reviewed recovery path instead.

## Schema and startup

Startup applies EF Core migrations and initializes bundled game/catalog data before constructing financial views. The current baseline is `20261013055107_InitialCreate`. Preserve migrations and the model snapshot together; never replace migrations with `EnsureCreated` or `EnsureDeleted` in application startup.

New development and scratch profiles initialize from migrations and bundled assets; they do not copy a personal production database. Production startup uses the copy-only transition above. Database migration and recovery must use validated application paths and backups; never treat personal databases as fixtures.

Automated persistence tests must pass explicit isolated `DbContextOptions<AppDbContext>` using `TestDatabase` or equivalent. Do not run destructive initialization against the default context.

`20261108103539_AddGameMultiplayerMetadata` adds nullable `GameMetadata.SupportsMultiplayer`,
`MultiplayerVerifiedAt`, and a `MultiplayerProvenance` text column. Existing rows start unknown
(null support/date, empty evidence); the migration does not infer support or modify financial,
ownership or history records. Catalog import saves the support claim and dated evidence together.
Legacy catalogs without these optional fields remain valid and represent unknown support.

### Startup failure and interrupted-upgrade recovery

Startup keeps financial views closed until migrations, game seeding and default settings succeed. Missing or malformed game seed assets, unsupported migration history, unavailable storage and write failures display **Unable to start tracker** and exit with code 1 after dismissal. Preserve the profile; correct the installation assets, free space or file access, then retry. A seed failure may leave the successfully migrated schema in place; it does not require deleting the database. Existing games do not require re-seeding. Bundled catalog import failure is traced separately and must not block access to financial history.

An interrupted EF/SQLite upgrade can retain a row with `Id=1` in `__EFMigrationsLock`, even when SQLite rolled back its uncommitted schema changes. Startup reports the retained lock instead of waiting indefinitely when it is already present. It never clears the lock automatically: another migration tool may still own it. Close tracker instances and migration tools, preserve the complete database/sidecar set and `Backups`, and request reviewed recovery. Do not delete a lock row/table, sidecars or migration history merely to force startup. Any real lock repair or database replacement requires explicit owner approval after verifying that no writer remains.

For an interrupted upgrade rehearsal, inspect the preserved working copy and the `Backups/before-migration-*.db` snapshot read-only. Check integrity, foreign keys, migration IDs and all-table fingerprints. Restore a verified snapshot with SQLite's backup API into a **new disposable profile**, then launch with a child-process-only `NERVE_TRACKER_DATA_DIRECTORY` override. Compare records and audit entries again after shutdown/restart. Restoration to a new scratch profile does not authorize replacing a real profile. Never apply a partially completed schema or stamp migration IDs by hand.

Copy-only relocation publishes the final profile only after preparation succeeds. Interrupted `.preparing-<GUID>` folders remain evidence; a retry starts a new preparation and leaves the source intact. An incomplete final folder still requires review rather than automatic cleanup. Do not uninstall or repair the former installation before the new app starts successfully.

PRO-10's disposable filesystem rehearsal covers actual app startup/restarts, missing/malformed seeds, unsupported history, file locks, a read-only database, bounded SQLite page-limit failure, abrupt synthetic upgrade/relocation exits and recovery. It does not certify physical power-loss durability, an actually full volume, arbitrary ACL policies, all historical schemas, or native installer/update lifecycle behavior. PRO-21's later real-account Stages 1–4A satisfied its owner-revised lifecycle criteria; do not repeat them. Installed synthetic Settings restore on the exact v1.1.0 candidate remains NOT VERIFIED and owner-approved deferred for personal use. Other release checks and audience boundaries remain governed by [Build and release](Release.md). Detailed earlier receipts are local-only under `.local-docs/PRO-10-STARTUP-RECOVERY.md` and `artifacts/pro10/evidence`.

For the migration and recovery audit trail, consult the local-only `.local-docs/DATA-LOCATION-MIGRATION.md`, `.local-docs/DATABASE-STORAGE-ISOLATION.md`, and `.local-docs/LEGACY-DATABASE-RECOVERY.md` when available.

## Proposed catalog identity persistence (PRO-44)

This is a design for owner review, not current storage behavior or an applied
migration. The representation, examples and transition fixtures are specified
in the [shared catalog identity contract](../Tools/CATALOG.md#proposed-shared-identity-contract-pro-44).

`Game.Id` remains the local earning/history identity. `GameMetadata.GameId`
is its primary/foreign key and `GameMetadata.CatalogId` has a unique index.
Provider IDs are references, never keys for sessions or a reason to merge
duplicate/rate-version games. Preserve existing links through renames,
archiving, removal and reintroduction. Initial linking retains the current
unambiguous name/alias and reviewed occurrence rules. Catalog import creates
or deletes no earning games and reassigns no session history.

The minimum proposed persistence is one versioned JSON document in existing
`CatalogSettings`, key `catalog.steam-identities`. It contains the active
schema, snapshot Version, exact payload SHA-256, and a CatalogId-keyed dictionary
of the complete snapshot membership and SteamIdentity objects, including
unmatched catalog entries and explicit empty/withdrawn decisions. Store no
account IDs or user library in this catalog-owned document. Validate the
document against the enclosing snapshot before committing. Retain it across
restart and Settings backup/restore; it is not a disposable download cache.
Keep provenance with the mapping rather than trying to reconstruct it from
mutable StoreOffers or the embedded ITAD map.

This reuses `CatalogSettings` because matching loads a bounded catalog snapshot
and needs no relational provider query. No EF schema change is necessary for
this decision, and no migration is authorized by PRO-44. Do not add a SteamId
column to Game or overload ItadId. If implementation establishes a need for
relational indexing, reconsider that specific persistence requirement with an
actual migration and persistence regressions rather than silently changing
the design.

Activation must write the full mapping document, imported metadata and
`catalog.version`/`catalog.published` in the same existing catalog transaction.
An absent schema-2 mapping document on a profile marked as schema 2 is an
unavailable/corrupt state; it must not silently enable the legacy inference.
Do not mutate an already activated snapshot under the same version/hash.
PRO-51 must retain snapshot membership even for unmatched CatalogIds: current
`CatalogService.Import` skips unmatched entries and leaves metadata for omitted
entries, so current GameMetadata alone cannot implement removal semantics.
Retain omitted local metadata links for history, but use active document
membership to disable their Steam mappings. Removal is not permission to
delete personal or financial rows.

Observed Steam matches remain derived from active catalog mappings and the
account-scoped `steam.library` snapshot already stored in CatalogSettings.
Invalidate derived results on mapping activation/withdrawal/removal/rollback
and account/cache changes; publish after commit on the UI dispatcher. Never
rewrite GameOwnership, purchase costs, library AppIDs, rates, caps, session
links or audit history to save a derived match. Failed activation leaves both
the previous complete catalog and account snapshot available. A mapping change
alone requires no Steam request or credential access.

PRO-52's previous-known-good artifact must retain the complete catalog-owned
mapping/membership document and its version/hash with compatible metadata.
Rollback restores those together through the catalog transaction; it must not
restore the whole database or revert manual ownership or the newer account
snapshot. Existing timestamp/version guards need a reviewed catalog-only
activation path; no bypass is implemented here. Old app execution against a
v2-activated profile remains unsupported pending a verified compatibility
guard under PRO-51. Existing Settings database restore rules remain unchanged.

## Concurrent edits and import previews

### Session payout assignments

Sessions imported now default to **Current payout**, the open cycle awaiting a payment.
In Sessions, select a recording, choose **Edit session**, and change **Payout assignment**
to Current payout or a specific Paid/Processing Payment. Dates and earnings remain independent
of this choice. New-session import previews also offer a payment choice for the whole new batch;
re-importing an existing session preserves its assignment. Pending sessions first verified now
join the open cycle unless they already have an explicit assignment.

For example, a November 5 session verified after a November 5 payment stays in Current payout.
An existing historical session excluded by the former date cutoff can be moved to Current payout
without changing its November 5 posted date.

New Paid/Processing payments, and records first changing into those statuses, offer a coverage
preview. Review the checked sessions before saving. Candidates are verified sessions currently
owed with known posted dates no later than the payment day. Unchecked candidates remain current;
later verifications are not consumed by that payment. Differences between the selected session
earnings and payment amount are shown for review, without rewriting either amount. Multiple imported
payments default to covering each candidate once, in payment-date order; selecting one session
for two payments rejects the batch. Sessions being force-verified/updated by the same transaction
import are excluded from its coverage choices. Internal service callers that omit a coverage
preview retain their immediate-write contract and do not capture explicitly current sessions.

`Session.HasExplicitPayoutAssignment` distinguishes new/edited allocation from legacy inference.
An explicit assignment with a null `AssignedPayoutId` means Current payout; a non-null ID identifies
its Payment. The original `PayoutId` is retained separately for historical source evidence.
`AddSessionPayoutAssignments` adds these fields and a restrictive settlement foreign key. Existing
rows default to legacy inference, preserving the previous posted-day cutoff until explicitly
changed or selected in a payment coverage preview. No historical bulk reassignment runs at startup.

Amount Owed sums verified current sessions with known posted dates. Explicitly assigned sessions
are excluded while their Payment is Paid/Processing and visible. If that payment is hidden or
becomes ineligible, those earnings return to Current payout while their membership is retained;
restoring eligibility settles them again. Unknown posting dates remain excluded and explained,
including after assignment edits. Total Earned, Total Paid, adjustment reconciliation and signs
retain their existing definitions. Dashboard and Statistics use the same session membership rule
for Current payout and continue to chart by recording date.

Assignment edits and payment coverage changes include before/after assignment values in the
session audit. Coverage commits recheck candidate records and payment eligibility inside the
SQLite transaction; changed candidates require a new preview. Payment, assignment and audit writes
commit together, and refresh notifications follow commit. Original source links/text are retained.

Normal application launches acquire the existing per-folder `Local\Nerve.EarningsTracker-…` mutex before database initialization. Equivalent absolute folder spellings, including a trailing separator, share an identity. Production also retains its former compatibility identity when selected through an explicit Release override. Different scratch profiles have separate locks. The mutex is scoped to a Windows session; external database tools and processes that bypass startup do not participate in it. It is not an exclusive SQLite file lock or a live cross-process refresh channel.

Session and payout imports re-run the existing matching rules for rows being applied inside their SQLite write transaction. Commit requires the same new/update targets and unchanged persisted target values as the preview, including force-verification targets. A new duplicate, competing match, deletion or edited target rejects the batch before writes. Return to input and preview again to review the latest history. Unrelated records do not invalidate a preview. Payout previews use detached rows so EF tracking cannot hide intervening edits. Input validation, ambiguity/conflict handling, source evidence, audited writes and commit-before-notification behavior remain in place.

Session drafts and payout editors retain their initial persisted values. Their save paths compare those values inside the service transaction and reject a stale save. Sessions also detect a changed row when refreshing an open inline draft; cancel the draft and reopen Sessions before editing again. Payout conflicts require closing the editor and reopening Payments. Internal callers that use the update services without an expected state intentionally retain their existing immediate-update contract; UI editors supply that state.

SQLite serializes these write transactions. A competing writer can still cause a busy/locked failure; it must not produce a partial financial or audit save. These checks do not prevent arbitrary external SQL, external record deletion or history repair, detect an edit that restores every observed value, or automatically merge drafts. They require no schema/concurrency-token migration. Representative file-backed, parallel-connection and separate-process evidence is recorded in `.local-docs/PRO-11-CONCURRENCY.md` when available.
