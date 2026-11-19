"""ARC-DEV-2454: validator of the Steamwoven kit manifest `tools/steamwoven_ui/kit.json` (TRD SWI-KIT-1..3, SWI-VER-2).

    python3 -B tools/steamwoven_ui/kit_check.py [--manifest PATH] [--census-tsv PATH] [--census-md PATH] [--no-census]

Checks the manifest alone (unique ids, known kinds including the strip's end cap `cap`, master sizes that are multiples of 6, layers that
name existing parts and make no cycle, known windows, kind rules of the 4:3 composition) and, unless `--no-census`, against the census of
docs/ui: every census art is the source of exactly one part or is listed as excluded, and every window is known and used. The manifest holds
no pixels: only the fields of the schema below, short text and numbers.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tools.steamwoven_ui import census as census_mod  # noqa: E402

MANIFEST = Path(__file__).resolve().parent / "kit.json"
SCHEMA = 1

# The asset kit's kinds (TRD "Asset kit"): `board` is the strip art (2880 px wide), `backdrop` a window background (inside the 2880 x 1440
# zone) or a full-screen plate (2880 x 2160), `cap` the end cap of a strip.
KINDS = ("material", "board", "backdrop", "cap", "hardware", "plate", "glyph", "state", "sheet", "grid", "picture", "gauge", "decor", "cursor")
STRIP_ANCHORS = ("top-strip", "bottom-strip")
CAP_ANCHORS = ("top-strip-left-end", "top-strip-right-end", "bottom-strip-left-end", "bottom-strip-right-end")
ANCHORS = STRIP_ANCHORS + CAP_ANCHORS + ("window-edge", "zone", "composition", "world", "overlay", "cursor", "window", "none")
STATES = ("idle", "hover", "pressed", "disabled")
STATUSES = ("planned", "generated", "master", "baked")
FAMILIES = ("BOARD", "BD-A", "BD-B", "BD-C", "BD-D", "BD-E", "BD-F", "BD-G", "BD-H")
BATCHES = ("B-BOARD", "BD-A", "BD-B", "BD-C", "BD-D", "BD-E", "BD-F", "BD-G", "BD-H", "B-HARDWARE", "B-BUTTONS", "B-GAUGES",
           "B-ICONS-SPELL", "B-ICONS", "B-SCHEMATIC", "B-CURSORS")
SIZE_BASES = ("units", "provisional", "open")
EXCLUDED_REASONS = ("font", "out-of-scope", "unused")
GRID = 6  # SWI-LAY-7: every master length is a multiple of 6 px
COMPOSITION = (2880, 2160)  # master px of the centred 4:3 composition
ZONE = (2880, 1440)  # the window zone

PART_KEYS = ("id", "kind", "size", "size_basis", "anchor", "layers", "states", "used_by", "source_arts", "family", "batch", "provenance",
             "status", "frames", "note")
OPTIONAL_PART_KEYS = ("note",)
TOP_KEYS = ("schema", "windows", "parts", "excluded")
WINDOW_KEYS = ("name", "anchor")
ID_PATTERN = re.compile(r"^[a-z]+\.[a-z0-9_]+(-\d+)?$")
SOURCE_PATTERN = re.compile(r"^(\d+|file:[A-Za-z0-9_.-]+)$")
WINDOW_ID = re.compile(r"^[HGM]\d\d$")
MAX_TEXT = 240  # a longer string is not an identifier or a note: it could carry pixels


def _is_int(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _text_errors(where: str, value: object, errors: list[str]) -> None:
    """AC4: no pixels, so no long strings and no data URIs anywhere in the manifest."""
    if isinstance(value, str):
        if len(value) > MAX_TEXT or value.startswith("data:"):
            errors.append(f"{where}: text of {len(value)} characters looks like data, not an identifier or a note")
    elif isinstance(value, dict):
        for key, item in value.items():
            _text_errors(f"{where}.{key}", item, errors)
    elif isinstance(value, list):
        for index, item in enumerate(value):
            _text_errors(f"{where}[{index}]", item, errors)


def check_manifest(manifest: object, arts: list[census_mod.CensusArt] | None = None,
                   windows: list[census_mod.CensusWindow] | None = None) -> list[str]:
    """All problems of a manifest (empty = valid). `arts` and `windows` switch on the census checks."""
    errors: list[str] = []
    if not isinstance(manifest, dict):
        return ["manifest: not an object"]
    for key in manifest:
        if key not in TOP_KEYS:
            errors.append(f"manifest: unknown key {key!r}")
    for key in TOP_KEYS:
        if key not in manifest:
            errors.append(f"manifest: missing key {key!r}")
    if manifest.get("schema") != SCHEMA:
        errors.append(f"manifest: schema must be {SCHEMA}")
    _text_errors("manifest", manifest, errors)

    window_table = manifest.get("windows")
    known_windows: set[str] = set()
    if not isinstance(window_table, dict):
        errors.append("windows: not an object")
    else:
        for ident, row in window_table.items():
            known_windows.add(ident)
            if not WINDOW_ID.match(ident):
                errors.append(f"windows.{ident}: not a window id (H01, G01, M01 form)")
            if not isinstance(row, dict) or set(row) != set(WINDOW_KEYS) or not all(isinstance(row[k], str) and row[k] for k in WINDOW_KEYS):
                errors.append(f"windows.{ident}: needs exactly the non-empty text fields {list(WINDOW_KEYS)}")
            elif row["anchor"] not in ANCHORS:
                errors.append(f"windows.{ident}: unknown anchor {row['anchor']!r}")

    parts = manifest.get("parts")
    if not isinstance(parts, list):
        errors.append("parts: not a list")
        parts = []
    by_id: dict[str, dict] = {}
    for index, part in enumerate(parts):
        where = f"parts[{index}]"
        if not isinstance(part, dict):
            errors.append(f"{where}: not an object")
            continue
        ident = part.get("id")
        where = f"part {ident!r}" if isinstance(ident, str) else where
        _check_part(part, where, known_windows, errors)
        if isinstance(ident, str):
            if ident in by_id:
                errors.append(f"{where}: duplicate id")
            by_id[ident] = part
    _check_layers(by_id, errors)

    sources: dict[str, str] = {}
    for ident, part in by_id.items():
        for art in part.get("source_arts", []) if isinstance(part.get("source_arts"), list) else []:
            if isinstance(art, str):
                if art in sources:
                    errors.append(f"source art {art}: feeds both {sources[art]} and {ident} (one art is one part, SWI-KIT-3)")
                sources[art] = ident

    excluded = manifest.get("excluded")
    excluded_ids: dict[str, str] = {}
    if not isinstance(excluded, dict):
        errors.append("excluded: not an object")
    else:
        for reason, items in excluded.items():
            if reason not in EXCLUDED_REASONS:
                errors.append(f"excluded: unknown reason {reason!r}")
            if not isinstance(items, list) or not all(isinstance(i, str) and SOURCE_PATTERN.match(i) for i in items):
                errors.append(f"excluded.{reason}: not a list of art identifiers")
                continue
            for art in items:
                if art in sources:
                    errors.append(f"art {art}: excluded ({reason}) and also the source of {sources[art]}")
                if art in excluded_ids:
                    errors.append(f"art {art}: excluded twice")
                excluded_ids[art] = reason
    if arts is not None:
        _check_census_arts(arts, sources, excluded_ids, errors)
    if windows is not None:
        _check_census_windows(windows, window_table if isinstance(window_table, dict) else {}, by_id, errors)
    return errors


def _check_part(part: dict, where: str, known_windows: set[str], errors: list[str]) -> None:
    for key in part:
        if key not in PART_KEYS:
            errors.append(f"{where}: unknown key {key!r}")
    for key in PART_KEYS:
        if key not in part and key not in OPTIONAL_PART_KEYS:
            errors.append(f"{where}: missing key {key!r}")
    ident, kind = part.get("id"), part.get("kind")
    if not isinstance(ident, str) or not ID_PATTERN.match(ident):
        errors.append(f"{where}: id must look like kind.name (lower case, digits, underscore)")
    elif isinstance(kind, str) and not ident.startswith(kind + "."):
        errors.append(f"{where}: id must start with its kind {kind!r}")
    if kind not in KINDS:
        errors.append(f"{where}: unknown kind {kind!r}")

    size, basis = part.get("size"), part.get("size_basis")
    if basis not in SIZE_BASES:
        errors.append(f"{where}: size_basis must be one of {list(SIZE_BASES)}")
    if basis == "open":
        if size is not None:
            errors.append(f"{where}: an open size must be null (the batch issue fixes it)")
    elif not (isinstance(size, list) and len(size) == 2 and all(_is_int(v) for v in size)):
        errors.append(f"{where}: size must be [width, height] in master px")
    else:
        for value, axis in zip(size, "wh"):
            if value <= 0 or value % GRID:
                errors.append(f"{where}: master {axis} {value} is not a positive multiple of {GRID} (SWI-LAY-7, SWI-KIT-2)")
        if size[0] > COMPOSITION[0] or size[1] > COMPOSITION[1]:
            errors.append(f"{where}: master {size[0]} x {size[1]} is larger than the composition {COMPOSITION[0]} x {COMPOSITION[1]}")
        elif kind == "backdrop" and not (size[1] <= ZONE[1] or list(size) == list(COMPOSITION)):
            errors.append(f"{where}: a backdrop is a window background (inside {ZONE[0]} x {ZONE[1]}) or a plate of exactly "
                          f"{COMPOSITION[0]} x {COMPOSITION[1]}")
        if kind == "board" and size[0] != COMPOSITION[0]:
            errors.append(f"{where}: a strip's art is {COMPOSITION[0]} px wide (no full-width board)")

    anchor = part.get("anchor")
    if anchor not in ANCHORS:
        errors.append(f"{where}: unknown anchor {anchor!r}")
    elif kind == "cap" and anchor not in CAP_ANCHORS:
        errors.append(f"{where}: a cap stands at a strip's end ({list(CAP_ANCHORS)})")
    elif kind == "board" and anchor not in STRIP_ANCHORS:
        errors.append(f"{where}: a strip's art anchors to the top or bottom strip")
    elif kind == "backdrop" and isinstance(size, list) and len(size) == 2 and anchor != ("composition" if list(size) == list(COMPOSITION)
                                                                                         else "zone"):
        errors.append(f"{where}: a full-screen plate anchors to the composition and a window background to the zone")
    elif anchor in CAP_ANCHORS and kind != "cap":
        errors.append(f"{where}: only a cap takes a strip-end anchor")

    for key in ("layers", "states", "used_by", "source_arts"):
        value = part.get(key)
        if not isinstance(value, list) or not all(isinstance(v, str) for v in value):
            errors.append(f"{where}: {key} must be a list of text")
        elif len(set(value)) != len(value):
            errors.append(f"{where}: {key} has a duplicate")
    states = part.get("states")
    if isinstance(states, list):
        for state in states:
            if state not in STATES:
                errors.append(f"{where}: unknown state {state!r}")
    used_by = part.get("used_by")
    if isinstance(used_by, list):
        for window in used_by:
            if window not in known_windows:
                errors.append(f"{where}: used by unknown window {window!r}")
    sources = part.get("source_arts")
    if isinstance(sources, list):
        for art in sources:
            if isinstance(art, str) and not SOURCE_PATTERN.match(art):
                errors.append(f"{where}: source art {art!r} is not an art number or file:NAME identifier")
        if kind == "cap" and sources:
            errors.append(f"{where}: an end cap is new and has no source art")

    family, batch = part.get("family"), part.get("batch")
    if family is not None and family not in FAMILIES:
        errors.append(f"{where}: unknown family {family!r}")
    if batch is None:
        if kind != "material":
            errors.append(f"{where}: only a material (the material sheet of #2455) has no batch")
    elif batch not in BATCHES:
        errors.append(f"{where}: unknown batch {batch!r}")

    status, provenance = part.get("status"), part.get("provenance")
    if status not in STATUSES:
        errors.append(f"{where}: status must be one of {list(STATUSES)}")
    elif status == "planned" and provenance is not None:
        errors.append(f"{where}: a planned part has no provenance record yet")
    elif status != "planned" and not (isinstance(provenance, str) and provenance):
        errors.append(f"{where}: a {status} part needs its provenance record id (SWI-GEN-6)")
    if provenance is not None and not isinstance(provenance, str):
        errors.append(f"{where}: provenance must be a record id or null")

    frames = part.get("frames")
    if not (_is_int(frames) and frames >= 1):
        errors.append(f"{where}: frames must be a whole number >= 1 (the original art's frame count; 1 for a new part)")
    if "note" in part and not isinstance(part["note"], str):
        errors.append(f"{where}: note must be text")


def _check_layers(by_id: dict[str, dict], errors: list[str]) -> None:
    for ident, part in by_id.items():
        layers = part.get("layers")
        if not isinstance(layers, list):
            continue
        for layer in layers:
            if layer == ident:
                errors.append(f"part {ident!r}: lists itself as a layer")
            elif layer not in by_id:
                errors.append(f"part {ident!r}: layer {layer!r} names no part")
    # A layer chain must end: depth-first search over the layer graph.
    state: dict[str, int] = {}

    def visit(ident: str) -> bool:
        if state.get(ident) == 2:
            return False
        if state.get(ident) == 1:
            return True
        state[ident] = 1
        layers = by_id[ident].get("layers")
        cyclic = False
        if isinstance(layers, list):
            for layer in layers:
                if isinstance(layer, str) and layer in by_id and layer != ident and visit(layer):
                    cyclic = True
        state[ident] = 2
        return cyclic

    for ident in by_id:
        if state.get(ident) is None and visit(ident):
            errors.append(f"part {ident!r}: the layers make a cycle")


def _check_census_arts(arts: list[census_mod.CensusArt], sources: dict[str, str], excluded: dict[str, str], errors: list[str]) -> None:
    census_ids = set()
    for art in arts:
        census_ids.add(art.ident)
        reason = census_mod.excluded_reason(art)
        if reason is None:
            if art.ident not in sources:
                errors.append(f"census art {art.ident} ({art.name}): not mapped to any part")
        elif excluded.get(art.ident) != reason:
            errors.append(f"census art {art.ident} ({art.name}): must be excluded as {reason!r}, found {excluded.get(art.ident)!r}")
    for art in list(sources) + list(excluded):
        if art not in census_ids:
            errors.append(f"art {art}: not in the census")


def _check_census_windows(windows: list[census_mod.CensusWindow], table: dict, by_id: dict[str, dict], errors: list[str]) -> None:
    census_ids = {w.ident for w in windows}
    for ident in sorted(census_ids - set(table)):
        errors.append(f"window {ident}: in the census, not in the manifest")
    for ident in sorted(set(table) - census_ids):
        errors.append(f"window {ident}: in the manifest, not in the census")
    used = {w for part in by_id.values() if isinstance(part.get("used_by"), list) for w in part["used_by"]}
    for window in windows:
        if window.arts > 0 and window.ident not in used:
            errors.append(f"window {window.ident} ({window.name}): draws {window.arts} arts but no part is used by it")


def load_manifest(path: Path = MANIFEST) -> object:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--manifest", type=Path, default=MANIFEST)
    parser.add_argument("--census-tsv", type=Path, default=census_mod.CENSUS_TSV)
    parser.add_argument("--census-md", type=Path, default=census_mod.CENSUS_MD)
    parser.add_argument("--no-census", action="store_true", help="check the manifest alone")
    args = parser.parse_args(argv)
    try:
        manifest = load_manifest(args.manifest)
    except (OSError, ValueError) as error:
        print(f"KIT CHECK FAIL: cannot read {args.manifest}: {error}")
        return 1
    arts = windows = None
    if not args.no_census:
        arts, windows = census_mod.load_arts(args.census_tsv), census_mod.load_windows(args.census_md)
    errors = check_manifest(manifest, arts, windows)
    if errors:
        for line in errors[:60]:
            print(line)
        print(f"KIT CHECK FAIL: {len(errors)} problem(s)" + (" (first 60 shown)" if len(errors) > 60 else ""))
        return 1
    parts = manifest["parts"]
    kinds: dict[str, int] = {}
    for part in parts:
        kinds[part["kind"]] = kinds.get(part["kind"], 0) + 1
    print("KIT CHECK PASS: " + f"{len(parts)} parts (" + ", ".join(f"{k} {n}" for k, n in sorted(kinds.items())) + f"), "
          f"{len(manifest['windows'])} windows" + ("" if args.no_census else f", census {len(arts)} arts mapped"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
