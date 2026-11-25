"""ARC-DEV-2459: the fit tool of the Steamwoven kit (TRD SWI-GEN-4, SWI-KIT-2, SWI-KIT-4, SWI-VER-3): a generated image -> the kit master of a manifest part.

    python3 -B tools/steamwoven_ui/fit.py fit PART_ID (--batch B --prompt ID | --source FILE [--provenance FILE | --no-provenance]) [--size WxH] [--crop X,Y,W,H]
                                             [--key none|border|#RRGGBB] [--material wood,brass] [--lock-strength S] [--force-lock] [--min-speck N] [--soft-shadow] [--halo-limits MEAN,P95]
                                             [--date D] [--local-root DIR] [--masters DIR] [--replace] [--dry-run] [--json]
    python3 -B tools/steamwoven_ui/fit.py self-test

`--dry-run` does the whole fit and prints the report and the plan; it writes nothing (run it first). Standard library only (`pngio.py`); the same inputs give the same bytes.

The steps (each is a function below):
  1. size: the part's master size is the manifest's (`kit.json`; a part with an open size takes `--size`), sides multiples of 6 px (SWI-KIT-2). The source is cropped
     (`--crop`, source pixels) or, without it, must have the master's aspect within 1 % (SWI-GEN-8); a master size above the generator's budget is reached here.
  2. alpha: a source with real alpha keeps it (alpha at most 3 or at least 252 is first made 0 or 255: a nearly opaque pixel is art, not an edge). `--key border` keys a flat
     background (its colour is the median of the border, its spread must be small), `--key #RRGGBB` keys that colour (the border must be that colour: a part that touches the
     border cannot be keyed); the keyed edge zone (2 source px around the background, and what is still close to its colour) takes the colour of the nearest interior pixels
     and its alpha is the share of the way from the background to them.
  3. edge decontamination: a pixel with partial alpha whose colour is more than `KEEP_DE` (CIE76) away from the mean of the nearest opaque pixels takes that mean,
     so no colour of the generator's background stays in the edge; a fully transparent pixel is stored as 0 0 0 0 (as the bake does).
  4. resample: premultiplied alpha, linear light; area average down, Catmull-Rom up (per axis); the alpha is clamped (no ringing outside 0..1).
  5. alpha clean-up: alpha at most `ALPHA_LOW` becomes 0, at least `ALPHA_HIGH` 255; a connected group of non-transparent pixels smaller than `--min-speck` is removed.
  6. colour lock (`--material`): the low frequencies of the colour are moved to the approved material sheet (SWI-GEN-2, SWI-GEN-11): the pixels are assigned, in blocks,
     to the nearest named material (L*a*b* distance to the mean of its cut-outs in the sheet), the weights are smoothed over a few blocks, and each material's mean L*a*b* is
     moved to the sheet's by a smooth field of additive shifts (`--lock-strength` 0 to 1 scales it), so texture and shape (the high frequencies) stay. A shift larger than `LOCK_MAX_DE` means a wrong
     generation, not a colour error: the fit stops (`--force-lock` overrides). The sheet must be the approved one (`APPROVED_SHEET_SHA256`).
  7. checks (the gates; a master is written only when every one holds): the size; the halo metric (below), measured on what the steps above made, before the safety net of step 3
     is run again on the master (the final value after it is reported as `halo_final`, it cannot fail by construction); the orphans (at most `ORPHAN_MAX_SHARE` of the edge
     pixels without an opaque pixel in reach: art too faint or thin for a clean edge); a master with at least `MIN_OPAQUE_SHARE` opaque pixels; the high-frequency energy of the lock within `HF_TOLERANCE`.

The halo metric (`edge_halo`): in the semi-transparent edge band (alpha 1..254) the mean CIE76 colour difference to the mean of the nearest opaque pixels (the first
ring, up to `EDGE_RADIUS`, that holds any) must be at most `HALO_MEAN_DE` and its 95th percentile at most `HALO_P95_DE`; a band pixel with no opaque pixel in reach is an
orphan and is counted and reported, not measured. The high-frequency energy (`hf_energy`) is the standard deviation of a plane minus its 7 x 7 box blur over the interior opaque
pixels, for L*, a* and b* (an additive shift of the means, which is what the lock is, leaves it unchanged; the sRGB bytes would change its size with the brightness), on the whole image up to 1.5 MP and on four 512 x 512 windows beyond.

Outputs: the master `<masters>/<part id>.png` (default `local/steamwoven-ui/masters`, the one the bake reads) and the provenance record
`<local root>/provenance/fit/<part id>.json` (SWI-GEN-6: the generator record of the source, the source hash and crop, the keying and the lock with the sheet hash, the
metrics, the master hash); the record id `fit:<part id>` is what the manifest's `provenance` field of the part names when its batch sets the part's status to `master`.
Exit 0 = fitted (or dry-run fine), 1 = a gate failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import os
import re
import sys
from array import array
from itertools import repeat
from operator import add, itemgetter, mul
from pathlib import Path

try:
    from . import pngio, tone_check
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import pngio  # type: ignore
    import tone_check  # type: ignore

HERE = Path(__file__).resolve().parent
MANIFEST = HERE / "kit.json"
TOOL_VERSION = 1
GRID = 6
APPROVED_SHEET_SHA256 = "4d5fb9c01fcec386d13a55a7deb133d99c86c93dd693dc23e9a8140ea555ab06"  # TRD SWI-GEN-11
ASPECT_TOLERANCE = 0.01          # SWI-GEN-8
ALPHA_LOW, ALPHA_HIGH = 3, 252   # alpha at most / at least becomes 0 / 255
MIN_SPECK = 24                   # px: a group of non-transparent pixels smaller than this is removed
EDGE_RADIUS = 4                  # px: reach of the nearest-opaque search at the master
SOURCE_EDGE_RADIUS = 6           # px: the same at the source
KEEP_DE = 3.0                    # a band pixel closer than this to the interior colour keeps its colour
HALO_MEAN_DE = 3.0               # gate: mean CIE76 of the edge band
HALO_P95_DE = 6.0                # gate: 95th percentile
HALO_OVERRIDE_MAX = (20.0, 45.0)   # the largest halo override --halo-limits accepts (MEAN, P95)
HF_TOLERANCE = 0.10              # gate: high-frequency energy kept within 10 %
HF_FLOOR = 0.25                  # grey levels: a change below this is no change (a flat part has no energy to compare by ratio)
HF_RADIUS = 3                    # 7 x 7 box
LOCK_MAX_DE = 15.0               # the largest shift of a material mean the lock accepts
ORPHAN_MAX_SHARE = 0.10          # gate: the share of edge pixels with no opaque pixel in reach
MIN_OPAQUE_SHARE = 0.001         # gate: the master has at least this share of opaque pixels
LOCK_BLOCKS = 160                # blocks along the longer side of the lock's field
KEY_SPREAD_MAX = 40.0            # the 99th percentile of the border's distance from its median colour
KEY_LOW_MIN, KEY_SOFT = 6.0, 36.0
KEY_EDGE_ZONE = 2                # px at the source: the width of the mixed edge of a keyed part
WHOLE_IMAGE_PIXELS = 1_500_000   # up to this many pixels the hf check reads the whole image

LIMITS = {"halo_mean_de": HALO_MEAN_DE, "halo_p95_de": HALO_P95_DE, "hf_tolerance": HF_TOLERANCE, "keep_de": KEEP_DE, "lock_max_de": LOCK_MAX_DE,
          "edge_radius": EDGE_RADIUS, "orphan_max_share": ORPHAN_MAX_SHARE, "min_opaque_share": MIN_OPAQUE_SHARE, "alpha_low": ALPHA_LOW, "alpha_high": ALPHA_HIGH, "min_speck": MIN_SPECK, "aspect_tolerance": ASPECT_TOLERANCE}

ID_PATTERN = re.compile(r"^[A-Za-z0-9_-][A-Za-z0-9_.-]*(/[A-Za-z0-9_-][A-Za-z0-9_.-]*)*$")


class FitError(Exception):
    """An input cannot be used (exit 2)."""


class GateError(Exception):
    """A gate failed (exit 1); carries the report."""

    def __init__(self, failures: list[str], report: dict) -> None:
        super().__init__("; ".join(failures))
        self.failures, self.report = failures, report


# --- small helpers ---------------------------------------------------------------------------------------------------------------------

def sha256_bytes(blob: bytes) -> str:
    return hashlib.sha256(blob).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json(path: Path) -> object:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise FitError(f"cannot read {path}: {error}") from error


def parse_size(text: str) -> tuple[int, int]:
    match = re.fullmatch(r"(\d+)[x,](\d+)", text)
    if not match:
        raise FitError(f"size {text!r} must look like WxH")
    return int(match.group(1)), int(match.group(2))


def percentile(values: list[float], share: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(math.ceil(share * len(ordered))) - 1)]


def lab_of(rgb: tuple[float, float, float]) -> tuple[float, float, float]:
    return tone_check._pixel_lab(*(max(0, min(255, round(v))) for v in rgb))


def delta_e(a: tuple[float, float, float], b: tuple[float, float, float]) -> float:
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def default_local_root() -> Path:
    try:
        from . import prompt_library  # noqa: PLC0415
    except ImportError:
        import prompt_library  # type: ignore  # noqa: PLC0415
    return prompt_library.default_local_root()


# --- the manifest part and the master size -----------------------------------------------------------------------------------------------

def find_part(part_id: str, manifest_path: Path = MANIFEST) -> dict:
    if not ID_PATTERN.match(part_id) or ".." in part_id:
        raise FitError(f"part id {part_id!r} is not a valid id")
    manifest = read_json(manifest_path)
    for part in manifest["parts"]:  # type: ignore[index]
        if part["id"] == part_id:
            return part
    raise FitError(f"part {part_id!r} is not in the kit manifest")


def master_size(part: dict, override: tuple[int, int] | None) -> tuple[int, int]:
    size = part.get("size")
    if size is None:
        if override is None:
            raise FitError(f"part {part['id']} has an open size in the manifest; give --size WxH (its batch fixes it)")
        want = override
    else:
        want = (size[0], size[1])
        if override is not None and override != want:
            raise FitError(f"--size {override[0]}x{override[1]} differs from the manifest size {want[0]}x{want[1]} of {part['id']}")
    if want[0] % GRID or want[1] % GRID or want[0] < GRID or want[1] < GRID:
        raise FitError(f"master size {want[0]}x{want[1]} is not a multiple of {GRID} px (SWI-KIT-2)")
    if want[0] * want[1] > pngio.MAX_PIXELS:
        raise FitError(f"master size {want[0]}x{want[1]} is too large")
    return want


def source_rect(src_w: int, src_h: int, want: tuple[int, int], crop: tuple[int, int, int, int] | None) -> tuple[int, int, int, int]:
    if crop is not None:
        x, y, w, h = crop
        if w < 1 or h < 1 or x < 0 or y < 0 or x + w > src_w or y + h > src_h:
            raise FitError(f"crop {x},{y},{w},{h} is outside the {src_w}x{src_h} source")
        rect = crop
    else:
        rect = (0, 0, src_w, src_h)
    if abs((rect[2] / rect[3]) / (want[0] / want[1]) - 1) > ASPECT_TOLERANCE:
        what = f"crop {rect[2]}x{rect[3]}" if crop is not None else f"source {src_w}x{src_h}"
        raise FitError(f"{what} is not the aspect of {want[0]}x{want[1]} within {ASPECT_TOLERANCE:.0%} (SWI-GEN-8); "
                       + ("give another --crop X,Y,W,H" if crop is not None else "give --crop X,Y,W,H"))
    return rect


# --- alpha: keying ---------------------------------------------------------------------------------------------------------------------

def nearest_opaque(data: bytearray, w: int, h: int, x: int, y: int, opaque_min: int, rmax: int) -> tuple[float, float, float] | None:
    """The mean colour of the opaque pixels (alpha >= opaque_min) on the first ring around (x, y) that holds any, up to radius `rmax`."""
    for r in range(1, rmax + 1):
        sr = sg = sb = n = 0
        for yy in range(max(0, y - r), min(h - 1, y + r) + 1):
            xs = range(max(0, x - r), min(w - 1, x + r) + 1) if yy in (y - r, y + r) else (x - r, x + r)
            for xx in xs:
                if 0 <= xx < w:
                    o = (yy * w + xx) * 4
                    if data[o + 3] >= opaque_min:
                        sr += data[o]
                        sg += data[o + 1]
                        sb += data[o + 2]
                        n += 1
        if n:
            return sr / n, sg / n, sb / n
    return None


def border_ring(image: pngio.Image) -> list[tuple[int, int, int]]:
    width = max(1, min(image.w, image.h) // 200)
    out = []
    data, w, h = image.data, image.w, image.h
    for y in range(h):
        xs = range(w) if y < width or y >= h - width else list(range(width)) + list(range(w - width, w))
        for x in xs:
            o = (y * w + x) * 4
            out.append((data[o], data[o + 1], data[o + 2]))
    return out


def estimate_background(image: pngio.Image) -> tuple[tuple[int, int, int], float]:
    """The median colour of the border and the 99th percentile of the border's distance from it."""
    ring = border_ring(image)
    median = tuple(sorted(p[c] for p in ring)[len(ring) // 2] for c in range(3))
    spread = percentile([math.dist(p, median) for p in ring], 0.99)
    return median, spread  # type: ignore[return-value]


def parse_key(text: str, image: pngio.Image) -> tuple[tuple[int, int, int], float]:
    if text == "border":
        colour, spread = estimate_background(image)
        if spread > KEY_SPREAD_MAX:
            raise FitError(f"the border is not a flat background (99th percentile {spread:.0f} from its median colour, limit {KEY_SPREAD_MAX:.0f}); "
                           "give --key #RRGGBB or use a source with real alpha")
        return colour, spread
    match = re.fullmatch(r"#([0-9A-Fa-f]{6})", text)
    if not match:
        raise FitError(f"--key {text!r} must be none, border or #RRGGBB")
    colour = tuple(int(match.group(1)[i:i + 2], 16) for i in (0, 2, 4))
    ring = border_ring(image)
    spread = percentile([math.dist(p, colour) for p in ring], 0.99)
    if spread > KEY_SPREAD_MAX:
        raise FitError(f"the border is not the key colour {text} (99th percentile {spread:.0f} from it, limit {KEY_SPREAD_MAX:.0f}); a part that touches the border cannot be keyed")
    return colour, spread  # type: ignore[return-value]


def _edge_zone(w: int, h: int, background: bytearray, radius: int) -> set[int]:
    """The pixels that are not background but lie within `radius` (Chebyshev) of a background pixel that touches a non-background one (4-neighbours)."""
    edge: set[int] = set()
    rows = [bytes(background[y * w:(y + 1) * w]) for y in range(h)]
    flip = re.compile(b"\x01")
    for y in range(h):
        row = rows[y]
        sideways = (int.from_bytes(row[:-1], "big") ^ int.from_bytes(row[1:], "big")).to_bytes(w - 1, "big") if w > 1 else b""
        for m in flip.finditer(sideways):
            x = m.start()
            edge.add(y * w + (x if row[x] else x + 1))
        if y + 1 < h:
            nxt = rows[y + 1]
            down = (int.from_bytes(row, "big") ^ int.from_bytes(nxt, "big")).to_bytes(w, "big")
            for m in flip.finditer(down):
                x = m.start()
                edge.add((y if row[x] else y + 1) * w + x)
    zone: set[int] = set()
    for i in edge:
        y, x = divmod(i, w)
        for yy in range(max(0, y - radius), min(h - 1, y + radius) + 1):
            for xx in range(max(0, x - radius), min(w - 1, x + radius) + 1):
                if not background[yy * w + xx]:
                    zone.add(yy * w + xx)
    return zone


def key_background(image: pngio.Image, colour: tuple[int, int, int], spread: float) -> dict:
    """Make the alpha of a flat background (in place). Distance up to `low` from the key colour is background (alpha 0). The edge zone, the pixels within `KEY_EDGE_ZONE`
    of the background and those still close to it (distance below `high`), is a mix of the art and the background: its colour becomes the nearest interior colour (the
    mean of the nearest pixels outside the zone) and its alpha the share of the way from the background to that colour. The rest is opaque art."""
    low = max(KEY_LOW_MIN, spread + 2.0)
    high = low + KEY_SOFT
    data, w, h = image.data, image.w, image.h
    low2, high2 = low * low, high * high
    br, bg, bb = colour
    dist2 = [(r - br) ** 2 + (g - bg) ** 2 + (b - bb) ** 2 for r, g, b in zip(data[0::4], data[1::4], data[2::4])]
    background = bytearray(1 if d <= low2 else 0 for d in dist2)
    zone = _edge_zone(w, h, background, KEY_EDGE_ZONE)
    zone.update(i for i, d in enumerate(dist2) if low2 < d < high2)
    alpha = bytearray(0 if background[i] else 255 for i in range(w * h))
    for i in zone:
        alpha[i] = 1
    data[3::4] = alpha
    refined = 0
    thin: dict[int, int] = {}  # decided after the loop, so a thin pixel is never taken as interior by the pixels after it
    for i in sorted(zone):
        y, x = divmod(i, w)
        o = i * 4
        d = math.sqrt(dist2[i])
        inner = nearest_opaque(data, w, h, x, y, 255, SOURCE_EDGE_RADIUS)
        if inner is None:  # a feature thinner than the zone: opaque unless it is still close to the background colour
            thin[i] = 255 if d >= high else max(1, min(254, round(255 * (d - low) / (high - low))))
            continue
        axis = (inner[0] - br, inner[1] - bg, inner[2] - bb)
        norm2 = sum(v * v for v in axis)
        share = sum((data[o + c] - col) * a for c, (col, a) in enumerate(zip(colour, axis))) / norm2 if norm2 > 1e-6 else 1.0
        data[o + 3] = max(0, min(254, round(255 * share)))
        data[o], data[o + 1], data[o + 2] = (round(v) for v in inner)
        refined += 1
    for i, a in thin.items():
        data[i * 4 + 3] = a
    return {"colour": list(colour), "low": round(low, 2), "high": round(high, 2), "zone_pixels": len(zone), "refined_pixels": refined}


def has_alpha(image: pngio.Image) -> bool:
    return image.source_alpha and min(image.data[3::4]) < 255


# --- edge decontamination and the halo metric ------------------------------------------------------------------------------------------

def decontaminate(image: pngio.Image, opaque_min: int, radius: int, keep_de: float = KEEP_DE) -> int:
    """Edge pixels (alpha 1..opaque_min-1) farther than `keep_de` from the nearest opaque colour take it; alpha 0 becomes 0 0 0 0. Returns the band pixels changed."""
    data, w = image.data, image.w
    changed = 0
    for i, a in enumerate(data[3::4]):
        if a >= opaque_min:
            continue
        o = i * 4
        if a == 0:
            data[o] = data[o + 1] = data[o + 2] = 0
            continue
        y, x = divmod(i, w)
        inner = nearest_opaque(data, w, image.h, x, y, opaque_min, radius)
        if inner is None:
            continue
        if delta_e(lab_of((data[o], data[o + 1], data[o + 2])), lab_of(inner)) > keep_de:
            data[o], data[o + 1], data[o + 2] = (max(0, min(255, round(v))) for v in inner)
            changed += 1
    return changed


def edge_halo(image: pngio.Image, radius: int = EDGE_RADIUS, soft_shadow: bool = False) -> dict:
    """SWI-VER-3: the colour difference of the semi-transparent edge band to the nearest opaque interior (see the module text).

    `soft_shadow` (ARC-UI-2927, `--soft-shadow`): the part carries a designed soft shadow (an end cap, whose art is an opaque bar): every semi-transparent pixel is the
    shadow, counted as `shadow_pixels`, neither measured nor an orphan (its colour is the shadow's by design, not a contamination of the art's edge). The edge
    decontamination is skipped for such a part too (it would recolour the shadow to the art's colour)."""
    data, w, h = image.data, image.w, image.h
    diffs: list[float] = []
    orphans = 0
    shadow = 0
    for i, a in enumerate(data[3::4]):
        if a == 0 or a == 255:
            continue
        if soft_shadow:
            shadow += 1
            continue
        y, x = divmod(i, w)
        inner = nearest_opaque(data, w, h, x, y, 255, radius)
        if inner is None:
            orphans += 1
            continue
        o = i * 4
        diffs.append(delta_e(lab_of((data[o], data[o + 1], data[o + 2])), lab_of(inner)))
    return {"band_pixels": len(diffs) + orphans, "measured": len(diffs), "orphans": orphans, "shadow_pixels": shadow,
            "mean_de": round(sum(diffs) / len(diffs), 3) if diffs else 0.0, "p95_de": round(percentile(diffs, 0.95), 3),
            "max_de": round(max(diffs), 3) if diffs else 0.0}


# --- resample (premultiplied, linear light) ----------------------------------------------------------------------------------------------

_LIN = [tone_check._lin(v) for v in range(256)]
_UNIT = [v / 255 for v in range(256)]
_ENC_STEPS = 16384
_ENC = bytes(max(0, min(255, round(255 * (12.92 * (i / _ENC_STEPS) if i / _ENC_STEPS <= 0.0031308 else 1.055 * (i / _ENC_STEPS) ** (1 / 2.4) - 0.055))))
             for i in range(_ENC_STEPS + 1))


def axis_taps(src_len: int, dst_len: int, offset: int = 0, kind: str = "auto") -> tuple[list[list[int]], list[list[float]]]:
    """Taps for one axis: K index lists and K weight lists (one entry per output index); indices are absolute (`offset` is the crop's start).
    kind auto: area average when shrinking, Catmull-Rom when growing; `linear` for smooth fields."""
    scale = src_len / dst_len
    rows: list[list[tuple[int, float]]] = []
    for o in range(dst_len):
        row: list[tuple[int, float]] = []
        if kind == "auto" and scale >= 1:
            lo, hi = o * scale, (o + 1) * scale
            i = int(math.floor(lo))
            while i < hi and i < src_len:
                weight = min(hi, i + 1) - max(lo, i)
                if weight > 1e-9:
                    row.append((i, weight / scale))
                i += 1
        else:
            centre = (o + 0.5) * scale - 0.5
            base = int(math.floor(centre))
            t = centre - base
            if kind == "linear":
                spots = ((base, 1 - t), (base + 1, t))
            else:
                spots = ((base - 1, -0.5 * t ** 3 + t ** 2 - 0.5 * t), (base, 1.5 * t ** 3 - 2.5 * t ** 2 + 1),
                         (base + 1, -1.5 * t ** 3 + 2 * t ** 2 + 0.5 * t), (base + 2, 0.5 * t ** 3 - 0.5 * t ** 2))
            for i, weight in spots:
                row.append((max(0, min(src_len - 1, i)), weight))
        rows.append(row)
    taps = max(len(r) for r in rows)
    idx = [[(r[k][0] if k < len(r) else r[0][0]) + offset for r in rows] for k in range(taps)]
    wts = [[(r[k][1] if k < len(r) else 0.0) for r in rows] for k in range(taps)]
    return idx, wts


def _gather(row, idx: list[int]):
    if len(idx) == 1:
        return [row[idx[0]]]
    return itemgetter(*idx)(row)


def resample_planes(planes: list, src_w: int, rect: tuple[int, int, int, int], dst_w: int, dst_h: int, kind: str = "auto") -> list[array]:
    """Separable resample of float planes (row-major, `src_w` wide) of the crop `rect` = (x, y, w, h) to dst_w x dst_h."""
    x0, y0, cw, ch = rect
    hx_idx, hx_w = axis_taps(cw, dst_w, x0, kind)
    vy_idx, vy_w = axis_taps(ch, dst_h, y0, kind)
    needed = sorted({i for k in vy_idx for i in k})
    out: list[array] = []
    for plane in planes:
        rows: dict[int, array] = {}
        for y in needed:
            row = plane[y * src_w:(y + 1) * src_w]
            acc: list[float] | None = None
            for k_idx, k_w in zip(hx_idx, hx_w):
                term = list(map(mul, _gather(row, k_idx), k_w))
                acc = term if acc is None else list(map(add, acc, term))
            rows[y] = array("f", acc)  # type: ignore[arg-type]
        result = array("f")
        for o in range(dst_h):
            acc2: list[float] | None = None
            for k_idx, k_w in zip(vy_idx, vy_w):
                weight = k_w[o]
                if weight == 0.0:
                    continue
                term2 = list(map(mul, rows[k_idx[o]], repeat(weight)))
                acc2 = term2 if acc2 is None else list(map(add, acc2, term2))
            result.extend(acc2 if acc2 is not None else [0.0] * dst_w)
        out.append(result)
    return out


def resample_image(image: pngio.Image, rect: tuple[int, int, int, int], dst_w: int, dst_h: int) -> pngio.Image:
    """Premultiplied linear-light resample of an RGBA image; the result is straight-alpha RGBA with the alpha clamped to 0..1."""
    data = image.data
    alpha = [_UNIT[v] for v in data[3::4]]
    planes = [list(map(mul, [_LIN[v] for v in data[c::4]], alpha)) for c in range(3)]
    planes.append(alpha)
    r_, g_, b_, a_ = resample_planes(planes, image.w, rect, dst_w, dst_h)
    out = bytearray(dst_w * dst_h * 4)
    a_bytes = bytearray(max(0, min(255, round(255 * v))) for v in a_)
    out[3::4] = a_bytes
    for c, plane in enumerate((r_, g_, b_)):
        values = bytearray(dst_w * dst_h)
        for i, (v, av) in enumerate(zip(plane, a_)):
            if a_bytes[i]:
                values[i] = _ENC[int(max(0.0, min(1.0, v / av if av > 1e-6 else 0.0)) * _ENC_STEPS + 0.5)]
        out[c::4] = values
    return pngio.Image(dst_w, dst_h, out)


# --- alpha clean-up ----------------------------------------------------------------------------------------------------------------------

def crush_alpha(image: pngio.Image) -> int:
    alpha = image.data[3::4]
    changed = 0
    for i, a in enumerate(alpha):
        if 0 < a <= ALPHA_LOW:
            alpha[i] = 0
            changed += 1
        elif ALPHA_HIGH <= a < 255:
            alpha[i] = 255
            changed += 1
    image.data[3::4] = alpha
    return changed


def remove_specks(image: pngio.Image, min_area: int) -> int:
    """Remove every 4-connected group of non-transparent pixels smaller than `min_area` (run-length labelling, union-find). Returns the pixels removed."""
    w, h, data = image.w, image.h, image.data
    alpha = bytes(data[3::4])
    if min_area <= 1 or min(alpha) != 0:
        return 0
    runs: list[tuple[int, int, int]] = []  # (row, start, end exclusive)
    by_row: list[list[int]] = []
    pattern = re.compile(rb"[\x01-\xff]+")
    for y in range(h):
        ids = []
        for m in pattern.finditer(alpha, y * w, (y + 1) * w):
            ids.append(len(runs))
            runs.append((y, m.start() - y * w, m.end() - y * w))
        by_row.append(ids)
    parent = list(range(len(runs)))

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for y in range(1, h):
        above = by_row[y - 1]
        j = 0
        for i in by_row[y]:
            _, s, e = runs[i]
            while j < len(above) and runs[above[j]][2] <= s:
                j += 1
            k = j
            while k < len(above) and runs[above[k]][1] < e:
                ra, rb = find(i), find(above[k])
                if ra != rb:
                    parent[ra] = rb
                k += 1
    area: dict[int, int] = {}
    for i, (_, s, e) in enumerate(runs):
        root = find(i)
        area[root] = area.get(root, 0) + e - s
    removed = 0
    for i, (y, s, e) in enumerate(runs):
        if area[find(i)] < min_area:
            for x in range(s, e):
                o = (y * w + x) * 4
                data[o] = data[o + 1] = data[o + 2] = data[o + 3] = 0
            removed += e - s
    return removed


# --- the colour lock ---------------------------------------------------------------------------------------------------------------------

def sheet_reference(local_root: Path, materials: list[str]) -> dict:
    """The mean L*a*b* of each named material over the opaque pixels of its cut-outs in the approved sheet (hashes checked)."""
    sheet = local_root / "material-sheet" / "sheet.png"
    if not sheet.is_file():
        raise FitError(f"the approved material sheet {sheet} is missing")
    digest = sha256_file(sheet)
    if digest != APPROVED_SHEET_SHA256:
        raise FitError(f"the material sheet's sha256 {digest} is not the approved one ({APPROVED_SHEET_SHA256}, TRD SWI-GEN-11); "
                       "a changed sheet needs the owner's new approval")
    record_path = local_root / "material-sheet" / "cutouts" / "cutouts.provenance.json"
    record = read_json(record_path)
    refs: dict[str, dict] = {}
    for material in materials:
        ids = sorted(k for k, v in record.items() if v.get("material") == material)  # type: ignore[union-attr]
        if not ids:
            known = sorted({str(v.get("material")) for v in record.values()})  # type: ignore[union-attr]
            raise FitError(f"the material sheet has no cut-out of material {material!r} (known: {', '.join(known)})")
        sums = [0.0, 0.0, 0.0]
        count = 0
        for cut_id in ids:
            path = record_path.parent / f"{cut_id}.png"
            if not path.is_file() or sha256_file(path) != record[cut_id]["sha256"]:  # type: ignore[index]
                raise FitError(f"cut-out {cut_id} is missing or differs from its provenance record")
            image = pngio.read(path)
            step = max(1, int(math.sqrt(image.w * image.h / 40000)))
            for y in range(0, image.h, step):
                for x in range(0, image.w, step):
                    o = (y * image.w + x) * 4
                    if image.data[o + 3] == 255:
                        lab = tone_check._pixel_lab(image.data[o], image.data[o + 1], image.data[o + 2])
                        for c in range(3):
                            sums[c] += lab[c]
                        count += 1
        if not count:
            raise FitError(f"the cut-outs of material {material!r} have no opaque pixel")
        refs[material] = {"lab": tuple(s / count for s in sums), "cutouts": ids}
    return {"sheet_sha256": digest, "materials": refs}


def colour_lock(image: pngio.Image, reference: dict, strength: float = 1.0, force: bool = False) -> dict:
    """Move the low frequencies of the colour of `image` (in place) to the sheet's material means (module text, step 6); returns the report."""
    if not 0.0 <= strength <= 1.0:
        raise FitError(f"--lock-strength {strength} must be between 0 and 1")
    w, h, data = image.w, image.h, image.data
    names = list(reference["materials"])
    refs = [reference["materials"][n]["lab"] for n in names]
    pixels = [i for i, a in enumerate(data[3::4]) if a == 255]
    if not pixels:
        raise FitError("the colour lock needs opaque pixels, the part has none")
    zeros = bytes(4 * w * h)
    labs = [array("f", zeros), array("f", zeros), array("f", zeros)]
    pixel_lab = tone_check._pixel_lab
    for i in pixels:
        o = i * 4
        labs[0][i], labs[1][i], labs[2][i] = pixel_lab(data[o], data[o + 1], data[o + 2])
    block = max(1, round(max(w, h) / LOCK_BLOCKS))
    bw, bh = -(-w // block), -(-h // block)
    sums = [[0.0] * (bw * bh) for _ in range(3)]
    count = [0] * (bw * bh)
    for i in pixels:
        y, x = divmod(i, w)
        b = (y // block) * bw + x // block
        for c in range(3):
            sums[c][b] += labs[c][i]
        count[b] += 1
    assigned = [[0.0] * (bw * bh) for _ in names]
    totals = [[0.0, 0.0, 0.0, 0] for _ in names]  # the sums of the L*a*b* of each material's pixels and their number
    for b in range(bw * bh):
        if not count[b]:
            continue
        mean = (sums[0][b] / count[b], sums[1][b] / count[b], sums[2][b] / count[b])
        m = min(range(len(names)), key=lambda k, mean=mean: delta_e(mean, refs[k])) if len(names) > 1 else 0
        assigned[m][b] = 1.0
        for c in range(3):
            totals[m][c] += sums[c][b]
        totals[m][3] += count[b]
    deltas = []
    for k, name in enumerate(names):
        if not totals[k][3]:
            raise FitError(f"no part of the image is nearest to material {name!r}; check --material")
        mean = tuple(totals[k][c] / totals[k][3] for c in range(3))
        full_shift = tuple(refs[k][c] - mean[c] for c in range(3))
        shift_de = math.sqrt(sum(v * v for v in full_shift))
        if shift_de > LOCK_MAX_DE and not force:
            raise FitError(f"the colour lock would move material {name!r} by dE {shift_de:.1f} (limit {LOCK_MAX_DE:g}): the generation is off-tone, "
                           "regenerate it (or --force-lock)")
        deltas.append({"material": name, "mean_lab": [round(v, 2) for v in mean], "sheet_lab": [round(v, 2) for v in refs[k]],
                       "shift": [round(strength * v, 2) for v in full_shift], "shift_de": round(strength * shift_de, 2), "blocks": int(sum(assigned[k]))})
    # one smooth field of shifts: the material weights smoothed over a few blocks, normalised
    weights = [tone_check.box_blur(plane, 2, bw, bh) for plane in assigned]
    total_pixels = sum(t[3] for t in totals)
    overall = [sum(deltas[k]["shift"][c] * totals[k][3] for k in range(len(names))) / total_pixels for c in range(3)]
    field = [[0.0] * (bw * bh) for _ in range(3)]
    for b in range(bw * bh):
        norm = sum(wm[b] for wm in weights)
        for c in range(3):
            field[c][b] = sum(weights[k][b] * deltas[k]["shift"][c] for k in range(len(names))) / norm if norm > 1e-6 else overall[c]
    if block == 1:
        full = field
    else:
        up = resample_planes(field, bw, (0, 0, bw, bh), bw * block, bh * block, "linear")
        full = [[plane[y * bw * block + x] for y in range(h) for x in range(w)] for plane in up]
    from_lab = tone_check._from_lab
    for i, a in enumerate(data[3::4]):
        if a:
            o = i * 4
            lab = pixel_lab(data[o], data[o + 1], data[o + 2])
            data[o], data[o + 1], data[o + 2] = from_lab(lab[0] + full[0][i], lab[1] + full[1][i], lab[2] + full[2][i])
    return {"sheet_sha256": reference["sheet_sha256"], "strength": strength, "block": block, "materials": deltas,
            "cutouts": {n: reference["materials"][n]["cutouts"] for n in names}}


def hf_windows(w: int, h: int) -> list[tuple[int, int, int, int]]:
    if w * h <= WHOLE_IMAGE_PIXELS:
        return [(0, 0, w, h)]
    size = 512
    xs = [max(0, min(w - size, round(w * f) - size // 2)) for f in (0.25, 0.75)]
    ys = [max(0, min(h - size, round(h * f) - size // 2)) for f in (0.25, 0.75)]
    return [(x, y, min(size, w), min(size, h)) for y in ys for x in xs]


def hf_energy(image: pngio.Image) -> dict:
    """The high-frequency energy of the opaque interior: the std of a plane minus its box blur, for L*, a* and b* (module text)."""
    sums = [0.0, 0.0, 0.0]
    count = 0
    for x0, y0, ww, hh in hf_windows(image.w, image.h):
        piece = image.crop(x0, y0, ww, hh).data
        alpha = [1.0 if a == 255 else 0.0 for a in piece[3::4]]
        inside = [v >= 0.9999 for v in tone_check.box_blur(alpha, HF_RADIUS, ww, hh)]
        labs = [tone_check._pixel_lab(*piece[o:o + 3]) if ok else (0.0, 0.0, 0.0) for o, ok in zip(range(0, len(piece), 4), inside)]
        planes = [[lab[k] for lab in labs] for k in range(3)]
        for k, plane in enumerate(planes):
            blurred = tone_check.box_blur(plane, HF_RADIUS, ww, hh)
            sums[k] += sum((p - q) ** 2 for p, q, ok in zip(plane, blurred, inside) if ok)
        count += sum(inside)
    if not count:
        return {"pixels": 0, "l": 0.0, "a": 0.0, "b": 0.0, "total": 0.0}
    stds = [math.sqrt(s / count) for s in sums]
    return {"pixels": count, "l": stds[0], "a": stds[1], "b": stds[2], "total": math.sqrt(sum(v * v for v in stds))}


def hf_check(before: dict, after: dict) -> dict:
    ok = True
    ratios = {}
    for key in ("l", "a", "b", "total"):
        b, a = before[key], after[key]
        ratios[key] = round(a / b, 4) if b > 1e-9 else 1.0
        if abs(a - b) > max(HF_TOLERANCE * b, HF_FLOOR):
            ok = False
    return {"before": {k: round(v, 4) for k, v in before.items() if k != "pixels"}, "after": {k: round(v, 4) for k, v in after.items() if k != "pixels"},
            "ratio": ratios, "within_tolerance": ok, "pixels": before["pixels"]}


# --- the fit -----------------------------------------------------------------------------------------------------------------------------

def fit_image(source: pngio.Image, want: tuple[int, int], crop: tuple[int, int, int, int] | None = None, key: str = "none",
              reference: dict | None = None, strength: float = 1.0, force_lock: bool = False, min_speck: int = MIN_SPECK,
              soft_shadow: bool = False) -> tuple[pngio.Image, dict]:
    """The whole pipeline on pixels; returns the master image and the report (no file is touched). Raises FitError; the gates are checked by `gates`."""
    report: dict = {}
    rect = source_rect(source.w, source.h, want, crop)
    report["crop"] = list(rect)
    work = pngio.Image(source.w, source.h, bytearray(source.data), source.source_alpha)
    if has_alpha(work):
        report["alpha"] = "source"
    elif key != "none":
        colour, spread = parse_key(key, work)
        report["alpha"] = "key"
        report["key"] = key_background(work, colour, spread)
    else:
        report["alpha"] = "opaque"
    if report["alpha"] != "opaque":
        crush_alpha(work)  # alpha 252..254 is opaque to every later step, so it is cleaned as opaque, not left as a contaminated "interior"
        report["source_decontaminated"] = 0 if soft_shadow else decontaminate(work, 255, SOURCE_EDGE_RADIUS)  # a designed shadow is semi-transparent by intent: its colour is not contamination
    master = resample_image(work, rect, want[0], want[1])
    report["alpha_crushed"] = crush_alpha(master)
    report["specks_removed_px"] = remove_specks(master, min_speck)
    if reference is not None:
        before = hf_energy(master)
        report["lock"] = colour_lock(master, reference, strength, force_lock)
        report["hf"] = hf_check(before, hf_energy(master))
    report["halo"] = edge_halo(master, soft_shadow=soft_shadow)  # what the pipeline made: the gate (the safety net below can only make the final edge better)
    report["master_decontaminated"] = 0 if soft_shadow else decontaminate(master, 255, EDGE_RADIUS)
    report["halo_final"] = edge_halo(master, soft_shadow=soft_shadow)
    report["opaque_share"] = round(sum(1 for a in master.data[3::4] if a == 255) / (master.w * master.h), 5)
    report["size"] = [master.w, master.h]
    return master, report


def gates(report: dict, want: tuple[int, int], halo_limits: tuple[float, float] | None = None) -> list[str]:
    """`halo_limits` (ARC-UI-2483, `--halo-limits MEAN,P95`): a recorded override of the halo gate for a textured or hair-thin cut-out, whose edge band is measured before the safety net and is far from
    flat at the master's scale (a bumpy brass rim, a needle a few pixels wide). It is capped (`HALO_OVERRIDE_MAX`) and must be finite. The safety net still recolours the band afterwards, so the final
    edge (`halo_final`) is within the standard limits by construction; the override only admits a rougher pipeline edge, it does not switch the gate off."""
    failures = []
    if tuple(report["size"]) != want:
        failures.append(f"size {report['size']} is not the master size {list(want)}")
    halo = report["halo"]
    limit_mean, limit_p95 = halo_limits or (HALO_MEAN_DE, HALO_P95_DE)
    if halo["mean_de"] > limit_mean or halo["p95_de"] > limit_p95:
        failures.append(f"edge halo: mean dE {halo['mean_de']} (limit {limit_mean}), 95th percentile {halo['p95_de']} (limit {limit_p95})")
    if halo["band_pixels"] and halo["orphans"] / halo["band_pixels"] > ORPHAN_MAX_SHARE:
        failures.append(f"{halo['orphans']} of {halo['band_pixels']} edge pixels have no opaque pixel within {EDGE_RADIUS} px (limit {ORPHAN_MAX_SHARE:.0%}): "
                        "the art is too faint or too thin for a clean edge")
    if report["opaque_share"] < MIN_OPAQUE_SHARE:
        failures.append(f"the master is empty (opaque share {report['opaque_share']}, at least {MIN_OPAQUE_SHARE} needed): check the alpha or the key")
    if "hf" in report and not report["hf"]["within_tolerance"]:
        failures.append(f"the colour lock changed the high-frequency energy by more than {HF_TOLERANCE:.0%}: {report['hf']['ratio']}")
    return failures


def load_source(args: argparse.Namespace, local_root: Path) -> tuple[Path, pngio.Image, dict | None]:
    if args.source:
        path = Path(args.source)
        provenance_path = Path(args.provenance) if args.provenance else None
    else:
        if not args.batch or not args.prompt:
            raise FitError("give --source FILE, or --batch B --prompt ID for an outbox image")
        path = local_root / "outbox" / args.batch / f"{args.prompt}.png"
        provenance_path = Path(args.provenance) if args.provenance else local_root / "provenance" / args.batch / f"{args.prompt}.json"
    if not path.is_file():
        raise FitError(f"source image {path} not found")
    try:
        image = pngio.read(path)
    except pngio.PngError as error:
        raise FitError(f"{path}: {error}") from error
    record = None
    if provenance_path is not None and not args.no_provenance:
        if not provenance_path.is_file():
            raise FitError(f"provenance record {provenance_path} not found (SWI-GEN-6: run prompt_library.py provenance, or pass --no-provenance for a hand-made source)")
        record = read_json(provenance_path)
        if record.get("image_sha256") != sha256_file(path):  # type: ignore[union-attr]
            raise FitError(f"{path.name} differs from the image its provenance record {provenance_path.name} describes (hash mismatch)")
    elif not args.no_provenance:
        raise FitError("a source needs its generator provenance record (--provenance FILE), or --no-provenance for a hand-made source")
    return path, image, record  # type: ignore[return-value]


def provenance_record(part: dict, source_path: Path, source: pngio.Image, generator: dict | None, report: dict, master_bytes: bytes,
                      master: pngio.Image, date: str, rel_master: str, local_root: Path, key: str) -> dict:
    try:
        shown = str(source_path.resolve().relative_to(local_root.resolve()))
    except ValueError:
        shown = source_path.name
    record: dict = {"record": f"fit:{part['id']}", "tool": "tools/steamwoven_ui/fit.py", "tool_version": TOOL_VERSION, "date": date,
                    "part": part["id"], "kind": part.get("kind"),
                    "master": {"file": rel_master, "size": [master.w, master.h], "sha256": sha256_bytes(master_bytes), "pixel_sha256": sha256_bytes(bytes(master.data))},
                    "source": {"file": shown, "sha256": sha256_bytes(source_path.read_bytes()), "size": [source.w, source.h], "crop": report["crop"],
                              "alpha": report["alpha"], "key": key if report["alpha"] == "key" else "none"},
                    "generator": generator, "limits": LIMITS}
    for k in ("key", "lock", "hf", "halo", "halo_final", "opaque_share", "alpha_crushed", "specks_removed_px", "source_decontaminated", "master_decontaminated"):
        if k in report:
            record["keying" if k == "key" else k] = report[k]
    return record


def run_fit(args: argparse.Namespace) -> dict:
    local_root = Path(args.local_root) if args.local_root else default_local_root()
    part = find_part(args.part, Path(args.manifest) if args.manifest else MANIFEST)
    want = master_size(part, parse_size(args.size) if args.size else None)
    source_path, source, generator = load_source(args, local_root)
    try:
        crop = tuple(int(v) for v in args.crop.split(",")) if args.crop else None
    except ValueError:
        raise FitError(f"--crop {args.crop!r} must be four integers X,Y,W,H") from None
    if crop is not None and len(crop) != 4:
        raise FitError("--crop needs X,Y,W,H")
    if not 0.0 <= args.lock_strength <= 1.0:
        raise FitError(f"--lock-strength {args.lock_strength} must be between 0 and 1")
    materials = [m for m in args.material.split(",") if m] if args.material else []
    reference = sheet_reference(local_root, materials) if materials else None
    master, report = fit_image(source, want, crop, args.key, reference, args.lock_strength, args.force_lock, args.min_speck, args.soft_shadow)  # type: ignore[arg-type]
    halo_limits = tuple(float(v) for v in args.halo_limits.split(",")) if getattr(args, "halo_limits", None) else None
    if halo_limits is not None:
        if len(halo_limits) != 2 or not all(math.isfinite(v) and 0 < v for v in halo_limits):
            raise FitError("--halo-limits needs MEAN,P95 as two finite positive numbers")
        if halo_limits[0] > HALO_OVERRIDE_MAX[0] or halo_limits[1] > HALO_OVERRIDE_MAX[1]:
            raise FitError(f"--halo-limits {halo_limits[0]},{halo_limits[1]} is above the cap {HALO_OVERRIDE_MAX[0]},{HALO_OVERRIDE_MAX[1]}: the art is not a cut-out this gate can describe")
    failures = gates(report, want, halo_limits)  # type: ignore[arg-type]
    masters = Path(args.masters) if args.masters else local_root / "masters"
    target = masters / f"{part['id']}.png"
    blob = pngio.encode(master)
    report.update({"part": part["id"], "master_file": str(target), "master_sha256": sha256_bytes(blob), "gates": "FAIL" if failures else "PASS",
                   "dry_run": bool(args.dry_run)})
    if failures:
        raise GateError(failures, report)
    date = args.date or datetime.date.today().isoformat()
    record = provenance_record(part, source_path, source, generator, report, blob, master, date, str(target), local_root, args.key)
    if halo_limits is not None:
        record["limits"] = {**record["limits"], "halo_override": {"mean_de": halo_limits[0], "p95_de": halo_limits[1], "cap": list(HALO_OVERRIDE_MAX)}}
    report["record"] = record
    if target.exists() and not args.replace:
        raise FitError(f"master {target} exists; pass --replace to overwrite it")
    if args.dry_run:
        return report
    target.parent.mkdir(parents=True, exist_ok=True)
    record_path = local_root / "provenance" / "fit" / f"{part['id']}.json"
    record_path.parent.mkdir(parents=True, exist_ok=True)
    for path, data in ((record_path, (json.dumps(record, indent=2) + "\n").encode("utf-8")), (target, blob)):  # the record first: a master never lacks its record
        temp = path.with_name(path.name + ".tmp")
        try:
            temp.write_bytes(data)
            os.replace(temp, path)
        finally:
            temp.unlink(missing_ok=True)
    report["record_file"] = str(record_path)
    return report


def describe(report: dict) -> str:
    halo = report["halo"]
    lines = [f"{'DRY-RUN ' if report.get('dry_run') else ''}fit {report['part']} -> {report['size'][0]}x{report['size'][1]}  {report['gates']}",
             f"  alpha: {report['alpha']}  crop {report['crop']}  specks removed {report['specks_removed_px']} px  alpha crushed {report['alpha_crushed']}",
             f"  halo: {halo['measured']} edge pixels, mean dE {halo['mean_de']} (limit {HALO_MEAN_DE}), p95 {halo['p95_de']} (limit {HALO_P95_DE}), orphans {halo['orphans']}; after the safety net p95 {report['halo_final']['p95_de']}"]
    if "lock" in report:
        for m in report["lock"]["materials"]:
            lines.append(f"  lock {m['material']}: shift L*a*b* {m['shift']} (dE {m['shift_de']}), {m['blocks']} blocks")
        lines.append(f"  high-frequency energy ratio {report['hf']['ratio']}  (tolerance {HF_TOLERANCE:.0%})")
    lines.append(f"  master {report['master_file']}  sha256 {report['master_sha256']}")
    return "\n".join(lines)


# --- self test and CLI -------------------------------------------------------------------------------------------------------------------

def synthetic_disc(size: int, background: tuple[int, int, int] | None) -> pngio.Image:
    """A brass-coloured disc with a darker outline, anti-aliased, on a flat background or (background None) on transparent. No art."""
    image = pngio.Image(size, size, bytearray(size * size * 4), source_alpha=background is None)
    c, radius = (size - 1) / 2, size * 0.36
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - c, y - c)
            cover = max(0.0, min(1.0, radius + 0.5 - d))
            ring = 1.0 if d > radius - size * 0.04 else 0.0
            fg = (160 - 40 * ring + (x % 5) * 3, 128 - 30 * ring + (y % 4) * 3, 72 - 12 * ring)
            o = (y * size + x) * 4
            if background is None:
                image.data[o:o + 4] = bytes((*(round(v) for v in fg), round(255 * cover)))
            else:
                image.data[o:o + 4] = bytes((*(round(cover * f + (1 - cover) * b) for f, b in zip(fg, background)), 255))
    return image


def self_test() -> list[str]:
    """Synthetic disc only (no game file): keyed on magenta and with real alpha, both fitted to a 6 px grid size and checked by the gates."""
    failures = []
    for label, source, key in (("keyed", synthetic_disc(96, (255, 0, 255)), "border"), ("alpha", synthetic_disc(96, None), "none")):
        master, report = fit_image(source, (180, 180), key=key)
        problems = gates(report, (180, 180))
        if (master.w, master.h) != (180, 180) or problems:
            failures.append(f"{label} disc: {problems}")
        if pngio.encode(fit_image(source, (180, 180), key=key)[0]) != pngio.encode(master):
            failures.append(f"{label} disc: a second run gave other bytes")
    return failures


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=(__doc__ or "").split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("self-test")
    f = sub.add_parser("fit")
    f.add_argument("part")
    f.add_argument("--batch")
    f.add_argument("--prompt")
    f.add_argument("--source")
    f.add_argument("--provenance")
    f.add_argument("--no-provenance", action="store_true")
    f.add_argument("--size")
    f.add_argument("--crop")
    f.add_argument("--key", default="none")
    f.add_argument("--material")
    f.add_argument("--lock-strength", type=float, default=1.0)
    f.add_argument("--force-lock", action="store_true")
    f.add_argument("--halo-limits", help="MEAN,P95: a recorded override of the halo gate for a textured or hair-thin cut-out (capped; the safety net still recolours the band)")
    f.add_argument("--soft-shadow", action="store_true", help="the part carries a designed soft shadow (an end cap): shadow pixels away from the art are not orphans")
    f.add_argument("--min-speck", type=int, default=MIN_SPECK)
    f.add_argument("--date")
    f.add_argument("--local-root")
    f.add_argument("--masters")
    f.add_argument("--manifest")
    f.add_argument("--replace", action="store_true")
    f.add_argument("--dry-run", action="store_true")
    f.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    if args.command == "self-test":
        failures = self_test()
        print("FIT self-test " + ("PASS" if not failures else "FAIL: " + "; ".join(failures)))
        return 1 if failures else 0
    try:
        report = run_fit(args)
    except FitError as error:
        print(f"FIT error: {error}", file=sys.stderr)
        return 2
    except GateError as error:
        print(describe(error.report))
        for failure in error.failures:
            print(f"  GATE FAILED: {failure}", file=sys.stderr)
        return 1
    print(json.dumps(report, indent=2) if args.json else describe(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
