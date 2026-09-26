# Crash Site terrain identifier resolution (ARC-RES-004)

## Scope and verdict

This is a bounded, read-only terrain-resource investigation for the frozen
Crash Site sector. It adds no loader, renderer, full map traversal, object
parser, precedence policy, extracted asset, or original-game payload to the
repository.

**Verdict:** the selected sector's tile/facade table decodes into its two
CE-defined packed identifier classes and, after applying the callback's tile
name normalization, resolves all 4,096 cells to one candidate in the explicit
`arcanum2.dat` source. Every selected frame was boundedly inspected before the
representative decodes. The pinned source also establishes a bounded,
renderer-independent diagnostic footprint/crop/flip specification for the
tile and editor-facade draw calls below. This does not establish source
precedence, lighting/palette parity, a general runtime facade layer, or static
scenery readiness.

All CE statements below are source-derived from the single external lineage
at commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`, not independent format
confirmation. Retail byte measurements are labelled as such.

## Frozen input and byte boundary

The input remains the R3 module described in
[crash-site-slice.md](crash-site-slice.md) and
[crash-site-evidence.md](crash-site-evidence.md):

| Field | Value |
| --- | --- |
| map / start | `Arcanum1-024-fixed`, `(92958, 82592)` |
| sector / packed identifier | `(1452, 1290)` / `86570436012` |
| local origin / extent | `(92928, 82560)` / `64 x 64` |
| selected DAT entry | ordinal `463`, compressed, decoded length `47291` |
| module fingerprint | `355938305` bytes; SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` |
| explicit resource fingerprint | `147991122` bytes; SHA-256 `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` (`arcanum2.dat`) |

The independent project probe's bounded prefix establishes zero lights and a
4,096-word table at offsets `4..16387`; the table order is now also tied to
CE's `src/game/tile.c::tile_id_from_loc` and `src/game/tile.h::TILE_MAKE`:
`ordinal = localX | (localY << 6)`. Thus ordinal is x-major within each
successive local-Y row, with `localX = ordinal & 63` and
`localY = (ordinal >> 6) & 63`. `src/game/sector_tile_list.c` reads the
serialized values into this 4,096-element array; `tile_art_id_at` indexes it
with that ordinal. This does not establish semantics for any later sector
suffix bytes.

The real R3 table contains 3,033 high-nibble-0 words and 1,063
high-nibble-11 words, 1,041 distinct words overall. Under the cited CE source,
those classes dispatch to tile and facade handling, respectively. There were
no other high-nibble classes in this table. These are retail aggregate
measurements; no raw identifier list is retained.

## CE source map: identifier, lookup, resource, frame

### Dispatch and packed fields

`first_party/tig/src/art.c` at the pinned commit is the behavioral source for
these field interpretations.

* `tig_art_type` reads bits `28..31` (`art.c:668-671`). The selected classes
  are `TIG_ART_TYPE_TILE` (0) and `TIG_ART_TYPE_FACADE` (11).
* For a tile, `tig_art_tile_id_create` (`art.c:1427-1451`) defines: `num1`
  bits `22..27`, `num2` bits `16..21`, edge/variation-1 bits `12..15`,
  variation-2 bits `9..11`, indoor/outdoor type bit `8`, flip flags `7` and
  `6`, palette bits `4..5`, and the low flags. The source's accessors
  `sub_503700` and `sub_5037B0` apply the two fixed edge tables and low-flag
  adjustment (`art.c:1477-1529`), but the callback does not consume an
  unnormalized sector word directly.
* For a facade, `tig_art_facade_id_create` and its accessors
  (`art.c:2473-2531`) define its number as bits `17..24` plus 256 when bit 27
  is set, tile number bits `11..16`, type bit 25, flip bit 26, frame bits
  `1..10`, and walkability bit 0.

The probe implements only these two branches. Invalid or a third class is a
fail-closed error; no default resource or placeholder is selected.

### MES initialization and pathname lookup

The real chain is callback based, not a filename guess:

1. `src/main.c:177-182` installs `name_resolve_path` as the TIG ART-file-path
   resolver; `first_party/tig/src/art.c:372-408` stores that callback during
   ART initialization.
