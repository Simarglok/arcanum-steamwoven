"""Read-only PE32 image loader for the function oracle (ARC-DEV-007).

The file is read once, its SHA-256 is checked against the expected value before anything is parsed, and the image is
built in memory (headers plus each section at its virtual address). Nothing is written back to the install.
"""
from __future__ import annotations

from dataclasses import dataclass
import hashlib
from pathlib import Path
import struct

# The retail GOG 1.0.7.4 Arcanum.exe (docs/research/original-executable-check.md#inputs).
PINNED_EXE_SHA256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52"
MAX_FILE_BYTES = 64 * 1024 * 1024
MAX_IMAGE_BYTES = 64 * 1024 * 1024
MAX_SECTIONS = 32


class RefusedExecutable(Exception):
    """The file is not the executable the oracle may run, or not a PE32 image it can map."""


@dataclass(frozen=True)
class Section:
    name: str
    start: int  # absolute virtual address
    end: int    # exclusive


@dataclass(frozen=True)
class PeImage:
    base: int
    image: bytes
    sections: tuple[Section, ...]
    sha256: str

    def section_of(self, address: int) -> Section | None:
        return next((section for section in self.sections if section.start <= address < section.end), None)


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def load(path: Path, expected_sha256: str = PINNED_EXE_SHA256) -> PeImage:
    """Map the PE32 file at `path`, or raise RefusedExecutable when its SHA-256 is not `expected_sha256`."""
    path = Path(path)
    if not path.is_file() or path.is_symlink():
        raise RefusedExecutable(f"not a regular file: {path}")
    size = path.stat().st_size
    if size > MAX_FILE_BYTES:
        raise RefusedExecutable(f"file too large ({size} bytes)")
    data = path.read_bytes()
    actual = hashlib.sha256(data).hexdigest()
    if actual != expected_sha256:
        raise RefusedExecutable(f"SHA-256 {actual} is not the expected {expected_sha256}")
    return parse(data, actual)


def parse(data: bytes, sha256: str) -> PeImage:
    def u16(offset: int) -> int:
        return read(offset, "<H")

    def u32(offset: int) -> int:
        return read(offset, "<I")

    def read(offset: int, fmt: str) -> int:
        width = struct.calcsize(fmt)
        if offset < 0 or offset + width > len(data):
            raise RefusedExecutable(f"truncated header at {offset:#x}")
        return struct.unpack_from(fmt, data, offset)[0]

    if data[:2] != b"MZ":
        raise RefusedExecutable("no MZ header")
    pe = u32(0x3C)
    if data[pe:pe + 4] != b"PE\0\0":
        raise RefusedExecutable("no PE signature")
    coff = pe + 4
    if u16(coff) != 0x14C:
        raise RefusedExecutable("not an x86-32 image")
    count = u16(coff + 2)
    optional_size = u16(coff + 16)
    optional = coff + 20
    if u16(optional) != 0x10B:
        raise RefusedExecutable("not a PE32 optional header")
    if count < 1 or count > MAX_SECTIONS:
        raise RefusedExecutable(f"bad section count {count}")
    base = u32(optional + 28)
    image_size = u32(optional + 56)
    header_size = u32(optional + 60)
    if image_size < header_size or image_size > MAX_IMAGE_BYTES or header_size > len(data):
        raise RefusedExecutable("bad image or header size")
    image = bytearray(image_size)
    image[:header_size] = data[:header_size]
    sections = []
    table = optional + optional_size
    for index in range(count):
        entry = table + 40 * index
        if entry + 40 > len(data):
            raise RefusedExecutable("truncated section table")
        name = data[entry:entry + 8].rstrip(b"\0").decode("latin-1")
        virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from("<IIII", data, entry + 8)
        extent = virtual_size or raw_size
        if virtual_address < header_size or virtual_address + extent > image_size:
            raise RefusedExecutable(f"section {name!r} outside the image")
        copied = min(raw_size, extent)
        if raw_offset + copied > len(data):
            raise RefusedExecutable(f"section {name!r} outside the file")
        image[virtual_address:virtual_address + copied] = data[raw_offset:raw_offset + copied]
        sections.append(Section(name, base + virtual_address, base + virtual_address + extent))
    return PeImage(base, bytes(image), tuple(sections), sha256)
