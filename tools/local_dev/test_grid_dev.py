"""Public literal WORLD003 report controls; never original verification evidence."""
import copy
import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import unittest
from typing import Any
from unittest.mock import patch
from tools.local_dev import config, manifests, retention
from tools.local_dev.grid_validation import validate_report
from tools.local_dev.human_validation import validate_report as validate_human
from tools.local_dev.test_human_dev import NAMES, report as human_report
from tools.local_dev import test_local_dev as fixtures

# Independent literal gate contract, never generated from production GATES.
GRID_GATES = dict(zip((*NAMES, 'source-grid-original', 'exported-grid-original'), (
    'locked-build.log', 'source-headless.log', 'source-graphical.log', 'export.log',
    'exported-headless.log', 'exported-graphical.log', 'codesign.log', 'tests.log',
    'source-world-original.log', 'exported-world-original.log', 'source-world-persistence.log',
    'exported-world-persistence.log', 'hostfxr.log', 'human-metadata.log',
    'source-human-original.log', 'exported-human-original.log', 'source-grid-original.log',
    'exported-grid-original.log')))


def gate_receipt(gate, file):
    data = file.read_bytes()
    return dict(schema='arcanum-grid-gate-result-v1', task='ARC-WORLD-003', candidateCommit='a'*40,
        gate=gate, log=file.name, outcome='PASS', exitCode=0, logBytes=len(data),
        logSha256=hashlib.sha256(data).hexdigest(), startedAt='2026-10-03T00:00:00+00:00',
        finishedAt='2026-10-03T00:00:01+00:00')


