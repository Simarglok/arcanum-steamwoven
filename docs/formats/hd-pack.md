# HD pack and recipe format (ARC-DATA-014)

Status: **format, Godot-free reader, import and device budget, with synthetic tests.**
Code: [`src/Arcanum.Data/Hd`](../../src/Arcanum.Data/Hd); tests:
[`tests/Arcanum.Data.Tests/Hd`](../../tests/Arcanum.Data.Tests/Hd). Nothing here draws
an HD frame: binding textures, caches and the HUD are the viewer's
([ARC-RENDER-014](../rendering/terrain-viewer.md#hd-render-path-arc-render-014)), the setting, toggle and
active pack choice the viewer's too ([ARC-UI-006](../rendering/terrain-viewer.md#visual-profile-setting-and-toggle-arc-ui-006)), the build tool (`./arcanum hd-build` / `hd-import`) is ARC-DEV-003.
Interface art entries (slice kind `interface`, ARC-DEV-242) are [below](#interface-art-arc-dev-242); the viewer draws them since ARC-UI-089 ([terrain viewer](../rendering/terrain-viewer.md#visual-profile-setting-and-toggle-arc-ui-006)). Terrain chunk entries are bound since ARC-RENDER-016 (`ResolveTerrainChunk`). The rules this
format serves are in the [HD presentation contract](../rendering/hd-presentation-contract.md)
(ARC-RENDER-013, with its ARC-DATA-014 and ARC-RENDER-016 amendments).

Owner decisions it follows (2026-10-16, [decisions](../architecture/decisions.md), HD
pack): the pack is a separate local directory outside the repository and is not
distributed; it is obtained either by building it on the device from a recipe or by
importing a ready pack built on another device. After the owner rejected the measured
plain upscales (the edge kept its steps), the direction became "1 + 3": a **4×
soft-edge** pack as the base, and a generative experiment (ARC-RES-038). So scale and
alpha mode are recipe fields, 4× with soft alpha is the default shape, 2× stays
supported, and the method is **not** fixed by the format.

Nothing original or derived enters git: no ART pixels, HD frames, model weights,
adapters trained on Arcanum art, screenshots or real manifests. Tests generate synthetic
frames in temporary directories.

Operating the installed pack (staging, diff-only update, backup, rollback, iCloud copy) is
[`docs/workflow/hd-pack.md`](../workflow/hd-pack.md).

## Pack directory

```text
<pack>/
  manifest.json        identity, review, recipe pin, build record, entries
  recipe.json          the recipe it was built from (any name, pinned by hash)
  frames/…/*.png       one 8-bit RGBA PNG per entry (any layout under the rules below)
```

A pack is **portable**: PNG and JSON only, every path inside is relative, and nothing
names a user folder, an OS or a GPU path. The same directory works after being copied to
macOS, Linux/SteamOS or Windows, onto a case-sensitive or case-insensitive file system.
Paths (`file`, `recipe.file`) are lowercase ASCII segments `[a-z0-9_-]` with inner dots,
joined by `/`, 1..8 segments, each at most 64 and the whole at most 200 characters; no
`.`/`..`, no leading or trailing dot, no backslash, drive or absolute form, no Windows
device name (`con`, `prn`, `aux`, `nul`, `com0-9`, `lpt0-9`); entries end in `.png`,
the recipe in `.json`. A file or any directory below the pack root that is a symbolic
link (or a Windows junction), dangling or not, is refused when read, so a pack cannot
reach outside itself; other reparse points such as cloud-sync placeholders are ordinary
files. Every pack file must be a non-empty regular file: an empty file, a FIFO or a device
(which report length 0) is refused as unreadable without being opened. JSON files are UTF-8 without a byte-order mark; text that is not valid UTF-8 or
holds an unpaired surrogate makes the file malformed.

**Layout convention (ARC-DEV-376).** The reader reads every `file` as the manifest gives it and ignores unlisted files, so how a pack is laid out is
not part of the format. The player's local interface pack keeps `interface/<screen>/<kind>/<art>-f<NNN>.png` with a `layout-map.tsv`, `INDEX.md` and
`index.tsv` at its root; the rules, the map and the tool commands that keep it are in [`docs/workflow/hd-pack.md`](../workflow/hd-pack.md#pack-layout-arc-dev-376).

**Optimized textures (ARC-DEV-376).** A device-local cache of mipmapped textures, named by each PNG's SHA-256, sits beside the game's data and never inside
a pack; it is not part of the format ([workflow](../workflow/hd-pack.md#optimized-textures-arc-dev-376)). `HdPack.SpriteFileSha256`, `CheckPrepared`,
`DecodeFrame` and `InterfaceSprites` are the calls it uses.

Where packs live (discovery, `HdPack.Discover`): a **collection** is a folder whose
subdirectories are packs (the game's own pack folder, e.g. `user://hd-packs/`, or
`local/hd-packs/` in a checkout, which git ignores); a **pack location** is one pack
directory the user chose anywhere (an external drive, a shared folder). Hidden
subdirectories (leading `.`) are skipped; at most 64 packs per collection are opened
(option). Each listed pack carries its own open result, so one broken pack hides
nothing. One pack is active at a time; the viewer chooses it (ARC-UI-006): `ARC_HD_PACK`, else the
first pack that opened and is not rejected in `user://hd-packs/` and, in a source run, the checkout's
`local/hd-packs/`.

### Local widescreen main-menu art

The viewer can use an optional `main-menu-16x9.png` at the root of the active pack
when HD interface art is on. This local file sits outside the sprite manifest. Use an
opaque, non-interlaced 8-bit RGBA PNG, up to 3840 × 2160 and 40 MiB. Its middle
75 percent of width must register with the original 800 × 600 menu frame. The viewer
scales the background and the menu controls together and crops the side art on
narrower screens. The same plate backs every menu window that draws the table scene (main menu,
Single Player, New Game and the in-play menus; ARC-DEV-344); the options window and the other
windows keep their own art. A missing or unusable file leaves the normal menu art available.
Keep the source and generated art on the player's device (ARC-UI-122).

## manifest.json

```json
{
  "schema": 1,
  "kind": "arcanum-steamwoven-hd-pack",
  "id": "crash-site-bodies-4x",
  "scale": 4,
  "review": "unreviewed",
  "recipe": { "file": "recipe.json", "sha256": "<64 hex>" },
  "build": { "builder": "arcanum hd-build", "builderVersion": "1", "createdUtc": "2026-10-16T12:00:00Z",
             "gpu": "Apple M4 Pro", "os": "macOS 27" },
  "entries": [
    { "kind": "sprite", "file": "frames/0000.png", "sha256": "<png sha256>", "bytes": 12345,
      "slice": "crash-site-exterior-bodies",
      "art": { "sha256": "<ART payload sha256>", "view": 40960, "nameSha256": "<ART name sha256>" },
      "group": 0, "frame": 0, "palette": 0,
      "legacy": { "width": 64, "height": 47, "rgbaSha256": "<Legacy RGBA sha256>" },
      "review": "unreviewed" }
  ]
}
```

Every object has exactly its listed properties: an unknown or duplicate property, a
non-integer number, or a string outside its pattern refuses the pack. `schema` is 1; a
later incompatible format raises it.

| Field | Rule |
| --- | --- |
| `id` | `[a-z0-9][a-z0-9_-]{0,63}`; the import directory name |
| `scale` | integer N; accepted by default: 2 and 4 (`HdPackOptions.Scales`); must equal the recipe's |
| `review` | `unreviewed` / `approved` / `rejected` (pack); per entry the same (written by the ARC-RENDER-015 review, below) |
| `recipe` | relative path and SHA-256 of the recipe file, which must parse |
| `build` | printable ASCII labels, 1..128 characters, no `/` or `\` (so no local path can hide in them); `createdUtc` exactly `yyyy-MM-ddTHH:mm:ssZ`; `gpu` is recorded because outputs depend on it (ARC-RES-033) |
| `entries` | 0..400,000 (option); each `file` used once; each sprite key once; each terrain chunk (slice, x, y) once |

**Identity.** `HdPackIdentity` = (`id`, `scale`, SHA-256 of the `manifest.json` bytes).
The manifest pins every file by hash, so the identity covers the whole pack. A rebuild,
even of the same recipe on another GPU, and any review mark are a new identity, and HD
caches keyed by it reload. A copy (import) keeps the identity.

### Sprite entries: the portable frame key

**Dormant (ARC-RENDER-2175, owner decision 2026-11-15).** HD graphics means fonts, interface and light only: the viewer does not bind a pack's `sprite` or terrain-chunk entries under the HD option (every world frame draws Legacy as `WorldDormant`), and the pack builder for them (ARC-DEV-003) is deferred. The format, the reader, the identity oracle and this section stay in place for a later decision; the developer variable `ARC_HD_WORLD_SPRITES=1` runs the old path for a manual run on a sprite pack and makes the `hd` smoke skip its gate check (the toggle smokes and self-tests open the gate in code).

An HD frame is bound to the **install-independent part** of the ARC-RENDER-013 frame key
(`HdSpriteKey`):

| Part | From `AreaFrameKey` / `AreaVisualRecord` |
| --- | --- |
| `art.sha256` | `PayloadSha256`: SHA-256 of the whole ART entry after resource precedence (ARC-DATA-011) |
| `art.view` | `ViewLength`: decoded bytes the frame is read from (132..2³¹−1); since ARC-DATA-036 the ART's own extent for every AREA visual: bytes after the last frame are excluded (a pack built from map-mode frames before it misses the few frames of such an ART, which draw Legacy). A frame decoded outside the owner (`HdFrameQueries.ArtIdentity`) still uses the whole entry |
| `group` | physical group (0..7) |
| `frame` | frame (0..4095) |
| `palette` | **effective** palette slot (0..3): an HD frame is built from the RGBA of its own slot (ARC-RES-033) |
| `alpha` | optional, only `"masked"` (ARC-UI-113), only in a slice whose kinds include `interface`: the alpha check is the `masked` rule, not the slice's mode |

The DAT source role, ordinal, offset, flags and sizes of `AreaEntryIdentity` are left out
on purpose: they describe where the bytes sit in one installation, not what they are. The
checks bind the entry to this device's Legacy frame:

- `legacy.width`, `legacy.height`, `legacy.rgbaSha256`: the decoded Legacy frame
  (`AreaVisualRecord.RgbaSha256`) must be identical, or the frame is `SourceMismatch`.
- `art.nameSha256`: `AreaEntryIdentity.NameSha256` (SHA-256 of the raw entry name bytes).
  It only classifies a miss: a key the pack lacks whose ART name the pack knows from
  **other** ART bytes is `SourceMismatch` (a mod or another game version replaced the
  ART); anything else, including another frame of an ART the pack has, is
  `FrameMissing`.

### Terrain chunk entries

```json
{ "kind": "terrainChunk", "file": "chunks/4_1.png", "sha256": "…", "bytes": 1, "slice": "ground",
  "chunk": { "x": 4, "y": 1, "size": 512, "padding": 32 },
  "tiles": ["<Legacy RGBA sha256 of each composed tile/facade frame, in plan order>"],
  "legacy": { "width": 512, "height": 512, "rgbaSha256": "<composed level-0 chunk>" },
  "review": "unreviewed" }
```

Terrain is upscaled as composed 512-pixel chunks with neighbour padding, never tile by
tile (ARC-RES-033). An entry is parsed, validated (size equal to the recipe's
`terrain.chunkSize`, padding equal to its `terrain.padding` ≥ 32, legacy size equal to the
chunk, 1..4,096 tile hashes, slice selecting `terrainChunk`) and integrity-checked like a
sprite. The file is exactly `scale · size` square: the padding is neighbour context used
while building and cropped away, never stored.

**Binding a chunk (ARC-RENDER-016).** The viewer asks `HdPack.ResolveTerrainChunk` with an
`HdSpriteQuery` made by `HdSpriteQuery.ForTerrainChunk`: chunk coordinates as
`AreaTerrainChunks` numbers them in the map's chart, the SHA-256 of the composed level-0
Legacy chunk (transparent where no tile is drawn) and `HdChunkKey.TilesDigest` of the
`tiles` list (the SHA-256 of the lowercase hex hashes, each followed by a line feed). The
`chunk.x`/`chunk.y` pair is unique per slice only, so one pack can hold the chunks of two
maps in two slices (for example `exterior-ground` and `cave-ground`): candidates at the
coordinates are matched by composed Legacy frame and tile digest. No entry at those
coordinates is `FrameMissing`; entries there that all differ are `SourceMismatch` (a mod or
another version changed a tile); then the entry's review mark, file, PNG, size and alpha
checks apply as for a frame, against the composed chunk's binary alpha.

## Frame files

One PNG per entry in exactly one profile (`HdPng`): 8-bit RGBA (colour type 6), deflate,
filter method 0 with any of the five row filters, no interlace, IDAT chunks consecutive,
IEND last with nothing after it, every CRC correct, a valid zlib header, inflated data of
exactly the image size and a matching Adler-32 (the deflate framing itself is not
checked beyond what .NET's inflater reports; the file hash covers the bytes).
Ancillary chunks are skipped; any other critical chunk, PLTE included, is unsupported. The decoder refuses an image above
the pixel limit before inflating it and requires the inflated data to be exactly
`h·(1 + 4w)` bytes. `HdPng.EncodeRgba` writes this profile deterministically.

PNG and not raw RGBA, because the whole game is ~21 GB raw at 2× and ~84 GB at 4× for one
palette per ART (ARC-RES-033 census); packs can be partial and per slice, so early packs
hold only the frame keys a map uses.

## Binding a frame on this device

`HdPack.ResolveSprite(query, legacyRgba)` takes the portable key, the ART name hash and
this installation's decoded Legacy frame (its pixels must hash to the query; a
contradiction or a null field is a caller error and throws). It returns `Hd` with exactly
`N·w × N·h × 4` bytes or a per-frame refusal; it never throws for pack content and one
refusal changes no other frame. In order:

1. Pack review `rejected` → `PackRejected`.
2. Key absent → `SourceMismatch` or `FrameMissing` (above); Legacy size or RGBA hash
   differs → `SourceMismatch`; entry review `rejected` → `FrameRejected`.
3. `Invalid` with a reason: HD frame above the pixel limit (`TooLarge`, default 64 M
   pixels), file missing, unreadable or a link (`FileMissing`, `FileUnreadable`), size
   not the declared `bytes` (`FileSize`), SHA-256 differs (`HashMismatch`), not the PNG
   profile (`PngMalformed`, `PngUnsupported`), not exactly `N·w × N·h`
   (`SizeMismatch`), or the alpha check fails (`AlphaMismatch`).

**Alpha check**, by the recipe's alpha mode, against this device's Legacy alpha (binary in
the original art):

- `soft` (default): every HD pixel with alpha > 0 lies within one Legacy texel of the
  Legacy silhouette (its parent texel or one of the 8 around it is opaque), and the HD
  mask (alpha ≥ 128) and the nearest-replicated Legacy mask have an intersection over
  union of at least the recipe's `minIoUPermille` (900..1000). The edge may soften and
  move by up to one Legacy texel; it may not grow blobs or lose shape.
- `legacy`: every HD texel has exactly its parent Legacy texel's alpha (hard edge; with
  it, HD coverage equals Legacy coverage pixel for pixel).
- `legacySmooth` (ARC-UI-111; no recipe fields, the IoU is fixed at 900 ‰): the Legacy silhouette with an anti-aliased edge.
  (1) An HD pixel whose parent texel and all in-frame 8 neighbours lie on one side of the Legacy mask (alpha ≥ 128) has exactly
  its parent's alpha, so there is no square background, no growth and no leak; only the pixels along the edge may take any
  alpha. (2) A single-texel hole or speck (at least two in-frame 4-neighbours, all on the other side of the mask) keeps its
  centre (the pixels within a quarter texel of the texel's centre) on its own side of 128, so holes and colour keys survive.
  (3) The HD mask and the nearest-replicated Legacy mask have an IoU of at least 900 ‰. Exact Legacy alpha also passes it.
- `masked` (ARC-UI-113; never a recipe or slice mode, only an **entry's** own `"alpha": "masked"`, see below): the owner's mask
  replaced the Legacy alpha, so the HD alpha may be smaller than the Legacy silhouette in any way, but no HD pixel has alpha > 0
  farther than one Legacy texel from it (its parent texel or one of the 8 around it is opaque). No IoU gate, no hole rule: the
  mask decides the shape. An entry without the field is checked by its slice's mode as before.

There is **no comparison with reference HD hashes**: cross-GPU bit-exactness of the
upscalers is not established (ARC-RES-033), so packs are checked for integrity and
against the installation, not for reproducing someone else's pixels.

## Review marks (ARC-RENDER-015)

`HdPackReview.Save(pack, marks, packReview)` writes approve/reject marks into
`manifest.json`; the [ART viewer](../project/components/Arcanum.Godot.md#hd-beforeafter-review-arc-render-015)
calls it for each mark. Only the `review` values change: every other property, value
and order is copied (compact JSON, non-ASCII and HTML-like characters unescaped). The
rewritten manifest is parsed as a pack and compared with the opened one (everything but
the marks equal) before it is written to a hidden `.manifest-review-<guid>.tmp` in the
pack directory and renamed over `manifest.json`. Nothing is written when the marks equal
the current ones (`Unchanged`), when `manifest.json` is no longer the bytes the pack was
opened from, checked before writing and again just before the rename (`Changed`), when it
is missing, unreadable or a link (`ManifestUnreadable`), when the rewritten manifest would
not open as the same pack (`Invalid`), or when the file system refuses (`WriteFailed`, the
old manifest stays). The new manifest takes the directory's default file permissions. A marked pack is a new identity; frame files are
never touched.

For review, `HdReviewComparison.Compare` decodes the Legacy frame from the entry's own
view, physical group, frame and palette of the given ART bytes and binds the HD frame as
`ResolveSprite` does, except that review marks do not refuse it and a failed alpha check
still returns the pixels (`AlphaMatches` false) so the reviewer sees what the game would
refuse. It reports `ArtMismatch`, `ViewOutOfRange`, `LegacyUndecodable`, `PaletteMismatch`
(the slot falls back) or `Refused` with the pack's reason. `HdReviewIndex` finds a pack
frame for a shown frame (the full view first, otherwise the same group/frame/palette under
another view of the same bytes), counts marks and steps in manifest order.

## Import of a pack built elsewhere

`HdPackImport.Import(source, collection, replace)`:

1. The source must open, and `VerifyIntegrity` must find every entry file present, of the
   declared size, with the declared SHA-256 and a PNG header of exactly `N·w × N·h`.
   Any problem refuses the import (`IntegrityFailed` with counts per reason); a damaged
   copy is never imported partly.
2. Only `manifest.json`, the recipe and the entry files are copied (other files in the
   source are ignored), each re-hashed while copying, into a hidden
   `<collection>/.import-<guid>/`, which is opened again and must have the same identity.
3. The staging directory is renamed to `<collection>/<id>`. An installed pack of that id
   is kept (`AlreadyExists`) unless `replace` is set, also when another import installed
   it in the meantime. With `replace`, an installed pack of the same identity that is
   intact is still kept (`AlreadyExists`; this also stops a pack reached through a link
   from replacing itself), while a damaged one is repaired. Otherwise the old pack is
   moved aside to a hidden `.replaced-<guid>`, replaced and deleted, and moved back if the
   rename fails. A source inside `<collection>/<id>` is always refused (`SourceInvalid`):
   by path before copying, and, for one reached through a link or a differently spelt
   path, by finding its manifest gone once the old pack moved aside, which moves the old
   pack straight back before anything is deleted. If even
   that fails, the result names the hidden directory that still holds it; such a
   directory is never removed automatically. A copy that is in place but does not open
   there is `InstalledButUnreadable`. Staging directories older than a day, left by an
   interrupted import, are removed at the next import.

The source is never modified (the copy reads each file once more and refuses one that
changed size or hash since the check). The installation is not consulted at import: frames are
checked against it when bound, so a pack imported onto a device with a modded or other
game version still works for every frame whose Legacy bytes match, and the rest draw
Legacy with a reason. Archives (`.zip`) are not read; ARC-DEV-003's `hd-import` may
unpack one into a folder first.

## recipe.json

A recipe says what to build and exactly how; it holds no pixels and no paths and may be
kept in the repository under MIT. Examples, all parsed by the tests (examples of methods
ARC-RES-033 measured or the format must hold, **not** defaults or recommendations):

- [`hd-recipe-x4plus-4x-soft.json`](examples/hd-recipe-x4plus-4x-soft.json): the decided
  shape, 4× with soft alpha (network, Real-ESRGAN x4plus at native 4×, colour bleed).
- [`hd-recipe-av3-2x.json`](examples/hd-recipe-av3-2x.json): 2× with the Legacy hard edge.
- [`hd-recipe-hq2x.json`](examples/hd-recipe-hq2x.json): an algorithm, no model.
- [`hd-recipe-generative-4x.json`](examples/hd-recipe-generative-4x.json): the
  provenance a generative pack records; every name and hash in it is a placeholder.

| Field | Rule |
| --- | --- |
| `schema`, `kind` | 1, `arcanum-steamwoven-hd-recipe` |
| `id` | as a pack id |
| `scale` | the pack scale N (accepted: 2, 4) |
| `slices` | 1..64 `{name, map, kinds}`; names unique; `map` an id resolved by the build; `kinds` from `player`, `critter`, `npc`, `scenery`, `container`, `item`, `wall`, `facade`, `tile`, `terrainChunk`, `interface` (ARC-DEV-242), no duplicates; optional `alpha` (the same object as the recipe's) is the slice's own policy and wins over the recipe's for that slice's entries. Which kinds a renderer binds is the contract's rule (characters and objects first) |
| `method` | below |
| `rgb` | optional colour preparation `{edgeExtend: 1..64, fill: "mean"}`: opaque colours bled into the transparent area, the rest the mean colour (stops the dark halo of networks) |
| `alpha` | `{mode: "soft", minIoUPermille: 900..1000}`, `{mode: "legacy"}` or `{mode: "legacySmooth"}` (the last two take no `minIoUPermille`; the gate for `legacySmooth` is fixed at the reader's default) |
| `terrain` | `{chunkSize: 512, padding: 32..256}`, present exactly when a slice selects `terrainChunk` |

`method.kind` decides which pins are required (others are refused):

| Kind | Required | Optional |
| --- | --- | --- |
| `network` | `tool`, `toolVersion`, `toolSha256`, `model`, `licence`, `modelFiles` (1..8 `{name, sha256}`), `nativeScale`, `downscale`, `tileSize` (1..4096, explicit), `tta` (must be `false`) | `parameters` |
| `generative` | `tool`, `toolVersion`, `model`, `licence`, `modelFiles` (base model, control models), `nativeScale`, `downscale` | `toolSha256`, `tileSize`, `adapters` (1..16 `{name, sha256, weight}`: LoRA and similar), `seed` (0..2⁶³−1), `prompt`, `negativePrompt` (1..4,000 UTF-16 code units, any language, line feeds and tabs allowed, no other control characters, so CR LF line ends are refused), `parameters` |
| `algorithm` | `tool`, `toolVersion`, `toolSha256`, `nativeScale`, `downscale` | `licence`, `parameters` |
| `external` | `tool`, `toolVersion`, `nativeScale`, `downscale` | `toolSha256`, `licence`, `parameters` |

- `nativeScale` equals `scale` with `downscale: "none"`, or is a multiple of it with
  `downscale: "box"` (an offline box downscale at build time, e.g. a 4× model for a 2×
  pack; never a runtime filter).
- Network tiles are explicit because automatic tiling depends on GPU memory and changes
  the output; the build refuses an input side larger than the tile (one tile per input),
  and TTA is off (ARC-RES-033). With a terrain section the tile must hold a padded chunk
  (`chunkSize + 2·padding`, at least 576).
- `parameters` are up to 32 `name: "value"` labels (`[a-z][A-Za-z0-9]{0,31}`, values
  printable ASCII without `/` or `\`) for anything else a method needs (sampler, steps,
  denoise, control strength, magnify method).
- Labels and file names never carry paths. Weights and adapters are never in the
  repository; an adapter trained on Arcanum art is derived from it and stays local like
  the pack. A model's own licence is the owner's check (`licence` is recorded, not
  judged).

## Interface art (ARC-DEV-242)

A slice of kind `interface` holds the HD redraws of interface ART (`art/interface/*.art`: panels, strips, buttons, gauges, icons). Its
entries are ordinary `sprite` entries; the key is built by `HdInterfaceKey` so the builder and the viewer agree without the DAT entry
fields:

| Part | Interface ART |
| --- | --- |
| `art.sha256`, `art.view` | SHA-256 and byte length of the **whole file** the content stack's winner holds (interface art is read whole, with trailing bytes allowed) |
| `art.nameSha256` | SHA-256 of the lowercase ASCII path `art/interface/<file>` (`HdInterfaceKey.Path`), not a DAT entry's raw name |
| `group`, `palette` | 0 and 0 (interface art draws palette slot 0) |
| `frame` | the frame number |

Interface art needs the `legacy` or the `legacySmooth` alpha mode (never `soft`: a soft alpha erases single-texel holes, ARC-RES-094).
`legacy` is the exact parent-texel alpha (holes, colour keys and the hard edge are the original's, which shows as 4-pixel steps on
every diagonal edge at 4×); `legacySmooth` (ARC-UI-111, what the importer writes) keeps the interior, holes and colour keys exact and
anti-aliases only the edge. A pack of soft-edged sprites and interface art therefore sets `"alpha":
{"mode":"legacySmooth"}` on the interface slice and keeps its recipe-level soft alpha for the rest; the check follows the entry's slice
(`HdPack.ResolveSprite`, review and integrity alike). A sprite entry may carry `"alpha": "masked"` (ARC-UI-113, the only value; a slice
without the `interface` kind refuses it, and an older reader refuses the pack): its alpha is then checked by the `masked` rule above
and not by the slice's mode. The importer writes it for frames given an `alphaMask` or `ellipse` (below), so a button keeps only its
circle and not the rim and corners of the original frame; every other entry is unchanged. One pack is active at a time, so the interface
slice lives in the pack that also holds the sprites. Example: [`hd-recipe-interface-redraw-4x.json`](examples/hd-recipe-interface-redraw-4x.json) (an interface-only
pack).

**Import of the owner's redraws** (`tools/hd-interface-import`, local files only, nothing derived enters git). The redraws come from
an external generator and are not registered to the original's pixels; the import registers and locks them (the U3 method of
ARC-RES-094, its note is PR #733):

```sh
./arcanum run --kind dotnet -- dotnet run --project tools/hd-interface-import -- --module "<game>/modules/Arcanum.dat" --plan PLAN.json --pack <pack dir> [--dry-run]
```

The plan (`HdInterfacePlan`, kind `arcanum-steamwoven-hd-interface-plan`) lists items; image paths are relative to the plan file:

| Item mode | Image | Steps |
| --- | --- | --- |
| `sheet` | a redraw of a sheet of Legacy frames side by side (`columns` for a grid; `pad` texels of bled border, grey between; any scale, aspect kept or free space around) | fit the whole sheet then each frame (NCC of the blurred luma, coarse to fine, scale and offset per axis), translation refinement against the Legacy silhouette, bicubic resample to the scale, lock (unless `"lock": false`), smooth Legacy alpha |
| `exact` | an image that already covers exactly one frame (a stitched strip) | resample, lock (unless `"lock": false`), smooth Legacy alpha |
| `ready` | one image per tile, already registered at exactly `scale·w × scale·h` | lock (unless `"lock": false`), smooth Legacy alpha |
| `ready` with `"coverage": true` | one coverage image per tile (ARC-DEV-361), grey, exactly `scale·w × scale·h` | none: the image is taken as it is (no lock, no Legacy alpha): red = green = blue = the image's grey, alpha 255 where the grey is above 0 and 0 elsewhere; refused when a texel is not grey or when ink lies farther than one Legacy texel from the Legacy glyph (the `masked` rule, checked at build); the frame is written with `"alpha": "masked"` |

**Font glyph frames (ARC-DEV-361).** The glyphs of a bitmap font (an interface ART of 225 frames, frame *n* is character *n* + 31) are ordinary
`interface` sprite entries keyed like any interface frame, with `"alpha": "masked"` and a coverage image as above: the HD frame of a glyph holds its
coverage in red, green and blue and alpha 255 on every ink texel, 0 elsewhere (a partial alpha would be scaled twice on the premultiplied
font canvas). No new entry field, slice kind or alpha mode exists; an older reader that knows `masked` binds them, one that does not refuses the pack, as
for ARC-UI-113. The viewer takes such an entry for a glyph of the **Coverage** text blend only ([viewer](../rendering/terrain-viewer.md)); a pack
with no font entry draws the Legacy text, and a glyph the pack leaves out (control-character placeholders, a frame whose trace vanished,
the coloured frame 0) draws its Legacy frame. The frames come from `tools/hd_font` ([HD font tracing](../workflow/hd-font.md)); the plan
for the importer is written by `hd_pack.py fonts-plan`, never by hand ([workflow](../workflow/hd-pack.md#traced-fonts-in-the-pack-arc-dev-361)).

**Alpha mask (ARC-UI-113).** Some original overlay buttons (`char_minus`, `char_plus`, `oldchar_*`, `char_htft*`, slider buttons) carry
pieces of the plate under them (a copper rim, black corners). The HD plate is redrawn separately, so the Legacy alpha of such a button
leaves a light square round it. A tile or a `derived` frame of any item mode may therefore name where its alpha comes from instead of
the Legacy alpha, with one of:

| Field | Meaning |
| --- | --- |
| `"alphaMask": "path.png"` | a grey PNG (white = keep, black = clear; a colour file takes the mean of its channels, an alpha channel is ignored; path relative to the plan file). Its size is the Legacy frame's `w × h`, smoothed like `legacySmooth` (a texel is kept at ≥ 128; the edge is the bilinear, one-HD-pixel ramp), or the HD frame's `scale·w × scale·h`, taken as the alpha itself |
| `"ellipse": [cx, cy, rx, ry]` | Legacy pixels from the frame's top-left corner (texel `x` covers `x..x+1`; the whole of a 19×19 frame is `[9.5, 9.5, 9.5, 9.5]`); the edge is anti-aliased at the HD resolution by 8×8 samples per HD pixel |

Giving both is an error. The mask only removes: a texel the Legacy frame leaves transparent stays transparent (an HD mask or an ellipse
is cut to the Legacy-opaque texels; a Legacy-size mask is intersected with them), and a mask that leaves nothing opaque is refused. A
partly covered edge pixel takes the colour of the nearest fully covered pixel within one Legacy texel, so the edge shows the button and
not what the redraw has beyond the circle. A `derived` frame without its own field takes its base tile's. The frame is written with
`"alpha": "masked"`; frames without a field are built exactly as before, so existing plans and packs work unchanged.

The **lock** pulls the redraw's luma, box-reduced to the Legacy grid over the opaque texels, back to the Legacy luma (four rounds)
and its low-frequency chroma to the Legacy chroma (two rounds), by bicubic-enlarged residuals; `coarse` > 0 blurs the corrections over
that many Legacy texels (the "c" variant). A `derived` frame (a button's other frames) is the redrawn tile plus the bicubic-enlarged
Legacy difference `frame − frame 0`, then locked (ARC-UI-117: `"lock": false` on an item of any mode skips the lock of every frame the item builds, derived frames included, so they stay the registered redraw plus the bicubic Legacy difference and keep the redraw's own tone; the default is `true`, and the fit, the Legacy difference and the alpha are the same). The alpha (ARC-UI-111) is the Legacy mask enlarged bilinearly and steepened to about one HD pixel: texels whose 3×3 Legacy
neighbourhood is uniform keep their parent's alpha, an axis-aligned edge stays as crisp as the original's, a staircase diagonal becomes
a one-pixel gradient, single-texel holes keep their centre. HD pixels near the edge whose parent is transparent take the nearest
opaque-parent colour (within one Legacy texel), so the new partial alpha shows the art's edge and not the generator's background. A frame
whose smooth alpha fails the `legacySmooth` check (a tiny all-edge frame) takes the exact Legacy alpha. The tool prints the fit of each
frame (NCC, scale, offset); a weak fit (a sheet whose pictures the generator drew smaller inside their cells) needs `ready` images
registered otherwise. An existing pack gets the slice added or its interface entries replaced (every other entry, the pack's review and
its recipe method stay; the manifest is a new identity), a missing one is created; frame files are written first, the manifest last,
and the pack is opened and verified again. The image reader takes 8- and 16-bit grey, grey+alpha, RGB and RGBA, no palette or interlace.

## Portrait entries (ARC-UI-2865)

The original draws a portrait from one bitmap of its `portrait\` folder: 64 × 64 `<stem>.bmp` (character sheet, save and load list, hover panel, loot and
barter windows, follower frames at 32, identify and repair) and 128 × 128 `<stem>_b.bmp` (character creation and the premade browser; a stem without a large
bitmap draws the small one). The HD pack redraws such a bitmap as an entry of kind `portrait` (4× → 256 × 256 and 512 × 512):

```json
{ "kind": "portrait", "file": "interface/portraits/heroes/portrait-hum1_b-f000.png", "sha256": "<png sha256>", "bytes": 12345,
  "slice": "interface", "portrait": { "path": "portrait/hum1_b.bmp" },
  "legacy": { "width": 128, "height": 128, "rgbaSha256": "<Legacy RGBA sha256>" }, "review": "unreviewed" }
```

Owner decision (the owner's request of 2026-11-19 on #2865, as the coordinator relayed it: portraits go into the HD pack and are drawn from there). The format follows the
rest of the pack, so nothing else changes:

| Part | Rule |
| --- | --- |
| `kind` | `portrait`; the entry's exact properties are the common ones (`file`, `sha256`, `bytes`, `slice`, `legacy`, `review`) and `portrait`. An older reader refuses a pack that holds one (unknown kind), like `masked`. |
| `portrait.path` | the original bitmap's path, lowercase, exactly `portrait/<name>.bmp` with `<name>` of `[a-z0-9_-]` and at most 48 characters. The key of the entry, unique in the pack (`HdPortraitKey`). A name outside the pattern has no HD entry and draws the BMP. |
| `slice` | a slice whose kinds include `interface` (the pack's `interface` slice): a portrait is interface art, so the slice's Legacy alpha rule applies, and no new slice or recipe change is needed. |
| `legacy` | the bitmap's decoded pixels as `Arcanum.Data.Characters.PortraitBitmap` reads them (uncompressed 1/4/8/24-bit Windows bitmap, top row first, alpha 255): width, height and the SHA-256 of the RGBA. The importer and the viewer hash the same decoder's pixels, so a bitmap another game version or a mod replaced is `SourceMismatch` and draws the BMP. |
| HD file | the pack's one PNG profile (8-bit RGBA), exactly `Scale × Legacy size`, **fully opaque** (a portrait has no alpha; the importer writes alpha 255 and the slice's alpha check holds). Taken as it is: no registration or lock. |
| key | carried as an `HdSpriteKey` (hash of the path, `View` 0, group, frame and palette 0), so the pack's lookup, file checks and the device cache serve portraits like frames (`HdPack.ResolveSprite`, `CheckPrepared`, `InterfaceSprites`); `HdEntryKind.Portrait`. The review tool (`HdReview`) lists sprites only and leaves portraits as they are. |

The viewer (`PortraitHd.Texture`) makes every portrait texture from the bitmap's own pixels and keeps its size (64 or 128), so every draw site keeps its on-screen
size and layout; with the HD setting on and an entry for that bitmap it draws the redraw (mipmapped and filtered down for the 32 × 32 follower frames), per file,
otherwise the BMP. Import: `hd_pack.py portraits-plan` writes the plan (kind `arcanum-steamwoven-hd-portrait-plan`) from the delivered redraws, the importer
(`hd-interface-import --plan`) checks each redraw against the bitmap it reads from the game and writes the entries into a staging pack (a portrait plan replaces only the portraits it names; the pack's sprites and other portraits stay), and the update goes in with `apply --add-new`
([workflow](../workflow/hd-pack.md#portraits-arc-ui-2865)).

## Device budget file

`HdDeviceBudget` / `HdDeviceBudgetCodec` read the per-device HD limits that the contract
amendment defines (`user://hd_budget_v1.json` in the game; the host owns storage):

```json
{ "schema": 1, "areaVisibleBytes": 1309649536, "areaRetainedFrames": 512,
  "areaRetainedBytes": 536870912, "worldBytes": 1073741824, "terrainBytes": 805306368 }
```

Every limit is optional and defaults to its ceiling; a value above the ceiling is clamped
to it; 0 turns HD off in that domain; a missing, malformed (negative, non-integer,
unknown or duplicate property) or other-schema file loads as the ceilings. The example
lowers terrain to 48 chunks of 4× (16 MiB each), which suits a 16 GB shared-memory
handheld (Steam Deck class) that also wants room for the game.

## Limits (`HdPackOptions`)

| Option | Default |
| --- | --- |
| `Scales` | 2, 4 |
| `MaxManifestBytes` | 256 MiB (the manifest is parsed in memory) |
| `MaxRecipeBytes` | 1 MiB |
| `MaxEntries` | 400,000 (whole game: 337,513 frames for one palette per ART) |
| `MaxFrameFileBytes` | 256 MiB |
| `MaxHdFramePixels` | 64 M (256 MiB RGBA) |
| `MaxPacksPerCollection` | 64 |
| `MaxReportedProblems` | 100 problems listed; all are counted |

## Not ready / out of scope

- The setting, toggle and diagnostic key are ARC-UI-006's and the HD draw, caches and HUD counter
  ARC-RENDER-014/016's ([viewer](../rendering/terrain-viewer.md#hd-render-path-arc-render-014)); no build or import command (ARC-DEV-003). The before/after
  review (ARC-RENDER-015) reviews sprite entries only; terrain chunk entries are bound in the
  AREA view (ARC-RENDER-016) but not shown by the review tool.
- One active pack; layering several packs is not defined. A whole-game manifest near the
  limits needs about its own size in memory to open; an index split is a later change.
- Archive import, pack signing and any distribution mechanism are out of scope (the pack
  is not distributed).
- Real packs, real installations and other GPUs are not exercised; every test is
  synthetic. Visual quality of any method is ARC-RES-033/038 and ARC-RENDER-015.

Provenance: format and code written for this repository from its own frame keys and the
public PNG specification.
