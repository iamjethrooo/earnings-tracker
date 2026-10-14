# Earnings Tracker — repository instructions

## Project and documentation

Windows desktop tracker for game sessions, verified earnings, payouts, adjustments, reconciliation, and game acquisition information. The application targets `net10.0-windows` with WPF, EF Core 10/SQLite, FluentIcons.Wpf, and bundled Inter. The solution is `EarningsTracker.slnx`; tests use xUnit v3 and real isolated SQLite databases. Catalog maintainer tools use Python 3.11+ and the standard library.

Read the relevant documentation instead of duplicating it:

- [Design system](Docs/Design-System.md) — the established design document; there is no root `DESIGN_SYSTEM.md`.
- [Database and storage](Docs/Database.md) — database locations, migration boundaries, and isolation rules.
- [Build and release](Docs/Release.md) — development commands and Velopack release gates.
- [Testing and financial invariants](Tests/README.md).
- [Catalog maintenance and publication gates](Tools/CATALOG.md).

Local-only Codex audits and Phase 1–4 implementation records are preserved under `.local-docs/` when available. They are historical context; maintained requirements belong in the tracked documents above.

Architecture and conventions below describe the current implementation. Workflow and release recommendations are identified explicitly; they do not require a new architecture.

## Architecture and responsibilities

- `Models/`: EF entities, including sessions, payouts, games, catalog records, and audit history. Some models also expose display helpers; this is not a strictly separated domain layer.
- `ViewModels/`: observable page/item state, filtering, editing drafts, and presentation. Uses `INotifyPropertyChanged`, `ObservableCollection`/`BatchObservableCollection`, and `ICollectionView`.
- `Views/` and `Views/Modals/`: XAML and code-behind for interaction, page loading, dialogs, and orchestration. Dashboard, Payments (`PayoutsView`), and Statistics also own reporting/presentation state. Preserve these existing boundaries when making focused changes; do not perform a wholesale MVVM conversion.
- `MainWindow`: navigation through `NavigationPage` identifiers, game-detail entry, and modal orchestration. Route through its coordinator rather than sidebar labels or indices. Pages generally recreate on navigation; do not add blanket caching that changes refresh, subscription, or credential lifetimes.
- `Utilities/`: the actual service layer. `SessionService`, `PayoutService`, and `GameService` handle audited financial/history mutations; parsers and importers handle source input, preview, matching, and commit. `PayoutAccounting` defines shared accounting rules. Catalog services, updates, credentials, artwork/cache, settings, and presentation helpers also live here.
- `Data/`: `AppDbContext`, catalog model configuration, seed initialization, and bundled catalog/artwork. `Migrations/` contains EF migrations and the model snapshot.
- `Controls/`, `Commands/`, `Converters/`, and `Resources/`: shared WPF infrastructure, modal work/close guards, commands, binding conversion, and styling.
- There is no repository abstraction, DI container, or separate `Services/` project. Services, ViewModels, and some views use EF directly. Do not introduce extra layers merely to standardize existing code.

## C#, WPF, and asynchronous conventions

- Nullable reference types and implicit usings are enabled. Use the `EarningsTracker` namespace, PascalCase types/members, and the local file's indentation/private-field conventions. Both block and file-scoped namespaces exist; avoid mass formatting or renaming. No repository-wide formatter configuration or `global.json` is checked in.
- Preserve XAML names, binding contexts, dependent-property notifications, command availability, and local trigger precedence. Use the existing `RelayCommand` and observable-state mechanisms rather than adding an MVVM dependency.
- Use `Task`-returning operations; reserve `async void` for WPF/event boundaries and handle failures there. Publish observable collections and UI objects on their dispatcher. Keep blocking reads, parsing, imports, and artwork decoding off the UI thread using the existing patterns.
- Preserve coalesced/queued refreshes and stale-result checks. Publish complete snapshots after successful reads; on failure retain the previous snapshot, identify stale data, and expose retry. First-load failures must not present unavailable financial totals as zero.
- Scope and dispose contexts/transactions. Do not use one `DbContext` concurrently. `SessionImporter` opens contexts per operation; `PayoutImporter` owns its supplied context and is disposable. Preserve importer/dialog ownership and `DialogWork` busy/commit guards.
- Trace unexpected errors and show actionable feedback at the UI boundary. `PostCommitRefreshException` means persistence **already committed**: report a saved-but-refresh-failed warning, not a failed save, and do not repeat the write. `PayoutService.DataChanged` is published after commit; loaded views marshal notifications and unsubscribe on unload.
- Preserve explicit culture handling: USD display uses `CurrencyDisplay`/`en-US`, source parsers use their established source cultures, and editable inputs/binding converters follow their existing culture contracts. Do not reinterpret historical dates or normalize all timestamps without a scoped requirement and regression tests.

