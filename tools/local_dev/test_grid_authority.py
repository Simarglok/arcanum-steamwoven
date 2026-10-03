"""Literal WORLD003 resolver/launcher/lease controls; all payloads synthetic."""
import argparse

import json
import os
from pathlib import Path
import runpy

import unittest
from unittest.mock import patch

from tools.local_dev import config, export_endpoint, launch, manifests, retention
from tools.local_dev import test_local_dev as fixtures
from tools.local_dev import test_world_dev as world


class GridAuthorityTests(unittest.TestCase):
    root: Path
    setUp=fixtures.LocalDevTests.setUp
    tearDown=fixtures.LocalDevTests.tearDown
    conf=fixtures.LocalDevTests.conf
    v2_manifest=fixtures.LocalDevTests.v2_manifest
    authority_fixture=fixtures.LocalDevTests.authority_fixture
    authority_snapshot=fixtures.LocalDevTests.authority_snapshot
    roles=world.WorldDevTests.roles

    def cli(self):
        return runpy.run_path(str(Path(__file__).resolve().parents[1]/'arcanum-local'))

    def assert_no_launch(self, cli, arguments):
        before=self.authority_snapshot()
        for duplicate in ([],['--allow-duplicate']):
            args=cli['parser']().parse_args(['launch','terrain','--mode','world-diagnostic',*arguments,*duplicate])
            with patch.object(launch,'start') as start:
                with self.assertRaises((OSError,ValueError)):cli['command_launch'](args)
                start.assert_not_called()
            self.assertEqual(before,self.authority_snapshot())

    def test_literal_selected_current_all_siblings_and_duplicate_names_remain_fail_closed(self):
        parents=[self.authority_fixture(task=task,tool='terrain') for task in
            ('ARC-WORLD-001','ARC-GPAD-004','ARC-RENDER-006')]
        current=self.authority_fixture(task='ARC-WORLD-003',tool='terrain')
        conf=config.load_config();conf['inputs']=self.roles();config.save_config(conf)
        cli=self.cli();before=self.authority_snapshot()
        args=cli['parser']().parse_args(['launch','terrain','--mode','world-diagnostic','--dry-run','--show-paths'])
        # Actual dry-run goes through resolver and launcher, never patched start.
        cli['command_launch'](args)
        self.assertEqual(before,self.authority_snapshot())
        found,payload,_=manifests.resolve_current(current.parent.parent,'terrain',root_id='test',selected_task='ARC-WORLD-003')
        self.assertEqual(payload['task'],'ARC-WORLD-003');self.assertEqual(found.parent.name,'a'*40)
        for task in ('ARC-RENDER-003','ARC-RENDER-004','ARC-RENDER-005','ARC-DEV-001',
                'ARC-WORLD-001','ARC-GPAD-004','ARC-RENDER-006','ARC-WORLD-003'):
            sibling=current.parent.parent/task/'current-v2.json';sibling.parent.mkdir(exist_ok=True)
            for state in ('dangling','malformed'):
                if state=='dangling':sibling.symlink_to(self.root/'missing-authority')
                else:sibling.write_text('{}')
                with self.subTest(task=task,state=state):self.assert_no_launch(cli,['--dry-run'])
                sibling.unlink()
        duplicate=current.with_name('current-v2.json');duplicate.write_bytes(current.read_bytes())
        self.assert_no_launch(cli,['--dry-run']);duplicate.unlink()
        self.assertTrue(all(p.is_file() for p in parents))

    def test_selection_head_root_task_pointer_and_automation_bindings_reject_before_launch(self):
        current=self.authority_fixture(task='ARC-WORLD-003',tool='terrain')
        conf=config.load_config();conf['inputs']=self.roles();config.save_config(conf)
        cli=self.cli();original=current.read_bytes();pointer=json.loads(original)
        for change in ({'manifestSha256':'b'*64},{'manifestRelativePath':'b'*40+'/manifest.json'},
                {'manifestRelativePath':'../../outside/manifest.json'},{'schema':'OTHER'}):
            current.write_text(json.dumps(pointer|change));self.assert_no_launch(cli,['--dry-run'])
        current.write_bytes(original)
        for selection in ('test:ARC-WORLD-003:'+'b'*40,'wrong:ARC-WORLD-003:'+'a'*40,'test:ARC-RENDER-006:'+'a'*40):
            config.save_config(conf|{'launchSelection':{'terrain':selection}});self.assert_no_launch(cli,['--dry-run'])
        config.save_config(conf)
        path=current.parent/('a'*40)/'manifest.json';originalManifest=path.read_bytes();payload=json.loads(originalManifest)
        for change in ({'rootId':'wrong'},{'task':'ARC-RENDER-006'},
                {'candidate':payload['candidate']|{'commit':'b'*40}},
                {'app':payload['app']|{'engineArgs':['--headless']}},
                {'app':payload['app']|{'ordinaryArgs':['--capture']}}):
            path.write_text(json.dumps(payload|change));current.write_text(json.dumps(pointer|{'manifestSha256':manifests.sha256(path)}))
            self.assert_no_launch(cli,['--dry-run'])
        path.write_bytes(originalManifest);current.write_bytes(original)
        self.assert_no_launch(cli,['--actor-frame','0','--dry-run'])

    def test_world003_finalized_partial_and_unsafe_lease_reject_before_child_and_authority_writes(self):
        path,_=self.v2_manifest(task='ARC-WORLD-003')
        payload=json.loads(path.read_bytes());payload['logicalTool']='terrain'
        for key in ('preflight','observation'):payload['exportBudget'][key]['tool']='terrain'
        path.write_text(json.dumps(payload));config.save_config(self.conf());config.save_registry(config.default_registry())
        args=argparse.Namespace(tool='terrain',root_id='test',task='ARC-WORLD-003',candidate='a'*40,
            manifest=str(path),command=['/usr/bin/false'])
        before=self.authority_snapshot()
        with patch.object(export_endpoint.subprocess,'run') as child,patch.object(retention,'export_preflight') as preflight:
            with self.assertRaisesRegex(ValueError,'finalized'):export_endpoint.run_export(args)
            child.assert_not_called();preflight.assert_not_called()
        self.assertEqual(before,self.authority_snapshot())
        candidate=path.parent.parent/('d'*40);candidate.mkdir();(candidate/'partial.log').write_text('preserved failure')
        args.candidate='d'*40;args.manifest=str(candidate/'manifest.json');before=self.authority_snapshot()
        with patch.object(export_endpoint.subprocess,'run') as child,patch.object(retention,'export_preflight') as preflight:
            with self.assertRaisesRegex(ValueError,'partial'):export_endpoint.run_export(args)
            child.assert_not_called();preflight.assert_not_called()
        self.assertEqual(before,self.authority_snapshot())
        sentinel=self.root/'sentinel';sentinel.write_bytes(b'unchanged')
        for index,kind in enumerate(('link','dangling','fifo','directory','hardlink')):
            candidate=path.parent.parent/f'{index+16:040x}';candidate.mkdir();lock=candidate/'.export-lease.lock'
            if kind=='link':lock.symlink_to(sentinel)
            elif kind=='dangling':lock.symlink_to(self.root/'absent')
            elif kind=='fifo':os.mkfifo(lock)
            elif kind=='directory':lock.mkdir()
            else:os.link(sentinel,lock)
            args.candidate=candidate.name;args.manifest=str(candidate/'manifest.json');before=self.authority_snapshot()
            with patch.object(export_endpoint.subprocess,'run') as child,patch.object(retention,'export_preflight') as preflight:
                with self.subTest(kind=kind),self.assertRaises((OSError,ValueError)):export_endpoint.run_export(args)
                child.assert_not_called();preflight.assert_not_called()
            self.assertEqual(before,self.authority_snapshot())
