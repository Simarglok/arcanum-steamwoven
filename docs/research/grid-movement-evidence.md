# Grid movement evidence — ARC-RES-013

## Result and classification boundary

**Project policy/status**: one source-specification candidate, not production
movement or real-data admission. The [contract](../world/grid-movement-contract.md)
implements ratified P1–P8 as documentation; its independently authored
[synthetic worksheet](../../tools/research/grid_movement_probe.py) exercises
exact/rational examples. CE source is research lineage, not copied implementation.
Original runtime, source-to-original fidelity and UX comfort are **NOT_VERIFIED**.

Subsequent [ARC-RES-014 cardinal evidence](cardinal-traversal-evidence.md) establishes
a bounded source/data-only pair in a declared isolated context. It does not rerun
or reclassify this historical motion worksheet, move an original actor, or establish
runtime permits; the [readiness frontier](../world/grid-movement-contract.md#real-traversal-readiness-frontier)
links that narrow new admission separately.

All claims use **Original/source-confirmed**, **Project policy**, **Hypothesis** or
**Unknown**. Original/source-confirmed has an explicit subtype: **CE-source**
means inspected source text only; **historical original metadata** means earlier
identity-bound observations, not a new read/runtime observation. Review must
independently inspect sources and recompute tables, not infer a pass from this
record or from source acquisition alone.

## Acquisition, pin and license

**Original/source-confirmed (CE-source/acquisition)**:

- Public repository: https://github.com/alexbatalov/arcanum-ce
- Literal revision: `a7ff41b300ef712f0e7d088183a3d08957110cdd`.
- Acquired 2026-10-02, completed at `2026-10-02T15:15:21Z`, as a depth-one Git
  fetch of that exact object, followed by detached checkout and `rev-parse HEAD`.
  The returned head equalled the literal pin; no branch-tip substitution.
- First bounded authorized toolkit name discovery returned no CE match; broader
  file discovery and its repository/extra-repository manifests list other archives,
  not this pin. That does **not** prove exhaustive archive absence. Used the
  authorized public alternative, not CE-derived ArcNET as an independent oracle.
- Retained one ignored task-local source cache for review. No CE build/executable,
  archived tool, Windows environment or original executable was run. No original
  input, payload, image or game capture was uploaded or copied.
- Directly read [LICENSE.md](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/LICENSE.md#L1-L15):
  title **Sustainable Use License**, Version 1.0. The limitations are not an MIT
  grant. SHA-256 `3c260f537a0c7c3d140e5524551e5743c836cca8d819216a6934aa4669d9abec`.
  No CE code/comments/structures were copied or mechanically translated into the
  candidate. Attribution is not a clean-room, legal independence or MIT-compatible
  reuse claim. The probe is a synthetic policy worksheet, not a CE port.

Reproduction (use caller-selected ignored storage, retain license, do not run CE):

```sh
git init "$CE_CACHE"
git -C "$CE_CACHE" fetch --depth=1 https://github.com/alexbatalov/arcanum-ce.git a7ff41b300ef712f0e7d088183a3d08957110cdd
git -C "$CE_CACHE" checkout --detach FETCH_HEAD
git -C "$CE_CACHE" rev-parse HEAD
```

No cleanup authority is implied. Capacity preflight observed 181 GiB available
on the worktree volume; this bounded source retrieval needed no export, SDK
install or shared-cache deletion. Paths/session ownership and exact-head command
logs are operational evidence in the task handoff, not committed personal paths.

### Inspected file integrity manifest

**Original/source-confirmed (CE-source/acquisition)**. SHA-256 is over the fetched
file bytes, not rendered web pages; line ranges below refer to this exact pin.
Only bounded relevant ranges were inspected in the large animation/object files.

| Relative CE file | SHA-256 |
| --- | --- |
| LICENSE.md | `3c260f537a0c7c3d140e5524551e5743c836cca8d819216a6934aa4669d9abec` |
| src/game/location.h | `838bdf5df9c7d0e3ae9d798f8ae13bd6eb28d5208a977920eeeac9c095554a0d` |
| src/game/location.c | `c4d86ac41edad8f74389d6f8e4aa87a9585c533a5e6b865c0c4e17f6ce7736f4` |
| src/game/anim.c | `f8e623e88f310e3f05b017de987d131a7bc8330b53a2164d14045636ab219dd3` |
| src/game/object.c | `22b2d0dbcbd15a2e79248fabcb554c37941859b3ee8f81e1d6a0fe8cbaadac11` |
| src/game/path.c | `e8b5747ebbed19c9bda1b91b488cbebf45a76c0716b49a508773ef75282ad7a5` |
| src/game/tile.c | `6d8003273b93282d07ece3298263aa6b14be6d9e31275679a349267d7049ea5c` |
| first_party/tig/include/tig/art.h | `69e98f97e0c65f48b9473ca4c8669de967b4d5e8d6f04aceb801843f27323bb5` |

## Bounded normal-path source trace

Every S row is **Original/source-confirmed (CE-source)**, conditional on the
specified branch. Not original-runtime proof. The source retains cryptic symbols,
explicit gaps and implementation caveats. No full combat/movement engine survey
was performed. The table is prose specification evidence, not reusable CE code.

| ID / claim | Exact immutable definition and caller | Conditions and limits |
| --- | --- | --- |
| S1 Location representation, projector, offset normalization | [location.h:38–55](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.h#L38-L55) packs/gets two 32-bit halves. [location_xy:135–143](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L135-L143), [location_at:147–199](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L147-L199), [location_normalize:503–529](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L503-L529); normal update calls normalize in S5. | Isometric projector uses origin and 40/20 factors; normalize constructs projected+offset+(40,20), converts to a new map cell and rebases offsets only if location changes. `location_at` uses integer arithmetic/limits, not P1's continuous directed tie law. Top-down differs. |
| S2 Eight neighbors and bounds | [location_in_dir:369–417](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L369-L417); normal begin caller [anim.c:10454–10469](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10454-L10469). [location_rot:294–365](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/location.c#L294-L365) itself carries an original-code-difference note. | Rotations 0..7 give (-1,-1),(-1,0),(-1,+1),(0,+1),(+1,+1),(+1,0),(+1,-1),(0,-1). Neighbor result rejects negative/out-of-limit X/Y. Source mapping is not compass proof or diagnostic MapFacing. |
| S3 Normal move goal and begin | [anim_goal_node_move_to_tile:349–371](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L349-L371) refers to AG_ATTEMPT_MOVE. [attempt_move:403–425](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L403-L425) binds begin `sub_42E9B0` and update `sub_4305D0`. [begin:10365–10421](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10365-L10421), [rotation/next-cell/pause:10454–10500](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10454-L10500). | Ordinary non-shortcut route checks null/flags, stoned/paralyzed/stunned states and action points, retains previous ART, selects animation, sets current ART rotation from first path rotation, determines next location and sets motion flags. [10423–10451](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10423-L10451) is a separate shortcut, not continuous interpolation proof. |
| S4 Frame-driven offset progression | Normal update [11471–11485](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11471-L11485) calls `object_inc_current_aid`, then reads offsets. [object_inc_current_aid:2763–2791](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2763-L2791) increments the frame and adds the new frame's offset to instance fields; [sub_4423E0:4301–4332](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4301-L4332) updates spatial/offset state. | Not-destroyed object, changed ART frame, successful frame-data lookup and nonzero frame offset. This is motion-state accumulation, not a second offset addition in `object_get_rect`. S1 normalizes afterward. No continuous linear interpolation, distance per frame or source speed inferred. |
| S5 Entry and subsequent segment completion are distinct | [normal update:11371–11413](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11371-L11413), [normalization branch:11578–11614](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11578-L11614). With path flag 0x02, [11490–11509](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11490-L11509) calls [sub_42EF60:10560–10595](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10560-L10595) and conditionally advances path index. Location write route [sub_43E770:2224–2294](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2224-L2294). | Current ART Stand exits; normal path without 0x02 normalizes/rebases and sets it on change. With 0x02, signed offset tests and extra_target_tile guard subsequent progression. Floating, traps, slipping and network context complicate it; [11587–11590](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11587-L11590) is explicitly incomplete host work. This supports separated location/progress concepts, NOT exact P1/P2 equality or original cancellation. |
| S6 Action selection and completion, including conflict | [sub_42EDC0:10503–10537](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10503-L10537); [normal completion/turn:11536–11559](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11536-L11559); [sub_430490:11314–11318](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11314-L11318). Separate [sub_42F140:10649–10707](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10649-L10707) sets Stand at its terminal path; [cleanup sub_430F20:11641–11667](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L11641-L11667). [art enum:234–241](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/first_party/tig/include/tig/art.h#L234-L241). | Begin can select StealthWalk, Walk or Run given concealment/training/goal/run/fatigue. Normal terminal path restores previous animation and frame0 and zeroes offsets (floating adjustment separate); turn between rotations resets offsets and changes ART rotation. Stand=0, Walk=1 in enum. Cleanup computes local Stand ART but does not call object_set_current_aid; do not claim effective Stand from it. The prior placement evidence's compact “Stand when movement completes” is not universal for the newly traced normal route. Preserve that historical attribution and this qualification, not an erased conflict. |
| S7 Scheduler, not raw ART speed | Begin [10483–10494](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10483-L10494), [sub_42EE90:10539–10557](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L10539-L10557). Helper [sub_437990:15343–15399](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L15343-L15399), [15487–15499](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L15487-L15499); scheduler caller [anim_timeevent_process:3664–3690](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L3664-L3690), [delay:3772–3776](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L3772-L3776), [reschedule:3825–3849](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L3825-L3849). | Normal callback's delay sentinel -2 uses pause_time; rescheduling converts delay and multiplies its millisecond field by 8, with catch-up branch. Begin reads ART FPS/fallback, then stat-derived pause clamps 30..800 and shrunk may double it. Helper depends on art/body/action/stat and has raw-FPS-return branches. Do not report a measured wall-clock step duration or multiply selected FPS into gameplay speed from this trace. |
| S8 Directional traversal is contextual, not cell boolean | Normal [sub_425760/sub_4257E0:4737–4763](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4737-L4763), [tile_is_blocking:166–190](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/tile.c#L166-L190); [object_traversal_is_blocked/cost:2962–3027](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2962-L3027), [source list/wall/portal:3029–3111](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3029-L3111), [destination contributions:3152–3256](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3152-L3256). | Tile first, actor-derived polymorph/portal/window flags, nonzero traversal cost OR blocking object. Even rotations recurse through both shoulder routes; source walls/portals and reversed destination orientation differ. Doors may invoke AI-open, critters depend on death/flags. Pure project permits cannot be aliases for side-effecting source routines. |
| S9 Local route output is not step execution/global range | [path_make:116–137](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L116-L137); [sub_420E30:1008–1012](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/path.c#L1008-L1012); bounded caller [anim.c:4833–4858](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L4833-L4858). | Normal return is capped to eight rotations, caller supplies its own storage via sub_426320. PATH_FLAG_0x1000 chooses explicitly incomplete alternate function. Retain the [pathfinding study](../input/arcanum-pathfinding-study.md) distinction: segment cap is not a global route-distance limit. |

## Source versus policy versus unknowns

| Claim / classification | Supported conclusion / excluded inference |
| --- | --- |
| Original/source-confirmed (CE-source), S1/S5 | Location and offsets change separately and may be rebased before segment completion; not original-runtime exact boundary proof. |
| Project policy, P1–P3 | Normalized signed integer centers, linear interpolation, directed half-way equality, one diagonal commit/no shoulder ownership. No claim of CE integer-boundary equivalence. |
| Project policy, P4–P6 | Owner smooth-return/finish rule, cancellation latch, pending replacement and timestamp ordering. Source press/release route was not established; do not label it original. |
| Project policy, P7 | Explicit positive frozen step duration, return at same segment speed, Walk while moving and Idle settled, outbound/reverse-return facing. Separate from CE frame-offset displacement/stat scheduler. |
| Original/source-confirmed (CE-source), S2/S3 | Map delta to numeric path/current-ART rotation in inspected normal begin. Compass meaning/general facing/visual adapter remain Unknown. |
| Original/source-confirmed (CE-source), S4/S6 | Frame offsets accumulate in motion fields; normal completion restores previous action. The actor placement formula still does not add frame offset a second time in the final normal rectangle. |
| Unknown | Original executable interpolation, release/tie/turn parity, frame-distance/speed, dynamic invalidation/recovery and generalized motion. No unexplained source branch filled from diagnostic defaults. |
| Hypothesis | Short tap comfort, reverse-return visual feel, turn feel and analog timing need future independent UX observations. |

## Historical original-data lineage — not rerun

**Original/source-confirmed (historical original metadata)**. No original game
files were opened for a new movement observation in this run; therefore no new
before/after fingerprint equality is claimed. These are attributed historical
identities and results, deliberately not presented as fresh command success:

- [Sector profile and identities](sector-passability-evidence.md#immutable-selected-input):
  module 355,938,305 bytes, SHA-256
  `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f`;
  explicit resource 147,991,122 bytes, SHA-256
  `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d`;
  six-prototype aggregate
  `684e04f305cef5e0c89caabee852728b33bfb85d4a7e6b2f509505815c080342`.
  Fixed base ordinal463, decoded length47,291, sector(1452,1290), extent64×64.
  This specification does not select PATCH/DIF/loose precedence.
- ARC-RES-011 [historical probe](sector-passability-evidence.md#probe-evidence-and-finite-cases)
  reports **3033 tile-name Unknown**, deliberately without parsing names; 4096
  combined occupancies Unknown. It is not a current component regression.
- ARC-DATA-006 [current documented component aggregate](terrain-obstruction-components-evidence.md#fixed-profile-aggregate-check)
  reports class-0 cells: 0 Blocked, 2942 NoBlock, **91 Unknown/null**, total3033.
  Different admitted grammar/probe stage. Neither aggregate was rerun here, neither
  makes a complete cell/step Open. Ordinary-static evidence is destination-only,
  not total collision, under [its contract](../world/static-obstruction-contract.md).
- [Actor chain](actor-placement-evidence.md#reproducibility-and-privacy) retains
  actor-research-source-01, module ordinal643, current ART0xc9001810, Idle/Walk
  resource ordinals4753/4754 and requested-facing/group/flip bindings. Its historical
  structural reports exit3 NOT_READY and byte equality are not movement-runtime
  observations or this run's pass. Cached-render and original placement remain
  unsupported/unknown as recorded there.

**Project policy**: any future new observation must bind exact unchanged input
fingerprints before and after, named context/capabilities, simulation times,
committed location/anchor/offsets, facing/action/frame and start/release/turn event
ordering. Keep original captures/content local. **Unknown/NOT_VERIFIED**: no
original executable experiment on macOS was attempted. The bounded source-spec
acceptance explicitly permits this limitation; it does not permit a fidelity or
real gameplay readiness claim.

## Executed local checks and independent-review boundary

**Project policy/check evidence**. Local-only; no CI required. Documentation-only
scope requires no .NET/Godot build or GUI export. Native review of the **exact
frozen head** is mandatory; this implementation record is not a review verdict.
Exact task/run/session/workspace/head/time/command/result logs are retained in
ignored task-local storage and the native review handoff, outside sealed bundles.

```sh
python3 -B tools/research/grid_movement_probe.py --self-test
git diff --check
```

The executed worksheet reports **53** synthetic cases: all eight signed neighbor
directions at before/equal/after entry; negative chart origin; twelve literal
ordered-input transcripts with integer frame partitions; zero-distance release;
continuous return arithmetic; invalid time/event/direction/nonmonotonic request
atomicity; rejected zero/negative/nonfinite durations; and separate Unknown,
Unsupported and Blocked diagonal rejection fixtures. Its evaluator imports no
production helpers/constants, CE data or game assets. Literal oracle anchors and
transcripts are independent of evaluator calculations. It exercises the documented
examples, **not** a production API/permit model, action-event adapter, real collision
or source-runtime comparison. It does not implement identity-bound permits,
closed-batch watermark validation or action/facing events; its zero-progress
release fixture seeds an already admitted start at the same timestamp. Those
proposed API boundaries require specification audit, not a fabricated runtime pass.
Reviewer must independently recompute the examples rather
than use a shared evaluator as sole oracle.

A task-local standard-library checker exercises relative files/Markdown anchors,
reads source-line fragments against the fetched exact pin, hashes inspected files
against the manifest, fetches the cited public raw files at the literal pin and
compares their bytes, and audits allowed-file/privacy scope. Its negative controls
reject a missing file, missing heading and out-of-range source anchor. A link
check is not proof of the behavioral claim; the table's actual source bodies and
branch conditions were separately read. Final command output/head bindings are
in the task handoff, not stale logs from an earlier task.

Self-check history: first worksheet run caught a literal expected final watermark
of 0 for an end=8 start-release fixture. Corrected that oracle to8; re-execution
passed. This is local fixture authoring, not a failed native-review correction.
No native quality correction rounds or quota waits have occurred at this stage.

## Complete acceptance and findings ledger

All rows preserve requirement, seam, independent oracle, actual check/result and
disposition. “Developer checked” is implementation_ready only; independent native
review remains outstanding and can reopen any row. Source-policy differences and
accepted original-runtime NOT_VERIFIED survive review/publication handoffs.

| ID / required behavior | Production/spec seam | Independent oracle | Actual check / result | Disposition |
| --- | --- | --- | --- | --- |
| AC1 Classified coordinates/bounds/centers/neighbors/facing/interpolation/start-stop-turn/time/Idle-Walk; FPS not speed | Contract P1–P7; exact normal location/animation definitions/callers S1–S7, not diagnostic motion | Direct pinned definitions and normal caller trace; Reviewer independently audits | Source ranges read; conditions, frame-offset motion and previous-action completion conflicts preserved; all contract claims classified; runtime NOT_VERIFIED | Developer checked; pending independent source/table review |
| AC2 Explicit engine-independent states/API; before/on/after/corner/repeat/turn/re-input/time/rejection examples | Contract P1–P8 and synthetic worksheet only | Literal exact/rational anchors and motion transcripts; Reviewer recomputation | Executed53 cases incl signed directions, cancellation/settlement arithmetic, multi-event/partition equivalence, duration validation and separate traversal rejection; no shared production constants; API-only boundaries explicitly not implemented | Developer checked; pending independent recomputation/specification audit |
| AC3 91 Unknown frontier, cardinal edges, diagonals, actor/context/static/dynamic/cross-sector; Unknown never Open | Contract readiness table; existing passability/component/static boundaries; S8/S9 | Historical admitted-profile vocabulary/results plus directly inspected source dependencies | Current91 vs historical3033 attributed separately; destination-only static boundary, all missing contributors and incomplete branch retained; no total Open inference | Developer checked; real traversal NOT READY, not a required implementation |
| AC4 Exact CE pin/license/file-symbol-lines/hash/acquisition, historical local metadata, conflicts and limits | This manifest/source matrix/historical identities | Git exact object and independently computed hashes; no new original inputs opened | Public pin acquired/read; Sustainable Use License text/title/hash verified; source ranges directly inspected, original-runtime NOT_VERIFIED explicitly accepted | Developer checked; pending Reviewer provenance audit |
| AC5 Local doc/link/probe/privacy checks and independent whole-contract exact-head review | Three docs plus synthetic worksheet; native review | Exercised link checker with negative controls, Git diff/scope and independent dev-reviewer | Probe executed; local link/source/hash/privacy/diff outputs bound in handoff; no CI/build/export needed; review NOT_VERIFIED until native verdict | Local gates checked; OPEN independent native review |
| F-POLICY Ratification before freeze, conformant P1–P8 | Root decision and contract | Orchestrator ratification720 with controlling clarifications | Root decision read; directed equality, immutable cancellation, batch ordering, explicit timing/permit boundaries carried into candidate | CLOSED ratification; Developer conformance checked, independent audit pending |
| F-EVIDENCE Actual source/license/fingerprint access | Ignored bounded CE cache and this manifest | Exact Git pin, direct source/license reads, hashes/public raw comparison | Planner's missing execution evidence resolved by actual public acquisition; no fake source pass or CE-derived independent oracle | CLOSED access gap; source-to-original parity still Unknown |
| F-HISTORY Preserve distinct historical/current tile-name admissions | Historical section and readiness table | Sector evidence historical3033 and component evidence current91 | Both retained as different probe stages, neither relabelled fresh nor total Open | CLOSED candidate check; independent audit pending |
| F-SEAMS No diagnostic free-motion/floor/facing/FPS rebranding or production change | Candidate diff and explicit contract boundary | Exact allowed-path diff and Reviewer source inspection | Documentation plus independently authored synthetic worksheet only; diagnostic production code unchanged | CLOSED candidate scope check; independent audit pending |

## Implemented / not implemented / remaining work

**Project policy/status**. Implemented: classified source-spec contract, pinned
license/hash/caller evidence, full readiness/history ledger, synthetic arithmetic
and local documentation checks. Not implemented: production movement/types, real
step permits, source scheduler, full pathfinding, original-runtime experiment or
GUI export. Remaining: native dev-reviewer exact-head full-contract audit and
corrections if needed; root-controlled authorized private PR on the same reviewed
candidate, current target/identity and PR-head readback. Review is not publication,
publication is not merge. Owner alone merges; existing dependency/owner-merge gates
are unchanged. No additional milestone or global/profile/game mutation authorized.
