#!/usr/bin/env python3
"""Independent MIT research helpers; headers/identity only, never audio decoding.

Opt-in reports can contain original observation metadata: keep them local.
The selected-source profile is explicit, not retail archive precedence.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass, field
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys

import dat_probe as dat

TABLE_CAP = 65536
HEADER_CAP = 65536
LOOSE_SIZE_CAP = 8 * 1024 * 1024
ARCHIVE_CAP = 512 * 1024 * 1024
INDEX_CAP = 32 * 1024 * 1024


class AudioProbeError(ValueError):
    pass


def require(condition: bool, label: str) -> None:
    if not condition:
        raise AudioProbeError(label)


def resource_key(name: str) -> str:
    require(isinstance(name, str) and 0 < len(name) <= 240, 'PathLimit')
    require(all(32 <= ord(c) < 127 for c in name), 'PathEncoding')
    normalized = name.replace('\\', '/')
    require(not normalized.startswith('/') and ':' not in normalized, 'AbsolutePath')
    require(all(p not in ('', '.', '..') for p in normalized.split('/')), 'TraversalPath')
    return normalized.lower()


def parse_mes(data: bytes) -> dict[int, str]:
    """Strict bounded ASCII research subset, preserving numeric identity.

    Comments are discarded before parsing. Duplicate keys fail, not last-wins.
    This is not a replacement for the project's original-text MES reader.
    """
    require(0 < len(data) <= TABLE_CAP, 'TableLimit')
    # Work in bytes: prose/comments outside records have unknown encoding and
    # are not resource identity. Never guess a codepage for an actual value.
    cleaned = bytearray()
    cursor, in_field = 0, False
    while cursor < len(data):
        if not in_field and data[cursor:cursor+2] == b'/*':
            end = data.find(b'*/', cursor+2)
            require(end >= 0, 'MalformedTableComment')
            cursor = end + 2
            continue
        if not in_field and data[cursor:cursor+2] == b'//':
            end = data.find(b'\n', cursor+2)
            cursor = len(data) if end < 0 else end + 1
            continue
        value = data[cursor]
        if value == ord('{'):
            require(not in_field, 'MalformedTable')
            in_field = True
        elif value == ord('}'):
            require(in_field, 'MalformedTable')
            in_field = False
        cleaned.append(value)
        cursor += 1
    require(not in_field, 'MalformedTable')
    clean = bytes(cleaned)
    pattern = rb'\{([^{}]*)\}\s*\{([^{}]*)\}'
    records = re.findall(pattern, clean)
    result = {}
    require(len(records) <= 4096, 'TableEntryLimit')
    for key_bytes, value_bytes in records:
        try:
            key, value = key_bytes.decode('ascii'), value_bytes.decode('ascii')
        except UnicodeDecodeError as error:
            raise AudioProbeError('TableEncodingUnknown') from error
        require(re.fullmatch(r'-?\d{1,10}', key.strip()) is not None, 'TableKey')
        number = int(key)
        require(-(2**31) <= number < 2**31 and len(value) <= 1024, 'TableFieldLimit')
        require(number not in result, 'DuplicateTableId')
        result[number] = value.strip()
    # Detect unmatched/malformed braces rather than silently dropping a record.
    residue = re.sub(pattern, b'', clean)
    require(b'{' not in residue and b'}' not in residue, 'MalformedTable')
    require(bool(result), 'EmptyTable')
    return result


def resolve_sfx(cue: int, ordered_tables: list[dict[int, str]],
                user: dict[int, str]) -> str | None:
    for table in [*ordered_tables, user]:
        if cue in table:
            return resource_key('sound/' + table[cue])
    return None


def speech_candidates(identity: int, gender_nonzero: bool) -> tuple[str, ...]:
    require(type(identity) is int and (identity == -1 or 0 <= identity <= 0x7fffffff),
            'SpeechIdentity')
    if identity == -1:
        return ()
    directory, line = (identity >> 16) & 0x7fff, identity & 0xffff
    stem = f'sound/speech/{directory:05d}/v{line}'
    return (stem + '_m.mp3',) if gender_nonzero else (stem + '_f.mp3', stem + '_m.mp3')


def resolve_speech(identity: int, gender_nonzero: bool, available: set[str]) -> str | None:
    return next((p for p in speech_candidates(identity, gender_nonzero) if p in available), None)


@dataclass(frozen=True)
class SchemeEntry:
    path: str
    mode: str = 'ambient'
    hours: tuple[int, int] = (0, 23)
    frequency: int = 5
    volume_percent: tuple[int, int] = (100, 100)
    balance_percent: tuple[int, int] = (50, 50)

    def eligible(self, hour: int, draw: int) -> bool:
        require(type(hour) is int and 0 <= hour <= 23, 'HourRange')
        require(type(draw) is int and 0 <= draw <= 999, 'AudioDrawRange')
        return self.hours[0] <= hour <= self.hours[1] and (self.mode != 'ambient' or draw < self.frequency)


def parse_scheme(text: str) -> SchemeEntry:
    """Intentional strict proposed subset, not an emulation of the source's substring/atoi scan."""
    require(0 < len(text) <= 1024, 'SchemeTextLimit')
    parts = text.lower().split()
    require(bool(parts), 'SchemeTextLimit')
    reference = parts[0]
    if reference.startswith('#'):
        require(re.fullmatch(r'#[0-9]{1,10}', reference) is not None, 'SchemeReference')
        require(0 < int(reference[1:]) <= 0x7fffffff, 'SchemeReference')
    else:
        resource_key('sound/' + reference)
    options = {}
    modes = []
    for part in parts[1:]:
        if part in ('/loop', '/anchor', '/over'):
            modes.append(part[1:])
            continue
        match = re.fullmatch(r'/(vol|time|freq|bal|scatter):([0-9]+)(?:[,-]([0-9]+))?', part)
        if match is None:
            raise AudioProbeError('UnsupportedSchemeOption')
        key, first, last = match.groups()
        require(key not in options, 'DuplicateSchemeOption')
        options[key] = (int(first), int(last if last is not None else first))
    require(len(modes) <= 1, 'AmbiguousSchemeMode')
    mode = modes[0] if modes else 'ambient'
    hours = options.get('time', (0, 23))
    vol = options.get('vol', (100, 100))
    bal = options.get('bal', (50, 50))
    freq = options.get('freq', (5, 5))
    require(0 <= hours[0] <= hours[1] <= 23, 'UnsupportedHourWindow')
    require(0 <= vol[0] <= vol[1] <= 100 and 0 <= bal[0] <= bal[1] <= 100, 'PercentRange')
    require(freq[0] == freq[1] and 0 <= freq[0] <= 1000, 'FrequencyRange')
    if 'scatter' in options:
        scatter = options['scatter']
        require(mode == 'ambient' and scatter[0] == scatter[1] and 0 <= scatter[0] <= 100,
                'UnsupportedScatter')
        vol = (100 - scatter[0], 100)
        bal = (50 - scatter[0] // 2, 50 + scatter[0] // 2)
    require(mode == 'ambient' or not any(k in options for k in ('freq', 'bal', 'scatter')),
            'UnsupportedSongOption')
    require(mode in ('ambient', 'loop') or 'time' not in options, 'UnsupportedSongTime')
    return SchemeEntry(reference, mode, hours, freq[0], vol, bal)


def scheme_entries(identity: int, index: dict[int, str], listing: dict[int, str]) -> list[tuple[int, SchemeEntry]]:
    if identity == 0:
        return []
    require(identity in index, 'MissingSchemeId')
    match = re.search(r'#([0-9]+)\s*$', index[identity])
    if match is None or not 0 < int(match[1]) <= 0x7fffffff - 99:
        raise AudioProbeError('SchemeBase')
    base = int(match[1])
    selected = [(key, parse_scheme(listing[key])) for key in range(base, base + 100)
                if key in listing and listing[key].strip()]
    require(len(selected) <= 25, 'SchemeSoundLimit')
    return selected


def wav_header(data: bytes) -> dict:
    require(12 <= len(data) <= dat.HARD_OUTPUT_CAP, 'WavSampleLimit')
    require(data[:4] == b'RIFF' and data[8:12] == b'WAVE', 'WavSignature')
    require(struct.unpack_from('<I', data, 4)[0] + 8 == len(data), 'WavRiffLength')
    cursor, fmt, audio_bytes, loops = 12, None, None, []
    chunks = 0
    while cursor < len(data):
        require(cursor + 8 <= len(data), 'WavChunkHeader')
        tag, size = data[cursor:cursor+4], struct.unpack_from('<I', data, cursor+4)[0]
        start, end = cursor + 8, cursor + 8 + size
        require(end + (size & 1) <= len(data), 'WavChunkBounds')
        chunks += 1
        require(chunks <= 128, 'WavChunkLimit')
        if tag == b'fmt ':
            require(fmt is None and size >= 16, 'WavFormatChunk')
            codec, channels, rate, byte_rate, alignment, bits = struct.unpack_from('<HHIIHH', data, start)
            require(codec == 1 and channels in (1, 2) and bits == 16 and rate in (11025, 22050, 44100, 48000), 'UnsupportedWavClass')
            require(alignment == channels * 2 and byte_rate == rate * alignment, 'WavPcmGeometry')
            fmt = {'codec': 'PCM16', 'channels': channels, 'sample_rate': rate, 'alignment': alignment}
        elif tag == b'data':
            require(audio_bytes is None, 'DuplicateWavData')
            audio_bytes = size
        elif tag == b'smpl':
            require(size >= 36, 'WavSmplHeader')
            count = struct.unpack_from('<I', data, start+28)[0]
            require(len(loops) + count <= 16 and 36 + 24 * count <= size, 'WavLoopLimit')
            for i in range(count):
                _, kind, first, last, fraction, repeat = struct.unpack_from('<6I', data, start+36+24*i)
                loops.append({'type': kind, 'start': first, 'end_inclusive': last, 'fraction': fraction, 'repeat': repeat})
        cursor = end + (size & 1)
    if fmt is None or audio_bytes is None:
        raise AudioProbeError('WavRequiredChunks')
    require(audio_bytes > 0 and audio_bytes % fmt['alignment'] == 0, 'WavDataGeometry')
    frames = audio_bytes // fmt['alignment']
    require(all(0 <= x['start'] <= x['end_inclusive'] < frames for x in loops), 'WavLoopBounds')
    return {**fmt, 'data_bytes': audio_bytes, 'frames': frames, 'smpl_loops': loops,
            'proof': 'header-only; not decoded or played'}


def mpeg_frame(data: bytes, offset: int) -> tuple[int, dict]:
    require(offset + 4 <= len(data), 'Mp3FrameHeader')
    word = int.from_bytes(data[offset:offset+4], 'big')
    require(word >> 21 == 0x7ff, 'Mp3Sync')
    version, layer, bitrate, rate = (word >> 19) & 3, (word >> 17) & 3, (word >> 12) & 15, (word >> 10) & 3
    require(version in (0, 2, 3) and layer == 1 and 1 <= bitrate <= 14 and rate < 3,
            'UnsupportedMp3Class')
    rates = (44100, 48000, 32000)
    sample_rate = rates[rate] // (1 if version == 3 else 2 if version == 2 else 4)
    kbps = ((32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320)
            if version == 3 else (8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160))[bitrate-1]
    length = (144 if version == 3 else 72) * kbps * 1000 // sample_rate + ((word >> 9) & 1)
    return length, {'mpeg_version': {3: '1', 2: '2', 0: '2.5'}[version], 'layer': 3,
                    'sample_rate': sample_rate, 'bitrate_kbps': kbps,
                    'channels': 1 if (word >> 6) & 3 == 3 else 2}


def mp3_header(prefix: bytes) -> dict:
    require(8 <= len(prefix) <= HEADER_CAP, 'Mp3PrefixLimit')
    start = 0
    if prefix[:3] == b'ID3':
        require(len(prefix) >= 10 and prefix[3] in (3, 4), 'UnsupportedId3')
        require(all(b < 128 for b in prefix[6:10]), 'Id3Synchsafe')
        size = 0
        for b in prefix[6:10]:
            size = size * 128 + b
        start = 10 + size + (10 if prefix[3] == 4 and prefix[5] & 16 else 0)
        require(start + 8 <= len(prefix), 'Id3BeyondPrefix')
    # Two consecutive layer-III frames, not one accidental sync in arbitrary bytes.
    for pos in range(start, min(start + 4096, len(prefix)-7)):
        try:
            length, first = mpeg_frame(prefix, pos)
            next_length, second = mpeg_frame(prefix, pos + length)
            require(pos + length + next_length <= len(prefix), 'Mp3IncompleteFrames')
            require(all(first[k] == second[k] for k in ('mpeg_version', 'sample_rate', 'channels')), 'Mp3FrameMismatch')
            return {**first, 'first_frame_offset': pos, 'two_frames': True,
                    'proof': 'header-only; duration/VBR/decoder/playback unknown'}
        except AudioProbeError:
            continue
    raise AudioProbeError('NoBoundedMp3FramePair')


def sector_sound(data: bytes) -> dict:
    """Selected framing only. No object, DIF or gameplay decoding."""
    require(4 <= len(data) <= dat.HARD_OUTPUT_CAP, 'SectorLimit')
    cursor = 0
    def take(size: int) -> bytes:
        nonlocal cursor
        require(0 <= size <= len(data) - cursor, 'SectorTruncated')
        value = data[cursor:cursor+size]
        cursor += size
        return value
    def integer() -> int:
        return struct.unpack('<i', take(4))[0]
    lights = integer()
    require(0 <= lights <= 4096, 'SectorLightLimit')
    take(lights * 48 + 4096 * 4)
    roof = integer()
    require(roof in (0, 1), 'SectorRoof')
    if roof == 0:
        take(1024)
    version = integer()
    require(0xaa0000 <= version <= 0xaa0004, 'SectorVersion')
    if version == 0xaa0000:
        return {'version': version, 'sound': None}
    scripts = integer()
    require(0 <= scripts <= 4096, 'SectorScriptLimit')
    take(scripts * 24)
    if version >= 0xaa0002:
        take(12)
    if version < 0xaa0003:
        return {'version': version, 'sound': None}
    take(12)  # townmap, aptitude, lighting; not sound identity
    offset = cursor
    flags, music, ambient = struct.unpack('<Iii', take(12))
    require(music >= 0 and ambient >= 0, 'SectorSchemeId')
    return {'version': version, 'offset': offset, 'bytes': 12,
            'flags_raw': flags, 'music': music, 'ambient': ambient}


def positional_distance(dx: int, dy: int) -> int:
    require(type(dx) is int and type(dy) is int and abs(dx) <= 1000000 and abs(dy) <= 1000000, 'CoordinateLimit')
    return math.isqrt(dx*dx + 4*dy*dy)


@dataclass
class OwnershipScenario:
    """Synthetic proposed lifecycle seam, NOT a playback implementation."""
    generation: int = 1
    serial: int = 0
    active: dict[tuple[int, int], str] = field(default_factory=dict)
    schemes: tuple[int, int] = (0, 0)

    def play(self, owner: str) -> tuple[int, int]:
        require(bool(owner) and len(self.active) < 32, 'VoiceBudget')
        self.serial += 1
        token = (self.generation, self.serial)
        self.active[token] = owner
        return token

    def cancel(self, token: tuple[int, int]) -> None:
        self.active.pop(token, None)

    def close_owner(self, owner: str) -> None:
        self.active = {k: v for k, v in self.active.items() if v != owner}

    def set_schemes(self, music: int, ambient: int) -> bool:
        require(type(music) is int and type(ambient) is int and music >= 0 and ambient >= 0, 'SchemeIdentity')
        pair = (music, 0 if music == ambient else ambient)
        changed = pair != self.schemes
        self.schemes = pair
        return changed

    def reset(self) -> None:
        self.generation += 1
        self.active.clear()
        self.schemes = (0, 0)


def selected_dat(path: Path, name: str) -> tuple[bytes, dict]:
    require(path.is_file() and dat.FOOTER_SIZE <= path.stat().st_size <= ARCHIVE_CAP, 'ArchiveLimit')
    # Bound existing DAT parser before it allocates/reads the index.
    with path.open('rb') as stream:
        stream.seek(-dat.FOOTER_SIZE, 2)
        footer = dat.read_exact(stream, dat.FOOTER_SIZE, 'footer')
        tail = dat.FOOTER.unpack(footer)[3]
        require(dat.FOOTER_SIZE + 4 <= tail <= min(path.stat().st_size, INDEX_CAP), 'IndexLimit')
        stream.seek(path.stat().st_size - tail)
        require(dat.U32.unpack(dat.read_exact(stream, 4, 'count'))[0] <= 200000, 'IndexEntryLimit')
    before = dat.hash_file(path)
    archive = dat.parse_archive(path)
    key = resource_key(name)
    matches = []
    for entry in archive.entries:
        if entry.is_directory:
            continue
        try:
            candidate = resource_key(entry.raw_name[:-1].decode('ascii'))
        except (UnicodeDecodeError, AudioProbeError):
            continue
        if candidate == key:
            matches.append(entry)
    require(len(matches) == 1, 'MissingOrDuplicateResource')
    entry = matches[0]
    require(entry.flags in (dat.PLAIN, dat.COMPRESSED), 'UnsupportedDatFlags')
    data = dat.read_bounded_entry(archive, entry)
    after = dat.hash_file(path)
    require(before == after, 'OriginalChanged')
    return data, {'source': 'explicit-dat', 'resource': key, 'ordinal': entry.ordinal,
                  'offset': entry.offset, 'stored': entry.stored_size, 'decoded': entry.unpacked_size,
                  'archive_sha256_before': before, 'archive_sha256_after': after,
                  'payload_sha256': hashlib.sha256(data).hexdigest()}


def selected_loose(root: Path, name: str) -> tuple[bytes, dict]:
    key = resource_key(name)
    parts = name.replace('\\', '/').split('/')
    path = root
    require(not root.is_symlink(), 'LooseSymlink')
    for part in parts:
        path = path / part
        require(not path.is_symlink(), 'LooseSymlink')
    require(path.resolve().is_relative_to(root.resolve()), 'LooseOutsideRoot')
    require(path.is_file() and 0 < path.stat().st_size <= LOOSE_SIZE_CAP, 'LooseSizeLimit')
    before = dat.hash_file(path)
    with path.open('rb') as stream:
        prefix = stream.read(HEADER_CAP)
    after = dat.hash_file(path)
    require(before == after, 'OriginalChanged')
    return prefix, {'source': 'explicit-loose', 'resource': key, 'file_bytes': path.stat().st_size,
                    'prefix_bytes': len(prefix), 'sha256_before': before, 'sha256_after': after}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--self-test', action='store_true')
    group = parser.add_mutually_exclusive_group()
    group.add_argument('--dat', type=Path)
    group.add_argument('--loose-root', type=Path)
    parser.add_argument('--entry')
    parser.add_argument('--kind', choices=('table', 'wav', 'mp3', 'sector'))
    args = parser.parse_args(argv)
    if args.self_test:
        import audio_resource_probe_tests as tests
        tests.run()
    if args.dat or args.loose_root:
        if not args.entry or not args.kind:
            parser.error('selected input requires --entry and --kind')
        if args.loose_root and args.kind != 'mp3':
            parser.error('loose bounded prefix profile supports only MP3')
        data, report = selected_dat(args.dat, args.entry) if args.dat else selected_loose(args.loose_root, args.entry)
        if args.kind == 'table':
            table = parse_mes(data)
            report['shape'] = {'entries': len(table), 'duplicate_policy': 'reject'}
        else:
            report['shape'] = {'wav': wav_header, 'mp3': lambda b: mp3_header(b[:HEADER_CAP]), 'sector': sector_sound}[args.kind](data)
        print(json.dumps(report, indent=2, sort_keys=True))
    elif not args.self_test:
        parser.error('supply --self-test or one explicit selected source')
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (AudioProbeError, dat.DatProbeError, OSError) as error:
        print(f'audio-probe: {error}', file=sys.stderr)
        raise SystemExit(2)
