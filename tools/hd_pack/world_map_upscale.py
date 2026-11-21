#!/usr/bin/env python3
"""ARC-RENDER-3065: the HD world map recipe (docs/rendering/hd-world-town-map.md#world-map-one-picture-non-generative-upscale-ac3).

Two commands, both on the player's own local picture (the assembled 2000 x 2000 world map); nothing of the original or of a result is stored in git.

    blend   edge-masked blend of a `realesr-animevideov3` x4 picture (made by `realesrgan-ncnn-vulkan`, a non-generative
            super-resolution network) and a Lanczos x4 picture of the same map: the network wins on strong ink lines and
            lettering, Lanczos everywhere else, so the canvas weave keeps the original's softness (the plain network turns the
            weave into a grid).
    outline outline check: the candidate is box-downscaled back to the original size and compared with it: mean absolute
            error, and precision / recall / F1 of the Canny outlines at 1 and 2 pixels of tolerance.

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
    """The soft edge mask at the upscaled size: 1 on strong ink lines and lettering, 0 on plain paper."""
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
    height, width = original.shape[:2]
    return np.clip(cv2.resize(soft, (width * scale, height * scale), interpolation=cv2.INTER_CUBIC), 0, 1)


def blend(original, network_up, lanczos_up_image=None):
    """Edge-masked blend: mask * network + (1 - mask) * Lanczos, in bounded strips."""
    cv2, np, _ = _imports()
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
    original = read_bgr(args.original)
    if args.command == "blend":
        network = read_bgr(args.network)
        lanczos = read_bgr(args.lanczos) if args.lanczos else None
        result, coverage = blend(original, network, lanczos)
        if not cv2.imwrite(str(out_path), result, [cv2.IMWRITE_PNG_COMPRESSION, 1]):
            raise SystemExit(f"cannot write {out_path}")
        print(f"wrote {out_path} (mask coverage {coverage:.3f})")
        print(_format("blend", outline_check(original, result)))
        return 0
    for item in args.candidate:
        name, _, path = item.partition("=")
        if not path:
            raise SystemExit(f"--candidate wants NAME=PATH, got {item!r}")
        print(_format(name, outline_check(original, read_bgr(path))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
