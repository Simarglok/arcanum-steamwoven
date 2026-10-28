# Schematic assembly runtime

Status: **Implemented bounded component, wired for the hero and NPC crafters.** Work item ARC-TECHNOLOGY-004 (2026-10-21), slice TECH-4 of the
[technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted). The
schematic component gate, the assemble button and the assembly plan as pure rules in `Arcanum.Simulation.Technology`, and the
hero's assembly as one session change that makes items into its inventory (snapshot schema 24: the made items). The viewer's
[schematic window](schematic-window-runtime.md) (ARC-TECHNOLOGY-005) calls them for the hero.

Requirements: [FR-ITEM-018](../requirements/items-economy.md#fr-item-018) (assembly).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe`: the first reading of ARC-RES-062
([the window and its gate](../research/technology-evidence.md#the-window-and-its-gate-fr-item-018-fr-magic-012),
[assembly](../research/technology-evidence.md#assembly-0x56e720-fr-item-018-f2)) and a reading for this slice
([ledger "Schematic assembly"](../research/ledger/technology.md#schematic-assembly-arc-technology-004), SA1–SA6).
Code: [`SchematicAssemblyRules`](../../src/Arcanum.Simulation/Technology/SchematicAssemblyRules.cs),
[`GameSessionAssembly`](../../src/Arcanum.Session/GameSessionAssembly.cs); tests
[`SchematicAssemblyRulesTests`](../../tests/Arcanum.Simulation.Tests/Technology/SchematicAssemblyRulesTests.cs),
[`SchematicAssemblyStateTests`](../../tests/Arcanum.Session.Tests/SchematicAssemblyStateTests.cs),
[`SchematicAssemblyRealDataTests`](../../tests/Arcanum.Session.Tests/SchematicAssemblyRealDataTests.cs).

## Gate (0x56E190)

`SchematicAssemblyRules.Gate(alternatives, inventory, expertise, facts, reserved)` for one component:

- the alternatives (base numbers) in order, each as a prototype (base + 20), searched by `Find` in the crafter's **own**
  items (`CraftItem`; no nested container, no party): an exact prototype; items at locations 1000..1008 (worn), broken items
  (the destroyed bit of the current art) and items whose description differs from their prototype's are skipped; an item at
  2000..2009 is kept only as a fallback (the last one), any other match is taken at once; an ammunition item counts while it has
  a unit;
- the first alternative found sets the prototype, otherwise the first alternative is shown;
- **expertise**: the crafter's effective expertise ([tech degrees](tech-degrees-runtime.md)) in that prototype's item discipline
  must be at least −complexity, signed, so neutral and magickal components pass (an expertise below 0 through effects can fail
  even them). A prototype whose facts the caller does not know, or whose discipline is outside 0..7, fails the test.

## Button (0x56DDC0) and O7

`Button(first, second)`: **expertise lacking** when either component fails the expertise test (checked first), else **item
missing** when either is not found, else **can assemble**. The click shows `schematic_text.mes` 1 for lacking expertise and 0
for a missing item (`RefusalMessage`); the hover always 3. O7, the disagreement of the first two readers about which code is
which, is settled by a third reading and by the installed texts (message 0 names missing items, message 1 lacking expertise).

## Assembly (0x56E720)

`Plan(component1, component2, product, quantity, inventory, expertise, facts)` with the owner's decisions (2026-10-20, both
modes, no switch):

1. the gate is checked again for both components (**TC5**);
2. component 1 is found first and **reserved**, so component 2 is searched among the other items (**TC4**: the original lets
   one item serve both lists and would consume it twice);
3. the product is alternative i1 when i1 ≠ 0, else alternative i2.

`GameSessionOwner.AssembleSchematic(expected, schematic, items, facts, productKind, insertCheck)` is the hero's assembly, one
revision, with nothing consumed before every check passed (**TC3**: the original consumes first and loses the components when
a creation fails):

- the plan above over the hero's own items: kit, taken and made items (`items`: the host's location and description facts,
  also for `made:n`, which is a component only when the host answers for it; a worn item's location is its slot, a broken
  item's state the session's durability record);
- a record without a product at the found index or with a quantity below 1 is `InvalidSchematics`;
- the product kind (`productKind`, the product prototype's type, name and description) must be a type the hero's lists admit
  (gold, keys and key rings are not: `ItemTypeUnsupported`; an ammunition product needs its type and units, `productAmmo`, and
  merges into the hero's stack of its prototype since ARC-WORLD-066, [ammunition](inventory-runtime.md#ammunition-arc-world-066));
- the insert check is asked for each of the "quantity" products; the host answers each unit as if both components were already
  gone and the earlier units already inserted (the session asks before it changes anything). The original creates each product on
  the crafter's tile (0x4ED8B0) and moves it into the inventory (0x4617F0) without checking the result: a refused unit shows the
  insert check's message for a PC and stays on the tile (ledger SA3–SA5). Since ARC-WORLD-070 the session does the same: a refused
  unit lies on the hero's tile as a made item in the [ground store](inventory-runtime.md#placed-items-arc-world-059)
  (`SessionAssemblyResult.OnGround`, the first refusal in `Verdict`), which the hero can pick up like any item it placed; the
  assembly succeeds. An ammunition unit the inventory accepts merges into the hero's stack of its prototype (one an earlier unit
  started included); a refused one is a stack of its own on the ground;
- the item limit (the two components leave, the products enter the inventory or the ground: the ground store counts toward
  `MaxPcItems`), the ammunition records and sum, the made numbers and the durability records for the two deletions — all before
  anything is consumed, so a budget refusal still spends nothing; a tile outside the store's range refuses a refused unit
  (`InsertRefused`, nothing spent);
- then component 1 and component 2 are deleted ([durability](item-durability-runtime.md) deletion), and the products enter
  as made items `made:n` (n one past every made entity held, placed, deleted or with a durability record; the `made:` prefix is the
  session's own: a seed or a taken item may not carry it). No roll, skill, action points, time or experience. No snapshot field is
  added (the ground store's `placed` list already holds made items).

The result names the plan, the made, the grounded and the consumed items. The host shows `schematic_text.mes` 2 and plays sound
3018 + the current discipline (3018..3025, the window's, ARC-TECHNOLOGY-005) on success (the AREA viewer adds the refusal and the
count on the ground), or the refusal message of the plan's button.

`QueryHeroAssembly(schematic, items, facts)` gives the plan without changing anything (the page the window draws).

## Made items (snapshot schema 24)

`SessionInventoryState.Made` / `QueryMadeItems()`: `"inventory":{…,"made":[{"entity","type","prototype","name","description"}]}`,
in the order they entered the list. Restore refuses a made item of a type the lists do not admit (`InvalidItem`), and an entity
that is not `made:n`, that was deleted or that is held twice (`InvalidSnapshot`). Since ARC-WORLD-055 (schema 29) an NPC can hold
made items too ([NPC inventory changes](inventory-runtime.md#npc-inventory-changes-arc-world-055)): the numbers are shared, and a made
item the PC takes from an NPC joins this list in entry order (so the numbers need not ascend, and a heroless session can hold one). Made items count toward
`GameSessionLimits.MaxPcItems`, can be worn and broken like any held item, and no seed may list them. Schemas 1..23 are refused.

## Differences from the original

| Original 1.0.7.4 | Steamwoven (both modes) |
| --- | --- |
| Assembly does not re-check the expertise (TC5). | The plan checks the gate again. |
| One item that matches both component lists serves both and is consumed twice (TC4). | Component 1's item is reserved. |
| The components are consumed before the products are made; a failed creation loses them (TC3). | Every product is checked before anything is consumed. |
| Each product enters the inventory after the components left and the earlier products entered. | The insert check is asked first for every unit; the host answers each as if that had happened. A refused unit lies on the crafter's tile as in the original (ARC-WORLD-070). |
| — | The reservation is greedy: component 1 takes its first alternative present even when another would leave component 2 an item (latent: no installed record lists one prototype in both lists). |
| An ammunition component loses one unit. | The rule does (`UseUnits`), and since ARC-WORLD-066 the session spends one unit of the held stack (the stack goes at its last unit). |

## NPC crafter (ARC-TECHNOLOGY-006)

`QueryNpcTechnology(crafter)`, `QueryNpcAssembly(crafter, schematic, items, facts)` and
`AssembleSchematicForNpc(expected, crafter, schematic, items, facts, productKind, insertCheck, productAmmo, crafterCell)` run the same gate, button and plan for
an NPC (ledger [NC4–NC7](../research/ledger/technology.md#npc-schematic-crafter-arc-technology-006), SW7). `SessionNpcCrafter`
is the NPC's map data (sheet and scheme, read only while the session keeps no advancement record of it) and its effects' stat
changes. The components are searched in the NPC's current list (seed items still there on every attached seed that lists it, then
its added made items), with its expertise; on success both components leave the NPC with the durability deletion and the products
enter its list as `made:n` ([NPC inventory changes](inventory-runtime.md#npc-inventory-changes-arc-world-055)) or lie on
`crafterCell` (below), one revision. Refusals, before any effect: an invalid crafter (`InvalidAdvancement`), the version, an NPC the
session does not know (`UnknownHolder`), a dead NPC (`InvalidLifeTarget`: the original's open refuses a dead crafter), then the
hero's: the gate, the record, the product kind, the insert check for the NPC asked for every unit (a call back into the owner, a
seed attach included, is `StaleVersion`), a refused unit that can go nowhere (`InsertRefused`), the budgets (the NPC's list bound,
the store, the ground store, the made numbers, the durability records) and the revision. The original leaves a product
the NPC cannot take on the ground at the crafter and still completes (NC7); so does the session since ARC-WORLD-070: a refused unit
lies on `crafterCell` (the NPC's tile on the hero's map, the host's to give) in the ground store, which the hero can pick up. The
ground store is the hero's: without a hero or without the crafter's tile a refused unit refuses the assembly (`InsertRefused`,
nothing spent); the ground units count toward the hero's `MaxPcItems`.

## Not ready

- NPC crafters are done by ARC-TECHNOLOGY-006 (above); the "expertise in discipline" effects of an NPC are not modelled.
- Gold and key products and components (the hero's lists hold neither); ammunition is done by ARC-WORLD-066.
- Junk piles and the player's drop (WORLD-063, WORLD-064); not a gap: a refused product gets no decay event, as in the original
  ([ARC-WORLD-062](inventory-runtime.md#item-decay-arc-world-062), ledger ID9); the AREA viewer passes no NPC crafter tile (its
  insert check for an NPC accepts, so it never needs one).