2. `tig_art_build_path` delegates every non-misc ART ID to that callback
   (`art.c:5452-5497`), and `tig_art_exists` uses that resolved path
   (`art.c:465-477`).
3. `src/game/name.c::name_init` loads the name-table subsystem. Its actual
   initialization sequence invokes `a_name_tile_init` then
   `a_name_facade_init` (`name.c:236-379`). The tile initializer requires exact
   MES keys `0`, `100`, `200`, `300`, and `400`: range counting starts only
   when each category's exact start exists, loading resets only at the first
   four starts, and edge loading searches exact key `400`
   (`a_name.c:193-222`, `424-540`, `543-575`; `mes.c:252-279`). The facade
   initializer requires 1 through 511 entries and exact key `0`, sorts the
   loaded MES entries, and appends their values sequentially to
   `facade_names[index++]` (`a_name.c:1107-1183`).
4. `name_resolve_path` normalizes first and dispatches tile and facade IDs to
   `a_name_tile_aid_to_fname` and `a_name_facade_aid_to_fname`
   (`name.c:856-881`, `973-977`). Tile lookup chooses the MES category from
   outdoor/type and flip bits, then `build_tile_file_name` applies the actual
   edge/variation and ordering rules (`a_name.c:256-385`). Crucially,
   `name_normalize_aid` clears a tile's palette and all four low flag bits
   before that dispatch (`name.c:648-655`; `art.c:1011-1025,1041-1055`). The
   probe keeps the raw identifier for frame/palette evidence but uses
   `rawId & ~0x3f` only for the callback pathname calculation. Facade lookup uses
   its decoded number as a zero-based ordinal into the sorted,
   sequentially-populated `facade_names` array, then constructs its ART
   pathname (`a_name.c:1129-1147`). It is not a MES-key lookup.

The companion probe parses only brace-keyed MES entries needed by these two
lookups and fails closed on malformed syntax or duplicate keys. It requires the
five exact tile initialization keys `0`, `100`, `200`, `300`, and `400` before
forming the four tile categories; later keys within a category may remain
gapped. Facade initialization rejects counts outside 1–511 and a table without
exact key `0` before it forms the sorted ordinal table. For facades it
deliberately discards the numerical keys only after sorting, retaining values
in ordinal order, matching the cited initialization path. It preserves the
source token before `/` exactly rather than applying an unsourced trim. Its
positive ordinal regression uses gapped keys (`0`, `25`, `100`, `333`) and
proves packed ordinal `3` selects the fourth sorted value rather than looking
for key `3`; this valid gapped lookup does not relax the initialization key-0
requirement. Synthetic DAT fixtures drive malformed, missing and duplicate lookup, a
non-zero facade start, each shifted/missing tile anchor, unsupported class,
invalid selector, normalized duplicate candidate, normalized case/separator
pathname, separated corrected versus historical candidate inventories, and
every resolver branch through the production archive lookup path. The macOS
probe explicitly models CE's **non-Windows** DAT-database comparison: lowercase
ASCII plus `\\`→`/` conversion, with exactly one normalized candidate required
in the selected source (`database.c:180-185,356-374`; `compat.c:9-22`). In the
pinned `compat.c`, the slash conversion is a non-Windows conditional branch;
Windows is a no-op before lowercasing. This experiment makes no Windows-path or
cross-profile duplicate claim. The bounded local run reports representative
chains below, but no MES text or decoded pixels.

### Selected frame and metadata

For these types CE has no rotation branch:
`tig_art_id_rotation_get` returns zero for both tile and facade
(`art.c:761-778`). `tig_art_id_frame_get` selects frame zero for tiles and the
facade's 10-bit frame field for facades (`art.c:903-921`).
`tig_art_id_palette_get` (`art.c:1029-1036`) takes bits `4..5` only for a
tile and returns palette `0` for a facade; facade bits `4..5` instead belong to
its frame field. The synthetic regression gives a facade a frame whose bits
`4..5` are nonzero and requires its selected palette to remain zero. For each
bounded representative the probe uses that `(group=0, selected frame,
type-specific selected palette)` selection with the existing
`ArtDecoder.Inspect` and `ArtDecoder.Decode`; it rejects an out-of-range frame
or entry above 1 MiB before decode.

