# Crash Site sector framing: R3 correction

## Scope and status

This note records a bounded, read-only framing experiment for the frozen
Crash Site sector. It corrects the earlier *hypothesis* that the byte after the
4,096 tile words began an object record. It is not a universal sector format
specification, a production loader, an asset resolver, or a static-scene
claim.

The behavior below is an earlier source reading (provenance in git history), cited by the executable address of its function; level F0 (not checked in the executable) unless a ledger row is cited. It is not independent confirmation. The numeric results are an experiment on the one
fingerprinted user-owned input. No game bytes, names, object IDs, or extracted
assets are committed.

## Frozen input and reproduction

| Field | Value |
| --- | --- |
| `START_MAP` selector | `Arcanum1-024-fixed`, `(92958, 82592)` |
| Sector selector | `(1452, 1290)`, packed ID `86570436012` |
| Origin and extent | `(92928, 82560)`, 64 by 64 positions, inclusive ranges `92928..92991` and `82560..82623` |
| Start offset inside sector | `(30, 32)` |
| DAT entry | ordinal 463, flag `0x00000002`, decoded length 47,291 |
| Terminal object count | 317; not a count of static scenery |
| Module fingerprint | 355,938,305 bytes; SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` |

The one-sector extent follows the sector shift of the earlier source reading
(F0), not a claim about all formats or maps. The later
[area inventory](area-inventory.md) and [area contract](../world/area-contract.md)
survey the start-containing townmap component; this note keeps the one-sector
regression, not a census of that area or a production readiness claim.

### Identity chain

The selected map is the observed module's only `START_MAP` record, not an
inference from a directory name; the selected sector is the one containing that
record's start coordinate, and no neighbouring sector is included.

| Link | Observation | Status / confidence |
| --- | --- | --- |
| Community context | The preserved Terra-Arcanum catalogue records a historical Crash Site page and image URL. | reported; context only |
| Map selector | Raw DAT entry `Rules\MapList.mes` is ordinal 14,935, compressed with `0x00000002`, and decodes to 5,454 bytes. Record 5,000 is the observed module's sole `START_MAP`; it names `Arcanum1-024-fixed` and gives `(92958, 82592)`. | experimentally verified locally; high |
| Map properties | Raw entry `maps\Arcanum1-024-fixed\map.prp` is ordinal 8,023; its 24 decoded bytes begin with observed words `2`, `1`, and a little-endian pair `(128000, 128000)`. No semantic labels are assigned. | experimentally verified byte observation; high |
| Sector selector | `sector_id_from_loc` (0x4CFC50) shifts both location coordinates by six and packs the results. Applied to the start coordinate, that gives `(1452, 1290)` and `86570436012`. The matching sector entry is ordinal 463 and decodes to 47,291 bytes. | entry and size experimentally verified; coordinate interpretation F0; medium |
| Framed object window | The final signed little-endian word is `317`. The corrected section order of `sector_load_game` (0x4D1A30, F0) places the object window at `17140..47287`; the bounded v119 delimiter consumes 317 records to the terminal count. | byte experiment plus F0 order; medium |

### Bounded selector and safety limits

The probe requires the observed module size and SHA-256 above, raw-name matches
exactly one selected `.sec` entry, limits both stored and decoded payloads to
1 MiB, and fingerprints the input before and after its in-memory read. Its caps:
light count 1,024, object count 4,096. The selected input has zero serialized
lights; its 4,096 tile words hold 1,041 distinct raw identifiers and a mix of
high-nibble classes, so the probe does **not** claim every word is a directly
resolvable terrain ART ID. No raw IDs or game payloads are recorded in the
repository.

Use the configured SDK and pass the local module path explicitly:

```text
dotnet restore --locked-mode
dotnet build --no-restore
dotnet restore tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --locked-mode
dotnet build tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-restore
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- --self-test
dotnet run --project tools/research/Arcanum.CrashSiteProbe/Arcanum.CrashSiteProbe.csproj --no-build -- \
  --module <explicit-module-path> --prototype-root <explicit-prototype-directory> --resource <explicit-resource-path>
