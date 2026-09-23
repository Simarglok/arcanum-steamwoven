#!/usr/bin/env python3
"""Read-only, opt-in research probe for the observed DAT container candidate.

This is deliberately not a production reader: it emits only structural and
aggregate facts, never writes archive payloads, and accepts local archive paths
only as explicit arguments.
"""

from __future__ import annotations

import argparse
import contextlib
import hashlib
import importlib
import io
import json
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
import struct
import sys
import tempfile
from typing import Any, Callable
import zlib

FOOTER = struct.Struct("<16s4sII")
FOOTER_SIZE = FOOTER.size
U32 = struct.Struct("<I")
ENTRY_FIXED = struct.Struct("<4xIIII")
ENTRY_FIELDS = struct.Struct("<4sIIII")
MARKER = b"1TAD"
COMPRESSED = 0x002
DIRECTORY = 0x400
PLAIN = 0x001
OBSERVED_FLAG_MASK = PLAIN | COMPRESSED | DIRECTORY
SOURCE_REPORTED_UNOBSERVED_FLAG_MASK = 0x100 | 0x200 | 0x800
HARD_OUTPUT_CAP = 1024 * 1024
HARD_STORED_INPUT_CAP = 1024 * 1024
PREFERRED_OUTPUT_CAP = 64 * 1024


class DatProbeError(ValueError):
    """A structural or bounded-sampling validation failure."""


@dataclass(frozen=True)
class Footer:
    marker: bytes
    name_bytes_hint: int
    tail_region_bytes: int
    index_start: int


@dataclass(frozen=True)
class Entry:
    ordinal: int
    raw_name: bytes
    opaque: bytes
    flags: int
    unpacked_size: int
    stored_size: int
    offset: int

    @property
    def is_directory(self) -> bool:
        return bool(self.flags & DIRECTORY)

    @property
    def is_compressed(self) -> bool:
        return bool(self.flags & COMPRESSED)


@dataclass(frozen=True)
class Archive:
    path: Path
    size: int
    footer: Footer
    entries: tuple[Entry, ...]


@dataclass(frozen=True)
class SampledEntry:
    entry: Entry
    payload: bytes


@dataclass(frozen=True)
class InspectedArchive:
    archive: Archive
    sample_kinds: frozenset[bool]
    samples: tuple[SampledEntry, ...]


def checked_add(left: int, right: int, limit: int, label: str) -> int:
    if left < 0 or right < 0 or left > limit or right > limit - left:
        raise DatProbeError(f"{label}: out of range")
    return left + right


def read_exact(stream: io.BufferedReader | io.BytesIO, length: int, label: str) -> bytes:
    data = stream.read(length)
    if len(data) != length:
        raise DatProbeError(f"{label}: truncated")
    return data


def parse_archive(path: Path) -> Archive:
    size = path.stat().st_size
    if size < FOOTER_SIZE:
        raise DatProbeError("footer: truncated")

    with path.open("rb") as stream:
        stream.seek(size - FOOTER_SIZE)
        _opaque_prefix, marker, name_bytes_hint, tail_region_bytes = FOOTER.unpack(
            read_exact(stream, FOOTER_SIZE, "footer")
        )
        if marker != MARKER:
            raise DatProbeError(f"footer marker: expected {MARKER!r}, got {marker!r}")
        if tail_region_bytes < FOOTER_SIZE or tail_region_bytes > size:
            raise DatProbeError("footer tail-region size: out of range")
        index_start = size - tail_region_bytes
        index_end = size - FOOTER_SIZE
        if index_start > index_end:
            raise DatProbeError("index: overlaps footer")
        stream.seek(index_start)
        count = U32.unpack(read_exact(stream, U32.size, "entry count"))[0]
        if count > (index_end - stream.tell()) // (U32.size + 1):
            raise DatProbeError("entry count: physically unreachable")

        entries: list[Entry] = []
        for ordinal in range(count):
            if stream.tell() + U32.size > index_end:
                raise DatProbeError("entry name length: outside index")
            name_length = U32.unpack(read_exact(stream, U32.size, "entry name length"))[0]
            remaining = index_end - stream.tell()
            if name_length == 0 or name_length > remaining:
                raise DatProbeError("entry name: invalid length")
            raw_name = read_exact(stream, name_length, "entry name")
            if not raw_name.endswith(b"\0"):
                raise DatProbeError("entry name: missing NUL terminator")
            if stream.tell() + ENTRY_FIXED.size > index_end:
                raise DatProbeError("entry fields: outside index")
            opaque, flags, unpacked_size, stored_size, offset = ENTRY_FIELDS.unpack(
                read_exact(stream, ENTRY_FIXED.size, "entry fields")
            )
            if not flags & DIRECTORY:
                payload_end = checked_add(offset, stored_size, index_start, "payload bounds")
                if payload_end > index_start:
                    raise DatProbeError("payload bounds: intersects index")
            entries.append(Entry(ordinal, raw_name, opaque, flags, unpacked_size, stored_size, offset))
        if stream.tell() != index_end:
            raise DatProbeError("index: unconsumed bytes")

    return Archive(path, size, Footer(marker, name_bytes_hint, tail_region_bytes, index_start), tuple(entries))