The project ART decoder's supported one-frame byte contract is documented in
[art.md](../formats/art.md). This confirms a selected physical frame and its
bounded metadata for representatives, but not timing, palette effects,
resource precedence, or visual parity.

## Read-only local result

The following command was run against the frozen module and one explicitly
chosen local resource archive. Paths are supplied by the operator and are not
recorded here:

```text
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.local/share/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --no-build -- --self-test
dotnet run --project tools/research/Arcanum.TerrainProbe/Arcanum.TerrainProbe.csproj --no-build -- --module <frozen-module.dat> --resource <explicit-resource.dat> [--inventory <read-only-installed-archive> ...] [--loose-root <read-only-art-root> ...]
```

The executed real-data run used the explicit base archive `arcanum2.dat` and
reported:

```text
input module_bytes=355938305 module_sha256=8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f resource_bytes=147991122 resource_sha256=4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d
database_path_profile=ce-non-windows-macos ascii_lowercase=true backslash_to_slash=true
lookup tables tile_categories=31,6,2,13 facade_entries=373
module sector_ordinal=463 decoded_bytes=47291 tiles=4096
resolution tile_cells=3033 facade_cells=1063 unique_tile_ids=284 unique_facade_ids=757 source_candidates=1 tile_unresolved_cells=0 facade_unresolved_cells=0
coverage zero_matches=0 ambiguous_candidates=0 bad_selectors=0 unsupported_behavior=0 selected_frame_validated=4096
normalization branch=tile-outdoor-forward changed_cells=457 changed_ids=86 raw_zero_cells=457 raw_zero_ids=86 recovered_cells=457 recovered_ids=86
normalization branch=tile-single-material changed_cells=0 changed_ids=0 raw_zero_cells=0 raw_zero_ids=0 recovered_cells=0 recovered_ids=0
branch kind=tile name=tile-outdoor-forward cells=1269 ids=253 resolved_cells=1269 unresolved_cells=0 representative=true
branch kind=tile name=tile-single-material cells=1764 ids=31 resolved_cells=1764 unresolved_cells=0 representative=true
branch kind=facade name=facade-ordinal cells=1063 ids=757 resolved_cells=1063 unresolved_cells=0 representative=true
inventory set=corrected candidate_paths=220 sources=8 sources_with_candidates=1 candidate_entries=220 precedence=not_inferred
inventory set=historical_pre_normalization candidate_paths=86 sources=8 sources_with_candidates=0 candidate_entries=0 precedence=not_inferred
loose_inventory set=corrected candidate_paths=220 roots=3 roots_with_candidates=0 candidate_entries=0 precedence=not_inferred
loose_inventory set=historical_pre_normalization candidate_paths=86 roots=3 roots_with_candidates=0 candidate_entries=0 precedence=not_inferred
placement relative(0,0)=0,0 relative(1,0)=-40,20 relative(0,1)=40,20 start(30,32)=80,1240
integrity module_before_after_match=true resource_before_after_match=true
```

The original pre-normalization probe result—2,576 resolved tile cells and 457
unresolved cells—is retained as historical evidence. The bounded comparison
shows its cause: all 457 cells (86 distinct raw IDs), and no other branch,
changed pathname when the callback's tile palette/flag normalization was
applied; their former paths each had zero candidate while their normalized
paths resolve uniquely. The corrected run has zero missing/ambiguous/bad
selector/unsupported results and validates all 4,096 selected ART frames.
Both selected input fingerprints were recomputed before and after and matched;
the tool writes neither source input nor decoded payloads.

### Reproducible representative chains

The following is the complete bounded representative output from that run, not
a corpus dump. `local` is `(x,y)` within the fixed 64×64 sector and `entry` is
the unique raw-ASCII pathname winner's DAT ordinal in `arcanum2.dat`. The
`anchor` is the projected cell anchor including the source-confirmed `+1` X
tile/facade rectangle adjustment; it is a boundary, not a claim that these ART
metadata fields have been reduced to a C# destination rectangle.

