import argparse
import hashlib
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

    def metadata_rate_snapshot(self):
        snapshot = copy.deepcopy(self.snapshot)
        snapshot['Games'][0]['Reviewed'] = True
        snapshot['Games'].append({'CatalogId': 'variant', 'Name': 'Example (2 USD/hr)', 'Reviewed': True,
                                  'RateVariant': {'GameName': 'Example', 'RatePerHour': 2, 'MinutesCap': 3000,
                                                  'LegacyCatalogId': 'a'}})
        return snapshot

    def test_metadata_rate_variants_are_additive_in_both_schemas(self):
        for schema in (1, 2):
            snapshot = self.metadata_rate_snapshot()
            snapshot['SchemaVersion'] = schema
            if schema == 2:
                for game in snapshot['Games']:
                    game['SteamIdentity'] = {'State': 'Unresolved', 'ReviewedAt': None, 'Apps': [], 'Provenance': 'Pending review'}
            catalog.validate(snapshot)

    def test_metadata_rate_variants_reject_invalid_terms_and_references(self):
        for field, value in [('RatePerHour', 0), ('RatePerHour', 1.555), ('RatePerHour', True),
                             ('MinutesCap', 0), ('MinutesCap', True), ('GameName', 'Other game'),
                             ('LegacyCatalogId', 'missing'), ('LegacyCatalogId', 'variant')]:
            snapshot = self.metadata_rate_snapshot()
            snapshot['Games'][1]['RateVariant'][field] = value
            with self.subTest(field=field, value=value), self.assertRaisesRegex(ValueError, 'metadata rate variant'):
                catalog.validate(snapshot)
        for invalid in ({'Reviewed': False}, {'RosterAddition': {'RatePerHour': 2, 'MinutesCap': 3000}},
                        {'NameOccurrence': 1, 'ExpectedNameCount': 2}):
            snapshot = self.metadata_rate_snapshot()
            snapshot['Games'][1].update(invalid)
            with self.subTest(invalid=invalid), self.assertRaisesRegex(ValueError, 'metadata rate variant'):
                catalog.validate(snapshot)

    def test_metadata_rate_variant_is_not_an_extra_seed_or_a_roster_addition(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            catalog.save(path / 'Data/games.json', [{'Name': 'Example', 'RatePerHour': 2, 'MinutesCap': 3000}])
            snapshot = self.metadata_rate_snapshot()
            snapshot['Version'] = 'catalog-metadata-rate'
            catalog.save(path / 'catalog.json', snapshot)
            catalog.save(path / 'review.json', {'ReleaseApproved': True})
            args = argparse.Namespace(catalog=path / 'catalog.json', review=path / 'review.json',
                                      artwork=path / 'artwork', output=path / 'package')
            with patch.object(catalog, 'ROOT', path), patch.object(catalog, 'request', side_effect=AssertionError('Offline only')):
                catalog.package(args)
            self.assertEqual((path / 'package/catalog.json').read_bytes(), (path / 'catalog.json').read_bytes())

    def test_published_metadata_rate_terms_are_preserved(self):
        previous = self.metadata_rate_snapshot()
        previous.update(Version='catalog-old', PublishedAt='2026-01-01T00:00:00Z')
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            catalog.save(path / 'previous.json', previous)
            current = copy.deepcopy(previous)
            current['Version'] = 'catalog-new'
            current['PublishedAt'] = '2026-01-02T00:00:00Z'
            current['Games'][1]['RateVariant']['RatePerHour'] = 3
            catalog.save(path / 'current.json', current)
            with self.assertRaisesRegex(ValueError, 'Keep published metadata rate variant'):
                catalog.check_order(argparse.Namespace(catalog=path / 'current.json', previous=path / 'previous.json'))

    def test_steam_identity_validation_matches_shared_fixtures(self):
        fixtures = catalog.load(catalog.ROOT / "Tests/EarningsTracker.Tests/Fixtures/steam-identities.json")
        self.snapshot.update(SchemaVersion=2, PublishedAt=fixtures["PublishedAt"])
        for case in fixtures["Cases"]:
            with self.subTest(case=case["Name"]):
                self.snapshot["Games"][0].update(Reviewed=case.get("Reviewed", True), SteamIdentity={
                    "State": "Reviewed", "ReviewedAt": fixtures["ReviewedAt"],
                    "Provenance": "Synthetic review", "Apps": case["Apps"],
                })
                if case["Valid"]:
                    catalog.validate(self.snapshot)
                else:
                    with self.assertRaisesRegex(ValueError, "Steam identity"):
                        catalog.validate(self.snapshot)

    def test_schema_two_enrichment_and_package_use_explicit_review_only(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            game = self.snapshot["Games"][0]
            game.update(Reviewed=True, IgdbId=123)
            catalog.save(path / "catalog.json", self.snapshot)
            review = {"ReleaseApproved": True, "IgdbRedistributionApproved": True,
                      "Mappings": {"a": {"Reviewed": True, "IgdbId": 123, "SteamIds": [1001]}}}
            catalog.save(path / "review.json", review)
            args = argparse.Namespace(catalog=path / "catalog.json", review=path / "review.json", cache=path / "cache",
                artwork=path / "artwork", output=path / "enriched.json", refresh_igdb=False, steam_identities=True)
            with patch.object(catalog, "request", side_effect=AssertionError("No provider calls")):
                catalog.enrich(args)
                self.assertEqual(catalog.load(args.output)["Games"][0]["SteamIdentity"]["State"], "Unresolved")
                review["Mappings"]["a"]["SteamIdentity"] = {"State": "Reviewed", "ReviewedAt": "2026-01-01T00:00:00Z",
                    "Provenance": "Exact synthetic PC experience", "Apps": [{"AppId": 1001, "Kind": "FullGame"}]}
                catalog.save(args.review, review)
                catalog.enrich(args)
                candidate = catalog.load(args.output)
                candidate["Games"][0]["RosterAddition"] = {"RatePerHour": 2.5, "MinutesCap": 3000}
                catalog.save(args.output, candidate)
                args.catalog, args.output = args.output, path / "package"
                with patch.object(catalog, "ROOT", path):
                    catalog.save(path / "Data/games.json", [{"Name": "Example"}])
                    args.steam_identities = False
                    with self.assertRaisesRegex(ValueError, "offline verification"):
                        catalog.package(args)
                    args.steam_identities = True
                    with self.assertRaisesRegex(ValueError, "minimum-client-version"):
                        catalog.package(args)
                    args.minimum_client_version = "1.5.0"
                    catalog.package(args)
            manifest = catalog.load(path / "package/manifest.json")
            self.assertEqual(manifest["SchemaVersion"], 2)
            self.assertEqual(manifest["MinimumClientVersion"], "1.5.0")
            self.assertEqual(manifest["Version"], catalog.load(args.catalog)["Version"])
            self.assertEqual((path / "package/catalog.json").read_bytes(), args.catalog.read_bytes())
            coverage = catalog.load(path / "package/coverage.json")
            self.assertEqual(coverage["SteamIdentityCoverage"]["Reviewed"], 1)
            self.assertEqual(coverage["RosterAdditions"], [
                {"CatalogId": "a", "Name": "Example", "RatePerHour": 2.5, "MinutesCap": 3000}])

    def test_roster_terms_must_be_valid_and_survive_later_snapshots(self):
        self.snapshot.update(SchemaVersion=2, Version="catalog-old", PublishedAt="2026-01-01T00:00:00Z")
        game = self.snapshot["Games"][0]
        game.update(Reviewed=True, SteamIdentity={"State": "Unresolved", "ReviewedAt": None, "Apps": [],
                                                "Provenance": "Pending review"})
        for terms in ({"RatePerHour": 1.555, "MinutesCap": 3000}, {"RatePerHour": 2, "MinutesCap": True},
                      {"RatePerHour": 2, "MinutesCap": 3000, "IsArchived": True}):
            game["RosterAddition"] = terms
            with self.subTest(terms=terms), self.assertRaisesRegex(ValueError, "roster addition|starting rate"):
                catalog.validate(self.snapshot)
        game["RosterAddition"] = {"RatePerHour": 2, "MinutesCap": 3000}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            previous, current = path / "previous.json", path / "current.json"
            catalog.save(previous, self.snapshot)
            catalog.prepare(argparse.Namespace(catalog=previous, output=current))
            args = argparse.Namespace(catalog=current, previous=previous)
            catalog.check_order(args)
            candidate = catalog.load(current)
            candidate["Games"][0].pop("RosterAddition")
            catalog.save(current, candidate)
            with self.assertRaisesRegex(ValueError, "Keep published roster addition"):
                catalog.check_order(args)

    def test_same_title_roster_variants_require_distinct_rates(self):
        self.snapshot["SchemaVersion"] = 2
        first = self.snapshot["Games"][0]
        first.update(Name="Example (2 USD/hr)", Reviewed=True,
                     SteamIdentity={"State": "Unresolved", "ReviewedAt": None, "Apps": [],
                                    "Provenance": "Pending review"},
                     RosterAddition={"GameName": "Example", "RatePerHour": 2, "MinutesCap": 3000})
        second = copy.deepcopy(first)
        second.update(CatalogId="b", Name="Example (2.50 USD/hr)")
        self.snapshot["Games"].append(second)
        with self.assertRaisesRegex(ValueError, "Duplicate roster rate"):
            catalog.validate(self.snapshot)
        second["RosterAddition"]["RatePerHour"] = 2.5
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
            catalog.save(path / "catalog.json", dict(self.snapshot, Version="catalog-test"))
            catalog.save(path / "review.json", {"ReleaseApproved": False})
            args = argparse.Namespace(catalog=path / "catalog.json", review=path / "review.json", artwork=path, output=path / "output")
            with self.assertRaisesRegex(ValueError, "ReleaseApproved"):
                catalog.package(args)
            self.assertFalse((path / "output").exists())

    def test_publication_requires_a_distinct_advancing_version(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            args = argparse.Namespace(catalog=path / "next.json", previous=path / "previous.json",
                                      previous_manifest=path / "manifest.json")
            catalog.save(args.previous, dict(self.snapshot, Version="catalog-b"))
            manifest = {"SchemaVersion": 1, "Version": "catalog-b",
                        "CatalogUrl": catalog.RELEASE_ROOT + "catalog-b/catalog.json",
                        "Sha256": hashlib.sha256(args.previous.read_bytes()).hexdigest()}
            catalog.save(args.previous_manifest, manifest)
            for version, failure in [("catalog-a", "must advance"), ("catalog-b", "already exists"), ("catalog-c", None)]:
                with self.subTest(version=version):
                    catalog.save(args.catalog, dict(self.snapshot, Version=version))
                    if failure:
                        with self.assertRaisesRegex(ValueError, failure):
                            catalog.check_order(args)
                    else:
                        catalog.check_order(args)
            catalog.save(args.previous_manifest, dict(manifest, Sha256="0" * 64))
            with self.assertRaisesRegex(ValueError, "does not bind"):
                catalog.check_order(args)

    def test_bootstrap_report_accounts_for_entire_catalog(self):
        snapshot = catalog.load(catalog.ROOT / "Data/catalog.json")
        catalog.validate(snapshot)
        self.assertEqual(len(snapshot["Games"]), 583)
        self.assertEqual(len({g["CatalogId"] for g in snapshot["Games"]}), 583)
        self.assertEqual(sum(not game.get('RateVariant') for game in snapshot['Games']), 581)

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
