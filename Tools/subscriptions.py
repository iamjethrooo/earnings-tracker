"""Collect official subscription catalogs, propose changes, and apply reviewed decisions.

No account or provider keys are required. Collection never modifies the bundled catalog.
"""
import argparse
import hashlib
import html
import json
from pathlib import Path
import re
import time
import unicodedata
import urllib.parse
import urllib.request
import urllib.error
import catalog

SERVICES = ["PC Game Pass"]
XBOX_PAGE = "https://www.xbox.com/en-PH/xbox-game-pass/games"


def fetch(url, payload=None, headers=None, text=False):
    if urllib.parse.urlparse(url).scheme != "https":
        raise ValueError("HTTPS is required")
    data = json.dumps(payload).encode() if payload is not None else None
    request = urllib.request.Request(url, data=data, headers={"User-Agent": "Mozilla/5.0 NerveCatalog/1.0", **(headers or {})})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read(16 * 1024 * 1024 + 1)
            if len(raw) > 16 * 1024 * 1024:
                raise ValueError("Provider response exceeds limit")
            content = raw.decode("utf-8")
            return content if text else json.loads(content)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"Provider HTTP {error.code}; previous snapshot retained") from None
    except urllib.error.URLError:
        raise RuntimeError("Provider unavailable; previous snapshot retained") from None


def normalized(title):
    title = html.unescape(title).replace("™", "").replace("®", "").replace("©", "")
    title = unicodedata.normalize("NFKD", title).encode("ascii", "ignore").decode().casefold()
    title = title.replace("&", " and ").replace("'", "").replace("’", "")
    # Platform labels are not editions. Preserve years, remaster and edition names.
    title = re.sub(r"\s*\((?:pc|windows|windows pc)\)\s*$", "", title)
    return re.sub(r"\s+", " ", re.sub(r"[^a-z0-9]+", " ", title)).strip()


def provider_snapshot(service, url, products, complete, region_confirmed=False, evidence=""):
    if not products:
        raise ValueError("Empty catalog is not evidence of removals")
    return {"Service": service, "SourceUrl": url, "FetchedAt": catalog.now(), "Complete": complete,
            "Region": "PH" if region_confirmed else "Unverified", "RegionConfirmed": region_confirmed,
            "RegionEvidence": evidence, "Platform": "PC", "Products": products}


def windows_product(product):
    return any(platform.get("PlatformName") == "Windows.Desktop"
               for sku in product.get("DisplaySkuAvailabilities", []) for availability in sku.get("Availabilities", [])
               for platform in availability.get("Conditions", {}).get("ClientConditions", {}).get("AllowedPlatforms", []))


def active_windows_availability(availability, now):
    conditions = availability.get("Conditions", {})
    platforms = conditions.get("ClientConditions", {}).get("AllowedPlatforms", [])
    return (any(p.get("PlatformName") == "Windows.Desktop" for p in platforms)
            and catalog.parsedate(conditions.get("StartDate", "9999-01-01T00:00:00Z")) <= now
            and catalog.parsedate(conditions.get("EndDate", "0001-01-01T00:00:00Z")) > now)


def full_pc_entitlement(product, plan_id, now):
    if product.get("Properties", {}).get("IsDemo"): return None
    matching = []
    for sku in product.get("DisplaySkuAvailabilities", []):
        properties = sku.get("Sku", {}).get("Properties", {})
        if properties.get("IsTrial") or properties.get("IsPreOrder"): continue
        for availability in sku.get("Availabilities", []):
            price = availability.get("OrderManagementData", {}).get("Price", {})
            if not active_windows_availability(availability, now) or "Purchase" not in availability.get("Actions", []): continue
            # Free games/perks do not need membership and are not subscription acquisitions.
            if not availability.get("RemediationRequired") and price.get("MSRP") == 0 and price.get("ListPrice") == 0:
                return None
            if price.get("ListPrice") == 0 and any(r.get("BigId") == plan_id for r in availability.get("Remediations", [])):
                matching.append(availability["Conditions"]["EndDate"])
    return max(matching, key=catalog.parsedate) if matching else None


