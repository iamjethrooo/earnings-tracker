"""Resolve unambiguous cached identities and prepare initial price-provider mappings."""
import argparse
import re
import unicodedata
from pathlib import Path
import catalog


# Explicit expansions preserve editions and years; ambiguous short names are omitted.
EXPANSIONS = {
    "Ghost of Tsushima": "Ghost of Tsushima: Director's Cut",
    "Legends of IdleOn": "IdleOn: The Idle RPG",
    "Headliners": "The Headliners",
    "Fate Trigger: The Novita": "Fate Trigger",
    "RotMG Exalt": "Realm of the Mad God",
    "Rivals 2": "Rivals of Aether II",
    "Pixel Gun 3D": "Pixel Gun 3D: PC Edition",
    "Bonerooms": "The Bonerooms",
    "Flashing Lights": "Flashing Lights: Police Fire EMS",
    "Crime Cleaner": "Crime Scene Cleaner",
    "Asphalt 9": "Asphalt Legends",
    "The Planet Crafter": "Planet Crafter",
    "Total War: NAPOLEON": "Napoleon: Total War",
    "Resident Evil 0 / Biohazard 0": "Resident Evil 0",
    "MovieStar Planet": "MovieStarPlanet",
    "Vampire Crawlers": "Vampire Crawlers: The Turbo Wildcard from Vampire Survivors",
    "Mullet Mad Jack": "Mullet Madjack",
    "Cat Quest 2": "Cat Quest II",
    "TWD Final": "The Walking Dead: The Final Season",
    "Fallen Order": "Star Wars Jedi: Fallen Order",
    "MGSV Phantom Pain": "Metal Gear Solid V: The Phantom Pain",
    "Danganronpa Trigger Happy Havoc": "Danganronpa: Trigger Happy Havoc",
    "Danganronpa Killing Harmony": "Danganronpa V3: Killing Harmony",
    "Car Mechanic Sim 18'": "Car Mechanic Simulator 2018",
    "TABS": "Totally Accurate Battle Simulator",
    "ESO": "The Elder Scrolls Online",
    "GR Breakpoint": "Tom Clancy's Ghost Recon: Breakpoint",
    "Tabletop Sim": "Tabletop Simulator",
    "MTG Arena": "Magic: The Gathering Arena",
    "Arkham Knight": "Batman: Arkham Knight",
    "Metal Gear Rising": "Metal Gear Rising: Revengeance",
    "Dale & Dawson SS": "Dale & Dawson Stationery Supplies",
    "Shadow of War": "Middle-earth: Shadow of War",
    "SWTOR": "Star Wars: The Old Republic",
    "TH: Undergound 2": "Tony Hawk's Underground 2",
    "WWZ": "World War Z",
    "ConSim": "Construction Simulator",
    "Europa IV": "Europa Universalis IV",
    "MK11": "Mortal Kombat 11",
    "NFS Payback": "Need for Speed: Payback",
    "FS 19": "Farming Simulator 19",
    "NASCAR 2003": "NASCAR Racing 2003 Season",
    "Spider Man Remastered": "Marvel's Spider-Man Remastered",
    "Spider-Man Miles Morales": "Marvel's Spider-Man: Miles Morales",
    "Crusader Kings 3": "Crusader Kings III",
    "Age of Empires 4": "Age of Empires IV",
    "Civilization 6": "Sid Meier's Civilization VI",
    "Diablo 2 Resurrected": "Diablo II: Resurrected",
    "Diablo 3": "Diablo III",
    "Diablo 4": "Diablo IV",
    "Football Manager 26": "Football Manager 26",
    "Dying Light 2": "Dying Light 2: Stay Human",
    "Stormworks": "Stormworks: Build and Rescue",
    "Ghost Recon Wildlands": "Tom Clancy's Ghost Recon: Wildlands",
    "Sandstorm": "Insurgency: Sandstorm",
    "Friday the 13th": "Friday the 13th: The Game",
    "Clone Drone": "Clone Drone in the Danger Zone",
    "Getting Over It": "Getting Over It with Bennett Foddy",
    "Assassin's Creed IV": "Assassin's Creed IV: Black Flag",
    "The Division": "Tom Clancy's The Division",
    "RaceRoom Racing": "RaceRoom Racing Experience",
    "Texas Chainsaw Massacre": "The Texas Chain Saw Massacre",
    "CarX Street PC": "CarX Street",
    "Kerbal Space": "Kerbal Space Program",
    "Lobotomy": "Lobotomy Corporation",
    "The Witcher 3": "The Witcher 3: Wild Hunt",
    "Ace Combat 7": "Ace Combat 7: Skies Unknown",
    "Taxi Life": "Taxi Life: A City Driving Simulator",
    "Starcraft 2": "StarCraft II: Wings of Liberty",
    "Hatsune Miku Project DIVA Mega Mix": "Hatsune Miku: Project DIVA Mega Mix+",
}


