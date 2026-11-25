#!/usr/bin/env python3
"""ARC-UI-3271 (batch B-BUTTONS): the schematic window's WIDE ROUND BUTTON (the brass bar with its two arrow wells and the brass ring round the olive dome, baked into art 365 Schematic_Base) as one kit part, PACK FIRST
(owner rule 2026-11-25): the old HD pack has a good 4x frame of the art (`interface/schematics/panels/schematic_base-f000.png`), so the part is that frame's brass + an ANALYTIC alpha + the surfaces' shared soft
shadow (#3249); nothing is generated.

    python3 -B tools/steamwoven_ui/wide_button_kit.py rule [--pack FILE] [--dry-run] [--out FILE] [--local-root DIR]
    python3 -B tools/steamwoven_ui/wide_button_kit.py outline [--pack FILE] --out FILE          (the analytic outlines over the pack frame at 3x, a check picture)
    python3 -B tools/steamwoven_ui/wide_button_kit.py self-test

The part (master px of the 2880 x 2160 composition, one 600 x 354 canvas laid at (66, 1230) of the schematic surface): the pack frame is resampled to the master's scale (the master is 3.6 px per unit, the pack 4, so 0.9; Lanczos 3)
and kept where the analytic shape is: a STADIUM (the bar: a rectangle with a half disc at each end) united with the DISC of the brass ring round the dome, minus three round HOLES (the dome and the two wells), whose
pixels the surface keeps from the original (the dome's glossy face, the arrows). The shape's edges are filtered by the lens ring's tent (2 px, 8 x 8 sub-samples) so no stair of the pack's own alpha shows (its holes are octagonal
stairs, 4 px steps). The soft shadow of the whole silhouette falls down and to the right outside it (the ellipse shift of `surface_compose.cast_disc_shadows`: 13 px, Gaussian sigma 16, strength 0.7) as black with alpha, because
the part is laid after the surface's own shadows. The circles and the bar were measured on the pack frame (the transparent holes' areas and bounding boxes, the brass's extent) and checked on the original master.

Everything stays under the ignored `local/steamwoven-ui/`; nothing here holds art. Standard library only (`pngio`). Exit 0 = done, 1 = a check failed or an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import hardware_kit, pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import hardware_kit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore

TOOL_VERSION = 1
PART = "plate.schematic_wide_button"
PACK_FRAME = ("interface-redraw-4x", "interface/schematics/panels/schematic_base-f000.png")     # the old HD pack (4x) and the frame of art 365
SCALE = 0.9                    # master px per pack px: 3.6 px per unit / 4 px per unit
WINDOW_TOP = 148               # master row where the window zone (and the art's top) starts
CANVAS = (600, 354)            # master px, on the 6 px grid
ORIGIN = (66, 1230)            # where the canvas lies on the 2880 x 2160 master
# the shapes, measured on the pack frame (pack px) and put to master px
PACK_DOME = (405.5, 1397.5, 102.0)          # the dome's hole: centre x, y, radius (the transparent hole's bounding box 204 px, area radius 101.6)
PACK_RING = 117.0                           # the brass ring's outer radius round the dome (extent 1280 to 1515)
PACK_WELLS = ((215.3, 1395.5), (595.0, 1395.3))   # the two arrow wells' centres
PACK_WELL_R = 43.5                          # their radius (bounding boxes 88 px, area radius 43)
PACK_BAR = (1394.5, 64.0)                   # the bar's centre row and half height (extent 1331 to 1458): a stadium whose end half discs are centred on the wells' columns
MASTER_DOME = (PACK_DOME[0] * SCALE, WINDOW_TOP + PACK_DOME[1] * SCALE, PACK_DOME[2] * SCALE)
MASTER_RING = PACK_RING * SCALE
MASTER_WELLS = tuple((x * SCALE, WINDOW_TOP + y * SCALE) for x, y in PACK_WELLS)
MASTER_WELL_R = PACK_WELL_R * SCALE
MASTER_BAR_Y = WINDOW_TOP + PACK_BAR[0] * SCALE
MASTER_BAR_H = PACK_BAR[1] * SCALE
SHADOW = (13, 13, 16.0, 0.7)    # dx, dy, sigma, strength: the same as `surface_compose.cast_disc_shadows`
EDGE_SAMPLES, EDGE_REACH = hardware_kit.EDGE_SAMPLES, hardware_kit.EDGE_REACH
LANCZOS = 3


class KitError(hardware_kit.KitError):
    pass


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


# --- the analytic shapes (master px, canvas-free) ------------------------------------------------------------------------------------------

def bar_ends() -> tuple[float, float]:
    """The columns of the bar's two end half discs: the wells' columns, made symmetric about the dome's (the pack measures them 190.2 and 189.5 px from it)."""
    half = (MASTER_WELLS[1][0] - MASTER_WELLS[0][0]) / 2
    centre = MASTER_DOME[0]
    return centre - half, centre + half