## UI and design rules

- Follow [Docs/Design-System.md](Docs/Design-System.md): charcoal surfaces, purple actions/selection, Inter typography, semantic status colors, and existing compact layouts. Reuse styles, components, tokens, and templates before adding local equivalents.
- `Resources/Styles.xaml` is the compatibility merge entry point. Keep existing resource keys available. In shared dictionaries use `DynamicResource` for tokens from sibling dictionaries and keep `BasedOn` references within the dictionary; views can use loaded `StaticResource` keys. Preserve the assembly resource URI `EarningsTracker`, matching the assembly name.
- Prefer `PageHost`, panel/heading styles, `ArtworkPresenter`, status variants, and import/feedback styles. Pages own their inset; do not add duplicate host padding. Preserve deliberate status-trigger differences across history, Dashboard, and game details.
- Keep keyboard focus, accessible names, live feedback, modal containment, and commit close protection. Bound list viewports must retain virtualization/recycling; do not wrap large lists in an unbounded outer scroller.
- Distinguish empty, filtered-empty, loading, error, and stale states. Keep prices, editions, availability, and stale-data warnings visible when simplifying provenance. Settings backup/restore, game-view defaults, and display preferences remain placeholders unless explicitly in scope.

## Financial and history integrity

- Use `decimal` for money and preserve imported precision/signs. Session earnings are nonnegative; signed bonuses/corrections are a separate concept. Never apply absolute values to stored adjustments or round invalid input simply to accept it.
- Reuse `PayoutAccounting` rather than inventing a new totals formula. Paid/Processing payments count toward paid totals; qualifying separate adjustments affect earnings without double-counting amounts included in sessions. Balance follows verified sessions' posted-date cutoff, not simply total earned minus paid. Preserve unknown posting dates and exclusion notices.
- Preserve source IDs, raw/source-value evidence, original values, reconciliation state, force-verification state, and before/after audit entries. Keep record and audit writes atomic; preserve import preview, ambiguity/conflict handling, reimport idempotence, and commit-time validation.
- Preserve nullable historical game links, stable catalog IDs, duplicate game identities/rate versions, and cap-reset history. Do not auto-reassign old sessions or recalculate stored earnings from today's game rates or prices.
- Imported payouts cannot be deleted through the existing service. Manual payouts use audited soft deletion; session deletion retains a deletion audit entry. Do not bypass these rules with direct SQL or new UI shortcuts.
- Require explicit user approval for destructive data repair, history deletion, database replacement/reset, or irreversible bulk rewrites. Normal authorized edits must continue through the existing audited paths.

## SQLite and migrations

- Follow [Database and storage](Docs/Database.md) for protected profiles, explicit test storage, migration behavior, and persistence rules. Never delete or replace a user's database, import personal records into fixtures, or bypass the audited recovery paths.
- Startup runs `Database.Migrate()` and seed initialization before constructing financial views. Seeds initialize an empty Games table; catalog import failure must not block access to financial history. Do not replace migrations with `EnsureCreated`/`EnsureDeleted` or suppress migration/model checks to make startup succeed.
- Schema changes require an actual persistence requirement. Preserve legacy columns, relationships/delete behaviors, and the payout soft-delete query filter. Add an EF migration and update the snapshot consistently; do not rewrite applied migrations. Verify fresh initialization, synthetic legacy upgrades, repeated migrations, and model/snapshot agreement through the existing persistence tests.
- EF Core Tools is referenced, but no local `dotnet-ef` tool manifest is present. Verify matching migration tooling is available rather than assuming a CLI installation or installing new tooling without considering scope.
- Tests must supply isolated `DbContextOptions<AppDbContext>` via `TestDatabase` or equivalent explicit options. They bypass production path/copy/initialization; never run destructive test setup against default contexts.
- Catalog refreshes must preserve financial/personal records, validate mappings/checksums, and keep saved data on provider failure. API keys use Windows Credential Manager; do not log/export secrets or store them in SQLite/source control. Preserve the diagnostics allowlist.

