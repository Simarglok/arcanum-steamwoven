# What the original saves of actions in flight: casts, missiles, projectiles and the hero's runs (ARC-RES-096)

Status: **Research**, work item ARC-RES-096, 2026-11-08. Docs only; no code changes, no checks run.
Question (owner, MIG14 tails): MIG14 closed at S5 with the cast in progress, the missiles and
projectiles in flight and the hero's runs left out of the save
([saved runs](../architecture/saved-runs.md), S6 cancelled 2026-11-07). How does the original
behave: does its save keep them, and what happens on load?

Evidence: one reading (F1) of the retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`,
re-hashed for this reading), static, per the [original executable check](original-executable-check.md);
behaviour in prose only. It builds on rows PS11, PS12, PS15, PS18, PS20, PS26, PS30 and PS31 of the
[original's persistence](../architecture/original-persistence.md) and GS1–GS19 of the
[original's goals](../architecture/original-goals.md); it adds the rows SV1–SV8 below. Rows marked
**(inference)** were not read to the end.

## Answer

The original does **not** treat any of the three as special. Its save is a dump of tables and
objects, with no test for animation, casting, movement, missiles or combat outside a turn-based
round. A save made in the middle of a cast, with a missile in the air or with the hero walking
keeps all of it, and the load continues it:

| Case | Saved? | After the load |
|---|---|---|
| (a) A cast in progress | Yes: the caster's animation run with its casting goals, the spell-run record and the queued Anim and MagicTech events | The cast carries on from the saved step and releases (**inference** from the matching slots). Lost: the cast sound, two unidentified fields of the spell-run record, and the armed target cursor |
| (b) Missiles and projectiles in flight | Yes: they are ordinary objects created at run time, written whole, with their flight runs and events | They keep flying. Hit or miss was decided at launch and is stored in the projectile's fields; the damage is computed at impact |
| (c) The hero's runs (walk, run, attack, throw, pick, skills) | Yes: no filter by owner, so the hero's run is saved like any other | The hero resumes from the saved node. A walk driven by a held mouse button is not renewed (the 250 ms repeat is input state) |

## Rows

| Row | Address | Behaviour | Level |
|---|---|---|---|
| SV1 | save entry 0x403030; callers 0x401851 (F7), 0x5437A9 (save window), 0x55AE32 (export) | The save entry itself tests no game state. **F7** refuses only when the hero is dead or missing, in a network game, and in a turn-based round when the acting critter is not the hero (message 0x13C9); an open interface window is closed first and the save goes on. It then pops the interface-mode stack twice and saves to the slot "SlotAuto". The **save window** has no gate of its own; the gate is how the Esc menu opens: a dialog running, the window held in 0x66D864 open, a network game or a round that is not the hero's turn open the menu in mode 3 (**inference:** the menu without Save; 0x541180 not read). The **export** key saves a temporary slot under the same turn rule and loads it straight back. No test anywhere for animation runs, casting, spell runs, movement, missiles or combat mode outside a round; the busy test 0x44E8C0 has six callers, all in party orders, none on a save path | F1 |
| SV2 | Anim save 0x421EA0 → 0x4221C0 → 0x422350 goal writer; MagicTech save 0x44F250 → 0x44F3C0 | The only filter of the animation save is the in-use bit of the slot: no owner type, no player-character filter, no goal filter, and the carry flag of the descriptor table 0x5B03D0 is not used (that table serves the teleport only, PS26). The spell-run save filters only on the record's in-use bit | F1 (confirms PS15) |
| SV3 | menu load 0x5432B0, reset 0x541710, Map reset hook 0x40EBA0, sweep 0x424250, loader 0x403410, Anim load 0x4224E0, recount 0x423E60 | **Order.** The menu load closes the menu, checks the version, runs the full reset (the Map reset hook there ends the magic-tech state and interrupts every animation run at priority 6), destroys the default hero, then the loader reads the modules in table order: Map, MagicTech, Combat, TimeEvent, Anim. So the sweep that ends every run comes **before** the saved runs are read and does not touch them. After the loader only a name copy and a message follow: nothing interrupts runs or the hero. The Anim load ends with a recount that frees any run whose goal type is outside the 87 and logs a count mismatch | F1 (confirms PS30) |
| SV4 | casting start 0x455C30, slot take 0x455740, MagicTech load 0x44F540 → 0x44F620 | The casting start takes a spell-run slot (record offset 0 holds the slot index) and builds the casting goal on the caster: goal 26, or 82 when the spell info asks for the cast to finish as a goal, 73 for one variant, with an 84 pushed over it for the 82 variant; the goal frame names the run by that **slot index** (parameter 11). Both tables load back into the **same slot positions** (run-length walk with a running index), so the link survives a load. The record's flag word, with the state bits the casting start sets, is saved; the Anim event that drives the casting run and the MagicTech events are saved kinds (PS12). **In memory only:** the goal frame's sound slot (saved as −1, so the cast sound is not resumed), spell-run record bytes +0xC and +0x14C..+0x157 (meaning not read, Q3) and the interface mode (the F7 pop drops an armed target cursor). Nothing else found | F1; the continuation is an **inference** |
| SV5 | projectile 0x4B24F0 from prototype 0x13A4 via 0x4ED8B0; spell missile 0x450C90 / 0x450D1A via 0x43CBA0; flush 0x410B20 | An arrow or thrown weapon in flight is a new object made from the projectile prototype; a spell missile is made by the magic-tech code and linked into the spell-run record. Both are objects created in play (dynamic, OM15–OM27). The map flush writes into `mobile.mdy` every non-static object whose field 19 has bit 0x2000 set and bits 0x1 and 0x800000 clear; there is **no type, prototype or projectile exclusion**. So they are written whole, with the projectile's fields 77–80 (the shot's combat flags, two combat numbers and the weapon handle) | F1 |
| SV6 | shooter 0x4B39B0 (from 0x4B3715), goal 36 node 0x42F8F9, impact 0x4B2870, no-hit end 0x42CAC0 → 0x4B2650 → 0x4B2690 | **Hit or miss is decided at launch**: with the hit flag 0x2 clear the target is cleared, a scatter tile is picked and flag 0x20 set; field 77 keeps the flags and the flight frame keeps the target, both saved. At impact the combat data is rebuilt from fields 77–80 and the damage is computed then (the damage roll itself was not read, Q4). A shot that ends without a hit puts a thrown weapon down on the tile and destroys the projectile. The flight run (goal 28 for a missile, 36 for a shot) is in the Anim table with its frame and path | F1 |
| SV7 | walk nodes in 0x424000–0x433000, path block of the run record | The run record's 248-byte path block (+0xC00..+0xCF7) is saved and loaded whole; the walk nodes keep their path position and counters inside it, and the pace is saved too (**inference** on the field names). Nothing in the menu load tail, the loader or the name copy stops the hero. The held-button repeat of the walk (every 250 ms) is input state and not saved | F1 |
| SV8 | open items | Q1: whether a dialog window takes the F7 key before the main-loop branch (0x52BDA0). Q2: what the in-load flag 0x5E34FC changes in 0x44D730. Q3: meaning of spell-run bytes +0xC and +0x14C..+0x157 and whether the release step reads them. Q4: the impact damage roll (0x4B2F60 / 0x4B6B90) and the roles of fields 78 and 79. Q5: side effects of the Player module (PS13) on the hero's animation fields. Q6: a save inside a turn-based round, whose restart runs before the Anim load and can start eye-candy runs that share slots (PS30) | — |

Corrections to earlier rows: none contradicts. Additions: PS11 (the record's first field is the slot
index and is read back, so slot positions are kept; +0xC and the last eleven bytes are not saved);
PS15 (no owner or player filter; the load-end recount); PS20 (the exact `mobile.mdy` test of SV5).
The goal numbers of SV4 are the reader's from the casting start; the saved-runs concept names the
casting goals 26, 27 and 83 after the session's own goal table, and which of the two numberings
applies to each variant was not reconciled here.

## What it means for Steamwoven (MIG14)

- The owner's cancellation of S6 ("saving in the middle of a cast is excessive") was a choice
  about scope, not a copy of the original: the original saves all three and has **no gate**.
  The two quick-save gates of the viewer ("a cast is playing", "a spell missile is flying") are
  Steamwoven-only refusals.
- The original does not save the AI-driven pieces differently: NPC casts, missiles and shots
  go the same way as the hero's, so a faithful save also keeps those of NPCs (today the runs that name a cut
  spell run are not written, saved-runs §3.1).
- The design left in [saved runs](../architecture/saved-runs.md) §3.5 covers the three parts: the
  cast (spell run, caster's marks, casting goals), the missile objects (position, flight line,
  pace) and the projectiles (the hero's attack facts plus the decided resolution, the original's
  fields 77–80). The original resolves hit or miss at launch and keeps it in the projectile's fields, which is the
  shape of the design's `PendingShot` with its decided `AttackResolution`.
- Differences an "as the original" reopening would have to accept: the cast sound is not resumed,
  a held-button walk is not renewed, and an armed target cursor is dropped.
