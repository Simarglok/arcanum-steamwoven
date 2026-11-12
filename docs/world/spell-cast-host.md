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
behind it, is observed by the L2 native smoke `ARC-MAGIC-015 native-smoke cast`. The missile and the facing turn (ARC-MAGIC-018)
are read in [ledger "Spell missile and caster facing"](../research/ledger/spell-runs.md#spell-missile-and-caster-facing-arc-magic-018)
(SM1–SM9, MG44); the same smoke casts a missile spell. The cursor's pick is read in [ledger "Target
picker"](../research/ledger/spell-runs.md#target-picker-arc-magic-021) (SU15–SU19, MG45).

Code:
- [`SessionSpellRecords`](../../src/Arcanum.Session/SessionSpellRecords.cs) — the spell data in the rules' types;
- [`SessionSpellCaster`](../../src/Arcanum.Session/SessionSpellCaster.cs) — the run host, the damage world, the event pass's
  magic host and the casting goal;
- [`AreaSpellCastMap`](../../src/Arcanum.World/AreaSpellCastMap.cs) — the AREA map as a cast reads it;
- [`SpellUiRules`](../../src/Arcanum.Simulation/Magic/SpellUiRules.cs) — the spell UI's activation, cursor mask and no-target message
  (ARC-MAGIC-015) and the cursor's pick (`Pick`, `PickTest`, `PickSpares`; ARC-MAGIC-021);
- [`AreaSpellCasting`](../../src/Arcanum.World/AreaSpellCasting.cs) and the viewer's
  [`TerrainViewerSpellCast`](../../tools/terrain-viewer/Godot/TerrainViewerSpellCast.cs), [`AreaSpellPane`](../../tools/terrain-viewer/Godot/AreaSpellPane.cs)
  — the viewer's casting (ARC-MAGIC-015);
- `GameSessionOwner.PaySpellFatigue` — a payment's fatigue;
- [`SpellMissileRules` and `SpellFacingRules`](../../src/Arcanum.Simulation/Magic/SpellMissileRules.cs) and the viewer's
  [`TerrainViewerSpellMissiles`](../../tools/terrain-viewer/Godot/TerrainViewerSpellMissiles.cs) — the missile and the facing turn
  (ARC-MAGIC-018).

Tests:
- [`SessionSpellRecordsTests`](../../tests/Arcanum.Session.Tests/SessionSpellRecordsTests.cs);
- [`SessionSpellCasterTests`](../../tests/Arcanum.Session.Tests/SessionSpellCasterTests.cs);
- [`AreaSpellCastRealDataTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaSpellCastRealDataTests.cs);
- [`SpellUiRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellUiRulesTests.cs),
  [`AreaSpellCastingTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaSpellCastingTests.cs) (ARC-MAGIC-015);
- [`SpellMissileRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellMissileRulesTests.cs) and the missile census in
  `SpellEyeCandyRealDataTests` (ARC-MAGIC-018);
- [`SpellPickRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellPickRulesTests.cs) (ARC-MAGIC-021).

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
- a path to every tile the terrain verdict does not block (objects and reachability are not tested; ARC-MAGIC-055: before, every admitted tile);
- NPCs stand on their map cells, except the companions: since ARC-MAGIC-021 a companion's row stands on its committed cell and on
  none while it is on another map (no target and no critter then); since ARC-DEV-076 (MIG3) a visitor from another map is a target,
  a party member of the hero and a critter too, found by identity through the record resolver (`AreaSpellCastMap`'s `visitors`: the
  companions the viewer's group carried here, standing where the group places them, never on their home-map cell; their combat values,
  worn items and the items' spells come from the resolver); in a square query a companion comes after the rows of the swept cells;
- an NPC wields no weapon in a spell's context (MG36 only reaches a caster with a weapon);
- spell damage to objects other than critters is ready since ARC-DEV-255 for the viewer's doors, windows, containers, scenery and traps ([object damage](object-damage-runtime.md#damage-that-is-no-hero-attack-arc-dev-255)); since ARC-COMBAT-025 an item lying on the ground and an acid's worn items (an NPC's, the hero's starting kit) take the damage step too ([item damage](object-damage-runtime.md#damage-to-items-that-is-no-hero-attack-arc-combat-025)); any other item the hero wears is not listed.

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

  - ObjFlag (ARC-DEV-302, MT13's hold part, [below](#hold-flags-arc-dev-302)) writes the three hold flags on a critter (the hero or an
    NPC): entangled and stoned among the spell flags, paralysed among the critter flags; and (ARC-DEV-304,
    [below](#spoken-with-dead-arc-dev-304)) the spoken-with-dead spell flag of spell 56.
  - Effect (ARC-DEV-303, [below](#effect-component-and-the-entangle-re-resistance-arc-dev-303)) adds and removes an effect on the hero (an NPC has no
    effect list in the session: not ready); and a hit on an entangled critter asks its save again (`CritterHit`).

  Every other component type, and ObjFlag for any other flag or field, does nothing and is logged as not ready (MT13–MT18).
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
- The run's object list (+0x130) is the run's own (`SpellRun.ObjectList`, ARC-MAGIC-1234: [run object list](#run-object-list-arc-magic-1234)); the summoned list (+0x134) is the session's created objects of the run.
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
2. **Target.** A target that is destroyed or off ends the goal (0x429BC0).
3. **Facing turn (`StartGoal`'s facing, 0x42C850, ARC-MAGIC-018).** After the range node, goal 26 turns the parent toward its
   target object (`SpellFacingRules.Turn`, ledger SM rows). A parent stoned (spell flag 0x10000), or a PC or NPC parent stunned or
   paralysed (critter flags 0x60), refuses: the goal ends through its end node (`SpellCastGoalOutcome.CannotTurn`, MG39's path).
   Without a target object (a tile cast) or with the parent as its target, nothing turns. Otherwise the parent turns at once, with
   no animation, to the rotation from its tile toward the target's (0x4B8D50); the host sets it on the parent's art.
4. **Release frame (`Release`, 0x429C80).** At the animation's action frame the target is checked again (0x456A90,
   `TargetStillValid`):
   - the BEGIN area passes at once when it is 0, exactly 0x10, or has the self bit without the tile bit;
   - otherwise the picker's wrapper (0x4F2680) over the area test: a target object fails for object flag 0x800, is tested as the
     candidate and the target with no tiles, and then with its location as both tiles; a target tile is tested as both tiles. The
     wrapper's global gate (0x603D20) belongs to the picker and is not modelled.

   Then the run's BEGIN is processed (0x456F70). The release fires once per goal: a run already released, or whose BEGIN already
   ran, is not released again.
5. **End (`EndGoal`, 0x429CD0).** A goal that ends before its release frame frees the run with its BEGIN cost charged (0x456FA0:
   the run cost with mastery, applied). After the release frame (the goal's frame flag 0x8), or for a run whose BEGIN ran, the end
   frees nothing; the host forgets the release when the run is freed or a new goal starts for its slot, since the slot is reused.
   A goal starts once (`StartGoal`) for the run it was made for (a failed release frees its run in both settings, MG41).

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
   elsewhere. A left press casts at what the pick finds, in the original's order (`SpellUiRules.Pick`, `SessionSpellCaster.PickTarget`,
   ARC-MAGIC-021, SU15–SU19): the object under the pointer (the hover's screen pick, which for the pick also sees through the hero
   with a no-self mask, the hero's followers with the non-party bit and dead critters with the not-dead bit), else the objects on the
   tile under the pointer (the hero first, then the map's rows), else that tile. A scenery object stands for its occupant; an object
   that fails is tried once more as its own tile, which then is the target (a tile spell clicked on an NPC casts at the NPC's tile).
   A failure shows spell.mes 606 for "not damaged" (SU10), the last reason this pick's tests left, and keeps the cursor. The cursor stays open
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
   that are not ready. Eye candy is drawn ([below](#eye-candy-arc-magic-017)); a summon's refusal gets a line (ARC-DEV-352).
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
   entry whose slot a newer cast takes. ARC-DEV-318 (MIG14 S9c): the queued goals are goal-26 frames at the bottom of the hero's casting run, as an NPC's are, and F7 saves the playing goal with its queue ([saved runs](../architecture/saved-runs.md)).

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
2. **Its own goal (an overlay run, goals 45–48, 58, 59, 75, 76, EC9–EC10, ARC-DEV-143).** The release's secondary cast entry (6·spell + 4, on the caster, after
   a passed re-check) and an EyeCandy component's entry play only when the entry animates and has a foreground or underlay part. The play flags choose the goal
   (callback 47/48, end callback 58/59, fire damage 75/76, reverse the twin; the callback flag wins) and the run starts beside the owner's main run
   (`AreaEyeCandyHost.Play`, no interrupt; a full list of the owner's 7 foreground, 7 background or 4 underlay places, or a duplicate with `check_already`, plays nothing;
   `stack` queues it under the previous run): frame k at k·pace (58/59 begin with no wait, so one frame earlier), the parts leave at frames·pace; an `anim_forever` entry's parts loop from frame 0 every
   frames·pace until a Remove. A callback goal calls back once at the timing part's action frame (47/48) or at its end (58/59); since ARC-DEV-352 the viewer gives the call to the run (its CALLBACK or END_CALLBACK action, [summon](#summon-for-objects-arc-dev-352)). An AGoal component for goal 44
   (`Hover`) starts the object's hover run and an AGoalTerminate for 44 (`HoverEnded`) ends it.
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

## The missile (ARC-MAGIC-018)

A spell whose projectile eye candy (entry 6·spell + 1) has art launches a missile at the casting goal's release frame instead of
releasing (goal 27's node 0x429C40, ledger [SM rows](../research/ledger/spell-runs.md#spell-missile-and-caster-facing-arc-magic-018));
the BEGIN waits for the missile's arrival. The rules are Godot-free (`SpellMissileRules`), the session's part is in `SessionSpellCaster`:

1. **The launch (`LaunchMissile`, SM1).** The release gate marks the goal's frame handled, so the casting goal's end frees nothing;
   there is no target re-check and no BEGIN now. The secondary cast eye candy (6·spell + 4) plays on the caster at the launch (it
   does not play again at the arrival). The casting goal goes on with its own eye candy one pace later (`PaceAfterRelease`) and
   ends by itself. Only the gate of a cast eye candy launches: a cast without one, or whose target fails before its eye candy
   (QC4), releases at once without a missile (EC7).
2. **The missile (SM2).** It leaves the caster's tile for the target object's tile (a tile cast: the tile), its art the projectile
   entry's, faced in the screen direction between the two tiles (0x4B6A00, 0x4B6B10 for an eye-candy art: rotation in bits 9..11,
   number + direction / 8), unlit.
3. **The flight (`Leg`, `Tick`, SM3–SM5).** A leg is the combat projectile's line (10-pixel steps between the two tiles' centres,
   at most 100). Every 35 ms the missile takes the entry's `Proj Speed:` steps (4 when 0), one art frame a step, and nothing on the
   tiles it crosses is tested: no strike, no obstruction, no range. When a leg's steps run out it waits one pace of its art
   (1000 / its frame rate, 100 ms when that does not read), then (`LegEnd`): on its destination with its target object there, or a
   tile cast, it arrives; a target that moved is followed on a new leg from the missile's tile; elsewhere it flies its line again.
   A missile already on its destination at the launch (a cast at the caster's own tile) arrives at once.
4. **The arrival (`ArriveMissile`, SM3; SM6 for a failed re-check).** The release: the target re-check and the BEGIN, as the casting goal's (`Release`); a
   failed re-check frees the run unpaid in both settings (MG41, owner decision 2026-10-28). The missile is destroyed.
5. **A lost missile (`LoseMissile`, MG44).** A leg whose line is refused (more than 100 steps), a point off the map, another
   map or session ends the missile's goal without its release: the original destroys the missile and frees nothing, so the run
   keeps its slot, never begun; both rule modes free it unpaid (owner decision 2026-10-28: fixed without a switch).
6. **The viewer** (`TerrainViewerSpellMissiles.cs`). The hero's missile is drawn as the combat projectiles are (the faced art at its
   step's frame, moved by its pixel offset, additive when translucent, at the art id's draw scale); the cast's log line follows the
   arrival. The casting goal's facing turn turns the drawn hero in place until it moves. F7 saves a missile in flight (ARC-DEV-289,
   [saved runs](../architecture/saved-runs.md) §3.5: its flight, run and spell run are saved and it flies on after a load). Smoke: when its first damaging spell has no missile, the `cast` stage also casts the first critter
spell that has one (learning it if needed): the hero turns to the NPC, the missile is drawn faced toward it, flies one leg of the
line's steps and arrives, and the run's BEGIN pays its cost.

## The weapon's spell pane (ARC-UI-016)

The original's interface bar has a weapon button and a mode 8 that shows the wielded weapon's spells (ledger
[IP rows](../research/ledger/spell-runs.md#item-spell-pane-arc-ui-016)). The rules are Godot-free (`ItemSpellPaneRules`), the
item facts come from the cast host (`SessionSpellCaster.ItemPane`), the viewer draws them in its spell pane:

1. **The items.** Since ARC-UI-016 the viewer's cast host knows the hero's items (its item lookup: a kit item's prototype fields
   from the hero load, a taken item's from its object row, listed or worn; made items are not covered yet), so an item can be the
   source of a cast (`CastAt(..., item)`, ARC-MAGIC-022) and the item executors find it. A row of the shown map whose item the hero
   holds is no map object then (`AreaSpellCastMap`'s `held`): the item stands in the hero's inventory at the hero's location.
   Since ARC-UI-031 the lookup also knows the items the shown map's NPCs hold now, with the NPC as holder
   (`AreaCombatHost.NpcItemSpells`); a carried row (object flag 0x1000) is no map object, so it stands at its holder, and the host is
   built with its `ItemTriggers`, so the life steps of its own runs fire the worn items' spells
   ([item spell triggers](item-spell-triggers.md#not-ready)).
2. **The weapon button** (`WeaponButtonShown`, IP1). It shows in the pane's header while the wielded weapon (slot 1004) has charges
   (not 0, unlimited included) or the magical flag and a complexity above 0; technological weapons never show it. A press opens the
   weapon's spell pane in place of the spell list (IP2, the bar's mode 8), a second press returns to the list.
3. **The weapon's pane** (`PaneButtons`, `Gauge`, IP3, IP4). An identified weapon with a complexity above 0 shows one button per
   filled spell slot whose record is not technological, in slot order; a slot whose record has item triggers shows disabled (its
   press does nothing, IP5). The header shows the charges, "unlimited" or, for an unidentified weapon, nothing known (the pane then
   has no buttons). Identification is the item's stored flag: the session does not change it yet, and no installed weapon prototype
   is identified (census), so with the installed data the pane shows no slot buttons until identification is modelled; the cast
   from a slot is exercised by the smoke's lent, identified facts.
4. **The activation** (IP5, `CanActivate` for 0x57C040). A slot without item triggers is activated as a spell of the pane is (SU1–SU3)
   with the hero, the weapon's holder, as the caster and the weapon as the source: the targeting cursor keeps the weapon, and the
   cast at the picked target (`CastAt` with the item) skips the hero's cost check and pays from the weapon's charges (the
   session's saved count, ARC-MAGIC-022), not the hero's fatigue.
5. **The close** (IP6). The pane returns to the spell list as soon as its weapon is no longer the wielded one (the inventory
   callback 0x5570D0 does it on the item's removal).

The native cast smoke (`ARC_AREA_MAP_SMOKE_ONLY=cast`) checks it after its casts: no kit weapon carries castable spells, so it lends
the wielded kit weapon the spell facts of the first weapon prototype with a castable critter spell that needs no damaged target
(prototype 6134 in the installed data), marked identified; `M` shows the weapon button, the weapon's pane opens, the slot opens the
cursor with the weapon as the source, a click casts at the NPC, the weapon pays one charge (20 → 19) and the hero no fatigue; taking
the weapon off (through the trigger host, since the run still has it as its source) returns the pane to the list, and wielding it
again brings the button back. Marker `ARC-UI-016 native-smoke item-pane PASS`.

## The hotkey bar (ARC-UI-022)

The original's hotkey bar holds ten entries on the keys 1..0 and two recent actions, the latest on the key A (ledger
[HK rows](../research/ledger/spell-runs.md#hotkey-bar-arc-ui-022), IP7–IP9). The rules are Godot-free (`HotkeyRules`,
`Arcanum.Simulation.Hotkeys`), the entries are the session's (`GameSessionOwner` hotkey calls, saved since schema 53), the viewer
carries out the activations with the session and the cast host.

1. **Entries** (`HotkeyEntry`). Type 1 an item, 2 a skill-bar button (0 prowling, 1 pick pocket, 2 disarm traps, 3 repair, HK9), 3 a
   spell, 4 an item and one of its spells. `AssignHotkey` is a drop on the bar from outside it (HK5): it overwrites the entry, and an
   item entry's item leaves the grid for the inventory location 2000 + entry; ammunition, gold and a second item of a prototype an item
   entry already holds are refused. `MoveHotkey` is a drag between entries (the two trade places), `ClearHotkey` a drag off the bar
   (the item goes back to the grid). An entry empties when its item leaves the hero or moves: a wear, a take-off, a grid move or a move
   onto, along or off the bar runs the original's inventory callback, which empties every entry naming the item, whatever its type,
   except the item entry that now holds it (HK6).
2. **Recent actions** (`RecordHotkeyAction`, `HotkeyRules.RecordAction` for 0x57EFA0, HK2). A press of the spell pane (a spell) or the
   skill bar (a button) becomes the latest; the same action as the latest changes nothing, any other pushes the latest to the second
   place. Only spells and skill-bar buttons are recorded, as only the bars write them.
3. **Keys** (`KeyActivates`, HK8). The keys 1..0 activate their entries on the press, not while another critter has the turn of a
   turn-based round, and A the latest recent action (the character path has no turn test); an empty entry does nothing.
4. **The activation** (0x54FCF0, `PcCanActivate`, `PlanItem`, `PlanItemSpell`, `SpellActivates` for 0x57C080; HK1, HK3, HK4).
   Nothing for a dead or unconscious hero. A spell is activated as a press of the spell pane is (SU1–SU3). An item-and-spell entry
   activates the spell with the item as its source when the item is the wielded weapon; any other item is first wielded into the weapon
   slot (which empties the entry, HK6). A skill-bar button: prowling toggles the concealment (ST1); repair opens the inventory window,
   whose Repair buttons stand in for the item cursor (RP4). An item entry acts on another item of its prototype first (0x462930,
   `QueryHotkeySameKind`), except a written item that keeps its text: it is used when it can be used and is not thrown, else a weapon or
   armour is worn into its slot (0x550000; the entry's own item, the occupant goes back to the pack), ammunition and gold do nothing and
   any other item is held on the cursor.

The bar's art and its drag and drop are [ARC-UI-027](#the-hotkey-bars-art-and-drag-arc-ui-027); the viewer keeps stand-in keys:
`Ctrl`+`1`..`0` puts the latest recent action on that entry (the way to put a skill-bar button on the bar while the viewer draws no skill
bar; the original drags from the bars, not from the recent-action buttons, HK13), `Ctrl`+`Shift`+`1`..`0` empties it, a key `1`..`0` while the inventory window holds an item drops it on the entry, the
palette's Prowl action is the skill bar's prowling button, and the status line lists the entries. The cast smoke checks the latest recent
action after its pane presses, `Ctrl`+`1`, the keys `1` and `A` (each opens the spell's cursor), `Ctrl`+`Shift`+`1` and an empty key, and
the weapon's spell on entry 1 opening the weapon's cursor; marker `ARC-UI-022 native-smoke hotkeys PASS`.

## The hotkey bar's art and drag (ARC-UI-027)

The bar is drawn on the bottom strip of the [interface bars](../rendering/terrain-viewer.md#interface-bars-arc-ui-018) with the game's art
(ledger [HK10–HK16](../research/ledger/spell-runs.md#hotkey-bar-art-and-drag-arc-ui-027)). The rules are Godot-free (`HotkeyBarRules`,
`Arcanum.Presentation.Hud`), the view is `AreaHotkeyBarView` (a part of the bottom strip, scaled with it) and the host
`TerrainViewerHotkeyBar.cs`, which carries the drops out with the session's `AssignHotkey`, `MoveHotkey` and `ClearHotkey`.

1. **Slots** (HK10, HK11). The ten slots at the strip's top row, each with `HKSlt1.ART` (173, the art the original draws for every slot)
   and the entry's icon centred or clipped in it (`IconPlace`); a dragged entry's icon hides. The pointer's slot is `EntryAt` (32 columns
   per slot inside the band at y 440..476).
2. **Icons and counts** (HK12). A spell entry and an item-and-spell entry show the spell's icon (`SpellIcon`, spells 0..79), a skill-bar
   entry its button's art (`SkillIcon`). An item entry prints its count (`QueryHotkeyCount`, the items of its prototype the hero holds) at
   the slot's left + 2 and the strip's top while it lies in 0..9999; its icon is [ARC-UI-032's](#the-hotkey-bars-item-icons-arc-ui-032).
3. **Buttons** (HK13, HK14, HK16). A left click on a slot (press and release over its 32 × 32 button) activates the entry, or drops the item the inventory
   window holds on it (`ClickEntry`); the activation is [ARC-UI-022's](#the-hotkey-bar-arc-ui-022) without the keys' turn test. The two
   recent-action buttons at (69, 548) and (114, 548) show their action's icon (frame 0, frame 1 while held) and a click on one activates its
   action, with the bar buttons' sounds 3000 and 3001, whatever is dragged or held (HK13). The light button (182) at (616, 455) sounds and does nothing, as in the original.
4. **Drag** (HK15). A right press in a slot's hit band (from y 440) starts a drag (`StartsDrag`) from a slot that holds an entry, from a spell of the spell pane or from a
   slot of the weapon's pane (an item and its spell); the icon follows the pointer. The right release decides (`Drop`): on another slot a
   dragged entry trades places with it and anything else is put there (HK5's refusals apply); on its own slot nothing changes; off the bar a
   dragged entry is emptied and anything else ends with no change; ARC-UI-087: an item entry's item stays on the game cursor then (HK15, 0x573630),
   thrown from there by a left press in the game view ([terrain viewer](../rendering/terrain-viewer.md#inventory-throw-from-the-game-cursor-arc-ui-063)). A left release goes on with the drag. While an entry is dragged its key skips it and the stand-in keys leave it alone.

The viewer draws no skill bar, so a skill-bar button reaches the bar through `Ctrl`+`1`..`0` only. The cast smoke checks the bar after the
hotkey keys: the slot art, a right drag of the pane's spell onto slot 2 and its icon, its key 3 skipping it while it is dragged and a drop on its own slot, a click on slot 2 opening the spell's cursor, a drag
from slot 2 to slot 6 and one off the bar, an item entry's name and count, and the latest recent action's button (icon, click, sounds);
marker `ARC-UI-027 native-smoke hotkey-bar PASS`.

## The hotkey bar's item icons (ARC-UI-032)

An item entry shows its item's inventory art when the art fits a slot, else its type icon (ledger
[HK17–HK18](../research/ledger/spell-runs.md#hotkey-bar-item-icons-arc-ui-032)); the dragged entry's icon follows the pointer as well.

1. **Fields.** The item's stored fields are its object row's when a map of the run holds it (instance over prototype), else its kind's
   prototype's (`AreaItemIcons`, Core). Identified is the original's test (0x464370): complexity 0 or less, the stored item flag 0x1, or
   the session's identification mark (`QueryItemIdentified`).
2. **Inventory art** (HK18). The inventory art id (field 0x5D) names its file through `art\item\item_inven.mes` with the ground art's
   number rule (`AreaObjectArt.ResolveItemInventory`, 0x4EC290; the area owner's `Resolve` keeps refusing inventory art); its only frame is
   drawn with the id's palette when both sides are at most 32 (`ItemShowsInventoryArt`), centred in the slot.
3. **Type icon** (HK17). Otherwise `HotkeyBarRules.ItemTypeIcon` (0x554C20) gives the interface art: by type, the art subtype, the
   coverage, the ammunition type, the written subtype or the discipline, one art further for magick and two for technology on weapons and
   armour, and the hexed icons for an identified item with item flag 0x40. The bar reads every type icon with its other art
   (`TypeIconNumbers`). An inventory art that does not read also takes the type icon; only when neither reads does the slot show the item's
   name. Log line `ARC-UI-032 hotkey item-icon item=… InventoryArt|TypeIcon|NoIcon …`, once per item (again when it is identified).

## The hotkey bar's hover descriptions (ARC-UI-035)

The pointer entering a slot or a recent-action button shows its entry's description in the bottom strip's message window (ledger
[HK19–HK23](../research/ledger/spell-runs.md#hotkey-bar-hover-descriptions-arc-ui-035)); `AreaHoverDescriptions` (Core) composes it,
`TerrainViewerHotkeyHover.cs` wires it.

1. **Dispatch** (HK19). An entry being dragged or empty shows nothing; an item entry shows its item window, a skill-bar button entry its
   skill's hint, a spell or an item's spell entry the spell's hint. A recent action dispatches the same way.
2. **Hints** (HK20). A skill hint (message-log type 7) draws icon 444, the button's skill's name and description from `skill.mes`
   (`HotkeyBarRules.ButtonSkill`, 0x57A6A0); a spell hint (type 8) the college's icon (`HotkeyBarRules.CollegeIcon`), the name, the
   minimum willpower, right of it the fatigue cost the hero pays with the maintenance (intgame.mes 59, 73, 74, 75; the spells page's row),
   and the description. Both are hover entries: never stored, drawn only in the message mode and more than 3 s after the last stored
   entry; a right-aligned text uses the text draw's flag 2.
3. **Pane names** (HK21). While the spells (mode 1), skills (2) or weapon's spells (8) pane holds the window, a hover writes only the name
   (a skill-bar button's from `skill_ui.mes` 100 + button, a spell's, an item's) centred on a black fill of the mode's text area; leaving
   keeps it, as the original's leave acts in the message mode only; the pane's change takes it away.
4. **Item window** (HK22). The item's type icon (always the type icon, `HotkeyBarRules.ItemTypeIcon`) and its name as the original's
   0x441B60 gives it (`AreaItemIcons.Describe`: the description when identified, else the unknown description; a key its key's name) in
   the font's colour: orange for an identified hexed item, blue for another magickal one, white otherwise. It is drawn at once in the
   message mode (no time test) and never stored. Leaving the slot takes it away and, as for every hover, the window shows the viewed
   entry again (ML7's fix). ARC-DEV-326 (HK24): the item's examine script (point 0) runs first, with the hero as the triggerer
   (`AreaUseScriptHost.RunExamine`), and an answer of skip default leaves no description; the same step opens the world hover's description
   of every object, so a scenery or critter with an examine script is described only when it lets the default run. The script's printed
   lines go to the message window. ARC-SCRIPT-1119: an item the session made (`made:n`, no map row) runs its examine script too, from its prototype's point 0 (or the slot a run kept on the item) as a session item run (`GameSessionOwner.RunItemExamine`, `SessionItemScripts.Admission`, one revision when the run changed anything, a refused run applies nothing and the item is described as before); the world hover does not reach it (a made item on the ground has no row to point at). A run the viewer cannot apply (refused by the
   admission, a teleport it asks for) describes as before; a conversation it asks for is not started (a made item's run that asks for one is refused, so the item is described as before). ARC-UI-1120 (HK25, HK26): the barter window's still pointer on an item runs it first, with the hero as the triggerer, and then makes the quote
   (the window's handler is the inventory's, 0x573C74; the description is drawn only while the bars show, which the viewer hides under the window, so in practice the script runs and the item window is a known gap; the viewer runs it only with its interface on),
   and the dialog start runs the speaker's examine script and redraws its window as the world hover does (0x567460), once, with the interface on. The
   floating tooltip of the viewer without the interface bars (the original has none) does not run it, and the follower portraits' hover (0x56A9D0,
   HK26) is not built.
5. **Inventory** (HK23). In the inventory window, the pointer resting 35 ms on another item of the doll or the grid (nothing held) draws
   that item's window the same way; resting on no item keeps the last description.
6. **Smoke.** The hotkey-bar smoke enters slot 2 (a spell: its hint with the college's icon), a skill entry (its hint; in the spells mode
   only its name in the pane's text area), the item entry (its window) and the recent-action button (its spell's hint), and leaves each
   (marker `ARC-UI-035 native-smoke hotkey-hover PASS`); the inventory smoke rests the pointer on a grid item (marker
   `ARC-UI-035 native-smoke inventory-still PASS`).

## The maintain bar (ARC-MAGIC-026)

Magic plan slice MT20b (owner bug report 2026-10-29: the five top slots did not show or end maintained spells;
[FR-MAGIC-006](../requirements/combat-magic-tech.md#fr-magic-006)). The top strip's five slots SplSlt1–5 show the hero's maintained
spells and a click on one ends its spell (ledger [MB1–MB9](../research/ledger/spell-runs.md#maintain-bar-arc-magic-026), two readers, F2).
Code: [`MaintainBarRules`](../../src/Arcanum.Simulation/Magic/MaintainBarRules.cs) (Simulation), the session's bar
(`GameSessionOwner.QueryMaintainBar`, `QueryMaintainBarOpenSlots`, `MaintainBarTakes`, `StopMaintainedSpell` in
[`GameSessionSpellRuns`](../../src/Arcanum.Session/GameSessionSpellRuns.cs)), `SessionSpellCaster.StopMaintainedSpell`,
[`AreaMaintainBarLayout`](../../tools/terrain-viewer/Core/AreaMaintainBar.cs) (Core),
[`AreaMaintainBarView`](../../tools/terrain-viewer/Godot/AreaMaintainBarView.cs) and the host
[`TerrainViewerMaintainBar`](../../tools/terrain-viewer/Godot/TerrainViewerMaintainBar.cs). Tests:
[`MaintainBarRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/MaintainBarRulesTests.cs), the bar case of
[`SessionSpellRunTests`](../../tests/Arcanum.Session.Tests/SessionSpellRunTests.cs) and
[`AreaMaintainBarTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaMaintainBarTests.cs).

1. **Slots** (MB1, MB7). Five slots, each empty, showing its run, or stopping (its stop asked). The hero's effective intelligence / 4
   slots are open, at most five (MG12); the others are closed.
2. **Add** (MB2, MB3). At a maintained magic spell's maintenance tick the scheduler asks the bar for the run (0x4604A0): a run the bar
   shows in an open slot stays; otherwise the first open slot whose run is none takes it. The session answers against its bar after the
   notes the acting run rules left so far (a run ended earlier in the same call frees its slot first) and adds the run when the change
   is committed, with the table. A full bar refuses the run and the scheduler ends it (MG12: the original writes slot −1).
3. **Remove** (MB4). The run rules' "maintain bar" note (0x4604C0: a run's end, release or failed maintenance) frees the first slot
   holding the run. A slot whose run the table no longer holds is emptied too (a technical guard: a freed run's id is reused).
4. **Click** (MB5). The slots are push buttons: a press plays 3000, the release over the slot 3001; the release over a slot showing a run
   turns it to stopping and interrupts the run through the cast host (0x457110: MAINTAIN turns to END, its ticks go, it is processed);
   the run's end frees the slot. An empty or stopping slot does nothing. The stopping state is a session change even when the interrupt
   changes no run.
5. **Hover** (MB6). The pointer entering a slot that shows a run describes its spell as the hotkey bar's spell entry does (the spell's
   hint, or its name in a pane's text area).
6. **Look** (MB8). A slot holding a run (showing or stopping) draws the spell's icon (`HotkeyBarRules.SpellIcon`, idle frame, the down
   frame while held); an open empty slot its art 188 + i; a closed slot art 188 + i with SP_Plugged 628 + i one pixel up and left, and
   takes no pointer. The arts load with the bars' art, the icons with the hotkey bar's.
7. **Closing** (MB8). When the open count drops, each slot it closes while showing a run is clicked, so its spell ends.
8. **Saving** (MB9). The bar is saved with the settled runs it shows (ARC-DEV-210, schema 73): a loaded game shows the saved slots again.
9. **Smoke.** Inside the cast stage, before the hotkey bar: the hero casts the first maintained college spell (no damage at BEGIN) at
   itself or the NPC, released at once; slot 0 shows its icon, the closed slots their plugged art, the open ones their slot art;
   hovering slot 0 describes the spell; a click plays 3000 and 3001, ends the spell and empties the slot (marker
   `ARC-MAGIC-026 native-smoke maintain-bar PASS`).

## The spell pane in the bottom strip (ARC-UI-053)

While the interface bars show, the spell pane (`M` or the Spells button) is the original's bar mode 1 in the bottom strip's message
window (ledger [SPN1–SPN6](../research/ledger/spell-runs.md#spell-pane-arc-ui-053)); `SpellPaneRules` (Presentation) holds the layout and
which buttons show, `AreaSpellBarView` draws it and `TerrainViewerSpellBar.cs` wires it.

1. **Look** (SPN1, SPN2). The mode's background art 8 (`spell_ro.art`, 410 × 107) at (196, 492); the college buttons on the upper row at
   y 497, x 201 + 25 × college for colleges 0–7 and 400 + 25 × (college − 8) for 8–15, each with its own 24 × 24 art (`s_conveyance.art`
   … `s_temporal.art`, three frames); the selected college's spells on the lower row at y 528, x 284, 334, 384, 435 and 485 by rank, each
   with its 32 × 32 icon (the hotkey bar's spell icons). A hover's name is written over the background in the mode's text area
   (208, 574, 387 × 18). The buttons are the bar's push buttons: down while held over them, lit under the pointer; the selected college
   shows down. A button whose art does not read is not shown.
2. **What shows** (SPN3). A college's button shows when the hero has a rank in it; the selected college's spells show rank by rank while
   the hero knows them. The selection starts on college 0, which shows no spell when the hero has no rank in conveyance, until a college
   is pressed.
3. **Presses** (SPN4). A college is selected on its button's press and its spells replace the old college's; a spell is activated on the
   left release over its icon (the latest recent action first, then the spell's activation, as the diagnostic pane's press did); a right
   press on a spell starts the hotkey drag (ARC-UI-027). Each button plays 3000 on the press and 3001 on the release over it.
4. **Names** (SPN5). The pointer entering a college's button writes the college's name (`spell.mes` 500 + college), entering a spell's
   icon the spell's name, in the mode's text area; leaving keeps it.
5. **Window** (SPN6). The pane shows only while it holds the message window: the pointer above the strip gives the window back to the
   log and the pane hides, a move back over the strip shows it again (ML8); the pane stays open meanwhile, so `M` then closes it, as the
   original's toggle acts on the Spells button's state, not on the mode shown (0x54DBF0). Without the bars or the pane's art (automated runs without a
   bar smoke, the self-test) the diagnostic pane lists the spells as before. The weapon button and the weapon's pane (bar mode 8) stay in
   the diagnostic pane, which shows only while the weapon has something to show.
6. **Smoke.** Inside the hotkey-bar smoke (cast stage): `M` shows the pane in the strip with its background and the colleges the hero has a
   rank in; a click on the spell's college selects it (drawn down, 3000 and 3001) and shows its spells with their icons; entering the
   spell's icon names it; a click on it activates it and makes it the latest recent action; the pointer above the strip hides the pane
   (marker `ARC-UI-053 native-smoke spell-pane PASS`). The drag onto slot 2 starts from the strip's icon.

## Endings of a critter's runs (ARC-DEV-301)

A critter's life step can end the spell runs it parents or sources (triage T1 of the 2026-11-08 entangle report; [ledger UE1–UE5](../research/ledger/spell-runs.md#spell-run-endings-of-a-critter-arc-dev-301)).
`GameSessionOwner` cannot act on the runs by itself (the run rules need the host's world and the session refuses a run action while they act), so
the life step it commits tells the host through `CritterRunEnd`, which the newest `SessionSpellCaster` of the session sets: a critter that was
not unconscious and is now (the fatigue setter 0x45D5A0) is **demaintained** (`SessionSpellRunActionKind.Demaintain`, 0x457450) and a critter that died (the kill 0x45DA20)
is **gone** (`ObjectGone`, 0x459740). The host queues the endings with the Destroy component's and tells them when the run rules are idle: at the end of its own
action (`Act`, `FireItemEvents`, a burn) or of the frame's event pass (`ProcessEvents`): a death by the cast host's own damage is told at once, any other death (a melee blow) at the next pass, the next frame at the latest, and while the game is paused at the next pass of a frame that runs one. `EndMaintainedSpells` is the AI gate's
step (0x4A8570 → 0x457450) and `ObjectGone` the NPC destroy's (`AreaAiHost.Destroy`, 0x43CCA0). An ended run takes its eye candy off, so the vines of an entangle
leave the hero with its caster. Smoke stage `spell-end` (`docs/rendering/area-map-mode.md`). Differences: the ending comes after the outer action (the original ends the runs inside
the kill, so a run killing its own parent is left alone by the guard 0x5B0BA4 there and is ended here), and the summon variants (0x4598D0, the summon list) are MT17's. The loss of consciousness is the transition test of the life step (conscious before, unconscious after); the setter's other conditions (a new fatigue damage of 0, the object test 0x405BC0, a critter flagged 0x4 that the original never counts as unconscious where this test uses the Undead trait) are not modelled. The sink belongs to the newest cast host: endings queued in a host that a map change retired are not told, so a run can outlive its dead parent for the moment between the map change and the new host's build.

### The world-map travel start (ARC-MAGIC-059)

The world map's start check (0x565140, [ledger UE6](../research/ledger/spell-runs.md#spell-run-endings-of-a-critter-arc-dev-301)) runs when a journey is started and again where a segment ends and another follows
(`AreaWorldMap.TravelCheck`, set by the viewer to `WorldMapTravelCheck`). It interrupts the hero's running actions at level 3 through the hero runs host (`Interrupt`: a walk or a swing ends with its end nodes, an
attempted cast of priority 4 or 5 refuses) and, when none refused, calls `EndMaintainedSpells` for the hero: every run the hero is the source of ends (technology runs and item-trigger runs without a maintenance
period stay). A refusal at the start leaves the PC where it is and the map showing the route (`AreaWorldMapRefusal.ActionsRefused`); a refusal between segments drops every remaining waypoint and lands the PC
(`WorldJourney.Step`'s `continueJourney`). Smoke stage `worldmap` (it takes the hero since this slice) starts a maintained spell of the hero and checks it is gone after Enter. The rest option click (0x57B710)
calls the same ending without the interrupt: since ARC-MAGIC-1161 the sleep and wait window's press (`TerrainViewerApp.SleepPressed`, [sleep window](sleep-window-runtime.md)) calls `EndMaintainedSpells` for the hero after the leader-sleeping script event, so both callers of UE6 are wired. Smoke stage `sleep` starts a maintained spell of the hero, opens the window (the spell runs), presses a length and checks the run and its slot are gone.

## Differences from the original

Owner decisions 2026-10-20, 2026-10-21 and 2026-10-27: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A goal refused before its animation (action points, the pre-animation check, or a target destroyed or off) frees the run with its BEGIN charged: the caster pays for a cast that never started (MG39). | The run is freed unpaid. | `MagicAnomalyProfile.GoalRefusalChargesBegin` (owner decision 2026-10-27: fix) |
| The charge of a run freed before its BEGIN ignores the free flag, the technology flag and `ChargeBeginCost: 0` (MG40). | Charged only when its BEGIN would have paid. | `MagicAnomalyProfile.GoalEndChargesAnyRun` (owner decision 2026-10-27: fix) |
| A failed target re-check at the release frame leaves the run in its slot (MG41). | The run is freed unpaid. | No switch since ARC-WORLD-108: a slot leak, fixed in both settings (owner decision 2026-10-28; before, `ReleaseRefusalKeepsRun`) |
| The stage writes its payment's fatigue before the components run. | The host applies the call's payments after the call, so a component reading the caster's fatigue in the same pass sees it unpaid. | — |
| A spell's life changes come from the components inside the run's pass. | The same, through the session's life calls; their revisions come before the run's own. | — |
| A pick with no target shows spell.mes 606 when the area test's failure global holds "not damaged" — a global no pick clears, so an earlier test's reason can show (MG42). | 606 only for the current pick's own reason. | — (owner decision 2026-10-27: fix without a switch) |
| The pick's end clears `OF_CLICK_THROUGH` on the objects it saw through, so one whose data carried the flag loses it (MG45). | No flag is written: such an object keeps it, in both modes. | — (technical bug, latent; fixed without a switch by the owner rule of 2026-10-28) |
| Queued casts whose goals never play (the stack ended through the end step or was interrupted) keep their runs in their slots, unbegun and unpaid, until the game ends (MG43). | The runs are freed, unpaid. | — (owner decision 2026-10-28: fix in both modes, no switch) |
| A queued goal is the casting goal 26 with its own get-up, unconceal and facing nodes and subgoals (QC2). | The viewer's goals have no subgoals and no facing turn, so the stack index is the number of queued casts. | — (not ready: the facing turn) |
| An eye candy's sound is played with it; a multi-rotation eye candy turns to its object's art; `can_autoscale` sizes it to a critter's body; an eye candy sits at its object's offsets. | None of these (EC9). | — (not ready) |
| An eye candy with a light art keeps a live light on its object in a free overlay light slot while its run lasts; with all four slots in use the add refuses (IW2–IW4). | Built (ARC-MAGIC-1229): `AreaEyeCandyHost.Lights` answers the light from the shown runs, `Play` refuses a lit add without a free slot, the viewer stands the light as a carried light where its object stands (`AreaEyeCandyLights`); the light is not an object light of the map and not saved itself: the overlay run is saved (ARC-MAGIC-1280) and the loaded session's host takes it again (`AdoptRestored`). | both (not ready: the object's own offsets) |
| An eye-candy entry with no art but the translucent or tint flag is "played" (0x43EF10, SUM13): a callback-flagged add waits. | Such an entry is "not played" and calls back at once (no installed record is known to have one). | — (not ready) |
| A callback play flag makes the run's CALLBACK action wait for the eye candy's animation (EC11). | Since ARC-DEV-352 the same ([summon](#summon-for-objects-arc-dev-352)); an add the overlay host refuses after the cast host was told it plays (no free place, a duplicate, a full goal table) calls back on the next frame, not in the same pass. | — (one frame later) |
| A foreground or background part is scaled by the object's scale too (EC12). | Every object is taken at 100 %. | — (not ready) |
| An eye-candy ART with a frame rate of 0 divides by zero (EC8). | Treated as no eye candy: the release runs at once. | — (no installed ART has one) |
| A spell missile whose goal ends before it arrives (its next leg's line refused, the goal stopped) frees nothing: its run keeps its slot, never begun (MG44). | The run is freed unpaid. | — (owner decision 2026-10-28: fix in both modes, no switch) |
| A missile's point off the map stops the game with an error (0x42F390). | The missile is lost (MG44's path). | — |
| A missile's art with a frame rate of 0 divides by zero at its setup (0x42F2D0). | Its pace is 100 ms, as for an art that does not read. | — |
| A missile carries its entry's light (0x458C00) and is drawn at its object's scale. | No light; drawn at its art id's draw scale. | — (not ready) |
| A target object freed while the missile flies has its location read from a freed handle (not read). | A target gone from the map counts as arrived; the re-check then refuses it. | — |
| Goal 28's first leg builds its line only when the reused run slot's path flag is set; a slot whose last run did not end through the end step flies its stale line, and a never-used one does not advance (SM9; not observed in play). | The first leg always builds its line. | — (latent, no switch) |
| The missile plays its projectile entry's sound (0x458C00). | No sound. | — (not ready) |
| The bar's weapon button is refreshed only on the PC's inventory moves, so a weapon whose charges ran out keeps a stale button until the next move (IP1). | The viewer re-reads the button's condition each frame. | — (technical, fixed in both modes by the owner rule 2026-10-28) |
| An item-and-spell entry whose item is not the wielded weapon wields it, which empties the entry, then reads the emptied entry again: the cast is lost (HK4). | The item and spell read before the wield are cast; the entry still empties. | — (technical, fixed in both modes) |
| That wield takes the old weapon off first and leaves it off when the wield is refused (HK3). | The session's wear swaps in one step; a refusal changes nothing. | — (technical, fixed in both modes) |
| A drag between entries leaves the source an entry without its object and puts a target item back into the inventory (HK5). | The two entries trade places. | — (technical, fixed in both modes) |
| A load replays the two recent actions through 0x57EFA0 in saved order, so they trade places on every load; a new action's object goes to the second slot (HK2, HK7). | The bar comes back as saved; recent actions are only spells and skill-bar buttons. | — (technical, fixed in both modes) |
| The selected college is kept in the save with the bar mode (0x54A1C4, 0x54A252). The press handler does not set the pressed college down: only the pane's build sets the selected college's button down (0x5512DD), so the look follows a new selection when the pane is built again (SPN4). | The selection lasts the viewer's run and does not follow a load or a new game; the selected college is always the one drawn down. | — (not ready: the save; display only) |
| An entry dragged off the bar keeps its hidden flag: it stays in the bar's data, undrawn and skipped by its key, until a drop on it or a load brings it back (HK15). | The entry is emptied (an item entry's item goes on the game cursor, ARC-UI-087). | — (technical, a lost state; fixed in both modes) |
| An item entry dragged off the bar leaves its item on the cursor for the inventory or the map to place (HK15). | The item is lifted to the map's cursor (ARC-UI-087, [TT8](../research/ledger/combat.md)); a released drag over an open inventory window closes the window. | — |
| An item entry's count is set on its drop and on a load and recounted when an item leaves its place (HK6, HK12): an item picked up later shows only after such a change. | The count is read at every redraw, so a pickup shows at once. | — (display only) |
| The count of an item entry of the prototype 15085 is always 1 (0x462B40, HK12). | Its items are counted like any other's (as the same-kind finder, AM1). | — (not ready) |
| The type icon's tables are read without a bound, so an index past them reads the next table or what follows (HK17). | The same within the five tables and the message window's icons; past them no icon (the item's name shows). | — (latent: the installed items stay inside the tables) |
| An inventory art that does not read leaves the bar with what the art library gives for it (not read). | The type icon. | — |
| A broken item's inventory art carries the destroyed bit 10 (DU4), whose effect on the drawing was not read. | Drawn as stored; the bit is ignored. | — (not ready) |
| A full maintain bar, or one with no open slot, writes the run into slot −1 (a neighbouring variable) and answers that it took it; more than five open slots (intelligence 24 and above) index past the table (MB3, MG12). | A full bar refuses the run, so the scheduler ends it; at most five slots open. With intelligence 24 and above the cap allows a sixth maintained spell, which ends at its first tick. | — (out-of-bounds write, fixed in both modes by the owner rule of 2026-10-17) |
| The maintain bar is saved with the game (MB9). | Saved with its settled runs (ARC-DEV-210). | — |
| Hovering a maintain slot also marks the run's target (or first target, or parent) as the hovered object (0x43C270, MB6). | Only the spell's description. | — (not ready) |
| A freed run whose bar note never came keeps its slot, and the next run given its id shows there (no such path read). | A slot whose run the table no longer holds is emptied. | — (technical guard) |

Recorded for MT20a-2, which plays the goals (F1, one reader):
- **Friendly cast.** Goal 73 (the friendly cast) has no end and frees nothing on its refusals.
- **Queued cast.** A queued cast stores its casting animation in another slot than the one goal 83 plays (no college spell has a
  casting animation).
- **Overlay art.** A refused goal's overlay removal is called with art 0.


## Hold flags (ARC-DEV-302)

Read in the retail executable (F1, ARC-DEV-302): the ObjFlag component 0x454A10 and its reference count 0x455100, with the S13b read
([GT145–GT153](../architecture/goal-table.md#the-s13b-read-of-the-executable)).

- **Where the flags live.** The session keeps what the components write on a critter as a change over the value its data stores
  (`GameSessionOwner.WriteObjectFlag`, `QueryObjectFlags`): a set bit is on and a cleared bit is off whatever the map data or prototype says.
  Only the hero (`pc`) and mobile objects (`oid:`) carry them; a write that gives back the data's value keeps no change, so a hold that
  ended leaves nothing behind. A set needs the critter on the cast host's map; a clear does not (a run's END can come after its critter
  left the map). They are saved in the `spells` section (`objectFlags`, schema 83). The cast
  host reads every object and critter with them (its map overlay), so the run rules' forbidden flags, the areas' required and excluded
  flags and the cancels see what a spell wrote; the session's life facts carry stone and paralysis as traits (`LifeTraits.Stoned`,
  `Paralyzed`), so the activity test (0x45D790), the damage and the stun read them.
- **The reference count (0x455100).** Setting a bit the critter already has does nothing while an active run provides it; clearing keeps
  a bit more than one run provides. "Provides" is counted only for the spell flags: an active run whose object is the critter and whose
  record turns that spell flag on (its `SpellFlagsSet`, +0x114), the run being processed among them, counted in the table the run rules
  hold at that moment (an END inside an action sees the runs that action already ended). The critter flags count no run, so
  of two paralyses the first to end clears the bit (the original's own behaviour). The original also looks for the critter in a run's
  two object lists (+0x130, +0x134); the counts are not widened to them (ARC-MAGIC-1234 keeps the run's object list, the counts still read only its target).
- **Side effects.** Setting entangled (spell flag 0x1000000) or paralysed (critter flag 0x40) logs `HoldStarted`, and the viewer
  interrupts the critter's runs whose bottom goal is 3 or whose current goal is 61 at level 6 with their end nodes (0x44E4D0 with the
  author −1, `HoldInterrupt`): the hero's through the hero runs host, an NPC's through the AI host. Stone (spell flag 0x10000) interrupts
  nothing. No goal starts and no turn ends.
- **The hold's effect.** The step start refuses a paralysed or stoned mover (GT148): an NPC's walk node waits in place with delay 0
  (`AiStepBlock.Held`), the hero's step node asks again in a frame, and the run resumes toward its old destination when the hold ends. The
  path request set-up refuses an entangled PC or NPC (GT149), so a new walk of an entangled critter fails at its plan. An entangled hero
  already running keeps running in **Original** and has its steps held in **Steamwoven** (owner decision D8 of 2026-11-07,
  `NavigationRuleProfile.EntangledPcKeepsMoving`); a Steamwoven far route (the follower walks the goal itself) gives no step while the hero
  is held either. A paralysed or stoned hero cannot act, so its clicks are dropped (GT150), but it is not down: its walk run stays (the
  walk ports' `HeldOnly`; the route stops the viewer makes for a hero that went down after a hit or a spell skip a hero only held), while its
  attack is cancelled and its attack and talk goals end as for a hero that is down. The route follower asks for no step while the hero is
  held (`RouteFollower.Held`), so a far route waits and goes on too.

| Original | Steamwoven | Switch |
| --- | --- | --- |
| An entangled PC already running runs on to the end of its planned path (GT153). | Its steps are held as a paralysed PC's. | `EntangledPcKeepsMoving` (D8) |
| Of two paralysing runs, the first to end clears the bit (the critter flags are not counted). | The same. | — (kept) |
| The step start also refuses a stunned mover (0xDA & 0x20). | Only the holds are tested here; a stun is its own goal run (57) that the damage path starts. | — (not ready) |

**Not ready.** (The Effect component, and the entangle's re-resistance on a hit, are [below](#effect-component-and-the-entangle-re-resistance-arc-dev-303).) The other bits and fields of ObjFlag (invisible, shrunk, water walking, charm, mind control, fleeing, blinded, "don't
draw", unlocking and the rest: MT13, MT14), stone's render flag (object flag 0x80000), the runs' object lists in the count (kept by the run since ARC-MAGIC-1234, still not read by the counts), the hold of a
companion the companion group walks, and an NPC's or the hero's hold ending when the caster dies (MT11, ARC-DEV-301). A save keeps every
written flag, but not a run waiting for a callback or its BEGIN (saved runs): a hold such a run wrote outlives it after a load, as its END
does not come.

## Effect component and the entangle re-resistance (ARC-DEV-303)

Read in the retail executable (F1, ARC-DEV-303, slice T2b of the [bug triage](../research/open-bugs.md)): the Effect component 0x451F20, the hit effect 0x459A30 and
the re-resistance 0x459C10, [ledger ER1–ER9](../research/ledger/magic.md#effect-component-and-the-entangle-re-resistance-arc-dev-303) (RS10 raised from F1).

- **What "Effect 62" is.** The number of an effect rule of `effect.mes`, not a component type: rule 62 is the speed stat −5. Entangle (spell 51) adds it at its BEGIN
  and takes it off at its END beside its entangled flag; Body of Water (28) and Plague of Insects (70) add it too, Body of Stone (18) rule 61 (also speed −5). No other hold spell
  has an Effect component.
- **The component (`SessionSpellCaster.EffectComponent`).** `Count:` entries plus, for `Scaled:` above 0 and a PC or NPC parent, `Scaled × the parent's aptitude captured with the run / 100`
  (`SpellRunObject.Aptitude`, 100 for a fired "spell at maximum"); an add is the hero's effect add (`AddHeroEffect`, the record's cause, the spell cause when it names none), a removal the
  typed removal (`RemoveHeroEffect`: the first entry of the number, whoever added it, EF2); a count of 0 or less and a target that is no critter do nothing. The effect rules come from the viewer
  (`EffectDefinitions`: the hero's effect tables, effect.mes and the module's gameeffect.mes); without them an add is not ready. The session's refusals (an innate number, a full list) are
  logged (`EffectNotSaved`) and the spell goes on. The hero's stats read the entry at once (the speed stat of the sheet); an END removes it with the flag.
- **The hit effect (`SessionSpellCaster.CritterHit`, 0x459A30).** A critter whose spell flags hold entangled looks for the first active run whose target it is and whose record sets that
  flag (the run finder 0x459170, slot order) and asks that run's save again (`SpellResistanceRules.ReResist`, 0x459C10): a d100 against the stored magic resistance (a margin that only
  matters for a record that is not maintained) and the record's `Resist:` save, `S = stat − penalty + modifier`, **a d20 at most S resists**; entangle's is strength − 1, so a strong critter shakes it off
  on a hit. The caster of the run, a PC's armed fate "save against magick" (it skips the d100), a technology record, an unresistable run and a save of nothing hold as in the first
  resistance. A critter that resists again plays "resisted" (the log's `Resisted`, the local player's spell.mes 643 when the run's parent is the local player, `EntangleBroken`) and its
  run ends by event (0x4571E0: the interrupt waits 1 ms, so the hold lasts until the next event pass, then the END clears the flag and removes the speed effect; inside the run rules the ending
  waits for the outer action as the other endings do). A critter that holds logs `EntangleHeld`.
- **Where the hit comes from.** The weapon hits of the hero (`FinishCritterHit` of the viewer) and of the NPCs (`AreaAiHost.HitEffect`, the three attack paths: melee and shot, landed shot, thrown
  item), a hit that reached its victim (`AreaAiHost.HitReached`: not refused, no shot still flying, not vetoed, not prevented, the outcome a hit or a critical hit; misses never ask). The original
  reaches it from every hit on a PC or NPC (the attack end 0x4B6930: ER5).
- **Other damage (ARC-MAGIC-1143, ledger [RH1–RH5](../research/ledger/magic.md#re-resistance-on-damage-that-is-not-a-weapon-hit-arc-magic-1143)).** A spell's Damage component and a native trap's damage reach the hit
  effect too. The spell's (0x4518D0): after the damage step, when the victim is not destroyed or off and the step dealt damage (the packet's flag 0x200000: hit-point or fatigue damage above 0, poison alone no) the damage
  effect (0x433020) runs and ends in the hit effect (`SessionSpellCaster.SpellDamageHit`, called by `ApplyDamage` for a victim whose life step changed, after the step's AI pass); the damage effect itself returns first for a
  critter whose critter flags 2 hold 0x100 (`NoDamageEffectCritterFlag2`). The trap's (0x4BCDC0, damage scripts 30001 and 30003..30006, context flag 0x80000): the damage step runs the attack end after its resistance step
  and before its dead test, so the hit effect runs for a victim the taking-damage script and the invulnerable gate let through, through the damage effect when a hit-point slot that is left after resistance is above 0, directly otherwise
  (`TrapDamageHit`, called by `ApplyTrapDamage` before the life step). **A burn asks nothing**: it adds no context flag and no hit effect follows (RH5), pinned by a test. Not modelled: acid that only damaged worn items (the original asks the target's save once, RH5); not read: the weapon hit effect's veto (0x4377C0)
  before the trap's hit effect (a trap is no weapon), the trap shot (30002, `ShotNotReady`), the poison tick. Tests: [`SessionSpellDamageReResistTests`](../../tests/Arcanum.Session.Tests/SessionSpellDamageReResistTests.cs).
- **MG48 (Original bug, owner's bug rule 2026-10-28).** The original's save part reads the target's type not from the struck critter but from the global the run processing last cached (the type of the last
  target object any run action met, −1 after an action that met none: 0x451070 resets it at every action). Entangle's own maintenance tick has no target (its area is none), so after its first tick
  the cached type is −1 and the save part is skipped: **the entangle holds on every hit until a later action meets or re-aims to a PC or NPC** (the type is also written by a component's re-aim,
  0x453D40: the parent for the self bit, the source for the source bit, and by the source swap after the targets, 0x45148C; `ISpellRunHost.SourceSwappedBack`; the re-aim to the last summoned object is not modelled). Steamwoven asks the critter's own type (always a PC or NPC here), so the save is asked on every
  hit. The Original setting keeps the stale read (`MagicAnomalyProfile.ReResistReadsCachedTargetType`, the host remembers the type per run action and target: `ISpellRunHost.ActionStarted`,
  `TargetStarted`; not saved, a rebuilt host starts with −1 as a fresh process has no cached type).

| Original | Steamwoven | Switch |
| --- | --- | --- |
| The re-resistance's save part reads the type the run processing last cached, so after an action that met no target object it is skipped and the entangle holds on every hit. | The struck critter's own type: the save is asked on every hit. | `ReResistReadsCachedTargetType` (MG48) |
| The resistance term of the save is subtracted (MG4). | Added. | `SaveSubtractsResistance` (MG4, shared) |
| The aptitude scaling of the resistance reads the "fate-maximised" flag of the last processed action. | The scaling applies (the flag is not kept). | — (not ready) |

**Not ready.** The spell host's own reads of an NPC under its effects (the Effect component takes an NPC target since ARC-MAGIC-027, [consumers](durable-effects-runtime.md#consumers-of-resistance-and-stat-changes-arc-magic-027), but `Combatant` and the stat getter here still read the NPC without its list: spell damage ignores its resistance effects, a save its stat effects); the poison tick's hit effect (0x4B1170 calls the damage effect after its damage step, [RH2](../research/ledger/magic.md#re-resistance-on-damage-that-is-not-a-weapon-hit-arc-magic-1143); the session runs the tick as a life timer with no cast host), a hit by a weapon with its own hit effect (0x4377C0: the original runs nothing after it), any hit path that reaches neither `FinishCritterHit` nor the AI host's three paths; the hit effect's eye candy for the spell flags 0x400 and 0x400000; the run finder's two object lists (the run's own list is stored since ARC-MAGIC-1234, the finder does not read it); the AI notice of the willpower
branch (stale globals; no installed record reaches it); a hit on an entangled companion that the companion group walks.

## Spoken with dead (ARC-DEV-304)

Read in the retail executable (F1, ARC-DEV-304; [GT216–GT219](../architecture/goal-table.md#the-b24-read-of-the-executable-arc-dev-304)):
the same ObjFlag component 0x454A10 and its reference count 0x455100.

- **The flag.** Spell 56 "spirit of the dead" turns the spell flag `OSF_SPOKEN_WITH_DEAD` (0x2000000) on at its Begin and off at its End and
  does nothing else (spells 136 and 216 carry the same component). The flag lives where the hold flags live (the session's change over the
  data's value, the `spells.objectFlags` section, schema 83: no new step) and follows the same reference count, so it stays while any run
  provides it. The spell's own disallowed-target mask is the flag, so a second cast on the same body is refused.
- **What reads it.** `SessionSpellCaster.SpokenWithDead(id)`. A dead NPC that carries it is a speaker again: the click rules take
  `HeroClickFacts.TargetTalksDead` and send the hero to the talk (goal 21) instead of the loot (GT115); the can-speak head
  (`CanSpeakRules.Head`, 0x4AD800) answers 0 for it where it answers 1 for a body without the flag; the viewer's `SpeakerPresent` counts it, and
  the hostile check is skipped for the dead (0x412FD0). The dialog point of a dead NPC runs its own script only when the script file has the death-speech flag 0x8 (SU15): the conversation host's `DialogState` now answers `Dead` for a dead NPC of its map (as the use host's does), so the dispatcher stops a dead NPC without the flag at run default (`ScrStop.DeadWithoutSpeech`) and the start answers `GenericGreeting`: the generic greeting (ARC-DEV-324, GT220–GT222), a floating line over the corpse and no window. The line is the greeting builder's dead line, an NPC line 1500..1599 (the NPC's own override record 12014 first, else the NPC generated file; no `g:` line or dialog file needed), through `DialogGenericGreeting.Say` over the NPC's `AreaGreeting`; the host queues it on `AreaScriptHost.TakeFloats` and the viewer floats it as it floats a script's float line. The flag taken off, the body is a corpse to
  loot again and the talk click does nothing. The cast log says "can be spoken with" and "can no longer be spoken with". The hover cursor over a flagged body is the talk cursor (`AreaHover.Body`) and the pick-pocket loot click's status names the goal the click resolved to (`AreaPickPocket.LootStatus`), so cursor, click and status agree. A flagged body that is no dialog NPC still ends "no longer a speaker" at the talk goal's last node.
- **Smoke.** Stage `spirit-talk` (SM26): a script's free cast of spell 56 at a killed dialog NPC with no caster (the hero's own cast
  hits the willpower check, RN5, which does not apply to a run with no parent), the loot click before, the talk click during (the
  dialog start is `Started` for an NPC whose script has the death-speech flag 0x8, `GenericGreeting` with a floated line and no window for one without; the recorded Crash Site run printed `DeathSpeech:false`, `DialogStart:GenericGreeting`) and the loot click after the END.

**Not ready.** The conversation host's dialog gates (`DialogState`) model only dead (surrender and mind control stay normal) and see as dead only NPCs of the shown map; the generic greeting (run default, ARC-DEV-324) is the builder's text as a floating line only (no log record, no speech sound, no duration or colour of the original's display 0x568540, GT223; the viewer still leaves combat mode and refuses a talk under attack before it knows no window opens, which the original's dialog-start callback does and this path does not reach), and a flagged body that is no dialog NPC (a plain critter without NPC fields) has no greeting facts and says nothing; the reader at 0x4C31A0 (GT217); an open conversation is not closed by the END in
the part read.

## Summon for objects (ARC-DEV-352)

Slice E1-b of the [missing-systems plan](../project/missing-systems-plan.md#e1-summon-and-objects-created-at-run-time-priority), on the created scenery of
[E1-a](../architecture/object-directory.md#e1-a-created-scenery-and-eye-candy-objects-arc-dev-347) and the reads of [ARC-RES-101](../research/ledger/spell-runs.md)
(ledger [SUM1–SUM16](../research/ledger/spell-runs.md#summon-component-and-the-objects-it-creates-arc-res-101)); code `SessionSpellCasterSummon.cs`, viewer
`TerrainViewerSummon.cs`, [tests](../../tests/Arcanum.Session.Tests/SessionSpellSummonTests.cs), smoke stages `summon-flash` and `summon-wall`.

- **The component (0x452830, SUM1–SUM4).** On each list entry that passes its area test and is a tile (an object entry has no tile, TA9): the object of prototype file `Proto:` + 20 (SC7) is
  created on the shown map at the tile, no offset, with spell flag `OSF_SUMMONED` (0x4000), the run's parent as its summoner and the run as its owner. Only a prototype of type 3 (scenery) is made; a
  critter prototype and a list pick are E1-e's (below). The cast host reads type, object flags and scenery flags of the prototype from the viewer (`SummonPrototypes`); a creation the
  session refuses is logged (`SummonRefused`; the original exits, an unreachable bug, SUM2).
- **Empty tiles (SUM9).** The area tests are the existing ones over a map that now lists the created objects too: `Tgt_Tile_Empty` (Fireflash) refuses a tile with blocking terrain (the step evidence's terrain verdict since ARC-MAGIC-055; before, only an unadmitted sector refused) or any object but an
  item without `OF_NO_BLOCK`, a standing critter included, so a flash cast at a creature that still stands on its tile makes no flash; `Tgt_Tile_Empty_Immobiles` (Wall of Fire) counts only walls,
  portals, scenery and traps.
- **The summoned slot and list (SUM1, SUM7, TA10).** The last summoned object is reset once per action and is what a `Tgt_Summoned` re-aim names (the flash's eye candy); the run's summoned list is the
  created objects naming the run, newest first; a freed run's slot lets go of them (they stay, with no run). The created objects are map objects of the cast host (type, flags and hit points from the
  prototype, with 1 hit point: the viewer does not read the prototype's); they take no damage.
- **Destroy (DS2, SUM8).** A Destroy on a created object removes it from the session (one revision) and the runs hear of it after the outer action (0x459740); the viewer ends its loop and its overlay runs.
- **Eye-candy callbacks (EC11, EC14).** An EyeCandy Add with `callback` (0x8) or `end_callback` (0x10) asks the overlay host whether the entry plays (art, animating, a foreground or underlay part,
  readable frames): played, the run waits and the goal's callback (47/48 at the action frame, 58/59 at the end) calls the run's CALLBACK or END_CALLBACK action; not played, the callback runs at once
  (the END callback for `end_callback` alone). Fireflash's flash goes at the action frame of entry 2 (about 750 ms on the installed art, seen in the smoke); Wall of Fire's END line names an entry
  with no art, so its END_CALLBACK Destroy runs at once and takes all five pieces in one action.
- **The wall's burn (GT77, GT94, SUM4).** A created scenery whose start rule admits a loop gets one on the viewer's scenery host (goal 1, or goal 77 for `OSCF_IS_FIRE`) the first time it is shown, and is
  drawn at the loop's frame. The burn of a summoned piece has the summoner as its source: the source and the NPCs led by a PC source are spared, the source's stat 16 × 5 / 100 − 1 is added before a
  round's doubling (order F1-low), and the victim's AI hears the attack (0x4A9650); a spared victim does not spend the round gate. The source is the viewer's, not saved: the first loop of the object in a
  session has it (after a load too, as the original saves the run with its slot), a loop started again on a new host (a window shown again, the sector reload) or after a sweep has none and burns
  everyone, as the original's restarted run.
- **Lost callbacks.** The overlay runs are saved since ARC-MAGIC-1280 (a quick load brings them back with their tag; an eye candy whose spell run is gone is written with no tag) and are given up with their map or window. A run that can only be waiting for a callback (an END with END_CALLBACK components, or a processed BEGIN
  with CALLBACK components and neither maintenance nor duration) that no callback goal will call is called back by the viewer after two frames, so a flash never outlives its run; a freed run's overlay
  runs call nobody back (the run that takes the slot next is not theirs).

**Not ready.** Critter summons (E1-e), a Dispel on a summoned scenery object (SUM8: it ends the run that made it and so that run's END, which is done by [Dispel](#dispel-arc-magic-034)), the summon branch of the object-gone endings (DS4: an active run whose summoned list holds a destroyed object ends by event;
today only the run that destroys its own objects does so, and it is the one being processed), the network paths, the burn's experience, blame and witnesses, the blocking pieces of Wall of Stone and Wall of
Force in the path and passability queries (they are created; nothing blocks on them yet), a lost callback of a timed or maintained run (it is not called back; its CALLBACK line does not run). A creation or
destroy is a session change inside the run pass: a pass the session then refuses (a full event budget) leaves the object made or gone with the run unchanged.


## Summon for creatures (ARC-MAGIC-053)

Slice E1-e of the [missing-systems plan](../project/missing-systems-plan.md#e1-summon-and-objects-created-at-run-time-priority), on the created critters of E1-c and the reads of
[ARC-RES-101](../research/ledger/spell-runs.md) (ledger [SUM5–SUM8 and SUM17–SUM22](../research/ledger/spell-runs.md#summon-component-and-the-objects-it-creates-arc-res-101)); code
`SessionSpellCasterSummon.cs`, `GameSessionOwner.CreateSummonedCritter`, `SummonLists`, `SpellRunSystem.ObjectGone`, viewer `TerrainViewerSummon.cs`, `TerrainViewerCreatedCritters.cs`,
[tests](../../tests/Arcanum.Session.Tests/SessionSpellSummonCritterTests.cs), smoke stage `summon-undead`.

- **Which prototype.** The component's own `Proto:` (the basic number, + 20 for the file), else the pick of its `List:` (0..5, `SummonLists`): a weighted list draws once over 0..total−1 (the original draws 0..100, so
  an overrun of the table falls to entry 0: MG9, fixed in both modes), an aptitude list takes the first entry whose threshold is not below the run's captured parent aptitude (`SpellRunObject.Aptitude`, 100 under the
  armed "spell at maximum" fate). Each list entry the area passed draws on its own (SUM1, SUM18). A prototype that is not type 3 or 16, a list outside the table, or a component with `Clear_Faction:` or `Palette:`
  on a critter (no installed record has either) stays `ComponentNotReady`.
- **What is made (SUM5, SUM20).** `CreateSummonedCritter` creates the critter at the entry's tile (an `oid:critter:n`, no tile search) in one revision with the spell flag 0x4000, `OF_DONTDRAW` and
  `OCF2_NO_PICKPOCKET` written and the summoner and run on its record. The viewer's row builder clears its experience worth and pool and copies the hero's alignment into stat 19 when the hero summoned it (the alignment
  is the hero's of the moment the world first reads the row; a loaded game reads it again). The host is told at once (`CritterSummoned`): the viewer hands the critter to the resolver and the AI host, queues its
  inventory (the restock, 0x442260; it runs before the summon effect makes the creature the summoner's follower, so the viewer generates it with the PC-leader gate not read, `GeneratesAtCreation(..., beforeLeader: true)`) and its **creation notice** (`QueueCreationNotice`, TG9, ARC-DEV-402): the script of the tile it stands on runs with it as the triggerer.
- **The records' lines do the rest.** `Tgt_Summoned` finds the creature through the run's summoned list: the ObjFlag line `OSF_MIND_CONTROLLED` (the forced join of the hero's party, no flee, MA-4), the EyeCandy entry with the
  callback, the CALLBACK line that clears `OF_DONTDRAW` (the viewer draws the creature when the flag is gone; the AI gate treats a hidden critter as not acting), the END callback and its Destroy.
- **The run and the creature.** The death of the summoned creature tells the runs it is gone (0x459740); the first loop (SUM21) ends the run whose summoned list holds it, and the END_CALLBACK Destroy takes the
  corpse away. A destroyed critter that is a follower leaves the party. A Dispel on the creature is MA-3's (it destroys a summoned critter).
- **Not built.** The concealed start of SUM19, `Clear_Faction:` and `Palette:`, the faction copy and the alignment of an NPC summoner (E1-f), the familiar's own flags and its hiding with the party on the world map.

## The ObjFlag component and the component table (ARC-DEV-358, MA-1)

- **The dispatch is a table.** `SessionSpellCaster.Apply` looks a component's type up in `ComponentHandlers` (`SessionSpellCasterComponents.cs`, one line per type: Damage, Heal, Destroy, EyeCandy, Summon, AGoal,
  AGoalTerminate, ObjFlag, Effect) and runs its handler, which answers its outcome or null for a component it leaves undone (logged as `ComponentNotReady`). A slice that adds a component adds its line and its own
  partial file; the order of the old if-chain is gone (no type had two handlers).
- **Every flag bit.** The ObjFlag component takes every bit of the spell flags (field 20), the object flags (field 19, new: `SessionObjectFlagField.Object`, saved as `objectSet` and `objectCleared`
  in `spells.objectFlags`, schema 88) and the critter flags (field 218) on the hero and a mobile critter, with the reference count (0x455100: a set stops while another run provides the bit, a clear while
  more than one does; spell flags only) and the original's side effects (ledger [OB1–OB5](../research/ledger/spell-runs.md#objflag-component-of-the-cast-host-arc-dev-358)): invisible writes the object flags invisible
  (not for the local player) and translucent; shrunk, water walking and stoned write their object flag; don't-draw writes the object flag and the paralysed critter flag of a critter (Disintegrate's vanishing body);
  entangled and paralysed ask for the hold interrupt as before; the other bits (shielded, anti-magic shell, bonds of magick, full reflection, the four detection bits, the body flags, hardened hands, enshrouded, Tempus Fugit (read by the stat getter: [ARC-MAGIC-056](character-stats-runtime.md#tempus-fugit-speed-arc-magic-056)),
  illusion, familiar, spoken with the dead and the rest) are plain writes. The cast host's map (`FlagCastMap`) answers the object flags with the written ones, so the run rules, the target rules (a don't-draw object is no target)
  and the invisible-awareness readers see them; the AI host's hidden test, action gate and hostility facts read them too.
- **Body spells feed the unarmed damage.** `GameSessionOwner.UnarmedBodyOf` picks Body of Earth, Fire, Water, else Hardened Hands from the spell flags a critter holds in the original's order (OB5);
  the hero's attack values and every NPC handed out by `WithNpcEffects` carry it, so a written flag reaches the resolver's unarmed bonus. The host's own `Combatant` and stat getter for an NPC now go through
  `WithNpcEffects` as well (the MA-2 left-over: spell damage on an NPC reads its Shield of Protection or Body spell, and so does a burn).
- **Not ready.** The flags with a function of their own (mind controlled, charmed, undead, fleeing, blinded) were not ready until ARC-MAGIC-035 ([below](#social-and-ai-flags-arc-magic-035)); the portal and container lock fields (MA-6), the item, NPC and trap flag fields (MA-7, E1-c), the water-walking clear's drowning
  damage and the don't-draw clear's second function (OB2, OB3: F0, MA-8), and non-critter targets. The viewer's draw of a translucent, shrunk or hidden critter is built since ARC-MAGIC-045 ([below](#hit-eye-candy-and-object-draw-flags-arc-magic-045)).

## Identify (ARC-MAGIC-032)

Slice MA-7 of the [magic audit](../research/magic-audit-res-103.md) (owner decision D5(a), 2026-11-11: magic builds the state and a text answer, the window is the identify-window slice E8's), on the
executable reads [XR1–XR5](../research/ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357); code `SessionSpellCasterIdentify.cs`, `GameSessionIdentify.cs`,
[tests](../../tests/Arcanum.Session.Tests/SessionSpellCasterIdentifyTests.cs).

- **The component (0x4522A0, XR1).** It has no data and **writes nothing**; it shows something to the casting local player. It does nothing without a target, and nothing when the run's parent is not the hero
  (the wrappers 0x4EE4C0, 0x4EE430, 0x4EE3A0 test the local player; a single-player game does no more), so an NPC caster's Identify has no effect and is no "not ready" line. For the hero the host logs one
  answer by the target's object type: `IdentifyCritter` (a PC or NPC: the original opens the read-only NPC sheet, XR2), `IdentifyContainer` (the container's inventory in window mode 4 with no lock, jam or
  magically-held test, XR3), `IdentifyItem` (any other type: the item description, the true one when the item is identified, XR4). The viewer shows each as a line in the message log
  (`AreaSpellCasting.Line`); the windows, and what a press in mode 4 may move (not read), are E8's, which reads the same events.
- **Divine Magick's identified flag (0x454A10, XR4, XR5).** Spell 9 (area `Tgt_Obj_Inven`) sets `OIF_IDENTIFIED` (`obj_f_item_flags` 0x57, bit 0x1) on its item before its Identify. In the original it is
  a plain write: no side effect for item flags, no reference count, and nothing in the run reads it back. The session keeps it as the **identification mark** that a barter window already sets
  (`GameSessionOwner.MarkItemIdentified`, `QueryItemIdentified`, saved in the inventory section: no schema step), so every reader of that mark (the name, the item window's lines, the worth in a barter, the
  spell pane, the AI's item rule) sees the item identified. The mark is refused, with a log line (`ItemIdentifyNotSaved`), for an item the session does not track. The component logs `ItemIdentified` for a
  new or an existing mark. A clear of the flag, another item-flag bit, another field and a target that is no item stay "not ready" (no installed record has them).
- **Census (MA-0).** Records 6 and 7 leave the not-ready list on both census targets; record 9 keeps its `ObjFlag` on the census's critter targets (its target is an item).

**Not ready.** The three windows (E8); the multiplayer packets (kinds 7, 8, 10); an inventory item of a loot, steal or barter window as a target ([below](#an-inventory-item-as-a-spell-target-arc-magic-1128)).

### An inventory item as a spell target (ARC-MAGIC-1128)

Issue #1128, on the executable reads SU3 and IA4 and the new [IW44](../research/ledger/ui-audio.md) (the inventory window's press in the cursor modes). Code: `TerrainViewerSpellCast.cs` (`OpenInventoryForSpell`, `OpenSpellTargeting`, `InventoryItemPicked`, `SpellTargetApply`), `TerrainViewerItemAiming.cs` (`ItemAimInventoryPick`, `ClearItemTarget`), `AreaInventoryGridArea.TargetPicking` and its press, `TerrainViewerInterfaceModes.cs` (`TargetOverInventory`); tests `SessionSpellCasterIdentifyTests` (the pick) and the cast smoke stage (`TerrainViewerInventoryTargetSmoke.cs`, marker `ARC-MAGIC-1128 native-smoke inventory-target`).

- **The activation (0x57BC70, SU3).** A BEGIN area with the inventory bit (0x2000000; spell 9 is `Tgt_Obj_Inven`, 0x2000004) opens the caster's own inventory before the cursor's tests: a window already open on the stack is kept when it is the player's own (mode 8), the original closes a barter or steal window first and leaves a loot window open; the viewer closes a loot or steal window and opens the own one (the viewer picks items of the own window only), and an open window of another kind refuses with a notice. The cast's action points are asked after that (a refusal leaves the inventory open and no cursor, as in the original), then the cursor is pushed **over** the inventory (`OpenTarget(overInventory: true)`: stack Game>Inventory>SpellTarget). The per-frame window guard does not drop a target over the inventory (`TargetOverInventory`). Shift with the object bit does not cast at the hero at once for such a spell (a project choice: the quick cast skips the pick's area test, which refuses the hero as it is no item in an inventory, so the cursor opens as without Shift).
- **The pick (IW44, 0x573850 → 0x575FA0, then 0x57C110).** A left press on an item of the window with mode 1 on top names that item as the object target (the item under the pointer in any visible list: grid and doll) and lifts nothing; a press on no item does nothing. The pick runs the same area test as a click in the game view (`SessionSpellCaster.PickTarget` with the item as the point's object: the item is an object in its holder's inventory, object flag 0x1000, at its holder's location, `Tgt_Obj_Inven` needs the flag), and a target that passes is cast at (`CastSpell`, the casting goal as for any target). A game-view press with an inventory window open picks nothing (SU9: the cursor picks in the game view only with no inventory window open).
- **After the cast and the cancel.** The cursor stays open after a cast, as for every spell (SU9), with the window under it. A right release in the window cancels the cursor (project choice: the executable's right-release branch in the cursor modes was not read), and the cancel takes the window with it (the leave case of mode 1 sets the repeat flag, SA38); the window's own close pops the cursor.
- **The item path (IA4, IA6).** An item whose slot-0 spell has the inventory bit and which is used from the window (`OpenItemAiming`) pushes mode 16 over the window the same way (the window stays; a press names the item, `ItemAimInventoryPick` runs the slot-0 spell's area test and applies the use to it: `ApplyItemAim` pops mode 16 once, IA5, and the window stays). The use's own cast belongs to the item use (ARC-DEV-365); the smoke hero has no usable magical item, so the smoke checks the routing up to the use.
- **Not ready.** An item of a loot, steal or barter window as a target (those windows lay their items out themselves and the cast host knows the hero's and the shown map's NPCs' items only); the right-release branch; the multiplayer packets.


## Dispel (ARC-MAGIC-034)

Slice MA-3 of the [magic audit](../research/magic-audit-res-103.md) (owner decisions D1–D6 of 2026-11-11), on the executable reads [DP1–DP3](../research/ledger/spell-runs.md#dispel-component-arc-magic-034);
code `SpellRunSystem.Dispel` (Simulation), `SessionSpellCasterDispel.cs`, the session action `SessionSpellRunActionKind.Dispel`, tests `SpellRunSystemTests`, `SessionSpellCasterDispelTests`. Spells 41 (Disperse Magick) and 42
(Dweomer Shield: the Dispel on its caster, then its anti-magic shell flag) are the installed records with the component.

- **The component (0x451B90, DP1).** It has no data and takes the object the per-target stage handed it; a tile entry does nothing. It is in the component table (`SessionSpellCasterComponents.cs`) and is always taken (no
  `ComponentNotReady`), though two parts of the original are left (below).
- **The runs' half (0x451C40, DP2).** Every run but the one being processed (the dispelling run itself: Dweomer Shield is sourced by the very object it dispels and must not end itself) ends by event (`EndByEvent`:
  the maintain bar note, a BEGIN never processed freed and charged, else an interrupt 1 ms later) when its record is not technological and its **source** is the object (an inactive slot too), or, among active runs,
  its **target** or its **summoned list** (the objects its Summon components made, SUM7) holds it. A run matching two tests is told twice, as in the original. The endings are told after the outer action, as the destroyed
  objects' are (`_goneObjects`, the run rules cannot be re-entered from a component); they are events 1 ms later in the original as well, so only an ending of a never-processed BEGIN (freed at once there) comes a moment later
  here. The summoned-list test goes through the host port `ISpellRunHost.SummonedListHolds`, which this host answers from the objects its summon map holds for the run.
- **The object's half (DP2).** For a portal or container (ARC-MAGIC-037, [below](#locks-and-holds-on-portals-and-containers-arc-magic-037)): the magically held bit is cleared by a plain write. For the hero and a critter this host knows: the stoned spell flag (0x10000) is cleared by a plain write (`Dispelled` with `DispelStonedCleared`: no reference count), and then the object flag stoned (0x80000) that Flesh to Stone's ObjFlag wrote is cleared too when it is set (the object flags clear 0x43D280 the original calls after the spell flag's write, ARC-MAGIC-039; `DispelObjectStonedCleared`, a flag write marked as a side effect; the render bit 0x2000000 of field 0x147 it also clears is not kept by the session); an object flag stoned without the stoned spell flag is left, because the call sits inside the spell flag's branch; a created critter the session holds with the summoned spell flag (0x4000) is destroyed (`Dispelled` with `DispelDestroyed`, then `SummonDestroyed`; the destroy waits until the runs' endings above are told, because the original reads the runs' summoned lists before 0x43CCA0, and the runs hear of it after that). A critter that is no created one of the session and holds 0x4000 logs `DispelDestroyNotReady` (no spell makes one before E1-e); the hero is never destroyed here (the original destroys a PC with the bit too). The stoned clear is not done for a created critter.
- **`ObjectGone` fix (DP3).** The object-gone endings (0x459740) end the active runs **aimed at** the object (the run's target, +0xA0); the host read the run's other object (the attacker of an item's attack) before.
- **Perma-polymorph (ARC-MAGIC-047, DP2).** The last step of the object half: a PC or NPC (not one the summoned bit destroys, not a created critter) whose critter flags 2 hold bit 0x400000 (the data's value with the session's writes, schema 90) gets the Trait restore (the `TraitRestored` line, the session's monster body removed, the elemental bits cleared for a body it knew as an elemental one) and then the bit cleared by a plain write; the `Dispelled` line carries `DispelPermaPolymorphUndone` (8). The viewer redraw of a map NPC whose data body is the polymorph (the stored art, field 24) is not built: nothing in the session says which body to draw.
- **Not ready.** The summoned critters of the map's data, and the network packet. The host does not read the spell's resistance or reflection again: the
  per-target stage ran before the component, as for every component.
- **Census (MA-0).** Records 41 and 42 leave the not-ready list on both census targets (the real-data census table lost their rows).

## Social and AI flags (ARC-MAGIC-035)

Slice MA-4 of the [magic audit](../research/magic-audit-res-103.md) (owner decisions D1–D6 of 2026-11-11), on the executable reads [SO1–SO8](../research/ledger/spell-runs.md#social-and-ai-flags-arc-magic-035) (0x454A10 re-read in the
disassembly); code `SessionSpellCasterSocial.cs` (the cast host), `ISpellSocialHost` and `AreaSpellSocial` (the AI host's part), `GameSessionOwner.SetBlinded`, `AreaAiHost.SpellReaction`, `SpellPeace`, `SpellFlee` and `SpellStopFleeing`;
tests `SessionSpellCasterSocialTests`. The five flags the ObjFlag component of MA-1 left not ready run in the original's own order, with the run's **parent** (the caster) as the one who acts (SO1):

| Flag (field) | Set | Clear |
| --- | --- | --- |
| Charmed (spell flags 0x800000) | NPC: an animal makes **peace** with the caster (`ISpellSocialHost.Peace`), any other NPC gets **+30** toward the hero when the caster is the hero (`ChangeReaction`); then the flag. The original also adds the NPC flag KOS_OVERRIDE (named, below). | NPC: **−30** toward the hero when the caster is the hero, then the flag off. Any other object: the plain write. |
| Mind controlled (spell flags 0x8000000) | No caster: the plain write. A caster and **no NPC** (the hero): **nothing is written** (`SocialIgnored`). An NPC: **−50** toward the hero (hero caster, target not summoned) **before** the flag, the flag, a **forced join** of the NPC under the caster (`ForceJoinParty`; the hero is the party's one leader, any other caster logs `SocialNotModelled`), then **no-flee** for good (`SetNoFlee`). | NPC: the flag off, then a **forced leave** (`LeaveParty` with `Force`); the reaction is not given back and no-flee stays. |
| Fleeing (critter flags 0x10) | NPC: **flee** from the caster (`ISpellSocialHost.Flee`, the danger setter's FLEE); the flag is **never written** (the setter's state is the session's danger record). The hero: nothing. | NPC: **stop fleeing** (the setter NONE, then the NPC's flee goals ended). The hero: nothing. |
| Blinded (critter flags 0x80) | The life state's **blinded condition** is set (`GameSessionOwner.SetBlinded`: what the combat resolver, the skill checks and the AI gate's recovery roll read); the hero and an NPC. | The condition is cleared. |
| Undead (critter flags 0x4) | The object flag 0x20000000 (a side effect), then the critter flag. | Both taken back. |

- **The social host.** Charm's reactions and peace and fear's flight need the shown map's AI host (its reaction facts, danger facts and goal runs), so the cast host asks an `ISpellSocialHost` (`SessionSpellCaster.Social`; the viewer wires
  `AreaSpellSocial` over its AI host). Without one a charmed, mind controlled or fleeing flag on an **NPC** stays not ready (nothing is written); a host that answers false for a step is logged (`SocialRefused`, the effect it was asked) and
  the flag is written as the original does regardless. The hero's flags need no host; blinded and undead need none.
- **The host's steps.** `SpellReaction` is `AdjustReaction` with the NPC's and the hero's reaction facts (0x4C0DE0); `SpellPeace` is `MakePeace` (0x4AA300) with the order facts, then the NPC's attack goal (18 or 19) at the hero's side is
  interrupted at level 5 (the helper 0x44E050); `SpellFlee` is the setter with FLEE and the caster as the fleeing-from handle (0x4AF130); `SpellStopFleeing` is the stop-fleeing rule (0x4AF170) and ends the NPC's run when a frame of it is the flee goal (25).
  Each answers false for an NPC the host does not run.
- **The log.** `SpellCastEventKind.SocialEffect` carries what was done (`SocialReaction` with the delta, `SocialPeace`, `SocialFlee`, `SocialStopFleeing`, `SocialJoin`, `SocialLeave`, `SocialNoFlee`, `SocialBlinded`) or not done
  (`SocialIgnored`, `SocialNotModelled`, `SocialRefused`).
- **Not ready.** The NPC flag KOS_OVERRIDE a charm adds (the session keeps no NPC flag layer); a follower of a caster that is no hero; the undead flag's readers (the combat values and the life rules read the data's undead bit, so a created
  undead is not undead for combat, healing or poison yet); the flags on a created critter (E1-e); an END after the NPC left the shown map (the blinded condition is cleared; charm's −30 and stop fleeing need the AI host and are logged refused); the reaction facts' `MindControlledByPc` (the host's facts carry false, so a later reaction change of a mind-controlled NPC is not skipped).

## Not ready

- A worn item dropped on the hotkey bar (the original takes it off the doll into the location 2000 + entry; the session refuses it);
  the sounds (sound 0, the drop's item sound, SN31), the item use and the cursor hold of an item entry, the pick-pocket and disarm-traps
  buttons, the stunned or paralysed test (critter flags 0x20, 0x40) and the conditions 0x566EF0 and 0x541680 (ARC-UI-022).
- The spell pane (ARC-UI-053): the selected college in the save, the weapon button at (161, 443) and the weapon's pane (bar mode 8:
  background 564, five slots at y 542) on the strip, the skills pane (bar mode 2), the compact interface's pane, a spell's icon beyond
  spell 79.
- The hotkey bar (ARC-UI-027, ARC-UI-032, ARC-UI-035): a spell's icon beyond spell 79 (the item window's lines after the name are built
  since ARC-UI-108, [OH12–OH18](../research/ledger/ui-audio.md#hovered-object-arc-ui-057)), the turn-based action-point preview
  (0x557370), the inventory window modes' extras of the still pointer (HK23),
  the recent actions' starting skill-bar buttons (pick pocket and prowling; the session starts them empty), the skill bar as a drag source,
  the compact interface's bar, and the count's font.
- The missile's light and sound (an NPC caster's missile flies since ARC-WORLD-105, its facing turn is the AI host's since ARC-WORLD-102).
- Eye-candy sounds and lights, tinting, rotation, `can_autoscale`, the object's scale and offsets
  (EC9, EC12).
- Other goals while a cast plays: the viewer ignores world presses and the stick until the goal and its queue end, and refuses F7
  meanwhile (the session saves no goal or queue).
- The hotkey bar (0x54FCF0,
  0x57C080, IP7–IP9): later slices of ARC-UI-016. The host runs an item's own spells with the item as source since ARC-MAGIC-020
  ([item spell triggers](item-spell-triggers.md)); the weapon's spell pane is [above](#the-weapons-spell-pane-arc-ui-016).
- The weapon's pane draws no spell icons and shows no hover description (0x5507E0).
- The maintain bar (ARC-MAGIC-026): the hovered object of a slot's hover (0x43C270), the bar's save with the runs (MT9b), the
  multiplayer stop message and the second set of slot buttons in their own windows (0x5C6E90, the compact interface).
- The pick's stand-ins (ARC-MAGIC-021): the hero is no drawn pick target, so the pick finds it only through its tile; the order of
  the objects on one tile is the host's (the hero, then the map's rows in file order; the original's sector list order is not
  read); the dead critters seen through are the whole map's, where the original takes those in a window-sized rectangle around the
  PC (the same unless the view is scrolled away from the hero); the screen pick has the hover's limits (ARC-UI-005).
- A run's events on a map the viewer has no cast host for (no combat host, or no spell data yet) get plain passes, which refuse
  them.
- In a turn-based round (ARC-COMBAT-020) the casting goal's start charges its 4 action points through the round's world; outside
  one nothing is charged. The predictor has no display in the original's UI (SU11), so none is planned.
- Worn mana stores do not pay (no item store state).
- NPC casters' weapons; the sector kind; paths.
- The raise and taking-damage scripts run no SCR.

## Movement component (ARC-MAGIC-036)

Slice MA-5 of the [magic audit](../research/magic-audit-res-103.md#movement-ma-5-arc-magic-036) (owner decision D3b: tile movement first), on the executable reads [MV1–MV3](../research/ledger/spell-runs.md#movement-component-and-the-immediate-path-relocation-arc-magic-036).
Code `SessionSpellCasterMovement.cs` (the cast host), `ImmediatePathWalk` (Simulation, the path walk), `AreaAiHost.RelocateAlongPath` (NPCs and companions), `TerrainViewerMovement.cs` (the viewer's host: the hero and the dispatch), `AreaMapTransfers.Enter` with its
`jumpPoint` argument; tests `SessionSpellCasterMovementTests`, `ImmediatePathWalkTests`, `AreaAiPathRelocationTests`, the smoke stage `movement`.

- **The component.** Type 0 (`Move_Location: Target_Tile`, Spatial Distortion, spell 3) on the hero or a critter the host knows, with a run whose margin is 0 and a target tile other than (0, 0): the host logs `SpellCastEventKind.Movement` (the critter, the tile's x in the value, y in the detail) and answers
  "taken". A resisted run, a (0, 0) tile and a missing target object do nothing and are not "not ready". A target that is no known critter stays `ComponentNotReady` for the relocating types (0 and 1); types 1 to 3 are in the second half below.
- **The relocation** is the host's, as the knockback is: the path request of the drop (`AreaDropMap.Path`, flags 0x10, the owner kind by the critter's side) gives a count of at most 8; the critter walks **that many tiles of the route and no further** (a far target leaves it on the eighth tile), its walks end, it is placed on the last
  tile, and the tile scripts run: every tile walked (tile script only, no jump point, no sector script) and the last tile once more with its jump point and sector script, as the placement's notification does. No route (equal tiles, a blocked or occupied destination) moves nothing.
- **Not ready.** See MV rows: objects that are no critter, a created critter, the interrupt of goals other than walks (the scripts' order against the placement and types 1 to 3 are closed by ARC-MAGIC-038 below).

## Teleport types and the anti-teleport test (ARC-MAGIC-038)

Second half of slice MA-5 of the [magic audit](../research/magic-audit-res-103.md#movement-ma-5-arc-magic-036), on the executable reads [TP1–TP5](../research/ledger/spell-runs.md#teleport-types-and-the-anti-teleport-test-arc-magic-038).
Code `AntiTeleportTable` (Data: `Rules\AntiTeleport.mes`), `AntiTeleportRules` (Simulation: the test 0x4BDD30), `SessionSpellCasterTeleport.cs` (`ISpellTeleportHost`, the components, `PayTeleportCost`), `AreaSpellTeleport` (World: the host over the world-map data and the shown map), `TerrainViewerTeleport.cs` (the portal's
change of place and the world map's teleport mode), `DropPlacementRules.PlaceOnce`; tests `AntiTeleportTableTests`, `AntiTeleportRulesTests`, `AreaSpellTeleportTests`, `SessionSpellCasterMovementTests`, `AreaAiPathRelocationTests`, the smoke stage `movement`.

- **Invocation check.** Spell 4 asks the anti-teleport test for its **parent** before the cast (`SessionSpellCaster.Teleport`, IV9 of the invocation check); with no teleport host every teleport is allowed. A refusal logs message 10000 whenever the parent is the hero, for a direct cast, an item's spell and a script's cast alike.
- **Type 2 (Teleportation).** With a run whose margin is 0 and a current target object, the test is asked for the run's parent: a refusal logs message 10000 (`SpellCastEventKind.Message`) and does nothing. Otherwise the host logs `SpellCastEventKind.TeleportMap` (the source object, the spell in the value) and **moves nothing**: the viewer opens the world map in its teleport mode. A pick there is
  paid by `PayTeleportCost` (the run's cost, only when the source is the hero; the original ignores the payment's answer, so a hero short of fatigue still lands) and lands the hero (the world map's landing, flags 0x20). Spell 4 charges nothing at BEGIN.
- **Type 3 (area portal, record 218).** The host asks the teleport host for the object's area (`SpellPortalPlan`: the area, whether the test allows it, the area's location on the start map); no area or a refusal logs message 10000 for the hero and does nothing; otherwise it logs `SpellCastEventKind.TeleportPortal` (the object, the location's x in the value, y in the detail) and the viewer sends the hero to the
  start map at that location with the teleport spell's change-of-place request (flags 0x120).
- **Type 1 (random tile).** The host draws the direction (`SpellRollPurpose.MovementDirection`, 0 to 8) and logs `SpellCastEventKind.MovementRandom` (the radius, the critter, the direction); the viewer places the critter with `DropPlacementRules.PlaceOnce` and runs the relocation of the first half. A step off the map relocates nothing. The original measures the placement from the run's target block; the host uses the current target object's tile, which differs only for a run with several targets (no installed record uses type 1).
- **The scripts' order.** The hero's relocation enqueues the tile scripts of the walked tiles and runs them **before** the interrupt and the placement, then the last tile's full entry once more; NPCs and companions run theirs through `AreaAiHost.BeforeRelocationScripts`, as the original does (MV2, MV3).
- **The teleport mode's window and the followers (ARC-MAGIC-1147, ledger [TP6–TP9](../research/ledger/spell-runs.md#teleport-types-and-the-anti-teleport-test-arc-magic-038)).** The original's pick has **no town map and no route points**: the scripted open clears the town number and the walkable flag, so the window opens in the map view, the town map cannot be reached, a click picks an area and never adds a waypoint, and the Travel button is hidden. The viewer does the same (`AreaWorldMapArtLayout.TravelShown`, the panel's `teleportMode`, the guard in `StartWorldMapTravel`); the compass, the continent view and the lens stay. The pick's landing is the world map's change of place of the hero (flags 0x20), so **the whole party goes with the hero** and arrives next to it (the carried companion is observed by the smoke stage `teleport-follow`, which casts spell 4, picks a known far area and checks the companion's distance and drawing); the area portal's request (flags 0x120) is the same without the wait orders. Not built: the fallback to the area portal when the map module cannot be read (TP8; it needs the same data), the carried followers of a follower and the exclusion by the follower flag field 0x118 bit 8 beyond what the change of place models.
- **Not ready.** The multiplayer host branch of type 2; an NPC or other non-critter as the target of the portal and the relocation (the viewer logs "not ready"); message 640 shown as an English label; the centring of the view; the interrupt of goals other than walks.

- **Not ready.** See MV rows: types 1 to 3, objects that are no critter, a created critter, the interrupt of goals other than walks, the scripts' order against the placement.

## Locks and holds on portals and containers (ARC-MAGIC-037)

Slice MA-6 of the [magic audit](../research/magic-audit-res-103.md), on the executable reads [LM1–LM6](../research/ledger/spell-runs.md#lock-flags-of-portals-and-containers-arc-magic-037) (0x454A10 tail re-read in the disassembly);
code `SessionSpellCasterLocks.cs` (the cast host), `GameSessionOwner.WriteOpenableFlag` and `LockRules.WriteBit` (the lock state of [lock-state-runtime](lock-state-runtime.md)), `ISpellCastMap.Openable` and `AreaSpellCastMap`
(the shown map's portals and containers), `AreaSpellCasting.Line`; tests `SessionSpellCasterLockTests`, `AreaSpellCastLockTests`. Records: Unlocking Cantrip (1), Magelock (75) and the Dispel of 41 and 42.

- **The component** (`ObjFlag` on field 46 or 56, before the critter flags in the table) takes a portal or container the cast map names (`ISpellCastMap.Openable`: the session's lock identity, the T0 baseline and the row's lock
  level, slot 37). A field of the other kind (46 on a container, 56 on a portal) and a critter target stay not ready.
- **Every bit is a plain write** through the session's lock state (`WriteOpenableFlag`): one revision, **no Lock event** (LM1: the original writes the field, it does not call the lock function), so an object a spell unlocked stays
  unlocked, and Magelock's held bit (set at BEGIN, cleared at END) is read by the open attempt, the door runs, the hover line and the target tests that already exist. There is no reference count: the first END of two Magelocks clears the bit.
- **The clear of the locked bit** runs the unlock gate first (LM3: no parent or a parent that is no critter, or the per-pass fate flag, passes; a lock level of 19 or less passes; above it the run's captured parent aptitude must
  reach the level). A refused gate leaves the lock and logs `UnlockRefused` (the value is the level) and the `Resisted` line with spell.mes 643 for the local player (0x45A520); a passed gate writes
  (`OpenableFlagWritten`, value the bit, detail 0x1 on and 0x2 a container). **`LockGuardAlert` is logged in both cases** (LM2: 0x4AEE50 with loudness 0 and kind 2, on the object, by the run's parent) when the run has a parent.
- **Dispel** clears the held bit of a portal or container by a plain write (LM5), with no gate and no line but `OpenableFlagWritten`.
- **The fate flag** (`_spellAtMaximum`, 0x5E75A8): set by `UseSpellAtMaximum` and reset by `Collect`, the two calls that open each target pass.
- **Target tests.** `AreaSpellCastMap` puts the session's lock flags (the baseline with the session's writes) into the portal or container target (`PortalFlags`, `ContainerFlags`), so the "locked", "not locked" and
  "not magically held" tests of a spell's areas (TA8) see what an earlier spell or a key wrote. Magelock cannot target a held door; Unlocking Cantrip's area is the object, portal and container.
- **Viewer lines** (the original tells none): "is unlocked", "is locked", "is held by magick", "is no longer held by magick"; the refusal is the "resists" line and message 643.
- **Not ready.** The guard alert's witnesses (0x4AEE50; PL6 and the pick-locks witnesses are not built either), the screen refresh, and the stone-to-flesh pair of Flesh to Stone (48) for a portal that holds the stoned spell flag (the
  session keeps written flags for the hero and mobile objects only; no installed record makes a stone portal; whether the installed maps store one was not checked).
- **Census (MA-0).** Records 1 and 75 keep their `ObjFlag` rows: the census applies every component to a critter, and a lock flag of a portal or container is not ready on a critter by design; the slice's tests cover the objects.

## Trait component (ARC-MAGIC-040)

Slice MA-8 (first slice) of the [magic audit](../research/magic-audit-res-103.md#trait-ma-8-arc-magic-040), on the executable reads [TR1–TR6](../research/ledger/spell-runs.md#trait-component-arc-magic-040) (0x452AD0, 0x452B00, 0x452CD0 and their art and item helpers). Code `SessionSpellCasterTrait.cs`, `GameSessionTraitArt.cs`
(the session state), tests `SessionSpellCasterTraitTests`. The installed records with the component: Polymorph (49), the body spells of air, earth, fire and water (13, 18, 23, 28) and the technology records 88, 110 and 111, all of the data field `art_num`.

- **What is taken.** The Trait component of art type (`TraitData.Field` = 1) on a PC or an NPC: the hero, a critter this host knows and a critter the session created. A run without a current target object, and a target that is no PC or NPC, do nothing and are not "not ready" (the original returns without a message);
  another data field, a critter this host does not know, and a species of 32 or more or a palette of 4 or more (the original's art builder refuses them and the game exits) and a negative species other than −1 (the original masks it to five bits and goes on; our choice, no installed record has one) are not ready.
- **The state.** The session keeps the species and palette the spell chose by critter (`GameSessionOwner.WriteTraitArt`, `QueryTraitArt`; the hero `pc` or a mobile `oid:`; at most 4,096; saved as `spells.traitArt`, schema 89, upgrade step 88 adds the empty list). The body's rotation, animation and frame are not kept:
  the original builds the new art from the old one's, so the viewer that draws the body will do the same when it draws, and the stored art (the original's field 24) is the critter's own data, which the session never changes.
- **Apply** (the value is the species): writes the record and logs `TraitApplied` (value the species, detail the palette in the low two bits, `TraitElemental` for the species 26 to 28, `TraitStateChanged` when the record changed). There is **no reference count**: a second apply goes over the first.
- **Restore** (the value −1, the END's component): removes the record and logs `TraitRestored` (value the species the critter had, −1 when the session held none; the detail as above, the elemental bit meaning the session's record was an elemental body; the original tests the critter's current art instead, so the host must test its own art too). It is done for a critter that has no record too, because the
  original rebuilds the art and chooses the best items again for any PC or NPC. The first END's restore takes the body off although another run still holds one.
- **The viewer's half (ARC-MAGIC-046; the original behaviour is the next paragraph).** `TerrainViewerTrait.cs` reads the two log lines (`TakeTraitEvent`): the hero's `TraitApplied` takes every worn slot off at the next world frame through the session's `Unequip` (an item whose take-off is refused, by an item script or a full inventory, stays worn and is logged; the original drops that leftover on the tile, which the viewer does not do yet) and the hero's `TraitRestored` chooses the best items (`EquipBest` with the inventory host's wear and insert checks). `TerrainViewerTraitBody.cs` draws every hosted NPC that has a record with the monster art (class 12 through `AreaCreatedCritterArt`, species and palette of the record, the rotation and animation of the NPC's own body, its own cell and facing) through the moving bodies the canvas draws; the record's removal draws the NPC's own body again. ARC-MAGIC-052 draws the hero's monster body: with a trait record for `pc` the viewer's `WantedHeroBody` is `HeroBody.Monster(species, palette)` and `HeroBodyArt.LoadMonster` reads the species' ARTs by the original's art id (class 12, species, animation, palette; critter name rules N1) from the window's art archives into the same five-action hero profile and facing table: stand (animation 0), walk (1) and run (6), attack (20) and the fall (7) and get-up (8) when the species has them, the fire elemental's low attack (21); not the throw (owner D2). The weapon part of the art id is 0 out of combat mode and 1 in it (ARC-MAGIC-1219, PR1/PR2), the original's missing-art repair picks the file (PR4: the stand with part 1 for part 0, the walk with part 0 for part 1, the walk for a missing run; the air elemental reads) and a palette the ART lacks is read as palette 0 (PR5). An action the species has no ART for plays the nearest it has (vault and attack the stand) under the action's own selector with the weapon part 15; a species without a stand ART or not eight rotations is refused, logged, and the hero keeps its own body until the next Trait apply. The monster body has no armour or shield part (the hero's worn items are off anyway) and its weapon part is only ever 0 or 1. The END's two component orders (the installed data clears the body-of flag, then restores the art; GrandFix restores the art first) end alike in the viewer, which queues the equipment step after the whole END (smoke `trait-body`). Limits of what is built: a companion keeps its own body (the companion group draws it), a dead or knocked-down polymorphed NPC lies in its own human pose (the fallen poses read the NPC's own body), a standing NPC that never moved shows the facing of its own art, not its current one, a monster has no armed or weapon pose, and the best-items choice at the restore wears only what the equipment host's policy wears (an item with item spells or a move script that was taken off at the apply stays in the pack, unlike the original, which wears it again). The new art id takes its rotation and animation from the old art through the original's two readers (0x502880, 0x503E20, so a unique NPC art keeps its rotation from bits 9 to 11). Smoke stage `trait-body`.
- **The original around the body (ARC-MAGIC-046 and 047 built it, except the hero's monster body).** The original takes every worn item off (the nine slots, an item that stays dropped on the holder's tile) when the body is applied, draws the monster body, sets the elemental critter flags 2 and blit flags for the species 26 to 28 (MG10: assigned, the other bits lost) and, at the
  restore, clears them, draws the stored body again and chooses the best items for every slot. The session half tells all of it by the two lines (the draw and the equipment are the viewer's, above). ARC-MAGIC-047 (schema 90) keeps the two flag words in the session: the elemental species 26 to 28 **assign** the critter flags 2 the value 2 and the blit flags 0x10 in Original mode (MG10: `MagicAnomalyProfile.ElementalFlagsAssigned`, every other bit of both words lost, the no-pickpocket bit among them; `GameSessionOwner.AssignObjectFlag`) and turn the two bits on in Steamwoven mode; the restore clears the two bits in both modes (a wiped word stays wiped, as in the original) for a body the session knew as an elemental one (the original tests the current art, which the session knows only through its own record). The data's blit flags are not read (taken as 0), the critter flags 2 of the hero and of a critter off the map are taken as 0 (a data-held perma bit is seen for an NPC of the shown map only), and a flag write the session refuses (more than 4,096 critters with written flags) is dropped without a log line. The perma-polymorph step is skipped for a created critter, which nothing in the session can mark with the bit. No reader of the two bits is built (the blood pool's elemental test reads another bit, 0x400 of the critter flags 2). The equipment rules already refuse to wear an item while the polymorph or "body of" flag is set
  ([equipment](equipment-runtime.md)).
- **Census (MA-0).** The Trait rows of records 13, 18, 23, 28, 49, 88, 110 and 111 leave the not-ready list on both census targets.

## Control components (ARC-MAGIC-044)

Slice E4-c of the [technology audit](../research/technology-audit-res-125.md#6-queue) (ARC-MAGIC-044, and the Use row ARC-MAGIC-048) on the executable reads [CN1–CN6](../research/ledger/spell-runs.md#control-components-arc-magic-044) (0x452F00, 0x4529D0, 0x451850, 0x4529F0, the loop 0x4510F0). Code `SessionSpellCasterControl` (the table of `SessionSpellCasterComponents`), tests `SessionSpellCasterControlTests`. The four components steer the list of **one target**; the jump is the run system's (`SpellComponentOutcome.JumpTo`, `EndOfList`), which runs component `n + 1` after a jump to `n`.

| Component | What the cast host does |
| --- | --- |
| NoOp (0x452F00) | Nothing, and it is not "not ready". |
| Terminate (0x4529D0) | Ends the list of this target (the next target's list starts; the run goes on). |
| ChargeNBranch (0x451850) | With a run source that has a payer: the payment of `Cost:` (apply on, the run's spell, so a cell record spends cells through the cell source and TC1; an item source spends a charge, saved as a BEGIN's is; a critter pays fatigue), no mastery halving. A failed payment jumps to `Branch:` (−1: the end of the list). No source, a source with no payer or a cost of 0 does nothing. |
| TestNBranch (0x4529F0) | For a PC or NPC target object: the target's **hit-point damage** (field 29: maximum hit points minus current) against `TestVal:` by the comparison (equal, greater, greater or equal, less, less or equal); a passing test goes on, a failing one (or an unknown comparison index) jumps to `Branch:`. A tile or a non-critter target does nothing. The other three fields (`art_num`, `Teleport_Tile`, `fleeing_from`) are logged not ready. |
| Use (0x452ED0, ARC-MAGIC-048) | With a target object: the host seam `UseObject` runs that object's own use script (point 1) with the object as its triggerer too (`AreaUseScriptHost.RunUseScript`, any triggerer; the viewer wires it in `TerrainViewerAi.UseScriptOfCast`) and `ObjectUsed` is logged (the value is 1 when a script ran). A tile does nothing; no host is not ready. Not modelled: the dispatcher's trap preamble for this call (CN7), a teleport the script asks for, an item held in a pack. |

**Not ready here:** the three other TestNBranch fields. The installed records with the components: 184 (a Use component after 4 s), 147 (a cell weapon, one cell per use), 168 (maintained armour: a cell a tick and a test on hit-point damage), 187 (a cell at BEGIN), and 188 (a NoOp).

## Cast component (ARC-MAGIC-049)

Slice E4-e of the [technology audit](../research/technology-audit-res-125.md#6-queue) on the executable reads [NC1–NC3](../research/ledger/spell-runs.md#cast-component-arc-magic-049) (0x451740, 0x455A20, 0x455AC0, 0x455C30). Code `SessionSpellCasterNested` (the table of `SessionSpellCasterComponents`), tests `SessionSpellCasterNestedTests` and the `SpellRunRulesTests` rows of `SpellInvocation.SourceLocation`, smoke step 3b of the stage `tech-craft` (record 203 on the player's data).

| Part | What the cast host does |
| --- | --- |
| Cast (0x451740) | Records the request: the spell (`Spell:`, 10000 when absent), the run's parent, the current target object and tile (the object's location when the target has only an object) with their facts as they are now. A target with neither object nor tile, or an object the host has no facts of, asks nothing. A parent that has left the map is logged refused (`NestedCast` with detail −1). No data (the reader gave none): not ready. |
| The start | After the outer action or event pass (`Act`, `StopMaintainedSpell`, `ProcessEvents`, and every host call that fires the pending items: `EndMaintainedSpells`, `ObjectGone`, `FireItemEvents`, `DyingAnimationEnded`, the damage and trap ports; a call from inside the run rules finds the session busy and keeps the request for the outer drain) the host starts each request in order through `GameSessionOwner.StartSpellRun` with no source, the parent, `SkipCastGoal` (goal 73) and the invocation's source location set to the parent's location (`SpellInvocation.SourceLocation`: the origin of the BEGIN-area test and the run's start location). The invocation check is the rules'; a refusal, a spell with no record or a full table logs `NestedCast` with detail −1 and starts nothing. A run with a parent plays its goal as an item executor's run does (the action points through `CastCombat`, then `Release`, whose BEGIN applies its components; a refused start frees the run unpaid); one without is begun by its event 2 ms later. |
| The log | `NestedCast`: run = the run whose component asked, value = the spell, object = the target (none for a tile), detail = the new run's slot or −1. `NestedCastsDropped`: the requests beyond `MaxNestedCastsPerStart` (64) in one start. |

The viewer sets `SessionSpellCaster.CastCombat` to the combat world its item triggers use (`TerrainViewerSpellCast`); the nested runs' components log into the same cast log the viewer posts every world frame. **Not modelled:** the original's start inside the component (the slot order), the unbounded self-cast chain (bounded), and the action points of goal 73 for a host with no combat world.

## Run object list (ARC-MAGIC-1234)

Found by the native stage `spell-effects` (ARC-DEV-1148): Congeal Time (76) never wore off, because its END area names the run's object list (high bit 0x80000000, no object bit) and no host kept one. Read in the executable
([ledger TA17](../research/ledger/spell-runs.md#spell-target-areas-arc-magic-007)): the run owns a list at +0x130; the first collecting action whose area has the list bit while the list is empty stores the gathered objects (newest first), and every
later action with the bit targets the stored objects and gathers nothing from the list. Spell 76's BEGIN gathers the non-party critters within 9 tiles and so builds the list; its END (the forced one, the duration's, a dispel's) removes effect 57
from every one of them, wherever they stand now on the map the host casts on (a listed object is looked up on the current map, as the run's target is: after a change of map the END finds none).

- **The run keeps it.** `SpellRun.ObjectList` (`SpellRunObjectList`, equal by contents); `SpellRunSystem` stores the list a collection built (`ISpellRunHost.CollectRun`: the targets and the built list) when the run's list is empty and the build holds objects. `SessionSpellCaster.CollectRun` hands the stored list to the target rules as the run list (`SpellTargetContext.RunList`, the objects still on the map; a stored list that holds objects is never rebuilt) and returns what the rules built.
- **The save keeps it.** Schema 92 adds `objectList` (the objects in order, each id, type, aptitude) to every run row of the spells section; older saves load with an empty list (their runs never had one). Restore refuses an entity that is not an entity name.
- **The Dispel reads it.** `SpellRunSystem.Dispel` ends an active run whose object list holds the object, as the third test of 0x451C40 (the processed run and technology runs are left, as for the other tests).
- **Not widened.** The reference counts (0x455100 and the object-flag counts), the run finder (0x459170) and the run lookup's reference check (0x455820) still read only a run's target, source and parent (the "under the influence" test 0x459380 of the AI chooser and the scripts, over 0x458CF0, reads the list: `SpellRunRules.UnderInfluence`); no installed spell sets a flag the counts read through a list. The aptitude kept in a list entry is the object's at the build. The stage `spell-effects` expects the removal for 76 now.

## Hit eye candy and object draw flags (ARC-MAGIC-045)

Slice E4-a2 of the [technology audit](../research/technology-audit-res-125.md#6-queue); readings HE1–HE4 and OD1–OD6 in the [ledger](../research/ledger/spell-runs.md#object-draw-flags-and-the-hit-eye-candy-arc-magic-045). No schema change, no new component.

- **The hit effect plays the shield's eye candy.** `SessionSpellCaster.CritterHit` (the cast host's hit effect 0x459A30, called by every hit that reaches a PC or NPC) now also reads the spell flags shielded (0x400, Shield of Protection) and magnetic inversion (0x400000): for each, the first active run that targets the critter and whose record sets the flag (the run finder 0x459170 as modelled) gives an `EyeCandyAdded` event of entry `6 × spell + 5`, play flags 0, caused by the run; magnetic inversion with no such run gives the fixed entry 0x449 with run −1; shielded with no run plays nothing. They come before the entangled branch's save, as in the original. No state changes. The viewer plays them through the eye candy host it already has. Not modelled: the one prototype's fixed entry 0x4EB (HE3).
- **The viewer draws what the object flags say.** The flags a run's ObjFlag component writes (invisible 0x200 and translucent 0x40, shrunk 0x80, don't-draw 0x100; the session keeps them over the map data's since ARC-DEV-358) change the body's draw by `AreaBodyStyleRules`: a don't-draw body, and an invisible body the player cannot see (detects invisible, or perception of 20 or more), is not drawn and not picked; a translucent or seen invisible body is drawn at alpha 128; a shrunk body at half size about its hotspot. Draw commands carry the frame's hotspot (`AreaDrawCommand.Hot`) for that; the canvas applies the styles by body (`SetAreaBodyStyles`), `TerrainViewerBodyStyles` computes them when the session changes. Eye candy and overlays are other commands and stay. Smoke stage `body-styles` (translucent, shrunk, don't-draw, invisible with and without detection, and the shield's hit eye candy on the first living NPC of the map).
- **Not built:** the overlays and eye candy of a shrunk body are not halved (the original halves them, OD4), an un-shrink of a data-shrunk body, a carried follower's style, the hero's shrink (its marker has no hotspot), the drunk flag (SFR2: the original has no reader), the one prototype's fixed hit entry.

## Remaining components, the spell stop and the focus wipe (ARC-MAGIC-1146)

Leftovers of the 0x44-0x45 triage (ARC-RES-108, issue #1146); readings RM1–RM8 in the [ledger](../research/ledger/spell-runs.md#spell-component-remainders-arc-magic-1146). No schema change (the charge count is the session's saved charge list, the teleport tile one more mobile object field).

- **EnvFlag** (0x452F10) is the original's stub: taken, no effect, no "not ready" line.
- **Recharge** (0x454920): an item target (object type 5..14) the host reads gains the component's amount up to `Max:` (9999), only while the charges are below it; any other target is left. The count is `GameSessionOwner.RechargeItemCharges` (the same saved list a spent charge uses; a recorded count also marks the item magical when the host reads it). The log lines are `ItemRecharged` (the new count) and `ItemRechargeNotSaved` (the session's refusal: an untracked item, a count outside 0..1,000,000). The one installed use is record 103.
- **Trait64** (0x452E40): for an NPC target, `Target_Tile` into the teleport tile field (242) stores the tile as the NPC's `npc.teleport.tile` with the open map as its teleport map (`SetTeleportTile`); `Caster_Obj` or `Caster_Owner_Obj` into the fleeing-from field (230) writes the run's parent as the danger record's fleeing-from (`WriteFleeingFrom`, no setter step). Every other field and source pair, the hero and other objects are not ready (`ComponentNotReady`). No installed spell uses it.
- **TraitIdx** (0x452D80) stays not ready: its data value is an addend to the effective stat (or resistance) that becomes the base value, and the session holds no write for an NPC's base stats or resistances.
- **Spell stop on an object** (0x459290, 0x4593F0): `FindRunOnObject(spell, object)` is the first active run (slot order) of the spell whose target, object list or summoned list holds the object; `StopSpellOnObject` interrupts it through the run rules and answers whether one was found. The script action 130 is a follow-up of the script lane.
- **Focus wipe and decay cancel.** The life step that kills an NPC queues the `combat.focus.wipe` event ([queue runtime](game-event-queue-runtime.md)); the life step that revives one removes its pending body-decay event (0x45EC80).

