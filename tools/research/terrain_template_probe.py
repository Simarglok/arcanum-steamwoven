#!/usr/bin/env python3
"""ARC-RES-034 read-only probe: generated wilderness (terrain.tdf and terrain templates).

Research only, not a loader or CE translation. The rules are independently
written from the pinned CE locators cited in
docs/research/generated-wilderness-evidence.md. Original mode reads the owner's
installation in memory and prints only numbers, map and template folder names
(DAT member paths), counts and SHA-256 digests; sector payloads, object
records and MES colours never leave the process. Self-test inputs are invented.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
import re
import struct
import sys
import tempfile
import zlib

import dat_probe

PIN = "a7ff41b300ef712f0e7d088183a3d08957110cdd"

# terrain.tdf (CE terrain.c TerrainHeader): float version, u32 flags, i64 width,
# i64 height, i32 base type, i32 padding. Version 1.2 compared as the float32 word.
TDF_HEADER = struct.Struct("<4sIqqii")
TDF_VERSION = struct.pack("<f", 1.2)
TDF_COMPRESSED = 0x1
MAX_MAP_SECTORS = 4096  # per axis; the main map is 2000
MAX_TDF_BYTES = 16 * 1024 * 1024
MAX_SECTOR_BYTES = 1024 * 1024
MAX_MES_BYTES = 64 * 1024

TERRAIN_MES = "terrain/terrain.mes"
TERRAIN_TYPE_MAX = 32
FIRST_TRANSITION_MESSAGE = 200
VARIANTS = 4

# Template slot (x, y) inside a "<from> to <to>" folder for each 4-bit transition
# code; None means "swap the pair and use code 15 - c" (CE terrain_sector_path and
# its 16-entry table). Codes 0 and 15 have no slot either way.
TRANSITION_SLOTS: list[tuple[int, int] | None] = [
    None, (1, 0), (1, 2), (1, 1), (3, 2), (0, 0), (2, 2), None,
    (3, 0), (2, 0), None, None, (3, 1), None, None, None,
]

SECTOR_X_MASK = 0x3FFFFFF
TILES = 4096
LIGHT_RECORD = 48
TILE_SCRIPT_RECORD = 24
MAX_LIGHTS = 1024
MAX_TILE_SCRIPTS = 4096
MAX_OBJECTS = 4096
PLACEHOLDER_MIN, PLACEHOLDER_MAX = 0xAA0000, 0xAA0004
OBJECT_VERSION = 119

# Mount order, first wins (docs/research/content-precedence.md). The game folder
# itself is answered by targeted case-insensitive lookups instead of a full walk.
MOUNTS = [("module_folder", "modules/Arcanum", "folder"), ("module_patch0", "modules/Arcanum.PATCH0", "dat"),
          ("module_dat", "modules/Arcanum.dat", "dat"), ("data_folder", "data", "folder"),
          ("arcanum4", "Arcanum4.dat", "dat"), ("arcanum3", "arcanum3.dat", "dat"),
          ("arcanum2", "arcanum2.dat", "dat"), ("arcanum1", "arcanum1.dat", "dat"),
          ("game_folder", ".", "root"), ("tig", "tig.dat", "dat")]


class ProbeFailure(ValueError):
    """Only authored static codes, never input-derived messages."""


def require(value: bool, code: str) -> None:
    if not value:
        raise ProbeFailure(code)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


# ------------------------------------------------------------ v119 object walker
# Project wire descriptors (src/Arcanum.Data/StaticObjects/StaticObjectLoader.cs);
# only enough to delimit records and read type, own-ID kind and location.
def kinds(spec: str) -> list[str]:
    out: list[str] = []
    for token in spec.split(","):
        name, _, count = token.partition("*")
        out.extend([name] * (int(count) if count else 1))
    return out


COMMON = kinds("i,p64,i*3,au32*3,i*7,au32*3,i*12,ai32,ascript,i*2,au32,au64")
FIRST = {0: kinds("i*3,au32,au64"), 1: kinds("i*6,au32,au64"), 2: kinds("i*4,aoid,i*4,au32,au64"),
         3: kinds("i,poid,i*2,au32,au64"), 4: kinds("i*3,poid,i*2,au32,au64"),
         15: kinds("i*2,ai32*3,au32,i*4,au32*2,poid,i,poid*5,i,aoid,i*2,aoid,p64,i*6,au32,au64"),
         17: kinds("i*3,au32,au64")}
ITEM = kinds("i,poid,i*19,au32,au64")
SECOND = {5: kinds("i*4,ai32*3,i*18,au32,au64"), 6: kinds("i*5,au32,au64"), 7: kinds("i*4,ai32*2,i*4,au32,au64"),
          8: kinds("i*4,au32,au64"), 9: kinds("i*3,au32,au64"), 10: kinds("i*3,au32,au64"),
          11: kinds("i*3,au32,au64"), 12: kinds("i,au32,i*2,au32,au64"), 13: kinds("i*6,au32,au64"),
          14: kinds("i*3,au32,au64"),
          15: kinds("i*2,au32,au64,i*2,aquest,au32,au64,au32,au64,i,au64,au32*3,i,s,i,au32*2,i*2,au32,au64"),
          16: kinds("i,poid,i,poid*2,i*2,au64,i,p64*2,i*3,poid,i*2,aoid,au32*2,i*3,au32,aoid")}
ARRAY_WIDTH = {"ai32": 4, "au32": 4, "ai64": 8, "au64": 8, "ascript": 12, "aquest": 16, "aoid": 24}
LOCATION_SLOT = 1


def first_fields(object_type: int) -> list[str]:
    if 5 <= object_type <= 14:
        return ITEM
    return FIRST[15 if object_type == 16 else object_type]


def mask_lengths(object_type: int) -> list[int]:
    first, second = first_fields(object_type), SECOND.get(object_type, [])
    lengths = [32, len(COMMON) - 32, min(32, len(first))]
    if object_type in (15, 16):
        lengths += [len(first) - 32, len(second)]
    elif 5 <= object_type <= 14:
        lengths.append(len(second))
    return lengths


class Cursor:
    def __init__(self, data: bytes, start: int, end: int):
        self.data, self.pos, self.end = data, start, end

    def take(self, length: int) -> bytes:
        require(0 <= length <= self.end - self.pos, "ObjectTruncated")
        chunk = self.data[self.pos:self.pos + length]
        self.pos += length
        return chunk

    def i16(self) -> int:
        return struct.unpack("<h", self.take(2))[0]

    def i32(self) -> int:
        return struct.unpack("<i", self.take(4))[0]

    def u32(self) -> int:
        return struct.unpack("<I", self.take(4))[0]

    def present(self) -> bool:
        flag = self.take(1)[0]
        require(flag <= 1, "ObjectPresenceByte")
        return flag == 1


def read_object(cursor: Cursor) -> dict:
    """Delimit one v119 instance record; return type, own-ID kind and location."""
    require(cursor.i32() == OBJECT_VERSION, "ObjectVersion")
    parent = cursor.take(24)
    own = cursor.take(24)
    require(struct.unpack_from("<h", parent)[0] in (0, 1, 2, 3), "ObjectParentKind")
    own_kind = struct.unpack_from("<h", own)[0]
    require(own_kind in (0, 1, 2, 3), "ObjectOwnKind")
    object_type = cursor.i32()
    require(0 <= object_type <= 17, "ObjectType")
    fields = COMMON + first_fields(object_type) + SECOND.get(object_type, [])
    count = cursor.i16()
    selected: list[int] = []
    start = 0
    for length in mask_lengths(object_type):
        mask = cursor.u32()
        require(mask & ~((1 << length) - 1) == 0, "ObjectMaskBits")
        selected += [start + bit for bit in range(length) if mask >> bit & 1]
        start += length
    require(start == len(fields) and count == len(selected), "ObjectFieldCount")
    location = None
    for slot in selected:
        kind = fields[slot]
        if kind == "i":
            cursor.take(4)
        elif not cursor.present():
            continue
        elif kind == "p64":
            value = struct.unpack("<q", cursor.take(8))[0]
            if slot == LOCATION_SLOT:
                location = value
        elif kind == "poid":
            cursor.take(24)
        elif kind == "s":
            length = cursor.i32()
            require(0 <= length <= 4096, "ObjectString")
            require(cursor.take(length + 1)[-1] == 0, "ObjectString")
        else:
            width, elements, _ = cursor.i32(), cursor.i32(), cursor.i32()
            require(width == ARRAY_WIDTH[kind] and 0 <= elements <= 65536, "ObjectArray")
            cursor.take(width * elements)
            words = cursor.i32()
            require(1 <= words <= 65536, "ObjectArrayBits")
            bits = cursor.take(4 * words)
            population = sum(bin(word).count("1") for word in struct.unpack(f"<{words}I", bits))
            require(population == elements, "ObjectArrayBits")
    return {"type": object_type, "own_kind": own_kind, "location": location}


# --------------------------------------------------------------- MES and types
def parse_mes(data: bytes) -> list[tuple[int, bytes]]:
    """Messages in file order. Sufficient for terrain.mes: `{num}{text}` pairs."""
    require(len(data) <= MAX_MES_BYTES, "MesTooLarge")
    messages = [(int(number), text) for number, text in re.findall(rb"\{(\d+)\}\{([^}]*)\}", data)]
    require(len({number for number, _ in messages}) == len(messages), "MesDuplicateNumber")
    return messages


@dataclass(frozen=True)
class TerrainTypes:
    names: list[str]          # index = terrain type, "/b" suffix removed
    blocked: list[bool]
    transitions: list[tuple[int, int]]


def terrain_types(data: bytes) -> TerrainTypes:
    by_number = dict(parse_mes(data))
    names, blocked = [], []
    for number in range(TERRAIN_TYPE_MAX):  # consecutive from 0, as the original counts them
        if number not in by_number:
            break
        text = by_number[number].decode("ascii")
        cut = text.find("/b")
        blocked.append(cut >= 0)
        names.append(text[:cut].rstrip() if cut >= 0 else text)
    require(names, "NoTerrainTypes")
    lowered = [name.lower() for name in names]
    transitions = []
    for number, text in parse_mes(data):
        if number < FIRST_TRANSITION_MESSAGE:
            continue
        left, separator, right = text.decode("ascii").partition(" to ")
        require(separator != "" and left.lower() in lowered and right.lower() in lowered, "TransitionUnparsed")
        transitions.append((lowered.index(left.lower()), lowered.index(right.lower())))
    require(transitions, "NoTransitions")
    return TerrainTypes(names, blocked, transitions)


# ---------------------------------------------------------------- terrain.tdf
@dataclass(frozen=True)
class TerrainMap:
    flags: int
    width: int
    height: int
    base_type: int
    padding: int
    tids: list[list[int]]  # [y][x]


def decode_tdf(data: bytes) -> TerrainMap:
    require(len(data) >= TDF_HEADER.size, "TdfTruncatedHeader")
    version, flags, width, height, base_type, padding = TDF_HEADER.unpack_from(data)
    require(version == TDF_VERSION, "TdfVersion")
    require(1 <= width <= MAX_MAP_SECTORS and 1 <= height <= MAX_MAP_SECTORS, "TdfDimensions")
    require(flags & ~TDF_COMPRESSED == 0, "TdfUnknownFlags")
    rows: list[list[int]] = []
    position = TDF_HEADER.size
    if flags & TDF_COMPRESSED:
        for _ in range(height):
            require(position + 4 <= len(data), "TdfRowTruncated")
            size = struct.unpack_from("<I", data, position)[0]
            position += 4
            require(1 <= size <= len(data) - position, "TdfRowTruncated")
            inflater = zlib.decompressobj()
            try:
                row = inflater.decompress(data[position:position + size], 2 * width + 1)
            except zlib.error as error:
                raise ProbeFailure("TdfRowZlib") from error
            require(len(row) == 2 * width and inflater.eof and not inflater.unused_data
                    and not inflater.unconsumed_tail, "TdfRowLength")
            rows.append(list(struct.unpack(f"<{width}H", row)))
            position += size
        require(position == len(data), "TdfTrailingBytes")
    else:
        require(len(data) - position == 2 * width * height, "TdfFlatLength")
        flat = struct.unpack_from(f"<{width * height}H", data, position)
        rows = [list(flat[y * width:(y + 1) * width]) for y in range(height)]
    return TerrainMap(flags, width, height, base_type, padding, rows)


def tid_fields(tid: int) -> tuple[int, int, int, int]:
    """base, second, transition code, variant."""
    return tid >> 11 & 31, tid >> 6 & 31, tid >> 2 & 15, tid & 3


def sector_id(x: int, y: int) -> int:
    return x | y << 26


def template_path(tid: int, types: TerrainTypes) -> tuple[str, str | None]:
    """(kind, DAT path) for a sector without its own file. Kinds: plain, transition,
    no_slot (the original builds a "-1.sec" name), invalid_type (the original has no name)."""
    base, second, code, variant = tid_fields(tid)
    if base >= len(types.names):
        return "invalid_type", None
    if base == second or code == 0:
        slot = sector_id(variant % 2, variant // 2)
        return "plain", f"terrain/{types.names[base].lower()}/{slot}.sec"
    if second >= len(types.names):
        return "invalid_type", None
    origin, target, slot_xy = base, second, TRANSITION_SLOTS[code]
    if slot_xy is None:
        origin, target, slot_xy = second, base, TRANSITION_SLOTS[15 - code]
    folder = f"terrain/{types.names[origin].lower()} to {types.names[target].lower()}"
    if slot_xy is None:
        return "no_slot", f"{folder}/-1.sec"
    return "transition", f"{folder}/{sector_id(*slot_xy)}.sec"


def terrain_blocked(tid: int, types: TerrainTypes) -> bool:
    """World-map travel block of a sector without its own file (CE terrain_is_blocked)."""
    if tid == 0xFFFF:
        return True
    base, second, _, _ = tid_fields(tid)
    return any(index < len(types.blocked) and types.blocked[index] for index in (base, second))


def relocate_object(template_location: int, real_sector: int) -> tuple[int, int]:
    """Object rule: keep the tile-local x/y (low six bits), take the real sector's origin."""
    x, y = template_location & 0xFFFFFFFF, template_location >> 32 & 0xFFFFFFFF
    return ((real_sector & SECTOR_X_MASK) << 6) + (x & 63), ((real_sector >> 26 & SECTOR_X_MASK) << 6) + (y & 63)


