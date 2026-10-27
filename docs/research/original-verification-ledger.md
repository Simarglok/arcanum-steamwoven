# Original verification ledger

Status: **Research**, work item ARC-RES-048 (2026-10-17); later rows from the tasks named in their sections. One place that records, for every
game-mechanics anomaly and decision input this project took from an earlier source reading, what the retail
1.0.7.4 executable does at the same function. It applies the
[original executable check](original-executable-check.md) to the decisions of the domain studies,
and its verdicts decide which differences become switches of the two rule settings of owner
decision D8 (see [the AI plan](npc-combat-ai-plan.md#owner-decisions-2026-10-16)).

The combat-core anomalies A1–A10 were checked by ARC-COMBAT-001 and are not repeated here (A11 and A12 are in
[Turn-based round](ledger/combat.md#turn-based-round-arc-combat-003); the item wear rules are in [Equipment](ledger/items.md#equipment-arc-world-028); critter art names and missing art in [Art names](ledger/data.md#art-names-and-missing-art-arc-res-050); the effect list is in [Durable effects](ledger/character.md#durable-effects-arc-world-029), blessings and curses in [Blessings and curses](ledger/character.md#blessings-and-curses-arc-world-035); experience and levels in [Experience and levels](ledger/character.md#experience-and-levels-arc-world-030); auto-level schemes in [Auto-level schemes](ledger/character.md#auto-level-schemes-arc-world-036); the skill state rules in [Skill rules](ledger/skills.md#skill-rules-arc-skills-001), the hero's skill reads in [Hero skills](ledger/skills.md#hero-skills-arc-skills-002), the dialog skill cases in [Dialog skill gates](ledger/skills.md#dialog-skill-gates-arc-skills-004); the prototype load phase in [Prototype phase](ledger/data.md#prototype-phase-arc-data-026); the phase of other rule files in [Load phases](ledger/data.md#load-phases-arc-data-027); `map.patch` in [Map patch](ledger/data.md#map-patch-arc-data-028); audio in [Audio](ledger/ui-audio.md#audio-arc-res-052); the logbook in [Logbook](ledger/ui-audio.md#logbook-arc-ui-008); the map list size in [Limits census](ledger/data.md#limits-census-arc-data-029); sector residency and loading in [Main-map streaming](ledger/world.md#main-map-streaming-arc-data-030); terrain types and template paths in [Terrain templates](ledger/world.md#terrain-templates-arc-data-031); the AI parameter rows, tick, gate and danger setter in [NPC AI core loop](ledger/npc-ai.md#npc-ai-core-loop-arc-res-054); the implemented functions that stood at F0 in [Implemented functions](ledger/data.md#implemented-functions-arc-dev-025); flee, surrender and back-off in [NPC AI morale](ledger/npc-ai.md#npc-ai-morale-arc-res-058); hostility, targeting, the enemy list and the attack fan-out in [NPC AI hostility and targeting](ledger/npc-ai.md#npc-ai-hostility-and-targeting-arc-res-057); sight, hearing, line of fire and witnesses in [NPC AI awareness](ledger/npc-ai.md#npc-ai-awareness-arc-res-056); healing, the AI action lists, spell and item eligibility, the item search and grenades in [NPC AI support and magic](ledger/npc-ai.md#npc-ai-support-and-magic-arc-res-059); tech degrees, schematics, assembly, tech items and item durability in [Technology](ledger/technology.md#technology-arc-res-062); the schematic and tech text data in [Schematic data](ledger/technology.md#schematic-data-arc-technology-001); item durability in [Item durability](ledger/items.md#item-durability-arc-world-053); NPC inventory changes across maps in [NPC inventory changes](ledger/items.md#npc-inventory-changes-arc-world-055); tech degrees and expertise in [Tech degrees](ledger/technology.md#tech-degrees-arc-technology-002); found schematics in [Schematic knowledge](ledger/technology.md#schematic-knowledge-arc-technology-003); the component gate and assembly in [Schematic assembly](ledger/technology.md#schematic-assembly-arc-technology-004); the schematic window, its callers and script action 129 in [Schematic window](ledger/technology.md#schematic-window-arc-technology-005); the NPC schematic crafter, `su` and action 129 for an NPC in [NPC schematic crafter](ledger/technology.md#npc-schematic-crafter-arc-technology-006); the spell record parse in [Spell records](ledger/magic.md#spell-records-arc-magic-001) and its components in [Spell components](ledger/magic.md#spell-components-arc-magic-002); spell cost and payment in [Spell cost and payment](ledger/magic.md#spell-cost-and-payment-arc-magic-003); companion orders, loyalty, the wait expiry, the level-up message, script action 139 and follower links across maps in [Companion orders and loyalty](ledger/social.md#companion-orders-and-loyalty-arc-res-064); the character editor's point buys and refunds in [Character point pool](ledger/character.md#character-point-pool-arc-world-052); the dialog's met-before condition, conversation start and end, the insult and the "force good reaction" fate in [Dialog reaction binding](ledger/social.md#dialog-reaction-binding-arc-world-043); how AI actions become goals, movement and attacks in [NPC AI action execution](ledger/npc-ai.md#npc-ai-action-execution-arc-res-066); learning, unlearning and mastery in [Spell book](ledger/magic.md#spell-book-arc-magic-004); areas, routes, time and landing of world-map travel in [World-map travel](ledger/world.md#world-map-travel-arc-res-068); the combat orders, the no-attack flag, follower joins and the post-fight cleanup in [NPC AI followers in combat](ledger/npc-ai.md#npc-ai-followers-in-combat-arc-res-067); the resistance, the saving throw and the fail-chance predictor in [Spell resistance](ledger/magic.md#spell-resistance-arc-magic-005); the PC's reputations, their table, reaction sum, faction tests and the dialog `RP` in [Reputations](ledger/social.md#reputations-arc-world-044); the invocation check, its range, line of fire, forbidden flags and item gate in [Spell invocation](ledger/spell-runs.md#spell-invocation-arc-magic-006); the area test, the target collection, a component's re-aim and the AI notice in [Spell target areas](ledger/spell-runs.md#spell-target-areas-arc-magic-007); the `gd_*` files, their open, the run count and the entry fetch in [Generated dialog files](ledger/data.md#generated-dialog-files-arc-data-042); the NPC `g:` greeting, its slots, generators, override lines and the reputation greeting in [Reputation greetings](ledger/social.md#reputation-greetings-arc-world-058); a run's creation, stage, times and cap in [Spell runs](ledger/spell-runs.md#spell-runs-arc-magic-009); the reading window, its texts, paging and sounds and the reading script actions in [Reading window](ledger/ui-audio.md#reading-window-arc-ui-049); the charge-cell branch of the spell payment, its owner, count, need and take in [Charge cells](ledger/technology.md#charge-cells-arc-technology-007); the character sheet's spell page in [Spell page](ledger/magic.md#spell-page-arc-magic-008); item spell slots, magick against technology for a device and the technology failure in [Tech devices](ledger/technology.md#tech-devices-arc-technology-008); ammunition stacks, their merge, count, use and removal in [Ammunition](ledger/items.md#ammunition-arc-world-066); the decay of items on the ground in [Item decay](ledger/items.md#item-decay-arc-world-062); a run's processing, scheduler, events and interrupts in [Spell run processing](ledger/spell-runs.md#spell-run-processing-arc-magic-010); the heal skill in [Heal](ledger/skills.md#heal-arc-skills-005), pick locks in [Pick locks](ledger/skills.md#pick-locks-arc-skills-006), the Repair skill's run in [Repair](ledger/skills.md#repair-arc-skills-007), the concealment toggle and stealth gait in [Stealth](ledger/skills.md#stealth-arc-skills-008), the PC background, its reader and every rule that reads it in [Backgrounds](ledger/character.md#backgrounds-arc-world-069), the script skill commands in [Script skill commands](ledger/skills.md#script-skill-commands-arc-skills-009), and the per-target stage, no-stack and cancels in [Spell per-target stage](ledger/spell-runs.md#spell-per-target-stage-arc-magic-011); the AI's script hooks, script conditions and actions on AI state and the dialog refusals in [NPC AI script and dialog surface](ledger/npc-ai.md#npc-ai-script-and-dialog-surface-arc-res-070); the Damage and Heal components in [Spell damage and heal](ledger/spell-runs.md#spell-damage-and-heal-arc-magic-012); the walk to a clicked tile in [Long and far routes](ledger/world.md#long-and-far-routes-arc-world-060) the attack's approach in [Attack approach](ledger/combat.md#attack-approach-arc-world-073), the ranged hero attack's reach test, throw, thrown range and wall blocker in [Ranged hero attack](ledger/combat.md#ranged-hero-attack-arc-combat-015), the casting goal and the cast host in [Cast host and casting goal](ledger/spell-runs.md#cast-host-and-casting-goal-arc-magic-014), and the spell UI's activation, cursor and cast in [Spell UI](ledger/spell-runs.md#spell-ui-arc-magic-015))
([original check](combat-core-evidence.md#original-executable-check-arc-combat-001)). Domain
documents keep their own tables; where a ledger row contradicts a domain document, this ledger is
the newer evidence, and the domain document's "likely intent" column is superseded by the verdict.
Owner decisions that rest on a contradicted fact are listed under
[Decisions to revisit](#decisions-to-revisit) and stay unchanged until the owner answers.

## Inputs and method

- The owner's unchanged GOG install, `Arcanum.exe` 1.0.7.4, SHA-256
  `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52` (re-hashed by every reader).
- The questions and the function addresses came from an earlier reading of a source reconstruction that
  the project no longer uses (owner decision 2026-10-18; its provenance is in git history). Function names
  in backticks are labels from that reading; the address is the reference.
- Read only with Xcode's `llvm-objdump` at those addresses, following the method's steps. Every
  function named below keeps its direct branches inside `.text`; none reaches the GOG `.IIDKing`
  section, so none is patched.
- Two independent readings (method step 6): a first reader per domain, then a second reader that
  did not see the first verdicts and re-read the rows that set or change a decision (most reconstruction slips
  and original bugs, and every row flagged as a decision input). [Second reading](#second-reading)
  lists the rows read once, the agreement and the one difference in classification.
- No bytes, listings or decompiled code are stored; scratch output stayed in the session
  scratchpad. Addresses are the retail virtual addresses.

## Verdicts and what they mean for the two settings

| Verdict | Meaning | Rule settings (D8) |
| --- | --- | --- |
| **Reconstruction slip** | The original differs from the earlier reading, which was wrong. | No switch: Steamwoven and Original both follow the original. |
| **Original bug** | The original does what the earlier reading said, and it looks unintended. | Switch candidate: Original keeps the bug; Steamwoven fixes it only when the owning slice records a fix under D2. |
| **Original** | The original does what the earlier reading said, and it is plausibly intended (or the "anomaly" was a misreading). | No switch: both settings follow it. |
| **Reconstruction change** | The earlier reading marked a deliberate change to the original; the row states the original. | The original behaviour is the Original setting; whether Steamwoven keeps the change is a D2 decision. |
| **Not checked** | No code to compare (data, labels, editor-only) or out of scope; the reason is given. | Unchanged. |

## Areas

Each section of the ledger lives in the file of its area; this index keeps one line per section with its rows. Search a row by its ID in the area files, for example `grep -n "^| WT3 " docs/research/ledger/*.md | cut -c1-300`.

| Section | Area | Rows |
| --- | --- | --- |
| <a id="magic-arc-res-043-load-map-arc-res-040"></a>[Magic (ARC-RES-043, load map ARC-RES-040)](ledger/magic.md#magic-arc-res-043-load-map-arc-res-040) | [Magic: spells, costs and the spell book](ledger/magic.md) | MG1–MG20, LM5–LM20 |
| <a id="skills-levels-and-character-arc-res-044-arc-res-031-arc-res-027"></a>[Skills, levels and character (ARC-RES-044, ARC-RES-031, ARC-RES-027)](ledger/character.md#skills-levels-and-character-arc-res-044-arc-res-031-arc-res-027) | [Character](ledger/character.md) | SK1–SK8, LM1–LM17 |
| <a id="npc-ai-reaction-and-companions-arc-res-039-arc-res-028-arc-res-024"></a>[NPC AI, reaction and companions (ARC-RES-039, ARC-RES-028, ARC-RES-024)](ledger/npc-ai.md#npc-ai-reaction-and-companions-arc-res-039-arc-res-028-arc-res-024) | [NPC AI](ledger/npc-ai.md) | P1–P7, LM3–LM18 |
| <a id="reaction-runtime-arc-world-026"></a>[Reaction runtime (ARC-WORLD-026)](ledger/social.md#reaction-runtime-arc-world-026) | [Reaction, reputation and companions](ledger/social.md) | R2–R4 |
| <a id="turn-based-round-arc-combat-003"></a>[Turn-based round (ARC-COMBAT-003)](ledger/combat.md#turn-based-round-arc-combat-003) | [Combat](ledger/combat.md) | TB1–TB15 |
| <a id="scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019"></a>[Scripts, event queue and randomness (ARC-RES-042, ARC-RES-045, ARC-SCRIPT-007, ARC-RES-046, ARC-WORLD-019)](ledger/scripts.md#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019) | [Scripts and events](ledger/scripts.md) | X1–X10, TE1–TE2, OQ2–OQ3 |
| <a id="world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock"></a>[World, movement and data (ARC-RES-047, ARC-RES-029, ARC-RES-034, ARC-DATA-021, clock)](ledger/world.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock) | [World, maps and movement](ledger/world.md) | LA1–LA2 |
| <a id="leaving-a-building-through-a-locked-portal-arc-res-049"></a>[Leaving a building through a locked portal (ARC-RES-049)](ledger/world.md#leaving-a-building-through-a-locked-portal-arc-res-049) | [World, maps and movement](ledger/world.md) | LV1–LV5 |
| <a id="rendering-body-blends-and-wading-arc-render-018"></a>[Rendering: body blends and wading (ARC-RENDER-018)](ledger/world.md#rendering-body-blends-and-wading-arc-render-018) | [World, maps and movement](ledger/world.md) | BL1–BL5 |
| <a id="equipment-arc-world-028"></a>[Equipment (ARC-WORLD-028)](ledger/items.md#equipment-arc-world-028) | [Items and inventory](ledger/items.md) | EQ1–EQ14 |
| <a id="art-names-and-missing-art-arc-res-050"></a>[Art names and missing art (ARC-RES-050)](ledger/data.md#art-names-and-missing-art-arc-res-050) | [Data and loading](ledger/data.md) | MA1–MA8 |
| <a id="durable-effects-arc-world-029"></a>[Durable effects (ARC-WORLD-029)](ledger/character.md#durable-effects-arc-world-029) | [Character](ledger/character.md) | EF1–EF10 |
| <a id="experience-and-levels-arc-world-030"></a>[Experience and levels (ARC-WORLD-030)](ledger/character.md#experience-and-levels-arc-world-030) | [Character](ledger/character.md) | XP1–XP12 |
| <a id="real-time-combat-arc-combat-004"></a>[Real-time combat (ARC-COMBAT-004)](ledger/combat.md#real-time-combat-arc-combat-004) | [Combat](ledger/combat.md) | RT1–RT12 |
| <a id="character-sheet-arc-ui-007"></a>[Character sheet (ARC-UI-007)](ledger/character.md#character-sheet-arc-ui-007) | [Character](ledger/character.md) | CS1–CS7 |
| <a id="hero-attack-arc-combat-005"></a>[Hero attack (ARC-COMBAT-005)](ledger/combat.md#hero-attack-arc-combat-005) | [Combat](ledger/combat.md) | FA1–FA10 |
| <a id="map-transitions-arc-data-025"></a>[Map transitions (ARC-DATA-025)](ledger/world.md#map-transitions-arc-data-025) | [World, maps and movement](ledger/world.md) | TR1–TR5 |
| <a id="prototype-phase-arc-data-026"></a>[Prototype phase (ARC-DATA-026)](ledger/data.md#prototype-phase-arc-data-026) | [Data and loading](ledger/data.md) | — |
| <a id="audio-arc-res-052"></a>[Audio (ARC-RES-052)](ledger/ui-audio.md#audio-arc-res-052) | [Interface windows and audio](ledger/ui-audio.md) | SN3–SN31 |
| <a id="load-phases-arc-data-027"></a>[Load phases (ARC-DATA-027)](ledger/data.md#load-phases-arc-data-027) | [Data and loading](ledger/data.md) | LP1–LP6 |
| <a id="roofs-arc-render-021"></a>[Roofs (ARC-RENDER-021)](ledger/world.md#roofs-arc-render-021) | [World, maps and movement](ledger/world.md) | RF1–RF10 |
| <a id="starting-kit-arc-world-034"></a>[Starting kit (ARC-WORLD-034)](ledger/character.md#starting-kit-arc-world-034) | [Character](ledger/character.md) | WB1–WB8 |
| <a id="blessings-and-curses-arc-world-035"></a>[Blessings and curses (ARC-WORLD-035)](ledger/character.md#blessings-and-curses-arc-world-035) | [Character](ledger/character.md) | BC1–BC6 |
| <a id="auto-level-schemes-arc-world-036"></a>[Auto-level schemes (ARC-WORLD-036)](ledger/character.md#auto-level-schemes-arc-world-036) | [Character](ledger/character.md) | AL1–AL14 |
| <a id="map-patch-arc-data-028"></a>[Map patch (ARC-DATA-028)](ledger/data.md#map-patch-arc-data-028) | [Data and loading](ledger/data.md) | MP1–MP8 |
| <a id="logbook-arc-ui-008"></a>[Logbook (ARC-UI-008)](ledger/ui-audio.md#logbook-arc-ui-008) | [Interface windows and audio](ledger/ui-audio.md) | LB1–LB13 |
| <a id="limits-census-arc-data-029"></a>[Limits census (ARC-DATA-029)](ledger/data.md#limits-census-arc-data-029) | [Data and loading](ledger/data.md) | LC1 |
| <a id="main-map-streaming-arc-data-030"></a>[Main-map streaming (ARC-DATA-030)](ledger/world.md#main-map-streaming-arc-data-030) | [World, maps and movement](ledger/world.md) | SS1–SS8 |
| <a id="terrain-templates-arc-data-031"></a>[Terrain templates (ARC-DATA-031)](ledger/world.md#terrain-templates-arc-data-031) | [World, maps and movement](ledger/world.md) | TR1–TR5 |
| <a id="ai-parameters-and-follow-consent-arc-data-033"></a>[AI parameters and follow consent (ARC-DATA-033)](ledger/npc-ai.md#ai-parameters-and-follow-consent-arc-data-033) | [NPC AI](ledger/npc-ai.md) | AP1–AP6 |
| <a id="npc-ai-core-loop-arc-res-054"></a>[NPC AI core loop (ARC-RES-054)](ledger/npc-ai.md#npc-ai-core-loop-arc-res-054) | [NPC AI](ledger/npc-ai.md) | CL1–CL13 |
| <a id="implemented-functions-arc-dev-025"></a>[Implemented functions (ARC-DEV-025)](ledger/data.md#implemented-functions-arc-dev-025) | [Data and loading](ledger/data.md) | IF1–IF12 |
| <a id="guard-conditions-arc-script-014"></a>[Guard conditions (ARC-SCRIPT-014)](ledger/scripts.md#guard-conditions-arc-script-014) | [Scripts and events](ledger/scripts.md) | SG1–SG6 |
| <a id="tile-and-sector-scripts-arc-script-017"></a>[Tile and sector scripts (ARC-SCRIPT-017)](ledger/scripts.md#tile-and-sector-scripts-arc-script-017) | [Scripts and events](ledger/scripts.md) | TS1–TS3 |
| <a id="object-variables-and-calls-arc-script-018"></a>[Object variables and calls (ARC-SCRIPT-018)](ledger/scripts.md#object-variables-and-calls-arc-script-018) | [Scripts and events](ledger/scripts.md) | OV1–OV4 |
| <a id="story-state-and-story-actions-arc-script-019"></a>[Story state and story actions (ARC-SCRIPT-019)](ledger/scripts.md#story-state-and-story-actions-arc-script-019) | [Scripts and events](ledger/scripts.md) | SS1–SS5 |
| <a id="item-insert-script-arc-res-055"></a>[Item insert script (ARC-RES-055)](ledger/scripts.md#item-insert-script-arc-res-055) | [Scripts and events](ledger/scripts.md) | IS1–IS4 |
| <a id="npc-ai-awareness-arc-res-056"></a>[NPC AI awareness (ARC-RES-056)](ledger/npc-ai.md#npc-ai-awareness-arc-res-056) | [NPC AI](ledger/npc-ai.md) | AW1–AW19 |
| <a id="npc-ai-morale-arc-res-058"></a>[NPC AI morale (ARC-RES-058)](ledger/npc-ai.md#npc-ai-morale-arc-res-058) | [NPC AI](ledger/npc-ai.md) | MR1–MR16 |
| <a id="npc-ai-hostility-and-targeting-arc-res-057"></a>[NPC AI hostility and targeting (ARC-RES-057)](ledger/npc-ai.md#npc-ai-hostility-and-targeting-arc-res-057) | [NPC AI](ledger/npc-ai.md) | HS1–HS22 |
| <a id="spell-records-arc-magic-001"></a>[Spell records (ARC-MAGIC-001)](ledger/magic.md#spell-records-arc-magic-001) | [Magic: spells, costs and the spell book](ledger/magic.md) | SR1–SR16 |
| <a id="spell-components-arc-magic-002"></a>[Spell components (ARC-MAGIC-002)](ledger/magic.md#spell-components-arc-magic-002) | [Magic: spells, costs and the spell book](ledger/magic.md) | SC1–SC12 |
| <a id="spell-cost-and-payment-arc-magic-003"></a>[Spell cost and payment (ARC-MAGIC-003)](ledger/magic.md#spell-cost-and-payment-arc-magic-003) | [Magic: spells, costs and the spell book](ledger/magic.md) | PC1–PC12 |
| <a id="npc-ai-support-and-magic-arc-res-059"></a>[NPC AI support and magic (ARC-RES-059)](ledger/npc-ai.md#npc-ai-support-and-magic-arc-res-059) | [NPC AI](ledger/npc-ai.md) | SP1–SP18 |
| <a id="technology-arc-res-062"></a>[Technology (ARC-RES-062)](ledger/technology.md#technology-arc-res-062) | [Technology](ledger/technology.md) | TC1–TC10 |
| <a id="schematic-data-arc-technology-001"></a>[Schematic data (ARC-TECHNOLOGY-001)](ledger/technology.md#schematic-data-arc-technology-001) | [Technology](ledger/technology.md) | SD1–SD6, TC11 |
| <a id="item-durability-arc-world-053"></a>[Item durability (ARC-WORLD-053)](ledger/items.md#item-durability-arc-world-053) | [Items and inventory](ledger/items.md) | DU1–DU8 |
| <a id="tech-degrees-arc-technology-002"></a>[Tech degrees (ARC-TECHNOLOGY-002)](ledger/technology.md#tech-degrees-arc-technology-002) | [Technology](ledger/technology.md) | TD1–TD9 |
| <a id="schematic-knowledge-arc-technology-003"></a>[Schematic knowledge (ARC-TECHNOLOGY-003)](ledger/technology.md#schematic-knowledge-arc-technology-003) | [Technology](ledger/technology.md) | SK1–SK4, TC12 |
| <a id="schematic-assembly-arc-technology-004"></a>[Schematic assembly (ARC-TECHNOLOGY-004)](ledger/technology.md#schematic-assembly-arc-technology-004) | [Technology](ledger/technology.md) | SA1–SA6 |
| <a id="schematic-window-arc-technology-005"></a>[Schematic window (ARC-TECHNOLOGY-005)](ledger/technology.md#schematic-window-arc-technology-005) | [Technology](ledger/technology.md) | SW1–SW8 |
| <a id="npc-schematic-crafter-arc-technology-006"></a>[NPC schematic crafter (ARC-TECHNOLOGY-006)](ledger/technology.md#npc-schematic-crafter-arc-technology-006) | [Technology](ledger/technology.md) | NC1–NC8 |
| <a id="skill-rules-arc-skills-001"></a>[Skill rules (ARC-SKILLS-001)](ledger/skills.md#skill-rules-arc-skills-001) | [Skills](ledger/skills.md) | SR1–SR14 |
| <a id="dialog-reaction-binding-arc-world-043"></a>[Dialog reaction binding (ARC-WORLD-043)](ledger/social.md#dialog-reaction-binding-arc-world-043) | [Reaction, reputation and companions](ledger/social.md) | DR1–DR7 |
| <a id="hero-skills-arc-skills-002"></a>[Hero skills (ARC-SKILLS-002)](ledger/skills.md#hero-skills-arc-skills-002) | [Skills](ledger/skills.md) | HK1–HK2 |
| <a id="character-point-pool-arc-world-052"></a>[Character point pool (ARC-WORLD-052)](ledger/character.md#character-point-pool-arc-world-052) | [Character](ledger/character.md) | CP1–CP12 |
| <a id="spell-book-arc-magic-004"></a>[Spell book (ARC-MAGIC-004)](ledger/magic.md#spell-book-arc-magic-004) | [Magic: spells, costs and the spell book](ledger/magic.md) | SB1–SB11 |
| <a id="spell-resistance-arc-magic-005"></a>[Spell resistance (ARC-MAGIC-005)](ledger/magic.md#spell-resistance-arc-magic-005) | [Magic: spells, costs and the spell book](ledger/magic.md) | RS1–RS10 |
| <a id="spell-invocation-arc-magic-006"></a>[Spell invocation (ARC-MAGIC-006)](ledger/spell-runs.md#spell-invocation-arc-magic-006) | [Magic: casting and spell runs](ledger/spell-runs.md) | IV1–IV12 |
| <a id="spell-target-areas-arc-magic-007"></a>[Spell target areas (ARC-MAGIC-007)](ledger/spell-runs.md#spell-target-areas-arc-magic-007) | [Magic: casting and spell runs](ledger/spell-runs.md) | TA1–TA16, MG24–MG27 |
| <a id="spell-runs-arc-magic-009"></a>[Spell runs (ARC-MAGIC-009)](ledger/spell-runs.md#spell-runs-arc-magic-009) | [Magic: casting and spell runs](ledger/spell-runs.md) | RN1–RN14, MG28–MG29 |
| <a id="spell-run-processing-arc-magic-010"></a>[Spell run processing (ARC-MAGIC-010)](ledger/spell-runs.md#spell-run-processing-arc-magic-010) | [Magic: casting and spell runs](ledger/spell-runs.md) | RP1–RP14, MG30–MG32 |
| <a id="spell-per-target-stage-arc-magic-011"></a>[Spell per-target stage (ARC-MAGIC-011)](ledger/spell-runs.md#spell-per-target-stage-arc-magic-011) | [Magic: casting and spell runs](ledger/spell-runs.md) | PT1–PT10, MG33–MG34 |
| <a id="spell-damage-and-heal-arc-magic-012"></a>[Spell damage and heal (ARC-MAGIC-012)](ledger/spell-runs.md#spell-damage-and-heal-arc-magic-012) | [Magic: casting and spell runs](ledger/spell-runs.md) | DH1–DH10, MG35–MG38, DF4 |
| <a id="cast-host-and-casting-goal-arc-magic-014"></a>[Cast host and casting goal (ARC-MAGIC-014)](ledger/spell-runs.md#cast-host-and-casting-goal-arc-magic-014) | [Magic: casting and spell runs](ledger/spell-runs.md) | CG1–CG8, MG39–MG41 |
| <a id="spell-ui-arc-magic-015"></a>[Spell UI (ARC-MAGIC-015)](ledger/spell-runs.md#spell-ui-arc-magic-015) | [Magic: casting and spell runs](ledger/spell-runs.md) | SU1–SU14, MG42 |
| <a id="doors-containers-and-locks-arc-res-065"></a>[Doors, containers and locks (ARC-RES-065)](ledger/world.md#doors-containers-and-locks-arc-res-065) | [World, maps and movement](ledger/world.md) | LK1–LK13 |
| <a id="npc-ai-action-execution-arc-res-066"></a>[NPC AI action execution (ARC-RES-066)](ledger/npc-ai.md#npc-ai-action-execution-arc-res-066) | [NPC AI](ledger/npc-ai.md) | EX1–EX20 |
| <a id="dialog-skill-gates-arc-skills-004"></a>[Dialog skill gates (ARC-SKILLS-004)](ledger/skills.md#dialog-skill-gates-arc-skills-004) | [Skills](ledger/skills.md) | DS1–DS5 |
| <a id="skill-check-arc-skills-003"></a>[Skill check (ARC-SKILLS-003)](ledger/skills.md#skill-check-arc-skills-003) | [Skills](ledger/skills.md) | SC1–SC17 |
| <a id="fate-points-arc-world-054"></a>[Fate points (ARC-WORLD-054)](ledger/skills.md#fate-points-arc-world-054) | [Skills](ledger/skills.md) | FT1–FT8 |
| <a id="stealth-arc-skills-008"></a>[Stealth (ARC-SKILLS-008)](ledger/skills.md#stealth-arc-skills-008) | [Skills](ledger/skills.md) | ST1–ST6 |
| <a id="script-skill-commands-arc-skills-009"></a>[Script skill commands (ARC-SKILLS-009)](ledger/skills.md#script-skill-commands-arc-skills-009) | [Skills](ledger/skills.md) | SX1–SX6 |
| <a id="repair-arc-skills-007"></a>[Repair (ARC-SKILLS-007)](ledger/skills.md#repair-arc-skills-007) | [Skills](ledger/skills.md) | RP1–RP5 |
| <a id="pick-locks-arc-skills-006"></a>[Pick locks (ARC-SKILLS-006)](ledger/skills.md#pick-locks-arc-skills-006) | [Skills](ledger/skills.md) | PL1–PL7 |
| <a id="heal-arc-skills-005"></a>[Heal (ARC-SKILLS-005)](ledger/skills.md#heal-arc-skills-005) | [Skills](ledger/skills.md) | HL1–HL5 |
| <a id="skills-page-arc-ui-009"></a>[Skills page (ARC-UI-009)](ledger/skills.md#skills-page-arc-ui-009) | [Skills](ledger/skills.md) | SP1–SP8 |
| <a id="companion-orders-and-loyalty-arc-res-064"></a>[Companion orders and loyalty (ARC-RES-064)](ledger/social.md#companion-orders-and-loyalty-arc-res-064) | [Reaction, reputation and companions](ledger/social.md) | OL1–OL31 |
| <a id="reputations-arc-world-044"></a>[Reputations (ARC-WORLD-044)](ledger/social.md#reputations-arc-world-044) | [Reaction, reputation and companions](ledger/social.md) | RP1–RP8, RM1, A9 |
| <a id="npc-inventory-changes-arc-world-055"></a>[NPC inventory changes (ARC-WORLD-055)](ledger/items.md#npc-inventory-changes-arc-world-055) | [Items and inventory](ledger/items.md) | NI1–NI7 |
| <a id="objects-without-art-arc-data-038"></a>[Objects without ART (ARC-DATA-038)](ledger/data.md#objects-without-art-arc-data-038) | [Data and loading](ledger/data.md) | NA1 |
| <a id="world-map-travel-arc-res-068"></a>[World-map travel (ARC-RES-068)](ledger/world.md#world-map-travel-arc-res-068) | [World, maps and movement](ledger/world.md) | WT1–WT13 |
| <a id="npc-ai-followers-in-combat-arc-res-067"></a>[NPC AI followers in combat (ARC-RES-067)](ledger/npc-ai.md#npc-ai-followers-in-combat-arc-res-067) | [NPC AI](ledger/npc-ai.md) | FO1–FO17 |
| <a id="lights-on-all-maps-arc-res-069"></a>[Lights on all maps (ARC-RES-069)](ledger/world.md#lights-on-all-maps-arc-res-069) | [World, maps and movement](ledger/world.md) | LT1–LT15 |
| <a id="combat-wear-arc-combat-007"></a>[Combat wear (ARC-COMBAT-007)](ledger/combat.md#combat-wear-arc-combat-007) | [Combat](ledger/combat.md) | CW1–CW10 |
| <a id="object-damage-arc-combat-008"></a>[Object damage (ARC-COMBAT-008)](ledger/combat.md#object-damage-arc-combat-008) | [Combat](ledger/combat.md) | OD1–OD14 |
| <a id="critical-effects-arc-combat-009"></a>[Critical effects (ARC-COMBAT-009)](ledger/combat.md#critical-effects-arc-combat-009) | [Combat](ledger/combat.md) | CR1–CR15 |
| <a id="generated-dialog-files-arc-data-042"></a>[Generated dialog files (ARC-DATA-042)](ledger/data.md#generated-dialog-files-arc-data-042) | [Data and loading](ledger/data.md) | GD1–GD5 |
| <a id="reputation-greetings-arc-world-058"></a>[Reputation greetings (ARC-WORLD-058)](ledger/social.md#reputation-greetings-arc-world-058) | [Reaction, reputation and companions](ledger/social.md) | RG1–RG12 |
| <a id="placed-items-arc-world-059"></a>[Placed items (ARC-WORLD-059)](ledger/items.md#placed-items-arc-world-059) | [Items and inventory](ledger/items.md) | PI1–PI6 |
| <a id="drop-cell-arc-combat-010"></a>[Drop cell (ARC-COMBAT-010)](ledger/combat.md#drop-cell-arc-combat-010) | [Combat](ledger/combat.md) | DC1–DC8 |
| <a id="reading-window-arc-ui-049"></a>[Reading window (ARC-UI-049)](ledger/ui-audio.md#reading-window-arc-ui-049) | [Interface windows and audio](ledger/ui-audio.md) | WR1–WR16 |
| <a id="item-use-script-arc-script-020"></a>[Item use script (ARC-SCRIPT-020)](ledger/scripts.md#item-use-script-arc-script-020) | [Scripts and events](ledger/scripts.md) | IU1–IU5 |
| <a id="freed-cells-arc-world-065"></a>[Freed cells (ARC-WORLD-065)](ledger/items.md#freed-cells-arc-world-065) | [Items and inventory](ledger/items.md) | FC1–FC3 |
| <a id="long-and-far-routes-arc-world-060"></a>[Long and far routes (ARC-WORLD-060)](ledger/world.md#long-and-far-routes-arc-world-060) | [World, maps and movement](ledger/world.md) | LR1–LR9 |
| <a id="attack-approach-arc-world-073"></a>[Attack approach (ARC-WORLD-073)](ledger/combat.md#attack-approach-arc-world-073) | [Combat](ledger/combat.md) | AP1–AP7 |
| <a id="charge-cells-arc-technology-007"></a>[Charge cells (ARC-TECHNOLOGY-007)](ledger/technology.md#charge-cells-arc-technology-007) | [Technology](ledger/technology.md) | CC1–CC8 |
| <a id="spell-page-arc-magic-008"></a>[Spell page (ARC-MAGIC-008)](ledger/magic.md#spell-page-arc-magic-008) | [Magic: spells, costs and the spell book](ledger/magic.md) | SG1–SG8 |
| <a id="tech-devices-arc-technology-008"></a>[Tech devices (ARC-TECHNOLOGY-008)](ledger/technology.md#tech-devices-arc-technology-008) | [Technology](ledger/technology.md) | DV1–DV7 |
| <a id="trap-devices-arc-technology-010"></a>[Trap devices (ARC-TECHNOLOGY-010)](ledger/technology.md#trap-devices-arc-technology-010) | [Technology](ledger/technology.md) | TP1–TP8 |
| <a id="ammunition-arc-world-066"></a>[Ammunition (ARC-WORLD-066)](ledger/items.md#ammunition-arc-world-066) | [Items and inventory](ledger/items.md) | AM1–AM7 |
| <a id="backgrounds-arc-world-069"></a>[Backgrounds (ARC-WORLD-069)](ledger/character.md#backgrounds-arc-world-069) | [Character](ledger/character.md) | BG1–BG8 |
| <a id="reach-and-line-of-fire-arc-combat-012"></a>[Reach and line of fire (ARC-COMBAT-012)](ledger/combat.md#reach-and-line-of-fire-arc-combat-012) | [Combat](ledger/combat.md) | RA1–RA10 |
| <a id="ranged-hero-attack-arc-combat-015"></a>[Ranged hero attack (ARC-COMBAT-015)](ledger/combat.md#ranged-hero-attack-arc-combat-015) | [Combat](ledger/combat.md) | RH1–RH7 |
| <a id="npc-ai-script-and-dialog-surface-arc-res-070"></a>[NPC AI script and dialog surface (ARC-RES-070)](ledger/npc-ai.md#npc-ai-script-and-dialog-surface-arc-res-070) | [NPC AI](ledger/npc-ai.md) | SU1–SU17 |
| <a id="window-key-modifiers-arc-input-004"></a>[Window key modifiers (ARC-INPUT-004)](ledger/ui-audio.md#window-key-modifiers-arc-input-004) | [Interface windows and audio](ledger/ui-audio.md) | WK1–WK4 |
| <a id="item-decay-arc-world-062"></a>[Item decay (ARC-WORLD-062)](ledger/items.md#item-decay-arc-world-062) | [Items and inventory](ledger/items.md) | ID1–ID9 |
| <a id="multiplayer-arc-res-072"></a>[Multiplayer (ARC-RES-072)](ledger/multiplayer.md#multiplayer-arc-res-072) | [Multiplayer](ledger/multiplayer.md) | NT1–NT16 |

## Decisions to revisit

These owner decisions or accepted proposals rest on a fact of the earlier reading that the original does not share.
Nothing below is changed by this ledger; each needs the owner's word or its owning slice.

1. **Reputation D1** ([reaction study](npc-reaction-reputation-evidence.md#source-anomalies-and-decisions)):
   the default "source parse" (signed effects get origin = faction = adjustment) is a reconstruction slip. The
   original keeps the written scope, which is the other reading D1 offered; under D8 a reconstruction slip gets
   no switch. **Decided 2026-10-17:** the owner chose the written scope in both settings.
2. **Dodge 5 × level** ([attack resolver](../world/attack-resolver-runtime.md), ARC-COMBAT-001,
   `AttackRules.DodgeEffectiveness`): implemented from the earlier reading; the original uses 2 × level. Under D8 both
   settings follow the original; ARC-WORLD-023 changed the resolver to 2 × level.
3. **Skills §9.5** ([skills study](skills-evidence.md#9-consequences-for-steamwoven-project-policy-proposals)):
   SK1, SK2, SK4 and SK5 are reconstruction slips, so the Original setting reproduces none of them; SK2's "one point
   per click" becomes a design choice, not a fix. SK8 stays a switch candidate; SK7 is the original's
   code.
4. **Window jump and door "leaving" test** ([locomotion](locomotion-evidence.md#anomalies) LA4/W3/D3,
   [first building](first-building-evidence.md) U4, ARC-WORLD-018 door admission and the research
   probes): the original swaps the tiles. **Settled by ARC-RES-049** ([rows LV1–LV5](ledger/world.md#leaving-a-building-through-a-locked-portal-arc-res-049)):
   the start-map counts do not change, and the step evidence decides these portals as the original
   does. The direction-5 side effect never opens a walking step; it stays a switch candidate only
   where a goal attempt runs without the traversal (using a door directly, not traced).
5. **SCR S2 choices** ([census](scr-census.md#anomalies-for-s1-s)): X7 (quotient, not sum) and X8
   (masked counters) are settled by the original, so neither is an Original-setting option; X10 stays a
   switch candidate. Implemented by ARC-SCRIPT-008 ([dispatcher core](../scripting/scr-dispatcher-runtime.md)):
   X7 and X8 as the original in both modes, X10 and X3 tile as Original switches. X6: the S1 admission refusal of negative gotos (`NegativeGoto`) is stricter than
   the original, which reads −1..−4 as control codes; whether the Original setting admits them is
   open (the census decides whether any data uses them).
6. **Tile script 2498 self-removal** ([Virgil script attachments](virgil-script-attachments.md),
   ARC-RES-037): held only in the earlier reading; in the original the removal does not persist. ARC-SCRIPT-008: it persists in
   Steamwoven (D2 fix) and not in the Original setting (`TileScriptChangesDiscarded`).
7. **Starting hero `*n` / `/n`** ([starting hero](starting-hero-evidence.md), SH-07 and the runtime's
   refusal): the original parses the factor, so backgrounds 5 and 11 double their reaction adjustment.
   **Implemented 2026-10-18 by ARC-WORLD-035** as the recommended option (read the factor as the original does, both
   modes; `/0` refused in both, it would divide by zero), put to the owner, who had not answered by then.
8. **Magic slices** ([magic core](magic-core-evidence.md)): MG1, MG2 (mastery `ceil(cost / 2)` at run
   time) and MG8 are reconstruction slips; O9 and O10 are answered by the reconstruction-change rows.
9. **AI stages** ([AI plan](npc-combat-ai-plan.md)): P1, P2, P3, LM1, LM2, LM13 and LM18 are reconstruction slips
   (P3 with a different reading than the plan expected); P4–P7 are the original's behaviour.
10. **Companion wait timeout** ([companions](companion-follow-evidence.md#open-questions) O3, event
    queue TE8): the timeout is real for leaders without extraordinary charisma.
11. **Auto-level A1 and LM11** ([Virgil auto-level](virgil-auto-level.md#anomalies-s)): no recursion
    in the original; followers rise at PC level 50 as the 1.0.7.4 notes say.

## Second reading

Four second readers re-read 77 decision-bearing rows from the executable without the first
verdicts: magic (MG1–MG12, MG15, MG18, MG19, the reconstruction changes, O9, LM9, LM20), skills (every basic
and tech formula, SK1–SK5, SK8, LM1, LM2, LM11, LM17, auto-level A1–A3, A6), AI and scripts (P1–P4,
P7, LM13, LM18, reputation P1 and A2, the wait timer, X1–X3, X6–X8, X10, TE1) and world (LA1, LA4,
wilderness A1, the clock functions). **Every observed behaviour agrees, and so does every verdict
but one classification**: the clock's day carry on a sum above (not equal to) one day was rated
"Original" by the first reader and "Original bug" by the second; the ledger records it as an
off-by-one bug of the original. Two readers saw single lines of an early draft of this file
through a repository-wide search after forming most of their verdicts; they say so in their
notes. For magic the lines shown were MG1, MG2, MG4, MG8, MG19, O9, LM9 (verdicts already formed)
and LM20 (read from the executable afterwards); for skills SK1–SK8, LM1, LM2, LM11, A1 and LM17
(verdicts already formed). The results did not change.

Read once (first reading only; P5, LM3 and LM4 were re-read by ARC-RES-054): LM10, LM3, LM12, MG13–MG17, MG20, LM5–LM7, LM19, SK6, SK7, auto-level
A4 and A5, LM16, P5, P6, LM4, LM8, reputation A3 (A5 was re-read by ARC-WORLD-044), X4, X5, X9, TE2–TE7, TE9, OQ1–OQ3, the
random facts, LA2, LA3, the window rows, wilderness A3 and A6, the combat fields and inheritance,
the lock hours and the follow rows.

Details the second reading added or sharpened:

- **TE1**: the delay is `seconds × 8000` modulo 2^32, so it wraps above 536,870 s; a zero product
  becomes 1 ms before the × 8, i.e. 8 ms. The separate signed overflow above 2,147,483 s that the
  study lists does not occur.
- **Clock current day/month**: the exchanged getters have no caller and no stored pointer in the
  executable; the bug is dead code.
- **Effect min/max**: `min` and `max` are recognised case-insensitively and then told apart by a
  lowercase second letter, so `MIN` acts as max.
- **SK8**: another use-skill callback (0x428E10) does map the requested flag to a forced success,
  so the drop in 0x42A2A0 looks unintended.
- **Shell and reflection at END**: a run cast before a reflection shield appeared is reflected at
  END (source and target swap) and the reflector pays again.
- **Wilderness A1**: the original also clears each moved light's "modified" mark, which the earlier reading omitted.

## Not checked

- Data questions: wilderness A4/A5, P7's label in `ai_params.mes`, how often shipped scripts reach
  SK1. (`xp_level.mes` entries 50/51 behind A1 are answered by [XP12](ledger/character.md#experience-and-levels-arc-world-030): the table is
  strictly increasing.) (LM15 labels and the LM17 keys
  are answered by the [combat-domain data census](combat-data-census.md): the description header's
  labels match the art, and no shipped `Resist:` uses an LM17 key.)
- Code left for later: wilderness A2 (editor only); locomotion W4 and the frame-time clamp;
  companion C2, C4, C10; how often the loyalty step runs in play (read statically as [OL19](ledger/social.md#companion-orders-and-loyalty-arc-res-064), an observation is still needed); what a teleport does after it is processed (followers, the destination map, W9); poison ticks on a switched-off critter ([TE11](ledger/scripts.md#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019)); reputation A6
  and A7; the first-tick marker after a load (OQ2, H1).
