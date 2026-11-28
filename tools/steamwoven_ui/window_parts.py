#!/usr/bin/env python3
"""ARC-UI-3474: the fixtures of the inventory family and the schematic window as kit parts, cut out of the player's own HD frames of those windows.

    python3 -B tools/steamwoven_ui/window_parts.py list
    python3 -B tools/steamwoven_ui/window_parts.py build --pack HD_PACK_DIR [--out DIR] [--only ID,...]
    python3 -B tools/steamwoven_ui/window_parts.py self-test

Arts 221, 223, 234, 235 and 365 bake in the pits and their brass rims, the figured frames of the name plate, the paper doll and the weapon and shield slots, the five count signs, the arrange legend, the blueprint page, the assemble
channel and the two gears. The rule-drawn stand-ins of ARC-UI-3098 are flat shapes beside the earlier generative HD version the owner likes, so each part is cut from that HD frame (rule P, pack first; the method of
`sheet_parts.py`, ARC-UI-3321): one opaque layer in the HD picture's own pixels with a soft edge over its measured cast shadow, resampled by area to the master size. What differs is how a part is found:

  - **rect**: a pit or a field with a straight brass rim: the rim's outer rectangle (HD px).
  - **colour**: a figured brass frame (`sheet_parts.classify`: black, brass or bright) or the pale paper of the page, closed, filled and opened; the piece that holds the anchor.
  - **shapes**: a sign on the wood whose dark or rusty pixels are the wood's own colours (the gears, the count signs, the legend): outlines traced round it (HD px), minus the holes that show the wood.

A pixel the pack frame leaves transparent (a place where the original draws a button or a picture over the art) is black, as in the original's art. In a pit the near-black ground is pure black (owner 2026-11-26). The paper doll's
pit keeps its grid lines (the HD frame's, on the original's places) inside the curved frame, so the viewer draws no cell wells over it. The blueprint page keeps its ruling, the two slot boxes and the bracket to them.

No original or derived pixel is in git: this file holds the rule and the places. Standard library only, on `pngio`. Same input gives the same bytes.
"""
from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass, replace
from pathlib import Path

try:
    from . import fixtures, image_archive, label_parts, pngio, sheet_parts
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import fixtures  # type: ignore
    import image_archive  # type: ignore
    import label_parts  # type: ignore
    import pngio  # type: ignore
    import sheet_parts  # type: ignore

HD = label_parts.HD  # HD frame pixels per original unit
READ_MARGIN = sheet_parts.READ_MARGIN
GROUND_LUMA = 14.0  # in a pit, a pixel darker than this is the ground: pure black
PAPER_LUMA = 100.0  # the blueprint page is lighter than this, the wood round it darker

# The HD pack's frames, relative to the pack's `interface` folder: art -> (file, width, height) in HD px.
FRAMES = {
    221: ("inventory/panels/inventor-f000.png", 1768, 1600),
    223: ("inventory/panels/pdoll-f000.png", 1432, 1600),
    234: ("barter-loot/panels/loot-f000.png", 1432, 1600),
    235: ("barter-loot/gauges/barter-f000.png", 1432, 1600),
    365: ("schematics/panels/schematic_base-f000.png", 3200, 1600),
}

Point = tuple[float, float]
Polygon = tuple[Point, ...]


@dataclass(frozen=True)
class WindowPart:
    ident: str
    art: int
    x: int  # the part's box in the art's own units
    y: int
    width: int
    height: int
    offset: int  # the art's x in the window's units (the pack's art 221 stands at 358)
    find: str  # "rect", "brass", "paper" or "shapes"
    rect: tuple[int, int, int, int] = (0, 0, 0, 0)  # "rect": the rim's outer corners (x0, y0, x1, y1) in HD px
    anchor: tuple[int, int] = (0, 0)  # "brass" and "paper": a point inside the part (art units)
    shapes: tuple[Polygon, ...] = ()  # "shapes": the outlines (HD px)
    holes: tuple[Polygon, ...] = ()  # "shapes": the wood seen through the part
    ground: bool = False  # near-black pixels of the part become pure black
    reach: int = sheet_parts.REACH  # HD px the cast shadow is measured and drawn out to (the gears' wide soft shadow reaches further)
    clear: tuple[tuple[int, int, int, int], ...] = ()  # boxes (x0, y0, x1, y1) in HD px of the frame left see-through: a neighbour's pixels the find takes with the part

    @property
    def place(self) -> tuple[int, int]:
        """The part's corner in the window's units (the viewer draws it there)."""
        return self.x + self.offset, self.y


