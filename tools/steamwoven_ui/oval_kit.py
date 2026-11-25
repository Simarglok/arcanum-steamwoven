#!/usr/bin/env python3
"""ARC-UI-3271 (batch B-BUTTONS): the inventory's hand and arrow ovals as ONE kit part, BY RULE (no Codex): the brass rim of the oval AND the recessed iron plate behind it whose four corners show,
drawn together on one canvas, so a surface lays one master where it used to carry the old art's ring and the interim analytic triangles of #3290.

    python3 -B tools/steamwoven_ui/oval_kit.py rule [--dry-run] [--local-root DIR]      (the master `plate.inventory_oval` and its rule record under local/steamwoven-ui/)
    python3 -B tools/steamwoven_ui/oval_kit.py self-test

The part (master px of the 2880 x 2160 composition, one canvas of 300 x 168 = the plate's rectangle of 286 x 156 plus the margin the rim needs; both ovals of the inventory, hand and arrow, are the same part):

* **Rim**: an ELLIPSE ring, outer semi-axes 141 x 78 (the rim box measured on the original, `disc_boxes`), RING_WIDTH px wide, the shape analytic and anti-aliased. Its material is the polished cast brass of the lens ring
  (#3261, `hardware_kit.py`): the body tone and the fine grain are the kit's approved brass (the HUD rings of #3259 and the socket rings of #3260 unrolled and laid end to end along the ellipse's arc length), the light is
  analytic and follows the SURFACE NORMAL of the ellipse (a sharp highlight at the top left, alternating light and dark reflection bands with sharp edges, a warm copper glint at the bottom right, a narrow dark chamfer with
  a thin bright edge on the inner side, a roll-off and no dark outline on the outer side). The inside of the ring is transparent: the surface keeps the original's parchment and glyph there.
* **Plate**: the dark rectangular plate shows only in the four corners of its rectangle, as triangles: the rectangle minus an ellipse concentric with the rim and larger than it (semi-axes 160 x 98, fitted on the original,
  #3290), the straight legs hard, the arc anti-aliased. Its material is the kit's dark iron (the iron plate of the menu strips, #3260: its mottled grain and its tone, a little warm), not a flat fill. The thin bevel runs along every
  edge of the plate and follows the same light as the rim (from the upper left): an edge that faces the light is lit, an edge that faces away is dark, over BEVEL_WIDTH px. The rim's soft shadow (the surfaces' disc shadow: the
  ellipse shifted 13 px down and right, Gaussian sigma 16, strength 0.7) is baked into the plate, because the part is laid over the surface after its shadow.

Everything stays under the ignored `local/steamwoven-ui/`; nothing here holds art. Standard library only (`pngio`). Exit 0 = done, 1 = a check failed, 2 = an input could not be used.
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import hardware_kit, pngio, prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import hardware_kit  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore

TOOL_VERSION = 1
PART = "plate.inventory_oval"
CANVAS = (300, 168)            # master px, on the 6 px grid (SWI-LAY-7)
PLATE = (7, 6, 293, 162)       # the plate's rectangle in the canvas (x0, y0, x1, y1; x1 and y1 exclusive): 286 x 156, measured on the original (x 2531 to 2817, y 1191 to 1347 for the hand oval)
CENTRE = (149.0, 83.0)         # the rim's centre in the canvas (the original's rim box centre, 2673 x 1268 for the hand oval)
RIM = (141.0, 78.0)            # the rim's OUTER semi-axes (the rim box of the original, 282 x 156)
CORNER = (160.0, 98.0)         # the semi-axes of the ellipse the plate's corner triangles are cut by (concentric, larger than the rim)
RING_WIDTH = 10.0              # px: the brass band (the old art's gold band is 5 px; the new one covers it and the dark dots of its inner edge)
BEVEL_WIDTH = 2.5              # px: the plate's bevel
BEVEL_LIT, BEVEL_DARK = 0.9, 0.6   # an edge facing straight at the light is this much lighter, straight away from it this much darker
LIGHT = (-math.sqrt(0.5), -math.sqrt(0.5))   # towards the light: the upper left (image coordinates)
IRON_TINT = (1.04, 1.0, 0.94)  # a little warm, the wood round it is brown (the original's plate is (44, 40, 35))
IRON_SOURCE = ("hardware.menu_plate_left", (300, 0))   # the kit's dark iron (#3260), and where in it the window of the plate is taken
IRON_EDGE = 2                  # px: the iron master's soft first and last rows are not used
SHADOW = (13, 13, 16.0, 0.7)   # dx, dy, sigma, strength: the same as `surface_compose.cast_disc_shadows`
EDGE_SAMPLES, EDGE_REACH = hardware_kit.EDGE_SAMPLES, hardware_kit.EDGE_REACH   # the lens ring's edge filter: a tent of 2 px, 8 x 8 sub-samples per pixel, so the analytic edges fade without a stair
BRASS_ROWS, GRAIN = hardware_kit.BRASS_ROWS, hardware_kit.GRAIN     # the lens ring's: rows of the brass texture across the band, how strongly the grain modulates
brass_material, light_of, brass_colour = hardware_kit.brass_material, hardware_kit.light_of, hardware_kit.brass_colour
KitError = hardware_kit.KitError


lum, clamp = hardware_kit.lum, hardware_kit.clamp


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


# the brass is the lens ring's (#3261): `hardware_kit.brass_material` (the kit's approved brass unrolled), `light_of` and `brass_colour` (the analytic light), imported, not copied


# --- the ellipse -------------------------------------------------------------------------------------------------------------------------------

def arc_table(a: float, b: float, steps: int = 2880) -> list[float]:
    """The arc length of the ellipse from the angle 0 to the parameter angle 2 pi k / steps (px)."""
    table, total = [0.0], 0.0
    for k in range(steps):
        t0, t1 = 2 * math.pi * k / steps, 2 * math.pi * (k + 1) / steps
        tm = (t0 + t1) / 2
        total += math.hypot(a * math.sin(tm), b * math.cos(tm)) * (t1 - t0)
        table.append(total)
    return table


def arc_at(table: list[float], t: float) -> float:
    steps = len(table) - 1
    u = (t % (2 * math.pi)) / (2 * math.pi) * steps
    k = min(steps - 1, int(u))
    return table[k] + (table[k + 1] - table[k]) * (u - k)


def signed_edge(x: float, y: float, a: float, b: float) -> tuple[float, float, float]:
    """The ellipse (semi-axes a, b, centre at the origin) at the point (x, y): the distance to it in px (positive outside, first order) and the outward unit normal."""
    u, v = x / a, y / b
    f = math.hypot(u, v)
    gx, gy = u / a, v / b
    g = math.hypot(gx, gy)
    if f < 1e-9 or g < 1e-12:
        return -min(a, b), 0.0, -1.0
    return (f - 1.0) / (g / f), gx / g, gy / g


def tent_share(x: float, y: float, test) -> float:
    """The tent-filtered share (the lens ring's filter) of the pixel whose centre is (x, y) where `test(px, py)` holds."""
    n = EDGE_SAMPLES
    got = total = 0.0
    for j in range(n):
        oy = (j + 0.5) / n * 2 - 1
        wy = 1 - abs(oy)
        for i in range(n):
            ox = (i + 0.5) / n * 2 - 1
            weight = wy * (1 - abs(ox))
            total += weight
            if test(x + ox * EDGE_REACH, y + oy * EDGE_REACH):
                got += weight
    return got / total


def shadow_field(x_range: range, y_range: range) -> dict[tuple[int, int], float]:
    """The rim's soft shadow at the canvas pixels: the ellipse of the rim shifted by (dx, dy) and blurred by a Gaussian (sigma); exact along the rows (the erf of each row's span), summed along the columns."""
    dx, dy, sigma, _strength = SHADOW
    cx, cy = CENTRE[0] + dx, CENTRE[1] + dy
    a, b = RIM
    root = sigma * math.sqrt(2.0)
    reach = int(math.ceil(3 * sigma))
    rows = {}
    for yy in range(int(cy - b) - reach - 1, int(cy + b) + reach + 2):
        yc = yy + 0.5 - cy
        if abs(yc) >= b:
            continue
        half = a * math.sqrt(1.0 - (yc / b) ** 2)
        rows[yy] = (cx - half, cx + half)
    kernel = {k: math.exp(-(k * k) / (2 * sigma * sigma)) for k in range(-reach, reach + 1)}
    norm = sum(kernel.values())
    out = {}
    cache: dict[tuple[int, int], float] = {}
    for y in y_range:
        for x in x_range:
            total = 0.0
            for k, w in kernel.items():
                span = rows.get(y + k)
                if span is None:
                    continue
                key = (y + k, x)
                value = cache.get(key)
                if value is None:
                    value = 0.5 * (math.erf((span[1] - (x + 0.5)) / root) - math.erf((span[0] - (x + 0.5)) / root))
                    cache[key] = value
                total += w * value
            out[(x, y)] = total / norm
    return out


# --- the iron ----------------------------------------------------------------------------------------------------------------------------------

def iron_window(masters: Path) -> tuple[pngio.Image, str]:
    """The kit's dark iron under the plate: a window of the menu strips' iron plate (its mottled grain and tone), taken at IRON_SOURCE's place, the missing rows mirrored."""
    name, (ox, oy) = IRON_SOURCE
    path = masters / f"{name}.png"
    if not path.is_file():
        raise KitError(f"{path} is missing: the approved iron of the kit (#3260) the plate's material is taken from")
    src = pngio.read(path)
    w, h = PLATE[2] - PLATE[0], PLATE[3] - PLATE[1]
    if ox + w > src.w:
        raise KitError("the iron master is too narrow for the plate's window")
    out = pngio.Image.blank(w, h)
    top, rows = oy + IRON_EDGE, src.h - oy - 2 * IRON_EDGE       # the iron master's first and last rows are its soft edge: the window uses the rows between, mirrored (ping-pong) when the plate is taller
    for y in range(h):
        k = y % (2 * rows)
        sy = top + (k if k < rows else 2 * rows - 1 - k)
        for x in range(w):
            p = src.pixel(ox + x, sy)
            if p[3] < 250:
                raise KitError("the window of the kit's iron is not opaque: the plate's place in the iron master no longer fits")
            i = (y * w + x) * 4
            out.data[i:i + 4] = bytes(p)
    return out, sha256_file(path)


def bevel_shade(px: float, py: float, cx: float, cy: float, ca: float, cb: float) -> float:
    """The plate's bevel at a pixel centre: a multiplier on the iron. Every edge of the plate (the four straight ones and the arc) lights or darkens a BEVEL_WIDTH px strip by how its outward normal faces the light."""
    x0, y0, x1, y1 = PLATE
    shade = 0.0
    edges = ((px - x0, (-1.0, 0.0)), (x1 - px, (1.0, 0.0)), (py - y0, (0.0, -1.0)), (y1 - py, (0.0, 1.0)))
    dist, nx, ny = signed_edge(px - cx, py - cy, ca, cb)
    edges += ((dist, (-nx, -ny)),)        # the arc: the plate lies outside the ellipse, its edge faces the ellipse's centre
    for d, (ex, ey) in edges:
        if d >= BEVEL_WIDTH:
            continue
        w = clamp(1.0 - max(0.0, d) / BEVEL_WIDTH, 0.0, 1.0)
        facing = ex * LIGHT[0] + ey * LIGHT[1]
        shade += (BEVEL_LIT if facing > 0 else BEVEL_DARK) * facing * w
    return 1.0 + shade


def render(material: dict, iron: pngio.Image) -> pngio.Image:
    """The part: the rim and the plate's corners on one canvas, the rim's shadow baked into the plate."""
    w, h = CANVAS
    cx, cy = CENTRE
    ra, rb = RIM
    ca, cb = CORNER
    x0, y0, x1, y1 = PLATE
    arc = arc_table(ra - RING_WIDTH / 2, rb - RING_WIDTH / 2)
    if len(material["grain"]) < arc[-1]:
        raise KitError("the brass grain is shorter than the rim's circumference: it would repeat with a seam")
    grain, tone, rows, count = material["grain"], material["tone"], BRASS_ROWS, len(material["grain"])
    tone_light = lum(*tone)
    # the pixels of the plate's corners (the rectangle outside the corner ellipse, with a pixel of margin for the arc's anti-aliasing)
    corners = [(x, y) for y in range(y0, y1) for x in range(x0, x1) if ((x + 0.5 - cx) / ca) ** 2 + ((y + 0.5 - cy) / cb) ** 2 > 0.8]
    shadow = shadow_field(range(x0, x1), range(y0, y1)) if corners else {}
    out = pngio.Image.blank(w, h)
    for (x, y) in corners:
        cover = tent_share(x + 0.5, y + 0.5, lambda qx, qy: ((qx - cx) / ca) ** 2 + ((qy - cy) / cb) ** 2 > 1.0)
        if cover <= 0:
            continue
        iron_p = iron.pixel(x - x0, y - y0)
        k = bevel_shade(x + 0.5, y + 0.5, cx, cy, ca, cb) * (1.0 - SHADOW[3] * shadow[(x, y)])
        colour = [clamp(iron_p[c] * IRON_TINT[c] * k, 0.0, 255.0) for c in range(3)]
        i = (y * w + x) * 4
        out.data[i:i + 4] = bytes((int(colour[0] + 0.5), int(colour[1] + 0.5), int(colour[2] + 0.5), int(round(cover * 255))))
    middle_a, middle_b = ra - RING_WIDTH / 2, rb - RING_WIDTH / 2
    for y in range(max(0, int(cy - rb) - 2), min(h, int(cy + rb) + 3)):
        for x in range(max(0, int(cx - ra) - 2), min(w, int(cx + ra) + 3)):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            d, nx, ny = signed_edge(dx, dy, ra, rb)       # px outside the rim's outer edge
            if d > 1.6 or d < -RING_WIDTH - 1.6:
                continue
            th = (math.degrees(math.atan2(ny, nx)) + 360.0) % 360.0
            rho = clamp(1.0 + d / RING_WIDTH, 0.0, 1.0)
            light, copper = light_of(th, rho)
            colour = brass_colour(light, copper, tone)
            t = math.atan2(dy / middle_b, dx / middle_a)
            idx = int(arc_at(arc, t)) % count
            kg = 1 + GRAIN * grain[idx][min(rows - 1, 2 + int(rho * (rows - 4)))] / tone_light
            r, g, b = (clamp(v * kg, 0.0, 255.0) for v in colour)
            alpha = tent_share(x + 0.5, y + 0.5, lambda qx, qy: -RING_WIDTH <= signed_edge(qx - cx, qy - cy, ra, rb)[0] <= 0.0) if (d > -0.8 or d < -RING_WIDTH + 0.8) else 1.0
            if alpha <= 0:
                continue
            i = (y * w + x) * 4
            out.data[i:i + 4] = bytes((int(r + 0.5), int(g + 0.5), int(b + 0.5), int(round(alpha * 255))))
    return out


def gates(image: pngio.Image) -> list[str]:
    """The checks of the part: the grid, the open inside, the opaque rim, the plate only in the corners."""
    failures = []
    w, h = CANVAS
    if (image.w, image.h) != (w, h) or w % 6 or h % 6:
        failures.append(f"the master is {image.w} x {image.h}, not {w} x {h} on the 6 px grid")
        return failures
    cx, cy = CENTRE
    ra, rb = RIM
    inner = [image.pixel(x, y)[3] for y in range(h) for x in range(w) if ((x + 0.5 - cx) / (ra - RING_WIDTH - 2)) ** 2 + ((y + 0.5 - cy) / (rb - RING_WIDTH - 2)) ** 2 < 1.0]
    if any(inner):
        failures.append("the inside of the rim is not transparent (the surface's parchment and glyph show there)")
    ca, cb = CORNER
    gap = [(x, y) for y in range(h) for x in range(w) if ((x + 0.5 - cx) / ra) ** 2 + ((y + 0.5 - cy) / rb) ** 2 > 1.1 and ((x + 0.5 - cx) / ca) ** 2 + ((y + 0.5 - cy) / cb) ** 2 < 0.94]
    if any(image.pixel(x, y)[3] for x, y in gap):
        failures.append("something is drawn between the rim and the plate's corners (the strip of wood there is the surface's)")
    for name, (x, y) in (("top left", (PLATE[0] + 3, PLATE[1] + 3)), ("top right", (PLATE[2] - 4, PLATE[1] + 3)), ("bottom left", (PLATE[0] + 3, PLATE[3] - 4)), ("bottom right", (PLATE[2] - 4, PLATE[3] - 4))):
        if image.pixel(x, y)[3] != 255:
            failures.append(f"the plate's {name} corner is not opaque")
    ring_top = image.pixel(int(cx), int(cy - rb + RING_WIDTH / 2))
    if ring_top[3] != 255 or not (ring_top[0] > ring_top[2] + 30):
        failures.append("the rim is not opaque brass at its top")
    return failures


def provenance_record(masters: Path, material: dict, iron_hash: str, blob: bytes) -> dict:
    return {"part": PART, "rule": "oval_kit", "tool_version": TOOL_VERSION, "date": datetime.date.today().isoformat(),
            "source": "the kit's brass (hardware.menu_ring, hardware.hud_ring) and iron (hardware.menu_plate_left); geometry measured on ctx-inventory-master-2880x2160",
            "geometry": {"canvas": list(CANVAS), "plate": list(PLATE), "centre": list(CENTRE), "rim": list(RIM), "corner_ellipse": list(CORNER), "ring_width": RING_WIDTH},
            "sources_sha256": {**material["sha256"], IRON_SOURCE[0]: iron_hash}, "image_size": list(CANVAS), "image_sha256": hashlib.sha256(blob).hexdigest(), "run_by": "agent"}


def cmd_rule(args: argparse.Namespace) -> int:
    masters = args.local_root / "masters"
    material = brass_material(masters)
    iron, iron_hash = iron_window(masters)
    image = render(material, iron)
    failures = gates(image)
    if failures:
        raise KitError(f"{PART}: " + "; ".join(failures))
    blob = pngio.encode(image)
    print(f"{PART}: {CANVAS[0]}x{CANVAS[1]} {hashlib.sha256(blob).hexdigest()[:12]}")
    if not args.dry_run:
        target = args.out or masters / f"{PART}.png"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(blob)
        if args.out is None:
            (args.local_root / "provenance" / "rule").mkdir(parents=True, exist_ok=True)
            (args.local_root / "provenance" / "rule" / f"{PART}.json").write_text(json.dumps(provenance_record(masters, material, iron_hash, blob), indent=2) + "\n", encoding="utf-8")
    print("OVAL KIT rule PASS" + (" (dry run, nothing written)" if args.dry_run else ""))
    return 0


def cmd_self_test(args: argparse.Namespace) -> int:
    code = subprocess.run([sys.executable, "-B", "-m", "unittest", "tools.steamwoven_ui.test_oval_kit"], cwd=HERE.parents[1]).returncode
    print("OVAL KIT self-test " + ("PASS" if code == 0 else "FAIL"))
    return 0 if code == 0 else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name, fn in (("rule", cmd_rule), ("self-test", cmd_self_test)):
        s = sub.add_parser(name)
        s.set_defaults(fn=fn)
        s.add_argument("--local-root", type=Path)
        s.add_argument("--dry-run", action="store_true")
        s.add_argument("--out", type=Path, help="write the master here (a trial), no record")
    args = parser.parse_args(argv)
    args.local_root = args.local_root or prompt_library.default_local_root()
    try:
        return args.fn(args)
    except (KitError, pngio.PngError) as error:
        print(f"OVAL KIT {args.command} FAIL: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
