# Equipment runtime (ARC-WORLD-028)

Status: **Implemented bounded component**, ARC-WORLD-028 (2026-10-17); the best items of a slot and the pre-made start's wield
ARC-WORLD-034 (2026-10-18, [Best items](#best-items-arc-world-034)); the item events of a move ARC-SCRIPT-042 (2026-10-28,
[Wield events](#wield-events-arc-script-042)). The hero's wear slots in the
[game session](game-session-runtime.md): which item may take which slot, wielding and wearing kit items and items taken
from the world, and what the worn items give the [attack resolver](attack-resolver-runtime.md) (ARC-COMBAT-001) and the
[turn-based round](turn-based-round-runtime.md) (ARC-COMBAT-003). The rules are pure functions in
`Arcanum.Simulation.Equipment.EquipmentRules`; the session holds the worn items (snapshot schema 12). Requirements:
[FR-ITEM-005](../requirements/items-economy.md#fr-item-005) (slot legality, wield and remove),
[FR-ITEM-006](../requirements/items-economy.md#fr-item-006) (weapon and armour item values into combat) and the light part
of [FR-ITEM-007](../requirements/items-economy.md#fr-item-007). The viewer uses it for the pre-made start and, since ARC-UI-015,
for the inventory window's paper doll ([viewer](../rendering/terrain-viewer.md#inventory-window-pickup-and-loot-arc-ui-004))
([Not ready](#not-ready)).

## Evidence

- The original's functions, read first: the item functions (wear check, wield, unwield, remove check, slot of an item,
  armour class adjustment, race size table, effective power and magic adjustment, weapon speed, range, minimum strength,
  unarmed damage, light), the object armour class and resistance, the paperdoll UI's own checks and the TIG item art bits.
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically by two independent readers at the same
  functions per the [original executable check](../research/original-executable-check.md); rows EQ1–EQ14 in the
  [verification ledger](../research/ledger/items.md#equipment-arc-world-028). The original does what
  that first reading describes except in two rows: one changes a rule (EQ1: the original refuses a shield with one crippled arm
  too) and one changes only stored flags (EQ12: the original keeps only the critter's largest light flag). EQ5 is a bug of the original without a session effect
  (the medallion slot recalculates the animation speed of the item, not the wearer), so no rule-mode switch.
- Installed original (opt-in, below): all 150 weapon, 251 armour and 148 generic prototypes become wear values.

## Rules (`EquipmentRules`)

| Rule | Value |
| --- | --- |
| Slots | helmet 1000, ring 1001 and 1002, medallion 1003, weapon 1004, shield 1005, body armour 1006, gauntlets 1007, boots 1008 |
| Slot of an item | a weapon the weapon slot; armour by the coverage of its inventory art (torso, shield, helmet, gauntlets, boots, ring, medallion); a generic item with the torch/shield flag the shield slot; anything else none |
| Wear check, in order | a polymorphed or "body of" wearer: not wieldable; a magickal item (complexity > 0) and Magick Allergy; broken (destroyed art bit; since ARC-WORLD-053 the session's [durability](item-durability-runtime.md) record of the item wins over the host's data, also in `EquipBest`, and a first break takes a worn item off); not the item's slot (either ring slot takes a ring); weapon: a two-handed weapon with a fixed hand count is refused next to a shield and with any crippled arm, any other weapon only with both arms crippled; shield: refused next to such a weapon and with any crippled arm (EQ1); body armour: the race's size (dwarf, gnome, halfling small; half-ogre, ogre large; else medium), male-only before female-only, unique NPC art; then the wearer's art for a weapon, an armour shield or body armour (the host's answer). Minimum strength is never checked. |
| Leaving a place | an item with the no-drop flag never leaves (wielding it is refused too); a worn item may always be taken off (the original's hexed-item check is a stub, EQ2) |
| Effective power | `(complexity + aptitude) / 2` truncated toward zero, clamped to `0 .. complexity` on the item's side |
| Magic adjustment | magickal `value × power / complexity`, technological `value − value × power / complexity`, neutral unchanged |
| Worn armour class | each worn armour item's AC adjustment + its magic AC adjustment; the caller adds base AC, effects and the AcAdjustment stat (`AttackRules.ArmorClass`) |
| Worn resistances | per type the adjustment + the magic adjustment of each worn armour item; the caller adds the base, the poison bonus of CN, effects and the 0..95 clamp |
| Light | each worn item's light sizes shrink by 1, 2 or 3 at an effective power ratio ≤ 75, 50, 25 %; the largest remaining size |
| Weapon | bare hands: `CombatWeapon.Unarmed(gauntlet unarmed bonus)`; else the stored damage, to-hit, charts, flags and condition, with range (never below 0), minimum strength and every magic adjustment for the wielder |
| Minimum strength | stored + magic adjustment; unless the hand count is fixed, two-handed +4 next to a shield, one-handed −2 without one |
| Weapon speed | speed factor + magic speed adjustment + 5 with apprentice training, 10 bare-handed; attack cost from `TurnBasedRules.AttackCost` |

`EquipmentRules.Evaluate(worn, aptitude, weaponTraining)` returns all of it as `EquipmentCombat`. `IsValid` bounds an
item to what the attack resolver accepts (numbers within ±1000, speeds within ±495, condition 0..100 000, weapon damage
and unarmed bonus not negative, the blocks of its kind).

## Session domain (`Arcanum.Session`)

| Entry | Contract |
| --- | --- |
| `QueryEquipment()` | The worn items in slot order: slot, the PC item entity (`kit:n` or a taken item), its saved values. |
| `QueryHeroEquipmentCombat(training)` | `EquipmentRules.Evaluate` for the hero's effective magick/tech aptitude; null without a hero. Without a training argument the hero's training in the wielded weapon's skill (ARC-SKILLS-002, [hero skills](hero-skills-runtime.md)); an explicit one overrides. |
| `Equip(expected, entity, slot, item, artCheck, insertCheck, scripts?, triggers?)` | The original's wield (0x464AE0). Refusals, in order: input, version, no hero (`EquipmentUnavailable`), an item the hero does not hold (`ItemNotAvailable`) or another kind than the saved one (`InventoryMismatch`); NoChange when already in that slot; a `SAP_INSERT_ITEM`, `SAP_REMOVE_ITEM`, `SAP_WIELD_ON` or `SAP_WIELD_OFF` script (`ItemScriptUnsupported`) or item spells without an item trigger host that supports the item, or a move of an item that is a run's source or (ARC-MAGIC-022) a worn item or occupant with the item spells mark without one (`ItemEffectUnsupported`; with a host the unwear and wear executors run after the move and an item with spells is marked, [item spell triggers](item-spell-triggers.md), ARC-MAGIC-020); the wear check with the hero's race, gender, aptitude and crippled arms from the life domain, then `artCheck` (`WearRefused` with the `EquipmentRefusal`); the no-drop flag of the item, then of the slot's occupant (`WearRefused`, `NotDroppable`); the insert check of the occupant going back to the grid (`InsertRefused`; `TechnologyRefused` for a technological occupant of a Technophobia hero, [backgrounds](#backgrounds-arc-world-069)). A ring in the other ring slot moves. One revision for the move; an item trigger host's runs add their own (ARC-MAGIC-020). |
| `Unequip(expected, slot, insertCheck, scripts?, triggers?)` | The original's unwield to the grid: NoChange for an empty slot; an item that is a run's source or (ARC-MAGIC-022) carries the item spells mark needs an item trigger host (`ItemEffectUnsupported`), whose unwear executor runs after the move (ARC-MAGIC-020); the no-drop flag, then the insert check (`TechnologyRefused` for a technological item of a Technophobia hero). One revision for the move; an item trigger host's runs add their own (ARC-MAGIC-020). |

`SessionWearableItem` is what the host offers: the saved kind, script points, whether the item has item spells, and its
`EquipmentItem` values. `SessionEquipmentFacts.FromCombatFields` builds the values from the stored combat fields
([ARC-DATA-021](../formats/combat-fields.md), which now also reads the current art, inventory art and use-art fragment).
The host's callbacks must not call back into the owner: a call that moves the revision makes the step `StaleVersion`.
The insert check is asked as the source asks it: on a swap with both the new item and the occupant already out of the grid
(the room the new item leaves counts); on unequipping with the item still worn (the source counts its weight twice near
the carry limit). The facts it gets carry only the saved kind; weight and grid size are the host's to look up.

**Combat effects.** The hero's life facts take the worn armour's normal resistance (clamped 0..95; a pre-made PC's base
resistance and resistance effects are not session state), so a worn item now reduces the round's overexertion fatigue
and the poison tick. The resolver and the round's attack cost read `QueryHeroEquipmentCombat`; no host calls them yet.

A worn item stays in its PC list (`QueryHeroKit`, `QueryPcInventory`): the session does not reorder the lists as the
source's remove-and-insert does, because no rule reads the order of the PC's own list.

## Snapshot schema 12

`GameSessionSnapshot.Equipment`, JSON between `inventory` and `random`, slots strictly ascending, entities unique:

```json
"equipment":[{"slot":"Weapon","entity":"kit:1","item":{"kind":"Weapon","complexity":0,"noDrop":false,"broken":false,
  "uber":false,"light":0,"usesShieldLocation":false,"hp":20,"maxHp":20,"weapon":{…},"armor":null}}]
```

(The synthetic test's values; the weapon block holds its flags, skill, five damage ranges and magic damage, to-hit,
speed, range and minimum strength with their magic adjustments, ammunition and the critical charts and chances; the
armour block coverage, sizes, gender flags, AC, five resistances and their magic adjustments and the unarmed bonus.) Since schema
43 each worn item also carries `scripts` (its item event points) and since schema 49 `itemSpells` (the item spells mark,
[item spell triggers](item-spell-triggers.md#saved-state-arc-magic-022)).
The codec checks shapes, names and ranges; `Restore` refuses (`InvalidEquipment`) a worn item without a hero, an entity the
hero does not hold or of another type, an item that does not belong in its slot or is invalid, an entity in two slots,
and a fixed two-handed weapon beside a shield. **Migration decision: none.** Schema 11 and older are refused; a quick save
of schema 11 loads as "written by an incompatible version" and the next F7 overwrites it (the viewer wears nothing).

## Best items (ARC-WORLD-034)

The original asks for a critter's best items in every slot (ledger rows WB1–WB8): a new game does it for the pre-made hero
before the game starts, and the AI does it for NPCs. `EquipmentChoice` holds the pure rules, the session runs the steps.

| Rule | Value |
| --- | --- |
| Worth (`Worth`, `Identified`) | an unidentified magickal item (complexity > 0 without the identified flag) 300; else the stored worth, at least 2 |
| Thrown damage (`ThrownDamage`) | a throwable weapon its own ranges; anything else normal `(weight + 99) / 100 .. (weight + 49) / 50` from the caller's weight |
| Weapon score (`WeaponScore`) | null without the ammunition for one use; else the sum over the five types of the average of the wielder's damage range (`AttackRules.WeaponDamageRange`: melee DamageBonus, magic adjustments, caps), times the attack effectiveness of the weapon's skill / 100; a skill at level 0 other than melee, or an effectiveness of 0, counts −1 |
| Choice (`ChooseBest`) | weapon slot: the eligible weapon with the highest score, a tie to the later candidate (the original compares object handles, which the session has not; candidates come in creation order); with a target the best weapon that reaches it wins; other slots: the eligible item worn nowhere with a strictly higher worth than the kept item and every earlier one |

`GameSessionOwner.EquipBest(expected, items, artCheck, insertCheck)` runs the slots 1000..1008 in order on a plan and
commits once (one revision; NoChange when the worn items end as they were): a slot's item that still passes the wear check
stays as the worth to beat, except in the weapon slot; any other comes off (no-drop or a refused insert check keeps it on);
then the choice over the hero's items in creation order (the kit, then the items taken from the world), wielded as by
`Equip`. The host gives every held item's facts (`SessionWearableLookup`; `SessionEquipmentFacts.Wearable` builds them from
the stored combat fields, which now also read the script points) and answers the art and insert checks. The hero's weapon
skill levels (melee, bow, throwing, firearms) are its session skills since ARC-SKILLS-002 ([hero skills](hero-skills-runtime.md)), and since
ARC-WORLD-066 its ammunition is the units of its stack of each type ([ammunition](inventory-runtime.md#ammunition-arc-world-066)). `SessionEquipBestStep` lists what happened per slot. Refused before any effect: version, no hero, invalid or
mismatched facts, a callback that moved the session. **Project policy:** a chosen item with a move script or item spells
is not wielded (the hero's call runs neither; the original wears it and runs them): its step names it, the slot keeps what it
held when the call began (a weapon taken off for the choice goes back on) and the call goes on.

ARC-UI-036: `GameSessionOwner.WieldStartBest(expected, items, artCheck, insertCheck)` is the new character's start (the original's
menu close 0x5412E0 with the best-items flag, [ledger OS5](../research/ledger/character.md#opening-shop-arc-ui-036)): the same plan,
refusals, policy and single revision, but only the weapon slot (1004) and then the body-armour slot (1006), each only when it is
empty. `GameSessionOwner.CheckHeroWear(item, slot)` answers the wear rules above for an item the hero need not hold, with the hero's
worn items (the opening shop's kit asks it for the clothes it made, OS3); the art lookup stays the caller's.

The AREA viewer's pre-made start (`--hero`, ARC-WORLD-027) runs it after creating the session, with the kit's facts read
from the hero's content prototypes and yes to the art and insert checks (one diagnostic body art, no grid); the HUD shows
`worn N`. Installed original (opt-in, `ARC_AREA_CONVERSATION_GAME_ROOT`): every pre-made hero starts with a weapon, body
armour and boots on, except the human woman, whose body armour 8220 carries wield-on and wield-off scripts and stays in the
pack.

ARC-COMBAT-1720 (ledger PI5, OH3): `GameSessionOwner.WieldBestWeapon(expected, items, artCheck, insertCheck, target)` is the hero's re-wield after
its weapon wore out (0x4B83E0 → 0x465170): the weapon slot only, the same plan, refusals, policy and single revision, with the target the best-item
call is given (`SessionWieldTarget`: its cell distance and magnetic inversion): an eligible weapon that reaches the target wins over a stronger one that
does not, and with no usable weapon the slot stays empty (NoChange). The gates of 0x4B83E0 are the host's: the "auto switch" setting (the settings
file's `auto switch` line, default off: the original never registers it) and the lost weapon the PC's own; the viewer asks right after an attack whose
wear step (`SessionWearStepKind.AttackerWeapon`) broke or deleted the hero's weapon, on a critter or an object. ARC-COMBAT-1738 adds the other two callers: an attack the ammunition check refused (`AttackRefusal.OutOfAmmunition`, 0x4B3170, the struck object as the target)
and a strike that took the struck hero's weapon (0x4B4390: the "drop weapon" critical that dropped it, the "destroy weapon" and "explode weapon" criticals, the "damage weapon" critical that broke it; `AreaHeroWeaponLoss`, the attacker as the hint) from an NPC's hit, arrow or throw (`AiHit`). Not built: a critical self-hit of the hero's own attack, a hit by a spell, a trap or a script, and the attacker's magnetic inversion for the hint (false); the options window's Auto Switch Weapons line writes the setting at once.

Since ARC-WORLD-103 the same plan dresses an NPC at the end of its regeneration (0x463E20 → 0x4654F0, ledger RK15), with the NPC's
wearer and chooser from the host and its list in place of the hero's items; the session then keeps what the NPC wears
(`inventory.npcWorn`, schema 57), which replaces its map data's inventory locations
([NPC best items after a regeneration](barter-runtime.md#npc-best-items-after-a-regeneration-arc-world-103)).

## NPC wear in combat (ARC-WORLD-106)

The viewer's NPC combat values read what the session's NPC wears, not its map data. `AreaCombatHost.Worn(session, map, npc)` is the
NPC's list as `QueryMapInventory` shows it (the regeneration's record over the map data's inventory locations; the items given to or
made for it; never a taken, dropped or destroyed one) with a wear-slot location (1000..1008), without a broken item (the original takes
an item off at its first break). An item's fields are its object row's on that map, else on another covered map (a taken item the hero
gave away), else its prototype's stored fields (a made item, an item a regeneration created, a hero kit item given away, as the
regeneration's wielder reads them; the viewer passes the barter data's prototypes, so before they load such an item is skipped: NOT
READY). A worn item whose fields give no durability values, or two in one slot, fail the build (`InvalidWornItem`); the event world's
life facts then fall back to the map data's worn items, so the NPC's life timers keep running.

`AreaCombatHost.Build(session, map, npc)` builds the NPC with `SessionCritterCombat.FromWorn` (each item in the slot the session names,
its stored location not read) and rebuilds when that set (slot, item, prototype) or the session changes; the worn sets are kept per
session revision. It serves the AI host (what the NPC strikes with, its reach and the
weapon kind the approach reads), the hero's attack target (armour class, resistances and light), the life bars, the cast map and the
event world's life facts (the poison tick's normal resistance; the viewer gives the host its shown session). `AreaCombatHost.Critter`
keeps the map data's worn items for the regeneration's wearer and chooser, which do not depend on them. The hit sound's body armour
(`WornArmor`) and the cast host's NPC item lookup (`NpcItemSpells`, a made item by its prototype) read the same set.

An NPC's firearms level is its stored tech skill (index 1), read as the original's tech level reads it for a PC or an NPC alike
(0x4C65E0, ledger [NF1](../research/ledger/skills.md#npc-firearms-level-arc-world-106)): four per point, 0..20, capped by its effective
perception; the training is the stored bits. The best-items choice scores a firearm by it (WB3: a level of 0 scores −1) and the AI's
attack values report it (NPC ranged attacks stay refused). The NPC's stored effects are not applied, as for its basic skills. Tests:
`AreaNpcSessionWearTests`, `NpcAttackStateTests`.

## Body armour art (ARC-WORLD-112)

The body a critter is drawn with wears its body armour, as the original sets the critter's art when an item enters or leaves the
body-armour slot 1006 (ledger [BA1–BA6](../research/ledger/items.md#body-armour-art-arc-world-112)). `BodyArmorArtRules.WornArt`
(0x465AE0) gives the art: with an item, the current art with the armour part of the item's use art (item field 0x5F, bits 6..9) and
that use art's palette (bits 4..5); with none, armour part 0 (underwear) and the palette of the critter's base art. A part the art
id cannot hold (9..15 for a critter, 8..15 for a monster, any but 0 for a unique NPC) fails, and the original's wear check refuses
that item as not wieldable (BA6). No other slot changes the body: helmet, gauntlets, boots, rings and the medallion are not drawn on
it, and the weapon and shield slots take the weapon art (not built here). `SessionWearableItem.UseArt` carries the item's use art
from its stored fields (`SessionEquipmentFacts.Wearable`); the session saves none of it, so there is no schema change.

The AREA viewer's hero (ARC-UI-039's `HeroBody`) wears the session hero's worn body armour: `HeroBody.Wearing(useArt)` sets the
body's armour part and palette, and the swap draws the new body at the next frame after a wear, a take-off, a new game or a load.
The file the id names is the art owner's rule N1 (plate parts 4 and 6 draw the sexless body, elf as human and halfling as dwarf). An
installed observation: the body armour in every pre-made hero's kit loads (clothes, part 8, for most; the robe 8114, part 5 palette
2, for the human man and the elf woman). Not ready: a made item's use art is not read, nor a taken item's whose map rows the inventory
host no longer holds (after a transition to another map or a load there); the hero is drawn bare in it. A worn body whose
ARTs do not read is drawn bare; NPCs and companions keep their map art (the session's NPC wear, ARC-WORLD-103/106, does not redraw
them); the viewer's art check still answers yes, so an item whose armour art is missing is worn and drawn bare. Tests:
`BodyArmorArtRulesTests`, `HeroBodyArtTests`, `EquipmentStateTests`; native smoke `ARC-WORLD-112 native-smoke body armour PASS`
(the hero smoke takes the body armour off and wears it again and checks the drawn frame each time).

## Backgrounds (ARC-WORLD-069)

The hero carries its background id in the session (`SessionHeroState.Background`, snapshot schema 36; a pre-made hero's comes from
its prototype, 39–50, through `SessionHeroDefinition.Background`; 0 none), read in the executable
([ledger BG1–BG8](../research/ledger/character.md#backgrounds-arc-world-069)). `QueryHeroBackground()` gives it. Only a
PC has a background (the original's reader answers 0 for any other object), so an NPC is never refused. Two rules read it:

- **Magick Allergy (69, BG3).** The hero as a wearer is `MagickAllergy`, so the wear check refuses a magickal item (complexity above
  0) with `WieldMagicItems` (the original's code 18, `mes\item.mes` 118), for `Equip` and the best-items choice (which skips it).
- **Technophobia (72, BG4).** The insert check's background part (`SessionBackgrounds.RefusesCarry`) refuses an item of negative
  complexity for the hero (`InsertRefused` with `SessionInsertVerdict.TechnologyRefused`, the original's code 15, `item.mes` 115),
  after the host's weight answer and before its room answer: on pickup (a placed seed item taken back too, while its origin map's seed is attached), loot, negative `IN`, an
  assembly product (`SessionAssemblyResult.Verdict`: since ARC-WORLD-070 the refused product lies on the crafter's tile and the
  assembly completes, as in the original) and the unwield (`Unequip`, the item a swap in `Equip` or the best-items
  choice takes off: a worn technological item stays worn). All-or-nothing: the refusal comes from the insert check, before any
  effect. The session applies it where it knows the item's complexity (a seed item's facts, a worn item's equipment facts, a
  product's component facts, 0 when the host gives none). Since ARC-WORLD-083 a made item keeps its complexity (`SessionMadeItem.Complexity`,
  snapshot schema 41) through every move, as the original's object keeps field 0x60 (ledger MC1): an assembly product (the hero's or
  an NPC crafter's) takes its prototype's from the host's facts, in the list, on the ground (`SessionPlacedItem.Complexity`) and in an
  NPC's list, so a technological product a Technophobia hero's assembly left on the tile stays refused when picked up, and a made or
  taken item given to an NPC is refused when taken back. A kit item, a made item the session adds through `AddItemToNpc` or a new
  ammunition stack split off another still has none (0). The start kit enters without an insert check
  (as the host-free start of ARC-WORLD-015). The AREA viewer names the refusal in its inventory and schematic messages.

### Setting and clearing the background (ARC-WORLD-097)

N10a, FR-CHAR-005, read in the executable ([ledger BD9–BD11](../research/ledger/character.md#hero-background-in-the-session-arc-world-097)).
The hero also keeps the background's description number (`SessionHeroState.BackgroundDescription`, the original's field 0x102,
snapshot schema 54; 0 for none; since ARC-UI-068 a pre-made hero's comes from its prototype, its story 1039–1050, through
`SessionHeroDefinition.BackgroundDescription`); `QueryHeroBackgroundDescription()` gives it.

- **`SetHeroBackground(expected, choice, effect)`** (`background_set` 0x4C25E0): stores `choice.Background` and `choice.Description`
  (a `BackgroundChoice` of the [background table](../requirements/characters-skills.md#fr-char-005)) and adds one entry of `effect`,
  the rule of `choice.Effect`, with the background cause, in one revision. It clears nothing first: two sets keep two entries, as in
  the original. Refused, in order: no hero (`InvalidHero`); a background outside 0..999 or a rule of another number (`InvalidBackground`);
  then the refusals of `AddHeroEffect` (a rule it does not admit, an innate number, a rule that differs from the one in use, the
  version, the capacity, the bounds, the revision).
- **`ClearHeroBackground(expected)`** (0x4C2650): zeroes both numbers and removes the first entry with the background cause, whatever
  its number (0x4EA2E0), in one revision; `NoChange` when both numbers are 0 and no entry has that cause. Refused without a hero
  (`InvalidHero`), on a stale version and with the other refusals of an effect change (the revision).

The creation screen (N10b; its window is ARC-UI-030, [viewer](../rendering/terrain-viewer.md#new-character-window-arc-ui-030)) clears
and then sets on each background button, and clears on a race or gender change (BD10; ledger NW4, NW5),
so a choice pressed again keeps one entry. The background's effect line enters the effect list like any other entry; only the changes
the session models (stats, maxima, reactions) change values. Not modelled: a second writer of the two fields that adds no effect
(BD11), the background of any object but the hero.

Out of scope (ledger BG2, BG5–BG8; [characters and skills](../requirements/characters-skills.md)): the character creation screen,
the start kit's gold and items, the low-intelligence speech, dark sight, the time- and place-dependent stat changes, Educator and
the container seed.

## Wield events (ARC-SCRIPT-042)

Plan slice S8 of the [dispatcher research](../research/scr-event-dispatcher.md#10-proposed-implementation-slices), the wield half
(the transfer half is ARC-SCRIPT-041, [item events](inventory-runtime.md#item-events-arc-script-041)). With an
`ISessionItemScriptHost`, `Equip` and `Unequip` run the item events of the original's removals and insertions through
`ScrDispatcher.RunObject` in the executable's order (ledger [IE3–IE6](../research/ledger/scripts.md#item-events-arc-script-041),
[WE7](../research/ledger/scripts.md#wield-events-arc-script-042)); every run has the hero as triggerer and the item as attachee:

| Move | Events in order |
| --- | --- |
| Equip into an empty slot (0x464AE0) | item: wield off (25) when it was worn in another slot (a ring), remove item (28); then wield on (24), insert item (21) |
| Equip over an occupant, the insert check accepts | item: (25), 28; occupant: 25, 28; the occupant's insert check; occupant: 21; item: 24, 21 |
| Equip over an occupant, the insert check refuses | item: (25), 28; occupant: 25, 28; the check; item back: (24 when it was worn), 21; occupant back: 24, 21 |
| Unequip (0x464C80) | the no-drop and insert checks first (a refusal runs nothing); then 25, 28, 21 |

- **Which points run.** A worn item keeps the item event points it carries (`SessionWornItem.ScriptPoints`: 21, 24, 25 and 28 of
  the host's `SessionWearableItem.ScriptPoints`), and a move runs only those; the host's `Slot` answers each, so a point without a
  slot ends `NoScript`. Without a host, an item or an occupant with item event points is refused (`ItemScriptUnsupported`) as before;
  a move of items without them runs nothing either way. `EquipBest` runs no scripts: a worn item with item event points stays in its
  slot (its step names it as unsupported), and a chosen item with them stays in the pack, as before.
- **What a run may do.** `SessionItemScripts.Admission` (S6 without dialog, story state and deferred calls, plus the item-effect
  commands of [set S14](../scripting/scr-dispatcher-runtime.md#item-effects-arc-script-042-set-s14) and play sound). The session
  applies, with the move and in one revision: the global flags; the printed lines (`EquipmentMutationResult.Messages`); the effect
  changes on the hero (`ScrEffects.EffectChanges` for the PC: add with the host's rule, `ISessionItemScriptHost.EffectDefinition`,
  and the cause as the original numbers it, `SessionItemScripts.Cause`; remove of the first entry of the number), checked as
  `AddHeroEffect` and `RemoveHeroEffect` check theirs, the effect list's damage shifts and timers included; and the slots the runs
  changed of the move's items (the event's own writeback and its call-ex runs'), which the session keeps
  (`QueryItemScriptSlots`) and every later item run reads instead of the host's slot. Eye candy and sounds stay in the runs
  (`EquipmentMutationResult.Scripts`) for the host to show and play.
- **Poisoning items (ARC-SCRIPT-1482).** The installed wield-on poisoning items (callers 30088, 30103, 30115, 30147, 30165, 30197)
  run inside this set: they add effects and only *schedule* their poison script with call script in (54), which the move puts in the
  session's event queue. The damage is the deferred call's: `AreaScriptHost.CallAdmissionFor` holds the magick-tech adjustment
  (`GameSessionOwner.QueryWornPowerRatio`, a worn item's ratio), the eye candy and, since ARC-SCRIPT-1586, the vitals questions get hit points and get fatigue points (107, 108), which the scripts 2677, 2679, 2681 and 2682 read after their first damage, and its damage (60, 138) and heal hit points (75)
  of the hero come from the talk set while the shown map's use host answers: they go through the use host's session path, no
  attacker (ARC-SCRIPT-1563, ledger [PZ11](../research/ledger/combat.md#poison-sources-arc-res-1329)). The item-event set itself has no damage action and needs none.
- **Item questions.** The identified test is the host's (`ISessionItemScriptHost.IsIdentified`); the power ratio of the
  magick-tech adjustment is the session's, for an item of the move and the hero (its effective magick/tech aptitude under the effect
  changes of the move's earlier runs) or another item of the move (not a critter: 100); items are never being rewielded (no rewield
  yet, WE6).
- **Refusals.** A run outside the set, an effect on any object but the PC, an add whose rule the host lacks, a cause outside 0..9 or
  one the session does not own (race, gender, injury), an innate effect number, a full or unbounded effect list, a slot change of an
  object that is not an item of the move, an object the host or session does not know, a ratio read after an effect change of the
  same run that changed the hero's aptitude (with a call-ex in the run, any ratio read with such a change), and a trap destruction refuse the whole move: nothing is
  applied and the result lists the runs made (`Scripts`); later runs are not made.
- **A refused occupant** (`RolledBack`): both items are back where they were after the scripts of both removals and both
  re-insertions ran, and what those runs changed is applied (`Changed`, or `NoChange` when nothing), as in the original; `Verdict`
  is the insert check's answer, `Displaced` is null and `Failure` is None (a host tells the refusal by `RolledBack` or the verdict,
  not by the failure as on the path without scripts).

**Snapshot schema 43.** A worn item's `scripts` (its item event points, ascending) and the inventory's `scriptSlots` (entity,
point, script, local flags, counters; by entity in ordinal order, then by point, at most `SessionItemScripts.MaxStoredSlots`). Restore
refuses a point outside 0..35 or the dialog-override slot, a script number outside 0..99,999, a deleted item's slot, worn points
other than 21, 24, 25 and 28 and an unsorted list (it does not check that the entity is an item the session or a seed lists). A deleted
item's kept slots go with it.

**Installed data** (the `--script-census` probe's `item_points` and `wield_pairs`): 43 of the 56 wield-on scripts and 50 of the 52
wield-off scripts run from line 0 inside the item set, and 42 of the 56 objects' wield pairs (wield on, and wield off with the
wield-on slot its call-ex names, from that line). The others stop first at call script in (54, 8 pairs: the cursed items that hurt
their wearer every few seconds), get stat (33, 2), remove script call (125, 2), add blessing (25, 1) and get magic-tech adjustment
(80, 1). The typical pair: wield on scales a base value by the power ratio into counter 0 and adds an item-caused effect that many
times; wield off reaches the same script's removal loop through call-ex, which reads the count the session kept.

**Not ready.**
- **Rewield** (0x465530, WE6): the original runs every worn item's wield off and wield on when the hero's magick or tech points
  change (the stat setter, stats 22 and 23), with the rewielding condition true; the session's advancement does not, so a worn
  scaled item keeps the count of its wield (the condition answers false).
- **Hosts.** The AREA viewer's inventory window calls `Equip` and `Unequip` with its item script host (ARC-UI-015); that host
  answers no effect rule and no identified test, so such a run refuses there.
- **Other paths.** Giving a worn item with item event points to an NPC (ARC-WORLD-071) and throwing such a wielded weapon
  (ARC-COMBAT-017) are refused (`ItemScriptUnsupported`), as those paths run no item events; a worn item deleted by wear or destruction,
  a weapon a critical miss drops (ARC-COMBAT-009) and an item whose first break takes it off (ARC-WORLD-053) leave the slot without their
  wield-off and remove-item scripts (their effects stay); item spells on wear are still refused; NPC equipment runs the wield scripts (24 and 25) in the rewield its TraitIdx magick and tech points run (ARC-MAGIC-1644, `GameSessionOwner.PlanNpcRewield`) and, with the item event scripts of the removal and insertion (28, 21), in its best-items wield (ARC-MAGIC-1684, see [NPC best items with item scripts](#npc-best-items-with-item-scripts-arc-magic-1684)); its barter, give, take and loot paths run none (#1743); the carried light of an NPC the session recalculated is `AreaRestockHost.NpcLight`, ARC-MAGIC-1561); the effect list's bound (`SessionEffectLimits.MaxHeroEffects`, 1024 entries since ARC-DEV-366, which raised it from 64 for the tech books' one entry per
  point of intelligence)
  refuses a move whose adds would pass it (the original has no bound). ARC-DEV-366: an item script may read the hero's stat (get stat, 33; see
  [tech books](inventory-runtime.md#tech-books-arc-dev-366)); the viewer's host answers effect rules from the hero's effect tables.

## Tests

- `Arcanum.Simulation.Tests/Equipment/EquipmentRulesTests`: slot mapping, race sizes, the wear check's order and every
  refusal (the shield with one crippled arm), the art and no-drop checks, effective power and adjustment with truncation,
  worn AC and resistances, light tiers, minimum strength, weapon speed and cost, bare hands with gauntlets, a wielded
  weapon's values, validation.
- `Arcanum.Session.Tests/EquipmentStateTests`: wielding a kit weapon, armour against overexertion, every refusal in order,
  the art check's place, a callback that changes the session (stale), the occupant's insert check and swap, a fixed two-handed weapon against a shield (also on restore), rings between slots, unequipping, no-drop, crippled arms
  from the life domain, race and gender, a taken item, the canonical text and restore, restore and decode refusals, the
  bridge from stored fields.
- `Arcanum.Simulation.Tests/Equipment/EquipmentChoiceTests` (ARC-WORLD-034): worth and identification, thrown damage, the
  weapon score (every type summed, DamageBonus, difficulty, untrained skills, ammunition), the choice with its tie rule, a
  target, and the other slots' strict worth.
- `Arcanum.Session.Tests/EquipBestTests` (ARC-WORLD-034): the start in one revision and a repeated call, a more valuable
  item replacing a kept one, ineligible items and a refused swap, a no-drop weapon that stays on and a shield that crippled
  arms take off, the tie to the later item, refusals and a meddling callback, unsupported items left in the pack.
- `Arcanum.Session.Tests/WieldEventScriptTests` (ARC-SCRIPT-042): a scaled wield-on script and its call-ex wield-off with the kept
  counter (also across the snapshot), the power ratio, the order of a swap, a refused occupant's rollback and its applied effects, a
  moved ring, every refusal with nothing changed, the identified test and eye candy, `EquipBest` leaving a scripted item, decode and
  restore refusals. `Arcanum.Scripting.Tests/Scr/ScrItemEffectTests`: the S14 commands.
  `Arcanum.TerrainViewer.Tests/AreaStartingHeroTests`: the viewer start wears the kit; mismatched facts and an unreadable
  kit prototype fail it. `EquipBestTests` also keeps the knife on when the better weapon is unsupported; the session's bridge
  test reads the wearable facts and the Data reader test the script points.
- `Arcanum.Data.Tests/Combat/CombatFieldReaderTests`: the art fields and their bits.
- Real data (opt-in `RealData`, `ARC_AREA_PROTOTYPES`): `EquipmentRealDataTests`. All 549 weapon, armour and generic
  prototypes convert (slots: 150 weapon, 165 body armour, 13 shields and 2 torches, 19 helmets, 15 gauntlets, 10 boots,
  19 rings, 10 medallions, 146 generic items worn nowhere); Virgil's armour 8114 is body armour with AC +3. Offered to a
  half-elf man, 290 of 403 are worn, 96 body armour items are another size, 5 are for women and 12 carry item spells.
  `StartingHeroTests.Every_premade_hero_starts_wearing_its_best_kit_items` (`ARC_AREA_CONVERSATION_GAME_ROOT`): the worn
  slots and prototypes of all twelve pre-made heroes (ARC-WORLD-034).

## Not ready

- **Paperdoll.** The viewer's inventory window has a diagnostic paper doll with wear and take-off buttons (ARC-UI-015, ledger
  IW1–IW9) and, since ARC-UI-019, the hero's grid with the mouse's drag onto cells and slots
  ([hero grid](inventory-runtime.md#hero-inventory-grid-arc-ui-019), IW10–IW15); the doll's art is not built, and a made item has
  no wear values there. With a
  worn taken item the viewer's attack wears none of the hero's items and a taken ranged weapon has no missile art (kit values only). The
  pre-made start wields the kit (ARC-WORLD-034). The best-items choice reads the hero's skill levels (ARC-SKILLS-002) and its ammunition stacks (ARC-WORLD-066); a custom
  character's start (weapon and armour only) and the AI's call with a target are not wired.
- **NPC equipment.** NPC slots stay the host's (seed inventory locations) until a regeneration dresses the NPC (ARC-WORLD-103: the
  session's record then wins); `EquipmentRules.Evaluate` serves them too, and looting a worn item is still `WornItemUnsupported`.
  Since ARC-WORLD-106 the viewer's NPC combat values read the session's worn items
  ([NPC wear in combat](#npc-wear-in-combat-arc-world-106)). The AI's best-items call (item insert's CHECK_WIELD, the gate with a
  target, SP13) is not wired: `AreaAiHost.ReWield` does nothing.
- **Not tied to the round.** `Equip` and `Unequip` neither charge action points nor refuse during a turn-based round; the
  whether the original charges for changing equipment in a round is not researched here.
- **Not modelled:** item spells on wear and unwear (such items are refused), scripts on wield without a script host (refused;
  with one, [wield events](#wield-events-arc-script-042)), art and
  appearance changes other than the hero's body armour ([body armour art](#body-armour-art-arc-world-112)), the animation speed recalculation (EQ5), the paperdoll UI's own path, identification
  (combat reads every magic adjustment), weapon wear, hexed items, ammunition pools, drop, barter, weight effects,
  base AC and resistances of the PC and its effects, skills and training.

## NPC best items with item scripts (ARC-MAGIC-1684)

The NPC's best-items wield (the regeneration's, RK15, and the AI gate's re-wield, `GameSessionOwner.RewieldNpc`) shares one planner, `PlanNpcWield`, with its own `BestPlan`. With the host's script context (`ISessionRestockWorld.NpcWieldScripts`: the shown map's item script host, the NPC's life target and its stored stats, an advancement record's sheet laid over them) the plan runs the item event scripts of every move as the original does (ledger IE3-IE6), the NPC as the triggerer: a take-off runs the removal (wield off 25, remove item 28) and the insertion at the pack (insert item 21) once the insert check accepts; a wield runs the item's removal, the occupant's removal, the occupant's insert check, then (accepted) the occupant's insertion and the item's insertion with wield on 24, or (refused) the item's and the occupant's insertions back where they were, so that both moves' scripts have run and the session still changes (`EquipBestResult.Disposition` Changed, the worn record untouched). One `ItemScriptRuns` carries the whole call, so a run reads the effect list and flags the earlier steps left; nothing is applied before the plan is complete, and then the runs commit with the worn record in the call's one revision (the regeneration's own). The printed lines go to the AI gate's `Rewield` notice (the message window); ARC-MAGIC-1745: the regeneration's lines (`SessionNpcWield.Messages`) are posted by the viewer for the restock event and for a created critter's stocking (the shop's merchant regeneration, opened from the main menu, has no message window and posts none), and the TraitIdx rewield (`PlanNpcRewield`, the NPC as the triggerer) logs its lines as `SpellCastEventKind.ItemScriptLine` after the write. The eye candy of any of these runs (action 120) is still not shown: no host plays a script's eye candy yet ([#1592](https://github.com/Simarglok/arcanum-steamwoven/issues/1592), follow-up [#1830](https://github.com/Simarglok/arcanum-steamwoven/issues/1830)); the runs' `ScrEffects.EyeCandies` stay in `ItemScriptRuns.Runs` for it.

What the session cannot apply is given up per item, not per call: a run it refuses (a script outside the admitted set, an effect on another object, a rule the host does not know, the life rules, a take-off whose scripts ran for a choice that then proved unsupported) leaves that item's moves undone as before (its step names it, `ItemScriptUnsupported`) and the plan starts again; a failure of the whole plan (the stored stats do not fit the character rules, the runs' changes do not fit the session) gives up every item's scripts. The best-items choice reads the NPC's damage bonus, aptitude and extraordinary intelligence: the first script whose effect moves any of them (or adds or removes an effect on a basic or tech skill, which the weapon score reads and the session does not model) is not run when a choice follows it, since the choice's inputs are a snapshot of the call's start. Without the script context, or for an item with no move script, the call is what it was: an item with a move script is not moved. Item spells are still not moved. Not run here: the hero's best-items wield (`EquipBest`), the NPC's give, take, loot and barter paths and the hero's rewield ([#1743](https://github.com/Simarglok/arcanum-steamwoven/issues/1743), [#1744](https://github.com/Simarglok/arcanum-steamwoven/issues/1744)).
