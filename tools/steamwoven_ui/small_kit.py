#!/usr/bin/env python3
"""ARC-UI-3206 (batch B-BUTTONS, slice 6): the small stone and dark buttons (scroll arrows, plus / minus, follower arrows) as kit parts, by the PACK-FIRST rule (owner 2026-11-25, TRD next to rules E and F).

Where a part has a good HD frame in the old generative pack (the installed `interface-redraw-4x`, 4 px per original cell), the kit part IS that frame (one per state) with an ANALYTIC alpha (a smooth even edge, no corners of
the old background, no staircase). Generation (Codex, rules E and F) is only for what has no pack frame: the two Plus / Minus buttons of the skill points (257, 258), a separate step.

    python3 -B tools/steamwoven_ui/small_kit.py plan   [--pack DIR]
    python3 -B tools/steamwoven_ui/small_kit.py build  [--only NAME,...] [--out DIR] [--dry-run] [--pack DIR] [--local-root DIR]
    python3 -B tools/steamwoven_ui/small_kit.py sheet  --out FILE [--only NAME,...] [--zoom N] [--pack DIR] [--local-root DIR]   (the composite original | pack-made parts, English labels, needs ImageMagick)
    python3 -B tools/steamwoven_ui/small_kit.py self-test

Per button (TRD SWI-KIT-5; the runtime's convention `SteamwovenKitComposer.ButtonLayers`: face, over it the glyph, over both the overlay `<face>_hover` or `<face>_pressed`):

* `plate.<name>`: the pack's idle frame at the plate's master size. The symbol, its checkerboard and the shading are IN the frame (owner: the cells in the pack's symbols are fine).
* `plate.<name>_hover`, `plate.<name>_pressed`: the pack's frame of that state at the same size (a full frame with the same alpha: laid over the idle plate it replaces it).
* `glyph.<name>`: an EMPTY layer at the plate's size (the symbol is in the frames; the runtime composes a face only with a glyph of its size, as for the Fate / Sleep ball).

The alpha: a round button (the olive dome with its brass ring) gets the ellipse of its own silhouette (the opaque box of the idle frame); a rectangle (a stone, olive or strip button) is opaque as it is; a scroll arrow
keeps the alpha of its own frame (a shaped arrow, softened by the resampling). The colours of the old transparent pixels are first bled outward from the opaque ones, so the new edge shows no dark fringe; the edge is one pixel of
anti-aliasing at the master size. The shared soft shadow of round parts (#3249) is a separate shared layer, not baked into these parts.

Nothing here holds art: every picture is read from the installed pack and written to the ignored `local/steamwoven-ui/`. Standard library only (`pngio.py`, `button_kit.py`); ImageMagick only for `sheet`.
Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import json
import math
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import button_kit, pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore

Image = pngio.Image
TOOL_VERSION = 2
MANIFEST = HERE / "kit.json"
PACK_ROOT = Path.home() / "Library" / "Application Support" / "Arcanum Steamwoven Game" / "hd-packs" / "interface-redraw-4x"   # the installed pack (read, never written)
PACK_SCALE = 4               # an HD pack frame is this many times the original
BLEED_PASSES = 4             # the colours of the old transparent pixels are bled this many pixels outward
OPAQUE = 128                 # an alpha at least this belongs to the silhouette of the old frame
LABEL_FONT = button_kit.LABEL_FONT


class SmallError(Exception):
    pass


@dataclass(frozen=True)
class Small:
    name: str                       # the plate's name in the manifest (`plate.<name>`) and the art's name in the pack's index
    art: int
    folder: str                     # the census frame folder (`<art>-<name>`)
    family: str                     # `round` (the ellipse alpha of the frame's silhouette), `rect` (an opaque frame, as it is), `own` (a shaped frame, its own alpha), `none` (no pack frame: generated)
    units: tuple[int, int]          # the original frame, in cells
    master: tuple[int, int]         # the kit master of the plate, in px (kit.json)
    frames: dict = field(default_factory=lambda: {"idle": 0, "pressed": 1, "hover": 2})   # state -> frame number (a state without a frame has no overlay)


def _round(name: str, art: int, folder: str) -> Small:
    return Small(name, art, folder, "round", (29, 30), (102, 108))


def _stone(name: str, art: int, folder: str) -> Small:
    return Small(name, art, folder, "rect", (32, 32), (114, 114))


SMALL = (
    _round("skiladdbut", 624, "624-SkilAddBut"),
    _round("skilminusbut", 625, "625-SkilMinusBut"),
    _round("sldrbutt_l_arrow", 667, "667-SldrButt_L_Arrow"),
    _round("sldrbutt_r_arrow", 668, "668-SldrButt_R_Arrow"),
    _stone("spelltech_add", 647, "647-SpellTech_Add"),
    _stone("spelltech_minus", 648, "648-SpellTech_Minus"),
    Small("oldchar_minus", 804, "804-OldChar_Minus", "rect", (13, 10), (48, 36)),
    Small("oldchar_plus", 805, "805-OldChar_Plus", "rect", (13, 10), (48, 36)),
    Small("scrllup", 317, "317-ScrllUP", "own", (11, 15), (42, 54)),
    Small("scrlldwn", 318, "318-ScrllDWN", "own", (11, 14), (42, 48)),
    Small("followerarrow_dwn", 501, "501-FollowerArrow_DWN", "rect", (67, 13), (240, 48), frames={"idle": 0}),
    Small("followerarrow_up", 502, "502-FollowerArrow_UP", "rect", (67, 13), (240, 48), frames={"idle": 0}),
    Small("follow_scroll_up", 506, "506-Follow_Scroll_up", "rect", (33, 13), (120, 48), frames={"idle": 0, "pressed": 1}),
    Small("follow_scroll_up_off", 507, "507-Follow_Scroll_up_OFF", "rect", (33, 13), (120, 48), frames={"idle": 0}),
    Small("follow_scroll_dwn", 508, "508-Follow_Scroll_dwn", "rect", (34, 13), (120, 48), frames={"idle": 0, "pressed": 1}),
    Small("follow_scroll_dwn_off", 509, "509-Follow_Scroll_dwn_OFF", "rect", (34, 13), (120, 48), frames={"idle": 0}),
)
# the two Plus / Minus buttons of the skill points (22 x 13 units, two frames) have no frame in the pack: generated (Codex, the neighbouring pack frames as the style sample), a separate step
GENERATED = (
    Small("sp_minus", 257, "257-SP_Minus", "none", (22, 13), (78, 48), frames={"idle": 0, "pressed": 1}),
    Small("sp_plus", 258, "258-SP_Plus", "none", (22, 13), (78, 48), frames={"idle": 0, "pressed": 1}),
)


def button_named(name: str) -> Small:
    for b in (*SMALL, *GENERATED):
        if b.name == name:
            return b
    raise SmallError(f"no small button {name!r}; the pack-first buttons are {[b.name for b in SMALL]}")


def selected(only: str | None) -> list[Small]:
    return [button_named(n) for n in only.split(",")] if only else list(SMALL)


# --- the pack's frames -------------------------------------------------------------------------------------------------------

def pack_index(pack: Path | None) -> dict[tuple[str, int], str]:
    """(art name, frame number) -> the frame's path in the pack, from its `index.tsv` (the first row of an art wins)."""
    index = pack / "index.tsv" if pack else None
    if index is None or not index.is_file():
        raise SmallError(f"no pack index {index}: the pack-first parts are read from the installed HD pack (--pack names another)")
    rows: dict[tuple[str, int], str] = {}
    for line in index.read_text(encoding="utf-8").splitlines():
        cols = line.split("\t")
        if len(cols) > 4 and cols[3].isdigit():
            rows.setdefault((cols[2], int(cols[3])), cols[4])
    return rows


