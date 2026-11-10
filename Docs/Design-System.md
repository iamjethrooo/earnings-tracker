# Earnings Tracker UI design system

Keep the existing charcoal surfaces, Inter typography, purple actions and selection, cover artwork, and compact coloured badges. Prefer existing styles and templates when adding UI. This is a consolidation of the current design, not a new theme.

## Resource organization

`App.xaml` loads `Resources/Styles.xaml` and `Converters.xaml`. `Styles.xaml` remains the compatibility entry point for existing named keys and merges these dictionaries:

| Dictionary | Responsibility |
| --- | --- |
| `Tokens.xaml` | Font, semantic brushes, type sizes, spacing, padding, radii. |
| `Typography.xaml` | Page and section headings, item text, metrics, filter labels, dialog feedback. |
| `Controls.xaml` | Buttons, focus, scrollbars, search, ComboBox, checkboxes and filter chips. |
| `Calendar.xaml` | DatePicker and calendar templates. |
| `ListsAndTables.xaml` | Navigation/list containers and table headers. |
| `Panels.xaml` | Page host, cards, metric panels, compact surfaces and feedback panels. |
| `Dialogs.xaml` | Modal text fields, multiline import fields and dialog surface. |
| `Presentation.xaml` | Artwork presenter and session status variants. |

Use `DynamicResource` for token references inside shared dictionaries: WPF may resolve these setters/templates before sibling dictionaries are available. Keep `BasedOn` references to styles in the same dictionary. Views can use `StaticResource` once application resources are loaded. Load the compatibility entry point in fixtures as the application does.

## Tokens and component roles

