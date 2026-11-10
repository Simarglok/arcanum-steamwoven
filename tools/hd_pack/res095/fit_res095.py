#!/usr/bin/env python3
"""ARC-RES-095: register every ChatGPT sheet result to its source sheet (U3 method of RES-094, manifest driven).

Writes, per redrawn cell, a registered raw HD image `ready/cells/<stem>__g0f<F>__<tile>.png` (exactly 4*w x 4*h, RGB) and a log
`ready/fit_log/<sheet>.json` with the sheet and cell NCC. Local data only (reads and writes the main checkout's ignored local/res095). Run with the res094 venv:
    local/res094/venv/bin/python tools/hd_pack/res095/fit_res095.py [--jobs N] [SHEET_ID ...]
"""
import sys, os, json, glob, time
import numpy as np
from PIL import Image
from scipy import ndimage as ndi
from scipy.optimize import minimize
from scipy.ndimage import map_coordinates
from hdlib import luma, nearest, load, bleed as hdbleed
from metrics import comp_luma, shift_of
from hd_paths import OUT

SCALE = 4

def content_bbox(rgb):
    nonwhite = rgb.min(2) < 244
    rows = np.nonzero(nonwhite.mean(1) > 0.03)[0]; cols = np.nonzero(nonwhite.mean(0) > 0.03)[0]
    if len(rows) == 0 or len(cols) == 0: return 0, 0, rgb.shape[1], rgb.shape[0]
    return cols[0], rows[0], cols[-1] + 1, rows[-1] + 1

def sample(Lr, xs, ys, order=1):
    return map_coordinates(Lr, [ys, xs], order=order, mode='nearest')

def ncc(a, b):
    a = a - a.mean(); b = b - b.mean()
    return float((a * b).sum() / np.sqrt((a * a).sum() * (b * b).sum() + 1e-9))

def make_xy(p, x0, y0, w, h, up):
    sx, sy, tx, ty = p
    gx, gy = np.meshgrid((np.arange(w * up) + 0.5) / up, (np.arange(h * up) + 0.5) / up)
    return sx * (gx + x0) + tx, sy * (gy + y0) + ty

def gradmag(x):
    gy, gx = np.gradient(x)
    return np.hypot(gx, gy)

def register(Lr, Ls, rect, init, bounds, up=2, blur=1.5, grad=False, only_eval=False):
    """grad=True: correlate edge maps (a redraw may invert the luma polarity of plate against wood)"""
    x, y, w, h = rect
    ref = ndi.gaussian_filter(np.kron(Ls[y:y + h, x:x + w], np.ones((up, up))), blur * up)
    if grad: ref = gradmag(ref)
    def cost(p):
        xs, ys = make_xy(p, x, y, w, h, up)
        cur = ndi.gaussian_filter(sample(Lr, xs, ys), blur * up)
        if grad: cur = gradmag(cur)
        return -ncc(ref, cur)
    if only_eval: return np.array(init, float), -cost(init)
    r = minimize(cost, np.array(init, float), method='Powell', bounds=bounds, options=dict(xtol=1e-3, ftol=1e-6, maxiter=40))
    c0 = -cost(init)
    if -r.fun >= c0: return r.x, -r.fun      # Powell with bounds can end worse than its start: keep the better of the two
    return np.array(init, float), c0

