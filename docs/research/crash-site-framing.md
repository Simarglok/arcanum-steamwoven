# Crash Site sector framing: R3 correction

## Scope and status

This note records a bounded, read-only framing experiment for the frozen
Crash Site sector. It corrects the earlier *hypothesis* that the byte after the
4,096 tile words began an object record. It is not a universal sector format
specification, a production loader, an asset resolver, or a static-scene
claim.

The source-derived behavior below comes from the single `arcanum-ce` lineage
at immutable commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`; it is not
independent confirmation. The numeric results are an experiment on the one
fingerprinted user-owned input. No game bytes, names, object IDs, or extracted
assets are committed.

## Frozen input and reproduction

| Field | Value |
| --- | --- |
| `START_MAP` selector | `Arcanum1-024-fixed`, `(92958, 82592)` |
| Sector selector | `(1452, 1290)`, packed ID `86570436012` |
| Origin and extent | `(92928, 82560)`, 64 by 64 |
| DAT entry | ordinal 463, flag `0x00000002`, decoded length 47,291 |
| Module fingerprint | 355,938,305 bytes; SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` |

Use the configured SDK and pass the local module path explicitly:

```text
dotnet restore --locked-mode
dotnet build --no-restore
dotnet restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- --self-test
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- --module <explicit-module-path>
```

The probe opens one exact raw DAT entry through `DatArchive`, caps stored and
decoded entries at 1 MiB, validates checked count-dependent arithmetic, and
compares the module size/SHA-256 before and after its in-memory read. It does
not write the input or extract its payload.

## Source map and loader order

