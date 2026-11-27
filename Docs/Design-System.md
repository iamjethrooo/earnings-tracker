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
- **Type:** compact page title (`TabName`) 25 DIPs; compact section title 16; body 14; compact body 13; supporting text 12; badge 11; metric 30. The overview layouts opt into `PageTitle` (32) and `ContentHeading` (24) for clearer hierarchy; existing compact pages retain their sizes. Use the named heading and metric styles rather than duplicating setters. Existing compact table/status sizes remain deliberate compatibility variants; new essential information should use at least supporting-text size.
- **Spacing:** 4/8/12/16/20/24 DIPs. `PageInset` is 20 on all sides, including below bounded list viewports. Use named padding tokens and retain local grid-specific gaps where they describe layout.
- **Shape:** control radius 6, card radius 8, dialog radius 12. Existing compact panels retain radius 7. Do not change geometry merely to replace a literal with a token.
- **Panels:** `MetricPanel` is the shared base for `DashboardStatPanel` and `GameDetailPanel`. `SurfacePanel` is the compact bordered surface; `FeedbackPanel` adds the standard bottom gap. `OpenSectionPanel` groups content with a top separator and 16-DIP spacing instead of another enclosing card. Apply local padding or margins only when a specific layout needs a documented variant.
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

Hover help uses the shared dark `ToolTip` style, including `ChartToolTip`. Tooltips use Inter
at the 13-DIP compact-body size and a 360-DIP maximum width. Plain strings wrap through
the template's string DataTemplate; WPF's default generated string presenter does not
honor an implicit TextBlock wrapping style. Explicit text content also wraps, while local
semantic foregrounds, content templates and placement-target bindings remain available.
Keep complete source/accounting explanations and chart values rather than truncating them.
Native opening delays, placement and disabled-control help remain unchanged; popups do
not take keyboard focus. `HoverFeedbackTests` checks wrapping, long unbroken text, rich
content, live placement-target bindings and 96/144-DPI renders without starting the app.

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

Action dropdowns use the shared `ContextMenu` and `MenuItem` templates in `Controls.xaml`.
Set the popup surface and item template together: setting only Background leaves the
Windows menu's white gutter and selection treatment visible. Menus use the dialog surface,
purple highlight/focus, muted disabled items and the existing destructive foreground.
The `MenuItem.SeparatorStyleKey` resource themes native menu separators. Headers retain
access keys, icons/checkmarks, gesture text, commands and submenus. Normal and searchable
ComboBoxes keep their existing selection, search, recycling and keyboard behavior.
`DropdownThemeTests` renders selectors and action/submenus at 96/144 DPI and verifies
dark pixels, destructive text, separators and native command invocation.
It also opens actual page and editor selectors with isolated synthetic records,
including nested Games filters, ratings, Settings and modal selectors.

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

The shell opens with the selected compact labeled rail: 116 DIPs wide, the existing 32-DIP E mark,
24-DIP Fluent icons above 13-DIP captions, and quiet purple selected tiles. Captions remain visible
for Dashboard, Sessions, Games, Payments, Statistics and bottom-anchored Settings. The chevron has
an explicit Expand navigation / Collapse navigation accessible name and tooltip. Expansion uses
224 DIPs with inline 20-DIP icons, 14-DIP labels and the familiar selection marker. Glyph font sizes
are explicit so icon bounds do not inherit the smaller caption scale.

Below 740 DIPs of window height the rail uses 60-DIP rows, 20-DIP icons and tighter gaps so all six
destinations fit at the 1000×600 minimum window. Normal rail rows are 76 DIPs; expanded rows are
44 DIPs. Both states retain keyboard focus and native list selection. Expansion is per-window UI
state, not a saved database preference; it changes width and presentation without recreating the
active page. Modal containment and session draft/navigation guards still apply. `MainWindow` owns
routing and game-detail entry; pages recreate on navigation, without a page cache.

Overview headings use `PageTitle`; compact pages retain `TabName`. Both use a short, wrapping `PageDescription` beneath. Existing page
actions sit to the right where present. Use `PrimaryButton` for the main action (such as import or
Save changes), and `SecondaryButton` for alternatives (such as Add payout or Cancel). Do not invent
an action to fill a header. Dashboard, Sessions, Games, Payments, Game Details, Statistics and Settings use the approved open layout while retaining their existing workflows.

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

