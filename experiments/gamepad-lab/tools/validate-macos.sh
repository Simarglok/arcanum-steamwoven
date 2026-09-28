#!/bin/sh
# Candidate-bound local macOS validation for ARC-GPAD-002. No publication.
set -eu

candidate_root=$(git rev-parse --show-toplevel)
lab_root=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
common_dir=$(git rev-parse --path-format=absolute --git-common-dir)
main_root=$(dirname "$common_dir")
head=$(git rev-parse HEAD)
build_root=${ARCANUM_BUILD_ROOT:-"$main_root/artifacts/builds/ARC-GPAD-002/$head"}
case "$build_root" in "$main_root"/artifacts/builds/ARC-GPAD-002/"$head") ;; *) printf '%s\n' 'ARCANUM_BUILD_ROOT must be exact-head ARC-GPAD-002 root.' >&2; exit 2 ;; esac
if test -n "$(git status --porcelain)"; then printf '%s\n' 'Candidate must be clean and committed before validating.' >&2; exit 2; fi

DOTNET_ROOT=${DOTNET_ROOT:-"$HOME/.local/share/dotnet"}
DOTNET="$DOTNET_ROOT/dotnet"
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin"
export __GODOT_SHELL_ENV_SET=1
if test ! -x "$DOTNET" || test ! -x "$GODOT"; then printf '%s\n' 'Canonical SDK 8.0.425 or Godot 4.7.2 mono is unavailable.' >&2; exit 2; fi
mkdir -p "$build_root/evidence"
settings_test_path="$build_root/evidence/isolated-settings.json"
rm -f "$settings_test_path"
# Source self-test, exported smoke, and restart probes use this exact ignored lab
# file. The trap removes it on both success and failure, so validation neither
# reads nor replaces a local user's normal user:// preference.
trap 'rm -f "$settings_test_path"' EXIT HUP INT TERM
export GAMEPAD_LAB_SETTINGS_TEST_PATH="$settings_test_path"
run_log() { name=$1; shift; "$@" >"$build_root/evidence/$name" 2>&1; }

run_log sdk-info.log "$DOTNET" --info
run_log locked-restore.log "$DOTNET" restore "$lab_root/GamepadLab.csproj" --locked-mode
run_log locked-tests-restore.log "$DOTNET" restore "$lab_root/Tests/GamepadLab.Tests.csproj" --locked-mode
run_log release-build.log "$DOTNET" build "$lab_root/GamepadLab.csproj" -c Release --no-restore
run_log core-harness.log "$DOTNET" run --project "$lab_root/Tests/GamepadLab.Tests.csproj" -c Release --no-restore -- --all
run_log editor-import.log "$GODOT" --headless --editor --path "$lab_root" --quit
git -C "$candidate_root" restore -- experiments/gamepad-lab/packages.lock.json experiments/gamepad-lab/Core/packages.lock.json
run_log debug-build.log "$DOTNET" build "$lab_root/GamepadLab.csproj" -c Debug --no-restore
run_log godot-self-test.log "$GODOT" --headless --path "$lab_root" -- --self-test
git -C "$candidate_root" restore -- experiments/gamepad-lab/packages.lock.json experiments/gamepad-lab/Core/packages.lock.json
run_log export.log "$GODOT" --headless --path "$lab_root" --export-debug macOS "$build_root/GamepadLab.app"
git -C "$candidate_root" restore -- experiments/gamepad-lab/packages.lock.json experiments/gamepad-lab/Core/packages.lock.json
executable=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$build_root/GamepadLab.app/Contents/Info.plist")
app="$build_root/GamepadLab.app/Contents/MacOS/$executable"
test -x "$app"
run_log exported-smoke.log "$app" --headless -- --smoke "$build_root/evidence/exported-smoke.json"
run_log persistence-write.log "$app" --headless -- --persistence write "$build_root/evidence/persistence-write.json"
run_log persistence-verify-nondefault.log "$app" --headless -- --persistence verify-nondefault "$build_root/evidence/persistence-verify-nondefault.json"
run_log persistence-verify-default.log "$app" --headless -- --persistence verify-default "$build_root/evidence/persistence-verify-default.json"
# This intentionally does not use --headless: the exported macOS application
# performs its bounded capture/layout path, writes a PNG, and quits itself.
run_log ordinary-launch.log /usr/bin/python3 "$lab_root/tools/run_with_timeout.py" 30 "$app" -- --capture 1280x800 "$build_root/evidence/ordinary-launch.png"
test -s "$build_root/evidence/ordinary-launch.png"
if /usr/bin/grep -q '^ERROR:' "$build_root/evidence/ordinary-launch.log"; then printf '%s\n' 'Ordinary exported-app launch logged an engine error.' >&2; exit 1; fi
run_log plist-lint.log /usr/bin/plutil -lint "$build_root/GamepadLab.app/Contents/Info.plist"
run_log codesign.log /usr/bin/codesign --verify --deep --strict --verbose=2 "$build_root/GamepadLab.app"
git diff --check
/usr/bin/python3 "$lab_root/tools/write_manifest.py" "$candidate_root" "$main_root" "$build_root" "$head" "$app"
printf '%s\n' "Validated $head in $build_root"