def relocate_light_as_written(template_location: int, real_sector: int) -> tuple[int, int]:
    """CE's light-list loader as written at the pin (marked "TODO: Check" there): it adds the
    unshifted sector x/y to a local tile index instead of using the object rule."""
    x, y = template_location & 0xFFFFFFFF, template_location >> 32 & 0xFFFFFFFF
    tile = (x & 63) | (y & 63) << 6
    return (tile & SECTOR_X_MASK) + (real_sector & SECTOR_X_MASK), (tile >> 26) + (real_sector >> 26 & SECTOR_X_MASK)


# ---------------------------------------------------------------- sector framing
def frame_sector(data: bytes) -> dict:
    """Base-stream sections of one sector file (docs/research/crash-site-framing.md order)."""
    require(len(data) <= MAX_SECTOR_BYTES, "SectorTooLarge")
    cursor = Cursor(data, 0, len(data))
    light_count = cursor.i32()
    require(0 <= light_count <= MAX_LIGHTS, "LightCount")
    lights = [struct.unpack_from("<qq", cursor.take(LIGHT_RECORD)) for _ in range(light_count)]  # (owner handle, location)
    cursor.take(4 * TILES)
    roof_empty = cursor.i32()
    if roof_empty == 0:
        cursor.take(4 * 256)
    placeholder = cursor.i32()
    require(PLACEHOLDER_MIN <= placeholder <= PLACEHOLDER_MAX, "Placeholder")
    tile_scripts, sector_script, metadata, sound, blocks = [], None, None, None, None
    if placeholder != PLACEHOLDER_MIN:
        count = cursor.i32()
        require(0 <= count <= MAX_TILE_SCRIPTS, "TileScriptCount")
        for _ in range(count):
            record = cursor.take(TILE_SCRIPT_RECORD)
            tile_scripts.append((struct.unpack_from("<I", record, 4)[0], struct.unpack_from("<i", record, 16)[0]))
        if placeholder >= 0xAA0002:
            sector_script = struct.unpack_from("<i", cursor.take(12), 8)[0]
        if placeholder >= 0xAA0003:
            metadata = struct.unpack("<3i", cursor.take(12))
            sound = cursor.take(12)
        if placeholder >= 0xAA0004:
            blocks = cursor.take(4 * 128)
    require(len(data) - cursor.pos >= 4, "ObjectCountTruncated")
    object_count = struct.unpack_from("<i", data, len(data) - 4)[0]
    require(0 <= object_count <= MAX_OBJECTS, "ObjectCount")
    objects = Cursor(data, cursor.pos, len(data) - 4)
    records = [read_object(objects) for _ in range(object_count)]
    require(objects.pos == objects.end, "ObjectWindowRemainder")
    return {"lights": lights, "roof_empty": roof_empty, "placeholder": placeholder, "tile_scripts": tile_scripts,
            "sector_script": sector_script, "metadata": metadata, "sound_set": sound is not None and any(sound),
            "blocked_tiles": sum(bin(b).count("1") for b in blocks) if blocks else 0, "objects": records}


