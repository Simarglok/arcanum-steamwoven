# Game data fixes (ARC-DATA-022)

Scope: how Steamwoven fixes defects in the installed game data that make the
retail 1.0.7.4 game lose content, without editing the player's files and without
storing game text in this repository. Status: mechanism for DLG files and the
three fixes found by ARC-SCRIPT-012. Godot-independent (`Arcanum.Data.Fixes`).

Owner decision (2026-10-17, option B of the
[analysis of the 38 strict DLG refusals](dlg-reader.md#the-38-strict-refusals-arc-script-012)):
the Steamwoven [rule mode](../world/rule-modes.md) fixes these data defects (D2),
the Original mode keeps them, and the fix mechanism serves later data fixes too.

## Mechanism

- **A fix names an exact copy.** `DlgDataFix` holds the ledger row whose switch
  enables it, the content-stack key of the file, the SHA-256 of the copy it was
  written for, an operation and, for operations on one record, the record id.
  A fix applies only when the path and the digest of the file as read both
  match, so a changed file, a patch or a mod that replaces the file is never
  edited. `DlgDataFixCatalogue` validates its entries (normalized key, lower-case
  digest, record exactly when the operation edits one, no repeats);
  `DlgDataFixCatalogue.Installed` is the catalogue for the installed game.
- **Operations name fields, never carry text.** `CompleteLastRecord` appends an
  empty field to a file whose only record lacks its effects field.
  `MoveResponseToActions` moves a record's response field, when it holds text
  that is not a plain number, to the end of its effects field (after `, ` when
  the effects field is not empty) and leaves the response empty. Each operation
  first checks that the file has the defect it expects and that the edit keeps
  every record; otherwise the attempt is `Refused` and the bytes stay as they
  were. The fixes of one file are matched against its unedited digest and
  applied in catalogue order.
- **Rule modes.** `DataFixProfile` (Arcanum.Data) is the data domain's profile in
  `GameRules.Data`: one boolean per ledger row, named after the original's
  behaviour, `true` in Original. `Applies(ledgerId)` says whether a row's fixes
  apply.
- **Where it applies.** `ScriptContent.Dialog(number, rules)` applies the fixes
  of the given profile after reading the winning copy; without a profile it reads
  the file as the retail game does. Change detection (`Changed`, `VerifySeen`)
  stays on the stored bytes. `DialogRead.Fixes` lists every attempt. The AREA
  conversation passes the session's current `Rules.Data` at each dialog read
  (rule 1 of the rule modes: read at the decision).

## Fixes

| Ledger | Switch (Original `true`) | Files | Original behaviour | Steamwoven |
| --- | --- | --- | --- | --- |
| DF1 | `StoneExamineTextLost` | `dlg/03042blue_stone_exa.dlg`, `dlg/03043red_stone_exa.dlg`, `dlg/03044green_stone_exa.dlg`, `dlg/03045yellow_stone_exa.dlg` (`CompleteLastRecord`) | The only record lacks its effects field, `dialog_load` opens nothing, and examining the stone (print line) shows nothing. | The record is read with an empty effects field and the print line shows its text. |
| DF2 | `NpcLineEffectsInResponseLost` | `dlg/01306benjamin_gershwin.dlg` record 315 (`MoveResponseToActions`) | The NPC line's effects (give item 5629, quest experience of level 2, local flag 6) are written in its response field, which is never read for an NPC line, so nothing happens. | The effects apply when the line is shown. |
| DF3 | `ReplyFinalLineInResponseLost` | `dlg/01009mayor_blackroot.dlg` record 361 (`MoveResponseToActions`) | A low-intelligence reply holds its final-line action (`fl 370`) in the response field; `atoi` reads 0 and the reply ends the talk after its reaction change. | The final-line action follows the reaction change, as in the other 1,197 replies whose effects end with `fl N` and whose response is empty. |

The digests are in the catalogue; every file has one copy in the installed game.
`01957game_of_witt` (IQ fields `N, -N`) is not a fix: the game reads the first
number and the second one's meaning is unknown.

## Coverage

- [`DataFixTests`](../../tests/Arcanum.Data.Tests/Fixes/DataFixTests.cs)
  (synthetic): profiles, exact-copy matching (other copy, other path, Original),
  both operations (empty and non-empty effects field, refusal on a plain number
  and a missing record), catalogue validation, `ScriptContent` with and without
  rules and change detection on the stored file.
- [`DataFixRealDataTests`](../../tests/Arcanum.Data.Tests/Fixes/DataFixRealDataTests.cs)
  (`RealData`, `DAT_ROOT`): every installed fix matches its file and applies in
  Steamwoven only; DF1 turns an empty file into one record, DF2 and DF3 keep
  every record and move exactly the response bytes. Structure only.

## Limits and NOT READY

- Only DLG files; other formats get operations when a defect needs one.
- The viewer runs neither print line nor float line (dispatcher set S3), and its
  dialog grammar has no experience action, so DF1 and DF2 show their effect only
  once those land; DF3's NPC is not on the Crash Site.
- A fix keyed to a digest does nothing on another release or a modified file;
  such a copy needs its own entry.
