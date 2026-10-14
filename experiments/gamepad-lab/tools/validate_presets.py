#!/usr/bin/env python3
"""Closed ARC-GPAD-005 validate phase of the existing leased export endpoint."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import plistlib
import subprocess
import sys
import uuid


def main() -> int:
    root, lab, package = (Path(value).resolve() for value in sys.argv[1:4])
    head = sys.argv[4]
    if (os.environ.get('ARCANUM_EXPORT_LEASE_HELD') != '1'
            or os.environ.get('ARCANUM_EXPORT_PHASE') != 'validate'
            or os.environ.get('ARCANUM_GAMEPAD_EXPORT_TASK') != 'ARC-GPAD-005'):
        raise ValueError('existing leased ARC-GPAD-005 validate phase required')
    os.chdir(root)
    sys.path.insert(0, str(root))
    from tools.local_dev.gamepad_export_gates import run_gate, validate_reports
    from tools.local_dev.gamepad_matrix_authority import input_map
    sdk = Path.home() / '.local/share/dotnet'
    dotnet = str(sdk / 'dotnet')
    godot = '/Applications/Godot_mono.app/Contents/MacOS/Godot'
    os.environ.update(DOTNET_ROOT=str(sdk), PATH=str(sdk) + ':/usr/bin:/bin:/usr/sbin:/sbin',
                      __GODOT_SHELL_ENV_SET='1', ARCANUM_GAMEPAD_CANDIDATE=head)
    if subprocess.check_output([dotnet, '--version'], text=True).strip() != '8.0.425':
        raise ValueError('canonical SDK mismatch')
    if not subprocess.check_output([godot, '--version'], text=True).startswith('4.7.2.stable.mono.'):
        raise ValueError('canonical Godot mismatch')
    evidence = package / 'evidence'
    evidence.mkdir(parents=True, exist_ok=True)
    isolated = evidence / 'isolated-settings.json'
    if isolated.exists():
        raise ValueError('isolated storage already exists')
    os.environ['GAMEPAD_LAB_SETTINGS_TEST_PATH'] = str(isolated)
    def gate(name: str, *command: str) -> None:
        if run_gate(evidence, head, name, list(command)):
            raise ValueError('actual command failed: ' + name)
    def persistence(origin: str, command: list[str]) -> None:
        gate(origin + '-preset-persistence.log', sys.executable, '-B', str(lab / 'tools/verify_persistence.py'),
             '--head', head, '--report', str(evidence / (origin + '-preset-persistence.json')),
             '--work', str(evidence / (origin + '-persistence')), '--', *command)
    try:
        gate('sdk-info.log', dotnet, '--info')
        gate('locked-restore.log', dotnet, 'restore', str(lab / 'GamepadLab.csproj'), '--locked-mode')
        gate('locked-tests-restore.log', dotnet, 'restore', str(lab / 'Tests/GamepadLab.Tests.csproj'), '--locked-mode')
        gate('release-build.log', dotnet, 'build', str(lab / 'GamepadLab.csproj'), '-c', 'Release', '--no-restore')
        gate('core-harness.log', dotnet, 'run', '--project', str(lab / 'Tests/GamepadLab.Tests.csproj'), '-c', 'Release', '--no-restore', '--', '--all')
        editor_inputs = input_map(root)
        gate('editor-import.log', godot, '--headless', '--editor', '--path', str(lab), '--quit')
        gate('sdk-editor-bridge.log', godot, '--headless', '--verbose', '--editor', '--path', str(lab), '--build-solutions', '--quit')
        bridge = (evidence / 'sdk-editor-bridge.log').read_text()
        if ("Found .NET Sdk version '8.0.425': " + str(sdk / 'sdk/8.0.425') not in bridge
                or 'Running:  ' + dotnet + ' build ' not in bridge
                or '/opt/homebrew/' in bridge or 'ERROR:' in bridge):
            raise ValueError('editor bridge selected wrong SDK/executable or logged an error')
        if input_map(root) != editor_inputs:
            raise ValueError('complete source inputs changed during editor bridge')
        gate('debug-build.log', dotnet, 'build', str(lab / 'GamepadLab.csproj'), '-c', 'Debug', '--no-restore')
        source = [godot, '--path', str(lab)]
        gate('godot-self-test.log', *source, '--headless', '--', '--self-test')
        gate('source-preset-matrix.log', *source, '--', '--preset-matrix', str(evidence / 'source-preset-matrix.json'), str(evidence / 'source-images'))
        persistence('source', source + ['--headless'])
        gate('shared-input-regressions.log', sys.executable, '-B', str(__file__), '--shared', str(root), dotnet)
        gate('local-lifecycle-tests.log', sys.executable, '-B', '-m', 'unittest',
             'tools.local_dev.test_local_dev', 'tools.local_dev.test_world_zoom_dev', 'tools.local_dev.test_grid_dev',
             'tools.local_dev.test_grid_authority', 'tools.local_dev.test_human_dev', 'tools.local_dev.test_world_runner',
             'tools.local_dev.test_world_dev', 'tools.local_dev.test_gamepad_export_gates', 'tools.local_dev.test_gamepad_matrix_authority')
        gate('export.log', godot, '--headless', '--verbose', '--path', str(lab), '--export-debug', 'macOS', str(package / 'GamepadLab.app'))
        app = package / 'GamepadLab.app'
        with (app / 'Contents/Info.plist').open('rb') as stream:
            executable = plistlib.load(stream)['CFBundleExecutable']
        if not isinstance(executable, str) or Path(executable).name != executable:
            raise ValueError('unsafe bundle executable')
        binary = str(app / 'Contents/MacOS' / executable)
        before = {str(p.relative_to(app)): hashlib.sha256(p.read_bytes()).hexdigest() for p in app.rglob('*') if p.is_file()}
        gate('exported-smoke.log', binary, '--headless', '--', '--smoke', str(evidence / 'exported-smoke.json'))
        gate('exported-preset-matrix.log', binary, '--', '--preset-matrix', str(evidence / 'exported-preset-matrix.json'), str(evidence / 'exported-images'))
        persistence('exported', [binary, '--headless'])
        after = {str(p.relative_to(app)): hashlib.sha256(p.read_bytes()).hexdigest() for p in app.rglob('*') if p.is_file()}
        if before != after:
            raise ValueError('runtime changed bundle payload')
        (evidence / 'ordinary-launch-not-verified.json').write_text(json.dumps(dict(schema='arcanum-ordinary-launch-v1', outcome='NOT_VERIFIED', reason='automated matrix is not ordinary or physical controller evidence')) + '\n')
        gate('plist-lint.log', '/usr/bin/plutil', '-lint', str(app / 'Contents/Info.plist'))
        gate('codesign.log', '/usr/bin/codesign', '--verify', '--deep', '--strict', '--verbose=2', str(app))
        validate_reports(evidence, head)
        subprocess.run(['git', 'diff', '--check'], check=True)
        if subprocess.check_output(['git', 'status', '--porcelain'], text=True):
            raise ValueError('candidate changed during export')
        print('ARC-GPAD-005 validation gates passed; endpoint owns freeze/finalize/registration')
        return 0
    finally:
        # Only this runner's explicitly isolated preference is disposable.
        if isolated.exists():
            isolated.unlink()


def shared(root: Path, dotnet: str) -> int:
    os.chdir(root)
    sys.path.insert(0, str(root))
    from tools.local_dev.gamepad_export_gates import engine_error
    def native(command: list[str], marker: str | None = None) -> None:
        result = subprocess.run(command, check=False, text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=180)
        print(result.stdout, flush=True)
        result.check_returncode()
        if engine_error(result.stdout.encode()) or marker is not None and marker not in result.stdout:
            raise ValueError('shared production scene regression observation missing or failed')
    projects = ('tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj',)
    for project in projects:
        subprocess.run([dotnet, 'restore', project, '--locked-mode'], check=True)
        subprocess.run([dotnet, 'test', project, '-c', 'Release', '--no-restore', '--filter', 'TestCategory!=RealData'], check=True)
    viewer = root / 'tools/terrain-viewer/Godot'
    project = viewer / 'Arcanum.TerrainViewer.csproj'
    subprocess.run([dotnet, 'restore', str(project), '--locked-mode'], check=True)
    subprocess.run([dotnet, 'build', str(project), '-c', 'Debug', '--no-restore'], check=True)
    godot = '/Applications/Godot_mono.app/Contents/MacOS/Godot'
    temporary = Path(os.environ['TMPDIR'])

    native([godot, '--headless', '--editor', '--path', str(viewer), '--quit'])
    for flags in (['--headless'], []):
        owned = uuid.uuid4().hex
        os.environ['WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH'] = str(temporary / ('world-input-' + owned + '.json'))
        os.environ['WORLD_DIAGNOSTIC_ZOOM_TEST_PATH'] = str(temporary / ('world-zoom-' + owned + '.json'))
        native([godot, *flags, '--path', str(viewer), '--', '--self-test'],
               'ARC-WORLD-001 self-test PASS combined scene/control/lifecycle')
    subprocess.run([dotnet, 'restore', 'Arcanum.Steamwoven.sln', '--locked-mode'], check=True)
    subprocess.run([dotnet, 'build', 'Arcanum.Steamwoven.sln', '--no-restore'], check=True)
    subprocess.run([dotnet, 'test', 'Arcanum.Steamwoven.sln', '--no-build', '--filter', 'TestCategory!=RealData'], check=True)
    return 0


if __name__ == '__main__':
    if len(sys.argv) == 4 and sys.argv[1] == '--shared':
        raise SystemExit(shared(Path(sys.argv[2]), sys.argv[3]))
    if len(sys.argv) != 5:
        raise SystemExit('candidate root, lab, package, exact head required')
    raise SystemExit(main())
