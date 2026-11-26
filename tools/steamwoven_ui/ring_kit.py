#!/usr/bin/env python3
"""ARC-UI-3270 (batch B-BUTTONS, slice 3c): the small round buttons in a thin brass ring (16 arts, 19 x 19 to 28 x 29 units): the character sheet's plus and minus (two sizes), the message window's four buttons, the multiplayer
cycle arrows and the small right arrow, the three inventory "lil" buttons and the small accept and cancel.

Pack-first (owner rule P, TRD "Asset kit"; comment of 2026-11-25 on the issue): every part is the player's own HD-pack frame of that state AS IT IS (ring, dome and symbol), cut to the circle of the ring by an ANALYTIC round alpha. The
pack's own alpha is not used (its edge is a staircase and keeps the original's baked shadow): the pack's transparent pixels are first given the colour next to them, then the frame is cut as `school_kit.py` cuts the college buttons.
No shadow is baked: the shared soft round shadow is the run-time layer's (#3249, #3288). No Codex item, no rule-drawn glyph.

    python3 -B tools/steamwoven_ui/ring_kit.py build [--only NAME,...] [--out DIR] [--dry-run]   (the parts -> masters/ and provenance/rule/)
    python3 -B tools/steamwoven_ui/ring_kit.py check [--only NAME,...]                           (every state has its pack frame at the art's aspect; the circles lie in the arts)
    python3 -B tools/steamwoven_ui/ring_kit.py sheet --out FILE [--only NAME,...] [--zoom N]     (original | pack | kit part, without and with the run-time shadow; English labels)
    python3 -B tools/steamwoven_ui/ring_kit.py self-test

The parts of a button (TRD SWI-KIT-5; all at the plate's size, the manifest row `plate.<name>`):

* `plate.<name>` the pack's idle frame (f000) cut to the circle; `glyph.<name>` EMPTY (the symbol is in the frame).
* `plate.<name>_pressed` the pack's frame f001, `plate.<name>_hover` the frame f002 (arts of two frames have no hover look: the original wraps hover to frame 0). Frame 3 of the four plus and minus arts (the empty plate) is never
  drawn by the original (census, ledger OF9), so it has no part.

The circles are this tool's own table (`RINGS`: centre and outer radius of the brass ring in art units, plus `TRIM` for the cut), measured on the pack's frames; the viewer does not draw these arts from the kit yet, so its table
`AreaKitRoundParts` has no row for them (the slice that wires them takes these numbers).

Nothing here holds art: every picture is read from and written to the ignored `local/` (the pack is `local/hd-packs/interface-redraw-4x`, read only). Standard library only (`pngio.py`); ImageMagick for `sheet`.
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
    from . import button_kit, pngio, prompt_library, school_kit
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE.parents[1]))
    from tools.steamwoven_ui import button_kit, pngio, prompt_library, school_kit  # type: ignore

Image = pngio.Image
TOOL_VERSION = 1
TRIM = 0.4                   # units: the cut keeps this much beyond the ring's radius (the viewer's trim of the round parts)
OVERSHOOT = 1.2              # units: how far the cut's circle may lie beyond the art's rectangle (the ring of the 28 x 27 buttons is cut flat by the top of the frame, as the original's own is)
BLEED_PASSES = 64            # pack px: enough to colour every transparent pixel of the largest frame (112 x 116) next to an opaque one
STATE_FRAMES = {"idle": 0, "pressed": 1, "hover": 2}   # the pack's frame of each state (the original's order, ledger OF9)


class RingError(Exception):
    pass


@dataclass(frozen=True)
class Ring:
    art: int
    folder: str              # the census frames' folder
    name: str                # `plate.<name>`, `glyph.<name>`; the pack's index name
    frames: int              # the original's frame count
    master: tuple[int, int]  # the plate row's size
    circle: school_kit.Circle

    @property
    def states(self) -> tuple[str, ...]:
        return ("idle", "pressed", "hover") if self.frames >= 3 else ("idle", "pressed")


def _ring(art: int, folder: str, frames: int, master: tuple[int, int], size: tuple[int, int], cx: float, cy: float, radius: float) -> Ring:
    return Ring(art, f"{art}-{folder}", folder.lower(), frames, master, school_kit.Circle(size[0], size[1], cx, cy, radius, TRIM))


RINGS = (
    _ring(23, "char_minus", 4, (66, 66), (19, 19), 8.75, 9.05, 7.6),
    _ring(24, "char_plus", 4, (66, 66), (19, 19), 10.5, 9.15, 7.6),
    _ring(770, "Char_HTFTMinus", 4, (78, 78), (21, 21), 10.0, 10.1, 9.1),
    _ring(771, "Char_HTFTPlus", 4, (78, 78), (21, 21), 11.55, 10.25, 8.7),
    *(_ring(art, name, 2, (84, 84), (23, 23), 11.6, 11.4, 11.2) for art, name in ((145, "mmb_chest"), (146, "mmb_note"), (147, "mmb_ques"), (148, "mmb_skull"))),
    *(_ring(art, name, 3, (84, 84), (23, 23), 11.6, 11.55, 11.3) for art, name in ((757, "MPCycleLeftButton"), (758, "MPCycleRightButton"), (299, "Sm_RightArrow"))),
    *(_ring(art, name, 2, (102, 96), (28, 27), 13.4, 12.1, 12.8) for art, name in ((338, "PD_lil_butt"), (339, "Inven_lil_butt"), (340, "tiny_butt"))),
    *(_ring(art, name, 3, (102, 102), (28, 29), 13.35, 13.0, 12.8) for art, name in ((823, "Lil_Accept"), (824, "Lil_Cancel"))),
)


def ring_named(name: str) -> Ring:
    for ring in RINGS:
        if ring.name == name:
            return ring
    raise RingError(f"{name} is no button of this slice")


def idents(ring: Ring) -> list[tuple[str, str | None]]:
    """The parts of a button and the state each one shows (None: the empty glyph)."""
    return [(f"plate.{ring.name}", "idle"), (f"glyph.{ring.name}", None), *((f"plate.{ring.name}_{s}", s) for s in ring.states if s != "idle")]


# --- the part ---------------------------------------------------------------------------------------------------------------------

def bled(frame: Image, passes: int = BLEED_PASSES) -> Image:
    """The frame fully opaque: every pixel that is not (nearly) opaque takes the mean colour of its already coloured neighbours, ring by ring outwards, so the pack's own edge and holes never show through the analytic cut."""
    w, h = frame.w, frame.h
    out = Image(w, h, bytearray(frame.data))
    known = [frame.data[i * 4 + 3] >= 250 for i in range(w * h)]
    if not any(known):
        raise RingError("a frame with no opaque pixel has nothing to cut")
    todo = [i for i in range(w * h) if not known[i]]
    for _ in range(passes):
        if not todo:
            break
        fresh: list[tuple[int, bytes]] = []
        for i in todo:
            x, y = i % w, i // w
            near = [j for j in ((i - 1) if x else -1, (i + 1) if x < w - 1 else -1, (i - w) if y else -1, (i + w) if y < h - 1 else -1) if j >= 0 and known[j]]
            if near:
                fresh.append((i, bytes(round(sum(out.data[j * 4 + k] for j in near) / len(near)) for k in range(3))))
        for i, colour in fresh:
            out.data[i * 4:i * 4 + 3] = colour
            known[i] = True
        todo = [i for i in todo if not known[i]]
    if todo:
        raise RingError(f"{len(todo)} transparent pixel(s) lie further than {passes} px from the frame's opaque ones")
    for i in range(w * h):
        out.data[i * 4 + 3] = 255
    return out


