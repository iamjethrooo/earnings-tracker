# Earnings Tracker {{version}}

Windows x64 · self-contained · stable application update channel.

- Separate the application installation from persistent data, with verified automatic first-launch preservation from the former tracker installation.
- Create verified standalone database backups in Settings. A confirmed restore closes the app and applies the backup on next launch, after creating a recovery snapshot.
- Save the Games starting sort, bookmarks-only filter and archived-game visibility. Include startup/recovery and import-concurrency fixes.
- Continue using Recordings-page session imports, audited financial history and saved catalog data offline. Game Details Add Session remains unavailable.
- Target full-package application updates only. Settings controls checking/downloading and restart; delta updates are deferred. GitHub discovery/download must be verified separately before claiming delivery works.

This is an **unsigned build**. Windows may show an unknown publisher or security warning. It has no verified Authenticode publisher identity. Release preparation does not authorize distribution: limited distribution requires the mandatory safety/reliability checks and separate owner approval. Broad distribution also requires trusted signing and timestamp verification. Keep Windows security controls enabled.

Application update configuration: {{configuration}}. Catalog updates are independent.

Verification scope: real-account native install/preservation, offline full-package update, same-version repair and new-app uninstall/reinstall passed in PRO-21 Stages 1–4A. Existing automated backup/restore evidence is retained. Installed synthetic Settings restore on the exact v1.1.0 candidate is **NOT VERIFIED — owner-approved deferred for personal use**. Interrupted installer/updater fault injection, legacy removal and delta verification are deferred, not passed. Exact final-candidate offline startup, shortcut and Windows warning observations remain unverified; earlier native observations do not certify them. No automatic binary rollback is promised. Keep independent verified backups and durable recovery copies; personal restore/manual recovery requires separate approval. Personal exceptions do not automatically apply to recipients.

Installation transition: this package uses `EarningsTracker.App` and the `win-x64-app-stable` channel. Install it alongside the former tracker and launch it successfully before uninstalling or repairing the former installation. Production first launch automatically copies and verifies saved data/backups into `%LOCALAPPDATA%\EarningsTracker.UserData`, retaining the originals. Use the new app afterward; edits in the old app will not appear in it. If startup reports a storage problem, leave the former installation/data intact and resolve it before any uninstall. No manual folder movement is needed. Never install into a data folder.

Leave the legacy installation dormant; its removal is unnecessary and remains deferred. Already-distributed legacy uninstall/repair tools can delete originals before successful preservation. Application updates cannot bridge the old identity/channel automatically.

Third-party licenses and notices accompany the application under `Licenses`. They do not license the owner's original source or grant redistribution rights for bundled game metadata, cover artwork or provider prices. Provider-rights decisions and native-updater transitive notice coverage remain unresolved; public publication is not approved. These notes are finalized candidate wording, not evidence that the release has been published or delivery verified.
