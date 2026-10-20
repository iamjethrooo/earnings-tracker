# Build and release

## Standing unsigned-release policy — 2026-10-20

The owner permits unsigned Windows releases, including v1.2.0 and future versions. No code-signing
certificate or version-specific unsigned exception is required at this time. Unsigned status alone
does not make a candidate or release a NO-GO. Final publication still requires explicit approval
for the exact release and artifacts; local packaging approval is not publication approval.

Disclose that Windows may show an unknown publisher or Windows SmartScreen warning, or block an
artifact under its security policy. Keep Windows security protections enabled; never recommend
disabling or bypassing them. SHA-256 checksums verify artifact integrity, not publisher identity.
Record actual signature status and distinguish observed Windows behavior from unverified behavior.

This standing decision supersedes earlier signing requirements and release-specific unsigned
exceptions below. Those records remain historical evidence; do not rewrite old packages, manifests,
checksums, notes or publication records. Other integrity, privacy and exact-candidate verification
requirements remain. Previously accepted, unchanged catalog/artwork need no repeated rights
investigation or asset audit; retain the documented legal limitations without claiming independent
redistribution-rights verification. See [distribution policy](Distribution.md).

## Development requirements

Build on Windows with the .NET 10 SDK and Windows Desktop runtime. Catalog maintainer scripts require Python 3.11+ and use the standard library. The solution is `EarningsTracker.slnx`.

`global.json` pins SDK `10.0.402`, with roll-forward disabled and prerelease SDKs excluded, following the owner's PRO-39 decision on 2026-10-19. Repository-local `dotnet --version` and evaluation-only MSBuild checks resolve that SDK and accept both projects' `net10.0-windows`/WPF configuration. Evaluation of the release script's intended Production properties confirms coherent v1.1.0 version/repository/runtime metadata; this is not build, restore or package verification. Earlier focused checks used the same SDK through an isolated invocation directory. Retain that evidence and record the exact SDK, source SHA, configuration, tools and hashes during separately authorized final-artifact preparation. Do not silently bypass the pin.

```powershell
dotnet restore EarningsTracker.slnx
dotnet build EarningsTracker.slnx --no-restore
dotnet test EarningsTracker.slnx --no-build --no-restore
dotnet build EarningsTracker.slnx --configuration Release --no-restore
dotnet test EarningsTracker.slnx --configuration Release --no-build --no-restore
```

Do not launch the default application merely to verify a build; startup can migrate the current user's database. Use the isolated storage rules in [Database and storage](Database.md) for manual testing.

## v1.0.0 session import scope

Only session imports copied from Nerve's **Recordings page** are supported in v1.0.0, through **Sessions → Import Sessions**. Recordings is the default and only accessible session import format. Payout imports remain available unchanged.

The Game Details session importer (**Games → game details → Add Session**) is hidden and deferred to a future release. Its implementation, parsing logic and existing tests are retained. This scope adjustment does not change stored sessions or databases.

## Velopack preparation

`Tools/Release.ps1` is the supported local packaging entry point. It pins the Velopack SDK and CLI, publishes a self-contained Windows x64 build, validates package inputs and privacy exclusions, and creates a local release manifest and checksums.

Package identity is `EarningsTracker.App`; channel is `win-x64-app-stable`. The default install root is `%LOCALAPPDATA%\EarningsTracker.App`; persistent data/backups use the sibling `%LOCALAPPDATA%\EarningsTracker.UserData`. Executable/assembly names remain `EarningsTracker`. See [storage transition and recovery](Database.md). The identity and channel change prevents a manual repair from replacing the former data-bearing install root and keeps old clients off new-identity updates. Do not reuse old full packages as delta bases, publish on `win-x64-stable`, or imply that old clients can cross this identity through an in-app update. Users install alongside the former app and complete first launch before any old uninstall/repair; no manual data movement is required.

