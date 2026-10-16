"""Closed RENDER007 original occurrence/draw validation, not a data parser.

Detailed numeric oracle and renderer rows stay private. Only safe reconciled
counts/fingerprints go into export evidence. Original runtime parity is separate.
"""
from __future__ import annotations

import base64
import binascii
from fractions import Fraction
import hashlib
import json
import math
from pathlib import Path
import re
import struct

from . import config

ORACLE_SHA256 = 'bfe9770d75a1c1922ca8817fd0c239e589c3de48020022f7de6bd450ef2d2b60'

# Independently accepted DATA009 corpus vector. Outside/unknown are pinned
# aggregate-partition invariants, NOT inferred from the 98 retained mobiles.
PINNED_RECONCILIATION = dict(Survey=621, CoreSectors=9, ContextPresent=11, ContextMissing=5,
    CoreTerrain=36864, ContextTerrain=45056, Statics=426, Aggregate=7399, CoreMobiles=88,
    ContextMobiles=10, OutsideMobiles=7301, UnknownMobiles=0, EditorCorrespondence=7399,
    Prototypes=56, Visuals=2444)


def _require(condition: bool, frontier: str) -> None:
    if not condition:
        raise ValueError(frontier)


def _unique(rows: list, key):
    result = {key(row): row for row in rows}
    _require(len(result) == len(rows), 'duplicate occurrence')
    return result


def sector_key(key: dict) -> tuple:
    return key['SourceSha256'], key['RuntimeMap'], key['Packed']


def object_key(key: dict) -> tuple:
    return (key['Kind'], key['Sector'], key['SourceIndex'], key['Offset'], key['Length'],
            key['ObjectIdSha256'], key['SourceSha256'], key['RuntimeMap'])


def visual_key(key: dict) -> tuple:
    return (key['Kind'], key['Sector']['Packed'], key['SourceIndex'], key['Layer'], key['SparseIndex'],
            key['ObjectIdSha256'], sector_key(key['Sector']), object_key(key['Object']) if key['Object'] is not None else None)


def _sector_identity(source_sha: str, packed: int) -> dict:
    return dict(SourceSha256=source_sha, RuntimeMap=1, Packed=packed)


def _visual_identity(kind: int, sector: dict, index: int, identity: dict | None = None) -> dict:
    return dict(Kind=kind, Sector=sector, SourceIndex=index, Layer=0, SparseIndex=0,
                ObjectIdSha256=identity['ObjectIdSha256'] if identity else '', Object=identity)


def _entry_identity(source: dict, role: int) -> dict:
    return dict(Source=dict(Role=role,Number=0), Ordinal=source['ordinal'], Flags=source['flags'],
                Offset=source['offset'], StoredBytes=source['stored'], DecodedBytes=source['decoded'],
                NameSha256=source['name_sha256'])


def _field(fields: dict, slot: int) -> int:
    return fields[str(slot)]['value']


