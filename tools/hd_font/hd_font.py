#!/usr/bin/env python3
"""Traces the player's interface bitmap fonts into 4x coverage glyph frames (ARC-DEV-355, slice 1 of ARC-RES-100).

    python3 -B tools/hd_font/hd_font.py trace --out local/hd-font/out --dry-run
    python3 -B tools/hd_font/hd_font.py trace --out local/hd-font/out
    python3 -B tools/hd_font/hd_font.py self-test

`trace` reads the fonts' ART files from the player's own installation (the DAT archives and loose folders, in the original's
search order), traces each glyph's coverage mask into an outline and writes 4x coverage frames, one PNG per original frame, plus
`font.json` per font and `receipt.json`, into a folder under `local/` (ignored by git).  The frames are derived from the
player's own game data: they stay local, never go into git or a distributed pack.  `--dry-run` reads the sources and the
fonts and prints what would be written; it writes nothing and needs no dependencies.  A real run installs the locked
dependencies (tools/hd_font/requirements.lock, with hashes) into a private venv on first use.
Details: docs/workflow/hd-font.md and docs/research/font-vectorization.md.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import venv
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
LOCK = HERE / "requirements.lock"
IN_VENV = "ARCANUM_HD_FONT_IN_VENV"
TOOL_VERSION = 2
DEFAULT_GAME_DIR = "/Applications/Arcanum"
DEFAULT_MODULE = "Arcanum"
ALPHA_MAX_DEFAULT, LEVEL_CAP_DEFAULT, OPT_TOLERANCE_DEFAULT = 0.6, 0.42, 0.2  # the tracer's constants (outline.py; a test keeps them equal)
DEFAULT_ARTS = (27, 229, 171, 327, 840)  # morph15font, Flare12Font, Cloister18Font, Morph30Font, NewTimes16Font
OUT_ROOT_NAME = "hd-font"
VENV_MARKER = ".hd-font-venv"


def log(message: str) -> None:
    print(message, flush=True)


# --- private venv ----------------------------------------------------------------------------------------------------

def replaceable_venv(environment: Path) -> bool:
    """Only an absent folder or a venv this tool created (it carries the marker) may be (re)created with clear=True."""
    return not environment.exists() and not environment.is_symlink() or (environment / VENV_MARKER).is_file()


def _deps_ready() -> bool:
    try:
        import numpy, scipy, PIL, potrace  # noqa: F401
    except ImportError:
        return False
    return True


def ensure_venv(argv: list[str]) -> None:
    """Continue inside the private venv with the locked dependencies, else (re)create it from the lock and re-run there.
    Dependencies installed in the host interpreter are never used, so the lock's hashes always apply."""
    if os.environ.get(IN_VENV):
        if _deps_ready():
            return
        raise SystemExit(f"hd_font: the locked dependencies are not importable in {sys.prefix}")
    environment = Path(os.environ.get("ARCANUM_HD_FONT_VENV") or REPO / "local" / "hd-font" / "venv")
    python = environment / "bin" / "python3"
    stamp = environment / "lock.sha256"
    lock_sha = hashlib.sha256(LOCK.read_bytes()).hexdigest()
    if not python.exists() or not stamp.exists() or stamp.read_text().strip() != lock_sha:
        if not replaceable_venv(environment):
            raise SystemExit(f"hd_font: {environment} exists and was not created by this tool (no {VENV_MARKER}); refusing to replace it")
        log(f"hd_font: installing the locked dependencies from {LOCK.relative_to(REPO)} into {environment}")
        if environment.exists():
            shutil.rmtree(environment)  # only a folder with this tool's marker gets here (replaceable_venv)
        environment.mkdir(parents=True)
        (environment / VENV_MARKER).write_text("created by tools/hd_font/hd_font.py\n")  # first, so an interrupted install stays replaceable
        venv.EnvBuilder(clear=False, with_pip=True).create(environment)
        install = [str(python), "-m", "pip", "install", "--quiet", "--disable-pip-version-check", "--no-input",
                   "--require-hashes", "--only-binary=:all:", "--no-deps", "-r", str(LOCK)]
        if subprocess.call(install) != 0:
            raise SystemExit("hd_font: installing the locked dependencies failed (network or an unsupported platform; the lock holds macOS wheels)")
        stamp.write_text(lock_sha + "\n")
    os.execve(str(python), [str(python), "-B", str(Path(__file__).resolve()), *argv], dict(os.environ, **{IN_VENV: "1"}))


