#!/usr/bin/env python3
"""ARC-RENDER-3065: the HD world map recipe (docs/rendering/hd-world-town-map.md#world-map-one-picture-non-generative-upscale-ac3).

Three commands, all on the player's own local picture (the assembled 2000 x 2000 world map); nothing of the original or of a result is stored in git.

    blend   edge-masked blend of a `realesr-animevideov3` x4 picture (made by `realesrgan-ncnn-vulkan`, a non-generative
            super-resolution network) and a Lanczos x4 picture of the same map: the network wins on strong ink lines and
            lettering, Lanczos everywhere else, so the canvas weave keeps the original's softness (the plain network turns the
            weave into a grid).
            After the blend the picture is back-projected onto the original (the original minus its box downscale is added back), which
            tames the network's blotches on dense hatching.
    cut     cut the picture into the 64 tiles of 1000 x 1000 and derive the continent picture (1460 x 1460) for `hd_pack.py maps-plan`.
    outline outline check: the candidate is box-downscaled back to the original size and compared with it: mean absolute
            error, and precision / recall / F1 of the Canny outlines at 1 and 2 pixels of tolerance, and the canvas strength on
            flat paper and water against Lanczos.

Needs numpy, opencv-python-headless and Pillow (local research tools, not part of the root checks).
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

SCALE = 4
# Mask recipe (owner pick "Blend A", 2026-11-21): strong ink lines only.
MASK_CANNY = (100, 200)  # on the original after a Gaussian blur of MASK_PRE_BLUR
MASK_PRE_BLUR = 1.0
MASK_MIN_COMPONENT = 30  # edge pixels; smaller specks are paper grain, not lines
MASK_DILATE_RADIUS = 2  # original pixels (elliptical kernel of 2 * radius + 1)
MASK_FEATHER_SIGMA = 1.2  # original pixels; the soft transition between the network and Lanczos
# Thickness gate (ARC-RENDER-3065, owner review 2026-11-24): the mask counts only near thick dark ink (shoreline, trees, mountains, the stems of lettering): a dark stroke at
# least about 3 original pixels wide (distance transform of the pixels below MASK_INK_LUMA reaching MASK_CORE_DISTANCE). The thin grey strokes of the sea hatching beside the coast
# (1 to 2 pixels) are no core, so they stay with Lanczos: through the network they become outlined cells with light halos at the display scale.
MASK_INK_LUMA = 100
MASK_CORE_DISTANCE = 1.5  # original pixels (distance to the nearest non-dark pixel)
MASK_CORE_DILATE_RADIUS = 3  # original pixels
MASK_CORE_FEATHER_SIGMA = 1.0  # original pixels
# Back-projection (ARC-RENDER-3065, owner review 2026-11-21): the network turns the dense sea hatching beside the coast into black-and-white blotches. After the
# blend the picture is pulled back to the original: the original minus the box downscale of the picture is added back, upsampled (bicubic), so that the picture
# agrees with the original at its own resolution. A mask gate on light strokes, a clamp of the network's move and a local min/max limiter were tried and did not
# remove the blotches (docs/rendering/hd-world-town-map.md).
BACK_PROJECT_STEPS = 1
# Outline check recipe: Canny thresholds that give the Lanczos x4 picture F1 0.965 (the check of ARC-RES-2992 was lost, #3080).
CHECK_CANNY = (40, 100)
STRIP = 1000  # output rows blended at a time: bounds the float32 temporaries (the 8000 x 8000 pictures and the mask stay resident, about 1 GB)


def _imports():
    try:
        import cv2
        import numpy as np
        from PIL import Image
    except ImportError as error:  # pragma: no cover - environment, reported plainly
        raise SystemExit(f"world_map_upscale needs numpy, opencv-python-headless and Pillow: {error}")
    return cv2, np, Image


def read_bgr(path: str | Path):
    cv2, _, _ = _imports()
    image = cv2.imread(str(path), cv2.IMREAD_COLOR)
    if image is None:
        raise SystemExit(f"cannot read the image: {path}")
    return image


def lanczos_up(original, scale: int = SCALE):
    """Plain Lanczos x`scale` (Pillow's Lanczos, the baseline of the upscale research)."""
    cv2, np, Image = _imports()
    rgb = cv2.cvtColor(original, cv2.COLOR_BGR2RGB)
    height, width = original.shape[:2]
    resized = Image.fromarray(rgb).resize((width * scale, height * scale), Image.LANCZOS)
    return cv2.cvtColor(np.asarray(resized), cv2.COLOR_RGB2BGR)


def line_mask(original, scale: int = SCALE):
    """The soft edge mask at the upscaled size: 1 on strong thick ink lines and lettering, 0 on plain paper and on thin hatching."""
    cv2, np, _ = _imports()
    gray = cv2.cvtColor(original, cv2.COLOR_BGR2GRAY)
    blurred = cv2.GaussianBlur(gray, (0, 0), MASK_PRE_BLUR)
    edges = cv2.Canny(blurred, *MASK_CANNY) > 0
    count, labels, stats, _ = cv2.connectedComponentsWithStats(edges.astype(np.uint8), connectivity=8)
    keep = np.zeros(count, dtype=bool)
    keep[1:] = stats[1:, cv2.CC_STAT_AREA] >= MASK_MIN_COMPONENT
    strong = keep[labels]
    size = 2 * MASK_DILATE_RADIUS + 1
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (size, size))
    soft = cv2.dilate(strong.astype(np.uint8) * 255, kernel).astype(np.float32) / 255
    soft = np.clip(cv2.GaussianBlur(soft, (0, 0), MASK_FEATHER_SIGMA), 0, 1)
    if MASK_CORE_DISTANCE:
        core = (cv2.distanceTransform((gray < MASK_INK_LUMA).astype(np.uint8), cv2.DIST_L2, 5) >= MASK_CORE_DISTANCE).astype(np.uint8) * 255
        core_size = 2 * MASK_CORE_DILATE_RADIUS + 1
        near_core = cv2.dilate(core, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (core_size, core_size))).astype(np.float32) / 255
        soft = soft * np.clip(cv2.GaussianBlur(near_core, (0, 0), MASK_CORE_FEATHER_SIGMA), 0, 1)
    height, width = original.shape[:2]
    return np.clip(cv2.resize(soft, (width * scale, height * scale), interpolation=cv2.INTER_CUBIC), 0, 1)