| kind / branch | local / ordinal / raw identifier | packed interpretation and lookup | unique ART winner and physical selection | metadata / confirmed anchor |
| --- | --- | --- | --- | --- |
| tile / `tile-outdoor-forward` | `(8,6)` / `392` / `0x000111C0` | `num1=0,num2=1,edge=1,variation=0,outdoor=1,flip1=1,flip2=1,palette=0,low=0`; forward outdoor pairing | `art\\tile\\drtgrs6a.art`, entry `4587`; group `0`, frame `0`, requested/effective palette `0/0` | `78×40`, hot `(39,39)`, offset `(0,0)`; anchor `(-79,280)` |
| tile / `tile-outdoor-forward` | `(61,8)` / `573` / `0x000111C1` | `num1=0,num2=1,edge=1,variation=0,outdoor=1,flip1=1,flip2=1,palette=0,low=1`; forward outdoor pairing | `art\\tile\\drtgrs6a.art`, entry `4587`; group `0`, frame `0`, requested/effective palette `0/0` | `78×40`, hot `(39,39)`, offset `(0,0)`; anchor `(-2119,1380)` |
| tile / normalized recovery | `(53,12)` / `821` / `0x000131C1` | raw low flag `1` normalizes to pathname ID `0x000131C0`; raw callback-emulation path had zero candidates, normalized outdoor-forward path wins | `art\\tile\\drtgrs4a.art`, entry `4577`; group `0`, frame `0`, requested/effective palette `0/0` | `78×40`, hot `(39,39)`, offset `(0,0)`; anchor `(-1639,1300)` |
| tile / `tile-single-material` | `(18,0)` / `18` / `0x000001C0` | `num1=0,num2=0,edge=0,variation=0,outdoor=1,flip1=1,flip2=1,palette=0,low=0`; same-material rule | `art\\tile\\drtbse0a.art`, entry `4490`; group `0`, frame `0`, requested/effective palette `0/0` | `78×40`, hot `(39,39)`, offset `(0,0)`; anchor `(-719,360)` |
| facade / `facade-ordinal` | `(27,32)` / `2075` / `0xB6000801` | `number=0,tile=1,type=1,flip=1,frame=0,walkability=1,palette=0`; sorted facade-name ordinal | `art\\Facade\\BHull.art`, entry `90`; group `0`, frame `0`, requested/effective palette `0/0` | `78×40`, hot `(39,39)`, offset `(0,0)`; anchor `(201,1180)` |
| facade / `facade-ordinal` | `(27,31)` / `2011` / `0xB6000802` | `number=0,tile=1,type=1,flip=1,frame=1,walkability=0,palette=0`; sorted facade-name ordinal | `art\\Facade\\BHull.art`, entry `90`; group `0`, frame `1`, requested/effective palette `0/0` | `78×40`, hot `(39,39)`, offset `(0,0)`; anchor `(161,1160)` |
| facade / `facade-ordinal` | `(27,30)` / `1947` / `0xB6000804` | `number=0,tile=1,type=1,flip=1,frame=2,walkability=0,palette=0`; sorted facade-name ordinal | `art\\Facade\\BHull.art`, entry `90`; group `0`, frame `2`, requested/effective palette `0/0` | `78×40`, hot `(39,39)`, offset `(0,0)`; anchor `(121,1140)` |

The selected representatives cover every observed branch and include a bounded
failure-to-resolution chain. R2 re-ran the same immutable inputs after adding
the placement calculation: each listed tile produced an un-clipped
`78×40` source/destination at its anchor; the two raw-low-flag tile samples
reported `flip_x=true`; each listed facade produced an `80×40` footprint but
a frame-bounded `78×40` source/destination with `flip_x=false`. The output
format makes another holder of the frozen input reproduce the same identifier
→ lookup → ART → frame/metadata → placement chain without committing a MES
table or decoded ART payload.

The single resource archive was intentionally passed explicitly. A bounded
read-only inventory checked the installed `arcanum1.dat`, `arcanum2.dat`,
`arcanum3.dat`, `Arcanum4.dat`, `tig.dat`, selected `Arcanum.dat`,
`Vormantown.dat`, and `Arcanum.PATCH0`, using their before/after SHA-256
values. It separately compared both generated sets: the 220 corrected
non-Windows/macOS-normalized paths and the 86 distinct historical
pre-`name_normalize_aid` paths responsible for the 457 prior misses. The
corrected set occurred 220 times, all once in `arcanum2.dat`; it occurred zero
times in every other inspected archive. The historical set occurred zero times
in all eight archives, including `arcanum2.dat`.