# --- helpers -----------------------------------------------------------------------------------------------------------

def parse_arts(text: str) -> list[int]:
    try:
        numbers = [int(part) for part in text.split(",") if part.strip()]
    except ValueError as error:
        raise SystemExit(f"hd_font: --art must be a comma-separated list of interface ART numbers: {error}") from error
    if not numbers or len(set(numbers)) != len(numbers) or any(number < 0 for number in numbers):
        raise SystemExit("hd_font: --art needs one or more distinct non-negative numbers")
    return numbers


def checked_out_dir(path: str) -> Path:
    """The output folder must be `<checkout>/local/hd-font/...` (a checkout is a folder holding `.git`; `local/` is ignored by git).
    That keeps the frames out of git, out of every HD pack, backup and staging folder, and out of the game folder."""
    resolved = Path(path).expanduser().resolve()
    for index, part in enumerate(resolved.parts):
        if part == "local" and index + 1 < len(resolved.parts) and resolved.parts[index + 1] == OUT_ROOT_NAME \
                and (Path(*resolved.parts[:index]) / ".git").exists():
            return resolved
    raise SystemExit(f"hd_font: --out {resolved} must be under <checkout>/local/{OUT_ROOT_NAME}/ (a checkout holds .git; local/ is ignored by git); "
                     "derived font data never goes into git, a pack or the game folder")


def write_file(path: Path, data: bytes | str) -> None:
    """Writes one output file; a symbolic link in the way is refused, never followed."""
    if path.is_symlink():
        raise SystemExit(f"hd_font: {path} is a symbolic link; refusing to write through it")
    if isinstance(data, str):
        path.write_text(data)
    else:
        path.write_bytes(data)


def check_receipt(out: Path) -> None:
    receipt = out / "receipt.json"
    if receipt.is_symlink() or (receipt.exists() and not receipt.is_file()):
        raise SystemExit(f"hd_font: {receipt} is not a plain file")
    if receipt.is_file():
        try:
            ours = json.loads(receipt.read_text()).get("tool") == "hd_font"
        except (OSError, ValueError, AttributeError):
            ours = False
        if not ours:
            raise SystemExit(f"hd_font: {receipt} was not written by this tool; refusing to overwrite it")


def font_dir_name(number: int, file: str) -> str:
    stem = Path(file).stem.lower()
    return f"{number}-{stem}"


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def check_font_folder(folder: Path) -> None:
    """Replace only what this tool wrote: an existing non-empty folder must hold its `font.json`."""
    if folder.is_symlink() or (folder.exists() and not folder.is_dir()):
        raise SystemExit(f"hd_font: {folder} is not a plain folder")
    if folder.is_dir() and any(folder.iterdir()) and not (folder / "font.json").is_file():
        raise SystemExit(f"hd_font: {folder} is not empty and holds no font.json of this tool; refusing to write into it")


# --- commands ----------------------------------------------------------------------------------------------------------

def load_fonts(args: argparse.Namespace):
    import artfont

    game_dir = Path(args.game_dir or os.environ.get("ARCANUM_GAME_DIR") or DEFAULT_GAME_DIR).expanduser()
    try:
        content = artfont.Content(artfont.discover_sources(game_dir, args.module))
        return [artfont.load_font(content, number) for number in parse_arts(args.art)], content
    except artfont.FontError as error:
        raise SystemExit(f"hd_font: {error}") from error


