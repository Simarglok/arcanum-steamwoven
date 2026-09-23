#!/usr/bin/env python3
"""Length-prefixed metadata bridge from the existing R1 probe to C# tests."""

from __future__ import annotations

import base64
import json
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "research"))
from dat_probe import parse_archive, read_bounded_entry  # noqa: E402


def emit(record: dict[str, object]) -> None:
    data = json.dumps(record, separators=(",", ":")).encode("utf-8")
    sys.stdout.buffer.write(struct.pack("<I", len(data)))
    sys.stdout.buffer.write(data)


def encoded(value: bytes) -> str:
    return base64.b64encode(value).decode("ascii")


def footer_opaque(path: Path) -> bytes:
    with path.open("rb") as stream:
        stream.seek(-28, 2)
        return stream.read(16)


def main() -> int:
    if len(sys.argv) not in (2, 4) or (len(sys.argv) == 4 and sys.argv[2] != "--payload"):
        raise SystemExit("usage: dat_probe_adapter.py ARCHIVE [--payload ORDINAL]")
    path = Path(sys.argv[1])
    archive = parse_archive(path)
    if len(sys.argv) == 4:
        emit({"k": "p", "payload": encoded(read_bounded_entry(archive, archive.entries[int(sys.argv[3])]))})
        return 0
    footer = archive.footer
    emit(
        {
            "k": "f",
            "opaque": encoded(footer_opaque(path)),
            "marker": encoded(footer.marker),
            "hint": footer.name_bytes_hint,
            "tail": footer.tail_region_bytes,
            "index": footer.index_start,
        }
    )
    for entry in archive.entries:
        emit(
            {
                "k": "e",
                "ord": entry.ordinal,
                "name": encoded(entry.raw_name),
                "opaque": encoded(entry.opaque),
                "flags": entry.flags,
                "declared": entry.unpacked_size,
                "stored": entry.stored_size,
                "offset": entry.offset,
            }
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())