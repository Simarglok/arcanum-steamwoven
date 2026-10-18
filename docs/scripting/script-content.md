# Script and dialog content (ARC-SCRIPT-011)

Scope: how scripts and dialogs are found and read by script number for **any map
and any module**: one universal loader over the game's
[content stack](../research/content-precedence.md), like the world catalogue and
the whole-map area plans, instead of archives pinned for one location. Status:
Data API and the AREA viewer's conversation host. Godot-independent.

## Rules (source facts)

- **Names.** The original maps each script number to a file name from the sorted,
  de-duplicated listing of `scr\*.scr` across the mounted sources; the number is
  what C `atoi` reads from the name, and a later name with the same number
  replaces an earlier one (the game prints an error). Numbers from 1000 up are
  listed at game start, before the module is mounted, so only names held by
  `tig.dat`, the game folder, `arcanum*.dat` or `data/` count for them; 1..999 are
  listed after the module is mounted, over every source. Sources and executable
  addresses: [content precedence, lookup rule](../research/content-precedence.md#lookup-rule-source-facts-and-derivation).
- **Dialog.** A script's DLG is its name with the `scr` folder and extension
  swapped for `dlg` (`scr\01324Virgil.scr` → `dlg\01324Virgil.dlg`). It is not
  looked up by number: a DLG file whose name differs from the script's is never
  opened for that script.
- **Copy.** Both paths are then opened through the stack: the first source in
  search order that holds the path wins.

## API (`Arcanum.Data.Scripts`)

- `ScriptNameIndex.Build(ContentStack)`: the number table. `Find(number)` gives a
  `ScriptName` (number, script path, dialog path as stack keys, and how many
  earlier names it replaced) or null. Counters: `ListedNames`, `ReplacedNames`,
  `UnnumberedNames` (no number above 0, or more than nine digits: the original's
  overflow is not modelled) and `ModuleOnlySystemNames` (≥ 1000 held only by
  module sources: the original never lists them). `Number(file)` is the `atoi`
  reading; `IsBase(role)` names the sources mounted before game start.
- `ScriptContent.Open(plan)` opens and owns a stack; `ScriptContent.Over(stack)`
  borrows one. `Script(number)` returns `ScriptRead` (the strictly framed
  `ScrProgram`), `Dialog(number)` returns `DialogRead` (the `DlgDocument`), each
  with a status: `Read`, `NoName`, `NotFound` (no source holds the DLG),
  `Unreadable` (stack read limits), `Invalid` (not a script, or a DLG the reader
  refuses), `Changed` or `Empty` (ARC-SCRIPT-012: a DLG with no record the
  original keeps, which `dialog_load` 0x412E10 refuses to open). Read details name
  statuses or virtual paths, never absolute paths or payload bytes; an open
  failure's detail is the stack's (it names the failing source path), which the
  viewer does not print.
- **Dialogs as the game reads them (ARC-SCRIPT-012).** Dialogs are read in the
  [original read mode](../formats/dlg-reader.md#original-read-mode-arc-data-017)
  (`ScriptContent.OriginalDialogs`) unless the caller passes other reader
  options: bytes between fields are skipped, numbers read like `atoi`, and a read
  that stops early keeps the records before the stop. The strict reader stays
  available on request, as a data-quality check. A caller that passes its own
  `DlgReaderOptions` (for other limits) gets the strict mode unless it sets
  `Mode = DlgReadMode.Original`.
- **One version per path.** The stack indexes names once; every read goes to the
  winning copy again. The first bytes read for a path are kept as SHA-256, and a
  later read with other bytes is `Changed` and sets `ScriptContent.Changed` for
  good, so one content object never mixes two versions of a file. `VerifySeen()`
  reads every path read so far again and compares it, so a caller catches a file
  changed since an earlier run before it runs anything, not only when it reads
  that file again.

## AREA viewer

- `AreaContentRoot` finds the game folder from the module archive the area is
  read from (`<game>/modules/<Name>.dat`, the installed layout; the mounted stack
  must hold that same file as its module archive). It serves the hover names
  (ARC-UI-005) and the scripts (`OpenScripts`). The viewer takes no new argument,
  so the launcher roles are unchanged.
- `AreaConversationHost` takes the `ScriptContent` (borrowed; null when it could
  not be opened, then every talk is `ContentUnavailable` with the load's reason)
  instead of a script and a dialog archive. `AreaScriptHost.Script` reads the
  dispatcher's scripts through it; `LoadDialog` reads the DLG by the script's name.
  `SourceChanged` now means a script or dialog file changed after its first read:
  before a run every file read before is checked again (`VerifySeen`), after the
  run the scripts it read, and the DLG of a dialog action is read before any
  effect is applied, so a changed file applies nothing. A DLG that is missing or
  not readable still leaves the script's effects applied, as before.
- The Godot viewer opens the content once per module archive (its path and the
  area's module digest) at the first publication (about 0.2–0.35 s on the main
  thread, measured on the owner's Mac) and keeps it for reloads, the cave and
  quick loads; a content that saw a file change is opened again at the next
  publication. Base archives replaced on disk while the viewer runs are seen only
  when the module changes or a file change was caught. Map mode (`--area-map`)
  reads its dialogs through it too since ARC-WORLD-032
  ([map mode](../rendering/area-map-mode.md#dialogs-arc-world-032)).

## Coverage on the installed original

Opt-in tests (category `RealData`, `DAT_ROOT` = the game folder, module Arcanum):
[`ScriptContentRealDataTests`](../../tests/Arcanum.Data.Tests/Scripts/ScriptContentRealDataTests.cs).

| Fact | Count |
| --- | ---: |
| `scr\*.scr` names listed | 2,399 |
| numbers with a name / names replaced / unnumbered / module-only ≥ 1000 | 2,399 / 0 / 0 / 0 |
| scripts read and framed | 2,399 |
| winning script copy from a module source | 3 |
| dialogs read / empty (no record the game keeps) / no DLG file | 697 / 4 / 1,698 |
| the same dialogs through the strict reader on request: read / refused | 663 / 38 |
| script dialog paths with more than one copy (patched) | 47 |
| DLG files in `dlg\` / not the dialog path of their number's script (another name, or no script) | 709 / 8 |

Virgil (script 1324) now reads `Arcanum4.dat`'s copy of `scr\01324Virgil.scr`
(49 lines: `arcanum2.dat`'s 47 lines unchanged and two added), and
`dlg\01324Virgil.dlg` has one copy, in `arcanum3.dat`. His talk is unchanged:
line 4 → node 1, the return resumes at line 5, following → line 6 → node 385
(`AreaConversationTests`, `ARC_AREA_CONVERSATION_GAME_ROOT`), and the native
AREA smoke passes over the stack.

## Limits and NOT READY

- The stack is a snapshot at open: files added later are not listed, and the
  original's sticky first-open flag is not modelled ([limits](../research/content-precedence.md#limits-and-not-ready)).
- The listing's wildcard is modelled as "a direct child of `scr\` ending in
  `.scr`"; Windows short-name matching and directory entries named `*.scr` are
  not modelled.
- The original read mode's two divergences stay ([DLG reader](../formats/dlg-reader.md#original-read-mode-arc-data-017)):
  fields over 4 KiB and NUL bytes are refused, not cut. Field text keeps CR LF as
  stored; a consumer that shows it must turn it into LF, as the game's text-mode
  read does.
- In three cases (six installed DLGs) the game itself loses content (data bugs);
  `Dialog(number, rules)` fixes them under the Steamwoven rules ([data fixes](../formats/data-fixes.md),
  ARC-DATA-022) and reads them as the game does without rules or under Original: see [the analysis of the 38 strict refusals](../formats/dlg-reader.md#the-38-strict-refusals-arc-script-012).
- Item, trap and sector scripts read through the same API, but the viewer binds
  only the dialog point of a map's speakers ([dispatcher](scr-dispatcher-runtime.md)).

## Tests

- [`ScriptContentTests`](../../tests/Arcanum.Data.Tests/Scripts/ScriptContentTests.cs):
  `atoi` reading, listing order and replacement, the game-start/module split,
  module copies winning, dialog by name, typed bad bytes, change detection,
  borrowed and owned stacks.
- [`AreaScriptHostTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaScriptHostTests.cs):
  the viewer root (module outside a game folder, missing module), scripts and
  dialogs through the stack, `SourceChanged` and `ContentUnavailable`.
- Real data as above; the Godot self-test accepts `ContentUnavailable` for its
  synthetic area, whose module is not inside a game folder.
