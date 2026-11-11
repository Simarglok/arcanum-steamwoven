#!/usr/bin/env python3
"""Function census of the retail 1.0.7.4 Arcanum.exe (ARC-RES-106, V0 of the function audit plan).

Reads the player's own executable with llvm-objdump (free tool, no Ghidra needed), finds the start of every function it
can reach without a reconstruction and writes ``docs/verification/census.csv``: addresses and tags only, no bytes, no
listings and no names. The disassembly is streamed from the tool and never stored.

Function starts come from five sources, joined with ``+`` in the ``source`` column:

* ``call``   a direct ``call rel32`` from the code section lands on it;
* ``ptr``    a dword in a data section, or an immediate operand of the code, holds its address and the byte before it ends
             a function (return, jump, padding);
* ``pro``    it begins with the frame prologue ``push ebp; mov ebp, esp`` right after a function end;
* ``reg``    the registry ``functions.csv`` names it;
* ``seed``   the entry point, WinMain and the game-loop roots below.

A function runs from its start to the next start. Jump tables in the code section are found from the indexed jumps that
use them and are cut out of the instruction list, so their dwords are not read as code.

Reach follows direct calls and unconditional jumps to another start (a tail call or thunk). It does NOT follow function
pointers, so the flag ``address_taken`` marks functions that may be reached through a table, a callback or an event.

Usage: ``exe_census.py --exe Arcanum.exe [--out docs/verification/census.csv] [--summary]``; ``--self-test`` runs the
synthetic checks (no game file).
"""

from __future__ import annotations

import argparse
import bisect
import csv
import hashlib
import os
import re
import struct
import subprocess
import sys
from collections import Counter
from pathlib import Path

PINNED_SHA256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52"

# Game-loop roots (docs/architecture/original-time-loop.md TL1, TL3, TL5, TL6): the in-game loop, the module ping
# that calls every module's per-frame slot, the six slots, the engine ping. WinMain is seeded as a function start only.
LOOP_ROOTS = (0x401560, 0x402580, 0x51F220, 0x4D3330, 0x40EE20, 0x41B2A0, 0x45B370, 0x4A5490, 0x437E10)
WINMAIN = 0x401000

CSV_COLUMNS = ("address", "size", "range", "source", "callers", "calls_out", "address_taken", "registered", "cited", "reach")

INSTR_RE = re.compile(r"^\s*([0-9a-f]+):\s+(\S+)\s*(.*)$")
HEX_RE = re.compile(r"0x([0-9a-f]+)")
ADDR_CITE_RE = re.compile(r"0x([0-9A-Fa-f]{6})\b")
TABLE_JMP_RE = re.compile(r"\[(?:\d\*\w+|\w+\*\d)\s*\+\s*0x([0-9a-f]+)\]")
BYTE_TABLE_RE = re.compile(r"byte ptr \[\w+ \+ 0x([0-9a-f]+)\]")
CMP_LIMIT_RE = re.compile(r"^\w+, 0x([0-9a-f]+)$")
PAD_MNEMONICS = ("int3", "nop")
# Documents that describe the census itself: their addresses are not citations (and would change the CSV on every edit).
CITED_EXCLUDED = ("verification/README.md", "research/function-audit-plan.md")


class Pe:
    """The parts of a 32-bit PE the census needs."""

    def __init__(self, data: bytes):
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        if data[pe:pe + 4] != b"PE\0\0":
            raise ValueError("not a PE file")
        n_sections = struct.unpack_from("<H", data, pe + 6)[0]
        opt_size = struct.unpack_from("<H", data, pe + 20)[0]
        opt = pe + 24
        self.entry = struct.unpack_from("<I", data, opt + 16)[0]
        self.image_base = struct.unpack_from("<I", data, opt + 28)[0]
        self._raw: dict[str, tuple[int, int, int, int]] = {}
        sec = opt + opt_size
        for i in range(n_sections):
            name = data[sec + i * 40:sec + i * 40 + 8].rstrip(b"\0").decode("ascii", "replace")
            vsize, va, rsize, roff = struct.unpack_from("<IIII", data, sec + i * 40 + 8)
            self._raw[name] = (self.image_base + va, vsize, roff, rsize)
        self.data = data

    def section(self, name: str) -> tuple[int, int]:
        va, vsize, _, _ = self._raw[name]
        return va, va + vsize

    def dwords(self, name: str):
        va, vsize, roff, rsize = self._raw[name]
        size = min(vsize, rsize) & ~3
        blob = self.data[roff:roff + size]
        for i in range(0, len(blob), 4):
            yield va + i, struct.unpack_from("<I", blob, i)[0]

    def byte_at(self, address: int) -> int | None:
        for va, vsize, roff, rsize in self._raw.values():
            if va <= address < va + min(vsize, rsize):
                return self.data[roff + address - va]
        return None

    def dword_at(self, address: int) -> int | None:
        for va, vsize, roff, rsize in self._raw.values():
            if va <= address and address + 4 <= va + min(vsize, rsize):
                return struct.unpack_from("<I", self.data, roff + address - va)[0]
        return None