def part_of(ring: Ring, frame: Image) -> Image:
    return school_kit.round_part(bled(frame), ring.circle, ring.master)


def state_frame(local: Path, ring: Ring, state: str) -> tuple[Image, Path]:
    try:
        frame, path = school_kit.read_pack(local, ring.name, STATE_FRAMES[state])
    except school_kit.SchoolError as error:
        raise RingError(str(error)) from error
    ux, uy = frame.w / ring.circle.width, frame.h / ring.circle.height
    if abs(ux - uy) > 0.05 * ux:
        raise RingError(f"{ring.name} {state}: the pack frame {frame.w} x {frame.h} is not the art's aspect {ring.circle.width} x {ring.circle.height}")
    return frame, path


def ring_parts(local: Path, ring: Ring) -> dict[str, Image]:
    return {ident: school_kit.empty_layer(ring.master) if state is None else part_of(ring, state_frame(local, ring, state)[0]) for ident, state in idents(ring)}


def record_of(local: Path, ring: Ring, ident: str, image_path: Path, image: Image) -> dict:
    """The provenance record of a part (SWI-GEN-6): no generator, the rule, and the hash of the pack frame it was cut from."""
    state = dict(idents(ring))[ident]
    sources: dict = {}
    if state is not None:
        path, index_sha = school_kit.pack_path(local, ring.name, STATE_FRAMES[state])
        sources = {f"{school_kit.PACK_DIR}/{ring.name}-f{STATE_FRAMES[state]:03d}": {"sha256": button_kit.sha256_file(path), "index_sha12": index_sha}}
    c = ring.circle
    return {"part": ident, "rule": "ring_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(), "source": "none (empty layer)" if state is None else "pack", "sources": sources,
            "empty": not any(image.data[3::4]), "parameters": {"circle_units": {"cx": c.cx, "cy": c.cy, "radius": c.radius, "trim": c.trim, "art_size": [c.width, c.height]}, "extrude_px": school_kit.EXTRUDE_PX, "master": [image.w, image.h]},
            "image_sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(), "image_size": [image.w, image.h], "run_by": "agent"}


# --- commands ---------------------------------------------------------------------------------------------------------------------

def selected(args: argparse.Namespace) -> list[Ring]:
    only = [n for n in args.only.split(",") if n] if args.only else None
    if only is None:
        return list(RINGS)
    unknown = sorted(set(only) - {r.name for r in RINGS})
    if unknown or not only:
        raise RingError(f"--only names buttons this slice does not hold: {unknown}")
    return [r for r in RINGS if r.name in only]


def cmd_check(args: argparse.Namespace) -> int:
    local = school_kit.pack_local(args.local_root)
    bad = []
    rings = selected(args)
    for ring in rings:
        c = ring.circle
        if min(c.cx - c.cut, c.cy - c.cut, c.width - c.cx - c.cut, c.height - c.cy - c.cut) < -OVERSHOOT:
            bad.append(f"{ring.name}: the circle leaves the art by more than {OVERSHOOT} unit")
        for state in ring.states:
            try:
                state_frame(local, ring, state)
            except RingError as error:
                bad.append(str(error))
    for line in bad:
        print(line)
    print("RING KIT check " + ("FAIL" if bad else f"PASS ({len(rings)} buttons, a pack frame for every state, circles inside the arts)"))
    return 1 if bad else 0


def cmd_build(args: argparse.Namespace) -> int:
    local_root = args.local_root
    local = school_kit.pack_local(local_root)
    out = args.out or local_root / "masters"
    records = local_root / "provenance" / "rule" if args.out is None else out / "records"
    built = [(ring, ring_parts(local, ring)) for ring in selected(args)]
    total = 0
    for _, parts in built:
        for ident, image in parts.items():
            covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
            print(f"{ident:34s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
            total += 1
    if args.dry_run:
        print(f"RING KIT build DRY-RUN PASS ({total} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ring, parts in built:
        for ident, image in parts.items():
            path = out / f"{ident}.png"
            pngio.write(path, image)
            (records / f"{ident}.json").write_text(json.dumps(record_of(local, ring, ident, path, image), indent=2) + "\n", encoding="utf-8")
    print(f"RING KIT build PASS ({total} parts written to {out})")
    return 0


def grounded(part: Image, ring: Ring) -> Image:
    """The picture over the sheet's ground with the margin `school_kit.staged` leaves, and no shadow."""
    mw, mh = school_kit.margin_of(ring.circle, ring.master)
    w, h = part.w + 2 * mw, part.h + 2 * mh
    out = Image(w, h, bytearray(bytes((*school_kit.GROUND, 255)) * (w * h)))
    for y in range(part.h):
        for x in range(part.w):
            k, o = (y * part.w + x) * 4, ((y + mh) * w + x + mw) * 4
            a = part.data[k + 3] / 255.0
            out.data[o:o + 3] = bytes(int(round(part.data[k + c] * a + school_kit.GROUND[c] * (1.0 - a))) for c in range(3))
    return out


def cmd_sheet(args: argparse.Namespace) -> int:
    """Per button one row: ORIGINAL (idle, pressed, hover) | PACK as drawn today (the same) | KIT part, cut only | KIT part with the run-time shadow. Every cell is scaled to one size per unit."""
    local_root = args.local_root
    local = school_kit.pack_local(local_root)
    frames = args.frames or local_root / "census" / "frames"
    if shutil.which("magick") is None:
        raise RingError("ImageMagick `magick` is not installed")
    rings = selected(args)
    states = ("idle", "pressed", "hover")
    columns = ("ORIGINAL", "HD PACK (today)", "KIT: cut only", "KIT: with shadow")
    per_unit = 4 * args.zoom
    cell = int((29 + 2 * school_kit.MARGIN_UNITS) * per_unit)
    label_w, gap = 190, 14
    work = Path(tempfile.mkdtemp(prefix="ring-sheet-"))
    try:
        blank = work / "blank.png"
        spacer = work / "spacer.png"
        subprocess.run(["magick", "-size", f"{cell}x{cell}", "xc:#202020", str(blank)], check=True)
        subprocess.run(["magick", "-size", f"{gap}x{cell}", "xc:#d0d0d0", str(spacer)], check=True)
        rows = []
        for ring in rings:
            margin = school_kit.margin_of(ring.circle, ring.master)
            size = (ring.master[0] + 2 * margin[0], ring.master[1] + 2 * margin[1])
            show = (int(round(size[0] * ring.circle.width / ring.master[0] * per_unit)), int(round(size[1] * ring.circle.height / ring.master[1] * per_unit)))
            label = work / f"{ring.name}-label.png"
            subprocess.run(["magick", "-size", f"{label_w}x{cell}", "xc:#202020", "-font", school_kit.LABEL_FONT, "-fill", "white", "-pointsize", "18", "-gravity", "center", "-annotate", "+0+0",
                            f"{ring.name}\nart {ring.art}, {ring.circle.width} x {ring.circle.height}", str(label)], check=True)
            cells: list[Path] = [label]
            for column, tag in enumerate(columns):
                cells.append(spacer)
                for state in states:
                    if state not in ring.states:
                        cells.append(blank)
                        continue
                    pack = state_frame(local, ring, state)[0]
                    if column == 0:
                        shot = grounded(school_kit.upscaled(pngio.read(frames / ring.folder / f"f{STATE_FRAMES[state]}.png"), ring.master), ring)
                    elif column == 1:
                        shot = grounded(school_kit.resampled(pack, *ring.master), ring)
                    elif column == 2:
                        shot = grounded(part_of(ring, pack), ring)
                    else:
                        shot = school_kit.staged(part_of(ring, pack), ring.circle, ring.master)
                    path = work / f"{ring.name}-{column}-{state}.png"
                    pngio.write(path, shot)
                    subprocess.run(["magick", str(path), "-filter", "point" if column == 0 else "Lanczos", "-resize", f"{show[0]}x{show[1]}!", "-background", "#202020", "-gravity", "center", "-extent", f"{cell}x{cell}", str(path)], check=True)
                    cells.append(path)
            row = work / f"{ring.name}-row.png"
            subprocess.run(["magick", *map(str, cells), "+append", str(row)], check=True)
            rows.append(row)
        width = label_w + len(columns) * (gap + 3 * cell)
        head = work / "head.png"
        command = ["magick", "-size", f"{width}x70", "xc:#202020", "-font", school_kit.LABEL_FONT, "-fill", "white", "-pointsize", "24"]
        for column, tag in enumerate(columns):
            command += ["-annotate", f"+{label_w + column * (gap + 3 * cell) + gap + 12}+30", tag, "-annotate", f"+{label_w + column * (gap + 3 * cell) + gap + 12}+60", "idle  |  pressed  |  hover"]
        subprocess.run([*command, str(head)], check=True)
        subprocess.run(["magick", str(head), *map(str, rows), "-background", "#202020", "-append", str(args.out)], check=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"RING KIT sheet PASS: {args.out}")
    return 0


def self_test() -> int:
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_ring_kit"], cwd=HERE.parents[1]).returncode
    print("RING KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return code


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("build", "check", "sheet", "self-test"):
        p = sub.add_parser(name)
        if name == "self-test":
            continue
        p.add_argument("--local-root", type=Path, default=None, help="the steamwoven-ui folder of the main checkout's local/ (default)")
        p.add_argument("--only", help="button names, comma separated")
        if name == "build":
            p.add_argument("--out", type=Path)
            p.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            p.add_argument("--out", type=Path, required=True)
            p.add_argument("--frames", type=Path)
            p.add_argument("--zoom", type=int, default=2, choices=range(1, 5))
    args = parser.parse_args(argv)
    if args.command == "self-test":
        return self_test()
    if args.local_root is None:
        args.local_root = prompt_library.default_local_root()
    try:
        return {"build": cmd_build, "check": cmd_check, "sheet": cmd_sheet}[args.command](args)
    except (RingError, school_kit.SchoolError, OSError, pngio.PngError, subprocess.CalledProcessError) as error:
        print(f"RING KIT {args.command} ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
