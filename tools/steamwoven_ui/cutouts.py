"""ARC-RES-2455: cut-outs and tiles of the material sheet (SWI-GEN-2).

The material sheet is cut from the approved, tone-fitted context images, not drawn: `cut` crops each rectangle of `prompts/cutouts.json` (coordinates of the
800 x 600 layout, scaled by the source width / 800) out of `local/steamwoven-ui/outbox/style-anchors-contexts/<source>.png` into
`local/steamwoven-ui/material-sheet/cutouts/<id>.png` and records where each came from (source file and hash, layout rectangle, pixel rectangle).
Codex only makes the seamless tiles of the large surfaces (wood, paper, parchment) from a cut-out (`tiles-inbox` builds the batch inbox for `codex_batch.py`);
`tile-check` compares a returned tile with the ORIGINAL material (mean L*, chroma and the spread of brightness as in `tone_check`) and measures the seam ratio.
Standard library only, on `pngio` and `tone_check`. Images stay under the ignored `local/` (SWI-PRV-1); only the list of rectangles and the prompts are in git.

    python3 -B tools/steamwoven_ui/cutouts.py check
    python3 -B tools/steamwoven_ui/cutouts.py cut [--local-root DIR]
    python3 -B tools/steamwoven_ui/cutouts.py tiles-inbox [--local-root DIR]
    python3 -B tools/steamwoven_ui/cutouts.py tile-check [--local-root DIR] [--results DIR] [--json]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

try:
    from . import pngio, tone_check
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import pngio  # type: ignore
    import tone_check  # type: ignore

HERE = Path(__file__).resolve().parent
PROMPTS = HERE / "prompts"
CUTOUTS_FILE = PROMPTS / "cutouts.json"
LAYOUT_W, LAYOUT_H = 800, 600
SOURCE_BATCH = "style-anchors-contexts"
TILE_BATCH = "style-anchors-tiles"
TILE_LIMITS = {"mean_l_delta": 3.0, "mean_chroma_ratio": 0.12, "std_l_ratio": 0.25, "seam_ratio": 1.5}  # std_l_ratio only counts when the std differs by more than 1 L*
TILE_STD_FLOOR = 1.0


class CutoutError(Exception):
    pass


def load(path: Path = CUTOUTS_FILE) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def default_local_root() -> Path:
    from prompt_library import default_local_root as root  # noqa: PLC0415  (same folder rule as the library)
    return root()


def png_size(path: Path) -> tuple[int, int]:
    """Width and height from the IHDR chunk (the image is not decoded)."""
    head = path.read_bytes()[:24]
    if head[:8] != pngio.SIGNATURE or head[12:16] != b"IHDR":
        raise CutoutError(f"{path} is not a PNG")
    return int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")


def check(spec: dict, masks: dict | None = None) -> list[str]:
    """Problems of the cut-out list; empty means valid."""
    problems: list[str] = []
    masks = masks or tone_check.load_masks()
    ids = [i["id"] for i in spec["items"]]
    if len(set(ids)) != len(ids):
        problems.append("duplicate cut-out ids")
    for item in spec["items"]:
        x0, y0, x1, y1 = item["rect"]
        if not (0 <= x0 < x1 <= LAYOUT_W and 0 <= y0 < y1 <= LAYOUT_H):
            problems.append(f"{item['id']}: rectangle {item['rect']} is outside the 800 x 600 layout")
        if item["source"] not in masks["screens"]:
            problems.append(f"{item['id']}: source {item['source']} is not a context screen")
    tile_ids = [t["id"] for t in spec["tiles"]]
    if len(set(tile_ids)) != len(tile_ids):
        problems.append("duplicate tile ids")
    for tile in spec["tiles"]:
        if tile["from"] not in ids:
            problems.append(f"{tile['id']}: cut-out {tile['from']} is not in the list")
        reference = tile["reference"]
        screen = masks["screens"].get(reference["screen"])
        if screen is None or reference["material"] not in screen["materials"]:
            problems.append(f"{tile['id']}: reference {reference} has no tone mask")
        prompt = PROMPTS / f"{tile['id']}.txt"
        if not prompt.is_file():
            problems.append(f"{tile['id']}: prompt file is missing")
        elif not prompt.read_text(encoding="utf-8").isascii():
            problems.append(f"{tile['id']}: prompt text is not plain ASCII")
    return problems


def pixel_rect(rect: list, width: int, height: int) -> tuple[int, int, int, int]:
    """The layout rectangle on an image of any size (rounded outwards by the scale; at least 1 px)."""
    sx, sy = width / LAYOUT_W, height / LAYOUT_H
    x0, y0 = int(math.floor(rect[0] * sx)), int(math.floor(rect[1] * sy))
    x1, y1 = max(x0 + 1, int(math.ceil(rect[2] * sx))), max(y0 + 1, int(math.ceil(rect[3] * sy)))
    return x0, y0, min(x1, width), min(y1, height)


def cut(spec: dict, local_root: Path) -> dict:
    """Write the cut-outs and their provenance record; returns the record."""
    problems = check(spec)
    if problems:
        raise CutoutError("; ".join(problems))
    sources = local_root / "outbox" / SOURCE_BATCH
    out = local_root / "material-sheet" / "cutouts"
    out.mkdir(parents=True, exist_ok=True)
    record: dict = {}
    images: dict[str, pngio.Image] = {}
    for item in spec["items"]:
        path = sources / f"{item['source']}.png"
        if not path.is_file():
            raise CutoutError(f"missing source image {path}")
        if item["source"] not in images:
            images[item["source"]] = pngio.read(path)
        image = images[item["source"]]
        x0, y0, x1, y1 = pixel_rect(item["rect"], image.w, image.h)
        piece = image.crop(x0, y0, x1 - x0, y1 - y0)
        target = out / f"{item['id']}.png"
        pngio.write(target, piece)
        record[item["id"]] = {"source": item["source"], "source_file": f"outbox/{SOURCE_BATCH}/{item['source']}.png", "source_sha256": sha256_file(path),
                              "source_size": [image.w, image.h], "layout_rect": item["rect"], "pixel_rect": [x0, y0, x1, y1], "size": [piece.w, piece.h],
                              "material": item["material"], "sha256": sha256_file(target)}
    (out / "cutouts.provenance.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    return record


def tiles_inbox(spec: dict, local_root: Path) -> dict:
    """Build the batch inbox `style-anchors-tiles` (prompt = common rules + tile text; reference = the cut-out) for `codex_batch.py run`."""
    common = (PROMPTS / "common.txt").read_text(encoding="utf-8").rstrip("\n")
    inbox = local_root / "inbox" / TILE_BATCH
    (inbox / "prompts").mkdir(parents=True, exist_ok=True)
    items = []
    for tile in spec["tiles"]:
        cutout = local_root / "material-sheet" / "cutouts" / f"{tile['from']}.png"
        if not cutout.is_file():
            raise CutoutError(f"missing cut-out {cutout}: run `cut` first")
        text = common + "\n\n" + (PROMPTS / f"{tile['id']}.txt").read_text(encoding="utf-8")
        text.encode("ascii")
        (inbox / "prompts" / f"{tile['id']}.txt").write_text(text, encoding="utf-8")
        ref_dir = inbox / "refs" / tile["id"]
        ref_dir.mkdir(parents=True, exist_ok=True)
        ref = ref_dir / f"{tile['from']}.png"
        ref.write_bytes(cutout.read_bytes())
        items.append({"id": tile["id"], "title": f"Seamless tile from {tile['from']}", "prompt_file": f"prompts/{tile['id']}.txt",
                      "prompt_sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(), "size": [1254, 1254],
                      "references": [{"slot": "cutout", "file": f"refs/{tile['id']}/{tile['from']}.png", "sha256": sha256_file(ref)}],
                      "outbox_file": f"outbox/{TILE_BATCH}/{tile['id']}.png"})
    (inbox / "batch.json").write_text(json.dumps({"batch": TILE_BATCH, "after": "cutouts of the accepted contexts", "items": items}, indent=2) + "\n", encoding="utf-8")
    return {"inbox": str(inbox), "items": len(items)}


def seam_ratio(image: "pngio.Image") -> float:
    """Worse of the two axes: the mean colour step across the wrap-around join (last column to first, last row to first) over the mean step between neighbouring
    columns (rows) inside the tile; 1.0 = as smooth as inside. A flat interior with a jump at the join gives 1e9."""
    w, h, d = image.w, image.h, image.data
    if w < 2 or h < 2:
        raise CutoutError(f"a tile of {w} x {h} px has no inside to compare with")

    def column_step(x_a: int, x_b: int) -> float:
        return sum(abs(d[(y * w + x_a) * 4 + c] - d[(y * w + x_b) * 4 + c]) for y in range(h) for c in range(3)) / (h * 3)

    def row_step(y_a: int, y_b: int) -> float:
        return sum(abs(d[(y_a * w + x) * 4 + c] - d[(y_b * w + x) * 4 + c]) for x in range(w) for c in range(3)) / (w * 3)

    cols, rows = range(0, w - 1, max(1, w // 64)), range(0, h - 1, max(1, h // 64))
    inner_cols, inner_rows = sum(column_step(x, x + 1) for x in cols) / len(cols), sum(row_step(y, y + 1) for y in rows) / len(rows)
    ratios = []
    for join, inner in ((column_step(w - 1, 0), inner_cols), (row_step(h - 1, 0), inner_rows)):
        ratios.append(join / inner if inner else (1.0 if join == 0 else 1e9))
    return max(ratios)


def tile_tone(image: "pngio.Image", scale: float) -> dict:
    """Blurred tone stats of a tile as `tone_check` reads a material: reduced by `scale` (context pixels per layout unit), Lab, box blur 6 px."""
    w, h = max(8, round(image.w / scale)), max(8, round(image.h / scale))
    wx, wy = tone_check._weights(image.w, w), tone_check._weights(image.h, h)
    planes = []
    for c in range(3):
        rows = [[sum(image.data[(y * image.w + i) * 4 + c] * wt for i, wt in cols) for cols in wx] for y in range(image.h)]
        out: list[float] = []
        for cols in wy:
            acc = [0.0] * w
            for j, wt in cols:
                acc = [a + v * wt for a, v in zip(acc, rows[j])]
            out.extend(acc)
        planes.append(tone_check.box_blur(out, tone_check.BLUR_RADIUS, w, h))
    return tone_check.tone_stats(tone_check.to_lab(planes))


def reference_tone(spec_tile: dict, local_root: Path, masks: dict | None = None) -> dict:
    """The ORIGINAL material's blurred tone (mean L*, chroma, std L*) over its pixels, from the layout masks."""
    masks = masks or tone_check.load_masks()
    reference = spec_tile["reference"]
    screen = masks["screens"][reference["screen"]]
    original = pngio.read(local_root / "refs" / f"{reference['screen']}-800x600.png")
    lab = tone_check.to_lab(tone_check.reduce_to_800x600(original))
    mask = tone_check.material_masks({**screen, "materials": {reference["material"]: screen["materials"][reference["material"]]}}, masks["strip_counters"], lab)[reference["material"]]
    mask_half = tone_check._half(mask)
    coverage = tone_check.box_blur(mask_half, tone_check.HALF_RADIUS, tone_check.HALF_W, tone_check.HALF_H)
    cells = {i for i in range(tone_check.HALF_W * tone_check.HALF_H) if mask_half[i] >= tone_check.MIN_COVER and coverage[i] >= tone_check.MIN_COVER}
    if len(cells) < tone_check.MIN_CELLS:
        raise CutoutError(f"reference {reference} is absent in the original")
    blurred = tone_check.masked_blur(lab, mask, coverage)
    return tone_check.tone_stats([v for i, v in enumerate(blurred) if i in cells])