def parse_objdump(lines):
    """Yield (address, mnemonic, operands) for the instruction lines of one section."""
    for line in lines:
        m = INSTR_RE.match(line)
        if m:
            yield int(m.group(1), 16), m.group(2), m.group(3).strip()


def direct_target(operands: str) -> int | None:
    """The absolute target of ``call/jmp 0xADDR <...>``, or None for an indirect operand."""
    if operands.startswith("0x"):
        m = HEX_RE.match(operands)
        return int(m.group(1), 16) if m else None
    return None


def merge_regions(regions):
    """Sort and merge overlapping or touching (lo, hi) ranges."""
    merged = []
    for lo, hi in sorted(regions):
        if merged and lo <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(merged[-1][1], hi))
        else:
            merged.append((lo, hi))
    return merged


def find_tables(instrs, text_lo, text_hi, read_dword, read_byte=None):
    """Address ranges of jump tables and of the byte index tables that follow them inside the code section.

    The compiler lays a switch out as [dword jump table][byte index table]: the jump table is found from the indexed
    jump, and the byte table starts exactly where the dword run ends and is read by a ``mov``/``movzx`` of
    ``byte ptr [reg + table]``. Its length is the limit of the ``cmp reg, N`` just before the read (N + 1 bytes); without
    such a compare the bytes are taken while they stay below the jump table's entry count.
    """
    jump_starts = set()
    byte_reads: dict[int, int] = {}  # byte table address -> index of the reading instruction
    for i, (_, mnem, ops) in enumerate(instrs):
        if mnem == "jmp":
            m = TABLE_JMP_RE.search(ops)
            if m:
                a = int(m.group(1), 16)
                if text_lo <= a < text_hi:
                    jump_starts.add(a)
        elif mnem in ("mov", "movzx"):
            m = BYTE_TABLE_RE.search(ops)
            if m:
                a = int(m.group(1), 16)
                if text_lo <= a < text_hi:
                    byte_reads.setdefault(a, i)
    regions = []
    for a in sorted(jump_starts):
        end = a
        while end - a < 4 * 4096:
            v = read_dword(end)
            if v is None or not (text_lo <= v < text_hi):
                break
            end += 4
        if end > a:
            regions.append((a, end))
            i = byte_reads.get(end)
            if i is not None:
                n = None
                for _, mnem, ops in instrs[max(0, i - 4):i]:
                    m = CMP_LIMIT_RE.match(ops) if mnem == "cmp" else None
                    if m:
                        n = int(m.group(1), 16) + 1
                if n is None and read_byte is not None:
                    limit = (end - a) // 4
                    n = 0
                    while n < 0x400:
                        b = read_byte(end + n)
                        if b is None or b >= limit:
                            break
                        n += 1
                if n:
                    regions.append((end, end + n))
    return merge_regions(regions)


def in_regions(regions, address):
    i = bisect.bisect_right(regions, (address, float("inf"))) - 1
    return i >= 0 and regions[i][0] <= address < regions[i][1]


