import argparse
import copy
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

import catalog


class CatalogToolTests(unittest.TestCase):
    def setUp(self):
        self.snapshot = {"SchemaVersion": 1, "Version": "test", "PublishedAt": catalog.now(), "Games": [
            {"CatalogId": "a", "Name": "Example", "Reviewed": False, "Genres": [], "Offers": [], "Subscriptions": []}]}

    def test_unreviewed_enrichment_is_rejected(self):
        self.snapshot["Games"][0]["Genres"] = ["Action"]
        with self.assertRaises(ValueError):
            catalog.validate(self.snapshot)

    def test_official_manual_metadata_has_no_fabricated_igdb_identity_and_reuses_artwork(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            catalog.save(path / "catalog.json", self.snapshot)
            mapping = {"Reviewed": True, "IgdbId": None, "ReviewedAt": "2026-01-01T00:00:00Z",
                "MatchEvidence": "Official exact prologue identity", "ManualMetadata": {"Summary": "Official prologue",
                    "Genres": ["Simulation"], "CoverUrl": "https://example.com/prologue.jpg", "Provenance": "Official storefront"}}
            catalog.save(path / "review.json", {"Mappings": {"a": mapping}})
            args = argparse.Namespace(catalog=path / "catalog.json", review=path / "review.json", cache=path / "cache",
                artwork=path / "artwork", output=path / "enriched.json", refresh_igdb=False)
            with patch.object(catalog, "igdb_headers", side_effect=AssertionError("No IGDB identity")), \
                 patch.object(catalog, "request", return_value=b"official cover") as fetch:
                catalog.enrich(args)
                args.catalog = args.output; catalog.enrich(args)
                self.assertEqual(fetch.call_count, 1)
            game = catalog.load(args.output)["Games"][0]
            self.assertIsNone(game["IgdbId"])
            self.assertEqual(game["Summary"], "Official prologue")
            self.assertEqual(game["VerifiedAt"], mapping["ReviewedAt"])
            catalog.validate(catalog.load(args.output))
            mapping["ManualMetadata"].pop("Provenance")
            catalog.save(path / "review.json", {"Mappings": {"a": mapping}})
            with self.assertRaisesRegex(ValueError, "provenance"): catalog.enrich(args)

    def test_archived_status_uses_duplicate_seed_occurrence(self):
        seed = [{"Name": "Example", "IsArchived": True}, {"Name": "Example", "IsArchived": False}]
        with patch.object(catalog, "load", return_value=seed):
            self.assertTrue(catalog.is_archived({"Name": "Example", "NameOccurrence": 1}))
            self.assertFalse(catalog.is_archived({"Name": "Example", "NameOccurrence": 2}))
            self.assertFalse(catalog.is_archived({"Name": "Unknown"}))

    def test_duplicate_occurrences_are_distinct_but_unmapped_duplicates_fail(self):
        other = copy.deepcopy(self.snapshot["Games"][0])
        other["CatalogId"] = "b"
        self.snapshot["Games"].append(other)
        with self.assertRaises(ValueError):
            catalog.validate(self.snapshot)
        for i, game in enumerate(self.snapshot["Games"], 1):
            game.update(NameOccurrence=i, ExpectedNameCount=2)
        catalog.validate(self.snapshot)

    def test_release_is_blocked_without_explicit_review(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            catalog.save(path / "catalog.json", self.snapshot)
            catalog.save(path / "review.json", {"ReleaseApproved": False})
            args = argparse.Namespace(catalog=path / "catalog.json", review=path / "review.json", artwork=path, output=path / "output")
            with self.assertRaisesRegex(ValueError, "ReleaseApproved"):
                catalog.package(args)
            self.assertFalse((path / "output").exists())

    def test_bootstrap_report_accounts_for_entire_catalog(self):
        snapshot = catalog.load(catalog.ROOT / "Data/catalog.json")
        catalog.validate(snapshot)
        self.assertEqual(len(snapshot["Games"]), 579)
        self.assertEqual(len({g["CatalogId"] for g in snapshot["Games"]}), 579)

    def test_cached_enrichment_requires_no_credentials_and_downloads_artwork_once(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            catalog.save(path / "catalog.json", self.snapshot)
            catalog.save(path / "review.json", {"IgdbRedistributionApproved": True,
                         "Mappings": {"a": {"Reviewed": True, "IgdbId": 123}}})
            fetched = "2026-01-01T00:00:00Z"
            catalog.save(path / "cache/123.json", {"FetchedAt": fetched,
                         "Game": {"id": 123, "summary": "Cached description", "genres": [{"name": "Action"}],
                                  "cover": {"image_id": "fixture"}}})
            args = argparse.Namespace(catalog=path / "catalog.json", review=path / "review.json",
                                      cache=path / "cache", artwork=path / "artwork", output=path / "enriched.json", refresh_igdb=False)
            with patch.object(catalog, "igdb_headers", side_effect=AssertionError("No credentials needed")), \
                 patch.object(catalog, "request", return_value=b"fixture artwork") as fetch:
                catalog.enrich(args)
                catalog.enrich(args)
                self.assertEqual(fetch.call_count, 1)
            result = catalog.load(args.output)["Games"][0]
            self.assertEqual(result["VerifiedAt"], fetched)
            self.assertEqual(result["Genres"], ["Action"])

    def test_existing_metadata_allows_manual_updates_without_cache_or_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            self.snapshot["Games"][0].update(Reviewed=True, IgdbId=123, ItadId="saved-price-id", Summary="Saved", Genres=["Action"], VerifiedAt="2026-01-01T00:00:00Z")
            catalog.save(path / "catalog.json", self.snapshot)
            catalog.save(path / "review.json", {"IgdbRedistributionApproved": True,
                         "Mappings": {"a": {"Reviewed": True, "IgdbId": 123, "Subscriptions": []}}})
            args = argparse.Namespace(catalog=path / "catalog.json", review=path / "review.json",
                                      cache=path / "missing-cache", artwork=path / "artwork", output=path / "enriched.json", refresh_igdb=False)
            with patch.object(catalog, "igdb_headers", side_effect=AssertionError("No credentials needed")), \
                 patch.object(catalog, "request", side_effect=AssertionError("No network needed")):
                catalog.enrich(args)
            self.assertEqual(catalog.load(args.output)["Games"][0]["ItadId"], "saved-price-id")

    def test_completed_candidate_search_is_reusable_without_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            catalog.save(path / "catalog.json", self.snapshot)
            catalog.save(path / "candidates.json", {"a": {"Name": "Example", "Candidates": []}})
            args = argparse.Namespace(catalog=path / "catalog.json", cache=path / "cache", output=path / "candidates.json")
            with patch.object(catalog, "igdb_headers", side_effect=AssertionError("No credentials needed")):
                catalog.candidates(args)


if __name__ == "__main__":
    unittest.main()
