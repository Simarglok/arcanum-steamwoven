"""Literal GPAD-004 closed terrain mapping/gate regressions."""
import json
import runpy
from pathlib import Path
from unittest.mock import patch
from tools.local_dev import config, launch, retention, manifests
from tools.local_dev.test_world_dev import WorldDevTests


class WorldZoomDevTests(WorldDevTests):
    def test_explicit_gpad_coexists_without_weakening_old_ambiguity_or_safety(self):
        old=self.authority_fixture(task='ARC-WORLD-001',tool='terrain')
        current=self.authority_fixture(task='ARC-GPAD-004',tool='terrain')
        conf=config.load_config(); conf['inputs']=self.roles(); config.save_config(conf)
        cli=runpy.run_path(str(Path(__file__).resolve().parents[1]/'arcanum-local'))
        args=cli['parser']().parse_args(['launch','terrain','--mode','world-diagnostic','--dry-run'])
        before=self.authority_snapshot()
        with patch.object(launch,'start',return_value={}) as start:
            cli['command_launch'](args); start.assert_called_once()
        self.assertEqual(before,self.authority_snapshot())
        conf['launchSelection']['terrain']='test:ARC-WORLD-001:'+'a'*40; config.save_config(conf)
        with patch.object(launch,'start',return_value={}) as start:
            cli['command_launch'](args); start.assert_called_once()
        conf['launchSelection']['terrain']='test:ARC-GPAD-004:'+'a'*40; config.save_config(conf)
        with self.assertRaises(ValueError): manifests.resolve_current(current.parent.parent,'terrain',root_id='test')
        duplicate=current.parent/'current-v2.json'; duplicate.write_bytes(current.read_bytes())
        with patch.object(launch,'start') as start:
            with self.assertRaises(ValueError): cli['command_launch'](args)
            start.assert_not_called()
        duplicate.unlink()
        original=old.read_bytes(); old.write_text('{}')
        with patch.object(launch,'start') as start:
            with self.assertRaises(ValueError): cli['command_launch'](args)
            start.assert_not_called()
        old.write_bytes(original)
        payload_path=current.parent/('a'*40)/'manifest.json'
        payload=json.loads(payload_path.read_bytes()); payload['task']='ARC-WORLD-001'
        payload_path.write_text(json.dumps(payload))
        with patch.object(launch,'start') as start:
            with self.assertRaises(ValueError): cli['command_launch'](args)
            start.assert_not_called()

    def test_gpad004_exact_terrain_launcher_eligibility(self):
        self.authority_fixture(task='ARC-GPAD-004', tool='terrain')
        conf=config.load_config(); conf['inputs']=self.roles(); config.save_config(conf)
        cli=runpy.run_path(str(Path(__file__).resolve().parents[1]/'arcanum-local'))
        args=cli['parser']().parse_args(['launch','terrain','--mode','world-diagnostic','--dry-run'])
        with patch.object(launch,'start',return_value={}) as start:
            cli['command_launch'](args)
            observed=start.call_args.args[1]
            self.assertEqual(observed[observed.index('--mode')+1],'world-diagnostic')
            launch.assert_ordinary(observed)
        self.assertIn('ARC-GPAD-004',manifests.SUPPORTED_TOOLS['terrain'])
        self.assertNotIn('ARC-GPAD-004',manifests.SUPPORTED_TOOLS['gamepad'])

    def test_gpad004_all_thirteen_gates_fail_closed_before_registration(self):
        path,_=self.v2_manifest(task='ARC-GPAD-004')
        payload=json.loads(path.read_bytes()); payload['logicalTool']='terrain'
        budget=payload['exportBudget']
        for name,key in (('export-preflight.json','preflight'),('export-observation.json','observation')):
            budget[key]['tool']='terrain'
            record=path.parent/'evidence'/name; record.write_text(json.dumps(budget[key]))
            entry=next(e for e in payload['evidence'] if e['relativePath'].endswith(name))
            entry.update(bytes=record.stat().st_size,sha256=manifests.sha256(record))
        names=('locked-build','source-headless','source-graphical','export','exported-headless','exported-graphical','codesign-deep-strict','focused-tests','source-world-original','exported-world-original','source-world-persistence','exported-world-persistence','dual-hostfxr')
        payload['checks']=[{'name':name,'outcome':'PASS'} for name in names]
        payload['delivery']={'originalInputModes':'PASS'}
        identity='test:ARC-GPAD-004:'+'a'*40
        for name in names:
            for outcome in ('MISSING','FAIL','NOT_VERIFIED'):
                changed=payload|{'checks':[c|{'outcome':outcome} if c['name']==name else c for c in payload['checks'] if outcome!='MISSING' or c['name']!=name]}
                path.write_text(json.dumps(changed)); before=self.authority_snapshot(); registry=config.default_registry()
                with self.assertRaises(ValueError): retention.register_export(registry,conf=self.conf(),tool='terrain',candidate_id=identity,manifest=path)
                self.assertEqual(registry,config.default_registry()); self.assertEqual(before,self.authority_snapshot())
        path.write_text(json.dumps(payload)); registry=config.default_registry()
        retention.register_export(registry,conf=self.conf(),tool='terrain',candidate_id=identity,manifest=path)
        self.assertEqual(registry['builds'][identity]['lifecycle'],'exported')
