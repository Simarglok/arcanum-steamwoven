"""ARC-DEV-062: the daily package build, `./arcanum daily`: one command from a fresh origin/main to the player packages.

Fetches origin main and stops at once (SKIP) when both player packages were already made by it from that commit (their
BUILD.txt), unless --force. Otherwise it checks the commit out into a fresh detached worktree in a temporary folder (never
the owner's checkout or working tree), and from there runs the commit's own commands: the root checks (CLAUDE.md §2: the
solution's restore, build and tests through the launch queue, the dat probe and oracle self-tests), the full map smoke
chain (`./arcanum smoke full`), then `./arcanum package mac` and `win` of that same commit, each told the chain's result
for its BUILD.txt. Owner decision 2026-10-29: any failure blocks the packages; a failed check or smoke stops the day
before packaging and the report names each failed step with its run log and first error, so a fix thread can start; the
package is built by the first day after the fix. The report and each step's whole output go to
artifacts/packages/logs/daily/ in the main checkout;
nothing else under artifacts/packages is written here (the packages' bug reports are never touched). The rule:
docs/workflow/packaging.md#daily-package-build-arc-dev-062.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass, field
from datetime import datetime, timezone
import fcntl
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
from typing import Callable, Sequence

from . import package, run_queue, smoke

DOTNET_CHECKS = ('dotnet restore Arcanum.Steamwoven.sln --locked-mode && dotnet build --no-restore && '
                 'dotnet test --no-build --filter "TestCategory!=RealData"')
# The owner's opt-in data and settings variables (DAT_ROOT, ARC_*, ARCANUM_*) would turn on real-data tests in the root
# checks; they stay synthetic, as in the PR checks. The launcher's home (the queue and its configuration) is kept.
CHECKS_KEPT = frozenset({"ARCANUM_LOCAL_HOME", "ARCANUM_QUEUE_LEGACY_LOCK", "ARCANUM_ORACLE_VENV"})
# The oracle self-test installs its locked Unicorn into this venv when it has none; a fresh worktree has no local/, so
# the run reuses the main checkout's venv (a cache of the lock's wheels) instead of installing from the network each day.
ORACLE_VENV = Path("local/original-oracle/venv")
CHECK_MINUTES = 30.0  # the unqueued self-tests; the queued steps have the queue's limits
TEMP_PREFIX = "arcanum-daily-"
# The smoke steps that are not smokes: the chain's viewer build and project import. Their failure means the commit does
# not build as a viewer, so no package is made from it.
SMOKE_GATES = ("build", "import")
SUMMARY_LINE = re.compile(r"^\s+(\S+)\s+(PASS|FAIL) exit=(\d+)$")
LOCK = "arcanum-daily.lock"
DAILY_RAN = re.compile(r"^map-smoke PASS\b", re.MULTILINE)  # BUILD.txt of a package that ./arcanum daily made
DETAIL_LINES = 12  # a failed smoke's lines in the report: its run's FAIL line (with the log) and the first error
PASS, FAIL = 0, 1

Runner = Callable[[Sequence[str], Path, dict[str, str], Path, float | None], int]


@dataclass(frozen=True)
class Step:
    name: str
    command: tuple[str, ...]
    environment: str = "full"  # "full": the caller's; "checks": without the owner's opt-in variables
    minutes: float | None = None  # a limit for a step outside the queue


@dataclass
class SmokeResult:
    code: int
    passed: list[str] = field(default_factory=list)
    failed: list[str] = field(default_factory=list)
    not_run: list[str] = field(default_factory=list)
    details: dict[str, list[str]] = field(default_factory=dict)  # each step's own lines (its run's PASS/FAIL, first error)

    @property
    def failure(self) -> str | None:
        """Why no package may be made from this chain, or None when every step passed."""
        if self.code >= smoke.STOPPED:
            return f"the smoke chain was stopped (exit {self.code})"
        gates = [name for name in self.failed if name in SMOKE_GATES]
        if gates:
            return "the smoke chain's viewer " + " and ".join(gates) + " failed"
        if not self.passed and not self.failed:
            return f"the smoke chain printed no summary (exit {self.code})"
        if self.failed or self.not_run or self.code:
            return ("smoke failed: " + (", ".join(self.failed) or f"exit {self.code}")
                    + (f" (not run {', '.join(self.not_run)})" if self.not_run else ""))
        return None

    def line(self) -> str:
        """BUILD.txt's map-smoke line of a passing chain: the smokes that ran."""
        return "PASS (" + ", ".join(name for name in self.passed if name not in SMOKE_GATES) + ")"


