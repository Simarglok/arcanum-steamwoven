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
| 3 | Cast Wall of Fire (spell 21), once on open ground and once next to a wall. | A straight line of 5 pieces centred on the target tile; the line runs along the direction from the caster to the target (TA12). A tile with a wall gets no piece (`Tgt_Tile_Empty_Immobiles`); creatures do not stop a piece. Also check that the line runs along the cast direction, not across it. ARC-MAGIC-054: the pieces are drawn additive, as the original's translucent flames. The owner saw 5 pieces in the retail game (2026-11-12) ([note](../research/wall-of-fire-look-magic-054.md)); ARC-MAGIC-055 drops pieces on blocking terrain. | same (5 pieces, owner 2026-11-12) |
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
| 4 | Cast Weaken (spell 46) on a strong enemy in turn-based mode, and fight it. | Its strength falls by 5 and its resistances drop by effect 154 (compare the damage you deal to it and the damage it deals). Its maximum hit points fall by 10 (two per point of strength, ARC-WORLD-1144): a unhurt enemy's current hit points fall with the maximum, a wounded one's stay unless they would pass it. | open |
| 5 | Cast Congeal Time (spell 76) on an enemy and enter turn-based mode. In real time watch its attack pause. | Its action points per round fall (speed −50 %); in real time its attacks come slower; its walking speed does **not** change (the original does not read the speed stat for walking). | open |
| 6 | Cast Hasten (spell 77) on yourself and on a companion, and enter turn-based mode. | More action points per round (speed +100 %) and quicker attacks in real time; walking speed is unchanged. | open |
| 7 | Stack protective spells until the sheet's resistance would pass 95 %. | The protection stops at 95 % for the hero and ordinary NPCs. A monster (animal, undead, mechanical) is not capped in the original. | open |

Known differences (documented, not bugs):

- The character sheet's resistance lines (hero and NPC) do not show effect changes; damage does.
- A poison tick meets an NPC's resistance without its effects (an NPC's maximum hit points and fatigue follow its effect-changed strength, willpower and constitution since ARC-WORLD-1144).
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

