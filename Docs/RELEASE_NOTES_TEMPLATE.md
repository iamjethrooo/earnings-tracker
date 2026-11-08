# Earnings Tracker v{{version}}

{{changes}}

## Installation and Distribution Notes

Platform: Windows x64 · Self-contained .NET 10

Release: Unsigned · Stable full-package updates

Update configuration: {{configuration}}.

### Windows Security

This is an **unsigned Windows release**. Windows may show an unknown publisher or Windows SmartScreen
warning, or block installation under its security policy. Keep Windows security protections enabled.
SHA-256 checksums verify artifact integrity, not publisher identity. Publication requires separate
approval for the exact release; local preparation is not a publication announcement.
Unsigned releases are permitted under the standing policy; no certificate or version-specific
unsigned-release exception is required.

### Installation and Data Preservation

Application identity remains `EarningsTracker.App`, on `win-x64-app-stable`. Persistent data and
backups remain separate from installation in `%LOCALAPPDATA%\EarningsTracker.UserData`. Do not install
into a data folder. Users of the former tracker identity must retain the original installation/data
until preservation succeeds; old-identity in-app updates do not bridge the identity change. Leave
legacy uninstall/repair operations alone until the data is safely preserved; retain verified backups.

### Verification and Licensing

Exact installer/update behavior for this candidate has not yet been verified. Native keyboard,
screen-reader and monitor-DPI behavior remain documented limitations. Third-party notices accompany
the application under `Licenses`. They do not license the owner's
original source or grant rights to bundled metadata, artwork or provider prices. Accepted
catalog/artwork inclusion does not establish independently verified redistribution rights.
Previously documented native dependency-notice completeness limitations remain disclosed.
