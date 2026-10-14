# Build and release

## Development requirements

Build on Windows with the .NET 10 SDK and Windows Desktop runtime. Catalog maintainer scripts require Python 3.11+ and use the standard library. The solution is `EarningsTracker.slnx`.

```powershell
dotnet restore EarningsTracker.slnx
dotnet build EarningsTracker.slnx --no-restore
dotnet test EarningsTracker.slnx --no-build --no-restore
dotnet build EarningsTracker.slnx --configuration Release --no-restore
dotnet test EarningsTracker.slnx --configuration Release --no-build --no-restore
```

Do not launch the default application merely to verify a build; startup can migrate the current user's database. Use the isolated storage rules in [Database and storage](Database.md) for manual testing.

## Velopack preparation

`Tools/Release.ps1` is the supported local packaging entry point. It pins the Velopack SDK and CLI, publishes a self-contained Windows x64 build, validates package inputs and privacy exclusions, and creates a local release manifest and checksums.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Release.ps1 -Version 1.0.0 -UnsignedLocal
```

The script accepts `Local`, `RemoteTest`, or `Production` update configuration. RemoteTest and Production preparation require a reviewed Phase 3 commit; RemoteTest also requires a separate test repository. Output is written to a new directory under `artifacts/`. Preparation does not upload, sign, commit, or tag. `-UnsignedLocal` explicitly acknowledges unsigned preparation; it does not approve distribution. Local artifacts are rehearsal inputs. Limited distribution requires Production configuration, release acceptance, and separate owner approval.

Packaging excludes application debug symbols and rejects private-key containers (`pfx`, `p12`, `pem`, `key`, `pvk`, `snk`, `jks`, `keystore`) along with personal databases and backups. Production installers request a Start Menu root shortcut; Local and RemoteTest installers omit shortcuts to avoid shell integration during disposable rehearsals. Verify the shortcut with the exact final installer. Inter's copyright and OFL license ship as `Licenses/Inter-OFL.txt`; its source is `Resources/Fonts/OFL.txt`, from the upstream commit identified by the font's embedded version (`66647c0bb`).

See [Phase 5 verification](../.local-docs/UI-Phase-5-Velopack.md) for current evidence and unresolved release gates. Local artifacts labeled 1.0.0 are preparation builds, not a published or approved stable release.

## Acceptance for unsigned limited distribution

The owner approved planning an unsigned v1.0.0 for personal use and trusted friends on 2026-10-14. This changes the signing criterion, not the safety/reliability requirements or publication authorization. Use `PASS`, `FAIL`, `NOT VERIFIED`, or `APPROVED EXCEPTION` for every acceptance item. Missing evidence is `NOT VERIFIED`; a known contradiction is `FAIL`. Exceptions must state their scope, reason and explicit owner approval. An unapproved proposed exception cannot satisfy a gate.

Mandatory requirements for this audience:

- Review the complete source, including renamed/untracked tests, storage guards and UI changes. Obtain commit approval, then prepare the final candidate from a clean reviewed commit. Align product/package/assembly/file versions, package ID `EarningsTracker`, intended tag `v1.0.0`, runtime `win-x64`, and channel `win-x64-stable`. Tag creation remains a separate authorized step.
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

## Controlled clean-Windows verification

Use a disposable Windows x64 VM with a fresh standard user, no SDK/runtime/developer tools, no personal profile or credentials, and a recoverable checkpoint. Record OS/build, security policy, display/DPI, exact installer hashes and source commit. Do not enable virtualization or change host security settings just to obtain a test environment. A host with development tools or an altered `DOTNET_ROOT` is not a clean-machine pass.

1. Transfer only the reviewed final artifacts and synthetic fixtures. Verify checksums independently and record unsigned signature status. Keep Defender, SmartScreen and OS policy enabled; record warning/block behavior without bypassing it.
2. Install offline, verify the requested Start Menu shortcut and target, launch via the shortcut and directly, confirm self-contained startup and expected profile selection. Record installer/app exit results. Use VM-only data.
3. Confirm fresh financial history is empty; run session and payout imports, duplicate reimport, audited edits and reconciliation with positive/negative amounts, unknown posting date, Paid/Processing statuses, bookmarks/ownership and settings. Reopen offline and compare persisted records/audits/integrity/FKs.
4. Check keyboard-only navigation, modal focus/close protection, 100/125/150/200% DPI, minimum/restored/maximized sizes, 1366x768 and 1920x1080, scrolling, Narrator names/feedback and high contrast. Record clipping, unreachable actions or unreadable states.
5. Rehearse new-identity A-to-B discovery/download/manual restart on a persistent synthetic profile. Capture data before/after update and uninstall/reinstall. Verify old package identity is not silently treated as a supported upgrade path.
6. Test offline/malformed/corrupt feed recovery, cancellation/interrupted download and controlled post-exit/lock/disk-full failures in the disposable checkpoint only. Restore the VM checkpoint, never a personal database, between destructive fault scenarios.
7. Verify uninstaller/credential retention using a disposable non-secret credential only. Archive hashes, screenshots, receipts and limits privately. Any unavailable check stays `NOT VERIFIED` or needs an explicit scoped exception.

## New-identity GitHub rehearsal preparation

Use `iamjethrooo/nerve-earnings-tracker-updates-test`, never production. Reserve fresh disposable versions after reading its releases; the completion plan uses `0.0.120` and `0.0.121`, subject to a fresh collision check. Both use package ID `EarningsTracker` and stable channel `win-x64-stable`. Existing former-identity releases are historical evidence only.

After the reviewed source prerequisite is available, run `Tools/Release.ps1` twice with `-UnsignedLocal -UpdateConfiguration RemoteTest -TestUpdateRepository iamjethrooo/nerve-earnings-tracker-updates-test -ReviewedPhase3Commit <reviewed SHA>`, distinct new artifacts directories and matching version/tag inputs. Supply A's validated full package as B's `-PreviousFullPackage`. Review manifests, exact upload sets, compiled metadata, checksums and delta reconstruction before requesting publication approval. Do not silently replace a missing historical reviewed SHA with HEAD.

Prepare notes titled `UNSIGNED TEST — EarningsTracker 0.0.120` / `0.0.121`; test tags target the verified README-only repository commit, not application source. Use separate installation/data directories under artifacts, a child-process-only `NERVE_TRACKER_DATA_DIRECTORY`, and synthetic data. Do not inherit `NERVE_TRACKER_LOCAL_UPDATE_SOURCE`. Retain before/after row fingerprints and integrity/FK results. Pause before creating drafts, uploading or publishing: anonymous discovery requires approved stable test publication. Once authorized, publish A, install and establish baseline, then publish B and verify discovery/download/manual restart, preservation and recovery. Recheck the exact public assets against approved hashes.

The detailed Phase 1–4 plans and verification records are retained under local-only `.local-docs/` for this checkout. The maintained release procedure is this document and `Tools/Release.ps1`.
