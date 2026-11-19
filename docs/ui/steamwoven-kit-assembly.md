# Kit assembly tool: a clean base plus placed details, baked into the HD frame (ARC-DEV-2972)

Status: **tool, 2026-11-19** ([#2972](https://github.com/Simarglok/arcanum-steamwoven/issues/2972), epic
[#2446](https://github.com/Simarglok/arcanum-steamwoven/issues/2446)). `tools/steamwoven_ui/assemble.py`, standard library only
(`pngio.py` reads and writes the PNGs; no imaging package). Requirement touched: [FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007)
(status unchanged). Nothing here holds or produces original art: the pictures it reads and writes are local files.

## What it is for

The kit principle (owner, 2026-11-19, [#2893](https://github.com/Simarglok/arcanum-steamwoven/issues/2893)): Codex never draws a repeated
element twice. A strip or a window background is **assembled**:

1. a clean **base** of the material (wood, paper, leather), generated once from the original with the details masked out;
2. a library of unique **details** (slot socket, brass ring, button plate per size, vial housing, rivets, lens rings, button glyphs over a
   shared plate), each generated once;
3. this tool lays the details on the base at the **original coordinates** (in original units), so ten slots are one socket placed ten
   times and the same button in two windows is one file;
4. the result is **baked into the HD frame of the same original ART**: same frame count and order, same size (`scale` x the original's),
   and the original's alpha geometry by the pack's rule (`legacy`, `legacySmooth` or `masked`, [HD pack format](../formats/hd-pack.md#interface-art-arc-dev-242)),
   so it fits the current pack and the 4:3 composition ([TRD](steamwoven-interface-trd.md#asset-kit)). Runtime layering can come later.

**States** are kept the way the original has them ([states note](steamwoven-interface-states.md#the-originals-state-rules)): a multi-frame
ART gets one output frame per original frame (button: 0 idle, 1 pressed, 2 hover; no disabled frame), and each frame names its state; the
base and every detail may carry a picture per state, so the states of one element come from one sheet or one fixed rule, never from
unrelated generations. The tool takes the pictures it is given; the rule that derives them is the kit's (K1, K2 of the census).

## Use

```sh
python3 -B tools/steamwoven_ui/assemble.py LAYOUT.json --dry-run          # compose, bake, run the alpha gates, print the plan, write nothing
python3 -B tools/steamwoven_ui/assemble.py LAYOUT.json --out DIR [--json]  # write <art>-fNNN.png per frame and <art>.assembly.json
```

Exit 0 and `ASSEMBLE PASS` or `ASSEMBLE DRY-RUN PASS`; exit 1 and `ASSEMBLE FAIL: <reason>` on the first problem, and nothing is written
then. A frame is written only after it passes the alpha gate below. The output names are the pack's frame names
(`<art>-f<NNN>.png`), so the files go to the pack's staging the way the importer's `ready` items do (`tools/hd-interface-import`,
[HD pack workflow](../workflow/hd-pack.md)); this tool does not touch a pack or its manifest. Time: a full 800 x 41 unit strip at x4 takes
a few seconds in plain Python (about 4 s).

## The layout file

JSON, `kind` `arcanum-steamwoven-kit-layout`, `version` 1; unknown fields are refused. Paths are relative to the layout file. Pictures are
PNG, 8 bit, not interlaced.

| Field | Meaning |
| --- | --- |
| `target.part` | optional: the kit part (a `kit.json` id, [#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)) this layout assembles; its row must have the layout's frame count and list every frame's state, and `target.art` must be one of its `source_arts` |
| `target.art` | the original ART's identifier (a name or a number), used for the output file names |
| `target.size` | `[w, h]` of one original frame in original units |
| `target.frames` | the original's frame count (1 to 255) |
| `scale` | the HD frame's scale over the original (the pack scale, 1 to 16; 2 and 4 are the pack's) |
| `master.scale` | pixels per original unit of the base and the details (default `scale`); an integer, a decimal or a ratio such as `"18/5"` for the 3.6 of the master composition ([TRD](steamwoven-interface-trd.md#screen-frame-and-scale)) |
| `master.snap` | the grid of positions in master pixels (default 1; the TRD's master grid is 6) |
| `master.position` | `snap` (default): a position is `units x master.scale` rounded half up to the grid; `exact`: it must already be a whole grid multiple, else the layout is refused |
| `alpha.mode` | `legacy`, `legacySmooth` or `masked` ([the pack's rules](../formats/hd-pack.md#interface-art-arc-dev-242)) |
| `alpha.legacy` | one picture, or one for each frame, whose **alpha channel** is the original frame's alpha at `target.size` (local, made from the player's own data) |
| `frames` | one `{state}` for each original frame, in frame order; omitted: every frame is state `default` |
| `base` | `{image}` or `{images: {state: path}}`: the clean material, exactly `target.size x master.scale` pixels (half up); optional `part` names its kit part |
| `details` | `{id: {image}}` or `{id: {images: {state: path}}}`; an `images` entry named `default` serves the states it does not list; optional `part` names the detail's kit part, and its picture must then have the part's master size when the manifest fixes one (SWI-KIT-3) |
| `placements[]` | `{id, detail, at: [x, y], anchor, frames, states}`: `at` in original units (exact numbers), `anchor` `tl` (default, the detail's top-left corner) or `center`; `frames` (frame numbers) and `states` (names) limit where it is drawn, all frames by default |

## Rules

- **One detail, one picture.** A file is read once. The base and every detail are brought from the master scale to the pack scale **once
  per picture** (the identity when the two scales are equal; otherwise an exact area average of premultiplied pixels, a detail's size is
  `master size x scale / master.scale` rounded half up), and then every placement copies those same pixels at whole output pixels
  (opaque pixels exactly, translucent ones by an integer source-over). A detail placed N times is therefore the same pixels in every
  place at any pair of scales (the tests check five placements at equal scales and at 3.6 to 4). A detail that is a whole number of
  output pixels at the pack scale (a multiple of 9 master px at 3.6 to 4) is not resampled by a fraction.
- **Whole pixels.** Positions are exact (`3.6` is read as 18/5, not a binary float) and become whole master pixels by `master.position` and
  `master.snap` (the TRD's 6 px grid), then output pixels `round(master px x scale / master.scale)` (half up); a placement box in the report
  is in output pixels. With `anchor: center` the snapped point is the detail's centre and the top-left is `centre - w // 2` in output
  pixels, so a centre-anchored detail is on the grid by its centre, not by its corner. A detail that leaves the frame is refused. The base is
  the original frame size at the master scale in whole pixels (half up); the output is exactly `scale x w` by `scale x h`.
- **Alpha.** The colour is the composed canvas; the alpha is the original's by the mode: `legacy` the parent texel's exact alpha;
  `legacySmooth` the original silhouette with a one-pixel anti-aliased edge (the importer's rule: texels whose 3 x 3 neighbourhood is
  uniform keep the parent's alpha, single-texel holes keep their centre, band pixels over a transparent parent take the nearest opaque
  colour), falling back to the exact alpha and the composed colour for a frame that fails its own gate, as the importer does (reported); `masked` the composed alpha,
  cleared farther than one original texel from the silhouette (a plate that must not keep the original's corners). Every written frame
  passes the pack's gate (`check_alpha` is a port of `HdAlphaCheck`: size, the three `legacySmooth` rules and the 900 permille overlap, `legacy` exactness, `masked` reach).
- **Coverage.** A visible pixel (`legacy`, `legacySmooth`) must have base or detail pixels under it; a hole in the base under the
  silhouette is refused and counted, so a frame never ships transparent colour where the pack shows it. An `alpha.legacy` picture with no
  alpha channel is refused (a grey or RGB PNG counts only with a `tRNS` colour key, which the reader applies).
- **Limits.** `target.art` is letters, digits, `_ . -` (at most 80, not `.` or `..`); one frame or master canvas is at most 64 Mpx; numbers
  are at most 32 characters and take no exponent; I/O errors and bad files end as `ASSEMBLE FAIL` and nothing is written.

## Example (synthetic names)

```json
{
  "kind": "arcanum-steamwoven-kit-layout", "version": 1,
  "target": {"art": "Slot_Row", "size": [100, 40], "frames": 1},
  "scale": 4,
  "master": {"scale": "18/5", "snap": 6},
  "alpha": {"mode": "legacySmooth", "legacy": "legacy/slot_row.png"},
  "base": {"image": "kit/wood_row.png"},
  "details": {"socket": {"image": "kit/socket.png"}, "ring": {"image": "kit/ring.png"}},
  "placements": [
    {"id": "s0", "detail": "socket", "at": [5, 5]},
    {"id": "s1", "detail": "socket", "at": [25, 5]},
    {"id": "r0", "detail": "ring", "at": [60, 20], "anchor": "center"}
  ]
}
```

A three-frame button is the same file with `"frames": [{"state": "idle"}, {"state": "pressed"}, {"state": "hover"}]` and `images` maps on the
base and the glyph.

## Checks and limits

- Tests: `python3 -B -m unittest tools.steamwoven_ui.test_assemble` (synthetic pictures only: PNG round trip, exact unit-to-pixel maps,
  a detail placed five times is pixel-identical (equal scales and 3.6 to 4), PNG filters, palette and colour keys decoded from hand-built files, a synthetic three-frame button with states, size and alpha gates for all three modes,
  refusals, the dry run); they also run in the batched `tools/local_dev/run_tests.py` through `test_steamwoven_assemble.py`.
- **The kit manifest** (`tools/steamwoven_ui/kit.json`, [#2454](https://github.com/Simarglok/arcanum-steamwoven/issues/2454)) is the vocabulary:
  a layout that names parts (`target.part`, `base.part`, `details.*.part`) is checked against it (`--manifest PATH`, default the committed
  file): part ids exist, the target's frame count, states and original art match its row, a named detail has its part's master size, and the
  layout works at the kit master of 3.6 px per unit (the manifest's sizes are master pixels). A layout without part names is not checked,
  so a lab layout works without the manifest. The tool does not read ART files: the original frame size and alpha come from the layout and
  from local pictures. A later slice can generate layouts from manifest rows.
- The PNG reader and writer here (`pngio.py`) and the shared module of the bake tool ([#2460](https://github.com/Simarglok/arcanum-steamwoven/issues/2460))
  are two copies of the same standard library code; whichever merges second takes the first's module (follow-up issue
  [#2991](https://github.com/Simarglok/arcanum-steamwoven/issues/2991)).
- Masking the details out of the original to make the clean base, generating the pictures and the review are the owner's and Codex's
  steps ([pipeline](steamwoven-interface-trd.md#generation-pipeline)); the outputs are local and never go to git (no original or derived art in git).
