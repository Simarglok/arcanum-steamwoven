# Trap state (ARC-WORLD-076)

Status: **Implemented** (Godot-free rules and session owner), slice 1 of the trap domain
([FR-NAV-006](../requirements/world-travel-runtime.md#fr-nav-006)). No caller yet: the
viewer's container window still refuses a trapped container, the walk does not look for
traps, and the spot and disarm rolls (skills plan S10), the effects of the native trap
scripts and the trap devices (ARC-TECHNOLOGY-011/012) are later slices. The rules follow
the retail 1.0.7.4 executable as read in
[ledger TT1–TT10](../research/ledger/technology.md#trap-state-arc-world-076).

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
| `NativeRun(script, headerFlags)` | 0x4BCB70 (frame) | Null when the trap code does not take the script. 30007 is taken and does nothing. 30000 casts the spell in the counters' top byte, 30001..30006 build an attack; the victims are every PC and NPC in the square of half side 4, 2 or 1 (file flag 0x80, 0x40, 0x20) around the trapped object, or the triggerer alone; header flag 0x4 clears the script's own number. Also the eye candy lines of `Rules\TrapEyeCandy.mes` (3k played on the triggerer, 3k + 1 on each victim, 3k + 2 the spotted mark stopped on the trapped object, k = script − 30000; a spot mark plays 3k + 2 on a trapped object with a native script and a removal stops it). |
| `StepTriggers(moverSpellFlags)` | 0x4305D0 (part) | A mover with spell flag 0x2 or 0x4 (field 0x14) sets off no trap object. |

## Session owner (`GameSessionOwner`)

A trap's state is its T0 baseline (`SessionTrapBaseline`: carrier, use script, header
flags, counters and object flags, which the host reads from the map data and the script
file) until a spot mark or a removal. Each write records the property `trap.state` of the
object's entity (bit 1 spotted, bit 2 removed) as a map change, which the snapshot already
carries, so the domain adds **no schema**. A removed trap object is also recorded destroyed
(`object.condition`), so the object damage domain sees it gone. A host that reads the
object's use script must take the trap state's script (0 once removed).

| Method | What it does |
| --- | --- |
| `QueryTrap` | The current class, script, object flags (with the spotted mark) and whether the trap was removed. |
| `TrapKnownTo`, `TrapSpotGate` | `Known` and `SpotGate` on the current state. |
| `MarkTrapSpotted` | Records the spotted mark for the player or an NPC led by a PC; NoChange otherwise or when marked already. |
| `TriggerTrap(cause, spottedNow, moverSpellFlags)` | The meeting of a triggerer with the trap. `Step` first checks `StepTriggers`. `Use` and `Step` go through the object entry point: a triggerer that does not know the trap and spots it now (`spottedNow`, the caller's gate and roll) marks it (on the PC's side) and the use is cancelled (`Spotted`); otherwise the trap fires (`Fired`) with its native run, and an auto-removing run removes it. `Attack` (the damage step's direct trigger 0x4BCB00) fires with no spot and never removes. An ordinary trap script fires with a null run: the host runs it through the dispatcher. |
| `DisarmTrap(success, critical, training)` | `Disarm` on the current state: removal, the device prototype (the caller creates the item in the disarmer's inventory), or a sprung trap fired as a use. |

Every write is one revision; a bad object, carrier, script or cause is refused with
`InvalidTrap`, an old version with `StaleVersion`.

## Not modelled here

- The spot trap and disarm traps rolls, the spot modifier of the item at inventory
  location 1005 and the master's second roll (S10).
- The walk's spot before each step (0x427110, ledger TT10) and the route cost of a known
  trap (DC8): the walk is the host's.
- The effects of the native trap scripts (0x4BCDC0: the spell and the attacks), their
  messages and eye candy.
- Trap devices placed by the player (ARC-TECHNOLOGY-011/012, ledger TP1–TP8) and the
  multiplayer per-player lists.

## Tests

- `tests/Arcanum.Simulation.Tests/Traps/TrapRulesTests.cs`: every rule above.
- `tests/Arcanum.Session.Tests/TrapStateTests.cs`: baseline, spot marks, use, step, attack
  and ordinary script triggers, disarm outcomes, snapshot round trip and refusals.