## Recommended implementation workflow

1. Inspect `git status`, affected code, callers, bindings, resources, and relevant tests/docs. Preserve unrelated modifications and untracked source; do not assume untracked files are disposable.
2. Make small, readable changes within existing boundaries. Avoid unrelated refactors, dependency upgrades, schema changes, and generated catalog churn. Treat UI-only tasks as presentation changes unless business changes are explicitly requested.
3. Complete and verify one meaningful task before proceeding. Resolve build/test failures first; add behavior-level regression tests for changed financial, persistence, import, or recovery behavior.
4. Report modifications, commands/results, warnings, limitations, and required manual checks. Update the relevant documentation when a maintained contract changes.

## Verification commands

Run from the repository root on Windows with the .NET 10 SDK and Windows Desktop runtime:

```powershell
dotnet restore EarningsTracker.slnx
dotnet build EarningsTracker.slnx --no-restore
dotnet test EarningsTracker.slnx --no-build --no-restore
dotnet build EarningsTracker.slnx --configuration Release --no-restore
dotnet test EarningsTracker.slnx --configuration Release --no-build --no-restore
```

Relevant subsets: `dotnet test EarningsTracker.slnx --no-restore --filter "Category=Regression"` or `--filter "FullyQualifiedName~PayoutAccountingTests"`. Save evidence with `--logger "trx;LogFileName=tests.trx" --results-directory Tests/TestResults`.

If a running app locks the normal output, use `--property:OutputPath=artifacts/verification/` consistently on build and test commands. Only use `--no-build` after building the same configuration/output. Do not kill a user's running app to unblock verification.

For catalog/tool changes, run `python -m unittest discover -s Tools -p "test_*.py"`. See [Tools/CATALOG.md](Tools/CATALOG.md) for offline report/package validation and its explicit review/publication gates; provider collection and public publishing are not routine test commands.

WPF tests use the `Wpf` collection/`WpfTestHost` dispatcher and actual resources; some create offscreen windows and rendered fixtures without running `App.OnStartup`. Compilation is not runtime verification, offscreen fixtures are not a complete interactive test, and in-memory migrations do not prove filesystem durability. UI changes need appropriate rendered/layout checks plus manual keyboard, DPI, scrolling, and dialog checks. Do not launch the default app merely to verify a build: startup can migrate user data. Use an explicitly isolated manual-test environment.

## Recommended release discipline for 1.0.0

- Prioritize compatibility, accounting/history integrity, upgrade safety, and offline access over architectural churn. Run Debug/Release checks and relevant migration/import regressions; record remaining manual checks before release.
- No product release version is explicitly assigned in the project. Settings shows Development build unless assembly metadata `ReleaseVersion` is supplied; the default assembly version is diagnostic information. When a release is authorized, deliberately align product metadata and assembly/package versions rather than silently declaring 1.0.0.
- Catalog snapshot versions and publication are separate from application releases. Keep the `CATALOG_PUBLISH_ENABLED` and provider/review approval gates in place; follow [Tools/CATALOG.md](Tools/CATALOG.md).
- No application installer/signing/release pipeline or coverage-percentage gate is established here. Confirm release requirements with the user when needed; do not invent packaging policies or claim automated end-to-end coverage.

Store temporary audits, phase verification reports, and implementation logs in .local-docs/. Keep only maintained technical and operational documentation in Docs/.