The session now creates, saves and hosts a critter made from a prototype ([created critters](../architecture/object-directory.md#e1-c1-created-critters-arc-dev-359)). The viewer draws it (E1-c2) and a script makes it (E1-c3, below); the creature Summon is E1-e.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Let a script create a creature next to the hero (the snapping-twig tile of the start map creates eight, [ARC-DEV-371](#arc-dev-371--script-created-critters-e1-c3-2026-11-11)), and watch them. | It appears on the tile, stands there at first, and starts its ordinary behaviour after a short delay that grows with its distance from you (a created NPC gets the delayed first tick, RL16 and RL17). Hostile ones come at you; peaceful ones stand or wander as their prototype says. | open |
| 2 | Save with the creatures of a snapping twig alive, load. | It is still there, where it was last standing. | open |

Known differences (documented, not bugs):

- A created creature's inventory is generated at creation since ARC-DEV-386 (OM16, RK16, RK17). Its tile script does fire where it is made since E1-c3.
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

The viewer draws, animates and picks a critter the session created ([E1-c2](../architecture/object-directory.md#e1-c2-created-critters-in-the-viewer-arc-dev-370)). Since ARC-DEV-371 a script makes them (the snapping twig of the start map, below); the creature Summon is E1-e.
The smoke stage `created-critter` covers the draw, the pick, the walk frame and the removal on real data.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Step on a snapping-twig tile of the start map ([ARC-DEV-371](#arc-dev-371--script-created-critters-e1-c3-2026-11-11)) and watch the creatures in the first seconds. | It appears on the tile in its prototype's stand pose and facing, at the size and offset of a map creature of the same body. Compare its size and footing with a map creature of the same race. | open |
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
| 5 | Find a map item that carries the record 214 (a technology item on a map; see the placement census) and use it as the hero. | The original adds effect 334 80 times (its list has no bound). Since ARC-RES-127 our hero and NPC lists take the 80 entries (the list holds 1024). Note what the original does to the sheet and compare. | open |

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
| 8 | Cast Disintegrate (34) on a creature. | The creature takes 30000 electrical damage and dies; the original sets don't-draw on it. Ours writes the flag (and the paralysed bit); since ARC-MAGIC-045 the viewer draws no body for it either (the eye candy stays), and the AI and the spell target rules treat it as gone. | open |

Known differences (reading only, no game step):

- Water walking (spells 13, 28): the flag and its object flag are written; the original's clear then tests the tile under the critter and may run a damage step (F0, MA-8). Ours does neither.
- Mind control, charm, undead, fleeing and blinded (MA-4) and the locks (MA-6) are still not ready and show "not ready" in the spell line.
- The viewer's draw of translucent, shrunk, hidden and seen-invisible critters is ARC-MAGIC-045 (the section at the end of this file).

## ARC-DEV-366 — Tech books give expertise equal to intelligence (PR #980, 2026-11-11)

The rules and what is built are in [tech books](../world/inventory-runtime.md#tech-books-arc-dev-366) and [ledger XA9, XA11](../research/ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114).
Where the eight books lie in the world is not read yet ([census](../research/technology-evidence.md)), so the first step is to find where one lies in the original; in ours it works wherever the viewer lets the hero pick up, loot or buy a book. Known differences, not to be re-reported: taking a book out of the pack by a drop, a sale or a gift is
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
| 3 | Cast Flesh to Stone (48) on a creature, then Disperse Magick (41) on it. | The creature is no longer held (it acts again). The original clears the stoned spell flag and then the stoned object flag (0x43D280): the creature stops looking like stone and acts again. Ours clears both flags (ARC-MAGIC-039; MA-3 wrongly left the object flag set). Compare how the creature looks after the Dispel in both games and whether it acts at once. Record what you see. | open |
| 4 | Cast Disperse Magick (41) on a creature that a companion or a creature cast a maintained spell on, and on a creature a technology item's effect holds. | The spells of a magic record end; the effects of a technological record (a gadget, a schematic) are never ended by it. | open |

Known differences (reading only, no game step):

- Polymorph (49) and a script's permanent polymorph (critter flags 2 bit 0x400000) are undone by the original's Dispel with the art restored; ours has neither (MA-8). Since ARC-MAGIC-040 the Trait component keeps the monster body of Polymorph and the body spells in the session and tells the host, and since ARC-MAGIC-046 the viewer draws a polymorphed NPC as its monster body and takes the hero's worn items off and chooses them again at the END; the hero's own monster body is drawn since ARC-MAGIC-052 (and since ARC-MAGIC-1219 with the weapon part of combat mode, the original's missing-art repair for the air elemental and palette 0 for the elemental bodies' palette 1). Since ARC-MAGIC-047 the elemental bodies write the critter flags 2 and blit flags (MG10: assigned in Original mode, added in Steamwoven mode) and Dispel undoes a perma-polymorph in the session (the redraw of a map NPC whose data body is the polymorph is not built).
- A portal or container held by Magelock (75) loses the hold in the original; ours does the same since MA-6 ([steps below](#arc-magic-037--locks-and-holds-unlocking-cantrip-and-magelock-ma-6-2026-11-11)).
- A summoned critter is destroyed by the original's Dispel; ours destroys one the session created, and no spell makes one before the creature summons (E1-e).

## ARC-MAGIC-037 — Locks and holds: Unlocking Cantrip and Magelock (MA-6, 2026-11-11)

The rules are in [Locks and holds](../world/spell-cast-host.md#locks-and-holds-on-portals-and-containers-arc-magic-037), the executable reading in [LM1–LM6](../research/ledger/spell-runs.md#lock-flags-of-portals-and-containers-arc-magic-037).
No viewer smoke stage casts at a door, so every step here is by hand. Compare in both games with the same hero (a mage who knows Unlocking Cantrip of the Conveyance college and Magelock of the Temporal college), the same door and the same chest; use the hero's own "locked" door of a house and a locked chest, and a door with a high lock level for step 3 (pick it with lockpicks first to see how hard it is).

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Unlocking Cantrip (1) on a locked door with a low lock level (below 20), then open it. Close it, wait more than one game hour (rest or let the clock run), and try the door again. | The door unlocks and opens. **It is still unlocked after the hour**: the spell writes the lock field itself and schedules no relock (a key or lockpicks relock an hour later). Ours does the same; if the original relocked it after an hour, note it here. | open |
| 2 | Cast Unlocking Cantrip (1) on a locked chest with a low lock level and open it; then do the same with the lockpicks on a second locked chest and wait an hour. | The spell opens the chest and the chest stays unlocked for good; the lockpicked chest locks again after the hour (and, as before, a spilled or looted chest keeps its contents). Ours matches. | open |
| 3 | Cast Unlocking Cantrip (1) with a hero of low magical aptitude on a door or chest whose lock level is above 19 and above the hero's aptitude (the character sheet's Magick aptitude line; the lock level is the lock's difficulty shown by the pick-locks check). Then raise the aptitude (an aptitude-giving item or a higher level) to at least the lock level and cast again. | The first cast fails: the lock stays and the "resisted" feedback (the resisted effect, spell.mes 643 for the player) plays on the door. At aptitude not below the level the lock opens. A lock of level 19 or lower opens at any aptitude. Ours matches (the "resists" line and message 643). | open |
| 4 | Stand near a guard or the owner of the door (an NPC who watches it) and cast Unlocking Cantrip on it. | The original calls the object's witnesses (0x4AEE50) after the cast, whether the lock opened or not: NPCs within 10 tiles that guard the object (or are guards) and see or hear the caster react, an unlock making them hostile. A door or chest with no owner alerts nobody. **Ours does nothing yet** (the guards' reaction to a lock crime is not built; the cast log only has a line). Record what the original's guards do. | open |
| 5 | Cast Magelock (75) on an unlocked, closed door, then try to open it. Cast it on an open door and try to close it. | The held door does not open (the locked sound plays, the hover line shows the held text) and a held open door cannot be closed either. When the spell ends (its duration or maintenance), the door works again. Ours matches. | open |
| 6 | Cast Magelock on a door, then Disperse Magick (41) on the same door. Then cast Magelock on a door twice if the game lets you (two casters, or the spell again on a door it already holds). | Disperse Magick clears the hold at once. The area of Magelock excludes a door that is already held, so the second cast is refused at the target. If two holds stack (two casters), the first end clears the bit although the other spell goes on. Ours does the same; record whether the original allows the second target. | open |

Known differences (reading only, no game step):

- The guard alert's witnesses and the screen refresh after an unlock are not built; the stone-to-flesh pair of Flesh to Stone (48) on a portal that holds the stoned spell flag is not built either (no installed record makes a stone portal).
- A container's magically held bit has no writer but Dispel (no installed record writes it).

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

## ARC-UI-134 — The hero's lens on the world map shows the game around the hero (PR pending, 2026-11-11)

Read in the original executable (ledger [WM141](../research/ledger/ui-audio.md), WM140): the square in the window's top left is the same view-window lens as the logbook's, the inventory's
and the barter window's. While the map is open it shows the game around the hero, scaled to half size (178 game pixels become the 89-pixel square), with the ring art 198 over it; the open
centres the camera on the hero first. While a journey runs the square is black under the ring art, and the live view returns when the journey ends. Both rule modes behave the same; with the
HD interface switch on the frame, the ring and the buttons are the HD art and the lens is the same picture. Known differences, not to be re-reported: the copy reads what the layers under the
window drew, floaters included, where the original copies the game view alone; whether the HD frame's square cutout against the brass ring looks right is reported separately (ARC-UI-126).

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Stand somewhere with scenery and creatures around the hero and open the world map (`W` or the bar's Map button). | The top-left square shows the game around the hero at half size, the hero in the middle of it (the middle is slightly below the centre of the view), under the brass ring; the map itself is unchanged. | open |
| 2 | Move the hero's neighbours (wait a few turns, or stand beside a walking creature or a flame), then open the map and look at the lens for a moment. | The lens is live: animations in it keep running while the map is open. | open |
| 3 | Open the map twice from different places (the second time after walking a few tiles). | The lens always centres on the hero; the game view under it was centred on the hero by the open. | open |
| 4 | Set a waypoint and press Travel. | The square turns black under the ring at once and stays black for the whole journey. | open |
| 5 | End a journey (arrival, or the right button or Delete during it). | After a right button or Delete the live view is back in the lens; on arrival the map closes. | open |
| 6 | Open the map in a town-map sector and switch kinds (zoom, whole map, `Z`), and use the note quill (`Q`). | The lens stays live in every kind and with the quill; only a journey blackens it. | open |
| 7 | Click the lens, in the middle and in the corner. | The map closes (ARC-UI-133); the lens picture does not change that. | open |
| 8 | With the HD interface switch on, repeat steps 1 and 4. | The same lens in the HD frame: the ring over the square; the corners of the square show wood, not a hole. If a corner still shows the world through, report it. | open |

## ARC-UI-136 — One brass ring around the hero's lens in HD (PR pending, 2026-11-11)

With the HD interface switch on, the lens on the world map showed a doubled brass edge at the ring's top and upper right: the ring of the window frame (outside the lens square) and the ring of the cover art 198 (inside it) were
painted separately and did not coincide. The ring of the window frame `mapmain` is rebuilt from the cover's ring ([HD interface art](../rendering/hd-interface-art.md#world-map-window-arc-ui-126)); the Legacy art is untouched. The repair is a local pack change
(`lens_ring.py`, then `hd_pack.py apply`, backup and rollback), and it is installed in the pack the game reads (manifest `696edbd2…`, backup `ARC-UI-136-v2-cde8a57839e6`, `hd_pack.py rollback` undoes it). The cause and the method are in the same section; no frame is in git. Known difference, not to be re-reported: the cover's corner wood and the
window frame's wood have a slightly different grain, which leaves a faint square patch at the lens corners (not part of this repair).

| # | Step | Expected | Status |
| --- | --- | --- | --- |
| 1 | In Legacy (HD interface off) open the world map (`W`) and look closely at the ring around the top-left lens, at the top and the right side. | The original: one clean brass ring, no second edge. | open |
| 2 | With the HD interface on, open the world map and look at the same ring, top and right side, with the lens live and again during a journey (black lens). | One continuous brass ring like the original, no doubled edge at the top or at the right; the ring is the same width all around; no seam where the ring meets the square's border. | open |
| 3 | Open the character sheet and the logbook in HD and look at their lens ring. | Unchanged (the cover art `pcwincvr` is not part of this repair). | open |
| 4 | Compare the HD ring with the Legacy ring for colour. | The rebuilt top and side arcs are the same warm brass as the arcs that were not rebuilt; report a visibly paler or blurrier arc. | open |

## ARC-DEV-371 — Script-created critters (E1-c3, 2026-11-11)

A script's create object (action 52) makes a critter when the prototype is a critter's ([E1-c3](../architecture/object-directory.md#e1-c3-script-created-critters-arc-dev-371)). The installed case on the
start map is the "snapping twig" tile script (1629, 130 tiles): it creates eight critters of one prototype next to the hero. Compare it in both games with the same hero on the same tile.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Walk onto a snapping-twig tile of the start map (130 tiles in the wilderness; one cluster is around tile (50851, 15844), far from the Crash Site; the smoke log lists the nearest five to the hero). | Eight creatures of one kind appear within three tiles of you, all at once, each on a tile of its own or sharing one when the search found no free tile. They stand a moment and then act as that kind does (hostile ones come at you). | open |
| 2 | Walk away from them until they are far (a window move), then come back. | In the original an encounter creature that is left without a next AI tick outside a fight is destroyed: those that were left behind are gone, not standing where you left them. Compare how many remain. | open |
| 3 | Step onto the same twig tile again, and onto the next twig tile. | Each step creates eight more; creatures made earlier are not removed by it. | open |
| 4 | Kill one of them and look at what it leaves. | The snapping twig's creatures have no inventory source, so they leave nothing in the original too ([ARC-DEV-386](#arc-dev-386--created-critter-inventory-e1-c4-2026-11-11) gives a created creature its goods when its prototype has a source). Note what the original's creature drops. | open |
| 5 | Stand a created creature on a tile that has a tile script (a trigger or a door-warning tile) if you can lure one. | It runs the tile script as any creature that walks onto the tile does. | open |
| 6 | Save with the creatures alive and load. | They are where they last stood, and still count as encounter creatures. | open |

Known differences (documented, not bugs):

- The tile script of the tile a created creature is made on runs after the whole script has run, not inside the creating action, so a script that depends on it between two creations sees it one frame late.
- A created creature's inventory is generated by ARC-DEV-386 (restock at creation, then the best-items wield); the twig's creatures have none to generate.
- A script's vicinity lists (the "critters near" of a script) do not list created creatures yet.
- The twig tile's own text ("snapping twig", if the original prints one) is the script's, printed as for any tile script.

## ARC-DEV-386 — Created critter inventory (E1-c4, 2026-11-11)

The creature a script creates gets its goods the way the original's creation finish gives them ([E1-c4](../architecture/object-directory.md#e1-c4-created-critter-inventory-arc-dev-386); the restock body when the prototype has an inventory source, then the best-items wield). The twig's creatures (start map, [ARC-DEV-371](#arc-dev-371--script-created-critters-e1-c3-2026-11-11)) have no source; the smoke log line `ARC-DEV-386 script-critters stocked prototype=... source=...` names one critter prototype that has one, which the stage makes next to the hero.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Make a creature whose prototype has an inventory source (a script that creates a guard, a bandit or a merchant-type creature) and kill it. | Its body holds goods at once: coins and the items of its inventory set, and the best of them worn (it is dressed as a map creature of the same kind is). Compare the kind of items, the coins and what it wears with a map creature of the same prototype. | open |
| 2 | Kill a created creature that has goods and open its body. | The loot window lists its goods; taking them works as on a map creature's body. | open |
| 3 | Make the same creature twice (two steps on the same tile, or two scripts). | Each gets its own set (the draws differ), not a copy of one list. | open |
| 4 | Make a creature, walk away until its map is left, come back. | An encounter creature that is left behind is gone with its goods (nothing of it is left to loot). | open |
| 5 | Save the game a moment after a creature was made and load. | The creature keeps the goods it had when it was saved; the one made in the same instant as the save may come back empty (documented below). | open |

Known differences (documented, not bugs):

- The goods are generated one frame after the creation, not inside the creating action: a script that reads the new creature's inventory in the same run sees it empty. A save written in that frame, or a creation in the first seconds after the module opens (the barter data is still loading), keeps the creature empty.
- A creature whose prototype has no inventory source gets nothing, as in the original (it is not cleared or dressed either).
- The prototype's own list of inventory entries is not read, and the holder has no script points (a transfer script of the prototype is not checked).
- A created creature has no conversation or barter (no dialog identity yet).


## ARC-MAGIC-036 — Spatial Distortion moves the caster along the route, at most eight tiles (PR pending, 2026-11-11)

Read in the original executable (ledger [MV1–MV3](../research/ledger/spell-runs.md#movement-component-and-the-immediate-path-relocation-arc-magic-036)): the Movement component of Spatial Distortion (spell 3) is no teleport. It asks the same
path request the item drop uses and walks **at most eight tiles** of the route, running the tile script of every tile it walks, then puts the caster on the last tile. Compare in both games with a hero who knows the spell
(Conveyance college, rank 4), on any open ground such as the Crash Site. Both rule modes behave the same. Known differences, not to be re-reported: the view is not re-centred beyond the camera following the hero. Teleportation (spell 4), the random tile, the area portal and the order of the tile scripts (before the placement) were built afterwards: see ARC-MAGIC-038.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast spell 3 at a clear tile 3 tiles away in the open. | The caster stands on the target tile at once; a cast animation or flash plays on the caster. | open |
| 2 | Cast spell 3 at a clear tile 15 tiles away in a straight line. | The caster ends on the eighth tile of the way, not on the target (the original cuts the route at eight). | open |
| 3 | Cast it at a tile behind a wall or across a closed door where the way round is long. | Either the caster ends up to eight tiles along the walking route, or, when there is no route, nothing moves and the fatigue is still paid. | open |
| 4 | Start a click-walk far away, then cast spell 3 at a near tile while the hero walks. | The walk stops with the move; the hero stands on the target and does not walk on. | open |
| 5 | Cast it so that the route crosses or ends on a tile with a script (a trap-like tile, a door sill with a script). | The tile's script runs; the last tile's script runs twice (watch for a message that appears twice). | open |
| 6 | Cast it at a tile with a creature on it. | `Tgt_Tile_Empty` refuses the tile: the cast does not pick it. | open |

## ARC-MAGIC-038 — Teleportation opens the world map; the teleport ban areas (PR pending, 2026-11-11)

Read in the original executable (ledger [TP1–TP5](../research/ledger/spell-runs.md#teleport-types-and-the-anti-teleport-test-arc-magic-038), contract [teleport types](../world/spell-cast-host.md#teleport-types-and-the-anti-teleport-test-arc-magic-038)): in a single-player game Teleportation (spell 4,
Conveyance college) moves nothing by itself. It opens the **world map in a teleport mode**; you click a known area, and the hero lands there. Places where teleporting is refused come from the data file `Rules\AntiTeleport.mes` (2 round regions and 5 whole maps in the installed game). Both rule modes behave the same.
Use a hero who knows Teleportation, with enough fatigue (the spell's fatigue cost is charged only when an area is picked). Known differences, not to be re-reported: in the teleport mode the town map, the compass and the waypoint journey are not offered; the refusal message (the original's message 640) is shown as an
English label, not the game's text; the view is not re-centred; an NPC cast of the area portal (technology 218) or of a random move does not carry the creature; a multiplayer host is not built.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Teleportation in the open (outside any ban area). | The world map opens at once in teleport mode (the town map is not shown). No fatigue is taken yet. | open |
| 2 | Click an area you have discovered, 320 tiles or closer to the click point. | The hero lands on that area on the main map, the map window closes, and the spell's fatigue is taken now (a hero too tired to pay still lands). Repeat from a town or dungeon map: the teleport map opens there too and the click picks the area. | open |
| 3 | Click open sea or a place with no discovered area within reach. | Nothing happens; the map stays open and nothing is paid. | open |
| 4 | Close the map (Escape or its close button) without picking. | Nothing is paid and the hero stays where it was. | open |
| 5 | Cast it in a map the file disables (five maps in the installed data), or inside one of the two regions on the main map. | The cast is refused with a message at once and the world map does not open; no fatigue is taken. | open |
| 6 | Open the teleport map and pick an area that lies inside one of the ban regions. | The pick is refused with a message and the map stays open; nothing is paid. Pick another area to leave. | open |
| 7 | Use the technology device that teleports to the area (record 218) on the main map, and in a map that is not the main one. | On the main map the hero is sent to the area's location with a blank screen and no wait orders; elsewhere to the main map at the location of the area that map belongs to. | open |
| 8 | Walk with Spatial Distortion (spell 3) over a tile that has a script, starting from a tile next to it. | The script runs **before** the caster is put on the last tile: a message from it names the caster's old tile; the last tile's script runs once more afterwards. | open |


## ARC-RES-121 — Function audit wave 3, groups G and I (2026-11-11)

The audit changes no behaviour. It first named two places where the original behaves in a way our code does not reproduce and the export showed no caller for ([ledger GI-5](../research/ledger/census-triage-gi.md#findings-of-wave-3)). Task T5 (ARC-RES-123) then searched the whole executable bytes and found no reference to either, so both are dead code as far as a static search shows: the typed-order garbling 0x4C3120 is not run (an order should arrive whole) and the bar hide toggle 0x54ACB0 is not what F10 runs. One confirmation of the live F10 path remains.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Start the original game with `-fullscreen` and press F10 on a normal map (without that start option the toggle does nothing). | The interface hides or shows through the interface hide toggle 0x5578E0 (the main loop key handler's F10 branch; [UI ledger](../research/ledger/ui-audio.md) PA4). The older bar toggle 0x54ACB0 has no caller anywhere, so it is not what happens. Ours only reserves the key. | open |



## ARC-DEV-387 — The start map loads only when a new game needs it (O1 of the performance diagnosis, 2026-11-11)

The start map is no longer loaded when the game starts with the menu ([early menu](../rendering/terrain-viewer.md#main-menu-arc-ui-011)). It begins to load when you
enter the New Game window (the pre-made heroes or New Character); Last Save and a save chosen in the Load Game window load the map of the save itself, once. The
original loads the map after your choice, not before, so compare the order and the waiting. Compare on a start with the menu (`./arcanum area`, or a packaged
build); the first build for this comparison is any build from `main` that contains this task. Have at least one save, ideally made on a map other than the start map
(for example inside a town) and one made on the start map.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Start the game, open Single Player and Options, change a setting, go back; do not press New Game. Watch the machine (Activity Monitor) or the log line `ARC-DEV-387 start map load begins`. | The menu works at once and stays light: no map is loaded behind it, as in the original, which has no map before a choice. The menu has no music in this build (known difference: it plays through the start map's session, a follow-up). | open |
| 2 | Press Last Save with a save made in a town. | The menu stays and says "loading the save…" and takes no other input; after the load the game is on the town map at the saved place with the saved hero. The start map is not shown at any point. The wait is one map load, not two. | open |
| 3 | The same with Load Game and a row of the list (the window opens at once, before any map). | The same as step 2, for the chosen row. A row of a save on the start map works too. | open |
| 4 | Press Last Save with no save at all. | The window stays and the menu says there is no save to continue from, with no wait and no map load (the original goes back to the window without a message; the wording is the project's). | open |
| 5 | Press New Game, then the pre-made browser at once (before the map is ready) and browse; press Start. | The browser works at once. Start says the world is still loading and the game starts by itself when the map is ready (the one visible difference from the original). Escape takes it back. | open |
| 6 | Press New Game, then Back out to Single Player and choose Last Save while the start map is still loading. | The save's own map loads instead and the game starts on it; the start map's load is given up. The result is the same as step 2. | open |
| 7 | Press New Game, press New Character and go through the window, the editor and the shop. | The wait note first (the new character's window needs the map's start); then the windows work as before. | open |
| 8 | Choose a save whose file is damaged or from another game (copy one and break it). | The menu comes back with the reason; nothing else changes. | open |

Known differences (documented, not bugs):

- A new game still waits for the start map, which begins to load when the New Game window opens.
- The main thread is busy while the start map is published, for about a second after the load ends, so the menu may answer late then (O6 of the diagnosis).
- The menu's music was silent until the start map was loaded; ARC-UI-139 (below) plays it from the start without a scene.
- While a save's map loads the menu stays on screen with a note; the original shows its own loading screen.

## ARC-UI-139 — The main menu's music plays without a scene (follow-up of ARC-DEV-387, 2026-11-11)

The menu plays its music pair (the shop map's start sector, scheme 26 `arcanum.mp3`) through the scheme player of its own, without a game session or a scene, from the
moment it opens until a game starts or a save loads ([scheme player](../audio/scheme-player.md#viewer-integration)). Compare on a start with the menu (`./arcanum area`, or a
packaged build) with the music volume above 0, in both games.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Start the game and wait on the main menu without pressing anything for a minute or two. | The main theme plays from the first moment of the menu and loops without a gap or restart. | open |
| 2 | Open Single Player, Options (change the music volume on the Sound page), New Game and the pre-made browser, then go back. | The theme goes on through every window, and the volume follows the slider. | open |
| 3 | Press New Game, then New Character, so that the start map loads under the menu. | The theme does not stop and does not restart when the map has loaded (the original has no pause there either). | open |
| 4 | Press Last Save, or choose a row in Load Game, while the theme plays. | The theme plays until the save is loaded, then the map's own music takes over. | open |
| 5 | Start a new game with a pre-made hero. | The theme stops and the Crash Site's music (or its night ambience) starts; the theme does not come back. | open |
| 6 | Press Esc in play and open the in-play menu. | The game's own music goes on (the theme is only the main menu's). | open |

Known differences (documented, not bugs):

- The hour of the menu's player is fixed (12) because the menu has no game clock; the installed pair plays at every hour, so no difference is expected.
- A transition song in the menu's pair would play without the restore bookkeeping (the installed pair has none).

## ARC-WORLD-124 — Timed bombs: placing trap devices with a spell (E6-b, 2026-11-11)

The rules are in [placed trap devices](../world/trap-device-runtime.md). Give the hero the timed bomb (item 15154, record 176; or 15193, record 220) and compare in both games. The script
trap devices (15100, 15120) are the next slice (ARC-WORLD-125, [below](#arc-world-125--script-trap-devices-arming-traps-on-objects-and-tiles-e6-c-2026-11-11)).

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Use the bomb from the inventory (Use icon) and click an empty tile a few cells away. | The hero walks next to the tile, the bomb leaves the pack and a marker object stands on the tile; nothing else happens at once. | open |
| 2 | Wait 88 game seconds (about 11 s of play at the normal speed). | The marker disappears and the explosion goes off on the tile: fire and normal damage to everything within two tiles, a flash, no knockback. | open |
| 3 | Place the record 176 bomb (15154) with a few people awake within ten tiles that can see the hero, then wait. | They turn on the hero as soon as the bomb is placed (not when it goes off); people who cannot see or hear it, and companions, do not. | open |
| 4 | Use the bomb on a critter or on a scenery object instead of a tile. | The bomb is spent with no check of the target and there is no marker; the explosion goes off at that object's place after the same 88 s. | open |
| 5 | Save while the fuse runs, load, and wait. | The marker is still there and the bomb goes off at the same moment. | open |

Known differences (documented, not bugs):

- The original shows no message when a device is placed (its spotted mark is quiet); the viewer's status line names the outcome.
- The marker is a plain scenery in Steamwoven: it cannot be spotted, disarmed or triggered by a step (the original's marker has no script either).

## ARC-WORLD-125 — Script trap devices: arming traps on objects and tiles (E6-c, 2026-11-11)

The rules are in [placed trap devices](../world/trap-device-runtime.md#script-trap-devices-arc-world-125). Give the hero the spike trap (item 15100, script 30001) or item 15120 and compare in both games.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Use the device from the inventory (Use icon) and click a closed, unscripted chest or a door. | The hero walks next to it, the device leaves the pack, and the chest or door is now trapped (the trap is known to the hero). | open |
| 2 | Open that chest, or use that door. | The trap goes off on the hero as its script says. | open |
| 3 | Use the device on an NPC, a creature or a scenery object that is not a door or chest. | The device stays in the pack; the game shows its "cannot be set here" line (`mes\trap.mes` line 2). | open |
| 4 | Use the device on a chest or door that already holds a script (a trap or a lock script). | The same refusal, the device stays. | open |
| 5 | Use the device on an empty floor tile. | A trap marker (a hidden-trap sprite) stands on the tile and the device is spent. | open |
| 6 | Use a second device on the same tile. | The device stays and the game shows `mes\trap.mes` line 3. | open |
| 7 | Save and load with a trapped chest and a marker on the map. | Both are still there; the chest is still trapped. | open |

Known differences (documented, not bugs):

- The refusal lines appear in the viewer's status text, not in a message window.
- A marker does not go off when the hero steps on it yet, and it cannot be disarmed or spotted by a roll yet (a later slice): in the original it fires on the step. Check steps 5 and 6 only.

## ARC-UI-147 — The character editor of the new-character flow in the original's look (2026-11-11)

The editor is the in-game character sheet's original look between the menu's strips, with the opening shop's Accept and Back buttons ([viewer](../rendering/terrain-viewer.md#character-editor-in-creation-mode-arc-ui-034)). Compare in both games.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | New Game, New Character, give a name, Accept. | The editor opens as the character sheet (the wooden window with the portrait, the five points, the eight stats and the skills page) between the top and bottom strips, with the green arrow (Accept) and the red arrow (Back) in the bottom strip. | open |
| 2 | Press the + and − of a stat, of a skill, of a college's spell rank and of a tech degree; try a − before any + . | Points move as in the game's sheet; a − at the start values is refused (in the original with its text in the text bar, here in the note line at the top). | open |
| 3 | Click the portrait's round area. | Nothing happens: the editor does not close (in the game the same click closes the sheet). | open |
| 4 | Accept, then Back in the shop, then change a stat again. | The points spent before the shop can still be taken back. | open |

Known differences (documented, not bugs):

- The hover hints of the buttons and values are not shown in the editor (the original shows them in the game's text bar).

## ARC-UI-148 — Generated interface: the inventory window (S3, 2026-11-12)

The rules are in [viewer slice S3](../rendering/hd-interface-density.md#viewer-slice-s3-arc-ui-148). Set the HD interface line on the launcher screen to **Generated (experimental)**, start a game
and press `I`. Compare with the same window under **HD** and **Legacy**.

| # | Step | Expected | Status |
| --- | --- | --- | --- |
| 1 | Open the inventory in a 1280 × 800 window, then press `I` again. | The pack grid, the doll grid, the count bars and the name box are drawn by code in the same places as in HD; the grid cells, the slots and every click land where they did. | open |
| 2 | Drag the window's edge to make it bigger and smaller while the inventory is open. | The frame redraws at the new size (the previous picture stays until it is ready); the lines of the grid stay sharp and one unit wide at every size. | open |
| 3 | Pick up an item and drop it into a cell and into a wear slot. | Same behaviour as in HD and Legacy. | open |
| 4 | Look at the drop and use boxes. | Still the original's picture (the glow and the green or red corners show the state). | open |

Known differences (documented, not bugs):

- The curved flourishes at the corners of the original's boxes are not drawn in Generated: the boxes are plain rectangles with a brass rim.

## ARC-MAGIC-058 — Spell runs at the hero's change of map (2026-11-12)

The hero's change to another map (a jump point, the Crash Site cave, a map-mode exit) ends every spell run that names an object that does not travel with the hero (the hero, the followers that go along and what they hold); runs of the hero and of the followers that travel go on. Built from the executable (functions 0x45C720, 0x44F7A0, 0x44FA70, 0x44FC30; [rules](../world/spell-run-rules.md#runs-at-the-players-change-of-map-arc-magic-058-mt9b)). Compare in both games.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Cast a maintained spell on yourself (for example a protective spell with a slot in the top bar), walk through an exit to another map. | The spell and its slot are still there on the new map. | open |
| 2 | With a follower in the party, cast a maintained spell on the follower and change map with the follower coming along. | The spell on the follower goes on; its slot stays. | open |
| 3 | Cast a maintained spell on a creature that is not in the party (or on a follower you ordered to wait), then leave the map. | The spell ends as you leave: the slot is empty on the new map and the creature is without the effect when you come back. | open |
| 4 | Cast Wall of Fire (or another spell that makes scenery) and leave the map. | The run ends as you leave and its fire pieces are gone when you return. (A summoned creature that follows you travels and its run goes on; creature summons are not built yet.) | open |
| 5 | Cast a lock or hold spell on a door or chest of the map and leave. | The effect is over when you return (the run ended at the exit). | open |

Known differences (documented, not bugs):

- A run that travels keeps its next tick time; the original queues the tick again at the map entry (the time 0x459500 gives is not read).
- The change of map by the world map is not covered (the endings on rest and world-map travel are MT11's).

## ARC-MAGIC-059 — Spells end when a world-map journey starts (MT11, 2026-11-12)

The world-map start check (0x565140) ends the hero's maintained spells ([ledger UE6](../research/ledger/spell-runs.md#spell-run-endings-of-a-critter-arc-dev-301), [cast host](../world/spell-cast-host.md#the-world-map-travel-start-arc-magic-059)). Compare in both games with a hero who can cast a maintained spell (for example a protection or light spell, one that shows on the top strip).

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Cast a maintained spell on the hero, open the world map, plan a route and start the journey. | The spell's slot leaves the top strip as the journey starts and its effect is gone on arrival. | open |
| 2 | Plan a route of two or more waypoints and start it. | The spells end at the start; the journey goes on through every segment. | open |
| 3 | Start a journey while the hero is walking or has just clicked a target. | The walk stops and the journey starts. | open |
| 4 | Start a journey while the hero is in the middle of a cast (its casting animation). | The original refuses the start when the cast cannot be interrupted (a cast attempt of priority 4 or 5); Steamwoven shows "the hero's action cannot be interrupted" and keeps the route. | open |

Known differences (documented, not bugs):

- Resting (the rest option click, 0x57B710) also ends the sleeper's maintained spells in the original, but the rest window (ARC-WORLD-1160) does not end them yet (issue #1161), so only travel is wired.


## ARC-MAGIC-057 — The hero's spot trap roll (E6-a, spot half, 2026-11-12)

The rules are in [trap state](../world/trap-state-runtime.md) (the Spot item) and the [ledger SP1–SP2](../research/ledger/technology.md#spot-trap-roll-arc-magic-057). Use a hero with a low and one with a high Spot Trap skill (and Perception), on a map with a hidden floor trap (a trap plate) or a trapped chest or door, in both games.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Walk along a route that crosses a hidden floor trap with a low-skill hero. Repeat on a reloaded save a few times. | Sometimes the walk stops one tile before the trap with the "trap spotted" line and the trap is shown; other times the hero steps on it and it goes off. The chance rises with the skill and Perception. | open |
| 2 | The same with a high-skill hero (a master of Spot Trap if you have one). | The trap is spotted much more often; a master gets a second chance on every miss. | open |
| 3 | Open a trapped chest or use a trapped door that you have not spotted. | Either the use is stopped with the "trap spotted" line (the trap now shows as known) or the trap goes off as you open it. | open |
| 4 | Find a magical trap (a trap whose script is a spell) with a hero below Expert in Spot Trap. | It is never spotted; it goes off on the step or the use. From Expert on it can be spotted. | open |
| 5 | Cast Sense Hidden (spell 8), then repeat step 1. | The trap is spotted without a roll every time (the spell flag), and the hero's Spot Trap level should also be higher while the spell runs (effect 157): compare the sheet's skill value in both games. | open |

Known differences (documented, not bugs):

- The light at the trap is taken as full: an untrained hero in the dark is not penalised yet (the original adds up to 30 against an untrained hero).
- The item that adds a modifier to the roll (inventory location 1005, name 2866) is not modelled.
- Companions' own spot attempts are not made; disarming a trap is ARC-SKILLS-1156 (below).

## ARC-SKILLS-1156 — Disarming a trap from the hotkey bar (E6-a, disarm half, 2026-11-12)

The rules are in [trap state](../world/trap-state-runtime.md#disarm-arc-skills-1156) and the [ledger SP3, TT5, TT6](../research/ledger/technology.md#spot-trap-roll-arc-magic-057). Put the Disarm Traps skill button on the hotkey bar (the skill bar's entry), and use heroes with a low, an expert and a master Disarm Traps skill, on a map with a spotted trap (a trap plate you saw, or a trapped chest or door you spotted), in both games. Compare the odds over several reloads and the device.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Press the Disarm Traps entry on the hotkey bar, then click a trap you have not spotted. | Nothing is rolled: the trap is not a target (no known trap there) and the cursor is back to normal. | open |
| 2 | Press the entry, then click a spotted trap (the plate, a chest or a door) with a low-skill hero. Reload and repeat a few times. | Sometimes the trap is gone, sometimes nothing happens, sometimes (a critical failure) the trap goes off on you. The odds rise with the skill. | open |
| 3 | The same with a trap object that has a high difficulty (a strong trap plate) against one with a low difficulty. | The strong trap is disarmed less often at the same skill. A chest's or a door's trap has no such difficulty term. | open |
| 4 | As an Expert, disarm a trap and look at the pack after a critical success. | The trap's device item (a trap kit the trap's script names) lands in the pack, or on the ground at your feet when the pack cannot take it. Compare which items you get and how often. | open |
| 5 | As a Master, repeat step 2 many times and count successes against an Expert of the same stats. | A Master succeeds clearly more often (a failed roll gets a second chance). | open |
| 6 | In turn-based mode, press the entry and click a spotted trap with fewer than 4 action points left, then with enough. | With too few the subturn ends and nothing is rolled; otherwise 4 action points are spent. | open |
| 7 | Right-click while the cursor is armed. | The cursor is cancelled. | open |

Known differences (documented, not bugs):

- When a follower is better at disarming, the original lets the best follower act instead of the hero ("let me handle it", Ctrl skips it); Steamwoven always uses the hero.
- The original's skill-bar press builds the invocation at once, so Steamwoven does not walk the hero to the trap first; whether the original limits the distance by the difficulty is read from the shared check only.
- A trap you placed yourself with a script trap device is a marker object in Steamwoven and cannot be targeted yet.
- The skill pane's own Disarm Traps button is [#1113](https://github.com/Simarglok/arcanum-steamwoven/issues/1113).

## ARC-MAGIC-056 — Tempus Fugit speed (spell 79)

Rules: [contract](../world/character-stats-runtime.md#tempus-fugit-speed-arc-magic-056), ledger [TF1–TF3](../research/ledger/spell-runs.md#tempus-fugit-speed-arc-magic-056). Compare in both games with the same hero (any Temporal hero with Tempus Fugit) and a hostile or neutral creature near.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Tempus Fugit (spell 79) on the hero. Open the character sheet (speed line) and the inventory window. | The hero's speed is 10 higher while the spell is maintained, and back to the old value when it ends. | open |
| 2 | Cast it, then fight a creature in turn-based mode, and compare the action points of the hero and of the creature with and without the spell. | The hero has 10 more action points; every other creature has 10 fewer (at least the game's minimum of 5). | open |
| 3 | Cast it and fight in real time; compare the pause between attacks of the hero and of the creature with and without the spell. | The hero attacks faster, the creature slower. Walking speed does not change. | open |
| 4 | Have a companion with the hero when casting. | The companion is in the spell's area and gains the same +10 as the hero; a companion that joins after the cast does not (not built, XR7). | open |

Known differences (documented, not bugs):

- An NPC that casts Tempus Fugit (record 79 carries a flee priority for the AI) changes speeds even when the player does not hold the spell: the owner decided on 2026-11-11 that the original's player-only test is a bug of the game, fixed in both modes. In the original such a cast does nothing to anyone's speed.
- The follower join and leave flag writes (XR7) are not built: a companion that joins after the cast gains nothing, and one that leaves keeps the flag until the spell ends.

## ARC-MAGIC-060 — Item spells on the hero's hit, the cell weapon (PR #1184, 2026-11-12)

The hero's melee swings and shots now fire the combat events of the worn items (before, they did not in the game). The cell weapon of Electric degree 4 (record 147) is the one installed item that shows it; armour 8215 (record 87) answers a hit too. Compare in both games.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | With Electric degree 4 and power cells in the pack, assemble the degree-4 melee weapon, wield it and hit an NPC. | Each hit that lands takes cells from the pack (a technologist pays up to double) and adds an electrical shock to the weapon's own damage; a missed swing takes nothing. In the original the shock's own damage fires the weapon again until the NPC dies or the cells run out (MG46), here once per hit: only the first shock and its cells are compared. | open |
| 2 | Hit with no cells in the pack. | The weapon still hits for its own damage; no shock, no cells. | open |
| 3 | Kill an NPC with the weapon. | The killing hit pays the cells of its firing; nothing is expected to fire after the NPC is dead (the original's repeat per MG46 ends with the kill). | open |

Known differences (documented, not bugs):

- An NPC's own worn items fire when it attacks since ARC-MAGIC-1188 (below).
- A chain of item spells answers once per item (owner decision 2026-10-28, MG46), where the original repeats until the target dies.

## ARC-MAGIC-1188 — Item spells of an NPC's attack (2026-11-12)

Worn items of an NPC that attacks now fire their combat events, as the hero's do (the same rule in the original executable, 0x4B4390 serves any attacker). The one installed case you can see: armour 8215 (record 87) answers a hit when worn by an NPC or by you. Compare in both games.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Let an NPC that wears armour 8215 (or other armour with a hit spell) be hit by a hostile NPC (a companion fighting an enemy). | The wearer's armour spell starts on each hit that lands, as for you wearing it; a miss starts nothing. | open |
| 2 | Let an NPC that wields or wears a combat-event item (a weapon or armour with an "attacks"/"damages" spell) hit you or another NPC. | The item's spell starts on each hit that lands, aimed as the original's executor aims it; a missed swing starts nothing. | open |
| 3 | Watch a bow or firearms shot of an NPC that wears such an item. | The events come when the arrow strikes, not when it leaves the bow. | open |

Known differences (documented, not bugs):

- A critical self-hit of the attacker (a critical miss that hurts the NPC itself) starts none of these events; its life events are the caller's and not built for NPCs yet.
- The attacker's weapon that a thrown-item attack throws is not among its worn items when the events fire (the thrown item is the context weapon).

## ARC-MAGIC-045 — Shield hit eye candy and the draw of translucent, shrunk and hidden critters (2026-11-11)

The rules and what is built are in [the cast host](../world/spell-cast-host.md#hit-eye-candy-and-object-draw-flags-arc-magic-045) and [ledger HE1–HE4, OD1–OD6](../research/ledger/spell-runs.md#object-draw-flags-and-the-hit-eye-candy-arc-magic-045). Steps need a mage hero (or scrolls) and a creature to fight.

| # | Step | Expected (original) | Status |
| --- | --- | --- | --- |
| 1 | Cast Shield of Protection (30) on yourself, then let a creature hit you with a weapon or a spell that damages you. | Each hit that reaches you plays the shield's hit effect on you (a short flash of the spell's own animation). Misses do not. | open |
| 2 | Cast Invisibility (69) on yourself. | You are drawn half transparent (not gone). | open |
| 3 | Cast Invisibility (69) on a friendly companion or NPC. | Without detection the creature is not drawn at all (a creature of high perception, 20 or more, or a Detect Invisible effect shows it half transparent). | open |
| 4 | Cast the spell that shrinks a creature (the one with the shrunk flag; the player's spell list names it) on a creature. | It is drawn at half size, standing on its tile. | open |
| 5 | Cast Disintegrate (34) on a creature that stands near an eye candy (a burning or magic overlay). | The body is gone at once; any overlay of the spell still plays there. | open |

Known differences (documented, not bugs):

- The hero's own shrink is not drawn (the hero's draw has no hotspot to scale about); a shrunk hero is drawn full size.
- One prototype's hit plays a fixed extra eye candy in the original (HE3); ours does not.
- Magnetic inversion plays the fixed entry 0x449 when no run provides the flag; which installed record sets the flag by a non-spell path is not read, so step 1's variant for it is not listed.

## ARC-MAGIC-053 — Summon for creatures: Summon Undead and the other creature summons (E1-e, 2026-11-11)

The rules are in [spell cast host: summon for creatures](../world/spell-cast-host.md#summon-for-creatures-arc-magic-053); the original's reading is the
[ledger SUM5, SUM6 and SUM17–SUM22](../research/ledger/spell-runs.md#summon-component-and-the-objects-it-creates-arc-res-101). Give the hero the spells (a mage with Summoning: Summon Undead 57,
Succour Beast 53, Orcish Champion 71, Guardian Ogre 72, Familiar 74) and compare in both games. It closes the owner's report of 2026-11-08 17:19:23 (no summoning animation and no summoned NPC).

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Cast Summon Undead (57) at an empty tile a few cells away. | The summoning animation plays on the tile, and when it reaches its callback frame (about two thirds of a second) the creature appears; before that nothing stands there. | open |
| 2 | Look at who is summoned with a weak magick aptitude and, after raising it, with a strong one (thresholds 20, 60 and 85 of the aptitude). | The creature changes with the aptitude (list 5: four undead kinds, the strongest above 85). | open |
| 3 | Watch the new creature and open the party portraits. | It is the hero's follower at once (mind control), fights the hero's enemies and follows the hero. | open |
| 4 | Let the creature be killed. | The spell ends with it: its upkeep stops, and the body is removed. | open |
| 5 | Stop maintaining the spell (or wait for the fatigue to run out). | The animation plays backwards and the creature goes; it leaves the party. | open |
| 6 | Cast it on a tile with a tile script (a trap or a notice tile). | The script runs with the summoned creature as the one who stepped on it. | open |
| 7 | Cast Succour Beast (53) and Orcish Champion (71) as well. | Beasts by aptitude (list 4) and a fixed orc (prototype 28443): the same animation, reveal and follower. | open |
| 8 | Save while the creature stands, load, and wait. | The creature is still there and still the hero's follower (its flags, party place and run are saved). | open |

Known differences (documented, not bugs):

- The original starts a summoned creature whose art has a sneak pose in that pose (concealed, SUM19); Steamwoven starts it standing.
- The creature takes the hero's alignment as the original does, but of the moment the world first reads it: a loaded game reads the hero's then.
- The original's weighted lists overrun their table on a draw of 100 (MG9); Steamwoven fixes it in both modes (technical bug rule).
- Familiar (74): its own flags (`OSF_FAMILIAR`, `ONF_FAMILIAR`) and its hiding with the party on the world map are MA-1's and the world map's.

## ARC-WORLD-1158 — Body decay of dead creatures (E2-b, 2026-11-12)

The rules are in the [ledger BD1–BD7](../research/ledger/npc-ai.md#body-decay-built-arc-world-1158) (the executable's PO10). Compare in both games with a creature you kill and then leave lying: wait or sleep one game day (the game bar's wait, key S) near the body.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Kill a creature that carries an item or two (a map NPC, then a creature a generator made) and stay on the map. Pass 23 hours, look at the body; then pass another 2 hours. | The body lies there the first 23 hours and is gone a game day after the death (the Decay Dead Bodies event). | open |
| 2 | Look at the spot after the body is gone. | The creature's items lie on its tile (in a junk pile when two or more) and disappear two game days later as any dropped item; an item the data marks "not droppable" is gone with the body. | open |
| 3 | Kill a creature, leave the map for more than a day, come back. | The body is gone on return (the event runs late once its map is the shown one). | open |
| 4 | Kill a creature of a generator and wait for its body to go. | The generator does not make a replacement because of the decay: only a generator whose live count the death released ticks again (PO7, PO11); the made-so-far count is never given back. | open |

Known differences (documented, not bugs):

- The hero's own death queues no decay event here (the game is over); the original queues one for a PC too.
- The items of a dead follower that died away from its home map stay with the destroyed critter (a seed other than the body's map is not read).
- Whether any installed creature carries the "no decay" critter flag (0x40 of critter flags 2) is read from the data when each creature is built: such a body is never removed, as in the original.

## ARC-INPUT-1123 — Lockpicks need 4 action points to start (2026-11-12)

The rule is in [interface modes](../architecture/interface-modes.md#action-points-at-the-start-of-a-target-mode-arc-input-1123). Compare it in both games in a turn-based fight (turn-based on, combat mode, a fight with a creature near) with a hero that carries lockpicks.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | On your own turn spend all action points (walk until they are gone, while it is still your turn), then pick the lockpicks from the inventory. With 1 to 3 points left the original lets a hero with normal fatigue through (only fatigue of 1 or less refuses). | The inventory closes and no lock cursor opens; nothing is said. (Steamwoven shows "pick a lock: not enough action points" on its status line, a known difference.) | open |
| 2 | On your own turn with 4 or more points left, pick the lockpicks. | The inventory closes and the lock cursor opens. | open |
| 3 | Outside a turn-based fight, pick the lockpicks. | The lock cursor opens whatever the points. | open |

## ARC-WORLD-1132 — First gold pile (2026-11-12)

The rule is in [First gold pile](../world/inventory-runtime.md). Compare in both games with a hero whose inventory grid is full and who holds no coin.

| # | Step | Expected (from the original executable) | Status |
| --- | --- | --- | --- |
| 1 | Fill the whole inventory grid, hold no gold, then receive coins (a dialog reward, selling an item, a successful steal). | The coins do not reach the hero: a pile of coins lies on the hero's tile and the call reports success. | open |
| 2 | Pick the pile up with the grid still full. | The pickup is refused and the pile stays on the ground; with a free place the coins join the hero's gold. | open |

## ARC-SCRIPT-1163 — NPC state script actions (E5-a, 2026-11-12)

Script actions that change an NPC's own state now run from use, tile, sector, heartbeat and dying scripts ([ledger NS1–NS8](../research/ledger/scripts.md#npc-state-commands-arc-script-1163-e5-a),
[dispatcher](../scripting/scr-dispatcher-runtime.md#npc-state-commands-arc-script-1163-set-s36)). No installed map is named below because the check that lists the admitted scripts is by script number, not by map; use a
companion, a shopkeeper and a guard in any town where a quest script touches them, and compare the same quest step in both games.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Make a script release a companion (quest steps that end a companion's service: critter disband). | The companion stops following at once, leaves the party list and keeps standing where it is; it does not say anything. | open |
| 2 | Reach a quest step that sets or adjusts an NPC's reaction toward the hero, then talk to it. | Its reaction category in the conversation (and the barter prices) match the original after the step; an NPC the hero mind-controls does not change. | open |
| 3 | Reach a quest step that moves an NPC's day or night standpoint, then wait until the matching time of day (and, for the two extended standpoints, change map). | After the step the NPC goes to the new standpoint at the next day or night change, not the old one. | open |
| 4 | Reach a quest step that changes the script of an NPC or scenery (change script), then use or talk to it again. | The object runs the new script from then on, with its local flags and counters kept. | open |

Known differences (documented, not bugs):

- Change art number (a door, scenery or creature changing its picture from a script) is not built: scripts that use it are still refused, so those quest steps do nothing in Steamwoven.
- A script in a dialog (a talk) that uses one of these actions is still refused; only use, tile, sector, heartbeat and dying scripts run them.
- A reaction or standpoint change acts for NPCs of the map on screen only.

## ARC-SCRIPT-1164 — world-object script actions (E5-b, 2026-11-12)

Script commands that act on a door, chest, sector or monster generator now run from use, tile, sector, heartbeat and dying scripts ([ledger WO1–WO13](../research/ledger/scripts.md#world-object-commands-arc-script-1164-e5-b),
[dispatcher](../scripting/scr-dispatcher-runtime.md#world-object-commands-arc-script-1164-set-s37)). No installed map is named below because the check that lists the admitted scripts is by script number, not by map; compare the same
quest step in both games.

| # | Step | Expected (from the original executable and data) | Status |
| --- | --- | --- | --- |
| 1 | Reach a quest step that locks or unlocks a door or chest from a script (set lock state), then try to open it. | The object is locked or unlocked as the step says; the lock picks and keys behave as for any locked object. | open |
| 2 | Reach a quest step that opens or closes a door or chest from a script (toggle open or closed). | A door swings open or shut at once; a chest changes to its next or previous picture frame; a script that tests the open state then branches on it. | open |
| 3 | Reach a quest step that gives the hero or an NPC an item (create item). | The item appears in the holder's inventory (or, with no room, is not taken). | open |
| 4 | Reach a quest step that blocks a sector, then open the world map and route into it. | The route does not enter the blocked sector, and a blocked destination is refused. | open |

Known differences (documented, not bugs):

- Transfer item, set written start, destroy item named and toggle item inventory display are not applied by any host yet: scripts that use them are still refused.
- The world map's refusal when the hero's own sector is blocked, and the dimmed picture of a blocked sector, are not built.
- A script in a dialog (a talk) that uses one of these commands is still refused.
