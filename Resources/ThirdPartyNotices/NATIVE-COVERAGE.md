# Native Windows updater notice coverage

## Exact reference candidate

Inspected read-only on 2026-10-20: Earnings Tracker 1.1.0 candidate from source `f9b34a37fc333281d5585685da8ba4d8f6933947`, packaged with vpk/Velopack 1.2.161. These hashes identify the preserved reference candidate, not an already refreshed distribution containing these notices.

| Redistributed component | Candidate location | SHA-256 | Vendor binary whose `.text` matches |
| --- | --- | --- | --- |
| Setup | `EarningsTracker.App-win-x64-app-stable-Setup.exe` | `e6258898cf24b9483869bd88ee10aceae7b559159b262b0f07ad3b6c75673642` | `setup_x64.exe` |
| Updater | full package `lib/app/Squirrel.exe`; portable `Update.exe` (identical bytes) | `fe9df9350f5a6ba6e75e4dd54f7e33d2c8188262f6fc36c8a4ec0c3dfed92f52` | `update_x64.exe` |
| Execution stub | full package `lib/app/Earnings Tracker_ExecutionStub.exe`; portable `Earnings Tracker.exe` (identical bytes) | `b3087fe9f97c2e3cf5e134dc11899c0f7c29cb448bb2955b60ae88812bdce74e` | `stub_x64.exe` |

The executable code-section hashes/sizes match the corresponding vendor binaries in the actual vpk 1.2.161 package. Whole-file bytes differ because the generated candidate includes application resources and Setup carries its bundle. Section matching supports binary provenance; it is not a reproducible-build attestation or a complete dependency inventory. The ordinary `current/EarningsTracker.exe` apphost is covered separately by Microsoft runtime notices, not classified as a Velopack stub.

## Confirmed coverage

The package nuspec identifies upstream commit `92d6a1c91716729d449034df5c50307dcce39493`. Its [MIT license](Velopack-LICENSE.txt) is retained. [NATIVE-SOURCES.md](NATIVE-SOURCES.md) records 21 additional unmodified texts, their hashes and archive provenance for these confirmed native libraries:

| Component/version | Evidence and license texts |
| --- | --- |
| gif 0.14.2; image 0.25.10; png 0.18.1 | Setup version-string evidence agrees with the pinned lockfile. Offered MIT/Apache-2.0 texts retained. |
| rustls 0.23.45 | Setup/updater version-string evidence and pinned lockfile. Offered Apache-2.0/ISC/MIT texts retained. |
| rustls-pki-types 1.15.1; rustls-webpki 0.103.15 | Setup/updater evidence and pinned lockfile. MIT/Apache-2.0 alternatives for pki-types; ISC for webpki, with original copyrights. |
| ureq 3.4.2; ureq-proto 0.6.4 | Setup/updater evidence and pinned lockfile. Offered MIT/Apache-2.0 texts retained. |
| zip 8.6.0 | Setup/updater evidence and pinned lockfile. MIT text retained. |
| zstd-safe 8.0.0 | Updater evidence and pinned lockfile. BSD-3-Clause text retained. |
| zstd-sys 2.1.0+zstd.1.5.7 | Locked native dependency of confirmed zstd-safe; package BSD-3-Clause and vendored Zstandard BSD texts retained. This is the native library, not the packaging-only `zstd.exe`. |

Archives were fetched from the authoritative crates.io distribution host and every SHA-256 matched the pinned Cargo.lock before any text was copied. No crate code was executed. Retaining offered license alternatives does not apply those licenses to Earnings Tracker's original source. No upstream copyright or license wording was invented.

## Precise remaining gap — NOT VERIFIED

This bounded review does **not** establish complete notice closure for all three binaries. The missing evidence is a version-specific per-binary dependency/feature/runtime inventory, tied to these vendor binaries, with the required copyright/license/NOTICE texts for the remainder. Examples of unresolved closure include TLS/crypto transitives such as `ring`/`untrusted`, image/deflate transitives, and statically linked Rust standard library/compiler runtime and MSVC CRT. Examples identify the kind of gap, not an assertion that every optional transitive in the workspace ships in every binary.

The reviewed upstream Windows build manifest uses `x86_64-win7-windows-msvc`, nightly `build-std`, `--locked`, `--features windows`, workspace compilation and `RUSTFLAGS=-C target-feature=+crt-static`. That explains why the managed `.deps.json` and OS DLL imports cannot alone settle native notice obligations. `deny.toml` is a license-allow policy, not a delivered notice bundle or per-binary SBOM. The reviewed supply-chain workflow audits advisories; it does not generate the missing notice closure. The published 1.2.161 asset list contains library/tool packages but no separately named Windows binary SBOM/license report. The packaged vendor notice file describes packaging-only/Linux tools and is not substituted for this evidence.

Stop this review here. Obtain upstream-provided Windows notice/SBOM evidence for 1.2.161, or perform a separately scoped exact native-build dependency/notice reconstruction if required. Do not assume top-level MIT, a successful cargo-deny policy, a lockfile entry or owner risk acceptance satisfies missing third-party obligations. Do not sweep all workspace crates or unrelated build tools into the shipped list.

## Candidate decision

A **private** notice/documentation candidate refresh is technically possible after explicit baseline/build approval, but it would still carry this unresolved distribution limitation. It must not be marked public-ready. Public release remains NO-GO until native notice closure and the separate provider/source/audience decisions are resolved. Any accepted new notice files must be included in a future full-only package refresh and its new hash/manifest/feed validation. Existing candidate packages and hashes remain unchanged; no build or installation occurred in this review.

## Authoritative inputs

- [Pinned upstream source and lockfile](https://github.com/velopack/velopack/tree/92d6a1c91716729d449034df5c50307dcce39493)
- [Windows build inputs](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/.github/workflows/build-rust.yml)
- [Native binary dependencies](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/src/bins/Cargo.toml)
- [License policy](https://github.com/velopack/velopack/blob/92d6a1c91716729d449034df5c50307dcce39493/deny.toml)
- [Published 1.2.161 assets](https://github.com/velopack/velopack/releases/tag/1.2.161)
- Exact crate archive/checksum/member references in [NATIVE-SOURCES.md](NATIVE-SOURCES.md).