- **Surfaces:** `DefaultBackgroundColor` is the page canvas; `SidebarBackgroundColor`, `SurfaceColor`, `CardBackgroundColor`, `DialogBackgroundColor`, and `InputBackgroundColor` describe distinct roles. Borders and hover colours have their own keys.
- **Actions:** retain `AccentColor`, `AccentHoverColor`, `AccentTextColor`, `AccentBackgroundColor`, and `TextOnAccentColor`. Filled primary actions use dark text; links and selections use the appropriate accent-text or background role.
- **Status:** success/verified, warning/pending, error/rejected, neutral/not uploaded, and archived retain text labels alongside colour. `PresentationPalette` supplies shared value-type status colours to XAML and background presentation work. Payment cyan remains a separate transaction-type role. Worker-created brushes must remain frozen.
- **Font:** `AppFont` resolves bundled Inter using the assembly's actual resource URI. The main window and standalone windows use this resource.
- **Type:** page title 25 DIPs; section title 16; body 14; compact body 13; supporting text 12; badge 11; metric 30. Use the named heading and metric styles rather than duplicating setters. Existing compact table/status sizes remain deliberate compatibility variants; new essential information should use at least supporting-text size.
- **Spacing:** 4/8/12/16/20/24 DIPs. `PageInset` is 20 on all sides, including below bounded list viewports. Use named padding tokens and retain local grid-specific gaps where they describe layout.
- **Shape:** control radius 6, card radius 8, dialog radius 12. Existing compact panels retain radius 7. Do not change geometry merely to replace a literal with a token.
- **Panels:** `MetricPanel` is the shared base for `DashboardStatPanel` and `GameDetailPanel`. `SurfacePanel` is the compact bordered surface; `FeedbackPanel` adds the standard bottom gap. Apply local padding or margins only when a specific layout needs a documented variant.
- **Tables:** `TableColumnHeader` provides the shared template and honors `HorizontalContentAlignment` (left by default; monetary columns can align right); `CompactTableColumnHeader` preserves Statistics' existing density. Keep readable column minima, scrolling fallback, sort events, and recycling at the view level.
- **Imports:** use `ImportInputTextBox`, `DialogBusyText`, and `InputValidationText`. View names, messages, handlers, preview/confirmation steps, and importer lifetimes stay in their existing workflows.
- **Inline validation:** `InlineValidationText` extends `InputValidationText` for bound editor errors and collapses when text is empty. Import validation retains explicit visibility control.
- **Session filters:** `SessionFilterChip` uses supporting-size text and token spacing, semantic status text, and purple selection/focus. Its text explicitly consumes the control foreground so implicit typography cannot override status colors.
- **Progress:** `MetricProgressBar` is a determinate 0–100 utilization indicator using the shared accent and control surface. Bind read-only computed values with `Mode=OneWay`. Keep exact totals and over-cap percentages in adjacent text; the indicator alone clamps to its visual range.
- **Artwork:** `ArtworkPresenter.Content` is the existing `ImageSource` binding. Its template owns the placeholder and image; the caller owns frame dimensions and layout. It adds no loading service or tab stop.
- **Games:** `GameCardItem` derives from `ItemButton` with one compact inset, normal body weight, and fully readable archived rows. Archive status retains its semantic badge; bookmarks use the shared `BookmarkButton` accent and focus treatment. Names use `SectionHeading`, essential metadata uses supporting text, and rates retain explicit USD formatting. The Games list owns a bounded native pixel-scrolling viewport with content scrolling, recycling, and a one-page cache on each side; do not replace it with an outer scroller or disable content scrolling.
- **Games preferences:** Settings stores the starting Games sort (Newest by default), bookmarks-only filter (off by default), and archived-game visibility (on by default) in the existing catalog settings store. The sort and bookmark choices apply when Games opens, then remain adjustable in its toolbar. Archived visibility affects only the Games list; archived game history and reporting remain available. Invalid saved sort values fall back to Newest.
- **Badges:** history and compact details deliberately have different Pending/duration treatments. `DetailedSessionStatusText` preserves the existing order: status colour, zero-duration warning, then the not-uploaded override. Do not combine these variants by changing status rules.
- **Transaction badges:** `TransactionBadge` and `TransactionBadgeText` share Sessions' control radius, padding, and supporting-size text for transaction types, statuses, and reconciliation labels. Callers supply semantic brushes and descriptive text. Payment cyan denotes Payment only; unknown/Other types are neutral. Paid/Verified use success, Pending/Processing warning, Failed error, and historical/unknown statuses neutral.
- **Payout import details:** expandable sections retain their category/count headings and use shared compact surfaces, headings, and item containers. Populated sections use a recycling ListBox with a 240-DIP maximum viewport inside the modal scroller; this is a viewport bound, not a new spacing token. Skipped/failed sections open for review when populated. Read-only selection does not resolve a match; the existing explicit skip choice and conflict block remain authoritative.
- **Detail badges:** `DetailedSessionStatusBadge` supplies the matching container for raw session records, with the same duration/not-uploaded precedence as detail text. History badges still bind to session item wrappers. Use the appropriate variant for the binding context and retain their deliberate status differences.

## Hosting and accessibility

The shared ScrollViewer style enables `Controls/SmoothScroll.cs`: discrete mouse-wheel input
follows its destination with a 100 ms settling interval in pixel viewports, accumulating repeated
input without restarting the easing and responding immediately to direction changes. Updates
follow WPF rendering frames with elapsed-time weighting instead of a fixed-rate timer.
It preserves content scrolling, recycling and existing viewport bounds.
Logical item-scrolling lists, nested input controls, modified wheel gestures and high-resolution
wheel packets retain native handling. Windows' client-area animation preference disables easing.
Keyboard input, mouse clicks, unload and viewport size changes cancel pending animation.
Extent changes clamp the destination without stopping, so virtualized row-height estimates can
adjust while scrolling. The rendering handler is subscribed only during an active animation.
Set `SmoothScroll.IsEnabled="False"`
on an individual ScrollViewer to opt out. Physical touchpad/wheel feel and large artwork-heavy
lists still require interactive performance checks; offscreen tests do not measure frame pacing.

Apply `PageHost` explicitly to `MainContent`. Pages own their 20-DIP inset on all sides, so the host defaults to zero padding. The template consumes explicit padding, border and content alignment, forwards content templates, and applies margin only through normal layout. Ordinary ContentControls retain their normal WPF templates.

