# Local diagnostic launch and build retention

`tools/arcanum-local` is a Python-standard-library command for choosing only explicitly configured local diagnostic inputs and ignored artifact roots. It is not a game launcher, exporter, discovery scanner, cleaner, or acceptance authority.

## Project launcher

`./arcanum` in the repository root is a thin wrapper over `tools/arcanum-local`;
every identity, artifact-authority and ordinary-launch check stays there.

    ./arcanum setup [--game-root /Applications/Arcanum]   # verify the game, write every input role, register artifacts/world-builds as root "world"
    ./arcanum area [--dry-run]                            # crash-site-area from the current ARC-RENDER-007 build
    ./arcanum world [--dry-run]                           # world-diagnostic from the current ARC-WORLD-003 build
    ./arcanum status                                      # which modes have every input configured

`area` and `world` select the build named by that task's `current.json` pointer
(never timestamps or directory order) and launch it; a real run leaves the terrain
launch selection on that build, a `--dry-run` restores the previous selection.
Role paths follow the installed game layout used by the area research
(`docs/research/area-passability-closure.md`): `areaMobile` is the Crash Site map's
normal-game mobile file `modules/Arcanum/maps/qrmvs-420-1zjcnpgn` (the game's
obfuscated mobile-file name), `prototypes` is `data/proto`. **Rule:** a change that adds
or changes a viewer mode's arguments or original inputs updates `GAME_ROLES`,
`MODE_ROLES` and `ROLE_FLAGS` in `tools/local_dev/launch.py`, `./arcanum` when it
adds a mode, and this section, in the same PR. `test_launcher_sync.py` fails when
the viewer's `TerrainViewerArguments` flags and the launcher drift apart, or when a
pinned role identity disagrees with the Data layer.

## Configuration and privacy

The only persistent files are owner-only (0600), atomically replaced JSON documents:

    ~/Library/Application Support/Arcanum Steamwoven/config-v1.json
    ~/Library/Application Support/Arcanum Steamwoven/build-registry-v1.json

They have schemas `arcanum-local-config-v1` and `build-registry-v1`. They hold registered artifact-root identifiers, explicit original-input roles, a safe non-path source label, preferences, selection, and acceptance lifecycle records. They never belong in Git. Test runs may set `ARCANUM_LOCAL_HOME` to an owned temporary directory.

Configuration does not scan a machine. `configure discover --game-root PATH` examines only that caller-selected directory and the known role candidates in `GAME_ROLES` (`tools/local_dev/launch.py`: module, the three resource archives, the module patch, the normal-game mobile file and the prototype directory), without following symlinks; a result requires the documented candidate's exact size and SHA-256 gate (the prototype directory must hold `.pro` files). `--write-inputs` replaces the configured inputs with every verified role and fails closed, writing nothing, when any role is missing. `configure inputs` sets roles explicitly instead. The original installation is never changed and paths are not printed unless an inventory has the explicit local-only `--show-paths` option.

    tools/arcanum-local list [--json]
    tools/arcanum-local configure discover --game-root PATH [--write-inputs]
    tools/arcanum-local configure inputs --module PATH --terrain-resource PATH --actor-resource PATH --prototypes PATH [--source-id LABEL]
    tools/arcanum-local configure add-artifact-root --id ID PATH
    tools/arcanum-local configure select-launch --tool terrain --candidate ROOT_ID:ARC-DEV-001:CANDIDATE

## Ordinary diagnostics launch