# ------------------------------------------------------------------ sources
def find_case_insensitive(root: Path, relative: str) -> Path | None:
    current = root
    for part in relative.split("/"):
        if part in ("", "."):
            continue
        if not current.is_dir():
            return None
        matches = [child for child in current.iterdir() if child.name.lower() == part.lower()]
        if len(matches) != 1:
            return None
        current = matches[0]
    return current


def read_capped_entry(archive: dat_probe.Archive, entry: dat_probe.Entry, cap: int) -> bytes:
    """dat_probe.read_bounded_entry with an explicit per-call cap (terrain.tdf is above 1 MiB)."""
    require(not entry.is_directory and 0 < entry.stored_size <= cap and entry.unpacked_size <= cap, "EntryCap")
    with archive.path.open("rb") as stream:
        stream.seek(entry.offset)
        stored = stream.read(entry.stored_size)
    require(len(stored) == entry.stored_size, "EntryTruncated")
    if not entry.is_compressed:
        require(entry.stored_size == entry.unpacked_size, "EntryPlainSize")
        return stored
    inflater = zlib.decompressobj()
    try:
        output = inflater.decompress(stored, entry.unpacked_size + 1)
    except zlib.error as error:
        raise ProbeFailure("EntryZlib") from error
    require(len(output) == entry.unpacked_size and inflater.eof and not inflater.unused_data
            and not inflater.unconsumed_tail, "EntryLength")
    return output


