"""ARC-DEV-2460: a small PNG reader and writer for the Steamwoven kit tools (standard library only; no numpy, no Pillow).

The kit's masters are PNG, RGBA, 8 bit, sRGB, straight alpha (TRD SWI-KIT-2). This module reads exactly that (a non-interlaced
8-bit RGBA image with any filter, any number of IDAT chunks; ancillary chunks are ignored) and writes it back in one canonical
form: IHDR, sRGB, one IDAT, IEND, filter 0 on every row, zlib level 9. The same pixels give the same bytes in the same Python/zlib build.
A file that is not that format is refused with `PngError` and a reason; nothing is guessed or converted.
"""
from __future__ import annotations

from itertools import accumulate
import struct
import zlib

SIGNATURE = b"\x89PNG\r\n\x1a\n"
MAX_SIDE = 16384  # a sanity bound: the largest master is 2880 x 2160


class PngError(ValueError):
    """The file is not an 8-bit non-interlaced RGBA PNG, or it is damaged."""


class Image:
    """An RGBA8 image: `rows` holds `height` rows of `width * 4` bytes (R, G, B, A per pixel, straight alpha)."""

    __slots__ = ("width", "height", "rows")

    def __init__(self, width: int, height: int, rows: list[bytes]) -> None:
        if width <= 0 or height <= 0 or len(rows) != height or any(len(row) != width * 4 for row in rows):
            raise PngError("rows do not match the size")
        self.width = width
        self.height = height
        self.rows = rows

    def pixels(self) -> bytes:
        return b"".join(self.rows)


def _chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def encode(image: Image) -> bytes:
    """The canonical PNG bytes of an image."""
    raw = b"".join(b"\x00" + row for row in image.rows)
    return b"".join((
        SIGNATURE,
        _chunk(b"IHDR", struct.pack(">IIBBBBB", image.width, image.height, 8, 6, 0, 0, 0)),
        _chunk(b"sRGB", b"\x00"),
        _chunk(b"IDAT", zlib.compress(raw, 9)),
        _chunk(b"IEND", b""),
    ))


def _paeth(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    return a if pa <= pb and pa <= pc else (b if pb <= pc else c)


def _unfilter(kind: int, line: bytearray, prev: bytearray, bpp: int) -> None:
    n = len(line)
    if kind == 0:
        return
    if kind == 1:
        for c in range(bpp):
            line[c::bpp] = bytes(v & 255 for v in accumulate(line[c::bpp]))
    elif kind == 2:
        line[:] = bytes((a + b) & 255 for a, b in zip(line, prev))
    elif kind == 3:
        for i in range(n):
            left = line[i - bpp] if i >= bpp else 0
            line[i] = (line[i] + ((left + prev[i]) >> 1)) & 255
    elif kind == 4:
        for i in range(n):
            left = line[i - bpp] if i >= bpp else 0
            upleft = prev[i - bpp] if i >= bpp else 0
            line[i] = (line[i] + _paeth(left, prev[i], upleft)) & 255
    else:
        raise PngError(f"unknown row filter {kind}")


def decode(data: bytes) -> Image:
    """Read an 8-bit non-interlaced RGBA PNG; refuse anything else."""
    if data[:8] != SIGNATURE:
        raise PngError("not a PNG file")
    pos = 8
    header = None
    idat: list[bytes] = []
    seen_end = False
    while pos + 8 <= len(data):
        (length,) = struct.unpack(">I", data[pos:pos + 4])
        kind = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        crc = data[pos + 8 + length:pos + 12 + length]
        if len(body) != length or len(crc) != 4:
            raise PngError("truncated chunk")
        if struct.unpack(">I", crc)[0] != zlib.crc32(kind + body) & 0xFFFFFFFF:
            raise PngError(f"bad checksum in chunk {kind!r}")
        pos += 12 + length
        if kind == b"IHDR":
            if length != 13:
                raise PngError("bad IHDR")
            header = struct.unpack(">IIBBBBB", body)
        elif kind == b"IDAT":
            idat.append(body)
        elif kind == b"IEND":
            seen_end = True
            break
    if header is None or not idat or not seen_end:
        raise PngError("missing IHDR, IDAT or IEND")
    width, height, depth, color, compression, filtering, interlace = header
    if (depth, color) != (8, 6):
        raise PngError(f"need 8-bit RGBA (colour type 6), got depth {depth} colour type {color}")
    if compression != 0 or filtering != 0 or interlace != 0:
        raise PngError("interlaced or unknown-method PNG is not supported")
    if not (0 < width <= MAX_SIDE and 0 < height <= MAX_SIDE):
        raise PngError(f"size {width} x {height} is out of bounds")
    try:
        raw = zlib.decompress(b"".join(idat))
    except zlib.error as exc:
        raise PngError(f"damaged image data: {exc}") from exc
    stride = width * 4
    if len(raw) != height * (stride + 1):
        raise PngError("image data length does not match the size")
    rows: list[bytes] = []
    prev = bytearray(stride)
    for y in range(height):
        base = y * (stride + 1)
        kind = raw[base]
        line = bytearray(raw[base + 1:base + 1 + stride])
        _unfilter(kind, line, prev, 4)
        rows.append(bytes(line))
        prev = line
    return Image(width, height, rows)
