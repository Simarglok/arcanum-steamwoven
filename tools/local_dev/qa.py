"""ARC-DEV-2187: ``./arcanum qa``, the owner's local check page (slice 2a of the owner QA epic #2185).

A localhost-only page (Python standard library only) lists the steps of ``docs/project/owner-recheck-list.md`` as the parser of
ARC-DEV-2186 (``owner_checks``) reads them from ``origin/main`` after a fetch, and records the owner's result per step:
``same`` / ``differs`` / ``cannot-test`` / ``open``, a note and one Ctrl+B report folder (its name only). Results are
appended to ``local/qa/results.jsonl`` of the main checkout (git-ignored). Marking a step ``differs`` creates the GitHub issue
at once with the local ``gh``: text only, never a screenshot, save or log; the issue number is stored, so a retry or a repeat
never creates a second one. ``./arcanum qa sync`` sends the ones that failed. The rule is docs/workflow/toolchain.md#owner-qa-page-arc-dev-2187.

Nothing here writes the main checkout's tracked files, and nothing reads a report except the first line of the player's
text and the ``Check: ARC-TYPE-NNN#k`` line in the head of ``report.md``.

ARC-DEV-2189 (slice 4 of the epic): when the owner opens a step, the page writes its id as one line into ``local/qa/active-check.txt``
(git-ignored); Ctrl+B of the viewer reads it, shows it in the report window and writes ``Check: ARC-TYPE-NNN#k`` into the report, which
the page reads back to preselect and list a step's reports. The file is removed when the page stops and when it starts.

ARC-DEV-2190 (slice 5): the page also writes ``local/qa/checks.json`` (the list snapshot) so the in-game overlay (Ctrl+Q) lists the steps
without Python; the game appends its results to the same ``results.jsonl`` (``src: game``, ``build``), and the running page creates the
issue of a new ``differs`` result every ``AUTO_SYNC_SECONDS`` (``qa sync`` does the same on demand, exactly once per step). The overlay is
off in a normal build; ``./arcanum qa overlay on|off`` writes or removes the marker ``local/qa/owner-qa.on`` (``ARC_OWNER_QA=1`` also turns it on).
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import datetime
import fcntl
import hmac
import json
import os
from pathlib import Path
import re
import secrets
import socketserver
import signal
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

from . import owner_checks

ROOT = Path(__file__).resolve().parents[2]
PAGE = Path(__file__).with_name("qa_page.html")
RESULT_STATUSES = owner_checks.STATUSES          # open | same | differs | cannot-test
REPORT_NAME = re.compile(r"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(?:_\d+)?$")   # BugReport.cs: reports/YYYY-MM-DD_HH-MM-SS[_n]
# the task types of docs/workflow/task-naming.md that have a `type:*` label
TYPE_LABELS = frozenset({"res", "data", "render", "world", "input", "gpad", "ui", "script", "dev", "ai", "combat", "skills",
                         "magic", "technology"})
MAX_NOTE = 4000
MAX_BODY = 64 * 1024
GH_TIMEOUT = 60
START_WITHIN_SECONDS = 60
TOKEN_HEADER = "X-QA-Token"
PLAYER_TEXT_LIMIT = 120
ACTIVE_FILE = "active-check.txt"     # the one line the viewer's Ctrl+B reads (BugReportCheck.cs); keep the name in step with it
CHECKS_FILE = "checks.json"          # the list snapshot the in-game overlay reads (OwnerQa.cs, ARC-DEV-2190)
OVERLAY_MARKER = "owner-qa.on"       # its presence turns the overlay on in a build started from this checkout (OwnerQa.cs)
AUTO_SYNC_SECONDS = 20               # the running page creates the issues of differs steps recorded by the game this often ...
SETTLE_SECONDS = 20                  # ... once the step's latest result is this old (a note or a report may still follow the mark)
CHECK_PREFIX = "Check: "              # the report.md line of BugReportCheck.LinePrefix


def main_checkout(root: Path = ROOT) -> Path:
    """The main checkout of this repository (``local/`` and ``artifacts/`` live there, also when run from a worktree)."""
    try:
        common = subprocess.check_output(["git", "-C", str(root), "rev-parse", "--path-format=absolute", "--git-common-dir"],
                                         text=True, stderr=subprocess.DEVNULL).strip()
    except (OSError, subprocess.CalledProcessError) as error:
        raise ValueError("run ./arcanum qa from a git checkout of the project") from error
    return Path(common).parent


def default_data_dir(root: Path = ROOT) -> Path:
    return main_checkout(root) / "local" / "qa"


def default_reports_dir(root: Path = ROOT) -> Path:
    return main_checkout(root) / "artifacts" / "packages" / "macos" / "reports"


def _age_seconds(ts: object) -> float:
    """Seconds since an ISO ``...Z`` time (a time that cannot be read counts as old, so such a step is not held back)."""
    try:
        moment = datetime.datetime.strptime(str(ts), "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=datetime.timezone.utc)
    except ValueError:
        return float("inf")
    return (datetime.datetime.now(datetime.timezone.utc) - moment).total_seconds()


def now_iso() -> str:
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


# ---------------------------------------------------------------------------------------------------------------- results


class Store:
    """``results.jsonl``: an append-only log of events, the latest event of a step wins (a crash or a second process never
    loses a line); ``state.json``: the step ids the owner has seen, for the "new since the last visit" filter.

    A ``result`` event carries status, note and report folder name; an ``issue`` event carries the number of the issue
    created for the step (sticky: later results keep it) or the error of a failed attempt."""

    def __init__(self, directory: Path) -> None:
        self.directory = Path(directory)
        self.results_path = self.directory / "results.jsonl"
        self.state_path = self.directory / "state.json"
        self.lock_path = self.directory / ".lock"
        self.active_path = self.directory / ACTIVE_FILE
        self._active_lock = threading.Lock()          # the server answers on several threads

    @contextmanager
    def locked(self):
        """One writer at a time over every thread and process (flock on separate descriptors conflicts, also in one process)."""
        self.directory.mkdir(parents=True, exist_ok=True)
        with open(self.lock_path, "a") as handle:
            fcntl.flock(handle, fcntl.LOCK_EX)
            try:
                yield
            finally:
                fcntl.flock(handle, fcntl.LOCK_UN)

    def read(self) -> tuple[dict[str, dict], int]:
        """The merged current result of every step that has events, and the number of unreadable lines (skipped)."""
        merged: dict[str, dict] = {}
        bad = 0
        try:
            text = self.results_path.read_text(encoding="utf-8")
        except FileNotFoundError:
            return merged, 0
        for line in text.split("\n"):
            if not line.strip():
                continue
            try:
                event = json.loads(line)
                step = event["step"]
                kind = event["kind"]
                if not isinstance(step, str) or kind not in ("result", "issue"):
                    raise ValueError("bad event")
            except (ValueError, KeyError, TypeError):
                bad += 1
                continue
            current = merged.setdefault(step, {"step": step, "status": "open", "note": "", "report": None, "ts": event.get("ts"),
                                               "issue": None, "issueUrl": None, "issueError": None,
                                               "recorded": False})
            if kind == "result":
                status = event.get("status")
                if status not in RESULT_STATUSES:
                    bad += 1
                    continue
                current.update(status=status, note=str(event.get("note") or ""), report=event.get("report") or None, ts=event.get("ts"),
                               recorded=True)
            elif isinstance(event.get("issue"), int) and not isinstance(event.get("issue"), bool):
                current.update(issue=event["issue"], issueUrl=event.get("url") or None, issueError=None)
            elif event.get("error"):
                current["issueError"] = str(event["error"])
        return merged, bad

    def append(self, event: dict) -> None:
        """Append one line (the caller holds the lock). A last line cut short by a crash is closed first."""
        self.directory.mkdir(parents=True, exist_ok=True)
        line = json.dumps(event, ensure_ascii=False, separators=(",", ":")) + "\n"
        prefix = ""
        try:
            with open(self.results_path, "rb") as existing:
                existing.seek(0, os.SEEK_END)
                if existing.tell() > 0:
                    existing.seek(-1, os.SEEK_END)
                    if existing.read(1) != b"\n":
                        prefix = "\n"
        except FileNotFoundError:
            pass
        with open(self.results_path, "ab") as handle:
            handle.write((prefix + line).encode("utf-8"))
            handle.flush()
            os.fsync(handle.fileno())

    def write_active(self, step_id: str) -> None:
        """The step the owner has open, one line, replaced whole (the viewer may read it at any moment)."""
        with self._active_lock:
            self.directory.mkdir(parents=True, exist_ok=True)
            handle, name = tempfile.mkstemp(prefix=".active-", suffix=".tmp", dir=self.directory)
            try:
                with os.fdopen(handle, "w", encoding="utf-8") as stream:
                    stream.write(step_id + "\n")
                os.replace(name, self.active_path)
            except OSError:
                try:
                    os.unlink(name)
                except OSError:
                    pass
                raise

    def write_checks(self, commit: str, ref: str, steps: list[owner_checks.Step]) -> None:
        """The list snapshot for the in-game overlay, replaced whole (text only: id, section title, step, expected, list status)."""
        data = {"version": 1, "commit": commit, "ref": ref, "ts": now_iso(),
                "steps": [{"id": step.id, "section": step.section_title, "step": step.step, "expected": step.expected, "status": step.status}
                          for step in steps]}
        self.directory.mkdir(parents=True, exist_ok=True)
        handle, name = tempfile.mkstemp(prefix=".checks-", suffix=".tmp", dir=self.directory)
        try:
            with os.fdopen(handle, "w", encoding="utf-8") as stream:
                json.dump(data, stream, ensure_ascii=False, separators=(",", ":"))
            os.replace(name, self.directory / CHECKS_FILE)
        except OSError:
            try:
                os.unlink(name)
            except OSError:
                pass
            raise

    def clear_active(self) -> None:
        with self._active_lock:
            try:
                self.active_path.unlink()
            except FileNotFoundError:
                pass
            except OSError as error:                  # a file that cannot be removed must not stop the page or its exit
                print(f"qa: could not remove {self.active_path}: {error}", file=sys.stderr)

    def seen(self) -> set[str] | None:
        """The step ids of the last visit; None when there was none."""
        try:
            data = json.loads(self.state_path.read_text(encoding="utf-8"))
            ids = data["seen"]
            return {item for item in ids if isinstance(item, str)} if isinstance(ids, list) else None
        except (OSError, ValueError, KeyError, TypeError):
            return None

    def write_seen(self, ids: list[str]) -> None:
        with self.locked():
            temporary = self.state_path.with_suffix(".tmp")
            temporary.write_text(json.dumps({"seen": sorted(ids), "ts": now_iso()}), encoding="utf-8")
            os.replace(temporary, self.state_path)


# ---------------------------------------------------------------------------------------------------------------- reports


def report_head(report_md: Path) -> tuple[str, str]:
    """(the first line of the player's text, the step id of the ``Check:`` line) of a report's ``report.md``, '' for each that is
    absent; only its head is read. The check line counts only before the first ``##`` heading, so the player's own text (quoted
    with ``>``, below that heading) cannot set it."""
    try:
        with open(report_md, "rb") as handle:
            head = handle.read(8192).decode("utf-8", errors="replace")
    except OSError:
        return "", ""
    text, check = "", ""
    inside, headed = False, False
    for line in head.split("\n"):
        if line.startswith("## "):
            headed = True
            if inside:
                break
            inside = line.strip() == "## What the player wrote"
        elif not headed and not check and line.startswith(CHECK_PREFIX):
            candidate = line[len(CHECK_PREFIX):].strip()
            check = candidate if owner_checks.STEP_ID.match(candidate) else ""
        elif inside and not text and line.startswith(">"):
            text = line[1:].strip()[:PLAYER_TEXT_LIMIT]
            if check:
                break
    return text, check


def player_text(report_md: Path) -> str:
    """The first line of the player's text in a report's ``report.md`` ('' when there is none); only its head is read."""
    return report_head(report_md)[0]


def list_reports(directory: Path) -> list[dict]:
    """The report folders (name, local time, epoch seconds, first line of the player's text, the step id its ``Check:`` line names
    or None), newest first."""
    found = []
    try:
        entries = list(os.scandir(directory))
    except OSError:
        return found
    for entry in entries:
        if not REPORT_NAME.match(entry.name) or not entry.is_dir(follow_symlinks=False):
            continue
        try:
            moment = datetime.datetime.strptime(entry.name[:19], "%Y-%m-%d_%H-%M-%S")
        except ValueError:
            continue
        text, check = report_head(Path(entry.path) / "report.md")
        found.append({"name": entry.name, "epoch": int(moment.timestamp()), "time": moment.strftime("%Y-%m-%d %H:%M:%S"),
                      "text": text, "check": check or None})
    found.sort(key=lambda item: (item["epoch"], item["name"]), reverse=True)
    return found


# ---------------------------------------------------------------------------------------------------------------- GitHub


def run_gh(arguments: list[str], stdin: str | None = None, cwd: Path | None = None) -> tuple[int, str, str]:
    """Run the local ``gh``; a missing gh or a timeout is a failed result, never an exception."""
    try:
        done = subprocess.run(["gh", *arguments], input=stdin, capture_output=True, text=True, encoding="utf-8",
                              cwd=cwd, timeout=GH_TIMEOUT)
    except FileNotFoundError:
        return 127, "", "gh is not installed"
    except subprocess.TimeoutExpired:
        return 124, "", f"gh timed out after {GH_TIMEOUT} s"
    except OSError as error:
        return 126, "", str(error)
    return done.returncode, done.stdout, done.stderr


def issue_title(step: owner_checks.Step) -> str:
    text = " ".join(step.step.split())
    if len(text) > 90:
        text = text[:87].rstrip() + "..."
    return f"{step.id}: differs from the original: {text}"


def issue_body(step: owner_checks.Step, note: str, report: str | None) -> str:
    """Text only: the step and its expectation from the repository list, the owner's note and the report folder's name."""
    lines = [
        f"The owner marked the check **{step.id}** as *differs* on the local check page (`./arcanum qa`, epic #2185).",
        "",
        f"**Section:** {step.section_title}",
        f"**Step:** {step.step}",
        f"**Expected:** {step.expected}",
        "",
        f"**Owner's note:** {note.strip() if note.strip() else '(none)'}",
        "",
        f"**Bug report folder (stays on the owner's Mac, not in git):** `{report}`" if report else "**Bug report folder:** none attached",
        "",
        "The list: `docs/project/owner-recheck-list.md` (the step stays there with its link until the fix merges).",
    ]
    return "\n".join(lines) + "\n"


def base_labels(step: owner_checks.Step) -> list[str]:
    labels = ["bug"]
    if step.type.lower() in TYPE_LABELS:
        labels.append(f"type:{step.type.lower()}")
    return labels


ISSUE_URL = re.compile(r"/issues/(\d+)\s*$")


# ---------------------------------------------------------------------------------------------------------------- the app


class QaApp:
    """The list snapshot, the store and the issue creation; the HTTP handler and ``qa sync`` both work through it."""

    def __init__(self, store: Store, reports_dir: Path, repo: Path = ROOT, ref: str = "origin/main", gh=run_gh, fetch: bool = True,
                 snapshot: bool = True) -> None:
        self.store = store
        self.snapshot = snapshot
        self.reports_dir = Path(reports_dir)
        self.repo = Path(repo)
        self.ref = ref
        self.gh = gh
        self.fetch = fetch
        self.parsed = owner_checks.Parsed()
        self.commit = ""
        self.warning = ""
        self.new_ids: set[str] = set()
        self._guard = threading.Lock()

    # list -------------------------------------------------------------------------------------------------------------

    def reload(self, fetch: bool | None = None) -> None:
        """Read the list from the ref (after a fetch unless turned off); a failed fetch is a warning, a missing ref an error."""
        warning = ""
        if self.fetch if fetch is None else fetch:
            branch = self.ref.split("/", 1)[1] if self.ref.startswith("origin/") else None
            if branch:
                try:
                    done = subprocess.run(["git", "-C", str(self.repo), "fetch", "--quiet", "origin", branch], capture_output=True,
                                          text=True, encoding="utf-8", timeout=GH_TIMEOUT)
                    if done.returncode != 0:
                        warning = f"git fetch failed, the list is as of the last fetch: {done.stderr.strip() or done.returncode}"
                except (OSError, subprocess.TimeoutExpired) as error:
                    warning = f"git fetch failed, the list is as of the last fetch: {error}"
        parsed = owner_checks.parse_ref(self.ref, repo=self.repo)      # RuntimeError when the ref has no list
        commit = subprocess.run(["git", "-C", str(self.repo), "rev-parse", "--short", self.ref], capture_output=True, text=True).stdout.strip()
        with self._guard:
            self.parsed, self.commit, self.warning = parsed, commit, warning
        if self.snapshot:
            try:
                self.store.write_checks(commit, self.ref, list(parsed.steps))
            except OSError as error:        # the overlay keeps the earlier snapshot; the page itself is not affected
                self.warning = (self.warning + "; " if self.warning else "") + f"could not write {CHECKS_FILE}: {error}"

    def set_text(self, text: str, commit: str = "test") -> None:
        """Take the list from text (tests)."""
        with self._guard:
            self.parsed, self.commit, self.warning = owner_checks.parse(text), commit, ""

    def mark_visit(self) -> None:
        """Fix the "new since the last visit" set for this run, then remember every current step as seen."""
        ids = [step.id for step in self.parsed.steps]
        previous = self.store.seen()
        self.new_ids = set() if previous is None else {item for item in ids if item not in previous}
        self.store.write_seen(ids)

    # state ------------------------------------------------------------------------------------------------------------

    def state(self) -> dict:
        results, bad = self.store.read()
        with self._guard:
            parsed, commit, warning = self.parsed, self.commit, self.warning
        sections = []
        for section in parsed.sections:
            if not section.steps:
                continue
            steps = []
            for step in section.steps:
                result = results.get(step.id)
                entry = {"id": step.id, "number": step.number, "step": step.step, "expected": step.expected, "listStatus": step.status,
                         "listNote": step.note, "status": result["status"] if result and result["recorded"] else step.status,
                         "note": result["note"] if result else "", "report": result["report"] if result else None,
                         "issue": result["issue"] if result else None, "issueUrl": result["issueUrl"] if result else None,
                         "issueError": result["issueError"] if result else None, "recorded": bool(result and result["recorded"]),
                         "isNew": step.id in self.new_ids}
                steps.append(entry)
            sections.append({"code": section.code, "title": section.title, "steps": steps})
        return {"sections": sections, "commit": commit, "ref": self.ref, "warning": warning, "badLines": bad,
                "problems": [problem.format() for problem in parsed.problems], "reports": list_reports(self.reports_dir)}

    # active check -----------------------------------------------------------------------------------------------------

    def set_active(self, step_id: object) -> str | None:
        """The owner opened a step: write it as the active check for Ctrl+B; None (or '') clears it. Returns the step id or None."""
        if step_id is None or step_id == "":
            self.store.clear_active()
            return None
        with self._guard:
            known = any(step.id == step_id for step in self.parsed.steps) if isinstance(step_id, str) else False
        if not known:
            raise ValueError("step is not a step of the list")
        self.store.write_active(step_id)
        return step_id

    # results ----------------------------------------------------------------------------------------------------------

    def save(self, payload: dict) -> dict:
        """Record one result; a ``differs`` step without an issue gets one at once. Returns the step's merged result."""
        step_id = payload.get("step")
        step = self.parsed.by_id().get(step_id) if isinstance(step_id, str) else None
        if step is None:
            raise ValueError(f"unknown step {step_id!r}")
        status = payload.get("status")
        if status not in RESULT_STATUSES:
            raise ValueError(f"status must be one of {', '.join(RESULT_STATUSES)}")
        note = payload.get("note", "")
        if not isinstance(note, str) or len(note) > MAX_NOTE:
            raise ValueError(f"note must be text of at most {MAX_NOTE} characters")
        report = payload.get("report") or None
        if report is not None:
            if not isinstance(report, str) or not REPORT_NAME.match(report) or not (self.reports_dir / report).is_dir():
                raise ValueError("report must be the name of a report folder")
        with self.store.locked():
            self.store.append({"kind": "result", "ts": now_iso(), "step": step.id, "status": status, "note": note.strip(), "report": report})
            current = self.store.read()[0].get(step.id, {})
            if status == "differs" and current.get("issue") is None:
                self._create_issue(step, current)
            return self.store.read()[0][step.id]

    def _create_issue(self, step: owner_checks.Step, current: dict) -> None:
        """Create the step's issue and record its number or the error (the caller holds the store lock and saw no issue)."""
        labels = base_labels(step)
        code, out, err = self.gh(["issue", "view", str(step.task), "--json", "title,labels"], None, self.repo)
        if code == 0:       # the area of the task's own issue, only when that issue is the task's (an old code's number may be another issue)
            try:
                found = json.loads(out)
                if str(found.get("title", "")).startswith(step.code):
                    labels += [item["name"] for item in found.get("labels", []) if str(item.get("name", "")).startswith("area:")][:1]
            except (ValueError, TypeError, KeyError, AttributeError):
                pass
        if current.get("issueError"):
            # an earlier attempt failed, maybe after GitHub had created the issue (a timeout): reuse it instead of a second one
            code, out, err = self.gh(["issue", "list", "--state", "all", "--search", f'"{step.id}" in:title', "--json", "number,url,title",
                                      "--limit", "10"], None, self.repo)
            if code == 0:
                try:
                    for found in json.loads(out):
                        if str(found.get("title", "")).startswith(f"{step.id}: ") and isinstance(found.get("number"), int):
                            self.store.append({"kind": "issue", "ts": now_iso(), "step": step.id, "issue": found["number"], "url": found.get("url")})
                            return
                except (ValueError, TypeError, AttributeError):
                    pass
        arguments = ["issue", "create", "--title", issue_title(step), "--body-file", "-"]
        for label in labels:
            arguments += ["--label", label]
        code, out, err = self.gh(arguments, issue_body(step, current.get("note", ""), current.get("report")), self.repo)
        match = ISSUE_URL.search(out) if code == 0 else None
        if match:
            self.store.append({"kind": "issue", "ts": now_iso(), "step": step.id, "issue": int(match.group(1)), "url": out.strip().splitlines()[-1]})
        else:
            message = (err.strip() or out.strip() or f"gh exited with {code}")[:500]
            self.store.append({"kind": "issue", "ts": now_iso(), "step": step.id, "error": message})

    def sync(self, dry_run: bool = False, retry_failed: bool = True, settle: float = 0) -> tuple[list[str], list[str]]:
        """Create the issues of the ``differs`` steps that have none (recorded by the game, or a failed earlier attempt); returns
        (created or would be, failed). ``retry_failed=False`` (the running page's timer) leaves a step whose earlier attempt failed to
        the next ``qa sync`` instead of calling gh again every few seconds. ``settle`` (seconds): a step whose latest result is younger
        waits (the issue body is fixed when it is created, so a note or report that follows the mark should be in it)."""
        done, failed = [], []
        with self.store.locked():
            results, _ = self.store.read()
            by_id = self.parsed.by_id()
            for step_id, result in results.items():
                step = by_id.get(step_id)
                if step is None or result["status"] != "differs" or result["issue"] is not None:
                    continue
                if not retry_failed and result["issueError"]:
                    continue
                if settle and _age_seconds(result.get("ts")) < settle:
                    continue
                if dry_run:
                    done.append(step_id)
                    continue
                self._create_issue(step, result)
                (done if self.store.read()[0][step_id]["issue"] is not None else failed).append(step_id)
        return done, failed


