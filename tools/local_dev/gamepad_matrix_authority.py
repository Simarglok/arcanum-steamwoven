"""Fresh O5 authority and passive-observation checks shared by runner and gates.

Completion IDs authenticate reached original Require paths, not trusted PASS
strings. Source/build/runtime binding is mandatory. Historical unbound reports
cannot acquire this authority. Registration additionally requires clean inputs.
"""
from __future__ import annotations
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import uuid
from . import config

CAP = 134217728
PROOF = ['faces', 'modal-slots-target-grid', 'wheel', 'lifecycle-projection',
         'physical-targets', 'settings-widgets', 'lifecycle-controls', 'modal-text',
         'labels', 'scale-resize', 'pixels-release-settings']
PRODUCERS = ('LabPresetVerification.cs', 'LabPresetContractVerification.cs', 'LabPresetSafetyVerification.cs')
SCHEMA = 'arcanum-gamepad-matrix-authority-v1'


def digest(value: object) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest()


def file_pin(path: Path) -> dict:
    path = config.regular_file(path)
    h = hashlib.sha256(); size = 0
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1048576), b''):
            size += len(block); h.update(block)
    return dict(bytes=size, sha256=h.hexdigest())


def input_map(root: Path) -> dict:
    def git(*args): return subprocess.check_output(['git', '-C', str(root), *args], text=True)
    paths = git('ls-files', '-z', '--cached', '--others', '--exclude-standard').split('\0')
    files = {p: file_pin(root / p) for p in sorted(set(paths)) if p and (root / p).is_file()}
    return dict(workspace=str(root), head=git('rev-parse', 'HEAD').strip(), branch=git('branch', '--show-current').strip(),
                status=git('status', '--porcelain=v1', '--untracked-files=all'), files=files)


def inventory(directory: Path) -> dict:
    directory = config.canonical_existing(str(directory), directory=True)
    files = {str(p.relative_to(directory)): file_pin(p) for p in sorted(directory.rglob('*')) if p.is_file() or p.is_symlink()}
    if not files:
        raise ValueError('missing application build outputs')
    return dict(directory=str(directory), files=files)


def validate_map(value: object, head: str, stable: bool) -> dict:
    if not isinstance(value, dict) or value.get('head') != head or not isinstance(value.get('status'), str):
        raise ValueError('input map candidate mismatch')
    files = value.get('files')
    if not isinstance(files, dict) or not files or stable and value['status']:
        raise ValueError('clean complete candidate input map required')
    for path, pin in files.items():
        if (not isinstance(path, str) or Path(path).is_absolute() or '..' in Path(path).parts
                or not isinstance(pin, dict) or type(pin.get('bytes')) is not int or pin['bytes'] < 0
                or not isinstance(pin.get('sha256'), str) or not re.fullmatch('[0-9a-f]{64}', pin['sha256'])):
            raise ValueError('unsafe or malformed input map')
    for name in PRODUCERS:
        if 'experiments/gamepad-lab/Scripts/' + name not in files:
            raise ValueError('missing executed oracle source')
    for path in ('experiments/gamepad-lab/Scenes/Main.tscn', 'experiments/gamepad-lab/project.godot',
                 'experiments/gamepad-lab/GamepadLab.csproj', 'global.json', 'tools/local_dev/gamepad_matrix_authority.py'):
        if path not in files:
            raise ValueError('missing production/build/consumer input')
    if stable:
        # A blank dirty-status string alone is not stable-head authority.
        # Independently compare the complete tracked tree to immutable Git bytes.
        workspace = value.get('workspace')
        if not isinstance(workspace, str): raise ValueError('stable candidate workspace missing')
        root = config.canonical_existing(workspace, directory=True)
        tracked = subprocess.check_output(['git','-C',str(root),'ls-tree','-r','--name-only','-z',head], text=True).split('\0')
        if set(files) != {p for p in tracked if p}: raise ValueError('stable input map is not the complete committed tree')
        for path in files:
            data = subprocess.check_output(['git','-C',str(root),'show',head+':'+path])
            if files[path] != dict(bytes=len(data),sha256=hashlib.sha256(data).hexdigest()):
                raise ValueError('stable source bytes do not match candidate head')
    return value


