# Pick locks runtime (ARC-SKILLS-006)

Status: **Implemented bounded component**, ARC-SKILLS-006 (2026-10-21), task S9 of the skills plan,
on the session's [lock state](lock-state-runtime.md) (ARC-WORLD-050) and the [shared skill check](skill-check-runtime.md). The hero
picks the lock of a portal or a container: the check, unlocking, locking and jamming with their Lock events, the lockpicks'
bonus, the fate, and the 4 action points of the inventory use in a turn-based round. Pure rules:
`PickLockRules` in [`Arcanum.Simulation.Skills`](../../src/Arcanum.Simulation/Skills/PickLockRules.cs); session: `PickLock` in
[`GameSessionPickLock.cs`](../../src/Arcanum.Session/GameSessionPickLock.cs); viewer: **Pick a lock** in the AREA inventory window,
then a press on a door or window in map mode ([doors and windows](../rendering/area-map-mode.md#doors-and-windows-arc-world-051)).
No snapshot schema change: the lock state is map changes, the Lock event is the existing kind.
Requirements: [FR-NAV-007](../requirements/world-travel-runtime.md#fr-nav-007) (lock picking and the access state), [FR-ITEM-004](../requirements/items-economy.md#fr-item-004) (locks), [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019)
(a skill used). [FR-AI-003](../requirements/story-social-ai.md#fr-ai-003) (NPCs and doors), which the plan lists for S9, is
not covered: NPC pickers and the witnesses' reaction are not modelled.

## Evidence

- The retail 1.0.7.4 `Arcanum.exe`, read statically per the [original executable check](../research/original-executable-check.md)
  by two independent readers: rows PL1–PL7 of the [verification ledger](../research/ledger/skills.md#pick-locks-arc-skills-006).
  They agree on every row; no anomaly that needs a rule-mode switch (PL7 confirms the ledger's SK4).
- The check, its difficulty and the fate: [SC1–SC17](../research/ledger/skills.md#skill-check-arc-skills-003), FT6.
  The lock setters and the Lock event: [LK2–LK5](../research/ledger/world.md#doors-containers-and-locks-arc-res-065).
- Installed original (opt-in, [below](#real-data-census)).

## Rules

| Rule | Behaviour (original) | API |
| --- | --- | --- |
| Outcome (PL1) | Only "locked now" is read (LK2). A locked target unlocks on any success (relock an hour later) and jams on a critical failure (relock a day later); an unlocked one locks on a success. | `PickLockRules.Resolve` |
| Witnesses (PL1, PL6) | Called on a locked target after a success and after any failure; never on an unlocked one. | `PickLockOutcome.WitnessesCalled`, `WitnessFailure` |
| Difficulty (PL2, SC9–SC11) | The lock's difficulty (portal field 47, container field 57), halved for a master when positive; minus the lockpicks' bonus as stored; no darkness from expert; nothing for no lockpicks. | `SkillCheckRules.Run` with `TargetLockable`, `LockpickBonus`, `LockDifficulty`, `Light` |
| Lockpicks (PL4) | Generic item flag 0x02, bonus field 0xD4; never used up or destroyed. | `SessionLockpick` |
| Item use (PL3) | The player's only start is lockpicks from the inventory; 4 action points in the hero's subturn of a turn-based round; the check runs at once (no animation). | `PickLock(..., world)` |

`PickLock(version, target, lockpick, world?)` takes a `SessionPickLockTarget` (the object, its T0 lock facts, the lock's difficulty
±1,000 and the light 0..255 at it) and the hero's `SessionLockpick` (or null: the path of a script or a service, which charges
nothing). Refused before any roll or charge: no hero or a dead one (`PickLockUnavailable`), an invalid object, difficulty or light
(`InvalidOpenable`), an item that is not lockpicks or holds values outside the bounds (`InvalidLockpick`), lockpicks with a use
script (`ItemScriptUnsupported`, as the session runs none), lockpicks the hero does not hold or the session deleted
(`ItemNotAvailable`), the version. With lockpicks, a world and the hero's subturn it first checks the attempt's worst case (a first
record of the object's three lock values, room for a Lock event and the hero's fatigue timer, two revisions), then charges 4 action
points (`ConsumeActionPoints`, its own revision). The check, the lock write, its Lock event, the fate and the generator are then one
revision; a failure that changes no lock still spends its draws and a revision. A junk pile has no lock: nothing is written. The
answer carries the check, the outcome, the object's state after it and whether the fate was used.

## Viewer

The AREA inventory window lists **Pick a lock with:** for each lockpicks item in the PC's list (the kit's from the hero load's
prototype fields, a taken item's from its object row). The button closes the window and arms the lockpicks; the next left press on
a door or window in map mode picks its lock, walking within range 1 first (the use-item-with-skill goal's range); a press anywhere
else, a key press, the window's focus loss, a follower walk order, a map change or a reload cancels it. The HUD reports "the lock opens", "locked it", "the lock jams" or "the pick failed". The viewer passes the light at the portal's cell (ARC-WORLD-1149, [FR-LIGHT-011](../rendering/area-lighting.md#light-level-for-checks-arc-world-1149));
in a turn-based round (ARC-COMBAT-020) it passes the round's world, so the pick charges its 4 action points on the hero's subturn.

## An NPC as the user (ARC-COMBAT-1776)

Follower Skills slice 1 (the chooser and the options row are slice 2): `PickLockByNpc(version, user, target, lockpick?)` in
[`GameSessionFollowerSkills.cs`](../../src/Arcanum.Session/GameSessionFollowerSkills.cs) is the same check, outcome, lock write, Lock event and
generator draws as `PickLock` (they share `PickLockStep`), for an NPC that is the user (`SessionNpcSkillUser`: its life target, its map data's
advancement sheet and its effects' modifiers, as the NPC heal's and pick pocket's). It differs as the original does (SC12, SC14, PL4, PL5):

- the NPC's own pick-locks level and training (`QueryNpcSkills`), its own extraordinary intelligence, its injuries from its life record; **no
  local-PC difficulty term** (the Easy and Hard changes of effectiveness are the PC's) and **no fate** (a PC's only);
- its own lockpicks (generic item flag 0x02, the bonus as stored, the condition for a critical failure), or none: a follower, the AI and a script
  pick without them (PL4, PL7); the session does not check that the NPC holds them (the host reads its holder), but refuses a use script, a
  destroyed item and one the hero holds (`ItemNotAvailable`);
- no action points (the PC's use paid them in a round, or the AI goal paid its own) and no hero is needed;
- refused, typed: a user that is the hero, whose sheet or modifiers do not hold or that is dead (`PickLockUnavailable`), an invalid object,
  difficulty or light (`InvalidOpenable`), lockpicks that are none or out of bounds (`InvalidLockpick`), with a use script
  (`ItemScriptUnsupported`), destroyed or the hero's (`ItemNotAvailable`), the version.

The AI host's pick-locks attempt (`AreaAiHost.AttemptPickLocks`, goal 67 node 2, 0x42A4E0) is this call: the lock is what the scene names
(`AreaAiScene.PickLockTarget`, the viewer's: a door or window by its entity, a container by its "inv:" string, at the light of its cell; `AreaPortals.PickTarget`,
`AreaContainerLoot.PickTarget`), the lockpicks are the goal's tool item when the NPC holds them. A scene without the provider, or a target it does not know,
refuses the attempt (the trace says "no lock target") and changes nothing. The answer is carried by the skill notice (`AreaAiNotice.PickLock`).
Disarm Traps by an NPC: [trap state](trap-state-runtime.md).

## Containers in the viewer (ARC-UI-1127)

Armed lockpicks (the item cursor of the lockpicks, interface mode 16) on a container of the map (E8-g): the press starts the same skill goal 33 (skill 14) with the container's loot target string, so the hero walks within range 1, turns and the pick runs at the frame's next safe point. The pick is the session's on the container's lock identity, its T0 baseline and its lock level (field 57, slot 37 as a portal's field 47), at the light of the container's cell (`AreaContainerLoot.Pick`; code `TerrainViewerAreaContainerPick.cs`). Nothing opens: the next press opens the picked container through the window's admission ([lock state](lock-state-runtime.md)). Smoke: stage `item-aim`, step 7 (the nearest locked container of the town sector without an unlock script: the pick result, and the container's open attempt follows it).

## Boundaries (NOT READY)

- **Witnesses** (PL6): the session reports whether the original calls them; nobody acts on it (no NPC sight, hearing, reaction or
  attack is wired to it, the AI hostility slice does not exist yet).
- **Not modelled:** the best-follower substitution and its line (PL5, slice 2 of Follower Skills, #1738), the witnesses and the floating
  text of an NPC's pick, the multiplayer packets, keys (the session holds none, ARC-WORLD-050). The NPC's pick itself is built since
  ARC-COMBAT-1776 ([above](#an-npc-as-the-user-arc-combat-1776)); the timed pick-lock goal's animation loops are the AI host's (ARC-AI-1524).

## Real-data census

- `LockpickRealDataTests` (Data, `DAT_ROOT`), first observation 2026-10-21: three lockpick prototypes (15101 bonus 10, 15104
  bonus 5, 15198 bonus 0, no scripts); three placed (15104) on the 80 interiors; 398 of 519 portals and 442 containers locked; lock
  difficulties 0..1,000 (635 of them 0).
- `AreaPickLockTests` (viewer, `DAT_ROOT`): only the halfling-male kit holds lockpicks; a pre-made hero picks the first locked
  door with a difficulty on the interiors through the viewer's portal path.

## Checks

`tests/Arcanum.Simulation.Tests/Skills/PickLockRulesTests.cs`, `tests/Arcanum.Session.Tests/PickLockStateTests.cs` (unlock and
relock, lock, jam and its day relock, fate without lockpicks, junk pile, refusals, the turn-based charge),
`tests/Arcanum.Session.Tests/FollowerSkillsStateTests.cs` (an NPC as the user of a pick and of a disarm), `tests/Arcanum.TerrainViewer.Tests/AreaAiSkillTests.cs` (the AI host's attempt),
`tests/Arcanum.TerrainViewer.Tests/AreaPickLockTests.cs`, the viewer self-test (the pick button) and the native smoke
(`ARC-SKILLS-006 native-smoke pick PASS` on a map with a locked door and the halfling-male hero).

ARC-INPUT-1123: the start of the armed lockpicks asks the 4 action points of the item cursor (IA4) before mode 16 opens, as the original does; with no points left in a
turn-based round on the hero's own subturn the start is refused (the window is closed, no cursor), and the same smoke step checks it
(`ARC-INPUT-1123 native-smoke lockpick-ap PASS`). See [interface modes](../architecture/interface-modes.md#action-points-at-the-start-of-a-target-mode-arc-input-1123).
