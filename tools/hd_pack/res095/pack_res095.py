#!/usr/bin/env python3
"""ARC-RES-095: from the fitted cells to a DEV-242 import plan.

1. stitch tiled arts (overlapping tiles, vertical pieces), 2. QA table (ready/qa.json, QA_REPORT.md),
3. review sheets (source | registered redraw), 4. ready/plan.json for tools/hd-interface-import (mode ready/exact).
Local data only. Run with the res094 venv:  local/res094/venv/bin/python tools/hd_pack/res095/pack_res095.py [--min-ncc 0.5]
"""
import sys, os, json, glob, collections
import numpy as np
from PIL import Image
from hdlib import load, luma
from hd_paths import OUT, W94

MIN_NCC = 0.5
if '--min-ncc' in sys.argv: MIN_NCC = float(sys.argv[sys.argv.index('--min-ncc') + 1])
PACK_ID = 'interface-redraw-4x'

st = json.load(open(f'{OUT}/.build_state.json'))
index = st['index']
census = json.load(open(f'{W94}/census.json'))
rows = {r['stem']: r for r in census['rows']}
for d in ('frames', 'review'): os.makedirs(f'{OUT}/ready/{d}', exist_ok=True)

# ------------------------------------------------------------ collect cells
cells = collections.defaultdict(list)     # (stem, frame) -> [(frame entry, cell img path, ncc, sheet id, log dict)]
sheet_logs = {}
manifests = {}
for gdir in sorted(glob.glob(f'{OUT}/groups/G*')):
    for m in json.load(open(f'{gdir}/manifest.json')):
        manifests[m['id']] = (m, gdir)
        lp = f"{OUT}/ready/fit_log/{m['id']}.json"
        if not os.path.exists(lp): continue
        lg = json.load(open(lp)); sheet_logs[m['id']] = lg
        by = {c['name']: c for c in lg['cells']}
        for f in m['frames']:
            name = f"{f['stem']}__g0f{f['frame']}" + (f"__t{f['tile']}" if 'tile' in f else '')
            c = by.get(name)
            if c: cells[(f['stem'], f['frame'])].append((f, f"{OUT}/ready/cells/{name}.png", c['ncc'], m['id'], c))

def stitch(parts, W, H):
    acc = np.zeros((H * 4, W * 4, 3), np.float64); wt = np.zeros((H * 4, W * 4), np.float64)
    horiz = sorted([p for p in parts if not p[0].get('split')], key=lambda p: p[0]['src_x'])
    for i, (f, path, *_r) in enumerate(parts):
        img = np.array(Image.open(path).convert('RGB')).astype(np.float64)
        h, w = img.shape[:2]
        x0, y0 = f['src_x'] * 4, f['src_y'] * 4
        wx = np.ones(w)
        if not f.get('split') and f in [p[0] for p in horiz]:
            k = [p[0] for p in horiz].index(f)
            if k > 0:
                pv = horiz[k - 1][0]; lo = (pv['src_x'] + pv['w'] - f['src_x']) * 4
                if lo > 0: wx[:lo] *= (np.arange(lo) + 0.5) / lo
            if k < len(horiz) - 1:
                nx = horiz[k + 1][0]; ro = (f['src_x'] + f['w'] - nx['src_x']) * 4
                if ro > 0: wx[-ro:] *= 1 - (np.arange(ro) + 0.5) / ro
        acc[y0:y0 + h, x0:x0 + w] += img * wx[None, :, None]; wt[y0:y0 + h, x0:x0 + w] += wx[None, :]
    return np.clip(acc / np.maximum(wt, 1e-6)[..., None] + 0.5, 0, 255).astype(np.uint8), float(wt.min())

qa = {}
for (stem, fr), parts in sorted(cells.items()):
    fw, fh = rows[stem]['frameRows'][fr]['w'], rows[stem]['frameRows'][fr]['h']
    ncc = min(p[2] for p in parts)
    a_ = load(stem, 0, fr); m_ = a_[..., 3] > 0
    flat = (not m_.any()) or float(luma(a_[..., :3].astype(np.float32))[m_].std()) < 3.0
    if flat: ncc = 1.0   # a flat frame has no structure to correlate: NCC is undefined, the lock fixes the colour
    tiled = 'tile' in parts[0][0]
    ok = True; note = ''
    if tiled:
        need = parts[0][0]['tiles']
        if len(parts) != need: ok = False; note = f'{len(parts)} of {need} tiles ready'
        else:
            img, wmin = stitch(parts, fw, fh)
            if wmin <= 0: ok = False; note = 'tiles leave a gap'
            else: Image.fromarray(img).save(f'{OUT}/ready/frames/{stem}__g0f{fr}.png')
    else:
        Image.open(parts[0][1]).save(f'{OUT}/ready/frames/{stem}__g0f{fr}.png')
    c0 = parts[0][4]
    aniso = max(abs(p[4]['px_per_ui'] / p[4]['px_per_ui_y'] - 1) for p in parts)
    sheet = sheet_logs[parts[0][3]]
    flags = []
    if flat: flags.append('flat source (NCC n/a)')
    if ncc < MIN_NCC: flags.append(f'low NCC {ncc:.2f}')
    if aniso > 0.05: flags.append(f'anisotropic {aniso:.2f}')
    if sheet.get('grad_mode'): flags.append('edge-map fit (tone differs strongly)')
    if not ok: flags.append(note)
    qa[f'{stem}#{fr}'] = dict(stem=stem, frame=fr, sheets=sorted({p[3] for p in parts}), ncc=round(ncc, 3), aniso=round(aniso, 3), complete=ok, flags=flags,
                              aspect_ratio=round(sheet['aspect_ratio'], 3), included=ok and ncc >= MIN_NCC)