class GridReportTests(unittest.TestCase):
    def sample(self) -> dict[str, Any]:
        head='a'*40; digest='b'*64
        start='StepStarted@1/1:6,1>7,1|ActionChanged@1/1:6,1>7,1'
        finish='|CellCommitted@5/4:6,1>7,1|CenterArrived@3/2:6,1>7,1|ActionChanged@3/2:6,1>7,1'
        traces={
            'early':start+'|StepCancelled@9/8:6,1>7,1|FacingChanged@9/8:6,1>7,1|CenterArrived@5/4:6,1>7,1|ActionChanged@5/4:6,1>7,1',
            'equality':start+finish, 'late':start+finish,
            'open-reverse':start+finish+'|StepStarted@2/1:7,1>6,1|FacingChanged@2/1:7,1>6,1|ActionChanged@2/1:7,1>6,1|CellCommitted@9/4:7,1>6,1|CenterArrived@5/2:7,1>6,1|ActionChanged@5/2:7,1>6,1',
            'blocker':'StepRejected@1/1:6,1>5,1'}
        cases=[]
        for viewport in ([1280,800],[1440,900],[800,600]):
            for name,trace in traces.items():
                edges=[([6,1],[5,1],1)] if name=='blocker' else [([6,1],[7,1],5)]
                if name=='open-reverse':edges.append(([7,1],[6,1],1))
                admissions=[]
                for index,(source,dest,direction) in enumerate(edges):
                    time=dict(Value=dict(Numerator=index+1,Denominator=1,IsValid=True),IsValid=True)
                    key=dict(Binding=dict(ChartId='SECTOR_1452_1290_LOCAL_CENTERS',ActorId='HUMAN_FIXED_DIAGNOSTIC',
                        ContextId='c'*64,CapabilitiesProfileId='ISOLATED_PC_HUMAN_FIXED_V1',
                        CapabilitiesFingerprint='KIND15_ART2_FLAGS0_GROUND_NONPOLYMORPH_REGISTRY0',
                        AssessmentProfileId='BASE_SECTOR_1452_1290_ADJACENT_V1',InputFingerprint=digest,EvidenceEpoch=7),
                        From=dict(X=source[0],Y=source[1]),To=dict(X=dest[0],Y=dest[1]),Direction=direction,
                        StartTime=time,OpportunityId=index+1,ProposedStepId=index+1,
                        MaterialIntentKey=dict(Time=time,Sequence=index*4+1),EvidenceRevision=0)
                    admissions.append(dict(from_=source,to=dest,direction=direction,records=317,gaps=0,
                        selectedEvidenceSha256=digest,evidenceEpoch=7,coverage='Supported',
                        disposition='Blocked' if name=='blocker' else 'Open',key=key))
                    admissions[-1]['from']=admissions[-1].pop('from_')
                cases.append(dict(name=name,viewport=viewport,trace=trace,rendered='PASS',
                    cell=[7,1] if name in ('equality','late') else [6,1],phase='Settled',admissions=admissions))
        return dict(schema='arcanum-grid-validation-v1',task='ARC-WORLD-003',candidateCommit=head,
            host='source',kind='grid-original',outcome='PASS',exitCode=0,
            originalIdentity='authenticated-original-grid-and-actor',profile='HumanFixed',
            assessmentProfile='BASE_SECTOR_1452_1290_ADJACENT_V1',context='ISOLATED_PC_CRITTER_ART_NONPOLYMORPH',
            duration='1/2',contextFingerprint='c'*64,graphical=True,actualViewports=[[1280,800],[1440,900],[800,600]],
            negativeControls='PASS',renderedPhases=['Idle','Walk','Returning','OutboundAfterEntry','Blocked'],
            originalParity='NOT_VERIFIED',selectedEvidenceSha256=digest,evidenceEpoch=7,
            blocker=dict(sourceIndex=1,cell=[5,1],anchor=[-163,134],rectangle=[-276,-128,204,267],
                visible='PASS',reason='ADMITTED_DESTINATION_SCENERY_BLOCKER'),cases=cases,screenCases=self.screen_fixture(digest))

    @staticmethod
    def screen_fixture(digest):
        rows=[]
        offsets=[(-1,-1),(-1,0),(-1,1),(0,1),(1,1),(1,0),(1,-1),(0,-1)]
        inversions=[[0,1,2,3,4,5,6,7],[0,7,6,5,4,3,2,1],[4,3,2,1,0,7,6,5],[4,5,6,7,0,1,2,3]]
        for viewport in ([1280,800],[1440,900],[800,600]):
            for device in ('keyboard','stick'):
                for flags in range(4):
                    for sector in range(8):
                        facing=inversions[flags][sector];dx,dy=offsets[facing];target=[7+dx,2+dy]
                        edge=f'7,2>{target[0]},{target[1]}'
                        events=[f'StepStarted@2/1:{edge}']
                        if facing!=4:events.append(f'FacingChanged@2/1:{edge}')
                        events.extend([f'ActionChanged@2/1:{edge}',f'CellCommitted@9/4:{edge}',f'CenterArrived@5/2:{edge}',f'ActionChanged@5/2:{edge}'])
                        legs=[]
                        if facing%2==0:
                            hx,hy=[7+dx,2],[7,2+dy];xd,yd=(1 if dx<0 else 5),(7 if dy<0 else 3)
                            for role,source,destination,direction in [('XShoulder',[7,2],hx,xd),('XFinal',hx,target,yd),('YShoulder',[7,2],hy,yd),('YFinal',hy,target,xd)]:
                                legs.append(dict(role=role,**{'from':source},to=destination,direction=direction,originalDirection=facing,records=317,gaps=0,blockers=[]))
                        rows.append(dict(viewport=viewport,device=device,sector=sector,flags=flags,facing=facing,direction=facing,projected=[40*(dy-dx),20*(dy+dx)],
                            **{'from':[7,2]},to=target,rendered='PASS',coverage='Supported',disposition='Open',gaps=0,profile='BASE_SECTOR_1452_1290_ADJACENT_V1',
                            evidenceEpoch=7,selectedEvidenceSha256=digest,trace='|'.join(events),legs=legs))
        return rows

    def test_screen_matrix_rejects_swapped_ne_nw_stale_profile_legs_and_duplicates(self):
        good=self.sample()
        changes=[lambda r:r.pop('screenCases'),lambda r:r.__setitem__('assessmentProfile','BASE_SECTOR_1452_1290'),
            lambda r:r['screenCases'][1].__setitem__('to',[7,1]),lambda r:r['screenCases'][0]['legs'].pop(),
            lambda r:r['screenCases'][0]['legs'][0].__setitem__('originalDirection',4),
            lambda r:r['screenCases'].__setitem__(1,r['screenCases'][0])]
        for change in changes:
            bad=copy.deepcopy(good);change(bad)
            with self.assertRaises(ValueError):validate_report(bad,'a'*40,'source')

    def test_original_world_keyboard_route_requires_stick_matrix_and_inert_keyboard(self):
        good=self.sample()
        good['screenCases']=[row for row in good['screenCases'] if row['device']=='stick']
        good['keyboardRoute']='original-world'
        good['keyboardInert']=[dict(viewport=v,sector=s,events=0,cell=[7,2],phase='Settled')
            for v in ([1280,800],[1440,900],[800,600]) for s in range(8)]
        validate_report(good,'a'*40,'source')
        changes=[lambda r:r.pop('keyboardInert'),lambda r:r['keyboardInert'][3].__setitem__('events',2),
            lambda r:r['keyboardInert'].pop(),lambda r:r.__setitem__('keyboardRoute','lab'),
            lambda r:r['screenCases'].append(dict(r['screenCases'][0],device='keyboard')),
            lambda r:r['screenCases'][0].__setitem__('device','keyboard')]
        for change in changes:
            bad=copy.deepcopy(good);change(bad)
            with self.assertRaises(ValueError):validate_report(bad,'a'*40,'source')
        legacy=self.sample(); legacy['keyboardInert']=good['keyboardInert']
        with self.assertRaises(ValueError):validate_report(legacy,'a'*40,'source')
        # ARC-INPUT-001 evidence must carry the route: a legacy WASD-moving matrix is rejected.
        validate_report(self.sample(),'a'*40,'source')
        with self.assertRaises(ValueError):validate_report(self.sample(),'a'*40,'source',require_keyboard_route=True)
        validate_report(good,'a'*40,'source',require_keyboard_route=True)

    def test_literal_report_and_each_binding_rejects_changed_or_missing(self):
        good=self.sample(); validate_report(good,'a'*40,'source')
        changes={'task':'ARC-RENDER-006','candidateCommit':'c'*40,'host':'exported','kind':'human',
            'outcome':'NOT_VERIFIED','exitCode':1,'originalIdentity':'synthetic','profile':'MonsterRegression',
            'assessmentProfile':'OTHER','context':'OTHER','duration':'8/1','graphical':False,
            'actualViewports':[[800,600]],'negativeControls':'FAIL','renderedPhases':['Idle'],
            'selectedEvidenceSha256':'wrong','evidenceEpoch':0,'blocker':{},'cases':[]}
        for key,value in changes.items():
            with self.subTest(key=key),self.assertRaises(ValueError):validate_report(good|{key:value},'a'*40,'source')
            missing=good.copy();missing.pop(key)
            with self.subTest(missing=key),self.assertRaises(ValueError):validate_report(missing,'a'*40,'source')
        for path,value in [('trace','StepStarted'),('phase','Returning'),('cell',[5,1]),('rendered','NOT_VERIFIED')]:
            changed=copy.deepcopy(good);changed['cases'][0][path]=value
            with self.subTest(path=path),self.assertRaises(ValueError):validate_report(changed,'a'*40,'source')
        for key,value in [('records',316),('gaps',1),('evidenceEpoch',8),('selectedEvidenceSha256','c'*64),('direction',3),('key',None),('key',{})]:
            changed=copy.deepcopy(good);changed['cases'][0]['admissions'][0][key]=value
            with self.subTest(admission=key),self.assertRaises(ValueError):validate_report(changed,'a'*40,'source')

    def test_old_reverse_action_before_facing_is_rejected_for_each_host_and_viewport(self):
        old='StepStarted@2/1:7,1>6,1|ActionChanged@2/1:7,1>6,1|FacingChanged@2/1:7,1>6,1'
        correct='StepStarted@2/1:7,1>6,1|FacingChanged@2/1:7,1>6,1|ActionChanged@2/1:7,1>6,1'
        for host in ('source','exported'):
            good: dict[str, Any]=self.sample()
            good['host']=host
            validate_report(good,'a'*40,host)
            for viewport in ([1280,800],[1440,900],[800,600]):
                changed=copy.deepcopy(good)
                row=next(row for row in changed['cases'] if row['name']=='open-reverse' and row['viewport']==viewport)
                self.assertIn(correct,row['trace'])
                row['trace']=row['trace'].replace(correct,old)
                with self.subTest(host=host,viewport=viewport),self.assertRaisesRegex(ValueError,'transcript'):
                    validate_report(changed,'a'*40,host)

    def test_human_report_is_explicitly_task_bound_not_an_alias(self):
        good=human_report()|{'task':'ARC-WORLD-003'}
        validate_human(good,'a'*40,'source','human','ARC-WORLD-003')
        with self.assertRaises(ValueError):validate_human(good,'a'*40,'source','human')
        with self.assertRaises(ValueError):validate_human(human_report(),'a'*40,'source','human','ARC-WORLD-003')
        with self.assertRaises(ValueError):validate_human(good,'a'*40,'source','human','UNKNOWN')


