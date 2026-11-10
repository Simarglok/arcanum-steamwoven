#!/usr/bin/env python3
"""ARC-RES-095: build the local redraw kit (groups of ChatGPT sheets) for ALL interface art.

Local data only (ignored `local/`); the script is in git (ARC-DEV-341). Run with the res094 venv:
    local/res094/venv/bin/python tools/hd_pack/res095/build_res095.py [--no-4x]

Inputs : local/res094/frames/<stem>__g0f<F>.png   (decoded RGBA, palette slot 0, exact alpha)
         local/res094/census.json / kinds.json    (frame sizes, kinds, interface.mes names)
         local/res094/u3_refine, u3_redraw        (style refs, finished redraws)
Outputs: local/res095/{README.md,INDEX.md,index.csv,index.json,common/,groups/}
Sheet manifest schema = res094 u3_redraw/manifest.json (coordinates in 1x UI pixels, `scale` = upload scale).
"""
import csv, collections, json, math, os, re, shutil, subprocess, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

from hd_paths import MAIN, W94, OUT  # noqa: E402
from hdlib import bleed  # noqa: E402

DO_4X = '--no-4x' not in sys.argv
PAD, GAP, BG = 8, 16, (128, 128, 128)
MAX_UP = 1700            # longest side of an uploaded sheet in px
MAX_SHEETS = 14          # sheets (= ChatGPT requests) per group, soft
FONT = ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc', 14)

# ---------------------------------------------------------------- census
census = json.load(open(f'{W94}/census.json'))
rows = {r['stem']: r for r in census['rows']}
kinds = json.load(open(f'{W94}/kinds.json'))
num_of = collections.defaultdict(list)
orig_name = {}
for n, v in census['names'].items():
    num_of[v.lower().rsplit('.', 1)[0]].append(int(n))
    orig_name[v.lower().rsplit('.', 1)[0]] = v

# ---------------------------------------------------------------- done / draft (from the coordinator's note and res094 QA)
R94 = 'local/res094'
DONE = {  # stem -> (frames done, where the chosen art lives, note)
    'inttop': ('0', f'{R94}/out/refine/S01_inttop_stitched_blend_v3.png', 'strip, 5 refined tiles stitched (S01_inttop_t1..t5)'),
    'intbotom': ('0', f'{R94}/out/refine/S02_intbotom_stitched_blend.png', 'strip, 4 refined tiles stitched (S02_intbotom_t1..t4)'),
    'nextbut': ('0', f'{R94}/u3_refine/results/B08_nextbut_f0.png', 'frame 0 redrawn; frames 1,2 = Legacy frame difference (out/U3dc_B)'),
    'combat_button': ('0', f'{R94}/u3_refine/results/B09_combat_f0.png', 'frame 0 redrawn; frames 1,2 derived (out/U3dc_B)'),
    'skills_button': ('0', f'{R94}/u3_refine/results/B10_skills_f0.png', 'frame 0 redrawn; frames 1,2 derived (out/U3dc_B)'),
    'yelovial': ('0', f'{R94}/u3_refine/results/11_vials.png', 'second-session crisper vials sheet'),
    'redvial': ('0', f'{R94}/u3_refine/results/11_vials.png', 'second-session crisper vials sheet'),
    'bluvial': ('0', f'{R94}/u3_refine/results/11_vials.png', 'second-session crisper vials sheet'),
    'clrvial': ('0', f'{R94}/u3_refine/results/11_vials.png', 'second-session crisper vials sheet'),
    'ammo_icon_gold': ('0', f'{R94}/u3_redraw/results/13_motifs_ammo.png', 'motif sheet 13, first set'),
    'ammo_icon_arrows': ('0', f'{R94}/u3_redraw/results/13_motifs_ammo.png', 'motif sheet 13, first set'),
    'ammo_icon_bullets': ('0', f'{R94}/u3_redraw/results/13_motifs_ammo.png', 'motif sheet 13, first set'),
    'ammo_icon_charges': ('0', f'{R94}/u3_redraw/results/13_motifs_ammo.png', 'motif sheet 13, first set'),
    'ammo_icon_fuel': ('0', f'{R94}/u3_redraw/results/13_motifs_ammo.png', 'motif sheet 13, first set'),
    'ammo_icon_mana': ('0', f'{R94}/u3_redraw/results/13_motifs_ammo.png', 'motif sheet 13, first set'),
    'coinsicon': ('0', f'{R94}/u3_refine/results/14_motifs_big.png', 'motif sheet 14, second set on flat grey'),
    'arrows_icon': ('0', f'{R94}/u3_refine/results/14_motifs_big.png', 'motif sheet 14, second set on flat grey'),
    'bulletsicon': ('0', f'{R94}/u3_refine/results/14_motifs_big.png', 'motif sheet 14, second set on flat grey'),
    'chargesicon': ('0', f'{R94}/u3_refine/results/14_motifs_big.png', 'motif sheet 14, second set on flat grey'),
    'fuelicon': ('0', f'{R94}/u3_refine/results/14_motifs_big.png', 'motif sheet 14, second set on flat grey'),
    'dialoguebox': ('0', f'{R94}/u3_redraw/results/D06_dialoguebox_aligned_base.png', 'first-session aligned base (D06)'),
    'inventor': ('0', f'{R94}/u3_redraw/results/03_inventor.v3-local.png', 'inventory main, masked v3-local'),
}
DRAFT = {  # a draft redraw exists in res094 but was not accepted / failed its QA: stays in the queue, flagged
    'pdoll': f'{R94}/u3_redraw/results/04_pdoll.png',
    'mainmenuback': f'{R94}/u3_redraw/results/05_mainmenuback.png',
    'saveloadbackground': f'{R94}/u3_redraw/results/07_saveloadbackground.png (failed flat-fill check)',
    'magicmeleeicon': f'{R94}/u3_redraw/results/12_icons.png (failed fidelity check)',
    'skl_heal': f'{R94}/u3_redraw/results/12_icons.png (failed fidelity check)',
}