The supported logical mappings are ART `ARC-RENDER-001/002`, terrain `ARC-RENDER-003/004/005/006`, `ARC-DEV-001`, `ARC-WORLD-001/003` and `ARC-GPAD-004`, Gamepad `ARC-GPAD-001/002/003/005`, and Display `ARC-UI-001`. The resolver supports only their observed schema-v1 pointers and immutable `arcanum-build-manifest-v2`/`arcanum-build-current-v2` bindings. Unknown/mixed schemas, a missing or stale digest, an escape from a configured root, or ambiguous current selection fails closed. Required hashes must be full lowercase SHA-256; empty/malformed hashes cannot disable verification. Only observed ART/UI legacy pointers may omit the hash entirely; a supplied hash is always validated. It never chooses by modification time.

    tools/arcanum-local launch art --dry-run
    tools/arcanum-local launch gamepad --dry-run
    tools/arcanum-local launch display --dry-run
    tools/arcanum-local launch terrain --mode terrain-only --dry-run

Every supported task and both pointer names are inspected before selection. Only a genuinely missing node under proved safe directories is absent: dangling/existing symlinks, special nodes, non-directory parents, unreadable authority and detected substitutions fail closed. The same read-only, directory-pinned no-follow proof governs optional config/registry reads and retention protection; unsafe local state cannot fall back to defaults or stale in-memory authority. Missing safe local state still uses the documented defaults without creating files.

Launch selection is an explicit local `launchSelection` record, separate from `accepted` and `rollback`: it must name the exact configured root, task, and candidate currently bound by a supported pointer. A v2 manifest's `rootId` must equal that configured root. Launch uses a process argv, never a shell: engine arguments, literal `--`, then application arguments. Its macOS equivalent is `open APP --args [engine flags] -- [application flags]`. Ordinary launch rejects smoke/self-test/matrix/capture/automated resize/input/fullscreen flags and cannot build or export. A per-tool advisory lock and exact configured executable check reject duplication by default; `--allow-duplicate` is the sole opt-in (including when the launcher lease is held). It never kills processes or uses broad process-name matching. Launch and plan output omit executable, input, and candidate paths unless the caller passes the explicit local-only `--show-paths` option.

Terrain actor diagnostic defaults are deliberately caller-labelled zero map/camera/offset anchors, `normal-uncached`, scale 100, shrunk false, Idle, facing 0, frame 0. In actor-diagnostic mode only, callers may explicitly provide `--actor-map-x`, `--actor-map-y`, `--actor-camera-origin-x`, `--actor-camera-origin-y`, `--actor-offset-x`, `--actor-offset-y`, `--actor-render-branch`, `--actor-scale-percent`, `--actor-shrunk`, `--actor-action`, `--actor-facing`, and `--actor-frame`. The launcher accepts only the viewer's fixed normal-uncached/100/non-shrunk profile, Idle/Walk, facing 0..7, and an action-valid frame; terrain-only and combined reject these options. This is diagnostic policy, not inferred world state or original-placement parity.

## Retention lifecycle

### Gamepad presets artifact (ARC-GPAD-005)

Use a separate owned `ARCANUM_LOCAL_HOME`, root `gpad005` pointing to ignored `artifacts/gamepad-builds`, and literal `ARCANUM_GAMEPAD_EXPORT_TASK=ARC-GPAD-005` with the existing `experiments/gamepad-lab/tools/validate-macos.sh`. The output is `ARC-GPAD-005/FULL_HEAD/GamepadLab.app` under that root. Leave default/old homes, 001/002/003 current/accepted/rollback authorities and terrain 004 unchanged; do not broaden selection to skip unsafe siblings or repackage 003. Before export, prove the canonical tools/editor bridge, clean complete committed tree, lease and measured destination budget. A known complete configured payload may supply the estimate, not directory history or a guessed app size.

005 adds bound SDK/editor, source/exported matrix and preferences, shared-regression and policy gates to the existing Core/self-test/export/smoke/codesign requirements. Writer and registration use the same full semantic/report/source-build-runtime validator. Native baselines and actual non-headless same-scene tuple rasters are distinct from physical display/comfort observations. Source and exported restart/migration checks use isolated preference paths. A failed/partial/finalized candidate stays intact and cannot be blindly exported again; no manual `builds register-export` bypass is permitted.

