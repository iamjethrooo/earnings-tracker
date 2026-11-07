import unittest

from build_steam_app_mappings import build_mappings


class SteamAppMappingsTests(unittest.TestCase):
    def test_only_reviewed_unambiguous_apps_are_included(self):
        snapshot = {"Games": [
            {"Reviewed": True, "ItadId": "base"},
            {"Reviewed": False, "ItadId": "unreviewed"},
            {"Reviewed": True, "ItadId": "ambiguous"},
            {"Reviewed": True, "ItadId": "package"},
            {"Reviewed": True, "ItadId": "missing"},
            {"Reviewed": True, "ItadId": "invalid"},
            {"Reviewed": True, "ItadId": None},
        ]}
        cache = {"base": ["sub/1", "app/220", "app/220"], "unreviewed": ["app/221"],
                 "ambiguous": ["app/220", "app/221"], "package": ["sub/220", "bundle/220"],
                 "invalid": ["app/0", "app/-1", "app/4294967296", "app/not-an-id", "app/٢٢٠", None]}
        self.assertEqual({"base": 220}, build_mappings(snapshot, cache))