Preserve the shared focus visual, primary-button contrast, popup parts, accessible names, live feedback, modal keyboard containment, commit close protection, and virtualization established in Phases 1–3. A visual extraction must not remove a local trigger, hide an action, reset a binding context, or add an unintended tab stop.

Editor focus must remain visible while hovered: `ModalTextBox` applies its keyboard-focus border after its hover border and uses the shared disabled-opacity treatment. Search hints use `SecondaryTextColor` without an extra opacity reduction. Preserve these behaviors when updating the shared styles and verify keyboard focus, contrast, and compact layouts as part of UI changes.

### Filter toolbars

Games and Sessions share an open, two-row toolbar: search, sort and clear actions first,
then wrapping filters. Use `FilterSearchBox` for the search icon and focus border,
`FilterDropdown` for sort and period selectors, `ActiveFilterDropdown` for simple filters,
and `SearchableFilterDropdown` for game/genre filters. Active filters use the purple border
and background; index zero is the unfiltered choice. `FilterCheckBox` supplies the Games
quick-filter chips, and `FilterToolbarButton` keeps toolbar actions at the same density.
Controls are 34 DIPs high with compact body text. Dashboard and Statistics use the same
period-selector style. Keep page-specific bindings, accessible names and filter behavior.

### Dropdowns and explanatory disclosures

Standard ComboBoxes use a single charcoal field, an outline chevron, a rounded popup and compact
checkmarked choices. Selection and keyboard focus use the purple accent. Native selection bindings,
editable parts and popup names remain available; popup viewports recycle their rows.

Use `SearchableComboBox` for game, genre and payout pickers. Its popup opens with a search field,
matches labels without case sensitivity and shows “No matching choices” when empty. Labels come
from `DisplayMemberPath`, `TextSearch.TextPath`, or `DropdownSearch.MemberPath` for a custom item
template (the session game editor uses `Name`). Duplicate game/rate versions remain separate items
with their existing identifying details. Short status, period, store and sort lists remain simple selectors.

`Controls/DropdownSearch.cs` filters a popup-only snapshot, preserving the source collection, its
view and the bound selection. Arrow keys move the result highlight; Enter or clicking a result
commits the original item identity. Escape cancels; Tab closes without committing and moves focus.
Closing clears the search, and reopening includes current items. Changes to the source while open
refresh results; removed items cannot be committed. Source bindings and audited save paths remain
owned by their existing views and view models.

Expanders use `InlineDisclosure`: a quiet inline label and chevron, purple while expanded, with
the existing content revealed underneath. This includes “How calculated”, source evidence and
history sections. Existing header templates, expansion bindings, content, warnings and bounded
inner lists remain in place. Local Expander styles should derive from `InlineDisclosure` to keep
this treatment while retaining their own defaults and triggers.

`DropdownDisclosureTests` checks selection isolation, identity-preserving commit, cancel/Tab,
live source changes and native expansion automation. It exports narrow/wide popup and disclosure
layouts at 96/144 DPI under `DropdownDisclosureVisuals` in the test output. These are offscreen
checks; physical mouse/wheel input, screen readers and actual monitor-DPI transitions remain manual.

Check resource lookup and trigger precedence in real WPF fixtures, not just XML compilation. Render minimum/default/wide layouts and inspect both loaded artwork and missing covers. Check final foregrounds explicitly when replacing a local style: an explicit TextBlock style does not automatically inherit the implicit one.

## Recovery and feedback (Phase 5)

Use `EmptyStateText` for quiet empty-list guidance, with a clear distinction between no data and no filter matches. Show loading and failure messages in a separate `FeedbackPanel` when rows are retained; describe stale data and provide the existing retry action. Avoid repeating the same error in the count summary.

Settings feedback uses verified/success, pending/warning, rejected/error, and secondary/neutral tokens alongside descriptive text and polite announcements. Mixed update results retain their warning meaning. Keep prices, editions, availability, and stale-data warnings visible; reviewed provenance can use progressive disclosure.

Sidebar routing uses `NavigationPage` identifiers rather than labels or list indices. Product versions must be explicit release metadata; default assembly versions remain diagnostic information.

