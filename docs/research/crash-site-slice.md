# Crash Site: frozen initial sector

## Scope

The later [area inventory](crash-site-area-inventory.md) and [area contract](../world/crash-site-area-contract.md) survey the start-containing townmap component, separate boundary context and normal-game mobile sources, and record potential cave endpoints. This page retains the historical one-sector regression, not a census of that expanded area or a production readiness claim.

ARC-RES-003 is limited to one reproducible Crash Site research candidate. It
is neither a production map loader nor evidence of gameplay, asset precedence,
scene parity, or rendering behavior.

The selected map is the observed module's only `START_MAP` record, not an
inference from a directory name. The selected sector is the sector containing
that record's start coordinate. No neighbouring sector is included.

## Identity chain

| Link | Observation | Status / confidence |
| --- | --- | --- |
| Community context | The preserved Terra-Arcanum catalogue records a historical Crash Site page and image URL. | reported; context only |
| Map selector | Raw DAT entry `Rules\MapList.mes` is ordinal 14,935, compressed with `0x00000002`, and decodes to 5,454 bytes. Record 5,000 is the observed module's sole `START_MAP`; it names `Arcanum1-024-fixed` and gives `(92958, 82592)`. | experimentally verified locally; high |
| Map properties | Raw entry `maps\Arcanum1-024-fixed\map.prp` is ordinal 8,023; its 24 decoded bytes begin with observed words `2`, `1`, and a little-endian pair `(128000, 128000)`. No semantic labels are assigned. | experimentally verified byte observation; high |
| Sector selector | `sector_id_from_loc` (0x4CFC50) shifts both location coordinates by six and packs the results. Applied to the start coordinate, that gives `(1452, 1290)` and `86570436012`. The matching sector entry is ordinal 463 and decodes to 47,291 bytes. | entry and size experimentally verified; coordinate interpretation F0 (earlier source reading, not checked in the executable); medium |
| Framed object window | The final signed little-endian word is `317`. The corrected section order of `sector_load_game` (0x4D1A30; earlier source reading, F0) places the object window at `17140..47287`; the bounded v119 delimiter consumes 317 records to the terminal count. | byte experiment plus F0 order; medium |

The facts here come from an earlier source reading (provenance in git history), cited by the executable address of its function; level F0 (not checked in the executable) unless a ledger row is cited; they are not
independent corroboration.

## Frozen input

| Field | Value |
| --- | --- |
| Map name | `Arcanum1-024-fixed` |
| Start location | `(92958, 82592)` |
| Sector coordinate | `(1452, 1290)` |
| Packed sector identifier | `86570436012` |
| Local origin | `(92928, 82560)` |
| Extent | 64 by 64 positions, inclusive ranges `92928..92991` and `82560..82623` |
| Start offset inside sector | `(30, 32)` |
| Sector entry | module DAT ordinal 463, flag `0x00000002`, decoded length 47,291 |
| Terminal object count | 317; not a count of static scenery |

The one-sector extent follows the sector shift of the earlier source reading (F0), not a claim about
all formats or maps.

## Bounded selector and safety limits

The read-only probe accepts one explicit module path and requires the observed
module size `355938305` and SHA-256
`8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f`.
It opens the archive through `Arcanum.Data.DatArchive`, raw-name matches exactly
one selected `.sec` entry, limits both stored and decoded payloads to 1 MiB,
and fingerprints the input before and after its in-memory read.

It frames only this small sector subset, in the order of `sector_load_game`:

1. signed little-endian light count, capped at 1,024;
2. `count * 48` bytes of opaque serialized lights, skipped only after checked
   arithmetic and bounds validation;
3. 4,096 four-byte ART identifiers;
4. the roof discriminator and the placeholder-controlled script,
   metadata/sound, and block sections, with checked count/length bounds; and
5. the terminal signed little-endian object count, capped at 4,096, plus the
   bounded v119 delimiter over the derived object window.

The selected real input had zero serialized lights. The 4,096 values contain
1,041 distinct raw identifiers and a mix of high-nibble classes, so this probe
does **not** claim that every word is a directly resolvable terrain ART ID.
It records no raw IDs or game payloads in the repository.

## Corrected framing and bounded resource status

The selected sector contains a measured object window rather than an
undifferentiated suffix. With zero lights, the tile table ends at decoded offset
16,388; its LE word `1` is the roof `empty` discriminator (F0), not an
object version. The subsequent placeholder `0xAA0004` enables tile scripts,
sector script, metadata/sound, and block-mask sections. The object window is
therefore `17140..47287` (30,147 bytes), followed by the terminal count `317`.
The bounded v119 delimiter reaches that terminal word for 317 records.

This framing result alone did not identify any object as static scenery or
prove its current ART, baseline, or order. ARC-RES-007 subsequently records a
separate selected-v119 object/current-ART result in
`crash-site-objects.md`; no general object parser is authorized by either
result.

The two observed terrain classes are no longer blocked at raw-ID decomposition:
the companion investigation records their MES callback lookup (F0),
one-source exact ART selection, physical frame selection, coverage failures,
and projected anchors in `crash-site-terrain-resolution.md`. It remains
unacceptable to promote a raw word to a filename, invent multi-source
precedence, or call the unresolved whole sector ready.

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
  separate ARC-RES-007 result establishes a selected-v119 type-3/static
  predicate and current-ART/prototype chain for this framed suffix. Baseline,
  placement, occlusion, and static-scene ordering remain unestablished.

## Explicitly not established

- object baseline, placement, occlusion, or static-scene ordering rule;
- a complete terrain scene or multi-source precedence policy; R2 establishes
  diagnostic tile/editor-facade footprint/crop/flip/order only, while lighting,
  camera policy, normal-game facade rendering, and scene parity remain open;
- patch, loose-file, or archive conflict precedence;
- object baselines, visual offsets, mirroring, height, occlusion, or measured
  scene parity;
- lighting, walls, roofs, gameplay entities, or a playable scene.

Those gaps prevent declaring a terrain scene or static Godot scene ready. They
do not contradict the narrower fixed-source terrain-only loader contract above;
they are research requirements, not defaults a later implementation may
silently guess.

## Sources

- User-owned module archive: local, read-only evidence above.
- Existing project reader: `src/Arcanum.Data/DatArchive.cs`.
- Earlier source reading (F0), cited by executable address: the sector functions
  0x4CEF70–0x4D31C0, the sector tile list 0x4F7BA0–0x4F7DF0, the sector object
  list 0x4F1150–0x4F2330, the location functions 0x4B8440–0x4B99C0, and the ART
  library's identifier functions; earlier provenance is in git history.
- Community catalogue: toolkit metadata record; contextual only.

No original bytes, extracted assets, name inventory, or user installation path
is committed.
