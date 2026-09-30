# Local diagnostic launch and build retention

`tools/arcanum-local` is a Python-standard-library command for choosing only explicitly configured local diagnostic inputs and ignored artifact roots. It is not a game launcher, exporter, discovery scanner, cleaner, or acceptance authority.

## Configuration and privacy

The only persistent files are owner-only (0600), atomically replaced JSON documents:

    ~/Library/Application Support/Arcanum Steamwoven/config-v1.json
    ~/Library/Application Support/Arcanum Steamwoven/build-registry-v1.json

They have schemas `arcanum-local-config-v1` and `build-registry-v1`. They hold registered artifact-root identifiers, explicit original-input roles, a safe non-path source label, preferences, selection, and acceptance lifecycle records. They never belong in Git. Test runs may set `ARCANUM_LOCAL_HOME` to an owned temporary directory.

Configuration does not scan a machine. `configure discover --game-root PATH` examines only that caller-selected directory and the three known candidates `modules/Arcanum.dat`, `arcanum1.dat`, and `arcanum2.dat`, without following symlinks; a result requires the documented candidate's exact size and SHA-256 gate. `configure inputs` needs each original role explicitly. The original installation is never changed and paths are not printed unless an inventory has the explicit local-only `--show-paths` option.

    tools/arcanum-local list [--json]
    tools/arcanum-local configure discover --game-root PATH
    tools/arcanum-local configure inputs --module PATH --terrain-resource PATH --actor-resource PATH --prototypes PATH [--source-id LABEL]
    tools/arcanum-local configure add-artifact-root --id ID PATH
    tools/arcanum-local configure select-launch --tool terrain --candidate ROOT_ID:ARC-DEV-001:CANDIDATE

## Ordinary diagnostics launch

The supported logical mappings are ART `ARC-RENDER-001/002`, terrain `ARC-RENDER-003/004/005` and `ARC-DEV-001`, Gamepad `ARC-GPAD-001/002/003`, and Display `ARC-UI-001`. The resolver supports only their observed schema-v1 pointers and immutable `arcanum-build-manifest-v2`/`arcanum-build-current-v2` bindings. Unknown/mixed schemas, a missing or stale digest, an escape from a configured root, or more than one current pointer fails closed. It never chooses by modification time.

    tools/arcanum-local launch art --dry-run
    tools/arcanum-local launch gamepad --dry-run
    tools/arcanum-local launch display --dry-run
    tools/arcanum-local launch terrain --mode terrain-only --dry-run

Launch selection is an explicit local `launchSelection` record, separate from `accepted` and `rollback`: it must name the exact configured root, task, and candidate currently bound by a supported pointer. A v2 manifest's `rootId` must equal that configured root. Launch uses a process argv, never a shell: engine arguments, literal `--`, then application arguments. Its macOS equivalent is `open APP --args [engine flags] -- [application flags]`. Ordinary launch rejects smoke/self-test/matrix/capture/automated resize/input/fullscreen flags and cannot build or export. A per-tool advisory lock and exact configured executable check reject duplication by default; `--allow-duplicate` is the sole opt-in (including when the launcher lease is held). It never kills processes or uses broad process-name matching. Launch and plan output omit executable, input, and candidate paths unless the caller passes the explicit local-only `--show-paths` option.

Terrain actor diagnostic defaults are deliberately caller-labelled zero map/camera/offset anchors, `normal-uncached`, scale 100, shrunk false, Idle, facing 0, frame 0. In actor-diagnostic mode only, callers may explicitly provide `--actor-map-x`, `--actor-map-y`, `--actor-camera-origin-x`, `--actor-camera-origin-y`, `--actor-offset-x`, `--actor-offset-y`, `--actor-render-branch`, `--actor-scale-percent`, `--actor-shrunk`, `--actor-action`, `--actor-facing`, and `--actor-frame`. The launcher accepts only the viewer's fixed normal-uncached/100/non-shrunk profile, Idle/Walk, facing 0..7, and an action-valid frame; terrain-only and combined reject these options. This is diagnostic policy, not inferred world state or original-placement parity.

## Retention lifecycle

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

Preflight uses the largest verified accepted/current same-tool payload plus 10%; otherwise `--output-bytes` must equal ONE verified complete configured payload named by `--estimate-manifest`. Aggregate historical-directory or source-tree size is not a measurement. Temporary budget is provenance-qualified prior sampled allocation (`qualified-history-estimate`), an explicit estimate, or conservative twice output; reserve is `max(2 GiB, one output)`. The FULL budget is checked independently on each distinct export, effective temporary, and declared build/cache filesystem (shared devices deduplicate). Canonical existing ancestor/device/inode proofs are revalidated; the configuration filesystem is never a proxy. Missing destination or trusted estimate fails before export.

The three existing Gamepad, Display and Terrain `validate-macos.sh` endpoints re-exec under `tools.local_dev.export_endpoint` with configured `ARCANUM_LOCAL_ROOT_ID` and an explicit effective `TMPDIR`. Optional `ARCANUM_OUTPUT_BYTES` plus `ARCANUM_ESTIMATE_MANIFEST` supplies the one-payload estimate when no valid accepted/current measurement exists. One candidate lease spans preflight, validation/export/runtime/codesign, frozen observation, manifest-only finalization, registration and atomic pointer replacement. Internal `ARCANUM_EXPORT_PHASE=validate|finalize` is endpoint-controlled: validate NEVER writes a manifest; finalize NEVER reruns export/tests. Finalized or ambiguous partial candidates reject intact before evidence writes/child invocation, including failed same-head retries. No automatic cleanup or rebuild of immutable artifacts is permitted.

The bounded sampler observes baseline/periodic/final destination free bytes and isolated per-export TMPDIR allocated blocks; it always stops/joins. `evidence/export-observation.json` (`arcanum-export-observation-v1`) is frozen BEFORE every writer hashes evidence and bound separately as `exportBudget.observation`. Free-space drops and output bytes are never temporary usage. Production Godot/.NET caches are not fully isolated by TMPDIR: observations therefore say `temporaryMeasurementStatus=unavailable`, `observedTemporaryBytes=null`, while preserving real sampled filesystem low water and coverage limitations. Compatibility `temporaryLowWaterBytes=0` means unavailable here, NOT a measured zero. Only successful qualified fully attributed observations with candidate/manifest/observation provenance update history; failed finalization/publication cannot advance it. Legacy v2 remains read-only compatible, and its old temporary estimate is never trusted observed history. ART remains next-export-only guidance, not rebuilt here.

## Boundaries

A local generated/exported artifact is never implicitly accepted, merged, or published. Real artifact deletion requires a separately accepted fully proven plan; this candidate intentionally performs no deletion. Human review, original-data checks, graphical inspection, physical controller tests, Bluetooth reconnect/drift, all axes/settings, exhaustive stability, and original parity remain separate evidence.
