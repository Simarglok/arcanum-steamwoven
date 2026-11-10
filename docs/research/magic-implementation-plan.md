# Magic implementation plan

Status: **Plan**, work item ARC-RES-060 (2026-10-20), top-20 item 12; the owner approved starting the slices on 2026-10-20,
and MT1, MT2, MT3, MT5, MT6, MT7, MT8, MT9a-1, MT9a-2, MT10, MT12, MT19, MT20a-1 and MT20a-2 are done as ARC-MAGIC-001, ARC-MAGIC-002, ARC-MAGIC-004, ARC-MAGIC-003, ARC-MAGIC-005, ARC-MAGIC-006, ARC-MAGIC-007, ARC-MAGIC-009, ARC-MAGIC-010, ARC-MAGIC-011, ARC-MAGIC-012, ARC-MAGIC-008, ARC-MAGIC-014 and ARC-MAGIC-015, so the first cast (milestone below) is reached. The owner split MT9a into MT9a-1 (ARC-MAGIC-009, run creation and the payment stage, which the technology lane waits on) and MT9a-2 (ARC-MAGIC-010, the scheduler and interrupts) on 2026-10-21; "MT9a" below means both. It cuts the
[magic core research](magic-core-evidence.md) (ARC-RES-043) into ordered code slices, one PR each, and says for every
slice what it builds, which requirements it covers, what it depends on, what it reuses and how it is checked against the
original executable. It replaces the eight slices proposed at the end of that research
([proposed implementation slices](magic-core-evidence.md#proposed-implementation-slices-project-policy-not-accepted)).
Codes are allocated when each task is created (owner decision D6 of
the [NPC combat AI plan](npc-combat-ai-plan.md#owner-decisions-2026-10-16)), so slices carry plan labels **MT1..MT21**
(MT9 and MT20 in two parts each) here, not codes. Of the other lanes named below, the [skills plan](skills-plan.md) (ARC-RES-061) is merged
and the technology research (ARC-RES-062) is in flight. Docs only: no code, reader, schema or test changes.

Requirements: [FR-MAGIC-001](../requirements/combat-magic-tech.md#fr-magic-001) to
[FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007) in full; the core's part of
[FR-MAGIC-008](../requirements/combat-magic-tech.md#fr-magic-008) (summons),
[FR-MAGIC-009](../requirements/combat-magic-tech.md#fr-magic-009) (teleport),
[FR-MAGIC-010](../requirements/combat-magic-tech.md#fr-magic-010) (magic against technology) and
[FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012) (magic UI); inputs from
[FR-CHAR-015](../requirements/characters-skills.md#fr-char-015) (points),
[FR-CHAR-020](../requirements/characters-skills.md#fr-char-020) (aptitude),
[FR-CHAR-022](../requirements/characters-skills.md#fr-char-022) (effects) and
[FR-CHAR-024](../requirements/characters-skills.md#fr-char-024) (fate);
script hooks of [FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002).
[FR-MAGIC-011](../requirements/combat-magic-tech.md#fr-magic-011) (item triggers) is a later shared slice
([Outside this plan](#outside-this-plan)).

## Answer

1. **23 slices in six phases** (MT1–MT21, with MT9 and MT20 split in two): data (MT1–MT4), cast rules (MT5–MT8,
   pure functions), runs (MT9a–MT11, session),
   components (MT12–MT15), world components (MT16–MT18) and host (MT19–MT21). Two prerequisites belong to the WORLD
   type: per-critter magic state for every NPC (P1) and creating objects at run time (P2).
2. **First visible result** ("first cast"): the hero learns a spell in the character sheet and casts a damaging or
   healing spell at a critter in the AREA viewer, on any map: MT1, MT2, MT3, MT5–MT8, MT9a-1, MT9a-2, MT10, MT12, MT19,
   MT20a-1 and MT20a-2 (the owner split MT20a on 2026-10-21), fourteen PRs (plus the skills lane's point transaction). The first cast uses a damage or heal spell without
   `Duration:` or `Maintain:`, which ends right after BEGIN, so it needs no saved runs (MT9b) and no maintain bar (MT20b). MT4 and MT11 can run beside them.
3. **Most foundations exist**: the session clock, saved event queue, labelled seeded randomness, life state (hit points,
   fatigue, knockout, death, heal, resurrect), the attack resolver's typed damage and resistances, the hero's effect
   list, equipment, reaction memory, character stats with magick points and aptitude, auto-level spell purchases with
   the spell order checks, the critter field reader with college levels and mastery, rule modes and the function
   registry ([Reuse](#what-the-slices-reuse)). The missing ones are spell data, the cast pipeline, active runs,
   NPC effect lists and spell flags (P1), and run-time object creation (P2).
4. **Every slice reads its functions in the executable before merge** (two readers, ledger rows), because since
   ARC-DEV-025 no implemented function stays at F0; seven pure functions are proposed for the function oracle (F3)
   ([Checking against the original](#checking-against-the-original)).
5. **Fifteen rows were decided by the owner on 2026-10-20** (eleven bugs of the original, four reconstruction
   changes): every recommendation was accepted ([Anomaly decisions](#anomaly-decisions-accepted-2026-10-20)); the MT2
   census settled MG7 (record only) and moved MG18 to MT8 with MG21–MG23. MG12 is fixed in both modes by the owner's no-crash rule; MG1, MG2, MG8, MG13, MG14, MG16, MG17 and MG20 follow the
   original and need no decision.
6. **Boundaries agreed with the parallel lanes (2026-10-20)**: skills builds the shared character-point transaction
   (slice S5 of the skills plan) and the fate owner (S7); technology (ARC-RES-062) owns tech degrees, schematics,
   charge cells and tech items; NPC AI slice I8 consumes MT1–MT13, and MT17 for summoning
   ([Boundaries](#boundaries-with-other-lanes)).

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

## What the slices reuse

| Existing component | Used by | How |
| --- | --- | --- |
| [`MesFile`](../../src/Arcanum.Data/Messages/MesFile.cs) and the content stack phases (ARC-DATA-027) | MT1, MT2 | `spelllist.mes`, `spell.mes`, `magictech.mes` from the base at init |
| `AutoLevelTables.ReadSpellWillpower` ([advancement](../world/advancement-runtime.md)) | MT1, MT3 | replaced by the full record reader; auto-level keeps its input |
| `AutoLevelRules` spell purchase (AL6: order, level 1/1/5/10/15, INT 5, WP) | MT3 | its checks move into one spell-book rule both auto-level and the UI call |
| `SessionHeroAdvancement.Colleges`, NPC advancement sheets (schema 20) | MT3 | college levels already saved; MT3 adds mastery |
| `CombatFieldReader` / `CombatFields` ([combat fields](../formats/combat-fields.md)): `SpellTech` (colleges, mastery), item `ManaStore`, `Spells`, `SpellManaStore`, object `SpellFlags`, resistances | MT3, MT5, MT6, P1 | NPC spell books, worn mana stores, item charges, stored magic resistance (O9) |
| [`CharacterStatRules`](../world/character-stats-runtime.md): magick points 0..210, aptitude with a sector input | MT3, MT4, MT6 | aptitude stays there; MT4 supplies the sector adjustment |
| [Life state](../world/life-state-runtime.md) (`LifeRules`, session life domain) | MT5, MT11, MT12 | fatigue payment and fainting, damage, heal, resurrect, death hooks |
| [Attack resolver](../world/attack-resolver-runtime.md) damage packet, resistances, immunities | MT12 | spell damage enters the same damage step |
| [Durable effects](../world/durable-effects-runtime.md) (EF1–EF10) | MT13, MT9a | Effect component; the timed-effect END hands over to runs (its "Not ready" says so) |
| [Event queue](../world/game-event-queue-runtime.md), [clock](../world/game-clock-runtime.md) | MT9a, MT9b, MT11 | a saved magic-tech event kind and the item recharge kind on game time (`seconds × 8000` ms, ledger TE1) |
| [Equipment](../world/equipment-runtime.md) | MT5, MT11 | worn slots for mana stores; unwear ends item runs |
| [Reaction](../world/npc-reaction-runtime.md) | MT14 | charm ±30 and mind control −50 through the reaction owner |
| [Party and follow](../world/party-runtime.md) | MT11, MT14 | follower leaving combat demaintains; mind control follows and disbands |
| [Turn-based round](../world/turn-based-round-runtime.md) (`CastSpellCost = 4`), [real-time combat](../world/real-time-combat-runtime.md) | MT20a-2 | casting costs 4 AP; the BEGIN fires on the release frame |
| The session's map change and the map catalogue; `AreaMapTransfers` (ARC-WORLD-031) as the viewer host's part | MT16 | teleport as a map change, anti-teleport areas |
| [SCR dispatcher](../scripting/scr-dispatcher-runtime.md) admission sets | MT15, MT21 | Use component, spell conditions and actions |
| [Rule modes](../world/rule-modes.md), [function registry](../verification/README.md), oracle (ARC-DEV-007) | all | switches, marks, receipts |

## Slices

Phase and order follow the dependencies; slices in one row of "Needs" that do not depend on each other can run in
parallel. "Read" lists the function groups each slice reads twice in the executable (addresses as in the research);
"Decides" lists the anomaly rows the slice implements under the accepted decisions, settles (MG7), measures (MG18) or re-reads.

### Phase A: data and character

| Slice | Type | Result | FR | Needs | Read / oracle | Decides |
| --- | --- | --- | --- | --- | --- | --- |
| **MT1 Spell records** (done: ARC-MAGIC-001, [contract](../formats/spell-records.md)) | MAGIC | `Arcanum.Data.Magic` reader of `spelllist.mes` lines +0..+4 into immutable records (every field of the [definitions table](magic-core-evidence.md#definitions-fr-magic-001): IQ, cost, maintain, duration, stat info, trigger count, resist, range, charges, info, flags, cancel and disallow sets, item triggers, ten `AI_*`), `spell.mes` names and college texts, `magictech.mes` component names; strict and like-original reader modes (load-map policy 2), typed refusals. Contract `docs/formats/spell-records.md`. | 001 | — | parse (0x44FE30, 0x44FFA0, 0x450090, 0x4501D0, 0x457580, 0x4578F0, 0x457D00, 0x457E70); LM rows already read stay | — |
| **MT2 Components and target areas as data** (done: ARC-MAGIC-002, [contract](../formats/spell-records.md#components-lines-5-onward)) | MAGIC | Component lines (+5 to the first gap) for the five actions: `AoE:` over the 65 target names, spell-flag filters, `Radius:`, `Count:`, and the 25 component types with their keys, typed per type. `RealData` census of what [census U3](combat-data-census.md#unknowns) left open: component types per action, `Dmg_Flags:`, summon lists and `Clear_Faction:`, `No_Stack:` and cancel sets, `No_Resist:` with `Maintain:`/`Duration:` (sizes MG19), records 80..139. | 001, 003, 007 (data) | MT1 | components (0x458060), target table (0x596140) | MG7 (record only); MG18 measured, classified in MT8 |
| **MT3 Spell book** (done: ARC-MAGIC-004, [contract](../world/spell-book-runtime.md); learn oracle F3) | MAGIC | One spell-book rule (learn rank r, unlearn the highest, mastery once with all five, re-validation after a stat decrease) that auto-level and the UI share; magick points +1/−1 per rank; mastery saved for the hero and NPC sheets (schema bump); NPC books from the critter fields of any map. Spends through the shared point transaction of the skills plan's S5. | 002; FR-CHAR-015 | MT1, the skills plan's S5 | learn/unlearn (0x4B1530–0x4B1A40), checks (0x4B1B90, 0x4B1C00), mastery (0x4B1C70, 0x4B1CB0, 0x4B1CF0); oracle candidate for learn (0x4B1790) if the stubs stay small | — |
| **MT4 Aptitude inputs** | MAGIC | The sector aptitude adjustment of every sector (the input `CharacterStatContext.SectorAptitudeAdjustment` has no source yet) and the day, night, sky and nature mage backgrounds' ±4 magick points at read time. | FR-CHAR-020; 005 | — | sector (0x45A060), backgrounds branch of the stat getter (0x4B0490) | — |

### Phase B: cast rules (pure functions in `Arcanum.Simulation`)

| Slice | Type | Result | FR | Needs | Read / oracle | Decides |
| --- | --- | --- | --- | --- | --- | --- |
| **MT5 Cost and payment** (done: ARC-MAGIC-003, [contract](../world/spell-cost-runtime.md)) | MAGIC | The three cost formulas as the original has them (run time `ceil(cost / 2)` for mastery then dwarf ×2; UI and AI with truncation; UI check without mastery, MG2); the pre-animation affordability check on the cast spell (MG1); a payment plan: free runs, `ChargeBeginCost: 0`, fatigue-limited half, worn mana stores first (MG20), fatigue refusal at −15, fail-after-pay for maintained or untimed spells, item spell charges; the charge-cell source (records with `ChargesFromCells:`) through a port the technology lane fills, taken first in the payment, before the fatigue-limiting, mana-store and fatigue steps, as the original's payment (0x450420) does; its amount and refusal are the technology lane's rules (its item-source surcharge is the technology research's (ARC-RES-062) anomaly TC1); the worn-store recharge amount. | 004 | MT1 | cost (0x4507D0, oracle), pay (0x450420), checks (0x4508A0 oracle, 0x450940), spell cost (0x4B1650, 0x4B1660 oracle, 0x4B16C0), recharge (0x459590, 0x459640, 0x459680) | MG20 (2nd reading only) |
| **MT6 Resistance and save** (done: ARC-MAGIC-005, [contract](../world/spell-resistance-runtime.md); resistance and predictor oracle F3; MG4 switch) | MAGIC | Per-target resolver returning the margin or a skip: stored magic resistance (O9), the tech-aligned raise, d100 margin, `Resist:` save d20, extraordinary willpower immunity, damaging floor 50, the fate save as an input (MG13); the fail-chance predictor exactly as the original, with its own labelled d20 (MG14); technology records skip it. | 005, 010 | MT1, fate input (the skills plan's S7 later) | resist (0x4537B0, oracle), fail chance (0x453B20 oracle, 0x453CC0) | MG4, O9 |
| **MT7 Invocation check** (done: ARC-MAGIC-006, [contract](../world/spell-invocation-runtime.md); range, flags and item gate oracle F3; MG5 kept in both modes) | MAGIC | The check before any slot or payment, with a typed refusal per step: turn, spell number, range (Chebyshev, inventory always in range), line of fire through a port (filled by AI awareness I2 or a later LOS slice), forbidden flags, Bonds of Magick, NPC fallback radius 2, item aptitude gate and holder-can-use (0x456A10), intelligence 5, anti-teleport. | 003, 009 (gate), 010 | MT1, MT2 | check (0x4564E0), range (0x454700), LOS (0x456BC0), flags (0x456430), gate (0x456A10) | MG5 |
| **MT8 Target areas** (done: ARC-MAGIC-007, [contract](../world/spell-target-areas.md); area test, collection, re-aim and AI notice oracle F3; MG18, MG21–MG23 intent; MG24, MG26 fixed in both modes; MG25 switch; MG27 kept as MG5) | MAGIC | Target lists for an action's area over a world query port (objects and tiles of the active map, party, parent, attacker, the run's object list) and the per-component filter and re-aim; reads the cone, count and radius branches the research left open (O3). | 003 | MT2 | targets (0x4F40B0) and the target functions it calls (0x4F24F0–0x4F4E40), apply area (0x453D40, 0x453EE0, 0x453F20) | MG18, MG21, MG22, MG23 (recorded by ARC-MAGIC-002) |

### Phase C: active runs (`Arcanum.Session`)

| Slice | Type | Result | FR | Needs | Read / oracle | Decides |
| --- | --- | --- | --- | --- | --- | --- |
| **MT9a-1 Run creation and payment stage** (done: ARC-MAGIC-009, [contract](../world/spell-run-rules.md); time, stage and cap oracle F3; MG3, MG11, MG19 switches; MG6 kept; MG28 fixed in both modes; MG29 switch (owner decision 2026-10-21, ARC-MAGIC-010); split approved by the owner 2026-10-21) | MAGIC | The run table (512 slots); creation (0x455A20, 0x455AC0, the rules of 0x455C30) with the MT7 check before a slot; the stage 0x452F20 at BEGIN, MAINTAIN and END with the payments through `SpellPaymentRules.Pay` (MT5): technology records skip the checks and payment only at BEGIN; no-stack and cancels through a port for MT10; the time scaling 0x455250, the duration (`DurationStatInfo:`, trigger counts) and maintenance intervals, mastery ×2, × 8; the cap 0x4545E0 with the slot counters (`INT / 4`). | 006 | MT5, MT6, MT7 | stage (0x452F20, 0x4532F0, 0x453370), run (0x455740, 0x4558D0, 0x455A20, 0x455AC0, 0x455C30), time (0x455250, oracle), cap (0x4545E0, oracle; 0x450AC0, 0x450B40, 0x450B90), timer interval (0x454790) | MG3, MG6, MG11, MG19 |
| **MT9a-2 Scheduler and interrupts** (done: ARC-MAGIC-010, [contract](../world/spell-run-rules.md#processing-and-scheduling-arc-magic-010); split approved by the owner 2026-10-21; scheduler and interrupt action oracle F3; MG19 scheduler side; MG29 switch; MG30–MG32 decided by the owner 2026-10-21 (MG30, MG32 fixes, MG31 keep); the charge-cell maintenance tested end to end) | MAGIC | The processing (0x4510F0) of BEGIN, MAINTAIN, END, CALLBACK and END_CALLBACK through a component executor port with the target lists of MT8 and a per-target port for MT10; the scheduler's order (unresistable first, nothing applied, maintained, timed, neither) and its sourceless ticks (8) with MT9a-1's intervals, cap and trigger counts; explicit interrupt (maintain bar, script stop); the magic-tech event kind on the session's queue; the processing's re-entrancy (ledger RN8); the end-to-end test that a maintained technology record pays charge cells tick after tick over game time (ARC-TECHNOLOGY-008's leftover, [tech devices](../world/tech-devices-runtime.md#maintain-with-charge-cells-cc7)). | 006 | MT9a-1 | process shell (0x4510F0), schedule (0x453FA0), timers (0x4547F0, 0x4548D0), interrupt (0x456F70–0x457530), event handler (0x459430) | MG19 (its scheduler side) |
| **MT9b Saved runs** (partly done: ARC-DEV-210 saves the runs with BEGIN processed, the maintain bar and the runs' `magic.run` events as schema 73; runs before BEGIN or waiting for a callback and the runs of maps that are not active are not saved yet, [saved runs](../architecture/saved-runs.md)) | MAGIC | Runs and their events saved in the snapshot (schema bump), including runs of maps that are not active (the original's per-map `MT.dat`), and restored as the original's post-load does. | 006 | MT9a-2 | save and load (0x44F250, 0x44F3C0, 0x44F540, LM19), map close (0x44F7A0, 0x44FA70, 0x44FC30) | — |
| **MT10 Per-target stage** (done: ARC-MAGIC-011, [contract](../world/spell-target-stage.md); per-target stage, no-stack and cancels oracle F3; MG15 switch; MG16, MG17, O10 original; MG30, MG32 switches; MG33 keep; MG34 switch ARC-MAGIC-013; all decided by the owner 2026-10-21) | MAGIC | Between the area and the components: resistance per target (MT6), anti-magic shell on source and target (target spell flags from the static object fields until P1 brings them into the session), full reflection with the source/target swap and its cost (MG15), the original's order (O10), no-stack after payment (MG16), cancels by `Cancels_SF:` and `Cancels_EnvSF:`, fate maximisation per action (MG17). | 005, 006, 010 | MT6, MT8, MT9a | stack and cancel (0x453410, 0x4534E0), shell (0x453710), reflect (0x455550), env (0x458A80) | MG15, MG16, MG17, O10 |
| **MT11 Endings and recharge** | MAGIC | Every ending trigger of the caster and target: the caster fainting from payment or damage (0x45D5A0 → demaintain 0x457450), rest, world-map travel, a follower leaving combat (0x4A8570), death or destruction of parent, source item or target (0x459740, 0x4598D0, 0x459A20), unwearing a source item; the worn-store recharge event (merged per item, saved). The summon's death ending is MT17's, the entangle re-resist MT13's. **Done (ARC-DEV-301):** a critter's death and loss of consciousness, the AI gate's demaintain and an NPC's destroy end the runs through the cast host ([ledger UE1–UE5](ledger/spell-runs.md#spell-run-endings-of-a-critter-arc-dev-301)); rest, world-map travel and the summon variant are still open. | 006; FR-COMBAT-010 | MT9b, MT10 | the listed addresses | — |

### Phase D: components

| Slice | Type | Result | FR | Needs | Read / oracle | Decides |
| --- | --- | --- | --- | --- | --- | --- |
| **P1 Critter magic state** | WORLD | Every critter of any map gets an effect list and object spell flags in the session (loaded from its map and prototype fields, saved as map-local state, snapshot bump), so components can reach NPCs; the hero's existing list stays as is. | FR-CHAR-022; 007 | — | effect functions already read (EF1–EF10); spell-flag field read and write | — |
| **MT12 Damage and Heal** (done: ARC-MAGIC-012, [contract](../world/spell-damage-heal.md); Damage, Heal and acid oracle F3; DF4 data fix; MG35–MG38 switches, decided by the owner 2026-10-21; MG34 in ARC-MAGIC-013) | MAGIC | Damage (typed, `Dmg:` range, `Scaled` by the aptitude captured at cast, `Full` fatigue, `Dmg_Flags:` passed to the combat damage step, margin reduction, death flag cancelled by a margin) and Heal (hit points, poison, fatigue, `Full`, resurrect and reanimate with the resurrect-script veto as a port, scars) for any critter. It also lands data fix DF4: record 221's repeated `Dmg_Flags:` loses `Scaled` (ARC-MAGIC-002 census); under the owner's data-fix rule (ARC-DATA-022) Steamwoven restores it, Original keeps the loss, with its ledger row and `DataFixProfile` switch (the data-fix tests use DF4 as an unknown id today and change with it). | 007; FR-COMBAT-008, FR-COMBAT-010 | MT9a, MT10 | damage (0x4518D0), heal (0x4521A0), combat heal (0x4B58C0) | — |
| **MT13 Effect and object flags** (hold flags done: ARC-DEV-302, [hold flags](../world/spell-cast-host.md#hold-flags-arc-dev-302): ObjFlag for entangled, stoned and paralysed with the reference count, the walk interrupt, the step and plan refusals and D8; spoken with dead done: ARC-DEV-304, [spoken with dead](../world/spell-cast-host.md#spoken-with-dead-arc-dev-304): spell 56's flag 0x2000000, read by the click, the can-speak head and the speaker presence; Effect component for the hero and the entangle's re-resistance on weapon hits done: ARC-DEV-303, [Effect and re-resistance](../world/spell-cast-host.md#effect-component-and-the-entangle-re-resistance-arc-dev-303), ledger ER1–ER9, MG48; NPC effects, the spell-damage hit and the other flags open) | MAGIC | Effect add and remove with `Count:` and `Scaled:` (remove-first by number, ledger "Effect remove risk"); ObjFlag with reference counting over active runs; render and movement side effects: invisible, shrunk, water walking (death on a blocking tile), stoned, entangled (re-resisted on each hit), paralysed, "don't draw". | 007; FR-CHAR-022 | MT9a, P1 | effect (0x451F20), flag (0x454A10, 0x455100), entangle (0x459A30, 0x459C10) | — |
| **MT14 Social and AI flags** | MAGIC | The object flags that reach other domains: charm (+30 on, −30 off, both through the PC's reaction effects, as the original), mind control (follow without flee, −50 for a PC on a non-summoned NPC, disband at the end), undead, fleeing and blinded as AI and combat notifications, unlocking against the lock with the guard alert. | 007 | MT13 | flag (0x454A10) branches, lock (0x4551C0), reaction adjustment (0x4C0DE0) | charm asymmetry |
| **MT15 Control components** | MAGIC | Dispel, Interrupt (MG7), Terminate, TestNBranch, ChargeNBranch (charge source from the technology port), Noop, EnvFlag (logs only), Identify, Recharge, TraitIdx, Trait64, Use (the target's use script through the SCR dispatcher), AGoalTerminate. Split candidate: if the contract exceeds eight criteria, the branch and control components go first and Identify, Recharge, TraitIdx, Trait64 and Use second. | 007 | MT9a, MT13 | dispel (0x451AF0, 0x451B90, 0x451BB0, 0x451C40), interrupt (0x452300), branch (0x4529D0, 0x4529F0), recharge (0x454920), stub (0x452F10) | MG7 |

### Phase E: world components

| Slice | Type | Result | FR | Needs | Read / oracle | Decides |
| --- | --- | --- | --- | --- | --- | --- |
| **MT16 Movement, goals, nested casts** | MAGIC | Movement: to the target tile, a random tile in a radius, a world-map teleport or the nearest known area, through the universal map transfer, refused in anti-teleport areas; what a teleport does to followers afterwards (ledger "Not checked", W9). AGoal: knockback 4 cells, knockdown, follow, other goals through an animation-goal port. Cast: the parent casts another spell through the full check. Destroy. Split candidate: teleport with followers apart from AGoal, Cast and Destroy. | 007, 009 | MT7, MT9a, MT10 | movement (0x4524C0, 0x452650), goal (0x4514E0, 0x4516D0, 0x451700), cast (0x451740, 0x451850), destroy (0x451AF0) | — |
| **P2 Objects created at run time** | WORLD | The session creates a critter from a prototype on a tile of the active map, saves it as map-local state and removes it again; also what monster generators (FR-AI-004) will need. | FR-AI-004 (part); 008 | — | object create and destroy paths the summon calls | — |
| **MT17 Summon** | MAGIC | Summon a prototype or a list pick (lists 0..2 weighted, 3..5 by aptitude), the summoned flags, palette, faction from `Clear_Faction:` (MG8, a reconstruction slip), alignment of the parent, tracking in the run so its END or a dispel removes the summon, and the run's end when the summon dies or is destroyed (0x459740). | 008 | MT9a, P2 | summon (0x450C40), list (0x452830, 0x452900 oracle) | MG9, summon palette |
| **MT18 Polymorph** | MAGIC | Trait: drop what the new art cannot wield, monster art and palette, elemental flags (MG10), restore with `-1`. | 007 | MT13 | trait (0x452AD0, 0x452B00, 0x452CD0, 0x452D80, 0x452E40, 0x452ED0) | MG10 |

### Phase F: host and UI

| Slice | Type | Result | FR | Needs | Read / oracle | Decides |
| --- | --- | --- | --- | --- | --- | --- |
| **MT19 Spell book in the character sheet** (done: ARC-MAGIC-008, [viewer](../rendering/terrain-viewer.md#spells-page-arc-magic-008); college page, hints, + and − through the pool, ledger SG1–SG8) | UI | Colleges, ranks and mastery in the character sheet (ARC-UI-007); learn and unlearn through MT3 with the refusal text of the failing check. L2 native smoke. | 012; FR-UI-004 | MT3 | UI learning (0x57C540, 0x57C670) | — |
| **MT20a-1 Cast host** (done: ARC-MAGIC-014, [contract](../world/spell-cast-host.md); the owner split MT20a on 2026-10-21) | MAGIC | The spell data in the rules' types, the world the run rules ask over the session and any map (target functions, line of fire, run host, damage world, the event pass's magic world), the casting goal as the original's rules (4 AP in turn-based mode, the pre-animation check, BEGIN at the release frame with the target re-check, the end that frees a run charged) and the first cast without a window on the installed original. MG39–MG41 switches, decided by the owner 2026-10-27. | 003; FR-COMBAT-003 | MT7, MT9a, MT10, MT12, MT19 | casting goal (0x429B50–0x429CD0), pre-animation check (0x450940), release re-check (0x456A90) | — |
| **MT20a-2 Casting in the AREA viewer** (done: ARC-MAGIC-015, [contract](../world/spell-cast-host.md#the-viewer-arc-magic-015); the owner split MT20a on 2026-10-21) | UI | Spell choice, targeting cursor, the cast goal's animation through MT20a-1's host (the release frame's BEGIN), feedback texts of `spell.mes`, eye candy as presentation events, the predictor (MT6). L2 native smoke of the first cast; owner look-and-feel check as a separate owner-validation item. | 012, 003; FR-COMBAT-003, FR-COMBAT-006 | MT20a-1 | spell UI (0x57BC70, 0x57BFF0–0x57C110), the goals' nodes and eye candy (CG1, CG8) | — |
| **MT20b Maintain bar** (done: ARC-MAGIC-026, [contract](../world/spell-cast-host.md#the-maintain-bar-arc-magic-026); the bar is not saved until MT9b) | UI | The `INT / 4` slots of the PC's maintained runs with stop (MG12 fixed in both modes: it is an out-of-bounds write). L2 native smoke. | 012, 006 | MT20a-2, MT9b | maintain bar (0x57C290–0x57C4B0) | MG12 |
| **MT21 Scripts and dialog** | SCRIPT | SCR conditions `knows_spell`, `mastered_spell_college`, `obj_is_influenced_by_spell`, `obj_has_body_spell`; actions cast spell on (81 lines, 40 live, [census](scr-census.md)), free and unresistable casts, give mastery, stop spell; a dialog NPC's free cast; paid spellcasting services (100 coins). | FR-SCRIPT-002; 002, 006 | MT9a, MT3 | script (0x447220) spell branches, dialog (0x419D50), money (0x4B1650–0x4B1740) | — |

### Milestones

| Milestone | Slices | What the owner can see |
| --- | --- | --- |
| First cast | MT1, MT2, MT3, MT5, MT6, MT7, MT8, MT9a-1, MT9a-2, MT10, MT12, MT19, MT20a-1, MT20a-2 (all done; reached 2026-10-27 with ARC-MAGIC-015, observed by its native smoke) | The hero learns a spell and casts a damaging or healing spell at a critter on any map; fatigue drops; the target resists or takes damage. |
| Magic core | + MT4, MT9b, MT11, MT20b, P1, MT13, MT14, MT15, MT21 | Maintained spells with the bar, buffs and curses on NPCs, charm and mind control, dispel, scripted casts; FR-MAGIC-001..007 covered. |
| World magic | + MT16, P2, MT17, MT18 | Teleport, summons, polymorph. |

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
| MG10 | original bug | fix (add the flags instead of replacing them), switch | MT18 |
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

Agreed through the coordinator on 2026-10-20:

- **Skills ([ARC-RES-061](skills-plan.md))**: one character-point pool, the session's unspent points; learning or
  unlearning a rank spends or refunds through the editor-budget transaction that slice S5 of the [skills plan](skills-plan.md) builds (a WORLD code; whichever lane
  gets there first builds it, the other reuses it). The aptitude formula stays in `CharacterStatRules`; skills owns the
  tech points from tech skills, magic owns magick points from spells, the sector adjustment and the mage backgrounds.
  The shared fate owner (point count, armed fate per kind, consumed on use, saved) is slice S7 of the skills plan, built as
  ARC-WORLD-054 ([fate points](../world/fate-points-runtime.md)): magic reads "spell at maximum" and "save against magick" with
  `HeroFateArmed` and uses them with `UseHeroFate`, its pure rules taking them as armed-fate inputs as the attack resolver does.
- **Technology (ARC-RES-062)**: tech degrees and disciplines, schematics, tech items (records 140..222 as their
  effects), the charge-cell source for `ChargesFromCells:` and `ChargeNBranch`, device failure from magick against
  technology, and the tech UI. The engine (run pipeline, the 25 components, payment step, resistance) is this plan's.
- **NPC AI (ARC-RES-039)**: slice I8 needs MT1 (the ten `AI_*` lists), MT3 (`spell_is_known`, highest rank), MT5
  (`spell_cast_cost`), MT6 (the predictor), MT7 (eligibility and line of fire), MT9a (the maintained-run predicate,
  MG11), MT8 and MT10 (to perform a cast), MT12–MT13 (heal ladder, buffs) and, for summoning, MT17, as the research lists
  ([what I8 needs](magic-core-evidence.md#what-ai-slice-i8-needs-from-this-domain)). Line of fire comes from the AI
  awareness stage (A2, then I2); MT7 takes it through a port until then.

## Outside this plan

- **Item spells** (FR-MAGIC-011): the item trigger executor (0x4CB830) and its callers (0x4CB720–0x4CC160), on use,
  wear, unwear, hit and death, with item spell charges and LM7, serve magic and technology items alike. Read with O6 by
  ARC-MAGIC-020 ([ledger IT1–IT10](ledger/spell-runs.md#item-spell-triggers-arc-magic-020)), which runs the wear and unwear
  through the cast host and lifts the equipment's refusal where a trigger host is given ([contract](../world/item-spell-triggers.md)).
  Left as slices of their own: pickup, drop and item use with saved charges; the combat and life events; the item spell
  pane (done by ARC-UI-016, [IP rows](ledger/spell-runs.md#item-spell-pane-arc-ui-016)), inventory-target spells and hotkeys
  (0x57C080, IP7–IP9).
- **Technology runtime**: the technology lane above.
- **Spell visuals**: `SpellEyeCandy.mes`, the cast, release and component eye candy and the cast's timing are done
  ([ARC-MAGIC-017](../world/spell-cast-host.md#eye-candy-arc-magic-017)); the missile's art and flight (goals 28 and 78) and the
  caster's facing turn are done ([ARC-MAGIC-018](../world/spell-cast-host.md#the-missile-arc-magic-018)); the missile's light and
  sound remain.
- **Queued cast**: a second cast while one plays is queued beneath it and plays in turn
  ([ARC-MAGIC-019](../world/spell-cast-host.md#the-viewer-arc-magic-015), SU13, QC1–QC6, MG43).
- **Cursor pick**: the targeting cursor picks its target in the original's order (0x4F28A0: the object under the point, the
  tile's objects, the tile; [ARC-MAGIC-021](../world/spell-cast-host.md#the-viewer-arc-magic-015), SU15–SU19, MG45).
- **Line of fire**: the AI awareness lane (combat open question O4).
- **Fate state**: slice S7 of the [skills plan](skills-plan.md).

## Open questions

- **Q1** Whether the owner wants the first-cast milestone before the magic core is complete (this plan's order), or the
  whole core first without the viewer.
- **Q2** Settled 2026-10-20: the owner accepted all fifteen recommendations ([Anomaly decisions](#anomaly-decisions-accepted-2026-10-20)).
- **Q3** O4 of the research (how spell timers behave in turn-based mode) is read in MT9a; if the original freezes them
  with game time, nothing extra is needed, since the event queue already holds game events while game time stands.