The same run also performed direct, bounded `File.Exists` checks only for those
two generated path sets under the three relevant loose ART roots at the
installation root, its `data` child, and its `modules` child. None of the three
ART roots existed; therefore both sets had zero loose candidates. This is not a
recursive scan and it does not define a loose-file policy. If a historical-path
candidate had existed, `name_resolve_path` would still call
`name_normalize_aid` before `a_name_tile_aid_to_fname`, so that historical path
could not be the callback-selected path for this macOS profile. No such
candidate was observed. Together these bounded results rule out another
inspected DAT/PATCH, a discovered duplicate, or a discovered loose candidate as
the cause of the historical 457 misses; they do **not** establish general
module/PATCH/loose-file precedence.

## Historical R1 anchor-only boundary (superseded by R2 below)

CE `src/game/location.c::location_xy` maps a world cell in isometric view as:

```text
screenX = originX + 40 * (worldY - worldX - 1)
screenY = originY + 20 * (worldY + worldX)
```

After cancelling the caller-controlled origin and the shared `-1` constant,
sector-relative points are:

```text
relativeX = 40 * (localY - localX)
relativeY = 20 * (localY + localX)
```

The probe checks `(0,0) -> (0,0)`, `(1,0) -> (-40,20)`, `(0,1) -> (40,20)`,
and R3's start offset `(30,32) -> (80,1240)`.

The concrete draw call sites establish the cell anchor but not a standalone
C# baseline formula. `src/game/tile.c::tile_draw_iso` assigns the packed ID,
uses the projected location, and starts the tile rectangle at
`(centerX + 1, centerY)` (`tile.c:684-740`). `src/game/facade.c::facade_draw`
uses `location_xy` for each facade cell, offsets its isometric tile rectangle
by one X pixel, and passes it to the ART blit (`facade.c:117-151`). TIG then
loads selected `hot_x`, `hot_y`, `offset_x`, and `offset_y` frame metadata in
`tig_art_frame_data` (`art.c:1197-1257`). The source inspected here does not
supply a small independent formula that reduces the later renderer's blit
geometry to `ArtDecoder` metadata alone. Therefore:

* confirmed: relative projected cell anchors, `+1` X anchor adjustment,
  type-specific selected frame, and the metadata fields routed to the renderer;
* not confirmed: a C# draw rectangle/baseline transform, crop rule, palette
  lighting behavior, or facade-vs-tile overlap ordering;
* deliberately not claimed: static-scenery occlusion. It still depends on the
  unresolved selected-retail object envelope and object ART/baseline evidence.

## R2 renderer-independent tile/facade placement specification

This source-derived diagnostic specification is tied to pinned CE
`a7ff41b300ef712f0e7d088183a3d08957110cdd`; it is not CE execution, a
renderer, or a pixel/lighting-parity result. `location_xy` (`src/game/location.c:135-144`)
gives the relative point `(40 * (localY - localX), 20 * (localY + localX))`.
Both callers add one X pixel, therefore the diagnostic anchor is
`A = (40 * (localY - localX) + 1, 20 * (localY + localX))`. Arithmetic is
signed integer addition/subtraction with no rounding.

| call / source | initial footprint at `A` | selected ART metadata effect |
| --- | --- | --- |
| `tile_draw_iso` (`src/game/tile.c:667-705`) | `(A.x,A.y,78,40)` | frame width/height bound the copy; `hot_x`, `hot_y`, `offset_x`, and `offset_y` are not read here |
| `facade_draw` editor path (`src/game/facade.c:107-146`) | `(A.x,A.y,80,40)` | frame width/height bound the copy; the four geometry metadata values are not read here |

