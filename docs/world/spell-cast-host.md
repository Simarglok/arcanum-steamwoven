# Spell cast host (ARC-MAGIC-014)

Status: **Implemented bounded component; the AREA viewer casts through it (ARC-MAGIC-015).** Work item ARC-MAGIC-014 (2026-10-21), slice MT20a-1
"Cast host" of the [magic implementation plan](../research/magic-implementation-plan.md). The owner split MT20a on 2026-10-21:
- MT20a-1 (this slice) builds the world the run rules ask, over the session and any map, and the casting goal as the original's
  rules, and proves the first cast without a window;
- MT20a-2 (ARC-MAGIC-015, 2026-10-27) puts it in the AREA viewer: the spell pane, the targeting cursor, the casting goal's timing,
  the feedback texts and the eye candy as presentation events ([below](#the-viewer-arc-magic-015)).

Requirements: [FR-MAGIC-003](../requirements/combat-magic-tech.md#fr-magic-003) (the invocation over a real map: line of fire,
areas), [FR-COMBAT-003](../requirements/combat-magic-tech.md#fr-combat-003) (a cast costs 4 action points),
[FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012) (the spell UI: activation, target choice, refusals; ARC-MAGIC-015),
[FR-COMBAT-006](../requirements/combat-magic-tech.md#fr-combat-006) (the cast's release frame; ARC-MAGIC-015).

## Verification

Two readers read the casting goal in the retail 1.0.7.4 `Arcanum.exe` ([ledger "Cast host and casting
goal"](../research/ledger/spell-runs.md#cast-host-and-casting-goal-arc-magic-014), CG1–CG8). The headless first cast
runs on the installed original (`AreaSpellCastRealDataTests`). The spell UI is read in [ledger "Spell
UI"](../research/ledger/spell-runs.md#spell-ui-arc-magic-015) (SU1–SU14, MG42) and the queued cast in [ledger "Queued
cast"](../research/ledger/spell-runs.md#queued-cast-arc-magic-019) (QC1–QC6, MG43); the viewer's cast, with a second cast queued
behind it, is observed by the L2 native smoke `ARC-MAGIC-015 native-smoke cast`.

Code:
- [`SessionSpellRecords`](../../src/Arcanum.Session/SessionSpellRecords.cs) — the spell data in the rules' types;
- [`SessionSpellCaster`](../../src/Arcanum.Session/SessionSpellCaster.cs) — the run host, the damage world, the event pass's
  magic host and the casting goal;
- [`AreaSpellCastMap`](../../tools/terrain-viewer/Core/AreaSpellCastMap.cs) — the AREA map as a cast reads it;
- [`SpellUiRules`](../../src/Arcanum.Simulation/Magic/SpellUiRules.cs) — the spell UI's activation, cursor mask and no-target message
  (ARC-MAGIC-015);
- [`AreaSpellCasting`](../../tools/terrain-viewer/Core/AreaSpellCasting.cs) and the viewer's
  [`TerrainViewerSpellCast`](../../tools/terrain-viewer/Godot/TerrainViewerSpellCast.cs), [`AreaSpellPane`](../../tools/terrain-viewer/Godot/AreaSpellPane.cs)
  — the viewer's casting (ARC-MAGIC-015);
- `GameSessionOwner.PaySpellFatigue` — a payment's fatigue.

Tests:
- [`SessionSpellRecordsTests`](../../tests/Arcanum.Session.Tests/SessionSpellRecordsTests.cs);
- [`SessionSpellCasterTests`](../../tests/Arcanum.Session.Tests/SessionSpellCasterTests.cs);
- [`AreaSpellCastRealDataTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaSpellCastRealDataTests.cs);
- [`SpellUiRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellUiRulesTests.cs),
  [`AreaSpellCastingTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaSpellCastingTests.cs) (ARC-MAGIC-015).

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

## The viewer (ARC-MAGIC-015)

The AREA viewer (`--hero`, exterior, cave and `--area-map`) casts as the original's spell UI does (ledger SU1–SU14):

1. **The spell pane.** `M`, on its release as in the original (SU14), shows or hides a pane with one button per spell the hero
   knows (the spell book's ranks, college by college) and the cost the original's UI shows (`CastCost`). The original switches
   its interface bar to the spell pane; the viewer has no bar, so the key toggles the pane. The pane is no window: the world
   keeps its clicks.
2. **The activation** (`SpellUiRules.Activate`, SU1–SU3). Nothing for a hero that is dead or unconscious or a disabled spell. A BEGIN
   area 0, exactly the radius bit, or self without the tile bit is cast at once at the hero's tile. Otherwise the action points are
   checked (4 in a turn-based round, the session's preview of the charge; since ARC-COMBAT-020 the viewer hosts one) and the targeting cursor opens with the BEGIN area, plus
   the non-party bit for an aggressive spell while no Alt key is held; an Alt key held in the cursor removes it (SU7). With Shift, an
   object spell that may target its caster is cast at once at the hero. An open cursor is closed first.
3. **The cursor** (SU9). The hover shows "cast" (cross cursor) where a left press would cast and "no target" (forbidden cursor)
   elsewhere. A left press casts at the object under the pointer when it passes the area test, else at the hero when its tile is under
   the pointer, else at the tile; a failure shows spell.mes 606 for "not damaged" (SU10) and keeps the cursor. The cursor stays open
   after a cast, as in the original, until a right press, another activation, a window (inventory, sheet, logbook, dialog …) or a
   map change.
4. **The cast** (`SessionSpellCaster.CastAt`, SU8): spell.mes 600 when the hero cannot afford the spell, 604 without a line of fire,
   then the run; its casting goal starts (`StartGoal`; a refusal ends it, `EndGoal(refused: true)`), and its eye candy (below,
   since ARC-MAGIC-017) times the release frame (`Release`: the re-check and BEGIN) and the end (`EndGoal`) on the world clock. While the goal plays, world presses and the stick are ignored (the original queues other goals under it). A goal
   cut short before its release frame (a lost hero, a map change, another actor) ends through its own host and frees its run
   (0x429CD0); a goal of a session a quick load replaced is dropped with that session. Before the cast eye candy is added the
   target is checked again (0x429F00, QC4): a failure releases at once (nothing begins) and the goal ends through the end step,
   without playing the cast eye candy (since ARC-MAGIC-019; before, the eye candy played to the release frame's re-check).
5. **Feedback.** The host's new log entries become one line in the status bar and the message window: spell.mes messages by
   number (the player's own texts, `SpellTexts.Message`), the fatigue paid, damage, healing, resistance and the component types
   that are not ready. Eye candy is drawn ([below](#eye-candy-arc-magic-017)); only an eye candy's callback gets a line.
6. **Events.** While the session holds a run, or the shown map's host exists, the event pump's passes go through the shown
   map's host (`SessionSpellCaster.ProcessEvents`, only a host of the pump's own session), so a run's later events run with the
   map's magic world and their payments are applied.
7. **A second cast** (ARC-MAGIC-019, `SessionSpellCaster.Queue`, SU13, QC1–QC6). A cast while a goal plays (from the cursor,
   which stays open, or from the pane) is checked and started as any cast (600, 604, the run), then queued beneath the playing
   goal: at most four wait (the playing goal's stack index 4 or more refuses, QC2), and none in a turn-based round; a refused
   cast's run is freed unpaid and the original shows nothing (the viewer's status line names the refusal, a diagnostic). The queued casts play in the order they were cast (QC1): when the playing
   goal ends after its cast eye candy (a pop, QC3), the first queued goal starts (`StartGoal`, the same checks as a first goal).
   A goal without cast eye candy, one whose target failed before it, or a queued goal refused at its start ends through the end
   step, and a goal cut short — or a hero who cannot act when the goal ends — interrupts the whole stack: either way the casts still queued never play (QC5), their runs freed
   unpaid in both modes (`DropQueued`, MG43, owner decision 2026-10-28). A queued run exists from the click, so a run starting before it can cancel it by
   its `Cancels_SF:` (0x4534E0) like any active run of the same source (QC6); its slot is free again, so the viewer forgets a queued
   entry whose slot a newer cast takes. F7 stays refused while a goal plays.

The first cast (plan milestone) is the native smoke: the pre-made hero learns the first damaging rank-1 college spell, opens the
pane, activates it and casts with the cursor at the core NPC with the fewest hit points of the shown map (exterior, cave or
`--area-map`); its fatigue drops and the NPC resists or takes damage. A second click while the goal plays queues a second cast
(ARC-MAGIC-019), which plays after the first and pays at its own release (an NPC the first cast killed is raised before the
queued goal's first frame); the drops (no eye candy, a target failing first, a refused start, a lost hero) have no smoke.

## Eye candy (ARC-MAGIC-017)

The eye-candy table ([format](../formats/spell-eye-candy.md), `SpellEyeCandyTable`) is loaded with the spell data. The rules are
Godot-free (`Arcanum.Simulation.Magic.SpellEyeCandyRules`, ledger [EC rows](../research/ledger/spell-runs.md#spell-eye-candy-arc-magic-017)):

1. **The cast (`CastEyeCandyAnimation`, goal 27, EC7–EC8).** The cast entry (6·spell) is placed on the caster at frame 0. Its timing
   part is the underlay, else the foreground, else the background; the pace is 1000 / the frame rate of the part the play built
   last (the underlay, else the background, else the foreground). Each step moves the
   animated parts one frame (with an underlay only the underlay, else the foreground and the background), the first at once and
   the others one pace apart. The timing part's new frame equal to its action frame (a foreground or underlay only) fires the
   release frame once: a passed release steps again at once, a failed one ends the goal at once. A step that finds a part at its
   last frame ends the goal one pace later. So frame k shows at (k − 1)·pace before the release and (k − 2)·pace after it; an
   action frame of 0 or past the last frame never releases, and the end frees the run (CG7). Without a cast eye candy (no art, no
   part, an ART that does not read, a rate of 0, no table) the release runs at once and the goal ends.
2. **Its own goal (`EyeCandyAnimation`, goal 45, EC9–EC10).** The release's secondary cast entry (6·spell + 4, on the caster, after
   a passed re-check) and an EyeCandy component's entry play only when the entry animates and has a foreground or underlay part:
   frame k at k·pace, the parts removed at frames·pace; an `anim_forever` entry's parts loop from frame 0 every frames·pace
   until a Remove.
3. **The component (`SessionSpellCaster`, EC11).** An EyeCandy component of a run's pass logs `EyeCandyAdded` (skipped for a run forced
   to end) or `EyeCandyRemoved` with its target object, the entry (6·spell + its number) and its play flags; no target object,
   nothing. A Remove takes only this run's entry unless `no_id`.
4. **The draw (the AREA viewer, EC12).** Each placed part is an extra-art body (`AreaExtraArt`, class 14 through
   `art\eye_candy\eye_candy.mes`) at its object's tile (the hero's committed cell, an NPC's live cell): the underlay and the
   background ordered right before the object, the foreground right after it; the foreground and background at the art id's draw
   scale, the underlay unscaled; a translucent part additive and unlit. The order is the object's own place (an NPC's T0 body with
   its offset ranks; the standing hero's cell), one rank below or above. A play needs a free slot of each list it places in (7
   foreground, 7 background, 4 underlay per object, EC9), else nothing is shown. A map change or a quick load clears them; the
   goal's end removes the cast entry. Each frame of a part is one art id of the viewer's extra art (at most 4,096 art ids and
   64 MiB of pixels per run, shared with the ground items); past it a frame is not drawn and said once.

## Differences from the original

Owner decisions 2026-10-20, 2026-10-21 and 2026-10-27: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A goal refused before its animation (action points, the pre-animation check, or a target destroyed or off) frees the run with its BEGIN charged: the caster pays for a cast that never started (MG39). | The run is freed unpaid. | `MagicAnomalyProfile.GoalRefusalChargesBegin` (owner decision 2026-10-27: fix) |
| The charge of a run freed before its BEGIN ignores the free flag, the technology flag and `ChargeBeginCost: 0` (MG40). | Charged only when its BEGIN would have paid. | `MagicAnomalyProfile.GoalEndChargesAnyRun` (owner decision 2026-10-27: fix) |
| A failed target re-check at the release frame leaves the run in its slot (MG41). | The run is freed unpaid. | `MagicAnomalyProfile.ReleaseRefusalKeepsRun` (owner decision 2026-10-27: fix) |
| The stage writes its payment's fatigue before the components run. | The host applies the call's payments after the call, so a component reading the caster's fatigue in the same pass sees it unpaid. | — |
| A spell's life changes come from the components inside the run's pass. | The same, through the session's life calls; their revisions come before the run's own. | — |
| A pick with no target shows spell.mes 606 when the area test's failure global holds "not damaged" — a global no pick clears, so an earlier test's reason can show (MG42). | 606 only for the current pick's own reason. | — (owner decision 2026-10-27: fix without a switch) |
| Queued casts whose goals never play (the stack ended through the end step or was interrupted) keep their runs in their slots, unbegun and unpaid, until the game ends (MG43). | The runs are freed, unpaid. | — (owner decision 2026-10-28: fix in both modes, no switch) |
| A queued goal is the casting goal 26 with its own get-up, unconceal and facing nodes and subgoals (QC2). | The viewer's goals have no subgoals and no facing turn, so the stack index is the number of queued casts. | — (not ready: the facing turn) |
| An eye candy's sound and light are played with it; a multi-rotation eye candy turns to its object's art; `can_autoscale` sizes it to a critter's body; an eye candy sits at its object's offsets. | None of these (EC9). | — (not ready) |
| A callback play flag makes the run's CALLBACK action wait for the eye candy's animation (EC11). | The callback never comes: the eye candy is drawn and the run goes on as if none were asked. | — (not ready) |
| A foreground or background part is scaled by the object's scale too (EC12). | Every object is taken at 100 %. | — (not ready) |
| An eye-candy ART with a frame rate of 0 divides by zero (EC8). | Treated as no eye candy: the release runs at once. | — (no installed ART has one) |

Recorded for MT20a-2, which plays the goals (F1, one reader):
- **Friendly cast.** Goal 73 (the friendly cast) has no end and frees nothing on its refusals.
- **Queued cast.** A queued cast stores its casting animation in another slot than the one goal 83 plays (no college spell has a
  casting animation).
- **Overlay art.** A refused goal's overlay removal is called with art 0.

## Not ready

- The missile's art and flight (goal 28, the projectile entry 6·spell + 1); the facing turn of goal 26 (0x42C850).
- Eye-candy sounds and lights, tinting, rotation, `can_autoscale`, the object's scale and offsets and the eye-candy callbacks
  (EC9, EC11, EC12).
- Other goals while a cast plays: the viewer ignores world presses and the stick until the goal and its queue end, and refuses F7
  meanwhile (the session saves no goal or queue).
- Spells on an inventory item (the inventory bit: the original opens the inventory to pick the item); item and hotkey activation
  (0x57C040, 0x57C080, read as IT9). The host runs an item's own spells with the item as source since ARC-MAGIC-020
  ([item spell triggers](item-spell-triggers.md)); the picker 0x4F28A0's own object/tile order (the viewer's: object, hero tile, tile).
- A run's events on a map the viewer has no cast host for (no combat host, or no spell data yet) get plain passes, which refuse
  them.
- In a turn-based round (ARC-COMBAT-020) the casting goal's start charges its 4 action points through the round's world; outside
  one nothing is charged. The predictor has no display in the original's UI (SU11), so none is planned.
- Worn mana stores do not pay (no item store state).
- Spell damage to objects other than critters; NPC casters' weapons; the sector kind; paths.
- The raise and taking-damage scripts run no SCR.