def fit_sheet(m, gdir):
    sid = m['id']; scale = m['scale']; pad = m['pad']
    res = np.array(Image.open(f'{gdir}/results/{sid}.png').convert('RGB')).astype(np.float32)
    src = np.array(Image.open(f'{gdir}/sheets/{sid}.png').convert('RGB'))
    S1 = src[::scale, ::scale].astype(np.float32)
    Ls = luma(S1); Wd, H = m['sheetW'], m['sheetH']
    Lr = luma(res)
    Lr = ndi.gaussian_filter(Lr, max(0.5, 0.4 * ((res.shape[1] / Wd + res.shape[0] / H) / 2)))   # one UI pixel box-ish, avoids aliasing when sampled
    x0, y0, x1, y1 = content_bbox(res)
    sx0 = (x1 - x0) / Wd; sy0 = (y1 - y0) / H
    init = [sx0, sy0, x0, y0]
    log = dict(id=sid, size=[res.shape[1], res.shape[0]], bbox=[int(x0), int(y0), int(x1), int(y1)], aspect_ratio=float((res.shape[1] / res.shape[0]) / (Wd / H)), cells=[])
    rect = (0, 0, Wd, H)
    bounds = [(sx0 * 0.88, sx0 * 1.12), (sy0 * 0.88, sy0 * 1.12), (x0 - 0.12 * (x1 - x0), x0 + 0.12 * (x1 - x0)), (y0 - 0.12 * (y1 - y0), y0 + 0.12 * (y1 - y0))]
    upS = 1 if max(Wd, H) > 700 else 2
    _, c_init = register(Lr, Ls, rect, init, bounds, up=upS, only_eval=True)
    grad = c_init < 0.35
    pS, cS = register(Lr, Ls, rect, init, bounds, up=upS, grad=grad)
    log['grad_mode'] = bool(grad); log['ncc_init_luma'] = float(c_init)
    log['sheet'] = dict(params=[float(v) for v in pS], ncc=float(cS), px_per_ui=float(pS[0]), px_per_ui_y=float(pS[1]))
    os.makedirs(f'{OUT}/ready/cells', exist_ok=True)
    for f in m['frames']:
        st, fi = f['stem'], f['frame']; w, h = f['w'], f['h']
        tile = f.get('tile', 0)
        a = load(st, 0, fi)
        if 'src_x' in f: a = a[f['src_y']:f['src_y'] + h, f['src_x']:f['src_x'] + w]
        b = [(pS[0] * 0.97, pS[0] * 1.03), (pS[1] * 0.97, pS[1] * 1.03), (pS[2] - 4 * pS[0], pS[2] + 4 * pS[0]), (pS[3] - 4 * pS[1], pS[3] + 4 * pS[1])]
        p, c = register(Lr, Ls, (f['x'], f['y'], w, h), pS, b, up=2 if max(w, h) < 200 else 1, grad=grad)
        p = np.array(p, float)
        gx, gy = np.meshgrid((np.arange(w * SCALE) + 0.5) / SCALE, (np.arange(h * SCALE) + 0.5) / SCALE)
        A0 = comp_luma(nearest(a))
        for _ in range(4):   # rigid translation refinement against the Legacy silhouette
            xs = p[0] * (gx + f['x']) + p[2]; ys = p[1] * (gy + f['y']) + p[3]
            hd = np.stack([map_coordinates(res[..., k], [ys - 0.5, xs - 0.5], order=3, mode='nearest') for k in range(3)], -1)
            hd = np.clip(hd, 0, 255)
            if A0.std() < 4 or grad: break
            Bm = comp_luma(np.dstack([hd, nearest(a[..., 3:4], 4)[..., 0]]).astype(np.uint8))
            try: sh = shift_of(ndi.gaussian_filter(A0, 4), ndi.gaussian_filter(Bm, 4), 10) / 4
            except Exception: break
            p[2] -= p[0] * sh[1]; p[3] -= p[1] * sh[0]
            if np.hypot(*sh) < 0.05: break
        xs = p[0] * (gx + f['x']) + p[2]; ys = p[1] * (gy + f['y']) + p[3]
        # keep the cubic kernel inside the cell: outside it the redraw holds the grey gap or the white margin, which shows as bright edge lines
        mx = 2.0; xs = np.clip(xs, p[0] * f['x'] + p[2] + mx, p[0] * (f['x'] + w) + p[2] - mx); ys = np.clip(ys, p[1] * f['y'] + p[3] + mx, p[1] * (f['y'] + h) + p[3] - mx)
        hd = np.stack([map_coordinates(res[..., k], [ys - 0.5, xs - 0.5], order=3, mode='nearest') for k in range(3)], -1)
        hd = np.clip(hd, 0, 255)
        hd, guarded = guard(hd, a)
        name = f'{st}__g0f{fi}' + (f'__t{tile}' if 'tile' in f else '')
        Image.fromarray((hd + 0.5).astype(np.uint8)).save(f'{OUT}/ready/cells/{name}.png')
        log['cells'].append(dict(name=name, guarded=float(guarded), ncc=float(c), px_per_ui=float(p[0]), px_per_ui_y=float(p[1]), params=[float(v) for v in p]))
    os.makedirs(f'{OUT}/ready/fit_log', exist_ok=True)
    json.dump(log, open(f'{OUT}/ready/fit_log/{sid}.json', 'w'), indent=1)
    return log


