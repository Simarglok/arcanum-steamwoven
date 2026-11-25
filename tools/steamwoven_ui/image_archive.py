#!/usr/bin/env python3
"""Per-task image archive of the main checkout (ARC-DEV-3277).

Every image a task produces (raw Codex outputs, collages, fitted masters, composites, screenshots) is kept under
`local/steamwoven-ui/archive/<TASK>/` of the MAIN checkout (never a worktree: removing a worktree must lose nothing),
the accepted version also under `<TASK>/final/`, and each file has one row in `archive/registry.jsonl`
(task, file, kind, status, sources, hash, size, date). Nothing here is committed: the images are derived from the
player's own data (SWI-PRV-1). Standard library only.

    image_archive.py dir ARC-UI-3040                 # create and print the task folder (use it as --out of any tool)
    image_archive.py add ARC-UI-3040 A.png B.png --kind composite [--status unknown] [--note TEXT]
    image_archive.py mark ARC-UI-3040 composite/A.png final    # status change; `final` also copies into final/
    image_archive.py scan [TASK]                     # register files copied into archive/<TASK>/ by hand
    image_archive.py index [TASK]                    # counts per task, kind and status
    image_archive.py verify                          # every row's file exists and still has its hash
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from datetime import date, datetime
from pathlib import Path

try:
    from . import prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import prompt_library  # type: ignore

KINDS = ("raw-codex", "collage", "master", "composite", "screenshot", "reference", "unknown")
STATUSES = ("final", "rejected", "intermediate", "unknown")
IMAGE_EXTS = (".png", ".jpg", ".jpeg", ".webp", ".gif")
TASK_RE = re.compile(r"(ARC-[A-Z]+-\d+(?:-[A-Za-z0-9]+)?|_unsorted/(?!\.+$)[A-Za-z0-9._-]+)")
FINAL_DIR = "final"
REGISTRY_NAME = "registry.jsonl"


def archive_root(local_root: Path | None = None) -> Path:
    """`local/steamwoven-ui/archive` of the main checkout (a worktree has no `local/` of its own)."""
    return (local_root or prompt_library.default_local_root()) / "archive"


def check_task(task: str) -> str:
    if not isinstance(task, str) or not TASK_RE.fullmatch(task):
        raise ValueError(f"task {task!r} is not ARC-<TYPE>-<number> or _unsorted/<bucket>")
    return task


def task_dir(task: str, *, create: bool = True, root: Path | None = None) -> Path:
    path = (root or archive_root()) / check_task(task)
    if create:
        path.mkdir(parents=True, exist_ok=True)
    return path


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def image_size(path: Path) -> list[int] | None:
    """[width, height] of a PNG (header only); None for any other format."""
    try:
        return list(prompt_library.png_size(path))
    except (ValueError, OSError):
        return None


def clone_copy(src: Path, dst: Path) -> None:
    """Copy `src` to `dst` (copy-on-write clone on APFS, a plain copy elsewhere); never moves or alters `src`."""
    dst.parent.mkdir(parents=True, exist_ok=True)
    part = dst.with_name(dst.name + f".part{os.getpid()}")  # an interrupted copy leaves no half file under the real name
    try:
        done = subprocess.run(["cp", "-c", "-p", str(src), str(part)], capture_output=True) if sys.platform == "darwin" else None
        if done is None or done.returncode != 0:
            shutil.copy2(src, part)
        os.replace(part, dst)
    finally:
        part.unlink(missing_ok=True)


# ---------------------------------------------------------------- the registry

class Registry:
    """The rows of `registry.jsonl`, keyed by (location, task, file)."""

    def __init__(self, root: Path | None = None) -> None:
        self.root = root or archive_root()
        self.path = self.root / REGISTRY_NAME
        self.rows: dict[tuple[str, str, str], dict] = {}
        if self.path.is_file():
            for line in self.path.read_text(encoding="utf-8").splitlines():
                if line.strip():
                    row = json.loads(line)
                    self.rows[self.key(row)] = row

    @staticmethod
    def key(row: dict) -> tuple[str, str, str]:
        return (row.get("location", "archive"), row["task"], row["file"])

    def by_hash(self, task: str, digest: str) -> dict | None:
        for row in self.rows.values():
            if row["task"] == task and row["sha256"] == digest and row.get("location", "archive") == "archive" and not row["file"].startswith(FINAL_DIR + "/"):
                return row
        return None

    def put(self, row: dict) -> dict:
        self.rows[self.key(row)] = row
        return row

    def save(self) -> None:
        """Write the registry under a lock; rows another session wrote since this one loaded are kept (this one wins on a shared key)."""
        self.root.mkdir(parents=True, exist_ok=True)
        with (self.root / "registry.lock").open("a") as lock:
            try:
                import fcntl
                fcntl.flock(lock, fcntl.LOCK_EX)
            except ImportError:  # no flock (Windows): single writer assumed
                pass
            if self.path.is_file():
                for line in self.path.read_text(encoding="utf-8").splitlines():
                    if line.strip():
                        row = json.loads(line)
                        self.rows.setdefault(self.key(row), row)
            body = "".join(json.dumps(r, sort_keys=True, ensure_ascii=False) + "\n" for _, r in sorted(self.rows.items()))
            tmp = self.path.with_name(f"{REGISTRY_NAME}.{os.getpid()}.tmp")
            tmp.write_text(body, encoding="utf-8")
            os.replace(tmp, self.path)


def _row(task, rel, kind, status, sources, digest, size_bytes, size, mtime, note, basis, location="archive"):
    if kind not in KINDS:
        raise ValueError(f"kind {kind!r} not in {KINDS}")
    if status not in STATUSES:
        raise ValueError(f"status {status!r} not in {STATUSES}")
    return {
        "task": task, "file": rel, "location": location, "kind": kind, "status": status,
        "sources": sorted(set(sources)), "sha256": digest, "bytes": size_bytes, "size": size,
        "date": datetime.fromtimestamp(mtime).date().isoformat(), "registered": date.today().isoformat(),
        "note": note, "basis": basis,
    }


def add_file(registry: Registry, task: str, src: Path, *, kind: str, status: str = "unknown", note: str = "",
             subdir: str = "", basis: dict | None = None, name: str | None = None) -> dict:
    """Copy `src` into `archive/<task>/<kind>/[subdir/]name` and register it. The same content added twice to one
    task adds the extra source to the existing row; a different file with a taken name gets the hash in its name."""
    check_task(task)
    digest = sha256_file(src)
    existing = registry.by_hash(task, digest)
    if existing is not None:
        existing["sources"] = sorted(set(existing["sources"]) | {str(src)})
        stored = registry.root / task / existing["file"]
        if not stored.is_file():
            clone_copy(src, stored)
        if status != "unknown" and existing["status"] != status:
            existing["status"] = status
        if kind != "unknown" and existing["kind"] == "unknown":
            existing["kind"] = kind
        return existing
    folder = Path(kind) / subdir if subdir else Path(kind)
    rel = folder / (name or src.name)
    target = registry.root / task / rel
    if target.exists():
        rel = folder / f"{rel.stem}-{digest[:8]}{rel.suffix}"
        target = registry.root / task / rel
    clone_copy(src, target)
    st = src.stat()
    row = _row(task, rel.as_posix(), kind, status, [str(src)], digest, st.st_size, image_size(src), st.st_mtime, note, basis or {})
    return registry.put(row)


def mark(registry: Registry, task: str, rel: str, status: str) -> dict:
    """Change a row's status; `final` also puts a copy in `<task>/final/` and registers it."""
    row = registry.rows.get(("archive", task, rel))
    if row is None:
        raise KeyError(f"{task}/{rel} is not in the registry")
    if rel.startswith(FINAL_DIR + "/"):
        raise ValueError(f"{rel} is already a copy in {FINAL_DIR}/; mark the original row")
    if status not in STATUSES:
        raise ValueError(f"status {status!r} not in {STATUSES}")
    row["status"] = status
    if status == "final":
        src = registry.root / task / rel
        final_rel = f"{FINAL_DIR}/{Path(rel).name}"
        taken = registry.root / task / final_rel
        if taken.exists() and sha256_file(taken) != row["sha256"]:  # another accepted file of the same name
            final_rel = f"{FINAL_DIR}/{Path(rel).stem}-{row['sha256'][:8]}{Path(rel).suffix}"
        clone_copy(src, registry.root / task / final_rel)
        copy = dict(row, file=final_rel, sources=[str(src)], registered=date.today().isoformat())
        registry.put(copy)
    return row


