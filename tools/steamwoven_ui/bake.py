"""ARC-DEV-2460: the bake of the Steamwoven kit (TRD SWI-RUN-1, SWI-VER-4): masters -> the four step sets x1, x2/3, x1/2, x1/3.

    python3 -B tools/steamwoven_ui/bake.py bake   [--masters DIR] [--out DIR] [--steps x1,x2-3,x1-2,x1-3] [--only ID,...] [--prune] [--dry-run] [--no-kit]
    python3 -B tools/steamwoven_ui/bake.py verify [--masters DIR] [--out DIR] [--steps ...]
    python3 -B tools/steamwoven_ui/bake.py self-test

A master is a PNG (RGBA, 8 bit, sRGB, straight alpha, sides multiples of 6 px; `png_io.py`) at `<masters>/<part id>.png`; the baked set of a
step is `<out>/<step>/<part id>.png` plus `<out>/<step>/manifest.json`. Defaults: `local/steamwoven-ui/masters` and `local/steamwoven-ui/baked` of the
main checkout (ignored by git; the kit is derived content, SWI-PRV-1; storage and backup: docs/workflow/toolchain.md "Steamwoven kit bake").

The resample (standard library only, exact integer arithmetic):
  * step a/b (x1 = 1/1, x2/3 = 2/3, x1/2 = 1/2, x1/3 = 1/3): every baked length is the master length times the step, exactly (the master side must
    be a multiple of b, which the 6 px grid guarantees); x1 is the master copied pixel for pixel;
  * each output pixel is the exact area average of the master pixels it covers (weights are integers, no float in the sums);
  * sRGB 8 bit -> linear light (16 bit table) -> premultiplied by alpha, averaged, un-premultiplied, -> sRGB 8 bit: no dark fringe, no colour
    bleeding out of transparent pixels; a pixel that ends fully transparent has colour 0 so its bytes do not depend on hidden colour.
Same masters give the same bytes (the PNG writer is canonical; the manifest also holds a hash of the raw pixels, which does not depend on zlib).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
from dataclasses import dataclass, field

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tools.steamwoven_ui import png_io  # noqa: E402
from tools.steamwoven_ui import prompt_library  # noqa: E402

KIT = Path(__file__).resolve().parent / "kit.json"
BAKE_VERSION = 1  # bump with any change of the resample or the PNG writer: reused files of an older version are rebaked
MANIFEST_SCHEMA = 1
STEPS = {"x1": (1, 1), "x2-3": (2, 3), "x1-2": (1, 2), "x1-3": (1, 3)}  # name -> (a, b): the step is a / b
GRID = 6  # SWI-LAY-7
LOCK_NAME = ".bake.lock"


def _srgb_to_linear16(code: int) -> int:
    c = code / 255.0
    lin = c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return int(lin * 65535.0 + 0.5)


def _linear16_to_srgb(value: int) -> int:
    lin = value / 65535.0
    c = lin * 12.92 if lin <= 0.0031308 else 1.055 * lin ** (1 / 2.4) - 0.055
    return min(255, int(c * 255.0 + 0.5))


LIN16 = tuple(_srgb_to_linear16(i) for i in range(256))
SRGB8 = tuple(_linear16_to_srgb(i) for i in range(65536))


def step_taps(a: int, b: int) -> list[list[tuple[int, int]]]:
    """For each output phase 0..a-1 the (source offset in 0..b-1, integer weight) pairs; the weights of a phase sum to b.

    Output pixel i covers the source interval [i*b/a, (i+1)*b/a); in units of 1/a source px that is [i*b, (i+1)*b) and source pixel k is [k*a, (k+1)*a).
    """
    taps: list[list[tuple[int, int]]] = []
    for phase in range(a):
        lo, hi = phase * b, (phase + 1) * b
        row = [(k, min(hi, (k + 1) * a) - max(lo, k * a)) for k in range(b) if min(hi, (k + 1) * a) > max(lo, k * a)]
        taps.append(row)
    return taps


def baked_size(width: int, height: int, step: str) -> tuple[int, int]:
    """The exact baked size, or ValueError when a side is not a multiple of the step's denominator."""
    a, b = STEPS[step]
    if width % b or height % b:
        raise ValueError(f"{width} x {height} is not a multiple of {b} px per side")
    return width * a // b, height * a // b


