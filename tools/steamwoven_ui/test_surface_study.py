"""ARC-RES-2457: tests of the surface study planners and command builders (no ImageMagick run, no art)."""
from __future__ import annotations

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

from . import method_c, surface_study


class SurfaceStudyTests(unittest.TestCase):
    def test_zones_cover_the_composition_exactly(self):
        zones = list(surface_study.ZONES.values())
        self.assertEqual(zones[0][1], 0)
        for a, b in zip(zones, zones[1:]):
            self.assertEqual(a[1] + a[3], b[1])
        self.assertEqual(zones[-1][1] + zones[-1][3], surface_study.MASTER[1])
        self.assertTrue(all(z[0] == 0 and z[2] == surface_study.MASTER[0] for z in zones))

    def test_tiles_cover_the_window_zone_with_overlaps(self):
        rects = surface_study.tile_rects()
        window_w, window_h = surface_study.ZONES["window"][2:4]
        self.assertEqual(len(rects), surface_study.TILE_COLUMNS * surface_study.TILE_ROWS)
        self.assertEqual(max(r["x"] + r["w"] for r in rects), window_w)
        self.assertEqual(max(r["y"] + r["h"] for r in rects), window_h)
        self.assertEqual(min(r["x"] for r in rects), 0)
        self.assertEqual(min(r["y"] for r in rects), 0)
        self.assertEqual(len({r["id"] for r in rects}), len(rects))
        first = rects[0]
        self.assertEqual((first["left"], first["top"]), (0, 0))
        for r in rects[1:]:
            self.assertTrue(r["left"] > 0 or r["top"] > 0)
        # every point of the zone is inside some tile
        for x in (0, 1319, 1320, 1559, 2879):
            for y in (0, 359, 360, 1079, 1439):
                self.assertTrue(any(r["x"] <= x < r["x"] + r["w"] and r["y"] <= y < r["y"] + r["h"] for r in rects), (x, y))

    def test_ramp_mask_command_uses_the_overlaps(self):
        rect = {"w": 100, "h": 50, "left": 20, "top": 0}
        command = surface_study.ramp_mask_command(rect, Path("m.png"))
        self.assertIn("min(1,(i+0.5)/20)", command)
        both = surface_study.ramp_mask_command({"w": 100, "h": 50, "left": 20, "top": 10}, Path("m.png"))
        self.assertIn("min(min(1,(i+0.5)/20),(j+0.5)/10)", both)
        none = surface_study.ramp_mask_command({"w": 100, "h": 50, "left": 0, "top": 0}, Path("m.png"))
        self.assertIn("1", none)

    def test_prompts_are_ascii_and_have_the_size(self):
        zone = surface_study.ZONE_BODY.format(w=2880, h=148, aspect="19.5", what="the top strip", screen_name="inventory")
        tile = surface_study.TILE_BODY.format(w=1560, h=1080)
        for text in (zone, tile, surface_study.COMMON.read_text(encoding="utf-8")):
            text.encode("ascii")
        self.assertIn("2880 x 148", zone)
        self.assertIn("1560 x 1080", tile)

    def test_zones_inbox_refuses_an_unknown_zone(self):
        with self.assertRaises(surface_study.StudyError):
            surface_study.zones_inbox(Path("o.png"), Path("local"), zones=("middle",))

    def test_names_that_leave_the_inbox_or_the_prompt_charset_are_refused(self):
        for batch, screen in (("../x", "logbook"), ("a/b", "logbook"), (".hidden", "logbook"), ("ok", "log book"), ("ok", "k\u00e4se"), ("ok", "../x")):
            with self.assertRaises(surface_study.StudyError, msg=(batch, screen)):
                surface_study.zones_inbox(Path("o.png"), Path("local"), batch=batch, zones=("window",), screen_name=screen)

    def test_assemble_refuses_a_misspelled_zone_and_main_refuses_a_zone_without_a_file(self):
        with self.assertRaises(surface_study.StudyError):
            surface_study.assemble({"top-strip": Path("a"), "window": Path("b"), "bottom-strip": Path("c"), "windw": Path("d")}, Path("out.png"))
        self.assertEqual(surface_study.main(["assemble", "--zone", "window", "--out", "o.png"]), 2)
        self.assertEqual(surface_study.main(["assemble", "--zone", "window=", "--out", "o.png"]), 2)

    def test_assemble_needs_every_zone(self):
        with self.assertRaises(surface_study.StudyError):
            surface_study.assemble({"window": Path("w.png")}, Path("out.png"))

    @unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
    def test_zones_inbox_takes_a_subset_and_names_the_screen_and_assemble_places_the_zones(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            subprocess.run(["magick", "-size", "2880x2160", "xc:gray50", str(root / "o.png")], check=True)
            info = surface_study.zones_inbox(root / "o.png", root, batch="fam-test", zones=("window",), screen_name="logbook")
            self.assertEqual(info["items"], 1)
            batch = json.loads((root / "inbox" / "fam-test" / "batch.json").read_text(encoding="utf-8"))
            self.assertEqual([i["id"] for i in batch["items"]], ["window"])
            self.assertEqual(batch["after"], "the upscaled ORIGINAL of the logbook screen")
            prompt = (root / "inbox" / "fam-test" / "prompts" / "window.txt").read_text(encoding="utf-8")
            self.assertIn("logbook", prompt)
            self.assertNotIn("inventory", prompt)
            for name, colour, size in (("top", "red", "2103x107"), ("win", "green", "1774x887"), ("bot", "blue", "2103x417")):
                subprocess.run(["magick", "-size", size, f"xc:{colour}", str(root / f"{name}.png")], check=True)
            out = root / "a.png"
            surface_study.assemble({"top-strip": root / "top.png", "window": root / "win.png", "bottom-strip": root / "bot.png"}, out)
            read = lambda x, y: subprocess.run(["magick", str(out), "-format", f"%[pixel:p{{{x},{y}}}]", "info:"], capture_output=True, text=True).stdout
            self.assertEqual(subprocess.run(["magick", "identify", "-format", "%wx%h", str(out)], capture_output=True, text=True).stdout, "2880x2160")
            self.assertLessEqual(out.read_bytes()[24], 8)  # IHDR bit depth: surface_compose reads 8 bit only, never the Q16 of xc:black
            self.assertEqual(read(100, 10), "srgb(255,0,0)")
            self.assertEqual(read(100, 600), "srgb(0,128,0)")
            self.assertEqual(read(100, 2000), "srgb(0,0,255)")

    def test_method_c_commands(self):
        tile = method_c.tile_command(Path("t.png"), 432, (2880, 2160), Path("o.png"))
        self.assertIn("432x432!", tile)
        self.assertIn("2880x2160", tile)
        self.assertIn("compose:args=0,-1,1,0.5", method_c.high_pass_command(Path("a.png"), 6.0, Path("o.png")))
        self.assertIn("compose:args=0,1,1,-0.5", method_c.add_command(Path("l.png"), Path("h.png"), Path("o.png")))
        self.assertEqual(method_c.low_pass_command(Path("a.png"), 6.0, Path("o.png")), ["magick", "a.png", "-blur", "0x6.0", "o.png"])


if __name__ == "__main__":
    unittest.main()
