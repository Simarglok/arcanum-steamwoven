# Generated dialog files

Status: **Implemented bounded component**, ARC-DATA-042 (2026-10-21). `Arcanum.Data.Messages.GeneratedDialogFiles` reads a
module's generated-dialog files, `mes/gd_*.mes`, through the [content stack](../research/content-precedence.md) as the
original's dialog module names and opens them, and offers the original's run count and entry fetch for a line pick. It is
Godot-independent and read-only. Requirement: [FR-DIALOG-003](../requirements/story-social-ai.md#fr-dialog-003) (the files;
no line is built yet). The consumer, the NPC greeting of the dialog, is ARC-WORLD-057 ([Not ready](#not-ready)).

## Evidence

The retail 1.0.7.4 `Arcanum.exe`, read statically by two independent readers that saw only the questions
([ledger "Generated dialog files"](../research/original-verification-ledger.md#generated-dialog-files-arc-data-042), GD1–GD5,
anomaly GD-A1); they agree on every row. Local data: the opt-in census below (counts only).

## Files

`GeneratedDialogFile` names the 32 files in the original's handle order (GD1). In each name the first letter is the speaker's
gender and the second the listener's; "pc" files are spoken by the PC to an NPC of the named gender. The gender pairs always
come in the order m2m, m2f, f2f, f2m.

| Handles | Files | Used by (original) |
| --- | --- | --- |
| 0, 1 | `gd_pc2m`, `gd_pc2f` | PC options (dumb PC: 16, 17) |
| 2–5 | `gd_cls_*` | NPC lines by social class (dumb NPC: 20–23) |
| 6, 7 | `gd_cls_pc2m`, `gd_cls_pc2f` | PC options by the NPC's social class (dumb PC: 18, 19) |
| 8–11 | `gd_rce_*` | NPC lines by race (dumb NPC: 24–27) |
| 12–15 | `gd_npc_*` | plain NPC lines (no dumb files) |
| 16–27 | the `dumb` variants above | |
| 28–31 | `gd_sto_*` | story lines (not read by this slice's questions) |

## API

| API | Behaviour |
| --- | --- |
| `GeneratedDialogFiles.Load(ContentStack, MesReaderOptions?)` | 0x418250: every file from the stack's winner at module load (`ContentLoadPhase.ModuleLoaded`). The original opens a file on first use and exits the program when one is missing (GD2); here a missing file stays absent (`Absent`), and a file that cannot be read or parsed as MES refuses the whole load (`ReadFailed`, `ParseFailed` with the path): project policy. |
| `GeneratedDialogFiles.Create(files)` | The set from 32 parsed files in handle order (null = absent), for synthetic data or another loader. |
| `NpcLineFile(group, npcMale, pcMale, npcDumb)` | GD5: the file an NPC line of the class, race or NPC group reads, by the NPC's gender, then the PC's; the dumb files for a dumb NPC (class and race only). |
| `RunLength(file, first, last)` | GD3 (0x4D4550, `MesFile.RunLength`): 0 without an entry numbered exactly `first`, else the stored entries from it numbered ≤ `last`, gaps not seen; 0 for an absent file. |
| `Line(file, number)` | GD4 (0x4D43A0): the entry's message (raw bytes, CR LF read as LF), or a missing result (`Message` null; `FileAbsent` when the file is absent). The original shows its "Error! Missing line" text instead; what is shown is the caller's. |

The lines are raw: name substitution (`@pcname@`, `@npcname@`), the random pick and the text decoding are the caller's. The
pick the original makes (GD5) is the first number plus a uniform random number from 0 to the run length less one, so a block
with a gap can pick a missing number (anomaly GD-A1); how the greeting treats it is decided in ARC-WORLD-057.

## Census

Installed original (`DAT_ROOT`, opt-in `GeneratedDialogFilesTests` RealData): all 32 files present, each from the base
archives; entries in handle order 281, 284, 4,116 ×2, 4,144 ×2, 420, 386, 846 ×4, 733 ×4, 62, 58, 33 ×2, 22 ×4, 6 ×4,
275 ×4. Gaps (counted in aligned 50-number blocks): 2 of 242 blocks in each `gd_cls_*` file, 1 of 33 in `gd_cls_pc2f`, 10 of 68
in each `gd_npc_*` file; none elsewhere.

## Tests

`Arcanum.Data.Tests/Messages/GeneratedDialogFilesTests`: the names and order, the NPC-line file by genders and dumbness, the
run length (gap, missing start, end below start), lines (found, missing, absent file), the load (module precedence, a missing
file absent, a parse refusal); opt-in census.

## Not ready

- No line is built: the greeting generators (ranges, social-class and race offsets, the NPC's override lines), the dialog
  markers `g:`, `i:`, `m:`, `r:` and name substitution of generated lines are ARC-WORLD-057 (greeting) or later slices.
- The PC option files (`gd_pc*`, `gd_cls_pc*` and their dumb variants) and the story files (`gd_sto_*`) are loaded but have
  no consumer.
- Text decoding and localisations other than the declared MES byte profiles.