class Source:
    def __init__(self, role: str, path: Path, kind: str):
        self.role, self.path, self.kind = role, path, kind
        self.archive = dat_probe.parse_archive(path) if kind == "dat" else None
        self.names: dict[str, list] = {}
        self._lookups: dict[str, Path | None] = {}
        if self.archive is not None:
            for entry in self.archive.entries:
                if not entry.is_directory:
                    name = entry.raw_name.rstrip(b"\0").decode("latin-1").replace("\\", "/").lower()
                    self.names.setdefault(name, []).append(entry)
        elif kind == "folder":
            for file in path.rglob("*"):
                if file.is_file():
                    self.names.setdefault(file.relative_to(path).as_posix().lower(), []).append(file)

    def has(self, name: str) -> bool:
        if self.kind != "root":
            return name in self.names
        if name not in self._lookups:
            hit = find_case_insensitive(self.path, name)
            self._lookups[name] = hit if hit is not None and hit.is_file() else None
        return self._lookups[name] is not None

    def under(self, folder: str) -> list[str]:
        """Member names directly in `folder/` (lowercase)."""
        if self.kind != "root":
            prefix = folder + "/"
            return [n for n in self.names if n.startswith(prefix) and "/" not in n[len(prefix):]]
        directory = find_case_insensitive(self.path, folder)
        if directory is None or not directory.is_dir():
            return []
        return [f"{folder}/{child.name.lower()}" for child in directory.iterdir() if child.is_file()]

    def read(self, name: str, cap: int) -> bytes:
        require(self.has(name), "MissingEntry")
        if self.kind == "root":
            path = self._lookups[name]
            require(path is not None and path.stat().st_size <= cap, "LooseFileCap")
            return path.read_bytes()
        hits = self.names[name]
        require(len(hits) == 1, "AmbiguousEntry")
        if self.archive is not None:
            return read_capped_entry(self.archive, hits[0], cap)
        require(hits[0].stat().st_size <= cap, "LooseFileCap")
        return hits[0].read_bytes()


def open_sources(root: Path) -> list[Source]:
    sources = []
    for role, relative, kind in MOUNTS:
        path = root if relative == "." else find_case_insensitive(root, relative)
        if path is None or (kind == "dat") != path.is_file():
            continue
        sources.append(Source(role, path, kind))
    require(any(s.kind == "dat" for s in sources), "NoArchives")
    return sources


class Stack:
    def __init__(self, sources: list[Source]):
        self.sources = sources
        self._winner: dict[str, Source | None] = {}

    def winner(self, name: str) -> Source | None:
        if name not in self._winner:
            self._winner[name] = next((s for s in self.sources if s.has(name)), None)
        return self._winner[name]

    def read(self, name: str, cap: int) -> tuple[str, bytes]:
        source = self.winner(name)
        require(source is not None, "MissingEntry")
        return source.role, source.read(name, cap)

    def all_names(self) -> set[str]:
        return {n for s in self.sources if s.kind != "root" for n in s.names}


