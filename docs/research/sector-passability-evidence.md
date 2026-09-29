# Selected Crash Site sector passability evidence (ARC-RES-011)

## Status and boundary

This is a bounded research record for the fixed 64×64 Crash Site **base-sector** profile. It is not a collision implementation, a route finder, a generalized map reader, a `.dif`/PATCH/loose-file precedence rule, a dynamic-object system, or original-runtime validation.

The source study is limited to one attributed lineage: [`alexbatalov/arcanum-ce` commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd). Its Sustainable Use License remains applicable to that source. No CE code was copied or mechanically translated; this project does not make a clean-room, independent-corroboration, or original-executable-parity claim.

The candidate implements a fixed-profile, aggregate-only `--passability` mode in `Arcanum.CrashSiteProbe`. It decodes only the already-framed 512-byte block section and raw facade bit 0. It intentionally leaves tile-name, static-object spatial, state, actor, diagonal, and cross-sector dependencies visible as unknown or unsupported.

## Immutable selected input

The selected base entry and coordinate chain are established by [Crash Site slice](crash-site-slice.md) and [framing](crash-site-framing.md). No original payload, installation path, names/MES text, graphics, screenshots, object payload, or mask/grid is retained here.

| Input / selector | Fixed identity or limit |
| --- | --- |
| Module | 355,938,305 bytes; SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` |
| Base sector selector | `maps\\Arcanum1-024-fixed\\86570436012.sec`; ordinal 463; compressed flag `0x00000002`; decoded length 47,291 |
| Sector / origin / extent | `(1452,1290)` / `(92928,82560)` / 64×64 |
| Explicit resource | 147,991,122 bytes; SHA-256 `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` |
| Prototype boundary | Six referenced prototype files; aggregate SHA-256 `00d5610fc295b84a8dd355d843b0307d0dff5f73e6d353ed25c81667882d65d6` |

The base framing positions the block section at `16628..17140`, exactly 128 little-endian 32-bit words (512 bytes), after the placeholder-controlled sections. The independent probe rejects a missing, wrong-size, truncated, overlapping, or arithmetically invalid framing. A base entry cannot establish `.dif` or runtime state; those requests are outside this profile.

## Source trace and exact mapping

| Claim | Immutable CE evidence | Classification | Consequence |
| --- | --- | --- | --- |
| Serialized shape | [`SectorBlockList.mask[128]`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_block_list.h#L6-L9) and [`sector_block_list_load`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_block_list.c#L34-L42) read 128 `uint32_t` values. | CE-derived serialized base-sector fact | The probe admits exactly 512 bytes, never an inferred variable mask. |
| Index and polarity | [`tileblock_is_tile_blocked`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_block.c#L311-L327) tests `mask[tile / 32] & (1u << (tile % 32))`; its setter sets that bit for `blocked` and clears it otherwise ([L335-L351](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile_block.c#L335-L351)). | CE-derived serialized base-sector fact | Word `ordinal / 32`, least-significant-bit-first `ordinal % 32`; set means explicit blocked in this lineage. |
| Local ordinal | [`TILE_MAKE`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.h#L22-L24) is `x | (y << 6)`; [`tile_id_from_loc`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L133-L143) masks local X/Y to 0..63 and [`tile_art_id_at`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L146-L163) indexes the sector tile array. | CE-derived coordinate fact; selected sector origin is measured | `ordinal = localX | (localY << 6)`, `world = (92928 + localX,82560 + localY)`. |
| Tile blocking branches | [`tile_is_blocking`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L166-L190) checks explicit block first, facade walkability next, then `a_name_tile_is_blocking`; its `a2` branch suppresses the final tile-name result. | CE runtime/caller-context plus mixed data | A raw tile word alone is not an Open result. |
| Facade branch | The same function returns the inverse of [`tig_art_facade_id_walkable_get`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/src/art.c#L2473-L2531); existing terrain evidence maps this to raw facade bit 0. | CE-derived raw word fact | For the terrain component, bit 0 clear is an unconditional CE-lineage facade blocker; bit 0 set is not an Open result. The probe does not infer art geometry. |
| Tile-name branch | [`a_name_tile_is_blocking`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/a_name.c#L749-L778) consumes `TF_BLOCK`, whose parser accepts `b` in name-table flags ([L470-L482](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/a_name.c#L470-L482)). | ART/MES/name-derived fact | The passability mode deliberately does not parse or emit MES text; every tile-name result remains Unknown. |

The selected facade anchors retain their existing raw-word evidence: `(27,30)` / 1947 / `0xB6000804`, `(27,31)` / 2011 / `0xB6000802`, and `(27,32)` / 2075 / `0xB6000801`. The raw facade walkability interpretation is a source-attributed input fact, not a visual/ART-size rule.

## Object and step dependency matrix

| Dependency | CE source and admitted serialized slot | Classification | Selected-profile status |
| --- | --- | --- | --- |
| Source and destination object lookup | [`object_calc_traversal_cost_func`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2978-L3027) recursively checks diagonal shoulders and lists walls/portals at the source; it then calls `location_in_dir` and lists destination objects ([L3029-L3282](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3029-L3282)). | CE runtime/context value | Unknown: a serialized local location is not a proven spatial contribution or footprint. |
| Common object flags | `OBJ_F_FLAGS` follows current ART, location, offsets, and 14 intervening common fields in [`obj.h`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.h#L10-L32): v119 zero-based slot 18. Existing bounded loader evidence resolves it direct-or-prototype only. [`OF_NO_BLOCK`](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_flags.h#L11-L25) affects destination non-wall/non-portal objects at object.c L3245-L3254. | Serialized prototype/base fact when resolved; CE runtime spatial use | Unknown: the five examples and the 317-record census do not prove all traversal contributors. Existing type-3/non-`OF_DYNAMIC` remains a census predicate, not collision. |
| Spell flags | `OBJ_F_SPELL_FLAGS` is v119 zero-based common slot 19. `OSF_PASSWALLED` affects walls ([object.c L3083-L3092](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3083-L3092)); `OSF_POLYMORPHED` changes traversal flags ([anim.c L4751-L4763](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4751-L4763)). | CE runtime/context value | Unadmitted and Unknown. |
| Current ART and actor capability | Current ART is v119 slot 0; the traversal code reads wall/portal art rotation and pieces, portal state, and `ai_attempt_open_portal` ([object.c L3034-L3134](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3034-L3134)). Animation derives portal/window flags from actor capabilities. | Serialized fact plus ART and CE runtime/context values | Unknown. No ART dimensions, alpha, hotspot, sprite pixels, or opacity is used as collision evidence. |
| Direct step caller | The direct path branch invokes `tile_is_blocking`, object traversal, windows, spotted traps, and optional fire ([path.c L239-L280](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L239-L280)). | CE runtime/context value | A finite cell result cannot stand in for a step result. |
| Diagonal | The fallback calls all eight rotations and uses a special diagonal blocker condition ([path.c L425-L457](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L425-L457)); object traversal recursively probes shoulders. | CE behavior with unresolved actor/object context | Unknown; no project diagonal policy is inferred. |
| `PATH_FLAG_0x1000` | It selects `sub_420E30` ([path.c L121-L130](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L121-L130)), which is explicitly TODO/incomplete ([L1008-L1012](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L1008-L1012)). | Unknown/unverified original behavior | Unavailable, never defaulted. |

## Probe evidence and finite cases

`--self-test` has independently authored boundary expectations for `(0,0)`, `(31,0)`, `(32,0)`, `(63,0)`, `(0,1)`, and `(63,63)`; set/clear bits across 32-bit boundaries; invalid coordinates/ordinals; exact/511-byte/absent/wrong-size/overflow framing; and controlled facade bit-0 class cases. Existing probe coverage remains responsible for v119 direct/prototype slot-18-versus-slot-19 sentinels, envelope truncation/caps, and resource/prototype failures.

One fixed-profile read-only run passed every before/after identity check for the module, explicit resource, and six referenced prototypes. The aggregate was `0` serialized block bits set / `4096` clear; `87` facade bit-0 set / `976` clear; `3033` tile-name rules unknown; `317` bounded static-census records; and `4096` combined occupancy results unknown. A zero set-bit count is an observed aggregate, not evidence that clear means open.

The finite result contains terrain `(0,0)` / ordinal 0 / world `(92928,82560)`, `(63,63)` / 4095 / `(92991,82623)`, and `(30,32)` / 2078 / `(92958,82592)`: each has a clear base bit and an Unknown combined occupancy result. It also contains the three required facade anchors: `(27,30)` / 1947 and `(27,31)` / 2011 are bit-0 clear and therefore `Supported + Blocked` only for the terrain component; `(27,32)` / 2075 is bit-0 set and remains Unknown rather than Open. All three combined occupancies remain `Unknown + null` because the static and context frontiers are unresolved. The existing bounded source-indexed object cases remain `0:(50,0)`, `1:(5,1)`, `2:(6,1)`, `3:(6,1)`, and `4:(44,1)`; their local locations are not collision footprints and no object disposition is emitted.

The run also records a cardinal `(30,32)->(31,32)` result as `Unknown + null`, a diagonal `(30,32)->(31,31)` result as `Unknown + null`, and the sector-edge `(63,63)->(64,63)` result as `Unsupported + null`. Because no set-bit case exists in the exact input, no artificial set-case ordinal is emitted. The fixed anchors exhibit both facade bit values, so no extra disagreement ordinal is needed. For any future profile with an otherwise unrepresented branch, the probe adds at most its lexicographically first matching ordinal. It does not emit a full grid, raw mask, payload, name, MES text, ART data, or local path.

## Integrity and negative findings

The implementation rejects selector/fingerprint mismatch, wrong compressed flag, decoded-size cap violation, malformed framing, missing/excess block bytes, invalid local coordinates, and overflow. It preserves the original probe CLI unchanged unless `--passability` is explicit.

The evidence supports a **terrain component** fact: a set block bit is an explicit blocking condition in the attributed source. It does not support a combined static occupancy or traversal verdict: clear masks still require facade/name, spatial object, state, actor, and movement-context evidence. The fixed base entry also cannot answer `.dif`, PATCH, loose-file, dynamic-object, neighboring-sector, or original-runtime questions.
