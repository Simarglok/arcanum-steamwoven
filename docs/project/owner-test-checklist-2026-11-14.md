# Owner test checklist, 2026-11-14

One working list for the owner's in-game and on-Mac checks. Build from fresh `origin/main` (for example `./arcanum package mac`, or the viewer from `main`). Write what you saw
on the **Notes:** line of each item. The status of the re-check list steps (Part C) is still set in [owner-recheck-list.md](owner-recheck-list.md), which stays the source of
truth; nothing is listed twice.

- **Part A**: checks asked for by open threads (input log, skill pane, missed shots, fonts, menu and HD mirror, optional checks).
- **Part B**: visible effects of the fixes merged on 2026-11-14.
- **Part C**: every open step of the re-check list (419 steps in 88 sections).

## PRs checked as merged into `main`

All 23 were read with `gh pr view` on 2026-11-14 and are **merged**; none is missing.

| PR | Merged | Task |
| --- | --- | --- |
| #917 | 2026-11-10 | ARC-DEV-343 main-menu PNG named in the log |
| #936 | 2026-11-10 | ARC-DEV-339 inventory arrange button, two layouts |
| #937 | 2026-11-10 | ARC-DEV-338 cast log each frame, ghost of Brego |
| #1404 | 2026-11-12 | ARC-UI-1113 skill pane, key K |
| #1422 | 2026-11-12 | ARC-INPUT-1356 moving-creature pick, diagnostics |
| #1438 | 2026-11-12 | ARC-INPUT-1400 log of every mouse press |
| #1580 | 2026-11-13 | ARC-SCRIPT-1483 (issue #1483) combat-point scripts from every hit |
| #1645 | 2026-11-13 | ARC-UI-1574 (issue #1574) pending load callback on a thrown load |
| #1675 | 2026-11-13 | ARC-WORLD-1582 (issue #1582) companion follow walk ends on a trap tile |
| #1964 | 2026-11-14 | ARC-SCRIPT-1764 newspaper script actions a85, a86 |
| #1967 | 2026-11-14 | ARC-COMBAT-1929 backstab from behind |
| #1969 | 2026-11-14 | ARC-COMBAT-1935 combat-mode exit gate for R, right release, dialog start |
| #1970 | 2026-11-14 | ARC-COMBAT-1766 projectile end scripts |
| #1972 | 2026-11-14 | ARC-COMBAT-1941 Shift+Alt on objects, Alt on a wall |
| #1974 | 2026-11-14 | ARC-COMBAT-1930 missed shot alerts nobody, weapon loudness |
| #1976 | 2026-11-14 | ARC-UI-1940 world map opener asks the exit gate |
| #1977 | 2026-11-14 | ARC-COMBAT-1968 a throw or a shot is never a backstab |
| #1979 | 2026-11-14 | ARC-UI-1786 follower menu texts from `follower_ui.mes` |
| #1980 | 2026-11-14 | ARC-UI-1785 follower panel buttons with hidden interface |
| #1981 | 2026-11-14 | ARC-SCRIPT-1789 healer service flag 0x2000 |
| #1984 | 2026-11-14 | ARC-UI-1787 portrait click with lockpicks or a pending order |
| #1985 | 2026-11-14 | ARC-UI-1750 recipes for barter doll 337 and hero doll cover 341 |
| #1986 | 2026-11-14 | ARC-DEV-1982 token-saving rules (docs only) |

## Part A — checks asked for by threads

### A1. Double clicks (#1400, PR #1438, logging only)

- **Do:** build from `main`. Open the Harm cursor, press once on a creature with the mouse that showed the duplicates, press **Ctrl+B** and send the report. Repeat with a second mouse (or the trackpad) and then make **one real double click** on purpose.
- **Expect:** one `ARC-INPUT-1400 press` line per physical press, each with its own `id=`, `frame=`, `device=`; a `BURST=2` or `BURST=3` mark on a line that came less than 50 ms after the previous press of the same button and stage. A real double click shows two lines about 100 ms or more apart. Only one device bursting means the cause is that device or its macOS setting. Details: section ARC-INPUT-1400 of the re-check list.
- **Notes:**

### A2. Skill pane (#1113, PR #1404)

- **Do:** open the skill pane with the **Skills** button of the game bar or the **K** key.
- **Expect:** four buttons (sneak, pick pocket, disarm traps, repair) and each works.
- **Notes:**

### A3. Misses on moving targets (#1356, PR #1422, diagnostics only)

- **Do:** cast at running wolves; right after a miss press **Ctrl+B**. Optionally attach the save and say which wolf.
- **Expect:** the report carries the pointer's surroundings of the refused spell press, so the cause of the miss can be read. The hover ring should also follow a moving body.
- **Notes:**

### A4. Codex fonts (ARC-UI-145 follow-up)

- **Do:** (1) confirm the scope with the coordinator: **stage A** items 27 / 229 / 171 / 327, **stage B** items 28 / 230 / 301 / 371 / 483; items **300 / 370 / 48 are not touched**. (2) Give Codex the file `local/hd-font/generative-comparison-20261111/TASK-FOR-CODEX-remaining-fonts.md`. (3) After an apply, on the **world map at 2560×1440** look at the glyphs **W, 3, 2, S**.
- **Expect:** the glyphs have no ragged edges. To undo an apply: `python3 -B tools/hd_pack/hd_pack.py rollback <backup>` (run it with `--dry-run` first).
- **Notes:**

### A5. Main menu and HD mirror (PR #917)

- **Do:** in a terminal that can read the pack folders run `python3 -B tools/hd_pack/hd_pack.py mirror --dry-run`. Then start the game with "HD interface" on.
- **Expect:** `mirror --dry-run` shows what it would copy and nothing is changed; the game log has one line `ARC-DEV-343 main menu backdrop file=main-menu-16x9.png status=Used ...` when "HD interface" is on. Note: the docs now name the **iCloud** copy as the default destination of `mirror` (it replaced the Google Drive copy, which stays untouched); say which one your terminal reads.
- **Question for you:** do you want a separate `hd_pack.py` command that installs the pack on another device (today the iCloud copy is carried over by hand)?
- **Notes:**

### A6. Optional: reload after death (#1574, PR #1645)

- **Do:** die, and reload from the death screen; then press **F8** twice in a row.
- **Expect:** the reload after death works; the second F8 press in a row is refused.
- **Notes:**

### A7. Optional: a companion stops on a trap (#1582, PR #1675)

- **Do:** lead a companion that is walking after you (not fighting) over a trap plate it spots or sets off.
- **Expect:** it stops on the plate's tile. Step 6 of ARC-WORLD-1469 (Part C) covers the other half, a script plate walked over by you or an NPC.
- **Notes:**

### A8. Optional: poisoned weapon (#1483, PR #1580)

- **Do:** hit with a poisoned weapon.
- **Expect:** known limit: **a poisoned weapon does not poison yet**; the combat-point scripts (hit, critter hits, critical hit) do run from every hit. Nothing to fix from this check; note anything else you see.
- **Notes:**

### A9. Optional: ghost of Brego (PR #937)

- **Do:** go to the cave where the ghost of Brego appears.
- **Expect:** the ghost is visible (drawn from the cast log each frame).
- **Notes:**

### A10. Optional: inventory arrange button (PR #936)

- **Do:** press the arrange button at (763, 197) of the inventory window.
- **Expect:** it switches between the two layouts, rows and columns.
- **Notes:**

## Part B — visible effects of today's merged fixes

Not repeated here: the backstab fixes #1967 and #1977 (steps 1–5 of ARC-COMBAT-1929 in Part C). No visible effect, nothing to check: #1964 (newspaper script actions run only from scripts; no map reaches one yet), #1970 (projectile end-script coverage), #1981 (healer flag, accepted and never stored), #1986 (docs).

### B1. Leaving combat mode and the world map while an NPC fights you (#1969, #1976)

