# HD pack and recipe format (ARC-DATA-014)

Status: **format, Godot-free reader, import and device budget, with synthetic tests.**
Code: [`src/Arcanum.Data/Hd`](../../src/Arcanum.Data/Hd); tests:
[`tests/Arcanum.Data.Tests/Hd`](../../tests/Arcanum.Data.Tests/Hd). Nothing here draws
an HD frame: binding textures, caches, the HUD and the toggle are ARC-RENDER-014 and
ARC-UI-006, the build tool (`./arcanum hd-build` / `hd-import`) is ARC-DEV-003, terrain
chunks are ARC-RENDER-016. The rules this format serves are in the
[HD presentation contract](../rendering/hd-presentation-contract.md) (ARC-RENDER-013, with
its ARC-DATA-014 amendment).

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
link or reparse point is refused when read, so a pack cannot reach outside itself.

Where packs live (discovery, `HdPack.Discover`): a **collection** is a folder whose
subdirectories are packs (the game's own pack folder, e.g. `user://hd-packs/`, or
`local/hd-packs/` in a checkout, which git ignores); a **pack location** is one pack
directory the user chose anywhere (an external drive, a shared folder). Hidden
subdirectories (leading `.`) are skipped; at most 64 packs per collection are opened
(option). Each listed pack carries its own open result, so one broken pack hides
nothing. Choosing the active pack is ARC-UI-006; one pack is active at a time.

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
| `review` | `unreviewed` / `approved` / `rejected` (pack); per entry the same (ARC-RENDER-015 writes them) |
| `recipe` | relative path and SHA-256 of the recipe file, which must parse |
| `build` | printable ASCII labels, 1..128 characters, no `/` or `\` (so no local path can hide in them); `createdUtc` exactly `yyyy-MM-ddTHH:mm:ssZ`; `gpu` is recorded because outputs depend on it (ARC-RES-033) |
| `entries` | 0..400,000 (option); each `file` used once; each sprite key once |

**Identity.** `HdPackIdentity` = (`id`, `scale`, SHA-256 of the `manifest.json` bytes).
The manifest pins every file by hash, so the identity covers the whole pack. A rebuild,
even of the same recipe on another GPU, and any review mark are a new identity, and HD
caches keyed by it reload. A copy (import) keeps the identity.

### Sprite entries: the portable frame key

An HD frame is bound to the **install-independent part** of the ARC-RENDER-013 frame key
(`HdSpriteKey`):

| Part | From `AreaFrameKey` / `AreaVisualRecord` |
| --- | --- |
| `art.sha256` | `PayloadSha256`: SHA-256 of the whole ART entry after resource precedence (ARC-DATA-011) |
| `art.view` | `ViewLength`: decoded bytes the frame is read from (132..2³¹−1) |
| `group` | physical group (0..7) |
| `frame` | frame (0..4095) |
| `palette` | **effective** palette slot (0..3): an HD frame is built from the RGBA of its own slot (ARC-RES-033) |

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

### Terrain chunk entries (reserved)

```json
{ "kind": "terrainChunk", "file": "chunks/4_1.png", "sha256": "…", "bytes": 1, "slice": "ground",
  "chunk": { "x": 4, "y": 1, "size": 512, "padding": 32 },
  "tiles": ["<Legacy RGBA sha256 of each composed tile/facade frame, in plan order>"],
  "legacy": { "width": 512, "height": 512, "rgbaSha256": "<composed level-0 chunk>" },
  "review": "unreviewed" }
