"""Offline checks for the PowerShell helper; Git/GitHub mutations are intercepted."""
import datetime as dt
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import catalog
import subscriptions


POWERSHELL = shutil.which("pwsh") or shutil.which("powershell")
TOOLS = Path(__file__).resolve().parent


@unittest.skipUnless(POWERSHELL, "PowerShell is not installed")
class UpdateCatalogScriptTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        (self.root / "Tools").mkdir()
        (self.root / "Data").mkdir()
        for name in ("Update-Catalog.ps1", "catalog.py", "subscriptions.py"):
            shutil.copyfile(TOOLS / name, self.root / "Tools" / name)
        (self.root / "Tools/test_fixture.py").write_text(
            'import unittest\nimport catalog\n'
            'class FixtureTests(unittest.TestCase):\n'
            '    def test_snapshot(self):\n'
            '        catalog.validate(catalog.load(catalog.ROOT / "Data/catalog.json"))\n',
            encoding="utf-8",
        )
        self.snapshot = {
            "SchemaVersion": 1, "Version": "catalog-offline-fixture",
            "PublishedAt": dt.datetime.now(dt.timezone.utc).isoformat(),
            "Games": [{"CatalogId": "a", "Name": "Example", "Reviewed": False,
                       "Genres": [], "Offers": [], "Subscriptions": []}],
        }
        self.review = {"ReleaseApproved": True, "IgdbRedistributionApproved": True,
                       "PriceRedistributionApproved": True}
        self.state = {"Enabled": "true", "PushFails": False, "HeadChanges": False, "DirtyInputs": False}
        self.environment = os.environ.copy()
        self.write_json("Data/games.json", [{"Name": "Example"}])
        # These functions intercept every Git/GitHub invocation in the child script.
        # Python still runs the real validator, tests, and packager against disposable data.
        (self.root / "harness.ps1").write_text(r'''
$ErrorActionPreference = 'Stop'
$global:testRoot = $PSScriptRoot
$global:testState = Get-Content (Join-Path $testRoot 'state.json') -Raw | ConvertFrom-Json
$global:headReads = 0
$global:pythonExecutable = (Get-Command python -CommandType Application | Select-Object -First 1).Source
function Write-TestCall($Command, $Arguments) {
    $entry = @{ Command = $Command; Arguments = @($Arguments) } | ConvertTo-Json -Compress
    Add-Content -LiteralPath (Join-Path $testRoot 'calls.jsonl') -Value $entry -Encoding UTF8
}
function global:git {
    Write-TestCall 'git' $args
    $global:LASTEXITCODE = 0
    switch ($args[0]) {
        'check-ref-format' { }
        'symbolic-ref' { 'codex/catalog-update' }
        'remote' { 'https://github.com/iamjethrooo/earnings-tracker.git' }
        'diff' { if ($testState.DirtyInputs) { 'Data/catalog.json' } }
        'ls-files' { if ($args -contains '--error-unmatch') { 'Data/catalog.json' } }
        'rev-parse' {
            $global:headReads++
            if ($testState.HeadChanges -and $headReads -gt 1) { 'b' * 40 } else { 'a' * 40 }
        }
        'push' { if ($testState.PushFails) { $global:LASTEXITCODE = 1 } }
        default { throw "Unexpected Git invocation: $args" }
    }
}
function global:gh {
    Write-TestCall 'gh' $args
    $global:LASTEXITCODE = 0
    switch ($args[0]) {
        'auth' { }
        'api' {
            if ($args[1] -like '*/actions/variables/*') { $testState.Enabled }
            elseif ($args[1] -like '*/git/ref/heads/*') { 'a' * 40 }
            else { throw "Unexpected GitHub API call: $args" }
        }
        'workflow' { }
        default { throw "Unexpected GitHub invocation: $args" }
    }
}
function global:python {
    if ($args[0] -eq 'Tools/subscriptions.py' -and $args[1] -eq 'collect') {
        Write-TestCall 'python' $args
        $fixture = Join-Path $testRoot 'collection-fixture.json'
        if (-not (Test-Path -LiteralPath $fixture)) { throw 'Unexpected Game Pass collection; network access is disabled in tests.' }
        $output = $args[[array]::IndexOf($args, '--output') + 1]
        Copy-Item -LiteralPath $fixture -Destination $output
        [IO.File]::WriteAllText([IO.Path]::ChangeExtension($output, '.review.json'), '{"CollectionHash":"offline-fixture","Decisions":[]}')
        $global:LASTEXITCODE = 0
    } elseif ($args[0] -eq 'Tools/catalog.py' -and $args[1] -eq 'prices') {
        Write-TestCall 'python' $args
        $fixture = Join-Path $testRoot 'prices-fixture.py'
        if (-not (Test-Path -LiteralPath $fixture)) { throw 'Unexpected price refresh; network access is disabled in tests.' }
        & $pythonExecutable $fixture @($args | Select-Object -Skip 2)
        $global:LASTEXITCODE = $LASTEXITCODE
    } else {
        & $pythonExecutable @args
        $global:LASTEXITCODE = $LASTEXITCODE
    }
}
$parameters = @{}
$inputParameters = Get-Content (Join-Path $testRoot 'parameters.json') -Raw | ConvertFrom-Json
foreach ($property in $inputParameters.PSObject.Properties) { $parameters[$property.Name] = $property.Value }
try {
    & (Join-Path $testRoot 'Tools/Update-Catalog.ps1') @parameters
    exit 0
} catch {
    Write-Output $_.Exception.Message
    exit 1
}
''', encoding="utf-8")

    def write_json(self, path, value):
        (self.root / path).write_text(json.dumps(value), encoding="utf-8")

    def run_helper(self, **parameters):
        self.write_json("Data/catalog.json", self.snapshot)
        self.write_json("Data/catalog-review.json", self.review)
        self.write_json("parameters.json", parameters)
        self.write_json("state.json", self.state)
        result = subprocess.run(
            [POWERSHELL, "-NoProfile", "-NonInteractive", "-File", str(self.root / "harness.ps1")],
            cwd=self.root, env=self.environment, capture_output=True, text=True, timeout=60,
        )
        log = self.root / "calls.jsonl"
        self.calls = [json.loads(line) for line in log.read_text(encoding="utf-8-sig").splitlines()] if log.exists() else []
        self.output = result.stdout + result.stderr
        return result.returncode

    def assert_no_publication(self):
        self.assertFalse(any(call["Arguments"][0] in ("push", "workflow") for call in self.calls), self.calls)

    def test_prepare_stamps_candidate_preserves_input_and_reports_without_git(self):
        self.assertEqual(0, self.run_helper(), self.output)
        candidates = list((self.root / ".local-docs").glob("catalog-updates/*/catalog.json"))
        self.assertEqual(1, len(candidates))
        candidate = json.loads(candidates[0].read_text(encoding="utf-8"))
        self.assertNotEqual(self.snapshot["Version"], candidate["Version"])
        self.assertEqual(self.snapshot["Games"], candidate["Games"])
        self.assertEqual(self.snapshot, json.loads((self.root / "Data/catalog.json").read_text()))
        self.assertTrue(candidates[0].with_name("coverage.json").is_file())
        self.assertEqual([], self.calls)

    def gamepass_collection(self, product_id="MATCHED", complete=True):
        game = self.snapshot["Games"][0]
        game["Reviewed"] = True
        game["Subscriptions"] = [{
            "Service": "PC Game Pass", "Platform": "PC", "Region": "PH", "Included": True,
            "VerifiedAt": "2020-01-01T00:00:00Z", "ExpiresAt": "2020-01-02T00:00:00Z",
            "Url": subscriptions.XBOX_PAGE, "Provenance": "Official subscription catalog; id:MATCHED; reviewed",
        }]
        source = subscriptions.provider_snapshot("PC Game Pass", subscriptions.XBOX_PAGE, [{
            "Id": product_id, "Title": "Example", "Url": subscriptions.XBOX_PAGE,
            "ExpiresAt": (dt.datetime.now(dt.timezone.utc) + dt.timedelta(days=2)).isoformat(),
        }], complete, True, "Official PH PC full-game entitlement")
        with patch.object(catalog, "is_archived", return_value=False):
            collection = subscriptions.propose(self.snapshot, {}, [source])
        self.write_json("collection-fixture.json", collection)
        return collection

    def final_candidate(self):
        path, = (self.root / ".local-docs").glob("catalog-updates/*/catalog.json")
        return path, json.loads(path.read_text(encoding="utf-8"))

    def test_gamepass_refresh_reverifies_existing_matches_and_keeps_sources_unchanged(self):
        collection = self.gamepass_collection()
        self.assertEqual(0, self.run_helper(RefreshGamePass=True), self.output)
        path, candidate = self.final_candidate()
        row = candidate["Games"][0]["Subscriptions"][0]
        self.assertEqual(collection["Candidates"][0]["FetchedAt"], row["VerifiedAt"])
        self.assertEqual(collection["Candidates"][0]["ExpiresAt"], row["ExpiresAt"])
        self.assertEqual(self.snapshot, json.loads((self.root / "Data/catalog.json").read_text()))
        self.assertEqual(self.review, json.loads((self.root / "Data/catalog-review.json").read_text()))
        self.assertTrue(path.with_name("subscription-report.md").is_file())
        self.assertEqual([], json.loads(path.with_name("subscription-candidates.review.json").read_text())["Decisions"])
        self.assert_no_publication()

    def test_gamepass_refresh_accepts_new_provider_id_for_unique_exact_identity(self):
        collection = self.gamepass_collection(product_id="DIFFERENT")
        self.assertEqual(0, self.run_helper(RefreshGamePass=True), self.output)
        _, candidate = self.final_candidate()
        row = candidate["Games"][0]["Subscriptions"][0]
        self.assertTrue(row["Included"])
        self.assertIn("id:DIFFERENT;", row["Provenance"])
        self.assertEqual(collection["Candidates"][0]["FetchedAt"], row["VerifiedAt"])

    def test_gamepass_additions_and_removals_are_applied_automatically(self):
        collection = self.gamepass_collection()
        self.snapshot["Games"].append({"CatalogId": "b", "Name": "New game", "Reviewed": True,
                                       "Genres": [], "Offers": [], "Subscriptions": []})
        collection["Sources"][0]["Products"] = [{"Id": "NEW", "Title": "New game", "Url": subscriptions.XBOX_PAGE}]
        with patch.object(catalog, "is_archived", return_value=False):
            collection = subscriptions.propose(self.snapshot, {}, collection["Sources"])
        self.write_json("collection-fixture.json", collection)
        self.assertEqual({"Added", "Removed"}, {c["Action"] for c in collection["Candidates"]})
        self.assertEqual(0, self.run_helper(RefreshGamePass=True), self.output)
        path, candidate = self.final_candidate()
        self.assertFalse(candidate["Games"][0]["Subscriptions"][0]["Included"])
        self.assertTrue(candidate["Games"][1]["Subscriptions"][0]["Included"])
        review_candidate = json.loads(path.with_name("catalog-review.json").read_text(encoding="utf-8"))
        self.assertFalse(review_candidate["Mappings"]["a"]["Subscriptions"][0]["Included"])
        self.assertTrue(review_candidate["Mappings"]["b"]["Subscriptions"][0]["Included"])
        self.assertEqual(["a", "b"], [game["CatalogId"] for game in candidate["Games"]])
        report = path.with_name("subscription-report.md").read_text(encoding="utf-8")
        self.assertIn("Applied automatically", report)
        self.assertIn("Added", report)
        self.assertIn("Removed", report)

    def test_gamepass_refresh_uses_custom_schema2_snapshot_without_losing_identities(self):
        collection = self.gamepass_collection()
        schema2 = dict(self.snapshot, SchemaVersion=2)
        identity = {"State": "Unresolved", "Apps": [], "ReviewedAt": None, "Provenance": "Pending review"}
        schema2["Games"] = [dict(self.snapshot["Games"][0], SteamIdentity=identity)]
        self.write_json("edited-v2.json", schema2)
        source_path = str(self.root / "edited-v2.json")
        self.assertEqual(0, self.run_helper(Channel="schema2", Catalog=source_path, RefreshGamePass=True), self.output)
        _, candidate = self.final_candidate()
        self.assertEqual(2, candidate["SchemaVersion"])
        self.assertEqual(identity, candidate["Games"][0]["SteamIdentity"])
        self.assertEqual(collection["Candidates"][0]["FetchedAt"], candidate["Games"][0]["Subscriptions"][0]["VerifiedAt"])
        collect, = [call for call in self.calls if call["Command"] == "python"]
        self.assertEqual(source_path, collect["Arguments"][collect["Arguments"].index("--catalog") + 1])

    def test_partial_gamepass_collection_refreshes_positive_evidence_with_warning(self):
        collection = self.gamepass_collection(complete=False)
        self.assertEqual(0, self.run_helper(RefreshGamePass=True), self.output)
        _, candidate = self.final_candidate()
        self.assertEqual(collection["Candidates"][0]["FetchedAt"], candidate["Games"][0]["Subscriptions"][0]["VerifiedAt"])
        self.assertIn("Game Pass list is partial", self.output)

    def test_failed_gamepass_collection_retains_evidence_without_final_candidate(self):
        self.write_json("collection-fixture.json", {
            "CollectedAt": catalog.now(), "Candidates": [], "Ambiguous": [], "Unmatched": [],
            "Sources": [{"Provider": "xbox", "Failed": True}],
        })
        self.assertNotEqual(0, self.run_helper(RefreshGamePass=True))
        self.assertIn("No Philippine PC Game Pass availability was collected", self.output)
        self.assertEqual([], list((self.root / ".local-docs").glob("catalog-updates/*/catalog.json")))
        report, = (self.root / ".local-docs").glob("catalog-updates/*/subscription-report.md")
        self.assertIn("provider failed", report.read_text())
        self.assertEqual(self.snapshot, json.loads((self.root / "Data/catalog.json").read_text()))
        self.assert_no_publication()

    def test_combined_refresh_preserves_gamepass_update_and_refreshes_prices(self):
        collection = self.gamepass_collection()
        self.snapshot["Games"][0]["ItadId"] = "test-itad-id"
        self.environment["ITAD_API_KEY"] = "offline-test-key"
        (self.root / "prices-fixture.py").write_text(
            'import sys\nfrom pathlib import Path\nfrom unittest.mock import patch\n'
            'sys.path.insert(0, str(Path(__file__).parent / "Tools"))\nimport catalog\n'
            'response = [{"id": "test-itad-id", "deals": [{"shop": {"name": "Test store"}, '
            '"url": "https://example.com/game", "platforms": [{"name": "Windows"}], '
            '"price": {"currency": "PHP", "amount": 100}, '
            '"regular": {"currency": "PHP", "amount": 200}, '
            '"timestamp": catalog.now()}]}]\n'
            'sys.argv = ["catalog.py", "prices", *sys.argv[1:]]\n'
            'with patch.object(catalog, "request", return_value=response):\n    catalog.main()\n',
            encoding="utf-8",
        )
        self.assertEqual(0, self.run_helper(RefreshGamePass=True, RefreshPrices=True), self.output)
        _, candidate = self.final_candidate()
        game = candidate["Games"][0]
        self.assertEqual(collection["Candidates"][0]["FetchedAt"], game["Subscriptions"][0]["VerifiedAt"])
        self.assertEqual(100, game["Offers"][0]["SalePrice"])
        self.assertEqual("PHP", game["Offers"][0]["Currency"])
        self.assert_no_publication()

    def test_gamepass_whatif_never_collects(self):
        self.assertEqual(0, self.run_helper(RefreshGamePass=True, WhatIf=True), self.output)
        self.assertEqual([], self.calls)
        self.assertFalse((self.root / ".local-docs").exists())

    def test_gamepass_refresh_cannot_be_combined_with_publish(self):
        self.assertNotEqual(0, self.run_helper(Mode="Publish", RefreshGamePass=True, Ref="codex/catalog-update"))
        self.assertIn("Prepare and review changes separately first", self.output)
        self.assertEqual([], self.calls)

    def test_publish_requires_recorded_release_approval(self):
        self.review["ReleaseApproved"] = False
        self.assertNotEqual(0, self.run_helper(Mode="Publish", Ref="codex/catalog-update"))
        self.assertIn("ReleaseApproved is disabled", self.output)
        self.assert_no_publication()

    def test_uncommitted_catalog_input_stops_before_publication(self):
        self.state["DirtyInputs"] = True
        self.assertNotEqual(0, self.run_helper(Mode="Publish", Ref="codex/catalog-update"))
        self.assertIn("Review and commit catalog/tool changes", self.output)
        self.assert_no_publication()

    def test_publish_requires_enabled_repository_gate(self):
        self.state["Enabled"] = "false"
        self.assertNotEqual(0, self.run_helper(Mode="Publish", Ref="codex/catalog-update"))
        self.assertIn("CATALOG_PUBLISH_ENABLED must already be true", self.output)
        self.assert_no_publication()

    def test_invalid_package_stops_before_push(self):
        self.snapshot["Games"][0]["Name"] = "Not the seeded game"
        self.assertNotEqual(0, self.run_helper(Mode="Publish", Ref="codex/catalog-update"))
        self.assertIn("Catalog must account for every seeded game", self.output)
        self.assert_no_publication()

    def test_changed_head_stops_before_push(self):
        self.state["HeadChanges"] = True
        self.assertNotEqual(0, self.run_helper(Mode="Publish", Ref="codex/catalog-update"))
        self.assertIn("HEAD changed", self.output)
        self.assert_no_publication()

    def test_failed_push_never_dispatches(self):
        self.state["PushFails"] = True
        self.assertNotEqual(0, self.run_helper(Mode="Publish", Ref="codex/catalog-update"))
        self.assertTrue(any(call["Arguments"][0] == "push" for call in self.calls), self.output)
        self.assertFalse(any(call["Arguments"][0] == "workflow" for call in self.calls), self.calls)

    def test_publish_pushes_exact_commit_then_dispatches_correct_channel(self):
        self.assertEqual(0, self.run_helper(Mode="Publish", Ref="codex/catalog-update"), self.output)
        writes = [call for call in self.calls if call["Arguments"][0] in ("push", "workflow")]
        self.assertEqual(["push", "origin", "a" * 40 + ":refs/heads/codex/catalog-update"], writes[0]["Arguments"])
        self.assertEqual(["workflow", "run", "catalog.yml", "--repo", "iamjethrooo/earnings-tracker",
                          "--ref", "codex/catalog-update", "-f", "channel=legacy"], writes[1]["Arguments"])
        self.assertIn("Publication queued", self.output)

    def test_schema2_requires_actual_minimum_version(self):
        self.snapshot["SchemaVersion"] = 2
        self.write_json("Data/catalog-v2.json", self.snapshot)
        self.assertNotEqual(0, self.run_helper(Mode="Publish", Channel="schema2", Ref="codex/catalog-update"))
        self.assertIn("actual compatible released", self.output)
        self.assert_no_publication()

    def test_schema2_publication_passes_minimum_version_to_package_and_workflow(self):
        schema2 = dict(self.snapshot, SchemaVersion=2)
        schema2["Games"] = [dict(self.snapshot["Games"][0], SteamIdentity={
            "State": "Unresolved", "Apps": [], "ReviewedAt": None, "Provenance": "Pending review",
        })]
        self.write_json("Data/catalog-v2.json", schema2)
        self.assertEqual(0, self.run_helper(Mode="Publish", Channel="SCHEMA2",
                                          MinimumClientVersion="1.5.0", Ref="codex/catalog-update"), self.output)
        manifest_path, = (self.root / ".local-docs").glob("catalog-updates/publish-*/manifest.json")
        manifest = json.loads(manifest_path.read_text())
        self.assertEqual(2, manifest["SchemaVersion"])
        self.assertEqual("1.5.0", manifest["MinimumClientVersion"])
        dispatch, = [call for call in self.calls if call["Arguments"][0] == "workflow"]
        self.assertEqual(["-f", "channel=schema2", "-f", "minimum_client_version=1.5.0"], dispatch["Arguments"][-4:])

    def test_whatif_does_not_push_dispatch_or_package(self):
        self.assertEqual(0, self.run_helper(Mode="Publish", Ref="codex/catalog-update", WhatIf=True), self.output)
        self.assert_no_publication()
        self.assertFalse((self.root / ".local-docs").exists())


if __name__ == "__main__":
    unittest.main()