def read_bounded_entry(archive: Archive, entry: Entry) -> bytes:
    if entry.is_directory or entry.stored_size == 0:
        raise DatProbeError("sample: directory or empty entry")
    if entry.stored_size > HARD_STORED_INPUT_CAP:
        raise DatProbeError("sample: stored input exceeds hard cap")
    if entry.unpacked_size > HARD_OUTPUT_CAP:
        raise DatProbeError("sample: declared output exceeds hard cap")

    with archive.path.open("rb") as stream:
        stream.seek(entry.offset)
        stored = read_exact(stream, entry.stored_size, "sample payload")

    if entry.is_compressed:
        try:
            decompressor = zlib.decompressobj()
            output = decompressor.decompress(stored, entry.unpacked_size + 1)
            if len(output) > entry.unpacked_size:
                raise DatProbeError("sample: decompressed output exceeds declared length")
            if decompressor.unused_data or decompressor.unconsumed_tail:
                raise DatProbeError("sample: zlib trailing or unconsumed data")
            if not decompressor.eof:
                remaining = entry.unpacked_size + 1 - len(output)
                if remaining <= 0:
                    raise DatProbeError("sample: decompressed output exceeds declared length")
                output += decompressor.flush(remaining)
            if len(output) > entry.unpacked_size:
                raise DatProbeError("sample: decompressed output exceeds declared length")
            if not decompressor.eof:
                raise DatProbeError("sample: zlib stream did not reach EOF")
            if decompressor.unused_data or decompressor.unconsumed_tail:
                raise DatProbeError("sample: zlib trailing or unconsumed data")
        except zlib.error as error:
            raise DatProbeError("sample: invalid zlib stream") from error
        if len(output) != entry.unpacked_size:
            raise DatProbeError("sample: decompressed length differs from declared length")
        return output
    if entry.stored_size != entry.unpacked_size:
        raise DatProbeError("sample: plain stored and declared sizes differ")
    return stored


def sample_entry(sample: SampledEntry) -> dict[str, object]:
    entry = sample.entry
    kind = "compressed" if entry.is_compressed else "plain"

    return {
        "ordinal": entry.ordinal,
        "kind": kind,
        "stored_size": entry.stored_size,
        "declared_output_size": entry.unpacked_size,
        "stored_input_hard_cap_bytes": HARD_STORED_INPUT_CAP,
        "output_hard_cap_bytes": HARD_OUTPUT_CAP,
        "zlib_verified": kind == "compressed",
    }


