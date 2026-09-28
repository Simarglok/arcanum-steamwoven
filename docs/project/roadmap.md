# High-level dependency roadmap

This is a product dependency map, not a task queue or authorization to implement a milestone. Active cards, assignments and acceptance contracts live on Hermes board `arcanum-steamwoven`. The [status catalogue](status.md) anchors existing work; [vision](vision.md) anchors accepted direction. Stages below are proposed sequencing, not agreed detailed implementation contracts or dates.

| Stage | Depends on | Outcome and gate before expansion |
| --- | --- | --- |
| Current foundation | Reviewed research and bounded Data/Presentation/Scripting components | Maintain source-linked scope, toolchain and uncertainty. Existing loaders/viewers/labs do not prove a full game. |
| Diagnostic terrain + scenery integration | Terrain viewer, selected object/resource metadata, placement evidence | A bounded combined diagnostic scene with explicit source selection and fail-closed unsupported cases. Resolve remaining placement/order/occlusion questions separately; ARC-RENDER-004 remains proposed. |
| Actor and movement foundations | Admitted actor ART/timeline semantics; investigated map/collision/navigation data | Actor presentation and movement with explicit obstacle/navigation boundaries. Continue accepted controller direction in a separate bounded prototype, then verify physical Bluetooth behavior. |
| Minimal world interaction and dialog | Stable actor/world identity, targeting, bounded dialog runtime and state ports | One admitted interaction and dialog integrated with world state, with documented frontier/failure behavior. |
| Small playable slice | Combined scene, actor motion, obstacle handling, interaction/dialog, input integration | A small reproducible scene with keyboard/mouse and gamepad, meaningful checks and human observations. Scope still does not imply the complete Crash Site or original game. |
| Gameplay expansion | Verified slice and newly researched dependencies | Extend scripting/quests, inventory, combat, persistence and transitions incrementally; each contract preserves compatibility evidence and unsupported cases. |
| Compatibility and presentation expansion | Behavioral evidence and stable simulation boundary | Validate Legacy compatibility and other platforms; subsequently upscale original frames/directions one-to-one. New animation directions/frames and 3D require separate future decisions. |

Where dependencies permit, bounded data research and controller UX exploration can proceed independently. Neither constitutes approval to launch workers, create cards, change shared profiles, or dispatch the whole roadmap. Original-runtime comparison and evidence lineage remain gates throughout, not a final label granted by green synthetic tests.
