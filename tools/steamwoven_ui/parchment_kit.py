#!/usr/bin/env python3
"""ARC-UI-3205 (batch B-BUTTONS, slice 5, part one): the three OVAL BOXES of the inventory, loot and barter windows (Drop 224, Use 342, Gamble 345; 80 x 43 units, one frame each) with their lit (ON 225, 343, 344) and refused
(ILL 348, 347, 346) states, as kit parts by the owner's PACK-FIRST rule (2026-11-25, TRD SWI-KIT-5 "Pack-first"): when the old generated HD pack (`local/hd-packs/interface-redraw-4x`, ARC-RES-095) has a good frame of a part, the part is that
frame with an analytic alpha; generation (Codex, rule E, rule F) is only for parts the pack lacks. The pack's frames of these nine arts are good (a painted hand, dice with their dots, the brass ring, green and red corners): nothing is generated.

    python3 -B tools/steamwoven_ui/parchment_kit.py build [--out DIR] [--only NAME,...] [--dry-run] [--local-root DIR]    (the nine parts -> masters/ and provenance/rule/)
    python3 -B tools/steamwoven_ui/parchment_kit.py sheet --out FILE [--zoom N] [--local-root DIR]                       (original | the new part on a wood stand-in, English labels; needs ImageMagick)
    python3 -B tools/steamwoven_ui/parchment_kit.py self-test

The parts (TRD SWI-KIT-5; all at the face's size, 288 x 156 px, the 80 x 43 unit frame at 3.6 px per unit snapped to the 6 px grid):

* `plate.<name>` the idle face: the pack's idle frame, resampled (Lanczos) to the master, with the alpha below.
* `state.<on_name>`, `state.<ill_name>` the lit and the refused face: the pack's frames of those states, the same way (the green and the red corners are kept).

The alpha is analytic (no stair of old pixels, no old background): the union of the ring's ellipse and the four corner triangles. The old frame is a rectangle: between the gold ring and the four dark corners it holds old WOOD, which on the new
wood of the window surface would be a patch (owner decision, #3101). The ring's ellipse has semi-axes `RING` px; the corners are the rectangle minus a larger ellipse (`CORNER`: the ellipse fitted on the original to the inner edges of its four dark corners, ARC-UI-3290: 160 x 98 against the ring's 141 x 78
on the 2880 px master, here scaled to the 288 px frame); both edges are anti-aliased by `SAMPLES` x `SAMPLES` samples per pixel, the rectangle's own edges stay hard. The soft shadow of the oval on the wood is the surface's (`surface_compose.py` `disc_boxes`, #3249), not the part's.
Nothing here holds art: every picture is read from and written to the ignored `local/`. Standard library only (`pngio.py`); ImageMagick for the resampling and `sheet`.
Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import pngio  # type: ignore
    import prompt_library  # type: ignore

TOOL_VERSION = 2
UNITS = (80, 43)                 # the original frame, in units
MASTER = (288, 156)              # 80 x 3.6 = 288, 43 x 3.6 = 154.8 snapped to the 6 px grid (kit.json)
CENTRE = (142.5, 78.0)           # the ring's and the corner ellipse's common centre in master px (the pack's ring is a pixel and a half left of the frame's middle, as the original's: ring box 2532 to 2814 in a plate 2531 to 2817)
RING = (141.5, 78.5)             # the ring's ellipse: semi-axes in master px, measured on the pack frame's brass (141 x 78 on the 2880 px master of ARC-UI-3290) with its dark outline
CORNER = (160.0, 98.0)           # the corner ellipse: semi-axes in master px (160 x 98 on the 2880 px master, ARC-UI-3290); the corners are the rectangle outside it
SAMPLES = 4                      # anti-aliasing samples per pixel side
PACK_SIZE = (320, 172)           # the pack's frames, 4 x the original's units
PACK_DIR = ("hd-packs", "interface-redraw-4x")
PACK_FOLDERS = ("interface/inventory/buttons", "interface/inventory/panels")
LABEL_FONT = "/System/Library/Fonts/Supplemental/Arial.ttf"  # the labels of a composite are English (project rule)
STAND_IN = (62, 44, 30)          # the wood stand-in of a composite

Image = pngio.Image


class ParchmentError(Exception):
    pass


@dataclass(frozen=True)
class Box:
    art: int
    folder: str
    name: str          # `plate.<name>`; the pack frame is `<name>-f000.png`
    on_art: int
    on_folder: str
    on_name: str       # `state.<on_name>`
    ill_art: int
    ill_folder: str
    ill_name: str      # `state.<ill_name>`


BOXES = (
    Box(224, "224-Drop_OFF", "drop_off", 225, "225-Drop_ON", "drop_on", 348, "348-Drop_ILL", "drop_ill"),
    Box(342, "342-Use_Box_OFF", "use_box_off", 343, "343-Use_Box_ON", "use_box_on", 347, "347-Use_Box_ILL", "use_box_ill"),
    Box(345, "345-Gamble_Box_OFF", "gamble_box_off", 344, "344-Gamble_Box_ON", "gamble_box_on", 346, "346-Gamble_Box_ILL", "gamble_box_ill"),
)


def part_ids(box: Box) -> list[str]:
    """The three kit parts of a box by id: the idle face, the lit face, the refused face."""
    return [f"plate.{box.name}", f"state.{box.on_name}", f"state.{box.ill_name}"]


def pack_name(ident: str) -> str:
    """The pack frame's art name of a part id (`plate.use_box_off` -> `use_box_off`)."""
    return ident.split(".", 1)[1]


