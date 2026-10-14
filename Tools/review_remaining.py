"""Apply the reviewed remaining identities and owner's edition choices, using cached IGDB metadata."""
import argparse
import html
from pathlib import Path
import re
import time
import urllib.parse
import catalog

# Reviewed against exact IGDB metadata and official storefront identities. Seed names/IDs never change.
IDENTITIES = {
    "Poppy Playtime Chapter 3": (258842, "2555190"),
    "DRAGON BALL GEKISHIN SQUADRA EM": (314178, "2072560"),
    "Call of Duty: Modern Warfare 2 (2009)": (559, "10180"),
    "eFootball PES 2021": (135881, "1259970"),
    "MX Simulator": (21182, None), "Pokémon Infinite Fusion": (143761, None),
    "Nova-Life: Amboise": (104328, "885570"), "Golf It": (30099, "571740"),
    "Coop Deluxe": (307768, None), "Retro Rewind": (346334, "3552140"),
    "Track Day R": (143045, "1511630"), "Cities": (9066, "255710"),
    "Baldi's Basics": (102832, None), "Spaceflight Simulator M": (190824, "1718870"),
    "New World": (24654, "1063730"), "The Planet Crafter": (143574, "1284190"),
    "WRC": (264412, "1849250"), "SWORN": (291948, "1763250"),
    "Bus Simulator 21 Next Stop": (240037, "976590"), "Scooter Flow": (152451, "1648390"),
}
OWNER_CHOICES = {"Cities", "WRC", "Baldi's Basics", "Retro Rewind", "Coop Deluxe", "Spaceflight Simulator M", "SWORN"}
DIRECT = {
    "MX Simulator": "https://mxsimulator.com/order.html",
    "Pokémon Infinite Fusion": "https://www.pokecommunity.com/threads/pok%C3%A9mon-infinite-fusion.347883/",
    "Coop Deluxe": "https://www.sm64coopdx.com/",
    "Baldi's Basics": "https://mystman12.itch.io/baldis-basics",
}
PROLOGUES = {"Fast Food Simulator: Prologue": "3099020", "TCG Card Shop Simulator: Prologue": "3103840"}

def offer(source, url, checked, provenance, kind="Unknown", currency="PHP", regular=None, sale=None):
    return {"Source": source, "Url": url, "Edition": "Reviewed edition", "Region": "PH", "Currency": currency,
        "RegularPrice": regular, "SalePrice": sale, "Kind": kind, "VerifiedAt": checked,
        "ExpiresAt": None, "Provenance": provenance}

def steam(app_id):
    cache = catalog.ROOT / "Data/RemainingCache" / ("steam-" + app_id + ".json")
    if cache.exists(): return catalog.load(cache)
    response = catalog.request("https://store.steampowered.com/api/appdetails?" + urllib.parse.urlencode({"appids": app_id, "cc": "ph", "l": "english"}))
    result = {"VerifiedAt": catalog.now(), "Response": response[app_id]}
    catalog.save(cache, result); time.sleep(0.3)
    return result

def steam_offer(app_id, cached):
    response = cached["Response"]; data = response.get("data", {})
    if response.get("success") and not data.get("platforms", {}).get("windows"):
        raise ValueError("Reviewed Steam product is not Windows compatible")
    price = data.get("price_overview")
    kind = "FreeToPlay" if data.get("is_free") else "Purchase" if price else "Unknown"
    return offer("Steam", "https://store.steampowered.com/app/" + app_id + "/", cached["VerifiedAt"],
        "Official Steam appdetails; cc=ph; exact reviewed app ID " + app_id,
        kind, price["currency"] if price else "PHP", price["initial"]/100 if price else 0 if kind == "FreeToPlay" else None,
        price["final"]/100 if price else None)

