"""ARC-DEV-2226: the rolling smoke on origin/main, `./arcanum main-smoke`: a red main is found within about two hours.

Fetches origin main and stops at once (SKIP) when main has not moved since the last green or red run of this command
(unless --force). Otherwise it checks the commit out into a fresh detached worktree in a temporary folder and runs the
core smoke there, `./arcanum smoke pr --zones core,crash-site --retry --low-priority` (every cheap stage and the Crash
Site smoke; a stage that fails inside its process is re-run alone, so "fails alone" and "passes alone" are known), with
every queue step behind the ordinary runs (run_queue.LOW), so PR runs go first. Green records the commit and clears the
flag; red files or updates ONE GitHub issue (labels `bug`, `type:dev`, `status:ready`, an `area:*` by the first failing
stage) with the failing stages, the first red commit and the merge range since the last green commit, and writes the flag
that `./arcanum smoke pr` prints (red_flag.py). A run that proves nothing (stopped, cut off by its time limit, no
summary) is recorded as inconclusive and changes neither the flag nor the issue. Every run's failing stages and
alone-reruns go to flake-log.jsonl (the later flake register's input). State, flag, logs and the report are in the
`main-smoke` folder of the launcher home. The schedule is a launchd agent the owner starts
(`./arcanum main-smoke --print-launchd`). The rule: docs/workflow/toolchain.md#rolling-smoke-on-main-arc-dev-2226.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import fcntl
import json
import os
from pathlib import Path
import plistlib
import re
import shlex
import shutil
import signal
import subprocess
import sys
import tempfile
from typing import Any, Callable, Sequence

from . import daily, package, red_flag, run_queue, smoke, start_check

ZONES = "core,crash-site"
SMOKE_MINUTES = 180.0  # the whole smoke command, queue waits included: a run past it proves nothing
INTERVAL_SECONDS = 2 * 60 * 60
TEMP_PREFIX = "arcanum-main-smoke-"
LOCK = "arcanum-main-smoke.lock"
STATE = "state.json"
FLAKES = "flake-log.jsonl"
LABEL = "com.arcanum.steamwoven.main-smoke"
TITLE = "Red on main (rolling smoke)"
LABELS = ("bug", "type:dev", "status:ready")
DEFAULT_AREA = "area:scene"
# The first failing stage's area label: the first row whose word is in the stage name wins.
AREAS: tuple[tuple[str, tuple[str, ...]], ...] = (
    ("area:combat-magic-tech", ("cast", "spell", "turn-based", "knockdown", "ranged", "grenade", "tech", "shot", "burn", "magic", "combat")),
    ("area:items-economy", ("barter", "loot", "inv", "container", "junk", "pickpocket", "item", "disarm", "bed")),
    ("area:story-social-ai", ("ai", "npc", "premade", "opening", "follower", "companion", "dialog", "healer", "talk", "life")),
    ("area:world-travel-runtime", ("transfer", "worldmap", "travel", "roundtrip", "stream", "resident", "teleport", "saved-runs",
                                   "save-resume", "census", "walk", "door", "wild")),
    ("area:ui-media-lifecycle", ("menu", "options", "bar", "death", "slides", "movies", "hover", "scene", "report")),
)
MAX_RANGE_LINES = 60
PASS, RED, INCONCLUSIVE = 0, 1, 2
GREEN_RESULT, RED_RESULT, INCONCLUSIVE_RESULT = "green", "red", "inconclusive"

Runner = daily.Runner
Gh = Callable[[Sequence[str], str | None], str]  # gh arguments and stdin text -> stdout; raises GhError


class GhError(RuntimeError):
    pass


def say(message: str) -> None:
    print(f"MAIN-SMOKE {message}", flush=True)


def stamp(now: datetime) -> str:
    return now.strftime("%Y-%m-%dT%H:%M:%SZ")


# --- the result of one smoke run ---------------------------------------------------------------------------------

RETRY_STEP = re.compile(r"^(?P<group>.+)-retry-(?P<stage>.+)$")
ORDER_LINE = re.compile(r"^(?P<step>\S+) \(.*each of (?P<stages>.+) passes alone\)$")


def classify(result: daily.SmokeResult, code: int) -> tuple[str, str]:
    """(green | red | inconclusive, why). Red needs a summary that names a failed step (a failed viewer build or import
    included: main does not build); a stopped run, the time limit or a missing summary proves nothing about main."""
    if code >= smoke.STOPPED or code == run_queue.TIMEOUT_EXIT:
        return INCONCLUSIVE_RESULT, f"the smoke run was stopped or ran out of time (exit {code})"
    failure = result.failure
    if failure is None:
        return GREEN_RESULT, "every step passed"
    if result.failed:
        return RED_RESULT, failure
    return INCONCLUSIVE_RESULT, failure


TRANSIENT_STEP = re.compile(r"^\s+(\S+)\s+FAIL exit=(\d+)$", re.MULTILINE)
TRANSIENT_EXITS = frozenset({run_queue.TIMEOUT_EXIT, start_check.FAILED_START_EXIT})


def drop_transient(result: daily.SmokeResult, text: str) -> list[str]:
    """Take the failed steps whose own exit is a queue timeout or a viewer that did not start out of `result.failed` and
    return them: a loaded Mac proves nothing about main. A run left with no other failed step is inconclusive."""
    transient = [name for name, code in TRANSIENT_STEP.findall(text) if int(code) in TRANSIENT_EXITS and name in result.failed]
    result.failed = [name for name in result.failed if name not in transient]
    return transient


def failing_stages(result: daily.SmokeResult) -> list[str]:
    """The failing stages: a `GROUP-retry-STAGE` step that failed is that stage (it failed alone too), and the group that
    owns such a retry is not listed again; any other failed step (a build, the Crash Site smoke, a one-stage process) is
    listed by its own name."""
    retried = {found.group("group") for name in result.failed if (found := RETRY_STEP.match(name))}
    stages: list[str] = []
    for name in result.failed:
        found = RETRY_STEP.match(name)
        stage = found.group("stage") if found else name
        if (found or name not in retried) and stage not in stages:
            stages.append(stage)
    return stages


def flake_records(result: daily.SmokeResult, sha: str, at: str) -> list[dict[str, str]]:
    """What the flake register will want: every stage that passed alone after failing in its process (`passes-alone`),
    failed alone too (`fails-alone`), or failed in a step that has no alone re-run (`failed`)."""
    records = []
    for entry in result.order_dependent:
        found = ORDER_LINE.match(entry)
        for stage in (item.strip() for item in found.group("stages").split(",")) if found else ():
            records.append({"at": at, "sha": sha, "stage": stage, "outcome": "passes-alone"})
    retried = {found.group("group") for name in result.failed if (found := RETRY_STEP.match(name))}
    for name in result.failed:
        found = RETRY_STEP.match(name)
        if found:
            records.append({"at": at, "sha": sha, "stage": found.group("stage"), "outcome": "fails-alone"})
        elif name not in retried:
            records.append({"at": at, "sha": sha, "stage": name, "outcome": "failed"})
    return records


def area_label(stages: Sequence[str]) -> str:
    name = (stages[0] if stages else "").lower()
    tokens = set(re.split(r"[^a-z0-9]+", name))
    for label, words in AREAS:  # a word is a whole token of the name ("ai" is not in "main"); a hyphenated word is a part of it
        if any(word in tokens or ("-" in word and word in name) for word in words):
            return label
    return DEFAULT_AREA


# --- state, merge range and the issue --------------------------------------------------------------------------

def read_state(folder: Path) -> dict[str, Any]:
    try:
        state = json.loads((folder / STATE).read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, ValueError):
        return {}
    return state if isinstance(state, dict) else {}


def write_state(folder: Path, state: dict[str, Any]) -> None:
    folder.mkdir(parents=True, exist_ok=True)
    temporary = folder / (STATE + ".tmp")
    temporary.write_text(json.dumps(state, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, folder / STATE)


def merge_range(root: Path, green: str | None, sha: str) -> tuple[list[str], str | None]:
    """The first-parent commits after the last green commit up to this one (newest first), and a note when it cannot be
    given (no green commit known, or git cannot list it)."""
    if not green:
        return [], "no earlier green run is known, so the range is unknown"
    try:
        listing = package.git(root, "log", "--first-parent", "--format=%h %s", f"{green}..{sha}")
    except package.PackageError as error:
        return [], f"the range could not be listed: {error}"
    return [line for line in listing.splitlines() if line.strip()], None


def compare_url(root: Path, green: str | None, sha: str) -> str | None:
    try:
        remote = package.git(root, "remote", "get-url", "origin")
    except package.PackageError:
        return None
    found = re.search(r"github\.com[:/]([^/\s]+/[^/\s]+?)(?:\.git)?$", remote)
    return f"https://github.com/{found.group(1)}/compare/{green}...{sha}" if found and green else None


def issue_body(red: dict[str, Any], range_lines: Sequence[str], range_note: str | None, link: str | None,
               details: Sequence[str]) -> str:
    lines = [
        "Filed and updated by `./arcanum main-smoke` (ARC-DEV-2226): the rolling smoke on `origin/main`. "
        "Each red run rewrites this body; a green run comments and clears the flag.",
        "",
        f"- **Red on main since** `{red['firstRed']}` (first red run {red['firstRedAt']}).",
        f"- **Last checked** `{red['checked']}` ({red['checkedAt']}), red.",
        f"- **Last green** " + (f"`{red['lastGreen']}` ({red.get('lastGreenAt', '?')})" if red.get("lastGreen") else "none known"),
        f"- **Failing stages**: {', '.join(red['stages']) or 'unknown'}",
        f"- **Smoke set**: zones `{red.get('zones', ZONES)}`; the failing stages were re-run alone once (see the flake log on the Mac).",
        "",
        "## Merge range since the last green commit",
        "",
    ]
    if range_note:
        lines.append(range_note)
    else:
        lines.append(f"{len(range_lines)} first-parent commit(s), newest first" + (f" ([compare]({link}))" if link else "") + ":")
        lines.append("")
        lines.extend(f"- {line}" for line in range_lines[:MAX_RANGE_LINES])
        if len(range_lines) > MAX_RANGE_LINES:
            lines.append(f"- ... and {len(range_lines) - MAX_RANGE_LINES} older ones")
    lines.extend(["", "## First errors", "", "```", *(details or ["(no step output was kept)"]), "```", "",
                  "Cite this issue instead of re-running a failing stage on a clean main; `./arcanum smoke pr` prints the flag. "
                  "The fix's PR closes it."])
    return "\n".join(lines) + "\n"


def default_gh(arguments: Sequence[str], stdin: str | None) -> str:
    try:
        return subprocess.run(["gh", *arguments], input=stdin, capture_output=True, text=True, check=True, timeout=120).stdout.strip()
    except (OSError, subprocess.SubprocessError) as error:
        raise GhError(f"gh {' '.join(arguments[:2])} failed: {getattr(error, 'stderr', '') or error}".strip()) from error


def open_issue(gh: Gh, number: int | None) -> bool:
    if not number:
        return False
    try:
        return json.loads(gh(["issue", "view", str(number), "--json", "state"], None)).get("state") == "OPEN"
    except (GhError, ValueError):
        return False


def file_issue(gh: Gh, red: dict[str, Any], body: str, known: int | None) -> int:
    """Update the one open issue (the state's, else the open one with the title), or create it; its number."""
    number = known if open_issue(gh, known) else None
    if number is None:
        found = json.loads(gh(["issue", "list", "--state", "open", "--label", "bug", "--label", "type:dev", "--search",
                               f'"{TITLE}" in:title', "--json", "number,title"], None) or "[]")
        number = next((item["number"] for item in found if str(item.get("title", "")).startswith(TITLE)), None)
    title = f"{TITLE}: {', '.join(red['stages'][:3]) or 'smoke'}" + (" and more" if len(red["stages"]) > 3 else "")
    if number is not None:
        gh(["issue", "edit", str(number), "--title", title, "--body-file", "-"], body)
        return int(number)
    labels = ",".join((*LABELS, area_label(red["stages"])))
    created = gh(["issue", "create", "--title", title, "--label", labels, "--body-file", "-"], body)
    found_number = re.search(r"/issues/(\d+)\s*$", created)
    if not found_number:
        raise GhError(f"gh issue create gave no issue number: {created[:200]}")
    return int(found_number.group(1))


# --- one run ---------------------------------------------------------------------------------------------------

def smoke_command(zones: str) -> tuple[str, ...]:
    return ("./arcanum", "smoke", "pr", "--zones", zones, "--retry", "--low-priority")


def append_flakes(folder: Path, records: Sequence[dict[str, str]]) -> None:
    if not records:
        return
    folder.mkdir(parents=True, exist_ok=True)
    with (folder / FLAKES).open("a", encoding="utf-8") as handle:
        for record in records:
            handle.write(json.dumps(record, sort_keys=True) + "\n")


def main_smoke(*, root: Path, folder: Path, fetch: bool, force: bool, keep_work: bool, dry_run: bool, issues: bool, zones: str,
               runner: Runner = daily.run_logged, gh: Gh = default_gh, environ: dict[str, str] | None = None,
               now: Callable[[], datetime] = lambda: datetime.now(timezone.utc)) -> int:
    environ = dict(os.environ if environ is None else environ)
    if environ.get(run_queue.TICKET_ENV):
        raise package.PackageError("run ./arcanum main-smoke on its own, not inside ./arcanum run: its smoke takes its own "
                                   "places in the queue")
    if fetch:
        say("fetch origin main")
        package.git(root, "fetch", "origin", "main")
    sha, date = package.resolve(root, "origin/main")
    state = read_state(folder)
    last = state.get("lastRun") or {}
    green = (state.get("lastGreen") or {}).get("sha")
    say(f"origin/main {sha} ({date}); last green {(green or 'none')[:10]}; last run "
        f"{(last.get('sha') or 'none')[:10]} {last.get('result', '')}".rstrip())
    if not force and last.get("sha") == sha and last.get("result") in (GREEN_RESULT, RED_RESULT):
        red = state.get("red") or {}
        if last["result"] == RED_RESULT and issues and red and not red.get("issue"):
            # the smoke is not run again for the same commit, only the issue that gh could not file before
            say(f"red {sha[:10]} has no issue yet: filing it without running the smoke again")
            return publish_red(root=root, folder=folder, state=state, red=red, issues=issues, gh=gh, note=say)
        say(f"SKIP main has not moved since the last {last['result']} run ({sha[:10]}); --force runs it again")
        return PASS
    command = smoke_command(zones)
    playing = run_queue.playing_state()  # ARC-DEV-2272: the core smoke opens windows; --force does not override the owner
    if playing is not None and not dry_run:
        say(f"SKIP owner is playing ({run_queue.playing_text(playing)}): the smoke opens windows, so it was not run and nothing "
            "is recorded (no green, no red); it runs when the mode has ended (./arcanum queue --not-playing)")
        return PASS
    if dry_run:
        say(f"dry run: would check out {sha[:10]} into a fresh worktree and run there (limit {SMOKE_MINUTES:g} minutes): "
            + smoke.shown(command))
        say(f"state, flag, flake log and logs: {folder}; "
            + ("on red it files or updates the one GitHub issue" if issues else "no GitHub issue (--no-issue)"))
        return PASS
    logs = folder / "logs"
    if logs.is_symlink() or logs.exists() and not logs.is_dir():
        raise package.PackageError(f"{logs} must be a real folder")
    if logs.exists():
        shutil.rmtree(logs)
    logs.mkdir(parents=True)
    temporary = Path(tempfile.gettempdir()).resolve()
    for stale in daily.remove_stale(root, temporary, TEMP_PREFIX):
        say(f"removed the leftover {stale}")
    work = Path(tempfile.mkdtemp(prefix=TEMP_PREFIX, dir=temporary))
    checkout = work / "checkout"
    report: list[str] = []

    def note(line: str) -> None:
        report.append(line)
        say(line)

    outcome = INCONCLUSIVE
    added = False
    try:
        package.git(root, "worktree", "add", "--detach", str(checkout), sha)
        added = True
        started = stamp(now())
        note(f"rolling smoke {started} commit {sha} ({date}) zones {zones}")
        log = logs / "smoke.log"
        say("step smoke")
        code = runner(command, checkout, dict(environ), log, SMOKE_MINUTES)
        text = log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""
        note(f"smoke exit={code} log={log}")
        result = daily.parse_smoke(text, code)
        transient = drop_transient(result, text)
        if transient:
            note(f"not counted as red (a step timed out or did not start): {', '.join(transient)}")
        verdict, why = classify(result, code)
        at = stamp(now())
        note(f"RESULT {verdict.upper()} {why}")
        outcome = record(root=root, folder=folder, state=state, verdict=verdict, sha=sha, at=at, zones=zones, result=result,
                         issues=issues, gh=gh, note=note, transient=transient)
        return outcome
    except package.PackageError as error:
        note(f"RESULT {'STOPPED' if str(error).startswith('stopped by signal') else 'FAIL'} {error}; recorded as inconclusive")
        state["lastRun"] = {"sha": sha, "at": stamp(now()), "result": INCONCLUSIVE_RESULT}
        write_state(folder, state)
        return INCONCLUSIVE
    finally:
        held = {number: signal.signal(number, signal.SIG_IGN) for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)}
        try:
            if keep_work:
                say(f"work kept at {work} (the next run removes it)")
            else:
                if added:
                    try:
                        package.git(root, "worktree", "remove", "--force", str(checkout))
                    except package.PackageError as error:
                        say(f"note: {error}")
                shutil.rmtree(work, ignore_errors=True)
            (logs / "report.txt").write_text("\n".join(report) + "\n", encoding="utf-8")
            say(f"report {logs / 'report.txt'}")
        finally:
            for number, handler in held.items():
                signal.signal(number, handler)