def pack_frame(pack: Path, rows: dict[tuple[str, int], str], b: Small, number: int) -> tuple[Image, Path] | None:
    """The pack's frame `number` of the button (4 px per cell), or None when the pack has none."""
    rel = rows.get((b.name, number))
    if rel is None or not (pack / rel).is_file():
        return None
    image = pngio.read(pack / rel)
    expected = (b.units[0] * PACK_SCALE, b.units[1] * PACK_SCALE)
    if (image.w, image.h) != expected:
        raise SmallError(f"{rel}: {image.w} x {image.h}, the art {b.art} at {PACK_SCALE} x is {expected[0]} x {expected[1]}")
    return image, pack / rel


def read_frame(frames: Path, b: Small, number: int) -> Image:
    path = frames / b.folder / f"f{number}.png"
    if not path.is_file():
        raise SmallError(f"no frame {path}")
    image = pngio.read(path)
    if (image.w, image.h) != b.units:
        raise SmallError(f"{path}: {image.w} x {image.h}, the art {b.art} is {b.units[0]} x {b.units[1]}")
    return image


# --- the analytic alpha ------------------------------------------------------------------------------------------------------

def opaque_box(image: Image) -> tuple[float, float, float, float]:
    """x0, y0, x1, y1 (edges, in pixels) of the pixels with an alpha of `OPAQUE` or more."""
    xs, ys = [], []
    for i in range(image.w * image.h):
        if image.data[i * 4 + 3] >= OPAQUE:
            xs.append(i % image.w)
            ys.append(i // image.w)
    if not xs:
        raise SmallError("a frame with no opaque pixel has no silhouette")
    return float(min(xs)), float(min(ys)), float(max(xs) + 1), float(max(ys) + 1)


def bleed(image: Image, passes: int = BLEED_PASSES) -> Image:
    """The colours of the transparent pixels taken outward from the opaque ones (and made opaque), so an analytic edge a little outside the old one shows no dark fringe."""
    w, h = image.w, image.h
    out = Image(w, h, bytearray(image.data))
    known = [image.data[i * 4 + 3] >= 250 for i in range(w * h)]
    for _ in range(passes):
        fresh: list[tuple[int, bytes]] = []
        for i in range(w * h):
            if known[i]:
                continue
            x, y = i % w, i // w
            near = [j for j in ((i - 1) if x else -1, (i + 1) if x < w - 1 else -1, (i - w) if y else -1, (i + w) if y < h - 1 else -1) if j >= 0 and known[j]]
            if near:
                fresh.append((i, bytes(round(sum(out.data[j * 4 + k] for j in near) / len(near)) for k in range(3))))
        for i, colour in fresh:
            out.data[i * 4:i * 4 + 3] = colour
            out.data[i * 4 + 3] = 255
            known[i] = True
    return out


def edge(distance: float) -> float:
    """One pixel of anti-aliasing: 1 inside, 0 outside, a linear step across the pixel at the outline (`distance` > 0 inside)."""
    return max(0.0, min(1.0, distance + 0.5))


def ellipse_alpha(box: tuple[float, float, float, float], size: tuple[int, int], master: tuple[int, int]) -> list[float]:
    """The ellipse inscribed in `box` (frame pixels of an image of `size`) as an alpha plane at the master size (a smooth even edge)."""
    w, h = master
    sx, sy = w / size[0], h / size[1]
    cx, cy = (box[0] + box[2]) / 2.0 * sx, (box[1] + box[3]) / 2.0 * sy
    a, b = (box[2] - box[0]) / 2.0 * sx, (box[3] - box[1]) / 2.0 * sy
    r = min(a, b)
    out = [0.0] * (w * h)
    for y in range(h):
        for x in range(w):
            rho = math.hypot((x + 0.5 - cx) / a, (y + 0.5 - cy) / b)
            out[y * w + x] = edge((1.0 - rho) * r)   # the distance to the outline in master pixels (exact on a circle, a close one on a mild ellipse)
    return out


def resample(image: Image, w: int, h: int) -> Image:
    """The picture at another size by area-weighted box averaging (premultiplied), made for a reduction."""
    out = Image(w, h, bytearray(w * h * 4))
    sx, sy = image.w / w, image.h / h
    for y in range(h):
        y0, y1 = y * sy, (y + 1) * sy
        for x in range(w):
            x0, x1 = x * sx, (x + 1) * sx
            acc = [0.0, 0.0, 0.0, 0.0]
            wsum = 0.0
            for iy in range(int(y0), min(image.h, int(math.ceil(y1)))):
                wy = min(y1, iy + 1) - max(y0, iy)
                for ix in range(int(x0), min(image.w, int(math.ceil(x1)))):
                    wt = wy * (min(x1, ix + 1) - max(x0, ix))
                    p = image.pixel(ix, iy)
                    a = p[3] / 255.0
                    acc[0] += p[0] * a * wt
                    acc[1] += p[1] * a * wt
                    acc[2] += p[2] * a * wt
                    acc[3] += a * wt
                    wsum += wt
            if acc[3] > 0 and wsum > 0:
                o = (y * w + x) * 4
                out.data[o:o + 3] = bytes(max(0, min(255, int(round(acc[k] / acc[3])))) for k in range(3))
                out.data[o + 3] = int(round(255 * acc[3] / wsum))
    return out


def part_from_frame(b: Small, frame: Image, box: tuple[float, float, float, float]) -> Image:
    """A kit part from a pack frame: at the master size as it is (a rectangle is opaque, a scroll arrow keeps its own shaped alpha, which the area averaging softens); a round one with the old transparent pixels bled out and the analytic ellipse alpha of the idle frame's silhouette `box` (so every state has the very same edge)."""
    if b.family != "round":
        return resample(frame, *b.master)
    flat = resample(bleed(frame), *b.master)
    alpha = ellipse_alpha(box, (frame.w, frame.h), b.master)
    for i, a in enumerate(alpha):
        flat.data[i * 4 + 3] = int(round(a * 255))
    return flat


def button_parts(pack: Path, rows: dict[tuple[str, int], str], b: Small) -> tuple[dict[str, Image], dict[str, Path]]:
    """`plate.<name>` (the idle frame), `plate.<name>_hover` / `_pressed` for the states the art has, the empty `glyph.<name>`; and the pack file each plate came from."""
    found = {state: pack_frame(pack, rows, b, number) for state, number in b.frames.items()}
    missing = [state for state, f in found.items() if f is None]
    if missing:
        raise SmallError(f"{b.name}: the pack has no frame for {missing} (the pack-first parts need every state)")
    box = opaque_box(found["idle"][0])  # type: ignore[index]
    parts: dict[str, Image] = {}
    sources: dict[str, Path] = {}
    for state in b.frames:
        frame, path = found[state]  # type: ignore[misc]
        ident = f"plate.{b.name}" if state == "idle" else f"plate.{b.name}_{state}"
        parts[ident] = part_from_frame(b, frame, box)
        sources[ident] = path
    parts[f"glyph.{b.name}"] = Image.blank(*b.master)
    return parts, sources


def build_parts(pack: Path, buttons: list[Small]) -> tuple[dict[str, Image], dict[str, Path]]:
    rows = pack_index(pack)
    parts: dict[str, Image] = {}
    sources: dict[str, Path] = {}
    for b in buttons:
        p, s = button_parts(pack, rows, b)
        parts.update(p)
        sources.update(s)
    return parts, sources


def owner_of(ident: str) -> Small:
    """The button a part id (`plate.<name>`, `plate.<name>_hover`, `plate.<name>_pressed`, `glyph.<name>`) belongs to."""
    name = ident.split(".", 1)[1]
    for suffix in ("_hover", "_pressed"):
        if name.endswith(suffix) and any(b.name == name[:-len(suffix)] for b in SMALL):
            name = name[:-len(suffix)]
    return button_named(name)


# --- commands -----------------------------------------------------------------------------------------------------------------

def record_of(ident: str, b: Small, source: Path | None, image_path: Path, pack: Path) -> dict:
    return {
        "id": ident,
        "tool": "small_kit.py",
        "tool_version": TOOL_VERSION,
        "rule": ("pack-first (owner 2026-11-25): the old HD pack's frame of this state at the master size (a round one with the analytic ellipse alpha of its silhouette, any other with its own alpha)"
                 if source is not None else "an empty glyph layer at the plate's size: the symbol is in the pack frames"),
        "source_art": b.art,
        "source": {"pack": pack.name, "file": str(source.relative_to(pack)), "sha256": button_kit.sha256_file(source)} if source is not None else None,
        "sha256": button_kit.sha256_file(image_path),
        "date": datetime.date.today().isoformat(),
    }


def need_magick() -> None:
    if shutil.which("magick") is None:
        raise SmallError("ImageMagick (`magick`) is needed for this command")


def cmd_plan(args: argparse.Namespace) -> int:
    manifest = {r["id"]: r for r in json.loads(MANIFEST.read_text(encoding="utf-8"))["parts"]}
    rows = pack_index(args.pack)
    bad = 0
    for b in (*SMALL, *GENERATED):
        row = manifest.get(f"plate.{b.name}")
        have = [state for state, number in b.frames.items() if (b.name, number) in rows]
        full = len(have) == len(b.frames)
        ok = row is not None and row["size"] == list(b.master) and (full if b.family != "none" else not have)
        bad += 0 if ok else 1
        how = "pack frames" + (" + analytic alpha" if b.family == "round" else "") if b.family != "none" else "no pack frame: generated"
        print(f"plate.{b.name:24s} art {b.art}  {b.family:6s} {b.units[0]:>2} x {b.units[1]:<2} -> {b.master[0]} x {b.master[1]}  states {','.join(have) or '-':20s} {how:30s} {'ok' if ok else 'CHECK'}")
    print(f"SMALL KIT plan {'PASS' if not bad else 'FAIL'} ({len(SMALL)} pack-first buttons, {len(GENERATED)} generated)")
    return 0 if not bad else 1


def cmd_build(args: argparse.Namespace) -> int:
    local = args.local_root
    out = args.out or local / "masters"
    records = local / "provenance" / "rule" if args.out is None else out / "records"
    buttons = selected(args.only)
    parts, sources = build_parts(args.pack, buttons)
    for ident, image in parts.items():
        covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i] > 0) / (image.w * image.h)
        print(f"{ident:36s} {image.w} x {image.h}  covered {covered * 100:5.1f} %")
    if args.dry_run:
        print(f"SMALL KIT build DRY-RUN PASS ({len(parts)} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for ident, image in parts.items():
        path = out / f"{ident}.png"
        pngio.write(path, image)
        (records / f"{ident}.json").write_text(json.dumps(record_of(ident, owner_of(ident), sources.get(ident), path, args.pack), indent=2) + "\n", encoding="utf-8")
    print(f"SMALL KIT build PASS ({len(parts)} parts written to {out})")
    return 0


def upscaled(frame: Image, size: tuple[int, int]) -> Image:
    """An original frame at the master size, nearest cells (the 'original' column shows the original's own pixels)."""
    w, h = size
    out = Image(w, h, bytearray(w * h * 4))
    for py in range(h):
        cy = min(frame.h - 1, int((py + 0.5) * frame.h / h))
        for px in range(w):
            cx = min(frame.w - 1, int((px + 0.5) * frame.w / w))
            out.data[(py * w + px) * 4:(py * w + px) * 4 + 4] = frame.data[(cy * frame.w + cx) * 4:(cy * frame.w + cx) * 4 + 4]
    return out


def cmd_sheet(args: argparse.Namespace) -> int:
    """The composite for the owner: per button the original idle frame (nearest cells) | the kit's idle | hover | pressed plate (the pack-made parts)."""
    need_magick()
    frames = args.frames or args.local_root / "census" / "frames"
    buttons = selected(args.only)
    parts, _ = build_parts(args.pack, buttons)
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        rows_out = []
        for k, b in enumerate(buttons):
            cells = [("original idle", upscaled(read_frame(frames, b, 0), b.master)), ("idle", parts[f"plate.{b.name}"])]
            for state in ("hover", "pressed"):
                if f"plate.{b.name}_{state}" in parts:
                    cells.append((state, parts[f"plate.{b.name}_{state}"]))
            files = []
            for j, (label, image) in enumerate(cells):
                path = work / f"{k}-{j}.png"
                pngio.write(path, image)
                lab = work / f"{k}-{j}-lab.png"
                subprocess.run(["magick", str(path), "-background", "#3c3c3c", "-flatten", "-filter", "point", "-resize", f"{args.zoom * 100}%", "-gravity", "North", "-background", "#202020", "-fill", "white",
                                "-font", LABEL_FONT, "-pointsize", "14", "-splice", "0x22", "-annotate", "+0+3", f"{b.name} {label}", str(lab)], check=True)
                files.append(lab)
            row = work / f"row-{k}.png"
            subprocess.run(["magick", *[str(f) for f in files], "-background", "#202020", "-gravity", "North", "+append", "-bordercolor", "#202020", "-border", "6", str(row)], check=True)
            rows_out.append(row)
        args.out.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["magick", *[str(r) for r in rows_out], "-background", "#202020", "-gravity", "West", "-append", str(args.out)], check=True)
    print(f"SMALL KIT sheet PASS ({len(buttons)} buttons) -> {args.out}")
    return 0


def self_test() -> int:
    alpha = ellipse_alpha((0.0, 0.0, 20.0, 20.0), (20, 20), (40, 40))
    assert alpha[20 * 40 + 20] == 1.0 and alpha[0] == 0.0, "an ellipse is solid in the middle and clear in the corner"
    print("SMALL KIT self-test PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("plan", "build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--local-root", type=Path, default=None)
        s.add_argument("--pack", type=Path, default=PACK_ROOT, help="the installed HD pack the parts are read from")
        if name != "plan":
            s.add_argument("--only")
        if name == "build":
            s.add_argument("--out", type=Path)
            s.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            s.add_argument("--frames", type=Path)
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=3)
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        args.local_root = args.local_root or prompt_library.default_local_root()
        return {"plan": cmd_plan, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (SmallError, pngio.PngError, OSError, subprocess.CalledProcessError) as error:
        print(f"SMALL KIT FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
