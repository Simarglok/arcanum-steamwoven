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
setters; for the fate functions of ARC-WORLD-054 one PC with its fate points and armed fates and recording stubs of the stat,
field, hit-point and fatigue writes; for the invocation check's range, forbidden flags and item gate of ARC-MAGIC-006 the
parent's location, the target tile and object, the record's range and forbidden masks, and a holder, an item and the power it
draws); the same model becomes the inputs of the OriginalExe tests. Importing this module does not
need Unicorn.
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

# Globals, record and run fields and service functions used by the resistance and its predictor (ARC-MAGIC-005).
RUN_POINTER = 0x5E75F0          # the magic run being processed
RECORD_POINTER = 0x5E7598       # its spell record
TARGET_HANDLE = 0x5E6D48        # the current target (low, high at +4)
RESIST_POINTER = 0x5E7620       # the record's Resist: pair (stat, modifier)
TARGET_TYPE = 0x5E75AC          # the current target's object type
FATE_MAXIMIZED = 0x5E75A8       # the action is fate-maximised
DISPLAY_MARGIN = 0x5E75A0       # the margin shown for the target (written)
RUN_SPELL, RUN_ACTION, RUN_PARENT, RUN_FLAGS, RUN_MARGIN = 0x04, 0x08, 0x58, 0x13C, 0x144
RECORD_RESIST, RECORD_PERIOD, RECORD_FLAGS = 0x0C, 0x18, 0x34
FLAG_AGGRESSIVE, FLAG_TECHNOLOGY, FLAG_DAMAGING, RUN_UNRESISTABLE = 0x2, 0x4, 0x80, 0x10
FIELD_CRITTER_FLAGS2, NO_DISINTEGRATE = 0xDB, 0x2000
FIELD_RESISTANCES, MAGIC_RESISTANCE = 0x1F, 4
STAT_APTITUDE = 16
FATE_USE = 0x4F5270             # consume an armed fate: (handle low, handle high, kind) -> 1 consumed / 0; kind 4 "save against magick"
FATE_SAVE_AGAINST_MAGICK = 4
RANDOM = 0x4CEF00               # random integer: (min, max) -> min..max
NOTIFY_AI = 0x453F20            # tell the target's AI of a spell it shrugged off: (parent low, parent high, target low, target high)
UNTOUCHED = 0x7FFF0001          # the margins before the call: left as they are when the function returns early
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

# Service functions, fields and tables used by the fate functions (ARC-WORLD-054).
FIELD_SETTER = 0x406D40         # object field write: (handle low, handle high, field, value); a recording stub
HP_DAMAGE_SET = 0x43D530        # hit-point damage write: (handle low, handle high, value); a recording stub
FATIGUE_DAMAGE_SET = 0x45D5A0   # fatigue damage write: (handle low, handle high, value); a recording stub
FIELD_FATE_FLAGS = 0xFE
STAT_FATE_POINTS, STAT_POISON = 20, 24
FATE_KINDS = 12
FATE_MASKS = (0x5BC48C, 0x5BC48C + 4 * FATE_KINDS)
FATE_CALLBACK = 0x603D48        # the fate window's refresh callback; null in the world model
FATE_IMMEDIATE_CODE = (0x4F52D0, 0x4F5320)

# Service functions, fields and record offsets used by the invocation check's range, flags and item gate (ARC-MAGIC-006).
FIELD64_GETTER = 0x406DA0       # 64-bit object field: (handle low, handle high, field) -> EDX:EAX; field 2 is the location
FIELD_LOCATION = 2
FIELD_OBJECT_FLAGS = 0x13
IN_INVENTORY = 0x1000
FIELD_CRITTER_FLAGS = 0xDA
FIELD_COMPLEXITY = 0x60
ITEM_POWER = 0x4614A0           # the power a holder draws from an item: (item low, item high, holder low, holder high); stubbed here
RECORD_RANGE = 0x30
RECORD_DISALLOWED = 0x11C       # caster spell flags, then target spell flags (+4) and target critter flags (+8)
RANGE_CODE = ((0x450280, 0x4502B0), (0x4B96F0, 0x4B9760))
ITEM = (0x5EED0004, 0x00000042)    # the item of the item gate

# Service functions, fields, stats and globals used by the area test (ARC-MAGIC-007).
AREA_TEST_CODE = ((0x40DA20, 0x40DA4B), (0x45D870, 0x45D8C5), (0x45D8D0, 0x45D8F8), (0x43D510, 0x43D525), (0x4B96F0, 0x4B9760))
CURRENT_HP = 0x43D600           # current hit points: (handle low, handle high) -> value
PC_LEADER = 0x45DDA0            # the PC at the top of an NPC's leader chain: (handle low, handle high) -> handle
TILE_BLOCKED = 0x4D7110         # a tile is blocked: (tile low, tile high, flag) -> 0 or 1
OBJECTS_AT = 0x4407C0           # objects on a tile: (tile low, tile high, type mask, list struct*); the list head at +0x50
LIST_FREE = 0x4410E0            # frees such a list struct
FIND_PATH = 0x41F3C0            # path search over the request at 0x603B88 -> steps (0: none)
SECTOR_OF = 0x4D70B0            # the sector of a location: (low, high) -> sector
SECTOR_KIND = 0x5038C0          # a sector's indoor/outdoor kind: (sector) -> kind
MULTIPLAYER = 0x62AC6C          # bit 0: a multiplayer game (0 in the world model)
FAILED_BIT = 0x603D40           # the bit of the last "damaged" failure (written, 64-bit)
PATH_GLOBALS = ((0x603B88, 0x603BAC), (0x603C98, 0x603CB8))
FIELD_HP_DAMAGE, FIELD_MATERIAL, FIELD_WEIGHT, FIELD_PORTAL_FLAGS, FIELD_CONTAINER_FLAGS = 0x1D, 0x1E, 0x59, 0x2E, 0x38
STAT_ALIGNMENT, STAT_POISON = 0x13, 0x18
AREA_SELF, AREA_SOURCE, AREA_OTHER, AREA_PC_A, AREA_PC_B = ((0x5EED0011 + i, 0x42) for i in range(5))
AREA_CANDIDATE = (0x5EED0010, 0x42)
AREA_TILE_OBJECTS = tuple((0x5EED0020 + i, 0x42) for i in range(3))
AREA_CONTEXT, AREA_MASK, AREA_NODES = 0x1000, 0x1100, 0x1200
TARGET_TABLE, TARGET_NAMES = 0x596140, 65   # the target names' 64-bit values (read from the image)

# Service functions and tables used by the target collection (ARC-MAGIC-007).
COLLECT_CODE = ((0x4F2D20, 0x4F3F10), (0x4F3F10, 0x4F40B0), (0x4F2600, 0x4F2680), (0x4F25E0, 0x4F2600), (0x4F2C60, 0x4F2CB0),
                (0x58AC70, 0x58ACA4)) + AREA_TEST_CODE
WALL_TABLE = (0x4F4DA8, 0x4F4DC8)   # the wall branch's jump table by direction
OBJECTS_IN_RECT = 0x440B40      # objects in a rectangle: (rect*, type mask, list struct*); the list head at +0x50
PARTY_LIST = 0x4413E0           # a critter's party and followers: (handle low, handle high, list struct*)
DIRECTION = 0x4B8D50            # the direction from one location to another: (low, high, low, high) -> 0..7
ART_ROTATION = 0x502880         # the cone's art rotation (its answer is not used)
FIELD64_GETTER_2 = 0x406E80     # 64-bit object field: (handle low, handle high, field) -> EDX:EAX (occupant 0x46, owner 0x58)
NODE_ALLOC = 0x4BAC30           # a run-list node from the pool
OBJECT_REFERENCE = 0x443EB0     # fills an object reference: (handle low, handle high, out*)
FIELD_OCCUPANT, FIELD_OWNER = 0x46, 0x58
WORLD_OBJECTS = 8
WORLD_HANDLES = tuple((0x5EED0030 + i, 0x42) for i in range(WORLD_OBJECTS))
COLLECT_CONTEXT, COLLECT_MASK, COLLECT_HEADS, COLLECT_LISTS, COLLECT_NODES, COLLECT_OUT = 0x1000, 0x1100, 0x1180, 0x1200, 0x3000, 0x8000

# Globals and service functions used by the re-aim and the AI notice (ARC-MAGIC-007).
COMPONENT_POINTER = 0x5E761C    # the component being applied
CURRENT_TARGET, CURRENT_TILE, CURRENT_TYPE, LAST_SUMMONED = 0x5E6D48, 0x5E6D50, 0x5E75AC, 0x5E6D70
LAST_NOTIFIED = 0x5E75E0        # the target the AI was last told of
AI_ATTACKED = 0x4A9650          # tell a target's AI it was attacked: (attacker low, high, victim low, high, 1, 0)
COMPONENT_APPLY = 0x20
RUN_SOURCE, RUN_TARGET_OBJECT, RUN_TARGET_TILE, RUN_OTHER = 0x10, 0xA0, 0xE0, 0xE8


