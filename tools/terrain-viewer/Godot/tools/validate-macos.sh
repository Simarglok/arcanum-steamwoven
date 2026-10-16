#!/bin/sh
# Bounded exact-head ARC-DEV-001 Terrain export and success-only registration.
set -eu

candidate_root=$(git rev-parse --show-toplevel)
project_root=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
common_dir=$(git rev-parse --path-format=absolute --git-common-dir)
main_root=$(dirname "$common_dir")
head=$(git rev-parse HEAD)
task=${ARCANUM_TERRAIN_EXPORT_TASK:-ARC-DEV-001}
case "$task" in ARC-DEV-001) artifact_dir=builds ;; ARC-WORLD-001|ARC-GPAD-004|ARC-RENDER-006|ARC-WORLD-003|ARC-RENDER-007|ARC-INPUT-001|ARC-WORLD-007) artifact_dir=world-builds ;; *) printf '%s\n' 'Unsupported terrain export task.' >&2; exit 2 ;; esac
build_root=${ARCANUM_BUILD_ROOT:-"$main_root/artifacts/$artifact_dir/$task/$head"}
regression_task=$task
# ARC-INPUT-001 (diagnostic palette) is delivered on the whole-core AREA build and
# keeps every ARC-RENDER-007 gate; its palette matrices run in the synthetic gates.
# ARC-WORLD-007 (LMB click-to-move) likewise; its route matrix runs in the area-grid probe.
area_task=0
case "$task" in ARC-RENDER-007|ARC-INPUT-001|ARC-WORLD-007) area_task=1; regression_task=ARC-WORLD-003 ;; esac
if test "$build_root" != "$main_root/artifacts/$artifact_dir/$task/$head"; then printf '%s\n' 'Build root must bind the closed exact-head terrain task.' >&2; exit 2; fi
if test -n "$(git status --porcelain)"; then printf '%s\n' 'Candidate must be clean and committed before exporting.' >&2; exit 2; fi
if test "${ARCANUM_EXPORT_LEASE_HELD:-}" != 1; then
  : "${ARCANUM_LOCAL_ROOT_ID:?ARCANUM_LOCAL_ROOT_ID must identify the configured ignored artifact root}"
  : "${TMPDIR:?TMPDIR must identify the effective temporary destination}"
  set --
  if test -n "${ARCANUM_OUTPUT_BYTES:-}"; then
    : "${ARCANUM_ESTIMATE_MANIFEST:?a single complete measured payload manifest is required}"
    set -- --output-bytes "$ARCANUM_OUTPUT_BYTES" --estimate-manifest "$ARCANUM_ESTIMATE_MANIFEST"
  fi
  cd "$candidate_root"
  exec /usr/bin/env ARCANUM_EXPORT_LEASE_HELD=1 /usr/bin/python3 -B -m tools.local_dev.export_endpoint \
    --tool terrain --root-id "$ARCANUM_LOCAL_ROOT_ID" --task "$task" --candidate "$head" \
    --manifest "$build_root/manifest.json" --temporary-root "$TMPDIR" \
    --write-root "$candidate_root" --write-root "$HOME/.nuget/packages" \
    --write-root "$HOME/Library/Application Support/Godot" "$@" -- /bin/sh "$0"
fi
case "${ARCANUM_EXPORT_PHASE:-}" in
  finalize)
    exec /usr/bin/python3 -B "$project_root/tools/terrain_write_manifest.py" "$candidate_root" "$main_root" "$build_root" "$head" "$build_root/ArcanumTerrainViewer.app/Contents/MacOS/Arcanum Terrain Viewer" "$task" ;;
  validate) ;;
  *) printf '%s\n' 'Endpoint validate/finalize phase required.' >&2; exit 2 ;;
esac

DOTNET_ROOT=${DOTNET_ROOT:-"$HOME/.local/share/dotnet"}
DOTNET="$DOTNET_ROOT/dotnet"
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
export DOTNET_ROOT PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin" __GODOT_SHELL_ENV_SET=1
if test ! -x "$DOTNET" || test ! -x "$GODOT"; then printf '%s\n' 'Canonical SDK 8.0.425 or Godot 4.7.2 mono is unavailable.' >&2; exit 2; fi
mkdir -p "$build_root/evidence"
cleanup_candidate() {
  git -C "$candidate_root" restore -- src/Arcanum.Data/packages.lock.json src/Arcanum.Presentation/packages.lock.json src/Arcanum.Input/packages.lock.json \
    src/Arcanum.Simulation/packages.lock.json tools/terrain-viewer/Core/packages.lock.json tools/terrain-viewer/Godot/packages.lock.json
  # Godot .uid files are tracked; the export must leave them unchanged.
}
trap cleanup_candidate EXIT HUP INT TERM
run_log() {
  name=$1; shift
  if test "$task" = ARC-WORLD-003 || test "$area_task" = 1; then
    (cd "$candidate_root" && /usr/bin/python3 -B -m tools.local_dev.grid_export_gates \
      --evidence "$build_root/evidence" --head "$head" --task "$task" --log "$name" -- "$@")
  else
    "$@" >"$build_root/evidence/$name" 2>&1
  fi
}
# One opt-in native AREA probe per call, with owned isolated preferences.
area_probe() {
  log=$1; probe=$2; oracle_variable=$3; oracle=$4; shift 4
  preferences=$(/usr/bin/mktemp -d "$TMPDIR/area-probe.XXXXXX")
  run_log "$log" /usr/bin/env "$probe=1" "$oracle_variable=$oracle" \
    "WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH=$preferences/input.json" "WORLD_DIAGNOSTIC_ZOOM_TEST_PATH=$preferences/zoom.json" \
    "$@" -- --self-test
}
run_log locked-restore.log "$DOTNET" restore "$project_root/Arcanum.TerrainViewer.sln" --locked-mode
run_log locked-build.log "$DOTNET" build "$project_root/Arcanum.TerrainViewer.sln" --no-restore
run_log tests.log "$DOTNET" test "$candidate_root/tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj" --no-build
if test "$task" != ARC-DEV-001; then
  run_log source-headless.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --kind synthetic --headless
  run_log source-graphical.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --kind synthetic