def record(*, root: Path, folder: Path, state: dict[str, Any], verdict: str, sha: str, at: str, zones: str,
           result: daily.SmokeResult, issues: bool, gh: Gh, note: Callable[[str], None], transient: Sequence[str] = ()) -> int:
    """Write the run into the state, the flag, the flake log and the issue; the command's exit code."""
    state["lastRun"] = {"sha": sha, "at": at, "result": verdict}
    previous = state.get("red") or {}
    green = (state.get("lastGreen") or {})
    try:
        append_flakes(folder, [*flake_records(result, sha, at),
                               *({"at": at, "sha": sha, "stage": name, "outcome": "transient"} for name in transient)])
    except OSError as error:
        note(f"note: the flake log was not written: {error}")
    if verdict == INCONCLUSIVE_RESULT:
        write_state(folder, state)
        note("the flag and the issue are left as they were")
        return INCONCLUSIVE
    if verdict == GREEN_RESULT:
        state["lastGreen"] = {"sha": sha, "at": at}
        state["red"] = None
        cleared = red_flag.clear(folder)  # the flag first: a failure between the two leaves a state that repairs itself
        write_state(folder, state)
        number = previous.get("issue")
        if issues and number:
            try:
                gh(["issue", "comment", str(number), "--body-file", "-"],
                   f"Main is green again at `{sha}` ({at}): the rolling smoke passed. The flag is cleared. "
                   "Close this issue when the fix is merged.")
            except GhError as error:
                note(f"note: no comment on #{number}: {error}")
        note(("flag cleared; " if cleared else "") + f"green recorded at {sha[:10]}")
        return PASS
    stages = failing_stages(result)
    red = {"firstRed": previous.get("firstRed") or sha, "firstRedAt": previous.get("firstRedAt") or at, "checked": sha,
           "checkedAt": at, "lastGreen": green.get("sha"), "lastGreenAt": green.get("at"), "stages": stages, "zones": zones,
           "issue": previous.get("issue")}
    details: list[str] = []
    for name in (result.failed or [])[:6]:
        details.append(f"{name}:")
        details.extend(f"  {line}" for line in daily.failure_lines(result.details.get(name, [])))
    red["details"] = details
    return publish_red(root=root, folder=folder, state=state, red=red, issues=issues, gh=gh, note=note)