def xbox(fetcher=fetch):
    page = fetcher(XBOX_PAGE, text=True)
    scripts = re.findall(r'<script[^>]+src=["\']([^"\']+)', page)
    script = next((s for s in scripts if "xgpcatPopulate" in s), None)
    if not script:
        raise ValueError("Xbox catalog script changed; review adapter")
    script_url = urllib.parse.urljoin(XBOX_PAGE, script)
    if urllib.parse.urlparse(script_url).hostname != "www.xbox.com":
        raise ValueError("Unexpected Xbox script host")
    source = fetcher(script_url, text=True)
    identity = re.search(r'["\']pcgaVTaz["\']\s*:\s*["\']([a-f0-9-]{36})["\']', source)
    if not identity:
        raise ValueError("Xbox PC collection identifier changed")
    url = "https://catalog.gamepass.com/sigls/v2?" + urllib.parse.urlencode({"id": identity[1], "language": "en-ph", "market": "PH"})
    listing = fetcher(url)
    ids = sorted({x["id"] for x in listing if isinstance(x, dict) and re.fullmatch(r"[A-Z0-9]{12}", x.get("id", ""))})
    if len(ids) < 20:
        raise ValueError("Unexpectedly small Xbox PC collection; no removals proposed")
    products = []
    received = set()
    now = catalog.dt.datetime.now(catalog.dt.timezone.utc)
    # Resolve the plan's title rather than assuming a marketplace ID means PC Game Pass.
    plan = fetcher("https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=CFQ7TTC0KGQ8&market=PH&languages=en-PH")
    if not any(p["ProductId"] == "CFQ7TTC0KGQ8" and p["LocalizedProperties"][0]["ProductTitle"] == "PC Game Pass" for p in plan.get("Products", [])):
        raise ValueError("PC Game Pass plan changed; review adapter")
    for start in range(0, len(ids), 50):
        details = fetcher("https://displaycatalog.mp.microsoft.com/v7.0/products?" + urllib.parse.urlencode(
            {"bigIds": ",".join(ids[start:start + 50]), "market": "PH", "languages": "en-PH"}))
        for p in details.get("Products", []):
            received.add(p["ProductId"])
            localized = p.get("LocalizedProperties", [])
            expiry = full_pc_entitlement(p, "CFQ7TTC0KGQ8", now)
            if localized and windows_product(p) and expiry and p.get("ProductType") == "Game":
                products.append({"Id": p["ProductId"], "Title": localized[0]["ProductTitle"],
                    "Url": "https://www.xbox.com/en-PH/games/store/-/" + p["ProductId"], "Platform": "PC",
                    "ExpiresAt": catalog.parsedate(expiry).isoformat().replace("+00:00", "Z")})
        time.sleep(0.15)
    return provider_snapshot("PC Game Pass", XBOX_PAGE, products, received == set(ids), True,
        "Official PC collection and full-game PC Game Pass entitlement requested with market=PH; active Windows.Desktop SKU, no trials/preorders/free perks")


def candidate_id(service, product_id, catalog_id, action):
    return hashlib.sha256(json.dumps([service, product_id, catalog_id, action]).encode()).hexdigest()[:24]


