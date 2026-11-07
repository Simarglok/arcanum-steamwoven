# The remaining hosts move into the world runtime (K17, MIG15)

Status: **slice 1 (ARC-DEV-217, the concept and the file classification, docs-only, 2026-11-07), slice 2 (ARC-DEV-218, the 15 leaf files, 2026-11-07) and slice 3 (ARC-DEV-219, the type-level read of the knot, docs-only, 2026-11-07) have landed; the moves follow in the slices named in [the slice plan](#slice-plan).**
Concept and plan of MIG15 of the [target model](target-model.md#migration-order) (element TM11, gaps GR32 and GR33 of the [gap register](gap-register.md), owner decision GQ6 A).

## The gap (GR32)

`tools/terrain-viewer/Core` held 165 files and about 32 000 lines when this concept was written (150 after S2). It has no Godot reference, but the rules that read base records and drive the world live there: the AI host, the
dialog, use-script and script hosts, the inventory, barter and restock hosts, the companions, homes and map transfers, the hero's goal runs, the combat and spell hosts. The session never opens game
data; facts reach it from these hosts as call arguments. GQ6 A decided that the rules leave the tool for the Godot-free world runtime (`src/Arcanum.World`), which already holds the record resolver,
the residency chart and ports, the frame step, the goal stepper, the hero request boundary and the interface mode stack. MIG15 moves what is left, in slices that never change behaviour.

## What moves and what stays

A file of `Core` falls into one of four groups. The test is what the file *decides*, not what it is called.

| Group | Rule of the group | Examples | Where it ends |
| --- | --- | --- | --- |
| **Rules and hosts** | Reads base records or session state and decides what happens; no pixels, no Godot types, no `Arcanum.Presentation` or `Arcanum.Input` use | `AreaAdvancement`, `AreaItemUse`, `AreaBarter`, `AreaRestock`, `AreaInventoryHost`, `AreaCompanions`, `AreaHomes`, `AreaMapTransfers`, `AreaTransition`, `AreaStartingHero` | `src/Arcanum.World` |
| **Busy rules** | A rules file that a running migration slice changes (MIG13 S12–S15, MIG14, the dialog lane) | `AreaAiHost`, `AreaHeroRunsHost`, `AreaCombat`, `AreaCombatRound`, `AreaHeroDeath`, `AreaFallenPoses`, `AreaHeldThrow`, `AreaHeroKnockdown`, `AreaSpellCasting`, `AreaSpellCastMap`, `AreaMissileHost`, `AreaProjectileHost`, `AreaKnockbackHost`, `AreaSceneryHost`, `AreaEyeCandyHost`, `AreaDoorHost`, `AreaScriptHost`, `AreaUseScripts`, `AreaConversation`, `AreaGreeting` | Moves after its migration slice has merged |
| **Mixed** | One file holds a rule and its drawing or its start-up | `AreaScenePlan` (the traversal chart with the draw commands), `AreaLoadOperation`, `AreaMapLoadOperation`, `TerrainLoadCoordinator` | Split first (the seam is the slice's first step), then the rule half moves |
| **Viewer** | Art, windows, fonts, input mapping, HD, launcher, command line, diagnostics, policies of the camera and frame pacing | every `*Art.cs`, `HdFrameSource`, `HdPresentation`, `HdResidency`, `HdNearestPack` (a test fixture), `AreaCombatFloaters`, `AreaLifeBars`, `AreaMessageLog`, `AreaGameBar`, `AreaMainMenu`, `AreaGameOptions`, `LauncherScreen`, `BugReport`, `TerrainViewerArguments`, `Viewer*`, `Diagnostic*` (except `DiagnosticGridSession` and its evidence records, which are Mixed), `ActorDiagnostic*`, `CameraPolicy`, `FramePacingPolicy`, `WorldZoomPolicy`, `AreaVisibilityIndex`, `AreaNearestAxis` | stays in `tools/terrain-viewer/Core` |

## How a move is done

- **One move, no change.** A file goes by `git mv` into `src/Arcanum.World`, its namespace becomes `Arcanum.World`, its types keep their names. No rule, no signature and no behaviour changes in a move slice;
  a consumer only gains `using Arcanum.World;`. A move slice has no schema change and no new smoke stage.
- **Bottom up.** A file moves when every `Core` type it uses has moved or moves in the same slice. `Arcanum.World` cannot reference `Core`, so the order follows the dependencies, leaves first
  (the dependency table was built from the type names of the files and is re-run at the start of each slice).
- **A hub is split, not dragged.** `AreaScenePlan` (504 lines, named by 13 `Core` files and 20 Godot files), `DiagnosticGridSession` (290 lines, 3 `Core` and 6 Godot files), `AreaLoadOperation` and `AreaMapLoadOperation` mix rules the hosts need with drawing or start-up. A hub slice names the seam
  (a port the viewer fills, as `ISectorTerrain` and `IGoalStepHost` are) before it moves the half the hosts read.
- **Tests go with the subject.** A test file whose subject moved goes to `tests/Arcanum.World.Tests`, with its name; the rest stay in `tests/Arcanum.TerrainViewer.Tests` and gain the `using`.
- **Docs follow.** Paths in `docs/` that name a moved file are updated in the same PR; the component catalogue row of the world runtime lists the new files.
- **The viewer keeps drawing and input.** After MIG15 the Godot layer calls the world runtime through its request boundary (TM11, `HeroRequests`) and ports; it holds no rule that reads a base record.
- **The oracle stays the executable.** A move never classifies a mechanic; a moved file keeps its bug flags and its "Original"/"Steamwoven" switches as they are.

## The knot (ARC-DEV-219, a type-level read of the dependency graph)

The file-level table of S1 is too coarse: it counted a name such as `Frame` or `Entry` as a dependency, and it could not see which half of a file is the blocker. A read at the level of the 542
top-level types of `Core` (comments stripped, a type moves only when every `Core` type it names can move; the Viewer group of the first table is fixed) gives three facts:

- **One knot.** 55 files are one strongly connected component: `AreaScenePlan`, `DiagnosticGridSession`, `ActorDiagnostic*`, `TerrainLoadCoordinator`, `AreaLoadOperation`, `AreaMapLoadOperation`, `AreaInventoryHost`,
  `AreaConversation`, `AreaHover`, `AreaCompanions`, `AreaStartingHero`, `AreaUseScripts`, `AreaPortals` and the art, panel and HD files they name (`AreaInventoryArt`, `AreaLootArt`, `AreaBarterArt`, `AreaGameBar`,
  `AreaMessageLog`, `HeroBodyArt`, `HdPresentation`). No file of it can move alone: the rules and the viewer's loader and art types name each other. The 15 files of S2 and
  the files with no edge into the knot (`AreaCellPicking`, `AreaChangeOfPlace`, `AreaInventoryHole`, `AreaStartMapAreas`, `AreaNewGameStart`, `CrashSiteProfile`, and the busy hosts of the second table) were the only ones free.
- **The cut is small and known.** The rule types that stay behind name few viewer types. Most often **`TerrainViewerArguments`** and **`TerrainViewerMode`** (ten files: the map loaders, `AreaMapHomes`, `AreaMapRecords`,
  `AreaShopMap.Load`), then `HeroBody`, `HdFrameQueries`, `DiagnosticGridActor`, `AreaAiAttackArtRequest` and `AreaAiAttackPlay`, `AreaInterfaceFrame`, the panel data of `AreaSpellsPage`, `AreaSkillsPage`,
  `AreaStatsPage` and `AreaCharacterSheet` (view models of pure data), `AreaLootGridItem`, `AreaMessageEntry` and `AreaMessageKind`, and the layout records of the inventory and loot art.
- **Each blocker is a seam of one of three kinds.** (1) *The launch options*: `TerrainViewerArguments` is both the command line (`Parse`, `--mode`) and the open request of a map (`GameRoot`, `AreaMapName`, `AreaMapArrival`,
  `AreaMapSector`, `DataRules`, `Hero`, the archive paths). The open request becomes a record of the world runtime (`AreaLaunch`) that the viewer fills from the arguments; the parser stays. (2) *Pure data in a viewer file*
  (`AreaMessageEntry`, the page view models, the layout records): the type is extracted into a world file and the viewer file keeps the drawing. (3) *A callback into drawing* (`HeroBody`, `HdFrameQueries`, the attack art
  request): a port the viewer fills, as `ISectorTerrain` and `IGoalStepHost` are.

So MIG15 is not a move of files one by one; it is five cuts (the launch options, the page data, the art ports, the loader split, the scene hub) followed by the moves they free.

## Slice plan

| Slice | Code | Result | Depends on |
| --- | --- | --- | --- |
| S1 | ARC-DEV-217 | This concept and the classification (docs-only) | — |
| S2 | ARC-DEV-218 | **Done.** Wave 1: the leaf rules with no `Core` dependency (`AreaContentRoot`, `AreaAdvancement`, `AreaItemUse`, `AreaHealingItems`, `AreaLockpicks`, `AreaOccupancy`, `AreaPrototypeItems`, `AreaScriptStory`, `AreaRandomSeed`, `AreaReputations`, `AreaWorldMap`, `AreaRealSteps`, `AreaWholeMobile`, `AreaRouteApproachPlanner`, `AreaSectorChart`). Tests that read only moved types move with them (`AreaRouteApproachPlannerTests`); the others use viewer fixtures and stay, and `Arcanum.World` shows its internals to `Arcanum.TerrainViewer.Tests` and `Arcanum.World.Tests`. `AreaOccupancyRules.PcType` and `NpcType` became public | S1 |
| S3 | ARC-DEV-219 | **Done (docs-only).** The type-level read of the knot above | S2 |
| S4 | ARC-DEV-220 | Cut 1, the launch options: `AreaLaunch` and `TerrainViewerMode` in `Arcanum.World`, the viewer's arguments fill them; the ten files that name the arguments take the options | S3 |
| S5 | ARC-DEV-221 | Cuts 2 and 3, the page data and the art ports: the pure-data types of the viewer files move, the ports for `HeroBody`, `HdFrameQueries` and the attack art request are defined | S3 |
| S6 | ARC-DEV-222 | Cuts 4 and 5, the loader split and the scene hub: the rule half of `AreaLoadOperation` and `AreaMapLoadOperation`, `AreaScenePlan`'s chart, `DiagnosticGridSession` and its evidence move | S4, S5 |
| S7 | ARC-DEV-223 | The freed rules, wave 2: trade and items (`AreaBarter`, `AreaRestock`, `AreaContainerLoot`, `AreaInventoryHost`, `AreaItemScriptHost`, `AreaReading`, `AreaShopMerchant`), places and travel (`AreaHomes`, `AreaPortals`, `AreaMapTransfers`, `AreaTransition`, `AreaObjectTargets`, `AreaMapRecords`, `AreaResidency`) | S6 |
| S8 | ARC-DEV-224 | The freed rules, wave 3: companions (`AreaCompanions`, `AreaCompanionData`, `AreaCompanionLoyalty`, `AreaCompanionOrders`, `AreaBodyMoves`, `AreaAmbient`, `AreaLocalLighting`) and the start of a game (`AreaStartingHero`, `AreaNewCharacter`, `AreaNewGameStart`, `AreaCreationEditor`, `AreaOpeningShop`) | S6 |
| S9 | ARC-DEV-225 | The busy rules after MIG13 S15 and MIG14 (AI, hero runs, combat, spells, projectiles, missiles, doors, scenery, eye candy, scripts, dialog) | MIG13, MIG14, the dialog lane |
| S10 | ARC-DEV-226 | The re-check: every remaining `Core` file is Viewer group; GR32 and GR33 closed in the gap register; the status rows | S9 |

Slices S4 to S8 are cut by the type-level read of their time (the read is re-run at the start of each); a slice that finds a seam it did not expect says so in its report and splits. A slice that needs a code past ARC-DEV-226 asks the coordinator for one.
