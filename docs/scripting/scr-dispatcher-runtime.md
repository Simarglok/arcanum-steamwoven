# SCR dispatcher core

## Status and boundary

Work item ARC-SCRIPT-008, slice **S2** of the
[SCR event dispatcher plan](../research/scr-event-dispatcher.md#10-proposed-implementation-slices).
`Arcanum.Scripting.Scr.ScrDispatcher` decides, runs and reports one script event:
an object's slot for an attachment point, a tile script, or a sector script. It
runs only programs that the [static admission](scr-program-admission.md) admits
for `ScrAdmissionSet.S2`. It reads the game through a host port and changes
nothing itself: the result carries every change, for the caller to apply as one
step. Godot-free, net8.0, synchronous. No session binding, no engine event fires
it yet (see [Not ready](#not-ready)).

Sources: rows E1–E13 of the dispatcher research (a reading of the original's
functions, F0 unless the ledger lists them), the
[verification ledger](../research/original-verification-ledger.md) for the
original 1.0.7.4 executable, and the rule modes of
[Steamwoven and Original](../world/rule-modes.md). Original-executable parity
beyond the rows named here is **NOT_VERIFIED**.

## API

| Call | Event | Writeback |
| --- | --- | --- |
| `RunObject(ScrObjectEvent, IScrHost, ScrRunOptions?)` | point 0..35 on an attachee (triggerer and extra optional), start line ≥ 0 | the attachee's slot for the point when its number, local flags or counters changed; a trap object whose script became 0 is destroyed instead |
| `RunTile(ScrTileEvent, IScrHost, ScrRunOptions?)` | the tile's slot, the entering critter; point use, line 0, no attachee | the tile's slot, unless `TileScriptChangesDiscarded` (Original, X3 tile) |
| `RunSector(ScrSectorEvent, IScrHost, ScrRunOptions?)` | the sector's slot, the player; point use, line 0, no attachee | the sector's slot in both modes (the original, ledger X3 sector) |

`IScrHost` answers reads only: the attachee's slot (`ScrSlot`: number, local
flags, counters), a script by number (`ScrScriptLookup`: found program, missing,
unreadable), global flag 0..3199, the story state, and the object questions of
the gates below. A host answers from one consistent state for the whole call.

`ScrRunOptions`: `Rules` (`ScrRuleProfile`, from `GameRules.Scripts`, read at
each call), `Admission` (default `S2`; a set the interpreter cannot run is an
`ArgumentException`) and `MaxExecutedLines` (1..1,000, default the original's
1,000). Invalid options throw; an event never does.

`ScrRunResult`: `Outcome` (`RunDefault`, `SkipDefault`, or `Refused` when the
script is outside the admitted set and nothing ran), `Stop` (why it ended),
`Script` (the number that ran, 2188 for a mind-controlled NPC), `StopLine`,
`Refusal`, `Trace` (one `ScrTraceStep` per executed line: line, condition, held,
action, next line or null) and `Effects`.

`ScrEffects`: the slot `Writeback` (source, attachee or null, point, new slot)
or `DestroyTrapObject`, the changed `GlobalFlags` (final values, ascending, only
those that differ from the host's), the new `StoryState`, the conversation to start (`Dialog`), the deferred `Calls`
the printed `Lines` (S6), the `Sounds` (S7) and the `Teleports` (S4). A run reads its
own writes back from an overlay. Effects made before a stop are kept, as the
original applies them as it goes; a refused script has none.

## Decision order of an object event

Each step returns at once when it applies (the original's object script
dispatch 0x441980 and script executor 0x4449B0):

1. Point outside 0..35, the dialog-override slot (31, it names a DLG), a
   negative line or a default (empty) object reference: `SkipDefault`,
   `InvalidEvent`.
2. No slot, or script 0: `RunDefault`, `NoScript` (E2).
3. A use event with a triggerer: the host's trap spot; `Spotted` gives
   `SkipDefault`, `TrapSpotted` (E2).
4. Missing or unreadable script: `SkipDefault` (E4).
5. A trap script, number 30000..30007 with a trap file flag (0x1 or 0x2), as
   the original's trap script test (0x4BCB70) checks: `SkipDefault`,
   `TrapScript`. The trap domain runs it and must reproduce what the original does
   afterwards: the seven installed trap scripts carry the auto-removing flag, so
   the slot clears and a trap object is destroyed through E3 (E4). A trap flag on
   any other number is an ordinary script.
6. A monster generator attachee: `RunDefault` (E5).
7. Dialog point only (E6). The host reports any combination of dead,
   surrendered and mind-controlled, and the gates test them in the original's
   order (0x444A95–0x444B17): dead without the death-speech flag, then
   surrendered without the surrender-speech flag: `RunDefault`; then
   mind-controlled: script 2188 runs on the NPC's slot instead, its own number is
   kept afterwards, and a missing 2188 gives `RunDefault` (X5).
8. Admission for the set; a refusal is `Refused` with the `ScrRefusal`.
9. The run, then the writeback (E3).

## The run

A run starts at the event's line with ten local variables at 0 and a private
copy of the slot's local flags and counters (E7, E11). Each line evaluates its
condition (the always-true condition runs only its true action) and runs one
action. Read in the original at 0x444B8E–0x444C4D:

| End | Stop | Default | Auto-removal |
| --- | --- | --- | --- |
| return and skip default (1) | `ReturnSkipDefault` | skip | yes |
| return and run default (2) | `ReturnRunDefault` | run | yes |
| the last line continues | `FellOffEnd` | skip | yes |
| a line outside the script: a goto to exactly the line count, a start line outside it, an empty script | `LineOutOfRange` | skip | no |
| 1,000 executed lines (or the option's lower budget) | `Budget` | skip | no |
| a computed global flag number outside 0..3199 | `IndexOutOfRange` | skip | no |

Auto-removal (file flag 0x4 of the attachment's own script) sets the number to 0.
Remove this script (5) and change this script (6) take effect after the run and
win over auto-removal; a computed change to −1 is the original's "no change",
and any other computed value is stored as the original does (E10). Admission
refuses a literal change outside 0..99999, −1 included (project strictness, no
installed script has one). A computed local flag number uses its low five bits,
as the original's `shl` does (0x4473C2, 0x4473F5): 32 is flag 0 and −1 flag 31.
The `IndexOutOfRange` stop is a fix in both modes: the original would read or
write outside its global flag store.

Values: a counter reads and writes its byte; a local variable its slot; a number
is itself; a global flag reads 0 or 1. Add, subtract and multiply wrap as 32-bit
integers. Get story state stores the current value; set story state only ever
raises it (original 0x4450A0).

## Rule-mode differences and fixes

| ID | Original 1.0.7.4 | Steamwoven | Original setting |
| --- | --- | --- | --- |
| X7 divide | signed quotient, nothing for divisor 0 (ledger X7) | as the original | as the original |
| X7 edge | `int.MinValue / −1` faults the original's `idiv` | stores `int.MinValue` (both fixed: crash) | stores `int.MinValue` |
| X8 counter write | masked to the counter's byte (ledger X8) | as the original | as the original |
| X10 global-flag write | clears the flag, ORs the whole value shifted to its bit into the 32-flag word (2 sets the next flag, a negative value every higher one) | the value's low bit into the flag only (D2 fix) | `GlobalFlagWriteSpills` |
| X3 tile writeback | the unmodified tile record is stored back | changes persist, as for objects and sectors (D2 fix) | `TileScriptChangesDiscarded` |
| X3 sector writeback | number, flags and counters persist (ledger X3 sector) | as the original | as the original |
| X6 negative goto | −1..−4 are control codes | refused by admission (project strictness; no installed script uses one) | refused |
| global flag number outside 0..3199 | reads or writes outside the flag store | `IndexOutOfRange` stop (both fixed: out of bounds) | the same stop |

Set and clear global flag (100, 101) write 1 and 0 and never spill. The D2 fixes
above are recorded by this slice; `ScrRuleProfile` is part of
`GameRules` (`Scripts`).

## Dialogue (ARC-SCRIPT-009, slice S3)

- **Region admission.** The dispatcher admits, for each event, only the lines a
  run from the event's line can reach (`ScrAdmission.AdmitFrom`): it follows a
  literal goto, the next line, and ends a path at a return or a dialog action;
  both actions of a conditional line are followed, the dead false action of an
  always-true line is not. A run of an admitted region can only reach lines
  inside it, so it still never stops half-way on an unsupported command, and
  lines outside it are never read. The whole-script `Admit` stays for the census
  and tools. This lets Virgil talk: his entry lines 0..4 and the dialog lines
  they reach are inside the set, while later lines (float line, calls, loops) are
  not reached by the talk.
- **`ScrAdmissionSet.S3`** (the default of `ScrRunOptions`) is S2 plus condition
  18 (the first object's leader is the second object) and 39 (a jilted NPC),
  action 4 (dialog) and the single-object focus kinds triggerer, attachee,
  extra object and player. The host answers `Player`, `Leader`, `IsJilted` and
  `IsPc`. An object operand naming a role the event does not have (no
  triggerer, attachee, extra or player) stops the run with `MissingObject`
  (skip default); the original would pass a null handle (both fixed).
- **Dialog action (4).** It ends the run with `Stop = Dialog` and skip default;
  auto-removal applies as for a return. When the triggerer is a PC, `Effects.Dialog`
  (`ScrDialogStart`: PC, NPC, script, line, node) asks the caller to start the
  DLG of the run's script (2188 for a mind-controlled NPC) at the node after it
  applied the writeback: the original stores the slot before its start callback
  (X1 in the original: flags to flags).
- **Resumption.** A DLG end of 0 resumes the dialog script at the action's line
  + 1, −N at line N: the caller dispatches a dialog event with that line, and the
  region is admitted from it.

The AREA viewer's conversation host (`AreaConversationHost`) now runs the talk
request and the resumption through the dispatcher with `AreaScriptHost`
(`tools/terrain-viewer/Core`): the speaker's dialog slot is its effective area
attachment overridden by the session's entity map changes `dialog.lf`,
`dialog.counters` and `dialog.script` (no schema change; a value equal to the
loaded one clears its key); global flags are the session's; the PC leads a
following speaker. It runs `S3` (until ARC-SCRIPT-019 without the story-state actions, which
now read and raise the session's story-state number). Run default is the original's generic greeting (shown as a
status, not modelled); a skip default without a dialog action ends the talk; a
refusal is `EntryNotAdmitted` with the line, code and type. The session applies
global flags and the slot as separate steps (it has no transaction across its
flag owner and its map changes); a refused step stops the later ones and is
reported as `ScriptEffectsRejected`. Since ARC-SCRIPT-011 the scripts and the
DLG come from the game's content stack ([script content](script-content.md)):
the original's name for each script number, the winning copy, the DLG by the
script's name; `SourceChanged` is a file that changed after its first read, and
`ContentUnavailable` a content that could not be opened. A resumption that finds
no script in the slot (it removed itself) is `ScriptRemoved`. The DLG node must
be an NPC record.
In the Godot viewer only the player's close of a finished conversation resumes
the script; a reset, reload or map change closes the panel without resuming.

## Deferred calls (ARC-SCRIPT-010, slice S5)

- **`ScrAdmissionSet.S5`** (the default of `ScrRunOptions`) is S3 plus call
  script in (54) and remove script call (125). Call script at (55) stays
  outside: one installed line uses it and no run reaches it (ARC-RES-045), and
  its time-of-day arithmetic counted from the module's start hour (TE2) is not
  settled.
- **Call script in** asks for a call (`ScrEffects.Calls`, in action order):
  script, line, triggerer and attachee (an absent role is stored as absent, as
  the original stores a null handle) and a game delay of the seconds × 8000 ms
  (real seconds at the 8× game rate). The original makes a 32-bit millisecond
  count of `seconds × 1000`, raises 0 to 1 and multiplies by 8 (call-in case
  0x44867B, millisecond helper 0x45A950), so 0 seconds is 8 ms. TE1, an original bug at the
  edges: that count wraps above 536,870 s and a negative count gives weeks.
  Steamwoven computes without wrapping and reads a negative count as 0 (8 ms);
  the Original setting wraps (`ScrRuleProfile.CallDelayWraps`). Not modelled:
  the later wrap of the unsigned millisecond sum when the queue adds the delay
  to the current time of day (from about 526,070 s, depending on the hour).
- **Remove script call** asks to remove the first pending call of a script whose
  attachee is the named object.
- **`ScrDispatcher.RunCall(ScrCallEvent)`** runs a call that came due: the named
  script from its file header (initial local flags and counters), at the stored
  line, as a use event with the stored roles and no extra object; a missing or
  trap script and a monster generator attachee end it as for an object event; a
  role an operand reads but the call does not have stops it with
  `MissingObject`. A deferred call has no attachment, so nothing is written
  back, with the original's one exception: a dialog action (PC triggerer)
  stores the run's local flags and counters into the attachee's **use** slot
  (the run's point) before the conversation starts, when they differ, keeping
  the slot's number (0x447282). In the installed data most call-in targets an
  S5 run reaches are such delayed conversations. The conversation's "return to
  script" end resumes the NPC's **dialog** slot (the original's DLG end runs
  `SAP_DIALOG` of the NPC), which may be another script than the call's;
  `ScrDialogStart` says so.

The session keeps the calls on its event queue as the kind `scr.call`
([event queue](../world/game-event-queue-runtime.md#session-wiring-arc-world-024)):
the caller applies `ScrEffects.Calls` with `ScheduleScriptCall` and
`RemoveScriptCall`, and an event pass hands each due call to an
`ISessionScriptWorld` host, which runs `RunCall` and applies the effects. Since
ARC-WORLD-039 the AREA viewer pumps the queue ([host pump](../world/game-event-queue-runtime.md#host-pump-arc-world-039)):
its set admits call script in and remove script call ("S3-area-sound-calls", "S3-area-sound-calls-scheme" since ARC-SCRIPT-016),
`AreaScriptHost.Apply` hands the requests to the session in action order, and a due
call runs through `AreaConversationHost.RunCall` (a call that would start a
conversation is not run).

## Print line (ARC-SCRIPT-013, slice S6)

- **`ScrAdmissionSet.S6`** is S5 plus print line (action 24); `ScrRunOptions` accepts any subset of S6 and keeps S5 as
  its default, so existing hosts are unchanged.
- **Print line** returns `ScrEffects.Lines`, one `ScrPrintLine(Script, Line, Kind)` per action in order: the DLG line
  (operand 0) of the running attachment's script (the number the run executes, as the dialog action names it: 2188 while
  a mind-controlled NPC's attachment runs it, E6; the called script for a deferred call) and the second number
  (operand 1), both read as values. A run without an attachee
  (tile, sector, a call without one) prints nothing, as the original (ledger IS2).
- **`ScrPrintedLine.Read(document, line, alternate, names)`** builds the shown text from a loaded DLG as the original's
  NPC-line step does: a missing record shows one space; the text or (`alternate`, a critter triggerer that is not male)
  the alternate text, Windows-1252 with `@pcname@`/`@npcname@` substituted. A record with an action field, generated
  text (a `g:`, `m:` or `r:` prefix, or exactly `i:`, tested on the record text whichever field is shown), another token
  or an undecodable text is `Unsupported`: the
  original would run the action or generate the text, which is not modelled, so a caller refuses the run's effects. It
  carries the registry mark of 0x44B390 (partial). The session uses both for an item's insert-item script
  ([inventory runtime](../world/inventory-runtime.md#insert-item-script-arc-script-013)).

## Sound actions (ARC-SCRIPT-040, set S7)

- **`ScrAdmissionSet.S7`** is S6 plus play sound (82), play sound on (83) and play sound scheme (87); `ScrRunOptions`
  accepts any subset of S7. They return `ScrEffects.Sounds`, one `ScrSound(Kind, Id, Ambient, Object)` per action in
  order (checked in the 1.0.7.4 executable, [audio study](../research/audio-evidence.md) SN28):
  - **Play sound**: `Effect` with the id (a value operand): once, not positional (the original's plain play call,
    volume 127, centre).
  - **Play sound on**: `OnObject` with the id and the object: once, positional on the object's tile. The original passes
    an absent object's null handle to the sound call (undefined); here nothing plays and the run goes on.
  - **Play sound scheme**: `Scheme` with the music and ambient ids, the session's scheme request unchanged
    (`PlaySoundScheme`, [session sound](../world/session-sound-runtime.md)).
- **The AREA viewer** admits the three in its dialog set (`AreaScriptHost.Admission`, "S3-area-sound-calls", since ARC-SCRIPT-016 "S3-area-sound-calls-scheme"): `Apply` hands a
  scheme pair to the session (one the session drops, while sound is locked, or refuses, a negative id, changes nothing and
  stops nothing) and keeps the effect sounds for the viewer, which plays them on the next frame
  (`TerrainViewerScriptSounds`: play sound through the effects channel at the plain volume, play sound on through the
  positional rules on the PC's tile or where the speaker stands now, a companion's live cell,
  [positional sound](../audio/positional-sound.md)). Like the flags, the scheme request and the queued sounds stay
  applied when a later step of `Apply` is refused (the original applies as it goes).
- The teleport and fade sound operands go to the host with the teleport request (set S4 below).
- Set numbers no longer follow the slice plan of the [event dispatcher research](../research/scr-event-dispatcher.md):
  S6 is print line and S7 the sound actions, not heartbeat and combat.

## Skill commands (ARC-SKILLS-009, set S11)

- **`ScrAdmissionSet.S11`** is S10 plus get skill (47) and use item with skill (79); `ScrRunOptions` accepts any subset of S11 (now
  the interpreter's widest set; the default stays S5). A set with 47 needs a host that implements `IScrSkillQueries`, checked before
  any event runs. Read in the retail 1.0.7.4 executable by two independent readers: ledger
  [SX1–SX6](../research/original-verification-ledger.md#script-skill-commands-arc-skills-009).
- **Get skill (47)**: the skill number (a value), the object, the store (operand 2, the only store outside operand 0; admission refuses
  a number literal there). The run stores `IScrSkillQueries.SkillLevel(object, skill)`: the effective level the original's level
  functions give (basic 0..11, tech 12..15 as tech index skill − 12), never the points or the training. A number outside 0..15 stores 0
  without asking the host; the host answers 0 for an object that is not a PC or an NPC (SX2). An absent object stops the run
  (`MissingObject`) as every object read does: the original reads its null handle through the object pool, whose result is undefined.
  The run goes on to the next line (SK1: no fall-through).
- **Use item with skill (79)**: source, item, target (objects), skill and modifier (values), as read. The run returns one
  `ScrSkillUse(Source, Item, Target, Skill, Modifier)` per action in `ScrEffects.SkillUses` and goes on; an absent target or source
  asks for nothing (SX3). The item may be absent. What the original does next is the host's and is not built: the source's
  use-item-with-skill goal (gate cost 4, no action points charged, no flags), which runs the shared skill check once with the modifier
  as the invocation modifier (counted twice, SC12 and the A2 switch) and the skill's handler (SX4).
- **The AREA viewer** admits get skill in its dialog set while the session has a hero (`AreaScriptHost.SkillAdmission`,
  "S3-area-sound-calls-scheme-schematic-reading-skill", through `RunAdmission`; without a hero a script that reads a skill is refused,
  not run on a made-up level). The PC's level is the session hero's. An NPC of the host's map whose map data the viewer reads (the
  combat host's fields, with `--hero`) takes the sheet the session saved for it (a levelled follower) or else that map data, with the map
  data's effects (`GameSessionOwner.QueryNpcSkills(SessionNpcAdvancement, AdvancementModifiers)`); any other object is 0, including an
  NPC whose map data is not read (even with a saved sheet) and an object naming another map, as a deferred call's role can. Use item with
  skill is not admitted there: the [census](../research/scr-census.md) counts its 2 installed lines, none live, and `Apply` refuses a
  request.
- Installed scripts (opt-in census test): 17 use action 47, 2 action 79 (none live by the ARC-RES-045 census), 1 action 115; S11 admits 683 scripts whole, 2 more
  than S10. Action 115 (try to steal 100 coins, SX5) belongs to pick pocket (task S15) and is not admitted.

## Reading actions (ARC-UI-049, set S10)

- **`ScrAdmissionSet.S10`** is S9 plus show book (64), show picture (65) and show plaque (114); `ScrRunOptions` accepts any subset
  of S10 (the interpreter's widest set until ARC-SKILLS-009 widened it to S11; the default stays S5).
- **The actions** (ledger [WR15](../research/original-verification-ledger.md#reading-window-arc-ui-049)): a value operand (the
  book's or plaque's first text line, the picture's art) and an object operand (the viewer). The run returns one
  `ScrReadingRequest(Kind, Number, Viewer)` per action in `ScrEffects.Readings`, in action order, and goes on to the next line; an
  absent viewer shows nothing. The
  original opens the reading window only for a player viewer, with no dead check; the host decides.
- **The AREA viewer** admits them in its dialog set (`AreaScriptHost.Admission`, "S3-area-sound-calls-scheme-schematic-reading") and
  in its use, tile and sector set (`AreaUseScriptHost.Admission`, "S4-area-use-reading-objects"); both keep the requests whose viewer is the
  PC and the viewer opens the [reading window](../world/reading-window-runtime.md#script-openers-scripting-wr15).
- Installed scripts (opt-in census): 10 use action 64, 3 action 65, 13 action 114; S10 admits 681 scripts whole, 24 more than S9.
  Action 94 (set a written item's first text line, 2 scripts) is not admitted.

## Start schematic UI (ARC-TECHNOLOGY-005, set S9)

- **`ScrAdmissionSet.S9`** is S8 plus start schematic UI (129); `ScrRunOptions` accepted any subset of S9 until ARC-UI-049 widened
  it to S10 (the default stays S5).
- **The action** (ledger [SW8](../research/original-verification-ledger.md#schematic-window-arc-technology-005)): its object
  operand names one object; the original opens the window with (PC, PC) for a PC, (NPC, its PC leader) for an NPC that has one,
  and nothing otherwise, but calls it through a callback slot the game never fills (TC2). The run returns one
  `ScrSchematicRequest(Object)` in `ScrEffects.Schematics` and goes on to the next line; an absent object names none. The host
  resolves it (TC2, both fixed).
- **The AREA viewer** admits 129 in its dialog set (`AreaScriptHost.Admission`, "S3-area-sound-calls-scheme-schematic", since ARC-UI-049 "…-reading"); `Apply`
  resolves each request (`AreaScriptHost.ResolveSchematic`): the PC opens the hero's window once nothing else owns input; an NPC
  with a PC leader (the original walks its leader chain to the first PC; the viewer's leader is the direct follow, the same while no
  NPC leads another) opens it with the NPC as crafter (ARC-TECHNOLOGY-006, ledger NC3); any other
  object opens nothing. It changes no session state ([schematic window](../world/schematic-window-runtime.md#script-action-129-sw8-tc2)).
- No installed script uses action 129.

## Set auto-level scheme (ARC-SCRIPT-016, set S8)

- **`ScrAdmissionSet.S8`** is S4 plus set auto-level scheme (139); `ScrRunOptions` accepts any subset of S9 since
  ARC-TECHNOLOGY-005 (the default stays S5). Its focus kinds stay the four single objects, so focus groups are not admitted.
- **The action** (ledger [OL30](../research/original-verification-ledger.md#companion-orders-and-loyalty-arc-res-064)): its object
  operand is a list in the original (one object or a group, 0x44B4E0), and each object gets the number (a value operand, no range
  check) through the scheme setter (0x4A6D90), which writes a PC's or an NPC's scheme and ignores any other object. The run returns
  one `ScrSchemeRequest(Object, Scheme)` per object in `ScrEffects.Schemes`, in action order, and goes on to the next line; an
  absent object names none (the original's list then holds a null handle, which the setter ignores). The host decides what the
  object is. Nothing is bought at once: the scheme runs at the object's next level step
  ([advancement](../world/advancement-runtime.md), AL12).
- **Session**: `SetAutoLevelScheme(expected, SessionSchemeTarget.Hero | ForNpc(loaded), scheme)` stores it (registry 0x4A6D90,
  partial, F2). The session keeps schemes within 0..199 (its snapshot bound), so a number outside is refused where the original
  stores it and later buys nothing with it: a documented deviation no installed script reaches.
- **The AREA viewer** admits 139 in its dialog set (`AreaScriptHost.Admission`, since ARC-TECHNOLOGY-005 "S3-area-sound-calls-scheme-schematic", since ARC-UI-049 "…-reading"); `Apply` hands each
  request to the session in action order: the PC's own scheme, or an NPC's record with its map data (the host's `npcAdvancement`,
  which the viewer reads from the NPC's home-map fields with `--hero`); any other object is ignored, and a refused number or a PC
  without a hero changes nothing and stops nothing, as the original's run goes on; a change the session cannot take (the record
  budget, the revision) stops the later steps, as a refused call request does (`AreaScriptHost.SchemeRefusalStops`).
- **Installed original** (opt-in `ScrSchemeTests`, `DAT_ROOT`): one set-scheme action in all scripts, line 5 of the winning script
  2023 (on the triggerer, 101); S8 admits it from that line, and the next line (action 99) is the first refusal, so the script
  itself does not run yet.

## Teleports and guards (ARC-SCRIPT-014, set S4)

The dispatcher part of plan slice S4: what a use, tile or sector script asks for when it moves an object to a map, and the
conditions that guard the conditional map transitions of the [transition research](../research/world-transitions.md#conditions).
The viewer's use wiring is ARC-SCRIPT-015 ([guarded use transitions](../rendering/area-map-mode.md#guarded-use-transitions-arc-script-015):
map mode runs the use script of a guarded use site under "S4-area-use" (since ARC-UI-049 "S4-area-use-reading", plus the reading
actions), S4 without story state and the dialog action, and
the player's teleport becomes the transfer); tile and sector events since ARC-SCRIPT-017
([tile and sector scripts](../rendering/area-map-mode.md#tile-and-sector-scripts-arc-script-017)).

- **`ScrAdmissionSet.S4`** is S7 plus teleport (44) and fade-and-teleport (102), and the guard conditions PC quest state (6),
  holds an item named (17), is named (20), wields an item named (21) and has a reputation (33). `ScrRunOptions` accepts any
  subset of S4 (the default set stays S5). Focus stays one object (triggerer, attachee, extra object, player), so the
  guards over focus groups are not admitted yet.
- **Teleports.** `ScrEffects.Teleports` holds one `ScrTeleport(Object, CatalogueMap, X, Y, Fade)` per action, in order, and
  the run goes on to the next line (ledger [TR1, TR2](../research/original-verification-ledger.md#map-transitions-arc-data-025)):
  the object from operand 0 (fade-and-teleport: 3), then the map as the script gives it, x and y as values;
  `RuntimeMap` is the map less 4,999 in 32 bits, as the original computes it. A fade-and-teleport also carries its seconds,
  sound and movie (`ScrTeleportFade`); the original skips it only in a network game. An absent object asks for nothing
  (the original would queue a teleport of no object; what its processing does with one is NOT_VERIFIED). The host owns the
  pending-teleport rule: the original keeps one pending teleport, a new request replaces it unless the pending one is the
  local PC's, which refuses the rest (TR4).
- **Guards** (ledger [SG1–SG6](../research/original-verification-ledger.md#guard-conditions-arc-script-014)). A host whose set
  holds them implements `IScrObjectQueries`: the quest state of the object, whether it holds an item with that name number,
  its name number, whether it is mind-controlled, the name number of the item it wields in each location 1000..1008, and
  whether it has the reputation. The interpreter asks only for the object the condition names: quest state equals the
  value (6); holds the item (17); is named, unless mind-controlled (20); wields the item in any of the nine locations (21);
  has the reputation (33). A set with a guard and a host without the queries is a caller error: the dispatcher throws before
  any event runs, never a script refusal. As for the other single-object conditions, an absent object stops the run (`MissingObject`) where the
  original takes the false branch over zero objects.
- **Registry**: `ScrGuards.Holds` marks 0x4451B0 and `ScrGuards.Teleport` 0x447220, both partial (only these conditions
  and actions; the earlier slices' conditions and actions are not migrated to the registry yet).
- **Installed original** (opt-in `ScrTeleportTests`, `DAT_ROOT`): from line 0 set S7 admits 469 scripts and S4 774. Of the
  357 scripts with a teleport action, S7 admits 22 (none of their teleports is reached from line 0) and S4 294; with a host
  with no quests, items, names, wielded items, reputations or flags, those runs ask for 257 teleports.

## Object variables and calls (ARC-SCRIPT-018, set S12)

`ScrAdmissionSet.S12` is S11 (the skill commands, ARC-SKILLS-009) plus call script (7), assign object (15), get object
type (34) and the run's ten object variables as a focus kind (11), as the original runs them (ledger [OV1–OV4](../research/original-verification-ledger.md#object-variables-and-calls-arc-script-018)).

- **Object variables** start empty in every run. Assign object writes the object operand 1 names (none for an absent one)
  into the triggerer, the attachee, the extra object or an object variable; any other focus kind is not written. The run's
  roles are its own copy, so a later action sees the new triggerer.
- **Get object type** writes the object's type into the number store of operand 1 through
  `IScrObjectQueries.ObjectType` (15 for a PC, 16 for an NPC, the type field of any other object); a set with it needs a
  host with the queries, checked before any event runs. An absent object, or one the host does not know, stops the run
  (`MissingObject`); a literal store is refused at admission.
- **Call script** runs the script of operand 0 at once from the line of operand 1, with operands 2 and 3 as triggerer and
  attachee, no extra object, the callee's own header flags and counters and fresh variables. It is admitted from that line
  with the caller's set when the call happens. Its effects join the caller's (one global-flag overlay, one list of
  requests), nothing it changes about itself is kept (its remove or change this script included), its return code is
  ignored and the caller goes on; a callee that stops on an absent object, an index or its line budget ends only itself.
  A missing callee runs nothing, and so do a trap script and a monster-generator attachee (the runner's gates E4, E5).
  A callee outside the set refuses the whole dispatch (`Refused`, the callee's refusal), and so does a dialog action in
  its admitted lines (the original's dialog store for a called run is not read; refused at that line as
  `ActionNotAdmitted`). An unreadable callee ends the dispatch with `UnreadableScript`, and a nesting deeper than
  `ScrRunOptions.MaxCallDepth` (default 16, the original's bound is its stack; the deepest literal call chain in the installed scripts is 8, and four scripts are on call cycles; kept at 16 by the owner's decision of 2026-10-21, [rule modes](../world/rule-modes.md)) or more than `MaxCallDepth` ×
  `MaxExecutedLines` lines in the whole dispatch end it with `CallDepth`; both apply nothing. The result's trace is the
  top run's (empty for a refused callee, whose number the result names). The runner's dialog-point gates (E6) are not
  applied to a callee, so a dialog-point event never runs call script. Only single objects are admitted, so a call runs once; the original runs one call per triggerer and
  attachee pair of two groups.
- **Registry**: `ScrInterpreter.Call` marks 0x44B030 (partial); the 15 and 34 cases are in the 0x447220 row.
- **Installed original** (opt-in `AreaUseScriptsOriginalTests`, a new game's state): map mode's tile scripts that run grow
  from 254 to 644 of 3,012, and a tile of `void-kerghans castle` (script 3070) teleports through a call; the 21 guarded use
  sites keep 9 running and 12 refused (they need loops, call-ex or other conditions).
- **Hosts**: the viewer's use, tile and sector set gains all three and the object variables ("S4-area-use-reading-objects");
  a call that ends the dispatch (an unreadable callee, the call bounds) counts there as a run refused before it started, so
  a tile's unconditional teleport still fires. Its dialog set stays without them ("S3-area-sound-calls-scheme-schematic-reading"):
  a callee there would need the dialog gates, assign object could change the dialog's NPC, which the conversation takes
  from the click, and the host answers no object queries. A dialog-point event with call script in its set is refused
  before it runs (`ArgumentException`).

## Story state and story actions (ARC-SCRIPT-019, set S13)

`ScrAdmissionSet.S13` is S12 plus set PC quest state (16), mark map location (49), set rumor (50), add reputation (72) and
remove reputation (73), as the original runs them (ledger [SS1–SS5](../research/original-verification-ledger.md#story-state-and-story-actions-arc-script-019)).

- **Story state** is the session's number (`GameSessionOwner.QueryStoryState`, `RaiseStoryState`; snapshot schema 37): 0 at a
  new game, only ever raised. Get story state (42) reads it through the host, set story state (43) asks to raise it; the
  viewer's hosts apply the raise after the run.
- **Requests**: each action hands one `ScrStateRequest` per object to the host in action order (`ScrEffects.States`): the
  quest and state with the run's attachee as the giver (16, every object of operand 0), the area (49) and the rumor (50) for
  the first object of operand 1 (none for an absent one), the reputation (72, 73, every object of operand 0). Only single
  objects are admitted.
- **Hosts** (`AreaScriptStory.Apply`): the viewer's use, tile and sector set and its dialog set take them and apply each
  request for the PC through the session (`SetQuestState`, `DiscoverArea`, `LearnRumor`, `AddReputation`,
  `RemoveReputation`); a request for any other object is ignored, as the original's functions store nothing for it. A request
  the session refuses (a number outside its ranges, a quest state outside 0..6, where the original stores it unchecked)
  stops the later ones. A completed quest's experience award goes on as a dialog command's does: the dialog host queues it
  with its talk's awards, the use host hands it to the viewer, which levels the followers and shows the level-up messages.
  Not applied: the quest giver's reaction change (to 41 at state 2, 10 more at state 4) and the alignment and sound of a
  completion (owned by the reaction and quest domains).
- **Registry**: `QueryStoryState` marks 0x445090 (full) and `RaiseStoryState` 0x4450A0 (partial: single player).
- **Installed original** (opt-in `AreaUseScriptsOriginalTests`, a new game's state): map mode's tile scripts that run grow
  from 644 to 684 of 3,012; the 21 guarded use sites keep 9 running and 12 refused.

## Checks

- `tests/Arcanum.Scripting.Tests/Scr/ScrDispatcherTests.cs`: every step of the
  decision order, every end of a run with and without auto-removal, remove and
  change, writeback only on change, fresh locals, trap object destruction, X7,
  X8 and X10 in both modes, the own-write overlay, story state, computed-index
  stops, tile and sector writeback per mode, refusal, invalid events and
  options, the trace, the profile.
- `tests/Arcanum.Session.Tests/RuleModeTests.cs`: `GameRules.Scripts` per mode.
- `tests/Arcanum.Scripting.Tests/Scr/ScrTeleportTests.cs` (ARC-SCRIPT-014): both teleport actions and their operand order,
  absent objects, request order, each guard true and false, the wield locations, the absent object of a guard, the set
  and host checks; opt-in, `DAT_ROOT`: the S7 and S4 counts and the teleport requests of the installed scripts.
- Opt-in, `DAT_ROOT` = the game folder, `ScrDispatcherRealDataTests`: every
  installed script dispatched on a synthetic NPC slot from line 0 in both
  modes, with the file's own number. S2 admits 148 of 2,399 (the 144 of S1 and
  four that remove or change themselves); 116 end on a return, 25 empty scripts
  on an unreadable line 0, the seven trap scripts (30000..30006) go to trap code
  without running, 2,251 are refused before running; both modes end every run
  the same way. Since ARC-SCRIPT-009 the dispatch admits the S3 region from
  line 0: 376 scripts run (160 end on a return, 184 on a dialog action, 25 on an
  unreadable line 0, the seven trap scripts go to trap code), 2,023 are refused;
  the whole-script S2 count stays 148.
- `tests/Arcanum.Scripting.Tests/Scr/ScrDialogueDispatchTests.cs` (ARC-SCRIPT-009):
  region admission (reached lines, both branches, the dead false action, two
  refusals, entry outside the script), Virgil-like entries and resumption, the
  dialog action for a PC and a non-PC, mind control, object roles and
  `MissingObject`, the S3 set.
- `tests/Arcanum.Scripting.Tests/Scr/ScrPrintLineTests.cs` (ARC-SCRIPT-013): print line refused below S6, its lines
  in order from an object event and a call, none without an attachee, options wider than S6 refused, and the printed
  text rules (missing line, alternate text, tokens, action field, generated text).
- `tests/Arcanum.Scripting.Tests/Scr/ScrSoundTests.cs` (ARC-SCRIPT-040): the sound actions refused below S7, returned in
  action order with value operands read, an absent object playing nothing, a run with only sounds not empty. Opt-in
  `ScrAdmissionRealDataTests`: S6 admits 336 installed scripts whole, S7 342; the six more each have a sound action.
- `tests/Arcanum.Scripting.Tests/Scr/ScrSkillTests.cs` (ARC-SKILLS-009): get skill storing the host's levels, 0 outside 0..15 without
  asking, an absent object stopping the run, the store refused as a literal, the action refused below S11, a set with 47 refused for a
  host without skill queries; use item with skill handing its five operands on, nothing without a source or a target, the run going on.
  Opt-in `ScrAdmissionRealDataTests`: the installed uses of 47, 79 and 115 and the S11 count. Viewer:
  `AreaScriptHostTests.Get_skill_reads_the_heros_and_an_npcs_effective_levels`; session: `HeroSkillsTests` (the NPC skills from map data
  until the session saves a sheet).
- `tests/Arcanum.Scripting.Tests/Scr/ScrReadingTests.cs` (ARC-UI-049): the reading actions refused below S10, one request per action in
  order with the number and the viewer, an absent viewer showing nothing, the run going on, a run with only a request not empty; opt-in
  `ScrAdmissionRealDataTests`: the installed uses of 64, 65, 114 and 94 and the S10 count.
- `tests/Arcanum.Scripting.Tests/Scr/ScrSchematicTests.cs` (ARC-TECHNOLOGY-005): start schematic UI refused below S9 (without
  the S4 guards), one request per object, an absent object naming none, the run going on, a run with only a request not empty.
- `tests/Arcanum.Scripting.Tests/Scr/ScrSchemeTests.cs` (ARC-SCRIPT-016): set scheme refused below S8, one request per object in
  action order with a value operand, an absent object naming none, a focus group refused, a deferred call's requests; opt-in
  (`DAT_ROOT`): the one installed action and its admission. `tests/Arcanum.Session.Tests/AdvancementStateTests.cs`: the hero's and
  an NPC's scheme set without a purchase, run at the next level step, a scheme without a rule buying nothing, the refusals and the
  snapshot. `AreaScriptHostTests`: requests to an NPC record, a refused number and an unreadable object ignored.
- `tests/Arcanum.TerrainViewer.Tests/AreaScriptHostTests.cs`: the viewer host's
  slot over the session keys, writeback and baseline clearing, refusals in
  `Apply`, invalid stored keys, scripts and dialogs found by number once, the
  NPC-node check. `AreaConversationTests` (opt-in original): Virgil's talk runs
  lines 0..4 to node 1, resumption at line 5 returns, a resumption at line 16 is
  refused, following picks line 6 → node 385. ARC-SCRIPT-040: a scheme request reaches the session, effect sounds are
  taken once, a speaker is found by its object; the opt-in map conversation census finds no first talk (line 0, no
  follower, no resumption) that reaches a sound action yet.
- `tests/Arcanum.TerrainViewer.Tests/AreaUseScriptHostTests.cs` (ARC-SCRIPT-015): the use-script host over invented
  content: a held item and a quest guarding a teleport, names, holds and wielded items from rows and holders, a
  mind-controlled object, the use slot written back and a self-removing script, refusals that change nothing.
  `AreaMapTransfersTests`: which guarded use sites are scripted, the script's outcomes and teleport as the transfer.
  Opt-in `AreaUseScriptsOriginalTests`: the guarded door of `tulla interior` and the census of 21 guarded use sites.
- `tests/Arcanum.Scripting.Tests/Scr/ScrObjectVariableTests.cs` (ARC-SCRIPT-018): object variables, types and role writes,
  unset variables, admission of stores and indexes, the host check, and call script (header, start line, shared flags,
  effects, no writeback, the caller going on, a missing, a refused and a too deep callee).
  ARC-SCRIPT-017: tile and sector scripts run for the PC with their slot on the sector, per rule mode; the transfers run
  them on entry (tile, then sector) and drop a teleport while one is pending; opt-in: the exit tiles of `iron clan hq` and
  the census of 3,012 tile and 20 sector scripts.

## Not ready

- The session has no global variables (a snapshot schema change, coordinated with other schema work); no admitted set reads
  them. The story-state number is the session's since ARC-SCRIPT-019.
- Tile and sector writebacks name no location; the caller applies them to the
  tile or sector it dispatched.
- The AREA viewer's talk, resumption and deferred calls run the dispatcher (its set "S3-area-sound-calls-scheme-schematic-reading-story": S3,
  plus the sound actions, call script in / remove script call, set auto-level scheme, start schematic UI, the reading actions and the story
  actions; with a session hero also get skill, ARC-SKILLS-009); since ARC-SCRIPT-015 map mode's use
  clicks on guarded use sites run it under "S4-area-use-reading-objects-story", and since ARC-SCRIPT-017 the tile and sector scripts the player
  enters, and since ARC-SCRIPT-020 the use script of a written item the PC reads (`AreaUseScriptHost.RunItemUse`: attachment point 1,
  the PC as the user, no target, the item's inventory row; skip default stops the reading, a teleport is refused); heartbeat and
  combat are later slices.
- Not in any admitted set yet: focus groups, loops, call-ex, float line, reactions and most other world actions.
  Admitted since S4: teleport and fade-and-teleport (S4), set auto-level scheme (S8), start schematic UI (S9), the reading
  actions (S10), the skill commands (S11), call script, assign object and get object type (S12), set PC quest state, mark map
  location, set rumor and add or remove reputation (S13).
- The viewer's dialog host models only the dialog slot of the map's speakers;
  generators, traps, death, surrender, mind control and the jilted flag answer
  "no", and the generic greeting is a status line. Its use-script host (ARC-SCRIPT-015) models the use slot of any
  row of the shown map, mind control from the row's spell flags and the jilted flag from the session; generators and
  traps answer "no" there too.
- Use item with skill (79) is a request only: no host runs the goal, the check or the skill's handler, and no NPC source is modelled
  (SX3, SX4; no live installed script uses it). Try to steal 100 coins (115) waits for pick pocket (task S15).
- X2 (an uninitialised attachee type without attachee) is reachable only through
  call-script and is left to the slice that admits calls.