# ------------------------------------------------------------------- report
SECTOR_FILE = re.compile(r"^maps/([^/]+)/(\d+)\.sec$")
TEMPLATE_FILE = re.compile(r"^terrain/([^/]+)/(\d+)\.sec$")


def variant_counts(stack: Stack, types: TerrainTypes) -> list[int]:
    counts = []
    for name in types.names:
        count = 0
        for variant in range(VARIANTS):  # the original stops at the first missing variant
            if stack.winner(f"terrain/{name.lower()}/{sector_id(variant % 2, variant // 2)}.sec") is None:
                break
            count += 1
        counts.append(count)
    return counts


def survey_map(stack: Stack, map_folder: str, types: TerrainTypes, neighbours: bool) -> tuple[dict, Counter]:
    role, data = stack.read(f"maps/{map_folder}/terrain.tdf", MAX_TDF_BYTES)
    terrain = decode_tdf(data)
    own: set[int] = set()
    for source in stack.sources:
        for name in source.under(f"maps/{map_folder}"):
            match = SECTOR_FILE.match(name)
            if match:
                own.add(int(match.group(2)))
    resolved, by_source, used, missing, blocked = Counter(), Counter(), Counter(), Counter(), Counter()
    fields = [Counter(), Counter(), Counter(), Counter()]
    for y, row in enumerate(terrain.tids):
        for x, tid in enumerate(row):
            for index, value in enumerate(tid_fields(tid)):
                fields[index][value] += 1
            if sector_id(x, y) in own:
                resolved["own_file"] += 1
                continue
            kind, path = template_path(tid, types)
            if terrain_blocked(tid, types):
                blocked["generated_world_map_blocked"] += 1
            if path is None or stack.winner(path) is None:
                resolved[f"{kind}_fill"] += 1
                missing[path or kind] += 1
                continue
            resolved[kind] += 1
            used[path] += 1
            by_source[stack.winner(path).role] += 1
    inside = {i for i in own if (i & SECTOR_X_MASK) < terrain.width and (i >> 26) < terrain.height}
    base, second, code, variant = fields
    pure = sum(1 for row in terrain.tids for tid in row if tid_fields(tid)[0] == tid_fields(tid)[1])
    report = {
        "tdf_source": role, "tdf_bytes": len(data), "tdf_sha256": sha(data), "flags": terrain.flags,
        "width": terrain.width, "height": terrain.height, "header_base_type": terrain.base_type,
        "header_padding": terrain.padding, "own_sector_files": len(own), "own_files_outside_grid": len(own - inside),
        "resolution": dict(sorted(resolved.items())), "template_sources": dict(sorted(by_source.items())),
        "distinct_templates_used": len(used), "missing_templates": dict(sorted(missing.items())),
        "world_map_blocked_generated": blocked["generated_world_map_blocked"],
        "base_types": {str(k): v for k, v in sorted(base.items())},
        "second_types": {str(k): v for k, v in sorted(second.items())},
        "transition_codes": {str(k): v for k, v in sorted(code.items())},
        "variants": {str(k): v for k, v in sorted(variant.items())},
        "base_equals_second": pure,
        "code_zero_with_different_types": sum(1 for row in terrain.tids for tid in row
                                              if tid_fields(tid)[2] == 0 and tid_fields(tid)[0] != tid_fields(tid)[1]),
        "nonzero_code_with_equal_types": sum(1 for row in terrain.tids for tid in row
                                             if tid_fields(tid)[2] != 0 and tid_fields(tid)[0] == tid_fields(tid)[1]),
    }
    if neighbours:
        report["transition_neighbours"] = neighbour_table(terrain)
    return report, used


def neighbour_table(terrain: TerrainMap) -> dict:
    """Per transition code: share of the eight neighbours that are a pure sector of the
    code's second type (sector x grows right, y grows down in the grid)."""
    offsets = [(dx, dy) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if (dx, dy) != (0, 0)]
    hits: dict[int, list[list[int]]] = {}
    for y in range(1, terrain.height - 1):
        for x in range(1, terrain.width - 1):
            base, second, code, _ = tid_fields(terrain.tids[y][x])
            if base == second:
                continue
            table = hits.setdefault(code, [[0, 0] for _ in offsets])
            for index, (dx, dy) in enumerate(offsets):
                n_base, n_second, _, _ = tid_fields(terrain.tids[y + dy][x + dx])
                table[index][1] += 1
                table[index][0] += n_base == second and n_second == second
    return {f"{code:04b}": {f"{dx:+d},{dy:+d}": round(h / t, 2) for (dx, dy), (h, t) in zip(offsets, table)}
            for code, table in sorted(hits.items())}


