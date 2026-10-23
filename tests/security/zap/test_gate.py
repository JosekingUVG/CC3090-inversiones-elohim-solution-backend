"""Offline regressions: incomplete/invalid security evidence must never pass."""
import copy
import json
import tempfile
import unittest
from pathlib import Path

from common import assert_response, local_target, scrub, write_json
from evaluate import evaluate
from prepare import filtered_openapi

ROOT = Path(__file__).resolve().parent


class GateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name)
        self.policy = json.loads((ROOT / "policy.json").read_text())
        self.scope = json.loads((ROOT / "scope.json").read_text())
        for profile in self.policy["profiles"]:
            ops = self.scope["profiles"][profile]
            write_json(self.output / f"execution-{profile}.json", {"exitCode": 2})
            write_json(self.output / f"preparation-{profile}.json", {"complete": True})
            write_json(self.output / f"coverage-{profile}.json", {
                "profile": profile, "activeScanStarted": True, "activeScanCompleted": True,
                "passiveScanCompleted": True, "historyCollected": True,
                "scans": [{"progress": "100"}], "activeRules": [{"id": "40018"}], "activeMessageCount": 10,
                "baseline": [{"method": o["method"], "path": o["path"], "status": o["status"],
                              "businessAssertionPassed": True} for o in ops],
                "observed": [{"method": o["method"], "path": o["path"], "status": o["status"], "count": 1} for o in ops]})
            write_json(self.output / f"report-{profile}.json", {"site": [{"alerts": []}]})
            (self.output / f"report-{profile}.html").write_text("<html>test report</html>")

    def gate(self):
        return evaluate(self.output, self.policy, self.scope)

    def modify(self, name, callback):
        path = self.output / name
        value = json.loads(path.read_text())
        callback(value)
        write_json(path, value)

    def test_warning_exit_can_pass_with_complete_evidence(self):
        self.assertTrue(self.gate()["passed"])

    def test_high_and_project_specific_alerts_block(self):
        for risk, plugin in ((3, "99999"), (2, "40018")):
            with self.subTest(risk=risk, plugin=plugin):
                write_json(self.output / "report-admin.json", {"site": [{"alerts": [
                    {"riskcode": str(risk), "pluginid": plugin, "name": "Synthetic finding"}]}]})
                self.assertFalse(self.gate()["passed"])

    def test_scanner_errors_and_timeouts_block(self):
        for code in (1, 3, 124, 125, 130):
            with self.subTest(code=code):
                write_json(self.output / "execution-admin.json", {"exitCode": code})
                self.assertFalse(self.gate()["passed"])

    def test_missing_report_blocks(self):
        (self.output / "report-client.html").unlink()
        self.assertFalse(self.gate()["passed"])

    def test_empty_or_invalid_report_blocks(self):
        for content in ('{}', 'not JSON', '{"site": []}', '{"site": [{}]}'):
            with self.subTest(content=content):
                (self.output / "report-client.json").write_text(content)
                self.assertFalse(self.gate()["passed"])

    def test_denied_baseline_blocks(self):
        self.modify("coverage-client.json", lambda x: x["baseline"][0].update(status=401))
        self.assertFalse(self.gate()["passed"])

    def test_unobserved_operation_blocks(self):
        self.modify("coverage-client.json", lambda x: x.update(observed=[]))
        self.assertFalse(self.gate()["passed"])

    def test_unfinished_scan_blocks(self):
        self.modify("coverage-admin.json", lambda x: x.update(scans=[{"progress": "99"}]))
        self.assertFalse(self.gate()["passed"])

    def test_passive_timeout_blocks(self):
        self.modify("coverage-admin.json", lambda x: x.update(passiveScanCompleted=False))
        self.assertFalse(self.gate()["passed"])

    def test_no_active_requests_blocks(self):
        self.modify("coverage-admin.json", lambda x: x.update(activeMessageCount=0))
        self.assertFalse(self.gate()["passed"])

    def test_wrong_business_data_blocks(self):
        with self.assertRaises(AssertionError):
            assert_response([{"id": "other-tenant"}], {"contains": [{"id": "expected"}]})

    def test_secret_scrubbing(self):
        self.assertEqual(scrub("Bearer abc secret", ["abc", "secret"]), "Bearer [REDACTED] [REDACTED]")
        self.assertNotIn("eyJ", scrub("eyJhbGciOiJIUzI1NiJ9.abc.signature"))

    def test_external_targets_rejected(self):
        for url in ("https://example.com", "http://localhost:5000@evil.test:80", "http://localhost:5000/api"):
            with self.assertRaises(ValueError):
                local_target(url)

    def test_openapi_restricts_operations_and_server_overrides(self):
        doc = {"openapi": "3.0.1", "servers": [{"url": "https://external.test"}],
               "paths": {"/api/v1/productos/{id}": {
                   "servers": [{"url": "https://external.test"}],
                   "get": {"servers": [{"url": "https://external.test"}],
                           "parameters": [{"in": "path", "name": "id", "schema": {"type": "string"}}]},
                   "delete": {}}}}
        original = copy.deepcopy(doc)
        ops = [{"path": "/api/v1/productos/{id}", "url": "/api/v1/productos/fixture", "method": "GET"}]
        result = filtered_openapi(doc, ops, "http://localhost:5000")
        path = result["paths"][ops[0]["path"]]
        self.assertNotIn("delete", path)
        self.assertNotIn("servers", path)
        self.assertNotIn("servers", path["get"])
        self.assertEqual(path["get"]["parameters"][0]["example"], "fixture")
        self.assertEqual(doc, original)
        with self.assertRaises(ValueError):
            filtered_openapi(doc, [{**ops[0], "method": "PUT"}], "http://localhost:5000")


if __name__ == "__main__":
    unittest.main()
