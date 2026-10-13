# Nerve UI design system

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
- **Badges:** history and compact details deliberately have different Pending/duration treatments. `DetailedSessionStatusText` preserves the existing order: status colour, zero-duration warning, then the not-uploaded override. Do not combine these variants by changing status rules.
- **Transaction badges:** `TransactionBadge` and `TransactionBadgeText` share Sessions' control radius, padding, and supporting-size text for transaction types, statuses, and reconciliation labels. Callers supply semantic brushes and descriptive text. Payment cyan denotes Payment only; unknown/Other types are neutral. Paid/Verified use success, Pending/Processing warning, Failed error, and historical/unknown statuses neutral.
- **Payout import details:** expandable sections retain their category/count headings and use shared compact surfaces, headings, and item containers. Populated sections use a recycling ListBox with a 240-DIP maximum viewport inside the modal scroller; this is a viewport bound, not a new spacing token. Skipped/failed sections open for review when populated. Read-only selection does not resolve a match; the existing explicit skip choice and conflict block remain authoritative.
- **Detail badges:** `DetailedSessionStatusBadge` supplies the matching container for raw session records, with the same duration/not-uploaded precedence as detail text. History badges still bind to session item wrappers. Use the appropriate variant for the binding context and retain their deliberate status differences.

## Hosting and accessibility

Apply `PageHost` explicitly to `MainContent`. Pages own their 20-DIP inset on all sides, so the host defaults to zero padding. The template consumes explicit padding, border and content alignment, forwards content templates, and applies margin only through normal layout. Ordinary ContentControls retain their normal WPF templates.

Preserve the shared focus visual, primary-button contrast, popup parts, accessible names, live feedback, modal keyboard containment, commit close protection, and virtualization established in Phases 1–3. A visual extraction must not remove a local trigger, hide an action, reset a binding context, or add an unintended tab stop.

Editor focus must remain visible while hovered: `ModalTextBox` applies its keyboard-focus border after its hover border and uses the shared disabled-opacity treatment. Search hints use `SecondaryTextColor` without an extra opacity reduction. See [the application consistency audit](UI-Design-System-Audit.md) for remaining compatibility variants, contrast concerns, and manual regression checks.

Check resource lookup and trigger precedence in real WPF fixtures, not just XML compilation. Render minimum/default/wide layouts and inspect both loaded artwork and missing covers. Check final foregrounds explicitly when replacing a local style: an explicit TextBlock style does not automatically inherit the implicit one.

## Recovery and feedback (Phase 5)

Use `EmptyStateText` for quiet empty-list guidance, with a clear distinction between no data and no filter matches. Show loading and failure messages in a separate `FeedbackPanel` when rows are retained; describe stale data and provide the existing retry action. Avoid repeating the same error in the count summary.

Settings feedback uses verified/success, pending/warning, rejected/error, and secondary/neutral tokens alongside descriptive text and polite announcements. Mixed update results retain their warning meaning. Keep prices, editions, availability, and stale-data warnings visible; reviewed provenance can use progressive disclosure.

Sidebar routing uses `NavigationPage` identifiers rather than labels or list indices. Product versions must be explicit release metadata; default assembly versions remain diagnostic information.

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
