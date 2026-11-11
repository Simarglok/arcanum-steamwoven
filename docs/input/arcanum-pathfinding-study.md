# Arcanum local-pathfinding study — bounded reading of the original's path functions

Date: 2026-09-29. Scope: ARC-GPAD-003 study only. This document is a reading of the original's local pathfinding functions at the cited addresses, evidence level F0 (not checked in the executable); the verification ledger lists none of them. It is not an observation of the running original or an assertion of original-runtime compatibility.

## Scope of the reading

- Functions read: the local path entry (0x41F3C0), the direct routine (0x41F840), the fallback grid search (0x41F9F0), the distance estimate (0x4200C0), the alternate branch (0x420E30), the NPC budget setters (0x421AC0, 0x421AE0), the world-map path entry (0x4207D0), plus bounded call/traversal context: the animation path setup (0x425930, 0x426320), the animation traversal helpers (0x425760, 0x4257E0), the object traversal test (0x43FD70) and the tile blocking test (0x4D7110).
- No executable, Windows environment, original installation or game data was run or uploaded for this study.

Names below for the path-request record and its fields (`PathCreateInfo`, `max_rotations`, `field_24`, `PATH_FLAG_0x1000`, `path_limit`, `path_time_limit`) are descriptive labels for the reading, not recovered original symbols. Several of the functions read have no meaningful names or documentation, the large animation module especially, so the reading is corroborating evidence with explicit gaps, not a complete recovery of original behaviour.

## Local path entry points

The path request (`PathCreateInfo`) carries an object, source/destination locations, `max_rotations`, rotation output, flags, and a scratch field. The local path entry (0x41F3C0) rejects equal locations, selects special flag paths, validates a destination in some flag cases, tries a direct route first, then selects a fallback search when that direct route returns zero.

The direct routine (0x41F840) rejects source-to-target distance over 32, advances toward the destination one rotation at a time, and tests tiles, object traversal, windows, spotted traps, and optional fire. A direct success returns the count; direct failure records the number of completed steps in `field_24`. This supports the narrow conclusion that the original attempts straight local progress before its local fallback (F0). It does not prove an original click-to-move contract.

The local path entry (0x41F3C0) returns at most eight rotations for this normal entry point after the chosen routine returns. That is a returned-segment cap, not evidence of a global eight-cell movement limit: callers pass their own `max_rotations` storage/budget. For example, animation setup (0x425930) obtains that value through the path-budget helper (0x426320) and writes the returned count into the run's path maximum; other bounded callers use storage sizes or a 100-rotation value. The reading therefore supports "normal path-entry result is capped to 8" but not "a character can only move eight cells."

## Fallback search facts

The fallback search (0x41F9F0) implements a bounded local grid search:

- It derives a 64×64 origin window and rejects deltas greater than 32 on either axis. Its cost/backtrack tables each have 4096 entries.
- It scans all 4096 table slots to choose a positive-cost open node with minimal accumulated-plus-estimated cost. The estimate comes from the distance function (0x4200C0), which is `10 * max(dx, dy)`. This is function-level structure, not a claim that it is a complete or optimal algorithm.
- It considers all eight rotations. Traversal can discard tile-blocked or object-costed neighbors; diagonal object blockers receive an additional restriction in this representation. Base cost is 10, with visible additions for spotted traps, windows, optional fire, lighting differences, and direction changes.
- NPC calls have aggregate guards: a per-second counter compared with `path_limit` (initialized to 10) and elapsed work compared with `path_time_limit` (initialized to 250); the limit setter (0x421AC0) accepts 1–34 and the time-limit setter (0x421AE0) accepts 1–999. These are implementation limits read from the functions (F0), not measured original-game budgets.

Traversal is not a single terrain boolean. The direct routine (0x41F840) invokes the tile blocking test (0x4D7110) plus the object traversal test (0x43FD70). The latter delegates to traversal-cost calculation and treats either a cost result or blocking object as blocked. The tile blocking test checks tile-block data, facade walkability, and art-name blocking, with a special second-argument path. An animation helper (0x425760, 0x4257E0) also checks tile blocking followed by object traversal flags derived from object capabilities. This corroborates that local traversal depends on context/flags and object state, not merely an unqualified tile grid.

## Explicit gaps and out-of-scope branches

The local path entry (0x41F3C0) chooses the alternate branch (0x420E30, called with 300) when `PATH_FLAG_0x1000` is present, but this reading has no recovered body for that function. The exact behavior of that branch is therefore unknown; it is a material limit on algorithm recovery.

The world-map path entry (0x4207D0) and a separate 16×16 sector search sit beside the local functions. They are world-map routing, not the local character path implementation studied here, and are outside ARC-GPAD-003 implementation scope.

No claim is made about undocumented flags, all animation call paths, fidelity of this reading to the running original, target interpretation, original terrain/collision data, movement animation timing, or path quality under real game content.

ARC-RES-013 separately records a [grid movement runtime](../world/grid-movement-runtime.md)
and [motion evidence](../research/grid-movement-evidence.md). Its directed
entry/commit, smooth-return/finish and timing law are Project policy, not conclusions
from this route search. The normal move trace also qualifies universal Stand-at-end
claims: the inspected ordinary update restores the previous animation. Neither study
establishes original-runtime movement or turns the returned-segment cap into a global limit.

## Comparison with the synthetic Gamepad Lab

ARC-GPAD-002/003's default is direct semantic desired direction; its optional Nearby Goal mode stores a 96 px actor-relative synthetic point, refreshes only under explicit cadence/hysteresis conditions, and stops on the procedural occupancy callback. It does not reproduce the original's locations, flags, eight rotations, traversal costs, NPC budgets, or fallback search.

The study reinforces the conservative boundary already chosen: a local implementation must separate direct intent, bounded collision/traversal evidence, retry behavior, and any future route search. It does not justify broad A*, navmesh work, altered diagonal traversal, obstacle smoothing, or a claim that a faster open set produces a better route.

No safe movement-algorithm change supported by the reading was identified for this card. The current Nearby Goal behavior already avoids redundant replanning after a blocked held intent by latching until neutral/material redirection, and its deterministic stored-target cadence is documented/tested separately. That is a bounded efficiency/stability property of the synthetic lab, not an improvement on the original's path quality.

## Finite conclusion

The functions read show a direct local attempt followed by a bounded 64×64, eight-direction cost search in one normal fallback branch, with substantial traversal context and a normal returned-segment cap of eight (F0). They also include an alternate local branch whose body this reading did not recover and a separate world-map algorithm. The appropriate action is to retain the synthetic lab's direct-intent default and bounded Nearby Goal comparison without importing or expanding navigation behavior. Original-runtime compatibility remains unknown.