def in_stadium(x: float, y: float) -> bool:
    left, right = bar_ends()
    dy = y - MASTER_BAR_Y
    if abs(dy) > MASTER_BAR_H:
        return False
    if left <= x <= right:
        return True
    cx = left if x < left else right
    return (x - cx) ** 2 + dy ** 2 <= MASTER_BAR_H ** 2


def in_disc(x: float, y: float, cx: float, cy: float, r: float) -> bool:
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def in_silhouette(x: float, y: float) -> bool:
    return in_stadium(x, y) or in_disc(x, y, MASTER_DOME[0], MASTER_DOME[1], MASTER_RING)


def in_hole(x: float, y: float) -> bool:
    return in_disc(x, y, MASTER_DOME[0], MASTER_DOME[1], MASTER_DOME[2]) or any(in_disc(x, y, wx, wy, MASTER_WELL_R) for wx, wy in MASTER_WELLS)


def in_shape(x: float, y: float) -> bool:
    return in_silhouette(x, y) and not in_hole(x, y)


def tent_share(x: float, y: float, test) -> float:
    n = EDGE_SAMPLES
    got = total = 0.0
    for j in range(n):
        oy = (j + 0.5) / n * 2 - 1
        wy = 1 - abs(oy)
        for i in range(n):
            ox = (i + 0.5) / n * 2 - 1
            weight = wy * (1 - abs(ox))
            total += weight
            if test(x + ox * EDGE_REACH, y + oy * EDGE_REACH):
                got += weight
    return got / total


def coverage(x: float, y: float, test) -> float:
    """The tent-filtered share of the pixel at (x, y) inside `test`: the exact 0 or 1 away from an edge, sub-sampled near it."""
    here = test(x, y)
    reach = EDGE_REACH * 1.6
    if all(test(x + dx, y + dy) == here for dx, dy in ((reach, 0), (-reach, 0), (0, reach), (0, -reach), (reach, reach), (-reach, -reach), (reach, -reach), (-reach, reach))):
        return 1.0 if here else 0.0
    return tent_share(x, y, test)


# --- the soft shadow of the silhouette -----------------------------------------------------------------------------------------------------

def silhouette_spans(yc: float) -> list[tuple[float, float]]:
    """The silhouette's horizontal span(s) at the row whose centre is at `yc` (master y), merged, left to right."""
    spans = []
    left, right = bar_ends()
    dy = yc - MASTER_BAR_Y
    if abs(dy) < MASTER_BAR_H:
        cap = math.sqrt(MASTER_BAR_H ** 2 - dy ** 2)
        spans.append((left - cap, right + cap))
    dy = yc - MASTER_DOME[1]
    if abs(dy) < MASTER_RING:
        half = math.sqrt(MASTER_RING ** 2 - dy ** 2)
        spans.append((MASTER_DOME[0] - half, MASTER_DOME[0] + half))
    spans.sort()
    merged: list[tuple[float, float]] = []
    for lo, hi in spans:
        if merged and lo <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(merged[-1][1], hi))
        else:
            merged.append((lo, hi))
    return merged


