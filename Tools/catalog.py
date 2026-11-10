"""Reviewed catalog preparation. Python 3.11+, standard library only; no secrets in output."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RELEASE_ROOT = "https://github.com/iamjethrooo/earnings-tracker/releases/download/"


def immutable_version(value):
    return (isinstance(value, str) and re.fullmatch(r"catalog-[A-Za-z0-9][A-Za-z0-9.-]{0,119}", value)
            and value not in ("catalog-latest", "catalog-v2-latest"))


def is_archived(entry):
    """Use the original seed occurrence, preserving separate duplicate records."""
    seed = load(ROOT / "Data/games.json")
    matches = [g for g in seed if g["Name"].casefold() == entry["Name"].casefold()]
    occurrence = entry.get("NameOccurrence") or 1
    return occurrence <= len(matches) and bool(matches[occurrence - 1].get("IsArchived"))


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def save(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def request(url, payload=None, headers=None, raw=False):
    data = payload.encode() if isinstance(payload, str) else (json.dumps(payload).encode() if payload is not None else None)
    req = urllib.request.Request(url, data=data, headers={"User-Agent": "EarningsTracker-Catalog/1.0", **(headers or {})})
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            result = response.read(16 * 1024 * 1024 + 1)
            if len(result) > 16 * 1024 * 1024:
                raise ValueError("Provider response exceeds limit")
            return result if raw else json.loads(result)
    except urllib.error.HTTPError as error:
        # Do not log URLs or request headers: credentials may be present.
        raise RuntimeError(f"Provider returned HTTP {error.code}; retry later (no immediate retry)") from None


def igdb_headers():
    client = os.environ.get("IGDB_CLIENT_ID")
    secret = os.environ.get("IGDB_CLIENT_SECRET")
    if not client or not secret:
        raise ValueError("Set IGDB_CLIENT_ID and IGDB_CLIENT_SECRET in your environment")
    body = urllib.parse.urlencode({"client_id": client, "client_secret": secret, "grant_type": "client_credentials"})
    token = request("https://id.twitch.tv/oauth2/token", body, {"Content-Type": "application/x-www-form-urlencoded"})["access_token"]
    return {"Client-ID": client, "Authorization": "Bearer " + token, "Content-Type": "text/plain"}


def candidates(args):
    headers = None
    catalog = load(args.catalog)
    results = load(args.output) if Path(args.output).exists() else {}
    for game in catalog["Games"]:
        if is_archived(game) and not game.get("Reviewed"):
            continue
        if game["CatalogId"] in results:
            continue
        if headers is None:
            headers = igdb_headers()
        query = f'search {json.dumps(game["Name"], ensure_ascii=False)}; fields name,slug,summary,genres.name,cover.image_id,platforms.name,game_type,first_release_date,version_parent; limit 8;'
        matches = request("https://api.igdb.com/v4/games", query, headers)
        for match in matches:
            save(Path(args.cache) / (str(match["id"]) + ".json"), {"FetchedAt": now(), "Game": match})
        results[game["CatalogId"]] = {"Name": game["Name"], "Candidates": matches}
        save(args.output, results)
        time.sleep(0.3)
    print(f"Prepared candidates for {len(results)} titles; nothing is automatically approved")


def enrich(args):
    review = load(args.review)
    headers = None
    catalog = load(args.catalog)
    artwork = Path(args.artwork)
    artwork.mkdir(parents=True, exist_ok=True)
    mappings = review.get("Mappings", {})
    include_steam = getattr(args, "steam_identities", False) or catalog["SchemaVersion"] == 2
    processed = 0
    for game in catalog["Games"]:
        mapping = mappings.get(game["CatalogId"])
        if not mapping or not mapping.get("Reviewed"):
            continue
        if game.get("IgdbId") != mapping.get("IgdbId"):
            for field in ("SupportsMultiplayer", "MultiplayerVerifiedAt", "MultiplayerProvenance"):
                game.pop(field, None)
        if mapping.get("ManualMetadata") is not None:
            # Provider-absent prologues and platform experiences keep explicit, reviewed official metadata.
            metadata = mapping["ManualMetadata"]
            if not mapping.get("MatchEvidence") or not metadata.get("Provenance"):
                raise ValueError("Manual metadata requires identity evidence and provenance")
            game.update({"IgdbId": mapping.get("IgdbId"), "Reviewed": True,
                "ItadId": mapping.get("ItadId"), "Aliases": mapping.get("Aliases", []),
                "Summary": metadata.get("Summary", ""), "Genres": metadata.get("Genres", []),
                "VerifiedAt": mapping["ReviewedAt"], "Provenance": metadata["Provenance"]})
            for field in ("SupportsMultiplayer", "MultiplayerVerifiedAt", "MultiplayerProvenance"):
                if field in metadata:
                    game[field] = metadata[field]
            url = metadata.get("CoverUrl", "")
            if url:
                if not url.startswith("https://"): raise ValueError("HTTPS artwork URL is required")
                checksum = game.get("CoverSha256", "") if game.get("CoverUrl") == url else ""
                path = artwork / (checksum + ".img")
                data = path.read_bytes() if checksum and path.exists() else request(url, raw=True)
                if checksum and hashlib.sha256(data).hexdigest() != checksum: raise ValueError("Cached artwork checksum mismatch")
                checksum = hashlib.sha256(data).hexdigest()
                (artwork / (checksum + ".img")).write_bytes(data)
                game.update(CoverUrl=url, CoverSha256=checksum)
            else:
                game.update(CoverUrl="", CoverSha256="")
            for field in ("Offers", "Subscriptions"):
                if field in mapping: game[field] = mapping[field]
            continue
        igdb_id = int(mapping["IgdbId"])
        cached_path = Path(args.cache) / (str(igdb_id) + ".json")
        if cached_path.exists() and not args.refresh_igdb:
            cached = load(cached_path)
        elif game.get("Reviewed") and game.get("IgdbId") == igdb_id and not args.refresh_igdb:
            # Existing enriched entries need no credentials to update manual sources/subscriptions.
            cached = None
        else:
            if headers is None:
                headers = igdb_headers()
            query = f'fields name,summary,genres.name,cover.image_id,platforms.name; where id = {igdb_id};'
            matches = request("https://api.igdb.com/v4/games", query, headers)
            if len(matches) != 1:
                raise ValueError(f'IGDB mapping not found: {game["Name"]}')
            cached = {"FetchedAt": now(), "Game": matches[0]}
            save(cached_path, cached)
            time.sleep(0.3)
        if cached is not None:
            match = cached["Game"]
            if match["id"] != igdb_id:
                raise ValueError("Cached IGDB identity does not match reviewed mapping")
            game.update({"Summary": match.get("summary", ""), "Genres": sorted({x["name"] for x in match.get("genres", [])}),
                         "VerifiedAt": cached["FetchedAt"]})
            if "cover" in match:
                url = "https://images.igdb.com/igdb/image/upload/t_cover_big/" + match["cover"]["image_id"] + ".jpg"
                old_hash = game.get("CoverSha256", "") if game.get("CoverUrl") == url else cached.get("CoverSha256", "")
                old_path = artwork / (old_hash.lower() + ".img")
                if old_hash and old_path.exists():
                    data = old_path.read_bytes()
                    if hashlib.sha256(data).hexdigest() != old_hash.lower():
                        raise ValueError("Cached artwork checksum mismatch")
                else:
                    data = request(url, raw=True)
                checksum = hashlib.sha256(data).hexdigest()
                (artwork / (checksum + ".img")).write_bytes(data)
                game.update({"CoverUrl": url, "CoverSha256": checksum})
                cached.update({"CoverUrl": url, "CoverSha256": checksum})
                save(cached_path, cached)
            else:
                game.update({"CoverUrl": "", "CoverSha256": ""})
        game.update({"IgdbId": igdb_id, "ItadId": mapping.get("ItadId", game.get("ItadId")), "Reviewed": True,
                     "Provenance": "IGDB; manually reviewed identity",
                     "Aliases": mapping.get("Aliases", [])})
        # Manual offers/subscriptions carry actual verification dates, never today's date merely because a build ran.
        if "Offers" in mapping:
            game["Offers"] = mapping["Offers"]
        if "Subscriptions" in mapping:
            game["Subscriptions"] = mapping["Subscriptions"]
        processed += 1
        if processed % 25 == 0 and not include_steam:
            save(args.output, catalog)
            print(f"Enriched {processed} titles", flush=True)
    stamp(catalog)
    if include_steam:
        catalog["SchemaVersion"] = 2
        for game in catalog["Games"]:
            mapping = mappings.get(game["CatalogId"], {})
            game["SteamIdentity"] = mapping.get("SteamIdentity", {
                "State": "Unresolved", "ReviewedAt": None, "Apps": [],
                "Provenance": "No explicit Steam identity review for this catalog ID.",
            })
        validate(catalog)
    save(args.output, catalog)
    print("Built enrichment for explicitly reviewed mappings")


def stamp(catalog):
    catalog["PublishedAt"] = now()
    catalog["Version"] = "catalog-" + dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")


def prices(args):
    key = os.environ.get("ITAD_API_KEY")
    if not key:
        raise ValueError("Set ITAD_API_KEY in your environment")
    # Local preparation is separate from the redistribution gates checked by package().
    catalog = load(args.catalog)
    games = [g for g in catalog["Games"] if g.get("Reviewed") and g.get("ItadId")]
    for start in range(0, len(games), 200):
        batch = games[start:start + 200]
        response = request("https://api.isthereanydeal.com/games/prices/v3?country=PH&vouchers=false",
                           [g["ItadId"] for g in batch], {"ITAD-API-Key": key, "Content-Type": "application/json"})
        by_id = {r["id"]: r for r in response}
        for game in batch:
            row = by_id.get(game["ItadId"])
            if row is None:
                continue
            offers = [o for o in game.get("Offers", []) if o.get("Provenance") != "IsThereAnyDeal"]
            for deal in row["deals"]:
                if not any(p["name"] == "Windows" for p in deal["platforms"]):
                    continue
                if deal["price"]["currency"] != deal["regular"]["currency"]:
                    raise ValueError("Offer currencies differ")
                offers.append({"Source": deal["shop"]["name"], "Url": deal["url"], "Edition": "Base game",
                               "Region": "PH", "Currency": deal["price"]["currency"],
                               "RegularPrice": deal["regular"]["amount"], "SalePrice": deal["price"]["amount"],
                               "Kind": "Purchase", "VerifiedAt": parsedate(deal["timestamp"]).isoformat().replace("+00:00", "Z"),
                               "ExpiresAt": parsedate(deal["expiry"]).isoformat().replace("+00:00", "Z") if deal.get("expiry") else None,
                               "Provenance": "IsThereAnyDeal"})
            game["Offers"] = offers
    stamp(catalog)
    save(args.output, catalog)
    print(f"Refreshed Philippine prices for {len(games)} mapped games")


def parsedate(value):
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(dt.timezone.utc)


def validate_steam_identity(identity, reviewed, published):
    error = "Invalid Steam identity"
    if not isinstance(identity, dict):
        raise ValueError(error)
    state, timestamp = identity.get("State"), identity.get("ReviewedAt")
    provenance, apps = identity.get("Provenance"), identity.get("Apps")
    if (not isinstance(provenance, str) or not provenance.strip()
            or len(provenance.encode("utf-16-le")) // 2 > 4096 or not isinstance(apps, list)):
        raise ValueError(error)
    if state == "Unresolved" and timestamp is None and not apps:
        return
    if (not isinstance(timestamp, str)
            or not re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)", timestamp)
            or not dt.datetime.min.replace(tzinfo=dt.timezone.utc) < parsedate(timestamp) <= published):
        raise ValueError(error)
    if state in ("NotApplicable", "Withdrawn") and not apps:
        return
    if state != "Reviewed" or reviewed is not True or not 1 <= len(apps) <= 32:
        raise ValueError(error)
    previous, kind = 0, None
    for app in apps:
        if not isinstance(app, dict):
            raise ValueError(error)
        app_id, app_kind = app.get("AppId"), app.get("Kind")
        if (type(app_id) is not int or not previous < app_id <= 4294967295
                or app_kind not in ("FullGame", "Demo", "Prologue", "Dlc", "Tool")
                or (kind is not None and app_kind != kind)):
            raise ValueError(error)
        previous, kind = app_id, app_kind


def validate(catalog):
    schema = catalog.get("SchemaVersion")
    if type(schema) is not int or schema not in (1, 2) or not catalog.get("Version") or not catalog.get("PublishedAt"):
        raise ValueError("Invalid catalog header")
    timestamp = catalog["PublishedAt"]
    if not isinstance(timestamp, str) or not re.fullmatch(
            r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)", timestamp):
        raise ValueError("Catalog publication time must be a UTC timestamp")
    published = parsedate(timestamp)
    if not dt.datetime.min.replace(tzinfo=dt.timezone.utc) < published <= dt.datetime.now(dt.timezone.utc):
        raise ValueError("Invalid catalog publication time")
    if not isinstance(catalog.get("Games"), list) or len(catalog["Games"]) > 10000:
        raise ValueError("Invalid catalog entries")
    ids, names = set(), set()
    for game in catalog["Games"]:
        identity = game["CatalogId"]
        if not identity or identity in ids:
            raise ValueError("Duplicate catalog identity")
        ids.add(identity)
        if schema == 2:
            validate_steam_identity(game.get("SteamIdentity"), game.get("Reviewed"), published)
        occurrence = game.get("NameOccurrence")
        if occurrence is not None and (not 1 <= occurrence <= game.get("ExpectedNameCount", 0) or game.get("Aliases")):
            raise ValueError("Invalid duplicate occurrence mapping")
        for name in [game["Name"], *game.get("Aliases", [])]:
            identity_name = (name.casefold(), occurrence if name == game["Name"] else None)
            if not name.strip() or identity_name in names:
                raise ValueError("Missing or ambiguous name/alias")
            names.add(identity_name)
        enriched = game.get("CoverUrl") or game.get("Genres") or game.get("Offers") or game.get("Subscriptions")
        multiplayer = game.get("SupportsMultiplayer")
        if multiplayer is not None:
            if type(multiplayer) is not bool or not game.get("Reviewed") or not game.get("MultiplayerProvenance", "").strip() or len(game["MultiplayerProvenance"]) > 4096:
                raise ValueError("Multiplayer metadata requires a reviewed identity and source evidence")
            checked_at = parsedate(game.get("MultiplayerVerifiedAt") or "")
            if checked_at > dt.datetime.now(dt.timezone.utc) + dt.timedelta(minutes=5):
                raise ValueError("Multiplayer verification cannot be in the future")
        elif game.get("MultiplayerProvenance") or game.get("MultiplayerVerifiedAt"):
            raise ValueError("Unknown multiplayer support cannot have verification evidence")
        if enriched and not game.get("Reviewed"):
            raise ValueError(f'Unreviewed enrichment: {game["Name"]}')
        if game.get("CoverUrl"):
            if not game["CoverUrl"].startswith("https://") or len(game.get("CoverSha256", "")) != 64:
                raise ValueError("Invalid cover/checksum")
            int(game["CoverSha256"], 16)
        for offer in game.get("Offers", []):
            if not offer["Url"].startswith("https://") or offer["Region"] != "PH" or len(offer["Currency"]) != 3 or not offer.get("Provenance"):
                raise ValueError("Invalid offer")
            if offer["Kind"] not in {"Purchase", "FreeToPlay", "Giveaway", "Subscription", "Unknown"}:
                raise ValueError("Invalid acquisition kind")
            if any(offer.get(k) is not None and offer[k] < 0 for k in ("SalePrice", "RegularPrice")):
                raise ValueError("Negative price")
            parsedate(offer["VerifiedAt"])
        for sub in game.get("Subscriptions", []):
            if sub["Region"] != "PH" or sub["Platform"] != "PC" or not sub["Url"].startswith("https://") or not sub.get("Provenance"):
                raise ValueError("Invalid PC subscription entry")
            parsedate(sub["VerifiedAt"])


def report(args):
    catalog = load(args.catalog)
    validate(catalog)
    games = catalog["Games"]
    result = {"Version": catalog["Version"], "Total": len(games),
              "Reviewed": sum(bool(g.get("Reviewed")) for g in games),
              "Artwork": sum(bool(g.get("CoverUrl")) for g in games),
              "Genres": sum(bool(g.get("Genres")) for g in games),
              "Offers": sum(bool(g.get("Offers")) for g in games),
              "Subscriptions": sum(bool(g.get("Subscriptions")) for g in games),
              "Active": sum(not is_archived(g) for g in games),
              "Archived": sum(is_archived(g) for g in games),
              "Unresolved": [g["Name"] for g in games if not g.get("Reviewed") and not is_archived(g)],
              "ArchivedWithoutMetadata": [g["Name"] for g in games if not g.get("Reviewed") and is_archived(g)]}
    if catalog["SchemaVersion"] == 2:
        result["SteamIdentityCoverage"] = {state: sum(g["SteamIdentity"]["State"] == state for g in games)
            for state in ("Reviewed", "Unresolved", "NotApplicable", "Withdrawn")}
        result["UnresolvedSteamIdentities"] = [{"CatalogId": g["CatalogId"], "Reason": g["SteamIdentity"]["Provenance"]}
            for g in games if g["SteamIdentity"]["State"] == "Unresolved"]
    save(args.output, result)
    print(f'{result["Reviewed"]}/{result["Total"]} reviewed; {len(result["Unresolved"])} unresolved')


def package(args):
    catalog = load(args.catalog)
    validate(catalog)
    if catalog["SchemaVersion"] == 2 and not getattr(args, "steam_identities", False):
        raise ValueError("Schema 2 packaging requires --steam-identities for offline verification")
    version = catalog["Version"]
    if not immutable_version(version):
        raise ValueError("Invalid immutable catalog version")
    minimum = getattr(args, "minimum_client_version", None)
    if catalog["SchemaVersion"] == 2:
        if (not minimum or not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", minimum)
                or any(int(part) > 2147483647 for part in minimum.split("."))):
            raise ValueError("Schema 2 requires --minimum-client-version MAJOR.MINOR.PATCH")
    elif minimum is not None:
        raise ValueError("The legacy channel cannot require a minimum client version")
    review = load(args.review)
    if not review.get("ReleaseApproved"):
        raise ValueError("Set ReleaseApproved only after reviewing coverage and provider permissions")
    from collections import Counter
    seed_names = Counter(g["Name"] for g in load(ROOT / "Data/games.json"))
    if catalog["SchemaVersion"] == 1 and seed_names != Counter(g["Name"] for g in catalog["Games"]):
        raise ValueError("Catalog must account for every seeded game")
    if any(g.get("IgdbId") for g in catalog["Games"]) and not review.get("IgdbRedistributionApproved"):
        raise ValueError("IGDB redistribution permission is not approved")
    if any(o.get("Provenance") == "IsThereAnyDeal" for g in catalog["Games"] for o in g.get("Offers", [])) and not review.get("PriceRedistributionApproved"):
        raise ValueError("Price redistribution permission is not approved")
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    # Keep the source bytes so a bundled and remotely delivered version have the same identity.
    (output / "catalog.json").write_bytes(Path(args.catalog).read_bytes())
    with zipfile.ZipFile(output / "artwork.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for game in catalog["Games"]:
            checksum = game.get("CoverSha256")
            if not checksum:
                continue
            path = Path(args.artwork) / (checksum.lower() + ".img")
            if not path.exists():
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(request(game["CoverUrl"], raw=True))
            if hashlib.sha256(path.read_bytes()).hexdigest() != checksum.lower():
                raise ValueError(f'Artwork checksum mismatch: {game["Name"]}')
            if path.name not in archive.namelist():
                archive.write(path, path.name)
    base = RELEASE_ROOT + version + "/"
    manifest = {"SchemaVersion": catalog["SchemaVersion"], "Version": version, "CatalogUrl": base + "catalog.json",
                                   "Sha256": hashlib.sha256((output / "catalog.json").read_bytes()).hexdigest(),
                                   "ArtworkUrl": base + "artwork.zip",
                                   "ArtworkSha256": hashlib.sha256((output / "artwork.zip").read_bytes()).hexdigest()}
    if minimum is not None:
        manifest["MinimumClientVersion"] = minimum
    save(output / "manifest.json", manifest)
    args.output = str(output / "coverage.json")
    report(args)


def check_order(args):
    incoming = load(args.catalog)
    previous = load(args.previous)
    validate(incoming)
    validate(previous)
    if not immutable_version(incoming["Version"]) or not immutable_version(previous["Version"]):
        raise ValueError("Invalid immutable catalog version")
    def publication_key(snapshot):
        timestamp = snapshot["PublishedAt"]
        # datetime keeps microseconds. Match the client's final 100 ns digit.
        fractional_ticks = re.search(r"\.([0-9]{7})(?:Z|\+00:00)$", timestamp)
        return parsedate(timestamp), int(fractional_ticks[1][-1]) if fractional_ticks else 0

    incoming_time = publication_key(incoming)
    previous_time = publication_key(previous)
    manifest_path = getattr(args, "previous_manifest", None)
    if manifest_path:
        manifest = load(manifest_path)
        version = previous["Version"]
        checksum = hashlib.sha256(Path(args.previous).read_bytes()).hexdigest()
        if (not immutable_version(version) or manifest.get("Version", version) != version
                or manifest.get("CatalogUrl") != RELEASE_ROOT + version + "/catalog.json"
                or manifest.get("SchemaVersion") != previous["SchemaVersion"]
                or manifest.get("Sha256", "").lower() != checksum):
            raise ValueError("Current manifest does not bind its immutable snapshot")
    if getattr(args, "legacy_transition", False):
        if previous["SchemaVersion"] != 1 or incoming["SchemaVersion"] != 2:
            raise ValueError("The legacy transition must advance from schema 1 to schema 2")
        if incoming_time <= previous_time:
            raise ValueError("The first schema-2 snapshot must be newer than the pinned legacy snapshot")
    elif incoming["SchemaVersion"] != previous["SchemaVersion"]:
        raise ValueError("Catalog channel schema cannot change")
    if incoming["Version"] == previous["Version"]:
        raise ValueError("Catalog version already exists")
    if (incoming_time, incoming["Version"]) <= (previous_time, previous["Version"]):
        raise ValueError("Catalog channel must advance")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["candidates", "enrich", "prices", "report", "package", "check_order"])
    parser.add_argument("--catalog", default=str(ROOT / "Data/catalog.json"))
    parser.add_argument("--review", default=str(ROOT / "Data/catalog-review.json"))
    parser.add_argument("--artwork", default=str(ROOT / "Data/Artwork"))
    parser.add_argument("--cache", default=str(ROOT / "Data/IgdbCache"))
    parser.add_argument("--refresh-igdb", action="store_true", help="Explicitly refetch reviewed metadata instead of using the saved IGDB cache")
    parser.add_argument("--steam-identities", action="store_true", help="Opt into schema 2 for enrichment or offline packaging")
    parser.add_argument("--minimum-client-version", help="Minimum app release version for a schema-2 package")
    parser.add_argument("--previous", help="Current immutable channel snapshot for check_order")
    parser.add_argument("--previous-manifest", help="Validate the current pointer against --previous")
    parser.add_argument("--legacy-transition", action="store_true", help="Check the first schema-2 version against schema 1")
    parser.add_argument("--output")
    args = parser.parse_args()
    if args.command == "check_order" and not args.previous:
        parser.error("check_order requires --previous")
    if args.command != "check_order" and not args.output:
        parser.error("--output is required")
    try:
        globals()[args.command](args)
    except (ValueError, KeyError, OSError, RuntimeError) as error:
        parser.exit(1, f"Catalog tool failed: {error}\n")


if __name__ == "__main__":
    main()