json.dump(qa, open(f'{OUT}/ready/qa.json', 'w'), indent=1)

# ------------------------------------------------------------ review sheets
for sid, (m, gdir) in manifests.items():
    if sid not in sheet_logs: continue
    s = 2 if max(m['sheetW'], m['sheetH']) <= 420 else 1
    src = Image.open(f"{gdir}/sheets/{sid}.png").convert('RGB')
    left = src.resize((m['sheetW'] * s, m['sheetH'] * s), Image.NEAREST if m['scale'] >= s else Image.LANCZOS)
    right = Image.new('RGB', left.size, (128, 128, 128))
    for f in m['frames']:
        name = f"{f['stem']}__g0f{f['frame']}" + (f"__t{f['tile']}" if 'tile' in f else '')
        p = f"{OUT}/ready/cells/{name}.png"
        if os.path.exists(p):
            im = Image.open(p).convert('RGB'); right.paste(im.resize((f['w'] * s, f['h'] * s), Image.LANCZOS), (f['x'] * s, f['y'] * s))
    both = Image.new('RGB', (left.width * 2 + 8, left.height), (0, 0, 0)); both.paste(left, (0, 0)); both.paste(right, (left.width + 8, 0))
    both.save(f'{OUT}/ready/review/{sid}.jpg', quality=88)

# ------------------------------------------------------------ plan
old = json.load(open(f'{W94}/plan-interface.json'))
items = [dict(it, lock=False) for it in old['items']]   # ARC-UI-114: the importer honours lock:false in sheet/exact/derived too
done_arts = {a['art'].lower() for it in items for a in it['tiles']}
n_add = 0; excluded = []
aliases = json.load(open(f'{OUT}/ready/aliases.json'))
EXCLUDE = set()
# ARC-UI-113: overlay buttons keep only their round collar (Legacy pixels from the frame corner): the rest of the sprite is a piece of the plate it sits on
ELLIPSE = {'char_minus': [9.1, 9.4, 8.2, 8.2], 'char_plus': [10.4, 9.4, 8.2, 8.2], 'char_htftminus': [10.3, 10.4, 9.7, 9.7], 'char_htftplus': [11.0, 10.4, 9.3, 9.3]}
for key, q in sorted(qa.items()):
    stem, fr = q['stem'], q['frame']
    v = index[stem]
    name = v['name']
    if name.lower() in done_arts: continue
    if stem in EXCLUDE: continue   # overlay sprites that carry a piece of the plate they sit on: the redrawn piece shows as a square patch on the redrawn plate
    if stem in aliases: continue   # byte-identical art: same key as its target, would fail pack verify with DuplicateKey
    if not q['included']: excluded.append(key); continue
    img = f'frames/{stem}__g0f{fr}.png'
    tile = {'art': name, 'frame': fr}
    if stem in ELLIPSE and fr == 0: tile['ellipse'] = ELLIPSE[stem]
    derived = [{'art': name, 'frame': k, 'from': fr} for k in [int(x) for x in v['derived_frames'].split(',') if x] if fr == 0 and (rows[stem]['frameRows'][k]['w'], rows[stem]['frameRows'][k]['h']) == (rows[stem]['frameRows'][0]['w'], rows[stem]['frameRows'][0]['h'])] if v['role'] == 'redraw' else []
    if derived:
        items.append({'image': img, 'mode': 'exact', 'tiles': [tile], 'derived': derived, 'lock': False})
    else:
        items.append({'image': img, 'mode': 'ready', 'images': [img], 'tiles': [tile], 'lock': False})
    n_add += 1
plan = dict(schema=1, kind='arcanum-steamwoven-hd-interface-plan', packId=PACK_ID, scale=4, items=items)
json.dump(plan, open(f'{OUT}/ready/plan.json', 'w'), indent=1)
print('cells', len(qa), 'included items added', n_add, 'excluded', len(excluded), 'plan items', len(items))
print('excluded:', excluded[:80])
