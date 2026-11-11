"""Standard-library reading of the player's interface font ART files (ARC-DEV-355).

Finds an interface ART number's file in the player's content sources (DAT archives and loose folders, in the original's
search order, docs/research/content-precedence.md) and decodes its group-0 frames to 8-bit coverage masks.
Nothing here needs numpy; the tracing half is `outline.py`.  Read-only: no original byte is written anywhere.

Format facts (docs/formats/art.md, ledger rows MW3/MW4/MW31): a 132-byte header of 33 little-endian words, one 1,024-byte
B,G,R,X palette table per non-zero selector (words 3..6), 28-byte frame headers (width, height, encoded size, centre x/y,
delta x/y), then the payloads; a payload is raw when its size equals width x height, else the 0x7f-count RLE.  A font's
glyph is a coverage mask: the palette entry's red byte is the coverage, index 0 is transparent (MW31).
"""

from __future__ import annotations

import re
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "research"))
import dat_probe  # noqa: E402  (the one DAT index reader of the Python tools)

HEADER_SIZE = 132
FRAME_HEADER = struct.Struct("<IIIiiii")
PALETTE_SIZE = 1024
MAX_ART_BYTES = 8 * 1024 * 1024
MAX_FRAMES = 512
MAX_SIDE = 512
INTERFACE_INDEX = "art/interface/interface.mes"
INTERFACE_FOLDER = "art/interface/"


class FontError(ValueError):
    """A typed, user-facing failure: a missing source, an unreadable archive, a malformed ART file."""


def normalize(name: str) -> str:
    return name.replace("\\", "/").strip("/").lower()


@dataclass(frozen=True)
class Source:
    """One content source: a DAT archive (`kind` "dat") or a loose folder (`kind` "dir")."""

    kind: str
    path: Path
    label: str


def discover_sources(game_dir: Path, module: str = "Arcanum") -> list[Source]:
    """Search order, highest priority first, as the original mounts them (docs/research/content-precedence.md):
    module folder, patch9..patch0 (only when the module .dat exists), module .dat, `data/`, arcanum*.dat last..first,
    the game folder.  Only sources that exist are returned."""
    game_dir = game_dir.resolve()
    if not game_dir.is_dir():
        raise FontError(f"game folder not found: {game_dir}")
    modules = game_dir / "modules"
    found: list[Source] = []

    def case_file(folder: Path, name: str) -> Path | None:
        return next((entry for entry in folder.iterdir() if entry.name.lower() == name.lower() and entry.is_file()), None) if folder.is_dir() else None

    def case_dir(folder: Path, name: str) -> Path | None:
        return next((entry for entry in folder.iterdir() if entry.name.lower() == name.lower() and entry.is_dir()), None) if folder.is_dir() else None

    if (folder := case_dir(modules, module)) is not None:
        found.append(Source("dir", folder, f"module folder {module}"))
    module_dat = case_file(modules, f"{module}.dat")
    if module_dat is not None:
        for digit in range(9, -1, -1):
            if (patch := case_file(modules, f"{module}.patch{digit}")) is not None:
                found.append(Source("dat", patch, f"{module} patch{digit}"))
        found.append(Source("dat", module_dat, f"module {module}"))
    if (data := case_dir(game_dir, "data")) is not None:
        found.append(Source("dir", data, "data folder"))
    base = sorted((entry for entry in game_dir.iterdir()
                   if entry.is_file() and re.fullmatch(r"arcanum.*\.dat", entry.name.lower())), key=lambda entry: entry.name.lower())
    for entry in reversed(base):
        found.append(Source("dat", entry, entry.name.lower()))
    found.append(Source("dir", game_dir, "game folder"))
    return found