After registration, set the owned home's exact launch selection `gpad005:ARC-GPAD-005:FULL_HEAD`, then ordinary `launch gamepad --dry-run` and `launch gamepad` with all test-only preference/candidate overrides removed. Record actual executable/PID/argv/one-window/runtime classification outside the sealed bundle, and revalidate complete manifest/evidence/payload digests and deep/strict codesign after runtime. Selection/registration is not independent acceptance; local acceptance overlays require the full exact-head review/authority references. Owner physical XboxBT/drift/reconnect/comfort/final-layout stays separately PENDING. [Preset instructions and full checklist](../input/gamepad-presets.md).

WORLD uses the same terrain logical tool and immutable v2 authority, with a
separate ignored configured `artifacts/world-builds` root. Do not delete legacy
current pointers or relax ambiguity checks to select it. Configure only an
owned home when task isolation is required, explicit authenticated input roles
(prototypes is a directory), then select `ROOT_ID:ARC-WORLD-001:<full-head>`.
Ordinary invocation is explicitly `launch terrain --mode world-diagnostic`;
default stays terrain-only, preview overrides and legacy-artifact WORLD mode
reject. Manifest `app.ordinaryArgs` is metadata, not an implicit CLI default.
The closed `ARCANUM_TERRAIN_EXPORT_TASK=ARC-WORLD-001` endpoint adds focused
tests and original source/export WORLD gates to the existing seven terrain
gates. Missing/failed/unverified original input gates prevent registration.
One final-head export follows source corrections and resource proof. Later
ordinary logs and manifest/payload digest equality are recorded outside the
sealed artifact, without automated input/capture or owner-interaction kills.
See [integrated WORLD policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy).

`builds inventory` is conservative and dry-run by default. It reports only configured roots and known candidate directories. A candidate is kept for unknown ownership, current/rollback/active/open-review/unmerged lifecycle, in-use state, symlink ambiguity, or any missing proof. A candidate is eligible only when containment, supported ownership, and explicit retired/disposable state are all proved.

    tools/arcanum-local builds inventory [--show-paths]
    tools/arcanum-local builds plan --output PLAN.json
    tools/arcanum-local builds apply --plan PLAN.json --accept-plan PLAN_ID
    tools/arcanum-local builds export-preflight --tool terrain --root-id ID --temporary-root TMP [--output-bytes BYTES --estimate-manifest MANIFEST.json] [--temporary-bytes BYTES] [--write-root BUILD_OR_CACHE]
    tools/arcanum-local builds register-export --tool terrain --candidate ID --manifest MANIFEST.json
    tools/arcanum-local builds accept --tool terrain --candidate ID --manifest MANIFEST.json --authority REF --review REF
    tools/arcanum-local builds retire --candidate ID

A plan binds config/registry generations and digests plus registered-root, candidate and payload identities, manifest hash, and both the presence and hash/absence of each current pointer. Apply takes the exclusive registry lock, freshly reloads and revalidates the configuration, registry, root, candidate, manifest, payload, pointer and plan bindings, and rejects launcher/export leases or an exact executable in use. Those leases remain held from intent write through payload removal and receipt write. It can remove only a revalidated explicitly retired registered v2 `.app` payload and writes an owner-only `prune-intent-v1.json` followed by an idempotent `payload-pruned-v1.json` receipt; crash recovery revalidates the immutable digest and every authority fact before finishing only the same plan ID. It never deletes a candidate, manifest, evidence, worktree, toolkit, original install, SDK, cache, temporary directory, or unrelated path. Inventory treats every symlink, unreadable, unsupported/mixed/unknown manifest or registry identity as `KEEP/SKIP`. Export registration requires an exactly bound v2 candidate, measured on-disk inventory, complete package, inactive executable, export lease, and all named tool-specific success gates (including deep/strict codesign) passing; it creates lifecycle `exported`, not acceptance. Acceptance requires that exact registered candidate/digest plus authority and review references, then atomically records the accepted candidate and one rollback. Accepted current/rollback entries cannot be retired.

