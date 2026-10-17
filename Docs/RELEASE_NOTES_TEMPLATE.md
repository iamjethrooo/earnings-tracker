# Earnings Tracker {{version}}

Windows x64 · self-contained · stable application update channel.

- Track sessions, verified earnings, payouts, signed adjustments and audit history.
- Initialize a new per-user database with the bundled game catalog and default settings.
- Preserve existing records during application updates and catalog refreshes.
- Check and download application updates from Settings; restart only when you choose and when editing/import/database work is finished.

This is an **unsigned build**. Windows may show an unknown publisher or security warning. It has no verified Authenticode publisher identity. Release preparation does not authorize distribution: limited distribution requires the mandatory safety/reliability checks and separate owner approval. Broad distribution also requires trusted signing and timestamp verification. Keep Windows security controls enabled.

Application update configuration: {{configuration}}. Catalog updates are independent.

Installation transition: this package uses `EarningsTracker.App` and the `win-x64-app-stable` channel. Install it alongside the former tracker and launch it successfully before uninstalling or repairing the former installation. Production first launch automatically copies and verifies saved data/backups into `%LOCALAPPDATA%\EarningsTracker.UserData`, retaining the originals. Use the new app afterward; edits in the old app will not appear in it. If startup reports a storage problem, leave the former installation/data intact and resolve it before any uninstall. No manual folder movement is needed. Never install into a data folder.
