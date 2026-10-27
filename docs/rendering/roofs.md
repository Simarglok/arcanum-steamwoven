# Roofs (ARC-RENDER-021, ARC-RENDER-022)

## Scope

Roofs of any whole map of the content stack, read as the original reads them
and kept with the original's rules for hiding, fading and covering, checked in
the retail executable ([ledger RF1–RF10](../research/ledger/world.md#roofs-arc-render-021);
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
  as in the [leaving test](../research/ledger/world.md#leaving-a-building-through-a-locked-portal-arc-res-049).
- `HotSpot(visual)`: a mirrored roof frame is drawn with hot spot x 0 and
  offset x 0, unlike other mirrored art (RF7).
- `Slot`, `Normalize`, `ScreenPosition`, `Art` (RF1), `FadeCorners` (the corner
  alphas of a faded piece on the original's hardware path, RF2; without 3D
  hardware the original stipples a faded roof) and `Piece`.

## Viewer (ARC-RENDER-022)

The viewer's map mode draws the roofs of the published map with `AreaRoofGrid`
as the state, so any map with roofs works the same way:

- **Plan.** `AreaScenePlan.Roofs` holds one command per roof block, outside the
  terrain and body commands: its frame is drawn at the block's normalized tile's
  screen origin moved 120 left and 200 up, less the frame's hot spot (`HotSpot`:
  hot spot x 0 for a mirrored roof frame), mirrored for pieces 9–12 (RF1, RF7).
  The roofs' frames join the map's CPU frame repository.
- **Drawing.** An unfilled roof is drawn after every terrain item and body,
  tinted by the outdoor light only (the original's hardware path, RF2); a faded
  roof takes `FadeCorners`' four corner alphas, interpolated over the frame in
  its drawn orientation. Roofs stay Legacy under an HD pack.
- **Hiding.** Every tile the actor's committed cell changes to recalculates the
  roof of that tile (RF3, the flood RF5), so stepping onto a tile whose block
  band holds it hides that whole roof; a same-map transfer first unfills the
  roof of the tile it leaves (RF8). A map change and a reset scene start from
  the map's stored roof state and recalculate the arrival tile.
- **Fading.** Each wall the wall check holds counts on the roof of its
  normalized tile, a wall whose flags forbid its own fade included
  (`AreaWallCheckState.HeldWalls`); a roof fades with its first wall and
  unfades with its last (`AddWallFade`/`ReleaseWallFade`, RF9).
- **Covering.** An unfaded, unfilled roof covers the indoor tiles three tiles
  before its band (RF4): they are drawn again in black right after the terrain,
  where the original leaves them undrawn on its cleared view. A roof faded or
  not covers the bodies on those tiles: they are not drawn. The pick does not
  test the cover (RF10), so a covered body is still picked where its frame is
  opaque and no roof pixel is hit.
- **Picking.** A click whose point, moved 120 down, falls on a tile with an
  unfilled roof and on an opaque pixel of that roof's frame picks no object
  (`HitCandidate` and the pixel test, RF10).
- The HUD names the roof blocks and how many are hidden or faded.

The native map smoke observes it when a roof is reachable from the map's start
(`ARC-RENDER-022 native-smoke roofs PASS`, or `NONE` / `NO-REACHABLE-ROOF`):
next to a roof, outside it, roofs are drawn in view and, when the view has a
roof pixel over a pickable body, a click there picks nothing (`PickOverBody`
says whether it was checked); standing on a tile under a roof whose band holds
it hides that roof. On `tulla interior` the first such tile hides 491 of its 515
blocks (one roof over the whole building); `ARC_AREA_ROOF_CAPTURE=PATH` saves
the views outside and inside.

## Installed original

The opt-in whole-map test (`AreaMapPlansRealDataTests`, `DAT_ROOT`) reads every
interior with no refused roof: five interiors have roofs, `tulla interior` 515
blocks, `tulla interior - battle - earth` 184, `tulla interior - battle - air`
132, `bates mansion lev 2` 72 and `caladon-mafia underground` 28; every
interior stays complete, and the shades are 255, 200 and 80.

## Not yet

- The software path's stippled fade, the roof switch and the view types other
  than isometric; the black of a covered tile is the cleared view the original
  leaves, not checked pixel for pixel.
- The actor itself on a covered tile is still drawn (T0 bodies are covered on
  their tile, and since ARC-WORLD-037 a companion on the tile it stands on); the tile scripts' and other critters' moves do not
  recalculate roofs (the original does it for the local PC only).
- Roofs of the pinned Crash Site exterior (a regression smoke only since
  ARC-RENDER-028). The main world map opens by window since ARC-RENDER-024 and
  its windows read roofs (ARC-DATA-036); no roof smoke runs there.
- What recomputes fill and fade after a save is loaded is not traced (U7 of the
  research).
- The hit test's pixel rule: the original counts palette indexes 0 and 1 as a
  miss and tests a roof frame unmirrored (its mirror branch serves other art
  classes only), while the viewer tests the decoded alpha in the drawn
  orientation, so pieces 9–12 and index-1 pixels may differ (RF10).