def census(stack: Stack, names: list[str], used: Counter) -> dict:
    totals = {"all": Counter(), "used": Counter()}
    types = {"all": Counter(), "used": Counter()}
    kinds_ = {"all": Counter(), "used": Counter()}
    light_rules, scripts = Counter(), Counter()
    for name in names:
        match = TEMPLATE_FILE.match(name)
        slot_x, slot_y = int(match.group(2)) & SECTOR_X_MASK, int(match.group(2)) >> 26
        _, data = stack.read(name, MAX_SECTOR_BYTES)
        frame = frame_sector(data)
        groups = ["all"] + (["used"] if name in used else [])
        for group in groups:
            t = totals[group]
            t["sectors"] += 1
            t["lights"] += len(frame["lights"])
            t["sectors_with_lights"] += bool(frame["lights"])
            t["roofs"] += frame["roof_empty"] == 0
            t[f"placeholder_{frame['placeholder']:x}"] += 1
            t["tile_scripts"] += len(frame["tile_scripts"])
            t["tile_scripts_attached"] += sum(1 for _, number in frame["tile_scripts"] if number)
            t["sector_scripts_attached"] += bool(frame["sector_script"])
            t["aptitude_adjustment_set"] += bool(frame["metadata"] and frame["metadata"][1])
            t["lights_with_owner_handle"] += sum(1 for owner, _ in frame["lights"] if owner)
            t["townmap_or_light_scheme_set"] += bool(frame["metadata"] and (frame["metadata"][0] or frame["metadata"][2]))
            t["sound_set"] += frame["sound_set"]
            t["blocked_tiles"] += frame["blocked_tiles"]
            t["objects"] += len(frame["objects"])
            t["sectors_without_objects"] += not frame["objects"]
            for record in frame["objects"]:
                types[group][str(record["type"])] += 1
                kinds_[group][str(record["own_kind"])] += 1
                location = record["location"]
                if location is None:
                    t["objects_without_location"] += 1
                    continue
                lx, ly = location & 0xFFFFFFFF, location >> 32 & 0xFFFFFFFF
                t["objects_in_own_slot" if (lx >> 6, ly >> 6) == (slot_x, slot_y) else "objects_outside_own_slot"] += 1
            if group == "all" and frame["sector_script"]:
                scripts[str(frame["sector_script"])] += 1
            for _, location in frame["lights"]:
                lx, ly = location & 0xFFFFFFFF, location >> 32 & 0xFFFFFFFF
                if group == "all":
                    light_rules["lights_in_own_slot" if (lx >> 6, ly >> 6) == (slot_x, slot_y) else "lights_outside_own_slot"] += 1
                    probe = sector_id(1451, 1302)
                    light_rules["object_rule_differs_from_as_written"] += (
                        relocate_object(location, probe) != relocate_light_as_written(location, probe))
    return {"totals": {g: dict(sorted(c.items())) for g, c in totals.items()},
            "object_types": {g: dict(sorted(c.items(), key=lambda i: int(i[0]))) for g, c in types.items()},
            "own_id_kinds": {g: dict(sorted(c.items())) for g, c in kinds_.items()},
            "lights": dict(sorted(light_rules.items())), "sector_script_numbers": dict(sorted(scripts.items()))}


def survey(root: Path, main_map: str) -> dict:
    sources = open_sources(root)
    stack = Stack(sources)
    _, mes = stack.read(TERRAIN_MES, MAX_MES_BYTES)
    types = terrain_types(mes)
    names = stack.all_names()
    template_names = sorted(n for n in names if TEMPLATE_FILE.match(n))
    folders = Counter(TEMPLATE_FILE.match(n).group(1) for n in template_names)
    transition_folders = {f"{types.names[a].lower()} to {types.names[b].lower()}" for a, b in types.transitions}
    transition_folders |= {f"{types.names[b].lower()} to {types.names[a].lower()}" for a, b in types.transitions}
    plain_folders = {name.lower() for name in types.names}
    referenced = {slot for slot in TRANSITION_SLOTS if slot is not None}
    maps = sorted({m.group(1) for n in names if (m := re.match(r"^maps/([^/]+)/terrain\.tdf$", n))})
    require(main_map.lower() in maps, "MainMapMissing")
    per_map, used_all = {}, Counter()
    for folder in maps:
        report, used = survey_map(stack, folder, types, folder == main_map.lower())
        per_map[folder] = report
        used_all.update(used)
    generated = {m: r for m, r in per_map.items() if r["width"] * r["height"] > r["own_sector_files"]}
    slot_use = Counter()
    for path, count in used_all.items():
        number = int(path.rsplit("/", 1)[1][:-4])
        slot_use[f"{number & SECTOR_X_MASK},{number >> 26}"] += count
    report = {
        "result": "PASS", "pin": PIN,
        "mounts": [{"role": s.role, "kind": s.kind} for s in sources],
        "terrain_mes_sha256": sha(mes),
        "terrain_types": [{"type": i, "name": n, "world_map_blocked": b, "plain_variants": v}
                          for i, (n, b, v) in enumerate(zip(types.names, types.blocked, variant_counts(stack, types)))],
        "transition_messages": len(types.transitions),
        "template_folders": len(folders), "template_sector_files": len(template_names),
        "template_folders_not_named_by_mes": sorted(f for f in folders if f not in plain_folders | transition_folders),
        "template_folders_named_by_mes_missing": sorted(f for f in plain_folders | transition_folders if f not in folders),
        "transition_folders_missing_slots": {
            f: sorted(f"{x},{y}" for x, y in referenced if f"terrain/{f}/{sector_id(x, y)}.sec" not in template_names)
            for f in sorted(folders) if " to " in f
            and any(f"terrain/{f}/{sector_id(x, y)}.sec" not in template_names for x, y in referenced)},
        "transition_folder_unreferenced_slot_files": sum(
            1 for n in template_names if " to " in n and
            (int(n.rsplit("/", 1)[1][:-4]) & SECTOR_X_MASK, int(n.rsplit("/", 1)[1][:-4]) >> 26) not in referenced),
        "maps_with_terrain": len(maps), "maps_with_generated_sectors": len(generated),
        "main_map": main_map.lower(), "main": per_map[main_map.lower()],
        "other_maps_generated": {m: {k: r[k] for k in ("width", "height", "own_sector_files", "resolution", "header_base_type", "base_types")}
                                 for m, r in sorted(generated.items()) if m != main_map.lower()},
        "templates_used_by_any_map": len(used_all), "template_slot_use": dict(sorted(slot_use.items())),
        "template_census": census(stack, template_names, used_all),
    }
    report["report_sha256"] = sha(json.dumps(report, sort_keys=True).encode())
    return report