# ---------------------------------------------------------------- per-stem plan
def sizes(r):
    return [(x['w'], x['h']) for x in r['frameRows']]

def role(stem):
    """what happens to this art: redraw | font | cursor | done"""
    k = kinds[stem]
    if k == 'font': return 'font'
    if k == 'cursor': return 'cursor'
    if stem in DONE: return 'done'
    if min(min(w, h) for w, h in sizes(rows[stem])) < 4: return 'thin'
    return 'redraw'

def redraw_frames(stem):
    """frames that go on ChatGPT sheets (the rest are derived from frame 0 by the Legacy frame difference)"""
    r = rows[stem]; k = kinds[stem]; n = r['frames']
    if k == 'animation/overlay' or (k.startswith('panel') and n > 1):
        return list(range(n))
    return [0]

def theme(stem):
    k = kinds[stem]; s = stem
    if k == 'button' or k == 'other': return 'buttons'
    if k == 'icon': return 'icons'
    if k == 'gauge': return 'gauges'
    if k == 'strip': return 'hud-frames'
    if k == 'animation/overlay' or (k.startswith('panel') and rows[stem]['frames'] > 1): return 'animations'
    big = k == 'panel>=300'
    if re.search(r'(scheme|schem|schm_|^s_)', s) and not big and s != 'schematic_base': return 'schematics'
    if s in ('intrface', 'clk_timestrip', 'tmp_day', 'tmp_nght', 'black', 'dialoguewindow', 'stone_plaque', 'bloodstone', 'nav_cvr'): return 'hud-frames'
    if re.search(r'(map$|^map|tarantsewer|tullamural|vorman|mazzerinsmystery|splash)', s): return 'maps-pictures'
    if re.match(r'(mp|multiplayer|kick_ban|privmes|broadcast|mply)', s): return 'multiplayer'
    if re.match(r'(book|logbooks|notebackground|writensidebar|newsbackground|vendnewsback)', s): return 'books-logs'
    if re.match(r'(char_|char[a-z]+_?base|skill|disciplines|spell_ro|charrace|createcharacter|pregen|createyourown|portname|choos|cvr_|fate_|sleep_|slp_|telebackground|multimove)', s): return 'character-windows'
    if re.search(r'(barter|loot|pdoll|pd_cover|inventor|follwer|follower|wp_|mineentrance)', s): return 'inventory-windows'
    if re.search(r'(menu|singleplayer|optionsbase|optionsmenu|option|saveload|saveshot)', s): return 'menus'
    if s.endswith('_rot') or s.endswith('_ro') or re.search(r'(schematic_base|combine|scheme_rot)', s): return 'craft-windows'
    return 'misc-panels'

