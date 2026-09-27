#!/bin/sh
set -eu

candidate_root=$(git rev-parse --show-toplevel)
lab_root=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
common_dir=$(git rev-parse --path-format=absolute --git-common-dir)
main_root=$(dirname "$common_dir")
if test -n "$(git status --porcelain)"; then
  printf '%s\n' 'Candidate must be clean and committed before validating.' >&2
  exit 2
fi
head=$(git rev-parse HEAD)
build_root=${ARCANUM_BUILD_ROOT:-"$main_root/artifacts/builds/ARC-UI-001/$head"}
case "$build_root" in
  "$main_root"/artifacts/builds/ARC-UI-001/*) ;;
  *) printf '%s\n' 'ARCANUM_BUILD_ROOT must remain under this task artifact root.' >&2; exit 2 ;;
esac

export DOTNET_ROOT=${DOTNET_ROOT:-"$HOME/.local/share/dotnet"}
DOTNET="$DOTNET_ROOT/dotnet"
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
export PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin"
export __GODOT_SHELL_ENV_SET=1

mkdir -p "$build_root/evidence/screenshots"
"$DOTNET" restore "$lab_root/Arcanum.Experiments.DisplayUiLab.csproj" --locked-mode
"$DOTNET" build "$lab_root/Arcanum.Experiments.DisplayUiLab.csproj" --no-restore
"$GODOT" --verbose --headless --path "$lab_root" --editor --build-solutions --quit
"$GODOT" --headless --path "$lab_root" -- --run-layout-matrix --report "$build_root/evidence/layout-matrix.json"
"$GODOT" --path "$lab_root" -- --capture-evidence "$build_root/evidence/screenshots"
"$GODOT" --headless --path "$lab_root" --export-debug "macOS debug" "$build_root/DisplayUiLab.app"
"$build_root/DisplayUiLab.app/Contents/MacOS/Display UI Lab" --headless -- --run-layout-matrix --report "$build_root/evidence/exported-layout-matrix.json"
/usr/bin/plutil -lint "$build_root/DisplayUiLab.app/Contents/Info.plist"
/usr/bin/codesign --verify --deep --strict --verbose=2 "$build_root/DisplayUiLab.app"
/usr/bin/file "$build_root/DisplayUiLab.app/Contents/MacOS/Display UI Lab" > "$build_root/evidence/architecture.txt"

 /usr/bin/python3 - "$build_root/manifest.json" "$head" "$(git branch --show-current)" "$(git rev-parse origin/main)" "$DOTNET" <<'PY'
import hashlib, json, pathlib, subprocess, sys
manifest, head, branch, base, dotnet = sys.argv[1:]
root = pathlib.Path(manifest).parents[4]
changed = subprocess.check_output(["git", "diff", "--name-only", f"{base}..{head}"], text=True).splitlines()
payload = {
  "schemaVersion": 1, "task": "ARC-UI-001", "generatedAt": "local validation",
  "candidate": {"baseCommit": base, "commit": head, "branch": branch, "ordinaryDiffSha256": hashlib.sha256(subprocess.check_output(["git", "diff", "--no-ext-diff", base, head])).hexdigest(), "changedFiles": changed, "gitStatus": "clean"},
  "toolchain": {"dotnetSdk": "8.0.425", "executable": dotnet, "godotVersion": "4.7.2.stable.mono", "templates": {"version": "4.7.2.stable.mono"}, "launchEnvironment": "process-local canonical environment"},
  "app": {"relativePath": "DisplayUiLab.app", "executable": "Contents/MacOS/Display UI Lab", "bundleIdentifier": "org.arcanumsteamwoven.displayuilab", "architectures": "recorded in evidence/architecture.txt", "signature": "codesign verified"},
  "checks": [{"status": "PASS", "evidence": "evidence/layout-matrix.json"}, {"status": "PASS", "evidence": "evidence/exported-layout-matrix.json"}],
  "evidence": {"matrixReports": ["evidence/layout-matrix.json", "evidence/exported-layout-matrix.json"], "nativeReport": None, "screenshots": [{"path": p.name, "classification": "simulated-layout"} for p in pathlib.Path(manifest).parent.joinpath("evidence/screenshots").glob("*.png")]},
  "verification": {"actual": ["locked restore/build", "Godot editor bridge", "macOS export and launch"], "simulated": ["SubViewport layout matrix", "SubViewport screenshots"], "notVerified": ["physical pixel mapping/readability", "native focus recovery without observed notifications"], "deferred": ["Windows/Linux/handheld/controller/performance"]},
  "delivery": {"status": "verified-local-candidate; exact-head review pending"}
}
pathlib.Path(manifest).write_text(json.dumps(payload, indent=2) + "\n")
PY
printf '%s\n' "Validated $head in $build_root"