def analyze(instrs, text_lo, text_hi, data_words, registry, seeds, loop_roots, cited, tables, report=None):
    """Return the census rows (a list of dicts) for a decoded instruction list.

    instrs      list of (address, mnemonic, operands), ascending, jump tables already removed
    data_words  iterable of dword values found in the data sections
    registry    set of registered addresses
    seeds       set of addresses that are function starts by definition (entry point, WinMain)
    loop_roots  game-loop root addresses
    cited       dict address -> number of documents citing it
    tables      sorted list of (lo, hi) table ranges, only used to refuse starts inside them
    report      optional dict; receives ``dropped_targets``, the direct call targets that are not on a decoded
                instruction boundary (so they cannot become starts)
    """
    addrs = [a for a, _, _ in instrs]
    index_of = {a: i for i, a in enumerate(addrs)}

    def is_code(a):
        return a in index_of and not in_regions(tables, a)

    # Direct calls and jumps, and pointer-looking immediates.
    call_targets: Counter[int] = Counter()
    jmp_edges = []  # (from address, target) of unconditional direct jumps
    call_edges = []
    imm_ptrs = Counter()
    for a, mnem, ops in instrs:
        if mnem == "call":
            t = direct_target(ops)
            if t is not None and text_lo <= t < text_hi:
                call_targets[t] += 1
                call_edges.append((a, t))
        elif mnem == "jmp":
            t = direct_target(ops)
            if t is not None and text_lo <= t < text_hi:
                jmp_edges.append((a, t))
        elif not mnem.startswith("j"):
            for m in HEX_RE.finditer(ops):
                v = int(m.group(1), 16)
                if text_lo <= v < text_hi and "ptr [" not in ops[:m.start()].rsplit(",", 1)[-1]:
                    imm_ptrs[v] += 1
    data_ptrs = Counter(v for v in data_words if text_lo <= v < text_hi)

    def after_end(a):
        """True if the instruction before ``a`` ends a function-like block (return, jump, padding)."""
        i = index_of.get(a)
        if i is None or i == 0:
            return i == 0
        pm = instrs[i - 1][1]
        return pm.startswith("ret") or pm == "jmp" or pm in PAD_MNEMONICS

    sources: dict[int, set[str]] = {}

    def add(a, why):
        if is_code(a):
            sources.setdefault(a, set()).add(why)

    for a in seeds:
        add(a, "seed")
    for a in loop_roots:
        add(a, "seed")
    for a in registry:
        add(a, "reg")
    for t in call_targets:
        add(t, "call")
    if report is not None:
        report["dropped_targets"] = sorted(t for t in call_targets if not is_code(t))
    for v in set(data_ptrs) | set(imm_ptrs):
        if is_code(v) and after_end(v):
            add(v, "ptr")
    for i, (a, mnem, ops) in enumerate(instrs):
        if mnem == "push" and ops == "ebp" and i + 1 < len(instrs) and instrs[i + 1][1:] == ("mov", "ebp, esp") and after_end(a):
            add(a, "pro")

    starts = sorted(sources)
    start_set = set(starts)

    def owner(a):
        i = bisect.bisect_right(starts, a) - 1
        return starts[i] if i >= 0 else None

    # Edges between functions: direct calls, and unconditional jumps that leave the function.
    edges: dict[int, set[int]] = {s: set() for s in starts}
    callers: Counter[int] = Counter()
    for a, t in call_edges:
        o = owner(a)
        if o is not None and t in start_set and t != o:
            edges[o].add(t)
            callers[t] += 1
    for a, t in jmp_edges:
        o = owner(a)
        if o is not None and t in start_set and t != o:
            edges[o].add(t)
            callers[t] += 1

    def reach(roots):
        seen = set()
        stack = [r for r in roots if r in start_set]
        while stack:
            f = stack.pop()
            if f in seen:
                continue
            seen.add(f)
            stack.extend(edges[f] - seen)
        return seen

    from_registered = reach(registry)
    from_loop = reach(loop_roots)

    rows = []
    for i, s in enumerate(starts):
        end = starts[i + 1] if i + 1 < len(starts) else text_hi
        reg = s in registry
        in_reg = s in from_registered
        in_loop = s in from_loop
        rows.append({
            "address": f"0x{s:06X}",
            "size": end - s,
            "range": f"0x{s >> 16:X}",
            "source": "+".join(sorted(sources[s])),
            "callers": callers[s],
            "calls_out": len(edges[s]),
            "address_taken": "y" if (s in data_ptrs or s in imm_ptrs) else "n",
            "registered": "y" if reg else "n",
            "cited": cited.get(s, 0),
            "reach": "both" if in_reg and in_loop else "registered" if in_reg else "loop" if in_loop else "none",
        })
    return rows


def registry_addresses(path: Path) -> set[int]:
    with path.open(newline="", encoding="utf-8") as f:
        return {int(row["address"], 16) for row in csv.DictReader(f)}


def cited_addresses(docs_root: Path) -> dict[int, int]:
    """Address -> number of Markdown documents under docs/ that cite it as 0xNNNNNN."""
    counts: Counter[int] = Counter()
    for md in sorted(docs_root.rglob("*.md")):
        if md.relative_to(docs_root).as_posix() in CITED_EXCLUDED:
            continue
        seen = {int(m.group(1), 16) for m in ADDR_CITE_RE.finditer(md.read_text(encoding="utf-8", errors="replace"))}
        counts.update(seen)
    return dict(counts)


