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
   `TrapScript`. The trap domain ([trap state](../world/trap-state-runtime.md), ARC-WORLD-076) runs it and must reproduce what the original does
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
| X10 global-flag write (and the hero's PC flag write, GV4) | clears the flag, ORs the whole value shifted to its bit into the 32-flag word (2 sets the next flag, a negative value every higher one) | the value's low bit into the flag only (D2 fix) | `GlobalFlagWriteSpills` |
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
`dialog.counters` and `dialog.script` (since ARC-DEV-096 fields of the object, found by its
identity and saved in `objects.state`, schema 66; a value equal to the
loaded one clears its key where the record sits); global flags are the session's; the PC leads a
following speaker. It runs `S3` (until ARC-SCRIPT-019 without the story-state actions, which
now read and raise the session's story-state number). Run default is the original's generic greeting (ARC-DEV-324: the greeting builder's line floated over the NPC, no window;
`AreaConversationStart.GreetingLine`); a skip default without a dialog action ends the talk; a
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
- **Item events (ARC-SCRIPT-041, the transfer half of slice S8).** The session runs the get, transfer, remove-item and
  insert-item points of a pickup, loot and negative `IN` through `RunObject` in the executable's order, each run reading the
  earlier runs' flag writes, and a gate's skip default leaves the item ([item events](../world/inventory-runtime.md#item-events-arc-script-041),
  ledger [IE1–IE7](../research/ledger/scripts.md#item-events-arc-script-041)). The admitted set, call depth 16 and the line
  budget are unchanged.

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

- ARC-SCRIPT-2752: the use host's set admits use item with skill (79); its hosts start the NPC source's goal 40 for pick locks (ledger [SX7](../research/ledger/skills.md#script-skill-commands-arc-skills-009)), and leave the run unapplied for any other request.

- **`ScrAdmissionSet.S11`** is S10 plus get skill (47) and use item with skill (79); `ScrRunOptions` accepts any subset of S11 (now
  the interpreter's widest set; the default stays S5). A set with 47 needs a host that implements `IScrSkillQueries`, checked before
  any event runs. Read in the retail 1.0.7.4 executable by two independent readers: ledger
  [SX1–SX6](../research/ledger/skills.md#script-skill-commands-arc-skills-009).
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
- **The actions** (ledger [WR15](../research/ledger/ui-audio.md#reading-window-arc-ui-049)): a value operand (the
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
- **The action** (ledger [SW8](../research/ledger/technology.md#schematic-window-arc-technology-005)): its object
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
- **The action** (ledger [OL30](../research/ledger/social.md#companion-orders-and-loyalty-arc-res-064)): its object
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
  the run goes on to the next line (ledger [TR1, TR2](../research/ledger/world.md#map-transitions-arc-data-025)):
  the object from operand 0 (fade-and-teleport: 3), then the map as the script gives it, x and y as values;
  `RuntimeMap` is the map less 4,999 in 32 bits, as the original computes it. A fade-and-teleport also carries its seconds,
  sound and movie (`ScrTeleportFade`); the original skips it only in a network game. An absent object asks for nothing
  (the original would queue a teleport of no object; what its processing does with one is NOT_VERIFIED). The host owns the
  pending-teleport rule: the original keeps one pending teleport, a new request replaces it unless the pending one is the
  local PC's, which refuses the rest (TR4).
- **Guards** (ledger [SG1–SG6](../research/ledger/scripts.md#guard-conditions-arc-script-014)). A host whose set
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
type (34) and the run's ten object variables as a focus kind (11), as the original runs them (ledger [OV1–OV4](../research/ledger/scripts.md#object-variables-and-calls-arc-script-018)).

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
  A callee outside the set refuses the whole dispatch (`Refused`, the callee's refusal). A dialog action of a callee
  (ARC-SCRIPT-046, ledger X11) asks for its conversation as at the top (`Effects.Dialog`: the PC triggerer, the callee's
  attachee, the callee's number, line and node) and ends only the callee, so its caller goes on; the callee runs at its
  caller's attachment point (0x44B030 copies it), and before the conversation the attachee's slot of that point takes the
  callee's local flags and counters when they differ, its script number kept (`Effects.SlotWrites`, as call-ex keeps a
  slot). A second conversation in one dispatch is refused at its line (`ActionNotAdmitted`, a project limit; the original's
  dialog start for an open dialog is not read). A dialog action in a call-ex run stays refused (its store would write the
  slot the run itself changes). An unreadable callee ends the dispatch with `UnreadableScript`, and a nesting deeper than
  `ScrRunOptions.MaxCallDepth` (default 16, the original's bound is its stack; the deepest literal call chain in the installed scripts is 8, and four scripts are on call cycles; kept at 16 by the owner's decision of 2026-10-21, [rule modes](../world/rule-modes.md)) or more than `MaxCallDepth` ×
  `MaxExecutedLines` lines in the whole dispatch end it with `CallDepth`; both apply nothing. The result's trace is the
  top run's (empty for a refused callee, whose number the result names). The callee runs at the caller's point (0x44B030 copies it into the invocation, then the event runner 0x4449B0), so at the dialog point its attachee meets the dialog gates (E6: dead without the callee's death-speech flag or surrendered without its surrender-speech flag runs nothing, mind-controlled runs 2188; ARC-SCRIPT-2198; the hosts answer all three states for an NPC of the shown map, the session's danger record and spell flag 0x8000000 for the last two, ARC-SCRIPT-2464), and a dialog-point event may run call script. Only single objects are admitted, so a call runs once; the original runs one call per triggerer and
  attachee pair of two groups.
- **Transform of the attachee (ARC-SCRIPT-2440, ledger TI5b/TI5c)**: transform (40) adds its request as before; when the host says `IScrHost.RebindsTransformedAttachee` the interpreter also hands the later actions of the run (and the scripts it calls) the placeholder
  `ScrTransformedObject.Placeholder` (`transformed:N`) as the attachee, as the original writes the new handle into the run's own record. The host makes the object and rebinds the placeholder with `ScrEffects.Rebind` before it applies the later requests; the run's own writeback stays
  the old attachee's. A host that returns false (the default: the use, tile, thrown-item and other hosts) sees no change.
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
remove reputation (73), as the original runs them (ledger [SS1–SS5](../research/ledger/scripts.md#story-state-and-story-actions-arc-script-019)).

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

## Item effects (ARC-SCRIPT-042, set S14)

`ScrAdmissionSet.S14` is S13 plus the conditions identified (47) and items being rewielded (50) and the actions add effect (77),
remove effect (78), call-ex (81), the magick-tech adjustment (91) and play script eye candy (120): the commands the installed wield-on
and wield-off scripts use (ledger [WE1–WE6](../research/ledger/scripts.md#wield-events-arc-script-042)). A host whose set holds 47, 50
or 91 implements `IScrItemQueries` (`ScrRunOptions.Validate` refuses one that does not).

- **Effect actions** hand one `ScrEffectRequest` per object of operand 0 to the host in action order (`ScrEffects.EffectChanges`):
  add with the effect and the cause as read, remove with the effect (the first entry of the number, whatever its cause).
- **Magick-tech adjustment** stores `(ratio × value + 50) / 100` into operand 3 (32-bit wrap, truncation toward zero, a counter
  keeps the low byte), the ratio `IScrItemQueries.PowerRatio(item, owner)` of operands 1 and 2; admission refuses a number literal
  as the store. An absent object or one the host does not know stops the run (`MissingObject`).
- **Play script eye candy** hands `ScrEyeCandy(number, object)` to the host (`ScrEffects.EyeCandies`); an absent object shows
  nothing. ARC-SCRIPT-1592: the dialog host and the use host keep the requests of the runs they applied (`TakeEyeCandies`, in action order,
  taken once) and the viewer plays them on the shown map every frame as overlay runs of the script list (`AreaEyeCandyHost.PlayScript`, ledger [VT9](../research/ledger/scripts.md)).
  ARC-SCRIPT-1828: **stop script eye candy** (124) hands `ScrEyeCandy(number, object, StopScript)` on, which the hosts keep in the same list in action order (`AreaScriptEyeCandy.Stop`) and the viewer takes off through `AreaEyeCandyHost.StopScript`: the first run of the entry on the object only, as the original's walk 0x4243E0 does (ledger [WE12a](../research/ledger/scripts.md)).
  ARC-SCRIPT-2041: the focus kind **every portal in the vicinity** (19; set **S60**, S59 plus the kind) is built by the interpreter for a loop for and a first-object read through `IScrPortalQueries.PortalsInVicinity`, with the scenery's cap (ledger [PV1–PV7](../research/ledger/scripts.md#the-portal-vicinity-focus-kind-arc-script-2041-the-loop-of-the-ground-rune-tile-scripts)); the interpreter ran at most S60 until ARC-SCRIPT-2319. The use host lists the shown map's portals within the window's reach; its use, placed, dying and combat-point sets admit the kind, the heartbeat set does not.
  ARC-SCRIPT-2322: the focus kinds **every container in the vicinity** (17) and **any container in the vicinity** (18; one list, type mask 4; set **S62**, S61 plus both) are built by the interpreter for a loop for, a first-object read and a condition's list through `IScrContainerQueries.ContainersInVicinity`, with the same cap (ledger [CV1–CV7](../research/ledger/scripts.md#the-container-vicinity-focus-kinds-arc-script-2322-every-and-any-container-in-the-vicinity)); the interpreter runs at most S62. The use host lists the shown map's containers within the window's reach; its use, placed, dying, combat-point and heartbeat sets admit both kinds; the session's item set does not.
  ARC-SCRIPT-2248: the use set also admits toggle state (56) of S16, which the heartbeat set held alone, so the placed, dying and combat-point sets do too (ledger [OT1–OT7](../research/ledger/scripts.md#toggle-state-in-the-use-set-arc-script-2248-the-ground-rune-tile-script-2221)): the ground rune tile script 2221 is admitted whole; the host toggles an NPC of the shown map and refuses any other object whole at plan time (#2324).
  ARC-SCRIPT-2319: the focus kinds **every one in the group** (13) and **anyone in the group** (14; set **S61**, S60 plus both kinds) are built by the interpreter for an operand list and a first-object read as the triggerer's direct followers (`IScrGroupQueries.FollowersOf`, cut at 100) and then the triggerer itself, no filter (ledger [GF1–GF8](../research/ledger/scripts.md#the-group-focus-kinds-arc-script-2319-everyone-and-anyone-in-the-group)); the interpreter ran at most S61 until ARC-SCRIPT-2326. The use host answers from the session's follower array; its use, placed, dying, combat-point and talk sets admit the kinds, the heartbeat set does not.
  ARC-SCRIPT-2326: **get armor** (action 32; set **S63**, S62 plus the action) stores the armour part of the first object's current art id (bits 20..23 of a critter art, bits 20..22 of a monster art, 0 for any other art; no armour class) into operand 1 through `IScrCritterFieldQueries.ArmorOf`; and the group kinds are admitted in slot 0 of the list conditions the interpreter tests object by object (`ScrAdmissionSet.ListConditions`: is undead 38, is animal 37 and the others, kind 14 "any", kind 13 "every", the triggerer last: a PC triggerer is never undead), ledger [AR1–AR7](../research/ledger/scripts.md#get-armor-and-the-group-in-list-conditions-arc-script-2326); the interpreter runs at most S63. The use host reads the PC's armour from the worn body armour's use art and an NPC's from its stored current art; since ARC-SCRIPT-2342 a session-dressed NPC's from its worn body armour's use art and a polymorphed critter's (hero or NPC) as 0 (ledger AR8).
  ARC-SCRIPT-2321: the focus kind **any scenery in the vicinity** (16; set **S64**, S63 plus the kind) is the list of kind 15 (one case in the exe builder 0x44B4E0), resolved by the interpreter for an object operand, a first-object read and a loop for through `IScrWorldQueries.SceneryInVicinity`; a condition reads it as "one is enough" (ledger [AS1–AS6](../research/ledger/scripts.md#the-any-scenery-focus-kind-arc-script-2321-the-tile-script-of-the-kna-tha-map-and-the-bust-script-1734)); the interpreter runs at most S64. The use, placed, dying, combat-point and heartbeat sets admit it where they admit kind 15.
- **Identified** counts `IScrItemQueries.IsIdentified` over operand 0's objects (one at most in the set): true when it is identified,
  false for none or an absent object; an object the host does not know stops the run. **Items being rewielded** is
  `IScrItemQueries.ItemsBeingRewielded`.
- **Call-ex** runs the attachment of operand 1's point of operand 0's object from operand 2's line with operand 3 as the triggerer and
  no extra object, as an event of its own (0x441980): no slot, script 0, a missing or trap script and a monster generator run nothing;
  the run shares the dispatch's effects, global overlay and bounds (call depth 16 and its line budget, ARC-SCRIPT-018), and its slot,
  when it changed, is reported in `ScrEffects.SlotWrites` in the order the runs ended; a later call-ex of the same slot reads it. The
  caller applies the slot writes and then the event's own `Writeback`, so the event's slot wins when both changed it, as the original's
  outer run writes its copy last (and only when it changed). Project limits: a use or dialog point (the trap spot and the dialog
  gates are not modelled for a nested run), a dialog action in the callee and a trap object whose script becomes 0 refuse the
  dispatch; a point outside 0..35 or the dialog-override slot ends it (`ScrStop.CallExPoint`, nothing applied); an absent object
  stops the caller's run.
- **Hosts.** Only the session's item events run S14 commands (through `SessionItemScripts.Admission`, the
  [equipment's wield events](../world/equipment-runtime.md#wield-events-arc-script-042)); the viewer's use, tile, sector and dialog
  sets do not hold them. ARC-DEV-366: the item set also admits get stat (33, ledger [GS1](../research/ledger/scripts.md)), which the
  session answers for the hero from the effect list the move's earlier runs left (`IScrStatQueries` on the session's run host); a run that
  reads a stat after an effect change of its own that moves the stat, or a stat the session does not hold (GS3), refuses the move
  (`ItemScriptUnsupported`; a dispatch that ends as `ScrStop.NotModelled` is refused, not skipped).

## World objects (ARC-WORLD-081, set S15)

`ScrAdmissionSet.S15` is S14 plus random (37), loop for (18), loop end (19) and create object (52), the focus kinds "current looped
object" (10) and "every scenery in vicinity" (15), and a counter operand with any index (`CounterIndexWraps`): the commands of the plant
generator 30285 that template sectors run (ledger [PG1–PG13](../research/ledger/scripts.md#template-sector-scripts-arc-res-073);
contract [template sector plants](../world/template-sector-plants-runtime.md)). A host whose set holds random, loop for or focus kind 15
implements `IScrWorldQueries` (`SceneryInVicinity`, `Random`); `ScrRunOptions.Validate` refuses one that does not, and the interpreter
ran at most S15 until S16 (below).

- **Random** stores a draw of `IScrWorldQueries.Random` between its two values (either order, both included) into operand 2; admission
  refuses a number literal there.
- **Loop for** fills the run's list once from its object operand, remembers the line after it and makes the first object the current
  looped object; an empty list goes on after the first later line with the "true" condition and a loop end as its true action
  (`ScrAdmission.AfterLoopEnd`, 0x44BC60), and without one past the end. **Loop end** steps to the next object and jumps back, or clears
  the loop after the last (the current looped object stays). A loop for inside a running loop and a loop end without one do nothing.
  Region admission takes a loop end's successors as the next line and the line after every loop for of the script.
- **Focus kind 15** is the host's list of scenery within `ScrWorld.VicinityReach` (10) tiles of the run's attachee on both axes (none
  without one), the first `ScrWorld.VicinityListCap` (100) of it under `ScrRuleProfile.VicinityListCapped` (Original, PG3); as a single
  object it names the list's first. Focus kind 10 names the current looped object (none before the run's first loop).
- **Create object** hands `ScrCreateRequest(prototype, near)` to the host (`ScrEffects.Creations`): the number as named (the host adds 20,
  PG8) and the first object of operand 1; an absent object asks for nothing.
- **Counters** read and write counter (index mod 4) for any index (PG6), the original's five-bit shift.
- **Hosts.** The viewer's use, tile and sector set "S4-area-use-reading-objects-story-world" (`AreaUseScriptHost.Admission`; tile and sector runs add the dialog action, `PlacedAdmission` "S4-area-placed-dialog", since ARC-SCRIPT-046) holds the
  S15 world commands (not S14's item commands); its host answers the vicinity from the shown map and draws from the session, and hands
  each creation to `GameSessionOwner.CreateGroundItem`, which applies the Steamwoven limit (`ScrRuleProfile.CreatedItemsUnbounded`,
  Original: none).

## Object state actions (ARC-WORLD-114, set S16)

`ScrAdmissionSet.S16` is S15 plus toggle state (56) and kill (58); the interpreter ran at most S16 until S17 (below). Both hand one
`ScrObjectStateRequest(change, object)` per object of their object operand to the host, in action order (`ScrEffects.ObjectStates`;
an absent single object asks for nothing): `ToggleOff` flips the object's "off" flag 0x2 by the raw field writes, not the object flag
setter, so its lights are left as they are (ledger [PM2](../research/ledger/scripts.md#unchosen-pre-made-heroes-arc-world-114)); `Kill`
is a death with no killer (0x45D900, PM3). The run goes on after either. The installed users that matter now are the Crash Site
pre-made heroes' heartbeat scripts 1781..1792 ([character creation evidence](../research/character-creation-evidence.md#unchosen-pre-made-heroes-arc-world-114)).

- **Hosts.** The viewer's heartbeat set "S2-area-heartbeat-state-kill" (`AreaUseScriptHost.HeartbeatAdmission`, S2 with the
  single-object focus kinds plus 56 and 58) runs an NPC's first-heartbeat and heartbeat scripts from the AI host
  ([heartbeat scripts](../world/ai-host.md#heartbeat-scripts-arc-world-114)); its host applies a kill through the world's
  `AreaScriptWorld.Kill` and an NPC's toggle as the map change `obj.off`. No other host's set holds them.

## Critter vicinity and the dialog point in call-ex (ARC-SCRIPT-047, set S17)

`ScrAdmissionSet.S17` is S16 plus the focus kinds everyone in the vicinity (8) and anyone in the vicinity (9); the interpreter runs
at most S17. Both name the list `IScrWorldQueries.CrittersInVicinity(centre)`: the PCs and NPCs whose tile is within
`ScrWorld.VicinityReach` (10) tiles of the attachee's on both axes, the attachee itself and dead critters included, destroyed or
switched-off ones left out, at most 100 (`VicinityListCapped` past that; ledger
[TG4](../research/ledger/scripts.md#script-triggers-arc-script-047)). An action names the whole list (loop for, or one request per
object), an optional single object takes its first object. `Check` refuses kinds 8 and 9 in a condition operand (`FocusKindNotAdmitted`);
the list rule of SG6 (every object, or any) is the interpreter's since S18 for the dead and switched-off conditions, which a set may
pair with the scenery list (15).

Call-ex (81) now runs the dialog point (9): it goes through the object entry as the original's 0x44B170 does, so the dialog gates of
0x4449B0 apply (TG5): a dead attachee whose script lacks file flag 0x8 or a surrendered one without 0x10 runs nothing (the action
goes on as a call that ran default), a mind-controlled one runs script 2188 in place of its slot's script, whose slot keeps the
held script number. A dialog action in such a callee is allowed: the callee's dialog store is the slot the call-ex writes back (X11),
so `ScrDialogStart` carries the callee, the line, the node and `Point = 9`; a host opens the talk and resumes that slot (TG8). A
dialog action at any other point carries that point. Call-ex of the use point (1) stays refused (a nested use's trap spot is not
modelled). A callee of the dialog point that reaches call script (7) is refused, as a dialog-point event's set may not hold
call script (the called script would run at the dialog point without the gates). The call-ex's writeback supersedes the callee's
dialog store of the same slot (one `SlotWrites` entry for the pair; an earlier call-ex of the slot keeps its own).

- **Hosts.** The viewer's heartbeat set "S2-area-heartbeat-state-kill-talk" (since ARC-SCRIPT-049 "S2-area-heartbeat-state-kill-talk-world", below and
  [heartbeat scripts](../world/ai-host.md#heartbeat-scripts-arc-world-114)) admits 8 and 9 with loop for, loop end, get object type,
  call-ex and dialog: that runs Virgil's opening heartbeat 1100 ([script trigger audit](../research/script-trigger-audit.md)). No
  other host's set holds the critter kinds.

## Heartbeat world questions (ARC-SCRIPT-049, set S18)

`ScrAdmissionSet.S18` is S17 plus the conditions obj is dead (22) and obj is switched off (27) and the actions distance between (71)
and set faction (134); the interpreter runs at most S18 (ledger [HW1–HW4](../research/ledger/scripts.md#heartbeat-world-questions-arc-script-049)).

- **Obj is dead, obj is switched off.** A host that admits them implements `IScrObjectStateQueries` (`IsDead`, `IsSwitchedOff`;
  the run options refuse a set with them for a host without it). Each tests the objects of operand 0 and holds by the list rule of SG6: at least one holds,
  and all of them or the focus kind is an "any" kind; an empty list (an absent single object) is false. Dead is the original's test
  0x45D8D0 (current hit points 0 or less), switched off the object flag 0x2. A null answer is an object the host does not know: the
  run stops (`MissingObject`). The critter list kinds stay refused in a condition operand; the scenery list (15) is the one list a
  set may test (the viewer's heartbeat set has none).
- **Distance between.** The first objects of operands 0 and 1, their tile distance (the larger of the two axis differences, 0x441AE0 →
  0x4B96F0) into operand 2, cut to 32 bits; a literal store is refused (`WriteToNumber`). The places come from
  `IScrWorldQueries.Location`; an absent object, or one without a place, stops the run (the original reads the null handle's
  location).
- **Set faction.** One `ScrObjectStateRequest` (`SetFaction`, the first object of operand 0, the faction as `Value`); an absent object
  asks nothing. The original (0x45D0C0) writes the faction field 0x124 of an NPC only; the host leaves any other object as it is.
- **Run order.** A toggle state (56) or kill (58) earlier in the same dispatch counts for a later dead or switched-off test of that
  object (the original flips the flag and kills at once): the interpreter keeps the toggles' parity and the killed objects of the
  dispatch over the host's answers.
- **Hosts.** The viewer's heartbeat set (since ARC-SCRIPT-049 "S2-area-heartbeat-state-kill-talk-world",
  [heartbeat scripts](../world/ai-host.md#heartbeat-scripts-arc-world-114)) admits the four with following (18), is named (20), has a
  reputation (33) and call script in (54). A heartbeat's call script in is queued; the call runs later under the talk host's set when it
  fires, and is refused there when that set does not admit the callee or no talk host is open on the map (the census counts the
  heartbeat, not its callee).

## Talk set (ARC-SCRIPT-050, set S19)

`ScrAdmissionSet.S19` is S18 (ARC-SCRIPT-049) plus float line (23) and get area (84); the interpreter runs at most S19. Slice 2 of the
[script trigger audit](../research/script-trigger-audit.md#6-fix-slices-prioritised); both actions read in the executable (ledger
[FL1–FL5, GA1–GA3, ML1](../research/ledger/scripts.md#talk-commands-arc-script-050)).

- **Float line** (operands: the DLG line, the objects). Its dialog PC is the first PC in the game-window rectangle around the run's
  **triggerer** (`IScrWorldQueries.CrittersInVicinity(triggerer)`, the first object the host's `IsPc` accepts); without a PC there nothing
  happens and no operand is read; without a triggerer nothing happens either (a project choice: the original's list read of a null
  handle was not traced). Otherwise each object of the object operand, in list order, gets one `ScrFloatLine`
  (the running script's number as for print line, the line, the object as the speaker, the PC) in `ScrEffects.Floats`. No attachee is
  needed. The host builds the text as print line does (`ScrPrintedLine.Read`, with the alternate text for a PC that is not male); the
  original runs the line's action field between the PC and the speaker and hands the text to the dialog interface's speech display
  (FL4, FL5).
- **Get area** (operands: the object, the store). `IScrAreaQueries.AreaOf(object)` stores the area of the **current map** from its
  map-list row (`MapListEntry.Area`, the row's optional `Area:` value read in the original's order after `Type:` and `WorldMap:`, ML1;
  `MapList.AreaOfRuntime` is 0x40FFE0), whatever the object's map; on the start map with area 0 the original takes the area at the
  object's location (GA3, GA4: the nearest area whose location's sector has the same non-zero town map as the object's sector,
  `WorldTravelGeometry.AreaAt`; ARC-DEV-178). The host answers null when it cannot place the object (the world-map data is still loading, or the
  object's sector is not among the sectors the viewer loaded) and then the dispatch ends with the stop `NotModelled` (skip default,
  nothing of it applied, a float line or a flag write before it included); the viewer reports such a talk or deferred call as not ready.
  An absent object stops the run (`MissingObject`) as at every single-object read; a literal store is refused (`WriteToNumber`).
- **Print line and the action field.** A DLG action field that holds only separators (space, tab, `,`, `;`, `:`, `(`, `)`) has no
  operation, as the conversation grammar reads it, so `ScrPrintedLine.Read` shows such a line (24 of the 30 floated lines whose field is
  not empty hold one or two spaces). This is the grammar's admission policy, not a reading of the original's scanner. A real operation is
  still `Unsupported`.
- **Hosts.** The AREA viewer's dialog host (`AreaScriptHost`) runs the talk set "S3-area-talk-queries" (with a session hero
  "S3-area-talk-queries-skill"): its earlier set plus the guard conditions of S4 (PC quest state, holds an item named, is named, wields an
  item named, has a reputation), obj is dead and obj is switched off of S18, random (37), float line and get area, while its `QueryHost` gives the shown map's use host
  (`AreaUseScriptHost`) of the talk's map with the PC on it, built for the shown snapshot only, which answers them as for a use or heartbeat run (map mode wires it; since ARC-DEV-081 a carried companion talks through the same host but keeps the earlier set, `AreaScriptHost.AdmissionFor`). `AreaScriptWorld.MapArea` gives the use host the shown map's map-list area and whether it is the start map.
  The dialog host applies a run's float lines first: a missing or unreadable DLG floats nothing (the original's load fails), a line
  with an action field or generated text leaves the whole run unapplied; the shown texts go to the viewer (`TakeFloats`), which floats
  them over the speaker in the floater field. Over the installed interiors (the opt-in walk of `AreaMapConversationOriginalTests`):
  94 talks start and 14 end in their script, against 50 and 1 without the queries; 12 of them float a line and none reaches a floated
  line with an action field. The census admits 115 of the 180 dialog rows from line 0 (52 before).

## Tile-script actors (ARC-SCRIPT-052, set S20)

`ScrAdmissionSet.S20` is S19 (ARC-SCRIPT-050) plus destroy (68) and stop attacking (109); the interpreter ran at most S20 until ARC-DEV-100 (S21, below). Slice 8 of
the [script trigger audit](../research/script-trigger-audit.md#6-fix-slices-prioritised), its first group: call-ex (81), destroy and stop
attacking are the actions most refused tile scripts reach (ledger [TD1–TD3](../research/ledger/scripts.md#tile-script-actors-arc-script-052)).

- **Destroy, stop attacking.** One `ScrObjectStateRequest` (`Destroy`, `StopAttacking`) for the first object of operand 0, and the run
  goes on; an absent object asks nothing (a project choice: the original passes the null handle on without a test). The original's
  destroy (0x43CCA0, TD2) leaves an object already destroyed as it is, runs the object's destroy point (2) first and keeps it on a skip
  default, then takes it out of the world (object flag 0x1); stop attacking is 0x4AA4A0 (TD3), the session's `StopAttacking`.
- **Hosts.** The viewer's tile and sector set (`AreaUseScriptHost.PlacedAdmission`, "S4-area-placed-dialog-actor") adds call-ex, destroy
  and stop attacking to the dialog action. A tile that an NPC steps on runs his own dialog point through call-ex as a heartbeat does
  (ARC-SCRIPT-047): Virgil on the altar tiles (1328) runs his dialog script 1324 from line 14, which asks for node 250, and the viewer
  opens that talk. Its reply `l:` (record 251) ends the talk and opens the world map; the other two close it (ledger [VA1–VA5](../research/ledger/scripts.md#virgils-altar-talk-and-the-world-map-reply-l-arc-script-1358), ARC-SCRIPT-1358). Destroy acts on an NPC of the shown map: it is kept as the map change `obj.destroyed` (1), and the view no longer draws
  or reaches the NPC (`AreaObjectTargets.Gone`, the object lists, the AI host); one destroyed already, in its map data or by a script,
  changes nothing. The PC, a follower of the PC (the party removal is not read) and an NPC whose destroy point holds a script (the
  destroy event is audit slice 4) leave the run unapplied. Stop attacking acts on the PC (each of its followers) or an NPC of the shown
  map: the session's `StopAttacking` with the AI host's facts (`AreaScriptWorld.PeaceFacts`), then the AI host's part of each step
  (`AreaScriptWorld.Stopped`, the animation interrupt), as an order's stop does; an NPC it reaches that fights the hero's side without
  reaction facts, or is in combat mode without combat-mode facts, leaves the run unapplied before anything applies (the session would
  refuse it). A destroy is not seen by later lines of the same run (a dead, switched-off or vicinity test still finds the NPC), and a run
  that asks for a talk with an NPC destroyed before or by it is left unapplied.
- **Installed original.** Of the 3,012 tile scripts of the 79 maps map mode opens, 829 run and 2,053 are refused (684 and 2,198 before); since ARC-DEV-100 (S21, below) 856 run and 2,026 are refused, and since ARC-DEV-157 (S22) 1,186 run and 1,696 are refused (ARC-DEV-166, 2026-11-07),
  each in a new game's state; the first refusals left are listed in the audit's slice 8.

## Script stores (ARC-DEV-100, set S21)

`ScrAdmissionSet.S21` is S20 plus the value kinds global variable (1), PC variable (5) and PC flag (6) (`ScrAdmissionSet.StoreValueKinds`); the
interpreter ran at most S21 until ARC-DEV-157 (S22, below). MIG8 slice 4 of GR29 (SA13, SA14): the session holds the stores since ARC-DEV-098, and this slice lets a run read and
write them (ledger [GV1–GV4](../research/ledger/scripts.md#script-stores-arc-dev-100)). A set that names a store kind needs a host that implements
`IScrStoreQueries` (checked before any event runs, like the other query ports).

- **Global variable (kind 1).** A 32-bit word, index 0..1999 (admission refuses an index outside it; the index is always the operand's literal,
  so the run's own `IndexOutOfRange` stop is only a guard), zero at a new game. A write keeps the whole value, unmasked (GV1). The host answers `GlobalVariable(index)`.
- **PC variable and flag (kinds 5, 6).** The operand packs a focus kind in its high 16 bits and the index in its low 16 (0..65535, GV3, GV4). The
  object is the first of that kind (an object variable always 0, the vicinity kinds the first object of their list); admission refuses a high
  half that is no focus kind (`UnknownFocusKind`, negative included) or one the set does not admit (`FocusKindNotAdmitted`). An object that is
  not a PC (`IScrHost.IsPc`) reads 0 and ignores a write; an absent object stops the run (`MissingObject`, the project's rule for OV3). The host
  answers `PcVariable(index)` and `PcFlag(index)` for its one PC; the session answers 0 and false without a hero. A flag write spills into its
  32-flag word under the Original setting exactly as a global flag's does (`GlobalFlagWriteSpills`, X10, GV4) and writes the value's low bit
  into the flag alone in Steamwoven.
- **Run and writeback.** The stores are one overlay of the dispatch (a called script shares its caller's, as for the flags): a read finds the
  run's own write first. `ScrEffects.GlobalVariables`, `PcVariables` and `PcFlags` carry each value the dispatch changed (a value equal to the
  store's is no change), ascending by index; `IsEmpty` counts them.
- **Application.** `GameSessionOwner.ApplyScriptStores(effects)` applies them in that order as versioned writes; a session without a hero ignores
  the PC's (SA14), and any other refusal stops the later ones and the host reports the run unapplied. The viewer's dialog host
  (`AreaScriptHost.Admission` and the sets built on it) and use host (`AreaUseScriptHost.Admission`, `PlacedAdmission`, `HeartbeatAdmission`)
  admit the three kinds, answer them from the session and apply them with the other effects; with them Dernholm castle's opening heartbeat
  1015, the one installed heartbeat whose first refusal was a global variable operand, is admitted.
- **Item events (ARC-DEV-152).** `SessionItemScripts.Admission` admits the three kinds too: the session's run host answers them, a
  later run of the same move reads the earlier runs' writes first (as the move's global flags), and the commit writes them into the
  stores with the move's one revision (a declined move keeps them, as its flags); a session without a hero drops the PC's (SA14).
- **DLG (ARC-DEV-152).** The dialog's own GV, PV and PF operations are the conversation grammar's, not the interpreter's (ledger
  [DG1–DG4](../research/ledger/scripts.md#dialog-script-stores-arc-dev-152), [conversation runtime](dialog-conversation-runtime.md)).
- **Per PC (ARC-DEV-265).** A PC's flags and variables are fields 272 and 273 of that PC object (SA14), so the interpreter keys a PC cell
  by its PC: `IScrStoreQueries.PcVariable(pc, index)` and `PcFlag(pc, index)` are asked only for an object the host's `IsPc` calls a PC,
  the run's overlay keeps each PC's cells apart, and `ScrEffects.PcVariables` and `PcFlags` carry the PC with each write
  (`ScrPcVariableWrite`, `ScrPcFlagWrite`, ordered by the PC and then the index, `ScrPcOrder`). The session holds the arrays of the hero
  alone (`SessionScriptLimits.IsHero`: the script object `pc` on any map); another PC, which only a network game has (FR-NET), reads 0 in
  the session's and the viewer's hosts and `ApplyScriptStores` and the item events drop its writes. The DLG operations act on the
  conversation's PC (DG1, DG3: the dialog state's first handle), the hero, and are unchanged. No schema change.

## Script stats (ARC-DEV-157, set S22)

`ScrAdmissionSet.S22` is S21 plus get stat (33); the interpreter runs at most S22. Bug B23 (tile script 30286 refused at action 33) and the largest
first refusal of the tile scripts in the [audit](../research/script-trigger-audit.md#6-fix-slices-prioritised) (608 rows). Ledger
[GS1–GS4](../research/ledger/scripts.md#script-stats-arc-dev-157). A set that holds the action needs a host that implements `IScrStatQueries`
(checked before any event runs, like the other query ports).

- **The command.** Operand 0 is the stat number (a value), operand 1 the object, operand 2 the store (admission refuses a literal number there,
  `WriteToNumber`). The stored value is the host's `EffectiveStat(obj, stat)`, the original's stat getter (GS1). Intelligence (4) above 4 stores 1
  when the host's `IsDumbByBackground(obj)` says the object's background is dumb (55, 74, 75; GS2); the interpreter asks it only then.
- **Not modelled.** A host that cannot answer (`EffectiveStat` null) ends the whole dispatch with `ScrStop.NotModelled` and nothing applied, as
  get area on the start map does (GA3); an absent object stops the run (`MissingObject`, GS4).
- **The viewer.** `AreaUseScriptHost.Admission` (the use set, and the tile and sector set built on it) holds get stat and answers it for the PC
  only, from `GameSessionOwner.QueryHeroScriptStat` (strength to charisma 0..7, aptitude 16, level 17, alignment 19; GS3) and
  `QueryHeroDumbBackground`; an NPC, another stat and a session without a hero are not modelled. Such a run is reported as
  `AreaScriptedUseOutcome.Refused` (`script N: NotModelled`) instead of a silent default (only get stat reaches it in this host; get area is not in its sets and the dialog host reports its own not-modelled stop). A draw of a random number made before the stat read is not undone (the session generator advanced), as for any run that ends without applying. The dialog host and the heartbeat set do not hold the action.
- **The vicinity loop (bug B20).** `AreaUseScriptHost.PlacedAdmission` (tile and sector scripts) also admits the focus kinds everyone (8) and
  anyone (9) in the vicinity of S17 in actions. The list is around the run's attachee and a tile or sector run has none, so it is empty (PG3) and a
  loop over it jumps past its loop end, as the original's; the use set keeps its S4/S15 kinds. A tile or sector script that uses kind 8 or 9 outside a loop, or inside a PC variable or flag operand, now reads no object (`MissingObject`) instead of being refused at admission, so the tile's unconditional teleport fallback no longer fires for such a script (PG3, GV3). With get stat and these kinds 45 installed scripts
  more are admitted whole from line 0 (1,244 of 2,399, were 1,199) as the tile and sector set.
- **Not done.** Script 2768 (Brego's ghost) is still refused: its loop body casts a spell on the looped object (action 61, cast spell on,
  0x448AB7, MT21 of the magic plan), which needs a host spell cast; it is the first refusal of 16 installed scripts under this set. Stats other than GS3's, an NPC's stats, and the
  dialog host's and heartbeat sets' use of get stat.



## Script reactions (ARC-DEV-178, set S23)

`ScrAdmissionSet.S23` is S22 plus get reaction (29); the interpreter ran at most S23 until S24 (below). Bug B19 (the town NPC talk of script 1817 stopped at
action 29). Ledger [GR1, GR2, GA4](../research/ledger/scripts.md#script-reactions-and-start-map-areas-arc-dev-178). A set that holds the action
needs a host that implements `IScrReactionQueries` (checked before any event runs).

- **The command.** Operand 0 and operand 1 are objects (their first), operand 2 the store (admission refuses a literal number there,
  `WriteToNumber`). The stored value is the host's `ReactionOf(npc, pc)`, the original's standard effective reaction of operand 0 toward operand 1
  (0x4C0CC0, R1: 0 while the PC is on the NPC's enemy list, 50 for a first object that is no NPC or a second that is no PC).
- **Not modelled.** A host that cannot answer (`ReactionOf` null) ends the whole dispatch with `ScrStop.NotModelled` and nothing applied; an absent
  object stops the run (`MissingObject`).
- **The viewer.** Only the AREA dialog host (`AreaScriptHost.TalkAdmission` and `SkillTalkAdmission`) holds the action: it answers for one of its
  speakers toward the PC from the session's reaction query, the one the dialog's `re` condition reads, so a script and a reply see the same value.
  ARC-SCRIPT-2505: any other NPC of the shown map toward the PC (Kerghan's script 1105 line 57 loop) is the use host's `ReactionOf`, the AI host's standard reaction through
  `AreaScriptWorld.StandardReaction`, and a first object that is no NPC (the PC among them) gives 50. Any other pair is not modelled (the use, tile, sector and heartbeat sets do not hold the action).
- **Tests.** `ScrReactionTests` (the interpreter, a fake host), `AreaTalkQueryTests` (the viewer's host), `WorldTravelTests` (`NearestArea`,
  `AreaAt`), `AreaWorldMapTests` (the town maps of the shown sectors and of the area table).


## Town-map unfog (ARC-DEV-205, set S24)

`ScrAdmissionSet.S24` is S23 plus unfog town map (113); the interpreter runs at most S28 (ARC-DEV-236, S14e: S27 plus become prone 93, [GT187](../architecture/goal-table.md#the-s14d-read-of-the-executable)); S27 (ARC-DEV-234, S14c: S26 plus try to steal 100 coins 115, [GT164](../architecture/goal-table.md#the-s14-read-of-the-executable)); S26 (ARC-DEV-233, S14b: S25 plus perform animation 62 and heal fatigue 76, [GT155–GT182](../architecture/goal-table.md#the-s14b-read-of-the-executable); ARC-DEV-231, MIG13 S14a: S24 plus walk to 69 and run to 74, requests the AI host starts as goal 7 or 55, [GT154–GT167](../architecture/goal-table.md#the-s14-read-of-the-executable); the viewer's tile, sector and heartbeat sets). Bug B22 (the wreck's sector script 3036 was refused at
every entry of its sector). Ledger [UF1–UF4](../research/ledger/scripts.md#town-map-unfog-arc-dev-205).

- **The command.** Operand 0 is a number, the town map. A positive one is handed to the caller in `ScrEffects.TownMaps`, in action order, with no
  object (the original's town map is game-wide); 0 asks for nothing (0x4BEAB0 returns at once), nor does a negative number (no town map has
  one). The run goes on with the next line.
- **The session.** `GameSessionOwner.RevealTownMap` keeps the town map in the story section's `revealedTownMaps` (schema 71, upgrade step 70 → 71
  adds the empty list): 1..`SessionStoryLimits.LastTownMap` (4095), a revealed one NoChange, a number outside refused (`InvalidTownMap`).
  `IsTownMapRevealed` and `QueryRevealedTownMaps` read it. The original keeps a bit per cell in a `.tmf` file of the save folder (PS25, UF3) and
  checks the number against `rules\townmap.mes` and the town map's `.tmi` file (UF2); the session keeps only the whole-map reveal and reads
  neither file.
- **The viewer.** The use set (`AreaUseScriptHost.Admission`) and through it the tile and sector set (`PlacedAdmission`) hold the action; the
  story step (`AreaScriptStory`) applies the town maps after the PC's story requests and skips a number above the session's range (the original
  skips a town map it holds no info for, UF2), so the run's other effects apply. The dialog and heartbeat sets do not hold it: the
  [census](../research/scr-census.md) counts it in two scripts (13 live lines), one a use script ([audit](../research/script-trigger-audit.md),
  first refusals of the use point) and one the sector script 3036 (bug B22).
- **Tests.** `ScrTownMapTests` (the interpreter), `TownMapStateTests` (the session, the snapshot and the upgrade step),
  `AreaUseScriptHostTests` (a sector script's reveal in the viewer).

## Story and time commands (ARC-SCRIPT-1165, set S34)

`ScrAdmissionSet.S34` is S33 plus six conditions and thirteen actions (`StoryConditions`, `StoryActions`); the interpreter runs at most S34
(`ScrRunOptions.Validate`). Ledger [SQ1–SQ14](../research/ledger/scripts.md#story-and-time-commands-arc-script-1165-e5-c); plan slice E5-c.

- **Conditions.** Daytime (1: the hour is 6 to 17), global quest state (7: the quest's game-wide state equals the value), obj has blessing (8) and obj has curse (9)
  (the PC's lists, the list rule of SG6), rumor known (40: the first object is a PC that learned the rumor) and rumor quelled (41: game-wide).
- **Clock actions** (96 day since startup, 97 current hour, 98 current minute, 105 hours since startup) store a number into operand 0 from
  `IScrStoryQueries.Time` (`ScrGameTime`); a host with no clock ends the dispatch with `NotModelled` and applies nothing; a literal store is refused at admission.
- **Story actions** hand one `ScrStateRequest` each to the caller, in action order with the older story requests: set quest global state (17: only 4 or 6 are stored and a
  final state stays; the request is asked only when it changes the state as the run sees it), add and remove blessing and curse (25 to 28, per object of operand 1),
  quell rumor (51, the first object of operand 1), give quest XP (63, the first object of operand 0 and a quest experience level), grant one fate point (117) and set PC quest
  unbotched (119, per object of operand 0).
- **The run's own changes** to the game-wide quest states, the quelled and learned rumors and the PC's blessings and curses are read back by its later conditions (the
  original changes the game at once); a PC's quest state set with action 16 is not.
- **The host.** `IScrStoryQueries` (the clock, `GlobalQuestState`, `RumorKnown`, `RumorQuelled`, `HasBlessing`): `AreaUseScriptHost` and `AreaScriptHost` implement it over
  the session (`AreaScriptStoryQueries`) and hold the commands in all their sets (use, placed, heartbeat, dying, dialog and talk); `AreaScriptStory.Apply` gives the requests to the
  session (`SetGlobalQuestState`, `QuellRumor`, `AwardQuestExperience`, `GrantHeroFatePoint`, `UnbotchQuest`, `AddHeroBlessing`, `RemoveHeroBlessing`), for the PC only
  (a session without a hero or experience tables gives nothing for the PC-only ones and the run goes on); a blessing or curse needs the host's `BlessingEffects` rule, which the
  viewer sets from the module's tables (`BlessingEffectOf`), and without it the action is refused.
- **Limits.** The item-event host (wield on and off, hit) does not run these commands, so a blessing item's script stays refused there; the "received" message and the logbook
  mark of a blessing or curse and the sound of an unbotched quest are not shown; a quest or rumor number outside the session's ranges is refused (the original stores it unchecked)
  and stops the later requests, and reads as 0 or false in a condition.
- **Tests.** `ScrStoryTimeTests` (the interpreter), `StoryStateTests` (the session's two new writes and the snapshot rule), `AreaUseScriptHostTests` (the viewer host over a real
  session), `AreaStoryScriptAdmissionTests` (the sets and, with `DAT_ROOT`, the 96 installed scripts: S34 admits 52 whole).

## NPC state commands (ARC-SCRIPT-1163, set S36)

`ScrAdmissionSet.S36` is S35 plus nine actions (`NpcStateActions`); the interpreter runs at most S36. Ledger [NS1–NS8](../research/ledger/scripts.md#npc-state-commands-arc-script-1163-e5-a); plan slice E5-a.

- **Disband (22)**, **set and adjust reaction (30, 31)**, the **standpoints (45, 46; extended 140, 141 with a map)** and **change art number (59)** hand one `ScrObjectStateRequest` each to the caller in action order
  (`Disband`, `SetReaction`, `AdjustReaction`, `SetDayStandpoint`, `SetNightStandpoint`, `ChangeArtNumber`; the extended standpoints carry the catalogue map in `Map`; the standpoint's second word already has the original's
  sign extension OR-ed in). A disband also removes the leader as the run sees it, so its later following tests read false.
- **Change script (99)** changes the slot of an attachment point of any object: the run's own attachee and point set the run's own result (as change this script), any other slot keeps its flags and counters, takes the number and is
  kept in `ScrEffects.SlotWrites` and in the run's slot overlay (a later call-ex reads it). A point beyond 35 ends the dispatch (`CallExPoint`).
- **The host.** `AreaUseScriptHost` holds all nine since ARC-SCRIPT-1735 (eight until then) in its use, placed, heartbeat and dying sets (`NpcStateHostActions`): disband is the session's forced leave, the reactions go to `AreaScriptWorld.ScriptReaction`
  (the viewer wires `AreaAiHost.ScriptReaction`), the standpoints are the session's saved object fields (`SessionNpcStandpoint`) that the AI host's route facts prefer to the row's, the extended map is written by
  `GameSessionOwner.SetTeleportMap`. Change art number (ARC-SCRIPT-1735, ledger [AN1–AN9](../research/ledger/scripts.md#change-art-number-arc-script-1735-e5-a-tail)) writes the object's art id with frame 0 as the session's saved object field `obj.art` (`SessionObjectArt`, `GameSessionOwner.SetObjectArt`, `QueryObjectArt`, `QueryObjectArts`; no schema step) and only when an ART file of the module names the id (`AreaScriptWorld.ArtExists`); the viewer draws a scenery with the new art in place of its T0 body. A scenery of a loaded map and (ARC-SCRIPT-1779, ledger [AN10–AN12](../research/ledger/scripts.md#change-art-number-arc-script-1735-e5-a-tail), and ARC-SCRIPT-1826, [AD1](../research/ledger/scripts.md#change-art-number-arc-script-1735-e5-a-tail)) an item take a new art: an item with a loaded row, an item of another map's data carried here (its row read from the origin map the session names) and a made or script-created item (its prototype's art is the baseline; `made:n` keeps the `obj.art` record as a mobile object) (an item's number within its type's range, `ObjectArtNumber.ItemRange`; its ground and flight art only, the inventory art is another field and stays); the PC, a critter class, a destroyed object and an id with no file change nothing; a door or window and a container of the shown map take a new art too (ARC-SCRIPT-1778, ledger [AD1–AD5](../research/ledger/scripts.md#change-art-number-arc-script-1735-e5-a-tail): their open state is the frame of the art id, so the action also writes the session's frame 0 and the object is closed after it with its lock fields kept; the portal's frames and open frame are the new art's, `AreaPortals.Retarget`); a trap, unique NPC, a door or container that is not on the shown map, a starting-kit item, an item the session knows no origin or prototype of or an object with no loaded row refuses the run (`ArtPlan`); no installed run reaches a unique NPC (ARC-SCRIPT-1780, ledger AN16–AN17), so its body is not modelled.
- **Tests.** `ScrNpcStateTests` (the interpreter), `AreaNpcStateScriptTests` (the host over a real session), `AreaAiRouteTests` and `AreaAiHostScriptWalkTests` (the AI host's side), `TeleportMapStateTests` (the session's fields and
  their snapshot), `AreaNpcStateScriptAdmissionTests` (the sets and, with `DAT_ROOT`, the 270 installed scripts: S36 admits 208 whole).

## World-object commands (ARC-SCRIPT-1164, set S37)

`ScrAdmissionSet.S37` is S36 plus three conditions (`WorldObjectConditions`: obj is open 36, sector is blocked 45, monster generator disabled 46) and ten actions (`WorldObjectActions`: transfer item 41, set lock state 53, create item 66,
toggle open or closed 88, set written start 94, toggle sector blocked 106, toggle monster generator 110, touch art 123, destroy item named 126, toggle item inventory display 127); the interpreter runs at most S37. Ledger
[WO1–WO18](../research/ledger/scripts.md#world-object-commands-arc-script-1164-e5-b); plan slice E5-b, its tail ARC-SCRIPT-1279.

- **Object actions** hand one `ScrObjectStateRequest` each to the caller in action order (`CreateItem`, `TransferItem`, `SetLockState`, `ToggleOpenClosed`, `SetWrittenStart`, `DestroyItemNamed`, `ToggleItemInventoryDisplay`).
- **Sector and generator toggles** (106, 110) hand a `ScrWorldRequest` (`ScrWorldChange.SectorBlocked` or `GeneratorBlocked`) that carries the state the toggle ends in; **touch art (123)** reads its operand and hands nothing.
- **The run reads its own toggles back.** Conditions 36, 45 and 46 read the run's overlay first (the open state a toggle left, the sector list, the generator bits) and then the host through `IScrWorldObjectQueries`
  (`IsOpen`, `CanOpen`, `SectorBlocked`, `GeneratorBlocked`); a host without the interface refuses a run that holds them (`ScrRunOptions.Validate`).
- **The host.** `AreaUseScriptHost` holds the three conditions and all ten actions in its use, placed, heartbeat and dying sets (`WorldObjectHostActions`; ARC-SCRIPT-1164 held six of them, ARC-SCRIPT-1279 the other four). Lock state and the open toggle act on a portal or container of the shown map
  (`AreaScriptWorld.Openable` gives the lock identity, the T0 baseline and the ART facts): the lock bit through `SetOpenableLocked` (1 locks, 0 unlocks with the Lock event, any other value a plain flag clear), the toggle through the
  session's art frame. Create item goes through `CreateItemForHero` or `AddItemToNpc` with the viewer's insert check. The blocked sector list is the session's sector-scope map changes `map.blocked` (`SessionSectorBlock`,
  `GameSessionOwner.QuerySectorBlocked`, `SetSectorBlocked`; no schema step) and `AreaWorldMap.Passes` leaves a blocked sector out of the route; the generator toggle is `ToggleGeneratorBlock`.
- **The four item actions (ARC-SCRIPT-1279, ledger WO14–WO18).** Destroy item named (126) and transfer item (41) find the first item of the holder's list with the name (`GameSessionOwner.QueryNamedItem`, the finder 0x462480: the hero's kit, taken
  and made items, or an NPC's or container's list; any other object holds nothing) and destroy it through the one destroy path (`DestroyNamedItem`) or move it to the other holder (`TransferNamedItem`: NPC to hero is `TakeItemFromNpc`, container to hero
  `LootItem`, hero to NPC or container the gift of `GiveItemToNpc`). Set written start (94) and toggle item inventory display (127) write the session's item fields (`SetItemWrittenStart`, `ToggleItemDisplay`, `QueryItemFieldWrites`; saved in the
  inventory section as `itemFields`, schema 94) on the item row the operand names, and the reading window's facts take a written start over the prototype's (`AreaInventoryHost.WrittenFacts`). The host asks the session before anything applies
  (`QueryNamedItemDestroy`, `QueryNamedItemTransfer`) and leaves a run it cannot apply unapplied (the plan checks what the moves would refuse: scripts of the item and of the new holder, worn item spells, running spells, gold and keys, a container off the actor's map; a run naming one holder and item name twice is refused whole); the session's own budgets still decline a move as the original's room test does; limits in WO17.
- **Tests.** `ScrWorldObjectTests` (the interpreter), `AreaWorldObjectScriptTests` (the host over a real session), `SectorBlockedStateTests` (the session's list), `AreaWorldMapTests` (the route), `AreaWorldObjectScriptAdmissionTests`
  (the sets and, with `DAT_ROOT`, the 117 installed scripts: S37 admits 84 whole, the hosts' sets 39, 69, 35 and 77), `ScriptItemStateTests` (the session's finder, destroy, move and item fields with their snapshot).

## Get location and the Molochean agent's heartbeat (ARC-SCRIPT-1359, set S38)

`ScrAdmissionSet.S38` is S37 plus get location (95), which writes the first object's tile x and y into number operands 1 and 2 (32 bits each) through `IScrWorldQueries.Location`; an absent object or one without a place stops the run as distance
between (71) does, and a literal in operand 1 or 2 is refused. The interpreter runs at most S42 (S38 plus loop break and stop spell, see below). Ledger [GL1–GL2](../research/ledger/scripts.md#get-location-and-the-crash-site-molochean-agent-arc-script-1359). The
heartbeat set (`AreaUseScriptHost.HeartbeatAdmission`, and the dying set built on it) holds it: with global flag 2323 set, the Crash Site agent's heartbeat 1717 sets the day and night standpoints, reads where it stands and runs to its tile (run to 74 is in the set since
ARC-DEV-231) through the AI host's goal start, so it is admitted whole. Tests: `ScrHeartbeatWorldTests` (the action and the set), `AreaHeartbeatScriptTests` (a 1717-shaped script over the host and, with `DAT_ROOT`, the installed agent).
Not built: the use, tile, sector and talk sets (tile script 2080 also uses 95).

## Loop break (ARC-SCRIPT-1166, set S40)

`ScrAdmissionSet.S40` is S38 plus loop break (20), which ends the run's running loop at once: it clears the list, leaves the current looped object as it is and jumps to the line after the loop end that the empty-list search finds (`ScrAdmission.AfterLoopEnd`, the original's 0x44BC60: from the line after the loop for, the first line whose condition is "true" and whose true action is loop end, so the line after it; without one, the line after the end, which ends the run). Without a running loop it does nothing and the run goes on with the next line (the original logs an error). A new loop for can start after a break. The region admission treats a break like a loop end: its successors are the next line and the line after the loop end of any loop for of the script. Ledger [PG15](../research/ledger/scripts.md). The use, placed (tile and sector), heartbeat and dying host sets hold it beside loop for and loop end; the dialog host runs no loops. Of the 22 installed scripts that carry a loop break, S40 admits 12 whole, the placed and dying sets 7 each, the heartbeat set 1 and the use set none (the others hold commands of other slices; `AreaLoopBreakScriptAdmissionTests`, with `DAT_ROOT`). Tests: `ScrWorldTests` (the interpreter and the region), `AreaUseScriptHostTests` (a sector script's loop over the vicinity ends at the first marker). Call script at (55) stays refused: it occurs once, on a line no run reaches.

## Stop spell on object (ARC-SCRIPT-1294, set S42)

`ScrAdmissionSet.S42` is S40 plus stop spell on object (130, the original's 0x4593F0 called from the action dispatcher at 0x44A895). Operand 0 is read as a value (the spell number) and operand 1 as the first object of its list; with an object the interpreter hands one `ScrObjectStateRequest` (`ScrObjectStateChange.StopSpell`, the object and the spell) to the caller in action order, without one nothing happens (the original tests the handle), and the run goes on with the next line either way: the answer of 0x4593F0 is dropped. Ledger [RM5–RM6](../research/ledger/spell-runs.md). The placed (tile and sector), heartbeat and dying host sets hold it beside the cast actions; the use click's set and the dialog host's sets do not (they hold no cast actions either). The host applies it for the PC (the run rules' `pc`) and for an object of the shown map through `AreaScriptWorld.ScriptStopSpell`, which the viewer wires to `AreaAiHost.ScriptStopSpell`; that calls the cast host's `SessionSpellCaster.StopSpellOnObject` (the first active run of the spell in slot order that holds the object as target, in its run list or in its summoned list, is interrupted through the run rules, [cast host](../world/spell-cast-host.md#remaining-components-the-spell-stop-and-the-focus-wipe-arc-magic-1146)); a run with the action and no viewer answer is left unapplied (`stop spell needs the AI host`). `AreaUseScriptHost.LastStopSpell` shows the last answer (test and smoke seam). Limits: an object of another map is left alone (the cast host holds the shown map's runs); the object's script name is the cast host's id for the PC, NPCs, session entities and sector objects (`sector:S:N`), and the host translates it for a mobile row with no session entity (scenery, a mobile portal or trap: `oid:` and the digest in the script host, `row:Mobile:N` in the cast host) through `SessionSpellCaster.CastIdOf` and `AreaSpellCastMap.CastIdOfScriptEntity` (ARC-SCRIPT-1576); the script host names no spell-created scenery (a created critter has one id in both hosts), so none is a target here; the interrupt's own effects are the run rules'. Four installed scripts carry the action (1054, 2789, 2792, 3022; the census of live roots reaches one of them): S42 admits 3 whole, the placed and dying sets 1 each and the heartbeat and use sets none (the others hold commands of other slices). Tests: `ScrStopSpellTests` (the interpreter and the sets), `AreaHeartbeatScriptTests` (the host over the viewer's answer), `AreaStopSpellScriptAdmissionTests` (the sets and, with `DAT_ROOT`, the installed script).

## Vitals commands and the food use script (ARC-SCRIPT-1481, set S43)

`ScrAdmissionSet.S43` is S42 plus get hit points (107), get fatigue points (108) and heal poison (128). The two reads store an object's current points into number operand 1 and its maximum into operand 2 through `IScrVitalQueries` (an object the host cannot
answer ends the dispatch as not modelled with nothing applied; an absent object stops the run; a literal in either store is refused); heal poison asks the host once per object of its operand (`ScrObjectStateChange.HealPoison`, the amount in `Value`). The interpreter
runs at most S43. Ledger [VT1–VT8](../research/ledger/scripts.md#vitals-commands-and-the-food-use-script-arc-script-1481).

The area use host (`AreaUseScriptHost.Admission`, with the placed and dying sets built on it) holds the three commands and heal hit points (75), heal fatigue (76), the magick-tech adjustment (91), play script eye candy (120) and destroy (68), so the food use script
30282 is admitted whole. The host answers the PC from the session's life and an NPC of the shown map from `AreaScriptWorld.Vitals`; get stat 24 reads the same poison level (0 to 1000). Heal poison and the hero's heal fatigue go through `AreaScriptWorld.ScriptHealLife`
(the viewer's `HealLifeByScript`: the session's heal with the fatigue and poison amounts); a negative heal poison amount raises the level by its size (ARC-SCRIPT-1593: the life step is asked with the negative amount, the viewer writes the level plus the amount through `GameSessionOwner.RaisePoisonLevel`). Destroy of the **item in use** (`RunItemUse`) takes the food out of the hero's list through the session's used-item
destruction; destroy of any other non-NPC object stays refused. `RunItemUse(…, target: entity)` makes that object (the PC, or an NPC of the shown map) the extra object (the use's target, ledger IU2); the viewer passes the use's target. Tests: `ScrVitalTests` (the interpreter and the sets),
`AreaVitalScriptTests` (the host over a session, an NPC's vitals, the self-destroying item and, with `DAT_ROOT`, the installed 30282 admitted and a whole hero leaving the potion alone). Under S43 the census (`--script-refusal-census --set S43`) admits 1,575 attached scripts
on 6,856 placed sites (S38: 1,558 on 6,113). An NPC of the shown map that eats runs the script since ARC-SCRIPT-1590 ([ledger VT10](../research/ledger/scripts.md)). Script eye candy is played by the viewer since ARC-SCRIPT-1592 ([ledger VT9](../research/ledger/scripts.md)). A food the session made or the starting kit holds (no object row) runs its prototype's use script since ARC-SCRIPT-1594 ([ledger VT8](../research/ledger/scripts.md)): the slot from `QueryItemScriptSlot`, the magick-tech ratio from the prototype's complexity (`AreaUseScriptHost.PrototypeComplexity`), a written slot kept through `GameSessionOwner.StoreItemScriptSlot`.

## Newspaper commands (ARC-SCRIPT-1764, set S59)

`ScrAdmissionSet.S59` is S58 plus queue newspaper (85) and float newspaper headline (86); the interpreter runs at most S59 (S60 since ARC-SCRIPT-2041, S61 since ARC-SCRIPT-2319, S62 since ARC-SCRIPT-2322, S63 since ARC-SCRIPT-2326, S64 since ARC-SCRIPT-2321, S65 since ARC-SCRIPT-2320: plus the focus kinds every follower and any follower, answered through `IScrGroupQueries` like the group kinds). Both hand `ScrNewspaperRequest` effects (`ScrEffects.Newspapers`, in action order) to the host and the run goes on.
Queue newspaper reads its two operands as numbers (the issue, and the front flag by its value, 0 or not) and the host writes the session's queue by the rule of `GameSessionOwner.QueueNewspaper` (the original's 0x4BF140, the same rule the dialog code `np` uses).
Float newspaper headline takes its object from operand slot **1** (census anomaly X9: the editor's signature puts it in slot 0, the interpreter reads slot 1 and never reads slot 0): the admission checks slot 1 as an object and leaves slot 0 alone, the interpreter requests the headline float over the object
(`OptionalObject`, a group's first), and nothing for an absent object. The host reads the head issue's headline (`WrittenTexts.Headline`, 0x56CCA0), punctuates it as the vendor's headline line does (`WrittenTexts.PunctuatedHeadline`: a stop unless it ends in `.`, `?` or `!`, then a space)
and floats it through its float list (`TakeFloats`, with the float line's `AreaScriptFloat`); an empty headline (no newspaper file, an issue with no message) floats nothing.

Hosts: the dialog host (`AreaScriptHost.Admission` and the three sets built on it) and the use host (`AreaUseScriptHost.Admission`, with the placed, dying and combat-point sets built on it, and `HeartbeatAdmission`) apply both through `AreaScriptNewspaper` (a queue request the session refuses for a limit stops the
later steps of the apply, like the other session requests). The thrown-item host, the item script host and the slide scripts stay outside (their sets are their own and no installed script of them holds a newspaper action). Tests: `ScrNewspaperTests` (the interpreter and the admission, including the slot-1 reading),
`AreaScriptNewspaperTests` (the dialog host over a session and a content stack with a newspaper file) and `AreaUseScriptHostTests` (a tile script queues and floats). Ledger [NP9–NP12](../research/ledger/scripts.md#script-actions-queue-newspaper-and-float-newspaper-headline-arc-script-1764); census numbers in
[the refusal census](../research/script-refusal-census.md).

## Checks

- `tests/Arcanum.Scripting.Tests/Scr/ScrDispatcherTests.cs`: every step of the
  decision order, every end of a run with and without auto-removal, remove and
  change, writeback only on change, fresh locals, trap object destruction, X7,
  X8 and X10 in both modes, the own-write overlay, story state, computed-index
  stops, tile and sector writeback per mode, refusal, invalid events and
  options, the trace, the profile.
- `tests/Arcanum.Session.Tests/RuleModeTests.cs`: `GameRules.Scripts` per mode.
- `tests/Arcanum.Scripting.Tests/Scr/ScrObjectStateTests.cs` (ARC-WORLD-114): toggle state and kill on the pre-made heroes' script
  shape (chosen: off, script removed, skip default; else kill, run default), each object of the operand in action order and none for an
  absent one, S16 admits both and S15 neither; `tests/Arcanum.TerrainViewer.Tests/AreaHeartbeatScriptTests.cs`: the viewer's heartbeat
  host (kill through the world, the off map change and the gone set, the removed slot, a second toggle back on, the refusals); opt-in,
  `DAT_ROOT`: the installed Crash Site's twelve scripts kill eleven and switch the chosen hero's NPC off.
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
  text rules (missing line, alternate text, tokens, action field, generated text; ARC-SCRIPT-050: a separators-only action field).
- `tests/Arcanum.Scripting.Tests/Scr/ScrTalkCommandTests.cs` (ARC-SCRIPT-050): S19 over S18; float line over one object and over a list
  with the PC around the triggerer, nothing without a PC or a triggerer, an absent object floating nothing; get area storing the host's
  area, a host without an answer or an absent object stopping the run; sets with either action refused for a host that cannot answer,
  and a set wider than S19 refused. Viewer: `AreaTalkQueryTests` (the talk set through the use host, the floated text and the
  separators-only action field, get area 0 off the start map and stopping on it, a floated action field leaving the run unapplied, the
  set widening only for a use host of the talk's map); `MapTransitionsTests` (the map list's area, ML1); opt-in
  `AreaMapConversationOriginalTests.Every_dialog_npc_of_every_interior_starts_with_the_talk_queries` and `ScriptTriggerCensusTests`.
- `tests/Arcanum.Scripting.Tests/Scr/ScrStoreTests.cs` (ARC-DEV-100): global variables read from the host, overlaid, unmasked and handed back changed only;
  conditions over them; the hero's variable and flag by index through the player or the triggerer; a non-PC object reading 0 and ignoring a write; the
  flag spill under Original and the single flag under Steamwoven; an absent object stopping the run; a called script sharing the stores; S21 over S20,
  the host check and the focus kind of a PC operand in admission. Session: `ScriptStoreApplyTests` (the writes of a real run applied in order, a session
  without a hero ignoring the PC's). Viewer: `AreaScriptStoreTests` (a heartbeat writing and reading them through the use host, saved and loaded; opt-in:
  the installed heartbeat 1015 admitted).
- `tests/Arcanum.Scripting.Tests/Scr/ScrTileActorTests.cs` (ARC-SCRIPT-052): S20 over S19; destroy and stop attacking asking for the first
  object with the run going on, an absent object asking nothing, a tile's call-ex of the stepping NPC's dialog point asking for his talk.
  Viewer: `AreaUseScriptHostTests.A_tile_stops_the_npc_who_steps_on_it_attacking_and_destroys_him` (the session's stop and the host's
  part, the destroyed NPC gone, a second destroy changing nothing, the PC and an NPC with a destroy script refused); opt-in
  `AreaUseScriptsOriginalTests.Virgil_stepping_onto_the_altar_tile_asks_for_his_altar_conversation` and the tile census. Native:
  `./arcanum smoke pr --stages altar --sector 1452,1291` (Virgil joins, follows the PC across an altar tile, his talk opens at node 250 of
  DLG 1324 as a resumable dialog-point talk; the window holds his home and the altar).
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

- The session holds the global variables (2000) and the hero's flags and variables since ARC-DEV-098 (schema 67, `scripts` section), and the viewer's dialog and
  use hosts read and write them since ARC-DEV-100 (set S21); item events and the DLG conditions and actions over PC flags and variables do not
  ([script stores](#script-stores-arc-dev-100-set-s21)). The story-state number is the session's since ARC-SCRIPT-019.
- Tile and sector writebacks name no location; the caller applies them to the
  tile or sector it dispatched.
- The AREA viewer's talk, resumption and deferred calls run the dispatcher (its set "S3-area-sound-calls-scheme-schematic-reading-story": S3,
  plus the sound actions, call script in / remove script call, set auto-level scheme, start schematic UI, the reading actions and the story
  actions; with a session hero also get skill, ARC-SKILLS-009); since ARC-SCRIPT-015 map mode's use
  clicks on guarded use sites run it under "S4-area-use-reading-objects-story" (since ARC-WORLD-081 "S4-area-use-reading-objects-story-world"), and since ARC-SCRIPT-017 the tile and sector scripts the player
  enters, and since ARC-SCRIPT-020 the use script of a written item the PC reads (`AreaUseScriptHost.RunItemUse`: attachment point 1,
  the PC as the user, no target, the item's inventory row; skip default stops the reading, a teleport is refused); since ARC-WORLD-114
  an NPC's first-heartbeat and heartbeat scripts from the AI host (set "S2-area-heartbeat-state-kill", since ARC-SCRIPT-047
  "S2-area-heartbeat-state-kill-talk", since ARC-SCRIPT-049 "S2-area-heartbeat-state-kill-talk-world"); combat is a later slice. Which points the original fires and which have no host here: the
  [script trigger audit](../research/script-trigger-audit.md).
- Not in any admitted set yet: focus groups other than the vicinity's scenery and critters (conditions over a list kind in neither), reactions (loop break since ARC-SCRIPT-1166, stop spell since ARC-SCRIPT-1294) and most other world
  actions; create object makes items only (ARC-WORLD-081).
  Admitted since S4: teleport and fade-and-teleport (S4), set auto-level scheme (S8), start schematic UI (S9), the reading
  actions (S10), the skill commands (S11), call script, assign object and get object type (S12), set PC quest state, mark map
  location, set rumor and add or remove reputation (S13), the item-effect commands and call-ex (S14, the session's item events only), random,
  loop for and loop end over the vicinity's scenery and create object (S15, ARC-WORLD-081), toggle state and kill (S16, ARC-WORLD-114;
  the viewer applies them to an NPC of the shown map only), the critter vicinity and call-ex of the dialog point (S17, ARC-SCRIPT-047),
  obj is dead, obj is switched off, distance between and set faction (S18, ARC-SCRIPT-049; the viewer's heartbeat set and talks),
  float line and get area (S19, ARC-SCRIPT-050; the viewer's talks only, a floated line's action field not run, get area on the start map
  with area 0 by the object's location since ARC-DEV-178), get reaction (S23, ARC-DEV-178, below), unfog town map (S24, ARC-DEV-205, below; the town-map window and its per-cell fog not modelled), destroy and stop attacking (S20, ARC-SCRIPT-052; the viewer's tile and sector set, a destroy of the PC, a
  follower or an NPC with a destroy script not modelled).
- Trigger points no host runs, with the census of the installed attachments (ARC-SCRIPT-2825; the [slot census](../research/scr-census.md) over every sector, mobile and prototype, not the shown windows; [conditions TQ11, TQ16, TQ21, TQ25](../research/script-trigger-conditions.md)): unlock (3, a portal's or container's open attempt before the lock test) has 1 attachment (a door of the main map, script 1151; the door answers `ScriptGuarded`, a container with an unlock script `UnlockScriptUnsupported`, [#2860](https://github.com/Simarglok/arcanum-steamwoven/issues/2860)); buy object (17, the sale check on every price with the PC as seller) has **0** attachments, so it is not modelled and nothing is lost; leader killing (20, each follower of the killer's PC leader when an NPC dies) has 1 (script 1014 of an NPC of the main map, [#2859](https://github.com/Simarglok/arcanum-steamwoven/issues/2859)); caught thief (33, the victim as triggerer before point 11 at a failed steal or plant) has 10 (scripts 2931, 2840, 2839 on NPC thieves of the main map, [#2858](https://github.com/Simarglok/arcanum-steamwoven/issues/2858)); the PC thief has no script. The viewer's own census (`ScriptTriggerCensusTests`) counts only the shown windows of the 79 maps, so it sees none of the three on the main map; recount with the probe's `--script-census` before relying on these numbers.
- The viewer's dialog host models only the dialog slot of the map's speakers;
  generators, traps, death, surrender, mind control and the jilted flag answer
  "no", and the generic greeting is a floated line. Its use-script host (ARC-SCRIPT-015) models the use slot of any
  row of the shown map, mind control from the row's spell flags and the jilted flag from the session; generators and
  traps answer "no" there too.
- The six cast actions (48, 61, 118, 121, 122, 136; set S29, ARC-DEV-241) hand one cast request each to the host (the target, the caster or none, the spell, the free 0x2 and unresistable 0x4 flags) and the run goes on. The area script host
  applies it to a target that is an NPC of the shown map or the PC, through the AI host's script cast (the cast host's script entry point; an NPC caster plays its casting goal; ARC-DEV-244: a cast whose caster is the PC plays the hero's casting goal in the viewer, queued under a playing cast as a click's is and silent when the invocation check refuses it).
  ARC-SCRIPT-1168: the dialog host's talk sets hold the six cast actions too (only while the shown map's use host answers) and hand each cast request, in action order, to the use host's same path (`AreaUseScriptHost.ApplyScriptCast`), so a
  talk script's cast (the PC or the speaker as caster, the PC or an NPC of the shown map as target) starts the goal as the dialog opens; a world with no AI host refuses the run whole before anything applies (ledger [CA1–CA5](../research/ledger/scripts.md#cast-actions-in-the-dialog-host-arc-script-1168-e4-g-rest)).
- Damage (60), apply unresistable damage (138) and heal hit points (75) (set S30, ARC-DEV-243) hand one request per object of their operand (amount, damage type 0..4, the unresistable flag) to the host and the run goes on. The area script host applies them to the PC and
  to an NPC of the shown map through the cast host's damage step (no attacker; resistance unless 138; no armour, difficulty or critical step) and the session's heal (no resurrection, no get-up); any other object is left as it is (not ready).
  ARC-SCRIPT-1563: the dialog host admits the three actions too (its talk set, while the shown map's use host answers) and hands them, in action order, to the use host's same path (`AreaUseScriptHost.ApplyScriptDamage` and `ApplyScriptHeal`), so a ARC-SCRIPT-1598: a lethal damage of the same run ends the talk silently (the start test is asked again after the run's effects) and the speaker's slot write is placed before any effect applies (ledger [CP7](../research/ledger/combat.md#poison-sources-arc-res-1329)).
  talk's poison script (the installed 1059) and a deferred call's damage apply before the dialog opens; a world that cannot apply them refuses the run whole (ledger [CP6](../research/ledger/combat.md#combat-point-scripts-arc-script-1483)).
- The spell conditions obj has body spell (14), obj is influenced by spell (35), knows spell (48) and mastered spell college (49), and the action give spell mastery in college (112) (set S31, ARC-MAGIC-029, MT21a, ledger
  [SC1–SC6](../research/ledger/scripts.md#spell-conditions-and-the-spell-mastery-action-arc-magic-029-mt21a)): the conditions ask the host through `IScrSpellQueries` (the spell flags word, whether a spell run influences the object, whether the object
  knows the spell, the college it mastered) and take the list rule of SG6 over the operand's objects; condition 14 tests the four body bits 0x3C of the spell flags, 48 and 35 take the spell of operand 1 as read, 49 compares the mastered college with
  operand 1 (−1 matches no mastery). The action hands the first object of operand 0 and the college as read to the host as one `GiveSpellMastery` request and the run goes on; the host's rule (a PC or NPC with no mastery and five ranks) decides. A host
  that implements none of these keeps running S30. ARC-MAGIC-030 (MT21b, ledger [SC7–SC9](../research/ledger/scripts.md#spell-conditions-and-the-spell-mastery-action-arc-magic-029-mt21a)): the use host answers them (spell flags of the row and the session's written flags, the session's spell runs, the hero's or an NPC's spell book) and writes the mastery through the session (own branch of the request chains; the PC and an NPC of the shown map), its use, placed, heartbeat and dying sets and the talk host's query sets hold the five commands; the body-of-element bits are written by no spell component yet. 46 installed scripts use one of the five; only a few are admitted whole, the rest stop on other commands first.
- Use ammunition units (135; set S32, ARC-MAGIC-041, ledger [TI8, TI11](../research/ledger/technology.md)): operand 0 is the count and operand 1 the object; the run hands one `ScrAmmoUse` (the object, the count, ammunition type 2 = power cells) to the host and goes on, as the original ignores the unit use's answer (0x4658E0). A count of 0, a negative count and an absent object ask for nothing. The area script host spends the cells through the session (`SpendAmmo`) before it applies the run's deferred calls; no cell, and an object the session holds no ammunition for, spend nothing and the run goes on. Only the powered shield's script (29016, line 4) uses it.
- Deferred item calls (ARC-SCRIPT-1482, ledger [PZ11](../research/ledger/combat.md#poison-sources-arc-res-1329)): `AreaScriptHost.CallAdmissionFor` also holds the magick-tech adjustment (91, the host's `PowerRatio` is a worn item's ratio for the PC found by its entity, any other item or owner stops the run), play script eye candy (120) and, since ARC-SCRIPT-1586, get hit points and get fatigue points (107, 108: the host's `HitPoints` and `FatiguePoints` answer the PC from the session's life and an NPC through the shown map's use host, anything else stops the run as not modelled); the damage (60, 138) and heal (75) of the wield-on poison scripts come from the talk set (ARC-SCRIPT-1563). The wield-on poisoning items' scripts use them (poison sources S8).
- Fade (103; set S35, ARC-SCRIPT-1241, [movies study](../research/bink-movies.md) MV5): four number operands, seconds, sound, movie and the flag that picks how the screen comes back. The run hands one `ScrFade` with the operands as read to the host and goes on (the original plays the movie inside the action and goes on after it). The dialog host and the use host apply it (`AreaScriptFades`): the game clock jumps by seconds × 1000 ms when the seconds are above 0 (0x45C200, the next ping sees the jump, TL26), then a movie number other than 0 goes to the viewer with the sound operand as its audio track (when above 0; 0 is the default track, `AreaScriptFade`, `TakeFades`) and, with no movie, a sound above 0 plays as an effect sound (dialog host only; a use click plays none). The screen's fade out and in and the flag's later fade-in are not drawn (the viewer cuts to the movie and back; the flag is carried, not used). The whole action is skipped in a network host game; a single-player host is never one. Which installed scripts reach it (2862, 2863 and 3061 need end-game slides, 132, first; Kerghan's dialog 1105 needs change script, 99; the two small scripts 1093 and 1109 are called by dialog scripts through call script, which the dialog host leaves out): [movies study](../research/bink-movies.md) section 7.
- Use item with skill (79) is a request only: no host runs the goal, the check or the skill's handler, and no NPC source is modelled
  (SX3, SX4; no live installed script uses it). Try to steal 100 coins (115) waits for pick pocket (task S15).
- X2 (an uninitialised attachee type without attachee) is reachable only through
  call-script and is left to the slice that admits calls.
