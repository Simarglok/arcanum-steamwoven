#!/usr/bin/env python3
"""ARC-UI-136: make the brass ring of the world-map lens one ring in the HD interface pack.

The lens in the top left of the world-map window (ARC-UI-134) is built from two frames: the window frame `mapmain`
(the part of the ring outside the lens square) and the cover `pcwincvr` (art 198, drawn over the square, the part of
the ring inside it). The HD redraw painted the two separately, so the rings differ by a few HD texels and the upper
and right arcs show a doubled brass edge. `pcwincvr` is shared with the character sheet and the logbook and stays as it is.

This tool rebuilds the arcs of `mapmain` that lie outside the square from the ring cross-section of `pcwincvr`:
the cover's ring is cut into a polar strip around its own fitted circle; the angles where the cover shows the whole
cross-section are kept, the angles in between are filled by a smooth blend of the two neighbouring cross-sections plus the
grain of the nearby cover ring. Only RGB of the opaque texels of `mapmain` outside the square and within a few texels of the
ring change; the alpha channel, the square and everything else stay byte for byte, so the frame still passes the
pack's alpha rule.

It writes a *staging folder* (the repaired PNG at its installed path and a copy of the installed manifest in which only that
entry's SHA-256 and byte count changed) and never writes into a pack; install it with `hd_pack.py apply --staged ... --base
<installed pack>` as every repair ([workflow](../../../docs/workflow/hd-pack.md)). No frame is in git.

    <res094 venv>/bin/python tools/hd_pack/res095/lens_ring.py --pack PACK --staging local/res095/pack-out/ui136 [--dry-run]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter1d, map_coordinates

MAPMAIN = "interface/world-map/panels/mapmain-f000.png"
COVER = "interface/character-sheet/panels/pcwincvr-f000.png"
LENS_LEGACY = (25, 24, 89)  # AreaWorldMapArtLayout.Lens: x, y, side in window pixels (WM140)
RING_WINDOW = (-26.0, 9.0)  # texels from the cover ring's outer edge that are rebuilt (ring, its inner dark edge, a little shadow)
FEATHER = (5.0, 8.0)        # texels over which the rebuilt ring fades into the original (inner, outer)
POLAR_STEPS = 1440


def _luma(a: np.ndarray) -> np.ndarray:
    return 0.3 * a[..., 0] + 0.59 * a[..., 1] + 0.11 * a[..., 2]


def _brass(a: np.ndarray) -> np.ndarray:
    r, b, alpha = a[..., 0], a[..., 2], a[..., 3]
    return (alpha > 128) & (r - b > 40) & (_luma(a) > 60)


def fit_ring(cover: np.ndarray, origin: tuple[int, int]) -> tuple[float, float, float]:
    """Circle through the outer brass edge of the cover's ring, in window texels: (cx, cy, outer radius)."""
    ys, xs = np.nonzero(_brass(cover))
    if len(xs) < 50:
        raise ValueError("the cover's ring was not found")
    xs = xs + origin[0]
    ys = ys + origin[1]
    cx, cy = float(xs.mean()), float(ys.mean())  # the four brass arcs are symmetric around the ring centre
    radius = 0.0
    for _ in range(4):
        theta = np.degrees(np.arctan2(-(ys - cy), xs - cx)) % 360
        rr = np.hypot(xs - cx, ys - cy)
        px, py = [], []
        for a in np.arange(0, 360, 2):
            sel = (np.abs((theta - a + 180) % 360 - 180) < 1.5) & (rr > 0.9 * np.median(rr)) & (rr < 1.1 * np.median(rr))
            if sel.sum() < 6:
                continue
            i = np.argmax(np.where(sel, rr, 0))
            # the edge counts only where the frame goes on past it (the cover cuts the ring at the square's border)
            beyond_x = cx + (xs[i] - cx) * (rr[i] + 4) / rr[i] - origin[0]
            beyond_y = cy + (ys[i] - cy) * (rr[i] + 4) / rr[i] - origin[1]
            if not (1 <= beyond_x < cover.shape[1] - 1 and 1 <= beyond_y < cover.shape[0] - 1):
                continue
            px.append(xs[i])
            py.append(ys[i])
        px, py = np.array(px, float), np.array(py, float)
        if len(px) < 8:
            raise ValueError("the cover's ring was not found")
        sol = np.linalg.lstsq(np.c_[2 * px, 2 * py, np.ones(len(px))], px ** 2 + py ** 2, rcond=None)[0]
        cx, cy = float(sol[0]), float(sol[1])
        radius = float(np.sqrt(sol[2] + cx * cx + cy * cy))
    return cx, cy, radius


