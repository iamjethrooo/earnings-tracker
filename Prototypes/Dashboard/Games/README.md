# PRO-19 · Games library prototype

Iteration 03 adds a synthetic PC game library to the existing [prototype shell](../README.md). The Dashboard and Sessions presentation and behavior remain unchanged. Production Games, settings, catalog, artwork, financial records and services are not used or modified. PRO-19 remains **In Progress** for owner review; functional integration remains deferred.

## Launch safely

From the repository root on Windows with the pinned .NET SDK and Windows Desktop support:

```powershell
dotnet build Prototypes/Dashboard/DashboardPrototype.csproj --no-restore
dotnet run --project Prototypes/Dashboard/DashboardPrototype.csproj --no-build -- --games
```

If restore is needed, first run `dotnet restore Prototypes/Dashboard/DashboardPrototype.csproj --ignore-failed-sources`. Always provide this project path. Omitting `--games` still starts Dashboard, and `--sessions` still starts Sessions. Games also opens through the existing sidebar. The executable continues to use the isolated `PrototypeApp` entry point and its own output directory.

The running preview makes no database, catalog, filesystem, credential, Steam or network calls. NuGet restore may use the network for the already referenced icon package. Closing the process discards all sample ownership, bookmarks and starting preferences. No game-launch or store-opening action is provided.

## Layout and interactions

A compact list was chosen over a cover grid to keep rates, caps, potential and acquisition information aligned for comparison. It reuses `GameCardItem`, `ArtworkPresenter`, `BookmarkButton`, `MetricProgressBar`, shared feedback, typography and controls. Artwork is local vector illustration; missing covers use the shared placeholder. Archived rows stay readable and explicitly identify historical rates and inactive earning opportunities.

Search, access and sorting occupy the main toolbar row; genre, store, bookmarks and archive visibility form a compact second row. Filter labels sit above their controls with consistent columns and gaps. Search finds fictional titles, and filters compose. Access options include personally owned, playable now, free-to-play, current PC Game Pass, unavailable price and stale information. Sorting offers the existing Newest, Highest Pay, Game A–Z and Lowest price choices. Archived identities sort after active ones, matching the established library pattern.

The native ListBox retains a bounded, recycling `VirtualizingStackPanel`, pixel scrolling and a one-page cache. There is no outer library scroller or extra panel framework. Below 1,050 DIPs of page-region width, comparison values move to a second row within each item; the items panel and virtualization remain unchanged. No acquisition column is hidden. At minimum height, fewer rows are visible and native vertical scrolling provides the rest.

Select a row for its active state; open its title or press Enter to see details. Bookmark and ownership buttons are separate actions. Removing a bookmark while bookmarks-only is active filters the item out, while an already open details context remains readable. Returning to the library retains filters and scroll position; selection clears if the item no longer matches. Ownership changes preserve the sample purchase cost.

Personal ownership actions say **Mark owned** or **Owned · remove**, showing the current owned flag and its removal action without implying a Steam change. Rate, cycle cap and **Full-cycle potential** share a quiet comparison surface within the existing row. Missing covers use a readable two-line **No artwork** placeholder built from existing styles, without another asset or focus stop.

Details use a readable two-column layout, stacking below 900 DIPs of page-region width. Earnings/cap, personal ownership, subscription evidence and Steam demo slots sit beside acquisition editions and history navigation. The details body scrolls independently with the Library return action above it. Session, rate and cap-reset history actions open labeled illustrative evidence overlays. They do not mutate records, recalculate historical earnings, or alter the separate Sessions/Dashboard fixtures.

## Measures and acquisition evidence

The 180 fictional games and their offers are anchored to **Oct 19, 2026**. The first named examples include Orbital Drift, Mosslight Valley, Neon Circuit, The Last Outpost, Sable Atlas, Juniper Hollow, Glass Sea and Rift Chronicle. Remaining titles form a synthetic stress fixture, not a copied catalog.

