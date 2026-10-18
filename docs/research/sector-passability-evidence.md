# Selected Crash Site sector passability evidence (ARC-RES-011)

## Status and boundary

This is a bounded research record for the fixed 64×64 Crash Site **base-sector** profile. It is not a collision implementation, a route finder, a generalized map reader, a `.dif`/PATCH/loose-file precedence rule, a dynamic-object system, or original-runtime validation.

The source study is an earlier source reading (provenance in git history), cited by the executable address of its function; level F0 (not checked in the executable) unless a ledger row is cited. This record does not make an independent-corroboration or original-executable-parity claim.

The candidate implements a fixed-profile, aggregate-only `--passability` mode in `Arcanum.CrashSiteProbe`. It decodes the already-framed 512-byte block section and raw facade bit 0, and now reports the separate bounded ordinary-static-scenery component recorded in [ARC-RES-012](static-obstruction-evidence.md). Tile-name, dynamic/state, actor, diagonal, edge, precedence, and cross-sector dependencies remain visible as unknown or unsupported; the combined result is still not passability.

The separate later [ARC-RES-014 cardinal mode](cardinal-traversal-evidence.md) uses
production terrain/name inspections and a full contributor census in a declared
isolated context. Its four admitted directional rows do not rewrite this historical
aggregate-only mode or certify general occupancy/installed-game passability.

## Immutable selected input

The selected base entry and coordinate chain are established by [Crash Site slice](crash-site-slice.md) and [framing](crash-site-framing.md). No original payload, installation path, names/MES text, graphics, screenshots, object payload, or mask/grid is retained here.

| Input / selector | Fixed identity or limit |
| --- | --- |
| Module | 355,938,305 bytes; SHA-256 `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` |
| Base sector selector | `maps\\Arcanum1-024-fixed\\86570436012.sec`; ordinal 463; compressed flag `0x00000002`; decoded length 47,291 |
| Sector / origin / extent | `(1452,1290)` / `(92928,82560)` / 64×64 |
| Explicit resource | 147,991,122 bytes; SHA-256 `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` |
| Prototype boundary | Six fixed ID/length/SHA-256 identities, verified before parsing and again after use; aggregate over sorted `ID:length:SHA-256` tuples: `684e04f305cef5e0c89caabee852728b33bfb85d4a7e6b2f509505815c080342` |

The probe binds the selector to ordinal 463, flag `0x00000002`, and exactly 47,291 decoded bytes before it reads the entry. The base framing positions the block section at `16628..17140`, exactly 128 little-endian 32-bit words (512 bytes), after the placeholder-controlled sections. The independent probe rejects a missing, wrong-size, truncated, overlapping, or arithmetically invalid framing. A base entry cannot establish `.dif` or runtime state; those requests are outside this profile.

## Source trace and exact mapping

| Claim | Earlier source reading (F0), by function address | Classification | Consequence |
| --- | --- | --- | --- |
| Serialized shape | The sector block list's `mask[128]` and `sector_block_list_load` (0x4F8250) read 128 `uint32_t` values. | Serialized base-sector fact (F0) | The probe admits exactly 512 bytes, never an inferred variable mask. |
| Index and polarity | `tileblock_is_tile_blocked` (0x4BB410) tests `mask[tile / 32] & (1u << (tile % 32))`; its setter sets that bit for `blocked` and clears it otherwise (0x4BB490). | Serialized base-sector fact (F0) | Word `ordinal / 32`, least-significant-bit-first `ordinal % 32`; set means explicit blocked in this reading. |
| Local ordinal | `TILE_MAKE` is `x | (y << 6)`; `tile_id_from_loc` (0x4D7090) masks local X/Y to 0..63 and `tile_art_id_at` (0x4D70B0) indexes the sector tile array. | Coordinate fact (F0); selected sector origin is measured | `ordinal = localX | (localY << 6)`, `world = (92928 + localX,82560 + localY)`. |
| Tile blocking branches | `tile_is_blocking` (0x4D7110) checks explicit block first, facade walkability next, then `a_name_tile_is_blocking`; its `a2` branch suppresses the final tile-name result. | Runtime/caller-context (F0) plus mixed data | A raw tile word alone is not an Open result. |
| Facade branch | The same function returns the inverse of `tig_art_facade_id_walkable_get`; existing terrain evidence maps this to raw facade bit 0. | Raw word fact (F0) | For the terrain component, bit 0 clear is an unconditional facade blocker in this reading; bit 0 set is not an Open result. The probe does not infer art geometry. |
| Tile-name branch | `a_name_tile_is_blocking` (0x4EBAB0, 0x4EBAD0) consumes `TF_BLOCK`, whose parser accepts `b` in name-table flags (0x4EB2E0). | ART/MES/name-derived fact | The passability mode deliberately does not parse or emit MES text; every tile-name result remains Unknown. |

