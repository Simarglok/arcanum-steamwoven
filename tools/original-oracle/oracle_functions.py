"""Functions the oracle may run, their world model and their seeded inputs (ARC-DEV-007).

Each function has its own code range (its first instruction up to the next function) plus the ranges of the game
functions it may call, the stubs that replace service functions, and the globals it may read. Inputs are drawn from a
seed inside the domain of the C# side (ExperienceTable.Create, levels 0..51, experience 0..2,000,000,000; for the skill
functions of ARC-SKILLS-001 one critter with its twelve stored basic skills, its eight primary stats, the monstrous flag
and one additive effect change of the skill; for the spell cost functions of ARC-MAGIC-003 one caster, one spell record's
cost, the cheat flag and a recording payment stub; for the effectiveness functions of ARC-SKILLS-003 a source and an
optional target critter with their skill levels, the target's perception, intelligence and Magnetic Inversion flag, the
source's extraordinary intelligence, the local PC and the game difficulty; for the spell learn of ARC-MAGIC-004 one critter's
college rank, level, intelligence, willpower and magick points, the spell's willpower minimum and recording rank and stat
setters); the same model becomes the inputs of the OriginalExe tests. Importing this module does not need Unicorn.
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

# Globals and service functions used by the spell cost functions (ARC-MAGIC-003).
SPELL_TABLE_POINTER = 0x6876D8  # pointer to the 223 spell records of 0x158 bytes; +0x08 is Cost
SPELL_RECORD = 0x158
SPELL_RECORDS = 223
SPELL_COST_FIELD = 0x08
SPELLS_LOADED = 0x5E7618        # the magic-tech init's loaded flag, read by the cost getter 0x450340
CHEAT = 0x5E75FC                # the cheat flag: every run-time and check cost is 1
MASTERY = 0x4B1CB0              # mastered college of a critter: (handle low, handle high) -> 0..15 or -1
PAY = 0x450420                  # payment: (handle low, handle high, cost, apply, spell) -> 1 paid / 0 refused; a recording stub
STAT_RACE = 27
DWARF = 1
COST_GETTER_CODE = (0x450340, 0x450370)

# Service functions and tables used by the effectiveness functions (ARC-SKILLS-003).
BASIC_LEVEL = 0x4C5EB0          # effective basic skill level: (handle low, handle high, skill) -> 0..20; stubbed here
TECH_LEVEL = 0x4C65E0           # effective tech skill level: (handle low, handle high, tech skill) -> 0..20; stubbed here
EXTRAORDINARY = 0x4B0EE0        # a stat is extraordinary: (handle low, handle high, stat) -> 0 or 1
LOCAL_PC = 0x40DA50             # the local PC's handle in EDX:EAX
DIFFICULTY = 0x404590           # the game difficulty setting: 0 easy, 1 normal, 2 hard
FIELD_SPELL_FLAGS = 0x14
MAGNETIC_INVERSION = 0x400000
STAT_IN, STAT_PE = 4, 5
TECH_SKILLS = 4
TARGET = (0x5EED0002, 0x00000042)  # the second fictitious object (the target)
OTHER_PC = (0x5EED0003, 0x00000042)
SKILL_FLAGS = (0x5B6F64, 0x5B6F64 + 4 * (BASIC_SKILLS + TECH_SKILLS))
OPPOSED_TABLE = (0x5B7044, 0x5B7044 + 4 * 19)
BASIC_EFFECTIVENESS_CODE = ((0x4C62E0, 0x4C6410), (0x4C6410, 0x4C64B0))

# Service functions and globals used by the spell learn (ARC-MAGIC-004).
SPELL_ARRAY_GETTER = 0x407470   # spell-and-tech array element: (handle low, handle high, field, index) -> value
SPELL_ARRAY_SETTER = 0x4F0270   # spell-and-tech array element write: (handle low, handle high, field, index, value)
STAT_BASE_GETTER = 0x4B0740     # stored stat: (handle low, handle high, stat) -> value
STAT_SETTER = 0x4B0980          # stored stat write: (handle low, handle high, stat, value)
IS_PLAYER = 0x40D9F0            # the object is the local player: (handle low, handle high) -> 0 or 1
FIELD_SPELL_TECH = 0xDF
STAT_INTELLIGENCE = 4
STAT_WILLPOWER = 6
STAT_MAGICK_POINTS = 22
SPELL_WILLPOWER_FIELD = 0x04
DEFAULT_WILLPOWER = (0x5B0DE0, 0x5B0DE0 + 20)   # 6, 9, 12, 15, 18 before the spell table is loaded
LEVEL_MINIMA = (0x5B5770, 0x5B5770 + 20)        # 1, 1, 5, 10, 15
COLLEGE_SPELLS = 80
SPELL_LEARN_CODE = ((0x4B1790, 0x4B1950), (0x4B1750, 0x4B1790), (0x4B1650, 0x4B1660), (0x4B1AB0, 0x4B1B90), (0x4502E0, 0x450340))


@dataclass(frozen=True)
class FunctionSpec:
    address: int
    name: str                          # Steamwoven's own name
    code: tuple[tuple[int, int], ...]  # allowed code ranges, [start, end), the function's own first
    model: str                         # "experience", "bonus", "skill-level", "training-set", "spell-cost" or "effectiveness"

    @property
    def key(self) -> str:
        return f"{self.address:06X}"


FUNCTIONS: dict[int, FunctionSpec] = {spec.address: spec for spec in (
    FunctionSpec(0x4A68B0, "experience-to-next-level", ((0x4A68B0, 0x4A6900),), "experience"),
    FunctionSpec(0x4A6900, "experience-progress", ((0x4A6900, 0x4A6980), (0x4A68B0, 0x4A6900)), "experience"),
    FunctionSpec(0x4A6980, "level-bonus-points", ((0x4A6980, 0x4A69C0),), "bonus"),
    FunctionSpec(0x4C5EB0, "basic-skill-level", SKILL_LEVEL_CODE, "skill-level"),
    FunctionSpec(0x4C6170, "basic-skill-training-set", ((0x4C6170, 0x4C62B0),) + SKILL_LEVEL_CODE, "training-set"),
    FunctionSpec(0x4C62E0, "basic-skill-effectiveness", BASIC_EFFECTIVENESS_CODE, "effectiveness"),
    FunctionSpec(0x4C6410, "opposed-skill-effectiveness", ((0x4C6410, 0x4C64B0),), "effectiveness"),
    FunctionSpec(0x4C69F0, "tech-skill-effectiveness", ((0x4C69F0, 0x4C6AF0),), "effectiveness"),
    FunctionSpec(0x4507D0, "spell-run-cost", ((0x4507D0, 0x4508A0),), "spell-cost"),
    FunctionSpec(0x4508A0, "spell-check-cost", ((0x4508A0, 0x450940),), "spell-cost"),
    FunctionSpec(0x4B1660, "spell-cast-cost", ((0x4B1660, 0x4B16C0), COST_GETTER_CODE), "spell-cost"),
    FunctionSpec(0x4B1790, "spell-learn", SPELL_LEARN_CODE, "spell-learn"),
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


@dataclass
class Recorder:
    """What a training-set run did: the array writes (index, value) and the Educator updates."""
    writes: list[tuple[int, int]]
    educated: int = 0


def critter_stubs(critter: Critter, recorder: Recorder | None = None) -> dict:
    """Stubs answering from one critter; any other object, field, index, stat or skill is rejected. With a recorder the
    array write, the Educator update and the network test (single player) are stubbed too."""
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
    if recorder is not None:
        def write(argument) -> int:
            recorder.writes.append((index(argument), argument(4)))
            return 0

        def educator(argument) -> int:
            same(argument)
            recorder.educated += 1
            return 0
        stubs.update({ARRAY_SETTER: write, EDUCATOR: educator, NETWORK_GATE: lambda argument: 1})
    return stubs


@dataclass
class SpellCostCase:
    """The spell cost functions' one caster and spell: the spell number, its record's cost, the caster's mastered college,
    object type and race, the cheat flag and what the payment stub answers."""
    spell: int
    cost: int
    mastery: int
    kind: int
    race: int
    cheat: int
    paid: int


def draw_spell_cost_case(rng: random.Random) -> SpellCostCase:
    """A spell 0..222 (college spells more often), a cost mostly 0..60 with odd values and sometimes negative or near the
    32-bit edges, a mastered college often the spell's own, a PC or NPC mostly (items and other types too), a dwarf often,
    the cheat flag rarely."""
    spell = rng.randrange(80) if rng.random() < 0.6 else rng.randrange(SPELL_RECORDS)
    roll = rng.random()
    cost = (rng.randint(0, 60) if roll < 0.85 else rng.randint(-60, -1) if roll < 0.92
            else rng.choice((0x3FFFFFFF, 0x40000000, 0x7FFFFFFF, -0x40000000, -0x40000001, -0x80000000)))
    mastery = spell // 5 if spell < 80 and rng.random() < 0.5 else rng.randint(-1, 15)
    kind = rng.choice((15, 16)) if rng.random() < 0.8 else rng.choice((0, 3, 5, 9, 14, 17))
    race = DWARF if rng.random() < 0.4 else rng.randint(0, 10)
    return SpellCostCase(spell, cost, mastery, kind, race, int(rng.random() < 0.1), int(rng.random() < 0.8))


@dataclass
class PayRecorder:
    """The payment calls of one run: (cost, apply, spell) each."""
    calls: list[tuple[int, int, int]]


def spell_cost_stubs(case: SpellCostCase, recorder: PayRecorder) -> dict:
    """Stubs answering from one caster: its type, race and mastered college; the payment records its arguments and
    answers the case's result. Any other object, field or stat is rejected."""
    from oracle_emu import StubRejected, to_signed

    def same(argument) -> None:
        if (argument(0), argument(1)) != HANDLE:
            raise StubRejected(f"handle {argument(0):#x}:{argument(1):#x} is not the world model's object")

    def field(argument) -> int:
        same(argument)
        if argument(2) != FIELD_TYPE:
            raise StubRejected(f"field {argument(2):#x} is not in the world model")
        return case.kind

    def stat(argument) -> int:
        same(argument)
        if argument(2) != STAT_RACE:
            raise StubRejected(f"stat {argument(2)} is not in the world model")
        return case.race

    def mastery(argument) -> int:
        same(argument)
        return case.mastery & 0xFFFFFFFF

    def pay(argument) -> int:
        same(argument)
        recorder.calls.append((to_signed(argument(2)), argument(3), to_signed(argument(4))))
        return case.paid

    return {FIELD_GETTER: field, STAT_GETTER: stat, MASTERY: mastery, PAY: pay}


