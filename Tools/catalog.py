"""Reviewed catalog preparation. Python 3.11+, standard library only; no secrets in output."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]


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
    processed = 0
    for game in catalog["Games"]:
        mapping = mappings.get(game["CatalogId"])
        if not mapping or not mapping.get("Reviewed"):
            continue
        if mapping.get("ManualMetadata") is not None:
            # Provider-absent prologues and platform experiences keep explicit, reviewed official metadata.
            metadata = mapping["ManualMetadata"]
            if not mapping.get("MatchEvidence") or not metadata.get("Provenance"):
                raise ValueError("Manual metadata requires identity evidence and provenance")
            game.update({"IgdbId": mapping.get("IgdbId"), "Reviewed": True,
                "ItadId": mapping.get("ItadId"), "Aliases": mapping.get("Aliases", []),
                "Summary": metadata.get("Summary", ""), "Genres": metadata.get("Genres", []),
                "VerifiedAt": mapping["ReviewedAt"], "Provenance": metadata["Provenance"]})
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
        if processed % 25 == 0:
            save(args.output, catalog)
            print(f"Enriched {processed} titles", flush=True)
    stamp(catalog)
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


def validate(catalog):
    if catalog.get("SchemaVersion") != 1 or not catalog.get("Version") or not catalog.get("PublishedAt"):
        raise ValueError("Invalid catalog header")
    parsedate(catalog["PublishedAt"])
    ids, names = set(), set()
    for game in catalog["Games"]:
        identity = game["CatalogId"]
        if not identity or identity in ids:
            raise ValueError("Duplicate catalog identity")
        ids.add(identity)
        occurrence = game.get("NameOccurrence")
        if occurrence is not None and (not 1 <= occurrence <= game.get("ExpectedNameCount", 0) or game.get("Aliases")):
            raise ValueError("Invalid duplicate occurrence mapping")
        for name in [game["Name"], *game.get("Aliases", [])]:
            identity_name = (name.casefold(), occurrence if name == game["Name"] else None)
            if not name.strip() or identity_name in names:
                raise ValueError("Missing or ambiguous name/alias")
            names.add(identity_name)
        enriched = game.get("CoverUrl") or game.get("Genres") or game.get("Offers") or game.get("Subscriptions")
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
    save(args.output, result)
    print(f'{result["Reviewed"]}/{result["Total"]} reviewed; {len(result["Unresolved"])} unresolved')


def package(args):
    catalog = load(args.catalog)
    validate(catalog)
    review = load(args.review)
    if not review.get("ReleaseApproved"):
        raise ValueError("Set ReleaseApproved only after reviewing coverage and provider permissions")
    from collections import Counter
    seed_names = Counter(g["Name"] for g in load(ROOT / "Data/games.json"))
    if seed_names != Counter(g["Name"] for g in catalog["Games"]):
        raise ValueError("Catalog must account for every seeded game")
    if any(g.get("IgdbId") for g in catalog["Games"]) and not review.get("IgdbRedistributionApproved"):
        raise ValueError("IGDB redistribution permission is not approved")
    if any(o.get("Provenance") == "IsThereAnyDeal" for g in catalog["Games"] for o in g.get("Offers", [])) and not review.get("PriceRedistributionApproved"):
        raise ValueError("Price redistribution permission is not approved")
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    save(output / "catalog.json", catalog)
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
    base = "https://github.com/iamjethrooo/earnings-tracker/releases/download/" + catalog["Version"] + "/"
    save(output / "manifest.json", {"SchemaVersion": 1, "CatalogUrl": base + "catalog.json",
                                   "Sha256": hashlib.sha256((output / "catalog.json").read_bytes()).hexdigest(),
                                   "ArtworkUrl": base + "artwork.zip",
                                   "ArtworkSha256": hashlib.sha256((output / "artwork.zip").read_bytes()).hexdigest()})
    args.output = str(output / "coverage.json")
    report(args)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["candidates", "enrich", "prices", "report", "package"])
    parser.add_argument("--catalog", default=str(ROOT / "Data/catalog.json"))
    parser.add_argument("--review", default=str(ROOT / "Data/catalog-review.json"))
    parser.add_argument("--artwork", default=str(ROOT / "Data/Artwork"))
    parser.add_argument("--cache", default=str(ROOT / "Data/IgdbCache"))
    parser.add_argument("--refresh-igdb", action="store_true", help="Explicitly refetch reviewed metadata instead of using the saved IGDB cache")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        globals()[args.command](args)
    except (ValueError, KeyError, OSError, RuntimeError) as error:
        parser.exit(1, f"Catalog tool failed: {error}\n")


if __name__ == "__main__":
    main()
