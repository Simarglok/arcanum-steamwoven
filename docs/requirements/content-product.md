# Data, compatibility and separate Steamwoven goals

**Current status:** the status of each requirement in this document, the implemented scope, the remainder and the documentation are in the [coverage matrix](coverage/content-product.md) (rechecked 2026-10-20, ARC-DEV-027). The "Steamwoven" fields below and the summary coverage section give details as of their last edit (initial audit 2026-10-09, merged main [`a8a553b7`](https://github.com/Simarglok/arcanum-steamwoven/tree/a8a553b7c406ace77ea019bf2b13251f00ecddb6), PR #54); where they differ, the matrix is right.

Draft, 2026-10-02. Initial Steamwoven research pin `b1245e59c585c7b7b03dd0b764e2d3e31d0f89ae`. **Source (F0)** below means reading the original's functions at the given addresses, level F0 (not verified in the exe), unless the [verification ledger](../research/original-verification-ledger.md) has a row for it; no new original-data or runtime checks were run. Confirmation by reading a function is not the same as observing the original's behavior. The scenarios below are proposed acceptance, not executed tests. See the [register rules](README.md).

## Content and system dependencies

<a id="fr-content-001"></a>

### FR-CONTENT-001 — Original resources are read from selected sources

**Behavior.** A launch uses the data of the user's own installed game; a resource's logical name is kept regardless of the physical archive/directory. A missing required source has an explicit error result. **Source (F0):** module DAT and catalog loading (0x404C10; the attach order is checked in the ledger row "Prototype phase"), repository attach; the policy of not distributing assets comes from the [vision](../project/vision.md), not from the original. **Dependencies/acceptance:** an external data source, DAT/loose lookup; a correct installation resolves the declared resource, an incomplete one gives a diagnosable error without substitution. **Coverage:** partial — authenticated [Area owner/selected-frame leases](../world/area-loader.md), bounded DAT and explicit selected NPC SCR/DLG sources. ARC-DATA-011 added read-only installation discovery and a shared resolver ([research and API](../research/content-precedence.md)); the Area loader and NPC dialog still read explicitly selected archives.

<a id="fr-content-002"></a>

### FR-CONTENT-002 — Resource and patch priorities are set by a verified profile

**Behavior.** When several versions of one resource exist, the version is chosen according to the declared game/patch profile, the same for all consumers. **Source (F0):** module patch registration (0x404C10), lookup with database flags and a writable repository. The rule "the last directory always wins" is not enough here: the archive branch takes flags and a separate loose-file search into account. **Dependencies/acceptance:** an independent resolver; a synthetic base/patch/loose conflict with explicit expected outcomes under the approved profile. **Coverage:** partial (ARC-DATA-011, [research](../research/content-precedence.md)). The mount order and the lookup rule are traced through the original's functions (F0; module mounting after the base sources — row "Prototype phase"); on a static installation the first read and exists/extract choose one copy — the first source in the search order. `Arcanum.Data.Content` implements this rule with synthetic and real-data tests; on the owner's installation there are 109 overlaps, all inside archives. The current loaders still accept selected caller archives (Virgil's script is read from the shadowed copy in `arcanum2.dat`). Not modeled: files written during play, the first-open sticky flag, `.ignore`, `.dif` and other modules; parity with the original is NOT_VERIFIED.

<a id="fr-content-003"></a>

### FR-CONTENT-003 — A campaign/module has its own initial parameters

**Behavior.** The selected module sets the campaign identity, the initial map/character and the starting time within the supported profile; the parameters of one campaign must not be applied implicitly to another. **Source (F0):** the module list (0x4027C0), mod-load callbacks and mobile preparation (0x402A50), adventure/time metadata (0x4B9C30; the starting year 1885 and 15:00 are checked — row "Clock start"). The initial reset is traced: the new-game reset (0x4B9BE0) selects the shopping map with a fallback to the start map, opens it and calls player character creation (0x40D8A0). This does not establish the final campaign map/PC/prototype binding, the specific spawn point or the original's runtime result; they remain unknown. **Acceptance:** two synthetic module profiles do not mix identity and starting clocks; an unsupported module is rejected explicitly. **Coverage:** absent; the fixed Crash Site diagnostic selector is not a campaign bootstrap. **Second module (owner decision 2026-10-27, ARC-DEV-044):** choosing and loading another module (for example `Vormantown`, which the original also shows in the single-player module list, [NT7](../research/ledger/multiplayer.md#multiplayer-arc-res-072)) in single-player comes after the alpha; network play reuses it later ([FR-NET-004](network.md#fr-net-004)).

<a id="fr-content-004"></a>

### FR-CONTENT-004 — Objects keep identity, type and inherited properties

**Behavior.** An instance override and a prototype default are distinguished; entity references are not replaced by a sprite index or a temporary UI handle. **Source (F0):** the object read chooses proto/instance (0x406600), the field fetch has direct/transient/prototype branches (0x408A20; replacement of a prototype field by an instance field is checked — row "Field inheritance"), the solitary object reader (0x4E6BC0). **Acceptance:** differing instance/default values, identity kept across a transition, an explicit unsupported type. **Coverage:** partial — [Area](../world/area-loader.md) keeps multisector v119 static/mobile/prototype identity, direct/effective presence and immutable occurrences. A mutable registry and the save/actor lifecycle are absent.

<a id="fr-content-005"></a>

### FR-CONTENT-005 — Systems keep reading the original structures

**Behavior.** DAT/ART/PRO/MOB/MES/DLG/SCR/maps remain the sources for the supported areas; a one-time conversion does not hide the original identity and incompatible records. **Provenance:** the accepted [architecture direction](../architecture/decisions.md); the original's functions show separate DAT variants and an object reader (0x406600, F0), but do not prove that every variant is admissible. **Acceptance:** supported/unsupported formats and versions have an explicit result; a cache does not lose the original IDs. **Coverage:** partial — bounded DAT/ART/MES/SEC/PRO/mobile/DLG readers and the selected SCR entry graph are used directly. Support for all variants, overlays/DIF/PATCH, save and the full VM is not claimed. [Catalogue](../project/status.md).

<a id="fr-content-006"></a>

### FR-CONTENT-006 — Text resources and encoding are not substituted by hand

**Behavior.** Names, descriptions, dialogs, books and UI text are loaded by their source identifiers; the encoding and localization profile is set explicitly. **Source (F0):** MES load (0x4D4290), numeric/text record parse (0x4D4A60). These functions do not establish a universal Unicode/localization contract. **Acceptance:** synthetic Cyrillic/Latin and a missing message ID are handled under the declared profile without corrupting raw bytes. **Coverage:** partial — the byte-preserving [DLG reader](../formats/dlg-reader.md), bounded MES projections for data and the [MES reader ARC-DATA-013](../formats/mes-reader.md) with explicit ASCII/Windows-1252/Windows-1251 profiles and a typed missing number (Cyrillic with `я` is still rejected); localization, UI text and consumers of the MES reader are absent.

<a id="fr-content-007"></a>

### FR-CONTENT-007 — Randomness belongs to gameplay state

**Behavior.** Gameplay checks have a declared RNG policy; the camera, frame rate or a visual-mode change must not consume game rolls. **Source:** seed/state and integer-range draws (0x4CEED0, 0x4CEEE0, 0x4CEF00, 0x4CEF40, 0x4CEF50); the generator, ranges, seed and reseeds are checked — ledger row "Random (R1–R6)". The separation of rendering and RNG is **project policy**; the exact order of calls in the original is not reconstructed by this report. **Acceptance:** for a given seed and sequence of game commands, visual changes do not change the rolls. **Coverage:** partial — the [session generator](../world/session-random-runtime.md) (ARC-WORLD-019): SplitMix64 from the host seed, labeled uniform rolls, seed and position in snapshot schema 7, the roll source for the attack resolver; rolls are made only by an explicit call, so drawing does not spend them. The task's decision: a project generator instead of the original's LCG (in the original the seed is taken from the time and is not saved, so the original sequence cannot be reproduced) and without the original's modulo bias; the owner may revisit it. Combat is not wired everywhere; the seed of new viewer sessions is taken from the clock, as the original seeds its generator at startup (ARC-DEV-080), tests and smokes use seed 0; the original's reseeding of the generator for inventory and dialog is not modeled. Load (GI6, owner decision 2026-11-04, ARC-DEV-077): in Steamwoven the saved generator is restored, so a reload repeats the results; in Original a load continues the host's running generator (the original does not save the generator), and at viewer startup this generator is seeded by the clock (ARC-DEV-080), so a load right after a restart does not repeat the rolls; the saved gambling roll (PS14) will appear together with gambling in the session.

<a id="fr-content-008"></a>

### FR-CONTENT-008 — Mods and patches have measurable coverage

**Behavior.** Compatibility is stated for a specific data/patch/mod profile and the supported features. The presence of a DAT loader does not mean support for UAP/Multiverse or executable changes. **Provenance:** the [accepted compatibility direction](../project/vision.md) and the original's module/patch hookup (0x404C10, F0); broad compatibility is not confirmed. **Acceptance:** a reproducible synthetic override and an opt-in local sample of the selected profile without changing the installation; unsupported engine extensions are named. **Coverage:** general mod coverage is absent; bounded loaders work with specific caller inputs. Support for existing mods is a staged goal.

## Modern product requirements — not original behavior

<a id="fr-product-001"></a>

### FR-PRODUCT-001 — Modern resolutions, HiDPI and a readable UI

World and UI scale independently; the viewport does not change the simulation. Modern resolution, HiDPI, a scalable UI and the independence of the simulation from the viewport are accepted. The specific resolution/window mode/UI scale selection UI is proposed for discussion; the accessibility of text and actions remains a goal. **Basis:** the [vision](../project/vision.md), the owner's observation of small legacy sprites on QHD. **Proposed acceptance:** real QHD/HiDPI and a separately selected resolution matrix, usable settings/navigation, world/UI independence; the exact set of devices/modes is not yet approved. **Coverage:** partial — the [synthetic/native display lab](../input/display-ui-validation.md); production UI and the physical readability of the whole game are not confirmed.

<a id="fr-product-002"></a>

### FR-PRODUCT-002 — Keyboard/mouse and semantic gamepad control

**Accepted clarification 2026-10-04:** keyboard/mouse use the original Arcanum scheme and its interface actions; WASD movement is not required. New presets and contextual UI belong to the gamepad. The detailed specification and diagnostic extensions are in [original-keyboard-mouse](original-keyboard-mouse.md); the modern resolution/HiDPI/UI scaling from FR-PRODUCT-001 are kept.

The gamepad passes actor/camera/action intents, the UI shows contextual prompts; direct control does not require moving the cursor separately and pressing movement at each step. The base verification target is Xbox Series over Bluetooth; this is not a claim that all controllers were checked. **Basis:** the [accepted controller direction](../project/vision.md#accepted-controller-direction). **Acceptance:** movement/selection/dialog/menus are available with both input methods; reconnection and actual device UX are checked separately. **Coverage:** partial — the [input lab](../../experiments/gamepad-lab/README.md) and a diagnostic scene; original-world interaction and production menus are absent.

<a id="fr-product-003"></a>

### FR-PRODUCT-003 — Independent camera, follow and controlled zoom

The left stick controls the actor; the right one the camera, with independent settings for both axes. Manual pan drops follow; after the actor/camera return to neutral, a new actor-only command restores follow. LT — zoom out, RT — zoom in; exact presets for returning to 100% and a comfortable magnification are proposed as a UX detail and are not yet an accepted concrete mapping. **Basis:** the owner's direct decisions in discussion; the [current diagnostic policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy). The preferred direction is described by the visible movement of the picture, not by a checkbox name. **Acceptance:** both axes set independently by mouse/gamepad, a neutral/follow scenario, an exact preset, saved preferences. **Coverage:** partial — merged WORLD zoom/Follow/grid integration: LT-out/RT-in, 0.125..8, exact 1/2/3x presets/numeric entry, ordinary default2x and a separate persisted zoom policy. [PR #37](https://github.com/Simarglok/arcanum-steamwoven/pull/37), [WORLD003](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy). Numerical1.00 is not physical pixel1:1; production limits and Bluetooth/QHD comfort are not separately confirmed.

<a id="fr-product-004"></a>

### FR-PRODUCT-004 — Legacy and a future upscale share one world

The base graphics use the existing sprites; a future upscale keeps the world size/anchors/frames/directions/timing. A runtime toggle keeps the actor/world/action state, camera framing and UI; a missing HD asset allows the declared Legacy fallback. **Basis:** [visual modes direction](../architecture/decisions.md), [vision](../project/vision.md). **Acceptance:** the same world anchor and size, a toggle during movement/lighting without a map reload, identical gameplay outcomes; human review of HD assets. **Coverage:** Legacy diagnostic rendering partial; the [HD presentation contract](../rendering/hd-presentation-contract.md) (ARC-RENDER-013: scaled crop, Legacy anchors, per-frame fallback, budgets, toggle protocol, synthetic tests); the [2× upscale methods research](../rendering/hd-upscale-research.md) (ARC-RES-033: recommended method, alpha, palette slots, ground seams, licenses); the [HD render path](../rendering/terrain-viewer.md#hd-render-path-arc-render-014) (ARC-RENDER-014: characters and objects in AREA/WORLD, per-frame rollback, budgets, HUD counter); the [Legacy/HD toggle](../rendering/terrain-viewer.md#visual-profile-setting-and-toggle-arc-ui-006) (ARC-UI-006: persisted setting, the H key and palette, commit in one frame or after 500 ms; Session snapshots are byte-equal in Legacy, HD and across toggles, including on the original Crash Site with dialog, walking, the hour and entering the cave); there is no real asset pack. Additional frames and 3D are future variants.

<a id="fr-product-005"></a>

### FR-PRODUCT-005 — Platforms are verified on one functional base

macOS, Windows, Linux and handheld PC must run one declared data/simulation profile; handheld readability, performance and controller UX are checked separately. **Basis:** the [platform sequence](../architecture/decisions.md). **Acceptance:** a reproducible selected slice on each supported platform with the same state outcomes. **Coverage:** development and bounded diagnostics on macOS; general game cross-platform compatibility is not confirmed. This is a future product goal, not the result of having a Godot platform export.

<a id="fr-product-006"></a>

### FR-PRODUCT-006 — Compatibility and intentional fixes are distinguished

The original simulation is the principal reference. Improved pathfinding or gameplay bug fixes may be accepted separately with explicitly described differences; a change must not be disguised as restored original behavior. **Basis:** the [vision](../project/vision.md) and the discussion of a possible path improvement. **Acceptance:** a source/observed baseline, a regression scenario and a declared difference before an override is activated. **Coverage:** policy and a [bounded path study](../input/arcanum-pathfinding-study.md); there is no proven path-quality improvement or gameplay fix profile.

<a id="fr-product-007"></a>

### FR-PRODUCT-007 — Late image improvements have separate boundaries

Normal/depth/occlusion/emissive maps, soft shadows, fog, weather, particles, improved water and smoother visual day/night transitions are Remastered presentation variants. They must not change authoritative geometry, gameplay luminance, RNG or clocks. **Provenance:** the owner's initial product vision; these are development candidates, not restored original functions and not a required scope of the early Legacy milestone. **Acceptance:** for each separately accepted capability, show identical gameplay state outcomes under different visual settings; asset review and performance are checked separately. **Coverage:** absent. If the original already has a similar effect, its legacy behavior is researched separately; a similar name does not prove identical semantics.

## Steamwoven coverage up to 2026-10-20

The coverage table of this section has been moved to the [coverage matrix](coverage/content-product.md): there each requirement has its current status, scope, remainder and documentation (2026-10-20). The paragraphs below described this document's former table (initial audit 2026-10-09 with later edits) and are kept as history; the words "table", "rows" and "columns" in them refer to it.

No row closes a requirement completely. "Partial" denotes exactly the scope in the last column, and "Verification" denotes bounded historical checks of related components, not a new run and not original parity.

## Source inventory and questions

The inventory includes the object identifier group 0x4E62A0–0x4E6B00 (13 functions; GUID and resident/static identity; selected functions 0x4E62A0, 0x4E62D0, 0x4E6340), the prototype field tables of group 0x4681B0–0x49C610 (data; not a full bootstrap), the ART and missing-image metadata of group 0x41CA40–0x41F240, the initialization of service indicators of groups 0x4BB720–0x4BB980 and 0x4BB9A0–0x4BBCE0, and the sparse-array container infrastructure. The selected functions of the object pool (0x4E4CD0–0x4E59A0), field storage (0x4E3F80–0x4E6240), object list nodes (0x4E9920–0x4E99C0), magic and technology nodes (0x4BAC10–0x4BAF50) and sparse arrays (0x4E7430–0x4E7AB0) are described in the [object infrastructure inventory](source-coverage.md#object-infrastructure); the full lifetime/restore paths are not researched. No new requirements are derived from the group's topic.

The waypoint group (0x4BFCE0–0x4C0370) contains a draw entry (0x4BFE00) and the drawing of NPC waypoint markers (0x4C0140, 0x4C0280). A caller in the editor draw path (0x404740) is found; the filling of the private tracked-object list and its actual use are not established. This is the boundary of knowledge about the overlay, not a confirmed additional game mechanic.

The listed functions of the game library groups (0x4020F0–0x405070), module parameters (0x4B9B90–0x4B9DB0), object storage (0x405110–0x40D670), object files (0x4E6B60–0x4E71B0), MES reading (0x4D4290–0x4D4DD0), the random number generator (0x4CEEB0–0x4CEF50) and the engine library function for files and DAT archives were read; this is not a line-by-line audit of every group. The current Data/Simulation/Presentation docs and the `StaticSceneryScenePlan` path were checked. The loaders of all proto fields, base-install registration, the art variant/resource cache, the charset/font pipeline and all platform-specific lookup branches need additional detail. Large groups of functions are not considered fully researched because of a single reference.

The architectural constraints — an independent MIT runtime, data/simulation separation from Godot and the ban on including original assets (one exception: the [app icon](../../assets/app-icon/README.md), owner decision 2026-11-28) — are recorded in [architecture](../architecture/decisions.md) and the [research method](../research/method.md). These are constraints on the whole implementation, not additional game mechanics.
