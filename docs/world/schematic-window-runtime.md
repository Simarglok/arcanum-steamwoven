# Schematic window runtime

Status: **Implemented bounded component (viewer).** Work items ARC-TECHNOLOGY-005 (the hero's window, slice TECH-5) and
ARC-TECHNOLOGY-006 (the NPC crafter), 2026-10-21, of the
[technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted). The
schematic window in the AREA viewer (`crash-site-area`: exterior, cave and `--area-map`): the hero's, opened with the original key
`T` or by script action 129 for the PC, and an NPC crafter's, opened by the dialog action `su` or action 129 for an NPC with a PC
leader, over the session's [tech degrees](tech-degrees-runtime.md), [found schematics](schematic-knowledge-runtime.md),
[assembly](schematic-assembly-runtime.md) and [NPC inventory changes](inventory-runtime.md#npc-inventory-changes-arc-world-055). No
saved state is added (the snapshot schema is unchanged).

Requirements: [FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012) (schematics),
[FR-ITEM-017](../requirements/items-economy.md#fr-item-017) (learned and found schematics shown),
[FR-ITEM-018](../requirements/items-economy.md#fr-item-018) (assembly).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe` ([ledger "Schematic window"](../research/ledger/technology.md#schematic-window-arc-technology-005),
SW1–SW8, and ["NPC schematic crafter"](../research/ledger/technology.md#npc-schematic-crafter-arc-technology-006), NC1–NC8). Code: [`AreaSchematics`](../../tools/terrain-viewer/Core/AreaSchematics.cs) (the Godot-free page),
[`AreaSchematicsPanel`](../../tools/terrain-viewer/Godot/AreaSchematicsPanel.cs) (the diagnostic panel),
[`TerrainViewerAreaSchematics`](../../tools/terrain-viewer/Godot/TerrainViewerAreaSchematics.cs) (keys, sounds, routing),
[`AreaScriptHost`](../../tools/terrain-viewer/Core/AreaScriptHost.cs) (action 129) and the SCR dispatcher's
[`ScrSchematicRequest`](../../src/Arcanum.Scripting/Scr/ScrDispatchTypes.cs) (admission set S9); tests
[`AreaSchematicsTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaSchematicsTests.cs),
[`AreaScriptHostTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaScriptHostTests.cs),
[`ScrSchematicTests`](../../tests/Arcanum.Scripting.Tests/Scr/ScrSchematicTests.cs), the Godot self-test
`ARC-TECHNOLOGY-005 self-test PASS` (since ARC-TECHNOLOGY-006 with an NPC crafter) and the native smokes
`ARC-TECHNOLOGY-005 native-smoke schematics PASS` and `ARC-TECHNOLOGY-006 native-smoke npc crafter PASS` (the window path `su` takes
after its talk ended, with a real speaker; the reply pick itself is covered by [`AreaSchematicConversationTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaSchematicConversationTests.cs)); the NPC crafter in
[`NpcAssemblyStateTests`](../../tests/Arcanum.Session.Tests/NpcAssemblyStateTests.cs) and
[`DialogSchematicsTests`](../../tests/Arcanum.Scripting.Tests/Dialog/DialogSchematicsTests.cs).

## Opening and closing (SW1–SW3)

- `T` (logical key down) opens the window for the session's hero as crafter and viewer, as the original's in-game key handler
  calls the open with the local PC as both arguments (SW1). It acts from the world only: not while the palette, a dialog, another window or
  UI focus owns input (the original's guards are those of C and L; its mode change would close another open window instead,
  the viewer's modal windows own their keys). `T` is no longer a reserved key in `crash-site-area` with a session.
- The hero's window opens only with a hero, and never for a dead one (the original returns for a dead crafter or viewer); the HUD
  names the refusal (`no hero (start with --hero)`, `refused: the hero is dead`). An NPC crafter's window needs no hero (the viewer is
  the PC, which the diagnostic session may run without one) but refuses a dead hero or NPC, and an NPC without data or one the
  session does not know (`refused: no data of the NPC crafter`).
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
  original keeps them until a game reset; every NPC crafter's open resets them (NC5, below).
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
  (no tile to leave a refused product on, an item type the session does not hold, the inventory limit); a completion whose
  inventory refused some products adds the refusal and how many lie on the ground (ARC-WORLD-070).
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
| an NPC with a PC leader (the original walks its leader chain to the first PC, NC3; the viewer's leader is the direct follow, the same while no NPC leads another) | (NPC, leader) | the NPC crafter's window (below), from the NPC's home-map data |
| an NPC without a leader, an absent object | nothing | nothing |

No installed script uses action 129.

## NPC crafter (ARC-TECHNOLOGY-006)

The original's crafter can be an NPC (ledger NC1–NC7, SW7): the window searches the NPC's own inventory with the NPC's expertise,
assembly consumes the components from the NPC and puts the products into the NPC, and the PC (the viewer) only sees the completion
text and hears its sound. Both modes do the same; any NPC on any map.

- **Openers.** The dialog action `su` on a PC reply (NC1): the conversation's schematic port (`IDialogSchematicPort`) checks that
  neither the NPC nor the hero is dead, the field's other actions run in order, the reply's target node is not entered, and the
  conversation ends at once with no further NPC line (`DialogEndKind.SchematicWindow`; the original's mode change ends the dialog,
  NC2). The viewer then opens the window with that NPC as crafter; closing it returns to the world, the talk does not resume.
  Script action 129 for an NPC with a PC leader opens the same window (the table above). `su` together with JO, UW, FL, ET or an
  insult in one reply is offered blocked, and `su` in an NPC line's own action stops at an unsupported-operation frontier (NC8, F1).
- **Crafter facts.** `SessionNpcCrafter`: the NPC's sheet and scheme from its home-map fields (instance over prototype, the whole
  field, NC4) unless the session keeps an advancement record of it, and the stat changes of its effects there
  (`AreaAdvancement.Npc`). `GameSessionOwner.QueryNpcTechnology` gives its degrees, counting degrees and expertise (effective
  intelligence through those changes). The "expertise in discipline" effects the original applies to every critter are not modelled
  for NPCs (the session keeps no NPC effects).
- **The window.** The learned tab lists the first "degree" records of each discipline by the NPC's stored degrees; the found tab is
  empty (the original builds it for a PC crafter only, NC5); every NPC open resets the tab, discipline and page. The page searches
  the NPC's current list: its seed items still there, then the made items the session added to it
  ([NPC inventory changes](inventory-runtime.md#npc-inventory-changes-arc-world-055)).
- **Assemble.** `GameSessionOwner.AssembleSchematicForNpc` (the hero's rules, TC3/TC4/TC5): both components leave the NPC with the
  durability deletion and the products enter its list as `made:n`, one revision; text 2 and sound 3018 + the discipline. The viewer
  has no NPC weight or room model, so its insert check for the NPC accepts; the session itself refuses a full list or store.

## Differences from the original

| Original 1.0.7.4 | Steamwoven (both modes) |
| --- | --- |
| Action 129 calls a pointer the game never fills (TC2). | The PC opens its window; an NPC with a PC leader opens the NPC crafter's window. |
| A product the NPC cannot take stays on the ground at the crafter, with a floating refusal text, and the assembly still completes (NC7). | The session does the same since ARC-WORLD-070 (the host gives the NPC's tile); the viewer's insert check for an NPC accepts, so its products always enter the list. The status line names a hero's products left on the ground with the refusal (no floating text). |
| An NPC crafter's expertise includes the "expertise in discipline" effects of its effect list (NC4). | Not modelled: the session keeps no NPC effects. |
| `su` in an NPC line's own action also opens the window (NC8, F1). | Not admitted (an unsupported-operation frontier). |
| When the open refuses (a dead NPC or PC), the dialog text was cleared and later picks are ignored (NC2, inferred). | The schematic port refuses and the conversation fails with a rejected command (nothing applied). |
| `T` with another window open: the mode change closes that window and opens the schematics. | The open window owns the keys; `T` acts from the world only. |
| `T` also plays the generic click sound 0. | Only the window's own sounds play. |
| Assemble acts on the button's release. | The mouse button does too; `Tab` and a controller confirm act on their press. |
| The window does not pause the game. | Modal: the clock pauses and the actor gate closes, as the viewer's other windows. |
| The original window art and layout. | A diagnostic panel (above). |

## Not ready

- The interface button, the window art, the slot pictures and overlays, the item description box on slot hover, the tab and
  discipline hover texts, the close spot.
- An NPC's weight, background and room in the viewer's insert check for an NPC crafter; NPC effects on expertise.
