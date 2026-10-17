"""Functions the oracle may run, their world model and their seeded inputs (ARC-DEV-007).

Each function has its own code range (its first instruction up to the next function) plus the ranges of the game
functions it may call, the stubs that replace service functions, and the globals it may read. Inputs are drawn from a
seed inside the domain of the C# side (ExperienceTable.Create, levels 0..51, experience 0..2,000,000,000); the same
model becomes the inputs of the OriginalExe tests. Importing this module does not need Unicorn.
"""
from __future__ import annotations

from dataclasses import dataclass
import random
import struct
from typing import Callable

MAX_LEVEL = 51
MAX_EXPERIENCE = 2_000_000_000
# 1000 x remaining stops fitting in 32 bits above this many experience points still to the next level (ledger XP3).
XP3_BOUND = 2_147_483
TABLE_POOL = 256
FORMAT = 1

# Service function and global used by the experience functions (addresses in the retail 1.0.7.4 executable).
STAT_GETTER = 0x4B0490          # stat value of an object: (handle low, handle high, stat number) -> value
XP_TABLE_POINTER = 0x5F5C94     # pointer to the 51 experience thresholds read from rules\xp_level.mes
STAT_LEVEL = 17
STAT_EXPERIENCE = 18
HANDLE = (0x5EED0001, 0x00000042)  # the one fictitious object of the world model


@dataclass(frozen=True)
class FunctionSpec:
    address: int
    name: str                          # Steamwoven's own name
    code: tuple[tuple[int, int], ...]  # allowed code ranges, [start, end), the function's own first
    model: str                         # "experience" or "bonus"

    @property
    def key(self) -> str:
        return f"{self.address:06X}"


FUNCTIONS: dict[int, FunctionSpec] = {spec.address: spec for spec in (
    FunctionSpec(0x4A68B0, "experience-to-next-level", ((0x4A68B0, 0x4A6900),), "experience"),
    FunctionSpec(0x4A6900, "experience-progress", ((0x4A6900, 0x4A6980), (0x4A68B0, 0x4A6900)), "experience"),
    FunctionSpec(0x4A6980, "level-bonus-points", ((0x4A6980, 0x4A69C0),), "bonus"),
)}


def parse_address(text: str) -> int:
    value = int(text, 16)
    if value not in FUNCTIONS:
        raise ValueError(f"no oracle function at {text} (known: {', '.join(f'0x{a:X}' for a in FUNCTIONS)})")
    return value


# --- seeded world models ---------------------------------------------------------------------------------------------