def prepare(output):
    snapshot = catalog.load(catalog.ROOT / "Data/catalog.json")
    review = catalog.load(catalog.ROOT / "Data/catalog-review.json")
    report = []
    for game in snapshot["Games"]:
        if game.get("Reviewed") or catalog.is_archived(game): continue
        name = game["Name"]; checked = catalog.now()
        mapping = {"Reviewed": True, "ManualOverride": True, "OriginalName": name, "ReviewedAt": checked}
        if name in IDENTITIES:
            identity, app_id = IDENTITIES[name]
            metadata = catalog.load(catalog.ROOT / "Data/IgdbCache" / (str(identity)+".json"))["Game"]
            mapping.update(IgdbId=identity, IgdbTitle=metadata["name"], MatchEvidence=
                "Reviewed exact identity/platform/edition using cached IGDB and official source" + ("; owner explicitly selected this edition" if name in OWNER_CHOICES else ""))
            if app_id:
                raw = steam(app_id); mapping["Offers"] = [steam_offer(app_id, raw)]
                native = {x["uid"] for x in metadata.get("external_games", []) if x.get("external_game_source", {}).get("name") == "Steam"}
                if app_id not in native and name != "Bus Simulator 21 Next Stop": raise ValueError("Steam identity does not corroborate IGDB")
                if name == "Bus Simulator 21 Next Stop" and "Next Stop" not in raw["Response"].get("data", {}).get("name", ""):
                    raise ValueError("Next Stop storefront identity needs review")
                lookup = catalog.ROOT / "Data/RemainingCache" / ("itad-"+app_id+".json")
                if lookup.exists(): found = catalog.load(lookup)
                else:
                    found = catalog.request("https://api.isthereanydeal.com/lookup/id/shop/61/v1", ["app/"+app_id], {"Content-Type": "application/json"})
                    catalog.save(lookup, found)
                if found.get("app/"+app_id): mapping.update(ItadId=found["app/"+app_id], PriceMatchEvidence="Exact reviewed Steam app ID "+app_id)
            else:
                mapping["Offers"] = [offer("Official website", DIRECT[name], checked, "Reviewed official developer/project link; purchase price unknown")]
                if name == "Coop Deluxe": mapping["AccessNote"] = "PC port/mod requires a compatible Super Mario 64 ROM; not treated as standalone free access"
                if name == "MX Simulator":
                    mapping["ManualMetadata"] = {"Summary": "A motocross racing simulator for Windows and Linux.",
                        "Genres": ["Racing", "Simulator", "Sport"],
                        "CoverUrl": "https://images.igdb.com/igdb/image/upload/t_cover_big/" + metadata["cover"]["image_id"] + ".jpg",
                        "Provenance": "IGDB identity/artwork; genre and platform reviewed against https://mxsimulator.com/index.html and https://mxsimulator.com/order.html"}
        elif name in PROLOGUES:
            app_id = PROLOGUES[name]; cached = steam(app_id); data = cached["Response"].get("data", {})
            if not cached["Response"].get("success") or data.get("name") != name: raise ValueError("Prologue storefront identity not verified")
            mapping.update(IgdbId=None, MatchEvidence="Exact official Steam prologue app; distinct from full game",
                ManualMetadata={"Summary": html.unescape(re.sub("<[^>]+>", "", data.get("short_description", ""))),
                    "Genres": ["Simulator" if g["description"] == "Simulation" else g["description"] for g in data.get("genres", []) if g["description"] != "Free To Play"],
                    "CoverUrl": data["header_image"], "Provenance": "Official Steam metadata; reviewed prologue app " + app_id},
                Offers=[steam_offer(app_id, cached)])
        elif name == "Sound Space +":
            url = "https://www.roblox.com/games/2677609345/Sound-Space-Rhythm-Game"
            mapping.update(IgdbId=None, MatchEvidence="Owner confirmed Roblox Sound Space, not standalone Rhythia/Sound Space Plus",
                ManualMetadata={"Summary": "Sound Space Studio's rhythm experience on Roblox. Optional cosmetics and platform purchases are separate from game access.",
                    "Genres": ["Music"], "Provenance": "Owner-confirmed Roblox experience 2677609345; " + url},
                Offers=[offer("Roblox", url, checked, "Official Roblox experience; storefront access price not verified")])
        elif name == "OPTCG Simulator":
            url = "https://optcgsim.com/"
            mapping.update(IgdbId=None, MatchEvidence="Owner confirmed Batsu's One Piece Card Game simulator; creator's Linktree links this site",
                ManualMetadata={"Summary": "Batsu's unofficial practice simulator for the One Piece Card Game. The developer provides a Windows download.",
                    "Genres": ["Card & Board Game"], "Provenance": "Official creator link https://linktr.ee/maebatsu → " + url},
                Offers=[offer("Official website", url, checked, "Official creator download page; purchase price not verified")])
        else: raise ValueError("No reviewed mapping for " + name)
        review["Mappings"][game["CatalogId"]] = mapping
        catalog.save(catalog.ROOT / "Data/catalog-review.json", review)
        report.append({"CatalogId": game["CatalogId"], "Name": name, "IgdbId": mapping.get("IgdbId"),
            "ResolvedTitle": mapping.get("IgdbTitle", name), "Evidence": mapping["MatchEvidence"], "Sources": mapping.get("Offers", [])})
        print(name, "reviewed", "IGDB" if mapping.get("IgdbId") else "official metadata", flush=True)
    catalog.save(output, report)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default=str(catalog.ROOT / "Data/catalog-resolution-report.json"))
    prepare(parser.parse_args().output)
