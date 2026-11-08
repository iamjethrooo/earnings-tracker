import argparse
import copy
from pathlib import Path
import tempfile
import unittest

import catalog
import multiplayer


class MultiplayerTests(unittest.TestCase):
    def setUp(self):
        self.source = {"id": 123, "url": "https://www.igdb.com/games/example"}
        self.checked_at = "2026-01-01T00:00:00Z"

    def test_structured_modes_distinguish_multiplayer_solo_and_unknown(self):
        self.assertEqual(multiplayer.metadata({**self.source, "summary": "multiplayer co-op"}, self.checked_at), {})
        for mode in multiplayer.MULTIPLAYER_MODES:
            with self.subTest(mode=mode):
                result = multiplayer.metadata({**self.source, "game_modes": [{"name": mode}]}, self.checked_at)
                self.assertTrue(result["SupportsMultiplayer"])
                self.assertIn(mode, result["MultiplayerProvenance"])
        result = multiplayer.metadata({**self.source, "game_modes": [{"name": "Single player"}]}, self.checked_at)
        self.assertFalse(result["SupportsMultiplayer"])
        self.assertEqual(result["MultiplayerVerifiedAt"], self.checked_at)

    def test_pc_specific_coop_counts_but_another_platform_alone_does_not(self):
        game = {**self.source, "multiplayer_modes": [{"platform": 6, "onlinecoop": True}]}
        self.assertTrue(multiplayer.metadata(game, self.checked_at)["SupportsMultiplayer"])
        game["multiplayer_modes"][0]["platform"] = 48
        self.assertEqual(multiplayer.metadata(game, self.checked_at), {})

    def test_apply_changes_only_mode_fields_and_version_and_is_repeatable(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            snapshot = {"SchemaVersion": 1, "Version": "original", "PublishedAt": self.checked_at,
                        "Games": [{"CatalogId": "a", "Name": "Example", "Reviewed": True, "IgdbId": 123,
                                   "Summary": "Original", "Genres": ["Action"], "Offers": [], "Subscriptions": []}]}
            collection = {"FetchedAt": self.checked_at,
                          "Games": [{**self.source, "game_modes": [{"name": "Co-operative"}]}]}
            catalog.save(path / "catalog.json", snapshot)
            catalog.save(path / "collection.json", collection)
            args = argparse.Namespace(catalog=path / "catalog.json", collection=path / "collection.json", output=path / "output.json")
            multiplayer.apply(args)
            first = catalog.load(args.output)
            args.catalog = args.output
            multiplayer.apply(args)
            self.assertEqual(first, catalog.load(args.output))
            original = copy.deepcopy(first)
            for field in ("SupportsMultiplayer", "MultiplayerVerifiedAt", "MultiplayerProvenance"):
                del original["Games"][0][field]
            original["Version"] = snapshot["Version"]
            self.assertEqual(original, snapshot)

    def test_invalid_or_undated_multiplayer_claims_are_rejected(self):
        game = {"CatalogId": "a", "Name": "Example", "Reviewed": True, "SupportsMultiplayer": True}
        snapshot = {"SchemaVersion": 1, "Version": "test", "PublishedAt": self.checked_at, "Games": [game]}
        with self.assertRaises(ValueError): catalog.validate(snapshot)
        game.update(MultiplayerProvenance="Source", MultiplayerVerifiedAt=self.checked_at)
        catalog.validate(snapshot)
        game["SupportsMultiplayer"] = 1
        with self.assertRaises(ValueError): catalog.validate(snapshot)
        game["SupportsMultiplayer"] = None
        with self.assertRaises(ValueError): catalog.validate(snapshot)


if __name__ == "__main__":
    unittest.main()
