# Ctrl+B bug reports: triage on `main`, 2026-11-08

Status: **Triage result**, work item ARC-RES-097 (2026-11-08). Docs-only: no source change, no checks run. It fixes nothing; every
fix below is a proposed task for a new thread, started only after the owner says so.

Requirements (reference only, no coverage row changes): [FR-MAGIC-006](../requirements/combat-magic-tech.md#fr-magic-006),
[FR-MAGIC-007](../requirements/combat-magic-tech.md#fr-magic-007), [FR-ITEM-005](../requirements/items-economy.md#fr-item-005),
[FR-ITEM-009](../requirements/items-economy.md#fr-item-009), [FR-ITEM-010](../requirements/items-economy.md#fr-item-010),
[FR-UI-004](../requirements/ui-media-lifecycle.md#fr-ui-004), [FR-WORLD-003](../requirements/world.md#fr-world-003).

## Answer

- **45 reports** sat in `artifacts/packages/macos/reports/` outside `archive/` (44 of 2026-10-29 and 2026-10-31, already catalogued as
  B01-B46 in the [open-bugs plan](open-bugs-migration-smoke-plan.md), and **one new**, 2026-11-08 13:30). None was in
  `archive/INDEX.md`. The older ones are judged against `main` `a56343bcf` through the [re-checks](open-bugs-resmoke-2026-11-04.md)
  and the merged fixes since 2026-11-04.
- **26 reports are handled** (a fix or regression thread of their own merged, or the owner called the case the original's
  behaviour) and are moved to `reports/archive/` with one INDEX line each (section 5). **19 stay** in `reports/`: 1 belongs to a thread
  that runs now (B20), 4 to bugs queued by the coordinator (B11, B12 twice, B18), the new one, and 13 that no thread owns yet
  (B01 twice, B04, B07 twice, B21, B23, B24, B31, B32, B36, B37, B41).
- **The new report (entangle visual stays after the caster dies) is two missing pieces, not one**, and neither is a regression (section 2):
  the spell run is never ended when its caster dies (the original ends it in the kill routine), and the spell's hold component and the
  Effect component are still not executed at all, so the hero was never held. The second piece is B11's remainder (and B24's).
- **Bugs that no thread owns yet, with a cause found in code or log:** B36 (a made item has no wear facts), B32 (a right click on a
  written item stops at the use gate; reading is a diagnostic button), B31 (a mouse drop on the hotkey slot is not routed). **Unverified, need
  one stage or read each:** B01, B07, B21, B23, B41; **B04 and B37 need an owner decision** (section 4).
- The brief named `bug-triage-2026-11-06.md`; no such file is in the repository. The B-list used here is the one of the
  [plan](open-bugs-migration-smoke-plan.md) and its [re-checks](open-bugs-resmoke-2026-11-04.md).

## 1. Method and its limits

- **Reports.** All 45 folders read as the player's text, the game-state table and the log; where a cause was open, the log's own lines
  decided (ids and outcomes only, no game text is quoted; the folders hold original content and are not committed).
- **Code.** Each claim about the source names the file and symbol; `origin/main` at `a56343bcf`.
- **Original executable.** One read for the new report (section 2), static, retail 1.0.7.4 `Arcanum.exe` through `llvm-objdump`
  (level F1, one reader); nothing from it is kept in git. Every other mechanic is cited from the existing ledgers.
- **Not done.** No viewer run and no reproduction from a report's save: this is a reading of code, logs and docs. A row says
  "present (cause seen)" only when a line of code or log shows it on current `main`.
- **Task sizes.** S: one PR of about 200 lines, one thread. M: one PR with a smoke stage, a long thread. L: more than one slice.

## 2. The new report: entangle visual stays after the caster dies (2026-11-08_13-30-55)

Player: the entangle spell's visual did not end when the NPC that cast it died; the hero under it could still run (after the NPC's
death; before that not tried). Build `728d9968`, Steamwoven rules, level 1 hero on the main map near the Crash Site.

**What the log shows.**

1. An NPC casts spell 51 (entangle, per the table of [GT145](../architecture/goal-table.md)) on the hero: `ai-cast ... spell=51 target=pc`.
2. At the release the spell run writes `ComponentNotReady` for the ObjFlag component and for the Effect component, then adds the
   eye-candy entries 308 and 309 on the hero; the message line says "ObjFlag effect not ready; Effect effect not ready". So the
   spell's **hold flag was never written** and Effect 62 never ran: only the pictures were.
3. The hero kills the NPC with two casts of spell 55 (about 4 s later). The log has no `EyeCandyRemoved` for 308 or 309 afterwards;
   the screenshot taken 14 s after the death still shows the vines on the hero.

**Cause (code).**

- Nothing ends a spell run when a critter that is its parent dies. The run system has the actions (`Demaintain`, `ObjectGone`,
  `ClearSources` in `GameSessionSpellRuns.cs`), but the only caller of `ObjectGone` is the Destroy component for a lying item
  (`SessionSpellCaster.cs`, `DestroyComponent`); no critter death or knock-out path calls any of them.
- The NPC AI gate's "end the maintained spells" step (DEMAINTAIN_SPELLS, `AiSchedulerRules`) ends in
  `AreaAiHost.EndMaintainedSpells`, whose body is empty (`AreaAiHost.cs`). So an NPC's maintained spell also never ends when the NPC
  leaves combat.
- The hold itself (ObjFlag, spell flag 0x1000000, and Effect 62) is the open slice MT13 ([magic plan](magic-implementation-plan.md),
  [spell cast host](../world/spell-cast-host.md)): the owner's decision of 2026-11-07 (an entangled hero cannot move in Steamwoven
  mode, an already-running hero keeps running in Original mode) is not implementable until that component exists.

**The original (read in this task, F1).** The kill routine at 0x45DA20 (the ledger's GT34 step 6) calls 0x459740 with the dying object (the call site
0x45DA96). That routine walks the 512-entry run table once for each of three tests: it ends (through the interrupt 0x4571E0) a
run that lists the object among its targets, ends a run whose parent is the object, and detaches (0x457530) a run whose source item
is the object; the other callers are three sites inside the destroy routine 0x43CCA0 and one at 0x4B5B38 (not identified). This agrees with the ledger's
reading of "death of an involved object" ([magic core evidence](magic-core-evidence.md): "the parent's runs end ... runs on the
target end") and with the caster-unconscious ending (0x45D5A0, demaintain 0x457450). The End of the entangle spell clears the flag
and removes its eye candy ([GT152](../architecture/goal-table.md)). So **in the original the entangle ends the moment its caster
dies**, and the hero is free; Steamwoven's missing ending is a gap, not a different rule.

**Split.** (a) The ending: critter death, knock-out and destruction end the runs they parent, and the AI gate's demaintain really ends
them (MT11 of the magic plan, FR-MAGIC-006): size M. (b) The hold: ObjFlag and Effect components, which is B11's remainder
(and B24's flag): size L, three slices (section 4, task T2).

## 3. The 44 older reports

State is on `main` `a56343bcf`. "Code" is the task that handled it; "Archive" is the decision of this triage.

| Report | Bug | State | Code | Archive |
| --- | --- | --- | --- | --- |
| 2026-10-29_17-16-19 | B17 and B18 | B17 fixed; **B18 open** (examine text, queued) | ARC-DEV-171 for B17 | no (B18) |
| 2026-10-29_17-19-14 | B17 | fixed (the reply condition `ru`) | ARC-DEV-171 | yes |
| 2026-10-29_17-44-57 | B11 | partly the original's own behaviour, the hold waits for MT13 | ARC-DEV-188 (read only) | no (queued) |
| 2026-10-29_17-46-09 | B38 | fixed in source by the thread that merged after the re-checks (hurt and spell sounds, bark on death) | ARC-DEV-296 | yes |
| 2026-10-29_18-02-58 | B36 | **present, cause seen** (section 4) | none | no |
| 2026-10-29_18-04-32 | B24 | present, waits for the ObjFlag component | none | no |
| 2026-10-29_21-07-06 | B02 | gone | ARC-DEV-085, ARC-DEV-155 | yes |
| 2026-10-29_21-07-37 | B06 and B19 | B06 fixed; B19: get reaction and get area added | ARC-DEV-170, ARC-DEV-178 | yes |
| 2026-10-29_21-08-25 | B02 | gone | ARC-DEV-085, ARC-DEV-155 | yes |
| 2026-10-29_21-09-10 | B01 | unverified, the case was never run (the same made-item key as B36) | none | no |
| 2026-10-29_21-10-07 | B01 | as above | none | no |
| 2026-10-29_21-13-22 | B05 | gone (stage `follower-far`) | ARC-DEV-170 | yes |
| 2026-10-29_21-15-08 | B45 | fixed (stage `indoor-fade`) | ARC-DEV-203 | yes |
| 2026-10-29_21-16-23 | B23 | unverified: the log shows a locked door at the click, not a container | ARC-DEV-157 in part | no |
| 2026-10-29_21-17-14 | B46 | gone (stage `fire-order`) | ARC-DEV-203 | yes |
| 2026-10-29_21-20-33 | B41 | **probably gone**: in the 2026-11-08 report "bar button Spells" is followed by casts on `main` (the pane's use is not logged) | none | no (one stage to confirm) |
| 2026-10-29_21-26-10 | B21 | unverified: script 1643 refuses at its line 2 | none | no |
| 2026-10-29_21-36-31 | B30 | fixed (doll and bag toggles; the original has no sort buttons) | ARC-UI-072 | yes |
| 2026-10-29_21-43-18 | B12 | present, one half open, queued | ARC-DEV-250 (stage only) | no |
| 2026-10-29_21-44-16 | B37 | not reproduced: the data gives the box 200 hit points; the resmoke row stays open until a run with a log shows what was seen: **owner decision** | ARC-DEV-161 (read) | no |
| 2026-10-29_21-46-22 | B12 | as 21-43-18 | ARC-DEV-250 (stage only) | no |
| 2026-10-29_21-47-12 | B04 | unverified, probably the original's rule: **owner decision** | ARC-DEV-086 read | no |
| 2026-10-29_21-48-14 | B20 | present, thread running | coordinator | no (until its code is given) |
| 2026-10-29_21-48-45 | B31 | **present, cause seen** (section 4) | none | no |
| 2026-10-29_21-49-38 | B14 | fixed with a real grenade (stage `grenade`) | ARC-DEV-259, ARC-DEV-140 | yes |
| 2026-10-29_21-50-47 | B32 | **present, cause seen** | none | no |
| 2026-10-31_12-40-53 | B08 | fixed (the notice of an aggressive spell; stage `ai-react`) | ARC-DEV-281, ARC-DEV-264 | yes |
| 2026-10-31_12-42-01 | B33 | fixed (right button opens the order menu; stage `companion-click`) | ARC-DEV-280, ARC-DEV-263 | yes |
| 2026-10-31_12-42-50 | B39 | fixed (a spell or trap kill pays the kill award) | ARC-DEV-295 | yes |
| 2026-10-31_12-43-44 | B40 | fixed (the chest's inventory source read whole) | ARC-DEV-161 | yes |
| 2026-10-31_12-45-24 | B03 | fixed (a dead NPC's worn items looted; stage `loot-worn`) | ARC-DEV-202 | yes |
| 2026-10-31_12-47-56 | B38 | as 17-46-09 | ARC-DEV-296 | yes |
| 2026-10-31_12-48-49 | B07 | unverified (placement of animals) | none | no |
| 2026-10-31_12-49-43 | B13 | gone (dying is a run; stage `critter-fall`) | ARC-DEV-137, ARC-DEV-248 | yes |
| 2026-10-31_12-51-38 | B07 | as 12-48-49 | none | no |
| 2026-10-31_12-53-43 | B34 | fixed (the original's save messages) | ARC-DEV-168 | yes |
| 2026-10-31_12-54-07 | B35 | fixed (the original's load messages) | ARC-DEV-168 | yes |
| 2026-10-31_12-55-03 | B43 | gone (route to a barrel; stage `route-barrel`) | ARC-WORLD-121, ARC-DEV-247 | yes |
| 2026-10-31_12-55-56 | B43 | as above | ARC-WORLD-121, ARC-DEV-247 | yes |
| 2026-10-31_12-59-21 | B43 | as above | ARC-WORLD-121, ARC-DEV-247 | yes |
| 2026-10-31_13-00-50 | B43 | as above | ARC-WORLD-121, ARC-DEV-247 | yes |
| 2026-10-31_13-01-22 | B44 | the original's behaviour, kept (owner, 2026-11-07) | ARC-DEV-203 | yes ("not a bug") |
| 2026-10-31_13-02-37 | B43 | as above | ARC-WORLD-121, ARC-DEV-247 | yes |
| 2026-10-31_13-03-23 | B16 | fixed (2 s fades, the load indicator) | ARC-DEV-204 | yes |
| 2026-11-08_13-30-55 | new (entangle) | section 2 | none | no |

## 4. What is open, grouped into tasks

Codes are the coordinator's to give; the names below are working names. "Bug" ids are those of the plan.

| Task | Bugs | Cause | Proposed fix | Size |
| --- | --- | --- | --- | --- |
| **T1: spell runs end with their parent** | new report (visual stays) | Section 2: no critter death, knock-out or destruction ends a run; `AreaAiHost.EndMaintainedSpells` is empty | MT11 slice: the kill, knock-out and destroy of a critter call the run system's object-gone and demaintain actions as 0x459740, 0x4598D0, 0x459A20 and 0x45D5A0 do; implement `EndMaintainedSpells` through the session; a stage `spell-end` (an NPC casts a maintained spell on the hero, the hero kills it, the eye candy is gone) | M |
| **T2: spell hold components** (B11's task) | B11 remainder, the hold half of the new report, B24 | ObjFlag and Effect components are logged "not ready" (MT13); no flag is ever written. B24: a cast of spell 56 on a corpse, in the 2026-11-08 log, ends with the same "ObjFlag effect not ready" | T2a: ObjFlag with the reference count and the interrupt of goals 3 and 61 ([GT146](../architecture/goal-table.md), GT147), the entangled planners' refusal (GT149), the owner's Steamwoven/Original split for the entangled hero; T2b: Effect 62 and the re-resist on each hit (RS10); T2c: B24, the spirit-of-the-dead spell (spell 56; the flag it sets is presumably `OSF_SPOKEN_WITH_DEAD`, to be read first) and the talk with a dead speaker; stage `hold-hero` (SM09) | L (3 slices) |
| **T3: made items have facts** | B36, B01 | `AreaInventoryHost.Wearable` and `UseFacts` return null for a made item (their summaries say "its prototype's facts are not read here: NOT READY"); the logs of B36 and B32 show a wear drop of `made:21` ending with no disposition, which is what the "no wear values here" branch prints (consistent, not proved); B01 names the same key | Read a made item's facts from its prototype in `Wearable`, `UseFacts` and the other host reads (`GridMoveItem` already handles a made item); the stage `inv-carry` (SM02) is the regression for B01 | M |
| **T4: inventory interface, item steps** | B31, B32, B41 | B31: the log has `drop kind=PutBack` and no `hotkey-drop` line of either prefix (ARC-UI-022, ARC-UI-027), so a mouse release on the bar slot does not reach `DropHeldItemOnEntry` (the key and the slot click do); B32: the right-click use of an item (likely the note) ends at the use gate (`gate=NotUsable` in the log), and reading is only the diagnostic "Read:" button of the window; B41: not reproduced (probably gone) | Route a mouse drop on the hotkey slot to the held item; read the original's use of a written item (0x462CC0) and open the reading window from it; the item steps of the stage `inv-ui` (SM23: wear boots, drop on the bar, use a note, open the spell pane) close B41 or show it | S to M |
| **T5: placed animals census** | B07 | No cause. The log shows only damage lines for the animals; two reports differ by where the boars stood | SM07 `placement-census` (read-only): map rows of animals within ten sectors of the Crash Site against the host's NPCs after the map opens and after a quick load; a fix only if a row is missing or moved | S, then maybe M. **Done by ARC-DEV-308 (2026-11-08): no defect found, see [the B07 re-check](open-bugs-resmoke-2026-11-04.md#re-check-of-b07-by-the-stage-placement-census-arc-dev-308-sm07-2026-11-08)** |
| **T6: elven town entrance** | B21 | Script 1643 ends `ReturnSkipDefault` at line 2 for the click; the story condition is not read | Read script 1643 and the guard dialog's state in the installed data (as ARC-DEV-171 did for 1696); likely the original's rule | S (research) |
| **T7: trash cans** | B23 | ARC-DEV-157 admitted the tile script's get-stat; the click itself is not identified (the log shows `portal use ... Locked` of a door at the clicks, no container) | Add it to the thread that runs B20 and B25 (tile scripts), or run it as a stage on `--sector 1409,1314` once that thread merges | S |
| Owner decision | B04, B37 | ARC-DEV-086 and ARC-DEV-155 read the report's log: Take all leaves the pile, closing the window dissolves it (the original's JP4, JP5); the report may have looked at the pile while the window was still open. B37: ARC-DEV-161 found 200 hit points in the data and no log of what the reporter saw | B04: call it "not a bug" and archive (recommended), or ask the reporter whether the pile stayed after the window closed. B37: archive as "not reproduced" (recommended), reopen if it is seen again | none |

Not new tasks, listed so the table covers every open bug: B10 (save of runs, in progress), B11 (T2 above), B12 and B18 (queued),
B20 and B25 (thread running); B38 and B42 merged as ARC-DEV-296 after the re-checks (stage evidence not seen by this triage).

## 5. Archive record

Moved on the owner's Mac, whole folder unchanged, into `artifacts/packages/macos/reports/archive/` (the folders are original content
and never enter git; the INDEX lines follow [the rule](../player/alpha-build.md#9-reporting-a-bug-ctrlb), ARC-DEV-056). The 26
reports with "yes" in section 3. The report of B20 (2026-10-29_21-48-14) is moved when that running thread's code is given.