`NavButton` pairs each page label with a Fluent icon selected by its `NavigationPage` tag, above the caption in rail mode and beside it when expanded. Icons follow the row foreground and add no focus stop. Shared scrollbars use a 12-DIP interaction lane with an inset rounded thumb, a quiet charcoal rail on hover, and purple while dragging. Both orientations retain viewport sizing and native page-scroll commands, with a 48-DIP minimum thumb length for long documents. The shared ScrollViewer template preserves its named scrolling parts and content scrolling, with a transparent corner when both bars appear.

## Compact Sessions explorer (PRO-47)

Sessions uses 64-DIP recycling rows with compact covers and a two-line recording date/time. Game, recording date/time,
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
the 390-DIP adjacent panel (with a 24-DIP gap) becomes a drawer up to 390 DIPs wide. The covered page is disabled
and Tab cycles within the drawer. Close, Escape in read mode and the scrim return to browsing;
a draft blocks dismissal with Save/Cancel guidance. Escape while editing cancels the draft. Open
ComboBoxes/calendars consume their own Escape first. Reparenting across the breakpoint retains the
draft and restores editor focus. Read/edit content scrolls while actions stay pinned.

The inspector wraps the full game title, emphasizes earnings, and uses neutral field labels.
Open game details is an explicit secondary link. Source identifiers and audit evidence stay under
Source & history. Edit session remains the primary pinned action; More actions exposes the existing
confirmed, audited deletion without adding a deletion shortcut to the table.

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

Saved games remain available while update checks run. Progress appears alongside
the library status and competing refresh actions wait until the current check
finishes. Saving a cap reset updates the open game's cycle progress in both views
and reevaluates the Time left to earn filter before returning to the library.

Games uses four primary columns: Game, Rate · USD, Current cycle and Acquisition. A single wide-layout header replaces repeated row captions; compact rows restore those captions. Game includes compact artwork,
title, genre, stores, ownership and bookmarking. Rows use quiet separators and neutral title links.
Ownership actions appear for owned, selected, hovered or keyboard-focused rows. Full-cycle potential stays in Game Details.
Below 1,050 DIPs of page-region width the comparison
fields move beneath the title. Missing artwork has a readable fallback. Selecting a row keeps
the library open; its title or Enter opens details. Returning to Library retains filters,
selection and scroll position and restores focus when the original item is still available.
The identity and comparison columns share the available row width; rate, cycle
progress and acquisition align across games without a fixed-width inset. Cycle bars use verified
linked sessions after the saved cap-reset date; pending, rejected, unlinked and pre-reset sessions
do not contribute. Zero progress hides the track. Hours use `played / cap` and the bar clamps
to 100% while text retains over-cap hours. The row shows the remaining minutes, a reached or
unknown cap, or why the game has no active earning rate.
Library acquisition evidence shows the store, checked date and stale/expired warnings; edition
and region remain available in Game Details.

The primary toolbar row contains an expanding search field with an icon, a separately labeled
sort selector, Filters and a secondary Clear filters action. The quick-filter row contains Playable now,
Time left to earn and Bookmarked. Toolbar controls are 34 DIPs high, with 8-DIP filter gaps and purple
selected states. Filters holds Free-to-play, Installed, Owned, Multiplayer, genre, store, Steam library,
PC Game Pass and Show archived in a bounded scrolling popover. Its count includes every active
secondary option, including Show archived and nondefault genre/store selections. Toggling an option
keeps the popover open; Escape closes it and returns focus to Filters. Tab stays within the popover.
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

