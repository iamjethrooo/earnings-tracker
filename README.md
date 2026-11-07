# Earnings Tracker

A Windows desktop app for tracking Nerve gaming rewards: sessions, verified earnings, payouts, adjustments, and reconciliation. Built with WPF, .NET 10, and SQLite.

This is an independent project, unaffiliated with Nerve or other platforms.

## Features

- Import sessions and payouts, reconcile earnings, and retain an audit history of changes.
- Review earnings, payments, and trends in the Dashboard and Statistics pages.
- Browse a bundled games catalog with artwork, store offers, Philippine prices, and PC Game Pass availability.
- Track ownership, purchase costs, and break-even estimates; optionally sync Steam ownership.
- Use saved data offline and manage database backups and restores in Settings.

Earnings are displayed in USD; catalog prices use PHP.

## Build and test

Requires Windows and .NET SDK **10.0.402**, pinned in `global.json`, with Windows Desktop support. From the repository root:

```powershell
dotnet restore EarningsTracker.slnx
dotnet build EarningsTracker.slnx --no-restore
dotnet test EarningsTracker.slnx --no-build --no-restore
```

No provider credentials are needed to build or run the isolated tests. See [testing](Tests/README.md) for coverage and additional checks, and [build and release](Docs/Release.md) for packaging instructions.

## Data and optional integrations

Production data lives in `%LOCALAPPDATA%\EarningsTracker.UserData\EarningsTracker.db`, separate from the installation. Debug builds use `EarningsTracker.Development`. Startup applies database migrations; use an explicitly isolated profile for manual testing. See [database, backups, and recovery](Docs/Database.md).

The bundled catalog works without API keys. Settings accepts an optional IsThereAnyDeal key for live prices. For Steam ownership, open **Settings → Steam account**, enter your profile URL or SteamID64 and a Steam Web API key, then choose **Link account**. Steam Game details must be public. Keys are stored in Windows Credential Manager, outside database backups. Steam sync preserves manual ownership and purchase costs.

## Documentation

- [Design system](Docs/Design-System.md)
- [Catalog maintenance](Tools/CATALOG.md)
- [Distribution terms](Docs/Distribution.md) and [third-party notices](Resources/ThirdPartyNotices/README.md)

Original application source and assets are all rights reserved; third-party material is governed by its respective terms.
