# Trap state (ARC-WORLD-076)

Status: **Implemented** (Godot-free rules and session owner), slices 1 to 3 of the trap domain
([FR-NAV-006](../requirements/world-travel-runtime.md#fr-nav-006)). The effects of the other
native trap scripts are applied since ARC-WORLD-107 ([Effects](#effects-arc-world-107)); the
AREA viewer's PC meets traps on a use, before each step and on a step since ARC-WORLD-110
([Viewer](#viewer-arc-world-110)). The spot and disarm rolls (skills plan S10), the shot of
script 30002, the drawing of a spotted trap object, the attack's trap trigger in the viewer,
NPC and companion steps and the trap devices (ARC-TECHNOLOGY-011/012) are later slices. The rules
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
object's entity as a map change (bit 1 spotted, bit 2 removed, and from bit 8 the script
number the removal cleared), which the snapshot already carries, so the domain adds **no
schema**. A removal holds only while the baseline names the script it cleared, so a trap
armed again on the object later (a trap device on an object with an empty use slot, TP2) is
seen; the spotted bit stays through a removal, as the original keeps object flag 0x2000000
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
- **Shot** (30002): no shooter, no effect at all (no spell either); with a shooter the
  result is `ShotNotReady` and nothing is applied yet: the shot (the dispatcher 0x4B3170
  with a non-critter attacker, TT15), its draw and its spell are a later slice together. The
  host lists the shooter candidates around the triggerer; without a triggerer it lists none.
- **Spell**: 30000, and any damage with a top byte (a shot's comes with the shot slice), starts the spell at the victim
  with no parent and no source (TT16); its BEGIN comes as an event of the host's pass. An
  unknown spell number casts nothing.

The result (`SessionTrapEffectResult`) names the effect, the damage source and each victim's
outcome, amount, damage step, hit points taken, shooter and spell run. The floating line
`mes\trap.mes` 1 over the triggerer (`TrapEffectRules.FiredLine`) and the eye candy lines of
the run are the host's. The effects add no session state of their own: the life, object and
spell run owners record them.

## Viewer (ARC-WORLD-110)

The AREA viewer's trap host (`tools/terrain-viewer/Core/AreaTraps.cs`) meets the shown map's
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
  marked, the step is refused (`TRAP_SPOTTED`) and the walk ends, as the walk goal's
  next-step check leads to its end node (ledger [EX20](../research/ledger/npc-ai.md)). The
  check comes before the turn-based step charge, as the walk goal's check (0x4270B0) runs
  before its step start (0x42E9B0).
- **Step.** Each step the PC starts onto a tile meets the first trap object there
  (`TriggerTrap(Step, …)`, 0x4305D0, TT9): the PC's spell flag 0x2 or 0x4 steps over it, a
  known trap fires too; a trap spotted or fired ends the walk on that tile.
- **Spot.** The spot trap roll is not made (S10): an unknown trap is spotted only when the
  player's spell flags hold 0x100 (TT3, the gate `Detected`); a gate that needs the roll
  spots nothing, and the walk check logs it.
- **Effects.** A fired native run goes to `SessionSpellCaster.ApplyTrapEffect` with the cast
  map's id of the trapped object and the hero as the triggerer. Without a cast host no trap
  is met at all (a use goes on as before and the dispatcher stops at a native trap script; a
  step meets nothing), so no trap is consumed without its effect. A 30002 trap fires in the
  session but its shot is not applied (`NotReady`). The floating line, the eye candy and the
  damage effect are not shown; the status line names the outcome and the
  `mes\trap.mes` line number.

## Not modelled here

- The spot trap and disarm traps rolls, the spot modifier of the item at inventory
  location 1005 and the master's second roll (S10).
- The route cost of a known trap (DC8) on the PC's route (the drop map's route has it), the
  steps of NPCs and companions onto trap objects, the attack's trap trigger in the viewer,
  and the drawing of a spotted trap object (the area data resolves no body for a don't-draw
  object yet).
- The shot of script 30002 (TT15) with its draw and its spell (a found shooter applies
  nothing yet), and the shooter list's rectangle around the triggerer (TT12); the floating line and the eye candy of a fired run, and the damage effect (TT14),
  are the host's presentation.
- Trap devices placed by the player (ARC-TECHNOLOGY-011/012, ledger TP1–TP8) and the
  multiplayer per-player lists.

## Tests

- `tests/Arcanum.Simulation.Tests/Traps/TrapRulesTests.cs`: every rule above.
- `tests/Arcanum.Session.Tests/TrapStateTests.cs`: baseline, spot marks, use, step, attack
  and ordinary script triggers, disarm outcomes on trapped and trap objects, a trap armed
  again after a removal, a full change budget, snapshot round trip and refusals.
- `tests/Arcanum.Simulation.Tests/Traps/TrapEffectRulesTests.cs`: the decoding, the shooter
  search, the damage source and the amount draw.
- `tests/Arcanum.TerrainViewer.Tests/AreaTrapsTests.cs`: a row's baseline, a native trap on a
  use (cancelled script, effect, repeat), a removal emptying the use slot, no cast host, the
  detect flag's spot on a use, an ordinary trap script, the shot and 30007, the walk check
  (roll not ready, detect flag, known traps skipped, destroyed trap objects) and the step
  (stepped over, fired and removed, spotted); `AreaContainerLootTests.cs`: a trap met on the
  use keeps the container shut.
- `tests/Arcanum.Session.Tests/TrapEffectTests.cs`: victims (square and triggerer), one draw
  per victim, each damage type's slot, the damage source, an object victim, the spell with no
  caster, a damage's spell, the shot with and without a shooter, and 30007.