def say(message: str) -> None:
    print(f"DAILY {message}", flush=True)


def parse_smoke(text: str, code: int) -> SmokeResult:
    """The `./arcanum smoke` summary: each step's PASS or FAIL line and the `not run:` line."""
    result = SmokeResult(code)
    lines = text.splitlines()
    name = None
    for line in lines:
        if line.startswith("SMOKE step "):
            name = line.split()[2]
            result.details[name] = []
        elif line.startswith("SMOKE "):
            name = None
        elif name is not None and line.strip() and not line.startswith("ARCANUM_QUEUE "):
            result.details[name].append(line.rstrip())
    try:
        start = max(index for index, line in enumerate(lines) if line.strip() == "SMOKE summary") + 1
    except ValueError:
        return result
    for line in lines[start:]:
        found = SUMMARY_LINE.match(line)
        if found:
            (result.passed if found.group(2) == "PASS" else result.failed).append(found.group(1))
        elif line.strip().startswith("not run:"):
            result.not_run = [name.strip() for name in line.split(":", 1)[1].split(",") if name.strip()]
    return result


def checks_environment(environ: dict[str, str]) -> dict[str, str]:
    return {key: value for key, value in environ.items()
            if key in CHECKS_KEPT or key in smoke.KEPT_VARIABLES or not key.startswith(smoke.DROPPED_PREFIXES)}


def packaged_commit(folder: Path) -> str | None:
    """The commit in a platform's BUILD.txt when ./arcanum daily made that package (its map-smoke line holds the chain's
    PASS or FAIL), else None: a package made by hand (map-smoke NOT RUN) never had the chain, so the day builds again."""
    try:
        text = (folder / "BUILD.txt").read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None
    found = re.search(r"^commit ([0-9a-f]{40})$", text, re.MULTILINE)
    return found.group(1) if found and DAILY_RAN.search(text) else None


def failed_attempt(logs: Path) -> str | None:
    """The commit of the last daily run when it ended in RESULT FAIL (its report), else None."""
    try:
        text = (logs / "report.txt").read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None
    found = re.search(r"^daily package build \S+ commit ([0-9a-f]{40}) ", text, re.MULTILINE)
    return found.group(1) if found and re.search(r"^RESULT FAIL ", text, re.MULTILINE) else None


def check_steps(python: str) -> list[Step]:
    """The root checks (CLAUDE.md §2), run in the fresh checkout; dotnet through the launch queue."""
    return [
        Step("dotnet", ("./arcanum", "run", "--kind", "dotnet", "--label", "daily root checks", "--", "sh", "-c", DOTNET_CHECKS),
             "checks"),
        Step("dat-probe", (python, "-B", "tools/research/dat_probe.py", "--self-test"), "checks", CHECK_MINUTES),
        Step("oracle", (python, "-B", "tools/original-oracle/oracle.py", "--self-test"), "checks", CHECK_MINUTES),
    ]


def smoke_step() -> Step:
    return Step("smoke", ("./arcanum", "smoke", "full"))


def package_step(platform: str, commit: str, map_smoke: str) -> Step:
    return Step(f"package-{platform}", ("./arcanum", "package", platform, "--ref", commit, "--no-fetch", "--map-smoke", map_smoke))


def run_logged(command: Sequence[str], cwd: Path, environment: dict[str, str], log: Path, minutes: float | None) -> int:
    """The step's whole output into its log (the steps' own quiet lines stay there too); its exit code."""
    with log.open("wb") as handle:
        try:
            return subprocess.run(list(command), cwd=cwd, env=environment, stdin=subprocess.DEVNULL, stdout=handle,
                                  stderr=subprocess.STDOUT, timeout=None if minutes is None else minutes * 60).returncode
        except subprocess.TimeoutExpired:
            handle.write(f"\ndaily: stopped after {minutes:g} minutes\n".encode())
            return run_queue.TIMEOUT_EXIT
        except OSError as error:
            handle.write(f"daily: could not start {command[0]}: {error}\n".encode())
            return 127


