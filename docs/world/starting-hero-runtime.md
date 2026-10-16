# Starting hero runtime

Status: **Implemented bounded component**, ARC-WORLD-015. The [game session](game-session-runtime.md) owns the player
character: its stats in the [character stats owner](character-stats-runtime.md) (ARC-WORLD-012), level, unspent points,
alignment, gold and starting kit, saved in snapshot schema 6, and it answers the PC facts that DLG reply gates read.
A Godot-free reader in `Arcanum.Data` turns one original pre-made PC prototype into its new-game start
([ARC-RES-027](../research/starting-hero-evidence.md), proposals 1–4). It covers the pre-made path of
[FR-CHAR-001](../requirements/characters-skills.md#fr-char-001), the pre-made kit of
[FR-CHAR-007](../requirements/characters-skills.md#fr-char-007), the session wiring of
[FR-CHAR-008/009](../requirements/characters-skills.md#fr-char-008) and the explicit start-character input of
[FR-CONTENT-003](../requirements/content-product.md#fr-content-003).
**The AREA viewer is not wired to it yet** (`tools/terrain-viewer` is reserved by another task): it still starts a
session without a hero and talks with its PROJECT diagnostic PC profile.

Categories as in the research: source claims (S) are read from `alexbatalov/arcanum-ce` at
`a7ff41b300ef712f0e7d088183a3d08957110cdd` (Sustainable Use License, not MIT); nothing is copied. Data claims (D) are
checked by the opt-in real-data tests below. Retail executable parity is NOT_VERIFIED.

## Pre-made hero reader (Arcanum.Data)

`PremadeHeroReader.Read(PremadeHeroSources, basicPrototype)` reads the PC prototype file `basic + 20` (v119, through
the shared object wire reader), the race and female effects from `rules\effect.mes` and the gold and items of its
inventory source set from `rules\InvenSource.mes` (and `rules\InvenSourceBuy.mes`), plus each listed item prototype. The
caller supplies a prototype lookup (`DirectoryPrototypes` over the installation's loose `data/proto`, where all 13 PC
prototypes and their kit items live, SH-02/SH-05) and the three rule tables (the tests resolve them through the
[content stack](../research/content-precedence.md)). It creates no object and runs no script.

The result is the hero **at the start of a new game** (S): `level_set(pc, 1)` keeps the prototype's race, gender,
alignment and age and writes the stat defaults (eight primary stats 8, level 1, 5 unspent points, magick and tech points
0; [level_set](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/level.c#L708-L763),
[defaults](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L115-L144)); the
stat setter re-adds the race effect `64 + race` and, for a woman, effect 330. The stored background (39..50) is display
data and adds nothing (ARC-RES-027 proposal 4). Gold is the set's coin amount and the items are its prototypes in order.
The source then gives each item to the PC through `item_transfer`
([inventory source](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2073-L2164)):
an item whose prototype attaches a `SAP_GET` or `SAP_INSERT_ITEM` script is refused here (`ItemScriptUnsupported`), and
the PC's insert check (carry weight, room) is **NOT evaluated**; a refused item would be destroyed in the source. For the
twelve installed sets no item is refused (D), and the heaviest-kit check against ST 8 is NOT_VERIFIED.

Typed refusals (`PremadeHeroFailure`, fail-closed): missing, ambiguous or invalid prototype (size, wire, trailing bytes,
number); not a PC; race outside 0..10 or gender outside 0/1; prototype state a new PC would carry that is not modelled
(skills, experience, inventory, gold object, bank money, an effect whose cause is neither race nor gender, more than one
stored effect per innate cause); an effect the rules refuse; an inventory set the rules refuse; a coin range or an item
rate other than 100 (`RandomInventorySource`: a range or rate 1..99 needs gameplay randomness, FR-CONTENT-007, and rate
0 never creates its item); a missing, ambiguous or invalid item prototype; an item that is gold or not an item type; an
item with a get or insert script.

**Effect rules** (`EffectRules`, S: [effect_parse](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/effect.c#L360-L481)):
effect N is message N of `rules\effect.mes` (50..399; an absent message is an empty effect, as the source's
zero-initialised table). Tokens split on spaces and commas only, as in the source, so any byte outside printable ASCII
(a tab or line feed included, which would glue two tokens) is `InvalidText`; an attribute keyword (ASCII case-insensitive) from the six
collections in source order (28 stats, 12 basic skills, 4 tech skills, 5 resistances, 8 tech disciplines, 10 special
values), then `+n`/`-n` (add), the same with `%` (percent), `min n` or `max n`. Project admission policy: `*n` and
`/n` are `UnsupportedOperator` (CE stores factor 0, original NOT_VERIFIED, SH-07); an unknown token, a missing operand,
a malformed number (sign rules, 1..9 digits), more than seven changes in one collection or a byte outside printable
ASCII are typed refusals instead of the source's partial effect.

**Inventory source sets** (`InventorySourceRules`, S:
[parser and lookup](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/invensource.c#L167-L330)):
set N is message N, numbered from 1 without gaps, reachable when `0 < N < entry count`. An entry is
`name: min,max rate,proto …`; here the entry must be printable ASCII (the source splits on commas and spaces only, so a
tab or line feed would empty the set), the name 1..69 characters before the first colon and every space-separated token
a pair of digit runs, at most 300 items. A buy line for the set with anything but spaces is refused
(`BuyListUnsupported`): a failing one empties the set in the source and buy lists are not modelled.

## Session hero

`GameSessionDescriptor` takes an optional `SessionHeroDefinition`: PC prototype number, race, gender, the ten stored stats
(eight primary, magick points, tech points), level, unspent points, alignment, the race and gender effects as
`StatEffectDefinition` (only stats the character owner models; `null` when none), gold and the kit as item kinds. The
host maps the reader's result (the mapping lives in the real-data test until the viewer is wired): effect changes on
the 17 primary/derived stats and magick/tech points are kept; changes on skills, resistances, tech disciplines,
special values, level, alignment, age, gender and race are not modelled and dropped (for the pre-made heroes: tech
skills, prowling, dodge, melee, haggle, resistances, critical-hit chance, the bad-reaction adjustment and age, SH-09).

`Create` refuses (`InvalidHero`, before any component is built) a prototype below 1, an undefined race or gender, a
stats list that is not ten values, a stored value outside the race's range (strict, as `SetBase`), level outside
0..51, unspent points outside 0..56, alignment outside −1000..1000 (the source's stat ranges,
[stat.c](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L38-L110)),
an effect the character owner refuses (no modifiers, more than seven, a magnitude above 1000, divisor 0, negative id),
negative gold, a kit item of a type transfers cannot hold (gold, ammunition, key, key ring) and a kit larger than
`MaxPcItems`. The definition is copied (its lists are checked on the copy). Gold and the kit enter the inventory at revision 0: the kit as `kit:1`,
`kit:2`, … (`SessionKitItem`, no origin map), kept apart from the items the PC takes from the world (`QueryPcInventory`
keeps its ARC-WORLD-014 meaning, which the viewer's inventory window reads) and counted with them toward `MaxPcItems`.
A seed that lists a kit entity is `InventoryMismatch`.

| Entry | Contract |
| --- | --- |
| `QueryHero()` | `SessionHeroState` (the saved fields) or `null` without a hero. |
| `QueryHeroStats(context)` | The character owner's `CharacterStatsQueryResult` under the context (base/effective/derived values, applied innate effects), or `null`. |
| `QueryHeroDialogFacts()` | `DialogPcFacts` for DLG gates, or `null`: gender, **effective** intelligence (race and gender effects applied), the source race index 0..10 (CE `RACE_*`). `IsDumb` is false: the source sets it for backgrounds 55, 74 and 75 only ([critter_is_dumb](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L2392-L2419)) and no background is modelled. |
| `QueryGold()` | The PC's gold (0 without a hero). |
| `QueryHeroKit()` | The starting kit in kit order (empty without a hero); in the PC's inventory it precedes `QueryPcInventory`. |

The hero is read-only in this task: no stat purchase, level-up, alignment change, effect added at run time, background
or name. A host that hands `QueryHeroDialogFacts()` to its conversation (for the AREA host, as the `Pc` of its
`AreaConversationProfile`) gets the hero's own gender, intelligence and race gates instead of the diagnostic profile.

## Snapshot schema 6

`hero` is encoded between `party` and `inventory`; `null` for a session without a hero:

```json
"hero":{"prototype":16089,"race":"HalfElf","gender":"Female",
        "stats":{"strength":8,"dexterity":8,"constitution":8,"beauty":8,"intelligence":8,"perception":8,
                 "willpower":8,"charisma":8,"magickPoints":0,"techPoints":0},
        "level":1,"unspentPoints":5,"alignment":0,
        "raceEffect":{"effect":67,"modifiers":[{"stat":"Dexterity","operator":"Add","value":1}, …]},
        "genderEffect":{"effect":330,"modifiers":[{"stat":"Strength","operator":"Add","value":-1}, …]}},
"inventory":{"gold":385,"kit":[{"entity":"kit:1","type":"Weapon","prototype":6049,"name":4021,"description":6029}, …],"pc":[]}
```

(The synthetic test's values; line breaks added.) The effects are saved with their modifiers, so a restore needs no
effect table. Decoding refuses unknown race, gender, stat and operator names (`OutOfRange`), missing or unknown fields,
more than 64 modifiers (`BudgetExceeded`), a negative effect id or gold, a prototype below 1. `Restore` rebuilds the
hero under the same checks as `Create` (`InvalidHero`) and refuses gold or a kit item without a hero, and a kit item that
is not the next `kit:n` (`InvalidSnapshot`).

**Migration decision: none.** Schemas 1..5 are `UnsupportedSchema` (and schema 6 since ARC-WORLD-019, which added
[`random`](session-random-runtime.md#snapshot-schema-7), and schema 7 since ARC-COMBAT-002, which added
[`life`](life-state-runtime.md#snapshot-schema-8)); a schema-5 quick save loads as "written by an
incompatible version" with the running session kept, and the next F7 overwrites it. The viewer's sessions have no hero,
so nothing is lost.

## Evidence

- Synthetic: [HeroStateTests](../../tests/Arcanum.Session.Tests/HeroStateTests.cs) (start, effective stats, facts,
  every `InvalidHero` input and the range edges, copy, the kit budget and seed clash, canonical text, restore and decode
  refusals, gender/IQ/RA gates of a synthetic DLG) and [PremadeHeroTests](../../tests/Arcanum.Data.Tests/Characters/PremadeHeroTests.cs)
  (effect and inventory-source grammars and refusals, synthetic v119 PC/item prototypes, every reader refusal, the
  directory lookup).
- Real data (opt-in `RealData`): [PremadeHeroRealDataTests](../../tests/Arcanum.Data.Tests/Characters/PremadeHeroRealDataTests.cs)
  (`DAT_ROOT`): all twelve pre-made heroes match the ARC-RES-027 table (race, gender, set, gold, items, scheme,
  alignment 0, age 20, background 39..50) and SH-09's half-elf, half-ogre and female effects; prototype 16066 has no set.
  [StartingHeroTests](../../tests/Arcanum.TerrainViewer.Tests/StartingHeroTests.cs) (`ARC_AREA_CONVERSATION_GAME_ROOT`):
  hero 16069 (half-elf man) starts with effective DX 9, BE 9, CN 7, magick points 1, 385 gold and three kit items;
  16070 (half-ogre man) has intelligence 4; 16075 (human woman) has ST 7, CN 9 and 300 gold. Virgil's original first
  talk offers the half-elf replies 4 and 7 at node 1 and walks the 13-node path of the viewer test; the half-ogre gets
  only reply 5 and walks the low-intelligence path 1, 9, 220, 222, 225, 228, 74; the woman gets reply 26 at node 24 and
  replies 37 and 45 at node 35. A save and load keep the hero, gold and kit.

## Not implemented

The pre-made hero global flags: the main menu sets flags 2350..2361 (`MainMenu.mes` 551..562) so that the chosen hero's
flag is true and the others false (ARC-RES-025 CC-07). Neither the reader nor the session sets them; a host that starts
a hero must set the chosen flag in the session's GF descriptor (the real-data test starts from the plain new-game flags,
and whether Crash Site scripts read them is an open ARC-RES-025 question).

The auto-level scheme replay (the hero keeps its 5 points unspent, so its sheet is **not** the original level-1 sheet;
ARC-RES-027 open question), the new-character path (prototype 16066 with the block-0 background, clothes and skill
kits), backgrounds and their context effects, skills, spells, tech, hit-point and fatigue pools (the hero's life state is
[ARC-COMBAT-002](life-state-runtime.md), with pools 0 as every pre-made prototype stores them), item wielding
(`item_wield_best_all`: the kit is carried, not worn), the PC object and name, the viewer wiring and a hero picker.
When the viewer starts a hero, its inventory window and insert check must read `QueryHeroKit()` too: the ARC-UI-004 host
builds the PC list from `QueryPcInventory()` alone.
