#!/usr/bin/env python3
"""Independent public Fraction oracle for the deterministic AREA renderer.

Generate BEFORE native readback. The native conformance gate pins the complete
output digest, never calibrates expected indices from GPU output.
"""
import argparse
from fractions import Fraction as F
import hashlib
import json
from pathlib import Path


def generate():
    rows=[]
    for source_width,source_height in [(31,44),(27,35),(10,23)]:
        for crop in [(0,0,source_width,source_height),(2,3,source_width-4,source_height-6)]:
            for zoom in [F(1,2),F(1),F(2)]:
                for ox,oy in [(F(100),F(100)),(F(399,4),F(401,4)),(F(201,2),F(199,2))]:
                    for dx,dy in [(40,20),(-1,-3),(41,23)]:
                        for fx,fy in [(False,False),(True,False),(False,True),(True,True)]:
                            cx,cy,w,h=crop
                            points=[]
                            for sy in range(int(oy+dy*zoom)-1,int(oy+(dy+h)*zoom)+2):
                                for sx in range(int(ox+dx*zoom)-1,int(ox+(dx+w)*zoom)+2):
                                    u=(F(2*sx+1,2)-ox)/zoom-dx
                                    v=(F(2*sy+1,2)-oy)/zoom-dy
                                    if not (0<=u<w and 0<=v<h): continue
                                    tx=cx+(w-1-int(u) if fx else int(u))
                                    ty=cy+(h-1-int(v) if fy else int(v))
                                    points.append([sx,sy,tx,ty,0 if (tx+3*ty)%7==0 else 255])
                            rows.append(dict(width=source_width,height=source_height,crop=crop,zoom=float(zoom),
                                origin=[float(ox),float(oy)],destination=[dx,dy,w,h],flip=[fx,fy],points=points))
    return rows


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    rows=generate()
    encoded=(json.dumps(rows,separators=(',',':'))+'\n').encode()
    digest=hashlib.sha256(encoded).hexdigest()
    if digest!='2fcc52540cbcc85cdbf94a3033d16dadd91ad82a8b298d418f1fc7bedc915fcf':
        raise ValueError('frozen independent fixture changed')
    with args.output.open('xb') as stream:stream.write(encoded)
    print('cases',len(rows),'pixels',sum(len(row['points']) for row in rows),'sha256',digest)


if __name__=='__main__':main()