def propose(snapshot, mappings, sources):
    games = [g for g in snapshot["Games"] if g.get("Reviewed") and not catalog.is_archived(g)]
    names = {}
    for game in games:
        mapping = mappings.get(game["CatalogId"], {})
        for title in [game["Name"], mapping.get("IgdbTitle", ""), *game.get("Aliases", [])]:
            if title: names.setdefault(normalized(title), {})[game["CatalogId"]] = game
    candidates = []; unmatched = []; ambiguous = []
    for source in sources:
        if source.get("Failed"): continue
        if source["Service"] not in SERVICES: continue
        seen = set(); products = source["Products"]
        for product in products:
            matches = list(names.get(normalized(product["Title"]), {}).values())
            if not matches:
                unmatched.append({"Service": source["Service"], "Title": product["Title"], "ProviderId": product["Id"]}); continue
            if len(matches) != 1:
                ambiguous.append({"Service": source["Service"], "Title": product["Title"], "CatalogIds": [g["CatalogId"] for g in matches]}); continue
            game = matches[0]; seen.add(game["CatalogId"])
            previous = next((s for s in game.get("Subscriptions", []) if s["Service"] == source["Service"] and s.get("Included")), None)
            action = "Reverify" if previous else "Added"
            candidates.append({"CandidateId": candidate_id(source["Service"], product["Id"], game["CatalogId"], action),
                "CatalogId": game["CatalogId"], "Name": game["Name"], "ProviderTitle": product["Title"], "ProviderId": product["Id"],
                "Service": source["Service"], "Action": action, "Url": product["Url"], "SourceUrl": source["SourceUrl"],
                "FetchedAt": source["FetchedAt"], "RegionConfirmed": source["RegionConfirmed"], "RegionEvidence": source["RegionEvidence"],
                "Edition": product.get("Edition", ""), "ExpiresAt": product.get("ExpiresAt"), "Approved": False})
        if source["Complete"] and source["RegionConfirmed"]:
            provider_ids = {p["Id"] for p in products}
            for game in games:
                previous = next((s for s in game.get("Subscriptions", []) if s["Service"] == source["Service"]
                    and s.get("Included") and s.get("Provenance", "").startswith("Official subscription catalog")), None)
                previous_id = re.search(r"(?:^|; )id:([^;]+)", previous.get("Provenance", "")) if previous else None
                if previous and game["CatalogId"] not in seen and not (previous_id and previous_id[1] in provider_ids):
                    candidates.append({"CandidateId": candidate_id(source["Service"], "removed", game["CatalogId"], "Removed"),
                        "CatalogId": game["CatalogId"], "Name": game["Name"], "Service": source["Service"], "Action": "Removed",
                        "ProviderTitle": "", "ProviderId": "removed", "Url": previous["Url"], "FetchedAt": source["FetchedAt"],
                        "SourceUrl": source["SourceUrl"], "RegionConfirmed": True, "RegionEvidence": source["RegionEvidence"], "Approved": False})
    competing = {}
    for candidate in candidates:
        competing.setdefault((candidate["CatalogId"], candidate["Service"]), []).append(candidate)
    rejected = set()
    for (game_id, service), matches in competing.items():
        if len({c["ProviderId"] for c in matches}) > 1:
            ambiguous.append({"Service": service, "CatalogIds": [game_id],
                "ProviderIds": [c["ProviderId"] for c in matches], "Reason": "Multiple provider editions require review"})
            rejected.update(c["CandidateId"] for c in matches)
    candidates = list({c["CandidateId"]: c for c in candidates if c["CandidateId"] not in rejected}.values())
    return {"SchemaVersion": 1, "CollectedAt": catalog.now(), "Sources": sources, "Candidates": candidates,
            "Ambiguous": ambiguous, "Unmatched": unmatched}


def collection_hash(collection):
    return hashlib.sha256(json.dumps(collection, sort_keys=True, ensure_ascii=False).encode()).hexdigest()


def fresh(timestamp):
    checked = catalog.parsedate(timestamp)
    now = catalog.dt.datetime.now(catalog.dt.timezone.utc)
    return now - catalog.dt.timedelta(days=7) <= checked <= now + catalog.dt.timedelta(minutes=5)


