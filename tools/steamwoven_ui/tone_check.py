"""ARC-RES-2455: the tone check of a refined context image against its ORIGINAL (SWI-GEN-9).

A refinement may raise only resolution and surface texture, never the palette, brightness or contrast of the original. The check reduces both images
to 800 x 600 (area average), converts them to CIE L*a*b* (sRGB, D65), blurs them (box blur, radius 6 px at 800 x 600, so the texture drops out) and compares
the mean L* (brightness), the mean chroma C* (saturation), the L* standard deviation (contrast) and the colour difference CIE76 delta E per pixel (mean and
95th percentile). The comparison is made per material and for the whole, over the pixels of the ORIGINAL that belong to a material (SWI-GEN-9):

- a material is a colour class (wood, brass, paper; read on the unblurred ORIGINAL pixel) inside a layout zone, from `prompts/tone_masks.json` (text, coordinates
  of the 800 x 600 layout); everything that is content in the ORIGINAL and empty in the redraw (text fields, portrait, the schematic picture, items in slots,
  counters) is a rectangle of `exclude`, and the HUD's world rows are left out; text on paper falls out by its colour;
- the blur is normalised over the material pixels only (a masked average), so text, black fields and content do not pull the tone of the paper or the wood.

    python3 -B tools/steamwoven_ui/tone_check.py compare ORIGINAL.png RESULT.png [--json]
    python3 -B tools/steamwoven_ui/tone_check.py batch --originals DIR --results DIR [--ids ctx-hud,...] [--json]
    python3 -B tools/steamwoven_ui/tone_check.py fit SCREEN ORIGINAL.png RESULT.png OUTPUT.png --material paper

`compare` is the plain whole-image check (no masks). `batch` pairs `DIR_ORIGINALS/<id>-800x600.png` with `DIR_RESULTS/<id>.png` and applies the masks of the id.
`fit` is the region tone fit (a fallback after a regeneration): inside one material it shifts L* and scales the chroma of the result to the mean of the ORIGINAL.
Exit 0 = every region of every pair within the limits, 1 = a region over a limit, 2 = an input could not be read.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

try:
    from . import pngio
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import pngio  # type: ignore

TARGET_W, TARGET_H = 800, 600
BLUR_RADIUS = 6  # px at 800 x 600
LIMITS = {
    "mean_l_delta": 3.0,        # |mean L* result - original| in L* points (0..100)
    "mean_chroma_ratio": 0.12,  # |mean C* result / original - 1|
    "contrast_ratio": 0.12,     # |std L* result / original - 1| (counts only when the std also differs by more than CONTRAST_FLOOR)
    "mean_delta_e": 6.0,        # mean CIE76 delta E of the blurred images
    "p95_delta_e": 15.0,        # 95th percentile of the same
}


CONTRAST_FLOOR = 2.0  # L* points: the blurred std of one material is small (2-8), so a ratio alone would fail a 0.5 L* difference
MASKS_FILE = Path(__file__).resolve().parent / "prompts" / "tone_masks.json"
HALF_W, HALF_H, HALF_RADIUS = TARGET_W // 2, TARGET_H // 2, BLUR_RADIUS // 2  # masked comparison works at 400 x 300 (radius 3 = 6 px at 800 x 600)
MIN_CELLS = 80  # a material needs this many measured 400 x 300 cells (about 0.07 % of the screen), else it is reported as absent
FIT_RADIUS, FIT_LOW, FIT_HIGH = 8, 0.2, 0.5  # region tone fit weight: blur radius (px at 800 x 600) and the stretch of the blurred mask
MIN_COVER = 0.25  # a cell is measured when this share of it, and of its blur window, is material (thin brass rings are never half a cell)
# Colour classes, read on the unblurred ORIGINAL pixel as (L*, C*, hue in degrees).
CLASSES = {
    "wood": lambda l, c, h: 8 <= l <= 42 and 4 <= c < 19 and 25 <= h <= 85,
    "brass": lambda l, c, h: 15 <= l <= 70 and c >= 19 and 55 <= h <= 105,
    "paper": lambda l, c, h: l >= 42 and 4 <= c <= 45 and 65 <= h <= 110,
}


class ToneError(Exception):
    pass


def _weights(src: int, dst: int) -> list[list[tuple[int, float]]]:
    """Area-average weights: for each output index a list of (input index, weight) with weights summing to 1."""
    scale = src / dst
    table = []
    for o in range(dst):
        lo, hi = o * scale, (o + 1) * scale
        row = []
        i = int(math.floor(lo))
        while i < hi and i < src:
            w = min(hi, i + 1) - max(lo, i)
            if w > 1e-9:
                row.append((i, w / scale))
            i += 1
        table.append(row)
    return table


def reduce_to_800x600(image: "pngio.Image") -> list[list[float]]:
    """Three channel planes (R, G, B as floats 0..255, row-major 800 x 600), area-averaged from the image."""
    if image.w * 3 != image.h * 4:
        raise ToneError(f"aspect {image.w}x{image.h} is not 4:3")
    wx, wy = _weights(image.w, TARGET_W), _weights(image.h, TARGET_H)
    data = image.data
    planes = []
    for c in range(3):
        # horizontal pass for every source row, then vertical
        rows = []
        for y in range(image.h):
            base = y * image.w * 4 + c
            rows.append([sum(data[base + i * 4] * w for i, w in cols) for cols in wx])
        out = []
        for cols in wy:
            acc = [0.0] * TARGET_W
            for j, w in cols:
                r = rows[j]
                acc = [a + v * w for a, v in zip(acc, r)]
            out.extend(acc)
        planes.append(out)
    return planes


def box_blur(plane: list[float], radius: int = BLUR_RADIUS, w: int = TARGET_W, h: int = TARGET_H) -> list[float]:
    """Separable box blur of a w x h plane (default 800 x 600), edges clamped."""
    n = 2 * radius + 1

    def run(values: list[float]) -> list[float]:
        padded = [values[0]] * radius + values + [values[-1]] * radius
        total = sum(padded[:n])
        out = [total / n]
        for i in range(1, len(values)):
            total += padded[i + n - 1] - padded[i - 1]
            out.append(total / n)
        return out

    rows = []
    for y in range(h):
        rows.extend(run(plane[y * w:(y + 1) * w]))
    cols = [run([rows[y * w + x] for y in range(h)]) for x in range(w)]
    return [cols[x][y] for y in range(h) for x in range(w)]


def _lin(v: float) -> float:
    v /= 255.0
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def _f(t: float) -> float:
    return t ** (1 / 3) if t > 216 / 24389 else (24389 / 27 * t + 16) / 116


def to_lab(planes: list[list[float]]) -> list[tuple[float, float, float]]:
    cache: dict[int, float] = {}

    def lin(v: float) -> float:
        k = int(v * 4)
        got = cache.get(k)
        if got is None:
            got = cache[k] = _lin(k / 4)
        return got

    out = []
    for r, g, b in zip(*planes):
        rl, gl, bl = lin(r), lin(g), lin(b)
        x = (0.4124564 * rl + 0.3575761 * gl + 0.1804375 * bl) / 0.95047
        y = 0.2126729 * rl + 0.7151522 * gl + 0.0721750 * bl
        z = (0.0193339 * rl + 0.1191920 * gl + 0.9503041 * bl) / 1.08883
        fx, fy, fz = _f(x), _f(y), _f(z)
        out.append((116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)))
    return out


def tone_stats(lab: list[tuple[float, float, float]]) -> dict:
    n = len(lab)
    mean_l = sum(p[0] for p in lab) / n
    var = sum((p[0] - mean_l) ** 2 for p in lab) / n
    chroma = sum(math.hypot(p[1], p[2]) for p in lab) / n
    return {"mean_l": mean_l, "std_l": math.sqrt(var), "mean_chroma": chroma}


def blurred_lab(image: "pngio.Image") -> list[tuple[float, float, float]]:
    return to_lab([box_blur(p) for p in reduce_to_800x600(image)])


def compare_labs(original: list, result: list, limits: dict = LIMITS) -> dict:
    a, b = tone_stats(original), tone_stats(result)
    des = sorted(math.dist(p, q) for p, q in zip(original, result))
    p95 = des[int(len(des) * 0.95) - 1]
    values = {
        "mean_l_delta": b["mean_l"] - a["mean_l"],
        "mean_chroma_ratio": b["mean_chroma"] / a["mean_chroma"] - 1 if a["mean_chroma"] else 0.0,
        "contrast_ratio": b["std_l"] / a["std_l"] - 1 if a["std_l"] else 0.0,
        "mean_delta_e": sum(des) / len(des),
        "p95_delta_e": p95,
    }
    values["contrast_delta"] = b["std_l"] - a["std_l"]
    failed = [k for k, limit in limits.items() if abs(values[k]) > limit and (k != "contrast_ratio" or abs(values["contrast_delta"]) > CONTRAST_FLOOR)]
    return {"original": a, "result": b, "values": values, "limits": dict(limits), "failed": failed, "pass": not failed}


def compare_files(original: Path, result: Path, limits: dict = LIMITS) -> dict:
    try:
        o, r = pngio.read(original), pngio.read(result)
    except (OSError, pngio.PngError) as error:
        raise ToneError(f"cannot read {original} or {result}: {error}") from error
    return compare_labs(blurred_lab(o), blurred_lab(r), limits)



def _in_rects(x: int, y: int, rects: list) -> bool:
    return any(r[0] <= x < r[2] and r[1] <= y < r[3] for r in rects)


def material_masks(spec: dict, common_exclude: list, lab: list) -> dict[str, list[float]]:
    """Per material a 0/1 mask over the 800 x 600 pixels, from the colour class of the ORIGINAL pixel, the zones and the excluded content."""
    exclude = list(spec.get("exclude", [])) + (list(common_exclude) if spec.get("strips") else [])
    skip = spec.get("skip_rows")
    masks: dict[str, list[float]] = {}
    for name, material in spec["materials"].items():
        klass, zone, not_zone = CLASSES[material["class"]], material.get("zone"), material.get("not_zone")
        mask = [0.0] * (TARGET_W * TARGET_H)
        for y in range(TARGET_H):
            if skip and skip[0] <= y < skip[1]:
                continue
            for x in range(TARGET_W):
                i = y * TARGET_W + x
                l, a, b = lab[i]
                if not klass(l, math.hypot(a, b), math.degrees(math.atan2(b, a)) % 360):
                    continue
                if zone and not _in_rects(x, y, zone):
                    continue
                if (not_zone and _in_rects(x, y, not_zone)) or _in_rects(x, y, exclude):
                    continue
                mask[i] = 1.0
        masks[name] = mask
    return masks


def _half(plane: list[float]) -> list[float]:
    out = []
    for y in range(0, TARGET_H, 2):
        top, bottom = y * TARGET_W, (y + 1) * TARGET_W
        out.extend((plane[top + x] + plane[top + x + 1] + plane[bottom + x] + plane[bottom + x + 1]) / 4 for x in range(0, TARGET_W, 2))
    return out


def masked_blur(lab: list, mask_half: list[float], coverage: list[float]) -> list[tuple[float, float, float]]:
    """Normalised blur of the Lab planes at 400 x 300 over the mask only (a masked average, radius 6 px at 800 x 600)."""
    planes = []
    for c in range(3):
        weighted = _half([p[c] * m for p, m in zip(lab, _upsample(mask_half))])
        planes.append(box_blur(weighted, HALF_RADIUS, HALF_W, HALF_H))
    return [(planes[0][i] / coverage[i], planes[1][i] / coverage[i], planes[2][i] / coverage[i]) if coverage[i] > 0 else (0.0, 0.0, 0.0) for i in range(HALF_W * HALF_H)]


def _upsample(half: list[float]) -> list[float]:
    out = []
    for y in range(TARGET_H):
        row = half[(y // 2) * HALF_W:(y // 2 + 1) * HALF_W]
        out.extend(v for v in row for _ in (0, 1))
    return out


def compare_material(lab_original: list, lab_result: list, mask: list[float], limits: dict = LIMITS) -> dict:
    """Tone comparison over one material mask; `{"absent": cells}` when fewer than MIN_CELLS cells are measured."""
    mask_half = _half(mask)
    coverage = box_blur(mask_half, HALF_RADIUS, HALF_W, HALF_H)
    cells = [i for i in range(HALF_W * HALF_H) if mask_half[i] >= MIN_COVER and coverage[i] >= MIN_COVER]
    if len(cells) < MIN_CELLS:
        return {"absent": len(cells), "pass": True, "failed": []}
    original = masked_blur(lab_original, mask_half, coverage)
    result = masked_blur(lab_result, mask_half, coverage)
    report = compare_labs([original[i] for i in cells], [result[i] for i in cells], limits)
    report["cells"] = len(cells)
    return report


def compare_screen(original: "pngio.Image", result: "pngio.Image", screen: dict, common_exclude: list, limits: dict = LIMITS) -> dict:
    """The per-material tone comparison of one screen plus `all` (the union of its materials). `pass` needs every present region within the limits."""
    lab_o = to_lab(reduce_to_800x600(original))
    lab_r = to_lab(reduce_to_800x600(result))
    masks = material_masks(screen, common_exclude, lab_o)
    union = [max(values) for values in zip(*masks.values())]
    regions = {name: compare_material(lab_o, lab_r, mask, limits) for name, mask in {**masks, "all": union}.items()}
    failed = [name for name, report in regions.items() if not report["pass"]]
    return {"regions": regions, "failed": failed, "pass": not failed}



_LIN_CACHE: dict[int, float] = {}


def _pixel_lab(r: int, g: int, b: int) -> tuple[float, float, float]:
    rl, gl, bl = (_LIN_CACHE.setdefault(v, _lin(v)) for v in (r, g, b))
    x = (0.4124564 * rl + 0.3575761 * gl + 0.1804375 * bl) / 0.95047
    y = 0.2126729 * rl + 0.7151522 * gl + 0.0721750 * bl
    z = (0.0193339 * rl + 0.1191920 * gl + 0.9503041 * bl) / 1.08883
    fx, fy, fz = _f(x), _f(y), _f(z)
    return 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)


def _from_lab(l: float, a: float, b: float) -> tuple[int, int, int]:
    fy = (l + 16) / 116
    fx, fz = fy + a / 500, fy - b / 200
    inv = lambda t: t ** 3 if t ** 3 > 216 / 24389 else (116 * t - 16) / (24389 / 27)
    x, y, z = inv(fx) * 0.95047, (l / (24389 / 27) if l <= 8 else fy ** 3), inv(fz) * 1.08883
    rl = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z
    gl = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z
    bl = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z
    enc = lambda v: 12.92 * v if v <= 0.0031308 else 1.055 * max(v, 0.0) ** (1 / 2.4) - 0.055
    return tuple(max(0, min(255, round(255 * enc(v)))) for v in (rl, gl, bl))


def fit_material(original: "pngio.Image", result: "pngio.Image", screen: dict, common_exclude: list, material: str, passes: int = 4) -> tuple["pngio.Image", dict]:
    """Region tone fit: shift the L* and scale the chroma of the result inside one material so that its blurred mean L* and mean chroma equal the
    ORIGINAL's (texture, shapes and hue stay). The weight is the material mask of the ORIGINAL, softened. One pass lands near the target (the
    layout of the result differs a little from the ORIGINAL), so it repeats up to `passes` times until both means are within 0.3 L* and 2 %."""
    lab_o = to_lab(reduce_to_800x600(original))
    mask = material_masks(screen, common_exclude, lab_o)[material]
    mask_half = _half(mask)
    coverage = box_blur(mask_half, HALF_RADIUS, HALF_W, HALF_H)
    cells = [i for i in range(HALF_W * HALF_H) if mask_half[i] >= MIN_COVER and coverage[i] >= MIN_COVER]
    if len(cells) < MIN_CELLS:
        raise ToneError(f"material {material} is absent in this screen")
    a = tone_stats([v for i, v in enumerate(masked_blur(lab_o, mask_half, coverage)) if mask_half[i] >= MIN_COVER and coverage[i] >= MIN_COVER])
    # weight: where the material has a zone, the zone itself (minus the excluded content), so that the fold, the text lines and the glyphs of a page are
    # all fitted, with a soft edge; a hard colour-class mask would leave them untouched and print the text shapes as ghosts; without a zone the colour
    # class mask blurred wide and stretched
    zone = screen["materials"][material].get("zone")
    if zone:
        exclude = list(screen.get("exclude", [])) + (list(common_exclude) if screen.get("strips") else [])
        weight = [1.0 if _in_rects(x, y, zone) and not _in_rects(x, y, exclude) else 0.0 for y in range(TARGET_H) for x in range(TARGET_W)]
        soft = box_blur(weight, 3)
    else:
        soft = [min(1.0, max(0.0, (v - FIT_LOW) / (FIT_HIGH - FIT_LOW))) for v in box_blur(mask, FIT_RADIUS)]
    current, steps = result, []
    for _ in range(passes):
        blur_r = masked_blur(to_lab(reduce_to_800x600(current)), mask_half, coverage)
        b = tone_stats([blur_r[i] for i in cells])
        shift, scale = a["mean_l"] - b["mean_l"], a["mean_chroma"] / b["mean_chroma"]
        steps.append({"shift_l": shift, "chroma_scale": scale, "before": b})
        if abs(shift) <= 0.3 and abs(scale - 1) <= 0.02:
            break
        out = bytearray(current.data)
        for y in range(current.h):
            row = min(TARGET_H - 1, y * TARGET_H // current.h) * TARGET_W
            for x in range(current.w):
                w = soft[row + min(TARGET_W - 1, x * TARGET_W // current.w)]
                if w < 0.02:
                    continue
                i = (y * current.w + x) * 4
                l, aa, bb = _pixel_lab(out[i], out[i + 1], out[i + 2])
                k = 1 + w * (scale - 1)
                out[i:i + 3] = bytes(_from_lab(max(0.0, min(100.0, l + w * shift)), aa * k, bb * k))
        current = pngio.Image(current.w, current.h, out)
    return current, {"material": material, "original": a, "passes": steps}


def load_masks(path: Path = MASKS_FILE) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def compare_screen_files(screen_id: str, original: Path, result: Path, masks: dict | None = None, limits: dict = LIMITS) -> dict:
    masks = masks or load_masks()
    if screen_id not in masks["screens"]:
        raise ToneError(f"no tone masks for {screen_id}")
    try:
        o, r = pngio.read(original), pngio.read(result)
    except (OSError, pngio.PngError) as error:
        raise ToneError(f"cannot read {original} or {result}: {error}") from error
    return compare_screen(o, r, masks["screens"][screen_id], masks["strip_counters"], limits)


def describe(name: str, report: dict) -> str:
    if "absent" in report:
        return f"  -    {name}: absent ({report['absent']} cells)"
    v, a, b = report["values"], report["original"], report["result"]
    flags = " ".join(f"{k}!" for k in report["failed"])
    return (f"{'PASS' if report['pass'] else 'FAIL'} {name}: L* {a['mean_l']:.1f} -> {b['mean_l']:.1f} ({v['mean_l_delta']:+.2f}), "
            f"chroma {a['mean_chroma']:.1f} -> {b['mean_chroma']:.1f} ({v['mean_chroma_ratio']:+.1%}), "
            f"contrast {a['std_l']:.1f} -> {b['std_l']:.1f} ({v['contrast_ratio']:+.1%}), "
            f"dE mean {v['mean_delta_e']:.2f} p95 {v['p95_delta_e']:.2f}" + (f"  [{flags}]" if flags else ""))


def limits_line() -> str:
    lim = LIMITS
    return (f"limits: |dL*| <= {lim['mean_l_delta']:g}, |chroma| <= {lim['mean_chroma_ratio']:.0%}, |contrast| <= {lim['contrast_ratio']:.0%}, "
            f"mean dE <= {lim['mean_delta_e']:g}, p95 dE <= {lim['p95_delta_e']:g}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    one = sub.add_parser("compare")
    one.add_argument("original", type=Path)
    one.add_argument("result", type=Path)
    many = sub.add_parser("batch")
    many.add_argument("--originals", type=Path, required=True)
    many.add_argument("--results", type=Path, required=True)
    many.add_argument("--ids")
    fit = sub.add_parser("fit", help="fit the tone of one material of a result to the ORIGINAL (region tone fit)")
    fit.add_argument("screen")
    fit.add_argument("original", type=Path)
    fit.add_argument("result", type=Path)
    fit.add_argument("output", type=Path)
    fit.add_argument("--material", required=True)
    for p in (one, many):
        p.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    if args.command == "fit":
        try:
            masks = load_masks()
            fitted, info = fit_material(pngio.read(args.original), pngio.read(args.result), masks["screens"][args.screen], masks["strip_counters"], args.material)
        except (ToneError, KeyError, OSError, pngio.PngError) as error:
            print(f"TONE ERROR: {error}", file=sys.stderr)
            return 2
        pngio.write(args.output, fitted)
        print(json.dumps(info, indent=2))
        return 0
    try:
        if args.command == "compare":
            reports = {args.result.stem: {"regions": {"all": compare_files(args.original, args.result)}}}
        else:
            masks = load_masks()
            ids = args.ids.split(",") if args.ids else sorted(p.stem for p in args.results.glob("ctx-*.png"))
            reports = {i: compare_screen_files(i, args.originals / f"{i}-800x600.png", args.results / f"{i}.png", masks) for i in ids}
    except ToneError as error:
        print(f"TONE ERROR: {error}", file=sys.stderr)
        return 2
    for report in reports.values():
        report["failed"] = [n for n, r in report["regions"].items() if not r["pass"]]
        report["pass"] = not report["failed"]
    if args.json:
        print(json.dumps(reports, indent=2))
    else:
        print(limits_line())
        for name, report in reports.items():
            print(f"{'PASS' if report['pass'] else 'FAIL'} {name}")
            for region, sub_report in report["regions"].items():
                print(describe(region, sub_report))
    ok = all(r["pass"] for r in reports.values())
    print(f"TONE CHECK {'PASS' if ok else 'FAIL'}: {sum(r['pass'] for r in reports.values())}/{len(reports)} within the limits", file=sys.stderr if args.json else sys.stdout)
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
