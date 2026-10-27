# NPC awareness: sight, hearing and the sound line (ARC-WORLD-078)

Status: **Implemented bounded component**, ARC-WORLD-078, NPC combat AI plan slice
[I2](../research/npc-combat-ai-plan.md#implementation-slices-proposals-after-the-matching-research).
`Arcanum.Simulation.Ai.AwarenessRules` answers whether a critter sees or hears another, as the
retail 1.0.7.4 executable computes it ([awareness research](../research/npc-ai-awareness.md),
stage A2; [ledger AW1–AW6, AW13, AW17, AW18](../research/ledger/npc-ai.md#npc-ai-awareness-arc-res-056),
two readers). Pure, Godot-free functions over explicit facts and an injected map in world cells; no
session state, no caller yet. The line of fire is ARC-COMBAT-012's
`LineOfFireRules.Trace` ([hero attack runtime](hero-attack-runtime.md)).

## Rules

- **Perception distance** (`PerceptionDistance`, 0x4AF240): `clamp(x, 1, 10)`.
- **Facing** (`Faces`, 0x45EFF0): `k = (direction − rotation + 8) mod 8`, where the direction from
  the observer's tile to the target's is `LineOfFireRules.Rotation` (5 for the same tile) and the
  rotation is the observer's current art's; k = 3, 4 or 5 (the three directions behind) is not faced.
- **Sight** (`See`, 0x4AF260) returns an `AwarenessResult`: the excess distance (0 = seen) and an
  `AwarenessReason`. 1000 with `State` for a stunned, blinded, sleeping or unconscious observer; 1000
  with `Invisible` for an invisible target unless the observer has extraordinary Perception or detects
  invisibility; 1000 with `Facing` for a target behind; 1000 with `Distance` beyond 1000 tiles.
  Otherwise `x = max(0, d − PerceptionDistance(P′))` with `d` the Chebyshev distance and
  `P′ = P − P × M / 100` (`StealthRules.PerceptionAfterMargin`); a line of fire with a blocker gives
  `x + 1` with `Blocked`, else `Range` or `Perceived`. The line's cover is discarded.
- **Hearing** (`Hear`, 0x4AF470): 1000 with `State` for a stunned or unconscious observer, with
  `Distance` beyond 1000 tiles. Sleeping halves Perception, then the margin lowers it;
  `H = (PerceptionDistance(P′ − 4) + radius − 2) / 2 − SoundCost` with the noise radius 2, 8 or 15 of
  `Loudness` Silent, Normal or Loud; heard when `d ≤ H`, else `d − H` with `Range`. Blindness, facing
  and invisibility do not matter.
- **Sound cost** (`SoundCost`, 0x4AF640): 0 for the same tile, 100 for a line that cannot be built
  (AW17); otherwise the sum of `SoundStepCost` over the steps of `LineOfFireRules.Walk` (the line of
  fire's own walk: 10-pixel screen samples from the observer's tile centre plus its pixel offset,
  AW18). An edge step costs 2 for each wall piece of any kind on the crossed edge (window and doorway
  pieces and a pass-walled wall too: re-read in the executable by this task), 1 for each closed,
  unbroken door or window there, 8 when the tile entered is soundproof (`IAwarenessMap.Soundproof`,
  0x4D7180), and the same wall and portal costs on that tile's opposite edge; objects cost nothing and
  nothing blocks; a neighbour off the map makes the step cost 1. A corner step sums four edge steps as
  the line of fire does.
- **Prowling margin.** `M` (0..100) is an input: the shared skill check
  (`SkillCheckRules.Effectiveness` and `Difficulty` for prowling, the prowler as source and the
  observer as target) through `StealthRules.Margin`; for the hero, `GameSession.HeroProwlingMargin`
  (ARC-SKILLS-008). `WithProwlerLine` carries the line traced from the observer to the prowler's tile
  into the sight difficulty's facts (a blocker hides the prowler, else the cost is the cover). The
  margin is 0 when the target is not concealed (sight) or not moving silently (hearing).
- **Clear shot** (`LineOfFireRules.ClearShot`, AW8): cover below 26 and no blocker but the target, the
  test of the two attack-goal callbacks; their weapon, reach and goal handling are slice I7's.

## Settings

`CombatAnomalyProfile.CornerStepCountsBothRoutes` (AW13, owner decision 2026-10-20): the Original
setting counts a soundproof corner tile on both routes around the corner (+16); Steamwoven counts it
once (+8). Side tiles and crossed edges count once in both. The same switch already governs the line
of fire's corner cover (AW13, AW14). AW15, AW17 and AW18 are kept in both settings.

## Tests

`AwarenessRulesTests` (Simulation, synthetic map): the study's worked numbers (Perception 8 seen at 6
and out by 4 at 12; the expert prowler of level 12 behind one cover object, margin 85 from the shared
check, seen at 2; behind a closed door margin 100, never seen; the hearing table; a closed door;
the moving-silently prowler with armour, margin 60; a wall edge and a soundproof tile cost 10), the
corner step in both settings, the line limit, and the FR-AI-002 matrix of observer states, invisibility,
facing, distance, blockers, concealment and loudness.

## Not yet

- Callers: the target scan and target validity (slice I4), witnesses and alert squares with
  `WitnessHandledTwice` (AW16, slice I4), the dynamite notice, the guards (S22), script conditions 28
  and 29, the attack set-up's unnoticed mark, the skill difficulty's −30 "unaware" term, the examine
  lines and the inventory window. None of them calls these functions yet.
- The session or viewer facts: critter flags, effective Perception, art rotation, invisibility and an
  NPC prowler's margin (only the hero's exists), and a host map with soundproof tiles.
- The editor's top-down view geometry (AW17), as for the line of fire.
- No AI parameter slot is read here: sight and hearing use the Perception stat and flags only.
