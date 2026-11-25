"""ARC-UI-3207: the kit parts of B-BUTTONS slice 7: the map markers (arts 141 to 144, 204 to 206, 814) and the covers of the empty worn slots (241 to 249).

    python3 -B tools/steamwoven_ui/marker_kit.py plan
    python3 -B tools/steamwoven_ui/marker_kit.py build [--look pick|pack|rule] [--only NAMES] [--out DIR] [--dry-run]
    python3 -B tools/steamwoven_ui/marker_kit.py sheet --out PNG [--only NAMES]
    python3 -B tools/steamwoven_ui/marker_kit.py self-test

Two looks, no Codex in either. `pack` (rule P, pack first): the old HD pack's frame of the art at the master size with an analytic alpha (the original's
silhouette, enlarged smoothly; a slot cover is the pack's picture, opaque but for the original's transparent cells); a frame the pack does not hold is made by rule. `rule`: everything from the original's own frames: the round markers as
true circles of the original's radii and colours, the other markers with a smooth outline and the original's flat colours, the slot covers as the original's
flat silhouette (its darker details a darker step) with a smooth outline under straight grid lines. `pick`, the default and what the manifest records, is the
mix of the two by art (`RULE_PICK` takes the rule look, every other art the pack frame, and a missing pack frame is then an error). `sheet` is the composite
original | pack | rule with the picked cell framed green.
The originals and the pack are the player's own files and are never committed.
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
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import button_kit, kit_check, pngio, small_kit
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import button_kit  # type: ignore
    import kit_check  # type: ignore
    import pngio  # type: ignore
    import small_kit  # type: ignore

Image = pngio.Image
TOOL_VERSION = 1
PACK_SCALE = small_kit.PACK_SCALE
SS = 4                      # the analytic shapes are sampled SS x SS times per master pixel
SILHOUETTE_SIGMA = 0.45     # in original cells: smooth enough for a diagonal, sharp enough to keep a one-cell line
EDGE_HALF = 0.14            # the half width of the soft threshold of a smoothed mask
COVER_SIGMA = 0.38          # the covers' tone field: a little sharper, so the original's inner lines and specks survive
COVER_LEVELS = (0.2, 0.4, 0.6, 0.8)  # soft thresholds of the covers' tone: darker details of the original stay a darker step, edges stay sharp
COLOUR_POWER = 8            # how sharply two colour regions of a smoothed marker part


class MarkerError(Exception):
    pass


@dataclass(frozen=True)
class Art:
    name: str                # the part's name: `glyph.<name>` and, for a second frame, `glyph.<name>_lit`
    art: int
    folder: str              # the census frame folder
    pack_name: str           # the art's name in the pack's index
    family: str              # "marker" (a drawn symbol) | "round" (a circle marker) | "cover" (an empty worn slot)
    sizes: tuple[tuple[int, int], ...]   # original units of each frame


def _a(name: str, art: int, file: str, family: str, *sizes: tuple[int, int]) -> Art:
    return Art(name, art, f"{art}-{file}", file, family, sizes)


ARTS = (
    _a("mm_chest", 141, "mm_chest", "marker", (16, 18)),
    _a("mm_note", 142, "mm_note", "marker", (13, 17)),
    _a("mm_ques", 143, "mm_ques", "marker", (11, 14)),
    _a("mm_skull", 144, "mm_skull", "marker", (19, 18)),
    _a("mm_cross", 204, "MM_Cross", "marker", (11, 11), (13, 13)),
    _a("mm_loc", 205, "MM_Loc", "round", (9, 9), (11, 11)),
    _a("mm_wayp", 206, "MM_WayP", "round", (9, 9), (11, 11)),
    _a("mm_locnew", 814, "MM_LocNew", "round", (11, 11), (11, 11)),
    _a("cvr_helmet", 241, "CVR_Helmet", "cover", (62, 64)),
    _a("cvr_boot", 242, "CVR_Boot", "cover", (64, 63)),
    _a("cvr_gauntlet", 243, "CVR_Gauntlet", "cover", (63, 64)),
    _a("cvr_armor", 244, "CVR_Armor", "cover", (128, 160)),
    _a("cvr_medalion", 245, "CVR_Medalion", "cover", (64, 31)),
    _a("cvr_ring1", 246, "CVR_Ring1", "cover", (32, 32)),
    _a("cvr_ring2", 247, "CVR_Ring2", "cover", (32, 32)),
    _a("cvr_shield", 248, "CVR_Shield", "cover", (95, 127)),
    _a("cvr_weapon", 249, "CVR_Weapon", "cover", (96, 129)),
)
SAME_AS = {"cvr_ring2": "cvr_ring1"}   # arts whose original frames are pixel for pixel another's: the pack frame of that one serves them
# The arts that take the rule look in the picked mix: the pack holds no frame of art 204 and no lit frame of 205 and 206, reads the cross of 814 as a diamond,
# and draws the covers of the armour, shield and weapon slots as solid objects (an empty slot then looks taken, #3105).
RULE_PICK = frozenset({"mm_cross", "mm_loc", "mm_wayp", "mm_locnew", "cvr_armor", "cvr_shield", "cvr_weapon"})


@dataclass(frozen=True)
class Part:
    ident: str
    art: Art
    frame: int
    units: tuple[int, int]
    size: tuple[int, int]    # the master


def parts_of(art: Art) -> list[Part]:
    return [Part(f"glyph.{art.name}" + ("" if n == 0 else "_lit"), art, n, units, (kit_check.snap(units[0]), kit_check.snap(units[1]))) for n, units in enumerate(art.sizes)]


PARTS = {p.ident: p for a in ARTS for p in parts_of(a)}


SAME_FRAMES = frozenset({"mm_locnew"})   # arts whose two frames are the same picture
PACK_ALPHA = {False: "with the original's silhouette as an analytic alpha", True: "opaque but for the original's transparent cells"}   # by "is a cover"


def picked_look(art: Art) -> str:
    return "rule" if art.name in RULE_PICK else "pack"


def provenance_of(ident: str) -> str:
    return f"{picked_look(PARTS[ident].art)}:marker_kit:{ident}"


def note_of(ident: str) -> str:
    part = PARTS[ident]
    what = ("the cover of an empty worn slot" if part.art.family == "cover" else "the map marker") + f" of art {part.art.art}"
    if part.frame:
        what += ", frame 1 (same picture as frame 0)" if part.art.name in SAME_FRAMES else ", frame 1 (yellow ring)"
    if picked_look(part.art) == "pack":
        how = "the old HD pack's frame" + (f" of art {art_named(SAME_AS[part.art.name]).art} (the same original picture)" if part.art.name in SAME_AS else "") + f", {PACK_ALPHA[part.art.family == 'cover']}"
    else:
        how = {"cover": "by rule: the original's flat silhouette and darker details with a smooth outline under straight grid lines",
               "round": "by rule: true circles of the original's radii and colours",
               "marker": "by rule: the original's flat colours with a smooth outline"}[part.art.family]
    return f"{what}: {how} (marker_kit.py, ARC-UI-3207, no Codex)"


def art_named(name: str) -> Art:
    for a in ARTS:
        if a.name == name:
            return a
    raise MarkerError(f"no art {name!r}; the arts are {[a.name for a in ARTS]}")


def selected(only: str | None) -> list[Art]:
    return [art_named(n) for n in only.split(",")] if only else list(ARTS)


def read_original(frames: Path, part: Part) -> Image:
    path = frames / part.art.folder / f"f{part.frame}.png"
    if not path.is_file():
        raise MarkerError(f"no frame {path}")
    image = pngio.read(path)
    if (image.w, image.h) != part.units:
        raise MarkerError(f"{path}: {image.w} x {image.h}, the art {part.art.art} frame {part.frame} is {part.units[0]} x {part.units[1]}")
    return image


# --- the rule look -----------------------------------------------------------------------------------------------------------

def edge(plane: list[float]) -> list[float]:
    return [button_kit.smoothstep(0.5 - EDGE_HALF, 0.5 + EDGE_HALF, v) for v in plane]


def silhouette_alpha(frame: Image, size: tuple[int, int]) -> list[float]:
    """The original's transparent places enlarged smoothly: the analytic alpha of a part at the master size."""
    mask = [1.0 if frame.data[i * 4 + 3] >= 128 else 0.0 for i in range(frame.w * frame.h)]
    if all(v == 1.0 for v in mask):
        return [1.0] * (size[0] * size[1])
    return edge(button_kit.resample_plane(mask, (frame.w, frame.h), size, SILHOUETTE_SIGMA))


