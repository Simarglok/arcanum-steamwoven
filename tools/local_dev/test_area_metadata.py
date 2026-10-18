"""Public, independently authored metadata-stage controls, NOT native coverage.

No original bytes/rows/pixels and no renderer helpers construct expectations.
The small explicit census enters only the same private metadata-stage seam;
compare_original still pins the accepted corpus and executes its pixel stage.
"""
from copy import deepcopy
import json
from pathlib import Path
import tempfile
import unittest

from tools.local_dev import area_validation as area


def sector(sha, x=1452, y=1290):
    return dict(SourceSha256=sha,RuntimeMap=1,Packed=x+(y<<26))


def visual(kind, source, index, body=None) -> dict:
    return dict(Kind=kind,Sector=source,SourceIndex=index,ObjectIdSha256=body['ObjectIdSha256'] if body else '',
                Layer=0,SparseIndex=0,Object=body)


def entry(ordinal, role=2):
    return dict(Source=dict(Role=role,Number=0),Ordinal=ordinal,Flags=2,Offset=ordinal*100,
                StoredBytes=100,DecodedBytes=200,NameSha256='c'*64)


def oracle_entry(ordinal):
    return dict(ordinal=ordinal,flags=2,offset=ordinal*100,stored=100,decoded=200,name_sha256='c'*64)


