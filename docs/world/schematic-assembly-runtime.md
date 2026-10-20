# Schematic assembly runtime

Status: **Implemented bounded component, not wired.** Work item ARC-TECHNOLOGY-004 (2026-10-21), slice TECH-4 of the
[technology research](../research/technology-evidence.md#proposed-implementation-slices-project-policy-not-accepted). The
schematic component gate, the assemble button and the assembly plan as pure rules in `Arcanum.Simulation.Technology`, and the
hero's assembly as one session change that makes items into its inventory (snapshot schema 24: the made items). No viewer or
schematic window calls them yet.

Requirements: [FR-ITEM-018](../requirements/items-economy.md#fr-item-018) (assembly).

Every rule was read in the retail 1.0.7.4 `Arcanum.exe`: the first reading of ARC-RES-062
([the window and its gate](../research/technology-evidence.md#the-window-and-its-gate-fr-item-018-fr-magic-012),
[assembly](../research/technology-evidence.md#assembly-0x56e720-fr-item-018-f2)) and a reading for this slice
([ledger "Schematic assembly"](../research/original-verification-ledger.md#schematic-assembly-arc-technology-004), SA1–SA6).
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
  (gold, ammunition, keys and key rings are not: `ItemTypeUnsupported`);
- each of the "quantity" products must pass the insert check; the host answers each unit as if both components were already
  gone and the earlier units already inserted (the session asks before it changes anything); The original creates each product on the crafter's tile
  (0x4ED8B0) and moves it into the inventory (0x4617F0), which leaves it on the tile when the inventory refuses it; the
  session keeps no new ground items, so a refusal refuses the assembly (`InsertRefused`) instead;
- the item limit (the two components leave, the products enter) and the durability records for the two deletions;
- then component 1 and component 2 are deleted ([durability](item-durability-runtime.md) deletion), and the products enter
  as made items `made:n` (n one past every made entity held, deleted or with a durability record; the `made:` prefix is the
  session's own: a seed or a taken item may not carry it). No roll, skill, action points, time or experience.

The result names the plan, the made and the consumed items. The host shows `schematic_text.mes` 2 and plays sound 3018 + the
current discipline (3018..3025, the window's, ARC-TECHNOLOGY-005) on success, or the refusal message of the plan's button.

`QueryHeroAssembly(schematic, items, facts)` gives the plan without changing anything (the page the window draws).

## Made items (snapshot schema 24)

`SessionInventoryState.Made` / `QueryMadeItems()`: `"inventory":{…,"made":[{"entity","type","prototype","name","description"}]}`,
in the order they were made. Restore refuses a made item without a hero or of a type the lists do not admit (`InvalidItem`), and
an entity that is not `made:n` with strictly ascending n or that was deleted (`InvalidSnapshot`). Made items count toward
`GameSessionLimits.MaxPcItems`, can be worn and broken like any held item, and no seed may list them. Schemas 1..23 are refused.

## Differences from the original

| Original 1.0.7.4 | Steamwoven (both modes) |
| --- | --- |
| Assembly does not re-check the expertise (TC5). | The plan checks the gate again. |
| One item that matches both component lists serves both and is consumed twice (TC4). | Component 1's item is reserved. |
| The components are consumed before the products are made; a failed creation loses them (TC3). | Every product is checked before anything is consumed. |
| A product the inventory refuses stays on the crafter's tile. | The assembly is refused (the session keeps no new ground items). |
| Each product enters the inventory after the components left and the earlier products entered. | The insert check is asked first for every unit; the host answers each as if that had happened. |
| — | The reservation is greedy: component 1 takes its first alternative present even when another would leave component 2 an item (latent: no installed record lists one prototype in both lists). |
| An ammunition component loses one unit. | The rule does (`UseUnits`); the hero's lists hold no ammunition, so the session never meets one. |

## Not ready

- No caller: the schematic window (TECH-5) and its tabs, pages, sounds and messages.
- NPC crafters (the dialog's `su`): the rules take any crafter's item view and expertise, but the session wiring for an NPC's
  inventory and degrees is not done.
- Ammunition and stack products and components in the hero's lists (ARC-WORLD-014 refuses stacks).
- Dropping a product on the ground when the inventory cannot take it.