THEME_ORDER = ['buttons', 'icons', 'gauges', 'hud-frames', 'inventory-windows', 'character-windows', 'craft-windows',
               'books-logs', 'menus', 'misc-panels', 'maps-pictures', 'animations', 'schematics', 'multiplayer']
THEME_NOTE = {
    'hud-frames': 'frames and strips of the HUD and general window chrome (top/bottom bars are done)',
    'buttons': 'buttons of every window; idle frame 0 only, pressed/lit frames are derived later',
    'icons': 'small and medium icons (64 px item/skill/race/class icons, 32 px hotkeys, markers)',
    'gauges': 'bars, pips and gauge parts (the four vials are done)',
    'character-windows': 'character sheet, skills, spells, character creation windows',
    'inventory-windows': 'inventory, loot, barter, follower windows',
    'craft-windows': 'schematic, rotator and crafting windows',
    'books-logs': 'logbook, books, notes, newspaper backgrounds',
    'menus': 'main, options and single-player menu backgrounds',
    'maps-pictures': 'town maps, murals and splash pictures',
    'misc-panels': 'everything else of panel size',
    'multiplayer': 'multiplayer windows (low priority: no multiplayer in the project)',
    'animations': 'multi-frame arts (compass, hovers, waypoints, load indicator); ALL frames go on one sheet per art',
    'schematics': 'technology schematic pictures (295 x 225, about 100 of them)',
}

# ---------------------------------------------------------------- items and sheets
def item_list():
    items = []
    for stem, r in rows.items():
        if role(stem) != 'redraw': continue
        for f in redraw_frames(stem):
            w, h = sizes(r)[f]
            items.append(dict(stem=stem, frame=f, w=w, h=h, theme=theme(stem), kind=kinds[stem]))
    return items

def pick_scale(sw, sh, tile=False):
    for s in ((8, 7, 6, 5, 4, 3, 2, 1) if tile or max(sw, sh) <= 440 else (4, 3, 2, 1)):
        if max(sw, sh) * s <= MAX_UP: return s
    return 1

def layout(items, W):
    """shelf layout at fixed sheet width W; returns (placed, sw, sh)"""
    placed, x, y, rowh = [], PAD, PAD, 0
    for it in items:
        it = dict(it)
        if x + it['w'] + PAD > W and placed:
            x, y, rowh = PAD, y + rowh + GAP, 0
        it['x'], it['y'] = x, y
        placed.append(it); x += it['w'] + GAP; rowh = max(rowh, it['h'])
    return placed, max(i['x'] + i['w'] for i in placed) + PAD, max(i['y'] + i['h'] for i in placed) + PAD

def best_layout(items, lim):
    """the width (<= lim) that gives the most compact, near-square sheet with height <= lim, or None"""
    best = None
    for W in range(max(i['w'] for i in items) + 2 * PAD, lim + 1, 8):
        pl, sw, sh = layout(items, W)
        if sh > lim: continue
        score = abs(math.log((sw / sh) / 1.3)) + 0.0005 * sw * sh / 1000
        if best is None or score < best[0]: best = (score, pl, sw, sh)
    return best

def pack(items, lim, max_items):
    """greedy chunks in the given order; each chunk gets its own best width; returns [(placed, sheetW, sheetH)]"""
    sheets, cur = [], []
    def close():
        nonlocal cur
        if cur:
            _, pl, sw, sh = best_layout(cur, lim) or (0,) + layout(cur, lim)
            sheets.append((pl, sw, sh))
        cur = []
    for it in items:
        if cur and (len(cur) >= max_items or best_layout(cur + [it], lim) is None): close()
        cur.append(it)
    close()
    return sheets

