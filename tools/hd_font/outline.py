"""Outline tracing of 1x coverage masks into 4x coverage frames (ARC-DEV-355; method in docs/workflow/hd-font.md).

Needs numpy, scipy, Pillow and potracer (tools/hd_font/requirements.lock).  Deterministic: the same masks give the same bytes.

Per glyph: pad by 3 px, cubic-upsample to 8x (pixel-centre aligned), 6 rounds of iterative back-projection so the 8x field
reproduces the 1x masks when box-reduced, binarize at the **mass-preserving level** (the level at or below 0.42 at which the
binary area equals the mask's total coverage), Potrace curve fit (turdsize 24 at 8x, alphamax 0.6, optimizing curves, tolerance 0.2),
even-odd fill rasterized at 16x and box-reduced to 4x.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
from PIL import Image
from scipy import ndimage
import potrace

UPSAMPLE = 8
PAD = 3
BACK_PROJECTIONS = 6
TURD_SIZE = 24
ALPHA_MAX = 0.6
OPT_TOLERANCE = 0.2
OUTPUT_SCALE = 4
SUPERSAMPLE = 4  # per output pixel: 4 x 4 = 16 samples, so 16x the 1x grid
BISECTION_STEPS = 14
LEVEL_LOW, LEVEL_HIGH, LEVEL_CAP = 0.05, 0.95, 0.42
CURVE_POINTS = 12


@dataclass(frozen=True)
class GlyphTrace:
    """One traced glyph: `coverage` is a (4h, 4w) float array in [0, 1] (16 samples per pixel), plus measurements."""

    coverage: np.ndarray
    nodes: int
    level: float
    mass: float          # the 1x mask's total coverage, in pixels
    traced_mass: float   # the outline's coverage at 4x over 16, in 1x pixels (the same unit)
    mean_abs_error: float  # outline reduced to 1x against the mask, over pixels either touches
    vanished: bool       # the mask had ink worth a pixel's tenth and the outline has none


def upsample(field: np.ndarray, factor: int, order: int = 3) -> np.ndarray:
    """Pixel-centre aligned resample: output pixel j reads input coordinate (j + 0.5) / factor - 0.5."""
    height, width = field.shape
    ys = (np.arange(height * factor) + 0.5) / factor - 0.5
    xs = (np.arange(width * factor) + 0.5) / factor - 0.5
    grid_y, grid_x = np.meshgrid(ys, xs, indexing="ij")
    out = ndimage.map_coordinates(field, [grid_y, grid_x], order=order, mode="constant", cval=0.0)
    return np.clip(out, 0.0, 1.0)


def box_down(field: np.ndarray, factor: int) -> np.ndarray:
    height, width = field.shape
    return field.reshape(height // factor, factor, width // factor, factor).mean(axis=(1, 3))


def back_project(coverage: np.ndarray, factor: int = UPSAMPLE, rounds: int = BACK_PROJECTIONS) -> np.ndarray:
    field = upsample(coverage, factor)
    for _ in range(rounds):
        residual = coverage - box_down(field, factor)
        field = np.clip(field + upsample(residual, factor, order=1), 0.0, 1.0)
    return field


def mass_level(field: np.ndarray, mass: float, factor: int = UPSAMPLE) -> float:
    """The level in [0.05, 0.95] at which the area above it equals `mass` (1x pixels), capped at LEVEL_CAP, 0.42 (bisection, 14 steps)."""
    low, high = LEVEL_LOW, LEVEL_HIGH
    for _ in range(BISECTION_STEPS):
        middle = (low + high) / 2
        if (field >= middle).sum() / (factor * factor) > mass:
            low = middle
        else:
            high = middle
    return min(LEVEL_CAP, (low + high) / 2)


def _bezier(p0: np.ndarray, p1: np.ndarray, p2: np.ndarray, p3: np.ndarray) -> np.ndarray:
    t = np.linspace(0.0, 1.0, CURVE_POINTS, endpoint=False)[:, None]
    return ((1 - t) ** 3) * p0 + 3 * ((1 - t) ** 2) * t * p1 + 3 * (1 - t) * t * t * p2 + (t ** 3) * p3


def outline_paths(binary: np.ndarray, factor: int = UPSAMPLE) -> tuple[list[np.ndarray], int]:
    """Potrace curves of a boolean field as flattened closed polygons in 1x pixel units (x, y), and their node count."""
    bitmap = potrace.Bitmap(~binary)
    curves = bitmap.trace(turdsize=TURD_SIZE, turnpolicy=potrace.POTRACE_TURNPOLICY_MINORITY,
                          alphamax=ALPHA_MAX, opticurve=True, opttolerance=OPT_TOLERANCE)
    polygons: list[np.ndarray] = []
    nodes = 0
    for curve in curves:
        current = np.array([curve.start_point.x, curve.start_point.y], float)
        points: list[np.ndarray] = []
        for segment in curve.segments:
            nodes += 1
            end = np.array([segment.end_point.x, segment.end_point.y], float)
            if segment.is_corner:
                corner = np.array([segment.c.x, segment.c.y], float)
                points += [current, corner]
            else:
                c1 = np.array([segment.c1.x, segment.c1.y], float)
                c2 = np.array([segment.c2.x, segment.c2.y], float)
                points += list(_bezier(current, c1, c2, end))
            current = end
        polygons.append(np.array(points) / factor)
    return polygons, nodes


def rasterize(polygons: list[np.ndarray], shape: tuple[int, int], scale: int, supersample: int = SUPERSAMPLE) -> np.ndarray:
    """Even-odd area coverage of closed polygons (1x units) on a grid `scale` times finer than 1x, sampled at `supersample` x
    `supersample` points per output pixel.  `shape` is the 1x (height, width).

    Exact scanline fill: a sample is inside when its centre is, so a pixel-aligned square covers exactly its area (no edge is
    included twice, unlike a drawing library's inclusive polygon fill).  Every polygon edge toggles the parity of the samples to its
    right on the rows whose centre it crosses; the running parity along a row is the even-odd rule over all paths together."""
    height, width = shape
    fine = scale * supersample
    rows_total, cols_total = height * fine, width * fine
    toggles = np.zeros((rows_total, cols_total + 1), dtype=np.int32)
    for polygon in polygons:
        if len(polygon) < 3:
            continue
        start = np.asarray(polygon, dtype=np.float64) * fine
        end = np.roll(start, -1, axis=0)
        for (x0, y0), (x1, y1) in zip(start, end):
            if y0 == y1:
                continue
            low, high = (y0, y1) if y0 < y1 else (y1, y0)
            first = max(0, int(np.ceil(low - 0.5)))
            last = min(rows_total, int(np.ceil(high - 0.5)))  # exclusive: a sample row whose centre is at the top end is outside
            if first >= last:
                continue
            rows = np.arange(first, last)
            crossing = x0 + (rows + 0.5 - y0) * (x1 - x0) / (y1 - y0)
            columns = np.clip(np.ceil(crossing - 0.5).astype(np.int64), 0, cols_total)
            np.add.at(toggles, (rows, columns), 1)
    samples = (np.cumsum(toggles, axis=1) % 2)[:, :cols_total].astype(np.float64)
    return samples.reshape(height * scale, supersample, width * scale, supersample).mean(axis=(1, 3))


def trace_mask(mask: np.ndarray) -> GlyphTrace:
    """Trace one 1x coverage mask (float [0, 1], shape (h, w)) into a 4x coverage array of shape (4h, 4w).

    The outline may reach a little past the mask's rectangle; the part outside the rectangle is clipped."""
    height, width = mask.shape
    padded = np.pad(mask.astype(np.float64), PAD)
    mass = float(mask.sum())
    empty = GlyphTrace(np.zeros((height * OUTPUT_SCALE, width * OUTPUT_SCALE)), 0, 0.0, mass, 0.0, 0.0, False)
    if mass <= 0.0:
        return empty
    field = back_project(padded)
    level = mass_level(field, mass)
    polygons, nodes = outline_paths(field >= level)
    padded_height, padded_width = padded.shape
    big = rasterize(polygons, (padded_height, padded_width), OUTPUT_SCALE)
    crop = big[PAD * OUTPUT_SCALE:(PAD + height) * OUTPUT_SCALE, PAD * OUTPUT_SCALE:(PAD + width) * OUTPUT_SCALE]
    reduced = box_down(crop, OUTPUT_SCALE)
    touched = (mask > 0) | (reduced > 0.02)
    error = float(np.abs(reduced - mask)[touched].mean()) if touched.any() else 0.0
    traced_mass = float(reduced.sum())
    vanished = mass >= 0.1 and traced_mass == 0.0
    return GlyphTrace(crop, nodes, level, mass, traced_mass, error, vanished)


def frame_pixels(coverage: np.ndarray) -> np.ndarray:
    """(h, w, 4) uint8: red = green = blue = coverage x 255, alpha 255 on every ink pixel and 0 elsewhere (the HD glyph frame
    rule of the research findings in docs/workflow/hd-font.md)."""
    value = np.rint(np.clip(coverage, 0.0, 1.0) * 255.0).astype(np.uint8)
    alpha = np.where(value > 0, 255, 0).astype(np.uint8)
    return np.stack([value, value, value, alpha], axis=-1)


def png_bytes(pixels: np.ndarray) -> bytes:
    import io

    buffer = io.BytesIO()
    Image.fromarray(pixels, "RGBA").save(buffer, format="PNG", optimize=False, compress_level=9)
    return buffer.getvalue()
