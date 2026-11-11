# Technology coverage re-audit: what is built, what is missing, and the queue

Status: **Audit refresh and queue**, work item ARC-RES-125 (2026-11-11), against `origin/main` at `113d23e`. Docs-only: no source, test or build change and no
repository check run (the cloud clone has no .NET or Godot; no census, no viewer run and no executable read was done for this note). It is the status refresh of
the [technology audit](technology-audit-res-104.md) (ARC-RES-104), the technology counterpart of the [magic audit](magic-audit-res-103.md): that audit already holds the
matrix of 8 disciplines by 7 degrees and the slice list, so this note does **not** redo it. It says which slices have landed since, what is still missing, what
"runs" does not prove, and the order of what is left.

Requirements (reference only, no coverage row changes): [FR-ITEM-011](../requirements/items-economy.md#fr-item-011), [FR-ITEM-016](../requirements/items-economy.md#fr-item-016),
[FR-ITEM-017](../requirements/items-economy.md#fr-item-017), [FR-ITEM-018](../requirements/items-economy.md#fr-item-018), [FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007),
[FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010), [FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011), [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012),
[FR-CHAR-017](../requirements/characters-skills.md#fr-char-017), [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019), [FR-CHAR-020](../requirements/characters-skills.md#fr-char-020).

## 1. Answer

1. **The hero's chain is complete on the player's data.** Buy a degree on the sheet, open the schematic window (`T`, dialog `su`, script 129), assemble (118 recipes gate-checked), use the product, wear or fire it:
   covered by the smoke stage `tech-craft` (degrees 1 and 2, assembly, two foods, a firearm) and by unit tests. Degrees, expertise, the component gate, charge cells, item durability and repair,
   technology failure and magick cancel rolls, aptitude and its resistance rule are built. No hidden mechanic of the original is missing (RES-104 answer 5 still holds; the aptitude and expertise readers were checked in ARC-RES-114).
2. **Technology records: 74 of 83 run** on the hero and on an NPC. The census table in `TechCensusRealDataTests` (ARC-DEV-363, updated since by the ObjFlag writer, Dispel, social flags and Movement) holds nine rows that are not RUNS:
   147, 168, 183, 184, 187, 188, 203, 214 (HERO: an NPC's effect list refuses the 80 entries it adds; fixed by ARC-RES-127, see row 11 of section 6) and 222. Over the 65 records an installed item carries: **59 RUNS, 1 HERO, 3 PART, 2 INERT**
   (RES-104 counted 17 / 18 / 16 / 14 when it was written). The Summon, NPC effect list and ObjFlag groups that caused most gaps are closed.
3. **RUNS is a host verdict, not a behaviour check.** The census says every component of a record is taken; it does not say the flag it writes has a reader. By a symbol search on `113d23e`, the drunk bit (records 172, 181)
   and the shielded spell bit (209) have **no reader in `src` or `tools`**, and the viewer draw does not read the hidden, shrunk or translucent flags (a limit MA-1 named). Section 4 lists which flags have readers.
4. **What is genuinely missing is short and now has no blocker:** the powered shield cannot be worn (T-m2), creature items do nothing when thrown (T-m1, unblocked since E1-c landed), placed trap devices refuse (E6-b, unblocked since T-g),
   the cell-branch and control components (E4-c), nested Cast (203), creature Summon with AIRedirect (183, E1-e) and Trait (222, MA-8), plus the reader gaps of section 4 and the smoke gaps of section 5.
5. **Nothing here is owned by a running thread** (no technology or magic thread is active on 2026-11-11). Four of the slices below are shared with the magic queue ([magic audit](magic-audit-res-103.md)): E4-c, E4-e/MA-8, E1-e and the flag readers; they are listed once, here, with the magic label.

## 2. Method and limits

- **Read:** the RES-104 audit and its updates, the census (ARC-RES-115) and item-script (ARC-RES-112) notes, the coverage rows named above, the component catalogue `docs/project/status/technology.md`,
  `docs/project/missing-systems-plan.md`, the magic audit's work order, `TechCensusRealDataTests.cs` (its `Expected` table and carried tally) and the merged-PR history since 2026-11-08.
- **Code claims** are symbol searches on `113d23e`, each hand-read at the lines that decide it; "no reader" means no symbol outside the flag's definition, which can miss a reader that tests the raw bit.
- **Not done:** no run of the census (the counts above are the test's pinned table, not a fresh run), no executable read (research-private is not in the cloud), no viewer check. Reads of the original stay at their ledger level.

## 3. What changed since RES-104

| Item | State on `113d23e` | Where |
| --- | --- | --- |
| T-a smoke `tech-craft` | done, ARC-DEV-362 | `TerrainViewerTechCraftHarness.cs` |
| T-b census test | done, ARC-DEV-363; its table moved with MA-1 (ARC-DEV-358), MA-3 (ARC-MAGIC-034), MA-4 (-035), MA-5 (-036, -038) | `TechCensusRealDataTests.cs` |
| T-h NPC grenades | done, ARC-DEV-364; the exe's inverted blast-radius test is fixed in both modes, ARC-DEV-373 | [ledger TH1-TH4](ledger/npc-ai.md#npc-grenade-host-arc-dev-364) |
| T-g item cursor and the hero's goals 39 and 41 | done, ARC-DEV-365; the device use still answers NOT READY (see E6-b) | [ledger IA1-IA16](ledger/items.md#item-aiming-and-use-with-a-target-arc-res-113) |
| T-m3 tech books | done, ARC-DEV-366 | [XA11](ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114) |
| T-j stale statements | done, ARC-RES-116 | RES-104 section 9 |
| E1-b Summon of scenery, E1-d NPC effect list, E1-c critters (c1 to c3, inventory) | done | missing-systems plan, magic audit work order |
| Reads T-r1 to T-r4 | done (ARC-RES-112 to -115) | notes of the same names |
| Still open from RES-104 | T-m1, T-m2, E6-b, E4-c, E4-e, E4-f, E1-e, E1-f; the creation editor's degree buttons were "not confirmed" and are still unchecked | section 6 |

## 4. Readers of the flags the technology records write

A flag counts as read when a symbol outside the data tables uses it; behaviour against the original is a ledger question, not decided here.

| Flag (records) | Reader found | Note |
| --- | --- | --- |
| Fatigue limit (153, 195, 210) | yes (spell payment, combat, attack resolver) | |
| Blind (177, 186) | yes (`GameSessionBlinded`, life rules, ground rules) | |
| Flee (148, 165), mind control (142, 194), undead (194) | yes (morale rules, attack resolver, AI host; `SessionSpellCasterSocial` is their writer) | MA-4 |
| Magnetic inversion (182, 187) | yes (attack rules, equipment choice, skill check) | |
| Enshroud (167) | yes (AI host, hostility rules) | |
| **Drunk (172, 181)** | **no, and none in the original** | read in ARC-RES-126: no code of the executable and no installed record reads the bit ([SFR2](ledger/spell-runs.md#readers-of-the-drunk-and-shielded-spell-flags-arc-res-126)); nothing to build |
| **Shielded (209)** | **no** (the `shielded` in `HostilityRules` is the accidental-hit test, not the spell bit) | read in ARC-RES-126: the only reader is the hit effect 0x459A30, a hit eye candy (SFR3); record 83 lists it in `Disallowed_TSF:`, which the session reads (SFR4) |
| Don't-draw (178, 183, 200, 217) | written; `AreaAiHost` and traps read it | the viewer draw does not (MA-1 limit) |
| Permanent polymorph (222) | **INERT**: needs the Trait component | MA-8 |

## 5. Smoke and test gaps

`tech-craft` is the only stage with a technology record on real data besides `grenade` (whatever the map lays) and `cast`. Not exercised on real data (RES-104 section 7, unchanged): a Chemistry, Therapeutics or Explosives product used, a found schematic document read,
a charge cell spent and an NPC crafter through `su`. Per the smoke-scope rule these go into `tech-craft` (or one sibling stage) rather than a new runner.
Unit-test coverage of the rules is as RES-104 lists it; no gap was found there.

## 6. Queue

Labels are planning labels, not task codes (the coordinator hands codes out). Sizes: S about 200 lines, M one PR with a smoke stage, L more than one slice. "Needs" lists unfinished work only.

| Order | Label | Content | Records / products | Size | Needs | Lane |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | **T-m2** powered shield | The item-event set admits actions 54, 125 and 135; a deferred 15-second call checks the wear and uses one power-cell unit, saved with the game | Electric degree 3 shield (cannot be worn today) | S to M | the game event queue's script call kind ([game-event-queue](game-event-queue.md)), catalogue entry for action 135 | directory |
| 2 | **T-m1** creature items (built as ARC-MAGIC-042) | HIT and MISS item events at a thrown item's landing, actions 40, 21 and 36, the created critter as a follower or ally, viewer smoke step | Mechanical degrees 6 and 7 products | M to L | E1-c (done) | directory and viewer |
| 3 | **E6-b** placed trap devices | The device use stops answering NOT READY: placing on an object or a tile, the device spent, marker 4047 or 4049, the delayed run after 88 000 ms (TP3), alarm for record 176, refusals IA13 | Mechanical degrees 1 and 5; the timed bombs: items 15154 (record 176) and 15193 (record 220) | M | T-g (done), E1-b (done) | directory |
| 4 | **R-1** flag-reader read (**done, ARC-RES-126**) | Mac read: who reads the drunk bit and the shielded spell bit in the exe, and what they do; ledger rows only (SFR1 to SFR5) | 172, 181, 209 | S | none | none |
| 5 | **E4-a2** shielded hit eye candy and don't-draw draw | R-1 found one reader: the hit effect 0x459A30 plays the run's hit eye candy for shielded (and magnetic inversion) at a hit on a PC or NPC (the entangled branch is built); drunk has none. Plus the viewer's draw of hidden, shrunk and translucent critters and of don't-draw objects | 172, 181, 209, 178, 183, 200, 217 | M | R-1 | spell-cast and viewer |
| 6 | **E4-c** control components (first slice **done**, ARC-MAGIC-044, 2026-11-11: NoOp, Terminate, ChargeNBranch, TestNBranch on hit-point damage, [ledger CN1–CN5](ledger/spell-runs.md#control-components-arc-magic-044); **Use (record 184) is left**: it needs the viewer's script host, ledger CN6) | ChargeNBranch, TestNBranch, Terminate, Use, NoOp with the cell branch and the TC1 switch (cell weapon spends one cell; maintained armour pays a cell a tick) | 147, 168, 184, 187, 188 | L (two slices) | none | spell-cast |
| 7 | **E4-e** nested Cast | Nested Cast component | 203 | S to M | E4-c | spell-cast |
| 8 | **MA-8** Trait and polymorph (E4-f) | Trait: art and palette, drop what the new art cannot wield, restore | 222 (and the Body spells of the magic queue) | L | MA-1, MA-2 (done) | render and equipment |
| 9 | **E1-e** creature Summon with AIRedirect | Prototype or list pick, summoned flags, faction; AIRedirect (E4-b remainder) | 183 and 11 summon spells | L | E1-c (done), MA-4 (done) | Summon file |
| 10 | **T-s** smoke extension | Section 5 gaps in `tech-craft` or a sibling stage | n/a | M | none (best after 1 to 3, which add steps to it) | smoke |
| 11 | **R-2** record 214 | **Done, ARC-RES-127 (2026-11-11):** the original's effect list has no bound (ledger [EF11](ledger/character.md#durable-effects-arc-world-029)), so the NPC list was raised from 64 to 1024 entries and record 214 now RUNS; the census row is gone. Was: the record adds effect 334 eighty times; the effect list holds 64 and an NPC's write is refused (the census row says HERO). Read what the original does at overflow, then decide | 214 | S | none | read |
| 12 | **T-c** creation editor degree buttons | Check that the character-creation editor offers the degree buttons and keeps the TC10 floor; fix or close the "not confirmed" note. **Done, ARC-MAGIC-043:** the creation editor had no degree buttons (CM8 shows the page in the original); a tech page now spends and refunds through the technology lane with the snapshot floor and replays at Accept | n/a | S | none | UI |
| 13 | **E1-f** AI summoning | Magic queue's last slice; touches no technology record | n/a | M | E1-e | AI host |

Waves (a slice starts when its needs are done): **wave 0, now and in parallel:** T-m2, T-m1, E6-b (three different files: event queue and item events, created critters, trap code), R-1 and R-2 (reads) and T-c;
**wave 1:** E4-a2 (after R-1), E4-c (the spell-cast lane has one writer at a time) and the smoke extension T-s once its slice is in; **wave 2:** E4-e, MA-8, E1-e; **wave 3:** E1-f.
After rows 1 to 3 the player-facing technology list has no known hole except the control-component records; rows 4 to 5 are correctness of effects the host already runs.

## 7. Decisions for the owner

| # | Question | Options | Recommendation |
| --- | --- | --- | --- |
| 1 | Start wave 0 (T-m2, T-m1, E6-b) as three threads now? | yes / one at a time | Yes: the three files do not meet; each is bounded and unblocked |
| 2 | E6-b's timed bombs (items 15154 and 15193, records 176 and 220) were put off to a later wave by the owner's alpha order of 2026-10-27 (project memory; the order itself is not recorded in the repository docs). Fold them into E6-b, or keep them apart? | fold / separate | Fold: the delayed run at the tile is the same event as the placed device's; one slice, one stage |
| 3 | Do rows 11 to 13 (record 214 read, the creation editor check, E1-f) wait until wave 0 is done? | wait / start | Wait: none blocks play and each is small |
| 4 | Count the eye-candy line of a record (no light, sound or tint) as a gap? | no / yes | No, as RES-104 decision 4 |

The coverage rows named at the top need no change in this PR; each slice edits its own rows. RES-104 keeps the full matrix (section 5); its counts are now behind the census table, which is the live source.
