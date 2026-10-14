# Earnings Tracker

> Storage: Debug uses `%LOCALAPPDATA%\EarningsTracker.Development\EarningsTracker.db`; ordinary Release uses `%LOCALAPPDATA%\EarningsTracker\EarningsTracker.db`. Packaged test builds require scratch storage. See [Database and storage](Docs/Database.md).

Windows desktop earnings tracker, currently supporting only the Nerve gaming rewards platform. This is an independent project and is not affiliated with, endorsed by, or sponsored by Nerve or any other company, platform, organization, or rights holder.

## Build from source

Windows with the .NET 10 SDK and Windows Desktop runtime is required. Open `EarningsTracker.slnx` or run from the repository root:

```powershell
dotnet restore EarningsTracker.slnx
dotnet build EarningsTracker.slnx --no-restore
dotnet test EarningsTracker.slnx --no-build --no-restore
```

The source includes the complete v1 baseline migration, bundled catalog/artwork, Inter font, and isolated tests. Local provider caches and credentials are not needed to build or run the tests. Maintainer tests use Python 3.11+ and its standard library: `python -m unittest discover -s Tools -p "test_*.py"`.

Launching the application can migrate the current user's database. Use explicitly isolated storage for manual testing; do not launch it merely to verify a build. Velopack bootstrap, update UI, and separate Local/RemoteTest/Production update sources are implemented. Ordinary builds default to Local with no GitHub application update source. See [Database and storage](Docs/Database.md) and [Build and release](Docs/Release.md) for the supported boundaries and Phase 5 gates.

Fresh databases use `20261013055107_InitialCreate` and initialize bundled games/catalog plus default settings, with no personal records. See [Database and storage](Docs/Database.md) for migration boundaries. Detailed Phase 1–4 evidence and planning notes remain in the ignored local `.local-docs/` directory when available.

For EF tooling, run `dotnet tool restore`, then supply an absolute scratch path, for example:

```powershell
dotnet ef migrations has-pending-model-changes -- --database C:\path\design-time.db
```

The local EF tool is pinned to `10.0.11`. Design-time commands require an explicit path and reject both `EarningsTracker.db` and the former `nerve_earnings_tracker.db` filename; ordinary application startup continues to use per-user storage. See [Database and storage](Docs/Database.md).

## Local installer

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Release.ps1 -Version 1.0.0 -UnsignedLocal
```

This restores the pinned Velopack CLI/SDK `1.2.161`, publishes Windows x64 self-contained with runtime `10.0.12`, validates private-data exclusions, and creates unsigned limited-test artifacts under a new `artifacts` directory. It never publishes to GitHub or resets data. Broad public distribution requires signing and catalog review. The committed icon can be regenerated from its SVG with `python Tools/generate_icon.py`. See [Build and release](Docs/Release.md) for Phase 5 gates.

## Games catalog

Games supports reviewed artwork, genres, store offers, Philippine prices, PC subscription filters, personal ownership/costs, and exchange-rate-based break-even estimates. Earnings remain in USD. Catalog updates preserve financial and personal data and work from saved snapshots offline; optional IsThereAnyDeal keys enable manual live price refresh.

IGDB metadata is fetched once into a reusable maintainer cache and bundled with artwork. Initial dated prices also ship with the app; users only need an optional price API key if they want live updates. Publishing uses committed data and requires no provider credentials or scheduled refreshes.

The bundled 579-entry catalog contains 564 reviewed identities and genre records, 563 artwork records, 485 records with offers or reviewed source links, and 542 edition-matched price-provider IDs. All 506 active identities are resolved; 15 archived entries have no metadata and require no resolution. The remaining artwork placeholder is OPTCG Simulator, and unavailable purchase prices stay unknown. See the [coverage report](Data/catalog-report.json) and [reviewed resolutions](Data/catalog-resolution-report.json). The bundle contains 55 reviewed Philippine PC Game Pass matches, with no EA Play/Ubisoft subscription candidates. See the [subscription review report](Data/subscription-report.md). Public snapshot publishing remains disabled until provider permissions and release review are approved. See [catalog maintenance and publishing](Tools/CATALOG.md).

Select PC Game Pass in Settings → Subscriptions if your membership is active. Settings also manages the optional IsThereAnyDeal API key, automatic catalog checks, manual refresh, update status, and diagnostics export. Currency & region shows the supported Philippine PC catalog, PHP game prices, USD earnings, and saved exchange rate. Backup & restore, game-view defaults, and display preferences are placeholders. Playable now uses owned games, free games, and current verified Game Pass availability. Subscription records expire from Playable now after seven days without verification. A credential-free maintainer collector and weekly GitHub Actions review workflow prepare updates; the workflow becomes active after pushing it to the default branch with Actions enabled. Desktop users receive approved published snapshots through the existing in-app refresh.

Artwork appears in Games, Dashboard recordings, and Sessions. Games rows have a quick ownership toggle that preserves saved purchase costs. Displayed sources, price comparisons, and store filters use Steam, Epic Games, Ubisoft Connect, EA app, Microsoft Store, GOG, Battle.net, PC Game Pass, Roblox, and reviewed official project websites. Reseller offers remain stored as provider provenance but are excluded from the user interface.

## Tests

The solution includes an isolated xUnit test project for accounting, imports, reconciliation, migrations, and audit history. See [Tests/README.md](Tests/README.md) for setup, commands, coverage, and manual-check limits.
