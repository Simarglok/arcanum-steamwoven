#!/usr/bin/env python3
"""Extra exports of the retail 1.0.7.4 Arcanum.exe that the Ghidra text export lacks (ARC-DEV-385).

The Ghidra export (ARC-DEV-377/379, ``research-private/ghidra-export``) holds the decompiled C, the listing, Ghidra's tables and a
few byte scans. This tool reads the player's own executable, the Ghidra listing and the project's function registry and writes
what is still missing, as plain text under the same folder (all of it derived from the original: it stays in ``research-private``
and is never committed or quoted into git):

* ``asm/funcs/<addr>.asm``     the listing of each function in its own file (operand width and sign next to the C);
* ``source-map/``              the original's own ``assert`` calls: source file, line number, expression text, function
                               (``asserts.tsv``, ``files.tsv``) and the strings that name a source module (``module-strings.tsv``);
* ``tables/str-tables.tsv``, ``tables/str-table-rows.tsv``  tables of records that start with a text pointer (names, messages);
* ``tables/data-dwords.txt``   every initialised dword of ``.rdata``/``.data`` with its target (text, function, data, float);
* ``tables/imm-stores.tsv``    constants the code stores into globals (the start-up initialisation of the zero-filled ``.data``);
* ``tables/indirect-calls.tsv``  calls and jumps through a register or memory (callbacks, vtables, jump tables, imports);
* ``names/functions-named.tsv``  the project's own names (``docs/verification/functions.csv``) next to Ghidra's;
* ``other-binaries/``          PE summary (sections, imports, exports) of every DLL, ASI and EXE beside the executable.

Usage: ``exe_export_extras.py --exe Arcanum.exe --export research-private/ghidra-export [--install DIR] [--dry-run]``.
``--self-test`` runs synthetic checks (no game file).
"""

from __future__ import annotations

import argparse
import bisect
import csv
import hashlib
import os
import re
import shutil
import struct
import sys
import tempfile
from collections import Counter, defaultdict
from pathlib import Path

PINNED_SHA256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52"
OUT_DIRS = ("asm", "source-map", "tables", "names", "other-binaries")
ASM_RE = re.compile(r"^([0-9a-f]{8})  ((?:[0-9a-f]{2}(?: [0-9a-f]{2})*)(?:\.\.)?)\s+(\S.*)$")
FUNC_RE = re.compile(r"^; FUNCTION (\S+) ([0-9a-f]{8})")
SRC_RE = re.compile(r"\.(?:c|cpp|h|hpp)$", re.I)
MODULE_RE = re.compile(r"^[A-Za-z_][\w]*\.(?:c|cpp|h|hpp)\b|\\[\w .]+\.(?:c|cpp|h|hpp)$", re.I)
PRINT = set(range(0x20, 0x7F)) | {9, 10, 13}


# ---------------------------------------------------------------- PE image

class Image:
    """Sections of a PE file: virtual address -> file bytes (zero-filled past the raw size, as the loader does)."""

    def __init__(self, data: bytes):
        self.d = data
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        nsec, = struct.unpack_from("<H", data, pe + 6)
        opt, = struct.unpack_from("<H", data, pe + 20)
        self.base, = struct.unpack_from("<I", data, pe + 24 + 28)
        self.secs = []
        for i in range(nsec):
            nm, vs, va, rs, ro = struct.unpack_from("<8sIIII", data, pe + 24 + opt + 40 * i)
            self.secs.append((nm.rstrip(b"\0").decode("latin1"), self.base + va, vs, ro, rs))

    def sec(self, va):
        for nm, a, vs, ro, rs in self.secs:
            if a <= va < a + max(vs, rs):
                return nm
        return None

    def byte(self, va):
        for nm, a, vs, ro, rs in self.secs:
            if a <= va < a + rs:
                return self.d[ro + va - a]
            if a + rs <= va < a + vs:
                return 0
        return None

    def raw_range(self, name):
        for nm, a, vs, ro, rs in self.secs:
            if nm == name:
                return a, self.d[ro:ro + rs]
        return None

    def cstr(self, va, limit=512):
        out = bytearray()
        for k in range(limit):
            b = self.byte(va + k)
            if b is None:
                return None
            if b == 0:
                return bytes(out)
            if b not in PRINT:
                return None
            out.append(b)
        return None

    def dword(self, va):
        bs = [self.byte(va + k) for k in range(4)]
        return None if None in bs else struct.unpack("<I", bytes(bs))[0]