- **Do:** in combat mode with an NPC that fights you (or a companion) close by, press **R**, release the right button, start a dialog (also the NPC's own talk) and press **W**. Then let things calm down and repeat.
- **Expect:** while the fight lasts you stay in combat mode with the message 24 text, the dialog does not start and **W** shows message 600 and opens no map. When calm, R and the right release leave combat mode, and **W** leaves combat mode and opens the map.
- **Notes:**

### B2. Shift+Alt and Alt in combat mode (#1972)

- **Do:** in combat mode press Shift+Alt on a door, window, container or scenery; then Alt on a wall (the Crash Site cave has walls).
- **Expect:** Shift+Alt is the attempt attack without a walk or a run; Alt on a wall makes the hero walk up and swing at the wall (it used to walk to the ground tile). The wall takes no damage.
- **Notes:**

### B3. A missed shot makes no noise at the target (#1974)

- **Do:** fire a bow or throw a weapon at an NPC and miss; compare loud and silent weapons.
- **Expect:** a shot or throw that missed does not alert its aimed target; a hit does. The alarm of an attack follows the weapon's loudness (loud, normal, silent).
- **Notes:**

### B4. Follower order menu words (#1979)

- **Do:** right-click a follower's portrait.
- **Expect:** the eight rows show the words of the game's own `follower_ui.mes` (a row whose message is missing is not drawn).
- **Notes:**

### B5. Follower panel buttons with the interface hidden (#1980)

- **Do:** lead a follower, hide the interface bars and then show them again.
- **Expect:** with the bars hidden the show/hide button of the panel stands at y 587 and the scroll arrows at y 574; with the bars drawn they are at 428 and 415. The portraits never move.
- **Notes:**

### B6. Clicking a follower's portrait with lockpicks or an order pending (#1984)

- **Do:** arm the lockpicks and click a follower's portrait; then choose Walk or Attack in the order menu and click a portrait instead of the target.
- **Expect:** the pick ends at once because a critter is no lock; with the order waiting, the click is an ordinary talk with that follower.
- **Notes:**

### B7. Barter doll and hero doll cover in the generated interface (#1985)

- **Do:** set the HD interface line to **Generated (experimental)** and open a barter window with the hero's doll toggle on and off.
- **Expect:** the merchant's doll (art 337) and the hero's doll cover (art 341) fit their pits and grids; the cover shows no brass rim and its right columns show the pack. Compare with HD and Legacy. The other barter window steps are in section ARC-UI-1194 (Part C).
- **Notes:**

## Part C — open steps of the owner re-check list

419 open steps in 88 sections, copied from [owner-recheck-list.md](owner-recheck-list.md) as of 2026-11-14 (newest sections last). The re-check list stays the source of truth: set a step **same** or **differs** there; write what you saw in the Notes lines here. The ARC-INPUT-1400 section is left out (it is item A1) step 5 of ARC-WORLD-1469 is left out (it is item A7) and so is the superseded row 4 of ARC-UI-1364 (covered by ARC-UI-1504); ARC-COMBAT-1929 is kept here and not repeated in Part B.

### ARC-DEV-352 — Fireflash and Wall of Fire (PR #941, 2026-11-11)

Setup: The component rules are in [Summon for objects](../world/spell-cast-host.md#summon-for-objects-arc-dev-352). Compare them in both games with the same hero and spells. The first build for this comparison was `artifacts/packages/macos` at commit d2899817a; any later build from `main` serves too.

- [ ] **ARC-DEV-352 #1.** Cast Fireflash (spell 22) at empty ground.
  - Expect: A flash appears on the strike tile for about 0.75 s and goes away by itself.
  - Notes:
- [ ] **ARC-DEV-352 #2.** Cast Fireflash at a creature that stands on a tile.
  - Expect: No flash object, because the tile is not empty (`Tgt_Tile_Empty`). Note also what the creature itself gets (eye candy, damage): the Summon rules do not decide that, so it is part of the comparison. The owner does not remember this empty-tile rule, so it is the main thing to check.
  - Notes:
- [ ] **ARC-DEV-352 #4.** Walk the hero or a creature through the wall.
  - Expect: The pieces do not block passage: their prototype carries the "no block" flag in the data.
  - Notes:
- [ ] **ARC-DEV-352 #5.** Stand the caster and a companion in the wall, then a hostile creature.
  - Expect: The caster and creatures led by the caster's party are not burned. Other creatures burn each step, and in turn-based mode once per round.
  - Notes:
- [ ] **ARC-DEV-352 #6.** Stop maintaining Wall of Fire (or let it run out).
  - Expect: All pieces disappear at the same moment.
  - Notes:

### ARC-MAGIC-027 — Effect consumers: resistances and buffs on NPCs (2026-11-11)

Setup: Resistance and stat changes of effects now reach combat values for the hero and NPCs ([consumers](../world/durable-effects-runtime.md#consumers-of-resistance-and-stat-changes-arc-magic-027), [audit](../research/magic-audit-res-103.md#effect-consumers-ma-2-arc-magic-027)). Spells named below put the effect on the target; the flags and the body art of the Body spells are not built yet, so compare numbers only.

- [ ] **ARC-MAGIC-027 #1.** Cast Resist Magick (spell 40) on yourself and open the character sheet. Then let a hostile caster cast a resistable spell (one with a save, for example Stun or Charm) at you many times, with and without Resist Magick, and compare how often it lands.
  - Expect: The Magick resistance line on the sheet rises while the spell runs. In the original the failure rate of the enemy's spells does not change (its save reads the stored magic resistance only, ledger RE2; a bug of the game, owner decision D6c). **Steamwoven now differs on purpose** in both modes: the rate does drop under Resist Magick (see ARC-MAGIC-033 below). Steamwoven's sheet does not show effect resistances yet (known difference below). **If the rate does change in the original too, tell the coordinator.**
  - Notes:
- [ ] **ARC-MAGIC-027 #2.** Cast Shield of Protection (spell 30) on yourself, then take a normal-damage hit from a weapon, a fire hit and an electrical hit, with and without the spell.
  - Expect: Armour class rises by 10 and the damage of each type falls by the resistances of effect 155 (rules data), never below zero damage and never past 95 % protection.
  - Notes:
- [ ] **ARC-MAGIC-027 #3.** Cast Body of Fire, Body of Air, Body of Stone and Body of Water (spells 23, 13, 18, 28) one after the other on yourself and take a fire or an electrical hit.
  - Expect: Only the resistance and speed numbers are compared here: Body of Fire protects against fire, the others by their effect rules; the body art and the flags are not built.
  - Notes:
- [ ] **ARC-MAGIC-027 #4.** Cast Weaken (spell 46) on a strong enemy in turn-based mode, and fight it.
  - Expect: Its strength falls by 5 and its resistances drop by effect 154 (compare the damage you deal to it and the damage it deals). Its maximum hit points fall by 10 (two per point of strength, ARC-WORLD-1144): a unhurt enemy's current hit points fall with the maximum, a wounded one's stay unless they would pass it.
  - Notes:
- [ ] **ARC-MAGIC-027 #5.** Cast Congeal Time (spell 76) on an enemy and enter turn-based mode. In real time watch its attack pause.
  - Expect: Its action points per round fall (speed −50 %); in real time its attacks come slower; its walking speed does **not** change (the original does not read the speed stat for walking).
  - Notes:
- [ ] **ARC-MAGIC-027 #6.** Cast Hasten (spell 77) on yourself and on a companion, and enter turn-based mode.
  - Expect: More action points per round (speed +100 %) and quicker attacks in real time; walking speed is unchanged.
  - Notes:
- [ ] **ARC-MAGIC-027 #7.** Stack protective spells until the sheet's resistance would pass 95 %.
  - Expect: The protection stops at 95 % for the hero and ordinary NPCs. A monster (animal, undead, mechanical) is not capped in the original.
  - Notes:

### ARC-MAGIC-029 — Script conditions about spells and the mastery action (MT21a, 2026-11-11)

Setup: The interpreter now reads four spell conditions (has a body spell, is under the influence of a spell, knows a spell, has mastered a college) and the action "give spell mastery in college" ([ledger SC1–SC6](../research/ledger/scripts.md#spell-conditions-and-the-spell-mastery-action-arc-magic-029-mt21a)). **Nothing changes in game yet**: no host answers the conditions, so 22, 6, 16 and 16 installed scripts (conditions 14, 35, 48, 49) and 2 (action 112) still stop where they stopped before. These steps become testable when the host wiring lands; they are listed now so the comparison is planned.

- [ ] **ARC-MAGIC-029 #1.** With a character that knows a spell of a college (and one that does not), talk to or trigger a script that asks "knows spell" (32 lines in 16 installed scripts).
  - Expect: The answer is yes only when the spell's rank is at most the rank the character has in that college (so knowing rank 2 of a college means knowing the college's rank 1 and 2 spells). A spell number outside 0..79 is never known.
  - Notes:
- [ ] **ARC-MAGIC-029 #2.** Compare a character with no mastered college and one who mastered a college against a script that asks "mastered college N".
  - Expect: Equal college number is yes. Asking for college −1 is yes for a character with no mastery.
  - Notes:
- [ ] **ARC-MAGIC-029 #3.** Reach a script that gives mastery (2 installed scripts) with a character that has five ranks in the named college, then one that has fewer, then one that already mastered another.
  - Expect: Mastery is written only for a hero or NPC with no mastered college and five ranks in that college; otherwise nothing happens and the script goes on. Mastery cannot be given twice.
  - Notes:
- [ ] **ARC-MAGIC-029 #4.** Stand under Body of Fire, Earth, Water or Air (spells 23, 18, 28, 13) next to an NPC whose script asks "has a body spell" (22 installed scripts).
  - Expect: Any of the four body bits counts; invisibility and floating do not.
  - Notes:

### ARC-DEV-359 — Created critters, session and hosts (E1-c1, 2026-11-11)

Setup: The session now creates, saves and hosts a critter made from a prototype ([created critters](../architecture/object-directory.md#e1-c1-created-critters-arc-dev-359)). The viewer draws it (E1-c2) and a script makes it (E1-c3, below); the creature Summon is E1-e.

- [ ] **ARC-DEV-359 #1.** Let a script create a creature next to the hero (the snapping-twig tile of the start map creates eight, [ARC-DEV-371](#arc-dev-371--script-created-critters-e1-c3-2026-11-11)), and watch them.
  - Expect: It appears on the tile, stands there at first, and starts its ordinary behaviour after a short delay that grows with its distance from you (a created NPC gets the delayed first tick, RL16 and RL17). Hostile ones come at you; peaceful ones stand or wander as their prototype says.
  - Notes:
- [ ] **ARC-DEV-359 #2.** Save with the creatures of a snapping twig alive, load.
  - Expect: It is still there, where it was last standing.
  - Notes:

### ARC-MAGIC-028 — Aptitude inputs: sector adjustment and mage backgrounds (2026-11-11)

Setup: The rules and the readers are merged ([character stats](../world/character-stats-runtime.md#context-inputs-arc-magic-028-fr-char-020), [ledger AQ1–AQ8](../research/ledger/magic.md#aptitude-inputs-arc-magic-028)), but the hero's sheet and the other callers still pass sector adjustment 0 and no background, so **our game shows none of this yet**; the steps below only record what the original game does, to compare when the wiring slice has landed. Magick points and aptitude are read on the character sheet.

- [ ] **ARC-MAGIC-028 #1.** On the main map, read the aptitude on the sheet inside the sector at (1749, 1512) and in a neighbouring sector with no adjustment; then in the sectors at (969, 1024) and (970, 1024), and at (969, 1025) and (969, 1030).
  - Expect: Aptitude is shifted by +40 in (1749, 1512), by −20 in (969, 1024) and (970, 1024), by +10 in (969, 1025) and by +8 in (969, 1030), then clamped to −100..100. A sector of the 1 654 with metadata that is not named here adds 0.
  - Notes:
- [ ] **ARC-MAGIC-028 #2.** On the map "k'na tha", read the aptitude inside one of its 19 sectors with adjustment (for example the one at (12, 12)) and in a sector next to it that is not one of them.
  - Expect: The sectors with adjustment add +30; the others add 0.
  - Notes:
- [ ] **ARC-MAGIC-028 #3.** Take a hero with the Day Mage background and watch the magick points on the sheet across 5:59/6:00 and 17:59/18:00 game time.
  - Expect: The value is 4 above the stored points from 6:00 to 17:59 and 4 below it from 18:00 to 5:59: a swing of 8 at both edges, and aptitude follows.
  - Notes:
- [ ] **ARC-MAGIC-028 #4.** The same with the Night Mage background.
  - Expect: The reverse of step 3: 4 above from 18:00 to 5:59, 4 below from 6:00 to 17:59.
  - Notes:
- [ ] **ARC-MAGIC-028 #5.** A Sky Mage hero walks from open ground into a house, a cave or a dungeon.
  - Expect: Magick points are 4 above the stored points on an outdoor tile and 4 below on any other tile (including where the game has no tile data); a swing of 8 at the doorway.
  - Notes:
- [ ] **ARC-MAGIC-028 #6.** A Nature Mage hero walks from a town street or a stone floor onto grass, forest or other natural ground.
  - Expect: Magick points are 4 above the stored points on ground whose tile name has the `n` flag (the Arcanum module's names have it on 21 of the 31 outdoor flippable, 1 of the 6 outdoor, none of the 2 indoor flippable and 4 of the 13 indoor records) and 4 below elsewhere. Note which grounds count: that list is the point to compare.
  - Notes:

### ARC-RES-108 — Executable triage of 0x44-0x45 (2026-11-11)

Setup: [Ledger section](../research/ledger/census-triage-0x44-0x45.md). The triage read 391 functions of the executable and compared them with the code; the differences below are the ones where our code does something other than the function. They are read from the executable, not seen in game, so each step says what the original is expected to do.

- [ ] **ARC-RES-108 #1.** Cast a knockback spell (a spell with a push component) from an inventory item such as a scroll or wand at a creature that is not in line with you and the item.
  - Expect: The creature is pushed away from the spell's parent object (here the holder of the item), else from the run origin. Ours pushes away from the run's source object, which differs when source and parent differ (0x4514E0).
  - Notes:
- [ ] **ARC-RES-108 #2.** With a hero who has the backstab skill trained but no points in it, attack a creature from behind.
  - Expect: The original needs the skill level to be non-zero: no backstab (ours too since ARC-COMBAT-1929, see its section below).
  - Notes:
- [ ] **ARC-RES-108 #3.** Put the party into a fight and then end it with peace (the peace spell or dialog end of combat) while companions are mid-attack.
  - Expect: The attack goals of the peace head and of its followers are interrupted at level 5 (0x44E050, 0x44E0E0); ours leaves those runs going.
  - Notes:
- [ ] **ARC-RES-108 #4.** Stand in a place where a script or spell looks for the critters "near" the hero (a vicinity-based script condition or action), once at the edge of the screen and once close.
  - Expect: The vicinity is the game window rectangle centred on the location, converted to tiles; ours uses a fixed square of 10 tiles on both axes (0x440FF0).
  - Notes:
- [ ] **ARC-RES-108 #5.** Walk a path across a tile with burning scenery (fire from a spell or trap) with a mover that avoids obstacles (a companion following you).
  - Expect: A tile with a running fire goal costs 80 more when the mover carries walk flag 0x400; ours has no such flag, so a path never avoids burning scenery (0x444500).
  - Notes:

### ARC-RES-110 — Function triage of the exe bands 0x46, 0x49 and 0x4A (2026-11-11)

Setup: The triage changes no behaviour. These are the places where it found original NPC behaviour that our code does not reproduce at all ([ledger FT6](../research/ledger/census-triage-d.md#what-the-reading-found-that-the-earlier-documents-did-not-say)); each step is to see the original do it, so that the follow-up task can be sized. The readings are F1 (one reader).

- [ ] **ARC-RES-110 #1.** Open or attack a container or a door in the open view of a guard NPC (a shop keeper or a town guard), then of an ordinary passer-by.
  - Expect: Guard NPCs, and NPCs of social class 5, within 10 tiles see or hear it: each of them attacks, loses 20 reaction or floats a warning (0x4AEE50). Ours only raises a flag and nobody reacts.
  - Notes:
- [ ] **ARC-RES-110 #2.** Watch NPCs whose AI row forbids opening portals next to a closed door on their route.
  - Expect: The AI parameter flag of slot 16 decides whether an NPC may open a portal; the player may always (0x4AEB10). Ours stores the flag and never asks it.
  - Notes:
- [ ] **ARC-RES-110 #3.** At night, move a sleeping NPC off its bed (push, teleport or leave the map and come back) and wait.
  - Expect: The sleeper is put back onto a bed object found at its night standpoint, else at its day one (0x4AD0B0, 0x4AD140). Ours leaves it where it is.
  - Notes:
- [ ] **ARC-RES-110 #4.** Frighten an NPC with a fear effect, or let one surrender in a dialog.
  - Expect: It starts to flee from the source (the start-flee request 0x4AF130). Ours has only the stop-fleeing side.
  - Notes:

### ARC-DEV-365 — Item cursor and the item use goals 39 and 41 (2026-11-11)

Setup: The hero's use icon on an item that needs a target opens a cursor, and the use walks next to the target when the original does. The rules are those of [IA1–IA16](../research/ledger/items.md#item-aiming-and-use-with-a-target-arc-res-113). The comparison needs an item of each kind from the original data (the first four steps need a scroll or a food item with a spell, which the Mac's save can be given by a script).

- [ ] **ARC-DEV-365 #1.** Hold a scroll whose spell asks for an object (a damage scroll), open the inventory and release the item on the use icon with no Shift.
  - Expect: The window closes and the spell cursor opens (the same cursor art as for a spell); a left press on a creature uses the scroll on it, a right press cancels the cursor. The cursor stays open after a press that finds no target.
  - Notes:
- [ ] **ARC-DEV-365 #2.** With the same cursor open, press a creature farther than the spell's range, then one inside it.
  - Expect: Inside the range the scroll is used at once from where the hero stands; outside it the hero walks next to the creature first and then uses it (item-use goal 39).
  - Notes:
- [ ] **ARC-DEV-365 #3.** Release a scroll or potion on the use icon with Shift held.
  - Expect: An item whose spell can target an object is used on the hero at once, with no cursor (and no "not on yourself" test, unlike the spell cursor).
  - Notes:
- [ ] **ARC-DEV-365 #4.** Open the cursor with a scroll whose spell has a tile in its target mask (a fire spell) and press an empty tile.
  - Expect: The scroll is used on the tile at once, from where the hero stands; it casts at the pressed tile, the scroll's level test and the item's use script do not run for a tile.
  - Notes:
- [ ] **ARC-DEV-365 #5.** Hold a script trap device (a spike trap) and release it on the use icon; press an empty tile three cells away, then a creature.
  - Expect: The cursor accepts an object or a tile (a trap device has no spell). The hero walks next to the tile (goal 41) or next to the creature (goal 39) first; the trap device itself is not placed yet (it stays in the pack, "not ready").
  - Notes:
- [ ] **ARC-DEV-365 #6.** Release an item that needs a target but has no spell (a food item) on the use icon and press a creature.
  - Expect: The cursor accepts any object, and the item's use script sees the creature as its target. Ours runs the script without the target object.
  - Notes:

### ARC-RES-118 — Function triage of the exe band 0x42-0x43 (2026-11-11)

Setup: The triage changes no behaviour. These are the places where the readers found original behaviour that our code differs from or lacks, each checked in the code ([ledger](../research/ledger/census-triage-b.md#findings-worth-a-task)); each step is to see the original do it, so that a follow-up task can be sized. The readings are F1 (one reader).

- [ ] **ARC-RES-118 #1.** Watch an NPC that wanders (a townsperson with the wander flag) once it has reached its standpoint, by day and in the dark.
  - Expect: It starts a wander goal (goal 65, in the dark goal 66) and walks to a destination near the standpoint (0x435E60, 0x436040). Ours starts none: the standpoint step of the route rules is marked not implemented, so the NPC stands still.
  - Notes:
- [ ] **ARC-RES-118 #2.** Stack two critters on one tile (for example a refused attack start next to a follower, or a follower asked to step aside) and watch them.
  - Expect: The NPCs are asked to move away from each other (the step-apart request 0x4364D0, goal 68). Ours has an empty step-apart method, so they stay on one cell.
  - Notes:
- [ ] **ARC-RES-118 #3.** Talk to an NPC (a dialog open) and, while the dialog is open or just after it, make a script or route walk start for that NPC in a round.
  - Expect: The start gate asks the activity test and whether the NPC has a conversation partner, not only whether it is the round's current participant (0x4339A0, 0x4C1110). Ours tests only the participant, so an NPC in a conversation may be sent walking.
  - Notes:
- [ ] **ARC-RES-118 #4.** Swing and hit a creature with a weapon that has its own eye candy (a magic weapon), and with a blow that draws blood.
  - Expect: The weapon's own eye candy plays at the swing and at the hit, and the one at the hit may veto the damage effect (0x4377C0); the blood eye candy of the hit plays (0x433020). Ours plays neither (only the blood pool of a kill).
  - Notes:

### ARC-DEV-364 — NPC grenades (T-h, 2026-11-11)

Setup: An NPC in a fight can now pick a grenade from its pack and throw it at its target ([grenade step](../world/ai-host.md#grenade-step-arc-dev-364), [ledger TH1–TH4](../research/ledger/npc-ai.md#npc-grenade-host-arc-dev-364)). The original's blast-radius reading is inverted (TH1), so the original game gives every installed grenade radius 0; the owner decided on 2026-11-11 (ARC-DEV-373) that this is a bug of the game, so **both rule modes** use the real radius of the grenade's spell and the original game will differ from them in steps 2 to 4. The modes differ in one place only: **Original** never checks PCs in the blast (SP15). Compare with an NPC that carries a grenade (the technology Explosives products, or the grenades in the crash site cave) and has throwing skill points.

- [ ] **ARC-DEV-364 #1.** Start a fight with a hostile NPC that carries a grenade and has points in throwing, standing a few tiles away with a clear line.
  - Expect: At the start of the fight it throws the grenade once (CHECK_GRENADE is set once per fight start); the grenade flies, explodes with its own spell and is gone from its pack.
  - Notes:
- [ ] **ARC-DEV-364 #2.** Repeat with the hostile NPC standing right next to you.
  - Expect: Both modes: it does not throw while the target is within the grenade's radius and attacks instead. The original game throws anyway (radius 0, TH1); that difference is the fixed bug, not a new finding.
  - Notes:
- [ ] **ARC-DEV-364 #3.** Put a friend of that NPC (another NPC of its faction) next to you, then fight.
  - Expect: Both modes: it holds the grenade while a friend it protects stands in the blast square. The original game throws anyway and hurts its friend (TH1).
  - Notes:
- [ ] **ARC-DEV-364 #4.** Give a companion a grenade (it needs throwing points), then stand next to a hostile NPC that the companion fights.
  - Expect: **Steamwoven:** the companion holds the throw while you stand in the blast square. **Original:** it throws once the target is outside the radius and no NPC it protects stands in the square, and the blast can hit you, its leader (PCs are never checked, SP15).
  - Notes:
- [ ] **ARC-DEV-364 #5.** Walk behind a wall or a closed door so a blocker stands between you, then fight.
  - Expect: It does not throw while anything blocks the line to your tile (the target tile's own objects do not count).
  - Notes:
- [ ] **ARC-DEV-364 #6.** An NPC with a grenade and no throwing points (a creature with none).
  - Expect: It never throws and attacks as before.
  - Notes:

### ARC-RES-117 — Executable triage of 0x40-0x41 (2026-11-11)

Setup: [Ledger section](../research/ledger/census-triage-a.md). The triage changes no behaviour (it adds registry rows and `[OriginalFunction]` marks). These are the places where the reading found original behaviour that our code does not reproduce; each step is to see the original do it. They are read from the executable, not seen in game.

- [ ] **ARC-RES-117 #1.** Make a creature back off or flee from a target (a scared or surrendering NPC, a follower ordered to back off) in open ground.
  - Expect: It walks away along a tile found by the away-path search (0x41F6C0). Ours finds no away tile, so the back-off ends at once.
  - Notes:
- [ ] **ARC-RES-117 #2.** Talk to a teacher, then to a healer, a rumor monger and a newspaper vendor (a reply that opens a skill list, a cure, a rumor or a paper).
  - Expect: The replies marked for trainers, healers, rumors, town directions and newspapers show their generated lines and offers; training by an apprentice costs 100 gold (0x418CA0..0x41A8C0). Ours now builds the trainer's `t:` reply (ARC-SCRIPT-1150: the skill menu, the price on the reaction scale, the payment and the training); the rumor monger's `r:` reply too (ARC-SCRIPT-1304: one rumor of its list, for gold when it names a price, told in the NPC's words and written to the logbook); the news reply `c:` too (ARC-SCRIPT-1321: the story line of the story state, then the one way on); the town directions replies `d:`/`x:` too (ARC-SCRIPT-1533) and the quest-state reply `q:` (ARC-SCRIPT-1532); the healer reply is still refused as unsupported.
  - Notes:
- [ ] **ARC-RES-117 #3.** Start a talk with an NPC that is hostile on sight, then with one that cannot speak to you (too evil, too good, party full).
  - Expect: A hostile NPC answers with its race line and the talk ends; an NPC that cannot speak refuses with the line of its reason range (0x412FD0, 0x413A90). Ours opens the talk first.
  - Notes:
- [ ] **ARC-RES-117 #4.** Ask a talkative NPC for money (a dialog money request), once with enough gold and once without.
  - Expect: The request, payment and no-money steps run from the reply (0x418A40, 0x418B30, 0x418C40). Ours has no such reply state.
  - Notes:
- [ ] **ARC-RES-117 #5.** Save while combat music plays (any fight), reload that save and leave the fight.
  - Expect: In the Original setting the pre-combat music and ambient schemes are lost by the save (original bug SN18, 0x41B1D0); Steamwoven keeps them, so compare which music returns.
  - Notes:
- [ ] **ARC-RES-117 #6.** Walk through a door or fight a critter, then leave the map while its run is still going and come back.
  - Expect: The map-clear flag gates the end nodes of goal runs (0x410060); ours has no such flag, so compare how door and critter runs end across the map change.
  - Notes:

### ARC-MAGIC-030 — The script hosts answer the spell commands (MT21b, 2026-11-11)

Setup: The use, tile, sector, heartbeat, dying and talk script hosts now answer "has a body spell", "under the influence of a spell", "knows a spell" and "mastered college" and write "give spell mastery" for the hero and for an NPC of the shown map ([ledger SC7–SC9](../research/ledger/scripts.md#spell-conditions-and-the-spell-mastery-action-arc-magic-029-mt21a)). It completes the steps of the ARC-MAGIC-029 list above. **Few installed scripts run yet**: 46 use these commands and only a handful are admitted whole, because they also use commands later slices bring (get stat 33 in 17 of them, action 91 in 16, others), so a step may stop where the script meets one of those.

- [ ] **ARC-MAGIC-030 #1.** With a hero who has learned all five spells of a college and has no mastery, reach a script that gives mastery of that college (2 installed scripts), then look at the college on the spells page.
  - Expect: The college is now mastered (its spells cost half and last twice as long between maintenance ticks). Reaching the script again, or one for another college, changes nothing.
  - Notes:
- [ ] **ARC-MAGIC-030 #2.** The same with a hero who has fewer than five spells of the college.
  - Expect: Nothing changes and the script goes on.
  - Notes:
- [ ] **ARC-MAGIC-030 #3.** Ask "knows spell" (16 installed scripts) of a hero who knows rank 3 of a college, for the college's rank 3 spell and its rank 4 spell.
  - Expect: Rank 3 is known, rank 4 is not.
  - Notes:
- [ ] **ARC-MAGIC-030 #4.** Reach a script that gives mastery to an NPC who has all five ranks of a college (a mage NPC of the shown map).
  - Expect: The NPC's mastered college is set once and stays; an NPC the map data gives no sheet for is skipped with the run left unapplied (see below).
  - Notes:

### ARC-RES-119 — Function triage of the exe bands 0x50 to 0x54 (2026-11-11)

Setup: [Ledger section](../research/ledger/census-triage-h1.md#function-triage-of-the-exe-bands-0x50-to-0x54-arc-res-119). The triage read 933 functions of the engine library and the interface of the executable and changed no behaviour; the one step below is where it found a visible difference from our held-item cursor. The reading is F1 (one reader).

- [ ] **ARC-RES-119 #1.** Pick up an item from the inventory so that it hangs on the cursor, then move the cursor to the right and the bottom edge of the screen.
  - Expect: The cursor art laid over the item's art is clipped to the screen and its hot point is shifted, so the held cursor moves to stay on screen (0x500160, 0x5000F0). Ours places the overlay at a fixed offset from the hot point and does not shift it.
  - Notes:

### ARC-DEV-370 — Created critters in the viewer (E1-c2, 2026-11-11)

Setup: The viewer draws, animates and picks a critter the session created ([E1-c2](../architecture/object-directory.md#e1-c2-created-critters-in-the-viewer-arc-dev-370)). Since ARC-DEV-371 a script makes them (the snapping twig of the start map, below); the creature Summon is E1-e. The smoke stage `created-critter` covers the draw, the pick, the walk frame and the removal on real data.

- [ ] **ARC-DEV-370 #1.** Step on a snapping-twig tile of the start map ([ARC-DEV-371](#arc-dev-371--script-created-critters-e1-c3-2026-11-11)) and watch the creatures in the first seconds.
  - Expect: It appears on the tile in its prototype's stand pose and facing, at the size and offset of a map creature of the same body. Compare its size and footing with a map creature of the same race.
  - Notes:
- [ ] **ARC-DEV-370 #2.** Hover and click it, then fight it.
  - Expect: The cursor, name and life bar are those of a map creature; it can be attacked and dies like one, lying in the pose the kill chose.
  - Notes:
- [ ] **ARC-DEV-370 #3.** Watch it walk and run when it chases or wanders.
  - Expect: Its steps use the walk and run cycles in the direction it walks, as map creatures do.
  - Notes:
- [ ] **ARC-DEV-370 #4.** Create a creature whose art is a monster or a unique character (not a human body).
  - Expect: The right monster or character body is drawn; note any creature that is drawn as nothing (the log says `created-critter-art ... status=MissingFile`).
  - Notes:

### ARC-DEV-363 — Technology census (slice T-b, PR #971, 2026-11-11)

Setup: The census test pins which components of the 83 technology records the host takes ([audit section 7](../research/technology-audit-res-104.md#7-smoke-and-test-coverage)); it reads our code, not the original, so the steps below are the places where the census says a record runs but nobody has compared its effect with the original game. Use a hero with the technology points for the degrees named, or the items from a map or a console. Compare numbers and what happens on screen, not art or sound.

- [ ] **ARC-DEV-363 #1.** Throw a flash grenade (an Explosives product of degree 1 or 5, or one found on a map) at an enemy standing on open ground and at an empty tile.
  - Expect: Fire and normal damage land on the creatures in the area and the target is pushed; a flash object appears on the strike tile and goes away by itself (the census counts the record as running: the summon of its scenery and its destroy).
  - Notes:
- [ ] **ARC-DEV-363 #2.** Throw a stun grenade (Explosives degree 4) and a smoke grenade (degree 3) at a group.
  - Expect: The stun grenade stuns creatures in its radius with a flash; the smoke grenade makes a smoke cloud for about 10 s, then it goes. The smoke grenade also sets the don't-draw flag on creatures, which is not built (the census lists it as partly running).
  - Notes:
- [ ] **ARC-DEV-363 #3.** Drink a Therapeutics potion (any of the seven stat potions) as the hero, and give the same potion to a companion by a script or by throwing it.
  - Expect: The stat bonus (+2 to the stat or stats named) appears on the sheet and goes away when the potion ends. On a companion the census says the effect list is written; compare the numbers shown or their effects in a fight.
  - Notes:
- [ ] **ARC-DEV-363 #4.** Use Chemistry degree 1 (poison draught) on a creature, and Chemistry degree 4 (acid) on a creature.
  - Expect: The poison does 290 damage over its duration; the acid does 10 to 100 damage and makes a flash. Compare the damage numbers with the original.
  - Notes:
- [ ] **ARC-DEV-363 #5.** Find a map item that carries the record 214 (a technology item on a map; see the placement census) and use it as the hero.
  - Expect: The original adds effect 334 80 times (its list has no bound). Since ARC-RES-127 our hero and NPC lists take the 80 entries (the list holds 1024). Note what the original does to the sheet and compare.
  - Notes:

### ARC-MAGIC-031 — The hero's sector aptitude and mage backgrounds are wired (2026-11-11)

Setup: The hero's character sheet, inventory, carry weight and equipment now read the sector's aptitude adjustment, the tile under the hero, the hero's background and the game hour ([ledger AQ wiring note](../research/ledger/magic.md#aptitude-inputs-arc-magic-028)). This makes the steps of the ARC-MAGIC-028 list above playable; the expected values there stand. Magick points and aptitude are read on the character sheet.

- [ ] **ARC-MAGIC-031 #1.** On the main map (`arcanum1-024-fixed`), read the aptitude on the sheet standing in sector (1749, 1512) and then in a neighbouring sector; then in (969, 1024) and (970, 1024), (969, 1025) and (969, 1030).
  - Expect: +40 in (1749, 1512), −20 in (969, 1024) and (970, 1024), +10 in (969, 1025), +8 in (969, 1030), 0 in an unnamed sector. The value changes at the sector border, not before.
  - Notes:
- [ ] **ARC-MAGIC-031 #2.** Same on the map "k'na tha", in one of its 19 sectors with adjustment and next to it.
  - Expect: +30 inside, 0 next to them.
  - Notes:
- [ ] **ARC-MAGIC-031 #3.** Day Mage hero: watch magick points across 5:59/6:00 and 17:59/18:00; then the Night Mage.
  - Expect: Day Mage 4 above the stored points from 6:00 to 17:59 and 4 below otherwise; Night Mage the reverse.
  - Notes:
- [ ] **ARC-MAGIC-031 #4.** Sky Mage hero: walk from open ground into a house and a cave.
  - Expect: 4 above on an outdoor tile, 4 below indoors (a swing of 8 at the doorway).
  - Notes:
- [ ] **ARC-MAGIC-031 #5.** Nature Mage hero: walk from a street or stone floor onto grass or forest.
  - Expect: 4 above on a tile with the `n` flag, 4 below elsewhere; note which grounds count.
  - Notes:
- [ ] **ARC-MAGIC-031 #6.** In a sector with adjustment, put on a magickal or technological item whose weight or effect depends on aptitude and compare with the same item next to the sector.
  - Expect: The item's effective weight follows the aptitude shown on the sheet (not checked in the original game; note what you see).
  - Notes:

### ARC-DEV-358 — ObjFlag writer: spell flags on the hero and creatures (MA-1, 2026-11-11)

Setup: The component rules are in [the ObjFlag component](../world/spell-cast-host.md#the-objflag-component-and-the-component-table-arc-dev-358-ma-1), the executable reading in [OB1–OB5](../research/ledger/spell-runs.md#objflag-component-of-the-cast-host-arc-dev-358). Compare in both games with the same hero and spells; a spell that also has a not-ready part (Trait, Dispel, Summon) is named in the step. The flags are written by the spell; some are drawn or read by other systems only later, which the steps say.

- [ ] **ARC-DEV-358 #1.** Cast Hardened Hands (45) on yourself, then hit a creature unarmed (no weapon in hand).
  - Expect: The unarmed damage gets +2 normal damage while the spell lasts, and loses it when it ends. Ours feeds the bonus from the flag; check the number in the combat log.
  - Notes:
- [ ] **ARC-DEV-358 #2.** Cast Body of Stone (18) on yourself and hit a creature unarmed.
  - Expect: +5 normal damage unarmed; the spell's art change (Trait) and its weight and speed side are not ready in ours, so the hero keeps its normal body.
  - Notes:
- [ ] **ARC-DEV-358 #3.** Cast Invisibility (69) on yourself and walk in front of an enemy that has already noticed you.
  - Expect: The original makes you translucent, and the enemy's awareness rules read the invisible flag. Ours writes the flags and the AI host reads them, but the viewer does not draw you translucent yet.
  - Notes:
- [ ] **ARC-DEV-358 #4.** Cast Dweomer Shield (42), then have a creature cast a harmful spell at you (or cast at yourself from a scroll).
  - Expect: The shell stops the spell at the per-target stage and the spell shows the shell message (602/603). Ours already read the flag before; now the spell writes it. Its Dispel half is built since ARC-MAGIC-034 (see the next section).
  - Notes:
- [ ] **ARC-DEV-358 #5.** Cast Bonds of Magick (43) on yourself and try to cast another spell.
  - Expect: The cast is refused at the invocation check while the flag holds.
  - Notes:
- [ ] **ARC-DEV-358 #6.** Cast Reflection Shield (44) on yourself and let a creature cast a hostile spell at you.
  - Expect: The spell is reflected to its caster.
  - Notes:
- [ ] **ARC-DEV-358 #7.** Cast Sanctuary (63) on yourself near a hostile creature that has not seen you yet.
  - Expect: The hostility rules read the enshrouded flag; compare which creatures still attack.
  - Notes:
- [ ] **ARC-DEV-358 #8.** Cast Disintegrate (34) on a creature.
  - Expect: The creature takes 30000 electrical damage and dies; the original sets don't-draw on it. Ours writes the flag (and the paralysed bit); since ARC-MAGIC-045 the viewer draws no body for it either (the eye candy stays), and the AI and the spell target rules treat it as gone.
  - Notes:

### ARC-DEV-366 — Tech books give expertise equal to intelligence (PR #980, 2026-11-11)

Setup: The rules and what is built are in [tech books](../world/inventory-runtime.md#tech-books-arc-dev-366) and [ledger XA9, XA11](../research/ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114). Where the eight books lie in the world is not read yet ([census](../research/technology-evidence.md)), so the first step is to find where one lies in the original; in ours it works wherever the viewer lets the hero pick up, loot or buy a book. Known differences, not to be re-reported: taking a book out of the pack by a drop, a sale or a gift is refused in ours (the removal script does not run there yet); a book picked up from the ground after the hero dropped it is refused likewise.

- [ ] **ARC-DEV-366 #1.** With a hero of intelligence 8 to 10 and no degree in the discipline, open the character sheet's technology page, note the expertise of the book's discipline, pick up a tech book (for example the Herbology Manual) and look again.
  - Expect: The discipline's expertise rises by the hero's intelligence (not by 1) the moment the book is in the pack; the other disciplines do not change.
  - Notes:
- [ ] **ARC-DEV-366 #2.** Pick up a second copy of the same book, or a book of another discipline.
  - Expect: A second copy adds the intelligence again (the effects stack); another discipline's book raises that discipline.
  - Notes:
- [ ] **ARC-DEV-366 #3.** Raise the intelligence while carrying a book (a potion, an item or a level-up), look at the expertise, then move the book to another place in the pack grid and look again.
  - Expect: Before the move the expertise still counts the old intelligence; after the move (the original removes the book's effects and adds them again) it counts the new intelligence.
  - Notes:
- [ ] **ARC-DEV-366 #4.** Take the book out of the pack (drop it or sell it).
  - Expect: In the original the expertise falls back by the intelligence the book last counted. In ours the drop and the sale are refused for now (see above), so only the original can be compared here.
  - Notes:
- [ ] **ARC-DEV-366 #5.** Open the barter window of a merchant that sells a tech book and buy it.
  - Expect: The book enters the pack and the expertise rises as in step 1 (the purchase fires the same insertion).
  - Notes:

### ARC-MAGIC-032 — Identify: See Contents, Read Aura, Divine Magick (MA-7, 2026-11-11)

Setup: The component rules are in [Identify](../world/spell-cast-host.md#identify-arc-magic-032) (executable reads XR1–XR5 in the [ledger](../research/ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)). By owner decision D5(a) the viewer shows the answer as a line in the message log; since ARC-UI-1127 (E8-g) the creature's sheet window of Read Aura is built too ([below](#arc-ui-1127--identify-sheet-heal-another-critter-and-lockpicks-on-containers-slice-e8-g-2026-11-12)); the container's inventory window of See Contents and the item description window are the rest of slice E8-g, so **those windows are a known difference**, not a bug. Compare in both games with the same hero (Divination college: See Contents 6, Read Aura 7, Divine Magick 9).

- [ ] **ARC-MAGIC-032 #1.** Cast Read Aura (spell 7) at a creature.
  - Expect: The original opens the character sheet of the creature, read only (no + or − buttons, all four tabs), the same window as the dialog's "show your abilities" reply. Steamwoven since ARC-UI-1127: the same read-only sheet for an NPC (a line "identify: ... (its character sheet)" in the message log too); a creature that is the hero keeps the line only.
  - Notes:
- [ ] **ARC-MAGIC-032 #2.** Cast See Contents (spell 6) at a **locked** container.
  - Expect: The original opens the container's inventory at once, with no lock, jam or magically-held test, so the items of a locked chest show. Steamwoven: the line only.
  - Notes:
- [ ] **ARC-MAGIC-032 #3.** In step 2, take or move an item in that window.
  - Expect: Not read in the executable (what a press may move in inventory mode 4): note what the original allows (take, put, nothing).
  - Notes:
- [ ] **ARC-MAGIC-032 #4.** Cast Divine Magick (spell 9) on an unidentified magickal item in the pack (a wand or a ring with a "?" description).
  - Expect: The item's name and description become the true ones, its spell slots and charge gauge show, and its worth in a trader's window is its real worth. Steamwoven (ARC-MAGIC-1128): pressing the spell opens the inventory with the spell cursor over it; a left click on the item casts at it (the cursor stays, a right click cancels and closes the window).
  - Notes:
- [ ] **ARC-MAGIC-032 #5.** Cast Divine Magick on an item that was already identified.
  - Expect: The description shows again; nothing else changes.
  - Notes:

### ARC-MAGIC-033 — Resist Magick counts in the magic save (D6c, 2026-11-11)

Setup: The original's save reads the stored magic resistance only, so Resist Magick (spell 40) never helped. The owner decided (D6c of the [audit](../research/magic-audit-res-103.md#decisions-for-the-owner)) that this bug is fixed in **both modes with no "Original" setting**: the save, the entangle re-resistance and the AI's fail-chance predictor read the stored resistance under the target's `resistmagic` effect changes ([rules](../world/durable-effects-runtime.md#consumers-of-resistance-and-stat-changes-arc-magic-027), [ledger RS11 to RS13](../research/ledger/magic.md#resist-magick-in-the-magic-save-arc-magic-033)). These steps compare with the original to see the difference it makes; they are not meant to match it.

- [ ] **ARC-MAGIC-033 #1.** In the original, with a hero that has a modest magic resistance (none is fine), let an enemy caster cast a resistable spell (one with a save: Stun, Charm, Entangle) at you 20 times with nothing on you and 20 times under Resist Magick (spell 40). Count how often it lands.
  - Expect: The same rate in both runs (the save ignores the effect). In Steamwoven the rate drops under Resist Magick: +25 magic resistance on a hero with 0 stored means a d100 below 25 resists (for a hero without a negative aptitude).
  - Notes:
- [ ] **ARC-MAGIC-033 #2.** In the original, cast Resist Magick on a companion and let an enemy caster target it with a spell that has no save (for example a Damage-and-effect spell with `No_Save`), 20 times with and without.
  - Expect: No difference in the original. In Steamwoven about a quarter more casts are resisted and skipped.
  - Notes:
- [ ] **ARC-MAGIC-033 #3.** Cast a spell with a save at an enemy that has a high stored magic resistance (an immune-type creature) while it holds Resist Magick (spell 40) or Weaken (spell 46).
  - Expect: The original: the creature resists as it always did. Steamwoven: a blessing never makes an immune creature easier; an effect that lowers the magic resistance (Weaken, spell 46, if its effect 154 does) lowers it.
  - Notes:
- [ ] **ARC-MAGIC-033 #4.** Stack Resist Magick several times on yourself (or Resist Magick plus Shield of Protection) and let a caster hit you.
  - Expect: The original's sheet stops at 95 % and the save ignores it all. Steamwoven's save stops at 95 % for the hero and ordinary creatures.
  - Notes:

### ARC-MAGIC-034 — Dispel: Disperse Magick and Dweomer Shield (MA-3, 2026-11-11)

Setup: The rules are in [Dispel](../world/spell-cast-host.md#dispel-arc-magic-034), the executable reading in [DP1–DP3](../research/ledger/spell-runs.md#dispel-component-arc-magic-034). Compare in both games with the same hero and spells.

- [ ] **ARC-MAGIC-034 #1.** Cast Shield of Protection (30) on yourself, then Dweomer Shield (42) on yourself.
  - Expect: The Dispel at the start ends your maintained spells (Shield of Protection ends, its bar slot frees); Dweomer Shield's own run is left and keeps its slot. Ours ends the runs the target is the source of or is aimed at, but never the dispelling run.
  - Notes:
- [ ] **ARC-MAGIC-034 #2.** Cast a maintained spell on a creature (for example Entangle), then cast Disperse Magick (41) on the creature.
  - Expect: The spell ends (its END runs: the flag is taken off). Ours ends the runs aimed at the creature the same way.
  - Notes:
- [ ] **ARC-MAGIC-034 #3.** Cast Flesh to Stone (48) on a creature, then Disperse Magick (41) on it.
  - Expect: The creature is no longer held (it acts again). The original clears the stoned spell flag and then the stoned object flag (0x43D280): the creature stops looking like stone and acts again. Ours clears both flags (ARC-MAGIC-039; MA-3 wrongly left the object flag set). Compare how the creature looks after the Dispel in both games and whether it acts at once. Record what you see.
  - Notes:
- [ ] **ARC-MAGIC-034 #4.** Cast Disperse Magick (41) on a creature that a companion or a creature cast a maintained spell on, and on a creature a technology item's effect holds.
  - Expect: The spells of a magic record end; the effects of a technological record (a gadget, a schematic) are never ended by it.
  - Notes:

### ARC-MAGIC-037 — Locks and holds: Unlocking Cantrip and Magelock (MA-6, 2026-11-11)

Setup: The rules are in [Locks and holds](../world/spell-cast-host.md#locks-and-holds-on-portals-and-containers-arc-magic-037), the executable reading in [LM1–LM6](../research/ledger/spell-runs.md#lock-flags-of-portals-and-containers-arc-magic-037). No viewer smoke stage casts at a door, so every step here is by hand. Compare in both games with the same hero (a mage who knows Unlocking Cantrip of the Conveyance college and Magelock of the Temporal college), the same door and the same chest; use the hero's own "locked" door of a house and a locked chest, and a door with a high lock level for step 3 (pick it with lockpicks first to see how hard it is).

- [ ] **ARC-MAGIC-037 #1.** Cast Unlocking Cantrip (1) on a locked door with a low lock level (below 20), then open it. Close it, wait more than one game hour (rest or let the clock run), and try the door again.
  - Expect: The door unlocks and opens. **It is still unlocked after the hour**: the spell writes the lock field itself and schedules no relock (a key or lockpicks relock an hour later). Ours does the same; if the original relocked it after an hour, note it here.
  - Notes:
- [ ] **ARC-MAGIC-037 #2.** Cast Unlocking Cantrip (1) on a locked chest with a low lock level and open it; then do the same with the lockpicks on a second locked chest and wait an hour.
  - Expect: The spell opens the chest and the chest stays unlocked for good; the lockpicked chest locks again after the hour (and, as before, a spilled or looted chest keeps its contents). Ours matches.
  - Notes:
- [ ] **ARC-MAGIC-037 #3.** Cast Unlocking Cantrip (1) with a hero of low magical aptitude on a door or chest whose lock level is above 19 and above the hero's aptitude (the character sheet's Magick aptitude line; the lock level is the lock's difficulty shown by the pick-locks check). Then raise the aptitude (an aptitude-giving item or a higher level) to at least the lock level and cast again.
  - Expect: The first cast fails: the lock stays and the "resisted" feedback (the resisted effect, spell.mes 643 for the player) plays on the door. At aptitude not below the level the lock opens. A lock of level 19 or lower opens at any aptitude. Ours matches (the "resists" line and message 643).
  - Notes:
- [ ] **ARC-MAGIC-037 #4.** Stand near a guard or the owner of the door (an NPC who watches it) and cast Unlocking Cantrip on it.
  - Expect: The original calls the object's witnesses (0x4AEE50) after the cast, whether the lock opened or not: NPCs within 10 tiles that guard the object (or are guards) and see or hear the caster react, an unlock making them hostile. A door or chest with no owner alerts nobody. **Ours does nothing yet** (the guards' reaction to a lock crime is not built; the cast log only has a line). Record what the original's guards do.
  - Notes:
- [ ] **ARC-MAGIC-037 #5.** Cast Magelock (75) on an unlocked, closed door, then try to open it. Cast it on an open door and try to close it.
  - Expect: The held door does not open (the locked sound plays, the hover line shows the held text) and a held open door cannot be closed either. When the spell ends (its duration or maintenance), the door works again. Ours matches.
  - Notes:
- [ ] **ARC-MAGIC-037 #6.** Cast Magelock on a door, then Disperse Magick (41) on the same door. Then cast Magelock on a door twice if the game lets you (two casters, or the spell again on a door it already holds).
  - Expect: Disperse Magick clears the hold at once. The area of Magelock excludes a door that is already held, so the second cast is refused at the target. If two holds stack (two casters), the first end clears the bit although the other spell goes on. Ours does the same; record whether the original allows the second target.
  - Notes:

### ARC-RES-120 — Function triage of the exe band 0x55-0x59 (2026-11-11)

Setup: The triage changes no behaviour. These are the places where it found original interface and world-map behaviour that our code does not reproduce at all ([ledger H2-1 to H2-13](../research/ledger/census-triage-h2.md#what-the-reading-found-that-the-earlier-documents-did-not-say)); each is read from the executable, not seen in game, so each step says what the original is expected to do.

- [ ] **ARC-RES-120 #1.** Walk or travel on the outdoor map for 5 to 11 hours of game time, then rest on the outdoor map (the rest window open) and at night.
  - Expect: An encounter event fires every 300 to 700 game minutes outside named areas; its chance is 5 percent per roll, times 5 while resting, from a table by region, and a creature group (chosen by terrain, density, night and the hero's level) appears on grid rings near the party and ends the journey or wakes the rest (0x559640, 0x558DE0, 0x559010, 0x559260). Ours has no encounters.
  - Notes:
- [ ] **ARC-RES-120 #2.** Start a world-map journey with a companion and a maintained spell, then watch the party, the music and the maintained spell; also press W in combat and on a map other than the start map.
  - Expect: The party is hidden and the map music plays while travelling and the party shows again on leaving; the hero is asked to interrupt its actions before each segment and the maintained spells drop; the map opens from any map, leaving combat mode first when it can (0x5615D0, 0x565140, 0x5607E0).
  - Notes:
- [ ] **ARC-RES-120 #3.** Walk the hero on the outdoor map into the radius of a town you have not discovered, without opening the world map.
  - Expect: Entering the new sector marks the town known and adds its map marker (0x564F60, 0x564A70). Ours discovers only on world-map travel, dialog and scripts.
  - Notes:
- [ ] **ARC-RES-120 #4.** Start a talk with a hostile NPC while in combat mode, then with an NPC that stands with its back to the hero.
  - Expect: The dialog start is refused for a dead or unconscious hero, an NPC that cannot speak, a second dialog and a hero that cannot leave combat mode, and the NPC turns to face the hero (0x567460, 0x568540). Ours skips the refusals and the turn.
  - Notes:
- [ ] **ARC-RES-120 #5.** Put a spell on a hotkey, unlearn it in the editor, look at the bar; put an armour on a hotkey while another armour is worn and press it; drop a weapon whose spell pane is open.
  - Expect: The unlearned spell's hotkey empties; the key swaps the worn item and puts the old one back when the new one is refused; dropping the weapon closes the pane and clears the two quick slots that name it (0x57F340, 0x550000, 0x5570D0).
  - Notes:
- [ ] **ARC-RES-120 #6.** Open the hero's inventory with a heavy weapon and a low strength (for example strength 5 with a two-handed sword), then hover items in a shop and use the gamble button.
  - Expect: The damage and armour numbers, a weight line and an encumbrance line "Name (limit)", gold and four ammunition counts show beside the doll and the weapon is tinted red below its minimum strength; the barter hover writes a quote with a haggle percentage; the gamble button needs the merchant's gambling points and a limit by the merchant's gambling level (0x576520, 0x578330, 0x579840).
  - Notes:
- [ ] **ARC-RES-120 #7.** Press Enter with a companion in the party and type the companion's name and a command; use a scroll or a lockpick from the inventory and click a target; press I twice with the inventory open.
  - Expect: The line is sent as a broadcast order with the tile under the pointer; an item that needs a target opens the target cursor and walks next to the target when charged or out of range, lockpicks take their own path unless Ctrl is held; the second I closes the window and puts a held item back (0x571950, 0x571AA0, 0x572340).
  - Notes:
- [ ] **ARC-RES-120 #8.** With a party whose follower has a better disarm skill than the hero, use skill-bar Disarm Traps on a trap (once plain, once with Ctrl held); then repair an undamaged item from the skill bar with the same party.
  - Expect: Disarm Traps: the best follower does the job and a hand-over line floats over that follower, unless either Ctrl key is held, then the hero does it (0x57A620, called by 0x57A320 only for the disarm skill; the Follower Skills option, on by default, switches the hand-over off). Repair is **not** handed over: it stays with the hero even when a follower repairs better, and the "nothing to repair" line floats over the hero (0x57AB10); the other skill-bar skills (pick pocket and the rest) do not hand over either. The skill result lines float over the user (0x57A770 and copies). Ours: built by ARC-COMBAT-1777 for both jobs (the lockpick use as the follower's own pick goal; the viewer cannot tell the left Ctrl key from the right one, and the follower's critical-success device lands in the hero's pack) ([ledger OH4](../research/ledger/ui-audio.md#hidden-options-arc-res-1107), [plan E8-e5](missing-systems-plan.md#e8-c-and-e8-e-slices-read-by-arc-res-1107)).
  - Notes:
- [ ] **ARC-RES-120 #9.** Use a bed in a town (or press the wait key where the town map allows it) and rest to the chosen length.
  - Expect: The rest window steps one game hour per 200 ms, heals the sleeper and the followers each hour, drops maintained spells, fires script event 29 at the followers and ends at the chosen length, at 07:00, at 20:00 or at full life; an unconscious hero, combat that cannot be left, a refusing bed script or an occupied bed refuse (0x57B180, 0x57B710, 0x57B7F0, 0x57BAC0). Ours has no rest.
  - Notes:
- [ ] **ARC-RES-120 #10.** Open the character sheet and click its fourth tab; close the sheet in the new-character editor with damage on the hero.
  - Expect: The tab lists every named auto-level scheme (15 rows, scrollbar) and a click sets the shown character's scheme; closing in creation mode clears hit-point and fatigue damage (0x55D060, 0x55D210, 0x55DF90, 0x55A150).
  - Notes:
- [ ] **ARC-RES-120 #11.** Open Options from the main menu and in play.
  - Expect: Three pages of eight slots (module, difficulty, violence filter, combat mode, auto attack, auto switch weapons, always run, follower skills; brightness, text duration, floats, float speed, combat taunts; three volumes) and a combat mode with three values including fast turn-based (0x589280, 0x589610, 0x5896A0). Ours shows the Follower Skills line since ARC-COMBAT-1777 (on by default, saved as `follower skills`), fewer other slots and two combat values (ledger OP2).
  - Notes:
- [ ] **ARC-RES-120 #12.** Maintain three spells and lower the hero's intelligence below the number maintained times four (a curse); stand across an hour change; let the hero die.
  - Expect: Slots close and their spells end (0x553670); the ambient light re-applies the hour every 3,600,000 ms of game time (0x57D620); a dead hero resets the game to the main menu after 5 s (0x57D410).
  - Notes:
- [ ] **ARC-RES-120 #13.** Read a long book and a newspaper; right-click a follower portrait of a mind-controlled follower and of one that cannot speak.
  - Expect: The text wraps by pixels into page and column rectangles (ours cuts at about 700 characters, 0x56C800, 0x56CAA0, 0x56CB60); the menu greys Wait for a mind-controlled follower and Inventory when the can-open test refuses (0x56B970, 0x575080).
  - Notes:

### ARC-UI-133 — Click on the hero's lens closes the world map (PR pending, 2026-11-11)

Setup: Read in the original executable (ledger [WM140](../research/ledger/ui-audio.md), WM36): in the window's top left the executable registers a square, 89 × 89 pixels at window (25, 24), the hole of the background art, as the hero's lens (the same view-window mode as the logbook's lens). A left release inside that square closes the map, as the key `W` does. The circle in the brass ring is only what is drawn around the square; the click test is the whole square, corners included. Known differences, not to be re-reported: in the original the lens shows the game around the hero at half size with the cover art 198 over it; in ours the square still shows the world behind the window, unscaled, and no cover art (its own task, because it also decides the ARC-UI-126 look of the hole). In both rule modes the click behaves the same.

- [ ] **ARC-UI-133 #1.** Open the world map (`W` or the bar's Map button) and click the small window in the top left of the brass frame, in the middle of the circle.
  - Expect: The map closes and the game world is shown again, on press and release of the left button (the release does it).
  - Notes:
- [ ] **ARC-UI-133 #2.** Open the map, press the left button on the lens, keep it down, move the pointer out onto the wood and release there.
  - Expect: Nothing happens (the test is on the release, at the release point); the map stays open.
  - Notes:
- [ ] **ARC-UI-133 #3.** Open the map and click in a corner of the square, outside the round ring (the corner where the wood shows through), and just outside the square on each side.
  - Expect: The corner of the square closes the map; a click one pixel outside the square does not.
  - Notes:
- [ ] **ARC-UI-133 #4.** Open the map, start a journey (Travel), and click the lens while it runs.
  - Expect: The map closes through the same close as `W`; the walking party stops where it stands (WT12). In ours this lands the party at its current place.
  - Notes:
- [ ] **ARC-UI-133 #5.** In a town-map sector (kind 2) and in the continent view (button below the zoom), click the lens.
  - Expect: The map closes in every view.
  - Notes:
- [ ] **ARC-UI-133 #6.** With the HD interface switch on, repeat step 1.
  - Expect: The same as in the original look: the click works in HD too.
  - Notes:

### ARC-MAGIC-035 — Social and AI flags: Charm, Dominate Will, Nightmare, Flash, Create Undead (MA-4, 2026-11-11)

Setup: The rules are in [Social and AI flags](../world/spell-cast-host.md#social-and-ai-flags-arc-magic-035), read from the executable in [SO1 to SO8](../research/ledger/spell-runs.md#social-and-ai-flags-arc-magic-035). Compare them in both games with the same hero (a mage with these spells; a hero without the college can learn them from the spell book by cheat or lend them through a wand). No viewer smoke stage drives these spells, so these steps are the only run of the whole path.

- [ ] **ARC-MAGIC-035 #1.** Cast Charm (spell 35) on a calm human who is not yours, then talk to them.
  - Expect: Their reaction toward you is 30 higher than before the cast; when the spell ends it drops by 30 again.
  - Notes:
- [ ] **ARC-MAGIC-035 #2.** Cast Charm Beast (spell 50) on an animal that is attacking you.
  - Expect: The animal stops attacking you (peace with you). When it ends the reaction does not change except by the -30 of a charmed NPC.
  - Notes:
- [ ] **ARC-MAGIC-035 #3.** Cast Dominate Will (spell 39) on a hostile human.
  - Expect: Their reaction drops by 50 at once, they join you as a follower (they follow you and will not flee), and fight on your side. When the spell ends they leave your party (the reaction stays low).
  - Notes:
- [ ] **ARC-MAGIC-035 #4.** Cast Control Beast (spell 52) on an animal.
  - Expect: The same as step 3: the animal follows you and does not flee; at the end it is free again.
  - Notes:
- [ ] **ARC-MAGIC-035 #5.** Cast Nightmare (spell 38) on a human who fights you.
  - Expect: They run away from you (a flee goal) and do not attack while it lasts; at the end they stop fleeing.
  - Notes:
- [ ] **ARC-MAGIC-035 #6.** Cast Flash (spell 66) on a human who fights you, and watch their hits.
  - Expect: They are blinded (their melee is much worse); at the end, or by chance each AI pass, the blindness goes.
  - Notes:
- [ ] **ARC-MAGIC-035 #7.** Cast Create Undead (spell 58) on a corpse.
  - Expect: The creature rises under your control (it follows you), and at the spell's end it dies again. Note whether it is hurt by healing or poison as an undead would be.
  - Notes:
- [ ] **ARC-MAGIC-035 #8.** Dominate Will on a creature that is already your follower, and on one that is summoned.
  - Expect: A summoned creature does not lose reaction (no -50).
  - Notes:
- [ ] **ARC-MAGIC-035 #9.** Charm a kill-on-sight creature of another faction (a hostile wolf pack member is one), end the spell, then walk near it without attacking.
  - Expect: While charmed and after the end it does not attack you on sight for being of another faction (the reaction and alignment tests still apply); after you attack it, it is hostile as before (the attack takes the override off).
  - Notes:

### ARC-DEV-372 — Shared content index (O4 of the performance diagnosis, 2026-11-11)

Setup: The viewer's readers now share one index of the game folder per run instead of each indexing it again. Nothing in the game's behaviour is meant to change, so the steps compare the start and the first window opens with the original game: the same texts, names and numbers, and nothing missing that the original shows.

- [ ] **ARC-DEV-372 #1.** Start a new game and read the first screen: the hero's name and stats, the first dialog, the inventory (`I`), the character sheet (`C`) and the logbook (`L`).
  - Expect: The texts, names and numbers are the ones the original shows for the same hero and map; no window opens empty or with a missing label.
  - Notes:
- [ ] **ARC-DEV-372 #2.** Open the world map (`W`), the spell pane (`M`) and the schematics (`T`) on the first map, then walk to a cave or another map and open the same windows again.
  - Expect: Same content as before the change; the second map's windows are as complete as the first's.
  - Notes:
- [ ] **ARC-DEV-372 #3.** Look at an NPC with a hover name, a book or note to read, a shop's barter window and a companion's greeting.
  - Expect: Names, texts and prices match the original; none is blank.
  - Notes:
- [ ] **ARC-DEV-372 #4.** Start the game, quit, and start it again.
  - Expect: The second start shows the same content as the first.
  - Notes:

### ARC-UI-134 — The hero's lens on the world map shows the game around the hero (PR pending, 2026-11-11)

Setup: Read in the original executable (ledger [WM141](../research/ledger/ui-audio.md), WM140): the square in the window's top left is the same view-window lens as the logbook's, the inventory's and the barter window's. While the map is open it shows the game around the hero, scaled to half size (178 game pixels become the 89-pixel square), with the ring art 198 over it; the open centres the camera on the hero first. While a journey runs the square is black under the ring art, and the live view returns when the journey ends. Both rule modes behave the same; with the HD interface switch on the frame, the ring and the buttons are the HD art and the lens is the same picture. Known differences, not to be re-reported: the copy reads what the layers under the window drew, floaters included, where the original copies the game view alone; whether the HD frame's square cutout against the brass ring looks right is reported separately (ARC-UI-126).

- [ ] **ARC-UI-134 #1.** Stand somewhere with scenery and creatures around the hero and open the world map (`W` or the bar's Map button).
  - Expect: The top-left square shows the game around the hero at half size, the hero in the middle of it (the middle is slightly below the centre of the view), under the brass ring; the map itself is unchanged.
  - Notes:
- [ ] **ARC-UI-134 #2.** Move the hero's neighbours (wait a few turns, or stand beside a walking creature or a flame), then open the map and look at the lens for a moment.
  - Expect: The lens is live: animations in it keep running while the map is open.
  - Notes:
- [ ] **ARC-UI-134 #3.** Open the map twice from different places (the second time after walking a few tiles).
  - Expect: The lens always centres on the hero; the game view under it was centred on the hero by the open.
  - Notes:
- [ ] **ARC-UI-134 #4.** Set a waypoint and press Travel.
  - Expect: The square turns black under the ring at once and stays black for the whole journey.
  - Notes:
- [ ] **ARC-UI-134 #5.** End a journey (arrival, or the right button or Delete during it).
  - Expect: After a right button or Delete the live view is back in the lens; on arrival the map closes.
  - Notes:
- [ ] **ARC-UI-134 #6.** Open the map in a town-map sector and switch kinds (zoom, whole map, `Z`), and use the note quill (`Q`).
  - Expect: The lens stays live in every kind and with the quill; only a journey blackens it.
  - Notes:
- [ ] **ARC-UI-134 #7.** Click the lens, in the middle and in the corner.
  - Expect: The map closes (ARC-UI-133); the lens picture does not change that.
  - Notes:
- [ ] **ARC-UI-134 #8.** With the HD interface switch on, repeat steps 1 and 4.
  - Expect: The same lens in the HD frame: the ring over the square; the corners of the square show wood, not a hole. If a corner still shows the world through, report it.
  - Notes:

### ARC-UI-136 — One brass ring around the hero's lens in HD (PR pending, 2026-11-11)

Setup: With the HD interface switch on, the lens on the world map showed a doubled brass edge at the ring's top and upper right: the ring of the window frame (outside the lens square) and the ring of the cover art 198 (inside it) were painted separately and did not coincide. The ring of the window frame `mapmain` is rebuilt from the cover's ring ([HD interface art](../rendering/hd-interface-art.md#world-map-window-arc-ui-126)); the Legacy art is untouched. The repair is a local pack change (`lens_ring.py`, then `hd_pack.py apply`, backup and rollback), and it is installed in the pack the game reads (manifest `696edbd2…`, backup `ARC-UI-136-v2-cde8a57839e6`, `hd_pack.py rollback` undoes it). The cause and the method are in the same section; no frame is in git. Known difference, not to be re-reported: the cover's corner wood and the window frame's wood have a slightly different grain, which leaves a faint square patch at the lens corners (not part of this repair).

- [ ] **ARC-UI-136 #1.** In Legacy (HD interface off) open the world map (`W`) and look closely at the ring around the top-left lens, at the top and the right side.
  - Expect: The original: one clean brass ring, no second edge.
  - Notes:
- [ ] **ARC-UI-136 #2.** With the HD interface on, open the world map and look at the same ring, top and right side, with the lens live and again during a journey (black lens).
  - Expect: One continuous brass ring like the original, no doubled edge at the top or at the right; the ring is the same width all around; no seam where the ring meets the square's border.
  - Notes:
- [ ] **ARC-UI-136 #3.** Open the character sheet and the logbook in HD and look at their lens ring.
  - Expect: Unchanged (the cover art `pcwincvr` is not part of this repair).
  - Notes:
- [ ] **ARC-UI-136 #4.** Compare the HD ring with the Legacy ring for colour.
  - Expect: The rebuilt top and side arcs are the same warm brass as the arcs that were not rebuilt; report a visibly paler or blurrier arc.
  - Notes:

### ARC-DEV-371 — Script-created critters (E1-c3, 2026-11-11)

Setup: A script's create object (action 52) makes a critter when the prototype is a critter's ([E1-c3](../architecture/object-directory.md#e1-c3-script-created-critters-arc-dev-371)). The installed case on the start map is the "snapping twig" tile script (1629, 130 tiles): it creates eight critters of one prototype next to the hero. Compare it in both games with the same hero on the same tile.

- [ ] **ARC-DEV-371 #1.** Walk onto a snapping-twig tile of the start map (130 tiles in the wilderness; one cluster is around tile (50851, 15844), far from the Crash Site; the smoke log lists the nearest five to the hero).
  - Expect: Eight creatures of one kind appear within three tiles of you, all at once, each on a tile of its own or sharing one when the search found no free tile. They stand a moment and then act as that kind does (hostile ones come at you).
  - Notes:
- [ ] **ARC-DEV-371 #2.** Walk away from them until they are far (a window move), then come back.
  - Expect: In the original an encounter creature that is left without a next AI tick outside a fight is destroyed: those that were left behind are gone, not standing where you left them. Compare how many remain.
  - Notes:
- [ ] **ARC-DEV-371 #3.** Step onto the same twig tile again, and onto the next twig tile.
  - Expect: Each step creates eight more; creatures made earlier are not removed by it.
  - Notes:
- [ ] **ARC-DEV-371 #4.** Kill one of them and look at what it leaves.
  - Expect: The snapping twig's creatures have no inventory source, so they leave nothing in the original too ([ARC-DEV-386](#arc-dev-386--created-critter-inventory-e1-c4-2026-11-11) gives a created creature its goods when its prototype has a source). Note what the original's creature drops.
  - Notes:
- [ ] **ARC-DEV-371 #5.** Stand a created creature on a tile that has a tile script (a trigger or a door-warning tile) if you can lure one.
  - Expect: It runs the tile script as any creature that walks onto the tile does.
  - Notes:
- [ ] **ARC-DEV-371 #6.** Save with the creatures alive and load.
  - Expect: They are where they last stood, and still count as encounter creatures.
  - Notes:

### ARC-DEV-386 — Created critter inventory (E1-c4, 2026-11-11)

Setup: The creature a script creates gets its goods the way the original's creation finish gives them ([E1-c4](../architecture/object-directory.md#e1-c4-created-critter-inventory-arc-dev-386); the restock body when the prototype has an inventory source, then the best-items wield). The twig's creatures (start map, [ARC-DEV-371](#arc-dev-371--script-created-critters-e1-c3-2026-11-11)) have no source; the smoke log line `ARC-DEV-386 script-critters stocked prototype=... source=...` names one critter prototype that has one, which the stage makes next to the hero.

- [ ] **ARC-DEV-386 #1.** Make a creature whose prototype has an inventory source (a script that creates a guard, a bandit or a merchant-type creature) and kill it.
  - Expect: Its body holds goods at once: coins and the items of its inventory set, and the best of them worn (it is dressed as a map creature of the same kind is). Compare the kind of items, the coins and what it wears with a map creature of the same prototype.
  - Notes:
- [ ] **ARC-DEV-386 #2.** Kill a created creature that has goods and open its body.
  - Expect: The loot window lists its goods; taking them works as on a map creature's body.
  - Notes:
- [ ] **ARC-DEV-386 #3.** Make the same creature twice (two steps on the same tile, or two scripts).
  - Expect: Each gets its own set (the draws differ), not a copy of one list.
  - Notes:
- [ ] **ARC-DEV-386 #4.** Make a creature, walk away until its map is left, come back.
  - Expect: An encounter creature that is left behind is gone with its goods (nothing of it is left to loot).
  - Notes:
- [ ] **ARC-DEV-386 #5.** Save the game a moment after a creature was made and load.
  - Expect: The creature keeps the goods it had when it was saved; the one made in the same instant as the save may come back empty (documented below).
  - Notes:

### ARC-MAGIC-036 — Spatial Distortion moves the caster along the route, at most eight tiles (PR pending, 2026-11-11)

Setup: Read in the original executable (ledger [MV1–MV3](../research/ledger/spell-runs.md#movement-component-and-the-immediate-path-relocation-arc-magic-036)): the Movement component of Spatial Distortion (spell 3) is no teleport. It asks the same path request the item drop uses and walks **at most eight tiles** of the route, running the tile script of every tile it walks, then puts the caster on the last tile. Compare in both games with a hero who knows the spell (Conveyance college, rank 4), on any open ground such as the Crash Site. Both rule modes behave the same. Known differences, not to be re-reported: the view is not re-centred beyond the camera following the hero. Teleportation (spell 4), the random tile, the area portal and the order of the tile scripts (before the placement) were built afterwards: see ARC-MAGIC-038.

- [ ] **ARC-MAGIC-036 #1.** Cast spell 3 at a clear tile 3 tiles away in the open.
  - Expect: The caster stands on the target tile at once; a cast animation or flash plays on the caster.
  - Notes:
- [ ] **ARC-MAGIC-036 #2.** Cast spell 3 at a clear tile 15 tiles away in a straight line.
  - Expect: The caster ends on the eighth tile of the way, not on the target (the original cuts the route at eight).
  - Notes:
- [ ] **ARC-MAGIC-036 #3.** Cast it at a tile behind a wall or across a closed door where the way round is long.
  - Expect: Either the caster ends up to eight tiles along the walking route, or, when there is no route, nothing moves and the fatigue is still paid.
  - Notes:
- [ ] **ARC-MAGIC-036 #4.** Start a click-walk far away, then cast spell 3 at a near tile while the hero walks.
  - Expect: The walk stops with the move; the hero stands on the target and does not walk on.
  - Notes:
- [ ] **ARC-MAGIC-036 #5.** Cast it so that the route crosses or ends on a tile with a script (a trap-like tile, a door sill with a script).
  - Expect: The tile's script runs; the last tile's script runs twice (watch for a message that appears twice).
  - Notes:
- [ ] **ARC-MAGIC-036 #6.** Cast it at a tile with a creature on it.
  - Expect: `Tgt_Tile_Empty` refuses the tile: the cast does not pick it.
  - Notes:

### ARC-MAGIC-038 — Teleportation opens the world map; the teleport ban areas (PR pending, 2026-11-11)

Setup: Read in the original executable (ledger [TP1–TP5](../research/ledger/spell-runs.md#teleport-types-and-the-anti-teleport-test-arc-magic-038), contract [teleport types](../world/spell-cast-host.md#teleport-types-and-the-anti-teleport-test-arc-magic-038)): in a single-player game Teleportation (spell 4, Conveyance college) moves nothing by itself. It opens the **world map in a teleport mode**; you click a known area, and the hero lands there. Places where teleporting is refused come from the data file `Rules\AntiTeleport.mes` (2 round regions and 5 whole maps in the installed game). Both rule modes behave the same. Use a hero who knows Teleportation, with enough fatigue (the spell's fatigue cost is charged only when an area is picked). Known differences, not to be re-reported: the original's teleport mode offers no town map and no route points either (ARC-MAGIC-1147, ledger TP6–TP7; see the added steps below); the refusal message (the original's message 640) is shown as an English label, not the game's text; the view is not re-centred; an NPC cast of the area portal (technology 218) or of a random move does not carry the creature; a multiplayer host is not built.

- [ ] **ARC-MAGIC-038 #1.** Cast Teleportation in the open (outside any ban area).
  - Expect: The world map opens at once in teleport mode (the town map is not shown). No fatigue is taken yet.
  - Notes:
- [ ] **ARC-MAGIC-038 #2.** Click an area you have discovered, 320 tiles or closer to the click point.
  - Expect: The hero lands on that area on the main map, the map window closes, and the spell's fatigue is taken now (a hero too tired to pay still lands). Repeat from a town or dungeon map: the teleport map opens there too and the click picks the area.
  - Notes:
- [ ] **ARC-MAGIC-038 #3.** Click open sea or a place with no discovered area within reach.
  - Expect: Nothing happens; the map stays open and nothing is paid.
  - Notes:
- [ ] **ARC-MAGIC-038 #4.** Close the map (Escape or its close button) without picking.
  - Expect: Nothing is paid and the hero stays where it was.
  - Notes:
- [ ] **ARC-MAGIC-038 #5.** Cast it in a map the file disables (five maps in the installed data), or inside one of the two regions on the main map.
  - Expect: The cast is refused with a message at once and the world map does not open; no fatigue is taken.
  - Notes:
- [ ] **ARC-MAGIC-038 #6.** Open the teleport map and pick an area that lies inside one of the ban regions.
  - Expect: The pick is refused with a message and the map stays open; nothing is paid. Pick another area to leave.
  - Notes:
- [ ] **ARC-MAGIC-038 #7.** Use the technology device that teleports to the area (record 218) on the main map, and in a map that is not the main one.
  - Expect: On the main map the hero is sent to the area's location with a blank screen and no wait orders; elsewhere to the main map at the location of the area that map belongs to.
  - Notes:
- [ ] **ARC-MAGIC-038 #8.** Walk with Spatial Distortion (spell 3) over a tile that has a script, starting from a tile next to it.
  - Expect: The script runs **before** the caster is put on the last tile: a message from it names the caster's old tile; the last tile's script runs once more afterwards.
  - Notes:
- [ ] **ARC-MAGIC-038 #9.** (ARC-MAGIC-1147) In the teleport map, look for the Travel button, press the town-map button and the `Z` key, and click the map while a companion travels with you.
  - Expect: No Travel button is shown; there is no town map (not even from a town, where `W` would open it); a click picks a discovered area or does nothing, it never adds a route point; the compass arrows scroll and the continent view works as usual.
  - Notes:
- [ ] **ARC-MAGIC-038 #10.** (ARC-MAGIC-1147) Join a companion, cast Teleportation and pick a far discovered area.
  - Expect: The companion arrives with you and stands next to you at the destination, whatever distance it was from you before the cast; a companion you had released earlier on a foreign map is not carried (it goes home).
  - Notes:

### ARC-RES-121 — Function audit wave 3, groups G and I (2026-11-11)

Setup: The audit changes no behaviour. It first named two places where the original behaves in a way our code does not reproduce and the export showed no caller for ([ledger GI-5](../research/ledger/census-triage-gi.md#findings-of-wave-3)). Task T5 (ARC-RES-123) then searched the whole executable bytes and found no reference to either, so both are dead code as far as a static search shows: the typed-order garbling 0x4C3120 is not run (an order should arrive whole) and the bar hide toggle 0x54ACB0 is not what F10 runs. One confirmation of the live F10 path remains.

- [ ] **ARC-RES-121 #1.** Start the original game with `-fullscreen` and press F10 on a normal map (without that start option the toggle does nothing).
  - Expect: The interface hides or shows through the interface hide toggle 0x5578E0 (the main loop key handler's F10 branch; [UI ledger](../research/ledger/ui-audio.md) PA4). The older bar toggle 0x54ACB0 has no caller anywhere, so it is not what happens. Ours only reserves the key.
  - Notes:

### ARC-DEV-387 — The start map loads only when a new game needs it (O1 of the performance diagnosis, 2026-11-11)

Setup: The start map is no longer loaded when the game starts with the menu ([early menu](../rendering/terrain-viewer.md#main-menu-arc-ui-011)). It begins to load when you enter the New Game window (the pre-made heroes or New Character); Last Save and a save chosen in the Load Game window load the map of the save itself, once. The original loads the map after your choice, not before, so compare the order and the waiting. Compare on a start with the menu (`./arcanum area`, or a packaged build); the first build for this comparison is any build from `main` that contains this task. Have at least one save, ideally made on a map other than the start map (for example inside a town) and one made on the start map.

- [ ] **ARC-DEV-387 #1.** Start the game, open Single Player and Options, change a setting, go back; do not press New Game. Watch the machine (Activity Monitor) or the log line `ARC-DEV-387 start map load begins`.
  - Expect: The menu works at once and stays light: no map is loaded behind it, as in the original, which has no map before a choice. The menu has no music in this build (known difference: it plays through the start map's session, a follow-up).
  - Notes:
- [ ] **ARC-DEV-387 #2.** Press Last Save with a save made in a town.
  - Expect: The screen goes black with the clock animation in the top-right corner (two pixels in; no wait note; ARC-UI-1362) and takes no input; after the load the game is on the town map at the saved place with the saved hero. The start map is not shown at any point. The wait is one map load, not two.
  - Notes:
- [ ] **ARC-DEV-387 #3.** The same with Load Game and a row of the list (the window opens at once, before any map).
  - Expect: The same as step 2, for the chosen row. A row of a save on the start map works too.
  - Notes:
- [ ] **ARC-DEV-387 #4.** Press Last Save with no save at all.
  - Expect: The window stays and the menu says there is no save to continue from, with no wait and no map load (the original goes back to the window without a message; the wording is the project's).
  - Notes:
- [ ] **ARC-DEV-387 #5.** Press New Game, then the pre-made browser at once (before the map is ready) and browse; press Start.
  - Expect: The browser works at once. Start says the world is still loading and the game starts by itself when the map is ready (the one visible difference from the original). Escape takes it back.
  - Notes:
- [ ] **ARC-DEV-387 #6.** Press New Game, then Back out to Single Player and choose Last Save while the start map is still loading.
  - Expect: The save's own map loads instead and the game starts on it; the start map's load is given up. The result is the same as step 2.
  - Notes:
- [ ] **ARC-DEV-387 #7.** Press New Game, press New Character and go through the window, the editor and the shop.
  - Expect: The wait note first (the new character's window needs the map's start); then the windows work as before.
  - Notes:
- [ ] **ARC-DEV-387 #8.** Choose a save whose file is damaged or from another game (copy one and break it).
  - Expect: The menu comes back with the reason; nothing else changes.
  - Notes:

### ARC-UI-139 — The main menu's music plays without a scene (follow-up of ARC-DEV-387, 2026-11-11)

Setup: The menu plays its music pair (the shop map's start sector, scheme 26 `arcanum.mp3`) through the scheme player of its own, without a game session or a scene, from the moment it opens until a game starts or a save loads ([scheme player](../audio/scheme-player.md#viewer-integration)). Compare on a start with the menu (`./arcanum area`, or a packaged build) with the music volume above 0, in both games.

- [ ] **ARC-UI-139 #1.** Start the game and wait on the main menu without pressing anything for a minute or two.
  - Expect: The main theme plays from the first moment of the menu and loops without a gap or restart.
  - Notes:
- [ ] **ARC-UI-139 #2.** Open Single Player, Options (change the music volume on the Sound page), New Game and the pre-made browser, then go back.
  - Expect: The theme goes on through every window, and the volume follows the slider.
  - Notes:
- [ ] **ARC-UI-139 #3.** Press New Game, then New Character, so that the start map loads under the menu.
  - Expect: The theme does not stop and does not restart when the map has loaded (the original has no pause there either).
  - Notes:
- [ ] **ARC-UI-139 #4.** Press Last Save, or choose a row in Load Game, while the theme plays.
  - Expect: The theme plays until the save is loaded, then the map's own music takes over.
  - Notes:
- [ ] **ARC-UI-139 #5.** Start a new game with a pre-made hero.
  - Expect: The theme stops and the Crash Site's music (or its night ambience) starts; the theme does not come back.
  - Notes:
- [ ] **ARC-UI-139 #6.** Press Esc in play and open the in-play menu.
  - Expect: The game's own music goes on (the theme is only the main menu's).
  - Notes:

### ARC-WORLD-124 — Timed bombs: placing trap devices with a spell (E6-b, 2026-11-11)

Setup: The rules are in [placed trap devices](../world/trap-device-runtime.md). Give the hero the timed bomb (item 15154, record 176; or 15193, record 220) and compare in both games. The script trap devices (15100, 15120) are the next slice (ARC-WORLD-125, [below](#arc-world-125--script-trap-devices-arming-traps-on-objects-and-tiles-e6-c-2026-11-11)).

- [ ] **ARC-WORLD-124 #1.** Use the bomb from the inventory (Use icon) and click an empty tile a few cells away.
  - Expect: The hero walks next to the tile, the bomb leaves the pack and a marker object stands on the tile; nothing else happens at once.
  - Notes:
- [ ] **ARC-WORLD-124 #2.** Wait 88 game seconds (about 11 s of play at the normal speed).
  - Expect: The marker disappears and the explosion goes off on the tile: fire and normal damage to everything within two tiles, a flash, no knockback.
  - Notes:
- [ ] **ARC-WORLD-124 #3.** Place the record 176 bomb (15154) with a few people awake within ten tiles that can see the hero, then wait.
  - Expect: They turn on the hero as soon as the bomb is placed (not when it goes off); people who cannot see or hear it, and companions, do not.
  - Notes:
- [ ] **ARC-WORLD-124 #4.** Use the bomb on a critter or on a scenery object instead of a tile.
  - Expect: The bomb is spent with no check of the target and there is no marker; the explosion goes off at that object's place after the same 88 s.
  - Notes:
- [ ] **ARC-WORLD-124 #5.** Save while the fuse runs, load, and wait.
  - Expect: The marker is still there and the bomb goes off at the same moment.
  - Notes:

### ARC-WORLD-125 — Script trap devices: arming traps on objects and tiles (E6-c, 2026-11-11)

Setup: The rules are in [placed trap devices](../world/trap-device-runtime.md#script-trap-devices-arc-world-125). Give the hero the spike trap (item 15100, script 30001) or item 15120 and compare in both games.

- [ ] **ARC-WORLD-125 #1.** Use the device from the inventory (Use icon) and click a closed, unscripted chest or a door.
  - Expect: The hero walks next to it, the device leaves the pack, and the chest or door is now trapped (the trap is known to the hero).
  - Notes:
- [ ] **ARC-WORLD-125 #2.** Open that chest, or use that door.
  - Expect: The trap goes off on the hero as its script says.
  - Notes:
- [ ] **ARC-WORLD-125 #3.** Use the device on an NPC, a creature or a scenery object that is not a door or chest.
  - Expect: The device stays in the pack; the game shows its "cannot be set here" line (`mes\trap.mes` line 2).
  - Notes:
- [ ] **ARC-WORLD-125 #4.** Use the device on a chest or door that already holds a script (a trap or a lock script).
  - Expect: The same refusal, the device stays.
  - Notes:
- [ ] **ARC-WORLD-125 #5.** Use the device on an empty floor tile.
  - Expect: A trap marker (a hidden-trap sprite) stands on the tile and the device is spent.
  - Notes:
- [ ] **ARC-WORLD-125 #6.** Use a second device on the same tile.
  - Expect: The device stays and the game shows `mes\trap.mes` line 3.
  - Notes:
- [ ] **ARC-WORLD-125 #7.** Save and load with a trapped chest and a marker on the map.
  - Expect: Both are still there; the chest is still trapped.
  - Notes:

### ARC-UI-147 — The character editor of the new-character flow in the original's look (2026-11-11)

Setup: The editor is the in-game character sheet's original look between the menu's strips, with the opening shop's Accept and Back buttons ([viewer](../rendering/terrain-viewer.md#character-editor-in-creation-mode-arc-ui-034)). Compare in both games.

- [ ] **ARC-UI-147 #1.** New Game, New Character, give a name, Accept.
  - Expect: The editor opens as the character sheet (the wooden window with the portrait, the five points, the eight stats and the skills page) between the top and bottom strips, with the green arrow (Accept) and the red arrow (Back) in the bottom strip.
  - Notes:
- [ ] **ARC-UI-147 #2.** Press the + and − of a stat, of a skill, of a college's spell rank and of a tech degree; try a − before any + .
  - Expect: Points move as in the game's sheet; a − at the start values is refused (in the original with its text in the text bar, here in the note line at the top).
  - Notes:
- [ ] **ARC-UI-147 #3.** Click the portrait's round area.
  - Expect: Nothing happens: the editor does not close (in the game the same click closes the sheet).
  - Notes:
- [ ] **ARC-UI-147 #4.** Accept, then Back in the shop, then change a stat again.
  - Expect: The points spent before the shop can still be taken back.
  - Notes:

### ARC-UI-148 — Generated interface: the inventory window (S3, 2026-11-12)

Setup: The rules are in [viewer slice S3](../rendering/hd-interface-density.md#viewer-slice-s3-arc-ui-148). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game and press `I`. Compare with the same window under **HD** and **Legacy**.

- [ ] **ARC-UI-148 #1.** Open the inventory in a 1280 × 800 window, then press `I` again.
  - Expect: The pack grid, the doll grid, the count bars and the name box are drawn by code in the same places as in HD; the grid cells, the slots and every click land where they did.
  - Notes:
- [ ] **ARC-UI-148 #2.** Drag the window's edge to make it bigger and smaller while the inventory is open.
  - Expect: The frame redraws at the new size (the previous picture stays until it is ready); the lines of the grid stay sharp and one unit wide at every size.
  - Notes:
- [ ] **ARC-UI-148 #3.** Pick up an item and drop it into a cell and into a wear slot.
  - Expect: Same behaviour as in HD and Legacy.
  - Notes:
- [ ] **ARC-UI-148 #4.** Look at the drop and use boxes.
  - Expect: Still the original's picture (the glow and the green or red corners show the state).
  - Notes:

### ARC-MAGIC-058 — Spell runs at the hero's change of map (2026-11-12)

Setup: The hero's change to another map (a jump point, the Crash Site cave, a map-mode exit) ends every spell run that names an object that does not travel with the hero (the hero, the followers that go along and what they hold); runs of the hero and of the followers that travel go on. Built from the executable (functions 0x45C720, 0x44F7A0, 0x44FA70, 0x44FC30; [rules](../world/spell-run-rules.md#runs-at-the-players-change-of-map-arc-magic-058-mt9b)). Compare in both games.

- [ ] **ARC-MAGIC-058 #1.** Cast a maintained spell on yourself (for example a protective spell with a slot in the top bar), walk through an exit to another map.
  - Expect: The spell and its slot are still there on the new map.
  - Notes:
- [ ] **ARC-MAGIC-058 #2.** With a follower in the party, cast a maintained spell on the follower and change map with the follower coming along.
  - Expect: The spell on the follower goes on; its slot stays.
  - Notes:
- [ ] **ARC-MAGIC-058 #3.** Cast a maintained spell on a creature that is not in the party (or on a follower you ordered to wait), then leave the map.
  - Expect: The spell ends as you leave: the slot is empty on the new map and the creature is without the effect when you come back.
  - Notes:
- [ ] **ARC-MAGIC-058 #4.** Cast Wall of Fire (or another spell that makes scenery) and leave the map.
  - Expect: The run ends as you leave and its fire pieces are gone when you return. (A summoned creature that follows you travels and its run goes on; creature summons are not built yet.)
  - Notes:
- [ ] **ARC-MAGIC-058 #5.** Cast a lock or hold spell on a door or chest of the map and leave.
  - Expect: The effect is over when you return (the run ended at the exit).
  - Notes:

### ARC-MAGIC-059 — Spells end when a world-map journey starts (MT11, 2026-11-12)

Setup: The world-map start check (0x565140) ends the hero's maintained spells ([ledger UE6](../research/ledger/spell-runs.md#spell-run-endings-of-a-critter-arc-dev-301), [cast host](../world/spell-cast-host.md#the-world-map-travel-start-arc-magic-059)). Compare in both games with a hero who can cast a maintained spell (for example a protection or light spell, one that shows on the top strip).

- [ ] **ARC-MAGIC-059 #1.** Cast a maintained spell on the hero, open the world map, plan a route and start the journey.
  - Expect: The spell's slot leaves the top strip as the journey starts and its effect is gone on arrival.
  - Notes:
- [ ] **ARC-MAGIC-059 #2.** Plan a route of two or more waypoints and start it.
  - Expect: The spells end at the start; the journey goes on through every segment.
  - Notes:
- [ ] **ARC-MAGIC-059 #3.** Start a journey while the hero is walking or has just clicked a target.
  - Expect: The walk stops and the journey starts.
  - Notes:
- [ ] **ARC-MAGIC-059 #4.** Start a journey while the hero is in the middle of a cast (its casting animation).
  - Expect: The original refuses the start when the cast cannot be interrupted (a cast attempt of priority 4 or 5); Steamwoven shows "the hero's action cannot be interrupted" and keeps the route.
  - Notes:

### ARC-MAGIC-1161 — Spells end when the hero starts to rest (MT11, 2026-11-12)

Setup: Choosing a length in the sleep or wait window ends the hero's maintained spells (the rest option click 0x57B710 → 0x457450, [ledger UE6](../research/ledger/spell-runs.md#spell-run-endings-of-a-critter-arc-dev-301), [sleep window](../world/sleep-window-runtime.md)). Compare in both games with a hero who can cast a maintained spell (a protection or light spell, one that shows on the top strip).

- [ ] **ARC-MAGIC-1161 #1.** Cast a maintained spell on the hero, open the sleep window (key `S`) and look at the top strip.
  - Expect: The spell is still there: opening the window ends nothing.
  - Notes:
- [ ] **ARC-MAGIC-1161 #2.** Choose any length (for example one hour).
  - Expect: The spell's slot leaves the top strip as the length is chosen (not at the end of the rest).
  - Notes:
- [ ] **ARC-MAGIC-1161 #3.** Open the window with the spell running and close it again with `S` without choosing a length.
  - Expect: The spell goes on.
  - Notes:
- [ ] **ARC-MAGIC-1161 #4.** Cast a maintained spell on a follower (you are the caster), then rest.
  - Expect: The spell ends too: the run's caster is the hero. A spell a follower cast itself goes on.
  - Notes:

### ARC-MAGIC-057 — The hero's spot trap roll (E6-a, spot half, 2026-11-12)

Setup: The rules are in [trap state](../world/trap-state-runtime.md) (the Spot item) and the [ledger SP1–SP2](../research/ledger/technology.md#spot-trap-roll-arc-magic-057). Use a hero with a low and one with a high Spot Trap skill (and Perception), on a map with a hidden floor trap (a trap plate) or a trapped chest or door, in both games.

- [ ] **ARC-MAGIC-057 #1.** Walk along a route that crosses a hidden floor trap with a low-skill hero. Repeat on a reloaded save a few times.
  - Expect: Sometimes the walk stops one tile before the trap with the "trap spotted" line and the trap is shown; other times the hero steps on it and it goes off. The chance rises with the skill and Perception.
  - Notes:
- [ ] **ARC-MAGIC-057 #2.** The same with a high-skill hero (a master of Spot Trap if you have one).
  - Expect: The trap is spotted much more often; a master gets a second chance on every miss.
  - Notes:
- [ ] **ARC-MAGIC-057 #3.** Open a trapped chest or use a trapped door that you have not spotted.
  - Expect: Either the use is stopped with the "trap spotted" line (the trap now shows as known) or the trap goes off as you open it.
  - Notes:
- [ ] **ARC-MAGIC-057 #4.** Find a magical trap (a trap whose script is a spell) with a hero below Expert in Spot Trap.
  - Expect: It is never spotted; it goes off on the step or the use. From Expert on it can be spotted.
  - Notes:
- [ ] **ARC-MAGIC-057 #5.** Cast Sense Hidden (spell 8), then repeat step 1.
  - Expect: The trap is spotted without a roll every time (the spell flag), and the hero's Spot Trap level should also be higher while the spell runs (effect 157): compare the sheet's skill value in both games.
  - Notes:

### ARC-MAGIC-1189 — Spot trap remainders: powered shield and companions (E6-a, 2026-11-12)

Setup: The rules are in [trap state](../world/trap-state-runtime.md) (the Spot item and the NPC and companion spot item) and the [ledger SP2, TT2, TT10](../research/ledger/technology.md#spot-trap-roll-arc-magic-057). Use a hero with a technology build (tech points, so a negative aptitude), a powered shield (the technology product that uses charges) and some charges, on a map with hidden traps, in both games.

- [ ] **ARC-MAGIC-1189 #1.** With the powered shield worn and charges in the pack, walk over hidden traps many times (reload between tries). Repeat with the shield taken off.
  - Expect: With the shield worn and charges in the pack a technologist spots traps more often: its (negative) aptitude is a bonus on the roll. Without charges, or with the shield off, the odds are the ordinary ones.
  - Notes:
- [ ] **ARC-MAGIC-1189 #2.** Do the same with a magickal hero (positive aptitude) wearing the shield.
  - Expect: No bonus: a positive aptitude clears the modifier.
  - Notes:
- [ ] **ARC-MAGIC-1189 #3.** Walk with a companion across a hidden trap, in daylight and then in the dark.
  - Expect: The companion sometimes spots the trap with its own roll and stops before it; the trap then shows as known to you. A companion with a better Spot Trap skill spots more often; darkness makes an untrained companion worse.
  - Notes:
- [ ] **ARC-MAGIC-1189 #4.** Let a companion walk onto a trap it did not spot.
  - Expect: The trap goes off on the companion as the triggerer (its damage or spell reaches the companion, and others in the trap's square). See also the ARC-WORLD-1469 section.
  - Notes:

### ARC-WORLD-1469 — NPCs and companions set off traps under their steps (E6-a, firing half, 2026-11-13)

Setup: The rules are in [trap state](../world/trap-state-runtime.md#npc-and-companion-meeting-arc-world-1469) and the [ledger TT17, TT18](../research/ledger/technology.md#trap-state-arc-world-076). Use a map with a trap plate on a route an NPC walks (a plate you know, and one you do not), a companion in the party, and a trapped door on an NPC's route if you have one, in both games.

- [ ] **ARC-WORLD-1469 #1.** Watch an NPC whose route crosses a known trap plate.
  - Expect: The NPC steps on it, the trap goes off with the NPC as the triggerer (a radius 0 trap hurts only the NPC; a larger one hurts everybody in its square, you included), and the NPC stops on that tile; an auto-removing plate is gone afterwards.
  - Notes:
- [ ] **ARC-WORLD-1469 #2.** Lead a companion over a plate neither of you spotted, many times.
  - Expect: The companion sometimes spots it with its own roll, stops and marks it for you; otherwise the trap goes off on the companion. A trap you placed yourself is known to the companion: it goes off at once.
  - Notes:
- [ ] **ARC-WORLD-1469 #3.** Give a hero the Detect Traps spell and let a free NPC walk toward a plate you placed.
  - Expect: The NPC does not know your plate: it spots it without a roll before the step, stops short of it and marks nothing, so it stays unknown to you.
  - Notes:
- [ ] **ARC-WORLD-1469 #4.** Let an NPC open a trapped door on its route.
  - Expect: The trap meets the NPC (spotted, or it goes off), and the door stays shut for that try.
  - Notes:
- [ ] **ARC-WORLD-1469 #6.** Walk yourself, and let an NPC walk, across a trap object whose script is not a native trap (an ordinary script plate, or the 30002 shot) (ARC-WORLD-1582).
  - Expect: The walk ends on that tile, as the original's (nothing else happens: no meeting is built for these); a mover with the step-over flags walks on.
  - Notes:

### ARC-SKILLS-1156 — Disarming a trap from the hotkey bar (E6-a, disarm half, 2026-11-12)

Setup: The rules are in [trap state](../world/trap-state-runtime.md#disarm-arc-skills-1156) and the [ledger SP3, TT5, TT6](../research/ledger/technology.md#spot-trap-roll-arc-magic-057). Put the Disarm Traps skill button on the hotkey bar (the skill bar's entry), and use heroes with a low, an expert and a master Disarm Traps skill, on a map with a spotted trap (a trap plate you saw, or a trapped chest or door you spotted), in both games. Compare the odds over several reloads and the device.

- [ ] **ARC-SKILLS-1156 #1.** Press the Disarm Traps entry on the hotkey bar, then click a trap you have not spotted.
  - Expect: Nothing is rolled: the trap is not a target (no known trap there) and the cursor is back to normal.
  - Notes:
- [ ] **ARC-SKILLS-1156 #2.** Press the entry, then click a spotted trap (the plate, a chest or a door) with a low-skill hero. Reload and repeat a few times.
  - Expect: Sometimes the trap is gone, sometimes nothing happens, sometimes (a critical failure) the trap goes off on you. The odds rise with the skill.
  - Notes:
- [ ] **ARC-SKILLS-1156 #3.** The same with a trap object that has a high difficulty (a strong trap plate) against one with a low difficulty.
  - Expect: The strong trap is disarmed less often at the same skill. A chest's or a door's trap has no such difficulty term.
  - Notes:
- [ ] **ARC-SKILLS-1156 #4.** As an Expert, disarm a trap and look at the pack after a critical success.
  - Expect: The trap's device item (a trap kit the trap's script names) lands in the pack, or on the ground at your feet when the pack cannot take it. Compare which items you get and how often.
  - Notes:
- [ ] **ARC-SKILLS-1156 #5.** As a Master, repeat step 2 many times and count successes against an Expert of the same stats.
  - Expect: A Master succeeds clearly more often (a failed roll gets a second chance).
  - Notes:
- [ ] **ARC-SKILLS-1156 #6.** In turn-based mode, press the entry and click a spotted trap with fewer than 4 action points left, then with enough.
  - Expect: With too few the subturn ends and nothing is rolled; otherwise 4 action points are spent.
  - Notes:
- [ ] **ARC-SKILLS-1156 #7.** Right-click while the cursor is armed.
  - Expect: The cursor is cancelled.
  - Notes:

### ARC-MAGIC-056 — Tempus Fugit speed (spell 79)

Setup: Rules: [contract](../world/character-stats-runtime.md#tempus-fugit-speed-arc-magic-056), ledger [TF1–TF3](../research/ledger/spell-runs.md#tempus-fugit-speed-arc-magic-056). Compare in both games with the same hero (any Temporal hero with Tempus Fugit) and a hostile or neutral creature near.

- [ ] **ARC-MAGIC-056 #1.** Cast Tempus Fugit (spell 79) on the hero. Open the character sheet (speed line) and the inventory window.
  - Expect: The hero's speed is 10 higher while the spell is maintained, and back to the old value when it ends.
  - Notes:
- [ ] **ARC-MAGIC-056 #2.** Cast it, then fight a creature in turn-based mode, and compare the action points of the hero and of the creature with and without the spell.
  - Expect: The hero has 10 more action points; every other creature has 10 fewer (at least the game's minimum of 5).
  - Notes:
- [ ] **ARC-MAGIC-056 #3.** Cast it and fight in real time; compare the pause between attacks of the hero and of the creature with and without the spell.
  - Expect: The hero attacks faster, the creature slower. Walking speed does not change.
  - Notes:
- [ ] **ARC-MAGIC-056 #4.** Have a companion with the hero when casting.
  - Expect: The companion is in the spell's area and gains the same +10 as the hero; a companion that joins after the cast gains it at once (XR7, ARC-MAGIC-1190); one that leaves the party or is told to wait loses it at once (and gets it back at the unwait while the spell is held).
  - Notes:
- [ ] **ARC-MAGIC-056 #5.** With the spell active, open a companion's character sheet (talk to it, the sheet action).
  - Expect: The speed line shows the shift: 10 more than without the spell for a companion in the spell, 10 less for an NPC outside it (never below 1).
  - Notes:

### ARC-MAGIC-060 — Item spells on the hero's hit, the cell weapon (PR #1184, 2026-11-12)

Setup: The hero's melee swings and shots now fire the combat events of the worn items (before, they did not in the game). The cell weapon of Electric degree 4 (record 147) is the one installed item that shows it; armour 8215 (record 87) answers a hit too. Compare in both games.

- [ ] **ARC-MAGIC-060 #1.** With Electric degree 4 and power cells in the pack, assemble the degree-4 melee weapon, wield it and hit an NPC.
  - Expect: Each hit that lands takes cells from the pack (a technologist pays up to double) and adds an electrical shock to the weapon's own damage; a missed swing takes nothing. In the original the shock's own damage fires the weapon again until the NPC dies or the cells run out (MG46), here once per hit: only the first shock and its cells are compared.
  - Notes:
- [ ] **ARC-MAGIC-060 #2.** Hit with no cells in the pack.
  - Expect: The weapon still hits for its own damage; no shock, no cells.
  - Notes:
- [ ] **ARC-MAGIC-060 #3.** Kill an NPC with the weapon.
  - Expect: The killing hit pays the cells of its firing; nothing is expected to fire after the NPC is dead (the original's repeat per MG46 ends with the kill).
  - Notes:

### ARC-MAGIC-1188 — Item spells of an NPC's attack (2026-11-12)

Setup: Worn items of an NPC that attacks now fire their combat events, as the hero's do (the same rule in the original executable, 0x4B4390 serves any attacker). The one installed case you can see: armour 8215 (record 87) answers a hit when worn by an NPC or by you. Compare in both games.

- [ ] **ARC-MAGIC-1188 #1.** Let an NPC that wears armour 8215 (or other armour with a hit spell) be hit by a hostile NPC (a companion fighting an enemy).
  - Expect: The wearer's armour spell starts on each hit that lands, as for you wearing it; a miss starts nothing.
  - Notes:
- [ ] **ARC-MAGIC-1188 #2.** Let an NPC that wields or wears a combat-event item (a weapon or armour with an "attacks"/"damages" spell) hit you or another NPC.
  - Expect: The item's spell starts on each hit that lands, aimed as the original's executor aims it; a missed swing starts nothing.
  - Notes:
- [ ] **ARC-MAGIC-1188 #3.** Watch a bow or firearms shot of an NPC that wears such an item.
  - Expect: The events come when the arrow strikes, not when it leaves the bow.
  - Notes:

### ARC-MAGIC-045 — Shield hit eye candy and the draw of translucent, shrunk and hidden critters (2026-11-11)

Setup: The rules and what is built are in [the cast host](../world/spell-cast-host.md#hit-eye-candy-and-object-draw-flags-arc-magic-045) and [ledger HE1–HE4, OD1–OD6](../research/ledger/spell-runs.md#object-draw-flags-and-the-hit-eye-candy-arc-magic-045). Steps need a mage hero (or scrolls) and a creature to fight.

- [ ] **ARC-MAGIC-045 #1.** Cast Shield of Protection (30) on yourself, then let a creature hit you with a weapon or a spell that damages you.
  - Expect: Each hit that reaches you plays the shield's hit effect on you (a short flash of the spell's own animation). Misses do not.
  - Notes:
- [ ] **ARC-MAGIC-045 #2.** Cast Invisibility (69) on yourself.
  - Expect: You are drawn half transparent (not gone).
  - Notes:
- [ ] **ARC-MAGIC-045 #3.** Cast Invisibility (69) on a friendly companion or NPC.
  - Expect: Without detection the creature is not drawn at all (a creature of high perception, 20 or more, or a Detect Invisible effect shows it half transparent).
  - Notes:
- [ ] **ARC-MAGIC-045 #4.** Cast the spell that shrinks a creature (the one with the shrunk flag; the player's spell list names it) on a creature.
  - Expect: It is drawn at half size, standing on its tile.
  - Notes:
- [ ] **ARC-MAGIC-045 #5.** Cast Disintegrate (34) on a creature that stands near an eye candy (a burning or magic overlay).
  - Expect: The body is gone at once; any overlay of the spell still plays there.
  - Notes:

### ARC-MAGIC-053 — Summon for creatures: Summon Undead and the other creature summons (E1-e, 2026-11-11)

Setup: The rules are in [spell cast host: summon for creatures](../world/spell-cast-host.md#summon-for-creatures-arc-magic-053); the original's reading is the [ledger SUM5, SUM6 and SUM17–SUM22](../research/ledger/spell-runs.md#summon-component-and-the-objects-it-creates-arc-res-101). Give the hero the spells (a mage with Summoning: Summon Undead 57, Succour Beast 53, Orcish Champion 71, Guardian Ogre 72, Familiar 74) and compare in both games. It closes the owner's report of 2026-11-08 17:19:23 (no summoning animation and no summoned NPC).

- [ ] **ARC-MAGIC-053 #1.** Cast Summon Undead (57) at an empty tile a few cells away.
  - Expect: The summoning animation plays on the tile, and when it reaches its callback frame (about two thirds of a second) the creature appears; before that nothing stands there.
  - Notes:
- [ ] **ARC-MAGIC-053 #2.** Look at who is summoned with a weak magick aptitude and, after raising it, with a strong one (thresholds 20, 60 and 85 of the aptitude).
  - Expect: The creature changes with the aptitude (list 5: four undead kinds, the strongest above 85).
  - Notes:
- [ ] **ARC-MAGIC-053 #3.** Watch the new creature and open the party portraits.
  - Expect: It is the hero's follower at once (mind control), fights the hero's enemies and follows the hero.
  - Notes:
- [ ] **ARC-MAGIC-053 #4.** Let the creature be killed.
  - Expect: The spell ends with it: its upkeep stops, and the body is removed.
  - Notes:
- [ ] **ARC-MAGIC-053 #5.** Stop maintaining the spell (or wait for the fatigue to run out).
  - Expect: The animation plays backwards and the creature goes; it leaves the party.
  - Notes:
- [ ] **ARC-MAGIC-053 #6.** Cast it on a tile with a tile script (a trap or a notice tile).
  - Expect: The script runs with the summoned creature as the one who stepped on it.
  - Notes:
- [ ] **ARC-MAGIC-053 #7.** Cast Succour Beast (53) and Orcish Champion (71) as well.
  - Expect: Beasts by aptitude (list 4) and a fixed orc (prototype 28443): the same animation, reveal and follower.
  - Notes:
- [ ] **ARC-MAGIC-053 #8.** Save while the creature stands, load, and wait.
  - Expect: The creature is still there and still the hero's follower (its flags, party place and run are saved).
  - Notes:

### ARC-WORLD-1158 — Body decay of dead creatures (E2-b, 2026-11-12)

Setup: The rules are in the [ledger BD1–BD7](../research/ledger/npc-ai.md#body-decay-built-arc-world-1158) (the executable's PO10). Compare in both games with a creature you kill and then leave lying: wait or sleep one game day (the game bar's wait, key S) near the body.

- [ ] **ARC-WORLD-1158 #1.** Kill a creature that carries an item or two (a map NPC, then a creature a generator made) and stay on the map. Pass 23 hours, look at the body; then pass another 2 hours.
  - Expect: The body lies there the first 23 hours and is gone a game day after the death (the Decay Dead Bodies event).
  - Notes:
- [ ] **ARC-WORLD-1158 #2.** Look at the spot after the body is gone.
  - Expect: The creature's items lie on its tile (in a junk pile when two or more) and disappear two game days later as any dropped item; an item the data marks "no display" or that a spell summoned is gone with the body, any other item (a "not droppable" one too) lies on the ground.
  - Notes:
- [ ] **ARC-WORLD-1158 #3.** Kill a creature, leave the map for more than a day, come back.
  - Expect: The body is gone on return (the event runs late once its map is the shown one).
  - Notes:
- [ ] **ARC-WORLD-1158 #4.** Kill a creature of a generator and wait for its body to go.
  - Expect: The generator does not make a replacement because of the decay: only a generator whose live count the death released ticks again (PO7, PO11); the made-so-far count is never given back.
  - Notes:

### ARC-INPUT-1123 — Lockpicks need 4 action points to start (2026-11-12)

Setup: The rule is in [interface modes](../architecture/interface-modes.md#action-points-at-the-start-of-a-target-mode-arc-input-1123). Compare it in both games in a turn-based fight (turn-based on, combat mode, a fight with a creature near) with a hero that carries lockpicks.

- [ ] **ARC-INPUT-1123 #1.** On your own turn spend all action points (walk until they are gone, while it is still your turn), then pick the lockpicks from the inventory. With 1 to 3 points left the original lets a hero with normal fatigue through (only fatigue of 1 or less refuses).
  - Expect: The inventory closes and no lock cursor opens; nothing is said. (Steamwoven shows "pick a lock: not enough action points" on its status line, a known difference.)
  - Notes:
- [ ] **ARC-INPUT-1123 #2.** On your own turn with 4 or more points left, pick the lockpicks.
  - Expect: The inventory closes and the lock cursor opens.
  - Notes:
- [ ] **ARC-INPUT-1123 #3.** Outside a turn-based fight, pick the lockpicks.
  - Expect: The lock cursor opens whatever the points.
  - Notes:

### ARC-WORLD-1132 — First gold pile (2026-11-12)

Setup: The rule is in [First gold pile](../world/inventory-runtime.md). Compare in both games with a hero whose inventory grid is full and who holds no coin.

- [ ] **ARC-WORLD-1132 #1.** Fill the whole inventory grid, hold no gold, then receive coins (a dialog reward, selling an item, a successful steal).
  - Expect: The coins do not reach the hero: a pile of coins lies on the hero's tile and the call reports success.
  - Notes:
- [ ] **ARC-WORLD-1132 #2.** Pick the pile up with the grid still full.
  - Expect: The pickup is refused and the pile stays on the ground; with a free place the coins join the hero's gold.
  - Notes:

### ARC-SCRIPT-1163 — NPC state script actions (E5-a, 2026-11-12)

Setup: Script actions that change an NPC's own state now run from use, tile, sector, heartbeat and dying scripts ([ledger NS1–NS8](../research/ledger/scripts.md#npc-state-commands-arc-script-1163-e5-a), [dispatcher](../scripting/scr-dispatcher-runtime.md#npc-state-commands-arc-script-1163-set-s36)). No installed map is named below because the check that lists the admitted scripts is by script number, not by map; use a companion, a shopkeeper and a guard in any town where a quest script touches them, and compare the same quest step in both games.

- [ ] **ARC-SCRIPT-1163 #1.** Make a script release a companion (quest steps that end a companion's service: critter disband).
  - Expect: The companion stops following at once, leaves the party list and keeps standing where it is; it does not say anything.
  - Notes:
- [ ] **ARC-SCRIPT-1163 #2.** Reach a quest step that sets or adjusts an NPC's reaction toward the hero, then talk to it.
  - Expect: Its reaction category in the conversation (and the barter prices) match the original after the step; an NPC the hero mind-controls does not change.
  - Notes:
- [ ] **ARC-SCRIPT-1163 #3.** Reach a quest step that moves an NPC's day or night standpoint, then wait until the matching time of day (and, for the two extended standpoints, change map).
  - Expect: After the step the NPC goes to the new standpoint at the next day or night change, not the old one.
  - Notes:
- [ ] **ARC-SCRIPT-1163 #4.** Reach a quest step that changes the script of an NPC or scenery (change script), then use or talk to it again.
  - Expect: The object runs the new script from then on, with its local flags and counters kept.
  - Notes:

### ARC-SCRIPT-1164 — world-object script actions (E5-b, 2026-11-12)

Setup: Script commands that act on a door, chest, sector or monster generator now run from use, tile, sector, heartbeat and dying scripts ([ledger WO1–WO13](../research/ledger/scripts.md#world-object-commands-arc-script-1164-e5-b), [dispatcher](../scripting/scr-dispatcher-runtime.md#world-object-commands-arc-script-1164-set-s37)). No installed map is named below because the check that lists the admitted scripts is by script number, not by map; compare the same quest step in both games.

- [ ] **ARC-SCRIPT-1164 #1.** Reach a quest step that locks or unlocks a door or chest from a script (set lock state), then try to open it.
  - Expect: The object is locked or unlocked as the step says; the lock picks and keys behave as for any locked object.
  - Notes:
- [ ] **ARC-SCRIPT-1164 #2.** Reach a quest step that opens or closes a door or chest from a script (toggle open or closed).
  - Expect: A door swings open or shut at once; a chest changes to its next or previous picture frame; a script that tests the open state then branches on it.
  - Notes:
- [ ] **ARC-SCRIPT-1164 #3.** Reach a quest step that gives the hero or an NPC an item (create item).
  - Expect: The item appears in the holder's inventory (or, with no room, is not taken).
  - Notes:
- [ ] **ARC-SCRIPT-1164 #4.** Reach a quest step that blocks a sector, then open the world map and route into it.
  - Expect: The route does not enter the blocked sector, and a blocked destination is refused.
  - Notes:

### ARC-WORLD-1162 — beds: sleeper flags, occupied bed frame and nudge (slice 3, 2026-11-12)

Setup: The rules are in [Sleep window runtime](../world/sleep-window-runtime.md#npc-beds-arc-world-1162-slice-2-ledger-swb16swb21). The occupied frame of a bed is the next frame of its art (ledger SWB15, 0x43F7F0); the build checks it by number only (the plan holds the frames, the draw command carries the next one), so what the frame looks like has not been seen. Maps with NPC beds: the installed data has 51 NPC rows whose standpoint is a bed tile (Roseborough inn2, Wheel clan, Bates mansion and others); a night hour (21:00 to 05:59) is needed.

- [ ] **ARC-WORLD-1162 #1.** In a house with beds, wait in the original until about 22:00 and look at a bed an NPC went to.
  - Expect: The NPC is not drawn standing; the bed is drawn in its next frame (a sleeper in the bed). Empty beds keep the frame they have in the map data.
  - Notes:
- [ ] **ARC-WORLD-1162 #2.** Compare the bed's picture occupied in both games (position, size, mirror).
  - Expect: The occupied frame sits where the empty one did (the art's own offsets move it). A bed that looks shifted or cut means the frame offset handling (0x43F7F0) differs.
  - Notes:
- [ ] **ARC-WORLD-1162 #3.** Look at where the NPC lay down: it stood on the bed's own tile, or next to it.
  - Expect: An NPC on the bed's tile is moved one tile in direction 4 (x + 1, y + 1) when it lies down; it reappears there when it wakes. An NPC beside the bed is not moved.
  - Notes:
- [ ] **ARC-WORLD-1162 #4.** Wake it (attack it, or wait until about 06:00 and watch; the original has no other waker of a sleeping NPC in the executable, talking to it is not one).
  - Expect: It is drawn standing again on the tile it was seated on; the bed goes back to its first frame. The NPC walks off on its own later.
  - Notes:
- [ ] **ARC-WORLD-1162 #5.** Lie down yourself in a bed whose script lets you (Crash Site cave) and look at the bed while the window is open.
  - Expect: The bed shows its next frame; the screen fades. After the rest the bed is empty again.
  - Notes:

### ARC-UI-1127 — Identify sheet, heal another critter and lockpicks on containers (slice E8-g, 2026-11-12)

Setup: The rules are in [Identify](../world/spell-cast-host.md#identify-arc-magic-032), [Heal](../world/heal-runtime.md#viewer) and [Pick locks](../world/pick-locks-runtime.md#containers-in-the-viewer-arc-ui-1127). Open question the checker may settle: the original starts the heal skill goal 33 for a healing kit used on another critter (the hero walks within range 1, then heals); Steamwoven starts the item use goal 39 (the same walk, range 1), so the picture of the walk and the 4 action points should match, only the goal's number differs.

- [ ] **ARC-UI-1127 #1.** Hurt a creature (an NPC or a follower), hold the heal kit (prototype 15199) and release it on the Use icon, then press the creature.
  - Expect: The cursor opens; the hero walks next to the creature and the heal runs (the check of the heal skill: an amount, or a failure that may cost five uses). Steamwoven: the same, the HUD names the amount and the uses left.
  - Notes:
- [ ] **ARC-UI-1127 #2.** Arm lockpicks (use icon, no Shift) and press a locked chest.
  - Expect: The hero walks next to the chest and picks its lock (4 action points in a turn-based round); a success unlocks it, a critical failure jams it. Steamwoven: the same; the picked chest opens by the next press.
  - Notes:
- [ ] **ARC-UI-1127 #3.** Cast Read Aura on a creature (see ARC-MAGIC-032, step 1).
  - Expect: The read-only sheet of the creature opens.
  - Notes:

### ARC-MAGIC-1361 — Harm damage and the difficulty share (2026-11-12)

Setup: The numbers are in [DH11](../research/ledger/spell-runs.md). The owner's report (Harm "twice as strong") is answered by the data: no formula differs. Compare in both games with the same hero and the same difficulty setting.

- [ ] **ARC-MAGIC-1361 #1.** In the options set the difficulty to Normal (not Easy). Cast Harm (spell 55) once at a wolf, with a hero of 8 magick points (a night mage at night, or 8 points of magick).
  - Expect: 17 hit points per cast (3 + 37 × 40 / 100). On Easy 25, on Hard 13: the original adds half of a PC attacker's damage on Easy and takes a quarter on Hard, for spells and weapons alike.
  - Notes:
- [ ] **ARC-MAGIC-1361 #2.** Press once on the wolf with the Harm cursor, with your own mouse, and watch the casts in the log (Ctrl+B).
  - Expect: One cast per press, as in the original (the cursor stays open for the next press). Two or three casts within a few milliseconds from one press mean the mouse sends duplicate presses.
  - Notes:

### ARC-SCRIPT-1359 — the Crash Site Molochean agent after the altar talk (2026-11-12)

Setup: The agent's heartbeat (script 1717) is admitted whole since this task ([GL1–GL2](../research/ledger/scripts.md)). Start a new game at the Crash Site; the checks need the story flag set by the altar talk.

- [ ] **ARC-SCRIPT-1359 #1.** Talk to Virgil at the altar (the tile script runs his dialog), then watch the Molochean agent near the altar and read the log (Ctrl+B).
  - Expect: No `heartbeat-refused ... script 1717` line any more. The agent sets its day and night standpoints to its tile and runs to it (goal 74), as the original's heartbeat does.
  - Notes:
- [ ] **ARC-SCRIPT-1359 #2.** Let the agent reach its tile.
  - Expect: The follow-up of the scene (talk or attack) starts as in the original. If it does not, report what the agent does (the scene may belong to another script; a new issue is then filed).
  - Notes:

### ARC-MAGIC-1399 — queued casts when the target dies (2026-11-12)

Setup: Answered by the executable ([QC7](../research/ledger/spell-runs.md)): no deviation, nothing changed in the rules. The original charges each queued cast's cost when the target dies; the report's four payments are consistent with a killing cast plus three queued casts, not verified against the report itself (see also the duplicate presses of [ARC-MAGIC-1361](#arc-magic-1361--harm-damage-and-the-difficulty-share-2026-11-12)).

- [ ] **ARC-MAGIC-1399 #1.** Cast Harm at a wolf several times in a row quickly so that casts queue, and let the first kill it. Read the fatigue bar and the log (Ctrl+B).
  - Expect: The hero (without the mastered college of Harm and without charged worn items) pays 5 fatigue for the killing cast and 5 for each cast still queued on the wolf; the queued casts are then gone. Steamwoven: the same.
  - Notes:

### ARC-RENDER-1355 — window move with the pointer on an NPC (PR #1446, 2026-11-12)

Setup: A repair of the game's own view, not original behaviour. Compare in the current build from `main`, new game on the main map.

- [ ] **ARC-RENDER-1355 #1.** Land at Misty Hills by the world map, rest the mouse pointer on the gypsy trader and walk west along the road (cells about 90172 to 90158 on y = 84179) across the window boundary.
  - Expect: The view goes on with the interface; no bare terrain-only screen, no `stream publication failed` line in `godot.log`.
  - Notes:

### ARC-SCRIPT-1360 — The NPC of the first building (PR #1452, 2026-11-12)

Setup: The quest NPC of the house at the first building (prototype 17156, dialog 2786, heartbeat 2787) is stored off in the map data; its heartbeat switches him on once the PC's quest 1158 is no longer unknown ([toggle state](../world/ai-host.md), [ledger SU6](../research/ledger/npc-ai.md)). Compare in both games with a hero whose quest 1158 (from the spirit's task) has been accepted.

- [ ] **ARC-SCRIPT-1360 #1.** With quest 1158 not started, stand inside the house.
  - Expect: The room is empty.
  - Notes:
- [ ] **ARC-SCRIPT-1360 #2.** Accept quest 1158 and stand inside the house (the heartbeat runs every few seconds while the hero is near).
  - Expect: The NPC appears at his cell within a few seconds and his talk (dialog 2786) opens.
  - Notes:

### ARC-WORLD-1159 — Random encounters on the world journey (E2-c, 2026-11-12)

Setup: The rules are in [random encounters](../world/random-encounters-runtime.md) (ledger PO13–PO19). Compare them in both games on the outdoor map, with a hero of any level, away from towns. Encounters are rare by design (the chance is 5 per cent of each firing, one firing per 300 to 700 game minutes, five times that while resting), so a long journey or a long rest is needed.

- [ ] **ARC-WORLD-1159 #1.** Open the world map in wilderness and travel far (many hours of game time).
  - Expect: Now and then the journey stops: the map closes and the party is back on the map where the marker stood, with a group of creatures standing close by.
  - Notes:
- [ ] **ARC-WORLD-1159 #2.** Press the world-map key right after such a stop.
  - Expect: The map does not open for about four seconds.
  - Notes:
- [ ] **ARC-WORLD-1159 #3.** Rest in the wilderness with the window's long options.
  - Expect: Sometimes the window closes by itself and creatures stand next to the party; the chance is five times higher than while walking.
  - Notes:
- [ ] **ARC-WORLD-1159 #4.** Look at where the creatures stand against the party.
  - Expect: Two rings: one side west of the party's tile or north of it, about six tiles away, in small columns of three. Ours do not turn toward the party (a known gap).
  - Notes:
- [ ] **ARC-WORLD-1159 #5.** Meet a second group in the same encounter (a table row with two creature kinds).
  - Expect: The first creature of the second kind is missing (the original destroys it, a quirk kept).
  - Notes:
- [ ] **ARC-WORLD-1159 #6.** Try the same in a town sector and while standing in a named area.
  - Expect: No encounter happens there.
  - Notes:

### ARC-SCRIPT-1481 — food that heals and cures poison (2026-11-13)

Setup: The food use script 30282 (the generic magic potion on 729 food items, most of them in the main map's chests and on its NPCs) runs when the hero eats the item ([VT1–VT8](../research/ledger/scripts.md#vitals-commands-and-the-food-use-script-arc-script-1481)). Use a food item that holds a potion in the hero's own inventory window (right click).

- [ ] **ARC-SCRIPT-1481 #1.** Hurt the hero (a fight or a trap), then take a food item that the original calls a healing potion from a chest or an NPC and right-click it in the inventory.
  - Expect: The item is eaten (it leaves the list), hit points (or fatigue, or poison, by the potion) rise by the potion's amount scaled by the hero's magick or tech aptitude, and the use costs 4 action points in a round.
  - Notes:
- [ ] **ARC-SCRIPT-1481 #2.** Do the same with a hero at full health (and no poison).
  - Expect: Nothing happens and the potion stays: the script skips the default before it destroys the item.
  - Notes:
- [ ] **ARC-SCRIPT-1481 #3.** Poison the hero (a poison trap or a spider bite) and eat a cure-poison potion.
  - Expect: The poison level falls by the potion's amount (to 0 at least) and the poison damage ticks stop when it reaches 0.
  - Notes:
- [ ] **ARC-SCRIPT-1481 #4.** Eat a magick potion with a technological hero (a dwarf with a high tech aptitude).
  - Expect: The item is gone and nothing heals (the potion fizzles with its own effect), as the original's script does.
  - Notes:

### ARC-UI-1363 — The sleep and wait window in the original's look (2026-11-13)

Setup: The window is drawn from the build 0x57B4F0 ([sleep window](../world/sleep-window-runtime.md#the-windows-look-arc-ui-1363-ledger-sz13-swb6-swb7), ledger SZ13, SWB6, SWB7): art 565 at (573, 42), a button of art 293 and a line of `sleepUI.mes` for each length. Compare it in both games, from the key `S` and from the bar's Sleep button (in the wilderness and in a town-map sector with the wait marker).

- [ ] **ARC-UI-1363 #1.** Press `S` in the wilderness.
  - Expect: The window stands at the right edge of the play view (573, 42) with eight rows: a round button and a line each (one hour ... until healed).
  - Notes:
- [ ] **ARC-UI-1363 #2.** Look at the button frames and the line colour.
  - Expect: Idle, lit under the pointer, down while pressed; the lines are white in the small font.
  - Notes:
- [ ] **ARC-UI-1363 #3.** Do the same in a town-map sector with the wait marker.
  - Expect: Seven rows (the wait lengths, no until healed).
  - Notes:
- [ ] **ARC-UI-1363 #4.** Press a button and watch the window.
  - Expect: The rest runs; the buttons stay visible, and a second press on another length restarts the rest (ARC-UI-1511).
  - Notes:

### ARC-UI-1362 — Load screen for a save load and a quick load (2026-11-13)

- [ ] **ARC-UI-1362 #1.** From the main menu press Load Game, choose a save made on another map than the start map.
  - Expect: The screen goes black at once with the clock animation in the top-right corner (two pixels in) and stays so until the game appears on the save's map; the menu shows no wait note.
  - Notes:
- [ ] **ARC-UI-1362 #2.** Press F8 with the newest save on another map than the one shown.
  - Expect: The same black screen with the clock until the saved map is there.
  - Notes:
- [ ] **ARC-UI-1362 #3.** Load a save whose map is missing or broken (rename its map folder in a copy).
  - Expect: The black screen goes and the menu (or the running game) comes back with the reason.
  - Notes:
- [ ] **ARC-UI-1362 #4.** Press F8 with a save on the map already shown.
  - Expect: No load screen is seen: the load of the shown map is one step in this build (issue #1504).
  - Notes:

### ARC-UI-1365 — The experience progress strip under the hot-key bar (PR #1555, 2026-11-13)

Setup: The strip is drawn as the draw 0x556C20 does ([experience strip](../rendering/terrain-viewer.md#experience-strip-arc-ui-1365), ledger GB13, GB14). Known and already documented: the viewer redraws the strip at once when experience changes; the original repaints it at its bar refresh events (and at once only while the character sheet is open).

- [ ] **ARC-UI-1365 #1.** Start a new hero, look at the bottom bar under the ten hot-key slots.
  - Expect: No red segments at the start of a level.
  - Notes:
- [ ] **ARC-UI-1365 #2.** Gain about half of the experience a level needs (kill or quest), open and close the character sheet.
  - Expect: Red segments fill from the left, about five of ten at half; a thin bar under them grows by the rest.
  - Notes:
- [ ] **ARC-UI-1365 #3.** Gain experience until just below the next level, then reach it.
  - Expect: The strip is nearly full, then shows the new level's progress (almost empty) after the level-up message.
  - Notes:

### ARC-UI-1364 — top strip buttons lit by game events and the Map button's face (2026-11-13)

Setup: The rules are in [bar buttons lit by game events](../rendering/terrain-viewer.md#bar-buttons-lit-by-game-events-arc-ui-1364) (ledger GB15–GB17). Compare them in both games with the interface bars on.

- [ ] **ARC-UI-1364 #1.** Gain a level (kill something, or finish a quest) with the character sheet closed.
  - Expect: The level-up sound and message appear and the Character button turns red (the lit art).
  - Notes:
- [ ] **ARC-UI-1364 #2.** Open the character sheet (the button or `C`).
  - Expect: The Character button goes back to its normal look.
  - Notes:
- [ ] **ARC-UI-1364 #3.** Gain a level while the sheet is open, then close it.
  - Expect: The button stays normal.
  - Notes:
- [ ] **ARC-UI-1364 #4.** Take or drop an object on the ground, or have one given to you, with the inventory closed.
  - Expect: The Inventory button lights; opening the inventory puts it out.
  - Notes:
- [ ] **ARC-UI-1364 #5.** Accept or complete a quest, add or lose a blessing or a curse, or gain a reputation, with the logbook closed.
  - Expect: The Logbook button lights; opening the logbook puts it out.
  - Notes:
- [ ] **ARC-UI-1364 #6.** Learn a new world-map area (a dialog or script that reveals one) while standing in the wilderness.
  - Expect: The Map button (the blue globe in the wilderness) lights until the world map is opened.
  - Notes:
- [ ] **ARC-UI-1364 #7.** Walk from the wilderness into a town with a town map, and out again.
  - Expect: In the town the Map button shows its town art; in the wilderness the blue globe. The map opens on the town map in the town.
  - Notes:
- [ ] **ARC-UI-1364 #8.** Save with a button lit and load the save.
  - Expect: The lit button is lit again after the load, as in the original (ARC-UI-1542, schema 98); a save from an older build loads with all buttons normal.
  - Notes:

### ARC-UI-1504 — Load screen for a load of the map already shown (2026-11-13)

Setup: The original shows the load screen for every successful load of a save, the saved map being the one already open too; the viewer's load of the shown map is one step, so it now raises the screen first and runs the load a frame or two later (F8, the load window of the in-play menu, Last Save).

- [ ] **ARC-UI-1504 #1.** In play press F8 with the newest save on the map already shown.
  - Expect: The screen goes black with the clock in the top-right corner for a moment (a frame or two, longer on a big map), then the game shows the saved state. The keys do nothing meanwhile.
  - Notes:
- [ ] **ARC-UI-1504 #2.** Open the in-play menu, Load Game and choose a save on the shown map.
  - Expect: The same black screen with the clock, then the window is closed and the game is the saved one.
  - Notes:
- [ ] **ARC-UI-1504 #3.** The same with a save whose file is damaged (copy one and break it).
  - Expect: The screen goes at once, the load window stays with the reason (F8: the running game stays, the message window says the load failed).
  - Notes:
- [ ] **ARC-UI-1504 #4.** Press F8 with no save at all.
  - Expect: No black screen; only the two notes, as before.
  - Notes:

### ARC-UI-1194 — Generated interface: the loot, barter and steal windows (S4, 2026-11-13)

Setup: The rules are in [viewer slice S4](../rendering/hd-interface-density.md#viewer-slice-s4-arc-ui-1194). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game and open a container, a body (the toggles under the picture switch to the doll), pick a pocket and talk to a merchant for the barter window. Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1194 #1.** Open a chest in a 1280 × 800 window.
  - Expect: The ring of the round view, the box of the image with its six rivets and the grid with its chain are drawn by code in the same places as in HD; the grid's cells, Take all and every click land where they did.
  - Notes:
- [ ] **ARC-UI-1194 #2.** Open a body and press the doll toggle.
  - Expect: The doll view (nine slots, two curved pits) is drawn by code; worn items sit on their slots as before.
  - Notes:
- [ ] **ARC-UI-1194 #3.** Talk to a merchant and open barter; with a follower in the party open it again.
  - Expect: The medallion stays a picture, the merchant's image, name and quote sit in their boxes; a follower's window keeps its two cycle buttons on their plates.
  - Notes:
- [ ] **ARC-UI-1194 #4.** Make the window bigger and smaller while one of these windows is open.
  - Expect: The frames redraw at the new size (the previous picture stays until it is ready); lines stay one unit wide.
  - Notes:
- [ ] **ARC-UI-1194 #5.** Open the inventory and one of these windows in turn.
  - Expect: The ring of the round view looks the same in both (the paper doll's ring is generated too now).
  - Notes:

### ARC-UI-1195 — Generated interface: the character sheet (S5, 2026-11-13)

Setup: The rules are in [viewer slice S5](../rendering/hd-interface-density.md#viewer-slice-s5-arc-ui-1195). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game and press `C`. Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1195 #1.** Press `C` in a 1280 × 800 window.
  - Expect: The window's boxes (name, info, stats with their rivets, the page area) are drawn by code in the same places as in HD; the stat numbers, the portrait, the gauge and the experience ruler sit where they did.
  - Notes:
- [ ] **ARC-UI-1195 #2.** Click the three tabs (skills, technology, spells).
  - Expect: Each page is dark stone drawn by code; the group buttons, the discipline buttons, the college sockets and the + and − buttons are in place and answer to hover and press.
  - Notes:
- [ ] **ARC-UI-1195 #3.** Press + and − on a stat and on a skill with points to spend.
  - Expect: The buttons show their hover and down looks; the numbers change as before.
  - Notes:
- [ ] **ARC-UI-1195 #4.** Resize the window and open the sheet again.
  - Expect: The frames are sharp at the new size, no seam between the window and the pages.
  - Notes:
- [ ] **ARC-UI-1195 #5.** Open an NPC's sheet in a dialog (reply `c-e`).
  - Expect: The same look, with no + or − buttons.
  - Notes:

### ARC-UI-1784 — Generated interface: the schemes page and the college tabs (S5b, 2026-11-13)

Setup: The rules are in [viewer slice S5b](../rendering/hd-interface-density.md#viewer-slice-s5b-arc-ui-1784). Set the HD interface line to **Generated (experimental)**, start a game and press `C`. Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1784 #1.** Open the spells tab.
  - Expect: The sixteen college balls keep their colours and glyphs inside a brass ring drawn by code; hover and the pressed look change the ring and the ball as before.
  - Notes:
- [ ] **ARC-UI-1784 #2.** Open the schemes tab (the fourth, the one with the scroll icon).
  - Expect: The page is dark stone like the other pages: the scheme's name in a sunk plaque with a rivet at each end, the list's pit and the scroll bar's track; the names, the bar's arrows and thumb are where they were.
  - Notes:
- [ ] **ARC-UI-1784 #3.** Resize the window and look at both pages again.
  - Expect: The frames are sharp at the new size.
  - Notes:
- [ ] **ARC-UI-1784 #4.** Look at the spell icons next to the generated pits.
  - Expect: Judge whether they need a generated frame (they are pictures in S5b).
  - Notes:

### ARC-UI-1197 — Generated interface: the world map and the town map (S6, 2026-11-13)

Setup: The rules are in [viewer slice S6](../rendering/hd-interface-density.md#viewer-slice-s6-arc-ui-1197). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game and press `W` (the town map opens in the Crash Site; on the open ground the world map). Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1197 #1.** Press `W` in a 1280 × 800 window.
  - Expect: The window's wood, the brass rim around the map view, the readout boxes, the compass rose, the route sketch and the Travel button's ring sit where they did in HD; the map picture is as before.
  - Notes:
- [ ] **ARC-UI-1197 #2.** Press `Z` or the Zoom and Whole-map buttons to change the view.
  - Expect: The frame does not change; the buttons (still art) answer to hover and press.
  - Notes:
- [ ] **ARC-UI-1197 #3.** Move the pointer over the map.
  - Expect: The two readout boxes show their numbers in the same places.
  - Notes:
- [ ] **ARC-UI-1197 #4.** Resize the window (or go full screen) and open the map again.
  - Expect: The frame is sharp at the new size, no seam between the wood and the kept pictures.
  - Notes:

### ARC-COMBAT-1738 — Auto Switch Weapons option and its re-wield cases (2026-11-13)

Setup: The rules are in the [ledger OH3](../research/ledger/ui-audio.md#hidden-options-arc-res-1107) and [equipment runtime](../world/equipment-runtime.md). Start a game with a hero who carries two weapons.

- [ ] **ARC-COMBAT-1738 #1.** Open Options (main menu and in play), Game page.
  - Expect: Auto Switch Weapons is the fifth line, after Auto Attack, Off at the first start.
  - Notes:
- [ ] **ARC-COMBAT-1738 #2.** Turn Auto Switch Weapons On, wield a bow with no arrows (or a gun with no bullets) and attack.
  - Expect: The attack does not shoot, and the hero wields the best other weapon at once; with the option Off nothing changes.
  - Notes:
- [ ] **ARC-COMBAT-1738 #3.** With it On, let an enemy with a critical hit (drop weapon or destroy weapon) strike the hero.
  - Expect: The hero's lost weapon is replaced by the best weapon left; with the option Off the hand stays empty.
  - Notes:

### ARC-COMBAT-1929 — Backstab from behind (2026-11-14)

Setup: Rules in [ledger VC27](../research/ledger/combat.md#partial-rows-by-risk-combat-slice-2-arc-res-1104) and [hero attack](../world/hero-attack-runtime.md). Use a hero with points in the backstab skill and a dagger wielded; a sword, axe or two-handed sword needs the skill above apprentice.

- [ ] **ARC-COMBAT-1929 #1.** In combat mode, attack a creature that stands with its back to you (it faces away, not yet turned to you) with the dagger.
  - Expect: The hit adds your backstab level to the normal damage; a hit on a creature that faces you does not.
  - Notes:
- [ ] **ARC-COMBAT-1929 #2.** Do it again from behind a creature that has not noticed you (sneak up quietly; it neither sees nor hears you).
  - Expect: A true backstab: five times the backstab level is added to the damage and the creature's armour does not count for a trained hero.
  - Notes:
- [ ] **ARC-COMBAT-1929 #3.** Try the same with bare hands, a bow, a club or a mace, and with a sword while the skill is at apprentice.
  - Expect: No backstab bonus (only the dagger at any training, and the sword, axe and two-handed sword above apprentice).
  - Notes:
- [ ] **ARC-COMBAT-1929 #4.** Let a creature with the backstab skill and a dagger (a thief or assassin) hit you or a companion from behind.
  - Expect: The same bonus applies to its hit.
  - Notes:
- [ ] **ARC-COMBAT-1929 #5.** ARC-COMBAT-1968: from behind a creature that has not noticed you, throw the dagger (a throwing dagger, default throws) and shoot a bow.
  - Expect: No backstab bonus: the original clears the backstab flags for a throw and a shot, so the damage is the plain damage.
  - Notes:

### ARC-SCRIPT-1732 — The healer menu behind `h:` (2026-11-13)

Setup: Built from the executable's state machine ([ledger HLR1–HLR12](../research/ledger/social.md#healer-reply-arc-script-1732)); the effects start through the AI host, so the look and the timing of the heal and the spells are for you to judge.

- [ ] **ARC-SCRIPT-1732 #1.** Talk to a town healer (a priest or physician whose talk has the healing reply) and pick it.
  - Expect: The NPC says its menu line; the options are by the healer's rank: its own treatment (when it carries a healing item), up to two lists of spells and the best spell last, and a way out. Each spell is named with its `spell.mes` name.
  - Notes:
- [ ] **ARC-SCRIPT-1732 #2.** Choose a spell, then yes, with enough gold; then again with too little.
  - Expect: The price is the reaction-scaled 100 (about 100 at a neutral reaction); the gold goes to the healer, the spell is cast at you (a heal, a poison cure, and at rank 5 a resurrection); with too little gold the healer says its no-money line and nothing is paid.
  - Notes:
- [ ] **ARC-SCRIPT-1732 #3.** Choose the healer's own treatment when your hit points are low.
  - Expect: The price follows its heal skill ((level + 2) × (tier + 1), reaction scaled); the healer walks the skill goal and heals through the usual heal check.
  - Notes:
- [ ] **ARC-SCRIPT-1732 #4.** Ask a healer who follows you the same.
  - Expect: No price screen and no payment: the service runs at once.
  - Notes:

### ARC-UI-1196 — Generated interface: the logbook (S7, 2026-11-13)

Setup: The rules are in [viewer slice S7](../rendering/hd-interface-density.md#viewer-slice-s7-arc-ui-1196). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game and open the logbook (L). Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1196 #1.** Open the logbook in a 1280 × 800 window.
  - Expect: The side panel is wood with the round view of the hero, the book is a maroon cover with two pages sinking toward the fold and stacks of page edges at both sides; the titles and page numbers sit where they did.
  - Notes:
- [ ] **ARC-UI-1196 #2.** Click each of the seven tabs, move over them.
  - Expect: The paper bookmarks (their own outlines and glyphs) follow the Legacy ones; the shown tab is the pressed one; hints and the page sound are as before.
  - Notes:
- [ ] **ARC-UI-1196 #3.** Fill a tab so it has several spreads (the rumors with many entries) and turn the pages with the two arrows.
  - Expect: The arrows are drawn on the page under them (no box of another colour); a turn plays the page sound; the arrow hides on the first and last spread.
  - Notes:
- [ ] **ARC-UI-1196 #4.** Make the window bigger and smaller while the logbook is open.
  - Expect: The book redraws at the new size (the previous picture stays until it is ready); the lines of the page edges stay one unit wide.
  - Notes:

### ARC-UI-1740 — Gambling: bets in the barter window (2026-11-13)

Setup: The rules are in [barter](../world/barter-runtime.md#gambling-arc-ui-1740). Start a game, spend skill points on Gambling for the hero and talk to a merchant who has Gambling points (most shops do).

- [ ] **ARC-UI-1740 #1.** Open barter and drag one of your own items onto the bet box on the right of the window.
  - Expect: The box stays in its rest art, the item goes back to your grid, nothing is printed.
  - Notes:
- [ ] **ARC-UI-1740 #2.** Drag one of the merchant's wares over the box without releasing.
  - Expect: The box changes to the lit art and the merchant's confirm line with the price shows in the quote line; a very expensive ware (above your limit) or a hero with too little money shows the dark art and the merchant's refusal line instead.
  - Notes:
- [ ] **ARC-UI-1740 #3.** Release the ware on the box.
  - Expect: The merchant says a won or lost line. A win gives the ware for nothing, a loss takes its price in coins and gives the ware; the trade sound plays once; the box returns to rest.
  - Notes:
- [ ] **ARC-UI-1740 #4.** Open barter once, close it and save; bet and note the result; load that save, open barter and bet the same ware.
  - Expect: The outcome is the same win or loss as the first time (the roll is saved, reloading does not reroll).
  - Notes:

### ARC-UI-1199 — Generated interface: the schematic window (S8, 2026-11-14)

Setup: The rules are in [viewer slice S8](../rendering/hd-interface-density.md#viewer-slice-s8-arc-ui-1199). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game with a hero who has technology degrees and open the schematics window (T). Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1199 #1.** Open the schematics window in a 1280 × 800 window.
  - Expect: The window is wood with a ruled cream page, a brass-rimmed picture frame for the record, two boxes for the components, a dark channel with the assemble button and its two lamps in brass rims; the two gears with the mortarboard and the scroll, the round view of the hero and the record's picture are the originals'.
  - Notes:
- [ ] **ARC-UI-1199 #2.** Click each of the eight discipline tabs and move over them.
  - Expect: The paper bookmarks (their own outlines and glyphs) stick out of the page's right edge; the shown tab is the pressed one; the page and the record change as before.
  - Notes:
- [ ] **ARC-UI-1199 #3.** Press the learned and found buttons and the assemble button; hold each down.
  - Expect: Each is a brass-rimmed round button that sinks while held; texts, sounds and the assembling are as before.
  - Notes:
- [ ] **ARC-UI-1199 #4.** Pick a discipline with several pages and turn them with the two arrows (the arrow hides on the first and last page).
  - Expect: The arrows are drawn on the page under them (no box of another colour); the ruling goes on beneath them.
  - Notes:
- [ ] **ARC-UI-1199 #5.** Make the window bigger and smaller while the window is open.
  - Expect: The frames redraw at the new size (the previous picture stays until it is ready); the lines of the page stay one unit wide.
  - Notes:

### ARC-UI-1877 — The reading window in the original's look (2026-11-14)

Setup: The rules are in [reading window runtime](../world/reading-window-runtime.md#viewer-area-area-exterior-cave-and---area-map) and the ledger rows [WR17–WR23](../research/ledger/ui-audio.md#reading-window-arc-ui-049). Open a written item from the inventory with the **Read** button (or the right button on it), and a plaque or picture through a script that shows one. Compare with the original game on the same text. The look is the original's whether the HD interface is on or off; the Generated mode is [ARC-UI-1201](#arc-ui-1201--generated-interface-the-reading-windows-s14-2026-11-14).

- [ ] **ARC-UI-1877 #1.** Read a book (for example the one in the Crash Site's items).
  - Expect: The wood frame on the left with the round view of your hero in a ring (the ring closes the window when you release the left button in it), the book open on the right; two pages, the text in the book's own fonts, a next button on the right page's top and, from the second spread, a previous button on the left.
  - Notes:
- [ ] **ARC-UI-1877 #2.** Turn the pages with the buttons and compare where a page ends with the original.
  - Expect: A page ends where the next line no longer fits its 290 pixels; the next page continues with the same word. A message that is cut starts the next page mid-text.
  - Notes:
- [ ] **ARC-UI-1877 #3.** Read a note.
  - Expect: The note's paper with its text in one block, no page buttons.
  - Notes:
- [ ] **ARC-UI-1877 #4.** Read a telegram.
  - Expect: The form's printed lines (names, date, address) in their places and the message below, each line ending with STOP.
  - Notes:
- [ ] **ARC-UI-1877 #5.** Read a newspaper.
  - Expect: The masthead (three headings at the top, the dateline below), a headline, and the news and the fillers in four columns; a long item continues at the top of the next column.
  - Notes:
- [ ] **ARC-UI-1877 #6.** Read a plaque (a script shows it).
  - Expect: The plaque's background with white text.
  - Notes:

### ARC-UI-1200 — Generated interface: message window, spell and skill panes, Fate and Sleep (S9, 2026-11-14)

Setup: The rules are in [viewer slice S9](../rendering/hd-interface-density.md#viewer-slice-s9-arc-ui-1200). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game and compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1200 #1.** Look at the message window at the bottom strip; pick something up or wait for a message.
  - Expect: The window is a dark slate frame with a black text area; the entries, icon and scrolling are as before.
  - Notes:
- [ ] **ARC-UI-1200 #2.** Press `M` (spells) with a hero who knows spells, then `K` (skills).
  - Expect: Both panes are slate with five (spells) or four (skills) dark wells under the icon buttons and a dark name plate with a rivet at each end; the sixteen college domes show for colleges you have no rank in; the names appear in the plate when you point at a button.
  - Notes:
- [ ] **ARC-UI-1200 #3.** Press `F` (Fate) and toggle a few buttons.
  - Expect: The window is slate with a dark bar behind every second line; the toggle buttons are generated brass buttons with the green lamp, sunk when on; the lines and sounds are as before.
  - Notes:
- [ ] **ARC-UI-1200 #4.** Open the sleep window (`S`, or the sleep button) and point at its buttons.
  - Expect: Four dark bars behind the odd lines; the buttons are lit under the pointer; the lines read as before.
  - Notes:
- [ ] **ARC-UI-1200 #5.** Make the window bigger and smaller while the Fate or Sleep window is open.
  - Expect: The window redraws at the new size (the previous picture stays until it is ready).
  - Notes:

### ARC-UI-1819 — Generated interface: the common dialog box (S10, 2026-11-14)

Setup: The rules are in [viewer slice S10](../rendering/hd-interface-density.md#viewer-slice-s10-arc-ui-1819). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game, press Esc and choose Quit (the quit question). Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1819 #1.** Press Esc, then Quit, in a 1280 × 800 window.
  - Expect: The box is a dark stone plate with a thin brass rim and a black text field; the question's white text sits where it did; the green check and the red cross are in round brass buttons at the same places.
  - Notes:
- [ ] **ARC-UI-1819 #2.** Move over the two buttons and hold each down.
  - Expect: The button under the pointer is lit, the held one sinks; the answer is taken on the release, as before.
  - Notes:
- [ ] **ARC-UI-1819 #3.** Resize the window (or go full screen) while the question is open.
  - Expect: The box is sharp at the new size, with no seam between the plate and the buttons.
  - Notes:
- [ ] **ARC-UI-1819 #4.** Die in the game (Steamwoven rules) and look at the load question over the death slide.
  - Expect: The same box and buttons.
  - Notes:

### ARC-UI-1817 — Generated interface: the follower panel and the order menu (S12, 2026-11-14)

Setup: The rules are in [viewer slice S12](../rendering/hd-interface-density.md#viewer-slice-s12-arc-ui-1817). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game with at least one follower (and, for step 3, seven or more), and look at the panel on the screen's left edge. Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1817 #1.** Look at a follower's frame and the strip under the frames in a 1280 × 800 window.
  - Expect: The frame is an iron plate round the portrait, black channels under the health and fatigue bars; the strip is an iron plate with a rivet at each end and a red arrow; all at the same places and sizes as before.
  - Notes:
- [ ] **ARC-UI-1817 #2.** Click the strip (hide, then show).
  - Expect: The frames go and come back; the strip's arrow turns green and red; clicks land as before.
  - Notes:
- [ ] **ARC-UI-1817 #3.** With seven or more followers, scroll with the two arrows to both ends.
  - Expect: The arrows are iron plates with a green arrow, dark green at the end of the list.
  - Notes:
- [ ] **ARC-UI-1817 #4.** Right-click a portrait.
  - Expect: The order menu is a dark slate slab with darker bands under Attack, Spread Out, Wait and Character Sheet; the words are readable and the hover colour still shows.
  - Notes:
- [ ] **ARC-UI-1817 #5.** Resize the window (or go full screen) with the panel and the menu open.
  - Expect: Both are sharp at the new size, with no seam.
  - Notes:

### ARC-UI-1820 — Generated interface: the item identify and repair windows (S13, 2026-11-14)

Setup: The rules are in [viewer slice S13](../rendering/hd-interface-density.md#viewer-slice-s13-arc-ui-1820). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game and talk to an NPC who identifies or repairs items (a dialog with the `ii` or `ri` reply). Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1820 #1.** Choose the identify or repair reply in a 1280 × 800 window.
  - Expect: The paper doll's frame (name plate, the doll grid, the two curved pits) and the pack's frame (grid pit with its cell lines, the count bars) are drawn by code in the same places as in the inventory; the round view's ring too; the NPC's picture sits at the left of the black name plate and its quote text in it.
  - Notes:
- [ ] **ARC-UI-1820 #2.** Click an item of yours.
  - Expect: The item's cells are filled dark red, the NPC says the quote in the name plate; a long quote may run below the plate onto the wood, as in the original.
  - Notes:
- [ ] **ARC-UI-1820 #3.** Look at the small round button right of the arrangement icons (the arrange button) here and in the inventory, a chest and the barter window.
  - Expect: A dark ball in a brass ring in all four; clicking it still arranges your items (outside the service windows too).
  - Notes:
- [ ] **ARC-UI-1820 #4.** Make the window bigger and smaller while the window is open.
  - Expect: The frames redraw at the new size (the previous picture stays until it is ready); lines stay one unit wide.
  - Notes:

### ARC-UI-1201 — Generated interface: the reading windows (S14, 2026-11-14)

Setup: The plan is [hd-interface-density](../rendering/hd-interface-density.md#viewer-slice-s14-arc-ui-1201). Turn on the HD interface and the generated one in the game settings, open a written item (a book, a note, a newspaper, a telegram; a plaque or picture through a script). Compare with **HD** (generated off) and **Legacy**.

- [ ] **ARC-UI-1201 #1.** Read a book in a 1280 × 800 window.
  - Expect: The table is wood with the side bar's grain continuing under the book; the book is a cloth cover with two parchment pages, the page-edge stacks and the clasps at the fold; the text is as before.
  - Notes:
- [ ] **ARC-UI-1201 #2.** Turn the pages with the next and previous buttons.
  - Expect: The buttons are the parchment under them with the original's arrow (no box of another colour); the pages turn as before.
  - Notes:
- [ ] **ARC-UI-1201 #3.** Read a note, a telegram and a newspaper (an Extra edition if you have one).
  - Expect: A parchment sheet on the table; the telegram has its two form boxes and the double rule under the title, the newspaper the EXTRA boxes, the rules under the masthead and between the four columns; the text sits inside them as in the original.
  - Notes:
- [ ] **ARC-UI-1201 #4.** Read a plaque (through a script that shows one).
  - Expect: A dark slate slab with a second panel inside it; the white text sits on it.
  - Notes:
- [ ] **ARC-UI-1201 #5.** Make the window bigger and smaller while one is open.
  - Expect: The frames redraw at the new size; the rules stay one unit wide.
  - Notes:

### ARC-UI-1818 — Generated interface: the quantity pane (S15, 2026-11-14)

Setup: The rules are in [viewer slice S15](../rendering/hd-interface-density.md#viewer-slice-s15-arc-ui-1818). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game with gold or ammunition in the pack, and drag the stack onto another window or the ground so the pane asks how many. Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1818 #1.** Open the quantity pane in a 1280 × 800 window.
  - Expect: The bottom strip's pane is a slate plate with a black recess under the kind icon and a counter strip at the original's place; the All, Accept and Cancel buttons are the original's dark glass balls in a generated brass rim; all at the same places and sizes as in Legacy.
  - Notes:
- [ ] **ARC-UI-1818 #2.** Click up and down, then All.
  - Expect: The count changes as before; the digits are black (inferred from the original's colour channel, not seen in the original) and sit in the counter strip.
  - Notes:
- [ ] **ARC-UI-1818 #3.** Accept and Cancel.
  - Expect: The stack moves or stays as before; the pane goes away.
  - Notes:
- [ ] **ARC-UI-1818 #4.** Resize the window (or go full screen) with the pane open.
  - Expect: The pane is sharp at the new size, with no seam.
  - Notes:
- [ ] **ARC-UI-1818 #5.** Open the Save or Load window and look at the Delete Save Game button (it is the same art as the pane's Cancel button, 32).
  - Expect: It is a dark glass ball with its mark in a generated brass rim; clicks work as before.
  - Notes:

### ARC-UI-1751 — Generated interface: the buttons of the loot, barter and steal windows (S16, 2026-11-14)

Setup: The rules are in [viewer slice S16](../rendering/hd-interface-density.md#viewer-slice-s16-arc-ui-1751). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game and open a container, a dead body and a follower's barter window. Compare with **HD** and **Legacy**.

- [ ] **ARC-UI-1751 #1.** Open a container (loot window).
  - Expect: The Take all button (the big arrow ball) is the original's dark glass ball with its red arrow inside a generated brass rim; same place and size; a click takes all as before.
  - Notes:
- [ ] **ARC-UI-1751 #2.** Open a dead body's window.
  - Expect: The doll and bag toggles under the portrait are the original's balls inside a generated rim; each switches the view as before.
  - Notes:
- [ ] **ARC-UI-1751 #3.** Open the barter window of a follower while you lead two or more followers.
  - Expect: The two small arrow balls (previous and next follower) are generated balls; they cycle the follower as before.
  - Notes:
- [ ] **ARC-UI-1751 #4.** Resize the window (or go full screen) with each window open.
  - Expect: The buttons are sharp at the new size, with no seam.
  - Notes:
- [ ] **ARC-UI-1751 #5.** Start a new character and look at the race, background and gender arrows (the same arts as the follower cycle buttons, 757 and 758).
  - Expect: They are the same generated balls; clicks work as before.
  - Notes:
