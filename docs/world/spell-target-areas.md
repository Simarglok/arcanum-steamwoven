# Spell target areas (ARC-MAGIC-007)

Status: **Implemented bounded component, not wired.** Work item ARC-MAGIC-007 (2026-10-21), slice MT8 "Target areas" of the
[magic implementation plan](../research/magic-implementation-plan.md). The original's target functions: the area test, the
list an action's area gathers, a component's filter and re-aim in the per-target stage, and the AI notice of an aggressive
spell, as the original has them. Godot-free pure rules in `Arcanum.Simulation.Magic` for any map: the world (objects, tiles,
paths, sectors, parties) comes through a port the host fills from the active map. The invocation check's area questions
(MT7) are answered by the same rules. One new rule-mode switch (MG25); no saved state.

Requirements: [FR-MAGIC-003](../requirements/combat-magic-tech.md#fr-magic-003) (the target of a magic action is chosen by the
definition's parameters: self, object, group, tile and radius, line, the NPC fallback).

Every rule below was read in the retail 1.0.7.4 `Arcanum.exe` by two independent readers
([ledger "Spell target areas"](../research/ledger/spell-runs.md#spell-target-areas-arc-magic-007), rows TA1–TA16),
and the area test (0x4F2D20), the collection (0x4F40B0), the re-aim (0x453D40) and the AI notice (0x453EE0) were compared with
the original in the function oracle on 10,000 seeded inputs each (F3, receipts [`4F2D20.json`](../verification/receipts/4F2D20.json),
[`4F40B0.json`](../verification/receipts/4F40B0.json), [`453D40.json`](../verification/receipts/453D40.json),
[`453EE0.json`](../verification/receipts/453EE0.json); re-run with `./arcanum oracle --function 0x4F2D20` and the others). The
Original setting matches on every input; the Steamwoven setting differs only on MG25's inputs. The oracle's world model is a
context with eight objects; its stubs write the lists the world queries answer into memory (`poke`). Code:
[`SpellTargetRules`](../../src/Arcanum.Simulation/Magic/SpellTargetRules.cs),
[`SpellInvocationAreas`](../../src/Arcanum.Simulation/Magic/SpellInvocationAreas.cs),
[`MagicAnomalyProfile`](../../src/Arcanum.Simulation/Magic/MagicAnomalyProfile.cs); tests:
[`SpellTargetRulesTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellTargetRulesTests.cs),
[`SpellTargetOriginalExeTests`](../../tests/Arcanum.Simulation.Tests/Magic/SpellTargetOriginalExeTests.cs).

## Inputs

- `SpellTargetArea`: the 64-bit target mask (the OR of the [target names](../formats/spell-records.md#target-areas-line-0)'
  values), the spell flags a target must have and must not have, the radius and the count (−1 or 0: no limit) — one action's
  area (record +0x70 + 0x18 × action) or a component's own area; the data reader's `SpellArea` maps one to one.
- `SpellTargetObject`: an object's id, type, location, object flags, spell flags, critter flags and flags 2, current hit points,
  hit-point damage, alignment, poison, material, weight, a door's and a chest's flags, its PC leader (the PC at the top of an
  NPC's leader chain), a scenery object's occupant and an item's owner.
- `SpellTargetContext` (the original's 0x64-byte context): the area; self (a run's parent) and source; the origin location;
  the candidate object and tile under test; the action's target object and tile; the other object (a run's +0xE8 object) whose
  party a non-party area also spares; the last summoned object and the run's summoned list; the run's object list (null: none;
  empty: not built yet).
- `ISpellTargetWorld` (filled by the host for the active map): objects by id; the objects in the square `centre ± radius` with a
  type mask, in the original's query order (0x440B40: sector rows, sectors from the left, the object that entered a sector last
  first; ledger TB13); the objects on a tile (0x4407C0); whether a tile is blocked (0x4D7110); whether the source has a path of
  at most 200 steps to a tile (0x41F3C0); a location's indoor/outdoor kind (0x4D70B0, 0x5038C0); the direction between two
  locations (0x4B8D50); a source's party and followers (0x4413E0).

## Area test (`Test`, 0x4F2D20)

An empty mask passes. A mask with the self, object or radius bit (`Tgt_Self`, `Tgt_Object`, `Tgt_Obj_Radius`: low 0x15) tests
the candidate object; with no candidate it goes on to the tile tests only with the tile or list bit, otherwise it fails. A mask
without those bits goes to the tile tests, which run only with the tile bit (`Tgt_Tile`); a mask without it passes whatever
else it holds. The object tests, each failing the candidate:

1. Destroyed or switched off (a critter that is "safe off" still counts); not drawn (a sleeping critter still counts).
2. The required and forbidden spell flags.
3. Self (`Tgt_Self`, `Tgt_Obj_Self`): only the self; no-self (`Tgt_Obj_No_Self`): neither the self nor the source (with both bits
   neither test runs); source: only the source.
4. With the critter bit (`Tgt_Obj_T_Critter` and the critter names): a PC or NPC; PC / not PC; dead / not dead (hit points ≤ 0);
   animal, undead, demon, mechanical, good / evil (alignment), unrevivifiable, unresurrectable and their negations; poisoned;
   party (`Tgt_All_Party_Critters`, `Tgt_Party_Critter`: a PC or a member of any PC's party); else non-party.
5. Without it: portal / container / wall / not wall by type; **not dead for any object** (MG18) and **non-party for PCs and NPCs**
   (MG21).
6. Damaged / not damaged (hit-point damage, or a critter's injuries), with the failure bit the targeting cursor shows;
   locked / not locked / not magically held (doors and chests only); damaged or poisoned; flesh.
7. Radius (`Tgt_Obj_Radius`): the distance from the action's target tile, or else the source, at most the radius.
8. Summoned: the spell flag "summoned" (MG25); in / not in an inventory; not invulnerable; an item under weight 5.

The tile tests (`Tgt_Tile` names) on the candidate tile: none fails (passes with the list bit); tile-self (the candidate
object's tile, with a source); a path from the source; empty (not blocked, and no non-item without "no block" on it) and empty of
immobiles; the tile radius (the target tile's distance to itself: refuses only a negative radius); the same indoor/outdoor kind as
the origin. Stone, in-wall, not-stone, no-empty, wall line, offscreen, cone, parent, attacker and list bits test nothing.

## Collection (`Collect`, 0x4F40B0)

The list an action's area gathers, in this order: the self, then the source (without no-self; destroyed or switched-off
objects skipped); the target object; the last summoned and the run's summoned list; the target tile; the source's tile
(`Tgt_Tile_Self`, MG23); objects in the radius around the target object, or else the target tile, of the types the critter,
portal, container and wall bits ask (scenery always, standing for its occupant; every type but projectiles when none is asked),
each passing the area test from the source's view; tiles in the radius (a square, rows first from the top left); the wall
line (both ways from the centre, perpendicular to the direction from the origin to the target tile); the candidate's tile; the
"cone" (O3: a square of the radius around the target tile or the source — its objects with the self bit, of the types TA11 names but every type only with the object bit, else its
tiles; the original reads the source's art rotation and does not use it); the run's object list (built from the gathered objects, in
reverse order, by the first action that names it; reused after); the source's party and followers; an item source's owner.
The count limits the radius, wall and cone branches (a duplicate counts). Duplicates are skipped; at most 256 entries (MG24).
The original's `Tgt_Party_Critter`, `Tgt_Non_Party_Critters` and `Tgt_Attacker_Obj` gather nothing.

## Components (`ReAim`, `TriggersAllow`, `AiNotices`)

In the run's per-target stage (0x4510F0), for each target and each component of the action, in this order: the component's
own area test against the target and its tile (a failed component is skipped); the re-aim by its `Apply_AoE` mask (0x453D40):
self, source or the last summoned object becomes the target (in that order, each even when absent); the tile bit sets the tile
to the current target's location ("offscreen") or else the run's target tile or its target object's location; the re-aimed
target stays for the following components, **also when the next gate refuses this one**; the item-trigger gate
(`TriggersAllow`): when the record has item triggers and the component names some, they must meet the run's. For an aggressive record, a target and
an action other than END (after the gate) the target's AI is told that the parent attacked it, and the run's other object when it is not the
parent, once per target in a row (0x453EE0, 0x453F20).

## Invocation check (`SpellInvocationAreas`)

The MT7 check's area questions: the BEGIN target mask alone (no spell flags), radius 2, no count, with the parent as self,
the invocation's source, the source's location as origin (the invocation's +0x38) and the target object and tile as candidate and action target. The
NPC fallback marks the target's location as candidate and target tile, adds the tile-radius and indoor/outdoor bits, gathers
the list and answers the first tile that passes the same test (an object target with object bits fails again; tile areas find
a tile). The line of fire stays the caller's (`LineOfFireRules.Trace`, ARC-COMBAT-012).

## Differences from the original

Owner decisions 2026-10-20: every difference is documented.

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| The summoned test reads the flags word the test read last: the critter flags ("surrendered") with the critter bit, a door's or chest's flags after a lock test (MG25). No installed record reaches it. | The spell flag "summoned". | `MagicAnomalyProfile.SummonedTestReadsLastFlags` (Original) |
| The list has room for 256 entries and its helpers write past it: a tile radius of 8 or more, a tile cone or a crowded radius overwrites the context (MG24). No installed record reaches it. | Further entries are dropped. | both (no-crash) |
| A missing object's location is read through handle 0 (tile-self test without a candidate, the radius and wall centre without a target tile or object, the re-aim's tile); a sub-context without a source keeps an unset origin (MG26). | No location: the tile-self test fails, the tile radius and the wall gather nothing, the re-aim's tile is none, the indoor/outdoor test is skipped. | both (no-crash) |
| A scenery object's occupant is used by handle, whatever it is. | An occupant the world port does not know leaves the scenery object. | — |
| The re-aim caches the new target's type in a global the resistance reads. | The caller reads the type from the target. | — |
| The multiplayer party list (0x441310) joins the party and non-party tests and the party gather. | Not modelled (single player). | — |
| The world queries, the objects' fields and the run's globals are read directly. | The caller passes the facts; the world port answers the queries. | — |

MG18 (`Tgt_Obj_No_ST_Critter_Dead` without the critter bit), MG21 (`Tgt_Non_Party_Critters`), MG22 (`Tgt_Obj_Radius` without the
object bit) and MG23 (`Tgt_Tile_Self` without the tile bit) are the original's intent: the test and the collection handle each
bit on its own (the not-dead and non-party tests run without the critter bit, the radius bit is an object kind, the tile-self
bit gathers the source's tile), so the values follow the original in both modes. MG27: the tile-radius test measures the target
tile's distance to itself, so it never refuses a radius ≥ 0; the comparison it was meant to make is unknown, and with the
installed records (tile-radius components of records 11, 27, 180 and 200 have their action's radius, and their entries come from
the same square) any likely fix changes nothing, so it follows the original in both modes, as MG5 does (decided under the owner's 2026-10-20 acceptance of the MG5 rule; recorded by ARC-MAGIC-008).

## Not ready

- No caller: the run lifecycle (MT9a) gathers the list per action and the per-target stage (MT10) filters and re-aims per
  component; the spell UI's picker (0x4F2680, 0x4F28A0) is the casting UI's (`SpellUiRules.Pick`, ARC-MAGIC-021).
- No host fills `ISpellTargetWorld` yet; the viewer's map data and the session's party are its sources.
- The line of fire (0x4ADE00) is ARC-COMBAT-012's `LineOfFireRules.Trace`; no caller passes it here yet.