**PRO-21 lifecycle acceptance (owner decision, 2026-10-19): satisfied for v1.1.0 personal use and limited sharing.** Accept the completed real-account native Stages 1–4A: side-by-side installation and automatic verified preservation, offline full-package in-app update 1.0.21→1.0.22, same-version repair, and new-app uninstall/reinstall. Native identities, every-table/schema/audit/settings fingerprints, decimal accounting, migration IDs, SQLite integrity/FKs and backup/recovery preservation passed. Retain the existing synthetic copy/WAL/failure/recovery evidence. Do not repeat these stages in a synthetic environment or replay completed suites merely to close the lifecycle gate.

**Accepted limitations and deferred verification:** real-account testing is not disposable/clean-Windows testing. Interrupted installer/updater fault injection and legacy v1.0.0 uninstall/reinstall remain **NOT VERIFIED — OWNER-APPROVED DEFERRED** for these audiences, not passed tests or additional v1.1.0 lifecycle blockers. Leave the legacy installation dormant; removal is unnecessary and still requires separate authorization. Preserve the migration-before-uninstall warning below: already-distributed legacy tools can delete originals before preservation and cannot be retroactively fixed. Automatic binary rollback is not certified. These decisions do not authorize failure injection, original-data deletion, further installations or release operations; preserve backups/recoveries and obtain separate approval for any such action.

**v1.1.0 update scope: full packages only.** Delta application and delta-failure fallback are owner-approved deferred. Future v1.1.0 packaging/feeds must offer no delta assets; do not supply a previous-full delta base for that release. No tooling defaults or hosted assets have been changed by this documentation decision. Stage 2 verifies offline pending-package apply/restart, not GitHub discovery/download. Current-identity/channel full-package delivery and anonymous Production feed/asset validation remain release checks; retain the prior historical RemoteTest evidence without presenting it as current Production coverage.

Run native installer rehearsals from a verified native user context: a launcher hosted by a packaged tool can redirect AppData writes into its package cache even while the app reports an ordinary-looking data path. Independently verify actual executable and database file-handle paths from the native account. A successful preservation notice against redirected data does not validate the user's native profile. Stop and retain evidence if installation lands in an unexpected location; do not silently retry or treat a shared Start Menu shortcut as proof of the target installation.

## v1.1.0 public release decision (PRO-39, 2026-10-20)