**Newest order:** `Data/game-order.json` is the embedded, reviewed NERVE roster sequence supplied
on 2026-11-21, excluding Slay the Spire 2. Its 582 entries distinguish name, hourly rate, cap and
archive status; declared aliases retain local names such as Car Mechanic Simulator 2018. MAVRIX,
Predecessor ($2.00) and Wreckreation lead the list. Predecessor ($1.50) belongs between The Long
Drive and ArcheAge (reference position 383), regardless of when its local record was created.
Saved active/archived groups and archive filtering are retained. A unique name/rate/cap match
retains its roster position after an archive toggle; repeated active/archived terms require the
matching archive state. This ordering does not change saved titles or archive flags: Blue Archive
and Goddess of Victory: Nikke remain active in the existing seed, unlike the supplied reference.
Those saved-state differences can change absolute row numbers outside the matching reference.

Games outside this reviewed sequence, including future catalog additions, use descending local
ID ahead of known entries within their active/archived group. Equal roster positions use descending
local ID for a deterministic tie-break. Other sort modes retain their existing behavior. Maintain
the order asset when reviewing a new source sequence; do not renumber database records or reorder
the legacy game seed to change the display. The ordering asset contains public roster fields only.

Details has a pinned Games action and a bounded, recycling session-history viewport. Its finite
header uses 90×120-DIP artwork, a 32-DIP title, identity, rate and bookmark controls. Open sections
replace enclosing cards. Earnings & cycle groups verified cycle usage/progress, remaining potential,
full-cycle maximum, stored verified earnings and the audited cap-reset editor. Ownership, purchase
cost and personal ratings occupy the wider left column; acquisition offers, subscription evidence
and History & evidence occupy the right. Subscription warnings retain saved dates and uncertainty.
The columns stack below 900 DIPs of page-region width. About this game reveals the complete wrapping
description on request, keeping session history closer to the earning summary. Offer headings
identify store/edition/region, with prominent source-currency prices;
checked dates, saved quotes and source evidence remain accessible. Archived opportunities are labeled
historical. Saved same-name rate identities and cap-reset audit values are separate read-only
disclosures. Full-width session history below the finite header uses aligned recording, duration,
USD earnings and status columns. The single bounded recycling viewport retains large-history
scrolling; Session history scrolls to the existing status filters and rows.
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

The Dashboard uses an open all-time account summary, a selected-period metric strip, a full-width
earnings chart and aligned recent-session rows. Unpaid verified earnings is the main account figure;
Paid and Processing appear separately, with explicit scope and receipt qualifications. Pending's
helper reads “Awaiting verification.” Keep the missing-posted-date notice visible below the account
summary rather than inside a disclosure. Calculation details retain the lifetime totals.

The heading and primary Import sessions action stay available while the body scrolls. The period
selector sits beside Period overview and its date range, so it clearly applies to the metrics,
chart and recent sessions below. Preserve Current payout as the starting selection. Main sections
use typography, spacing and thin dividers rather than individual metric cards. Reuse the shared
shell, tokens, artwork, status variants, focus treatment and buttons.
Dashboard's local title and main section headings use 32 and 24 DIPs respectively to establish the
open overview's hierarchy. Sessions, Games, Payments, Game Details, Statistics and Settings opt into the same scale
through the shared overview styles.