@dataclass(frozen=True)
class FunctionSpec:
    address: int
    name: str                          # Steamwoven's own name
    code: tuple[tuple[int, int], ...]  # allowed code ranges, [start, end), the function's own first
    model: str                         # "experience", "bonus", "skill-level", ..., "spell-range", "spell-flags" or "item-gate"

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
    FunctionSpec(0x4F5090, "fate-armed", ((0x4F5090, 0x4F50C0),), "fate"),
    FunctionSpec(0x4F50C0, "fate-arm", ((0x4F50C0, 0x4F51B0), FATE_IMMEDIATE_CODE), "fate"),
    FunctionSpec(0x4F51B0, "fate-undo", ((0x4F51B0, 0x4F5270),), "fate"),
    FunctionSpec(0x4F5270, "fate-use", ((0x4F5270, 0x4F52D0),), "fate"),
    FunctionSpec(0x4507D0, "spell-run-cost", ((0x4507D0, 0x4508A0),), "spell-cost"),
    FunctionSpec(0x4508A0, "spell-check-cost", ((0x4508A0, 0x450940),), "spell-cost"),
    FunctionSpec(0x4B1660, "spell-cast-cost", ((0x4B1660, 0x4B16C0), COST_GETTER_CODE), "spell-cost"),
    FunctionSpec(0x4B1790, "spell-learn", SPELL_LEARN_CODE, "spell-learn"),
    FunctionSpec(0x4537B0, "spell-resist", ((0x4537B0, 0x453B20),), "spell-resist"),
    FunctionSpec(0x453B20, "spell-fail-chance", ((0x453B20, 0x453CC0),), "spell-fail-chance"),
    FunctionSpec(0x454700, "spell-range", ((0x454700, 0x454790),) + RANGE_CODE, "spell-range"),
    FunctionSpec(0x456430, "spell-flags", ((0x456430, 0x4564E0),), "spell-flags"),
    FunctionSpec(0x456A10, "item-gate", ((0x456A10, 0x456A90),), "item-gate"),
    FunctionSpec(0x4F2D20, "spell-area-test", ((0x4F2D20, 0x4F3F10),) + AREA_TEST_CODE, "spell-area-test"),
    FunctionSpec(0x4F40B0, "spell-target-collection", ((0x4F40B0, 0x4F4DA8),) + COLLECT_CODE, "spell-target-collection"),
    FunctionSpec(0x453D40, "spell-reaim", ((0x453D40, 0x453EE0),), "spell-reaim"),
    FunctionSpec(0x453EE0, "spell-ai-notice", ((0x453EE0, 0x453FA0),), "spell-ai-notice"),
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


@dataclass
class FateCase:
    """The fate functions' one PC: the kind asked (0..11), its fate points (stat 20) and its armed fate flags (field 0xFE)."""
    kind: int
    points: int
    armed: int


def draw_fate_case(rng: random.Random) -> FateCase:
    """A kind 0..11 (the full heal more often), points mostly 0..3 with the edges 0, 1, 99 and 100, armed flags with the asked
    kind's bit half of the time among any other of the eleven bits."""
    kind = 0 if rng.random() < 0.15 else rng.randrange(FATE_KINDS)
    points = rng.choice((0, 1, 99, 100)) if rng.random() < 0.4 else rng.randint(0, 100) if rng.random() < 0.3 else rng.randint(0, 3)
    armed = rng.getrandbits(11)
    if kind > 0:
        bit = 1 << (kind - 1)
        armed = armed | bit if rng.random() < 0.5 else armed & ~bit
    return FateCase(kind, points, armed)


@dataclass
class FateRecorder:
    """What a fate run wrote: the field writes (field, value), the stat writes (stat, value), the hit-point and fatigue
    damage writes."""
    fields: list[tuple[int, int]]
    stats: list[tuple[int, int]]
    hit_points: list[int]
    fatigue: list[int]


def fate_stubs(case: FateCase, recorder: FateRecorder) -> dict:
    """Stubs answering from one PC: its fate flags, fate points and single-player authority; every write is recorded. Any other
    object, field or stat is rejected."""
    from oracle_emu import StubRejected, to_signed

    def same(argument) -> None:
        if (argument(0), argument(1)) != HANDLE:
            raise StubRejected(f"handle {argument(0):#x}:{argument(1):#x} is not the world model's object")

    def field(argument) -> int:
        same(argument)
        if argument(2) != FIELD_FATE_FLAGS:
            raise StubRejected(f"field {argument(2):#x} is not in the world model")
        return case.armed

    def set_field(argument) -> int:
        same(argument)
        if argument(2) != FIELD_FATE_FLAGS:
            raise StubRejected(f"field write {argument(2):#x} is not in the world model")
        recorder.fields.append((argument(2), argument(3)))
        return 0

    def stat(argument) -> int:
        same(argument)
        if argument(2) != STAT_FATE_POINTS:
            raise StubRejected(f"stat {argument(2)} is not in the world model")
        return case.points

    def set_stat(argument) -> int:
        same(argument)
        if argument(2) not in (STAT_FATE_POINTS, STAT_POISON):
            raise StubRejected(f"stat write {argument(2)} is not in the world model")
        recorder.stats.append((argument(2), to_signed(argument(3))))
        return 0

    def damage(target: list[int]):
        def answer(argument) -> int:
            same(argument)
            target.append(to_signed(argument(2)))
            return 0
        return answer

    return {FIELD_GETTER: field, FIELD_SETTER: set_field, STAT_BASE_GETTER: stat, STAT_SETTER: set_stat, NETWORK_GATE: lambda argument: 1,
            HP_DAMAGE_SET: damage(recorder.hit_points), FATIGUE_DAMAGE_SET: damage(recorder.fatigue)}

@dataclass
class ResistCase:
    """The resistance's one run, record and target: the spell, the action (0 BEGIN), the unresistable run flag, the
    fate-maximised flag, the record's technology, damaging and aggressive flags, maintenance period and Resist: pair (stat −1
    for none), the target's object type, whether it is the run's parent, its "no disintegrate" flag, stored magic resistance,
    aptitude, Resist: stat value and whether that stat is extraordinary, whether an armed fate fires, and the pre-drawn d100 and
    d20."""
    spell: int
    action: int
    unresistable: int
    fate_maximized: int
    technology: int
    damaging: int
    aggressive: int
    period: int
    resist_stat: int
    modifier: int
    kind: int
    caster: int
    no_disintegrate: int
    resistance: int
    aptitude: int
    stat_value: int
    extraordinary: int
    fate: int
    d100: int
    d20: int


def draw_resist_case(rng: random.Random) -> ResistCase:
    """BEGIN mostly; Disintegrate (34) often; a save of willpower or another primary stat or none; stored resistances 0..95
    mostly (some negative or above 100); aptitudes over the whole range; stat values around the d20."""
    roll = rng.random()
    resist_stat = -1 if roll < 0.3 else STAT_WILLPOWER if roll < 0.65 else rng.choice((0, 1, 2, 3, 4, 5, 7))
    resistance = rng.randint(0, 95) if rng.random() < 0.85 else rng.choice((-20, -1, 100, 120, 200))
    return ResistCase(spell=34 if rng.random() < 0.15 else rng.randrange(SPELL_RECORDS),
                      action=0 if rng.random() < 0.85 else rng.choice((1, 2)), unresistable=int(rng.random() < 0.05),
                      fate_maximized=int(rng.random() < 0.2), technology=int(rng.random() < 0.05), damaging=int(rng.random() < 0.4),
                      aggressive=int(rng.random() < 0.6), period=rng.choice((0, 0, 0, 1, 5, -1)), resist_stat=resist_stat,
                      modifier=rng.randint(-6, 6), kind=rng.choice((15, 16, 16)) if rng.random() < 0.9 else rng.choice((0, 5, 9)),
                      caster=int(rng.random() < 0.1), no_disintegrate=int(rng.random() < 0.3), resistance=resistance,
                      aptitude=rng.randint(-100, 100), stat_value=rng.randint(1, 25), extraordinary=int(rng.random() < 0.15),
                      fate=int(rng.random() < 0.3), d100=rng.randint(1, 100), d20=rng.randint(1, 20))


@dataclass
class ResistRecorder:
    """The service calls of one run: the random calls (min, max), the fate uses (kind) and the AI notifications."""
    rolls: list[tuple[int, int]]
    fates: list[int]
    notified: int = 0


def resist_stubs(case: ResistCase, recorder: ResistRecorder) -> dict:
    """Stubs answering from one target (and, for the AI notification, its parent): object type, critter flags 2, stored magic
    resistance, aptitude, the Resist: stat, the extraordinary test, the fate use and the random range; anything else is
    rejected. The predictor reads the same facts without the fate use or the notification."""
    from oracle_emu import StubRejected

    def target(argument) -> None:
        if (argument(0), argument(1)) != TARGET:
            raise StubRejected(f"handle {argument(0):#x}:{argument(1):#x} is not the world model's target")

    def field(argument) -> int:
        target(argument)
        if argument(2) == FIELD_TYPE:
            return case.kind
        if argument(2) == FIELD_CRITTER_FLAGS2:
            return NO_DISINTEGRATE if case.no_disintegrate else 0
        raise StubRejected(f"field {argument(2):#x} is not in the world model")

    def resistances(argument) -> int:
        target(argument)
        if (argument(2), argument(3)) != (FIELD_RESISTANCES, MAGIC_RESISTANCE):
            raise StubRejected(f"array {argument(2):#x}[{argument(3)}] is not in the world model")
        return case.resistance & 0xFFFFFFFF

    def stat(argument) -> int:
        target(argument)
        if argument(2) == STAT_APTITUDE:
            return case.aptitude & 0xFFFFFFFF
        if case.resist_stat != -1 and argument(2) == case.resist_stat:
            return case.stat_value
        raise StubRejected(f"stat {argument(2)} is not in the world model")

    def extraordinary(argument) -> int:
        target(argument)
        if argument(2) != case.resist_stat:
            raise StubRejected(f"extraordinary stat {argument(2)} is not the Resist: stat")
        return case.extraordinary

    def fate(argument) -> int:
        target(argument)
        recorder.fates.append(argument(2))
        return case.fate

    def rolled(argument) -> int:
        bounds = (argument(0), argument(1))
        recorder.rolls.append(bounds)
        if bounds == (1, 100):
            return case.d100
        if bounds == (1, 20):
            return case.d20
        raise StubRejected(f"random range {bounds} is not in the world model")

    def notify(argument) -> int:
        if (argument(0), argument(1)) != (TARGET if case.caster else HANDLE) or (argument(2), argument(3)) != TARGET:
            raise StubRejected("notification of other objects")
        recorder.notified += 1
        return 0

    return {FIELD_GETTER: field, ARRAY_GETTER: resistances, STAT_GETTER: stat, EXTRAORDINARY: extraordinary, FATE_USE: fate,
            RANDOM: rolled, NOTIFY_AI: notify}


