"""Functions the oracle may run, their world model and their seeded inputs (ARC-DEV-007).

Each function has its own code range (its first instruction up to the next function) plus the ranges of the game
functions it may call, the stubs that replace service functions, and the globals it may read. Inputs are drawn from a
seed inside the domain of the C# side (ExperienceTable.Create, levels 0..51, experience 0..2,000,000,000; for the skill
functions of ARC-SKILLS-001 one critter with its twelve stored basic skills, its eight primary stats, the monstrous flag
and one additive effect change of the skill); the same model becomes the inputs of the OriginalExe tests. Importing this
module does not need Unicorn.
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

# Service functions, fields and tables used by the skill functions (ARC-SKILLS-001).
FIELD_GETTER = 0x406CA0         # object field: (handle low, handle high, field) -> value
ARRAY_GETTER = 0x4072D0         # array field element: (handle low, handle high, field, index) -> value
ARRAY_SETTER = 0x407340         # array field element write: (handle low, handle high, field, index, value)
MONSTROUS = 0x45F730            # critter is mechanical, monster, animal or undead: (handle low, handle high) -> 0 or 1
SKILL_EFFECTS = 0x4EA960        # effect changes of a basic skill: (handle low, handle high, skill, level) -> level
NETWORK_GATE = 0x4A2BA0         # network authority test; 1 in single player
EDUCATOR = 0x4C2950             # the Educator background update after a training change: (handle low, handle high)
FIELD_TYPE = 0x14F
FIELD_BASIC_SKILLS = 0xDD
CRITTER_TYPES = (15, 16)        # PC, NPC
BASIC_SKILLS = 12
PRIMARY_STATS = 8
KEY_STAT_TABLE = (0x5B6F04, 0x5B6F04 + 4 * BASIC_SKILLS)
TRAINING_MINIMA = (0x5B6F44, 0x5B6F44 + 16)
SKILL_CAPS = (0x5B6FA4, 0x5B6FA4 + 80)  # the cap per key stat 1..20
SKILL_LEVEL_CODE = ((0x4C5EB0, 0x4C5F70), (0x4C5E50, 0x4C5EB0), (0x4C5F70, 0x4C5FA0), (0x4C5FA0, 0x4C6000))


@dataclass(frozen=True)
class FunctionSpec:
    address: int
    name: str                          # Steamwoven's own name
    code: tuple[tuple[int, int], ...]  # allowed code ranges, [start, end), the function's own first
    model: str                         # "experience", "bonus", "skill-level" or "training-set"

    @property
    def key(self) -> str:
        return f"{self.address:06X}"


FUNCTIONS: dict[int, FunctionSpec] = {spec.address: spec for spec in (
    FunctionSpec(0x4A68B0, "experience-to-next-level", ((0x4A68B0, 0x4A6900),), "experience"),
    FunctionSpec(0x4A6900, "experience-progress", ((0x4A6900, 0x4A6980), (0x4A68B0, 0x4A6900)), "experience"),
    FunctionSpec(0x4A6980, "level-bonus-points", ((0x4A6980, 0x4A69C0),), "bonus"),
    FunctionSpec(0x4C5EB0, "basic-skill-level", SKILL_LEVEL_CODE, "skill-level"),
    FunctionSpec(0x4C6170, "basic-skill-training-set", ((0x4C6170, 0x4C62B0),) + SKILL_LEVEL_CODE, "training-set"),
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


@dataclass
class Critter:
    """The skill functions' one critter: type, twelve stored basic skills, eight primary stats, the monstrous flag and an
    additive effect change of the skill's level."""
    kind: int
    skills: list[int]
    stats: list[int]
    monstrous: int
    effect: int


def draw_critter(rng: random.Random) -> tuple[int, Critter]:
    """A skill (melee more often, for the monstrous rule) and a critter inside SkillRules' domain: stored values 0..1023
    (points mostly 0..6, any training bits, sometimes higher bits), stats mostly 1..20 with the clamp edges, effects
    mostly 0."""
    skill = 2 if rng.random() < 0.3 else rng.randrange(BASIC_SKILLS)

    def stored() -> int:
        points = rng.randint(0, 6) if rng.random() < 0.85 else rng.randint(0, 63)
        high = rng.randint(1, 3) << 8 if rng.random() < 0.1 else 0
        return points | rng.randint(0, 3) << 6 | high

    def stat() -> int:
        return rng.randint(1, 20) if rng.random() < 0.85 else rng.choice((-3, 0, 0, 1, 5, 6, 8, 9, 20, 21, 25, 40))
    effect = 0 if rng.random() < 0.5 else rng.randint(-25, 25)
    return skill, Critter(rng.choice(CRITTER_TYPES), [stored() for _ in range(BASIC_SKILLS)], [stat() for _ in range(PRIMARY_STATS)],
                          int(rng.random() < 0.35), effect)


