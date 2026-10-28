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
- [`AreaSpellCastMap`](../../tools/terrain-viewer/Core/AreaSpellCastMap.cs) — the AREA map as a cast reads it;
- [`SpellUiRules`](../../src/Arcanum.Simulation/Magic/SpellUiRules.cs) — the spell UI's activation, cursor mask and no-target message
  (ARC-MAGIC-015) and the cursor's pick (`Pick`, `PickTest`, `PickSpares`; ARC-MAGIC-021);
- [`AreaSpellCasting`](../../tools/terrain-viewer/Core/AreaSpellCasting.cs) and the viewer's
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
- a path to every admitted tile;
- NPCs stand on their map cells, except the companions: since ARC-MAGIC-021 a companion's row stands on its committed cell and on
  none while it is on another map (no target and no critter then; a visitor from another map is not a row of the shown map and is
  no target); in a square query a companion comes after the rows of the swept cells;
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
   failed re-check keeps the run in the original and frees it unpaid in Steamwoven (MG41's switch). The missile is destroyed.
5. **A lost missile (`LoseMissile`, MG44).** A leg whose line is refused (more than 100 steps), a point off the map, another
   map or session ends the missile's goal without its release: the original destroys the missile and frees nothing, so the run
   keeps its slot, never begun; both rule modes free it unpaid (owner decision 2026-10-28: fixed without a switch).
6. **The viewer** (`TerrainViewerSpellMissiles.cs`). The hero's missile is drawn as the combat projectiles are (the faced art at its
   step's frame, moved by its pixel offset, additive when translucent, at the art id's draw scale); the cast's log line follows the
   arrival. The casting goal's facing turn turns the drawn hero in place until it moves. F7 is refused while a missile flies (the
   session saves no missile). Smoke: when its first damaging spell has no missile, the `cast` stage also casts the first critter
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
   dragged entry is emptied (its item back to the grid) and anything else ends with no change. A left release goes on with the drag. While an entry is dragged its key skips it and the stand-in keys leave it alone.

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
| The pick's end clears `OF_CLICK_THROUGH` on the objects it saw through, so one whose data carried the flag loses it (MG45). | No flag is written: such an object keeps it, in both modes. | — (technical bug, latent; fixed without a switch by the owner rule of 2026-10-28) |
| Queued casts whose goals never play (the stack ended through the end step or was interrupted) keep their runs in their slots, unbegun and unpaid, until the game ends (MG43). | The runs are freed, unpaid. | — (owner decision 2026-10-28: fix in both modes, no switch) |
| A queued goal is the casting goal 26 with its own get-up, unconceal and facing nodes and subgoals (QC2). | The viewer's goals have no subgoals and no facing turn, so the stack index is the number of queued casts. | — (not ready: the facing turn) |
| An eye candy's sound and light are played with it; a multi-rotation eye candy turns to its object's art; `can_autoscale` sizes it to a critter's body; an eye candy sits at its object's offsets. | None of these (EC9). | — (not ready) |
| A callback play flag makes the run's CALLBACK action wait for the eye candy's animation (EC11). | The callback never comes: the eye candy is drawn and the run goes on as if none were asked. | — (not ready) |
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
| An entry dragged off the bar keeps its hidden flag: it stays in the bar's data, undrawn and skipped by its key, until a drop on it or a load brings it back (HK15). | The entry is emptied (an item entry's item back to the grid). | — (technical, a lost state; fixed in both modes) |
| An item entry dragged off the bar leaves its item on the cursor for the inventory or the map to place (HK15). | The item goes back to the grid at once. | — (not ready: the map's cursor hold) |
| An item entry's count is set on its drop and on a load and recounted when an item leaves its place (HK6, HK12): an item picked up later shows only after such a change. | The count is read at every redraw, so a pickup shows at once. | — (display only) |
| The count of an item entry of the prototype 15085 is always 1 (0x462B40, HK12). | Its items are counted like any other's (as the same-kind finder, AM1). | — (not ready) |
| The type icon's tables are read without a bound, so an index past them reads the next table or what follows (HK17). | The same within the five tables and the message window's icons; past them no icon (the item's name shows). | — (latent: the installed items stay inside the tables) |
| An inventory art that does not read leaves the bar with what the art library gives for it (not read). | The type icon. | — |
| A broken item's inventory art carries the destroyed bit 10 (DU4), whose effect on the drawing was not read. | Drawn as stored; the bit is ignored. | — (not ready) |

Recorded for MT20a-2, which plays the goals (F1, one reader):
- **Friendly cast.** Goal 73 (the friendly cast) has no end and frees nothing on its refusals.
- **Queued cast.** A queued cast stores its casting animation in another slot than the one goal 83 plays (no college spell has a
  casting animation).
- **Overlay art.** A refused goal's overlay removal is called with art 0.

## Not ready

- A worn item dropped on the hotkey bar (the original takes it off the doll into the location 2000 + entry; the session refuses it);
  the sounds (sound 0, the drop's item sound, SN31), the item use and the cursor hold of an item entry, the pick-pocket and disarm-traps
  buttons, the stunned or paralysed test (critter flags 0x20, 0x40) and the conditions 0x566EF0 and 0x541680 (ARC-UI-022).
- The hotkey bar (ARC-UI-027, ARC-UI-032): a spell's icon beyond spell 79, the descriptions shown when the pointer enters a slot or a
  recent-action button (0x550150, read as HK19: the next slice),
  the recent actions' starting skill-bar buttons (pick pocket and prowling; the session starts them empty), the skill bar as a drag source,
  the compact interface's bar, and the count's font.
- NPC casters' missiles and facing turns (the viewer casts for the hero only); the missile's light and sound.
- Eye-candy sounds and lights, tinting, rotation, `can_autoscale`, the object's scale and offsets and the eye-candy callbacks
  (EC9, EC11, EC12).
- Other goals while a cast plays: the viewer ignores world presses and the stick until the goal and its queue end, and refuses F7
  meanwhile (the session saves no goal or queue).
- Spells on an inventory item (the inventory bit: the original opens the inventory to pick the item) and the hotkey bar (0x54FCF0,
  0x57C080, IP7–IP9): later slices of ARC-UI-016. The host runs an item's own spells with the item as source since ARC-MAGIC-020
  ([item spell triggers](item-spell-triggers.md)); the weapon's spell pane is [above](#the-weapons-spell-pane-arc-ui-016).
- The weapon's pane draws no spell icons and shows no hover description (0x5507E0).
- The pick's stand-ins (ARC-MAGIC-021): the hero is no drawn pick target, so the pick finds it only through its tile; the order of
  the objects on one tile is the host's (the hero, then the map's rows in file order; the original's sector list order is not
  read); the dead critters seen through are the whole map's, where the original takes those in a window-sized rectangle around the
  PC (the same unless the view is scrolled away from the hero); the screen pick has the hover's limits (ARC-UI-005).
- A run's events on a map the viewer has no cast host for (no combat host, or no spell data yet) get plain passes, which refuse
  them.
- In a turn-based round (ARC-COMBAT-020) the casting goal's start charges its 4 action points through the round's world; outside
  one nothing is charged. The predictor has no display in the original's UI (SU11), so none is planned.
- Worn mana stores do not pay (no item store state).
- Spell damage to objects other than critters; NPC casters' weapons; the sector kind; paths.
- The raise and taking-damage scripts run no SCR.
