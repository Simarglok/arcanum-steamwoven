"""Self-test of the function oracle on a synthetic PE32 image (ARC-DEV-007); needs no game file.

The x86-32 code below was written for this test (hand-assembled, commented per instruction); it holds no game bytes.
"""
from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path
import random
import struct
import tempfile
from contextlib import redirect_stderr, redirect_stdout
import unittest

import oracle
import oracle_functions
import oracle_pe
from oracle_emu import Emulator, Range, RunFailure, StubRejected

BASE = 0x00400000
TEXT = BASE + 0x1000
DATA = BASE + 0x2000
GLOBAL = DATA          # a 32-bit global the world model defines
OTHER = DATA + 4       # a global it does not define
STUB = TEXT + 0x100    # a service function replaced by a stub (never executed: int3)


def rel32(at: int, size: int, target: int) -> bytes:
    return struct.pack("<i", target - (at + size))


# Synthetic functions, each at its own address in .text.
SUM = TEXT + 0x00  # a + 2 * b
STAT = TEXT + 0x20  # stub(handle low, handle high, 0x11) + GLOBAL
CALLS_OUT = TEXT + 0x40  # the planted unexpected call: calls SUM
LOOP = TEXT + 0x50  # never returns
FAULT = TEXT + 0x60  # reads unmapped memory
READS_OTHER = TEXT + 0x70  # reads a global outside the model
WRITES = TEXT + 0x80  # writes the model's global
CLOBBERS = TEXT + 0x90  # does not restore ebx
CODE = {
    SUM: bytes.fromhex("8b442404" "8b4c2408" "8d0448" "c3"),  # mov eax,[esp+4]; mov ecx,[esp+8]; lea eax,[eax+ecx*2]; ret
    STAT: bytes.fromhex("6a11" "ff74240c" "ff74240c") + b"\xe8" + rel32(STAT + 10, 5, STUB)  # push 0x11; push hi; push lo; call STUB
    + bytes.fromhex("83c40c") + b"\x03\x05" + struct.pack("<I", GLOBAL) + b"\xc3",           # add esp,12; add eax,[GLOBAL]; ret
    CALLS_OUT: b"\xe8" + rel32(CALLS_OUT, 5, SUM) + b"\xc3",                                  # call SUM; ret
    LOOP: bytes.fromhex("ebfe"),                                                              # jmp $
    FAULT: b"\xa1" + struct.pack("<I", 0x10) + b"\xc3",                                       # mov eax,[0x10]; ret
    READS_OTHER: b"\xa1" + struct.pack("<I", OTHER) + b"\xc3",                                # mov eax,[OTHER]; ret
    WRITES: b"\xa3" + struct.pack("<I", GLOBAL) + b"\xc3",                                    # mov [GLOBAL],eax; ret
    CLOBBERS: bytes.fromhex("31db" "c3"),                                                     # xor ebx,ebx; ret
    STUB: b"\xcc",                                                                            # int3
}


def own(address: int) -> tuple[Range]:
    return (Range(address, address + len(CODE[address])),)


def synthetic_pe() -> bytes:
    """A minimal PE32 file: headers, .text (0x401000) and .data (0x402000)."""
    file = bytearray(0x600)
    file[0:2] = b"MZ"
    struct.pack_into("<I", file, 0x3C, 0x40)
    file[0x40:0x44] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", file, 0x44, 0x14C, 2, 0, 0, 0, 0xE0, 0x102)
    optional = 0x58
    struct.pack_into("<H", file, optional, 0x10B)
    struct.pack_into("<I", file, optional + 28, BASE)
    struct.pack_into("<II", file, optional + 32, 0x1000, 0x200)
    struct.pack_into("<II", file, optional + 56, 0x3000, 0x200)
    table = optional + 0xE0
    struct.pack_into("<8sIIII", file, table, b".text", 0x200, 0x1000, 0x200, 0x200)
    struct.pack_into("<8sIIII", file, table + 40, b".data", 0x100, 0x2000, 0x200, 0x400)
    for address, code in CODE.items():
        offset = 0x200 + address - TEXT
        file[offset:offset + len(code)] = code
    struct.pack_into("<II", file, 0x400, 37, 99)  # GLOBAL, OTHER
    return bytes(file)


class OracleSelfTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        cls.exe = Path(cls.temp.name) / "synthetic.exe"
        cls.exe.write_bytes(synthetic_pe())
        cls.sha = hashlib.sha256(cls.exe.read_bytes()).hexdigest()
        cls.image = oracle_pe.load(cls.exe, cls.sha)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def emulator(self) -> Emulator:
        return Emulator(self.image, instruction_limit=10_000)

    def stat_stub(self, expected: int, value: int):
        seen = []

        def stub(argument):
            seen.append((argument(0), argument(1), argument(2)))
            if argument(2) != expected:
                raise StubRejected(f"stat {argument(2)}")
            return value
        return stub, seen

    def failure(self, function, arguments=(), **kwargs) -> RunFailure:
        with self.assertRaises(RunFailure) as caught:
            self.emulator().call(function, arguments, **kwargs)
        return caught.exception

    def test_the_pinned_hash_refuses_any_other_file(self):
        with self.assertRaises(oracle_pe.RefusedExecutable):
            oracle_pe.load(self.exe)
        with self.assertRaises(oracle_pe.RefusedExecutable):
            oracle_pe.load(Path(self.temp.name) / "missing.exe", self.sha)
        self.assertEqual(oracle_pe.PINNED_EXE_SHA256, "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52")
        with self.assertRaises(oracle_pe.RefusedExecutable):
            oracle_pe.parse(b"MZ" + b"\0" * 62, "x")

    def test_the_image_is_mapped_at_its_virtual_addresses(self):
        self.assertEqual(self.image.base, BASE)
        self.assertEqual([(s.name, s.start, s.end) for s in self.image.sections],
                         [(".text", TEXT, TEXT + 0x200), (".data", DATA, DATA + 0x100)])
        self.assertEqual(self.image.image[SUM - BASE:SUM - BASE + len(CODE[SUM])], CODE[SUM])
        self.assertEqual(self.image.section_of(STUB).name, ".text")

    def test_a_function_returns_its_signed_result(self):
        emulator = self.emulator()
        self.assertEqual(emulator.call(SUM, (3, 4), code=own(SUM)), 11)
        self.assertEqual(emulator.call(SUM, (-10, 2), code=own(SUM)), -6)
        self.assertEqual(emulator.call(SUM, (0x7FFFFFFF, 1), code=own(SUM)), -0x7FFFFFFF)  # 32-bit wrap

    def test_a_stub_answers_from_the_model_and_a_global_is_read(self):
        stub, seen = self.stat_stub(0x11, 5)
        result = self.emulator().call(STAT, (0x1234, 0x42), code=own(STAT), stubs={STUB: stub},
                                      readable=(Range(GLOBAL, GLOBAL + 4),))
        self.assertEqual(result, 42)
        self.assertEqual(seen, [(0x1234, 0x42, 0x11)])

    def test_a_stub_rejecting_its_arguments_fails_the_run(self):
        stub, _ = self.stat_stub(0x12, 5)
        self.assertEqual(self.failure(STAT, (1, 2), code=own(STAT), stubs={STUB: stub}, readable=(Range(GLOBAL, GLOBAL + 4),)).kind, "stub")

    def test_a_missing_stub_is_an_unexpected_call(self):
        self.assertEqual(self.failure(STAT, (1, 2), code=own(STAT), readable=(Range(GLOBAL, GLOBAL + 4),)).kind, "call")

    def test_the_planted_unexpected_call_fails_the_run(self):
        failure = self.failure(CALLS_OUT, code=own(CALLS_OUT))
        self.assertEqual(failure.kind, "call")
        self.assertIn(f"{SUM:#x}", failure.detail)

    def test_reads_and_writes_outside_the_model_fail(self):
        stub, _ = self.stat_stub(0x11, 5)
        self.assertEqual(self.failure(STAT, (1, 2), code=own(STAT), stubs={STUB: stub}).kind, "read")
        self.assertEqual(self.failure(READS_OTHER, code=own(READS_OTHER), readable=(Range(GLOBAL, GLOBAL + 4),)).kind, "read")
        self.assertEqual(self.failure(WRITES, code=own(WRITES), readable=(Range(GLOBAL, GLOBAL + 4),)).kind, "write")

    def test_a_fault_a_missing_return_and_a_clobbered_register_fail(self):
        self.assertEqual(self.failure(FAULT, code=own(FAULT)).kind, "fault")
        self.assertEqual(self.failure(LOOP, code=own(LOOP)).kind, "no-return")
        self.assertEqual(self.failure(CLOBBERS, code=own(CLOBBERS)).kind, "abi")

    def test_a_failed_run_leaves_the_emulator_usable(self):
        emulator = self.emulator()
        with self.assertRaises(RunFailure):
            emulator.call(CALLS_OUT, (), code=own(CALLS_OUT))
        self.assertEqual(emulator.call(SUM, (1, 1), code=own(SUM)), 3)

    def test_seeded_runs_are_deterministic_and_record_every_input(self):
        spec = oracle_functions.FunctionSpec(SUM, "synthetic-sum", ((SUM, SUM + 0x10),), "bonus")
        first = oracle_functions.run(spec, self.emulator(), 7, 300, exe_sha256=self.sha, unicorn_version="test")
        second = oracle_functions.run(spec, self.emulator(), 7, 300, exe_sha256=self.sha, unicorn_version="test")
        self.assertEqual(json.dumps(first), json.dumps(second))
        self.assertEqual(len(first["cases"]), 300)
        self.assertTrue(all(result == a + 2 * b for a, b, result in first["cases"]))
        self.assertEqual((first["function"], first["seed"], first["exeSha256"]), (f"0x{SUM:X}", 7, self.sha))
        third = oracle_functions.run(spec, self.emulator(), 8, 300, exe_sha256=self.sha, unicorn_version="test")
        self.assertNotEqual(first["cases"], third["cases"])

    def test_the_experience_model_stays_in_the_csharp_domain_and_reaches_the_edges(self):
        rng = random.Random(1)
        tables = [oracle_functions.draw_table(rng) for _ in range(1000)]
        self.assertTrue(all(oracle_functions.admissible(table) for table in tables))
        self.assertTrue(any(table[-1] == oracle_functions.MAX_EXPERIENCE for table in tables))
        cases = [oracle_functions.draw_experience_case(rng, tables) for _ in range(10_000)]
        self.assertTrue(all(0 <= level <= 51 and 0 <= xp <= oracle_functions.MAX_EXPERIENCE for _, level, xp in cases))
        self.assertTrue({0, 1, 50, 51} <= {level for _, level, _ in cases})

        def remaining(index, level, xp):
            return tables[index][level] - xp
        inner = [(i, l, x) for i, l, x in cases if 1 <= l <= 50]
        self.assertTrue(any(remaining(*case) > oracle_functions.XP3_BOUND for case in inner))
        self.assertTrue(any(remaining(*case) == oracle_functions.XP3_BOUND for case in inner))
        self.assertTrue(any(remaining(*case) == 0 for case in inner))
        self.assertTrue(any(remaining(*case) < 0 for case in inner))

    def test_the_registry_names_three_functions_with_their_own_code_first(self):
        self.assertEqual(sorted(oracle_functions.FUNCTIONS), [0x4A68B0, 0x4A6900, 0x4A6980])
        for address, spec in oracle_functions.FUNCTIONS.items():
            self.assertEqual(spec.code[0][0], address)
            self.assertTrue(all(start < end for start, end in spec.code))
            self.assertNotIn("_", spec.name)  # Steamwoven's own kebab-case names
        self.assertEqual(oracle_functions.parse_address("0x4a6900"), 0x4A6900)
        with self.assertRaises(ValueError):
            oracle_functions.parse_address("0x401000")

    def test_generated_inputs_stay_out_of_the_work_tree(self):
        with self.assertRaises(ValueError):
            oracle.checked_out_dir(oracle.REPO / "docs" / "oracle")
        with self.assertRaises(ValueError):
            oracle.checked_out_dir(oracle.REPO)
        oracle.checked_out_dir(oracle.REPO / "research-private" / "oracle")
        oracle.checked_out_dir(oracle.REPO / "local" / "oracle")
        oracle.checked_out_dir(Path(self.temp.name) / "out")

    def test_generate_refuses_small_runs_and_other_executables(self):
        out = Path(self.temp.name) / "out"
        for argv in (["generate", "--count", "100", "--exe", str(self.exe), "--out", str(out)],
                     ["generate", "--exe", str(self.exe), "--out", str(out)],
                     ["generate", "--function", "0x401000", "--exe", str(self.exe), "--out", str(out)]):
            with redirect_stderr(io.StringIO()), redirect_stdout(io.StringIO()):
                self.assertEqual(oracle.main(argv), 2, argv)
        self.assertFalse(any(out.glob("*.json")))


def main() -> int:
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(OracleSelfTest)
    result = unittest.TextTestRunner(verbosity=1).run(suite)
    print("oracle self-test:", "PASS" if result.wasSuccessful() else "FAIL")
    return 0 if result.wasSuccessful() else 1
