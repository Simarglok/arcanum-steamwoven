"""ARC-RES-2457 method A as the player sees it (owner pick 2026-11-21): the Codex result (`surface_study.py`, zones as single images) is taken for the WOOD only.
Everything that is not wood (text, counters, digits, fields filled in play, portrait circle, grids, brass and iron parts, buttons) is the ORIGINAL's, by the
ORIGINAL's mask, as a stand-in for the kit parts; the wood of the Codex result is cleaned of its random dark specks, the original's rows of dots are put
back, and its tone is fitted to the ORIGINAL's wood (mean L*, contrast, chroma). Needs ImageMagick (`magick`). Local files only, no art in git.

    python3 -B tools/steamwoven_ui/surface_compose.py compose --original O.png --a A.png --out OUT.png --work DIR
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

try:
    from . import method_c, pngio, tone_check
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import method_c  # type: ignore
    import pngio  # type: ignore
    import tone_check  # type: ignore

SPECK_THRESHOLD = 12      # 0..255: a pixel this much darker than the median of its 9 px horizontal neighbours is a speck
DOT_THRESHOLD = 6         # the same for the ORIGINAL, whose rows of dots are kept (low: the dots are faint after the x3.6 upscale)
DOT_MAX_PIXELS = 12       # a dot is a small component
MEDIAN = "9x1"            # horizontal median: the wood grain runs along x and survives, dots and short scratches do not
ROW_TOLERANCE = 3         # px: dots of one row differ in y by at most this
ROW_GAP = 40              # px: neighbouring dots of one row are at most this far apart
ROW_MIN_DOTS = 6
MASK_FEATHER = 3.0
JOINS = (148, 1588)       # rows where the strips meet the window zone (surface_study.ZONES); the ORIGINAL's rows are kept there
JOIN_KEEP = 6             # px each side of a join that stay the ORIGINAL's
JOIN_FEATHER = 2.0


class ComposeError(Exception):
    pass


def run(command: list[str]) -> None:
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise ComposeError(f"{command[0]} failed: {result.stderr.strip()[:300]}")


def gray_bytes(path: Path, extra: list[str] | None = None) -> bytes:
    result = subprocess.run(["magick", str(path), "-colorspace", "Gray", *(extra or []), "-depth", "8", "gray:-"], capture_output=True)
    if result.returncode != 0:
        raise ComposeError(f"magick failed: {result.stderr.decode(errors='replace').strip()[:300]}")
    return result.stdout


def components(points: set[int], width: int) -> list[list[int]]:
    """8-connected components of a set of pixel indices."""
    seen: set[int] = set()
    out = []
    for start in sorted(points):
        if start in seen:
            continue
        seen.add(start)
        stack, comp = [start], []
        while stack:
            q = stack.pop()
            comp.append(q)
            y, x = divmod(q, width)
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    r = (y + dy) * width + x + dx
                    if r in points and r not in seen:
                        seen.add(r)
                        stack.append(r)
        out.append(comp)
    return out


def dot_rows(centres: list[tuple[float, float]], tolerance: float = ROW_TOLERANCE, gap: float = ROW_GAP, min_dots: int = ROW_MIN_DOTS) -> set[int]:
    """Indices of the dots that belong to a row: at least `min_dots` dots whose y is within `tolerance` of the row's first dot and whose neighbours along x
    are at most `gap` apart. `centres` are (y, x)."""
    order = sorted(range(len(centres)), key=lambda i: (centres[i][0], centres[i][1]))
    kept: set[int] = set()
    used: set[int] = set()
    for i in order:
        if i in used:
            continue
        band = sorted((j for j in order if j not in used and abs(centres[j][0] - centres[i][0]) <= tolerance), key=lambda j: centres[j][1])
        chain: list[int] = []
        for j in band:
            if chain and centres[j][1] - centres[chain[-1]][1] > gap:
                if len(chain) >= min_dots:
                    kept.update(chain)
                used.update(chain)
                chain = []
            chain.append(j)
        if len(chain) >= min_dots:
            kept.update(chain)
        used.update(chain)
    return kept


def speck_map(path: Path, threshold: int, width: int, height: int, work: Path, name: str) -> bytes:
    """Per pixel 255 where the luma is more than `threshold` darker than its horizontal median, else 0."""
    gray, median = work / f"{name}-gray.png", work / f"{name}-median.png"
    run(["magick", str(path), "-colorspace", "Gray", "-alpha", "off", str(gray)])
    run(["magick", str(gray), "-statistic", "Median", MEDIAN, str(median)])
    out = work / f"{name}-speck.png"
    run(["magick", str(median), str(gray), "-compose", "Minus", "-composite", "-threshold", f"{threshold / 255 * 100:.2f}%", str(out)])
    data = gray_bytes(out)
    if len(data) != width * height:
        raise ComposeError(f"{name}: speck map is {len(data)} bytes, expected {width * height}")
    return data


def write_gray(data: bytes, width: int, height: int, out: Path) -> None:
    result = subprocess.run(["magick", "-size", f"{width}x{height}", "-depth", "8", "gray:-", str(out)], input=data, capture_output=True)
    if result.returncode != 0:
        raise ComposeError(f"magick failed: {result.stderr.decode(errors='replace').strip()[:300]}")


def despeckle(a: Path, out: Path, work: Path, width: int, height: int) -> int:
    """The Codex wood without its dark specks: each speck pixel (grown by 1 px) takes the horizontal median of its neighbours. Returns the speck pixel count."""
    specks = speck_map(a, SPECK_THRESHOLD, width, height, work, "a")
    mask = work / "a-speck-grown.png"
    run(["magick", str(work / "a-speck.png"), "-morphology", "Dilate", "Disk:1", str(mask)])
    median = work / "a-median-rgb.png"
    run(["magick", str(a), "-alpha", "off", "-statistic", "Median", MEDIAN, str(median)])
    run(["magick", str(a), "-alpha", "off", str(median), str(mask), "-composite", str(out)])
    return sum(1 for b in specks if b)


def original_dots(original: Path, wood_class_mask: Path, work: Path, width: int, height: int) -> tuple[Path, int]:
    """A mask of the ORIGINAL's rows of dots inside its wood (the dots grown by 1 px), written as a grey PNG; also the number of dots kept."""
    specks = speck_map(original, DOT_THRESHOLD, width, height, work, "o")
    wood = gray_bytes(wood_class_mask)
    points = {i for i, b in enumerate(specks) if b and wood[i] > 128}
    comps = [c for c in components(points, width) if len(c) <= DOT_MAX_PIXELS]
    centres = [(sum(q // width for q in c) / len(c), sum(q % width for q in c) / len(c)) for c in comps]
    keep = dot_rows(centres)
    data = bytearray(width * height)
    for i in keep:
        for q in comps[i]:
            data[q] = 255
    out = work / "o-dots.png"
    raw = work / "o-dots-raw.png"
    write_gray(bytes(data), width, height, raw)
    run(["magick", str(raw), "-morphology", "Dilate", "Disk:1", str(out)])
    return out, len(keep)


def wood_cells(original: "pngio.Image", masks: dict, screen_id: str = method_c.SCREEN) -> tuple[list[float], list[float], set[int]]:
    """The ORIGINAL's wood class mask, its coverage and the cells that count, as `tone_check.fit_material` reads a material."""
    screen = masks["screens"][screen_id]
    lab = tone_check.to_lab(tone_check.reduce_to_800x600(original))
    mask = tone_check.material_masks({**screen, "materials": {"wood": screen["materials"]["wood"]}}, masks["strip_counters"], lab)["wood"]
    mask_half = tone_check._half(mask)
    coverage = tone_check.box_blur(mask_half, tone_check.HALF_RADIUS, tone_check.HALF_W, tone_check.HALF_H)
    cells = {i for i in range(tone_check.HALF_W * tone_check.HALF_H) if mask_half[i] >= tone_check.MIN_COVER and coverage[i] >= tone_check.MIN_COVER}
    if len(cells) < tone_check.MIN_CELLS:
        raise ComposeError("no wood in the original")
    return mask, coverage, cells


def wood_stats(image: "pngio.Image", mask: list[float], coverage: list[float], cells: set[int]) -> dict:
    lab = tone_check.to_lab(tone_check.reduce_to_800x600(image))
    blurred = tone_check.masked_blur(lab, mask, coverage)
    return tone_check.tone_stats([v for i, v in enumerate(blurred) if i in cells])


def fit_numbers(original: dict, result: dict) -> dict:
    """The L* shift is applied after scaling the contrast about the result's mean: L' = m_o + s (L - m_r), chroma' = c chroma."""
    s = original["std_l"] / result["std_l"] if result["std_l"] else 1.0
    c = original["mean_chroma"] / result["mean_chroma"] if result["mean_chroma"] else 1.0
    return {"mean_original": original["mean_l"], "mean_result": result["mean_l"], "contrast_scale": min(2.0, max(0.5, s)), "chroma_scale": min(4.0, max(0.25, c))}


def apply_fit(image: "pngio.Image", weight: "pngio.Image", numbers: dict) -> "pngio.Image":
    """L* and chroma of the pixels where the grey `weight` image is not 0, blended by the weight."""
    out = bytearray(image.data)
    wd = weight.data
    for p in range(image.w * image.h):
        w = wd[p * 4] / 255
        if w < 0.01:
            continue
        i = p * 4
        l, a, b = tone_check._pixel_lab(out[i], out[i + 1], out[i + 2])
        l2 = numbers["mean_original"] + numbers["contrast_scale"] * (l - numbers["mean_result"])
        k = 1 + w * (numbers["chroma_scale"] - 1)
        out[i:i + 3] = bytes(tone_check._from_lab(max(0.0, min(100.0, l + w * (l2 - l))), a * k, b * k))
    return pngio.Image(image.w, image.h, out)


def keep_joins(solid: Path, out: Path, width: int, height: int) -> None:
    """The wood weight with the rows around each strip join set to 0 (softly), so the join is the ORIGINAL's designed edge and the Codex wood is not cut
    by two independently generated zones."""
    draw = []
    for j in JOINS:
        draw += ["-draw", f"rectangle 0,{j - JOIN_KEEP} {width - 1},{j + JOIN_KEEP - 1}"]
    keep = solid.with_name("join-keep.png")
    run(["magick", "-size", f"{width}x{height}", "xc:white", "-fill", "black", *draw, "-blur", f"0x{JOIN_FEATHER}", "-colorspace", "Gray", str(keep)])
    run(["magick", str(solid), str(keep), "-compose", "Multiply", "-composite", str(out)])


def compose(original: Path, a: Path, out: Path, work: Path) -> dict:
    work.mkdir(parents=True, exist_ok=True)
    img_o = pngio.read(original)
    w, h = img_o.w, img_o.h
    if pngio.read(a).w != w:
        raise ComposeError("the original and the Codex result must have the same size")
    masks = tone_check.load_masks()
    solid, raw = work / "wood-solid.png", work / "wood-class.png"
    method_c.write_mask(original, solid, feather=MASK_FEATHER, solid=True)
    method_c.write_mask(original, raw)
    keep_joins(solid, solid, w, h)
    clean = work / "a-clean.png"
    specks = despeckle(a, clean, work, w, h)
    dots, dot_count = original_dots(original, raw, work, w, h)
    mask, coverage, cells = wood_cells(img_o, masks)
    target = wood_stats(img_o, mask, coverage, cells)
    before = wood_stats(pngio.read(clean), mask, coverage, cells)
    numbers = fit_numbers(target, before)
    fitted_img = apply_fit(pngio.read(clean), pngio.read(solid), numbers)  # pixels outside the wood weight are untouched, and replaced by the original below
    fitted = work / "a-fitted.png"
    pngio.write(fitted, fitted_img)
    # the Codex wood where the ORIGINAL is wood (soft edge); the ORIGINAL's rows of dots on top
    run(["magick", str(original), "-alpha", "off", str(fitted), str(solid), "-composite", str(original), str(dots), "-composite", str(out)])
    after = wood_stats(pngio.read(out), mask, coverage, cells)
    return {"out": str(out), "speck_pixels": specks, "dots_kept": dot_count, "fit": numbers, "wood_target": target, "wood_before": before, "wood_after": after}


def describe(info: dict) -> str:
    f = lambda s: f"L* {s['mean_l']:.2f}, contrast {s['std_l']:.2f}, chroma {s['mean_chroma']:.2f}"
    return (f"COMPOSE wrote {info['out']}; {info['speck_pixels']} speck pixels removed, {info['dots_kept']} dots of the original's rows kept\n"
            f"  wood original: {f(info['wood_target'])}\n  wood Codex clean: {f(info['wood_before'])}\n  wood composed: {f(info['wood_after'])}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("compose")
    p.add_argument("--original", type=Path, required=True)
    p.add_argument("--a", type=Path, required=True, help="method A assembled to the master size (2880 x 2160)")
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--work", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        info = compose(args.original, args.a, args.out, args.work)
    except (ComposeError, method_c.MethodCError, tone_check.ToneError, pngio.PngError, KeyError, OSError) as error:
        print(f"SURFACE COMPOSE ERROR: {error!r}", file=sys.stderr)
        return 2
    print(describe(info))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
