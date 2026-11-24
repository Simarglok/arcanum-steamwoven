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
# The viewer's grey (76, 76, 76) where a whole map has nothing to draw (no sector file there, or beyond the map): only regions of at least 5 x 5 pixels count as filler, so a grey pixel of a stone texture stays.
VOID_GREY = (76, 76, 76)
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


def grey_void(image):
    """The pixels of the viewer's void grey that lie in a grey region of at least 5 x 5 pixels (a morphological opening of the grey mask)."""
    mask = (image == np.array(VOID_GREY, np.uint8)).all(2)
    if not mask.any():
        return mask
    h, w = mask.shape
    padded = np.pad(mask, 2)
    core = np.ones_like(mask)
    for dy in range(5):
        for dx in range(5):
            core &= padded[dy:dy + h, dx:dx + w]
    if not core.any():
        return core
    padded = np.pad(core, 2)
    grown = np.zeros_like(mask)
    for dy in range(5):
        for dx in range(5):
            grown |= padded[dy:dy + h, dx:dx + w]
    return grown & mask


def depth(fx, fy, centre):
    """How far (in sectors) a point lies inside the 3 x 3 window of `centre`; 0 or less is outside."""
    cx, cy = centre
    return np.minimum(np.minimum(fx - (cx - 1), (cx + 2) - fx), np.minimum(fy - (cy - 1), (cy + 2) - fy))


