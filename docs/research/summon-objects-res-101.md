# Summon: what Fireflash, Wall of Fire and Summon Undead create

Status: **Research**, work item ARC-RES-101 (2026-11-10), slice E1-r of the [missing-systems plan](../project/missing-systems-plan.md#e1-summon-and-objects-created-at-run-time-priority).
Docs-only: no code changes and no checks run (the census below ran locally, not as a repository check). It answers the plan's questions for
B47 of the [2026-11-10 triage](open-bugs.md): what the Summon spell component
(type 18, `magictech_effect_summon` 0x450C40, ProcFunc 0x452830) creates for Fireflash (spell 22), Wall of Fire (spell 21) and Summon Undead (spell 57),
how those objects live and die, and which installed spells summon something other than a critter. It ends in a hand-off for the slices that
build it: E1-a (created scenery and eye-candy objects) and E1-b (the Summon component for objects).

Requirements (reference only; the coverage rows get one added sentence each):
[FR-MAGIC-008](../requirements/combat-magic-tech.md#fr-magic-008) (a summon creates a linked effect with a limited life) and
[FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007) (spell effects: components).

Earlier results reused, not re-read: the summon's critter half ([MG8, MG9](ledger/magic.md), [SC7, SC12](ledger/magic.md)), the target lists
([TA1, TA9, TA10, TA12, TA15](ledger/spell-runs.md#spell-target-areas-arc-magic-007)), run processing ([RP4, RP12](ledger/spell-runs.md#spell-run-processing-arc-magic-010)),
the eye-candy component ([EC9 to EC14](ledger/spell-runs.md#spell-eye-candy-arc-magic-017)), the Destroy component and the object-gone endings
([DS1 to DS4, UE1 to UE5](ledger/spell-runs.md#spell-destroy-component-arc-dev-259)), the object model ([OM15 to OM17, OM25](../architecture/original-object-model.md)) and
the scenery goals and burn ([GT75 to GT77, GT94 to GT98](../architecture/goal-table.md)). The new rows are
[SUM1 to SUM16](ledger/spell-runs.md#summon-component-and-the-objects-it-creates-arc-res-101); this note tells the story and hands it off.

## Short answers

| Question | Answer |
| --- | --- |
| What is Fireflash's explosion? | A **scenery object** (type 3) of basic prototype 4050 (file 4070), made at the strike tile **only if that tile is empty** (no blocking terrain, no blocking non-item object, a standing critter included). Its own art is a 1 × 1 pixel frame; the visible flash is the spell's eye-candy entry 2 added to it. It lives until that animation's callback frame (about 0.75 s) and is then destroyed by the record's `Callback` Destroy. It has no damaging flag: it only shows. The damage is the spell's own Damage component. |
| What is Wall of Fire? | **One** Summon component applied to **five** list entries: five scenery objects of basic prototype 4031 (file 4051), in a line of five tiles (the centre and two each way, the line's direction from the caster toward the target), none on terrain that blocks or on a blocking wall, portal, scenery or trap. Each has its own 16-frame art. The piece carries the scenery flag `OSCF_IS_FIRE`, so its main run is goal 77: fire damage to the critters and items on its tile (3 fire per burn step outside combat; 6 fire once per round in a turn-based round), with **the caster as the burn's source** (the caster and the NPCs whose leader chain reaches a PC caster are spared). A restart of the piece's main run (a sector reload or fold, the run-table sweep) loses that source, and the piece then burns its caster too. No timer: the pieces stand while the caster maintains the spell and go together, at once, when the run ends. |
| What does Summon Undead's second line do? | It turns the spell flag `OSF_MIND_CONTROLLED` (field 20, bit 27) **on, on the summoned creature**. Spell 57's first line is the Summon: an NPC from list 5, picked by the caster's magick aptitude (thresholds 20, 60, 85, then the rest). Spells 53 and 74 are the same pattern. Spell 56 summons nothing. |
| Which installed spells summon something that is not a critter? | 25 of the 42 Summon components make a **scenery** from seven prototypes (spells 11, 17, 21, 22, 26, 27, 32 and 18 technology-range records). **No** installed Summon makes an item, an eye-candy object, a container or any other type. The other 17 make critters (13 by prototype, 4 by list). |

## Method and inputs

- The owner's unchanged GOG install: `Version.txt` reads `VERSION:1.0.7.4`; `Arcanum.exe` SHA-256
  `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52`, re-hashed for this reading.
- Static reading with Xcode's `llvm-objdump` (read-only, into the scratchpad; nothing kept), following the
  [original executable check](original-executable-check.md), by one reader (F1). The functions are listed in the
  [ledger section](ledger/spell-runs.md#summon-component-and-the-objects-it-creates-arc-res-101); every direct branch of them stays inside `.text`.
  No bytes, listings or decompiled code are in git.
- The census used an opt-in scratch run (`DAT_ROOT`, not committed) of the repository's own spell reader (like-original mode), the spell eye-candy table, the file
  names and fields of the installed prototype files and the ART headers. Counts, spell numbers and numbers only; no art, no text.
- A fresh agent re-reads the exact diff and the addresses it names before the PR (method step 6 for the claims it can re-check); its findings are recorded
  in the pull request. Nothing here is F2 until then.

## How the original summons

The ProcFunc builds one request per list entry (SUM1): the summoner is the run's parent, the location the entry's tile, the prototype the component's own
`Proto:` or a list pick (SUM6). The summon effect creates the object through the ordinary creation path (SUM2; OM15: the object is marked dynamic, OM16),
stores its handle through the request's result pointer, hands it to the context's "summoned" slot (which the records' `Apply_AoE: Tgt_Summoned` lines re-aim to; the slot is reset once per action, not per entry, so an entry whose Summon is refused re-aims to the previous entry's object), sets the spell flag `OSF_SUMMONED`
(0x4000) and then branches by type (SUM3):

- **Any type but an NPC** (SUM4): one more step, 0x4372B0, which writes the summoner into the source slot of every goal run the new object owns.
  Nothing else: no art change, no palette, no `OF_DONTDRAW`. This corrects OM25's inference that the write went into the spell runs.
- **An NPC** (SUM5): art turned toward the summoner with the component's palette, `OF_DONTDRAW` set, `OCF2_NO_PICKPOCKET` set, experience worth and
  pool cleared, then the faction rules (`Clear_Faction:` clears the faction; otherwise the parent's alignment is copied, and an NPC summoner's faction too).

Being an ordinary object in its sector's list, a created scenery is drawn by the ordinary object draw with its overlays (SO1, EC12). The run keeps every summoned object in a list (SUM7). **Nothing in the run's end destroys them**; they leave only through a `Destroy` component aimed at
`Tgt_Summoned` (the installed records put it in their CALLBACK or END_CALLBACK line), a Dispel that ends the run (so its END path runs) or, for a summoned critter,
a Dispel on the critter itself (SUM8). The records' own endings are what make a wall go away.

## Fireflash (spell 22)

The BEGIN list is the strike tile and the objects within radius 2 other than the caster, walls and the party's critters. Components, in order: the Damage,
EyeCandy 2 and 3 on the critters in the radius, then **the Summon (area `Tgt_Tile_Empty`)**, then EyeCandy entry 2 `Add` with `Play: Callback` re-aimed to the new
scenery, then `Destroy` on `Tgt_Summoned` in the CALLBACK action.

- **When it exists.** The Summon's area has a tile bit and an empty test, so it runs for the tile entry only, and only if the tile passes `Tgt_Tile_Empty` (SUM9: terrain
  not blocking, and no object of any type but items and projectiles without `OF_NO_BLOCK`). A cast at a creature from afar is a tile cast at the creature's tile at the click
  (RC4); if the creature still stands there at the landing, the tile is not empty and **no flash is made** (the creatures in the radius still get entries 2 and 3). This
  is the original's own rule, agreed by data and code (SUM16 b). The B47 report's log shows the Summon component was applied, so Steamwoven's area test passed it in
  that cast; why is not known from the log.
- **What it is.** Scenery, basic prototype 4050 (file 4070): 100 hit points, flags see-through, shoot-through, click-through, provides-cover, invulnerable, **no block**; scenery
  flags 0, so its main run is the plain loop (goal 1) and nothing burns. Art: scenery category 15, number 11, a single 1 × 1 pixel frame, i.e. nothing visible of its own. It is
  placed at the tile with no offset and no rotation, marked dynamic (OM16) and summoned (0x4000), with the caster as the source of its goal runs (SUM4).
- **What shows.** Eye-candy entry 2 of the spell (foreground, translucent, animating, not looping, with a sound and a light), 7 frames at 8 per second, action frame 6.
- **How it dies.** `Play: Callback` makes the run wait for the animation; the callback goal fires at the action frame (EC14) about 0.75 s after the start (derived from the
  ART header, not observed), the run's CALLBACK action runs the Destroy (DS2: the object destroy 0x43CCA0) and the run ends. No maintenance, no duration, no timer of its own.
- **The same flash** is the explosion of 16 records, the thrown grenades and gases among them (SUM10, SUM14). Tech: Explode (Paralysis) (202) is the one that keeps it for its 6-second
  duration and destroys it at END.

## Wall of Fire (spell 21)

- **Five pieces from one component.** The record has one Summon component. Its BEGIN area is `Tgt_Tile` plus `Tgt_Tile_Radius_Wall` with radius 2 (TA12): the centre and two tiles
  each way along the step the direction from the origin to the target tile picks, five distinct tiles. The component's own area is `Tgt_Tile_Empty_Immobiles` (SUM9): terrain
  that blocks, or a blocking wall, portal, scenery or trap on the tile, leaves it without a piece; critters and items do not. The five pieces are the same prototype, made in list order
  at their own tiles with no offset and no rotation.
- **What a piece is.** Scenery, basic prototype 4031 (file 4051): 100 hit points, see-through, shoot-through, click-through, provides-cover, invulnerable, **no block** (everything can
  stand and walk in it), blit flag 0x10, scenery flag **`OSCF_IS_FIRE`**. Art: scenery category 15 number 101, 75 × 130, 16 frames at 15 per second (looping through the scenery's main run).
  The installed eye-candy table has **no entries 2 and 3 for this spell**, so the record's EyeCandy lines add nothing: the wall's whole look is its own art.
- **It hurts.** `OSCF_IS_FIRE` makes the main run goal 77 (GT77), which burns the objects on its tile: 3 fire per burn step outside combat, 6 fire once per round in a turn-based round, through the full damage step with the piece as the
  attacker (GT76, GT94). Because the summon wrote the caster into the run's source slot (SUM4), the caster and the NPCs whose leader chain reaches a PC caster are skipped, the caster's
  aptitude bonus is added and the blame and experience go to the caster. A piece made without a source (a placed one) would burn everyone.
- **Life.** No timer. The record maintains (cost 1, period 10, no duration), so the pieces stand while the run is maintained and until it ends: unpaid MAINTAIN (RP2), the caster's
  knock-out, death or demaintaining (UE2, UE3, UE5), a Dispel on the caster's run. At END the Remove lines find nothing; the END line (EyeCandy 2 `Add`, `Reverse`, `End_Callback`) names an
  entry with no art and no translucent or tint flag, so the play answers "not played" and the END callback runs at once (EC11); the END_CALLBACK `Destroy` on `Tgt_Summoned` then destroys **all five together**.
  The `Callback` line (`OF_DONTDRAW` off) is never fired and the summon never sets `OF_DONTDRAW` on a scenery, so it is inert.
- **Siblings.** Wall of Stone (17) and Wall of Force (32) are the same shape with `Tgt_Tile_Empty` (so blocked tiles drop out of the list), pieces that **block** (files 4052 and 4050; the
  stone is not invulnerable), static or looping art, no eye candy, maintenance 1/10 and 2/10 (SUM14).

## Summon Undead (spell 57), Succour Beast (53), Familiar (74)

Spell 57: line 0 is the Summon (area `Tgt_Tile_Empty`, list 5); the pick is by aptitude (SUM6): thresholds 20, 60, 85, then the rest, against the run's captured parent aptitude. The creature is
made through the NPC branch (SUM5: faced toward the caster, `OF_DONTDRAW` set, alignment copied, faction by the rule). **Line 1** is an ObjFlag line on `Tgt_Summoned`:
`obj_f_spell_flags`, `OSF_MIND_CONTROLLED`, on. It is the one flag line of the record, it sets the flag on the **summoned creature**, and nothing turns it off (the END destroys the creature). Line 2
plays EyeCandy 2 on it with `Callback` (19 frames at 18 per second, action frame 12, about 0.67 s) and the `Callback` line clears `OF_DONTDRAW` on `Tgt_Summoned`: the creature appears when the summoning
animation reaches its callback frame. At END the entry plays in reverse with an END callback and the END_CALLBACK `Destroy` removes the creature. Spell 53 is the same with list 4; Spell 74
(list 3) also sets `OSF_FAMILIAR` on the caster and `ONF_FAMILIAR` on the creature and has no END lines, so the familiar stays. Spell 56 (Conjure Spirit) has no Summon
(ARC-DEV-304). The B47 log's "value=16" is the component type number of ObjFlag.

## Census of the installed Summon components

42 components, one in each of 42 records, all BEGIN (SUM10).

| Made | Prototype (basic; file = +20) | Spells (component index) |
| --- | --- | --- |
| scenery, cloud | 4029 (4049) | 11 Poison Vapours (2); 200 Mustard Gas (1) |
| scenery, wall of force | 4030 (4050) | 32 (0) |
| scenery, wall of fire | 4031 (4051) | 21 (0); 180 Tech: Explode (Fire) (0) |
| scenery, wall of stone | 4032 (4052) | 17 (0) |
| scenery, fog and smoke | 4033 (4053) | 26 Call Fog (0); 178 Tech: Explode (Smoke) (0) |
| scenery, ice squall | 4034 (4054) | 27 (1) |
| scenery, flash | 4050 (4070) | 22 (3); 148 (3); 166 (2); 175 (5); 176 (5); 177 (3); 179 (3); 185 (4); 198 (3); 199 (4); 201 (5); 202 (3); 205 (4); 217 (1); 219 (5); 220 (5) |
| critter by prototype (for completeness) | NPC prototypes | 14, 19, 24, 29, 68, 71, 72, 73, 123, 124, 125, 126, 135 |
| critter by list (for completeness) | list 0, 3, 4, 5 | 183 (list 0), 74 (list 3), 53 (list 4), 57 (list 5); lists 1 and 2 are used by no installed record |
| item, container, eye-candy object, any other type | none | none |

The cloud, fog, ice and flash prototypes all share the invisible art (category 15 number 11); the spell's eye-candy overlays are their look. The three wall prototypes have their own
art and no eye candy.

## Anomalies and classification (BUG RULE)

| What | Verdict |
| --- | --- |
| OM25's inference that 0x4372B0 writes a PC caster into the spell runs | Reconstruction slip: it writes the summoner into the new object's goal runs (SUM4). Both modes follow the new row. |
| Fireflash makes no flash on an occupied strike tile | The original's own rule; data and code agree. Not a bug of the executable, so no Original switch and no Steamwoven rule is proposed. Whether the owner's cast met an empty tile is unknown; a flash on an occupied tile would need a data change, which the project does not make. |
| A failed summon creation calls `exit(1)` | Original bug, unreachable with installed data; Steamwoven keeps a typed refusal (no switch). |
| The inert `OF_DONTDRAW` `Callback` lines of the walls and clouds | Original data, harmless. |

## Hand-off

### E1-a: created scenery objects (needs this note, not E1-b)

- **Kinds to create:** type 3 scenery from seven prototypes, file numbers 4049 to 4054 and 4070 (SUM11). All of them are in the installed prototype files; the created object is the prototype's field
  set plus: location (the tile, no offset), `OF_DYNAMIC` (OM16), spell flag `OSF_SUMMONED` (0x4000). Nothing else is written by the summon (SUM4).
- **Drawn and picked by the ordinary object paths.** Flags to honour: see-through, shoot-through, click-through (not picked), provides-cover, invulnerable (all but file 4052), `OF_NO_BLOCK` (all but files
  4050 and 4052). The blocking pieces (Wall of Force, Wall of Stone) must be seen by the passability and path queries as a blocking scenery on its tile (static scenery is already; a created one is new), and the
  Wall of Stone piece must be breakable (100 hit points, not invulnerable; the object damage path of ARC-DEV-255).
- **Art:** scenery category 15 numbers 11 (1 × 1 invisible), 100 (static), 101 (16-frame loop), 102 (15-frame loop); blit flag 0x10 on files 4050 and 4051. The existing scenery art path decodes these
  selectors; the 1 × 1 file carries bytes after its frame (ARC-DATA-016), so it needs the lenient decode, and an invisible 1 × 1 frame is not a missing art.
- **Main run:** the scenery's animation loop is goal 1, or goal 77 (burn) with `OSCF_IS_FIRE` (file 4051); the original starts it when the object enters the object list (GT77, GT98). The viewer's
  scenery loops (`AreaSceneryHost`) must start for a created scenery too. **The summoner is the burn's source** (SUM4), but only in the run's bottom frame: the original loses it on any restart of the main run (sector reload or fold, sweep, art set), so the created-object record may carry the summoner's identity for the first run, and a rebuilt run need not get it back.
- **Save (the original):** the object as a dynamic object of its map (NI3, OM20: saved whole in the map's mobile data; a destroyed dynamic object is not written, OM17, OM19); its overlays (the eye-candy fields) with it; the
  spell run with its summoned list (PS11); the goal runs. Steamwoven keeps goal runs run-local (H9), so E1-a decides what to rebuild; the minimum to rebuild a piece exactly is: prototype file number, tile, summoner
  identity, spell flags, the owning run's id, and whether it is on its destroy path.
- **Life:** no timer on the object. Removal is by a Destroy (flag `OF_DESTROYED`; the object stays in memory until the map clears, OM19) or by the map's own clear. A sector fold carries created objects with their
  sector (OM16, RL3).
- **Not needed here:** any eye-candy **object** kind. No installed Summon makes one; "eye candy" in these records is an overlay on the created scenery (EC9), which Steamwoven already draws.

### E1-b: the Summon component for objects (needs E1-a)

- **Apply once per passing list entry** (SUM1), in list order, with the entry's tile as the location and the run's parent as the summoner; keep the context's "summoned" slot for the `Tgt_Summoned` re-aim (it persists across entries and is reset once per action, SUM1: a refused entry re-aims to the previous entry's object), and put every
  object on the run's summoned list (head insertion, by permanent ID, SUM7). `Tgt_Summoned` gathers read that list and skip destroyed objects (TA10).
- **The area tests are already built** (`SpellTargetRules.Empty` with the masks 0x3FFEF and 0x2000B; the wall and tile-radius gathers). The component must not apply to an object entry (it has no tile).
- **The counts to expect in a stage:** Wall of Fire five pieces in a line (fewer where a blocking immobile or blocking terrain stands); Wall of Stone and Force the same with an empty-tile test per piece; Call Fog, Poison Vapours, Squall of Ice and the
  smoke or fire techniques up to nine pieces (a 3 × 3 square, radius 1), as their areas say.
- **The burn:** pass the summoner into the scenery run's source (SUM4), so the fire wall spares the caster and, for a PC caster, its followers (GT94). The source lives only in the run's bottom frame: a restart of the main run drops it (SUM4), which the original does too. The burn code of S9c (`AiExecutorRules.FireDamage`, `SessionSpellCaster.ApplyBurn`, the viewer's `AreaFireBurner`, ARC-DEV-144) is written for a scenery with no source (GT76); the slice checks how it takes one and gives it the summoner.
- **END and dispel:** the records' lines do the removing; the component adds nothing. Wall END: Remove lines, the END EyeCandy line (an entry with no art and no translucent or tint flag is "not played", the END callback runs at once, EC11), then END_CALLBACK `Destroy` on `Tgt_Summoned`
  (DS2: a run whose margin is not 0 spares an object that lacks 0x4000; created pieces have it). Flash: the `Callback` action after the animation's callback frame. Dispel on a summoned scenery ends the run only (SUM8).
- **A stage per spell (the plan's 21 and 22):** 22 on an **empty** tile: the flash exists, then is destroyed after the animation's callback (about 0.75 s); 22 on a tile with a creature: **no flash**, entries 2 and 3 on the creatures in the radius.
  21: five pieces in a line chosen by the cast direction, the burn on a creature standing on one (3 fire per step outside combat, 6 once per round in a turn-based round, the caster unharmed), END at the spell's end or a dispel of the run: all five gone together at once.
- **Closes B47** for reports 1 and 2 of the three, with the Fireflash caveat above for the owner. Report 3 (Summon Undead) is E1-e.

### For E1-c, E1-e, E1-f (not started here)

SUM5 and SUM6 give the critter half; the one open read is the AI's use of `OSF_MIND_CONTROLLED` and the stat value stored in the summoned list's node (SUM7). The `Callback`/`OF_DONTDRAW` reveal and the END destroy of the creature follow the same record lines as the scenery.

## Not read

Who tests `OSF_MIND_CONTROLLED` in the AI and movement code; the reader of the stat value kept in a summoned-list node; the functions 0x45EE30 and 0x424070; the odd loop shape of 0x4372B0 for a run
with a deeper stack; the network packets; what the save holds of a run beyond its two lists. Values only a running game shows (how often an empty tile is met, the real animation timings) stay at F1 (F4 is a
runtime observation).
