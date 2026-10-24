"""Regression for host/container UID differences during temporary cleanup."""
import os
import signal
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import runner


class RunnerTests(unittest.TestCase):
    def test_scan_containers_disable_bytecode_in_shared_directory(self):
        commands = []

        def run(command, **kwargs):
            commands.append(command)
            return type("Result", (), {"returncode": 2})()

        previous_umask = os.umask(0o077)
        previous_handler = signal.getsignal(signal.SIGTERM)
        try:
            with tempfile.TemporaryDirectory() as output:
                with patch.dict(os.environ, {"ZAP_OUTPUT_DIR": output}), \
                     patch.object(runner, "prepare", return_value=[]), \
                     patch.object(runner, "proxy_port", return_value=18080), \
                     patch.object(runner, "evaluate", return_value={"passed": True}), \
                     patch.object(runner.subprocess, "run", side_effect=run):
                    self.assertEqual(runner.main(), 0)
            scans = [command for command in commands if command[:2] == ["docker", "run"]]
            self.assertEqual(len(scans), 2)
            for command in scans:
                env_values = [command[i + 1] for i, value in enumerate(command) if value == "--env"]
                self.assertIn("PYTHONDONTWRITEBYTECODE=1", env_values)
                mount = command[command.index("--volume") + 1].split(":")[0]
                self.assertFalse(Path(mount).exists(), "Private directory must be removed")
        finally:
            os.umask(previous_umask)
            signal.signal(signal.SIGTERM, previous_handler)


if __name__ == "__main__":
    unittest.main()
