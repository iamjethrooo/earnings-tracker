import argparse
import copy
import datetime as dt
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import catalog
import subscriptions as s


class SubscriptionTests(unittest.TestCase):
    def setUp(self):
        self.game = {"CatalogId": "game", "Name": "Example", "Reviewed": True, "Genres": [], "Offers": [], "Subscriptions": []}
        self.snapshot = {"SchemaVersion": 1, "Version": "test", "PublishedAt": catalog.now(), "Games": [self.game]}
        self.source = s.provider_snapshot("PC Game Pass", s.XBOX_PAGE,
            [{"Id": "ID", "Title": "Example", "Url": "https://www.xbox.com/en-PH/games/store/-/ID"}], True, True, "Official PH PC list")

    def propose(self, sources=None):
        with patch.object(catalog, "is_archived", return_value=False):
            return s.propose(self.snapshot, {"game": {"IgdbTitle": "Example"}}, sources or [self.source])

    def test_normalization_preserves_editions_and_years(self):
        self.assertEqual(s.normalized("Example™ (Windows)"), "example")
        self.assertNotEqual(s.normalized("Example Remastered"), "example")
        self.assertNotEqual(s.normalized("Example 2012"), s.normalized("Example 2005"))

    def test_additions_are_candidates_without_modifying_catalog(self):
        candidate = self.propose()["Candidates"][0]
        self.assertEqual(candidate["Action"], "Added")
        self.assertFalse(candidate["Approved"])
        self.assertEqual(self.game["Subscriptions"], [])

    def test_archived_and_ambiguous_identities_are_not_automatched(self):
        with patch.object(catalog, "is_archived", return_value=True):
            self.assertEqual(s.propose(self.snapshot, {}, [self.source])["Candidates"], [])
        duplicate = copy.deepcopy(self.game); duplicate["CatalogId"] = "duplicate"
        self.snapshot["Games"].append(duplicate)
        result = self.propose()
        self.assertEqual(result["Candidates"], [])
        self.assertEqual(len(result["Ambiguous"]), 1)

    def test_incomplete_failed_and_unverified_regions_never_imply_removals(self):
        self.game["Subscriptions"] = [{"Service": "PC Game Pass", "Included": True, "Url": s.XBOX_PAGE,
            "Provenance": "Official subscription catalog; id:missing; reviewed"}]
        self.source["Products"][0]["Title"] = "Other"
        self.source["Complete"] = False
        self.assertEqual(self.propose()["Candidates"], [])
        self.source["Complete"] = True; self.source["RegionConfirmed"] = False
        self.assertEqual(self.propose()["Candidates"], [])
        self.source["RegionConfirmed"] = True
        self.assertEqual(self.propose()["Candidates"][0]["Action"], "Removed")
        self.assertEqual(self.propose([{"Failed": True}])["Candidates"], [])

    def test_product_title_change_is_not_a_false_removal(self):
        self.game["Subscriptions"] = [{"Service": "PC Game Pass", "Included": True, "Url": s.XBOX_PAGE,
            "Provenance": "Official subscription catalog; id:ID; reviewed"}]
        self.source["Products"][0]["Title"] = "New name needing review"
        self.assertEqual(self.propose()["Candidates"], [])

    def test_empty_catalog_is_not_removal_evidence(self):
        with self.assertRaises(ValueError): s.provider_snapshot("PC Game Pass", s.XBOX_PAGE, [], True, True)

    def test_competing_provider_editions_require_review(self):
        product = copy.deepcopy(self.source["Products"][0]); product["Id"] = "OTHER"
        self.source["Products"].append(product)
        result = self.propose()
        self.assertEqual(result["Candidates"], [])
        self.assertEqual(len(result["Ambiguous"]), 1)

    def test_future_and_stale_dates_cannot_grant_access(self):
        now = dt.datetime.now(dt.timezone.utc)
        self.assertTrue(s.fresh(now.isoformat()))
        self.assertFalse(s.fresh((now+dt.timedelta(days=1)).isoformat()))
        self.assertFalse(s.fresh((now-dt.timedelta(days=8)).isoformat()))

    def test_full_entitlement_excludes_trials_preorders_discounts_and_future_access(self):
        now = dt.datetime.now(dt.timezone.utc)
        availability = {"Actions": ["Purchase"], "RemediationRequired": True, "Remediations": [{"BigId": "PLAN"}],
            "OrderManagementData": {"Price": {"ListPrice": 0, "MSRP": 500}},
            "Conditions": {"StartDate": (now-dt.timedelta(days=1)).isoformat(), "EndDate": (now+dt.timedelta(days=1)).isoformat(),
                "ClientConditions": {"AllowedPlatforms": [{"PlatformName": "Windows.Desktop"}]}}}
        product = {"DisplaySkuAvailabilities": [{"Sku": {"Properties": {}}, "Availabilities": [availability]}]}
        self.assertIsNotNone(s.full_pc_entitlement(product, "PLAN", now))
        self.assertIsNone(s.full_pc_entitlement(product, "OTHER", now))
        props = product["DisplaySkuAvailabilities"][0]["Sku"]["Properties"]
        for flag in ["IsTrial", "IsPreOrder"]:
            props[flag] = True; self.assertIsNone(s.full_pc_entitlement(product, "PLAN", now)); props[flag] = False
        availability["OrderManagementData"]["Price"]["ListPrice"] = 100
        self.assertIsNone(s.full_pc_entitlement(product, "PLAN", now))
        availability["OrderManagementData"]["Price"]["ListPrice"] = 0
        availability["Conditions"]["StartDate"] = (now+dt.timedelta(days=1)).isoformat()
        self.assertIsNone(s.full_pc_entitlement(product, "PLAN", now))

    def test_unsupported_subscription_sources_are_ignored(self):
        self.source["Service"] = "EA Play"
        self.assertEqual(self.propose()["Candidates"], [])

    def files(self, directory, collection, decisions):
        root = Path(directory)
        for name, data in [("catalog", self.snapshot), ("collection", collection), ("decisions", decisions),
                           ("review", {"Mappings": {"game": {"Reviewed": True, "IgdbId": 1}}})]:
            catalog.save(root/(name+".json"), data)
        return argparse.Namespace(catalog=root/"catalog.json", collection=root/"collection.json", decisions=root/"decisions.json",
            review=root/"review.json", output=root/"output.json")

    def test_apply_requires_region_edition_identity_and_exact_collection(self):
        self.source["RegionConfirmed"] = False
        collection = self.propose(); candidate = collection["Candidates"][0]
        decision = {"CandidateId": candidate["CandidateId"], "Approved": True, "IdentityConfirmed": True,
            "EditionConfirmed": True, "Evidence": "Reviewed exact native PC edition"}
        with tempfile.TemporaryDirectory() as directory, patch.object(catalog, "is_archived", return_value=False):
            args = self.files(directory, collection, {"CollectionHash": s.collection_hash(collection), "Decisions": [decision]})
            with self.assertRaisesRegex(ValueError, "regional evidence"): s.apply(args)
            self.assertFalse(args.output.exists())
            decision.update(RegionConfirmed=True, RegionEvidence="Official PH product listing checked")
            catalog.save(args.decisions, {"CollectionHash": s.collection_hash(collection), "Decisions": [decision]})
            s.apply(args)
            self.assertTrue(catalog.load(args.output)["Games"][0]["Subscriptions"][0]["Included"])
            collection["CollectedAt"] = "different"; catalog.save(args.collection, collection)
            with self.assertRaisesRegex(ValueError, "exact collection"): s.apply(args)

    def test_reverify_changes_only_previously_reviewed_provider_id(self):
        self.game["Subscriptions"] = [{"Service": "PC Game Pass", "Included": True, "Platform": "PC", "Region": "PH",
            "VerifiedAt": "2020-01-01T00:00:00Z", "Url": s.XBOX_PAGE, "Provenance": "Official subscription catalog; id:ID; reviewed"}]
        collection = self.propose()
        with tempfile.TemporaryDirectory() as directory, patch.object(catalog, "is_archived", return_value=False):
            args = self.files(directory, collection, {"Decisions": []}); s.reverify(args)
            self.assertEqual(catalog.load(args.output)["Games"][0]["Subscriptions"][0]["VerifiedAt"], self.source["FetchedAt"])
            collection["Candidates"][0]["ProviderId"] = "different"; catalog.save(args.collection, collection); s.reverify(args)
            self.assertEqual(catalog.load(args.output)["Games"][0]["Subscriptions"][0]["VerifiedAt"], "2020-01-01T00:00:00Z")


if __name__ == "__main__": unittest.main()
