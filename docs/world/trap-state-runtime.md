# Trap state (ARC-WORLD-076)

Status: **Implemented** (Godot-free rules and session owner), slices 1 to 3 of the trap domain
([FR-NAV-006](../requirements/world-travel-runtime.md#fr-nav-006)). The effects of the other
native trap scripts are applied since ARC-WORLD-107 ([Effects](#effects-arc-world-107)); the
AREA viewer's PC meets traps on a use, before each step and on a step since ARC-WORLD-110
([Viewer](#viewer-arc-world-110)). The spot trap roll is made since ARC-MAGIC-057 ([ledger SP1–SP2](../research/ledger/technology.md#spot-trap-roll-arc-magic-057));
the disarm roll and action are made since ARC-SKILLS-1156 ([Disarm](#disarm-arc-skills-1156)); the shot of
script 30002 is applied since ARC-TECHNOLOGY-1619 (the strike at once, without its missile: [Effects](#effects-arc-world-107));
the drawing of a spotted trap object, the shot's missile and the trap devices (ARC-TECHNOLOGY-011/012) are later slices. NPCs and companions meet traps under their steps and on a door they open since ARC-WORLD-1469
([NPC and companion meeting](#npc-and-companion-meeting-arc-world-1469)). The rules
follow the retail 1.0.7.4 executable as read in
[ledger TT1–TT10](../research/ledger/technology.md#trap-state-arc-world-076) and
[TT11–TT16](../research/ledger/technology.md#trap-effects-arc-world-107).

## What a trap is

A trap is an object's use script (attachment point 1, slot 1) whose script file header has
the nonmagical (0x1) or magical (0x2) trap flag. It sits on one of two carriers:

- a **trap object** (type 17) on a tile: hidden (don't-draw) until spotted, set off by a
  mover arriving on its tile, destroyed when its trap is removed;
- a **trapped object**: any other object (a container, a door, a scenery) whose use script
  is a trap, set off by a use; removal clears its use script's number.

The native trap scripts 30000..30007 are run by the trap code instead of the interpreter
(the dispatcher's `TrapScript` stop, E4); a trap flag on any other number marks an ordinary
script, which the interpreter runs.

## Rules (`Arcanum.Simulation.Traps`)

| Method | Original | What it does |
| --- | --- | --- |
| `Classify(script, headerFlags)` | 0x4BBDD0, 0x4BBE00 | No trap for script 0 or a header without a trap flag; magical wins over nonmagical. |
| `Known(observer, objectFlags)` | 0x4BBFE0 | An NPC without a PC leader knows every trap the PC did not place (object flag 0x1000000); the player and an NPC led by a PC know a spotted (0x2000000) or PC-placed one; no one else knows any. |
| `SpotGate(observer, trapDevice, trap, objectFlags, playerSpellFlags)` | 0x4BBE40 (gate) | No spot for a non-critter, a trap device item or no trap; a known trap answers spotted at once; the player's spell flag 0x100 spots without a roll; otherwise the spot trap roll decides (S10). |
| `MarkSpotted(observer, carrier, objectFlags, quiet)` | 0x4BC090 | Only the player or an NPC led by a PC sets the spotted flag; a trap object also loses don't-draw. The player gets line 0 of `mes\trap.mes` unless the caller is quiet. |
| `Disarm(trap, known, success, critical, training, device)` | 0x4BC7B0 | Nothing without a trap or when the disarmer does not know it; success removes it, an expert's (training ≥ 2) critical success also yields the device; a critical failure sets it off; any other failure does nothing. |
| `DevicePrototype(script, objectFlags, counters)` | 0x4BC9C0 | A native script only. A PC-placed trap gives 6047, or 15101 when the counters' top byte is set; otherwise the table 0, 6047, 7038, 7039, 7041, 7040, 10065, 0 for 30000..30007. |
| `NativeRun(script, headerFlags)` | 0x4BCB70 (frame) | Null when the trap code does not take the script. 30007 is taken and does nothing. Classifies the effect only (30000 a spell, 30001..30006 an attack; decoding the counters is 0x4BCDC0's, `TrapEffectRules.Of`); the victims are every PC and NPC in the square of half side 4, 2 or 1 (file flag 0x80, 0x40, 0x20) around the trapped object, or the triggerer alone; header flag 0x4 clears the script's own number. Also the eye candy lines of `Rules\TrapEyeCandy.mes` (3k played on the triggerer, 3k + 1 on each victim, 3k + 2 the spotted mark stopped on the trapped object, k = script − 30000; a spot mark plays 3k + 2 on a trapped object with a native script and a removal stops it). |
| `StepTriggers(moverSpellFlags)` | 0x4305D0 (trap gate only) | A mover with spell flag 0x2 or 0x4 (field 0x14) sets off no trap object. |

## Session owner (`GameSessionOwner`)

A trap's state is its T0 baseline (`SessionTrapBaseline`: carrier, use script, header
flags, counters and object flags, which the host reads from the map data and the script
file) until a spot mark or a removal. Each write records the property `trap.state` of the
object's entity as a map change (bit 1 spotted, bit 2 removed, bit 4 placed by the PC since ARC-WORLD-125, and from bit 8 the script
number the removal cleared), which the snapshot already carries, so the domain adds **no
schema**. A removal holds only while the baseline names the script it cleared, so a trap
armed again on the object later (a trap device on an object with an empty use slot, TP2) is
seen (a script trap device, ARC-WORLD-125, also writes the placed and spotted bits and so clears the removal); the spotted and placed bits stay through a removal, as the original keeps object flags 0x2000000 and 0x1000000
(TT5). A removed trap object is also recorded destroyed (`object.condition`), so the object
damage domain sees it gone. A host that reads the object's use script must take the trap
state's script (0 once removed), and must not offer a destroyed object to the trap methods
(an object an attack destroyed keeps its trap, as in the lock domain).

| Method | What it does |
| --- | --- |
| `QueryTrap` | The current class, script, object flags (with the spotted mark) and whether the trap was removed. |
| `TrapKnownTo`, `TrapSpotGate` | `Known` and `SpotGate` on the current state. |
| `MarkTrapSpotted` | Records the spotted mark for the player or an NPC led by a PC; NoChange otherwise or when marked already. |
| `TriggerTrap(cause, spottedNow, moverSpellFlags)` | The meeting of a triggerer with the trap. `Step` names a trap object only and first checks `StepTriggers`. `Use` and `Step` go through the object entry point: a triggerer that does not know the trap and spots it now (`spottedNow`, the caller's gate and roll) marks it (on the PC's side) and the use is cancelled (`Spotted`, with `PlayerLine` when the player gets line 0 of `mes\trap.mes`); a non-critter spots nothing; otherwise the trap fires (`Fired`) with its native run, and an auto-removing run removes it. `Attack` (the damage step's direct trigger 0x4BCB00) fires with no spot and never removes. An ordinary trap script fires with a null run: the host runs it through the dispatcher. |
| `DisarmTrap(success, critical, training)` | `Disarm` on the current state: removal, the device prototype (the caller creates the item in the disarmer's inventory), or a sprung trap fired as a use. |

Every write is one revision; a bad object, carrier, script, observer, cause or training
(0..3) is refused with `InvalidTrap`, an old version with `StaleVersion`, and a write the
session cannot take with `BudgetExceeded` or `RevisionExhausted`. A rejected result reports
nothing done (outcome `NoTrap` or `NotTrapped`, no run, no device), so a caller acts on the
outcome only when the mutation is not rejected.

### With the SCR dispatcher

The session decides the spot and the trap's own run; the dispatcher runs scripts.

- A use of a trapped object: the host calls `TriggerTrap(Use, …)` first, with its spot roll.
  `Spotted` cancels the use; `NoTrap` lets the use go on as an ordinary dispatch; `Fired`
  with a native run is the trap code's run (its effects: `SessionSpellCaster.ApplyTrapEffect`), and with a null
  run the host dispatches the ordinary trap script at the use point, its
  `CheckTrapBeforeUse` answering `NotSpotted` (the spot is decided), so E2 rolls only once.
- A step: the same with `Step` on the trap object of the destination tile.
- An attack's break: `TriggerTrap(Attack, …)`; an ordinary trap script then runs without the
  slot write-back (TT7), which the dispatcher's object entry point does not offer yet: such a
  host stops at the session's answer.

## Effects (ARC-WORLD-107)

A fired native run (`Fired` with a run) is applied by the cast host,
`SessionSpellCaster.ApplyTrapEffect(SessionTrapFiring)`, with the trapped object, the
triggerer, the run, the use script's counters and the trapped object's object flags (the
trap state's), and for script 30002 the shooter candidates the host lists. The rules are
`Arcanum.Simulation.Traps.TrapEffectRules` (ledger TT11–TT16):

| Method | Original | What it does |
| --- | --- | --- |
| `Of(script, counters)` | 0x4BCDC0 (cases) | 30000 a spell (top byte, cast even when 0); 30001 and 30003 normal damage, 30004 fire, 30005 electrical, 30006 poison, in the range of the low byte and the second byte; 30002 a shot of normal damage whose shooter's tag is the third byte; after a damage or a shot the top byte's spell when it is not 0. Null for any other number. |
| `Shooter(candidates, tag)` | 0x4BD150 | The first candidate whose use script is 30007 and whose counters' third byte is the tag, or none. |
| `CreditsPlayer(objectFlags)` | 0x4BD950 | A PC-placed trap (object flag 0x1000000) makes the player the damage source. |
| `Amount(effect, rolls)` | 0x4CEF00 | The range drawn once per victim (labelled `combat.Damage<type>`): no draw for an empty range, a reversed one swapped. |

For each victim, in order (every PC and NPC of the map's square around the trapped object,
or the triggerer alone for radius 0, none without a triggerer):

- **Damage** (30001, 30003..30006): the amount in the type's slot through the damage step
  (`AttackResolver.DamageStep`, the same as a spell's) with the trapped object as a non-PC
  attacker and no weapon flag (TT13); a critter's life takes it through
  `GameSessionOwner.ApplyDamage` with the trapped object as attacker and the player (for a
  PC-placed trap) as the source, which fires the life item events; an object takes it
  through the map (`ISpellCastMap.DamageObject`). The item events the step calls for fire
  before the victim's spell and the next victim, as inside the original's call. The hit's
  damage effect (TT14) is the host's to show.
- **Shot** (30002, ARC-TECHNOLOGY-1619, ledger TT15): no shooter, no effect at all (no draw, no spell either); with a shooter
  the amount is drawn from the range (one draw), then the dispatcher's two d100 rolls (the hit location, the technology-failure
  roll; a shot's hit is decided whatever they give), then the trap's top-byte spell is cast at the victim, then the missile's
  strike follows at once: the attack event of the shooter object goes out (`SessionSpellCaster.AiNotice` with the shooter as
  the attacker: an NPC victim goes to COMBAT_FOCUS on it, see [the AI host](#the-ai-host-and-the-shooter-object-arc-technology-1619)),
  then the victim's taking-damage script is asked with the shooter as the attacker, the damage step takes the amount in the
  normal slot and the hit effect of a PC or NPC victim runs. The victim's result carries the damage step's own outcome and
  the shooter. The player credit of a PC-placed trap is lost in a shot (the result's `Killer` is null: the strike's context
  names the shooter as the damage source, TT15). The strike also calls the shooter's critter-hits script point (0x4B2F60, not run) and the sound cue draws one random (0x4F0BF0, not drawn: the project draws no sound cues). The hit effect (0x4B6930) runs whatever the damage step did, a veto and an invulnerable target included.
  ARC-TECHNOLOGY-1635 (the missile): the strike does not follow the dispatcher when the host flies the missile (`SessionSpellCaster.LaunchTrapShot`, set by the viewer): after the spell the session makes a shot of its own
  (`GameSessionOwner.BeginTrapShot`: shooter object, victim, amount; the 16 shots in flight are shared with the hero's and the NPCs' shots, so a full budget strikes at once and, while trap missiles fly, a hero's own ranged attack or throw can be refused for the budget) and the host launches one projectile of the default art (scenery art 0, faced along its line, drawn)
  from the shooter object's tile to the victim's location. Where the missile meets the victim on the line (`SessionSpellCaster.StrikeTrapShot`) the event and the damage described above run; a victim that left the line is
  not struck: the missile flies on past its destination (as a miss), strikes another living critter by the shared stray rule (the same strike with the shooter as attacker) or ends at its range of 20 tiles with nothing struck.
  The session's shot is saved with the missile (`flights.trapShots`, schema 100: shooter, victim, amount) and strikes after a load. The hero victim's armed critical-miss fate (0x4F5270 kind 3, ledger TT15) makes the hit a
  miss that pops on the shooter's own tile (`SessionTrapHit.Missed`: the fate is used up, a third d100 of the critical-miss procedure is drawn, no event, no damage; the spell is cast all the same). Not built: a missile that
  meets an object other than a critter on its free flight strikes none ([#1760](https://github.com/Simarglok/arcanum-steamwoven/issues/1760)). Without a host that flies (no launch hook, or a launch the host refuses) the strike follows the dispatcher at once, as it did. The smoke stage `ranged` has a trap-shot leg on the real map: a scenery, portal or trap object 3..8 cells from the hero shoots through the cast host; the missile is drawn and strikes the hero for exactly the drawn amount, a hero moved off the line is not struck, and a shot saved in flight with F7 strikes after F8 as the live one did. The host lists the shooter candidates around the triggerer
  (`AreaTrapShooters.Candidates`: the objects of types wall, portal, scenery and trap in the isometric window of the game
  view, each with its use script and counters, the square query's order); without a triggerer it lists none.
- **Spell**: 30000, and any damage or shot with a top byte, starts the spell at the victim
  with no parent and no source (TT16); its BEGIN comes as an event of the host's pass. An
  unknown spell number casts nothing.

The result (`SessionTrapEffectResult`) names the effect, the damage source and each victim's
outcome, amount, damage step, hit points taken, shooter and spell run. The floating line
`mes\trap.mes` 1 over the triggerer (`TrapEffectRules.FiredLine`) and the eye candy lines of
the run are the host's. The effects add no session state of their own: the life, object and
spell run owners record them.

### The AI host and the shooter object (ARC-TECHNOLOGY-1619)

The attack event (0x4A9650, ledger [VB2-2 gap d](../research/ledger/npc-ai.md#partial-rows-by-risk-ai-slice-2-arc-res-1104)) of an object that is no critter reaches `AreaAiHost` through the cast host's
`AiNotice` hook (`AreaAiHost.SpellAttacked`). The host describes such an object to the hostility rules through `AreaAiScene.ObjectFacts` (the viewer fills it from the shown map's cast
map: type, tile and object flags; the busted state, 0x43F630, is not read), so `Find` answers an object of kind `Object` or `Wall`, its tile answers `Tile` and `Distance`, and the focus is
valid as the original's validity test 0x4AB990 keeps it (a wall or a busted one is not, a destroyed, off, not drawn or invulnerable one is gone). An NPC victim of such an attacker
is asked into COMBAT_FOCUS on it through the setter with no alert sound and the event returns (`AreaAiHost.AttackEvent`, plan `VictimFightsObject`); a victim of any other kind does
nothing more.

ARC-AI-1636: the NPC then fights the object. The danger update keeps the focus while 0x4AB990 does (the object whole, not a wall, within the leash), and the viewer fills the busted state of the shooter object
(`AreaAiObjectFacts.Busted`, 0x43F630 through `AreaObjectTargets.IsBusted`: a portal's or container's lock-domain busted bit 0x80, a scenery's or trap's busted condition; a destroyed object answers 0), so a break drops
the focus on the next pass (calm down, or the next target). Combat perform (0x4AC6E0) starts the attack goal on the object as on a critter; the goal's tests read a type 15 or 16 only for death (0x429450, 0x4294F0:
`AreaAiHost.DeadCritter` answers false for an object) and the object's flags 0x1 and 0x2 for its presence (`Gone`). The goal walks next to the object and its hit step is the session's object attack
(`AreaAiHost.HitObjectFocus` → `GameSessionOwner.AttackObjectByNpc`, no skill roll, the viewer's `AreaAiScene.ObjectStruck` follow-up as for a blocker of the line of fire), after which the attack event of the record (0x4B3170 → 0x4A9650)
goes out for the object as victim. Not built ([#1755](https://github.com/Simarglok/arcanum-steamwoven/issues/1755), [#1756](https://github.com/Simarglok/arcanum-steamwoven/issues/1756)): the attack animation reads the object's art (0x42B9C0 node 5 reads the target's art and flag 0x80), which the host takes as a standing critter's, and the NPC's spell and item choice for an
object target (0x4ABC70 reads no target type) runs over the cast host's targets unchanged.

## Viewer (ARC-WORLD-110)

The AREA viewer's trap host (`src/Arcanum.World/AreaTraps.cs`) meets the shown map's
traps for the PC. A row's baseline is the use-script host's
(`AreaUseScriptHost.TrapBaseline`): type 17 a trap object, any other type a trapped object;
its use slot as the session's map changes hold it, the script file's header flags (0 for a
script the content does not hold) and its object flags (slot 18). The trap object's entity
is `AreaUseScriptHost.EntityId`, as for the object damage domain.

- **Use.** `AreaUseScriptHost.Run` (a container's loot open, a guarded use site) asks the
  trap host first: `TriggerTrap(Use, …)`. A trap spotted now, a native run fired (its effect
  through the cast host; the trap code answers skip default) or a refused meeting ends the
  use with no script run (`AreaScriptedUse.Trap`); the loot window then stays shut
  (`TrapSpotted`, `TrapFired`, `TrapRefused`). An ordinary trap script that fired runs as
  the use script; no trap goes on as before. The host's use slot (`Slot`) and `IsTrap` take
  the trap state's removal, so a removed trap's script no longer runs.
- **Walk.** Before each step the PC's step gate checks the next tile (0x427110, TT10): every
  trap object there the player does not know gets a spot attempt; the first one spotted is
  marked, the step is refused (`TRAP_SPOTTED`) and the walk ends (a mark the session refuses
  lets the step go, so a full budget cannot block the tile for good), as the walk goal's
  next-step check leads to its end node (ledger [EX20](../research/ledger/npc-ai.md)). The
  check comes before the turn-based step charge, as the walk goal's check (0x4270B0) runs
  before its step start (0x42E9B0).
- **Step.** Each step the PC starts onto a tile meets the first trap object there
  (`TriggerTrap(Step, …)`, 0x4305D0, TT9): the PC's spell flag 0x2 or 0x4 steps over it, a
  known trap fires too; a trap spotted or fired ends the walk on that tile, and since ARC-WORLD-1582 so does any trap object on it the mover does not step over (`AreaTraps.StepEndsWalk`, TT17: an ordinary script, a 30002 shot or a refused meeting end it too). The original
  meets it on the arrival; the viewer meets it when the step starts, as it runs tile scripts,
  so an advance that spans several steps (a very slow frame) may carry the PC past the tile
  before the route stops. A trap object with an ordinary trap script is not met on a step
  (its dispatch with the trap as attachee and extra object is a later slice).
- **Spot.** An unknown trap is spotted without a roll when the player's spell flags hold 0x100
  (TT3, the gate `Detected`); otherwise the hero rolls the spot trap skill (ARC-MAGIC-057, ledger
  SP1, SP2): `GameSessionOwner.RollHeroSpotTrap` runs the shared check (the hero's level and training,
  its injuries, the light), then `TrapRules.SpotCase`: a master that failed without a critical failure
  rolls once more, and below expert a magical trap is never spotted. The roll draws from the
  session generator and costs one revision; a success is marked by the caller (`MarkTrapSpotted`).
  The walk check rolls for each unknown trap object on the next tile (the first one spotted ends the
  walk) and a use or a step rolls once more for the trap it meets, as the original's separate
  attempts do (SP2); a failed roll leaves the trap unknown and the use or step goes on, so the trap
  fires. The light at the trap is the light at its cell (ARC-WORLD-1149, as the lock pick's): the darkness term of an untrained hero
  applies. The roll's modifier is the powered shield's (ARC-MAGIC-1189, SP2): the worn item at location 1005 named 2866, charges of
  ammunition type 2 in the roller's list and an aptitude (stat 16) at or below 0 give the aptitude itself (negative: a bonus);
  `TrapRules.SpotItemModifier` is the rule, the session reads the facts. A hero-less session rolls nothing (the trap stays unknown).
- **NPC and companion spot.** `GameSessionOwner.RollNpcSpotTrap` is the same branch with an NPC as the source (ARC-MAGIC-1189, TT2, TT3, SP2):
  the NPC's own spot trap level and training from its sheet and effects, its blindness and crippled limbs, the light at the trap, the
  modifier of the powered shield it wears; no fate and no difficulty-setting term (the local PC's). The gate is the observer's
  (`GameSessionOwner.NpcSpotObserver`): a free NPC knows every trap the PC did not place, a companion led by the PC knows the spotted and
  the PC-placed ones, and the player's Detect Traps flag spots for any observer. `AreaTraps.WalkCheckNpc` is the walk check of the NPC's
  next tile (0x427110, TT10), asked by `AreaAiHost.StepBlocked` through the scene's `Traps` provider before the step starts: the first
  trap spotted blocks the step as a step that cannot start does (the walk ends, EX20). A companion on its own follow walk (not held by
  the AI host) asks the same check through `AreaCompanionGroup.StepTrap`, which refuses the step with `TrapSpottedReason` (the gate stays on the companion's grid through its rebuilds; a companion the AI host holds asks nothing there, as the host's own check rolled). The AI host rolls only for a step that can start. A walker meeting a trapped door tile rolls again after its door push, as the walk node restarts; once a companion marks a trap the next frame's step goes (nothing ends its follow goal or plans around the tile yet). A companion marks what it spots (the player
  knows it from then on); a free NPC marks nothing (TT4), so it rolls again at its next step. An NPC with no sheet or life rolls nothing.
- **NPC and companion meeting.** See [NPC and companion meeting](#npc-and-companion-meeting-arc-world-1469).
- **Disarm.** See [Disarm](#disarm-arc-skills-1156).
- **Effects.** A fired native run goes to `SessionSpellCaster.ApplyTrapEffect` with the cast
  map's id of the trapped object and the hero as the triggerer. Without a cast host no trap
  is met at all (a use goes on as before and the dispatcher stops at a native trap script; a
  step meets nothing), so no trap is consumed without its effect. A 30002 trap is met when the host lists
  its shooters (`AreaTrapEffects.ListsShooters`, ARC-TECHNOLOGY-1619: the viewer does, `AreaTrapShooters`); a host that does not leaves it unmet (the use is then refused as before). The floating line, the eye candy and the
  damage effect are not shown; the status line names the outcome and the
  `mes\trap.mes` line number.

## NPC and companion meeting (ARC-WORLD-1469)

The firing half of E6-a for a walker that is not the hero (ledger [TT17, TT18](../research/ledger/technology.md#trap-state-arc-world-076), TT7, TT8, TT9). The original's step function runs for every critter that moves, so an NPC
or a companion that arrives on a tile with a trap object sets it off with itself as the triggerer, and the opener of a trapped door meets the door's trap on its use node.

- **Host.** `AreaTraps.StepNpc` (a step onto a tile) and `AreaTraps.UseNpc` (a use) are `Step` and `Use` for an `AreaTrapWalker`: the critter's entity, whether the PC leads it (the observer of TT2) and, when it has a sheet,
  its `SessionNpcSpot` (null: it cannot roll, so an unknown trap is met without a spot attempt, as a missed roll). The meeting is `GameSessionOwner.TriggerTrap` with the walker's observer and spell flags
  (`AreaTrapEffects.SpellFlagsOf`: a walker whose spell flags hold 0x2 or 0x4 steps over the trap, as the hero); a trap it does not know gets the entry point's spot attempt with its own roll
  (`GameSessionOwner.RollNpcSpotTrap`, as on the walk check), the player's Detect Traps flag spotting without one; a known trap fires. A fired native run is applied through `AreaTrapEffects.ApplyByCritter`
  (`SessionSpellCaster.ApplyTrapEffect` with the walker as `Triggerer`: the victim of a radius 0 trap, the floating line and eye candy being the host's, as for the hero). Nothing is met when the cast host cannot
  take a critter's trap (no trap consumed without its effect), and an ordinary (non-native) trap script is not met on a step, as the hero's.
- **Spotted.** A spotted trap cancels the step or the use as for the hero (`EndsWalk`, `Cancels`); only a companion led by the PC marks it (TT4), so the player knows it from then on; a free NPC marks nothing and,
  its trap being unknown to it again, rolls again at its next meeting.
- **Steps of the AI host.** The walk node asks `IAiGoalWorld.StepMeetsTrap` right after it began the step (`AreaAiHost.TrapMetOn`); a trap spotted or set off makes the goal's path index its length, so the walk ends on
  that tile and the goal above plans again from there (TT17). It covers the NPCs the host walks and the companions it holds. The meeting is taken at the step's start, only for a step that really began, not at its arrival (the viewer's difference for the hero's
  step too, above): an NPC already stands on the trap's tile, a companion the host holds walks there over the next frames. Since ARC-WORLD-1582 the walk also ends on a trap object that is no trap or whose shot is not built, as the original's (TT17; `AreaAiHost.TrapMetOn` asks `AreaTraps.StepEndsWalk`, a walker with the step-over flags 0x2 or 0x4 is spared). A relocation (a far NPC placed on its standpoint, a route's relocation, a knockback slide) is no step and meets no trap.
- **Companions on their own follow walk.** The viewer reports the tiles the companions' drive stepped onto (the ones the AI host does not hold) to `AreaAiHost.TrapMetOn`; since ARC-WORLD-1582 a walk that ends on the tile (TT17) is cut short: `AreaCompanionGroup.EndWalkOnTrap` stops the
  companion's goal through `CompanionFollower.EndWalkOnTrap` (a started step still ends at its cell centre, an ordered goal ends, the stop is not remembered as unreachable and the next update goes on approaching from where the
  companion stands, as after a stop before a critter), and a trap that spots the companion marks it. The drive of one frame is complete before the viewer reports its steps, so a frame whose advance spans two steps (a very slow frame) may carry
  the companion past the tile before its goal stops, as for the hero.
- **Doors.** The opener goals' use step (`AreaDoorHost.UseDoor`, 0x428A10, TT18) asks `AreaDoorHost.OpenerScript` for an NPC or a companion opener (`AreaAiHost.TrapMetByUse`, then the door's own use script, ARC-WORLD-1668); a trap that stops the use (spotted, a
  native run that answers skip default, or a refused meeting) or a script that skips the default ends the opener's run and the door stays shut. The hero's own door use meets it since ARC-WORLD-1581 ([Door use node](#door-use-node-arc-world-1581-arc-world-1668)).
- **Viewer.** The meetings the walkers had are logged (`ARC-WORLD-1469 walker-trap`) and shown on the status line with the outcome (`AreaAiHost.TakeTrapMeetings`).

## Door use node (ARC-WORLD-1581, ARC-WORLD-1668)

The hero is an opener too (ledger TT18, TT7). The use node of goal 12 (`AreaDoorHost.UseDoor`, 0x428A10) charges the 2 action points first for any target type, a door included (its first call is the payment 0x4B7CD0 with the cost 2, CT3; ARC-WORLD-1668 re-read the node and found that
the earlier "no charge for a door target" was a misreading: the charge is the call in the node's first condition, before the access test and the type switch). For the hero the viewer charges in `HeroUse` (`TerrainViewerAreaPortals.HeroDoorUse`) and then, as the original's node runs the door's use
script through the object entry point (0x441980, point 1) with the user as the triggerer **and** the extra object, runs the door's use slot in two steps (`AreaDoorHost.HeroScript`, `HeroDoorScript` in the viewer; an NPC or companion opener's `OpenerScript`, `OpenerDoorScript`, does the same with itself as the user):

1. The door's trap meets the opener (`AreaTraps.Use` for the hero, `AreaTraps.UseNpc` through `AreaAiHost.TrapMetByUse` for an NPC): a trap spotted now, a native run that went off (its effect through the cast host, the opener the triggerer) or a refused meeting answers 0: the opener's run ends, the door stays shut and the points stay spent.
2. Then the door's own use script runs (`AreaUseScriptHost.RunUseScript` with the opener as the triggerer and the extra object, its effects applied as a use's are, the printed lines to the message window): an ordinary (non-native) trap script, which the meeting only reports as fired, runs here, and so does a door's plain use script. The dispatcher's answer "skip default"
(including a script that runs past its last line and a script file the content lacks, as the original's dispatcher does) answers 0 as well (`AreaUseScriptHost.UseStops`): the run ends, the door stays shut. "Run default", a door with no use script, a trap that is gone, a native trap script the trap code could not meet (no cast host, a critter the host cannot apply it for; a divergence: the original fires it and consumes a self-removing trap, here nothing is consumed and the door opens as before) and a run the host cannot apply (refused by the admission, effects it cannot apply, a teleport it cannot take, changed files) go on and the door opens.

**A teleport or a conversation the door's script asks for** (ARC-WORLD-1805, ledger TT20): the teleport action only records a request (0x4D3380) and the script goes on, so the answer decides the door as above and the object changes place at the next ping. The hero's run takes a teleport of the hero: the transfer is queued (`AreaMapTransfers.QueueDoorTeleport`; a refusal of the destination leaves the run unapplied and the door opens; a transfer already pending drops it, TR4), and the viewer applies it at its next safe point, after the door's opening started. A door or window is no use trigger of `AreaMapTransfers` (the four installed doors that teleport, one script on two doors of map 5067, one on 5068, one on 5071, are used through the node like any door: the action points, the lock test, the trap, the script, the opening). An NPC's or a companion's teleport by the script is not applied (the run is left unapplied and the door opens, [#1837](https://github.com/Simarglok/arcanum-steamwoven/issues/1837)). No installed door or window use script starts a conversation, so none is built (census `--door-use-census`). Viewer line: `ARC-WORLD-1805 door-teleport`.

Viewer lines: `ARC-WORLD-1581 hero-door-trap`, `ARC-WORLD-1668 door-use-script` and the status line. Smoke: the last part of `door-close` makes the closed door trapped (native script 30001 with counters 0) and requires that the hero's use meets the trap and leaves the door shut with no run, and that the next use with the trap gone opens it.

## Disarm (ARC-SKILLS-1156)

The hero's disarm traps press (E6-a second half; ledger [SP3](../research/ledger/technology.md#spot-trap-roll-arc-magic-057), TT5, TT6):

- **Session.** `GameSessionOwner.RollHeroDisarmTrap` takes the trap's difficulty (object field 308 = 0x134, read for a trap object only, and bounded by `MaxTrapDifficulty`) and the light at the trap. A trap that is gone or that the hero does not know is NoChange with nothing charged or rolled. In a round the press pays 4 action points first (`UseSkillCost`, 0x4B7CD0; its own revision; another's subturn or too few points refuse it as `DisarmOutOfTurn`), then the shared check, `TrapRules.DisarmCase` (a failed master that did not fail critically rolls once more; a pass of that roll has no critical) and `DisarmTrap`. The rolls, the hero's fate point and the disarm are one revision; a removal the session cannot write takes the rolls and the fate point back and refuses. Success removes the trap, an expert's critical success also yields the device prototype, a critical failure sets the trap off.
- **Device.** `GameSessionOwner.CreateItemForHero` creates the device as a made item of the hero's pack, or on the hero's tile when the hero cannot take it (weight, Technophobia, no grid room, the host's check): one revision. `AreaUseScriptHost.GiveHeroPrototypeItem` gives it its prototype's weight and scripts.
- **Viewer.** `AreaTraps.Disarm` is the host's press on a row (the trap object's difficulty, the light at the row, the sprung trap's effect through the cast host, the device). The hotkey bar's Disarm traps entry and, since ARC-UI-1113, the skill pane's Disarm Traps button (bar mode 2, [skill pane](spell-cast-host.md#the-skill-pane-in-the-bottom-strip-arc-ui-1113)) arm the skill cursor (interface mode 2, as the pick pocket's, `TerrainViewerAreaDisarm.cs`); a left press on a known trap (a chest, door or scenery under the pixel, else a known trap object on the pressed tile) runs the press, an unknown trap is no target, a right press, a window or another cursor cancels it. The outcome is on the status line. Smoke stage `disarm`.
- **Limits.** The press's 4 action points are not refunded when the roll then fails to run or the removal cannot be written (a full change budget, an exhausted generator); a critical failure on a trap with an ordinary (non-native) script runs that script as a use with the hero as the triggerer. A pack at its item limit leaves the device on the hero's tile.
- **An NPC as the disarmer (ARC-COMBAT-1776).** `GameSessionOwner.RollNpcDisarmTrap(version, user, trapped, baseline, difficulty, light)` ([`GameSessionFollowerSkills.cs`](../../src/Arcanum.Session/GameSessionFollowerSkills.cs)) is the same check, master's second roll, removal, device and critical-failure outcomes (they share `DisarmStep`), with the NPC's own disarm-traps level and training, its own effectiveness facts (**no local-PC term**), its injuries and **no fate** (SC12, SC14). The trap must be known to it by the observer rules of an NPC (TT2, `TrapRules.Known`): `SessionNpcSkillUser.LedByPc` picks `LedNpc` (what the PC spotted or placed) or `FreeNpc` (what the PC did not place); an unknown or absent trap is NoChange with nothing rolled. No action points are charged (the PC's press paid, or the AI's goal does) and no hero is needed. The device an expert's critical success yields and a sprung trap's effect are the caller's (`AreaTraps.Disarm` gives the device to the hero's pack: the follower-hosted press is slice 2 of Follower Skills, #1738). Refused: a user that is the hero, whose sheet or modifiers do not hold or that is dead (`SkillsUnavailable`), an invalid object, difficulty or light (`InvalidTrap`), the version.
- **The chooser of the follower (ARC-COMBAT-1777, 0x57A620 → 0x4C9050).** `GameSessionOwner.QueryDisarmScore(user, trapped, baseline, difficulty, light)` is a user's score for a disarm: `SkillCheckRules.Score` (effectiveness minus difficulty) of the request the disarm would run, the same light and trap-object difficulty terms as `DisarmStep` (they share `WithTrapFacts`), the hero's with its local-PC term (`user` null), an NPC's without; nothing is rolled, charged or written. The viewer's press (`DisarmByFollower`) charges the hero's 4 action points, then `AreaTraps.DisarmByNpc` runs `RollNpcDisarmTrap` for the best follower, a sprung native trap's effect with the follower as the triggerer (`ApplyByCritter`) and an ordinary trap script with it as the triggerer; an expert's device lands in the hero's pack (the original gives it to the user, TT5).
- **Not modelled.** The reach of the press (the original's skill-bar press builds the invocation at once; no walk is read for it), the interface hooks' messages, a PC-placed script trap marker as the target.

## Not modelled here

- The chooser that hands the Disarm Traps press to a better follower (SP3, 0x57A620; the NPC's own check is built, ARC-COMBAT-1776).
- An NPC's or a companion's use of a trapped object other than a door is **not a gap**: the original has no such use (ledger [TT19](../research/ledger/technology.md#trap-state-arc-world-076), ARC-WORLD-1688). The use goals 38 and 81 are built only by the hero's click, and goal 12, the one use goal an NPC runs, ends unless its target is a portal, so the use node's container, creature and bed branches have no NPC caller; what an NPC does with a container is the pickup goal 15/16 (junk piles, [NPC pickup](../research/ledger/npc-ai.md#npc-ground-pickup-built-arc-ai-1309)). The door's own use script and its trap are run by the node since ARC-WORLD-1668. The hero's teleport and the absent conversation of a door's use script are settled by ARC-WORLD-1805 ([Door use node](#door-use-node-arc-world-1581-arc-world-1668), ledger TT20); an NPC's teleport by a script stays open ([#1837](https://github.com/Simarglok/arcanum-steamwoven/issues/1837)).
- The 30002 trap in the viewer (with its shot), an ordinary trap script on a step, and the
  route cost of a known trap (DC8) on the PC's route (the drop map's route has it), the
  attack's trap trigger in the viewer,
  and the drawing of a spotted trap object (the area data resolves no body for a don't-draw
  object yet).
- The shooter list's exact rectangle around the triggerer (TT12); the floating line and the eye candy of a fired run, and the damage effect (TT14),
  are the host's presentation.
- Trap devices placed by the player (ARC-TECHNOLOGY-011/012, ledger TP1–TP8) and the
  multiplayer per-player lists.

## Tests

- `tests/Arcanum.Simulation.Tests/Traps/TrapRulesTests.cs`: every rule above.
- `tests/Arcanum.Session.Tests/TrapStateTests.cs`: baseline, spot marks, use, step, attack
  and ordinary script triggers, disarm outcomes on trapped and trap objects, a trap armed
  again after a removal, a full change budget, snapshot round trip and refusals.
- ARC-SKILLS-1156: `TrapRulesTests` (`DisarmCase`), `TrapStateTests` (the press: unknown or gone trap, one revision, the master's third roll, the trap object's difficulty, the device, a refused removal taking the rolls back, the round's charge; the created device) and `AreaTrapsTests` (the host's press, the difficulty field, the known targets).
- `tests/Arcanum.Simulation.Tests/Traps/TrapEffectRulesTests.cs`: the decoding, the shooter
  search, the damage source and the amount draw.
- `tests/Arcanum.TerrainViewer.Tests/AreaTrapsTests.cs`: a row's baseline, a native trap on a
  use (cancelled script, effect, repeat), a removal emptying the use slot, no cast host, the
  detect flag's spot on a use, an ordinary trap script, the shot and 30007, the walk check
  (roll not ready, detect flag, known traps skipped, destroyed trap objects) and the step
  (stepped over, fired and removed, spotted); `AreaContainerLootTests.cs`: a trap met on the
  use keeps the container shut.
- ARC-WORLD-1581: `AreaDoorHostTests` (the hero's use node charges, then asks the door's trap; a stopping trap ends the run; a refused charge asks none; an NPC opener does not use the hero's port), `AreaTrapsTests` (a trapped door meets the hero on its use and stays shut).
- ARC-WORLD-1668: `AreaDoorHostTests` (the ports are now the use-script ports, `HeroScript` and `OpenerScript`), `AreaTrapsTests` (a door's own use script stops the use only when it skips the default; an ordinary trap script runs after its meeting; an NPC opener is triggerer and extra object; no script or no row stops nothing).
- ARC-WORLD-1469: `AreaTrapsTests` (a free NPC's and a companion's step onto a trap object and use of a trapped one: the triggerer, a known trap, an unknown one with and without a roll or a sheet, the step-over flags, no critter side, who marks it; the AI host's walk ending on the trap's tile and the trap firing once; ARC-WORLD-1582: the walk ending on any trap object not stepped over), `CompanionFollowTests` (a walk ended on a trap tile: goal stopped, step finished, approach resumes, ordered goal ended), `AreaDoorHostTests` (the opener's use node asks the trap), `ExecutorRulesTests` (a step onto a trap ends the walk there).
- `tests/Arcanum.Session.Tests/TrapEffectTests.cs`: victims (square and triggerer), one draw
  per victim, each damage type's slot, the damage source, an object victim, the spell with no
  caster, a damage's spell, the shot with and without a shooter (ARC-TECHNOLOGY-1619: its draws, the spell before the event, the event before the damage, the veto, the lost credit), and 30007.
- ARC-TECHNOLOGY-1619: `tests/Arcanum.TerrainViewer.Tests/AreaTrapShootersTests.cs` (the candidates), `AreaTrapsTests` (a shot met with a host that lists shooters) and `AreaAiObjectAttackerTests` (the attack event of a shooter object).