def all_part_ids() -> list[str]:
    return [i for b in BOXES for i in part_ids(b)]


# --- the analytic alpha --------------------------------------------------------------------------------------------------

def coverage(x: int, y: int, size: tuple[int, int] = MASTER) -> float:
    """The share of pixel (x, y) that is part: inside the ring's ellipse, or outside the corner ellipse (the four triangles of the rectangle), by SAMPLES x SAMPLES samples. The ellipses are concentric with the frame."""
    w, h = size
    cx, cy = CENTRE[0] * w / MASTER[0], CENTRE[1] * h / MASTER[1]
    ra, rb = RING[0] * w / MASTER[0], RING[1] * h / MASTER[1]
    ca, cb = CORNER[0] * w / MASTER[0], CORNER[1] * h / MASTER[1]
    hit = 0
    for j in range(SAMPLES):
        for i in range(SAMPLES):
            px, py = x + (i + 0.5) / SAMPLES - cx, y + (j + 0.5) / SAMPLES - cy
            inside_ring = (px / ra) ** 2 + (py / rb) ** 2 <= 1.0
            outside_corner = (px / ca) ** 2 + (py / cb) ** 2 > 1.0
            hit += inside_ring or outside_corner
    return hit / (SAMPLES * SAMPLES)


def alpha_plane(size: tuple[int, int] = MASTER) -> list[float]:
    return [coverage(x, y, size) for y in range(size[1]) for x in range(size[0])]


# --- the parts ------------------------------------------------------------------------------------------------------------

def pack_root(local: Path) -> Path:
    return local.parent / PACK_DIR[0] / PACK_DIR[1]


def pack_frame(local: Path, name: str) -> Path:
    """The pack's frame of an art name, or an error naming where it was looked for (every state of every box must be there)."""
    root = pack_root(local)
    for folder in PACK_FOLDERS:
        path = root / folder / f"{name}-f000.png"
        if path.is_file():
            return path
    raise ParchmentError(f"the HD pack has no frame {name}-f000.png under {root} ({', '.join(PACK_FOLDERS)})")


def need_magick() -> None:
    if shutil.which("magick") is None:
        raise ParchmentError("ImageMagick (`magick`) is needed for this command")


def resampled(path: Path, work: Path) -> Image:
    """A pack frame at the master size (Lanczos); a frame of another size than the pack's is refused."""
    source = pngio.read(path)
    if (source.w, source.h) != PACK_SIZE:
        raise ParchmentError(f"{path}: {source.w} x {source.h}, a pack frame of a box is {PACK_SIZE[0]} x {PACK_SIZE[1]}")
    need_magick()
    out = work / f"{path.stem}-master.png"
    subprocess.run(["magick", str(path), "-filter", "Lanczos", "-resize", f"{MASTER[0]}x{MASTER[1]}!", f"PNG32:{out}"], check=True)
    return pngio.read(out)


def with_alpha(frame: Image, plane: list[float]) -> Image:
    """The frame with the analytic alpha (the frame's own alpha, opaque in the pack, times the coverage); the colour of a clear pixel is stored as 0 0 0 0."""
    out = Image(frame.w, frame.h, bytearray(frame.data))
    for i, c in enumerate(plane):
        o = i * 4
        a = int(round(frame.data[o + 3] * c))
        out.data[o:o + 4] = bytes((frame.data[o], frame.data[o + 1], frame.data[o + 2], a)) if a else bytes(4)
    return out


def build_parts(local: Path, only: set[str] | None = None) -> dict[str, Image]:
    """Every part by id (a box's three, or those of `only`), each its pack frame at the master with the analytic alpha."""
    plane = alpha_plane()
    parts: dict[str, Image] = {}
    with tempfile.TemporaryDirectory() as tmp:
        for box in BOXES:
            if only and box.name not in only:
                continue
            for ident in part_ids(box):
                parts[ident] = with_alpha(resampled(pack_frame(local, pack_name(ident)), Path(tmp)), plane)
    return parts


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def owner_of(ident: str) -> Box:
    for box in BOXES:
        if ident in part_ids(box):
            return box
    raise ParchmentError(f"{ident} belongs to no box of this slice")


def record_of(ident: str, local: Path, image_path: Path) -> dict:
    """The provenance record of a rule-made part (SWI-GEN-6): no generator, the rule and the hash of the pack frame it took."""
    source = pack_frame(local, pack_name(ident))
    return {"part": ident, "rule": "parchment_kit", "tool_version": TOOL_VERSION, "method": "pack-first", "date": datetime.date.today().isoformat(),
            "sources": {str(source.relative_to(local.parent)): sha256_file(source)},
            "parameters": {"centre": list(CENTRE), "ring": list(RING), "corner": list(CORNER), "samples": SAMPLES, "master": list(MASTER), "pack_size": list(PACK_SIZE)},
            "image_sha256": sha256_file(image_path), "image_size": list(MASTER), "run_by": "agent"}


