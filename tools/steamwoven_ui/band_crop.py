"""ARC-UI-3083: crops the padded band out of a Codex result for the extreme aspects (the strips, 19.5:1 and 5:1).

For an extreme aspect the generator does not return a canvas at the asked aspect: it keeps a roughly 2.8:1 canvas (2103 x 748) and draws the piece at the
right proportion inside it, with white or black bars above and below (docs/ui/steamwoven-surface-method-study.md, "Codex at the extreme aspects"). This
module turns such a canvas into the band and resizes it to the zone:

1. a fuzz trim removes the bars (the colour of the canvas corner, 4 % fuzz);
2. a shave of 2 to 4 px at each trimmed edge removes the light anti-aliasing line the bars leave on the band (without it the seam at the join rises to 12.8);
   the shave inside that range is the one that brings the band closest to the asked aspect, and the columns are cropped at both sides when the band is still
   wider than the asked aspect (so the band is at the asked aspect and never stretched);
3. the band's aspect must be within 1 % of the asked one (SWI-GEN-8), else the crop is refused (`CropError`) and the item stays `wrong-aspect`;
4. the band is resized to the zone size with a Lanczos filter (when a zone size is given).

The geometry (`plan`) is pure Python; the pixels go through ImageMagick (`magick`), as in the sibling tools of this folder (`surface_compose.py`, `method_c.py`).
Reads and writes local files only; no art in git.
"""
from __future__ import annotations

import hashlib
import re
import shutil
import subprocess
from pathlib import Path

DEFAULT_FUZZ = 4.0  # percent: the bars are flat colour, the art is not
MIN_SHAVE = 2  # px per trimmed edge: the least that removes the light line
MAX_SHAVE = 4  # px per trimmed edge: the most the study shaved
TOLERANCE = 0.01  # SWI-GEN-8: the band's aspect is within 1 % of the asked one
MAX_COLUMN_CUT = 0.02  # the columns cut at both sides to land on the aspect are at most 2 % of the band's width, else the art itself has another aspect
MIN_PADDING = 0.05  # each bar is at least 5 % of the canvas height, else nothing was padded and the canvas is simply the wrong aspect
FILTER = "Lanczos"
MAGICK_TIMEOUT = 120


class CropError(Exception):
    """The canvas cannot be cropped to the asked aspect; the item stays `wrong-aspect` with this reason."""


def parse_box(text: str) -> tuple[int, int, int, int]:
    """`WxH+X+Y` (the `%@` of ImageMagick, the trim bounding box) -> (x, y, w, h)."""
    found = re.fullmatch(r"(\d+)x(\d+)\+(\d+)\+(\d+)", text.strip())
    if not found:
        raise CropError(f"no trim box in {text.strip()[:60]!r}")
    w, h, x, y = (int(v) for v in found.groups())
    return x, y, w, h


