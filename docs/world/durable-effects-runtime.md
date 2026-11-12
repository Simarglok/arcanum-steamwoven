# Durable effects runtime (ARC-WORLD-029)

Status: **Implemented bounded component**, ARC-WORLD-029 (2026-10-17), top-20 item 17. The hero's effect list in the
[game session](game-session-runtime.md): numbered effects with a cause that stay until something removes them (a
blessing, a curse, an item or spell effect, a script's effect), timed effects whose END waits on the session's
[event queue](game-event-queue-runtime.md) (ARC-WORLD-024), and what an effect does to the hero's stats, its two maxima
(hit points and fatigue) and its reaction effects. The list is saved (snapshot schema 13; blessings and curses, schema 17,
[below](#blessings-and-curses-arc-world-035)); an END is a saved event.
Requirements: [FR-CHAR-022](../requirements/characters-skills.md#fr-char-022) (effects, their causes and removal) and the
effect part of [FR-MAGIC-006](../requirements/combat-magic-tech.md#fr-magic-006) and
[FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007) (a timed run whose components add and remove an effect).
Poison is not an effect: its level and ticks are the [life state](life-state-runtime.md)'s (ARC-COMBAT-002, ARC-WORLD-024).
Not wired into the viewer ([Not ready](#not-ready)).

## Evidence

- The original's functions, read first: the effect functions (add, the four removals, count, the internal removal,
  the aggregation), the hit-point damage setter and maximum (0x43D530, 0x43D5A0), the fatigue damage setter and maximum
  (0x45D5A0, 0x45D670), the magick and tech Effect component and death hooks, and the script add-effect and
  remove-effect actions.
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically per the
  [original executable check](../research/original-executable-check.md) by two independent readers; rows EF1–EF10 in the
  [verification ledger](../research/ledger/character.md#durable-effects-arc-world-029). **The original does what
  that first reading describes** in every row; it adds only a network branch before the critter test of add and typed removal, which a single
  player game never takes. The one oddity of the original that a player could meet (EF10: an effect that lowers a maximum
  far enough leaves the critter at no fatigue or no hit points without the faint or death handling) has no session effect
  beyond the values themselves, so **no rule-mode switch**: both modes follow the original.
- Installed original (opt-in, below): 305 of the 310 effect rules parse; all of them but the fixture hero's own race
  effect are admitted.

## The list (source facts, EF1–EF5)

A critter's effects are two parallel arrays: the effect number and its cause (race 0, background 1, class 2, bless 3,
curse 4, item 5, spell 6, injury 7, tech 8, gender 9). The source puts no bound on the number, the cause or the length.

| Operation | Original |
| --- | --- |
| Add (0x4E9F70) | Appends number and cause. Before and after it reads strength, the encumbrance level, maximum hit points and maximum fatigue: a strength change recalculates encumbrance; a change of either maximum is **added to that damage** through its setter, which clamps at 0. Then the animation speed. |
| Remove one typed (0x4EA100) | The **first** entry with the number, whatever its cause; then the animation speed, also when nothing matched. |
| Remove all typed, one or all caused by (0x4EA200, 0x4EA2E0, 0x4EA3C0) | Every entry with the number; the first or every entry with the cause. No animation speed. |
| Internal removal (0x4EA520) | Shifts the later entries up in both arrays and shortens them; the same strength and maxima handling as add. |
| Count (0x4EA4A0) | Entries with the number. |

Consequences: current hit points and fatigue stay when a maximum rises (the damage grows with it: a buff does not heal), and
fall only to the new maximum when it sinks below them; each entry is its own step, so a removal of several entries can
leave less than one combined step would (see the tests). The hit-point setter schedules the resting timer on a positive
write, the fatigue setter the 80-second recovery on a non-zero one; the faint consequences run only when a non-zero write
makes the critter unconscious, which a shift that keeps the current value never does (EF7, EF10).

The aggregation (0x4EA6C0, EF6) sums the percent changes, multiplies the multiply and divide factors,
sums the adds, takes the highest minimum and the lowest maximum, and applies them in that order; −1 is the "unset" mark of
both bounds, so a bound of −1 is never applied and a −1 met after a lower minimum or a higher maximum clears it (no
installed rule uses −1); its "ignore innate" variant skips race, background, class and gender. Maximum hit points and maximum fatigue apply their effects to the whole
sum of points, adjustment and stat part (the hit-point maximum 0x43D5A0, the fatigue maximum 0x45D670, EF8).

A timed spell is a magick or tech run, not a timed effect: its Effect component (0x451F20, EF9) adds `Count` entries
(plus `Scaled × parent aptitude / 100` for a critter parent) at BEGIN and removes as many with the typed removal at END,
which the run's duration schedules ([magic core](../research/magic-core-evidence.md#duration-maintenance-and-ending-fr-magic-006)).
The END therefore removes the first entries of the number, which may be an item's or a curse's (ledger row "Effect remove
risk", Original).

## Session domain (`Arcanum.Session`)

The hero's list starts with its innate entries, the race effect `64 + race` and, for a woman, the female effect 330, as
every pre-made hero's prototype does (ARC-RES-027 SH-03); their stat changes are the hero's saved `RaceEffect` and
`GenderEffect` (ARC-WORLD-015). The durable entries follow them in list order.

| Entry | Contract |
| --- | --- |
| `QueryHeroEffects()` | The innate entries, the durable entries, the rule of each number in use and the pending timed effects; `Count(number)` counts the whole list. Null without a hero. |
| `AddHeroEffect(expected, definition, cause, count = 1)` | The add (0x4E9F70) `count` times. Refusals in order: no hero (`EffectsUnavailable`); a rule the session does not admit, a cause of the race and gender setters or the life domain's scars (injury), a count below 1 (`InvalidEffect`); an innate number (`InnateEffect`: its typed removal would meet the innate entry first); a number in use with other changes (`EffectMismatch`); the version; more than 1024 durable entries (`SessionEffectLimits.MaxHeroEffects`, 64 before ARC-DEV-366; `EffectCapacityExceeded`); entries that could leave the consumers' bounds once some of them are removed (`InvalidEffect`, below); the event budget and revision. One revision. |
| `AddHeroTimedEffect(expected, definition, cause, count, durationMs)` | The same, and an END event (`effect.hero.end`) after 1 ms up to `int.MaxValue × 8000` ms of game time (a `Duration:` in real seconds × 8000); at most 64 pending ENDs. The result's `TimedEffect` is its queue sequence. |
| `RemoveHeroEffect(expected, number, count = 1)` | The typed removal (0x4EA100) `count` times; an innate number is `InnateEffect`; NoChange when nothing matches. |
| `RemoveAllHeroEffects(expected, number)`, `RemoveHeroEffectsCausedBy(expected, cause, all)` | The other three removals; race, gender and injury causes are `InvalidEffect`. |
| `EndHeroTimedEffect(expected, sequence)` | The run interrupted (a dispel, a stop, the death of its caster or target): its END now and its event cancelled; `UnknownTimedEffect` when none is pending. |
| `ProcessEvents` | A due END removes its entries without a version check, one revision; `SessionEventPassResult.Effects` reports it. |

**Bounds that no removal can break.** An add is admitted only when every subset of the resulting entries stays within the
bounds of their consumers: at most 64 changes of each reaction value (the reaction rules' limit), and each maximum below
32 000 in either direction even if the minimum and maximum changes that hold it and the changes that pull it the other way
were removed (a hero's maximum before effects is at most 406: no pools, every stat and the level at their life limits).
So a removal or a due END never meets a refusal, and an END is never dropped unrun. Real data is far inside these bounds.

Each change runs its steps on a trial character owner first; for each step the hero's life facts are rebuilt and each
maximum's difference moves its damage (clamped at 0, saturating at the life limit), then the fatigue recovery timer is
set if the fatigue damage became positive (the session's "set on change" rule, ARC-WORLD-024). Nothing is written before
every check passed. `SessionEffectResult.Life` is the new life record when it changed.

**What an effect does in the session.** `SessionEffectDefinition` keeps every change of the rule
(`SessionEffectDefinition.FromRule(EffectRule)`, the ARC-RES-027 effect reader), also those not modelled yet. Modelled:
the stats of `CharacterStatsOwner` (source stats 0..16, 22, 23: the primary and derived stats and the magick and tech
points), the maximum hit points and maximum fatigue (`LifeFacts.MaxHitPointEffects`, `MaxFatigueEffects`), and the
reaction, good and bad reaction adjustments (`QueryHeroReactionFacts`, which the [NPC reaction](npc-reaction-runtime.md)
rules read). Admitted: numbers 0..399 (the source's tables; a larger one would read past them), at most seven changes per
collection, each naming an attribute of its collection with nine digits at most, every modelled change within ±1000.
The `xpgain` special changes every experience award after the difficulty (ARC-WORLD-030,
[experience](experience-runtime.md)); its changes are modelled since then (±1000; one installed rule uses it, within that bound).
The tech-discipline changes are live since ARC-TECHNOLOGY-002: the hero's effective expertise reads them ([tech degrees](tech-degrees-runtime.md)).
The resistance changes (the five of `resistdamage`, `resistfire`, `resistelectrical`, `resistpoison`, `resistmagic`) are modelled since ARC-MAGIC-027
([consumers](#consumers-of-resistance-and-stat-changes-arc-magic-027)); their values are held to ±1000 like the stats'.
Inert until their owners exist: skills, tech skills, the critical-hit specials,
level, age, gender, race, alignment, poison level.

## Simulation (`Arcanum.Simulation`)

`EffectModifier(Operator, Value)` and `EffectModifiers.Apply` are the aggregation for a single value, with the −1 mark;
the character owner's stats and the reaction effects now aggregate through it too, so the three cannot drift. `LifeFacts` gains
`MaxHitPointEffects` and `MaxFatigueEffects` (empty by default); `LifeRules.MaxHitPoints` and `MaxFatigue` apply them to
the whole sum, and `LifeRules.IsValid` refuses facts whose maxima reach 32 000 (the pinned death damage) in either
direction, so a pinned corpse still reads as dead.

## Snapshot schema 13

`GameSessionSnapshot.Effects`, JSON between `equipment` and `random`; the ENDs are events in `events`:

```json
"effects":{"hero":[{"effect":120,"cause":"Item"},{"effect":122,"cause":"Spell"}],
  "definitions":[{"effect":120,"changes":[{"kind":"Stat","attribute":0,"operator":"Add","value":2}]},
                 {"effect":122,"changes":[{"kind":"Special","attribute":0,"operator":"Add","value":10}]}]}
```

(The synthetic test's values.) An END event has no entities and the values `[scheduledAt, durationMs, number, count]`; the
kind joins registry version 1 without a new version, as the deferred SCR call did. The codec checks shapes, names, ranges
and definitions by strictly ascending number; `Restore` refuses effects without a hero (`InvalidSnapshot`), an entry of a
cause the domain does not own, without its rule or of an innate number, a rule no entry uses or one the session does not
admit, and maxima the life rules refuse (`InvalidEffect`), and an END without a hero, of an innate number or whose deadline
is not its duration after its start (`InvalidEvents`). **Migration decision: none.** Schema 12 and older are refused; a
quick save of schema 12 loads as "written by an incompatible version" and the next F7 overwrites it (the viewer adds no
effect).

## Tests

- `Arcanum.Simulation.Tests/Character/EffectModifiersTests`: the order, the bounds and their −1 mark, truncation, validity.
- `Arcanum.Simulation.Tests/Combat/LifeRulesTests`: effects over the whole maximum, under poison, the 32 000 bound.
- `Arcanum.Session.Tests/EffectStateTests`: list order and the four removals whatever the cause, the damage shift of each
  maximum (an unharmed hero who grows stronger keeps 30 of 34 hit points), step-by-step removal, a timed END on the queue
  that takes an item's entry first, an interrupted END, every refusal, the bounds no removal can break (a capped maximum, 64
  reaction changes), a hero left without hit points by an effect with no death step (EF10; removing the effect adds the
  maximum back to the damage, so the hero stays at none), the reaction effects, the canonical text with an END that fires
  after a restore, decode and restore refusals.
- Real data (opt-in `RealData`, `DAT_ROOT`): `EffectRealDataTests`. Of the 309 `effect.mes` entries and the module's one,
  all 310 parse, 5 of them with `*` or `/` (read as the original reads them since ARC-WORLD-035, decision 7; none on a
  maximum); all but the fixture half-elf's race effect 67 are added and removed; 194 change a modelled stat, 7 the maximum
  hit points, 7 the maximum fatigue, 5 the reaction, 7 the bad and 5 the good reaction adjustment.

## Blessings and curses (ARC-WORLD-035)

The hero's blessings and curses: two PC lists of a number and the game time it was received (the logbook's date), each
of which adds one effect entry with the bless or curse cause. Requirement
[FR-CHAR-023](../requirements/characters-skills.md#fr-char-023). Evidence: the retail executable, read by two independent
readers at the blessing and curse functions (ledger rows BC1–BC6). The original also has a network branch (never taken
in single player) and copies a blessing's text without a length limit (BC1).

| Source (bless / curse) | Original |
| --- | --- |
| Table entry (0x4C4170 / 0x4C3CC0) | The module's `mes\gamebless.mes` / `mes\gamecurse.mes`: entry `number × 10 + field`, field 0 the name, 1 the effect number, 2 the description; a number below 50 reads nothing, a missing entry gives an empty text. |
| Effect (0x4C4420 / 0x4C3F70) | `atoi` of the effect entry: an empty or non-numeric text is effect 0, the module's `gameeffect.mes` effect 0. |
| Has (0x4C4280 / 0x4C3DD0) | A PC only; the number in its list. |
| Add (0x4C42F0 / 0x4C3E40) | A PC only; nothing when held. Appends the number and the current game time, adds the effect with cause bless (3) or curse (4), and for the local PC shows message 1000 and lights the logbook button. |
| Remove (0x4C4450 / 0x4C3FA0) | A PC only; nothing when not held. Removes the first entry (later entries and their times move up), then the first effect entry of the effect number (the typed removal 0x4EA100, whatever caused it), and lights the logbook button. |

**Data.** `Arcanum.Data.Characters.BlessingRules` reads a table: `EffectOf(table, number)` gives the source's effect number
and how it was read (`Number`, `BelowRange`, `Missing`, `Prefix`, `NotANumber`; a number outside 32 bits is refused as
`Overflow`, where the original's `atol` would wrap; no installed entry is near it); `Numbers(table)` lists the numbers with
an effect entry. `EffectRules.ReadAny(effects, module, number)` reads any effect 0..399 the way the tables hold it (0..49 from
the module, a number without an entry has no changes). Installed original: 18 blessings (50..67) and 17 curses (50..66).
Blessings 50 and 51 name module effects 4 and 8, which the module does not define (no changes). **Curses 52, 53 and 54
hold a placeholder that is no number, so the original adds the module's effect 0** (strength +6, beauty −6, intelligence −6,
prowling −3, age +20, ARC-RES-027 SH-08) with the curse. They are unused placeholder slots: their name and description are
a placeholder too, and nothing in the installed game gives them (BC6). Ten scripts give curses: two by a literal number
(curses 50 and 51, on two deaths) and eight altar scripts by their attachment's second counter, which the eight altars placed
in the installed maps set to 55–58 and 60–63 (the same values as the scripts' own initial counters). So both modes follow the
original, and no switch is needed (owner decision 2026-10-20).

**Session.** `QueryHeroBlessings(kind)`, `AddHeroBlessing(expected, kind, number, effect)` and
`RemoveHeroBlessing(expected, kind, number)` with `SessionBlessingKind` `Blessing` or `Curse`. Add: the hero, the version,
NoChange when held (the effect is not looked at), at most 64 entries per list (`SessionEffectLimits.MaxBlessings`, `EffectCapacityExceeded`), then
`AddHeroEffect`'s checks and step with the bless or curse cause; the host passes the effect rule it read for the number
(`BlessingRules.EffectOf`, then `EffectRules.ReadAny`, `SessionEffectDefinition.FromRule`) and shows the received message on a
change. Remove: NoChange when not held; else the entry leaves the list and the first effect entry of the effect number it
added is removed (none may be left). Each change is one revision; the entry keeps the effect number it added, which is what
the source reads again at removal. Both modes behave as the original, the unreachable placeholder curses included (BC6).

**Snapshot schema 17.** `SessionEffectState.Blessings` and `Curses`, in `effects` after `definitions`:
`"blessings":[{"number":55,"effect":120,"timeMs":54000000}],"curses":[]` (list order). `Restore` refuses them without a hero
(`InvalidSnapshot`), and a number held twice, a time after the saved clock, or an effect number outside 0..399 or innate
(`InvalidEffect`). Schema 16 and older are refused, not migrated.

**Tests.** `Arcanum.Data.Tests/Characters/BlessingRulesTests` (the `atoi` cases, below 50, missing, the module's numbers);
`Arcanum.Session.Tests/BlessingStateTests` (held once with its time, curses apart, removal of the first effect of its number
whatever its cause, a blessing whose effect is already gone, refusals, the canonical text and restore refusals); real data
(`DAT_ROOT`): `BlessingRealDataTests` (the tables, their effect numbers and the literal SCR numbers) and
`EffectRealDataTests.Installed_blessings_and_curses_are_received_and_taken_back` (all 35 given and taken back).

**Not ready.** No SCR action or condition runs them, no logbook page, no "received" message in the viewer; fate points and
the deferred blessings of FR-CHAR-024 are not part of this.

## Not ready

- **No host.** Nothing in the viewer adds an effect yet: no spell casting, no item spells (wearing such an item is still
  refused), no SCR add-effect, remove-effect, blessing or curse action or has-blessing/has-curse condition (the dispatcher
  admits none of them), no effect shown on a character sheet, no logbook page of blessings and curses.
- **NPCs (ARC-DEV-348, ARC-MAGIC-027).** The session keeps an effect list per NPC [below](#npc-effect-lists-arc-dev-348); the Effect component takes it and the stat and resistance changes reach the NPC's combat values (ARC-MAGIC-027, [consumers](#consumers-of-resistance-and-stat-changes-arc-magic-027), which also lists what is left).
- **Spell runs (ARC-DEV-303).** The cast host's Effect component ([spell cast host](spell-cast-host.md#effect-component-and-the-entangle-re-resistance-arc-dev-303)) adds and removes these entries on the hero
  (`AddHeroEffect` with the spell cause, `RemoveHeroEffect`); Entangle's effect 62 (speed −5) is the first. Since ARC-MAGIC-027 it reaches an NPC's list too.
- **Runs.** A timed effect stands for a run made only of Effect components. Active spells (magic slice 4) own maintenance,
  the `INT / 4` cap, the scaled count and its MG17, and every ending trigger: they will call `EndHeroTimedEffect` or replace
  the END kind. The session ends no timed effect by itself on death, unconsciousness, rest or travel.
- **Dead without a death time.** An effect that takes the hero's hit points to none (EF10) runs no death step: the hero
  reads as dead with no death time, and removing the effect leaves it there (the maximum returns to the damage). Hosts must
  not take a death time as the only sign of death.
- **Not modelled:** encumbrance (a strength change does not recalculate it), the animation speed, the injury cause (the
  life domain counts scars; their effect 50 changes nothing in the session), the innate effects' non-stat changes (the
  gnome's bad-reaction adjustment, the halfling's critical-hit chance: ARC-WORLD-015 keeps only stats), background and class
  effects of a new character. A multiply or divide change on a maximum is refused (`InvalidEffect`): no installed rule has
  one, and the bounds that keep removals admissible assume none.

## NPC effect lists (ARC-DEV-348)

Slice E1-d of the [missing-systems plan](../project/missing-systems-plan.md) (magic plan P1); requirements
[FR-CHAR-022](../requirements/characters-skills.md#fr-char-022) and [FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007). The original keeps one list per PC or NPC
(`effect_add` takes either, ledger EF1), so an NPC's list works as the hero's, over the NPC's own life record. Code: `GameSessionNpcEffects.cs`; tests `NpcEffectStateTests`.

- **Loaded list.** The map or prototype data stores the list (the two effect arrays, `CombatCritterFields.Effects`); `SessionCritterCombat.StoredEffects` and
  `SessionLifeTarget.LoadedEffects` carry it. The host that reads the effect tables sets `GameSessionOwner.NpcEffectRules` (number to `SessionEffectDefinition`); without it a
  loaded entry has no changes (the source reads a number with no table entry as none).
- **Session list.** `AddNpcEffect`, `RemoveNpcEffect` (the first entry of the number, whatever its cause, EF2), `RemoveAllNpcEffects` and `RemoveNpcEffectsCausedBy` (EF3) take the
  NPC's `SessionLifeTarget`. The first call that changes a list adopts the loaded list whole; from then on the session's list wins, as an NPC's life record does. `QueryNpcEffects`
  answers the session's list, else the loaded one. NPC causes are the source's ten; no cause is refused (the race and gender setters own no NPC entry). One revision per call.
- **Maxima and damage.** The entries' maximum-hit-point and maximum-fatigue changes replace the host's (`LifeFacts.MaxHitPointEffects`, `MaxFatigueEffects`) wherever the session
  reads an NPC's life (`WithWrittenHolds`); each entry added or removed moves the NPC's hit-point and fatigue damage by the change of the maximum, clamped at no damage (EF1, EF5),
  and sets the timers a damage write sets (fatigue recovery, the NPC's resting). A maximum pushed to nothing leaves the NPC at no points with no faint and no death step (EF10, both modes).
- **Saved.** The `effects` section's `critters` (schema 87, upgrade step 84→85 adds the empty list): per NPC its entries, causes and the rule of each number in use.
- **Spell flags.** The written flags (`WriteObjectFlag`, `spells.objectFlags`, schema 83) already hold any bit of the spell and critter flag fields for any mobile NPC; the hosts read
  the stored field from the map data (slot 19) and the session's change over it. Which bits a spell component writes is the caster's.
- **Limits** (project policy, the source has none, EF11): 1024 entries per NPC, the hero's capacity (it was 64 until ARC-RES-127, which refused the 80 entries record 214 adds), 4096 lists (an add or a removal that adopts a loaded list counts). The entries are checked at each change (`Bounded`) against
  the largest base maximum the NPC's own facts give, so no removal of them is refused for the maxima; a removal is still refused when the loaded list itself is not admitted (more than 1024
  entries, an undefined cause, a rule the session refuses). A restore does not know the NPC's facts, so it does not check that bound: where the list laid over the facts is refused by the life
  rules (a level-up after the add), the host's maxima stand instead of the list's. An NPC's timers stay on one map: a change on another map takes its pending timers along, and is refused during an event pass.
- **Not ready.** The maxima of a loaded list apply from the first session change of that list (before it the host's facts stand), and then without moving the damage, which the data stores relative to them; no timed NPC effect (an END event of its own) and no
  cleanup of the list of a destroyed NPC; the ObjFlag component's other bits on an NPC (`SessionSpellCaster*`) and a host that sets `NpcEffectRules` and fills the loaded list are the next slices (E4-a).
  What the stat and resistance changes of an NPC's list reach, and what not, is [below](#consumers-of-resistance-and-stat-changes-arc-magic-027).

## Consumers of resistance and stat changes (ARC-MAGIC-027)

Magic audit slice MA-2 ([audit](../research/magic-audit-res-103.md)): what the changes of an effect list do once they are in it. Requirements
[FR-CHAR-022](../requirements/characters-skills.md#fr-char-022), [FR-COMBAT-008](../requirements/combat-magic-tech.md#fr-combat-008) and
[FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010). Evidence: ledger [RE1–RE3](../research/ledger/magic.md#effect-consumers-arc-magic-027) (the effective resistance function 0x43D6D0 and the magic save 0x4537B0, read in the retail executable).

- **Resistances (RE1).** The effective resistance (`CombatantRules.Resistances`, 0x43D6D0) is the stored value plus the worn armour's adjustments plus, for poison, `5 × (CN − 4)` when positive; **then** the effect aggregation of that
  resistance (the same one as for stats: percents summed, factors multiplied, adds summed, the highest minimum, the lowest maximum), **then** the clamp to 0..95 (not for a monstrous NPC, which keeps the sum). A buff that lifts a
  resistance past 95 is cut for a PC and an ordinary NPC; a curse can take a stored resistance down to 0. `EffectResistances` carries the five lists of modifiers; `EffectDomain.Resistances` collects them from a list.
- **The hero.** `QueryHeroAttack` builds the hero's `Combatant` with its durable effects' resistance changes (so every attack, spell damage and the poison tick of the hero meet them; the hero's normal resistance for its life facts too).
  The innate entries' changes (race, gender) are not modelled, as before (ARC-WORLD-015 keeps stats only); the hero's stored resistance is none.
- **An NPC.** `GameSessionOwner.WithNpcEffects(SessionCritterCombat)` returns the NPC's combat values under its list (the session's, else the loaded one when the host gave `NpcEffectRules`): the stat changes of the entries
  are applied to a throwaway copy of its character owner (the shared one is never touched; race and gender causes of a loaded list are applied as spell entries there), its resistance changes to the effective resistances. Speed
  (the turn-based action points through `AreaCombatWorld.Facts`, the attack pause through `AttackValues`), strength, armour class adjustment and the other stats read through it, and so do the NPC's attacks and defences in the
  session (`Levelled` of `GameSessionNpcAttack`, the hero's attack on an NPC) and the AI host (`Build`). The viewer's round facts take it too.
- **The Effect component.** `SessionSpellCaster.EffectComponent` adds and removes on an NPC target through `AddNpcEffect` and `RemoveNpcEffect` (the census no longer lists the NPC's Effect: the college spells 10, 15, 20, 25, 37, 40, 46, 51, 67, 70, 76, 77, the NPC half of the Body spells and Shield of Protection, and the same records outside the colleges).
- **The magic save reads the effects (RE2 fixed, ARC-MAGIC-033).** The original's spell resistance (0x4537B0) reads the **stored** magic resistance (the object's resistance array, index 4) directly, not the effective one, so the
  `resistmagic` changes of effects 56 (Resist Magick), 154, 155, 51, 52 and 55 change what the character sheet's Magick line shows and nothing a save rolls. That is a bug of the game, and the owner decided (D6c, 2026-11-11) to fix it
  **in both modes with no switch**: `SpellResistanceRules.ResistanceUnderEffects` is the stored value under the effect aggregation (no change: the stored value exactly), cut to 0..95 for a PC and an ordinary NPC but never outside a wider
  stored value (an immune NPC stays immune), a monstrous NPC cut to the resolver's ±1000 only; the hero stores none, so its value is the effects alone (`GameSessionOwner.HeroSaveMagicResistance`, `SessionCritterCombat.SaveMagicResistance`).
  `SessionSpellCaster.ResistTarget` builds the facts with it, so the save, the entangle re-resistance and the AI's fail-chance predictor read the same value ([ledger RS11–RS13](../research/ledger/magic.md#resist-magick-in-the-magic-save-arc-magic-033)).
- **An NPC's maxima under its stats (ARC-WORLD-1144).** The maximum hit points and fatigue formulas read the effective strength, willpower and constitution (0x43D5A0, 0x43D630, 0x45D670 through the stat getter 0x4B0490, for a PC or an NPC alike), so
  the stat changes of the session's list reach them: `GameSessionOwner.WithStatEffects` re-reads the strength (also under each poison penalty, which lowers the base before the effects and the race's range cuts after them), the willpower, the
  constitution and the heal rate and poison recovery it derives, through the same character owner the NPC's combat stats use (`LifeFacts.Race` gives the range). It lays the list over the host's own facts, once; an add or a removal then moves the damage by the
  change of each maximum as for a maximum's own change (a buff does not heal). Facts built without a race count as human. The level is not a stat of that owner: a level change stays inert.
- **The poison tick and the sheet's resistance lines (ARC-UI-1145).** The normal resistance a poison tick meets (`LifeFacts.NormalResistance`, 0x43D6D0 with the normal type) is the stored value plus the worn armour's adjustment under the
  list's `resistnormal` changes, clamped as the combat's resistance is (0..95, a monstrous NPC ±1000): the host's facts carry the sum before the effects (`NormalResistanceBase`, `NormalResistanceUnclamped`) and
  `WithCritterEffects` works the value out from it, so a list laid over facts again gives the same (constitution does not enter the normal resistance, only the poison line's bonus). Facts without a base keep their value. The character sheet's five
  resistance lines now read the effects: the hero's (`QueryHeroSheetResistances`: worn armour, the poison bonus of the effective constitution and the durable effects' changes, the Magick line the worn magic resistance under the `resistmagic` changes; the
  doll's armour rating reads the same) and an NPC's (`SessionCritterCombat.SheetResistances` over `WithNpcEffects`: stored resistances, the poison bonus of the constitution an effect moved, the changes of its list; the Magick line the stored value under
  `resistmagic`, which the save still does not read, RE2 above; the NPC sheet reads no worn armour, CHW24). Tests: `NpcEffectStateTests` (the poison tick), `AreaCharacterSheetTests`, `AreaNpcSheetTests`.
- **Not done.** A list the map data stores and the session has not adopted gives no maxima, no stat changes and no normal-resistance change to its life facts (as before: the list becomes the session's with the first add or removal),
    the spell host's own NPC combatant (`SessionSpellCaster.Combatant`, line 550) and its stat getter (line 1182) still read the NPC without its list until ARC-DEV-352 (#941) is merged, since that
  file is that slice's; encumbrance and walking speed stay as they were (the original's walking does not read the speed stat, ledger XR rows of the audit).
- **Tests.** `EffectConsumerStateTests` (resistances of the hero and of an NPC, the clamp, monstrous NPCs, stat changes of an NPC's list, a loaded list with a host rule), `CombatSkillRulesTests.ResistanceEffectsAreAggregatedOverTheSumAndUnderTheClamp`,
  `SessionSpellEntangleTests` (the component on an NPC victim), the spell census `SpellCensusRealDataTests`, and for the magic save `SessionSpellCasterResistMagickTests` and `SpellResistanceRulesTests` (ARC-MAGIC-033).