@dataclass
class EffectivenessCase:
    """The effectiveness functions' source and optional target: the skill (basic 0..11, tech 0..3), the source's level, the
    target's presence, level of the same skill, perception, intelligence and spell flags, the source's extraordinary
    intelligence, who the local PC is (0 the source, 1 another PC, 2 none) and the difficulty setting."""
    skill: int
    level: int
    target: int
    target_level: int
    target_pe: int
    target_in: int
    spell_flags: int
    extraordinary: int
    local: int
    difficulty: int


def draw_effectiveness_case(rng: random.Random, tech: bool) -> EffectivenessCase:
    """Levels 0..20 with the edges more often, a target four times in five, its stats mostly 1..20 and sometimes far
    off (so the opposed thresholds are crossed), spell flags with Magnetic Inversion half of the time among other bits, the
    difficulty mostly 0..2 (other values rarely)."""
    def level() -> int:
        return rng.choice((0, 1, 19, 20)) if rng.random() < 0.2 else rng.randint(0, 20)

    def stat() -> int:
        return rng.randint(1, 20) if rng.random() < 0.8 else rng.choice((-5, 0, 25, 30, 40))
    flags = rng.getrandbits(32) & ~MAGNETIC_INVERSION if rng.random() < 0.3 else 0
    if rng.random() < 0.5:
        flags |= MAGNETIC_INVERSION
    difficulty = rng.randint(0, 2) if rng.random() < 0.95 else rng.choice((-1, 3, 7))
    return EffectivenessCase(rng.randrange(TECH_SKILLS if tech else BASIC_SKILLS), level(), int(rng.random() < 0.8), level(),
                             stat(), stat(), flags, int(rng.random() < 0.3), rng.choice((0, 0, 1, 2)), difficulty)


