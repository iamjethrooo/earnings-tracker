"""Apply the catalog owner's explicitly selected editions; retain cached metadata."""
import catalog

choices = {"Persona 4": (9600, "original (2008)"),
           "Binding of Isaac": (2132, "original (2011)"),
           "NFS Most Wanted": (3193, "2012"),
           "NFS Hot Pursuit": (105, "2010")}
snapshot = catalog.load(catalog.ROOT / "Data/catalog.json")
review = catalog.load(catalog.ROOT / "Data/catalog-review.json")
headers = None
for entry in snapshot["Games"]:
    if entry["Name"] not in choices:
        continue
    identity, selection = choices[entry["Name"]]
    if identity is None:
        # Exact release-year match prevents accidentally selecting the remaster.
        headers = headers or catalog.igdb_headers()
        matches = catalog.request("https://api.igdb.com/v4/games",
            'search "Need for Speed: Hot Pursuit"; fields name,summary,genres.name,cover.image_id,platforms.name,game_type,first_release_date; where game_type = 0; limit 30;', headers)
        matches = [g for g in matches if g["name"] == "Need for Speed: Hot Pursuit"
                   and catalog.dt.datetime.fromtimestamp(g["first_release_date"], catalog.dt.timezone.utc).year == 2010]
        if len(matches) != 1:
            raise ValueError("2010 Hot Pursuit identity remains ambiguous")
        identity = matches[0]["id"]
        catalog.save(catalog.ROOT / "Data/IgdbCache" / (str(identity) + ".json"),
                     {"FetchedAt": catalog.now(), "Game": matches[0]})
    path = catalog.ROOT / "Data/IgdbCache" / (str(identity) + ".json")
    cached = catalog.load(path)
    # Store identifiers are fetched once and used for edition-specific price matching.
    if not cached.get("SourcesFetched"):
        headers = headers or catalog.igdb_headers()
        sources = catalog.request("https://api.igdb.com/v4/games",
            f'fields external_games.uid,external_games.external_game_source.name; where id = {identity};', headers)
        cached["Game"]["external_games"] = sources[0].get("external_games", [])
        cached["SourcesFetched"] = True
        catalog.save(path, cached)
    game = cached["Game"]
    mapping = {"Reviewed": True, "IgdbId": identity, "IgdbTitle": game["name"],
               "OriginalName": entry["Name"], "ManualOverride": True,
               "MatchEvidence": "Catalog owner explicitly selected " + selection,
               "ReviewedAt": catalog.now(), "UserEditionChoice": selection}
    steam = {g["uid"] for g in game.get("external_games", [])
             if g.get("external_game_source", {}).get("name") == "Steam" and g["uid"].isdigit()}
    if len(steam) == 1 and entry["Name"] != "Persona 4":
        app = "app/" + next(iter(steam))
        found = catalog.request("https://api.isthereanydeal.com/lookup/id/shop/61/v1", [app],
                                {"Content-Type": "application/json"})
        if found.get(app):
            mapping["ItadId"] = found[app]
            mapping["PriceMatchEvidence"] = "Reviewed edition's IGDB Steam app ID: " + app
    review["Mappings"][entry["CatalogId"]] = mapping
    print(entry["Name"], selection, "IGDB", identity, "price mapping", bool(mapping.get("ItadId")), flush=True)
catalog.save(catalog.ROOT / "Data/catalog-review.json", review)