def critter_stubs(critter: Critter, writes: list[tuple[int, int]] | None = None, educated: list[int] | None = None) -> dict:
    """Stubs answering from one critter; any other object, field, index, stat or skill is rejected."""
    from oracle_emu import StubRejected

    def same(argument) -> None:
        if (argument(0), argument(1)) != HANDLE:
            raise StubRejected(f"handle {argument(0):#x}:{argument(1):#x} is not the world model's object")

    def field(argument) -> int:
        same(argument)
        if argument(2) != FIELD_TYPE:
            raise StubRejected(f"field {argument(2):#x} is not in the world model")
        return critter.kind

    def index(argument) -> int:
        same(argument)
        if argument(2) != FIELD_BASIC_SKILLS or not 0 <= argument(3) < BASIC_SKILLS:
            raise StubRejected(f"array {argument(2):#x}[{argument(3)}] is not in the world model")
        return argument(3)

    def stat(argument) -> int:
        same(argument)
        if not 0 <= argument(2) < PRIMARY_STATS:
            raise StubRejected(f"stat {argument(2)} is not in the world model")
        return critter.stats[argument(2)]

    def monstrous(argument) -> int:
        same(argument)
        return critter.monstrous

    def effects(argument) -> int:
        same(argument)
        if not 0 <= argument(2) < BASIC_SKILLS:
            raise StubRejected(f"skill {argument(2)} is not in the world model")
        return (argument(3) + critter.effect) & 0xFFFFFFFF

    stubs = {FIELD_GETTER: field, ARRAY_GETTER: lambda argument: critter.skills[index(argument)], STAT_GETTER: stat,
             MONSTROUS: monstrous, SKILL_EFFECTS: effects}
    if writes is not None:
        def write(argument) -> int:
            writes.append((index(argument), argument(4)))
            return 0

        def educator(argument) -> int:
            same(argument)
            educated.append(1)
            return 0
        stubs.update({ARRAY_SETTER: write, EDUCATOR: educator, NETWORK_GATE: lambda argument: 1})
    return stubs


# --- runs ------------------------------------------------------------------------------------------------------------

def run(spec: FunctionSpec, emulator, seed: int, count: int, *, exe_sha256: str, unicorn_version: str,
        progress: Callable[[int], None] | None = None) -> dict:
    """Run `spec` on `count` seeded inputs; any failed run raises (oracle_emu.RunFailure)."""
    from oracle_emu import Range, RunFailure, StubRejected

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
    elif spec.model in ("skill-level", "training-set"):
        from oracle_emu import to_signed
        readable = tuple(Range(start, end) for start, end in (KEY_STAT_TABLE, TRAINING_MINIMA, SKILL_CAPS))
        for case in range(count):
            skill, critter = draw_critter(rng)
            row = [skill, critter.skills[skill], critter.monstrous, critter.effect, *critter.stats]
            if spec.model == "skill-level":
                row.append(emulator.call(spec.address, (*HANDLE, skill), code=code, stubs=critter_stubs(critter), readable=readable))
            else:
                training = rng.randint(0, 3) if rng.random() < 0.95 else rng.choice((-1, 4, 5))
                writes: list[tuple[int, int]] = []
                educated: list[int] = []
                result = emulator.call(spec.address, (*HANDLE, skill, training), code=code,
                                       stubs=critter_stubs(critter, writes, educated), readable=readable)
                if len(writes) > 1 or (writes and writes[0][0] != skill) or len(educated) > 1:
                    raise RunFailure("stub", f"unexpected writes {writes} or Educator calls {len(educated)}")
                row.extend((training, result, to_signed(writes[0][1]) if writes else -1, len(educated)))
            cases.append(row)
            if progress and case % 1000 == 0:
                progress(case)
        stats = ["st", "dx", "cn", "be", "in", "pe", "wp", "ch"]
        tail = ["result"] if spec.model == "skill-level" else ["training", "result", "written", "educated"]
        document.update(fields=["skill", "stored", "monstrous", "effect", *stats, *tail])
    else:
        raise ValueError(f"unknown model {spec.model}")
    document["cases"] = cases
    return document
