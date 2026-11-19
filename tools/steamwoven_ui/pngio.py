"""ARC-DEV-2972: a minimal PNG reader and writer on the standard library (8-bit, not interlaced), so the kit tools need no
imaging package. It holds no art: it moves pixels between files and the `Image` below."""
from __future__ import annotations

import struct
import zlib
from dataclasses import dataclass

SIGNATURE = b"\x89PNG\r\n\x1a\n"
MAX_PIXELS = 64 * 1024 * 1024


class PngError(Exception):
    pass


@dataclass
class Image:
    """RGBA, 8 bit, straight alpha, row-major; `data` has `w * h * 4` bytes."""
    w: int
    h: int
    data: bytearray
    source_alpha: bool = True  # False: decoded from a PNG with no alpha channel and no colour key (every pixel opaque by default)

    @staticmethod
    def blank(w: int, h: int, rgba: tuple[int, int, int, int] = (0, 0, 0, 0)) -> "Image":
        return Image(w, h, bytearray(bytes(rgba) * (w * h)))

    def pixel(self, x: int, y: int) -> tuple[int, int, int, int]:
        i = (y * self.w + x) * 4
        return self.data[i], self.data[i + 1], self.data[i + 2], self.data[i + 3]

    def crop(self, x: int, y: int, w: int, h: int) -> "Image":
        out = bytearray()
        for row in range(y, y + h):
            out += self.data[(row * self.w + x) * 4:(row * self.w + x + w) * 4]
        return Image(w, h, out)


def _chunks(blob: bytes):
    if blob[:8] != SIGNATURE:
        raise PngError("not a PNG file")
    pos = 8
    while pos + 8 <= len(blob):
        length, kind = struct.unpack(">I4s", blob[pos:pos + 8])
        body = blob[pos + 8:pos + 8 + length]
        if len(body) != length:
            raise PngError("truncated chunk")
        yield kind, body
        pos += 12 + length
        if kind == b"IEND":
            return
    raise PngError("no IEND chunk")


def _paeth(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    return a if pa <= pb and pa <= pc else b if pb <= pc else c


def decode(blob: bytes) -> Image:
    header = None
    palette = b""
    trns = b""
    idat = bytearray()
    for kind, body in _chunks(blob):
        if kind == b"IHDR":
            if len(body) != 13:
                raise PngError("bad IHDR chunk")
            header = struct.unpack(">IIBBBBB", body)
        elif kind == b"PLTE":
            palette = body
        elif kind == b"tRNS":
            trns = body
        elif kind == b"IDAT":
            idat += body
    if header is None:
        raise PngError("no IHDR chunk")
    w, h, depth, color, _compression, _filter, interlace = header
    if depth != 8 or interlace != 0 or color not in (0, 2, 3, 4, 6):
        raise PngError(f"unsupported PNG (depth {depth}, colour type {color}, interlace {interlace}): 8 bit, not interlaced only")
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[color]
    stride = w * channels
    if w < 1 or h < 1 or w * h > MAX_PIXELS:
        raise PngError(f"picture size {w}x{h} is outside 1 to {MAX_PIXELS} pixels")
    try:
        decoder = zlib.decompressobj()
        raw = decoder.decompress(bytes(idat), (stride + 1) * h + 1)  # never more than the header promises (a compression bomb stops here)
    except zlib.error as error:
        raise PngError(f"bad IDAT data: {error}") from error
    if len(raw) != (stride + 1) * h:
        raise PngError("IDAT size does not match the header")
    rows: list[bytearray] = []
    previous = bytearray(stride)
    for y in range(h):
        kind = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            left = line[i - channels] if i >= channels else 0
            up = previous[i]
            corner = previous[i - channels] if i >= channels else 0
            if kind == 1:
                line[i] = (line[i] + left) & 255
            elif kind == 2:
                line[i] = (line[i] + up) & 255
            elif kind == 3:
                line[i] = (line[i] + ((left + up) >> 1)) & 255
            elif kind == 4:
                line[i] = (line[i] + _paeth(left, up, corner)) & 255
            elif kind != 0:
                raise PngError(f"unknown filter {kind}")
        rows.append(line)
        previous = line
    key = None  # a colour key of tRNS for grey (one value) and RGB (three values): that colour is transparent
    if color == 0 and len(trns) >= 2:
        key = (int.from_bytes(trns[:2], "big"),)
    elif color == 2 and len(trns) >= 6:
        key = tuple(int.from_bytes(trns[i:i + 2], "big") for i in (0, 2, 4))
    out = bytearray(w * h * 4)
    for y, line in enumerate(rows):
        for x in range(w):
            o = (y * w + x) * 4
            if color == 6:
                out[o:o + 4] = line[x * 4:x * 4 + 4]
            elif color == 2:
                out[o:o + 3] = line[x * 3:x * 3 + 3]
                out[o + 3] = 0 if key is not None and tuple(out[o:o + 3]) == key else 255
            elif color == 0:
                out[o] = out[o + 1] = out[o + 2] = line[x]
                out[o + 3] = 0 if key is not None and line[x] == key[0] else 255
            elif color == 4:
                out[o] = out[o + 1] = out[o + 2] = line[x * 2]
                out[o + 3] = line[x * 2 + 1]
            else:
                index = line[x]
                if index * 3 + 3 > len(palette):
                    raise PngError("palette index out of range")
                out[o:o + 3] = palette[index * 3:index * 3 + 3]
                out[o + 3] = trns[index] if index < len(trns) else 255
    return Image(w, h, out, source_alpha=color in (3, 4, 6) or key is not None)


def _chunk(kind: bytes, body: bytes) -> bytes:
    return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)


def encode(image: Image) -> bytes:
    """RGBA 8 bit, filter 0 on every row, one IDAT: the same bytes for the same pixels (a build is reproducible)."""
    stride = image.w * 4
    raw = bytearray()
    for y in range(image.h):
        raw.append(0)
        raw += image.data[y * stride:(y + 1) * stride]
    return (SIGNATURE + _chunk(b"IHDR", struct.pack(">IIBBBBB", image.w, image.h, 8, 6, 0, 0, 0))
            + _chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + _chunk(b"IEND", b""))


def read(path) -> Image:
    with open(path, "rb") as handle:
        return decode(handle.read())


def write(path, image: Image) -> None:
    with open(path, "wb") as handle:
        handle.write(encode(image))