def effectiveness_stubs(case: EffectivenessCase) -> dict:
    """Stubs answering from the source (HANDLE) and the target (TARGET): skill levels, the target's PE and IN, the
    source's extraordinary IN, the target's spell flags, the local PC and the difficulty. Anything else is rejected."""
    from oracle_emu import StubRejected

    def who(argument) -> int:
        handle = (argument(0), argument(1))
        if handle == HANDLE:
            return 0
        if handle == TARGET and case.target:
            return 1
        raise StubRejected(f"handle {argument(0):#x}:{argument(1):#x} is not the world model's object")

    def level(limit: int):
        def answer(argument) -> int:
            side = who(argument)
            if argument(2) != case.skill or not 0 <= argument(2) < limit:
                raise StubRejected(f"skill {argument(2)} is not in the world model")
            return case.level if side == 0 else case.target_level
        return answer

    def stat(argument) -> int:
        if who(argument) != 1 or argument(2) not in (STAT_IN, STAT_PE):
            raise StubRejected(f"stat {argument(2)} is not in the world model")
        return (case.target_pe if argument(2) == STAT_PE else case.target_in) & 0xFFFFFFFF

    def extraordinary(argument) -> int:
        if who(argument) != 0 or argument(2) != STAT_IN:
            raise StubRejected(f"extraordinary stat {argument(2)} is not in the world model")
        return case.extraordinary

    def field(argument) -> int:
        if who(argument) != 1 or argument(2) != FIELD_SPELL_FLAGS:
            raise StubRejected(f"field {argument(2):#x} is not in the world model")
        return case.spell_flags

    local = (HANDLE, OTHER_PC, (0, 0))[case.local]
    return {BASIC_LEVEL: level(BASIC_SKILLS), TECH_LEVEL: level(TECH_SKILLS), STAT_GETTER: stat, EXTRAORDINARY: extraordinary,
            FIELD_GETTER: field, LOCAL_PC: lambda argument: local, DIFFICULTY: lambda argument: case.difficulty & 0xFFFFFFFF}


