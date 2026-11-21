"""ARC-RES-2457: surface metrics for the method comparison of a 2880 px strip or window background (seam ratio at a join, repetition as the
autocorrelation peak, sharpness at the x1 and x1/3 steps). Standard library only; it reads and writes no art, it measures PNG files the caller names."""
from __future__ import annotations

import argparse
import json
import sys
from itertools import accumulate
from operator import mul
from pathlib import Path

try:
    from . import pngio
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import pngio  # type: ignore

REDUCE = 8            # repetition is read on the luma plane reduced by this factor (keeps the pure-Python cost low and ignores pixel noise)
HIGH_PASS_RADIUS = 3  # radius (reduced pixels) of the box blur removed before the autocorrelation: drops the macro tone, keeps the texture
MIN_AXIS = 32         # an axis shorter than this (reduced pixels) has no repetition reading
SEAM_BAND = 24        # lines on each side of a join that give the "inside" step


class MetricError(Exception):
    pass


def luma(image: "pngio.Image") -> list[float]:
    d = image.data
    return [(299 * d[i] + 587 * d[i + 1] + 114 * d[i + 2]) / 1000 for i in range(0, image.w * image.h * 4, 4)]


def reduce_plane(plane: list[float], w: int, h: int, factor: int) -> tuple[list[float], int, int]:
    """Box reduction by a whole factor (the remainder rows and columns are dropped)."""
    if factor < 1:
        raise MetricError("the reduction factor must be at least 1")
    if factor == 1:
        return plane, w, h
    w2, h2 = w // factor, h // factor
    if w2 < 1 or h2 < 1:
        raise MetricError(f"{w} x {h} px cannot be reduced by {factor}")
    out: list[float] = []
    area = factor * factor
    for by in range(h2):
        sums = [0.0] * w2
        for y in range(by * factor, (by + 1) * factor):
            row = plane[y * w:y * w + w2 * factor]
            for bx in range(w2):
                sums[bx] += sum(row[bx * factor:(bx + 1) * factor])
        out.extend(s / area for s in sums)
    return out, w2, h2


def gradient_stats(plane: list[float], w: int, h: int, weights: list[float] | None = None) -> dict:
    """Mean and 95th percentile of the central-difference gradient magnitude (|dx| + |dy|) and the variance of the 4-neighbour Laplacian. With `weights`
    (1 = counted) only the pixels whose four neighbours are all counted are read."""
    if w < 3 or h < 3:
        raise MetricError("sharpness needs at least 3 x 3 px")
    grads, laps = [], []
    for y in range(1, h - 1):
        base = y * w
        for x in range(1, w - 1):
            i = base + x
            if weights is not None and not (weights[i] and weights[i - 1] and weights[i + 1] and weights[i - w] and weights[i + w]):
                continue
            gx, gy = (plane[i + 1] - plane[i - 1]) / 2, (plane[i + w] - plane[i - w]) / 2
            grads.append(abs(gx) + abs(gy))
            laps.append(plane[i - 1] + plane[i + 1] + plane[i - w] + plane[i + w] - 4 * plane[i])
    if not grads:
        return {"mean_gradient": 0.0, "p95_gradient": 0.0, "laplacian_variance": 0.0, "pixels": 0}
    grads.sort()
    mean_lap = sum(laps) / len(laps)
    return {"mean_gradient": sum(grads) / len(grads), "p95_gradient": grads[int(0.95 * (len(grads) - 1))],
            "laplacian_variance": sum((v - mean_lap) ** 2 for v in laps) / len(laps), "pixels": len(grads)}