def esc(b: bytes, n=80) -> str:
    s = b.decode("latin1").replace("\\", "\\\\").replace("\n", "\\n").replace("\r", "\\r").replace("\t", "\\t")
    return s if len(s) <= n else s[:n] + "…"


# ---------------------------------------------------------------- listing

def parse_asm(lines):
    """Yield (function_address, address, mnemonic, operands) of each instruction line of the Ghidra ``text.asm``."""
    cur = None
    for l in lines:
        l = l.rstrip("\n")
        m = FUNC_RE.match(l)
        if m:
            cur = int(m.group(2), 16)
            continue
        m = ASM_RE.match(l)
        if m and cur is not None:
            t = m.group(3)
            if t[:1].isupper():
                p = t.split(None, 1)
                yield cur, int(m.group(1), 16), p[0], (p[1] if len(p) > 1 else "")


def split_asm_by_function(lines):
    """Yield (address, [lines]) for each ``; FUNCTION`` block of the listing."""
    cur, buf = None, []
    for l in lines:
        m = FUNC_RE.match(l)
        if m:
            if cur is not None:
                yield cur, buf
            cur, buf = int(m.group(2), 16), [l.rstrip("\n")]
        elif cur is not None and not l.startswith("; block"):
            buf.append(l.rstrip("\n"))
    if cur is not None:
        yield cur, buf


def hex_imm(s):
    s = s.strip()
    m = re.fullmatch(r"(-?)0x([0-9a-f]+)", s, re.I)
    if not m:
        return None
    v = int(m.group(2), 16)
    return -v if m.group(1) else v


# ---------------------------------------------------------------- source map

def find_asserts(insns, img):
    """Pushes of the text of a source file name (the original's assert macro passes file and line).

    Returns rows (function, site, callee, file, line, kind_text, expression). Found by content, not by position: the line is
    the nearest earlier ``PUSH imm`` below 100000 (within 4 instructions), the expression the nearest earlier text pushed, the
    kind the next text pushed, the callee the next direct call (within 12 instructions) of the same function.
    """
    rows = []
    n = len(insns)
    for i, (fn, addr, mn, ops) in enumerate(insns):
        v = hex_imm(ops) if mn == "PUSH" else None
        if v is None:
            continue
        s = img.cstr(v & 0xFFFFFFFF)
        if not (s and len(s) > 4 and SRC_RE.search(s.decode("latin1"))):
            continue
        line = None
        for k in range(i - 1, max(i - 5, -1), -1):
            if insns[k][0] != fn:
                break
            if insns[k][2] == "PUSH":
                x = hex_imm(insns[k][3])
                if x is not None and 0 <= x < 100000 and img.sec(x) is None:
                    line = x
                    break
        if line is None:
            continue
        expr = kind = ""
        for k in range(i - 1, max(i - 7, -1), -1):
            if insns[k][0] != fn:
                break
            x = hex_imm(insns[k][3]) if insns[k][2] == "PUSH" else None
            t = img.cstr(x & 0xFFFFFFFF) if x is not None else None
            if t:
                expr = esc(t, 200)
                break
        for k in range(i + 1, min(i + 4, n)):
            if insns[k][0] != fn:
                break
            x = hex_imm(insns[k][3]) if insns[k][2] == "PUSH" else None
            t = img.cstr(x & 0xFFFFFFFF) if x is not None else None
            if t:
                kind = esc(t, 200)
                break
        callee = None
        for k in range(i + 1, min(i + 13, n)):
            if insns[k][0] != fn:
                break
            if insns[k][2] == "CALL" and hex_imm(insns[k][3]) is not None:
                callee = hex_imm(insns[k][3])
                break
        rows.append((fn, addr, callee, s.decode("latin1"), line, kind, expr))
    return rows