| Information | Meaning in this prototype |
| --- | --- |
| USD/hour | Displayed sample hourly rate. Archived games explicitly show a historical rate. |
| Cycle cap | Verified sample hours used / cycle hours, with an Oct 1 recording-date boundary. Exact over-cap percentages remain in text; only the progress visual clamps. |
| Full-cycle potential | Illustrative USD full-cycle earnings ceiling at the sample rate and cap. It is not a forecast, remaining balance or promised earnings. Archived games say History only. |
| Personal ownership | In-memory owned flag. Toggling it preserves an existing purchase amount/currency and does not change Steam indicators. |
| Playable now | Personal ownership, a current free primary offer, or current inclusion in the active sample PC Game Pass plan. A stale subscription alone does not grant access. |
| Prices | Source amount, currency, edition, platform, region and checked date remain distinct. Null means unknown, never zero/free. Non-PH and unavailable offers retain their restrictions. |
| Lowest price | Compares current PH primary offers only. PHP is compared directly; USD uses a clearly labeled fixture rate of 56.25 PHP/USD dated Oct 19. Missing FX, unavailable amounts and stale offers sort last. Alternative editions are not mixed into the primary-offer comparison. |

Subscription evidence older than seven days is labeled stale and excluded from subscription-based playable access, following the existing design convention. The fixture supplies explicit stale-price flags; the conservative exclusion of those offers from Lowest price is a **prototype design choice for review**, not a production policy change. A snapshot retry does not erase individual stale-offer evidence. Estimates preserve original USD/EUR amounts and do not imply current provider verification. No live exchange rate is requested.

Selecting Lowest price displays the comparison rules above the bounded list: current PH primary offers compare by PHP equivalent within active/archived groups. Stale, unknown, unavailable and missing-FX offers sort last together, not as zero/free or as distinct price rankings. Row explanations distinguish those exclusion reasons. USD uses only the dated fixture rate; original currencies and editions stay visible. PHP acquisition costs cannot be compared directly with USD earnings without conversion; the details wording makes that distinction explicit. This refinement changes presentation, not the sort algorithm or access rules.

Steam ownership and installed labels are **future UI slots only**, with explicit demo/not-connected wording. They neither scan the PC nor infer a real account's games. They are independent of the editable personal ownership flag. There is no launcher.

## Starting preferences and examples

The initial view uses the existing defaults: **Newest**, **bookmarks-only off**, **archived visible**. Starting view opens the same three preference choices in an in-memory overlay. Apply sample starting view demonstrates applying them together. Real saved preferences are neither read nor written. Clear resets search and filters while retaining sort and archive visibility.

The Example selector includes Populated, Archived, Missing artwork, Unavailable price, Loading, Filtered empty, Stale and Error. Real filter combinations also produce filtered-empty guidance. Loading/error do not present missing data as an empty financial total. Stale keeps the previous library snapshot with a timestamp and Retry. These are deterministic presentation examples, not service/recovery implementations.

## Focused checks and renders

Use a new output folder:

```powershell
dotnet run --project Prototypes/Dashboard/DashboardPrototype.csproj --no-build -- --render-games .local-docs/PRO-19/games-review-local
```

The runner exercises the actual WPF resources/bindings and writes minimum 1000×600, compact 1200×720, default 1440×960 and wide 1800×1000 PNGs. Additional artifacts cover detail stacking/scrolling, acquisition currencies, stale evidence, archived history, bookmarks, starting preferences and read states. A 144-DPI export is rendering evidence, not an actual monitor-transition check.

`checks.txt` records focused assertions for virtualization and final-row reachability, row reflow, filters and sorts, bookmark/ownership changes, preserved purchase evidence, subscription eligibility, price/FX distinctions, source constraints, details/history, starting preferences, scroll restoration and WPF binding diagnostics. Success exits 0; failure exits 1 and writes `failure.txt`. `bindings.txt` captures binding diagnostics. No broad production suite or production startup is required.

## Owner review

Review the compact-list choice, repeated comparison labels, narrow-row density, full-page details rather than a drawer, starting-preference presentation, and conservative stale-price sorting. All are isolated design proposals. No `Docs/Design-System.md` contract was changed.

Native mouse selection versus embedded buttons, keyboard/tab order and focus return, wheel behavior, live resize/maximize, screen readers and actual monitor DPI changes remain manual checks. Offscreen command/layout assertions do not certify physical input. Stop after this Games review; production integration, launcher features and other backlog work are outside scope.