## Shared navigation and editing (PRO-46)

The approved PRO-19 shell uses a compact 188-DIP sidebar, the restrained E mark, and the existing
`NavButton` icons, purple selected background and selection marker. Production keeps Settings at the
bottom and preserves all six destinations. `MainWindow` owns routing and game-detail entry; pages
recreate on navigation, without a page cache.

Page headings use `TabName` with a short, wrapping `PageDescription` beneath it. Existing page
actions sit to the right where present. Use `PrimaryButton` for the main action (such as import or
Save changes), and `SecondaryButton` for alternatives (such as Add payout or Cancel). Do not invent
an action to fill a header. Payments and Settings retain their existing workflows. Dashboard,
Sessions and Games content redesigns remain separate work.

Session editing retains an isolated draft and all supported fields in the selected-session panel. One draft can be opened
through the UI at a time; filters are disabled until it finishes. Navigation, game-detail entry,
imports, deleting a row and window close retain an open draft and show Save/Cancel guidance.
Cancel and Escape discard the draft without writing; focus returns to the read action inside a narrow drawer, or its row beside a wide details panel. Closing the drawer returns focus to the row. Save validates without
normalizing input, then uses `SessionService` with the captured edit state. Reentrant/queued saves
cannot submit a busy or completed draft. Existing modal focus containment, close guards, preview
confirmation and importer disposal remain in place.

Recorded date, posted date, duration and earnings validation appears beside the corresponding input,
with accessible help text and polite feedback. A failed save focuses/reveals that field and its
message. Production rules remain authoritative: for example, zero-duration recordings are valid;
the prototype's duration cap and required change-reason demonstration are not production rules.
Unchanged values retain their original precision and timestamp details.

A conflict keeps the draft and explains how to Cancel, inspect the refreshed record and edit again.
A committed save retires the draft before display notifications. A refresh failure shows an explicit
saved warning and a separate **Retry refresh** action that only reloads; it never repeats the write.
PRO-47 hosts the editor in the bounded details scroll area with Save/Cancel pinned below it.
The session rows remain compact while editing.

Verification uses isolated synthetic databases, real resources and offscreen WPF layouts. Programmatic
focus and modal traversal assertions are not native input certification. Physical keyboard/focus,
wheel behavior, screen-reader announcements and actual monitor-DPI transitions remain unverified;
owner-operated checks are not required for this integration.

`NavButton` pairs each page label with a 20-DIP Fluent icon selected by its `NavigationPage` tag. Icons follow the row foreground and add no focus stop. Shared scrollbars use a 12-DIP interaction lane with an inset rounded thumb, a quiet charcoal rail on hover, and purple while dragging. Both orientations retain viewport sizing and native page-scroll commands, with a 48-DIP minimum thumb length for long documents. The shared ScrollViewer template preserves its named scrolling parts and content scrolling, with a transparent corner when both bars appear.

## Compact Sessions explorer (PRO-47)

Sessions uses the approved compact table with 44-DIP recycling rows. Game, recording date/time,
duration, USD earnings, verification, source and source ID remain separate columns. Column widths
have readable minima; native horizontal scrolling exposes overflow. The toolbar controls sorting;
column headings do not imply click sorting. The table occupies a bounded star-sized Grid row with
native pixel scrolling, a recycling VirtualizingStackPanel and a one-page cache. There is no outer
table scroller or expanded editor inside a row.

Search matches game names, recorded source IDs and the internal `Session #id` identifier. Verification
includes the existing Unlinked choice. Game filters distinguish saved game IDs (including
duplicate names/rate versions); unlinked historical names have their own choices. Recording-date
filters use All recording dates, Last 7 days and Last 30 days, inclusive through the current local day.
All dates remains the production default so historical rows are visible. Clear resets search,
verification, game and date filters while retaining sorting. Verification counts describe the complete
loaded snapshot; the summary reports matching versus total sessions.