@dataclass
class RangeCase:
    """The range's parent location (origin), target tile and optional target object (its object flags and location), the
    spell, its record's Range: and whether the spell table is loaded. Coordinates are unsigned 32-bit; (0, 0) is none."""
    origin: tuple[int, int]
    tile: tuple[int, int]
    target: int
    flags: int
    location: tuple[int, int]
    spell: int
    range: int
    loaded: int


def _coordinate(rng: random.Random) -> int:
    return rng.randint(0, 200_000) if rng.random() < 0.9 else rng.choice((0, 1, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFE, 0xFFFFFFFF))


def _near(rng: random.Random, origin: tuple[int, int], reach: int) -> tuple[int, int]:
    """A location around `origin`, often exactly `reach` away on one axis (the edge of the range)."""
    if rng.random() < 0.15:
        return (_coordinate(rng), _coordinate(rng))
    dx, dy = rng.randint(-reach - 3, reach + 3), rng.randint(-reach - 3, reach + 3)
    if rng.random() < 0.3:
        dx = rng.choice((-1, 1)) * (reach + rng.choice((-1, 0, 1)))
    return ((origin[0] + dx) & 0xFFFFFFFF, (origin[1] + dy) & 0xFFFFFFFF)


def draw_range_case(rng: random.Random) -> RangeCase:
    """Ranges mostly 0..30 (some negative or extreme), the table loaded mostly; locations around the parent's, on the edge of the
    range often, sometimes far or at the 32-bit edges; no origin, no tile or no object location sometimes; a target object half
    the time, in an inventory a third of those."""
    reach = rng.randint(0, 30) if rng.random() < 0.85 else rng.choice((-1, -5, -0x80000000, 0x7FFFFFFF, 1000))
    origin = (0, 0) if rng.random() < 0.08 else (_coordinate(rng), _coordinate(rng))
    near = max(0, min(reach, 40))
    tile = (0, 0) if rng.random() < 0.15 else _near(rng, origin, near)
    target = int(rng.random() < 0.5)
    flags = (IN_INVENTORY if rng.random() < 0.3 else 0) | rng.choice((0, 0, 0x20, 0x2000, 0x10000))
    location = (0, 0) if rng.random() < 0.1 else _near(rng, origin, near)
    return RangeCase(origin=origin, tile=tile, target=target, flags=flags, location=location, spell=rng.randrange(SPELL_RECORDS),
                     range=reach, loaded=int(rng.random() < 0.9))


def range_stubs(case: RangeCase) -> dict:
    """Stubs answering from the target object: its object flags and its location; anything else is rejected."""
    from oracle_emu import StubRejected

    def target(argument) -> None:
        if (argument(0), argument(1)) != TARGET:
            raise StubRejected(f"handle {argument(0):#x}:{argument(1):#x} is not the world model's target")

    def field(argument) -> int:
        target(argument)
        if argument(2) != FIELD_OBJECT_FLAGS:
            raise StubRejected(f"field {argument(2):#x} is not in the world model")
        return case.flags

    def field64(argument) -> tuple[int, int]:
        target(argument)
        if argument(2) != FIELD_LOCATION:
            raise StubRejected(f"64-bit field {argument(2):#x} is not in the world model")
        return case.location

    return {FIELD_GETTER: field, FIELD64_GETTER: field64}


@dataclass
class FlagsCase:
    """The forbidden flags' caster (optional) and target (optional, its object type), their spell flags, the target's critter
    flags and the record's three forbidden masks."""
    caster: int
    target: int
    caster_flags: int
    target_flags: int
    kind: int
    critter_flags: int
    masks: tuple[int, int, int]


def _flags(rng: random.Random) -> int:
    """Few bits from a small pool (so a mask meets a flag often), the sign bit sometimes."""
    pool = (0x1, 0x8, 0x200, 0x1000, 0x400000, 0x80000000)
    return sum(bit for bit in pool if rng.random() < 0.2)


def draw_flags_case(rng: random.Random) -> FlagsCase:
    return FlagsCase(caster=int(rng.random() < 0.9), target=int(rng.random() < 0.8), caster_flags=_flags(rng), target_flags=_flags(rng),
                     kind=rng.choice((15, 16, 16)) if rng.random() < 0.7 else rng.randint(0, 17), critter_flags=_flags(rng),
                     masks=(_flags(rng), _flags(rng), _flags(rng)))


def flags_stubs(case: FlagsCase) -> dict:
    """Stubs answering the caster's spell flags and the target's spell flags, object type and critter flags."""
    from oracle_emu import StubRejected

    def field(argument) -> int:
        handle, number = (argument(0), argument(1)), argument(2)
        if handle == HANDLE and number == FIELD_SPELL_FLAGS:
            return case.caster_flags
        if handle == TARGET and number in (FIELD_SPELL_FLAGS, FIELD_TYPE, FIELD_CRITTER_FLAGS):
            return {FIELD_SPELL_FLAGS: case.target_flags, FIELD_TYPE: case.kind, FIELD_CRITTER_FLAGS: case.critter_flags}[number]
        raise StubRejected(f"field {number:#x} of {handle[0]:#x}:{handle[1]:#x} is not in the world model")

    return {FIELD_GETTER: field}


@dataclass
class GateCase:
    """The item gate's holder (optional) and its aptitude, the other object (optional) and its aptitude, the item's magic-tech
    complexity and the power the holder draws from it."""
    holder: int
    aptitude: int
    other: int
    other_aptitude: int
    complexity: int
    power: int


def draw_gate_case(rng: random.Random) -> GateCase:
    return GateCase(holder=int(rng.random() < 0.9), aptitude=rng.randint(-100, 100), other=int(rng.random() < 0.5),
                    other_aptitude=rng.randint(-100, 100), complexity=rng.choice((0, 0, -5)) if rng.random() < 0.3 else rng.randint(1, 100),
                    power=rng.choice((0, 0, -1, -30)) if rng.random() < 0.4 else rng.randint(1, 100))


@dataclass
class GateRecorder:
    """What one gate run asked: the power calls and the aptitude reads of the other object."""
    powers: int = 0
    other_reads: int = 0


def gate_stubs(case: GateCase, recorder: GateRecorder) -> dict:
    """Stubs answering the item's complexity, the holder's and the other object's aptitude and the holder's power over the item."""
    from oracle_emu import StubRejected

    def field(argument) -> int:
        if (argument(0), argument(1)) != ITEM or argument(2) != FIELD_COMPLEXITY:
            raise StubRejected(f"field {argument(2):#x} of {argument(0):#x}:{argument(1):#x} is not in the world model")
        return case.complexity & 0xFFFFFFFF

    def stat(argument) -> int:
        handle = (argument(0), argument(1))
        if argument(2) != STAT_APTITUDE or handle not in (HANDLE, OTHER_PC):
            raise StubRejected(f"stat {argument(2)} of {handle[0]:#x}:{handle[1]:#x} is not in the world model")
        if handle == OTHER_PC:
            recorder.other_reads += 1
            return case.other_aptitude & 0xFFFFFFFF
        return case.aptitude & 0xFFFFFFFF

    def power(argument) -> int:
        if (argument(0), argument(1)) != ITEM or (argument(2), argument(3)) != HANDLE:
            raise StubRejected("power of other objects")
        recorder.powers += 1
        return case.power & 0xFFFFFFFF

    return {FIELD_GETTER: field, STAT_GETTER: stat, ITEM_POWER: power}


@dataclass
class AreaTestCase:
    """The area test's one context: the mask (low, high), the required and forbidden spell flags and the radius; the candidate
    (0 none, 1 its own object, 2 the self, 3 the source, 4 the other object); whether self, source and the other object exist; the candidate's
    facts (type, object flags, spell flags, critter flags and flags 2, hit points, hit-point damage, alignment, poison,
    material, weight, portal and container flags, PC leader code); the self's and the other object's PC leader codes (0 none,
    1 self, 2 other, 3 and 4 two PCs, 5 the candidate); the candidate's, the source's, the origin's, the candidate tile's and
    the target tile's locations (0 none); the objects on the candidate tile (type, object flags); the tile's blocked flag, the
    path answer and the origin's and the tile's sector kinds."""
    lo: int
    hi: int
    required: int
    forbidden: int
    radius: int
    candidate: int
    self_present: int
    source_present: int
    other_present: int
    facts: tuple[int, ...]
    leader: int
    self_leader: int
    other_leader: int
    candidate_location: tuple[int, int]
    source_location: tuple[int, int]
    origin: tuple[int, int]
    tile: tuple[int, int]
    target_tile: tuple[int, int]
    tile_objects: tuple[tuple[int, int], ...]
    blocked: int
    path: int
    origin_kind: int
    tile_kind: int


AREA_FACTS = ("type", "objectFlags", "spellFlags", "critterFlags", "critterFlags2", "hitPoints", "damage", "alignment", "poison",
              "material", "weight", "portalFlags", "containerFlags")


def _subset(rng: random.Random, bits: tuple[int, ...], p: float) -> int:
    return sum(bit for bit in bits if rng.random() < p)