Retention protects the union of every supported `current.json` and `current-v2.json` across configured roots/tasks, all explicit launch selections (even stale), and accepted/rollback references. Multiple valid currents protect all targets although launch rejects ambiguity. Malformed/unreadable/escaping/unsupported authority means KEEP/SKIP for the affected root/tool. Plans carry `protectionBindings` with hashes AND absence of every pointer and the selection digest. Apply rechecks under lock/leases before intent, deletion and crash recovery; old plans without this proof require replanning. Retire requires an existing registered immutable configured identity and cannot retire a protected selection. Repeated exact manual registration preserves lifecycle; digest conflicts reject.

Prune receipts/intents and export publication snapshots use the same pinned read-only regular-file proof as configuration and pointers. Only genuine absence is optional; dangling/loop/existing links, directories, special nodes, unreadable files and detected substitutions reject boundedly without being opened as streams or replaced as missing records. Apply also rechecks receipt/intent bytes or absence after lease acquisition and immediately before intent/deletion/recovery mutation. This is fail-closed detection, not a claim of an atomic filesystem-wide snapshot against arbitrary concurrent writers.

Payload proof requires canonical unique root-relative paths and exact equality with the package's complete regular-file set, including empty files, plus every byte count and digest. Equal byte totals alone are not completeness evidence. Exporter, launcher and registry locks use the same directory-pinned no-follow regular-file acquisition; symlink, dangling, special-node, hardlinked and unsafe-parent leases fail before child invocation or target mutation. An unsafe lock-only candidate is not an empty candidate and cannot be reused.

Preflight uses the largest verified accepted/current same-tool payload plus 10%; otherwise `--output-bytes` must equal ONE verified complete configured payload named by `--estimate-manifest`. Aggregate historical-directory or source-tree size is not a measurement. Temporary budget is provenance-qualified prior sampled allocation (`qualified-history-estimate`), an explicit estimate, or conservative twice output; reserve is `max(2 GiB, one output)`. The FULL budget is checked independently on each distinct export, effective temporary, and declared build/cache filesystem (shared devices deduplicate). Canonical existing ancestor/device/inode proofs are revalidated; the configuration filesystem is never a proxy. Missing destination or trusted estimate fails before export.

The three existing Gamepad, Display and Terrain `validate-macos.sh` endpoints re-exec under `tools.local_dev.export_endpoint` with configured `ARCANUM_LOCAL_ROOT_ID` and an explicit effective `TMPDIR`. Optional `ARCANUM_OUTPUT_BYTES` plus `ARCANUM_ESTIMATE_MANIFEST` supplies the one-payload estimate when no valid accepted/current measurement exists. One candidate lease spans preflight, validation/export/runtime/codesign, frozen observation, manifest-only finalization, registration and atomic pointer replacement. Internal `ARCANUM_EXPORT_PHASE=validate|finalize` is endpoint-controlled: validate NEVER writes a manifest; finalize NEVER reruns export/tests. Finalized or ambiguous partial candidates reject intact before evidence writes/child invocation, including failed same-head retries. No automatic cleanup or rebuild of immutable artifacts is permitted.

The bounded sampler observes baseline/periodic/final destination free bytes and isolated per-export TMPDIR allocated blocks; it always stops/joins. `evidence/export-observation.json` (`arcanum-export-observation-v1`) is frozen BEFORE every writer hashes evidence and bound separately as `exportBudget.observation`. Free-space drops and output bytes are never temporary usage. Production Godot/.NET caches are not fully isolated by TMPDIR: observations therefore say `temporaryMeasurementStatus=unavailable`, `observedTemporaryBytes=null`, while preserving real sampled filesystem low water and coverage limitations. Compatibility `temporaryLowWaterBytes=0` means unavailable here, NOT a measured zero. Only successful qualified fully attributed observations with candidate/manifest/observation provenance update history; failed finalization/publication cannot advance it. Legacy v2 remains read-only compatible, and its old temporary estimate is never trusted observed history. ART remains next-export-only guidance, not rebuilt here.