def shadow_field() -> list[list[float]]:
    """The silhouette shifted by (dx, dy) and blurred by a Gaussian (sigma) at every canvas pixel (0 to 1): exact along the rows (the erf of every span), summed along the columns."""
    dx, dy, sigma, _strength = SHADOW
    w, h = CANVAS
    ox, oy = ORIGIN
    root = sigma * math.sqrt(2.0)
    reach = int(math.ceil(3 * sigma))
    kernel = {k: math.exp(-(k * k) / (2 * sigma * sigma)) for k in range(-reach, reach + 1)}
    norm = sum(kernel.values())
    rows: dict[int, list[float]] = {}
    for row in range(-reach, h + reach):
        spans = silhouette_spans(oy + row + 0.5 - dy)
        line = []
        for col in range(w):
            x = ox + col + 0.5 - dx
            line.append(sum(0.5 * (math.erf((hi - x) / root) - math.erf((lo - x) / root)) for lo, hi in spans))
        rows[row] = line
    out = []
    for row in range(h):
        acc = [0.0] * w
        for k, wt in kernel.items():
            line = rows[row + k]
            for col in range(w):
                acc[col] += wt * line[col]
        out.append([a / norm for a in acc])
    return out


# --- the pack frame ------------------------------------------------------------------------------------------------------------------------

def pack_path(pack: Path | None) -> Path:
    if pack is not None:
        return pack
    return prompt_library.main_checkout() / "local" / "hd-packs" / PACK_FRAME[0] / PACK_FRAME[1]


def lanczos(x: float) -> float:
    if x == 0:
        return 1.0
    if abs(x) >= LANCZOS:
        return 0.0
    px = math.pi * x
    return LANCZOS * math.sin(px) * math.sin(px / LANCZOS) / (px * px)


def resample(src: pngio.Image) -> pngio.Image:
    """The canvas' window of the pack frame at the master's scale (Lanczos 3, area-correct for a 0.9 shrink): opaque RGB, the pack frame is the whole opaque art outside its holes."""
    w, h = CANVAS
    ox, oy = ORIGIN
    support = LANCZOS / SCALE
    def taps(centre: float, limit: int) -> list[tuple[int, float]]:
        lo, hi = int(math.floor(centre - support)), int(math.ceil(centre + support))
        pairs = [(min(limit - 1, max(0, i)), lanczos((i + 0.5 - centre) * SCALE)) for i in range(lo, hi + 1)]
        total = sum(v for _i, v in pairs)
        return [(i, v / total) for i, v in pairs]
    xs = [taps((ox + col + 0.5) / SCALE, src.w) for col in range(w)]
    ys = [taps((oy + row + 0.5 - WINDOW_TOP) / SCALE, src.h) for row in range(h)]
    cache: dict[int, list[list[float]]] = {}
    def source_row(y: int) -> list[list[float]]:
        if y not in cache:
            cache[y] = [[0.0, 0.0, 0.0] for _ in range(w)]
            data = src.data
            for col, taps_x in enumerate(xs):
                acc = cache[y][col]
                for i, wt in taps_x:
                    p = (y * src.w + i) * 4
                    acc[0] += wt * data[p]
                    acc[1] += wt * data[p + 1]
                    acc[2] += wt * data[p + 2]
        return cache[y]
    out = pngio.Image.blank(w, h)
    for row, taps_y in enumerate(ys):
        acc = [[0.0, 0.0, 0.0] for _ in range(w)]
        for j, wt in taps_y:
            line = source_row(j)
            for col in range(w):
                for c in range(3):
                    acc[col][c] += wt * line[col][c]
        for col in range(w):
            i = (row * w + col) * 4
            out.data[i:i + 4] = bytes((min(255, max(0, int(acc[col][0] + 0.5))), min(255, max(0, int(acc[col][1] + 0.5))), min(255, max(0, int(acc[col][2] + 0.5))), 255))
        if row % 16 == 0:
            for stale in [k for k in cache if k < taps_y[0][0] - 1]:
                del cache[stale]
    return out


