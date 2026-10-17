# Build and release

## Development requirements

Build on Windows with the .NET 10 SDK and Windows Desktop runtime. Catalog maintainer scripts require Python 3.11+ and use the standard library. The solution is `EarningsTracker.slnx`.

`global.json` pins SDK `10.0.401` with roll-forward disabled. The installed `10.0.402` was used for earlier focused verification through an isolated invocation directory; that evidence is useful but is not an exact-pin release build. For a reproducible final build, use a disposable builder with `10.0.401`, record `dotnet --version`, the source SHA, configuration, tool versions and package hashes, and retain the pin. If `10.0.401` cannot be obtained, review and approve a deliberate pin change to `10.0.402` with its own focused build/package verification before treating that output as the release candidate. Do not silently bypass or change the pin.

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

PRO-21 release gate: rehearse the old `EarningsTracker` installed layout with synthetic data at its install root, transition automatically, then test new-identity in-app A-to-B, same-version repair, and old/new uninstall/reinstall in a disposable Windows profile/VM. Record installer hashes, actual install/data paths, migration IDs, every-table fingerprints, integrity/FK results, and existing/recovery-backup hashes at every stage. Also rehearse a failed copy and recovery to a new scratch database. An override that starts with data already outside the old root does not validate this transition. Old packages remain unsafe before successful transition; do not remove them as part of unattended upgrade. Native lifecycle acceptance remains required before release.

## v1.1.0 candidate and release decision (PRO-39)

The proposed source candidate is `6844dc0`, two commits after the v1.0.0 tag `91250fe`: `400c9c7` separates install/data storage and adds verified automatic copy, Settings backup/restore, startup/recovery and import-concurrency fixes; `6844dc0` persists Games sort, bookmarks-only and archived visibility defaults. This is a **planning candidate**, not a packaged or accepted release. Record the exact final SHA after any approved documentation commit. Recordings-page session import remains the supported flow; the Game Details Add Session flow remains hidden.

Production package ID `EarningsTracker.App` installs by default under `%LOCALAPPDATA%\EarningsTracker.App` (`current` holds the app); channel `win-x64-app-stable` is distinct from the former `EarningsTracker` / `win-x64-stable`. Executable/assembly names remain `EarningsTracker`. Production database, backups and artwork use `%LOCALAPPDATA%\EarningsTracker.UserData`. Local and RemoteTest packages require isolated scratch storage and do not perform the personal-data transition. The former channel cannot update across identities; do not use its full package as a new-identity delta base.