## Closed human WORLD candidate

Terrain additionally supports `ARC-GPAD-004` and `ARC-RENDER-006` in the same
ignored world-builds root and v2 schema. Explicit task selection permits valid
historical currents to coexist; every supported sibling is still inspected and
unsafe/duplicate same-task/wrong-head authorities fail closed. RENDER006 ordinary
WORLD uses HumanFixed, while standalone actor preview remains MonsterRegression.

Use `ARCANUM_TERRAIN_EXPORT_TASK=ARC-RENDER-006` with the existing leased endpoint.
The inherited thirteen terrain/WORLD/GPAD gates plus `human-metadata`,
`source-human-original`, `exported-human-original` must all PASS. The new three
structured reports bind task/head/host/profile/original identity and actual human
graphical coverage, not only historical WORLD marker text. Keep independent
original-pixel oracle local/ignored; never ship it in a manifest or package.
No ordinary input automation is added. After selecting the immutable candidate,
`launch terrain --mode world-diagnostic --dry-run` and a real ordinary launch
are separate from smoke; require actual Idle/Walk observation and unchanged
seal/signature outside the sealed app. Registration is still not acceptance,
review, publication or owner merge.

## Bounded original grid candidate (ARC-WORLD-003)

WORLD003 uses the existing terrain endpoint, logical tool and world-builds root,
not a new exporter or alias. In an owned `ARCANUM_LOCAL_HOME`, configure all five
input roles (module, terrain resource, actor resource, prototype directory,
source label), root and exact `ROOT_ID:ARC-WORLD-003:<full-head>` selection.
Do not overwrite parent/default homes or delete historical task currents.

    ARCANUM_LOCAL_HOME=OWNED_CONFIG ARCANUM_LOCAL_ROOT_ID=WORLD_ROOT_ID \
      ARCANUM_TERRAIN_EXPORT_TASK=ARC-WORLD-003 TMPDIR=OWNED_TMP \
      tools/terrain-viewer/Godot/tools/validate-macos.sh

After successful immutable export and exact selection:

    ARCANUM_LOCAL_HOME=OWNED_CONFIG tools/arcanum-local launch terrain --mode world-diagnostic --dry-run
    ARCANUM_LOCAL_HOME=OWNED_CONFIG tools/arcanum-local launch terrain --mode world-diagnostic

The actual grid source has no free fallback: preview overrides, wrong legacy
task/head, malformed or unsafe sibling authorities, and missing inputs reject
before launch. Retain all sixteen parent gates, plus source/exported
`grid-original` reports validated against literal complete traces at all three
required viewports. For WORLD003, every gate also has a timestamped actual-child
exit/digest receipt; writer and registration enforce PASS/exit0/task/head/log
binding. Registration requires both logs and receipts in the sealed evidence
inventory. Synthetic authority fixtures prove fail-closed consumer behavior,
not original graphical acceptance.

Dry-run establishes argv only. Ordinary graphical observation must separately
establish original human Idle/Walk, early return, post-entry finish and visible
blocked destination on the exact exported app with no automation flags. Keep
its PID/argv/viewport/exit/crash and post-run manifest/full payload/evidence/
signature proof outside the seal. A native-smoke PASS cannot replace this lane.
See [bounded grid policy and input mapping](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy).

## Boundaries

A local generated/exported artifact is never implicitly accepted, merged, or published. Real artifact deletion requires a separately accepted fully proven plan; this candidate intentionally performs no deletion. Human review, original-data checks, graphical inspection, physical controller tests, Bluetooth reconnect/drift, all axes/settings, exhaustive stability, and original parity remain separate evidence.