```

Terrain is upscaled as composed 512-pixel chunks with neighbour padding, never tile by
tile (ARC-RES-033). The kind is **reserved**: it is parsed, validated (size equal to the
recipe's `terrain.chunkSize`, padding equal to its `terrain.padding` ≥ 32, legacy size
equal to the chunk, 1..4,096 tile hashes, slice selecting `terrainChunk`) and
integrity-checked, but never bound; how a chunk is keyed to a chart and drawn is the
ARC-RENDER-016 amendment.

## Frame files

One PNG per entry in exactly one profile (`HdPng`): 8-bit RGBA (colour type 6), deflate,
filter method 0 with any of the five row filters, no interlace, IDAT chunks consecutive,
IEND last with nothing after it, every CRC correct. Ancillary chunks are skipped; any
other critical chunk, PLTE included, is unsupported. The decoder refuses an image above
the pixel limit before inflating it and requires the inflated data to be exactly
`h·(1 + 4w)` bytes. `HdPng.EncodeRgba` writes this profile deterministically.

PNG and not raw RGBA, because the whole game at 2× is ~21 GB raw for one palette per ART
(ARC-RES-033), 16× the source at 4×; packs can be partial and per slice, so early packs
hold only the frame keys a map uses.

## Binding a frame on this device

`HdPack.ResolveSprite(query, legacyRgba)` takes the portable key, the ART name hash and
this installation's decoded Legacy frame (its pixels must hash to the query; a
contradiction is a caller error and throws). It returns `Hd` with exactly
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

There is **no comparison with reference HD hashes**: cross-GPU bit-exactness of the
upscalers is not established (ARC-RES-033), so packs are checked for integrity and
against the installation, not for reproducing someone else's pixels.

## Import of a pack built elsewhere

`HdPackImport.Import(source, collection, replace)`:

1. The source must open, and `VerifyIntegrity` must find every entry file present, of the
   declared size, with the declared SHA-256 and a PNG header of exactly `N·w × N·h`.
   Any problem refuses the import (`IntegrityFailed` with counts per reason); a damaged
   copy is never imported partly.
2. Only `manifest.json`, the recipe and the entry files are copied (other files in the
   source are ignored), each re-hashed while copying, into a hidden
   `<collection>/.import-<guid>/`, which is opened again and must have the same identity.
3. The staging directory is renamed to `<collection>/<id>`. An existing pack of that id
   is kept (`AlreadyExists`) unless `replace` is set; then it is moved aside, replaced and
   deleted, and restored if the rename fails. A pack cannot replace itself.

The source is never modified. The installation is not consulted at import: frames are
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
| `slices` | 1..64 `{name, map, kinds}`; names unique; `map` an id resolved by the build; `kinds` from `player`, `critter`, `npc`, `scenery`, `container`, `item`, `wall`, `facade`, `tile`, `terrainChunk`, no duplicates. Which kinds a renderer binds is the contract's rule (characters and objects first) |
| `method` | below |
| `rgb` | optional colour preparation `{edgeExtend: 1..64, fill: "mean"}`: opaque colours bled into the transparent area, the rest the mean colour (stops the dark halo of networks) |
| `alpha` | `{mode: "soft", minIoUPermille: 900..1000}` or `{mode: "legacy"}` |
| `terrain` | `{chunkSize: 512, padding: 32..256}`, present exactly when a slice selects `terrainChunk` |

`method.kind` decides which pins are required (others are refused):

| Kind | Required | Optional |
| --- | --- | --- |
| `network` | `tool`, `toolVersion`, `toolSha256`, `model`, `licence`, `modelFiles` (1..8 `{name, sha256}`), `nativeScale`, `downscale`, `tileSize` (1..4096, explicit), `tta` (must be `false`) | `parameters` |
| `generative` | `tool`, `toolVersion`, `model`, `licence`, `modelFiles` (base model, control models), `nativeScale`, `downscale` | `toolSha256`, `tileSize`, `adapters` (1..16 `{name, sha256, weight}`: LoRA and similar), `seed` (0..2⁶³−1), `prompt`, `negativePrompt` (1..4,000 characters, any language, no control characters), `parameters` |
| `algorithm` | `tool`, `toolVersion`, `toolSha256`, `nativeScale`, `downscale` | `licence`, `parameters` |
| `external` | `tool`, `toolVersion`, `nativeScale`, `downscale` | `toolSha256`, `licence`, `parameters` |

- `nativeScale` equals `scale` with `downscale: "none"`, or is a multiple of it with
  `downscale: "box"` (an offline box downscale at build time, e.g. a 4× model for a 2×
  pack; never a runtime filter).
- Network tiles are explicit because automatic tiling depends on GPU memory and changes
  the output; the build refuses an input side larger than the tile (one tile per input),
  and TTA is off (ARC-RES-033).
- `parameters` are up to 32 `name: "value"` labels (`[a-z][A-Za-z0-9]{0,31}`, values
  printable ASCII without `/` or `\`) for anything else a method needs (sampler, steps,
  denoise, control strength, magnify method).
- Labels and file names never carry paths. Weights and adapters are never in the
  repository; an adapter trained on Arcanum art is derived from it and stays local like
  the pack. A model's own licence is the owner's check (`licence` is recorded, not
  judged).

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

- No HD draw, texture cache, HUD counter, setting, toggle or diagnostic key
  (ARC-RENDER-014, ARC-UI-006); no build or import command (ARC-DEV-003); no before/after
  review tool (ARC-RENDER-015). Terrain chunk entries are never bound (ARC-RENDER-016).
- One active pack; layering several packs is not defined. A whole-game manifest near the
  limits needs about its own size in memory to open; an index split is a later change.
- Archive import, pack signing and any distribution mechanism are out of scope (the pack
  is not distributed).
- Real packs, real installations and other GPUs are not exercised; every test is
  synthetic. Visual quality of any method is ARC-RES-033/038 and ARC-RENDER-015.

Provenance: format and code written for this repository from its own frame keys and the
public PNG specification; no CE code or structure is used.