class Content:
    """Lazy lookup of single files across the sources; each DAT index is parsed once, on first use."""

    def __init__(self, sources: list[Source]) -> None:
        self.sources = sources
        self._indexes: dict[Path, dict[str, dat_probe.Entry]] = {}
        self._archives: dict[Path, dat_probe.Archive] = {}

    def _index(self, source: Source) -> dict[str, "dat_probe.Entry"]:
        cached = self._indexes.get(source.path)
        if cached is None:
            try:
                archive = dat_probe.parse_archive(source.path)
            except (dat_probe.DatProbeError, OSError) as error:
                raise FontError(f"{source.label}: cannot read the archive index: {error}") from error
            self._archives[source.path] = archive
            cached = {}
            for entry in archive.entries:
                if entry.is_directory:
                    continue
                try:
                    name = normalize(entry.raw_name.rstrip(b"\0").decode("ascii"))
                except UnicodeDecodeError:
                    continue
                cached.setdefault(name, entry)
            self._indexes[source.path] = cached
        return cached

    def find(self, name: str) -> tuple[bytes, str] | None:
        """The first source in search order that holds `name`: (bytes, source label), or None."""
        key = normalize(name)
        for source in self.sources:
            if source.kind == "dir":
                candidate = source.path.joinpath(*key.split("/"))
                match = _case_insensitive(candidate, source.path, key)
                if match is not None:
                    size = match.stat().st_size
                    if size > MAX_ART_BYTES:
                        raise FontError(f"{source.label}: {key} is {size} bytes, over the {MAX_ART_BYTES} limit")
                    return match.read_bytes(), source.label
                continue
            entry = self._index(source).get(key)
            if entry is None:
                continue
            if entry.unpacked_size > MAX_ART_BYTES:
                raise FontError(f"{source.label}: {key} declares {entry.unpacked_size} bytes, over the {MAX_ART_BYTES} limit")
            try:
                data = _read_entry(self._archives[source.path], entry)
            except (dat_probe.DatProbeError, OSError) as error:
                raise FontError(f"{source.label}: {key}: {error}") from error
            return data, source.label
        return None


def _case_insensitive(candidate: Path, root: Path, key: str) -> Path | None:
    if candidate.is_file():
        return candidate
    current = root
    for part in key.split("/"):
        if not current.is_dir():
            return None
        current = next((child for child in current.iterdir() if child.name.lower() == part), None)  # type: ignore[assignment]
        if current is None:
            return None
    return current if current.is_file() else None


def _read_entry(archive: "dat_probe.Archive", entry: "dat_probe.Entry") -> bytes:
    """`dat_probe.read_bounded_entry` has a 1 MiB cap; font ART files are far below it, anything bigger is refused."""
    return dat_probe.read_bounded_entry(archive, entry)


MES_LINE = re.compile(rb"\{\s*(\d+)\s*\}\s*\{([^}]*)\}")


def parse_mes(data: bytes) -> dict[int, str]:
    """The simple `{number}{text}` form of `interface.mes`; the first message of a number wins, Latin-1 text, trimmed."""
    messages: dict[int, str] = {}
    for line in data.splitlines():
        if line.lstrip().startswith(b"//"):
            continue
        match = MES_LINE.search(line)
        if match is not None:
            messages.setdefault(int(match.group(1)), match.group(2).decode("latin-1").strip())
    return messages


def interface_art_name(content: Content, number: int) -> tuple[str, str]:
    """(file name, source label of interface.mes) of an interface ART number."""
    found = content.find(INTERFACE_INDEX)
    if found is None:
        raise FontError(f"{INTERFACE_INDEX} not found in any content source")
    messages = parse_mes(found[0])
    name = messages.get(number)
    if not name:
        raise FontError(f"{INTERFACE_INDEX} has no message {number}")
    return name, found[1]


@dataclass(frozen=True)
class Frame:
    """One decoded frame: `red` holds width x height bytes row by row (0 where the palette index is 0), the original's coverage."""

    width: int
    height: int
    centre_x: int
    centre_y: int
    delta_x: int
    delta_y: int
    red: bytes
    grey: bool


