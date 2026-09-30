# Local diagnostic launch and build retention

`tools/arcanum-local` is a Python-standard-library command for choosing only explicitly configured local diagnostic inputs and ignored artifact roots. It is not a game launcher, exporter, discovery scanner, cleaner, or acceptance authority.

## Configuration and privacy

The only persistent files are owner-only (0600), atomically replaced JSON documents:

    ~/Library/Application Support/Arcanum Steamwoven/config-v1.json
    ~/Library/Application Support/Arcanum Steamwoven/build-registry-v1.json

They have schemas `arcanum-local-config-v1` and `build-registry-v1`. They hold registered artifact-root identifiers, explicit original-input roles, a safe non-path source label, preferences, selection, and acceptance lifecycle records. They never belong in Git. Test runs may set `ARCANUM_LOCAL_HOME` to an owned temporary directory.

Configuration does not scan a machine. `configure discover --game-root PATH` examines only that caller-selected directory and the three known candidates `modules/Arcanum.dat`, `arcanum1.dat`, and `arcanum2.dat`, without following symlinks. `configure inputs` needs each original role explicitly. The original installation is never changed and paths are not printed unless an inventory has the explicit local-only `--show-paths` option.

    tools/arcanum-local list [--json]
    tools/arcanum-local configure discover --game-root PATH
    tools/arcanum-local configure inputs --module PATH --terrain-resource PATH --actor-resource PATH --prototypes PATH [--source-id LABEL]
    tools/arcanum-local configure add-artifact-root --id ID PATH

## Ordinary diagnostics launch

The supported logical mappings are ART `ARC-RENDER-001/002`, terrain `ARC-RENDER-003/004/005` and `ARC-DEV-001`, Gamepad `ARC-GPAD-001/002/003`, and Display `ARC-UI-001`. The resolver supports only their observed schema-v1 pointers and immutable `arcanum-build-manifest-v2`/`arcanum-build-current-v2` bindings. Unknown/mixed schemas, a missing or stale digest, an escape from a configured root, or more than one current pointer fails closed. It never chooses by modification time.

    tools/arcanum-local launch art --dry-run
    tools/arcanum-local launch gamepad --dry-run
    tools/arcanum-local launch display --dry-run
    tools/arcanum-local launch terrain --mode terrain-only --dry-run

Launch uses a process argv, never a shell: engine arguments, literal `--`, then application arguments. Its macOS equivalent is `open APP --args [engine flags] -- [application flags]`. Ordinary launch rejects smoke/self-test/matrix/capture/automated resize/input/fullscreen flags and cannot build or export. A per-tool advisory lock and exact configured executable check reject duplication by default; `--allow-duplicate` is the sole opt-in. It never kills processes or uses broad process-name matching.

Terrain actor diagnostic defaults are deliberately caller-labelled zero map/camera/offset anchors, `normal-uncached`, scale 100, shrunk false, Idle, facing 0, frame 0. This is diagnostic policy, not inferred world state or original-placement parity.

## Retention lifecycle

`builds inventory` is conservative and dry-run by default. It reports only configured roots and known candidate directories. A candidate is kept for unknown ownership, current/rollback/active/open-review/unmerged lifecycle, in-use state, symlink ambiguity, or any missing proof. A candidate is eligible only when containment, supported ownership, and explicit retired/disposable state are all proved.

    tools/arcanum-local builds inventory [--show-paths]
    tools/arcanum-local builds plan --output PLAN.json
    tools/arcanum-local builds apply --plan PLAN.json --accept-plan PLAN_ID
    tools/arcanum-local builds export-preflight --tool terrain --output-bytes BYTES [--temporary-bytes BYTES]
    tools/arcanum-local builds register-export --tool terrain --candidate ID --manifest MANIFEST.json
    tools/arcanum-local builds accept --tool terrain --candidate ID --manifest MANIFEST.json --authority REF --review REF
    tools/arcanum-local builds retire --candidate ID

A plan binds config/registry generations and digests. Apply validates the exact accepted ID and is deliberately no-op-safe until each payload has separately proven manifest-level ownership; it never deletes a candidate, manifest, evidence, worktree, toolkit, original install, SDK, cache, temporary directory, or unrelated path. Export registration requires an existing manifest with all recorded checks passing and creates lifecycle `exported`; it is not acceptance. Acceptance requires an exact manifest digest plus authority and review references, then atomically records the accepted candidate and one rollback. Accepted current/rollback entries cannot be retired.

Preflight requires a measured/trusted output estimate and reserves output + temporary + `max(2 GiB, one output)`. Without a trusted measurement it fails safely. This records only preflight; existing component validation remains responsible for exports, manifests, codesign, and all source/export checks.

## Boundaries

A local generated/exported artifact is never implicitly accepted, merged, or published. Real artifact deletion requires a separately accepted fully proven plan; this candidate intentionally performs no deletion. Human review, original-data checks, graphical inspection, physical controller tests, Bluetooth reconnect/drift, all axes/settings, exhaustive stability, and original parity remain separate evidence.