def _polar_strip(cover: np.ndarray, origin: tuple[int, int], circle: tuple[float, float, float], rho: np.ndarray):
    cx, cy, radius = circle
    alpha = cover[..., 3] / 255.0
    theta = np.radians(np.arange(POLAR_STEPS) * 360.0 / POLAR_STEPS)
    t, rr = np.meshgrid(theta, radius + rho, indexing="ij")
    x = cx + rr * np.cos(t) - origin[0]
    y = cy - rr * np.sin(t) - origin[1]
    h, w = cover.shape[:2]
    inside = (x >= 1.5) & (x <= w - 2.5) & (y >= 1.5) & (y <= h - 2.5)
    strip = np.zeros((POLAR_STEPS, len(rho), 4))
    for c in range(3):
        strip[..., c] = map_coordinates(cover[..., c] * alpha, [y, x], order=1, mode="nearest")
    strip[..., 3] = map_coordinates(alpha, [y, x], order=1, mode="nearest")
    return strip, inside.all(axis=1)


def _fill_gaps(strip: np.ndarray, anchored: np.ndarray, rho: np.ndarray) -> np.ndarray:
    """Angles the cover does not show whole: smooth blend of the neighbouring cross-sections plus the grain of the nearby ring."""
    n = len(strip)
    low = gaussian_filter1d(strip, sigma=n / 90.0, axis=0, mode="wrap")
    grain_weight = np.clip((1.0 - rho) / 3.0, 0, 1) * np.clip((rho - RING_WINDOW[0] - 4.0) / 3.0, 0, 1)
    grain = (strip - low) * grain_weight[None, :, None]
    out = strip.copy()
    idx = np.nonzero(anchored)[0]
    if len(idx) == 0:
        raise ValueError("the cover shows no whole cross-section of the ring")
    span = n // 9

    def reflect(pos: float, lo: float, hi: float) -> int:
        w = hi - lo
        m = (pos - lo) % (2 * w)
        return int(lo + (m if m <= w else 2 * w - m))

    for i in range(n):
        if anchored[i]:
            continue
        before, after = idx[idx < i], idx[idx > i]
        p = before[-1] if len(before) else idx[-1] - n
        q = after[0] if len(after) else idx[0] + n
        t = (i - p) / (q - p)
        t = t * t * (3 - 2 * t)
        smooth = low[p % n] * (1 - t) + low[q % n] * t
        grain_p = grain[reflect(p - (i - p), p - span, p) % n]
        grain_q = grain[reflect(q + (q - i), q, q + span) % n]
        out[i] = smooth + grain_p * (1 - t) + grain_q * t
    out[..., 3] = np.clip(out[..., 3], 0, 1)
    return out