def plan(canvas: tuple[int, int], box: tuple[int, int, int, int], asked: list[int] | tuple[int, int], tolerance: float = TOLERANCE,
         min_shave: int = MIN_SHAVE, max_shave: int = MAX_SHAVE) -> dict:
    """The crop rectangle (x, y, w, h) inside the canvas for a trim box and an asked aspect; pure geometry.

    Only a wide band (asked aspect above 1) with bars above and below is cropped; the bars at the left and right (when the trim found some) are shaved by
    `min_shave`. The rectangle's aspect is within `tolerance` of the asked one, else `CropError`.
    """
    cw, ch = canvas
    x, y, w, h = box
    want = asked[0] / asked[1]
    if want <= 1:
        raise CropError(f"the asked aspect {asked[0]}:{asked[1]} is not a wide band; only strips are cropped")
    top, bottom, left, right = y, ch - (y + h), x, cw - (x + w)
    if min(top, bottom) < ch * MIN_PADDING:
        raise CropError(f"no bars above and below the band (the trim left {top} px above and {bottom} px below of {ch}); the canvas is the wrong aspect, not padded")
    shave_x_left = min_shave if left > 0 else 0
    shave_x_right = min_shave if right > 0 else 0
    x1, w1 = x + shave_x_left, w - shave_x_left - shave_x_right
    if w1 <= 0 or h <= 2 * min_shave:
        raise CropError(f"the band {w} x {h} is too small to shave")
    ideal = w1 / want
    total = max(2 * min_shave, min(2 * max_shave, h - round(ideal)))  # the vertical shave that brings the band closest to the asked aspect
    h1 = h - total
    if h1 <= 0:
        raise CropError(f"the band {w} x {h} is too small to shave")
    aspect = w1 / h1
    w2, x2 = w1, x1
    if aspect > want * (1 + 1e-4):  # wider than asked: cut equal columns at both sides, never stretch
        w2 = round(h1 * want)
        if w1 - w2 > w1 * MAX_COLUMN_CUT:
            raise CropError(f"the band {w} x {h} is much wider than the asked aspect {asked[0]}:{asked[1]} after the shave (it would cut {w1 - w2} of {w1} columns)")
        x2 = x1 + (w1 - w2) // 2
        aspect = w2 / h1
    error = aspect / want - 1
    if abs(error) > tolerance:
        raise CropError(f"the band {w} x {h} cannot reach the asked aspect {asked[0]}:{asked[1]} within {tolerance:.0%} with a shave of at most {max_shave} px per edge "
                        f"(best {w2} x {h1}, aspect error {error:+.2%})")
    top_shave = (total + 1) // 2
    return {"rect": [x2, y + top_shave, w2, h1], "trim_box": [x, y, w, h], "canvas": [cw, ch], "bars": {"top": top, "bottom": bottom, "left": left, "right": right},
            "shave": {"top": top_shave, "bottom": total - top_shave, "left": shave_x_left, "right": shave_x_right},
            "columns_cut": w1 - w2, "band": [w2, h1], "band_aspect": round(aspect, 5), "asked_aspect": round(want, 5), "aspect_error": round(error, 5)}


def run_magick(args: list[str]) -> str:
    magick = shutil.which("magick")
    if not magick:
        raise CropError("ImageMagick (`magick`) is needed for the band crop and was not found")
    result = subprocess.run([magick, *args], capture_output=True, text=True, timeout=MAGICK_TIMEOUT)
    if result.returncode != 0:
        raise CropError(f"magick failed: {result.stderr.strip()[:300]}")
    return result.stdout


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def magick_version() -> str:
    first = run_magick(["-version"]).splitlines()[0] if shutil.which("magick") else "unknown"
    return " ".join(first.split()[:3])


def crop_file(source: Path, out: Path, canvas: tuple[int, int], asked: list[int] | tuple[int, int], size: list[int] | tuple[int, int] | None = None,
              fuzz: float = DEFAULT_FUZZ, min_shave: int = MIN_SHAVE, max_shave: int = MAX_SHAVE) -> dict:
    """Crops the band of `source` (a PNG of pixel size `canvas`), resizes it to `size` (when given) and writes `out`; returns the report for the provenance record."""
    box = parse_box(run_magick([str(source), "-fuzz", f"{fuzz:g}%", "-format", "%@", "info:"]))
    geometry = plan(canvas, box, asked, TOLERANCE, min_shave, max_shave)
    x, y, w, h = geometry["rect"]
    command = [str(source), "-crop", f"{w}x{h}+{x}+{y}", "+repage"]
    if size:
        command += ["-filter", FILTER, "-resize", f"{size[0]}x{size[1]}!"]
    out.parent.mkdir(parents=True, exist_ok=True)
    temp = out.with_name(out.name + ".part.png")
    run_magick([*command, "-strip", f"PNG32:{temp}"])  # 8 bit RGBA always (a flat band would otherwise come out as a palette PNG that pngio does not read)
    temp.replace(out)
    return {**geometry, "fuzz_percent": fuzz, "shave_range": [min_shave, max_shave], "resized_to": list(size) if size else None, "filter": FILTER if size else None,
            "tool": f"ImageMagick {magick_version()}", "raw_sha256": sha256_file(source), "cropped_sha256": sha256_file(out)}


def describe(report: dict) -> str:
    shave = report["shave"]
    return (f"band {report['band'][0]} x {report['band'][1]} from the {report['canvas'][0]} x {report['canvas'][1]} canvas (bars {report['bars']['top']}/{report['bars']['bottom']} px, "
            f"shave {shave['top']}/{shave['bottom']} px, columns cut {report['columns_cut']}), aspect error {report['aspect_error']:+.2%}"
            + (f", resized to {report['resized_to'][0]} x {report['resized_to'][1]}" if report["resized_to"] else ""))
