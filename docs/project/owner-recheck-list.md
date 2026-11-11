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
| 1 | Cast Resist Magick (spell 40) on yourself and open the character sheet. Then let a hostile caster cast a resistable spell (one with a save, for example Stun or Charm) at you many times, with and without Resist Magick, and compare how often it lands. | The Magick resistance line on the sheet rises while the spell runs. In the original the failure rate of the enemy's spells does not change (its save reads the stored magic resistance only, ledger RE2; a bug of the game, owner decision D6c). **Steamwoven now differs on purpose** in both modes: the rate does drop under Resist Magick (see ARC-MAGIC-033 below). Steamwoven's sheet does not show effect resistances yet (known difference below). **If the rate does change in the original too, tell the coordinator.** | open |
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

## ARC-DEV-365 — Item cursor and the item use goals 39 and 41 (2026-11-11)

The hero's use icon on an item that needs a target opens a cursor, and the use walks next to the target when the original does. The rules are those of [IA1–IA16](../research/ledger/items.md#item-aiming-and-use-with-a-target-arc-res-113). The comparison needs an item of each kind from the original data (the first four steps need a scroll or a food item with a spell, which the Mac's save can be given by a script).

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Hold a scroll whose spell asks for an object (a damage scroll), open the inventory and release the item on the use icon with no Shift. | The window closes and the spell cursor opens (the same cursor art as for a spell); a left press on a creature uses the scroll on it, a right press cancels the cursor. The cursor stays open after a press that finds no target. | open |
| 2 | With the same cursor open, press a creature farther than the spell's range, then one inside it. | Inside the range the scroll is used at once from where the hero stands; outside it the hero walks next to the creature first and then uses it (item-use goal 39). | open |
| 3 | Release a scroll or potion on the use icon with Shift held. | An item whose spell can target an object is used on the hero at once, with no cursor (and no "not on yourself" test, unlike the spell cursor). | open |
| 4 | Open the cursor with a scroll whose spell has a tile in its target mask (a fire spell) and press an empty tile. | The scroll is used on the tile at once, from where the hero stands; it casts at the pressed tile, the scroll's level test and the item's use script do not run for a tile. | open |
| 5 | Hold a script trap device (a spike trap) and release it on the use icon; press an empty tile three cells away, then a creature. | The cursor accepts an object or a tile (a trap device has no spell). The hero walks next to the tile (goal 41) or next to the creature (goal 39) first; the trap device itself is not placed yet (it stays in the pack, "not ready"). | open |
| 6 | Release an item that needs a target but has no spell (a food item) on the use icon and press a creature. | The cursor accepts any object, and the item's use script sees the creature as its target. Ours runs the script without the target object. | open |

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
The original's blast-radius reading is inverted (TH1), so the original game gives every installed grenade radius 0; the owner decided on 2026-11-11 (ARC-DEV-373) that this is a bug of the game, so **both
rule modes** use the real radius of the grenade's spell and the original game will differ from them in steps 2 to 4. The modes differ in one place only: **Original** never checks PCs in the blast (SP15). Compare with an NPC that carries a grenade (the technology Explosives products, or the grenades in the crash site cave) and has throwing skill points.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Start a fight with a hostile NPC that carries a grenade and has points in throwing, standing a few tiles away with a clear line. | At the start of the fight it throws the grenade once (CHECK_GRENADE is set once per fight start); the grenade flies, explodes with its own spell and is gone from its pack. | open |
| 2 | Repeat with the hostile NPC standing right next to you. | Both modes: it does not throw while the target is within the grenade's radius and attacks instead. The original game throws anyway (radius 0, TH1); that difference is the fixed bug, not a new finding. | open |
| 3 | Put a friend of that NPC (another NPC of its faction) next to you, then fight. | Both modes: it holds the grenade while a friend it protects stands in the blast square. The original game throws anyway and hurts its friend (TH1). | open |
| 4 | Give a companion a grenade (it needs throwing points), then stand next to a hostile NPC that the companion fights. | **Steamwoven:** the companion holds the throw while you stand in the blast square. **Original:** it throws once the target is outside the radius and no NPC it protects stands in the square, and the blast can hit you, its leader (PCs are never checked, SP15). | open |
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

