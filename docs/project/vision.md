# Project vision

Arcanum Steamwoven aims to provide an independent MIT C#/Godot runtime for Arcanum using the player's legally acquired original data. Preserve original gameplay as the principal behavioral reference; distinguish compatibility findings, intentional fixes and optional mod profiles. This is an unofficial fan project with noncommercial project intent; that intent does not restrict use of independently authored MIT code. Original assets and third-party archives are not redistributed. Eventual original-data and mod compatibility require explicit versioned profiles and behavioral evidence.

Data and authoritative simulation should remain independent of Godot, UI and physical input. Current assemblies and their bounded implementations are listed in [status](status.md); the complete game runtime remains a goal. Research must retain source lineage and uncertainty. CE-derived specifications may be attributed evidence, but no CE code copying, mechanical translation, fork or runtime dependency is authorized; this does not imply a clean-room claim.

## Presentation and platforms

Develop and investigate natively on macOS first. Windows, Linux and handheld PC support require later explicit validation. Modern resolutions, high-DPI and scalable UI are baseline frontend goals, independent of HD assets.

Early graphics use the original sprites. Later upscale original assets one-to-one, preserving existing frames, directions, anchors and timing. Upscale results require human review for visual fidelity and preserved geometry/timing. Additional frames/directions and 3D are future possibilities outside the early graphics scope. Legacy and later upgraded visuals should share simulation and preserve world/action state when switching; this is direction, not implemented switching.

## Accepted controller direction

Keyboard/mouse and first-class gamepad are product goals. The accepted target is an Xbox Series controller over Bluetooth: left stick moves the actor, right stick controls the camera. Historical lab defaults invert both right-stick axes; current integrated WORLD uses separately persisted false/false defaults with independent inversion toggles. Manual camera movement cancels actor follow. Once camera and actor movement have stopped, subsequent actor-only movement resumes recentering/follow. WORLD-only LT-out/RT-in zoom changes camera ratio, not actor simulation, asset size or UI/display scale.

ARC-GPAD-002/003 implement this direction in the [synthetic gamepad lab](../../experiments/gamepad-lab/README.md), including configurable inversion, direct movement intent and the neutral-then-actor-only Follow transition. The lab uses procedural obstacles and synthetic map bounds; it is not connected to the original terrain or a production character. Physical Xbox Bluetooth behavior and comfort remain unverified. See the [merged status](status.md) and lab owner checklist for the evidence boundary.
The [integrated WORLD viewer](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy) is a separate bounded diagnostic route; it does not establish collision, original placement/movement parity, or physical device comfort. Its initial zoom 2.00 is an assessable-actor hypothesis, not an ideal-QHD or physical 1:1 claim.

## Authority and evidence

The [dependency roadmap](roadmap.md) explains sequencing without creating tasks. Hermes board `arcanum-steamwoven` remains authoritative for board work; direct explicit user instructions may authorize bounded Codex work. Operational profiles and model routing belong to conductor-hermes. A component may be implemented while original-runtime parity, human visual inspection or physical device behavior remains unverified.

## Considered and deferred alternatives

- A CE fork was rejected by the user because its licensing did not fit the independent MIT product goal. This records a product decision, not a new legal analysis of CE or third-party licenses.
- A new generic engine was considered; C#/Godot was chosen to reduce infrastructure work while retaining independent data/simulation boundaries.
- New sprite directions/frames and 3D are deferred because of asset effort, required skills and review burden. Early work preserves original sprites and later one-to-one upscale.
- Controller mouse emulation is not the primary gamepad UX. Semantic movement, camera and interaction need bounded testing and physical device evidence.
