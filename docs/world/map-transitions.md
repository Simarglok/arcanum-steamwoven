# Map list and map transitions (ARC-DATA-025)

## Scope

Step one of the whole-world stage M3 ("all transitions"). The data layer reads,
for any map of the [universal catalogue](map-catalog.md), every way the data
moves the player off a trigger: jump points and the teleports that the scripts
of placed objects, tiles and sectors can reach. It classifies each one as
unconditional or not by the policy of the
[transition research](../research/world-transitions.md#unconditional-project-policy)
(ARC-RES-035). It decodes; it never runs a script. Nothing is bound to a
location: the map, its objects, prototypes and scripts are read from the content
stack as the original opens them, so a module or patch is read the same way.
No viewer, session or launcher file changes here; the viewer's map mode uses
this in ARC-WORLD-031.

## API

`Arcanum.Data.Maps`:

- `MapList.Read(stack)`: the rows of `rules/maplist.mes` (the stack's winner),
  numbered from 5000 up to the first missing number, at most 200 (the map list
  load, 0x4125C0, ledger LC1). Each `MapListEntry` has the catalogue number, the folder
  name as the row spells it (trimmed), the start tile (the second and third
  values read like C `strtoll`, where the original's 64-bit `atoi` wraps on overflow; null
  when missing or negative, where the original crashes on a short row and keeps a negative
  value, ledger IF2) and the runtime
  number (catalogue − 4999). `Find(name)` compares without case and returns the
  first row; `ByCatalogue` and `ByRuntime` look a row up by number. A list that
  is missing, unreadable or not a MES file is empty with a typed `Status`.
  `AreaMapPlans` now reads its catalogue numbers and start tile through it.
- `MapTransitions.Read(catalog, list, map, scripts)` returns a
  `MapTransitionSet`: the map's list row (`Entry`, null for an unlisted map), one
  `MapTransitionSite` per jump point and per teleport and call chain a trigger
  can reach, and the reads that failed (`Issues`: a refused sector, mobile file,
  prototype or unreadable script drops only what depends on it).

A site holds:

- `Trigger`: `JumpPoint` (the player enters the cell), `Use` (a use click on a
  placed object, slot 1 of its script array), `TileEntry` (a critter enters a
  scripted tile) or `SectorEntry` (the player enters a sector with a script).
- `Location`: the trigger's tile (for a use click the object's tile, a sector
  object's local location rebased onto its sector; for a sector entry the
  sector's origin). `Object`: for a use click, the sector and window ordinal
  (null sector and the record ordinal for a mobile), the SHA-256 of the
  object's own id, its type and prototype. Sector objects carry a null id, so
  their digests are equal: the sector and ordinal identify one, as the area
  owner keys it; a mobile's id is its own.
- `Script`, `Chain` and `Line`: the attached script, the scripts from it to the
  teleport through literal calls, and the teleport's line. `Fade`: a
  fade-and-teleport. `OneShot`: the attached script removes or replaces itself
  on a reached branch, or carries the auto-removing file flag, so the trigger
  works once; never a tile entry, whose own changes the original throws away
  (ledger X3, TR5).
- `DestinationCatalogue`, `DestinationRuntime` and `Arrival`: the destination
  map and tile. A jump point's runtime −1 and a script's runtime −1 (catalogue
  4998) keep the current map; a computed destination has runtime 0.
- `Guards`, empty for an unconditional site: `ConditionalPath` (behind a
  condition, in or after a loop, through a deferred call or script replacement,
  through a call whose attachee is a focus group, or through a trapped callee),
  `ComputedDestination`, `MovesOtherObject` (not the triggerer or the player),
  `CallChangesTriggerer`, `ObjectOffOrDestroyed`, `ObjectNotClickable`
  (don't-draw, invisible or click-through), `TrapScript`, `ContainerOpenCheck`
  (a container opens before its use script runs), `UseRunsOtherAction` (a use
  click on anything but a portal, container or scenery) and
  `UnlistedDestination`.

The effective script array and flags of an object are its own serialized field
when present (an explicit null included), else its prototype's. Call depth is
at most 6 (`MapTransitions.MaxCallDepth`); a call cycle at the same entry line
is followed once, and calls reaching the same callee entry with the same
guards once; an attached script whose call graph needs more than 4,096 visits
(`MapTransitions.MaxVisits`) gives no site and a `LimitExceeded` issue. A
tile's first jump point is the only one read, as the original reads it. An
instance whose prototype cannot be read keeps its own fields (the prototype
is reported). The map's own file issues are part of `Issues`. Script numbers
resolve through the original's [script names](../scripting/script-content.md).

## Installed original

The opt-in test (`tests/Arcanum.Data.Tests/Maps/MapTransitionsRealDataTests.cs`,
`DAT_ROOT` = the game folder, about 1 s) reads all 79 listed map folders
with no issue but the editor's two side files and gives the numbers of the research census: 45 jump points
(7 leave their map), 281 use paths on scenery and portals (241 unconditional),
5 container paths (none), 421 tile paths (9 unconditional), no sector-script
path; 151 unconditional cross-map paths (144 scenery, 2 portals, 5 tiles), two
unconditional one-shot triggers, and 44 maps reachable from the start map. It
also finds the Crash Site cave pair (scripts 2793 and 1532) that the pinned
profiles state, with the same arrival tiles.

## Original check

The decoded facts were checked in the retail executable
([ledger TR1–TR5](../research/original-verification-ledger.md#map-transitions-arc-data-025)):
the teleport and fade-and-teleport operands and their map − 4999, a jump
point firing for any PC, a tile script for any critter, one pending player
teleport, −1 as the current map, the first jump point of a tile, and a tile
script that never keeps its own changes. The unconditional/conditional split is
the project's policy, not a rule of the original.

## Not covered

- Dialog, dying, destroy, heartbeat and bust triggers and dialog resumption
  (ARC-RES-035 counts them; they need the SCR dispatcher, SCRIPT slice S4).
- What a guard reads at T0 is not evaluated; a conditional site stays closed
  for M3. Call-ex, objects created at run time and scripts of unplaced
  prototypes are not followed, as in the census.
- A tile entry fires for any critter, not only the player; the consumer decides
  who triggers it.