def guard(hd, a):
    """pull texels where the redraw's 4x4 mean colour is far from the Legacy colour back towards the (bicubic) Legacy frame:
    bright edge lines, tan corners and similar patches the lock cannot remove; returns (rgb, share of opaque texels touched)"""
    h, w = a.shape[:2]
    m = a[..., 3] > 0
    if not m.any(): return hd, 0.0
    leg = a[..., :3].astype(np.float32)
    box = hd.reshape(h, 4, w, 4, 3).mean((1, 3))
    mf = m.astype(np.float32)
    sm = lambda x: np.stack([ndi.gaussian_filter(x[..., k] * mf, 1.2) for k in range(3)], -1) / np.maximum(ndi.gaussian_filter(mf, 1.2), 1e-3)[..., None]
    dist = np.abs(sm(box) - sm(leg)).max(2)      # patches of several texels, not the dither
    wt = np.clip((dist - 20.0) / 25.0, 0, 1) * m
    # only near the cell border and the alpha silhouette: there sit the bright lines and tan corners. Inside the art a strong colour
    # change is the redraw itself (a lapis orb instead of the flat dithered blue) and must stay
    inside = np.pad(m, 1, constant_values=False)
    near = np.clip(1 - (ndi.distance_transform_edt(inside)[1:-1, 1:-1] - 5.0) / 3.0, 0, 1)
    wt = wt * near
    if wt.max() <= 0: return hd, 0.0
    wt = ndi.gaussian_filter(wt, 0.6)
    leg_fill = hdbleed(leg, m)
    up = np.array(Image.fromarray(np.clip(leg_fill + 0.5, 0, 255).astype(np.uint8)).resize((w * 4, h * 4), Image.BICUBIC)).astype(np.float32)
    W4 = np.array(Image.fromarray(wt.astype(np.float32), 'F').resize((w * 4, h * 4), Image.BILINEAR))[..., None]
    return hd * (1 - W4) + up * W4, float(((wt > 0.5) & m).sum() / m.sum())

def job(args):
    m, gdir = args
    t = time.time()
    try:
        lg = fit_sheet(m, gdir)
        return m['id'], 'ok', lg['sheet']['ncc'], min(c['ncc'] for c in lg['cells']), round(time.time() - t)
    except Exception as e:
        return m['id'], 'ERR ' + repr(e)[:200], 0, 0, 0

if __name__ == '__main__':
    argv = sys.argv[1:]
    jobs = 8
    if '--jobs' in argv:
        i = argv.index('--jobs'); jobs = int(argv[i + 1]); del argv[i:i + 2]
    todo = []
    for gdir in sorted(glob.glob(f'{OUT}/groups/G*')):
        for m in json.load(open(f'{gdir}/manifest.json')):
            if argv and m['id'] not in argv: continue
            if os.path.exists(f"{gdir}/results/{m['id']}.png"): todo.append((m, gdir))
    print(len(todo), 'sheets', flush=True)
    from multiprocessing import Pool
    with Pool(jobs) as pool:
        for r in pool.imap_unordered(job, todo):
            print(*r, flush=True)
