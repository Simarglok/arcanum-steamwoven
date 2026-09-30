#!/bin/sh
# Complete, candidate-bound local validation for ARC-UI-001.
# This intentionally does no publication.  The ignored current.json is replaced
# atomically only by validation_support.py after every exact-head gate succeeds.
set -eu

candidate_root=$(git rev-parse --show-toplevel)
lab_root=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
common_dir=$(git rev-parse --path-format=absolute --git-common-dir)
main_root=$(dirname "$common_dir")
head=$(git rev-parse HEAD)
build_root=${ARCANUM_BUILD_ROOT:-"$main_root/artifacts/builds/ARC-UI-001/$head"}

case "$build_root" in
  "$main_root"/artifacts/builds/ARC-UI-001/"$head") ;;
  *) printf '%s\n' 'ARCANUM_BUILD_ROOT must be this task root keyed by the exact candidate HEAD.' >&2; exit 2 ;;
esac

if test -n "$(git status --porcelain)"; then
  printf '%s\n' 'Candidate must be clean and committed before validating.' >&2
  exit 2
fi
if test "${ARCANUM_EXPORT_LEASE_HELD:-}" != 1; then
  : "${ARCANUM_LOCAL_ROOT_ID:?ARCANUM_LOCAL_ROOT_ID must identify the configured ignored artifact root}"
  cd "$candidate_root"
  exec /usr/bin/env ARCANUM_EXPORT_LEASE_HELD=1 /usr/bin/python3 -B -m tools.local_dev.export_endpoint \
    --tool display --root-id "$ARCANUM_LOCAL_ROOT_ID" --task ARC-UI-001 --candidate "$head" \
    --manifest "$build_root/manifest.json" -- /bin/sh "$0"
fi

export DOTNET_ROOT=${DOTNET_ROOT:-"$HOME/.local/share/dotnet"}
DOTNET="$DOTNET_ROOT/dotnet"
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
SUPPORT="$lab_root/tools/validation_support.py"
REGISTRY="$HOME/.local/share/arcanum-steamwoven/toolchain.json"
export PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin"
export __GODOT_SHELL_ENV_SET=1

if test ! -x "$DOTNET" || test ! -x "$GODOT" || test ! -f "$REGISTRY"; then
  printf '%s\n' 'Canonical toolchain executable or registry is unavailable.' >&2
  exit 2
fi

mkdir -p "$build_root/evidence/screenshots"

run_log() {
  name=$1
  shift
  "$@" >"$build_root/evidence/$name" 2>&1
}

# The two direct commands are deliberately literal: no -p: overrides can mask
# a mismatch between the documented debug lock and a Godot/export lock.
run_log locked-restore.log "$DOTNET" restore "$lab_root/Arcanum.Experiments.DisplayUiLab.csproj" --locked-mode
run_log locked-build.log "$DOTNET" build "$lab_root/Arcanum.Experiments.DisplayUiLab.csproj" --no-restore
"/usr/bin/python3" "$SUPPORT" toolchain-report --registry "$REGISTRY" --dotnet-root "$DOTNET_ROOT" --output "$build_root/evidence/toolchain.json"

run_log editor-bridge.log "$GODOT" --verbose --headless --path "$lab_root" --editor --build-solutions --quit
/usr/bin/grep -F "$DOTNET" "$build_root/evidence/editor-bridge.log" >/dev/null
/usr/bin/grep -F "8.0.425" "$build_root/evidence/editor-bridge.log" >/dev/null
git diff --exit-code -- "$lab_root/packages.lock.json"

run_log source-layout-matrix.log "$GODOT" --headless --path "$lab_root" -- --run-layout-matrix --report "$build_root/evidence/layout-matrix.json"
run_log capture-evidence.log "$GODOT" --path "$lab_root" -- --capture-evidence "$build_root/evidence/screenshots"

for screenshot in \
  1280x720-200-dialogue.png \
  1920x1080-100-inventory.png \
  3440x1440-150-dialogue.png \
  3840x2160-200-settings-diagnostics.png
do
  test -s "$build_root/evidence/screenshots/$screenshot"
done

run_log export.log "$GODOT" --headless --path "$lab_root" --export-debug "macOS debug" "$build_root/DisplayUiLab.app"
git diff --exit-code -- "$lab_root/packages.lock.json"
app="$build_root/DisplayUiLab.app/Contents/MacOS/Display UI Lab"
test -x "$app"
run_log exported-layout-matrix.log "$app" --headless -- --run-layout-matrix --report "$build_root/evidence/exported-layout-matrix.json"

# Non-headless native smoke self-terminates and carries its own bounded frame
# waits.  The outer watchdog prevents a GUI/tooling regression from hanging CI.
"$app" -- --run-native-smoke --candidate "$head" --report "$build_root/evidence/native-smoke.json" >"$build_root/evidence/exported-native-smoke.log" 2>&1 &
native_pid=$!
(
  sleep 300
  if kill -0 "$native_pid" 2>/dev/null; then
    kill -TERM "$native_pid" 2>/dev/null || true
  fi
) &
watchdog_pid=$!
set +e
wait "$native_pid"
native_status=$?
set -e
kill "$watchdog_pid" 2>/dev/null || true
wait "$watchdog_pid" 2>/dev/null || true
test "$native_status" -eq 0
cp "$build_root/evidence/exported-native-smoke.log" "$build_root/evidence/exported-ordinary-launch.log"

run_log plist-lint.log /usr/bin/plutil -lint "$build_root/DisplayUiLab.app/Contents/Info.plist"
run_log plist-values.log /usr/bin/plutil -extract CFBundleIdentifier raw -o - "$build_root/DisplayUiLab.app/Contents/Info.plist"
/usr/bin/grep -Fx 'org.arcanumsteamwoven.displayuilab' "$build_root/evidence/plist-values.log" >/dev/null
/usr/bin/plutil -extract NSHighResolutionCapable raw -o - "$build_root/DisplayUiLab.app/Contents/Info.plist" >>"$build_root/evidence/plist-values.log"
/usr/bin/grep -Fx 'true' "$build_root/evidence/plist-values.log" >/dev/null
run_log architecture.txt /usr/bin/lipo -archs "$app"
/usr/bin/grep -E 'arm64.*x86_64|x86_64.*arm64' "$build_root/evidence/architecture.txt" >/dev/null
run_log codesign.log /usr/bin/codesign --verify --deep --strict --verbose=2 "$build_root/DisplayUiLab.app"

run_log root-locked-restore.log "$DOTNET" restore "$candidate_root/Arcanum.Steamwoven.sln" --locked-mode
run_log root-locked-build.log "$DOTNET" build "$candidate_root/Arcanum.Steamwoven.sln" --no-restore
run_log data-tests.log "$DOTNET" test "$candidate_root/tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj" --no-build --filter 'TestCategory!=RealData'
run_log presentation-tests.log "$DOTNET" test "$candidate_root/tests/Arcanum.Presentation.Tests/Arcanum.Presentation.Tests.csproj" --no-build --filter 'TestCategory!=RealData'
"/usr/bin/python3" "$SUPPORT" hygiene --candidate-root "$candidate_root" --output "$build_root/evidence/hygiene.json"

# validation_support rechecks clean status, reports, all exact screenshot paths,
# classifications, and hygiene before atomically updating only ARC-UI-001/current.json.
"/usr/bin/python3" "$SUPPORT" write-manifest --candidate-root "$candidate_root" --main-root "$main_root" --build-root "$build_root" --toolchain-report "$build_root/evidence/toolchain.json"
printf '%s\n' "Validated $head in $build_root"