def repair_ring(mapmain: np.ndarray, cover: np.ndarray, origin: tuple[int, int]) -> np.ndarray:
    """New RGBA of `mapmain` (uint8, same shape); `origin` is the cover's top left in `mapmain` (texels)."""
    cover = cover.astype(float)
    base = mapmain.astype(float)
    circle = fit_ring(cover, origin)
    cx, cy, radius = circle
    rho = np.arange(RING_WINDOW[0], RING_WINDOW[1] + 1.0)
    strip, whole = _polar_strip(cover, origin, circle, rho)
    strip = _fill_gaps(strip, whole, rho)
    pad = np.concatenate([strip[-2:], strip, strip[:2]], axis=0)

    reach = int(radius + RING_WINDOW[1] + 4)
    y0, y1 = max(0, int(cy) - reach), min(base.shape[0], int(cy) + reach)
    x0, x1 = max(0, int(cx) - reach), min(base.shape[1], int(cx) + reach)
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(float)
    r = np.hypot(xx - cx, yy - cy)
    theta = np.degrees(np.arctan2(-(yy - cy), xx - cx)) % 360
    rel = r - radius
    inside = (rel >= RING_WINDOW[0]) & (rel <= RING_WINDOW[1])
    coords = [np.where(inside, theta / 360 * POLAR_STEPS, 0) + 2, np.where(inside, rel - RING_WINDOW[0], 0)]
    sampled = [map_coordinates(pad[..., c], coords, order=1, mode="nearest") for c in range(4)]
    weight = (np.clip((rel - RING_WINDOW[0]) / FEATHER[0], 0, 1) * np.clip((RING_WINDOW[1] - rel) / FEATHER[1], 0, 1) * inside
              * (base[y0:y1, x0:x1, 3] > 127))
    out = base.copy()
    for c in range(3):
        out[y0:y1, x0:x1, c] = base[y0:y1, x0:x1, c] * (1 - weight * sampled[3]) + sampled[c] * weight
    return np.clip(np.rint(out), 0, 255).astype(np.uint8)


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pack", required=True, help="the installed pack folder (read only)")
    ap.add_argument("--staging", required=True, help="a new folder for the repair (not inside the pack)")
    ap.add_argument("--dry-run", action="store_true", help="fit and report, write nothing")
    args = ap.parse_args(argv)
    pack, staging = Path(args.pack).expanduser().resolve(), Path(args.staging).expanduser().resolve()
    if staging == pack or pack in staging.parents:
        print("refusing: the staging folder is inside the pack", file=sys.stderr)
        return 2
    manifest = json.loads((pack / "manifest.json").read_text())
    entry = next((e for e in manifest["entries"] if e.get("file") == MAPMAIN), None)
    if entry is None or not any(e.get("file") == COVER for e in manifest["entries"]):
        print("refusing: the pack lacks the mapmain or pcwincvr frame", file=sys.stderr)
        return 2
    scale = int(manifest["scale"])
    lx, ly, side = LENS_LEGACY
    origin = (lx * scale, ly * scale)
    mapmain = np.array(Image.open(pack / MAPMAIN).convert("RGBA"))
    cover = np.array(Image.open(pack / COVER).convert("RGBA"))
    if cover.shape[0] != side * scale or cover.shape[1] != side * scale:
        print(f"refusing: the cover is {cover.shape[1]}x{cover.shape[0]}, expected {side * scale}", file=sys.stderr)
        return 2
    circle = fit_ring(cover.astype(float), origin)
    print(f"cover ring: centre ({circle[0] / scale:.2f}, {circle[1] / scale:.2f}) window px, outer radius {circle[2] / scale:.2f}")
    result = repair_ring(mapmain, cover, origin)
    changed = int((result != mapmain).any(axis=2).sum())
    if (result[..., 3] != mapmain[..., 3]).any():
        print("refusing: the alpha channel changed", file=sys.stderr)
        return 1
    print(f"changed texels: {changed}; alpha unchanged")
    if args.dry_run:
        return 0
    target = staging / MAPMAIN
    target.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(result, "RGBA").save(target, optimize=False)
    entry = dict(entry)
    entry["sha256"], entry["bytes"] = _sha(target), target.stat().st_size
    manifest["entries"] = [entry if e.get("file") == MAPMAIN else e for e in manifest["entries"]]
    (staging / "manifest.json").write_text(json.dumps(manifest, indent=2))
    print(f"wrote {target} sha256 {entry['sha256'][:12]}; apply with hd_pack.py apply --staged {staging} --base {pack} --task ARC-UI-136 --dry-run")
    return 0


if __name__ == "__main__":
    sys.exit(main())
