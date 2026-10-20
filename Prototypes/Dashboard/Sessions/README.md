# PRO-19 · Sessions Explorer prototype

Iteration 02 extends the isolated [Dashboard prototype shell](../README.md) with a compact Sessions explorer. It uses the same charcoal/purple styling, Inter, navigation and page-heading pattern. Production Sessions, imports, accounting, persistence and schemas are untouched. PRO-19 remains **In Progress** for owner acceptance; functional integration is deferred. The separate [Games prototype](../Games/README.md) shares the shell.

## Launch

From the repository root on Windows with the pinned .NET SDK and Windows Desktop support:

```powershell
dotnet build Prototypes/Dashboard/DashboardPrototype.csproj --no-restore
dotnet run --project Prototypes/Dashboard/DashboardPrototype.csproj --no-build -- --sessions
```

If this is the first build, run `dotnet restore Prototypes/Dashboard/DashboardPrototype.csproj --ignore-failed-sources` first. Omitting `--sessions` starts the existing Dashboard; the sidebar switches between both prototypes. Always pass this project path, rather than running the production application. Outputs remain in the existing prototype project's own bin directory.

The entry point is still `PrototypeApp`, a plain WPF Application with no production project reference. There are no database contexts, credentials, files, clipboard reads, provider calls or network services in Sessions. Dependency restore may contact NuGet. Closing the process discards every sample change.

## Review the explorer

The fixture contains **1,500 fictional sessions**, anchored to Oct 19, 2026, from four fictional games. The initial last-30-days filter shows 540 records. Game, recorded date/time, duration, USD earnings, labeled verification, source and source ID stay in separate aligned columns. Each row is 44 DIPs high. Source evidence and unknown posted dates remain explicit in details.

- Search by game or source ID, such as `SAMPLE-20481`. Combine verification, game and recording-date filters. Date choices are Last 7 days, Last 30 days and All sample dates, inclusive through the fixed snapshot. Clear resets filters to the full sample library.
- Sort numerically by earnings or duration, chronologically, or by game. The toolbar is the deliberate sorting control; column headers do not imply click sorting. Column order stays fixed.
- Select a row to inspect it without editing. Select the purple game-title link to open a separate game-navigation preview; it does not intentionally change row selection. **Back to session** returns to that selected record. The separate Games page is available from the sidebar; its sample records are not synchronized.
- The right panel is adjacent at page-region widths of at least 1,150 DIPs. Below that, it becomes a 360-DIP drawer spanning the page content height. Close it with ×, Escape, or the scrim to return to browsing. A covered table is disabled for input. Details and source evidence scroll inside the panel; actions stay at its bottom.
- **Import sessions** opens a static three-record preview, including a duplicate example. It reads no source and adds no rows. It is an affordance and feedback preview, not a replacement importer.

The table always occupies a bounded star-sized Grid row. It uses native `ListView`/`GridView`, shared table rows/headers, `ICollectionView` filtering/sorting, pixel scrolling, and a recycling `VirtualizingStackPanel`. There is no outer table scroller, row expansion or detail content in the items panel. Readable column minima can require native horizontal scrolling at minimum width; source information is retained rather than hidden. The filter toolbar wraps at narrower widths.

## Viewing and editing

1. Select a session, then choose **Edit session**. The displayed record is unchanged while a separate draft is open.
2. Edit duration, earnings, verification and change reason. Recording date/time, game identity, posted date and original source evidence are read-only in this iteration. Editing numbers uses the current culture; USD display uses en-US.
3. Choose **Save sample** or **Cancel**. Escape cancels an editable draft. Cancel does not write. Unsaved changes block page navigation, panel dismissal and window close with explicit Save/Cancel guidance. Filters and table selection are unavailable until editing finishes.

Prototype validation requires duration greater than zero and no more than 1,440 minutes, nonnegative decimal earnings, a supported verification status and a 1–240-character change reason. These are demonstration constraints, not newly approved production rules. Invalid input is neither normalized nor rounded. Currency display rounds to cents, while the in-memory decimal retains its precision.

Validation appears directly below the corresponding field, with polite live feedback and the same message exposed as the editor's accessibility help text. A failed save focuses and scrolls to the field plus its message, including repeated attempts from the bottom of the narrow drawer. Editing an unrelated field retains the outstanding error. Conflict and refresh feedback are revealed when entering the narrow layout or completing/retrying a save there. Verification badges reserve enough width for the full status after row replacement and recycling.

Use **Synthetic save result** in the editor:

| Result | Behavior |
| --- | --- |
| Normal | Shows a short nonblocking busy state, then applies the change only to the in-memory fixture. |
| Conflict | Simulates a revision change. Saving stops, the draft remains intact, and feedback asks you to Cancel, inspect and Edit again. Choose Normal for the next attempt. |
| Saved · refresh failed | Applies the sample change once, exits edit mode, and shows a saved warning. Retry refresh clears the warning without saving again. |

During the approximately 900-ms busy interval, inputs, Save, Cancel, panel dismissal, navigation and close are guarded. No financial amount is recalculated from duration. Original source values remain intact after a sample correction. In-memory edits do not synchronize with the separate Dashboard fixture.

## Read and recovery examples

The **Example** selector demonstrates Populated, Empty, Filtered empty, Loading, Stale and Error. Real filter combinations also produce filtered-empty guidance. Empty offers the import preview; filtered-empty offers Clear filters. Loading and first-load Error hide unavailable records and counts. Stale retains the prior fixture with a timestamp and Retry. These are deterministic UI examples, not real async loading/recovery code.

## Focused verification and visual artifacts

Use a new local output directory:

```powershell
dotnet run --project Prototypes/Dashboard/DashboardPrototype.csproj --no-build -- --render-sessions .local-docs/PRO-19/sessions-review-local
```

This renders real WPF controls/resources offscreen at 1000×600, 1200×720, 1440×960 and 1800×1000, along with table-only narrow layouts, editing, validation, unsaved, busy, conflict, saved warning, read states, import and game navigation. A 144-DPI export samples rendering at 150%; it does not test actual monitor transitions.

`checks.txt` records focused assertions covering realized container counts, reachability of the last of 1,500 rows, filters, numeric sorting, selection, separate game navigation, drafts, validation, navigation guards, precision/source preservation, simulated conflict, save-once refresh behavior and binding diagnostics. Success exits 0. Failure exits 1 and writes `failure.txt`; `bindings.txt` records WPF binding diagnostics. No broad production suite is required for this isolated presentation iteration.

Remaining native review: actual mouse selection versus link clicks, keyboard/tab order, focus return and containment, wheel handoff between table/details, window close during saving, live resizing/maximize and monitor DPI transitions. The prototype contains guards and focus handling, but offscreen command/layout checks do not certify those physical interactions or screen-reader output.

Owner decisions: the drawer breakpoint, 44-DIP row density, preset date filters, the limited editable fields, and keeping source evidence beneath the main details. Games browsing, full editing/import workflows and production integration remain outside this iteration.