The map is a behavioral attribution, not copied implementation. All paths in
this table are at CE commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`.

| Stage | Source map | What it establishes for the base `.sec` stream |
| --- | --- | --- |
| Address and base open | `src/game/sector.c::sector_load_game`; `src/game/sector.c::sector_id_from_loc`; `src/game/sector.h::SECTOR_MAKE` | The game constructs the sector path from the packed ID, opens the base stream, and uses X/Y shifted by six for the sector address. |
| Difference stream | `src/game/sector.c::sector_load_game`, `DIF_HAVE_*` branches | A separate optional `.dif` stream can override light, tile, roof, scripts, metadata, sound, blocks, or objects. It is runtime/save-state input, not bytes in the selected module entry. This probe frames the selected base entry only. |
| Lights | `src/game/sector_light_list.c::sector_light_list_load`; `src/game/light.c::light_read`; `src/game/light.c::LightSerializedData` | Reads an LE signed count, then one fixed 48-byte serialized light per item. On this input the count is zero, so no light record bytes are consumed. |
| Tiles | `src/game/sector_tile_list.c::sector_tile_list_load_internal` | Reads exactly 4,096 four-byte ART words. |
| Roofs | `src/game/sector_roof_list.c::sector_roof_list_load_internal`; `src/game/sector_roof_list.h::SectorRoofList` | Reads an `int empty`; only when it is zero does it read 256 four-byte roof ART words. |
| Placeholder and conditional sections | `src/game/sector.c::sector_load_game`; `src/game/tile_script_list.c::tile_script_list_load`; `src/game/sector_script_list.c::sector_script_list_load`; `src/game/sector_sound_list.c::sector_sound_list_load`; `src/game/sector_block_list.c::sector_block_list_load` | Reads a placeholder in the inclusive range `0xAA0000..0xAA0004`. Nonzero loads a count plus 24-byte tile-script entries; `>= 0xAA0002` loads one 12-byte sector script; `>= 0xAA0003` loads three four-byte metadata values and a 12-byte sound record; `>= 0xAA0004` loads 128 four-byte block-mask values. |
| Object list | `src/game/sector_object_list.c::objlist_load` | Saves the current cursor, seeks to `end - 4`, reads the terminal signed count, seeks back to the saved cursor, reads that many records, then reads and compares the trailing count. |
| Runtime additions after stream decoding | `src/game/sector_object_list.c::objlist_load`; `src/game/sector.c::sector_load_game`; `src/game/sector_light_list.c::sector_light_list_load` | Object locations are rebased to the sector, postprocessed, and assigned temporary IDs; game loading later folds objects/lights and may precache art. These actions are runtime state and are not additional bytes in the selected base stream. |

## Reproduced framing result

Offsets are half-open: `end = start + length`. The probe obtained the following
aggregate-only output from the frozen input. Every adjacent end/start pair
matches; the terminal word ends at decoded length 47,291.

| Section | Start | Length | End | Count/discriminator | Formula or bound |
| --- | ---: | ---: | ---: | --- | --- |
| Light count | 0 | 4 | 4 | 0 | one signed LE `i32`; `0..1024` probe cap |
| Serialized lights | 4 | 0 | 4 | 0 | `light_count * 48` |
| Tile words | 4 | 16,384 | 16,388 | 4,096 | `4096 * 4` |
| Roof section | 16,388 | 4 | 16,392 | `empty = 1` | one LE `i32`; zero would add `256 * 4` bytes |
| Placeholder | 16,392 | 4 | 16,396 | `0xAA0004` | bounded CE discriminator range |
| Tile scripts | 16,396 | 196 | 16,592 | 8 | `4 + (8 * 24)` |
| Sector script | 16,592 | 12 | 16,604 | enabled by placeholder | one 12-byte `Script` |
| Metadata and sound | 16,604 | 24 | 16,628 | enabled by placeholder | `3 * 4 + 12` |
| Block mask | 16,628 | 512 | 17,140 | enabled by placeholder | `128 * 4` |
| Object-record window | 17,140 | 30,147 | 47,287 | 317 | terminal count controls bounded delimiter |
| Terminal count | 47,287 | 4 | 47,291 | 317 | `decoded_length - 4` |

With the corrected object start, the first LE signed word is 119. The existing
bounded delimiter consumes exactly 317 version-119 records from
`[17140, 47287)` and reaches the terminal count; the probe retains no record
payloads, IDs, names, or ART values. It deliberately does **not** request
prototype/current-ART resolution in the selected real-data run, because that
is outside this framing result.

## Resolution of the earlier `1 / 119` discrepancy

The prior `1` at offset 16,388 was a raw observation but not evidence of a
retail object version. The CE-derived loader order places the roof `empty`
discriminator at that offset. Its observed value is `1`, which selects the
empty-roof branch and consumes no roof ART array. The next placeholder enables
four further base-stream sections before `objlist_load` is reached. Therefore
interpreting offset 16,388 as an object start omitted real, conditional
sections and produced the false `1 / 119` mismatch.

The corrected conclusion is narrow:

- `1` is the selected input's roof-empty discriminator under the attributed CE
  framing, not a claimed retail object-record version.
- The base-stream object window is reproducibly `[17140, 47287)`, and its
  first word is 119; the bounded v119 delimiter reaches its terminal count.
- This does not establish a general retail format, all save/difference-file
  behavior, object semantics, static scenery, or resource paths.

## Probe checks and limits

The independent probe self-test covers the fixed prefix, truncation, negative
and capped light/object counts, tile-table bounds, non-tile strict fixture,
roof discriminator, placeholder discriminator, tile-script count cap, all
placeholder-controlled framing branches, the existing version-119 delimiter,
and its variable-width-field bounds. The real run rechecks its input
fingerprint before and after.

Still not established:

- `.dif` selection/precedence or any runtime/save-state content for this run;
- object-to-static-scenery classification, current ART inheritance, resource
  winner/path, ART frame/palette, baseline, or draw order;
- a terrain or static-scene readiness claim.

The source-derived framing is sufficient to prevent treating the suffix as an
unframed block. It is not permission to extend this bounded probe into a
production object parser or renderer.