def fixture():
    counts=dict(Survey=3,CoreSectors=1,ContextPresent=1,ContextMissing=1,CoreTerrain=3,ContextTerrain=1,
        Statics=1,Aggregate=3,CoreMobiles=2,ContextMobiles=1,OutsideMobiles=0,UnknownMobiles=0,
        EditorCorrespondence=3,Prototypes=3,Visuals=6)
    report: dict=dict(Schema='arcanum-area-render-coverage-v1',Task='ARC-RENDER-007',RendererObserved=True,
        OriginalRuntimeParity='NOT_VERIFIED',CatalogueMap=5000,RuntimeMap=1,
        Profile='arc-data-009-complete-file-effective-source-v1',Chart=dict(OriginX=92928,OriginY=82560),
        Reconciliation=counts,Inputs=[],Sectors=[],Terrain=[],Objects=[],Visuals=[],Coverage=[],Commands=[])
    oracle: dict=dict(counts=dict(survey=3,core_sectors=1,context_present=1,context_missing=1,core_terrain=3,
        context_terrain=1,statics=1,aggregate=3,core_mobiles=2,context_mobiles=1,editor=3,
        prototype_count=3,selected_requests=6),survey=[{'id':i} for i in (1,2,3)],
        identities=[],sectors=[],missing_context=[[1454,1290]],terrain=[],statics=[],mobiles=[],prototypes=[],requests=[])
    for role,number,sha in ((0,0,'a'),(1,0,'b'),(2,0,'c'),(3,0,'d'),(4,29,'e'),(4,30,'f'),(4,31,'9')):
        literal=('module','resource1','resource2','normal-game-mobile')[role] if role<4 else f'prototype:{number}'
        report['Inputs'].append(dict(Key=dict(Role=role,Number=number),Length=100,Sha256=sha*64))
        oracle['identities'].append(dict(role=literal,length=100,sha256=sha*64))
    for x,core,ordinal in ((1452,True,7),(1453,False,8)):
        sk=sector('a'*64,x)
        framing=dict(terminal=1 if core else 0,townmap=2 if core else 0,lights=0,roof_words=0,placeholder=0)
        oracle['sectors'].append(dict(id=sk['Packed'],x=x,y=1290,core=core,entry=oracle_entry(ordinal),
            payload_sha256='1'*64,framing=framing))
        report['Sectors'].append(dict(Key=sk,Coordinate=dict(X=x,Y=1290,Packed=sk['Packed'],OriginX=x*64,OriginY=82560),
            Scope=0 if core else 1,Present=True,DatOrdinal=ordinal,PayloadSha256='1'*64,TerminalCount=framing['terminal'],
            Townmap=framing['townmap'],LightCount=0,RoofDiscriminator=1,Placeholder=0,Entry=entry(ordinal,0)))
    missing=sector('a'*64,1454)
    report['Sectors'].append(dict(Key=missing,Coordinate=dict(X=1454,Y=1290,Packed=missing['Packed'],OriginX=93056,OriginY=82560),
        Scope=1,Present=False,Entry=None,PayloadSha256=None,DatOrdinal=None))
    ep=dict(sector=sector('a'*64)['Packed'],ordinal=0,prototype=29,sap=1,script=2,catalogue_destination=3,
            runtime_destination=4,destination=[5,6],object_sha256='0'*64)
    oracle['endpoint']=ep
    report['Endpoints']=[dict(Sector=ep['sector'],Ordinal=0,Prototype=29,Sap=1,Script=2,CatalogueDestination=3,
        RuntimeDestination=4,ArrivalX=5,ArrivalY=6,SourceObjectIdSha256='0'*64)]

    def selected(key,raw,lookup,ordinal,width,height,hx=0,hy=0,ox=0,oy=0,scale=100,group=0,frame=0,palette=0,mirror=False,chain=None):
        row=dict(Key=key,RawArt=raw,LookupArt=lookup,Entry=entry(ordinal),PayloadSha256='2'*64,ViewLength=200,
            Policy='strict-full-entry',NominalFacing=3 if mirror else 0,SourceGroup=group,PhysicalGroup=group,Frame=frame,
            RequestedPalette=palette,EffectivePalette=palette,Mirrored=mirror,Width=width,Height=height,HotX=hx,HotY=hy,
            ArtOffsetX=-17,ArtOffsetY=21,ObjectOffsetX=ox,ObjectOffsetY=oy,Scale=scale,EncodedBytes=50,
            RgbaBytes=width*height*4,RgbaSha256='3'*64)
        name=f"static:{key['Sector']['Packed']}:{key['SourceIndex']}" if key['Kind']==1 else f"mobile:{key['SourceIndex']}" if key['Kind']==2 else f'terrain:({ordinal}, {group}, {frame}, {palette})'
        oracle['requests'].append(dict(key=name,source='resource2',entry=oracle_entry(ordinal),full_sha256='2'*64,
            view_length=200,policy='strict-full-entry',chain=chain,oracle=dict(width=width,height=height,hot_x=hx,hot_y=hy,
            offset_x=-17,offset_y=21,group=group,frame=frame,requested_palette=palette,effective_palette=palette,
            encoded_bytes=50,rgba_bytes=width*height*4,rgba_sha256='3'*64)))
        report['Visuals'].append(row)
        return row

    def command(vk,descriptor,identity,src,dst,phase,x,y,first=0,second=0,flipx=False,flipy=False,additive=False):
        return dict(Visual=vk,Identity=identity,Source=src,Destination=dst,Phase=phase,MapX=x,MapY=y,
            FirstRank=first,SecondRank=second,FlipX=flipx,FlipY=flipy,Alpha=255,Additive=additive,
            Frame=dict(Entry=descriptor['Entry'],Palette=descriptor['EffectivePalette'],Group=descriptor['PhysicalGroup'],
            Frame=descriptor['Frame'],Policy='strict-full-entry',PayloadSha256='2'*64,ViewLength=200,RgbaSha256='3'*64))

    for index,raw,ordinal,lookup,frame,x,dst in (
            (0,144,10,128,0,92928,dict(X=1,Y=0,Width=78,Height=40)),
            (1,193,20,192,0,92929,dict(X=-39,Y=20,Width=78,Height=40)),
            (2,0xB0000002,30,0xB0000002,1,92930,dict(X=-79,Y=40,Width=80,Height=40))):
        vk=visual(0,sector('a'*64),index)
        descriptor=selected(vk,raw,lookup,ordinal,83 if index==2 else 78,45 if index==2 else 40,
            frame=frame,palette=1 if index==0 else 0)
        # Wire oracle column3 is an index into requests, NOT a DAT ordinal.
        oracle['terrain'].append([vk['Sector']['Packed'],index,raw,index,x,82560,True])
        report['Terrain'].append(dict(Key=vk,SelectedVisual=vk,RawArt=raw,LookupArt=lookup,LocalX=index,LocalY=0,
            WorldX=x,WorldY=82560,Scope=0))
        report['Coverage'].append(dict(Identity=None,Visual=vk,Disposition=1 if index==2 else 0,Reason='CoreTerrain'))
        report['Commands'].append(command(vk,descriptor,None,dict(X=0,Y=0,Width=80 if index==2 else 78,Height=40),dst,
            1 if index==2 else 0,x,82560,flipx=index==1))
    context_vk=visual(0,sector('a'*64,1453),0)
    oracle['terrain'].append([context_vk['Sector']['Packed'],0,144,0,92992,82560,False])
    report['Terrain'].append(dict(Key=context_vk,SelectedVisual=report['Terrain'][0]['Key'],RawArt=144,LookupArt=128,
        LocalX=0,LocalY=0,WorldX=92992,WorldY=82560,Scope=1))
    report['Coverage'].append(dict(Identity=None,Visual=context_vk,Disposition=5,Reason='DATA009TerrainScope'))

    for kind,index,typ,proto,flags,raw,ox,oy,scale,blit,width,height,hx,hy,x,scope in (
            (1,0,3,29,132,0x40000000,-3,5,85,0x13,31,44,-7,9,92928,0),
            (2,4,9,30,0x1000,0x60000020,0,0,100,0,10,23,1,2,92929,0),
            (2,5,16,31,0,0x20001810,0,0,100,0,27,35,4,7,92929,0),
            (2,6,16,31,0,0x20001810,0,0,100,0,27,35,4,7,92992,1)):
        identity=dict(SourceSha256=('a' if kind==1 else 'd')*64,RuntimeMap=1,Kind=kind,
            Sector=sector('a'*64)['Packed'] if kind==1 else None,SourceIndex=index,Offset=100+index*100,
            Length=90,ObjectIdSha256=str(index)*64)
        values={0:raw,1:0 if kind==1 else x+(82560<<32),2:ox,3:oy,8:blit,11:scale,18:flags,36:512 if kind==1 else 0}
        fields: dict={str(slot):dict(presence='value',value=value,origin='instance') for slot,value in values.items()}
        source=dict(offset=identity['Offset'],length=90,own_sha256=identity['ObjectIdSha256'],sha256='4'*64,
            semantic_sha256='5'*64,parent_sha256='6'*64,parent_number=proto,type=typ,world_x=x,world_y=82560,
            fields=fields,effective=deepcopy(fields))
        if kind==1:
            source.update(sector_id=identity['Sector'],ordinal=index);oracle['statics'].append(source)
        else:
            source.update(index=index,scope='context' if scope else 'core');oracle['mobiles'].append(source)
        copied=[dict(Slot=slot,Presence=2,Origin=0,Number=value,Elements=[],Fingerprint=None) for slot,value in values.items()]
        if kind==1:
            fields['9']=dict(presence='null',origin='instance')
            fields['43']=dict(presence='array',origin='instance',count=1,width=4,entries=[dict(key=5,value=-1)])
            source['effective']=deepcopy(fields)
            copied.extend([dict(Slot=9,Presence=1,Origin=0,Number=None,Elements=[],Fingerprint=None),
                dict(Slot=43,Presence=4,Origin=0,Number=None,Elements=[dict(Key=5,Words=[0xffffffff])],Fingerprint=None)])
        report['Objects'].append(dict(Key=identity,Version=119,Type=typ,PrototypeNumber=proto,PrototypeReferenceSha256='6'*64,
            PrototypeOwnSha256='7'*64,RawSha256='4'*64,SemanticSha256='5'*64,Scope=scope,SerializedLocation=values[1],
            WorldX=x,WorldY=82560,Disposition=3 if scope else 1 if flags&0x1000 else 0,
            DirectFields=copied,EffectiveFields=deepcopy(copied),SchemaReady=True,PlacementReady=True,RequiredLayersReady=True))
        if scope:
            report['Coverage'].append(dict(Identity=identity,Visual=None,Disposition=5,Reason='DATA009Scope'));continue
        vk=visual(kind,sector(identity['SourceSha256']),index,identity)
        mirror=typ==16;group=5 if mirror else 0;palette=1 if mirror else 2 if typ==9 else 0
        chain=dict(raw_current_aid=raw,object_offset_x=ox,object_offset_y=oy,scale=scale,nominal_facing=3 if mirror else 0,
            source_mirrored=mirror,requested_group=group)
        descriptor=selected(vk,raw,raw&~0x30 if typ==9 else raw,40+index,width,height,hx,hy,ox,oy,scale,group,palette=palette,mirror=mirror,chain=chain)
        if typ==9:
            report['Coverage'].append(dict(Identity=identity,Visual=vk,Disposition=6,Reason='ExteriorSectorInsertionRefusedByInventory'));continue
        report['Coverage'].append(dict(Identity=identity,Visual=vk,Disposition=4 if kind==1 else 2,Reason='DATA009ResolvedBody'))
        # Literal independently evaluated signed two-stage truncation and ranks;
        # ART offsets are deliberately NOT placement offsets.
        dst=dict(X=39,Y=22,Width=13,Height=18) if kind==1 else dict(X=-4,Y=33,Width=27,Height=35)
        report['Commands'].append(command(vk,descriptor,identity,dict(X=0,Y=0,Width=width,Height=height),dst,
            2 if kind==1 else 4,x,82560,25 if kind==1 else 20,-17 if kind==1 else -20,
            flipx=True,flipy=kind==1,additive=kind==1))
    oracle['prototypes']=[dict(own_number=n,own_sha256='7'*64) for n in (29,30,31)]
    # Freeze independent graphs: mutations must not accidentally change oracle.
    # JSON report rows are independent values, not aliased Python dictionaries.
    # Roundtrip each graph so intended-frontier mutations touch only one row.
    return json.loads(json.dumps(report)),json.loads(json.dumps(oracle)),dict(counts)