```

The probe opens one exact raw DAT entry through `DatArchive`, caps stored and
decoded entries at 1 MiB, validates checked count-dependent arithmetic, and
compares the module size/SHA-256 before and after its in-memory read. It does
not write the input or extract its payload.

## Source map and loader order

The map is a behavioral attribution from an earlier source reading (F0), cited by function address.

| Stage | Source map | What it establishes for the base `.sec` stream |
| --- | --- | --- |
| Address and base open | `sector_load_game` (0x4D1A30); `sector_id_from_loc` (0x4CFC50); the sector ID packing | The game constructs the sector path from the packed ID, opens the base stream, and uses X/Y shifted by six for the sector address. |
| Difference stream | `sector_load_game` (0x4D1A30), `DIF_HAVE_*` branches | A separate optional `.dif` stream can override light, tile, roof, scripts, metadata, sound, blocks, or objects. It is runtime/save-state input, not bytes in the selected module entry. This probe frames the selected base entry only. |
| Lights | `sector_light_list_load` (0x4F72C0); `light_read` (0x4DDD20); the serialized light record | Reads an LE signed count, then one fixed 48-byte serialized light per item. On this input the count is zero, so no light record bytes are consumed. |
| Tiles | `sector_tile_list_load_internal` (0x4F7CE0) | Reads exactly 4,096 four-byte ART words. |
| Roofs | `sector_roof_list_load_internal` (0x4F8020); the sector roof list record | Reads an `int empty`; only when it is zero does it read 256 four-byte roof ART words. |
| Placeholder and conditional sections | `sector_load_game` (0x4D1A30); `tile_script_list_load` (0x4F65B0); `sector_script_list_load` (0x4F6250); `sector_sound_list_load` (0x4F8150); `sector_block_list_load` (0x4F8250) | Reads a placeholder in the inclusive range `0xAA0000..0xAA0004`. Nonzero loads a count plus 24-byte tile-script entries; `>= 0xAA0002` loads one 12-byte sector script; `>= 0xAA0003` loads three four-byte metadata values and a 12-byte sound record; `>= 0xAA0004` loads 128 four-byte block-mask values. |
| Object list | `objlist_load` (0x4F1350) | Saves the current cursor, seeks to `end - 4`, reads the terminal signed count, seeks back to the saved cursor, reads that many records, then reads and compares the trailing count. |
| Runtime additions after stream decoding | `objlist_load` (0x4F1350); `sector_load_game` (0x4D1A30); `sector_light_list_load` (0x4F72C0) | Object locations are rebased to the sector, postprocessed, and assigned temporary IDs; game loading later folds objects/lights and may precache art. These actions are runtime state and are not additional bytes in the selected base stream. |

Earlier source reading (F0), cited by executable address: the sector
functions 0x4CEF70–0x4D31C0, the sector tile list 0x4F7BA0–0x4F7DF0, the sector
object list 0x4F1150–0x4F2330, the location functions 0x4B8440–0x4B99C0, and the
ART library's identifier functions.

## Separate readiness verdicts

- **Fixed-source terrain-only loader contract: READY.** The frozen sector's
  caller-supplied immutable module and explicit one-source resource archive
  have complete 4,096-cell tile/facade resolution and selected-frame plus
  diagnostic placement evidence. This narrow contract retains the framed
  object suffix as opaque and does not choose sources for callers.
- **Terrain scene: NOT READY.** A production/Godot scene and renderer
  integration do not exist; lighting/palette treatment, camera/dirty-rectangle
  policy, normal-runtime facade behavior, and measured presentation comparison
  remain unestablished.
- **Static-object diagnostic: READY; static scenery scene: NOT READY.** The
  selected-v119 type-3/static predicate and current-ART/prototype chain for
  this framed suffix are established. Baseline, placement, occlusion, and
  static-scene ordering remain unestablished.

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
| Placeholder | 16,392 | 4 | 16,396 | `0xAA0004` | bounded discriminator range (F0) |
| Tile scripts | 16,396 | 196 | 16,592 | 8 | `4 + (8 * 24)` |
| Sector script | 16,592 | 12 | 16,604 | enabled by placeholder | one 12-byte `Script` |
| Metadata and sound | 16,604 | 24 | 16,628 | enabled by placeholder | `3 * 4 + 12` |
| Block mask | 16,628 | 512 | 17,140 | enabled by placeholder | `128 * 4` |
| Object-record window | 17,140 | 30,147 | 47,287 | 317 | terminal count controls bounded delimiter |
| Terminal count | 47,287 | 4 | 47,291 | 317 | `decoded_length - 4` |

With the corrected object start, the first LE signed word is 119. The existing
bounded delimiter consumes exactly 317 version-119 records from
`[17140, 47287)` and reaches the terminal count; the probe retains no record
payloads or names and emits only selected numeric identifiers/metadata.
Prototype/current-ART resolution is
documented separately in `crash-site-objects.md`; it does not change this
framing result.

## Resolution of the earlier `1 / 119` discrepancy

The prior `1` at offset 16,388 was a raw observation but not evidence of a
retail object version. The loader order of `sector_load_game` (earlier source reading, F0) places the roof `empty`
discriminator at that offset. Its observed value is `1`, which selects the
empty-roof branch and consumes no roof ART array. The next placeholder enables
four further base-stream sections before `objlist_load` is reached. Therefore
interpreting offset 16,388 as an object start omitted real, conditional
sections and produced the false `1 / 119` mismatch.

The corrected conclusion is narrow:

- `1` is the selected input's roof-empty discriminator under the F0
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
- resource winner/path, baseline, or draw order; selected object classification,
  current-ART inheritance, and five bounded ART metadata chains are documented
  separately in `crash-site-objects.md`;
- a terrain or static-scene readiness claim.

The source-derived framing is sufficient to prevent treating the suffix as an
unframed block. It is not permission to extend this bounded probe into a
production object parser or renderer.