# --- commands --------------------------------------------------------------------------------------------------------------

def cmd_build(args: argparse.Namespace) -> int:
    local = args.local_root
    out = args.out or local / "masters"
    records = local / "provenance" / "rule" if args.out is None else out / "records"
    only = set(args.only.split(",")) if args.only else None
    if only and not only <= {b.name for b in BOXES}:
        raise ParchmentError(f"--only names unknown boxes {sorted(only - {b.name for b in BOXES})}; the boxes are {[b.name for b in BOXES]}")
    parts = build_parts(local, only)
    for ident, image in parts.items():
        covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
        print(f"{ident:24s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    if args.dry_run:
        print(f"PARCHMENT KIT build DRY-RUN PASS ({len(parts)} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ident, image in parts.items():
        path = out / f"{ident}.png"
        pngio.write(path, image)
        (records / f"{ident}.json").write_text(json.dumps(record_of(ident, local, path), indent=2) + "\n", encoding="utf-8")
    print(f"PARCHMENT KIT build PASS ({len(parts)} parts written to {out})")
    return 0


def upscale(frame: Image) -> Image:
    """An original frame at the master size, nearest (the 'original' column of a comparison)."""
    out = Image(MASTER[0], MASTER[1], bytearray(MASTER[0] * MASTER[1] * 4))
    for y in range(MASTER[1]):
        for x in range(MASTER[0]):
            sx, sy = x * frame.w // MASTER[0], y * frame.h // MASTER[1]
            out.data[(y * MASTER[0] + x) * 4:(y * MASTER[0] + x) * 4 + 4] = bytes(frame.data[(sy * frame.w + sx) * 4:(sy * frame.w + sx) * 4 + 3]) + b"\xff"
    return out


def over_stand_in(part: Image) -> Image:
    out = Image(part.w, part.h, bytearray(part.w * part.h * 4))
    for i in range(0, len(part.data), 4):
        a = part.data[i + 3] / 255.0
        out.data[i:i + 4] = bytes([int(round(part.data[i + k] * a + STAND_IN[k] * (1.0 - a))) for k in range(3)] + [255])
    return out


def cmd_sheet(args: argparse.Namespace) -> int:
    """Per box and state: the original frame | the new part on a wood stand-in (English labels)."""
    need_magick()
    local = args.local_root
    frames = local / "census" / "frames"
    rows = []
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        parts = build_parts(local)
        for box in BOXES:
            tiles = []
            for ident, folder, label in ((part_ids(box)[0], box.folder, "idle"), (part_ids(box)[1], box.on_folder, "lit"), (part_ids(box)[2], box.ill_folder, "refused")):
                for kind, image in (("original", upscale(pngio.read(frames / folder / "f0.png"))), ("new part", over_stand_in(parts[ident]))):
                    raw, tile = work / f"{ident}-{kind}.png", work / f"{ident}-{kind}-l.png"
                    pngio.write(raw, image)
                    subprocess.run(["magick", str(raw), "-filter", "point", "-resize", f"{args.zoom * 100}%", "-background", "#202020", "-fill", "white", "-font", LABEL_FONT, "-pointsize", "18",
                                    "-gravity", "North", "-splice", "0x28", "-annotate", "+0+4", f"{kind}, {label}", str(tile)], check=True)
                    tiles.append(str(tile))
            row = work / f"{box.name}-row.png"
            subprocess.run(["magick", "montage", *tiles, "-tile", "6x1", "-geometry", "+4+4", "-background", "#202020", str(row)], check=True)
            rows.append(str(row))
        args.out.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["magick", *rows, "-background", "#202020", "-append", str(args.out)], check=True)
    print(f"PARCHMENT KIT sheet written to {args.out}")
    return 0


def self_test() -> int:
    """Checks that need no art: the analytic alpha's geometry and the part table."""
    w, h = MASTER
    assert coverage(0, 0) == 1.0, "the very corner of the rectangle is a corner triangle"
    assert coverage(w // 2, h // 2) == 1.0, "the middle of the ring is part"
    assert coverage(w // 2, 0) == 1.0 and coverage(w // 2, h - 1) == 1.0, "the ring reaches the middle of the top and the bottom"
    assert coverage(w - 1, h // 2) == 0.0, "the ring stops short of the middle of a side: old wood there is clear"
    assert len(all_part_ids()) == 9 and len(set(all_part_ids())) == 9
    print("PARCHMENT KIT self-test PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--local-root", type=Path, default=None)
        if name == "sheet":
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=2)
        else:
            s.add_argument("--out", type=Path, default=None)
            s.add_argument("--only", default=None)
            s.add_argument("--dry-run", action="store_true")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (ParchmentError, pngio.PngError, OSError, subprocess.CalledProcessError) as error:
        print(f"PARCHMENT KIT FAIL: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