def objdump_binary() -> str:
    explicit = os.environ.get("LLVM_OBJDUMP")
    if explicit:
        return explicit
    out = subprocess.run(["xcrun", "-f", "llvm-objdump"], capture_output=True, text=True, check=False)
    if out.returncode == 0 and out.stdout.strip():
        return out.stdout.strip()
    return "llvm-objdump"


def disassemble(exe: Path, lo: int | None = None, hi: int | None = None):
    cmd = [objdump_binary(), "-d", "--no-show-raw-insn", "--x86-asm-syntax=intel", "-j", ".text"]
    if lo is not None:
        cmd += [f"--start-address=0x{lo:X}", f"--stop-address=0x{hi:X}"]
    proc = subprocess.Popen(cmd + [str(exe)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    instrs = list(parse_objdump(proc.stdout))
    err = proc.stderr.read()
    if proc.wait() != 0 or (not instrs and lo is None):
        raise RuntimeError(f"objdump failed: {err.strip()[:200]}")
    return instrs


def segments_between(tables, text_lo, text_hi):
    """The code stretches between jump tables."""
    out, cur = [], text_lo
    for lo, hi in tables:
        if lo > cur:
            out.append((cur, lo))
        cur = max(cur, hi)
    if cur < text_hi:
        out.append((cur, text_hi))
    return out


def disassemble_code(exe: Path, text_lo: int, text_hi: int, read_dword, read_byte=None):
    """Disassemble the code section so that no table is decoded as code and every stretch after a table starts on
    its true boundary: a linear pass finds the tables, then each stretch between them is decoded on its own (the
    pass repeats until the tables stop changing, since a table hidden behind another one's mis-decoded tail is only
    seen once that tail is decoded right)."""
    from concurrent.futures import ThreadPoolExecutor
    instrs = disassemble(exe)
    tables = find_tables(instrs, text_lo, text_hi, read_dword, read_byte)
    for _ in range(4):
        segments = segments_between(tables, text_lo, text_hi)
        with ThreadPoolExecutor(max_workers=8) as pool:
            parts = list(pool.map(lambda seg: disassemble(exe, seg[0], seg[1]), segments))
        instrs = [t for part in parts for t in part]
        again = find_tables(instrs, text_lo, text_hi, read_dword, read_byte)
        if again == tables:
            break
        tables = merge_regions(tables + again)
    else:
        print("WARN: the table search did not settle in 4 passes", file=sys.stderr)
    return instrs, tables


def run(args) -> int:
    exe = Path(args.exe)
    data = exe.read_bytes()
    sha = hashlib.sha256(data).hexdigest()
    if sha != PINNED_SHA256 and not args.allow_other_exe:
        print(f"FAIL: {exe} is not the pinned 1.0.7.4 executable (SHA-256 {sha})", file=sys.stderr)
        return 2
    pe = Pe(data)
    text_lo, text_hi = pe.section(".text")
    instrs, tables = disassemble_code(exe, text_lo, text_hi, pe.dword_at, pe.byte_at)
    data_words = [v for name in (".data", ".rdata") for _, v in pe.dwords(name)]
    repo = Path(args.repo)
    registry = registry_addresses(repo / "docs/verification/functions.csv")
    cited = cited_addresses(repo / "docs")
    entry = pe.image_base + pe.entry
    report: dict = {}
    rows = analyze(instrs, text_lo, text_hi, data_words, registry, {entry, WINMAIN}, set(LOOP_ROOTS), cited, tables, report)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=CSV_COLUMNS, lineterminator="\n")
        w.writeheader()
        w.writerows(rows)
    print_summary(rows, registry, tables, cited, text_lo, text_hi, report)
    print(f"PASS census: {len(rows)} functions written to {out}")
    return 0


def print_summary(rows, registry, tables, cited=None, text_lo=0x401000, text_hi=0x596000, report=None):
    print(f"functions {len(rows)}, jump tables cut {len(tables)}, registry rows inside the code section "
          f"{sum(1 for r in rows if r['registered'] == 'y')} of {len(registry)}")
    only_reg = sum(1 for r in rows if r["source"] == "reg")
    print(f"registry rows found by the scan alone (not only seeded by the registry): "
          f"{sum(1 for r in rows if r['registered'] == 'y') - only_reg}")
    if cited is not None:
        starts = {int(r["address"], 16) for r in rows}
        stray = sorted(a for a in cited if text_lo <= a < text_hi and a not in starts)
        print(f"cited addresses inside the code section that are not a function start: {len(stray)}")
        print(f"cited function starts: {sum(1 for r in rows if r['cited'] > 0)}")
    if report is not None:
        print(f"direct call targets that are not on an instruction boundary (dropped): {len(report['dropped_targets'])}",
              [f"0x{a:06X}" for a in report["dropped_targets"]])
    print("reach:", dict(Counter(r["reach"] for r in rows)))
    print("source:", dict(sorted(Counter(r["source"] for r in rows).items())))
    print("address_taken:", dict(Counter(r["address_taken"] for r in rows)))
    unreg_cited = sum(1 for r in rows if r["registered"] == "n" and r["cited"] > 0)
    print("cited but not registered:", unreg_cited)
    orphans = sum(1 for r in rows if r["callers"] == 0 and r["address_taken"] == "n" and r["registered"] == "n"
                  and r["reach"] == "none" and r["source"] in ("pro", "ptr", "pro+ptr"))
    print("no caller, not address-taken, not registered, not reached (detected only by prologue/pointer):", orphans)
    per_range: dict[str, Counter] = {}
    for r in rows:
        per_range.setdefault(r["range"], Counter())[r["reach"]] += 1
    for rng in sorted(per_range, key=lambda x: int(x, 16)):
        c = per_range[rng]
        print(f"range {rng}: total {sum(c.values())}", dict(c))


def self_test() -> int:
    failures = []

    def check(name, cond):
        if not cond:
            failures.append(name)

    # Parser of the tool's line format.
    sample = ["  40102a:      \tcall\t0x589e08 <.text+0x188e08>", "00401000 <.text>:", "  401032:      \tmov\tbyte ptr [edi], al"]
    parsed = list(parse_objdump(sample))
    check("parse count", len(parsed) == 2)
    check("parse call", parsed[0] == (0x40102A, "call", "0x589e08 <.text+0x188e08>"))
    check("direct target", direct_target(parsed[0][2]) == 0x589E08)
    check("indirect target", direct_target("dword ptr [0x59a000]") is None)

    # Synthetic code: A (seed) calls B and jumps to the thunk T; B is registered and calls C; D is address-taken
    # (found only through a data dword); E is dead and has only a frame prologue; a jump table sits after A.
    lo, hi = 0x1000, 0x1100
    code = [
        (0x1000, "push", "esi"), (0x1001, "call", "0x1010 <x>"), (0x1006, "jmp", "dword ptr [4*eax + 0x1080]"),
        (0x100D, "ret", ""), (0x100E, "int3", ""), (0x100F, "int3", ""),
        (0x1010, "call", "0x1020 <x>"), (0x1015, "ret", ""), (0x1016, "int3", ""),
        (0x1020, "xor", "eax, eax"), (0x1022, "ret", ""), (0x1023, "int3", ""),
        (0x1030, "mov", "eax, 0x1"), (0x1035, "ret", ""), (0x1036, "int3", ""),
        (0x1040, "push", "ebp"), (0x1041, "mov", "ebp, esp"), (0x1043, "ret", ""), (0x1044, "int3", ""),
        (0x1050, "xor", "eax, eax"), (0x1052, "ret", ""), (0x1053, "int3", ""),
        (0x1060, "jmp", "0x1020 <x>"), (0x1065, "int3", ""),
        (0x1070, "call", "0x1060 <x>"), (0x1075, "call", "0x1030 <x>"), (0x107A, "push", "0x1090"), (0x107F, "ret", ""),
        (0x1090, "xor", "eax, eax"), (0x1092, "ret", ""),
    ]
    words = {0x1080: 0x1000, 0x1084: 0x100D}
    tbl = find_tables(code, lo, hi, lambda a: words.get(a))
    check("jump table found", tbl == [(0x1080, 0x1088)])
    rows = analyze(code, lo, hi, [0x1050], {0x1010}, {0x1000}, {0x1070}, {0x1040: 2}, tbl)
    by = {r["address"]: r for r in rows}
    check("starts", sorted(by) == ["0x001000", "0x001010", "0x001020", "0x001030", "0x001040", "0x001050", "0x001060", "0x001070", "0x001090"])
    check("call source", by["0x001020"]["source"] == "call")
    check("data pointer source", by["0x001050"]["source"] == "ptr" and by["0x001050"]["address_taken"] == "y")
    check("immediate pointer source", by["0x001090"]["source"] == "ptr" and by["0x001090"]["address_taken"] == "y")
    check("plain constant is no pointer", by["0x001030"]["address_taken"] == "n")
    check("prologue source", by["0x001040"]["source"] == "pro" and by["0x001040"]["cited"] == 2)
    check("registered reach", by["0x001010"]["reach"] == "registered" and by["0x001020"]["reach"] == "both")
    check("loop reach through tail jump", by["0x001060"]["reach"] == "loop" and by["0x001020"]["reach"] == "both")
    check("dead code not reached", by["0x001040"]["reach"] == "none" and by["0x001040"]["callers"] == 0)
    check("callers count", by["0x001020"]["callers"] == 2)
    check("size", by["0x001000"]["size"] == 0x10 and by["0x001070"]["size"] == 0x20 and by["0x001090"]["size"] == hi - 0x1090)
    check("table start refused", "0x001080" not in by)

    # Byte index table after a jump table: limit from the compare, or the run of small bytes without one.
    words2 = {0x2080: 0x2000, 0x2084: 0x2000}
    with_cmp = [(0x2000, "cmp", "ecx, 0x5"), (0x2003, "mov", "al, byte ptr [ecx + 0x2088]"), (0x2009, "jmp", "dword ptr [4*eax + 0x2080]")]
    check("byte table with compare", find_tables(with_cmp, 0x2000, 0x2100, words2.get, lambda a: 0) == [(0x2080, 0x208E)])
    no_cmp = [(0x2003, "movzx", "eax, byte ptr [ecx + 0x2088]"), (0x2009, "jmp", "dword ptr [4*eax + 0x2080]")]
    small = {0x2088: 0, 0x2089: 1, 0x208A: 0, 0x208B: 0xCC}
    check("byte table without compare", find_tables(no_cmp, 0x2000, 0x2100, words2.get, small.get) == [(0x2080, 0x208B)])
    stray_read = [(0x2003, "mov", "al, byte ptr [ecx + 0x2090]"), (0x2009, "jmp", "dword ptr [4*eax + 0x2080]")]
    check("byte read not at a table end is ignored", find_tables(stray_read, 0x2000, 0x2100, words2.get, small.get) == [(0x2080, 0x2088)])
    check("merge", merge_regions([(5, 9), (1, 3), (3, 4), (8, 12)]) == [(1, 4), (5, 12)])
    check("segments", segments_between([(0x1080, 0x108E)], 0x1000, 0x1100) == [(0x1000, 0x1080), (0x108E, 0x1100)])
    check("in_regions", in_regions([(0x10, 0x20)], 0x10) and not in_regions([(0x10, 0x20)], 0x20) and not in_regions([], 0x10))
    check("self recursion not a caller", analyze([(0x1000, "call", "0x1000 <x>"), (0x1005, "ret", "")], 0x1000, 0x1100, [], set(),
                                                 {0x1000}, set(), {}, [])[0]["callers"] == 0)
    rep = {}
    analyze([(0x1000, "call", "0x1003 <x>"), (0x1005, "ret", "")], 0x1000, 0x1100, [], set(), {0x1000}, set(), {}, [], rep)
    check("mid-instruction target reported", rep["dropped_targets"] == [0x1003])

    if failures:
        for f in failures:
            print(f"FAIL exe_census self-test: {f}", file=sys.stderr)
        return 1
    print("PASS exe_census self-test")
    return 0


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("--self-test", action="store_true", help="run the synthetic checks (no game file)")
    p.add_argument("--exe", help="path of the retail Arcanum.exe (SHA-256 is checked)")
    p.add_argument("--repo", default=str(Path(__file__).resolve().parents[2]), help="repository root")
    p.add_argument("--out", default=None, help="CSV to write (default docs/verification/census.csv)")
    p.add_argument("--allow-other-exe", action="store_true", help="skip the pinned-hash check")
    args = p.parse_args(argv)
    if args.self_test:
        return self_test()
    if not args.exe:
        p.error("--exe is required (or --self-test)")
    if args.out is None:
        args.out = str(Path(args.repo) / "docs/verification/census.csv")
    return run(args)


if __name__ == "__main__":
    sys.exit(main())
