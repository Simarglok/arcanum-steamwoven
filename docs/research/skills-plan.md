# Skills: ordered tasks

Status: **Research plan**, work item ARC-RES-061 (2026-10-20), item 13 of the
owner's top-20 task list (project planning, outside the repository). This
document turns the `SKILLS` slices K1–K7 of the
[skills research](skills-evidence.md#10-proposed-skills-slices) (ARC-RES-044)
into ordered tasks against `origin/main` of 2026-10-20 (through the merge of PR #271, `e52f447`). Each task names its
result, the requirement groups it covers, what it reuses, how it is checked
against the original executable, and what it waits for. It changes no code,
contract, session schema or dialog port. Every task, code and contract below
is a **proposal** until the owner accepts the plan. Tasks are named S1..S17
here; each takes its code when it is created (D6, [codes](#codes)), and a task that changes the snapshot takes the next free
schema number at its merge.

Requirements: [FR-CHAR-016](../requirements/characters-skills.md#fr-char-016)
.. [FR-CHAR-020](../requirements/characters-skills.md#fr-char-020) are the
target; the skill uses touch [FR-CHAR-012](../requirements/characters-skills.md#fr-char-012),
[FR-CHAR-015](../requirements/characters-skills.md#fr-char-015),
[FR-CHAR-024](../requirements/characters-skills.md#fr-char-024),
[FR-COMBAT-005](../requirements/combat-magic-tech.md#fr-combat-005),
[FR-DIALOG-002](../requirements/story-social-ai.md#fr-dialog-002),
[FR-DIALOG-004](../requirements/story-social-ai.md#fr-dialog-004),
[FR-AI-002](../requirements/story-social-ai.md#fr-ai-002) and the item groups
[FR-ITEM-004](../requirements/items-economy.md#fr-item-004),
[FR-ITEM-009](../requirements/items-economy.md#fr-item-009),
[FR-ITEM-011](../requirements/items-economy.md#fr-item-011),
[FR-ITEM-013](../requirements/items-economy.md#fr-item-013),
[FR-ITEM-015](../requirements/items-economy.md#fr-item-015),
[FR-ITEM-016](../requirements/items-economy.md#fr-item-016) and
[FR-ITEM-019](../requirements/items-economy.md#fr-item-019).

## What main already has

ARC-RES-044 was written before most of the character work merged. Against
main of 2026-10-20 the slices start from more than they assumed:

| Already on main | Where | What it means for the slices |
| --- | --- | --- |
| Level, cap, points and training of the five combat skills, checked in the executable (FA7–FA9, F2) | [`CombatSkillRules`](../../src/Arcanum.Simulation/Combat/CombatSkillRules.cs) | K1 extends these to all sixteen skills instead of starting over. |
| Level of all 16 skills with effects, basic and tech point setters with the tech-points side effect, key-stat table, checked (AL1–AL14, F2) | private `Work` of [`AutoLevelRules`](../../src/Arcanum.Simulation/Character/AutoLevelRules.cs) (ARC-WORLD-036) | A second copy of the same rules. K1 makes one public owner and both callers use it. |
| The hero's 12 basic and 4 tech skill fields (points and training bits), and every follower's sheet, in the session and its snapshot (schema 20) | [`SessionHeroAdvancement`](../../src/Arcanum.Session/GameSessionAdvancement.cs) | K3 needs no new snapshot field for points or training: the training bits live in the same stored values. |
| Pre-made heroes spend their starting points through their auto-level scheme, skills included | [advancement runtime](../world/advancement-runtime.md) | The hero has real skill points today, but nothing reads them (next row). |
| The hero's attack reads every combat skill as level 0, untrained; the best-items choice reads skill levels as 0; weapon speed gets no training | `QueryHeroAttack` in [`GameSessionAttack`](../../src/Arcanum.Session/GameSessionAttack.cs) (its comment predates ARC-WORLD-036), `EquipmentRules.Evaluate(..., SkillTraining.None)` | A visible gap K3 closes first. |
| Race and gender skill effects (SH-09: dwarf tech +2, elf tech −2 and others) are dropped when the hero is built | [starting hero runtime](../world/starting-hero-runtime.md), [advancement runtime](../world/advancement-runtime.md) | K3 keeps them. |
| Attack resolver with its own effectiveness, critical and dodge formulas (dodge 2 × level as the original) and the A2 switch `InvocationModifierCountedTwice` | [`AttackRules`](../../src/Arcanum.Simulation/Combat/AttackRules.cs), [rule modes](../world/rule-modes.md) | K2 moves these into the shared skill check; the attack keeps its results. |
| Session random generator with labelled rolls (top-20 item 1) | [session random](../world/session-random-runtime.md) | K2 rolls through it; no injected stand-in is needed. |
| Life state with heal, crippling and blinding (top-20 item 2), item usage bonus and charges read from prototypes | [life state](../world/life-state-runtime.md), [`CombatGenericFields`](../../src/Arcanum.Data/Combat/CombatFields.cs) | Heal (K4) can start without new world owners. |
| Read-only character sheet (top-20 item 11) | [viewer](../rendering/terrain-viewer.md#character-sheet-arc-ui-007) | K7 adds a skills page to it. |
| Dialog grammar admits `LF GF RE RA` as player conditions (and `WA` with a party port); any other opcode is `UnsupportedOperation` | [dialog conversation runtime](../scripting/dialog-conversation-runtime.md) | K3's dialog gates are new opcodes. |
| `Rules\map.patch` `training` key read but not modelled | [map patch](../world/map-patch.md) | Training sources (K7). |
| Prowling margin in the observer's sight and hearing, read in the executable by two readers; it names the shared skill check ARC-SKILLS-003 as its source | [NPC AI awareness](npc-ai-awareness.md#21-the-prowling-margin) (ARC-RES-056) | K5 reuses it; AI slice I2 takes the margin from S3. |

Missing owners that some uses wait for: lock state of portals and containers,
trap state on objects, item durability, a barter window, the shared fate
points, character backgrounds (Educator).

## Boundaries with other lanes

Settled with the magic plan (ARC-RES-060) on 2026-10-20 through the project
coordinator:

1. **One pool of character points.** The session's unspent points with one
   spend-a-point / refund-a-point path down to the value accepted when the
   editor opened. S5 builds it; if the magic lane gets there first,
   it builds it and skills reuse it. The spell checks stay in the magic rules.
2. **Aptitude** stays in `CharacterStatRules`. Skills own only the tech points
   that tech skill points add; magic owns magick points from spells, the sector
   adjustment and the mage backgrounds; tech degrees go to `TECHNOLOGY`.
3. **Fate points** (FR-CHAR-024) are a shared session owner built by the skills
   lane (S7). Magic consumes "spell at maximum" and "save against
   magick" through it; the attack resolver already takes its fates as inputs.
4. **The skill check** belongs to skills; magic calls none. SK1 (script "get
   skill") stays with skills and the SCR dispatcher.
5. **NPC AI**: slice I2 (awareness) takes the prowling margin from
   S3, or as an input until it merges. `ai_check_use_skill` (SK4)
   and NPC heal, repair and pick-lock decisions belong to the AI slices; they
   call the skill check.

## How each task is checked against the original

Every task follows [the original executable check](original-executable-check.md):

- A function the task implements is read in the retail 1.0.7.4 executable by
  one reader and, independently, a second (step 6) before the code is written:
  level **F2**, a row in the [verification ledger](original-verification-ledger.md)
  and the [function registry](../verification/functions.csv), and the
  `OriginalFunction` mark on the method. Functions already at F2 (table above)
  are reused, not re-read.
- A pure arithmetic function also gets a **function oracle** run (F3,
  `./arcanum oracle`, [ARC-DEV-007](../../tools/original-oracle/README.md)) when
  its world model is small: named per task below.
- An anomaly follows its ledger verdict under D8: a reconstruction slip gets
  the original's behaviour in both settings; an original bug becomes a switch
  of the Original setting ([rule modes](../world/rule-modes.md)); a design
  choice for the Steamwoven setting is put to the owner when the task starts.
- Real-data checks run on the Mac over the whole module (every prototype, every
  dialog, every script), never one location, and commit aggregates only.

## The tasks

<a id="codes"></a>**Codes.** Per the [naming rules](../workflow/task-naming.md),
`SKILLS` covers skills and their checks, training and use, while character state
and the session runtime stay `WORLD`. So S5 (the shared point pool, stats
included) and S7 (fate points) take the next free `ARC-WORLD-NNN` codes, and
the other fifteen take `ARC-SKILLS-NNN` codes in the order they are created
(none is used on 2026-10-20: no branch, PR or commit has the type), S1 being
`ARC-SKILLS-001`; `ARC-SKILLS-003` is kept for S3 whatever the creation order, the code ARC-RES-056 already names
for the prowling margin. Each task is one PR on the Mac (.NET and the original data)
unless marked; the components listed exclude tests and docs, which every task
has; the contract is written in the
[Definition of Ready](../workflow/tasks.md#definition-of-ready) form when the
task is created. All Data, Simulation, Scripting and Session parts stay
Godot-free.

### Wave 1: S1 now; S2 and S3 after S1; S4 after S2

**S1 Skill rules for all sixteen skills** (K1).
Result: one public, pure owner of skill state rules; `CombatSkillRules` and
`AutoLevelRules` call it instead of keeping their own copies.
Components: Simulation `Character` (or a new `Skills` folder), the callers
`CombatSkillRules` and `AutoLevelRules`.
Content: the 16 skills (12 basic, 4 tech) with their key stats; level with
effects, clamp and key-stat cap (monstrous melee); training get (stored bits,
monstrous melee) and set (one step up at a time, minimum level 0/1/9/18, any
lower or equal tier with its minimum); point set for basic and tech (the tech
points side effect); point cost and refund; the guard that refuses lowering a
key stat below what the bought points need. Typed refusals, no exceptions at the
boundary. No session or snapshot change.
Requirements: FR-CHAR-016, FR-CHAR-017, FR-CHAR-018 (ladder rules),
FR-CHAR-020 (tech points).
Reuse: `CombatSkillRules`, `AutoLevelRules.Work`, `EffectModifiers`,
`CharacterStatRules`.
Original check: reuse F2 for 0x4C5E50, 0x4C5EB0, 0x4C5F70, 0x4C5FA0,
0x4C6000, 0x4C60C0, 0x4C65E0, 0x4C66E0; read 0x4C6170 (basic training set),
0x4C67F0 and 0x4C6850 (tech training get and set), 0x4C69C0 (minimum level),
0x4C6580 and 0x4C6680 (tech base and points), 0x4C64B0..0x4C64E0 (cost and
refund), 0x4C6B50 with its caller 0x4B0980 (stat-lowering guard), and the tech
part of the key-stat table. Oracle: level (0x4C5EB0) and training set
(0x4C6170).
Real data: every critter prototype's skill fields read through the rules (any
points above 5, any training without its level: census items of
[section 11](skills-evidence.md#11-census-items-for-the-mac)).
Depends on: nothing new.

**S2 The hero's and followers' skills in play** (first half of K3).
Result: the session answers the hero's 16 skill levels and training, and every
reader of a skill uses them: the attack, dodge and backstab of the hero, weapon
speed (Apprentice +5), the best-items choice, follower sheets and the party
consent's persuasion training (`PcPersuasion`, today a caller fact). The race
and gender skill changes (SH-09) are kept as effect modifiers of the hero's
innate race and gender effects and applied when a level is read, never written
into the stored points.
Components: Session, the AREA viewer host where it builds the hero.
Requirements: FR-CHAR-016, FR-CHAR-017, FR-CHAR-009 (race effects on skills),
FR-CHAR-022 (effects on skills), FR-COMBAT-005 and FR-COMBAT-012 inputs.
Reuse: S1, `SessionHeroAdvancement`, the durable-effects owner,
`QueryHeroAttack`, `GameSessionOwner.EquipBest`, `FollowConsentRules`.
Original check: no new function; reuse F2 for 0x465CF0 (weapon speed) and the
level and training functions. Snapshot: none expected (the skill fields are saved
since schema 20); if keeping the race skill effects changes what is saved, the
task takes the next free schema number.
Real data: all twelve pre-made heroes start with the skill levels their scheme
and race give; one attacks with a non-zero melee level.
Depends on: S1.

**S3 The shared skill check** (K2).
Result: one pure skill check for every skill: effectiveness (16 formulas, the
opposed table, +10 for extraordinary intelligence, game difficulty for the PC,
the 95 cap), difficulty from an explicit facts record (modifier, weapon, awareness, aim and armour
class, distance, light, injuries, per-skill terms), the roll, critical success
and failure, fate and forced success as inputs, typed outcomes. The attack
resolver uses it for the attack and dodge rolls; its results do not change.
Components: Simulation skill check, `AttackRules` and the attack resolver.
Requirements: FR-CHAR-019, FR-COMBAT-005, FR-COMBAT-009.
Reuse: `AttackRules` effectiveness and criticals (moved, not copied), the A2
switch `InvocationModifierCountedTwice`, labelled session rolls (`skill.*`).
Original check: read 0x4C62E0 and 0x4C69F0 (effectiveness, formulas already
agree with SKX1, the whole function now), 0x4C6410 (opposed table), 0x4C8430
(difficulty), the generic part of 0x4C7160 (run), 0x4C81A0 and 0x4C82E0
(critical chances). Oracle: 0x4C6410, 0x4C62E0, 0x4C69F0. A2 as decided;
SK6 (prowling formula unreachable, backstab 0) as the original.
Depends on: S1. Unblocks AI slice I2 (prowling margin).

**S4 Dialog skill gates** (second half of K3).
Result: dialog conditions `SK` (skill level), `PS` (persuasion), `HA`
(haggle), `TR` (training) and actions `TR` (set training) and `ET` (expert
teacher gate) work in every conversation through a skills port of the session;
before, they were `UnsupportedOperation`.
Components: Scripting `Dialog` grammar and conversation, Session skills port,
the viewer's conversation host.
Requirements: FR-DIALOG-002, FR-DIALOG-004, FR-CHAR-018.
Reuse: the dialog grammar and its port pattern (`WA`/`UW` party port),
S1 (training set), S2 (levels).
Original check: read the skill cases of 0x4150D0 (conditions) and 0x415BA0
(actions): sign rules, effective level against the threshold, the `et` gate
(level ≥ 9 and at least Apprentice). The persuasion training of the party
consent comes from S2.
Real data: a census of `sk ps ha tr et` over every dialog of the module
(counts and the maps that use them); every such line parses.
Depends on: S2.

### Wave 2: after wave 1

**S5 Buying and refunding character points** (rest of K1; a `WORLD` code).
**Built by ARC-WORLD-052** (2026-10-21, [contract](../world/character-point-pool.md), ledger
[CP1–CP12](ledger/character.md#character-point-pool-arc-world-052)): the pool covers stats, hit and fatigue
points and both skill families; spell ranks and tech degrees plug in through a lane (MT3, TECH-2); the − click is an
option for S6, by default the original's "until the level changes", which stops at the floor in both modes (CP6).
Result: the session's one spend-a-point / refund-a-point path over the hero's
unspent points with the accepted floor, for basic and tech skills and primary
stats; the magic lane's spell ranks use the same path.
Components: Session.
Requirements: FR-CHAR-016, FR-CHAR-017, FR-CHAR-015.
Original check: read 0x57ACD0 and 0x57AEB0 (+ and −; SK2), 0x55D3A0, 0x55D6F0
and 0x5597C0 (accepted floor), 0x55AE70 (the stat hint), 0x55A5C0 (stat + and
−). Decision for the owner when the task starts: SK2 in the Steamwoven
setting, the original's "one click removes points until the level changes" or
"one point per click" (a design choice, ledger decision 3).
Depends on: S1.

**S6 Skills page on the character sheet** (UI part of K7).
**Built by ARC-UI-009** (2026-10-21, [viewer](../rendering/terrain-viewer.md#skills-page-arc-ui-009), ledger
[SP1–SP8](ledger/skills.md#skills-page-arc-ui-009)): the sixteen skills in the original's four groups with the
skill texts of `mes\skill.mes` (`SkillTexts`, Data), + and − through the point pool with the editor the sheet opens; the − click
is the original's default (no owner choice of the alternative). The owner's look at the page (L3) is still open.
Result: the AREA viewer's sheet shows the 16 skills with level, training and
the key-stat hint, and + and − buy and refund points through S5.
Components: viewer core, Godot viewer (its smoke run is an acceptance criterion).
Requirements: FR-CHAR-012, FR-CHAR-016, FR-UI-004.
Original check: read 0x55BD10 (display) and the skill texts of `mes\skill.mes`.
Human-Gates: an owner look at the page (separate L3 item, does not block merge).
Depends on: S2, S5.

**S7 Fate points** (shared owner; a `WORLD` code).
**Built by ARC-WORLD-054** (2026-10-21, [contract](../world/fate-points-runtime.md), ledger
[FT1–FT8](ledger/skills.md#fate-points-arc-world-054)): the session's fate owner with the attack, conversation-start
and dialog `fp` readers; the skill uses and magic call its API. Snapshot schema 26.
Result: the hero's fate points in the session: count, armed fate per kind
(skill critical success, attack critical hit and miss, spell at maximum, save
against magick), consumed on use, saved; the skill check and the attack
resolver read it; magic consumes its two kinds.
Components: Session, the skill check and attack resolver inputs.
Requirements: FR-CHAR-024, FR-MAGIC-005.
Original check: locate the fate owner's functions in the executable (from the
callers in 0x4C7160 and the attack), read them by two readers; the fated repair
(full HP) stays with S11. Snapshot: next free schema number.
Depends on: S3.

**S8 Heal with bandages and healing items** (first K4 use).
**Built by ARC-SKILLS-005** (2026-10-21, [contract](../world/heal-runtime.md), ledger
[HL1–HL5](ledger/skills.md#heal-arc-skills-005)): the hero heals any critter with a healing item; the best-follower
substitution and target choice in the viewer are left out. Snapshot schema 31 (the items' uses).
Result: the hero (or the best follower) uses a healing item on any living
critter: the check, healed amount, Master and critical cures, item charges and
destruction, 4 action points in a round.
Components: Simulation heal handler, Session use, viewer action.
Requirements: FR-CHAR-019, FR-ITEM-009, FR-CHAR-026.
Reuse: life state heal and condition cures, item usage bonus and charges,
the turn-based charge.
Original check: read the heal case of 0x4C7160, 0x462CC0, 0x4352C0 and
0x428E10 (use item with skill; an inventory use charges its 4 points there),
and 0x42A2A0 (the 4-point charge of the use-skill-on goal, for a heal on another
critter).
Depends on: S3; S7 for the fated heal (input until then).

### Wave 3: waits for another owner

All of S9..S15 also need S3; S11 needs S7 for the fated repair.

| Task | Result | Requirements | Original check (read) | Waits for |
| --- | --- | --- | --- | --- |
| S9 Pick locks (**built by ARC-SKILLS-006**, [contract](../world/pick-locks-runtime.md), ledger [PL1–PL7](ledger/skills.md#pick-locks-arc-skills-006); witnesses, best-follower substitution and the AI's timed goal with the apprentice speed-up not modelled) | Unlock and lock portals and containers, jam on a critical failure, lockpick bonus, Expert ignores light, Master halves the lock, Apprentice works faster, best-follower substitution | FR-ITEM-004, FR-AI-003, FR-CHAR-019 | pick-lock case of 0x4C7160, 0x436220, 0x571CB0, 0x4C8FA0..0x4C9050 | ARC-WORLD-050: the session owner of portal and container lock state (open, locked, jammed, busted, lock rules, relock timer), universal across maps, built in the world lane after its executable research ARC-RES-065 (both planned, not merged on 2026-10-20) |
| S10 Spot and disarm traps | Spot on every path step and use, Master second roll, magical traps need Expert, disarm, Expert's critical yields the device | FR-ITEM-016, FR-CHAR-019 | 0x4BBE40, 0x427110, 0x441980, 0x4BC7B0, 0x4BBFE0, 0x4BC090, 0x57A620 | trap state on objects through the SCR dispatcher |
| S11 Repair (**built by ARC-SKILLS-007**, [contract](../world/repair-runtime.md), ledger [RP1–RP5](ledger/skills.md#repair-arc-skills-007); the merchant's paid service waits for the barter window) | Repair by percentage with the durability cost per tier, Master fixes broken items, SK7 as the original, fated repair | FR-ITEM-011, FR-CHAR-019 | repair case of 0x4C7160, 0x4C8E60, 0x57A320, 0x57A5E0 | item durability in the session (item HP, the two-step break, what a broken item blocks, the item side of a repair quote): one `WORLD` task of the [technology research](technology-evidence.md#item-durability-fr-item-011) ARC-RES-062, code taken when it starts |
| S12 Stealth (**built by ARC-SKILLS-008**, [contract](../world/stealth-runtime.md), ledger [ST1–ST6](ledger/skills.md#stealth-arc-skills-008), snapshot schema 35; the observers are AI slice I2) | Concealment toggle for the hero and followers (saved), prowling margin for observers, stealth walk, only a Master runs | FR-AI-002, FR-CHAR-019 | 0x57A0A0, 0x45EE30, 0x425BF0, 0x42EDC0 (sight and hearing already read, ARC-RES-056) | AI slice I2 to consume it; can merge before I2 |
| S13 Haggle (**built by ARC-UI-050** with the barter window, [contract](../world/barter-runtime.md), ledger [BT1–BT10](ledger/items.md#barter-arc-ui-050); the price at F3 by the oracle; the merchant's substitute container built by ARC-WORLD-090, FR-ITEM-014) | Buy and sell prices, Expert and Master trade gates | FR-ITEM-013, FR-CHAR-019 | 0x461F80, 0x462170, 0x462230; oracle for 0x461F80 | a barter window |
| S14 Gambling | Gamble an item, PC limit table, the saved pre-drawn roll (snapshot: next free schema number) | FR-ITEM-019, FR-CHAR-019 | 0x579840, 0x579B60, 0x4C6520, 0x4C6560, the skill module save; SK5 as the original | a barter window |
| S15 Pick pocket | Steal and plant one item, gold and ammunition limits, detection, the theft as an attack | FR-ITEM-015, FR-CHAR-019 | 0x575100, 0x575180, pick-pocket case of 0x4C7160 | NPC inventories in the session; AI hostility slice for the consequence |
| S16 Trainers and skill services | Dialog trainers (`t:`, Apprentice for 100 gold), NPC skill services (`u:`, SK8), `map.patch` `training`, Educator | FR-CHAR-018, FR-DIALOG-004, FR-CHAR-006 | 0x418CA0, 0x418DE0, 0x418F30, 0x416C10, 0x4C62D0, 0x419B50..0x419CB0, 0x411940, 0x4C2950 | S4; Educator also waits for backgrounds (FR-CHAR-005); SK8 switch decided by the owner when it starts |
| S17 Script skill commands (**built by ARC-SKILLS-009**, [contract](../scripting/scr-dispatcher-runtime.md#skill-commands-arc-skills-009-set-s11), ledger [SX1–SX6](ledger/skills.md#script-skill-commands-arc-skills-009); use item with skill is a request only, no live installed use) | SCR "get skill" (SK1 as the original: no fall-through) and "use item with skill" with a modifier (A2) | FR-SCRIPT-002 | the two cases of 0x447220 | S3 and the SCR dispatcher |

### Order and the first visible results

```text
S1 ──> S2 ──> S4 ──> S16
 │      └───────────> S6
 ├────> S5 ─────────> S6
 └────> S3 ──> S7, S8, S12, S17
         └───> S9, S10, S11, S13, S14, S15 (each also waits for its owner outside the lane)
```

- After S2 a pre-made hero fights with the melee level its scheme
  bought, and a dwarf's tech skills include the race bonus.
- After S4 dialog lines gated by persuasion, haggle and training
  open for the hero.
- After S6 the owner buys skill points on the sheet (S5 first).
- After S8 the hero heals Virgil with a bandage.

The alpha minimum of the project's gap list (K1–K4 and the skills page) is
S1..S6, S8 and S9; S9 needs the lock state owner
first.

## Mapping to the K slices

| K slice (ARC-RES-044) | Tasks |
| --- | --- |
| K1 Skill ranks | S1, S5 |
| K2 Skill check | S3 (and S7 for fate) |
| K3 Hero skills in the session | S2, S4 |
| K4 Skills on the world | S8, S9, S10, S11 |
| K5 Stealth | S12 |
| K6 Trade and social | S13, S14, S15 |
| K7 Training and sheet | S6, S16 (and S17 for scripts) |

## Method

Read against `origin/main` at 2026-10-20 (merge of PR #271, `e52f447`): the skills research, its
ledger rows, the function registry, the code of the character, advancement,
combat, equipment, session and dialog owners, the requirements registry and
the component catalogue. No original file, probe or game data was read for
this work item.
