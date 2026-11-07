# Lock state of portals and containers (ARC-WORLD-050)

Status: **Implemented** (Godot-free rules and session owner). Callers: doors and windows at
runtime in the viewer's map mode since ARC-WORLD-051
([doors and windows](../rendering/area-map-mode.md#doors-and-windows-arc-world-051)) and the
pick-locks skill since ARC-SKILLS-006 ([pick locks](pick-locks-runtime.md)).
The rules follow the retail 1.0.7.4 executable as read in
[ledger LK1–LK13](../research/ledger/world.md#doors-containers-and-locks-arc-res-065).

## Rules (`Arcanum.Simulation.Locks`)

`LockRules` works on an object's lock flags: portal field 46 or container field 56 (the
project's record slot 36 for both), with the same bits for both kinds. The bits are:

- locked 0x1, jammed 0x2, magically held 0x4;
- never locked 0x8, always locked 0x10;
- locked by day 0x20, locked by night 0x40;
- busted 0x80.

| Method | Original | What it does |
| --- | --- | --- |
| `IsLocked(flags, hour)` | 0x441D40 | False for a busted or never-locked object. "Locked by day" holds only in hours 7..21 and "locked by night" only outside them. Otherwise the locked bit decides. Always locked is not read. |
| `SetLocked(flags, locked, hour)` | 0x441DD0 | Writes the locked bit. Unlocking a locked object schedules the Lock event one game hour later. |
| `SetJammed(flags, jammed, hour)` | 0x441F10 | Writes the jammed bit. Jamming an object that was not jammed schedules the Lock event one game day later. |
| `AfterReset(flags)` | 0x441E90 (flags) | The flags after a Lock event: locked and not jammed. |
| `KeyFits(lockKey, itemKey)` | 0x463340 | 0 never fits. A lock of 1 takes any key and a key of 1 opens any lock. Otherwise the two ids must be equal. |
| `Attempt(input)` | 0x4AEB70, 0x4AED80 | The open attempt, checked in the original's order. For a portal: busted, not a critter, unlock script, jammed, magically held, then (unless always locked) an NPC without a PC leader or a step leaving a building, then the lock and the party's keys. A container has no walker test and no exemptions, and a junk pile always opens. |

`PortalFrameRules` covers the open and close steps of 0x4F0950 and 0x4F0A90 and the toggle
of 0x4F08F0:

- A door whose even rotation is 0 or 6 uses frames 4..6, one of 2 or 4 uses frames 1..3. An
  odd rotation first drops by one.
- A window uses frames 0 and 1.
- An ART with a single frame never moves.
- The first opening step sets the open object flags: 0x430 for a door, 0x420 for a window.
  The last closing step clears them. Callers derive the flags from the frame.

## Session owner (`GameSessionOwner`, GameSessionLocks.cs)

The host passes each object as a `SessionOpenable`: its map and its session entity id (for a
static portal, the sector id and source index of `AreaUseScripts.EntityId`). It also passes the
object's T0 facts as a `SessionOpenableBaseline`: its kind, flags, lock key and ART frame, and
whether it is a junk pile.

- **State.** `QueryOpenable` returns the recorded state, or the baseline when nothing is
  recorded, together with the lock at the session hour (never locked for a junk pile). A
  recorded value out of range (a kind other than portal or container, a frame outside 0..31)
  counts as not recorded.
- **Lock, jam and frame writes.** `SetOpenableLocked`, `SetOpenableJammed` and
  `SetOpenableFrame` each add one revision. The first write records three map changes of the
  entity: `lock.kind`, `lock.flags` and `lock.frame`. The snapshot already carries map changes,
  so the domain adds no schema version. A junk pile has no lock, so a lock or jam write to it
  changes nothing.
- **Lock event.** It is the `lock.reset` event kind, game time and saved. It is scoped to the
  object's map, so it waits while another map is active and fires on return, as the original's
  parked events do. Any number can be pending, at most 1,024 (project policy). The registry
  version stays 1, as when the script call and effect END kinds were added.
- **When the event fires.** It locks the object, clears its jam and closes it. A portal goes to
  frame 0. A container goes to frame 0 only when its frame is exactly 1. The pass lists each
  firing in `SessionEventPassResult.Locks`. A host that checks objects (`ISessionScriptWorld`)
  decides whether the object still exists; otherwise it exists while its state is recorded,
  and an event whose object has no recorded state is dropped. A restore refuses a Lock event
  whose object has no recorded state or whose delay is neither an hour nor a day.
- **Open attempt.** `AttemptOpen` runs the player's open attempt with the current flags and
  hour and the hero's key list. The host supplies the unlock script's result and the leaving test.

## Limits (NOT READY)

- **Keys.** Since ARC-WORLD-123 `AttemptOpen` passes the hero's key list (`QueryHeroKeys`, ledger LK9, KY4), so a lock whose key
  the list holds opens (the match rule is `KeyFits`). The list is filled by `AddHeroKey` and by the hero taking a key item
  ([inventory](inventory-runtime.md#key-items-arc-world-123)). NOT READY: the key-ring item and a key that stays loose, the
  followers' keys (the original also searches them) and any session without a hero (it holds no keys).
- **Bust and magic.** `SetOpenableBusted` (ARC-WORLD-051) sets the busted bit on frame 0, as the
  original busts a window; its busted ART (bit 0x200) is not stored. A door the original destroys instead
  is destroyed in the [object damage](object-damage-runtime.md) domain (ARC-COMBAT-008, condition `Destroyed`),
  which also busts a window through the same bit when an attack breaks it. Magically held writes have no caller yet.
- **Not modelled.** NPC open attempts (an NPC walker), the multiplayer key branch and the
  bust script (SAP 30).