def original(root: Path, main_map: str) -> dict:
    dats = [s for s in open_sources(root) if s.kind == "dat"]
    before = {s.role: (s.path.stat().st_size, dat_probe.hash_file(s.path)) for s in dats}
    report = survey(root, main_map)
    after = {s.role: (s.path.stat().st_size, dat_probe.hash_file(s.path)) for s in dats}
    require(before == after, "InputChanged")
    report["inputs"] = {role: {"bytes": size, "sha256": digest} for role, (size, digest) in sorted(before.items())}
    return report


# ---------------------------------------------------------------- self-test
def synthetic_object(object_type: int, location: int | None, own_kind: int = 0) -> bytes:
    fields = COMMON + first_fields(object_type) + SECOND.get(object_type, [])
    selected = [0] + ([LOCATION_SLOT] if location is not None else [])
    out = bytearray(struct.pack("<i", OBJECT_VERSION))
    out += struct.pack("<hxxxxxxi12x", 1, 7)              # parent: prototype-addressable ID
    out += struct.pack("<h22x", own_kind)
    out += struct.pack("<ih", object_type, len(selected))
    start = 0
    for length in mask_lengths(object_type):
        out += struct.pack("<I", sum(1 << (s - start) for s in selected if start <= s < start + length))
        start += length
    assert start == len(fields)
    out += struct.pack("<i", 0x40000000)
    if location is not None:
        out += b"\1" + struct.pack("<q", location)
    return bytes(out)


def synthetic_sector(lights: list[int], objects: list[bytes], tile_script: int = 0) -> bytes:
    out = bytearray(struct.pack("<i", len(lights)))
    for location in lights:
        out += struct.pack("<qq32x", 0, location)
    out += bytes(4 * TILES) + struct.pack("<ii", 1, 0xAA0004)
    out += struct.pack("<i", 1) + struct.pack("<II8xi4x", 0, 5, tile_script)
    out += bytes(12) + struct.pack("<3i", 0, 0, 0) + bytes(12) + bytes(4 * 128)
    for record in objects:
        out += record
    out += struct.pack("<i", len(objects))
    return bytes(out)


def loc(x: int, y: int) -> int:
    return x | y << 32


def tdf(width: int, height: int, tids: list[int], compressed: bool, version: bytes = TDF_VERSION) -> bytes:
    head = TDF_HEADER.pack(version, TDF_COMPRESSED if compressed else 0, width, height, 0, 0)
    if not compressed:
        return head + struct.pack(f"<{len(tids)}H", *tids)
    body = bytearray()
    for y in range(height):
        row = zlib.compress(struct.pack(f"<{width}H", *tids[y * width:(y + 1) * width]), 9)
        body += struct.pack("<I", len(row)) + row
    return head + bytes(body)


def make_tid(base: int, second: int, code: int, variant: int) -> int:
    return base << 11 | second << 6 | code << 2 | variant


def expect_failure(code: str, function, *args) -> None:
    try:
        function(*args)
    except ProbeFailure as error:
        assert str(error) == code, (code, str(error))
        return
    raise AssertionError(f"expected {code}")


