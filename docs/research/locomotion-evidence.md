# Running, the walk/run toggle and jumping through windows

## Status, scope and evidence discipline

ARC-RES-047 answers how the original moves a critter at a walk or a run (speed,
fatigue in and out of combat, the toggle and its keys) and how a walker passes
through a window (which edges, which actors, the animation, the route cost). It
covers FR-SCENE-016 and FR-NAV-003 for windows and the `NumLock`/Always Run row of
the [keyboard requirements](../requirements/original-keyboard-mouse.md). It is
**research, not a runtime**: only this document, a status row and the probe mode
below change. Doors and window *passability* at T0 are already Data evidence
([ARC-WORLD-018](../world/area-step-assessment.md#whole-maps-arc-world-018)); this
note adds what happens when the walker actually crosses.

- **Source (S):** an earlier source reading (provenance in git history), cited by the executable address of its function; level F0 (not checked in the executable) unless a ledger row is cited or an **O** entry says
  otherwise. The probe restates the rules in
  its own form.
- **Original (O):** read-only disassembly of the GOG 1.0.7.4 `Arcanum.exe` at the
  addresses given (method of ARC-COMBAT-001, Xcode `llvm-objdump`). Only
  function shapes and constants are compared; no machine code is published.
- **Data (D):** the six pinned archives, the normal-game exterior aggregate and the
  prototype directory, read by the probe.
- **Proposal (P)** and **Unknown (U)** as in the other research notes.

## Answer

- A step is a walk or a run. Running is a flag on the move, chosen per click: the
  **Always Run** option (a settings value, not save state), **Num Lock** and **Ctrl**
  decide it for a ground click (L1–L3); a click on an object always runs (L4).
  Running needs current fatigue above 0, encumbrance below *significant* (under 60 %
  of the carry weight) and no concealment (unless Prowling master) (L5).
- A run covers a tile about **3.3× faster** than a walk for the viewer's human body:
  at Speed 8, 547 ms per axis step walking and 168 ms running; 773 / 235 ms per
  diagonal step (L6–L8). Speed changes the frame rate, not the stride.
- Out of combat, running costs nothing. In a turn-based round each run step costs
  1 AP instead of 2; in combat mode (real time too) every fifth run step costs 1
  fatigue (L9). Fatigue drain over
  time comes only from heavy encumbrance, which already forbids running.
- A window is a **wall piece** (10, 13, 14, 17–19) on a tile edge. A humanoid (or
  orc) walker crosses it on an axis step with its own **vault** animation (ART
  animation 13, 9 frames at 8 fps, 1125 ms; the tile changes at the action frame).
  A window portal on that tile must pass the walker's open attempt, as a door does:
  jammed, magically held or locked windows stop it (W1–W6). Diagonal steps through a
  window are always blocked; the route search allows a window edge at 6× the cost of a
  plain step (W7).
- On the start map 324 of 1138 window edges are jumpable by day; 760 are jammed,
  including all four windows of the [first house](first-building-evidence.md). The
  nearest jumpable window is 2114 tiles (Chebyshev) from the start point, outside the 9 core
  sectors the viewer admits (D3).

## Inputs and reproduction

Inputs are those of the [first building research](first-building-evidence.md)
without `--game-root`: the six archives (module, resource1..4, PATCH0), the
exterior aggregate (`a548f611…de6a5`, 1,823,280 bytes) and the prototype
directory. Archives, the aggregate and the 16 prototype files read are fingerprinted
before and after the run; sectors must come from the module only and every ART must
resolve in exactly one archive.

```sh
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --locomotion-self-test
"$DOTNET" run --project tools/research/Arcanum.CrashSiteProbe --no-build -- --locomotion-research \
  --prototype-root "$GAME/data/proto" --mobile "$GAME/modules/Arcanum/maps/<exterior aggregate>" \
  --module "$GAME/modules/Arcanum.dat" --resource1 "$GAME/arcanum1.dat" --resource2 "$GAME/arcanum2.dat" \
  --resource3 "$GAME/arcanum3.dat" --resource4 "$GAME/arcanum4.dat" --patch0 "$GAME/modules/Arcanum.PATCH0" \
  > "$PRIVATE/locomotion.json"
```

The report holds numbers, booleans and SHA-256 digests only. The current report
digest is not recorded here; earlier digests are in git history. The self-test
covers the original's frame-rate formula at the Speed boundaries (walk, run and
stealth), the frame clamp, tile step vectors, the cycle projection, window
admission order, the original's leaving test from the portal's tile and from
across, and the ART animation letter.

## Walk and run (S, O, D)

| Id | Fact | Evidence |
| --- | --- | --- |
| L1 | **Always Run** is a settings key (`always run`, default 0) shown in the options menu; it is not part of a save. | S settings register (0x421B00), options row |
| L2 | Effective always-run for the local PC: option on → run unless Ctrl is held; option off → run while Ctrl is held **or Num Lock is on**. O: the original reads Num Lock as a toggled state seeded from `GetKeyState(VK_NUMLOCK)` and flipped on each press, so Num Lock on means "always run" while the option is off. This answers the open `NumLock` row of the keyboard requirements: Num Lock does not write the option. | S get_always_run (0x4304C0, 0x430580); O `0x4304C0`, `0x52B350`, `0x52B580` |
| L3 | Ground click: with Ctrl (or the Num Lock key physically held) and the option off the PC runs to the tile; otherwise it moves to the tile, and that move runs when L2 says always-run and encumbrance is below *significant*. So Ctrl is the "run this once" modifier, and with Always Run on Ctrl makes a walk. The original tests the held key here; through L2 the effect is the same as a test of the toggled Num Lock state. | S tile click (0x54DE50), move to tile (0x433640); O `0x54E650` |
| L4 | Clicking an object (approach to use, talk, pick up, attack) always turns running on, whatever Ctrl, Num Lock and the option say (the inner Ctrl tests are dead after the Ctrl branch); only Shift sets a different goal flag instead. Running is still subject to L5. | S object click (0x54ED30); O `0x54FB19`, `0x54FC0D` → `0x436AB0` |
| L5 | Running is granted only below encumbrance *significant*: the level is the first whose share of the carry weight the load stays under (15/30/45/60/75/90/100 %), so *significant* starts at 60 % and running needs a load under 60 %. When the step begins, it falls back to a walk if current fatigue is ≤ 0 or the walker is concealed without Prowling master (stealth walk below Prowling expert). | S turn_on_running (0x436AB0), step animation (0x42EDC0), encumbrance, level (0x45F790) |
| L6 | Frame rate by Speed for a human (dwarf and halfling add 4 to each value): walk middle/low/high 17/6/30, run 20/8/30, stealth 10/4/16. Speed < 8: low + Speed·(middle−low)/8; Speed = 8: middle; 8 < Speed < 30: middle + Speed·(high−middle)/30; ≥ 30: high. Divisions truncate. Frame time = 1000/fps ms, clamped 30..800. Non-critter ART and the body-of-element spells keep the ART rate. | S rate (0x437990), frame time (0x42EE90); O `0x437990` |
| L7 | The stride is the ART: each frame adds its offset to the walker's screen offset, and the tile changes when the offset passes the step. Human walk (resource1 ordinal 2668) 10 frames, sum per cycle (±42, ±22) on an axis rotation = 1.06 tiles, (±60, 0)/(0, ±30) on a diagonal = 0.75 tiles; run (ordinal 2671) 10 frames, (±119, ±60) = 2.98 tiles and (±170, 0)/(0, ±85) = 2.125 tiles. Both ARTs carry their middle rate (17 and 20 fps) and action frame 9. | D; S [frame offsets][grid-s4] |
| L8 | Time per tile (Speed 8, the viewer's human): walk 58 ms frames, 547 ms axis / 773 ms diagonal; run 50 ms frames, 168 / 235 ms. Speed 9 (the half-elf pre-made hero with DX 9, [starting hero](starting-hero-evidence.md); Speed = DX, +5 when extraordinary, stat (0x4B0740)): walk 472 / 667, run 144 / 202. Before ARC-WORLD-020 the viewer used a flat 500 ms step. | D (probe `timing`) |
| L9 | AP per step in a **turn-based round**: walk 2, run 1 (needs fatigue > 0); outside a round the AP charge always passes. In **combat mode** (real time included) a run step adds 1 to a per-goal counter and every 5th run step (20th for fatigue-limiting critters, every step for fatigue-draining ones: 5/4 rounds to 1) charges **1 fatigue damage**; if that write fails the step is refused. Outside combat mode running is free. The TB move estimate counts 1 AP per step for a PC with always-run. | S step AP (0x430FC0), move cost (0x4B7BA0); O `0x430FC0` (the original adds 1; ledger LA2, combat anomaly A4) |
| L10 | Fatigue drain on a timer only starts at encumbrance *significant* (every 600 s game time), *heavy* (60 s) and *severe* (5 s). Running is already refused there (L5). Recovery is every 80 game seconds ([combat research](combat-core-evidence.md)). | S fatigue delay |
| L11 | Followers run when farther than their follow range + 2 ([companion research](companion-follow-evidence.md)). | S (earlier research) |

## Windows (S, O, D)

| Id | Fact | Evidence |
| --- | --- | --- |
| W1 | A window is a **wall** whose piece is 10, 13, 14, 17, 18 or 19 on the step's edge, on either the tile being left or the tile entered (facing back). In the move's window check, pieces 9, 11, 12, 15, 16, 20 on that edge stop it, and a window piece on the tile being left decides before the far tile is read; in traversal every non-opening piece blocks. (The probe counts a stop piece on either side; the start map has none.) | S window edge (0x4273B0), traversal (0x43FE00) |
| W2 | Only critter ART (humanoids; not unique-NPC ART) and orc monsters can jump; others treat a window piece as a wall, as does a polymorphed critter. | S can jump (0x45F570), walker flags (0x4257E0); O `0x45F570` |
| W3 | A diagonal step across a window piece is always blocked. An axis step is allowed and marked as a window; a window **portal** on that edge must be busted or pass the walker's open attempt, with the door rules of ARC-WORLD-018 (unlock script, jammed, magically held, leaving a building, day/night locks, key). The jump goal repeats that attempt whether or not the window is open, with the direction from the walker to the portal: rotation 5 when the walker stands on the portal's tile. **Original (ARC-RES-049):** the step's traversal attempt passes the step's edge and the goal the walker's direction; the leaving test really exchanges its two tiles for a walker from across, and on the portal's own tile it tests a portal of raw rotation 0, 1 or 2 in reverse, so the two attempts never agree there. Leaving passes from an indoor portal tile for rotations 3..7 when the tile along the rotation is not indoor, and from across when that tile is indoor and the portal's tile is not (for an odd rotation, the tile across the edge) ([ledger LV1–LV5](ledger/world.md#leaving-a-building-through-a-locked-portal-arc-res-049)). | S traversal (0x43FE00), traversal far side (0x43FE00), open attempt (0x4AEB70), jump check (0x4288A0), direction (0x4B8D50) |
| W4 | When the walker reaches a window edge, its move pushes a **jump** goal (O: same goal table at `0x5A73D0`/`0x5A63D0`). The jump takes the first portal on the window's tile; if that portal is closed the walker first opens it, as a door. It then plays animation 13 (**vault**) facing the step, one frame per 1000/fps ms (no Speed scaling). At the action frame the location moves one tile through the window and the screen offset is shifted back by one step, so the sprite does not jump. After the last frame the walker resumes its gait (run or walk) and continues its path; at the path's end it stands. | S jump goal, attempt move, begin (0x42D7D0), update (0x42D910), tile shift (0x42F000), continue (0x42F140) |
| W5 | Human vault ART (resource1 ordinal 2677): 9 frames, 8 fps, action frame 2, so 1125 ms; frame offsets add (±30, ±15) on an axis rotation (0.75 tile). | D |
| W6 | AP: the traced jump charges 2 AP only to an NPC in a round when the vault cannot begin; other round costs were not traced (U2). | S AP (0x433270) |
| W7 | Route search: the straight attempt stops at a window; the best-first search allows the window edge at cost +50 on top of the step's 10. Walkers that cannot jump never route through. | S straight (0x41F840), search (0x41F9F0) |

## Start-map census (D)

| Id | Fact |
| --- | --- |
| D1 | 621 sectors, 28,064 walls, 1,588 portals (978 windows). 1,138 window pieces, each on its own edge (none faces another window piece). The exterior aggregate carries no walls or portals. |
| D2 | 978 window edges have one portal on the window's tile, on the same edge; 160 have none (empty frames). Flag combinations of those 978 portals: jammed 748, jammed and locked 12, locked 60, locked and never-locked 88 (plus 1 also locked at night), locked and always-locked 33, locked at night 21, locked and magically held 3, busted 12 (9 of them also jammed). |
| D3 | Jumpable by day (hour 15) from at least one side: **324** edges in 67 sectors (160 empty frames, 12 busted, the rest unlocked or leaving a building). From the window's own tile: leaving 135, unlocked 17, locked 51; from across: unlocked 110, locked 93; jammed 760 and magically held 3 either way. The 18 rotation-1 windows a door's leaving rule would open are refused (W3: the traversal calls them leaving, the jump goal does not). ARC-RES-049 recounted the census with the original's leaving test: unchanged (no locked window stands on an outdoor tile with an indoor tile across, so leaving from across adds none). The nearest jumpable window is (92824, 84706), an empty frame 2114 tiles (Chebyshev) from the start point; the next four are 2642..2690 tiles away. |
| D4 | The [first house](first-building-evidence.md) at sector (1451, 1302) has four window edges, all jammed: not jumpable. |
| D5 | ARC-RES-049 (report field `locked_doors`): 215 closed locked doors the leaving shortcut can apply to (no unlock script; not busted, jammed, held, never- or always-locked), all of odd rotation (1: 24, 3: 101, 5: 50, 7: 40). With the original's test 150 open when leaving from the portal's tile (the same as the earlier reading), none from across, and 4 of rotation 1 on an indoor tile with outdoor beyond stay shut. |

## Anomalies

**Original check (ARC-RES-048):** LA1–LA3 are confirmed. For the leaving test the original swaps the portal and far tiles, so a walker from across can count as leaving (ledger LA4 (c)); W3's earlier "never from across" was wrong. ARC-RES-049 settled what that means for a step (W3) and recounted D3: the numbers do not change. The verdicts in the [verification ledger](ledger/world.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock) are authoritative.

- **LA1** At Speed 8 the original returns the middle value (O `0x437A5C`, ledger
  LA1). For the viewer's human that equals the ART's own rate (17 and 20 fps); the
  earlier reading differed only for other bodies. Proposal: follow the original.
- **LA2** = combat **A4**: in combat mode the original charges 1 fatigue on every
  fifth run step (O, also checked in ARC-COMBAT-001; ledger LA2). Proposal: follow
  the original.
- **LA3** An object approach runs whatever Ctrl, Always Run and Num Lock say (L4).
  This is the original's behaviour (ledger LA3); kept as is.
- **LA4** The jump's open attempt looks from the walker's own tile with rotation 5
  (W3), so a rotation-1 window is never "leaving" from inside (ledger LA4 (a, b));
  kept. The door goal uses the same direction call, so ARC-WORLD-018's door
  admission may differ from the original on the portal's own tile: a follow-up
  check. *Answered by ARC-RES-049:* from inside, rotation 0..2 never leaves
  (the traversal and the goal disagree); from across, the original leaves (ledger
  LV1–LV5); the step evidence follows the original.

## Proposed implementation slices (P)

1. **Run gait (`ARC-WORLD-020`).** A per-step duration from the gait (L6–L8: frames ×
   frame time ÷ tiles per cycle, axis and diagonal), chosen by the host when it admits
   the step, so the Simulation contract does not change; the step's permit names its
   gait for the presentation. Viewer: Always Run option, Num Lock and Ctrl on ground
   clicks (L2–L3), object approaches running (L4), run ART frames, followers running
   (L11). Fatigue and encumbrance are not modelled yet, so running is never refused.
   No save schema change: Always Run is a viewer setting.
2. **Window jump (`ARC-WORLD-021`).** Data: the `WINDOW_JUMP_UNMODELLED` step of
   whole-map evidence becomes an admitted axis step marked as a jump (W1–W3), for a
   walker that can jump. Route planner: window edge cost (W7). Simulation/viewer: vault
   playback (W4–W5) with the tile change at the action frame. Synthetic fixtures; a
   real-data check needs a map with a jumpable window (D3), not the Crash Site core.
3. **Combat costs** (with the combat round slices): AP 1/2 per step and the fatigue
   counter (L9), when turn-based movement exists.

## Unknowns

- **U1** How `object_inc_current_aid` and the per-rotation offset sign test round a
  stride that is not a whole number of tiles (walk 1.06): the project keeps an
  average time per tile, not per-frame parity.
- **U2** Turn-based cost of a window jump beyond W6.
- **U3** Windows inside interior maps are not counted here. ARC-WORLD-021 found 22
  directed window-jump steps out of the cells reachable from the starts of two of the
  54 complete interiors.
- **U4** Shift-click goal flag (L4) and the multiplayer always-run flag are not
  described.
- **U5** The keyboard requirements' `NumLock` rows are answered by L2; the
  requirement text is updated by the implementation slice.

[grid-s4]: grid-movement-evidence.md
