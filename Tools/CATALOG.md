# Maintaining the Games catalog

The catalog is independent of earnings. `Data/catalog.json` assigns stable IDs to every seeded title. Do not regenerate these IDs or change earnings data to match providers. The initial catalog deliberately marks all unverified entries unresolved.

The populated bundle contains 564 reviewed identities/genre records, 563 artwork records, 485 offer/source records, and 55 reviewed Philippine PC Game Pass mappings. All 506 active identities are resolved; archived entries require no resolution. OPTCG Simulator retains an artwork placeholder and unverified purchase prices stay unknown. `Tools/populate_catalog.py` prepares exact title matches, corroborates conflicting identities using shared Steam IDs, caches ITAD mappings, and verifies permanent free access against official Philippine Steam listings. `Tools/apply_edition_choices.py` and `Tools/review_remaining.py` record reviewed identities and the owner's explicit choices. These are maintainer tools; desktop users configure their optional ITAD key in Settings and refresh prices through Settings → Refresh now or Games → Refresh catalog.

Duplicate seed titles have NameOccurrence and ExpectedNameCount. Initial import uses the original ascending game-ID order only when the exact duplicate count matches; otherwise those entries stay unmatched. Once assigned, the persisted stable catalog ID takes precedence. Do not remove duplicate financial records or add aliases to occurrence mappings.

Requires Python 3.11+ (standard library only). Configure `IGDB_CLIENT_ID` and `IGDB_CLIENT_SECRET` once for the initial metadata fetch. Configure `ITAD_API_KEY` once to prepare initial prices. Never commit credentials. Normal builds, publishing, and desktop users need none of these keys. Optional desktop price keys are stored using Windows Credential Manager.

Back up `Data/IgdbCache` and `Data/igdb-candidates.json` to retain the one-time provider fetch. They remain on disk but are excluded from Git and the desktop bundle; the reviewed metadata and checksummed artwork are bundled separately.

## Review and enrichment

1. Run `python Tools/catalog.py candidates --output candidates.json`. This resumable search fetches descriptions, genres, and cover identifiers along with match candidates and saves full responses in `Data/IgdbCache`. Completed searches require no further credentials. Back up the cache and candidates file; they contain data, not credentials.
2. Check each title's edition, platform, remaster/year, and provider IDs. Add entries to `Data/catalog-review.json` keyed by the existing CatalogId: `{"Reviewed": true, "IgdbId": 123, "ItadId": "provider-uuid", "Aliases": []}`. Never approve an uncertain match.
3. Run `python Tools/catalog.py enrich --output enriched.json` to prepare local data. This uses cached responses without IGDB authentication and downloads each reviewed cover once to `Data/Artwork` with a SHA-256 name. Re-running enrichment reuses the saved metadata and artwork. Only a newly chosen uncached ID or explicit `--refresh-igdb` needs IGDB credentials again. Copy the reviewed result into `Data/catalog.json` when ready. Redistribution flags are checked when packaging for public release, not during private preparation.
4. Review acquisition sources not covered by the price provider. Add `Offers` and `Subscriptions` to a mapping with actual checked dates and provenance. Offers use Source, Url, Edition, Region (`PH`), Currency, RegularPrice, SalePrice, Kind (`Purchase`, `FreeToPlay`, `Giveaway`, `Subscription`, `Unknown`), VerifiedAt, optional ExpiresAt, and Provenance. Subscriptions use Service (`PC Game Pass`), Platform (`PC`), Region (`PH`), Included, VerifiedAt, optional ExpiresAt, Url, and Provenance.
5. When updating subscription availability, review it against the official regional PC catalogs and re-run enrichment with the saved cache. This needs no IGDB credentials. Merely building a snapshot never refreshes verification timestamps. Subscription entries older than seven days remain visible but are excluded from Playable now until reverified.
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

The checked-in workflow remains inactive until repository variable `CATALOG_PUBLISH_ENABLED` is `true`. It runs only on manual dispatch, packages the committed initial metadata/prices, and publishes immutable version releases plus a `catalog-latest` channel. It needs no IGDB or price API secrets and has no scheduled price refresh. Approve reviewed mappings and permission/release flags before enabling it. You can publish optional catalog corrections later using the saved data without setting up IGDB again.

Desktop clients check the manifest at most once daily after opening Games, and can refresh manually. Updates validate the schema, mappings, and checksums before importing in one SQLite transaction. Missing releases/providers leave saved data available. Optional personal keys enable manual price refresh. FX refresh uses Frankfurter's keyless USD-based daily rates, preserving the last valid rates offline. The initial public channel will be unavailable until the first approved snapshot is published.

For offline installation, keep the reviewed catalog in `Data/catalog.json` and artwork in `Data/Artwork` before building. Both are copied into the app output. Initial prices remain visible after becoming stale (48 hours), with their checked dates; users can enter an optional IsThereAnyDeal key and manually refresh prices. Nobody needs an IGDB account to use the app. FX dates are displayed on estimates. Ownership is manual and persists independently of catalog refreshes.
