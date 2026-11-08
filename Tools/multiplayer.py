"""Collect structured game modes for reviewed IGDB identities and enrich a catalog offline."""

import argparse
import time
from pathlib import Path

import catalog


MULTIPLAYER_MODES = {"Multiplayer", "Co-operative", "Split screen", "Massively Multiplayer Online (MMO)", "Battle Royale"}


def collect(args):
    snapshot = catalog.load(args.catalog)
    catalog.validate(snapshot)
    identities = sorted({game["IgdbId"] for game in snapshot["Games"] if game.get("Reviewed") and game.get("IgdbId")})
    headers = catalog.igdb_headers()
    games = []
    for start in range(0, len(identities), 100):
        batch = identities[start:start + 100]
        query = ("fields name,url,game_modes.name,multiplayer_modes.platform,multiplayer_modes.onlinecoop,"
                 "multiplayer_modes.offlinecoop,multiplayer_modes.onlinemax,multiplayer_modes.offlinemax,"
                 "multiplayer_modes.splitscreen; where id = (" + ",".join(map(str, batch)) + "); limit 100;")
        games.extend(catalog.request("https://api.igdb.com/v4/games", query, headers))
        print(f"Fetched modes for {len(games)}/{len(identities)} reviewed identities", flush=True)
        time.sleep(0.3)
    if {game["id"] for game in games} != set(identities) or len(games) != len(identities):
        raise ValueError("Incomplete or duplicate IGDB identity response; no collection saved")
    catalog.save(args.output, {"FetchedAt": catalog.now(), "Games": games})


def metadata(game, fetched_at):
    """Absence is unknown; descriptions and title keywords never establish support."""
    modes = {mode["name"] for mode in game.get("game_modes", [])}
    pc_modes = [mode for mode in game.get("multiplayer_modes", []) if mode.get("platform") == 6]
    pc_multiplayer = any(mode.get("onlinecoop") or mode.get("offlinecoop") or mode.get("splitscreen")
                         or (mode.get("onlinemax") or 0) > 1 or (mode.get("offlinemax") or 0) > 1
                         for mode in pc_modes)
    if pc_multiplayer or modes & MULTIPLAYER_MODES:
        supported = True
    elif modes == {"Single player"}:
        supported = False
    else:
        return {}
    url = game.get("url", "")
    if not url.startswith("https://www.igdb.com/games/"):
        raise ValueError("IGDB modes require a source game URL")
    evidence = ", ".join(sorted(modes)) or "PC multiplayer record"
    pc_evidence = "PC multiplayer/co-op record; " if pc_multiplayer else ""
    return {"SupportsMultiplayer": supported, "MultiplayerVerifiedAt": fetched_at,
            "MultiplayerProvenance": f"IGDB structured game modes: {evidence}; {pc_evidence}{url}"}


def apply(args):
    snapshot = catalog.load(args.catalog)
    collection = catalog.load(args.collection)
    fetched_at = collection["FetchedAt"]
    catalog.parsedate(fetched_at)
    games = {game["id"]: game for game in collection["Games"]}
    if len(games) != len(collection["Games"]):
        raise ValueError("Duplicate collected IGDB identities")
    counts = {True: 0, False: 0, None: 0}
    for entry in snapshot["Games"]:
        source = games.get(entry.get("IgdbId")) if entry.get("Reviewed") else None
        if source is not None:
            # A new complete provider result can retract previous multiplayer evidence.
            for field in ("SupportsMultiplayer", "MultiplayerVerifiedAt", "MultiplayerProvenance"):
                entry.pop(field, None)
            entry.update(metadata(source, fetched_at))
        counts[entry.get("SupportsMultiplayer")] += 1
    # This is supplemental metadata for the existing snapshot. Keep its publication cutoff:
    # re-dating unchanged old offers would replace newer live prices during catalog import.
    suffix = "-multiplayer-" + catalog.parsedate(fetched_at).strftime("%Y%m%dT%H%M%SZ")
    snapshot["Version"] = snapshot["Version"].split("-multiplayer-")[0] + suffix
    catalog.validate(snapshot)
    catalog.save(args.output, snapshot)
    print(f"Multiplayer: {counts[True]}; single-player-only: {counts[False]}; unknown: {counts[None]}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    for name in ("collect", "apply"):
        command = commands.add_parser(name)
        command.add_argument("--catalog", required=True, type=Path)
        command.add_argument("--output", required=True, type=Path)
        if name == "apply":
            command.add_argument("--collection", required=True, type=Path)
    args = parser.parse_args()
    {"collect": collect, "apply": apply}[args.command](args)


if __name__ == "__main__":
    main()
