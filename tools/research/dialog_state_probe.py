#!/usr/bin/env python3
"""ARC-RES-019 read-only worksheet, not a dialog/state runtime or CE translation.

Public inputs in self-test are invented. Original mode publishes only hashes,
identifiers and operation shapes; it never writes extracted payloads or text.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import tempfile

import dat_probe

PIN = "a7ff41b300ef712f0e7d088183a3d08957110cdd"


class WorksheetFailure(ValueError):
    """Only authored static codes, never input-derived messages."""


def require(value: bool, code: str) -> None:
    if not value:
        raise WorksheetFailure(code)


def digest(path: Path) -> str:
    return dat_probe.hash_file(path)


def threshold(value: int, encoded: int) -> bool:
    # Algebraic threshold worksheet; not a query adapter.
    return value <= -encoded if encoded < 0 else value >= encoded


def eligible(rows: list[tuple[int, int, int, int]], gender: int, iq: int,
             reaction: int) -> list[int]:
    # rows: (synthetic identity, gender selector, IQ gate, RE gate).
    return [identity for identity, selector, iq_gate, re_gate in rows
            if selector in (-1, gender) and threshold(iq, iq_gate)
            and threshold(reaction, re_gate)]


def self_test() -> dict:
    checks = 0

    def check(actual, expected):
        nonlocal checks
        require(actual == expected, "SyntheticOracleMismatch")
        checks += 1

    # Known-answer file hashes exercise the same streaming path as original mode.
    with tempfile.TemporaryDirectory(prefix="dialog-state-selftest-") as directory:
        sample = Path(directory) / "synthetic.bin"
        for payload, expected in [
            (b"", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
            (b"abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]:
            sample.write_bytes(payload)
            check(digest(sample), expected)

    # Literal boundary oracles, not values derived from a proposed API.
    for reaction, token, expected in [(39, 40, False), (40, 40, True),
                                      (41, 40, True), (39, -40, True),
                                      (40, -40, True), (41, -40, False),
                                      (-1, 0, False), (0, 0, True)]:
        check(threshold(reaction, token), expected)
    rows = [(201, -1, 5, 40), (202, -1, 5, -39),
            (203, -1, -4, 40), (204, -1, -4, -39),
            (205, 1, 5, 40), (206, 0, -4, 40)]
    for gender, iq, reaction, expected in [
        (0, 7, 50, [201]), (0, 7, 20, [202]),
        (0, 3, 50, [203, 206]), (0, 3, 20, [204]),
        (1, 7, 50, [201, 205]), (1, 3, 50, [203])]:
        check(eligible(rows, gender, iq, reaction), expected)
    check(eligible([(i, -1, 1, 0) for i in range(201, 207)], 0, 7, 50),
          [201, 202, 203, 204, 205, 206])  # overflow is not truncation
    # GF bit worksheet and post-effect gate, two ordered writes.
    word = 0
    word |= 1 << 3
    check((word >> 3) & 1, 1)
    word &= ~(1 << 3)
    check((word >> 3) & 1, 0)
    # Reaction action classification: unsigned target vs signed delta.
    old = 50
    check(old + (35 - old), 35)
    check(old + 5, 55)
    check(old - 5, 45)
    # Item-name lookup selects first INSTANCE, not prototype or quantity.
    inventory = [("I1", 801, 3), ("I2", 801, 1), ("I3", 802, 2)]
    check(next((row[0] for row in inventory if row[1] == 801), None), "I1")
    check(next((row[0] for row in inventory if row[1] == 999), None), None)
    # Trace worksheet: source effects precede selected action and target effects.
    entry = ["GF1", "GF2", "GF3", "IN4", "GF5", "QU6", "RU7"]
    check(entry + ["RE8", "GF9"],
          ["GF1", "GF2", "GF3", "IN4", "GF5", "QU6", "RU7", "RE8", "GF9"])
    check(entry[:3], ["GF1", "GF2", "GF3"])  # IN rejected, no rollback
    check(entry[:6], ["GF1", "GF2", "GF3", "IN4", "GF5", "QU6"])
    # Lexical sign must survive numerical conversion (including signed zero).
    check([form for form in ["35", "+5", "-5", "+0", "-0"]
           if form.startswith(("+", "-"))], ["+5", "-5", "+0", "-0"])
    check(operations(b"GF3 1, GF4 0; IN -801; QU77 2; RU1007"),
          [("GF", "two-decimal"), ("GF", "two-decimal"),
           ("IN", "negative-decimal"), ("QU", "two-decimal"),
           ("RU", "unsigned-decimal")])
    check(operations(b"RE35"), [("RE", "unsigned-decimal")])
    check(operations(b"RE+0"), [("RE", "positive-signed-decimal")])
    check(operations(b""), [])
    check(fields(b"// synthetic\n{100}{invented}{}{}{}{900}{}\n")[0][0], b"100")
    for invalid in [b"GF3 2", b"GF3", b"IN-801 2", b"ZZ1", b"RE>35", b"RE\xff"]:
        try:
            operations(invalid)
        except ValueError:
            checks += 1
        else:
            raise WorksheetFailure("SyntheticInvalidOperandAccepted")
    for invalid in [b"{100}", b"{100}{" + b"a" * 4097 + b"}{}{}{}{}{}",
                    b"{100}{nested{value}{}{}{}{}{}"]:
        try:
            fields(invalid)
        except ValueError:
            checks += 1
        else:
            raise WorksheetFailure("SyntheticInvalidFieldAccepted")
    return {"category": "SyntheticAlgebraAndTraceWorksheet", "checks": checks,
            "result": "PASS", "productionRuntimeTest": False}


def unique_numeric(archive: dat_probe.Archive, script: int):
    rows = []
    for entry in archive.entries:
        match = re.fullmatch(rb"scr\\([0-9]+)[^\\/:]*\.scr\x00", entry.raw_name)
        if match and int(match[1]) == script:
            rows.append(entry)
    require(len(rows) == 1, "CatalogueNotUnique")
    return rows[0]


def fields(payload: bytes) -> list[list[bytes]]:
    # Independent bounded worksheet parser: seven brace fields per record.
    # Display fields are retained only transiently, never decoded or emitted.
    require(len(payload) <= 524288, "DialogBudget")
    rows = []
    cursor = 0
    while cursor < len(payload):
        if payload[cursor:cursor + 2] == b"//":
            end = payload.find(b"\n", cursor)
            cursor = len(payload) if end == -1 else end + 1
            continue
        if payload[cursor] in b" \r\n":
            cursor += 1
            continue
        row = []
        for _ in range(7):
            require(payload[cursor:cursor + 1] == b"{", "FieldOpening")
            end = payload.find(b"}", cursor + 1)
            require(end >= 0 and end - cursor <= 4097, "FieldBudget")
            value = payload[cursor + 1:end]
            require(b"{" not in value and b"\x00" not in value, "FieldShape")
            row.append(value)
            cursor = end + 1
        rows.append(row)
        require(len(rows) <= 10000, "RecordBudget")
    return rows


def operations(payload: bytes) -> list[tuple[str, str]]:
    text = payload.decode("ascii", errors="strict")
    # Selected observation only: strict forms, NOT universal source grammar.
    pieces = re.split(r"(?=[A-Za-z]{2})", text)
    result = []
    for piece in pieces:
        if not piece.strip(" \t,;:()"):
            continue
        match = re.fullmatch(r"([A-Za-z]{2})\s*([+-]?[0-9]+)(?:\s+([0-9]+))?[ \t,;:()]*", piece)
        if match is None:
            raise WorksheetFailure("OperandShapeUnknown")
        opcode = match[1].upper()
        first = match[2]
        second = match[3]
        require(opcode in {"GF", "IN", "RE", "QU", "RU"}, "OperationUnknown")
        require((second is not None) == (opcode in {"GF", "QU"}), "ArityUnknown")
        shape = "two-decimal" if second is not None else (
            "negative-decimal" if first.startswith("-") else
            "positive-signed-decimal" if first.startswith("+") else "unsigned-decimal")
        if opcode == "GF":
            require(second in {"0", "1"}, "NonBooleanGlobalFlag")
        result.append((opcode, shape))
    return result


def original(scripts: Path, dialogs: Path) -> dict:
    before = [digest(scripts), digest(dialogs)]
    require(before == ["4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d",
                       "cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499"],
            "WrongSelectedArchives")
    scr_archive = dat_probe.parse_archive(scripts)
    selected = unique_numeric(scr_archive, 1324)
    scr = dat_probe.read_bounded_entry(scr_archive, selected)
    require(hashlib.sha256(scr).hexdigest() ==
            "f726b47ca8a960b9d876583320fb9592bd79009ff1880c71650bceecfa43e4fe", "WrongScript")
    require(len(scr) == 6268, "ScriptLength")
    # Independently inspect active first-five conditions and DIALOG target.
    kinds = [struct.unpack_from("<i", scr, 64 + 132 * i)[0] for i in range(5)]
    require(kinds == [18, 3, 39, 3, 0], "WrongEntryConditions")
    terminal = 64 + 132 * 4 + 44
    require(struct.unpack_from("<i", scr, terminal)[0] == 4 and scr[terminal + 4] == 3
            and struct.unpack_from("<i", scr, terminal + 12)[0] == 1, "WrongDialogTerminal")
    dlg_archive = dat_probe.parse_archive(dialogs)
    raw = b"dlg" + selected.raw_name[3:-4] + b"dlg\x00"
    matches = [entry for entry in dlg_archive.entries if entry.raw_name == raw]
    require(len(matches) == 1, "DialogNotUnique")
    payload = dat_probe.read_bounded_entry(dlg_archive, matches[0])
    require(hashlib.sha256(payload).hexdigest() ==
            "34a49a2c160cf8a97856cc0a9e48baa04fe0611c5d6bf05b5f52a7310f45e53a", "WrongDialog")
    rows = fields(payload)
    ids = [int(row[0]) for row in rows]
    require(len(rows) == 409 and len(set(ids)) == 409, "WrongRecords")
    start = ids.index(1)
    group = []
    for row in rows[start + 1:]:
        if not row[3].strip():
            break
        group.append(row)
    require([int(row[0]) for row in group] == [2, 3, 4, 5, 6, 7], "WrongReplies")
    ordered = sorted(rows, key=lambda row: int(row[0]))
    sorted_start = next(i for i, row in enumerate(ordered) if int(row[0]) == 1)
    sorted_group = []
    for row in ordered[sorted_start + 1:]:
        if not row[3].strip():
            break
        sorted_group.append(row)
    require([int(row[0]) for row in sorted_group] == [2, 3, 4, 5, 6, 7],
            "SelectedOrderMismatch")
    require(int(rows[start + 1 + len(group)][0]) ==
            int(ordered[sorted_start + 1 + len(sorted_group)][0]), "NextNpcMismatch")
    entry_ops = operations(rows[start][6])
    require([op for op, _ in entry_ops] == ["GF", "GF", "GF", "IN", "GF", "QU", "RU"],
            "WrongEntryEffects")
    require(entry_ops[3] == ("IN", "negative-decimal"), "WrongTransferDirection")
    require(all(int(row[5]) == 9 for row in group), "WrongDirectTargets")
    reply_ops = [operations(row[6]) for row in group]
    require(reply_ops == [[], [], [("RE", "unsigned-decimal")],
                          [("RE", "unsigned-decimal")], [], [("RE", "unsigned-decimal")]],
            "WrongReplyActionShapes")
    require(all(operations(row[4]) and operations(row[4])[0][0] == "RE" for row in group),
            "WrongReplyConditions")
    require(rows[ids.index(9)][6] == b"", "TargetNotEmpty")
    # Enumerate integer interval representatives from selected IQ/RE predicates.
    # No original thresholds, IQ values or gender operand values are emitted.
    predicates = []
    iq_points, reaction_points = set(), set()
    for row in group:
        condition = re.fullmatch(rb"\s*RE\s*([+-]?[0-9]+)\s*", row[4], re.I)
        if condition is None:
            raise WorksheetFailure("ReactionConditionShape")
        selector = int(row[2]) if row[2].strip() else -1
        iq_gate, re_gate = int(row[3]), int(condition[1])
        predicates.append((int(row[0]), selector, iq_gate, re_gate))
        for dest, gate in [(iq_points, iq_gate), (reaction_points, re_gate)]:
            boundary = abs(gate)
            dest.update([boundary - 1, boundary, boundary + 1])
    subsets = sorted({tuple(eligible(predicates, gender, iq, reaction))
                      for gender in (0, 1) for iq in iq_points for reaction in reaction_points})
    after = [digest(scripts), digest(dialogs)]
    require(before == after, "InputChanged")
    return {"category": "ObservedFieldsAndSourcePredicateWorksheet", "cePin": PIN,
            "archiveHashes": before, "integrityUnchanged": True,
            "scriptId": 1324, "scriptLine": 4, "npcNode": 1, "records": len(rows),
            "replyIds": [2, 3, 4, 5, 6, 7], "directTarget": 9,
            "entryShapes": entry_ops, "replyActionShapes": reply_ops,
            "predicateSubsets": subsets, "maximumEligibleInWorksheet": max(map(len, subsets)),
            "originalExecution": "NOT_VERIFIED", "runtimeAdmission": "NOT_IMPLEMENTED"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--scripts", type=Path)
    parser.add_argument("--dialogs", type=Path)
    args = parser.parse_args()
    try:
        if args.self_test and args.scripts is None and args.dialogs is None:
            report = self_test()
        elif not args.self_test and args.scripts is not None and args.dialogs is not None:
            report = original(args.scripts, args.dialogs)
        else:
            raise WorksheetFailure("InvalidMode")
        print(json.dumps(report, indent=2))
        return 0
    except WorksheetFailure as error:
        print(json.dumps({"result": "FAIL", "category": str(error)}))
        return 1
    except (OSError, ValueError, IndexError, struct.error):
        # Do not emit raw paths, payload, original exceptions or expressions.
        print(json.dumps({"result": "FAIL", "category": "InputOrWorksheetFailure"}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