def plan_windows(tmi, tiles, step=32, whole=False):
    """The capture windows. `whole` (a map that loads whole: every sector is drawn, there is no window edge) is one run with no sector, key (None, None)."""
    needed = needed_sectors(tmi, tiles)
    centres = [(None, None)] if whole else lattice_windows(needed)
    w, h, cols = tmi["width"], tmi["height"], tmi["columns"]
    ys, xs = np.mgrid[0:h:step, 0:w:step]
    tile_of = (ys // TILE_H) * cols + (xs // TILE_W)
    required = np.isin(tile_of, np.fromiter(tiles, dtype=np.int64))
    fx, fy = sector_fraction(xs.astype(np.float64), ys.astype(np.float64), tmi)
    depths = np.stack([np.ones_like(fx) if c[0] is None else depth(fx, fy, c) for c in centres])
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


def run_smoke(sector, job, extra=(), expect=None, map_name=MAIN_MAP):
    """One viewer run of the capture seam. `sector` is the window centre of a map that opens as a window (the main map); a map that loads whole takes none."""
    command = [str(REPO / "arcanum"), "smoke", "map", map_name, "--stages", "door-close",
               *(["--sector", f"{sector[0]},{sector[1]}"] if sector is not None else []),
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


def window_name(centre) -> str:
    return "whole" if centre[0] is None else f"{centre[0]}_{centre[1]}"


def town_inputs(scan: Path, town: str):
    """The town's .tmi record and tile list from a scan folder (`scan` command: tmi-all.tsv and tiles-all/NUMBER.txt), so no viewer run is needed to plan it."""
    tmi = read_tmi(scan / "tmi-all.tsv", town)
    tiles = [int(line) for line in (scan / "tiles-all" / f"{tmi['number']}.txt").read_text().split()]
    return tmi, tiles


def cmd_plan(args):
    out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
    whole = args.map.lower() != MAIN_MAP.lower()
    seconds = 0.0
    if args.scan:
        tmi, tiles = town_inputs(Path(args.scan), args.town)
    else:
        info_dir = out / "info"
        job = out / "job_info.json"
        if not (info_dir / "tiles.txt").exists():
            job.write_text(json.dumps({"out": str(info_dir), "w": 1280, "h": 720, "zoom": 1.0, "town": args.town, "hour": HOUR, "shots": []}))
            ok, seconds = run_smoke(None if whole else args.info_sector, job, expect=info_dir / "tiles.txt", map_name=args.map)
            if not ok:
                raise SystemExit("info run failed")
        tmi = read_tmi(info_dir / "tmi-all.tsv", args.town)
        tiles = [int(line) for line in (info_dir / "tiles.txt").read_text().split()]
    needed, windows = plan_windows(tmi, tiles, whole=whole)
    plan = {"town": args.town, "map": args.map, "whole": whole, "tmi": tmi, "tiles": tiles, "needed_sectors": sorted(needed),
            "windows": [{"centre": list(c), "shots": s} for c, s in windows.items()]}
    (out / "plan.json").write_text(json.dumps(plan))
    for stale in out.glob("cap_*"):                     # captures of an earlier plan (another shot grid) are never reused
        shutil.rmtree(stale)
    for centre, shots in windows.items():
        n = window_name(centre)
        (out / f"job_{n}.json").write_text(json.dumps({"out": str(out / f"cap_{n}"), "w": SHOT_W, "h": SHOT_H, "zoom": 1.0, "town": args.town,
                                                      "hour": HOUR, "shots": shots}))
    print(f"plan {args.town} ({args.map}): {len(tiles)} tiles, {len(needed)} sectors, {len(windows)} windows, {sum(len(s) for s in windows.values())} shots (info run {seconds:.0f}s)")


def cmd_capture(args):
    out = Path(args.out)
    plan = json.loads((out / "plan.json").read_text())
    total = 0.0
    seconds_by_window = {}
    for window in plan["windows"]:
        n = window_name(window["centre"])
        if (out / f"cap_{n}" / "origin.txt").exists() and not args.force:
            print(f"window {n}: done"); continue
        ok, seconds = run_smoke(None if plan.get("whole") else window["centre"], out / f"job_{n}.json", ("--no-build",), expect=out / f"cap_{n}" / "shots.tsv",
                                map_name=plan.get("map", MAIN_MAP))
        total += seconds
        seconds_by_window[n] = round(seconds, 1)
        print(f"window {n}: {'ok' if ok else 'FAILED'} {seconds:.0f}s, {len(window['shots'])} shots")
        if not ok:
            raise SystemExit(1)
    (out / "capture.json").write_text(json.dumps({"windows": len(plan["windows"]), "shots": sum(len(w["shots"]) for w in plan["windows"]),
                                                  "seconds": round(total, 1), "seconds_by_window": seconds_by_window}))
    print(f"capture total {total:.0f}s")


def stitch_picture(out: Path, plan: dict):
    tmi = plan["tmi"]
    w, h = tmi["width"], tmi["height"]
    full = np.zeros((h, w, 3), np.uint8)
    best = np.zeros((h, w), np.uint8)               # deepest window so far, in 1/100 sector (0 = none)
    for window in plan["windows"]:
        n = window_name(window["centre"])
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
            d = np.full(sub.shape[:2], 100, np.uint8) if window["centre"][0] is None else np.clip(np.floor(depth(fx, fy, tuple(window["centre"])) * 100), 0, 255).astype(np.uint8)
            filler = np.zeros(sub.shape[:2], bool)
            for colour in FILLER:
                filler |= (sub == np.array(colour, np.uint8)).all(2)
            filler |= grey_void(sub)
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


# Measured on Ashbury (2991 tiles), 2026-11-21: capture 692 s on a Mac whose window queue was shared with other threads (queue waits included, so an upper bound), stitch with the check 24 s,
# 112.7 MB of 24-bit PNG tiles at scale 1.0 (37.7 KB per tile). The 0.5 box tiles are about a third of that (12.6 KB per tile).
ASHBURY = {"tiles": 2991, "capture_seconds": 692, "stitch_seconds": 24, "bytes_1.0": 112_672_145}


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


# ---- all towns (ARC-RENDER-3107): the plan table and the full run -------------------------------------------------------------------------------------

def read_tsv(path: Path):
    with open(path, newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle, delimiter="\t"))


def slug(name: str) -> str:
    return "".join(c if c.isalnum() else "-" for c in name).strip("-").lower()


def pick_map(tmi, rows):
    """The map a town map is drawn from: of the maps whose sectors carry the town map's number, the one whose sector bounds lie nearest the origin location of the
    .tmi (a town map number can sit on two maps: K'na Tha 2's origin is on the main map, but interior sectors carry its number too)."""
    ox, oy = tmi["originX"] // SECTOR, tmi["originY"] // SECTOR

    def distance(row):
        dx = max(int(row["min_x"]) - ox, 0, ox - int(row["max_x"]))
        dy = max(int(row["min_y"]) - oy, 0, oy - int(row["max_y"]))
        return math.hypot(dx, dy)
    return min(rows, key=distance), distance(min(rows, key=distance))


TOWN_COLUMNS = ["module", "number", "name", "tiles", "picture", "map", "kind", "origin_sector", "map_sector_bounds", "other_maps", "needed_sectors", "windows", "shots", "status", "note"]


def cmd_towns(args):
    """The plan table of every town map: its game map, the opening kind (window of the main map or a whole map), the sector the window run needs and the windows and shots of the plan."""
    scan = Path(args.scan)
    tmis = {int(r["number"]): r for r in read_tsv(scan / "tmi-all.tsv")}
    by_number = {}
    for row in read_tsv(scan / "townmap-sectors.tsv"):
        by_number.setdefault(int(row["townmap"]), []).append(row)
    rows = []
    for number in sorted(tmis):
        name = tmis[number]["name"]
        tmi = read_tmi(scan / "tmi-all.tsv", name)
        tiles = [int(t) for t in (scan / "tiles-all" / f"{number}.txt").read_text().split()] if (scan / "tiles-all").exists() else []
        candidates = by_number.get(number, [])
        row = {"module": args.module, "number": number, "name": name, "tiles": len(tiles) or tmis[number].get("tiles", ""), "picture": f"{tmi['width']}x{tmi['height']}"}
        if not candidates:
            rows.append({**row, "status": "no-map", "note": "no sector of any map of the module carries this town map number"}); continue
        best, dist = pick_map(tmi, candidates)
        whole = best["map"].lower() != MAIN_MAP.lower()
        row.update({"map": MAIN_MAP if not whole else best["map"], "kind": "whole" if whole else "window", "origin_sector": f"{tmi['originX'] // SECTOR},{tmi['originY'] // SECTOR}",
                    "map_sector_bounds": f"{best['min_x']},{best['min_y']}..{best['max_x']},{best['max_y']} ({best['sectors']} sectors)",
                    "other_maps": ";".join(f"{c['map']} ({c['sectors']} sectors, {c['min_x']},{c['min_y']}..{c['max_x']},{c['max_y']})" for c in candidates if c is not best)})
        if tiles:
            needed, windows = plan_windows(tmi, tiles, whole=whole)
            row.update({"needed_sectors": len(needed), "windows": len(windows), "shots": sum(len(s) for s in windows.values())})
        note = []
        if dist > 2:
            note.append(f"origin sector {dist:.0f} away from the sectors that carry the number")
        if args.module != "Arcanum":
            note.append("the viewer's area load opens the Arcanum module only (#3176)")
        row.update({"status": "render" if args.module == "Arcanum" else "blocked", "note": "; ".join(note)})
        rows.append(row)
    with open(args.out, "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=TOWN_COLUMNS, delimiter="\t")
        writer.writeheader(); writer.writerows(rows)
    print(f"{len(rows)} towns -> {args.out}")


RESULT_COLUMNS = ["module", "number", "name", "map", "kind", "tiles", "windows", "shots", "capture_s", "stitch_s", "bytes", "mean_error", "worst_window", "result", "classification"]


def town_result(out: Path, town: dict, result: str, classification: str = "") -> dict:
    report = json.loads((out / "render.json").read_text()) if (out / "render.json").exists() else {}
    capture = json.loads((out / "capture.json").read_text()) if (out / "capture.json").exists() else {}
    against = report.get("against_original_0.25", {})
    worst = against.get("worst_64x64_windows") or [{}]
    return {"module": town["module"], "number": town["number"], "name": town["name"], "map": town["map"], "kind": town["kind"], "tiles": town["tiles"],
            "windows": capture.get("windows", ""), "shots": capture.get("shots", ""), "capture_s": capture.get("seconds", ""), "stitch_s": report.get("stitch_seconds", ""),
            "bytes": report.get("bytes", ""), "mean_error": against.get("mean_error", ""), "worst_window": worst[0].get("mean_error", ""), "result": result, "classification": classification}


def cmd_all(args):
    """Every town of the table that is marked `render`: plan, capture and stitch with the 64 x 64 check into ROOT/<number>-<name>/, one result row per town in ROOT/results.tsv
    (a finished town is skipped; a failed one is recorded and the run goes on). Towns run in the order given by --only or the table's."""
    root = Path(args.out); root.mkdir(parents=True, exist_ok=True)
    towns = [t for t in read_tsv(Path(args.towns)) if t["status"] == "render"]
    if args.only:
        wanted = [int(n) for n in args.only.split(",")]
        towns = [t for n in wanted for t in towns if int(t["number"]) == n]
    results_path = root / "results.tsv"
    done = {r["number"]: r for r in read_tsv(results_path)} if results_path.exists() else {}
    for town in towns:
        number = town["number"]
        if number in done and done[number]["result"] in ("pass", "window-fail") and not args.redo:
            continue
        out = root / f"{int(number):03d}-{slug(town['name'])}"
        original = Path(args.originals) / town["module"] / town["name"]
        started = time.time()
        label = f"[{number}] {town['name']} ({town['map']}, {town['kind']}, {town['tiles']} tiles)"
        print(label, flush=True)
        try:
            ns = argparse.Namespace(out=str(out), town=town["name"], map=town["map"], scan=args.scan, info_sector=None, force=False, scale="1.0", original=str(original))
            if not args.stitch_only:             # (the captures of an earlier run are kept; a plan would clear them)
                cmd_plan(ns); cmd_capture(ns)
            try:
                cmd_stitch(ns); result = "pass"
            except SystemExit as stop:
                result = "window-fail" if "64 x 64" in str(stop) else f"stitch-error: {stop}"
        except SystemExit as stop:
            result = f"capture-error: {stop}"
        except Exception as error:                   # one town never stops the run
            result = f"error: {type(error).__name__}: {error}"
        row = town_result(out, town, result)
        done[number] = row
        with open(results_path, "w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=RESULT_COLUMNS, delimiter="\t")
            writer.writeheader(); writer.writerows(sorted(done.values(), key=lambda r: int(r["number"])))
        print(f"  -> {result} in {time.time() - started:.0f}s", flush=True)


# ---- composite for the human look (ARC-RENDER-3107) ------------------------------------------------------------------------------------------------------

def mosaic_region(tiles_dir: Path, town: str, tiles, cols: int, tw: int, th: int, x0: int, y0: int, w: int, h: int):
    """The part (x0, y0, w, h) of a town's picture made of its tile files (<Town>NNNNNN.png, tw x th each); a missing tile is black."""
    canvas = np.zeros((h, w, 3), np.uint8)
    for r in range(y0 // th, (y0 + h - 1) // th + 1):
        for c in range(x0 // tw, (x0 + w - 1) // tw + 1):
            index = r * cols + c
            if index not in tiles:
                continue
            path = tiles_dir / f"{town}{index:06d}.png"
            if not path.exists():
                continue
            tile = np.asarray(Image.open(path).convert("RGB"))
            tx0, ty0 = c * tw, r * th
            sx0, sy0, sx1, sy1 = max(x0, tx0), max(y0, ty0), min(x0 + w, tx0 + tw), min(y0 + h, ty0 + th)
            canvas[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = tile[sy0 - ty0:sy1 - ty0, sx0 - tx0:sx1 - tx0]
    return canvas


def thumbnail(tiles_dir: Path, town: str, tiles, cols: int, rows: int, tw: int, th: int, factor: int, width: int):
    """The whole picture reduced by `factor` (a mean of factor x factor pixels per tile) and scaled to `width`."""
    small_w, small_h = max(tw // factor, 1), max(th // factor, 1)
    canvas = np.zeros((rows * small_h, cols * small_w, 3), np.uint8)
    for index in tiles:
        path = tiles_dir / f"{town}{index:06d}.png"
        if path.exists():
            r, c = divmod(index, cols)
            canvas[r * small_h:(r + 1) * small_h, c * small_w:(c + 1) * small_w] = np.asarray(Image.open(path).convert("RGB").resize((small_w, small_h), Image.BOX))
    image = Image.fromarray(canvas)
    return image.resize((width, max(int(image.height * width / image.width), 1)), Image.BOX)


def label_bar(image, text, height=34):
    from PIL import ImageDraw, ImageFont
    bar = Image.new("RGB", (image.width, height), (24, 24, 28))
    try:
        font = ImageFont.load_default(size=18)
    except TypeError:
        font = ImageFont.load_default()
    ImageDraw.Draw(bar).text((10, 7), text, fill=(235, 235, 235), font=font)
    out = Image.new("RGB", (image.width, image.height + height))
    out.paste(bar, (0, 0)); out.paste(image, (0, height))
    return out


def cmd_composite(args):
    """One labelled picture of a town: the whole render (a thumbnail with the crop box), then the same crop of the original tiles (0.25, enlarged 4 x by nearest) and of our render (1.0).
    The crop is --crop X,Y,W,H in picture pixels at scale 1.0, else the worst 64 x 64 window of the check (render.json) widened to 1024 x 512. All labels are English."""
    from PIL import ImageDraw
    out = Path(args.out)
    plan = json.loads((out / "plan.json").read_text())
    report = json.loads((out / "render.json").read_text())
    tmi = plan["tmi"]; cols, rows = tmi["columns"], tmi["rows"]
    tiles = set(plan["tiles"])
    ours_dir = out / "tiles-1.0"
    original = Path(args.original)
    cw, ch = 1024, 512
    if args.crop:
        x, y, cw, ch = [int(v) for v in args.crop.split(",")]
    else:
        worst = (report.get("against_original_0.25", {}).get("worst_64x64_windows") or [{"x4": 0, "y4": 0}])[0]
        x = int(worst["x4"] * 4 + 128 - cw / 2); y = int(worst["y4"] * 4 + 128 - ch / 2)
    x = int(min(max(x, 0), max(tmi["width"] - cw, 0))); y = int(min(max(y, 0), max(tmi["height"] - ch, 0)))
    ours = mosaic_region(ours_dir, plan["town"], tiles, cols, TILE_W, TILE_H, x, y, cw, ch)
    ox0, oy0 = x // 4, y // 4
    orig = mosaic_region(original, plan["town"], tiles, cols, TILE_W // 4, TILE_H // 4, ox0, oy0, cw // 4, ch // 4)
    orig = Image.fromarray(orig).resize((cw // 4 * 4, ch // 4 * 4), Image.NEAREST)
    panels = [label_bar(orig, f"Original tiles (scale 0.25, enlarged 4x), crop {x},{y} {cw}x{ch}"), label_bar(Image.fromarray(ours), "Steamwoven render (scale 1.0), same crop")]
    width = panels[0].width + panels[1].width + 30
    factor = 16
    thumb = thumbnail(ours_dir, plan["town"], tiles, cols, rows, TILE_W, TILE_H, factor, min(width, 1600))
    scale = thumb.width / (cols * TILE_W)
    ImageDraw.Draw(thumb).rectangle([x * scale, y * scale, (x + cw) * scale, (y + ch) * scale], outline=(255, 64, 64), width=3)
    thumb = label_bar(thumb, f"{plan['town']}: whole render (scale 1.0, reduced), crop box in red; mean error {report.get('against_original_0.25', {}).get('mean_error', 'n/a')}, worst 64x64 window "
                             f"{(report.get('against_original_0.25', {}).get('worst_64x64_windows') or [{}])[0].get('mean_error', 'n/a')}")
    sheet = Image.new("RGB", (max(width, thumb.width), thumb.height + max(p.height for p in panels) + 20), (60, 60, 66))
    sheet.paste(thumb, (0, 0)); sheet.paste(panels[0], (0, thumb.height + 10)); sheet.paste(panels[1], (panels[0].width + 30, thumb.height + 10))
    Path(args.dest).parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.dest)
    print(f"composite {args.dest} {sheet.width}x{sheet.height}")


def cmd_report(args):
    """The markdown table of the all-towns note: the plan table of the modules joined with the results of the full run (empty cells where a town was not run)."""
    results = {(r["module"], r["number"]): r for r in read_tsv(Path(args.results))} if args.results else {}
    lines = ["| Module | # | Town map | Game map | Opens as | Tiles | Windows / shots | Capture s | Stitch s | MB | Mean error | Worst 64 x 64 | Result |",
             "| --- | ---: | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |"]
    for table in args.towns:
        for t in read_tsv(Path(table)):
            r = results.get((t["module"], t["number"]), {})
            mb = f"{int(r['bytes']) / 1e6:.1f}" if r.get("bytes") else ""
            plan = f"{t['windows']} / {t['shots']}" if t.get("windows") else ""
            result = r.get("result", "") or (f"{t['status']}: {t['note']}" if t["note"] else t["status"])
            if r.get("classification"):
                result += f": {r['classification']}"
            lines.append(f"| {t['module']} | {t['number']} | {t['name']} | `{t.get('map', '')}` | {t.get('kind', '')} | {t['tiles']} | {plan} | {r.get('capture_s', '')} | {r.get('stitch_s', '')} | {mb} | "
                         f"{r.get('mean_error', '')} | {r.get('worst_window', '')} | {result} |")
    Path(args.dest).write_text("\n".join(lines) + "\n")
    print(f"{len(lines) - 2} rows -> {args.dest}")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    cost = sub.add_parser("cost")
    cost.add_argument("--index", required=True)
    towns = sub.add_parser("towns", help="the plan table of every town map of a scan folder")
    towns.add_argument("--scan", required=True)
    towns.add_argument("--module", default="Arcanum")
    towns.add_argument("--out", required=True)
    comp = sub.add_parser("composite", help="a labelled picture of a rendered town: the whole render and one crop against the original tiles")
    comp.add_argument("--out", required=True, help="the town's working folder (plan.json, render.json, tiles-1.0)")
    comp.add_argument("--original", required=True, help="folder of the town's original tile PNGs")
    comp.add_argument("--dest", required=True)
    comp.add_argument("--crop", help="X,Y,W,H at scale 1.0 (default: the worst 64 x 64 window)")
    report = sub.add_parser("report", help="the markdown table of the plan tables joined with the results of a full run")
    report.add_argument("--towns", nargs="+", required=True)
    report.add_argument("--results")
    report.add_argument("--dest", required=True)
    every = sub.add_parser("all", help="plan, capture and stitch every town marked `render` of a table")
    every.add_argument("--towns", required=True)
    every.add_argument("--scan", required=True)
    every.add_argument("--originals", required=True, help="folder of the original tile PNGs: MODULE/TOWN/TOWNNNNNNN.png")
    every.add_argument("--out", required=True)
    every.add_argument("--only", help="comma list of town numbers")
    every.add_argument("--redo", action="store_true")
    every.add_argument("--stitch-only", action="store_true", help="stitch the captures of an earlier run again (no plan, no capture); with --redo")
    for name, fn in (("plan", cmd_plan), ("capture", cmd_capture), ("stitch", cmd_stitch), ("run", None)):
        p = sub.add_parser(name)
        p.add_argument("--out", required=True, help="working folder (under local/)")
        if name in ("plan", "run"):
            p.add_argument("--town", required=True)
            p.add_argument("--map", default=MAIN_MAP, help="the map folder the town is drawn from (a map other than the main map loads whole and takes no sector)")
            p.add_argument("--scan", help="a scan folder (the `scan` command): the .tmi record and tile list come from it, no info run")
            p.add_argument("--info-sector", default=(504, 976), type=lambda s: tuple(int(v) for v in s.split(",")), help="any sector of the main map (the info run)")
        if name in ("capture", "run"):
            p.add_argument("--force", action="store_true")
        if name in ("stitch", "run"):
            p.add_argument("--scale", default="1.0", choices=["1.0", "0.5", "0.25"])
            p.add_argument("--original", help="folder of the original tile PNGs (<Town>NNNNNN.png) for the 0.25 check")
    args = parser.parse_args(argv)
    if args.command == "cost":
        cmd_cost(args)
    elif args.command == "towns":
        cmd_towns(args)
    elif args.command == "all":
        cmd_all(args)
    elif args.command == "composite":
        cmd_composite(args)
    elif args.command == "report":
        cmd_report(args)
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
