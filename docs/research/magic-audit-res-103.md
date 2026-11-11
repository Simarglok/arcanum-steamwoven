# Magic audit: the 80 college spells, what runs and the order of work

Status: **Research and plan**, work item ARC-RES-103 (2026-11-11). Docs-only: no source, test or build change and no repository checks run (the census below ran
locally on the owner's Mac and is not a repository check). It audits every spell of the 16 colleges (spells 0 to 79, five ranks each) against the installed data, the
code on `origin/main` (`c53887a94`, after DEV-347) and the smoke stages, and proposes the order of work by college. It **builds on and does not replace** the
[magic implementation plan](magic-implementation-plan.md) (MT slices), epics E1 and E4 of the [missing-systems plan](../project/missing-systems-plan.md) and the Summon note
[ARC-RES-101](summon-objects-res-101.md); where it names their slices it uses their labels, and it adds a few of its own (`MA-n`, planning labels, not task codes).

Requirements (reference only; no coverage row changes status): [FR-MAGIC-006](../requirements/combat-magic-tech.md#fr-magic-006),
[FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007), [FR-MAGIC-008](../requirements/combat-magic-tech.md#fr-magic-008),
[FR-MAGIC-009](../requirements/combat-magic-tech.md#fr-magic-009), [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012),
[FR-CHAR-022](../requirements/characters-skills.md#fr-char-022), [FR-SOCIAL-001](../requirements/story-social-ai.md#fr-social-001),
[FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003), [FR-AI-002](../requirements/story-social-ai.md#fr-ai-002),
[FR-TRAVEL-005](../requirements/world-travel-runtime.md#fr-travel-005).

## Answer

- **17 of the 80 spells run end to end, 17 run in part, 46 do nothing but pay their cost and play their eye candy.** "Run" is a code-path claim, not a run per spell:
  only three spells are named by a smoke stage (51, 56 and, by its data filter, 55); the others are covered by synthetic unit tests of their components. Everything else is read from the
  dispatch ([pipeline](#what-the-cast-pipeline-already-does)), so the first slice below is a test that makes this document checkable (MA-0).
- **One component is the biggest hole: `ObjFlag`.** 49 of the 80 spells carry it, on 35 distinct (field, flag) pairs, and the cast host writes four of them
  (entangled, stoned, paralysed, spoken with the dead). For eight more flags the rule that **reads** the flag already exists and nothing writes it (anti-magic shell,
  full reflection, bonds of magick, invisible, enshrouded, detecting alignment, mind control, the body spells' unarmed rule); one slice that writes any spell flag
  with the original's reference count (MA-1) turns several spells on at once.
- **Summon is the second hole** (18 spells: 7 make a scenery, 11 a critter) and it is already planned: E1-a (created scenery objects) merged as DEV-347 (#924) while this audit was written; E1-b, E1-c and E1-e remain.
- **The third hole is not in the plan: resistances and skills in effects are inert.** The session keeps stat, maximum and reaction changes of an effect, but not
  resistance changes ([durable effects](../world/durable-effects-runtime.md)). Five effect numbers used by spells change nothing else (54, 56, 154, 155 and the skill 157) and three more are half inert
  (51, 52, 55), so Resist Magick (40, which has no flag at all) and the resistance half of the Body spells and Shield of Protection (30) cannot work even when their flags are written (MA-2).
- **The plan's "control components" (E4-c) shrink for magic:** of the 25 component types the 80 spells use 12; the other 13 (Cast, Interrupt, Terminate, TestNBranch,
  ChargeNBranch, TraitIdx, Trait64, Use, EnvFlag, AIRedirect, AGoalTerminate, NoOp, Recharge) belong to the technology records, items and the 60 non-college records. For the colleges only **Dispel** is needed (spells 41, 42).
- **Order.** Wave 0 is what is in flight (DEV-348, NPC magic state). Wave 1 starts now: the spell-flag writer with the split of the cast host's dispatch (MA-1), the census test (MA-0),
  the executable reads the missing components need (MA-R) and created critters (E1-c). Wave 2 (after MA-1 merges, so the slices stop sharing one if-chain): Movement (MA-5), locks (MA-6), Dispel (MA-3),
  scenery Summon (E1-b), effect consumers (MA-2). Wave 3: social flags (MA-4), Trait and the body spells (MA-8), Identify (MA-7). Wave 4: creature Summon (E1-e), trap spotting (E6), the raise script and AI parity.
  After each wave the number of working spells is 24, 33, 53, 68 and 80 ([waves](#waves-what-is-parallel-and-what-is-not)).
- **Whole colleges:** after wave 1 Necromantic (white) works completely; after wave 2 four more (Conveyance, Force, Meta, Temporal); after wave 3 two more (Mental, Morph); after wave 4 the other nine (Air, Earth, Fire,
  Water, Divination, Nature, Necromantic (black), Phantasm, Summoning), which wait on created critters, Identify or trap spotting.
- **Decisions for the owner** are six (D6 added by MA-2) and sit in a table at the end ([decisions](#decisions-for-the-owner)); the owner decided all of them on 2026-11-11 (D1 a, D2 a, D3 b, D4 a, D5 a, D6 c).

## Method and limits

- **Data.** A scratch console program (not committed) read the owner's unchanged install (`Version.txt` 1.0.7.4) with the repository's own spell reader in like-original mode and listed, for
  spells 0..139, every record's cost, maintenance, duration, save, area line, AI priorities and every component with its parameters, plus the 22 effect rules the spells use. Numbers, flag
  names and spell names only; no art, no description text, no bytes. The "In the records" column below is generated from that listing.
- **Code.** `origin/main` at `c53887a94`: the dispatch of `SessionSpellCaster.Apply`, `SessionSpellCasterObjectFlags`, `SessionSpellCasterEffects`, `SpellLifeComponents`, the
  Simulation consumers of each flag (found by name and by flag mask), the durable-effects document, and the smoke stage sources that name a spell number. DEV-347 (E1-a, created scenery, schema 85, #924) merged while the audit was written; it did not change the cast host's dispatch. One slice is **in flight and not on
  `main`**: DEV-348 (E1-d, NPC magic state), in review on the owner's Mac. The audit assumes DEV-348 gives every critter an effect list and generic spell
  flags and leaves the Effect component's NPC branch in `SessionSpellCasterEffects.cs` to be checked against its merged diff.
- **Not done:** no viewer run, no smoke, no per-spell cast, no new executable read. "Today ✅" means every component the record needs has a code path and no known gap; it is not parity (parity stays with the
  [ledger](original-verification-ledger.md)). Where a fact needs the executable it is listed in MA-R, not guessed.
- **States.** ✅ every component has a code path; ◐ some do, the rest are named; ✗ nothing beyond payment and eye candy. When a component is not ready the cast log carries `ComponentNotReady` and the
  spell line shows a "not ready" message, so a ✗ or ◐ spell tells the player; it does not fail silently.
- **Independent check.** A fresh agent re-reads the exact diff against the code before the PR (the review step of the [task rules](../workflow/tasks.md#review-publication-and-merge)); its findings are in the pull request.

## What the cast pipeline already does

Everything before the components works for any caster and map (ledger [invocation](ledger/spell-runs.md#spell-invocation-arc-magic-006), [target areas](ledger/spell-runs.md#spell-target-areas-arc-magic-007),
[runs](ledger/spell-runs.md#spell-runs-arc-magic-009), [processing](ledger/spell-runs.md#spell-run-processing-arc-magic-010), [per-target stage](ledger/spell-runs.md#spell-per-target-stage-arc-magic-011),
[cost](ledger/magic.md#spell-cost-and-payment-arc-magic-003), [resistance](ledger/magic.md#spell-resistance-arc-magic-005)): the willpower, range, line of fire and flag checks, the cost and the
fatigue payment, the targets of each action, the save and the margin, the anti-magic shell and reflection at the per-target stage, the run table with BEGIN, MAINTAIN, END and the callback actions, the maintain bar, saved
runs of the active map, the endings by death, knock-out and the AI gate, the spell pane and hotkeys, the casting goal with its missile and eye candy, the AI's choice from its ten lists. 53 of the 80 spells are
maintained and two are timed, so run endings matter for most of them.

The components are the open part:

| Component | Lines in the 80 (spells) | State on `main` | Slice |
| --- | --- | --- | --- |
| EyeCandy | 201 (73) | Ready (log plus drawing, callbacks drive CALLBACK actions) | - |
| Damage | 18 (13) | Ready: typed, `Scaled`, flags (stun, scars, weapon drop, cripples) pass to the damage step | - |
| Heal | 7 (6) | Ready: hit points, poison, fatigue, full, scars, raise; the raise script (point 0x12) runs no SCR and allows | MA-10 (small) |
| AGoal | 4 (2) | Knockback (43) and hover (44) requests ready; the host pushes NPCs, not the hero or companions | MA-0 checks |
| **ObjFlag** | **101 (49)** | **Every spell-flag, object-flag and critter-flag bit on a critter except the social and AI ones (MA-1, ARC-DEV-358)** ([flags](#spell-and-object-flags-the-objflag-component)) | MA-4, MA-6, MA-8 |
| **Effect** | **48 (20)** | **Hero only**; changes to stats, maximums and reaction run, resistances and skills are inert ([effects](#effect-numbers-the-effect-component)) | E1-d, MA-2 |
| **Summon** | **18 (18)** | **Not built** | E1-a/b/c/e |
| Destroy | 17 (17) | Lying items only; the 17 spells use it on their summoned objects (CALLBACK, END_CALLBACK), so it follows Summon | E1-b |
| Trait | 10 (5) | Not built (Body spells, Polymorph) | MA-8 |
| Identify | 3 (3) | **Taken since MA-7 (ARC-MAGIC-032)**: the hero's answer is a typed event and a text line, the item's identification is the session's mark; the windows are E8's ([result](#identify-ma-7-arc-magic-032)) | E8 |
| Movement | 2 (2) | Not built; the area-change path of the session exists (MIG11) but nothing calls it | MA-5 |
| Dispel | 2 (2) | **Taken since MA-3 (ARC-MAGIC-034)**: the runs' half (source, target, summoned list) and the stoned and summoned halves of the object; the perma-polymorph (MA-8) and the portal and container hold (MA-6) are left ([result](#dispel-ma-3-arc-magic-034)) | - |

## The flags and the effects the 80 spells use

### Spell and object flags (the `ObjFlag` component)

The columns say whether anything **reads** the flag today and whether the cast host **writes** it. A "rule" is a Simulation or World rule that already tests the flag.

| Flag (field) | Spells | Reader today | Writer | Slice |
| --- | --- | --- | --- | --- |
| Entangled, stoned (spell flags); paralysed (critter flags); spoken with dead | 51; 48; 59, 78; 56 | Hold rules, walk refusals, hit re-save (ARC-DEV-302, 304) | **Ready** (stoned: render side effect not) | - |
| Mind controlled | 14, 19, 24, 29, 39, 52, 53, 57, 58, 68, 71, 72, 73, 74 | Reaction and companion rules | No (follow, no flee, -50 reaction, disband at END are the side effects) | MA-4 |
| Object "don't draw" (object flags) | 11, 14, 17, 19, 21, 24, 26, 27, 29, 32, 34, 53, 57, 68, 71-74 | AI host hidden test, target rules (the draw path was not re-checked) | **Ready on a critter (MA-1: the flag and the paralysed bit, [OB3](ledger/spell-runs.md#objflag-component-of-the-cast-host-arc-dev-358)); the rest follow Summon** | E1 |
| Charmed | 35, 50 | None found | No (+30 reaction toward a PC, kill-on-sight override) | MA-4 |
| Water walking; Body of air, earth, fire, water | 13, 28; 13, 18, 23, 28 | The unarmed body bonus (+2 hardened hands, body of earth, fire, water) exists and **no producer feeds it from the flags**; the AI's body-of-air start refusal reads a fact whose producer was not checked | Flags written (MA-1); Trait and the water-walking clear's damage (OB2) are MA-8 | MA-8 |
| Anti-magic shell, full reflection, bonds of magick | 42; 44; 43 | Per-target stage; invocation check | **Ready (MA-1)** | - |
| Invisible; enshrouded | 69; 63 | Awareness and hostility rules | **Ready (MA-1: the object flags invisible and translucent are written; the viewer's draw of them is not read)** | - |
| Detecting alignment; detecting invisible; detecting traps | 5; 8; 8 | Hover window; target stage; none (trap spotting is not built) | **Ready (MA-1)** | E6 |
| Shielded | 30 | Hit eye candy only | **Ready (MA-1)** | - |
| Hardened hands | 45 | Unarmed damage rule, fed from the flags since MA-1 ([OB5](ledger/spell-runs.md#objflag-component-of-the-cast-host-arc-dev-358)) | **Ready (MA-1)** | - |
| Shrunk; polymorphed | 47; 49 | Render and equipment rules partly | Flags written (MA-1: shrunk with its object flag); Trait is MA-8 | MA-8 |
| Fleeing; blinded; undead (critter flags) | 38; 66; 58 | AI rules read them | No (AI notification and combat effects are the side effects) | MA-4 |
| Locked (portal and container); magically held (portal) | 1; 75 | Door and container state in the session (pick locks) | No | MA-6 |
| Identified (item) | 9 | Not modelled | No | MA-7 |
| Illusion; familiar (spell and NPC flags); Tempus Fugit | 68; 74; 79 | Illusion: none in the executable either; familiar: the bad-associates test, the travel hide and the follower removal; Tempus Fugit: the speed of the stat getter ([MA-R](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)) | No | MA-4, MA-9 |

### Effect numbers (the `Effect` component)

Effects live in the hero's list and, since DEV-348, in every NPC's. What an effect does in the session: stat, maximum hit points and fatigue, reaction and experience changes are
modelled, and since MA-2 the resistance changes too; skills, tech skills and the level and alignment group are inert ([durable effects](../world/durable-effects-runtime.md)).

| Effect | Spells | Changes | Runs today |
| --- | --- | --- | --- |
| 57, 58 | 76; 77 | speed -50 %; +100 % | Stat modelled; in the original speed reaches the turn-based action points and the real-time attack pause, not walking ([MA-R](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)); the session's use is MA-2's |
| 59 | 37 | willpower -50 % | Modelled |
| 60 | 47 | damage bonus -50 % | Modelled |
| 61, 62 | 18; 28, 51, 70 | speed -5 | Modelled |
| 63, 159 | 30; 67 | armour class +1 (x10, x30) | Modelled |
| 156 | 49 | damage bonus capped at 1 | Modelled |
| 160, 161, 162, 163, 326 | 10; 15; 20; 25; 46 | one primary stat +1 (326: strength -1) | Modelled |
| 51, 52, 55 | 13; 18; 28 | speed or strength or damage cap, **plus resistances** | Stat half modelled; resistance half reaches the combat resolver (MA-2, ARC-MAGIC-027; normal, fire, electrical and poison; the magic part only the sheet) |
| 54, 56, 154, 155 | 23; 40; 46; 30 | resistances only | Reach the combat resolver for the hero and NPCs (MA-2); 56 (Resist Magick) is magic resistance only, which the original's save does not read and Steamwoven's does (ARC-MAGIC-033, D6c, [result](#effect-consumers-ma-2-arc-magic-027)) |
| 157 | 8 | spot-trap skill at least 20 | **Inert** (skills) |

## The matrix

Columns: the record's data (generated from the installed records: fatigue cost, maintenance `cost@period` in seconds, duration, save stat and modifier, then the components per action; **B** begin, **M** maintain, **E** end,
**C** callback, **EC** end-callback), what runs today, what is missing, and the slices it needs. Spell numbers are the data's own; the rank is in brackets.

### Conveyance (spells 0-4)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 0 | Disarm (1) | 5 fat, save dexterity 0; **B** Damage normal 0-0 (drops weapon), eye candy | ✅ Damage 0 with the weapon-drop flag; the damage step carries the flag (life effect `DropWeapon`). Not run per spell | Per-spell run to confirm the drop | - |
| 1 | Unlocking Cantrip (2) | 10 fat; **B** Flag locked off x2, eye candy | ✗ Cost and eye candy only | `ObjFlag` on portal and container lock flags | MA-6 |
| 2 | Unseen Force (3) | 15 fat; **B** Goal 43 x2, eye candy x2 | ◐ AGoal 43 (knockback) request reaches the host, which pushes NPCs | Hero and companions as targets are not pushed (knockback runs, outside the spell slices); loose objects unverified | MA-0 |
| 3 | Spatial Distortion (4) | 25 fat; **B** Movement Target_Tile, eye candy | ✗ Cost and eye candy only | `Movement` (target tile) | MA-5 |
| 4 | Teleportation (5) | 50 fat; **B** eye candy, Movement Teleport_Tile | ✗ Cost and eye candy only | `Movement` (teleport tile), no-teleport areas, followers | MA-5 |

### Divination (spells 5-9)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 5 | Sense Alignment (1) | 5 fat, maint 1@10; **B** Flag detecting_alignment on; **E** Flag detecting_alignment off | ✗ Cost and eye candy only; the hover window already reads flag 0x80 | `ObjFlag` writer for `OSF_DETECTING_ALIGNMENT` | MA-1 |
| 6 | See Contents (2) | 10 fat; **B** Identify | ◐ Cost, and since MA-7 the answer as a text line | `Identify` on containers: the original opens the inventory window in its mode 4 with no lock test ([MA-R](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)); the answer is a line, the window is E8's | E8 |
| 7 | Read Aura (3) | 15 fat; **B** Identify | ◐ Cost, and since MA-7 the answer as a text line | `Identify` on a critter: the original opens the read-only NPC sheet for the casting player ([MA-R](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)); the answer is a line, the window is E8's | E8 |
| 8 | Sense Hidden (4) | 25 fat, maint 5@10; **B** Flag detecting_invisible on, Flag detecting_traps on, Effect 157 add x1; **E** Flag detecting_invisible off, Flag detecting_traps off, Effect 157 rem x1 | ✗ Cost only | `ObjFlag` detecting invisible and traps; trap spotting is not built; effect 157 is a skill change (inert until the skills lane models skills) | MA-1, E6 |
| 9 | Divine Magick (5) | 30 fat; **B** Flag identified on, Identify | ◐ Cost, the identified mark and the text line (MA-7) | Picking an inventory item as the target (nothing targets an inventory item yet); the item description window is E8's | E8 |

### Air (spells 10-14)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 10 | Vitality of Air (1) | 5 fat, maint 1@10; **B** Effect 160 add x4, eye candy; **E** Effect 160 rem x4, eye candy | ◐ Effect 160 (CN +1 x4) on the hero, removed at END | Companions and NPCs as targets (no effect list) | E1-d |
| 11 | Poison Vapours (2) | 10 fat, maint 5@10, save constitution 0; **B** Damage poison 15-50 (scaled), Damage magic 2-8 (scaled), Summon proto 4029, eye candy x2; **M** Damage poison 15-50 (scaled), Damage magic 1-8 (scaled); **E** eye candy x3; **C** Flag dontdraw off; **EC** Destroy | ◐ Poison and magic damage at BEGIN and at each MAINTAIN tick | The cloud scenery (proto 4029) and its removal; affected area of later ticks unverified | E1-b |
| 12 | Call Winds (3) | 15 fat, save constitution -5; **B** Goal 43 x2, Damage normal 30-50, eye candy | ✅ Knockback request and normal damage in a square "cone" | Per-spell run | - |
| 13 | Body of Air (4) | 25 fat, maint 2@10; **B** Trait art 28, Flag body_air on, Flag water_walking on, Effect 51 add x1, eye candy; **E** Flag body_air off, Trait restore, Flag water_walking off, Effect 51 rem x1, eye candy x2 | ◐ Effect 51 on the hero and NPCs: speed +10, the damage-bonus cap and its four resistance changes (normal, fire, electrical, poison reach the resolver; MA-2) | `Trait` (art 28), flags body-of-air and water-walking | MA-1, MA-8 |
| 14 | Call Air Elemental (5) | 50 fat, maint 5@10; **B** Summon proto 28310, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only | `Summon` critter (proto 28310), mind-control flag on the summoned | E1-c, E1-e, MA-4 |

### Earth (spells 15-19)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 15 | Strength of Earth (1) | 5 fat, maint 1@10; **B** Effect 161 add x4, eye candy; **E** Effect 161 rem x4, eye candy | ◐ Effect 161 (ST +1 x4) on the hero | Companions and NPCs as targets | E1-d |
| 16 | Stone Throw (2) | 10 fat, save constitution -5; **B** Damage normal 1-50 (scaled), eye candy | ✅ Damage normal, `Scaled` | Per-spell run | - |
| 17 | Wall of Stone (3) | 15 fat, maint 1@10; **B** Summon proto 4032, eye candy x2; **E** eye candy x3; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost and eye candy only | `Summon` scenery (proto 4032) on the wall tiles, END removal | E1-b |
| 18 | Body of Stone (4) | 25 fat, maint 2@10; **B** Trait art 3, Flag body_earth on, Effect 61 add x1, Effect 52 add x1, eye candy; **E** Flag body_earth off, Trait restore, Effect 61 rem x1, Effect 52 rem x1, eye candy x2 | ◐ Effect 61 (speed -5) and 52 (ST +10, resistances) on the hero and NPCs (MA-2) | `Trait` (art 3), flag body-of-earth, the unarmed body rule is not fed from flags | MA-1, MA-8 |
| 19 | Call Earth Elemental (5) | 50 fat, maint 5@10; **B** Summon proto 28311, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only | `Summon` critter (proto 28311), mind-control flag | E1-c, E1-e, MA-4 |

### Fire (spells 20-24)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 20 | Agility of Fire (1) | 5 fat, maint 1@10; **B** Effect 162 add x4, eye candy; **E** Effect 162 rem x4, eye candy | ◐ Effect 162 (DX +1 x4) on the hero | Companions and NPCs as targets | E1-d |
| 21 | Wall of Fire (2) | 10 fat, maint 1@10; **B** Summon proto 4031, eye candy x2; **E** eye candy x3; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost and eye candy only (B47) | `Summon` scenery (proto 4031) x5 that burns (goal 77 exists), END removal | E1-b |
| 22 | Fireflash (3) | 15 fat, save constitution -5; **B** Damage fire 15-45 (scaled), eye candy x3, Summon proto 4050; **C** Destroy | ◐ Fire damage in radius 2 (B47) | The flash scenery (proto 4050, needs an empty tile) and its callback `Destroy` | E1-b |
| 23 | Body of Fire (4) | 25 fat, maint 10@10; **B** Trait art 26, Effect 54 add x1, Flag body_fire on, eye candy; **E** Flag body_fire off, Effect 54 rem x1, Trait restore, eye candy x2 | ◐ Effect 54 (resistances, the fire one among them) on the hero and NPCs (MA-2); cost and eye candy | `Trait` (art 26), flag body-of-fire; fire damage of a spell cast reads the fire resistance after ARC-DEV-352 (#941) | MA-1, MA-8 |
| 24 | Call Fire Elemental (5) | 50 fat, maint 5@10; **B** Summon proto 28312, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only | `Summon` critter (proto 28312), mind-control flag | E1-c, E1-e, MA-4 |

### Water (spells 25-29)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 25 | Purity of Water (1) | 5 fat, maint 1@10; **B** Effect 163 add x4, eye candy; **E** Effect 163 rem x4, eye candy | ◐ Effect 163 (BE +1 x4) on the hero | Companions and NPCs as targets | E1-d |
| 26 | Call Fog (2) | 10 fat, maint 1@10; **B** Summon proto 4033, eye candy x2; **E** eye candy x3; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost and eye candy only | `Summon` scenery (proto 4033) | E1-b |
| 27 | Squall of Ice (3) | 15 fat, maint 5@10, save constitution -5; **B** Damage normal 3-12 (scaled), Summon proto 4034, eye candy x2; **M** Damage normal 1-1 (scaled), Damage normal 3-12; **E** eye candy x3; **C** Flag dontdraw off; **EC** Destroy | ◐ Normal damage at BEGIN and at each MAINTAIN tick | The ice cloud scenery (proto 4034) | E1-b |
| 28 | Body of Water (4) | 25 fat, maint 2@10; **B** Trait art 27, Effect 55 add x1, Flag body_water on, Flag water_walking on, Effect 62 add x1, eye candy; **E** Flag body_water off, Effect 55 rem x1, Trait restore, Flag water_walking off, Effect 62 rem x1, eye candy x2 | ◐ Effects 55 (resistances), 62 (speed -5) on the hero and NPCs (MA-2) | `Trait` (art 27), flags body-of-water and water-walking | MA-1, MA-8 |
| 29 | Call Water Elemental (5) | 50 fat, maint 5@10; **B** Summon proto 28313, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only | `Summon` critter (proto 28313), mind-control flag | E1-c, E1-e, MA-4 |

### Force (spells 30-34)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 30 | Shield of Protection (1) | 5 fat, maint 1@10; **B** Effect 63 add x10, Effect 155 add x25, eye candy x2, Flag shielded on; **E** Effect 63 rem x10, Effect 155 rem x25, eye candy x3, Flag shielded off | ◐ Effect 63 (AC +1 x10) and effect 155 (resistances) on the hero and NPCs (MA-2) | `ObjFlag` shielded (hit eye candy) | MA-1 |
| 31 | Jolt (2) | 10 fat, save constitution -10; **B** Damage electrical 2-25 (scaled), eye candy x2 | ✅ Electrical damage in radius 1 around self | Per-spell run | - |
| 32 | Wall of Force (3) | 15 fat, maint 2@10; **B** Summon proto 4030, eye candy x2; **E** eye candy x3; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost and eye candy only | `Summon` scenery (proto 4030) on the wall tiles | E1-b |
| 33 | Bolt of Lightning (4) | 25 fat, save constitution -5; **B** Damage electrical 20-70 (scaled), eye candy x2 | ✅ Electrical damage | Per-spell run | - |
| 34 | Disintegrate (5) | 50 fat, save constitution -5; **B** Damage electrical 30000-30000, eye candy; **C** Flag dontdraw on | ◐ Electrical damage 30000 kills; the resistance rule for "no disintegrate" exists | `ObjFlag` OF_DONTDRAW (object flags field) at the callback: the body stays visible | MA-1 |

### Mental (spells 35-39)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 35 | Charm (1) | 5 fat, maint 1@10, save willpower -5; **B** Flag charmed on, eye candy; **E** Flag charmed off, eye candy | ✗ Cost and eye candy only | `ObjFlag` charmed with its +30 reaction side effect | MA-4 |
| 36 | Stun (2) | 10 fat, save willpower -5; **B** eye candy; **E** Damage normal 0-0 (stun) | ✅ Stun flag in the END damage (life effect `Stun`), willpower save | Per-spell run | - |
| 37 | Drain Will (3) | 15 fat, maint 1@10, save willpower -5; **B** Effect 59 add x1, eye candy; **E** Effect 59 rem x1 | ✗ Cost and eye candy only | Effect 59 (WP -50 %) on an NPC: no NPC effect list | E1-d |
| 38 | Nightmare (4) | 20 fat, save willpower -5; **B** eye candy, Flag fleeing on | ✗ Cost and eye candy only | `ObjFlag` fleeing with the AI notification | MA-4 |
| 39 | Dominate Will (5) | 25 fat, maint 6@10, save willpower -5; **B** Flag mind_controlled on, eye candy; **E** Flag mind_controlled off | ✗ Cost and eye candy only | `ObjFlag` mind-controlled with follow and reaction side effects | MA-4 |

### Meta (spells 40-44)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 40 | Resist Magick (1) | 5 fat, maint 1@10; **B** Effect 56 add x1, eye candy x2; **E** Effect 56 rem x1, eye candy x3 | ✅ Effect 56 on the hero and NPCs, removed at END; it changes the Magick resistance the sheet shows, and counts in the magic save, which the original's does not (a bug of the game fixed in both modes, ARC-MAGIC-033, D6c; [MA-2 result](#effect-consumers-ma-2-arc-magic-027)) | The original's spell does nothing for the saves (RE2); fixed, no switch | - |
| 41 | Disperse Magick (2) | 10 fat; **B** eye candy, Dispel | ◐ Dispel (MA-3, ARC-MAGIC-034): the runs sourced by, aimed at or listing the target end, the stoned spell flag goes, a summoned critter is destroyed; eye candy | The perma-polymorph and a held portal or container (MA-8, MA-6) | - |
| 42 | Dweomer Shield (3) | 15 fat, maint 5@10; **B** Dispel, Flag anti_magic_shell on, eye candy x2; **E** Flag anti_magic_shell off, eye candy x3 | ✅ Dispel on its caster without ending its own run (MA-3, ARC-MAGIC-034), the anti-magic shell flag (MA-1) and the shell consumer | The same left parts as 41 | - |
| 43 | Bonds of Magick (4) | 35 fat, maint 5@10; **B** Flag bonds_magic on, eye candy x2; **E** Flag bonds_magic off, eye candy x3 | ✗ Cost only (the invocation check already reads the flag) | `ObjFlag` bonds of magick | MA-1 |
| 44 | Reflection Shield (5) | 30 fat, maint 5@10; **B** Flag full_reflection on, eye candy x2; **E** Flag full_reflection off, eye candy x3 | ✗ Cost only (the per-target stage already reads reflection) | `ObjFlag` full reflection | MA-1 |

### Morph (spells 45-49)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 45 | Hardened Hands (1) | 5 fat, maint 1@10; **B** Flag hardened_hands on, eye candy x2; **E** Flag hardened_hands off, eye candy x3 | ✗ Cost only | `ObjFlag` hardened hands; the resolver has the rule but nothing feeds it from flags | MA-1 |
| 46 | Weaken (2) | 15 fat, maint 2@10, save constitution -5; **B** Effect 154 add x25, Effect 326 add x5, eye candy x2; **E** Effect 154 rem x25, Effect 326 rem x5, eye candy x2 | ◐ Effects 154 (resistances) and 326 (strength -5) on an NPC target; the strength reaches its combat values, not its maximum hit points (MA-2) | Save and the NPC life facts under a strength change; nothing else | - |
| 47 | Shrink (3) | 20 fat, maint 2@10, save constitution -5; **B** Flag shrunk on, Effect 60 add x1, eye candy; **E** Flag shrunk off, Effect 60 rem x1, eye candy x2 | ✗ Cost only | `ObjFlag` shrunk, effect 60 on an NPC, shrunk render | MA-1, E1-d |
| 48 | Flesh to Stone (4) | 20 fat, dur 10 s, save constitution -5; **B** eye candy x2, Flag stoned on, Flag stoned off; **E** eye candy x2, Flag stoned off, Flag stoned on | ◐ Stoned flag on critters (hold, ARC-DEV-302), timed 10 s | Stone render side effect, and the reverse path on stone objects and portals | MA-6 |
| 49 | Polymorph (5) | 30 fat, maint 5@10, save constitution -5; **B** Flag polymorphed on, eye candy, Trait art 20, Effect 156 add x1; **E** Flag polymorphed off, Trait restore, Effect 156 rem x1 | ✗ Cost only | `Trait` (art 20), flag polymorphed, effect 156; MG10 is a technical bug under the owner rule | MA-1, MA-8, E1-d |

### Nature (spells 50-54)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 50 | Charm Beast (1) | 8 fat, save willpower -5; **B** Flag charmed on, eye candy | ✗ Cost and eye candy only | `ObjFlag` charmed on animals (a separate animal path in the original) | MA-4 |
| 51 | Entangle (2) | 5 fat, maint 2@10, save strength -1; **B** Flag entangled on, eye candy x2, Effect 62 add x1; **E** Flag entangled off, eye candy x3, Effect 62 rem x1 | ◐ Entangle flag, effect 62 on the hero, re-save on each hit, ends with the caster. **Smoke**: spell-end, ai-cast | Effect 62 (speed -5) on an NPC victim (the flag already holds); speed use unchecked (MA-2) | E1-d |
| 52 | Control Beast (3) | 15 fat, maint 2@10, save willpower -5; **B** Flag mind_controlled on, eye candy x2; **E** Flag mind_controlled off, eye candy x3 | ✗ Cost and eye candy only | `ObjFlag` mind-controlled on animals | MA-4 |
| 53 | Succour Beast (4) | 20 fat, maint 4@10; **B** Summon list 4, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only | `Summon` list 4 (aptitude thresholds), mind-control flag | E1-c, E1-e, MA-4 |
| 54 | Regenerate (5) | 25 fat, maint 5@10; **B** eye candy x2; **M** Heal normal 1-7 (scaled); **E** eye candy x3 | ✅ Heal on each MAINTAIN tick, party area | Per-spell run | - |

### Necromantic (black) (spells 55-59)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 55 | Harm (1) | 5 fat; **B** Damage normal 3-40 (scaled), eye candy | ✅ Damage normal 3-40, `Scaled`. **Smoke**: first-cast stage (by its data filter this is the one spell it picks; inferred) | None known | - |
| 56 | Conjure Spirit (2) | 10 fat, maint 3@10; **B** Flag spoken_with_dead on, eye candy x2; **E** Flag spoken_with_dead off, eye candy x3 | ✅ Spoken-with-dead flag on a corpse for the run's life. **Smoke**: spirit-talk | None known | - |
| 57 | Summon Undead (3) | 15 fat, maint 2@10; **B** Summon list 5, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only (B47) | `Summon` list 5 (undead by aptitude), mind-control flag | E1-c, E1-e, MA-4 |
| 58 | Create Undead (4) | 20 fat, maint 3@10; **B** Heal normal 0-0 (raise), Heal poison 32000-32000, eye candy, Flag undead on, Flag mind_controlled on; **E** Flag mind_controlled off, Damage normal 0-0 (kill), Flag undead off | ◐ Raise and poison cure on a corpse; the END damage kills it again | Undead and mind-controlled flags: the raised body is hostile and the flags it clears at END were never set | MA-4 |
| 59 | Quench Life (5) | 50 fat, save constitution -5; **B** eye candy, Flag paralyzed on; **C** Flag paralyzed off, Damage normal 50-100 (scaled) | ✅ Paralysis (flag works), 50-100 damage at the eye candy callback | Per-spell run (callback timing) | - |

### Necromantic (white) (spells 60-64)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 60 | Minor Healing (1) | 5 fat; **B** Heal normal 5-30 (scaled), eye candy | ✅ Heal normal 5-30, `Scaled` | Per-spell run | - |
| 61 | Halt Poison (2) | 8 fat; **B** Heal poison 50-50, eye candy | ✅ Heal poison 50 | Per-spell run | - |
| 62 | Major Healing (3) | 20 fat; **B** Heal normal 40-80 (scaled), eye candy | ✅ Heal 40-80 plus scars and cripples | Per-spell run | - |
| 63 | Sanctuary (4) | 15 fat, maint 5@10; **B** Flag enshrouded on, eye candy x2; **E** Flag enshrouded off, eye candy x3 | ✗ Cost and eye candy only (the hostility rule reads the flag) | `ObjFlag` enshrouded | MA-1 |
| 64 | Resurrect (5) | 50 fat; **B** eye candy; **C** Heal normal 0-0 (raise) | ✅ Heal with raise and full at the callback; the raise script runs no SCR (allows) | Raise-script hook (point 0x12) | - |

### Phantasm (spells 65-69)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 65 | Illuminate (1) | 3 fat, maint 1@10; **B** eye candy; **E** eye candy | ✅ Eye candy 3 added and removed (light as the eye candy provides it) | Whether the glow lights the scene is unverified | - |
| 66 | Flash (2) | 10 fat, dur 10 s, save constitution -5; **B** Flag blinded on; **E** Flag blinded off | ✗ Cost only | `ObjFlag` blinded (critter flags) with AI and combat effects | MA-4 |
| 67 | Blur Sight (3) | 15 fat, maint 2@10; **B** Effect 159 add x30; **E** Effect 159 rem x30 | ✅ Effect 159 (AC +1 x30) on the hero (area is self) | Per-spell run | - |
| 68 | Phantasmal Fiend (4) | 20 fat, maint 4@10, save willpower -5; **B** Summon proto 28464, Flag illusion on, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only | `Summon` critter (proto 28464), illusion and mind-control flags | E1-c, E1-e, MA-4, MA-1 |
| 69 | Invisibility (5) | 30 fat, maint 6@10; **B** Flag invisible on, eye candy; **E** Flag invisible off, eye candy | ✗ Cost and eye candy only (awareness already reads invisible) | `ObjFlag` invisible with its render side effects | MA-1 |

### Summoning (spells 70-74)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 70 | Plague of Insects (1) | 3 fat, maint 1@10; **B** eye candy x2, Effect 62 add x1; **E** eye candy; **EC** eye candy x2, Effect 62 rem x1 | ✗ Eye candy only | Effect 62 on an NPC | E1-d |
| 71 | Orcish Champion (2) | 10 fat, maint 2@10; **B** Summon proto 28443, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only | `Summon` critter (proto 28443), mind-control flag | E1-c, E1-e, MA-4 |
| 72 | Guardian Ogre (3) | 15 fat, maint 3@10; **B** Summon proto 17286, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only | `Summon` critter (proto 17286), mind-control flag | E1-c, E1-e, MA-4 |
| 73 | Hellgate (4) | 30 fat, maint 6@10; **B** Summon proto 28318, Flag mind_controlled on, eye candy; **E** eye candy; **C** Flag dontdraw off; **EC** Destroy | ✗ Cost only | `Summon` critter (proto 28318), mind-control flag | E1-c, E1-e, MA-4 |
| 74 | Familiar (5) | 60 fat; **B** Summon list 3, Flag familiar on x2, Flag mind_controlled on, eye candy; **C** Flag dontdraw off | ✗ Cost only | `Summon` list 3 (familiars by aptitude), familiar flags on caster and summoned | E1-c, E1-e, MA-4, MA-1 |

### Temporal (spells 75-79)

| # | Spell | In the records (installed data) | Today | Missing | Needs |
| --- | --- | --- | --- | --- | --- |
| 75 | Magelock (1) | 5 fat, maint 1@10; **B** Flag magically_held on, eye candy x2; **E** Flag magically_held off, eye candy x3 | ✗ Cost and eye candy only | `ObjFlag` magically-held on portals | MA-6 |
| 76 | Congeal Time (2) | 10 fat, maint 2@10, save willpower -5; **B** Effect 57 add x1, eye candy x2; **E** Effect 57 rem x1, eye candy x3 | ◐ Effect 57 (speed -50 %) on NPCs in the area; the NPC's speed reads under its list: turn-based action points and attack pause (MA-2). Walking is not speed-driven in the original (XR) | Per-spell run; the host's round must read the list (the viewer does) | - |
| 77 | Hasten (3) | 20 fat, maint 3@10; **B** Effect 58 add x1, eye candy x2; **E** Effect 58 rem x1, eye candy x3 | ◐ Effect 58 (speed +100 %) on the hero and companions (MA-2); speed reaches the action points and the attack pause, not walking (XR) | Per-spell run | - |
| 78 | Stasis (4) | 25 fat, maint 4@10, save willpower -10; **B** Flag paralyzed on, eye candy x2; **E** Flag paralyzed off, eye candy x3 | ✅ Paralysis flag (hold) on a critter, maintained | Per-spell run | - |
| 79 | Tempus Fugit (5) | 40 fat, maint 5@10; **B** Flag tempus_fugit on, eye candy; **E** Flag tempus_fugit off, eye candy x2 | ✗ Cost and eye candy only | `ObjFlag` Tempus Fugit; no code reads it; in the original it gives +10 speed to its holders and -10 to every other critter while the player holds it ([MA-R](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)) | MA-9, MA-1 |

## By college

| School | Spells | ✅ | ◐ | ✗ | Blocked by | Whole school works after |
| --- | --- | --- | --- | --- | --- | --- |
| Conveyance | 0-4 | 1 | 1 | 3 | MA-6, MA-0, MA-5 | wave 2 |
| Divination | 5-9 | 0 | 0 | 5 | MA-1, MA-7, E6 | wave 4 |
| Air | 10-14 | 1 | 3 | 1 | E1-d, E1-b, MA-1, MA-2, MA-8, E1-c, E1-e, MA-4 | wave 4 |
| Earth | 15-19 | 1 | 2 | 2 | E1-d, E1-b, MA-1, MA-2, MA-8, E1-c, E1-e, MA-4 | wave 4 |
| Fire | 20-24 | 0 | 2 | 3 | E1-d, E1-b, MA-1, MA-2, MA-8, E1-c, E1-e, MA-4 | wave 4 |
| Water | 25-29 | 0 | 3 | 2 | E1-d, E1-b, MA-1, MA-2, MA-8, E1-c, E1-e, MA-4 | wave 4 |
| Force | 30-34 | 2 | 2 | 1 | MA-1, MA-2, E1-d, E1-b | wave 2 |
| Mental | 35-39 | 1 | 0 | 4 | MA-4, E1-d | wave 3 |
| Meta | 40-44 | 0 | 0 | 5 | MA-2, E1-d, MA-3, MA-1 | wave 2 |
| Morph | 45-49 | 0 | 1 | 4 | MA-1, E1-d, MA-2, MA-6, MA-8 | wave 3 |
| Nature | 50-54 | 1 | 1 | 3 | MA-4, E1-d, E1-c, E1-e | wave 4 |
| Necromantic (black) | 55-59 | 3 | 1 | 1 | E1-c, E1-e, MA-4 | wave 4 |
| Necromantic (white) | 60-64 | 4 | 0 | 1 | MA-1 | wave 1 |
| Phantasm | 65-69 | 2 | 0 | 3 | MA-4, E1-c, E1-e, MA-1 | wave 4 |
| Summoning | 70-74 | 0 | 0 | 5 | E1-d, E1-c, E1-e, MA-4, MA-1 | wave 4 |
| Temporal | 75-79 | 1 | 1 | 3 | MA-6, E1-d, MA-2, MA-9, MA-1 | wave 2 |

"Whole college works after wave N" counts the slices of the [work order](#work-order) only; the work that is not a spell component (hero and companion knockback for Unseen Force (2), the raise script, a new smoke) comes on top; spell 2 is counted when MA-0 has confirmed what it pushes. Spell 8 is counted when its detecting flags and trap spotting exist; its skill change (effect 157) waits for the skills lane.

## Work order

### Slices

Labels `E1-*`, `E4-*` and `MT*` are the existing plan's; `MA-*` are this audit's. Sizes as in the missing-systems plan (S about 200 lines, M one PR with a smoke stage, L more than one slice).
Every slice reads the functions it implements in the executable twice and adds ledger rows (the [rule of the magic plan](magic-implementation-plan.md#checking-against-the-original)), updates its coverage rows and adds its spells to the
census (MA-0) in the same PR.

| Slice | Content | Unlocks (spells) | Size | Needs | Lane and files | FR |
| --- | --- | --- | --- | --- | --- | --- |
| **E1-a** DEV-347 (merged, #924) | Created scenery and eye-candy objects, schema 85 | none by itself | - | - | directory, session | FR-MAGIC-008 |
| **E1-d** DEV-348 (in review) | Effect list and spell flags for every critter | 10, 15, 20, 25, 37, 51, 70 (76 also needs MA-2) once the Effect component's NPC branch is hooked up (check the merged diff) | - | - | `GameSession*` | FR-CHAR-022, FR-MAGIC-007 |
| **MA-0** Spell census, ARC-DEV-356 ([result](#spell-census-ma-0)) | An opt-in `RealData` test (`DAT_ROOT`) that applies every record's BEGIN, MAINTAIN, END and callbacks (records 0..139) to a synthetic target in the cast host and asserts the exact set of `ComponentNotReady` components per record (the per-spell hero cast for the ✅ rows was left out, see the result). It is the living matrix: each slice below edits its expected set | verifies 0, 2, 12, 16, 31, 33, 36, 54, 59-62, 64, 65, 67, 78 | M | - | tests plus one test host; no production file | FR-MAGIC-007 |
| **MA-5** Movement (E4-e) | `Movement` to a target tile and the teleport tile; no-teleport areas ([FR-TRAVEL-005](../requirements/world-travel-runtime.md#fr-travel-005)); followers after a teleport; Teleportation as a map change through the session path of MIG11 | 3, 4 | M-L | MA-1 (dispatch table) | new partial file; map transfer | FR-MAGIC-009, FR-TRAVEL-005 |
| **MA-R** Executable reads (ARC-DEV-357, done: [ledger XR1-XR15](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)) | One Mac read (docs and ledger rows only): `Identify` on a critter, a container and an item (what each shows, what it writes); Tempus Fugit; the consumers of the illusion and familiar flags and of detecting traps; what magically-held does to a door; whether the speed stat reaches walking and action points; the weapon drop of the Disarm flag | 5-9, 75, 79, 0 | M | - | docs and ledger only | FR-MAGIC-007 |
| **MA-1** Spell-flag writer (E4-a; **done**, ARC-DEV-358, 2026-11-11, [table](../world/spell-cast-host.md#the-objflag-component-and-the-component-table-arc-dev-358-ma-1)) | First splits the if-chain of `SessionSpellCaster.Apply` into a dispatch table with one partial file per component group; then generalises `ObjectFlag` from the four whitelisted bits to every `obj_f_spell_flags` and object-flag bit with the reference count it already has; per-flag side effects only where read (invisible render flags, shielded, shrunk, hardened hands and the body flags feed the unarmed rule); NPC targets once DEV-348 lands | 5, 43, 44, 45, 63, 69, 34, 47; part of 8, 30, 42 | M | - (NPC targets: DEV-348) | `SessionSpellCaster.Apply`, `SessionSpellCasterObjectFlags` | FR-MAGIC-007 |
| **MA-6** Locks and holds on objects | `ObjFlag` on portal and container flags through the lock state pick locks already writes: Unlocking Cantrip, Magelock, and Flesh to Stone's reverse path on stone portals | 1, 75, 48 | S-M | MA-1 | door and container state | FR-MAGIC-007, FR-AI-003 |
| **MA-3** Dispel (E4-c, narrowed; **done**, ARC-MAGIC-034, 2026-11-11, [result](#dispel-ma-3-arc-magic-034)) | Dispel: interrupt the runs sourced by, aimed at or listing the target, clear held, stoned and perma-polymorph, destroy a summoned target (exe read: MT15 lists the 0x451AF0 to 0x451C40 group for Dispel and Destroy together, so the Dispel function is confirmed first). The other control components are the technology lane's | 41, 42 | M | MA-1 | Dispel partial file, run host | FR-MAGIC-006, FR-MAGIC-007 |
| **E1-b** Summon of scenery | The seven scenery summons: cloud, fog, ice cloud, walls, the flash and Wall of Fire (burning goal 77 exists); END and Dispel remove them; stage per group ([hand-off](summon-objects-res-101.md#e1-b-the-summon-component-for-objects-needs-e1-a)) | 11, 17, 21, 22, 26, 27, 32 | M | E1-a (done), MA-1 | Summon partial file | FR-MAGIC-008 |
| **MA-2** Effect consumers, ARC-MAGIC-027 ([result](#effect-consumers-ma-2-arc-magic-027)) | Resistance changes of effects (damage, fire, electrical, poison, magic) reach the combat resolver and the magic save for the hero and, after DEV-348, NPCs; check that speed reaches walking and action points; skills stay with the skills lane | 40, 30, 46, 13, 18, 23, 28 (the resistance half), 77, 76 | M | E1-d (NPC part) | `GameSessionEffects`, resolver | FR-CHAR-022, FR-COMBAT-008, FR-MAGIC-010 |
| **E1-c** Created critters | A critter from a prototype on a tile, hosted wherever it stands, saved; action 52 for critters | prerequisite | L | E1-a (done) | directory, AI host (no cast-host file) | FR-MAGIC-008, FR-AI-004 |
| **MA-4** Social and AI flags (E4-b, MT14) | Charm (+30 / -30 through the reaction effects, animals on a separate path), mind control (follow, no flee, -50 for a PC on a non-summoned NPC, disband at END), undead, fleeing, blinded as AI and combat notifications; the illusion and familiar flags | 35, 38, 39, 50, 52, 66, 58; the flag half of 14, 19, 24, 29, 53, 57, 68, 71-74 | M-L | MA-1, E1-d | reaction, AI notices | FR-SOCIAL-001, FR-SOCIAL-003, FR-AI-002 |
| **MA-8** Trait, body spells, Polymorph (E4-f, MT18) | `Trait`: art and palette, drop what the new art cannot wield, restore; MG10 as a technical bug under the owner rule; Body spells with their flags and effects | 13, 18, 23, 28, 49 | L | MA-1, MA-2, E1-d | render, equipment | FR-MAGIC-007 |
| **MA-7** Identify, ARC-MAGIC-032 ([result](#identify-ma-7-arc-magic-032)) (E4-d, part of E8) | The session state of identification for items, containers and critters and the answer of the spell; the identify window stays with E8 | 6, 7, 9 | M-L | MA-R | session, UI | FR-MAGIC-007, FR-MAGIC-012 |
| **E1-e** Summon of critters | Prototype or list pick, summoned flags, palette, faction (MG8), run ends when the summon dies, summoned follower; the familiar hidden with the party on the world map (XR14 (b), decided by the owner: [Decisions](#decisions-for-the-owner)) | 14, 19, 24, 29, 53, 57, 68, 71, 72, 73, 74 | L | E1-c, MA-4 | Summon partial file | FR-MAGIC-008 |
| **E6** Trap spotting | The spot roll and the detecting-traps flag (spell 8's second flag) | 8 | M | MA-1 | traps | FR-ITEM-016 |
| **MA-9** Tempus Fugit | Implement what MA-R reads; the XR14 (a) fix decided by the owner ([Decisions](#decisions-for-the-owner)) | 79 | S-M | MA-R, MA-1 | to be read | FR-MAGIC-007 |
| **MA-10** Raise script hook | The resurrect script (point 0x12) through the SCR dispatcher | edge of 58, 64 | S | the SCR dispatcher (E5) | scripts | FR-MAGIC-007 |
| **E1-f** AI summoning and AI parity | The AI's summon list and one pass of `ai-cast` over every AI priority of the 80 (an NPC casting a spell must act like the hero's cast) | 60 spells carry an AI priority | M | E1-e | AI host | FR-AI-002 |

### Waves, what is parallel and what is not

| Wave | Slices | Starts when | Files shared with another slice of the wave? | Working spells after (of 80) |
| --- | --- | --- | --- | --- |
| 0 | DEV-348 (E1-d) | in review now | - | 24 (+7: 10, 15, 20, 25, 37, 51, 70) |
| 1 | MA-1, MA-0, MA-R, E1-c | now | MA-1 is in the cast host; MA-0 in tests; MA-R in docs; E1-c in the directory and AI host: four lanes | 33 (+9: 2, 5, 34, 43, 44, 45, 47, 63, 69) |
| 2 | MA-5, MA-6, MA-3, E1-b, MA-2 beside each other; MA-9 | MA-1 merged | After MA-1's dispatch table each owns one partial file; MA-2 is in `GameSession*` and the resolver | 53 (+20) |
| 3 | MA-4, MA-8, MA-7 | MA-1 and DEV-348 for MA-4; MA-2 for MA-8; MA-R for MA-7 | MA-4 reaction and AI notices; MA-8 render and equipment; MA-7 session and UI: three lanes | 68 (+15) |
| 4 | E1-e, E6, MA-10, E1-f | E1-c and MA-4 for E1-e | E1-e and E1-f in the Summon and AI files: one after the other | 80 (+12) |

Waves are dependency layers, not dates. A slice may sit later than its dependencies force (MA-4, MA-7, E6) to keep three or four lanes of different files running at once and to do the cheapest spells first. The serial chains are MA-1 before every other slice that adds a component (they share the dispatch until it is split), and E1-c then E1-e then E1-f. Two things come out of the plan's order:
the whole of **MA-1 then MA-6 and MA-3** is smaller than E4-a to E4-c as the plan wrote them, because the colleges need Dispel only; and **MA-2 (effect consumers) is new** and gates the Body spells, Resist Magick and the
resistance half of Shield of Protection even after every flag is written.

### Where each college ends up

- **Cheapest wins first:** Necromantic (white) is four spells already; Sanctuary (63) is MA-1. Force finishes with E1-b (Wall of Force) and MA-1 (Disintegrate's vanishing body); Conveyance with MA-5 and MA-6; Meta with MA-1, MA-2 and MA-3; Temporal with MA-1, MA-2, MA-6 and MA-9.
- **Longest:** Summoning (all five wait on created critters and mind control), the four elemental colleges (Wall, Fireflash, Elemental, Body), Nature (animals, charm and mind control, Succour Beast) and Divination (Identify).
- **Reported bugs on this path:** B47 (Fireflash, Wall of Fire, Summon Undead) is E1-b for 21 and 22 and E1-e for 57; the Entangle chain (51) already runs.

## Spell census (MA-0)

ARC-DEV-356 builds MA-0 as the opt-in test `SpellCensusRealDataTests` in `tests/Arcanum.Session.Tests` (`DAT_ROOT`, row in the [opt-in table](../workflow/toolchain.md#opt-in-original-data), owner decision D4 taken
by its recommendation (a) while it is open). For every record 0..139 it reads the installed records and `rules/effect.mes` like the original, applies each component of every action
(BEGIN, MAINTAIN, END, CALLBACK, END_CALLBACK) through the cast host's `Apply` to two synthetic targets in turn (the hero, and an NPC of the map), each on a fresh session, and compares the
set of components the host logs as `ComponentNotReady` with the table in the test. A change prints the new rows to paste. Each later slice edits that table in its own pull request.

What it checks and what it does not:

- It checks the **dispatch**: whether the host takes a component of a record on a PC and on an NPC. It runs no invocation check, target area, save, resistance or run action, so a component it
  takes may still be partial or inert (the resistance half of the effects, MA-2; the AGoal pushes of the hero), and a not-ready component on these two targets may be ready on another
  object (Destroy takes lying items only). A session refusal of a taken component's write (`ObjectFlagNotSaved`, `EffectNotSaved`) counts as a change of the table too. The per-spell hero cast the slice row names is left out: a cast through the run rules depends on the save rolls and the event pass,
  and checking it per spell is the smoke's and the per-slice tests' job. A cast from the hero to these targets was tried while building the test. It started for all 80 college spells, which shows only
  that the invocation check admits these targets.
- **Result on `origin/main` after ARC-DEV-353 (2026-11-11):** 18 of the 80 college spells leave no component not ready on either target: 0, 2, 12, 16, 31, 33, 36, 48, 54, 55, 56, 59-62, 64,
  65 and 78. Twelve more leave only the NPC's Effect (the hero has the only effect list until DEV-348): 10, 15, 20, 25, 37, 40, 46, 51, 67, 70, 76 and 77.
- **Against the matrix:** the census agrees with the 17 ✅ rows except 67 (Effect, hero only), and adds 2 and 48, which the matrix keeps at ◐ for work outside the dispatch (the hero and
  companion push; the stone portal and its render side). The ✗ rows 37, 40, 46 and 76 are taken by the host on the hero but change nothing that is modelled (resistances, MA-2) or are unchecked
  (speed, MA-2). No row disagrees in the other direction.
- **Records 80..139:** the same gaps, and three not-ready cases the colleges do not have: `Recharge` (103), `NoOp` (129, 130) and an AGoal goal other than 43 and 44 (89). Record 85
  adds effect 145 with the cause injury (7), and the session refuses that cause from a spell (`EffectNotSaved` on the hero); whether the original's effect add takes it is left to MA-2.
- **After ARC-MAGIC-027 (2026-11-11):** the Effect component takes an NPC's list, so every NPC `Effect` entry left the table (the college spells 10, 15, 20, 25, 37, 40, 46, 51, 67, 70, 76, 77 now leave nothing on either target, and so do records 85, 93-96, 104, 112, 114 and 138 outside the colleges on the NPC; the rest keep their ObjFlag, Trait, Summon, Destroy, Identify, Movement or Recharge rows).
- **Lanes that will change it:** E1-b (ARC-DEV-352, #941) changes the Summon and Destroy rows of the scenery summons; DEV-348 the NPC's Effect rows; MA-1 the ObjFlag rows. The slice that merges
  second re-runs the census and updates the table.
- **After ARC-DEV-352 (#941, 2026-11-11):** the Summon component is taken on both targets of every record. Its census targets are objects, and an object entry summons nothing in
  the original (TA9: only a tile entry has a tile), so the census can no longer tell a scenery summon from a critter summon. A critter summon on a tile entry still logs `ComponentNotReady`
  (E1-e); its unit tests, not the census, hold that. The Destroy rows stay: the census targets are a critter and the hero, not a summoned object.

## ObjFlag writer (MA-1, ARC-DEV-358)

Done on 2026-11-11 ([host contract](../world/spell-cast-host.md#the-objflag-component-and-the-component-table-arc-dev-358-ma-1), ledger [OB1 to OB5](ledger/spell-runs.md#objflag-component-of-the-cast-host-arc-dev-358)).

- **The dispatch is a table** (`SessionSpellCasterComponents.cs`): the next slices (MA-3 Dispel, MA-5 Movement, MA-6 locks, MA-8 Trait, E1-c and E1-e Summon) each add a line and their own partial file.
- **Every bit of the spell, object and critter flags is written on the hero and a mobile critter**, with the reference count and the original's side effects (invisible and translucent, shrunk, water walking, stoned, don't-draw with the paralysed bit).
  The session keeps the object flags as a third written field (schema 88). Eleven college spells (5, 8, 30, 34, 43, 44, 45, 47, 63, 69, 79) leave no component not ready on a PC and an NPC target any more (census, below), and the ObjFlag line left the not-ready set of 11, 13, 17, 18, 21, 23, 26, 27, 28, 32, 42 and 49.
- **Left, by name.** The social and AI flags (mind controlled and charmed with their reactions and follow; undead, fleeing and blinded: MA-4); the portal and container locks (MA-6); the item identified bit (MA-7);
  the drowning damage of the water-walking clear and the function the don't-draw clear calls when critter flags 2 have bit 0x2 (F0 rows OB2 and OB3, MA-8); the viewer's draw of a translucent, shrunk or hidden critter
  (the object flags are written and read by the AI host and the target rules; the render does not read them yet); the Trait component (MA-8) for the Body spells and Polymorph.
- **The unarmed body bonus is fed** from the flags a spell wrote: Body of Earth, Fire, Water and Hardened Hands reach the hero's and every NPC's unarmed damage (OB5).
- **Census (D4a: stays a permanent opt-in test, each slice edits its table).** After MA-1 the census table lost the rows of 5, 8, 30, 34, 43, 44, 45, 47, 63, 69 and 79 (college) and 83, 84, 105 and 134 (others); 11, 13, 17, 18, 21, 23, 26, 27, 28, 32 and 49 keep one component each (Destroy or Trait); after MA-3 records 41 and 42 left the table too. The census checks the dispatch only: a spell it lists as taken may still be partial (the flag's reader, the render).

## Dispel (MA-3, ARC-MAGIC-034)

Slice MA-3 (owner decisions D1–D6 of 2026-11-11), on the executable reads [DP1–DP3](ledger/spell-runs.md#dispel-component-arc-magic-034) (the Dispel function 0x451C40 and its component 0x451B90 read whole; MT15's
Dispel/Destroy address mix-up was already settled by DS1). Host: [Dispel](../world/spell-cast-host.md#dispel-arc-magic-034).

- **Built.** The Dispel component of spells 41 and 42: every non-technological run but the dispelling one that the target is the source of, or that is aimed at it or lists it as summoned, ends by event; a hero's or
  critter's stoned spell flag is cleared by a plain write; a summoned created critter is destroyed. Dweomer Shield's own run is the processed one and is left alone.
- **Found.** The run's object block at +0xA0 is its **target**, not its other object (0x455C30, DP3): the object-gone endings (0x459740) of the cast host ended the wrong runs and were fixed in the same slice.
- **Left, by name.** The perma-polymorph with its Trait restore (MA-8; the session keeps no critter flags 2), the magically held bit of a portal or container (MA-6, with Magelock), the other object list of a run (+0x130),
  and the original's stoned object flag, which Dispel leaves set (the spell flag alone is cleared).

## Effect consumers (MA-2, ARC-MAGIC-027)

Done on 2026-11-11 ([contract](../world/durable-effects-runtime.md#consumers-of-resistance-and-stat-changes-arc-magic-027), ledger [RE1 to RE3](ledger/magic.md#effect-consumers-arc-magic-027)).

- **Resistance changes reach the combat resolver.** The effective resistance (0x43D6D0) puts the effects after the armour and the poison bonus and before the clamp to 0..95 (RE1). The hero's durable effects and any NPC's list (the
  session's, else the loaded one under the host's rules) feed the `Combatant` of every attack, spell damage on the hero and the hero's poison tick. The NPC's stat changes (speed, strength) reach its combat values, its attack
  pause and the round's action points.
- **The Effect component takes an NPC target** (`SessionSpellCasterEffects.cs`, the one `SessionSpellCaster*` file outside PR #941), so the census lost every NPC `Effect` row.
- **The magic save does not read the effects in the original (RE2).** The original's save reads the stored magic resistance, so Resist Magick (spell 40) and the magic parts of effects 51, 52, 55, 154 and 155 show on the sheet and roll in no save.
  MA-2 followed it (O9, both modes); ARC-MAGIC-033 fixed it (below). **Owner question (new, D6): should Resist Magick work?** (a) as the original: no effect on saves; (b) a bug of the game fixed in Steamwoven only, with an "Original" setting; (c) fixed in both
  modes. Recommendation was (b): a spell that does nothing is a bug of the game a player meets, which the owner's bug rule (2026-10-28, ledger NW4) sends to an "Original" setting. **Decided 2026-11-11: (c), fixed in both modes with no "Original" setting. Done by ARC-MAGIC-033 ([ledger RS11 to RS13](ledger/magic.md#resist-magick-in-the-magic-save-arc-magic-033)): the save reads the stored resistance under the target's `resistmagic` changes, for the hero and every NPC, and so does the AI's fail-chance predictor.**
- **Speed.** The hero's speed already reached the attack pause and the turn-based action points; an NPC's does now. Walking is not speed-driven in the original (MA-R, XR), so nothing was added there.
- **Left over, by name.** (1) `SessionSpellCaster.Combatant` and its stat getter (`SessionSpellCaster.cs` lines 550 and 1182) read an NPC without its list, so spell damage on an NPC ignores a Shield of Protection or Body spell it holds and a spell's save reads
  its unchanged stats: both were one-line changes (`_owner.WithNpcEffects(npc.Combat)`), **made by MA-1 (ARC-DEV-358)**, which covers the fire half (the burn's `Combatant` goes through the same call). (2) An NPC's life facts do not read its effect-changed strength, constitution or resistance (its maximum hit points
  and the poison tick); the list's own maxima apply. (3) The character sheet's resistance lines do not show effect changes. (4) The hero's innate (race, gender) effects have no resistance changes in the session.
  (5) Per-spell runs of 30, 46, 76 and 77 are not done: no smoke stage names them.

## Identify (MA-7, ARC-MAGIC-032)

Done on 2026-11-11, by owner decision D5(a): magic builds the state and a text answer, the identify window is E8's ([contract](../world/spell-cast-host.md#identify-arc-magic-032), ledger [XR1 to XR5](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)).

- **The original's Identify writes nothing.** It shows a window or a description to the casting local player only (XR1), so an NPC caster's Identify has no effect. The host now takes the component and, for the hero, logs one typed answer by the target's object type:
  `IdentifyCritter` (the read-only NPC sheet, XR2), `IdentifyContainer` (the container's inventory in mode 4, no lock test, XR3), `IdentifyItem` (the item description, XR4); the viewer shows each as a line in the message log, E8 will read the same events and open the windows.
- **Divine Magick's identified flag is the session's identification mark** (`MarkItemIdentified`), the one a barter window already sets and saves, so there is no schema step. Every reader of the mark now sees it: the item name and window lines, the hotkey bar, the barter worth and
  the AI's item rule did already; the weapon's spell pane (slots, gauge, weapon button) now reads it too, which also fixes the pane of an item a barter window identified.
- **The census** loses `Identify` on records 6, 7 and 9; record 9 keeps its `ObjFlag` on the census's critter targets (its line takes an item only).
- **Left over, by name.** (1) Nothing picks an inventory item as a spell target (the inventory bit of the spell UI, `Tgt_Obj_Inven` in the target rules): spell 9 cannot be cast from the viewer; that is E8's pick. (2) The three windows (the NPC sheet for a spell, the container's inventory in mode 4, the item description). (3) What a press or release may move in inventory mode 4 (not read in MA-R, XR3). (4) A clear of the identified flag stays "not ready" (no installed record has one); an item whose own data has the flag stays identified whatever the mark says.

## Decisions for the owner

| # | Question | Options | Recommendation and decision |
| --- | --- | --- | --- |
| D1 | Work by component (flags, effects, summon...) as above and report by college, or finish one college at a time? | (a) component waves; (b) one college at a time, Necromantic (white), then Force, Meta, Conveyance, Temporal; (c) mixed: component waves, but wave 2 ordered so Force, Meta and Conveyance close first | (c): it is the same slices, ordered by what closes a college, so the report stays readable **Decided: owner chose (a), 2026-11-11 (waves by mechanic; the report stays by college).** |
| D2 | While a spell has missing components, does the spell pane keep offering it with the "not ready" line (today), hide it, or refuse the cast before payment? | (a) keep (today); (b) hide spells with an unbuilt component from the pane; (c) refuse before payment | (a): hiding hides progress and (c) changes the cast flow in a way no record describes; the line is already shown **Decided: owner chose (a), 2026-11-11.** |
| D3 | Teleportation scope: one slice, or split? | (a) one slice (MA-5) with tile movement, the world-map teleport, the no-teleport areas and followers; (b) split MA-5: tile movement first (spell 3), the rest second | (b): spell 3 needs no area data; the bans and the followers need the FR-TRAVEL-005 data **Decided: owner chose (b), 2026-11-11: MA-5 starts with tile movement (spell 3).** |
| D4 | Keep the census (MA-0) as a permanent opt-in real-data check in the toolchain table? | (a) yes; (b) one-off script, not kept | (a): it is the only way the matrix stays true; it adds one row to the opt-in table and runs only with `DAT_ROOT` **Decided: owner chose (a), 2026-11-11: MA-0 stays a permanent opt-in check.** |
| D5 | Identify (spells 6, 7, 9) needs a window the original has and the viewer does not: inside the magic lane or inside E8? | (a) magic lane builds session state and a text answer, E8 the window; (b) the whole thing in E8; (c) both in the magic lane | (a) **Decided: owner chose (a), 2026-11-11.** |
| D6 | Resist Magick (40) does nothing for the saves in the original (RE2). Fix it? | (a) as the original; (b) fixed in Steamwoven only, "Original" setting; (c) fixed in both modes | (b) **Decided: owner chose (c), 2026-11-11: fixed in both modes, no "Original" setting.** |

**Decided by the owner (2026-11-11): the two XR14 anomalies are bugs of the game, fixed in both modes with no switch** (the owner's bug rule of 2026-10-28: no "Original"/"Steamwoven" (D8) setting for either):

| XR14 | The original | Steamwoven, both modes | Slice |
| --- | --- | --- | --- |
| (a) Tempus Fugit | Speed changes only while the **local player** holds the spell flag ([XR6](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)); an NPC's cast changes nobody's speed unless the player holds it too | An NPC's Tempus Fugit takes effect without the player holding it. MA-9 states the exact rule (who gains, who is slowed) in its PR and records it as a difference from the original | MA-9 |
| (b) Familiar on the world map | The travel hide tests the caster-side `OSF_FAMILIAR` on the creature, which carries `ONF_FAMILIAR`, so the familiar stays shown ([XR10](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)) | The familiar is hidden and shown with the party like the PC and the non-summoned followers | E1-e |

Ledger row XR14 still reads "owner question": the ledger file is in open PR #941, so each slice updates XR14, adds the difference row next to XR6 or XR10 and updates the requirement's coverage row.

The 60 records outside the colleges (80..139, used by monsters and items) share every gap above; MA-0 covers them in its census at no extra cost, and a per-spell matrix for them can follow if wanted.

## Not read, not verified

- Whether the weapon-drop damage flag of Disarm (0) drops the weapon in a run (the original does, with a zero amount: [XR15](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)), whether Unseen Force (2) pushes non-critter objects, whether Illuminate (65) lights the scene, whether the maintained damage of the clouds (11, 27) hits the same
  targets as the BEGIN: all marked ✅ or ◐ by code, none run. MA-0 decides them.
- What Identify shows for a critter, what Tempus Fugit does, what the illusion and familiar flags change: read by MA-R ([ledger XR1-XR15](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)); left open there: what inventory mode 4 lets the player move, what happens on arrival to a summoned follower the travel hide left shown, whether a familiar's death reaches the follower removal, and two owner questions (XR14), decided by the owner on 2026-11-11 (both fixed in both modes, [Decisions](#decisions-for-the-owner)).
- DEV-348 is described from its thread status and the plan, not from its diff.
- The AI's use of each spell was not audited beyond the priorities in the records (60 of the 80 have one); the AI casts what the cast host can run, so a component that is not ready is not ready for NPCs either.
- Records 80..139 and the technology records (140..222) are outside the per-spell matrix.