def module_strings(strings_raw_rows):
    out = []
    for r in strings_raw_rows:
        text = r[3]
        if MODULE_RE.search(text):
            out.append((r[0], r[1], text, r[4] if len(r) > 4 else ""))
    return out


# ---------------------------------------------------------------- tables

def is_text_ptr(img, v, min_len=2):
    if v is None or v == 0:
        return False
    s = img.sec(v)
    if s not in (".rdata", ".data"):
        return False
    b = img.cstr(v)
    return b is not None and len(b) >= min_len and b[0] in PRINT and b[0] > 8


def find_str_tables(img, ranges, min_rows=3, max_stride=64):
    """Runs of records (stride 4..64 bytes) whose first dword points at a text. Returns [(addr, stride, [(addr, dwords)])]."""
    tables = []
    for lo, hi in ranges:
        a = lo
        while a + 4 <= hi:
            v = img.dword(a)
            if not is_text_ptr(img, v):
                a += 4
                continue
            best = None
            for stride in range(4, max_stride + 1, 4):
                n, nonnull, last = 0, 0, 0
                while a + (n + 1) * stride <= hi:
                    w = img.dword(a + n * stride)
                    if is_text_ptr(img, w):
                        nonnull += 1
                        last = n
                    elif not (stride >= 8 and w == 0 and n > 0):
                        break
                    n += 1
                n = last + 1
                if n >= min_rows and (best is None or n > best[1]):
                    best = (stride, n)
            if best:
                stride, n = best
                rows = [(a + i * stride, [img.dword(a + i * stride + k) for k in range(0, stride, 4)]) for i in range(n)]
                tables.append((a, stride, rows))
                a += n * stride
            else:
                a += 4
    return tables


def annotate_dword(img, v, fn_set, text_cover=None):
    if v is None:
        return ""
    if v == 0:
        return "0"
    if is_text_ptr(img, v, 1):
        return '->"%s"' % esc(img.cstr(v), 48)
    sec = img.sec(v)
    if sec == ".text":
        return "->fn %08x" % v if v in fn_set else "->code %08x" % v
    if sec in (".data", ".rdata", ".idata"):
        return "->%s %08x" % (sec, v)
    if v < 0x10000:
        return str(v)
    f = struct.unpack("<f", struct.pack("<I", v))[0]
    if f == f and 1e-6 < abs(f) < 1e7:
        return "float %g" % f
    if v >= 0xFFFF0000:
        return str(v - (1 << 32))
    return ""


def dword_dump(img, ranges, fn_set, covered):
    """Lines '<addr>  <hex>  <annotation>' of every non-zero aligned dword outside known text, zero runs collapsed."""
    out, zero_start = [], None
    for lo, hi in ranges:
        a = lo
        while a + 4 <= hi:
            if a in covered:
                if zero_start is not None:
                    out.append("%08x  ; %d zero bytes" % (zero_start, a - zero_start))
                    zero_start = None
                a += 4
                continue
            v = img.dword(a)
            if v == 0:
                if zero_start is None:
                    zero_start = a
                a += 4
                continue
            if zero_start is not None:
                out.append("%08x  ; %d zero bytes" % (zero_start, a - zero_start))
                zero_start = None
            out.append("%08x  %08x  %s" % (a, v, annotate_dword(img, v, fn_set)))
            a += 4
        if zero_start is not None:
            out.append("%08x  ; %d zero bytes" % (zero_start, hi - zero_start))
            zero_start = None
    return out


IMM_STORE_RE = re.compile(r"^(dword|word|byte) ptr \[(0x[0-9a-f]+)\],(-?0x[0-9a-f]+)$", re.I)