def _squares(*boxes: tuple[int, int, int, int]) -> tuple[Polygon, ...]:
    return tuple(((x0, y0), (x1, y0), (x1, y1), (x0, y1)) for x0, y0, x1, y1 in boxes)


def _traced(origin: tuple[int, int], zoom: int, *points: Point) -> Polygon:
    """An outline traced on a zoomed view of the frame: `origin` is the view's corner in HD px, `zoom` its pixels per HD px."""
    return tuple((origin[0] + x / zoom, origin[1] + y / zoom) for x, y in points)


COUNT_SIGNS = (
    # gold: the left group of stacks and the right group
    _traced((1320, 270), 8, (415, 65), (445, 55), (560, 55), (600, 70), (600, 235), (650, 245), (690, 260), (698, 285), (698, 545), (680, 570), (600, 578), (520, 572), (500, 555), (480, 555), (465, 570), (400, 578), (320, 572), (303, 555), (300, 345), (320, 325), (410, 318), (410, 80)),
    _traced((1320, 270), 8, (780, 195), (810, 178), (920, 175), (955, 190), (955, 320), (1030, 328), (1045, 345), (1045, 550), (1020, 568), (940, 575), (900, 560), (885, 540), (885, 470), (860, 445), (805, 430), (798, 330), (780, 315)),
    # arrows: the quiver, its feathers, the head of one arrow and the other's tail
    _traced((1320, 270), 8, (185, 1225), (205, 1205), (235, 1200), (405, 1105), (410, 1095), (430, 1090), (680, 935), (685, 915), (740, 890), (765, 900), (840, 1040), (835, 1060), (800, 1080), (770, 1085), (500, 1250), (310, 1355), (290, 1370), (250, 1365), (230, 1340), (190, 1250)),
    _traced((1320, 270), 8, (775, 915), (800, 890), (810, 850), (850, 810), (905, 780), (920, 775), (945, 800), (975, 805), (1000, 825), (1055, 840), (1055, 855), (1000, 875), (965, 895), (950, 930), (1010, 925), (1005, 945), (960, 955), (930, 990), (880, 1010), (845, 1005), (825, 1030), (795, 985), (825, 970), (800, 940)),
    _traced((1320, 270), 8, (215, 855), (270, 845), (330, 860), (342, 890), (322, 920), (350, 948), (482, 1038), (482, 1062), (462, 1062), (330, 982), (298, 978), (278, 942), (248, 925), (212, 890)),
    _traced((1320, 270), 8, (680, 1160), (702, 1162), (890, 1255), (960, 1225), (1000, 1228), (1030, 1250), (1038, 1290), (1075, 1300), (1070, 1330), (1030, 1350), (1000, 1372), (960, 1372), (900, 1342), (860, 1312), (855, 1290), (868, 1272), (688, 1184)),
    # bullets: the left group and the right group
    _traced((1320, 450), 8, (270, 600), (278, 590), (278, 360), (290, 345), (295, 260), (320, 215), (335, 200), (360, 195), (385, 215), (400, 260), (405, 345), (425, 365), (428, 520), (445, 520), (480, 495), (500, 478), (500, 285), (515, 260), (520, 165), (545, 125), (570, 125), (600, 160), (620, 230), (628, 290), (636, 300), (636, 500), (642, 515), (605, 545), (545, 565), (500, 590), (470, 625), (420, 664), (370, 674), (340, 656), (330, 635), (290, 628)),
    _traced((1320, 450), 8, (724, 460), (740, 435), (765, 412), (790, 425), (830, 450), (875, 480), (910, 485), (912, 340), (930, 325), (935, 260), (950, 210), (970, 185), (995, 188), (1020, 215), (1035, 260), (1040, 325), (1050, 340), (1050, 575), (1030, 592), (970, 596), (950, 592), (945, 612), (910, 632), (870, 634), (840, 612), (815, 586), (790, 576), (765, 550), (735, 520), (722, 490)),
    # the charge: the can with its two terminals
    ((1385.6, 562.5), (1388.0, 560.5), (1392.5, 560.5), (1395.0, 562.5), (1395.5, 565.5), (1406.0, 565.5), (1407.0, 562.0), (1409.5, 561.5), (1412.0, 562.5), (1412.5, 567.0), (1419.0, 568.5), (1423.0, 572.0), (1423.5, 576.0), (1423.0, 583.0), (1423.5, 590.0), (1424.0, 625.0), (1420.0, 628.5), (1408.0, 630.5), (1395.0, 630.5), (1385.0, 628.5), (1378.5, 624.0), (1377.5, 620.0), (1378.0, 590.0), (1376.0, 578.0), (1376.5, 573.0), (1380.0, 569.0), (1385.5, 566.5)),
    # fuel: the can with its spout, lid and handle
    _traced((1320, 580), 8, (320, 755), (338, 748), (400, 795), (420, 798), (520, 798), (530, 768), (560, 730), (595, 710), (598, 660), (615, 646), (785, 646), (797, 660), (797, 715), (842, 730), (870, 755), (882, 745), (905, 738), (945, 740), (978, 760), (1002, 800), (1007, 880), (997, 942), (962, 992), (920, 1017), (890, 1022), (872, 1040), (870, 1112), (842, 1152), (790, 1182), (680, 1197), (580, 1192), (508, 1172), (473, 1135), (463, 1070), (443, 1020), (403, 940), (383, 870), (390, 830), (368, 808), (318, 775)),
)
COUNT_HOLES = (
    _traced((1320, 580), 8, (885, 778), (920, 765), (955, 782), (973, 820), (978, 880), (958, 932), (925, 972), (888, 990), (878, 955), (878, 805)),
)