def command_trace(args: argparse.Namespace) -> int:
    """Runs the trace with the tracer parameters of the command line, and puts the tracer's constants back afterwards."""
    if not (0.0 <= args.alpha_max <= 1.3334 and 0.05 <= args.level_cap <= 0.95 and 0.0 <= args.opt_tolerance < float("inf")):
        raise SystemExit("hd_font: --alpha-max must be 0 to 1.3334, --level-cap 0.05 to 0.95 and --opt-tolerance at least 0")
    if args.dry_run:
        return _trace(args)
    import outline as tracer

    saved = (tracer.ALPHA_MAX, tracer.LEVEL_CAP, tracer.OPT_TOLERANCE)
    tracer.ALPHA_MAX, tracer.LEVEL_CAP, tracer.OPT_TOLERANCE = args.alpha_max, args.level_cap, args.opt_tolerance
    try:
        return _trace(args)
    finally:
        tracer.ALPHA_MAX, tracer.LEVEL_CAP, tracer.OPT_TOLERANCE = saved


def _trace(args: argparse.Namespace) -> int:
    out = checked_out_dir(args.out)
    check_receipt(out)
    sys.path.insert(0, str(HERE))
    fonts, content = load_fonts(args)
    log("hd_font: sources (highest priority first): " + "; ".join(f"{source.label}" for source in content.sources))
    for font in fonts:
        folder = out / font_dir_name(font.number, font.file)
        check_font_folder(folder)
        ink = sum(1 for frame in font.frames if any(frame.red))
        log(f"hd_font: art {font.number} {font.file} from {font.source}: {len(font.frames)} frames, {ink} with ink, "
            f"tallest {max(frame.height for frame in font.frames)} rows -> {folder} ({len(font.frames)} PNG at 4x + font.json)")
    if args.dry_run:
        log("hd_font: dry run, nothing written")
        return 0

    import numpy as np
    import outline as tracer

    out.mkdir(parents=True, exist_ok=True)
    receipt_fonts = []
    for font in fonts:
        folder = out / font_dir_name(font.number, font.file)
        check_font_folder(folder)
        folder.mkdir(parents=True, exist_ok=True)
        entries = []
        digest = hashlib.sha256()
        for index, frame in enumerate(font.frames):
            mask = np.frombuffer(frame.red, dtype=np.uint8).reshape(frame.height, frame.width).astype(np.float64) / 255.0
            result = tracer.trace_mask(mask)
            png = tracer.png_bytes(tracer.frame_pixels(result.coverage))
            name = f"frame-{index:03d}.png"
            write_file(folder / name, png)
            digest.update(png)
            entries.append({
                "frame": index, "character": index + 31, "file": name, "width": frame.width, "height": frame.height,
                "centreX": frame.centre_x, "centreY": frame.centre_y, "deltaX": frame.delta_x, "deltaY": frame.delta_y,
                "grey": frame.grey, "ink": bool(result.mass > 0), "nodes": result.nodes, "level": round(result.level, 4),
                "mass": round(result.mass, 4), "tracedMass": round(result.traced_mass, 4),
                "meanAbsError": round(result.mean_abs_error, 5), "vanished": result.vanished, "pngSha256": sha256_hex(png),
            })
        traced = [entry for entry in entries if entry["ink"]]
        summary = {
            "frames": len(entries), "withInk": len(traced),
            "nodesPerGlyph": round(sum(entry["nodes"] for entry in traced) / max(1, len(traced)), 1),
            "meanAbsError": round(sum(entry["meanAbsError"] for entry in traced) / max(1, len(traced)), 5),
            "vanished": [entry["frame"] for entry in traced if entry["vanished"]],
            "lowInk": sum(1 for entry in traced if entry["tracedMass"] < 0.85 * entry["mass"]),
        }
        meta = {
            "tool": "hd_font", "toolVersion": TOOL_VERSION, "scale": 4, "art": font.number, "file": font.file,
            "source": font.source, "artSha256": sha256_hex(font.raw), "framesSha256": digest.hexdigest(),
            "summary": summary, "frames": entries,
        }
        write_file(folder / "font.json", json.dumps(meta, indent=1, sort_keys=True) + "\n")
        receipt_fonts.append({"art": font.number, "file": font.file, "folder": folder.name, "artSha256": meta["artSha256"],
                              "framesSha256": meta["framesSha256"], "summary": summary})
        log(f"hd_font: art {font.number} {font.file}: {summary['withInk']} glyphs traced, {summary['nodesPerGlyph']} nodes each, "
            f"mean error {summary['meanAbsError']}, low ink {summary['lowInk']}, vanished {summary['vanished']}")
    receipt = {"tool": "hd_font", "toolVersion": TOOL_VERSION, "lockSha256": sha256_hex(LOCK.read_bytes()),
               "parameters": {"upsample": tracer.UPSAMPLE, "backProjections": tracer.BACK_PROJECTIONS, "turdSize": tracer.TURD_SIZE,
                              "alphaMax": tracer.ALPHA_MAX, "optTolerance": tracer.OPT_TOLERANCE, "outputScale": tracer.OUTPUT_SCALE,
                              "levelRule": f"mass-preserving, at most {tracer.LEVEL_CAP}"},
               "fonts": receipt_fonts}
    write_file(out / "receipt.json", json.dumps(receipt, indent=1, sort_keys=True) + "\n")
    log(f"hd_font: PASS {len(fonts)} fonts written under {out}")
    return 0


