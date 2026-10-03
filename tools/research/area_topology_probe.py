#!/usr/bin/env python3
"""Bounded, read-only occurrence inventory; no VM, reachability or precedence.
Specification: pinned CE script.h/script.c locators in area contract. This is
independently authored tooling, not a translated CE implementation.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import dat_probe

CAP = 1024 * 1024


def fingerprint(path):
    with path.open('rb') as stream:
        return {'bytes': path.stat().st_size, 'sha256': hashlib.file_digest(stream, 'sha256').hexdigest()}


def actions(payload):
    if not 64 <= len(payload) <= CAP:
        raise ValueError('SCRIPT_ENTRY_BOUND')
    count, capacity = struct.unpack_from('<ii', payload, 52)
    if not 0 <= count <= capacity or count > (CAP - 64) // 132 or len(payload) != 64 + count * 132:
        raise ValueError('SCRIPT_FRAMING')
    result = []
    for line in range(count):
        for branch, delta in (('true', 44), ('false', 88)):
            offset = 64 + line * 132 + delta
            code = struct.unpack_from('<i', payload, offset)[0]
            if code not in (6, 7, 44, 54, 55, 81, 99, 102):
                continue
            kinds = list(payload[offset + 4:offset + 12])
            values = list(struct.unpack_from('<8i', payload, offset + 12))
            row = {'line': line, 'branch': branch, 'action': code, 'operand_types': kinds, 'values': values}
            if code in (44, 102):
                slots = (1, 2, 3) if code == 44 else (4, 5, 6)
                row['kind'] = 'potential_teleport' if all(kinds[i] == 3 for i in slots) else 'computed_endpoint_gap'
                row['destination'] = [values[i] for i in slots]
            elif code in (7, 54, 55):
                row['kind'] = 'literal_call' if kinds[0] == 3 else 'computed_call_gap'
                row['target'] = values[0]
                row['entry_line_literal'] = kinds[1] == 3
                row['scheduled'] = code in (54, 55)
            elif code == 81:
                row['kind'] = 'object_focus_SAP_gap'  # Never use SAP as script ID.
            else:
                row['kind'] = 'script_change_gap'
            result.append(row)
    return result


def attachments(report):
    rows = []
    for domain, sectors in [('exterior', report['sectors']['selected']), ('cave', report['cave']['sectors']['selected'])]:
        for sector in sectors:
            origin = {'domain': domain, 'sector': sector['id']}
            for node in sector['attachments']:
                words = node['words']
                rows.append(dict(origin, source='tile', tile_id=words[1], sap=1, flags=words[2], counters=words[3], script=words[4]))
            if sector['sector_script'] is not None:
                rows.append(dict(origin, source='sector', **sector['sector_script']))
            for obj in sector['statics']:
                for script in obj['scripts']:
                    rows.append(dict(origin, source='sector_object', object_index=obj['SourceIndex'], inherited=obj['scripts_inherited'], **script))
    for obj in report['visuals']:
        for script in obj['scripts']:
            rows.append(dict(domain='exterior', source='mobile', aggregate_index=obj['index'], inherited=obj['scripts_inherited'], **script))
    for obj in report['cave']['mobiles']:
        for script in obj['scripts']:
            rows.append(dict(domain='cave', source='editor_mobile', ordinal=obj['ordinal'], inherited=obj['scripts_inherited'], **script))
    return [row for row in rows if row['script'] != 0]


def scan(roots, sources, read):
    checks, gaps, occurrences = [], [], []
    loaded = {}
    def inspect(script, hop, caller=None):
        variants = sources.get(script, [])
        if len(variants) != 1:
            gaps.append({'script': script, 'hop': hop, 'caller': caller, 'reason': 'source_missing_or_ambiguous', 'matches': len(variants)})
        for variant in variants:
            key = (script, variant['source'], variant['ordinal'])
            if key not in loaded:
                payload = read(variant)
                parsed = actions(payload)
                loaded[key] = parsed
                checks.append(dict(variant, script=script, bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest(), lines=(len(payload) - 64) // 132))
            for action in loaded[key]:
                row = dict(action, script=script, hop=hop, source=variant['source'], ordinal=variant['ordinal'], ambiguous=len(variants) != 1, caller=caller, activation='NOT_VERIFIED')
                occurrences.append(row)
                if action['kind'] == 'literal_call':
                    if hop == 0 and action['target'] not in roots:
                        inspect(action['target'], 1, {'script': script, 'line': action['line'], 'branch': action['branch'], 'source': variant['source'], 'scheduled': action['scheduled'], 'entry_line': action['values'][1], 'entry_line_literal': action['entry_line_literal']})
                    else:
                        gaps.append({'script': script, 'target': action['target'], 'hop': hop, 'reason': 'depth_or_root_cycle_stop'})
                elif action['kind'] != 'potential_teleport':
                    gaps.append({'script': script, 'line': action['line'], 'branch': action['branch'], 'hop': hop, 'reason': action['kind']})
    for script in sorted(roots):
        inspect(script, 0)
    return checks, gaps, occurrences


def self_test():
    # Literal wire offsets/values: not an encoder sharing parser enums/layout.
    payload = bytearray(196)
    struct.pack_into('<ii', payload, 52, 1, 1)
    struct.pack_into('<i', payload, 108, 44)
    for slot, value in zip((1, 2, 3), (5078, 2087, 2018)):
        payload[112 + slot] = 3
        struct.pack_into('<i', payload, 120 + slot * 4, value)
    struct.pack_into('<i', payload, 152, 102)
    for slot, value in zip((4, 5, 6), (5078, 2087, 2018)):
        payload[156 + slot] = 3
        struct.pack_into('<i', payload, 164 + slot * 4, value)
    parsed = actions(payload)
    assert [r['destination'] for r in parsed] == [[5078, 2087, 2018], [5078, 2087, 2018]]
    assert [r['branch'] for r in parsed] == ['true', 'false']
    payload[113] = 4
    assert actions(payload)[0]['kind'] == 'computed_endpoint_gap'
    sentinels = bytearray(196)
    struct.pack_into('<ii', sentinels, 52, 1, 1)
    struct.pack_into('<i', sentinels, 0, 44)
    struct.pack_into('<i', sentinels, 64, 102)
    struct.pack_into('<i', sentinels, 120, 44)
    assert actions(sentinels) == []
    assert actions(bytes(64)) == []
    for bad in (bytes(63), bytes(65), bytes(CAP + 1), payload[:-1]):
        try:
            actions(bad)
        except ValueError:
            pass
        else:
            raise AssertionError('truncation/cap')
    call = bytearray(196)
    struct.pack_into('<ii', call, 52, 1, 1)
    struct.pack_into('<i', call, 108, 7)
    call[112] = 3
    call[113] = 3
    struct.pack_into('<i', call, 120, 5001)
    sources = {5000: [{'source': 0, 'ordinal': 0}], 5001: [{'source': 0, 'ordinal': 1}]}
    checks, gaps, found = scan({5000}, sources, lambda v: call if v['ordinal'] == 0 else payload)
    assert len(checks) == 2 and any(r['kind'] == 'potential_teleport' and r['hop'] == 1 for r in found)
    struct.pack_into('<i', call, 108, 81)
    assert actions(call)[0]['kind'] == 'object_focus_SAP_gap'
    struct.pack_into('<ii', call, 52, 2, 1)
    try:
        actions(call)
    except ValueError:
        pass
    else:
        raise AssertionError('count capacity')
    print('area-topology-self-test: PASS (literal branches/operands/sentinels/one-hop/object-call/framing)')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--self-test', action='store_true')
    parser.add_argument('--inventory', type=Path)
    parser.add_argument('--archive', type=Path, action='append', default=[])
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not args.inventory or not args.archive:
        parser.error('--inventory and explicit --archive inputs required')
    report = json.loads(args.inventory.read_text())
    before = [fingerprint(path) for path in args.archive]
    if before[0] != {'bytes': report['module']['Bytes'], 'sha256': report['module']['Sha256']}:
        raise ValueError('MODULE_IDENTITY')
    archives = [dat_probe.parse_archive(path) for path in args.archive]
    sources = {}
    for index, archive in enumerate(archives):
        for entry in archive.entries:
            match = re.fullmatch(rb'(?i)scr[\\/](\d+)[^\\/]*\.scr\0', entry.raw_name)
            if match and not entry.is_directory:
                sources.setdefault(int(match[1]), []).append({'source': index, 'ordinal': entry.ordinal})
    attached = attachments(report)
    roots = {row['script'] for row in attached}
    # Freeze finite direct budget BEFORE payload reads. One-hop targets freeze
    # their own per-entry size before read; no directory-wide payload crawl.
    direct = [v for script in roots for v in sources.get(script, [])]
    budget = {'attached_ids': len(roots), 'direct_attempts': len(direct), 'direct_stored_bytes': sum(archives[v['source']].entries[v['ordinal']].stored_size for v in direct), 'direct_decoded_bytes': sum(archives[v['source']].entries[v['ordinal']].unpacked_size for v in direct)}
    read_manifest = []
    def read(variant):
        archive = archives[variant['source']]
        entry = archive.entries[variant['ordinal']]
        if max(entry.stored_size, entry.unpacked_size) > CAP:
            raise ValueError('SCRIPT_ENTRY_BOUND')
        read_manifest.append(dict(variant, stored_bytes=entry.stored_size, decoded_bytes=entry.unpacked_size))
        return dat_probe.read_bounded_entry(archive, entry)
    checks, gaps, occurrences = scan(roots, sources, read)
    if before != [fingerprint(path) for path in args.archive]:
        raise ValueError('INPUT_CHANGED')
    print(json.dumps({'schema_version': 1, 'inputs': before, 'attachments': attached, 'budget': budget, 'reads': read_manifest, 'checks': checks, 'gaps': gaps, 'occurrences': occurrences}, indent=2))


if __name__ == '__main__':
    main()