class Daily:
    """One run: its steps in order, each result into the report."""

    def __init__(self, checkout: Path, logs: Path, environ: dict[str, str], runner: Runner) -> None:
        self.checkout, self.logs, self.environ, self.runner = checkout, logs, environ, runner
        self.report: list[str] = []

    def note(self, line: str) -> None:
        self.report.append(line)
        say(line)

    def run(self, step: Step) -> tuple[int, str]:
        log = self.logs / f"{step.name}.log"
        environment = checks_environment(self.environ) if step.environment == "checks" else dict(self.environ)
        say(f"step {step.name}")
        code = self.runner(step.command, self.checkout, environment, log, step.minutes)
        text = log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""
        self.note(f"{step.name} {'PASS' if code == 0 else 'FAIL'} exit={code} log={log}")
        return code, text


def remove_stale(root: Path, temporary: Path) -> list[Path]:
    """The temporary folders an earlier daily run left (killed, or --keep-work), with their worktree registrations; only
    called under the lock, so no running day owns them."""
    removed = []
    listed = package.git(root, "worktree", "list", "--porcelain").splitlines()
    for path in (Path(line[len("worktree "):]) for line in listed if line.startswith("worktree ")):
        if path.parent.name.startswith(TEMP_PREFIX) and path.parent.parent == temporary:
            package.git(root, "worktree", "remove", "--force", str(path))
    for folder in sorted(temporary.glob(TEMP_PREFIX + "*")):
        if folder.is_dir() and not folder.is_symlink():
            shutil.rmtree(folder, ignore_errors=True)
            removed.append(folder)
    return removed


def daily(*, root: Path, output: Path, fetch: bool, force: bool, keep_work: bool, dry_run: bool,
          runner: Runner = run_logged, environ: dict[str, str] | None = None,
          now: Callable[[], datetime] = lambda: datetime.now(timezone.utc)) -> int:
    environ = dict(os.environ if environ is None else environ)
    if environ.get(run_queue.TICKET_ENV):
        raise package.PackageError("run ./arcanum daily on its own, not inside ./arcanum run: its steps take their own "
                                   "places in the queue")
    if fetch:
        say("fetch origin main")
        package.git(root, "fetch", "origin", "main")
    commit, date = package.resolve(root, "origin/main")
    folders = {key: output / platform.folder for key, platform in package.PLATFORMS.items()}
    current = {key: packaged_commit(folder) for key, folder in folders.items()}
    say(f"origin/main {commit} ({date}); packaged by daily: "
        + ", ".join(f"{key} {(value or 'none')[:10]}" for key, value in current.items()))
    if not force and all(value == commit for value in current.values()):
        say(f"SKIP no new commits since the last daily package ({commit[:10]}); --force builds it again")
        return PASS
    if not force and failed_attempt(output / "logs" / "daily") == commit:
        say(f"SKIP the last daily run already failed on {commit[:10]} (report {output / 'logs' / 'daily' / 'report.txt'}); "
            "the fix's commit builds, --force tries this one again")
        return FAIL  # the packages are not at main and main still fails
    python = sys.executable
    environ.setdefault("ARCANUM_ORACLE_VENV", str(root / ORACLE_VENV))
    if dry_run:
        for step in [*check_steps(python), smoke_step(), *(package_step(key, commit, "<map smoke result>") for key in package.PLATFORMS)]:
            print(f"{step.name} [{step.environment} environment] {smoke.shown(step.command)}")
        say(f"dry run: would check out {commit[:10]} into a fresh worktree and run the steps above there; logs and "
            f"report in {output / 'logs' / 'daily'}")
        return PASS
    logs = output / "logs" / "daily"
    if logs.is_symlink() or logs.exists() and not logs.is_dir():
        raise package.PackageError(f"{logs} must be a real folder")
    if logs.exists():
        shutil.rmtree(logs)
    logs.mkdir(parents=True)
    # Resolved, as package.py: a locked restore given the /var link path of the temporary folder refuses (NU1004).
    temporary = Path(tempfile.gettempdir()).resolve()
    for folder in remove_stale(root, temporary):
        say(f"removed the leftover {folder}")
    work = Path(tempfile.mkdtemp(prefix=TEMP_PREFIX, dir=temporary))
    checkout = work / "checkout"
    day = Daily(checkout, logs, environ, runner)
    day.note(f"daily package build {now().strftime('%Y-%m-%dT%H:%M:%SZ')} commit {commit} ({date})")
    code = FAIL
    added = False
    try:
        package.git(root, "worktree", "add", "--detach", str(checkout), commit)
        added = True
        day.note(f"checkout {checkout}")
        failed = [step.name for step in check_steps(python) if day.run(step)[0] != 0]
        if failed:
            day.note(f"RESULT FAIL root checks failed ({', '.join(failed)}): no smoke, no package")
            return code
        smoke_code, text = day.run(smoke_step())
        result = parse_smoke(text, smoke_code)
        day.note("smoke summary: passed " + (", ".join(result.passed) or "none") + "; failed " + (", ".join(result.failed) or "none")
                 + (f"; not run {', '.join(result.not_run)}" if result.not_run else ""))
        failure = result.failure
        if failure:
            for name in result.failed:
                day.note(f"failed smoke {name}:")
                for detail in result.details.get(name, [])[:DETAIL_LINES]:
                    day.note(f"  {detail}")
            day.note(f"RESULT FAIL {failure}: no package (the packages stay at their last commit)")
            return code
        line = result.line()
        day.note(f"map-smoke {line}")
        packaged = [key for key in package.PLATFORMS if day.run(package_step(key, commit, line))[0] == 0]
        failed = [key for key in package.PLATFORMS if key not in packaged]
        if failed:
            day.note(f"RESULT FAIL package {', '.join(failed)} failed" + (f" (installed: {', '.join(packaged)})" if packaged else ""))
        else:
            code = PASS
            day.note(f"RESULT PASS packaged {commit[:10]} for {', '.join(packaged)}")
        return code
    except package.PackageError as error:
        day.note(f"RESULT FAIL {error}")
        return FAIL
    finally:
        # A second stop while cleaning up must not leave the worktree behind (the next day removes it anyway).
        held = {number: signal.signal(number, signal.SIG_IGN) for number in (signal.SIGTERM, signal.SIGHUP)}
        try:
            if keep_work:
                say(f"work kept at {work} (the next daily run removes it)")
            else:
                if added:
                    try:
                        package.git(root, "worktree", "remove", "--force", str(checkout))
                    except package.PackageError as error:
                        say(f"note: {error}")
                shutil.rmtree(work, ignore_errors=True)
            (logs / "report.txt").write_text("\n".join(day.report) + "\n", encoding="utf-8")
            say(f"report {logs / 'report.txt'}")
        finally:
            for number, handler in held.items():
                signal.signal(number, handler)


