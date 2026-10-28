# Barter runtime (ARC-UI-050)

Status: **Implemented bounded component**, ARC-UI-050 (2026-10-28), the barter window over the session's
[NPC gold and transfers](inventory-runtime.md#npc-gold-and-transfers-arc-world-071) (ARC-WORLD-071), and with it the price and trade
gates of task S13 (haggle) of the [skills plan](../research/skills-plan.md). A dialog reply `b:` ends the talk and opens the window
with the speaker as merchant; the hero sells its items and buys the merchant's for the original's prices, and a generous trade moves a
loving merchant's reaction. Rules: `TradeRules` in [`Arcanum.Simulation.Items`](../../src/Arcanum.Simulation/Items/TradeRules.cs); buy
lists: `InventorySourceBuyRules` in [`Arcanum.Data.Characters`](../../src/Arcanum.Data/Characters/InventorySourceBuyRules.cs) and the
merchant's price multiplier (NPC field 0x125, `CombatNpcFields.RetailPriceMultiplier`); session: `BarterBuy`, `BarterSell` and
`QueryHeroHaggle` in [`GameSessionBarter.cs`](../../src/Arcanum.Session/GameSessionBarter.cs); dialog: the `b:` reply through
`IDialogBarterPort` ([dialog runtime](../scripting/dialog-conversation-runtime.md)); viewer: [barter window](../rendering/terrain-viewer.md#barter-window-arc-ui-050).
No snapshot schema change: a trade is a move of items and coins the inventory section already saves.
Requirements: [FR-ITEM-013](../requirements/items-economy.md#fr-item-013) (trade, price and payment),
[FR-UI-003](../requirements/ui-media-lifecycle.md#fr-ui-003) (dialog → trade), [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019)
(haggle used).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe`, read statically per the [original executable check](../research/original-executable-check.md)
  by two independent readers: rows BT1–BT10 of the [verification ledger](../research/ledger/items.md#barter-arc-ui-050). They agree on
  every row. They correct NI6 and NT4 (the restock timer is set when the window opens, not when it closes), R9 (the gift helper's
  parties; flag 0x08 is the won gamble), DU5, DU6 and the haggle summary of the skills evidence (the multiplier step, the buyer's
  script, the fence), and the "below the count" set range of the starting-hero reading (the last set is reachable).
- The function oracle ran the price 0x461F80 on 10,000 seeded inputs with its worth, identified test, sale and buy checks and reaction
  scale as they are: no difference, six planted mutants caught (F3, [receipt](../verification/receipts/461F80.json)).

## Rules

| Rule | Behaviour (original) | API |
| --- | --- | --- |
| Sale price (BT1) | Coins have no price. From the worth W (300 unidentified, else the stored worth, at least 2): when the merchant's multiplier m is above 100, `W × (100 + trunc(3 × (100 − m) / 8)) / 100`; then the PC's haggle share `× (trunc(H / 2) + 50) / 100` (at most 97 %); a damaged item `× current / maximum`; a negative price is 0 and 1 is 2. 32-bit products, truncating divisions. | `TradeRules.Price` |
| Buy price (BT1) | `W + (100 − H) × m × W / 10000` with the triple product in 32 bits, then the reaction scale, at least `W + 1` (1 or 2 become 3, dead for every worth the original produces). No damage scaling. | `TradeRules.Price` |
| Reaction scale (BT2) | A reaction above 100 counts as 100; × 200 % below 0, `(200 − 2r) %` below 50, else `(120 − trunc(2r / 5)) %` (100 % at 50, 80 % at 100). Only the buy price reads it. | `TradeRules.ReactionScale` |
| Sale check (BT3) | In order: a stored worth of 0; a broken item; below Expert haggle the merchant's buy-object script (point 17, on the merchant, the item as triggerer) and then its buy list; a stolen item (flag 0x8000) unless the merchant is a fence (NPC flag 0x4000), at every training. Worn, no-drop and quest items are not tested. | `TradeRules.SaleCheck` |
| Buy check (BT3) | A Master of haggle buys anything; else a "won't sell" item (flag 0x2), then a worn one (location 1000..1008), is refused. | `TradeRules.BuyCheck` |
| Buy list (BT8) | The merchant's inventory source (NPC field 0xEF) names its line of `rules\InvenSourceBuy.mes` (message number = set number): a first token `ALL` in any case buys everything; otherwise up to 300 space-separated basic prototype numbers (read with `atoi`, each in the original's ranges with a prototype). A failing line, or a failing loot line of the set, empties the set; a set without a line buys nothing. | `InventorySourceBuyRules`, `InventorySourceBuyList.Deals` |
| Trade (BT5) | A quote of 0 cancels the trade (outside a follower's window). A buy needs the hero's gold for the unit price ("not enough money"); a sale pays at most the merchant's own coins (a merchant without coins takes the item for nothing). The item moves first, the coins only after the move succeeded, the whole stack at the unit price × units. | `AreaBarter.Buy`/`Sell`, `TradeRules.SalePayment`, `BarterBuy`/`BarterSell` |
| Reaction (BT6) | After a paid trade the merchant's surplus (the price above the worth on a buy, the worth above the price on a sale) gives a merchant who loves the PC (reaction above 100) `surplus / 40` below 200, `5 + (surplus − 200) / 100` below 1000, else 15, when the surplus is above 50; a price of 0 changes nothing. | `TradeRules.GiftAmount`, `TradeRules.GiftReaction`, `AdjustReaction` |
| Follower (BT6) | A follower's window (the NPC's leader is the PC) has no prices: items move both ways for nothing, with no reaction change. | `AreaBarterMerchant.Follower` |
| Dialog (BT7) | A `b:` reply runs its action, then ends the dialog (not suspends it) and opens the window with the speaker; its response value is not followed. The open refuses only a dead or missing PC; nothing checks the merchant. | `DialogOptionTransition.Barter`, `DialogEndKind.BarterWindow` |

## Session

`BarterBuy(expected, npc, item, count, unitPrice, newStackKind, pileKind, heroInsert, npcInsert, scripts)` moves a whole item (count 1) through
`TakeItemFromNpc`, or units of an ammunition stack through `TakeAmmoFromNpc`, then pays `count × unitPrice` through `GiveGoldToNpc`.
`BarterSell(expected, npc, item, count, unitPrice, newStackKind, npcInsert)` moves the hero's item through `GiveItemToNpc` or
`GiveAmmoToNpc`, then takes the coins through `TakeGoldFromNpc`. Before any effect each refuses a negative price, a count other than 1
for an item, a total past 32 bits, a payer with fewer coins than the total (`GoldShort`: the original's "not enough money" before the
move, and its rolled-back stack payment after it), and the payment refusals that do not depend on the move (a pile past 32 bits, a full
record budget, a full NPC item budget or list, a transfer script or insert-item script on the pile); the buy's item move runs the
item's insert-item script through the host, as a loot does. A payment refused after the move (the host's insert check of a new pile for
the merchant, an exhausted made number, or coins an insert-item script spent) leaves the item moved and reports the payment's failure,
as the original leaves a moved item whose payment failed. The NPC list and budget checks for a new pile run before the move, so a
merchant at exactly its list limit is refused one item early. `QueryHeroHaggle()` gives the hero's haggle effectiveness and training. Tests:
[`BarterStateTests`](../../tests/Arcanum.Session.Tests/BarterStateTests.cs), [`TradeRulesTests`](../../tests/Arcanum.Simulation.Tests/Items/TradeRulesTests.cs),
[`TradeOriginalExeTests`](../../tests/Arcanum.Simulation.Tests/Items/TradeOriginalExeTests.cs) (oracle),
[`InventorySourceBuyRulesTests`](../../tests/Arcanum.Data.Tests/Characters/InventorySourceBuyRulesTests.cs),
[`DialogBarterTests`](../../tests/Arcanum.Scripting.Tests/Dialog/DialogBarterTests.cs), [`AreaBarterTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaBarterTests.cs).

## Not modelled (NOT READY)

- **Substitute container** (NPC field 0x126, used within 20 tiles by an NPC without a leader, BT4): the original shows and fills the
  container instead of the merchant's own list, moves the container's coins onto the merchant at the open, and checks a buy list through
  the container's own inventory source when the merchant's does not deal. Installed merchants keep their stock this way (the census of
  `AreaBarterTests`), so they show only their own list here and buy nothing below Expert haggle. With FR-ITEM-014 (stock and restock).
- **Identified flag**: the original identifies the merchant's goods at the open and marks a sold item identified (BT4, BT5); the session
  keeps no per-item identified state, so the window prices the merchant's goods as identified and the hero's items by their data.
- **Restock**: the open (re)schedules the shown inventory's regeneration 12–24 game hours later (BT4); FR-ITEM-014.
- **Buy-object script** (point 17): not run; a merchant that has one refuses every sale below Expert haggle. The original runs it at
  every sale quote (twice below Expert), so its side effects repeat on hover.
- The starting-hero loot reader (`InventorySourceRules`, ARC-WORLD-015) keeps its earlier reading: it treats the last set as out of
  reach and refuses any set with a buy line; BT9 corrects the range (follow-up for that reader, not changed here).
- **Count dialog** for stacks: a stack trades whole. **Quote and refusal lines** (`gd_npc_*` 800–1499 and 4600, "not enough money" from
  `gd_cls_*`): the window prints its own text. **Gambling** (FR-ITEM-019): the window has no gamble slot.
- **Paperdoll**: the merchant's worn items are not offered (the original reaches them only through its paperdoll); the follower menu and
  follower cycling open nothing.
- The uninitialised "buys everything" flag of a set without a buy line and of an out-of-range source (BT8): read as 0 (buys nothing).