def publish_red(*, root: Path, folder: Path, state: dict[str, Any], red: dict[str, Any], issues: bool, gh: Gh,
                note: Callable[[str], None]) -> int:
    """File or update the issue of a red state, then write the flag, then the state (the flag first, so a failure between
    them never leaves a state that says red for a commit whose flag is missing)."""
    range_lines, range_note = merge_range(root, red.get("lastGreen"), red["checked"])
    if issues:
        try:
            body = issue_body(red, range_lines, range_note, compare_url(root, red.get("lastGreen"), red["checked"]),
                              red.get("details") or [])
            red["issue"] = file_issue(gh, red, body, red.get("issue"))
            note(f"issue #{red['issue']} filed or updated")
        except (GhError, ValueError) as error:
            note(f"note: the issue was not filed or updated (the next run of this commit or a later one tries again): {error}")
    red_flag.write({key: red.get(key) for key in ("firstRed", "firstRedAt", "checked", "checkedAt", "lastGreen", "stages", "issue")}, folder)
    state["red"] = red
    write_state(folder, state)
    note(f"flag written: {red_flag.text(red).splitlines()[0]}")
    return RED


# --- the command -----------------------------------------------------------------------------------------------

def lock(root: Path) -> int:
    """One run at a time per repository: an exclusive lock in its git folder, held until the process ends."""
    path = Path(package.git(root, "rev-parse", "--path-format=absolute", "--git-common-dir")) / LOCK
    handle = os.open(path, os.O_RDWR | os.O_CREAT, 0o600)
    try:
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError as error:
        os.close(handle)
        raise package.PackageError("another ./arcanum main-smoke is running") from error
    return handle