def render(pack: pngio.Image) -> pngio.Image:
    """The part: the pack frame's brass under the analytic shape, the silhouette's soft shadow outside it, the holes open."""
    w, h = CANVAS
    ox, oy = ORIGIN
    color = resample(pack)
    shadow = shadow_field()
    strength = SHADOW[3]
    out = pngio.Image.blank(w, h)
    for row in range(h):
        for col in range(w):
            x, y = ox + col + 0.5, oy + row + 0.5
            shape = coverage(x, y, in_shape)
            sil = coverage(x, y, in_silhouette)
            cast = strength * shadow[row][col] * (1.0 - sil)         # the shadow only outside the silhouette (inside a hole the surface's own pixels show)
            alpha = shape + (1.0 - shape) * cast
            if alpha <= 0.002:
                continue
            i = (row * w + col) * 4
            r, g, b = color.data[i], color.data[i + 1], color.data[i + 2]
            out.data[i:i + 4] = bytes((int(r * shape / alpha + 0.5), int(g * shape / alpha + 0.5), int(b * shape / alpha + 0.5), int(round(alpha * 255))))
    return out


def gates(image: pngio.Image, pack: pngio.Image | None = None) -> list[str]:
    failures = []
    w, h = CANVAS
    if (image.w, image.h) != (w, h) or w % 6 or h % 6:
        return [f"the master is {image.w} x {image.h}, not {w} x {h} on the 6 px grid"]
    ox, oy = ORIGIN
    at = lambda x, y: image.pixel(int(round(x - ox)), int(round(y - oy)))
    dx, dy, r = MASTER_DOME
    if at(dx, dy)[3]:
        failures.append("the dome's hole is not open")
    for wx, wy in MASTER_WELLS:
        if at(wx, wy)[3]:
            failures.append("an arrow well's hole is not open")
    if at(dx, dy - (MASTER_DOME[2] + MASTER_RING) / 2)[3] != 255:
        failures.append("the ring's brass is not opaque above the dome")
    left, _right = bar_ends()
    if at(left - MASTER_BAR_H / 2, MASTER_BAR_Y - MASTER_BAR_H * 0.8)[3] != 255 or at((left + dx) / 2 - 30, MASTER_BAR_Y + MASTER_BAR_H * 0.6)[3] != 255:
        failures.append("the bar's brass is not opaque")
    outside = at(left - MASTER_BAR_H - 40, MASTER_BAR_Y)
    if outside[3] != 0:
        failures.append("something is drawn well outside the bar")
    shade = [at(dx + 40, dy + MASTER_RING + k)[3] for k in range(2, 30, 4)]     # below the ring the shadow fades out
    if not (0 < shade[0] <= int(255 * SHADOW[3]) + 1 and shade[0] > shade[-1]):
        failures.append("the soft shadow does not fall off below the ring")
    return failures


def provenance_record(path: Path, blob: bytes) -> dict:
    return {"part": PART, "rule": "wide_button_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "source": "pack-first: the old HD pack's 4x frame of art 365 (interface/schematics/panels/schematic_base-f000.png), analytic alpha, soft shadow of #3249",
            "pack": PACK_FRAME[0], "frame": PACK_FRAME[1], "sources_sha256": {"schematic_base-f000": sha256_file(path)}, "origin": list(ORIGIN),
            "geometry": {"canvas": list(CANVAS), "scale": SCALE, "dome": list(MASTER_DOME), "ring_r": MASTER_RING, "wells": [list(w) for w in MASTER_WELLS], "well_r": MASTER_WELL_R, "bar_y": MASTER_BAR_Y, "bar_half_height": MASTER_BAR_H},
            "image_size": list(CANVAS), "image_sha256": hashlib.sha256(blob).hexdigest(), "run_by": "agent"}