Selecting a row inspects it without entering edit mode. Enter on a table row opens/focuses its details.
The separate game-title button routes through MainWindow to Game Details without changing row
selection. Unlinked records have no game-navigation target. At page-region widths below 1,150 DIPs,
the 336-DIP adjacent panel becomes a 360-DIP drawer spanning the page. The covered page is disabled
and Tab cycles within the drawer. Close, Escape in read mode and the scrim return to browsing;
a draft blocks dismissal with Save/Cancel guidance. Escape while editing cancels the draft. Open
ComboBoxes/calendars consume their own Escape first. Reparenting across the breakpoint retains the
draft and restores editor focus. Read/edit content scrolls while actions stay pinned.

Production fields remain available: recording and posted dates, duration, earnings, status,
force-verification and explicit game linking for unlinked sessions. No prototype-only reason field,
positive-duration rule or sample duration cap is introduced. Original timestamp components and
monetary/duration precision are retained when a displayed value is unchanged. History status colors
continue to use SessionHistoryStatusBadge/Text, including labeled Not Uploaded; details expose
force-verification separately.

Payout assignment appears in session details and the editor. Choose Current payout or a named
Paid/Processing Payment; untouched historical rows show their inferred-date treatment explicitly.
Payment labels include date, USD amount, status and source/local ID to distinguish same-day payments.
The choice stays separate from source evidence and preserves draft selection during refresh.
New-session import previews offer the same payment choice for the new batch.