def back_project(original, picture, steps: int = BACK_PROJECT_STEPS, scale: int = SCALE):
    """Iterative back-projection onto the original: add (original - box downscale of the picture), upsampled bicubic, `steps` times; bounded strips."""
    cv2, np, _ = _imports()
    height, width = original.shape[:2]
    if picture.shape[:2] != (height * scale, width * scale):
        raise SystemExit(f"size mismatch: picture {picture.shape}, original {original.shape}")
    reference = original.astype(np.float32)
    for _ in range(steps):
        residual = np.empty_like(reference)
        for y in range(0, height, STRIP // scale):
            rows = min(STRIP // scale, height - y)
            down = cv2.resize(picture[y * scale : (y + rows) * scale], (width, rows), interpolation=cv2.INTER_AREA)
            residual[y : y + rows] = reference[y : y + rows] - down.astype(np.float32)
        result = np.empty_like(picture)
        for y in range(0, height * scale, STRIP):
            top, bottom = max(0, y // scale - 2), min(height, (y + STRIP) // scale + 2)  # padded source rows: the bicubic kernel reaches 2 rows
            up = cv2.resize(residual[top:bottom], (width * scale, (bottom - top) * scale), interpolation=cv2.INTER_CUBIC)
            up = up[y - top * scale : y - top * scale + STRIP]
            result[y : y + STRIP] = np.clip(picture[y : y + STRIP].astype(np.float32) + up + 0.5, 0, 255).astype(np.uint8)
        picture = result
    return picture


def blend(original, network_up, lanczos_up_image=None, back_project_steps: int = BACK_PROJECT_STEPS, project_on: str = "picture"):
    """Edge-masked blend: mask * network + (1 - mask) * Lanczos in bounded strips, then back-projected onto the original.

    `project_on` "picture" back-projects the blended result (agrees with the original everywhere, the canvas follows the original); "network" back-projects only
    the network picture before the blend (flat paper and water stay exactly Lanczos)."""
    cv2, np, _ = _imports()
    if project_on not in ("picture", "network"):
        raise SystemExit(f"project_on must be 'picture' or 'network', not {project_on!r}")
    if back_project_steps and project_on == "network":
        network_up = back_project(original, network_up, back_project_steps)
    lanczos = lanczos_up(original) if lanczos_up_image is None else lanczos_up_image
    if network_up.shape != lanczos.shape:
        raise SystemExit(f"size mismatch: network {network_up.shape}, Lanczos {lanczos.shape}")
    mask = line_mask(original)
    if mask.shape != lanczos.shape[:2]:
        raise SystemExit(f"mask {mask.shape} does not match the picture {lanczos.shape[:2]}")
    out = np.empty_like(lanczos)
    for y in range(0, lanczos.shape[0], STRIP):
        a = network_up[y : y + STRIP].astype(np.float32)
        l = lanczos[y : y + STRIP].astype(np.float32)
        w = mask[y : y + STRIP, :, None]
        out[y : y + STRIP] = np.clip(w * a + (1 - w) * l + 0.5, 0, 255).astype(np.uint8)
    if back_project_steps and project_on == "picture":
        out = back_project(original, out, back_project_steps)
    return out, float(mask.mean())


def _edges(image):
    cv2, _, _ = _imports()
    return cv2.Canny(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY), *CHECK_CANNY) > 0


def _prf(reference, test, tolerance: int):
    cv2, np, _ = _imports()
    size = 2 * tolerance + 1
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (size, size))
    near_reference = cv2.dilate(reference.astype(np.uint8), kernel) > 0
    near_test = cv2.dilate(test.astype(np.uint8), kernel) > 0
    precision = (test & near_reference).sum() / max(1, test.sum())
    recall = (reference & near_test).sum() / max(1, reference.sum())
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return float(precision), float(recall), float(f1)


def outline_check(original, candidate) -> dict[str, float]:
    """MAE after a box downscale, and the Canny outline precision / recall / F1 at 1 and 2 pixels."""
    cv2, np, _ = _imports()
    height, width = original.shape[:2]
    down = cv2.resize(candidate, (width, height), interpolation=cv2.INTER_AREA)
    mae = float(np.abs(down.astype(np.float32) - original.astype(np.float32)).mean())
    reference, test = _edges(original), _edges(down)
    precision, recall, f1_1 = _prf(reference, test, 1)
    _, _, f1_2 = _prf(reference, test, 2)
    return {"mae": mae, "precision_1px": precision, "recall_1px": recall, "f1_1px": f1_1, "f1_2px": f1_2}


def weave_ratio(original, candidate, lanczos=None, flat_below: float = 0.01) -> float:
    """Canvas strength on flat paper and water (where the mask is 0): high-pass energy of the candidate over that of Lanczos x4 (1.0 = as soft a canvas as Lanczos)."""
    cv2, np, _ = _imports()
    reference = lanczos_up(original) if lanczos is None else lanczos
    flat = line_mask(original) < flat_below

    def energy(picture) -> float:
        gray = cv2.cvtColor(picture, cv2.COLOR_BGR2GRAY).astype(np.float32)
        return float((gray - cv2.GaussianBlur(gray, (0, 0), 3.0))[flat].std())

    return energy(candidate) / max(energy(reference), 1e-6)


def cut_pictures(picture, out_dir: Path, scale: int = SCALE, tiles_per_side: int = 8, tile_base: str = "smallmapchunks", zoomed_name: str = "map_zoomed") -> int:
    """Cut the upscaled map into `tiles_per_side`^2 tiles (`<tile_base>001.png` .. row-major, `250 x scale` square) and derive the continent picture (`365 x scale`, Lanczos from
    the whole picture), with the `SHA256SUMS.txt` that `hd_pack.py maps-plan` checks. Returns the number of files written."""
    cv2, np, Image = _imports()
    import hashlib

    height, width = picture.shape[:2]
    tile = 250 * scale
    if (height, width) != (tile * tiles_per_side, tile * tiles_per_side):
        raise SystemExit(f"the picture is {width}x{height}, expected {tile * tiles_per_side} square ({tiles_per_side} tiles of {tile})")
    if out_dir.exists() and (not out_dir.is_dir() or any(out_dir.iterdir())):
        raise SystemExit(f"refusing to write into a folder that is not empty: {out_dir}")
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []
    for index in range(tiles_per_side * tiles_per_side):
        row, column = divmod(index, tiles_per_side)
        name = f"{tile_base}{index + 1:03d}.png"
        if not cv2.imwrite(str(out_dir / name), picture[row * tile : (row + 1) * tile, column * tile : (column + 1) * tile], [cv2.IMWRITE_PNG_COMPRESSION, 3]):
            raise SystemExit(f"cannot write {out_dir / name}")
        written.append(name)
    zoomed = 365 * scale
    resized = Image.fromarray(cv2.cvtColor(picture, cv2.COLOR_BGR2RGB)).resize((zoomed, zoomed), Image.LANCZOS)
    name = f"{zoomed_name}.png"
    resized.save(out_dir / name, compress_level=3)
    written.append(name)
    lines = [f"{hashlib.sha256((out_dir / n).read_bytes()).hexdigest()}  ./{n}" for n in written]
    (out_dir / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n")
    return len(written)


def _format(name: str, result: dict[str, float]) -> str:
    return (f"{name}: MAE {result['mae']:.2f}, precision/recall (1 px) {result['precision_1px']:.3f}/{result['recall_1px']:.3f}, "
            f"F1 (1 px / 2 px) {result['f1_1px']:.3f} / {result['f1_2px']:.3f}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    p_blend = sub.add_parser("blend", help="edge-masked blend of the network picture and Lanczos")
    p_blend.add_argument("--original", required=True, help="the assembled 2000 x 2000 world map")
    p_blend.add_argument("--network", required=True, help="the realesr-animevideov3 x4 picture of it")
    p_blend.add_argument("--lanczos", help="a ready Lanczos x4 picture (default: made here)")
    p_blend.add_argument("--out", required=True, help="the blended picture (PNG)")
    p_blend.add_argument("--back-project-on", choices=("picture", "network"), default="picture", help="back-project the blended picture or only the network picture")
    p_blend.add_argument("--back-project-steps", type=int, default=BACK_PROJECT_STEPS, help="back-projection steps onto the original after the blend (0 = none)")
    p_cut = sub.add_parser("cut", help="cut the x4 picture into the 64 tiles and derive the continent picture")
    p_cut.add_argument("--picture", required=True, help="the 8000 x 8000 picture")
    p_cut.add_argument("--out-dir", required=True, help="a new or empty folder for the PNGs and SHA256SUMS.txt")
    p_cut.add_argument("--zoomed-name", default="map_zoomed")
    p_check = sub.add_parser("outline", help="outline check of x4 candidates against the original")
    p_check.add_argument("--original", required=True)
    p_check.add_argument("--candidate", action="append", required=True, metavar="NAME=PATH")
    args = parser.parse_args(argv)
    cv2, _, _ = _imports()
    if args.command == "blend":
        out_path = Path(args.out)
        if out_path.exists() or out_path.is_symlink():
            raise SystemExit(f"refusing to overwrite {out_path}")
        if not out_path.parent.is_dir():
            raise SystemExit(f"the folder of --out does not exist: {out_path.parent}")
    if args.command == "cut":
        count = cut_pictures(read_bgr(args.picture), Path(args.out_dir), zoomed_name=args.zoomed_name)
        print(f"wrote {count} pictures and SHA256SUMS.txt into {args.out_dir}")
        return 0
    original = read_bgr(args.original)
    if args.command == "blend":
        network = read_bgr(args.network)
        lanczos = read_bgr(args.lanczos) if args.lanczos else None
        result, coverage = blend(original, network, lanczos, args.back_project_steps, args.back_project_on)
        if not cv2.imwrite(str(out_path), result, [cv2.IMWRITE_PNG_COMPRESSION, 1]):
            raise SystemExit(f"cannot write {out_path}")
        print(f"wrote {out_path} (mask coverage {coverage:.3f})")
        print(_format("blend", outline_check(original, result)) + f", weave vs Lanczos {weave_ratio(original, result, lanczos):.2f}")
        return 0
    for item in args.candidate:
        name, _, path = item.partition("=")
        if not path:
            raise SystemExit(f"--candidate wants NAME=PATH, got {item!r}")
        candidate = read_bgr(path)
        print(_format(name, outline_check(original, candidate)) + f", weave vs Lanczos {weave_ratio(original, candidate):.2f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
