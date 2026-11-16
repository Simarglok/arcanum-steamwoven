# Item identify and repair services (ARC-UI-1731)

Status: **Implemented bounded component**, ARC-UI-1731 (2026-11-13): the two windows the dialog replies `ii` and `ri` open, so a townsperson
identifies or repairs the hero's items for money. A Player reply with `ii` ends the talk and opens the **item identify window** (the original's
inventory window in mode 5); `ri` opens the **item repair window** (mode 6). With the game's art the viewer draws the original look (ARC-UI-1765, ledger IS8–IS11: the hero's own window with the NPC's image and its generated quote line); without it, or behind the switch, a diagnostic list (the hero's items, one quote per line).

- **Rules:** `ItemServiceRules` (the price 100, the identified test of 0x464370, the line ranges, the kept price), `TradeRules.UncheckedSalePrice` (0x461F80
  with the sale check off) and `ItemDurabilityRules.RepairQuote` (0x578760) in [`Arcanum.Simulation.Items`](../../src/Arcanum.Simulation/Items/ItemServiceRules.cs).
- **Session:** `GameSessionOwner.RepairForService` (the item part of the NPC's paid repair, 0x4C8E60: the NPC's Repair level and training, two draws, no fate, the
  forced amount) in [`GameSessionRepair.cs`](../../src/Arcanum.Session/GameSessionRepair.cs); the identify mark is `MarkItemIdentified` and the payment `GiveGoldToNpc`.
- **World:** `AreaItemService` in [`AreaItemService.cs`](../../src/Arcanum.World/AreaItemService.cs): the window's model (the hero's items with their quotes, the original's two clicks,
  the kept price, the payment); the dialog host's window port offers `ii` and `ri` (`AreaConversationHost.TakeItemServiceRequest`).
- **Viewer:** [`AreaItemServicePanel`](../../tools/terrain-viewer/Godot/AreaItemServicePanel.cs) and
  [`TerrainViewerAreaItemService.cs`](../../tools/terrain-viewer/Godot/TerrainViewerAreaItemService.cs); the windows are the interface modes `InventoryKind5` and `InventoryKind6`
  ([interface modes](../architecture/interface-modes.md)).
- **Requirements:** [FR-DIALOG-004](../requirements/story-social-ai.md#fr-dialog-004), [FR-ITEM-008](../requirements/items-economy.md#fr-item-008),
  [FR-ITEM-011](../requirements/items-economy.md#fr-item-011), [FR-UI-004](../requirements/ui-media-lifecycle.md#fr-ui-004).
- **Evidence:** rows IS1–IS7 of the [verification ledger](../research/ledger/items.md#item-identify-and-repair-windows-arc-ui-1731), the repair rules DU7, DU8, RP1–RP5, TC7.

## Flow (ledger IS1–IS3)

1. The reply's action ends the talk (`DialogEndKind.ItemIdentifyWindow` or `ItemRepairWindow`); the host names the window and the speaker once; the talk does not resume.
2. The viewer opens the window when nothing else owns input and the barter data is loaded (the gold pile kind and the item prototypes are the barter's data). A hero that is not alive
   refuses it; the window closes with a replaced session, a dead hero or a map change.
3. **First click on an item** selects it and quotes it (the original quotes the item under the pointer and pays when its handler sees the same item again; which pointer event that is was not settled, ledger IS3, so the two clicks are a viewer substitute for the missing hover): identify costs 100 (an identified item is refused: "already identified"); repair costs `trunc(P / 2) × damage / maximum`,
   at least 2, where P is the hero's sale price of the item to the NPC with the sale check off (the NPC's multiplier, the hero's haggle, the damage scaling of TC7), a broken item only
   from a master of Repair, an undamaged item "nothing to repair".
4. **Click on the selected item again** quotes it again and pays: a hero with fewer coins than the kept price gets the "no money" line; else identify marks the item identified, repair
   runs the NPC's forced Repair skill on the item (`RepairForService`), and the hero's coins move to the NPC. The kept price is then cleared and the item stays selected.

## Rule settings

- **Stale price (IS5).** The original keeps the last quoted price across a refused quote: after quoting A, quoting a refused B and the next event on B the hero pays A's price for nothing.
  The Original setting keeps that (`ItemRuleProfile.ServiceKeepsStalePrice`); Steamwoven clears the price at a refused quote (owner rule 2026-10-28 for gameplay slips).
- **Repair price (TC7).** The existing switch `RepairQuoteCountsDamageTwice` decides the damage scaling of the quote.

## Not modelled

- The portrait clash rule of 0x4CEB80 ([#2444](https://github.com/Simarglok/arcanum-steamwoven/issues/2444)), the hover event of IS3 (the viewer's press is a substitute), the NPC's name (IS8: not drawn by the original in these modes either).
- ARC-UI-1908: the NPC's image follows the portrait rule of 0x553D10 (IW27): an NPC the hero leads (the leader chain reaches it, `QueryPcLeader`) that stores a portrait is drawn with it, 32 by 32 by 0x4CE680 at the image's corner (the art stays halved); the loot and barter windows draw it at its own size (`AreaInventoryView.HolderPortrait`, `AreaLootArtLayout.HolderPortrait`).
- The network branches (a client sends a request), the item refresh and the round view.
- The repairer's own injuries (blindness, crippled arms): the effectiveness is read before the difficulty and the run is forced, so only the level and training count (RP5).
- A payment the session refuses after the service ran (a full record store) leaves the service done, as the original leaves a moved item whose payment failed.
