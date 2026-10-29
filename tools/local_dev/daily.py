"""ARC-DEV-062: the daily package build, `./arcanum daily`: one command from a fresh origin/main to the player packages.

Fetches origin main and stops at once (SKIP) when both player packages already hold that commit (their BUILD.txt), unless
--force. Otherwise it checks the commit out into a fresh detached worktree in a temporary folder (never the owner's
checkout or working tree), and from there runs the commit's own commands: the root checks (CLAUDE.md §2: the solution's
restore, build and tests through the launch queue, the dat probe and oracle self-tests), the full map smoke chain
(`./arcanum smoke full`), then `./arcanum package mac` and `win` of that same commit, each told the chain's result for
its BUILD.txt. Failed root checks stop it before any smoke or package; a failed viewer build or import of the smoke, or a
stopped smoke, stops it before the packages; failed smoke stages only mark the packages (BUILD.txt `map-smoke FAIL ...`)
unless --strict. The report and each step's whole output go to artifacts/packages/logs/daily/ in the main checkout;
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
CHECKS_KEPT = frozenset({"ARCANUM_LOCAL_HOME", "ARCANUM_QUEUE_LEGACY_LOCK"})
# The smoke steps that are not smokes: the chain's viewer build and project import. Their failure means the commit does
# not build as a viewer, so no package is made from it.
SMOKE_GATES = ("build", "import")
SUMMARY_LINE = re.compile(r"^\s+(\S+)\s+(PASS|FAIL) exit=(\d+)$")
LOCK = "arcanum-daily.lock"
PASS, FAIL, PACKAGED_WITH_SMOKE_FAILURES = 0, 1, 3

Runner = Callable[[Sequence[str], Path, dict[str, str], Path], int]


@dataclass(frozen=True)
class Step:
    name: str
    command: tuple[str, ...]
    environment: str = "full"  # "full": the caller's; "checks": without the owner's opt-in variables


@dataclass
class SmokeResult:
    code: int
    passed: list[str] = field(default_factory=list)
    failed: list[str] = field(default_factory=list)
    not_run: list[str] = field(default_factory=list)

    @property
    def blocking(self) -> str | None:
        """Why no package may be made from this chain whatever the policy, or None."""
        if self.code >= smoke.STOPPED:
            return f"the smoke chain was stopped (exit {self.code})"
        gates = [name for name in self.failed if name in SMOKE_GATES]
        if gates:
            return "the smoke chain's viewer " + " and ".join(gates) + " failed"
        if not self.passed and not self.failed:
            return f"the smoke chain printed no summary (exit {self.code})"
        return None

    def line(self, log: Path) -> str:
        """BUILD.txt's map-smoke line: PASS with the smokes, or FAIL with the failed and not run ones and the log."""
        smokes = [name for name in self.passed if name not in SMOKE_GATES]
        if not self.failed and not self.not_run and self.code == 0:
            return "PASS (" + ", ".join(smokes) + ")"
        parts = [", ".join(self.failed)] if self.failed else []
        if self.not_run:
            parts.append("not run " + ", ".join(self.not_run))
        return "FAIL " + "; ".join(parts or [f"exit {self.code}"]) + (f"; passed {', '.join(smokes)}" if smokes else "") + f" (log {log})"


def say(message: str) -> None:
    print(f"DAILY {message}", flush=True)


def parse_smoke(text: str, code: int) -> SmokeResult:
    """The `./arcanum smoke` summary: each step's PASS or FAIL line and the `not run:` line."""
    result = SmokeResult(code)
    lines = text.splitlines()
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
    """The commit in a platform's BUILD.txt, or None when there is no readable one."""
    try:
        text = (folder / "BUILD.txt").read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None
    found = re.search(r"^commit ([0-9a-f]{40})$", text, re.MULTILINE)
    return found.group(1) if found else None


def check_steps(python: str) -> list[Step]:
    """The root checks (CLAUDE.md §2), run in the fresh checkout; dotnet through the launch queue."""
    return [
        Step("dotnet", ("./arcanum", "run", "--kind", "dotnet", "--label", "daily root checks", "--", "sh", "-c", DOTNET_CHECKS),
             "checks"),
        Step("dat-probe", (python, "-B", "tools/research/dat_probe.py", "--self-test"), "checks"),
        Step("oracle", (python, "-B", "tools/original-oracle/oracle.py", "--self-test"), "checks"),
    ]


def smoke_step() -> Step:
    return Step("smoke", ("./arcanum", "smoke", "full"))


def package_step(platform: str, commit: str, map_smoke: str) -> Step:
    return Step(f"package-{platform}", ("./arcanum", "package", platform, "--ref", commit, "--no-fetch", "--map-smoke", map_smoke))


def run_logged(command: Sequence[str], cwd: Path, environment: dict[str, str], log: Path) -> int:
    """The step's whole output into its log (the steps' own quiet lines stay there too); its exit code."""
    with log.open("wb") as handle:
        try:
            return subprocess.run(list(command), cwd=cwd, env=environment, stdin=subprocess.DEVNULL, stdout=handle,
                                  stderr=subprocess.STDOUT).returncode
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
        code = self.runner(step.command, self.checkout, environment, log)
        text = log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""
        self.note(f"{step.name} {'PASS' if code == 0 else 'FAIL'} exit={code} log={log}")
        return code, text