def tile_report(tile: "pngio.Image", reference: dict, scale: float) -> dict:
    stats = tile_tone(tile, scale)
    values = {"mean_l_delta": stats["mean_l"] - reference["mean_l"],
              "mean_chroma_ratio": stats["mean_chroma"] / reference["mean_chroma"] - 1 if reference["mean_chroma"] else 0.0,
              "std_l_ratio": stats["std_l"] / reference["std_l"] - 1 if reference["std_l"] else 0.0,
              "std_l_delta": stats["std_l"] - reference["std_l"], "seam_ratio": seam_ratio(tile)}
    failed = [k for k, limit in TILE_LIMITS.items() if (values[k] > limit if k == "seam_ratio" else abs(values[k]) > limit)
              and (k != "std_l_ratio" or abs(values["std_l_delta"]) > TILE_STD_FLOOR)]
    return {"reference": reference, "tile": stats, "values": values, "limits": TILE_LIMITS, "failed": failed, "pass": not failed}


def check_provenance(tile_id: str, local_root: Path, results: Path) -> None:
    """The returned tile must come from the current inbox: its provenance prompt hash equals the batch item's (a stale file of an earlier run is refused)."""
    record = results / f"{tile_id}.provenance.json"
    batch = local_root / "inbox" / TILE_BATCH / "batch.json"
    if not record.is_file() or not batch.is_file():
        raise CutoutError(f"no provenance record or inbox batch for {tile_id}")
    wanted = next((i["prompt_sha256"] for i in json.loads(batch.read_text(encoding="utf-8"))["items"] if i["id"] == tile_id), None)
    if json.loads(record.read_text(encoding="utf-8")).get("prompt_sha256") != wanted:
        raise CutoutError(f"tile {tile_id} was made from another prompt than the current inbox: generate it again")