# ---------------------------------------------------------------------------------------------------------------- the server


def make_handler(app: QaApp, token: str, port_of):
    class Handler(BaseHTTPRequestHandler):
        server_version = "ArcanumQa"
        protocol_version = "HTTP/1.1"

        def log_message(self, *_: object) -> None:      # quiet: the terminal holds the start line only
            pass

        def _send(self, code: int, body: bytes, kind: str) -> None:
            self.send_response(code)
            self.send_header("Content-Type", kind)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Security-Policy", "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'")
            self.end_headers()
            self.wfile.write(body)

        def _json(self, code: int, value: object) -> None:
            self._send(code, json.dumps(value, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8")

        def _host_ok(self) -> bool:
            port = port_of()
            return self.headers.get("Host", "") in (f"127.0.0.1:{port}", f"localhost:{port}")

        def _authorized(self, query: dict[str, list[str]]) -> bool:
            if not self._host_ok():                      # a rebound DNS name is refused
                return False
            given = self.headers.get(TOKEN_HEADER) or (query.get("t") or [""])[0]
            return hmac.compare_digest(given.encode("utf-8"), token.encode("utf-8"))

        def do_GET(self) -> None:
            parts = urlsplit(self.path)
            query = parse_qs(parts.query)
            if not self._authorized(query):
                self._json(403, {"error": "forbidden"})
                return
            if parts.path == "/":
                self._send(200, PAGE.read_text(encoding="utf-8").replace("__QA_TOKEN__", token).encode("utf-8"), "text/html; charset=utf-8")
            elif parts.path == "/api/ping":
                self._json(200, {"ok": True})
            elif parts.path == "/api/state":
                self._json(200, app.state())
            elif parts.path == "/api/reports":
                self._json(200, {"reports": list_reports(app.reports_dir)})
            else:
                self._json(404, {"error": "not found"})

        def do_POST(self) -> None:
            parts = urlsplit(self.path)
            if self.headers.get(TOKEN_HEADER) is None or not self._authorized({}):   # a POST never takes the token from the URL
                self._json(403, {"error": "forbidden"})
                return
            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                length = -1
            if length < 0 or length > MAX_BODY:
                self._json(413, {"error": "body too large"})
                return
            try:
                payload = json.loads(self.rfile.read(length) or b"{}")
                if not isinstance(payload, dict):
                    raise ValueError("body must be an object")
            except ValueError as error:
                self._json(400, {"error": str(error)})
                return
            if parts.path == "/api/result":
                try:
                    self._json(200, app.save(payload))
                except ValueError as error:
                    self._json(400, {"error": str(error)})
            elif parts.path == "/api/active":
                try:
                    self._json(200, {"active": app.set_active(payload.get("step"))})
                except (ValueError, OSError) as error:
                    self._json(400, {"error": str(error)})
            elif parts.path == "/api/reload":
                try:
                    app.reload(True)
                    self._json(200, {"ok": True})
                except (RuntimeError, OSError) as error:
                    self._json(500, {"error": str(error)})
            else:
                self._json(404, {"error": "not found"})

    return Handler


class LocalServer(ThreadingHTTPServer):
    """``HTTPServer.server_bind`` asks the resolver for the host's full name, which takes 35 s on a Mac without a good
    resolver; this one only binds (the page needs no name)."""

    def server_bind(self) -> None:
        socketserver.TCPServer.server_bind(self)
        self.server_name, self.server_port = "127.0.0.1", self.server_address[1]


def start_server(app: QaApp, port: int = 0) -> tuple[LocalServer, str, str]:
    """Bind 127.0.0.1 only, serve in a thread and answer one ping through the real socket. Returns (server, url, token)."""
    token = secrets.token_urlsafe(24)
    holder: dict[str, LocalServer] = {}
    server = LocalServer(("127.0.0.1", port), make_handler(app, token, lambda: holder["server"].server_address[1]))
    server.daemon_threads = True
    holder["server"] = server
    threading.Thread(target=server.serve_forever, daemon=True).start()
    url = f"http://127.0.0.1:{server.server_address[1]}/?t={token}"
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    deadline = time.monotonic() + START_WITHIN_SECONDS
    while True:
        try:
            with opener.open(urllib.request.Request(f"http://127.0.0.1:{server.server_address[1]}/api/ping", headers={TOKEN_HEADER: token}), timeout=5) as reply:
                if reply.status == 200:
                    return server, url, token
        except (urllib.error.URLError, OSError):
            pass
        if time.monotonic() > deadline:
            server.shutdown()
            raise RuntimeError(f"the qa page did not answer within {START_WITHIN_SECONDS} s")
        time.sleep(0.2)


# ---------------------------------------------------------------------------------------------------------------- command


def configure(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("action", nargs="?", choices=("serve", "sync", "overlay"), default="serve",
                        help="serve (default): the local check page; sync: create the issues of the differs steps that have none; "
                        "overlay on|off: turn the in-game owner-checks overlay (Ctrl+Q) on or off for builds started from this checkout (ARC-DEV-2190)")
    parser.add_argument("state", nargs="?", choices=("on", "off"), help="with overlay: on or off")
    parser.add_argument("--ref", default="origin/main", help="the ref the list is read from with git show (default origin/main)")
    parser.add_argument("--no-fetch", action="store_true", help="do not fetch the ref's branch first")
    parser.add_argument("--no-open", action="store_true", help="do not open the page in the browser (the URL is printed)")
    parser.add_argument("--port", type=int, default=0, help="the localhost port (default: any free port)")
    parser.add_argument("--check", action="store_true", help="start the page, answer one request through its socket, print the result and stop")
    parser.add_argument("--dry-run", action="store_true", help="read the list and show what would happen (paths, steps, unsent issues); "
                        "start no server, write nothing, call no gh")
    parser.add_argument("--data-dir", type=Path, help="where results.jsonl and state.json live (default: local/qa of the main checkout)")
    parser.add_argument("--reports-dir", type=Path, help="the bug report folders (default: artifacts/packages/macos/reports of the main checkout)")


def auto_sync(app: QaApp, seconds: float = AUTO_SYNC_SECONDS, rounds: int | None = None, settle: float = SETTLE_SECONDS) -> None:
    """ARC-DEV-2190: while the page runs, create the issue of every ``differs`` result the game recorded (once per step: the issue
    number is stored) once the step's latest result is ``settle`` seconds old. A failed attempt waits for ``qa sync``. ``rounds`` (tests) stops the loop."""
    count = 0
    while rounds is None or count < rounds:
        time.sleep(seconds)
        count += 1
        try:
            done, failed = app.sync(retry_failed=False, settle=settle)
        except (OSError, ValueError) as error:
            print(f"qa: auto sync failed: {error}", file=sys.stderr)
            continue
        for step_id in done:
            print(f"qa: created the issue of {step_id} (recorded in the game)")
        for step_id in failed:
            print(f"qa: FAILED to create the issue of {step_id}; ./arcanum qa sync retries", file=sys.stderr)


def run(args: argparse.Namespace) -> int:
    try:
        data_dir = args.data_dir or default_data_dir()
        reports_dir = args.reports_dir or default_reports_dir()
    except ValueError as error:
        print(f"arcanum qa: {error}", file=sys.stderr)
        return 2
    app = QaApp(Store(data_dir), reports_dir, ROOT, args.ref, fetch=not args.no_fetch and not args.dry_run, snapshot=not args.dry_run)
    try:
        app.reload()
    except (RuntimeError, OSError) as error:
        print(f"arcanum qa: cannot read the list: {error}", file=sys.stderr)
        return 2
    print(f"qa: the list from {args.ref} ({app.commit}): {owner_checks.summary(app.parsed)}")
    if app.warning:
        print(f"qa: warning: {app.warning}", file=sys.stderr)
    if args.action == "overlay":
        marker = app.store.directory / OVERLAY_MARKER
        if args.state is None:
            print("arcanum qa: overlay needs on or off", file=sys.stderr)
            return 2
        if args.dry_run:
            print(f"qa: would turn the overlay {args.state}: {marker}")
            return 0
        try:
            if args.state == "on":
                marker.write_text("owner-qa overlay on (ARC-DEV-2190); remove with ./arcanum qa overlay off\n", encoding="utf-8")
            elif marker.exists():
                marker.unlink()
        except OSError as error:
            print(f"arcanum qa: cannot change {marker}: {error}", file=sys.stderr)
            return 1
        print(f"qa: the in-game overlay (Ctrl+Q) is {args.state}: {marker} (the list snapshot is {app.store.directory / CHECKS_FILE})")
        return 0
    if args.action == "sync":
        done, failed = app.sync(args.dry_run)
        for step_id in done:
            print(f"qa: {'would create' if args.dry_run else 'created'} the issue of {step_id}")
        for step_id in failed:
            print(f"qa: FAILED to create the issue of {step_id}: {app.store.read()[0][step_id]['issueError']}", file=sys.stderr)
        print(f"qa: sync: {len(done)} {'to create' if args.dry_run else 'created'}, {len(failed)} failed")
        return 1 if failed else 0
    if args.dry_run:
        results, bad = app.store.read()
        print(f"qa: results: {app.store.results_path} ({len(results)} step(s) recorded, {bad} unreadable line(s))")
        print(f"qa: reports: {app.reports_dir} ({len(list_reports(app.reports_dir))} folder(s))")
        print(f"qa: would serve 127.0.0.1:{args.port or '<free port>'}, {'without opening the browser' if args.no_open else 'then open the browser'}")
        return 0
    try:
        server, url, _ = start_server(app, args.port)
    except (RuntimeError, OSError) as error:
        print(f"arcanum qa: {error}", file=sys.stderr)
        return 1
    print(f"qa: started: {url}")
    if not args.check:
        app.mark_visit()          # a --check start must not use up the "new since the last visit" set
        app.store.clear_active()  # no step is open on a fresh page: a check left by an earlier page must not tag a report
        signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))   # a kill stops the page like Ctrl-C
    if args.check:
        server.shutdown()
        print("qa: check passed (the page answered through its socket)")
        return 0
    if not args.no_open:
        webbrowser.open(url)
    threading.Thread(target=auto_sync, args=(app,), daemon=True).start()
    print("qa: Ctrl-C stops the page")
    try:
        while True:
            time.sleep(3600)
    except KeyboardInterrupt:
        server.shutdown()
        app.store.clear_active()
        return 0