def guess_kind(path: str) -> str:
    """Kind from the path and file name (a heuristic; rows record `kind_basis: heuristic`)."""
    low = path.lower()
    name = low.rsplit("/", 1)[-1]
    if "generated_images" in low or "/codex-work/" in low or "/outbox/" in low:
        return "raw-codex"
    if "/masters/" in low:
        return "master"
    if re.search(r"(shot|screen|capture|smoke|frame|ingame|in-game|viewer|(^|[/_-])cap([/_-]|\.))", low):
        return "screenshot"
    if re.search(r"(sheet|collage|contact|montage|mosaic|grid)", name):
        return "collage"
    if re.search(r"(before|after|cmp|compare|composite|(^|[_-])vs([_-]|\.)|variant|ctx|row|-all|_all|final)", name):
        return "composite"
    return "unknown"


def scan(registry: Registry, task: str | None = None) -> int:
    """Register files that were put into `archive/<task>/` by hand (not through `add`): kind from the folder name
    or the heuristic, status `final` inside `final/`, else `unknown`. Returns the number of new rows."""
    new = 0
    for tdir in sorted(p for p in registry.root.glob("*") if p.is_dir()) + sorted(registry.root.glob("_unsorted/*")):
        rel_task = tdir.relative_to(registry.root).as_posix()
        if rel_task == "_unsorted" or (task and rel_task != task) or not TASK_RE.fullmatch(rel_task):
            continue
        for path in sorted(tdir.rglob("*")):
            if not path.is_file() or path.suffix.lower() not in IMAGE_EXTS:
                continue
            rel = path.relative_to(tdir).as_posix()
            if ("archive", rel_task, rel) in registry.rows:
                continue
            first = rel.split("/", 1)[0]
            kind = first if first in KINDS else guess_kind(rel)
            st = path.stat()
            registry.put(_row(rel_task, rel, kind, "final" if first == FINAL_DIR else "unknown", [str(path)], sha256_file(path),
                              st.st_size, image_size(path), st.st_mtime, "found in the folder by scan", {"kind_basis": "folder" if first in KINDS else "heuristic"}))
            new += 1
    return new


