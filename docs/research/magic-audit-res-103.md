# Magic audit: the 80 college spells, what runs and the order of work

Status: **Research and plan, mostly built** (ARC-RES-103, 2026-11-11; trimmed the same day: the per-spell matrix, the done slice rows and the waves are in git history, the census test only checks each record's `ComponentNotReady` set; the matrix and the prototypes and lists Summon needs (28310-28313, 28464, 28443, 17286, 28318; lists 3, 4 and 5 for spells 74, 53 and 57) are in git history). It audited every spell of the 16
colleges (spells 0 to 79, five ranks each) against the installed data, the code and the smoke stages, and ordered the work by mechanic. It **builds on and does not replace** the
[magic implementation plan](magic-implementation-plan.md) (MT slices), epics E1 and E4 of the [missing-systems plan](../project/missing-systems-plan.md) and the Summon note [ARC-RES-101](ledger/spell-runs.md); `MA-n` are this audit's planning
labels, not task codes.

Requirements (reference only): [FR-MAGIC-006](../requirements/combat-magic-tech.md#fr-magic-006), [FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007), [FR-MAGIC-008](../requirements/combat-magic-tech.md#fr-magic-008),
[FR-MAGIC-009](../requirements/combat-magic-tech.md#fr-magic-009), [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012), [FR-CHAR-022](../requirements/characters-skills.md#fr-char-022),
[FR-SOCIAL-001](../requirements/story-social-ai.md#fr-social-001), [FR-SOCIAL-003](../requirements/story-social-ai.md#fr-social-003), [FR-AI-002](../requirements/story-social-ai.md#fr-ai-002),
[FR-TRAVEL-005](../requirements/world-travel-runtime.md#fr-travel-005).

## Answer

- **The audit found three holes: `ObjFlag`, Summon and the inert effect resistances.** The first and third are closed: MA-1 (ARC-DEV-358) writes every spell, object and critter flag with the original's reference count and splits the cast host's dispatch into a table
  (`SessionSpellCasterComponents.cs`, one partial file per component group); MA-2 (ARC-MAGIC-027, with ARC-MAGIC-033) lets resistance and stat changes of effect lists reach the combat resolver and the magic save for the hero and every NPC (E1-d, ARC-DEV-348, gave every critter its effect list).
  Summon of scenery is built (E1-a ARC-DEV-347, E1-b ARC-DEV-352) and created critters exist (E1-c, ARC-DEV-359, -370, -371); **Summon of critters is built (E1-e, ARC-MAGIC-053; not built: `Clear_Faction:`, `Palette:`, the concealed start SUM19).**
- **Done slices:** MA-0 the spell census (ARC-DEV-356), MA-R the executable reads (ARC-DEV-357, ledger XR1-XR15), MA-1, MA-2, MA-3 Dispel (ARC-MAGIC-034, -039), MA-4 social and AI flags (ARC-MAGIC-035), MA-5 Movement (ARC-MAGIC-036, -038), MA-6 locks and holds (ARC-MAGIC-037),
  MA-7 Identify (ARC-MAGIC-032), MA-8 Trait and the body spells (ARC-MAGIC-040, -046, -047, -052), MA-9 Tempus Fugit (ARC-MAGIC-056). The control components that belong to the technology records and items (NoOp, Terminate, branches, Use, nested Cast) are ARC-MAGIC-044, -048 and -049.
- **Open:** E1-e creature Summon (14, 19, 24, 29, 53, 57, 68, 71-74), E1-f AI summoning, E6 trap spotting (spell 8), MA-10 the raise script hook (58, 64), the hero and companion push of Unseen Force (2), and the windows of Identify (E8) ([slices](#slices), [open spells](#open-spells)).
- **Only seven spells are named by a smoke stage** (51, 56 and, by its data filter, 55; since ARC-DEV-1148 30, 46, 76 and 77 by the stage `spell-effects`); the others are covered by unit tests of their components and by the census. "Runs" is a code-path claim, not parity (parity stays with the [ledger](original-verification-ledger.md)).
- **Decisions for the owner** were six; the owner decided all of them on 2026-11-11 (D1 a, D2 a, D3 b, D4 a, D5 a, D6 c) ([decisions](#decisions-for-the-owner)).

## Method and limits

- **Data.** A scratch console program (not committed) read the owner's unchanged install (`Version.txt` 1.0.7.4) with the repository's own spell reader in like-original mode: for spells 0..139 every record's cost, maintenance, duration, save, area line, AI priorities and components. Numbers, flag names and spell names only.
- **Code.** `origin/main` at `c53887a94` (2026-11-11) for the audit; the slices since changed what is said above, not the method.
- **States.** A component not ready leaves `ComponentNotReady` in the cast log and a "not ready" line on the spell pane (owner decision D2a), so an unbuilt spell tells the player; it does not fail silently.
- **Independent check.** A fresh agent re-reads the exact diff of every slice before its PR (the review step of the [task rules](../workflow/tasks.md#review-publication-and-merge)).

## What the cast pipeline already does

Everything before the components works for any caster and map (ledger [invocation](ledger/spell-runs.md#spell-invocation-arc-magic-006), [target areas](ledger/spell-runs.md#spell-target-areas-arc-magic-007),
[runs](ledger/spell-runs.md#spell-runs-arc-magic-009), [processing](ledger/spell-runs.md#spell-run-processing-arc-magic-010), [per-target stage](ledger/spell-runs.md#spell-per-target-stage-arc-magic-011),
[cost](ledger/magic.md#spell-cost-and-payment-arc-magic-003), [resistance](ledger/magic.md#spell-resistance-arc-magic-005)): the checks, the cost and payment, the targets of each action, the save, the anti-magic shell and reflection, the run table with BEGIN, MAINTAIN, END
and the callbacks, the maintain bar, saved runs of the active map, the endings by death, knock-out and the AI gate, the spell pane and hotkeys, the casting goal with its missile and eye candy, the AI's choice from its ten lists. 53 of the 80 spells are maintained and two are timed.

Components still not ready for the colleges: **Destroy** for critters and containers (E4-c); **AGoal** knockback pushes NPCs, not the hero or companions; **Heal**'s raise script (point 0x12) runs no SCR (MA-10);
effect 157 (spot-trap skill, spell 8) is inert because skills are not modelled by the durable effects; the flags' readers: the combat and life rules do not read a written undead bit, the viewer does not draw translucent, shrunk or hidden critters, the NPC flag KOS_OVERRIDE of Charm (35, 50) is not kept.

## Open spells

Every other college spell has a code path for every component of its record (the census, [below](#spell-census-ma-0), is the check).

| Spell | What is missing | Slice |
| --- | --- | --- |
| 2 Unseen Force | The original also pushes ground items of weight below 5 ([UO1–UO9](ledger/spell-runs.md#small-executable-reads-illuminate-unseen-force-on-objects-disarm-arc-res-1109)); **built by ARC-MAGIC-1230**: the item body of the knockback host, the saved cell (`item.moved`, no schema step) and the bump on the item's durability | done |
| 6, 7, 9 See Contents, Read Aura, Divine Magick | The answer is a text line; the windows (container inventory in mode 4, the NPC sheet, the item description) and picking an inventory item as a target are E8's | E8 |
| 8 Sense Hidden | Trap spotting for the detecting-traps flag works and the hero's spot roll exists (ARC-MAGIC-057); effect 157 (spot-trap skill): the hero's skill level reads the written effect's skill change since the skills lane, so the roll should see it, not run in the viewer | owner recheck |
| 14, 19, 24, 29 Elemental summons; 53 Succour Beast; 57 Summon Undead; 68 Phantasmal Fiend; 71-74 Orcish Champion, Guardian Ogre, Hellgate, Familiar | Creature Summon (prototype or list pick, flags on the summoned, run ends when it dies) | E1-e |
| 35, 50 Charm, Charm Beast | The NPC flag KOS_OVERRIDE a charm adds is not kept (the session has no NPC flag layer) | - |
| 47 Shrink, 34 Disintegrate, 69 Invisibility, 63 Sanctuary | The flags are written; the viewer's draw of shrunk, hidden, translucent critters is not built | render |
| 48 Flesh to Stone | Stone render side effect; the stone-to-flesh pair on a portal holding the stoned spell flag | - |
| 58 Create Undead, 64 Resurrect | The raise script hook; the combat and life rules read the data's undead bit, not the written one | MA-10 |
| 65 Illuminate | The original lights the scene: a live object light on the target while the spell runs ([IW1–IW6](ledger/spell-runs.md#small-executable-reads-illuminate-unseen-force-on-objects-disarm-arc-res-1109)); not built (no light is built on an object, the viewer draws saved object lights only) | lighting lane |
| 76, 77 Congeal Time, Hasten | Speed reaches the turn-based action points and the real-time attack pause, not walking (the original does not either, XR); stage `spell-effects` (ARC-DEV-1148) casts both: BEGIN adds the effect and moves the speed (77 up, 76 down); **found there: the END of 76 leaves its effect on the NPC** (its END area names the run's object list, which no host fills), so Congeal Time never wears off | [#1234](https://github.com/Simarglok/arcanum-steamwoven/issues/1234) |
| 79 Tempus Fugit | Built (MA-9, ARC-MAGIC-056): +10 speed to its holders and -10 to every other critter while any critter holds it (the original: while the player holds it, XR6); the follower flag writes of XR7 are not built | - |

## Work order

### Slices

Labels `E1-*`, `E4-*` and `MT*` are the existing plans'; `MA-*` are this audit's. Every slice reads the functions it implements in the executable twice, adds ledger rows
(the [rule of the magic plan](magic-implementation-plan.md#checking-against-the-original)), updates its coverage rows and edits its spells in the census (MA-0) in the same PR.
Order: E1-e before E1-f (one writer in the Summon and AI files); E6, MA-9 and MA-10 are independent of them and of each other.

| Slice | Content | Unlocks (spells) | Size | Needs | Lane and files | FR |
| --- | --- | --- | --- | --- | --- | --- |
| **E1-e** Summon of critters | Prototype or list pick, summoned flags, palette, faction (MG8), run ends when the summon dies, summoned follower; the familiar hidden with the party on the world map (XR14 (b), decided by the owner: [Decisions](#decisions-for-the-owner)) | 14, 19, 24, 29, 53, 57, 68, 71, 72, 73, 74 | L | E1-c (done), MA-4 (done) | Summon partial file | FR-MAGIC-008 |
| **E6** Trap spotting (spot roll; the E6-a row of the missing-systems plan) | The spot roll and the detecting-traps flag (spell 8's second flag); **spot half done, ARC-MAGIC-057; disarm half done, ARC-SKILLS-1156** | 8 | M | MA-1 (done) | traps | FR-ITEM-016 |
| **MA-9** Tempus Fugit | Implement what MA-R read (XR6); the XR14 (a) fix decided by the owner ([Decisions](#decisions-for-the-owner)) | 79 | S-M | MA-R, MA-1 (done) | done: ARC-MAGIC-056, session and stat files | FR-MAGIC-007 |
| **MA-10** Raise script hook | The resurrect script (point 0x12) through the SCR dispatcher | edge of 58, 64 | S | the SCR dispatcher (E5) | scripts | FR-MAGIC-007 |
| **E1-f** AI summoning and AI parity | The AI's summon list and one pass of `ai-cast` over every AI priority of the 80 (an NPC casting a spell must act like the hero's cast) | 60 spells carry an AI priority | M | E1-e | AI host | FR-AI-002 |

### By college

After the open slices every college works: Summoning and the elemental colleges' summons (Air, Earth, Fire, Water), Nature (Succour Beast) and Necromantic (black) (Summon Undead) wait on E1-e; Divination on E6 and E8's windows;
Temporal done by MA-9; Conveyance on the hero's push (Unseen Force). The other colleges (Force, Mental, Meta, Morph, Necromantic (white), Phantasm apart from 68) have no open row in the table above; the owner re-check steps are in the [owner recheck list](../project/owner-recheck-list.md).

## Spell census (MA-0)

ARC-DEV-356: the opt-in test `SpellCensusRealDataTests` in `tests/Arcanum.Session.Tests` (`DAT_ROOT`, row in the [opt-in table](../workflow/toolchain.md#opt-in-original-data), a permanent check by owner decision D4a). For every record 0..139 it reads the installed records and
`rules/effect.mes` like the original, applies each component of every action (BEGIN, MAINTAIN, END, CALLBACK, END_CALLBACK) through the cast host's `Apply` to two synthetic targets (the hero and an NPC of the map), each on a fresh session, and compares the set of components logged as
`ComponentNotReady` with its table; a change prints the new rows to paste, and each slice edits the table in its own PR.

- It checks the **dispatch** only: whether the host takes a component on a PC and on an NPC. It runs no invocation check, area, save, resistance or run action, so a taken component may still be partial or inert, and a not-ready one may be ready on another object (Destroy takes lying items only; a portal's lock flag is not ready on a critter by design).
- Since the Summon component (ARC-DEV-352) both targets take it on every record: an object entry summons nothing in the original (TA9), so the census cannot tell a scenery summon from a critter summon; unit tests hold the critter case. The per-spell hero cast the slice row named was left out (it depends on save rolls and the event pass).
- Records 80..139 share the gaps; three not-ready cases the colleges lack were `Recharge` (103), `NoOp` (129, 130; NoOp since ARC-MAGIC-044) and an AGoal other than 43 and 44 (89). Record 85 adds effect 145 with cause injury, which the session refuses from a spell (`EffectNotSaved`).

## Dispel (MA-3, ARC-MAGIC-034)

Done ([host contract](../world/spell-cast-host.md#dispel-arc-magic-034), ledger [DP1-DP3](ledger/spell-runs.md#dispel-component-arc-magic-034)): spells 41 and 42 end every non-technological run the target is the source of, aimed at or listed in (not the dispelling run), clear a stoned critter (ARC-MAGIC-039 also its stoned object flag) and the held portal and container bit (MA-6), destroy a summoned created critter, and undo a perma-polymorph (ARC-MAGIC-047).
Found: the run's object block at +0xA0 is its **target**, not its other object (0x455C30), which fixed the object-gone endings. Left: the run's other object list (+0x130).

## Effect consumers (MA-2, ARC-MAGIC-027)

Done ([contract](../world/durable-effects-runtime.md#consumers-of-resistance-and-stat-changes-arc-magic-027), ledger [RE1-RE3](ledger/magic.md#effect-consumers-arc-magic-027)): the effective resistance (0x43D6D0) puts effects after the armour and the poison bonus and before the clamp to 0..95; the hero's effects and every NPC's list feed attacks, spell damage and poison; an NPC's speed and strength reach its combat values, attack pause and the round's action points.
The original's magic save does not read the effects (RE2); the owner decided D6 (c): Resist Magick (40) works in both modes, done by ARC-MAGIC-033 ([RS11-RS13](ledger/magic.md#resist-magick-in-the-magic-save-arc-magic-033)), also in the AI's predictor.
Left: an NPC's life facts (maximum hit points, poison tick) ignore effect-changed strength, constitution and resistance; the character sheet's resistance lines show no effect changes; the hero's innate (race, gender) effects have no resistance changes; the stage `spell-effects` (ARC-DEV-1148) names 30, 46, 76 and 77 (BEGIN adds the effects and moves the stats; a forced END takes them off for 30, 46 and 77 and, a known gap, not for 76).

## Identify (MA-7, ARC-MAGIC-032)

Done by owner decision D5(a): magic builds the state and a text answer, the windows are E8's ([contract](../world/spell-cast-host.md#identify-arc-magic-032), ledger [XR1-XR5](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)). The original's Identify writes nothing and shows a window to the casting local player only, so an NPC caster's has no effect.
The hero's answer is a typed event and a message line by the target's type (`IdentifyCritter`, `IdentifyContainer`, `IdentifyItem`); Divine Magick's identified flag is the session's `MarkItemIdentified` mark (no schema step), now read by the weapon spell pane too.
Left: nothing picks an inventory item as a spell target (spell 9 cannot be cast from the viewer); the three windows; what inventory mode 4 lets the player move (XR3); a clear of the identified flag stays "not ready" (no installed record has one).

## Social and AI flags (MA-4, ARC-MAGIC-035)

Done ([contract](../world/spell-cast-host.md#social-and-ai-flags-arc-magic-035), ledger [SO1-SO8](ledger/spell-runs.md#social-and-ai-flags-arc-magic-035)): charm, mind control, fleeing, blinded and undead in the original's order (0x454A10 runs the side effects with the run's parent as the caster; fleeing, blinded and a non-NPC's mind control return before the closing write).
Left: the NPC flag KOS_OVERRIDE (SO2); a follower of a caster that is no hero; the undead flag's readers (the combat and life rules read the data's bit); created critters (E1-e); the AI host's reaction facts carry `MindControlledByPc` false; no viewer smoke stage drives these spells ([owner recheck list](../project/owner-recheck-list.md)).

## Movement (MA-5, ARC-MAGIC-036)

Done in two halves (owner decision D3b): Spatial Distortion (3) by ARC-MAGIC-036 ([contract](../world/spell-cast-host.md#movement-component-arc-magic-036), ledger [MV1-MV3](ledger/spell-runs.md#movement-component-and-the-immediate-path-relocation-arc-magic-036): the immediate path relocation 0x455350, at most 8 tiles, tile scripts run before the placement) and
Teleportation (4), the random tile and the area portal (record 218) by ARC-MAGIC-038 ([contract](../world/spell-cast-host.md#teleport-types-and-the-anti-teleport-test-arc-magic-038), ledger [TP1-TP5](ledger/spell-runs.md#teleport-types-and-the-anti-teleport-test-arc-magic-038): `Rules\AntiTeleport.mes` refuses a disabled map or a region's radius; in single player Teleportation opens the world map in a teleport mode and a pick within 320 lands the hero, paid then).
Left: the multiplayer host branch of type 2; an NPC or other non-critter as the portal or relocation target; the town map, the compass and the waypoint journey inside the teleport mode; message 640 as the game's text; the interrupt of goals other than walks; the viewer's `Tgt_Tile_Pathable_To` test answers "not blocked", so a cast at an unreachable tile is paid and moves nothing (as the original's component does).

## Locks and holds (MA-6, ARC-MAGIC-037)

Done ([contract](../world/spell-cast-host.md#locks-and-holds-on-portals-and-containers-arc-magic-037), ledger [LM1-LM6](ledger/spell-runs.md#lock-flags-of-portals-and-containers-arc-magic-037)): Unlocking Cantrip (1), Magelock (75) and the Dispel clear of the held bit on the doors and chests of the shown map. A spell's lock write is a plain field write, so Unlocking Cantrip schedules no Lock event (a door it opened stays unlocked); only the clear of the locked bit has side effects (aptitude gate above level 19, the "resisted" feedback, the guard alert).
Left: the guard alert's witnesses (0x4AEE50); Flesh to Stone's pair for a portal holding the stoned spell flag (no installed record makes one); the screen refresh; no smoke stage casts at a door ([owner recheck list](../project/owner-recheck-list.md#arc-magic-037--locks-and-holds-unlocking-cantrip-and-magelock-ma-6-2026-11-11)).

## Trait (MA-8, ARC-MAGIC-040)

Done in four slices ([host contract](../world/spell-cast-host.md#trait-component-arc-magic-040), ledger [TR1-TR6](ledger/spell-runs.md#trait-component-arc-magic-040)): the session keeps the species and palette a Trait chose per critter (schema 89, `-1` restores; no reference count, as the original) (ARC-MAGIC-040); the viewer draws a hosted NPC's monster body and moves the hero's worn items (ARC-MAGIC-046);
critter flags 2 and blit flags are the session's with MG10's switch and the Dispel undo (ARC-MAGIC-047, schema 90); the hero's own monster body is drawn (ARC-MAGIC-052). The original takes **every** worn item off, not only what the new art cannot wield; an art builder range violation (species 32 or more, palette 4 or more) exits the original and is "not ready" here.
Left: the dropped leftover of a refused take-off; an NPC's worn items (not tracked by the session); the redraw of a map NPC whose data body is a perma-polymorph (no such NPC in the installed maps: 0 of 3,792 critters, ARC-MAGIC-1220); a polymorphed hero's low attack and throw; the drowning damage of the water-walking clear (OB2); the multiplayer packet. Options, data findings and a recommendation for each limit: [ARC-RES-145](trait-polymorph-remainder-res-145.md).

## Tempus Fugit (MA-9, ARC-MAGIC-056)

Done ([contract](../world/character-stats-runtime.md#tempus-fugit-speed-arc-magic-056), ledger [TF1-TF3](ledger/spell-runs.md#tempus-fugit-speed-arc-magic-056)): speed (stat 11) of the hero and of every NPC handed out by `WithNpcEffects` takes +10 for a critter that holds `OSF_TEMPUS_FUGIT` and -10 for every other critter while some critter holds it, before the crippled-leg -5 and the effects, clamped by the stat range; it reaches the turn-based action points, the real-time attack pause, the inventory line and the sheet, not walking (XR8). The one rule that differs from the original is the owner's XR14 (a) fix: the original tests only the player's flag, Steamwoven any critter's, so an NPC's cast works without the player holding it (who gains: the holders; who is slowed: everyone else, the player included).
Left: the follower join and leave flag writes (XR7, TF3); no smoke stage casts it ([owner recheck list](../project/owner-recheck-list.md#arc-magic-056--tempus-fugit-speed-spell-79)).

## Decisions for the owner

| # | Question | Options | Recommendation and decision |
| --- | --- | --- | --- |
| D1 | Work by component (flags, effects, summon...) as above and report by college, or finish one college at a time? | (a) component waves; (b) one college at a time, Necromantic (white), then Force, Meta, Conveyance, Temporal; (c) mixed: component waves, but wave 2 ordered so Force, Meta and Conveyance close first | (c): it is the same slices, ordered by what closes a college, so the report stays readable **Decided: owner chose (a), 2026-11-11 (waves by mechanic; the report stays by college).** |
| D2 | While a spell has missing components, does the spell pane keep offering it with the "not ready" line (today), hide it, or refuse the cast before payment? | (a) keep (today); (b) hide spells with an unbuilt component from the pane; (c) refuse before payment | (a): hiding hides progress and (c) changes the cast flow in a way no record describes; the line is already shown **Decided: owner chose (a), 2026-11-11.** |
| D3 | Teleportation scope: one slice, or split? | (a) one slice (MA-5) with tile movement, the world-map teleport, the no-teleport areas and followers; (b) split MA-5: tile movement first (spell 3), the rest second | (b): spell 3 needs no area data; the bans and the followers need the FR-TRAVEL-005 data **Decided: owner chose (b), 2026-11-11: MA-5 starts with tile movement (spell 3).** |
| D4 | Keep the census (MA-0) as a permanent opt-in real-data check in the toolchain table? | (a) yes; (b) one-off script, not kept | (a): it is the only way the matrix stays true; it adds one row to the opt-in table and runs only with `DAT_ROOT` **Decided: owner chose (a), 2026-11-11: MA-0 stays a permanent opt-in check.** |
| D5 | Identify (spells 6, 7, 9) needs a window the original has and the viewer does not: inside the magic lane or inside E8? | (a) magic lane builds session state and a text answer, E8 the window; (b) the whole thing in E8; (c) both in the magic lane | (a) **Decided: owner chose (a), 2026-11-11.** |
| D6 | Resist Magick (40) does nothing for the saves in the original (RE2). Fix it? | (a) as the original; (b) fixed in Steamwoven only, "Original" setting; (c) fixed in both modes | (b) **Decided: owner chose (c), 2026-11-11: fixed in both modes, no "Original" setting.** |

**Decided by the owner (2026-11-11): the two XR14 anomalies are bugs of the game, fixed in both modes with no switch** (the owner's bug rule of 2026-10-28: no "Original"/"Steamwoven" (D8) setting for either). (a) is done by MA-9 (ARC-MAGIC-056); (b) stays open until E1-e merges:

| XR14 | The original | Steamwoven, both modes | Slice |
| --- | --- | --- | --- |
| (a) Tempus Fugit | Speed changes only while the **local player** holds the spell flag ([XR6](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)); an NPC's cast changes nobody's speed unless the player holds it too | An NPC's Tempus Fugit takes effect without the player holding it. The rule is stated in [MA-9](#tempus-fugit-ma-9-arc-magic-056) and recorded as a difference from the original (ledger TF1) | MA-9, done (ARC-MAGIC-056) |
| (b) Familiar on the world map | The travel hide tests the caster-side `OSF_FAMILIAR` on the creature, which carries `ONF_FAMILIAR`, so the familiar stays shown ([XR10](ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)) | The familiar is hidden and shown with the party like the PC and the non-summoned followers | E1-e |

Ledger row XR14 reads "decided" for (a) with its difference row TF1; E1-e does the same for (b) next to XR10.

The 60 records outside the colleges (80..139, used by monsters and items) share every gap above; the census covers them.

## Not read, not verified

- Whether the maintained damage of the clouds (11, 27) hits the same targets as the BEGIN: not run per spell. Read by ARC-RES-1109 ([ledger](ledger/spell-runs.md#small-executable-reads-illuminate-unseen-force-on-objects-disarm-arc-res-1109)): Disarm's weapon drop is not built for a spell (DW2; the original drops, XR15), Unseen Force (2) pushes ground items in the original (UO5) and, since ARC-MAGIC-1230, in the viewer (UO6), Illuminate (65) lights the scene in the original and not in the viewer (IW5, IW6).
- Left open by the executable reads (ledger XR1-XR15): what inventory mode 4 lets the player move, what happens on arrival to a summoned follower the travel hide left shown, whether a familiar's death reaches the follower removal.
- The AI's use of each spell was not audited beyond the priorities in the records (60 of the 80 have one); a component that is not ready is not ready for NPCs either.
- Records 80..139 and the technology records (140..222) are outside the per-spell view.
