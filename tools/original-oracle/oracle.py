#!/usr/bin/env python3
"""Function oracle for the retail 1.0.7.4 Arcanum.exe (ARC-DEV-007).

Runs single functions of the unchanged executable in Unicorn and writes seeded inputs with the original's outputs to
a local folder outside git, for the OriginalExe tests (docs/research/original-executable-check.md#function-emulation-oracle).

    python3 -B tools/original-oracle/oracle.py --self-test
    python3 -B tools/original-oracle/oracle.py generate --all [--exe PATH] [--out DIR] [--seed N] [--count N]
    python3 -B tools/original-oracle/oracle.py list

Unicorn comes from requirements.lock (exact version and hashes) in a private virtual environment, created on first use
under local/original-oracle/venv (or ARCANUM_ORACLE_VENV).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import venv

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
LOCK = HERE / "requirements.lock"
UNICORN_VERSION = "2.1.4"
DEFAULT_SEED = 20261017
MIN_COUNT = 10_000
IN_VENV = "ARCANUM_ORACLE_IN_VENV"
sys.path.insert(0, str(HERE))

import oracle_functions  # noqa: E402  (no Unicorn needed)
import oracle_pe  # noqa: E402


def default_out() -> Path:
    return Path(os.environ.get("ARCANUM_ORACLE_DIR") or REPO / "research-private" / "oracle")


def checked_out_dir(path: Path) -> Path:
    """The output folder, refused when it lies in the work tree outside the ignored research-private/ or local/."""
    resolved = path.expanduser().resolve()
    if resolved == REPO or REPO in resolved.parents:
        if not any(resolved == REPO / allowed or (REPO / allowed) in resolved.parents for allowed in ("research-private", "local")):
            raise ValueError(f"{path} is inside the work tree; generated inputs stay out of git (research-private/ or local/)")
    return resolved


# --- pinned Unicorn --------------------------------------------------------------------------------------------------

def _unicorn_ready() -> bool:
    try:
        import unicorn
    except ImportError:
        return False
    return getattr(unicorn, "__version__", None) == UNICORN_VERSION


def ensure_unicorn(argv: list[str]) -> None:
    """Continue when the pinned Unicorn is importable, else (re)create the private venv from the lock and re-run there."""
    if _unicorn_ready():
        return
    if os.environ.get(IN_VENV):
        raise SystemExit(f"oracle: Unicorn {UNICORN_VERSION} is not importable in {sys.prefix}")
    environment = Path(os.environ.get("ARCANUM_ORACLE_VENV") or REPO / "local" / "original-oracle" / "venv")
    python = environment / "bin" / "python3"
    stamp = environment / "lock.sha256"
    lock_sha = hashlib.sha256(LOCK.read_bytes()).hexdigest()
    if not python.exists() or not stamp.exists() or stamp.read_text().strip() != lock_sha:
        print(f"oracle: installing Unicorn {UNICORN_VERSION} from {LOCK.relative_to(REPO)} into {environment}", file=sys.stderr)
        venv.EnvBuilder(clear=True, with_pip=True).create(environment)
        install = [str(python), "-m", "pip", "install", "--quiet", "--disable-pip-version-check", "--no-input",
                   "--require-hashes", "--only-binary=:all:", "--no-deps", "-r", str(LOCK)]
        if subprocess.call(install) != 0:
            raise SystemExit("oracle: installing the locked Unicorn failed (network or an unsupported platform)")
        stamp.write_text(lock_sha + "\n")
    os.execve(str(python), [str(python), "-B", str(Path(__file__).resolve()), *argv], dict(os.environ, **{IN_VENV: "1"}))


# --- commands --------------------------------------------------------------------------------------------------------

def generate(args: argparse.Namespace) -> int:
    from oracle_emu import Emulator, RunFailure

    if args.count < MIN_COUNT:
        raise ValueError(f"--count must be at least {MIN_COUNT}")
    exe = args.exe or os.environ.get("ARCANUM_EXE")
    if not exe:
        raise ValueError("set ARCANUM_EXE or pass --exe (the installed Arcanum.exe)")
    out = checked_out_dir(Path(args.out) if args.out else default_out())
    image = oracle_pe.load(Path(exe))
    addresses = list(oracle_functions.FUNCTIONS) if args.all or not args.function else [oracle_functions.parse_address(a) for a in args.function]
    out.mkdir(parents=True, exist_ok=True)
    for address in addresses:
        spec = oracle_functions.FUNCTIONS[address]
        for start, end in spec.code:
            section = image.section_of(start)
            if section is None or section.name != ".text" or end > section.end:
                raise ValueError(f"{spec.key}: code range {start:#x}..{end:#x} is not inside .text")
        started = time.monotonic()
        try:
            document = oracle_functions.run(spec, Emulator(image), args.seed, args.count, exe_sha256=image.sha256,
                                            unicorn_version=UNICORN_VERSION)
        except RunFailure as failure:
            print(f"oracle: {spec.key} {spec.name}: run failed ({failure})", file=sys.stderr)
            return 1
        target = out / f"{spec.key}.json"
        with tempfile.NamedTemporaryFile("w", dir=out, delete=False, suffix=".tmp") as stream:
            json.dump(document, stream, separators=(",", ":"))
        os.replace(stream.name, target)
        print(f"oracle: 0x{spec.key} {spec.name}: {args.count} inputs, seed {args.seed}, exe {image.sha256}, "
              f"{time.monotonic() - started:.1f} s -> {target}")
    return 0


def list_functions(_: argparse.Namespace) -> int:
    for spec in oracle_functions.FUNCTIONS.values():
        print(f"0x{spec.key} {spec.name}")
    return 0


def self_test(_: argparse.Namespace) -> int:
    import oracle_selftest
    return oracle_selftest.main()


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(prog="oracle.py", description=__doc__.splitlines()[0])
    result.add_argument("--self-test", action="store_true", help="test the oracle on synthetic code (no executable needed)")
    commands = result.add_subparsers(dest="command")
    command = commands.add_parser("generate", help="write seeded inputs and the original's outputs")
    which = command.add_mutually_exclusive_group()
    which.add_argument("--function", action="append", metavar="ADDR", help="hex address, e.g. 0x4A68B0 (repeatable)")
    which.add_argument("--all", action="store_true", help="every oracle function (the default)")
    command.add_argument("--exe", help="the installed Arcanum.exe (default: $ARCANUM_EXE)")
    command.add_argument("--out", help="output folder (default: $ARCANUM_ORACLE_DIR or research-private/oracle)")
    command.add_argument("--seed", type=int, default=DEFAULT_SEED)
    command.add_argument("--count", type=int, default=MIN_COUNT)
    command.set_defaults(func=generate)
    commands.add_parser("list", help="the oracle functions").set_defaults(func=list_functions)
    return result


def main(argv: list[str]) -> int:
    args = parser().parse_args(argv)
    if args.self_test:
        args.func = self_test
    elif not args.command:
        parser().error("choose --self-test or a command")
    if args.func is not list_functions:
        ensure_unicorn(argv)
    try:
        return args.func(args)
    except (ValueError, oracle_pe.RefusedExecutable) as error:
        print(f"oracle: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