# --------------------------------------------------------------- the out-path guard

def ephemeral_kind(path: Path, main: Path | None = None) -> str | None:
    """"worktree" when `path` lies in a git worktree other than the main checkout, "temp" in a temp folder, else None."""
    main = (main or prompt_library.main_checkout()).resolve()
    resolved = Path(os.path.abspath(path)).resolve()
    if resolved == main or main in resolved.parents:
        inside_worktree = any(part == "worktrees" and parent == ".claude" for parent, part in zip(resolved.parts, resolved.parts[1:])) \
            or ".worktrees" in resolved.parts
        return "worktree" if inside_worktree else None
    temps = {Path(tempfile.gettempdir()).resolve(), Path("/tmp").resolve(), Path("/var/folders").resolve()}
    if any(t == resolved or t in resolved.parents for t in temps):
        return "temp"
    try:
        top = subprocess.run(["git", "rev-parse", "--show-toplevel"], cwd=resolved if resolved.is_dir() else resolved.parent,
                             capture_output=True, text=True).stdout.strip()
    except OSError:
        top = ""
    if top and Path(top).resolve() != main:
        return "worktree"
    return None


def warn_if_ephemeral(path: Path | str | None, tool: str, *, stream=None) -> str | None:
    """Print a warning when `path` (an --out/--work folder) is inside a worktree or a temp folder, where the images
    are lost with the worktree or the OS clean-up. Returns the kind, never refuses: the images of a task still
    have to be copied with `image_archive.py add` before they are posted."""
    if path is None:
        return None
    try:
        kind = ephemeral_kind(Path(path))
    except (OSError, subprocess.SubprocessError):
        return None
    if kind and os.environ.get("ARC_IMAGE_ARCHIVE_QUIET") != "1":  # the opt-out is for tests that compare stderr
        where = "a git worktree" if kind == "worktree" else "a temp folder"
        print(f"WARNING {tool}: {path} is inside {where}; it is lost when the worktree is removed or the OS cleans up. "
              f"Save the task's images with `python3 -B tools/steamwoven_ui/image_archive.py add <TASK> FILE...` "
              f"(archive/<TASK>/ of the main checkout) before posting them.", file=stream or sys.stderr)
    return kind


