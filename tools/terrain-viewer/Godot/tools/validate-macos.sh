#!/bin/sh
# Bounded exact-head ARC-DEV-001 Terrain export and success-only registration.
set -eu

candidate_root=$(git rev-parse --show-toplevel)
project_root=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
common_dir=$(git rev-parse --path-format=absolute --git-common-dir)
main_root=$(dirname "$common_dir")
head=$(git rev-parse HEAD)
build_root=${ARCANUM_BUILD_ROOT:-"$main_root/artifacts/builds/ARC-DEV-001/$head"}
case "$build_root" in "$main_root"/artifacts/builds/ARC-DEV-001/"$head") ;; *) printf '%s\n' 'ARCANUM_BUILD_ROOT must be the exact-head ARC-DEV-001 root.' >&2; exit 2 ;; esac
if test -n "$(git status --porcelain)"; then printf '%s\n' 'Candidate must be clean and committed before exporting.' >&2; exit 2; fi
if test "${ARCANUM_EXPORT_LEASE_HELD:-}" != 1; then
  : "${ARCANUM_LOCAL_ROOT_ID:?ARCANUM_LOCAL_ROOT_ID must identify the configured ignored artifact root}"
  cd "$candidate_root"
  exec /usr/bin/env ARCANUM_EXPORT_LEASE_HELD=1 /usr/bin/python3 -B -m tools.local_dev.export_endpoint \
    --tool terrain --root-id "$ARCANUM_LOCAL_ROOT_ID" --task ARC-DEV-001 --candidate "$head" \
    --manifest "$build_root/manifest.json" -- /bin/sh "$0"
fi

DOTNET_ROOT=${DOTNET_ROOT:-"$HOME/.local/share/dotnet"}
DOTNET="$DOTNET_ROOT/dotnet"
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
export DOTNET_ROOT PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin" __GODOT_SHELL_ENV_SET=1
if test ! -x "$DOTNET" || test ! -x "$GODOT"; then printf '%s\n' 'Canonical SDK 8.0.425 or Godot 4.7.2 mono is unavailable.' >&2; exit 2; fi
mkdir -p "$build_root/evidence"
cleanup_candidate() {
  git -C "$candidate_root" restore -- src/Arcanum.Data/packages.lock.json src/Arcanum.Presentation/packages.lock.json \
    tools/terrain-viewer/Core/packages.lock.json tools/terrain-viewer/Godot/packages.lock.json
  /bin/rm -f "$project_root/SyntheticActorFixture.cs.uid" "$project_root/TerrainDiagnosticHud.cs.uid"
}
trap cleanup_candidate EXIT HUP INT TERM
run_log() { name=$1; shift; "$@" >"$build_root/evidence/$name" 2>&1; }
run_log locked-restore.log "$DOTNET" restore "$project_root/Arcanum.TerrainViewer.sln" --locked-mode
run_log locked-build.log "$DOTNET" build "$project_root/Arcanum.TerrainViewer.sln" --no-restore
run_log tests.log "$DOTNET" test "$candidate_root/tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj" --no-build
run_log source-headless.log "$GODOT" --headless --path "$project_root" -- --self-test
run_log source-graphical.log "$GODOT" --path "$project_root" -- --self-test
run_log export.log "$GODOT" --verbose --headless --path "$project_root" --export-debug 'macOS debug' "$build_root/ArcanumTerrainViewer.app"
/usr/bin/grep -F "$DOTNET" "$build_root/evidence/export.log" >/dev/null
/usr/bin/grep -F '8.0.425' "$build_root/evidence/export.log" >/dev/null
app="$build_root/ArcanumTerrainViewer.app/Contents/MacOS/Arcanum Terrain Viewer"
test -x "$app"
run_log exported-headless.log "$app" --headless -- --self-test
run_log exported-graphical.log "$app" -- --self-test
run_log codesign.log /usr/bin/codesign --verify --deep --strict --verbose=2 "$build_root/ArcanumTerrainViewer.app"
cleanup_candidate
/usr/bin/python3 "$project_root/tools/terrain_write_manifest.py" "$candidate_root" "$main_root" "$build_root" "$head" "$app"
printf '%s\n' "Validated and registered $head in $build_root"
