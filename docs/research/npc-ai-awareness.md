# NPC AI awareness

Status: **Research**, work item ARC-RES-056 (2026-10-20), stage A2 of the
[NPC combat AI plan](npc-combat-ai-plan.md#phase-1-faithful-reconstruction)
(source units S6–S9). It fixes, as pure functions with integer arithmetic, when
a critter sees or hears another, how the straight line between two tiles is
priced for sound and for missiles (the line-of-fire test of combat open
question O4, with its 26 threshold and cover rating), how an attack alerts the
NPCs around it and what each witness does, and what arming dynamite does to the
NPCs that notice. It changes no code. The rules are inputs for implementation
slice I2 (awareness) of the plan and for the combat difficulty term
FR-COMBAT-007. The prowling margin (skill effectiveness, the opposed table and
the prowling difficulty) is documented here as the executable computes it, but
the shared skill check ARC-SKILLS-003 will own it: the AI code takes the margin
as an input and does not compute it.

Earlier facts reused here: the [core loop](npc-ai-core-loop.md) (ARC-RES-054:
the danger states and their setter 0x4AABE0, parameter slots), the
[skills study](skills-evidence.md) (ARC-RES-044: prowling, the opposed table,
the difficulty terms; effectiveness row SKX1 of the
[ledger](ledger/character.md#skills-levels-and-character-arc-res-044-arc-res-031-arc-res-027)),
the [area passability notes](sector-passability-evidence.md) (the shared step
cost function 0x43FDC0/0x43FE00, F0 until now) and the
[script census](scr-census.md) (conditions 28 and 29).

## Method

Behaviour comes from the retail executable (`Arcanum.exe` 1.0.7.4, SHA-256
`9bd80511…78d52`), read statically with `llvm-objdump` by a first reader and,
independently, by a second reader that saw only the questions
([method step 6](original-executable-check.md)); the second reader was asked
once to re-check one point, the sign of the concealment margin, as the ledger
intro records. Object fields were named from
the executable's own field table (0x59BEA8); flag bits, skill, stat and
social-class numbers are the project's records of the same constants. The rows
and verdicts are in the [ledger](ledger/npc-ai.md#npc-ai-awareness-arc-res-056).
Everything below is read from the executable unless marked **(inference)**.
Rules owned by other stages (protect, the attacked reaction, the target scan)
are named with their address and stage and only their inputs are stated here.

Conventions: "/" is signed 32-bit division truncating toward zero; distance is
the Chebyshev tile distance `max(|dx|, |dy|)` of `location_dist` (0x4B96F0)
between the two objects' tiles; loudness is an index 0 = silent, 1 = normal,
2 = loud (the weapon rule in [section 6](#6-noise-and-witnesses-s9)).

## Answer

1. **Perception distance (S6).** `clamp(x, 1, 10)` (0x4AF240). Sight passes the
   observer's (possibly reduced) Perception, hearing passes it minus 4, the AI
   target scan its Perception (box half-size) and the item search Perception
   minus 5.
2. **Sight (S6, 0x4AF260)** returns an *excess distance*: 0 means seen. A
   stunned, blinded, sleeping or unconscious observer, an invisible target
   (unless the observer has extraordinary Perception or detects invisibility),
   a target outside the observer's front five of eight directions, or a
   distance above 1000 give 1000. Otherwise `max(0, d − clamp(P′, 1, 10))`,
   plus 1 when something blocks the straight line, where `P′` is Perception
   reduced by the prowling margin of a concealed target. The F0 summary of the
   plan is confirmed; the early refusals are added.
3. **Hearing (S7, 0x4AF470)** returns an excess distance too. Only a stunned or
   unconscious observer or a distance above 1000 give 1000; there is no facing
   or invisibility test; sleeping halves Perception. The range is
   `(clamp(P′ − 4, 1, 10) + {0, 6, 13}[loudness]) / 2 − sound cost`, the
   table being the noise radii 2/8/15 minus 2; `P′` is reduced by the margin of
   a target moving silently, which concealment sets together with "concealed".
   This corrects the first-pass reading "loudness 2/8/15" for hearing: those
   values are the alert radii of [section 6](#6-noise-and-witnesses-s9).
4. **Sound cost (S7, 0x4AF640).** Along a straight screen-space line sampled
   every 10 pixels, each tile change costs 2 per wall and 1 per closed, unbroken
   door on the crossed edge and 8 for a soundproof tile entered; objects and
   critters cost nothing; same tile 0, a line too long to build 100.
5. **Line of fire (S8, 0x4ADE00), closes combat O4.** The same line walk with
   the missile flags: a solid wall piece blocks (unless the wall carries spell
   flag 0x100000); a closed door, an object or a living critter blocks only
   when it lacks the "shoot-through" flag, and with it adds **50** when not
   "see-through" or **20** when see-through and providing cover; objects on the
   target's tile are skipped. The function returns
   the summed cover and reports the first blocker. The **26 threshold** belongs
   to two animation-goal callbacks (0x425590, 0x429960): an attacker has a clear
   shot only when the cover is below 26 (at most one see-through cover object)
   and nothing but the target blocks. The attack difficulty adds the cover value
   and makes a blocked shot impossible; AI spell use and magick line of sight
   refuse at 100 or a blocker.
6. **Noise and witnesses (S9).** An attack by a critter on a critter (when the
   attack event's flags 1, 2 and 4 are clear) scans NPCs in a square of
   half-size 2/8/15 by loudness around the attacker (when the two are less than
   30 tiles apart) and around the victim (when more than 1 tile apart). Each
   witness other than the two and their leaders is woken if asleep, then joins
   the victim's side if it protects the victim and perceives it, else the
   attacker's side if it protects the attacker and perceives him, else flees
   from the attacker when it perceives the victim, is no guard, has no "no
   flee" flag and is in no danger state.
7. **Dynamite (S9, 0x4AA0D0).** When a critter uses the dynamite item as a trap,
   every living NPC within a 10-tile square around it that is not mind-controlled
   and does not have that critter as PC leader, and that sees it or hears it at
   silent loudness, counts as attacked by it (silent, no flags).
8. **Anomalies.** No first-pass anomaly belongs to A2. Two double counts are
   bugs of the original with Original switches (owner decision 2026-10-20), as
   the plan's precedents A6, A7 and MR3: a corner step of the line counts the
   corner tile's soundproofing and cover twice (AW13, with AW14, the last corner
   step skipping objects on the side tiles), and an NPC inside both alert
   squares is handled twice (AW16). Kept in both settings: living critters block
   lines although the walk asks to ignore critters (AW15), lines longer than
   about 1000 screen pixels fail with cost 100 (AW17, flagged for the owner
   because long ranged shots get +100 difficulty), the walk is shifted by the
   observer's pixel offset (AW18), and a sleeping witness wakes without any
   perception test (AW19) ([anomalies](#9-anomalies-and-decisions)).

## 1. Perception distance (S6)

**0x4AF240** `PerceptionDistance(x) = x < 1 ? 1 : (x > 10 ? 10 : x)`.

| Caller (call site) | Argument | Use |
| --- | --- | --- |
| sight 0x4AF260 (0x4AF3F0) | observer's Perception after the concealment margin | sight range |
| hearing 0x4AF470 (0x4AF5D8) | observer's Perception after sleeping and the moving-silently margin, minus 4 | hearing base |
| find target 0x4AB460 (0x4AB4F1) | NPC's effective Perception | half-size of the square scanned for PCs and NPCs (stage A3) |
| item search 0x4A8F90 (0x4A8FCA) | NPC's effective Perception minus 5 | half-size of the square scanned for items (stage A5) |

Example: Perception 8 → 8 for sight, 4 for hearing, 3 for the item search;
Perception 14 → 10, 10, 9.

## 2. Sight (S6)

**0x4AF260** `CanSee(observer O, target T) → int` (0 = seen).

1. O's critter flags hold stunned (0x20), blinded (0x80) or sleeping (0x1000)
   → 1000. O unconscious (0x45D800; also true for a non-critter, false for an
   undead) → 1000.
2. T invisible (object flag 0x200 or spell flag 0x1): unless O's Perception is
   extraordinary (0x4B0EE0) or O has the spell flag "detecting invisible"
   (0x200) → 1000.
3. Facing (0x45EFF0): `r` = direction from O to T (`object_rot`, 0..7; the same
   tile gives 5), `f` = the rotation of O's current art; `k = (r − f + 8) mod 8`.
   `k ∈ {3, 4, 5}` (the three directions behind) → 1000.
4. `P` = O's effective Perception (stat 5). If T is concealed (a PC or NPC with
   critter flag 0x1, 0x45EDE0): `M = clamp(E − D, 0, 100)` with `E` the prowling
   effectiveness of T against O and `D` the sight difficulty
   ([section 2.1](#21-the-prowling-margin)); `P := P − (P × M) / 100`.
5. `R = PerceptionDistance(P)`.
6. `d` = distance; `d > 1000` → 1000.
7. `x = max(0, d − R)`; line of fire from O to T's tile
   ([section 5](#5-line-of-fire-s8-combat-o4)) with a blocker output; a blocker
   → `x + 1`. The cover cost is discarded. Return `x`.

So a blocked line is never "seen", even at range; a value above 0 says by how
many tiles the target is out of sight (one more when blocked).

Worked numbers. O: Perception 8, awake, facing T, no blocker.
T not concealed at distance 6: `R = 8`, `x = 0` → seen; at 12 → 4.
T an expert prowler (training 2, prowling level 12) behind one see-through
cover object on the line: `E = 260 / 4 = 65` (Δ = 12 − 8 = 4), `D = −20`
(below), `M = 85`, `P = 8 − 680 / 100 = 2`, `R = 2`: seen at distance 2
(`x = 0`), not at 4 (`x = 2`). The same prowler with a closed door between:
`D = −100`, `M = 100`, `P = 0`, `R = 1`, and the door adds 1: never seen.

### 2.1 The prowling margin

This is what the executable computes; in Steamwoven the margin `M` comes from
the shared skill check ARC-SKILLS-003 and the awareness functions take it as
an input. Both perception tests build a skill invocation with T (the prowler) as source,
O (the observer) as target, skill 6 (prowling), no item, modifier 0, and the
flag 0x4000 for sight or 0x2000 for hearing.

**Effectiveness** (0x4C62E0 → opposed 0x4C6410): `Δ` = T's prowling level −
O's effective Perception; `Δ ≥ 10` → 100, `Δ ≤ −10` → 0, else `V[Δ] / 4` with
`V[−9..9]` = 55, 66, 78, 91, 105, 120, 136, 153, 171, 190, 209, 227, 244,
260, 275, 289, 302, 314, 325 (so 13 … 81 for Δ = −9 … 9; 47 at Δ = 0); +10 when T's
intelligence is extraordinary; for the local PC +E/2 on the easy and −E/4 on
the hard game difficulty; at least 0. Prowling has no 95 cap.

**Difficulty** (0x4C8430), in this order, starting from 0:

| # | Term | Value |
| ---: | --- | --- |
| 1 | O's state (first match) | paralysed −50; unconscious −50; falling-down animation (7) or stunned −30; sleeping −50. The "target unaware" term of other skills is skipped for prowling. In sight only the paralysed and falling terms can apply (the others returned 1000 before) |
| 2 | light, sight only, T not undead, T's prowling training untrained | `L` = light level at T (0x4DCE10, 0..255); `p = (255 − L) × 30 / 255`; T carrying light: extra-large `p/4`, large `p/3`, medium `p/2`, small `3p/4`; O without dark sight: `30 − p`, with dark sight: `p`; added when above 0 |
| 3 | T blinded | +30 |
| 4 | T's legs crippled | +30 |
| 5a | sight (0x4000) | line of fire from O to T's tile: a blocker → the whole difficulty is **−100** (earlier terms discarded); else cover `c`: `c = 0` and T's training below expert → +50; otherwise −`c` |
| 5b | hearing (0x2000) | for each worn armour item in slots 1000..1008: `a` = its silent-move adjustment + its magic silent-move adjustment (magic-adjusted for T); apprentice or better: `a / 2`; difficulty −`a` |

Light example (sight, untrained T, O without dark sight): `L = 51` →
`p = 204 × 30 / 255 = 24` → term `30 − 24 = 6`; in full light (`L = 255`)
`p = 0` → +30. Hearing example: leather armour with an adjustment of −10
(illustrative value; data not read), apprentice → −5 → difficulty +5.

## 3. Hearing (S7)

**0x4AF470** `CanHear(observer O, heard T, loudness s) → int` (0 = heard).

1. O stunned (critter flag 0x20) → 1000; O unconscious → 1000. Blindness,
   facing and invisibility do not matter.
2. `d` = distance; `d > 1000` → 1000.
3. `P` = O's effective Perception; O sleeping (0x1000) → `P := P / 2`.
4. T's critter flags hold "moving silently" (0x2) → `M = clamp(E − D, 0, 100)`
   with the hearing difficulty of [section 2.1](#21-the-prowling-margin);
   `P := P − (P × M) / 100`. Concealment sets and clears flags 0x1 and 0x2
   together, for the critter and all its followers (0x45EE30, 0x45EE90), so
   "moving silently" means concealed.
5. `H = (PerceptionDistance(P − 4) + N[s] − N[0]) / 2 − SoundCost(O, T)` with
   `N = {2, 8, 15}` (0x5B50C0), so the bracket adds 0, 6 or 13.
6. Return `d > H ? d − H : 0`.

| Perception `P` after steps 3–4 | silent | normal | loud |
| ---: | ---: | ---: | ---: |
| ≤ 5 | 0 | 3 | 7 |
| 8 | 2 | 5 | 8 |
| 10 | 3 | 6 | 9 |
| ≥ 14 | 5 | 8 | 11 |

(range before the sound cost). Worked numbers: O awake with Perception 8, a
closed door on the line (+1): normal `H = 5 − 1 = 4`, so heard at 4, at 6 the
result is 2; loud `H = 8 − 1 = 7`; silent `H = 2 − 1 = 1`. The same O asleep:
`P = 4` → normal 3, loud 7 (before the door). An expert prowler of level 12 with
the armour example above: `M = 65 − 5 = 60`, `P = 8 − 480 / 100 = 4` → normal
`H = 3`.

## 4. Sound cost (S7)

**0x4AF640** `SoundCost(O, T) → int`.

1. Same tile → 0.
2. The straight line (0x4201C0): from O's tile centre to T's tile centre in
   screen pixels (the tile's screen position plus 40, 20; in the normal
   isometric view a tile step of +x moves −40, +20 and +y moves +40, +20
   pixels), rasterised one pixel at a time along the major axis; every 10th
   pixel adds one sample (a signed byte pair dx, dy from the previous sample),
   the end point is fed until the last partial step is stored. At most 100
   samples: a longer line fails → **100** (AW17).
3. Walk: the position starts at O's tile centre **plus O's pixel offset**
   (object fields 3 and 4, AW18); each sample moves it, `location_at` gives the
   tile; when the tile changes, `rot` = direction from the current to the new
   tile and the step cost below is added with flags "sound" (0x20), plus "skip
   objects" (0x10) for the step into T's tile (no effect under "sound"). The walk
   ends at T's tile or when the samples run out.
4. Return the sum.

**Step cost under "sound"** (0x43FDC0 → 0x43FE00), for an edge step (odd
direction):

- source tile: each wall whose art rotation (rounded up to odd) equals `rot`
  +2; each door there that is closed and not broken +1;
- `location_in_dir` fails → the step returns 1 (dropping what it had);
- the destination tile is soundproof (0x4D7180) +8;
- destination tile: each wall whose rounded rotation + 4 equals `rot` +2, each
  such closed, unbroken door +1; every other object (containers, scenery,
  traps, critters) costs nothing and nothing blocks.

A corner step (even direction) is the sum of four edge steps: (from, rot−1),
(side 1, rot+1), (from, rot+1), (side 2, rot−1), where side 1 and side 2 are the
two neighbours; so each crossed edge counts once, each side tile once and the
corner tile twice (a soundproof corner tile +16, AW13).

Example: the line crosses one wall edge and then enters a soundproof tile by an
edge step: `2 + 8 = 10`; normal hearing at Perception 8 (`5 − 10`) is then
negative and nobody hears.

## 5. Line of fire (S8, combat O4)

**0x4ADE00** `LineOfFire(O, location L, out blocker) → int`.

1. Blocker := none. O on tile L → 0.
2. The line as in section 4; failure → **100** with no blocker (AW17).
3. Walk as in section 4 with flags "projectile" and "ignore critters" (0x08 |
   0x04), plus "skip objects" (0x10) on the step into L, and no mover (so no
   door is opened). After each step the cost is added; when the step reports a
   blocker it is stored and the cost so far (including that step) is returned.
   (With no output pointer the walk would go on; every caller passes one.)
4. Return the summed cost.

**Step under the missile flags** (edge step; corner steps as in section 4):

| Thing (source edge, destination tile) | Result |
| --- | --- |
| wall piece 10, 13, 14, 17, 18 or 19 on the crossed edge | passes on an edge step of the line, blocks inside a corner step (inference: the window pieces of the walking code; see AU1) |
| wall piece 22, 25, 26, 29, 30, 31 or 32 | passes, no cost |
| any other wall piece | blocks, unless the wall object has spell flag 0x100000; then the shoot-through rule below |
| door: broken art or open | passes |
| door: closed | shoot-through rule |
| destination object (container, scenery, missile, trap; items are not listed) | shoot-through rule (the "no block" flag does not exempt it) |
| living critter on the destination tile | shoot-through rule, so an ordinary critter **blocks** although "ignore critters" is set (AW15) |
| dead critter | passes |
| any non-wall object on L's tile (and, on a last corner step, on its two side tiles, AW14) | skipped |

**Shoot-through rule:** no "shoot-through" flag (0x20) → blocker;
shoot-through without "see-through" (0x10) → **+50**; shoot-through and
see-through with "provides cover" (0x4000) → **+20**; otherwise 0. A corner
step counts an object on its corner tile twice (+40 or +100, AW13) and a
blocker on either side tile blocks it.

Worked numbers: a line over two edge steps with one see-through cover object on
the middle tile → 20 (clear shot for the animation test, +20 attack
difficulty); a second such object → 40 (not clear); one shoot-through,
non-see-through object → 50; a closed door that is not shoot-through → blocker.

**Who reads it**:

| Caller (call site) | From → to | Use |
| --- | --- | --- |
| animation goal callback 0x425590 (0x425649), from the goal tables | attacker → target's tile | when the target moved since the run started, the run-info flag value 0x4 (at 0xC00) is set and nothing else is done; otherwise, after the reach check 0x4294F0 failed: when cost < **26** and no blocker but the target, and only with a wielded weapon, every goal entry of the run (run-info count) gets the weapon range and the same flag 0x4 is set |
| animation goal callback 0x429960 (0x429A8D; also via 0x4294A0) | attacker → target's tile | false when the target has object flag 0x1 or 0x2 (destroyed, off); otherwise true only when distance ≤ max(weapon range, 1), cost < **26** and no blocker but the target |
| attack update 0x4294F0 (0x42963C), from the attack goal 0x432B46, 0x432BE2 | attacker → target's tile | cost ignored; a blocker other than the target needs a path (0x426500) |
| skill difficulty 0x4C8430, bow, throwing, firearms (0x4C88D7) | source → target's tile | blocker: +1,000,000 (impossible, flag 0x20000); else cost > 0 is added (flag 0x200, cover) |
| skill difficulty, prowling sight (0x4C8BC6) | observer → prowler's tile | section 2.1 term 5a |
| sight 0x4AF260 (0x4AF446) | observer → target's tile | blocker +1 |
| AI spell or item use check 0x4AE720 (0x4AE994) | caster → target's tile, not for itself | cost ≥ 100 or a blocker other than the target → refusal 5 (stage A5) |
| AI grenade 0x4AC910 (0x4AC9F3) | NPC → target's tile | any blocker → no grenade (stage A5) |
| magick line of sight 0x456BC0 (0x456C67) | caster → target | cost ≥ 100 → none; a blocker that is not shoot-through and not the target → none |
| ranged attack resolution 0x4B3170 (0x4B33E9) | attacker → target's tile | on its ranged branch a blocker becomes the struck object (combat stage) |

So the **26 threshold** is the animation's "clear shot from here" test: one
see-through cover object (20) still allows the shot, two (40) or one
non-see-through shoot-through object (50) make the attacker look for another
position; the **cover rating** is the summed 20/50 values, which the attack
difficulty adds as a penalty.

## 6. Noise and witnesses (S9)

**Loudness of an attack** (0x4B2810): no item or not a weapon 1; a weapon with
the "loud" flag (0x1) 2, with the "silent" flag (0x2) 0, otherwise 1. Other
callers of the attack event pass fixed values (below).

**Attack event gate** (0x4A9650, `Attack(attacker A, victim V, loudness s,
flags)`, stage A3): the noise is raised (call at 0x4A98A8) only for a PC or NPC
attacker and a PC or NPC victim that passed the event's early refusals (dead,
nigh-invulnerable, off or invulnerable, the decoy, a wall, self), after V (or
its PC leader) lost concealment, and only when flag bits 1, 2 and 4 are clear.
The combat paths 0x4B2690 (calls at 0x4B279C, 0x4B27F7) and 0x4B3170 (call at
0x4B337B) pass the weapon loudness and set event flag 1 when the attack's
combat flag 0x800 is set, so those attacks raise no noise and no witnesses
(the meaning of combat flag 0x800 is AU5); the portal and container guards
0x4AEE50 (calls at 0x4AF060, 0x4AF105) pass their caller's loudness.

**0x4A9E10** `Noise(V, A, s)`: `r = N[s]` (2, 8 or 15); `d` = distance(V, A).

1. `d < 30` (twice the loud radius, whatever `s`): every NPC in the square of
   half-size `r` around **A** (0x4AE4E0, type NPC only) is a witness.
2. `d > 1` : every NPC in the square of half-size `r` around **V** is a
   witness.

An NPC inside both squares is handled twice (AW16).

**0x4A9F10** `Witness(W, A, V, s)`:

1. `leader` = W's leader, none while W has the NPC flag "wait here" (0x8).
   `W = V`, `W = A`, `leader = A` or `leader = V` → nothing.
2. W asleep → woken (0x4AD0B0), before any test (AW19).
3. W protects V (0x4AE3A0, stage A3: same faction, or a guard of the same
   origin; never while mind-controlled): if `CanSee(W, V) = 0` or
   `CanHear(W, V, s) = 0` → W reacts to an attack by **A** (0x4AA620, stage
   A3/A4); otherwise nothing.
4. Else W protects A: if W sees or hears A → W reacts to an attack by **V**.
5. Else: W a guard (social class 5), W with "no flee" (critter flag
   0x8000000) or W in any danger state other than NONE → nothing; otherwise, if
   W sees or hears **V** → the setter puts W into FLEE from **A** (0x4AABE0,
   state 2).

The victim's side comes first. Worked example: the PC shoots a villager V from
6 tiles with a normal weapon (`s = 1`, `r = 8`). Both squares (±8 around the
PC and around V) are scanned. A guard G of V's faction 5 tiles from V, facing
V, Perception 7: `R = 7`, `x = 0` → G reacts to an attack by the PC. Another
villager W of no protecting tie, 4 tiles behind V with its back turned,
Perception 6, nothing on the line: sight gives 1000, hearing
`H = (2 + 6) / 2 = 4`, `d = 4` → heard → W flees from the PC. A guard of
another faction does nothing.

## 7. Dynamite (S9)

**0x4AA0D0** `DynamiteNotice(U)`, called by the trap use on an object
(0x4BC384) and at a location (0x4BC4D3) with the using critter `U`, when the
used item's spell field 2 is 176 (the magick entry an earlier reading names
dynamite), after the item is consumed. For every NPC `N` in the square of
half-size 10 around `U`: skip a dead `N`, a mind-controlled `N` (spell flag
0x8000000) and an `N` whose PC leader is `U`; if `CanSee(N, U) = 0` or
`CanHear(N, U, silent) = 0` → `Attack(U, N, silent, 0)`, which in turn raises
the noise of section 6 with radius 2. The notice is given when the dynamite is
used, not when it explodes (the name "explosion" is the earlier reading's).

Example: the PC arms dynamite with an NPC 7 tiles away, facing it, Perception
8: `R = 8` → seen → the NPC is attacked by the PC (its reaction, enemy list and
fight follow from the attack event, stage A3).

## 8. Call sites of sight and hearing

Every caller treats 0 as perceived.

| Caller | Sight (call) | Hearing (call, loudness) | Use |
| --- | --- | --- | --- |
| script condition 28 "can see" / 29 "can hear" (0x4451B0) | 0x4462F2 | 0x446377, normal | 0 → condition true |
| witness 0x4A9F10 | 0x4A9FAB, 0x4AA00D, 0x4AA08D | 0x4A9FC0, 0x4AA02A, 0x4AA0A2, the attack's | see, else hear |
| dynamite 0x4AA0D0 | 0x4AA14B | 0x4AA162, silent | see, else hear |
| find target 0x4AB460 (stage A3) | 0x4AB702, 0x4AB855, 0x4AB8B0 | 0x4AB6F2, 0x4AB839, 0x4AB898: silent for a concealed critter, else normal | hear first, then see |
| target validity 0x4AB990 (stage A3) | 0x4ABBA1 | — | a concealed master prowler (training 3) not seen is no valid target |
| portal and container guards 0x4AEE50 (S22) | 0x4AEF8A | 0x4AEFA0, the caller's: loud from damage (0x4B4727, 0x4B474C) and magick (0x4550AE), silent from skill use (0x4C7EE4, 0x4C7F1D) | a guard that neither sees nor hears is skipped |
| attack set-up 0x4B2210 (combat) | 0x4B2303 | 0x4B2319, silent | with a backstab possible against an NPC, the attack is also marked unnoticed (0x8000) when the NPC neither sees nor hears the attacker |
| skill difficulty 0x4C8430 | 0x4C86B3 | 0x4C86C9, normal | −30 when the target, not in combat mode, neither sees nor hears the source (not for prowling) |
| examine critter 0x553F70 (0x5541AE, 0x5541CA) | NPC → concealed PC | silent | the smaller value picks one of five lines (0, 1, 2, 3, more); a sleeping NPC never shows 0 |
| inventory window 0x572370 | 0x572408 | — | in two of its modes the PC is turned toward the other object and refused when it does not see it |

Attack events from combat (0x4B279C, 0x4B27F7, 0x4B337B) pass the weapon
loudness, with event flag 1 (no noise) when the attack's combat flag 0x800 is
set; the guards (0x4AF060, 0x4AF105) pass their caller's loudness. Fixed
loudness of the other attack events: normal for the dialog (0x416717),
fire damage (0x42A87C), magick (0x453F5B, 0x453F8B), the follower broadcast
(0x4C3763) and skill use (0x4C7728, 0x4C793B); normal with flag 2 for the
script action (0x447D95, so no noise); loud with flag 4 for the follower
attack order (0x4C37D5, no noise); silent for the redirect (0x4A94F8) and
dynamite. The table index therefore stays 0..2 (a 3 would read the global
"NPC fighting" switch that follows the table).

## 9. Anomalies and decisions

Verdicts and rows:
[ledger](ledger/npc-ai.md#npc-ai-awareness-arc-res-056). No
first-pass anomaly (P1–P7) belongs to A2, and the F0 summaries of S6–S9 were
confirmed or refined, none contradicted. The rows below are behaviour of the
original that looks unplanned; none crashes or reads out of bounds. Decisions
follow D2 and D8 and the plan's precedents (A6 and A7: double counts are bugs of
the original with Original switches; MR3 of ARC-RES-058): a double count gets a
Steamwoven fix and a proposed Original switch that keeps the retail behaviour;
behaviour that looks designed or is a limit is kept in both settings, no switch.
The owner accepted every proposed Original switch of this table on 2026-10-20
(ARC-RES-063).

| ID | Original 1.0.7.4 | Verdict | Proposed decision |
| --- | --- | --- | --- |
| AW13 | A corner step of the line is priced as both L-shaped routes around the corner: each side tile once, the corner tile twice (a soundproof tile +16, a see-through cover object +40, so one such object on a corner tile already denies a clear shot), and a blocker on either side tile blocks the corner. | Original bug for the corner tile's double cost (the step function serves walking, where only a blocker matters); the side-tile blocking is the original's design | Steamwoven: the corner tile's soundproofing and objects count once; side tiles and blockers as in the original. Original setting keeps both routes summed (`CornerStepCountsBothRoutes`, owner decision 2026-10-20). |
| AW14 | On the last step, "skip objects" also skips objects on the two side tiles of a corner step. | Original bug (same reuse as AW13) | Follows AW13: Steamwoven skips only the target tile's objects; the same switch keeps the original (owner decision 2026-10-20). |
| AW15 | The line passes "ignore critters", but under "projectile" a living critter still goes to the shoot-through test, so critters block lines of fire and sight (the flag has no effect there). | Original (likely intended: no shooting through bodies) | Kept in both settings, no switch. |
| AW16 | An NPC inside both alert squares (around the attacker and around the victim) is handled twice; the second time repeats the attacked reaction, including its target-choice roll (stage A3). | Original bug (the A7 pattern: one event processed twice) | Steamwoven: each NPC is handled once per attack (the attacker's square first). Original setting handles it twice (`WitnessHandledTwice`, owner decision 2026-10-20). |
| AW17 | A line of 100 samples (about 1000 screen pixels along its major axis: some 12 tiles along the screen-horizontal tile diagonal, 24–25 along a tile axis or the screen vertical) fails: the line of fire returns 100 without a blocker (attack difficulty +100, no magick line of sight, AI refusal 5) and the sound cost is 100. The geometry also depends on the view option (0x5FC2D0, isometric or the editor's top-down view). | Original (limit) | Kept for the isometric view in both settings, no switch; the top-down view is not modelled. **For the owner:** beyond about 12 tiles along the screen-horizontal diagonal every ranged shot gets +100 difficulty and the animation never finds a clear shot. |
| AW18 | The walk adds the observer's pixel offset to a line built between tile centres, so a moving observer's line is shifted and may end beside the target tile (then the last-step skip never applies). | Original | Kept in both settings, no switch (inference: a standing critter has offset 0, so only a moving observer is affected). |
| AW19 | A witness inside an alert square is woken before any perception test. | Original | Kept in both settings, no switch. |

Further observations, no decision needed: sight discards the cover cost and a
blocked prowling line sets the difficulty to −100, dropping its other terms
(the original's design); "skip objects" has no effect under "sound"; the walk's
no-output-pointer path is unreachable; the dynamite notice hears at silent
loudness and fires on use, not on explosion.

## 10. Consequences for Steamwoven (project policy proposals)

1. **Slice I2** (done: ARC-WORLD-078, [AI awareness](../world/ai-awareness.md)) implements `PerceptionDistance`, `CanSee`, `CanHear`,
   `SoundCost` and `LineOfFire` as pure functions over an explicit record
   (observer and target flags, stats, light level and carried light, dark
   sight, facing, the tile line and the objects on its tiles) with the
   arithmetic above, returning the excess distance or the cost and the blocker
   with a reason (`STATE`, `INVISIBLE`, `FACING`, `RANGE`, `BLOCKED`). The
   prowling margin `M` is an input from the shared skill check ARC-SKILLS-003
   (effectiveness, opposed table, prowling difficulty with its line-of-fire
   term); the AI code does not compute it.
2. **The line walk** reproduces the screen-space sampling (10-pixel steps, 100
   samples, the observer's pixel offset) and the step function's edge and
   corner rules, so the Original setting reproduces AW13–AW18 and Steamwoven
   differs only by the AW13/AW14 fix; it shares the traversal records of the
   passability work.
3. **The combat difficulty** (FR-COMBAT-007) takes the cover value and the
   blocker from `LineOfFire`; the attack executor (stage A6) uses the 26
   threshold for "clear shot from here".
4. **Noise and witnesses** become an event of the attack owner (stage A3 / slice
   I4) with the loudness rule, the two squares and the witness order above;
   protect and the attacked reaction plug in from A3/A4; Steamwoven handles each
   witness once (AW16).

## Unknowns

- **AU1** Which wall pieces 10–32 are in the art (windows, openings) is a data
  question; the rule is stated by piece number.
- **AU2** Script conditions 28 and 29 may pass a non-critter observer: the
  unconscious test (0x45D800) is true for any non-critter, so sight and hearing
  return 1000 for it; only the critter-flag read made just before that test
  (a critter field on a non-critter) was not traced. A non-critter heard
  object is read for the moving-silently flag in the same way.
- **AU3** The light level at an object (0x4DCE10) is the lighting stage's
  function; only its 0..255 result is used here.
- **AU4** How often lines exceed the 100-sample limit in real fights is a
  runtime question.
- **AU5** What sets the combat record's flag 0x800 (which suppresses the
  noise of combat attacks) is the combat stage's question. *Settled by ARC-RES-066:*
  the missile's flight sets it for an object a missile meets instead of its target;
  the resolution's own test is never true ([execution](npc-ai-execution.md#8-other-answers)).
