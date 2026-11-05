"""ARC-DEV-125 (MIG12 S6): the systems that decide rules over time read the session's frame clocks, never Godot time.

docs/architecture/clocks-frame.md section 3.3: "Nothing in the rules reads Godot time". The viewer's one reader of the host's time for the
world is the clock source in TerrainViewerWorld.cs (the fallback where no frame runs) and the frame stamp TickAreaClock hands to the frame
step; the files below hold the rules that read the world clock and must hold no host-time call of their own.
"""
from __future__ import annotations

from pathlib import Path
import re
import unittest

REPO = Path(__file__).resolve().parents[2]
GODOT = REPO / "tools" / "terrain-viewer" / "Godot"
# The hero's attack and cast, projectiles, spell missiles, eye candy, dying and lying animations, body moves, blood pools, scenery,
# the hero's knockdown, the light animation and the world clock's reader of the frame (TerrainViewerAreaLighting.cs also holds the one
# host stamp the frame step is given, allowed below).
RULE_FILES = (
    "TerrainViewerAttack.cs", "TerrainViewerSpellCast.cs", "TerrainViewerProjectiles.cs", "TerrainViewerSpellMissiles.cs",
    "TerrainViewerEyeCandy.cs", "TerrainViewerBodyMoves.cs", "TerrainViewerBloodPools.cs", "TerrainViewerDyingAnimation.cs",
    "TerrainViewerHeroKnockdown.cs", "TerrainViewerSceneryAnimation.cs",
)
HOST_CALL = re.compile(r"\bTime\.GetTicks(?:Msec|Usec)\b")
CODE_LINE = re.compile(r"^\s*(?!//)")


def code_lines(path: Path):
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if CODE_LINE.match(line) and not line.lstrip().startswith(("///", "//")):
            yield number, line


class WorldClockSyncTests(unittest.TestCase):
    def test_rule_files_hold_no_host_time_call(self):
        for name in RULE_FILES:
            path = GODOT / name
            self.assertTrue(path.is_file(), name)
            hits = [f"{name}:{n}: {line.strip()}" for n, line in code_lines(path) if HOST_CALL.search(line)]
            self.assertEqual([], hits, "a rule reads the session's frame clocks (WorldMilliseconds), not Godot time")

    def test_the_world_time_reads_the_host_only_in_the_clock_source(self):
        # TerrainViewerWorld.cs: the clock source (WorldTime's fallback, the AI host's own clock until S5) and the restore of a smoke's
        # time; TerrainViewerPacing.cs: frame pacing, a presentation matter.
        allowed = {"TerrainViewerWorld.cs", "TerrainViewerPacing.cs"}
        found = {p.name for p in GODOT.glob("*.cs") if any(re.search(r"\bTime\.GetTicksUsec\b", line) for _, line in code_lines(p))}
        self.assertEqual(set(), found - allowed, "the microsecond host clock is the world clock's fallback only")

    def test_the_frame_participant_is_registered_where_a_driver_is_bound(self):
        lighting = (GODOT / "TerrainViewerAreaLighting.cs").read_text(encoding="utf-8")
        self.assertEqual(2, lighting.count("BindFrameParticipants(_areaAmbient);"), "every driver the viewer builds registers the frame tap")
        self.assertIn("frame.Register(_frameTap);", lighting)
        # ARC-DEV-124 (S5): the AI host is the other participant, registered with the tap, and a map drawn unlit binds its own frame driver the same way.
        self.assertIn("frame.Register(AiFrameParticipant);", lighting)
        self.assertIn("BindFrameParticipants(driver.Frame);", lighting)
        self.assertEqual(3, lighting.count("PingNpcChangeOfPlace)"), "every frame step opens with the NPCs' change-of-place ping")

    def test_the_ai_host_runs_on_the_frame_clocks(self):
        # ARC-DEV-124 (S5): the host steps in the frame step on the frame's world milliseconds and the viewer draws its movers on the host's clock.
        text = (GODOT / "TerrainViewerAi.cs").read_text(encoding="utf-8")
        self.assertNotIn("host.Pump(", text, "the frame step pumps the host (AreaAiFrameParticipant), not the viewer")
        self.assertNotIn("AiWorldMilliseconds", text)
        self.assertIn("host.NowMs", text)
        for path in GODOT.glob("*.cs"):
            self.assertNotIn("AiWorldMilliseconds", path.read_text(encoding="utf-8"), path.name)
        # What an AI notice starts in a consumer (the missile and its eye candy) runs on the world clock, never the AI host's stamp.
        self.assertIn("LaunchNpcSpellMissile(notice, WorldMilliseconds())", text)
        self.assertNotRegex(text, r"LaunchNpcSpellMissile\(notice, now\)")


if __name__ == "__main__":
    unittest.main()
