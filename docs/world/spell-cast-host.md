# Spell cast host (ARC-MAGIC-014)

Status: **Implemented bounded component; headless (no viewer caller yet).** Work item ARC-MAGIC-014 (2026-10-21), slice MT20a-1
"Cast host" of the [magic implementation plan](../research/magic-implementation-plan.md). The owner split MT20a on 2026-10-21:
- MT20a-1 (this slice) builds the world the run rules ask, over the session and any map, and the casting goal as the original's
  rules, and proves the first cast without a window;
- MT20a-2 (ARC-MAGIC-015) puts it in the AREA viewer: spell choice, targeting cursor, cast animation, texts and eye candy.

Requirements: [FR-MAGIC-003](../requirements/combat-magic-tech.md#fr-magic-003) (the invocation over a real map: line of fire,
areas), [FR-COMBAT-003](../requirements/combat-magic-tech.md#fr-combat-003) (a cast costs 4 action points).

## Verification

Two readers read the casting goal in the retail 1.0.7.4 `Arcanum.exe` ([ledger "Cast host and casting
goal"](../research/ledger/spell-runs.md#cast-host-and-casting-goal-arc-magic-014), CG1–CG8). The headless first cast
runs on the installed original (`AreaSpellCastRealDataTests`).

Code:
- [`SessionSpellRecords`](../../src/Arcanum.Session/SessionSpellRecords.cs) — the spell data in the rules' types;
- [`SessionSpellCaster`](../../src/Arcanum.Session/SessionSpellCaster.cs) — the run host, the damage world, the event pass's
  magic host and the casting goal;
- [`AreaSpellCastMap`](../../tools/terrain-viewer/Core/AreaSpellCastMap.cs) — the AREA map as a cast reads it;
- `GameSessionOwner.PaySpellFatigue` — a payment's fatigue.

Tests:
- [`SessionSpellRecordsTests`](../../tests/Arcanum.Session.Tests/SessionSpellRecordsTests.cs);
- [`SessionSpellCasterTests`](../../tests/Arcanum.Session.Tests/SessionSpellCasterTests.cs);
- [`AreaSpellCastRealDataTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaSpellCastRealDataTests.cs).

## The records (`SessionSpellRecords`)

Every record of `rules\spelllist.mes` translates field by field into the rules' types:
- **the run record:** cost, maintenance, duration and its stat, trigger count, willpower minimum, casting animation, `No_Stack:`
  (non-zero), cells, no-resist, no-reflect, cancels, item triggers, the components per action, the invocation check's record;
- **the resistance's record:** `Resist:`, the aggressive bit of `Info:` (0x2: aggressive and may-be-aggressive), a Damage
  component;
- **the target area of each action** (line +0), whose self bit (0x1) makes a self spell;
- **the components in file order,** numbered per action as the run rules number them, with their own area, `Apply_AoE:`, item
  triggers and the Damage and Heal data (MT12).

## The map (`ISpellCastMap`, `AreaSpellCastMap`)

The target functions' questions (MT8) over the shown map:
- **Objects.** A row with a session entity (an NPC, a container, an item of the inventory seed) answers under that id; any other row
  answers under `row:` kind and source index. The hero is the PC entity `pc` on its live cell.
- **NPC facts.** An NPC's facts are its combat values and stored fields with its life in the session: hit points, poison, the
  object, spell and critter flags, its stored magic resistance (resistance 4, O9), and its PC leader when it follows the hero.
- **Square and tile queries.** They read the rows on their cells; a carried item (object flag 0x1000) stands on no cell.
- **Direction.** The original's octant of a tile offset (0x4B8D50, 2:1 slope rule; no offset answers 5).
- **Line of fire.** It is the original's line (ARC-COMBAT-012) over the map's live line-of-fire map. A failed line scores 100.

Stand-ins, each a later slice's:
- one sector kind for the whole map (the indoor/outdoor test of an NPC's fallback tile);
- a path to every admitted tile;
- NPCs stand on their map cells;
- an NPC wields no weapon in a spell's context (MG36 only reaches a caster with a weapon);
- spell damage to objects other than critters is not ready (an acid's worn items take no damage step), and the hero's worn kit
  items carry no stored fields here, so the hero answers no worn items while wearing any.

## The run host (`SessionSpellCaster`)

The world the run rules ask (MT9a–MT12), for the hero and the map's NPCs:
- **The stage's facts.** These are:
  - the source's death and unconsciousness;
  - the parent's intelligence and willpower;
  - the local player;
  - the payer: a critter pays with its fatigue; worn mana stores are not modelled yet;
  - the source's race and mastered college.
- **Cells.** The hero's charge cells (ARC-TECHNOLOGY-007), for a payment the hero makes; any other payer's cells are not modelled
  and refuse.
- **Flags.** The forbidden flags (0x456430), the objects' spell flags and the local player's perception.
- **Resistance.** The resistance's facts: the stored magic resistance, the aptitude, the `Resist:` stat and its extraordinary
  value. The session's rolls and the hero's fate save drive it.
- **Targets.** The target collection of each action (MT8: self, source, origin, target, tile, other), the components' gate (the
  component area's test and the re-aim) and the fate "spell at maximum" (MT10).
- **Components.** Damage and Heal (MT12) are applied to the session's life:
  - an NPC through its life target, the hero as the hero;
  - the raise script is allowed (no SCR yet).

  Every other component type does nothing and is logged as not ready (MT13–MT18).
- **Stats.** By the original's numbers: 0..7 primary, 8..16 derived, 17 level.
- **Time facts.** Those of the duration and the maintenance, with the parent's mastered college (the hero's or an NPC's book).
- **The maintain bar.** It always has a slot (MT20b).

**Notes** (`Apply`). The payments of a call's notes are applied after the call:
- the stage's payment is charged to the run's source;
- a reflection's payment is charged to the reflection's payer;
- the fatigue goes through `GameSessionOwner.PaySpellFatigue`, a plain fatigue write.

The local player's messages, resisting targets (spell.mes 643) and freed runs go to the host's log for MT20a-2's feedback.

**The event pass.** The caster is also the pass's magic world (`ISessionSpellWorld`), so a run's ticks, END and interrupts run
through the same host. When the caster's event world is a script world (`ISessionScriptWorld`), the pass also runs deferred SCR
calls through it. The notes of the firings that ran are applied after the pass, so several ticks of one run in one pass each check
the fatigue from before the pass.

Further limits of the host:
- `SpellRunObject` lists of the run (the run list and the summoned object) are not filled; the target functions see none.
- An object that is not a critter has magic resistance 0.
- A refused call (a session rejection) may still have applied the components that ran before it.
- A critter without a life record in the session has no payer and pays nothing.

## The casting goal (CG rows)

The original's goals 26 and 82 (`ag_throw_spell`, with the record's casting animation) run these nodes, and 27 and 83
(`ag_attempt_spell`) play the cast:

1. **Start (`StartGoal`, 0x429B50).** The parent pays 4 action points (0x4B7CD0, only in a turn-based round in its own subturn;
   the hero overexerts as in ledger TB9; a hero knocked out by that overexertion still starts the goal here, and whether the
   original's later nodes stop it is not read: MT20a-2's). A refusal ends the goal. Then the pre-animation check (0x450940, PC3; a failed record lookup refuses): unless the run is
   free or has no source, the source must afford the cast spell's `Cost:` (its maintenance cost for another action) — no mastery,
   1 under the cheat, doubled for a dwarf — asked without applying. A refusal ends the goal.
2. **Target.** A target that is destroyed or off ends the goal (0x429BC0); the facing turn is the viewer's.
3. **Release frame (`Release`, 0x429C80).** At the animation's action frame the target is checked again (0x456A90,
   `TargetStillValid`):
   - the BEGIN area passes at once when it is 0, exactly 0x10, or has the self bit without the tile bit;
   - otherwise the picker's wrapper (0x4F2680) over the area test: a target object fails for object flag 0x800, is tested as the
     candidate and the target with no tiles, and then with its location as both tiles; a target tile is tested as both tiles. The
     wrapper's global gate (0x603D20) belongs to the picker and is not modelled.

   Then the run's BEGIN is processed (0x456F70). The release fires once per goal: a run already released, or whose BEGIN already
   ran, is not released again.
4. **End (`EndGoal`, 0x429CD0).** A goal that ends before its release frame frees the run with its BEGIN cost charged (0x456FA0:
   the run cost with mastery, applied). After the release frame (the goal's frame flag 0x8), or for a run whose BEGIN ran, the end
   frees nothing; the host forgets the release when the run is freed or a new goal starts for its slot, since the slot is reused.
   A goal starts once (`StartGoal`) for the run it was made for: a caller must not start a second goal for a run the original
   keeps after a failed release (MG41, Original), since the new start forgets that release and the end would then free the run.

## Differences from the original

Owner decisions 2026-10-20 and 2026-10-21: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A goal refused before its animation (action points, the pre-animation check, or a target destroyed or off) frees the run with its BEGIN charged: the caster pays for a cast that never started (MG39). | The run is freed unpaid. | `MagicAnomalyProfile.GoalRefusalChargesBegin` (pending the owner, recommended) |
| The charge of a run freed before its BEGIN ignores the free flag, the technology flag and `ChargeBeginCost: 0` (MG40). | Charged only when its BEGIN would have paid. | `MagicAnomalyProfile.GoalEndChargesAnyRun` (pending the owner, recommended) |
| A failed target re-check at the release frame leaves the run in its slot (MG41). | The run is freed unpaid. | `MagicAnomalyProfile.ReleaseRefusalKeepsRun` (pending the owner, recommended) |
| The stage writes its payment's fatigue before the components run. | The host applies the call's payments after the call, so a component reading the caster's fatigue in the same pass sees it unpaid. | — |
| A spell's life changes come from the components inside the run's pass. | The same, through the session's life calls; their revisions come before the run's own. | — |

Recorded for MT20a-2, which plays the goals (F1, one reader):
- **Friendly cast.** Goal 73 (the friendly cast) has no end and frees nothing on its refusals.
- **Queued cast.** A queued cast stores its casting animation in another slot than the one goal 83 plays.
- **Overlay art.** A refused goal's overlay removal is called with art 0.

## Not ready

- No viewer caller: the AREA viewer's casting (MT20a-2, ARC-MAGIC-015).
- The spell UI checks (0x57BC70, 0x57BFF0–0x57C110) and the predictor's display are MT20a-2's.
- Worn mana stores do not pay (no item store state).
- Spell damage to objects other than critters; NPC casters' weapons; the sector kind; paths.
- The raise and taking-damage scripts run no SCR.