def normalized(name):
    name = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode().casefold()
    name = name.replace("&", " and ")
    # Apostrophes are typographical; other punctuation separates words.
    name = name.replace("'", "")
    return re.sub(r"\s+", " ", re.sub(r"[^a-z0-9]+", " ", name)).strip()


def select_match(name, candidates):
    target = normalized(EXPANSIONS.get(name, name))
    matches = [g for g in candidates if normalized(g["name"]) == target
               and g.get("game_type") not in {1, 2, 4, 6, 7, 13, 14}
               and any(p.get("id") in {6, 82, 161, 163, 385} for p in g.get("platforms", []))]
    if any(g.get("game_type") == 0 for g in matches):
        # A separate PC port record or storefront bundle is not a different base game.
        matches = [g for g in matches if g.get("game_type") not in {3, 11}]
    unique = {g["id"]: g for g in matches}
    return next(iter(unique.values())) if len(unique) == 1 else None


def match(args):
    snapshot = catalog.load(args.catalog)
    candidates = catalog.load(args.candidates)
    review = catalog.load(args.review)
    unresolved = []
    for entry in snapshot["Games"]:
        if catalog.is_archived(entry) and not entry.get("Reviewed"):
            continue
        choices = candidates.get(entry["CatalogId"], {}).get("Candidates", [])
        existing = review["Mappings"].get(entry["CatalogId"])
        if existing and existing.get("Reviewed") and existing.get("ManualOverride"):
            continue
        selected = select_match(entry["Name"], choices)
        if selected:
            mapping = existing if existing and existing.get("IgdbId") == selected["id"] else {}
            mapping.update({
                "Reviewed": True, "IgdbId": selected["id"], "IgdbTitle": selected["name"],
                "MatchEvidence": "Unique exact normalized PC/browser title" if entry["Name"] not in EXPANSIONS
                    else "Explicit title expansion; unique exact PC/browser title",
                "OriginalName": entry["Name"], "ReviewedAt": catalog.now()
            })
            review["Mappings"][entry["CatalogId"]] = mapping
        else:
            review["Mappings"].pop(entry["CatalogId"], None)
            unresolved.append({"CatalogId": entry["CatalogId"], "Name": entry["Name"],
                               "SearchTitle": EXPANSIONS.get(entry["Name"], entry["Name"]),
                               "Candidates": [{"Id": g["id"], "Name": g["name"], "Platforms": [p["name"] for p in g.get("platforms", [])],
                                               "Type": g.get("game_type")} for g in choices]})
    catalog.save(args.review, review)
    catalog.save(args.output, unresolved)
    print(f'{len(review["Mappings"])} reviewed mappings; {len(unresolved)} unresolved')


def lookup_prices(args):
    review = catalog.load(args.review)
    titles = sorted({m["IgdbTitle"] for m in review["Mappings"].values() if m.get("Reviewed") and m.get("IgdbTitle")})
    cache = catalog.load(args.output) if Path(args.output).exists() else {}
    missing = [t for t in titles if t not in cache]
    for start in range(0, len(missing), 100):
        cache.update(catalog.request("https://api.isthereanydeal.com/lookup/id/title/v1", missing[start:start + 100],
                                     {"Content-Type": "application/json"}))
        catalog.save(args.output, cache)
    for mapping in review["Mappings"].values():
        if mapping.get("ManualOverride") or mapping.get("ItadId"):
            continue
        identity = cache.get(mapping.get("IgdbTitle"))
        if identity:
            mapping["ItadId"] = identity
            mapping["PriceMatchEvidence"] = "IsThereAnyDeal precise-title lookup of reviewed IGDB title"
    catalog.save(args.review, review)
    print(f'{sum(bool(m.get("ItadId")) for m in review["Mappings"].values())} price mappings')


