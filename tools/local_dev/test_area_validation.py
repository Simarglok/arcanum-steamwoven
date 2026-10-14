"""Independent synthetic controls for the closed RENDER007 coverage gate."""
from copy import deepcopy
import base64
import hashlib
from pathlib import Path
import tempfile
import struct
import unittest

from tools.local_dev import area_validation as area


RESOURCES=dict(Outcome='PASS',SweepSteps=120,SweepCommands=37363,MaxVisible=900,MaxGpuFrames=400,MaxGpuBytes=9000000,
    Reuploads=800,Evictions=700,CpuFrames=2444,CpuBytes=81000000,LoadMilliseconds=[1,2],ManagedBytes=[20,20],
    ResidentBytes=[20,21],Handles=[9,9],CancelledGenerationPublished=False,RetiredGenerationCollected=True)


class AreaValidationTests(unittest.TestCase):
    def test_sector_partition_presence_scope_and_equal_count_swap(self):
        oracle = {'sectors': [dict(id=1,x=1,y=0,core=True,entry={'ordinal':7},payload_sha256='a'*64,
            framing=dict(terminal=0,townmap=3,lights=0,roof_words=0,placeholder=0))], 'missing_context': [[2,0]]}
        rows = [dict(Key={'Packed':1},Coordinate=dict(X=1,Y=0,Packed=1,OriginX=64,OriginY=0),Scope=0,Present=True,
                     DatOrdinal=7,PayloadSha256='a'*64,TerminalCount=0,Townmap=3,LightCount=0,RoofDiscriminator=1,Placeholder=0),
                dict(Key={'Packed':2},Coordinate=dict(X=2,Y=0,Packed=2,OriginX=128,OriginY=0),Scope=1,Present=False,
                     Entry=None,PayloadSha256=None,DatOrdinal=None)]
        area._sector_partition(rows,oracle)
        mutations = [lambda r:r.pop(),lambda r:r[0].update(Present=False),lambda r:r[0].update(Scope=1),
                     lambda r:r[1].update(Scope=0),lambda r:r[0].update(RoofDiscriminator=0),
                     lambda r:r[0]['Coordinate'].update(OriginX=63),lambda r:r[1]['Key'].update(Packed=3)]
        for mutate in mutations:
            bad=deepcopy(rows);mutate(bad)
            with self.subTest(rows=bad), self.assertRaises((ValueError,KeyError)):area._sector_partition(bad,oracle)
        for words in (1,256):
            bad=deepcopy(oracle);bad['sectors'][0]['framing']['roof_words']=words
            with self.subTest(words=words),self.assertRaisesRegex(ValueError,'unsupported roof payload'):
                area._sector_partition(rows,bad)

    def test_input_roles_are_literal_unique_and_fully_bound(self):
        identities=[dict(role=role,length=100+i,sha256=chr(97+i)*64)
                    for i,role in enumerate(('module','resource1','resource2','normal-game-mobile','prototype:29'))]
        rows=[dict(Key=dict(Role=i,Number=29 if i==4 else 0),Length=100+i,Sha256=chr(97+i)*64) for i in range(5)]
        expected={(0,0):(100,'a'*64),(1,0):(101,'b'*64),(2,0):(102,'c'*64),(3,0):(103,'d'*64),(4,29):(104,'e'*64)}
        self.assertEqual(expected,area._input_identities(rows,identities))
        for role in ('mobile','Normal-game-mobile','unknown','prototype:029','prototype:0','prototype:+29'):
            bad=deepcopy(identities);bad[3]['role']=role
            with self.subTest(role=role),self.assertRaisesRegex(ValueError,'input role literal'):
                area._input_identities(rows,bad)
        with self.assertRaisesRegex(ValueError,'duplicate oracle input identity'):
            area._input_identities(rows,identities+[identities[3]])
        with self.assertRaisesRegex(ValueError,'duplicate occurrence'):
            area._input_identities(rows+[rows[3]],identities)
        for mutate in (lambda r:r[3]['Key'].update(Number=1),lambda r:r[3].update(Sha256='a'*64),
                       lambda r:r[3].update(Length=102),lambda r:r[3]['Key'].update(Role=2)):
            bad=deepcopy(rows);mutate(bad)
            with self.subTest(rows=bad),self.assertRaises(ValueError):area._input_identities(bad,identities)

    def test_all_named_counters_and_independent_census(self):
        # Synthetic count-census vectors. No original row or pixels are copied;
        # literal expectations do not import the comparator's constant vector.
        counts=dict(Survey=621,CoreSectors=9,ContextPresent=11,ContextMissing=5,CoreTerrain=36864,
            ContextTerrain=45056,Statics=426,Aggregate=7399,CoreMobiles=88,ContextMobiles=10,
            OutsideMobiles=7301,UnknownMobiles=0,EditorCorrespondence=7399,Prototypes=56,Visuals=2444)
        oracle: dict = dict(counts=dict(survey=621,core_sectors=9,context_present=11,context_missing=5,core_terrain=36864,
            context_terrain=45056,statics=426,aggregate=7399,core_mobiles=88,context_mobiles=10,editor=7399,
            prototype_count=56,selected_requests=2444),survey=[{'id':i} for i in range(621)],
            sectors=[{'core':True}]*9+[{'core':False}]*11,missing_context=list(range(5)),
            terrain=[[0,0,0,0,0,0,True]]*36864+[[0,0,0,0,0,0,False]]*45056,statics=[{}]*426,
            mobiles=[{'scope':'core'}]*88+[{'scope':'context'}]*10,prototypes=[{}]*56,requests=[{}]*2444)
        area._reconciliation(counts,oracle)
        for field in counts:
            bad=dict(counts);bad[field]+=1
            with self.subTest(field=field),self.assertRaisesRegex(ValueError,'named reconciliation vector'):
                area._reconciliation(bad,oracle)
            bad=dict(counts);bad.pop(field)
            with self.subTest(missing=field),self.assertRaisesRegex(ValueError,'named reconciliation vector'):
                area._reconciliation(bad,oracle)
        for bad in (counts|{'Extra':0},counts|{'Survey':True},counts|{'CoreMobiles':10,'ContextMobiles':88}):
            with self.assertRaisesRegex(ValueError,'named reconciliation vector'):area._reconciliation(bad,oracle)
        for field in oracle['counts']:
            bad=deepcopy(oracle);bad['counts'][field]+=1
            with self.subTest(oracle_count=field),self.assertRaisesRegex(ValueError,'independent reconciliation counts'):
                area._reconciliation(counts,bad)
        for field in ('survey','sectors','missing_context','terrain','statics','mobiles','prototypes','requests'):
            bad=deepcopy(oracle);bad[field].pop()
            with self.subTest(census=field),self.assertRaisesRegex(ValueError,'independent reconciliation census'):
                area._reconciliation(counts,bad)

    def test_submission_array_order_is_not_a_dictionary_census(self):
        def command(phase,y,x,second,first,kind,index,offset):
            return dict(Phase=phase,MapY=y,MapX=x,SecondRank=second,FirstRank=first,
                Identity=None if kind is None else dict(Kind=kind,SourceIndex=index,Offset=offset),Visual={'SourceIndex':index})
        rows=[command(0,0,0,0,0,None,0,0),command(4,0,0,-20,20,1,0,0),
              command(4,0,0,-20,20,2,0,0),command(4,0,0,-20,21,1,0,0),command(4,0,1,-100,-100,1,0,0),
              command(4,1,0,-100,-100,1,0,0)]
        area._command_sequence(rows)
        for index in range(len(rows)-1):
            bad=deepcopy(rows);bad[index],bad[index+1]=bad[index+1],bad[index]
            with self.subTest(index=index),self.assertRaisesRegex(ValueError,'command submission sequence'):
                area._command_sequence(bad)

    def test_selected_frame_binds_all_source_descriptor_fields(self):
        visual=dict(Entry={'Ordinal':8},EffectivePalette=1,PhysicalGroup=3,Frame=4,Policy='synthetic',
                    PayloadSha256='a'*64,ViewLength=77,RgbaSha256='b'*64)
        literal=dict(Entry={'Ordinal':8},Palette=1,Group=3,Frame=4,Policy='synthetic',
                     PayloadSha256='a'*64,ViewLength=77,RgbaSha256='b'*64)
        area._frame_binding({'Frame':literal},visual)
        for field in literal:
            bad=deepcopy(literal);bad[field]=None
            with self.subTest(field=field),self.assertRaises(ValueError):area._frame_binding({'Frame':bad},visual)

    def sample_fixture(self):
        key=dict(Kind=0,Sector=dict(Packed=1,SourceSha256='a'*64,RuntimeMap=1),SourceIndex=0,Layer=0,SparseIndex=0,ObjectIdSha256='',Object=None)
        rgba=bytes(channel for y in range(12) for x in range(12) for channel in ((x+1)*5,(y+1)*5,(x+y)*2+5,255))
        visual=dict(Entry={'Ordinal':8},EffectivePalette=0,PhysicalGroup=0,Frame=0,Policy='synthetic',
            PayloadSha256='b'*64,ViewLength=77,RgbaSha256=hashlib.sha256(rgba).hexdigest(),Width=12,Height=12)
        frame=dict(Entry={'Ordinal':8},Palette=0,Group=0,Frame=0,Policy='synthetic',PayloadSha256='b'*64,
            ViewLength=77,RgbaSha256=hashlib.sha256(rgba).hexdigest())
        draw=dict(Identity=None,Frame=frame,Destination=dict(X=0,Y=0,Width=8,Height=8),
                  Source=dict(X=2,Y=3,Width=8,Height=8),FlipX=False,FlipY=True)
        bits=lambda value:struct.unpack('<I',struct.pack('<f',value))[0]
        samples=[dict(Visual=key,**deepcopy(draw),Zoom=z,SourceX=3,SourceY=9,ScreenX=screen,ScreenY=screen,
            SourceU=ratio,SourceV=ratio,SamplingDomain='exact-binary-rational-center-discrete-reflection-v1',
            Snap=False,TransformBits=[bits(z),bits(z),bits(100),bits(100)],RawTransformBits=[bits(z),bits(z),bits(100),bits(100)],
            Expected=[20,50,29,255],Actual=[20,50,29,255]) for z,screen,ratio in ((.5,100,['1','1']),(1,101,['3','2']),(2,103,['7','4']))]
        report=dict(PixelSamples=samples,SampleFrameProofs=[dict(Frame=frame,Width=12,Height=12,RgbaBase64=base64.b64encode(rgba).decode())],
            Viewports=[dict(X=x,Y=y) for x,y in ((1280,800),(1440,900),(800,600),(1920,1080))])
        return report,{area.visual_key(key):draw},{area.visual_key(key):visual},{('terrain',1)}

    def test_required_sample_groups_zooms_crop_flips_rgba_and_viewports(self):
        report,commands,visuals,groups=self.sample_fixture()
        area._sample_coverage(report,commands,{},visuals,groups)
        mutations=[lambda r:r['PixelSamples'].pop(0),lambda r:r['PixelSamples'][0].update(Zoom=3),
            lambda r:r['Viewports'].pop(),lambda r:r['PixelSamples'][0].update(SourceX=1),
            lambda r:r['PixelSamples'][0].update(ScreenX=1280),lambda r:r['PixelSamples'][0].update(FlipX=True),
            lambda r:r['PixelSamples'][0].update(Frame={'synthetic':2}),
            lambda r:r['PixelSamples'][0].update(Actual=[54,62,73,0])]
        for mutate in mutations:
            bad=deepcopy(report);mutate(bad)
            with self.subTest(report=bad),self.assertRaises((ValueError,KeyError)):area._sample_coverage(bad,commands,{},visuals,groups)
        with self.assertRaises(ValueError):area._sample_coverage(report,commands,{},visuals,groups|{('terrain',2)})

    def test_sample_mapping_and_private_frame_proofs_reject_exact_frontiers(self):
        report,commands,visuals,groups=self.sample_fixture()
        mutations=[('sample nearest rule',lambda r:r['PixelSamples'][0].update(SamplingDomain='old-uniform-support')),
            ('sample effective transform',lambda r:r['PixelSamples'][0]['TransformBits'].__setitem__(2,0)),
            ('sample exact coordinate ratio',lambda r:r['PixelSamples'][0].update(SourceU=['2','1'])),
            ('sample independent nearest source index',lambda r:r['PixelSamples'][0].update(SourceX=4)),
            ('sample independent decoded RGBA',lambda r:r['PixelSamples'][0].update(Expected=[21,50,29,255],Actual=[21,50,29,255])),
            ('sample proof dimensions',lambda r:r['SampleFrameProofs'][0].update(Width=13)),
            ('sample frame proof missing',lambda r:r.update(SampleFrameProofs=[])),
            ('sample proof independent RGBA digest',lambda r:r['SampleFrameProofs'][0].update(RgbaBase64=base64.b64encode(bytes(576)).decode())),
            ('sample coordinate integer',lambda r:r['PixelSamples'][0].update(ScreenX=True)),
            ('sample transform finite',lambda r:r['PixelSamples'][0]['RawTransformBits'].__setitem__(0,0x7f800000))]
        for frontier,mutate in mutations:
            bad=deepcopy(report);mutate(bad)
            with self.subTest(frontier=frontier),self.assertRaisesRegex(ValueError,frontier):
                area._sample_coverage(bad,commands,{},visuals,groups)

    def test_safe_receipt_requires_exact_task_head_host_and_complete_observation(self):
        head='a'*40
        good=dict(schema='arcanum-area-validation-v1',task='ARC-RENDER-007',candidateCommit=head,host='source',
            outcome='PASS',exitCode=0,graphical=True,oracleSha256=area.ORACLE_SHA256,originalOccurrenceCoverage='PASS',
            originalAppearanceCoverage='PASS',drawCoverage='PASS',coreTerrain=36864,coreBodies=499,notApplicable=15,submitted=37363,
            resources=area.resource_summary(RESOURCES))
        area.validate_report(good,head,'source')
        for change in ({'task':'ARC-WORLD-003'},{'candidateCommit':'b'*40},{'host':'exported'}, {'graphical':False},
                       {'exitCode':1},{'drawCoverage':'NOT_VERIFIED'},{'submitted':37362},{'resources':None}):
            with self.subTest(change=change),self.assertRaises(ValueError):area.validate_report(good|change,head,'source')

    def test_resource_observation_is_bounded_and_reduced_to_safe_scalars(self):
        summary=area.resource_summary(RESOURCES)
        self.assertEqual(('PASS',37363,[9,9]),(summary['outcome'],summary['sweepCommands'],summary['handles']))
        for change in ({'Outcome':'FAIL'},{'SweepCommands':37362},{'MaxGpuFrames':2445},{'MaxGpuBytes':81853097},
                       {'CpuFrames':2445},{'CancelledGenerationPublished':True},{'Handles':[9,10]},
                       {'ManagedBytes':[0,40500000]},{'RetiredGenerationCollected':False},{'ResidentBytes':[0]},{'LoadMilliseconds':[1]},
                       {'Reuploads':0},{'SweepSteps':'120'}):
            with self.subTest(change=change),self.assertRaises(ValueError):area.resource_summary(RESOURCES|change)

    def test_original_oracle_is_pinned_before_report_is_read(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'oracle.json';path.write_text('{}')
            with self.assertRaisesRegex(ValueError,'independent oracle identity'):area.compare_original({},path)


if __name__ == '__main__':
    unittest.main()