def prepare(root: Path, evidence: Path, origin: str, head: str, command: list[str], load) -> dict:
    if origin not in ('source', 'exported') or not command or '--headless' in command:
        raise ValueError('actual graphical SOURCE/EXPORT invocation required')
    marker = command.index('--preset-matrix')
    if (command[marker + 1:marker + 3] != [str(evidence / (origin + '-preset-matrix.json')), str(evidence / (origin + '-images'))]
            or marker != len(command) - 3 and command[marker + 3:] != ['--o5-binding-probe']):
        raise ValueError('exact native matrix paths and supported scope required')
    build_name = 'debug-build.log.result.json' if origin == 'source' else 'export.log.result.json'
    build = load(evidence / build_name)
    mapping = validate_map(build.get('matrixBuild', {}).get('inputMap'), head, False)
    actual = input_map(root)
    if actual != mapping or build.get('outcome') != 'PASS' or type(build.get('exitCode')) is not int or build['exitCode'] != 0:
        raise ValueError('fresh successful build with unchanged complete inputs required')
    if origin == 'exported':
        from . import gamepad_export_locks
        gamepad_export_locks.verify_current(mapping)
        gamepad_export_locks.validate(evidence, mapping, build['matrixBuild'].get('exportLocks'),
                                     config.optional_authority_bytes(evidence / 'export.log') or b'')
    outputs = build['matrixBuild']['outputs']
    if inventory(Path(outputs['directory'])) != outputs:
        raise ValueError('application outputs changed since build')
    if origin == 'source' and command[:3] != [command[0], '--path', str(root / 'experiments/gamepad-lab')]:
        raise ValueError('source must run the actual production project')
    if origin == 'exported' and Path(command[0]).parent.parent.parent != Path(outputs['directory']):
        raise ValueError('exported must run the built app')
    runtime = dict(executable=str(config.regular_file(Path(command[0]))), executablePin=file_pin(Path(command[0])), outputs=outputs)
    binding = dict(nonce=uuid.uuid4().hex, inputMapSha256=digest(mapping),
                   buildReceiptSha256=file_pin(evidence / build_name)['sha256'], runtimeInventorySha256=digest(runtime))
    return dict(buildReceipt=build_name, inputMap=mapping, runtime=runtime, binding=binding)


def finish(evidence: Path, origin: str, head: str, command: list[str], context: dict, receipt: dict, load) -> None:
    root = Path(context['inputMap']['workspace'])
    if input_map(root) != context['inputMap'] or inventory(Path(context['runtime']['outputs']['directory'])) != context['runtime']['outputs'] or file_pin(Path(command[0])) != context['runtime']['executablePin']:
        raise ValueError('runtime or full inputs changed during matrix')
    if origin == 'exported':
        from . import gamepad_export_locks
        build = load(evidence / context['buildReceipt'])
        gamepad_export_locks.verify_current(context['inputMap'])
        gamepad_export_locks.validate(evidence, context['inputMap'], build['matrixBuild'].get('exportLocks'),
                                     config.optional_authority_bytes(evidence / 'export.log') or b'')
    report_name = origin + '-preset-matrix.json'
    report = load(evidence / report_name)
    if report.get('binding') != context['binding'] or report.get('origin') != origin or report.get('candidateCommit') != head:
        raise ValueError('actual producer/runtime binding mismatch')
    authority = dict(schema=SCHEMA, task='ARC-GPAD-005', candidateCommit=head, origin=origin,
                     rawReport=report_name, rawReportPin=file_pin(evidence / report_name),
                     buildReceipt=context['buildReceipt'], binding=context['binding'], runtime=context['runtime'],
                     runtimeReceipt=origin + '-preset-matrix.log.result.json',
                     runtimeReceiptPin=file_pin(evidence / (origin + '-preset-matrix.log.result.json')),
                     completeInputMapSha256=digest(context['inputMap']))
    with (evidence / (origin + '-matrix-authority.json')).open('x') as stream:
        json.dump(authority, stream, indent=2); stream.write('\n')


