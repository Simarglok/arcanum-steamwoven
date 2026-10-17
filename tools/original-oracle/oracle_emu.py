"""Strict single-function x86-32 emulator for the function oracle (ARC-DEV-007).

One cdecl call runs in Unicorn over the mapped image. The run fails (RunFailure) unless the function returns to the
caller with the stack balanced and the callee-saved registers kept, and it fails at once on:

- "call": an instruction outside the allowed code ranges that is not a stub (any callee nobody allowed);
- "stub": a stub that rejects its arguments;
- "read": a data read inside the image outside the allowed globals (state the world model does not define);
- "write": a write anywhere but the stack;
- "fault": an unmapped or invalid access, or an invalid instruction;
- "no-return": the instruction limit is reached first;
- "abi": the stack or a callee-saved register is not restored.
"""
from __future__ import annotations

from dataclasses import dataclass
import struct
from typing import Callable, Iterable, Sequence

from unicorn import UC_ARCH_X86, UC_HOOK_CODE, UC_HOOK_MEM_READ, UC_HOOK_MEM_WRITE, UC_MODE_32, Uc, UcError
from unicorn.x86_const import (UC_X86_REG_EAX, UC_X86_REG_EBP, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDI,
                               UC_X86_REG_EDX, UC_X86_REG_EIP, UC_X86_REG_ESI, UC_X86_REG_ESP)

from oracle_pe import PeImage

PAGE = 0x1000
STACK_BASE = 0x00100000
STACK_SIZE = 0x40000
SENTINEL = 0x0FFF0000  # return address of the outer call; an unmapped-code page of its own
HEAP_BASE = 0x10000000
HEAP_SIZE = 0x100000
DEFAULT_INSTRUCTION_LIMIT = 200_000
CALLEE_SAVED = {"ebx": UC_X86_REG_EBX, "esi": UC_X86_REG_ESI, "edi": UC_X86_REG_EDI, "ebp": UC_X86_REG_EBP}
SCRATCH_FILL = 0xCCCCCCCC

ArgumentReader = Callable[[int], int]
Stub = Callable[[ArgumentReader], int]


class RunFailure(Exception):
    def __init__(self, kind: str, detail: str):
        super().__init__(f"{kind}: {detail}")
        self.kind = kind
        self.detail = detail


class StubRejected(Exception):
    """Raised by a stub whose arguments are outside the world model."""


@dataclass(frozen=True)
class Range:
    start: int
    end: int  # exclusive

    def __contains__(self, address: int) -> bool:
        return self.start <= address < self.end


def _round_up(value: int) -> int:
    return (value + PAGE - 1) & ~(PAGE - 1)


def to_signed(value: int) -> int:
    value &= 0xFFFFFFFF
    return value - (1 << 32) if value & 0x80000000 else value


