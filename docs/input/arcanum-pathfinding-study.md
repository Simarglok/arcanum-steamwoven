# Arcanum CE local-pathfinding study — bounded source reading

Date: 2026-09-29. Scope: ARC-GPAD-003 source study only. This document describes a pinned Community Edition (CE) source snapshot, not an observation of the original Arcanum executable or an assertion of original-runtime compatibility.

## Source and provenance

- Repository: https://github.com/alexbatalov/arcanum-ce
- Inspected revision: `a7ff41b300ef712f0e7d088183a3d08957110cdd` (checked out detached on 2026-09-29).
- Relevant files: `src/game/path.c`, `src/game/path.h`, bounded call/traversal context in `src/game/anim.c`, `src/game/object.c`, and `src/game/tile.c`.
- License note: the pinned repository labels `LICENSE.md` “Sustainable Use License,” not MIT. No CE code was copied, translated, or incorporated. This is attribution and behavioral research, not a clean-room claim.
- No CE executable, Windows environment, original installation, or game data was run or uploaded.

The CE README itself calls the project a pre-release/technology preview and says much of its source has cryptic names or little documentation, specifically including the very large `anim.c`. That makes the source useful corroborating evidence with explicit gaps, not a complete recovery of original behavior.

## Local path entry points

`PathCreateInfo` carries an object, source/destination locations, `max_rotations`, rotation output, flags, and a scratch field (`path.h:22-30`). `path_make` is the local entry point (`path.c:63-138`): it rejects equal locations, selects special flag paths, validates a destination in some flag cases, tries a direct route first, then selects a fallback search when that direct route returns zero.

The direct routine `sub_41F840` rejects source-to-target distance over 32 (`path.c:231-233`), advances toward the destination one rotation at a time, and tests tiles, object traversal, windows, spotted traps, and optional fire (`path.c:239-280`). A direct success returns the count; direct failure records the number of completed steps in `field_24` (`path.c:283-292`). This supports the narrow conclusion that CE attempts straight local progress before its local fallback. It does not prove an original click-to-move contract.

`path_make` returns at most eight rotations for this normal entry point after the chosen routine returns (`path.c:133-137`). That is a returned-segment cap, not evidence of a global eight-cell movement limit: callers pass their own `max_rotations` storage/budget. For example, animation setup obtains that value through `sub_426320` and writes the returned count into `run_info->path.max` (`anim.c:4833-4858`); other bounded callers use storage sizes or a 100-rotation value. The CE source therefore supports “normal `path_make` result is capped to 8” but not “a character can only move eight cells.”

## Fallback search facts

`sub_41F9F0` implements a bounded local grid search:

- It derives a 64×64 origin window and rejects deltas greater than 32 on either axis (`path.c:337-373`). Its cost/backtrack tables each have 4096 entries (`path.c:39-43`).
- It scans all 4096 table slots to choose a positive-cost open node with minimal accumulated-plus-estimated cost (`path.c:380-401`). The estimate comes from `path_dist`, which is `10 * max(dx, dy)` (`path.c:558-578`). This is source-level structure, not a claim that it is a complete or optimal original algorithm.
- It considers all eight rotations (`path.c:417-430`). Traversal can discard tile-blocked or object-costed neighbors; diagonal object blockers receive an additional restriction in this representation (`path.c:437-457`). Base cost is 10, with source-visible additions for spotted traps, windows, optional fire, lighting differences, and direction changes (`path.c:462-515`).
- NPC calls have source-visible aggregate guards: a per-second counter compared with `path_limit` (initialized to 10) and elapsed work compared with `path_time_limit` (initialized to 250); `path_set_limit` accepts 1–34 and `path_set_time_limit` accepts 1–999 (`path.c:314-335`, `path.c:1015-1035`). These are CE-source implementation limits, not measured original-game budgets.

Traversal is not a single terrain boolean. The direct path routine invokes `tile_is_blocking` plus `object_traversal_is_blocked` (`path.c:251-275`). In CE, the latter delegates to traversal-cost calculation and treats either a cost result or blocking object as blocked (`object.c:2962-2969`). `tile_is_blocking` checks tile-block data, facade walkability, and art-name blocking, with a special second-argument path (`tile.c:166-190`). An animation helper also checks tile blocking followed by object traversal flags derived from object capabilities (`anim.c:4737-4763`). This corroborates that local traversal depends on context/flags and object state, not merely an unqualified tile grid.

## Explicit gaps and out-of-scope branches

`path_make` chooses `sub_420E30(..., 300)` when `PATH_FLAG_0x1000` is present (`path.c:121-130`), but this pinned function consists solely of `// TODO: Incomplete.` (`path.c:1008-1012`). The exact behavior of that branch is therefore unavailable from this revision; it is a material limit on algorithm recovery.

The same file contains `wmap_path_make` and a separate 16×16 sector search (`path.c:782-1005`). It is world-map routing, not the local character path implementation studied here, and is outside ARC-GPAD-003 implementation scope.

No claim is made about undocumented flags, all animation call paths, source-to-original fidelity, target interpretation, original terrain/collision data, movement animation timing, or path quality under real game content.

## Comparison with the synthetic Gamepad Lab

ARC-GPAD-002/003's default is direct semantic desired direction; its optional Nearby Goal mode stores a 96 px actor-relative synthetic point, refreshes only under explicit cadence/hysteresis conditions, and stops on the procedural occupancy callback. It neither runs CE code nor reproduces CE locations, flags, eight rotations, traversal costs, NPC budgets, or the CE fallback search.

The source study reinforces the conservative boundary already chosen: a local implementation must separate direct intent, bounded collision/traversal evidence, retry behavior, and any future route search. It does not justify broad A*, navmesh work, altered diagonal traversal, obstacle smoothing, or a claim that a faster open set produces a better route.

No safe source-supported movement-algorithm change was identified for this card. The current Nearby Goal behavior already avoids redundant replanning after a blocked held intent by latching until neutral/material redirection, and its deterministic stored-target cadence is documented/tested separately. That is a bounded efficiency/stability property of the synthetic lab, not a CE path-quality improvement.

## Finite conclusion

The pinned CE snapshot demonstrates a direct local attempt followed by a bounded 64×64, eight-direction cost search in one normal fallback branch, with substantial traversal context and a normal returned-segment cap of eight. It also contains an explicitly incomplete alternate local branch and a separate world-map algorithm. The appropriate action is to retain the synthetic lab's direct-intent default and bounded Nearby Goal comparison without importing or expanding navigation behavior. Original-runtime compatibility remains unknown.