else
  run_log source-headless.log "$GODOT" --headless --path "$project_root" -- --self-test
  run_log source-graphical.log "$GODOT" --path "$project_root" -- --self-test
fi
if test "$task" != ARC-DEV-001; then
  run_log source-world-original.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root"
  run_log source-world-persistence.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --kind persistence
fi
if test "$task" = ARC-RENDER-006 || test "$task" = ARC-WORLD-003 || test "$area_task" = 1; then
  run_log source-human-original.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --task "$regression_task" --kind human --timeout 180
  run_log human-metadata.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --task "$regression_task" --kind human-metadata --timeout 180
fi
if test "$task" = ARC-WORLD-003 || test "$area_task" = 1; then
  run_log source-grid-original.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --task "$regression_task" --kind grid-original --timeout 180
fi
if test "$area_task" = 1; then
  run_log source-area-original.log /usr/bin/python3 -B "$project_root/tools/area_validate.py" --candidate-root "$candidate_root"
  # Independent frozen oracles are generated before any native readback.
  oracles=$(/usr/bin/mktemp -d "$TMPDIR/area-oracles.XXXXXX")
  /usr/bin/python3 -B "$project_root/tools/area_motion_oracle.py" --output "$oracles/motion.json" >/dev/null
  /usr/bin/python3 -B "$project_root/tools/area_owner_seam_oracle.py" --output "$oracles/seams.json" >/dev/null
  /usr/bin/python3 -B "$project_root/tools/area_nearest_oracle.py" --output "$oracles/nearest.json" >/dev/null
  area_probe source-area-grid.log ARC_AREA_GRID_PROBE ARC_AREA_MOTION_ORACLE "$oracles/motion.json" "$GODOT" --path "$project_root"
  area_probe source-area-seams.log ARC_AREA_OWNER_SEAM_PROBE ARC_AREA_SEAM_ORACLE "$oracles/seams.json" "$GODOT" --path "$project_root"
  area_probe source-area-nearest.log ARC_AREA_NEAREST_PROBE ARC_AREA_NEAREST_ORACLE "$oracles/nearest.json" "$GODOT" --path "$project_root"
fi
run_log export.log "$GODOT" --verbose --headless --path "$project_root" --export-debug 'macOS debug' "$build_root/ArcanumTerrainViewer.app"
/usr/bin/grep -F "$DOTNET" "$build_root/evidence/export.log" >/dev/null
/usr/bin/grep -F '8.0.425' "$build_root/evidence/export.log" >/dev/null
app="$build_root/ArcanumTerrainViewer.app/Contents/MacOS/Arcanum Terrain Viewer"
test -x "$app"
if test "$task" != ARC-DEV-001; then
  run_log exported-headless.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --app "$app" --kind synthetic --headless
  run_log exported-graphical.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --app "$app" --kind synthetic
else
  run_log exported-headless.log "$app" --headless -- --self-test
  run_log exported-graphical.log "$app" -- --self-test
fi
if test "$task" != ARC-DEV-001; then
  run_log exported-world-original.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --app "$app"
  run_log exported-world-persistence.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --app "$app" --kind persistence
  run_log hostfxr.log /usr/bin/python3 -B "$project_root/tools/hostfxr_validate.py" "$build_root/ArcanumTerrainViewer.app"
fi
if test "$task" = ARC-RENDER-006 || test "$task" = ARC-WORLD-003 || test "$area_task" = 1; then
  run_log exported-human-original.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --app "$app" --task "$regression_task" --kind human --timeout 180
fi
if test "$task" = ARC-WORLD-003 || test "$area_task" = 1; then
  run_log exported-grid-original.log /usr/bin/python3 -B "$project_root/tools/world_validate.py" --candidate-root "$candidate_root" --app "$app" --task "$regression_task" --kind grid-original --timeout 180
fi
if test "$area_task" = 1; then
  run_log exported-area-original.log /usr/bin/python3 -B "$project_root/tools/area_validate.py" --candidate-root "$candidate_root" --app "$app"
  area_probe exported-area-grid.log ARC_AREA_GRID_PROBE ARC_AREA_MOTION_ORACLE "$oracles/motion.json" "$app"
  area_probe exported-area-seams.log ARC_AREA_OWNER_SEAM_PROBE ARC_AREA_SEAM_ORACLE "$oracles/seams.json" "$app"
  area_probe exported-area-nearest.log ARC_AREA_NEAREST_PROBE ARC_AREA_NEAREST_ORACLE "$oracles/nearest.json" "$app"
fi
run_log codesign.log /usr/bin/codesign --verify --deep --strict --verbose=2 "$build_root/ArcanumTerrainViewer.app"
cleanup_candidate
printf '%s\n' "Validated $head; observation and manifest finalization pending."