Below 850 DIPs of page-region width, the four metrics form two columns and recent rows place the
recording timestamp and duration beneath the game name instead of in separate columns. Earnings
and labeled verification remain visible. Recent sessions still contains at most five recordings
in a 240-DIP recycling viewport; Daily values keeps its separate bounded viewport. Session mix and
top earning games remain available in the initially collapsed More insights disclosure. Its panels
share a row at wider widths and stack at narrower widths. First-run guidance retains import actions;
failed reads retain stale/retry feedback and never show unavailable totals as zero.

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
| Earned KPI / purple bars | Verified stored session earnings by recording day, plus qualifying signed separate Bonus/Earnings correction amounts by payout date, using `EarningsPeriods` and `PayoutAccounting.IsSeparateEarnedAdjustment`. Included-in-session, unresolved and ineligible adjustments are excluded. |
| Pending KPI / amber bars | Stored Pending session earnings by recording day; separate from earned amounts. |
| Unpaid verified earnings (all time) | Lifetime `PayoutAccounting` balance: verified unpaid assignments with known posted dates, plus historical sessions after the latest Paid/Processing Payment day. Hidden/ineligible assigned payments return their sessions to current. It is independent of period selection and is not total earned minus payouts. Unknown posted dates are excluded and counted in the notice. |
| Paid + processing KPI | Existing `PayoutAccounting` Paid/Processing Payment amounts in the selected payment-date range; payment signs retain the shared accounting treatment. Payments can pay earnings recorded in another period. |
| Hours played | Stored duration of Verified + Pending period recordings, summed without modifying duration precision. Rejected/Not Uploaded hours are excluded. Statistics' verified-only hours are a subset. |
| Session mix | All four stored statuses by period recording date. Verified percentage denominator is **all period sessions**, including Rejected/Not Uploaded; no sessions displays “No sessions.” Status amounts are recorded values, not a claim that Rejected/Not Uploaded earnings were earned. Adjustments do not add sessions. |
| Top games | Top four saved game identities by verified period session earnings; archived identities remain eligible. Unlinked names remain explicit without invented game links. Bars use the highest game amount as denominator, or zero when all amounts are zero. Adjustments are not attributed to games. |
| Recent activity | Latest five period recordings ordered by recording timestamp, then stable session ID. Existing game-detail and Sessions navigation routes through MainWindow. |
| Lifetime notice | Existing total earned and total paid/processing figures remain available. Their all-history rules, including future records, are unchanged; period analytics exclude future recording/payment dates. |

Daily values exposes verified, signed adjustments, earned and pending values alongside the chart.
Daily earned and pending amounts use separate purple and amber bars rather than connected lines.
Statistics also uses bars for its selected daily/weekly/monthly buckets. The current bucket is
marked with an asterisk and described as incomplete in the caption, tooltip and values view;
Today is labeled Today so far in the calendar summary. Zero amounts remain real zero values,
and negative adjustments extend below the zero baseline.
The chart supports negative totals and displays legitimate zero/offsetting activity; absence of
eligible records has distinct empty guidance. USD formatting rounds display only. Exact persisted
decimals are retained in reporting rows. Whole-period earned amounts reconcile with Statistics at
the same recording-date range; session counts/status totals reconcile with Sessions at identical
date/status filters. Top-game totals exclude adjustments and are limited to four identities.

### Task guidance and review

A profile with no sessions or transactions shows a getting-started panel with direct session
and payout import actions. Those actions use MainWindow's existing navigation and guarded modal
coordinator. Empty analytical panels are collapsed for this first-run state; read failures still
retain the prior snapshot or hide unavailable totals on first load.

Dashboard's all-time summary and Payments use **Unpaid verified earnings** for the existing
`PayoutAccounting` balance. **Paid + processing** names the combined payment total; an adjacent
breakdown distinguishes Paid and Processing. These are reporting labels, not a withdrawal
confirmation. Dashboard's four selected-period metrics and Statistics' calendar summaries explicitly
state their date scope. No calculation, posting-date cutoff or adjustment eligibility changes.

Payments uses three open, neutral-value summaries for Total earned, Paid + processing and Unpaid
verified earnings. Definitions, the processing qualification, reconciliation and undated-session
warnings remain visible above the ledger; How calculated retains the full rules. Transaction history
uses quiet separators and readable rows. Edit selected and More actions sit with the ledger; the
confirmed Hide manual record action remains unavailable for imported payouts and session-derived
rows. Import payouts stays primary and Add payout remains secondary. Reporting and audited mutation
paths are unchanged.

Session importing offers copy instructions and a complete example. A bounded recycling review
list shows new, updated, conflicting, invalid and repeated records, including the resolved local
game/rate identity, recording date, duration, source status and unrounded USD amount. Updates
show the saved values alongside imported values. New-session payout selection updates the review;
previewing does not write records. Error recovery emphasizes Fix input, preserves pasted text and
selects the parser's source line when a line number is available, including wrapped input.
Saved-but-refresh-failed warnings still retire the write and never invite another commit.

The force-verification checkbox explains that the flag and saved posted date survive session
reimport; Verified status controls earned totals. Manual edits to Verified force-verified sessions
still use the existing posted-date validation and audit rules. Session rows have a spoken summary
and visible inspection guidance. Read-only drawers keep the background table legible while
preserving containment; editing uses the stronger scrim and guarded Save/Cancel footer.