def smooth_marker(frame: Image, size: tuple[int, int]) -> Image:
    """A drawn marker at the master size: the original's flat colours, every border between two colours and the outline a smooth sharp curve."""
    n = frame.w * frame.h
    colours: dict[tuple[int, int, int], list[float]] = {}
    for i in range(n):
        r, g, b, a = frame.data[i * 4:i * 4 + 4]
        if a >= 128:
            colours.setdefault((r, g, b), [0.0] * n)[i] = 1.0
    w, h = size
    weight = [0.0] * (w * h)
    acc = [[0.0] * (w * h) for _ in range(3)]
    for colour, plane in colours.items():
        smoothed = button_kit.resample_plane(plane, (frame.w, frame.h), size, SILHOUETTE_SIGMA)
        for i, v in enumerate(smoothed):
            if v > 1e-4:
                p = v ** COLOUR_POWER
                weight[i] += p
                for k in range(3):
                    acc[k][i] += colour[k] * p
    alpha = silhouette_alpha(frame, size)
    out = Image(w, h, bytearray(w * h * 4))
    for i in range(w * h):
        if alpha[i] > 0.002 and weight[i] > 0:
            out.data[i * 4:i * 4 + 4] = bytes((*(min(255, int(round(acc[k][i] / weight[i]))) for k in range(3)), int(round(alpha[i] * 255))))
    return out