def draw_area_test_case(rng: random.Random, values: list[int]) -> AreaTestCase:
    """Masks of one to four target names with sometimes stray bits and the not-invulnerable bit; candidates of every type
    with the flags the test reads drawn from small pools (so masks meet them often); identity with self or source sometimes;
    leaders among the self, the other object and two PCs; locations around one point, at the radius' edge often."""
    mask = 0
    for _ in range(rng.randint(1, 4)):
        mask |= rng.choice(values)
    if rng.random() < 0.3:
        mask |= 1 << rng.randrange(64)
    if rng.random() < 0.5:
        mask |= 0x2000 << 32
    if rng.random() < 0.15:
        mask |= (0x4000 << 32) | rng.choice((0x44, 0x40004, 0x80 << 32, 0x100 << 32))   # MG25: summoned with another flags word
    flag_pool = (0x1, 0x1000, 0x4000, 0x400000)
    facts = (rng.choice((0, 1, 2, 3, 5, 8, 14, 15, 16, 16, 17)),
             _subset(rng, (0x1, 0x2, 0x100, 0x400, 0x800, 0x1000, 0x400000), 0.12),
             _subset(rng, flag_pool, 0.25),
             _subset(rng, (0x4, 0x8, 0x80, 0x200, 0x1000, 0x4000, 0x800000, 0x1000000, 0x2000000, 0x20000000), 0.2),
             _subset(rng, (0x800000, 0x2000), 0.2),
             rng.choice((-5, 0, 1, 30)), rng.choice((0, 0, 3)), rng.choice((-50, -1, 0, 1, 50)), rng.choice((0, 0, 4)),
             rng.choice((0, 1, 4, 4, 6)), rng.choice((0, 4, 5, 10)), _subset(rng, (0x1, 0x4, 0x4000), 0.4),
             _subset(rng, (0x1, 0x4, 0x4000), 0.4))
    base = (rng.randint(100, 4000), rng.randint(100, 4000))
    radius = rng.choice((-1, 0, 1, 2, 5))

    def near() -> tuple[int, int]:
        reach = max(radius, 0) + rng.choice((0, 0, 1, 2))
        return (base[0] + rng.randint(-reach, reach), base[1] + rng.randint(-reach, reach))

    def maybe(p: float) -> tuple[int, int]:
        return (0, 0) if rng.random() < p else near()
    return AreaTestCase(lo=mask & 0xFFFFFFFF, hi=mask >> 32, required=_subset(rng, flag_pool, 0.1), forbidden=_subset(rng, flag_pool, 0.1),
                        radius=radius, candidate=rng.choice((0, 1, 1, 1, 1, 1, 2, 3, 4)), self_present=int(rng.random() < 0.85),
                        source_present=int(rng.random() < 0.85), other_present=int(rng.random() < 0.4), facts=facts,
                        leader=rng.choice((0, 0, 1, 2, 3, 4)), self_leader=rng.choice((0, 0, 3, 4, 5)), other_leader=rng.choice((0, 3, 4, 5)),
                        candidate_location=near(), source_location=near(), origin=maybe(0.15), tile=maybe(0.2), target_tile=maybe(0.3),
                        tile_objects=tuple((rng.choice((0, 1, 2, 3, 5, 9, 15, 16, 17)), _subset(rng, (0x400, 0x1), 0.4))
                                           for _ in range(rng.choice((0, 0, 1, 2, 3)))),
                        blocked=int(rng.random() < 0.15), path=rng.choice((0, 1, 7)), origin_kind=rng.randint(0, 1), tile_kind=rng.randint(0, 1))


def area_test_admissible(case: AreaTestCase) -> bool:
    """The original reads through a null handle where the tile-self bit and the tile bit meet a context without a candidate
    (0x406DA0 of handle 0); those inputs are outside the model (Steamwoven answers "no location" there)."""
    return not (case.hi & 0x10000 and case.hi & 0x8000 and area_handles(case)["candidate"] == (0, 0))


def area_handles(case: AreaTestCase) -> dict:
    """Which world-model handle each role has: the candidate, self, source, other (or null)."""
    self_h = AREA_SELF if case.self_present else (0, 0)
    source_h = AREA_SOURCE if case.source_present else (0, 0)
    other_h = AREA_OTHER if case.other_present else (0, 0)
    candidate = {0: (0, 0), 1: AREA_CANDIDATE, 2: self_h, 3: source_h, 4: other_h}[case.candidate]
    return {"candidate": candidate, "self": self_h, "source": source_h, "other": other_h}


def area_test_stubs(case: AreaTestCase, emulator, nodes: int) -> dict:
    """Stubs answering from the case: every object's fields and stats (the candidate's facts for the candidate's handle, the
    other roles' locations and leaders), hit points, the PC leader, the tile's objects (a list written into the caller's
    struct), the blocked flag, the path and the sector kinds; anything else is rejected."""
    from oracle_emu import StubRejected
    roles = area_handles(case)
    leaders = {0: (0, 0), 1: roles["self"], 2: roles["other"], 3: AREA_PC_A, 4: AREA_PC_B, 5: roles["candidate"]}
    tile_objects = dict(zip(AREA_TILE_OBJECTS, case.tile_objects))
    facts = dict(zip(AREA_FACTS, case.facts))

    def role_of(handle: tuple[int, int]) -> str:
        if handle == (0, 0):
            raise StubRejected("a null handle")
        if handle == roles["candidate"]:
            return "candidate"
        for name in ("self", "source", "other"):
            if handle == roles[name]:
                return name
        if handle in tile_objects:
            return "tile"
        raise StubRejected(f"handle {handle[0]:#x}:{handle[1]:#x} is not in the world model")

    def field(argument) -> int:
        handle, number = (argument(0), argument(1)), argument(2)
        role = role_of(handle)
        if role == "tile":
            kind, flags = tile_objects[handle]
            if number == FIELD_TYPE:
                return kind
            if number == FIELD_OBJECT_FLAGS:
                return flags
        elif role == "candidate":
            table = {FIELD_TYPE: facts["type"], FIELD_OBJECT_FLAGS: facts["objectFlags"], FIELD_SPELL_FLAGS: facts["spellFlags"],
                     FIELD_CRITTER_FLAGS: facts["critterFlags"], FIELD_CRITTER_FLAGS2: facts["critterFlags2"], FIELD_HP_DAMAGE: facts["damage"],
                     FIELD_MATERIAL: facts["material"], FIELD_WEIGHT: facts["weight"], FIELD_PORTAL_FLAGS: facts["portalFlags"],
                     FIELD_CONTAINER_FLAGS: facts["containerFlags"]}
            if number in table:
                return table[number] & 0xFFFFFFFF
        raise StubRejected(f"field {number:#x} of the {role} is not in the world model")

    def location(argument) -> tuple[int, int]:
        role = role_of((argument(0), argument(1)))
        if argument(2) != FIELD_LOCATION or role not in ("candidate", "source"):
            raise StubRejected(f"64-bit field {argument(2):#x} of the {role}")
        return case.candidate_location if role == "candidate" else case.source_location

    def stat(argument) -> int:
        if role_of((argument(0), argument(1))) != "candidate" or argument(2) not in (STAT_ALIGNMENT, STAT_POISON):
            raise StubRejected(f"stat {argument(2)}")
        return (facts["alignment"] if argument(2) == STAT_ALIGNMENT else facts["poison"]) & 0xFFFFFFFF

    def hit_points(argument) -> int:
        if role_of((argument(0), argument(1))) != "candidate":
            raise StubRejected("hit points of another object")
        return facts["hitPoints"] & 0xFFFFFFFF

    def leader(argument) -> tuple[int, int]:
        role = role_of((argument(0), argument(1)))
        code = {"candidate": case.leader, "self": case.self_leader, "other": case.other_leader}.get(role)
        if code is None:
            raise StubRejected(f"leader of the {role}")
        if role == "candidate" and roles["candidate"] == roles["self"]:
            code = case.leader
        return leaders[code]

    def tile_query(tile: tuple[int, int]) -> None:
        if tile != case.tile:
            raise StubRejected(f"tile {tile} is not the candidate tile")

    def blocked(argument) -> int:
        tile_query((argument(0), argument(1)))
        if argument(2) != 0:
            raise StubRejected("blocked flag other than 0")
        return case.blocked

    def objects_at(argument) -> int:
        tile_query((argument(0), argument(1)))
        mask, struct_address = argument(2), argument(3)
        head = 0
        for index in reversed(range(len(case.tile_objects))):
            kind, _ = case.tile_objects[index]
            if not mask & (1 << kind):
                continue
            node = nodes + 0x10 * index
            emulator.poke(node, struct.pack("<III", *AREA_TILE_OBJECTS[index], head))
            head = node
        emulator.poke(struct_address + 0x50, struct.pack("<I", head))
        return 0

    def path(argument) -> int:
        return case.path

    def sector(argument) -> int:
        point = (argument(0), argument(1))
        if point == case.origin:
            return 1
        if point == case.tile:
            return 2
        raise StubRejected(f"sector of {point}")

    def kind(argument) -> int:
        if argument(0) not in (1, 2):
            raise StubRejected("sector kind of another sector")
        return case.origin_kind if argument(0) == 1 else case.tile_kind

    return {FIELD_GETTER: field, FIELD64_GETTER: location, STAT_GETTER: stat, CURRENT_HP: hit_points, PC_LEADER: leader,
            TILE_BLOCKED: blocked, OBJECTS_AT: objects_at, LIST_FREE: lambda argument: 0, FIND_PATH: path, SECTOR_OF: sector,
            SECTOR_KIND: kind}