def report(args):
    collection = catalog.load(args.collection)
    decisions = catalog.load(args.decisions) if args.decisions else {"Decisions": []}
    if args.decisions and decisions.get("CollectionHash") != collection_hash(collection):
        raise ValueError("Decisions must reference this exact collection")
    approved = {d["CandidateId"] for d in decisions["Decisions"] if d.get("Approved")}
    lines = ["# Subscription catalog review", "", "Collected at " + collection["CollectedAt"] + " (UTC).",
        "", "Only reviewed Philippine PC availability is included in the app. Pending candidates grant no access.", "",
        "| Service | Observed products | Complete | PH confirmed | Approved matches | Pending matches |",
        "| --- | ---: | --- | --- | ---: | ---: |"]
    for source in collection["Sources"]:
        if source.get("Failed"):
            lines.extend(["", source["Provider"] + ": provider failed; previous data retained; no removals proposed."])
            continue
        matches = [c for c in collection["Candidates"] if c["Service"] == source["Service"]]
        count = sum(c["CandidateId"] in approved for c in matches)
        lines.append(f'| {source["Service"]} | {len(source["Products"])} | {source["Complete"]} | {source["RegionConfirmed"]} | {count} | {len(matches)-count} |')
    lines.extend(["", "## Source evidence", ""])
    for source in collection["Sources"]:
        if source.get("Failed"): continue
        lines.append(f'- [{source["Service"]}]({source["SourceUrl"]}): {source["RegionEvidence"]}.')
        if not source["Complete"]: lines.append("  Partial list: missing titles cannot establish removals.")
        for warning in source.get("Warnings", []): lines.append("  " + warning)
    lines.extend(["", "## Pending identity / regional review", ""])
    for candidate in collection["Candidates"]:
        if candidate["CandidateId"] not in approved:
            lines.append(f'- {candidate["Name"]} — {candidate["Service"]}; {candidate["Action"]}; provider title: {candidate["ProviderTitle"]}.')
    if collection["Ambiguous"]: lines.append(f'\n{len(collection["Ambiguous"])} ambiguous mappings require manual edition review; see collection JSON.')
    Path(args.output).write_text("\n".join(lines) + "\n", encoding="utf-8")


def reverify(args):
    collection = catalog.load(args.collection); snapshot = catalog.load(args.catalog)
    games = {g["CatalogId"]: g for g in snapshot["Games"]}; changed = 0
    for candidate in collection["Candidates"]:
        if candidate["Service"] not in SERVICES: continue
        if candidate["Action"] != "Reverify" or not candidate["RegionConfirmed"]: continue
        if not fresh(candidate["FetchedAt"]): continue
        game = games[candidate["CatalogId"]]
        if not game.get("Reviewed") or catalog.is_archived(game): continue
        row = next((s for s in game.get("Subscriptions", []) if s["Service"] == candidate["Service"] and s.get("Included")
                    and s.get("Provenance", "").startswith("Official subscription catalog; id:" + candidate["ProviderId"] + ";")), None)
        if row is None: continue  # New identities, additions and removals always require review.
        row["VerifiedAt"] = candidate["FetchedAt"]; row["ExpiresAt"] = candidate.get("ExpiresAt"); changed += 1
    if changed: catalog.stamp(snapshot)
    catalog.validate(snapshot); catalog.save(args.output, snapshot)
    print(f"Reverified {changed} previously reviewed, unchanged regional provider IDs; pending changes untouched")


def collect(args):
    sources = []; cache = Path(args.cache); cache.mkdir(parents=True, exist_ok=True)
    for name, collector in [("xbox", xbox)]:
        try:
            result = collector(); result = result if isinstance(result, list) else [result]
            # Commit source snapshots only after a complete adapter run, retaining last good data after failures.
            catalog.save(cache / (name + ".json"), result)
            sources.extend(result)
            print(name, ", ".join(f'{s["Service"]}: {len(s["Products"])} products, complete={s["Complete"]}' for s in result), flush=True)
        except (ValueError, KeyError, StopIteration, RuntimeError, OSError) as error:
            # Error details never include the public search key or request headers.
            sources.append({"Provider": name, "Failed": True, "CheckedAt": catalog.now(), "Error": type(error).__name__,
                "PreviousSnapshotRetained": (cache / (name + ".json")).exists()})
            print(name, "unavailable or changed; previous data retained; no removals proposed", flush=True)
    snapshot = catalog.load(args.catalog); review = catalog.load(args.review)
    collection = propose(snapshot, review["Mappings"], sources)
    catalog.save(args.output, collection)
    catalog.save(Path(args.output).with_suffix(".review.json"), {"CollectionHash": collection_hash(collection), "Decisions": []})
    print(f'{len(collection["Candidates"])} proposals; {len(collection["Ambiguous"])} ambiguous; catalog unchanged')


