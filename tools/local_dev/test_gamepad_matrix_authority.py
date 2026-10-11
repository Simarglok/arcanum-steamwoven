"""Synthetic policy fixtures only: no GUI/build/export acceptance claims."""
from __future__ import annotations
import copy
import hashlib
import json
import os
from pathlib import Path
import struct
import tempfile
import unittest
import zlib
from unittest.mock import patch
from . import gamepad_export_gates as gates
from . import gamepad_matrix_authority as authority

HEAD='a'*40


def fixture_report():
    rect=dict(x=0,y=0,width=200,height=31); clip=dict(x=0,y=0,width=1280,height=800)
    normal=['interact','inspect','context','back']; first=['slot-01','slot-02','slot-03','slot-04']; second=['slot-05','slot-06','slot-07','slot-08']
    scenarios=[dict(scenario='bank-'+str(m),prompt=' '.join('['+f+'] '+t for f,t in zip(('A','X','Y','B'),b))) for m,b in enumerate((normal,first,normal,second))]
    scenarios.append(dict(scenario='full-ten-modal-override',modal='Context',prompt='[A] modal-confirm [B] modal-cancel', controls=[dict(text=f'slot-{n:02}') for n in range(1,11)]))
    trace=[f'mask{m}/{f}:{t}' for m,b in enumerate((normal,first,normal,second)) for f,t in zip(('A','X','Y','B'),b)]
    trace+=['slot-09:assign/use:self/actor/1','slot-10:assign/use:self/actor/1','pending:cancel/0;stale:enemy-ash/denied/0','grid:4,4->5,4/cancel0/confirmAP1','labels:projected/Cyrillic/long/packed/removal/reload/release','settings:create/use/reset-copy/reset-defaults/scroll/positions-preserved;bounded-lifetime']
    obs=[dict(oracle='actual-projection',a=[0,0],b=[-1920,-1200],origin=[640,400],uiReturned=dict(X=-1920,Y=-1200))]
    obs += [dict(oracle='physical-target-denial',variant=v,before=0,after=0,last=dict(Kind='TargetUnavailable',TargetId='enemy-ash')) for v in ('removed','revision','hidden','dead','ineligible','wrong-kind')]
    obs += [dict(oracle='individual-lifecycle',boundary=b,control=str(c),before=0,after=1) for b in ('focus','modal','preset','reset','disconnect-reconnect','device-change') for c in range(19)]
    domains={'Settings':23,'Context':12,'Slot':4,'Assign':13,'Hub':6,'Battle':4,'Follower':2,'Bindings':45,'BindingButton':23}
    for kind,count in domains.items():
        obs.append(dict(oracle='modal-title',observation=dict(kind=kind,clip=clip,rect=rect,text='[A/Enter] [B/Esc]',font=16,nativeMinimum=dict(height=23),predicates=dict(enclosed=True,unclipped=True))))
        obs += [dict(oracle='modal-text',kind=kind,direction=d,index=i,clip=clip,rect=rect,font=18) for d in (1,-1) for i in (range(count) if d==1 else reversed(range(count)))]
    row=dict(preset='contextual',width=1280,height=800,uiScalePercent=100,zoom=.5,trace=trace,scenarios=scenarios,
        proof=dict(schema='arcanum-gamepad-o5-proof-v1',completed=['faces','modal-slots-target-grid','wheel','lifecycle-projection','physical-targets','settings-widgets','lifecycle-controls','modal-text','labels','scale-resize','pixels-release-settings']),
        pixelColorCount=9,labelGeometry=dict(labels=[dict(id='fixture',parent='LabHighlightLayer',fontSize=16,text='fixture',size=dict(x=200,y=31))]),
        packing=dict(contentWidth=452,labels=[dict(id='fixture',clipText=False,wrapMode='WordSmart',lines=1,minimumHeight=23,maximumWidth=200)]),
        labels=[dict(id='fixture',text='fixture',fontSize=16,overflow=False,rect=rect)],contractObservations=obs)
    secondrow=copy.deepcopy(row);secondrow['zoom']=2
    return dict(witnesses={},tuples=[row,secondrow],lifetimes=[dict(settlementStatus='cancelled',exitedGeneration=1,cancelledGeneration=2)])


