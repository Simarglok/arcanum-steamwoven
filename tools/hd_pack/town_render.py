#!/usr/bin/env python3
"""ARC-RENDER-3063: render a town map with our engine (docs/rendering/hd-world-town-map.md, "Render path").

Runs on the owner's Mac only (a window run of the AREA viewer needs focus). Needs numpy and Pillow (the venv `local/res094/venv`, run with `-I`).
Nothing here contains or produces tracked art: every output goes under the ignored `local/`.

  plan     one viewer run for the town's .tmi record and tile list, then the capture windows (a lattice of 3 x 3 sector windows, step 2,
           so every sector edge lies inside a window) and each window's shots -> OUT/plan.json, OUT/job_<cx>_<cy>.json
  capture  every window of the plan through `./arcanum smoke map ... --stages door-close` with ARC_TOWNCAP (the viewer seam,
           tools/terrain-viewer/Godot/TerrainViewerTownCapture.cs: no hero, no critters, roofs closed, no overlay)
  stitch   the captures into the town picture (each pixel from the window that holds it deepest), cut into the original's tiles at
           scale 1.0 (or 0.5 = 2 x 2 box, 0.25 = the original's nearest) -> OUT/tiles-<scale>/<Town>NNNNNN.png, OUT/render.json
           (--original DIR checks the 0.25 sampling against the original tiles: mean error and the worst 64 x 64 windows)
  run      plan + capture + stitch
  cost     the per-town and total cost estimate of all towns from a tile-count table (--index townmap-index.tsv: module, town, tiles, ...), scaled
           from the measured Ashbury run (ARC-RENDER-3063)

Picture geometry (ledger WM69): the picture is W x H screen pixels at scale 1; the origin location sits at (W/2, H/2 + 20); a tile is
256 x 128 pixels at scale 1 and its index is row * columns + column.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
REPO = Path(__file__).resolve().parents[2]
TILE_W, TILE_H = 256, 128
SHOT_W, SHOT_H = 5120, 1296          # the viewer window of a shot (the height clamps at the screen)
SECTOR = 64
MAIN_MAP = "Arcanum1-024-fixed"
# Terrain the viewer draws as filler beyond the loaded map (ARC-RES-2992): never taken as town pixels.
FILLER = [(96, 96, 255), (200, 200, 128), (32, 128, 32), (32, 96, 32)]
WINDOW_LIMIT = 10.0                    # the worst 64 x 64 window (0.25 sampling) must stay under this mean error (a missing building piece gave 14.4 where the whole town had 1.7)
HOUR = 15                              # the clock of the viewer's default start (ARC-RES-2992 measured it)


def read_tmi(path: Path, town: str) -> dict:
    with open(path, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle, delimiter="\t"):
            if row["name"].lower() == town.lower():
                return {k: (float(v) if k == "scale" else int(v) if k != "name" else v) for k, v in row.items()}
    raise SystemExit(f"town {town!r} not in {path}")


def sector_fraction(x, y, tmi):
    """Fractional sector coordinates of picture pixels (x, y) of scale 1."""
    dd = (x - tmi["width"] / 2) / 40
    ss = (y - tmi["height"] / 2 - 20) / 20
    return (tmi["originX"] + (ss - dd) / 2) / SECTOR, (tmi["originY"] + (ss + dd) / 2) / SECTOR


def needed_sectors(tmi, tiles, share=0.25):
    """The sectors that hold a real share (default 25 %) of their pixels in the tiles to write; tile 000000 (the stand-in, far from the town) is not counted.
    A sector below the share is only drawn as the edge of a neighbour's window."""
    counts = {}
    for index in tiles:
        if index == 0:
            continue
        r, c = divmod(index, tmi["columns"])
        ys, xs = np.mgrid[r * TILE_H:(r + 1) * TILE_H:32, c * TILE_W:(c + 1) * TILE_W:32]
        fx, fy = sector_fraction(xs.astype(np.float64), ys.astype(np.float64), tmi)
        for key in zip(np.floor(fx).astype(int).ravel().tolist(), np.floor(fy).astype(int).ravel().tolist()):
            counts[key] = counts.get(key, 0) + 1
    full_sector = SECTOR * SECTOR * 1600 / (32 * 32)        # sample points of one sector (a cell diamond is 1600 pixels)
    return {key for key, n in counts.items() if n >= share * full_sector}