LEGEND_SHAPES = _squares(
    (1361, 790, 1383, 811), (1417, 785, 1440, 807), (1397, 806, 1419, 827), (1342, 825, 1362, 846), (1378, 822, 1397, 841), (1417, 826, 1438, 846), (1379, 847, 1397, 865), (1357, 865, 1377, 887), (1398, 866, 1417, 887),
    (1526, 810, 1586, 870),
) + (_traced((1324, 772), 8, (1040, 433), (1290, 428), (1290, 345), (1315, 328), (1475, 468), (1532, 560), (1527, 582), (1318, 802), (1288, 818), (1280, 690), (1040, 688)),)

GEAR_HAT = _traced((290, 560), 4, (682, 106), (695, 96), (770, 96), (780, 110), (780, 150), (794, 214), (806, 230), (855, 242), (868, 238), (924, 148), (940, 142), (996, 185), (1002, 200), (987, 290), (980, 314), (1016, 345), (1030, 348), (1130, 312), (1148, 318), (1182, 370), (1182, 386), (1152, 420), (1128, 455), (1138, 470), (1182, 488), (1232, 508), (1282, 518), (1338, 534), (1342, 546), (1322, 560), (1262, 568), (1245, 578), (1242, 590), (1240, 650), (1227, 662), (1180, 672), (1122, 682), (1102, 692), (1102, 718), (1095, 730), (1127, 745), (1182, 810), (1184, 826), (1152, 876), (1136, 892), (1080, 872), (1042, 862), (1027, 906), (1022, 960), (1032, 990), (1027, 1012), (1017, 1110), (990, 1112), (968, 1088), (958, 1058), (940, 1062), (924, 1052), (868, 990), (854, 974), (802, 980), (792, 1006), (782, 1050), (777, 1102), (765, 1110), (684, 1110), (670, 1096), (666, 1040), (653, 996), (634, 977), (606, 975), (566, 1012), (536, 1042), (518, 1044), (454, 1006), (446, 990), (460, 948), (478, 894), (468, 882), (440, 868), (400, 874), (318, 897), (303, 886), (283, 830), (283, 804), (340, 743), (360, 733), (390, 720), (360, 702), (338, 682), (258, 672), (214, 662), (208, 625), (178, 590), (123, 552), (93, 541), (77, 528), (83, 516), (160, 508), (270, 488), (360, 476), (364, 462), (328, 440), (288, 400), (280, 385), (283, 368), (314, 318), (330, 309), (400, 334), (434, 349), (449, 344), (484, 314), (474, 290), (443, 200), (443, 184), (500, 147), (519, 145), (581, 224), (591, 238), (640, 234), (664, 227), (678, 214), (680, 150))