class ObservationPolicyTests(unittest.TestCase):
    def test_complete_literal_fixture(self): authority.validate_observations(fixture_report())

    def test_each_required_field_omission(self):
        for key in ('proof','pixelColorCount','packing','labelGeometry','labels','contractObservations','trace','scenarios'):
            with self.subTest(key=key):
                report=fixture_report();del report['tuples'][0][key]
                with self.assertRaises((ValueError,KeyError)): authority.validate_observations(report)

    def test_all_reached_oracles_required(self):
        for index in range(11):
            report=fixture_report();report['tuples'][0]['proof']['completed'].pop(index)
            with self.assertRaises(ValueError):authority.validate_observations(report)

    def test_each_changed_packing_value(self):
        for key,value in [('clipText',True),('wrapMode','Off'),('lines',0),('minimumHeight',100),('maximumWidth',201)]:
            with self.subTest(key=key):
                report=fixture_report();report['tuples'][0]['packing']['labels'][0][key]=value
                with self.assertRaises(ValueError):authority.validate_observations(report)

    def test_parent_switch_allowed_same_parent_dimension_rejected(self):
        report=fixture_report();row=report['tuples'][0]
        row['labels'][0]['overflow']=True;row['labels'][0]['rect']['width']=452
        row['labelGeometry']['labels'][0]['parent']='VBoxContainer';row['labelGeometry']['labels'][0]['size']['x']=452
        row['packing']['labels'][0]['maximumWidth']=452
        authority.validate_observations(report)
        row['labels'][0]['overflow']=False;row['labelGeometry']['labels'][0]['parent']='LabHighlightLayer'
        with self.assertRaises(ValueError):authority.validate_observations(report)

    def test_actual_projection_title_row_denial_lifecycle_adverse(self):
        for oracle in ('actual-projection','modal-title','modal-text','physical-target-denial','individual-lifecycle'):
            with self.subTest(oracle=oracle):
                report=fixture_report();obs=next(o for o in report['tuples'][0]['contractObservations'] if o['oracle']==oracle)
                if oracle=='actual-projection':obs['b'][0]+=1
                elif oracle=='modal-title':obs['observation']['predicates']['unclipped']=False
                elif oracle=='modal-text':obs['font']=17
                else:obs['after']+=1
                with self.assertRaises(ValueError):authority.validate_observations(report)

    def test_missing_observation_domains(self):
        for oracle in ('actual-projection','modal-title','modal-text','physical-target-denial','individual-lifecycle'):
            report=fixture_report();obs=report['tuples'][0]['contractObservations'];obs.remove(next(o for o in obs if o['oracle']==oracle))
            with self.assertRaises(ValueError):authority.validate_observations(report)

    def test_trace_glyph_and_modal_negative(self):
        for index in (0,4):
            report=fixture_report();report['tuples'][0]['scenarios'][index]['prompt']='invalid'
            with self.assertRaises(ValueError):authority.validate_observations(report)
        report=fixture_report();report['tuples'][0]['trace'][0]='wrong'
        with self.assertRaises(ValueError):authority.validate_observations(report)


