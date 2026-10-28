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
The AREA viewer starts a pre-made hero on request since ARC-WORLD-027 ([viewer start](#viewer-start-arc-world-027));
without `--hero` it starts a session without a hero and talks with its project diagnostic PC profile.

Categories as in the research: source claims (S) are a reading of the original's functions at the cited addresses,
evidence level F0 (not checked in the executable) unless the
[verification ledger](../research/original-verification-ledger.md) lists them. Data claims (D) are checked by the opt-in
real-data tests below. Retail executable parity is NOT_VERIFIED.

## Pre-made hero reader (Arcanum.Data)

`PremadeHeroReader.Read(PremadeHeroSources, basicPrototype)` reads the PC prototype file `basic + 20` (v119, through
the shared object wire reader), the race and female effects from `rules\effect.mes` and the gold and items of its
inventory source set from `rules\InvenSource.mes` (and `rules\InvenSourceBuy.mes`), plus each listed item prototype. The
caller supplies a prototype lookup (in play `ContentPrototypes.Read`, the base-source prototypes of the content stack the
original reads, ARC-DATA-026; on the installed original all 13 PC prototypes and their kit items are loose in `data/proto`,
SH-02/SH-05; `DirectoryPrototypes` over one folder is for synthetic fixtures) and the three rule tables (the tests resolve them through the
[content stack](../research/content-precedence.md)). It creates no object and runs no script.

The result is the hero **at the start of a new game** (S): setting the PC's level to 1 (0x4A6F00) keeps the prototype's race, gender,
alignment and age and writes the stat defaults (eight primary stats 8, level 1, 5 unspent points, magick and tech points
0; the stat defaults); the
stat setter re-adds the race effect `64 + race` and, for a woman, effect 330. The stored background (39..50) is display
data and adds nothing (ARC-RES-027 proposal 4). Gold is the set's coin amount and the items are its prototypes in order.
The original then gives each item to the PC through the item transfer (0x4617F0), from the inventory source function
(0x463E20):
an item whose prototype attaches a `SAP_GET` or `SAP_INSERT_ITEM` script is refused here (`ItemScriptUnsupported`), and
the PC's insert check (carry weight, room) is **NOT evaluated**; a refused item would be destroyed in the original. For the
twelve installed sets no item is refused (D), and the heaviest-kit check against ST 8 is NOT_VERIFIED.

ARC-WORLD-101: `PremadeHero.PlayerName` is the prototype's player name (field 0x10E, the PC block's one string field, v119 slot 86) as
its stored bytes up to the terminating zero (empty when unset); every installed pre-made prototype stores one (`RealData` test). The
viewer gives it as the hero's name to the seed of a generated-once container
([container loot window](barter-runtime.md#container-loot-window-arc-world-101)); the session does not hold it.

Typed refusals (`PremadeHeroFailure`, fail-closed): missing, ambiguous or invalid prototype (size, wire, trailing bytes,
number); not a PC; race outside 0..10 or gender outside 0/1; prototype state a new PC would carry that is not modelled
(skills, experience, inventory, gold object, bank money, an effect whose cause is neither race nor gender, more than one
stored effect per innate cause); an effect the rules refuse; an inventory set the rules refuse; a coin range or an item
rate other than 100 (`RandomInventorySource`: a range or rate 1..99 needs gameplay randomness, FR-CONTENT-007, and rate
0 never creates its item); a missing, ambiguous or invalid item prototype; an item that is gold or not an item type; an
item with a get or insert script.

**Effect rules** (`EffectRules`, S: the effect parser (0x4E9C20)):
effect N is message N of `rules\effect.mes` (50..399; an absent message is an empty effect, as the source's
zero-initialised table). Tokens split on spaces and commas only, as in the source, so any byte outside printable ASCII
(a tab or line feed included, which would glue two tokens) is `InvalidText`; an attribute keyword (ASCII case-insensitive) from the six
collections in source order (28 stats, 12 basic skills, 4 tech skills, 5 resistances, 8 tech disciplines, 10 special
values), then `+n`/`-n` (add), the same with `%` (percent), `min n`, `max n`, `*n` (multiply) or `/n` (divide): the
factor is the number after the sign, as the original reads it (SH-07, LM9; decision 7, ARC-WORLD-035), and `/0`, which would
divide by zero when the effect applies, is `ZeroDivisor` in both rule modes. Project admission policy: an unknown token, a missing operand,
a malformed number (sign rules, 1..9 digits), more than seven changes in one collection or a byte outside printable
ASCII are typed refusals instead of the source's partial effect.

**Inventory source sets** (`InventorySourceRules`, S:
parser and lookup (0x4BF5D0, 0x4BF640, 0x4BF7B0)):
set N is message N, numbered from 1 without gaps, reachable when `0 < N ≤ entry count` (ARC-WORLD-093 corrects the earlier
"below the count", ledger BT9). An entry is
`name: min,max rate,proto …`; here the entry must be printable ASCII (the source splits on commas and spaces only, so a
tab or line feed would empty the set), the name 1..69 characters before the first colon and every space-separated token
a pair of digit runs, at most 300 items. A set whose buy line or loot line fails in the source (`InventorySourceBuyRules`: a bad
buy number, a loot item outside the basic ranges or without a prototype file, …) is `Emptied`, as the source empties its items and buy
list (the coins read before the failure stay there, RK6); message n must be the n-th message, as the buy rules read it; a buy line
that parses is allowed (ARC-WORLD-093; it was refused as `BuyListUnsupported` before).

## Session hero

`GameSessionDescriptor` takes an optional `SessionHeroDefinition`: PC prototype number, race, gender, the ten stored stats
(eight primary, magick points, tech points), level, unspent points, alignment, the race and gender effects as
`StatEffectDefinition` (only stats the character owner models; `null` when none), gold, the kit as item kinds and, since
ARC-WORLD-069, the background id (`Background`, 0 none; a pre-made hero's 39–50, [backgrounds](equipment-runtime.md#backgrounds-arc-world-069)) and,
since ARC-UI-033, the portrait (`Portrait`, field 0xE7: a pre-made prototype's, or the new-character window's choice; 0 none,
negative refused; the session keeps it with the hero and saves it from schema 56, as it saves the player name that
`SetHeroPlayerName` stores, empty for every hero until the window's Accept writes it). The
host maps the reader's result (the mapping lives in the real-data test until the viewer is wired): effect changes on
the 17 primary/derived stats and magick/tech points are kept; since ARC-SKILLS-002 the changes on basic and tech skills are
kept too, as the hero's innate skill changes (`InnateSkills`, [hero skills](hero-skills-runtime.md)); changes on resistances, tech disciplines,
special values, level, alignment, age, gender and race are not modelled and dropped (for the pre-made heroes: resistances,
critical-hit chance, the bad-reaction adjustment and age, SH-09).

`Create` refuses (`InvalidHero`, before any component is built) a prototype below 1, an undefined race or gender, a
stats list that is not ten values, a stored value outside the race's range (strict, as `SetBase`), level outside
0..51, unspent points outside 0..56, alignment outside −1000..1000 (the original's stat ranges), a background outside 0..999,
an effect the character owner refuses (no modifiers, more than seven, a magnitude above 1000, divisor 0, negative id),
negative gold, a kit item of a type the kit cannot hold (gold, ammunition: no unit count, key, key ring) and a kit larger than
`MaxPcItems`. The definition is copied (its lists are checked on the copy). Gold and the kit enter the inventory at revision 0: the kit as `kit:1`,
`kit:2`, … (`SessionKitItem`, no origin map), kept apart from the items the PC takes from the world (`QueryPcInventory`
keeps its ARC-WORLD-014 meaning, which the viewer's inventory window reads) and counted with them toward `MaxPcItems`.
A seed that lists a kit entity is `InventoryMismatch`.

| Entry | Contract |
| --- | --- |
| `QueryHero()` | `SessionHeroState` (the saved fields) or `null` without a hero. |
| `QueryHeroStats(context)` | The character owner's `CharacterStatsQueryResult` under the context (base/effective/derived values, applied innate effects), or `null`. |
| `QueryHeroDialogFacts()` | `DialogPcFacts` for DLG gates, or `null`: gender, **effective** intelligence (race and gender effects applied), the original's race index 0..10. `IsDumb` is false: the original sets it for backgrounds 55, 74 and 75 only (the dumb test, 0x45FC00), which is not applied yet (the hero's background id is held since ARC-WORLD-069; a pre-made hero's is 39..50). |
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
  replies 37 and 45 at node 35. A save and load keep the hero, gold and kit. Since ARC-WORLD-027 the conversation host
  takes those facts from the session itself, and the viewer's `AreaStartingHero.Load` reads all twelve heroes from the
  installed game folder with flags 2350..2361 in order, the table's gold and kit sizes and their genders; the half-ogre's
  session has intelligence 4 and flag 2353 true.
- Viewer (ARC-WORLD-027): [AreaStartingHeroTests](../../tests/Arcanum.TerrainViewer.Tests/AreaStartingHeroTests.cs)
  (the hero list, `--hero` parsing, the flags, a session with the hero's flag through a save and load, refused loads,
  the window's kit lines and gold, the HUD line); native smoke `ARC-WORLD-027 native-smoke hero PASS` with `--hero`.

## Viewer start (ARC-WORLD-027)

`AreaStartingHero` (`tools/terrain-viewer/Core/AreaStartingHero.cs`) is the AREA host of this contract. `--hero NAME`
names one of the twelve heroes by race and gender (main-menu order, basic prototypes 16067..16078; the list is shared
with the launcher and checked by `test_launcher_sync.py`). During the background load it opens the module content stack
of the game folder that holds the module archive (the description tables' rule), reads the hero with
`PremadeHeroReader` over the stack's prototypes (`ContentPrototypes`, ARC-DATA-026) and the three rule tables, and maps it with
`AreaStartingHero.Definition` (the mapping above, moved out of the real-data test). Refusals are typed
(`AreaHeroStatus`: module outside a game folder, mount or stack failure, a table absent or invalid, a reader refusal,
an invalid flag message) and fail the viewer's load. Each table comes from the sources of the phase the original reads
it in (ARC-DATA-027, exe-checked, [ledger](../research/ledger/data.md#load-phases-arc-data-027)):
`rules\effect.mes`, `rules\InvenSource.mes`, `rules\InvenSourceBuy.mes`, `mes\MainMenu.mes`, `rules\xp_level.mes` and
`rules\xp_quest.mes` at game start, so from the first base source (a module copy is never read; a table only a module
holds is absent, detail `<path> NotInPhase`); `rules\gamequest.mes` at module load, so the stack winner. The installed module holds none of the
game-start tables, so the installed heroes read unchanged.

**Pre-made hero flag** (S, and checked in the original executable): picking a hero, the main menu's pre-made hero step
(0x545B60) reads `MainMenu.mes` messages 551 + i for each hero index i, turns each into a flag number with `atoi` and
writes 1 for the chosen hero and 0 for the others ([ledger](../research/ledger/character.md#skills-levels-and-character-arc-res-044-arc-res-031-arc-res-027)).
The viewer starts from the original new-game flags (all 0..3199 known false, so the 0 writes change nothing) with the
chosen hero's flag true (`AreaStartingHero.NewGameFlags`); on the installed original that is flag 2350 + i (CC-07).
Project admission: the chosen message must be one to five ASCII digits (space-padded allowed) naming a flag below 3,200;
anything else is `FlagInvalid` instead of `atoi`'s partial or zero value. The original writes any non-negative result, flag
0 included (ledger row Pre-made flags). Whether Crash Site scripts read these flags stays the open
ARC-RES-025 question. The main menu's gate on prototype 16075 and its ART, which makes the four female heroes selectable, is not evaluated
(it is open on the pinned installation, SH-04).

The session is created with that hero (`AreaTransitionCoordinator.CreateSession(…, hero)`); the snapshot schema is
unchanged (hero and flags are already saved). The conversation host takes its PC facts from the session's hero
(`QueryHeroDialogFacts()`), also when F8 rebinds it to a loaded session; a session without a hero keeps the given
profile's facts. The inventory window lists `QueryHeroKit()` before `QueryPcInventory()` with the description number's
text and shows `QueryGold()`; the HUD's PC count includes the kit. Since ARC-UI-007 the load also reads the experience
tables and the session gets them (`AreaHeroLoad.Experience`; a missing or invalid table refuses the load), and `C` shows
the hero's [character sheet](../rendering/terrain-viewer.md#character-sheet-arc-ui-007). Not done: the hero's own body ART
(the actor keeps the AREA profile's fixed human body), the PC's name, wielding the kit, and the new-character path.

## Not implemented

The auto-level scheme is [ARC-WORLD-036](advancement-runtime.md): with the scheme data the session runs the hero's scheme
at creation, as the original's player creation does, so it spends the 5 starting points (without the data they stay
unspent). Not implemented: the new-character path (prototype 16066 with the block-0 background, clothes and skill
kits), backgrounds and their context effects, skills, spells, tech, hit-point and fatigue pools (the hero's life state is
[ARC-COMBAT-002](life-state-runtime.md), with pools 0 as every pre-made prototype stores them), the PC object and name, and
the hero's body ART in the viewer. Since ARC-WORLD-034 the viewer's start wields the kit's best items
([equipment](equipment-runtime.md#best-items-arc-world-034)); a kit item with wield scripts or item spells stays carried.
The viewer's insert check accepts everything (ARC-UI-004), so the kit's weight is not checked there either.
