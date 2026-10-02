"""Literal RENDER006 authority oracles using public synthetic payloads only."""
import json
import runpy
from pathlib import Path
from unittest.mock import patch
from tools.local_dev import config, launch, manifests, retention
from tools.local_dev.human_validation import validate_report
from tools.local_dev.test_world_dev import WorldDevTests

NAMES = ('locked-build','source-headless','source-graphical','export','exported-headless',
         'exported-graphical','codesign-deep-strict','focused-tests','source-world-original',
         'exported-world-original','source-world-persistence','exported-world-persistence',
         'dual-hostfxr','human-metadata','source-human-original','exported-human-original')


def report(host='source', kind='human'):
    return dict(schema='arcanum-human-validation-v1', task='ARC-RENDER-006',
        candidateCommit='a'*40, host=host, kind=kind, profile='HumanFixed',outcome='PASS',exitCode=0,
        originalIdentity='authenticated-actor-role',physicalRows=88,distinctKeys=55,rgbaBytes=457676,
        metadata='PASS',allUsedRgba='PASS',graphical=True, actualViewports=[[1280,800],[1440,900],[800,600]],
        renderedSelections=264,negativeControls='PASS',defaultZoom=2,progression='PASS')


class HumanDevTests(WorldDevTests):
    def test_render006_selected_current_coexists_and_unsafe_sibling_still_rejects(self):
        old=self.authority_fixture(task='ARC-GPAD-004',tool='terrain')
        current=self.authority_fixture(task='ARC-RENDER-006',tool='terrain')
        conf=config.load_config();conf['inputs']=self.roles();config.save_config(conf)
        cli=runpy.run_path(str(Path(__file__).resolve().parents[1]/'arcanum-local'))
        args=cli['parser']().parse_args(['launch','terrain','--mode','world-diagnostic','--dry-run'])
        before=self.authority_snapshot()
        with patch.object(launch,'start',return_value={}) as start:
            cli['command_launch'](args);start.assert_called_once()
            launch.assert_ordinary(start.call_args.args[1])
        self.assertEqual(before,self.authority_snapshot())
        self.assertIn('ARC-RENDER-006',manifests.SUPPORTED_TOOLS['terrain'])
        self.assertNotIn('ARC-RENDER-006',manifests.SUPPORTED_TOOLS['gamepad'])
        old.write_text('{}')
        with patch.object(launch,'start') as start:
            with self.assertRaises(ValueError):cli['command_launch'](args)
            start.assert_not_called()
        with self.assertRaises(ValueError):manifests.resolve_current(current.parent.parent,'terrain')

    def test_human_report_cannot_be_old_world_marker_headless_wrong_identity_or_partial(self):
        good=report();validate_report(good,'a'*40,'source','human')
        changes=({'schema':'arcanum-world-validation-v1'},{'task':'ARC-GPAD-004'},
                 {'candidateCommit':'b'*40},{'host':'exported'},{'profile':'MonsterRegression'},
                 {'outcome':'NOT_VERIFIED'},{'exitCode':1},{'graphical':False},
                 {'originalIdentity':'synthetic'},{'physicalRows':55},{'distinctKeys':184},
                 {'rgbaBytes':0},{'metadata':'FAIL'},{'allUsedRgba':'NOT_VERIFIED'},
                 {'actualViewports':[[1280,800]]},{'renderedSelections':16},
                 {'negativeControls':'NOT_VERIFIED'},{'defaultZoom':8},{'progression':'FAIL'})
        for change in changes:
            with self.subTest(change=change),self.assertRaises(ValueError):validate_report(good|change,'a'*40,'source','human')

    def test_every_sixteen_gate_and_each_report_binding_reject_before_registration(self):
        path,_=self.v2_manifest(task='ARC-RENDER-006')
        payload=json.loads(path.read_bytes());payload['logicalTool']='terrain'
        for name,key in (('export-preflight.json','preflight'),('export-observation.json','observation')):
            payload['exportBudget'][key]['tool']='terrain'
            file=path.parent/'evidence'/name;file.write_text(json.dumps(payload['exportBudget'][key]))
            entry=next(e for e in payload['evidence'] if e['relativePath'].endswith(name))
            entry.update(bytes=file.stat().st_size,sha256=manifests.sha256(file))
        for name,host,kind in (('human-metadata.log','source','human-metadata'),('source-human-original.log','source','human'),('exported-human-original.log','exported','human')):
            file=path.parent/'evidence'/name;file.write_text(json.dumps(report(host,kind)))
            payload['evidence'].append(dict(relativePath='evidence/'+name,bytes=file.stat().st_size,sha256=manifests.sha256(file)))
        payload['checks']=[dict(name=name,outcome='PASS') for name in NAMES]
        payload['delivery']=dict(originalInputModes='PASS')
        identity='test:ARC-RENDER-006:'+'a'*40
        for name in NAMES:
            for outcome in ('MISSING','FAIL','NOT_VERIFIED'):
                changed=payload|{'checks':[c|{'outcome':outcome} if c['name']==name else c for c in payload['checks'] if outcome!='MISSING' or c['name']!=name]}
                path.write_text(json.dumps(changed));before=self.authority_snapshot();registry=config.default_registry()
                with self.assertRaises(ValueError):retention.register_export(registry,conf=self.conf(),tool='terrain',candidate_id=identity,manifest=path)
                self.assertEqual(registry,config.default_registry());self.assertEqual(before,self.authority_snapshot())
        path.write_text(json.dumps(payload));registry=config.default_registry()
        retention.register_export(registry,conf=self.conf(),tool='terrain',candidate_id=identity,manifest=path)
        self.assertEqual(registry['builds'][identity]['lifecycle'],'exported')
        for name in ('human-metadata.log','source-human-original.log','exported-human-original.log'):
            file=path.parent/'evidence'/name;original=file.read_bytes()
            file.write_text('{}');before=self.authority_snapshot();registry=config.default_registry()
            with self.assertRaises(ValueError):retention.register_export(registry,conf=self.conf(),tool='terrain',candidate_id=identity,manifest=path)
            self.assertEqual(registry,config.default_registry());self.assertEqual(before,self.authority_snapshot());file.write_bytes(original)
