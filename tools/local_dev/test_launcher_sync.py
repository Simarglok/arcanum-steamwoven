"""The ordinary launcher must stay in step with the viewer's argument parser and pinned inputs."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from tools.local_dev import config, launch

REPO = Path(__file__).resolve().parents[2]
# Viewer flags the ordinary launcher deliberately never passes (caller-side hash expectations).
NON_LAUNCHER_FLAGS = {"--expect-module-sha256", "--expect-resource-sha256", "--expect-actor-resource-sha256"}


class LauncherSyncTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name).resolve()

    def tearDown(self):
        self.temp.cleanup()

    def inputs(self) -> dict[str, str]:
        result = {"sourceId": "public-fixture"}
        for role, (relative, size, _) in launch.GAME_ROLES.items():
            path = self.root / "roles" / role
            if size is None:
                path.mkdir(parents=True, exist_ok=True); (path / "fixture.pro").write_bytes(b"x")
            else:
                path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"public fixture")
            result[role] = str(path)
        return result

    def test_every_viewer_flag_is_passed_or_explicitly_excluded(self):
        source = (REPO / "tools/terrain-viewer/Core/TerrainViewerArguments.cs").read_text()
        viewer = set(re.findall(r'case "(--[a-z0-9-]+)"', source))
        passed = set()
        for mode in launch.MODE_ROLES:
            argv = launch.terrain_arguments(self.inputs(), mode)
            passed |= {token for token in argv if token.startswith("--")}
            for role in launch.MODE_ROLES[mode]:
                self.assertIn(launch.ROLE_FLAGS[role], argv, f"{mode} must pass {role}")
        passed |= {token for token in launch.terrain_arguments(self.inputs(), "actor-diagnostic", {}) if token.startswith("--")}
        self.assertEqual(viewer, passed | NON_LAUNCHER_FLAGS,
                         "a viewer flag was added or removed: update GAME_ROLES/MODE_ROLES/ROLE_FLAGS and terrain_arguments")
        self.assertTrue(set(launch.ROLE_FLAGS.values()) <= viewer)
        self.assertEqual(set(launch.ROLE_FLAGS), set(launch.GAME_ROLES))
        self.assertTrue(all(set(roles) <= set(launch.GAME_ROLES) for roles in launch.MODE_ROLES.values()))

    def test_viewer_modes_match_the_launcher_modes(self):
        source = (REPO / "tools/terrain-viewer/Core/TerrainViewerArguments.cs").read_text()
        modes = set(re.findall(r'"([a-z-]+)" => TerrainViewerMode\.', source))
        self.assertEqual(modes, set(launch.MODE_ROLES), "a viewer mode was added or removed: update MODE_ROLES")

    def test_role_identities_match_the_data_layer_and_evidence_pins(self):
        profile = (REPO / "src/Arcanum.Data/Area/OriginalProfile.cs").read_text()
        for role in ("module", "actorResource", "terrainResource", "areaMobile"):
            _, size, sha256 = launch.GAME_ROLES[role]
            self.assertIn(f"{size}, \"{sha256}\"", profile, role)
        inventory = (REPO / "docs/research/crash-site-area-inventory.md").read_text()
        _, size, sha256 = launch.GAME_ROLES["areaEffects"]
        self.assertIn(f"| {size} | `{sha256}` |", inventory)
        dat = (REPO / "docs/research/dat-evidence.md").read_text()
        _, size, sha256 = launch.GAME_ROLES["areaPatch"]
        self.assertIn(f"| {size:,} | `{sha256}` |", dat)

    def test_project_launcher_modes_bind_known_modes_and_tasks(self):
        modes = runpy.run_path(str(REPO / "arcanum"), run_name="arcanum_module")["MODES"]
        # Same pairing arcanum-local enforces at launch: crash-site-area only from ARC-RENDER-007.
        allowed = {"crash-site-area": {"ARC-RENDER-007"},
                   "world-diagnostic": {"ARC-WORLD-001", "ARC-GPAD-004", "ARC-RENDER-006", "ARC-WORLD-003"}}
        for mode, task in modes.values():
            self.assertIn(mode, launch.MODE_ROLES)
            self.assertIn(task, allowed[mode], mode)

    def test_discovery_verifies_identity_and_rejects_links(self):
        game = self.root / "game"
        fixture = {"module": b"module", "areaMobile": b"mobile"}
        roles = {role: (f"files/{role}.bin", len(data), hashlib.sha256(data).hexdigest()) for role, data in fixture.items()}
        roles["prototypes"] = ("proto", None, None)
        for role, data in fixture.items():
            (game / "files").mkdir(parents=True, exist_ok=True); (game / roles[role][0]).write_bytes(data)
        (game / "proto").mkdir(); (game / "proto" / "a.pro").write_bytes(b"x")
        with patch.object(launch, "GAME_ROLES", roles):
            found, missing = launch.discover_inputs(game)
            self.assertEqual((sorted(found), missing), (["areaMobile", "module", "prototypes"], []))
            (game / roles["module"][0]).write_bytes(b"changed")
            self.assertEqual(launch.discover_inputs(game)[1], ["module"])
            (game / roles["module"][0]).unlink(); (game / roles["module"][0]).symlink_to(game / roles["areaMobile"][0])
            self.assertIn("module", launch.discover_inputs(game)[1])
            (game / "proto" / "a.pro").unlink()
            self.assertIn("prototypes", launch.discover_inputs(game)[1])

    def test_write_inputs_fails_closed_without_every_role(self):
        home = self.root / "home"; home.mkdir(); game = self.root / "empty-game"; game.mkdir()
        env = dict(os.environ, ARCANUM_LOCAL_HOME=str(home))
        completed = subprocess.run([sys.executable, "-B", str(REPO / "tools/arcanum-local"), "configure", "discover",
                                    "--game-root", str(game), "--write-inputs"], env=env, capture_output=True, text=True)
        self.assertNotEqual(completed.returncode, 0)
        conf = json.loads((home / "config-v1.json").read_text()) if (home / "config-v1.json").exists() else config.default_config()
        self.assertEqual(conf["inputs"], {})


if __name__ == "__main__":
    unittest.main()
