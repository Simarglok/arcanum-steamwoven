"""ARC-DEV-2223: `./arcanum smoke verify-pr`, the pre-merge check that the smoke set `--auto` selects is a floor.

Class A of the smoke audit (docs/workflow/smoke-stability.md): a PR ran a hand-picked `--stages` list narrower than what its
diff affects, or skipped the self-test or the map stage, and a deterministic regression landed on main. This check reads the
diff against the merge base with `origin/main` (the same selection as `smoke pr --auto`, smoke_zones.py), and the queue
journal of finished smoke steps (`smoke-stages.jsonl`, smoke_history.py), and fails when a unit the selection requires has no
passing run of this checkout on the files as they are now:

- each stage of each selected zone (`core` is every cheap stage; `complete` is taken as `core` + the Crash Site smoke at a
  PR, the heavy zones and the map batteries being the daily build's and `core` on main the rolling main smoke's);
- the Crash Site smoke when `crash-site` is selected;
- for a change of click or walk handling (the `walk` and `container-click` rows), also the `map` stage and `smoke
  self-test` (owner decision 2026-11-07, ARC-DEV-239).

A path no zone row names selects `complete` (fail closed) and is named in the output. A run counts when its recorded
working-tree hash (taken when the step started) equals the current one, or when no file that chose a zone differs between the two (a later merge of main, a
rebase, or an edit of a document does not void a smoke; a change to a file of the diff does). It never starts a smoke and
needs no Godot: it reads git and one file.

    ./arcanum smoke verify-pr [--base REF]

The rule is docs/workflow/tasks.md#smoke-scope and docs/workflow/toolchain.md#smoke-runner-arc-dev-061.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Mapping, Sequence

from . import smoke_history, smoke_zones

STAGE, CRASH_SITE_STEP, SELF_TEST = "stage", "crash-site", "self-test"
MAP_STAGE = "map"


@dataclass(frozen=True)
class Need:
    """One unit the diff requires: a map smoke stage, the Crash Site smoke or the self-test, and why."""
    kind: str
    name: str
    why: str

    def label(self) -> str:
        return self.name if self.kind == STAGE else f"{self.name} smoke" if self.kind == CRASH_SITE_STEP else self.name


def needs(selection: smoke_zones.Selection, known: Sequence[str]) -> list[Need]:
    """The units a PR with this selection must have run (the floor): the stages of each zone of `selection.pr_zones`, the
    Crash Site smoke, and for click or walk handling the `map` stage and the self-test."""
    found: dict[tuple[str, str], Need] = {}
    for zone in selection.pr_zones:
        if zone == smoke_zones.CRASH_SITE:
            found.setdefault((CRASH_SITE_STEP, CRASH_SITE_STEP), Need(CRASH_SITE_STEP, CRASH_SITE_STEP, f"zone {zone}"))
            continue
        for stage in smoke_zones.zone_stages(zone, known):
            found.setdefault((STAGE, stage), Need(STAGE, stage, f"zone {zone}"))
    if selection.click_walk:
        why = "click or walk handling (ARC-DEV-239)"
        found.setdefault((STAGE, MAP_STAGE), Need(STAGE, MAP_STAGE, why))
        found.setdefault((SELF_TEST, SELF_TEST), Need(SELF_TEST, SELF_TEST, why))
    return list(found.values())


def ran(need: Need, entry: Mapping[str, object]) -> bool:
    """Whether a journal entry is a passing run of the unit: the step's exit 0 means every stage it asked for printed its PASS; a
    step that failed only with failures listed in smoke_known_failures.json (`knownOnly`, named in the PR as not its failure)
    counts too, and so does a stage a failed process still passed before it stopped (`passed`); a pinned run (`custom`) never does. A battery step asks for `map` and `resources`."""
    if entry.get("custom"):
        return False  # a pinned run (--map, --hero, ...) is not the stage on its own configuration
    exit_code = entry.get("exit")
    if need.kind == STAGE:
        asked, passed = entry.get("asked"), entry.get("passed")
        if exit_code == 0:
            return isinstance(asked, list) and need.name in asked
        if isinstance(passed, list) and need.name in passed:
            return True  # a failed process still printed this stage's PASS before it stopped
        return entry.get("knownOnly") is True and isinstance(asked, list) and need.name in asked
    return entry.get("step") == need.name and (exit_code == 0 or entry.get("knownOnly") is True)


def known_note(entry: Mapping[str, object], need: Need) -> str:
    passed = entry.get("passed")
    if entry.get("exit") == 0 or (isinstance(passed, list) and need.name in passed):
        return ""
    return " (failed only with a known failure on main: name it in the PR)"


@dataclass(frozen=True)
class Result:
    need: Need
    status: str  # "ok", "missing" or "stale"
    detail: str


@dataclass(frozen=True)
class Report:
    results: tuple[Result, ...]

    @property
    def ok(self) -> bool:
        return all(result.status == "ok" for result in self.results)


def verify(selection: smoke_zones.Selection, known: Sequence[str], entries: Sequence[Mapping[str, object]],
           checkout: str, tree: str | None, differing: Callable[[str, str], set[str] | None]) -> Report:
    """Each required unit against the journal. `tree` is the working-tree hash now; `differing(a, b)` the files that differ
    between two recorded trees (None when git cannot tell: the run is then not counted). Only the files that chose a zone
    void a run when they change; a document or a test of the diff does not."""
    mine = [entry for entry in entries if entry.get("checkout") == checkout]
    touched = {path for paths in selection.because.values() for path in paths}
    results = []
    for need in needs(selection, known):
        passed = [entry for entry in mine if ran(need, entry)]
        if not passed:
            results.append(Result(need, "missing", f"no passing run in this checkout's journal ({need.why})"))
            continue
        why = ""
        for entry in reversed(passed):  # the newest run first
            seen = entry.get("tree")
            if not isinstance(seen, str) or tree is None:
                why = why or f"run #{entry.get('seq')} has no recorded files to compare"
            elif seen == tree:
                results.append(Result(need, "ok", f"run #{entry.get('seq')} on these files{known_note(entry, need)}"))
                break
            else:
                moved = differing(seen, tree)
                if moved is None:
                    why = why or f"run #{entry.get('seq')}: its files cannot be compared (git object gone)"
                elif not moved & touched:
                    results.append(Result(need, "ok", f"run #{entry.get('seq')}, no file of the diff changed since{known_note(entry, need)}"))
                    break
                else:
                    shown = sorted(moved & touched)
                    why = why or (f"run #{entry.get('seq')} ran before a change to "
                                  + ", ".join(shown[:3]) + (f" and {len(shown) - 3} more" if len(shown) > 3 else ""))
        else:
            results.append(Result(need, "stale", why))
    return Report(tuple(results))


def tree_differences(a: str, b: str, root: Path | None = None) -> set[str] | None:
    """The files that differ between two recorded working-tree hashes, None when git does not have one of them."""
    try:
        text = smoke_history.git_text("diff", "--name-only", "-z", "--no-renames", a, b, root=root or smoke_history.ROOT)
    except ValueError:
        return None
    return {name for name in text.split("\0") if name}


SHOWN = 12  # the units listed one by one; the rest of each status is counted


def lines(report: Report) -> list[str]:
    """The report: the count of units that have a run, then each missing or stale unit (the first SHOWN, then a count)."""
    good = sum(result.status == "ok" for result in report.results)
    out = [f"  ok      {good} of {len(report.results)} required units have a passing run on these files"]
    bad = [result for result in report.results if result.status != "ok"]
    for result in bad[:SHOWN]:
        out.append(f"  {'MISSING' if result.status == 'missing' else 'STALE  '} {result.need.label():<22} {result.detail}")
    if len(bad) > SHOWN:
        out.append(f"  ... and {len(bad) - SHOWN} more missing or stale units (the first reason above holds for most)")
    return out


def run(args: argparse.Namespace, say: Callable[[str], None] = lambda line: print(line, flush=True)) -> int:
    from . import smoke  # smoke imports this module only here, so the stage list is read late

    base = getattr(args, "base", None) or "origin/main"
    try:
        mark, files = smoke_zones.changed_files(base)
        selection = smoke_zones.select_zones(files, lambda path: smoke_zones.changed_lines(mark, path))
        known = smoke.map_stages()
    except ValueError as error:
        say(f"SMOKE verify-pr: {error}")
        return 2
    for line in smoke_zones.describe(selection, mark, base):
        say(line.replace("SMOKE auto:", "SMOKE verify-pr:", 1))
    if not needs(selection, known):
        say("SMOKE verify-pr PASS: no smoke zone covers this diff, nothing is required")
        return 0
    report = verify(selection, known, smoke_history.recent(smoke_history.FILE_KEEP), smoke.ROOT.name,
                    smoke_history.work_tree(), tree_differences)
    for line in lines(report):
        say(line)
    if report.ok:
        say(f"SMOKE verify-pr PASS: {len(report.results)} required units have a passing run on these files")
        return 0
    bad = [result.need.label() for result in report.results if result.status != "ok"]
    say(f"SMOKE verify-pr FAIL: {len(bad)} of {len(report.results)} required units have no passing run on these files "
        f"({', '.join(bad[:6])}{', ...' if len(bad) > 6 else ''}); run `./arcanum smoke pr --auto` "
        + ("and `./arcanum smoke self-test` " if any(result.need.kind == SELF_TEST for result in report.results) else "")
        + "and report it again")
    return 1