GEAR_SCROLL = _traced((280, 900), 4, (726, 148), (735, 138), (806, 138), (817, 150), (826, 228), (834, 264), (860, 278), (895, 284), (974, 198), (990, 192), (1042, 229), (1048, 244), (1032, 330), (1027, 354), (1060, 388), (1080, 398), (1180, 359), (1197, 366), (1234, 428), (1234, 446), (1201, 482), (1171, 506), (1162, 525), (1250, 512), (1400, 485), (1437, 487), (1482, 538), (1498, 578), (1498, 632), (1482, 662), (1450, 696), (1400, 708), (1200, 743), (1150, 754), (1150, 790), (1162, 808), (1237, 864), (1237, 882), (1196, 947), (1180, 951), (1100, 922), (1085, 915), (1050, 947), (1042, 962), (1057, 1040), (1052, 1087), (1000, 1117), (974, 1117), (943, 1082), (913, 1047), (874, 1042), (842, 1052), (830, 1080), (827, 1166), (811, 1182), (730, 1182), (720, 1166), (713, 1080), (703, 1052), (660, 1042), (640, 1033), (571, 1117), (554, 1120), (488, 1092), (480, 1070), (503, 998), (513, 974), (484, 946), (449, 933), (420, 938), (340, 963), (328, 956), (323, 934), (300, 944), (200, 968), (150, 984), (118, 978), (87, 951), (66, 900), (56, 842), (61, 780), (77, 737), (100, 721), (160, 714), (240, 697), (247, 687), (247, 620), (260, 605), (330, 595), (388, 587), (398, 565), (398, 535), (388, 520), (318, 470), (303, 440), (308, 424), (339, 367), (355, 359), (431, 378), (469, 398), (493, 384), (519, 354), (523, 344), (488, 270), (483, 233), (495, 226), (550, 189), (560, 192), (601, 239), (631, 284), (641, 298), (660, 293), (698, 277), (713, 255), (718, 230))

PARTS: tuple[WindowPart, ...] = (
    WindowPart("fixture.pack_pit", 221, 5, 3, 332, 396, 358, "rect", rect=(31, 22, 1329, 1577), ground=True),
    WindowPart("fixture.count_pit", 221, 367, 70, 64, 20, 358, "rect", rect=(1476, 288, 1717, 348)),
    WindowPart("fixture.count_icons", 221, 332, 67, 38, 118, 358, "shapes", shapes=COUNT_SIGNS, holes=COUNT_HOLES),
    WindowPart("fixture.arrange_legend", 221, 333, 193, 68, 34, 358, "shapes", shapes=LEGEND_SHAPES),
    WindowPart("fixture.name_pit", 223, 113, 21, 245, 66, 0, "brass", anchor=(230, 50)),
    WindowPart("fixture.doll_pit", 223, 8, 101, 350, 299, 0, "brass", anchor=(180, 200), ground=True, clear=((165, 404, 465, 415),)),  # the foot of the round view's ring over the top rim
    WindowPart("fixture.weapon_frame", 223, 21, 311, 101, 89, 0, "brass", anchor=(50, 370)),
    WindowPart("fixture.shield_frame", 223, 246, 311, 101, 89, 0, "brass", anchor=(310, 370)),
    WindowPart("fixture.holder_pit_loot", 234, 2, 130, 354, 270, 0, "rect", rect=(19, 532, 1403, 1586), ground=True),
    WindowPart("fixture.holder_pit_barter", 235, 3, 163, 353, 236, 0, "rect", rect=(24, 663, 1396, 1579), ground=True),
    WindowPart("fixture.quote_field", 235, 79, 122, 272, 43, 0, "rect", rect=(327, 499, 1391, 645), ground=True),
    WindowPart("fixture.schem_page", 365, 193, 6, 533, 381, 0, "paper", anchor=(230, 40)),
    WindowPart("fixture.schem_channel", 365, 33, 315, 137, 70, 0, "brass", anchor=(75, 350)),
    WindowPart("fixture.gear_hat", 365, 71, 141, 94, 80, 0, "shapes", shapes=(GEAR_HAT,), reach=40),
    WindowPart("fixture.gear_scroll", 365, 65, 228, 109, 82, 0, "shapes", shapes=(GEAR_SCROLL,), reach=40),
)
BY_ID = {part.ident: part for part in PARTS}
IDENTS = tuple(BY_ID)


