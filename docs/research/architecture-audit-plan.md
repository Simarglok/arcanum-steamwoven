# Architecture audit against the original: plan

Status: **Plan, accepted by the owner** (2026-10-29), work item ARC-RES-077. Docs-only:
no code changes and no checks run. Every slice takes its own task code from the
coordinator, its own thread and its own PR; R1 is ARC-RES-078 and M1 is ARC-RES-079.

The owner's ask: before more bug fixes or features, review the whole existing
implementation against the original game, find every mismatch at the level of the
architecture, and write concept documents for what is not implemented yet, so that
it is built right from the start.

## Why an architecture audit

The [follower transfer research](follower-map-transfer.md) (ARC-RES-076, PR #583)
found that the "visitor companion" is not a narrow bug but a systemic mismatch: the
original moves the follower object whole, with its one ID, into the destination
map's dynamic objects, while Steamwoven finds an NPC's base record (object row,
combat fields, AI row, inventory seed, body) only among the shown map's rows. Every
consumer that asks for "the row of X on the shown map" fails for a carried
companion, and eight special cases grew around it.

Function-level parity did not catch this. On `origin/main` 63b55a33 the
[function registry](../verification/README.md) holds 789 rows (F2 628, F1 123, F3 38;
partial 499, full 266, not implemented 24) and the
[coverage matrix](../requirements/coverage.md) 191 requirements (4 implemented,
163 partial, 15 researched, 9 not started). Each row compares one function's rules;
none asks *where an object lives, who owns it, how it is addressed, when it changes
and what is saved*. Those questions belong to a few engine mechanisms that every
domain stands on. This audit reads those mechanisms in the original first, maps
Steamwoven's equivalents, and only then sweeps the domains.

## Method

- Original behaviour comes only from the retail 1.0.7.4 executable, read per the
  [original executable check](original-executable-check.md) by two independent
  readers (method step 6); facts in prose, no bytes or listings in git. Earlier
  rows are reused by reference, not re-read, unless a slice needs a missing field.
- Steamwoven facts come from the code on the `origin/main` commit each slice names.
- Each finding is classed **systemic** (a model or addressing difference that every
  consumer of a mechanism inherits), **narrow** (one rule or path), or
  **intended** (a recorded owner decision or rule-mode switch, [decisions](../architecture/decisions.md)).
- Slices that read the executable run on the owner's Mac. Slices that only read
  Steamwoven's code and docs are docs-only and may run in a cloud clone with no checks.
- Fixes are not part of the audit. Each gap gets proposed fix slices; fixes start
  only after the owner's decisions on the target model (slice G1).

## Layer 0: the engine spines

The mechanisms every domain shares, with what is already known and what Steamwoven
does today. "Known" names existing research; "open" is what a slice must read.

| # | Spine | Original: known / open | Steamwoven today |
| --- | --- | --- | --- |
| SP1 | Object model and identity | Known: one object per thing with a persistent ID kept across maps (FM4), typed fields from the field table, prototypes. Open: the handle vs ID split, field ownership, what is copied from the prototype at creation and what is read through it later, object kinds the engine creates at run time | NPC identity `oid:` + hash of the object ID; base records are immutable rows of the loaded map (`Area`); mutable state in 79 session domain files, some keyed by entity, some by map, some by kind |
| SP2 | Residency and lifecycle | Known: sector cache (SS1–SS4), follower moved whole between maps with the source ID recorded as deleted (FM1–FM14). Open: create, destroy and move between sectors in general; what simulates on unloaded sectors; map change order | One shown map in the viewer (main map in 3×3 windows); records looked up per shown map; run-local caches (companion bodies) |
| SP3 | Persistence | Known: per-map dynamic objects and deleted IDs, a global game file. Open: the full save layout, which state is global vs per map, load order and fix-ups | Snapshot schema 58: an overlay of keyed map changes over the immutable map plus per-domain stores ([session runtime](../world/game-session-runtime.md)); per-kind stores for objects the game creates (placed items, junk piles, blood pools, created items, ammunition, thrown items) |
| SP4 | Time and the frame | Known: event types and map scope ([event queue](game-event-queue.md) P1–P7), AI cadence. Open: one frame's order (input, events, goals, AI, render), real vs game vs animation time, pause and turn-based gating as one model | Session clock and event queue; viewer pumps; a separate world clock for AI runs; per-host ticks |
| SP5 | Actions and goals | Known: NPC goal stacks and many goal rows ([AI execution](npc-ai-execution.md), spell missile, throw, cast goals). Open: whether the PC's walk, attack, use, cast and throw run on the same goal system, its slots, priorities, interrupt and replacement rules for the PC | Hero actions as viewer hosts and per-action goals (`AttackGoal`, `SpellCastGoal`, click route); NPC goals in `AiExecutorRules`; two machines |
| SP6 | Script attachment and state | Known: dispatcher, triggers, admission ([SCR dispatcher](scr-event-dispatcher.md)). Open: script slots on the object vs on the prototype, local flags and counters as object fields, runtime re-attachment, scripts of moved objects | Script state keyed by the record's home map (dialog local flags, counters, off/destroyed), hosts per map (`AreaScriptHost`, `AreaUseScriptHost`, `AreaItemScriptHost`) |
| SP7 | Relations between objects | Known: leader, the transient fight links cleared on a move (FM6). Open: container parent, wielder, substitute inventory, combat focus, summoner, and how each survives destroy, move and load | Mostly identity links in session domains; FM6 links partly modelled |
| SP8 | Authority and change routing | Known: host-authoritative network layer ([multiplayer](multiplayer-evidence.md) NT4–NT6). Open: which game changes pass through one change path, which is where a future network game and our session commands meet | Session owner calls from viewer hosts; no single command boundary named |
| SP9 | Input and interface modes | Known: windows, cursors, bars (UI ledgers). Open: the interface mode machine and how a click becomes a goal | Viewer input routing per window and mode |

## Suspected architecture-level mismatches

Hypotheses to confirm or drop, each with where it shows. H1 is confirmed by
ARC-RES-076; the rest are not checked yet.

| # | Hypothesis | Spine | Signs in Steamwoven |
| --- | --- | --- | --- |
| H1 | Base records addressed by (shown map, entity) instead of identity | SP1, SP2 | Confirmed, [follower transfer](follower-map-transfer.md#3-gap-systemic-not-narrow): eight special cases |
| H2 | No general dynamic object: each kind of object the game creates or changes has its own store and its own drawing, picking and saving path | SP1, SP3 | Separate session stores for placed items, junk piles, blood pools, created and made items, ammunition stacks, thrown items, NPC inventory changes |
| H3 | Mutable object state as a project-defined key vocabulary over immutable rows, not object fields | SP1, SP3 | `MapChangeKey` property codes per feature; a new field means a new key or a new domain |
| H4 | PC and NPC actions on two goal machines | SP5 | Viewer hero hosts vs `AiExecutorRules`; interrupts and replacement may differ for the PC |
| H5 | More than one clock drives the game | SP4 | Session clock, world clock for AI runs, viewer frame pumps |
| H6 | Game logic lives in the diagnostic viewer tool, not in Godot-free assemblies | SP8 | `tools/terrain-viewer/Core` (about 27 k lines of hosts) and `Godot` (about 50 k) hold hosts the alpha app runs; against the [simulation boundary](../architecture/decisions.md) direction |
| H7 | Only the shown map (or window) is alive: objects off it do not simulate, age or fire events as the original's do | SP2, SP4 | Hosts built for the shown map; main map windows |
| H8 | Script state keyed by the home map record instead of carried on the object | SP6 | Works while identity never changes; breaks for objects the game creates or clones |
| H9 | Run-local caches hold state the original keeps on the object, so a quick load or a fresh run loses it | SP2, SP3 | `_travellers` bodies (FM14); others to be listed |

## Slices

Codes are not taken; the coordinator hands each one out when its thread starts.
"Mac" reads the executable; "cloud" is docs-only code reading with no checks.

### Phase 1: reference model of the original (Mac)

| Slice | Covers | Output |
| --- | --- | --- |
| R1 | SP1 + SP7: object identity, handles and IDs, field ownership, prototypes at creation and after, run-time object kinds, links between objects | [`docs/architecture/original-object-model.md`](../architecture/original-object-model.md) (reference rows OM1–OM32; done, ARC-RES-078) |
| R2 | SP2: maps and sectors, create, destroy, move between sectors and maps, what lives off the loaded sectors, map change order | rows in the same document ([RL1–RL31](../architecture/original-object-model.md#7-sectors-what-a-sector-owns-who-is-listed-how-objects-move); done, ARC-RES-080) |
| R3 | SP3: the save layout, global vs per map state, load order and fix-ups | [`docs/architecture/original-persistence.md`](../architecture/original-persistence.md) (reference rows PS1–PS34; done, ARC-RES-081) |
| R4 | SP4: one frame's order, the time kinds, pause, turn-based gating, event scope | [`docs/architecture/original-time-loop.md`](../architecture/original-time-loop.md) (reference rows TL1–TL46; done, ARC-RES-082) |
| R5 | SP5: the goal system for PC and NPCs, slots, priorities, interrupts, how input, AI and scripts start goals | [`docs/architecture/original-goals.md`](../architecture/original-goals.md) (reference rows GS1–GS32; done, ARC-RES-083) |
| R6 | SP6 + SP8 + SP9: script slots and state on objects, the change path the network layer uses, the interface mode machine | [`docs/architecture/original-scripts-authority.md`](../architecture/original-scripts-authority.md) (reference rows SA1–SA55; done, ARC-RES-084) |

R1 first; R2–R6 may run in parallel after it, as many at once as the owner allows on the Mac.

### Phase 1b: Steamwoven's map (cloud, may start at once)

| Slice | Covers | Output |
| --- | --- | --- |
| M1 | Every state owner and record source in `src/` and the viewer: what each holds, how it is keyed (entity, map, kind), who reads base rows and how, every clock and goal machine, every run-local cache, what is in the snapshot | [`docs/architecture/steamwoven-model.md`](../architecture/steamwoven-model.md), one table row per owner (done, ARC-RES-079) |

### Phase 2: gap register and target model (Mac or cloud)

| Slice | Covers | Output |
| --- | --- | --- |
| G1 | R1–R6 against M1: confirm or drop H1–H9, find new ones, class each; a target model (for example one identity-based object directory with mutable fields and residency) and a migration order; the follower slices V1–V6 of ARC-RES-076 fold in here | [`docs/architecture/gap-register.md`](../architecture/gap-register.md) (GR1–GR35) and [`docs/architecture/target-model.md`](../architecture/target-model.md) (TM1–TM14, migration MIG1–MIG16, V1–V6 as MIG1–MIG5, MIG10, MIG11); owner questions GQ1–GQ6 as decision cards (ARC-RES-086) |

The owner's decisions on G1 settle the model the rest of the audit and every
concept document builds on.

### Phase 3: domain sweeps (mostly cloud)

Each sweep checks one domain's code against the target model and the R rows: which
consumers depend on a systemic gap, which narrow gaps exist, which function
registry rows are partial for an architectural reason. It re-reads the executable
only for F0 rows it cannot class otherwise (then on the Mac).

| Slice | Domain | Requirement sections |
| --- | --- | --- |
| D1 | World, maps, transitions, travel, lighting and time | [world](../requirements/world.md), [travel and runtime](../requirements/world-travel-runtime.md), [lighting and time](../requirements/lighting-time.md) |
| D2 | Scene objects, doors, containers, roofs, rendering inputs | [scene](../requirements/scene.md) |
| D3 | Items, inventory, economy, barter, decay | [items and economy](../requirements/items-economy.md) |
| D4 | Combat in both modes, projectiles, damage, life | [combat, magic, technology](../requirements/combat-magic-tech.md) (FR-COMBAT) |
| D5 | Magic, item spells and technology | [combat, magic, technology](../requirements/combat-magic-tech.md) (FR-MAGIC), [characters and skills](../requirements/characters-skills.md) (technology ranks FR-CHAR-017, FR-CHAR-020) |
| D6 | Character, skills, advancement | [characters and skills](../requirements/characters-skills.md) |
| D7 | NPC AI, followers, reaction and reputation | [story, social, AI](../requirements/story-social-ai.md) (FR-AI, FR-SOCIAL) |
| D8 | Scripts, dialogs, story state | [story, social, AI](../requirements/story-social-ai.md) (FR-SCRIPT, FR-DIALOG), [content and product](../requirements/content-product.md) |
| D9 | Interface, lifecycle, saves, media and audio | [UI, media, lifecycle](../requirements/ui-media-lifecycle.md) |

Each sweep writes its rows into the gap register and updates no coverage row unless
a status turns out wrong (then it fixes the row in the same PR).

### Phase 4: concept documents for what is not implemented

One document per not-yet-built part, after G1, each saying how the part sits on the
target model (identity, residency, time, goals, scripts, persistence), its
requirements, the original's behaviour read or still open, owner questions and
proposed implementation slices. They live in `docs/architecture/concepts/`.

| Slice | Concept | Requirements | Why now |
| --- | --- | --- | --- |
| K1 | Living world beyond the window: sector streaming and simulation of unloaded sectors | FR-WORLD-003, FR-WORLD-004, FR-RUNTIME-001 | Decides H7 for every later feature |
| K2 | Generated main-map sectors in play | FR-WORLD-003, FR-WORLD-005, FR-SCENE-001 | The largest gap of the whole world |
| K3 | NPC daily routine, standpoints, beds, doors and portals for NPCs | FR-AI-001, FR-AI-003 | Not started; touches residency and goals |
| K4 | Monster generators, respawn, body decay, random encounters | FR-AI-004, FR-TRAVEL-003, FR-RUNTIME-001 | Run-time objects (H2) |
| K5 | Summons and teleport effects, teleport bans by area | FR-MAGIC-008, FR-TRAVEL-005 | Run-time objects and moves between maps |
| K6 | Traps and placed devices | FR-ITEM-016 | Run-time objects |
| K7 | Sleep and rest | FR-TRAVEL-004 | Time model |
| K8 | Transition effects and time on transitions | FR-WORLD-015 | Map change order |
| K9 | Kill log and injury history, companions screen | FR-COMBAT-013, FR-UI-004 | Object relations and persistence |
| K10 | Newspapers and gambling | FR-SOCIAL-009, FR-ITEM-019 | Proposed for after alpha; listed for completeness |
| K11 | Network readiness: the change path a future network game needs | FR-NET-001…008, FR-SOCIAL-006 | Owner decision: after alpha ([network](../requirements/network.md)); the single change path is still decided in G1 |
| K12 | Movies, slides, endings, credits | FR-MEDIA-001, FR-MEDIA-002 | Proposed for after alpha |
| K13 | Object directory and field store (added by G1) | FR-WORLD-005, FR-WORLD-010, FR-WORLD-013 | The design of MIG6 ([target model](../architecture/target-model.md#migration-order)) |
| K14 | One goal table for every animated object (added by G1) | FR-RUNTIME-002, FR-RUNTIME-003, FR-AI-003 | The design of MIG13 |
| K15 | Clocks and the frame step in the session (added by G1) | FR-RUNTIME-001, FR-LIGHT-002, FR-UI-008 | The design of MIG12 |
| K16 | Interface mode stack (added by G1) | FR-UI-003, FR-UI-008 | The design of MIG16 |

The list is refreshed by G1: a part that the target model changes enough may get a
concept document even if it is partly built today.

## Work on hold

Owner decision (2026-10-29, Aleksandr, answers "1 - A", "2 - 2"): the plan is taken
as written, and every other bug fix and feature waits while the audit runs. The
follower fix slices V1–V6 of ARC-RES-076 fold into G1. The audit itself does not
change code.

## Size

Phase 1: 6 Mac slices; phase 1b: 1 cloud slice; phase 2: 1; phase 3: 9, mostly
cloud; phase 4: up to 12. About 29 PRs, of which about 20 can run in the cloud. An
order of magnitude, not a promise.