class AuthorityPolicyTests(unittest.TestCase):
    def setUp(self):
        self.work=tempfile.TemporaryDirectory(prefix='gpad-o5-policy-',dir=os.environ.get('TMPDIR'));self.addCleanup(self.work.cleanup)
        self.root=Path(self.work.name)

    def test_png_copy_bytes_crc_dimensions_and_unsafe_names(self):
        def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
        data=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b'\0\xff\0\0'))+chunk(b'IEND',b'')
        images=self.root/'source-images';images.mkdir();path=images/'native-baseline.png';path.write_bytes(data)
        sha=hashlib.sha256(data).hexdigest()
        gates.capture(self.root,'source-images',path.name,sha,[1,1])
        for name in ('../x.png','/x.png','a/b.png'):
            with self.assertRaises(ValueError):gates.capture(self.root,'source-images',name,sha,[1,1])
        with self.assertRaises(ValueError):gates.capture(self.root,'source-images',path.name,sha,[2,1])
        corrupt=data[:-1]+b'X';path.write_bytes(corrupt)
        with self.assertRaisesRegex(ValueError,'integrity'):gates.capture(self.root,'source-images',path.name,hashlib.sha256(corrupt).hexdigest(),[1,1])
        path.unlink();path.symlink_to(images/'missing')
        with self.assertRaises(ValueError):gates.capture(self.root,'source-images',path.name,sha,[1,1])

    def test_full_map_clean_and_required_source(self):
        files={p:dict(bytes=1,sha256=hashlib.sha256(b'x').hexdigest()) for p in ['experiments/gamepad-lab/Scripts/'+n for n in ('LabPresetVerification.cs','LabPresetContractVerification.cs','LabPresetSafetyVerification.cs')]+['experiments/gamepad-lab/Scenes/Main.tscn','experiments/gamepad-lab/project.godot','experiments/gamepad-lab/GamepadLab.csproj','global.json','tools/local_dev/gamepad_matrix_authority.py']}
        mapping=dict(head=HEAD,status='',files=files,workspace=str(self.root))
        def committed_fixture(command, **kwargs):
            return '\0'.join(files)+'\0' if 'ls-tree' in command else b'x'
        with patch.object(authority.subprocess,'check_output',side_effect=committed_fixture):
            authority.validate_map(mapping,HEAD,True)
            files['global.json']['sha256']='0'*64
            with self.assertRaises(ValueError):authority.validate_map(mapping,HEAD,True)
            files['global.json']['sha256']=hashlib.sha256(b'x').hexdigest()
        mapping['status']=' M dirty'
        authority.validate_map(mapping,HEAD,False)
        with self.assertRaises(ValueError):authority.validate_map(mapping,HEAD,True)
        mapping['status']='';del files['global.json']
        with self.assertRaises(ValueError):authority.validate_map(mapping,HEAD,False)

    def test_authority_raw_origin_head_or_receipt_omission_rejected(self):
        path=self.root/'source-matrix-authority.json';path.write_text('{}')
        for key,value in [('origin','exported'),('candidateCommit','b'*40),('rawReport','other.json'),('schema',authority.SCHEMA)]:
            path.write_text(json.dumps({key:value}))
            with self.assertRaises(ValueError):authority.validate_binding(self.root,'source',HEAD,{},gates.json_authority,stable=False)

    def test_probe_and_old_headless_focused_reports_cannot_register(self):
        for coverage,backend in [('discriminatory-binding-only','macOS'),('full-tuples/O5-coverage-audit-still-required','macOS'),('focused-layout-only','macOS'),('full-contract-o5','headless')]:
            path=self.root/'source-preset-matrix.json';path.write_text(json.dumps(dict(schema='arcanum-gamepad-lab-matrix-v1',task='ARC-GPAD-005',status='passed',candidateCommit=HEAD,origin='source',isolatedTestStorage=True,backend=backend,coverage=coverage)))
            with self.assertRaises(ValueError):gates.validate_matrix(self.root,'source',HEAD)

    def test_witness_depth_and_cycle_rejection(self):
        value={}
        for _ in range(130):value={'next':value}
        report=dict(witnessSchema='arcanum-gamepad-geometry-witnesses-v1',witnesses={'0'*64:'{}'},tuples=[dict(labelGeometry=value,settlement=[],scrollObservations=[])])
        with self.assertRaises(ValueError):gates.validate_witnesses(report)


if __name__=='__main__':unittest.main()