def archive_report(inspected: InspectedArchive) -> dict[str, object]:
    archive = inspected.archive
    flags = Counter(entry.flags for entry in archive.entries)
    unobserved_bits = Counter(entry.flags & ~OBSERVED_FLAG_MASK for entry in archive.entries)
    source_reported_unobserved_bits = Counter(
        entry.flags & SOURCE_REPORTED_UNOBSERVED_FLAG_MASK for entry in archive.entries
    )
    unknown_bits = Counter(
        entry.flags & ~(OBSERVED_FLAG_MASK | SOURCE_REPORTED_UNOBSERVED_FLAG_MASK) for entry in archive.entries
    )
    nul_terminated = sum(entry.raw_name.endswith(b"\0") for entry in archive.entries)
    ascii_decodable = 0
    non_ascii = 0
    slash_names = 0
    backslash_names = 0
    absolute_like = 0
    traversal_like = 0
    for entry in archive.entries:
        name = entry.raw_name[:-1]
        try:
            name.decode("ascii")
            ascii_decodable += 1
        except UnicodeDecodeError:
            non_ascii += 1
        slash_names += int(b"/" in name)
        backslash_names += int(b"\\" in name)
        absolute_like += int(name.startswith((b"/", b"\\")) or (len(name) >= 2 and name[1:2] == b":"))
        traversal_like += int(b".." in name.split(b"/") or b".." in name.split(b"\\"))

    result: dict[str, object] = {
        "archive_bytes": archive.size,
        "footer": {
            "marker_ascii": archive.footer.marker.decode("ascii"),
            "name_bytes_hint": archive.footer.name_bytes_hint,
            "tail_region_bytes": archive.footer.tail_region_bytes,
            "index_start": archive.footer.index_start,
            "index_end": archive.size - FOOTER_SIZE,
            "index_before_footer": archive.footer.index_start <= archive.size - FOOTER_SIZE,
        },
        "entry_count": len(archive.entries),
        "path_bytes": {
            "nul_terminated_count": nul_terminated,
            "ascii_decodable_count": ascii_decodable,
            "non_ascii_count": non_ascii,
            "forward_slash_count": slash_names,
            "backslash_count": backslash_names,
            "absolute_like_count": absolute_like,
            "traversal_component_count": traversal_like,
        },
        "flags": {
            "values": {f"0x{value:08x}": count for value, count in sorted(flags.items())},
            "observed_mask": f"0x{OBSERVED_FLAG_MASK:08x}",
            "unobserved_bits": {f"0x{value:08x}": count for value, count in sorted(unobserved_bits.items()) if value},
            "source_reported_unobserved_bits": {
                f"0x{value:08x}": count
                for value, count in sorted(source_reported_unobserved_bits.items())
                if value
            },
            "unknown_bits": {f"0x{value:08x}": count for value, count in sorted(unknown_bits.items()) if value},
            "directory_count": sum(entry.is_directory for entry in archive.entries),
            "compressed_count": sum(entry.is_compressed for entry in archive.entries),
        },
        "payload_bounds": {
            "non_directory_count": sum(not entry.is_directory for entry in archive.entries),
            "all_non_directory_entries_end_at_or_before_index": True,
        },
    }
    if inspected.sample_kinds:
        result["samples"] = [sample_entry(sample) for sample in inspected.samples]
    return result


def hash_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def selected_sample_entries(archive: Archive, sample_kinds: set[bool]) -> list[Entry]:
    selected = []
    for compressed in (False, True):
        if compressed not in sample_kinds:
            continue
        eligible = (
            entry
            for entry in archive.entries
            if not entry.is_directory
            and entry.stored_size > 0
            and entry.stored_size <= HARD_STORED_INPUT_CAP
            and entry.is_compressed == compressed
            and entry.unpacked_size <= HARD_OUTPUT_CAP
        )
        candidate = next((entry for entry in eligible if entry.unpacked_size <= PREFERRED_OUTPUT_CAP), None)
        if candidate is None:
            candidate = next(
                (
                    entry
                    for entry in archive.entries
                    if not entry.is_directory
                    and entry.stored_size > 0
                    and entry.stored_size <= HARD_STORED_INPUT_CAP
                    and entry.is_compressed == compressed
                    and entry.unpacked_size <= HARD_OUTPUT_CAP
                ),
                None,
            )
        if candidate is not None:
            selected.append(candidate)
    return selected


def sample_kinds_for_path(path: Path, sample_paths: set[Path], plain_sample_paths: set[Path]) -> set[bool]:
    kinds = {False, True} if path in sample_paths else set()
    if path in plain_sample_paths:
        kinds.add(False)
    return kinds


def inspect_archive(path: Path, sample_kinds: set[bool]) -> InspectedArchive:
    """Parse, select, and read each requested payload once for both consumers."""
    archive = parse_archive(path)
    samples = tuple(
        SampledEntry(entry, read_bounded_entry(archive, entry))
        for entry in selected_sample_entries(archive, sample_kinds)
    )
    return InspectedArchive(archive, frozenset(sample_kinds), samples)


