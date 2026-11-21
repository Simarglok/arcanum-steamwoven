"""ARC-UI-3058: match the tone of a kit glyph to its ORIGINAL frame, per state (owner request 2026-11-21, pilot of the round buttons).

A button glyph (key, gear, arrows, shield) is drawn in two looks, dull (idle frame) and lit (hover and pressed frames). The ORIGINAL's look of each state is a number: the mean
CIE L*a*b* (sRGB, D65) of the glyph's pixels in the frame, and the mean of the plate's dome around it. The generator keeps the shape but not the brightness (the first pilot's idle key
was almost invisible), so after `fit.py` the glyph master's colours are moved, as one shift of the mean L*, a* and b* over its interior (texture, relief and edge kept), to the ORIGINAL's mean of the
pixels the glyph covers. The ORIGINAL's glyph is dithered (about half of its pixels are the dark ground): its mean would make a smooth redraw as dark as the dome, so the target is
the mean of the brighter half of the glyph's pixels, the colour the dither shows (`--stat mean` takes the plain mean). `--dome-frame` takes the plate reference from another frame of the ORIGINAL (the idle frame for a lit glyph:
the hover frame's dome is lit by the glow, the kit's plate is not).

The glyph's place on the original's 39 x 39 grid is its master alpha placed at the layout's centre (units) at the master scale (3.6 px per unit by default) and sampled 4 x 4 per unit pixel; a pixel
counts as glyph when at least 0.9 of it is covered and as plate (dome) when none is and it lies within 13 units of the frame's centre (the plate's rim is left out).

    python3 -B tools/steamwoven_ui/glyph_tone.py measure ORIGINAL_FRAME.png GLYPH_MASTER.png --centre X,Y [--stat bright|mean] [--dome-frame IDLE_FRAME.png] [--plate PLATE_MASTER.png] [--json]
    python3 -B tools/steamwoven_ui/glyph_tone.py fit ORIGINAL_FRAME.png GLYPH_MASTER.png OUT.png --centre X,Y [--strength S] [--stat bright|mean] [--dome-frame IDLE_FRAME.png] [--plate PLATE_MASTER.png]
    python3 -B tools/steamwoven_ui/glyph_tone.py report ORIGINAL_FRAME.png RESULT_FRAME.png GLYPH_MASTER.png --centre X,Y [--stat bright|mean] [--dome-frame IDLE_FRAME.png]

`measure` prints the mean L*a*b* of the ORIGINAL's glyph and dome pixels and of the master. `fit` writes the master with the shift (`--strength` 0..1 scales it). With `--plate` (the kit's plate master the glyph will sit on) the target is the
ORIGINAL's glyph brightness *relative to its plate*: the master's mean L* goes to the plate's dome L* plus the ORIGINAL's (glyph - dome) L*, a* and b* go to the ORIGINAL glyph's (the plate stays as it is, the owner's rule); without it the target is the ORIGINAL's absolute mean. `report` compares an assembled frame
(any size, area-averaged to the 39 x 39 grid) with the ORIGINAL: glyph L*, dome L*, and their difference, the glyph's brightness against its plate. Standard library only; exit 0, 2 = an input could not be read.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

try:
    from . import pngio, tone_check
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import pngio  # type: ignore
    import tone_check  # type: ignore

GRID = 39
SUPER = 4
GLYPH_COVER = 0.9
DOME_RADIUS = 13.0
CENTRE = (GRID / 2, GRID / 2)


class ToneError(Exception):
    pass


def parse_centre(text: str) -> tuple[float, float]:
    try:
        x, y = (float(v) for v in text.split(","))
    except ValueError as error:
        raise ToneError(f"--centre {text!r} is not X,Y in original units") from error
    return x, y


def coverage(glyph: "pngio.Image", centre: tuple[float, float], master_scale: float = 3.6) -> list[float]:
    """The share of each unit pixel of the 39 x 39 frame that the glyph master's alpha covers (4 x 4 samples), the master centred at `centre` units."""
    out = []
    for uy in range(GRID):
        for ux in range(GRID):
            total = 0
            for sy in range(SUPER):
                for sx in range(SUPER):
                    mx = ((ux + (sx + 0.5) / SUPER) - centre[0]) * master_scale + glyph.w / 2
                    my = ((uy + (sy + 0.5) / SUPER) - centre[1]) * master_scale + glyph.h / 2
                    ix, iy = int(math.floor(mx)), int(math.floor(my))
                    if 0 <= ix < glyph.w and 0 <= iy < glyph.h:
                        total += glyph.data[(iy * glyph.w + ix) * 4 + 3]
            out.append(total / (255.0 * SUPER * SUPER))
    return out