def imm_stores(insns, img):
    rows = []
    for fn, addr, mn, ops in insns:
        if mn == "MOV":
            m = IMM_STORE_RE.match(ops)
            if m and img.sec(int(m.group(2), 16)) in (".data", ".rdata"):
                rows.append((fn, addr, m.group(1), int(m.group(2), 16), hex_imm(m.group(3))))
    return rows


def indirect_calls(insns, img, import_by_iat):
    rows = []
    for fn, addr, mn, ops in insns:
        if mn in ("CALL", "JMP") and hex_imm(ops) is None and not ops.startswith("0x"):
            slot = ""
            m = re.search(r"\[(0x[0-9a-f]{6,8})\]$", ops, re.I)
            if m:
                slot = "%08x" % int(m.group(1), 16)
            else:
                m = re.search(r"\*[1248] \+ (0x[0-9a-f]{6,8})\]$", ops, re.I)
                if m:
                    slot = "%08x" % int(m.group(1), 16)
            kind = "import" if slot in import_by_iat else ("table" if "*" in ops else ("global" if slot else "register"))
            rows.append((fn, addr, mn, ops, slot, kind, import_by_iat.get(slot, "")))
    return rows


# ---------------------------------------------------------------- names

def join_registry(index_rows, registry_rows):
    """index_rows: dicts of c/index.tsv; registry_rows: dicts of functions.csv. Returns (joined rows, registry rows with no function)."""
    by_addr = {int(r["address"], 16): r for r in index_rows}
    out, missing = [], []
    for r in registry_rows:
        a = int(r["address"], 16)
        g = by_addr.get(a)
        if g is None:
            missing.append(r)
        out.append((a, g["name"] if g else "", g["size_bytes"] if g else "", r["name"], r["domain"], r["status"], r["level"], r["evidence"]))
    return sorted(out), missing


# ---------------------------------------------------------------- other PE files

def pe_summary(data: bytes):
    if data[:2] != b"MZ":
        return None
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        return None
    machine, nsec, ts = struct.unpack_from("<HHI", data, pe + 4)
    opt, = struct.unpack_from("<H", data, pe + 20)
    magic, = struct.unpack_from("<H", data, pe + 24)
    plus = magic == 0x20B
    dd = pe + 24 + (112 if plus else 96)
    sub, = struct.unpack_from("<H", data, pe + 24 + 68)
    secs = []
    for i in range(nsec):
        nm, vs, va, rs, ro = struct.unpack_from("<8sIIII", data, pe + 24 + opt + 40 * i)
        secs.append((nm.rstrip(b"\0").decode("latin1"), va, vs, ro, rs))

    def off(rva):
        for _, va, vs, ro, rs in secs:
            if va <= rva < va + max(vs, rs):
                return ro + rva - va
        return None

    def cs(o):
        e = data.index(b"\0", o)
        return data[o:e].decode("latin1")

    imports, exports = [], []
    irva, isz = struct.unpack_from("<II", data, dd + 8)
    o = off(irva) if irva else None
    while o is not None:
        ilt, _, _, nrva, iat = struct.unpack_from("<IIIII", data, o)
        if not nrva:
            break
        dll = cs(off(nrva))
        t = off(ilt or iat)
        while t is not None:
            v, = struct.unpack_from("<Q" if plus else "<I", data, t)
            if v == 0:
                break
            if v >> (63 if plus else 31):
                imports.append((dll, "#%d" % (v & 0xFFFF)))
            else:
                imports.append((dll, cs(off(v) + 2)))
            t += 8 if plus else 4
        o += 20
    erva, esz = struct.unpack_from("<II", data, dd)
    o = off(erva) if erva else None
    if o is not None:
        base, nfn, nnm, afn, anm, aord = struct.unpack_from("<IIIIII", data, o + 16)
        names = {}
        for i in range(nnm):
            idx, = struct.unpack_from("<H", data, off(aord) + 2 * i)
            nmr, = struct.unpack_from("<I", data, off(anm) + 4 * i)
            names[idx] = cs(off(nmr))
        for i in range(nfn):
            rva, = struct.unpack_from("<I", data, off(afn) + 4 * i)
            if rva:
                exports.append((base + i, names.get(i, ""), rva))
    return {"machine": machine, "timestamp": ts, "subsystem": sub, "sections": secs, "imports": imports, "exports": exports}