def launchd_plist(root: Path, environ: dict[str, str]) -> bytes:
    """The launchd agent that runs the command every INTERVAL_SECONDS: from a fresh worktree of origin/main (as the daily's
    agent does, so the command itself is never stale), under caffeinate."""
    script = (f'REPO={shlex.quote(str(root))}; git -C "$REPO" fetch origin main -q || exit 1; '
              'W=$(mktemp -d "${TMPDIR:-/tmp}/arcanum-launch.XXXXXX"); git -C "$REPO" worktree add --detach -q "$W/wt" origin/main || exit 1; '
              'cd "$W/wt" && /usr/bin/caffeinate -i ./arcanum main-smoke; code=$?; '
              'cd /; git -C "$REPO" worktree remove --force "$W/wt"; rm -rf "$W"; exit $code')
    log = str(Path.home() / "Library/Logs/arcanum-main-smoke.log")
    return plistlib.dumps({
        "Label": LABEL,
        "ProgramArguments": ["/bin/zsh", "-c", script],
        "StartInterval": INTERVAL_SECONDS,
        "EnvironmentVariables": {"PATH": environ.get("PATH", "/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin"), "HOME": str(Path.home())},
        "StandardOutPath": log,
        "StandardErrorPath": log,
        "RunAtLoad": False,
    })