def limits(theme_name, kind):
    if theme_name in ('schematics',) or kind == 'panel100-299': return 560, 6
    if theme_name == 'animations': return 420, 40
    if theme_name == 'icons': return 420, 24
    if theme_name == 'buttons': return 420, 24
    return 420, 12

def order_key(it):
    if it['theme'] in ('icons', 'buttons', 'gauges'): return (it['h'], it['w'], it['stem'], it['frame'])
    return (it['stem'], it['frame'])

def build_sheets():
    by_theme = collections.defaultdict(list)
    for it in item_list(): by_theme[it['theme']].append(it)
    out = collections.OrderedDict()
    for t in THEME_ORDER:
        its = sorted(by_theme.get(t, []), key=order_key)
        sheets = []
        if t == 'animations':     # frames of one art stay on one sheet
            for stem in sorted({i['stem'] for i in its}):
                lim, mx = limits(t, 'x'); sheets += pack([i for i in its if i['stem'] == stem], lim, mx)
        else:
            big = [i for i in its if i['kind'] in ('panel>=300', 'strip')]
            rest = [i for i in its if i['kind'] not in ('panel>=300', 'strip')]
            for i in big:
                sheets += pack([i], 9999, 1)
            if rest:
                lim, mx = limits(t, rest[0]['kind']); sheets += pack(rest, lim, mx)
        out[t] = [tile_sheet(sh) for sh in sheets]
        out[t] = [x for lst in out[t] for x in lst]
    return out

