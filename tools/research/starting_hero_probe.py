#!/usr/bin/env python3
"""ARC-RES-027 read-only probe: pre-made heroes, starting gold/items, effects.

Research only, not a character runtime or a translation of the source. Rules are
independently written from the source locators cited in
docs/research/starting-hero-evidence.md. Original mode reads the owner's
installation in memory and prints only numbers, identifiers, counts, engine
keywords and SHA-256 digests; MES text, names and payload bytes never leave the
process. Self-test inputs are invented.
"""
from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
import struct
import sys
import tempfile

import dat_probe

OBJECT_VERSION = 119
TYPE_PC = 15
MAX_PROTO_BYTES = 64 * 1024
MAX_MES_STRING = 2000  # the source's string limit; longer entries are refused by the set parsers
MAX_SET_NAME = 70  # the source's set name limit: a name of this length or more fails
MAX_SET_ITEMS = 300  # the source's set size
MAX_EFFECT_CHANGES = 7  # the source's change limit per effect and attribute collection

# v119 wire kinds of the common block and the critter/PC groups, slot order.
# Same layout as src/Arcanum.Data/StaticObjects/StaticObjectLoader.cs.
I32, OPT_I64, ARR4, ARR8, ARR12, ARR16, OPT_STR, OPT_OID, ARR24 = range(9)


def kinds(spec: str) -> list[int]:
    table = {"i": I32, "p64": OPT_I64, "ai32": ARR4, "au32": ARR4, "ai64": ARR8, "au64": ARR8,
             "ascript": ARR12, "aquest": ARR16, "s": OPT_STR, "poid": OPT_OID, "aoid": ARR24}
    result: list[int] = []
    for token in spec.split(","):
        name, _, count = token.partition("*")
        result += [table[name]] * (int(count) if count else 1)
    return result


COMMON = kinds("i,p64,i*3,au32*3,i*7,au32*3,i*12,ai32,ascript,i*2,au32,au64")
CRITTER = kinds("i*2,ai32*3,au32,i*4,au32*2,poid,i,poid*5,i,aoid,i*2,aoid,p64,i*6,au32,au64")
PC = kinds("i*2,au32,au64,i*2,aquest,au32,au64,au32,au64,i,au64,au32*3,i,s,i,au32*2,i*2,au32,au64")
PC_KINDS = COMMON + CRITTER + PC
PC_MASK_WORDS = 5
# Bits per mask word for a PC: common 32+4, critter 32+1, PC 25 (StaticObjectLoader.MaskLength).
PC_MASK_LENGTHS = (32, len(COMMON) - 32, 32, len(CRITTER) - 32, len(PC))
assert (len(COMMON), len(CRITTER), len(PC)) == (36, 33, 25)

# Slots (obj.h order: common 0..35, critter 36..68, PC 69..93).
S_AID, S_NAME, S_DESCRIPTION = 0, 21, 22
S_CRITTER_FLAGS, S_CRITTER_FLAGS2, S_STAT_BASE, S_BASIC_SKILL, S_TECH_SKILL, S_SPELL_TECH = 36, 37, 38, 39, 40, 41
S_EFFECTS, S_EFFECT_CAUSES, S_PORTRAIT, S_GOLD = 46, 47, 49, 50
S_INVENTORY_NUM, S_INVENTORY_LIST, S_INVENTORY_SOURCE, S_AUTO_LEVEL = 55, 56, 57, 63
S_PC_FLAGS, S_BACKGROUND, S_BACKGROUND_TEXT, S_PLAYER_NAME, S_BANK_MONEY = 69, 73, 74, 86, 87

STATS = ["strength", "dexterity", "constitution", "beauty", "intelligence", "perception", "willpower", "charisma",
         "carry_weight", "damage_bonus", "ac_adjustment", "speed", "heal_rate", "poison_recovery", "reaction_modifier",
         "max_followers", "magick_tech_aptitude", "level", "experience_points", "alignment", "fate_points",
         "unspent_points", "magick_points", "tech_points", "poison_level", "age", "gender", "race"]
RACES = ["human", "dwarf", "elf", "half_elf", "gnome", "halfling", "half_orc", "half_ogre", "dark_elf", "ogre", "orc"]
GENDERS = ["female", "male"]
BODY_TYPES = ["human", "dwarf", "halfling", "half_ogre", "elf"]
EFFECT_CAUSES = ["race", "background", "class", "bless", "curse", "item", "spell", "injury", "tech", "gender"]

# Engine keywords of the effect and auto-level rule grammars (the source's effect and level parsers).
EFFECT_TOKENS = {
    "stat": ["st", "dx", "cn", "be", "in", "pe", "wp", "ch", "carry", "damage", "ac", "speed", "healrate",
             "poisonrate", "beautyreaction", "maxfollowers", "aptitude", "level", "xpsunused", "alignment",
             "fateunused", "unspentunused", "magicpts", "techpts", "poisonlevel", "age", "gender", "race"],
    "basic_skill": ["bow", "dodge", "melee", "throwing", "backstab", "pickpocket", "prowling", "spottrap",
                    "gambling", "haggle", "heal", "persuasion"],
    "tech_skill": ["repair", "firearms", "picklock", "armtrap"],
    "resistance": ["resistdamage", "resistfire", "resistelectrical", "resistpoison", "resistmagic"],
    "tech": ["expertiseanatomical", "expertisechemistry", "expertiseelectric", "expertiseexplosives",
             "expertisegun_smithy", "expertisemechanical", "expertisesmithy", "expertisetherapeutics"],
    "special": ["maxhps", "maxfatigue", "reaction", "crithitchance", "crithiteffect", "critfailchance",
                "critfaileffect", "badreactionadj", "goodreactionadj", "xpgain"],
}
SCHEME_TOKENS = {
    "stat": EFFECT_TOKENS["stat"][:8],
    "basic_skill": EFFECT_TOKENS["basic_skill"],
    "tech_skill": EFFECT_TOKENS["tech_skill"],
    "college": ["conveyance", "divination", "air", "earth", "fire", "water", "force", "mental", "meta", "morph",
                "nature", "necro_evil", "necro_good", "phantasm", "summoning", "temporal"],
    "tech": ["anatomical", "chemistry", "electric", "explosives", "gun_smithy", "mechanical", "smithy", "therapeutics"],
    "misc": ["maxhps", "maxfatigue"],
}