def lock(root: Path) -> int:
    """One daily run at a time per repository: an exclusive lock in its git folder, held until the process ends."""
    path = Path(package.git(root, "rev-parse", "--path-format=absolute", "--git-common-dir")) / LOCK
    handle = os.open(path, os.O_RDWR | os.O_CREAT, 0o600)
    try:
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError as error:
        os.close(handle)
        raise package.PackageError("another ./arcanum daily is running") from error
    return handle


def configure(command: argparse.ArgumentParser) -> None:
    """The `./arcanum daily` arguments."""
    command.add_argument("--force", action="store_true", help="build even when both packages were made by daily from origin/main")
    command.add_argument("--no-fetch", action="store_true", help="use origin/main as it is, without fetching it first")
    command.add_argument("--keep-work", action="store_true", help="keep the temporary worktree for a look (until the next daily run)")
    command.add_argument("--dry-run", action="store_true", help="fetch, say whether it would build and print the steps; "
                         "run nothing else")


def run(args: argparse.Namespace) -> int:
    def stopped(number: int, _frame: object) -> None:
        raise package.PackageError(f"stopped by signal {number}")
    # A stop (SIGTERM, SIGHUP) unwinds like an error, so the temporary worktree is removed.
    for number in (signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, stopped)
    try:
        root = package.main_checkout()
        handle = lock(root) if not args.dry_run else None
        try:
            return daily(root=root, output=root / "artifacts" / "packages", fetch=not args.no_fetch, force=args.force,
                         keep_work=args.keep_work, dry_run=args.dry_run)
        finally:
            if handle is not None:
                os.close(handle)
    except package.PackageError as error:
        print(f"DAILY FAIL {error}", file=sys.stderr)
        return FAIL
