# Owner re-check list

Places where the behaviour was built from the original executable and the data, but the owner has not yet compared it with the original game. On 2026-11-11 the owner asked that
slices no longer wait for his in-game comparison: they merge on their checks and add their comparison steps here. The owner works through this list in game later.

Rules for this file:

- One section per slice (task code, PR, date), newest last. Steps are short, in game terms, and say what the original is expected to do and on which map or spell.
- Each step has a status: **open** (not compared yet), **same** (the owner saw the original do the same), or **differs** (with a link to the bug report or task that follows it).
- Known differences that are already documented go under the section's own heading, so they are not re-reported as bugs.
- When a step differs, the coordinator turns it into a task. The step stays here with its link until that task merges.
- Repository text only: no screenshots, frames or other game content.

## ARC-DEV-352 — Fireflash and Wall of Fire (PR #941, 2026-11-11)

The component rules are in [Summon for objects](../world/spell-cast-host.md#summon-for-objects-arc-dev-352). Compare them in both games with the same hero and spells. The first build
for this comparison was `artifacts/packages/macos` at commit d2899817a; any later build from `main` serves too.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Fireflash (spell 22) at empty ground. | A flash appears on the strike tile for about 0.75 s and goes away by itself. | open |
| 2 | Cast Fireflash at a creature that stands on a tile. | No flash object, because the tile is not empty (`Tgt_Tile_Empty`). Note also what the creature itself gets (eye candy, damage): the Summon rules do not decide that, so it is part of the comparison. The owner does not remember this empty-tile rule, so it is the main thing to check. | open |
| 3 | Cast Wall of Fire (spell 21), once on open ground and once next to a wall. | A straight line of 5 pieces centred on the target tile; the line runs along the direction from the caster to the target (TA12). A tile with a wall gets no piece (`Tgt_Tile_Empty_Immobiles`); creatures do not stop a piece. Also check that the line runs along the cast direction, not across it. | open |
| 4 | Walk the hero or a creature through the wall. | The pieces do not block passage: their prototype carries the "no block" flag in the data. | open |
| 5 | Stand the caster and a companion in the wall, then a hostile creature. | The caster and creatures led by the caster's party are not burned. Other creatures burn each step, and in turn-based mode once per round. | open |
| 6 | Stop maintaining Wall of Fire (or let it run out). | All pieces disappear at the same moment. | open |

Known differences (documented, not bugs):

- The burn source of a wall piece lives in the viewer. After a map window move or a sector reload, the piece also burns its summoner, as the original's restarted run does.
- Critter summons (Call Elemental and the others) are not built yet (E1-e).
- Wall of Stone and Wall of Force pieces are created, but walking and path finding do not see them as blocking yet.

## ARC-MAGIC-027 — Effect consumers: resistances and buffs on NPCs (2026-11-11)

Resistance and stat changes of effects now reach combat values for the hero and NPCs ([consumers](../world/durable-effects-runtime.md#consumers-of-resistance-and-stat-changes-arc-magic-027),
[audit](../research/magic-audit-res-103.md#effect-consumers-ma-2-arc-magic-027)). Spells named below put the effect on the target; the flags and the body art of the Body spells are not built yet, so compare numbers only.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Resist Magick (spell 40) on yourself and open the character sheet. Then let a hostile caster cast a resistable spell (one with a save, for example Stun or Charm) at you many times, with and without Resist Magick, and compare how often it lands. | The Magick resistance line on the sheet rises while the spell runs. The failure rate of the enemy's spells does not change, because the original's save reads the stored magic resistance only (ledger RE2). Steamwoven's sheet does not show effect resistances yet (known difference below). **If the rate does change in the original, tell the coordinator: it is the owner question D6 of the audit.** | open |
| 2 | Cast Shield of Protection (spell 30) on yourself, then take a normal-damage hit from a weapon, a fire hit and an electrical hit, with and without the spell. | Armour class rises by 10 and the damage of each type falls by the resistances of effect 155 (rules data), never below zero damage and never past 95 % protection. | open |
| 3 | Cast Body of Fire, Body of Air, Body of Stone and Body of Water (spells 23, 13, 18, 28) one after the other on yourself and take a fire or an electrical hit. | Only the resistance and speed numbers are compared here: Body of Fire protects against fire, the others by their effect rules; the body art and the flags are not built. | open |
| 4 | Cast Weaken (spell 46) on a strong enemy in turn-based mode, and fight it. | Its strength falls by 5 and its resistances drop by effect 154 (compare the damage you deal to it and the damage it deals). Its hit points do not change in Steamwoven (known difference). | open |
| 5 | Cast Congeal Time (spell 76) on an enemy and enter turn-based mode. In real time watch its attack pause. | Its action points per round fall (speed −50 %); in real time its attacks come slower; its walking speed does **not** change (the original does not read the speed stat for walking). | open |
| 6 | Cast Hasten (spell 77) on yourself and on a companion, and enter turn-based mode. | More action points per round (speed +100 %) and quicker attacks in real time; walking speed is unchanged. | open |
| 7 | Stack protective spells until the sheet's resistance would pass 95 %. | The protection stops at 95 % for the hero and ordinary NPCs. A monster (animal, undead, mechanical) is not capped in the original. | open |

Known differences (documented, not bugs):

- The character sheet's resistance lines (hero and NPC) do not show effect changes; damage does.
- An NPC's maximum hit points do not follow its effect-changed strength or constitution, and a poison tick meets its resistance without effects.
- Spell damage dealt to an NPC ignores that NPC's resistance effects until PR #941 merges (the spell host reads the NPC without its list).
- The hero's innate race and gender effects carry no resistance changes in the session.

## ARC-MAGIC-029 — Script conditions about spells and the mastery action (MT21a, 2026-11-11)

The interpreter now reads four spell conditions (has a body spell, is under the influence of a spell, knows a spell, has mastered a college) and the action "give spell mastery in college" ([ledger SC1–SC6](../research/ledger/scripts.md#spell-conditions-and-the-spell-mastery-action-arc-magic-029-mt21a)). **Nothing changes in game yet**: no host answers the conditions, so 22, 6, 16 and 16 installed scripts (conditions 14, 35, 48, 49) and 2 (action 112) still stop where they stopped before. These steps become testable when the host wiring lands; they are listed now so the comparison is planned.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | With a character that knows a spell of a college (and one that does not), talk to or trigger a script that asks "knows spell" (32 lines in 16 installed scripts). | The answer is yes only when the spell's rank is at most the rank the character has in that college (so knowing rank 2 of a college means knowing the college's rank 1 and 2 spells). A spell number outside 0..79 is never known. | open |
| 2 | Compare a character with no mastered college and one who mastered a college against a script that asks "mastered college N". | Equal college number is yes. Asking for college −1 is yes for a character with no mastery. | open |
| 3 | Reach a script that gives mastery (2 installed scripts) with a character that has five ranks in the named college, then one that has fewer, then one that already mastered another. | Mastery is written only for a hero or NPC with no mastered college and five ranks in that college; otherwise nothing happens and the script goes on. Mastery cannot be given twice. | open |
| 4 | Stand under Body of Fire, Earth, Water or Air (spells 23, 18, 28, 13) next to an NPC whose script asks "has a body spell" (22 installed scripts). | Any of the four body bits counts; invisibility and floating do not. | open |

Known differences (documented, not bugs):

- No host answers or writes these yet, so the steps cannot be played until the follow-up that wires them (the spell book and flags for NPCs, the spell runs, the mastery write).
- Giving mastery to an object the script does not name does nothing here; the original reads an uninitialised handle.

## ARC-DEV-359 — Created critters, session and hosts (E1-c1, 2026-11-11)

The session now creates, saves and hosts a critter made from a prototype ([created critters](../architecture/object-directory.md#e1-c1-created-critters-arc-dev-359)). Nothing in the game makes one yet (script action 52 for critters is E1-c3, the creature Summon is E1-e),
and the viewer does not draw it (E1-c2), so there is nothing to play and compare. The steps below wait for those slices.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | After E1-c2 and E1-c3: let a script create a creature next to the hero (a script that creates a monster at a tile), and watch it. | It appears on the tile, stands there at first, and starts its ordinary behaviour after a short delay that grows with its distance from you (a created NPC gets the delayed first tick, RL16 and RL17). Hostile ones come at you; peaceful ones stand or wander as their prototype says. | open |
| 2 | After E1-c2: save with a created creature alive, load. | It is still there, where it was last standing. | open |

Known differences (documented, not bugs):

- A created creature has an empty inventory (the original generates its inventory list at creation, OM16) and its tile scripts do not fire where it is made (the location notification, GR12): both are E1-c3.
- Its teleport destination field (OM13) is not written; the field is not identified in the repository.
- The state records of a destroyed created creature stay in the save (its entity is never given again).

## ARC-MAGIC-028 — Aptitude inputs: sector adjustment and mage backgrounds (2026-11-11)

The rules and the readers are merged ([character stats](../world/character-stats-runtime.md#context-inputs-arc-magic-028-fr-char-020), [ledger AQ1–AQ8](../research/ledger/magic.md#aptitude-inputs-arc-magic-028)),
but the hero's sheet and the other callers still pass sector adjustment 0 and no background, so **our game shows none of this yet**; the steps below only record what the original game does, to compare when the wiring slice has
landed. Magick points and aptitude are read on the character sheet.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | On the main map, read the aptitude on the sheet inside the sector at (1749, 1512) and in a neighbouring sector with no adjustment; then in the sectors at (969, 1024) and (970, 1024), and at (969, 1025) and (969, 1030). | Aptitude is shifted by +40 in (1749, 1512), by −20 in (969, 1024) and (970, 1024), by +10 in (969, 1025) and by +8 in (969, 1030), then clamped to −100..100. A sector of the 1 654 with metadata that is not named here adds 0. | open |
| 2 | On the map "k'na tha", read the aptitude inside one of its 19 sectors with adjustment (for example the one at (12, 12)) and in a sector next to it that is not one of them. | The sectors with adjustment add +30; the others add 0. | open |
| 3 | Take a hero with the Day Mage background and watch the magick points on the sheet across 5:59/6:00 and 17:59/18:00 game time. | The value is 4 above the stored points from 6:00 to 17:59 and 4 below it from 18:00 to 5:59: a swing of 8 at both edges, and aptitude follows. | open |
| 4 | The same with the Night Mage background. | The reverse of step 3: 4 above from 18:00 to 5:59, 4 below from 6:00 to 17:59. | open |
| 5 | A Sky Mage hero walks from open ground into a house, a cave or a dungeon. | Magick points are 4 above the stored points on an outdoor tile and 4 below on any other tile (including where the game has no tile data); a swing of 8 at the doorway. | open |
| 6 | A Nature Mage hero walks from a town street or a stone floor onto grass, forest or other natural ground. | Magick points are 4 above the stored points on ground whose tile name has the `n` flag (the Arcanum module's names have it on 21 of the 31 outdoor flippable, 1 of the 6 outdoor, none of the 2 indoor flippable and 4 of the 13 indoor records) and 4 below elsewhere. Note which grounds count: that list is the point to compare. | open |

Known differences (documented, not bugs):

- No caller reads the sector or the tile where the hero stands yet, so nothing above is visible in our game.
- The other backgrounds' situational stat changes (66, 67, 68: outdoors, on water, low light; strength, dexterity, intelligence, willpower) are not built (AQ8).

## ARC-RES-108 — Executable triage of 0x44-0x45 (2026-11-11)

[Ledger section](../research/ledger/census-triage-0x44-0x45.md). The triage read 391 functions of the executable and compared them with the code; the differences below are the ones where our code does something other than the function. They are read from the executable, not seen in game, so each step says what the original is expected to do.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Cast a knockback spell (a spell with a push component) from an inventory item such as a scroll or wand at a creature that is not in line with you and the item. | The creature is pushed away from the spell's parent object (here the holder of the item), else from the run origin. Ours pushes away from the run's source object, which differs when source and parent differ (0x4514E0). | open |
| 2 | With a hero who has the backstab skill trained but no points in it, attack a creature from behind. | The original needs the skill level to be non-zero; ours needs the training to be above none, so the two differ for training without points (0x45F9D0). | open |
| 3 | Put the party into a fight and then end it with peace (the peace spell or dialog end of combat) while companions are mid-attack. | The attack goals of the peace head and of its followers are interrupted at level 5 (0x44E050, 0x44E0E0); ours leaves those runs going. | open |
| 4 | Stand in a place where a script or spell looks for the critters "near" the hero (a vicinity-based script condition or action), once at the edge of the screen and once close. | The vicinity is the game window rectangle centred on the location, converted to tiles; ours uses a fixed square of 10 tiles on both axes (0x440FF0). | open |
| 5 | Walk a path across a tile with burning scenery (fire from a spell or trap) with a mover that avoids obstacles (a companion following you). | A tile with a running fire goal costs 80 more when the mover carries walk flag 0x400; ours has no such flag, so a path never avoids burning scenery (0x444500). | open |

Known differences (reading only, no game step):

- Assign object operand inside a looped script object (focus kind 10): the original writes the loop's current object, ours ignores that write (0x44B9B0); whether an installed script does this is not checked.
- Midnight: at exactly 86,400,000 ms into a day the original still reads the old day word (0x45AA10, 0x45AA50), ours normalises at once; day, month and year differ for that one instant only.
- Documentation: [magic core evidence](../research/magic-core-evidence.md) lists 0x452ED0 under Trait, but the component table puts it at type 24 and it runs the target object's use script (the Use component).
- Documentation: the surrendered-NPC flee on dialog start (goal table GT220, NPC morale) goes through the same-party test 0x45E2E0, which is always false in single player (party id -1 unless the network flag is set); ours has no flee, which agrees.

## ARC-RES-110 — Function triage of the exe bands 0x46, 0x49 and 0x4A (2026-11-11)

The triage changes no behaviour. These are the places where it found original NPC behaviour that our code does not reproduce at all ([ledger FT6](../research/ledger/census-triage-d.md#what-the-reading-found-that-the-earlier-documents-did-not-say)); each step is to see the original do it, so that the follow-up task can be sized. The readings are F1 (one reader).

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Open or attack a container or a door in the open view of a guard NPC (a shop keeper or a town guard), then of an ordinary passer-by. | Guard NPCs, and NPCs of social class 5, within 10 tiles see or hear it: each of them attacks, loses 20 reaction or floats a warning (0x4AEE50). Ours only raises a flag and nobody reacts. | open |
| 2 | Watch NPCs whose AI row forbids opening portals next to a closed door on their route. | The AI parameter flag of slot 16 decides whether an NPC may open a portal; the player may always (0x4AEB10). Ours stores the flag and never asks it. | open |
| 3 | At night, move a sleeping NPC off its bed (push, teleport or leave the map and come back) and wait. | The sleeper is put back onto a bed object found at its night standpoint, else at its day one (0x4AD0B0, 0x4AD140). Ours leaves it where it is. | open |
| 4 | Frighten an NPC with a fear effect, or let one surrender in a dialog. | It starts to flee from the source (the start-flee request 0x4AF130). Ours has only the stop-fleeing side. | open |

## ARC-RES-118 — Function triage of the exe band 0x42-0x43 (2026-11-11)

The triage changes no behaviour. These are the places where the readers found original behaviour that our code differs from or lacks, each checked in the code ([ledger](../research/ledger/census-triage-b.md#findings-worth-a-task)); each step is to see the original do it, so that a follow-up task can be sized. The readings are F1 (one reader).

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Watch an NPC that wanders (a townsperson with the wander flag) once it has reached its standpoint, by day and in the dark. | It starts a wander goal (goal 65, in the dark goal 66) and walks to a destination near the standpoint (0x435E60, 0x436040). Ours starts none: the standpoint step of the route rules is marked not implemented, so the NPC stands still. | open |
| 2 | Stack two critters on one tile (for example a refused attack start next to a follower, or a follower asked to step aside) and watch them. | The NPCs are asked to move away from each other (the step-apart request 0x4364D0, goal 68). Ours has an empty step-apart method, so they stay on one cell. | open |
| 3 | Talk to an NPC (a dialog open) and, while the dialog is open or just after it, make a script or route walk start for that NPC in a round. | The start gate asks the activity test and whether the NPC has a conversation partner, not only whether it is the round's current participant (0x4339A0, 0x4C1110). Ours tests only the participant, so an NPC in a conversation may be sent walking. | open |
| 4 | Swing and hit a creature with a weapon that has its own eye candy (a magic weapon), and with a blow that draws blood. | The weapon's own eye candy plays at the swing and at the hit, and the one at the hit may veto the damage effect (0x4377C0); the blood eye candy of the hit plays (0x433020). Ours plays neither (only the blood pool of a kill). | open |

## ARC-DEV-364 — NPC grenades (T-h, 2026-11-11)

An NPC in a fight can now pick a grenade from its pack and throw it at its target ([grenade step](../world/ai-host.md#grenade-step-arc-dev-364), [ledger TH1–TH4](../research/ledger/npc-ai.md#npc-grenade-host-arc-dev-364)).
The settings differ on purpose in one place: the original's blast-radius reading is inverted (TH1), so **Original** keeps radius 0 for every installed grenade and **Steamwoven** uses the real radius of the
grenade's spell. Compare in both games with an NPC that carries a grenade (the technology Explosives products, or the grenades in the crash site cave) and has throwing skill points.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Start a fight with a hostile NPC that carries a grenade and has points in throwing, standing a few tiles away with a clear line. | At the start of the fight it throws the grenade once (CHECK_GRENADE is set once per fight start); the grenade flies, explodes with its own spell and is gone from its pack. | open |
| 2 | Repeat with the hostile NPC standing right next to you. | **Original:** it still throws, because the radius it tests is 0 (TH1). Steamwoven: it does not throw while the target is within the grenade's radius and attacks instead. | open |
| 3 | Put a friend of that NPC (another NPC of its faction) next to you, then fight. | **Original:** it throws anyway, hurting its friend. Steamwoven: it holds the grenade while a friend it protects stands in the blast square. | open |
| 4 | Give a companion a grenade (it needs throwing points), then stand next to a hostile NPC that the companion fights. | **Original:** the companion throws anyway and the blast can hit you, its leader (PCs are never checked, SP15; the radius is 0 here too, TH1). Steamwoven: it holds the throw while you stand in the blast square. | open |
| 5 | Walk behind a wall or a closed door so a blocker stands between you, then fight. | It does not throw while anything blocks the line to your tile (the target tile's own objects do not count). | open |
| 6 | An NPC with a grenade and no throwing points (a creature with none). | It never throws and attacks as before. | open |

Known differences (documented, not bugs):

- Only the first generic item with the grenade flag in the NPC's list is thrown, as in the original; an NPC does not pick the strongest grenade.
- The NPC's throw animation and the grenade's flight use the viewer's stand-ins; the NPC's own art is not read yet.

## ARC-RES-117 — Executable triage of 0x40-0x41 (2026-11-11)

[Ledger section](../research/ledger/census-triage-a.md). The triage changes no behaviour (it adds registry rows and `[OriginalFunction]` marks). These are the places where the reading found original behaviour that our code does not reproduce; each step is to see the original do it. They are read from the executable, not seen in game.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Make a creature back off or flee from a target (a scared or surrendering NPC, a follower ordered to back off) in open ground. | It walks away along a tile found by the away-path search (0x41F6C0). Ours finds no away tile, so the back-off ends at once. | open |
| 2 | Talk to a teacher, then to a healer, a rumor monger and a newspaper vendor (a reply that opens a skill list, a cure, a rumor or a paper). | The replies marked for trainers, healers, rumors, town directions and newspapers show their generated lines and offers; training by an apprentice costs 100 gold (0x418CA0..0x41A8C0). Ours refuses these replies as unsupported. | open |
| 3 | Start a talk with an NPC that is hostile on sight, then with one that cannot speak to you (too evil, too good, party full). | A hostile NPC answers with its race line and the talk ends; an NPC that cannot speak refuses with the line of its reason range (0x412FD0, 0x413A90). Ours opens the talk first. | open |
| 4 | Ask a talkative NPC for money (a dialog money request), once with enough gold and once without. | The request, payment and no-money steps run from the reply (0x418A40, 0x418B30, 0x418C40). Ours has no such reply state. | open |
| 5 | Save while combat music plays (any fight), reload that save and leave the fight. | In the Original setting the pre-combat music and ambient schemes are lost by the save (original bug SN18, 0x41B1D0); Steamwoven keeps them, so compare which music returns. | open |
| 6 | Walk through a door or fight a critter, then leave the map while its run is still going and come back. | The map-clear flag gates the end nodes of goal runs (0x410060); ours has no such flag, so compare how door and critter runs end across the map change. | open |

Known differences (reading only, no game step):

- NPC path searches are limited to about ten per second and 250 ms of search time per second in the original (0x41F9F0); ours always searches.
- The map patch reader asks for 4,196 bytes into a 4,096-byte buffer, and a matched line with no colon dereferences null (0x410D10, 0x411940; ledger MP2, MP4): original bugs, not reproduced.
- The art id normalisation sets the male gender in the explode branch where our reading wrote female; no drawn name differs (0x41D510, ledger MA2).
- The dialog second-operand scan accepts only space and tab in ours, every C library white space in the original (0x4167C0, project policy).
- The follower near-death line has no activity or null-callback test in the original (latent bug, 0x414490, ledger FO16).
- A paid skill service's forced-success request is dropped by the original (SK8, 0x419CB0).
- The character-creation notes record a repeated place-choice branch in the player creation (0x40DB70) that was never settled.

## ARC-MAGIC-030 — The script hosts answer the spell commands (MT21b, 2026-11-11)

The use, tile, sector, heartbeat, dying and talk script hosts now answer "has a body spell", "under the influence of a spell", "knows a spell" and "mastered college" and write "give spell mastery" for the hero and for an NPC of the shown map ([ledger SC7–SC9](../research/ledger/scripts.md#spell-conditions-and-the-spell-mastery-action-arc-magic-029-mt21a)). It completes the steps of the ARC-MAGIC-029 list above. **Few installed scripts run yet**: 46 use these commands and only a handful are admitted whole, because they also use commands later slices bring (get stat 33 in 17 of them, action 91 in 16, others), so a step may stop where the script meets one of those.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | With a hero who has learned all five spells of a college and has no mastery, reach a script that gives mastery of that college (2 installed scripts), then look at the college on the spells page. | The college is now mastered (its spells cost half and last twice as long between maintenance ticks). Reaching the script again, or one for another college, changes nothing. | open |
| 2 | The same with a hero who has fewer than five spells of the college. | Nothing changes and the script goes on. | open |
| 3 | Ask "knows spell" (16 installed scripts) of a hero who knows rank 3 of a college, for the college's rank 3 spell and its rank 4 spell. | Rank 3 is known, rank 4 is not. | open |
| 4 | Reach a script that gives mastery to an NPC who has all five ranks of a college (a mage NPC of the shown map). | The NPC's mastered college is set once and stays; an NPC the map data gives no sheet for is skipped with the run left unapplied (see below). | open |

Known differences (documented, not bugs):

- Standing under Body of Fire, Earth, Water or Air does **not** make "has a body spell" true yet: no spell component writes the body bits of the NPC's spell flags (the ObjFlag component takes only the hold bits). Step 4 of the ARC-MAGIC-029 list stays open until that is built. A body bit already in the NPC's map data does count.
- "Under the influence of a spell" tests the run's target only; the original also looks in the run's two object lists (as ledger AC1).
- Mastery for an NPC of another map, a PC-type row on the map, or an NPC whose map data gives no sheet is not applied; the run is left unapplied and the reason is logged.


## ARC-RES-119 — Function triage of the exe bands 0x50 to 0x54 (2026-11-11)

[Ledger section](../research/ledger/census-triage-h1.md#function-triage-of-the-exe-bands-0x50-to-0x54-arc-res-119). The triage read 933 functions of the engine library and the interface of the executable and changed no behaviour; the one step below is where it found a visible difference from our held-item cursor. The reading is F1 (one reader).

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Pick up an item from the inventory so that it hangs on the cursor, then move the cursor to the right and the bottom edge of the screen. | The cursor art laid over the item's art is clipped to the screen and its hot point is shifted, so the held cursor moves to stay on screen (0x500160, 0x5000F0). Ours places the overlay at a fixed offset from the hot point and does not shift it. | open |

Known differences (reading only, no game step):

- Interface reset: the original's stack reset also zeroes the message log words (count, newest and shown) next to the mode stack (0x54AA30); ours clears the mode stack only. Not visible until the message log is read after a reset.
- Pre-made heroes: the original offers 8 heroes and 8 buttons unless the art of prototype 16075 exists (then 12 and 10, 0x549A80); ours always lists twelve. No visible difference with the retail data.
- Art id readers: the original answers scale index 4 for any non-eye-candy art (0x504C60) and -1 for a non-roof art (0x504840); ours do no class test. Harmless while callers pass only those arts.
- Archive ignore lists: the original applies a `.ignore` entry only when the backslash count of the pattern equals that of the entry name (0x53CCE0); ours refuses archives that carry such a file.
- Art height (0x503F60) reads the critter height table at entry 0 for every critter because it passes the art number where the body field was meant (0x502710 against 0x503EA0); ours agrees (height 1) and the original behaviour is kept. Classify under D8 as an original bug before any switch is considered.
- Documentation: the labels of 0x502710 (items ledger WA6, interface ledger), 0x53E730 (SF3, the module loop, not the reset 0x53E6E0), 0x5407F0 (a start-up rumor check, not the logbook window) and the popup reachability of TL11 (0x539AA0 is also opened by 0x53A080) need correcting; the num-lock question IA8 is answered (the read is not inverted, 0x52B350); the dat marker variants do not share a footer layout (0x53E030).

## ARC-DEV-363 — Technology census (slice T-b, PR #971, 2026-11-11)

The census test pins which components of the 83 technology records the host takes ([audit section 7](../research/technology-audit-res-104.md#7-smoke-and-test-coverage)); it
reads our code, not the original, so the steps below are the places where the census says a record runs but nobody has compared its effect with the original game. Use a
hero with the technology points for the degrees named, or the items from a map or a console. Compare numbers and what happens on screen, not art or sound.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Throw a flash grenade (an Explosives product of degree 1 or 5, or one found on a map) at an enemy standing on open ground and at an empty tile. | Fire and normal damage land on the creatures in the area and the target is pushed; a flash object appears on the strike tile and goes away by itself (the census counts the record as running: the summon of its scenery and its destroy). | open |
| 2 | Throw a stun grenade (Explosives degree 4) and a smoke grenade (degree 3) at a group. | The stun grenade stuns creatures in its radius with a flash; the smoke grenade makes a smoke cloud for about 10 s, then it goes. The smoke grenade also sets the don't-draw flag on creatures, which is not built (the census lists it as partly running). | open |
| 3 | Drink a Therapeutics potion (any of the seven stat potions) as the hero, and give the same potion to a companion by a script or by throwing it. | The stat bonus (+2 to the stat or stats named) appears on the sheet and goes away when the potion ends. On a companion the census says the effect list is written; compare the numbers shown or their effects in a fight. | open |
| 4 | Use Chemistry degree 1 (poison draught) on a creature, and Chemistry degree 4 (acid) on a creature. | The poison does 290 damage over its duration; the acid does 10 to 100 damage and makes a flash. Compare the damage numbers with the original. | open |
| 5 | Find a map item that carries the record 214 (a technology item on a map; see the placement census) and use it as the hero. | The original adds an effect 80 times; our effect list holds 64 entries, so nothing is added here (refused). Note what the original does to the sheet. | open |

Known differences (documented, not bugs):

- The census checks the host's component dispatch, not a cast: area, save, run order and the item side are outside it (the Begin Destroy of a pack item, record 184).
- The Destroy of a grenade is left out of the census and covered by its own tests and the `grenade`, `summon-flash` and `summon-wall` stages.
- Records with an unbuilt ObjFlag bit (blind, flee, fatigue limit, drunk, magnetic inversion, enshroud, shield, don't-draw, permanent polymorph), the cell branches, Use, Cast, Movement, Trait, AIRedirect and the creature list of record 183 are listed in the audit as open.

## ARC-DEV-366 — Tech books give expertise equal to intelligence (PR #980, 2026-11-11)

The rules and what is built are in [tech books](../world/inventory-runtime.md#tech-books-arc-dev-366) and [ledger XA9, XA11](../research/ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114).
Where the eight books lie in the world is not read yet ([census](../research/technology-census-res-115.md)), so the first step is to find where one lies in the original; in ours it works wherever the viewer lets the hero pick up, loot or buy a book. Known differences, not to be re-reported: taking a book out of the pack by a drop, a sale or a gift is
refused in ours (the removal script does not run there yet); a book picked up from the ground after the hero dropped it is refused likewise.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | With a hero of intelligence 8 to 10 and no degree in the discipline, open the character sheet's technology page, note the expertise of the book's discipline, pick up a tech book (for example the Herbology Manual) and look again. | The discipline's expertise rises by the hero's intelligence (not by 1) the moment the book is in the pack; the other disciplines do not change. | open |
| 2 | Pick up a second copy of the same book, or a book of another discipline. | A second copy adds the intelligence again (the effects stack); another discipline's book raises that discipline. | open |
| 3 | Raise the intelligence while carrying a book (a potion, an item or a level-up), look at the expertise, then move the book to another place in the pack grid and look again. | Before the move the expertise still counts the old intelligence; after the move (the original removes the book's effects and adds them again) it counts the new intelligence. | open |
| 4 | Take the book out of the pack (drop it or sell it). | In the original the expertise falls back by the intelligence the book last counted. In ours the drop and the sale are refused for now (see above), so only the original can be compared here. | open |
| 5 | Open the barter window of a merchant that sells a tech book and buy it. | The book enters the pack and the expertise rises as in step 1 (the purchase fires the same insertion). | open |
