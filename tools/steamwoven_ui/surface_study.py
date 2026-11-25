"""ARC-RES-2457: the Codex inputs and the overlap blend of the surface method study (the inventory composition, 2880 x 2160, in the three aspect zones and the
overlapping tiles of the window zone). Needs ImageMagick (`magick`). Local files only: the inboxes and outboxes are under `local/steamwoven-ui/`, never in git.

  zones-inbox  batch `surface-aspect` (method A at the extreme aspects 19.5:1, 5:1, 2:1: the zone as ONE image to edit)
  tiles-inbox  batch `surface-tiles` (method B: overlapping 4:3 tiles of the window zone, each one image to edit)
  blend        the returned tiles resized to their rectangles and blended over the original with linear ramps in the overlaps (painter's order)
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
COMMON = HERE / "prompts" / "common.txt"
MASTER = (2880, 2160)
# the zones of the inventory composition (master pixels): top strip 41 units, window 400 units, bottom strip 159 units, at 3.6 px per unit
ZONES = {"top-strip": (0, 0, 2880, 148, "the top strip (800 x 41 units)"),
         "window": (0, 148, 2880, 1440, "the inventory window zone (800 x 400 units)"),
         "bottom-strip": (0, 1588, 2880, 572, "the bottom strip (800 x 159 units)")}
WINDOW_ORIGIN_Y = 148
TILE_W, TILE_H = 1560, 1080          # 4:3, about 1.7 MP (the generator keeps about 1.57 MP and returns smaller; the result is resized to the rectangle)
TILE_COLUMNS, TILE_ROWS = 2, 2
ZONE_BATCH, TILE_BATCH = "surface-aspect", "surface-tiles"
ZONE_BODY = """SURFACE STUDY - WHOLE PIECE AT AN EXTREME ASPECT
Size: {w} x {h} (aspect {aspect}:1). References: ORIGINAL ({what} of the {screen_name} screen, upscaled to 2880 px wide: this is the IMAGE TO EDIT; refine this image, raise its detail, do not change its colour or its shape).

Refine the whole piece in one image at exactly this aspect, {w}:{h}. The picture fills the whole frame edge to edge: do not letterbox it, do not pad it, do not add bars, borders or margins, do not crop it and do not change its aspect. Keep every element where it is in the ORIGINAL, the same wood tone, grain scale and streaks, the same brass and iron. Add no ornament, no shine and no new detail; only resolution and surface texture.
Goal: a window or strip background made whole at the master width, to be compared with other methods by seam, repetition and sharpness.

REFERENCE SLOTS FOR THIS PROMPT
- ORIGINAL: the original art of the piece, upscaled to the master width, given as the image to edit and as the layout truth
"""
TILE_BODY = """SURFACE STUDY - ONE OVERLAPPING TILE OF A WINDOW BACKGROUND
Size: {w} x {h} (aspect 4:3). References: ORIGINAL (a {w} x {h} px part of the inventory window zone, upscaled; this is the IMAGE TO EDIT; refine this image, raise its detail, do not change its colour or its shape).

Refine this part in one image at exactly this aspect. The picture fills the whole frame edge to edge: do not pad it, do not add bars, borders or margins, do not crop it and do not change its aspect. It is one piece of a larger picture that is refined in overlapping parts and blended, so keep every element exactly where it is in the ORIGINAL, including the elements cut by the picture edges, and keep the same wood tone, grain scale and streaks, the same brass and iron. Add no ornament, no shine and no new detail; only resolution and surface texture.
Goal: the parts, blended, are compared with other methods by seam, repetition and sharpness.

REFERENCE SLOTS FOR THIS PROMPT
- ORIGINAL: the original art of the part, upscaled to the master size, given as the image to edit and as the layout truth
"""


SAFE_NAME = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]*")   # a batch name is a folder name: no path parts, no leading dot


class StudyError(Exception):
    pass


def run(command: list[str]) -> None:
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise StudyError(f"{command[0]} failed: {result.stderr.strip()[:300]}")


def default_local_root() -> Path:
    sys.path.insert(0, str(HERE))
    from prompt_library import default_local_root as root  # noqa: PLC0415
    return root()


def tile_rects() -> list[dict]:
    """Rectangles (x, y, w, h) in window-zone pixels in painter's order (row by row); each tile's `left` and `top` are the overlaps with the earlier tiles."""
    window_w, window_h = ZONES["window"][2], ZONES["window"][3]
    xs = [round(i * (window_w - TILE_W) / (TILE_COLUMNS - 1)) for i in range(TILE_COLUMNS)]
    ys = [round(i * (window_h - TILE_H) / (TILE_ROWS - 1)) for i in range(TILE_ROWS)]
    rects = []
    for r, y in enumerate(ys):
        for c, x in enumerate(xs):
            left = xs[c - 1] + TILE_W - x if c else 0
            top = ys[r - 1] + TILE_H - y if r else 0
            rects.append({"id": f"tile-r{r}c{c}", "x": x, "y": y, "w": TILE_W, "h": TILE_H, "left": left, "top": top})
    return rects


def ramp_mask_command(rect: dict, out: Path) -> list[str]:
    """A mask of the tile's size: white, with a linear 0 -> 1 ramp over the overlap with the earlier tiles on the left and on the top."""
    expr = "1"
    if rect["left"]:
        expr = f"min({expr},(i+0.5)/{rect['left']})"
    if rect["top"]:
        expr = f"min({expr},(j+0.5)/{rect['top']})"
    return ["magick", "-size", f"{rect['w']}x{rect['h']}", "xc:black", "-colorspace", "Gray", "-fx", expr, str(out)]


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_batch(inbox: Path, batch: str, items: list[dict], screen_name: str = "inventory") -> None:
    (inbox / "batch.json").write_text(json.dumps({"batch": batch, "after": f"the upscaled ORIGINAL of the {screen_name} screen", "items": items}, indent=2) + "\n", encoding="utf-8")


def add_item(inbox: Path, batch: str, original: Path, item_id: str, title: str, body: str, rect: tuple[int, int, int, int]) -> dict:
    x, y, w, h = rect
    text = COMMON.read_text(encoding="utf-8").rstrip("\n") + "\n\n" + body
    text.encode("ascii")
    (inbox / "prompts").mkdir(parents=True, exist_ok=True)
    (inbox / "prompts" / f"{item_id}.txt").write_text(text, encoding="utf-8")
    ref = inbox / "refs" / item_id / "original.png"
    ref.parent.mkdir(parents=True, exist_ok=True)
    run(["magick", str(original), "-crop", f"{w}x{h}+{x}+{y}", "+repage", "-alpha", "off", str(ref)])
    return {"id": item_id, "title": title, "prompt_file": f"prompts/{item_id}.txt", "prompt_sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
            "size": [w, h], "references": [{"slot": "original", "file": f"refs/{item_id}/original.png", "sha256": sha256_file(ref)}], "outbox_file": f"outbox/{batch}/{item_id}.png"}


def zones_inbox(original: Path, local_root: Path, batch: str = ZONE_BATCH, zones: tuple[str, ...] | None = None, screen_name: str = "inventory") -> dict:
    """The inbox of method A zones (ARC-UI-3100: for any screen, any subset of the zones: the strips are made once from the HUD screen, a window zone per family)."""
    if not SAFE_NAME.fullmatch(batch):
        raise StudyError(f"the batch name {batch!r} must be a plain folder name ({SAFE_NAME.pattern})")
    if not (screen_name.isascii() and SAFE_NAME.fullmatch(screen_name)):
        raise StudyError(f"the screen name {screen_name!r} must be ASCII letters, digits, dots, dashes and underscores (it goes into the prompt text)")
    unknown = [z for z in (zones or ()) if z not in ZONES]
    if unknown:
        raise StudyError(f"unknown zones {unknown}; the zones are {list(ZONES)}")
    inbox = local_root / "inbox" / batch
    items = []
    for zone, (x, y, w, h, what) in ZONES.items():
        if zones and zone not in zones:
            continue
        body = ZONE_BODY.format(w=w, h=h, aspect=f"{w / h:.1f}", what=what.replace("inventory", screen_name), screen_name=screen_name)
        items.append(add_item(inbox, batch, original, zone, f"Method A: {zone} of the {screen_name} screen as one image ({w / h:.1f}:1)", body, (x, y, w, h)))
    write_batch(inbox, batch, items, screen_name)
    return {"inbox": str(inbox), "items": len(items)}


def assemble(zone_files: dict[str, Path], out: Path) -> dict:
    """Method A assembled: each zone image resized (Lanczos) to its rectangle and placed in the 2880 x 2160 composition; every zone is needed (the strips of one
    screen's A are the same in every family, so the files may come from different batches)."""
    extra = [z for z in zone_files if z not in ZONES]
    if extra:
        raise StudyError(f"unknown zones {extra}; the zones are {list(ZONES)}")
    missing = [z for z in ZONES if z not in zone_files]
    if missing:
        raise StudyError(f"zones missing: {missing}")
    command = ["magick", "-size", f"{MASTER[0]}x{MASTER[1]}", "xc:black"]
    for zone, (x, y, w, h, _) in ZONES.items():
        command += ["(", str(zone_files[zone]), "-alpha", "off", "-filter", "Lanczos", "-resize", f"{w}x{h}!", ")", "-geometry", f"+{x}+{y}", "-composite"]
    run(command + ["-alpha", "off", "-depth", "8", str(out)])  # xc:black is Q16: without -depth 8 the PNG is 16 bit and surface_compose refuses it
    return {"out": str(out), "zones": len(ZONES)}