def reduce_to_grid(image: "pngio.Image") -> list[tuple[float, float, float, float]]:
    """The frame area-averaged to 39 x 39 (premultiplied): (r, g, b, alpha) in 0..255 per unit pixel."""
    if image.w % GRID or image.h % GRID:
        raise ToneError(f"a frame of {image.w} x {image.h} px is not a whole multiple of the {GRID} x {GRID} grid")
    fx, fy = image.w // GRID, image.h // GRID
    out = []
    for uy in range(GRID):
        for ux in range(GRID):
            r = g = b = a = 0.0
            for y in range(uy * fy, (uy + 1) * fy):
                for x in range(ux * fx, (ux + 1) * fx):
                    o = (y * image.w + x) * 4
                    al = image.data[o + 3]
                    r += image.data[o] * al; g += image.data[o + 1] * al; b += image.data[o + 2] * al; a += al
            n = fx * fy
            out.append((r / a, g / a, b / a, a / n) if a else (0.0, 0.0, 0.0, 0.0))
    return out


def mean_lab(pixels: list[tuple[float, float, float, float]], weights: list[float]) -> tuple[float, float, float, int]:
    sl = sa = sb = sw = 0.0
    count = 0
    for (r, g, b, a), w in zip(pixels, weights):
        if w <= 0 or a < 200:
            continue
        lab = tone_check._pixel_lab(int(round(r)), int(round(g)), int(round(b)))
        sl += lab[0] * w; sa += lab[1] * w; sb += lab[2] * w; sw += w
        count += 1
    if sw == 0:
        raise ToneError("no pixel of the region is opaque")
    return sl / sw, sa / sw, sb / sw, count