@dataclass
class CollectCase:
    """The collection's one context over a world of eight objects: the area (mask low and high, required and forbidden spell
    flags, radius, count); the roles as world indices (−1 none): self, source, target object, other object, last summoned,
    candidate; the origin and target tile (0 none); the run list (−1 no list, else the stored objects, empty: not built yet),
    the summoned list and the source's party (world indices); the direction the wall asks; per object its facts (as the area
    test's), PC leader code (−1 none, else a world index or 8/9 for two PCs outside it), occupant and owner (world indices or
    −1) and location."""
    lo: int
    hi: int
    required: int
    forbidden: int
    radius: int
    count: int
    roles: tuple[int, ...]
    origin: tuple[int, int]
    target_tile: tuple[int, int]
    run_list: tuple[int, ...] | None
    summoned_list: tuple[int, ...] | None
    party: tuple[int, ...]
    direction: int
    objects: tuple[tuple[int, ...], ...]


COLLECT_ROLES = ("self", "source", "target", "other", "summoned", "candidate")
COLLECT_OBJECT = (*AREA_FACTS, "leader", "occupant", "owner", "x", "y")
COLLECT_GATHER_NAMES = (1, 2, 3, 5, 47, 48, 49, 54, 55, 56, 58, 59, 62, 64)   # self, source, object, radius, summoned, tile …


def draw_collect_case(rng: random.Random, values: list[int]) -> CollectCase:
    """Masks of one or two gather names and up to three test names; radius 0..3; count −1, 0, 1, 2 or 5; eight objects of
    every type around one point (critters often, scenery with an occupant, items with an owner); roles and lists from them."""
    mask = 0
    for _ in range(rng.randint(1, 2)):
        mask |= values[rng.choice(COLLECT_GATHER_NAMES)]
    for _ in range(rng.randint(0, 3)):
        mask |= rng.choice(values)
    if rng.random() < 0.5:
        mask |= 0x2000 << 32
    if rng.random() < 0.1:
        mask = (mask | (0x2000000 << 32) | 0x11) & ~0x4   # the cone with the radius bit and no object bit
    if rng.random() < 0.1:
        mask |= (0x4000 << 32) | rng.choice((0x44, 0x40004, 0x80 << 32)) | 0x10   # MG25 through the radius gather's tests
    base = (rng.randint(100, 4000), rng.randint(100, 4000))

    def near(reach: int = 3) -> tuple[int, int]:
        return (base[0] + rng.randint(-reach, reach), base[1] + rng.randint(-reach, reach))
    objects = []
    for index in range(WORLD_OBJECTS):
        kind = rng.choice((0, 1, 2, 3, 3, 5, 8, 15, 16, 16, 16, 17))
        facts = (kind, _subset(rng, (0x1, 0x2, 0x100, 0x400, 0x1000, 0x400000), 0.08), _subset(rng, (0x1, 0x1000, 0x4000), 0.2),
                 _subset(rng, (0x4, 0x8, 0x1000, 0x4000), 0.15), _subset(rng, (0x800000,), 0.2), rng.choice((-1, 0, 5, 20)),
                 rng.choice((0, 0, 2)), rng.choice((-10, 0, 10)), rng.choice((0, 3)), rng.choice((0, 4)), rng.choice((1, 6)),
                 _subset(rng, (0x1, 0x4), 0.3), _subset(rng, (0x1, 0x4), 0.3))
        leader = rng.choice((-1, -1, -1, 8, 9, rng.randrange(WORLD_OBJECTS)))
        occupant = rng.randrange(WORLD_OBJECTS) if kind == 3 and rng.random() < 0.5 else -1
        owner = rng.randrange(WORLD_OBJECTS) if 5 <= kind <= 14 and rng.random() < 0.6 else -1
        objects.append((*facts, leader, occupant, owner, *near()))

    def role(p: float) -> int:
        return rng.randrange(WORLD_OBJECTS) if rng.random() < p else -1
    roles = (role(0.85), role(0.85), role(0.6), role(0.3), role(0.3), role(0.3))
    run_list = None if rng.random() < 0.3 else tuple(rng.randrange(WORLD_OBJECTS) for _ in range(rng.choice((0, 0, 1, 3))))
    summoned = None if rng.random() < 0.4 else tuple(rng.randrange(WORLD_OBJECTS) for _ in range(rng.choice((0, 1, 2))))
    return CollectCase(lo=mask & 0xFFFFFFFF, hi=mask >> 32, required=_subset(rng, (0x1, 0x1000), 0.1), forbidden=_subset(rng, (0x4000,), 0.1),
                       radius=rng.randint(0, 3), count=rng.choice((-1, -1, 0, 1, 2, 5)), roles=roles, origin=(0, 0) if rng.random() < 0.2 else near(),
                       target_tile=(0, 0) if rng.random() < 0.3 else near(), run_list=run_list, summoned_list=summoned,
                       party=tuple(rng.randrange(WORLD_OBJECTS) for _ in range(rng.choice((0, 1, 3)))), direction=rng.choice((0, 1, 2, 3, 4, 5, 6, 7, 9)),
                       objects=tuple(objects))


def collect_tile_facts(tile: tuple[int, int]) -> tuple[int, int, int]:
    """The world model's tile answers, from the tile alone: blocked, a path from the source, the sector kind."""
    x, y = tile
    return int((x * 7 + y * 3) % 5 == 0), int((x + y) % 3 != 0), (x ^ y) & 1


def collect_stubs(case: CollectCase, emulator, pool: list[int]) -> dict:
    """Stubs answering from the world of eight objects: fields, stats, hit points, leaders, locations, occupants and owners;
    the rectangle, tile and party queries (lists written into the caller's struct, in world order); the tile answers; the
    direction; run-list nodes from a pool. A null handle or anything else outside the model is rejected."""
    from oracle_emu import StubRejected
    index_of = {handle: i for i, handle in enumerate(WORLD_HANDLES)}
    pcs = {8: (0x5EED0040, 0x42), 9: (0x5EED0041, 0x42)}

    def obj(argument, first: int = 0) -> tuple[int, ...]:
        handle = (argument(first), argument(first + 1))
        if handle not in index_of:
            raise StubRejected("a null handle" if handle == (0, 0) else f"handle {handle[0]:#x}:{handle[1]:#x} is not in the world model")
        return case.objects[index_of[handle]]

    def fact(o: tuple[int, ...], name: str) -> int:
        return o[COLLECT_OBJECT.index(name)]

    def field(argument) -> int:
        o = obj(argument)
        table = {FIELD_TYPE: "type", FIELD_OBJECT_FLAGS: "objectFlags", FIELD_SPELL_FLAGS: "spellFlags", FIELD_CRITTER_FLAGS: "critterFlags",
                 FIELD_CRITTER_FLAGS2: "critterFlags2", FIELD_HP_DAMAGE: "damage", FIELD_MATERIAL: "material", FIELD_WEIGHT: "weight",
                 FIELD_PORTAL_FLAGS: "portalFlags", FIELD_CONTAINER_FLAGS: "containerFlags"}
        if argument(2) == 1:
            return 0      # the art number, read only for the cone's unused rotation
        if argument(2) not in table:
            raise StubRejected(f"field {argument(2):#x}")
        return fact(o, table[argument(2)]) & 0xFFFFFFFF

    def field64(argument) -> tuple[int, int]:
        o = obj(argument)
        if argument(2) == FIELD_LOCATION:
            return (fact(o, "x"), fact(o, "y"))
        raise StubRejected(f"64-bit field {argument(2):#x}")

    def handle_field(argument) -> tuple[int, int]:
        o = obj(argument)
        name = {FIELD_OCCUPANT: "occupant", FIELD_OWNER: "owner"}.get(argument(2))
        if name is None:
            raise StubRejected(f"handle field {argument(2):#x}")
        return WORLD_HANDLES[fact(o, name)] if fact(o, name) >= 0 else (0, 0)

    def stat(argument) -> int:
        o = obj(argument)
        if argument(2) not in (STAT_ALIGNMENT, STAT_POISON, STAT_APTITUDE):
            raise StubRejected(f"stat {argument(2)}")
        return {STAT_ALIGNMENT: fact(o, "alignment"), STAT_POISON: fact(o, "poison"), STAT_APTITUDE: 0}[argument(2)] & 0xFFFFFFFF

    def leader(argument) -> tuple[int, int]:
        code = fact(obj(argument), "leader")
        return (0, 0) if code < 0 else pcs.get(code, WORLD_HANDLES[code] if code < WORLD_OBJECTS else (0, 0))

    def write_list(struct_address: int, indices: list[int]) -> None:
        head = 0
        for i in reversed(indices):
            node = pool[0]
            pool[0] += 0x10
            emulator.poke(node, struct.pack("<III", *WORLD_HANDLES[i], head))
            head = node
        emulator.poke(struct_address + 0x50, struct.pack("<I", head))

    def in_rect(argument) -> int:
        x0, y0, x1, y1 = struct.unpack("<4q", emulator.peek(argument(0), 32))
        write_list(argument(2), [i for i, o in enumerate(case.objects)
                                 if x0 <= fact(o, "x") <= x1 and y0 <= fact(o, "y") <= y1 and argument(1) & (1 << fact(o, "type"))])
        return 0

    def at(argument) -> int:
        tile = (argument(0), argument(1))
        write_list(argument(3), [i for i, o in enumerate(case.objects) if (fact(o, "x"), fact(o, "y")) == tile and argument(2) & (1 << fact(o, "type"))])
        return 0

    def party(argument) -> int:
        obj(argument)
        write_list(argument(2), list(case.party))
        return 0

    def blocked(argument) -> int:
        if argument(2) != 0:
            raise StubRejected("blocked flag other than 0")
        return collect_tile_facts((argument(0), argument(1)))[0]

    def path(argument) -> int:
        tile = struct.unpack("<II", bytes(emulator.mu.mem_read(0x603B98, 8)))
        return collect_tile_facts(tile)[1]

    def sector(argument) -> int:
        return 10 + collect_tile_facts((argument(0), argument(1)))[2]

    def kind(argument) -> int:
        if argument(0) not in (10, 11):
            raise StubRejected("sector kind of another sector")
        return argument(0) - 10

    def node(argument) -> int:
        address = pool[1]
        pool[1] += 0x50
        return address

    return {FIELD_GETTER: field, FIELD64_GETTER: field64, FIELD64_GETTER_2: handle_field, STAT_GETTER: stat,
            CURRENT_HP: lambda argument: fact(obj(argument), "hitPoints") & 0xFFFFFFFF, PC_LEADER: leader, OBJECTS_IN_RECT: in_rect,
            OBJECTS_AT: at, PARTY_LIST: party, LIST_FREE: lambda argument: 0, TILE_BLOCKED: blocked, FIND_PATH: path, SECTOR_OF: sector,
            SECTOR_KIND: kind, DIRECTION: lambda argument: case.direction, ART_ROTATION: lambda argument: 0, NODE_ALLOC: node,
            OBJECT_REFERENCE: lambda argument: 0}


