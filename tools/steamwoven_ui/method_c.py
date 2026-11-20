"""ARC-RES-2457 method C: a seamless high-resolution wood tile combined with the low-resolution macro image. Where the ORIGINAL (upscaled to the master
size) is wood, the result is the original's low-pass (plank layout, tone variation) plus the high-pass of the tile repeated over the whole picture; elsewhere it
is the original untouched. Needs ImageMagick (`magick`); reads and writes local files only, no art in git."""
from __future__ import annotations

import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

try:
    from . import pngio, tone_check
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import pngio  # type: ignore
    import tone_check  # type: ignore

SCREEN = "ctx-inventory"
LAYOUT = (800, 600)


class MethodCError(Exception):
    pass


def tile_command(tile: Path, tile_px: int, size: tuple[int, int], out: Path) -> list[str]:
    """The tile reduced to `tile_px` square and repeated over `size`."""
    return ["magick", str(tile), "-resize", f"{tile_px}x{tile_px}!", "-write", "mpr:t", "+delete", "-size", f"{size[0]}x{size[1]}", "tile:mpr:t", str(out)]


def high_pass_command(image: Path, sigma: float, out: Path) -> list[str]:
    """0.5 + (image - blur(image)): the detail around mid grey."""
    return ["magick", str(image), "(", "+clone", "-blur", f"0x{sigma}", ")", "-compose", "Mathematics", "-define", "compose:args=0,-1,1,0.5", "-composite", str(out)]


def low_pass_command(image: Path, sigma: float, out: Path) -> list[str]:
    return ["magick", str(image), "-blur", f"0x{sigma}", str(out)]


def add_command(low: Path, high: Path, out: Path) -> list[str]:
    """low + (high - 0.5)."""
    return ["magick", str(low), str(high), "-compose", "Mathematics", "-define", "compose:args=0,1,1,-0.5", "-composite", str(out)]


def blend_command(original: Path, detailed: Path, mask: Path, out: Path) -> list[str]:
    return ["magick", str(original), str(detailed), str(mask), "-composite", str(out)]


def wood_mask(original: "pngio.Image", masks: dict | None = None) -> "pngio.Image":
    """The wood class of the ORIGINAL at 800 x 600 (tone_check's own class mask) as a grey image."""
    masks = masks or tone_check.load_masks()
    screen = masks["screens"][SCREEN]
    lab = tone_check.to_lab(tone_check.reduce_to_800x600(original))
    mask = tone_check.material_masks({**screen, "materials": {"wood": screen["materials"]["wood"]}}, masks["strip_counters"], lab)["wood"]
    grey = bytearray()
    for v in mask:
        g = max(0, min(255, round(v * 255)))
        grey += bytes((g, g, g, 255))
    return pngio.Image(LAYOUT[0], LAYOUT[1], grey)


def run(command: list[str]) -> None:
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise MethodCError(f"{command[0]} failed: {result.stderr.strip()[:300]}")


def write_mask(original: Path, out: Path, feather: float = 0.0, size: tuple[int, int] | None = None, solid: bool = False) -> None:
    """The wood mask of the original, at the original's size (or `size`), as a grey PNG. `solid`: the speckled class mask made into solid regions (a blur,
    a majority threshold and a small erosion), for the metrics that read only wood."""
    image = pngio.read(original)
    work = Path(tempfile.mkdtemp(prefix="method-c-mask-"))
    small = work / "mask-800.png"
    pngio.write(small, wood_mask(image))
    w, h = size or (image.w, image.h)
    command = ["magick", str(small), "-filter", "Triangle", "-resize", f"{w}x{h}!"]
    if solid:
        command += ["-blur", "0x8", "-threshold", "55%", "-morphology", "Erode", "Disk:6"]
    run(command + (["-blur", f"0x{feather}"] if feather else []) + ["-colorspace", "Gray", "-alpha", "off", "-define", "png:color-type=2", str(out)])


def build(original: Path, tile: Path, out: Path, tile_px: int, sigma: float, feather: float) -> dict:
    image = pngio.read(original)
    work = Path(tempfile.mkdtemp(prefix="method-c-"))
    mask = work / "mask.png"
    write_mask(original, mask, feather)
    tiled, high, low, detailed = work / "tiled.png", work / "high.png", work / "low.png", work / "detailed.png"
    run(tile_command(tile, tile_px, (image.w, image.h), tiled))
    run(high_pass_command(tiled, sigma, high))
    run(low_pass_command(original, sigma, low))
    run(add_command(low, high, detailed))
    run(blend_command(original, detailed, mask, out))
    return {"out": str(out), "work": str(work), "tile_px": tile_px, "sigma": sigma, "feather": feather}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--original", type=Path, required=True, help="the ORIGINAL upscaled to the master size (2880 x 2160)")
    parser.add_argument("--tile", type=Path, help="the seamless wood tile")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--mask-only", action="store_true", help="write the wood mask to --out and stop (solid regions, for the metrics)")
    parser.add_argument("--tile-px", type=int, default=432, help="edge of one tile repeat in master pixels (the tile is reduced to it)")
    parser.add_argument("--sigma", type=float, default=6.0, help="blur sigma in master pixels that separates macro from detail")
    parser.add_argument("--feather", type=float, default=3.0, help="blur sigma of the wood mask edge in master pixels")
    args = parser.parse_args(argv)
    try:
        if args.mask_only:
            write_mask(args.original, args.out, solid=True)
            print(f"METHOD C wrote the wood mask {args.out}")
            return 0
        if not args.tile:
            parser.error("--tile is required unless --mask-only")
        info = build(args.original, args.tile, args.out, args.tile_px, args.sigma, args.feather)
    except (MethodCError, tone_check.ToneError, pngio.PngError, KeyError, OSError) as error:
        print(f"METHOD C ERROR: {error!r}", file=sys.stderr)
        return 2
    print(f"METHOD C wrote {info['out']} (tile {info['tile_px']} px, sigma {info['sigma']}, feather {info['feather']})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