# The built-in PC prototypes (docs/research/starting-hero-evidence.md): race, gender, body type,
# portrait, inventory source, background, background text, auto-level scheme.
PREMADE_PCS = {
    16066: ("human", "male", "human", 1005, 0, 0, 0, 0),
    16067: ("halfling", "male", "halfling", 1017, 108, 39, 1039, 76),
    16068: ("gnome", "male", "halfling", 1002, 24, 40, 1040, 77),
    16069: ("half_elf", "male", "human", 1032, 20, 41, 1041, 78),
    16070: ("half_ogre", "male", "half_ogre", 1027, 25, 42, 1042, 79),
    16071: ("elf", "male", "elf", 1033, 20, 43, 1043, 80),
    16072: ("human", "male", "human", 1005, 22, 44, 1044, 81),
    16073: ("half_orc", "male", "human", 1030, 20, 45, 1045, 82),
    16074: ("dwarf", "male", "dwarf", 1020, 24, 46, 1046, 83),
    16075: ("human", "female", "human", 1025, 23, 47, 1047, 84),
    16076: ("elf", "female", "elf", 1024, 22, 48, 1048, 85),
    16077: ("half_orc", "female", "human", 1031, 26, 49, 1049, 86),
    16078: ("half_elf", "female", "human", 1029, 26, 50, 1050, 87),
}
GENDER_GATE_PROTOTYPE = 16075
RACE_EFFECT_BASE, FEMALE_EFFECT = 64, 330
# Clothes, fallback jackets and skill kits of item_generate_inventory; gold proto.
STARTING_KIT = {"nice_suit": 8049, "small_nice_suit": 8065, "large_nice_suit": 8069, "jacket": 8110,
                "small_jacket": 8118, "large_jacket": 8126, "plain_dress": 8133, "small_ladies_clothes": 8134,
                "large_ladies_clothes": 8135, "crude_lockpicks": 15178, "bandages": 15179, "gold": 9056}
# Armour size by race (the source's race-to-armour-size table); a source fact, not an observation.
ARMOR_SIZE = {"human": "medium", "dwarf": "small", "elf": "medium", "half_elf": "medium", "gnome": "small",
              "halfling": "small", "half_orc": "medium", "half_ogre": "large", "dark_elf": "medium",
              "ogre": "large", "orc": "medium"}

# Critter ART file name parts (the source's art name builder), indexed by the art id fields.
ART_BODY = ["HM", "DF", "GH", "HG", "EF"]
ART_GENDER = "FMX"
ART_ARMOR = ["UW", "V1", "LA", "CM", "PM", "RB", "PC", "BN", "CD"]
ART_SHIELD = "XS"
ART_WEAPON = "ABCDEFGHIJKXYNZ"
ART_TYPE_CRITTER, ART_ARMOR_PLATE, ART_ARMOR_PLATE_CLASSIC, ART_ANIM_EXPLODE = 2, 4, 6, 20
ART_WEAPON_SWORD, ART_WEAPON_TWO_HANDED = 3, 7
ART_BODY_HUMAN, ART_BODY_DWARF, ART_BODY_HALFLING, ART_BODY_ELF = 0, 1, 2, 4
MISSING_STAND_NO_WEAPON = 0x8  # missing.dat bit of the stand/no-weapon check
CRITTER_ART_KINDS = 10  # missing.dat critter entries: gender + 2 * body type
ART_LIST_ROOTS = ("art/critter/", "art/monster/", "art/unique_npc/")
# Background conditions are race/gender codes, NPC or ANY; anything else is printed as a digest.
CONDITION_TOKENS = {r + g for r in ("HU", "DW", "EL", "HE", "GN", "HA", "HO", "HG") for g in "MF"} | {"NPC", "ANY"}

# Mount order, first wins (docs/research/content-precedence.md).
MOUNTS = [("module_folder", "modules/Arcanum", "folder"), ("module_patch0", "modules/Arcanum.PATCH0", "dat"),
          ("module_dat", "modules/Arcanum.dat", "dat"), ("data_folder", "data", "folder"),
          ("arcanum4", "Arcanum4.dat", "dat"), ("arcanum3", "arcanum3.dat", "dat"),
          ("arcanum2", "arcanum2.dat", "dat"), ("arcanum1", "arcanum1.dat", "dat")]
RESOURCES = ["rules/backgrnd.mes", "rules/effect.mes", "rules/gameeffect.mes", "rules/invensource.mes",
             "rules/invensourcebuy.mes", "rules/gamelevel.mes", "mes/gamelevelname.mes", "rules/userlevel.mes"]


class ProbeFailure(ValueError):
    """Only authored static codes, never input-derived messages."""


def require(value: bool, code: str) -> None:
    if not value:
        raise ProbeFailure(code)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def c_atoi(text: str) -> int:
    """C atoi: leading whitespace, optional sign, decimal digits, stop at the first other character."""
    i = 0
    while i < len(text) and text[i] in " \t\n\v\f\r":
        i += 1
    sign = 1
    if i < len(text) and text[i] in "+-":
        sign = -1 if text[i] == "-" else 1
        i += 1
    value = 0
    while i < len(text) and text[i].isascii() and text[i].isdigit():
        value = value * 10 + ord(text[i]) - 48
        i += 1
    return sign * value


def strtok(text: str, state: list[int], delimiters: str) -> str | None:
    """C strtok on one buffer; `state` holds the scan position between calls."""
    i = state[0]
    while i < len(text) and text[i] in delimiters:
        i += 1
    if i >= len(text):
        state[0] = len(text)
        return None
    j = i
    while j < len(text) and text[j] not in delimiters:
        j += 1
    state[0] = j + 1 if j < len(text) else j
    return text[i:j]


@dataclass(frozen=True)
class Mes:
    entries: dict[int, str]
    duplicates: int
    count: int