Games keeps its list viewport bounded and virtualized. Secondary active filters are named beside
the result count. Library tools & status contains maintenance controls and detailed provider/Steam
status; a concise saved-data/freshness summary remains visible, along with individual offer dates
and availability warnings. Game details puts cap-reset editing under Manage earning cycle with
an explanation; rate, remaining hours/earnings and ownership/access remain in the main flow.

Settings opens **Data & backup** and uses five selected categories rather than section jump links:
Data & backup; Games; Accounts & integrations; Updates; About. The keyboard-operable category list
sits beside the content at wide widths and wraps above it below 1,050 DIPs of page width. Only the
selected category is visible; controls remain mounted to retain preferences, drafts, disclosures
and in-flight work. Selection resets that category's content scroll, while global feedback and the
build/release-history footer stay available. Leaving Settings still clears password fields and
cancels its Steam work.

Data & backup separates Create a backup from Restore from backup. The included/excluded summary,
primary Create backup action, no-overwrite helper, visible restore consequences, Choose backup
entry point and About backups disclosure use existing typography, actions and semantic warning
colors. Body copy uses the 14-DIP body token; helper/evidence copy retains supporting text. The
backup inclusion columns stack below 540 DIPs of content width. The existing compatibility checks,
explicit confirmation, restart workflow and verified recovery snapshot remain authoritative.

Games contains saved display defaults, local-installation preferences, active subscriptions and
currency/region evidence. Accounts & integrations contains optional store-price and Steam setup,
with their credential/privacy guidance and busy controls. Updates separates application updates
from catalog updates/rollback. About retains diagnostics, provider attribution and the local-data
location. Optional price refresh and Steam configuration remain disclosed on request.
Payout forms offer a visible Cancel action. Session deletion and manual-payout hiding use
DangerButton, keeping their existing confirmations, audit records and soft-deletion behavior.
Unchecked checkbox outlines use InputOutlineColor; ownership buttons have a 28-DIP minimum
height and bookmarks retain their 36-DIP target and accessible name.

Verification uses isolated SQLite fixtures and actual WPF resources, including narrow/default/wide
renders, negative/zero/empty/error/stale states, bounded scrolling, programmatic focus/navigation
and a 144-DPI export. Physical keyboard/mouse/wheel behavior, screen-reader announcements and real
monitor-DPI transitions remain unverified; owner-operated manual tests are not an acceptance gate.

## Statistics charts

In **Statistics → Earnings by game**, select **Verified earnings per hour** to rank the top eight
game records in the selected date range, including the 30-day and 90-day views. The rate divides
stored verified session earnings by the total verified hours shown on the row. It uses sessions
linked to a game record with positive playtime and excludes separate payout adjustments. Historical
rate records are ranked separately even when they share a title. For example, sessions earning
$3 in 30 minutes and $9 in 90 minutes produce $6 per hour across 2 hours.

Statistics uses `PageTitle`, `ContentHeading`, `OpenSectionPanel`, Dashboard metric text styles,
`MetricProgressBar`, and shared selectors. Calendar summaries are open columns, with Today in accent
text and other values in primary text. The week/month captions describe the loaded snapshot day,
not the selected analysis range. How calculated retains the accounting definitions.

Earnings analysis places its range selector beside the heading and date/bucket description. The
trend and game ranking share one open region with a quiet divider; the ranking explicitly excludes
adjustments. Exact values remains a bounded, recycling disclosure with dates, earnings and hours.
Current-cycle cap usage stays below the analysis with its own scope, visible unlinked-session
warning, sortable headers, labeled percentages, hourly rate units and a bounded recycling table.
Earnings-period bars and game bars use `AccentColor`, gridlines use `DefaultBorderColor`, and axes use
`SecondaryTextColor` at `FontSizeSupporting`. `ChartToolTip` in Controls.xaml uses the existing
surface, text, border, padding, and font tokens; it introduces no palette or type scale.