The selected facade anchors retain their existing raw-word evidence: `(27,30)` / 1947 / `0xB6000804`, `(27,31)` / 2011 / `0xB6000802`, and `(27,32)` / 2075 / `0xB6000801`. The raw facade walkability interpretation is a source-attributed input fact, not a visual/ART-size rule.

## Object and step dependency matrix

`object_traversal_is_blocked` (0x43FD70) is not an occupancy predicate: it returns true when `object_calc_traversal_cost` (0x43FDC0) yields a nonzero cost **or** returns a non-null blocking object. Cost-only sound/window/projectile outcomes therefore have the same boolean result at that caller while retaining distinct source semantics. The rows below are an earlier source reading (F0), not original-runtime validation.

| Dependency | Earlier source reading (F0) and serialized/default status | Classification | Selected-profile status |
| --- | --- | --- | --- |
| Admitted v119 facts | The object field numbering puts current ART first: slot 0 is retained direct or from one matching prototype default; slot 1 location and slots 2/3 offsets are retained only when direct; `OBJ_F_FLAGS` enum 19 is zero-based slot 18 and retained direct or prototype-default. | Serialized base-sector/prototype fact | Only slot 0 and slot 18 participate in the bounded direct/default resolution. Location/offsets identify finite examples only, not footprint or traversal contribution. |
| Unadmitted serialized fields | `OBJ_F_SPELL_FLAGS` is zero-based common slot 19, but this probe does not retain it; fields after slot 18 are safely skipped only to delimit v119 records. No direct/prototype default is inferred for any unretained traversal field. | Unknown/unverified original behavior | `OSF_PASSWALLED` and `OSF_POLYMORPHED` remain Unknown, never supplied as default facts. |
| Source wall/portal list and orientation | `object_calc_traversal_cost_func` (0x43FE00) lists source `OBJ_TM_WALL | OBJ_TM_PORTAL`, normalizes ART rotation to odd, and compares it to direction. Diagonal rotations recursively inspect shoulders (same function); a failed directional location returns cost 1. | Runtime/context value (F0) | Unknown: the selected serialized local locations do not prove list membership, orientation, or shoulder contribution. |
| Wall/window and passwall branches | Source and destination wall branches inspect ART wall piece and `OBJ_TRAVERSAL_WINDOW_BLOCKS`/original rotation; certain pieces set a blocking object or mark a window, while `OBJ_TRAVERSAL_SOUND` adds cost 2. Other wall pieces block unless slot-19 `OSF_PASSWALLED` is set (source and destination branches of 0x43FE00). | Runtime/context value (F0) | Unknown: wall piece, traversal flags, and spell state are not admitted. |
| Portal state and actor attempt | Portal branches depend on damaged ART state, `portal_is_open`, `OBJ_TRAVERSAL_SOUND` (cost 1), `OBJ_TRAVERSAL_PORTAL_BLOCKS`, projectile flag, non-null actor, and `ai_attempt_open_portal` (0x4AEB70) result (source and destination branches of 0x43FE00; ledger LV2 and LV3 check the open attempt and the portal legs in the executable). | Runtime/context value | Unknown: open/damaged/AI-open state and actor are not serialized base facts here. |
| Projectile, sight, and cover outcomes | When a wall/portal or destination obstacle is found under `OBJ_TRAVERSAL_PROJECTILE`, `OF_SHOOT_THROUGH` can avoid a blocking object; `OF_SEE_THROUGH` plus `OF_PROVIDES_COVER` adds cost 20, otherwise it adds cost 50 (source and destination branches of 0x43FE00). | Runtime/context value (F0) | Unknown: those slot-18 bits, object type, and projectile context are not admitted traversal verdicts. |
| Destination object masks and critter state | The destination list uses mask `0x3801F`; wall/portal orientation is reversed. For non-wall/non-portal objects, `OBJ_TRAVERSAL_SKIP_OBJECTS | OBJ_TRAVERSAL_SOUND`, critter type, `OBJ_TRAVERSAL_IGNORE_CRITTERS`, and `critter_is_dead` control a blocker. Other objects use slot-18 `OF_NO_BLOCK`, with projectile/`OF_SHOOT_THROUGH` exception (destination branch of 0x43FE00). | Runtime/context value (F0) | Unknown: type-3/non-`OF_DYNAMIC` is only the existing census predicate, not collision. Critter death and list/spatial membership are runtime/context values. |
| Direct caller context | The direct path branch invokes tile, object, window, spotted-trap, and optional-fire checks (path function 0x41F840); animation derives traversal flags including polymorph/actor capability branches (animation functions 0x425760, 0x4257E0). | Runtime/context value (F0) | A finite cell result cannot stand in for a step result. |
| Diagonal and incomplete flag branch | The fallback uses all eight rotations and a special diagonal condition (path search 0x41F9F0); `PATH_FLAG_0x1000` selects explicitly incomplete `sub_420E30` (0x41F3C0, 0x420E30). | Unknown/unverified original behavior | Diagonal is Unknown and `PATH_FLAG_0x1000` is unavailable; neither is defaulted. |

