# Schematic window runtime

Status: **Implemented bounded component (viewer, PC crafter only).** Work item ARC-TECHNOLOGY-005 (2026-10-21), slice TECH-5 of
the [technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted). The
hero's schematic window in the AREA viewer (`crash-site-area`: exterior, cave and `--area-map`), opened with the original key
`T` or by script action 129 for the PC, over the session's [tech degrees](tech-degrees-runtime.md),
[found schematics](schematic-knowledge-runtime.md) and [assembly](schematic-assembly-runtime.md). No saved state is added (the
snapshot schema is unchanged).

Requirements: [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012) (schematics),
[FR-ITEM-017](../requirements/items-economy.md#fr-item-017) (learned and found schematics shown),
[FR-ITEM-018](../requirements/items-economy.md#fr-item-018) (assembly). The NPC crafter remains (below).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` ([ledger "Schematic window"](../research/original-verification-ledger.md#schematic-window-arc-technology-005),
SW1–SW8). Code: [`AreaSchematics`](../../tools/terrain-viewer/Core/AreaSchematics.cs) (the Godot-free page),
[`AreaSchematicsPanel`](../../tools/terrain-viewer/Godot/AreaSchematicsPanel.cs) (the diagnostic panel),
[`TerrainViewerAreaSchematics`](../../tools/terrain-viewer/Godot/TerrainViewerAreaSchematics.cs) (keys, sounds, routing),
[`AreaScriptHost`](../../tools/terrain-viewer/Core/AreaScriptHost.cs) (action 129) and the SCR dispatcher's
[`ScrSchematicRequest`](../../src/Arcanum.Scripting/Scr/ScrDispatchTypes.cs) (admission set S9); tests
[`AreaSchematicsTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaSchematicsTests.cs),
[`AreaScriptHostTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaScriptHostTests.cs),
[`ScrSchematicTests`](../../tests/Arcanum.Scripting.Tests/Scr/ScrSchematicTests.cs), the Godot self-test
`ARC-TECHNOLOGY-005 self-test PASS` and the native smoke `ARC-TECHNOLOGY-005 native-smoke schematics PASS`.

## Opening and closing (SW1–SW3)

- `T` (logical key down) opens the window for the session's hero as crafter and viewer, as the original's in-game key handler
  calls the open with the local PC twice (SW1). It acts from the world only: not while the palette, a dialog, another window or
  UI focus owns input (the original's guards are those of C and L; its mode change would close another open window instead,
  the viewer's modal windows own their keys). `T` is no longer a reserved key in `crash-site-area` with a session.
- It opens only with a hero, and never for a dead one (the original returns for a dead crafter or viewer); the HUD names the
  refusal (`no hero (start with --hero)`, `refused: the hero is dead`).
- Open plays 3008; the player's close (`T`, `Escape`, a controller cancel or the Close button) plays 3009 (SW3). A teardown (a
  map change, the session's end, the hero's death) closes it silently.
- Like the character sheet and the logbook it is modal: the actor gate closes, the clock pauses, a walk stops (the original
  does not pause; the viewer's modal windows do).
- The interface button of the original (art 471) is not drawn; `T` and action 129 are the viewer's ways in.

## Tabs, disciplines and pages (SW4)

- **Learned** lists, per discipline, the first "stored degree" learned records; **Found** lists the found schematics of the
  discipline in the order they were found (a PC crafter only). An empty tab shows the discipline's placeholder record.
- A discipline button sets the discipline and page 0 and plays 3010. Previous and Next move one page and play 3010; they are
  disabled on the first and last page (the original disables them in the redraw).
- A tab button acts only from the other tab: it keeps each tab's own discipline and page and plays 3011; the shown tab's button
  does nothing. The tab, discipline and page are kept across close and open and reset for a new session (a quick load), as the
  original keeps them until a game reset; the original also resets them for an NPC crafter (not reachable here).
- While open the window owns every key: `T` and `Escape` close it, `Tab` or a controller confirm presses Assemble.

## The page and Assemble (SW5, SW6)

The page is built from session queries only (`QueryHeroTechnology`, `QueryHeroFoundSchematics`, `QueryHeroAssembly`) and the
player's own data loaded once per module in the background (`schematic.mes` and `schematic_text.mes` through the content
stack, `tech.mes` for the discipline names, and every prototype the records name for its complexity, discipline and item kind):
the record's name and description, the two components (found or missing, the expertise they need), the product and quantity,
the hero's expertise in the discipline, and the button state.

- Assemble in state "item missing" shows `schematic_text.mes` 0, in state "expertise lacking" 1, with no change (SW4).
- In state "can assemble" it calls `GameSessionOwner.AssembleSchematic` with the window's facts and the inventory window's
  insert check; on success it shows `schematic_text.mes` 2 and plays **3018 + the current discipline** (3018..3025, SW6; the
  first reading of ARC-RES-062 said "current tab", corrected in the ledger). A session refusal is named on the message line
  (no room, an item type the session does not hold, the inventory limit).
- The hover of Assemble is `schematic_text.mes` 3.
- The viewer keeps no inventory locations: every held item counts as in the bag; the session knows the worn and broken ones.

**Diagnostic layout (project policy):** a plain panel with project labels (Learned, Found, D0..D7, Previous, Next, Close,
Assemble) instead of the original's window (interface art 365, the record art, the discipline glyphs, the slot pictures and
overlays, the button faces); the record texts and messages are the data's. Layout parity is NOT_VERIFIED.

## Script action 129 (SW8, TC2)

The SCR dispatcher records start schematic UI (129) as one request for its object operand (an absent object names none; the
run goes on); the admission set S9 adds it to S8, and the viewer's dialog-script set admits it. The viewer resolves each request
(TC2, both fixed, both modes; the original calls an uninitialised callback):

| Object | Original intent (arguments of the open) | Viewer |
| --- | --- | --- |
| the PC | (PC, PC) | the hero's window opens, as with `T`, once no dialog, window or palette owns input (a dialog's script runs while its dialog is open; the original's mode change would end the dialog first) |
| an NPC with a PC leader | (NPC, leader) | refused by name: `script 129: an NPC crafter is not ready (later slice)`; nothing opens, nothing changes |
| an NPC without a leader, an absent object | nothing | nothing |

No installed script uses action 129.

## Differences from the original

| Original 1.0.7.4 | Steamwoven (both modes) |
| --- | --- |
| Action 129 calls a pointer the game never fills (TC2). | The PC opens its window; an NPC with a PC leader is refused by name until the NPC crafter slice. |
| `T` with another window open: the mode change closes that window and opens the schematics. | The open window owns the keys; `T` acts from the world only. |
| The window does not pause the game. | Modal: the clock pauses and the actor gate closes, as the viewer's other windows. |
| The original window art and layout. | A diagnostic panel (above). |

## Not ready

- **NPC crafter** (the dialog action `su`, pending dialog action 7, and action 129 for an NPC with a PC leader): the original
  takes the components from and puts the products into the NPC's inventory, the PC only sees the completion message (SW7). It
  needs NPC inventory state in the session; refused by name until then.
- The interface button, the window art, the slot pictures and overlays, the item description box on slot hover, the tab and
  discipline hover texts, the close spot.
- Dropping a product on the ground when the inventory cannot take it (the assembly is refused, [assembly](schematic-assembly-runtime.md)).
