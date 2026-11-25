# Session inventory runtime

Status: **Implemented bounded component**, ARC-WORLD-014 (steps 2 and 3 of the
[ARC-RES-023 proposal](../research/crash-site-item-inventory.md#proposal-for-implementation-p)). A Godot-free
inventory domain in the [game session](game-session-runtime.md) (`Arcanum.Session`): it moves Crash Site items to the
PC by ground pickup, by looting a dead NPC and by the dialog's negative `IN`, and saves the items the PC took in
snapshot schema 5. It covers the transfer part of [FR-ITEM-001](../requirements/items-economy.md#fr-item-001) for
these three paths only. Since ARC-UI-004 the AREA viewer uses it: seeds from its loaded maps, pickup and loot clicks,
the inventory window and Virgil's `IN` ([viewer](../rendering/terrain-viewer.md#inventory-window-pickup-and-loot-arc-ui-004)).

Categories as in the research: source claims (S) are a reading of the original's functions at the cited addresses,
evidence level F0 (not checked in the executable) unless the
[verification ledger](../research/original-verification-ledger.md) lists them. Data claims (D) are checked by the opt-in
real-data test below.

## Model

**Seed (baseline, not state).** The host attaches one `SessionMapInventorySeed` per loaded map with
`AttachMapInventory(map, seed)`: every holder (container or NPC) with its list in serialized order, script attachment
points, a non-null substitute-inventory handle and its life fact (`Dead`, set only when the host knows the NPC is
dead), and every item with its facts (`SessionItemKind`: type, prototype, name class `OBJ_F_NAME`, description number;
weight, complexity, inventory location, quantity, script attachment points) and either a ground cell or exactly one
listing holder. For the Crash Site the host builds it from the [T0 inventory view](area-inventory-view.md)
(ARC-DATA-012) and life from the [step rules](area-step-assessment.md) (`CRITTER_DEAD` evidence); the viewer's
`AreaInventoryMap.Build` holds that mapping (ARC-UI-004). The seed is validated (unique entity ids, each item listed once or
on the ground, no dead or substitute container, defined item types 5..14, a quantity on gold and ammunition and only
there, distinct script points 0..35 (`SAP_EXAMINE`..`SAP_CRITICAL_MISS`); at most 2,048 items and holders and 512 per list; otherwise `InvalidInventorySeed`) and
copied; at most `MaxMaps` seeds are attached (`BudgetExceeded`). Attaching moves no revision, is never saved, and is
repeated after every restore.

**Window seeds (ARC-COMBAT-024).** A streamed main map is shown as a window of sectors (ARC-RENDER-024), and the viewer builds
its seed from that window only, marked `Window` (`AreaInventoryMap.Build` sets it for a window snapshot). Such a seed need not
list an item the session took from the map (a taken, placed or given item, or an item of a junk pile on the map) that lies
elsewhere: the map's previous seed's row of that item is kept with the new one (for lookups only, as its complexity, decay flag
and scripts; it joins no ground and no holder's list), and with no previous seed (a restored game whose window does not reach
the item) the item has no row and its row-read facts are not read here (NOT READY, as a taken item's wear art). An item a
window seed does list is checked as in any seed; one it does not list is not checked, so a save of other data is caught
only for the items its window reaches. Before, a window that no longer reached the sector an item was taken from
was refused (`InventoryMismatch`), and the scene was published without its inventory and combat hosts: killed NPCs stood,
the hero was drawn without its armour, nothing could be looted or attacked; a quick load of such a save was refused.

**State.** The ordered list of items the PC took from the world (`SessionPcItem`: entity, origin map, kind);
`QueryPcInventory` returns it. Since ARC-WORLD-015 the session also holds the hero's gold (`QueryGold`) and starting kit
(`SessionKitItem`: `kit:1`, `kit:2`, … with no origin map; `QueryHeroKit`; [starting hero](starting-hero-runtime.md)), and since ARC-TECHNOLOGY-004 the items it made from schematics (`SessionMadeItem`: `made:1`, … with no origin map; `QueryMadeItems`; [schematic assembly](schematic-assembly-runtime.md#made-items-snapshot-schema-24)),
which precedes the taken items in the PC's inventory and counts toward `MaxPcItems`. A taken item is hidden from its
seed's ground and holder lists (`QueryMapInventory`). The PC object, its
attachments and gold pickup are not session state yet; the hero's worn slots are ([equipment](equipment-runtime.md),
ARC-WORLD-028; a worn item stays in its list).

All entity ids follow the session rule (ASCII letters, digits and `. _ : -`, 1..128 characters); the AREA host uses
`oid:<SHA-256 of the raw 24-byte object ID>`, the id the conversation host already uses.

## Transfers

Each transfer takes the expected `GameSessionVersion`, works on the seed of the actor's map
(`InventoryUnavailable` without one), refuses before any effect, and adds one revision when it moves an item to the end
of the PC's list. `InventoryMutationResult` names the moved item and the insert check's answer.

| Call | Source path (S) | Admitted here |
| --- | --- | --- |
| `PickUpItem(item)` | left click → `AG_PICKUP_ITEM` → the item transfer (0x4617F0) of an unparented item: item `SAP_GET`, insert check, the object pickup (0x4417A0), the item insert (0x466640; item `SAP_INSERT_ITEM`) | an item on the ground of the actor's map |
| `PickUpItemByNpc(npc, item, insertCheck, mapPile?)` | ARC-AI-1655: with a script host the item's get script runs before the insert check and its insert-item script after the insertion, both with the NPC as the triggerer (`DeclinedBy`, `InsertScript`, `Messages` as the hero's pickup returns them); ARC-AI-1309: the NPC pickup goal's end (0x42AA70 → 0x4617F0 with an NPC as the new parent): an item lying on the actor's map (a seed item, a placed one, a junk pile's; ARC-AI-1486: or one of a standing junk pile of the map's data, named by its `mapPile` target) moves to the end of the NPC's list under the host's insert check, ammunition merging into its stack, NPC flag 0x40 set; a seed or hero item is recorded as given (a saved NPC list needs it), so the hero's loot of it returns it to its list; ARC-AI-1486: the removal's check (0x467860, JP5) then dissolves a pile left with one item or none at once (a session pile in the same revision, a pile of the map's data as a map change in the next) | an item on the ground or in a junk pile of the actor's map, a known NPC |
| `LootItem(holder, item)` | click on a dead critter opens it as a container (target `SAP_USE` checked first); a moved item runs the parented transfer: PC `SAP_TRANSFER`, insert check, the item removal (0x466E00; item `SAP_REMOVE_ITEM`, unequip when worn), the item insert (0x466640) | an item still listed by an NPC the seed marks dead, or one the session killed (ARC-COMBAT-005, [hero attack](hero-attack-runtime.md)); since ARC-WORLD-101 an item in the current list of a container of the seed that has not spilled (its seed items, the items a barter or a regeneration put into it); the host admits the open first ([container loot window](barter-runtime.md#container-loot-window-arc-world-101)); since ARC-DEV-202 a body's worn item (slots 1000..1008, a dressed body's record over its map data) is looted too: the removal unwields it ([worn items of a looted body](#worn-items-of-a-looted-body-arc-dev-202)) |
| `GiveItemToPc(npc, name)` | `DIALOG_ACTION_IN` with a negative value: first item in the NPC's list with that name class, else in its substitute inventory; parented transfer to the PC; dropped at the PC's location when the PC did not get it (ARC-WORLD-1131: modelled for an insert-check refusal, [room of a take](#room-of-a-take-arc-dev-333)) | the NPC's current list; no match and no substitute is NoChange, as in the source |

Refusals, in order, after input (`InvalidItem`), version (`StaleVersion`) and seed (`InventoryUnavailable`) checks:

- pickup: `ItemNotAvailable` (not on that map's ground, or already taken);
- loot: `UnknownHolder`, `HolderNotLootable` (a living NPC, or a container that spilled when it broke; before ARC-WORLD-101 every
  container), `HolderScriptUnsupported` (a body's `SAP_USE` script; a container's ran at its window's admission and refuses nothing), `ItemNotAvailable`
  (not in that holder's current list);
- IN: `UnknownHolder` (not an NPC of that map), then no own match: NoChange, or `SubstituteInventoryUnsupported` when the
  NPC has a substitute inventory;
- then the shared item rules: `ItemTypeUnsupported` (gold merges into the PC's gold; a key goes onto the hero's key ring since
  ARC-WORLD-123, [below](#key-items-arc-world-123), a key ring item and a key for a session without a hero stay refused; since ARC-WORLD-066 ammunition is admitted and merges into a held stack, [below](#ammunition-arc-world-066)), `WornItemUnsupported` (a parented item in slots 1000..1008, except the loot of a body, [below](#worn-items-of-a-looted-body-arc-dev-202)),
`ItemScriptUnsupported` (an item `SAP_GET` for pickup, `SAP_REMOVE_ITEM` for loot and IN or `SAP_INSERT_ITEM` when the
caller passes no script host; with a host they run, [item events](#item-events-arc-script-041); other attachments such as
examine and use do not run on a transfer), `BudgetExceeded` (`GameSessionLimits.MaxPcItems`, at most 512), `RevisionExhausted`, then `InsertRefused` (not for the dialog's `IN`, ARC-WORLD-1131: its insert-check refusal lays the item on the PC's tile, [room of a take](#room-of-a-take-arc-dev-333)), and
last the item's insert-item script (below).

**Insert check.** The original's insert check (0x466510) depends on the created character (carry weight against its strength, the
Technophobia background against technology items, free grid room around its own items). The session applies the Technophobia
part for the hero after the host's weight answer ([backgrounds](equipment-runtime.md#backgrounds-arc-world-069)); the weight is the
host's, and since ARC-DEV-333 (B48) the room of an item entering the hero's list from outside is the session's (below). The host answers it per item with a `SessionInsertCheck` delegate (`Accepted`, `TooHeavy`, `TechnologyRefused`,
`NoRoom`; any other value refuses). It runs only after every other refusal. If the host calls back into the owner from it and anything the transfer read
changed (revision, actor map, the attached seed, the item already taken), the transfer is `StaleVersion` with no effect.
A refusal changes nothing: for pickup and
loot that is the source behaviour; for IN the source drops the item at the PC's location (into a junk pile when
items lie there), and since ARC-WORLD-1131 so does the session ([room of a take](#room-of-a-take-arc-dev-333)).

Not modelled, on purpose: the PC's `SAP_TRANSFER` attachment without a script host (with one it runs, [item
events](#item-events-arc-script-041)), reach and the walk to an item, the
combat action-point cost of a pickup, item decay timers, the item insert's slot choice and hotkeys, encumbrance effects,
dropping, barter, theft, positive `IN`, NPC respawn and chest generation.

## Insert-item script (ARC-SCRIPT-013)

The original inserts the item and then runs the item's insert-item point (21) with the new holder as triggerer and the
item as attachee; the point cannot cancel and its result is ignored (ledger IF10). The Crash Site amulet (2527) is the
one Crash Site item with such a script; [ARC-RES-055](../research/item-insert-script.md) found it only prints a line.

`PickUpItem`, `LootItem` and `GiveItemToPc` take an optional `ISessionItemScriptHost` (an `IScrHost` plus
`PrintedLine(script, line)`). With it, a scripted item's transfer runs `ScrDispatcher.RunObject` for point 21 with the
item (`ScrObjectRef(actor map, item)`) and the player (`SessionEventKinds.PlayerEntity`) under the session's script rules
and `SessionItemScripts.Admission` (S6 without the dialog action, the story-state actions and the deferred calls). The
run is made after the insert check and before the commit; the admitted commands read nothing the insertion changes, so
the order is equivalent. The transfer then commits together with what the session applies from the run, or is refused
with `ItemScriptUnsupported` and no effect when:

- the script is outside the set (`Refused`) or its file exists but cannot be read (`UnreadableScript`);
- it reads a global flag the session does not hold as known (the run reads flags from the session, not from the host);
- the run changed the attachment (local flags, counters, its number), started a conversation, set story state, asked for
  a deferred call or a trap destruction: the moved item has no home for a changed slot yet;
- it writes a global flag the session does not hold as known;
- a printed line is `Unsupported` for the host (an action field, generated text, a name the host cannot fill).

Global flag writes are applied with the transfer, in its one revision. A printed line the host shows comes back in
`InventoryMutationResult.Messages` (`SessionScriptMessage`: script, line, kind, text); a missing DLG prints nothing
(`NoDialog`: no DLG or one without records), as does a missing script file (`MissingScript`, the transfer proceeds); a
DLG the host cannot read is `Unsupported`. `InventoryMutationResult.InsertScript` carries the run (also on a refusal). A
call back into the owner from the host during the run (any revision, actor, seed or global-flag change) makes the
transfer `StaleVersion`. Without a host the item is refused, as before.

The AREA viewer passes `AreaItemScriptHost` when the game's script content loaded: an item's slot from its area row,
scripts and DLGs from the content stack (the session's data-fix rules), the session's flags, the player and the party;
the printed line uses the hero's gender (male without a hero) and the hero's player name (the placeholder without a hero); ARC-SCRIPT-2811: a line naming an NPC triggerer by `@pcname@` (its not-yet-met description) or the item attachee by `@npcname@` (its shown name over the session's identified marks) is read through the shown map's use host
(`ISessionItemScriptHost.PrintedLineOf`); a line whose object has no row there (a made item) or a host without the name source is `Unsupported`, so such an item is refused. Lines a
script prints during Virgil's `IN` reach `SessionDialogInventoryPort.LastResult`, but the conversation panel does not show
them yet (no Crash Site `IN` gives a scripted item). The status line and the loot window show the printed text after the item's name.

## Item events (ARC-SCRIPT-041)

Plan slice S8 of the [dispatcher research](../research/scr-event-dispatcher.md#10-proposed-implementation-slices), the transfer
half. With an `ISessionItemScriptHost`, `PickUpItem`, `LootItem`, `GiveItemToPc` and `TakeItemFromNpc` (ARC-WORLD-071) run every item event of the original's
transfer through `ScrDispatcher.RunObject`, in the executable's order ([ledger IE1–IE7](../research/ledger/scripts.md#item-events-arc-script-041)):

| Transfer | Events in order | Roles (triggerer / attachee / extra) | Result |
| --- | --- | --- | --- |
| Pickup from the ground (0x461810) | item `SAP_GET` (4), then the insert check, then the insertion's `SAP_INSERT_ITEM` (21) | PC / item / —; PC / item / — | get gates: skip default leaves the item; insert-item ignored |
| Loot, negative `IN` and taking from an NPC (0x461CA0) | recipient's `SAP_TRANSFER` (32), then the insert check, then the removal's `SAP_REMOVE_ITEM` (28), then `SAP_INSERT_ITEM` (21) | holder / PC / item; holder / item / —; PC / item / — | transfer gates; remove-item and insert-item ignored |

- The runs come after the session's own refusals of the item (type, worn, budgets, revision) and before the insert check; the
  original runs the gate first, so the order differs only for items the session refuses anyway.
- An item point runs only when the item's facts list it (the seed's script points); the PC's transfer point always runs, and the
  host's `Slot` answers it (the AREA viewer's PC has no slot, so it ends `NoScript`). Without a host, an item that lists its get,
  remove-item or insert-item point is refused as before, and nothing runs.
- ARC-MAGIC-1743: the hero's give and sale to an NPC (`GiveItemToNpc`, `BarterSell` with a host; the barter window gives it) runs the item's events of the hero's side as the original does (0x461CA0, ledger IE3, IE4, IE6): wield off (25, a worn
  item) and remove item (28) with the hero as the triggerer, then insert item (21) with the recipient (an NPC or the merchant's container) as the triggerer, after the insert check accepts and before the move is applied; the points are those of the worn
  record (a kit or made item too) and of the item's origin seed row. A run the session cannot apply, or a stale version, refuses the move with nothing changed; without a host an item with any of them is refused (`ItemScriptUnsupported`), as is an item
  whose row is another map's seed row. The recipient's own wield of the item is its re-wield (NPC flag 0x40), not this call. The insert run's printed lines are read for an NPC recipient (a container's run keeps the hero's reading). ARC-MAGIC-1823: the dialog's non-negative `IN` (`SessionDialogItemTakePort`, host from `AreaInventoryHost.TakeItemDialogPort`), the pick pocket's plant (`PickPocket` passes its host on) and the script command's transfer item from the hero to an NPC or container (`TransferNamedItem` with the host `AreaUseScriptHost.ItemScripts`; `QueryNamedItemTransfer(itemScripts: host)` lets the plan pass) run the same scripts through the shown map's host; without one they stay refused. A script's transfer is applied after the run, in action order, so an item script reads the session as the effects applied before it left it, and the plan refuses an item whose scripts belong to another map's row (`ItemScriptUnsupported`), ARC-MAGIC-2252: the take directions of the script transfer (an NPC or a container to the hero: `TakeItemFromNpc`, `LootItem`) pass the host too and run the hero's transfer script (the gate, as for any transfer from a holder), the item's remove item (28, the holder as the triggerer) and insert item (21), and the plan refuses an item of another map's NPC whose row has such a script. The plan also makes a dry run of the move through the host (the runs read and prepared, nothing applied; ARC-MAGIC-2266: the dry run asks the caller's real insert check, the host's, passed to `QueryNamedItemTransfer` as to `TransferNamedItem`, so a move that check refuses, which runs no script after it, is not refused by the plan for a script it would never run; without a check the plan takes it as accepting): a nested run the session cannot apply is the plan's refusal, so the calling script is not left half applied. The dry run reads the session before the calling script's earlier effects apply, so a nested run that only fails after them is still found when the move applies (the earlier effects then stay), and a verdict the insert check gives before those effects (room or weight they free) can differ when the move applies, so a plan that passes on a refusing verdict may still meet a nested run the session cannot apply (recorded limits). A worn item leaving an NPC or container stays refused. ARC-UI-1824: the barter window shows the lines these runs printed (`AreaBarter.Settle` adds `AreaInventoryHost.Printed` to the text of a buy, a sale and a sale onto a slot, a refused one's too; `AreaBarter.Gamble` adds it to a won bet's line and a refused bet's text), as the loot window does. Only the origin seed row and the worn record show an item's scripts: an unworn kit or made item runs none.
- Every run uses `SessionItemScripts.Admission` (S6 without the dialog action, story state and deferred calls) and the effect rules
  of the insert-item script above (global flags and printed lines only; a changed attachment, a dialog, story state, a deferred call
  or a trap destruction refuses the transfer). Since ARC-SCRIPT-042 the set also holds the item-effect commands, and a transfer
  applies what an equipment move applies ([wield events](equipment-runtime.md#wield-events-arc-script-042)): the hero's effect
  changes and the moved item's changed slots, which the session keeps (also on a declined gate: the item's slot stays changed where it
  lies). Since ARC-DEV-152 the set also reads and writes the global variables and the hero's variables and flags (value kinds 1, 5,
  6; a session without a hero drops the PC's writes, SA14). Each run reads the global flags and these stores as the earlier runs of
  the same transfer wrote them, as in the original; nothing is applied before the commit, and the first refused run ends the transfer (`ItemScriptUnsupported`, no effect,
  no later run).
- **Skip default of a gate** (get or transfer): the item stays where it is and the insert check is not asked, as in the original. The
  run's global flag writes and printed lines still happen: `InventoryMutationResult.DeclinedBy` names the point, `Item` is null, the
  disposition is Changed (one revision) when the run wrote flags and NoChange otherwise; an item's decay event stays. The dialog port reports a declined `IN` as
  applied. The AREA viewer names the declining script in the status line and the loot window.
- **Session policy:** a transfer that is refused after a gate ran (by the insert check or a later run) drops the gate's writes too
  (the original keeps them; no installed item or PC carries a gate script, below).
- `InventoryMutationResult.Scripts` lists every run in order (`SessionItemScriptRun`: point, attachee entity, run), also on a
  refusal; `InsertScript` stays the insert-item run.

**Installed data** (the `--script-census` probe's `item_points`, counted as its attachment table: object slots, then prototype
defaults, then scripts; a script is admitted when its run from line 0 stays inside the item set): no object or prototype carries a
get (4), drop (5) or buy (17) script; insert-item 74 + 12 slots, 41 scripts, 7 admitted (the first refusals: loops, `is named`,
get object type, toggle state); remove-item 6 + 8 slots on generic items, 3 scripts, none admitted; transfer 16 slots, all on
containers (the recipient when the PC puts an item in), 14 scripts, 1 admitted; wield on 309 + 61 slots, 56 scripts, none admitted;
wield off 299 + 59 slots, 52 scripts, 1 admitted. The wield pair needs add and remove effect (77, 78), magic-tech adjustment (91),
call-ex (81) and the conditions identified (47) and items being rewielded (50); with them (ARC-SCRIPT-042) 43 wield-on and 50
wield-off scripts are admitted, the other counts stay.

**Not ready.** The wield-on and wield-off events of `EquipBest` and the rewield (`Equip` and `Unequip` run them since
ARC-SCRIPT-042, [wield events](equipment-runtime.md#wield-events-arc-script-042)); the drop event of the critical drops of
[placed items](#placed-items-arc-world-059) (no installed drop script; the player's drop runs it since ARC-UI-025,
[hero drop](#hero-drop-and-inventory-use-arc-ui-025)); the buy event (no barter);
the scripts of a placed item's pickup (PI6); a container as recipient (putting items into containers is not modelled).

## Tech books (ARC-DEV-366)

The eight tech books (prototypes 15208 to 15215, one per discipline) carry the insert-item script 30017 and the remove-item script 30018
([ledger XA9](../research/ledger/technology.md#aptitude-and-expertise-consumers-arc-res-114)). Their one effect is expertise in the book's discipline:
while a book is in the hero's pack the discipline's expertise is higher by the hero's intelligence.

- **The run.** The insertion (a pickup, a loot, a purchase, or a move of the book inside the grid) runs 30017: get stat (33) of the stat in
  counter 3 (4, intelligence) of the triggerer into counter 0, then a loop that adds the effect of counter 1 (168 to 175, the item cause) as
  often as counter 0 divided by counter 2 (1) says. The loop counts a local variable down, so counter 0 keeps the intelligence the insertion read.
  The removal runs 30018, whose call-ex runs 30017 from line 6: it removes the effect counter 0 times. A move in the grid fires the removal and then
  the insertion ([IW10](../research/ledger/items.md)), so it reads the intelligence again: a hero whose intelligence changed while carrying a book
  has the count of the new intelligence after the move and the old one before it. The installed scripts run as they are (opt-in real-data test
  `TechBookRealDataTests`); the effects 168 to 175 are read from `rules/effect.mes` (one change each: +1 expertise in the discipline).
- **Host.** `SessionItemScripts.Admission` admits get stat. The session answers it for the hero only, under the effect list the move's earlier runs left
  (`ItemScriptRuns.Stat`, `GameSessionOwner.QueryHeroScriptStat` when no run changed the list; the level and the alignment are the stored values,
  GS3); another object, a stat the session does not hold and a session without a hero end the run as not modelled, which refuses the move
  (`ItemScriptUnsupported`). A run that reads a stat after an effect change of the same run that moves the stat (the session applies a run's changes after it) is refused too;
  the book reads before it adds. The viewer's `AreaInventoryHost.EffectRules` is the hero's effect tables (`AreaItemScriptHost.EffectDefinition`); until
  ARC-DEV-366 the host knew no effect rule at all, so every item script that adds an effect was refused in the viewer.
- **Room.** The original's effect list has no bound ([EF1](../research/ledger/character.md)); the session's bound for the hero is 1024 entries
  (`SessionEffectLimits.MaxHeroEffects`; 64 before): eight books at the stat ceiling (100) need 800. The blessing and curse lists, the timed effects and an NPC's
  list keep their 64.
- **Not ready.** Taking a book out of the pack by a path that runs no removal (a drop, a sale, a gift, a theft, the placed-item pickup) is still refused
  where it was ([above](#item-events-arc-script-041)), so a book's effects end only with the grid move above; where the books lie in the world is not
  read ([RES-115](../research/technology-evidence.md)).

## Dialog seam

`IDialogInventoryPort` (Scripting) takes the conversation's `GiveItemCommand`. When a host passes it as the last
`DialogConversationPorts` argument (`Inventory`), negative `IN` goes there instead of the world port, and `IN` needs only
one of the two ports. `SessionDialogInventoryPort` runs `GiveItemToPc` on the session's current version with the host's
NPC-to-entity map and insert check: a transfer, the no-match case and (ARC-WORLD-1131) an item the insert check refused, which lies on the PC's tile, are `Applied`, any other refusal is `Rejected` (the
conversation then fails with `CommandRejected`), and `LastResult` keeps the session's answer. It must be rebuilt when
the host replaces the owner, like the party port.

## Snapshot schema 5

(Schema 7 since ARC-WORLD-019, schema 8 since ARC-COMBAT-002, schema 9 since ARC-WORLD-024; the inventory part is described here.)

`GameSessionSnapshot.Inventory` is encoded between `hero` and `random` (between `hero` and `lighting` before schema 7). Schema 6 (ARC-WORLD-015) added `gold` and the
`kit` list:

```json
"inventory":{"gold":0,"kit":[],"pc":[{"entity":"ring","map":79,"type":"Armor","prototype":3804,"name":2804,"description":32804},
                            {"entity":"apple","map":79,"type":"Food","prototype":4600,"name":3600,"description":33600}]}
```

(The synthetic test's values; line breaks added.)

Decoding keeps the list order and refuses an entity listed twice, in either list (`NotCanonical`), an unknown type name or a map below
1 (`OutOfRange`), missing or unknown fields and more than 512 items (`BudgetExceeded`). `Restore` refuses a type a
transfer cannot produce (gold, key, key ring: `InvalidItem`; ammunition without its stack record: `InvalidSnapshot`, ARC-WORLD-066), a repeated entity (`InvalidSnapshot`) and the
caller's lower limit, kit and taken items together (`BudgetExceeded`); gold or a kit item without a hero, and a kit
item that is not the next `kit:n`, are `InvalidSnapshot`. After a restore no seed is attached; `AttachMapInventory` then refuses
(`InventoryMismatch`) a seed that lacks an item the PC took from that map (a window seed may, see above), gives it another kind, or lists an item the
PC took from another map, so a save of other data cannot silently duplicate or lose an item. For the same reason a seed
may not list an item another attached seed lists (one object lies on one map).

**Migration decision: none.** Schemas 1..5 are `UnsupportedSchema` (schema 6: [starting hero](starting-hero-runtime.md#snapshot-schema-6)); a schema-4 quick save loads as "written by an
incompatible version" with the running session kept, and the next F7 overwrites it. The viewer has no inventory state
in schema 4 to lose (its `IN` record was in memory only).

## NPC inventory changes (ARC-WORLD-055)

One store of NPC inventory changes for every NPC on every map (`GameSessionNpcInventory.cs`), read in the executable first
([ledger "NPC inventory changes"](../research/ledger/items.md#npc-inventory-changes-arc-world-055), NI1–NI7).

**Original (NI1–NI5).** The original keeps no inventory store of its own: an NPC's list is fields of the NPC, each item its own
object with a parent field, and all of them are saved with the map the NPC stands on when the PC leaves it (changed fields of
original objects, deleted original objects by ID, whole records of objects created in play) and read back when the PC returns.
A follower carried to another map takes its whole inventory with it, parentage kept, and from then on is that map's object.

**Steamwoven.** The session keys the changes by the NPC's entity identity (the `oid:` entity of its home map), not by the map:
the NPC's list is its seed list on whatever attached seed lists it, minus the items the PC took or the session deleted, followed by
the made items the session added to it. Because an object stands on one map at a time and carries its items (NI4), keying by the
NPC gives the same visible result: the changes are there after the PC leaves and returns, and they stay the NPC's when it
visits another map (`QueryNpcInventory`). The map-bound calls (`QueryMapInventory`, `LootItem`, `GiveItemToPc`) still read the
holders of the actor's map seed, so on a map whose seed does not list a visiting companion its seed and added items are not shown
or taken there (as before this task for its seed items). ARC-WORLD-3118: the barter window's open and view are not among them: `QueryNpcHolder(npc)` finds the NPC's
holder (and the attached map whose seed lists it) on the actor's map first, else on the companion's home map, and `OpenBarter` and the window's list, worn view and wear test read it
there, so the window of a visiting companion opens (its coins and items are the home map's holder). The changes are:

- **Added made items** (`SessionInventoryState.Npcs`: `SessionNpcInventory(npc, items)`; `QueryNpcInventory(npc)`): `made:n`
  items with their saved kind, numbered with the hero's made items (one past every made number held by the hero or an NPC, deleted
  or with a durability record), in the order they entered the NPC's list. `AddItemToNpc(expected, npc, kind, insertCheck)` makes one
  (the creation 0x4ED8B0 and the transfer 0x4617F0 into the NPC, as the NPC crafter's product enters its inventory).
- **Deleted items**: `RemoveItemFromNpc(expected, npc, item)` deletes an added item or a seed item still in the NPC's list with the
  [durability](item-durability-runtime.md) deletion (hidden from every seed, the condition record dropped, recorded as deleted;
  the original's destroyed original object, whose ID goes to `mobile.des`, NI3). No item script runs without a script host; with one (ARC-SCRIPT-2854, `RemoveItemFromNpc(..., scripts)`, `DestroyNamedItem(..., scripts)`, `DestroyUsedItem(..., scripts)`) the item's remove-item script (28) runs as the removal does (IE3): holder as the triggerer, item as the attachee, result ignored, prepared with a dry run in the plan and committed with the deletion in one revision; an item whose row is another map's, or a run the session cannot apply, refuses the deletion before any effect. Without a host such an item is refused (`ItemScriptUnsupported`), never deleted with the script skipped. The spell path's spent scroll or food (`SessionSpellCaster.SpendUsedItem`, the cast host's destruction after an item use or an NPC's item cast, ARC-SCRIPT-2881) runs the 28 through the caster's `ItemScripts` host (the viewer sets it to the shown map's item script host) and logs the lines it printed as `ItemScriptLine` entries for the message window; without a host an item with a 28 stays. The hero's used-up item returns its lines in `DestroyUsedItemWithLines` (the viewer's plain spend and a use script's destroy of the used item post them). The plan's dry run reads the session before the calling script's earlier effects apply (a recorded limit, as the named-item move's). ARC-SCRIPT-2879: the restock clear (each deleted item, holder as the triggerer, persistent items stay) and a body's inventory walk (every item, corpse as the triggerer, before its destroy or drop) run it in the event pass through `ISessionEventWorld.ItemScriptHost`, each run prepared and committed on its own in the pass's one revision, its runs and lines in `SessionRestockRegeneration.Removals` and `SessionBodyDecayFiring.Removals`; a run the session cannot apply, an item whose map is not the shown one or a world with no host is counted in `Removals.Skipped` and the removal goes on without it (the pass cannot refuse). A gold pile or ammunition stack a move empties (the moves that run point 21) runs point 28 after it, with the holder as the triggerer, in the same prepared run set, and is refused whole, as the insert-item event is, when the session cannot run it. Other destroy paths run none yet ([#2899](https://github.com/Simarglok/arcanum-steamwoven/issues/2899) junk pile dissolve, [#2900](https://github.com/Simarglok/arcanum-steamwoven/issues/2900) other pile callers, created critters [#2903](https://github.com/Simarglok/arcanum-steamwoven/issues/2903)). ARC-SCRIPT-2880: `DestroyMapNpc` and `DestroyMapContainer` (and their refusals) take a script host and run 28 on each invulnerable item they take out, in the walk's order, the dying object as the triggerer (`RunDestroyRemovals`): the printed lines are the result's `GameSessionMutationResult.Messages`, which the script host appends to the printed lines of the run that destroys; without a host such an item refuses the destroy. ARC-SCRIPT-2903: `DestroyCreatedObject` and `DestroyCreatedObjects` (a summon's expiry or dispel, the Destroy component, a transform's undo, an encounter creature's leave) take the same host and run 28 on the invulnerable items a created critter drops, each with its critter as the triggerer, in one run set made before anything changes (`RunCreatedDestroyRemovals`); the lines are the result's `SessionCreatedObjectResult.Messages` (the spell host logs them as item script lines, the viewer posts them). Policy as the map NPC's: a run the session cannot apply, an item or critter of a map other than the shown one, or an item with the point and no host refuses the destroy whole, so the critter stands until the refusal clears (the viewer's two encounter-creature destroys make one attempt, the leave of a map included, and only log a refusal).
- **Items the PC takes** (loot, negative `IN`) leave the list as before; an added made item keeps its entity and kind and joins
  the hero's made items (`QueryMadeItems`), never the taken world items.

`QueryMapInventory`, `LootItem` and `GiveItemToPc` see the added items after the seed items; a corpse's added items are loot (life
from the session's life records), a living NPC's are not. The AREA viewer's loot window reads `QueryMapInventory`, so it shows
them. The NPC schematic crafter (ARC-TECHNOLOGY-006, [assembly](schematic-assembly-runtime.md#npc-crafter-arc-technology-006)) deletes its
components and adds its products through the same rules.

The NPC must be known to the session: an NPC holder of an attached seed, a party record, or an NPC that already holds added items
(else `UnknownHolder`). Refusals, before any effect: `InvalidItem` (an invalid id, a `made:` id as the NPC, an undefined type),
`StaleVersion`, `UnknownHolder`, then for an addition `ItemTypeUnsupported` (gold, keys and key rings, as for the PC; ammunition since
ARC-WORLD-066, with its type and units: `InvalidAmmo` without them),
`BudgetExceeded` (the NPC's list at 512 with its seed items, the store at `GameSessionLimits.MaxNpcItems` = 4,096, or no free made
number), `RevisionExhausted` and last `InsertRefused` (the host's insert check for the NPC; a call back into the owner from it, a seed
attach included, is `StaleVersion`); for a deletion `ItemNotAvailable` (not in the NPC's current list), `WornItemUnsupported` (unequipping an NPC is not
modelled), `BudgetExceeded` (the durability records) and `RevisionExhausted`. Each change is one revision. A seed that lists an NPC
holding added items as a container is `InventoryMismatch`, and one whose holder's current seed items and added items would exceed
512 is `BudgetExceeded` (a restore followed by an attach).

**Snapshot schema 29.** `"inventory":{…,"made":[…],"npcs":[{"npc":"oid:…","items":[{"entity","type","prototype","name","description"}]}]}`,
the NPCs in ordinal order. Decoding refuses an unordered NPC list or an empty item list, and an item held twice in the whole
inventory (`NotCanonical`); restore refuses an item that is not `made:n`, a `made:` NPC id, a deleted item or one held twice
(`InvalidSnapshot`), a type the lists do not admit (`InvalidItem`) and more than the caller's `MaxNpcItems` (`BudgetExceeded`).
Since the PC can take a made item from an NPC, the hero's made list keeps entry order and needs no hero. Schemas 1..28 are refused,
not migrated.

**Differences from the original (both modes).**

| Original 1.0.7.4 | Steamwoven |
| --- | --- |
| Changes are saved with the map the NPC stands on (NI1–NI3). | Changes are saved by NPC identity; the visible result is the same because the NPC carries its items (NI4). |
| A reference to an item no longer found is kept as a null list element with the count unchanged (NI2). | The list holds only items it has; a deleted item is gone from it. |
| Closing a barter window restocks a non-follower NPC with an inventory source after 12–24 game hours, destroying items without the persistent flag (NI6, F1; corrected by BT4 and RK2: the timer is set at every open). | The restock event (ARC-WORLD-094, [barter runtime](barter-runtime.md#restock-event-arc-world-094)); the AREA viewer schedules it at every barter and loot open (ARC-WORLD-096). |
| A teleport clears an NPC's substitute-inventory link; an insert makes the NPC re-equip its best items (NI7, F1). | Not modelled. |
| Any object type can enter an NPC's list. | Gold, keys and key rings are refused, as for the PC's lists; ammunition merges into the NPC's stack of its prototype (ARC-WORLD-066). |

Not modelled: moving a seed item from one NPC to another (no caller yet), giving an NPC an item from the PC (barter, drop on a
follower), NPC worn slots, gold, and a visiting companion's items in the map-bound calls of a map whose seed does not list it (the barter window excepted, ARC-WORLD-3118).

## Placed items (ARC-WORLD-059)

The hero's items the session placed on the ground (`GameSessionOwner.QueryPlacedItems`, `SessionPlacedItem`), read in the
executable first ([ledger "Placed items"](../research/ledger/items.md#placed-items-arc-world-059), PI1–PI6). In the
original an item is an object anywhere: dropping it removes it from the owner (unwielding a worn one) and moves it to a tile,
where it stays until something picks it up or its 48-hour decay destroys it; a pickup is the ordinary transfer.

**Store.** One saved list of the hero's items on the ground (only a session with a hero has one), in placing order: entity, kind, the hero's list it came from
(`SessionPlacedSource`: the kit, a world item the PC took, with the map whose seed lists it, or a made item), and the map and cell
it lies on. It admits the types the hero's lists admit (wider than the worn items the drops place today, for a later drop
command). A placed item stays taken, so its seed never shows it at its seed position, and it counts in the hero's item budget
(`MaxPcItems`), so it can always come back. `QueryMapInventory` lists the map's placed items after the NPC drops.

- **Placing** (internal, the hero's critical drops of [ARC-COMBAT-009](hero-attack-runtime.md#critical-effects-arc-combat-009):
  `DropHelmet`, `DropWeapon` and the arm-hit drops): the item leaves its wear slot and its list and lies on the tile the
  [drop cell](#drop-cell-arc-combat-010) chooses, or at the hero's cell when the host passes no map
  (`SessionCriticalStepOutcome.Dropped`). One revision. A no-drop item is refused, as in the original (PI1); an item an earlier
  step of the same attack broke has already left its slot (`NoItem`); a made item's number is never handed out again while it lies
  on the ground.
- **Assembly products** (ARC-WORLD-070): a schematic product unit the inventory's insert check refuses lies on the crafter's tile
  as a made item (source `made`): the hero's tile for the hero's assembly, the tile the host gives for an NPC crafter (on the hero's
  map), as the original's creation on the tile and unchecked transfer leave it (SA3–SA5). An ammunition unit there is a stack of its
  own with its units. The assembly checks the store's bound before it spends anything ([assembly](schematic-assembly-runtime.md)).
- **Pickup**: `PickUpItem` of a placed item on the actor's map returns it to its list, a kit item at its kit number's place (the
  kit stays in number order), a taken or made item at the end of its list. The host's insert check runs first (a call back into the
  owner from it is `StaleVersion`); one revision. On another map the item is not available.
- **Created items** (ARC-WORLD-081, [template sector plants](template-sector-plants-runtime.md)): an item a script's create object adds
  to the ground (`CreateGroundItem`, the plant generator's herbs) is a placed item of source `created`: a new `made:n` entity numbered with
  the made items, with its prototype's weight facts and complexity, on the tile the create action draws next to its object. It needs no
  hero, schedules no decay (ID9) and has a budget of its own (`MaxCreatedItems`, 8,192) outside the hero's; picked up it joins the made
  items, which needs room in the hero's budget. Under Steamwoven no item is created next to an object with 6 created items within 3 tiles
  (`CreatedItemRules`).
- **Deletion**: a placed item the durability rules delete leaves the store.
- **Seeds**: an attached seed must still list a placed world item of its map with its saved kind and no seed may list a placed kit
  or made item (`InventoryMismatch`), as for the hero's lists.

**Snapshot schema 32.** `"inventory":{…,"npcs":[…],"placed":[{"entity","source":"kit"|"pc"|"made","origin","map","x","y","type",
"prototype","name","description"}]}`, `origin` the seed map of a `pc` item and 0 otherwise, the cell 0..2^24 − 1 per coordinate.
Decoding refuses an unknown source or out-of-range value (`OutOfRange`), an origin that does not fit the source or an item held twice
in the whole inventory (`NotCanonical`) and more than the policy's hero items (`BudgetExceeded`); restore refuses any placed item without a hero, a kit item with no kit number or one beyond the kit's numbers (its kit, placed
and deleted items together), a made item without a made number, a world item with a kit or made id, a deleted item (`InvalidSnapshot`)
and a type the lists do not admit (`InvalidItem`). The kit's numbers skip its placed items as they skip its deleted ones. Schemas
1..31 are refused, not migrated.

**Not ready (the original's behaviour, PI1–PI6).**

- Junk piles (PI3): since ARC-WORLD-063 a placed item goes through the drop's placement ([junk piles](#junk-piles-arc-world-063)).
- The scripts and side effects of the removal (PI2) and of the pickup (PI6): item spells, the wield-off and remove-item scripts (a critter's critical drop runs the drop script, below, and an NPC's or the hero's worn item's wield-off (25) and remove-item (28) scripts, ARC-SCRIPT-2856 and ARC-SCRIPT-2886; the same 25 then 28 for the worn items a script destroy's walk, destroy item named, a body's walk and a restock clear take out, ARC-SCRIPT-2885),
  and the get and insert-item scripts of a pickup, none of which runs. A world item whose attached origin seed shows a drop script
  is not dropped by a critter's critical drop unless the host's drop script runs it (`DropScript`, ARC-SCRIPT-2853: skip default refuses the drop, `Refused`; a script the host cannot apply or no host, `ScriptUnsupported`), one with a get or insert-item script is not picked up (`ItemScriptUnsupported`); with its
  seed not attached, and for a kit or made item, the session does not know the scripts and the item goes unchecked. The light and armour of the hero follow its equipment at once.
- The PC's re-wield after losing a weapon (PI5): with the "auto switch" setting on, the original wields the best weapon. Since
  ARC-COMBAT-1720 the session has the step (`WieldBestWeapon`, [best items](equipment-runtime.md#best-items-arc-world-034)) and the
  viewer calls it, with the setting on, after a wear that broke the hero's weapon, an attack the ammunition check refused (0x4B3170) and an NPC's strike that took the
  hero's weapon (0x4B4390, ARC-COMBAT-1738); the hero's self-hit, a spell, trap or script hit are not built, and the slot stays empty there.
- NPC made items (ARC-WORLD-055) can use this store later. The viewer draws placed items and picks
  them up since ARC-RENDER-033 ([ground items](#ground-items-in-the-viewer-arc-render-033)); a placed made item (an assembly
  product left on the tile) is drawn from its product prototype's art since ARC-WORLD-070.

## Item decay (ARC-WORLD-062)

Requirement: [FR-ITEM-012](../requirements/items-economy.md#fr-item-012). Read in the retail 1.0.7.4 executable by two readers
([ledger "Item decay"](../research/ledger/items.md#item-decay-arc-world-062), ID1–ID9; it settles PI4).

| Rule | Value |
| --- | --- |
| Event (ID1) | The original's Item Decay (kind 25): saved, on the game clock (the column Poison, Resting and Lock use). `SessionEventKinds.ItemDecay` (`item.decay`): scoped to the item's map, so it fires while that map is the actor's (late, on return, as the Lock event); entity the item; values the game time it was scheduled at and its delay. |
| Which items (ID2, ID4, ID7, ID9) | Every drop on the ground schedules it two game days (172,800,000 ms) on, replacing the item's earlier one: the hero's critical drops, an NPC's items a critical dropped, and the items a broken container spilled. An item a map's data lays on the ground gets none (nothing dropped it), and neither does an assembly product the inventory refused (ARC-WORLD-070, ammunition stacks among them): the original creates it on the crafter's tile and the refused transfer leaves it there, and neither path reaches the schedule (ID9), so it lies until it is moved. |
| No decay (ID2, ID5) | An item with item flag 0x20000 (`SessionItemFacts.NoDecay`, from the map data) gets no event; no installed item prototype carries it (opt-in census `ItemDecayFlagCensusTests`: 0 of 798), but map instances can (two Crash Site items, [item inventory](../research/crash-site-item-inventory.md)). The switch that turns decay off is the world editor's; the game never sets it. |
| Pickup (ID4) | A pickup of the item ends its event (`PickUpItem`); a deleted item loses it too. |
| Firing (ID3, ID6) | An item that still lies loose on its map is deleted (one revision; it leaves the placed items or its seed's ground). While the host's loot window shows a container (`ISessionLootWindowWorld.LootWindowShowsContainer`) the event waits a minute more each time. An item no longer on the ground: nothing. The pass reports each firing (`SessionEventPassResult.Decays`: map, item, `ItemDecayStep`). Two session limits postpone a deletion a minute (`Postponed`): a seed item whose map's seed is not attached yet, and a full deleted-item budget (`MaxDurabilityRecords`; every decayed item stays in the saved deleted list). |
| Budget | At most 8,192 pending decay events (`MaxItemDecays`, project bound; the original has none): a drop beyond it does not decay. |

`QueryItemDecays` lists the pending events. No snapshot change: the event queue is already saved (schema 9 form); a restore checks
each decay event (one item on a map, set no later than the saved clock, the decay delay or the retry's, one per item). The viewer's
event pump fires them like the other kinds (`FrameStepReport.Decays`, log `ARC-WORLD-062 item-decay`) and redraws the ground.

Since ARC-WORLD-063 an item in a junk pile decays as a loose one, and a drop into a pile restarts its two days, while an item swept
into a new pile keeps its time ([junk piles](#junk-piles-arc-world-063)). The player's own drop is the window's `DropHeroItem` (ARC-UI-025). Not ready: the viewer's loot window shows the session's junk piles and (ARC-WORLD-101) the map's containers (a dead NPC it loots does not count, as in the original's
counter), so only an open pile or container delays a decay; the item flag of the hero's kit and made items is not known (no
installed prototype sets it); the session reads the no-decay flag only from an attached seed's row, so a seed item the PC took
whose map's seed is not attached (after a restore on another map) reads as decaying and is deleted two days after a drop, where the
original keeps it; a container broken while its map's seed is not attached spills items that get no event; while the deleted-item
budget (`MaxDurabilityRecords`) is full, each due decay postpones itself a minute at a time, so a long wait runs it once a game
minute; a dead body's decay (kind 24, a day after death) and the destruction of a holder, which drop their items
with decay in the original (ID8), are not modelled.

## Room of a take (ARC-DEV-333)

The original's insert check (0x466510) asks, after the weight and the Technophobia test, for a free place in the grid for the item's size
(code 2, ledger [WE4, WE10](../research/ledger/items.md); [BG4](../research/ledger/character.md)); every take into the PC's list runs it, and a
refusal leaves the item where it was. Bug B48 ("Take all" took 45 items into a full grid, the ones that did not fit were held in no cell) was the
missing room test. The session now asks it for every item that enters the hero's list from outside, after the host's answer and the weight and
background refusals, so a missing place answers `SessionInsertVerdict.NoRoom` (`GameSessionFailure.InsertRefused`, nothing changes, the version
stays): a ground pickup, a loot from a body or a container, a take from an NPC (steal, purchase) and every other `Take` caller; a new ammunition stack of units taken from an NPC (`TakeAmmoFromNpc`); an assembly product unit, which then lies on
the crafter's tile like a unit refused for weight (SA3). The place is the first free one for the item's size (`HeroGridRoom`, the same search
the placement uses, `FindSlot`, 0x4675A0), counted against the grid as it is when the take is asked, so the items of one Take all take the places
one after the other, and an assembly counts its components' cells as free and the places of its earlier units as taken. An ammunition stack that
joins a held stack of its prototype and a key that folds onto the ring need no place (WE4). Take all is the loop of the single take: it moves the
items that fit in list order and leaves the rest in the body, named in its message ("no free room in the grid"). The refusal's text in the original
is a message of the item message file chosen by the code (WE10); the viewer keeps its own wording. Not covered: the hero's gold pile that takes
a cell of its own when a restored save's coin has none (such a pile still waits outside the grid; the first coin of a live hero is ARC-WORLD-1132 below). A script's create object makes ground
items only (ARC-WORLD-081), so it never enters the hero's list. The dialog's `IN` (`GiveItemToPc`, ARC-WORLD-1131) runs the same test, but its refusal is not a failure: the original's negative `IN` (0x415BA0, case 9)
transfers the item to the PC, and when the PC is not its holder afterwards takes it out of the NPC's list (0x466E00) and places it at the PC's location
(0x466E50: a junk pile on the tile or a new one, then the decay timer). The session does this for every refusal of the insert check (no room, too heavy,
Technophobia): the result is `Changed` with the refusing verdict (`InventoryMutationResult.Verdict`), the item joins the hero's placed items on the shown map's
tile of the actor (`QueryPlacedItems`, source as the item came: pc, made), one revision, no insert-item script, no ring or stack merge, no pickup
trigger; an NPC of another map (a carried companion) keeps its list's origin on the placed item. The dialog port answers Applied and the conversation goes on. A tile the ground cannot hold
(`DropTileUnfit`) or a full list budget refuses the IN with no effect. Not covered: a transfer script's skip default (the original drops the item in that case too; the session still
answers `Declined`). An assembly's room test (ARC-WORLD-1133) lets the hero's items that wait outside the grid (the session's own state: the original keeps every listed
item in a cell) take the cells its components free first, in list order and by their size (`WaitingItemCells`, the same search as the next placement),
before it places the products, so a product that would only have fitted in a cell such an item takes lies on the crafter's tile like a refused unit.
An NPC crafter has no grid and is not affected.

**First gold pile (ARC-WORLD-1132).** The hero's gold is an item of its list (ledger [HG1](../research/ledger/ui-audio.md#hero-gold-pile-in-the-grid-arc-ui-090)), so
coins that reach a hero with no pile come as a new pile object that the transfer (0x464830) creates on the receiver's tile (0x464970) and moves in through the
insert path (0x4617F0 → 0x461810 → check 0x466510, [NT1](../research/ledger/items.md)). The check asks the grid for a free place of the pile's size
(code 2, WE4); with none the insert is refused, the new pile stays on the ground and the call still reports success, so the coins never reach the hero. The
session does this for every way coins are *transferred* to a hero with no coin: the dialog's `$$ n` (`GiveGoldToHero`) and a take from an NPC's or a
container's pile (`TakeGoldFromNpc`, and so a barter sale and a successful steal). The pile that did not fit is a made gold item with a coin record on the
hero's tile (`QueryPlacedItems`, source made; `InventoryMutationResult.Item` names it), one revision, no junk pile and no decay timer (a created object, not
a drop, as an assembly's refused product, ID9). A pickup or loot of a pile already lying on the ground is not a transfer: it runs the insert path on the pile
itself (JP8), so a hero with no pile and no free place is refused (`InsertRefused`, verdict `NoRoom`, no revision) and the pile stays where it lies.
The pile's kind is the NPC's seed pile's, else the neutral gold kind
(`SessionHeroGrid.GoldKind`, names 0). A tile the ground cannot hold (`DropTileUnfit`), a full coin-record or hero item budget or no free made number refuses the call
before any change. A hero that already holds a pile grows it by a plain add with no check, and the weight test is not modelled for gold (weight 0).
Not covered: an insert-item script on the hero and the refusal message the original shows for the PC (0x4673F0).

## Junk piles (ARC-WORLD-063)

Requirement: [FR-ITEM-012](../requirements/items-economy.md#fr-item-012). Read in the retail 1.0.7.4 executable by two readers
([ledger "Junk piles"](../research/ledger/items.md#junk-piles-arc-world-063), JP1–JP8; it settles PI3).

**Rules (Simulation).** `InventoryGridRules` is the original's holder grid: ten columns, 96 rows for a container and 12 for a critter;
an item takes its inventory art's frame in 32-pixel cells (`CellsOf`, 1 × 1 when the art does not read) from its slot (row × 10 +
column; worn and quick slots take no cell, `Occupied`), and `FindSlot` gives the first top-left cell, row by row, where its whole
rectangle is free (JP3). `JunkPileRules.Place` is the drop's placement (0x466EF0, JP1): the tile's junk piles in order, the first with
room takes the item; else, when loose items lie there, a new pile is made, each loose item that may move enters it at its first free
slot in tile order and then the dropped item when it fits (else it lies loose beside the pile; a pile that took nothing stays empty, as
in the original); else the item lies loose. `JunkPileRules.Dissolve` is the check after an item left a pile (0x463540, JP4): no item
removes the pile, one item releases it, more keep it.

**Session.** A pile the session made is a `SessionJunkPile`: entity `junk:n` (the number after the highest standing pile), map, cell
and its items in the pile's order, each with its slot and size (`SessionJunkPileItem`). An item in a pile keeps its own ground record
(a placed item, an NPC's dropped item, a spilled or seed item of the cell), so its decay and its map stay as they were; the pile only
says it lies in the pile, not on the ground. A pile item a host's map change moved off the pile's cell is neither shown in the pile
nor looted from it.

| Step | Value |
| --- | --- |
| Drop (JP1, JP2) | Every drop the session makes goes through the placement before its decay is scheduled: the hero's and an NPC's critical drops and a broken container's spill, whose items drop one by one, the last first (0x463730), so from the second item on they gather into a pile on the container's cell. An item swept into a new pile keeps its decay event (in a pile it still decays, JP6); the dropped item's starts over. A thrown item's plain placement (ARC-COMBAT-017) and an assembly product the inventory refused (ARC-WORLD-070, created on the tile) make no pile, as in the original; a later drop on their tile sweeps them in. An item in flight is not on the tile. |
| Size | The frame of the item's inventory art in cells (`CellsOf`, 0x461780) when the host's art source answers for it (`AttachItemArtSizes`, ARC-UI-055); else `SessionItemFacts.InventoryWidth` and `InventoryHeight` of the attached seed that lists the item (a seed with a side below 1 or above 2^27 cells is refused); 1 × 1 otherwise (a kit or made item without an art source, a seed not attached). |
| Ground | `QueryMapInventory(map).Ground` leaves out the items in piles; `JunkPiles` lists the map's piles with their items. `QueryJunkPiles` lists every pile. An item in a pile is not picked up from the ground (`PickUpItem`: `ItemNotAvailable`) and cannot be attacked there (`ObjectUnavailable`). |
| Loot (JP6) | `LootItem(pile, item)` takes an item from a pile of the actor's map as a loot from a holder (the recipient's transfer, the item's remove-item and insert-item scripts through the host; a placed item goes back to the list it came from, as its pickup does); the item lies in its pile slot, not in a worn slot, and its decay ends. The pile stays while the host's loot window shows it. |
| Close (JP4, JP5) | `CloseJunkPile(expected, pile)` is the host's loot window closing on the pile: a pile left with no item is removed, one left with one item is removed and the item dropped again on its cell through the placement, with a new decay event two days on. One revision when the pile went; `UnknownHolder` for a pile not on the actor's map. |
| Close of a pile of the map's data (JP4, JP5, JP9; ARC-DEV-310, bug B04) | The original's check does not ask where a pile came from, so a junk pile that lies in the map's data (a standing container of prototype 3043, such as the Crash Site cave's, which holds 3 items) goes the same way: `CloseMapJunkPile(expected, target)` (the host's `AreaContainerLoot.DissolveJunkPile` after the container's lid close) records a pile left with no item as destroyed with its cell as the spill cell, the map changes a broken container writes (`object.condition`, no schema step), so it is not drawn, not lootable and stays gone across a save; a pile left with one item does the same and the item is dropped on the cell through the placement with a new decay event (it joins loose items there into a pile of the session's). Two items or more, a pile already gone and a pile that holds an item a barter sold into it (not modelled) change nothing; `InvalidObject` for a target that is no junk pile of the data, `UnknownHolder` for one that is not a container of the actor's map's seed. Take all does not close the window (JP9), so the emptied pile stands, empty, until the window closes. Not modelled: the check when an item leaves a map pile with no window open (a decay in it; the map's piles hold items that never decay), and a window that closes after the session was replaced (a quick load) or on another map than the actor's, where the pile is left as it is. |
| Destroy (JP7) | An item destroyed in a pile (the decay, or any other deletion: ARC-DEV-086, `DestroyObject`) leaves it, and (no container shown in the loot window, or the decay would have waited) a pile left with one item or none dissolves at once, the remaining item's two days starting over. The window's own loot never dissolves its pile, `CloseJunkPile` does. |
| Bounds | At most 4,096 piles and 8,192 items in them (`SessionJunkPile.MaxPiles`, `MaxItems`, project bounds; the original has none): beyond them a drop lies loose. |

**Snapshot schema 46.** `"inventory":{…,"junkPiles":[{"entity","map","x","y","items":[{"entity","slot","width","height"}]}]}` in the
order the piles were made. Decoding refuses a width outside 1..10, a height outside 1..96, a slot outside 0..959 and a cell coordinate
outside 0..2^24 − 1 (`OutOfRange`) and more than the bounds (`BudgetExceeded`); restore refuses a pile entity that is not `junk:n` or
repeats, an item in two piles, a rectangle past the grid or overlapping another, a placed item away from its pile's map and cell, a
deleted item, an item the hero holds or an NPC's added item (`InvalidSnapshot`). An attached seed must list every item of a pile on its
map that is no placed item (a window seed only when its window holds it), and no seed of another map may (`InventoryMismatch`). Schemas 1..45 are refused, not migrated.

**Viewer.** The AREA viewer draws each pile at its cell from the pile prototype's current art (prototype 3043, read through the
object field reader; flat, as every placed pile of the interiors carries `OF_FLAT`) instead of its items, picks it as a junk pile
(`AreaInventoryTargetKind.JunkPile`; a turn-based round charges the use of an object), opens it in the loot window titled
`[junk pile]` (a project label: the pile has no map row to name it), takes items from it there and runs `CloseJunkPile` when the window
closes. While the window shows a pile, the event pump's world reports a container shown (`ISessionLootWindowWorld`), so the items'
decay waits a minute (ID3, ID6). Native smoke `ARC-DEV-310 native-smoke map-pile` (the same stage, first: a junk pile of the map's data is opened as a container, Take all, still standing while the window shows it, gone and not drawn after the close; on 2026-11-08 the Crash Site cave's pile of 3 items); native smoke `ARC-WORLD-063 native-smoke junk-pile` (map mode, `ARC_AREA_MAP_SMOKE_ONLY=junk`, run last
in the map battery): the hero breaks the shown map's weakest container with two items or more through the session's object attack (ARC-BUG-1536: a hero
whose weapon an earlier stage of the map chain destroyed takes the container down to its damage threshold and no further, so after 20 attacks in a
row without a hit point lost the container takes its remaining hit points as damage that is no attack, the same break and spill);
its items gather into one pile on its cell, which is drawn from the pile art, picked, opened with its items listed and their decay
held; one item is taken and the window closed. On 2026-10-28: Crash Site cave, a chest of 5 items, all 5 in the pile; Elven Ruins, a
chest of 3 (one with the no-decay flag), all 3 in the pile.

**Not ready.**

- The map data's own junk piles (15 on 10 interiors, census) take no dropped item (an NPC's pickup does take from them, ARC-AI-1486): a drop on their tile ignores them (it may make a session
  pile beside them); since ARC-WORLD-101 they open as containers of the map (the attempt always admits a junk pile, LK8), but they do
  not dissolve on closing.
- The tile's list order is the session's ground order (seed items, spilled, dropped, then placed items), not the original's object list
  order, which was not read; it decides only the order in a new pile and its slots.
- The scripts the original runs on the way: the get script of a swept item and the insert-item event of every insertion. An item whose
  attached seed shows a get or insert-item script is not swept (it stays loose beside the pile), and a dropped item with an
  insert-item script lies loose and makes no pile.
- Gold and ammunition do not merge into a stack of their kind in the pile (JP8); each stays an item of its own.
- The viewer's seeds leave every item's facts 1 × 1; its items take their art's size through the art source (ARC-UI-055), so a pile
  only of seed items with no art source attached fills only after 960 items.
- A deletion other than the decay (`DestroyItem`, a break) takes the item out of its pile without the dissolve check, so a pile can
  be left with one item until its window closes or another of its items decays.
- The network paths and the dissolve's network guard.

## Drop cell (ARC-COMBAT-010)

Where a dropped item lands, read in the executable first ([ledger "Drop cell"](../research/ledger/combat.md#drop-cell-arc-combat-010),
DC1–DC8, with CR14). `DropPlacementRules` (Simulation) is the drop's draws and tries: `Distance` draws 2..4 (0x461A90), `Place` makes
up to ten tries of 0x4F4E40, each with a direction draw 0..8 (8 stays), over an injected `IDropMap`: the original's step
(0x4B8FF0), its path request with the drop's flags (0x41F3C0, flags 0x10) for a `DropOwner` (an object, the PC side, or an NPC on
its own; the trap rule tells them apart) and its PC-or-NPC test on a tile (0x4407C0, the dead included).

**Session.** A critical drop (`SessionAttack.DropMap`, `SessionObjectAttack.DropMap`) with a map draws the distance from the
session generator before the drop's refusals (the drop script, the no-drop flag), then the tries after them; the drop script (point 5, ARC-SCRIPT-2853, ledger PI1, VP6) is asked through the session's `DropScript` host with the holder (the hero's handle, or the NPC) as the triggerer and the item as the attachee, and skip default refuses the drop (`Refused`, no look-for bit); after the no-drop check an NPC's worn seed item with a wield-off (25) or remove-item (28) script runs them (the removal 0x467860: 25 then 28, the NPC as the triggerer and the wearer of the effect changes) through the session's `NpcWearerScripts` host context (`SessionNpcWieldScripts`), planned before the drop's write and applied with it (their printed lines and eye candy go to `ItemRunOutput`); the hero's worn item with such a script runs them the same way (ARC-SCRIPT-2886, the hero as the triggerer, through the session's `HeroWearerScripts` item script host); no context for the NPC or the hero or a run the session cannot apply leaves the item (`ScriptUnsupported`); the generator's
exhaustion is `RandomExhausted`. The hero, and an NPC following it, are the PC side; any other NPC is an NPC on its own. The hero's
item joins its placed items on that tile; an NPC's seed item is the `item.dropped` map change at that tile. Without a map the item
lands on its holder's cell and nothing is drawn (a host without one).

**Pushed items (ARC-MAGIC-1230).** An item lying on the hero's map can be moved on the ground (`QueryGroundItem`, `MoveGroundItem`, the original's object move 0x43E770, which writes only the location: no flag, no hit points, no junk pile, no decay). A placed item takes the cell in its own record; a map-data item (a seed's loose item, a broken container's spilled item, an NPC's dropped item) keeps the cell as the object field `item.moved` (`SessionItemMove`, a packed cell as `item.dropped`'s, `QueryMovedItems`), which the map inventory's ground, `LooseCell`, the pick-up and the residency overlay read before the item's own cell; the field stays harmless after a pick-up (the taken item is no ground item). No schema step: the object rows keep their form. Unseen Force's push reaches it through `AreaKnockbackItems` ([goal table, tail T7](../architecture/goal-table.md)).

**Viewer port.** `AreaDropMap` (viewer Core) is the AREA viewer's map for the hero's attacks on NPCs and objects
(`AreaDropMapNow`): the path's gate on the target's tile, the direct attempt and the 64 × 64 search exactly as read, with the
step tests of the shown map's T0 step evidence (built on the same tile and traversal functions) and the doors' runtime passage;
critters never block a step (mode 4), and an object with flag 0x400 does not close the target's tile; containers and scenery the session destroyed or switched off no longer block; a trap's
flags come from its map data (slot 36). PCs and NPCs on a tile are the map's critter records that have not moved, dead or alive,
plus the hero's and the companions' live cells.

**Not ready.**

- The NPC search budget per real second (DCA2): the session never runs out of it.
- The map's bounds are the step evidence the viewer holds (the exterior's nine sectors, the cave); a tile or step the evidence
  cannot settle is treated as blocked (DCA3).
- A generator that runs out during the tries (after about 2^63 draws) leaves the distance draw made and the item where it was
  (`RandomExhausted`); the drop's draws are not taken on trial.

## Ground items in the viewer (ARC-RENDER-033)

The AREA viewer draws what the session holds on the ground but no T0 body drew, and the art a T0 body changes to:

- **Items.** Every item in `QueryMapInventory(map).Ground` that does not lie on its own T0 body's cell: spilled from a container
  (ARC-COMBAT-008), dropped by an NPC (ARC-COMBAT-009/010) or placed by the hero (ARC-WORLD-059). Its ground ART is its map row's
  current art (field 0; for a placed world item, the row of the map whose seed lists it) or, for a kit item, its prototype's
  (`AreaStartingHero.KitArt`), or, for a made item (ARC-WORLD-070), its product prototype's current art from the schematic data
  (`AreaSchematicData.ArtOf`). The viewer's own insert check accepts every item, so in play a product reaches the ground only with a
  refusing host; the native smoke checks the store and that the product's art reads. It is placed on its cell as the plan places a body there with no object offset and scale 100
  (`AreaScenePlan.Place`; whether the original keeps a dropped item's offsets and scale was not read in the executable: NOT
  VERIFIED), flat when its object flags carry OF_FLAT (0x4), and joins the moved bodies: an upright one the upright order,
  a flat one the flat commands in plan order (`AreaScenePlan.WithMovingFlat`). It is lit where it lies by its own art's channel.
  A click picks it up as any ground item, walking to its cell; a placed item without a row of the shown map (a kit item, a world
  item from another map) is named and picked under a key of its own, with its origin row's fields when there is one.
- **Destroyed ART.** A scenery or trap the session busted is drawn from its destroyed ART (field 24) at its T0 anchor
  (`AreaScenePlan.Repose`, as a fallen pose).
- **Art.** `AreaExtraArt` reads an art id the T0 snapshot did not decode from the game's content stack in its search order
  (ARC-DATA-018), named by the area owner's rules for classes 4, 6 and 7 (`AreaObjectArt`, shared with the owner) and decoded by
  its frame rules (the selected frame, group 0 of a single-rotation ART, the ART's own extent); explicit limits
  (`AreaExtraArtLimits`, for the whole viewer run of one module: 4,096 art ids, 64 MiB of pixels, 16 MiB per file; the opt-in
  census `AreaExtraArtOriginalTests` reads every destroyed ART and every held item's ground ART of the 80 interiors: 4 destroyed
  ARTs, 2,449 held items, 328 distinct art ids, none failing). An art id that does not resolve or read leaves the
  body as it was and is logged once (`ARC-RENDER-033 extra-art`).

Since ARC-WORLD-063 a junk pile is drawn in place of its items ([junk piles](#junk-piles-arc-world-063)).

**Not ready.** A busted window's busted ART. The items' decay is in since ARC-WORLD-062 (the event pump redraws the ground).

## Evidence

- Synthetic (ARC-WORLD-063): [JunkPileRulesTests](../../tests/Arcanum.Simulation.Tests/Items/JunkPileRulesTests.cs) (cell sizes and
  the unsigned wrap, the grid's rectangles and skipped slots, the first free slot, joining the first pile with room, a new pile with an
  item that may not move and one too big, the dissolve), [JunkPileStateTests](../../tests/Arcanum.Session.Tests/JunkPileStateTests.cs)
  (a second drop gathers both, the swept item's kept time, a later drop joins, another cell stays loose, loot and the window's close,
  an emptied pile, another map, a decay that dissolves a pile of two, items with scripts left loose, the snapshot and every refusal,
  the attach checks), the spill into a pile and its close in [ObjectAttackStateTests](../../tests/Arcanum.Session.Tests/ObjectAttackStateTests.cs),
  the viewer's pile window and decay hold in [AreaInventoryHostTests](../../tests/Arcanum.TerrainViewer.Tests/AreaInventoryHostTests.cs);
  opt-in census [JunkPileCensusOriginalTests](../../tests/Arcanum.TerrainViewer.Tests/JunkPileCensusOriginalTests.cs) (15 placed piles,
  none blocking); native smoke `ARC-WORLD-063 native-smoke junk-pile`.

- Synthetic (ARC-SCRIPT-041): [ItemEventScriptTests](../../tests/Arcanum.Session.Tests/ItemEventScriptTests.cs) (the get script
  before the insert check, its decline with and without effects, the transfer, remove-item and insert-item order of a loot with
  each run reading the earlier writes, the PC's declining transfer script on loot and `IN`, a refused later run or insert check
  dropping every write, a get read by insert-item, taking from an NPC with a host, a call back during a gate, a declined pickup
  keeping the item's decay event) and the viewer's `Moved`/`NotMoved` in
  [AreaInventoryHostTests](../../tests/Arcanum.TerrainViewer.Tests/AreaInventoryHostTests.cs). Native smoke: the exterior loot logs `scripts=32::NoScript,21:1341:ReturnSkipDefault` for the amulet.

- Synthetic (ARC-COMBAT-010): [DropPlacementRulesTests](../../tests/Arcanum.Simulation.Tests/Items/DropPlacementRulesTests.cs)
  (the distance, the critter test of the walk, no path, a long path, the direct attempt's partial count, tries off the map,
  direction 8), [AreaDropMapTests](../../tests/Arcanum.TerrainViewer.Tests/AreaDropMapTests.cs) (rotation and step tables,
  the direct attempt, the target's gate, the search round a container, traps by owner, a window, critters on a tile) and the
  session's drops through a map in [CriticalEffectsStateTests](../../tests/Arcanum.Session.Tests/CriticalEffectsStateTests.cs).

- Synthetic (ARC-WORLD-059): [PlacedItemsStateTests](../../tests/Arcanum.Session.Tests/PlacedItemsStateTests.cs) (placing a kit and a
  taken item, the view, another map, the insert check, the kit's order on return, save and restore, the attach check, deletion, codec and
  restore refusals) and the hero's critical drop in [CriticalEffectsStateTests](../../tests/Arcanum.Session.Tests/CriticalEffectsStateTests.cs).

- Synthetic (ARC-WORLD-055): [NpcInventoryStateTests](../../tests/Arcanum.Session.Tests/NpcInventoryStateTests.cs) (addition and numbering,
  every refusal, a party record without a seed, loot and `IN` of added items, deletion with the durability rules, round trip, the
  attach check, codec and restore refusals).
- Synthetic: [InventoryStateTests](../../tests/Arcanum.Session.Tests/InventoryStateTests.cs) (seed validation and
  copy, every refusal and its order, the insert check, list order, budget, codec and restore refusals, attach after
  restore, the dialog port) and [DialogInventoryTests](../../tests/Arcanum.Scripting.Tests/Dialog/DialogInventoryTests.cs)
  (routing, capability, rejected and throwing port).
- Real data (opt-in `RealData`, `ARC_AREA_CONVERSATION_GAME_ROOT`):
  [CrashSiteInventoryTests](../../tests/Arcanum.TerrainViewer.Tests/CrashSiteInventoryTests.cs). The step rules mark
  exactly the four research NPCs with items dead (5699, 4457, 4200, 1201). Virgil's original first conversation, opened
  by the viewer's conversation host, runs node 1's `IN -2804` through the session port and the ring (1714) enters the
  PC's list, leaving his weapon and worn armor. Without script content five of the six dead-NPC items loot and 2527 is
  refused for its `SAP_INSERT_ITEM` script; with the content (ARC-SCRIPT-013) it loots too, script 1341 prints line 1 of
  its DLG and writes no flag, and once global flag 1085 is set it returns at once and prints nothing. All 37 core ground items pick up. Virgil is not lootable; a chest is (since ARC-WORLD-101), but only for its own items. A save and load keep the 43 items,
  the re-attached seed agrees, and a second `IN -2804` changes nothing.

## Ammunition (ARC-WORLD-066)

Ammunition (type 6) is admitted to the hero's and NPCs' lists since ARC-WORLD-066 (snapshot schema 34), read in the executable
([ledger AM1–AM7](../research/ledger/items.md#ammunition-arc-world-066), anomalies AMA1–AMA6). Gold, keys and key
rings stay out. Code: [`GameSessionAmmo`](../../src/Arcanum.Session/GameSessionAmmo.cs) and the transfers above; tests
[`AmmoStateTests`](../../tests/Arcanum.Session.Tests/AmmoStateTests.cs) and
[`AreaAmmoLinesTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaAmmoLinesTests.cs).

- **Facts.** A seed's ammunition item carries its unit count (`Quantity`, field 0x8E) and its ammunition type (`AmmoType`,
  field 0x8F, 0..3: arrows, bullets, charge cells, fuel). A type outside 0..3 or a negative count refuses the seed (AMA2, AMA3:
  the original indexes past its tables). The Data reader gives the type (`AreaInventoryItem.AmmoType`).
- **State.** The session keeps a `SessionAmmoRecord(entity, type, units)` for every ammunition item the hero holds or placed,
  every one added to an NPC, and any other whose units it changed (a seed stack an NPC spent from or received into); by entity in
  ordinal order, at most `GameSessionLimits.MaxAmmoRecords` (4,096). A record's units are 0 or more (the original never removes a
  stack at 0 by itself). `QueryAmmo(entity)` gives a tracked stack or a seed stack's units; `QueryHeroAmmo()` the hero's stacks in
  list order; the map views (`QueryMapInventory`) show the current units.
- **Insertion (AM1, AM2).** Pickup, loot, negative `IN` and a placed item picked up again: an ammunition item merges into the
  hero's first held stack of the same prototype (its units added, the inserted item deleted with the durability deletion, as the
  original destroys it); without one it enters the list as a new stack. The insert check still runs. A sum that does not fit 32 bits
  is refused (`BudgetExceeded`, AMA1). `AddItemToNpc` takes ammunition with its type and units and merges it into the NPC's stack of
  the prototype in the same way.
- **Spending (AM4).** `SpendAmmo(expected, holder, type, count)` spends from the hero's, an NPC's or (ARC-TECHNOLOGY-009,
  `SessionAmmoHolder.ContainerOf`) a container's first stack of the type (the original's one stack a critter field names; AMA4
  latent; a container's only where the item rules find it, AMA5 below): fewer units refuse with nothing spent (`AmmoShort`), exactly the count
  deletes the stack, more leave the rest; a count of 0 changes nothing; a negative count or a type out of 0..3 is `InvalidAmmo`.
  One revision.
- **Charge cells (ARC-TECHNOLOGY-007).** `HeroCellSource(itemComplexity?)` is the engine's `ISpellCellSource` over the hero's own
  power-cell stack: an applied, paid plan spends the cells at once through `SpendAmmo`; an unapplied request writes nothing.
  `ContainerCellSource(container, itemComplexity)` (ARC-TECHNOLOGY-009) does the same for a device in a container from the
  container's stack. AMA5 is a switch (owner decision 2026-10-21): Original keeps the failed lookup (AM6: a container's stack is
  never found, so a spend finds nothing and a device in it has no cells), Steamwoven finds the container's first stack of the type
  (`ItemRuleProfile.ContainerStackLookupFails`). A container's spent units are kept as the session's record of that seed stack.
- **Assembly (TECH-4).** An ammunition component loses one unit (0x4658E0) and its stack goes at its last unit; an ammunition product
  (the product prototype's type and units, `productAmmo`) merges each unit into the crafter's stack of its prototype, one new
  `made:n` stack only when it holds none; a unit the insert check refuses is a stack of its own on the crafter's tile
  (ARC-WORLD-070). The installed recipes use it: four make ammunition (cells ×5 and ×1, bullets ×2, the fourth
  type ×5) and three take one unit of ammunition as a component. The NPC crafter does the same in its list.
- **Not modelled.** Gold in containers (the AMA5 fix will apply to its container lookup too); transfers of units between the hero and an NPC are
  ARC-WORLD-071's ([NPC gold and transfers](#npc-gold-and-transfers-arc-world-071)), other holders' are not modelled; the critter field and its
  clearing (AM5: the session finds a holder's stack by type); the weight of a stack (the host's insert check). Since ARC-COMBAT-012 the hero's
  attacks spend a shot's ammunition through `SpendAmmo` with the weapon's ammunition type and per-shot count, hit or miss, and a
  short stack refuses the attack after the hit location ([hero attack](hero-attack-runtime.md#reach-and-line-of-fire-arc-combat-012));
  the critical misses that take the
  whole stack or five shots' worth do since ARC-COMBAT-013 ([hero attack](hero-attack-runtime.md#critical-effects-arc-combat-009)).

## NPC gold and transfers (ARC-WORLD-071)

NPC gold as counted piles and the moves between the hero and an NPC that pick pocket (task S15 of the
skills plan) and the barter window (ARC-UI-050) share, read in the executable
([ledger NT1–NT4](../research/ledger/items.md#npc-gold-and-transfers-arc-world-071), with AM1–AM5). No prices and no skill:
the caller decides how many coins or units move. Code: [`GameSessionNpcTransfer`](../../src/Arcanum.Session/GameSessionNpcTransfer.cs);
tests [`NpcTransferStateTests`](../../tests/Arcanum.Session.Tests/NpcTransferStateTests.cs). Snapshot schema 39.

- **Gold (NT1).** An NPC's gold is its first gold pile in list order (its seed piles still in its list, then the piles the session added):
  `QueryNpcGold(npc)` gives its coins (the seed's `Quantity`, or the tracked count), 0 without a pile, null for an NPC the session does not
  know; a second pile (a made pile added while a seed with its own pile was not attached) is not counted until the first is gone. `TakeGoldFromNpc(expected, npc, count, pile)` moves coins to the hero's gold (from the named pile of the holder's current list, `ItemNotAvailable` when it holds none such; without one its first pile; since ARC-WORLD-101 the holder may be a standing container, whose loot window takes its coins): fewer coins refuse with nothing changed (`GoldShort`),
  exactly the count destroys the pile (the durability deletion), more leave the rest. `GiveGoldToNpc(expected, npc, count, pileKind,
  insertCheck)` moves the hero's coins to the NPC: its pile grows with no insert check; without one a new `made:n` pile of `pileKind` (the
  gold prototype 9056, the host's) enters the end of its list after the host's insert check. Gold needs a hero (`GoldUnavailable`); a pile
  with an insert-item script runs the original's event on the source pile first (ARC-SCRIPT-2855, 0x464830 → 0x441980 point 21: the destination is the triggerer, the pile the attachee, its answer ignored, its effects applied with the move) when `scripts` is given; without a host or with a run the session cannot apply the move is refused (`ItemScriptUnsupported`). The map views show a tracked
  pile's coins.
- **Items (NT2, NT3).** `GiveItemToNpc(expected, npc, item, insertCheck)` moves one of the hero's items (its kit, a taken or a made item;
  not a placed one) to the end of the NPC's list after the host's insert check for the NPC: a worn item leaves its slot, ammunition merges
  into the NPC's first stack of its prototype. A kit or taken item is recorded as given (`QueryGivenItems`, with a taken item's origin
  map); `QueryNpcInventory` still lists only made items. `TakeItemFromNpc(expected, npc, item, insertCheck, scripts)` takes an item of an
  NPC's current list in the actor's map seed (alive or not; life is the caller's concern) under the item rules of `LootItem` (insert check, Technophobia, insert-item script,
  ammunition merge), without its dead-holder and use-script conditions; a given kit item returns to the kit at its number's place, a given
  taken item to the taken items with its origin. Gold is `TakeGoldFromNpc`'s (`ItemTypeUnsupported`). A holder's transfer script (point
  32) is no gate of a new pile, a new ammunition stack or a restock item (ARC-SCRIPT-2857, exe 0x4617F0 → 0x461810: a created object has no parent, so it never reaches the parented transfer 0x461CA0; an existing pile or stack grows without it too, NT1): the move meets the fresh path's own checks instead (ARC-SCRIPT-2887, 0x461810, 0x466640): the new item's get script (point 4) from its prototype's array, with the holder as the triggerer, the new item as the attachee and no extra, runs before the insert check (skip default refuses the move: `DeclinedBy` = 4, `InsertRefused`, nothing created and none of the run's effects applied; the original leaves a refused pile or stack on the ground and destroys a refused restock item, so only the restock case matches, a limit for the others, [#2911](https://github.com/Simarglok/arcanum-steamwoven/issues/2911)), and its insert-item script (21), also with the holder as the triggerer, runs before the item is made and is applied with it (its answer cannot refuse). `NewNpcEntry` takes the script host of the move (`GiveGoldToNpc`, `GiveGoldToNpcFromNothing`, `MoveGoldBetweenNpcs`, `GiveAmmoToNpc`, the barter open, a restock through `ISessionRestockWorld.ItemScripts`); a prototype carrying either point with no host refuses the move (`ItemScriptUnsupported`; a restock item is then skipped), the runs' lines and results come back with the move's result (`SessionRestockRegeneration.Messages` for a restock). Installed data: the gold pile and the ammunition prototypes carry neither point, only 12 generic item prototypes carry an insert-item script. An existing pile or stack that grows keeps no run of its own (the original runs the new item's scripts for the merged incoming object too: open, [#2909](https://github.com/Simarglok/arcanum-steamwoven/issues/2909)); the hero as recipient of a created object is open as well ([#2910](https://github.com/Simarglok/arcanum-steamwoven/issues/2910)), while the script create-item action into an NPC's list and a follower's disarm device (`AddItemToNpc`, ARC-SCRIPT-2912) run both points as above (`AddItemToNpc` takes the script host; the use-script host passes its item script host, and a create item of a prototype carrying either point with no host is refused when the run is planned; the lines of the runs join the run's lines; the merge of ammunition into the NPC's stack runs none, #2909). Limits: the runs use the shown map's script host, so a restock of a holder on another map cannot run them ([#2913](https://github.com/Simarglok/arcanum-steamwoven/issues/2913)); the new item's runs commit after the source pile's own insert-item runs (the original's order) but, like every run of a move, are planned against the state before it, so they do not see the source runs' pending flag writes; the eye candy of the runs and the lines of a refused restock item are not reported. A given item (`GiveItemToNpc`, `GiveItemToContainer`, the barter sale, the script transfer item) runs the holder's transfer script first when the caller gives a script host and the shown map's seed lists the holder with the point (ARC-SCRIPT-2827, IE2, TQ19): point 32 with the hero as the triggerer, the holder as the attachee and the item as the extra, before the no-drop check, the capacity checks and the insert check; skip default leaves the item with the hero (`DeclinedBy` = 32, the run's flag writes, printed lines and effects still apply, as for a declined hero-recipient gate), a run the session cannot apply refuses the move with nothing changed. Without a host, or for a holder another map's seed lists, the move stays refused (`HolderScriptUnsupported`), as does a taken item whose origin seed shows a remove-item, insert-item or (worn)
  wield-off script (`ItemScriptUnsupported`).
- **Ammunition units (AM4).** `TakeAmmoFromNpc` and `GiveAmmoToNpc(expected, npc, stack, count, newStackKind, insertCheck)` move units of
  one named stack (since ARC-WORLD-101 `TakeAmmoFromNpc` also from a standing container's stack): fewer units refuse (`AmmoShort`), exactly the count deletes the source stack (a deletion, so a seed stack's remove-item
  script does not run or refuse, as for an emptied pile), more leave the rest; ARC-SCRIPT-2855 (0x4658E0): with a `scripts` host a source stack with an insert-item script runs it first (the destination the triggerer; without a host such a stack is refused with `ItemScriptUnsupported`; `MoveGoldBetweenNpcs`, `OpenBarter`, the barter, the steal and the hero's give use the same rule); the destination's
  first stack of the same type grows with no insert check, or a new `made:n` stack of `newStackKind` (the type's base prototype, the
  host's) enters its list after its insert check (the hero's with the Technophobia refusal, no complexity).
- **State.** The inventory section's `given` list (kit or taken items in NPCs' lists, by entity, origin 0 for a kit item) and `piles` list
  (tracked coins of seed piles and of made piles, by entity), each in ordinal order, at most `MaxNpcItems` and `MaxGoldRecords` (4,096).
  Restore refuses a given record whose item is in no NPC's list, a given kit item without a hero or past the kit's numbers, a made gold
  pile without its record, a pile record of the hero's item, of a made item in no NPC's list or with no coins; a seed attach refuses a
  given taken item its origin seed does not list with its kind, a given kit item any seed lists, and a pile record a seed lists as no gold; a given kit item leaves its number out of the kit's order, like a placed one.
- **Not modelled.** A refused new pile or stack, which the original leaves on the ground at the NPC's tile while it reports success (here
  nothing moves, `InsertRefused`); the transfer script's veto and the item and holder events (refused when present, not run); a source
  without a pile or stack, which the original credits from nothing (AMA6: refused here); the no-drop item flag
  (0x20); NPC flag 0x40, which makes the AI re-wield its best items after a move; gold in containers. The barter window's trades
  (item, then coins) are `BarterBuy` and `BarterSell` ([barter runtime](barter-runtime.md), ARC-UI-050); since ARC-WORLD-090 they move
  items out of and into a merchant's substitute container (its added list in the NPC store, marked as a container), and the barter open
  moves the container's coins onto the merchant; ARC-WORLD-094 and ARC-WORLD-096 restock it ([barter runtime](barter-runtime.md#restock-in-the-viewer-arc-world-096)).

## Made item complexity (ARC-WORLD-083)

A made item keeps its magick/tech complexity (field 0x60), which the hero's insert check reads for the Technophobia refusal
([backgrounds](equipment-runtime.md#backgrounds-arc-world-069)): in the original the item is one object whose field no drop,
placement, pickup or transfer writes ([ledger MC1](../research/ledger/items.md#made-item-complexity-arc-world-083)), so a product
the check refused once is refused again when picked up. Tests [`MadeItemComplexityStateTests`](../../tests/Arcanum.Session.Tests/MadeItemComplexityStateTests.cs).

- **Where it comes from.** An assembly product unit, the hero's or an NPC crafter's, takes the complexity of the host's facts for the
  product prototype (0 without facts), in the list it enters or on the crafter's tile.
- **Where it goes.** `SessionMadeItem.Complexity` in the hero's made items and in an NPC's list, `SessionPlacedItem.Complexity` on the
  ground. Placing, pickup, giving to an NPC and taking back carry it. A kit or taken item keeps 0 there: a taken item's origin seed
  answers for it, on the ground and as a given item in an NPC's list (0 while that seed is not attached). The facts the session shows
  the host for such an item (`QueryMapInventory`, the insert check's item) carry the same value the refusal reads.
- **None (0).** A kit item, a made item `AddItemToNpc` adds (the host passes only its kind) and a new ammunition stack split off
  another (`NewHeroStack`, `NewNpcStack`): the session is given no complexity for them.
- **Snapshot schema 41.** Each `made` row and each NPC list row gains `"complexity"` and each `placed` row ends with `"complexity"`,
  any 32-bit value; a missing one is `MissingField`, a non-zero one on a placed kit or taken item `NotCanonical`; restore refuses a
  non-zero one on a given item or a gold pile in an NPC's list (`InvalidSnapshot`). Schemas 1..40 are refused, not migrated.

## Weight and encumbrance (ARC-WORLD-085)

Alpha-gap item N5. The hero's carried weight, carry weight and encumbrance level, and the insert check's weight part, read in the
executable ([ledger WE1–WE9](../research/ledger/items.md#weight-and-encumbrance-arc-world-085), F2). Requirements:
[FR-ITEM-002](../requirements/items-economy.md#fr-item-002), the weight of stacks of
[FR-ITEM-003](../requirements/items-economy.md#fr-item-003) and the level of
[FR-CHAR-025](../requirements/characters-skills.md#fr-char-025). Tests:
[`CarryRulesTests`](../../tests/Arcanum.Simulation.Tests/Items/CarryRulesTests.cs),
[`CarryStateTests`](../../tests/Arcanum.Session.Tests/CarryStateTests.cs), real data in `StartingHeroTests`.

**Rules (`Arcanum.Simulation.Items.CarryRules`).** `ItemWeight(item, holder, aptitude)`: gold 0; any other item its stored weight
plus, with a holder, its magic weight adjustment as the holder gets it (a critter by its power, EQ9; a container keeps a magickal
item's whole adjustment and none of a technological one's); ammunition that times its units / 4. `TotalWeight` sums a list with
the holder; `Fits(total, item, carryWeight)` is `total + item ≤ carryWeight`; `Level(total, carryWeight)` is the first of the
thresholds 15, 30, 45, 60, 75, 90 % above the load, else 6. The numbers of the level's consequences are rules too (applied since
ARC-WORLD-087): `EffectEntries` (effect 158, one a level, eight at level 6), `DrainIntervalMs` (600 s, 60 s, 5 s at levels 4..6),
`Drain(currentFatigue, steps)` and `CanRun(level)` (below level 4). All arithmetic is the original's 32-bit arithmetic.

**Session.**

| Entry | Contract |
| --- | --- |
| `QueryHeroCarry()` | `SessionHeroCarry(Weight, CarryWeight, Level)`: the hero's list (kit, taken and made items, worn ones included; its coins weigh nothing) weighed with the hero, its effective carry weight (stat 8, `500 × ST`, under its poison) and the level. The aptitude is the hero's effective one without a sector adjustment (the session does not know the sector), clamped to ±100. Null without a hero. |
| The insert check's weight | For an item entering the hero's list from outside it (pickup, loot, `IN`, scripts, a placed item, taking from an NPC, a new ammunition stack, assembly products), the session refuses it with `TooHeavy` when `Fits` fails, before the Technophobia refusal; a host's `TooHeavy` counts as well, and the host still answers the room. Assembly products are weighed after the components are gone (TC3) and each accepted unit counts for the next. Units moved into a stack the hero already holds (`TakeAmmoFromNpc`) run no insert check, as the original's unit move grows a destination stack without one (AM4), so they are not weighed. The NPC's own weight stays the host's insert check (the session keeps no NPC stats); since this task the facts the host sees for a created object or an item the hero gives carry its weight. |
| Weight facts | `SessionItemWeight(Weight, MagicWeight, Complexity)` of each object the session tracks, kept as it entered: a seed item's from its seed facts (`SessionItemFacts.MagicWeight` is new), a kit item's from `SessionHeroKitItem.Weight` (the viewer reads the kit prototypes' fields 0x59, 0x5A and 0x60), an object the session creates (assembly unit, new stack, an NPC's added item) from the host's `AttachItemWeights(prototype → facts)`, which is not saved and is attached again after a restore; an assembly unit weighs with the product complexity the session keeps for it, any other created object with the prototype's complexity (which its made record does not keep, ARC-WORLD-083). Without it, or for facts the rules refuse, a created object weighs 0. The facts move with the object (placed, given, taken back) and go with its deletion. `QueryItemWeight(entity)`. |
| Snapshot schema 44 | `inventory.weights`: `{entity, weight, magicWeight, complexity}` rows by entity in ordinal order, only for a non-zero weight or adjustment (±1 000 000, `OutOfRange` beyond); restore refuses a row of an object the inventory does not hold or an all-zero row (`InvalidSnapshot`). Schemas 1..43 are refused, not migrated. |

Stacks (FR-ITEM-003): gold and ammunition already merge by prototype and move in part (ARC-WORLD-066, ARC-WORLD-071); this task
weighs them. The hero's coins are a counter, not an item: they weigh nothing, as the original's gold does.

**Viewer.** The AREA viewer's starting hero brings its kit weights, a seed item its magic weight adjustment, and the schematics window
attaches its products' weights before an assembly; a refused pickup or loot reads "the PC cannot carry that much weight". The window
shows no weight yet (the inventory window is alpha-gap N6).

**Not ready.** The level's consequences are [ARC-WORLD-087](#encumbrance-consequences-arc-world-087); the weight test of a wear move (the session does not weigh an item taken off or swapped: the original's
unwield runs the insert check, which was not read for an item already in the list, so a hero above its carry weight can still take
an item off); the key-ring exception (the session admits no keys); an NPC's weight and level; a sector's aptitude adjustment.

## Encumbrance consequences (ARC-WORLD-087)

The second half of alpha-gap item N5: what a change of the hero's encumbrance level does, read in the executable (ledger
[WE5, WE7–WE9, WEA1, WEA2](../research/ledger/items.md#weight-and-encumbrance-arc-world-085), F2; the five call sites of the
recalculation 0x45F820 were counted again for this task). Requirements: the consequences of
[FR-CHAR-025](../requirements/characters-skills.md#fr-char-025) and of [FR-ITEM-002](../requirements/items-economy.md#fr-item-002).
Tests: [`EncumbranceStateTests`](../../tests/Arcanum.Session.Tests/EncumbranceStateTests.cs), `CarryRulesTests`, the viewer's
`AreaLevelMessagesTests`.

**When the level is recalculated.** The original recalculates in five places, each comparing the level before its change with the
level after: the insertion into and the removal from a critter's list (which a wear move, a placement and a deletion run too), the
base strength setter, and the effect add and removal when the effective strength changed. The session follows them in one place:
every change of its state (each revision) is compared with the state before it, and the level is recalculated when the hero's held
objects changed, its base strength or the strength its effects give changed, and the level before differs from the level after.
Any other change (poison, the aptitude, units spent from a stack) recalculates nothing, as in the original, so the level the
effects show can lag the weight until the next recalculation (`QueryHeroCarry` always answers the level the weight gives).
The comparison is once per revision, while the original recalculates at each insertion and removal: a change that moves several
objects in one revision (an assembly's components and products, a wear swap, which the original runs as removals and insertions)
recalculates once, from the level before it to the level after it. Where the original passes an intermediate level, it would post
one more message, could set the fatigue timer at an intermediate level 4..6, and a level that differs only in between is never
recalculated here; a wear move, which keeps the weight, recalculates nothing in either.

| Setting | Units merged into a stack the hero holds (an insertion's merge, a unit move) |
| --- | --- |
| Steamwoven (default) | Recalculate: a held stack that grows counts as a change of the list (owner decision 2026-10-28, option A). |
| Original (`ItemRuleProfile.EncumbranceLagsOnStackMerge`) | No recalculation (WEA1), as the retail executable: the stack's object enters no list. |

**What a recalculation does** (no revision of its own: it is part of the change that caused it):

| Step | Contract |
| --- | --- |
| Effect 158 | Every entry of effect 158 leaves the hero's list (whatever added it), then effect 158 with the item cause is added `CarryRules.EffectEntries(level)` times (one a level, eight at level 6), through the same trial as any effect. The rule comes from the host (`AttachEncumbranceEffect(definition)`, number 158, an admitted rule; never saved, attached again after a restore), else from the hero's saved entries of it; without either no entries are added. The session's list holds at most 1024 entries (`SessionEffectLimits.MaxHeroEffects`), so a full list takes fewer; when the list's bounds refuse the additions, only the removals apply (no entries of effect 158 are left). |
| Message | `QueryHeroEncumbranceChange()` answers `SessionEncumbranceChange(Sequence, Level, Entries)` of the last change (sequence from 1, per owner; a restore shows none); the host shows line `11 + level` of `mes\critter.mes`, as the original shows it to the local PC (the original shows one per recalculation; the viewer shows the latest when several came between two frames). The original holds the message back while the inventory window drags an item; the viewer has no drag. |
| Fatigue timer | A level with a drain interval (4: 600 s, 5: 60 s, 6: 5 s) sets the `life.hero.encumbrance` event unless one is pending (WEA2), see the [event queue](game-event-queue-runtime.md). An NPC whose strength a TraitIdx write brings to such a level gets the `life.npc.encumbrance` event the same way (ARC-MAGIC-1568). |
| Run | `SessionHeroCarry.CanRun`: only below level 4 (WE9); the fatigue test of a run stays the host's. |

A new session's hero starts with an empty list at level 0 before its kit, so the first change of the session recalculates a kit
above level 0 (the host attaches the rule before that change; every installed pre-made kit is at level 0). A restore recalculates
nothing: the saved effect list and timer hold the consequences. A save made before this task (or a new session saved before its
first change) holds none, so its consequences appear at the next change of level. No snapshot schema change: the entries are effect entries
(schema 13) and the timer a saved event.

**Viewer.** The AREA viewer attaches effect 158 from the hero's effect tables to every new or loaded session, walks a hero at
level 4 or more whatever the chosen gait, and posts the level message to its message window (the level-up texts load
`mes\critter.mes` too). Native smoke: `ARC-WORLD-087 native-smoke encumbrance` (in the level message stage) checks the installed
rule of effect 158 and the seven message lines.

**Not ready.** The animation speed the level sets (0x436FA0); an NPC's level and its consequences; the level's display in the
inventory window (N6).

## Hero inventory grid (ARC-UI-019)

Source: alpha item N6 part 2, [ledger IW10–IW15](../research/ledger/ui-audio.md#inventory-grid-arc-ui-019) and the grid of
[junk piles](#junk-piles-arc-world-063) (JP3). The session keeps the PC's grid: 10 columns by 12 rows (`SessionHeroGrid`,
`InventoryGridRules.CritterRows`), each held item that is not worn at a slot (row × 10 + column, the original's inventory location)
with its size in cells, kept from the moment it took the slot: its inventory art's frame in 32-pixel cells (0x461780, ledger JP3) as the
host's art source gives it (ARC-UI-055, below); without an answer a taken item's seed facts (`InventoryWidth`/`InventoryHeight`) or 1 × 1.

- **Placement.** After every change (`Revise`) an item that left the PC's list or was put on loses its slot first, then each held item
  without one takes the first top-left free place for its size, row by row, in list order (kit, taken, made); a new session's kit
  does so in kit order. An occupant a wear move puts back (and an item taken off without a cell) takes the place its insert check found, with the worn
  item's cells free, before any waiting item is placed (IW11). An item
  without room keeps no slot (`SessionHeroGrid.Unplaced`) and takes the first that frees. Since ARC-DEV-333 (B48) no take creates
  such an item ([room of a take](#room-of-a-take-arc-dev-333)); the waiting state remains for a save from before, an item a hotkey entry
  released into a full grid, and an item whose art size grew.
- **Moves.** `QueryHeroGrid` answers the grid. `MoveHeroItem` is the same-holder move (0x5788C0): input, version, an item the PC holds
  and does not wear with its saved kind, remove-item or insert-item events without a script host (`ItemScriptUnsupported`), the
  no-drop flag (`WearRefused`/`NotDroppable`, onto its own slot too), the target cells (`GridCellsTaken`: the rectangle leaves the grid
  or meets another item; its own cells count as free), then the insert check with the item out (IW12): a load above the carry weight
  (`TooHeavy`), Technophobia, the host. A refusal leaves the item where it was. With a script host the item's remove-item and
  insert-item events run as the original's removal and insertion fire them (IW15), also on a refused check (the item goes back) and
  onto its own place; without such events a move onto its own place is NoChange. `Unequip` takes an optional grid slot: the cells
  are tested after the no-drop refusal, and without one the item takes the first free place.
- **Insert check of the wear moves (IW12).** `Unequip`, the occupant of `Equip` and `Equip` into an empty slot run the original's test:
  the hero's load above its carry weight refuses (`TooHeavy`; the load counts the moved item once, the occupant without the item that
  replaces it), then Technophobia, then the room (`NoRoom`: no free place for the item going to the grid, the moved item's cells
  free). Under the Original setting a worn ring moved to the other ring slot needs a free place too (IW13); Steamwoven moves it
  without the room test (switch `ItemRuleProfile.WornMoveNeedsRoom`, IWA1). The best-items start tests neither the load nor the room
  (not read for that caller).
- **Gold pile (ARC-UI-090).** The hero's gold is a pile of the hero's list in the original (ledger [HG1](../research/ledger/ui-audio.md#hero-gold-pile-in-the-grid-arc-ui-090)),
  so it has a grid cell. The session keeps the coins as the counter (`QueryGold`) and the pile as the grid record `gold:pc`
  (`SessionHeroGrid.GoldEntity`), present while the hero holds any coin: the last member in list order (PROJECT: the order of pile and kit was not
  read), sized by the art source with the gold kind (`SessionHeroGrid.GoldKind`, prototype file 9076) or 1 × 1. It appears with the first coin
  (the first free place), goes with the last coin (NT1: the pile is destroyed, its cell frees) and keeps its cell while coins come and go.
  `MoveHeroItem` with the pile's entity is the same-holder move (HG2): the pile has no weight, no no-drop flag and no scripts, so only the
  target cells decide (`GridCellsTaken`); the insert-check callback is not asked. A restore refuses (`InvalidGrid`) a pile of a hero with no
  coin; a hero with coins whose grid lists none gets one at the next change.
- **Gold drop and pickup (ARC-UI-091).** `DropHeroGold(expected, count, pileKind)` is the drop of QW1/QW5/IW16 for the pile: the count leaves the
  counter (the rest stays; the last coin destroys the pile, NT1) and a made gold item (`made:n`, kind gold, coin record in `inventory.piles`) is
  created on the hero's tile, settled as a junk pile when items lie there and scheduled to decay like any dropped item. It refuses a count above the
  coins (`GoldShort`), a tile that does not fit a drop (`DropTileUnfit`), a full gold-record or item budget (`BudgetExceeded`) and a hero-less
  session (`GoldUnavailable`). The pickup (`PickUpItem` for the ground item, `LootItem` for a junk pile's) turns the item back into coins:
  `TakePlaced` for a gold item destroys it and adds its coin record to the counter (HG4; a 32-bit overflow refuses with `BudgetExceeded`). A restore
  accepts a placed gold made item that has a coin record. The throw (ARC-UI-094) splits the coins off like an ammunition stack: `ThrowHeroItem` with the pile's entity, a count and a gold split kind takes `SplitGoldForThrow` (the coins leave the counter, a new made gold item enters the hero's list with its coin record and is thrown; it lands without decay and is picked up as above). Refused: a gold kind that is not gold or a count of 0 or less (`InvalidItem`), more coins than held (`GoldShort`), a full gold-record or item budget (`BudgetExceeded`), no coin (`ItemNotAvailable`), no count (`AttackUnsupported`).
- **Snapshot schema 52.** `inventory.grid` lists the slotted items (since schema 76 the hero's gold pile `gold:pc` too, ARC-UI-090) in strictly ascending slot order (`entity`, `slot`, `width` 1..10,
  `height` 1..12); a restore refuses (`InvalidGrid`) an item the PC does not hold or wears, one listed twice, a rectangle outside the grid
  or two that share a cell. A restore places nothing: the next change does. Schemas 1..51 are refused.

- **Art sizes (ARC-UI-055).** `AttachItemArtSizes(sizes)` attaches the host's inventory art frames: for an item's entity and kind the
  frame of its inventory art (field 0x5D) in pixels, or null. The session sizes the item with `CellsOf`, (pixels − 1) / 32 + 1 per side
  (a 0-pixel side is 2^27 cells and never fits, so a take of such an item is refused for lack of room), for the hero's grid and for the junk piles; without
  an answer the seed facts or 1 × 1 count. The source is not state (never saved; attached again after a restore; null detaches it and
  moves nothing). While it answers, an item whose kept size differs from its art's loses its slot at the next change and takes the
  first free place for its size again; attaching it does so at once, in one revision, when anything differs (PROJECT: a new hero's kit
  placed before the source was attached, or a grid saved before ARC-UI-055; the original's sizes never change). The viewer attaches
  it wherever its running session is set (after the seeds), reading the art through `AreaItemIcons.InventoryFrame` from the item's own map
  row while its map is loaded in the run, else from its prototype (an item whose row and prototype name different art can then
  change size, and be laid out again, between runs on different maps), once per item and session; the synthetic self-tests have no game folder and keep 1 × 1.

**Not ready.** The room of items entering from outside; a restore accepts a grid that leaves out held items
there is room for (they wait outside it until the next change places them); the item events of a wear move into an empty slot
that its insert check refuses (IW15); the stacks' merge on a grid drop; a made item's no-drop flag and scripts in the viewer.

## Hero drop and inventory use (ARC-UI-025)

Source: alpha item N6 part 3, [ledger IW16–IW19](../research/ledger/ui-audio.md#inventory-drop-and-use-arc-ui-025) with the drop's
rows PI1–PI3 and the item use's AG15, TP1 and IU1.

- **Drop.** `DropHeroItem` drops one of the hero's items (kit, taken or made, listed or worn) on the actor's tile, as the window's drop
  icon does (0x461A70: the drop 0x461AB0 with distance 0). In order: input, version, an item the hero holds and has not placed
  (`ItemNotAvailable`), its saved kind (`InventoryMismatch`), item event scripts without a host (`ItemScriptUnsupported`: drop 5,
  remove item 28 and, worn, wield off 25; a taken item's attached origin seed answers the drop script too), item spells without a
  trigger host (`ItemEffectUnsupported`), the drop script (its skip default keeps the item, `DeclinedBy` 5, its effects applied), the
  no-drop flag (`ItemNotDroppable`; a worn item's own record answers too), the tile (`DropTileUnfit`), then the removal's wield-off and
  remove-item events and the placement (`PlaceHeroItem`: a junk pile on the tile or a new one, then the decay timer). One revision;
  with a trigger host a worn item's unwear executor and the removal's spell steps follow. A refusal after the drop script drops its
  writes (session policy, as the pickup's). No action points (IW8). The caller places the actor on the hero's tile first. An
  ammunition stack takes `DropHeroAmmo` instead (ARC-UI-029, below).
- **Use.** `AiExecutorRules.UseItemNow` (0x462CC0) now reads an `IItemUseWorld`, the part of `IAiGoalWorld` the use needs, so the
  inventory window runs the same use as the NPC executor (see [terrain viewer](../rendering/terrain-viewer.md)).
- **No snapshot change.** A dropped item is a placed item (schema 32 and later).

**Not ready.** The item
targeting cursor of an item that needs a target (lockpicks excepted) and a trap device's use from the window.

## Stack drop and the quantity window (ARC-UI-029)

The original asks how many units of a gold or ammunition stack a move to another holder takes (interface mode 13,
[ledger QW1–QW6](../research/ledger/ui-audio.md#quantity-window-arc-ui-029)). The rules of the question are
`Arcanum.Presentation.Hud.QuantityBar` (engine-free): `Ask` says whether a move asks or how many it moves at once (one unit, a put
into a looted holder and Take all move at once), `Open` starts at 0 with the stack's units as the maximum, `Apply` is a press (All,
up, down, a keypad digit capped at the maximum, Backspace or Delete). The host shows it and makes the move with the count accepted.

- **Ammunition drop.** `DropHeroAmmo(expected, stack, count, newStackKind)` drops `count` units of one of the hero's ammunition stacks
  on the actor's tile as the original's stack path does (QW5): the units leave the stack (exactly the count deletes it, with no
  remove-item event), a new made stack of `newStackKind` (the original takes the type's base prototype; the host names the kind)
  carries them, and the drop's placement lays it down (junk pile, decay). The stack's drop script and no-drop flag are not consulted
  (the original's stack path never reaches 0x461A70). In order: input (`InvalidItem`; a negative count or a kind that is not
  ammunition `InvalidAmmo`), version, a stack the hero holds (`ItemNotAvailable`), a count of 0 (`NoChange`, as the original's request
  returns before any move), fewer units (`AmmoShort`), the tile (`DropTileUnfit`), the durability record of an emptied stack, the ammunition, item and
  made-number budgets (`BudgetExceeded`), the revision. One revision; no insert check, no events, no action points.
- **Takes from a dead NPC** (since ARC-WORLD-101 also from a container) use `TakeGoldFromNpc` (the pile given) and `TakeAmmoFromNpc` (ARC-WORLD-071) with the count.
- **Takes from a junk pile** (ARC-UI-1328) use `TakeGoldFromJunkPile(expected, pile, item, count)` and `TakeAmmoFromJunkPile(expected, pile, item, count, newStackKind, insertCheck)`: the item must lie in the pile of the actor's map (a placed item on its cell, or a map's loose item that joined it). The same rules as the NPC takes (NT1, AM4): the count moves, exactly the stack deletes it and takes it out of the pile, more leaves the rest in the pile (its coin or ammunition record, saved as any other), a gold pile with an insert-item script refuses. The pile's items are unchanged by a part taken, so its dissolve rules (JP5) hold; the destroy inside the take leaves the pile standing while the window shows it. Refusals: `UnknownHolder` (no such pile on the actor's map), `ItemNotAvailable` (not in the pile or not of that kind), the NPC takes' others; a count of 0 changes nothing. Other items of a pile still move whole through `LootItem`.
- **No snapshot change.** The new stack is a placed made item with its ammunition record (schemas 24, 32, 34).

**Not ready.** The question in the barter window and for a junk pile's or container's stacks (the hero's gold pile moves in the grid, drops and throws since ARC-UI-090, ARC-UI-091 and ARC-UI-094; the map drop and throw of other items is ARC-UI-063's, the ammunition stack's lift and throw ARC-UI-086's, the hotkey bar's lift ARC-UI-087's, see the
[terrain viewer](../rendering/terrain-viewer.md#inventory-throw-from-the-game-cursor-arc-ui-063)).

## Worn items of a looted body (ARC-DEV-202)

Bug B03 (Take all did not move the Kite Shaman's dagger: `WornItemUnsupported`) and
[ledger WL1–WL4](../research/ledger/items.md#worn-items-of-a-looted-body-arc-dev-202). The original tests no worn state on a loot: Take all
(0x579770) moves every item of the body's list, and the removal that every transfer runs (0x466E00 → 0x467860) takes a worn item off its
slot (wield-off event 25, then remove-item 28). A model gap, not a rule of the game, so both rule modes follow it, with no switch.

- `LootItem` (a body's NPC items, a dressed body's record over its map data, ARC-WORLD-103, and the made items it holds) takes a worn item:
  the insert check sees it unwielded (inventory location −1, as after the removal), a wield-off script (point 25) runs before the
  remove-item script (28) with the body as triggerer and needs a script host (else `ItemScriptUnsupported`, also for an item of another
  map's seed row), the item enters the hero's list as an unworn item, and a dressed body's record drops it (`Unwear`).
- ARC-MAGIC-1743 (ledger IE3, IE5, IE6): with the NPC's script context (`npcWearer`: the AREA viewer gives it through `AreaInventoryHost.NpcWearer`, the area's `NpcWieldScripts`) the wield-off run of a looted worn item runs
  over the NPC's own effect list and stored stats (the NPC as wearer, as the best-items wield's runs) and the gate, wield-off and remove-item runs have the NPC as the triggerer (a printed line is read for an NPC); the
  insert-item run keeps the hero as triggerer. With the context the runs of the loot (the gate and the insert-item run included) see the NPC as the wearer, so an effect or stat read on the hero in them is refused (`ItemScriptUnsupported`). Without the context the run is the hero-wearer run as before, so an effect on the NPC is refused (`ItemScriptUnsupported`).
- ARC-SCRIPT-1987 (ledger [INS5](../research/ledger/social.md#insult-line-arc-script-1505)): the dialog's negative `IN` (`GiveItemToPc`) takes a worn item of the NPC off its slot too, as the loot does (`unwield`; the transfer 0x461CA0 tests no worn state, WL3): the insert check sees it unwielded, wield-off (25) and remove-item (28) run with the NPC as the triggerer (the NPC's own context through `SessionDialogInventoryPort`'s `npcWearer`, the area's `NpcWearer`, else the hero-wearer run), the NPC's wear record drops it and the NPC is marked to re-wield (flag 0x40, its look follows the re-wield); a refused insert check or a no-drop flag lays the item on the hero's tile as for any `IN`.
- The other parented moves keep refusing a worn item with `WornItemUnsupported`: taking from a living NPC without `unwield` (a purchase of the list's own button,
  `TakeItemFromNpc`) and `RemoveItemFromNpc`; the pick-pocket steal takes it since ARC-DEV-1338 (`unwield`, [pick pocket](pick-pocket-runtime.md#viewer)). Their original rules were not read in this task.
- **Not modelled:** the no-drop flag's remove check (0x466DA0), the unwear executor and spells of an item with item spells (0x4CBFC0, 0x457450;
  the item facts of an NPC's list do not carry them), the art and light of the body (a fallen body keeps its stored art), and the NPC flag 0x40.
- Viewer: the window's Take all (`TakeAllFromBody`) loops the body's list as before and so takes the worn items; the diagnostic list
  marks them "(worn)". The original look's grid leaves them out (IW28); their view is the body's doll (IW29, ARC-UI-072, bug B30: `AreaInventoryView.BodyDoll`, nine slots by the worn item's inventory location).
- No snapshot change. Tests: `InventoryStateTests.LootTakesAWornItemOffTheBodyAndTheOtherParentedMovesKeepRefusingIt`,
  `ItemEventScriptTests.Loot_of_a_worn_item_runs_wield_off_before_remove_item_and_needs_a_host_for_them`; the `premade` smoke loots every
  item of the killed heroes' NPCs.

## Key items (ARC-WORLD-123)

Status: **Implemented** for the paths that end in `Take` (pickup, loot of a body, container or junk pile, a dialog's item, a purchase and a steal from a merchant). Ledger [KY1–KY5](../research/ledger/items.md#keys-on-the-heros-ring-arc-world-123).
The original's item insert (0x466640) ends with the key fold (0x466A00): a key item (type 11) inserted into a holder that is not a container
and holds a key ring is destroyed and its number (field 0xBA) is appended to the ring's list (0x466A50); the insert check (0x466510) skips the
weight and the room for it. With no ring the key stays an ordinary item. The session keeps the hero's list as the story section's
`heroKeys` (ARC-UI-081) and its hero always holds the one ring (`HeroHoldsKeyRing`; a session without a hero holds none).

- **Seed.** `SessionItemFacts.KeyId` is the key's number (the data view's `KeyId`, set by `AreaInventoryMap` for a key item only); a seed
  item that is not a key and carries one is refused (`InvalidInventorySeed`).
- **Transfers.** `PickUpItem`, `LootItem`, `GiveItemToPc`, `TakeItemFromNpc`, `BarterBuy` and the container and junk-pile loot fold a key item: the host's insert check is not asked, the Technophobia refusal
  of a negative complexity still applies, the item's get, transfer, remove-item and insert-item scripts run as for any item, then the item
  is destroyed (`DestroyObject`, as an ammunition stack merged into a held one) and its number goes to the end of the list, one revision.
  The item takes no place in the list, so `MaxPcItems` does not count it.
- **Refusals,** nothing changed: `InvalidItem` (no number, or a negative one), `KeyRingFull` (3000 numbers, project policy),
  `ItemTypeUnsupported` for a session without a hero. A number may repeat in the list.
- **Not modelled:** the key ring item (`ItemTypeUnsupported`), a key that stays loose while the PC holds no ring, the ring merge (0x466AA0),
  keys in NPC lists, the pick-pocket steal, assembly and a script's created items (still refused), and the followers' keys the lock check also reads.

## No-drop items in transfers (ARC-WORLD-1283)

The original's transfer from a holder (0x461CA0, ledger NT2, VP5) runs the recipient's transfer script, then the item's remove check (0x466DA0), then the insert
check. The remove check refuses an item with the no-drop flag (item flag 0x20), a worn item with the flag too (the flag is tested first; the worn-slot check 0x465010
answers 0), and shows item.mes 111 (100 + code 11, 0x4673F0) to the PC that is the old or the new parent. Steamwoven runs it in `Take` (loot of a body, a container
and a junk pile, `TakeItemFromNpc` for a steal or a purchase, the dialog's negative `IN`) and in `GiveItemTo` (`GiveItemToNpc`, a sale, a plant, a container as recipient):

- The refusal is `GameSessionFailure.ItemNotDroppable` with `InventoryMutationResult.PcMessage` 111 (`GameSessionOwner.NoDropMessage`), no revision, nothing changed, and no insert check
  is asked. The viewer's windows and the barter status show `AreaInventoryHost.Refusal` ("the item cannot be dropped, so it cannot change hands"); a pick-pocket attempt whose
  move is refused fails without being noticed (PP3) and its status line adds the same text.
- The dialog's negative `IN -name` goes on as the original does after any transfer that did not end with the PC (0x415BA0 case 9): the item leaves the NPC (0x466E00 tests
  no flag) and lies on the hero's tile; the result is `Changed`, carries `PcMessage` and no verdict, and the hero gets no pickup trigger.
- The flag is the item's own: the origin seed row's, the worn record's, or, for an item with no seed row (a kit item, a made item), its prototype's through
  `GameSessionOwner.AttachItemNoDrop` (not saved; the viewer attaches it with the prototype scripts, `AreaPrototypeItem.NoDrop`). Without a source such an item has no flag.
- Limits: a worn no-drop item of a living NPC taken by a steal reaches the check (the steal unwields since ARC-DEV-1338, as the loot of a body does); an unworn kit or taken item whose origin seed is not attached reads its prototype's flag, not its instance's;
  a worn item's flag is the worn record's. A dialog hand-over (`IN n >= 0`) of a no-drop item to an NPC takes the original's fallback after the refusal: the item is laid on
  the cell of an NPC the PC leads, else destroyed (TK2, `SettleRefusedHandover`).
- Ammunition or gold moved by units (0x4658E0, 0x464830) runs no remove check. The dialog's PC message has no on-screen surface yet.

## Next steps

The AREA viewer is wired (ARC-UI-004), the hero's weight is checked (ARC-WORLD-085) and its level has its consequences
(ARC-WORLD-087). Next: the remaining source paths (drop and junk piles, chests) after their own research.