def apply(args):
    collection = catalog.load(args.collection); review = catalog.load(args.decisions)
    if review.get("CollectionHash") != collection_hash(collection): raise ValueError("Decisions must reference this exact collection")
    snapshot = catalog.load(args.catalog); mappings = catalog.load(args.review)
    games = {g["CatalogId"]: g for g in snapshot["Games"]}
    candidates = {c["CandidateId"]: c for c in collection["Candidates"]}
    changed = 0
    for decision in review["Decisions"]:
        if not decision.get("Approved"): continue
        candidate = candidates[decision["CandidateId"]]; game = games[candidate["CatalogId"]]
        if not game.get("Reviewed") or catalog.is_archived(game): raise ValueError("Only reviewed active games can receive subscription mappings")
        if not fresh(candidate["FetchedAt"]):
            raise ValueError("Collection date is stale or in the future; recollect before approval")
        if candidate["Action"] not in ("Added", "Reverify", "Removed"): raise ValueError("Unknown subscription action")
        if not decision.get("IdentityConfirmed") or not decision.get("EditionConfirmed") or len(decision.get("Evidence", "")) < 10:
            raise ValueError("Identity, edition, and review evidence are required")
        if not candidate["RegionConfirmed"] and (not decision.get("RegionConfirmed") or len(decision.get("RegionEvidence", "")) < 10):
            raise ValueError("PH availability needs explicit regional evidence")
        service = candidate["Service"]
        if service not in SERVICES: raise ValueError("Unknown subscription tier")
        row = {"Service": service, "Platform": "PC", "Region": "PH", "Included": candidate["Action"] != "Removed",
               "VerifiedAt": candidate["FetchedAt"], "Url": candidate["Url"],
               "Provenance": f'Official subscription catalog; id:{candidate["ProviderId"]}; title:{candidate["ProviderTitle"]}; edition:{candidate.get("Edition") or "Reviewed base game"}; {candidate["SourceUrl"]}; ' + decision["Evidence"] + "; PH evidence: " + (decision.get("RegionEvidence") or candidate["RegionEvidence"]),
               "ExpiresAt": candidate.get("ExpiresAt")}
        if row["ExpiresAt"]: catalog.parsedate(row["ExpiresAt"])
        game["Subscriptions"] = [s for s in game.get("Subscriptions", []) if s["Service"] != service] + [row]
        mappings["Mappings"][game["CatalogId"]]["Subscriptions"] = game["Subscriptions"]
        changed += 1
    if not changed: raise ValueError("No approved decisions; catalog unchanged")
    catalog.stamp(snapshot); catalog.validate(snapshot)
    # Output is a reviewable snapshot; personal DB state is updated only by the app's transactional importer.
    catalog.save(args.output, snapshot); catalog.save(args.review, mappings)
    print(f"Applied {changed} reviewed subscription decisions")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["collect", "apply", "reverify", "report"])
    parser.add_argument("--catalog", default=str(catalog.ROOT / "Data/catalog.json"))
    parser.add_argument("--review", default=str(catalog.ROOT / "Data/catalog-review.json"))
    parser.add_argument("--cache", default=str(catalog.ROOT / "Data/SubscriptionCache"))
    parser.add_argument("--collection")
    parser.add_argument("--decisions")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try: {"collect": collect, "apply": apply, "reverify": reverify, "report": report}[args.command](args)
    except (ValueError, KeyError, OSError, RuntimeError) as error: parser.exit(1, f"Subscription import failed: {error}\n")


if __name__ == "__main__": main()
