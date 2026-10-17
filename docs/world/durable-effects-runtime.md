# Durable effects runtime (ARC-WORLD-029)

Status: **Implemented bounded component**, ARC-WORLD-029 (2026-10-17), top-20 item 17. The hero's effect list in the
[game session](game-session-runtime.md): numbered effects with a cause that stay until something removes them (a
blessing, a curse, an item or spell effect, a script's effect), timed effects whose END waits on the session's
[event queue](game-event-queue-runtime.md) (ARC-WORLD-024), and what an effect does to the hero's stats, its two maxima
(hit points and fatigue) and its reaction effects. The list is saved (snapshot schema 13); an END is a saved event.
Requirements: [FR-CHAR-022](../requirements/characters-skills.md#fr-char-022) (effects, their causes and removal) and the
effect part of [FR-MAGIC-006](../requirements/combat-magic-tech.md#fr-magic-006) and
[FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007) (a timed run whose components add and remove an effect).
Poison is not an effect: its level and ticks are the [life state](life-state-runtime.md)'s (ARC-COMBAT-002, ARC-WORLD-024).
Not wired into the viewer ([Not ready](#not-ready)).

## Evidence

- CE at the pinned commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`: `effect.c` (add, the four removals, count, the
  internal removal, the aggregation), `object.c` (`object_hp_damage_set`, `object_hp_max`), `critter.c`
  (`critter_fatigue_damage_set`, `critter_fatigue_max`), `magictech.c` (the Effect component, the death hooks),
  `script.c` (the add-effect and remove-effect actions). No code or structure was copied.
- The retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`), read statically per the
  [original executable check](../research/original-executable-check.md) by two independent readers; rows EF1–EF10 in the
  [verification ledger](../research/original-verification-ledger.md#durable-effects-arc-world-029). **The original does what
  CE reads** in every row; it adds only a network branch before the critter test of add and typed removal, which a single
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
| Add (`effect_add`, 0x4E9F70) | Appends number and cause. Before and after it reads strength, the encumbrance level, maximum hit points and maximum fatigue: a strength change recalculates encumbrance; a change of either maximum is **added to that damage** through its setter, which clamps at 0. Then the animation speed. |
| Remove one typed (`effect_remove_one_typed`, 0x4EA100) | The **first** entry with the number, whatever its cause; then the animation speed, also when nothing matched. |
| Remove all typed, one or all caused by (0x4EA200, 0x4EA2E0, 0x4EA3C0) | Every entry with the number; the first or every entry with the cause. No animation speed. |
| Internal removal (0x4EA520) | Shifts the later entries up in both arrays and shortens them; the same strength and maxima handling as add. |
| Count (0x4EA4A0) | Entries with the number. |

Consequences: current hit points and fatigue stay when a maximum rises (the damage grows with it: a buff does not heal), and
fall only to the new maximum when it sinks below them; each entry is its own step, so a removal of several entries can
leave less than one combined step would (see the tests). The hit-point setter schedules the resting timer on a positive
write, the fatigue setter the 80-second recovery on a non-zero one; the faint consequences run only when a non-zero write
makes the critter unconscious, which a shift that keeps the current value never does (EF7, EF10).

The aggregation (`effect_adjust_func`, 0x4EA6C0, EF6) sums the percent changes, multiplies the multiply and divide factors,
sums the adds, takes the highest minimum and the lowest maximum, and applies them in that order; −1 is the "unset" mark of
both bounds, so a bound of −1 is never applied and a −1 met after a lower minimum or a higher maximum clears it (no
installed rule uses −1); its "ignore innate" variant skips race, background, class and gender. Maximum hit points and maximum fatigue apply their effects to the whole
sum of points, adjustment and stat part (`object_hp_max` 0x43D5A0, `critter_fatigue_max` 0x45D670, EF8).

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
| `AddHeroEffect(expected, definition, cause, count = 1)` | `effect_add` `count` times. Refusals in order: no hero (`EffectsUnavailable`); a rule the session does not admit, a cause of the race and gender setters or the life domain's scars (injury), a count below 1 (`InvalidEffect`); an innate number (`InnateEffect`: its typed removal would meet the innate entry first); a number in use with other changes (`EffectMismatch`); the version; more than 64 durable entries (`EffectCapacityExceeded`); entries that could leave the consumers' bounds once some of them are removed (`InvalidEffect`, below); the event budget and revision. One revision. |
| `AddHeroTimedEffect(expected, definition, cause, count, durationMs)` | The same, and an END event (`effect.hero.end`) after 1 ms up to `int.MaxValue × 8000` ms of game time (a `Duration:` in real seconds × 8000); at most 64 pending ENDs. The result's `TimedEffect` is its queue sequence. |
| `RemoveHeroEffect(expected, number, count = 1)` | `effect_remove_one_typed` `count` times; an innate number is `InnateEffect`; NoChange when nothing matches. |
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
Inert until their owners exist: skills, tech skills, resistances, tech disciplines, the critical-hit specials,
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
  305 parse and 5 use `*` or `/`, which the effect reader refuses (ARC-RES-027 SH-07, decision 7 of the ledger still
  open); all 305 but the fixture half-elf's race effect 67 are added and removed; 191 change a modelled stat, 7 the maximum
  hit points, 7 the maximum fatigue, 5 the reaction, 4 the bad and 3 the good reaction adjustment.

## Not ready

- **No host.** Nothing in the viewer adds an effect yet: no spell casting, no item spells (wearing such an item is still
  refused), no blessings or curses, no SCR add-effect or remove-effect action (the dispatcher admits neither), no effect
  shown on a character sheet.
- **NPCs.** Only the hero has an effect list; NPC lists come from their map data and are not loaded.
- **Runs.** A timed effect stands for a run made only of Effect components. Active spells (magic slice 4) own maintenance,
  the `INT / 4` cap, the scaled count and its MG17, and every ending trigger: they will call `EndHeroTimedEffect` or replace
  the END kind. The session ends no timed effect by itself on death, unconsciousness, rest or travel.
- **Dead without a death time.** An effect that takes the hero's hit points to none (EF10) runs no death step: the hero
  reads as dead with no death time, and removing the effect leaves it there (the maximum returns to the damage). Hosts must
  not take a death time as the only sign of death.
- **Not modelled:** encumbrance (a strength change does not recalculate it), the animation speed, the injury cause (the
  life domain counts scars; their effect 50 changes nothing in the session), the innate effects' non-stat changes (the
  gnome's bad-reaction adjustment, the halfling's critical-hit chance: ARC-WORLD-015 keeps only stats), multiply and divide
  changes (refused by the reader), background and class effects of a new character.