def research(args):
    """Fetch expanded searches only; don't repeat completed original searches."""
    snapshot = catalog.load(args.catalog)
    candidates = catalog.load(args.candidates)
    headers = None
    for entry in snapshot["Games"]:
        if catalog.is_archived(entry):
            continue
        title = EXPANSIONS.get(entry["Name"], entry["Name"])
        data = candidates.setdefault(entry["CatalogId"], {"Name": entry["Name"], "Candidates": []})
        if (data.get("FilteredSearch") == title and data.get("SearchEncodingVersion") == 2) or select_match(entry["Name"], data["Candidates"]):
            continue
        if headers is None:
            headers = catalog.igdb_headers()
        import json
        matches = catalog.request("https://api.igdb.com/v4/games",
                                  f'search {json.dumps(title, ensure_ascii=False)}; fields name,slug,summary,genres.name,cover.image_id,platforms.name,game_type,first_release_date,version_parent; where game_type = (0,3,5,8,9,10,11,12); limit 30;', headers)
        for item in matches:
            catalog.save(Path(args.cache) / (str(item["id"]) + ".json"), {"FetchedAt": catalog.now(), "Game": item})
        by_id = {g["id"]: g for g in data["Candidates"] + matches}
        data["Candidates"] = list(by_id.values())
        data["FilteredSearch"] = title
        data["SearchEncodingVersion"] = 2
        catalog.save(args.candidates, candidates)
        catalog.time.sleep(0.3)
    print("Expanded searches cached")


def sources(args):
    """Cache external store identities and use precise Steam IDs to resolve title collisions."""
    candidates = catalog.load(args.candidates)
    review = catalog.load(args.review)
    snapshot = catalog.load(args.catalog)
    ids = {m["IgdbId"] for m in review["Mappings"].values()}
    choices_by_entry = {}
    for entry in snapshot["Games"]:
        target = normalized(EXPANSIONS.get(entry["Name"], entry["Name"]))
        choices = [g for g in candidates[entry["CatalogId"]]["Candidates"] if normalized(g["name"]) == target]
        choices_by_entry[entry["CatalogId"]] = choices
        ids.update(g["id"] for g in choices)
    missing = [identity for identity in sorted(ids) if not catalog.load(Path(args.cache) / (str(identity) + ".json")).get("SourcesFetched")]
    headers = catalog.igdb_headers() if missing else None
    for start in range(0, len(missing), 300):
        batch = missing[start:start + 300]
        rows = catalog.request("https://api.igdb.com/v4/games", "fields external_games.uid,external_games.url,external_games.external_game_source.name,external_games.platform; where id = (" + ",".join(map(str, batch)) + "); limit 500;", headers)
        for row in rows:
            path = Path(args.cache) / (str(row["id"]) + ".json")
            cached = catalog.load(path)
            cached["Game"]["external_games"] = row.get("external_games", [])
            cached["SourcesFetched"] = True
            catalog.save(path, cached)
    titles = sorted({g["name"] for choices in choices_by_entry.values() for g in choices})
    title_cache_path = catalog.ROOT / "Data/itad-title-cache.json"
    title_cache = catalog.load(title_cache_path) if title_cache_path.exists() else {}
    missing_titles = [t for t in titles if t not in title_cache]
    for start in range(0, len(missing_titles), 100):
        title_cache.update(catalog.request("https://api.isthereanydeal.com/lookup/id/title/v1", missing_titles[start:start + 100], {"Content-Type": "application/json"}))
        catalog.save(title_cache_path, title_cache)
    provider_ids = sorted({v for v in title_cache.values() if v})
    shop_cache_path = catalog.ROOT / "Data/itad-steam-cache.json"
    shop_cache = catalog.load(shop_cache_path) if shop_cache_path.exists() else {}
    missing_shop = [identity for identity in provider_ids if identity not in shop_cache]
    for start in range(0, len(missing_shop), 100):
        shop_cache.update(catalog.request("https://api.isthereanydeal.com/lookup/shop/61/id/v1", missing_shop[start:start + 100], {"Content-Type": "application/json"}))
        catalog.save(shop_cache_path, shop_cache)
    resolved = 0
    for entry in snapshot["Games"]:
        if entry["CatalogId"] in review["Mappings"]:
            continue
        possible = []
        for choice in choices_by_entry[entry["CatalogId"]]:
            cached = catalog.load(Path(args.cache) / (str(choice["id"]) + ".json"))
            steam_ids = {"app/" + x["uid"] for x in cached["Game"].get("external_games", [])
                         if x.get("external_game_source", {}).get("name", "").casefold() == "steam" and x.get("uid", "").isdigit()}
            itad_id = title_cache.get(choice["name"])
            overlap = steam_ids.intersection(shop_cache.get(itad_id) or [])
            if overlap:
                possible.append((choice, itad_id, sorted(overlap)))
        if len(possible) != 1:
            continue
        selected, itad_id, steam_ids = possible[0]
        review["Mappings"][entry["CatalogId"]] = {"Reviewed": True, "ManualOverride": True,
            "IgdbId": selected["id"], "IgdbTitle": selected["name"], "ItadId": itad_id,
            "MatchEvidence": "Precise ITAD title lookup and unique shared Steam product ID in IGDB",
            "SteamIds": steam_ids, "OriginalName": entry["Name"], "ReviewedAt": catalog.now()}
        resolved += 1
    catalog.save(args.review, review)
    print(f"Cached store identities; resolved {resolved} collisions by shared Steam ID")


