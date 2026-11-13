# Barter runtime (ARC-UI-050)

Status: **Implemented bounded component**, ARC-UI-050 (2026-10-28), the barter window over the session's
[NPC gold and transfers](inventory-runtime.md#npc-gold-and-transfers-arc-world-071) (ARC-WORLD-071), and with it the price and trade
gates of task S13 (haggle) of the skills plan. A dialog reply `b:` ends the talk and opens the window
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
[FR-ITEM-014](../requirements/items-economy.md#fr-item-014) (stock: the substitute container; restock in the session and the viewer),
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
([below](#restock-event-arc-world-094)) and the AREA viewer schedules and hosts it ([below](#restock-in-the-viewer-arc-world-096)).

| API | Original | Rule |
| --- | --- | --- |
| `InventorySource(holder)` | 0x4641A0 | An NPC's or a container's source; 0 for any other object (a PC included). |
| `Delay(draw, multiplayer)` | 0x4640C0 | The draw of 43,200,000..86,400,000 game ms (12–24 hours), a quarter in multiplayer. Only an object with a source is scheduled; the new event replaces its pending one (the caller's). The window open schedules the shown object in every mode (RK2). The original's range draw is one 15-bit value modulo the width, so its real delay is 12 hours plus at most 32,767 ms; the Original setting reproduces it, Steamwoven keeps 12–24 hours (`ItemRuleProfile.RestockDelayIsNarrow`, VPG5, owner decision 2026-11-12). |
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
| Schedule | 0x4640C0 at every window open (0x5733A9, RK2) | `ScheduleRestock(expected, holder, hasSource)` on the actor's map: with a source (the host's, RK1) the holder's pending event is replaced by one after a labelled `restock.delay` roll of 12–24 game hours (Steamwoven; 12 hours plus 0..32,767 ms under the Original setting, VPG5) (one revision); without one the pending event stays (`NoChange`). The multiplayer quarter delay is not modelled. |
| Firing | 0x464180 → 0x463C60 (RK3) | The `ISessionRestockWorld` host gives the holder's facts (`RestockFacts`: kind, source, dead, PC leader, container flags, on screen) and, for an off-screen container without flag 0x400, its merchant (`RestockMerchant`: the first NPC of the game-window-sized area whose substitute it is) and that merchant's facts; `RestockRules.Fire` decides. `Reschedule` sets a new event (a new roll); `None` sets none; a missing holder does nothing. |
| Clear | 0x463B30 keep 1 (RK5) | Every entry of the holder's current list (its seed items still there and its added items; worn and gold included) is deleted unless the host's `Persistent` says it carries flag 0x2000. A seed item gets the deleted-item record (it hides it from the seed); a made item leaves none and its number is kept as `lastDiscardedMade` (schema 51), so restocks do not fill the deleted-item budget and a new made item never takes a discarded number. |
| Generation | 0x463E20 (RK4, RK6) | `RestockRules.Generate` with labelled session rolls `restock.coins` and `restock.line`. Coins above 0 become a new `made:n` gold pile of `RestockPileKind` (a persistent pile that stayed takes them instead); each created line is `RestockItem(basic prototype)` made as a `made:n` item at the end of the list after the host's `RestockInsertCheck` (refused: destroyed, counted as `Refused`). Ammunition joins the holder's stack as the insertion merges it (an NPC's stack of the prototype; a container's by the type lookup, which follows AMA5). |
| Report | — | `SessionEventPassResult.Restocks`: the decision and each regeneration (`SessionRestockRegeneration`: outcome, removed, coins and pile, created items, refused and skipped counts), the merchant's first, then the container's; all of one firing in one revision. |

Decisions of this slice for sets (`SessionRestockSet`, the host's reading of `InventorySourceRules`):

- `Read`: a parsed set generates; a set the load emptied (`Emptied`) is given as the coin pair its loot line read and no lines, so it
  still generates its coins (RK6).
- `Unreachable`: a set outside 1..count (RK7), or one the load emptied before its coin pair, would give an undefined coin count: the
  list is cleared and nothing is generated, in both modes (technical bug, owner rule 2026-10-28).
- `Unsupported`: the shapes the project refuses (`Malformed`, `InvalidText`; since ARC-DEV-161 only 1 of the 123 installed sets, set 94, is
  `Malformed`) are read partially by the original; the holder keeps its list untouched (`SessionRestockOutcome.Unsupported`, not modelled). So do a holder
  the map's seed does not list as that kind, a spilled container, a holder with a transfer script (the session does not run it, so
  every created item would be refused after the clear) and a container with flag 0x200 with a source whose seed facts the host
  cannot give (ARC-WORLD-100: with them it draws seeded and is recorded as generated, [below](#session-generation-arc-world-100)).

ARC-DEV-161 (B40, the empty Crash Site chest): `InventorySourceRules.Read` reads a pair as the loot-line parse does (0x4BF7B0, `strtok` on `:`, `,`
and a space, ledger BT9), so the installed shapes it used to refuse as `Malformed` and the original reads whole are read: a negative coin minimum
(the treasure, monster and bandit sets, `-100,250`; the coin draw is between the pair and only a positive count gives coins, RK6), a space after a
comma (`80, 6167`) and one trailing comma on the right side of a pair (`40,6075,`). Both modes read them: the original does, so there is no
switch. Set 69 (the Crash Site chest 4453, prototype 3055) used to leave the chest empty (`generation=NoChange/Unsupported`) for every hero. A side
that `atoi` reads only in part (a letter, a second sign, text after the digits, two commas) stays `Malformed`; set 94 (`Elven Trader`, a number
without its comma, which the original reads shifted by one) is the one installed set left.

The session postpones a firing a minute (`SessionRestockFiring.Postponed`) when the pass has no restock host, the holder's map seed
is not attached, the deleted-item budget cannot hold the clear's records, or the session-wide added-item, pile or stack budgets could
not hold every line and a pile after the clear, or (ARC-WORLD-100) the generated-container budget (4096) is full; nothing is cleared then. A postponement or a reschedule whose new event is refused
(a full event budget) reports it in `SessionRestockFiring.ScheduleFailure`. With a host that checks objects (`ISessionScriptWorld`)
a restock event of a missing holder is dropped by the queue, which is the same "nothing" as RK3.

## Restock in the viewer (ARC-WORLD-096)

The AREA viewer (exterior, cave and `--area-map`) schedules the event and answers the session's restock questions. Code:
[`AreaRestock.cs`](../../src/Arcanum.World/AreaRestock.cs) (`AreaRestockHost`, `AreaPixelRect`, `IAreaRestockScreen`),
`AreaEventWorld` (the `ISessionRestockWorld` port), `TerrainViewerAreaRestock.cs`; tests:
[`AreaRestockHostTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaRestockHostTests.cs). The executable was read again for the shown
object and the merchant search: ledger [RK9](../research/ledger/items.md#merchant-restock-arc-world-093) (one reading, F1).

| Part | Original | Viewer |
| --- | --- | --- |
| Schedule | 0x5733A9 schedules the shown object 0x6813A8 at every open; in the barter mode 0x573042 sets it to the merchant's substitute container, or to the merchant when it has none (RK9) | `AreaRestockHost.Schedule` after every barter open the session accepted (the window's container, else the merchant) and every loot open (the looted holder), with the seed holder's T0 inventory source as `hasSource`; a junk pile or another object that is no seed holder of the actor's map asks nothing. Log `ARC-WORLD-096 restock schedule`. |
| Holder facts | 0x4641A0, 0x45D8D0, 0x45DDA0, field 0x38 (RK1, RK3) | The seed holder's kind and its T0 source and container flags (`AreaInventoryMap.RestockSeed`), death (`QueryNpcDead`) and a PC leader (`QueryCompanion`) from the session. |
| Screen test | 0x43DFD0 against 0x5E8808..0x5E8814, strict on every side (RK3) | The union of the container's drawn T0 body rectangles (`TerrainCanvas.AreaBodyRect`) against the game window: the viewport's visible rectangle through the canvas transform, in area pixels (what the viewer shows at its zoom). A container of a map that is not shown is off screen. |
| Merchant search | 0x440FC0 with the NPC mask, then 0x45F650 per NPC (RK3, RK9) | The tile rectangle that holds the tiles under the four corners of a window-sized screen rectangle centred on the container's tile (`AreaRestockHost.Vicinity`; about 26 × 26 tiles for 800 × 600 pixels; the window is the viewport's size at zoom 1, so a display zoom does not change it), the seed's NPCs in their T0 order and at their T0 cells, the first whose substitute container (T0 leader none and field 0x126, not led by the hero in the session, within 20 tiles) is the container. |
| Persistent flag | item field 0x57, flag 0x2000 (RK5) | The seed item's flags (`AreaInventoryMap.Persistent`); an item the session made has none. |
| Sets and items | 0x4BF5D0, 0x4ED8B0 (RK6, RK7) | `InventorySourceRules.Read` over the barter data: `Parsed` reads, `Emptied` reads the coin pair the loot-line parse read before it failed (the loot line is parsed before the buy line, BT8; `InventorySourceRules.LootCoins` over `InventorySourceBuyRules.LootLineCoins`: each side through `atoi`; a parse that failed before the pair is `Unreachable`, an overflowing side `Unsupported`), `Unreachable` stays, `Malformed` and `InvalidText` are `Unsupported`; a line's item is the prototype (basic + 20) the barter data read, with its type, name, description and an ammunition prototype's type and units; the pile is gold prototype 9076. |
| Insert check | 0x466510 | The viewer's diagnostic check (every item fits), as the barter window's. |

The host is ready once the barter data (the inventory sources and item prototypes) has loaded; before that a due event waits a minute
(`ISessionRestockWorld.RestockReady`). When the data failed to load the host has none: every set is `Unsupported` (the lists stay) and
no line's item is known, so the events still run and end. A pass through the cast host (`SessionSpellCaster.ProcessEvents`, while the viewer has one) wraps
the event world; the session asks the wrapped world for the restock host and the loot window (the latter was lost behind the cast host
before, so an item's decay did not wait for an open junk pile then). A restock that fired prints `ARC-WORLD-096 restock fired` with the
step and each regeneration's counts, and the inventory views are refreshed.

Native smoke (stage `barter`, `--area-map "wheel clan" --hero dwarf-male`, first observation 2026-10-28): the barter open scheduled the
merchant's container (due in about 19 game hours); a jump to the event with the container on screen scheduled it again; with the camera
panned until the container was off screen the next event found the merchant (no source of its own) and regenerated the container: 28
items removed, 934 coins and 26 new wares (none refused or skipped); the window opened again with 26 wares and the merchant's gold
631 → 1565 (the container's new coins moved at the open). Marker `ARC-WORLD-096 native-smoke restock PASS`.

## Generated-once containers (ARC-WORLD-098)

A container with flag 0x200 (116 installed, all with a source) is generated once, the first time something reaches its whole list,
from draws the original makes reproducible (ledger [RK10–RK14](../research/ledger/items.md#generated-once-containers-arc-world-098),
one reading, F1). Simulation holds the rules; the session runs them (ARC-WORLD-100, [below](#session-generation-arc-world-100)); the
viewer's container loot window calls the first-open generation (ARC-WORLD-101, [below](#container-loot-window-arc-world-101)).

| API | Original | Rule |
| --- | --- | --- |
| `RestockRules.GeneratedOnceSeed(pc, locationX)` | 0x463E20 (seeded branch), 0x40DA50, 0x4B0490, 0x4C2690, 0x441B60 | The container's tile x, plus with a PC race × background + gender and the products of the player name's byte pairs as signed chars (the name ends at its first zero; an odd last byte pairs with the zero); 32-bit wrapping. `RestockSeedPc` carries race (stat 27) and gender (stat 26) as the stat getter gives them, the background (field 0x101) and the name bytes (field 0x10E). |
| `OriginalRandom` | 0x4CEF40, 0x4CEF50, 0x4CEF00 | The original's one generator: state × 0x19660D + 0x3C6EF35F, a draw is bits 8..22 (0..32767); `Between` draws nothing for equal bounds, swaps reversed ones and takes the draw modulo the width in signed 32-bit arithmetic. The whole 32-bit range, where the original divides by zero, gives the low bound plus the draw (technical fix, both modes). |
| `RestockRules.GenerateOnce(set, seed)` | 0x463E20 | `Generate` drawing from `OriginalRandom(seed)` through `OriginalRestockRolls`: the same PC at the same container gets the same goods. |

The caller's part: the clear before (RK5), then after the lines the container's source and flag 0x200 are
cleared, so the window's restock schedule right after the first open finds no source and the container never restocks (RK12). The
original reaches the generation at the first open in loot mode (2) or mode 4 (RK12), at a restock event (RK3), and before any
whole-list work: the summon marking, the spill of a broken container, the death and destruction moves (RK13). The session keeps its
own generator for every other draw ([session random](session-random-runtime.md)); the original leaves its one generator in the seeded
state after the generation, which only the original's later draws notice. Tests:
[`OriginalRandomTests`](../../tests/Arcanum.Simulation.Tests/Randomness/OriginalRandomTests.cs),
[`RestockRulesTests`](../../tests/Arcanum.Simulation.Tests/Items/RestockRulesTests.cs).

### Session generation (ARC-WORLD-100)

The session generates a container with flag 0x200 once and remembers it (`inventory.generated`, snapshot schema 55): from then on its
source is 0 and its flag 0x200 clear, whatever the host's data says, so `ScheduleRestock` leaves it without an event and a pending
event finds no source (`RestockRules.Fire` → nothing). The draws come from `RestockRules.GenerateOnce` at
`RestockRules.GeneratedOnceSeed`, never from the session's own generator, so the same hero at the same container gets the same goods.

| API | Original | Rule |
| --- | --- | --- |
| `GenerateContainerOnce(expected, container, world)` | partner setup 0x572640 (call site 0x572779) → 0x463E20, RK12 | The first open in loot mode, which the host calls before `ScheduleRestock` for a container of the actor's map. Without flag 0x200, or once generated, no change. Otherwise the clear (persistent items stay, RK5) and the seeded generation as at a restock event; with a source the container is then recorded as generated. Flag 0x200 with source 0 (none installed) is only cleared and keeps its flag. An unreachable set (RK7) clears, generates nothing and is recorded. A container the session cannot regenerate (a set it does not admit, a transfer script, no seed facts from the host) keeps its list: outcome `Unsupported`, no change. Refused before any effect: an invalid entity, the version, a host not ready yet (`RestockReady` false) or no seed for the map
(`InventoryUnavailable`), a container the seed or the host does not list as one or a spilled one, a full record, list or generated budget (4096), the revision. One revision. |
| `FireRestock` (event `inventory.restock`) | 0x464180 → 0x463C60 → 0x463E20, RK3, RK10 | A container with flag 0x200 that the decision regenerates (on its own with flag 0x400, or through its merchant) draws seeded and is recorded; the merchant's own regeneration before it keeps the session's draws. Without the host's seed facts it keeps its list (`Unsupported`, as before). |
| `ISessionRestockWorld.GeneratedOnceFacts(map, container)` | 0x463E20 (field 2), 0x441B60 (field 0x10E) | The host's part of the seed: the container's tile x and the PC's player name bytes (the host's PC, else its prototype's; empty when unset). The default answers null (no seed facts). Asked only for a container with a source: flag 0x200 with source 0 is cleared without a seed. |
| `QueryContainerGenerated(map, container)` | 0x4EFDD0 | Whether the session generated the container (its source and flag cleared). |

The host must not call back into the session from its callbacks during a generation (the insert check, the item and set reads):
a nested `GenerateContainerOnce` on the same container would find it not yet recorded. The seed's PC is the session hero: its race as the source's index (stat 27) and its gender (stat 26: female 0, male 1), which no
session effect changes, and its background (field 0x101); a session without a hero seeds from the tile x alone. Tests:
[`GeneratedOnceStateTests`](../../tests/Arcanum.Session.Tests/GeneratedOnceStateTests.cs).

## Container loot window (ARC-WORLD-101)

The AREA viewer (exterior, cave and `--area-map`) opens a container's inventory in loot mode, as the original's open 0x572240 in mode 2
does (ledger [LW1–LW4](../research/ledger/items.md#container-loot-window-arc-world-101), one reading, F1). Code:
[`AreaContainerLoot.cs`](../../src/Arcanum.World/AreaContainerLoot.cs) (`Open`, `Close`, `Lootable`), the container target of
`AreaInventoryHost.Target`, `AreaRestockHost.GeneratedOnceFacts`, Godot `TerrainViewerAreaContainerLoot.cs`; session `LootItem` on a
container; data `PremadeHero.PlayerName`. Tests: [`AreaContainerLootTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaContainerLootTests.cs),
[`GeneratedOnceStateTests`](../../tests/Arcanum.Session.Tests/GeneratedOnceStateTests.cs) (the loot of generated goods),
[`PremadeHeroTests`](../../tests/Arcanum.Data.Tests/Characters/PremadeHeroTests.cs).

| Step | Original | Viewer |
| --- | --- | --- |
| Target | object click → use-container goal; use range 1, 2 action points first (AP6, CT3) | A left press on a container of the map's seed that has not broken (`AreaInventoryTargetKind.Container`) walks within reach 1 (since ARC-DEV-175 the click is the use goal 81 of the hero's table, not a walk-to-loot intent) and, in a turn-based round, charges 2 action points, as a body's loot. A broken container spilled its items (ARC-COMBAT-008) and is no target. |
| Admission | 0x572370: the player's open attempt 0x4AED80 (LK8); 0 runs the use script, whose skip default refuses with the locked sound; another code shows `inven_ui.mes` line *code* and plays the locked sound (LW1) | The session's `AttemptOpen` at its hour over the container's T0 lock baseline (flags field 0x38, key field 0x3A) and its recorded lock state; the session holds no keys, so a locked container stays shut. Locked, jammed or magically held: the container's locked sound (SN25, + 2), the viewer's own text, no window. Then the container's use script (SAP 1) runs through the viewer's use-script host with the PC as the triggerer (the S4 set and its additions, [SCR dispatcher](../scripting/scr-dispatcher-runtime.md#teleports-and-guards-arc-script-014-set-s4)): skip default plays the locked sound and refuses; a run the host refuses or a teleport it asks for refuses without effect. A native trap script (30000..30007), which the original's trap code handles on the use (not read), is not run: a trapped container does not open (the elven ruins' only 0x200 container is one). A container with an unlock script (SAP 3) is not opened (the attempt's script is not run). |
| Open frame | 0x572640: a container on frame 0 opens one frame with its open sound (LW2) | A container whose recorded frame is 0 steps to frame 1 in the session (`SetOpenableFrame`; an ART of one frame keeps 0) and plays its open sound (+ 0). The frame is drawn since ARC-SCRIPT-1778: a container off its T0 art or frame is drawn from its art id with the session's frame (`ContainerArtBodies`; an art a script's change art number gave it is drawn from the new art, [AN16–AN20](../research/ledger/scripts.md#change-art-number-arc-script-1735-e5-a-tail)). |
| Generation | 0x572640 → 0x463E20 for flag 0x200 (RK12) | `GenerateContainerOnce` for a container with flag 0x200 at T0 that the session has not generated, before the frame write (they do not read each other). A restock host not ready yet refuses the open before the use script runs (a retry then runs it once); a session refusal (a full budget) shows no window, after the use script; a container the session cannot regenerate keeps its list and opens. |
| Seed facts | 0x463E20: field 2's tile x, 0x441B60 field 0x10E | `AreaRestockHost.GeneratedOnceFacts`: the container's T0 tile x and the session hero's player name (`SessionHeroState.PlayerName`, ARC-UI-033): a pre-made hero starts with its prototype's name (`PremadeHero.PlayerName`, v119 slot 86, set by the viewer's `CreateSession`), the new character with the name typed on its screen. A hero whose name is empty adds no name term, as the original's unset name (0x441B60 copies an empty string); a session without a hero seeds from the tile x alone. |
| Window | 0x5727B0: the restock schedule (RK2), the loot counter (ID6) | The loot window on the container's current list; its restock schedule after the generation finds no source for a generated container. While it is open an item's decay waits (`ISessionLootWindowWorld.LootWindowShowsContainer`). Items are taken with the session's `LootItem` (keys, worn items and scripted items refused as for a body; the container's use script, run at the admission, is no refusal); gold and ammunition through the quantity window with `TakeGoldFromNpc` (the pile given) and `TakeAmmoFromNpc`, which take from a standing container too. |
| Close | 0x573440: a container on frame 1 without flag 0x100 closes with its close sound (LW3) | `AreaContainerLoot.Close` on the window's close: the recorded frame 1 steps back to 0 and the close sound (+ 1) plays. |

Logs: `ARC-WORLD-101 container open` (outcome, generation, frame, the regeneration's counts) and `ARC-WORLD-101 container close`.
Native smoke: stage `container` (map mode with `--hero`; the map battery runs it before the junk pile stage), marker
`ARC-WORLD-101 native-smoke container PASS`; `NONE` on a map without a standing, openable container with flag 0x200.

## NPC best items after a regeneration (ARC-WORLD-103)

At the end of an NPC's regeneration the original dresses it: 0x463E20 calls the best-items wield 0x4654F0 for an NPC (type 16) after
the lines, with no target, whatever the lines made (an unreachable set's empty generation too); a source of 0 ends the regeneration
before it (ledger RK15). 0x4654F0 is the call the pre-made hero's start makes: slots 1000 to 1008 in turn through 0x465170
([best items](equipment-runtime.md#best-items-arc-world-034), WB1–WB8). Code: `GameSessionNpcWear.cs`; tests:
[`NpcWearStateTests`](../../tests/Arcanum.Session.Tests/NpcWearStateTests.cs).

| Step | Original | Session |
| --- | --- | --- |
| When | 0x463E20 at 0x464069..0x46407E | After `Regenerate` of an NPC plan whose outcome is `Regenerated` or `Cleared` (not `Unsupported`), before the container's regeneration of the same firing; part of the firing's one revision. `SessionRestockRegeneration.Wield` reports it (`SessionNpcWield`: a failure, or the steps as `EquipBest` reports them); null for a container. |
| Facts | the NPC's object | `ISessionRestockWorld.RestockWielder(map, npc)` → `SessionNpcWielder`: the NPC as a wearer (race, gender, crippled arms in its life state), its chooser (damage bonus, aptitude, melee/bow/throwing levels, since ARC-WORLD-106 the firearms level from its stored tech skill (ledger NF1), extraordinary intelligence; the session sets `IsPc` false and the ammunition from the NPC's first stack of each type), the wear facts of each item of its list (`SessionNpcWearableLookup`) and the art lookup. The default answers null: the NPC then wears what the clear left it (`EquipmentUnavailable`, Not ready). |
| Start | the NPC's worn items after the clear | The items of its list that the session's list shows worn (persistent items keep their slots): the map data's inventory location, or the NPC's saved record once the session dressed it. |
| Choice | 0x465170 per slot (WB1–WB5) | The hero's plan with the NPC's facts: a kept item that still passes the wear check is the worth to beat (not the weapon slot, which always starts empty); candidates in the NPC's list order (its seed items, then its added items); a tie goes to the later one (project substitute for the handle order, WB4). An item a step takes off goes through `RestockInsertCheck`. A choice with a move script or item spells is not wielded and a worn one with them stays (`Unsupported`, the session runs no item script or spell, as for the hero). |
| Record | the NPC's inventory locations | On success the NPC's worn items are recorded (`inventory.npcWorn`, snapshot schema 57): for that NPC the record replaces its map data's inventory locations, so `QueryMapInventory`, the hero's take from the NPC (the loot window and the barter buy), the NPC crafter's components and its deletion see a made item it wields as worn and a seed item it took off as in its pack (location −1). `QueryNpcWorn(npc)` answers the record's items still in its list; null for an NPC the session never dressed. |

Refused before any change (the NPC keeps what the clear left it; nothing is recorded): no wielder (`EquipmentUnavailable`), a chooser
outside the bounds the weapon score reads (skill levels 0..20, aptitude −100..100, damage bonus within ±1,000,000:
`InvalidEquipment`), an item's invalid facts (`InvalidItem`), facts of another kind than the list's, or a worn item without wear facts
or outside its own slot (`InventoryMismatch`), a host callback that changed the session or attached a seed (`StaleVersion`). The firing waits a minute
when the dressed-NPC budget (4096) could not hold a new record. An item that leaves the NPC's list (to the hero, deleted, dropped by a
critical, discarded by the next regeneration) leaves its record, so it is not worn when it comes back.

The viewer's host (`AreaRestockHost.Wielder`, through `AreaEventWorld`) reads the NPC's combat values from the area's combat fields
(`SessionCritterCombat.Wearer`, `Chooser`; its life state the session's, else the map data's), an item's wear facts from its object row
on that map or, for an item the session made, from its prototype's stored fields, and answers the art lookup with yes (one diagnostic
body art, as at the hero's start). Since ARC-WORLD-106 the NPC fights with what it was dressed in
([NPC wear in combat](equipment-runtime.md#npc-wear-in-combat-arc-world-106)). Native observation: stage `barter`, marker `ARC-WORLD-103 native-smoke redress PASS` (the first
living NPC of the shown map with a source of its own whose set the project admits, scheduled through the window open's path; SKIP on a map without one).

## Direct regeneration (ARC-UI-038)

The opening shop asks for 0x463E20 directly on its merchant and then on the merchant's substitute container (0x545F1D, 0x545F34,
ledger [OS8](../research/ledger/character.md#opening-shop-merchant-arc-ui-038)), without the restock event's tests.
`GameSessionOwner.RegenerateInventory(expected, holder, world)` does it for a holder of the actor's map: the clear (persistent items
stay, RK5), also for a source of 0, which then ends it (no wield, RK15); otherwise the generation from the set with the session's
labelled rolls (a container with flag 0x200 seeded and recorded as at its first open) and, for an NPC, the best-items wield. A holder
the session cannot regenerate keeps its list (`Unsupported`, no change). Refused before any effect: an invalid entity, the version, a
host not ready or no seed for the actor's map, an object the seed or the host does not list as that kind of holder, a full budget and
the revision. One revision. The viewer's caller is the [opening shop merchant](../rendering/terrain-viewer.md#opening-shop-merchant-arc-ui-038).

## Follower cycle and examine (ARC-UI-1120)

Requirements: [FR-ITEM-008](../requirements/items-economy.md#fr-item-008) (examination), [FR-UI-003](../requirements/ui-media-lifecycle.md#fr-ui-003)
(window flow); ledger [HK25](../research/ledger/spell-runs.md#hotkey-bar-hover-descriptions-arc-ui-035) (examine in the barter window and at the
dialog start), [BW6](../research/ledger/ui-audio.md#barter-window-art-arc-ui-064) (the cycle).

- **Examine.** In the original look a pointer resting on an item (the hero's or the merchant's) runs the item's examine script with the hero as the
  triggerer and, unless it answered skip default, draws the item window (`TerrainViewerApp.BarterPointerStill`, the same description as the inventory's
  still pointer), and then makes the quote. The viewer runs it only with its interface on (an automated run without bars stays quiet); it hides its bars while the window is open, so the window is
  drawn only when they show (a known gap), the script runs either way. A ware with no map row that the session did not make runs no script. The dialog start does the same for the speaker (`DescribeDialogSpeaker`).
- **Cycle.** A follower's window while the hero leads two or more followers (the background 825) has the buttons 757 and 758
  (`AreaBarterArtLayout.CycleHit`, `AreaInventoryGridArea.CycleRequested`); a release over the pressed one asks for the previous or next follower of
  the hero's follower list (`AreaBarterCycle.Step`, wrapping, skipping the followers `BarterCycleSpeaker` refuses), closes the window and opens it again
  on that follower through the pending open of the dialog's `b:` reply. Limits: the refusal test is the can-speak head, the critter flags 0x20000008, the
  summoned spell flag and the viewer's present-speaker test (the tail of 0x4AD800 is not modelled); the pressed frame of the buttons is not drawn; a held item blocks the press (the original puts it back first); the close-and-reopen path has no automated test of its own (the smoke `barter` stage opens no follower window); a
  follower on another map is not a speaker here and is skipped. The follower portraits' hover (HK26) is built apart ([ARC-UI-1281](companion-orders-runtime.md#follower-portraits-hover-arc-ui-1281)).

## Not modelled (NOT READY)

- **Container loot window limits** (ARC-WORLD-101): a container with an unlock script does not open; a trapped one meets its trap
  first (ARC-WORLD-110, [trap state](trap-state-runtime.md#viewer-arc-world-110)) and stays shut when the trap is spotted or goes off,
  or without a cast host; a locked container opens when the hero's key list holds its key (ARC-WORLD-123: a key item bought or looted folds into the list); the refusal text is the viewer's, not the
  `inven_ui.mes` line; the facing turn and the sight test of the admission are not made; the open frame is recorded but not drawn and no
  open or close animation plays; a body's loot plays no sound 3007; the map data's own junk piles do not dissolve when their window
  closes (only the session's do); the steal mode (3) and the second container view (4) do not exist in the viewer.

- **Restock limits**: since ARC-WORLD-101 the loot window opens on containers too and schedules them; the steal mode and the window's
  other partners do not. NPC positions in the merchant search are their T0 cells, and the order is the seed's, not the original's
  sector list order (it matters only for two merchants of one container). The screen test's game window is the viewer's whole view at its
  zoom and the merchant search's area the viewport's size, not the original's fixed window. The generation of containers with flag 0x200 runs in the session
  ([above](#session-generation-arc-world-100)) and at the viewer's first loot open ([above](#container-loot-window-arc-world-101)); the
  callers of RK13–RK14 are modelled only for a critter a script created (ARC-DEV-386, RK16–RK17: the restock body at its creation); summons and a broken or destroyed holder are not. The NPC's best-items wield
  after a regeneration ([above](#npc-best-items-after-a-regeneration-arc-world-103)) takes no firearms level (a tech skill the map data's
  basic skills do not hold: 0, so a firearm scores −1 unless the NPC's sheet was levelled), reads no NPC effects, spells or art (never
  transformed, magick-allergic or unique), and the viewer builds an NPC's combat values (its attack's weapon, its armour class, its
  worn items a critical reads) from its map data's items and their map-data slots, cached per NPC: the session's record is not read
  there yet, so a re-dressed NPC fights as its map data dressed it (a later combat slice). Lines whose prototype is gold, a key or a key ring are skipped (the
  session's lists do not admit them), as are lines the host has no prototype for and lines past the holder's full list
  (`SessionRestockRegeneration.Skipped`). A roll refused by an exhausted generator (2^63 − 1 draws) throws out of the pass after the
  clear (not guarded).
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
  `gd_cls_*`): the window prints its own text. **Gambling** (FR-ITEM-019): see [Gambling](#gambling-arc-ui-1740); the opening shop's docked window has no bet box yet.
- **Paperdoll**: the merchant's worn items are not offered (the original reaches them only through its paperdoll); the follower menu and
  follower cycling open nothing.
- The uninitialised "buys everything" flag of a set without a buy line and of an out-of-range source (BT8): read as 0 (buys nothing).

## Gambling (ARC-UI-1740)

FR-ITEM-019, read in the executable ([ledger](../research/ledger/items.md#gambling-bets-arc-ui-1740)). A merchant's ware held over the bet box
(only the merchant's: the hero's own item is put back silently) runs the gate once as it enters (`AreaBarter.CheckGamble`): the
`GamblingRules.Gate` refusals in the original's order (the hero's side, a merchant without Gambling points, a price above the hero's limit
`GamblingRules.PriceLimit`, too few coins, a worn item below Expert and a "won't sell" item below Master, both by the hero's training) each
print the merchant's line of its category; a passing ware prints the training warning line when it needs one and the confirm line with the price.
The lines come from the player's generated NPC files (one draw each); without them the window prints a plain text.

A release on the box bets (`AreaBarter.Gamble`, the session's `GameSessionOwner.Gamble`): the Gambling check of the hero against the merchant's level
on the saved d100; a win moves the item for nothing and costs the merchant the item's worth in reaction (`GamblingRules.WonReaction`), a loss moves it
for the unit price like a buy. A stack goes whole. The saved roll is thrown at the first barter window (`GameSessionOwner.EnsureGamblingRoll`), the
check's own draw replaces it for the next bet, and the roll is part of the snapshot (schema 101). A refused move (no room, a script, too few coins for
a lost stack) leaves the roll, the generator and the fate as they were.