class GridEndpointOrderTests(unittest.TestCase):
    """Actual writer and registry boundaries, using public synthetic reports only."""
    def test_endpoint_cleanup_restores_locks_and_keeps_tracked_uid_sidecars(self):
        source = Path(__file__).resolve().parents[1] / 'terrain-viewer/Godot/tools/validate-macos.sh'
        cleanup = source.read_text().split('cleanup_candidate() {', 1)[1].split('\n}', 1)[0]
        with tempfile.TemporaryDirectory(dir=os.environ.get('TMPDIR')) as directory:
            root = Path(directory)
            names = ('TerrainViewerWorldGridTest', 'TerrainViewerWorldOriginalGridValidation',
                     'TerrainViewerWorldReadbackScope', 'TerrainViewerWorldRetirementTest')
            for name in (*names, 'Unrelated'):
                (root / (name + '.cs.uid')).write_text('public synthetic sidecar\n')
            # Exercise the production cleanup function. Lock restoration is a
            # recorded no-op in this synthetic workspace, not a real git write.
            git = root / 'git'
            git.write_text('#!/bin/sh\nprintf "%s\\n" "$@" > "$project_root/git-args"\n'); git.chmod(0o700)
            script = root / 'cleanup.sh'
            script.write_text('set -eu\ncleanup_candidate() {' + cleanup + '\n}\ncleanup_candidate\n')
            subprocess.run(['/bin/sh', str(script)], check=True,
                env={**os.environ, 'PATH': str(root) + ':/usr/bin:/bin',
                     'project_root': str(root), 'candidate_root': str(root)})
            # Godot .uid sidecars are tracked; deleting them would dirty the
            # exact candidate after validation.
            for name in (*names, 'Unrelated'):
                self.assertEqual('public synthetic sidecar\n', (root / (name + '.cs.uid')).read_text(), name)
            self.assertIn('src/Arcanum.Simulation/packages.lock.json', (root / 'git-args').read_text().splitlines())

    root: Path
    setUp=fixtures.LocalDevTests.setUp
    tearDown=fixtures.LocalDevTests.tearDown
    conf=fixtures.LocalDevTests.conf
    v2_manifest=fixtures.LocalDevTests.v2_manifest
    authority_snapshot=fixtures.LocalDevTests.authority_snapshot

    def endpoint_fixture(self):
        head='a'*40
        original,_=self.v2_manifest(task='ARC-WORLD-003')
        build=self.root/'artifacts/world-builds/ARC-WORLD-003'/head
        build.parent.mkdir(parents=True)
        original.parent.rename(build)
        path=build/'manifest.json'; payload=json.loads(path.read_bytes())
        (build/'GamepadLab.app').rename(build/'ArcanumTerrainViewer.app')
        payload['logicalTool']='terrain'
        for entry in payload['payloadInventory']:
            entry['relativePath']=entry['relativePath'].replace('GamepadLab.app','ArcanumTerrainViewer.app')
        payload['app']={'relativePath':'ArcanumTerrainViewer.app','executableRelativePath':'ArcanumTerrainViewer.app/Contents/MacOS/GamepadLab'}
        evidence=build/'evidence'
        for name,key in (('export-preflight.json','preflight'),('export-observation.json','observation')):
            payload['exportBudget'][key]['tool']='terrain'
            (evidence/name).write_text(json.dumps(payload['exportBudget'][key]))
        for host in ('source','exported'):
            for kind in ('original','persistence'):
                (evidence/f'{host}-world-{kind}.log').write_text(json.dumps(dict(schema='arcanum-world-validation-v1',candidateCommit=head,host=host,kind=kind,outcome='PASS')))
        for name,host,kind in (('human-metadata.log','source','human-metadata'),('source-human-original.log','source','human'),('exported-human-original.log','exported','human')):
            (evidence/name).write_text(json.dumps(human_report(host,kind)|{'task':'ARC-WORLD-003'}))
        for host in ('source','exported'):
            (evidence/f'{host}-grid-original.log').write_text(json.dumps(GridReportTests().sample()|{'host':host}))
        for name in ('locked-build','source-headless','source-graphical','export','exported-headless','exported-graphical','codesign','tests','hostfxr'):
            (evidence/f'{name}.log').write_text('PUBLIC SYNTHETIC gate fixture, not original proof\n')
        for gate, name in GRID_GATES.items():
            (evidence/(name+'.result.json')).write_text(json.dumps(gate_receipt(gate,evidence/name)))
        payload['evidence']=[dict(relativePath='evidence/'+file.name,bytes=file.stat().st_size,sha256=manifests.sha256(file)) for file in sorted(evidence.iterdir())]
        payload['checks']=[dict(name=name,outcome='PASS') for name in (*NAMES,'source-grid-original','exported-grid-original')]
        payload['delivery']={'originalInputModes':'PASS'}
        path.write_text(json.dumps(payload)); conf=self.conf()
        conf['artifactRoots']={'test':str(self.root/'artifacts/world-builds')}
        config.save_config(conf); config.save_registry(config.default_registry())
        script=Path(__file__).resolve().parents[1]/'terrain-viewer/Godot/tools/terrain_write_manifest.py'
        main=runpy.run_path(str(script))['main']
        executable=build/payload['app']['executableRelativePath']
        argv=[str(script),str(self.root/'source'),str(self.root),str(build),head,str(executable),'ARC-WORLD-003']
        def git(_, *args):
            if args==('status','--porcelain'):return ''
            if args==('branch','--show-current'):return 'public-fixture'
            return head
        return path, payload, conf, main, argv, git

    def refresh_evidence(self, path, payload):
        payload['evidence']=[dict(relativePath='evidence/'+file.name,bytes=file.stat().st_size,sha256=manifests.sha256(file))
            for file in sorted((path.parent/'evidence').iterdir()) if file.is_file()]
        path.write_text(json.dumps(payload))

    def assert_both_reject(self, path, payload, conf, main, argv, git):
        self.refresh_evidence(path,payload)
        before=self.authority_snapshot(); registry=config.default_registry()
        with self.assertRaises((ValueError,SystemExit)):
            retention.register_export(registry,conf=conf,tool='terrain',candidate_id='test:ARC-WORLD-003:'+'a'*40,manifest=path)
        self.assertEqual(registry,config.default_registry());self.assertEqual(before,self.authority_snapshot())
        encoded=path.read_bytes();path.unlink();before=self.authority_snapshot()
        with patch.dict(os.environ,ARCANUM_LOCAL_ROOT_ID='test'),patch.dict(main.__globals__,git=git),patch.object(sys,'argv',argv):
            with self.assertRaises((ValueError,SystemExit)):main()
        self.assertEqual(before,self.authority_snapshot());self.assertFalse(path.exists());path.write_bytes(encoded)

    def test_writer_and_registration_reject_old_order_without_authority_mutation(self):
        path,payload,conf,main,argv,git=self.endpoint_fixture()
        evidence=path.parent/'evidence';head='a'*40
        old='StepStarted@2/1:7,1>6,1|ActionChanged@2/1:7,1>6,1|FacingChanged@2/1:7,1>6,1'
        correct='StepStarted@2/1:7,1>6,1|FacingChanged@2/1:7,1>6,1|ActionChanged@2/1:7,1>6,1'
        for host in ('source','exported'):
            file=evidence/f'{host}-grid-original.log'; originalReport=file.read_bytes()
            changed=json.loads(originalReport)
            for row in changed['cases']:
                if row['name']=='open-reverse':row['trace']=row['trace'].replace(correct,old)
            file.write_text(json.dumps(changed))
            receipt=evidence/(file.name+'.result.json'); savedReceipt=receipt.read_bytes()
            receipt.write_text(json.dumps(gate_receipt(host+'-grid-original',file)))
            # Update evidence digest so registration reaches semantic validation,
            # not an unrelated stale-byte rejection.
            entry=next(e for e in payload['evidence'] if e['relativePath']=='evidence/'+file.name)
            entry.update(bytes=file.stat().st_size,sha256=manifests.sha256(file))
            receiptEntry=next(e for e in payload['evidence'] if e['relativePath']=='evidence/'+receipt.name)
            receiptEntry.update(bytes=receipt.stat().st_size,sha256=manifests.sha256(receipt))
            path.write_text(json.dumps(payload)); before=self.authority_snapshot(); registry=config.default_registry()
            with self.subTest(host=host,consumer='registry'),self.assertRaisesRegex(ValueError,'transcript'):
                retention.register_export(registry,conf=conf,tool='terrain',candidate_id='test:ARC-WORLD-003:'+head,manifest=path)
            self.assertEqual(registry,config.default_registry());self.assertEqual(before,self.authority_snapshot())
            savedManifest=path.read_bytes();path.unlink();before=self.authority_snapshot()
            with patch.dict(os.environ,ARCANUM_LOCAL_ROOT_ID='test'),patch.dict(main.__globals__,git=git),patch.object(sys,'argv',argv):
                with self.subTest(host=host,consumer='writer'),self.assertRaisesRegex(ValueError,'transcript'):main()
            self.assertEqual(before,self.authority_snapshot());self.assertFalse(path.exists())
            file.write_bytes(originalReport);entry.update(bytes=file.stat().st_size,sha256=manifests.sha256(file))
            receipt.write_bytes(savedReceipt);receiptEntry.update(bytes=receipt.stat().st_size,sha256=manifests.sha256(receipt))
            path.write_bytes(savedManifest)
        # Correct order must traverse both real consumers, with no validator patch.
        path.unlink()
        with patch.dict(os.environ,ARCANUM_LOCAL_ROOT_ID='test'),patch.dict(main.__globals__,git=git),patch.object(sys,'argv',argv):main()
        registry=config.default_registry()
        retention.register_export(registry,conf=conf,tool='terrain',candidate_id='test:ARC-WORLD-003:'+head,manifest=path)
        self.assertEqual(registry['builds']['test:ARC-WORLD-003:'+head]['lifecycle'],'exported')

    def test_all_eighteen_gate_receipts_missing_failed_unverified_or_stale_reject_both_consumers(self):
        args=self.endpoint_fixture();path,payload,*_=args;evidence=path.parent/'evidence'
        for gate,name in GRID_GATES.items():
            file=evidence/(name+'.result.json');original=file.read_bytes();good=json.loads(original)
            for change in (None, {'outcome':'FAIL','exitCode':1}, {'outcome':'NOT_VERIFIED'},
                    {'candidateCommit':'b'*40}, {'task':'ARC-RENDER-006'}, {'gate':'OTHER'},
                    {'logSha256':'b'*64}, {'startedAt':None}, {'finishedAt':'2026-10-02T00:00:00+00:00'}):
                if change is None:file.unlink()
                else:file.write_text(json.dumps(good|change))
                with self.subTest(gate=gate,change=change):self.assert_both_reject(*args)
                file.write_bytes(original)
        self.refresh_evidence(path,payload)
        registry=config.default_registry()
        retention.register_export(registry,conf=args[2],tool='terrain',candidate_id='test:ARC-WORLD-003:'+'a'*40,manifest=path)
        self.assertIn('test:ARC-WORLD-003:'+'a'*40,registry['builds'])

    def test_all_manifest_gates_missing_failed_unverified_duplicate_reject_registry(self):
        path,payload,conf,*_=self.endpoint_fixture()
        for gate in GRID_GATES:
            for outcome in ('MISSING','FAIL','NOT_VERIFIED','DUPLICATE'):
                changed=copy.deepcopy(payload)
                changed['checks']=[c|{'outcome':outcome} if c['name']==gate and outcome not in ('MISSING','DUPLICATE') else c
                    for c in changed['checks'] if outcome!='MISSING' or c['name']!=gate]
                if outcome=='DUPLICATE':changed['checks'].append(dict(name=gate,outcome='PASS'))
                path.write_text(json.dumps(changed));before=self.authority_snapshot();registry=config.default_registry()
                with self.subTest(gate=gate,outcome=outcome),self.assertRaises(ValueError):
                    retention.register_export(registry,conf=conf,tool='terrain',candidate_id='test:ARC-WORLD-003:'+'a'*40,manifest=path)
                self.assertEqual(registry,config.default_registry());self.assertEqual(before,self.authority_snapshot())
        for name in GRID_GATES.values():
            for suffix in ('','.result.json'):
                changed=copy.deepcopy(payload)
                changed['evidence']=[e for e in changed['evidence'] if e['relativePath']!='evidence/'+name+suffix]
                path.write_text(json.dumps(changed));before=self.authority_snapshot();registry=config.default_registry()
                with self.subTest(omitted=name+suffix),self.assertRaisesRegex(ValueError,'manifest inventory'):
                    retention.register_export(registry,conf=conf,tool='terrain',candidate_id='test:ARC-WORLD-003:'+'a'*40,manifest=path)
                self.assertEqual(registry,config.default_registry());self.assertEqual(before,self.authority_snapshot())

    def test_structured_grid_and_human_bindings_reject_both_consumers_without_mutation(self):
        args=self.endpoint_fixture();path,payload,*_=args;evidence=path.parent/'evidence'
        changes={'schema':'OTHER','task':'ARC-RENDER-006','candidateCommit':'b'*40,'host':'OTHER',
            'kind':'OTHER','outcome':'NOT_VERIFIED','exitCode':1,'originalIdentity':'synthetic',
            'profile':'MonsterRegression','assessmentProfile':'OTHER','context':'OTHER',
            'contextFingerprint':'bad','duration':'8/1','graphical':False,'actualViewports':[[800,600]],
            'negativeControls':'FAIL','renderedPhases':['Idle'],'originalParity':'PASS',
            'selectedEvidenceSha256':'bad','evidenceEpoch':0,'blocker':{},'cases':[]}
        for host in ('source','exported'):
            file=evidence/(host+'-grid-original.log');original=file.read_bytes();good=json.loads(original)
            mutations=[None, '{}', 'invalid JSON']
            for field,value in changes.items():
                mutations.append(json.dumps(good|{field:value}))
                missing=copy.deepcopy(good);missing.pop(field);mutations.append(json.dumps(missing))
            for viewport in ([1280,800],[1440,900],[800,600]):
                for field,value in (('trace','StepStarted'),('phase','Returning'),('cell',[5,1]),('rendered','FAIL'),('admissions',[])):
                    changed=copy.deepcopy(good);next(r for r in changed['cases'] if r['viewport']==viewport)[field]=value
                    mutations.append(json.dumps(changed))
                changed=copy.deepcopy(good)
                row=next(r for r in changed['cases'] if r['viewport']==viewport)
                row['admissions'][0]['key']['OpportunityId']=99
                mutations.append(json.dumps(changed))
                changed=copy.deepcopy(good);changed['cases'][0]=changed['cases'][1];mutations.append(json.dumps(changed))
            receipt=evidence/(file.name+'.result.json');savedReceipt=receipt.read_bytes()
            for index,encoded in enumerate(mutations):
                if encoded is None:file.unlink()
                else:
                    file.write_text(encoded);receipt.write_text(json.dumps(gate_receipt(host+'-grid-original',file)))
                with self.subTest(host=host,mutation=index):self.assert_both_reject(*args)
                file.write_bytes(original);receipt.write_bytes(savedReceipt)
        for name,host,kind in (('human-metadata.log','source','human-metadata'),('source-human-original.log','source','human'),('exported-human-original.log','exported','human')):
            file=evidence/name;original=file.read_bytes();good=json.loads(original)
            receipt=evidence/(name+'.result.json');savedReceipt=receipt.read_bytes()
            for field,value in {'task':'ARC-RENDER-006','candidateCommit':'b'*40,'host':'OTHER','kind':'OTHER',
                    'profile':'MonsterRegression','originalIdentity':'synthetic','graphical':False,'outcome':'FAIL',
                    'exitCode':1,'actualViewports':[[800,600]],'metadata':'NOT_VERIFIED','allUsedRgba':'FAIL',
                    'renderedSelections':16,'progression':'NOT_VERIFIED'}.items():
                # Metadata-only gate deliberately has no graphical claims;
                # those are mandatory in the separate source/exported human gates.
                if kind=='human-metadata' and field in ('graphical','actualViewports','renderedSelections','progression'):
                    continue
                file.write_text(json.dumps(good|{field:value}));receipt.write_text(json.dumps(gate_receipt(name.removesuffix('.log'),file)))
                with self.subTest(report=name,field=field):self.assert_both_reject(*args)
                file.write_bytes(original);receipt.write_bytes(savedReceipt)

    def test_real_gate_runner_retains_exit_digest_times_and_refuses_overwrite(self):
        from tools.local_dev.grid_export_gates import run_gate
        evidence=self.root/'evidence';evidence.mkdir()
        self.assertEqual(run_gate(evidence,'a'*40,'source-headless.log',['/usr/bin/printf','public synthetic gate\n']),0)
        result=json.loads((evidence/'source-headless.log.result.json').read_bytes())
        self.assertEqual((result['task'],result['gate'],result['candidateCommit'],result['exitCode'],result['outcome']),
            ('ARC-WORLD-003','source-headless','a'*40,0,'PASS'))
        self.assertEqual(result['logSha256'],hashlib.sha256(b'public synthetic gate\n').hexdigest())
        before=self.authority_snapshot()
        with self.assertRaises(ValueError):run_gate(evidence,'a'*40,'source-headless.log',['/usr/bin/false'])
        self.assertEqual(before,self.authority_snapshot())
        completed=subprocess.run([sys.executable,'-B','-m','tools.local_dev.grid_export_gates',
            '--evidence',str(evidence),'--head','a'*40,'--log','export.log','--','/usr/bin/false'],check=False)
        self.assertEqual(completed.returncode,1)
        result=json.loads((evidence/'export.log.result.json').read_bytes())
        self.assertEqual((result['exitCode'],result['outcome']),(1,'FAIL'))
        self.assertNotIn('command',result)
