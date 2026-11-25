#!/usr/bin/env python3
"""One-off and repeatable rescue of task images into the archive (ARC-DEV-3277).

Finds images that live only in places that go away (worktrees, session scratchpads, temp folders, the Codex output
folder) and COPIES them (never moves or deletes) into `local/steamwoven-ui/archive/<TASK>/` of the main checkout with
registry rows (`image_archive.py`). The task of a file comes, in order, from its path, the branch of its worktree, the
task code most specific to its session transcript, or else it goes to `_unsorted/<bucket>`; every row records how the
task was found (`basis.task_basis`). Safe to run again: the same content is never copied twice.

    image_rescue.py plan                       # counts per source and task, nothing copied
    image_rescue.py run [--only SOURCE,...]    # copy and register; sources: worktrees scratch loose tmp codex composites pipeline
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import re
import subprocess
import sys
from pathlib import Path

try:
    from . import image_archive as ia
    from . import prompt_library
except ImportError:  # run as a script
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import image_archive as ia  # type: ignore
    import prompt_library  # type: ignore

SOURCES = ("composites", "worktrees", "scratch", "loose", "tmp", "codex", "pipeline")
CODE_RE = re.compile(r"ARC-[A-Z]+-\d+", re.I)
SKIP_PARTS = {"venv", "site-packages", "node_modules", ".git", "bin", "obj", "__pycache__"}
SINCE = "2026-11-01"
PIXEL_NUMBERS = {"2560", "2880", "3840", "2160", "4096", "2880"}  # screen sizes that look like task numbers
# Pipeline batch (folder name under inbox/outbox/codex-work/work) -> task, from merged PR titles; unlisted batches go to _unsorted/batch-<name>.
BATCH_TASK = {
    "B-BOARD": "ARC-UI-2927", "B-GAUGES": "ARC-UI-2483", "B-HARDWARE-HUD": "ARC-UI-2481", "B-HARDWARE-MENU": "ARC-UI-3260",
    "B-HARDWARE": "ARC-UI-2481", "B-BUTTONS": "ARC-UI-3203", "kit-pilot-glyphs-relief": "ARC-UI-3058", "kit-pilot-round-button": "ARC-UI-3058",
}


def scratch_base() -> Path:
    """The session scratch root of Claude Code on this machine (`<tmp>/claude-<uid>`)."""
    return Path("/tmp").resolve() / f"claude-{os.getuid()}"


def slug(text: str) -> str:
    return re.sub(r"[^A-Za-z0-9._-]", "-", text).strip(".-") or "x"


def is_image(path: Path) -> bool:
    return path.suffix.lower() in ia.IMAGE_EXTS


def walk_images(root: Path):
    for dp, dn, fn in os.walk(root):
        dn[:] = [d for d in dn if d not in SKIP_PARTS]
        for f in fn:
            p = Path(dp) / f
            if is_image(p) and not p.is_symlink():
                yield p


class TaskMap:
    """Task codes known from the repository, and the worktree/session lookups."""

    def __init__(self, main: Path, home: Path) -> None:
        self.main, self.home = main, home
        self.codes: set[str] = set()
        self.branch_of: dict[str, str] = {}
        self._collect()
        by_num: dict[str, set[str]] = collections.defaultdict(set)
        for c in self.codes:
            by_num[c.rsplit("-", 1)[1]].add(c)
        self.by_num = {n: next(iter(cs)) for n, cs in by_num.items() if len(cs) == 1}
        self._transcript_cache: dict[str, tuple[str | None, str]] = {}
        self._df: dict[str, int] = {}
        self._sessions = 0

    def _collect(self) -> None:
        git = lambda *a: subprocess.run(["git", *a], cwd=self.main, capture_output=True, text=True).stdout
        for sub in git("log", "origin/main", "--format=%s", "-n", "6000").splitlines():
            m = CODE_RE.match(sub)
            if m:
                self.codes.add(m.group(0).upper())
        cur = None
        for line in git("worktree", "list", "--porcelain").splitlines():
            if line.startswith("worktree "):
                cur = Path(line[9:]).name
            elif line.startswith("branch ") and cur:
                branch = line[7:].removeprefix("refs/heads/")
                self.branch_of[cur] = branch
                self.codes.update(c.upper() for c in CODE_RE.findall(branch))
        comp = self.main / "local/steamwoven-ui/composites"
        if comp.is_dir():
            self.codes.update(d.name for d in comp.iterdir() if ia.TASK_RE.fullmatch(d.name))

    def normalise(self, text: str) -> str | None:
        m = CODE_RE.search(text)
        return m.group(0).upper() if m else None

    def from_name(self, name: str) -> str | None:
        """A code or a unique 4-digit task number in a file or folder name (`composite-3104.png`, `arc3096`)."""
        code = self.normalise(name)
        if code:
            return code
        nums = re.findall(r"(?<!\d)(\d{4})(?!\d)", name)
        hits = {self.by_num[n] for n in nums if n in self.by_num and int(n) >= 2400 and n not in PIXEL_NUMBERS}
        return next(iter(hits)) if len(hits) == 1 else None

    def prime_transcripts(self, projects: list[tuple[str, str]]) -> None:
        """Count in how many session transcripts each code appears, so boilerplate codes (the CLAUDE.md ones) are ignored."""
        counts: dict[str, collections.Counter] = {}
        for proj, sid in projects:
            f = self.home / ".claude/projects" / proj / f"{sid}.jsonl"
            if f.is_file():
                with f.open("rb") as fh:
                    text = fh.read(600_000).decode("utf-8", "ignore")
                counts[f"{proj}/{sid}"] = collections.Counter(c.upper() for c in CODE_RE.findall(text))
        self._sessions = max(len(counts), 1)
        for c in counts.values():
            for code in c:
                self._df[code] = self._df.get(code, 0) + 1
        self._counts = counts

    def from_session(self, proj: str, sid: str) -> tuple[str | None, str]:
        wt = proj.split("worktrees-", 1)[-1]
        branch = self.branch_of.get(wt)
        if branch:
            code = self.normalise(branch)
            if code:
                return code, "branch"
        counts = getattr(self, "_counts", {}).get(f"{proj}/{sid}")
        if counts:
            specific = [(n, c) for c, n in counts.items() if self._df.get(c, 0) / self._sessions <= 0.15 and c in self.codes]
            if specific:
                return max(specific)[1], "transcript"
        return None, "none"


def classify(src: Path, tm: TaskMap, default_bucket: str, hint_task: str | None = None) -> tuple[str, str]:
    """(task, task_basis) for a loose file: a code in its path first, else the bucket."""
    code = hint_task or tm.from_name("/".join(src.parts[-3:]))
    if code and ia.TASK_RE.fullmatch(code):
        return code, "path"
    return f"_unsorted/{slug(default_bucket)}", "none"


def codex_sessions(main: Path) -> dict[str, str]:
    """Codex generated-image path (`<session>/exec-....png`) -> batch, from the run events kept in codex-work."""
    out: dict[str, str] = {}
    for events in (main / "local/steamwoven-ui/codex-work").rglob("events-*.jsonl"):
        batch = events.relative_to(main / "local/steamwoven-ui/codex-work").parts[0]
        for line in events.read_text(encoding="utf-8", errors="ignore").splitlines():
            for m in re.finditer(r"generated_images/([0-9a-f-]+/exec-[0-9a-f-]+\.png)", line):
                out[m.group(1)] = batch
    return out


# --------------------------------------------------------------------- sources

def gather(source: str, main: Path, home: Path, tm: TaskMap) -> list[tuple[Path, str, str, str, str, str]]:
    """Rows to archive: (src, task, task_basis, kind, status, note)."""
    ui = main / "local/steamwoven-ui"
    rows: list[tuple[Path, str, str, str, str, str]] = []
    if source in ("composites", "worktrees"):
        # composites of the main checkout (`composites`) and the ones only a worktree holds (`worktrees`)
        roots = [ui / "composites"] if source == "composites" else sorted((main / ".claude/worktrees").glob("*/local/steamwoven-ui/composites"))
        for root in roots:
            for p in walk_images(root):
                parts = p.relative_to(root).parts
                task = parts[0] if ia.TASK_RE.fullmatch(parts[0]) else None
                if task is None:
                    task, basis = classify(p, tm, "composites-root")
                else:
                    basis = "path"
                sub = "/".join(parts[1:-1]) if task == parts[0] else "/".join(parts[:-1])
                kind = "composite" if "composites" in str(root) and not re.search(r"shot|cap", p.name.lower() + sub) else ia.guess_kind(str(p))
                wt = root.parts[root.parts.index("worktrees") + 1] if "worktrees" in root.parts else "main"
                rows.append((p, task, basis, kind, "unknown", f"composite from {wt}"))
    elif source == "scratch":
        base = scratch_base()
        sessions = []
        prefix = main.as_posix().replace("/", "-")  # a session folder is the checkout's path with / as -
        for proj in (sorted(base.glob(prefix + "*")) if base.is_dir() else []):
            for sdir in proj.glob("*/scratchpad"):
                sessions.append((proj.name, sdir.parent.name, sdir))
        seen = set()
        uniq = [s for s in sessions if not (s[2] in seen or seen.add(s[2]))]
        tm.prime_transcripts([(p, s) for p, s, _ in uniq])
        for proj, sid, sdir in uniq:
            code, basis = tm.from_session(proj, sid)
            wt = proj.split("worktrees-", 1)[-1] if "worktrees-" in proj else proj[-30:]
            for p in walk_images(sdir):
                task, tb = (code, basis) if code else (f"_unsorted/scratch-{slug(wt)}", "none")
                hint = tm.from_name(p.name)
                if hint and not code:
                    task, tb = hint, "path"
                rows.append((p, task, tb, ia.guess_kind(str(p)), "unknown", f"scratchpad {wt}/{sid[:8]}"))
    elif source == "loose":
        base = scratch_base()
        for entry in (sorted(base.iterdir()) if base.is_dir() else []):
            if entry.name.startswith("-") or entry.name in SKIP_PARTS or entry.name.startswith("venv"):
                continue
            files = [entry] if entry.is_file() and is_image(entry) else list(walk_images(entry)) if entry.is_dir() else []
            for p in files:
                task, tb = classify(p, tm, "loose-scratch", tm.from_name(entry.name))
                rows.append((p, task, tb, ia.guess_kind(str(p)), "unknown", "loose file in the scratch root"))
    elif source == "tmp":
        base = Path("/tmp").resolve()
        for dp, dn, fn in os.walk(base):
            depth = len(Path(dp).relative_to(base).parts)
            dn[:] = [d for d in dn if d not in SKIP_PARTS and not d.startswith("claude-") and not d.startswith("MSBuild") and depth < 2]
            for f in fn:
                p = Path(dp) / f
                if is_image(p) and not p.is_symlink() and p.stat().st_mtime >= _since():
                    task, tb = classify(p, tm, "loose-tmp")
                    rows.append((p, task, tb, ia.guess_kind(str(p)), "unknown", "loose file in /tmp"))
    elif source == "codex":
        known = {ia.sha256_file(p) for p in walk_images(ui)}
        batch_of = codex_sessions(main)
        cg = home / ".codex/generated_images"
        for p in sorted(cg.rglob("*.png")):
            if ia.sha256_file(p) in known:
                continue
            batch = batch_of.get(f"{p.parent.name}/{p.name}")
            task = BATCH_TASK.get(batch or "")
            if task:
                rows.append((p, task, "codex-events", "raw-codex", "intermediate", f"Codex output of batch {batch} that no outbox file or fit record uses (a retry)"))
            else:
                day = _day(p)
                rows.append((p, f"_unsorted/codex-{slug(day)}", "none", "raw-codex", "unknown", f"Codex output {p.parent.name}, batch {batch or 'unknown'}"))
    elif source == "pipeline":
        return rows  # registered in place by `register_pipeline`
    return rows


def _since() -> float:
    import time
    return time.mktime(time.strptime(SINCE, "%Y-%m-%d"))


def _day(p: Path) -> str:
    import time
    return time.strftime("%Y-%m", time.localtime(p.stat().st_mtime))


# ------------------------------------------------------- pipeline (in place)

def register_pipeline(reg: ia.Registry, main: Path) -> int:
    """Register the images the batch pipeline keeps in place under `local/steamwoven-ui/` (outbox, codex-work, masters,
    inbox/refs): one row per file for outbox, codex-work and masters, one summary row per folder for the bulk
    derivative folders. `final` = a master with a fit record, or an outbox file whose bytes a fit record names as its source."""
    ui = main / "local/steamwoven-ui"
    finals: dict[str, str] = {}  # source sha256 -> master part id
    master_batch: dict[str, str] = {}  # master part -> batch
    for rec in sorted((ui / "provenance/fit").glob("*.json")):
        try:
            data = json.loads(rec.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        sha = (data.get("source") or {}).get("sha256")
        if sha:
            finals[sha] = data.get("part", rec.stem)
        m = re.match(r"(?:work|outbox)/([^/]+)/", (data.get("source") or {}).get("file", ""))
        if m:
            master_batch[data.get("part", rec.stem)] = m.group(1)
    new = 0

    def put(p: Path, batch: str, kind: str, status: str, note: str) -> None:
        nonlocal new
        task = BATCH_TASK.get(batch)
        basis = "batch-table" if task else "none"
        task = task or f"_unsorted/batch-{slug(batch)}"
        rel = p.relative_to(ui).as_posix()
        st = p.stat()
        reg.put(ia._row(task, rel, kind, status, [str(p)], ia.sha256_file(p), st.st_size, ia.image_size(p), st.st_mtime, note,
                        {"task_basis": basis}, location="pipeline"))
        new += 1

    for p in sorted(walk_images(ui / "masters")):
        batch = master_batch.get(p.stem, "masters")
        put(p, batch, "master", "final" if p.stem in master_batch or (ui / f"provenance/fit/{p.stem}.json").exists() else "unknown",
            "fitted master (fit record)" if (ui / f"provenance/fit/{p.stem}.json").exists() else "master without a fit record")
    for p in sorted(walk_images(ui / "outbox")):
        batch = p.relative_to(ui / "outbox").parts[0]
        base = re.sub(r"-(attempt\d+|prewood|v\d+)$", "", batch)
        rejected = base != batch
        sha = ia.sha256_file(p)
        status = "final" if sha in finals else "rejected" if rejected else "intermediate"
        put(p, base, "raw-codex", status, f"Codex outbox {batch}")
    for p in sorted(walk_images(ui / "codex-work")):
        batch = p.relative_to(ui / "codex-work").parts[0]
        status = "final" if ia.sha256_file(p) in finals else "intermediate"
        put(p, batch, "raw-codex", status, "Codex run output (codex-work)")
    # bulk folders: one summary row each
    for name in ("pilot", "census", "compare", "refs", "work", "material-samples", "material-sheet"):
        d = ui / name
        if not d.is_dir():
            continue
        files = list(walk_images(d))
        key = ("pipeline", f"_unsorted/pipeline-{name}", name + "/")
        if not files:
            continue
        reg.put({"task": key[1], "file": key[2], "location": "pipeline", "kind": "directory", "status": "unknown", "sources": [str(d)],
                 "sha256": "", "bytes": sum(f.stat().st_size for f in files), "size": None, "date": "", "registered": ia.date.today().isoformat(),
                 "note": f"{len(files)} images, derivative or reference inputs of the pipeline, kept in place", "basis": {"task_basis": "none"}})
        new += 1
    return new


# ------------------------------------------------------------------------- CLI

def run(sources: list[str], dry: bool) -> int:
    main, home = prompt_library.main_checkout(), Path.home()
    reg = ia.Registry(main / "local/steamwoven-ui/archive")
    tm = TaskMap(main, home)
    total = collections.Counter()
    for source in sources:
        if source == "pipeline":
            n = 0 if dry else register_pipeline(reg, main)
            print(f"pipeline: {n} rows registered in place")
            continue
        rows = gather(source, main, home, tm)
        per_task = collections.Counter(r[1] for r in rows)
        basis = collections.Counter(r[2] for r in rows)
        print(f"{source}: {len(rows)} files, {len(per_task)} tasks, bases {dict(basis)}")
        if dry:
            for t, n in per_task.most_common(12):
                print(f"   {t:34} {n}")
            continue
        copied = 0
        for i, (src, task, tb, kind, status, note) in enumerate(rows, 1):
            before = len(reg.rows)
            try:
                row = ia.add_file(reg, task, src, kind=kind, status=status, note=note, basis={"task_basis": tb, "kind_basis": "heuristic"})
            except OSError as error:
                print(f"  skip {src}: {error}", file=sys.stderr)
                continue
            copied += len(reg.rows) - before
            if i % 500 == 0:
                reg.save()
        reg.save()
        total[source] = copied
        print(f"   copied {copied} new, {len(rows) - copied} already archived")
    if not dry:
        reg.save()
    return 0


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("cmd", choices=("plan", "run"))
    p.add_argument("--only", default=",".join(SOURCES), help="comma list of sources")
    args = p.parse_args(argv)
    sources = [s for s in args.only.split(",") if s]
    bad = [s for s in sources if s not in SOURCES]
    if bad:
        print(f"unknown source {bad}", file=sys.stderr)
        return 2
    return run(sources, args.cmd == "plan")


if __name__ == "__main__":
    sys.exit(main())
