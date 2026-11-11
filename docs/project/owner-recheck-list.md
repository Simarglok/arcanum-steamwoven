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
