# Ctrl+B bug reports: second triage on `main`, 2026-11-10

Status: **Triage result**, work item ARC-RES-098 (2026-11-10). Docs-only: no source change, no checks run. It fixes nothing; every
fix below is a proposed task for a new thread, started only after the owner says so. It continues the
[triage of 2026-11-08](bug-reports-triage-2026-11-08.md) (ARC-RES-097) and uses its B-list ([plan](open-bugs-migration-smoke-plan.md),
[re-checks](open-bugs-resmoke-2026-11-04.md)); the new bugs are numbered B47 onward.

Requirements (reference only, no coverage row changes): [FR-MAGIC-006](../requirements/combat-magic-tech.md#fr-magic-006),
[FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007), [FR-MAGIC-008](../requirements/combat-magic-tech.md#fr-magic-008),
[FR-ITEM-001](../requirements/items-economy.md#fr-item-001), [FR-ITEM-002](../requirements/items-economy.md#fr-item-002),
[FR-ITEM-007](../requirements/items-economy.md#fr-item-007), [FR-ITEM-009](../requirements/items-economy.md#fr-item-009),
[FR-ITEM-010](../requirements/items-economy.md#fr-item-010), [FR-LIGHT-009](../requirements/lighting-time.md#fr-light-009),
[FR-UI-001](../requirements/ui-media-lifecycle.md#fr-ui-001), [FR-UI-002](../requirements/ui-media-lifecycle.md#fr-ui-002), [FR-UI-004](../requirements/ui-media-lifecycle.md#fr-ui-004), [FR-UI-008](../requirements/ui-media-lifecycle.md#fr-ui-008),
[FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002).

## Answer

- **10 new reports** came after the last triage (builds `27fcacc216`, `24acf37113`, `98b21fa890`; 2026-11-08 17:18 to 2026-11-09 17:37). They are
  **8 distinct bugs**. None is a regression, and none is covered by a merged PR or a running thread (checked against #888–#904 and the
  running threads: UI-124, DEV-330, DEV-331, DEV-332, DEV-329, DEV-299).
- **Present, cause seen in code or log (4 bugs, 6 reports):** B47 the Summon spell component is not built (Fireflash, Wall of Fire and
  Summon Undead: three reports, one cause); B48 Take all and every other take from a container ignore the room of the hero's grid (the
  original refuses); B49 the quit box is the project's panel, the original's dialog box art exists and is used elsewhere (ARC-WORLD-120); B50 a
  worn lantern gives no light (slice V7d of the lights plan was never built; the hero's part built by ARC-DEV-335).
- **Present, cause open, one reproduction needed first (3 bugs, 3 reports):** B51 a taken note cannot be read (the use gate answers
  "not usable" for a Written item that the host reads; the inv-ui smoke reads another note and passes); B52 a follower's portrait shows two empty
  bars at full health on the temple map; B53 the ghost of Brego is not drawn although its script and dialog run (B20's follow-up).
- **Present, mechanics not read yet (1 report):** B54 "no sort button in the inventory". **Corrected by the owner after the merge of this
  triage:** the original's inventory window has an arrange button (art: scattered items, a red arrow, an ordered grid) that lays the items out
  in two ways, horizontally and vertically. The first version of this triage called it B30 again and "not a bug"; that was wrong, the ledger had
  never read the button (it only has the doll/bag toggles, [WL4](ledger/items.md), and the hit test, [IW14](ledger/ui-audio.md#inventory-grid-arc-ui-019)).
- **Mana on the follower portrait (part of B52) is not in the original either** (ledger [CH9](ledger/ui-audio.md): frame, health bar, fatigue
  bar only); only the empty health and fatigue bars are a defect.
- **Eight proposed fix tasks** (section 5; F8 is B54), sizes S to L. Fourteen older reports whose fix is merged are archived (section 4); four stay.

## 1. Method and its limits

- **Reports.** The 10 new folders read as the player's text, the game-state table, the screenshot and the log (ids, outcomes and
  counts only; no game text is quoted; the folders hold original content and are not committed). Three screenshots were looked at (the
  portrait, the quit box and the cave); one save was read for the note's row and one for the ghost's run.
- **Code.** Each claim about the source names the file and symbol; `origin/main` at `02665caba`.
- **Original executable.** Nothing was read in this task. Every mechanic is cited from the existing ledgers; the rows that a fix has to
  read again are named in section 5.
- **Not done.** No viewer run and no reproduction from a report's save (as in ARC-RES-097): a row says "present (cause seen)" only when a
  line of code or log shows it on current `main`; "cause open" says a reproduction is the first step of the task.
- **Task sizes.** S: one PR of about 200 lines, one thread. M: one PR with a smoke stage, a long thread. L: more than one slice.

## 2. The new reports

| Report | Build | Player's text (short) | Bug |
| --- | --- | --- | --- |
| 2026-11-08_17-18-35 | `27fcacc216` | Fireflash has no explosion | B47 |
| 2026-11-08_17-19-00 | `27fcacc216` | Wall of Fire has no visual at all | B47 |
| 2026-11-08_17-19-23 | `27fcacc216` | Summon Undead has no summoning animation and no summoned creature | B47 |
| 2026-11-08_17-20-07 | `27fcacc216` | Virgil's portrait shows no health and mana | B52 |
| 2026-11-08_17-24-08 | `27fcacc216` | Take all works with no room and what does not fit vanishes | B48 |
| 2026-11-08_17-24-54 | `27fcacc216` | The quit confirmation has no original graphics | B49 |
| 2026-11-08_22-17-50 | `24acf37113` | The inventory has no sort (arrange) button | B54 |
| 2026-11-08_22-18-11 | `24acf37113` | A worn lantern does not light the area around the hero | B50 |
| 2026-11-08_22-18-49 | `24acf37113` | Wilhelmina's note cannot be read (once it could, just after pickup) | B51 |
| 2026-11-09_17-37-49 | `98b21fa890` | The ghost of Brego did not appear in the cave, but the script runs and the dialog started | B53 |

### B47: the Summon component is not built (three reports)

Hero Cymoril, level 5, on the temple map (runtime map 18), Steamwoven rules; the owner was trying spells one after another.

**What the logs show** (same session, the three reports overlap in time).

1. Spell 22 (a missile; the player's "Fireflash"): launch, flight, `missile-end why=arrived`, then `ComponentNotReady value=18` and the
   message line "Summon effect not ready; fatigue -15". The strike has no explosion because the explosion is most likely a Summon (inferred from the log order).
2. Spell 21 (the player's "Wall of Fire"): at the release five `ComponentNotReady value=18` (five Summon components), message "Summon
   effect not ready" five times. The wall is most likely five summoned objects (inferred from the five lines), so nothing is drawn.
3. Spell 57 (the player's "Summon Undead"): `ComponentNotReady value=18` and `value=16`, message "Summon effect not ready; ObjFlag effect
   not ready". No creature, no summoning picture.

**Cause (code).** `SessionSpellCaster.Apply` (`SessionSpellCaster.cs`, the fall-through at the end) logs `ComponentNotReady` for every
component type it does not build; the Summon type is not built. The plan has the slice as **MT17 Summon** and its prerequisite
**P2 Objects created at run time** ([magic plan](magic-implementation-plan.md)), neither started. The ObjFlag line of spell 57 is a flag
other than the hold flags that ARC-DEV-302 built (the entangled, stoned and paralysed flags); the other flags are MT13/MT14 and still open.
B25's closing note says the same about creatures: "no host path creates a critter on the shown map yet" (ARC-DEV-328).

**Read (ARC-RES-101, [note](summon-objects-res-101.md)).** The Summon of spells 21 and 22 makes scenery objects: Wall of Fire is one component applied to five tiles, so the five
`ComponentNotReady` lines are five list entries, not five components; Fireflash's flash is made only when the strike tile is empty at the landing (a creature standing on it gives no flash),
and it is destroyed at the animation's callback frame. Spell 57's second line turns `OSF_MIND_CONTROLLED` on the summoned creature. Slices E1-a and E1-b (objects) and E1-e (creatures) carry the build.

**The original.** MT17's reads exist: summon `0x450C40`, lists `0x452830`/`0x452900`, ledger [MG8 and the summon palette](ledger/magic.md)
(faction from `Clear_Faction:`, palette, run end when the summon dies, 0x459740). What the object of a fire wall or a flash is (a scenery
or an eye-candy object) is not read yet; it is part of the first slice.

**Not in this bug (listed so the table is complete).** Other component types fall through the same `ComponentNotReady` line (Movement,
Cast, Polymorph, the control components: MT15, MT16, MT18). The reports name only Summon.

### B48: a take ignores the hero's room

Report on the main map at the Crash Site. Log: `loot-all taken=45 refused=0 pc=75` for the largest take and `taken=2 refused=0 pc=77` for the last one (the smaller takes between them refuse nothing
either); the items that do not fit are "unplaced" (held, but in no cell of the grid) and are listed only in the window's "no room in the grid" line.

**Cause (code).** `GameSessionBackground.HeroInsertVerdict` (`GameSessionBackground.cs`, lines 82–98) answers `NoRoom` only when its `room`
argument is false, and its own summary says "items entering from outside leave the room to the host (not ready)"; no caller of a take passes
it. `TerrainViewerAreaInventory.TakeAllFromBody` loops `AreaInventoryHost.Loot` and counts every `Moved` item, so nothing is refused
for lack of room. The held-but-unplaced list is the symptom the hero's grid view keeps (`SessionHeroGrid.Unplaced`, the window's "no room in
the grid" line), which is why an item "vanishes".

**The original.** Ledger [WE4](ledger/items.md): the insert check 0x466510 asks for the load, then the Technophobia test, **then the room
(code 2)**; the ground transfer ([IE1](ledger/scripts.md)) and the parented transfer ([IE2](ledger/scripts.md)), which every take from a
container or a body runs, call that check, and a refusal leaves the item where it was. So a take with no free grid place is refused one
item at a time, and Take all moves the items that fit and leaves the rest in the container. Steamwoven's gap is the missing room test, not
a different rule.

**Probably the same gap, unverified** (the ground transfer is IE1; barter, theft and a script's give are not read here, F2 confirms
them): picking an item up from the ground, buying in barter, a theft in the pick-pocket window, and any script that gives an item to the hero.

### B49: the quit box is not the original's dialog box

Main menu, "Exit Game". Screenshot: a flat parchment panel with the question, two grey buttons and a status line that says "original art
and layout NOT_VERIFIED".

**Cause (code).** `AreaMainMenuPanel.cs` (the comment at the art test, "the quit box stays the panel's", and the `QuitPending` branches)
keeps the quit question on the project's panel on purpose (ARC-UI-012).

**The original.** The ledger rows [DB1–DB4](ledger/ui-audio.md#dialog-box-arc-world-120) (ARC-WORLD-120) read the common dialog box of the
original's quit question (art `DialogueBox`, text in `Flare12Font`, the green check `Lil_Accept` and the red cross `Lil_Cancel`), and the
death-load question already draws it (owner decision of 2026-10-29 in [decisions](../architecture/decisions.md)). The quit box is the
same box with another question; the in-play menu's quit question is the same panel and has the same gap.

### B50: a worn lantern gives no light

Main map at night (game time 20:47), the lantern in the hero's worn light slot, inventory window open. The log has no light line at all.

**Cause.** The slice **V7d "Moving and carried lights"** of the [lights plan](lights-on-all-maps.md#steamwoven-slice-v7-proposed) was never built:
[area lighting](../rendering/area-lighting.md) says "Not drawn: moving and carried lights (V7d), shadows". V7a–V7c (object light
descriptions, object lights in map mode, animated lights) are merged.

**The original.** Read and ledgered: a critter's light is the largest of its worn items' light sizes, each shrinking with the item's
condition ([EQ12](ledger/items.md#equipment-arc-world-028)), set again whenever the worn items' light is recomputed
([LT12](ledger/world.md#lights-on-all-maps-arc-res-069), carried light section of the lights study). The wear rule `EquipmentRules.Light`
already returns the size (verification row for 0x467E80), so the slice is the viewer's: draw the hero's light and keep it in step with
wear, take-off and condition (and companions' and NPCs' worn lights, the second part).

### B51: a taken note cannot be read (B32 again)

Main map, inventory window. Log: two `ARC-UI-025 use ... source=UseIcon gate=NotUsable` for the note (a Written item, prototype 14083, held
in the grid, no `made:` entity). The save holds the item as an ordinary Written row of map 1.

**What is known.** `AreaItemUse.Gate` (`AreaItemUse.cs`, line 75) answers `NotUsable` when the item's facts lack flag `OIF_CAN_USE_BOX`
(0x80); the facts are non-null, so the host did read a row. ARC-DEV-306 (B32) fixed the facts of an item taken on another map and the
`inv-ui` smoke reads a note, and the build `24acf37113` already had it; so this note differs from the smoke's (another prototype, or
a flag that the instance row does not carry and the prototype does). The player says it could be read once "just after the pickup".

**Cause: open.** Not found by reading. The first step of the task is the reproduction: load the report's save on current `main`
and read the facts of the note (`AreaInventoryHost.UseFacts`, `TakenRow`, the prototype's and the row's item flags) against the original's use of
a written item (0x462CC0, [reading window](../world/reading-window-runtime.md)). The earlier report of B32 (`2026-10-29_21-50-47`) stays
with it. ARC-DEV-315 (made items in the row-only readers, merged 11-10) does not apply: the item is not a made one.

### B52: a follower's portrait bars are empty

Temple map (runtime map 18), Virgil following the hero. Screenshot, enlarged: the portrait with its name, and below it **two empty black bars**
(the health and fatigue bars) although Virgil is unhurt.

**The original.** Ledger [CH9](ledger/ui-audio.md): a follower frame has frame art 503, the portrait, a health bar (art 505, or 616 while
poisoned) and a fatigue bar (art 504), each cropped to the follower's current/maximum percent. **There is no magick-point bar**, so the
"mana" in the report is not a defect.

**Cause: open.** `AreaFollowerPortraits.cs` says "A follower without combat values shows empty bars"; the bars read
`CompanionLife` (`TerrainViewerAreaLifeBars.cs`, the combat host's values "wherever it stands, or null when the host has none"). A visiting
companion on the temple map may be the case with no values, but nothing in the log proves it. The `life-bars` smoke checks the portraits
only on the main map. The first step is the reproduction on the report's save (the companion's life values on map 18, percent and art
numbers), then the fix.

### B53: the ghost of Brego is not drawn (B20's follow-up)

Crash site cave (runtime map 79), Steamwoven rules. ARC-DEV-298 (B20) made the tile script 2768 run and the ghost's line float; in the report that
part works: `ARC-DEV-298 float ... floated=1`, then a talk click (`script=2789`) and the dialog is open. **The screenshot shows no ghost**,
only the hero, Virgil and the corpse by the bed.

**What the save shows.** The ghost NPC exists (the dialog has its entity) and carries **a spell run of spell 216 targeting it with
the spell flag 0x2000000** (`spells.objectFlags`: `spellSet 33554432`): the flag ARC-DEV-304 reads as "spoken with the dead". So the cast of
script action 61 appears to have run, and the host wrote the flag. The log has no cast line for it (the only `ARC-MAGIC-015 cast` line in the window is
the player's own spell 50), so the script-cast path is silent.

**Cause: open.** Not drawn although alive, with the flag set: either the ghost's art or draw flag (an object flag that the original's draw
loop reads), or the effect that spell 216 adds beyond the flag, is missing. The stage `tile-ghost` checks only the floated line, not
the ghost's picture. The first step is the reproduction in the cave (draw state of the ghost's row; which components spell 216 has and which
of them logged `ComponentNotReady`, as the other spells do in the logs of B47), then one exe read of how the original draws a creature with
that flag. FR-MAGIC-007 (object flags) and FR-SCRIPT-002 (the script's cast).

### B54: the inventory's arrange button is not built

Report text: no sort button in the inventory. The owner's correction (2026-11-10, with a screenshot of the original's window): the original has a
button that arranges the items in two ways, horizontally and vertically (not literally a "sort"). It sits in the inventory window's right
panel, below the two gold fields: an icon of scattered items, a red arrow and an ordered grid.

**State.** Not built in the viewer, and **not read in the executable as such**. The ledger's claim is wrong: [WL4](ledger/items.md) says "The window
has no sort buttons", from a list of the buttons the window's open makes; that list ([IW30](ledger/ui-audio.md), open 0x572815..0x572D85) has, in
every mode, **button 340 `tiny_butt` at (763, 197)**, which the ledger names with no purpose and the viewer does not build: the likely arrange
button (not proved). (The toggles at (23, 108) and (74, 108) in WL4 are the other party's, the loot or steal window's; the hero's own doll/bag
toggles are at (713, 37) and (751, 37), IW30.) IW14 says of the window toggle at 0x68150C that "the toggle's meaning was not read". The earlier
answer for B30 ("the original has no sort buttons", ARC-UI-072) rested on WL4's sentence and is withdrawn here; the WL4 row is corrected by the task below.

**What a task needs.** One executable read of the button (start with button 340 of IW30 and what its handler does; correct the WL4 sentence and the
IW30 row with what is read): its rectangle and art, its handler in the window handler (0x573850 region), the two
layouts (which order, which cells, what a no-fit does, whether the move runs the insert check and the item events of IW12/IW15), and the sound.
Then the viewer's button and layout over the session's grid move (`GameSessionHeroGrid`), with a stage.


## 3. Checked against threads and merged work

| Candidate | Result |
| --- | --- |
| #888–#904 (other-map item scripts, B01 temple, B10 wolf, B12, B18, B25, dead greeting, pick-pocket, death scripts, made items, worldmap smoke, item window lines, HD pack docs) | none covers B47–B54 |
| Running: UI-124 (combat panel), DEV-330 (roof over house containers), DEV-331 (ai-cast leaving the hero entangled), DEV-332 (main menu music), DEV-299 (town containers; B23) | none covers B47–B54; DEV-331 and DEV-299 hold two old reports, see section 4 |
| ARC-DEV-306 (B32 mouse drop, off-map item facts) and ARC-DEV-305/315 (made item facts) | do not close B51 (section 2); the made-item readers are not the case |
| ARC-DEV-298 (B20) | closed the floated line only; B53 is its follow-up |
| ARC-WORLD-120 (death load box) | has the original's dialog box (B49 reuses it) |

## 4. The stayed reports and the archive record

The 18 older reports still in `reports/` (three of them, B12 twice and B04, were marked "yes" by the last triage but never moved) are judged against `origin/main` `02665caba`.

| Report | Bug | State | Archive |
| --- | --- | --- | --- |
| 2026-10-29_17-16-19 | B17, B18 | B17 fixed (DEV-171); B18 fixed (DEV-326, #891, the examine script) | yes |
| 2026-10-29_18-02-58 | B36 | fixed: made items have facts (DEV-305, #863; the row-only readers DEV-315, #899) | yes |
| 2026-10-29_18-04-32 | B24 | fixed (DEV-304, #877, spirit of the dead; DEV-324, #894, the generic greeting) | yes |
| 2026-10-29_21-09-10 | B01 | fixed (DEV-314, #892, the stage `inv-carry` step 5 on the temple map) | yes |
| 2026-10-29_21-10-07 | B01 | as above | yes |
| 2026-10-29_21-20-33 | B41 | gone: the `inv-ui` stage opens the Spells button (DEV-306, in `24acf37113`) | yes |
| 2026-10-29_21-26-10 | B21 | the refusal is the original's rule; the printed line reaches the message window (DEV-309) and the death scripts open the flag (DEV-312, #897) | yes |
| 2026-10-29_21-44-16 | B37 | fixed: the hover window's fourth line (ARC-UI-107, #862) | yes |
| 2026-10-29_21-43-18 | B12 | gone (ARC-DEV-327 read the original: the centre is the click's tile for a far body; stage `cast-multi`); archived "yes" by the last triage, not moved | yes |
| 2026-10-29_21-46-22 | B12 | as above | yes |
| 2026-10-29_21-47-12 | B04 | fixed (ARC-DEV-310, #858, the cave's pile dissolves when emptied; stage `junk`); archived "yes" by the last triage, not moved | yes |
| 2026-10-29_21-48-45 | B31 | fixed: a mouse drop on the hotkey slot (DEV-306) | yes |
| 2026-10-31_12-48-49 | B07 | no defect found by the census stage (DEV-308, #861) | yes |
| 2026-10-31_12-51-38 | B07 | as above | yes |
| 2026-10-29_17-44-57 | B11 | hold flags (DEV-302, #867) and Effect (DEV-303, #900) merged; DEV-331 (the ai-cast smoke leaves the hero entangled) runs | no, until DEV-331 merges |
| 2026-11-08_13-30-55 | entangle visual | ends with the caster (DEV-301, #865), the hold built; same running DEV-331 | no, until DEV-331 merges |
| 2026-10-29_21-16-23 | B23 | DEV-299 re-checked: trash cans work, the report clicked a locked door; the roof over 9 house containers is DEV-330; thread not merged | no, until DEV-299 merges |
| 2026-10-29_21-50-47 | B32 | DEV-306 merged, but B51 shows a note that still cannot be read | no, with B51 |

The 14 reports marked "yes" are moved on the owner's Mac, whole folder unchanged, into `artifacts/packages/macos/reports/archive/` with one
INDEX line each ([the rule](../player/alpha-build.md#9-reporting-a-bug-ctrlb), ARC-DEV-056). The 10 new reports stay in `reports/` (no
fix thread has started for them yet; each moves when its task code is given). The other reports of the last triage were archived before.

## 5. Proposed fix tasks (one task = one thread; codes are the coordinator's)

| Task | Bugs (reports) | Cause | Proposed fix | FR groups | Size |
| --- | --- | --- | --- | --- | --- |
| **F1: Summon and objects made at run time** | B47 (3: Fireflash, Wall of Fire, Summon Undead) | MT17 and P2 of the magic plan are not built; the Summon component falls through to `ComponentNotReady` | Three slices: (a) the session creates and removes an object (a scenery or an eye-candy object) on the shown map from a prototype, saved as map-local state (P2; also what B25's remainder and the monster generators need); (b) the Summon component for those objects, their end at the run's END (the flash, the wall) with a stage per spell; (c) the Summon component for creatures: prototype or list pick, summoned flags, faction (`Clear_Faction:`, MG8), palette, the run's end when the summon dies (0x459740), and the ObjFlag flags of Summon Undead (MT14 part). Read first: what object a flash and a wall create (exe) | FR-MAGIC-008, FR-MAGIC-006, FR-MAGIC-007 | L (3 slices) |
| **F2: A take is refused when there is no room** (done by ARC-DEV-333) | B48 (1) | `HeroInsertVerdict(room)` is never false for an incoming item (`GameSessionBackground.cs`) | Give the insert check the room test for every take: Take all and one item from a container or a body, a pick-up from the ground, a purchase, a theft, a script's give (ledger WE4, IE1, IE2); the refusal's text read in the original; Take all leaves what does not fit; no more "unplaced" items from new takes; a stage `loot-room` (fill the grid, take all, the rest stays) | FR-ITEM-001, FR-ITEM-002, FR-UI-004 | S to M |
| **F3: Quit box in the original's dialog box** | B49 (1) | The quit question stays on the project's panel (`AreaMainMenuPanel.cs`) | Draw the main menu's "Exit Game" question and the in-play quit question in the original's dialog box of ARC-WORLD-120 (ledger DB1–DB4); keep the keys and the question text; one stage line of the main-menu self-test | FR-UI-001, FR-UI-008 | S |
| **F4: Carried lights (V7d)** | B50 (1) | Slice V7d of the lights plan was never built | Draw the hero's worn light (largest worn light size, condition shrink, EQ12, LT12) and keep it in step with wear, take-off and condition; second part: companions' and NPCs' worn lights; a stage `worn-light`. First part (the hero, the stage) built by ARC-DEV-335; the second part (the critters' saved lights carried with them) built by ARC-DEV-353 | FR-LIGHT-009, FR-ITEM-007 | M |
| **F5: A taken note can be read** | B51 (1), with B32 (1 older) | Open: the use gate answers "not usable" for a Written item whose facts the host read | Reproduce on the report's save; read the note's item flags (instance row against prototype) and the original's use of a written item (0x462CC0); fix the facts read; the `inv-ui` stage reads the report's kind of note (taken at the Crash Site wreck) | FR-ITEM-010, FR-ITEM-009 | S to M |
| **F6: A follower's portrait bars** | B52 (1) | Open: empty bars at full health on the temple map | Reproduce on the report's save (the companion's life values on map 18); fix the values or the art; extend the `life-bars` smoke to a visiting companion. The magick-point bar is not in the original (CH9) and is not built | FR-UI-002, FR-UI-004 | S |
| **F7: The ghost of Brego is drawn** | B53 (1), B20's follow-up | Open: the ghost exists, carries the flag of spell 216, and is not drawn | Reproduce in the cave; find which component of spell 216 or which draw flag the original reads; one exe read of the draw rule; extend the stage `tile-ghost` to check the ghost is drawn | FR-MAGIC-007, FR-SCRIPT-002 | S to M |
| **F8: The inventory's arrange button** (added after the owner's correction) | B54 (1) | Not built, mechanics not read | Read the button in the executable (art, rectangle, handler, the horizontal and the vertical layout, no-fit rule, sound), then build the button and both layouts over the session's grid move; stage `inv-arrange` | FR-UI-004, FR-ITEM-001 | M (read first) |

**Not tasks.** The remaining ObjFlag flags (MT13, MT14) and the other
not-ready component types (Movement, Cast, Polymorph: MT15, MT16, MT18) are separate slices of the magic plan, not proposed here.

**Order suggestion.** F2, F3 and F6 are small and independent; F8 starts with an executable read and is independent of the others; F1 is the largest and unblocks most visible spell gaps; F4 is independent. F5 and F7
each start with a reproduction and may collapse to a one-line fix. No two tasks touch the same files except F1(a) and the B25 remainder,
which F1 owns.

## 6. Archive record

The 14 reports with "yes" in section 4 were moved on 2026-11-10, each with its INDEX line, by this task. The 10 new reports and the four
"no" rows stay in `reports/`.