# ---------------------------------------------------------------- finding a part

def raster(width: int, height: int, shapes: list[Polygon], holes: list[Polygon]) -> list[list[bool]]:
    """The pixels whose centre lies inside any of the outlines and in none of the holes (px of the read box; even-odd crossing per outline)."""
    out = [[False] * width for _ in range(height)]
    for value, polygons in ((True, shapes), (False, holes)):
        for polygon in polygons:
            for y in range(height):
                centre = y + 0.5
                crossings = sorted(ax + (centre - ay) / (by - ay) * (bx - ax) for (ax, ay), (bx, by) in zip(polygon, polygon[1:] + polygon[:1]) if (ay <= centre) != (by <= centre))
                for left, right in zip(crossings[::2], crossings[1::2]):
                    for x in range(max(int(left + 0.5), 0), min(int(right + 0.5), width)):
                        out[y][x] = value
    return out


def find(part: WindowPart, rgb: list[list[list[float]]], origin: tuple[int, int]) -> tuple[list[list[int]], int]:
    """The labelled pieces of the read box (whose corner is `origin` in HD px) and the number of the part's own."""
    height, width = len(rgb[0]), len(rgb[0][0])
    ox, oy = origin
    if part.find in ("brass", "paper"):
        anchor = (part.anchor[0] * HD - ox, part.anchor[1] * HD - oy)
        if part.find == "brass":
            label, target = sheet_parts.find_part(rgb, anchor)
        else:
            paper = [[label_parts.luma(rgb[0][y][x], rgb[1][y][x], rgb[2][y][x]) > PAPER_LUMA for x in range(width)] for y in range(height)]
            paper = sheet_parts.erode(label_parts.dilate(paper, 3), 3)
            paper = sheet_parts.fill_holes(paper)
            paper = label_parts.dilate(sheet_parts.erode(paper, 3), 3)
            label, sizes = sheet_parts.pieces(paper, sheet_parts.MIN_PIECE)
            if not sizes or not label[anchor[1]][anchor[0]]:
                raise ValueError(f"{part.ident}: no paper at the anchor")
            target = label[anchor[1]][anchor[0]]
        return sheet_parts.smooth_pieces(label), target
    if part.find == "rect":
        x0, y0, x1, y1 = part.rect
        shapes, holes = list(_squares((x0, y0, x1, y1))), []
    else:
        shapes, holes = list(part.shapes), list(part.holes)
    moved = [[tuple((x - ox, y - oy) for x, y in polygon) for polygon in group] for group in (shapes, holes)]
    mask = raster(width, height, moved[0], moved[1])
    if not any(any(row) for row in mask):
        raise ValueError(f"{part.ident}: the outline is empty in the read box")
    return [[1 if v else 0 for v in row] for row in mask], 1


# ---------------------------------------------------------------- parts

def flatten(frame: pngio.Image) -> pngio.Image:
    """The frame with every pixel it leaves see-through composed over black (the original's art is black where the viewer draws a button or a picture over it)."""
    data = bytearray(frame.data)
    for i in range(3, len(data), 4):
        a = data[i]
        if a < 255:
            for c in range(3):
                data[i - 3 + c] = (data[i - 3 + c] * a + 127) // 255
            data[i] = 255
    return pngio.Image(frame.w, frame.h, data)


