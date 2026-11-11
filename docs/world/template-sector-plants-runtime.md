# Template sector plants runtime

Status: **Implemented in the Scripting dispatcher, the session and the AREA viewer's map mode.** Work item ARC-WORLD-081,
2026-10-28, from the research [template sector scripts](../research/template-sector-plants.md) (ARC-RES-073). A generated sector
of a main-map window runs its template's sector script when the player enters it, as an own sector file's does; the only such script,
the plant generator 30285, creates its herbs next to the template's marker, and they lie on the ground until picked up.

Requirements: [FR-WORLD-005](../requirements/world.md#fr-world-005) (sector data and its changes: the generated sectors' scripts),
[FR-SCENE-001](../requirements/scene.md#fr-scene-001) (the items the template script places), [FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002)
(random, loops over object groups and create object). Owner decision 2026-10-28 ([decisions](../architecture/decisions.md)): a fixed vicinity and a limit
under Steamwoven; the window-sized vicinity and no limit under Original.

Evidence: the retail 1.0.7.4 `Arcanum.exe`, ledger [PG1–PG13](../research/ledger/scripts.md#template-sector-scripts-arc-res-073) and
[OL29](../research/companion-orders-loyalty-evidence.md#5-waiting-followers-and-the-level-step-q5-xp5-xp7) (the play view's rectangle).
Code: [`ScrAdmissionSet.S15`](../../src/Arcanum.Scripting/Scr/ScrAdmission.cs), [`ScrInterpreter`](../../src/Arcanum.Scripting/Scr/ScrInterpreter.cs),
[`CreatedItemRules`](../../src/Arcanum.Simulation/Items/CreatedItemRules.cs), [`DropPlacementRules.PlaceCreated`](../../src/Arcanum.Simulation/Items/DropPlacementRules.cs),
[`GameSessionCreatedItems`](../../src/Arcanum.Session/GameSessionCreatedItems.cs), [`MapTransitions.TemplateSectorScripts`](../../src/Arcanum.Data/Maps/MapTransitions.cs),
[`AreaUseScriptHost`](../../src/Arcanum.World/AreaUseScripts.cs), [`AreaPrototypeItems`](../../src/Arcanum.World/AreaPrototypeItems.cs).
Tests: [`ScrWorldTests`](../../tests/Arcanum.Scripting.Tests/Scr/ScrWorldTests.cs), [`DropPlacementRulesTests`](../../tests/Arcanum.Simulation.Tests/Items/DropPlacementRulesTests.cs),
[`CreatedItemsStateTests`](../../tests/Arcanum.Session.Tests/CreatedItemsStateTests.cs), [`AreaUseScriptHostTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaUseScriptHostTests.cs),
opt-in [`TemplateSectorPlantsOriginalTests`](../../tests/Arcanum.TerrainViewer.Tests/TemplateSectorPlantsOriginalTests.cs).

## Which sectors run a script

- A main-map window ([window plans](whole-map-plans.md#window-plans-arc-data-032)) lists the sector scripts of its template sectors
  (`MapTransitions.TemplateSectorScripts`): for every core and context sector whose source is a terrain template, the template's
  sector script section (flags, counters, number) when its number is not 0, placed at the sector's origin. The load logs
  `ARC-WORLD-081 load stage=template-scripts scripts=N`.
- Map mode runs them on entry exactly as an own sector's script ([tile and sector scripts](../rendering/area-map-mode.md#tile-and-sector-scripts-arc-script-017)):
  when a completed step of the player enters the sector, from line 0 as a use, the player the triggerer and no attachee (PG9: the
  original runs a template sector's script section like an own file's). The slot lives in the session's map changes on the sector
  (`sector.script`, `sector.lf`, `sector.counters`); the plant generator changes none of its own, so nothing is stored.
- Every map, every generated sector and every template with a script is treated the same; nothing names a place.

## The commands (set S15)

The use, tile and sector set of the viewer (`AreaUseScriptHost.Admission`, now "S4-area-use-reading-objects-story-world"; tile and sector runs use it with the dialog action since ARC-SCRIPT-046, `PlacedAdmission`) gains the
world commands of `ScrAdmissionSet.S15`:

- **Random** (37, PG5): the bounds in either order, both included, into operand 2 (a number literal there refuses the script). The
  draw is the session's generator (label `scr.random`, [session random](session-random-runtime.md)), so a span above 32,768 is covered in
  both modes (the original's generator gives 15 bits a draw).
- **Loop for** and **loop end** (18, 19, PG7, PG11): the list is filled once from the object operand, the line after the loop for
  remembered and the first object made the current looped object (focus kind 10); loop end steps to the next object and jumps back, or
  after the last clears the loop and goes on (the current looped object stays the last one). An empty list goes on after the first later
  line whose condition is "true" and whose true action is loop end (`ScrAdmission.AfterLoopEnd`, 0x44BC60), and without one the run
  stops as on a line outside the script. A loop for inside a running loop and a loop end without a loop do nothing (the original logs
  an error). Region admission follows a loop end back to every loop for's next line.
- **Every scenery in vicinity** (focus kind 15, PG3): the scenery of the shown map within `ScrWorld.VicinityReach` = 10 tiles of the
  run's attachee on both axes (none without an attachee), not destroyed or switched off, in snapshot order. The PC's place is the
  walking actor's committed cell. As a single object it names the list's first object.
- **Create object** (52, PG1, PG8, PG12): the prototype number as named and the first object of operand 1; an absent object creates
  nothing. The viewer adds 20 to find the prototype file and reads its type, name, description, art and weight
  (`AreaPrototypeItems`); before anything of the run applies, every creation is checked (a prototype that is no item the session
  creates — no ammunition, gold or key —, no shown map, an object without a place, the created items' budget), and one that fails leaves the
  whole run unapplied (`EffectsRejected`), so a creation never leaves a run half applied.
- **Counters with any index** (PG6): a counter operand is admitted with any index and reads or writes counter (index mod 4), as the
  original's shift keeps five bits; the generator's index 2,012,780,696 reads counter 0.

## Where the created item lies

`GameSessionOwner.CreateGroundItem` places it: up to ten tries, each drawing a distance 1..3 and then the placement's direction 0..8
(8 stays) from the session's generator (`DropPlacementRules.PlaceCreated` over the viewer's `AreaDropMap`, the same placement 0x4F4E40
a drop uses); a try fails only when a step leaves the map, and a path that is missing or longer than the distance + 1 keeps the
object's own tile; after ten failures the object's own tile (PG1, PG2). Next to the marker (scenery) no critter test applies; next to a
critter (another script's create next to the PC or an NPC) the walk stops on the first tile with no PC or NPC (PG2), as the PC's side or
an NPC.

The item is a placed item of source `created` (snapshot schema 48): a new `made:n` entity numbered with the made items, the
prototype's kind, weight facts and complexity, on the shown map at that cell. It needs no hero, schedules no decay (the creation never
reaches the decay's scheduler, ledger ID9), and the viewer draws it from its prototype's art and picks it up like any placed item:
picked up it joins the hero's made items (the hero's budget must have room). Created items have their own budget
(`GameSessionLimits.MaxCreatedItems`, 8,192) outside the hero's 512.

## Vicinity and limit by rule mode

| | Steamwoven | Original |
| --- | --- | --- |
| Vicinity | 10 tiles on both axes around the attachee | the same box: the original takes the bounding box of a game-window-sized screen rectangle (0x440FF0), and its window is the fixed 800 × 400 play view, 10 tiles on both axes with 80 × 40 tiles (OL29) |
| Vicinity list | every scenery object of the box | the first 100 (`ScrRuleProfile.VicinityListCapped`, PG3) |
| Pile-up | at most `CreatedItemRules.Limit` = 6 created items within `CreatedItemRules.Reach` = 3 tiles of the object; a create next to a full patch adds nothing and draws nothing | no limit but the session's budget (`ScrRuleProfile.CreatedItemsUnbounded`) |

Why these values: the vicinity reach is the original's own play view (the viewer's window and zoom never decide it), so the fixed reach
the owner asked for is that view's 10 tiles; the 3-tile reach is the farthest a created object can land (distance at most 3, PG1, PG2);
the limit 6 is the most one run of the installed generator can create next to one marker (two draws in each of R rounds, R at most 3 in
the plains variants 1 and 67108865), so a patch never holds more than one lucky crossing makes. The limit counts created items lying near
the object (not seed or dropped items), applies to creations next to an object that stays (not next to a critter, which moves on, so an
item a script hands over beside the PC is never held back), and is read from the session's rule mode at the moment of each create, so the
switch takes effect at once (the vicinity's list cap follows the run's script rules, which the viewer takes from the same session).

## Not ready

- Only items and (since ARC-DEV-371, [E1-c3](../architecture/object-directory.md#e1-c3-script-created-critters-arc-dev-371)) critters are created; a create of any other object type (none in the template scripts) refuses the run's effects. The one
  installed case map mode reaches is the start map's "snapping twig" tile script 1629 (130 tiles): it creates eight critters of prototype
  28464 next to the PC, which the viewer creates since ARC-DEV-371 (the critters carry the encounter flag; their inventory is not generated yet).
- The vicinity's order (which 100 objects the original keeps) is not read; the project keeps snapshot order.
- The 15 own sector files that attach 30285 now run too; other use, tile and sector scripts that were refused only for random, loops,
  the vicinity or a counter index above 3 run as well (the real-data recount is in the [area map mode](../rendering/area-map-mode.md#tile-and-sector-scripts-arc-script-017)).
- How the original saves created objects for a template sector (its sector differences) is not modelled; the session keeps them.
- Only a window lists template sectors: a map loaded whole loads its own sector files only (K'na Tha's 548 generated sectors are not
  loaded, so nobody can enter them yet).
- Region admission follows a loop end back to every loop for of the script, reachable or not (on the safe side: it may refuse, never
  admit wrongly).
- Tile scripts still run for the player only.