def load_arkanum(source_root: Path) -> Any:
    """Load the opt-in comparator without copying or changing its source."""
    sys.path.insert(0, str(source_root))
    try:
        return importlib.import_module("formats.dat").Dat
    except ImportError as error:
        raise DatProbeError("Arkanum comparison source is not importable") from error


def compare_with_arkanum(inspected: InspectedArchive, Dat: Any) -> dict[str, object]:
    """Compare one inspection with a pinned source snapshot without rereading ours."""
    archive = inspected.archive
    alternate = Dat.open(str(archive.path))
    alternate_entries = list(alternate.name_to_entry.items())
    if len(alternate_entries) != len(archive.entries):
        raise DatProbeError("comparison: entry counts differ")
    for entry, (name, other) in zip(archive.entries, alternate_entries, strict=True):
        if name.encode("ascii") != entry.raw_name[:-1]:
            raise DatProbeError("comparison: entry path bytes differ")
        if (other.is_compressed, other.is_directory, other.full_size, other.compressed_size, other.location) != (
            entry.is_compressed,
            entry.is_directory,
            entry.unpacked_size,
            entry.stored_size,
            entry.offset,
        ):
            raise DatProbeError("comparison: entry fields differ")
    checked_samples = []
    for sample in inspected.samples:
        name, _other = alternate_entries[sample.entry.ordinal]
        if alternate[name] != sample.payload:
            raise DatProbeError("comparison: sampled payload differs")
        checked_samples.append(sample.entry.ordinal)
    return {
        "input": archive.path.name,
        "entry_count_equal": True,
        "all_entry_fields_equal": True,
        "sampled_payload_ordinals_equal": checked_samples,
    }