class Emulator:
    def __init__(self, image: PeImage, instruction_limit: int = DEFAULT_INSTRUCTION_LIMIT):
        self.image_range = Range(image.base, image.base + _round_up(len(image.image)))
        own = (Range(STACK_BASE, STACK_BASE + STACK_SIZE), Range(SENTINEL, SENTINEL + PAGE), Range(HEAP_BASE, HEAP_BASE + HEAP_SIZE))
        if image.base % PAGE or any(r.start < self.image_range.end and self.image_range.start < r.end for r in own):
            raise ValueError("image overlaps the emulator's own regions")
        self.image = image
        self.instruction_limit = instruction_limit
        self.mu = Uc(UC_ARCH_X86, UC_MODE_32)
        self.mu.mem_map(image.base, _round_up(len(image.image)))
        self.mu.mem_write(image.base, image.image)
        self.mu.mem_map(STACK_BASE, STACK_SIZE)
        self.mu.mem_map(SENTINEL, PAGE)
        self.mu.mem_map(HEAP_BASE, HEAP_SIZE)
        self.stack = Range(STACK_BASE, STACK_BASE + STACK_SIZE)
        self.heap = Range(HEAP_BASE, HEAP_BASE + HEAP_SIZE)
        self.instructions = 0

    def write_heap(self, data: bytes, offset: int = 0) -> int:
        """Place world-model data on the oracle's own heap (not part of the image); returns its address."""
        if offset < 0 or offset + len(data) > HEAP_SIZE:
            raise ValueError("heap write out of range")
        self.mu.mem_write(HEAP_BASE + offset, data)
        return HEAP_BASE + offset

    def set_global(self, address: int, value: int) -> None:
        """Set a 32-bit global of the image to a world-model value (written by the oracle, not by the function)."""
        if address not in self.image_range or address + 4 > self.image_range.end:
            raise ValueError(f"global {address:#x} outside the image")
        self.mu.mem_write(address, struct.pack("<I", value & 0xFFFFFFFF))

    def call(self, function: int, arguments: Sequence[int], *, code: Iterable[Range], stubs: dict[int, Stub] | None = None,
             readable: Iterable[Range] = ()) -> int:
        """Call `function` (cdecl) with 32-bit `arguments`; returns EAX as a signed 32-bit value."""
        code = tuple(code)
        readable = tuple(readable)
        stubs = dict(stubs or {})
        if not any(function in allowed for allowed in code):
            raise ValueError(f"function {function:#x} is outside its own allowed code ranges")
        mu = self.mu
        mu.mem_write(STACK_BASE, b"\0" * STACK_SIZE)
        esp = STACK_BASE + STACK_SIZE - 0x100
        for argument in reversed(arguments):
            esp -= 4
            mu.mem_write(esp, struct.pack("<I", argument & 0xFFFFFFFF))
        esp -= 4
        mu.mem_write(esp, struct.pack("<I", SENTINEL))
        mu.reg_write(UC_X86_REG_ESP, esp)
        saved = {"ebx": 0x0B0B0B0B, "esi": 0x05050505, "edi": 0x0D0D0D0D, "ebp": 0x0E0E0E0E}
        for name, register in CALLEE_SAVED.items():
            mu.reg_write(register, saved[name])
        for register in (UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_EDX):
            mu.reg_write(register, SCRATCH_FILL)
        failure: list[RunFailure] = []
        count = [0]

        def fail(kind: str, detail: str) -> None:
            if not failure:
                failure.append(RunFailure(kind, detail))
            mu.emu_stop()

        def on_code(uc: Uc, address: int, size: int, _: object) -> None:
            count[0] += 1
            if failure:
                return
            if address in stubs:
                sp = uc.reg_read(UC_X86_REG_ESP)
                caller = struct.unpack("<I", uc.mem_read(sp, 4))[0]

                def argument(index: int) -> int:
                    return struct.unpack("<I", uc.mem_read(sp + 4 + 4 * index, 4))[0]
                try:
                    result = stubs[address](argument)
                except StubRejected as error:
                    fail("stub", f"stub {address:#x} called from {caller:#x}: {error}")
                    return
                uc.reg_write(UC_X86_REG_EAX, result & 0xFFFFFFFF)
                uc.reg_write(UC_X86_REG_ESP, sp + 4)
                uc.reg_write(UC_X86_REG_EIP, caller)
                return
            if address == SENTINEL:
                return
            if not any(address in allowed for allowed in code):
                fail("call", f"execution reached {address:#x}, outside the allowed code and stubs")

        def on_read(uc: Uc, access: int, address: int, size: int, value: int, _: object) -> None:
            if failure or address in self.stack or address in self.heap:
                return
            if address in self.image_range and any(address in r and address + size <= r.end for r in readable):
                return
            fail("read", f"read of {size} bytes at {address:#x} outside the world model")

        def on_write(uc: Uc, access: int, address: int, size: int, value: int, _: object) -> None:
            if failure or (address in self.stack and address + size <= self.stack.end):
                return
            fail("write", f"write of {size} bytes at {address:#x} outside the stack")

        hooks = [mu.hook_add(UC_HOOK_CODE, on_code), mu.hook_add(UC_HOOK_MEM_READ, on_read),
                 mu.hook_add(UC_HOOK_MEM_WRITE, on_write)]
        fault = None
        try:
            mu.emu_start(function, SENTINEL, count=self.instruction_limit)
        except UcError as error:
            fault = str(error)
        finally:
            for hook in hooks:
                mu.hook_del(hook)
            self.instructions = count[0]
        if failure:
            raise failure[0]
        if fault is not None:
            raise RunFailure("fault", f"{fault} at {mu.reg_read(UC_X86_REG_EIP):#x}")
        eip = mu.reg_read(UC_X86_REG_EIP)
        if eip != SENTINEL:
            raise RunFailure("no-return", f"stopped at {eip:#x} after {count[0]} instructions")
        if mu.reg_read(UC_X86_REG_ESP) != esp + 4:
            raise RunFailure("abi", "stack not balanced on return")
        for name, register in CALLEE_SAVED.items():
            if mu.reg_read(register) != saved[name]:
                raise RunFailure("abi", f"callee-saved {name} not restored")
        return to_signed(mu.reg_read(UC_X86_REG_EAX))