def lattice_windows(needed):
    """Window centres on a lattice of step 2 (best phase): every lattice window that holds a needed sector, so that a sector edge is never the edge of
    every window that draws it (a window draws its outermost ring without what lies beyond it: gaps, black lines at the seam)."""
    best = None
    for px in (0, 1):
        for py in (0, 1):
            centres = set()
            for sx, sy in needed:
                for cx in range(sx - 1, sx + 2):
                    for cy in range(sy - 1, sy + 2):
                        if (cx - px) % 2 == 0 and (cy - py) % 2 == 0:
                            centres.add((cx, cy))
            if best is None or len(centres) < len(best):
                best = centres
    return sorted(best)


def depth(fx, fy, centre):
    """How far (in sectors) a point lies inside the 3 x 3 window of `centre`; 0 or less is outside."""
    cx, cy = centre
    return np.minimum(np.minimum(fx - (cx - 1), (cx + 2) - fx), np.minimum(fy - (cy - 1), (cy + 2) - fy))


def plan_windows(tmi, tiles, step=32):
    needed = needed_sectors(tmi, tiles)
    centres = lattice_windows(needed)
    w, h, cols = tmi["width"], tmi["height"], tmi["columns"]
    ys, xs = np.mgrid[0:h:step, 0:w:step]
    tile_of = (ys // TILE_H) * cols + (xs // TILE_W)
    required = np.isin(tile_of, np.fromiter(tiles, dtype=np.int64))
    fx, fy = sector_fraction(xs.astype(np.float64), ys.astype(np.float64), tmi)
    depths = np.stack([depth(fx, fy, c) for c in centres])
    owner = depths.argmax(0)
    shots_per_window = {}
    rows, cols_shot = math.ceil(h / SHOT_H), math.ceil(w / SHOT_W)
    for i, centre in enumerate(centres):
        mine = required & (owner == i) & (depths[i] > 0)
        shots = []
        for r in range(rows):
            for c in range(cols_shot):
                y0, y1, x0, x1 = r * SHOT_H, (r + 1) * SHOT_H, c * SHOT_W, (c + 1) * SHOT_W
                sub = mine[max(y0 // step - 2, 0):y1 // step + 3, max(x0 // step - 2, 0):x1 // step + 3]
                if sub.any():
                    shots.append({"name": f"r{r}c{c}", "dx": int(c * SHOT_W + SHOT_W / 2 - w / 2), "dy": int(r * SHOT_H + SHOT_H / 2 - h / 2 - 20)})
        if shots:
            shots_per_window[centre] = shots
    return needed, shots_per_window


def run_smoke(sector, job, extra=(), expect=None):
    command = [str(REPO / "arcanum"), "smoke", "map", MAIN_MAP, "--stages", "door-close", "--sector", f"{sector[0]},{sector[1]}",
               "--no-config-check", "--env", f"ARC_TOWNCAP={job}", *extra]
    started = time.time()
    result = subprocess.run(command, cwd=REPO, capture_output=True, text=True)
    ok = "SMOKE PASS" in result.stdout
    if ok and expect is not None:
        # the viewer can leave the capture early (no town, no session) and still pass the stage: the output must be there and new
        ok = expect.exists() and expect.stat().st_mtime >= started - 1
    if not ok:
        print(result.stdout[-1500:], result.stderr[-500:])
    return ok, time.time() - started


def cmd_plan(args):
    out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
    info_dir = out / "info"
    job = out / "job_info.json"
    seconds = 0.0
    if not (info_dir / "tiles.txt").exists():
        job.write_text(json.dumps({"out": str(info_dir), "w": 1280, "h": 720, "zoom": 1.0, "town": args.town, "hour": HOUR, "shots": []}))
        ok, seconds = run_smoke(args.info_sector, job, expect=info_dir / "tiles.txt")
        if not ok:
            raise SystemExit("info run failed")
    tmi = read_tmi(info_dir / "tmi-all.tsv", args.town)
    tiles = [int(line) for line in (info_dir / "tiles.txt").read_text().split()]
    needed, windows = plan_windows(tmi, tiles)
    plan = {"town": args.town, "tmi": tmi, "tiles": tiles, "needed_sectors": sorted(needed),
            "windows": [{"centre": list(c), "shots": s} for c, s in windows.items()]}
    (out / "plan.json").write_text(json.dumps(plan))
    for stale in out.glob("cap_*"):                     # captures of an earlier plan (another shot grid) are never reused
        shutil.rmtree(stale)
    for centre, shots in windows.items():
        n = f"{centre[0]}_{centre[1]}"
        (out / f"job_{n}.json").write_text(json.dumps({"out": str(out / f"cap_{n}"), "w": SHOT_W, "h": SHOT_H, "zoom": 1.0, "town": args.town,
                                                      "hour": HOUR, "shots": shots}))
    print(f"plan {args.town}: {len(tiles)} tiles, {len(needed)} sectors, {len(windows)} windows, {sum(len(s) for s in windows.values())} shots (info run {seconds:.0f}s)")


def cmd_capture(args):
    out = Path(args.out)
    plan = json.loads((out / "plan.json").read_text())
    total = 0.0
    for window in plan["windows"]:
        n = f"{window['centre'][0]}_{window['centre'][1]}"
        if (out / f"cap_{n}" / "origin.txt").exists() and not args.force:
            print(f"window {n}: done"); continue
        ok, seconds = run_smoke(window["centre"], out / f"job_{n}.json", ("--no-build",), expect=out / f"cap_{n}" / "shots.tsv")
        total += seconds
        print(f"window {n}: {'ok' if ok else 'FAILED'} {seconds:.0f}s, {len(window['shots'])} shots")
        if not ok:
            raise SystemExit(1)
    print(f"capture total {total:.0f}s")


def stitch_picture(out: Path, plan: dict):
    tmi = plan["tmi"]
    w, h = tmi["width"], tmi["height"]
    full = np.zeros((h, w, 3), np.uint8)
    best = np.zeros((h, w), np.uint8)               # deepest window so far, in 1/100 sector (0 = none)
    for window in plan["windows"]:
        n = f"{window['centre'][0]}_{window['centre'][1]}"
        cap = out / f"cap_{n}"
        ox, oy = [float(v) for v in (cap / "origin.txt").read_text().split()[:2]]
        with open(cap / "shots.tsv", newline="") as handle:
            rows = list(csv.DictReader(handle, delimiter="\t"))
        for row in rows:
            image = np.asarray(Image.open(cap / f"{row['name']}.png").convert("RGB"))
            ih, iw = image.shape[:2]
            if (iw, ih) != (SHOT_W, SHOT_H):
                raise SystemExit(f"{cap.name}/{row['name']}.png is {iw}x{ih}, the plan needs {SHOT_W}x{SHOT_H} (the viewer window was clamped by the screen)")
            px = int(round(float(row["camX"]) - iw / 2 - ox + w / 2)); py = int(round(float(row["camY"]) - ih / 2 - oy + h / 2 + 20))
            x0, y0, x1, y1 = max(px, 0), max(py, 0), min(px + iw, w), min(py + ih, h)
            if x1 <= x0 or y1 <= y0:
                continue
            sub = image[y0 - py:y1 - py, x0 - px:x1 - px]
            ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float64)
            fx, fy = sector_fraction(xs, ys, tmi)
            d = np.clip(np.floor(depth(fx, fy, tuple(window["centre"])) * 100), 0, 255).astype(np.uint8)
            filler = np.zeros(sub.shape[:2], bool)
            for colour in FILLER:
                filler |= (sub == np.array(colour, np.uint8)).all(2)
            take = ~filler & (d > 0) & (d > best[y0:y1, x0:x1])
            full[y0:y1, x0:x1][take] = sub[take]
            best[y0:y1, x0:x1][take] = d[take]
    return full, best


def reduce_picture(full, scale):
    h, w = full.shape[:2]
    if scale == 1.0:
        return full
    if scale == 0.5:
        out = np.zeros((h // 2, w // 2, 3), np.uint8)
        for y in range(0, h // 2, 512):
            out[y:y + 512] = np.round(full[y * 2:(y + 512) * 2].reshape(-1, 2, w // 2, 2, 3).astype(np.float32).mean((1, 3))).astype(np.uint8)
        return out
    if scale == 0.25:
        return np.ascontiguousarray(full[2::4, 2::4])
    raise SystemExit("scale must be 1.0, 0.5 (2 x 2 box) or 0.25 (nearest, the original's)")


def compare_original(full, plan, original: Path):
    """Our 1:1 picture sampled like the original (nearest at phase 2) against the original tiles: mean error and the worst 64 x 64 windows."""
    tmi = plan["tmi"]
    cols, rows = tmi["columns"], tmi["rows"]
    th, tw = TILE_H // 4, TILE_W // 4
    ours = np.ascontiguousarray(full[2::4, 2::4]).astype(np.int16)
    orig = np.zeros_like(ours)
    for index in plan["tiles"]:
        path = original / f"{plan['town']}{index:06d}.png"
        if not path.exists():
            continue
        r, c = divmod(index, cols)
        orig[r * th:(r + 1) * th, c * tw:(c + 1) * tw] = np.asarray(Image.open(path).convert("RGB"))
    inside = np.zeros(ours.shape[:2], bool)
    for index in plan["tiles"]:
        r, c = divmod(index, cols)
        inside[r * th:(r + 1) * th, c * tw:(c + 1) * tw] = True
    ours = np.where(inside[:, :, None], ours, 0)       # only the tiles that are written count
    both = (orig.sum(2) > 0) & (ours.sum(2) > 0)
    err = np.where(both, np.abs(ours - orig).mean(2), 0.0)
    block = 32
    bh, bw = err.shape[0] // block, err.shape[1] // block
    es = err[:bh * block, :bw * block].reshape(bh, block, bw, block).sum((1, 3))
    cs = both[:bh * block, :bw * block].reshape(bh, block, bw, block).sum((1, 3))
    # 64 x 64 windows at a stride of 32: four neighbouring blocks, kept when at least a quarter of their pixels were compared
    window_e = es[:-1, :-1] + es[1:, :-1] + es[:-1, 1:] + es[1:, 1:]
    window_c = cs[:-1, :-1] + cs[1:, :-1] + cs[:-1, 1:] + cs[1:, 1:]
    mean = np.where(window_c >= 64 * 64 // 4, window_e / np.maximum(window_c, 1), 0.0)
    order = np.argsort(mean.ravel())[::-1][:8]
    worst = [{"x4": int(i % mean.shape[1] * block), "y4": int(i // mean.shape[1] * block), "mean_error": round(float(mean.ravel()[i]), 2)} for i in order]
    d = err[both]
    worst_mean = worst[0]["mean_error"] if worst else 0.0
    return {"window_limit": WINDOW_LIMIT, "window_ok": worst_mean < WINDOW_LIMIT, "compared_pixels": int(both.sum()), "mean_error": round(float(d.mean()), 3), "below_5": round(float((d < 5).mean()), 4),
            "below_10": round(float((d < 10).mean()), 4), "worst_64x64_windows": worst,
            "orig_only_nonblack": int(((orig.sum(2) > 0) & ~(ours.sum(2) > 0)).sum()), "ours_only_nonblack": int((~(orig.sum(2) > 0) & (ours.sum(2) > 0)).sum())}


def cmd_stitch(args):
    out = Path(args.out)
    plan = json.loads((out / "plan.json").read_text())
    started = time.time()
    full, best = stitch_picture(out, plan)
    report = {"town": plan["town"], "tiles": len(plan["tiles"]), "stitch_seconds": round(time.time() - started, 1)}
    black = ~full.any(2)
    report["uncovered_pixels_in_tiles"] = int(sum(
        black[(i // plan["tmi"]["columns"]) * TILE_H:(i // plan["tmi"]["columns"] + 1) * TILE_H, (i % plan["tmi"]["columns"]) * TILE_W:(i % plan["tmi"]["columns"] + 1) * TILE_W].sum()
        for i in plan["tiles"]))
    if args.original:
        report["against_original_0.25"] = compare_original(full, plan, Path(args.original))
    scale = float(args.scale)
    picture = reduce_picture(full, scale)
    del full, best
    th, tw = int(TILE_H * scale), int(TILE_W * scale)
    tiles_dir = out / f"tiles-{args.scale}"
    tiles_dir.mkdir(exist_ok=True)
    total = 0
    cols = plan["tmi"]["columns"]
    for index in plan["tiles"]:
        r, c = divmod(index, cols)
        tile = Image.fromarray(np.ascontiguousarray(picture[r * th:(r + 1) * th, c * tw:(c + 1) * tw]))
        path = tiles_dir / f"{plan['town']}{index:06d}.png"
        tile.save(path)
        total += path.stat().st_size
    report.update({"scale": scale, "tile_size": [tw, th], "bytes": total, "bytes_per_tile": round(total / max(len(plan["tiles"]), 1))})
    (out / "render.json").write_text(json.dumps(report, indent=1))
    print(json.dumps(report, indent=1))
    if args.original and not report["against_original_0.25"]["window_ok"]:
        raise SystemExit(f"FAIL: a 64 x 64 window differs by more than {WINDOW_LIMIT} from the original (see worst_64x64_windows)")


# Measured on Ashbury (2991 tiles), 2026-11-21, on a Mac whose window queue was shared with other threads: capture 692 s, stitch with the check 289 s,
# 112.7 MB of 24-bit PNG tiles at scale 1.0 (37.7 KB per tile). The 0.5 box tiles are about a third of that (12.6 KB per tile).
ASHBURY = {"tiles": 2991, "capture_seconds": 692, "stitch_seconds": 289, "bytes_1.0": 112_672_145}


def cmd_cost(args):
    rows = []
    with open(args.index, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle, delimiter="\t"):
            tiles = int(row["tiles"])
            rows.append((row["module"], row["town"], tiles))
    per_tile_seconds = (ASHBURY["capture_seconds"] + ASHBURY["stitch_seconds"]) / ASHBURY["tiles"]
    per_tile_bytes = ASHBURY["bytes_1.0"] / ASHBURY["tiles"]
    total_tiles = sum(t for _, _, t in rows)
    lines = ["module\ttown\ttiles\test_seconds\test_bytes_1.0\test_bytes_0.5"]
    for module, town, tiles in rows:
        lines.append(f"{module}\t{town}\t{tiles}\t{tiles * per_tile_seconds:.0f}\t{tiles * per_tile_bytes:.0f}\t{tiles * per_tile_bytes / 3.1:.0f}")
    print("\n".join(lines))
    print(f"TOTAL {len(rows)} towns, {total_tiles} tiles, about {total_tiles * per_tile_seconds / 3600:.1f} h, "
          f"{total_tiles * per_tile_bytes / 1e9:.2f} GB at 1.0, {total_tiles * per_tile_bytes / 3.1 / 1e9:.2f} GB at 0.5 (box)", file=sys.stderr)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    cost = sub.add_parser("cost")
    cost.add_argument("--index", required=True)
    for name, fn in (("plan", cmd_plan), ("capture", cmd_capture), ("stitch", cmd_stitch), ("run", None)):
        p = sub.add_parser(name)
        p.add_argument("--out", required=True, help="working folder (under local/)")
        if name in ("plan", "run"):
            p.add_argument("--town", required=True)
            p.add_argument("--info-sector", default=(504, 976), type=lambda s: tuple(int(v) for v in s.split(",")), help="any sector of the main map (the info run)")
        if name in ("capture", "run"):
            p.add_argument("--force", action="store_true")
        if name in ("stitch", "run"):
            p.add_argument("--scale", default="1.0", choices=["1.0", "0.5", "0.25"])
            p.add_argument("--original", help="folder of the original tile PNGs (<Town>NNNNNN.png) for the 0.25 check")
    args = parser.parse_args(argv)
    if args.command == "cost":
        cmd_cost(args)
    elif args.command == "plan":
        cmd_plan(args)
    elif args.command == "capture":
        cmd_capture(args)
    elif args.command == "stitch":
        cmd_stitch(args)
    else:
        cmd_plan(args); cmd_capture(args); cmd_stitch(args)


if __name__ == "__main__":
    main()