For a visible/dirty rectangle `V` and selected frame bound
`B=(0,0,frameWidth,frameHeight)`, let `F` be the table footprint. The exact
unscaled crop used by both call chains is `D0=intersection(F,V)`,
`S0=D0-F.origin`, `S=intersection(S0,B)`, and
`D=(F.x+S.x,F.y+S.y,S.width,S.height)`. An empty intersection emits nothing.
The input source/destination rectangle sizes are equal, so `art_blit` takes its
integer non-stretch branch (`first_party/tig/src/art.c:3095-3126,3144-3180`).
`TigArtBlitInfo` carries only rectangles/palette/blend state (`art.h:402-414`),
and neither terrain caller invokes `tig_art_frame_data`: viewer hotspot
alignment is explicitly inapplicable here.

Path normalization does not replace the raw draw ID. A tile flips sampled X
only when raw bits 7 and 6 and low flag bit 0 are all set:
`tig_art_blit` toggles X (`art.c:540-552`, `1579-1618`) and `art_blit` reads
the selected source X in descending order (`art.c:3221-3267`). Its footprint
and destination do not move, and this path has no Y flip. A facade's bit 26 is
flippable (`art.c:2473-2493`), but `facade_draw` supplies no flip flag and
`tig_art_id_flags_get` returns zero for facades (`art.c:2627-2639`); this
bounded call chain therefore establishes no facade mirror.

Adjacent un-clipped examples are tile `(0,0)` `F=(1,0,78,40)`, tile `(1,0)`
`F=(-39,20,78,40)` with overlap `(1,20,38,20)`, and tile `(0,1)`
`F=(41,20,78,40)` with overlap `(41,20,38,20)`. A facade `(1,0)` has
`F=(-39,20,80,40)` and overlaps tile `(0,0)` at `(1,20,40,20)`.
For one sector, tile's outer/inner loops move by `(40,20)` then `(-40,20)`
(`tile.c:689-695,823-836`); facade loops `y` then `x` (`facade.c:117-161`).
Thus the bounded order is `(localY ascending, localX ascending)`. In editor,
`gamelib_draw_editor` draws tiles then facades (`gamelib.c:1719-1745`), so the
diagnostic/editor layer order is tiles then facades. `gamelib_draw_game` draws
tiles but not facades (`gamelib.c:1703-1716`): this does not claim a normal
game-runtime facade layer or scenery/object/roof ordering.

`Arcanum.TerrainProbe --self-test` independently exercises nonzero signed
hotspots/offsets (non-use), raw tile flip, set facade flippable bit (non-flip),
viewport crop, smaller-frame crop, and adjacent X/Y overlap. The real-data run
uses the same calculation for every representative and reports footprint,
source, destination, and raw-ID flip state.

## Future terrain-only loader contract

A separate implementation task may accept only a caller-supplied, immutable
module plus an explicitly ordered resource source set. It must:

1. validate the frozen selector/fingerprint, light prefix, 4,096-word bounds,
   and x-major ordinal mapping;
2. classify only packed tile/facade words, reject every other class, and retain
   unresolved lookup as an error record rather than an image fallback;
3. initialize the bounded MES-derived tables, apply the CE-attributed packed
   field and normalized lookup rules, and require exactly one candidate under
   the chosen source policy;
4. use `DatArchive` and `ArtDecoder` for any approved bounded ART entry,
   retaining the physical group/frame/palette selection separately from ART
   metadata; and
5. expose the bounded diagnostic footprint/source/destination/flip record and
   mode-qualified terrain order, retaining hotspot/offset metadata as evidence
   rather than applying a viewer baseline.

For the present R3 source set, resource lookup can expose all 4,096 records
with explicit source identity and selected-frame validation. The R2 rule permits
a bounded diagnostic terrain record, but not a complete terrain scene: lighting,
palette treatment, camera/dirty-rectangle policy, runtime facade intent, and
renderer parity remain separate evidence.

## Remaining blockers

* Determine the selected-version object field envelope and classify an actual
  static-scenery subset. The object window is now framed and its bounded v119
  delimiter reaches the terminal count, but that does not prove object
  semantics, current ART/prototype selection, baseline, or occlusion.
* Establish lighting/palette treatment, dirty-rectangle/camera policy, and a
  measured renderer comparison before claiming terrain scene or CE visual
  parity. The source shows editor-only facade layering, not a normal game
  facade pass.
* Preserve the distinction between these CE-source-derived rules and any
  independent retail-byte evidence. A CE-derived implementation or catalogue
  does not create a second corroborating lineage.
