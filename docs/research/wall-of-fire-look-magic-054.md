# Wall of Fire: how the wall looks, and how many pieces (ARC-MAGIC-054)

Status: **Closed.** The owner found on 2026-11-12 that the retail game makes 5 pieces, as the rules read; the 3 pieces of the first screenshot came from Arcanum Community Edition, no longer a comparison reference. Register entry B55 in [open bugs](open-bugs.md). Owner report 2026-11-11, with two screenshots (Arcanum CE in a cave, Steamwoven on the main map).
Requirements (reference only): [FR-MAGIC-008](../requirements/combat-magic-tech.md#fr-magic-008). Earlier reading:
ledger [SUM11, SUM13](ledger/spell-runs.md#summon-component-and-the-objects-it-creates-arc-res-101).

## What the screenshots show (the first one was not the retail game)

| | First screenshot (Arcanum CE, as it turned out) | Steamwoven (before this change) |
| --- | --- | --- |
| Pieces | 3 | 5 |
| Step between pieces | one tile (80 px at 800 x 600) | one tile |
| Line | along the screen's horizontal | along a screen diagonal |
| Look | translucent yellow-orange flame over a bright base; no dark fringe | dark, opaque orange blobs with a jagged black fringe (the "black particles") |

The step and the size of a piece are the same in both. The line's direction is decided by the cast: the original's wall gather turns the direction from the caster to the target tile
into a step perpendicular to the cast (TA12, read again in `0x4F40B0`: the table agrees with the one in `SpellTargetRules`), so two casts from different places lie along different
lines; this is no bug. The one real difference was the blend; the count turned out to be no difference (Question 3).

## Cause 1, fixed: the blend

The wall's piece is basic prototype 4031 (file 4051) whose stored blit flags are **0x10, additive** (read with the viewer's prototype reader: `blit=0x10/255`). The map's own bodies take their
blend from their blit flags (`AreaScenePlan` `State`, ARC-RENDER-018: additive wins over multiply, which wins over constant alpha); a body the session **creates** was placed by `AreaScenePlan.Place` with no
blend at all, so the sprite's black matte was painted over the ground (the fringe) and the flame was opaque. Fix: `ObjectSoundFields` carries the prototype's blit flags and alpha (slots 8 and 10), `AreaScenePlan.WithBlend` gives a
placed body the same blend rule as `State` (additive, multiply, constant alpha, mirror bits; base palette, unlit), and the viewer applies it to every created scenery and eye-candy body from its prototype. Other created
objects whose prototypes carry blit flags get the same treatment (none of the installed Summon prototypes besides 4051 is known to; not censused).
Observation: the smoke stage `summon-wall` saves the drawn wall when `ARC_AREA_SUMMON_WALL_CAPTURE=PREFIX` is set (`PREFIX-wall.png`; nothing is committed).

## Cause 2, fixed in ARC-MAGIC-055: the tile-blocked test of the cast map

The area test of `Tgt_Tile_Empty` and `Tgt_Tile_Empty_Immobiles` calls the original's `tile_is_blocking` (`0x4D7110`: the sector's block mask, else the tile name's flags, PB1). The cast map
(`AreaSpellCastMap.TileBlocked`) asked only whether the tile's sector was **admitted**, so blocking terrain never refused a piece of Wall of Fire (or Wall of Stone, Fireflash). It now uses the step evidence's
terrain verdict, as the drop map and the path request do (a tile the evidence cannot speak for counts as blocked). On the crash-site cave map the wall's five tiles now give 3 pieces: one tile of blocking terrain
is dropped, and the hero's own start tile is dropped for another reason (an object the test counts: a wall, portal, scenery or trap without "no block"). Open ground still gives 5 (smoke stage `summon-wall`).
The oracle (F3) had already run the collector and the area test on 10,000 inputs each; the input it could not check was the world's tile verdict, which is what this fixes.

## Question 3, closed

The owner found that the retail game makes 5 pieces; the 3 pieces seen first were Arcanum Community Edition's. Our count of 5 on open ground stands, and Cause 2 stays as a faithful change (terrain drops pieces where the executable's `tile_is_blocking` would); it is unrelated to the CE count, and our own cave run's 3 pieces is a separate result.