def parse_mes(data: bytes) -> Mes:
    """Brace pairs: {number}{text}; anything outside braces is ignored."""
    text = data.decode("latin-1")
    fields: list[str] = []
    i = 0
    while True:
        start = text.find("{", i)
        if start < 0:
            break
        end = text.find("}", start + 1)
        if end < 0:
            break
        fields.append(text[start + 1:end])
        i = end + 1
    entries: dict[int, str] = {}
    duplicates = 0
    for k in range(0, len(fields) - 1, 2):
        number = c_atoi(fields[k])
        if number in entries:
            duplicates += 1
            continue
        entries[number] = fields[k + 1]
    return Mes(entries, duplicates, len(fields) // 2)


# ---------------------------------------------------------------- prototypes
class Cursor:
    def __init__(self, data: bytes):
        self.data, self.pos = data, 0

    def take(self, n: int) -> bytes:
        require(0 <= n <= len(self.data) - self.pos, "ProtoTruncated")
        chunk = self.data[self.pos:self.pos + n]
        self.pos += n
        return chunk

    def i32(self) -> int:
        return struct.unpack("<i", self.take(4))[0]

    def u32(self) -> int:
        return struct.unpack("<I", self.take(4))[0]

    def present(self) -> bool:
        flag = self.take(1)[0]
        require(flag in (0, 1), "ProtoPresenceByte")
        return flag == 1


def read_id(cursor: Cursor, blocked: bool = False) -> tuple[int, int]:
    raw = cursor.take(24)
    kind = struct.unpack_from("<h", raw)[0]
    require(0 <= kind <= 3 or (blocked and kind == -1), "ProtoIdKind")
    return kind, struct.unpack_from("<i", raw, 8)[0]


def decode_prototype(data: bytes) -> tuple[int, int, dict[int, object]]:
    """v119 prototype: version, parent marker, own id, type, masks, then every slot."""
    require(len(data) <= MAX_PROTO_BYTES, "ProtoTooLarge")
    cursor = Cursor(data)
    require(cursor.i32() == OBJECT_VERSION, "ProtoVersion")
    require(read_id(cursor, blocked=True)[0] == -1, "ProtoParentMarker")
    kind, number = read_id(cursor)
    require(kind == 1, "ProtoOwnId")
    obj_type = cursor.i32()
    require(obj_type == TYPE_PC, "ProtoNotPc")
    for length in PC_MASK_LENGTHS:
        mask = cursor.u32()
        require(length == 32 or mask >> length == 0, "ProtoMaskReservedBits")
    fields: dict[int, object] = {}
    for slot, wire in enumerate(PC_KINDS):
        if wire == I32:
            fields[slot] = cursor.i32()
        elif not cursor.present():
            fields[slot] = None
        elif wire == OPT_I64:
            fields[slot] = struct.unpack("<q", cursor.take(8))[0]
        elif wire == OPT_OID:
            fields[slot] = ("oid",) + read_id(cursor)
        elif wire == OPT_STR:
            length = cursor.i32()
            require(0 <= length <= 4096, "ProtoString")
            body = cursor.take(length + 1)
            require(body[-1] == 0, "ProtoStringNul")
            fields[slot] = ("str", length, sha(body[:-1]))
        else:
            width = {ARR4: 4, ARR8: 8, ARR12: 12, ARR16: 16, ARR24: 24}[wire]
            require(cursor.i32() == width, "ProtoArrayWidth")
            count = cursor.i32()
            cursor.i32()
            require(0 <= count <= 4096, "ProtoArrayCount")
            values = cursor.take(width * count)
            words = cursor.i32()
            require(1 <= words <= 4096, "ProtoArrayWords")
            bits = cursor.take(words * 4)
            keys = [w * 32 + b for w in range(words) for b in range(32)
                    if struct.unpack_from("<I", bits, w * 4)[0] >> b & 1]
            require(len(keys) == count, "ProtoArrayBitset")
            fields[slot] = {key: (struct.unpack_from("<i", values, n * width)[0] if width == 4 else
                                  sha(values[n * width:(n + 1) * width])) for n, key in enumerate(keys)}
    require(cursor.pos == len(data), "ProtoTrailingBytes")
    return obj_type, number, fields


def prototype_header_type(data: bytes) -> int:
    cursor = Cursor(data)
    require(cursor.i32() == OBJECT_VERSION, "ProtoVersion")
    require(read_id(cursor, blocked=True)[0] == -1, "ProtoParentMarker")
    require(read_id(cursor)[0] == 1, "ProtoOwnId")
    return cursor.i32()


def critter_art_path(aid: int, missing: list[int] | None = None) -> str | None:
    """Critter ART file name for an art id, or None for another art type.

    Applies the engine's id normalisation first: female elves draw the human
    body; plate armour is male, elf → human, halfling → dwarf; and, for the
    stand animation without a weapon, the missing-art table switches to the
    unarmed file when its stand/no-weapon bit is set for that body/gender.
    The other missing-art rows and per-animation resets are not modelled, so
    only animation 0 ids are accepted."""
    if aid >> 28 != ART_TYPE_CRITTER:
        return None
    armor, body, gender = aid >> 20 & 0xF, aid >> 24 & 7, aid >> 27 & 1
    shield, weapon, anim = aid >> 19 & 1, aid & 0xF, aid >> 6 & 0x1F
    if armor >= len(ART_ARMOR) or body >= len(ART_BODY):
        return None
    require(anim == 0, "ArtAnimNotModelled")
    if body == ART_BODY_ELF and gender == 0:
        body = ART_BODY_HUMAN
    if armor in (ART_ARMOR_PLATE, ART_ARMOR_PLATE_CLASSIC):
        gender = 1
        body = {ART_BODY_ELF: ART_BODY_HUMAN, ART_BODY_HALFLING: ART_BODY_DWARF}.get(body, body)
    kind = gender + 2 * body
    if missing is not None and kind < len(missing) and weapon == 0 and missing[kind] & MISSING_STAND_NO_WEAPON:
        weapon = 1
    gender_code = ART_GENDER[2] if armor in (ART_ARMOR_PLATE, ART_ARMOR_PLATE_CLASSIC) else ART_GENDER[gender]
    armor_code = ART_ARMOR[armor]
    if anim == ART_ANIM_EXPLODE:
        armor_code, gender_code = "XX", ART_GENDER[2]
    if weapon == ART_WEAPON_TWO_HANDED and shield == 1:
        weapon = ART_WEAPON_SWORD
    stem = ART_BODY[body] + gender_code
    return f"art/critter/{stem}/{stem}{armor_code}{ART_SHIELD[shield]}{ART_WEAPON[weapon]}{chr(97 + anim)}.art".lower()


def art_fields(aid: int) -> dict[str, int]:
    return {"type": aid >> 28, "body_type": aid >> 24 & 7, "gender": aid >> 27 & 1, "armor": aid >> 20 & 0xF,
            "shield": aid >> 19 & 1, "weapon": aid & 0xF, "anim": aid >> 6 & 0x1F}


# ------------------------------------------------------------- rule parsers
def effect_over_limit(changes: list[dict]) -> bool:
    """The source stores at most 7 changes per attribute collection; more would overrun its arrays."""
    counts: dict[str, int] = {}
    for change in changes:
        counts[change["category"]] = counts.get(change["category"], 0) + 1
    return any(n > MAX_EFFECT_CHANGES for n in counts.values())


def parse_effect(text: str) -> tuple[list[dict], bool]:
    """effect.mes grammar: attribute token, then operator token (+n, -n, n%, *n, /n, min n, max n)."""
    changes: list[dict] = []
    state = [0]
    tok = strtok(text, state, " ,")
    while tok is not None:
        found = None
        for category, names in EFFECT_TOKENS.items():
            if tok.lower() in names:
                found = (category, names.index(tok.lower()))
                break
        if found is None:
            return changes, False
        op_tok = strtok(text, state, " ,")
        if op_tok is None:
            return changes, False
        if op_tok.lower() in ("min", "max"):
            value_tok = strtok(text, state, " ,")
            operator, param = op_tok.lower(), c_atoi(value_tok or "")
        elif op_tok[0] in "+-":
            operator, param = ("percent" if op_tok.endswith("%") else "add"), c_atoi(op_tok)
        elif op_tok[0] in "*/":
            # The original reads the number after the operator (ledger LM9, SH-07).
            operator, param = ("multiply" if op_tok[0] == "*" else "divide"), c_atoi(op_tok[1:])
        else:
            operator, param = "invalid", 0
        change = {"category": found[0], "attribute": found[1], "operator": operator, "param": param}
        changes.append(change)
        tok = strtok(text, state, " ,")
    return changes, True


def parse_scheme(text: str) -> tuple[list[dict], bool]:
    """gamelevel.mes grammar: '<token> <score>,' repeated; an unknown token ends the rule."""
    steps: list[dict] = []
    state = [0]
    tok = strtok(text, state, " ")
    while tok is not None:
        score = c_atoi(strtok(text, state, ",") or "")
        found = None
        for category, names in SCHEME_TOKENS.items():
            if tok.lower() in names:
                found = (category, names.index(tok.lower()))
                break
        if found is None:
            return steps, False
        steps.append({"category": found[0], "attribute": found[1], "target": score})
        tok = strtok(text, state, " ")
    return steps, True


@dataclass(frozen=True)
class InvenSet:
    number: int
    parsed: bool
    min_coins: int
    max_coins: int
    items: tuple[tuple[int, int], ...]
    reachable: bool


def buy_entry_ok(text: str, proto_ok) -> bool:
    """InvenSourceBuy.mes line: empty, ALL, or space-separated valid prototypes (at most 300)."""
    state = [0]
    tok = strtok(text, state, " ")
    if tok is None or tok.upper() == "ALL":
        return len(text) < MAX_MES_STRING
    count = 0
    while tok is not None:
        if count >= MAX_SET_ITEMS or not proto_ok(c_atoi(tok)):
            return False
        count += 1
        tok = strtok(text, state, " ")
    return len(text) < MAX_MES_STRING


def parse_invensource(mes: Mes, proto_ok, buy: Mes | None = None) -> tuple[list[InvenSet], dict]:
    """InvenSource.mes: sets numbered 1..N in order; 'name: min,max rate,proto rate,proto ...'.

    Iteration starts after number 0 (an entry 0 is skipped, as `mes_find_next` from 0
    does) and stops at the first gap. A line over the string limit, an empty line or
    a name of 70+ characters fails before the coins are read; an incomplete pair, an
    unknown prototype or more than 300 items fails after them and empties only the
    items (coins already read stay). A failing buy line empties the items too. Set N
    is out of range for the lookup (`id < count`)."""
    numbers = sorted(n for n in mes.entries if n > 0)
    count = mes.count
    sets: list[InvenSet] = []
    gap = None
    for index, number in enumerate(numbers, start=1):
        if number != index:
            gap = number
            break
        text = mes.entries[number]
        state = [0]
        name = strtok(text, state, ":") if len(text) < MAX_MES_STRING else None
        ok = name is not None and len(name) < MAX_SET_NAME
        min_coins = max_coins = 0
        items: list[tuple[int, int]] = []
        tok = strtok(text, state, ",") if ok else None
        first = True
        while ok and tok is not None:
            if not first and len(items) >= MAX_SET_ITEMS:
                ok = False
                break
            rate = c_atoi(tok)
            proto_tok = strtok(text, state, " ")
            if proto_tok is None:
                ok = False
                break
            proto = c_atoi(proto_tok)
            if first:
                min_coins, max_coins, first = rate, proto, False
            elif not proto_ok(proto):
                ok = False
                break
            else:
                items.append((rate, proto))
            tok = strtok(text, state, ",")
        if ok and buy is not None and number in buy.entries and not buy_entry_ok(buy.entries[number], proto_ok):
            ok = False
        sets.append(InvenSet(number, ok, min_coins, max_coins, tuple(items) if ok else (), 0 < number < count))
    return sets, {"entries": count, "contiguous_sets": len(sets), "first_gap": gap, "duplicates": mes.duplicates,
                  "entry_zero_skipped": 0 in mes.entries}


def parse_backgrounds(mes: Mes) -> list[dict]:
    blocks = []
    b = 0
    while all(10 * b + k in mes.entries for k in range(5)):
        e = mes.entries
        state, items = [0], []
        tok = strtok(e[10 * b + 4], state, " \t\n")
        while tok is not None:
            items.append(c_atoi(tok))
            tok = strtok(e[10 * b + 4], state, " \t\n")
        condition = e[10 * b + 2].split()
        known = all(t.upper() in CONDITION_TOKENS for t in condition)
        blocks.append({"block": b, "description": c_atoi(e[10 * b]), "effect": c_atoi(e[10 * b + 1]),
                       "effect_text_empty": e[10 * b + 1].strip() == "",
                       "condition": [t.upper() for t in condition] if known else {"sha256": sha(e[10 * b + 2].encode("latin-1"))},
                       "npc_only": known and [t.upper() for t in condition] == ["NPC"],
                       "money": c_atoi(e[10 * b + 3]), "items": items})
        b += 1
    return blocks


# ------------------------------------------------------------ installation
class Source:
    def __init__(self, role: str, path: Path, kind: str):
        self.role, self.path, self.kind = role, path, kind
        self.archive = dat_probe.parse_archive(path) if kind == "dat" else None
        self.names: dict[str, list] = {}
        if self.archive is not None:
            for entry in self.archive.entries:
                if entry.is_directory:
                    continue
                name = entry.raw_name.rstrip(b"\0").decode("ascii", "replace").replace("\\", "/").lower()
                self.names.setdefault(name, []).append(entry)
        else:
            for file in path.rglob("*"):
                if file.is_file():
                    self.names.setdefault(file.relative_to(path).as_posix().lower(), []).append(file)

    def read(self, name: str) -> bytes:
        hits = self.names[name]
        require(len(hits) == 1, "AmbiguousEntry")
        if self.archive is not None:
            return dat_probe.read_bounded_entry(self.archive, hits[0])
        require(hits[0].stat().st_size <= dat_probe.HARD_OUTPUT_CAP, "LooseFileTooLarge")
        return hits[0].read_bytes()


def find_case_insensitive(root: Path, relative: str) -> Path | None:
    current = root
    for part in relative.split("/"):
        if not current.is_dir():
            return None
        matches = [child for child in current.iterdir() if child.name.lower() == part.lower()]
        if len(matches) != 1:
            return None
        current = matches[0]
    return current


def open_sources(root: Path) -> list[Source]:
    sources = []
    for role, relative, kind in MOUNTS:
        path = find_case_insensitive(root, relative)
        if path is None or (kind == "dat") != path.is_file():
            continue
        sources.append(Source(role, path, kind))
    return sources


def resolve(sources: list[Source], name: str) -> tuple[list[dict], bytes | None, str | None, list[bytes]]:
    copies, winner, winner_role, payloads = [], None, None, []
    for source in sources:
        if name in source.names:
            data = source.read(name)
            copies.append({"source": source.role, "bytes": len(data), "sha256": sha(data)})
            payloads.append(data)
            if winner is None:
                winner, winner_role = data, source.role
    return copies, winner, winner_role, payloads


def find_prototype(sources: list[Source], description: int) -> tuple[str | None, bytes | None, int]:
    """First source holding `proto/<description+20 as 6 digits> - *.pro`; also counts all hits."""
    pattern = f"proto/{description + 20:06d} - *.pro"
    hits = [(s, n) for s in sources for n in s.names if fnmatch.fnmatchcase(n, pattern)]
    if not hits:
        return None, None, 0
    first_source = hits[0][0]
    first = [n for s, n in hits if s is first_source]
    require(len(first) == 1, "AmbiguousPrototype")
    return first_source.role, first_source.read(first[0]), len(hits)


# ------------------------------------------------------------------ report
def missing_art_table(sources: list[Source]) -> dict:
    """Critter rows of art/missing.dat, used only when its file count matches the ART lists;
    otherwise the stand/no-weapon bit is derived from file existence, as the source rebuilds it."""
    listed = {n for src in sources for n in src.names
              if n.startswith(ART_LIST_ROOTS) and n.count("/") == 3 and n.endswith(".art")}
    copies, data, role, _ = resolve(sources, "art/missing.dat")
    header = struct.unpack_from("<i", data)[0] if data and len(data) >= 4 + 4 * CRITTER_ART_KINDS else None
    file_masks = list(struct.unpack_from(f"<{CRITTER_ART_KINDS}I", data, 4)) if header is not None else None
    derived = []
    for kind in range(CRITTER_ART_KINDS):
        # The rebuild probes the normalised stand/no-weapon underwear id of each body/gender.
        path = critter_art_path((ART_TYPE_CRITTER << 28) | ((kind & 1) << 27) | ((kind // 2) << 24))
        derived.append(0 if path in listed else MISSING_STAND_NO_WEAPON)
    used = file_masks if header == len(listed) else derived
    return {"source": role, "copies": copies, "header_count": header, "listed_art_files": len(listed), "file_used": header == len(listed),
            "stand_no_weapon_missing": [bool(m & MISSING_STAND_NO_WEAPON) for m in used],
            "file_and_derived_agree": file_masks is not None and
            all(bool(a & MISSING_STAND_NO_WEAPON) == bool(b) for a, b in zip(file_masks, derived)),
            "masks": used}


def pc_row(description: int, sources: list[Source], missing: list[int]) -> dict:
    role, data, hits = find_prototype(sources, description)
    require(data is not None, "PcPrototypeMissing")
    obj_type, number, f = decode_prototype(data)
    require(number == description + 20, "PcPrototypeNumber")
    stats = f[S_STAT_BASE]
    effects, causes = f[S_EFFECTS] or {}, f[S_EFFECT_CAUSES] or {}
    race, gender = stats.get(27), stats.get(26)
    expected = PREMADE_PCS[description]
    aid = f[S_AID] & 0xFFFFFFFF
    art = critter_art_path(aid, missing)
    art_hits = [s.role for s in sources if art is not None and art in s.names]
    name = f[S_PLAYER_NAME]
    row = {
        "description": description, "file_number": number, "source": role, "copies": hits,
        "bytes": len(data), "sha256": sha(data), "type": obj_type,
        "name_class": f[S_NAME], "description_field": f[S_DESCRIPTION],
        "race": RACES[race] if race is not None and 0 <= race < len(RACES) else race,
        "gender": GENDERS[gender] if gender in (0, 1) else gender,
        "stat_base": {STATS[k]: v for k, v in sorted(stats.items()) if k < len(STATS)},
        "stat_base_keys": len(stats),
        "basic_skill_nonzero": sum(1 for v in (f[S_BASIC_SKILL] or {}).values() if v),
        "tech_skill_nonzero": sum(1 for v in (f[S_TECH_SKILL] or {}).values() if v),
        "spell_tech_nonzero": {k: v for k, v in sorted((f[S_SPELL_TECH] or {}).items()) if v},
        "effects": [{"effect": effects[k], "cause": EFFECT_CAUSES[causes[k]] if 0 <= causes.get(k, -1) < len(EFFECT_CAUSES) else causes.get(k)}
                    for k in sorted(effects)],
        "portrait": f[S_PORTRAIT], "inventory_source": f[S_INVENTORY_SOURCE], "inventory_num": f[S_INVENTORY_NUM],
        "inventory_list": len(f[S_INVENTORY_LIST] or {}), "gold_handle_present": f[S_GOLD] is not None,
        "auto_level_scheme": f[S_AUTO_LEVEL], "background": f[S_BACKGROUND], "background_text": f[S_BACKGROUND_TEXT],
        "bank_money": f[S_BANK_MONEY], "pc_flags": f"0x{f[S_PC_FLAGS] & 0xFFFFFFFF:X}",
        "critter_flags": f"0x{f[S_CRITTER_FLAGS] & 0xFFFFFFFF:X}", "critter_flags2": f"0x{f[S_CRITTER_FLAGS2] & 0xFFFFFFFF:X}",
        "player_name_bytes": None if name is None else name[1],
        "current_aid": f"0x{aid:08X}", "art": art_fields(aid), "art_path": art, "art_sources": art_hits,
    }
    row["matches_prototype_table"] = {
        "race": row["race"] == expected[0], "gender": row["gender"] == expected[1],
        "body_type": row["art"]["body_type"] < len(BODY_TYPES) and BODY_TYPES[row["art"]["body_type"]] == expected[2],
        "art_gender": row["art"]["gender"] == GENDERS.index(expected[1]),
        "portrait": row["portrait"] == expected[3], "inventory_source": row["inventory_source"] == expected[4],
        "background": row["background"] == expected[5], "background_text": row["background_text"] == expected[6],
        "auto_level_scheme": row["auto_level_scheme"] == expected[7],
    }
    return row


def effect_row(effect_tables: dict[int, str], number: int) -> dict:
    if number not in effect_tables:
        return {"effect": number, "present": False}
    changes, ok = parse_effect(effect_tables[number])
    return {"effect": number, "present": True, "parsed": ok, "changes": changes}


def original(root: Path) -> dict:
    sources = open_sources(root)
    require([s.role for s in sources][:1] != [], "NoSources")
    fingerprints = []
    for s in sources:
        if s.kind == "dat":
            fingerprints.append({"source": s.role, "kind": "dat", "bytes": s.path.stat().st_size,
                                 "sha256": dat_probe.hash_file(s.path), "entries": len(s.archive.entries)})
        else:
            fingerprints.append({"source": s.role, "kind": "folder", "files": sum(len(v) for v in s.names.values())})
    resources, winners, all_copies = {}, {}, {}
    for name in RESOURCES:
        copies, data, role, payloads = resolve(sources, name)
        all_copies[name] = [parse_mes(p) for p in payloads]
        resources[name] = {"copies": copies, "winner": role}
        winners[name] = parse_mes(data) if data is not None else None
        if data is not None:
            resources[name]["winner_entries"] = winners[name].count
            resources[name]["winner_duplicates"] = winners[name].duplicates

    proto_cache: dict[int, tuple[str | None, int | None, int]] = {}

    def proto_info(description: int) -> tuple[str | None, int | None, int]:
        if description not in proto_cache:
            role, data, hits = find_prototype(sources, description)
            proto_cache[description] = (role, prototype_header_type(data) if data else None, hits)
        return proto_cache[description]

    missing = missing_art_table(sources)
    pcs = [pc_row(d, sources, missing["masks"]) for d in sorted(PREMADE_PCS)]
    critter_folders = sorted({(n.split("/")[2], s.role) for s in sources for n in s.names
                              if n.startswith("art/critter/") and n.count("/") == 3})

    # Effects: module 0..49 from gameeffect.mes, 50..399 from effect.mes.
    tables: dict[int, str] = {}
    if winners["rules/effect.mes"]:
        tables.update({n: t for n, t in winners["rules/effect.mes"].entries.items() if 50 <= n < 400})
    if winners["rules/gameeffect.mes"]:
        tables.update({n: t for n, t in winners["rules/gameeffect.mes"].entries.items() if 0 <= n < 50})
    parsed_all = {n: parse_effect(t) for n, t in tables.items()}

    blocks = parse_backgrounds(winners["rules/backgrnd.mes"]) if winners["rules/backgrnd.mes"] else []
    for block in blocks:
        block["effect_resolved"] = effect_row(tables, block["effect"])
        block["item_prototypes"] = [{"description": d, "source": proto_info(d)[0], "type": proto_info(d)[1]} for d in block["items"]]
        del block["items"]

    sets, set_summary = parse_invensource(winners["rules/invensource.mes"], lambda d: proto_info(d)[1] is not None,
                                          winners["rules/invensourcebuy.mes"]) \
        if winners["rules/invensource.mes"] else ([], {})
    by_number = {s.number: s for s in sets}
    wanted_sets = sorted({p["inventory_source"] for p in pcs if p["inventory_source"]})
    set_rows = []
    for number in wanted_sets:
        s = by_number.get(number)
        set_rows.append({"set": number, "defined": s is not None} if s is None else {
            "set": number, "defined": True, "parsed": s.parsed, "reachable": s.reachable,
            "min_coins": s.min_coins, "max_coins": s.max_coins, "items": len(s.items),
            "all_rates_100": all(rate >= 100 for rate, _ in s.items),
            "entries": [{"rate": rate, "prototype": proto, "type": proto_info(proto)[1]} for rate, proto in s.items],
            "buy_entry": None if not winners["rules/invensourcebuy.mes"] or number not in winners["rules/invensourcebuy.mes"].entries
            else {"empty": winners["rules/invensourcebuy.mes"].entries[number].strip() == ""},
            "same_in_every_copy": all(c.entries.get(number) == winners["rules/invensource.mes"].entries.get(number)
                                      for c in all_copies["rules/invensource.mes"])})

    level_mes, name_mes = winners["rules/gamelevel.mes"], winners["mes/gamelevelname.mes"]
    schemes = []
    for scheme in sorted({p["auto_level_scheme"] for p in pcs if p["auto_level_scheme"]}):
        rule = level_mes.entries.get(scheme) if level_mes else None
        steps, ok = parse_scheme(rule) if rule is not None else ([], False)
        counts: dict[str, int] = {}
        for step in steps:
            counts[step["category"]] = counts.get(step["category"], 0) + 1
        schemes.append({"scheme": scheme, "rule_present": rule is not None, "parsed": ok, "steps": len(steps),
                        "categories": counts, "first_steps": steps[:6],
                        "same_in_every_copy": all(c.entries.get(scheme) == rule for c in all_copies["rules/gamelevel.mes"]),
                        "name_present": bool(name_mes and scheme in name_mes.entries)})
    standard = level_mes.entries.get(70) if level_mes else None

    def block_summary(rows: list[dict]) -> dict:
        categories: dict[str, int] = {}
        operators: dict[str, int] = {}
        for row in rows:
            changes = row["effect_resolved"].get("changes", [])
            for category in {c["category"] for c in changes}:
                categories[category] = categories.get(category, 0) + 1
            for change in changes:
                operators[change["operator"]] = operators.get(change["operator"], 0) + 1
        return {"blocks": len(rows), "blocks_changing": categories, "operators": operators,
                "zero_change_blocks": [r["block"] for r in rows if not r["effect_resolved"].get("changes")],
                "item_blocks": [r["block"] for r in rows if r["item_prototypes"]]}

    gate = next(p for p in pcs if p["description"] == GENDER_GATE_PROTOTYPE)
    return {
        "probe": "starting_hero_probe", "mode": "original",
        "sources": fingerprints, "resources": resources,
        "pc_prototypes": pcs,
        "missing_art": {k: v for k, v in missing.items() if k != "masks"},
        "critter_art_folders": [{"folder": f, "source": r} for f, r in critter_folders],
        "gender_gate": {"prototype": GENDER_GATE_PROTOTYPE, "art_path": gate["art_path"], "art_sources": gate["art_sources"],
                        "open": bool(gate["art_sources"]), "pregen_count_if_open": 13, "new_char_buttons_if_open": 10},
        "inventory_sources": {"summary": set_summary, "sets": set_rows},
        "auto_level_schemes": schemes, "standard_npc_scheme_70_present": standard is not None,
        "backgrounds": {"blocks": len(blocks), "rows": blocks,
                        "pc_offered": block_summary([b for b in blocks if not b["npc_only"]]),
                        "npc_only": block_summary([b for b in blocks if b["npc_only"]])},
        "effects": {
            "table_entries": len(tables), "unparsed": sorted(n for n, (_, ok) in parsed_all.items() if not ok),
            "over_change_limit": sorted(n for n, (changes, _) in parsed_all.items() if effect_over_limit(changes)),
            "at_change_limit": sorted(n for n, (changes, _) in parsed_all.items()
                                      if any(sum(c["category"] == k for c in changes) == MAX_EFFECT_CHANGES for k in EFFECT_TOKENS)),
            "race": [dict(effect_row(tables, RACE_EFFECT_BASE + r), race=RACES[r]) for r in range(len(RACES))],
            "female": effect_row(tables, FEMALE_EFFECT),
        },
        "starting_kit": {key: {"description": d, "source": proto_info(d)[0], "type": proto_info(d)[1], "copies": proto_info(d)[2]}
                         for key, d in STARTING_KIT.items()},
        "armor_size_by_race": ARMOR_SIZE,
    }


# ---------------------------------------------------------------- self-test
def make_proto(fields: dict[int, object], obj_type: int = TYPE_PC, number: int = 16095) -> bytes:
    out = bytearray(struct.pack("<i", OBJECT_VERSION))
    out += struct.pack("<h6xi12x", -1, 0)
    out += struct.pack("<h6xi12x", 1, number)
    out += struct.pack("<i", obj_type)
    out += b"".join(struct.pack("<I", (1 << n) - 1) for n in PC_MASK_LENGTHS)
    for slot, wire in enumerate(PC_KINDS):
        value = fields.get(slot)
        if wire == I32:
            out += struct.pack("<i", value or 0)
        elif value is None:
            out += b"\0"
        elif wire == OPT_STR:
            out += b"\1" + struct.pack("<i", len(value)) + value + b"\0"
        elif wire in (ARR4,):
            keys = sorted(value)
            words = max(keys) // 32 + 1 if keys else 1
            bits = [0] * words
            for key in keys:
                bits[key // 32] |= 1 << (key % 32)
            out += b"\1" + struct.pack("<iii", 4, len(keys), 0) + b"".join(struct.pack("<i", value[k]) for k in keys)
            out += struct.pack("<i", words) + b"".join(struct.pack("<I", w) for w in bits)
        else:
            raise AssertionError("self-test writes only i32, strings and 4-byte arrays")
    return bytes(out)


def self_test() -> dict:
    checks = 0

    def check(actual, expected):
        nonlocal checks
        require(actual == expected, f"SyntheticOracleMismatch{checks}")
        checks += 1

    # C helpers.
    check([c_atoi(s) for s in [" 12x", "-7", "+3", "", "abc", "\t40"]], [12, -7, 3, 0, 0, 40])
    state = [0]
    check([strtok(",a,,b ", state, ","), strtok(",a,,b ", state, ","), strtok(",a,,b ", state, ",")], ["a", "b ", None])

    # MES: brace pairs, comments outside braces, duplicate numbers keep the first.
    mes = parse_mes(b"// note\n{1}{one}\n{2}{} junk {1}{again}{3}{x")
    check((mes.entries, mes.duplicates, mes.count), ({1: "one", 2: ""}, 1, 3))

    # Effect grammar: operators, percent, min/max, unknown symbol stops.
    changes, ok = parse_effect("st +1, cn -2 heal +10% maxhps min 5, resistfire *2")
    check(ok, True)
    check([(c["category"], c["attribute"], c["operator"], c["param"]) for c in changes],
          [("stat", 0, "add", 1), ("stat", 2, "add", -2), ("basic_skill", 10, "percent", 10),
           ("special", 0, "min", 5), ("resistance", 1, "multiply", 2)])
    check(parse_effect("st +1, bogus +2")[1], False)
    check((effect_over_limit(parse_effect("st +1, " * 7)[0]), effect_over_limit(parse_effect("st +1, " * 8)[0])), (False, True))

    # Scheme grammar: '<token> <score>,' pairs in order; unknown token fails.
    steps, ok = parse_scheme("st 10, melee 3, fire 2, maxhps 40")
    check(ok, True)
    check([(s["category"], s["attribute"], s["target"]) for s in steps],
          [("stat", 0, 10), ("basic_skill", 2, 3), ("college", 4, 2), ("misc", 0, 40)])
    check(parse_scheme("st 10, nonsense 1")[1], False)

    # InvenSource: coins pair first, rate,proto pairs, an unknown proto empties only the
    # items, entry 0 is skipped, a gap stops parsing, set N of N entries is unreachable.
    known = {5001, 6002}
    inv = parse_mes(b"{0}{Z: 9,9}{1}{A: 10,20 100,5001 50,6002}{2}{B: 5,6 100,9999}{3}{C:}{5}{D: 1,1}")
    sets, summary = parse_invensource(inv, lambda d: d in known)
    check([(s.number, s.parsed, s.min_coins, s.max_coins, s.items, s.reachable) for s in sets],
          [(1, True, 10, 20, ((100, 5001), (50, 6002)), True), (2, False, 5, 6, (), True), (3, True, 0, 0, (), True)])
    check((summary["entries"], summary["contiguous_sets"], summary["first_gap"], summary["entry_zero_skipped"]), (5, 3, 5, True))
    long_name = parse_mes(b"{1}{" + b"N" * 70 + b": 7,8}{2}{" + b"N" * 69 + b": 7,8}{3}{x}")
    check([(s.parsed, s.min_coins) for s in parse_invensource(long_name, lambda d: True)[0]][:2], [(False, 0), (True, 7)])
    many = parse_mes(b"{1}{M: 1,1 " + b" ".join(b"1,5" for _ in range(301)) + b"}{2}{x}")
    check([(s.parsed, s.min_coins, s.items) for s in parse_invensource(many, lambda d: True)[0]][:1], [(False, 1, ())])
    buy = parse_mes(b"{1}{ALL}{2}{5001 9999}{3}{}")
    two = parse_mes(b"{1}{A: 1,1 100,5001}{2}{B: 2,2 100,5001}{3}{C: 3,3 100,5001}{4}{x}")
    check([(s.parsed, len(s.items)) for s in parse_invensource(two, lambda d: d in known, buy)[0]][:3],
          [(True, 1), (False, 0), (True, 1)])
    last, _ = parse_invensource(parse_mes(b"{1}{A: 1,2}{2}{B: 3,4}"), lambda d: True)
    check([(s.number, s.min_coins, s.max_coins, s.reachable) for s in last], [(1, 1, 2, True), (2, 3, 4, False)])

    # Backgrounds: complete 5-message blocks only.
    bg = parse_mes(b"{0}{1000}{1}{51}{2}{}{3}{400}{4}{}{10}{1001}{11}{}{12}{HUM}{13}{100}{14}{5001 6002}{20}{1002}")
    blocks = parse_backgrounds(bg)
    check([(b["block"], b["description"], b["effect"], b["condition"], b["money"], b["items"]) for b in blocks],
          [(0, 1000, 51, [], 400, []), (1, 1001, 0, ["HUM"], 100, [5001, 6002])])
    check((blocks[1]["effect_text_empty"], blocks[1]["npc_only"]), (True, False))
    odd = parse_backgrounds(parse_mes(b"{0}{1}{1}{2}{2}{secret words}{3}{4}{4}{}"))[0]["condition"]
    check(sorted(odd), ["sha256"])

    # Critter ART names: underwear human male; plate forces X; explode overrides armor; 2H+shield → sword.
    aid = (2 << 28) | (1 << 27) | (0 << 24) | (0 << 20)
    check(critter_art_path(aid), "art/critter/hmm/hmmuwxaa.art")
    check(critter_art_path((2 << 28) | (0 << 27) | (4 << 24) | (4 << 20) | (1 << 19) | 7), "art/critter/hmx/hmxpmsda.art")
    check(critter_art_path((2 << 28) | (0 << 27) | (4 << 24)), "art/critter/hmf/hmfuwxaa.art")
    check(critter_art_path((2 << 28) | (1 << 27) | (2 << 24) | (6 << 20)), "art/critter/dfx/dfxpcxaa.art")
    check(critter_art_path((2 << 28) | (1 << 27) | (4 << 24)), "art/critter/efm/efmuwxaa.art")
    check(critter_art_path(6 << 28), None)
    # Missing stand/no-weapon art (kind = gender + 2 * body) switches to the unarmed file.
    check(critter_art_path(aid, [0, MISSING_STAND_NO_WEAPON] + [0] * 8), "art/critter/hmm/hmmuwxba.art")
    check(critter_art_path(aid, [MISSING_STAND_NO_WEAPON] + [0] * 9), "art/critter/hmm/hmmuwxaa.art")
    try:
        critter_art_path((2 << 28) | (1 << 27) | (20 << 6))
    except ProbeFailure:
        checks += 1
    else:
        raise ProbeFailure("SyntheticRejectMissing")

    # Prototype decoder: header, every slot, sparse arrays, strings, trailing bytes.
    fields = {S_AID: aid, S_PORTRAIT: 1025, S_INVENTORY_SOURCE: 23, S_AUTO_LEVEL: 84, S_BACKGROUND: 47,
              S_BACKGROUND_TEXT: 1047, S_STAT_BASE: {26: 0, 27: 0, 0: 8}, S_EFFECTS: {0: 64}, S_EFFECT_CAUSES: {0: 0},
              S_PLAYER_NAME: b"abc"}
    data = make_proto(fields)
    obj_type, number, decoded = decode_prototype(data)
    check((obj_type, number, decoded[S_PORTRAIT], decoded[S_INVENTORY_SOURCE], decoded[S_STAT_BASE]),
          (TYPE_PC, 16095, 1025, 23, {0: 8, 26: 0, 27: 0}))
    check((decoded[S_EFFECTS], decoded[S_PLAYER_NAME][:2], decoded[S_GOLD]), ({0: 64}, ("str", 3), None))
    mask_at = 4 + 24 + 24 + 4 + 4  # second mask word: 4 valid bits
    reserved = data[:mask_at] + struct.pack("<I", 0x1F) + data[mask_at + 4:]
    bad_kind = data[:28] + struct.pack("<h", 9) + data[30:]
    for bad in [data + b"\0", data[:-1], make_proto(fields, obj_type=16), b"\x76" + data[1:], reserved, bad_kind]:
        try:
            decode_prototype(bad)
        except ProbeFailure:
            checks += 1
        else:
            raise ProbeFailure("SyntheticRejectMissing")

    # Installation: precedence over a folder and two archives, prototype lookup.
    with tempfile.TemporaryDirectory(prefix="starting-hero-selftest-") as directory:
        root = Path(directory)
        (root / "data" / "proto").mkdir(parents=True)
        (root / "data" / "proto" / "016095 - PC.pro").write_bytes(data)
        (root / "data" / "rules").mkdir()
        (root / "data" / "rules" / "Effect.mes").write_bytes(b"{64}{st +1}")
        archive = dat_probe.make_archive([(b"rules\\effect.mes", 0x001, 11, b"{64}{st +2}"),
                                          (b"Rules\\BACKGRND.MES", 0x001, 6, b"{0}{1}")])
        (root / "Arcanum4.dat").write_bytes(archive)
        sources = open_sources(root)
        check([s.role for s in sources], ["data_folder", "arcanum4"])
        copies, winner, role, _ = resolve(sources, "rules/effect.mes")
        check((len(copies), role, winner), (2, "data_folder", b"{64}{st +1}"))
        check(resolve(sources, "rules/effect.mes")[3], [b"{64}{st +1}", b"{64}{st +2}"])
        check(find_prototype(sources, 16075)[0], "data_folder")
        check(find_prototype(sources, 16076), (None, None, 0))
    return {"probe": "starting_hero_probe", "mode": "self-test", "checks": checks, "result": "PASS"}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--self-test", action="store_true")
    group.add_argument("--game-root", type=Path)
    args = parser.parse_args(argv)
    try:
        report = self_test() if args.self_test else original(args.game_root)
    except (ProbeFailure, dat_probe.DatProbeError) as error:
        print(json.dumps({"result": "FAIL", "code": str(error)}))
        return 1
    print(json.dumps(report, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