## Probe evidence and finite cases

`--self-test` has independently authored expectations for `(0,0)`, `(31,0)`, `(32,0)`, `(63,0)`, `(0,1)`, and `(63,63)`; set **and clear** behavior across 32-bit boundaries; invalid X/Y/ordinal and checked world-coordinate overflow; exact 512-byte, 511-byte, absent, wrong-size, and overflow mask framing. The pre-existing sector-framing suite separately covers placeholder selection, overlap/truncation, and count caps. Controlled facade bit-0, tile-name clear/blocked/unknown/unsupported, and unsupported-class cases execute against the same component-assessment seam that emits the aggregate.

The same seam has synthetic composition assertions for no blocker, one blocker, unresolved dependency, unsupported state, and multiple same-cell blockers; it also checks all four cardinal directions, all four diagonals (which remain `Unknown + null` even when synthetic components are otherwise open), same-cell, non-adjacent, and sector-edge requests. Existing probe coverage remains responsible for v119 direct/prototype slot-18-versus-slot-19 sentinels, envelope truncation/caps, and missing/ambiguous/truncated prototype failures.

One fixed-profile read-only run passed every before/after identity check for the module, explicit resource, and six referenced prototypes. The aggregate was `0` serialized block bits set / `4096` clear; `87` facade bit-0 set / `976` clear; `3033` tile-name rules unknown; `317` bounded static-census records; and `4096` combined occupancy results unknown. A zero set-bit count is an observed aggregate, not evidence that clear means open.

The finite result contains terrain `(0,0)` / ordinal 0 / world `(92928,82560)`, `(63,63)` / 4095 / `(92991,82623)`, and `(30,32)` / 2078 / `(92958,82592)`: each has a clear base bit and an Unknown combined occupancy result. It also contains the three required facade anchors: `(27,30)` / 1947 and `(27,31)` / 2011 are bit-0 clear and therefore `Supported + Blocked` only for the terrain component; `(27,32)` / 2075 is bit-0 set and remains Unknown rather than Open. All three combined occupancies remain `Unknown + null` because the static and context frontiers are unresolved. The existing bounded source-indexed object cases remain `0:(50,0)`, `1:(5,1)`, `2:(6,1)`, `3:(6,1)`, and `4:(44,1)`; their local locations are not collision footprints and no object disposition is emitted.

The run also records a cardinal `(30,32)->(31,32)` result as `Unknown + null`, a diagonal `(30,32)->(31,31)` result as `Unknown + null`, and the sector-edge `(63,63)->(64,63)` result as `Unsupported + null`. Because no set-bit case exists in the exact input, no artificial set-case ordinal is emitted. The fixed anchors exhibit both facade bit values, so no extra disagreement ordinal is needed. For any future profile with an otherwise unrepresented branch, the probe adds at most its lexicographically first matching ordinal. It does not emit a full grid, raw mask, payload, name, MES text, ART data, or local path.

## Integrity and negative findings

The implementation rejects selector/fingerprint mismatch, wrong ordinal/compressed flag/decoded length, a substituted six-prototype ID/length/SHA manifest before parsing, mutation of every fixed input after use, malformed framing, missing/excess block bytes, invalid local coordinates, and overflow. It preserves the original probe CLI unchanged unless `--passability` is explicit.

The evidence supports a **terrain component** fact: a set block bit is an explicit blocking condition in the earlier source reading (F0). ARC-RES-012 additionally supports only the separately named `ordinary-static-scenery/base-sector/cardinal-normal-critter` destination-cell component; see its [evidence](static-obstruction-evidence.md) and [contract](../world/static-obstruction-contract.md). Clear masks and static NoBlock still do not support combined occupancy or traversal: facade/name, dynamic/state, actor, edge, movement-context, and precedence evidence remain required. The fixed base entry also cannot answer `.dif`, PATCH, loose-file, generalized dynamic-object, neighboring-sector, or original-runtime questions.
