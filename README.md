# nerve-earnings-tracker

## Build from source

Windows with the .NET 10 SDK and Windows Desktop runtime is required. Open `nerve-earnings-tracker.slnx` or run from the repository root:

```powershell
dotnet restore nerve-earnings-tracker.slnx
dotnet build nerve-earnings-tracker.slnx --no-restore
dotnet test nerve-earnings-tracker.slnx --no-build --no-restore
```

The committed source includes the complete v1 baseline migration, bundled catalog/artwork, Inter font, and isolated tests. The historical development migration chain is preserved in commit `89feb15`. Local provider caches and credentials are not needed to build or run the offline tests. Maintainer tests use Python 3.11+ and its standard library: `python -m unittest discover -s Tools -p "test_*.py"`.

Launching the application can migrate the current user's database. Use a disposable Windows profile for manual testing; do not launch it merely to verify a build. The [v1.0.0 release plan](Docs/VELOPACK_PLAN.md) documents the separately approved database reset and packaging work; Velopack is not integrated yet.

Fresh databases use `20261013055107_InitialCreate` and initialize bundled games/catalog plus default settings, with no personal records. Old development databases are archived rather than upgraded by this baseline; do not open an archived database with the rebased application or use an old development executable against the new database. See the [Phase 1 record](Docs/PHASE1_REBASELINE_APPROVAL.md).

For EF tooling, run `dotnet tool restore`, then supply an absolute scratch path, for example:

```powershell
dotnet ef migrations has-pending-model-changes -- --database C:\path\design-time.db
```

The local EF tool is pinned to `10.0.11`. Design-time commands require an explicit path and reject the normal `nerve_earnings_tracker.db` filename; ordinary application startup continues to use per-user storage.

## Games catalog

Games supports reviewed artwork, genres, store offers, Philippine prices, PC subscription filters, personal ownership/costs, and exchange-rate-based break-even estimates. Earnings remain in USD. Catalog updates preserve financial and personal data and work from saved snapshots offline; optional IsThereAnyDeal keys enable manual live price refresh.

IGDB metadata is fetched once into a reusable maintainer cache and bundled with artwork. Initial dated prices also ship with the app; users only need an optional price API key if they want live updates. Publishing uses committed data and requires no provider credentials or scheduled refreshes.

The bundled 579-entry catalog contains 564 reviewed identities and genre records, 563 artwork records, 485 records with offers or reviewed source links, and 542 edition-matched price-provider IDs. All 506 active identities are resolved; 15 archived entries have no metadata and require no resolution. The remaining artwork placeholder is OPTCG Simulator, and unavailable purchase prices stay unknown. See the [coverage report](Data/catalog-report.json) and [reviewed resolutions](Data/catalog-resolution-report.json). The bundle contains 55 reviewed Philippine PC Game Pass matches, with no EA Play/Ubisoft subscription candidates. See the [subscription review report](Data/subscription-report.md). Public snapshot publishing remains disabled until provider permissions and release review are approved. See [catalog maintenance and publishing](Tools/CATALOG.md).

Select PC Game Pass in Settings → Subscriptions if your membership is active. Settings also manages the optional IsThereAnyDeal API key, automatic catalog checks, manual refresh, update status, and diagnostics export. Currency & region shows the supported Philippine PC catalog, PHP game prices, USD earnings, and saved exchange rate. Backup & restore, game-view defaults, and display preferences are placeholders. Playable now uses owned games, free games, and current verified Game Pass availability. Subscription records expire from Playable now after seven days without verification. A credential-free maintainer collector and weekly GitHub Actions review workflow prepare updates; the workflow becomes active after pushing it to the default branch with Actions enabled. Desktop users receive approved published snapshots through the existing in-app refresh.

Artwork appears in Games, Dashboard recordings, and Sessions. Games rows have a quick ownership toggle that preserves saved purchase costs. Displayed sources, price comparisons, and store filters use Steam, Epic Games, Ubisoft Connect, EA app, Microsoft Store, GOG, Battle.net, PC Game Pass, Roblox, and reviewed official project websites. Reseller offers remain stored as provider provenance but are excluded from the user interface.

## Tests

The solution includes an isolated xUnit test project for accounting, imports, reconciliation, migrations, and audit history. See [Tests/README.md](Tests/README.md) for setup, commands, coverage, and remaining manual checks.
