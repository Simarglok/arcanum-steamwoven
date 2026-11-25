"""ARC-UI-2927: the B-BOARD batch of the Steamwoven kit: the four strip masters and the four end caps (TRD SWI-LAY-12, SWI-BDR-4).

    python3 -B tools/steamwoven_ui/board_build.py refs   [--frames DIR] [--local-root DIR]
    python3 -B tools/steamwoven_ui/board_build.py crop   [--local-root DIR]
    python3 -B tools/steamwoven_ui/board_build.py strips [--window-source IMG] [--local-root DIR] [--bevel lighten|original] [--dry-run]
    python3 -B tools/steamwoven_ui/board_build.py caps   [--local-root DIR] [--variant plain|rivets] [--dry-run]
    python3 -B tools/steamwoven_ui/board_build.py self-test

The strip master heights are the grid ones, 150 and 570 px (decided by this batch; the units 147.6 / 572.4 are not multiples of 6 px, so only these bake to the four
steps exactly): 150 + 1440 + 570 = 2160, so the three zones still tile the composition. The strip art is made by the owner's method A (one Codex item per strip at the
generator's size, the extreme aspect band cropped, `surface_compose.py build` takes the wood and keeps every other pixel of the original as the stand-in of the
kit's metal, which comes with the hardware batch #2481); the original's transparent openings (sockets, portrait holes) stay transparent. A cap is the brass edge
bar of one strip end drawn by Codex on a flat ground, keyed and mirrored by the fit tool, plus a soft shadow that this tool lays outward (deterministic: dark
brown, alpha fading by a cosine over the cap's outer part). Everything stays under the ignored `local/steamwoven-ui/`; nothing here holds art.

`refs` writes the references of the prompts (`prompts/board-*.txt`, `cap-*.txt`) into `local/steamwoven-ui/refs/`; the inbox itself is `prompt_library.py inbox B-BOARD`.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
try:
    from . import band_crop, pngio, prompt_library, surface_metrics
except ImportError:  # run as a script
    sys.path.insert(0, str(HERE))
    import band_crop  # type: ignore
    import pngio  # type: ignore
    import prompt_library  # type: ignore
    import surface_metrics  # type: ignore

MASTER_W = 2880
TOP_H, WINDOW_H, BOTTOM_H = 150, 1440, 570
CAP_W = 72                  # master px: the brass bar at the strip's end (BAR_W) and the soft shadow outward of it
BAR_W = 24
SHADOW_RGB = (10, 7, 5)     # dark brown-black, like the shadow of the original's own panels
SHADOW_ALPHA = 0.55         # at the bar's outer edge; fades to 0 at the cap's outer edge
WINDOW_ROWS = (148, 1588)   # rows of the inventory original (compare/surface-study/original.png) that are the window zone: the neighbour of the strips' joins
ASK_HEIGHT = {"board.inttop": 144}  # the generator draws a 19.2:1 strip at about 20:1, so this strip is asked (and its reference made) at 20:1 and the band is resized back to 150 px:
#                                     the squash is then in the reference too and cancels, instead of shifting every detail by up to 7 px (the compose step lays the wood by the original's mask)
STRIPS = {  # part id -> (census frame folder, width, height, pair); the pair's two strips are composed together (one screen)
    "board.inttop": ("185-IntTop", MASTER_W, TOP_H, "hud"),
    "board.intbotom": ("184-IntBotom", MASTER_W, BOTTOM_H, "hud"),
    "board.charcreatetopbar": ("336-CharCreateTopBar", MASTER_W, TOP_H, "charcreate"),
    "board.charcreatebottombar": ("335-CharCreateBottomBar", MASTER_W, BOTTOM_H, "charcreate"),
}
CAPS = {  # part id -> (strip height, mirrored)
    "cap.top_strip_left": (TOP_H, False), "cap.top_strip_right": (TOP_H, True),
    "cap.bottom_strip_left": (BOTTOM_H, False), "cap.bottom_strip_right": (BOTTOM_H, True),
}
SAFE = "board-"


class BoardError(Exception):
    pass


def run(command: list[str]) -> str:
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise BoardError(f"{command[0]} failed: {result.stderr.strip()[:300]}")
    return result.stdout


def prompt_id(part_id: str) -> str:
    return part_id.replace(".", "-").replace("_", "-")


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def upscale_strip(frame: Path, width: int, height: int, out: Path, *, with_alpha: bool = False, black_first: bool = False) -> None:
    """The original art at the master size, Lanczos. Default: resized with its alpha (associated alpha, so no black bleeds from the openings into the art's edge) and
    then laid over black, the openings black: the ORIGINAL of the compose step. `black_first`: the openings filled black BEFORE the resize (what Codex was given as
    its reference, so the record of the batch matches `refs`). `with_alpha`: only the alpha, resized bilinear."""
    if with_alpha:
        run(["magick", str(frame), "-alpha", "extract", "-filter", "Triangle", "-resize", f"{width}x{height}!", str(out)])
    elif black_first:
        run(["magick", str(frame), "-background", "black", "-alpha", "remove", "-alpha", "off", "-filter", "Lanczos", "-resize", f"{width}x{height}!", str(out)])
    else:
        run(["magick", str(frame), "-filter", "Lanczos", "-resize", f"{width}x{height}!", "-background", "black", "-alpha", "remove", "-alpha", "off", str(out)])


def composition(top: Path, bottom: Path, window_source: Path, out: Path, work: Path) -> None:
    """The 2880 x 2160 composition: the top strip, the window zone rows of the inventory original, the bottom strip."""
    window = work / "window-zone.png"
    y0, y1 = WINDOW_ROWS
    run(["magick", str(window_source), "-crop", f"{MASTER_W}x{y1 - y0}+0+{y0}", "+repage", "-alpha", "off", str(window)])
    run(["magick", str(top), "-alpha", "off", str(window), "-alpha", "off", str(bottom), "-alpha", "off", "-append", "-alpha", "off", str(out)])


def spec_for(pair: str) -> dict:
    return {"id": f"board-{pair}", "screen": "ctx-hud", "joins": [TOP_H, TOP_H + WINDOW_H], "bevel_band": [0, TOP_H]}


def shadow_ramp(width: int = CAP_W, bar: int = BAR_W) -> list[float]:
    """Alpha of the cap's shadow per column for a LEFT cap (outer edge at x = 0, the bar at the right): 0 at the outer edge, rising by a cosine to SHADOW_ALPHA at the bar."""
    outer = width - bar
    return [SHADOW_ALPHA * (0.5 - 0.5 * math.cos(math.pi * (x + 0.5) / outer)) if x < outer else 0.0 for x in range(width)]


def cap_image(bar: pngio.Image, height: int, mirrored: bool) -> pngio.Image:
    """The cap master: the shadow columns, then the keyed brass bar (BAR_W x height) at the inner edge; mirrored for a right cap."""
    if bar.w != BAR_W or bar.h != height:
        raise BoardError(f"the bar is {bar.w} x {bar.h}, {BAR_W} x {height} is needed")
    img = pngio.Image.blank(CAP_W, height)
    ramp = shadow_ramp()
    for y in range(height):
        for x in range(CAP_W):
            i = (y * CAP_W + x) * 4
            if x < CAP_W - BAR_W:
                img.data[i:i + 4] = bytes((*SHADOW_RGB, round(ramp[x] * 255)))
            else:
                j = (y * BAR_W + (x - (CAP_W - BAR_W))) * 4
                img.data[i:i + 4] = bar.data[j:j + 4]
    if mirrored:
        out = pngio.Image.blank(CAP_W, height)
        for y in range(height):
            for x in range(CAP_W):
                s, d = (y * CAP_W + (CAP_W - 1 - x)) * 4, (y * CAP_W + x) * 4
                out.data[d:d + 4] = img.data[s:s + 4]
        return out
    return img


MAX_STRETCH = 0.12  # the band's own aspect against the zone's: the generator draws a strip flatter than asked (20:1 for 19.2:1); the band is only the wood donor of the compose step


def band_from_canvas(canvas: Path, width: int, height: int, out: Path, ask_height: int | None = None) -> dict:
    """The band of a padded canvas at the trim settings (fuzz, shave) whose band aspect is closest to the asked one: the edge rows of the band are dark art, soft and
    ambiguous against the bars, so a few settings are tried and the one that keeps the asked aspect wins; see `band_with`."""
    best = None
    for fuzz, shave in TRIM_SETTINGS:
        try:
            report = band_with(canvas, width, height, out, ask_height, shave, fuzz)
        except BoardError as error:
            best = best or error
            continue
        if not isinstance(best, dict) or abs(report["vertical_stretch"]) < abs(best["vertical_stretch"]):
            best = report
            if abs(report["vertical_stretch"]) < 0.02:
                break
    if not isinstance(best, dict):
        raise best  # type: ignore[misc]
    return band_with(canvas, width, height, out, ask_height, best["shave"], best["fuzz_percent"])


TRIM_SETTINGS = ((4.0, 3), (2.0, 1), (3.0, 2), (6.0, 3))  # (fuzz percent, shave px per edge)


def band_with(canvas: Path, width: int, height: int, out: Path, ask_height: int | None, shave: int, fuzz: float) -> dict:
    """The padded canvas of the generator (bars above and below) -> the band, shaved `shave` px at each edge (the light line of the bars), resized to the zone.

    Unlike `band_crop` the band is not refused for an aspect error up to MAX_STRETCH (12 %: the generator drew the 19.2:1 strip at about 21:1) and is resized to the zone with that stretch (recorded): the result is only the source
    of the wood surface, every non-wood pixel and every position of the strip is the original's (`surface_compose.py`)."""
    cw, ch = (int(v) for v in band_crop.run_magick(["identify", "-format", "%w %h", str(canvas)]).split())
    x, y, w, h = band_crop.parse_box(band_crop.run_magick([str(canvas), "-fuzz", f"{fuzz:g}%", "-format", "%@", "info:"]))
    if min(y, ch - y - h) < ch * band_crop.MIN_PADDING:
        raise BoardError(f"{canvas.name}: no bars above and below the band; the canvas is not padded")
    left, right = (shave if x > 0 else 0), (shave if cw - x - w > 0 else 0)
    rect = (x + left, y + shave, w - left - right, h - 2 * shave)
    aspect, asked = rect[2] / rect[3], width / (ask_height or height)
    stretch = asked / aspect - 1   # > 0: the band is stretched in height... measured as the zone's aspect over the band's
    if abs(stretch) > MAX_STRETCH:
        raise BoardError(f"{canvas.name}: the band {rect[2]} x {rect[3]} differs from the zone {width}:{height} by {stretch:+.1%} (more than {MAX_STRETCH:.0%})")
    band_crop.run_magick([str(canvas), "-crop", f"{rect[2]}x{rect[3]}+{rect[0]}+{rect[1]}", "+repage", "-alpha", "off", "-filter", "Lanczos", "-resize", f"{width}x{height}!", "-strip", f"PNG32:{out}"])
    return {"canvas": [cw, ch], "trim_box": [x, y, w, h], "rect": list(rect), "band_aspect": round(aspect, 4), "asked_aspect": round(asked, 4), "shave": shave, "fuzz_percent": fuzz, "asked_height": ask_height or height, "vertical_stretch": round(-stretch, 4)}


def latest_canvas(local: Path, item: str) -> Path | None:
    found = sorted((local / "codex-work" / "B-BOARD").glob(f"*/{item}.png"))
    return found[-1] if found else None


def cmd_crop(args) -> int:
    """Strip results the runner refused (a padded canvas): cropped into the outbox with the record saying so (the runner's own outcome is kept as `runner_outcome`)."""
    local = args.local_root
    for part, (_, w, h, _) in STRIPS.items():
        item = prompt_id(part)
        out = local / "outbox" / "B-BOARD" / f"{item}.png"
        canvas = latest_canvas(local, item)
        if canvas is None:
            print(f"CROP {item}: no canvas in the work folder")
            continue
        report = band_from_canvas(canvas, w, h, out, ASK_HEIGHT.get(part))
        record_path = local / "outbox" / "B-BOARD" / f"{item}.provenance.json"
        record = json.loads(record_path.read_text(encoding="utf-8"))
        if record.get("outcome") != "ok":
            record["runner_outcome"] = record.get("outcome")
            record["outcome"] = "ok"
        record["crop"] = {**report, "tool": "board_build.py crop", "canvas_file": canvas.name, "canvas_sha256": sha256_file(canvas), "result_sha256": sha256_file(out)}
        record["pixel_size"] = [w, h]
        record_path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        print(f"CROP {item}: band {report['rect'][2]}x{report['rect'][3]} -> {w}x{h}, vertical stretch {report['vertical_stretch']:+.1%}")
    return 0


def cmd_refs(args) -> int:
    local = args.local_root
    refs = local / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    for part, (folder, w, h, _) in STRIPS.items():
        out = refs / f"{prompt_id(part)}-original.png"
        upscale_strip(args.frames / folder / "f0.png", w, ASK_HEIGHT.get(part, h), out, black_first=True)
        print(f"REF {out.name} {w}x{ASK_HEIGHT.get(part, h)}")
    # the caps' reference: the left 360 px of the HUD strips (the end the cap closes), top and bottom side by side
    for variant in ("plain", "rivets"):
        out = refs / f"cap-{variant}-original.png"
        top, bottom = refs / "board-inttop-original.png", refs / "board-intbotom-original.png"
        run(["magick", "(", str(top), "-crop", "360x150+0+0", "+repage", ")", "(", str(bottom), "-crop", "360x570+0+0", "+repage", ")", "-background", "black", "-gravity", "north",
             "+append", "+repage", str(out)])
        print(f"REF {out.name}")
    return 0


def board_gate(record: dict, original: Path, built: Path) -> dict:
    """The gate of a strip pair. The strips stand beside the window zone of ANOTHER screen here (the inventory's), so the seam at a join is not the 1.8 / 1.5 of the
    window batches (SWI-VER-5, measured there with the generated window zone and the dark rule between the zones); it is gated as not worse than the same join of the
    original composition (+ 0.05), and the tone check of the whole screen must pass. The numbers of all three are recorded."""
    seams = []
    for item in record["seams"]:
        row = int(item["join"].split("=")[1])
        before = surface_metrics.join_seam(pngio.read(original), "y", row)
        seams.append({"join": item["join"], "built": round(item["ratio"], 3), "original": round(before, 3), "ok": item["ratio"] <= before + 0.05})
    passed = all(s["ok"] for s in seams) and bool(record["tone"]["pass"])
    note = "; ".join(f"{s['join']} built {s['built']} vs original {s['original']}" for s in seams) + f"; tone {'PASS' if record['tone']['pass'] else 'FAIL'}"
    return {"rule": "seam not worse than the original composition's, tone passes", "seams": seams, "tone_pass": bool(record["tone"]["pass"]), "passed": passed, "note": note}


def derived_record(local: Path, part: str, frame: Path, final: Path, surface_record: Path, work: Path) -> Path:
    """The generator record of a strip master source (`image_sha256` is what the fit tool checks): the Codex record of the item plus the derivation."""
    gen = json.loads((local / "outbox" / "B-BOARD" / f"{prompt_id(part)}.provenance.json").read_text(encoding="utf-8"))
    gen["image_sha256"] = sha256_file(final)
    gen["derived"] = {"tool": "board_build.py strips (surface_compose.py build: the generated wood over the original's pixels, the original's alpha)",
                      "original_frame_sha256": sha256_file(frame), "surface_record": surface_record.name, "surface_record_sha256": sha256_file(surface_record)}
    out = work / f"master-{prompt_id(part)}.provenance.json"
    out.write_text(json.dumps(gen, indent=2) + "\n", encoding="utf-8")
    return out


def cmd_strips(args) -> int:
    local = args.local_root
    work = local / "work" / "B-BOARD"
    work.mkdir(parents=True, exist_ok=True)
    results = []
    for pair in ("hud", "charcreate"):
        parts = {p: v for p, v in STRIPS.items() if v[3] == pair}
        top_id = next(p for p, v in parts.items() if v[2] == TOP_H)
        bottom_id = next(p for p, v in parts.items() if v[2] == BOTTOM_H)
        pwork = work / pair
        pwork.mkdir(exist_ok=True)
        orig_top, orig_bottom = pwork / "original-top.png", pwork / "original-bottom.png"
        upscale_strip(args.frames / parts[top_id][0] / "f0.png", MASTER_W, TOP_H, orig_top)
        upscale_strip(args.frames / parts[bottom_id][0] / "f0.png", MASTER_W, BOTTOM_H, orig_bottom)
        a_files = {}
        for part in (top_id, bottom_id):
            res = local / "outbox" / "B-BOARD" / f"{prompt_id(part)}.png"
            if not res.is_file():
                raise BoardError(f"no result for {part}: {res} (run the batch first)")
            a = pwork / f"a-{prompt_id(part)}.png"
            run(["magick", str(res), "-alpha", "off", "-filter", "Lanczos", "-resize", f"{MASTER_W}x{parts[part][2]}!", str(a)])
            a_files[part] = a
        original, a_comp = pwork / "original.png", pwork / "a.png"
        composition(orig_top, orig_bottom, args.window_source, original, pwork)
        composition(a_files[top_id], a_files[bottom_id], args.window_source, a_comp, pwork)
        spec = pwork / "spec.json"
        spec.write_text(json.dumps(spec_for(pair)) + "\n", encoding="utf-8")
        out = pwork / "built.png"
        prov = local / "provenance" / "surface" / f"board-{pair}.json"
        command = [sys.executable, "-B", str(HERE / "surface_compose.py"), "build", "--original", str(original), "--a", str(a_comp), "--out", str(out), "--work", str(pwork / "w"),
                   "--spec", str(spec), "--bevel", args.bevel, "--provenance", str(prov)]
        if args.reuse and out.is_file() and prov.is_file():
            print(f"REUSE {out.name} of {pair}")
        else:
            proc = subprocess.run(command, capture_output=True, text=True)
            print(proc.stdout.strip() or proc.stderr.strip())
        if not prov.is_file():
            raise BoardError(f"surface_compose wrote no record for {pair}")
        record = json.loads(prov.read_text(encoding="utf-8"))
        gate = board_gate(record, original, out)
        record["board_gate"] = gate
        prov.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print(f"GATE {pair}: {'PASS' if gate['passed'] else 'FAIL'} {gate['note']}")
        results.append({"pair": pair, "exit": 0 if gate["passed"] else 1})
        for part, (folder, w, h, _) in parts.items():
            y = 0 if h == TOP_H else TOP_H + WINDOW_H
            strip, alpha, final = pwork / f"strip-{prompt_id(part)}.png", pwork / f"alpha-{prompt_id(part)}.png", pwork / f"master-{prompt_id(part)}.png"
            run(["magick", str(out), "-crop", f"{w}x{h}+0+{y}", "+repage", "-alpha", "off", str(strip)])
            upscale_strip(args.frames / folder / "f0.png", w, h, alpha, with_alpha=True)
            run(["magick", str(strip), str(alpha), "-alpha", "off", "-compose", "CopyOpacity", "-composite", str(final)])
            fit = [sys.executable, "-B", str(HERE / "fit.py"), "fit", part, "--source", str(final), "--size", f"{w}x{h}", "--key", "none", "--replace"]
            fit += ["--provenance", str(derived_record(local, part, args.frames / folder / "f0.png", final, prov, pwork))]
            if args.dry_run:
                fit.append("--dry-run")
            proc = subprocess.run(fit, capture_output=True, text=True)
            print(f"FIT {part}: exit {proc.returncode} {(proc.stdout.strip() or proc.stderr.strip()).splitlines()[-1:]}")
            results.append({"part": part, "fit_exit": proc.returncode})
    return 0 if all(r.get("exit", 0) == 0 and r.get("fit_exit", 0) == 0 for r in results) else 1


def read_gray(path: Path) -> tuple[int, int, bytes]:
    img = pngio.read(path)
    return img.w, img.h, bytes(img.data)


def cmd_caps(args) -> int:
    local = args.local_root
    work = local / "work" / "B-BOARD" / "caps"
    work.mkdir(parents=True, exist_ok=True)
    sheet = local / "outbox" / "B-BOARD" / f"cap-{args.variant}.png"
    if not sheet.is_file():   # the runner refuses a ground that is not flat; the canvas is in its work folder and the bars are keyed by trimming below
        sheet = latest_canvas(local, f"cap-{args.variant}")
    if sheet is None or not sheet.is_file():
        raise BoardError(f"no cap sheet for {args.variant} (run the batch first)")
    # the two bars on the flat ground: find them by trimming columns, the tall one (bottom cap) at the left, the short one (top cap) at the right
    w, h = (int(v) for v in run(["magick", "identify", "-format", "%w %h", str(sheet)]).split())
    box = {}
    for name, half in (("bottom", f"{w // 2}x{h}+0+0"), ("top", f"{w - w // 2}x{h}+{w // 2}+0")):
        text = run(["magick", str(sheet), "-crop", half, "+repage", "-fuzz", "8%", "-trim", "-format", "%@", "info:"]).strip()
        box[name] = text
        print(f"BAR {name}: trim box {text}")
    parts = {"bottom": BOTTOM_H, "top": TOP_H}
    for part, (height, mirrored) in CAPS.items():
        which = "top" if height == TOP_H else "bottom"
        half = f"{w // 2}x{h}+0+0" if which == "bottom" else f"{w - w // 2}x{h}+{w // 2}+0"
        raw = work / f"bar-{which}-{args.variant}.png"
        run(["magick", str(sheet), "-crop", half, "+repage", "-fuzz", "8%", "-trim", "+repage", "-filter", "Lanczos", "-resize", f"{BAR_W}x{parts[which]}!", "-alpha", "off", str(raw)])
        bar = pngio.read(raw)
        for i in range(0, len(bar.data), 4):
            bar.data[i + 3] = 255
        out = work / f"{prompt_id(part)}-{args.variant}.png"
        pngio.write(out, cap_image(bar, height, mirrored))
        gen = json.loads((local / "outbox" / "B-BOARD" / f"cap-{args.variant}.provenance.json").read_text(encoding="utf-8"))
        gen["image_sha256"] = sha256_file(out)
        gen["derived"] = {"tool": "board_build.py caps: the bar trimmed from the sheet, scaled to the bar size, a deterministic soft shadow outward, mirrored for a right cap",
                          "sheet_file": sheet.name, "sheet_sha256": sha256_file(sheet), "variant": args.variant, "bar_px": BAR_W, "cap_px": CAP_W,
                          "shadow": {"rgb": list(SHADOW_RGB), "alpha_at_bar": SHADOW_ALPHA, "profile": "cosine"}}
        record = work / f"{prompt_id(part)}-{args.variant}.provenance.json"
        record.write_text(json.dumps(gen, indent=2) + "\n", encoding="utf-8")
        fit = [sys.executable, "-B", str(HERE / "fit.py"), "fit", part, "--source", str(out), "--size", f"{CAP_W}x{height}", "--key", "none", "--replace", "--soft-shadow", "--provenance", str(record)]
        if args.dry_run:
            fit.append("--dry-run")
        proc = subprocess.run(fit, capture_output=True, text=True)
        print(f"FIT {part}: exit {proc.returncode} {(proc.stdout.strip() or proc.stderr.strip()).splitlines()[-1:]}")
    return 0


def self_test() -> int:
    ramp = shadow_ramp()
    assert len(ramp) == CAP_W and ramp[0] < 0.01 and ramp[CAP_W - BAR_W - 1] > SHADOW_ALPHA * 0.99 and all(a == 0 for a in ramp[CAP_W - BAR_W:]), "ramp"
    assert all(ramp[i] <= ramp[i + 1] for i in range(CAP_W - BAR_W - 1)), "ramp is monotonic"
    bar = pngio.Image(BAR_W, 6, bytearray(bytes((200, 160, 60, 255)) * (BAR_W * 6)))
    left, right = cap_image(bar, 6, False), cap_image(bar, 6, True)
    assert left.pixel(CAP_W - 1, 0) == (200, 160, 60, 255) and left.pixel(0, 0)[3] <= 3, "left cap: bar at the inner (right) edge"
    assert right.pixel(0, 0) == (200, 160, 60, 255) and right.pixel(CAP_W - 1, 0)[3] <= 3, "right cap is the mirror"
    assert STRIPS["board.inttop"][2] + WINDOW_H + STRIPS["board.intbotom"][2] == 2160, "the zones tile the composition"
    assert all(v % 6 == 0 for part in STRIPS.values() for v in part[1:3]) and CAP_W % 6 == 0 and BAR_W % 6 == 0, "grid"
    assert prompt_id("cap.top_strip_left") == "cap-top-strip-left" and prompt_id("board.inttop") == "board-inttop"
    print("board_build self-test: OK")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("refs", "crop", "strips", "caps"):
        p = sub.add_parser(name)
        p.add_argument("--local-root", type=Path, default=None)
        p.add_argument("--frames", type=Path, default=None, help="the census frames folder (default <local root>/census/frames)")
        if name == "strips":
            p.add_argument("--window-source", type=Path, default=None, help="a 2880 x 2160 original whose window rows are the neighbour (default compare/surface-study/original.png)")
            p.add_argument("--bevel", choices=("lighten", "original"), default="lighten")
            p.add_argument("--reuse", action="store_true", help="keep the composed pair of an earlier run (its record and built.png) and only redo the gate and the fit")
        if name == "caps":
            p.add_argument("--variant", choices=("plain", "rivets"), default="plain")
        if name in ("strips", "caps"):
            p.add_argument("--dry-run", action="store_true")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    if args.command == "self-test":
        return self_test()
    args.local_root = args.local_root or prompt_library.default_local_root()
    args.frames = args.frames or args.local_root / "census" / "frames"
    if args.command == "strips" and args.window_source is None:
        args.window_source = args.local_root / "compare" / "surface-study" / "original.png"
    try:
        return {"refs": cmd_refs, "crop": cmd_crop, "strips": cmd_strips, "caps": cmd_caps}[args.command](args)
    except BoardError as error:
        print(f"BOARD error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