def _fit(steps: list[int], total: int, exact: bool) -> list[int]:
    current = sum(steps)
    if current > total or exact:
        steps = [max(1, step * (total - len(steps)) // current) for step in steps]
        if exact:
            steps[-1] += total - sum(steps)
    return steps


def draw_table(rng: random.Random) -> list[int]:
    """51 thresholds from 0, strictly increasing, at most MAX_EXPERIENCE (the admission of ExperienceTable.Create)."""
    kind = rng.choice(("gentle", "steep", "bound", "unit", "max"))
    if kind == "gentle":  # smooth growth like a shipped table
        steps, step = [], rng.randint(500, 2000)
        for _ in range(MAX_LEVEL - 1):
            steps.append(step)
            step = int(step * rng.uniform(1.03, 1.2)) + rng.randint(0, 500)
        steps = _fit(steps, MAX_EXPERIENCE, False)
    elif kind == "steep":
        steps = _fit([rng.randint(1, rng.choice((10, 1000, 100_000, 10_000_000, 80_000_000))) for _ in range(MAX_LEVEL - 1)],
                     MAX_EXPERIENCE, False)
    elif kind == "bound":  # steps around the XP3 bound
        steps = [XP3_BOUND + rng.randint(-50_000, 50_000) for _ in range(MAX_LEVEL - 1)]
    elif kind == "unit":
        steps = [rng.randint(1, 3) for _ in range(MAX_LEVEL - 1)]
    else:  # the last threshold exactly at the maximum
        steps = _fit([rng.randint(1, 100_000_000) for _ in range(MAX_LEVEL - 1)], MAX_EXPERIENCE, True)
    table = [0]
    for step in steps:
        table.append(table[-1] + step)
    return table


def admissible(table: list[int]) -> bool:
    return (len(table) == MAX_LEVEL and table[0] == 0 and table[-1] <= MAX_EXPERIENCE
            and all(b > a for a, b in zip(table, table[1:])))


def draw_experience_case(rng: random.Random, tables: list[list[int]]) -> tuple[int, int, int]:
    index = rng.randrange(len(tables))
    table = tables[index]
    level = rng.randint(0, MAX_LEVEL) if rng.random() < 0.6 else rng.choice((0, 1, 2, 49, 50, 51))
    here = table[level - 1] if level >= 1 else 0
    following = table[min(level, MAX_LEVEL - 1)]
    pick = rng.randrange(10)
    experience = (rng.randint(0, MAX_EXPERIENCE), here, here + rng.randint(0, 5000), following - 1, following,
                  following + 1, following + rng.randint(1, 100_000), following - XP3_BOUND, following - XP3_BOUND - 1,
                  following - rng.randint(0, following))[pick]
    return index, level, min(MAX_EXPERIENCE, max(0, experience))


def draw_bonus_case(rng: random.Random) -> tuple[int, int]:
    if rng.random() < 0.05:
        return rng.randint(-1000, 60), rng.randint(60, 1000)
    return rng.randint(-5, 60), rng.randint(-5, 65)


# --- runs ------------------------------------------------------------------------------------------------------------

def run(spec: FunctionSpec, emulator, seed: int, count: int, *, exe_sha256: str, unicorn_version: str,
        progress: Callable[[int], None] | None = None) -> dict:
    """Run `spec` on `count` seeded inputs; any failed run raises (oracle_emu.RunFailure)."""
    from oracle_emu import Range, StubRejected

    rng = random.Random(f"{seed}:{spec.key}")
    code = tuple(Range(start, end) for start, end in spec.code)
    document = {"format": FORMAT, "function": f"0x{spec.address:X}", "key": spec.key, "name": spec.name,
                "exeSha256": exe_sha256, "seed": seed, "count": count, "unicorn": unicorn_version}
    cases: list[list[int]] = []
    if spec.model == "experience":
        tables = [draw_table(rng) for _ in range(TABLE_POOL)]
        addresses = [emulator.write_heap(struct.pack(f"<{MAX_LEVEL}i", *table), index * 4 * MAX_LEVEL)
                     for index, table in enumerate(tables)]
        state = {"level": 0, "experience": 0}

        def stat(argument) -> int:
            if (argument(0), argument(1)) != HANDLE:
                raise StubRejected(f"handle {argument(0):#x}:{argument(1):#x} is not the world model's object")
            number = argument(2)
            if number == STAT_LEVEL:
                return state["level"]
            if number == STAT_EXPERIENCE:
                return state["experience"]
            raise StubRejected(f"stat {number} is not in the world model")

        for case in range(count):
            index, level, experience = draw_experience_case(rng, tables)
            state.update(level=level, experience=experience)
            emulator.set_global(XP_TABLE_POINTER, addresses[index])
            result = emulator.call(spec.address, HANDLE, code=code, stubs={STAT_GETTER: stat},
                                   readable=(Range(XP_TABLE_POINTER, XP_TABLE_POINTER + 4),
                                             Range(addresses[index], addresses[index] + 4 * MAX_LEVEL)))
            cases.append([index, level, experience, result])
            if progress and case % 1000 == 0:
                progress(case)
        document.update(fields=["table", "level", "experience", "result"], tables=tables)
    elif spec.model == "bonus":
        for case in range(count):
            first, last = draw_bonus_case(rng)
            cases.append([first, last, emulator.call(spec.address, (first, last), code=code)])
            if progress and case % 1000 == 0:
                progress(case)
        document.update(fields=["from", "to", "result"])
    else:
        raise ValueError(f"unknown model {spec.model}")
    document["cases"] = cases
    return document