def _digest(values: list[int]) -> int:
    """FNV-1a over a list of 32-bit values, as a signed 32-bit int (the C# test computes the same)."""
    value = 0x811C9DC5
    for item in values:
        for byte in struct.pack("<I", item & 0xFFFFFFFF):
            value = ((value ^ byte) * 0x01000193) & 0xFFFFFFFF
    return value - (1 << 32) if value & 0x80000000 else value


def HEAP_BASE_OF(emulator) -> int:
    return emulator.heap.start


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
    elif spec.model == "fate":
        from oracle_emu import RunFailure
        emulator.set_global(FATE_CALLBACK, 0)
        readable = (Range(*FATE_MASKS), Range(FATE_CALLBACK, FATE_CALLBACK + 4))
        for case_number in range(count):
            case = draw_fate_case(rng)
            recorder = FateRecorder([], [], [], [])
            result = emulator.call(spec.address, (*HANDLE, case.kind), code=code, stubs=fate_stubs(case, recorder), readable=readable)
            if len(recorder.fields) > 1 or len(recorder.hit_points) > 1 or len(recorder.fatigue) > 1 or len(recorder.stats) > 2:
                raise RunFailure("stub", f"unexpected writes {recorder}")
            points = next((value for number, value in recorder.stats if number == STAT_FATE_POINTS), -1)
            poison = next((value for number, value in recorder.stats if number == STAT_POISON), -1)
            flags = recorder.fields[0][1] if recorder.fields else -1
            cases.append([case.kind, case.points, case.armed, result, flags, points, poison,
                          recorder.hit_points[0] if recorder.hit_points else -1, recorder.fatigue[0] if recorder.fatigue else -1])
            if progress and case_number % 1000 == 0:
                progress(case_number)
        document.update(fields=["kind", "points", "armed", "result", "flagsWritten", "pointsWritten", "poisonWritten", "hitPointDamageWritten",
                                "fatigueDamageWritten"])
    elif spec.model == "spell-resist":
        from oracle_emu import RunFailure, to_signed
        run_address = emulator.write_heap(b"\0" * 0x200, 0)
        record_address = emulator.write_heap(b"\0" * 0x40, 0x200)
        resist_address = emulator.write_heap(b"\0" * 8, 0x240)
        for address, value in ((RUN_POINTER, run_address), (RECORD_POINTER, record_address), (RESIST_POINTER, resist_address),
                               (TARGET_HANDLE, TARGET[0]), (TARGET_HANDLE + 4, TARGET[1])):
            emulator.set_global(address, value)
        for case_number in range(count):
            case = draw_resist_case(rng)
            parent = TARGET if case.caster else HANDLE
            flags = (FLAG_TECHNOLOGY if case.technology else 0) | (FLAG_DAMAGING if case.damaging else 0) | (FLAG_AGGRESSIVE if case.aggressive else 0)
            emulator.write_heap(struct.pack("<ii", case.spell, case.action), RUN_SPELL)
            emulator.write_heap(struct.pack("<II", *parent), RUN_PARENT)
            emulator.write_heap(struct.pack("<I", RUN_UNRESISTABLE if case.unresistable else 0), RUN_FLAGS)
            emulator.write_heap(struct.pack("<I", UNTOUCHED), RUN_MARGIN)
            emulator.write_heap(struct.pack("<i", case.period), 0x200 + RECORD_PERIOD)
            emulator.write_heap(struct.pack("<I", flags), 0x200 + RECORD_FLAGS)
            emulator.write_heap(struct.pack("<ii", case.resist_stat, case.modifier), 0x240)
            emulator.set_global(TARGET_TYPE, case.kind)
            emulator.set_global(FATE_MAXIMIZED, case.fate_maximized)
            emulator.set_global(DISPLAY_MARGIN, UNTOUCHED)
            recorder = ResistRecorder([], [])
            readable = tuple(Range(a, a + 4) for a in (RUN_POINTER, RECORD_POINTER, RESIST_POINTER, TARGET_HANDLE, TARGET_HANDLE + 4,
                                                        TARGET_TYPE, FATE_MAXIMIZED, run_address + RUN_SPELL, run_address + RUN_ACTION,
                                                        run_address + RUN_PARENT, run_address + RUN_PARENT + 4, run_address + RUN_FLAGS,
                                                        record_address + RECORD_PERIOD, record_address + RECORD_FLAGS,
                                                        resist_address, resist_address + 4))
            writable = (Range(DISPLAY_MARGIN, DISPLAY_MARGIN + 4), Range(run_address + RUN_MARGIN, run_address + RUN_MARGIN + 4))
            result = emulator.call(spec.address, (), code=code, stubs=resist_stubs(case, recorder), readable=readable, writable=writable)
            if len(recorder.rolls) > 2 or len(recorder.fates) > 1 or recorder.notified > 1 or any(k != FATE_SAVE_AGAINST_MAGICK for k in recorder.fates):
                raise RunFailure("stub", f"unexpected calls {recorder}")
            rolls = "".join("d" if bounds == (1, 100) else "s" for bounds in recorder.rolls)
            if rolls not in ("", "d", "s", "ds"):
                raise RunFailure("stub", f"unexpected roll order {recorder.rolls}")
            cases.append([case.spell, case.action, case.unresistable, case.fate_maximized, case.technology, case.damaging, case.aggressive,
                          case.period, case.resist_stat, case.modifier, case.kind, case.caster, case.no_disintegrate, case.resistance,
                          case.aptitude, case.stat_value, case.extraordinary, case.fate, case.d100, case.d20, result,
                          to_signed(emulator.read_u32(run_address + RUN_MARGIN)), to_signed(emulator.read_u32(DISPLAY_MARGIN)),
                          {"": 0, "d": 1, "s": 2, "ds": 3}[rolls], len(recorder.fates), recorder.notified])
            if progress and case_number % 1000 == 0:
                progress(case_number)
        document.update(fields=["spell", "action", "unresistable", "fateMaximized", "technology", "damaging", "aggressive", "period",
                                "resistStat", "modifier", "type", "caster", "noDisintegrate", "resistance", "aptitude", "statValue",
                                "extraordinary", "fate", "d100", "d20", "result", "margin", "displayMargin", "rolls", "fateUses", "notified"])
    elif spec.model == "spell-fail-chance":
        from oracle_emu import RunFailure
        table = emulator.write_heap(b"\0" * (SPELL_RECORD * SPELL_RECORDS))
        emulator.set_global(SPELL_TABLE_POINTER, table)
        for case_number in range(count):
            case = draw_resist_case(rng)
            caster = TARGET if case.caster else HANDLE
            flags = (FLAG_TECHNOLOGY if case.technology else 0) | (FLAG_DAMAGING if case.damaging else 0) | (FLAG_AGGRESSIVE if case.aggressive else 0)
            record = table + SPELL_RECORD * case.spell
            emulator.write_heap(struct.pack("<ii", case.resist_stat, case.modifier), SPELL_RECORD * case.spell + RECORD_RESIST)
            emulator.write_heap(struct.pack("<i", case.period), SPELL_RECORD * case.spell + RECORD_PERIOD)
            emulator.write_heap(struct.pack("<I", flags), SPELL_RECORD * case.spell + RECORD_FLAGS)
            emulator.set_global(FATE_MAXIMIZED, case.fate_maximized)
            recorder = ResistRecorder([], [])
            stubs = resist_stubs(case, recorder)
            del stubs[FATE_USE], stubs[NOTIFY_AI]
            readable = tuple(Range(a, a + 4) for a in (SPELL_TABLE_POINTER, FATE_MAXIMIZED, record + RECORD_RESIST, record + RECORD_RESIST + 4,
                                                        record + RECORD_PERIOD)) + (Range(record + RECORD_FLAGS, record + RECORD_FLAGS + 4),)
            result = emulator.call(spec.address, (*caster, *TARGET, case.spell), code=code, stubs=stubs, readable=readable)
            emulator.write_heap(b"\0" * 0x38, SPELL_RECORD * case.spell)
            if any(bounds != (1, 20) for bounds in recorder.rolls) or len(recorder.rolls) > 1:
                raise RunFailure("stub", f"unexpected rolls {recorder.rolls}")
            cases.append([case.spell, case.fate_maximized, case.technology, case.damaging, case.period, case.resist_stat, case.modifier,
                          case.kind, case.caster, case.resistance, case.aptitude, case.stat_value, case.extraordinary, case.d20, result,
                          len(recorder.rolls)])
            if progress and case_number % 1000 == 0:
                progress(case_number)
        document.update(fields=["spell", "fateMaximized", "technology", "damaging", "period", "resistStat", "modifier", "type", "caster",
                                "resistance", "aptitude", "statValue", "extraordinary", "d20", "result", "rolls"])
    elif spec.model == "spell-range":
        from oracle_emu import to_signed
        table = emulator.write_heap(b"\0" * (SPELL_RECORD * SPELL_RECORDS))
        emulator.set_global(SPELL_TABLE_POINTER, table)
        for case_number in range(count):
            case = draw_range_case(rng)
            field = SPELL_RECORD * case.spell + RECORD_RANGE
            emulator.write_heap(struct.pack("<i", case.range), field)
            emulator.set_global(SPELLS_LOADED, case.loaded)
            readable = (Range(SPELL_TABLE_POINTER, SPELL_TABLE_POINTER + 4), Range(SPELLS_LOADED, SPELLS_LOADED + 4),
                        Range(table + field, table + field + 4))
            target = TARGET if case.target else (0, 0)
            result = emulator.call(spec.address, (*case.origin, *case.tile, *target, case.spell), code=code, stubs=range_stubs(case),
                                   readable=readable)
            emulator.write_heap(b"\0\0\0\0", field)
            cases.append([*(to_signed(v) for v in (*case.origin, *case.tile)), case.target, case.flags,
                          *(to_signed(v) for v in case.location), case.spell, case.range, case.loaded, result])
            if progress and case_number % 1000 == 0:
                progress(case_number)
        document.update(fields=["originX", "originY", "tileX", "tileY", "target", "objectFlags", "locationX", "locationY", "spell", "range",
                                "loaded", "result"])
    elif spec.model == "spell-flags":
        from oracle_emu import to_signed
        record = emulator.write_heap(b"\0" * SPELL_RECORD)
        for case_number in range(count):
            case = draw_flags_case(rng)
            emulator.write_heap(struct.pack("<3I", *case.masks), RECORD_DISALLOWED)
            caster = HANDLE if case.caster else (0, 0)
            target = TARGET if case.target else (0, 0)
            result = emulator.call(spec.address, (*caster, *target, record), code=code, stubs=flags_stubs(case),
                                   readable=(Range(record + RECORD_DISALLOWED, record + RECORD_DISALLOWED + 12),))
            cases.append([case.caster, case.target, to_signed(case.caster_flags), to_signed(case.target_flags), case.kind,
                          to_signed(case.critter_flags), *(to_signed(m) for m in case.masks), result])
            if progress and case_number % 1000 == 0:
                progress(case_number)
        document.update(fields=["caster", "target", "casterFlags", "targetFlags", "type", "critterFlags", "disallowedCaster",
                                "disallowedTarget", "disallowedCritter", "result"])
    elif spec.model == "item-gate":
        from oracle_emu import RunFailure
        for case_number in range(count):
            case = draw_gate_case(rng)
            recorder = GateRecorder()
            holder = HANDLE if case.holder else (0, 0)
            other = OTHER_PC if case.other else (0, 0)
            result = emulator.call(spec.address, (*holder, *other, *ITEM), code=code, stubs=gate_stubs(case, recorder))
            if recorder.powers > 1 or recorder.other_reads > 1:
                raise RunFailure("stub", f"unexpected calls {recorder}")
            cases.append([case.holder, case.aptitude, case.other, case.other_aptitude, case.complexity, case.power, result, recorder.powers,
                          recorder.other_reads])
            if progress and case_number % 1000 == 0:
                progress(case_number)
        document.update(fields=["holder", "aptitude", "other", "otherAptitude", "complexity", "power", "result", "powerCalls", "otherReads"])
    elif spec.model == "spell-area-test":
        from oracle_emu import RunFailure, to_signed
        values = [v for v in (emulator.read_u32(TARGET_TABLE + 8 * i) | emulator.read_u32(TARGET_TABLE + 8 * i + 4) << 32
                              for i in range(TARGET_NAMES)) if v]
        context = emulator.write_heap(b"\0" * 0x64, AREA_CONTEXT)
        mask_address = emulator.write_heap(b"\0" * 0x18, AREA_MASK)
        nodes = emulator.write_heap(b"\0" * 0x40, AREA_NODES)
        emulator.set_global(MULTIPLAYER, 0)
        done = 0
        while done < count:
            case = draw_area_test_case(rng, values)
            if not area_test_admissible(case):
                continue
            roles = area_handles(case)
            emulator.write_heap(struct.pack("<IIiii", case.lo, case.hi, case.required, case.forbidden, case.radius) + struct.pack("<i", -1), AREA_MASK)
            fields = struct.pack("<I4x", mask_address) + struct.pack("<II", *roles["self"]) + struct.pack("<II", *roles["source"]) \
                + struct.pack("<II", *case.origin) + struct.pack("<II", *roles["candidate"]) + struct.pack("<II", *case.tile) \
                + struct.pack("<II", 0, 0) + struct.pack("<II", *case.target_tile) + struct.pack("<II", *roles["other"]) + b"\0" * 0x1C
            emulator.write_heap(fields, AREA_CONTEXT)
            emulator.set_global(FAILED_BIT, UNTOUCHED)
            emulator.set_global(FAILED_BIT + 4, UNTOUCHED)
            readable = (Range(context, context + 0x64), Range(mask_address, mask_address + 0x18), Range(nodes, nodes + 0x40),
                        Range(MULTIPLAYER, MULTIPLAYER + 4))
            writable = (Range(FAILED_BIT, FAILED_BIT + 8),) + tuple(Range(start, end) for start, end in PATH_GLOBALS)
            result = emulator.call(spec.address, (context,), code=code, stubs=area_test_stubs(case, emulator, nodes), readable=readable, writable=writable)
            cases.append([to_signed(case.lo), to_signed(case.hi), case.required, case.forbidden,
                          case.radius, case.candidate, case.self_present, case.source_present, case.other_present,
                          *(to_signed(v) for v in case.facts), case.leader, case.self_leader, case.other_leader,
                          *case.candidate_location, *case.source_location, *case.origin, *case.tile, *case.target_tile,
                          len(case.tile_objects), *(v for pair in case.tile_objects + ((0, 0),) * (3 - len(case.tile_objects)) for v in pair),
                          case.blocked, case.path, case.origin_kind, case.tile_kind, result,
                          to_signed(emulator.read_u32(FAILED_BIT)), to_signed(emulator.read_u32(FAILED_BIT + 4))])
            done += 1
            if progress and done % 1000 == 0:
                progress(done)
        document.update(fields=["lo", "hi", "required", "forbidden", "radius", "candidate", "self", "source", "other", *AREA_FACTS, "leader",
                                "selfLeader", "otherLeader", "candidateX", "candidateY", "sourceX", "sourceY", "originX", "originY", "tileX",
                                "tileY", "targetTileX", "targetTileY", "tileObjects", "object0Type", "object0Flags", "object1Type", "object1Flags",
                                "object2Type", "object2Flags", "blocked", "path", "originKind", "tileKind", "result", "failedLow", "failedHigh"])
    elif spec.model == "spell-target-collection":
        from oracle_emu import RunFailure, to_signed
        values = [emulator.read_u32(TARGET_TABLE + 8 * i) | emulator.read_u32(TARGET_TABLE + 8 * i + 4) << 32 for i in range(TARGET_NAMES)]
        context = emulator.write_heap(b"\0" * 0x64, COLLECT_CONTEXT)
        mask_address = emulator.write_heap(b"\0" * 0x18, COLLECT_MASK)
        heads = emulator.write_heap(b"\0" * 8, COLLECT_HEADS)
        out = emulator.write_heap(b"\0" * (8 + 0x38 * 256), COLLECT_OUT)
        emulator.set_global(MULTIPLAYER, 0)
        redrawn = done = 0
        while done < count:
            case = draw_collect_case(rng, [v for v in values])
            def handle(index: int) -> tuple[int, int]:
                return WORLD_HANDLES[index] if index >= 0 else (0, 0)
            self_h, source_h, target_h, other_h, summoned_h, candidate_h = (handle(i) for i in case.roles)
            pool = [COLLECT_LISTS, COLLECT_NODES]
            def stored(indices, base: int, link: int) -> int:
                head = 0
                for k, i in enumerate(reversed(indices)):
                    offset = base + 0x50 * k
                    emulator.write_heap(struct.pack("<II", *WORLD_HANDLES[i]) + b"\0" * (link - 8) + struct.pack("<I", head), offset)
                    head = HEAP_BASE_OF(emulator) + offset
                return head
            run_head = stored(case.run_list or (), COLLECT_NODES + 0x2000, 0x40) if case.run_list is not None else 0
            summoned_head = stored(case.summoned_list or (), COLLECT_NODES + 0x3000, 0x40) if case.summoned_list is not None else 0
            emulator.write_heap(struct.pack("<II", run_head, summoned_head), COLLECT_HEADS)
            emulator.write_heap(struct.pack("<IIIIii", case.lo, case.hi, case.required, case.forbidden, case.radius, case.count), COLLECT_MASK)
            fields = struct.pack("<I4x", mask_address) + struct.pack("<II", *self_h) + struct.pack("<II", *source_h) + struct.pack("<II", *case.origin) \
                + struct.pack("<II", *candidate_h) + b"\0" * 8 + struct.pack("<II", *target_h) + struct.pack("<II", *case.target_tile) \
                + struct.pack("<II", *other_h) + struct.pack("<II", *summoned_h) + struct.pack("<I", out) \
                + struct.pack("<I", heads if case.run_list is not None else 0) + struct.pack("<I", heads + 4 if case.summoned_list is not None else 0) \
                + b"\0" * 8
            emulator.write_heap(fields, COLLECT_CONTEXT)
            emulator.write_heap(b"\0" * 8, COLLECT_OUT)
            heap = HEAP_BASE_OF(emulator)
            readable = (Range(heap, heap + COLLECT_OUT), Range(*WALL_TABLE), Range(MULTIPLAYER, MULTIPLAYER + 4))
            writable = (Range(out, out + 8 + 0x38 * 256), Range(heads, heads + 8), Range(FAILED_BIT, FAILED_BIT + 8),
                        Range(heap + COLLECT_NODES, heap + COLLECT_NODES + 0x2000)) + tuple(Range(start, end) for start, end in PATH_GLOBALS)
            pool[0] += HEAP_BASE_OF(emulator)
            pool[1] += HEAP_BASE_OF(emulator)
            try:
                emulator.call(spec.address, (context,), code=code, stubs=collect_stubs(case, emulator, pool), readable=readable, writable=writable)
            except RunFailure as failure:
                if "a null handle" not in str(failure):
                    raise
                redrawn += 1
                continue
            entries = []
            for k in range(emulator.read_u32(out)):
                lo_h, hi_h, x, y = struct.unpack("<IIII", emulator.peek(out + 8 + 0x38 * k, 16))
                entries.append(WORLD_HANDLES.index((lo_h, hi_h)) if (lo_h, hi_h) != (0, 0) else -1)
                entries.extend((x, y) if (lo_h, hi_h) == (0, 0) else ())
            built = []
            if case.run_list == ():
                node = emulator.read_u32(heads)
                while node:
                    built.append(WORLD_HANDLES.index(struct.unpack("<II", emulator.peek(node, 8))))
                    node = emulator.read_u32(node + 0x40)
            cases.append([to_signed(case.lo), to_signed(case.hi), case.required, case.forbidden, case.radius, case.count, *case.roles, *case.origin,
                          *case.target_tile, -1 if case.run_list is None else len(case.run_list), *(case.run_list or ()) + (-1,) * (3 - len(case.run_list or ())),
                          -1 if case.summoned_list is None else len(case.summoned_list), *(case.summoned_list or ()) + (-1,) * (2 - len(case.summoned_list or ())),
                          len(case.party), *case.party + (-1,) * (3 - len(case.party)), case.direction, *(to_signed(v) for o in case.objects for v in o),
                          emulator.read_u32(out), _digest(entries), len(built), _digest(built)])
            done += 1
            if progress and done % 1000 == 0:
                progress(done)
        object_fields = [f"o{i}{name[0].upper()}{name[1:]}" for i in range(WORLD_OBJECTS) for name in COLLECT_OBJECT]
        document.update(redrawn=redrawn, fields=["lo", "hi", "required", "forbidden", "radius", "count", *COLLECT_ROLES, "originX", "originY",
                                                 "targetTileX", "targetTileY", "runList", "run0", "run1", "run2", "summonedList", "summoned0", "summoned1",
                                                 "party", "party0", "party1", "party2", "direction", *object_fields, "entries", "digest", "built", "builtDigest"])
    elif spec.model in ("spell-reaim", "spell-ai-notice"):
        from oracle_emu import RunFailure, StubRejected, to_signed
        handles = WORLD_HANDLES[:5]   # parent, source, run target, last summoned, other
        run_address = emulator.write_heap(b"\0" * 0x100, 0)
        component = emulator.write_heap(b"\0" * 0x40, 0x100)
        record = emulator.write_heap(b"\0" * 0x40, 0x200)
        for address, value in ((RUN_POINTER, run_address), (COMPONENT_POINTER, component), (RECORD_POINTER, record)):
            emulator.set_global(address, value)
        redrawn = done = 0
        while done < count:
            present = [rng.random() < 0.75 for _ in handles]
            def h(i: int) -> tuple[int, int]:
                return handles[i] if present[i] else (0, 0)
            locations = [(rng.randint(1, 5000), rng.randint(1, 5000)) for _ in handles]
            types = [rng.choice((3, 5, 15, 16)) for _ in handles]
            current = rng.choice((-1, 0, 1, 2, 3, 4))
            tile = (0, 0) if rng.random() < 0.3 else (rng.randint(1, 5000), rng.randint(1, 5000))
            run_tile = (0, 0) if rng.random() < 0.3 else (rng.randint(1, 5000), rng.randint(1, 5000))
            apply = 0 if rng.random() < 0.1 else sum(bit for bit in (0x1, 0x2, 0x4, 0x4000 << 32, 0x8000 << 32, 0x800000 << 32, 0x4800 << 32) if rng.random() < 0.3)
            action = rng.choice((0, 1, 2, 3, 4))
            aggressive = int(rng.random() < 0.6)
            last = rng.choice((-1, 0, 1, 2, 3, 4))
            current_h = handles[current] if current >= 0 and present[current] else (0, 0)
            emulator.write_heap(struct.pack("<II", *h(1)), RUN_SOURCE)
            emulator.write_heap(struct.pack("<ii", 0, action), 0x4)
            emulator.write_heap(struct.pack("<II", *h(0)), RUN_PARENT)
            emulator.write_heap(struct.pack("<II", *h(2)), RUN_TARGET_OBJECT)
            emulator.write_heap(struct.pack("<II", *run_tile), RUN_TARGET_TILE)
            emulator.write_heap(struct.pack("<II", *h(4)), RUN_OTHER)
            emulator.write_heap(struct.pack("<Q", apply), 0x100 + COMPONENT_APPLY)
            emulator.write_heap(struct.pack("<I", 0x2 if aggressive else 0), 0x200 + RECORD_FLAGS)
            for address, value in ((CURRENT_TARGET, current_h[0]), (CURRENT_TARGET + 4, current_h[1]), (CURRENT_TILE, tile[0]), (CURRENT_TILE + 4, tile[1]),
                                   (CURRENT_TYPE, UNTOUCHED), (LAST_SUMMONED, h(3)[0]), (LAST_SUMMONED + 4, h(3)[1]),
                                   (LAST_NOTIFIED, handles[last][0] if last >= 0 else 0), (LAST_NOTIFIED + 4, handles[last][1] if last >= 0 else 0)):
                emulator.set_global(address, value)
            notices: list[tuple[int, int]] = []

            def who(handle: tuple[int, int]) -> int:
                if handle == (0, 0):
                    return -1
                if handle not in handles:
                    raise StubRejected(f"handle {handle[0]:#x}:{handle[1]:#x} is not in the world model")
                return handles.index(handle)

            def field(argument) -> int:
                i = who((argument(0), argument(1)))
                if i < 0:
                    raise StubRejected("a null handle")
                if argument(2) != FIELD_TYPE:
                    raise StubRejected(f"field {argument(2):#x}")
                return types[i]

            def location(argument) -> tuple[int, int]:
                i = who((argument(0), argument(1)))
                if i < 0:
                    raise StubRejected("a null handle")
                return locations[i]

            def attacked(argument) -> int:
                if (argument(4), argument(5)) != (1, 0):
                    raise StubRejected("other arguments")
                notices.append((who((argument(0), argument(1))), who((argument(2), argument(3)))))
                return 0
            words = lambda a: Range(a, a + 8)
            readable = (Range(run_address, run_address + 0x100), Range(component, component + 0x40), Range(record, record + 0x40),
                        Range(RUN_POINTER, RUN_POINTER + 4), Range(COMPONENT_POINTER, COMPONENT_POINTER + 4), Range(RECORD_POINTER, RECORD_POINTER + 4),
                        words(LAST_SUMMONED))
            writable = (words(CURRENT_TARGET), words(CURRENT_TILE), Range(CURRENT_TYPE, CURRENT_TYPE + 4), words(LAST_NOTIFIED))
            try:
                emulator.call(spec.address, (), code=code, stubs={FIELD_GETTER: field, FIELD64_GETTER: location, AI_ATTACKED: attacked},
                              readable=readable, writable=writable)
            except RunFailure as failure:
                if "a null handle" not in str(failure):
                    raise
                redrawn += 1
                continue
            target_after = who((emulator.read_u32(CURRENT_TARGET), emulator.read_u32(CURRENT_TARGET + 4)))
            last_after = who((emulator.read_u32(LAST_NOTIFIED), emulator.read_u32(LAST_NOTIFIED + 4)))
            flat = [v for pair in notices for v in pair] + [-2] * (4 - 2 * len(notices))
            cases.append([*(int(p) for p in present), *(v for loc in locations for v in loc), *types, current, *tile, *run_tile, to_signed(apply & 0xFFFFFFFF),
                          to_signed(apply >> 32), action, aggressive, last, target_after, emulator.read_u32(CURRENT_TILE), emulator.read_u32(CURRENT_TILE + 4),
                          len(notices), *flat, last_after])
            done += 1
            if progress and done % 1000 == 0:
                progress(done)
        roles = ("parent", "source", "runTarget", "summoned", "other")
        document.update(redrawn=redrawn, fields=[*(f"{r}Present" for r in roles), *(f"{r}{a}" for r in roles for a in ("X", "Y")), *(f"{r}Type" for r in roles),
                                                 "current", "tileX", "tileY", "runTileX", "runTileY", "applyLow", "applyHigh", "action", "aggressive", "last",
                                                 "targetAfter", "tileAfterX", "tileAfterY", "notices", "attacker0", "victim0", "attacker1", "victim1", "lastAfter"])
    else:
        raise ValueError(f"unknown model {spec.model}")
    document["cases"] = cases
    return document