def daily(*, root: Path, output: Path, fetch: bool, force: bool, strict: bool, keep_work: bool, dry_run: bool,
          runner: Runner = run_logged, environ: dict[str, str] | None = None,
          now: Callable[[], datetime] = lambda: datetime.now(timezone.utc)) -> int:
    environ = dict(os.environ if environ is None else environ)
    if environ.get(run_queue.TICKET_ENV):
        raise package.PackageError("run ./arcanum daily on its own, not inside ./arcanum run: its steps take their own "
                                   "places in the queue")
    if fetch and not dry_run:
        say("fetch origin main")
        package.git(root, "fetch", "origin", "main")
    commit, date = package.resolve(root, "origin/main")
    folders = {key: output / platform.folder for key, platform in package.PLATFORMS.items()}
    current = {key: packaged_commit(folder) for key, folder in folders.items()}
    say(f"origin/main {commit} ({date}); packaged: " + ", ".join(f"{key} {(value or 'none')[:10]}" for key, value in current.items()))
    if not force and all(value == commit for value in current.values()):
        say(f"SKIP no new commits since the last package ({commit[:10]}); --force builds it again")
        return PASS
    python = sys.executable
    if dry_run:
        for step in [*check_steps(python), smoke_step(), *(package_step(key, commit, "<map smoke result>") for key in package.PLATFORMS)]:
            print(f"{step.name} [{step.environment} environment] {smoke.shown(step.command)}")
        say(f"dry run: would check out {commit[:10]} into a fresh worktree and run the steps above there; logs and "
            f"report in {output / 'logs' / 'daily'}")
        return PASS
    logs = output / "logs" / "daily"
    if logs.exists():
        shutil.rmtree(logs)
    logs.mkdir(parents=True)
    # Resolved, as package.py: a locked restore given the /var link path of the temporary folder refuses (NU1004).
    work = Path(tempfile.mkdtemp(prefix="arcanum-daily-")).resolve()
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
        blocking = result.blocking or (f"--strict and the smoke failed ({', '.join(result.failed + result.not_run)})"
                                       if strict and (result.failed or result.not_run or smoke_code) else None)
        if blocking:
            day.note(f"RESULT FAIL {blocking}: no package")
            return code
        line = result.line(logs / "smoke.log")
        day.note(f"map-smoke {line}")
        packaged = [key for key in package.PLATFORMS if day.run(package_step(key, commit, line))[0] == 0]
        failed = [key for key in package.PLATFORMS if key not in packaged]
        if failed:
            day.note(f"RESULT FAIL package {', '.join(failed)} failed" + (f" (installed: {', '.join(packaged)})" if packaged else ""))
        elif line.startswith("PASS"):
            code = PASS
            day.note(f"RESULT PASS packaged {commit[:10]} for {', '.join(packaged)}")
        else:
            code = PACKAGED_WITH_SMOKE_FAILURES
            day.note(f"RESULT PASS WITH SMOKE FAILURES packaged {commit[:10]} for {', '.join(packaged)}; map-smoke {line}")
        return code
    except package.PackageError as error:
        day.note(f"RESULT FAIL {error}")
        return FAIL
    finally:
        if keep_work:
            say(f"work kept at {work}")
        else:
            if added:
                try:
                    package.git(root, "worktree", "remove", "--force", str(checkout))
                except package.PackageError as error:
                    say(f"note: {error}")
            shutil.rmtree(work, ignore_errors=True)
            if added:
                try:
                    package.git(root, "worktree", "prune")
                except package.PackageError:
                    pass
        (logs / "report.txt").write_text("\n".join(day.report) + "\n", encoding="utf-8")
        say(f"report {logs / 'report.txt'}")


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
    command.add_argument("--force", action="store_true", help="build even when both packages already hold origin/main")
    command.add_argument("--strict", action="store_true", help="make no package when any smoke of the chain failed "
                         "(default: package and mark the failed smokes in BUILD.txt)")
    command.add_argument("--no-fetch", action="store_true", help="use origin/main as it is, without fetching it first")
    command.add_argument("--keep-work", action="store_true", help="keep the temporary worktree for a look")
    command.add_argument("--dry-run", action="store_true", help="say whether it would build and print the steps; run nothing")


def run(args: argparse.Namespace) -> int:
    def stopped(number: int, _frame: object) -> None:
        raise package.PackageError(f"stopped by signal {number}")
    # A stop (SIGTERM, SIGHUP) unwinds like an error, so the temporary worktree is removed.
    for number in (signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, stopped)
    try:
        root = package.main_checkout()
        handle = lock(root)
        try:
            return daily(root=root, output=root / "artifacts" / "packages", fetch=not args.no_fetch, force=args.force,
                         strict=args.strict, keep_work=args.keep_work, dry_run=args.dry_run)
        finally:
            os.close(handle)
    except package.PackageError as error:
        print(f"DAILY FAIL {error}", file=sys.stderr)
        return FAIL
