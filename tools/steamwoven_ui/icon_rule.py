#!/usr/bin/env python3
"""ARC-UI-2485: icon frames for the HD pack by RULE, from the ORIGINAL's own frames (no Codex, no image model). Standard library only.

Two jobs, both writing the `<art>-fNNN.png` frames and the `<art>.assembly.json` record that `pack_plan.py` turns into an importer plan:

* `ghost` (Part A, a repair of the installed pack's frame): the pack's redraw of an icon with a black background keeps faint outlines of the redraw sheet's
  neighbouring icons ("ghosts") on that black. The frame is kept; where the ORIGINAL frame is black the pack's pixels are replaced by the original's
  black (a soft edge of about one HD pixel), so the picture is the pack's everywhere the original has content. `check` prints the audit's ghost share.
* `upscale` (Part B, an art the pack cannot serve): the original's frame at x`scale`. `square` is the plain crisp copy (every original pixel is one
  scale x scale block: the square dots and the one-pixel lines of the indicators stay exact); `smooth` smooths the COLOUR between the cells (bilinear,
  premultiplied by the cell's alpha, so a transparent cell never darkens an edge) and keeps the original's alpha exactly. The importer cuts every HD alpha to the texels the
  original leaves opaque (`HdAlphaMasks.Resolve`), so a rule cannot grow a silhouette beyond it; the exact alpha is what is written.

Frames are derived from the player's own art: write them only under `local/` (never committed). The tool refuses an output folder inside the checkout other than `local/`.

    python3 -B tools/steamwoven_ui/icon_rule.py check  --frames FRAMES --pack PACK ART...
    python3 -B tools/steamwoven_ui/icon_rule.py ghost  --frames FRAMES --pack PACK --out DIR ART...
    python3 -B tools/steamwoven_ui/icon_rule.py upscale --frames FRAMES --out DIR [--mode square|smooth] ART...

`FRAMES` is the census frame folder (`<art number or x>-<name>/f<N>.png`), `PACK` the installed (or staged) pack folder whose `index.tsv` names the pack's frames,
`ART` a census art name.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import sys
from pathlib import Path

try:
    from tools.steamwoven_ui import census, pngio
    from tools.steamwoven_ui.pngio import Image
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import census  # type: ignore[no-redef]
    import pngio  # type: ignore[no-redef]
    from pngio import Image  # type: ignore[no-redef]

REPO = Path(__file__).resolve().parents[2]
SCALE = 4
DARK = 14.0  # an original pixel darker than this (luma 0..255) is background black (the audit's rule)
GHOST_LIGHT = 30.0  # a pack pixel lighter than this inside the black is a ghost
GHOST_MAX_PERCENT = 0.3  # acceptance: the ghost share of the background after the repair
MARGIN = 2  # HD pixels the cleared region keeps back from the original's content
FEATHER = 3.0  # sigma, HD pixels, that rounds the cleared region's outline (an original pixel is 4 HD pixels, so its stair is smoothed away)
EDGE = (0.3, 0.7)  # the smooth step on the blurred region: weight 0 below, 1 above
MODES = ("square", "smooth")


class RuleError(Exception):
    """An input the rule cannot use; the message names it."""


# --- small helpers ------------------------------------------------------------------------------------------------------

def luma(r: int, g: int, b: int) -> float:
    return 0.299 * r + 0.587 * g + 0.114 * b


def erode(mask: list[bool], w: int, h: int, times: int = 1) -> list[bool]:
    """Cross-shaped erosion, the outside counts as empty (the audit's `binary_erosion`)."""
    for _ in range(times):
        out = [False] * (w * h)
        for y in range(h):
            for x in range(w):
                i = y * w + x
                if mask[i] and x > 0 and y > 0 and x < w - 1 and y < h - 1 and mask[i - 1] and mask[i + 1] and mask[i - w] and mask[i + w]:
                    out[i] = True
        mask = out
    return mask


def blur(plane: list[float], w: int, h: int, sigma: float) -> list[float]:
    radius = max(1, int(math.ceil(sigma * 3)))
    weights = [math.exp(-(k * k) / (2 * sigma * sigma)) for k in range(-radius, radius + 1)]
    total = sum(weights)
    weights = [v / total for v in weights]
    tmp = [0.0] * (w * h)
    for y in range(h):
        row = y * w
        for x in range(w):
            acc = 0.0
            for k, weight in enumerate(weights):
                xx = x + k - radius
                if 0 <= xx < w:
                    acc += plane[row + xx] * weight
            tmp[row + x] = acc
    out = [0.0] * (w * h)
    for y in range(h):
        for x in range(w):
            acc = 0.0
            for k, weight in enumerate(weights):
                yy = y + k - radius
                if 0 <= yy < h:
                    acc += tmp[yy * w + x] * weight
            out[y * w + x] = acc
    return out


def smoothstep(edge0: float, edge1: float, x: float) -> float:
    t = min(1.0, max(0.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3 - 2 * t)


def background(original: Image) -> list[bool]:
    """The original's black background: opaque pixels darker than DARK (a transparent pixel is no background)."""
    mask = []
    for y in range(original.h):
        for x in range(original.w):
            r, g, b, a = original.pixel(x, y)
            mask.append(a >= 128 and luma(r, g, b) < DARK)
    return mask


def check_sizes(original: Image, hd: Image, scale: int) -> None:
    if (hd.w, hd.h) != (original.w * scale, original.h * scale):
        raise RuleError(f"the HD frame is {hd.w}x{hd.h}, the original {original.w}x{original.h} at x{scale} is {original.w * scale}x{original.h * scale}")


# --- Part A: ghost ------------------------------------------------------------------------------------------------------

def ghost_percent(original: Image, hd: Image, scale: int = SCALE) -> float | None:
    """The audit's ghost share (ARC-RES-3144): the percentage of the HD pixels deep inside the original's black (the black eroded by one original pixel, then
    by two HD pixels) whose luma is above GHOST_LIGHT. None when no black is deep enough to measure."""
    check_sizes(original, hd, scale)
    core = erode(background(original), original.w, original.h, 1)
    w, h = hd.w, hd.h
    big = [core[(y // scale) * original.w + x // scale] for y in range(h) for x in range(w)]
    big = erode(big, w, h, 2)
    total = ghosts = 0
    for i, inside in enumerate(big):
        if inside and hd.data[i * 4 + 3] > 200:
            total += 1
            if luma(hd.data[i * 4], hd.data[i * 4 + 1], hd.data[i * 4 + 2]) > GHOST_LIGHT:
                ghosts += 1
    return 100.0 * ghosts / total if total else None


def clear_ghost(original: Image, hd: Image, scale: int = SCALE) -> Image:
    """The pack frame with the original's black background restored: every HD pixel inside the original's black (kept MARGIN pixels back from the content,
    outline smoothed by sigma FEATHER) takes the original's own colour; alpha and everything else stay the pack's."""
    check_sizes(original, hd, scale)
    w, h = hd.w, hd.h
    black = background(original)
    near = [black[(y // scale) * original.w + x // scale] for y in range(h) for x in range(w)]
    soft = blur([1.0 if v else 0.0 for v in erode(near, w, h, MARGIN)], w, h, FEATHER)
    weight = [smoothstep(EDGE[0], EDGE[1], v) for v in soft]
    out = Image(w, h, bytearray(hd.data))
    for y in range(h):
        for x in range(w):
            k = weight[y * w + x]
            if k <= 0.002:
                continue
            ox, oy = x // scale, y // scale
            source = original.pixel(ox, oy)
            i = (y * w + x) * 4
            for c in range(3):
                out.data[i + c] = max(0, min(255, int(round(hd.data[i + c] * (1 - k) + source[c] * k))))
    return out


# --- Part B: upscale ----------------------------------------------------------------------------------------------------

def upscale_square(original: Image, scale: int = SCALE) -> Image:
    """Every original pixel is one scale x scale block, colour and alpha exactly."""
    w, h = original.w * scale, original.h * scale
    out = Image(w, h, bytearray(w * h * 4))
    for y in range(h):
        for x in range(w):
            i = (y * w + x) * 4
            out.data[i:i + 4] = bytes(original.pixel(x // scale, y // scale))
    return out


def upscale_smooth(original: Image, scale: int = SCALE) -> Image:
    """Colour smoothed between the cells (bilinear at the HD pixel's centre over the four nearest cells, each weighted by its alpha so a transparent cell
    lends no colour); alpha is the original's, cell by cell, exactly."""
    ow, oh = original.w, original.h
    w, h = ow * scale, oh * scale
    out = Image(w, h, bytearray(w * h * 4))
    for y in range(h):
        fy = (y + 0.5) / scale - 0.5
        y0 = math.floor(fy)
        ty = fy - y0
        for x in range(w):
            ox, oy = x // scale, y // scale
            alpha = original.pixel(ox, oy)[3]
            if alpha == 0:
                continue
            fx = (x + 0.5) / scale - 0.5
            x0 = math.floor(fx)
            tx = fx - x0
            acc = [0.0, 0.0, 0.0]
            total = 0.0
            for dy in (0, 1):
                for dx in (0, 1):
                    cx, cy = min(max(x0 + dx, 0), ow - 1), min(max(y0 + dy, 0), oh - 1)
                    r, g, b, a = original.pixel(cx, cy)
                    if a == 0:
                        continue
                    k = (tx if dx else 1 - tx) * (ty if dy else 1 - ty) * (a / 255.0)
                    acc[0] += r * k
                    acc[1] += g * k
                    acc[2] += b * k
                    total += k
            i = (y * w + x) * 4
            if total <= 0:
                out.data[i:i + 4] = bytes(original.pixel(ox, oy))
            else:
                out.data[i:i + 4] = bytes((int(round(acc[0] / total)), int(round(acc[1] / total)), int(round(acc[2] / total)), alpha))
    return out


def upscale(original: Image, mode: str, scale: int = SCALE) -> Image:
    if mode not in MODES:
        raise RuleError(f"mode must be one of {', '.join(MODES)}")
    return upscale_square(original, scale) if mode == "square" else upscale_smooth(original, scale)


# --- files --------------------------------------------------------------------------------------------------------------

def check_out_dir(out_dir: Path) -> None:
    """The frames are derived from the player's own art: only `local/` of the checkout (or a folder outside it) may hold them."""
    resolved = out_dir.resolve()
    try:
        relative = resolved.relative_to(REPO)
    except ValueError:
        return
    if not relative.parts or relative.parts[0] != "local":
        raise RuleError(f"{out_dir}: derived frames are never written inside the checkout (only under local/)")


def original_frame(frames_dir: Path, art: census.CensusArt, frame: int) -> Image:
    number = art.ident if not art.ident.startswith("file:") else "x"
    path = frames_dir / f"{number}-{art.name}" / f"f{frame}.png"
    try:
        return pngio.read(path)
    except (OSError, pngio.PngError) as error:
        raise RuleError(f"{path}: {error}") from error


def pack_frames(pack_dir: Path) -> dict[tuple[str, int], Path]:
    """(lower-case art name, frame) -> the pack's PNG, from the pack's index.tsv."""
    table: dict[tuple[str, int], Path] = {}
    try:
        with open(pack_dir / "index.tsv", newline="", encoding="utf-8") as handle:
            for row in csv.DictReader(handle, delimiter="\t"):
                table[(row["art"].lower(), int(row["frame"]))] = pack_dir / row["path"]
    except (OSError, KeyError, ValueError) as error:
        raise RuleError(f"{pack_dir / 'index.tsv'}: {error}") from error
    return table


def record_key(art: census.CensusArt) -> str:
    """The art identifier of the assembly record: the census number, or the file name for an art that has none."""
    return art.name if art.ident.startswith("file:") else art.ident


def write_art(out_dir: Path, art: census.CensusArt, frames: list[Image], alpha_mode: str, scale: int = SCALE) -> None:
    """`<key>-fNNN.png` per frame and `<key>.assembly.json` (the record `pack_plan.py` reads)."""
    check_out_dir(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    key = record_key(art)
    rows = []
    for index, image in enumerate(frames):
        name = f"{key}-f{index:03d}.png"
        pngio.write(out_dir / name, image)
        rows.append({"frame": index, "state": f"frame{index}", "file": name, "size": [image.w, image.h]})
    record = {"art": key, "scale": scale, "alphaMode": alpha_mode, "source": "icon_rule.py", "frames": rows}
    (out_dir / f"{key}.assembly.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")


def find_arts(names: list[str]) -> list[census.CensusArt]:
    by_name = {row.name.lower(): row for row in census.load_arts()}
    found = []
    for name in names:
        row = by_name.get(name.lower())
        if row is None:
            raise RuleError(f"{name}: not in the census")
        found.append(row)
    return found


# --- commands -----------------------------------------------------------------------------------------------------------

def cmd_check(args: argparse.Namespace) -> int:
    table = pack_frames(Path(args.pack))
    worst = 0.0
    for art in find_arts(args.art):
        for frame in range(art.frames):
            path = table.get((art.name.lower(), frame))
            if path is None:
                raise RuleError(f"{art.name} frame {frame}: no pack frame")
            share = ghost_percent(original_frame(Path(args.frames), art, frame), pngio.read(path))
            print(f"{art.name} f{frame}: ghost {'n/a' if share is None else f'{share:.2f} %'}")
            worst = max(worst, share or 0.0)
    print(f"worst ghost {worst:.2f} % (limit {GHOST_MAX_PERCENT} %)")
    return 0 if worst < GHOST_MAX_PERCENT else 1


def cmd_ghost(args: argparse.Namespace) -> int:
    table = pack_frames(Path(args.pack))
    out_dir = Path(args.out)
    check_out_dir(out_dir)
    bad = 0
    for art in find_arts(args.art):
        originals, repaired = [], []
        for frame in range(art.frames):
            path = table.get((art.name.lower(), frame))
            if path is None:
                raise RuleError(f"{art.name} frame {frame}: no pack frame")
            original, hd = original_frame(Path(args.frames), art, frame), pngio.read(path)
            before = ghost_percent(original, hd)
            fixed = clear_ghost(original, hd)
            after = ghost_percent(original, fixed)
            print(f"{art.name} f{frame}: ghost {'n/a' if before is None else f'{before:.2f} %'} -> {'n/a' if after is None else f'{after:.2f} %'}")
            if after is not None and after >= GHOST_MAX_PERCENT:
                bad += 1
            originals.append(original)
            repaired.append(fixed)
        write_art(out_dir, art, repaired, "legacySmooth")
    return 1 if bad else 0


def cmd_upscale(args: argparse.Namespace) -> int:
    out_dir = Path(args.out)
    check_out_dir(out_dir)
    for art in find_arts(args.art):
        originals = [original_frame(Path(args.frames), art, frame) for frame in range(art.frames)]
        write_art(out_dir, art, [upscale(original, args.mode) for original in originals], "legacy")
        print(f"{art.name}: {art.frames} frame(s), {args.mode}")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name, helper, uses_pack in (("check", cmd_check, True), ("ghost", cmd_ghost, True), ("upscale", cmd_upscale, False)):
        p = sub.add_parser(name)
        p.add_argument("--frames", required=True, help="the census frame folder")
        if uses_pack:
            p.add_argument("--pack", required=True, help="the pack folder (index.tsv)")
        if name != "check":
            p.add_argument("--out", required=True)
        if name == "upscale":
            p.add_argument("--mode", choices=MODES, default="square")
        p.add_argument("art", nargs="+")
        p.set_defaults(func=helper)
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except RuleError as error:
        print(f"icon_rule: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