def validate_binding(evidence: Path, origin: str, head: str, report: dict, load, *, stable: bool) -> set[str]:
    name = origin + '-matrix-authority.json'; authority = load(evidence / name)
    raw = origin + '-preset-matrix.json'; runtime_name = origin + '-preset-matrix.log.result.json'
    build_name = 'debug-build.log.result.json' if origin == 'source' else 'export.log.result.json'
    if (authority.get('schema') != SCHEMA or authority.get('task') != 'ARC-GPAD-005'
            or authority.get('origin') != origin or authority.get('candidateCommit') != head
            or authority.get('rawReport') != raw or authority.get('rawReportPin') != file_pin(evidence / raw)
            or authority.get('buildReceipt') != build_name or authority.get('runtimeReceipt') != runtime_name
            or authority.get('runtimeReceiptPin') != file_pin(evidence / runtime_name)):
        raise ValueError('raw/build/runtime/origin/head authority rebinding')
    build = load(evidence / build_name); receipt = load(evidence / runtime_name)
    mapping = validate_map(build.get('matrixBuild', {}).get('inputMap'), head, stable)
    if build['matrixBuild'].get('inputMapSha256') != digest(mapping):
        raise ValueError('build input map digest mismatch')
    for value, gate in ((build, 'debug-build' if origin == 'source' else 'export'), (receipt, origin + '-preset-matrix')):
        log = config.optional_authority_bytes(evidence / (gate + '.log'))
        if (value.get('schema') != 'arcanum-gamepad-gate-result-v1' or value.get('task') != 'ARC-GPAD-005'
                or value.get('candidateCommit') != head or value.get('gate') != gate or value.get('outcome') != 'PASS'
                or value.get('log') != gate+'.log' or value.get('workspace') != mapping.get('workspace')
                or type(value.get('exitCode')) is not int or value['exitCode'] != 0 or value.get('engineError') is not False
                or log is None or value.get('logSha256') != hashlib.sha256(log).hexdigest() or value.get('logBytes') != len(log)
                or any(marker in log for marker in (b'ERROR:', b'SCRIPT ERROR:', b'Unhandled exception'))):
            raise ValueError('failed or unbound actual build/runtime receipt')
        from datetime import datetime, timezone
        try:
            start,end = (datetime.fromisoformat(value[k]) for k in ('startedAt','finishedAt'))
            if start.utcoffset()!=timezone.utc.utcoffset(start) or end.utcoffset()!=timezone.utc.utcoffset(end) or end<start:
                raise ValueError('ordered UTC build/runtime times required')
        except (KeyError,TypeError,ValueError) as error: raise ValueError('invalid build/runtime observation times') from error
    build_command = build.get('command')
    if not isinstance(build_command,list) or not build_command:
        raise ValueError('actual build invocation missing')
    if origin == 'source':
        if len(build_command)!=6 or build_command[1]!='build' or build_command[3:]!=['-c','Debug','--no-restore']:
            raise ValueError('actual Debug build invocation mismatch')
        project=Path(build_command[2]);project=project if project.is_absolute() else Path(mapping['workspace'])/project
        if project != Path(mapping['workspace'])/'experiments/gamepad-lab/GamepadLab.csproj':
            raise ValueError('build production project mismatch')
    elif '--export-debug' not in build_command or build_command[-1] != build['matrixBuild']['outputs'].get('directory'):
        raise ValueError('export artifact build invocation mismatch')
    binding = authority.get('binding'); runtime = authority.get('runtime')
    if (not isinstance(binding, dict) or binding != report.get('binding')
            or not isinstance(binding.get('nonce'), str) or not re.fullmatch('[0-9a-f]{32}', binding['nonce'])
            or binding.get('inputMapSha256') != digest(mapping) or binding['inputMapSha256'] != authority.get('completeInputMapSha256')
            or binding.get('buildReceiptSha256') != file_pin(evidence / build_name)['sha256']
            or not isinstance(runtime, dict) or binding.get('runtimeInventorySha256') != digest(runtime)
            or runtime.get('outputs') != build['matrixBuild'].get('outputs') or not runtime.get('executablePin')):
        raise ValueError('complete input/build/runtime digest mismatch')
    command = receipt.get('command')
    if (not isinstance(command, list) or not command or command[0] != runtime.get('executable')
            or '--headless' in command or '--layout-focus' in command or '--d4-context-prefix' in command
            or receipt.get('matrixBinding') != binding or receipt.get('workspace') != mapping.get('workspace')):
        raise ValueError('missing native invocation binding')
    outputs = runtime['outputs']
    if not isinstance(outputs,dict) or not isinstance(outputs.get('files'),dict) or not outputs['files']:
        raise ValueError('missing actual application outputs')
    if origin == 'source' and (command[:3] != [command[0],'--path',str(Path(mapping['workspace'])/'experiments/gamepad-lab')]
            or not {'GamepadLab.dll','GamepadLab.Core.dll','Arcanum.Input.dll'} <= set(outputs['files'])):
        raise ValueError('source project/application assembly identity mismatch')
    if origin == 'exported' and Path(command[0]).parent.parent.parent != Path(outputs.get('directory','')):
        raise ValueError('export runtime is not the bound packaged app')
    for pin in [runtime['executablePin'], *outputs['files'].values()]:
        if (not isinstance(pin,dict) or type(pin.get('bytes')) is not int or pin['bytes']<=0
                or not isinstance(pin.get('sha256'),str) or not re.fullmatch('[0-9a-f]{64}',pin['sha256'])):
            raise ValueError('malformed actual runtime pin')
    try:
        i = command.index('--preset-matrix')
        if command[i + 1:i + 3] != [str(evidence / raw), str(evidence / (origin + '-images'))]:
            # Staged read-only copies preserve original invocation paths. They
            # must have the exact original basenames and directory sibling.
            if Path(command[i+1]).name != raw or Path(command[i+2]).name != origin+'-images' or Path(command[i+1]).parent != Path(command[i+2]).parent:
                raise ValueError('matrix invocation paths mismatch')
        if command[i+3:] not in ([], ['--o5-binding-probe']): raise ValueError('unsupported matrix scope')
    except (ValueError, IndexError) as error:
        raise ValueError('exact native matrix invocation missing') from error
    env = receipt.get('matrixEnvironment')
    if not isinstance(env, dict) or env.get('ARCANUM_GAMEPAD_CANDIDATE') != head or not env.get('GAMEPAD_LAB_SETTINGS_TEST_PATH'):
        raise ValueError('missing isolated runtime environment')
    expected_env = {'ARCANUM_GAMEPAD_MATRIX_NONCE': binding['nonce'], 'ARCANUM_GAMEPAD_INPUT_MAP_SHA256': binding['inputMapSha256'],
                    'ARCANUM_GAMEPAD_BUILD_RECEIPT_SHA256': binding['buildReceiptSha256'], 'ARCANUM_GAMEPAD_RUNTIME_SHA256': binding['runtimeInventorySha256']}
    if any(env.get(k) != v for k,v in expected_env.items()): raise ValueError('runtime environment binding mismatch')
    from datetime import datetime
    try:
        start,end = (datetime.fromisoformat(receipt[k]) for k in ('startedAt','finishedAt'))
        build_end = datetime.fromisoformat(build['finishedAt'])
        if start.utcoffset() is None or end < start or start < build_end or (end-start).total_seconds()>420:
            raise ValueError('runtime time budget/build order mismatch')
    except (ValueError, KeyError, TypeError) as error: raise ValueError('invalid actual runtime times') from error
    required = {'evidence/'+n for n in (name, build_name, runtime_name, raw)}
    if origin == 'exported':
        from . import gamepad_export_locks
        required |= gamepad_export_locks.validate(evidence, mapping, build['matrixBuild'].get('exportLocks'),
                                                 config.optional_authority_bytes(evidence / 'export.log') or b'')
    return required


