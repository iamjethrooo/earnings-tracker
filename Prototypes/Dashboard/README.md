# PRO-19 · Dashboard visual prototype

An isolated WPF executable for reviewing the shared shell and analytics Dashboard. The Dashboard is iteration 01 of [PRO-19](https://linear.app/botlog/issue/PRO-19/prototype-the-post-v11-ui-revamp-with-sample-data), which remains **In Progress**. The same shell hosts the [Sessions Explorer (iteration 02)](Sessions/README.md) and [Games library (iteration 03)](Games/README.md). Functional integration remains deferred; PRO-16/46/47/48 retain their design-approval dependencies. This prototype does not change the approved production design system.

## Run safely

Use Windows, the repository's .NET SDK 10.0.402 and Windows Desktop support. Run these commands from the repository root:

```powershell
dotnet restore Prototypes/Dashboard/DashboardPrototype.csproj --ignore-failed-sources
dotnet build Prototypes/Dashboard/DashboardPrototype.csproj --no-restore
dotnet run --project Prototypes/Dashboard/DashboardPrototype.csproj --no-build
```

Always supply the prototype `--project` path. The prototype window says **Synthetic UI prototype** and the sidebar identifies the sample data. Close it normally when finished. No storage configuration, account, API key or production app is needed. Restore may contact NuGet to obtain the existing FluentIcons package; the running prototype has no network services.

`PrototypeApp.Main` creates a plain WPF `Application` and the prototype window. This project has **no project reference to the application**. It compiles only its own files and linked `RelayCommand`, `NavigationPage`, `PresentationPalette`, shared resource dictionaries and Inter. It does not include `App`, production `MainWindow`, EF, financial services, catalog data, credentials or Velopack. The production project excludes `Prototypes/**` from its build items. The prototype is deliberately outside the production solution.

Its assembly name is `EarningsTracker` to preserve the existing font/resource pack URI. Its output is exclusively `Prototypes/Dashboard/bin/...`; do not copy it into a production installation or combine the two projects' output directories.

## Review interactions

- Select **7 days**, **30 days** or **90 days**. The chart, period earnings, hours, game comparisons and session counts update together. Balance stays an explicitly labeled as-of snapshot.
- Hover the chart for exact daily amounts, or expand **Daily values** for a bounded, recycling list accessible with the keyboard.
- Select Sessions or Games in the sidebar to open their isolated previews. **Sessions →** also opens Sessions. The Dashboard sidebar item returns to this overview. Payments, Statistics and Settings remain scope placeholders. No production page is constructed.
- Select a game in recent activity or top games for a synthetic contextual preview. **Escape** or **Back to overview** closes it. The underlying shell is disabled while the preview is open; focus is restored to its origin.
- Use **Example** to inspect Populated, Empty, Loading, Error and Stale. Loading is a static example. Error hides unavailable metrics; Stale retains the sample snapshot and identifies it. Retry restores Populated without a service call.

## Layout and measure decisions

The compact 188-DIP sidebar and restrained E mark keep the visual identity centered on Earnings Tracker. The shared charcoal surfaces, Inter, purple selection/focus/links and semantic status colors come directly from `Resources/Styles.xaml`. The five KPI cards form one scan line. The wider left column prioritizes the earnings trend and recent activity; the right column explains verification status and game contribution. Cards use the existing radii, panel styles and spacing scale. The chart follows Statistics' native Canvas/Polyline pattern, with measured USD tick-label gutters and fewer date labels at narrower widths. There is no new chart library.

At less than 850 DIPs of page-region width, the analysis and activity columns stack. Cards resize with the window; metric text scales down only if needed. The page scrolls vertically at smaller sizes, keeping the title, date range and period controls visible. Recent activity is a finite five-session summary with its own 240-DIP recycling viewport; this is not a new Sessions explorer. Top games is capped by the four-game fixture.

| Measure | Synthetic definition / date basis |
| --- | --- |
| Verified earnings | Sum of verified session earnings in the selected inclusive recording-date range. No bonuses/corrections in this fixture. |
| Pending earnings | Pending session earnings in the same range, separately from verified earnings. |
| Available balance | Explicit illustrative $214.62 as-of Oct 19 snapshot, labeled posted after the Oct 12 payout. Independent of period; not a new accounting calculation. |
| Payouts | Independent sample Paid/Processing payments whose payment dates fall in the range; may pay earnings from earlier periods. |
| Hours | Duration of verified and pending sessions in the selected recording-date range. |
| Session mix | Count distribution, with separate dollar amounts for each status. |
| Top games | Verified earnings per game; bars compare each game with the highest earning game in the selected period. |

All dates, records, names, amounts and covers are fictional, anchored to **Oct 19, 2026, 23:59** for repeatable comparisons. Three covers are local vector illustrations and one uses the shared missing-cover presenter. Nothing is read from user files or the game catalog. Sample aggregation is presentation-only and does not replace `PayoutAccounting` or any production financial contract.

## Render and check

Use a fresh local output folder for each run:

```powershell
dotnet run --project Prototypes/Dashboard/DashboardPrototype.csproj --no-build -- --render .local-docs/PRO-19/review-local
```

This runs the actual WPF resources and bindings without displaying a window or invoking production startup. It writes PNGs for minimum 1000×600, compact 1200×720, default 1440×960 and wide 1800×1000 client layouts, scrolled views, 90 days, daily values, a game preview, navigation and feedback states. The 150% artifact renders the default logical layout to a 144-DPI bitmap; it is **not** an actual monitor DPI-transition test.

`checks.txt` records the executable assertions: layout widths/stacking, bounded row reachability, period and navigation interactions, fixture reconciliation, state transitions and binding diagnostics. Success returns exit code 0. Failure returns 1 and writes `failure.txt`; preserve the failed output separately from later runs. Rendered fixtures do not certify native input behavior.

## Owner review and remaining checks

Review the density of the five-card row at minimum size, the permanently visible period controls, the line/area treatment versus another chart style, and the separate status-count and game-contribution panels. The balance label and recording/payment-date distinction need approval before functional integration. Narrow layouts deliberately require vertical scrolling, and long game links can ellipsize.

Manual native keyboard/tab order, focus return/containment, mouse wheel behavior across nested bounded viewports, resizing/maximize, screen-reader announcements, and real monitor DPI transitions remain to be checked. The loading/error examples are deterministic presentations, not async recovery implementations. Dashboard performs no writes; Sessions edits are in-memory demonstrations only. There is no persistence, real import, Steam call, goal, forecast or new setting. These prototypes do not satisfy the full PRO-19 scope or authorize integration.