Payment creation and first transition to Paid/Processing show a coverage review before saving.
`PayoutCoverageView` uses shared surfaces and a bounded recycling checkbox list, with a live selected
total and any difference from the payment amount. Unchecked sessions remain current. A review prompt
uses pending-color feedback; validation errors retain rejected-color feedback. See
[assignment and accounting rules](Database.md#session-payout-assignments).

Source labels come from recorded import audit types. Source IDs are linked payout IDs when recorded;
they are not invented session-provider identifiers. Unknown historical IDs/posting dates remain
explicit. Source evidence and audit history load only for the selected record, with stale-result
checks and retry. Expandable, read-only fields retain raw session source, before/after audit JSON and
linked payout source values. The audit viewport is bounded and recycles its entries. Snapshot and
evidence reads are serialized in the view so they also work with isolated shared-connection fixtures.

A successful refresh publishes the completed read and preserves selected wrappers/drafts. A deleted
target can retain its draft for explicit Cancel or a rejected stale save; it still blocks navigation
and application restart. Cancel then removes the missing details context. Failed refreshes retain
previous rows, identify them as potentially stale with the last successful read time, and offer Retry.
First-load failure and loading do not show an empty-success count. Empty history offers Import;
filtered-empty offers Clear filters. Recovery feedback stays visible inside the narrow drawer and
repeated validation/conflict attempts reveal the relevant field/message. Retry refresh after a
committed save reloads without repeating the audited mutation.

Offscreen fixtures cover narrow/default/wide layouts, a 144-DPI export, keyboard focus/selection,
resizing, evidence, conflict recovery and a persisted synthetic 10,000-session history. These checks
do not certify native mouse/wheel behavior, screen readers or actual monitor-DPI transitions.
Owner-operated checks are not required; those limits remain recorded.

## Compact Games library and details (PRO-48)

Games uses four primary columns: Game, Rate, Cycle and Acquisition. Game includes compact artwork,
title, genre, stores, ownership and bookmarking. Full-cycle potential stays in Game Details.
Below 1,050 DIPs of page-region width the comparison
fields move beneath the title. Missing artwork has a readable fallback. Selecting a row keeps
the library open; its title or Enter opens details. Returning to Library retains filters,
selection and scroll position and restores focus when the original item is still available.
The identity and comparison columns share the available row width; rate, cycle
progress and acquisition align across games without a fixed-width inset. Cycle bars use verified
linked sessions after the saved cap-reset date; pending, rejected, unlinked and pre-reset sessions
do not contribute. Zero progress hides the track and says “Not started.” Hours use `played / cap`
with In progress or Cap reached status; the bar clamps to 100% while text retains over-cap hours.
Library acquisition evidence shows the store, checked date and stale/expired warnings; edition
and region remain available in Game Details.

The primary toolbar row contains an expanding search field with an icon, a separately labeled
sort selector and a secondary Clear filters action. The quick-filter row contains Playable now,
Owned, Bookmarked, Multiplayer, genre/store and More filters; it wraps at narrower widths. Toolbar controls
are 34 DIPs high, with 8-DIP filter gaps and purple selected states. More filters holds
Free-to-play, Steam library, PC Game Pass and Show archived. Its count includes every checked
secondary option, including Show archived. Toggling an option keeps the popover open; Escape
closes it and returns focus to More filters. Tab stays within the open popover.
Access flags remain independently combinable, and the existing matching count stays above the
library. Clear filters is enabled only for a search or filter it can reset; it retains sorting
and the current archived visibility choice. Bookmark selection uses a two-way binding so the
checkbox follows resets as well as user changes.
Multiplayer matches only reviewed catalog identities with recorded multiplayer/co-op support;
unknown and single-player-only entries are excluded. It combines independently with access,
ownership, bookmarks, search and archived visibility, and Clear filters unchecks it. The tooltip
explains that local modes count and current servers/player counts still need checking at the store.
Settings still supplies the saved starting sort, bookmarks-only and archived preferences;
temporary browsing choices do not overwrite them. Lowest price retains the existing comparison
of saved PH offers across editions using saved exchange rates. Outdated purchase prices remain
visible with checked dates; the prototype's synthetic price assumptions do not replace these rules.

Details has a pinned Games action and a bounded, recycling session-history viewport. Its finite
header uses prominent artwork, identity, rate, ownership and bookmark controls. Below it, one earnings
card groups verified cycle usage/progress, full-cycle potential, stored verified earnings and
the audited cap-reset editor. Ownership, purchase cost and subscription evidence
occupy the left column; individual acquisition cards and History & evidence occupy the right.
The columns stack below 900 DIPs of page-region width. Descriptions remain fully visible after the
primary cards. Offer headings identify store/edition/region, with prominent source-currency prices;
checked dates, saved quotes and source evidence remain accessible. Archived opportunities are labeled
historical. Saved same-name rate identities and cap-reset audit values are separate read-only
disclosures. Session history scrolls to the existing status filters and bounded session rows.
Existing cap-reset and ownership dialogs remain authoritative. A separate Steam badge identifies
matches from the last successful library sync configured in Settings. Details shows the sync time
or the reason evidence is unavailable. Manual ownership remains editable alongside the badge and
drives the Owned filter. See [Steam library access and ownership evidence](Steam-Library.md).
Acquisition offers remain separate, individually selectable cards. Archived details keep normal
text contrast and identify the saved rate as historical. When a game has no sessions, guidance
directs the user to the Sessions page for importing them.

The PC Game Pass filter includes previously verified PH/PC inclusion even after seven days or
recorded expiry. Rows and details show the actual saved verification date and **Game Pass
availability may have changed.** Stale evidence uses warning text/color and never claims current
availability. A newer recorded not-included result replaces older inclusion in this filter.
Game Pass uses compact status and checked/expiry dates. In game details it is plain text,
with source evidence in a tooltip instead of a subscription link or a second source paragraph.
Settings separately counts current inclusion, stale/expired inclusion, currently verified
not-included and other unverified records; stale verification does not confirm removal.

Playable now retains its existing ownership/free-access and subscription-membership rules.
Subscription access still requires verification within seven days and an unexpired record.
Browsing does not write timestamps, extend validity or refresh subscription evidence implicitly.
Catalog failures retain saved data and explain retry and stale-price/availability uncertainty.
Initial loading/failure, empty library and filtered-empty remain distinct.

Isolated fixtures cover narrow/default/wide rendering, keyboard entry/focus return, a 144-DPI
bitmap export, 1,200-game recycling and 10,000-session detail scrolling. These do not certify
physical keyboard/mouse/wheel input, screen-reader announcements or real monitor-DPI transitions.

### Personal game rankings (PRO-18)

[PRO-18](https://linear.app/botlog/issue/PRO-18/evaluate-grindability-and-replayability-rankings) adds two optional personal sorts in Games. **Ease of grinding** helps users choose a game for a routine earning session. **Replay appeal** helps them choose a game they feel like returning to.

| Personal rating | Question in Game Details | Choices from highest to lowest |
| --- | --- | --- |
| Ease of grinding | How easy is it for you to keep playing this game during a typical earning session? Consider setup time, interruptions, and sustained effort. | Easy, Moderate, Hard |
| Replay appeal | How much do you want to return to this game even when earnings are set aside? Consider variety and enjoyment. | High, Medium, Low |

Open **My ratings** in Game Details and choose **Save ratings**. Set or clear each field independently. A saved personal rating always sets that game's rank for that dimension. The existing rate and cycle figures remain available to weigh earning opportunities. Session history, earnings, and Steam observations have no effect on ratings.

An unrated game can receive a suggestion from other games the user rated. At least half of the combined unique genres must be shared. Both games need reviewed catalog metadata with genre tags. For each dimension, the app averages the applicable personal ratings on a three-step scale, weighting each by its share of overlapping genres. It rounds to the nearest step, with exact midpoints rounded up. Only personal ratings contribute, so suggestions cannot spread through chains of games. A new personal rating or a catalog correction recalculates suggestions.

For example, if a game tagged Action is rated **Easy** to grind, a game tagged Action and Puzzle can get **Suggested grinding ease · Easy**. A Puzzle-only game has no genre in common with the rated game, so it gets no suggestion from it. A personal **Hard** rating for the Action/Puzzle game overrides its suggestion.

In Games, **My ease of grinding** and **My replay appeal** sort filtered results by the selected rating or suggestion. Games with neither appear last. Within each band, ties use name then local game ID, both ascending. The row identifies personal and suggested ratings, and the tooltip shows how many rated games contributed to the suggestion. The starting sort stays the same. Personal ratings are saved by local game ID in the profile, so same-name entries can be rated separately. Suggestions are calculated on load and after edits.

## Analytics Dashboard (PRO-16)

The approved Dashboard has five KPI cards, an earnings trend beside the session mix, and recent
activity beside top earning games. It uses the shared shell, typography, panels, artwork, status
variants and buttons. Below 850 DIPs of page-region width the paired panels stack. The page body
scrolls vertically while the heading and period selector stay available. Recent activity is limited
to five recordings in a 240-DIP recycling viewport; Daily values has a separate bounded viewport.
Paired panels retain equal heights. The five KPI cards share the height of their tallest content
with standard padding, without a fixed minimum height that leaves unused space below the values.

Dashboard and Statistics default to Current payout. The alternative 7/30/90-day ranges include
the complete snapshot day and start 6/29/89 days earlier. The Current payout option matches Statistics: sessions need a known posted date
through the snapshot day and unpaid settlement membership. Explicitly current sessions remain
eligible even on or before the latest payment day; untouched historical sessions retain the
posted-day cutoff. Sessions assigned to visible Paid/Processing Payments are excluded. It charts those sessions by recording date and includes
qualifying separate adjustments after that cutoff through the snapshot day. The range expands to
the earliest eligible recording/adjustment, and the heading reports excluded undated verified
sessions. Dashboard status, pending and activity panels use the same assignment selection;
the lifetime balance remains independent of the filter.
Recording dates use `RecordedDate`, falling back to legacy `Date` when absent. No
timestamps or historical earnings are rewritten. The view reads persisted sessions, payouts and
catalog artwork, calculates the complete presentation, then publishes on its dispatcher. A saved
change arriving during a read queues another read. Failed reads keep the previous snapshot and
expose stale/retry feedback; first-load failures hide unavailable totals.

| Display | Definition and date basis |
| --- | --- |
| Earned KPI / purple trend | Verified stored session earnings by recording day, plus qualifying signed separate Bonus/Earnings correction amounts by payout date, using `EarningsPeriods` and `PayoutAccounting.IsSeparateEarnedAdjustment`. Included-in-session, unresolved and ineligible adjustments are excluded. |
| Pending KPI / dashed trend | Stored Pending session earnings by recording day; separate from earned amounts. |
| Available balance | Lifetime `PayoutAccounting` balance: verified unpaid assignments with known posted dates, plus historical sessions after the latest Paid/Processing Payment day. Hidden/ineligible assigned payments return their sessions to current. It is independent of period selection and is not total earned minus payouts. Unknown posted dates are excluded and counted in the notice. |
| Payouts KPI | Existing `PayoutAccounting` Paid/Processing Payment amounts in the selected payment-date range; payment signs retain the shared accounting treatment. Payments can pay earnings recorded in another period. |
| Hours played | Stored duration of Verified + Pending period recordings, summed without modifying duration precision. Rejected/Not Uploaded hours are excluded. Statistics' verified-only hours are a subset. |
| Session mix | All four stored statuses by period recording date. Verified percentage denominator is **all period sessions**, including Rejected/Not Uploaded; no sessions displays “No sessions.” Status amounts are recorded values, not a claim that Rejected/Not Uploaded earnings were earned. Adjustments do not add sessions. |
| Top games | Top four saved game identities by verified period session earnings; archived identities remain eligible. Unlinked names remain explicit without invented game links. Bars use the highest game amount as denominator, or zero when all amounts are zero. Adjustments are not attributed to games. |
| Recent activity | Latest five period recordings ordered by recording timestamp, then stable session ID. Existing game-detail and Sessions navigation routes through MainWindow. |
| Lifetime notice | Existing total earned and total paid/processing figures remain available. Their all-history rules, including future records, are unchanged; period analytics exclude future recording/payment dates. |

Daily values exposes verified, signed adjustments, earned and pending values alongside the chart.
The chart supports negative totals and displays legitimate zero/offsetting activity; absence of
eligible records has distinct empty guidance. USD formatting rounds display only. Exact persisted
decimals are retained in reporting rows. Whole-period earned amounts reconcile with Statistics at
the same recording-date range; session counts/status totals reconcile with Sessions at identical
date/status filters. Top-game totals exclude adjustments and are limited to four identities.

Verification uses isolated SQLite fixtures and actual WPF resources, including narrow/default/wide
renders, negative/zero/empty/error/stale states, bounded scrolling, programmatic focus/navigation
and a 144-DPI export. Physical keyboard/mouse/wheel behavior, screen-reader announcements and real
monitor-DPI transitions remain unverified; owner-operated manual tests are not an acceptance gate.

## Statistics charts

Statistics reuses `MetricPanel`, Dashboard metric text styles, `SurfacePanel`, `SectionHeading`,
`MetricProgressBar`, and the shared ComboBox. Today uses accent text; other period values use primary text.
Chart lines and game bars use `AccentColor`, gridlines use `DefaultBorderColor`, and axes use
`SecondaryTextColor` at `FontSizeSupporting`. `ChartToolTip` in Controls.xaml uses the existing
surface, text, border, padding, and font tokens; it introduces no palette or type scale.

Measure currency tick labels before reserving the axis gutter. Reduce date-label density with plot width.
Charts stack below 820 DIPs of chart-region width; period cards stack below 640 DIPs of card-region width.
Empty ranges show guidance instead of a zero line, while real zero-valued or offsetting activity retains
its chart. The bounded, recycling cap and trend-value viewports remain available.

## In-app copy

Use short headings, explicit action labels and field units. Omit page-description subtitles,
instructions for obvious controls and repeated success/audit reassurance. Imports use
**Preview import** before saving; unresolved game links appear under **Unlinked** in Sessions.

Dashboard, Payments and Statistics expose accounting rules through **How calculated**.
Keep relevant exclusions, stale-data warnings, unknown dates, historical rates, offer editions,
currencies and verification dates visible. Distinguish a failed save from **Saved · refresh failed**.
Steam sync/privacy guidance lives in Settings' **Sync details**. Keep destructive-action and
restore consequences at confirmation. Visible-copy reductions must preserve accessible names,
keyboard help, live feedback, exact values and source/audit evidence.
