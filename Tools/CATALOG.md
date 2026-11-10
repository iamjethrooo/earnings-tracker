# Maintaining the Games catalog

The catalog is independent of earnings. `Data/catalog.json` assigns stable IDs to every seeded title. Do not regenerate these IDs or change earnings data to match providers. The initial catalog deliberately marks all unverified entries unresolved.

The populated bundle contains 564 reviewed identities/genre records, 563 artwork records, 485 offer/source records, and 55 reviewed Philippine PC Game Pass mappings. All 506 active identities are resolved; archived entries require no resolution. OPTCG Simulator retains an artwork placeholder and unverified purchase prices stay unknown. `Tools/populate_catalog.py` prepares exact title matches, corroborates conflicting identities using shared Steam IDs, caches ITAD mappings, and verifies permanent free access against official Philippine Steam listings. `Tools/apply_edition_choices.py` and `Tools/review_remaining.py` record reviewed identities and the owner's explicit choices. These are maintainer tools; desktop users configure their optional ITAD key in Settings and refresh prices through Settings → Refresh now or Games → Refresh catalog.

Duplicate seed titles have NameOccurrence and ExpectedNameCount. Initial import uses the original ascending game-ID order only when the exact duplicate count matches; otherwise those entries stay unmatched. Once assigned, the persisted stable catalog ID takes precedence. Do not remove duplicate financial records or add aliases to occurrence mappings.

Requires Python 3.11+ (standard library only). Configure `IGDB_CLIENT_ID` and `IGDB_CLIENT_SECRET` once for the initial metadata fetch. Configure `ITAD_API_KEY` once to prepare initial prices. Never commit credentials. Normal builds, publishing, and desktop users need none of these keys. Optional desktop price keys are stored using Windows Credential Manager.

Back up `Data/IgdbCache` and `Data/igdb-candidates.json` to retain the one-time provider fetch. They remain on disk but are excluded from Git and the desktop bundle; the reviewed metadata and checksummed artwork are bundled separately.

## Multiplayer metadata

Schema 1 accepts optional `SupportsMultiplayer` (boolean or null), `MultiplayerProvenance`, and
`MultiplayerVerifiedAt`. A known value requires a reviewed identity, nonempty source evidence and
a real verification date. Missing fields mean unknown; legacy readers can ignore these additive
fields. No identity mapping or account ownership contract changes.

