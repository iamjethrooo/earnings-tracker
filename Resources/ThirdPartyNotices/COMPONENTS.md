# Deployed managed package coverage

Derived from the existing 1.1.0 Production `EarningsTracker.deps.json` and exact package nuspecs. SDK/EF design tools, test libraries and packaging-only vendors are not listed as deployed components. This is an inventory, not a grant over the application or provider content. Native Velopack dependency coverage remains open as described in README.md.

| Package | License | Notice files |
| --- | --- | --- |
| FluentIcons.Common/2.1.339.1 | MIT | FluentIcons-LICENSE.txt; Fluent-System-Icons-LICENSE.txt |
| FluentIcons.Wpf/2.1.339.1 | MIT | FluentIcons-LICENSE.txt; Fluent-System-Icons-LICENSE.txt |
| Microsoft.Data.Sqlite.Core/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt |
| Microsoft.EntityFrameworkCore/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt |
| Microsoft.EntityFrameworkCore.Abstractions/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt |
| Microsoft.EntityFrameworkCore.Relational/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt |
| Microsoft.EntityFrameworkCore.Sqlite/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt |
| Microsoft.EntityFrameworkCore.Sqlite.Core/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt |
| Microsoft.Extensions.Caching.Abstractions/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| Microsoft.Extensions.Caching.Memory/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| Microsoft.Extensions.Configuration.Abstractions/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| Microsoft.Extensions.DependencyInjection/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| Microsoft.Extensions.DependencyInjection.Abstractions/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| Microsoft.Extensions.DependencyModel/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| Microsoft.Extensions.Logging/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| Microsoft.Extensions.Logging.Abstractions/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| Microsoft.Extensions.Options/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| Microsoft.Extensions.Primitives/10.0.11 | MIT | Microsoft-10.0.11-LICENSE.txt; Microsoft-Extensions-10.0.11-NOTICES-1.txt |
| SQLitePCLRaw.bundle_e_sqlite3/2.1.12 | Apache-2.0 | SQLitePCLRaw-LICENSE.txt; SourceGear copyright below; SQLite public-domain reference in README.md |
| SQLitePCLRaw.core/2.1.12 | Apache-2.0 | SQLitePCLRaw-LICENSE.txt; SourceGear copyright below; SQLite public-domain reference in README.md |
| SQLitePCLRaw.lib.e_sqlite3/2.1.12 | Apache-2.0 | SQLitePCLRaw-LICENSE.txt; SourceGear copyright below; SQLite public-domain reference in README.md |
| SQLitePCLRaw.provider.e_sqlite3/2.1.12 | Apache-2.0 | SQLitePCLRaw-LICENSE.txt; SourceGear copyright below; SQLite public-domain reference in README.md |
| Velopack/1.2.161 | MIT | Velopack-LICENSE.txt; NATIVE-COVERAGE.md maps actual binaries; NATIVE-SOURCES.md maps 21 additional native-library license texts; full native transitive/static-runtime closure NOT VERIFIED |

SQLitePCLRaw package copyright (from the 2.1.12 nuspecs): Copyright 2014-2024 SourceGear, LLC. This attribution accompanies the verbatim Apache-2.0 license. Microsoft 10.0.11 nuspecs also state: © Microsoft Corporation. All rights reserved. The retained Microsoft license includes its .NET Foundation and Contributors copyright.

The self-contained Microsoft.NETCore.App.Runtime.win-x64 and Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.12 are additional shipped components, covered by their named package licenses and .NET notices in this directory. Inter is embedded and its verbatim OFL notice is shipped separately at ../Inter-OFL.txt.