@dataclass(frozen=True)
class FontArt:
    number: int
    file: str
    source: str
    frames: tuple[Frame, ...]
    raw: bytes


def decode_rle(payload: bytes, count: int, label: str) -> bytes:
    out = bytearray()
    position = 0
    while position < len(payload):
        control = payload[position]
        position += 1
        run = control & 0x7F
        if run == 0:
            raise FontError(f"{label}: zero-length run")
        if control & 0x80:
            if position + run > len(payload):
                raise FontError(f"{label}: truncated literal run")
            out += payload[position:position + run]
            position += run
        else:
            if position >= len(payload):
                raise FontError(f"{label}: truncated repeat run")
            out += bytes((payload[position],)) * run
            position += 1
        if len(out) > count:
            raise FontError(f"{label}: run output past {count} pixels")
    if len(out) != count:
        raise FontError(f"{label}: {len(out)} pixels decoded, {count} expected")
    return bytes(out)


def decode_font_art(data: bytes, label: str = "art") -> tuple[Frame, ...]:
    """Group-0 frames of an ART file, as coverage (red) masks.  A tail after the last frame is allowed (the original never checks it)."""
    if len(data) < HEADER_SIZE:
        raise FontError(f"{label}: shorter than the 132-byte header")
    words = struct.unpack_from("<33I", data, 0)
    groups = 1 if words[0] & 1 else 8
    per_group = words[8]
    if not 0 < per_group <= MAX_FRAMES:
        raise FontError(f"{label}: {per_group} frames per group")
    selectors = [word for word in words[3:7]]
    position = HEADER_SIZE
    palettes: list[bytes | None] = []
    for selector in selectors:
        if selector:
            if position + PALETTE_SIZE > len(data):
                raise FontError(f"{label}: truncated palette table")
            palettes.append(data[position:position + PALETTE_SIZE])
            position += PALETTE_SIZE
        else:
            palettes.append(None)
    palette = palettes[0]
    if palette is None:
        raise FontError(f"{label}: no palette slot 0")
    headers_start = position
    total = groups * per_group
    if headers_start + total * FRAME_HEADER.size > len(data):
        raise FontError(f"{label}: truncated frame header table")
    headers = [FRAME_HEADER.unpack_from(data, headers_start + index * FRAME_HEADER.size) for index in range(total)]
    payload_at = headers_start + total * FRAME_HEADER.size
    red_of = bytes(palette[index * 4 + 2] for index in range(256))
    green_of = bytes(palette[index * 4 + 1] for index in range(256))
    blue_of = bytes(palette[index * 4] for index in range(256))
    frames: list[Frame] = []
    for index in range(per_group):
        width, height, size, centre_x, centre_y, delta_x, delta_y = headers[index]
        where = f"{label} frame {index}"
        if not (0 < width <= MAX_SIDE and 0 < height <= MAX_SIDE):
            raise FontError(f"{where}: size {width}x{height}")
        if payload_at + size > len(data):
            raise FontError(f"{where}: payload past the end of the file")
        payload = data[payload_at:payload_at + size]
        payload_at += size
        count = width * height
        indexed = payload if size == count else decode_rle(payload, count, where)
        grey = all(red_of[value] == green_of[value] == blue_of[value] for value in set(indexed) if value)
        red = bytes(0 if value == 0 else red_of[value] for value in indexed)
        frames.append(Frame(width, height, centre_x, centre_y, delta_x, delta_y, red, grey))
    return tuple(frames)


def load_font(content: Content, number: int) -> FontArt:
    name, _index_source = interface_art_name(content, number)
    found = content.find(INTERFACE_FOLDER + name)
    if found is None:
        raise FontError(f"art {number}: {INTERFACE_FOLDER}{name} not found in any content source")
    data, source = found
    return FontArt(number, name, source, decode_font_art(data, f"art {number} {name}"), data)
