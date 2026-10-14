#!/usr/bin/env python3
"""Bounded fresh-process preference probes; only explicit owned storage is accessed."""
from __future__ import annotations
import argparse, datetime, hashlib, json, os, pathlib, subprocess

def main() -> int:
    p=argparse.ArgumentParser();p.add_argument('--report',type=pathlib.Path,required=True);p.add_argument('--work',type=pathlib.Path,required=True);p.add_argument('--head',required=True);p.add_argument('command',nargs=argparse.REMAINDER);a=p.parse_args()
    command=a.command[1:] if a.command[:1]==['--'] else a.command
    if not command or a.work.exists() or a.report.exists():raise ValueError('fresh owned work/report and executable required')
    a.work.mkdir(parents=True);state=a.work/'state';state.mkdir();settings=state/'settings.json';legacy=state/'settings.json.legacy-v1'
    env=dict(os.environ,GAMEPAD_LAB_SETTINGS_TEST_PATH=str(settings.resolve()),ARCANUM_GAMEPAD_CANDIDATE=a.head)
    events=[]
    def run(operation):
        report=a.work/(operation+'-'+str(len(events))+'.json');log=report.with_suffix('.log');start=datetime.datetime.now(datetime.timezone.utc).isoformat()
        with log.open('xb') as stream:result=subprocess.run([*command,'--','--persistence',operation,str(report.resolve())],env=env,stdout=stream,stderr=subprocess.STDOUT,timeout=25)
        events.append({'operation':operation,'startedAt':start,'finishedAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),'exitCode':result.returncode,'log':log.name,'logSha256':hashlib.sha256(log.read_bytes()).hexdigest(),'report':report.name})
        print(log.read_text(),flush=True)
        if result.returncode!=0 or any(marker in log.read_bytes() for marker in (b'ERROR:', b'SCRIPT ERROR:', b'Unhandled exception')):raise ValueError('fresh process failed '+operation)
        r=json.loads(report.read_text());assert r['status']=='passed' and r['isolatedTestStorage'] is True
        if 'storedJson' in r:assert r['sha256']==hashlib.sha256(settings.read_bytes()).hexdigest() and r['candidateCommit']==a.head
        return r
    axes={'version':1,'cameraDeadzone':.3,'actorDeadzone':.1,'cameraSensitivity':.75,'actorSensitivity':1.5,'invertCameraX':False,'invertCameraY':True,'movementMode':1}
    legacy.write_text(json.dumps(axes));original=legacy.read_bytes()
    def check_axis():
        r=json.loads(settings.read_text());assert r['schema']=='arcanum-gamepad-lab-settings-v2' and r['version']==2 and r['movementMode']=='NearbyGoal'
        for k in ('cameraDeadzone','actorDeadzone','cameraSensitivity','actorSensitivity','invertCameraX','invertCameraY'):assert r[k]==axes[k]
        return r
    try:
        run('verify-migration');check_axis();assert legacy.read_bytes()==original
        run('verify-migrated-v2');check_axis()
        for preset in ('contextual','quick-slots','radial'):
            run('write-'+preset);run('verify-'+preset);assert check_axis()['activePresetId']==preset
        run('write-copy');run('verify-copy');r=check_axis();assert r['activePresetId']=='user-copy' and len(r['slots'])==10
        assert r['slots']['slot-09']=='synthetic.spell.ally' and r['slots']['slot-10'] is None
        stored=settings.read_bytes();run('reject-copy');assert settings.read_bytes()==stored
        run('reset-copy');run('verify-reset-copy');r=check_axis();assert r['userCopy']['shoulderRoles']=={'lb':'lb','rb':'rb'}
        run('write-radial-widget-copy');run('verify-radial-widget-copy');r=check_axis()
        assert r['activePresetId']=='user-copy' and r['userCopy']['basePresetId']=='radial'
        assert r['userCopy']['shoulderRoles']=={'lb':'rb','rb':'lb'}
        wheels=[b for b in r['userCopy']['bindings'] if b['actionId']=='wheel-open']
        assert [(b['context'],b['button'],b['modifiers'],b['slotId']) for b in wheels]==[('world','rb',[],None),('world','rb',['rb'],None)]
        stored=settings.read_bytes();run('reject-radial-widget-copy');assert settings.read_bytes()==stored
        run('reset-radial-widget-copy');run('verify-reset-radial-widget-copy');r=check_axis()
        assert r['userCopy']['shoulderRoles']=={'lb':'lb','rb':'rb'}
        for malformed in ('{','{"schema":"arcanum-gamepad-lab-settings-v2","version":999}'):
            settings.write_text(malformed);run('verify-corrupt');assert settings.read_text()==malformed and legacy.read_bytes()==original
        run('write-contextual');stored=settings.read_bytes();state.chmod(0o500)
        try:run('save-io-failure')
        finally:state.chmod(0o700)
        assert settings.read_bytes()==stored and not tuple(state.glob('*.tmp-*'))
        # Preserve the earlier three-operation public seam as an additional restart oracle.
        run('write');run('verify-nondefault');run('verify-default')
        outcome='passed';error=None
    except Exception as e:outcome='failed';error=str(e)
    finally:state.chmod(0o700)
    a.report.write_text(json.dumps({'schema':'arcanum-gamepad-persistence-suite-v1','task':'ARC-GPAD-005','candidateCommit':a.head,'status':outcome,'isolatedTestStorage':True,'operations':events,'legacyUnchanged':legacy.read_bytes()==original,'error':error},indent=2)+'\n')
    return 0 if outcome=='passed' else 1

if __name__=='__main__':raise SystemExit(main())