class AreaMetadataTests(unittest.TestCase):
    def test_terrain_oracle_request_pointer_is_not_a_dat_ordinal(self):
        report,oracle,counts=fixture()
        # Pointers0/1/2 deliberately differ from DAT ordinals10/20/30.
        area._compare_metadata(report,oracle,expected_reconciliation=counts)
        for pointer,frontier in ((-1,'terrain oracle request index'),(999,'terrain oracle request index'),
                (True,'terrain oracle request index'),(3,'terrain oracle request kind'),(1,'terrain selected request association')):
            bad=deepcopy(oracle);bad['terrain'][0][3]=pointer
            with self.subTest(pointer=pointer),self.assertRaisesRegex(ValueError,'^'+frontier+'$'):
                area._compare_metadata(report,bad,expected_reconciliation=counts)

    def setUp(self):
        self.report,self.oracle,self.counts=fixture()
        self.baseline=area._compare_metadata(self.report,self.oracle,expected_reconciliation=self.counts)

    def refuse(self, mutate, frontier):
        bad=deepcopy(self.report);mutate(bad)
        with self.assertRaisesRegex(ValueError,'^'+frontier+'$'):
            area._compare_metadata(bad,self.oracle,expected_reconciliation=self.counts)

    def test_complete_metadata_stage_reaches_order_geometry_coverage_but_is_not_pixel_pass(self):
        commands,objects,visuals,terrain,suppressed=self.baseline
        self.assertEqual((5,4,6,4,1),(len(commands),len(objects),len(visuals),len(terrain),suppressed))
        self.assertNotIn('PixelSamples',self.report)
        with tempfile.TemporaryDirectory() as owned:
            path=Path(owned)/'public-synthetic.json';path.write_text(json.dumps(self.oracle))
            with self.assertRaisesRegex(ValueError,'^independent oracle identity$'):
                area.compare_original(self.report,path)
        # Default SAME stage still fixes original census, not this test census.
        with self.assertRaisesRegex(ValueError,'^named reconciliation vector$'):
            area._compare_metadata(self.report,self.oracle)

    def test_task_chart_endpoint_and_readiness_frontiers(self):
        for field,value,frontier in (('Task','ARC-WORLD-003','coverage task/schema'),('RendererObserved',False,'coverage observation boundary'),
                ('RuntimeMap',2,'runtime/chart metadata')):
            self.refuse(lambda r:r.update({field:value}),frontier)
        self.refuse(lambda r:r['Endpoints'][0].update(ArrivalX=7),'endpoint metadata')
        self.refuse(lambda r:r['Objects'][0].update(RequiredLayersReady=False),'object scope/readiness')

    def test_sector_full_identity_and_all_entry_fields(self):
        for field,value in (('SourceSha256','b'*64),('RuntimeMap',2)):
            self.refuse(lambda r:r['Sectors'][0]['Key'].update({field:value}),'sector full source/map identity')
        for field,value in (('Ordinal',9),('Flags',0),('Offset',701),('StoredBytes',99),('DecodedBytes',201),('NameSha256','d'*64),('Source',dict(Role=2,Number=0))):
            self.refuse(lambda r:r['Sectors'][0]['Entry'].update({field:value}),'sector entry identity')

    def test_terrain_full_identity_layers_and_lookup(self):
        for field,value in (('Kind',1),('Layer',1),('SparseIndex',1),('ObjectIdSha256','f'*64),('Object',self.report['Objects'][0]['Key'])):
            self.refuse(lambda r:r['Terrain'][0]['Key'].update({field:value}),'terrain full visual identity')
        for field,value in (('SourceSha256','b'*64),('RuntimeMap',2)):
            self.refuse(lambda r:r['Terrain'][0]['Key']['Sector'].update({field:value}),'terrain full visual identity')
        self.refuse(lambda r:r['Terrain'][0].update(LookupArt=192),'terrain lookup association')

    def test_full_object_and_nested_visual_identity(self):
        for field,value in (('RuntimeMap',2),('SourceSha256','b'*64)):
            self.refuse(lambda r:r['Objects'][0]['Key'].update({field:value}),'object full source/map identity')
        self.refuse(lambda r:r['Visuals'][3]['Key']['Object'].update(Offset=101),'selected body object cross-link')
        for field,value in (('Layer',1),('SparseIndex',1),('ObjectIdSha256','f'*64)):
            self.refuse(lambda r:r['Visuals'][3]['Key'].update({field:value}),'selected body visual identity')
        for field,value in (('RuntimeMap',2),('SourceSha256','b'*64),('Packed',0)):
            self.refuse(lambda r:r['Visuals'][3]['Key']['Sector'].update({field:value}),'selected body visual identity')

    def test_equal_size_valid_selected_swap_rejects_independent_association(self):
        def swap(r):
            r['Terrain'][0]['SelectedVisual']=deepcopy(r['Visuals'][1]['Key'])
            r['Commands'][0]['Frame']=deepcopy(r['Commands'][1]['Frame'])
        self.refuse(swap,'terrain selected request association')
        self.refuse(lambda r:r['Terrain'][3].update(SelectedVisual=deepcopy(r['Visuals'][1]['Key'])),'terrain selected request association')

    def test_selected_chain_group_lookup_and_descriptor(self):
        self.refuse(lambda r:r['Visuals'][0].update(SourceGroup=1),'selected terrain chain')
        self.refuse(lambda r:r['Visuals'][3].update(SourceGroup=1),'selected object lookup/group')
        self.refuse(lambda r:r['Visuals'][4].update(LookupArt=0x60000020),'selected object lookup/group')
        self.refuse(lambda r:r['Visuals'][5].update(ObjectOffsetX=1),'selected object chain')
        self.refuse(lambda r:r['Visuals'][3]['Entry'].update(Flags=0),'selected entry')
        self.refuse(lambda r:r['Visuals'][3].update(RgbaSha256='f'*64),'selected pixels/geometry')
        self.refuse(lambda r:r['Visuals'][0].update(PhysicalGroup=1),'selected request identity')

    def test_oracle_profile_policies_are_the_general_art_rules(self):
        # ARC-DATA-036: the oracle's per-object labels name the general rules the owner applies to every plan.
        self.assertEqual(area.oracle_policy('arc-data-009-exact-art-view-v1'),'original-art-extent-v1')
        self.assertEqual(area.oracle_policy('arc-data-009-exact-single-group-alias-v1'),'original-single-group-rotation-v1')
        self.assertEqual(area.oracle_policy('strict-full-entry'),'strict-full-entry')
        key=self.report['Visuals'][3]['Key']
        name=f"static:{key['Sector']['Packed']}:{key['SourceIndex']}" if key['Kind']==1 else f"mobile:{key['SourceIndex']}"
        oracle=deepcopy(self.oracle)
        next(r for r in oracle['requests'] if r['key']==name)['policy']='arc-data-009-exact-art-view-v1'
        for label,accepted in (('original-art-extent-v1',True),('arc-data-009-exact-art-view-v1',False),('strict-full-entry',False)):
            report=deepcopy(self.report);report['Visuals'][3]['Policy']=label
            for command in report['Commands']:
                if command['Visual']==key: command['Frame']['Policy']=label
            with self.subTest(label=label):
                if accepted: area._compare_metadata(report,oracle,expected_reconciliation=self.counts)
                else:
                    with self.assertRaisesRegex(ValueError,'^selected view policy$'):
                        area._compare_metadata(report,oracle,expected_reconciliation=self.counts)

    def test_full_inputs_and_object_fields_through_same_metadata_stage(self):
        self.refuse(lambda r:r['Inputs'][0].update(Sha256='b'*64),'input identity set')
        self.refuse(lambda r:r['Objects'][0]['EffectiveFields'][2].update(Number=-2),'object field value')
        self.refuse(lambda r:r['Objects'][0]['EffectiveFields'][2].update(Origin=1),'object field origin')
        self.refuse(lambda r:r['Objects'][0]['DirectFields'][-2].update(Presence=2),'object field presence')
        self.refuse(lambda r:r['Objects'][0]['EffectiveFields'][-1]['Elements'][0].update(Key=6),'object sparse key')
        self.refuse(lambda r:r['Objects'][0]['EffectiveFields'][-1]['Elements'][0].update(Words=[-1]),'object sparse value')

    def test_missing_mandatory_draws_and_full_command_identity(self):
        self.refuse(lambda r:r['Commands'].pop(0),'mandatory terrain draw')
        self.refuse(lambda r:r['Commands'].pop(3),'mandatory body draw')
        self.refuse(lambda r:r['Commands'][3]['Identity'].update(RuntimeMap=2),'body draw representation')
        self.refuse(lambda r:r['Coverage'][0]['Visual']['Sector'].update(RuntimeMap=2),'terrain coverage set')

    def test_complete_terrain_coverage_core_context_equal_count_and_duplicate(self):
        self.refuse(lambda r:r['Coverage'].pop(0),'terrain coverage set')
        self.refuse(lambda r:r['Coverage'].append(deepcopy(r['Coverage'][0])),'duplicate occurrence')
        self.refuse(lambda r:r['Coverage'][0]['Visual'].update(SourceIndex=3),'terrain coverage set')
        for index in (0,2,3):
            self.refuse(lambda r:r['Coverage'][index].update(Disposition=6),'terrain coverage disposition')
            self.refuse(lambda r:r['Coverage'][index].update(Reason='OutsideResourceScope'),'terrain coverage disposition')

    def test_body_coverage_identity_visual_suppression_and_context(self):
        self.refuse(lambda r:r['Coverage'][4]['Identity'].update(RuntimeMap=2),'object coverage set')
        self.refuse(lambda r:r['Coverage'][4].update(Visual=deepcopy(r['Visuals'][5]['Key'])),'body coverage visual cross-link')
        self.refuse(lambda r:r['Coverage'][5].update(Reason='OutsideResourceScope'),'suppressed coverage')
        self.refuse(lambda r:r['Coverage'][7].update(Disposition=6),'context disposition')
        self.refuse(lambda r:r['Coverage'][4].update(Disposition=2),'body eligible coverage disposition')

    def test_body_signed_scale_hotspot_rect_crop_flip_rank_blend(self):
        for field,value in (('X',40),('Y',21),('Width',14),('Height',17)):
            self.refuse(lambda r:r['Commands'][3]['Destination'].update({field:value}),'body draw representation')
        self.refuse(lambda r:r['Commands'][3]['Source'].update(X=1),'body source crop')
        for field,value in (('FirstRank',24),('SecondRank',-18),('Alpha',254),('Additive',False),('FlipX',False),('FlipY',False)):
            self.refuse(lambda r:r['Commands'][3].update({field:value}),'body phase/rank/blend/flip')

    def test_full_array_submission_order_and_frame_binding(self):
        for index in range(4):
            def swap(r):r['Commands'][index],r['Commands'][index+1]=r['Commands'][index+1],r['Commands'][index]
            self.refuse(swap,'command submission sequence')
        self.refuse(lambda r:r['Commands'][3]['Frame']['Entry'].update(Offset=4001),'draw selected frame binding')

    def test_terrain_crop_blend_flip_and_context_command_refusal(self):
        self.refuse(lambda r:r['Commands'][0]['Destination'].update(X=2),'terrain draw representation')
        self.refuse(lambda r:r['Commands'][1].update(FlipX=False),'terrain draw representation')
        for field,value in (('FlipY',True),('Additive',True),('Alpha',254)):
            self.refuse(lambda r:r['Commands'][0].update({field:value}),'terrain crop/order/blend')
        def context(r):
            extra=deepcopy(r['Commands'][0]);extra.update(Visual=r['Terrain'][3]['Key'],MapX=92992)
            r['Commands'].insert(2,extra)
        self.refuse(context,'context terrain excluded')


if __name__ == '__main__':
    unittest.main()
