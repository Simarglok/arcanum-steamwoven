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
ARC-WORLD-090 (merchant stock, first slice) adds the merchant's **substitute container** (NPC field 0x126): the window shows and fills the
container, its coins move onto the merchant at the open, the dealer test tries the container's buy list second, and the open identifies the
merchant's and the container's goods (snapshot schema 50: the identified items and the containers' added lists). ARC-UI-050 itself changed
no schema: a trade is a move of items and coins the inventory section already saves.
Requirements: [FR-ITEM-013](../requirements/items-economy.md#fr-item-013) (trade, price and payment),
[FR-ITEM-014](../requirements/items-economy.md#fr-item-014) (stock: the substitute container; restock not yet),
[FR-UI-003](../requirements/ui-media-lifecycle.md#fr-ui-003) (dialog → trade), [FR-CHAR-019](../requirements/characters-skills.md#fr-char-019)
(haggle used).

## Evidence

- The retail 1.0.7.4 `Arcanum.exe`, read statically per the [original executable check](../research/original-executable-check.md)
  by two independent readers: rows BT1–BT10 of the [verification ledger](../research/ledger/items.md#barter-arc-ui-050). They agree on
  every row. They correct NI6 and NT4 (the restock timer is set when the window opens, not when it closes), R9 (the gift helper's
  parties; flag 0x08 is the won gamble), DU5, DU6 and the haggle summary of the skills evidence (the multiplier step, the buyer's
  script, the fence), and the "below the count" set range of the starting-hero reading (the last set is reachable).
- ARC-WORLD-090 read the open's stock part in the executable: row BT11 of the
  [ledger](../research/ledger/items.md#merchant-stock-arc-world-090) (the substitute gate 0x45F650, the gold loop of 0x572640 through
  0x464830, the identification 0x4643D0).
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
| Substitute container (BT4, BT11) | An NPC with no leader whose field 0x126 names a container within 20 tiles (Chebyshev) shows and fills that container instead of its own list; a follower or a leader-led NPC has none. | `AreaInventoryMap.Substitute`, `AreaBarterMerchant.WithSubstitute` |
| Open stock (BT11) | At every open the container's gold piles move onto the merchant in list order (each destroyed, the merchant's first pile grows; a merchant without one gets a new pile), then, without a PC leader, every entry of the merchant's and the container's lists gets the identified flag 0x1 (worn and gold included). | `GameSessionOwner.OpenBarter`, `AreaBarter.Open` |
| Container trades (BT5) | A bought item comes out of the container, a sold item goes into it (ammunition joins its first stack of the type, which Original never finds: a new stack, AMA5); the coins are paid to and by the merchant, never the container. A sale of a whole item outside a follower's window marks it identified (units of ammunition are not marked: no complexity, no effect). | `BarterBuy`/`BarterSell` with `container`, `identify` |
| Dealer test (BT8) | The merchant's own buy list, else its substitute container's (the container's inventory source, field 0x3D). | `AreaBarterMerchant.Deals` |
| Identified (BT4, WB7) | The identified test (0x464370) reads the stored flag or the session's; the worth of an unidentified magickal item is 300. | `QueryItemIdentified`, `AreaBarter` lines |
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
merchant at exactly its list limit is refused one item early. `QueryHeroHaggle()` gives the hero's haggle effectiveness and training.

ARC-WORLD-090: `OpenBarter(expected, npc, container, identify, pileKind, npcInsert)` is the open's session part, one revision (no change when
nothing moves or is marked): the merchant must be an NPC of the actor's map seed and the container a container of it that is not spilled.
Its refusals come before any effect: a pile with an insert-item script, a merchant pile past 32 bits, a full pile, durability or identified
budget (`SessionInventoryState.MaxIdentified`, 16384), and the new pile's insert check (the original leaves a refused pile on the ground).
`BarterBuy(…, container)` takes the item from the container's current list (its seed items and the items sales put there) through the
loot core (`Take`), or units of one of its stacks; `BarterSell(…, container, identify)` puts the item into the container's added list (the
NPC store, marked as a container in the snapshot: `inventory.npcs[].container`), `identify` marking it in the move's revision.
`QueryItemIdentified(item)` answers the session's flag. A seed that lists a filled container as an NPC (or the reverse) is another map's
data (`InventoryMismatch`); a deleted item loses its flag. Snapshot schema 50 adds `inventory.identified` (entities in ordinal order) and
the container flag of each added list. Tests: [`MerchantStockStateTests`](../../tests/Arcanum.Session.Tests/MerchantStockStateTests.cs),
[`BarterStateTests`](../../tests/Arcanum.Session.Tests/BarterStateTests.cs), [`TradeRulesTests`](../../tests/Arcanum.Simulation.Tests/Items/TradeRulesTests.cs),
[`TradeOriginalExeTests`](../../tests/Arcanum.Simulation.Tests/Items/TradeOriginalExeTests.cs) (oracle),
[`InventorySourceBuyRulesTests`](../../tests/Arcanum.Data.Tests/Characters/InventorySourceBuyRulesTests.cs),
[`DialogBarterTests`](../../tests/Arcanum.Scripting.Tests/Dialog/DialogBarterTests.cs), [`AreaBarterTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaBarterTests.cs).

## Restock rules (ARC-WORLD-093)

`Arcanum.Simulation.Items.RestockRules` holds the original's restock event as pure rules (ledger
[RK1–RK8](../research/ledger/items.md#merchant-restock-arc-world-093), one reading, F1); the session runs it as an event
([below](#restock-event-arc-world-094)); the viewer comes in a later slice.

| API | Original | Rule |
| --- | --- | --- |
| `InventorySource(holder)` | 0x4641A0 | An NPC's or a container's source; 0 for any other object (a PC included). |
| `Delay(draw, multiplayer)` | 0x4640C0 | The draw of 43,200,000..86,400,000 game ms (12–24 hours), a quarter in multiplayer. Only an object with a source is scheduled; the new event replaces its pending one (the caller's). The window open schedules the shown object in every mode (RK2). |
| `Fire(holder, merchant)` | 0x464180 → 0x463C60 | `RestockDecision`: nothing without a source; an NPC regenerates unless dead or with a PC leader; a container with flag 0x400 regenerates; on screen it is `Reschedule`d; off screen its merchant (the host's: the first NPC of the game-window-sized area whose substitute it is) decides: dead, PC leader or none is nothing, else `RegenerateMerchantAndContainer` (the merchant only with a source of its own). |
| `Keeps(itemFlags)` | 0x463B30 (keep 1) | Only an item with the persistent flag 0x2000 stays; every other entry, worn and gold included, is destroyed. |
| `Generate(set, rolls)` | 0x463E20 | `RestockLoot`: one coin draw in the set's range (reversed bounds swapped; a new pile only above 0), then one 1..100 draw per line in order, creating the line's item when the draw is at most its rate. Creation, insert (a refused item is destroyed) and an NPC's best-items wield are the caller's. |

The four installed merchants (none trades from its own source, `AreaBarterTests`) have no source of their own and their containers neither restock flag, so they restock only off screen
through their living merchant (census in the ledger). `InventorySourceRules` (the loot reader) now takes sets 1..count (BT9) and reads a
set with a buy line unless the buy or loot line fails, which empties the set in the source (`Emptied`; BT8, BT9). Tests:
[`RestockRulesTests`](../../tests/Arcanum.Simulation.Tests/Items/RestockRulesTests.cs),
[`AreaRestockCensusTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaRestockCensusTests.cs) (opt-in census).

## Restock event (ARC-WORLD-094)

The session runs the restock event (`SessionEventKinds.Restock`, `inventory.restock`; [event queue](game-event-queue-runtime.md))
through the rules above. Code: `GameSessionRestock.cs`; tests:
[`RestockStateTests`](../../tests/Arcanum.Session.Tests/RestockStateTests.cs).

| Step | Original | Session |
| --- | --- | --- |
| Schedule | 0x4640C0 at every window open (0x5733A9, RK2) | `ScheduleRestock(expected, holder, hasSource)` on the actor's map: with a source (the host's, RK1) the holder's pending event is replaced by one after a labelled `restock.delay` roll of 12–24 game hours (one revision); without one the pending event stays (`NoChange`). The multiplayer quarter delay is not modelled. |
| Firing | 0x464180 → 0x463C60 (RK3) | The `ISessionRestockWorld` host gives the holder's facts (`RestockFacts`: kind, source, dead, PC leader, container flags, on screen) and, for an off-screen container without flag 0x400, its merchant (`RestockMerchant`: the first NPC of the game-window-sized area whose substitute it is) and that merchant's facts; `RestockRules.Fire` decides. `Reschedule` sets a new event (a new roll); `None` sets none; a missing holder does nothing. |
| Clear | 0x463B30 keep 1 (RK5) | Every entry of the holder's current list (its seed items still there and its added items; worn and gold included) is deleted unless the host's `Persistent` says it carries flag 0x2000. A seed item gets the deleted-item record (it hides it from the seed); a made item leaves none and its number is kept as `lastDiscardedMade` (schema 51), so restocks do not fill the deleted-item budget and a new made item never takes a discarded number. |
| Generation | 0x463E20 (RK4, RK6) | `RestockRules.Generate` with labelled session rolls `restock.coins` and `restock.line`. Coins above 0 become a new `made:n` gold pile of `RestockPileKind` (a persistent pile that stayed takes them instead); each created line is `RestockItem(basic prototype)` made as a `made:n` item at the end of the list after the host's `RestockInsertCheck` (refused: destroyed, counted as `Refused`). Ammunition joins the holder's stack as the insertion merges it (an NPC's stack of the prototype; a container's by the type lookup, which follows AMA5). |
| Report | — | `SessionEventPassResult.Restocks`: the decision and each regeneration (`SessionRestockRegeneration`: outcome, removed, coins and pile, created items, refused and skipped counts), the merchant's first, then the container's; all of one firing in one revision. |

Decisions of this slice for sets (`SessionRestockSet`, the host's reading of `InventorySourceRules`):

- `Read`: a parsed set generates; a set the load emptied (`Emptied`) is given as the coin pair its loot line read and no lines, so it
  still generates its coins (RK6).
- `Unreachable`: a set outside 1..count (RK7), or one the load emptied before its coin pair, would give an undefined coin count: the
  list is cleared and nothing is generated, in both modes (technical bug, owner rule 2026-10-28).
- `Unsupported`: the shapes the project refuses (`Malformed`, `InvalidText`; 39 of the 123 installed sets are `Malformed`) are read
  partially by the original; the holder keeps its list untouched (`SessionRestockOutcome.Unsupported`, not modelled). So are a holder
  the map's seed does not list as that kind and a spilled container.

The session postpones a firing a minute (`SessionRestockFiring.Postponed`) when the pass has no restock host, the holder's map seed
is not attached, or the deleted-item budget cannot hold the clear's records.

## Not modelled (NOT READY)

- **Restock in play**: the session event exists (ARC-WORLD-094, above), but the viewer does not schedule it at its window opens yet and
  has no restock host (screen test, merchant search, persistent flag, set reading, `AreaEventWorld` port): next slice. The first-open
  generation of containers with flag 0x200 (RK4, RK8; 116 installed), an NPC's best-items wield after a regeneration (0x4654F0) and the
  other callers of 0x463E20 and 0x463C60 (RK8) are not modelled. Lines whose prototype is gold, a key or a key ring are skipped (the
  session's lists do not admit them), as are lines the host has no prototype for, lines into a holder with a transfer script and
  lines past a full budget (`SessionRestockRegeneration.Skipped`).
- **Substitute container limits** (ARC-WORLD-090): the host finds the container from the T0 data (the NPC's leader handle and field
  0x126) and measures the 20 tiles between the T0 positions of the speaker and the container (the original measures where they are now);
  a leader the session gave the NPC other than the hero is not seen. A container's coins move only at a barter open (no other container
  gold move is modelled). The items sales put into a container are not part of its spill when an attack breaks it: they stay in the
  session's store, shown nowhere (not modelled). A broken container is no substitute: the open refuses it and the viewer opens the
  merchant's own list instead, as the original finds no container object. Other refused opens (a container pile with an insert-item
  script, a merchant without a pile that has a transfer script, a full budget) show no window. A container gold pile of 0 coins stays in the container: the
  original's open loops on it for ever (BT11, a hang: a technical bug, fixed in both modes without a switch by the owner rule of
  2026-10-28, [rule modes](rule-modes.md)).
- **Identified readers**: only the barter window reads the session's identified flag; the best-items choice, the AI item lists and the
  script condition `is identified` still read the stored flag.
- **Buy-object script** (point 17): not run; a merchant that has one refuses every sale below Expert haggle. The original runs it at
  every sale quote (twice below Expert), so its side effects repeat on hover.
- **Count dialog** for stacks: a stack trades whole. **Quote and refusal lines** (`gd_npc_*` 800–1499 and 4600, "not enough money" from
  `gd_cls_*`): the window prints its own text. **Gambling** (FR-ITEM-019): the window has no gamble slot.
- **Paperdoll**: the merchant's worn items are not offered (the original reaches them only through its paperdoll); the follower menu and
  follower cycling open nothing.
- The uninitialised "buys everything" flag of a set without a buy line and of an out-of-range source (BT8): read as 0 (buys nothing).
