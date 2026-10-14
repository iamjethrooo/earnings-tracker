"""Cache focused identity research for the remaining active titles; never auto-approve matches."""
import argparse
import json
from pathlib import Path
import time
import catalog

SEARCHES = {
    "DRAGON BALL GEKISHIN SQUADRA EM": "Dragon Ball Gekishin Squadra",
    "Call of Duty: Modern Warfare 2 (2009)": "Call of Duty: Modern Warfare 2",
    "Pokémon Infinite Fusion": "Pokemon Infinite Fusion",
    "Nova-Life: Amboise": "Nova-Life",
    "Golf It": "Golf It!",
    "Coop Deluxe": "SM64",
    "Retro Rewind": "Retro Rewind: Video Store Simulator",
    "Track Day R": "TrackDayR",
    "Cities": "Cities: Skylines",
    "Baldi's Basics": "Baldi's Basics in Education and Learning",
    "Fast Food Simulator: Prologue": "Fast Food Simulator",
    "Sound Space +": "Sound Space",
    "Spaceflight Simulator M": "Spaceflight Simulator",
    "New World": "New World",
    "The Planet Crafter": "Planet Crafter",
    "OPTCG Simulator": "One Piece",
    "WRC": "EA Sports WRC",
    "SWORN": "Sworn",
    "Bus Simulator 21 Next Stop": "Bus Simulator 21",
    "TCG Card Shop Simulator: Prologue": "TCG Card Shop Simulator",
    "Scooter Flow": "ScooterFlow",
}

SLUGS = {"Golf It": "golf-it", "Pokémon Infinite Fusion": "pokemon-infinite-fusion",
    "Coop Deluxe": "sm64coopdx", "Fast Food Simulator: Prologue": "fast-food-simulator-prologue",
    "TCG Card Shop Simulator: Prologue": "tcg-card-shop-simulator-prologue",
    "Sound Space +": "sound-space", "OPTCG Simulator": "optcgsim"}

def research(output):
    snapshot = catalog.load(catalog.ROOT / "Data/catalog.json")
    results = catalog.load(output) if Path(output).exists() else {}
    headers = None
    for game in snapshot["Games"]:
        if game.get("Reviewed") or catalog.is_archived(game): continue
        title = SEARCHES.get(game["Name"], game["Name"])
        previous = results.get(game["CatalogId"], {})
        slug = SLUGS.get(game["Name"])
        if previous.get("SearchTitle") == title and previous.get("SlugQuery") == slug: continue
        headers = headers or catalog.igdb_headers()
        query = f'search {json.dumps(title, ensure_ascii=False)}; fields name,slug,summary,genres.name,cover.image_id,platforms.name,game_type,first_release_date,version_parent,external_games.uid,external_games.url,external_games.external_game_source.name; limit 40;'
        matches = catalog.request("https://api.igdb.com/v4/games", query, headers)
        if slug:
            exact = catalog.request("https://api.igdb.com/v4/games",
                f'fields name,slug,summary,genres.name,cover.image_id,platforms.name,game_type,first_release_date,external_games.uid,external_games.url,external_games.external_game_source.name; where slug = {json.dumps(slug)}; limit 5;', headers)
            matches = list({m["id"]: m for m in matches + exact}.values())
        for match in matches:
            catalog.save(catalog.ROOT / "Data/IgdbCache" / (str(match["id"]) + ".json"),
                {"FetchedAt": catalog.now(), "Game": match, "SourcesFetched": True})
        results[game["CatalogId"]] = {"Name": game["Name"], "SearchTitle": title, "SlugQuery": slug, "Candidates": matches}
        catalog.save(output, results)
        print(game["Name"], [(m["id"], m["name"], m.get("game_type")) for m in (exact if slug else matches[:5])], flush=True)
        time.sleep(0.3)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default=str(catalog.ROOT / "Data/remaining-candidates.json"))
    research(parser.parse_args().output)