def mean_colour(colours: list[tuple[int, int, int]]) -> tuple[int, int, int]:
    return tuple(int(round(sum(c[k] for c in colours) / len(colours))) for k in range(3))  # type: ignore[return-value]


def round_layers(frame: Image) -> tuple[list[tuple[float, tuple[int, int, int]]], tuple[int, int, int] | None]:
    """A circle marker as rings from the outside in, `(radius in cells, colour)`, and the colour of its centre cross (or None). A coloured outline (yellow,
    violet) is one cell wide, as in the original; an outer radius is that of a disc with the area of the original's cells, at most half the frame, and the
    bright disc of a waypoint has the area of its bright cells; its rim takes the mean of the original's dark cells."""
    cells: dict[tuple[int, int, int], int] = {}
    for y in range(frame.h):
        for x in range(frame.w):
            r, g, b, a = frame.pixel(x, y)
            if a >= 128:
                cells[(r, g, b)] = cells.get((r, g, b), 0) + 1
    if not cells:
        raise MarkerError("an empty circle marker frame")
    half = min(frame.w, frame.h) / 2

    def radius(area: float) -> float:
        return min(half, math.sqrt(area / math.pi))

    yellow = [c for c in cells if c[0] > 200 and c[1] > 200 and c[2] < 80]
    rest = {c: k for c, k in cells.items() if c not in yellow}
    if not rest:  # the "new location" ring: a yellow ring one cell wide round a centre cross
        return [(half, yellow[0]), (half - 1.0, (-1, -1, -1))], yellow[0]
    rings: list[tuple[float, tuple[int, int, int]]] = []
    if yellow:
        rings.append((half, yellow[0]))
    inner_half = half - (1.0 if yellow else 0.0)
    purple = [c for c in rest if c[2] > 200]
    if purple:  # a coloured ring round a black centre
        outer = min(inner_half, radius(sum(rest.values())))
        rings.append((outer, purple[0]))
        rings.append((outer - 1.0, (0, 0, 0)))
    else:       # a bright disc in a dark rim
        bright = max(rest, key=lambda c: c[0])
        rim = [c for c, k in rest.items() if c != bright for _ in range(k)]
        rings.append((inner_half, mean_colour(rim) if rim else bright))
        rings.append((radius(rest[bright]), bright))
    return rings, None


def round_marker(frame: Image, size: tuple[int, int]) -> Image:
    rings, cross = round_layers(frame)
    w, h = size
    cx, cy = frame.w / 2, frame.h / 2
    out = Image(w, h, bytearray(w * h * 4))
    for py in range(h):
        for px in range(w):
            acc = [0, 0, 0]
            hits = 0
            for sy in range(SS):
                v = (py + (sy + 0.5) / SS) * frame.h / h - cy
                for sx in range(SS):
                    u = (px + (sx + 0.5) / SS) * frame.w / w - cx
                    colour = None
                    d = math.hypot(u, v)
                    for r, c in rings:  # outside in: the innermost ring that holds the sample wins
                        if d <= r:
                            colour = None if c[0] < 0 else c
                    if cross is not None and ((abs(u) <= 1.5 and abs(v) <= 0.5) or (abs(v) <= 1.5 and abs(u) <= 0.5)):
                        colour = cross
                    if colour is not None:
                        hits += 1
                        for k in range(3):
                            acc[k] += colour[k]
            if hits:
                o = (py * w + px) * 4
                out.data[o:o + 4] = bytes((*(int(round(acc[k] / hits)) for k in range(3)), int(round(255 * hits / (SS * SS)))))
    return out


