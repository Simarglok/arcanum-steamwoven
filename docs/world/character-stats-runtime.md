# Bounded character stats runtime

Status: **Implemented bounded component**, ARC-WORLD-012. An independently written,
Godot-free C# owner in `Arcanum.Simulation` (namespace `Arcanum.Simulation.Character`)
for [FR-CHAR-008..011](../requirements/characters-skills.md#fr-char-008): stored base
versus effective values, set-time limits, race/gender innate effects and read-only
derived stats. Since ARC-WORLD-015 the [game session's hero](starting-hero-runtime.md) is one
of these owners (saved in snapshot schema 6, effective intelligence for dialog gates). Since ARC-COMBAT-002 a query
context also carries the [life state](life-state-runtime.md)'s poison level and crippled legs; the viewer is not wired and
its conversation keeps the PROJECT diagnostic PC profile
(see [dialog conversation](../scripting/dialog-conversation-runtime.md)). Character
creation, skills, levels, backgrounds and effect lifetimes are separate requirements.

## Evidence and provenance

Rules profile `steamwoven-character-stats-v1`. Numbers are **source claims**: a reading
of the original's functions at the cited addresses, evidence level F0 (not checked in the
executable) unless the [verification ledger](../research/original-verification-ledger.md)
lists them, so retail parity is **NOT_VERIFIED**. The C# code, its structure and the
synthetic tests are independent MIT work.

| Claim | Source |
| --- | --- |
| Stored stat ranges and defaults (primary 1..20, default 8; magick/tech points 0..210, default 0) and derived clamp ranges | the original's stat tables |
| Racial primary maxima (dwarf/half-orc ST,CN 21; elf/dark elf DX,BE,WP 21; half-elf DX 21; gnome WP 22; halfling DX 22; half-ogre ST 24; ogre ST 26; orc ST,CN 22) | the racial stat maximum (0x4B0FC0, 0x4B0FD0) |
| Effective = base, then effects, then clamp to the race range | the effective stat getter (0x4B0490) |
| Derived formulas, derived writes ignored | the base stat getter and setter (0x4B0740, 0x4B0980) |
| Race/gender setter removes the old innate effect before applying the new one | setter side effects (0x4B0740, 0x4B0980), effect causes (0x4E9F70, 0x4EA100, 0x4EA200, 0x4EA2E0, 0x4EA3C0) |
| Aggregation: sum of percents, product of multipliers, product of divisors, sum of adds, largest minimum, smallest maximum, in that order | the effect aggregation (0x4EA690, 0x4EA6C0) |
| Innate effect contents (e.g. the female ST −1 / CN +1 effect, per-race effects) | Data: `rules\effect.mes` via the effect table load (0x4E99F0). **Not built in**: callers supply an `InnateEffectTable`. |

Derived formulas over effective inputs (C integer division, truncation toward zero):

| Stat | Base formula | Range |
| --- | --- | --- |
| CarryWeight | 500 × ST | 300..10000 |
| DamageBonus | ST − 10, halved if negative; doubled when ST ≥ 20 | −50..50 |
| AcAdjustment | DX − 10 | −9..95 |
| Speed | DX, +5 when DX ≥ 20 | 1..100 |
| HealRate | (CN + 1) / 3 | 0..6 |
| PoisonRecovery | CN | 1..20 |
| ReactionModifier | BE 1..19: source table entry `BE − 1` (−65 … 65); BE ≥ 20: 10 × BE − 100 | −65..200 |
| MaxFollowers | CH / 4 | 1..7 |
| MagickTechAptitude | (50 × MP − 55 × TP) / 10 + context sector adjustment | −100..100 |

"Extraordinary" means a primary stat whose **effective** value is at least 20.

## Context inputs (ARC-MAGIC-028, FR-CHAR-020)

`CharacterStatContext` carries what the original's stat getter (0x4B0490) reads from the world at the critter's place, all before effects and the range clamp
([ledger AQ1–AQ8](../research/ledger/magic.md#aptitude-inputs-arc-magic-028)):

- `SectorAptitudeAdjustment` (−100..100) is added to aptitude. The source is `WorldSector.AptitudeAdjustment()` of the sector the critter stands in: the second word of the sector file's
  metadata (version 3 and later; 0 otherwise or for a location outside the map). The Arcanum module has 24 non-zero sectors, −20 to 40.
- `Background`, `Hour`, `OutdoorTile` and `NatureTile`: the Day (62), Night (63), Sky (64) and Nature (65) Mage backgrounds move **magick points** by ±4 (+4 when the condition holds, −4 when
  not): Day by the hour 6..17, Night the reverse, Sky by an outdoor tile, Nature by a tile whose name has flag `n`. Aptitude reads the moved magick points. `Background` is the PC's
  background block (0 for an NPC and for any other background, which add nothing); `Hour` is 0..23 (an invalid hour is `InvalidContext`); the two tile facts come from
  `TileSoundTable.MageGround` over `WorldSector.TileArt` and are false where the sector cannot be locked (outside the map).

The session and viewer callers still pass 0 and no background; reading the sector and the tile where the hero stands is the next wiring slice.

## Accepted departures

- **Strict set-time limits.** `SetBase` rejects a value outside the current race range
  (`OutOfRange`, no state change) instead of silently clamping it; callers query
  `RangeOf` first. Writing a derived stat is a typed `DerivedStatReadOnly` rejection.
- **Race change keeps stored values.** A stored value above the new racial maximum stays
  stored (as in the source) and its effective value is clamped; it can only be lowered.
- **Removal by instance.** `AddEffect` returns an `AppliedEffectId`; `RemoveEffect`
  removes exactly that instance. Race and Gender causes are reserved for the setters,
  and their effects cannot be added or removed directly. Background and Class, which the
  source also treats as innate, are ordinary causes here until backgrounds are modelled.
- **No `-1` sentinel.** A minimum or maximum modifier of −1 is an ordinary bound.
- **Bounded arithmetic.** Explicit `CharacterStatLimits` (default 64 non-innate effects,
  7 modifiers per effect, |value| ≤ 1000; ceilings 4096/64/1,000,000). Divisor 0 is an
  invalid effect. Intermediate results saturate at ±1,000,000,000 (beyond that bound
  accumulation becomes order-dependent); source overflow behaviour is unknown.
- **Context is explicit.** Sector aptitude adjustment is a query argument bounded to
  −100..100, not read from a world location. Poison level (0..1000) and crippled legs
  (ARC-COMBAT-002) are query arguments too: poison lowers ST and DX by `min(3, poison / 100)`
  and crippled legs lower speed by 5, added to the base before effects and the clamp, as in
  the effective stat getter (0x4B0490). `GetBase` still reports the stored value or the formula.

## API

| Entry | Contract |
| --- | --- |
| `CharacterStatsOwner.Create(CharacterStatsDefinition)` | Validates race, gender, limits and every innate effect, copies the innate table, applies the race and gender effects at revision 0. Failure returns a typed failure and **no owner**. |
| `Query(CharacterStatContext)` | Detached `CharacterStatsSnapshot`: race, gender, revision, applied effects, `GetBase`/`GetEffective`/`GetRange`/`IsExtraordinary` for every stat. Derived values are recomputed from current state on every query, so they never go stale. |
| `SetBase(stat, value)` | Stored stats only. Changed, NoChange (same value) or Rejected (`InvalidStat`, `DerivedStatReadOnly`, `OutOfRange`). |
| `SetRace` / `SetGender` | Remove the old innate effect, set, apply the new one (missing table entry = no effect). Same value is NoChange. |
| `AddEffect(definition, cause)` / `RemoveEffect(id)` | Non-innate effects with typed rejection (`InvalidEffect`, `InvalidCause`, `EffectCapacityExceeded`, `UnknownEffect`). |

Every accepted mutation increments `Revision` once; rejected and no-op calls leave state
and revision unchanged. The owner is caller-serialized: no locks, events or callbacks.

## Not implemented

Skill/spell/tech prerequisite checks when lowering a stat; background,
Tempus Fugit, persuasion-expert and the other backgrounds' adjustments (the four mage backgrounds are rules, ARC-MAGIC-028); level,
experience, alignment, fate, unspent points, age and poison stats; HP/fatigue and
encumbrance recalculation on effect changes; effect lifetimes and durations (FR-CHAR-022);
skills and resistances; character creation beyond the pre-made start of
[ARC-WORLD-015](starting-hero-runtime.md) (which reads the innate effects from `effect.mes`); the character sheet UI.