def tile_sheet(sheet):
    """a single very wide/thin item cannot be redrawn in one request (res094: the model adds margins): cut overlapping tiles of ~4.3:1"""
    placed, sw, sh = sheet
    if len(placed) == 1 and sh / sw > 5.5 and placed[0]['h'] >= 150:    # tall thin strip: consecutive slices side by side on one sheet
        it = placed[0]; w, h = it['w'], it['h']
        k = max(2, round(math.sqrt(h / (1.3 * (w + GAP)))))
        ph = math.ceil(h / k); pcs = []
        for i in range(k):
            sy = i * ph; hh = min(ph, h - sy)
            pcs.append(dict(it, x=PAD + i * (w + GAP), y=PAD, w=w, h=hh, sx=0, sy=sy, tile=i + 1, tiles=k, split=True))
        return [(pcs, PAD * 2 + k * w + (k - 1) * GAP, PAD * 2 + ph)]
    if len(placed) != 1 or sw / sh <= 5.5 or placed[0]['w'] < 150: return [sheet]
    it = placed[0]; w, h = it['w'], it['h']
    tw = max(int(4.3 * h), 100)
    if w <= tw * 1.15: return [sheet]
    ov = max(10, tw // 9)
    n = math.ceil((w - ov) / (tw - ov)); tw2 = math.ceil((w + (n - 1) * ov) / n)
    out = []
    for i in range(n):
        sx = min(i * (tw2 - ov), w - tw2)
        t = dict(it, x=0, y=0, w=tw2, h=h, sx=sx, sy=0, tile=i + 1, tiles=n)
        out.append(([t], tw2, h))
    return out

def make_groups(by_theme):
    groups = []
    for t, sheets in by_theme.items():
        if not sheets: continue
        n = math.ceil(len(sheets) / MAX_SHEETS); size = math.ceil(len(sheets) / n)
        for k in range(n):
            groups.append([t + (f'-{k + 1}' if n > 1 else ''), [t], sheets[k * size:(k + 1) * size]])
    merged = []
    for g in groups:   # glue small neighbours
        if merged and len(merged[-1][2]) < 7 and len(g[2]) < 7 and len(merged[-1][2]) + len(g[2]) <= 16:
            merged[-1][0] += '+' + g[0]; merged[-1][1] += g[1]; merged[-1][2] += g[2]
        else:
            merged.append(g)
    return merged

# ---------------------------------------------------------------- images
def load_frame(stem, f):
    return np.array(Image.open(f'{W94}/frames/{stem}__g0f{f}.png').convert('RGBA'))

def opaque_fill(a):
    """transparent pixels take the neighbouring colour (ChatGPT cannot return alpha); returns uint8 RGB"""
    m = a[..., 3] > 0
    if not m.any(): return np.full(a.shape[:2] + (3,), 128, np.uint8)
    return np.clip(bleed(a[..., :3].astype(np.float32), m, 12) + 0.5, 0, 255).astype(np.uint8)

def near(a, s): return np.repeat(np.repeat(a, s, 0), s, 1)

def render_sheet(placed, sw, sh, s, labelled=False):
    cv = Image.new('RGB', (sw * s, sh * s), BG)
    for it in placed:
        a = load_frame(it['stem'], it['frame'])
        if 'sx' in it: a = a[it['sy']:it['sy'] + it['h'], it['sx']:it['sx'] + it['w']]
        cv.paste(Image.fromarray(near(opaque_fill(a), s)), (it['x'] * s, it['y'] * s))
    if labelled:
        d = ImageDraw.Draw(cv)
        for it in placed:
            t = f"{it['stem']}#{it['frame']}"
            d.rectangle([it['x'] * s, it['y'] * s, it['x'] * s + 8 * len(t) + 4, it['y'] * s + 16], fill=(0, 0, 0))
            d.text((it['x'] * s + 2, it['y'] * s + 1), t, fill=(255, 255, 0), font=FONT)
    return cv

# ---------------------------------------------------------------- used in
def used_in():
    stems = sorted(rows, key=len, reverse=True)
    pat = '(`|/|\\\\)(' + '|'.join(re.escape(s) for s in stems) + ')(`|\\.art)'
    p = subprocess.run(['git', '-C', MAIN, 'grep', '-I', '-i', '-o', '-E', pat, 'origin/main', '--', 'src', 'tools', 'docs'],
                       capture_output=True, text=True)
    hits = collections.defaultdict(collections.Counter)
    for line in p.stdout.splitlines():
        m = re.match(r'origin/main:([^:]+):(.*)$', line)
        if not m: continue
        path, txt = m.group(1), m.group(2).lower().strip('`/\\')
        txt = re.sub(r'\.art$', '', txt).strip('`/\\')
        if txt in rows: hits[txt][path] += 1
    res = {}
    for s in rows:
        files = hits.get(s)
        if not files: res[s] = ''; continue
        code = [os.path.basename(f) for f in files if f.endswith('.cs')]
        docs = [os.path.basename(f) for f in files if f.endswith('.md')]
        res[s] = '; '.join(sorted(set(code))[:4]) or ('docs: ' + '; '.join(sorted(set(docs))[:3]))
    return res

# ---------------------------------------------------------------- text
COMMON = open(f'{W94}/u3_refine/CHATGPT_BRIEF.md').read()
REFS = f'{OUT}/common/style_refs'
ANCHOR = {'panel': 'anchor_panel_inventor.png', 'button': 'anchor_button_nextbut.png', 'icon': 'anchor_icon_motifs14.png',
          'gauge': 'anchor_gauge_vials.png', 'anim': 'anchor_icon_motifs14.png', 'schematic': 'anchor_panel_inventor.png'}

KIND_ADD = {
 'panel': "This sheet holds one or more big window/background pictures of the interface, each in its own grey-separated cell. Black rectangles are windows other content shows through: keep them exactly the same size and perfectly flat near-black. Thin grid lines inside black fields stay in the same places and as thin as in image 1. Flat placeholder fills (a plain brown or grey rectangle) stay perfectly flat. Corner ornaments keep their exact outline. Areas that look like a plain flat fill of the neighbouring colour were transparent cut-outs in the game: keep them flat, draw nothing in them.",
 'button': "Each cell holds ONE button in its idle state (the pressed and lit states are derived later, do not draw them). Keep each round/square shape, rim, symbol and dotted/checker structure exactly in place; add only fine metal, enamel and wood detail. Do not move, merge, add or drop cells. Flat colour around a button (outside its outline) is a transparent cut-out: keep it flat.",
 'icon': "Each cell holds ONE separate icon or marker. Keep every cell exactly where it is, keep every silhouette and identity, add only fine material detail. Flat grey between cells stays plain grey; a flat fill around an icon (outside its outline) was transparent: keep it flat.",
 'gauge': "Each cell holds ONE bar, pip or gauge part. Keep shape, outline, colours and fill level exactly; add only fine material detail (glass, brass, metal). Flat fills outside outlines were transparent: keep them flat.",
 'anim': "All frames of ONE animated art are laid out in reading order (left to right, top to bottom) in a grid. Redraw ALL frames in one pass so they stay consistent: same shape and outline in every frame, only the animation phase differs as in image 1. Do not reorder, merge, add or drop frames. If the frames of one art continue on another sheet, keep the same look. Flat colour around the shapes was transparent: keep it flat.",
 'schematic': "Each cell holds ONE pre-rendered technical drawing of an item (a schematic picture on a flat panel). Keep the drawing exactly where it is, with the same outline, parts, proportions and framing; add only fine material detail (ink, paper grain, metal wear). Do not add text, symbols or extra parts. Flat fills that look like transparent cut-outs stay flat.",
}

def sheet_kind(theme_name, kind):
    if theme_name == 'schematics': return 'schematic'
    if theme_name == 'animations': return 'anim'
    if kind.startswith('panel') or kind == 'strip': return 'panel'
    return {'button': 'button', 'other': 'button', 'icon': 'icon', 'gauge': 'gauge'}.get(kind, 'panel')

def prompt_text(sid, sk, placed, sw, sh, s, group_dir):
    imgs = [f'{group_dir}/sheets/{sid}.png', f'{REFS}/ref_wood.png', f'{REFS}/ref_brass_frame_grid.png', f'{OUT}/common/style_anchors/{ANCHOR[sk]}']
    rules = COMMON.split('## Rules for every task')[1].split('## STYLE LOCK')[0].strip()
    style = '## STYLE LOCK' + COMMON.split('## STYLE LOCK')[1].split('## Tasks')[0].rstrip()
    style = style.replace('(applies to every task)', '(applies to every sheet)')
    i0 = style.index('Material references'); i1 = style.index('- WOOD')
    style = style[:i0] + 'Images 2 and 3 are MATERIAL REFERENCES ONLY: copy their wood, brass and black, never their layout, objects or composition.\n\n' + style[i1:]
    lines = [f'# {sid}: redraw request (one image)', '',
      "Attach these images, in this order (full paths on the owner's Mac):",
      f'  image 1 (EDIT TARGET): {imgs[0]}',
      f'  image 2 (material reference, wood): {imgs[1]}',
      f'  image 3 (material reference, brass and black): {imgs[2]}',
      f'  image 4 (style anchor, an already finished redraw of the same set: copy its level of detail and material only, never its layout): {imgs[3]}', '',
      'Paste the text between the lines after attaching:', '', '----------------------------------------',
      'Redraw image 1, an interface sheet of a late-1990s pre-rendered steampunk RPG, as a detailed hand-finished version of the SAME picture. Images 2 and 3 are material references, image 4 is a style anchor (same set, already finished).', '',
      '## Rules', rules, '', style, '', '## This sheet', KIND_ADD[sk], '',
      f'Image 1 is {sw * s} x {sh * s} px (the {sw} x {sh} sheet at {s}x nearest). Return the whole sheet edge to edge at the same aspect ratio; any larger size is fine (larger is better: give the maximum size you can). The grey (#808080) between and around the cells stays plain flat grey.', '',
      'Cells (position x,y in 1x units from the sheet corner, size w x h; name#frame):']
    for it in placed:
        if 'sx' in it and it.get('split'):
            extra = f" (PIECE {it['tile']} of {it['tiles']}: consecutive slices of ONE tall strip {rows[it['stem']]['frameRows'][it['frame']]['w']} x {rows[it['stem']]['frameRows'][it['frame']]['h']} px, shown side by side; this piece starts at source y={it['sy']}; keep the pattern continuous from piece to piece, the same look in every piece)"
        else:
          extra = f" (TILE {it['tile']} of {it['tiles']} of a {rows[it['stem']]['frameRows'][it['frame']]['w']} px wide art; this tile starts at source x={it['sx']}; neighbouring tiles overlap, so keep the edge content exactly as in image 1)" if 'sx' in it else ''
        lines.append(f"  - {it['stem']}#{it['frame']}: at {it['x']},{it['y']}, {it['w']} x {it['h']}{extra}")
    lines += ['', f'Save the result as: {group_dir}/results/{sid}.png', '----------------------------------------']
    return '\n'.join(lines)

# ---------------------------------------------------------------- main
def main():
    os.makedirs(OUT, exist_ok=True)
    for d in ('common/style_refs', 'common/style_anchors', 'groups'):
        os.makedirs(f'{OUT}/{d}', exist_ok=True)
    for f in ('ref_wood.png', 'ref_brass_frame_grid.png', 'ref_wood_icons_bars.png'):
        shutil.copy(f'{W94}/u3_refine/style_refs/{f}', f'{OUT}/common/style_refs/{f}')
    for k, src in (('anchor_panel_inventor.png', 'u3_redraw/results/03_inventor.v3-local.png'), ('anchor_button_nextbut.png', 'u3_refine/results/B08_nextbut_f0.png'),
                   ('anchor_icon_motifs14.png', 'u3_refine/results/14_motifs_big.png'), ('anchor_gauge_vials.png', 'u3_refine/results/11_vials.png')):
        shutil.copy(f'{W94}/{src}', f'{OUT}/common/style_anchors/{k}')
    shutil.copy(f'{W94}/u3_refine/CHATGPT_BRIEF.md', f'{OUT}/common/CHATGPT_BRIEF_RES094.md')
    shutil.copy(f'{W94}/u3_refine/STYLE_GUIDE.md', f'{OUT}/common/STYLE_GUIDE_RES094.md')
    for p in set(v[1] for v in DONE.values()) | {'local/res094/u3_redraw/results/' + os.path.basename(v.split(' ')[0]) for v in DRAFT.values()}:
        if not os.path.exists(f'{MAIN}/{p}'): print('MISSING', p)

    by_theme = build_sheets()
    groups = make_groups(by_theme)
    used = used_in()

    index = {}
    for stem, r in rows.items():
        index[stem] = dict(stem=stem, art_key=r['key'], name=orig_name.get(stem, stem + '.art'), nums=sorted(num_of.get(stem, [])), kind=kinds[stem], frames=r['frames'],
                           size=';'.join(sorted({f'{w}x{h}' for w, h in sizes(r)})), pixels=r['totalPix'], holes=bool(r['holeFrames']),
                           role=role(stem), status='', group='', sheets=[], redraw_frames='', derived_frames='', used_in=used.get(stem, ''), note='')

    group_dirs, manifest_all = {}, []
    for gi, (gname, gthemes, sheets) in enumerate(groups, 1):
        gid = f'G{gi:02d}_{gname}'
        gdir = f'{OUT}/groups/{gid}'
        group_dirs[gid] = gdir
        for d in ('sheets', 'labelled', 'prompts', 'results', 'frames_1x', 'frames_4x'):
            os.makedirs(f'{gdir}/{d}', exist_ok=True)
        man, stems = [], []
        for si, (placed, sw, sh) in enumerate(sheets, 1):
            sid = f'G{gi:02d}_s{si:02d}'
            s = pick_scale(sw, sh, 'tile' in placed[0])
            sk = sheet_kind(placed[0]['theme'], placed[0]['kind'])
            render_sheet(placed, sw, sh, s).save(f'{gdir}/sheets/{sid}.png')
            lab = render_sheet(placed, sw, sh, s, labelled=True)
            lab.resize((max(1, lab.width // 2), max(1, lab.height // 2)), Image.NEAREST).save(f'{gdir}/labelled/{sid}.labelled.png')
            open(f'{gdir}/prompts/{sid}.txt', 'w').write(prompt_text(sid, sk, placed, sw, sh, s, gdir))
            man.append(dict(id=sid, kind=sk, sheetW=sw, sheetH=sh, scale=s, pad=PAD, gap=GAP,
                            frames=[dict(stem=i['stem'], frame=i['frame'], x=i['x'], y=i['y'], w=i['w'], h=i['h'], art_key=rows[i['stem']]['key'], **({'src_x': i['sx'], 'src_y': i['sy'], 'tile': i['tile'], 'tiles': i['tiles'], 'split': bool(i.get('split'))} if 'sx' in i else {})) for i in placed],
                            source=f'sheets/{sid}.png', result=f'results/{sid}.png'))
            for i in placed:
                stems.append(i['stem']); index[i['stem']]['sheets'].append(sid)
        stems = sorted(set(stems))
        json.dump(man, open(f'{gdir}/manifest.json', 'w'), indent=1)
        manifest_all.append((gid, gthemes, man, stems))
        for stem in stems: index[stem]['group'] = gid
    extra = {'Z1_cursors-keep-legacy': [s for s in rows if role(s) in ('cursor', 'thin')],
             'Z2_done': [s for s in rows if role(s) == 'done'],
             'Z3_fonts-not-redrawn': [s for s in rows if role(s) == 'font']}
    for gid, stems in extra.items():
        for s in stems: index[s]['group'] = gid
    # frames (original + 4x) into the owning group (every frame of every art, redrawn or derived)
    owners = collections.defaultdict(list)
    for s, v in index.items(): owners[v['group']].append(s)
    for gid, stems in owners.items():
        gdir = f'{OUT}/groups/{gid}'
        os.makedirs(f'{gdir}/frames_1x', exist_ok=True)
        want4 = DO_4X and not gid.startswith('Z3')
        if want4: os.makedirs(f'{gdir}/frames_4x', exist_ok=True)
        for stem in stems:
            for f in range(rows[stem]['frames']):
                shutil.copy(f'{W94}/frames/{stem}__g0f{f}.png', f'{gdir}/frames_1x/{stem}__g0f{f}.png')
                if want4:
                    dst = f'{gdir}/frames_4x/{stem}__g0f{f}.png'
                    if not os.path.exists(dst): Image.fromarray(near(load_frame(stem, f), 4)).save(dst)
    for stem, v in index.items():
        if v['role'] == 'redraw':
            rf = redraw_frames(stem)
            v['status'] = 'draft-exists' if stem in DRAFT else 'todo'
            if stem in DRAFT: v['note'] = 'draft in ' + DRAFT[stem]
            v['redraw_frames'] = ','.join(map(str, rf))
            v['derived_frames'] = ','.join(str(f) for f in range(v['frames']) if f not in rf)
        elif v['role'] == 'done':
            v['status'] = 'DONE'; v['note'] = f"{DONE[stem][2]} -> {DONE[stem][1]}"
            v['redraw_frames'] = DONE[stem][0]
            v['derived_frames'] = ','.join(str(f) for f in range(1, v['frames']))
        elif v['role'] == 'thin':
            v['status'] = 'keep-legacy'; v['note'] = 'too thin to redraw (under 4 px): stays Legacy nearest'
        elif v['role'] == 'cursor':
            v['status'] = 'keep-legacy'; v['note'] = 'owner decision: cursors stay Legacy nearest (RES-094)'
        else:
            v['status'] = 'not-redrawn'; v['note'] = 'bitmap font glyphs, listed only'
    json.dump(dict(groups=[(g, t, [m['id'] for m in ms], st) for g, t, ms, st in manifest_all], index=index, extra={k: len(v) for k, v in extra.items()}),
              open(f'{OUT}/.build_state.json', 'w'), indent=1)
    return manifest_all, index

if __name__ == '__main__':
    man, index = main()
    print(len(man), 'groups', sum(len(m) for _, _, m, _ in man), 'sheets')
    for g, t, ms, st in man: print(g, len(ms), 'sheets', len(st), 'arts')
