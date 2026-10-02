#!/bin/sh
# Bounded exact-head ARC-DEV-001 Terrain export and success-only registration.
set -eu

candidate_root=$(git rev-parse --show-toplevel)
project_root=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
common_dir=$(git rev-parse --path-format=absolute --git-common-dir)
main_root=$(dirname "$common_dir")
head=$(git rev-parse HEAD)
task=${ARCANUM_TERRAIN_EXPORT_TASK:-ARC-DEV-001}
case "$task" in ARC-DEV-001) artifact_dir=builds ;; ARC-WORLD-001|ARC-GPAD-004) artifact_dir=world-builds ;; *) printf '%s\n' 'Unsupported terrain export task.' >&2; exit 2 ;; esac
build_root=${ARCANUM_BUILD_ROOT:-"$main_root/artifacts/$artifact_dir/$task/$head"}
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
    tools/terrain-viewer/Core/packages.lock.json tools/terrain-viewer/Godot/packages.lock.json
  /bin/rm -f "$project_root/SyntheticActorFixture.cs.uid" "$project_root/TerrainDiagnosticHud.cs.uid" \
    "$project_root/DiagnosticInputSettingsStore.cs.uid" "$project_root/DiagnosticInputSettingsPanel.cs.uid" "$project_root/TerrainViewerWorld.cs.uid" \
    "$project_root/TerrainViewerWorldLifecycleTest.cs.uid" "$project_root/TerrainViewerWorldSelfTest.cs.uid" "$project_root/TerrainViewerWorldValidation.cs.uid" \
    "$project_root/DiagnosticZoomSettingsStore.cs.uid" "$project_root/TerrainViewerWorldZoomTest.cs.uid"
}
trap cleanup_candidate EXIT HUP INT TERM
run_log() { name=$1; shift; "$@" >"$build_root/evidence/$name" 2>&1; }
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
run_log codesign.log /usr/bin/codesign --verify --deep --strict --verbose=2 "$build_root/ArcanumTerrainViewer.app"
cleanup_candidate
printf '%s\n' "Validated $head; observation and manifest finalization pending."
