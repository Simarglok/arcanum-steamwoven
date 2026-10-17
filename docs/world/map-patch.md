# Map patch

## Scope

ARC-DATA-028. `Rules\map.patch` is a module rule file whose lines change named objects of
a map when it opens. The original applies it on every map open, after the map's mobile
objects (and a saved game's differences) are loaded and before any sector, so it changes
mobile objects only, and it overrides restored save state. Steamwoven applies it in the
area owner of every whole-map plan ([whole-map plans](whole-map-plans.md)), so the patched
object is what every consumer reads: visuals, step evidence, inventory, names, combat
fields and dialog attachments. Behaviour checked in the 1.0.7.4 executable by two readers:
[ledger "Map patch"](../research/original-verification-ledger.md#map-patch-arc-data-028).

**NOT READY:** the pinned Crash Site exterior and cave profiles do not apply it (they keep
their pinned evidence, in which no patched object occurs); the main world map, which holds
most targets, cannot load whole yet; a saved game is not modelled, so the patch is applied
over the map's own mobile file only.

## API

- `MapPatchFile.Parse(bytes)` (Arcanum.Data.Maps): the `G_` GUID lines in file order
  (`MapPatchLine(Line, ObjectId, Attributes)`), `Issues` and `HasUnmodelledLines`;
  `For(guid)` lists the lines naming one object.
- `AreaMapPlans.Create` binds the stack's winner of `rules/map.patch` (read when a map
  opens, so every source counts) as the `AreaSourceRole.MapPatch` input and resolves, through
  the script name table and the stack, the 8-byte header of every script a `script`
  attribute names (`AreaSourcePlan.MapPatch`, `AreaMapPatchBinding`). Pinned profiles take
  none.
- The area owner parses the authenticated input and applies the lines to each mobile object
  with a GUID before it builds the object; `AreaObjectRecord.MapPatch` lists the lines applied
  and any change not modelled, `AreaSnapshot.MapPatch` the binding. The raw identities
  (`RawSha256`, `SemanticSha256`) stay those of the file; only the fields change.
- `AreaStepInspector.InspectWholeMap` reads the patched fields. Only a change that is not
  modelled (`MAP_PATCH_CHANGE_UNMODELLED`) or a file with unmodelled lines
  (`MAP_PATCH_LINE_UNSUPPORTED`) leaves evidence Unknown; the pinned profiles keep their
  earlier rule (an object a line names is Unknown).

## Reading the file (MP2, MP3)

Text mode (CR LF as LF). Each read takes up to 4,195 characters; the original stores them in a
4,096-byte buffer, so a read of 4,096 characters or more overruns it, and the file is then not
modelled (`LineOverflowsBuffer`). A line starting with `#` or empty is skipped; the line's last
character is cut (a final line without a newline loses its last real character). The object ID
kind is the first character, upper case: a `G_` GUID is parsed in the Windows memory layout,
which is how object files store it (each group read in base 16 after a check of its first
character only, so `0x` may open a group); `A`, `B`, `N` and `P` IDs are accepted by the
original but their lookups are not modelled (`UnsupportedObjectId`, the file is then not
modelled); other lines are skipped (`MalformedObjectId`). A NUL, a lone CR or 0x1A, which the
original's readers treat by source, make the file not modelled.

## Applying a line (MP4–MP8)

The attributes follow the line's first `:`. A found object on a line without one is a crash
in the original: not modelled. Keys compare ignoring case; values are the next token (C white
space); numbers are read like `atoi` (a value outside int32 is not modelled). An unknown key
stops the line. A field the object's type lacks is not written.

| Key | Effect | Object types |
| --- | --- | --- |
| `internal_name`, `known_name` | name and description number (slots 21, 22) | all |
| `alignment` | base stat 19, clamped to −1000..1000 | PC, NPC (a PC's followers re-check: not modelled) |
| `origin`, `faction` | NPC slots 80, 81 | NPC |
| `notify_npc` | portal slot 39, container slot 42 | portal, container |
| `magic_hit_adjust` | weapon slot 62 | weapon |
| `magic_tech_complexity` | item slot 45 | items |
| `level_scheme` | critter slot 63 | PC, NPC |
| `daystand`, `nightstand` | NPC stand points (slots 78, 79): x low, y high | NPC |
| `obj_flag`, `portal_flag`, `item_flag`, `critter_flag`, `npc_flag` | set the named bit, or clear it after `!`, over the effective value; an unknown name writes the value back (an unknown npc flag name fails in the original: not modelled) | all, portal, items, PC/NPC, NPC |
| `script` | slot, number: the script file's header and the number at that attachment slot; nothing when the number names no script file (so number 0 never clears a slot) | all |
| `training` | tier and skill: not modelled (the tier rule needs the computed skill level) | PC, NPC |

Writing one element of an array the object does not hold first copies its prototype's whole
array (stats, scripts).

## Installed original

`Rules\map.patch` comes from `Arcanum.PATCH0`: 50 `G_` lines, no issue. 32 name objects of the
main world map, 11 name objects of six interiors, 7 name no object of any installed map. On
the interiors (opt-in `MapPatchRealDataTests`, which prints every change as slot numbers and
values): an NPC moves its day and night stand points; an NPC is switched off, flagged and
given a script; a weapon gains item flags (twice); an NPC gains an npc flag and changes
faction; five NPC lines attach script 0 to the dialog slot and, as in the original, change
nothing (a switch candidate: the lines' evident intent is to clear the dialog). No patched
object is Unknown in step evidence; all 80 interiors still plan, load and step.