**Required v1.0.0 transition order:** (1) close the former app; (2) install the reviewed new Production identity *alongside* it without touching the old install; (3) launch the new app successfully and verify that records, settings, audit history, backups, SQLite integrity/FKs and migration IDs survived the automatic first-launch copy; (4) use only the new app, then consider old removal only with separate authorization. Keep the old files and recovery copies until preservation is verified. The already-distributed former uninstaller or repair can delete the only old database/backups **before** successful preservation; no new package can retroactively fix it. If first launch fails, leave both installations and all data/preparation folders intact and follow [storage recovery](Database.md#recovery). Never interpret a failed launch as permission to uninstall the former app.

### Draft v1.1.0 release notes (unpublished)

- v1.1.0 separates the new app installation from saved data. Close v1.0.0, install the new identity alongside it, and successfully launch the new app before considering removal of the old one. Use only the new app afterward.
- Settings can create a verified standalone database backup and schedule a confirmed restore that completes on next launch. Backups contain unencrypted personal records; cached artwork and the Credential Manager API key are excluded.
- Settings now saves the Games starting sort, bookmarks-only filter and archived-game visibility. Startup/recovery and import-concurrency fixes are included.
- This release is not yet accepted or distributed. If the final build is unsigned, its notes must identify the unsigned status and actual Windows warning/block behavior. Final notes and compiled metadata must match the reviewed package.

Use `PASS`, `FAIL`, `NOT VERIFIED`, or `APPROVED EXCEPTION` with evidence and scope. An exception requires explicit owner approval; missing evidence stays `NOT VERIFIED`. Existing focused synthetic/real-clone checks and PRO-9 native observations may be reused without summing overlapping test counts. They do not establish native installer behavior. Complete these release checks on an exact final candidate:

1. **Source/build/package:** review `91250fe..HEAD`, record SHA and exact SDK decision; align product, package, assembly and file versions at `1.1.0`. Reuse tests unless source/configuration invalidates them. Prepare an isolated `Tools/Release.ps1 -Version 1.1.0 -UnsignedLocal` rehearsal only after separate authorization. For sharing, use reviewed `Production` configuration and a genuinely reviewed Phase 3 ancestor. Check win-x64 self-contained payload, package/feed IDs, setup/full/delta (where applicable) and hashes, privacy exclusions, offline first launch, shortcut, actual unsigned warning/block behavior and anonymous Production feed access. No old-identity delta or cross-identity update claim.
2. **PRO-21 native lifecycle — blocking:** on an authorized disposable Windows x64 guest with synthetic data at the former default data-bearing install root, verify side-by-side first launch, new-identity A-to-B update, same-version repair, old/new uninstall/reinstall, failed-copy/retry and recovery. At each stage compare every table/schema, `__EFMigrationsHistory`, integrity/FKs and original/backup/recovery hashes. Local-feed A-to-B proves local mechanics only; Production GitHub delivery needs separate evidence and authorization. Obtain an explicit owner decision on the former uninstaller's pre-preservation boundary. Do not broaden PRO-21 beyond its established native scope.
3. **PRO-15 installed manual smoke — blocking:** in the isolated installed candidate, create a Settings backup and verify its standalone contents; change synthetic data, select restore, inspect confirmation, confirm app close, reopen to apply, and compare records, settings, audits, migration history, integrity/FKs and the `Backups\BeforeUserRestore` recovery snapshot. Exercise failure/retry. Never restore a personal profile.
4. **UI scope:** retain PRO-9's approved representative native keyboard, focus, dialogs, scrolling and window reflow evidence. Narrator listening, the comprehensive 100%/150%/200% DPI matrix and mixed-monitor DPI remain **NOT VERIFIED and indefinitely deferred** by the owner; do not report them as passes or restart them without a new request or concrete defect. Other exhaustive native paths remain unverified. Record any specific final-candidate UI gap that affects the intended audience.
5. **Distribution and privacy:** verify provider and asset redistribution permission/attribution for catalog data, artwork, prices, font and dependencies for the intended audience; keep catalog publication disabled until its independent approval gates pass. Review source/history and package privacy before sharing. An API key or matching asset hash is not redistribution permission. A public repository exposes pushed history and assets.

Current evidence at `6844dc0`: implementation and focused synthetic/real-clone checks for storage transition and backup/restore **PASS within their tested scope**; PRO-9's owner-approved representative native scope is **PASS**. PRO-21 native lifecycle, PRO-15 installed restore smoke, exact 1.1.0 package and pinned-SDK build, Production feed/permission review, and all release decisions are **NOT VERIFIED**. The former uninstaller's pre-preservation risk remains an unresolved acceptance boundary. These checks must not be relabeled as passed from source review or copied-data tests.

**Personal installation — GO** only after the exact candidate, data-preservation native lifecycle, installed backup/restore smoke, applicable build/package and offline checks pass, and the owner explicitly approves installation and the old-uninstaller boundary. Otherwise **NO-GO**. **Limited sharing — GO** only after those gates plus Production feed/installer validation, recipient unsigned-warning behavior, provider/asset permission and attribution, privacy review, and separate owner approval for the exact recipients/artifacts; otherwise **NO-GO**. **Public distribution — GO** only after the limited-sharing gates plus trusted signing/timestamp verification, broader clean-Windows/fault/UI acceptance and public-hosting permission/privacy review, with separate publication approval; otherwise **NO-GO**. No personal installation, distribution, tag, push or upload is authorized by this checklist.

For the historical v1.0.0 packaging example and acceptance plan below, retain its version/tag and audience as originally recorded; use this v1.1.0 section for the current decision.

## Historical v1.0.0 packaging example

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Release.ps1 -Version 1.0.0 -UnsignedLocal
```

The script accepts `Local`, `RemoteTest`, or `Production` update configuration. RemoteTest and Production preparation require a reviewed Phase 3 commit; RemoteTest also requires a separate test repository. Output is written to a new directory under `artifacts/`. Preparation does not upload, sign, commit, or tag. `-UnsignedLocal` explicitly acknowledges unsigned preparation; it does not approve distribution. Local artifacts are rehearsal inputs. Limited distribution requires Production configuration, release acceptance, and separate owner approval.

Packaging excludes application debug symbols and rejects private-key containers (`pfx`, `p12`, `pem`, `key`, `pvk`, `snk`, `jks`, `keystore`) along with personal databases and backups. Production installers request a Start Menu root shortcut; Local and RemoteTest installers omit shortcuts to avoid shell integration during disposable rehearsals. Verify the shortcut with the exact final installer. Inter's copyright and OFL license ship as `Licenses/Inter-OFL.txt`; its source is `Resources/Fonts/OFL.txt`, from the upstream commit identified by the font's embedded version (`66647c0bb`).

See [Phase 5 verification](../.local-docs/UI-Phase-5-Velopack.md) for current evidence and unresolved release gates. Local artifacts labeled 1.0.0 are preparation builds, not a published or approved stable release.

## Historical v1.0.0 acceptance for unsigned limited distribution

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

Use `iamjethrooo/nerve-earnings-tracker-updates-test`, never production. Reserve fresh disposable versions after reading its releases; the completion plan uses `0.0.120` and `0.0.121`, subject to a fresh collision check. Both use package ID `EarningsTracker.App` and stable channel `win-x64-app-stable`. Existing former-identity releases are historical evidence only.

After the reviewed source prerequisite is available, run `Tools/Release.ps1` twice with `-UnsignedLocal -UpdateConfiguration RemoteTest -TestUpdateRepository iamjethrooo/nerve-earnings-tracker-updates-test -ReviewedPhase3Commit <reviewed SHA>`, distinct new artifacts directories and matching version/tag inputs. Supply A's validated full package as B's `-PreviousFullPackage`. Review manifests, exact upload sets, compiled metadata, checksums and delta reconstruction before requesting publication approval. Do not silently replace a missing historical reviewed SHA with HEAD.

Prepare notes titled `UNSIGNED TEST — EarningsTracker 0.0.120` / `0.0.121`; test tags target the verified README-only repository commit, not application source. Use separate installation/data directories under artifacts, a child-process-only `NERVE_TRACKER_DATA_DIRECTORY`, and synthetic data. Do not inherit `NERVE_TRACKER_LOCAL_UPDATE_SOURCE`. Retain before/after row fingerprints and integrity/FK results. Pause before creating drafts, uploading or publishing: anonymous discovery requires approved stable test publication. Once authorized, publish A, install and establish baseline, then publish B and verify discovery/download/manual restart, preservation and recovery. Recheck the exact public assets against approved hashes.

The detailed Phase 1–4 plans and verification records are retained under local-only `.local-docs/` for this checkout. The maintained release procedure is this document and `Tools/Release.ps1`.
