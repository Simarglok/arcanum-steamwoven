# Trait / Polymorph remainder (MA-8): options for the open limits

Status: **Research with options and a recommendation**, work item ARC-RES-145 (2026-11-12). Nothing is implemented here; each recommended fix is a
later task. Read on 2026-11-12 in the 1.0.7.4 executable (`VERSION:1.0.7.4`) from the Ghidra export (F1, one reader; no emulator, no patching) and
in the player's installed archives (`arcanum1.dat` to `Arcanum4.dat`, the module archive and its patch). No original bytes, decompiled text, art or
GrandFix files are in git; addresses and behaviour are prose.
Owner answers of 2026-11-12 are in section 6; the two reads they asked for (the palette-absent draw, the GrandFix files) are in PR5 and section 5.

Requirements (reference only, no coverage row changes): [FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007),
[FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010). Context: [magic audit, Trait](magic-audit-res-103.md#trait-ma-8-arc-magic-040),
[host contract](../world/spell-cast-host.md#trait-component-arc-magic-040), [ledger TR1–TR6](ledger/spell-runs.md#trait-component-arc-magic-040),
[actor visual contract](../rendering/actor-visual-contract.md). Rule D9: the original's behaviour in both modes; where GrandFix differs it is marked.

## 1. Answer

The six limits the MA-8 report named are not six independent gaps. Two findings change the picture:

1. **The installed ARTs are complete for what the original draws.** What the loader (`HeroBodyArt.LoadMonster`) calls "missing" is the original's
   **missing-art repair (MA1)** and its **weapon part**, which the hero's monster body does not apply yet. The air elemental (species 28) has no
   stand and no run with weapon part 0, but it has the stand with weapon part 1 (the original's repair picks it) and a walk (the repair replaces the run).
2. **Three of the five installed body spells are refused today, not only the air elemental.** Body of Air, Body of Fire and Body of Water ask for palette 1,
   and the three elemental ART sets hold palette 0 only; the loader refuses a palette the file does not hold. Only Polymorph (sheep) and Body of Earth read.

| # | Limit (from the MA-8 report) | What is really behind it | Recommended fix | Size | Needs the owner |
| --- | --- | --- | --- | --- | --- |
| L1 | Low attack and throw not loaded | The low attack of a monster is the attack (the original's repair), except the fire elemental's own file; the throw has no consumer (no weapon can be worn in a monster body) | Load the fire elemental's low attack; leave the throw out | XS | no |
| L2 | Weapon part of the body's art id always 0 | The original keeps 0 out of combat mode and 1 in it; with 0 **no species has an attack file**, so the attack plays the stand pose today | Weapon part follows combat mode (1 in it) and the original's repair for walk and stand | S | no |
| L3 | Air elemental (28) has no ART, the hero keeps its own body | Missing weapon-0 stand and run are repaired by the original; palette 1 is absent from the three elemental ART sets (air, fire, water); fire needs a higher resource cap | Apply the repair, take palette 0 when the requested palette is absent (the original does, PR5), raise the monster cap | S–M | no (D3 answered) |
| L4 | Redraw of a map NPC whose data holds the perma-polymorph | No installed NPC prototype holds the bit (0 of 510); map instances were not scanned | Census of the map instances first; build only if one exists | XS, then S | yes: build or not |
| L5 | Body read error remembered for the run | Same rule as the human bodies; after L2 and L3 no installed spell leaves a body that cannot read | Forget the failure at the next Trait apply | XS | no |
| L6 | Distorted provenance text (cosmetic) | `Provenance` decodes a monster art id with the human fields | Name the monster body in it | XS | no |

GrandFix (section 5) does none of this: its documented changes are prototypes, text, spells, ground art and localization, not monster ARTs, art ids or
the Trait component, so it offers no fix to adopt and no data to compare for these limits.

## 2. What the original does (read for this task)

| ID | Function (address) | Original 1.0.7.4 | Verdict |
| --- | --- | --- | --- |
| PR1 | Trait apply, the new art id (0x452B00 → 0x503CD0) | The art builder takes nine arguments in this order: species (5 bits), three zero fields (armour 3, shield 1, a 5-bit field), the **rotation** of the old art (0x502880), the **animation** of the old art (0x503E20), the **weapon part** (4 bits) and the palette (2 bits). The apply passes weapon part = **1 when the old art's weapon part (0x5040D0) is not 0, else 0**, and the species and palette of the spell. A range violation exits the game (TR4). | Original (confirms TR4's wording; the builder's field order is now pinned) |
| PR2 | Weapon part and shield part of any critter art (0x465020 → 0x504100, 0x504180, 0x4B3D50) | The refresh asks the combat-mode test (0x4B3D50: type PC or NPC and the combat bit of the critter flags). Out of combat mode the weapon part is 0 and the shield 0. In it the weapon part is the use-art subtype of the item in slot 1004 (1 when the slot is empty, 1 again after a logged error when the item's art has no subtype), the shield 1 for an armour item in slot 1005. The two setters accept art classes **2 (critter), 12 (monster) and 13 (unique NPC)**, so a monster body gets its weapon part by the same refresh. The function returns the new art id (it does not store it; its callers do). The refresh runs after every wield and unwield of slots 1004 to 1006 (0x4677B0, 0x467CB0) and in the animation and art refresh 0x4B3F80 (called, not read line by line: F0 for its other branches). | Original |
| PR3 | Monster art names (N1, MA2, MA3) | `art/monster/<name>/<name><armour><X or S><weapon letter><animation letter>.art`; an armour of 0 is `uw`; the weapon letter is `a` for part 0, `b` for part 1; the animation letter is `a` plus the animation (stand `a`, walk `b`, run `g`, fall `h`, get up `i`, vault `n`, throw `o`, attack `u`, low attack `v`, `x` the 23rd). Animations 6 to 19 clear the weapon, the walk clears the shield, animation 23 sets weapon 1. | Original (already in the area loader) |
| PR4 | Missing-art repair (MA1, 0x41DA40) on a monster | The table at 0x5A106C has 19 entries, each one bit of the species' mask (`art/missing.dat`, current in the installed data; the weapon is compared only for animation 21), set when the entry's file does not exist. **Walk with weapon 1** is missing for 30 of the 32 species, so a walk in combat mode becomes the walk with weapon 0; the lizard man (12) and the phantom knight (13) have their own weapon-1 walk (`bb`) and keep it. **Run** missing becomes the walk. **Stand with weapon 0** missing becomes the stand with weapon 1. **Low attack (21) with weapon 1** missing becomes the attack (20). Further entries send stealth walk (3) to the walk, 23 to the stand and 17, 18, 19 and 24 to the fall (7). No entry covers the throw (14). | Original |
| PR5 | Frame lookup of a palette the art does not hold (the pixel lookup 0x502E50, the draws 0x5059F0 and 0x505EB0; the loader 0x51B710, the test 0x502E00) | The art cache loads every palette the file holds when it first caches an art (0x51B170); a load of one palette whose presence word is 0 answers error 0x10 (0x10 is also the answer to an open, read or header failure). **The pixel lookup and the two draws test the palette's table: when it is absent they read palette 0's table instead** (no guard if palette 0 is absent too, which no installed monster ART is). So an art id that asks for a palette the file lacks is drawn with palette 0, no placeholder. The art-id search 0x41E200 (for a monster it tries the next palette, then the next species) appears to be an iterator rather than a draw fallback; its other branches and the callers 0x43F090 and 0x43F130 of the cache test 0x502E00 are not read. | Original (F1, answers D3) |

## 3. What the installed data holds

**Monster ARTs (32 species, `monster.mes`).** Every species has the stand with weapon 0 (`a`) except the air elemental; every species has the stand and the attack with
weapon 1 (`ba`, `bu`), the walk (`ab`), the run (`ag`, except the air elemental), the fall and the get-up (`ah`, `ai`); the lizard man and the phantom knight also have a walk with weapon 1 (`bb`). No species has an attack with weapon 0.
The low attack with weapon 1 (`bv`) exists for the fire elemental only. The throw (`ao`) exists for the orc, the earth, fire, water and air elementals only. The vault (`an`) for the same five.
The orc also has armour codes `bn` and `pc` besides `uw`.

| Species (number, folder) | Palettes held by the ARTs | Installed spell that makes it |
| --- | --- | --- |
| 20 `shp` (sheep) | 0 to 3 | Polymorph (49), palette 0 |
| 3 `ert` (earth elemental) | 0 to 3 | Body of Earth, palette 1 |
| 26 `FIR` (fire elemental) | 0 only | Body of Fire, palette **1** |
| 27 `WAT` (water elemental) | 0 only | Body of Water, palette **1** |
| 28 `AIR` (air elemental) | 0 only | Body of Air, palette **1** |

**Today's loader on the real data** (a scratch run, 32 species by 4 palettes; not in git): every species and palette reads except the following. Fire elemental, palette 0: resource limit
(the 16 MiB RGBA or the 512-key cap; its frames are large). Fire, water and air with palette 1 (the spells' palette) and the air elemental in every palette: refused for the palette
(and, for air, the stand). Water elemental palettes 1 to 3, the death knight palette 3: palette absent. Spider queen (30): decode failure in every palette (no installed spell makes it).
So after the repair and the palette change the fire elemental also needs a higher cap.

**Prototypes.** Of 510 critter prototypes in the installed `data/proto`, none holds the perma-polymorph bit (critter flags 2, 0x400000) and 184 carry a monster art (class 12) as their art. Map
instances (the objects of the maps) were not scanned; a GrandFix install replaces NPC prototypes, so the census applies to the original data only. No installed item carries technology record 222 (the permanent polymorph); the script condition `obj_is_polymorphed` is read by 22 scripts.

## 4. Options per limit

### L1. Low attack and throw

- **Finding.** The profile has a low-attack slot used when the hero hits a short, shrunk, falling or lying target, and a throw slot used for a returning weapon. The monster loader never fills them. For
  the low attack the original's own answer is the attack (PR4) for 31 species, so an empty slot **is** correct there; only the fire elemental has a file of its own. The throw is used by a boomerang, and the
  equipment rules refuse to wear any item in a monster body, so the slot has no consumer.
- **What the throw is (owner question D2).** Animation 14 is the pose of a thrown weapon that comes back: the hero plays it when it attacks with a weapon carrying the returning-weapon flag (a boomerang);
  `WeaponArtRules.Animation` (0x42B9C0) picks it from that flag, and the human body loads its ART (`HumanThrowSelector`) for it. It is not the grenade or bottle throw, and not a spell. In a monster body the hero cannot wield
  anything (Trait takes every worn item off and the equipment rules refuse to wear one), so it can never play the throw; the question is only whether to load an ART nothing will draw.
- **A (recommended, owner D2: asked for the explanation).** Load the fire elemental's low attack (weapon 1) in the monster read; leave the throw out. XS, a unit test on the name and the real-data run.
- **B.** Also load the throw for the five species that have it. Nothing draws it; adds decode time and memory for no visible result.
- **C.** Emulate the original's bad-art draw for a throw of the other 27 species. Not worth it: it cannot happen without a wielded weapon.

### L2. Weapon slot in the art id

- **Finding.** The loader builds the body's art ids with weapon part 0 always. No species has an attack file with weapon 0 (section 3), so the hero's attack animation plays the stand pose today
  (the loader's stand-in). In the original the weapon part is 1 in combat mode and 0 out of it (PR2), the apply copies it from the old art (PR1), the stand and attack are read from the weapon-1 files and the
  walk is repaired to weapon 0 (PR4; the lizard man and the phantom knight keep their weapon-1 walk). The hero's equipment is empty in a monster body, so the part is only ever 0 or 1.
- **A (recommended).** `HeroBody.Armed(combatMode, null, null)` already yields part 1 for bare hands; give the monster body the same field and read in combat mode the stand `ba`, the attack `bu` (the
  fire elemental's low attack `bv`), the walk (`ab`, for the lizard man and the phantom knight `bb` in combat mode) and the run `ag`, out of it the weapon-0 files. The viewer already swaps the human hero's body on a combat-mode change (`WantedHeroBody` returns the monster body before that step today); the monster branch gets the same step, and the body key (and the failure memo, L5) the part.
  Smoke: extend `trait-body` with a combat-mode attack (the stand pose must differ from the attack). S.
- **B.** Keep part 0 and rename the stand-in to weapon 1 for the attack only. Draws an attack but not the combat stance; leaves the key as it is. Cheaper by half a day, still a divergence from PR2.
- **C.** Leave it (today's behaviour): the hero polymorphed to a monster never attacks visibly. Not recommended; the audit calls the attack a visible feature.

### L3. Air elemental (and fire and water)

- **Finding.** Three separate causes: (a) the air elemental lacks the weapon-0 stand and the run, which the original's repair covers (PR4); (b) the elemental body spells ask for palette 1 and the ARTs hold palette 0 only
  (PR5: the original draws palette 0 for a palette the file lacks); (c) the fire elemental's frames pass the loader's caps.
- **A (recommended, owner D3).** Apply the repair in the monster loader (the stand with weapon 1, the run replaced by the walk with the gait stride of the walk), take palette 0 when the requested palette is absent (PR5: what the
  original draws, and what the decoder's present-slot policy already does for whole-map NPC art), and raise the monster read's caps after measuring the fire elemental. S–M.
- **B.** Fall back to palette 0 only in the loader and leave the repair out. Leaves the air elemental refused. Not recommended.
- **C.** Keep refusing (hero keeps its own body, logged). The spells then look broken for three of five bodies. Not recommended.
- **Owner's question (answered).** D3: the original's draw was read first (PR5) and is copied: palette 0.

### L4. Redraw of a map NPC whose data is a perma-polymorph

- **Finding.** The restore builds the body from the critter's stored art (field 24) with the current art's rotation and animation (TR6). The session keeps the bit and the Dispel undo (ARC-MAGIC-047); the viewer
  has no way to draw the stored body of a map NPC whose data art is already the monster. No installed prototype carries the bit and no item carries the technology record; map instances were not checked.
- **A (recommended).** Census first (XS): scan the objects of every map for critter flags 2 bit 0x400000 with a scratch tool, as for prototypes. Build the redraw (read field 24, draw it at the Dispel) only if at least
  one NPC has the bit; otherwise record "no consumer in the installed data".
- **B.** Build it now. Unmeasured work for a case that may not exist. Not recommended.
- **C.** Cancel the limit with the census result noted. Valid if the census is empty.

### L5. Body read error remembered for the run

- **Finding.** `_heroBodyFailures` is keyed by the body and never cleared in a run, for the human bodies too. A data refusal is deterministic, so remembering it is right (the same failure twice stops, not loops);
  an I/O error is not. After L2 and L3 no installed spell leaves a body that cannot read.
- **A (recommended).** Drop the monster body's failure entry at the next Trait apply for the hero (the event the viewer already takes). No timer, no retry loop. XS.
- **B.** Keep as is and document. Acceptable once L3 is done.
- **C.** Retry I/O errors on a timer. More state for a case no one has seen. Not recommended.

### L6. Provenance text

- **Finding.** `ActorDiagnosticProfile.Provenance` for a hero body decodes the selector with the human fields, so a monster body reads as "type2/body…" nonsense. Cosmetic: it appears in the HUD gait line.
- **A (recommended).** For class 12 print the species and palette. XS, fold into L2.

## 5. GrandFix

**What it is.** GrandFix is a Russian community patch and localization of the 1.0.7.4 game, maintained by Gentle Giant on the Arcanum Club forum (the workshop page names version **1.3** as current). The 2020 forum post
of a new installer repackages the same GrandFix 1.3 with Mini Fix GG2 (a cumulative data fix of 2007), a fix for random encounters and a crash after level 50, and an optional level-cap removal. The forum also offers a build of
`Arcanum.exe` called 1.074 (no-CD, window mode, level-cap remover, random-encounter fix). No later version than 1.3 is named. Sources: [Grand Fix forum](https://forums.arcanumclub.org/forum/6-grand_fix/),
[GrandFix 1.3 changelog post](https://forums.arcanumclub.org/topic/284-grandfix-13/), [workshop page](https://arcanumclub.org/workshop/grand-fix), [Mini Fix #2](https://forums.arcanumclub.org/topic/1824-edinyj-mini-fiks-2/),
[new installer](https://forums.arcanumclub.org/topic/12469-novyj-installjator-grandfix-13/), [Gentle Giant's miscellaneous thread](https://forums.arcanumclub.org/topic/8510-raznoe/).

**What it changes** (its own changelog, read in English translation): the teleport device's spell and eye candy; NPC prototypes replaced with those of the English version (for example rats' dexterity);
the stats, resistances, damage and magic skills of summoned elementals, orc soldiers, the ogre destroyer, guardians, undead and animals, with portraits for some; item and ground art, descriptions, one schematic, vendor lists,
dialogues; Mini Fix GG2 adds a mage weapon, a lure, a new arrows schematic and vendor changes; the old WorldEd. Nothing in these lists touches the Trait component, polymorph, monster ARTs, art ids of bodies,
`monster.mes`, the missing-art file or the elementals' palettes.

| Limit | What GrandFix does | Differs from the original? |
| --- | --- | --- |
| L1 low attack and throw | Nothing documented | n/a |
| L2 weapon slot | Nothing documented (the art id is built by the executable; the 1.074 executable build lists no change of it) | n/a |
| L3 air elemental art, palette | No monster ART, no palette change documented; its elemental fix is the **summoned** elementals' prototype stats, not the body spells' art | The prototype stats of summoned elementals differ from the original (a GrandFix install reads different numbers); no art change is documented (not checked at file level) |
| L4 perma-polymorph data | Replaces NPC prototypes with those of the English version; no polymorph flag change documented | Prototype stats differ from the installed original's where it replaced them |
| L5, L6 | Not applicable (viewer code) | n/a |

**File-level comparison (owner D5, 2026-11-12).** The owner allowed a download. The installer `GrandFix1.3_MiniFixGG2.exe` (6,084,200 bytes, NSIS) came from the link of the 2020 forum post; it was unpacked with 7-Zip
without running anything (about 1,470 entries, 13 MB) and compared with the installed original, all outside git. Result:

| Part | GrandFix 1.3 | Against the original |
| --- | --- | --- |
| Monster ARTs | None: its archives (`arcanum4.dat`, `arcanum5.dat`, the module patches) hold no `art/monster` file and no `monster.mes` | No change to any monster ART, species table or palette |
| Spell records | `SpellList.mes` in both its archives, against the effective installed one (the original's `Arcanum4.dat`): the archive 4 copy has 14 records changed and 4 added, the archive 5 copy 18 changed and 7 added (teleport device, Mini Fix spells). The Begin records of the five body spells (species, palette 1) and of Polymorph are identical. **In the four body spells' End the order is swapped: the original clears the body-of spell flag first and then restores the art (Trait −1); GrandFix restores the art first.** | The Trait data (species, palette) is unchanged; the End order differs from the original |
| Monster prototypes (`rules/monster.txt`) | 5 of 162 entries changed: the demon's level, scale, stats and resistances; the L'anamelach's nigh-invulnerable flag; the automaton renamed Charged Automaton (palette 1, level 50, new stats and script); the Lord's Slave concealed flag; one internal name. The summoned elementals (air, fire, water, earth) are not among them | Data differs from the original; no art-id change for the elementals |
| Prebuilt `data/proto` | 1,427 files (4 more than the installed 1,423) with localized names; of the critter prototypes 3 have another art id (an orc and an earth-elemental prototype and the automaton) and 34 other stats; no perma-polymorph bit in any | Differs from the installed built files; not used by Steamwoven (it builds its own, ARC-RES-085) |
| `Arcanum.exe` | 2,007,040 bytes (the installed GOG build has 2,011,136); the code (`.text`) differs from the installed one in 9 bytes: 5 at the startup checks (0x40107D, 0x4010BD, 0x401393) and 4 in a constant 0x33 to 0x7F at 0x4A6642, 0x4A66A6, 0x4A68C8, 0x4A691B, with the same value in `.data` at 0x5B5248 (51 to 127: most likely the level cap that the installer's LevelHack option removes; not checked in the executable). The headers, import table, relocations, resources and 8 bytes of `.rdata` also differ (another build of the same version). None of the 9 bytes lies in Trait (0x452B00 to 0x452CD0), the art builder (0x503CD0), the weapon refresh (0x465020) or the missing-art repair (0x41DA40) | The code is the original's apart from those nine bytes |

So GrandFix gives no fix, no art and no data to adopt for L1 to L6, and the "touches no monster ART" claim of the change lists holds at file level. The Unofficial Arcanum Patch is a separate project and was not part
of this review; a forum post says it restores cut animations, unconfirmed for the air elemental.

**The End order.** In the original the body spells' End clears the spell flag and then restores the art; the restore chooses the best items again, and the equipment rules refuse to wear an item while a body-of flag is set. In the GrandFix order the restore runs while the flag is still set. Steamwoven takes the order from the player's data, so the implementation task must test both orders (the original's is the rule, D9; GrandFix's is a divergence).

**Consequences for D9.** GrandFix is not a source of original behaviour, and its data replacements are not to be adopted: Steamwoven reads the player's own installation, so a GrandFix install shows its own
prototype numbers, and any difference between that install and the original (stats of summoned elementals, NPC prototypes) is the player's data, not our rule. The fixes recommended above come from the
original's executable and archives, and need nothing from GrandFix.

## 6. Decisions for the owner

| # | Decision | Recommended | Owner answer (2026-11-12) |
| --- | --- | --- | --- |
| D1 | Weapon part follows combat mode for the monster body (L2) | Yes, as the original (PR2) | **Yes, as the original** |
| D2 | Load the throw for the five species that have it (L1) | No (nothing can draw it, see L1) | Asked what the throw is; explained in L1, decision open |
| D3 | Palette of the elemental bodies when the requested palette is absent (L3) | Read the original's draw first, then copy it | **Read first, then copy**: read (PR5), the original draws palette 0 |
| D4 | Redraw of a map NPC with a perma-polymorph in data (L4) | Census first, build only if a consumer exists | **Census of the map instances first, build only if one exists** |
| D5 | GrandFix file-level comparison | Not needed for these limits | **Allowed**: done (section 5), no change found |

## 7. Proposed tasks (codes from the coordinator)

1. **Monster body: weapon part, repair, palette** (L2, L1 fire low attack, L6, L5, L3): the loader changes, the `trait-body` smoke with a combat-mode attack and an elemental
   spell (Body of Air, Fire, Water), tests on the names and the repair, a real-data test over the 32 species. Requirement rows: FR-MAGIC-007 coverage note; graphics rule: one labelled composite (before: stand pose attack, after: attack; elementals before and after).
2. **Census of perma-polymorph NPCs in map data** (L4): a scratch scan, a result line in the ledger; the redraw only if the owner decides after seeing it.
3. **Corrections to the existing docs** (folded into task 1's PR): the host contract and the visual contract say species 28 lacks "the stand or walk ART" and that a monster has no armed pose; both are replaced by
   section 2 and 3 of this note.

## 8. Evidence and limits

- Read in the executable (F1, one reader; 0x41DA40, the table at 0x5A106C and the contents of `art/missing.dat` were re-read by the independent review, which decoded the masks per species): 0x452B00 with its call arguments, 0x503CD0, 0x5040D0, 0x502880, 0x503E20, 0x465020, 0x504100, 0x504180, 0x4B3D50, 0x5044F0, 0x0043EB30, 0x51B710 (the palette test),
  0x502E00, 0x502E50, 0x5059F0, 0x505EB0 (the palette-absent lookups, read on 2026-11-12) and the class-12 branch of 0x41E200. Not read: 0x4B3F80's other branches, the other branches of 0x41E200 and the callers 0x43F090 and 0x43F130 of 0x502E00.
- Measured on the installed data: the file listing of all archives for the monster folders; `monster.mes` (species names); `SpellList.mes` Trait records (species and palette); the ART headers of six species;
  `HeroBodyArt.LoadMonster` over 32 species and 4 palettes; 510 critter prototypes. All with scratch programs outside the repository; no output is committed.
- Not measured: the objects of the maps (L4), the fire elemental's frame sizes (L3), the original's gait timing for a run replaced by the walk (it follows the gait rate, HB2, and the stride of the file used).
- GrandFix files (section 5): a read-only unpack outside git, a byte comparison of the executables (headers and import tables left out), a list comparison of its archives against the installed ones, the prototype comparison through `CombatFieldReader`. Nothing was run, nothing is committed.