## ARC-DEV-370 — Created critters in the viewer (E1-c2, 2026-11-11)

The viewer draws, animates and picks a critter the session created ([E1-c2](../architecture/object-directory.md#e1-c2-created-critters-in-the-viewer-arc-dev-370)). Nothing in the game makes one yet (script action 52 for critters is E1-c3, the creature Summon is E1-e),
so these steps wait for those slices; the smoke stage `created-critter` covers the draw, the pick, the walk frame and the removal on real data.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | After E1-c3: let a script create a creature next to the hero and watch it in the first seconds. | It appears on the tile in its prototype's stand pose and facing, at the size and offset of a map creature of the same body. Compare its size and footing with a map creature of the same race. | open |
| 2 | Hover and click it, then fight it. | The cursor, name and life bar are those of a map creature; it can be attacked and dies like one, lying in the pose the kill chose. | open |
| 3 | Watch it walk and run when it chases or wanders. | Its steps use the walk and run cycles in the direction it walks, as map creatures do. | open |
| 4 | Create a creature whose art is a monster or a unique character (not a human body). | The right monster or character body is drawn; note any creature that is drawn as nothing (the log says `created-critter-art ... status=MissingFile`). | open |

Known differences (documented, not bugs):

- A prototype whose stand art file does not exist is not drawn; the original repairs a missing file by another animation (MA1), which is not applied here.
- A created creature's carried light (a torch) is not given to the lighting yet.

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

## ARC-MAGIC-031 — The hero's sector aptitude and mage backgrounds are wired (2026-11-11)

The hero's character sheet, inventory, carry weight and equipment now read the sector's aptitude adjustment, the tile under the hero, the hero's background and the game hour ([ledger AQ wiring note](../research/ledger/magic.md#aptitude-inputs-arc-magic-028)). This makes the steps of the ARC-MAGIC-028 list above playable; the expected values there stand. Magick points and aptitude are read on the character sheet.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | On the main map (`arcanum1-024-fixed`), read the aptitude on the sheet standing in sector (1749, 1512) and then in a neighbouring sector; then in (969, 1024) and (970, 1024), (969, 1025) and (969, 1030). | +40 in (1749, 1512), −20 in (969, 1024) and (970, 1024), +10 in (969, 1025), +8 in (969, 1030), 0 in an unnamed sector. The value changes at the sector border, not before. | open |
| 2 | Same on the map "k'na tha", in one of its 19 sectors with adjustment and next to it. | +30 inside, 0 next to them. | open |
| 3 | Day Mage hero: watch magick points across 5:59/6:00 and 17:59/18:00; then the Night Mage. | Day Mage 4 above the stored points from 6:00 to 17:59 and 4 below otherwise; Night Mage the reverse. | open |
| 4 | Sky Mage hero: walk from open ground into a house and a cave. | 4 above on an outdoor tile, 4 below indoors (a swing of 8 at the doorway). | open |
| 5 | Nature Mage hero: walk from a street or stone floor onto grass or forest. | 4 above on a tile with the `n` flag, 4 below elsewhere; note which grounds count. | open |
| 6 | In a sector with adjustment, put on a magickal or technological item whose weight or effect depends on aptitude and compare with the same item next to the sector. | The item's effective weight follows the aptitude shown on the sheet (not checked in the original game; note what you see). | open |

Known differences (documented, not bugs):

- Only the hero reads the ground. An NPC's aptitude does not follow its sector here, the weapon aptitude in a hero's attack reads the stat owner without the ground, and backgrounds 66–68 are not built.
- The ground comes from the shown window of the map: at the very edge of a window move, or on a cell the window holds no tile for, the hero has the neutral ground until the window moves.


## ARC-DEV-358 — ObjFlag writer: spell flags on the hero and creatures (MA-1, 2026-11-11)

The component rules are in [the ObjFlag component](../world/spell-cast-host.md#the-objflag-component-and-the-component-table-arc-dev-358-ma-1), the executable reading in [OB1–OB5](../research/ledger/spell-runs.md#objflag-component-of-the-cast-host-arc-dev-358).
Compare in both games with the same hero and spells; a spell that also has a not-ready part (Trait, Dispel, Summon) is named in the step. The flags are written by the spell; some are drawn or read by other systems only later, which the steps say.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Hardened Hands (45) on yourself, then hit a creature unarmed (no weapon in hand). | The unarmed damage gets +2 normal damage while the spell lasts, and loses it when it ends. Ours feeds the bonus from the flag; check the number in the combat log. | open |
| 2 | Cast Body of Stone (18) on yourself and hit a creature unarmed. | +5 normal damage unarmed; the spell's art change (Trait) and its weight and speed side are not ready in ours, so the hero keeps its normal body. | open |
| 3 | Cast Invisibility (69) on yourself and walk in front of an enemy that has already noticed you. | The original makes you translucent, and the enemy's awareness rules read the invisible flag. Ours writes the flags and the AI host reads them, but the viewer does not draw you translucent yet. | open |
| 4 | Cast Dweomer Shield (42), then have a creature cast a harmful spell at you (or cast at yourself from a scroll). | The shell stops the spell at the per-target stage and the spell shows the shell message (602/603). Ours already read the flag before; now the spell writes it. Its Dispel half is built since ARC-MAGIC-034 (see the next section). | open |
| 5 | Cast Bonds of Magick (43) on yourself and try to cast another spell. | The cast is refused at the invocation check while the flag holds. | open |
| 6 | Cast Reflection Shield (44) on yourself and let a creature cast a hostile spell at you. | The spell is reflected to its caster. | open |
| 7 | Cast Sanctuary (63) on yourself near a hostile creature that has not seen you yet. | The hostility rules read the enshrouded flag; compare which creatures still attack. | open |
| 8 | Cast Disintegrate (34) on a creature. | The creature takes 30000 electrical damage and dies; the original sets don't-draw on it. Ours writes the flag (and the paralysed bit) but the viewer still draws the body; the AI and the spell target rules treat it as gone. | open |

Known differences (reading only, no game step):

- Water walking (spells 13, 28): the flag and its object flag are written; the original's clear then tests the tile under the critter and may run a damage step (F0, MA-8). Ours does neither.
- Mind control, charm, undead, fleeing and blinded (MA-4) and the locks (MA-6) are still not ready and show "not ready" in the spell line.
- The viewer draws no translucent, shrunk or hidden critter from these flags yet.

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

## ARC-MAGIC-032 — Identify: See Contents, Read Aura, Divine Magick (MA-7, 2026-11-11)

The component rules are in [Identify](../world/spell-cast-host.md#identify-arc-magic-032) (executable reads XR1–XR5 in the [ledger](../research/ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)).
By owner decision D5(a) the viewer shows the answer as a line in the message log; the three windows of the original are the identify-window slice E8's, so the **window itself is a known difference**, not a bug.
Compare in both games with the same hero (Divination college: See Contents 6, Read Aura 7, Divine Magick 9).

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Read Aura (spell 7) at a creature. | The original opens the character sheet of the creature, read only (no + or − buttons, all four tabs), the same window as the dialog's "show your abilities" reply. Steamwoven: a line "identify: ..." in the message log and no window. | open |
| 2 | Cast See Contents (spell 6) at a **locked** container. | The original opens the container's inventory at once, with no lock, jam or magically-held test, so the items of a locked chest show. Steamwoven: the line only. | open |
| 3 | In step 2, take or move an item in that window. | Not read in the executable (what a press may move in inventory mode 4): note what the original allows (take, put, nothing). | open |
| 4 | Cast Divine Magick (spell 9) on an unidentified magickal item in the pack (a wand or a ring with a "?" description). | The item's name and description become the true ones, its spell slots and charge gauge show, and its worth in a trader's window is its real worth. Steamwoven builds this state (the item's identification mark), but cannot cast the spell yet: nothing picks an inventory item as a target. | open |
| 5 | Cast Divine Magick on an item that was already identified. | The description shows again; nothing else changes. | open |

Known differences (documented, not bugs):

- The three windows (the NPC sheet, the container's inventory, the item description) are E8's; the viewer says so in its line.
- Divine Magick cannot be cast from the viewer yet (no spell target in the inventory).

## ARC-MAGIC-033 — Resist Magick counts in the magic save (D6c, 2026-11-11)

The original's save reads the stored magic resistance only, so Resist Magick (spell 40) never helped. The owner decided (D6c of the [audit](../research/magic-audit-res-103.md#decisions-for-the-owner)) that this bug is fixed
in **both modes with no "Original" setting**: the save, the entangle re-resistance and the AI's fail-chance predictor read the stored resistance under the target's `resistmagic` effect changes
([rules](../world/durable-effects-runtime.md#consumers-of-resistance-and-stat-changes-arc-magic-027), [ledger RS11 to RS13](../research/ledger/magic.md#resist-magick-in-the-magic-save-arc-magic-033)).
These steps compare with the original to see the difference it makes; they are not meant to match it.

| # | Step | Expected in the original; ours differs on purpose | Status |
| --- | --- | --- | --- |
| 1 | In the original, with a hero that has a modest magic resistance (none is fine), let an enemy caster cast a resistable spell (one with a save: Stun, Charm, Entangle) at you 20 times with nothing on you and 20 times under Resist Magick (spell 40). Count how often it lands. | The same rate in both runs (the save ignores the effect). In Steamwoven the rate drops under Resist Magick: +25 magic resistance on a hero with 0 stored means a d100 below 25 resists (for a hero without a negative aptitude). | open |
| 2 | In the original, cast Resist Magick on a companion and let an enemy caster target it with a spell that has no save (for example a Damage-and-effect spell with `No_Save`), 20 times with and without. | No difference in the original. In Steamwoven about a quarter more casts are resisted and skipped. | open |
| 3 | Cast a spell with a save at an enemy that has a high stored magic resistance (an immune-type creature) while it holds Resist Magick (spell 40) or Weaken (spell 46). | The original: the creature resists as it always did. Steamwoven: a blessing never makes an immune creature easier; an effect that lowers the magic resistance (Weaken, spell 46, if its effect 154 does) lowers it. | open |
| 4 | Stack Resist Magick several times on yourself (or Resist Magick plus Shield of Protection) and let a caster hit you. | The original's sheet stops at 95 % and the save ignores it all. Steamwoven's save stops at 95 % for the hero and ordinary creatures. | open |

Known differences (documented, not bugs):

- The character sheet's Magick line still does not show effect changes (MA-2).
- The AI caster weighs its cast with the same resistance, so enemies are a little less eager to cast at a target under Resist Magick than the original's.

## ARC-MAGIC-034 — Dispel: Disperse Magick and Dweomer Shield (MA-3, 2026-11-11)

The rules are in [Dispel](../world/spell-cast-host.md#dispel-arc-magic-034), the executable reading in [DP1–DP3](../research/ledger/spell-runs.md#dispel-component-arc-magic-034).
Compare in both games with the same hero and spells.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Shield of Protection (30) on yourself, then Dweomer Shield (42) on yourself. | The Dispel at the start ends your maintained spells (Shield of Protection ends, its bar slot frees); Dweomer Shield's own run is left and keeps its slot. Ours ends the runs the target is the source of or is aimed at, but never the dispelling run. | open |
| 2 | Cast a maintained spell on a creature (for example Entangle), then cast Disperse Magick (41) on the creature. | The spell ends (its END runs: the flag is taken off). Ours ends the runs aimed at the creature the same way. | open |
| 3 | Cast Flesh to Stone (48) on a creature, then Disperse Magick (41) on it. | The creature is no longer held (it acts again). The original clears the stoned spell flag by a plain write and leaves the stoned object flag set: look at whether the creature still looks like stone or acts; ours clears the same flag and leaves the same one. Record what you see. | open |
| 4 | Cast Disperse Magick (41) on a creature that a companion or a creature cast a maintained spell on, and on a creature a technology item's effect holds. | The spells of a magic record end; the effects of a technological record (a gadget, a schematic) are never ended by it. | open |

Known differences (reading only, no game step):

- Polymorph (49) and a script's permanent polymorph (critter flags 2 bit 0x400000) are undone by the original's Dispel with the art restored; ours has neither (MA-8).
- A portal or container held by Magelock (75) loses the hold in the original; ours has no such writer yet (MA-6).
- A summoned critter is destroyed by the original's Dispel; ours destroys one the session created, and no spell makes one before the creature summons (E1-e).

## ARC-RES-120 — Function triage of the exe band 0x55-0x59 (2026-11-11)

The triage changes no behaviour. These are the places where it found original interface and world-map behaviour that our code does not reproduce at all ([ledger H2-1 to H2-13](../research/ledger/census-triage-h2.md#what-the-reading-found-that-the-earlier-documents-did-not-say)); each is read from the executable, not seen in game, so each step says what the original is expected to do.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Walk or travel on the outdoor map for 5 to 11 hours of game time, then rest on the outdoor map (the rest window open) and at night. | An encounter event fires every 300 to 700 game minutes outside named areas; its chance is 5 percent per roll, times 5 while resting, from a table by region, and a creature group (chosen by terrain, density, night and the hero's level) appears on grid rings near the party and ends the journey or wakes the rest (0x559640, 0x558DE0, 0x559010, 0x559260). Ours has no encounters. | open |
| 2 | Start a world-map journey with a companion and a maintained spell, then watch the party, the music and the maintained spell; also press W in combat and on a map other than the start map. | The party is hidden and the map music plays while travelling and the party shows again on leaving; the hero is asked to interrupt its actions before each segment and the maintained spells drop; the map opens from any map, leaving combat mode first when it can (0x5615D0, 0x565140, 0x5607E0). | open |
| 3 | Walk the hero on the outdoor map into the radius of a town you have not discovered, without opening the world map. | Entering the new sector marks the town known and adds its map marker (0x564F60, 0x564A70). Ours discovers only on world-map travel, dialog and scripts. | open |
| 4 | Start a talk with a hostile NPC while in combat mode, then with an NPC that stands with its back to the hero. | The dialog start is refused for a dead or unconscious hero, an NPC that cannot speak, a second dialog and a hero that cannot leave combat mode, and the NPC turns to face the hero (0x567460, 0x568540). Ours skips the refusals and the turn. | open |
| 5 | Put a spell on a hotkey, unlearn it in the editor, look at the bar; put an armour on a hotkey while another armour is worn and press it; drop a weapon whose spell pane is open. | The unlearned spell's hotkey empties; the key swaps the worn item and puts the old one back when the new one is refused; dropping the weapon closes the pane and clears the two quick slots that name it (0x57F340, 0x550000, 0x5570D0). | open |
| 6 | Open the hero's inventory with a heavy weapon and a low strength (for example strength 5 with a two-handed sword), then hover items in a shop and use the gamble button. | The damage and armour numbers, a weight line and an encumbrance line "Name (limit)", gold and four ammunition counts show beside the doll and the weapon is tinted red below its minimum strength; the barter hover writes a quote with a haggle percentage; the gamble button needs the merchant's gambling points and a limit by the merchant's gambling level (0x576520, 0x578330, 0x579840). | open |
| 7 | Press Enter with a companion in the party and type the companion's name and a command; use a scroll or a lockpick from the inventory and click a target; press I twice with the inventory open. | The line is sent as a broadcast order with the tile under the pointer; an item that needs a target opens the target cursor and walks next to the target when charged or out of range, lockpicks take their own path unless Ctrl is held; the second I closes the window and puts a held item back (0x571950, 0x571AA0, 0x572340). | open |
| 8 | With a party whose follower has a better disarm skill than the hero, use skill-bar disarm on a trap; also repair an undamaged item from the skill bar. | The job goes to the best follower unless Ctrl is held and a hand-over line floats over that follower; the skill result lines float over the user (0x57A620, 0x57A770 and copies). | open |
| 9 | Use a bed in a town (or press the wait key where the town map allows it) and rest to the chosen length. | The rest window steps one game hour per 200 ms, heals the sleeper and the followers each hour, drops maintained spells, fires script event 29 at the followers and ends at the chosen length, at 07:00, at 20:00 or at full life; an unconscious hero, combat that cannot be left, a refusing bed script or an occupied bed refuse (0x57B180, 0x57B710, 0x57B7F0, 0x57BAC0). Ours has no rest. | open |
| 10 | Open the character sheet and click its fourth tab; close the sheet in the new-character editor with damage on the hero. | The tab lists every named auto-level scheme (15 rows, scrollbar) and a click sets the shown character's scheme; closing in creation mode clears hit-point and fatigue damage (0x55D060, 0x55D210, 0x55DF90, 0x55A150). | open |
| 11 | Open Options from the main menu and in play. | Three pages of eight slots (module, difficulty, violence filter, combat mode, auto attack, auto switch weapons, always run, follower skills; brightness, text duration, floats, float speed, combat taunts; three volumes) and a combat mode with three values including fast turn-based (0x589280, 0x589610, 0x5896A0). Ours shows fewer slots and two combat values (ledger OP2). | open |
| 12 | Maintain three spells and lower the hero's intelligence below the number maintained times four (a curse); stand across an hour change; let the hero die. | Slots close and their spells end (0x553670); the ambient light re-applies the hour every 3,600,000 ms of game time (0x57D620); a dead hero resets the game to the main menu after 5 s (0x57D410). | open |
| 13 | Read a long book and a newspaper; right-click a follower portrait of a mind-controlled follower and of one that cannot speak. | The text wraps by pixels into page and column rectangles (ours cuts at about 700 characters, 0x56C800, 0x56CAA0, 0x56CB60); the menu greys Wait for a mind-controlled follower and Inventory when the can-open test refuses (0x56B970, 0x575080). | open |

## ARC-UI-133 — Click on the hero's lens closes the world map (PR pending, 2026-11-11)

Read in the original executable (ledger [WM140](../research/ledger/ui-audio.md), WM36): in the window's top left the executable registers a square, 89 × 89 pixels at window (25, 24), the hole of
the background art, as the hero's lens (the same view-window mode as the logbook's lens). A left release inside that square closes the map, as the key `W` does. The circle in the brass ring is only
what is drawn around the square; the click test is the whole square, corners included. Known differences, not to be re-reported: in the original the lens shows the game around the hero at half size
with the cover art 198 over it; in ours the square still shows the world behind the window, unscaled, and no cover art (its own task, because it also decides the ARC-UI-126 look of the hole).
In both rule modes the click behaves the same.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Open the world map (`W` or the bar's Map button) and click the small window in the top left of the brass frame, in the middle of the circle. | The map closes and the game world is shown again, on press and release of the left button (the release does it). | open |
| 2 | Open the map, press the left button on the lens, keep it down, move the pointer out onto the wood and release there. | Nothing happens (the test is on the release, at the release point); the map stays open. | open |
| 3 | Open the map and click in a corner of the square, outside the round ring (the corner where the wood shows through), and just outside the square on each side. | The corner of the square closes the map; a click one pixel outside the square does not. | open |
| 4 | Open the map, start a journey (Travel), and click the lens while it runs. | The map closes through the same close as `W`; the walking party stops where it stands (WT12). In ours this lands the party at its current place. | open |
| 5 | In a town-map sector (kind 2) and in the continent view (button below the zoom), click the lens. | The map closes in every view. | open |
| 6 | With the HD interface switch on, repeat step 1. | The same as in the original look: the click works in HD too. | open |


## ARC-MAGIC-035 — Social and AI flags: Charm, Dominate Will, Nightmare, Flash, Create Undead (MA-4, 2026-11-11)

The rules are in [Social and AI flags](../world/spell-cast-host.md#social-and-ai-flags-arc-magic-035), read from the executable in [SO1 to SO8](../research/ledger/spell-runs.md#social-and-ai-flags-arc-magic-035). Compare them in both games with the same
hero (a mage with these spells; a hero without the college can learn them from the spell book by cheat or lend them through a wand). No viewer smoke stage drives these spells, so these steps are the only run of the whole path.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Charm (spell 35) on a calm human who is not yours, then talk to them. | Their reaction toward you is 30 higher than before the cast; when the spell ends it drops by 30 again. | open |
| 2 | Cast Charm Beast (spell 50) on an animal that is attacking you. | The animal stops attacking you (peace with you). When it ends the reaction does not change except by the -30 of a charmed NPC. | open |
| 3 | Cast Dominate Will (spell 39) on a hostile human. | Their reaction drops by 50 at once, they join you as a follower (they follow you and will not flee), and fight on your side. When the spell ends they leave your party (the reaction stays low). | open |
| 4 | Cast Control Beast (spell 52) on an animal. | The same as step 3: the animal follows you and does not flee; at the end it is free again. | open |
| 5 | Cast Nightmare (spell 38) on a human who fights you. | They run away from you (a flee goal) and do not attack while it lasts; at the end they stop fleeing. | open |
| 6 | Cast Flash (spell 66) on a human who fights you, and watch their hits. | They are blinded (their melee is much worse); at the end, or by chance each AI pass, the blindness goes. | open |
| 7 | Cast Create Undead (spell 58) on a corpse. | The creature rises under your control (it follows you), and at the spell's end it dies again. Note whether it is hurt by healing or poison as an undead would be. | open |
| 8 | Dominate Will on a creature that is already your follower, and on one that is summoned. | A summoned creature does not lose reaction (no -50). | open |

Known differences (documented, not bugs):

- A charm adds the NPC flag "kill-on-sight override" in the original and never takes it off; ours does not keep that flag.
- Mind control makes a follower only under the hero (a spell cast by another creature on a creature does not make a follower); a mind-controlled creature of the world map is not covered.
- The undead flag a spell writes is not read by the combat or life rules yet, so step 7's undead behaviour may differ.
## ARC-DEV-372 — Shared content index (O4 of the performance diagnosis, 2026-11-11)

The viewer's readers now share one index of the game folder per run instead of each indexing it again. Nothing in the game's behaviour is meant to change, so the steps compare the
start and the first window opens with the original game: the same texts, names and numbers, and nothing missing that the original shows.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Start a new game and read the first screen: the hero's name and stats, the first dialog, the inventory (`I`), the character sheet (`C`) and the logbook (`L`). | The texts, names and numbers are the ones the original shows for the same hero and map; no window opens empty or with a missing label. | open |
| 2 | Open the world map (`W`), the spell pane (`M`) and the schematics (`T`) on the first map, then walk to a cave or another map and open the same windows again. | Same content as before the change; the second map's windows are as complete as the first's. | open |
| 3 | Look at an NPC with a hover name, a book or note to read, a shop's barter window and a companion's greeting. | Names, texts and prices match the original; none is blank. | open |
| 4 | Start the game, quit, and start it again. | The second start shows the same content as the first. | open |

Known differences (documented, not bugs):

- The index is read once per run (as the original reads its static installation): a file added or changed in the game folder while the game runs shows after the next start. An archive that changes in size or time is read again.