def self_test() -> dict:
    mes = b"{0}{Grass}\n{1}{Cliff /b}\n{2}{Sea  /b}\n{100}{1,2,3}\n{200}{grass to cliff}\n{201}{Sea to Grass}\n"
    types = terrain_types(mes)
    assert types.names == ["Grass", "Cliff", "Sea"] and types.blocked == [False, True, True]
    assert types.transitions == [(0, 1), (2, 0)]
    expect_failure("TransitionUnparsed", terrain_types, mes + b"{202}{grass into sea}")
    expect_failure("TransitionUnparsed", terrain_types, mes + b"{202}{grass to lava}")

    # tid fields and the path rule for every code
    assert tid_fields(make_tid(17, 9, 13, 2)) == (17, 9, 13, 2)
    assert template_path(make_tid(0, 0, 0, 3), types) == ("plain", f"terrain/grass/{1 | 1 << 26}.sec")
    assert template_path(make_tid(0, 1, 0, 2), types) == ("plain", f"terrain/grass/{1 << 26}.sec")
    seen = {}
    for code in range(1, 15):
        kind, path = template_path(make_tid(0, 1, code, 3), types)
        assert kind == "transition", code
        folder, number = path.rsplit("/", 1)
        seen[code] = (folder, int(number[:-4]))
    for code in (1, 2, 3, 4, 5, 6, 8, 9, 12):
        assert seen[code][0] == "terrain/grass to cliff"
    for code in (7, 10, 11, 13, 14):
        assert seen[code] == ("terrain/cliff to grass", seen[15 - code][1])
    assert len({seen[c][1] for c in (1, 2, 3, 4, 5, 6, 8, 9, 12)}) == 9
    assert template_path(make_tid(0, 1, 15, 0), types) == ("no_slot", "terrain/cliff to grass/-1.sec")
    assert template_path(make_tid(5, 5, 0, 0), types) == ("invalid_type", None)
    assert terrain_blocked(make_tid(0, 2, 6, 0), types) and not terrain_blocked(make_tid(0, 0, 0, 0), types)
    assert terrain_blocked(0xFFFF, types)

    # relocation: the object rule keeps tile-local x/y; the light loader as written does not
    real = sector_id(1451, 1302)
    assert relocate_object(loc(64 + 5, 128 + 9), real) == ((1451 << 6) + 5, (1302 << 6) + 9)
    assert relocate_light_as_written(loc(64 + 5, 128 + 9), real) == (5 + 9 * 64 + 1451, 1302)

    # terrain.tdf both layouts and refusals
    tids = [make_tid(0, 0, 0, v % 4) for v in range(6)]
    for compressed in (False, True):
        decoded = decode_tdf(tdf(3, 2, tids, compressed))
        assert decoded.tids == [tids[:3], tids[3:]] and decoded.width == 3
    expect_failure("TdfVersion", decode_tdf, tdf(3, 2, tids, True, struct.pack("<f", 1.1)))
    expect_failure("TdfFlatLength", decode_tdf, tdf(3, 2, tids, False)[:-2])
    expect_failure("TdfTrailingBytes", decode_tdf, tdf(3, 2, tids, True) + b"\0")
    bad_row = TDF_HEADER.pack(TDF_VERSION, 1, 3, 1, 0, 0)
    row = zlib.compress(b"\0" * 4)
    expect_failure("TdfRowLength", decode_tdf, bad_row + struct.pack("<I", len(row)) + row)
    expect_failure("TdfUnknownFlags", decode_tdf, TDF_HEADER.pack(TDF_VERSION, 2, 1, 1, 0, 0) + b"\0\0")

    # sector framing and the object walker
    sector = synthetic_sector([loc(70, 3)], [synthetic_object(3, loc(64 + 1, 2)), synthetic_object(16, None, 2),
                                             synthetic_object(5, loc(64, 0))], tile_script=12)
    frame = frame_sector(sector)
    assert frame["lights"] == [(0, loc(70, 3))] and [r["type"] for r in frame["objects"]] == [3, 16, 5]
    assert frame["objects"][1]["own_kind"] == 2 and frame["objects"][0]["location"] == loc(65, 2)
    assert frame["tile_scripts"] == [(5, 12)]
    expect_failure("ObjectWindowRemainder", frame_sector, sector[:-4] + b"\0" + sector[-4:])
    broken = bytearray(synthetic_object(3, None))
    broken[4 + 48 + 4 + 2 + 4 + 2] = 0xFF  # second mask word has only four valid bits
    expect_failure("ObjectMaskBits", read_object, Cursor(bytes(broken), 0, len(broken)))

    # end-to-end on an invented installation
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        (root / "modules").mkdir()
        empty = synthetic_sector([], [])
        templates = [(f"terrain\\grass\\{sector_id(v % 2, v // 2)}.sec", synthetic_sector([], [synthetic_object(3, loc((v % 2) * 64, (v // 2) * 64))]))
                     for v in range(3)]
        templates += [(f"terrain\\Grass to Cliff\\{sector_id(1, 0)}.sec", empty),
                      ("terrain\\cliff\\0.sec", synthetic_sector([loc(3, 3)], []))]
        resource = [(name.encode(), 0, len(data), data) for name, data in templates]
        resource.append((b"terrain\\terrain.mes", 0, len(mes), mes))
        (root / "arcanum2.dat").write_bytes(dat_probe.make_archive(resource))
        grid = [make_tid(0, 0, 0, 0), make_tid(0, 0, 0, 3), make_tid(0, 1, 1, 0), make_tid(1, 1, 0, 0),
                make_tid(0, 0, 0, 2), make_tid(0, 1, 2, 0), make_tid(0, 0, 0, 1), make_tid(2, 2, 0, 0)]
        world = tdf(4, 2, grid, True)
        own = synthetic_sector([], [])
        module = [(b"maps\\World\\terrain.tdf", 0, len(world), world),
                  (f"maps\\World\\{sector_id(3, 1)}.sec".encode(), 0, len(own), own)]
        (root / "modules" / "Arcanum.dat").write_bytes(dat_probe.make_archive(module))
        report = survey(root, "World")
        main = report["main"]
        assert main["resolution"] == {"own_file": 1, "plain": 4, "plain_fill": 1, "transition": 1, "transition_fill": 1}, main
        assert main["missing_templates"] == {f"terrain/grass/{1 | 1 << 26}.sec": 1, f"terrain/grass to cliff/{sector_id(1, 2)}.sec": 1}
        assert main["world_map_blocked_generated"] == 3 and main["template_sources"] == {"arcanum2": 5}
        assert [t["plain_variants"] for t in report["terrain_types"]] == [3, 1, 0]
        census_ = report["template_census"]
        assert census_["totals"]["all"]["objects_in_own_slot"] == 3 and census_["totals"]["all"]["lights"] == 1
        assert census_["lights"] == {"lights_in_own_slot": 1, "object_rule_differs_from_as_written": 1}
        assert report["template_folders_named_by_mes_missing"] == ["cliff to grass", "grass to sea", "sea", "sea to grass"]
    return {"result": "PASS", "self_test": "mes types/transitions, tid fields, path rule for codes 0..15, "
            "relocation rules, tdf flat/row-zlib and refusals, sector framing, v119 walker, end-to-end survey"}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--self-test", action="store_true")
    group.add_argument("--game-root", type=Path)
    parser.add_argument("--map", default="Arcanum1-024-fixed", help="map folder whose grid is reported in full")
    args = parser.parse_args(argv)
    try:
        report = self_test() if args.self_test else original(args.game_root, args.map)
    except (ProbeFailure, dat_probe.DatProbeError) as error:
        print(json.dumps({"result": "FAIL", "code": str(error)}))
        return 1
    print(json.dumps(report, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
