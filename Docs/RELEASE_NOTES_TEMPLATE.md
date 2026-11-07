# Earnings Tracker {{version}}

Windows x64 · self-contained .NET 10 · unsigned · full-package updates.

Application update configuration: {{configuration}}.

- Assign verified sessions to Current payout or a specific Paid/Processing payment. Review covered
  sessions when adding or importing payments, with visible totals and differences.
- Correct historical payout membership through the session editor. Newly verified historical
  sessions join Current payout unless already assigned; unchanged historical records retain their
  previous date-based treatment. Assignment changes are audited.
- Optionally link Steam in Settings to mark matching catalog games as owned. Filter by Steam
  ownership and sync manually or at most daily when Games opens. Failed syncs keep saved ownership;
  manual ownership and purchase costs remain separate. Steam Game details must be public.
- Cleaner Games and Game Details layouts, shorter Game Pass availability text, and no Game Pass
  link in subscription details. Checked dates, expiry dates and stale-availability warnings remain.
- Clearer payout coverage, import results and financial summaries across Dashboard, Payments,
  Sessions and Statistics, plus simpler Settings and update feedback.

This version adds session payout-assignment fields to the local database on first launch. It does
not automatically reassign existing sessions. Keep an independent backup before upgrading.
Steam API keys are stored in Windows Credential Manager and excluded from database backups;
the linked account ID and cached library are included.

This is an **unsigned Windows release**. Windows may show an unknown publisher or Windows SmartScreen
warning, or block installation under its security policy. Keep Windows security protections enabled.
SHA-256 checksums verify artifact integrity, not publisher identity. Publication requires separate
approval for the exact release; local preparation is not a publication announcement.
Unsigned releases are permitted under the standing policy; no certificate or version-specific
unsigned-release exception is required.

Application identity remains `EarningsTracker.App`, on `win-x64-app-stable`. Persistent data and
backups remain separate from installation in `%LOCALAPPDATA%\EarningsTracker.UserData`. Do not install
into a data folder. Users of the former tracker identity must retain the original installation/data
until preservation succeeds; old-identity in-app updates do not bridge the identity change. Leave
legacy uninstall/repair operations alone until the data is safely preserved. Keep verified backups.

Exact installer/update behavior for this candidate has not yet been verified. Native keyboard,
screen-reader and monitor-DPI behavior remain documented limitations. Third-party notices accompany
the application under `Licenses`. They do not license the owner's
original source or grant rights to bundled metadata, artwork or provider prices. Accepted
catalog/artwork inclusion does not establish independently verified redistribution rights.
Previously documented native dependency-notice completeness limitations remain disclosed.