def tiles_inbox(original: Path, local_root: Path) -> dict:
    inbox = local_root / "inbox" / TILE_BATCH
    items = []
    for rect in tile_rects():
        body = TILE_BODY.format(w=rect["w"], h=rect["h"])
        items.append(add_item(inbox, TILE_BATCH, original, rect["id"], f"Method B: window tile {rect['id']}", body, (rect["x"], rect["y"] + WINDOW_ORIGIN_Y, rect["w"], rect["h"])))
    write_batch(inbox, TILE_BATCH, items)
    return {"inbox": str(inbox), "items": len(items)}


def blend(original: Path, results: Path, out: Path, work: Path) -> dict:
    """The returned tiles, resized to their rectangles, laid over the original window zone with ramp masks, written as the whole composition (zones outside
    the window stay the original)."""
    work.mkdir(parents=True, exist_ok=True)
    window = work / "window.png"
    x0, y0, w0, h0, _ = ZONES["window"]
    run(["magick", str(original), "-crop", f"{w0}x{h0}+{x0}+{y0}", "+repage", "-alpha", "off", str(window)])
    for rect in tile_rects():
        tile = results / f"{rect['id']}.png"
        if not tile.is_file():
            raise StudyError(f"missing tile {tile}")
        mask, resized = work / f"{rect['id']}-mask.png", work / f"{rect['id']}-resized.png"
        run(ramp_mask_command(rect, mask))
        run(["magick", str(tile), "-alpha", "off", "-filter", "Lanczos", "-resize", f"{rect['w']}x{rect['h']}!", str(resized)])
        run(["magick", str(window), "(", str(resized), str(mask), "-alpha", "off", "-compose", "CopyOpacity", "-composite", ")", "-geometry", f"+{rect['x']}+{rect['y']}", "-compose", "Over", "-composite", str(window)])
    run(["magick", str(original), "-alpha", "off", str(window), "-geometry", f"+{x0}+{y0}", "-composite", str(out)])
    return {"out": str(out), "tiles": len(tile_rects())}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("zones-inbox", "tiles-inbox", "blend", "assemble"):
        p = sub.add_parser(name)
        if name == "assemble":
            p.add_argument("--zone", action="append", required=True, metavar="ZONE=FILE", help=f"a zone result ({', '.join(ZONES)}); all three are needed")
            p.add_argument("--out", type=Path, required=True)
            continue
        p.add_argument("--original", type=Path, required=True, help="the ORIGINAL upscaled to 2880 x 2160")
        p.add_argument("--local-root", type=Path)
        if name == "zones-inbox":
            p.add_argument("--batch", default=ZONE_BATCH, help="the batch (inbox folder) name")
            p.add_argument("--zones", help="comma-separated subset of the zones (default all three)")
            p.add_argument("--screen-name", default="inventory", help="the screen's name in the prompt text (ASCII)")
        if name == "blend":
            p.add_argument("--results", type=Path, help=f"default local/steamwoven-ui/outbox/{TILE_BATCH}")
            p.add_argument("--out", type=Path, required=True)
            p.add_argument("--work", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        if args.command == "assemble":
            pairs = [item.partition("=") for item in args.zone]
            if any(not sep or not file for _, sep, file in pairs):
                raise StudyError("each --zone is ZONE=FILE")
            info = assemble({zone: Path(file) for zone, _, file in pairs}, args.out)
            print(f"ASSEMBLE wrote {info['out']} from {info['zones']} zones")
            return 0
        local = args.local_root or default_local_root()
        size = subprocess.run(["magick", "identify", "-format", "%wx%h", str(args.original)], capture_output=True, text=True).stdout.strip()
        if size != f"{MASTER[0]}x{MASTER[1]}":
            raise StudyError(f"the original is {size or 'unreadable'}, {MASTER[0]}x{MASTER[1]} is needed")
        if args.command == "blend":
            info = blend(args.original, args.results or local / "outbox" / TILE_BATCH, args.out, args.work)
            print(f"BLEND wrote {info['out']} from {info['tiles']} tiles")
        elif args.command == "zones-inbox":
            info = zones_inbox(args.original, local, args.batch, tuple(args.zones.split(",")) if args.zones else None, args.screen_name)
            print(f"INBOX {info['inbox']}: {info['items']} items")
        else:
            info = tiles_inbox(args.original, local)
            print(f"INBOX {info['inbox']}: {info['items']} items")
    except (StudyError, OSError, KeyError) as error:
        print(f"SURFACE STUDY ERROR: {error!r}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