@dataclass
class SpellLearnCase:
    """The spell learn's one critter and spell: the spell 0..79, the forced flag, the object type (PC or NPC), the known rank
    of the spell's college, the effective level, intelligence and willpower, the stored magick points, whether the spell
    table is loaded and the spell's willpower minimum there."""
    spell: int
    force: int
    kind: int
    known: int
    level: int
    intelligence: int
    willpower: int
    magick: int
    loaded: int
    minimum: int


def draw_spell_learn_case(rng: random.Random) -> SpellLearnCase:
    """A spell 0..79 whose rank is often the next one, unforced mostly, levels, intelligence and willpower around the minima
    (1, 1, 5, 10, 15; 5; 6..18), magick points 0..210, the table mostly loaded with minima 0..25."""
    spell = rng.randrange(COLLEGE_SPELLS)
    rank = spell % 5 + 1
    known = rank - 1 if rng.random() < 0.6 else rng.randint(0, 5)
    return SpellLearnCase(spell, int(rng.random() < 0.2), rng.choice((15, 16)), known, rng.randint(0, 20), rng.randint(1, 10),
                          rng.randint(1, 22), rng.randint(0, 210), int(rng.random() < 0.8), rng.randint(0, 25))


@dataclass
class LearnRecorder:
    """The writes of one learn: the rank writes (college, rank) and the magick point writes (stat, value)."""
    ranks: list[tuple[int, int]]
    stats: list[tuple[int, int]]