**Published stable/latest with explicit owner approval; anonymous discovery/full download verified.** [GitHub v1.1.0 release](https://github.com/iamjethrooo/earnings-tracker/releases/tag/v1.1.0). The exact validated candidate was built from `e27851cb1aa2786663a74d718a8a51d6a713169f`. It supersedes the earlier `f9b34a3` candidate. Preserve every candidate byte; no rebuild or lifecycle, packaging, dependency or rights investigation is required. The approved release operations and delivery checks are complete; PRO-39 release acceptance is satisfied.

**Bundled assets — decided:** the owner explicitly accepts retaining `Data/catalog.json` and all 555 covers in v1.1.0. This is an **owner-accepted assumption, not independently verified redistribution permission**. Do not remove assets, modify the catalog, reopen the investigation or set independent provider/catalog approval flags. Original source/assets remain all rights reserved. See [distribution decisions](Distribution.md) for the retained notice uncertainty and approved v1.1.0-only unsigned exception.

### Candidate and acceptance evidence

- Source/package identity: Production `iamjethrooo/earnings-tracker`, `EarningsTracker.App`, `win-x64-app-stable`, product/package 1.1.0 and assembly/file 1.1.0.0. SDK 10.0.402, Velopack SDK/CLI 1.2.161, self-contained win-x64 runtime 10.0.12. Prior supported packaging and independent checks passed: manifest, hashes/sizes, archive integrity/privacy, compiled configuration, embedded full package, one Full feed entry and no deltas. Full/portable contain all 36 expected notices plus Inter. Prior packaging validation was reused; publication preflight rechecked all eight asset hashes without rebuilding.
- PRO-21 native Stages 1–4A and existing automated financial/import/migration/recovery/backup evidence are retained. Representative PRO-9 UI evidence and existing deferrals remain; no exhaustive matrix is reopened.
- **NOT VERIFIED:** complete native Setup/stub/updater transitive/static-runtime notice coverage; exact-candidate installed Settings restore, offline startup, shortcut and Windows warning observations; interrupted installer/updater faults, legacy removal and delta application. Existing personal deferrals do not certify public recipients. The owner explicitly accepted these disclosed public-release limitations in the consolidated final approval; they remain unverified. NuGet vulnerability-audit data was unavailable (NU1900). No clean-Windows certification or automatic binary rollback is claimed.
- **Unsigned public exception — owner approved, 2026-10-20:** Authenticode status is NotSigned; `UnsignedLocalOnly=true` remains immutable build provenance. The owner approved a v1.1.0-specific exception to the existing broad-distribution signing/timestamp policy for these exact bytes. Future signing policy is unchanged. Windows may show unknown-publisher/SmartScreen warnings or block installation; actual exact-candidate behavior is unobserved. Keep security controls enabled and stop on a block or unexpected elevation. Checksums do not establish publisher trust.
- **Current Production delivery — PASS:** after stable publication, anonymous pinned Velopack 1.2.161 GithubSource.GetReleaseFeed found one Full 1.1.0 entry, with no deltas. DownloadReleaseEntry downloaded 87,199,232 bytes to fresh scratch; SHA-256 matches the independent baseline and public feed, and feed SHA-1 also matches. Anonymous public stable/latest metadata and all eight GitHub asset digests/sizes agree. No app was launched or update applied.

### Installation and migration

Close the former tracker normally and retain independent verified backups/recovery copies. Install the new Production identity alongside it, normally under `%LOCALAPPDATA%\EarningsTracker.App`, never in a data folder. First launch copies and verifies eligible original data/backups into sibling `%LOCALAPPDATA%\EarningsTracker.UserData`, retaining originals; no manual folder movement is required. Verify records, payouts, audit history, totals, settings and backups before relying on the new app. Then use only the new app; old-app edits do not synchronize.

Leave the former installation dormant. Its existing repair/uninstaller can delete the original database/backups before successful preservation. If startup or preservation fails, retain all files and follow [storage recovery](Database.md#recovery); failure is not permission to uninstall. Legacy removal is deferred. Old `EarningsTracker` / `win-x64-stable` clients cannot cross identities through in-app updates. Portable is a package format, not a separate data migration/backup strategy; do not copy portable/full contents over an installation or UserData.

### Exact approved publication — completed 2026-10-20

The eight existing files are in `artifacts/pro39/v1.1.0-rc-20261020-notices-01/upload`:

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| EarningsTracker.App-win-x64-app-stable-Setup.exe | 94717440 | f10153a710896b7cf9d877398c3ff17d2eee26f0d05fb013a1589af22ff8bc38 |
| EarningsTracker.App-1.1.0-win-x64-app-stable-full.nupkg | 87199232 | aadf3c044765769f0b61139c420159a78361edabeac0ed370fdb53d1d9a01573 |
| EarningsTracker.App-win-x64-app-stable-Portable.zip | 87193660 | 9267d710eb5cf2ac4acd957c4791dde047c33af0e560a2969f69e50f882706c4 |
| releases.win-x64-app-stable.json | 7831 | 3a1b3f4d7d84812670b5816c81da52cced93cdfd0fc685ea948874537c8b9a44 |
| assets.win-x64-app-stable.json | 289 | 9d4ef1f2d5001aa373988cd2147384b33788ecd1bcc9e65cc9397ed4e914aba6 |
| RELEASE_NOTES.md | 3631 | c943b79090211a1023c3db41f424adf79355f78b2525a4541faa5ab7b6fd34f9 |
| release-manifest.json | 1808 | b0a9d0f41b834a5479c828a6a2de5305d4984761b74a4dff4069ae031b7d5ca4 |
| SHA256SUMS | 729 | d1de83a9e9ebf06b0122410f4cf97b8a8225158501b89a3fd8decafe5598519e |

SHA256SUMS covers its seven companions; its own hash is recorded separately above. The reviewed private publication plan/body/manifest are retained in `.local-docs/pro39-public-110-preparation-20261020/`. The published GitHub body explains that attached/embedded notes, source documentation at the tag and `UnsignedLocalOnly` describe the earlier preparation policy; explicit final release approval supersedes their pending rights/audience/signing decision wording, without claiming independently verified permissions or native notice completeness. Preserve original assets, source SHA and hashes. The owner explicitly approved that exception and the body; publication did not change the candidate bytes.

The owner approved and the operator completed this sequence (retained as the publication procedure):

1. Recheck remote refs/releases and local artifact identity; stop on divergence, collisions or changed bytes. Normally fast-forward `origin/main` with the five reviewed commits `400c9c7`, `6844dc0`, `bc9da71`, `f9b34a3`, `e27851c`, targeting exactly `e27851cb1aa2786663a74d718a8a51d6a713169f`. No force push, implicit later HEAD or new commit. Public source/history exposure is part of approval.
2. Create and push annotated `v1.1.0` at that exact SHA. Create draft `Earnings Tracker v1.1.0`, upload exactly the eight assets without overwrite, inspect the draft, then publish stable/latest (not prerelease). Stable publication immediately enables discovery by compatible new-identity clients.
3. Anonymously use pinned Velopack 1.2.161 GitHub discovery for the new identity/channel, download the single Full package to fresh scratch and verify 87,199,232 bytes and SHA-256 `aadf3c044765769f0b61139c420159a78361edabeac0ed370fdb53d1d9a01573` against the independent baseline and public metadata. Confirm new feed access and no Delta offerings. Do not install/apply/launch.
4. Recheck v1.0.0 tag/release/assets and old-channel feed unchanged (recorded old-feed SHA-256 `2fe7799e5563ccd94f29a4c44d4cc59c7b545ff3ecf1c9b06f01ff51ebf365fe`). Record evidence and update PRO-39; do not close while required delivery acceptance remains unverified. On failure stop/report; no automatic corrective publication, replacement, retagging or rollback writes.

Completion evidence: remote main and peeled v1.1.0 both resolve to `e27851cb1aa2786663a74d718a8a51d6a713169f`; annotated tag object is `b0c7b1612f7f728e5bc282e99ccfb413acebeef1`. Published release ID is `417601961`. Full download hash/size and no-delta discovery passed. v1.0.0 annotated tag `7ccb1d0efbb8edee3b3ed600b45880c81a38f889` still peels to `91250fed1f58e63ebc62027127a0b0052c916a3f`; its release/assets and old-feed hash are unchanged. Private API receipts and standalone discovery/download receipt are retained under `.local-docs/pro39-publication-20261020/` and `artifacts/pro39/public-delivery-20261020/`.

No catalog publication/gate changes, deltas, candidate rebuild, new source commit, production-data access or installed apply/launch occurred. Documentation edits remain uncommitted and outside the exact published SHA. The approved public body and Linear decision carry the later authorization without changing the candidate. Discovery/download is verified for this check; installed application and future delivery are not inferred.

## Historical v1.0.0 packaging example

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Release.ps1 -Version 1.0.0 -UnsignedLocal
```

The script accepts `Local`, `RemoteTest`, or `Production` update configuration. RemoteTest and Production preparation require a reviewed Phase 3 commit; RemoteTest also requires a separate test repository. Output is written to a new directory under `artifacts/`. Preparation does not upload, sign, commit, or tag. `-UnsignedLocal` explicitly acknowledges unsigned preparation; it does not approve distribution. Local artifacts are rehearsal inputs. Limited distribution requires Production configuration, release acceptance, and separate owner approval.

Packaging excludes application debug symbols and rejects private-key containers (`pfx`, `p12`, `pem`, `key`, `pvk`, `snk`, `jks`, `keystore`) along with personal databases and backups. Production installers request a Start Menu root shortcut; Local and RemoteTest installers omit shortcuts to avoid shell integration during disposable rehearsals. Verify the shortcut with the exact final installer. Inter's copyright and OFL license ship as `Licenses/Inter-OFL.txt`; its source is `Resources/Fonts/OFL.txt`, from the upstream commit identified by the font's embedded version (`66647c0bb`).

See [Phase 5 verification](../.local-docs/UI-Phase-5-Velopack.md) for current evidence and unresolved release gates. Local artifacts labeled 1.0.0 are preparation builds, not a published or approved stable release.

## Historical v1.0.0 acceptance for unsigned limited distribution

This historical checklist is not an additional v1.1.0 test queue. For v1.1.0 personal use/limited sharing, the revised criteria above supersede its legacy-removal, lifecycle replay, installer/updater fault-injection and delta requirements. Deferred checks remain unverified. Do not restart the historical exhaustive UI matrix or other completed checks.

The owner approved planning an unsigned v1.0.0 for personal use and trusted friends on 2026-10-14. This changes the signing criterion, not the safety/reliability requirements or publication authorization. Use `PASS`, `FAIL`, `NOT VERIFIED`, or `APPROVED EXCEPTION` for every acceptance item. Missing evidence is `NOT VERIFIED`; a known contradiction is `FAIL`. Exceptions must state their scope, reason and explicit owner approval. An unapproved proposed exception cannot satisfy a gate.

Mandatory requirements for this audience:

- Review the complete source, including renamed/untracked tests, storage guards and UI changes. Obtain commit approval, then prepare the final candidate from a clean reviewed commit. Align product/package/assembly/file versions, package ID `EarningsTracker.App`, intended tag `v1.0.0`, runtime `win-x64`, and channel `win-x64-app-stable`. Tag creation remains a separate authorized step.
- Resolve the production connection and record-preservation discrepancy through read-only inspection of the actual native connection. Record assembly/configuration, resolved data path, `DataSource`, `PRAGMA database_list`, migration IDs, integrity/FKs and a comparison against the reviewed recovery evidence. Never reset, move, replace or repair personal data as a release check. A tool shell's textual path alone does not prove live connection identity.
- Reuse applicable Debug/Release suite and migration/import/recovery evidence. Rerun only checks invalidated by source changes, configuration or final packaging. Validate exact final setup/full/delta/portable feeds, bytes and checksums, privacy exclusions and deployed dependencies.
- Verify isolated install/first launch/offline use, update/download/restart, invalid/corrupt source recovery, uninstall/reinstall preservation and Start Menu shortcut. Complete the clean-Windows checklist below. Live cancellation/interruption, post-exit apply failure, lock/disk-full recovery and uninstaller/credential retention remain requirements unless specifically accepted as bounded exceptions.
- Complete synthetic import/preview/commit/reimport/edit checks, keyboard/focus/modal containment, minimum/restored/maximized resizing, supported DPI/resolutions, scrolling, Narrator and high contrast. Offscreen WPF tests and accessibility trees provide partial evidence only.
- Confirm final compiled Production source is `iamjethrooo/earnings-tracker`, with anonymous asset access, stable channel, no downgrade/prerelease offer, and no test/local override. Rehearse the new identity separately before activating the update feed. Catalog uses the independent `catalog-latest` manifest; missing catalog hosting must retain saved/offline data.
- Review bundled catalog, prices, artwork, font and dependency permission/attribution evidence for the intended distribution. Keep catalog publication/review flags intact. Trusted friends still constitute distribution. Current [IsThereAnyDeal terms](https://docs.isthereanydeal.com/) require approval for private use beyond personal use; [IGDB documentation](https://api-docs.igdb.com/#about) refers to Twitch terms and attribution. Provider access or a matching artwork hash does not establish redistribution rights. Do not set approval flags without the corresponding evidence.
- Inspect source/history privacy before any source push. Keep recovery evidence, databases, exports, keys and `.local-docs/` private. A public release repository exposes assets and any pushed Git history; limited intended audience does not make public GitHub hosting private.
- Tell recipients the build is unsigned and may show unknown publisher/SmartScreen warnings. Record actual Authenticode status; SHA-256 verifies byte agreement, not publisher trust. Do not disable security controls or instruct recipients to bypass security barriers. If policy blocks an artifact, record the block and stop that check.
- Obtain separate approval before commits, source pushes, tags, test drafts/uploads/publication or production distribution. A completed preparation folder is not publication approval.

Trusted Authenticode signing and timestamp verification are mandatory for broad distribution. For the approved unsigned limited-distribution plan they are an `APPROVED EXCEPTION`, not a failed safety gate and not a claim of trust. Signing acquisition remains separately authorized. Other outstanding gates are not waived by the unsigned plan.

## Historical v1.0.0 controlled clean-Windows verification

Use a disposable Windows x64 VM with a fresh standard user, no SDK/runtime/developer tools, no personal profile or credentials, and a recoverable checkpoint. Record OS/build, security policy, display/DPI, exact installer hashes and source commit. Do not enable virtualization or change host security settings just to obtain a test environment. A host with development tools or an altered `DOTNET_ROOT` is not a clean-machine pass.

1. Transfer only the reviewed final artifacts and synthetic fixtures. Verify checksums independently and record unsigned signature status. Keep Defender, SmartScreen and OS policy enabled; record warning/block behavior without bypassing it.
2. Install offline, verify the requested Start Menu shortcut and target, launch via the shortcut and directly, confirm self-contained startup and expected profile selection. Record installer/app exit results. Use VM-only data.
3. Confirm fresh financial history is empty; run session and payout imports, duplicate reimport, audited edits and reconciliation with positive/negative amounts, unknown posting date, Paid/Processing statuses, bookmarks/ownership and settings. Reopen offline and compare persisted records/audits/integrity/FKs.
4. Check keyboard-only navigation, modal focus/close protection, 100/125/150/200% DPI, minimum/restored/maximized sizes, 1366x768 and 1920x1080, scrolling, Narrator names/feedback and high contrast. Record clipping, unreachable actions or unreadable states.
5. Rehearse new-identity A-to-B discovery/download/manual restart on a persistent synthetic profile. Capture data before/after update and uninstall/reinstall. Verify old package identity is not silently treated as a supported upgrade path.
6. Test offline/malformed/corrupt feed recovery, cancellation/interrupted download and controlled post-exit/lock/disk-full failures in the disposable checkpoint only. Restore the VM checkpoint, never a personal database, between destructive fault scenarios.
7. Verify uninstaller/credential retention using a disposable non-secret credential only. Archive hashes, screenshots, receipts and limits privately. Any unavailable check stays `NOT VERIFIED` or needs an explicit scoped exception.

## Historical new-identity GitHub rehearsal preparation

For the current full-only v1.1.0 scope, omit the previous-full input/delta generation and reconstruction described historically below. Any authorized delivery rehearsal should test only the remaining current-ID/channel discovery/full download/anonymous-access gap, reusing prior apply/preservation evidence rather than replaying the lifecycle.

Use `iamjethrooo/nerve-earnings-tracker-updates-test`, never production. Reserve fresh disposable versions after reading its releases; the completion plan uses `0.0.120` and `0.0.121`, subject to a fresh collision check. Both use package ID `EarningsTracker.App` and stable channel `win-x64-app-stable`. Existing former-identity releases are historical evidence only.

After the reviewed source prerequisite is available, run `Tools/Release.ps1` twice with `-UnsignedLocal -UpdateConfiguration RemoteTest -TestUpdateRepository iamjethrooo/nerve-earnings-tracker-updates-test -ReviewedPhase3Commit <reviewed SHA>`, distinct new artifacts directories and matching version/tag inputs. Supply A's validated full package as B's `-PreviousFullPackage`. Review manifests, exact upload sets, compiled metadata, checksums and delta reconstruction before requesting publication approval. Do not silently replace a missing historical reviewed SHA with HEAD.

Prepare notes titled `UNSIGNED TEST — EarningsTracker 0.0.120` / `0.0.121`; test tags target the verified README-only repository commit, not application source. Use separate installation/data directories under artifacts, a child-process-only `NERVE_TRACKER_DATA_DIRECTORY`, and synthetic data. Do not inherit `NERVE_TRACKER_LOCAL_UPDATE_SOURCE`. Retain before/after row fingerprints and integrity/FK results. Pause before creating drafts, uploading or publishing: anonymous discovery requires approved stable test publication. Once authorized, publish A, install and establish baseline, then publish B and verify discovery/download/manual restart, preservation and recovery. Recheck the exact public assets against approved hashes.

The detailed Phase 1–4 plans and verification records are retained under local-only `.local-docs/` for this checkout. The maintained release procedure is this document and `Tools/Release.ps1`.
