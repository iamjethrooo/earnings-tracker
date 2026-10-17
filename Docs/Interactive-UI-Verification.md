# Interactive WPF verification

Use this checklist for UI acceptance alongside [Design system](Design-System.md),
[storage isolation](Database.md), and [automated testing](../Tests/README.md).
Record PASS, FAIL, or NOT VERIFIED for each check; include build identity, Windows
version, monitor resolution/scaling, window size, fixture, and evidence. Automated
fixtures and UI Automation trees do not establish Narrator speech or native DPI behavior.

## Acceptance scope

The checklist below retains the full verification procedure for future use. PRO-9's
owner approved a reduced completion scope on 2026-10-17: representative native navigation,
keyboard/focus, imports, scrolling and window resizing, supplemented by the existing
107 passing WPF checks for popup handling, bounded realization, busy/commit guards and
recovery. Automated results supplement the recorded native interactions; they do not
establish that an unperformed native check passed.

Narrator listening/screen-reader verification, the full Windows 100%/150%/200% DPI
matrix and mixed-monitor DPI testing are **NOT VERIFIED — deferred indefinitely by
the project owner**. No follow-up is scheduled. Resume only on owner request or a
concrete defect/new requirement; no replacement issues are required for these exclusions.

The reduced scope accepts the combined native and automated evidence for normal usage.
Exhaustive native Tab order and popup/calendar traversal, long-history Page Up/Down and
Home/End, prolonged smoothness, large-import busy feedback and native fault/retry or
saved-refresh-warning injection remain unverified native coverage limits. Controlled
automated checks cover the relevant guards/recovery paths, not every native interaction.
These gaps are not successful tests or confirmed defects. PRO-9's detailed evidence and
scope assessment are retained in `.local-docs/PRO-9-INTERACTIVE-UI.md` when available.

## Safe setup

Build into a separate output directory. Launch that executable with a child-process-only
absolute `NERVE_TRACKER_DATA_DIRECTORY` pointing to a new scratch folder. Remove the
child's `NERVE_TRACKER_LOCAL_UPDATE_SOURCE` override. Confirm Settings' Local data path
matches the scratch folder before importing anything. Do not launch an installed app,
copy personal records, change credentials, or run installers/updaters for this checklist.
Retain the scratch profile and receipts. Use synthetic imports only.

Example synthetic session input (a bundled game, invented recording):

```text
PlateUp!
PlateUp!
PlateUp! · Oct 15, 2026 3:13 AM · 24m · File deleted
+$0.61
Verified
```

Example synthetic payment input:

```text
|10/16/2026|—|Payment|UI verification synthetic payment|Paid|$0.25|UI-VERIFY-PAY-1|
```

## Native checklist

1. **Keyboard:** Start with Tab and Shift+Tab, use arrows in sidebar navigation,
   Enter/Space on actions, and Escape in dialogs/editors. Visit Dashboard, Sessions,
   Games, game details, Payments, Statistics, and Settings. Check visible focus even
   while hovered, logical order, selected navigation, and invoker focus after closing.
   Test search, sort, status chips, bookmark, ownership, dropdowns and date-picker popups.
   Escape must close an open popup before its containing dialog. Check documented
   Sessions Enter/Escape behavior; do not assume undocumented global shortcuts exist.
2. **Narrator (human listening required):** On the isolated window, read each page and
   perform an import using keyboard only. Confirm useful control names, selected,
   expanded, checked, read-only and disabled states, signed currency, validation and
   saved/error feedback. Expand Statistics' View trend values and read dates/values.
   Confirm announcements are audible and timely without excessive repetition.
3. **DPI/window matrix (human display setup required):** At Windows scaling 100%,
   150%, and 200%, repeat minimum (1000×600 DIPs), restored (1200×720 DIPs), and
   maximized layouts on a display/work area large enough for those logical sizes.
   Check each page and both import dialogs for clipped text, hidden actions, popup
   placement, readable focus, artwork and scrollbars. Move between monitors with
   different scaling, then restore/maximize and open dropdown/calendar popups again.
   Record resolution/work area as well as scaling; a 1920×1080 display at 200% has
   less logical space than the window minimum. Record that case separately.
4. **Imports/modals:** For Sessions → Import Sessions and Payments → Import Payouts,
   test empty input, malformed input, valid preview, Back/repreview, save, Done and
   reimport. In the payout preview expand category lists. Tab/Shift+Tab must stay
   inside the modal; background controls must be unavailable. With a sufficiently
   large synthetic fixture observe progress, disabled commit/close actions, and
   recovery afterward. Do not attempt deletion or force termination to test guards.
   Games → Add Session is intentionally hidden for v1.0.0; it is not an acceptance flow.
5. **Scrolling/charts:** With large synthetic histories/catalogs, use wheel, scrollbar
   drag, Page Up/Down and Home/End. Reach first/last rows, filter and revisit details.
   Resize Statistics repeatedly; verify chart stacking, axes, date density, trend
   values and cap-table access. Automated container-count checks establish bounded
   realization; native smoothness and keyboard reachability need separate observations.
6. **State/recovery:** Check empty history and filtered-empty guidance, loading,
   saved refresh, and retained rows/drafts after refresh failure with Retry.
   Observe first-load failures without invented zero totals. Use controlled synthetic
   fault fixtures only; saved-but-refresh-failed messages must not invite a second save.
   Reuse automated paused-worker/fault coverage when a native fault fixture is absent,
   but keep the native result NOT VERIFIED.

Keep execution reports and screenshots under `.local-docs/` / ignored `artifacts/`.
Any failed or unperformed item required by the agreed acceptance scope remains open;
record exact reproduction steps before changing production code. Keep excluded coverage
explicitly NOT VERIFIED even when the reduced issue scope is complete.