def spell_learn_stubs(case: SpellLearnCase, recorder: LearnRecorder) -> dict:
    """Stubs answering from one critter: its type, its college rank, its effective level, intelligence and willpower, its
    stored magick points; the rank and stat writes are recorded; single player, not the local player. Any other object,
    field, index or stat is rejected."""
    from oracle_emu import StubRejected

    def same(argument) -> None:
        if (argument(0), argument(1)) != HANDLE:
            raise StubRejected(f"handle {argument(0):#x}:{argument(1):#x} is not the world model's object")

    def field(argument) -> int:
        same(argument)
        if argument(2) != FIELD_TYPE:
            raise StubRejected(f"field {argument(2):#x} is not in the world model")
        return case.kind

    def college(argument) -> int:
        same(argument)
        if argument(2) != FIELD_SPELL_TECH or argument(3) != case.spell // 5:
            raise StubRejected(f"array {argument(2):#x}[{argument(3)}] is not in the world model")
        return argument(3)

    def stat(argument) -> int:
        same(argument)
        values = {STAT_LEVEL: case.level, STAT_INTELLIGENCE: case.intelligence, STAT_WILLPOWER: case.willpower}
        if argument(2) not in values:
            raise StubRejected(f"stat {argument(2)} is not in the world model")
        return values[argument(2)]

    def base(argument) -> int:
        same(argument)
        if argument(2) != STAT_MAGICK_POINTS:
            raise StubRejected(f"stored stat {argument(2)} is not in the world model")
        return case.magick

    def set_stat(argument) -> int:
        same(argument)
        if argument(2) != STAT_MAGICK_POINTS:
            raise StubRejected(f"stat write {argument(2)} is not in the world model")
        recorder.stats.append((argument(2), argument(3)))
        return argument(3)

    def set_rank(argument) -> int:
        recorder.ranks.append((college(argument), argument(4)))
        return argument(4)

    def rank(argument) -> int:
        college(argument)
        return case.known

    def player(argument) -> int:
        same(argument)
        return 0

    return {NETWORK_GATE: lambda argument: 1, FIELD_GETTER: field, SPELL_ARRAY_GETTER: rank,
            STAT_GETTER: stat, STAT_BASE_GETTER: base, STAT_SETTER: set_stat, SPELL_ARRAY_SETTER: set_rank, IS_PLAYER: player}


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
        tables = (KEY_STAT_TABLE, SKILL_CAPS) if spec.model == "skill-level" else (KEY_STAT_TABLE, TRAINING_MINIMA, SKILL_CAPS)
        readable = tuple(Range(start, end) for start, end in tables)
        for case in range(count):
            skill, critter = draw_critter(rng)
            row = [skill, critter.skills[skill], critter.monstrous, critter.effect, *critter.stats]
            if spec.model == "skill-level":
                row.append(emulator.call(spec.address, (*HANDLE, skill), code=code, stubs=critter_stubs(critter), readable=readable))
            else:
                training = rng.randint(0, 3) if rng.random() < 0.95 else rng.choice((-1, 4, 5))
                recorder = Recorder([])
                result = emulator.call(spec.address, (*HANDLE, skill, training), code=code,
                                       stubs=critter_stubs(critter, recorder), readable=readable)
                writes = recorder.writes
                if len(writes) > 1 or (writes and writes[0][0] != skill) or recorder.educated > 1:
                    raise RunFailure("stub", f"unexpected writes {writes} or Educator calls {recorder.educated}")
                row.extend((training, result, to_signed(writes[0][1]) if writes else -1, recorder.educated))
            cases.append(row)
            if progress and case % 1000 == 0:
                progress(case)
        stats = ["st", "dx", "cn", "be", "in", "pe", "wp", "ch"]
        tail = ["result"] if spec.model == "skill-level" else ["training", "result", "written", "educated"]
        document.update(fields=["skill", "stored", "monstrous", "effect", *stats, *tail])
    elif spec.model == "spell-cost":
        from oracle_emu import RunFailure
        table = emulator.write_heap(b"\0" * (SPELL_RECORD * SPELL_RECORDS))
        emulator.set_global(SPELL_TABLE_POINTER, table)
        emulator.set_global(SPELLS_LOADED, 1)
        cast = spec.address == 0x4B1660
        for case_number in range(count):
            case = draw_spell_cost_case(rng)
            field = SPELL_RECORD * case.spell + SPELL_COST_FIELD
            emulator.write_heap(struct.pack("<i", case.cost), field)
            emulator.set_global(CHEAT, case.cheat)
            recorder = PayRecorder([])
            readable = (Range(SPELL_TABLE_POINTER, SPELL_TABLE_POINTER + 4), Range(CHEAT, CHEAT + 4), Range(SPELLS_LOADED, SPELLS_LOADED + 4),
                        Range(table + field, table + field + 4))
            arguments = (case.spell, *HANDLE) if cast else (*HANDLE, case.spell)
            result = emulator.call(spec.address, arguments, code=code, stubs=spell_cost_stubs(case, recorder), readable=readable)
            emulator.write_heap(b"\0\0\0\0", field)
            if len(recorder.calls) > 1 or (cast and recorder.calls):
                raise RunFailure("stub", f"unexpected payment calls {recorder.calls}")
            paid = recorder.calls[0] if recorder.calls else (0, -1, -1)
            cases.append([case.spell, case.cost, case.mastery, case.kind, case.race, case.cheat, case.paid, result, len(recorder.calls), *paid])
            if progress and case_number % 1000 == 0:
                progress(case_number)
        document.update(fields=["spell", "cost", "mastery", "type", "race", "cheat", "payAnswer", "result", "payCalls", "paidCost", "paidApply", "paidSpell"])
    elif spec.model == "effectiveness":
        tech = spec.address == 0x4C69F0
        readable = (Range(*SKILL_FLAGS), Range(*OPPOSED_TABLE), Range(0x4C63DC, 0x4C6408), Range(0x4C6AD8, 0x4C6AE8))
        for case_number in range(count):
            case = draw_effectiveness_case(rng, tech)
            target = TARGET if case.target else (0, 0)
            result = emulator.call(spec.address, (*HANDLE, case.skill, *target), code=code, stubs=effectiveness_stubs(case),
                                   readable=readable)
            cases.append([case.skill, case.level, case.target, case.target_level, case.target_pe, case.target_in,
                          int(bool(case.spell_flags & MAGNETIC_INVERSION)), int(bool(case.spell_flags & ~MAGNETIC_INVERSION)),
                          case.extraordinary, case.local, case.difficulty, result])
            if progress and case_number % 1000 == 0:
                progress(case_number)
        document.update(fields=["skill", "level", "target", "targetLevel", "targetPe", "targetIn", "magneticInversion", "otherFlags",
                                "extraordinary", "local", "difficulty", "result"])
    elif spec.model == "spell-learn":
        from oracle_emu import RunFailure, to_signed
        table = emulator.write_heap(b"\0" * (SPELL_RECORD * SPELL_RECORDS))
        emulator.set_global(SPELL_TABLE_POINTER, table)
        for case_number in range(count):
            case = draw_spell_learn_case(rng)
            field = SPELL_RECORD * case.spell + SPELL_WILLPOWER_FIELD
            emulator.write_heap(struct.pack("<i", case.minimum), field)
            emulator.set_global(SPELLS_LOADED, case.loaded)
            recorder = LearnRecorder([], [])
            readable = (Range(SPELL_TABLE_POINTER, SPELL_TABLE_POINTER + 4), Range(SPELLS_LOADED, SPELLS_LOADED + 4),
                        Range(table + field, table + field + 4), Range(*DEFAULT_WILLPOWER), Range(*LEVEL_MINIMA))
            result = emulator.call(spec.address, (*HANDLE, case.spell, case.force), code=code, stubs=spell_learn_stubs(case, recorder),
                                   readable=readable)
            emulator.write_heap(b"\0\0\0\0", field)
            if len(recorder.ranks) > 1 or len(recorder.stats) > 1 or len(recorder.ranks) != len(recorder.stats):
                raise RunFailure("stub", f"unexpected writes {recorder.ranks} {recorder.stats}")
            rank = recorder.ranks[0][1] if recorder.ranks else -1
            magick = to_signed(recorder.stats[0][1]) if recorder.stats else -1
            cases.append([case.spell, case.force, case.kind, case.known, case.level, case.intelligence, case.willpower, case.magick,
                          case.loaded, case.minimum, result, len(recorder.ranks), rank, magick])
            if progress and case_number % 1000 == 0:
                progress(case_number)
        document.update(fields=["spell", "force", "type", "known", "level", "in", "wp", "magick", "loaded", "minimum", "result",
                                "writes", "rankWritten", "magickWritten"])
    else:
        raise ValueError(f"unknown model {spec.model}")
    document["cases"] = cases
    return document