def bright_lab(pixels: list[tuple[float, float, float, float]], weights: list[float]) -> tuple[float, float, float, int]:
    """The mean L*a*b* of the brighter half (by L*) of the weighted pixels: a dithered glyph is its colour on half the pixels and the dark ground on the other half, so the
    designed colour of the glyph, the one a smooth redraw must have to read as strongly as the original, is the brighter half's."""
    labs = []
    for (r, g, b, a), w in zip(pixels, weights):
        if w > 0 and a >= 200:
            labs.append(tone_check._pixel_lab(int(round(r)), int(round(g)), int(round(b))))
    if not labs:
        raise ToneError("no pixel of the region is opaque")
    labs.sort()
    half = labs[len(labs) // 2:]
    n = len(half)
    return sum(v[0] for v in half) / n, sum(v[1] for v in half) / n, sum(v[2] for v in half) / n, n


def regions(cover: list[float]) -> tuple[list[float], list[float]]:
    glyph = [1.0 if c >= GLYPH_COVER else 0.0 for c in cover]
    dome = []
    for i, c in enumerate(cover):
        ux, uy = i % GRID, i // GRID
        inside = math.hypot(ux + 0.5 - CENTRE[0], uy + 0.5 - CENTRE[1]) <= DOME_RADIUS
        dome.append(1.0 if c == 0 and inside else 0.0)
    return glyph, dome


def master_mean(glyph: "pngio.Image") -> tuple[float, float, float, int]:
    sl = sa = sb = 0.0
    n = 0
    for i in range(0, len(glyph.data), 4):
        if glyph.data[i + 3] < 250:
            continue
        lab = tone_check._pixel_lab(glyph.data[i], glyph.data[i + 1], glyph.data[i + 2])
        sl += lab[0]; sa += lab[1]; sb += lab[2]; n += 1
    if not n:
        raise ToneError("the glyph master has no opaque pixel")
    return sl / n, sa / n, sb / n, n


def plate_dome_lab(plate: "pngio.Image", cover: list[float]) -> tuple[float, float, float]:
    """The mean L*a*b* of the kit plate master over the unit pixels the ORIGINAL's dome measure uses (outside the glyph, inside the dome radius); the plate spans the 39-unit frame."""
    _, dome_w = regions(cover)
    scale_x, scale_y = plate.w / GRID, plate.h / GRID
    sl = sa = sb = 0.0
    n = 0
    for i, w in enumerate(dome_w):
        if not w:
            continue
        ux, uy = i % GRID, i // GRID
        for sy in range(SUPER):
            for sx in range(SUPER):
                ix = min(plate.w - 1, int((ux + (sx + 0.5) / SUPER) * scale_x))
                iy = min(plate.h - 1, int((uy + (sy + 0.5) / SUPER) * scale_y))
                o = (iy * plate.w + ix) * 4
                if plate.data[o + 3] < 250:
                    continue
                lab = tone_check._pixel_lab(plate.data[o], plate.data[o + 1], plate.data[o + 2])
                sl += lab[0]; sa += lab[1]; sb += lab[2]; n += 1
    if not n:
        raise ToneError("the plate master has no opaque dome pixel")
    return sl / n, sa / n, sb / n


def measure(original: "pngio.Image", glyph: "pngio.Image", centre: tuple[float, float], master_scale: float = 3.6, plate: "pngio.Image | None" = None,
            stat: str = "bright", dome_original: "pngio.Image | None" = None) -> dict:
    grid = reduce_to_grid(original)
    cover = coverage(glyph, centre, master_scale)
    glyph_w, dome_w = regions(cover)
    og = (bright_lab if stat == "bright" else mean_lab)(grid, glyph_w)
    od = mean_lab(reduce_to_grid(dome_original) if dome_original is not None else grid, dome_w)
    mg = master_mean(glyph)
    result = {"stat": stat, "original_glyph_mean": mean_lab(grid, glyph_w)[:3], "original_glyph": og[:3], "original_glyph_pixels": og[3], "original_dome": od[:3], "original_dome_pixels": od[3], "master_glyph": mg[:3],
              "shift": (og[0] - mg[0], og[1] - mg[1], og[2] - mg[2])}
    if plate is not None:
        dome = plate_dome_lab(plate, cover)
        target_l = dome[0] + (og[0] - od[0])
        result["plate_dome"] = dome
        result["target_glyph"] = (target_l, og[1], og[2])
        result["shift"] = (target_l - mg[0], og[1] - mg[1], og[2] - mg[2])
    return result


def fit(original: "pngio.Image", glyph: "pngio.Image", centre: tuple[float, float], strength: float = 1.0,
        master_scale: float = 3.6, plate: "pngio.Image | None" = None, stat: str = "bright",
        dome_original: "pngio.Image | None" = None) -> tuple["pngio.Image", dict]:
    report = measure(original, glyph, centre, master_scale, plate, stat, dome_original)
    dl, da, db = (v * strength for v in report["shift"])
    out = pngio.Image(glyph.w, glyph.h, bytearray(glyph.data))
    for i in range(0, len(out.data), 4):
        if out.data[i + 3] == 0:
            continue
        l, a, b = tone_check._pixel_lab(out.data[i], out.data[i + 1], out.data[i + 2])
        out.data[i:i + 3] = bytes(tone_check._from_lab(max(0.0, min(100.0, l + dl)), a + da, b + db))
    report["applied_shift"] = (dl, da, db)
    return out, report


def report_frame(original: "pngio.Image", result: "pngio.Image", glyph: "pngio.Image", centre: tuple[float, float], master_scale: float = 3.6,
                 stat: str = "mean", dome_original: "pngio.Image | None" = None) -> dict:
    """The glyph's L*a*b* and its brightness over the dome in the ORIGINAL and in the RESULT; `stat` and `dome_original` are the options of `measure` and apply to the ORIGINAL (the result is a smooth redraw: its plain mean)."""
    cover = coverage(glyph, centre, master_scale)
    glyph_w, dome_w = regions(cover)
    out = {}
    for name, image in (("original", original), ("result", result)):
        grid = reduce_to_grid(image)
        pick = bright_lab if name == "original" and stat == "bright" else mean_lab
        dome_grid = reduce_to_grid(dome_original) if name == "original" and dome_original is not None else grid
        g, d = pick(grid, glyph_w), mean_lab(dome_grid, dome_w)
        out[name] = {"glyph_lab": g[:3], "dome_lab": d[:3], "glyph_minus_dome_l": g[0] - d[0]}
    out["delta_glyph_l"] = out["result"]["glyph_lab"][0] - out["original"]["glyph_lab"][0]
    out["delta_contrast_l"] = out["result"]["glyph_minus_dome_l"] - out["original"]["glyph_minus_dome_l"]
    return out


def fmt(lab) -> str:
    return "L*a*b* " + " ".join(f"{v:.1f}" for v in lab)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("measure", "fit", "report"):
        p = sub.add_parser(name)
        p.add_argument("original")
        if name == "report":
            p.add_argument("result")
        p.add_argument("glyph")
        if name == "fit":
            p.add_argument("out")
            p.add_argument("--strength", type=float, default=1.0)
        if name in ("fit", "measure", "report"):
            p.add_argument("--stat", choices=("bright", "mean"), default="bright", help="the ORIGINAL's glyph tone: the brighter half's mean (default, the dither's colour) or the mean of all glyph pixels")
            p.add_argument("--dome-frame", help="another frame of the ORIGINAL whose dome is the plate reference (the idle frame for a lit glyph: the hover frame's dome is lit by the glow, the kit's plate is not)")
        if name in ("fit", "measure"):
            p.add_argument("--plate", help="the kit plate master the glyph sits on: fit to the ORIGINAL's glyph brightness relative to the plate")
        p.add_argument("--centre", required=True)
        p.add_argument("--master-scale", type=float, default=3.6)
        p.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    try:
        original, glyph = pngio.read(args.original), pngio.read(args.glyph)
        centre = parse_centre(args.centre)
        if args.command == "measure":
            result = measure(original, glyph, centre, args.master_scale, pngio.read(args.plate) if args.plate else None, args.stat,
                             pngio.read(args.dome_frame) if args.dome_frame else None)
        elif args.command == "fit":
            if not 0.0 <= args.strength <= 1.0:
                raise ToneError("--strength must be between 0 and 1")
            image, result = fit(original, glyph, centre, args.strength, args.master_scale, pngio.read(args.plate) if args.plate else None, args.stat,
                                 pngio.read(args.dome_frame) if args.dome_frame else None)
            pngio.write(args.out, image)
        else:
            result = report_frame(original, pngio.read(args.result), glyph, centre, args.master_scale, args.stat, pngio.read(args.dome_frame) if args.dome_frame else None)
    except (OSError, pngio.PngError, ToneError) as error:
        print(f"GLYPH TONE error: {error}", file=sys.stderr)
        return 2
    if args.json:
        print(json.dumps(result, indent=1))
    elif args.command == "report":
        for name in ("original", "result"):
            r = result[name]
            print(f"{name}: glyph {fmt(r['glyph_lab'])}, dome {fmt(r['dome_lab'])}, glyph - dome L* {r['glyph_minus_dome_l']:.1f}")
        print(f"delta glyph L* {result['delta_glyph_l']:.1f}, delta glyph-dome contrast {result['delta_contrast_l']:.1f}")
    else:
        print(f"original glyph {fmt(result['original_glyph'])} ({result['original_glyph_pixels']} px), dome {fmt(result['original_dome'])}; master {fmt(result['master_glyph'])}")
        print("shift " + " ".join(f"{v:.1f}" for v in result.get("applied_shift", result["shift"])))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