def number(value: object) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError('nonfinite geometry')
    return float(value)


def enclosed(a: dict, b: dict) -> bool:
    for r in (a,b):
        for k in ('x','y','width','height'): number(r[k])
        if r['width'] <= 0 or r['height'] <= 0: raise ValueError('adverse geometry extent')
    return b['x'] >= a['x']-.02 and b['y'] >= a['y']-.02 and b['x']+b['width'] <= a['x']+a['width']+.02 and b['y']+b['height'] <= a['y']+a['height']+.02


def validate_observations(report: dict) -> None:
    table = report['witnesses']; cache = {}
    def resolve(v):
        while isinstance(v,dict) and '$witness' in v:
            key=v['$witness']
            if key not in cache: cache[key]=json.loads(table[key])
            v=cache[key]
        return v
    rows = report['tuples']; paired = {}
    for row in rows:
        ui = row['uiScalePercent']; viewport = dict(x=0,y=0,width=row['width'],height=row['height'])
        # Test-owned literal semantic table; never consult the production binding tables.
        normal = ['interact','inspect','context','back']; first = ['slot-01','slot-02','slot-03','slot-04']; second = ['slot-05','slot-06','slot-07','slot-08']
        banks = [first,second,normal,second] if row['preset']=='quick-slots' else [normal,first,normal,second]
        if row['preset']=='radial': banks = [normal]*4
        trace = [f'mask{mask}/{face}:{token}' for mask in range(4) if row['preset']!='radial' or mask%2==0
                 for face,token in zip(('A','X','Y','B'),banks[mask])]
        trace += ['slot-09:assign/use:self/actor/1','slot-10:assign/use:self/actor/1','pending:cancel/0;stale:enemy-ash/denied/0','grid:4,4->5,4/cancel0/confirmAP1']
        if row['preset']=='radial': trace += ['wheel:no-selection/up01/latch/choose1/fresh-LB/cancel']
        trace += ['labels:projected/Cyrillic/long/packed/removal/reload/release','settings:create/use/reset-copy/reset-defaults/scroll/positions-preserved;bounded-lifetime']
        if row.get('trace') != trace: raise ValueError('literal O5 trace mismatch')
        scenarios={s['scenario']:s for s in row['scenarios']}
        if len(scenarios)!=len(row['scenarios']): raise ValueError('duplicate scenario')
        for mask in range(4):
            if row['preset']=='radial' and mask%2: continue
            if any('['+f+'] '+t not in scenarios['bank-'+str(mask)]['prompt'] for f,t in zip(('A','X','Y','B'),banks[mask])):
                raise ValueError('literal glyph bank mismatch')
        full=scenarios['full-ten-modal-override']
        if full['modal']!='Context' or '[A] modal-confirm' not in full['prompt'] or '[B] modal-cancel' not in full['prompt'] or any(not any(f'slot-{n:02}' in c['text'] for c in full['controls']) for n in range(1,11)):
            raise ValueError('full-ten modal override missing')
        if row.get('proof') != dict(schema='arcanum-gamepad-o5-proof-v1', completed=PROOF):
            raise ValueError('incomplete original O5 Require path')
        if type(row.get('pixelColorCount')) is not int or row['pixelColorCount'] <= 8:
            raise ValueError('substantive production pixels missing')
        g=resolve(row['labelGeometry']); packing=row.get('packing')
        if not isinstance(packing,dict) or not isinstance(packing.get('labels'),list): raise ValueError('missing passive packing inputs')
        parts={p['id']:p for p in packing['labels']}; labels={l['id']:l for l in row['labels']}
        geom={resolve(l)['id']:resolve(l) for l in resolve(g['labels'])}
        if len(parts)!=len(packing['labels']) or len(labels)!=len(row['labels']) or set(parts)!=set(labels) or set(geom)!=set(labels):
            raise ValueError('duplicate or missing packing identity')
        for id,l in labels.items():
            p=parts[id]; actual=geom[id]; width=number(p['maximumWidth']); height=number(p['minimumHeight'])+8*ui/100
            expected_width=number(packing['contentWidth']) if l['overflow'] else 200*ui/100
            if (p.get('clipText') is not False or p.get('wrapMode')!='WordSmart' or type(p.get('lines')) is not int or p['lines']<1
                    or l['fontSize']!=16*ui/100 or actual['fontSize']!=l['fontSize'] or actual['text']!=l['text']
                    or (actual['parent']=='VBoxContainer') != l['overflow'] or abs(width-expected_width)>.02
                    or abs(l['rect']['width']-width)>.02 or abs(l['rect']['height']-height)>.02
                    or abs(actual['size']['x']-width)>.02 or abs(actual['size']['y']-height)>.02):
                raise ValueError('adverse or unexplained packing/zoom typography')
            if not l['overflow'] and not enclosed(viewport,l['rect']): raise ValueError('adjacent label clipping')
        key=tuple(row[k] for k in ('preset','width','height','uiScalePercent')); paired.setdefault(key,{})[row['zoom']]=labels
        observations=row['contractObservations']
        denials=[o for o in observations if o['oracle']=='physical-target-denial']
        if len(denials)!=6 or {o['variant'] for o in denials}!={'removed','revision','hidden','dead','ineligible','wrong-kind'}:
            raise ValueError('missing exact target-denial domain')
        lifecycle=[o for o in observations if o['oracle']=='individual-lifecycle']
        if len(lifecycle)!=114 or len({(o['boundary'],o['control']) for o in lifecycle})!=114 or any(o['after']!=o['before']+1 for o in lifecycle):
            raise ValueError('missing or adverse lifecycle control observations')
        projection=[o for o in observations if o['oracle']=='actual-projection']
        if len(projection)!=1: raise ValueError('actual projection missing/duplicate')
        a,b,o=(projection[0][k] for k in ('a','b','origin'))
        if any(abs((number(b[i])-number(o[i]))-4*(number(a[i])-number(o[i])))>.02 for i in (0,1)):
            raise ValueError('actual camera projection not 4x')
        if any(abs(number(projection[0]['uiReturned'][k])-number(b[i]))>.02 for i,k in enumerate(('X','Y'))):
            raise ValueError('UI scale changed actual world projection')
        titles=[o['observation'] for o in observations if o['oracle']=='modal-title']
        if len(titles)!=9 or {o['kind'] for o in titles}!={'Settings','Context','Slot','Assign','Hub','Battle','Follower','Bindings','BindingButton'}:
            raise ValueError('missing original title assertion readback')
        for t in titles:
            if (not enclosed(t['clip'],t['rect']) or '[A/Enter]' not in t['text'] or '[B/Esc]' not in t['text']
                    or t['font']!=16*ui/100 or t['rect']['height']<t['nativeMinimum']['height']
                    or any(v is not True for v in t['predicates'].values())):
                raise ValueError('adverse original modal title')
        for o in observations:
            if o['oracle']=='modal-text' and (not enclosed(o['clip'],o['rect']) or o['font']!=18*ui//100):
                raise ValueError('adverse padded modal row')
            if o['oracle']=='physical-target-denial' and (o['before']!=o['after'] or o['last']['Kind']!='TargetUnavailable' or o['last']['TargetId']!='enemy-ash'):
                raise ValueError('adverse target denial')
        domains = {'Settings':23,'Context':12,'Slot':4,'Assign':13,'Hub':6,'Battle':4,'Follower':2,'BindingButton':23}
        for kind in (*domains, 'Bindings'):
            modal=[o for o in observations if o['oracle']=='modal-text' and o['kind']==kind]
            count=domains.get(kind, len(modal)//2)
            if count<=0 or len(modal)!=count*2 or [(o['direction'],o['index']) for o in modal] != [(d,i) for d in (1,-1) for i in (range(count) if d==1 else reversed(range(count)))]:
                raise ValueError('missing bidirectional native modal row observations')
    for pair in paired.values():
        if set(pair)!={.5,2.}: raise ValueError('missing zoom-pair attribution')
        for id in pair[.5].keys() & pair[2.].keys():
            a,b=pair[.5][id],pair[2.][id]
            if a['text']!=b['text'] or a['fontSize']!=b['fontSize'] or a['overflow']==b['overflow'] and any(abs(a['rect'][k]-b['rect'][k])>.02 for k in ('width','height')):
                raise ValueError('unexplained same-parent zoom dimensions/text')
    for l in report['lifetimes']:
        if l.get('settlementStatus')!='cancelled' or l.get('cancelledGeneration')!=l.get('exitedGeneration',-2)+1:
            raise ValueError('original disposal assertion readback missing')