def describe(name: str, report: dict) -> str:
    v, r, t = report["values"], report["reference"], report["tile"]
    flags = " ".join(f"{k}!" for k in report["failed"])
    return (f"{'PASS' if report['pass'] else 'FAIL'} {name}: L* {r['mean_l']:.1f} -> {t['mean_l']:.1f} ({v['mean_l_delta']:+.2f}), chroma {r['mean_chroma']:.1f} -> {t['mean_chroma']:.1f} "
            f"({v['mean_chroma_ratio']:+.1%}), spread (std L*) {r['std_l']:.1f} -> {t['std_l']:.1f} ({v['std_l_ratio']:+.1%}), seam ratio {v['seam_ratio']:.2f}" + (f"  [{flags}]" if flags else ""))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("check", "cut", "tiles-inbox", "tile-check"):
        p = sub.add_parser(name)
        if name != "check":
            p.add_argument("--local-root", type=Path)
        if name == "tile-check":
            p.add_argument("--results", type=Path)
            p.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    try:
        spec = load()
        if args.command == "check":
            problems = check(spec)
            print("CUTOUTS check " + ("PASS: %d cut-outs, %d tiles" % (len(spec["items"]), len(spec["tiles"])) if not problems else "FAIL"))
            for problem in problems:
                print(f"- {problem}")
            return 1 if problems else 0
        local = args.local_root or default_local_root()
        if args.command == "cut":
            record = cut(spec, local)
            for key, item in record.items():
                print(f"{key}: {item['source']} {item['pixel_rect']} -> {item['size'][0]} x {item['size'][1]} px")
            return 0
        if args.command == "tiles-inbox":
            info = tiles_inbox(spec, local)
            print(f"INBOX {info['inbox']}: {info['items']} items")
            return 0
        results = args.results or local / "outbox" / TILE_BATCH
        masks = tone_check.load_masks()
        reports = {}
        for tile in spec["tiles"]:
            path = results / f"{tile['id']}.png"
            if not path.is_file():
                raise CutoutError(f"missing tile {path}")
            check_provenance(tile["id"], local, results)
            image = pngio.read(path)
            if image.w != image.h or image.w < 256:
                raise CutoutError(f"tile {path.name} is {image.w} x {image.h} px: a square of at least 256 px is needed")
            source_width = png_size(local / "outbox" / SOURCE_BATCH / f"{tile['reference']['screen']}.png")[0]
            reports[tile["id"]] = tile_report(image, reference_tone(tile, local, masks), source_width / LAYOUT_W)
    except (CutoutError, tone_check.ToneError, KeyError, ValueError, OSError, pngio.PngError) as error:
        print(f"CUTOUTS ERROR: {error!r}", file=sys.stderr)
        return 2
    if args.json:
        print(json.dumps(reports, indent=2))
    else:
        print("limits: |dL*| <= 3, |chroma| <= 12%, |spread| <= 25% (when std differs by > 1 L*), seam ratio <= 1.5")
        for name, report in reports.items():
            print(describe(name, report))
    ok = all(r["pass"] for r in reports.values())
    print(f"TILE CHECK {'PASS' if ok else 'FAIL'}: {sum(r['pass'] for r in reports.values())}/{len(reports)}", file=sys.stderr if args.json else sys.stdout)
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