def _combine(columns: list[list[int]], weights: list[int]) -> list[int]:
    if len(columns) == 1:
        w = weights[0]
        return [w * x for x in columns[0]]
    if len(columns) == 2:
        w0, w1 = weights
        return [w0 * x + w1 * y for x, y in zip(*columns)]
    return [sum(w * x for w, x in zip(weights, group)) for group in zip(*columns)]


def _horizontal(row: bytes, a: int, b: int, taps: list[list[tuple[int, int]]]) -> list[list[int]]:
    """One source row -> four lists (premultiplied linear R, G, B and alpha), each `width * a / b` long, weights not yet divided."""
    alpha = list(row[3::4])
    channels = [[LIN16[v] * al for v, al in zip(row[c::4], alpha)] for c in range(3)] + [alpha]
    if (a, b) == (1, 1):
        return channels
    out_len = len(alpha) * a // b
    result = []
    for values in channels:
        out = [0] * out_len
        for phase, phase_taps in enumerate(taps):
            out[phase::a] = _combine([values[k::b] for k, _ in phase_taps], [w for _, w in phase_taps])
        result.append(out)
    return result


def _finish_row(sums: list[list[int]], total: int) -> bytes:
    """Four weighted sums (R, G, B premultiplied linear; A) of one output row -> straight sRGB RGBA bytes."""
    sum_a = sums[3]
    alpha = [(2 * s + total) // (2 * total) for s in sum_a]
    out = bytearray(len(alpha) * 4)
    out[3::4] = bytes(alpha)
    for c in range(3):
        out[c::4] = bytes(SRGB8[(2 * p + s) // (2 * s)] if al else 0 for p, s, al in zip(sums[c], sum_a, alpha))
    return bytes(out)


def resample(image: png_io.Image, step: str) -> png_io.Image:
    """The image at the step (see the module doc). x1 returns the same pixels."""
    a, b = STEPS[step]
    out_w, out_h = baked_size(image.width, image.height, step)
    if (a, b) == (1, 1):
        return png_io.Image(image.width, image.height, list(image.rows))
    taps = step_taps(a, b)
    total = b * b
    rows: list[bytes] = []
    for block in range(image.height // b):
        horizontal = [_horizontal(image.rows[block * b + k], a, b, taps) for k in range(b)]
        for phase_taps in taps:
            sums = [_combine([horizontal[k][c] for k, _ in phase_taps], [w for _, w in phase_taps]) for c in range(4)]
            rows.append(_finish_row(sums, total))
    return png_io.Image(out_w, out_h, rows)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


# --- masters, kit, plan --------------------------------------------------------------------------------------------------------------


@dataclass
class Report:
    ok: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    lines: list[str] = field(default_factory=list)


def find_masters(masters: Path, only: set[str] | None = None) -> dict[str, Path]:
    """Part id -> master file: every `*.png` below `masters`, the id being the relative path without the suffix, with `/` separators."""
    found: dict[str, Path] = {}
    if not masters.is_dir():
        return found
    for path in sorted(masters.rglob("*.png")):
        if any(part.startswith(".") for part in path.relative_to(masters).parts):
            continue
        part_id = path.relative_to(masters).with_suffix("").as_posix()
        if only is None or part_id in only:
            found[part_id] = path
    return found


def load_kit_sizes(kit: Path = KIT) -> dict[str, tuple[list[int] | None, str]]:
    data = json.loads(kit.read_text(encoding="utf-8"))
    return {part["id"]: (part.get("size"), part.get("size_basis")) for part in data["parts"]}


def check_against_kit(part_id: str, size: tuple[int, int], kit: dict[str, tuple[list[int] | None, str]] | None, report: Report) -> bool:
    """False (an error) when the id is not a kit part or a master of `units` basis has another size; `provisional` only warns."""
    if kit is None:
        return True
    if part_id not in kit:
        report.errors.append(f"{part_id}: not a part of the kit manifest (use --no-kit for a scratch master)")
        return False
    kit_size, basis = kit[part_id]
    if kit_size is not None and list(size) != list(kit_size):
        message = f"{part_id}: master {size[0]} x {size[1]} differs from the manifest's {kit_size[0]} x {kit_size[1]} ({basis})"
        if basis == "units":
            report.errors.append(message)
            return False
        report.warnings.append(message)
    return True


def set_dir(out: Path, step: str) -> Path:
    return out / step


def read_manifest(directory: Path) -> dict | None:
    try:
        data = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return data if isinstance(data, dict) and data.get("schema") == MANIFEST_SCHEMA else None


def _write_atomic(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temp_name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(handle, "wb") as file:
            file.write(data)
        os.replace(temp_name, path)
    except BaseException:
        try:
            os.unlink(temp_name)
        except OSError:
            pass
        raise


def manifest_bytes(step: str, parts: list[dict]) -> bytes:
    a, b = STEPS[step]
    body = {"schema": MANIFEST_SCHEMA, "bake_version": BAKE_VERSION, "step": step, "ratio": [a, b], "parts": parts}
    body["set_sha256"] = sha256(json.dumps(parts, sort_keys=True, separators=(",", ":")).encode())
    return (json.dumps(body, indent=1, sort_keys=True) + "\n").encode()


def bake_set(masters: dict[str, Path], everything: set[str], out: Path, step: str, prune: bool, dry_run: bool, kit: dict | None, report: Report) -> None:
    """Bake one step. A part whose master hash, ratio and bake version are unchanged and whose file is intact is reused, not rebaked."""
    directory = set_dir(out, step)
    previous = read_manifest(directory) if directory.is_dir() else None
    old_parts = {p["id"]: p for p in previous["parts"]} if previous and previous.get("bake_version") == BAKE_VERSION else {}
    all_old = {p["id"]: p for p in previous["parts"]} if previous else {}
    parts: list[dict] = []
    for part_id, path in sorted(masters.items()):
        try:
            master_bytes = path.read_bytes()
            master_hash = sha256(master_bytes)
            old = old_parts.get(part_id)
            target = directory / f"{part_id}.png"
            if old and old["master"]["sha256"] == master_hash and target.is_file() and sha256(target.read_bytes()) == old["sha256"]:
                parts.append(old)
                report.lines.append(f"{step} {part_id}: unchanged")
                continue
            image = png_io.decode(master_bytes)
            if image.width % GRID or image.height % GRID:
                raise ValueError(f"master {image.width} x {image.height} is not a multiple of {GRID} px per side (SWI-KIT-2)")
            if not check_against_kit(part_id, (image.width, image.height), kit, report):
                continue
            size = baked_size(image.width, image.height, step)
            if dry_run:
                report.lines.append(f"{step} {part_id}: would bake {image.width} x {image.height} -> {size[0]} x {size[1]}")
                continue
            baked = resample(image, step)
            data = png_io.encode(baked)
            _write_atomic(target, data)
            parts.append({"id": part_id, "master": {"size": [image.width, image.height], "sha256": master_hash}, "size": [baked.width, baked.height],
                          "bytes": len(data), "sha256": sha256(data), "pixels_sha256": sha256(baked.pixels())})
            report.lines.append(f"{step} {part_id}: {image.width} x {image.height} -> {baked.width} x {baked.height}")
        except (png_io.PngError, ValueError, OSError) as exc:
            report.errors.append(f"{step} {part_id}: {exc}")
    if dry_run:
        return
    have = {p["id"] for p in parts}
    for part_id, old in all_old.items():  # a part not baked this time (filtered out or failed) keeps its record so the manifest stays true to the files
        stale = directory / f"{part_id}.png"
        if part_id in have or not stale.is_file():
            continue
        if prune and part_id not in everything and sha256(stale.read_bytes()) == old.get("sha256"):
            stale.unlink()
            report.lines.append(f"{step} {part_id}: pruned (its master is gone)")
            continue
        parts.append(old)
    parts.sort(key=lambda p: p["id"])
    if parts or previous:
        _write_atomic(directory / "manifest.json", manifest_bytes(step, parts))
    report.ok.append(step)


class Lock:
    """`<out>/.bake.lock` holds the pid; a lock of a dead process is taken over, one of a live process means wait."""

    def __init__(self, out: Path) -> None:
        self.path = out / LOCK_NAME

    def __enter__(self) -> "Lock":
        self.path.parent.mkdir(parents=True, exist_ok=True)
        for _ in range(2):
            try:
                handle = os.open(self.path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            except FileExistsError:
                try:
                    pid = int(self.path.read_text().strip() or "0")
                    os.kill(pid, 0)
                except (ValueError, ProcessLookupError):
                    self.path.unlink(missing_ok=True)
                    continue
                except PermissionError:
                    pass
                raise RuntimeError(f"another bake holds {self.path}; wait for it") from None
            os.write(handle, str(os.getpid()).encode())
            os.close(handle)
            return self
        raise RuntimeError(f"cannot take {self.path}")

    def __exit__(self, *exc) -> None:
        self.path.unlink(missing_ok=True)


def bake(masters_dir: Path, out: Path, steps: list[str], only: set[str] | None = None, prune: bool = False, dry_run: bool = False,
         use_kit: bool = True) -> Report:
    report = Report()
    everything = set(find_masters(masters_dir))
    masters = find_masters(masters_dir, only)
    if not masters:
        report.errors.append(f"no masters found under {masters_dir}")
        return report
    if only is not None:
        for missing in sorted(only - masters.keys()):
            report.errors.append(f"{missing}: no master file")
    kit = load_kit_sizes() if use_kit else None
    if dry_run:
        for step in steps:
            bake_set(masters, everything, out, step, prune, True, kit, report)
        return report
    try:
        with Lock(out):
            for step in steps:
                bake_set(masters, everything, out, step, prune, False, kit, report)
    except RuntimeError as exc:
        report.errors.append(str(exc))
    return report


def verify(masters_dir: Path, out: Path, steps: list[str]) -> Report:
    """Every part of each set manifest: the file exists with the recorded hash, its size is the master's times the step, and the master is the one baked."""
    report = Report()
    for step in steps:
        directory = set_dir(out, step)
        manifest = read_manifest(directory)
        if manifest is None:
            report.errors.append(f"{step}: no manifest in {directory}")
            continue
        a, b = STEPS[step]
        if manifest.get("step") != step or manifest.get("ratio") != [a, b]:
            report.errors.append(f"{step}: manifest says step {manifest.get('step')} ratio {manifest.get('ratio')}")
        expected_set = sha256(json.dumps(manifest["parts"], sort_keys=True, separators=(",", ":")).encode())
        if manifest.get("set_sha256") != expected_set:
            report.errors.append(f"{step}: set_sha256 does not match the part list")
        for part in manifest["parts"]:
            name = f"{step} {part['id']}"
            path = directory / f"{part['id']}.png"
            try:
                data = path.read_bytes()
            except OSError:
                report.errors.append(f"{name}: file is missing")
                continue
            if sha256(data) != part["sha256"] or len(data) != part["bytes"]:
                report.errors.append(f"{name}: file differs from the manifest")
                continue
            try:
                image = png_io.decode(data)
            except png_io.PngError as exc:
                report.errors.append(f"{name}: {exc}")
                continue
            mw, mh = part["master"]["size"]
            try:
                want = baked_size(mw, mh, step)
            except ValueError as exc:
                report.errors.append(f"{name}: {exc}")
                continue
            if (image.width, image.height) != want or list(want) != part["size"]:
                report.errors.append(f"{name}: size {image.width} x {image.height}, expected {want[0]} x {want[1]} ({mw} x {mh} times {a}/{b})")
            if sha256(image.pixels()) != part["pixels_sha256"]:
                report.errors.append(f"{name}: pixels differ from the manifest")
            master = masters_dir / f"{part['id']}.png"
            if master.is_file() and sha256(master.read_bytes()) != part["master"]["sha256"]:
                report.warnings.append(f"{name}: the master changed since the bake (rebake)")
        report.ok.append(step)
    return report


# --- self test and CLI ---------------------------------------------------------------------------------------------------------------


def _synthetic_master(width: int, height: int) -> png_io.Image:
    rows = []
    for y in range(height):
        row = bytearray()
        for x in range(width):
            inside = (x * 3 // width + y * 2 // height) % 2
            row += bytes((200 if inside else 20, (x * 255) // max(1, width - 1), (y * 255) // max(1, height - 1), 255 if (x + y) % 5 else 0))
        rows.append(bytes(row))
    return png_io.Image(width, height, rows)


def self_test() -> list[str]:
    """Synthetic masters only (no game file); returns the failures."""
    failures: list[str] = []
    master = _synthetic_master(36, 24)
    for step, (a, b) in STEPS.items():
        out = resample(master, step)
        want = (36 * a // b, 24 * a // b)
        if (out.width, out.height) != want:
            failures.append(f"{step}: size {out.width} x {out.height}, expected {want}")
        if png_io.encode(out) != png_io.encode(resample(master, step)):
            failures.append(f"{step}: two runs differ")
    if png_io.decode(png_io.encode(master)).rows != master.rows:
        failures.append("png round trip")
    if any(SRGB8[LIN16[i]] != i for i in range(256)):
        failures.append("sRGB table round trip")
    return failures


def _steps(text: str) -> list[str]:
    names = [t.strip() for t in text.split(",") if t.strip()]
    unknown = [n for n in names if n not in STEPS]
    if unknown:
        raise argparse.ArgumentTypeError(f"unknown step {unknown}; steps are {','.join(STEPS)}")
    return names


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("bake", "verify"):
        p = sub.add_parser(name)
        p.add_argument("--masters", type=Path)
        p.add_argument("--out", type=Path)
        p.add_argument("--steps", type=_steps, default=list(STEPS))
        if name == "bake":
            p.add_argument("--only", help="comma-separated part ids")
            p.add_argument("--prune", action="store_true", help="delete baked files whose master is gone (only files this bake wrote)")
            p.add_argument("--dry-run", action="store_true")
            p.add_argument("--no-kit", action="store_true", help="do not check ids and sizes against kit.json (scratch masters)")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    if args.command == "self-test":
        failures = self_test()
        for line in failures:
            print(f"FAIL {line}")
        print("BAKE self-test " + ("FAIL" if failures else "PASS"))
        return 1 if failures else 0
    local = prompt_library.default_local_root()
    masters = args.masters or local / "masters"
    out = args.out or local / "baked"
    if args.command == "verify":
        report = verify(masters, out, args.steps)
    else:
        only = {t.strip() for t in args.only.split(",") if t.strip()} if args.only else None
        report = bake(masters, out, args.steps, only, args.prune, args.dry_run, not args.no_kit)
    for line in report.lines:
        print(line)
    for line in report.warnings:
        print(f"WARN {line}")
    for line in report.errors:
        print(f"ERROR {line}")
    label = "BAKE " + args.command + (" dry-run" if getattr(args, "dry_run", False) else "")
    print(f"{label} {'FAIL' if report.errors else 'PASS'} (sets {len(report.ok)}, warnings {len(report.warnings)}, errors {len(report.errors)})")
    return 1 if report.errors else 0


if __name__ == "__main__":
    sys.exit(main())