def _trunc(value: int, denominator: int) -> int:
    return (1 if value >= 0 else -1) * (abs(value) // denominator)


def _frame_binding(draw: dict, visual: dict) -> None:
    expected = dict(Entry=visual['Entry'], Palette=visual['EffectivePalette'], Group=visual['PhysicalGroup'],
                    Frame=visual['Frame'], Policy=visual['Policy'], PayloadSha256=visual['PayloadSha256'],
                    ViewLength=visual['ViewLength'], RgbaSha256=visual['RgbaSha256'])
    _require(draw['Frame'] == expected, 'draw selected frame binding')


def _input_identities(actual: list, identities: list) -> dict:
    roles = {'module':0, 'resource1':1, 'resource2':2, 'normal-game-mobile':3}
    expected = {}
    for identity in identities:
        role = identity['role']
        if role in roles:
            key = (roles[role], 0)
        else:
            _require(re.fullmatch(r'prototype:[1-9][0-9]*', role) is not None, 'input role literal')
            key = (4, int(role.split(':')[1]))
        _require(key not in expected, 'duplicate oracle input identity')
        _require(type(identity['length']) is int and identity['length'] >= 0
                 and re.fullmatch(r'[0-9a-f]{64}',identity['sha256']) is not None, 'oracle input descriptor')
        expected[key] = (identity['length'], identity['sha256'])
    rows = _unique(actual, lambda row: (row['Key']['Role'],row['Key']['Number']))
    for row in rows.values():
        _require(type(row['Key']['Role']) is int and type(row['Key']['Number']) is int, 'input key integers')
    _require({key:(row['Length'],row['Sha256']) for key,row in rows.items()} == expected, 'input identity set')
    return expected


def _reconciliation(actual: dict, oracle: dict, expected: dict = PINNED_RECONCILIATION) -> None:
    _require(actual == expected and all(type(v) is int for v in actual.values()), 'named reconciliation vector')
    counts = oracle['counts']
    mapping = dict(Survey='survey',CoreSectors='core_sectors',ContextPresent='context_present',ContextMissing='context_missing',
        CoreTerrain='core_terrain',ContextTerrain='context_terrain',Statics='statics',Aggregate='aggregate',
        CoreMobiles='core_mobiles',ContextMobiles='context_mobiles',EditorCorrespondence='editor',Prototypes='prototype_count',Visuals='selected_requests')
    _require(all(actual[name] == counts[field] for name,field in mapping.items()), 'independent reconciliation counts')
    census = dict(Survey=len(_unique(oracle['survey'],lambda row:row['id'])),
        CoreSectors=sum(row['core'] is True for row in oracle['sectors']),
        ContextPresent=sum(row['core'] is False for row in oracle['sectors']),ContextMissing=len(oracle['missing_context']),
        CoreTerrain=sum(row[6] is True for row in oracle['terrain']),ContextTerrain=sum(row[6] is False for row in oracle['terrain']),
        Statics=len(oracle['statics']),CoreMobiles=sum(row['scope']=='core' for row in oracle['mobiles']),
        ContextMobiles=sum(row['scope']=='context' for row in oracle['mobiles']),Prototypes=len(oracle['prototypes']),Visuals=len(oracle['requests']))
    _require(all(actual[name] == value for name,value in census.items()), 'independent reconciliation census')


def _command_sequence(commands: list) -> None:
    def order(row):
        identity = row['Identity']
        return (row['Phase'],row['MapY'],row['MapX'],row['SecondRank'],row['FirstRank'],
                identity['Kind'] if identity else -1, identity['SourceIndex'] if identity else -1,
                identity['Offset'] if identity else -1, row['Visual']['SourceIndex'])
    _require(commands == sorted(commands,key=order), 'command submission sequence')


def _sector_partition(actual: list, oracle: dict) -> None:
    present = _unique(oracle['sectors'], lambda row: row['id'])
    missing = {x + (y << 26): (x, y) for x,y in oracle['missing_context']}
    rows = _unique(actual, lambda row: row['Key']['Packed'])
    _require(set(rows) == set(present) | set(missing), 'sector partition equality')
    for key,row in rows.items():
        source = present.get(key)
        x,y = (source['x'],source['y']) if source else missing[key]
        _require(row['Coordinate'] == dict(X=x,Y=y,Packed=key,OriginX=x*64,OriginY=y*64), 'sector coordinates')
        _require(row['Present'] is (source is not None) and row['Scope'] == (0 if source and source['core'] else 1), 'sector presence/scope')
        if source:
            f = source['framing']
            # Terrain wire discriminator1 means no roof words; they are not
            # interchangeable units. Active roofs are outside this strict corpus.
            _require(f['roof_words'] == 0, 'unsupported roof payload')
            _require((row['DatOrdinal'],row['PayloadSha256'],row['TerminalCount'],row['Townmap'],row['LightCount'],row['RoofDiscriminator'],row['Placeholder']) ==
                     (source['entry']['ordinal'],source['payload_sha256'],f['terminal'],f['townmap'],f['lights'],1,f['placeholder']), 'sector occurrence/sections')
        else:
            _require(row['Entry'] is None and row['PayloadSha256'] is None and row['DatOrdinal'] is None, 'absent sector source')


def _sample_coverage(report: dict, commands: dict, objects: dict, visuals: dict, required_groups: set) -> None:
    def frame_key(frame):
        return json.dumps(frame,sort_keys=True,separators=(',',':'))
    _require(isinstance(report.get('SampleFrameProofs'),list), 'sample frame proofs required')
    descriptors = {frame_key(dict(Entry=v['Entry'],Palette=v['EffectivePalette'],Group=v['PhysicalGroup'],
        Frame=v['Frame'],Policy=v['Policy'],PayloadSha256=v['PayloadSha256'],ViewLength=v['ViewLength'],RgbaSha256=v['RgbaSha256'])):v
        for v in visuals.values()}
    proofs = _unique(report['SampleFrameProofs'],lambda row:frame_key(row['Frame']))
    decoded = {}; total = 0
    for key,proof in proofs.items():
        _require(key in descriptors, 'sample proof selected descriptor')
        visual = descriptors[key]
        _require((proof['Width'],proof['Height']) == (visual['Width'],visual['Height']), 'sample proof dimensions')
        size = visual['Width']*visual['Height']*4; total += size
        _require(total <= 81853096 and isinstance(proof['RgbaBase64'],str)
                 and len(proof['RgbaBase64']) == ((size+2)//3)*4, 'sample proof byte budget')
        try:
            rgba = base64.b64decode(proof['RgbaBase64'],validate=True)
        except (ValueError,binascii.Error) as error:
            raise ValueError('sample proof encoding') from error
        _require(len(rgba)==size and hashlib.sha256(rgba).hexdigest()==visual['RgbaSha256'], 'sample proof independent RGBA digest')
        decoded[key] = rgba
    used_proofs = set()
    observed = set()
    for sample in report['PixelSamples']:
        key = visual_key(sample['Visual']); draw = commands[key]
        for field in ('Identity','Frame','Destination','Source','FlipX','FlipY'):
            _require(sample[field] == draw[field], 'sample command binding')
        group = ('terrain',key[1]) if draw['Identity'] is None else ('body',objects[object_key(draw['Identity'])]['Type'],visuals[key]['RawArt'] >> 28)
        zoom = sample['Zoom']; _require(zoom in (.5,1,2), 'sample zoom domain')
        observed.add((group,zoom))
        _require(sample.get('SamplingDomain') == 'exact-binary-rational-center-discrete-reflection-v1', 'sample nearest rule')
        def transform(field):
            bits = sample.get(field)
            _require(isinstance(bits,list) and len(bits)==4 and all(type(v) is int and 0<=v<=0xffffffff for v in bits), 'sample transform bits')
            values = [struct.unpack('<f',struct.pack('<I',v))[0] for v in bits]
            _require(all(math.isfinite(v) for v in values), 'sample transform finite')
            return [Fraction(v) for v in values]
        zx,zy,ox,oy = transform('TransformBits'); raw = transform('RawTransformBits')
        _require(type(sample.get('Snap')) is bool and zx==zy==raw[0]==raw[1]==zoom, 'sample transform zoom')
        expected_origin = [Fraction(math.ceil(v-Fraction(1,2))) for v in raw[2:]] if sample['Snap'] else raw[2:]
        _require([ox,oy]==expected_origin, 'sample effective transform')
        s = draw['Source']
        d = draw['Destination']
        for field in ('SourceX','SourceY','ScreenX','ScreenY'):
            _require(type(sample[field]) is int, 'sample coordinate integer')
        u = ((Fraction(2*sample['ScreenX']+1,2)-ox)/zx-d['X'])*s['Width']/d['Width']
        v = ((Fraction(2*sample['ScreenY']+1,2)-oy)/zy-d['Y'])*s['Height']/d['Height']
        _require(0<=u<s['Width'] and 0<=v<s['Height'], 'sample destination ownership')
        for field,value in (('SourceU',u),('SourceV',v)):
            ratio = sample.get(field)
            _require(isinstance(ratio,list) and len(ratio)==2 and all(isinstance(p,str) and re.fullmatch(r'-?(0|[1-9][0-9]*)',p) for p in ratio)
                     and int(ratio[1])>0 and Fraction(int(ratio[0]),int(ratio[1]))==value, 'sample exact coordinate ratio')
        px = s['X']+(s['Width']-1-int(u) if draw['FlipX'] else int(u))
        py = s['Y']+(s['Height']-1-int(v) if draw['FlipY'] else int(v))
        _require((sample['SourceX'],sample['SourceY'])==(px,py), 'sample independent nearest source index')
        _require(s['X'] <= sample['SourceX'] < s['X']+s['Width'] and s['Y'] <= sample['SourceY'] < s['Y']+s['Height'], 'sample source crop')
        _require(0 <= sample['ScreenX'] < 1280 and 0 <= sample['ScreenY'] < 800, 'sample actual viewport')
        expected,actual = sample['Expected'],sample['Actual']
        fk = frame_key(draw['Frame']); used_proofs.add(fk)
        _require(fk in decoded, 'sample frame proof missing')
        at = (py*descriptors[fk]['Width']+px)*4
        _require(expected == list(decoded[fk][at:at+4]), 'sample independent decoded RGBA')
        _require(len(expected)==len(actual)==4 and expected[3]==255 and all(0<=c<=255 for c in expected+actual)
                 and all(abs(a-b)<=2 for a,b in zip(expected,actual)), 'sample exact RGBA')
    _require(observed == {(group,zoom) for group in required_groups for zoom in (.5,1,2)}, 'required sample group/zoom equality')
    _require(used_proofs==set(proofs), 'sample proof set equality')
    _require({(v['X'],v['Y']) for v in report['Viewports']} == {(1280,800),(1440,900),(800,600),(1920,1080)}, 'required actual viewports')


def compare_original(report: dict, oracle_path: Path) -> dict:
    encoded = config.optional_authority_bytes(oracle_path)
    _require(encoded is not None and hashlib.sha256(encoded).hexdigest() == ORACLE_SHA256, 'independent oracle identity')
    assert encoded is not None
    oracle = json.loads(encoded)
    commands, actual_objects, visuals, terrain, suppressed = _compare_render_report(report, oracle)
    return dict(oracleSha256=ORACLE_SHA256, terrainOccurrences=len(terrain), objectOccurrences=len(actual_objects),
                selectedAppearances=len(visuals), coreTerrain=36864, coreBodies=len(commands)-36864, notApplicable=suppressed,
                submitted=len(commands), originalOccurrenceCoverage='PASS', originalAppearanceCoverage='PASS', drawCoverage='PASS')


def _compare_render_report(report: dict, oracle: dict, *, expected_reconciliation: dict = PINNED_RECONCILIATION,
                           expected_suppressed: int = 15) -> tuple:
    """Full metadata AND pixel core. Public fixtures do not bypass wrapper pinning."""
    commands, actual_objects, visuals, terrain, suppressed = _compare_metadata(report, oracle, expected_reconciliation=expected_reconciliation)
    _require(suppressed == expected_suppressed, 'original not-applicable census')
    groups = {('terrain',key[1]) if draw['Identity'] is None else ('body',actual_objects[object_key(draw['Identity'])]['Type'],visuals[key]['RawArt'] >> 28)
              for key,draw in commands.items() if not draw['Additive']}
    _sample_coverage(report,commands,actual_objects,visuals,groups)
    return commands, actual_objects, visuals, terrain, suppressed


def _compare_metadata(report: dict, oracle: dict, *, expected_reconciliation: dict = PINNED_RECONCILIATION) -> tuple:
    """Same production metadata/order stage, with a bounded public test census.

    This is NOT a coverage PASS and never emits a safe acceptance receipt. Only
    compare_original authenticates the fixed corpus, then requires pixel checks.
    Tests exercise this stage without replacing the production oracle digest or
    pretending the unresolved native half-zoom sample stage has passed.
    """
    _require(report.get('Schema') == 'arcanum-area-render-coverage-v1' and report.get('Task') == 'ARC-RENDER-007', 'coverage task/schema')
    _require(report.get('RendererObserved') is True and report.get('OriginalRuntimeParity') == 'NOT_VERIFIED', 'coverage observation boundary')
    _reconciliation(report['Reconciliation'],oracle,expected_reconciliation)
    sectors = _unique(oracle['sectors'], lambda row: row['id'])
    _sector_partition(report['Sectors'],oracle)
    _require((report['CatalogueMap'],report['RuntimeMap'],report['Profile'],report['Chart']) ==
             (5000,1,'arc-data-009-complete-file-effective-source-v1',dict(OriginX=92928,OriginY=82560)), 'runtime/chart metadata')
    expected_inputs = _input_identities(report['Inputs'],oracle['identities'])
    module_sha = expected_inputs[(0,0)][1]
    ep = oracle['endpoint']
    _require(report['Endpoints'] == [dict(Sector=ep['sector'],Ordinal=ep['ordinal'],Prototype=ep['prototype'],Sap=ep['sap'],Script=ep['script'],
             CatalogueDestination=ep['catalogue_destination'],RuntimeDestination=ep['runtime_destination'],ArrivalX=ep['destination'][0],ArrivalY=ep['destination'][1],SourceObjectIdSha256=ep['object_sha256'])], 'endpoint metadata')
    actual_sectors = _unique(report['Sectors'], lambda row: row['Key']['Packed'])
    for row in actual_sectors.values():
        _require(row['Key'] == _sector_identity(module_sha,row['Coordinate']['Packed']), 'sector full source/map identity')
        if not row['Present']:
            continue
        source = sectors[row['Key']['Packed']]
        _require(row['Entry'] == _entry_identity(source['entry'],0), 'sector entry identity')
        _require((row['DatOrdinal'], row['PayloadSha256'], row['TerminalCount']) ==
                 (source['entry']['ordinal'], source['payload_sha256'], source['framing']['terminal']), 'sector occurrence')
    terrain = _unique(oracle['terrain'], lambda row: (row[0], row[1]))
    actual_terrain = _unique(report['Terrain'], lambda row: (row['Key']['Sector']['Packed'], row['Key']['SourceIndex']))
    _require(set(actual_terrain) == set(terrain), 'terrain occurrence set')
    for key, row in actual_terrain.items():
        source = terrain[key]
        _require(row['Key'] == _visual_identity(0,_sector_identity(module_sha,source[0]),source[1]), 'terrain full visual identity')
        _require((row['RawArt'], row['WorldX'], row['WorldY']) == (source[2], source[4], source[5]), 'terrain placement input')
        _require(row['Scope'] == (0 if source[6] else 1) and (row['LocalX'],row['LocalY']) == (source[1]%64,source[1]//64), 'terrain scope/local identity')
        _require(row['LookupArt'] == (source[2] & ~63 if source[2] >> 28 == 0 else source[2]), 'terrain lookup association')
    statics = _unique(oracle['statics'], lambda row: (row['sector_id'], row['ordinal']))
    mobiles = _unique(oracle['mobiles'], lambda row: row['index'])
    actual_objects = _unique(report['Objects'], lambda row: object_key(row['Key']))
    prototypes = _unique(oracle['prototypes'], lambda row:row['own_number'])
    _require(len(actual_objects) == len(statics)+len(mobiles)
             and sum(row['Key']['Kind'] == 1 for row in actual_objects.values()) == len(statics), 'object occurrence census')
    _require({(r['Key']['Sector'],r['Key']['SourceIndex']) for r in actual_objects.values() if r['Key']['Kind']==1} == set(statics)
             and {r['Key']['SourceIndex'] for r in actual_objects.values() if r['Key']['Kind']==2} == set(mobiles), 'object source set equality')
    expected_sources = {}
    for key, row in actual_objects.items():
        source = statics[(row['Key']['Sector'], row['Key']['SourceIndex'])] if row['Key']['Kind'] == 1 else mobiles[row['Key']['SourceIndex']]
        expected_sources[key] = source
        scope = 0 if row['Key']['Kind']==1 or source['scope']=='core' else 1
        _require(row['Scope']==scope and row['SchemaReady'] and row['PlacementReady'] and row['RequiredLayersReady'], 'object scope/readiness')
        for actual, expected in [('RawSha256','sha256'), ('SemanticSha256','semantic_sha256'), ('PrototypeReferenceSha256','parent_sha256'),
                                 ('PrototypeNumber','parent_number'), ('Type','type'), ('WorldX','world_x'), ('WorldY','world_y')]:
            _require(row[actual] == source[expected], 'object occurrence/placement')
        _require((row['Key']['Offset'], row['Key']['Length'], row['Key']['ObjectIdSha256']) ==
                 (source['offset'], source['length'], source['own_sha256']), 'object source key')
        source_sha = expected_inputs[(0 if row['Key']['Kind']==1 else 3,0)][1]
        _require(row['Key'] == dict(SourceSha256=source_sha,RuntimeMap=1,Kind=row['Key']['Kind'],
            Sector=source['sector_id'] if row['Key']['Kind']==1 else None,SourceIndex=source['ordinal'] if row['Key']['Kind']==1 else source['index'],
            Offset=source['offset'],Length=source['length'],ObjectIdSha256=source['own_sha256']), 'object full source/map identity')
        _require(row['Version']==119 and row['PrototypeOwnSha256']==prototypes[row['PrototypeNumber']]['own_sha256']
                 and row['SerializedLocation']==source['fields']['1'].get('value'), 'object schema/prototype/direct location')
        flags = _field(source['effective'],18)
        _require(row['Disposition']==(3 if scope==1 else 1 if flags&0x1000 else 2 if flags&0x103 else 0), 'data object disposition')
        for field_group, source_group in [('DirectFields','fields'), ('EffectiveFields','effective')]:
            fields = _unique(row[field_group], lambda field: str(field['Slot']))
            _require(set(fields) == set(source[source_group]), 'object field set')
            for slot, field in fields.items():
                expected = source[source_group][slot]
                presence = expected['presence']
                required_presence = 1 if presence == 'null' else 3 if presence == 'empty' or (presence == 'array' and expected['count'] == 0) else 4 if presence == 'array' else 2
                _require(field['Presence'] == required_presence, 'object field presence')
                if source_group == 'effective':
                    _require(field['Origin'] == (0 if expected['origin'] == 'instance' else 1), 'object field origin')
                if isinstance(expected.get('value'), int):
                    _require(field['Number'] == expected['value'], 'object field value')
                if 'id_sha256' in expected:
                    _require(field['Fingerprint'] == expected['id_sha256'], 'object field identifier')
                if 'entries' in expected:
                    _require(len(field['Elements']) == len(expected['entries']), 'object sparse entries')
                    for actual_element, expected_element in zip(field['Elements'], expected['entries']):
                        _require(actual_element['Key'] == expected_element['key'], 'object sparse key')
                        if isinstance(expected_element.get('value'), int) and expected.get('width') == 4:
                            _require(actual_element['Words'][0] == (expected_element['value'] & 0xffffffff), 'object sparse value')
    requests = _unique(oracle['requests'], lambda row: row['key'])
    visuals = _unique(report['Visuals'], lambda row: visual_key(row['Key']))
    _require(len(visuals) == len(requests) == expected_reconciliation['Visuals'], 'selected appearance census')
    selected_requests = set()
    for row in visuals.values():
        key = row['Key']
        if key['Kind'] == 0:
            representative = actual_terrain.get((key['Sector']['Packed'],key['SourceIndex']))
            _require(representative is not None and key == representative['Key'], 'selected terrain visual identity')
            assert representative is not None
            _require((row['RawArt'],row['LookupArt'],row['NominalFacing'],row['SourceGroup'],row['Mirrored'],
                row['ObjectOffsetX'],row['ObjectOffsetY'],row['Scale']) ==
                (representative['RawArt'],representative['LookupArt'],0,0,False,0,0,100), 'selected terrain chain')
        else:
            body = actual_objects.get(object_key(key['Object'])) if key['Object'] is not None else None
            _require(body is not None, 'selected body object cross-link')
            assert body is not None
            packed = (body['WorldX']//64) + ((body['WorldY']//64) << 26)
            _require(key == _visual_identity(body['Key']['Kind'],_sector_identity(body['Key']['SourceSha256'],packed),
                body['Key']['SourceIndex'],body['Key']), 'selected body visual identity')
        name = f"mobile:{key['SourceIndex']}" if key['Kind'] == 2 else f"static:{key['Sector']['Packed']}:{key['SourceIndex']}" if key['Kind'] == 1 else f"terrain:({row['Entry']['Ordinal']}, {row['PhysicalGroup']}, {row['Frame']}, {row['RequestedPalette']})"
        _require(name in requests, 'selected request identity')
        source = requests[name]
        _require(name not in selected_requests, 'duplicate selected request'); selected_requests.add(name)
        _require(row['ViewLength']==source['view_length'] and row['Policy']==source['policy'], 'selected view policy')
        if source.get('chain'):
            chain = source['chain']
            _require((row['RawArt'],row['ObjectOffsetX'],row['ObjectOffsetY'],row['Scale'],row['NominalFacing'],row['Mirrored']) ==
                     (chain['raw_current_aid'] & 0xffffffff,chain['object_offset_x'],chain['object_offset_y'],chain['scale'],chain['nominal_facing'],chain['source_mirrored']), 'selected object chain')
            raw = chain['raw_current_aid'] & 0xffffffff
            _require(row['LookupArt'] == (raw & ~0x30 if raw >> 28 == 6 else raw)
                     and row['SourceGroup'] == chain['requested_group'], 'selected object lookup/group')
            source_object = expected_sources[object_key(key['Object'])]
            _require(raw == (_field(source_object['effective'],0) & 0xffffffff)
                     and (row['ObjectOffsetX'],row['ObjectOffsetY'],row['Scale']) ==
                     tuple(_field(source_object['effective'],slot) for slot in (2,3,11)), 'selected object effective association')
        _require(row['Entry']['Source']==dict(Role=1 if source['source']=='resource1' else 2,Number=0)
                 and source['source'] in ('resource1','resource2'), 'selected source role')
        for actual, expected in [('Ordinal','ordinal'),('Flags','flags'),('Offset','offset'),('StoredBytes','stored'),('DecodedBytes','decoded'),('NameSha256','name_sha256')]:
            _require(row['Entry'][actual] == source['entry'][expected], 'selected entry')
        _require(row['PayloadSha256'] == source['full_sha256'], 'selected payload')
        for actual, expected in [('Width','width'),('Height','height'),('HotX','hot_x'),('HotY','hot_y'),('ArtOffsetX','offset_x'),('ArtOffsetY','offset_y'),
                                 ('PhysicalGroup','group'),('Frame','frame'),('RequestedPalette','requested_palette'),('EffectivePalette','effective_palette'),('EncodedBytes','encoded_bytes'),('RgbaBytes','rgba_bytes'),('RgbaSha256','rgba_sha256')]:
            _require(row[actual] == source['oracle'][expected], 'selected pixels/geometry')
    _require(selected_requests==set(requests), 'selected request equality')
    for key,row in actual_terrain.items():
        selected = visuals.get(visual_key(row['SelectedVisual']))
        _require(selected is not None and selected['Key']['Kind']==0, 'terrain selected visual reference')
        assert selected is not None
        raw = terrain[key][2]
        request_index = terrain[key][3]
        _require(type(request_index) is int and 0<=request_index<len(oracle['requests']), 'terrain oracle request index')
        request = oracle['requests'][request_index]
        _require(request['key'].startswith('terrain:') and request['source']=='resource2', 'terrain oracle request kind')
        frame,palette = (0,(raw>>4)&3) if raw>>28==0 else ((raw>>1)&1023,0)
        _require(raw>>28 in (0,11) and (selected['Entry']['Ordinal'],selected['SourceGroup'],selected['PhysicalGroup'],
            selected['Frame'],selected['RequestedPalette']) == (request['entry']['ordinal'],0,0,frame,palette)
            and selected['LookupArt']==row['LookupArt'], 'terrain selected request association')
    commands = _unique(report['Commands'], lambda row: visual_key(row['Visual']))
    _command_sequence(report['Commands'])
    coverage = report['Coverage']
    covered_objects = _unique([row for row in coverage if row['Identity'] is not None], lambda row: object_key(row['Identity']))
    _require(set(covered_objects) == set(actual_objects), 'object coverage set')
    covered_terrain = _unique([row for row in coverage if row['Identity'] is None], lambda row: visual_key(row['Visual']))
    _require(set(covered_terrain) == {visual_key(row['Key']) for row in actual_terrain.values()}, 'terrain coverage set')
    expected_draws = set()
    suppressed = 0
    for key, row in actual_objects.items():
        coverage_row = covered_objects[key]
        _require(coverage_row['Identity'] == row['Key'], 'body coverage object identity')
        linked = [visual for visual in visuals.values() if visual['Key']['Object'] == row['Key']]
        _require(len(linked)==(0 if row['Scope']!=0 else 1)
                 and coverage_row['Visual']==(linked[0]['Key'] if linked else None), 'body coverage visual cross-link')
        if row['Scope'] != 0:
            _require(coverage_row['Disposition'] == 5 and coverage_row['Reason'] == 'DATA009Scope', 'context disposition')
            continue
        source = expected_sources[key]
        flags = _field(source['effective'], 18)
        reason = 'ExteriorSectorInsertionRefusedByInventory' if flags & 0x1000 else 'BodySuppressedBySerializedState' if flags & 0x103 else 'InvisibleNoDetectionObserver' if flags & 0x200 else None
        vk = visual_key(coverage_row['Visual'])
        if reason:
            suppressed += 1
            _require(coverage_row['Disposition'] == 6 and coverage_row['Reason'] == reason and vk not in commands, 'suppressed coverage')
            continue
        expected_draws.add(vk)
        _require(vk in commands, 'mandatory body draw')
        draw = commands[vk]; visual = visuals[vk]
        _frame_binding(draw,visual)
        ox, oy, scale = (_field(source['effective'], slot) for slot in (2,3,11))
        shrink = 2 if flags & 0x80 else 1
        dx, dy = source['world_x'] - 92928, source['world_y'] - 82560
        expected = dict(X=40*(dy-dx)+ox+40-_trunc(_trunc(visual['HotX']*scale,100),shrink),
                        Y=20*(dx+dy)+oy+20-_trunc(_trunc(visual['HotY']*scale,100),shrink),
                        Width=_trunc(_trunc(visual['Width']*scale,100),shrink), Height=_trunc(_trunc(visual['Height']*scale,100),shrink))
        _require(draw['Destination'] == expected and draw['Identity'] == row['Key'] and draw['Visual'] == visual['Key'], 'body draw representation')
        _require((draw['MapX'],draw['MapY']) == (source['world_x'],source['world_y']), 'body logical anchor')
        blit = _field(source['effective'],8)
        flat = bool(flags & 4); under = flat and source['type']==3 and bool(_field(source['effective'],36)&0x200)
        phase = 2 if under else 3 if flat else 4
        _require(coverage_row['Disposition']==(4 if under else 3 if flat else 2)
                 and coverage_row['Reason']=='DATA009ResolvedBody', 'body eligible coverage disposition')
        horizontal = _trunc(ox-40,2); vertical = 2*_trunc(oy,2)
        _require((draw['Phase'],draw['FirstRank'],draw['SecondRank'],draw['Alpha'],draw['Additive'],draw['FlipX'],draw['FlipY']) ==
                 (phase,vertical-horizontal,horizontal+vertical,255,bool(blit&0x10),visual['Mirrored'] != bool(blit&1),bool(blit&2)), 'body phase/rank/blend/flip')
        _require(draw['Source']==dict(X=0,Y=0,Width=visual['Width'],Height=visual['Height']), 'body source crop')
    for key, row in actual_terrain.items():
        source = terrain[key]
        vk = visual_key(row['Key'])
        coverage_row = covered_terrain[vk]
        _require(coverage_row['Visual'] == row['Key'], 'terrain coverage visual identity')
        facade = (row['RawArt'] >> 28) != 0
        _require((coverage_row['Disposition'],coverage_row['Reason']) ==
                 ((5,'DATA009TerrainScope') if row['Scope']!=0 else (int(facade),'CoreTerrain')), 'terrain coverage disposition')
        if row['Scope'] != 0:
            _require(vk not in commands, 'context terrain excluded')
            continue
        expected_draws.add(vk)
        _require(vk in commands, 'mandatory terrain draw')
        draw = commands[vk]; visual = visuals[visual_key(row['SelectedVisual'])]
        _frame_binding(draw,visual)
        dx,dy = row['WorldX']-92928,row['WorldY']-82560
        facade = (row['RawArt'] >> 28) != 0
        expected = dict(X=40*(dy-dx)+1,Y=20*(dx+dy),Width=min(80 if facade else 78,visual['Width']),Height=min(40,visual['Height']))
        _require(draw['Destination'] == expected and draw['Visual'] == row['Key'] and draw['Phase'] == int(facade) and draw['FlipX'] == (not facade and row['RawArt'] & 0xc1 == 0xc1), 'terrain draw representation')
        _require(draw['Source']==dict(X=0,Y=0,Width=expected['Width'],Height=expected['Height']) and draw['FlipY'] is False
                 and draw['Identity'] is None and draw['Alpha']==255 and draw['Additive'] is False
                 and (draw['MapX'],draw['MapY'],draw['FirstRank'],draw['SecondRank'])==(source[4],source[5],0,0), 'terrain crop/order/blend')
    _require(set(commands) == expected_draws, 'mandatory draw set equality')
    return commands, actual_objects, visuals, terrain, suppressed


REPOSITORY_FRAMES = 2444
REPOSITORY_BYTES = 81853096


def resource_summary(raw: object) -> dict:
    """Validate the native S5 observation and keep only safe scalar values."""
    _require(isinstance(raw, dict) and raw.get('Outcome') == 'PASS', 'area resources outcome')
    assert isinstance(raw, dict)
    def integers(name: str, length: int | None = None) -> list[int]:
        value = raw.get(name)
        _require(isinstance(value, list) and all(type(item) is int and item >= 0 for item in value)
                 and (length is None or len(value) == length), 'area resources ' + name)
        return value
    scalars = {name: raw.get(name) for name in ('SweepSteps', 'SweepCommands', 'MaxVisible', 'MaxGpuFrames',
                                                  'MaxGpuBytes', 'Reuploads', 'Evictions', 'CpuFrames', 'CpuBytes')}
    _require(all(type(value) is int and value > 0 for value in scalars.values()), 'area resources scalars')
    _require(scalars['SweepCommands'] == 37363 and scalars['CpuFrames'] <= REPOSITORY_FRAMES
             and scalars['CpuBytes'] <= REPOSITORY_BYTES and scalars['MaxGpuFrames'] <= REPOSITORY_FRAMES
             and scalars['MaxGpuBytes'] <= REPOSITORY_BYTES, 'area resources bounds')
    _require(raw.get('CancelledGenerationPublished') is False, 'area resources stale publication')
    _require(raw.get('RetiredGenerationCollected') is True, 'area resources retired generation')
    managed, resident, handles = integers('ManagedBytes', 2), integers('ResidentBytes', 2), integers('Handles', 2)
    # Resident size is an observation only; freed pages need not return to the OS.
    _require(handles[1] <= handles[0] and managed[1] - managed[0] < scalars['CpuBytes'] // 2, 'area resources retention')
    return {**{name[0].lower() + name[1:]: value for name, value in scalars.items()},
            'loadMilliseconds': integers('LoadMilliseconds', 2), 'managedBytes': managed,
            'residentBytes': resident, 'handles': handles, 'retiredGenerationCollected': True, 'outcome': 'PASS'}


# Export tasks whose area-original native smoke also loads the cave and must bind its transition smoke.
CAVE_TASKS = ('ARC-WORLD-009',)


def validate_report(report: object, head: str, host: str, task: str = 'ARC-RENDER-007') -> None:
    if not isinstance(report, dict):
        raise ValueError('area report object')
    _require(re.fullmatch(r'[0-9a-f]{40}', head) is not None, 'area exact head')
    _require((report.get('schema'),report.get('task'),report.get('candidateCommit'),report.get('host'),report.get('outcome'),report.get('exitCode')) ==
             ('arcanum-area-validation-v1','ARC-RENDER-007',head,host,'PASS',0), 'area gate binding')
    _require(report.get('graphical') is True and report.get('oracleSha256') == ORACLE_SHA256, 'area original/graphical identity')
    for field in ('originalOccurrenceCoverage','originalAppearanceCoverage','drawCoverage'):
        _require(report.get(field) == 'PASS', 'area complete coverage')
    _require((report.get('coreTerrain'),report.get('coreBodies'),report.get('notApplicable'),report.get('submitted')) == (36864,499,15,37363), 'area scene counts')
    resources = report.get('resources')
    _require(isinstance(resources, dict) and resources.get('outcome') == 'PASS'
             and resources.get('sweepCommands') == 37363, 'area resource observation')
    # The summary task names the gate schema; the cave receipt binds the export task's own smoke.
    _require(report.get('caveTransition') == ('PASS' if task in CAVE_TASKS else None), 'area cave transition binding')


def validate_reports(evidence: Path, head: str, task: str = 'ARC-RENDER-007') -> None:
    for host in ('source','exported'):
        data = config.optional_authority_bytes(evidence / f'{host}-area-original.log')
        _require(data is not None, 'missing area report')
        assert data is not None
        validate_report(json.loads(data),head,host,task)
