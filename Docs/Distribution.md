# Distribution decisions for v1.1.0

## Standing distribution policy — 2026-10-20

The owner permits unsigned Windows releases, including v1.2.0 and future versions. No certificate
or version-specific unsigned-release exception is required at this time. Unsigned status alone
must not cause a NO-GO assessment. Publication remains a separate decision requiring explicit
approval for the exact release and artifacts.

Clearly disclose unknown-publisher and possible Windows SmartScreen warnings. Windows may also
block an artifact under its security policy. Keep Windows protections enabled; never recommend
disabling or bypassing them. SHA-256 checksums establish artifact integrity, not publisher identity.

The existing bundled catalog and artwork have been accepted for distribution. If their contents
are unchanged, reuse that decision without repeating provider-rights investigations, artwork
audits or inventories. This acceptance is not independent verification of third-party rights.
Preserve existing legal limitations, attribution, provenance and the separate catalog-publication
gates; no new rights or native-notice completeness claim is made.

The v1.1.0 decisions and publication records below are retained as history. This standing policy
supersedes their signing requirements and version-specific exception wording for subsequent
releases, without changing any historical artifact, manifest, checksum or metadata.

## Current decision — 2026-10-20

The exact validated candidate is built from `e27851cb1aa2786663a74d718a8a51d6a713169f`, for `iamjethrooo/earnings-tracker`, package `EarningsTracker.App`, channel `win-x64-app-stable`. Preserve its eight assets and original checksums. No rebuild, catalog modification or additional investigation is requested. The owner explicitly approved the consolidated release decision on 2026-10-20. [v1.1.0 is published stable/latest](https://github.com/iamjethrooo/earnings-tracker/releases/tag/v1.1.0); anonymous pinned-client discovery and Full download/checksum verification passed. PRO-39 release acceptance is satisfied.

The owner explicitly approves retaining the existing `Data/catalog.json` and all **555 bundled game covers** in v1.1.0. Their inclusion is an **owner-accepted assumption, not independently verified redistribution permission**. This resolves the release-planning inclusion decision; do not reopen provider-rights investigations or remove/replace assets. This decision does not grant third-party rights or change `Data/catalog-review.json` approval flags or independent catalog publication gates.

Original application source and original assets remain **all rights reserved**, under the recorded owner decision. Public visibility grants no open-source license; third-party terms continue to govern their respective material. Preserve attribution, provenance, prices and affiliate links. No affiliation or guaranteed current availability is claimed.

## Notices and disclosed uncertainty

Reuse the completed bounded [native review](../Resources/ThirdPartyNotices/NATIVE-COVERAGE.md) and candidate receipt. The refreshed full/portable packages contain the 36 identified notice files plus Inter's OFL, verified byte-for-byte during prior validation. **Complete native Setup/stub/updater transitive/static-runtime dependency-notice coverage remains NOT VERIFIED.** Do not repeat the audit, claim completeness, or imply owner acceptance replaces third-party obligations. Disclose this uncertainty in the final public-release decision and release body; it is not converted into a passed check.

NuGet vulnerability-audit data was unavailable during preparation (NU1900); no new vulnerability audit is claimed. Existing lifecycle, privacy and package evidence is reused. Retain ignored private reports, real records, backups and recovery files locally. Source publication exposes reviewed history, author metadata and existing sanitized fixtures; the consolidated owner approval covered that exposure.

## Approved unsigned public-release exception — 2026-10-20

Existing broad-distribution policy requires trusted Authenticode signing and timestamp verification. The exact candidate is **NotSigned**, and its immutable manifest records `UnsignedLocalOnly=true`. The owner explicitly approved a **v1.1.0-only public unsigned exception** to that policy, together with the exact source/history, tag, eight assets, stable/latest publication and anonymous verification sequence. It does not change future-release signing policy or require signing acquisition/rebuilding in this task.

Windows may show SmartScreen, unknown-publisher or other security warnings, or block installation. No exact-candidate native warning behavior was observed. Keep Windows protections enabled; stop on a security-policy block or unexpected elevation. SHA-256 establishes byte agreement, not publisher identity or trust.

The consolidated owner approval accepts public distribution with the disclosed unverified exact-candidate offline startup, shortcut/security-warning observations and installed Settings restore, plus existing fault-injection, legacy-removal, delta and accessibility/DPI deferrals. Personal exceptions alone are not public acceptance. No automatic binary rollback or clean-Windows certification is claimed. Keep verified independent backups; install alongside the former app, verify preservation, then use the new app. Leave the legacy installation dormant; its repair/uninstaller can delete original data before preservation. See [release procedure](Release.md) and [storage recovery](Database.md#recovery).

## Immutable candidate and publication boundary

The published GitHub release body explains the current owner decision and approved release-specific signing exception. Attached `RELEASE_NOTES.md`, embedded feed/package notes and `release-manifest.json` retain historical preparation restrictions and `UnsignedLocalOnly`; these describe the build-time decision state. Do not alter their bytes to erase that history. The GitHub body explicitly explains the superseding authorization and retained uncertainty. No runtime or packaging change is needed for this external release decision.

The approved sequence completed: normal five-commit fast-forward to the exact source SHA, annotated v1.1.0 tag at that SHA, eight-asset draft/upload with matching GitHub SHA-256 digests, and stable/latest publication. Anonymous Velopack 1.2.161 GithubSource.GetReleaseFeed found exactly EarningsTracker.App 1.1.0 Full, with no deltas; DownloadReleaseEntry downloaded 87,199,232 bytes. SHA-256 `aadf3c044765769f0b61139c420159a78361edabeac0ed370fdb53d1d9a01573` matches the independent approved baseline and public feed; feed SHA-1 also matches. The new feed is anonymously accessible. v1.0.0 tag/release/assets and old-channel feed remain unchanged.

This verifies current anonymous discovery/download, not installed application of this release or future availability. No candidate rebuild, new source commit, installed apply/launch, production-data access, catalog publication or automatic corrective writes occurred. Full-package-only updates remain; no delta packages. Documentation changes remain uncommitted and outside the published source SHA. Private receipts are retained locally; the public body and PRO-39 record the later owner decision without altering immutable assets.