def configure(command: argparse.ArgumentParser) -> None:
    """The `./arcanum main-smoke` arguments."""
    command.add_argument("--force", action="store_true", help="run although main has not moved since the last green or red run")
    command.add_argument("--no-fetch", action="store_true", help="use origin/main as it is, without fetching it first")
    command.add_argument("--keep-work", action="store_true", help="keep the temporary worktree for a look (until the next run)")
    command.add_argument("--no-issue", action="store_true", help="do not touch GitHub: the state and the flag are still written")
    command.add_argument("--zones", default=ZONES, help=f"the smoke zones to run (default {ZONES}; see ./arcanum smoke pr --help)")
    command.add_argument("--dry-run", action="store_true", help="fetch, say whether it would run and print the command; run nothing else")
    command.add_argument("--print-launchd", action="store_true", help="print the launchd agent (every 2 hours) for "
                         "~/Library/LaunchAgents/" + LABEL + ".plist and exit; the owner loads it")


def run(args: argparse.Namespace) -> int:
    if args.print_launchd:
        sys.stdout.write(launchd_plist(package.main_checkout(), dict(os.environ)).decode("utf-8"))
        return PASS

    def stopped(number: int, _frame: object) -> None:
        for other in (signal.SIGTERM, signal.SIGHUP):
            signal.signal(other, signal.SIG_IGN)
        raise package.PackageError(f"stopped by signal {number}")
    for number in (signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, stopped)
    try:
        root = package.main_checkout()
        handle = lock(root) if not args.dry_run else None
        try:
            return main_smoke(root=root, folder=red_flag.directory(), fetch=not args.no_fetch, force=args.force,
                              keep_work=args.keep_work, dry_run=args.dry_run, issues=not args.no_issue, zones=args.zones)
        finally:
            if handle is not None:
                os.close(handle)
    except package.PackageError as error:
        print(f"MAIN-SMOKE FAIL {error}", file=sys.stderr)
        return INCONCLUSIVE