def free_games(args):
    """Verify permanent free-to-play against the publisher's Steam listing, not a zero-price deal."""
    import urllib.parse
    snapshot = catalog.load(args.catalog)
    review = catalog.load(args.review)
    shop_path = catalog.ROOT / "Data/itad-steam-cache.json"
    shops = catalog.load(shop_path) if shop_path.exists() else {}
    cache_path = catalog.ROOT / "Data/steam-free-cache.json"
    cache = catalog.load(cache_path) if cache_path.exists() else {}
    checked = 0
    for game in snapshot["Games"]:
        if not game.get("Reviewed"):
            continue
        mapping = review["Mappings"].get(game["CatalogId"], {})
        steam_products = shops.get(game.get("ItadId")) or []
        if any(o.get("SalePrice") == 0 or o.get("RegularPrice") == 0 for o in game.get("Offers", [])):
            pass
        elif game.get("Offers"):
            continue
        native_ids = {x["uid"] for x in catalog.load(Path(args.cache) / (str(game["IgdbId"]) + ".json"))["Game"].get("external_games", [])
                      if x.get("external_game_source", {}).get("name", "").casefold() == "steam" and x.get("uid", "").isdigit()}
        products = native_ids.intersection({p.removeprefix("app/") for p in steam_products if p.startswith("app/")}) if steam_products else native_ids
        if len(products) != 1:
            continue
        identity = next(iter(products))
        if identity not in cache:
            response = catalog.request("https://store.steampowered.com/api/appdetails?" + urllib.parse.urlencode({"appids": identity, "cc": "ph", "l": "english"}))
            row = response.get(identity, {})
            data = row.get("data", {})
            cache[identity] = {"CheckedAt": catalog.now(), "Success": row.get("success", False),
                               "Name": data.get("name"), "IsFree": data.get("is_free", False),
                               "Windows": data.get("platforms", {}).get("windows", False), "Type": data.get("type")}
            catalog.save(cache_path, cache)
            catalog.time.sleep(0.3)
        verified = cache[identity]
        if not (verified["Success"] and verified["IsFree"] and verified["Windows"] and verified["Type"] == "game"):
            continue
        offer = {"Source": "Steam", "Url": "https://store.steampowered.com/app/" + identity + "/", "Edition": "Base game",
                 "Region": "PH", "Currency": "PHP", "RegularPrice": 0, "SalePrice": None,
                 "Kind": "FreeToPlay", "VerifiedAt": verified["CheckedAt"], "ExpiresAt": None,
                 "Provenance": "Official Steam listing (Philippines): is_free, Windows, game"}
        game["Offers"] = [o for o in game.get("Offers", []) if not (o["Source"] == "Steam" and o.get("Provenance", "").startswith("Official Steam listing"))] + [offer]
        checked += 1
    catalog.stamp(snapshot)
    catalog.save(args.output, snapshot)
    print(f"Verified {checked} permanent free-to-play entries against Steam")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["match", "lookup-prices", "research", "sources", "free-games"])
    parser.add_argument("--catalog", default=str(catalog.ROOT / "Data/catalog.json"))
    parser.add_argument("--review", default=str(catalog.ROOT / "Data/catalog-review.json"))
    parser.add_argument("--candidates", default=str(catalog.ROOT / "Data/igdb-candidates.json"))
    parser.add_argument("--cache", default=str(catalog.ROOT / "Data/IgdbCache"))
    parser.add_argument("--output", required=True)
    options = parser.parse_args()
    {"match": match, "lookup-prices": lookup_prices, "research": research, "sources": sources, "free-games": free_games}[options.command](options)