def is_grid(p: tuple[int, int, int, int]) -> bool:
    return p[3] >= 128 and p[2] - p[0] >= 8


def cover_layers(frame: Image) -> tuple[list[float], list[int], list[int], dict[str, tuple[int, int, int]]]:
    """A slot cover's silhouette coverage per cell (0..1), its grid columns and rows, and its four flat colours: `ground`, `shape`, `line`, `line_shape`."""
    n = frame.w * frame.h
    pixels = [frame.pixel(i % frame.w, i // frame.w) for i in range(n)]
    grid = [is_grid(p) for p in pixels]
    columns = [x for x in range(frame.w) if sum(grid[y * frame.w + x] for y in range(frame.h)) > frame.h * 0.8]
    rows = [y for y in range(frame.h) if sum(grid[y * frame.w + x] for x in range(frame.w)) > frame.w * 0.8]

    def common(want_grid: bool, low: int, high: int) -> tuple[int, int, int] | None:
        counts: dict[tuple[int, int, int], int] = {}
        for p, g in zip(pixels, grid):
            if p[3] >= 128 and g == want_grid and low <= p[0] <= high:
                counts[p[:3]] = counts.get(p[:3], 0) + 1
        return max(counts, key=counts.__getitem__) if counts else None

    colours = {"ground": common(False, 0, 10) or (0, 0, 0), "shape": common(False, 20, 120)}
    if colours["shape"] is None:
        raise MarkerError("a slot cover without a silhouette colour")
    colours["line"] = common(True, 0, 62) or colours["ground"]
    colours["line_shape"] = common(True, 63, 255) or colours["shape"]
    ground, shape, line, line_shape = (colours[k][0] for k in ("ground", "shape", "line", "line_shape"))  # type: ignore[index]
    coverage = []
    for i, (p, g) in enumerate(zip(pixels, grid)):
        on_line = (i % frame.w) in columns or (i // frame.w) in rows
        low, high = (line, line_shape) if g and on_line else (ground, shape)
        coverage.append(min(1.0, max(0.0, (p[0] - low) / max(1, high - low))) if p[3] >= 128 else 0.0)
    return coverage, columns, rows, colours  # type: ignore[return-value]


def line_cover(cells: list[int], units: int, pixels: int) -> list[float]:
    """The share of each master pixel along one axis that the grid lines (one original cell wide) cover."""
    out = [0.0] * pixels
    for c in cells:
        a, b = c * pixels / units, (c + 1) * pixels / units
        for i in range(max(0, int(a)), min(pixels, int(math.ceil(b)))):
            out[i] = min(1.0, out[i] + max(0.0, min(b, i + 1) - max(a, i)))
    return out


def cover(frame: Image, size: tuple[int, int]) -> Image:
    """A slot cover by rule: the original's flat ground and silhouette (its outline a smooth sharp curve) under straight grid lines at the original's places."""
    coverage, columns, rows, colours = cover_layers(frame)
    w, h = size
    tone = button_kit.resample_plane(coverage, (frame.w, frame.h), size, COVER_SIGMA)
    shape = [sum(button_kit.smoothstep(t - EDGE_HALF / 2, t + EDGE_HALF / 2, v) for t in COVER_LEVELS) / len(COVER_LEVELS) for v in tone]
    cols, rws = line_cover(columns, frame.w, w), line_cover(rows, frame.h, h)
    # a transparent original cell is a straight edge of the cover (its top row): the nearest cell's alpha, no smoothing
    opaque = [255.0 if frame.data[i * 4 + 3] >= 128 else 0.0 for i in range(frame.w * frame.h)]
    out = Image(w, h, bytearray(w * h * 4))
    for py in range(h):
        cy = min(frame.h - 1, int((py + 0.5) * frame.h / h))
        for px in range(w):
            i = py * w + px
            s = shape[i]
            lines = 1.0 - (1.0 - cols[px]) * (1.0 - rws[py])
            rgb = []
            for k in range(3):
                under = colours["ground"][k] * (1 - s) + colours["shape"][k] * s
                on = colours["line"][k] * (1 - s) + colours["line_shape"][k] * s
                rgb.append(max(0, min(255, int(round(under * (1 - lines) + on * lines)))))
            alpha = int(opaque[cy * frame.w + min(frame.w - 1, int((px + 0.5) * frame.w / w))])
            if alpha:
                out.data[i * 4:i * 4 + 4] = bytes((*rgb, alpha))
    return out


def by_rule(part: Part, frame: Image) -> Image:
    if part.art.family == "cover":
        return cover(frame, part.size)
    return round_marker(frame, part.size) if part.art.family == "round" else smooth_marker(frame, part.size)


# --- the pack look ---------------------------------------------------------------------------------------------------------------

def bleed(image: Image) -> Image:
    """The colours of the texels that are not solid taken from their solid neighbours, outward, so a new smooth edge has no dark fringe."""
    w, h = image.w, image.h
    out = Image(w, h, bytearray(image.data))
    known = [out.data[i * 4 + 3] >= 200 for i in range(w * h)]
    if not any(known):
        raise MarkerError("a pack frame with no solid texel")
    frontier = [i for i in range(w * h) if not known[i]]
    while frontier:
        done, left = [], []
        for i in frontier:
            x, y = i % w, i // w
            near = [j for j in (i - 1 if x else -1, i + 1 if x < w - 1 else -1, i - w if y else -1, i + w if y < h - 1 else -1) if j >= 0 and known[j]]
            if near:
                done.append((i, bytes(sum(out.data[j * 4 + k] for j in near) // len(near) for k in range(3))))
            else:
                left.append(i)
        for i, rgb in done:
            out.data[i * 4:i * 4 + 3] = rgb
            known[i] = True
        if not done:
            break
        frontier = left
    for i in range(w * h):
        out.data[i * 4 + 3] = 255
    return out


def pack_source(pack: Path, rows: dict[tuple[str, int], str], part: Part) -> Path | None:
    name = art_named(SAME_AS[part.art.name]).pack_name if part.art.name in SAME_AS else part.art.pack_name
    rel = rows.get((name, part.frame))
    if rel is None:
        rel = next((v for (n, f), v in rows.items() if n.lower() == name.lower() and f == part.frame), None)
    return pack / rel if rel is not None and (pack / rel).is_file() else None


def from_pack(part: Part, frame: Image, source: Path) -> Image:
    image = pngio.read(source)
    expected = (part.units[0] * PACK_SCALE, part.units[1] * PACK_SCALE)
    if (image.w, image.h) != expected:
        raise MarkerError(f"{source.name}: {image.w} x {image.h}, the frame at {PACK_SCALE} x is {expected[0]} x {expected[1]}")
    out = small_kit.resample(bleed(image), *part.size)
    if part.art.family == "cover":  # opaque but for the original's transparent cells (a straight edge)
        alpha = []
        for py in range(out.h):
            cy = min(frame.h - 1, int((py + 0.5) * frame.h / out.h))
            alpha += [1.0 if frame.pixel(min(frame.w - 1, int((px + 0.5) * frame.w / out.w)), cy)[3] >= 128 else 0.0 for px in range(out.w)]
    else:
        alpha = silhouette_alpha(frame, part.size)
    for i, a in enumerate(alpha):
        out.data[i * 4 + 3] = int(round(a * 255))
        if a <= 0.002:
            out.data[i * 4:i * 4 + 4] = bytes(4)
    return out


# --- the build ----------------------------------------------------------------------------------------------------------------

def build_part(part: Part, frames: Path, pack: Path, rows: dict[tuple[str, int], str], look: str) -> tuple[Image, Path | None]:
    """The part at the master size and the pack file it came from (None: made by rule). Look `pack` falls back to the rule where the pack has no frame;
    the picked mix (`pick`) does not."""
    frame = read_original(frames, part)
    if part.art.name in SAME_AS and frame.data != read_original(frames, parts_of(art_named(SAME_AS[part.art.name]))[part.frame]).data:
        raise MarkerError(f"{part.ident}: the original frame is not {SAME_AS[part.art.name]}'s, so that pack frame does not serve it")
    wanted = picked_look(part.art) if look == "pick" else look
    source = pack_source(pack, rows, part) if wanted == "pack" else None
    if look == "pick" and wanted == "pack" and source is None:
        raise MarkerError(f"{part.ident}: the pack has no frame for it (the picked look is the pack's); --pack names the installed pack")
    return (from_pack(part, frame, source), source) if source is not None else (by_rule(part, frame), None)


def record_of(part: Part, image_path: Path, frames: Path, source: Path | None, pack: Path) -> dict:
    original = frames / part.art.folder / f"f{part.frame}.png"
    return {
        "id": part.ident,
        "tool": "marker_kit.py",
        "tool_version": TOOL_VERSION,
        "rule": (f"pack-first: the old HD pack's frame at the master size, {PACK_ALPHA[part.art.family == 'cover']}" if source is not None
                 else "by rule from the original's own frame (no Codex)"),
        "source_art": part.art.art,
        "source": None if source is None else {"pack": pack.name, "file": str(source.relative_to(pack)), "sha256": button_kit.sha256_file(source)},
        "original": {"frame": f"{part.art.folder}/f{part.frame}.png", "sha256": button_kit.sha256_file(original)},
        "sha256": button_kit.sha256_file(image_path),
        "date": datetime.date.today().isoformat(),
    }


def context(args: argparse.Namespace) -> tuple[Path, dict[tuple[str, int], str]]:
    rows = {} if getattr(args, "look", None) == "rule" else small_kit.pack_index(args.pack)   # the rule look reads nothing from the pack
    return args.frames or args.local_root / "census" / "frames", rows


def cmd_plan(args: argparse.Namespace) -> int:
    frames, rows = context(args)
    manifest = {r["id"]: r for r in json.loads(small_kit.MANIFEST.read_text(encoding="utf-8"))["parts"]}
    bad = 0
    for part in PARTS.values():
        row = manifest.get(part.ident)
        read_original(frames, part)
        ok = row is not None and row["size"] == (list(part.size) if part.frame == 0 else None) and row["provenance"] == provenance_of(part.ident)
        bad += 0 if ok else 1
        print(f"{part.ident:26s} art {part.art.art} f{part.frame}  {part.units[0]:>3} x {part.units[1]:<3} -> {part.size[0]} x {part.size[1]}  pack {'yes' if pack_source(args.pack, rows, part) else 'no ':3s}  {'ok' if ok else 'CHECK'}")
    print(f"MARKER KIT plan {'PASS' if not bad else 'FAIL'} ({len(PARTS)} parts)")
    return 0 if not bad else 1


def cmd_build(args: argparse.Namespace) -> int:
    frames, rows = context(args)
    out = args.out or args.local_root / "masters"
    records = args.local_root / "provenance" / "rule" if args.out is None else out / "records"
    made = []
    for art in selected(args.only):
        for part in parts_of(art):
            image, source = build_part(part, frames, args.pack, rows, args.look)
            covered = sum(1 for i in range(3, len(image.data), 4) if image.data[i]) / (image.w * image.h)
            print(f"{part.ident:26s} {image.w} x {image.h}  {'pack' if source else 'rule'}  covered {covered * 100:5.1f} %")
            made.append((part, image, source))
    if args.dry_run:
        print(f"MARKER KIT build DRY-RUN PASS ({len(made)} parts); nothing written")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    records.mkdir(parents=True, exist_ok=True)
    for part, image, source in made:
        path = out / f"{part.ident}.png"
        pngio.write(path, image)
        (records / f"{part.ident}.json").write_text(json.dumps(record_of(part, path, frames, source, args.pack), indent=2) + "\n", encoding="utf-8")
    print(f"MARKER KIT build PASS ({len(made)} parts written to {out})")
    return 0


def cmd_sheet(args: argparse.Namespace) -> int:
    """The composite for the owner, per frame: the original | A pack first | B by rule, all at the master size times the zoom."""
    if shutil.which("magick") is None:
        raise MarkerError("ImageMagick (`magick`) is needed for this command")
    frames, rows = context(args)
    arts = selected(args.only)
    covers = all(a.family == "cover" for a in arts)
    zoom = 1 if covers else args.zoom
    ground = "#606060" if covers else "#b9a47c"
    columns = [max(p.size[k] for a in arts for p in parts_of(a)) * zoom + 24 for k in (0, 1)]
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)

        def label(path: Path, text: str, w: int, h: int, background: str = "#202020") -> Path:
            subprocess.run(["magick", "-size", f"{w}x{h}", f"xc:{background}", "-font", small_kit.LABEL_FONT, "-fill", "white", "-pointsize", "16", "-gravity", "center", "-annotate", "+0+0", text, str(path)], check=True)
            return path

        def joined(path: Path, items: list[Path], how: str) -> Path:
            subprocess.run(["magick", *[str(i) for i in items], "-background", "#202020", how, str(path)], check=True)
            return path

        width = max(columns[0], 170)
        head = joined(work / "head.png", [label(work / f"h{k}.png", t, w, 30) for k, (t, w) in enumerate((("art", 170), ("original", width), ("A: HD pack first", width), ("B: by rule", width)))], "+append")
        lines = [head]
        for art in arts:
            for part in parts_of(art):
                frame = read_original(frames, part)
                pack_image, source = build_part(part, frames, args.pack, rows, "pack")
                cells = [label(work / f"{part.ident}-l.png", f"{art.art} {art.name}\nframe {part.frame}", 170, part.size[1] * zoom + 24)]
                for k, image in enumerate((small_kit.upscaled(frame, part.size), pack_image, by_rule(part, frame))):
                    raw = work / f"{part.ident}-{k}.png"
                    pngio.write(raw, image)
                    cell = work / f"{part.ident}-{k}c.png"
                    note = ["-font", small_kit.LABEL_FONT, "-pointsize", "13", "-fill", "black", "-gravity", "South", "-annotate", "+0+1", "no pack frame: by rule"] if k == 1 and source is None else []
                    subprocess.run(["magick", str(raw), "-filter", "point", "-resize", f"{zoom * 100}%", "-background", ground, "-flatten", "-gravity", "center", "-extent", f"{width - 6}x{part.size[1] * zoom + 18}",
                                    *note, "-bordercolor", "#30d050" if k == (2 if art.name in RULE_PICK else 1) else "#202020", "-border", "3", str(cell)], check=True)
                    cells.append(cell)
                lines.append(joined(work / f"{part.ident}-row.png", cells, "+append"))
        args.out.parent.mkdir(parents=True, exist_ok=True)
        joined(args.out, lines, "-append")
    print(f"MARKER KIT sheet PASS ({len(arts)} arts) -> {args.out}")
    return 0


def self_test() -> int:
    frame = Image(9, 9, bytearray(9 * 9 * 4))
    for y in range(9):
        for x in range(9):
            d = math.hypot(x - 4, y - 4)
            if d <= 4.4:
                frame.data[(y * 9 + x) * 4:(y * 9 + x) * 4 + 4] = bytes((255, 0, 0, 255) if d <= 3.2 else (74, 0, 0, 255))
    made = round_marker(frame, (30, 30))
    assert made.pixel(15, 15) == (255, 0, 0, 255) and made.pixel(0, 0)[3] == 0, "a red disc in the middle, clear corners"
    smooth = smooth_marker(frame, (30, 30))
    assert smooth.pixel(15, 15) == (255, 0, 0, 255) and smooth.pixel(0, 0)[3] == 0
    print("MARKER KIT self-test PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("plan", "build", "sheet"):
        s = sub.add_parser(name)
        s.add_argument("--local-root", type=Path, default=None)
        s.add_argument("--pack", type=Path, default=small_kit.PACK_ROOT, help="the installed HD pack the pack look reads")
        s.add_argument("--frames", type=Path)
        if name != "plan":
            s.add_argument("--only")
        if name == "build":
            s.add_argument("--look", default="pick", choices=("pick", "pack", "rule"))
            s.add_argument("--out", type=Path)
            s.add_argument("--dry-run", action="store_true")
        if name == "sheet":
            s.add_argument("--out", type=Path, required=True)
            s.add_argument("--zoom", type=int, default=3)
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    try:
        if args.command == "self-test":
            return self_test()
        if args.local_root is None:
            try:
                from . import fit
            except ImportError:
                import fit  # type: ignore
            args.local_root = fit.default_local_root()
        return {"plan": cmd_plan, "build": cmd_build, "sheet": cmd_sheet}[args.command](args)
    except (MarkerError, small_kit.SmallError, button_kit.ButtonError, pngio.PngError) as error:
        print(f"MARKER KIT FAIL: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
