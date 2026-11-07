# The remaining hosts move into the world runtime (K17, MIG15)

Status: **slice 1 (ARC-DEV-217, the concept and the file classification, docs-only, 2026-11-07); the moves follow in the slices named in [the slice plan](#slice-plan).**
Concept and plan of MIG15 of the [target model](target-model.md#migration-order) (element TM11, gaps GR32 and GR33 of the [gap register](gap-register.md), owner decision GQ6 A).

## The gap (GR32)

`tools/terrain-viewer/Core` holds 165 files and about 32 000 lines. It has no Godot reference, but the rules that read base records and drive the world live there: the AI host, the
dialog, use-script and script hosts, the inventory, barter and restock hosts, the companions, homes and map transfers, the hero's goal runs, the combat and spell hosts. The session never opens game
data; facts reach it from these hosts as call arguments. GQ6 A decided that the rules leave the tool for the Godot-free world runtime (`src/Arcanum.World`), which already holds the record resolver,
the residency model, the frame step, the goal stepper, the hero request boundary and the interface mode stack. MIG15 moves what is left, in slices that never change behaviour.

## What moves and what stays

A file of `Core` falls into one of four groups. The test is what the file *decides*, not what it is called.

| Group | Rule of the group | Examples | Where it ends |
| --- | --- | --- | --- |
| **Rules and hosts** | Reads base records or session state and decides what happens; no pixels, no Godot types, no `Arcanum.Presentation` or `Arcanum.Input` use | `AreaAdvancement`, `AreaItemUse`, `AreaBarter`, `AreaRestock`, `AreaInventoryHost`, `AreaCompanions`, `AreaHomes`, `AreaMapTransfers`, `AreaTransition`, `AreaStartingHero` | `src/Arcanum.World` |
| **Busy rules** | A rules file that a running migration slice changes (MIG13 S12–S15, MIG14, the dialog lane) | `AreaAiHost`, `AreaHeroRunsHost`, `AreaCombat`, `AreaCombatRound`, `AreaHeroDeath`, `AreaFallenPoses`, `AreaHeldThrow`, `AreaHeroKnockdown`, `AreaSpellCasting`, `AreaSpellCastMap`, `AreaMissileHost`, `AreaProjectileHost`, `AreaKnockbackHost`, `AreaSceneryHost`, `AreaEyeCandyHost`, `AreaDoorHost`, `AreaScriptHost`, `AreaUseScripts`, `AreaConversation`, `AreaGreeting` | Moves after its migration slice has merged |
| **Mixed** | One file holds a rule and its drawing or its start-up | `AreaScenePlan` (the traversal chart with the draw commands), `AreaLoadOperation`, `AreaMapLoadOperation`, `TerrainLoadCoordinator` | Split first (the seam is the slice's first step), then the rule half moves |
| **Viewer** | Art, windows, fonts, input mapping, HD, launcher, command line, diagnostics, policies of the camera and frame pacing | every `*Art.cs`, `HdFrameSource`, `HdPresentation`, `HdResidency`, `HdNearestPack` (a test fixture), `AreaCombatFloaters`, `AreaLifeBars`, `AreaMessageLog`, `AreaGameBar`, `AreaMainMenu`, `AreaGameOptions`, `LauncherScreen`, `BugReport`, `TerrainViewerArguments`, `Viewer*`, `Diagnostic*`, `ActorDiagnostic*`, `CameraPolicy`, `FramePacingPolicy`, `WorldZoomPolicy`, `AreaVisibilityIndex`, `AreaNearestAxis` | stays in `tools/terrain-viewer/Core` |

## How a move is done

- **One move, no change.** A file goes by `git mv` into `src/Arcanum.World`, its namespace becomes `Arcanum.World`, its types keep their names. No rule, no signature and no behaviour changes in a move slice;
  a consumer only gains `using Arcanum.World;`. A move slice has no schema change and no new smoke stage.
- **Bottom up.** A file moves when every `Core` type it uses has moved or moves in the same slice. `Arcanum.World` cannot reference `Core`, so the order follows the dependencies, leaves first
  (the dependency table was built from the type names of the files and is re-run at the start of each slice).
- **A hub is split, not dragged.** `AreaScenePlan` (504 lines, 18 users), `DiagnosticGridSession` (290, 17 users), `AreaLoadOperation` and `AreaMapLoadOperation` mix rules the hosts need with drawing or start-up. A hub slice names the seam
  (a port the viewer fills, as `ISectorTerrain` and `IGoalStepHost` are) before it moves the half the hosts read.
- **Tests go with the subject.** A test file whose subject moved goes to `tests/Arcanum.World.Tests`, with its name; the rest stay in `tests/Arcanum.TerrainViewer.Tests` and gain the `using`.
- **Docs follow.** Paths in `docs/` that name a moved file are updated in the same PR; the component catalogue row of the world runtime lists the new files.
- **The viewer keeps drawing and input.** After MIG15 the Godot layer calls the world runtime through its request boundary (TM11, `HeroRequests`) and ports; it holds no rule that reads a base record.
- **The oracle stays the executable.** A move never classifies a mechanic; a moved file keeps its bug flags and its "Original"/"Steamwoven" switches as they are.

## Slice plan

| Slice | Code | Result | Depends on |
| --- | --- | --- | --- |
| S1 | ARC-DEV-217 | This concept and the classification (docs-only) | — |
| S2 | ARC-DEV-218 | Wave 1: the leaf rules with no `Core` dependency (`AreaContentRoot`, `AreaAdvancement`, `AreaItemUse`, `AreaHealingItems`, `AreaLockpicks`, `AreaOccupancy`, `AreaPrototypeItems`, `AreaScriptStory`, `AreaRandomSeed`, `AreaReputations`, `AreaWorldMap`, `AreaRealSteps`, `AreaWholeMobile`, `AreaRouteApproachPlanner`, `AreaSectorChart`) | S1 |
| S3 | ARC-DEV-219 | The scene hub: the traversal chart, the portal and wall rules split from the draw commands of `AreaScenePlan`; `DiagnosticGridSession` and its evidence records move | S2 |
| S4 | ARC-DEV-220 | Trade and items: `AreaBarter`, `AreaRestock`, `AreaContainerLoot`, `AreaInventoryHost`, `AreaItemScriptHost`, `AreaReading`, `AreaShopMerchant` and their ports | S2, S3, the dialog lane for `AreaConversation` |
| S5 | ARC-DEV-221 | Places and travel: `AreaHomes`, `AreaPortals`, `AreaMapTransfers`, `AreaTransition`, `AreaObjectTargets`, `AreaMapRecords`, `AreaResidency` | S3 |
| S6 | ARC-DEV-222 | Companions: `AreaCompanions`, `AreaCompanionData`, `AreaCompanionLoyalty`, `AreaCompanionOrders`, `AreaBodyMoves`, `AreaAmbient`, `AreaLocalLighting` (the lighting rule half) | S3, S5 |
| S7 | ARC-DEV-223 | Start of a game: `AreaStartingHero`, `AreaNewCharacter`, `AreaNewGameStart`, `AreaCreationEditor`, `AreaOpeningShop`, the load operations after their split | S4, S5 |
| S8 | ARC-DEV-224 | The busy rules after MIG13 S15 and MIG14 (AI, hero runs, combat, spells, projectiles, missiles, doors, scenery, eye candy, scripts, dialog) | MIG13, MIG14, the dialog lane |
| S9 | ARC-DEV-225 | The re-check: every remaining `Core` file is Viewer group; GR32 and GR33 closed in the gap register; the status rows | S8 |

Slices S3 to S7 are cut by the dependency table of their time; a slice that finds a hub it did not expect says so in its report and shrinks. ARC-DEV-226 stays reserved for a slice that the hub split (S3)
shows to be too large for one PR.