def inventory(
    paths: list[Path], sample_paths: set[Path], plain_sample_paths: set[Path], source_root: Path | None = None
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    """Inspect each input once, then feed its bounded bytes to both consumers."""
    Dat = load_arkanum(source_root) if source_root else None
    reports = []
    comparisons = []
    for path in paths:
        inspected = inspect_archive(path, sample_kinds_for_path(path, sample_paths, plain_sample_paths))
        report = archive_report(inspected)
        report["input"] = path.name
        report["sha256"] = hash_file(path)
        reports.append(report)
        if Dat is not None:
            comparisons.append(compare_with_arkanum(inspected, Dat))
    return reports, comparisons


def make_archive(entries: list[tuple[bytes, int, int, bytes]]) -> bytes:
    payload = bytearray()
    index = bytearray(U32.pack(len(entries)))
    for name, flags, declared_size, stored in entries:
        offset = len(payload)
        payload.extend(stored)
        encoded_name = name + b"\0"
        index.extend(U32.pack(len(encoded_name)))
        index.extend(encoded_name)
        index.extend(ENTRY_FIXED.pack(flags, declared_size, len(stored), offset))
    footer = FOOTER.pack(bytes(16), MARKER, sum(len(name) + 1 for name, *_ in entries), len(index) + FOOTER_SIZE)
    return bytes(payload + index + footer)


def self_test() -> None:
    """Exercise production parse and bounded-read paths using only temporary files."""
    def assert_rejected(action: Callable[[], object], label: str) -> None:
        try:
            action()
        except DatProbeError:
            return
        raise AssertionError(f"{label} synthetic archive was accepted")

    def write_fixture(directory: Path, name: str, data: bytes) -> Path:
        path = directory / name
        path.write_bytes(data)
        return path

    plain = b"plain"
    compressed = zlib.compress(b"compressed")
    valid = make_archive(
        [(b"plain.bin", PLAIN, len(plain), plain), (b"packed.bin", COMPRESSED, len(b"compressed"), compressed)]
    )

    with tempfile.TemporaryDirectory() as temporary_directory:
        directory = Path(temporary_directory)
        valid_path = write_fixture(directory, "valid.dat", valid)
        parsed = parse_archive(valid_path)
        assert len(parsed.entries) == 2
        assert read_bounded_entry(parsed, parsed.entries[0]) == plain
        assert read_bounded_entry(parsed, parsed.entries[1]) == b"compressed"

        assert_rejected(lambda: parse_archive(write_fixture(directory, "truncated-footer.dat", valid[: FOOTER_SIZE - 1])), "truncated footer")
        truncated_index = U32.pack(0)[:2] + FOOTER.pack(bytes(16), MARKER, 0, FOOTER_SIZE + 2)
        assert_rejected(lambda: parse_archive(write_fixture(directory, "truncated-index.dat", truncated_index)), "truncated index")
        truncated_name = bytearray(valid)
        struct.pack_into("<I", truncated_name, parsed.footer.index_start + U32.size, 0xFFFFFFFF)
        assert_rejected(lambda: parse_archive(write_fixture(directory, "truncated-name.dat", bytes(truncated_name))), "truncated name")

        bad_count = bytearray(make_archive([]))
        struct.pack_into("<I", bad_count, 0, 1)
        assert_rejected(lambda: parse_archive(write_fixture(directory, "unreachable-count.dat", bytes(bad_count))), "unreachable count")

        first_offset_position = parsed.footer.index_start + U32.size + U32.size + len(b"plain.bin\0") + 16
        bad_bounds = bytearray(valid)
        struct.pack_into("<I", bad_bounds, first_offset_position, parsed.footer.index_start)
        assert_rejected(lambda: parse_archive(write_fixture(directory, "bad-bounds.dat", bytes(bad_bounds))), "out-of-range payload")
        overflow_bounds = bytearray(valid)
        struct.pack_into("<I", overflow_bounds, first_offset_position, 0xFFFFFFFF)
        assert_rejected(lambda: parse_archive(write_fixture(directory, "overflow-bounds.dat", bytes(overflow_bounds))), "overflow payload")

        unobserved_path = write_fixture(directory, "unobserved-flags.dat", make_archive([(b"flags.bin", 0x120, 1, b"x")]))
        unobserved_report = archive_report(inspect_archive(unobserved_path, set()))
        flag_report = unobserved_report["flags"]
        assert isinstance(flag_report, dict)
        assert flag_report["unobserved_bits"] == {"0x00000120": 1}
        assert flag_report["source_reported_unobserved_bits"] == {"0x00000100": 1}
        assert flag_report["unknown_bits"] == {"0x00000020": 1}

        short_output_path = write_fixture(directory, "short-output.dat", make_archive([(b"short.bin", COMPRESSED, 4, zlib.compress(b"two"))]))
        short_output_archive = parse_archive(short_output_path)
        assert_rejected(
            lambda: read_bounded_entry(short_output_archive, short_output_archive.entries[0]),
            "declared-length mismatch",
        )
        excess_path = write_fixture(directory, "excess-output.dat", make_archive([(b"excess.bin", COMPRESSED, 1, zlib.compress(b"two"))]))
        excess_archive = parse_archive(excess_path)
        assert_rejected(lambda: read_bounded_entry(excess_archive, excess_archive.entries[0]), "excess output")
        trailing_path = write_fixture(directory, "trailing.dat", make_archive([(b"trailing.bin", COMPRESSED, 1, zlib.compress(b"x") + b"tail")]))
        trailing_archive = parse_archive(trailing_path)
        assert_rejected(lambda: read_bounded_entry(trailing_archive, trailing_archive.entries[0]), "trailing stream")
        incomplete_path = write_fixture(directory, "incomplete.dat", make_archive([(b"incomplete.bin", COMPRESSED, 3, zlib.compress(b"abc")[:-1])]))
        incomplete_archive = parse_archive(incomplete_path)
        assert_rejected(lambda: read_bounded_entry(incomplete_archive, incomplete_archive.entries[0]), "incomplete stream")
        malformed_path = write_fixture(directory, "malformed.dat", make_archive([(b"malformed.bin", COMPRESSED, 1, b"not zlib")]))
        malformed_archive = parse_archive(malformed_path)
        assert_rejected(lambda: read_bounded_entry(malformed_archive, malformed_archive.entries[0]), "malformed stream")

        stored_too_large = b"x" * (HARD_STORED_INPUT_CAP + 1)
        stored_cap_path = write_fixture(directory, "stored-cap.dat", make_archive([(b"stored.bin", PLAIN, 1, stored_too_large)]))
        stored_cap_archive = parse_archive(stored_cap_path)
        assert selected_sample_entries(stored_cap_archive, {False}) == []
        assert_rejected(lambda: read_bounded_entry(stored_cap_archive, stored_cap_archive.entries[0]), "stored-input hard cap")
        output_cap_path = write_fixture(directory, "output-cap.dat", make_archive([(b"output.bin", COMPRESSED, HARD_OUTPUT_CAP + 1, zlib.compress(b"x"))]))
        output_cap_archive = parse_archive(output_cap_path)
        assert_rejected(lambda: read_bounded_entry(output_cap_archive, output_cap_archive.entries[0]), "output hard cap")

        fallback_plain = b"f" * (PREFERRED_OUTPUT_CAP + 1)
        fallback_path = write_fixture(directory, "fallback.dat", make_archive([(b"fallback.bin", PLAIN, len(fallback_plain), fallback_plain)]))
        fallback_archive = parse_archive(fallback_path)
        selected = selected_sample_entries(fallback_archive, {False})
        assert selected == [fallback_archive.entries[0]]
        assert read_bounded_entry(fallback_archive, selected[0]) == fallback_plain

        comparator_accesses: list[str] = []

        class FakeEntry:
            def __init__(self, entry: Entry) -> None:
                self.is_compressed = entry.is_compressed
                self.is_directory = entry.is_directory
                self.full_size = entry.unpacked_size
                self.compressed_size = entry.stored_size
                self.location = entry.offset

        class FakeArchive:
            def __init__(self, archive: Archive) -> None:
                self.name_to_entry = {
                    entry.raw_name[:-1].decode("ascii"): FakeEntry(entry) for entry in archive.entries
                }
                self.payloads = {"plain.bin": plain, "packed.bin": b"compressed"}

            def __getitem__(self, name: str) -> bytes:
                comparator_accesses.append(name)
                return self.payloads[name]

        class FakeDat:
            @staticmethod
            def open(_path: str) -> FakeArchive:
                return FakeArchive(parsed)

        original_reader = read_bounded_entry
        original_import = importlib.import_module
        production_reads: list[tuple[str, int]] = []

        def counted_reader(archive: Archive, entry: Entry) -> bytes:
            production_reads.append((archive.path.name, entry.ordinal))
            return original_reader(archive, entry)

        def fake_import(name: str, *args: Any, **kwargs: Any) -> Any:
            if name == "formats.dat":
                return type("FakeDatModule", (), {"Dat": FakeDat})
            return original_import(name, *args, **kwargs)

        try:
            globals()["read_bounded_entry"] = counted_reader
            importlib.import_module = fake_import
            with contextlib.redirect_stdout(io.StringIO()):
                assert main(
                    [str(valid_path), "--sample", str(valid_path), "--compare-arkanum", str(directory)]
                ) == 0
            assert production_reads == [("valid.dat", 0), ("valid.dat", 1)]
            assert comparator_accesses == ["plain.bin", "packed.bin"]

            production_reads.clear()
            comparator_accesses.clear()
            with contextlib.redirect_stdout(io.StringIO()):
                assert main([str(valid_path), "--compare-arkanum", str(directory)]) == 0
            assert production_reads == []
            assert comparator_accesses == []
        finally:
            globals()["read_bounded_entry"] = original_reader
            importlib.import_module = original_import


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", type=Path, help="explicit local DAT/PATCH candidate paths")
    parser.add_argument("--sample", action="append", default=[], type=Path, help="path eligible for bounded in-memory sampling")
    parser.add_argument("--plain-sample", action="append", default=[], type=Path, help="path eligible only for a bounded plain sample")
    parser.add_argument("--compare-arkanum", type=Path, help="opt-in path to a pinned Arkanum source checkout for comparison")
    parser.add_argument("--self-test", action="store_true", help="run synthetic structural checks only")
    args = parser.parse_args(argv)
    if args.self_test:
        self_test()
        print("self-test: PASS")
    if args.paths:
        reports, comparisons = inventory(
            args.paths, set(args.sample), set(args.plain_sample), args.compare_arkanum
        )
        result: dict[str, object] = {"inventory": reports}
        if args.compare_arkanum:
            result["arkanum_comparison"] = comparisons
        print(json.dumps(result, indent=2, sort_keys=True))
    if not args.self_test and not args.paths:
        parser.error("supply --self-test and/or explicit input paths")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except DatProbeError as error:
        print(f"dat-probe: {error}", file=sys.stderr)
        raise SystemExit(2)