Measure currency tick labels before reserving the axis gutter. Reduce date-label density with plot
width while retaining the first and last bucket labels without
collisions. Daily Today labels come from the actual snapshot bucket and appear once; weekly/monthly
labels keep their existing incomplete-period marker. Charts stack below 820 DIPs of chart-region
width, changing the divider to horizontal; calendar summaries stack below 640 DIPs of summary width.
Empty ranges show guidance instead of a zero line, while real zero-valued or offsetting activity retains
its chart. The bounded, recycling cap and trend-value viewports remain available.

## Modal layouts

Import, edit and review dialogs use `DialogTitle`, `DialogLabel`, `DialogFooter`,
`DialogBodyScroll` and `DialogReviewItem` from `Resources/Dialogs.xaml`. Keep the
charcoal surface, 28-DIP inset, purple actions and a single header. The 32-DIP title
matches the open-page hierarchy. Review lists use flat separators, not individual cards.

The shared `Modal` host supports two opt-in attached properties on its content view:
`Modal.OwnsScrolling` delegates vertical scrolling to the view's bounded body, so the
footer remains visible; `Modal.ContentMaxWidth` sets that state's preferred maximum
width. Other content retains the host's original outer scroller and 860-DIP maximum.
Keep the content row constrained with `Height="*"`; an Auto row can put the footer
outside a short viewport. Expansion changes presentation without replacing the view.

Session and payout paste inputs use a two-step indicator, visible field label, multiline
source input and Cancel / Preview import footer. Session copying instructions retain
the complete example. Input is retained on Back or Fix input. Input dialogs prefer
920 DIPs, session review 1,000 and payout review 940; the host still fits the available
window. Session review stacks its rows below 700 DIPs of content width and keeps the
review list in a 360-DIP recycling viewport. It shows exact source amounts, game/rate
identity, recording dates, status, AFK deductions when present, saved values and existing
update assignments. New rows share the visible payout selector. Artwork uses the
existing asynchronous cache. Conflicting, invalid and repeated rows remain explicit;
the separate conflict disclosure remains bounded at 240 DIPs.

The recorded-amount summary includes pending values and explicitly differs from
verified earned totals. Completed session imports prefer a compact 600-DIP dialog with
counts and exclusion/unlinked guidance. Error, success and saved-but-refresh-failed
messages retain their distinct recovery actions. Completed imports continue through
Done; they never offer another commit or Fix input after a successful write.

Payout review keeps category counts, saved/imported changes, skipped/failed details,
coverage previews and explicit ambiguous-row skipping. Payout conflicts still block
commit. Its detail lists retain bounded recycling viewports. Payout editing prefers
700 DIPs and pairs Date / Type and Status / Amount above the reconciliation decision;
the pairs stack below 520 DIPs. `DialogRadioButton` supplies a purple selected dot,
wrapping labels, keyboard focus and a 28-DIP minimum target. Signed amounts, explicit
reconciliation, optional reason and coverage confirmation retain the existing save path.

Catalog additions, local installations and release history remain owned utility windows.
`UtilityDialogWindow` supplies matching dark chrome with a draggable caption, resize
edges, accessible close control and contained keyboard traversal. Catalog rows align
rate and time cap with the title; conflicting titles remain excluded. Local installations
retains search, source filtering, per-title evidence and incomplete-scan feedback.
Release history opens the installed release or the latest included release in development
builds, retains all bundled notes and highlights Before upgrading disclosures. These
windows keep bounded, recycling lists and a visible footer. Mockup text never replaces
bundled release notes, and native file/restore confirmations keep their existing safeguards.

## In-app copy

Use short headings, explicit action labels and field units. Add concise date-scope captions and
guidance for choices that affect reporting; avoid instructions for obvious controls and repeated
success/audit reassurance. Imports use
**Preview import** before saving; unresolved game links appear under **Unlinked** in Sessions.

Dashboard, Payments and Statistics expose accounting rules through **How calculated**.
Keep relevant exclusions, stale-data warnings, unknown dates, historical rates, offer editions,
currencies and verification dates visible. Distinguish a failed save from **Saved · refresh failed**.
Steam sync/privacy guidance lives in Settings' **Sync details**. Keep destructive-action and
restore consequences at confirmation. Visible-copy reductions must preserve accessible names,
keyboard help, live feedback, exact values and source/audit evidence.