def mask_weights(mask: "pngio.Image", w: int, h: int) -> list[float]:
    """The grey mask resampled (nearest) to w x h as 1.0 where it is at least half white, else 0.0."""
    return [1.0 if mask.data[((y * mask.h // h) * mask.w + (x * mask.w // w)) * 4] >= 128 else 0.0 for y in range(h) for x in range(w)]


def sharpness(image: "pngio.Image", mask: "pngio.Image | None" = None) -> dict:
    """Sharpness at the master step (x1) and after a reduction by 3 (x1/3); `ratio_third` = x1/3 over x1 of the mean gradient. The gradient is per pixel,
    so a smooth or upscaled picture reads a ratio above 1 (about 3 for a smooth ramp) and a picture with real detail at the master size reads nearer 1: a
    ratio near 1 means sharp, not soft; compare it between candidates, do not gate it unnormalised."""
    plane = luma(image)
    weights = mask_weights(mask, image.w, image.h) if mask else None
    x1 = gradient_stats(plane, image.w, image.h, weights)
    third, w3, h3 = reduce_plane(plane, image.w, image.h, 3)
    x3 = gradient_stats(third, w3, h3, None if weights is None else [1.0 if v >= 0.99 else 0.0 for v in reduce_plane(weights, image.w, image.h, 3)[0]])
    return {"x1": x1, "x1_3": x3, "ratio_third": x3["mean_gradient"] / x1["mean_gradient"] if x1["mean_gradient"] else 0.0}


def _box_blur(plane: list[float], w: int, h: int, radius: int) -> list[float]:
    """Box blur with clamped edges through a summed-area table."""
    stride = w + 1
    table = [0.0] * (stride * (h + 1))
    for y in range(h):
        row_sums = list(accumulate(plane[y * w:(y + 1) * w], initial=0.0))
        above = table[y * stride:(y + 1) * stride]
        table[(y + 1) * stride:(y + 2) * stride] = [a + r for a, r in zip(above, row_sums)]
    out = []
    for y in range(h):
        y0, y1 = max(0, y - radius), min(h, y + radius + 1)
        for x in range(w):
            x0, x1 = max(0, x - radius), min(w, x + radius + 1)
            total = table[y1 * stride + x1] - table[y0 * stride + x1] - table[y1 * stride + x0] + table[y0 * stride + x0]
            out.append(total / ((y1 - y0) * (x1 - x0)))
    return out


def _axis_autocorrelation(rows: list[list[float]]) -> tuple[float | None, int | None]:
    """Peak of the normalised autocorrelation summed over `rows` at lags beyond its first non-positive value (the texture's own width) up to
    half the row; None when the rows are too short or flat."""
    n = len(rows[0])
    if n < MIN_AXIS:
        return None, None
    energy = sum(sum(v * v for v in row) for row in rows)
    if energy <= 1e-9:
        return None, None
    acf = [1.0]
    for lag in range(1, n // 2 + 1):
        total = sum(sum(map(mul, row[:-lag], row[lag:])) for row in rows)
        acf.append(total / energy * n / (n - lag))
    start = next((lag for lag, v in enumerate(acf) if v <= 0), None)
    if start is None:
        start = n // 4          # never decorrelates: read the repeat beyond a quarter of the axis
    start = max(start, 2)
    best = max(range(start, len(acf)), key=lambda lag: acf[lag], default=None)
    return (acf[best], best) if best is not None and best < len(acf) else (None, None)


def repetition(image: "pngio.Image", factor: int = REDUCE, mask: "pngio.Image | None" = None) -> dict:
    """Autocorrelation peak along x and along y of the high-passed luma, with its lag in master pixels. A value near 1 at some lag is a repeat
    (a tiled texture); an unrepeated surface has small peaks."""
    plane, w, h = reduce_plane(luma(image), image.w, image.h, factor)
    blurred = _box_blur(plane, w, h, HIGH_PASS_RADIUS)
    high = [a - b for a, b in zip(plane, blurred)]
    if mask:   # only the masked (e.g. wood) cells carry signal; the rest is zero
        cover, _, _ = reduce_plane(mask_weights(mask, image.w, image.h), image.w, image.h, factor)
        high = [v if c >= 0.99 else 0.0 for v, c in zip(high, cover)]
    rows = [high[y * w:(y + 1) * w] for y in range(h)]
    cols = [high[x::w] for x in range(w)]
    result = {}
    for axis, data in (("x", rows), ("y", cols)):
        peak, lag = _axis_autocorrelation(data)
        result[axis] = {"peak": peak, "lag_px": None if lag is None else lag * factor}
    peaks = [v["peak"] for v in result.values() if v["peak"] is not None]
    result["peak"] = max(peaks) if peaks else None
    return result


def join_seam(image: "pngio.Image", axis: str, position: int, band: int = SEAM_BAND) -> float:
    """Seam ratio at a join: the mean colour step across it (line position - 1 to line position) over the mean step between neighbouring lines
    in `band` lines on each side. axis "y" = a horizontal join at row `position`, "x" = a vertical join at column `position`. 1.0 = as smooth as
    the inside; a flat inside with a jump gives 1e9."""
    w, h, d = image.w, image.h, image.data
    size = h if axis == "y" else w
    if axis not in ("x", "y") or not 1 <= position < size:
        raise MetricError(f"no join {axis}={position} in a {w} x {h} px image")

    def step(a: int, b: int) -> float:
        if axis == "y":
            return sum(abs(d[(a * w + x) * 4 + c] - d[(b * w + x) * 4 + c]) for x in range(w) for c in range(3)) / (w * 3)
        return sum(abs(d[(y * w + a) * 4 + c] - d[(y * w + b) * 4 + c]) for y in range(h) for c in range(3)) / (h * 3)

    inner_lines = [(i, i + 1) for i in range(max(0, position - band), position - 1)] + [(i, i + 1) for i in range(position, min(size - 1, position + band))]
    if not inner_lines:
        raise MetricError("a join needs lines on at least one side to compare with")
    inner = sum(step(a, b) for a, b in inner_lines) / len(inner_lines)
    join = step(position - 1, position)
    return join / inner if inner else (1.0 if join == 0 else 1e9)


def measure(image: "pngio.Image", joins: list[tuple[str, int]] | None = None, mask: "pngio.Image | None" = None) -> dict:
    """`mask`: a grey image (any size, white = counted) limiting repetition and sharpness to one material (the wood); seams are always whole-line."""
    report = {"size": [image.w, image.h], "masked": mask is not None, "repetition": repetition(image, mask=mask), "sharpness": sharpness(image, mask)}
    if joins:
        report["seams"] = [{"join": f"{axis}={pos}", "ratio": join_seam(image, axis, pos)} for axis, pos in joins]
    return report


def parse_join(text: str) -> tuple[str, int]:
    axis, _, value = text.partition("=")
    if axis not in ("x", "y") or not value.isdigit():
        raise argparse.ArgumentTypeError(f"a join is y=ROW or x=COLUMN, not {text!r}")
    return axis, int(value)


def describe(name: str, report: dict) -> str:
    rep, sh = report["repetition"], report["sharpness"]
    fmt = lambda v: "n/a" if v is None else f"{v:.3f}"
    seams = ", ".join(f"{s['join']} {s['ratio']:.2f}" for s in report.get("seams", [])) or "none"
    return (f"{name}: {report['size'][0]} x {report['size'][1]} px; repetition peak x {fmt(rep['x']['peak'])} (lag {rep['x']['lag_px']}), "
            f"y {fmt(rep['y']['peak'])} (lag {rep['y']['lag_px']}); sharpness x1 {sh['x1']['mean_gradient']:.2f}, x1/3 {sh['x1_3']['mean_gradient']:.2f} "
            f"(ratio {sh['ratio_third']:.2f}); seams {seams}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("files", nargs="+", type=Path)
    parser.add_argument("--join", action="append", type=parse_join, default=[], help="y=ROW or x=COLUMN; repeatable (applies to every file)")
    parser.add_argument("--mask", type=Path, help="grey PNG (white = counted) limiting repetition and sharpness to one material")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    reports = {}
    try:
        mask = pngio.read(args.mask) if args.mask else None
        for path in args.files:
            reports[str(path)] = measure(pngio.read(path), args.join, mask)
    except (MetricError, pngio.PngError, OSError) as error:
        print(f"SURFACE METRICS ERROR: {error!r}", file=sys.stderr)
        return 2
    if args.json:
        print(json.dumps(reports, indent=2))
    else:
        for name, report in reports.items():
            print(describe(name, report))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
