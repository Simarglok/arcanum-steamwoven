"""Full SAME metadata+pixel core with PUBLIC original-independent fixtures."""
import base64
from copy import deepcopy
from fractions import Fraction
import hashlib
from pathlib import Path
import struct
import tempfile
import unittest

from tools.local_dev import area_validation as area
from tools.local_dev.test_area_metadata import fixture


def full_fixture():
    report,oracle,counts=fixture()
    pixels={}; hashes={}
    for visual in report['Visuals']:
        w,h=visual['Width'],visual['Height']
        rgba=bytes(c for y in range(h) for x in range(w) for c in ((x*7+11)%251,(y*9+13)%251,(x+y+17)%251,255))
        old=visual['RgbaSha256']; sha=hashlib.sha256(rgba).hexdigest()
        visual['RgbaSha256']=sha
        ordinal=visual['Entry']['Ordinal']; pixels[ordinal]=rgba; hashes[ordinal]=sha
    for request in oracle['requests']:
        request['oracle']['rgba_sha256']=hashes[request['entry']['ordinal']]
    for command in report['Commands']:
        command['Frame']['RgbaSha256']=hashes[command['Frame']['Entry']['Ordinal']]
    report.update(PixelSamples=[],SampleFrameProofs=[],Viewports=[dict(X=x,Y=y) for x,y in ((1280,800),(1440,900),(800,600),(1920,1080))])
    # Original-independent literal selected groups: first terrain and mirrored
    # class2 NPC. The additive static is accounted for but not an opaque sample.
    draws=[report['Commands'][0],report['Commands'][-1]]
    bits=lambda value:struct.unpack('<I',struct.pack('<f',value))[0]
    for draw in draws:
        visual=next(v for v in report['Visuals'] if v['Key']==draw['Visual'])
        ordinal=visual['Entry']['Ordinal']
        report['SampleFrameProofs'].append(dict(Frame=deepcopy(draw['Frame']),Width=visual['Width'],Height=visual['Height'],
            RgbaBase64=base64.b64encode(pixels[ordinal]).decode()))
        source,dest=draw['Source'],draw['Destination']
        # Literal u/v==1 chosen before sampler; reflected NPC index25/y1,
        # ordinary terrain index1/y1. No renderer projection/constants used.
        px,py=(25,1) if draw['Identity'] else (1,1)
        expected=[(px*7+11)%251,(py*9+13)%251,(px+py+17)%251,255]
        for zoom in (.5,1,2):
            ox=100.5-zoom*(dest['X']+1); oy=100.5-zoom*(dest['Y']+1)
            transform=[bits(zoom),bits(zoom),bits(ox),bits(oy)]
            report['PixelSamples'].append(dict(Visual=deepcopy(draw['Visual']),**{k:deepcopy(draw[k]) for k in ('Identity','Frame','Destination','Source','FlipX','FlipY')},
                Zoom=zoom,SourceX=px,SourceY=py,ScreenX=100,ScreenY=100,SourceU=['1','1'],SourceV=['1','1'],Snap=False,
                SamplingDomain='exact-binary-rational-center-discrete-reflection-v1',TransformBits=transform,RawTransformBits=list(transform),Expected=expected,Actual=list(expected)))
    return report,oracle,counts


class AreaFullComparatorTests(unittest.TestCase):
    def setUp(self):
        self.report,self.oracle,self.counts=full_fixture()

    def compare(self,report):
        return area._compare_render_report(report,self.oracle,expected_reconciliation=self.counts,expected_suppressed=1)

    def test_positive_traverses_same_full_metadata_and_pixel_core_not_pin_wrapper(self):
        result=self.compare(self.report)
        self.assertEqual((5,4,6,4,1),tuple(len(v) if isinstance(v,dict) else v for v in result))
        with tempfile.TemporaryDirectory() as owned:
            import json
            path=Path(owned)/'public-oracle.json';path.write_text(json.dumps(self.oracle))
            with self.assertRaisesRegex(ValueError,'^independent oracle identity$'):
                area.compare_original(self.report,path)

    def test_late_pixel_frontiers_are_reached_after_positive_full_metadata(self):
        for frontier,mutate in (
            ('required sample group/zoom equality',lambda r:r['PixelSamples'].pop()),
            ('sample exact coordinate ratio',lambda r:r['PixelSamples'][0].update(SourceV=['2','1'])),
            ('sample independent nearest source index',lambda r:r['PixelSamples'][3].update(SourceX=26)),
            ('sample independent decoded RGBA',lambda r:r['PixelSamples'][0].update(Expected=[1,2,3,255],Actual=[1,2,3,255])),
            ('sample proof independent RGBA digest',lambda r:r['SampleFrameProofs'][0].update(RgbaBase64=base64.b64encode(bytes(78*40*4)).decode()))):
            bad=deepcopy(self.report);mutate(bad)
            with self.subTest(frontier=frontier),self.assertRaisesRegex(ValueError,'^'+frontier+'$'):
                self.compare(bad)

    def test_metadata_still_cannot_be_bypassed_by_correct_pixel_stage(self):
        bad=deepcopy(self.report);bad['Commands'][0]['Destination']['X']+=1
        with self.assertRaisesRegex(ValueError,'^terrain draw representation$'):self.compare(bad)
