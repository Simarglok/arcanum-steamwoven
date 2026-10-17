# Roofs (ARC-RENDER-021, ARC-RENDER-022)

## Scope

Roofs of any whole map of the content stack, read as the original reads them
and kept with the original's rules for hiding, fading and covering, checked in
the retail executable ([ledger RF1–RF7](../research/original-verification-ledger.md#roofs-arc-render-021);
the stored layout and the first house are the
[first building research](../research/first-building-evidence.md#roofs-fr-scene-010), ARC-RES-029).
ARC-RENDER-021 is the data half: the roofs in the strict snapshot and the rules
as a Godot-free grid. Drawing them in the viewer's map mode is ARC-RENDER-022.
The pinned Crash Site exterior and cave profiles are unchanged: a roof list
there stays an unsupported sector state, so the exterior keeps no roofs.

## Data (ARC-RENDER-021)

A whole-map plan ([whole-map plans](../world/whole-map-plans.md)) binds two more
name tables, `art/roof/roofname.mes` and `art/roof/roofshade.mes`, each the
stack's winner (a stack without them reads no roofs, as before ARC-RENDER-021). For every core sector whose roof list is present (its first
word is 0), the owner reads the 256 roof art words that follow:

- Slot `x/4 + 16·(y/4)` of the sector-local tile is one 4 × 4 block; the word
  `0xFFFFFFFF` is no roof.
- A roof art is class 10: the number in bits 19–27 is the position of the
  file's name in `roofname.mes` (entries in order, as the original indexes them;
  entry 0 must exist), the frame in bits 14–18 (0–8), the fill bit 13, the fade
  bit 12, the palette in bits 4–5 and the mirror flag in bit 0. Pieces 9–12 are
  frames 0–3 drawn mirrored. Any other bit, a frame above 8 or a mirrored frame
  above 3 is refused for that slot alone (`UnsupportedArtClassOrState`, source
  index `4096 + slot`); a name past the table's end is `MissingSource`.
- Each block becomes an `AreaRoofRecord` (sector, slot, first tile of the block,
  stored art, piece, fill and fade at T0, visual) and its frame a visual
  (`AreaLayerKind.Roof`, source index `4096 + slot`) of `art/roof/<name>.art`,
  decoded once per distinct selection like every frame of a whole map. Fill and
  fade are state: the visual's lookup art leaves those bits out.
- `AreaSnapshot.RoofShades` holds `roofshade.mes` entries 0, 1 and 2 (opaque,
  partial, transparent; read like C `atoi` into a byte) when the map has roofs.
- Limits: the visual budget of a whole map grows by 256 per sector.

## Rules (ARC-RENDER-021)

`AreaRoofGrid` (`Arcanum.Data.Area`) starts from a snapshot's roofs and keeps
their fill and fade bits; `Revision` moves on every change. Each method that
implements an original function carries its address
([function registry](../verification/functions.csv), F2):

- `Recalc(x, y)`: the player entered the tile; the block under it is filled
  when its piece's band holds the tile, unfilled otherwise, and only a change
  floods (RF3).
- `FillOn`, `FillOff`, `FadeOn`, `FadeOff`, `IsFaded` (RF6) and the flood
  itself (RF5): a flood stops at a block already in the requested state and at
  an edge piece whose outer side faces the side it arrives from, so it stays
  inside one roof.
- `IsCovered(x, y, countFaded, indoor)`: an indoor tile under the block three
  tiles further on both axes, not filled, inside that piece's band; a faded roof
  covers only when the caller counts it (objects do, tiles do not) (RF4). The
  caller tells whether a tile's art is of type 0: tile art with bit 8 clear,
  facade art with bit 25 clear, and any other art (a missing tile included),
  as in the [leaving test](../research/original-verification-ledger.md#leaving-a-building-through-a-locked-portal-arc-res-049).
- `HotSpot(visual)`: a mirrored roof frame is drawn with hot spot x 0 and
  offset x 0, unlike other mirrored art (RF7).
- `Slot`, `Normalize`, `ScreenPosition`, `Art` (RF1), `FadeCorners` (the corner
  alphas of a faded piece on the original's hardware path, RF2; without 3D
  hardware the original stipples a faded roof) and `Piece`.

## Installed original

The opt-in whole-map test (`AreaMapPlansRealDataTests`, `DAT_ROOT`) reads every
interior with no refused roof: five interiors have roofs, `tulla interior` 515
blocks, `tulla interior - battle - earth` 184, `tulla interior - battle - air`
132, `bates mansion lev 2` 72 and `caladon-mafia underground` 28; every
interior stays complete, and the shades are 255, 200 and 80.

## Not yet

- Drawing, hiding, the walk's recalculation, the wall check's fading, covered
  tiles and objects and the roof's click test in the viewer (ARC-RENDER-022).
- Roofs of the start map (it is not opened in map mode) and of the pinned
  Crash Site exterior.
- What recomputes fill and fade after a save is loaded is not traced (U7 of the
  research).
