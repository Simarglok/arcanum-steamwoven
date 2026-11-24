#!/usr/bin/env python3
"""ARC-UI-3077: turns the frames of the kit assembly tool (assemble.py) into an importer plan for the HD pack.

`assemble.py` writes `<art>-fNNN.png` and `<art>.assembly.json`. The pack's importer (`tools/hd-interface-import`, run by
`hd_pack.py stage`) takes an `arcanum-steamwoven-hd-interface-plan` whose `ready` items name the frames and the ART files they
replace. This tool writes that plan and, for a frame assembled in the `masked` alpha mode, the grey alpha mask of the assembled plate
(so the plate keeps its own shape and not the rim and corners of the original frame), from the assembly records alone:

    python3 -B tools/steamwoven_ui/pack_plan.py DIR_OR_RECORD... [--out DIR] [--pack-id ID] [--masks auto|always|never] [--dry-run]

Per frame one `ready` item with `"lock": false` (an assembled frame is final: the importer's shading lock would pull it back to the
original's tone); the tile names the ART file (`Schematics_Button.ART`) found from the art number in the census
(`docs/ui/steamwoven-interface-census-arts.tsv`) or given with `--art-name 471=Schematics_Button.ART`. The plan, the masks and the art map
(printed) go next to the frames by default and are local files: the plan names original ART files, so it is never committed, and the tool
refuses an output folder inside the checkout other than `local/`. Standard library only.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import struct
import sys
import zlib
from dataclasses import dataclass, field
from pathlib import Path

try:
    from tools.steamwoven_ui import census, pngio
except ImportError:  # run as a script: python3 tools/steamwoven_ui/pack_plan.py
    import census  # type: ignore[no-redef]
    import pngio  # type: ignore[no-redef]

PLAN_KIND = "arcanum-steamwoven-hd-interface-plan"  # the importer's kind (HdInterfacePlan.Kind)
ALPHA_MODES = ("legacy", "legacySmooth", "masked")
MASK_MODES = ("auto", "always", "never")
DEFAULT_PACK_ID = "interface-redraw-4x"
PLAN_NAME = "plan.json"
MASK_DIR = "masks"
RECORD_SUFFIX = ".assembly.json"
REPO = Path(__file__).resolve().parents[2]
_PACK_ID = re.compile(r"[a-z0-9][a-z0-9_-]{0,63}")
_ART_FILE = re.compile(r"[A-Za-z0-9_.-]{1,80}")
_FRAME_FILE = re.compile(r"[A-Za-z0-9_.-]{1,96}\.png")
_NUMBER = re.compile(r"[0-9]{1,6}")
MAX_RECORD_BYTES = 4 * 1024 * 1024


class PlanError(Exception):
    """A record, frame or option that cannot become a plan; the message names it."""


@dataclass(frozen=True)
class FrameRecord:
    frame: int
    state: str
    file: str
    size: tuple[int, int]


@dataclass(frozen=True)
class Assembly:
    art: str  # target.art of the layout: the census number or a file name
    scale: int
    alpha_mode: str | None  # None: an older record that does not say
    frames: tuple[FrameRecord, ...]
    folder: Path  # where the record is, and the frames with it


@dataclass
class PlannedArt:
    art: str
    art_file: str  # the ART file name as interface.mes gives it
    source: str  # where the name came from: census, --art-name or the art itself
    frames: int
    size: tuple[int, int]  # one HD frame
    masks: bool


@dataclass
class Plan:
    document: dict
    arts: list[PlannedArt]
    files: dict[Path, bytes] = field(default_factory=dict)  # the masks, by path (written with the plan)


# --- the records ------------------------------------------------------------------------------------------------------

def _record_error(path: Path, text: str) -> PlanError:
    return PlanError(f"{path}: {text}")


def load_assembly(path: Path) -> Assembly:
    """Reads one `<art>.assembly.json` (assemble.py's report) and checks its shape; the frames are checked against their files later."""
    try:
        if path.stat().st_size > MAX_RECORD_BYTES:
            raise PlanError(f"{path}: more than {MAX_RECORD_BYTES} bytes")
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise PlanError(f"{path}: {error}") from error
    if not isinstance(data, dict):
        raise _record_error(path, "not an assembly record (an object)")
    art, scale, mode, rows = data.get("art"), data.get("scale"), data.get("alphaMode"), data.get("frames")
    if not isinstance(art, str) or not _ART_FILE.fullmatch(art) or art in (".", ".."):
        raise _record_error(path, "art must be letters, digits, _ . - (a census number or a file name)")
    if isinstance(scale, bool) or not isinstance(scale, int) or not 2 <= scale <= 16:
        raise _record_error(path, "scale must be an integer from 2 to 16 (the importer's range)")
    if mode is not None and mode not in ALPHA_MODES:
        raise _record_error(path, f"alphaMode must be one of {', '.join(ALPHA_MODES)}")
    if not isinstance(rows, list) or not rows:
        raise _record_error(path, "frames must list the assembled frames")
    frames = []
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            raise _record_error(path, f"frames[{index}] is not an object")
        number, state, file, size = row.get("frame"), row.get("state"), row.get("file"), row.get("size")
        if number != index or isinstance(number, bool):
            raise _record_error(path, f"frames[{index}].frame must be {index} (the frames come in order, 0 first)")
        if not isinstance(state, str) or not isinstance(file, str) or not _FRAME_FILE.fullmatch(file):
            raise _record_error(path, f"frames[{index}] needs a state and a plain .png file name")
        if (not isinstance(size, list) or len(size) != 2
                or any(isinstance(v, bool) or not isinstance(v, int) or not 1 <= v <= 16384 for v in size)):
            raise _record_error(path, f"frames[{index}].size must be [width, height]")
        frames.append(FrameRecord(index, state, file, (size[0], size[1])))
    return Assembly(art, scale, mode, tuple(frames), path.resolve().parent)


def find_records(inputs: list[Path]) -> list[Path]:
    """Each input is a record file or a folder holding records; the result is in art order (numbers first), without repeats."""
    found: list[Path] = []
    for item in inputs:
        if item.is_dir():
            records = sorted(item.glob("*" + RECORD_SUFFIX))
            if not records:
                raise PlanError(f"{item}: no *{RECORD_SUFFIX} record (run assemble.py --out on a layout first)")
            found.extend(records)
        elif item.is_file():
            found.append(item)
        else:
            raise PlanError(f"{item}: not a record file or a folder")
    unique = list(dict.fromkeys(p.resolve() for p in found))

    def order(path: Path) -> tuple[int, int, str]:
        head = path.name[: -len(RECORD_SUFFIX)] if path.name.endswith(RECORD_SUFFIX) else path.name
        return (0, int(head), head) if _NUMBER.fullmatch(head) else (1, 0, head)

    return sorted(unique, key=order)


# --- the ART file names -----------------------------------------------------------------------------------------------

def parse_art_names(items: list[str]) -> dict[str, str]:
    """`--art-name 471=Schematics_Button.ART` (repeatable): the art number or name of a record and the ART file it replaces."""
    names: dict[str, str] = {}
    for item in items:
        key, sep, value = item.partition("=")
        if not sep or not _ART_FILE.fullmatch(key) or not _ART_FILE.fullmatch(value) or not value.lower().endswith(".art"):
            raise PlanError(f"--art-name {item!r}: must be ART=File.ART (letters, digits, _ . -)")
        if key in names:
            raise PlanError(f"--art-name {key}: given twice")
        names[key] = value
    return names


def art_file_name(art: str, overrides: dict[str, str], arts: dict[str, census.CensusArt]) -> tuple[str, str]:
    """The ART file name and where it came from. A number is looked up in the census; any other identifier is the file's own name."""
    if art in overrides:
        return overrides[art], "--art-name"
    if _NUMBER.fullmatch(art):
        row = arts.get(art)
        if row is None:
            raise PlanError(f"art {art}: not in the census; give --art-name {art}=File.ART")
        return row.name + ".ART", "census"
    return (art if art.lower().endswith(".art") else art + ".ART"), "art name"


# --- masks ------------------------------------------------------------------------------------------------------------

def encode_grey(width: int, height: int, grey: bytes) -> bytes:
    """An 8-bit grey PNG (what the pilot's masks are): the importer reads white as keep and black as clear. Same pixels, same bytes."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        raw += grey[y * width:(y + 1) * width]
    return (pngio.SIGNATURE + pngio._chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 0, 0, 0, 0))
            + pngio._chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + pngio._chunk(b"IEND", b""))


def mask_of(image: pngio.Image, what: str) -> bytes:
    """The grey mask of an assembled frame: its alpha, at the HD size (the importer takes such a mask as the alpha itself)."""
    grey = bytes(image.data[3::4])
    if not any(grey):
        raise PlanError(f"{what}: no visible pixel, so no alpha mask (the importer refuses a mask that leaves nothing opaque)")
    return grey


def mask_path(art: str, frame: int) -> str:
    return f"{MASK_DIR}/{art}-f{frame:03d}.png"


# --- the plan ---------------------------------------------------------------------------------------------------------

def _relative(path: Path, start: Path) -> str:
    return os.path.relpath(path, start).replace(os.sep, "/")


def build_plan(assemblies: list[Assembly], out_dir: Path, pack_id: str = DEFAULT_PACK_ID, masks: str = "auto",
               overrides: dict[str, str] | None = None, arts: dict[str, census.CensusArt] | None = None) -> Plan:
    """The importer plan and its mask files (not yet written). Every frame is read and checked against its record, and against the census
    row of the art when there is one (frame count and size), so a wrong art or a frame of another scale is refused here and not by the importer."""
    if not _PACK_ID.fullmatch(pack_id):
        raise PlanError(f"--pack-id {pack_id!r}: must be lower-case letters, digits, - and _ (at most 64)")
    if masks not in MASK_MODES:
        raise PlanError(f"--masks: must be one of {', '.join(MASK_MODES)}")
    if not assemblies:
        raise PlanError("no assembly record")
    scales = {a.scale for a in assemblies}
    if len(scales) != 1:
        raise PlanError(f"the records have different scales {sorted(scales)}: one plan holds one scale")
    if len({a.art for a in assemblies}) != len(assemblies):
        raise PlanError("two records for the same art")
    if arts is None:
        arts = {row.ident: row for row in census.load_arts()}
    overrides = overrides or {}
    items, planned, files, taken = [], [], {}, {}
    for assembly in assemblies:
        art_file, source = art_file_name(assembly.art, overrides, arts)
        if art_file.lower() in taken:
            raise PlanError(f"art {assembly.art} and art {taken[art_file.lower()]} both name {art_file}")
        taken[art_file.lower()] = assembly.art
        row = arts.get(assembly.art)
        if row is not None and source == "census":
            if len(assembly.frames) != row.frames:
                raise PlanError(f"art {assembly.art}: {len(assembly.frames)} assembled frames, the census says {row.frames} for {row.name}")
            want = (row.size[0] * assembly.scale, row.size[1] * assembly.scale)
        else:
            want = assembly.frames[0].size
        write_masks = masks == "always" or (masks == "auto" and assembly.alpha_mode in ("masked", None))
        for frame in assembly.frames:
            what = f"art {assembly.art} frame {frame.frame}"
            if frame.size != want:
                raise PlanError(f"{what}: the record says {frame.size[0]}x{frame.size[1]}, expected {want[0]}x{want[1]} (the original's size at x{assembly.scale})")
            png = assembly.folder / frame.file
            try:
                image = pngio.read(png)
            except (OSError, pngio.PngError) as error:
                raise PlanError(f"{what}: {png}: {error}") from error
            if (image.w, image.h) != frame.size:
                raise PlanError(f"{what}: {png} is {image.w}x{image.h}, the record says {frame.size[0]}x{frame.size[1]}")
            if not image.source_alpha:
                raise PlanError(f"{what}: {png} has no alpha channel")
            tile: dict = {"art": art_file, "frame": frame.frame}
            if write_masks:
                relative = mask_path(assembly.art, frame.frame)
                tile["alphaMask"] = relative
                files[out_dir / relative] = encode_grey(image.w, image.h, mask_of(image, what))
            image_ref = _relative(png, out_dir)
            items.append({"image": image_ref, "mode": "ready", "images": [image_ref], "tiles": [tile], "lock": False})
        planned.append(PlannedArt(assembly.art, art_file, source, len(assembly.frames), want, write_masks))
    document = {"schema": 1, "kind": PLAN_KIND, "packId": pack_id, "scale": scales.pop(), "items": items}
    return Plan(document, planned, files)


def check_out_dir(out_dir: Path) -> None:
    """The plan names original ART files and the frames are derived from them: never inside the checkout except its ignored `local/`."""
    real = out_dir.resolve()
    if REPO == real or REPO in real.parents:
        if REPO / "local" != real and REPO / "local" not in real.parents:
            raise PlanError(f"{out_dir} is inside the checkout outside local/: a plan and its frames are local files, never in git")


def write_plan(plan: Plan, out_dir: Path) -> list[Path]:
    """Writes the masks, then the plan last. An existing plan.json is replaced only when it is an importer plan (never an unrelated file)."""
    target = out_dir / PLAN_NAME
    if target.exists() or target.is_symlink():
        try:
            old = json.loads(target.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            old = None
        if target.is_symlink() or not isinstance(old, dict) or old.get("kind") != PLAN_KIND:
            raise PlanError(f"{target} exists and is not an importer plan; give another --out")
    written = []
    try:
        out_dir.mkdir(parents=True, exist_ok=True)
        for path, blob in sorted(plan.files.items()):
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(blob)
            written.append(path)
        target.write_text(json.dumps(plan.document, indent=1) + "\n", encoding="utf-8")
        written.append(target)
    except OSError as error:
        raise PlanError(f"cannot write to {out_dir}: {error}") from error
    return written


def art_map_lines(plan: Plan) -> list[str]:
    return [f"  {item.art} -> {item.art_file}  ({item.frames} frame{'s' if item.frames != 1 else ''} of {item.size[0]}x{item.size[1]}, "
            f"{'alpha masks' if item.masks else 'the original alpha'}; name from {item.source})" for item in plan.arts]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("inputs", nargs="+", type=Path, help="assembly records (<art>.assembly.json) or folders holding them (assemble.py --out DIR)")
    parser.add_argument("--out", type=Path, help="where plan.json and masks/ go (default: the folder of the first record, next to the frames)")
    parser.add_argument("--pack-id", default=DEFAULT_PACK_ID, help=f"id of the staging pack the plan builds (default {DEFAULT_PACK_ID})")
    parser.add_argument("--masks", choices=MASK_MODES, default="auto",
                        help="auto (default): a grey alpha mask for each frame assembled in the masked alpha mode (and for an older record that does not say); "
                             "always, never")
    parser.add_argument("--art-name", action="append", default=[], metavar="ART=File.ART", help="the ART file of an art the census does not name (repeatable)")
    parser.add_argument("--dry-run", action="store_true", help="read and check everything, print the art map, write nothing")
    args = parser.parse_args(argv)
    try:
        records = find_records(args.inputs)
        assemblies = [load_assembly(path) for path in records]
        out_dir = (args.out or assemblies[0].folder).resolve()
        check_out_dir(out_dir)
        plan = build_plan(assemblies, out_dir, args.pack_id, args.masks, parse_art_names(args.art_name))
        written = [] if args.dry_run else write_plan(plan, out_dir)
    except PlanError as error:
        print(f"PACK-PLAN FAIL: {error}", file=sys.stderr)
        return 1
    print("art map (assembled art -> ART file the importer replaces):")
    for line in art_map_lines(plan):
        print(line)
    frames = len(plan.document["items"])
    print(f"plan {out_dir / PLAN_NAME}: {frames} frames of {len(plan.arts)} arts, scale {plan.document['scale']}x, pack id {args.pack_id}, "
          f"{len(plan.files)} alpha masks")
    if args.dry_run:
        print("PACK-PLAN DRY-RUN PASS: nothing written")
    else:
        print(f"PACK-PLAN PASS: wrote {len(written)} files")
    stage = f"python3 -B tools/hd_pack/hd_pack.py stage --plan {out_dir / PLAN_NAME} --staging local/res095/pack-out/{args.pack_id}"
    print(f"next (docs/workflow/hd-pack.md#kit-assembled-frames-in-the-pack-arc-ui-3077):\n  {stage} --dry-run\n  {stage} --fresh\n"
          f"  then hd_pack.py plan --staged <the staging folder> --base INSTALLED, and apply")
    return 0


if __name__ == "__main__":
    sys.exit(main())
