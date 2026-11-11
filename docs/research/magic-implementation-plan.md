# Magic implementation plan

Status: **Plan, mostly built** (ARC-RES-060, 2026-10-20; trimmed 2026-11-11, the finished slice rows, the reuse table and the answered questions are in git history).
The plan cut the [magic core research](magic-core-evidence.md) (ARC-RES-043) into ordered code slices, one PR each, labelled **MT1..MT21** (planning labels, not task codes; MT9 and MT20 in two parts; P1 and P2 are WORLD
prerequisites). Done, with their contracts in [spell-records](../formats/spell-records.md), `docs/world/spell-*.md` and the [magic catalogue](../project/status/magic.md): MT1 ARC-MAGIC-001, MT2 -002, MT3 -004,
MT5 -003, MT6 -005, MT7 -006, MT8 -007, MT9a-1 -009, MT9a-2 -010, MT10 -011, MT12 -012, MT19 -008, MT20a-1 -014, MT20a-2 -015, MT20b -026 (the first-cast milestone, reached 2026-10-27);
MT4 -028 (wired by -031), P1 ARC-DEV-348, P2 (created objects and critters: ARC-DEV-347, -359, -370, -371), MT14 -035, MT18 -040, -046, -047, -052; and, by the audit slices of the
[magic audit](magic-audit-res-103.md) (ARC-RES-103), parts of MT13 (ARC-DEV-302, -303, -304, -358, ARC-MAGIC-027), MT15 (-034, -044, -048, -049, -032), MT16 (-036, -038, -037) and MT21 (-029, -030).
Open: **MT11, MT13 residue, MT15 residue, MT16 residue, MT17, MT21 residue** and the MT4 residue (below). The requirements are
[FR-MAGIC-001](../requirements/combat-magic-tech.md#fr-magic-001) to [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012) (the core's part of 008 to 010), inputs from
[FR-CHAR-015](../requirements/characters-skills.md#fr-char-015), [FR-CHAR-020](../requirements/characters-skills.md#fr-char-020), [FR-CHAR-022](../requirements/characters-skills.md#fr-char-022) and
[FR-CHAR-024](../requirements/characters-skills.md#fr-char-024), and the script hooks of [FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002); the order of the remaining spell work by college is in the [magic audit](magic-audit-res-103.md)
and epics E1 and E4 of the [missing-systems plan](../project/missing-systems-plan.md).

## Rules for every slice

- **Universal.** Data comes through the content stack for any module (init-phase files from the base, as the
  [load map](combat-domain-load-map.md#9-consequences-for-steamwoven-project-policy-proposals) proposes and ARC-DATA-027
  does); critters, items and targets come from whatever map is active through the universal readers. No slice names a
  map, a location or a pinned profile; real-data tests run over the whole installation, not over Crash Site.
- **Godot-free core.** Data, Simulation and Session code never references Godot; the viewer is a host.
- **Typed results.** Refusals are typed results with a reason code (for the UI, the AI and tests), never exceptions at
  a runtime boundary.
- **Labelled randomness.** Every roll comes from the session's seeded source with a purpose label
  ([session randomness](../world/session-random-runtime.md)), including the predictor's d20 (MG14).
- **Rule modes.** Rules read the session's current `GameRules` at the decision. The first slice with a switch adds a
  `MagicAnomalyProfile` to `GameRules`; each switch has a ledger ID and a row in the
  [rule-mode registry](../world/rule-modes.md#registry). State never depends on the mode.
- **Snapshot.** A slice that adds saved state takes the next free schema number at merge time and coordinates
  through the coordinator (the NPC AI and skills lanes bump it too).
- **Records 140..222.** The spell reader reads all 223 records (one table), but technology records run only when the
  technology lane uses them.
- **Requirements.** Each slice's contract names its requirements in a `Requirements:` line (the FR column below), and
  its PR updates their rows in the [coverage matrix](../requirements/coverage.md) on the matrix's status scale
  ([rule](../workflow/tasks.md#functional-requirements)) and its row in the [component catalogue](../project/status.md).
- **Contract size.** Each slice is one result, at most three components and at most eight criteria
  ([Definition of Ready](../workflow/tasks.md#definition-of-ready)). A slice that outgrows that is split before work
  starts, with the owner's word.

## Checking against the original

Per the [original executable check](original-executable-check.md), every slice:

1. **Reads its functions** (the "Read" column below) in the retail 1.0.7.4 `Arcanum.exe` with `llvm-objdump` or
   headless Ghidra, with two independent readers, and records one row per question in a new section of the
   [verification ledger](original-verification-ledger.md) named after the slice's code. MG rows already read twice
   are not re-read; MG13, MG14, MG16, MG17 and MG20 were read once and get their second reading in the slice that
   uses them (the ledger lists MG15 both as read twice and as read once; MT10 re-reads it to be safe).
   Owner decision 2026-10-20: besides its own functions, each slice re-reads every fact of its area that is still at
   F0 (every claim of the [magic core research](magic-core-evidence.md) and the matching rows of the
   [requirements](../requirements/combat-magic-tech.md) that cite an address without a ledger row), so the domain
   leaves F0 as it is implemented; the facts it re-reads get ledger rows and their documents cite them.
2. **Marks** each implementing method with `[OriginalFunction(address, scope)]` and adds its row to the
   [function registry](../verification/functions.csv) at F2 or better.
3. **Runs the oracle** for the functions marked "oracle" below: a world model, an `OriginalExe` test with at least two
   planted mutants, a launcher row in `ORACLE_FUNCTIONS` and the receipt in `docs/verification/receipts/`, on the
   owner's Mac (`./arcanum oracle --function ADDR`). Proposed: cast cost (0x4507D0), the UI and AI cost (0x4B1660), the
   UI affordability check (0x4508A0), resistance and save (0x4537B0), the fail-chance predictor (0x453B20), the duration
   and period scaling (0x455250) and the summon list pick (0x452900). They read only a few stats, fields and random
   draws, which stubs can serve. The spell learn check (0x4B1790) is a conditional eighth, if its stubs stay small; ARC-MAGIC-004 ran it (eight one-line stubs, F3).
4. **Classifies** each difference: a reconstruction slip or the original's intended behaviour is followed in both
   modes with no switch; a bug of the original gets a decision ([below](#anomaly-decisions-accepted-2026-10-20)) and, if
   fixed, a switch; a crash or out-of-bounds access is fixed in both modes.

Data questions (record contents, component use, which items carry mana) go through the data probes and opt-in
`RealData` tests on the Mac, not through the executable.

## Open slices

"Read" lists the function groups the slice reads twice in the executable (addresses as in the research); "Decides" lists the anomaly rows it implements.

| Slice | Type | What is left | Needs | Read | Decides |
| --- | --- | --- | --- | --- | --- |
| **MT9b Saved runs** (**done**: ARC-DEV-210 schema 73, ARC-DEV-289 schema 84, ARC-MAGIC-058; [saved runs](../architecture/saved-runs.md), ledger [MX1–MX6](ledger/spell-runs.md#spell-runs-at-the-players-change-of-map-arc-magic-058)) | MAGIC | Settled runs and runs before BEGIN saved (S4, S8); a run waiting for a callback stands in Begin or End and is saved with them. The original's per-map `MT.dat`: at the player's change of map the runs that name an object that does not travel end and the others stay in the one table, so no run of a map that is not shown exists (ARC-MAGIC-058, missing-systems E4-h). Left: the re-queued deadline of a carried tick (MX5). | MT9a-2 | save and load (0x44F250, 0x44F3C0, 0x44F540, LM19), map close (0x44F7A0, 0x44FA70, 0x44FC30) | - |
| **MT11 Endings and recharge** (partly done: ARC-DEV-301, [ledger UE1-UE5](ledger/spell-runs.md#spell-run-endings-of-a-critter-arc-dev-301)) | MAGIC | Open: ending on rest and world-map travel, a follower leaving combat (0x4A8570), the summon's death ending (MT17), unwearing a source item, and the worn-store recharge event (merged per item, saved). Done: death, knockout, the AI gate's demaintain, an NPC's destroy. | MT9b | 0x459740, 0x4598D0, 0x459A20, 0x457450, 0x45D5A0 | - |
| **MT13 Effect and object flags** (nearly done) | MAGIC | The NPC's life facts do not read effect-changed strength, constitution or resistance (maximum hit points, poison tick); the viewer's draw of translucent, shrunk or hidden critters; the drowning damage of the water-walking clear (OB2). | - | flag (0x454A10, 0x455100) | - |
| **MT15 Control components** (Dispel, branches, NoOp, Terminate, Use, Cast, Identify done) | MAGIC | Left: Recharge, TraitIdx, Trait64, EnvFlag (logs only), AGoalTerminate; Interrupt is record only (MG7). Items in missing-systems E4-c and E4-d. | - | recharge (0x454920), interrupt (0x452300), stub (0x452F10) | MG7 |
| **MT16 Movement, goals, nested casts** (Movement and nested Cast done) | MAGIC | AGoal components: knockback 4 cells, knockdown, follow, other goals through an animation-goal port (hero and companion push for Unseen Force, spell 2); Destroy of critters and containers; the teleport mode's town map and waypoint journey, what a teleport does to followers (ledger "Not checked", W9). | - | goal (0x4514E0, 0x4516D0, 0x451700), destroy (0x451AF0) | - |
| **MT21 Scripts and dialog** (MT21a and MT21b done) | SCRIPT | The cast actions (81 lines, 40 live, [census](scr-census.md)), free and unresistable casts, stop spell; a dialog NPC's free cast; paid spellcasting services (100 coins); the body-of-element condition bits. | MT9a, MT3 | script (0x447220) spell branches, dialog (0x419D50), money (0x4B1650-0x4B1740) | - |

### Phase E: world components

| Slice | Type | What is left | Needs | Read | Decides |
| --- | --- | --- | --- | --- | --- |
| **MT17 Summon** (scenery done: ARC-DEV-352; created critters done: ARC-DEV-359, -370, -371) | MAGIC | Creature Summon (missing-systems E1-e, then E1-f for the AI): a prototype or a list pick (lists 0..2 weighted, 3..5 by aptitude), the summoned flags, palette, faction from `Clear_Faction:` (MG8), alignment of the parent, tracking in the run so its END or a dispel removes the summon, the run's end when the summon dies (0x459740); the familiar hidden with the party on the world map (XR14(b), [magic audit](magic-audit-res-103.md#decisions-for-the-owner)). | MT9a, P2 (done) | summon (0x450C40), list (0x452830, 0x452900 oracle); [ARC-RES-101](ledger/spell-runs.md) | MG9, summon palette |

The MT4 residue: only the hero reads the ground (ARC-MAGIC-031); an NPC's aptitude does not follow its sector, the weapon aptitude of a hero's attack reads without it, and backgrounds 66-68 are not built.


## Anomaly decisions (accepted 2026-10-20)

The owner accepted every recommendation below on 2026-10-20 and asked that every difference from the original and
every fix of a bug of the original be documented. The decided rows are recorded in the AI plan's
[anomaly decisions](npc-combat-ai-plan.md#anomaly-decisions) (original behaviour, chosen behaviour, reason, switch).
Each slice that implements a row names its switch there, adds it to the
[rule-mode registry](../world/rule-modes.md#registry) and describes the difference in its runtime contract. MG7 and
MG18 were taken up by the MT2 census (ARC-MAGIC-002): it settled MG7 (record only: no installed record has an Interrupt
component) and measured MG18, which stays record only and moves to MT8, to be classified with MG21–MG23, because whether it is a bug depends on how
the target functions test the kind bits (its row below).

ARC-MAGIC-005 (MT6), the first magic slice with a switch, brought the registry's Magic row in line and added
`MagicAnomalyProfile` to `GameRules`, as planned (the registry is read by a root test, so it changes with code, not
in a docs-only PR; ARC-MAGIC-002 added no switch): MG5, MG6, O9, O10, the charm bonus and the summon palette move to
"follow the original, no switch"; MG7 is record only; MG18 is classified in MT8; MG3, MG4, MG9, MG10, MG11, MG15 and
MG19 stay as Steamwoven fixes with Original switches. The
verdicts come from the
[ledger](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040). Under the
[rule modes](../world/rule-modes.md), a reconstruction slip and the original's intended behaviour get no switch; a bug
of the original gets a switch only if Steamwoven fixes it; a crash or out-of-bounds access is fixed in both modes.

| Row | Verdict | Decision | Slice |
| --- | --- | --- | --- |
| MG1 | slip (spell index); no mastery is the original | follow the original in both modes, no switch | MT5 |
| MG2 | slip (run-time rounding); three formulas are the original | follow the original in both modes, no switch | MT5 |
| MG3 | original bug | fix (charge only the surcharged cost), switch `MaintenancePaysBoth` (ARC-MAGIC-009) | MT9a-1 |
| MG4 | original bug | fix (add the resistance term, as the manual says), switch | MT6 |
| MG5 | original bug | keep the original in both modes: the intended formula is unknown | MT7 |
| MG6 | original bug | keep the original in both modes: the intended signed scaling is unknown | MT9a-1 (second reading ARC-MAGIC-009) |
| MG7 | original bug | record only: the MT2 census (ARC-MAGIC-002) finds no record with an Interrupt component | — |
| MG8 | slip | follow the original (`Clear_Faction:`), no switch | MT17 |
| MG9 | original bug | fix (roll 0..99), switch | MT17 |
| MG10 | original bug | fix (add the flags instead of replacing them), switch `ElementalFlagsAssigned` (ARC-MAGIC-047) | MT18 |
| MG11 | original bug | fix (count the caster's maintained runs), switch `MaintainedCountIsZero` (ARC-MAGIC-009); the AI's I8 relies on it | MT9a-1 |
| MG12 | original bug, out of bounds | fixed in both modes (owner rule 2026-10-17), "both fixed" in the registry | MT20b |
| MG13, MG14, MG16, MG17, MG20 | the original | follow it in both modes; second reading in the slice | MT5, MT6, MT10 |
| MG15 | original bug | fix (the reflected spell's own cost), switch `ReflectionPaysLastCost` (ARC-MAGIC-011) | MT10 |
| MG18 | the original (intent; ARC-MAGIC-007) | follow it in both modes: the area test runs the not-dead test for any object without the critter bit (ledger TA6), so `Tgt_Obj_No_ST_Critter_Dead` means "any object but dead ones" and Bolt of Lightning keeps hitting doors and chests; the census (ARC-MAGIC-002) showed the bit would change only records 33 and 168 | MT8 |
| MG19 | original bug | fix (separate the two flags), switch `UnresistableIsFinalEnd` (ARC-MAGIC-009; the scheduler's side and the source detach ARC-MAGIC-010); it reaches two technology records (`No_Resist:`) and the script action `cast_unresistable_spell` (7 live lines in 6 scripts); actions 121 and 122 are unused | MT9a-1, MT9a-2 |
| MG21, MG22, MG23 | the original (intent; ARC-MAGIC-007) | follow them in both modes: non-party is tested for PCs and NPCs without the critter bit, the radius bit is an object kind of its own, the tile-self bit gathers the source's tile (ledger TA6, TA10, TA11) | MT8 |
| MG24 | original bug (ARC-MAGIC-007) | fixed in both modes (no-crash rule): the target list stops at its 256 entries; no installed record reaches it | MT8 |
| MG25 | original bug (ARC-MAGIC-007) | fix (the summoned test reads the spell flag), switch `SummonedTestReadsLastFlags`; no installed cast reaches it (records 183 and 217 join the bits only in untested action areas) | MT8 |
| MG26 | original bug (ARC-MAGIC-007) | fixed in both modes (no-crash rule): a missing object has no location where the original reads through handle 0 | MT8 |
| MG27 | original bug (ARC-MAGIC-007) | keep the original in both modes, as MG5: the tile-radius test measures a tile against itself and the intended comparison is unknown; no installed cast would change. Decided under the owner's 2026-10-20 acceptance of the MG5 rule (recorded by ARC-MAGIC-008) | MT8 |
| MG28 | original bug (ARC-MAGIC-009) | fixed in both modes (no-crash rule): a full run table refuses the cast where the original quits the game | MT9a-1 |
| MG29 | original bug (ARC-MAGIC-009) | fix (owner decision 2026-10-21): no surcharge for a free run, after the stage already failed, or at a maintenance cost of 0; switch `SurchargeAlways` (ARC-MAGIC-010) | MT9a-1, MT9a-2 |
| MG30 | original bug (ARC-MAGIC-010, ledger MG30) | fix (owner decision 2026-10-21), switch `ReEntryDropsRuns` (ARC-MAGIC-011): a run the cancels interrupt while another run is processed is processed right after that run's pass | MT10 |
| MG31 | original bug (ARC-MAGIC-010, ledger MG31) | keep (owner decision 2026-10-21): follow the original in both modes, no switch — only one installed technology record (177) has CALLBACK without END_CALLBACK components on a timed run, and the run still ends with its trigger count | MT9a-2 |
| MG32 | original bug (ARC-MAGIC-010, ledger MG32) | fix (owner decision 2026-10-21), switch `FreeKeepsOtherEvents` (ARC-MAGIC-011): a free removes every event of the run | MT10 |
| MG33 | original bug (ARC-MAGIC-011, ledger MG33) | keep (owner decision 2026-10-21): follow the original in both modes, no switch — the cancels' environment loop compares the starting record's own fields, and no installed record has `Cancels_EnvSF:` | MT10 |
| MG34 | original bug (ARC-MAGIC-011, ledger MG34) | fix (owner decision 2026-10-21), switch `ResistedReflectionKeepsSwap` (ARC-MAGIC-013): a target the reflection swapped and the resistance skipped gets the run's source restored, as an applied target does | MT10 |
| MG35 | original bug (ARC-MAGIC-012, ledger MG35) | fix (owner decision 2026-10-21), switch `DifficultyScalesKillDamage`: a PC's full or death hit takes the current hit points without the difficulty share, so it kills on hard too | MT12 |
| MG36 | original bug (ARC-MAGIC-012, ledger MG36) | fix (owner decision 2026-10-21), switch `SpellDamageUsesCastersWeapon`: a spell's damage step has no weapon, so the caster's weapon flags (ignore resistance, "uber", damage armour) do not reach it | MT12 |
| MG37 | original bug (ARC-MAGIC-012, ledger MG37) | fix (owner decision 2026-10-21), switch `MarginTakesFromNoDamage`: a margin takes nothing from an amount of 0 or less | MT12 |
| MG38 | original bug (ARC-MAGIC-012, ledger MG38) | fix (owner decision 2026-10-21), switch `AcidIgnoresBodyArmour`: a wearer who wears body armour is spared | MT12 |
| MG39 | original bug (ARC-MAGIC-014, ledger MG39) | fix (owner decision 2026-10-27), switch `GoalRefusalChargesBegin`: a casting goal refused before its animation frees its run unpaid | MT20a-1 |
| MG40 | original bug (ARC-MAGIC-014, ledger MG40) | fix (owner decision 2026-10-27), switch `GoalEndChargesAnyRun`: a run freed before its BEGIN is charged only when its BEGIN would have paid | MT20a-1 |
| MG41 | original bug (ARC-MAGIC-014, ledger MG41) | technical bug, a slot leak (owner decision 2026-10-28, ARC-WORLD-108): fixed in both modes, the switch `ReleaseRefusalKeepsRun` removed; a run whose target fails the release re-check is freed unpaid | MT20a-1 |
| MG42 | original bug (ARC-MAGIC-015, ledger MG42) | fix without a switch (owner decision 2026-10-27, presentation only): the cursor's "not damaged" message only for the current pick's reason | MT20a-2 |
| MG43 | original bug (ARC-MAGIC-019, ledger MG43) | fix in both modes without a switch (owner decision 2026-10-28): queued casts whose goals never play are freed unpaid | MT20a-2 leftover |
| MG44 | original bug (ARC-MAGIC-018, ledger MG44) | fix in both modes (owner decision 2026-10-28: fixed in both rule modes, no switch): a run whose missile is lost before it arrives (a leg's line refused, the goal stopped) is freed unpaid | MT20a-2 leftover |
| MG45 | original bug (ARC-MAGIC-021, ledger MG45) | technical bug (owner rule 2026-10-28): fixed in both modes without a switch — no flag is written (latent: only an object whose data carries `OF_CLICK_THROUGH` could lose it) | MT20a-2 leftover |
| MG46 | original bug (ARC-MAGIC-024, ledger MG46, IC9) | gameplay bug (owner's bug rule 2026-10-28): the original behind the Original switch `ItemRunDamageFiresHooks`; Steamwoven (owner decision 2026-10-28, ARC-MAGIC-025: the narrow fix) lets each item fire at most once in a chain of item spells, keeping every other item's answer | FR-MAGIC-011 |
| MG47 | original bug (ARC-WORLD-108, ledger NU4) | gameplay bug (owner's bug rule 2026-10-28): the original behind the Original switch `AiItemCastClearsAnySource` (perform's cast with an item clears the sources of any item's runs, so an NPC's wand cast pays no charge); Steamwoven clears them only for the food or scroll it destroys | FR-MAGIC-011 |
| MG48 | original bug (ARC-DEV-303, ledger ER8) | gameplay bug (owner's bug rule 2026-10-28): the original behind the Original switch `ReResistReadsCachedTargetType` (the entangle's re-resistance reads the target type the run processing last cached, so after an action with no PC or NPC target it holds on every hit until a later action meets one); Steamwoven asks the struck critter's own type | FR-MAGIC-005, FR-MAGIC-007 |
| O9 magic resistance source | reconstruction change | the original (stored value, no armour, effects or clamp) in both modes | MT6 |
| O10 shell and reflection order | reconstruction change | the original in both modes | MT10 |
| Charm asymmetry | reconstruction change confirmed | the original (+30 and −30 both through the reaction effects) | MT14 |
| Summon palette | reconstruction change | the original (palette on the current art only) | MT17 |

The [rule-mode registry](../world/rule-modes.md#registry)'s Magic row also names LM10 (item effect module maximum, the
items owner), effect `min`/`max` (decided in `EffectRules`, durable effects) and LM20 (blessing and curse names, fixed
in both modes); none of them is a slice of this plan.


## Boundaries with other lanes

Agreed through the coordinator on 2026-10-20. **Skills** owns the shared character-point transaction and the fate owner ([fate points](../world/fate-points-runtime.md)); magic spends points through the first and reads and uses fate as armed-fate inputs. The aptitude formula stays in `CharacterStatRules`; skills owns the tech points from tech skills, magic owns the magick points from spells, the sector adjustment and the mage backgrounds.
**Technology** (ARC-RES-062) owns tech degrees, schematics, tech items (records 140..222 as their effects), the charge-cell source for `ChargesFromCells:` and `ChargeNBranch`, device failure from magick against technology, and the tech UI; the engine (run pipeline, the 25 components, payment, resistance) is this plan's.
**NPC AI** (ARC-RES-039) slice I8 consumes the cast rules ([what I8 needs](magic-core-evidence.md#what-ai-slice-i8-needs-from-this-domain)) and MT17 for summoning; line of fire comes from the AI awareness stage through a port.

## Outside this plan

- **Item spells** (FR-MAGIC-011): the item trigger executor (0x4CB830) and its callers serve magic and technology items alike. Done: wear, unwear, pickup, drop, item use, the combat, life and attack events
  ([ARC-MAGIC-020 to -025](../world/item-spell-triggers.md), [ledger IT1-IT10](ledger/spell-runs.md#item-spell-triggers-arc-magic-020)), the item spell pane ([ARC-UI-016](ledger/spell-runs.md#item-spell-pane-arc-ui-016)).
  Left: inventory-target spells and hotkeys (0x57C080, IP7-IP9).
- **Technology runtime**: the technology lane above.
- **Spell visuals**: the cast, release and component eye candy, the missile's art and flight and the caster's turn are done ([ARC-MAGIC-017](../world/spell-cast-host.md#eye-candy-arc-magic-017), [-018](../world/spell-cast-host.md#the-missile-arc-magic-018));
  the missile's light and sound remain.