def cmd_rule(args: argparse.Namespace) -> int:
    path = pack_path(args.pack)
    if not path.is_file():
        raise KitError(f"{path} is missing: the old HD pack's frame of art 365")
    image = render(pngio.read(path))
    failures = gates(image)
    if failures:
        raise KitError(f"{PART}: " + "; ".join(failures))
    blob = pngio.encode(image)
    print(f"{PART}: {CANVAS[0]}x{CANVAS[1]} {hashlib.sha256(blob).hexdigest()[:12]}")
    if not args.dry_run:
        masters = args.local_root / "masters"
        target = args.out or masters / f"{PART}.png"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(blob)
        if args.out is None:
            (args.local_root / "provenance" / "rule").mkdir(parents=True, exist_ok=True)
            (args.local_root / "provenance" / "rule" / f"{PART}.json").write_text(json.dumps(provenance_record(path, blob), indent=2) + "\n", encoding="utf-8")
    print("WIDE BUTTON KIT rule PASS" + (" (dry run, nothing written)" if args.dry_run else ""))
    return 0


def cmd_outline(args: argparse.Namespace) -> int:
    """The analytic shapes drawn as one-pixel red outlines over the pack frame's window at 3x: a picture to check the measured circles and the bar against the pack's brass."""
    path = pack_path(args.pack)
    if not path.is_file():
        raise KitError(f"{path} is missing: the old HD pack's frame of art 365")
    if args.out is None:
        raise KitError("outline needs --out")
    color = resample(pngio.read(path))
    zoom = 3
    w, h = CANVAS
    ox, oy = ORIGIN
    big = pngio.Image.blank(w * zoom, h * zoom)
    for row in range(h * zoom):
        for col in range(w * zoom):
            s = ((row // zoom) * w + col // zoom) * 4
            big.data[(row * w * zoom + col) * 4:(row * w * zoom + col) * 4 + 4] = bytes((color.data[s], color.data[s + 1], color.data[s + 2], 255))
    for tests, rgb in (((in_silhouette,), (255, 0, 0)), ((in_hole,), (0, 255, 255))):
        for row in range(h * zoom):
            for col in range(w * zoom):
                x, y = ox + (col + 0.5) / zoom, oy + (row + 0.5) / zoom
                here = tests[0](x, y)
                if here != tests[0](x + 1.0 / zoom, y) or here != tests[0](x, y + 1.0 / zoom):
                    i = (row * w * zoom + col) * 4
                    big.data[i:i + 3] = bytes(rgb)
    pngio.write(args.out, big)
    print(f"WIDE BUTTON KIT outline written: {args.out}")
    return 0


def cmd_self_test(args: argparse.Namespace) -> int:
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_wide_button_kit"], cwd=HERE.parents[1]).returncode
    print("WIDE BUTTON KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return 0 if code == 0 else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name, fn in (("rule", cmd_rule), ("outline", cmd_outline), ("self-test", cmd_self_test)):
        s = sub.add_parser(name)
        s.set_defaults(fn=fn)
        s.add_argument("--pack", type=Path, help="the pack frame of art 365 (default: the old HD pack in the main checkout's local/hd-packs)")
        s.add_argument("--local-root", type=Path)
        s.add_argument("--dry-run", action="store_true")
        s.add_argument("--out", type=Path, help="write here (a trial, no record)")
    args = parser.parse_args(argv)
    args.local_root = args.local_root or prompt_library.default_local_root()
    try:
        return args.fn(args)
    except (KitError, hardware_kit.KitError, pngio.PngError) as error:
        print(f"WIDE BUTTON KIT {args.command} FAIL: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