# ------------------------------------------------------------------------- CLI

def cmd_dir(args) -> int:
    print(task_dir(args.task))
    return 0


def cmd_add(args) -> int:
    reg = Registry()
    for name in args.files:
        src = Path(name)
        if not src.is_file() or src.suffix.lower() not in IMAGE_EXTS:
            print(f"skip {src}: not an image file", file=sys.stderr)
            continue
        row = add_file(reg, args.task, src, kind=args.kind, status=args.status, note=args.note)
        print(f"{row['task']}/{row['file']}  {row['status']}  {row['sha256'][:12]}")
    reg.save()
    return 0


def cmd_mark(args) -> int:
    reg = Registry()
    row = mark(reg, args.task, args.file, args.status)
    reg.save()
    print(f"{row['task']}/{row['file']} -> {row['status']}")
    return 0


def cmd_index(args) -> int:
    reg = Registry()
    counts: dict[tuple, int] = {}
    for row in reg.rows.values():
        if args.task and row["task"] != args.task:
            continue
        key = (row["task"], row["kind"], row["status"])
        counts[key] = counts.get(key, 0) + 1
    for (task, kind, status), n in sorted(counts.items()):
        print(f"{task:28} {kind:11} {status:12} {n}")
    print(f"rows {sum(counts.values())}")
    return 0


def cmd_scan(args) -> int:
    reg = Registry()
    n = scan(reg, args.task)
    reg.save()
    print(f"scan: {n} new rows")
    return 0


def cmd_verify(args) -> int:
    reg = Registry()
    bad = 0
    for row in reg.rows.values():
        base = reg.root.parent if row.get("location") == "pipeline" else reg.root / row["task"]
        path = base / row["file"]
        if row.get("kind") == "directory":
            continue
        if not path.is_file():
            print(f"MISSING {row['task']}/{row['file']}")
            bad += 1
        elif sha256_file(path) != row["sha256"]:
            if row.get("location") == "pipeline":  # the pipeline rewrites its own files (a re-fit, a re-bake): the row is a snapshot
                print(f"NOTE rewritten in place since registered: {row['file']}")
                continue
            print(f"CHANGED {row['task']}/{row['file']}")
            bad += 1
    print(f"verify {'FAIL' if bad else 'PASS'}: {len(reg.rows)} rows, {bad} problems")
    return 1 if bad else 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("dir", help="create and print archive/<TASK>/")
    s.add_argument("task")
    s.set_defaults(func=cmd_dir)
    s = sub.add_parser("add", help="copy images into the task archive and register them")
    s.add_argument("task")
    s.add_argument("files", nargs="+")
    s.add_argument("--kind", choices=KINDS, default="unknown")
    s.add_argument("--status", choices=STATUSES, default="unknown")
    s.add_argument("--note", default="")
    s.set_defaults(func=cmd_add)
    s = sub.add_parser("mark", help="set the status of an archived file (final also copies it into final/)")
    s.add_argument("task")
    s.add_argument("file", help="path inside the task folder, e.g. composite/A.png")
    s.add_argument("status", choices=STATUSES)
    s.set_defaults(func=cmd_mark)
    s = sub.add_parser("index", help="counts per task, kind and status")
    s.add_argument("task", nargs="?")
    s.set_defaults(func=cmd_index)
    s = sub.add_parser("scan", help="register files put into archive/<TASK>/ by hand")
    s.add_argument("task", nargs="?")
    s.set_defaults(func=cmd_scan)
    s = sub.add_parser("verify", help="every registered file exists and has its hash")
    s.set_defaults(func=cmd_verify)
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        return args.func(args)
    except (ValueError, KeyError, OSError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
