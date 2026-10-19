# Third-party notices for the v1.1.0 candidate

These notices apply only to the named third-party components. They do not select a license for Earnings Tracker's original source, grant game-artwork/provider rights, or authorize distribution. Preserve the verbatim copyright, license and applicable attribution texts with every distributed copy.

## Coverage

- [COMPONENTS.md](COMPONENTS.md) maps all 23 managed/runtime-package entries in the existing Production dependency graph to their license texts. Build-only analyzers, EF design tooling and test dependencies are excluded from deployed coverage.
- Velopack 1.2.161 managed library and native launcher/updater/Setup: [top-level MIT license](Velopack-LICENSE.txt). **Native transitive notice closure remains NOT VERIFIED**, as explained below.
- FluentIcons.Common and FluentIcons.Wpf 2.1.339.1: [wrapper MIT license](FluentIcons-LICENSE.txt), plus the separate [Microsoft Fluent System Icons MIT license](Fluent-System-Icons-LICENSE.txt), pinned to the wrapper's upstream icon submodule.
- Microsoft/EF/Extensions 10.0.11: [MIT license](Microsoft-10.0.11-LICENSE.txt) and the package-provided [Extensions notices](Microsoft-Extensions-10.0.11-NOTICES-1.txt). Additional package copyright attribution is retained in COMPONENTS.md.
- SQLitePCLRaw 2.1.12: [Apache-2.0 license](SQLitePCLRaw-LICENSE.txt) and its SourceGear copyright in COMPONENTS.md. Upstream SQLite core itself is [dedicated to the public domain](https://www.sqlite.org/copyright.html); this does not change the wrapper's Apache-2.0 terms. No separate NOTICE was present at the version's repository root; this is not a fabricated NOTICE or a blanket conclusion about every native dependency.
- Self-contained .NET runtime 10.0.12: [runtime license](NETCore-10.0.12-LICENSE.txt), [runtime third-party notices](NETCore-10.0.12-THIRD-PARTY-NOTICES.txt), [Windows Desktop license](WindowsDesktop-10.0.12-LICENSE.txt) and [version-pinned aggregate .NET notices](Dotnet-10.0.12-THIRD-PARTY-NOTICES.txt). Aggregate texts are deliberately retained in full; not every component mentioned in those upstream notices is asserted to ship here.
- Embedded Inter font: the unmodified OFL copyright/license ships separately at `Licenses/Inter-OFL.txt` (alongside this installed `ThirdParty` folder). Source copy: `Resources/Fonts/OFL.txt` in the repository.
- The application's E icon is generated from its source SVG, not an imported game logo. No new license for that original work is granted here. Provider-sourced game artwork is a separate unresolved category.

[SOURCES.md](SOURCES.md) records exact upstream commits/package versions and SHA-256 hashes of the unmodified texts. No license text was synthesized from an SPDX identifier. The identified managed libraries, self-contained Microsoft runtimes, font and icon licenses have supplied texts; this is not yet a certified complete notice set for the native Velopack executables or provider assets.

## Remaining native-updater uncertainty

The focused binary review is now recorded in [NATIVE-COVERAGE.md](NATIVE-COVERAGE.md): Setup/stub/updater code sections match their actual vpk 1.2.161 x64 vendor binaries, and 21 verified license texts for eleven confirmed native libraries have been added with [archive provenance](NATIVE-SOURCES.md). This closes those identified notice omissions, **not** the full per-binary transitive/static-runtime inventory. The exact remaining gap and the bounded stop decision are documented there; no indefinite audit or unrelated tool coverage is requested.

Pinned Velopack native `src/bins/Cargo.toml` uses Rust libraries (including image, ZIP, Zstandard and Windows/WebView bindings) beyond its .NET managed dependency graph. Its checked-in vendor notice file primarily describes packaging-only tools and Linux components; copying that file would misleadingly claim those vendors ship in this Windows app and would not establish native closure. The upstream top-level MIT license is not evidence that all transitive copyrights/notices are covered.

Before public distribution, obtain an authoritative notice/SBOM closure for the actual Windows Setup/stub/Update binaries, or verify that closure against their exact build inputs and include the required texts. Do not substitute notices for all crates in Cargo.lock: that also includes unused targets, examples, tests and build tools. This remaining evidence gap is not owner permission to ignore third-party obligations. No updater binary, dependency, product feature or provider asset was changed to hide it.

## Packaging

The project copies every file in this flat source folder to `Licenses/ThirdParty` for output and publish. Existing candidate installers/portable/full packages do not contain this new bundle. An authorized candidate refresh is required to embed notices and revised release notes, regenerate metadata/checksums, and validate those exact bytes. Do not modify the prior validated packages in place or claim these source files are already installed.