`Tools/multiplayer.py` fetches [IGDB structured game modes](https://api-docs.igdb.com/#game-mode)
for existing reviewed IGDB IDs only. Multiplayer, Co-operative, Split screen, MMO and Battle Royale
count as support, as do explicit PC multiplayer/co-op records. An explicit Single player mode alone
records false; missing modes and identities stay unknown. Game-level modes may span platforms:
the filter includes local modes and does not guarantee a particular PC mode, current servers,
cross-play or player count. Descriptions and title keywords never supply a claim.

```powershell
python Tools/multiplayer.py collect --catalog Data/catalog.json --output .local-docs/multiplayer/igdb-modes.json
python Tools/multiplayer.py apply --catalog Data/catalog.json --collection .local-docs/multiplayer/igdb-modes.json --output .local-docs/multiplayer/catalog.json
```

Collection uses the configured IGDB credentials; the saved response contains public metadata only.
Apply is offline. Review its candidate before copying it to `Data/catalog.json`. Only mode fields
and a deterministic version suffix change. The existing publication cutoff stays intact so old
offer quotes do not overwrite newer live prices; mode evidence has its own check date. Use the
current catalog as input: clients with a later published snapshot retain the normal downgrade guard.
Re-run collection explicitly to refresh modes. Standard enrichment preserves modes for unchanged
IGDB identities and clears them when an identity changes; manual metadata can supply the same
three evidence fields. Older full snapshots without mode fields restore unknown support on import.
Desktop users need no provider credentials. Publishing still requires the existing approval gates.

The 2026-11-08 bundled enrichment records 403 multiplayer entries, 156 single-player-only entries,
and 20 unknown entries, retaining duplicate/rate-version catalog identities independently.

## Steam ownership identities

`Data/steam-app-mappings.json` is embedded in the desktop app for matching linked Steam libraries. It maps reviewed ITAD identities to a single Steam app ID, using the existing local `Data/itad-steam-cache.json` shop-61 cache prepared by `populate_catalog.py`. The cache itself remains local and is not needed to build the application. Prepare a reviewable candidate offline:

```powershell
python Tools/build_steam_app_mappings.py --catalog Data/catalog.json --cache Data/itad-steam-cache.json --output .local-docs/steam-app-mappings-candidate.json
python -m unittest discover -s Tools -p "test_*.py"
```

Compare the candidate with the bundled map before copying reviewed changes into `Data/steam-app-mappings.json`. Only reviewed catalog identities with exactly one app ID are included; package/subscription/bundle IDs and ambiguous multiple-app results are omitted. Runtime matching also accepts reviewed direct Steam app links, rejects conflicting app IDs and skips edition-only Steam offers. The map does not establish a user's ownership; the linked account's successful owned-games response does. A catalog update introducing a new identity without a bundled mapping/direct app link remains manual until a later reviewed map ships. This command makes no network requests, changes no financial records and publishes nothing; existing publication gates still apply.

## Proposed shared identity contract (PRO-44)

Design prepared on 2026-11-08 for [PRO-44](https://linear.app/botlog/issue/PRO-44).
**Design approved by the owner on 2026-11-09.** PRO-45 implements schema-2
validation, bundled import and exact matching. The bundled snapshot uses schema 1.
PRO-51 implements the separate schema-2 channel and activation rules below.
This contract does not authorize provider collection, mapping approval, catalog replacement, or a
new application release. PRO-17 still owns account access/privacy, freshness,
and effective Owned/Playable now semantics.

### Verified baseline and offline evidence

The issue's original baseline predates `Utilities/SteamLibrary.cs` and
`Tools/build_steam_app_mappings.py`. Current matching uses reviewed metadata,
the embedded ITAD-keyed map, and direct Steam offer AppIDs. Any non-base-game
Steam offer blocks matching; otherwise exactly one runtime candidate is
required. This is not yet per-AppID review: `GameMetadata.Reviewed` reviews the
catalog identity, and neither the embedded map nor extracted offer IDs carry
their own review/provenance. Cached candidates are not read at runtime.

Offline inventory of `catalog-20261012T034024414899Z-multiplayer-20261108T103507Z`,
with no provider calls. Rechecked on 2026-11-09 against the current local files.

| Current matcher eligibility (not user ownership) | All identities | Active seed identities |
| --- | ---: | ---: |
| Single runtime AppID, reviewed, no edition blocker | 484 | 431 |
| Reviewed, no runtime AppID | 62 | 57 |
| Reviewed, blocked by a non-base Steam offer | 18 | 18 |
| Unreviewed catalog identity | 15 | 0 |
| Total | 579 | 506 |

Active/archive classification uses the original `Data/games.json` name and
occurrence, as `catalog.is_archived` does; it is not a query of personal data.
Of the 579 entries, 564 have reviewed metadata. Across reviewed `SteamIds`,
the local shop-61 cache, embedded mappings and direct Steam offer links, 500
entries have one distinct candidate, 73 have none and six have multiple.
These are evidence coverage counts, **not 500 approved ownership mappings**.
The source sets overlap: 28 entries have reviewed `SteamIds`, 485 have cached
AppIDs, 479 use the 471 embedded ITAD mappings, and 91 have direct app links.
There are zero multiple-ID conflicts within the runtime sources alone; the
six broader candidate conflicts below still need explicit disposition.

| Entry | Local AppID candidates | Current behavior |
| --- | --- | --- |
| SCP Containment Breach | 2178380, 3831680 | Maps direct offer 2178380; cache has both |
| Portal 2 | 620, 659 | Unmapped |
| Borderlands 2 | 49520, 217490 | Unmapped |
| Metro Exodus | 412020, 1449560 | Unmapped |
| Cold War | 260650, 834490 | Unmapped; reviewed `SteamIds` contains only 260650 |
| SWORN | 1034530, 1763250 | Edition-blocked; direct offer is 1763250 |

Seven AppIDs are shared by multiple currently matchable catalog identities:
2051620 (Enlisted), 1190460 (Death Stranding), 3280350 (Death Stranding 2),
220200 (Kerbal Space), 585420 (Trailmakers), 954850 (Kerbal Space Program 2),
and 645630 (the two Car Mechanic Simulator 2018 names). Sharing is not itself
a conflict; each catalog identity needs its own review.

Inventory inputs and SHA-256 receipts for the local working-copy bytes.
Line-ending conversion can change the hash without changing the parsed catalog.

| Local input | SHA-256 |
| --- | --- |
| `Data/catalog.json` | `2f9ae18888305d867d74f45d91a9e12e378725d082befd5d84e1f91b17e44d42` |
| `Data/catalog-review.json` | `75e846b810e7aa9aebff8e77a2c4d45eba763c820699f647b897577095f6faa4` |
| `Data/steam-app-mappings.json` | `e66f94e701635def8f78068ad32452054231020c2fc22fd641c5cd273533c630` |
| `Data/itad-steam-cache.json` (local only) | `83c43767f1619132150d94d90304a7f591f483bf1099f3af2c377e19ba23e66f` |
| `Data/games.json` | `91eb2f5bd0c4f6f8aef313aa03c991e34871b39a92399bd56546642af1b7d0e8` |

The local-only receipt/script are `.local-docs/PRO-44/inventory.json` and
`inventory.py`; run `python .local-docs/PRO-44/inventory.py` when available.
They enumerate every CatalogId and each source's IDs. To reproduce elsewhere,
join reviews on CatalogId and bundle/cache on ItadId, accept only positive
uint32 `app/<ASCII digits>` tokens, extract HTTPS Steam `/app/` offer IDs,
union per identity, and apply the current matcher gates separately. Missing
local cache means incomplete inventory, not evidence of no Steam identity.
Nothing here re-verifies current store availability, editions or entitlements.

### Identity, review and cardinality

`CatalogId` is the durable catalog identity. Treat it as an opaque,
case-sensitive nonempty ID, retaining every existing value; do not derive it
from a title or provider ID. `Game.Id` is the local earning/history identity.
IGDB, ITAD and Steam IDs are external references and never replace either.
The existing unique `GameMetadata.CatalogId` and primary key `GameId` keep one
catalog link per local financial row. Duplicate/rate-version rows retain
distinct CatalogIds even when they describe the same Steam game.

The proposed schema-2 `CatalogEntry.SteamIdentity` object is required for every
entry. Its complete value replaces the previous mapping on activation:

```json
{
  "State": "Reviewed",
  "ReviewedAt": "2026-11-08T00:00:00Z",
  "Provenance": "Synthetic example: exact PC experience and alternatives checked",
  "Apps": [
    { "AppId": 1001, "Kind": "FullGame" },
    { "AppId": 1002, "Kind": "FullGame" }
  ]
}
```

IDs and timestamp in this example are synthetic, not catalog approvals.

- `State` is `Reviewed`, `Unresolved`, `NotApplicable`, or `Withdrawn`.
  `Reviewed` requires reviewed catalog metadata, a nonempty Apps array, a real
  UTC review timestamp no later than publication, and nonempty provenance
  identifying the checked PC experience, source evidence and review decision.
  Identity review alone must not promote an AppID. Copying a cache hit or
  parsing a price URL does not constitute that decision.
- Each `AppId` is a JSON integer from 1 through 4294967295. No strings,
  fractions, signed coercion or URL values. IDs must be unique within an entry;
  serialize in ascending order. Limit Apps to 32 per entry. Unknown states,
  kinds, duplicate IDs or invalid fields reject the snapshot before writes.
- `Kind` is `FullGame`, `Demo`, `Prologue`, `Dlc`, or `Tool`. A reviewed entry
  may list several Apps only as independently sufficient alternatives for
  the **same** PC experience, with one consistent Kind. This is explicit OR,
  not a list of unresolved candidates and not an AND entitlement recipe.
  Composite/deluxe entitlement requirements are unsupported and unresolved.
- `Unresolved` has empty Apps, a reason in Provenance, and null ReviewedAt;
  `NotApplicable` and `Withdrawn` have empty Apps plus a review timestamp and
  reason/evidence. NotApplicable records reviewed absence of a Steam mapping,
  not an automatic conclusion from a missing cache. Withdrawn explicitly
  retracts a prior accepted mapping. Keep candidate IDs and rejected evidence
  in maintainer review data, outside runtime Apps. Bound the review/provenance
  text to 4096 characters and include it in snapshot checksum coverage.
- The same AppID can occur in several CatalogIds after separate reviews.
  For example, both existing Enlisted identities may reference app 2051620:
  both can display the same observed Steam evidence while their rates, caps,
  sessions and costs remain separate. Never collapse them or choose a winner.

Base games, demos and prologues are distinct experiences: owning a base game
does not match its demo/prologue, and the reverse is also forbidden. An exact
demo/prologue ID may establish only observed evidence for that demo/prologue
identity. DLC/tool entries can retain exact identity evidence but cannot grant
full-game ownership or Playable now through this contract. The same restriction
applies to edition upgrades. A base AppID cannot prove deluxe content.
Package/subscription (`sub/`) and bundle IDs must never be stored as AppId,
even if their numeric part equals a valid AppID. There is no untyped numeric
provider-import path: review must establish the `app/` namespace first.

Remasters, sequels and materially different editions use distinct identities;
do not substitute the original's ID. A reviewed equivalent PC rerelease may
join an existing entry's alternative Apps only with explicit equivalence
evidence, not merely a common title. An unrelated replacement gets a new
CatalogId. Non-Steam entries can be NotApplicable; uncertain entries stay
Unresolved. Renames/aliases change display or initial linking only, and
archiving changes financial-row visibility, neither changes identity.

### Exact matching and catalog transitions

Match only the committed snapshot's Reviewed SteamIdentity for the exact
persisted CatalogId, then intersect its accepted AppIDs with the account's
successful cached library snapshot. No title, alias, offer URL, ITAD fallback,
package expansion, first-candidate selection or fuzzy match may grant evidence
in schema 2. One or more accepted equivalent IDs yields one observation per
local row; it never duplicates earnings or counts a multi-app entry twice.
An unresolved/unreviewed/missing mapping yields unresolved evidence, not proof
of non-ownership. A valid reviewed map with no library intersection means
not observed in that snapshot, not proof of non-purchase. PRO-17 determines
the UI/filter vocabulary and stale/private/offline treatment.

On successful catalog activation, rematch the existing account snapshot on
the next Games load/refresh, without fetching Steam again. Changed mappings,
withdrawals, removals, rollback and app restart must all invalidate derived
results using snapshot identity/hash plus account/sync identity. Do not store
derived booleans as manual ownership or write to the cached library AppIDs.
Failures keep the complete last committed catalog and existing Steam cache;
never publish half of a changed mapping set. See the persistence decision in
[Database and storage](../Docs/Database.md#proposed-catalog-identity-persistence-pro-44).

Removal from a complete schema-2 snapshot disables that CatalogId's active
Steam mapping but retains its local metadata link and financial row. It is
not a financial archive/delete operation. A reintroduced identical CatalogId
uses the same link. A new identity can link only to a single existing unlinked
earning row through the current exact name/alias or reviewed occurrence rules;
otherwise report unmatched. Never create an earning game, steal a linked row,
reuse a removed CatalogId for another experience, renumber local IDs or move
historical sessions. Catalog-only entries may retain mappings without a local
GameId but cannot appear as new earning rows. Duplicate initial matching still
requires exact ExpectedNameCount and stable ascending Game.Id order, with no
aliases on occurrence mappings; changing the count leaves the link unresolved.

### Schema and older clients

Use catalog **SchemaVersion 2**, not optional identity fields silently added
to v1. Current C# and Python validators require exactly 1, while unknown JSON
properties can otherwise be ignored; a v1 extension cannot safely withdraw the
legacy embedded/direct-link inference. A schema-2-capable client must continue
reading v1 with its documented legacy behavior until it activates v2. The
first v2 activation replaces that fallback completely, including for empty,
unmatched and withdrawn entries. A later automatic v1 delivery must not
silently restore the legacy rules. A deliberate compatible rollback restores
a full validated prior v2 mapping/membership set (PRO-52).

### Feed compatibility and activation

The v1.1.0 client reads only `catalog-latest/manifest.json` and accepts only
schema 1. Schema 2 uses `catalog-v2-latest/manifest.json`. Before the first
schema-2 publication, review and pin the legacy pointer to a schema-1 snapshot
older than the first schema-2 snapshot. The workflow freezes legacy publishing
once the schema-2 channel exists. A new client tries schema 2 first and falls
back to the legacy feed on HTTP 404 until schema 2 has activated. A missing
feed or failed check leaves the saved catalog available offline. Old executables
cannot interpret schema-2 withdrawals. Use a compatible build with any
profile that has activated schema 2.

Schema-2 manifests declare `SchemaVersion`, `Version`, and
`MinimumClientVersion` (a released MAJOR.MINOR.PATCH), plus the immutable
version's `CatalogUrl` and SHA-256. Release builds compare against their
embedded release version before fetching the snapshot. Development builds
without release metadata show the update as requiring a compatible app.
Historical schema-1 manifests can omit `Version`. The immutable asset's
release tag still has to match the payload's version. Each byte change,
including a mapping-only correction, requires a new version.

The client checks the exact downloaded bytes, schema, version, UTC publication
time, and catalog mappings before activation. Snapshots advance by UTC
publication time, with ordinal version order breaking ties within a schema.
The first schema-2 snapshot must be strictly newer than the saved schema-1
snapshot. SHA-256 detects bytes that differ from the trusted manifest.
Trust comes from HTTPS, the configured GitHub repository path, and the
allowlisted GitHub release asset redirect hosts. The checksum does not
authenticate an independent publisher.

Settings shows the active catalog, the last confirmed check, and the most
recent result. An applied snapshot reports its unmatched count. Already
active, older, incompatible, rejected, and unavailable snapshots are
reported separately. Automatic checks remain once daily when Games opens.
Manual checks can retry after a failure. Rate limits do not trigger an
immediate retry. The catalog check has a 25-second deadline, bounded downloads,
and a limited redirect chain.

Activation treats the snapshot as complete. An unmatched entry creates no
earning game. An omitted entry loses its active enrichment, offers,
subscriptions, and provider mappings while retaining its stable local link.
A reintroduced entry reuses that link. See
[database storage](../Docs/Database.md) for live-price and snapshot-retention rules.

### Recover from a catalog update

Open **Settings → Catalog updates → Update details** and select **Restore
previous catalog**. Review the active and target versions before confirming.
The app verifies the retained payload and restores catalog metadata and
mappings. Financial records, manual ownership, and the Steam library cache
stay in place. Reopening Games shows matches derived from the restored mapping.

A successful restore consumes the recovery slot and rejects the replaced
version on future manual, automatic, and bundled startup checks. Refresh
resumes when a different, compatible snapshot newer than the restored one is
available. A missing or damaged prior payload leaves the active catalog
available. Catalog recovery uses a separate action from application backup
restore.

### Transition fixtures for implementation acceptance

These are design fixtures, not passing automated tests. C1/C2/C3, G1/G2 and
AppIDs 1001/1002/2001 are synthetic. A library contains `{1001}` unless stated.
Every transition must preserve sessions, audit history, financial IDs, rates,
caps/reset history, manual ownership, costs, bookmarks and library cache.

| Fixture / input transition | Expected mapping or activation result |
| --- | --- |
| C1 → G1, Reviewed FullGame [1001] | One observed match on G1 |
| C1 → G1, C2 → G2, both reviewed [1001] | Both rows observed; no merge, session move or financial write |
| C1 reviewed alternatives [1001,1002]; library {1002}, then {1001,1002} | One observation in either case |
| Candidates [1001,1002] without equivalence review | Unresolved, even if both in library |
| C1 base [1001], C2 prologue [2001] | C1 observed, C2 not observed; no cross-match |
| Deluxe or DLC C2 with only base licence 1001 | Unresolved deluxe; DLC cannot grant full-game evidence |
| `sub/1001`, `bundle/1001`, zero, fraction or uint overflow | Reject invalid runtime mapping; no writes |
| C1 renamed/aliased or local G1 archived | Keep C1 → G1; do not change history or unarchive |
| Reviewed [1001] replaced by [1002] | Old observation disappears; cached library unchanged |
| Reviewed mapping becomes Withdrawn or Unresolved | No accepted mapping; no legacy fallback |
| C1 omitted, then reintroduced | Disable active mapping while absent; retain and reuse C1 → G1 |
| New C3 with no local row, or colliding with linked G1 | Report unmatched/conflict; never create/reassign earning rows |
| Duplicate name count differs from ExpectedNameCount | Leave initial link unresolved; existing CatalogId links still win |
| Identity metadata unreviewed but SteamIdentity Reviewed | Reject snapshot, retain prior committed state |
| Old v1 client receives v2 | Reject unsupported schema and retain saved v1/financial data |
| New client loads v1, then v2 with empty Apps, then attempts v1 | Legacy only before v2 activation; later v1 cannot restore inference |
| Invalid checksum/partial update or crash before commit | Retain prior complete mapping/membership/version |
| Validated v2 rollback changes [1002] back to [1001] | Recompute evidence from cached library; no database-wide restore |
| Account switch or offline reload after mapping change | Use only selected account cache; refresh derived results without a request |

PRO-45 covers representation, validation and exact matching.
PRO-51 covers compatibility, activation and complete-snapshot removal.
PRO-52 covers catalog-only rollback. PRO-49
applies the approved PRO-17 account, privacy, and filtering behavior to the
existing Steam reader. The inventory supplies evidence for later AppID review.
Approval of this contract is separate from approval of individual AppIDs.

### Build and inspect schema 2 locally

Add an explicit `SteamIdentity` object to each reviewed maintainer mapping under
`Mappings[CatalogId]` in the review input. Entries without a decision become
`Unresolved`. Record conflicting evidence as an `Unresolved` reason and keep
its candidates outside runtime `Apps`. Legacy `SteamIds`, store URLs, and
cached candidates are evidence a maintainer can review.

Run `python Tools/catalog.py enrich --steam-identities --output schema2.json`
using the usual reviewed inputs and metadata caches. For an offline coverage
report, run `python Tools/catalog.py report --catalog schema2.json --output coverage.json`.
`SteamIdentityCoverage` counts each decision state, and `UnresolvedSteamIdentities`
lists catalog IDs and review reasons. Local package verification uses
`package --steam-identities --minimum-client-version MAJOR.MINOR.PATCH --catalog schema2.json --output release`.
Use the actual compatible release version when preparing an approved package.
The package copies the exact catalog bytes. To rehearse publication ordering
offline, run `python Tools/catalog.py check_order --catalog schema2.json --previous current-schema2.json`
against a local copy of the current immutable snapshot.

The gated workflow reads `Data/catalog-v2.json` for the `schema2` input and
`Data/catalog.json` for the legacy input. It checks version collisions and
channel order before creating the immutable release, then advances the
channel manifest. The approved schema-2 input must be committed separately.
Review/provider permissions and `CATALOG_PUBLISH_ENABLED` remain required.
If staging succeeds but updating the pointer fails, inspect the immutable
release and current pointer before any authorized retry. Re-running the job
with the same version fails the collision check.

## Review and enrichment

1. Run `python Tools/catalog.py candidates --output candidates.json`. This resumable search fetches descriptions, genres, and cover identifiers along with match candidates and saves full responses in `Data/IgdbCache`. Completed searches require no further credentials. Back up the cache and candidates file; they contain data, not credentials.
2. Check each title's edition, platform, remaster/year, and provider IDs. Add entries to `Data/catalog-review.json` keyed by the existing CatalogId: `{"Reviewed": true, "IgdbId": 123, "ItadId": "provider-uuid", "Aliases": []}`. Never approve an uncertain match.
3. Run `python Tools/catalog.py enrich --output enriched.json` to prepare local data. This uses cached responses without IGDB authentication and downloads each reviewed cover once to `Data/Artwork` with a SHA-256 name. Re-running enrichment reuses the saved metadata and artwork. Only a newly chosen uncached ID or explicit `--refresh-igdb` needs IGDB credentials again. Copy the reviewed result into `Data/catalog.json` when ready. Redistribution flags are checked when packaging for public release, not during private preparation.
4. Review acquisition sources not covered by the price provider. Add `Offers` and `Subscriptions` to a mapping with actual checked dates and provenance. Offers use Source, Url, Edition, Region (`PH`), Currency, RegularPrice, SalePrice, Kind (`Purchase`, `FreeToPlay`, `Giveaway`, `Subscription`, `Unknown`), VerifiedAt, optional ExpiresAt, and Provenance. Subscriptions use Service (`PC Game Pass`), Platform (`PC`), Region (`PH`), Included, VerifiedAt, optional ExpiresAt, Url, and Provenance.
5. When updating subscription availability, review it against the official regional PC catalogs and re-run enrichment with the saved cache. This needs no IGDB credentials. Merely building a snapshot never refreshes verification timestamps. Previously verified PH/PC inclusion remains in the PC Game Pass filter after seven days or recorded expiry, with its actual last verification date and “Game Pass availability may have changed.” A newer recorded not-included result replaces old inclusion. Stale subscription evidence cannot grant Playable now; current membership, seven-day verification and unexpired availability are still required. Settings distinguishes outdated inclusion from currently verified non-inclusion; outdated verification does not establish removal.
6. Run `python Tools/catalog.py prices --output priced.json` once and copy the result into `Data/catalog.json`. These initial dated prices ship with the app and are available without a user API key. Only reviewed ITAD mappings are used; PH requests preserve original currency and affiliate links. Unsupported stores remain manual. Zero-price purchase offers display as Free-to-play and appear in Free-to-play/Playable now filters; original provider classifications are retained, and expired zero-price sales fall back to the regular price. Price redistribution approval is checked at public packaging time.
7. Run `python Tools/catalog.py report --catalog priced.json --output coverage.json`. Review unresolved entries and coverage counts, set ReleaseApproved, then run `python Tools/catalog.py package --catalog priced.json --output release`. The package contains catalog, artwork, manifest checksums, and coverage report.

IGDB: https://api-docs.igdb.com/ (attribution and applicable Twitch/IGDB permissions). Prices: https://docs.isthereanydeal.com/ (preserve provided links). FX: https://frankfurter.dev/ (estimated conversion only).

## Subscription collection and review

Subscription collection needs no IGDB or account credentials. It reads official public catalogs, retains provider IDs and evidence, and prepares review candidates without changing the app database.

```powershell
python Tools/subscriptions.py collect --output subscription-candidates.json
python Tools/subscriptions.py report --collection subscription-candidates.json --output subscription-report.md
```

Collection supports only PC Game Pass. Xbox requests market PH and verifies current Windows full-game entitlements against the PC Game Pass plan. Trials, preorders, discounts, and unconditional free-game perks are excluded. Partial lists or failed downloads cannot establish removals; source cache data is retained locally in `Data/SubscriptionCache`.

The collection contains 434 eligible PH Game Pass products and 55 reviewed matches to active catalog entries, including the newly resolved Planet Crafter. EA Play/Ubisoft adapters and candidates have been removed. See `Data/subscription-report.md` and the collection JSON for the dated evidence.

The focused research tool `python Tools/resolve_remaining.py` caches only unresolved active searches in `Data/remaining-candidates.json` and `Data/IgdbCache`. `Tools/review_remaining.py` applies the recorded edition choices and checks exact Steam app IDs in PH, keeping separate prologues and platform experiences. These are one-time maintainer tasks. Reviewed mappings can include `ManualMetadata` (Summary, Genres, optional CoverUrl, and required Provenance), with a null IGDB ID where IGDB has no verified matching record. Enrichment reuses cached artwork and never invents a provider ID. A paid game's demo or a ROM-dependent mod is not marked as free standalone access. See `Data/catalog-resolution-report.json` for the recorded evidence.

Review `subscription-candidates.review.json`. Its CollectionHash binds decisions to that exact collection. For each approved candidate add a decision with CandidateId, Approved, IdentityConfirmed, EditionConfirmed, and Evidence. Candidates without verified PH evidence also require RegionConfirmed and RegionEvidence. Check the exact game, edition, PC platform, and regional full-game inclusion; do not infer access from a trial or discount. Duplicate financial titles and competing provider editions require explicit review. Archived entries are ignored. Collections must be no older than seven days.

```powershell
python Tools/subscriptions.py apply --collection subscription-candidates.json --decisions subscription-candidates.review.json --output reviewed-subscriptions.json
python Tools/subscriptions.py report --collection subscription-candidates.json --decisions subscription-candidates.review.json --output subscription-report.md
```

Apply writes a reviewable snapshot and records the reviewed mappings in `Data/catalog-review.json`. Copy the reviewed snapshot to `Data/catalog.json` to ship it; the app imports bundled snapshots transactionally and preserves ownership, settings, and earnings. Use a new output filename for each collection to retain its corresponding decisions.

Previously reviewed provider IDs can be rechecked without repeating identity review:

```powershell
python Tools/subscriptions.py reverify --collection subscription-candidates.json --output subscription-snapshot.json
```

Reverification only refreshes unchanged, previously approved IDs with fresh PH evidence. Additions, removals, region changes, and edition changes remain review tasks. The weekly `.github/workflows/subscriptions.yml` workflow collects, tests, and exports candidates, a report, and this refreshed snapshot as artifacts. It requires no secrets, publishes nothing, and starts only after pushing to the default branch with GitHub Actions enabled. Approved snapshots still use the permission-gated publishing process below. Desktop users select their plans and refresh published snapshots in-app; they never run these maintainer commands.

## Publishing

The workflow runs only on manual dispatch with `CATALOG_PUBLISH_ENABLED` set to `true`. It packages the committed snapshot and stages its immutable release before updating the selected channel. It needs no IGDB or price API secrets and has no scheduled price refresh. Approve reviewed mappings and permission/release flags before enabling it.

Desktop clients check the manifest at most once daily after opening Games, and can refresh manually. Updates validate the schema, mappings, and checksums before importing in one SQLite transaction. Missing releases/providers leave saved data available. Optional personal keys enable manual price refresh. FX refresh uses Frankfurter's keyless USD-based daily rates, preserving the last valid rates offline. The initial public channel will be unavailable until the first approved snapshot is published.

For offline installation, keep the reviewed catalog in `Data/catalog.json` and artwork in `Data/Artwork` before building. Both are copied into the app output. Initial prices remain visible after becoming stale (48 hours), with their checked dates; users can enter an optional IsThereAnyDeal key and manually refresh prices. Nobody needs an IGDB account to use the app. FX dates are displayed on estimates. Ownership is manual and persists independently of catalog refreshes.
