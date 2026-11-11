# Wall of Fire: how the wall looks, and how many pieces (ARC-MAGIC-054)

Status: **Bug fixed in part, one question open.** Register entry B55 in [open bugs](open-bugs.md). Owner report 2026-11-11, with two screenshots (the original game in a cave, Steamwoven on the main map).
Requirements (reference only): [FR-MAGIC-008](../requirements/combat-magic-tech.md#fr-magic-008). Earlier reading:
ledger [SUM11, SUM13](ledger/spell-runs.md#summon-component-and-the-objects-it-creates-arc-res-101).

## What the screenshots show

| | Original | Steamwoven (before this change) |
| --- | --- | --- |
| Pieces | 3 | 5 |
| Step between pieces | one tile (80 px at 800 x 600) | one tile |
| Line | along the screen's horizontal | along a screen diagonal |
| Look | translucent yellow-orange flame over a bright base; no dark fringe | dark, opaque orange blobs with a jagged black fringe (the "black particles") |

The step and the size of a piece are the same in both. The line's direction is decided by the cast: the original's wall gather turns the direction from the caster to the target tile
into a step perpendicular to the cast (TA12, read again in `0x4F40B0`: the table agrees with the one in `SpellTargetRules`), so two casts from different places lie along different
lines; this is no bug. The two real differences are the blend and the count.

## Cause 1, fixed: the blend

The wall's piece is basic prototype 4031 (file 4051) whose stored blit flags are **0x10, additive** (read with the viewer's prototype reader: `blit=0x10/255`). The map's own bodies take their
blend from their blit flags (`AreaScenePlan` `State`, ARC-RENDER-018: additive wins over multiply, which wins over constant alpha); a body the session **creates** was placed by `AreaScenePlan.Place` with no
blend at all, so the sprite's black matte was painted over the ground (the fringe) and the flame was opaque. Fix: `ObjectSoundFields` carries the prototype's blit flags and alpha (slots 8 and 10), `AreaScenePlan.WithBlend` gives a
placed body the same blend rule as `State` (additive, multiply, constant alpha, mirror bits; base palette, unlit), and the viewer applies it to every created scenery and eye-candy body from its prototype. Other created
objects whose prototypes carry blit flags get the same treatment (none of the installed Summon prototypes besides 4051 is known to; not censused).
Observation: the smoke stage `summon-wall` saves the drawn wall when `ARC_AREA_SUMMON_WALL_CAPTURE=PREFIX` is set (`PREFIX-wall.png`; nothing is committed).

## Question 2, open: 3 pieces or 5

The rules read from the executable give **5** pieces on open ground: one Summon applied to the five tiles of `Tgt_Tile` plus `Tgt_Tile_Radius_Wall` with radius 2 (the collector `0x4F40B0` runs i = 0 to the radius, both sides,
the area has no count limit: `Count` reads -1), each kept only if `Tgt_Tile_Empty_Immobiles` passes. The original's screenshot has 3, centred on the cursor. Not found in the read: any step that trims the list.
What is known:

- In the viewer on the crash-site cave map the wall made **4** pieces and none on the hero's own tile (the tile test refuses a tile; open ground gives 5). So the tile test can drop pieces, and the owner's cave
  may have dropped the two outer ones. Which tiles refused, and why, is not known (a blocking terrain tile, or an object type the test counts).
- The oracle (F3) already ran the collector and the area test on synthetic inputs; it did not run them on the cave's tile data.

To settle it, the owner casts the original's Wall of Fire once on **open ground outdoors** and counts the pieces. Five confirms the rules and points at the cave's blocked tiles (then the tile test is
checked against the cave's data with the oracle); three means a limit the read missed and the next step is a run of the original's cast path in the oracle. No change to the count rule is made here.
