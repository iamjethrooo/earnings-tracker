import unittest
import populate_catalog as populate


class IdentityTests(unittest.TestCase):
    @staticmethod
    def game(identity, name, platform=6, game_type=0):
        return {"id": identity, "name": name, "platforms": [{"id": platform}], "game_type": game_type}

    def test_unique_exact_pc_title_handles_typography(self):
        self.assertEqual(populate.select_match("Detroit Become Human", [self.game(1, "Detroit: Become Human")])["id"], 1)

    def test_remakes_with_same_title_are_not_guessed(self):
        self.assertIsNone(populate.select_match("Silent Hill 2", [self.game(1, "Silent Hill 2"), self.game(2, "Silent Hill 2", game_type=8)]))

    def test_console_only_matches_and_dlc_are_rejected(self):
        self.assertIsNone(populate.select_match("Example", [self.game(1, "Example", platform=48)]))
        self.assertIsNone(populate.select_match("Example", [self.game(1, "Example", game_type=13)]))

    def test_abbreviations_expand_explicitly_not_by_substring(self):
        self.assertEqual(populate.select_match("TABS", [self.game(1, "Totally Accurate Battle Simulator")])["id"], 1)
        self.assertIsNone(populate.select_match("Divinity", [self.game(1, "Divinity: Original Sin 2")]))

    def test_same_provider_id_is_not_a_false_ambiguity(self):
        game = self.game(1, "Example")
        self.assertEqual(populate.select_match("Example", [game, game])["id"], 1)


if __name__ == "__main__":
    unittest.main()