def command_self_test(_args: argparse.Namespace) -> int:
    import unittest

    sys.path.insert(0, str(HERE))
    suite = unittest.defaultTestLoader.loadTestsFromName("test_hd_font")
    result = unittest.TextTestRunner(verbosity=1).run(suite)
    return 0 if result.wasSuccessful() else 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="hd_font.py", description=__doc__.split("\n\n")[0])
    commands = parser.add_subparsers(dest="command", required=True)
    trace = commands.add_parser("trace", help="trace the player's interface fonts into 4x coverage frames under local/hd-font/")
    trace.add_argument("--out", required=True, help="output folder, under <checkout>/local/hd-font/; one sub-folder per font")
    trace.add_argument("--art", default=",".join(str(number) for number in DEFAULT_ARTS),
                       help="comma-separated interface ART numbers (default: 27,229,171,327,840)")
    trace.add_argument("--game-dir", help=f"the player's game folder (default: $ARCANUM_GAME_DIR or {DEFAULT_GAME_DIR})")
    trace.add_argument("--module", default=DEFAULT_MODULE, help="module whose archive and patches are mounted (default: Arcanum)")
    trace.add_argument("--alpha-max", type=float, default=ALPHA_MAX_DEFAULT,
                       help=f"Potrace corner threshold: 0 keeps every corner sharp, 1.33 rounds all (default: {ALPHA_MAX_DEFAULT})")
    trace.add_argument("--level-cap", type=float, default=LEVEL_CAP_DEFAULT,
                       help=f"upper bound of the mass-preserving binarization level, 0.05 to 0.95 (default: {LEVEL_CAP_DEFAULT})")
    trace.add_argument("--opt-tolerance", type=float, default=OPT_TOLERANCE_DEFAULT,
                       help=f"Potrace curve-optimization tolerance (default: {OPT_TOLERANCE_DEFAULT})")
    trace.add_argument("--dry-run", action="store_true", help="read the sources and fonts and print the plan; write nothing")
    trace.set_defaults(handler=command_trace, needs_venv=True)
    selftest = commands.add_parser("self-test", help="run the synthetic-fixture tests inside the locked venv")
    selftest.set_defaults(handler=command_self_test, needs_venv=True)
    return parser


def main(argv: list[str] | None = None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    args = build_parser().parse_args(argv)
    if args.needs_venv and not getattr(args, "dry_run", False):
        ensure_venv(argv)
    sys.path.insert(0, str(HERE))
    return args.handler(args)


if __name__ == "__main__":
    raise SystemExit(main())