# ---------------------------------------------------------------- driver

def read_tsv(path, delimiter="\t"):
    with open(path, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f, delimiter=delimiter))


def write_tsv(path, header, rows):
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter="\t", lineterminator="\n")
        w.writerow(header)
        w.writerows(rows)


def run(exe, export, install, registry, dry, replace=False):
    data = Path(exe).read_bytes()
    sha = hashlib.sha256(data).hexdigest()
    if sha != PINNED_SHA256:
        sys.exit("FAIL: %s is not the pinned 1.0.7.4 Arcanum.exe (sha256 %s)" % (exe, sha))
    export = Path(export)
    for need in ("listing/text.asm", "c/index.tsv", "data/functions-all.tsv", "data/imports.tsv", "raw/strings-raw.tsv"):
        if not (export / need).is_file():
            sys.exit("FAIL: missing %s (run the Ghidra export first)" % (export / need))
    if not Path(registry).is_file():
        sys.exit("FAIL: registry %s not found" % registry)
    if not Path(install).is_dir():
        sys.exit("FAIL: install folder %s not found" % install)
    existing = [d for d in OUT_DIRS if (export / d).exists()]
    if dry:
        print("dry run: would write", *["%s/%s%s" % (export, d, " (exists)" if d in existing else "") for d in OUT_DIRS], sep="\n  ")
        if existing and not replace:
            print("a real run would stop: these folders exist (use --replace)")
        return
    if existing and not replace:
        sys.exit("FAIL: %s exist (remove them or run with --replace)" % ", ".join(str(export / d) for d in existing))
    for d in existing:
        shutil.rmtree(export / d)
    img = Image(data)
    fn_rows = read_tsv(export / "data/functions-all.tsv")
    fn_set = {int(r["address"], 16) for r in fn_rows if r["kind"] != "external"}
    insns = list(parse_asm(open(export / "listing/text.asm", encoding="utf-8", errors="replace")))
    print("instructions", len(insns))

    (export / "asm/funcs").mkdir(parents=True)
    n = 0
    for a, buf in split_asm_by_function(open(export / "listing/text.asm", encoding="utf-8", errors="replace")):
        (export / "asm/funcs" / ("%08x.asm" % a)).write_text("\n".join(buf) + "\n", encoding="utf-8")
        n += 1
    print("asm files", n)

    (export / "source-map").mkdir()
    arows = find_asserts(insns, img)
    write_tsv(export / "source-map/asserts.tsv", ("function", "site", "callee", "file", "line", "kind_text", "expression"),
              [("%08x" % f, "%08x" % s, "%08x" % c if c else "", fl, ln, k, e) for f, s, c, fl, ln, k, e in arows])
    byfile = defaultdict(list)
    for f, s, c, fl, ln, k, e in arows:
        byfile[fl].append((f, ln, c))
    write_tsv(export / "source-map/files.tsv", ("file", "sites", "functions", "min_line", "max_line", "handlers", "function_list"),
              [(fl, len(v), len({x[0] for x in v}), min(x[1] for x in v), max(x[1] for x in v), " ".join("%08x" % h for h in sorted({x[2] for x in v if x[2]})),
                " ".join("%08x" % f for f in sorted({x[0] for x in v}))) for fl, v in sorted(byfile.items())])
    raw = [l.split("\t") for l in (export / "raw/strings-raw.tsv").read_text(encoding="utf-8", errors="replace").splitlines()[1:]]
    write_tsv(export / "source-map/module-strings.tsv", ("address", "section", "text", "refs"), module_strings(raw))
    print("asserts", len(arows), "files", len(byfile))

    (export / "tables").mkdir()
    ranges = []
    for nm in (".rdata", ".data"):
        rr = img.raw_range(nm)
        ranges.append((rr[0], rr[0] + len(rr[1])))
    tabs = find_str_tables(img, ranges)
    write_tsv(export / "tables/str-tables.tsv", ("address", "stride", "rows", "first_text"),
              [("%08x" % a, st, len(rows), esc(img.cstr(rows[0][1][0]), 40)) for a, st, rows in tabs])
    with open(export / "tables/str-table-rows.tsv", "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter="\t", lineterminator="\n")
        w.writerow(("table", "index", "address", "text", "other_dwords"))
        for a, st, rows in tabs:
            for i, (ra, dws) in enumerate(rows):
                t = esc(img.cstr(dws[0]), 120) if is_text_ptr(img, dws[0], 1) else ""
                w.writerow(("%08x" % a, i, "%08x" % ra, t, " ".join(annotate_dword(img, v, fn_set) or "%08x" % v for v in dws[1:])))
    print("string tables", len(tabs))
    covered = set()
    for r in raw:
        try:
            a, ln = int(r[0], 16), int(r[2])
        except ValueError:
            continue
        covered.update(range(a - a % 4, a + ln + 1, 4))
    (export / "tables/data-dwords.txt").write_text("\n".join(dword_dump(img, ranges, fn_set, covered)) + "\n", encoding="utf-8")
    ims = imm_stores(insns, img)
    write_tsv(export / "tables/imm-stores.tsv", ("function", "site", "width", "global", "value", "target"),
              [("%08x" % f, "%08x" % s, w, "%08x" % g, "0x%x" % (v & {"dword": 0xFFFFFFFF, "word": 0xFFFF, "byte": 0xFF}[w]), annotate_dword(img, v & 0xFFFFFFFF, fn_set) if w == "dword" else "") for f, s, w, g, v in ims])
    imp = {r["iat_address"]: "%s!%s" % (r["library"], r["name"]) for r in read_tsv(export / "data/imports.tsv")}
    ics = indirect_calls(insns, img, imp)
    write_tsv(export / "tables/indirect-calls.tsv", ("function", "site", "op", "operand", "slot", "kind", "import"),
              [("%08x" % f, "%08x" % s, m, o, sl, k, i) for f, s, m, o, sl, k, i in ics])
    print("imm stores", len(ims), "indirect", len(ics))

    (export / "names").mkdir()
    reg = read_tsv(Path(registry), ",")
    joined, missing = join_registry(read_tsv(export / "c/index.tsv"), reg)
    write_tsv(export / "names/functions-named.tsv", ("address", "ghidra_name", "size", "project_name", "domain", "status", "level", "evidence"),
              [("%08x" % a, g, s, n_, d, st, lv, ev) for a, g, s, n_, d, st, lv, ev in joined])
    write_tsv(export / "names/registry-not-function.tsv", ("address", "name", "domain", "status"),
              [(r["address"], r["name"], r["domain"], r["status"]) for r in missing])
    print("named", len(joined), "registry rows without a function", len(missing))

    (export / "other-binaries").mkdir()
    idx = []
    for p in sorted(Path(install).iterdir()):
        if p.suffix.lower() in (".exe", ".dll", ".asi") and p.name.lower() != "arcanum.exe":
            b = p.read_bytes()
            try:
                s = pe_summary(b)
            except (ValueError, struct.error, TypeError) as e:
                idx.append((p.name, len(b), hashlib.sha256(b).hexdigest(), "error: %s" % e, "", ""))
                continue
            if not s:
                continue
            lines = ["%s  sha256 %s  size %d" % (p.name, hashlib.sha256(b).hexdigest(), len(b)),
                     "machine 0x%x  timestamp %d  subsystem %d" % (s["machine"], s["timestamp"], s["subsystem"]), "sections"]
            lines += ["  %-8s va 0x%x vsize 0x%x raw 0x%x" % (nm, va, vs, rs) for nm, va, vs, ro, rs in s["sections"]]
            lines.append("imports (%d)" % len(s["imports"]))
            lines += ["  %s!%s" % i for i in s["imports"]]
            lines.append("exports (%d)" % len(s["exports"]))
            lines += ["  %d %s 0x%x" % e for e in s["exports"]]
            (export / "other-binaries" / (p.name + ".txt")).write_text("\n".join(lines) + "\n", encoding="utf-8")
            idx.append((p.name, len(b), hashlib.sha256(b).hexdigest(), len(s["sections"]), len(s["imports"]), len(s["exports"])))
    write_tsv(export / "other-binaries/index.tsv", ("file", "size", "sha256", "sections", "imports", "exports"), idx)
    print("other binaries", len(idx))


def self_test():
    import io
    ok = True

    def check(name, cond):
        nonlocal ok
        print(("PASS " if cond else "FAIL ") + name)
        ok &= bool(cond)

    # synthetic one-section image at 0x400000: .text 0x1000 (virtual only used for addresses), .data 0x2000 raw 0x100
    hdr = bytearray(0x400)
    struct.pack_into("<I", hdr, 0x3C, 0x80)
    hdr[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<HH", hdr, 0x84, 0x14C, 2)
    struct.pack_into("<H", hdr, 0x80 + 20, 0xE0)
    struct.pack_into("<I", hdr, 0x80 + 24 + 28, 0x400000)
    secs = [(b".text", 0x100, 0x1000, 0x100, 0x400), (b".data", 0x1000, 0x2000, 0x300, 0x500)]
    for i, (nm, vs, va, rs, ro) in enumerate(secs):
        struct.pack_into("<8sIIII", hdr, 0x80 + 24 + 0xE0 + 40 * i, nm, vs, va, rs, ro)
    body = bytearray(0x400)
    def put(va, bts):
        o = va - 0x402000 + 0x100
        body[o:o + len(bts)] = bts
    put(0x402000, b"hello\0")
    put(0x402008, b"w:\\x\\Anim.c\0")
    put(0x402020, b"x != 0\0")
    for i, t in enumerate((b"alpha\0", b"beta\0", b"gamma\0")):
        put(0x402100 + 8 * i, struct.pack("<II", 0x402200 + 8 * i, 10 + i))
        put(0x402200 + 8 * i, t)
    img = Image(bytes(hdr) + bytes(body))
    check("cstr reads a string", img.cstr(0x402000) == b"hello")
    check("section lookup", img.sec(0x402000) == ".data" and img.sec(0x401000) == ".text")
    asm = ["; block .text 00401000-004010ff", "", "; FUNCTION FUN_00401000 00401000",
           "00401000  68 20 20 40 00                       PUSH 0x402020",
           "00401005  68 d0 01 00 00                       PUSH 0x1d0",
           "0040100a  68 08 20 40 00                       PUSH 0x402008",
           "0040100f  68 00 20 40 00                       PUSH 0x402000",
           "00401014  e8 00 00 00 00                       CALL 0x00401100",
           "00401019  c7 05 40 20 40 00 05 00 00 00        MOV dword ptr [0x00402040],0x5",
           "00401023  ff 15 50 20 40 00                    CALL dword ptr [0x00402050]",
           "00401029  ff 24 85 60 20 40 00                 JMP dword ptr [EAX*4 + 0x00402060]",
           "; FUNCTION FUN_00401100 00401100", "00401100  c3                                   RET"]
    ins = list(parse_asm(asm))
    check("asm parse functions", [i[0] for i in ins][:1] == [0x401000] and ins[-1][0] == 0x401100)
    check("asm split", [a for a, _ in split_asm_by_function(asm)] == [0x401000, 0x401100])
    rows = find_asserts(ins, img)
    check("assert read", rows == [(0x401000, 0x40100a, 0x401100, "w:\\x\\Anim.c", 0x1d0, "hello", "x != 0")])
    st = imm_stores(ins, img)
    check("imm store", st == [(0x401000, 0x401019, "dword", 0x402040, 5)])
    ic = indirect_calls(ins, img, {"00402050": "K!X"})
    check("indirect import", ic[0][5] == "import" and ic[0][6] == "K!X" and ic[1][5] == "table" and ic[1][4] == "00402060")
    tabs = find_str_tables(img, [(0x402100, 0x402240)])
    check("string table", len(tabs) == 1 and tabs[0][1] == 8 and len(tabs[0][2]) == 3)
    check("annotate", annotate_dword(img, 0x402000, set()).startswith('->"hello') and annotate_dword(img, 0x3F800000, set()) == "float 1")
    j, miss = join_registry([{"address": "00401000", "name": "FUN_00401000", "size_bytes": "9"}],
                            [{"address": "0x401000", "name": "n", "domain": "d", "status": "s", "level": "F1", "evidence": "e"},
                             {"address": "0x500000", "name": "m", "domain": "d", "status": "s", "level": "F1", "evidence": "e"}])
    check("registry join", j[0][1] == "FUN_00401000" and len(miss) == 1)
    # minimal PE with one import and one export
    d = bytearray(0x600)
    d[0:2] = b"MZ"
    struct.pack_into("<I", d, 0x3C, 0x40)
    d[0x40:0x44] = b"PE\0\0"
    struct.pack_into("<HHI", d, 0x44, 0x14C, 1, 123)
    struct.pack_into("<H", d, 0x40 + 20, 0xE0)
    struct.pack_into("<H", d, 0x40 + 24, 0x10B)
    struct.pack_into("<H", d, 0x40 + 24 + 68, 2)
    dd = 0x40 + 24 + 96
    struct.pack_into("<II", d, dd, 0x1100, 0x40)       # export dir
    struct.pack_into("<II", d, dd + 8, 0x1200, 0x28)   # import dir
    struct.pack_into("<8sIIII", d, 0x40 + 24 + 0xE0, b".t", 0x1000, 0x1000, 0x400, 0x200)
    def pw(rva, bts):
        d[0x200 + rva - 0x1000:0x200 + rva - 0x1000 + len(bts)] = bts
    pw(0x1110, struct.pack("<IIIIII", 1, 1, 1, 0x1140, 0x1150, 0x1160))
    pw(0x1140, struct.pack("<I", 0x1010))
    pw(0x1150, struct.pack("<I", 0x1180))
    pw(0x1160, struct.pack("<H", 0))
    pw(0x1180, b"Fn\0")
    pw(0x1200, struct.pack("<IIIII", 0x1300, 0, 0, 0x1280, 0x1300))
    pw(0x1280, b"K.DLL\0")
    pw(0x1300, struct.pack("<II", 0x1340, 0))
    pw(0x1340, b"\0\0Imp\0")
    s = pe_summary(bytes(d))
    check("pe imports/exports", s and s["imports"] == [("K.DLL", "Imp")] and s["exports"] == [(1, "Fn", 0x1010)])
    # run() on a missing export folder refuses
    try:
        with tempfile.TemporaryDirectory() as t:
            Path(t, "x.exe").write_bytes(b"MZ")
            run(str(Path(t, "x.exe")), t, t, t, False)
        check("refuses a wrong exe", False)
    except SystemExit as e:
        check("refuses a wrong exe", "pinned" in str(e))
    print("self-test " + ("OK" if ok else "FAILED"))
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--exe")
    ap.add_argument("--export", help="research-private/ghidra-export")
    ap.add_argument("--install", help="folder holding the other DLL/ASI/EXE files (default: the folder of --exe)")
    ap.add_argument("--registry", default=str(Path(__file__).resolve().parents[2] / "docs/verification/functions.csv"))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--replace", action="store_true", help="delete the five output folders first")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        sys.exit(self_test())
    if not a.exe or not a.export:
        ap.error("--exe and --export are required")
    run(a.exe, a.export, a.install or str(Path(a.exe).parent), a.registry, a.dry_run, a.replace)


if __name__ == "__main__":
    main()