def build_part(frame: pngio.Image, part: WindowPart) -> pngio.Image:
    """The master of one part from the flattened frame of its art: the read box found and cut, the part's box of it premultiplied and reduced to the part's master size, straight again."""
    margin = READ_MARGIN + (part.reach - sheet_parts.REACH) // HD  # the unshadowed ring lies beyond the reach
    x0, y0 = max(part.x - margin, 0), max(part.y - margin, 0)
    x1, y1 = min(part.x + part.width + margin, frame.w // HD), min(part.y + part.height + margin, frame.h // HD)
    read = label_parts.read_box(frame, (x0, y0, x1, y1))
    px_w, px_h = part.width * HD, part.height * HD
    ox, oy = (part.x - x0) * HD, (part.y - y0) * HD
    label, target = find(part, read, (x0 * HD, y0 * HD))
    solid = sheet_parts.cut_labelled(read, label, target, (ox, oy, ox + px_w, oy + px_h), part.reach)
    planes = [[[0.0] * px_w for _ in range(px_h)] for _ in range(4)]
    for y in range(max(-oy, 0), min(px_h, solid.height - oy)):
        for x in range(max(-ox, 0), min(px_w, solid.width - ox)):
            sx, sy = ox + x, oy + y
            a = solid.alpha[sy][sx]
            if a <= 0.0 or any(cx0 <= x0 * HD + sx < cx1 and cy0 <= y0 * HD + sy < cy1 for cx0, cy0, cx1, cy1 in part.clear):
                continue
            colour = [solid.rgb[c][sy][sx] for c in range(3)]
            if part.ground and solid.core[sy][sx] and label_parts.luma(*(read[c][sy][sx] for c in range(3))) < GROUND_LUMA:
                colour = [0.0, 0.0, 0.0]
            for c in range(3):
                planes[c][y][x] = colour[c] * a
            planes[3][y][x] = a * 255.0
    master_w, master_h = fixtures.snap(part.width), fixtures.snap(part.height)
    return sheet_parts.straight(label_parts.area_resample(planes, master_w, master_h), master_w, master_h)


def read_frame(pack: Path, art: int) -> pngio.Image:
    name, width, height = FRAMES[art]
    path = pack / "interface" / name
    frame = pngio.read(path)
    if (frame.w, frame.h) != (width, height):
        raise ValueError(f"the HD frame of art {art} must be {width} x {height} ({HD}x), got {frame.w} x {frame.h}: {path}")
    return flatten(frame)


def build(pack: Path, out: Path, only: set[str] | None = None) -> list[str]:
    out.mkdir(parents=True, exist_ok=True)
    frames: dict[int, pngio.Image] = {}
    written = []
    for part in PARTS:
        if only and part.ident not in only:
            continue
        if part.art not in frames:
            frames[part.art] = read_frame(pack, part.art)
        pngio.write(out / f"{part.ident}.png", build_part(frames[part.art], part))
        written.append(part.ident)
    return written


# ---------------------------------------------------------------- self-test on a synthetic frame (no game pixel)

def self_test() -> int:
    problems = []
    for part in PARTS:
        _, width, height = FRAMES[part.art]
        if part.x < 0 or part.y < 0 or part.x + part.width > width // HD or part.y + part.height > height // HD:
            problems.append(f"{part.ident}: the part's box leaves its art")
        if part.ident not in fixtures.BY_ID or fixtures.BY_ID[part.ident].master != (fixtures.snap(part.width), fixtures.snap(part.height)):
            problems.append(f"{part.ident}: not a fixture of the kit or its size differs from the fixture's")
        box = (part.x * HD, part.y * HD, (part.x + part.width) * HD, (part.y + part.height) * HD)
        points = [(part.rect[0], part.rect[1]), (part.rect[2], part.rect[3])] if part.find == "rect" else [p for polygon in part.shapes for p in polygon] if part.find == "shapes" else [(part.anchor[0] * HD, part.anchor[1] * HD)]
        if not points or any(not (box[0] <= x <= box[2] and box[1] <= y <= box[3]) for x, y in points):
            problems.append(f"{part.ident}: its outline or anchor leaves its box")
    # a synthetic pit: brass rim, near-black ground, a see-through window, a cast shadow down and right, on grainy wood
    picture, _ = sheet_parts._synthetic(200, 160, (-500, -500), 1)  # bare wood
    frame = pngio.Image.blank(200, 160)
    for y in range(160):
        for x in range(200):
            colour = [picture[c][y][x] for c in range(3)]
            if 40 <= x < 140 and 40 <= y < 110:
                colour = [8.0, 8.0, 8.0] if 46 <= x < 134 and 46 <= y < 104 else [170.0, 125.0, 55.0]
            elif 46 <= x < 148 and 46 <= y < 118:
                colour = [v * 0.5 for v in colour]
            alpha = 0 if 80 <= x < 90 and 60 <= y < 70 else 255
            frame.data[(y * 200 + x) * 4:(y * 200 + x) * 4 + 4] = bytes([int(min(max(v, 0), 255)) for v in colour] + [alpha])
    flat = flatten(frame)
    part = WindowPart("synthetic", 0, 6, 6, 36, 28, 0, "rect", rect=(40, 40, 140, 110), ground=True)
    read = label_parts.read_box(flat, (0, 0, 50, 40))
    label, target = find(part, read, (0, 0))
    solid = sheet_parts.cut_labelled(read, label, target)
    if not solid.core[75][90] or solid.core[20][20] or solid.alpha[75][90] < 0.999:
        problems.append("the rect part is not found where it is, or is found on the wood")
    if solid.alpha[113][100] < 0.2 or solid.alpha[30][100] > 0.15:
        problems.append("the pit's shadow is not found below it, or one is found above it")
    if [read[c][65][85] for c in range(3)] != [0.0, 0.0, 0.0]:
        problems.append("a see-through pixel is not black")
    scale = fixtures.snap(part.width) / (part.width * HD)
    master = build_part(flat, part)
    mx, my = int((70 - part.x * HD) * scale), int((90 - part.y * HD) * scale)
    if tuple(master.data[(my * master.w + mx) * 4:(my * master.w + mx) * 4 + 4]) != (0, 0, 0, 255):
        problems.append("the pit's ground is not pure black in the master")
    if build_part(flat, part).data != master.data:
        problems.append("the cut is not deterministic")
    cleared = build_part(flat, replace(part, clear=((60, 80, 80, 100),)))
    if cleared.data[(my * master.w + mx) * 4 + 3] != 0 or cleared.data == master.data:
        problems.append("a clear box is not left see-through")
    mask = raster(20, 20, [((2.0, 2.0), (18.0, 2.0), (18.0, 18.0), (2.0, 18.0))], [((6.0, 6.0), (14.0, 6.0), (14.0, 14.0), (6.0, 14.0))])
    if not mask[3][3] or mask[10][10] or mask[0][0] or sum(map(sum, mask)) != 256 - 64:
        problems.append("the outline raster is wrong")
    for problem in problems:
        print("WINDOW PARTS FAIL:", problem)
    if problems:
        return 1
    print(f"WINDOW PARTS PASS: {len(PARTS)} parts, boxes inside their arts and fixture sizes, outline raster, see-through black, pit shadow and black ground, clear box, deterministic")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("list")
    build_parser = sub.add_parser("build")
    build_parser.add_argument("--pack", type=Path, required=True, help="the installed HD pack's folder (the one that holds `interface`)")
    build_parser.add_argument("--out", type=Path, default=None, help="default: local/steamwoven-ui/masters of the main checkout")
    build_parser.add_argument("--only", default="")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    if args.command == "list":
        for part in PARTS:
            print(f"{part.ident}  art {part.art} at {part.x}, {part.y} (window {part.place[0]}, {part.place[1]})  {part.width} x {part.height} units  {part.find}  master {fixtures.snap(part.width)} x {fixtures.snap(part.height)}")
        return 0
    if args.command == "self-test":
        return self_test()
    only = {item for item in args.only.split(",") if item}
    unknown = only - set(IDENTS)
    if unknown:
        print("unknown part:", ", ".join(sorted(unknown)), file=sys.stderr)
        return 2
    out = args.out or fixtures.default_out()
    image_archive.warn_if_ephemeral(out, "window_parts.py build")
    try:
        done = build(args.pack, out, only or None)
    except (ValueError, pngio.PngError, OSError) as error:
        print("WINDOW PARTS FAIL:", error, file=sys.stderr)
        return 2
    print("WINDOW PARTS wrote", ", ".join(done))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
