"""ARC-DEV-2187: tests of ./arcanum qa with synthetic fixtures (a fake ``gh``, no network, no real list or report)."""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request

from . import owner_checks, qa

REPO = Path(__file__).resolve().parents[2]
LIST = """# List

## ARC-DEV-100 - First slice (PR #1)

| # | Step | Expected | Status |
| --- | --- | --- | --- |
| 1 | Cast the spell. | A flash appears. | open |
| 2 | Walk through it. | No block. | same (owner) |

## ARC-UI-200 - Second slice

| # | Step | Expected | Status |
| --- | --- | --- | --- |
| 1 | Open the window. | It opens. | open |

## ARC-BUG-300 - No type label

| # | Step | Expected | Status |
| --- | --- | --- | --- |
| 1 | Do it. | Done. | open |
"""


class FakeGh:
    """Records every call; ``fail`` makes `issue create` fail; ``area`` is what `issue view` knows."""

    def __init__(self, fail: bool = False, area: dict[int, tuple[str, list[str]]] | None = None) -> None:
        self.calls: list[tuple[list[str], str | None]] = []
        self.fail = fail
        self.area = area or {}
        self.next = 4000
        self.existing: list[dict] | None = None      # what `issue list` finds (None: the call fails)

    def __call__(self, arguments: list[str], stdin: str | None = None, cwd: Path | None = None) -> tuple[int, str, str]:
        self.calls.append((arguments, stdin))
        if arguments[:2] == ["issue", "view"]:
            known = self.area.get(int(arguments[2]))
            if known is None:
                return 1, "", "not found"
            return 0, json.dumps({"title": known[0], "labels": [{"name": name} for name in known[1]]}), ""
        if arguments[:2] == ["issue", "list"]:
            return (0, json.dumps(self.existing), "") if self.existing is not None else (2, "", "unexpected")
        if arguments[:2] == ["issue", "create"]:
            if self.fail:
                return 1, "", "HTTP 502"
            self.next += 1
            return 0, f"https://github.com/o/r/issues/{self.next}\n", ""
        return 2, "", "unexpected"

    def creates(self) -> list[tuple[list[str], str | None]]:
        return [call for call in self.calls if call[0][:2] == ["issue", "create"]]


def make_app(root: Path, gh=None, reports: Path | None = None) -> qa.QaApp:
    app = qa.QaApp(qa.Store(root / "data"), reports or root / "reports", gh=gh or FakeGh(), fetch=False)
    app.set_text(LIST)
    return app


def make_report(reports: Path, name: str, text: str | None = "the door stays shut") -> None:
    folder = reports / name
    folder.mkdir(parents=True)
    body = "# Bug report\n\n## What the player wrote\n\n" + (f"> {text}\n> second line\n" if text else "(no text)\n") + "\n## Game state\n\n> not this\n"
    (folder / "report.md").write_text(body, encoding="utf-8")
    (folder / "log.txt").write_text("SECRET LOG", encoding="utf-8")


class StoreTests(unittest.TestCase):
    def test_latest_event_wins_and_issue_is_sticky(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            store = qa.Store(Path(tmp))
            store.append({"kind": "result", "ts": "t1", "step": "ARC-DEV-100#1", "status": "differs", "note": "a", "report": None})
            store.append({"kind": "issue", "ts": "t2", "step": "ARC-DEV-100#1", "issue": 7, "url": "u"})
            store.append({"kind": "result", "ts": "t3", "step": "ARC-DEV-100#1", "status": "same", "note": "b", "report": "2026-11-15_10-00-00"})
            merged, bad = store.read()
            self.assertEqual(0, bad)
            self.assertEqual(("same", "b", 7), (merged["ARC-DEV-100#1"]["status"], merged["ARC-DEV-100#1"]["note"], merged["ARC-DEV-100#1"]["issue"]))

    def test_unreadable_lines_are_skipped_and_a_cut_line_is_closed(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            store = qa.Store(Path(tmp))
            store.results_path.write_text('{"kind":"result","step":"ARC-DEV-100#1","status":"same","note":"","ts":"t"}\nnot json\n{"kind":"result","step":"x"', encoding="utf-8")
            store.append({"kind": "result", "ts": "t", "step": "ARC-DEV-100#2", "status": "same", "note": "", "report": None})
            merged, bad = store.read()
            self.assertEqual(2, bad)            # the garbage line and the cut line
            self.assertEqual({"ARC-DEV-100#1", "ARC-DEV-100#2"}, set(merged))

    def test_results_survive_a_restart(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            first = make_app(Path(tmp))
            first.save({"step": "ARC-UI-200#1", "status": "cannot-test", "note": "no key"})
            second = make_app(Path(tmp))
            step = second.state()["sections"][1]["steps"][0]
            self.assertEqual(("cannot-test", "no key", True), (step["status"], step["note"], step["recorded"]))

    def test_the_results_path_is_git_ignored(self) -> None:
        self.assertIn("/local/", (REPO / ".gitignore").read_text().splitlines())
        done = subprocess.run(["git", "-C", str(REPO), "check-ignore", "-q", "local/qa/results.jsonl"])
        self.assertEqual(0, done.returncode)


class ReportTests(unittest.TestCase):
    def test_lists_newest_first_with_the_players_first_line_only(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            reports = Path(tmp)
            make_report(reports, "2026-11-15_09-00-00")
            make_report(reports, "2026-11-15_10-00-00", None)
            make_report(reports, "2026-11-15_10-00-00_2", "later one")
            (reports / "archive").mkdir()
            (reports / "stray.txt").write_text("x")
            found = qa.list_reports(reports)
            self.assertEqual(["2026-11-15_10-00-00_2", "2026-11-15_10-00-00", "2026-11-15_09-00-00"], [item["name"] for item in found])
            self.assertEqual(["later one", "", "the door stays shut"], [item["text"] for item in found])
            self.assertNotIn("SECRET", json.dumps(found))

    def test_a_missing_folder_is_an_empty_list(self) -> None:
        self.assertEqual([], qa.list_reports(Path("/nonexistent-qa-reports")))

    def test_a_report_name_is_validated_and_must_exist(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            app = make_app(Path(tmp))
            make_report(app.reports_dir, "2026-11-15_10-00-00")
            for bad in ("../x", "archive", "2026-11-15_10-00-01", "/etc"):
                with self.assertRaises(ValueError, msg=bad):
                    app.save({"step": "ARC-UI-200#1", "status": "same", "report": bad})
            saved = app.save({"step": "ARC-UI-200#1", "status": "same", "report": "2026-11-15_10-00-00"})
            self.assertEqual("2026-11-15_10-00-00", saved["report"])
            self.assertNotIn("SECRET", app.store.results_path.read_text())      # the folder name only


class IssueTests(unittest.TestCase):
    def test_differs_creates_one_issue_and_a_repeat_never_a_second(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            gh = FakeGh(area={100: ("ARC-DEV-100: the slice", ["type:dev", "area:scene", "status:ready"])})
            app = make_app(Path(tmp), gh)
            make_report(app.reports_dir, "2026-11-15_10-00-00")
            first = app.save({"step": "ARC-DEV-100#1", "status": "differs", "note": "no flash at all", "report": "2026-11-15_10-00-00"})
            self.assertEqual(4001, first["issue"])
            again = app.save({"step": "ARC-DEV-100#1", "status": "differs", "note": "still no flash"})
            app.save({"step": "ARC-DEV-100#1", "status": "same"})
            app.save({"step": "ARC-DEV-100#1", "status": "differs"})
            self.assertEqual(4001, again["issue"])
            self.assertEqual(1, len(gh.creates()))
            arguments, body = gh.creates()[0]
            self.assertEqual(["--label", "bug"], arguments[arguments.index("--label"):arguments.index("--label") + 2])
            self.assertIn("type:dev", arguments)
            self.assertIn("area:scene", arguments)
            self.assertNotIn("status:ready", arguments)
            self.assertIn("ARC-DEV-100#1", arguments[arguments.index("--title") + 1])
            for text in ("Cast the spell.", "A flash appears.", "no flash at all", "`2026-11-15_10-00-00`"):
                self.assertIn(text, body)
            self.assertNotIn("SECRET", body)
            self.assertNotIn("the door stays shut", body)           # the player's text of the report is not copied

    def test_other_statuses_create_nothing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            gh = FakeGh()
            app = make_app(Path(tmp), gh)
            for status in ("same", "cannot-test", "open"):
                app.save({"step": "ARC-DEV-100#1", "status": status})
            self.assertEqual([], gh.calls)

    def test_area_is_taken_only_from_the_tasks_own_issue(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            gh = FakeGh(area={200: ("ARC-DEV-999: another thing", ["area:world"])})
            app = make_app(Path(tmp), gh)
            app.save({"step": "ARC-UI-200#1", "status": "differs"})
            arguments = gh.creates()[0][0]
            self.assertIn("type:ui", arguments)
            self.assertNotIn("area:world", arguments)

    def test_a_code_type_without_a_label_gets_bug_only(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            gh = FakeGh()
            make_app(Path(tmp), gh).save({"step": "ARC-BUG-300#1", "status": "differs"})
            labels = [value for flag, value in zip(gh.creates()[0][0], gh.creates()[0][0][1:]) if flag == "--label"]
            self.assertEqual(["bug"], labels)

    def test_a_failure_keeps_the_result_and_sync_retries_it_once(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            gh = FakeGh(fail=True)
            app = make_app(Path(tmp), gh)
            failed = app.save({"step": "ARC-DEV-100#1", "status": "differs", "note": "n"})
            self.assertEqual(("differs", None, True), (failed["status"], failed["issue"], "502" in failed["issueError"]))
            self.assertEqual((["ARC-DEV-100#1"], []), app.sync(dry_run=True))
            self.assertEqual(1, len(gh.creates()))                 # a dry run calls nothing
            gh.fail = False
            created, still = app.sync()
            self.assertEqual((["ARC-DEV-100#1"], []), (created, still))
            self.assertEqual(([], []), app.sync())                 # nothing left: no second issue
            self.assertEqual(2, len(gh.creates()))
            step = app.state()["sections"][0]["steps"][0]
            self.assertEqual((4001, None), (step["issue"], step["issueError"]))

    def test_a_retry_reuses_an_issue_that_a_timed_out_create_had_made(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            gh = FakeGh(fail=True)
            app = make_app(Path(tmp), gh)
            app.save({"step": "ARC-DEV-100#1", "status": "differs"})
            gh.fail = False
            gh.existing = [{"number": 77, "url": "https://github.com/o/r/issues/77", "title": "ARC-DEV-100#1: differs from the original: x"},
                           {"number": 78, "url": "u", "title": "ARC-DEV-100#10: another step"}]
            self.assertEqual((["ARC-DEV-100#1"], []), app.sync())
            self.assertEqual(77, app.state()["sections"][0]["steps"][0]["issue"])
            self.assertEqual(1, len(gh.creates()))                  # only the failed first attempt

    def test_sync_keeps_failures_listed(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            app = make_app(Path(tmp), FakeGh(fail=True))
            app.save({"step": "ARC-DEV-100#1", "status": "differs"})
            self.assertEqual(([], ["ARC-DEV-100#1"]), app.sync())

    def test_a_missing_gh_is_a_failed_result(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            app = make_app(Path(tmp), lambda *a, **k: (127, "", "gh is not installed"))
            saved = app.save({"step": "ARC-DEV-100#1", "status": "differs"})
            self.assertEqual(("differs", None, "gh is not installed"), (saved["status"], saved["issue"], saved["issueError"]))

    def test_invalid_input_is_refused_before_anything_is_written(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            app = make_app(Path(tmp))
            for payload in ({"step": "nope", "status": "same"}, {"step": "ARC-DEV-100#1", "status": "maybe"},
                            {"step": "ARC-DEV-100#1", "status": "same", "note": "x" * (qa.MAX_NOTE + 1)}, {"step": 5, "status": "same"}):
                with self.assertRaises(ValueError):
                    app.save(payload)
            self.assertFalse(app.store.results_path.exists())


class StateTests(unittest.TestCase):
    def test_state_has_sections_list_status_and_progress_inputs(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            app = make_app(Path(tmp))
            state = app.state()
            self.assertEqual(["ARC-DEV-100", "ARC-UI-200", "ARC-BUG-300"], [section["code"] for section in state["sections"]])
            second = state["sections"][0]["steps"][1]
            self.assertEqual(("same", False), (second["status"], second["recorded"]))       # the list's own status shows until a result exists
            app.save({"step": "ARC-DEV-100#2", "status": "differs"})
            self.assertEqual("differs", app.state()["sections"][0]["steps"][1]["status"])

    def test_new_since_the_last_visit(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            first = make_app(Path(tmp))
            first.mark_visit()
            self.assertEqual(set(), first.new_ids)                          # no earlier visit: nothing is new
            second = make_app(Path(tmp))
            second.set_text(LIST + "\n## ARC-DEV-400 - Newer\n\n| # | Step | Expected | Status |\n| --- | --- | --- | --- |\n| 1 | A. | B. | open |\n")
            second.mark_visit()
            self.assertEqual({"ARC-DEV-400#1"}, second.new_ids)
            self.assertTrue(second.state()["sections"][-1]["steps"][0]["isNew"])
            third = make_app(Path(tmp))
            third.mark_visit()
            self.assertEqual(set(), third.new_ids)

    def test_the_list_is_read_from_a_ref_without_touching_the_checkout(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            app = qa.QaApp(qa.Store(Path(tmp) / "d"), Path(tmp) / "r", REPO, "HEAD", gh=FakeGh(), fetch=False)
            before = subprocess.run(["git", "-C", str(REPO), "status", "--porcelain"], capture_output=True, text=True).stdout
            app.reload()
            after = subprocess.run(["git", "-C", str(REPO), "status", "--porcelain"], capture_output=True, text=True).stdout
            self.assertEqual(before, after)
            self.assertEqual([], app.parsed.problems)
            self.assertGreater(len(app.parsed.steps), 100)


class ServerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.gh = FakeGh()
        self.app = make_app(Path(self.tmp.name), self.gh)
        make_report(self.app.reports_dir, "2026-11-15_10-00-00")
        self.server, self.url, self.token = qa.start_server(self.app)
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.port = self.server.server_address[1]
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def call(self, path: str, body: object | None = None, token: str | None = None, host: str | None = None) -> tuple[int, dict | str]:
        headers = {qa.TOKEN_HEADER: token if token is not None else self.token}
        if host:
            headers["Host"] = host
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"
        try:
            with self.opener.open(urllib.request.Request(f"http://127.0.0.1:{self.port}{path}", data=data, headers=headers), timeout=10) as reply:
                text = reply.read().decode()
                return reply.status, json.loads(text) if "json" in reply.headers["Content-Type"] else text
        except urllib.error.HTTPError as error:
            with error:
                return error.code, json.loads(error.read().decode())

    def test_binds_localhost_only(self) -> None:
        self.assertEqual("127.0.0.1", self.server.server_address[0])

    def test_the_page_needs_the_token_and_a_local_host(self) -> None:
        with self.assertRaises(urllib.error.HTTPError) as caught:
            self.opener.open(f"http://127.0.0.1:{self.port}/", timeout=10)
        caught.exception.close()
        self.assertEqual(403, caught.exception.code)
        with self.opener.open(self.url, timeout=10) as reply:
            status, page = reply.status, reply.read().decode()
        self.assertEqual(200, status)
        self.assertIn(self.token, page)
        self.assertNotIn("__QA_TOKEN__", page)
        self.assertEqual(403, self.call("/api/state", token="wrong")[0])
        self.assertEqual(403, self.call("/api/state", host="evil.example:80")[0])      # a rebound DNS name

    def test_api_state_result_reports_and_reload(self) -> None:
        status, state = self.call("/api/state")
        self.assertEqual(200, status)
        self.assertEqual("2026-11-15_10-00-00", state["reports"][0]["name"])
        status, saved = self.call("/api/result", {"step": "ARC-UI-200#1", "status": "differs", "note": "bad", "report": "2026-11-15_10-00-00"})
        self.assertEqual((200, 4001), (status, saved["issue"]))
        status, _ = self.call("/api/result", {"step": "ARC-UI-200#1", "status": "differs"})
        self.assertEqual(1, len(self.gh.creates()))
        self.assertEqual(400, self.call("/api/result", {"step": "nope", "status": "same"})[0])
        self.assertEqual(200, self.call("/api/reports")[0])
        self.assertEqual(404, self.call("/api/none")[0])

    def test_a_post_needs_the_header_and_is_size_limited(self) -> None:
        request = urllib.request.Request(f"http://127.0.0.1:{self.port}/api/result?t={self.token}", data=b"{}", method="POST")
        with self.assertRaises(urllib.error.HTTPError) as caught:
            self.opener.open(request, timeout=10)
        caught.exception.close()
        self.assertEqual(403, caught.exception.code)                 # the URL token never authorises a POST
        self.assertEqual(413, self.call("/api/result", {"pad": "x" * (qa.MAX_BODY + 1)})[0])

    def test_concurrent_differs_posts_create_one_issue(self) -> None:
        results: list[int] = []

        def post() -> None:
            results.append(self.call("/api/result", {"step": "ARC-DEV-100#1", "status": "differs"})[1]["issue"])

        threads = [threading.Thread(target=post) for _ in range(6)]
        for thread in threads:
            thread.start()
        for thread in threads:
            thread.join()
        self.assertEqual(1, len(set(results)))
        self.assertEqual(1, len(self.gh.creates()))


class CommandTests(unittest.TestCase):
    def run_qa(self, *arguments: str, data: Path, reports: Path) -> subprocess.CompletedProcess:
        return subprocess.run([sys.executable, "-B", str(REPO / "arcanum"), "qa", *arguments, "--no-fetch", "--ref", "HEAD",
                               "--data-dir", str(data), "--reports-dir", str(reports)], capture_output=True, text=True, timeout=120)

    def test_help_and_dry_run_write_nothing(self) -> None:
        helped = subprocess.run([sys.executable, "-B", str(REPO / "arcanum"), "qa", "--help"], capture_output=True, text=True, timeout=60)
        self.assertEqual(0, helped.returncode)
        for flag in ("--dry-run", "--check", "--no-open", "--data-dir", "sync"):
            self.assertIn(flag, helped.stdout)
        with tempfile.TemporaryDirectory() as tmp:
            data = Path(tmp) / "data"
            done = self.run_qa("--dry-run", data=data, reports=Path(tmp) / "reports")
            self.assertEqual(0, done.returncode, done.stderr)
            self.assertIn("would serve", done.stdout)
            self.assertFalse(data.exists())
            synced = self.run_qa("sync", "--dry-run", data=data, reports=Path(tmp) / "reports")
            self.assertEqual(0, synced.returncode, synced.stderr)
            self.assertIn("sync: 0 to create", synced.stdout)

    def test_check_starts_the_page_and_stops(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            done = self.run_qa("--check", "--no-open", data=Path(tmp) / "data", reports=Path(tmp) / "reports")
            self.assertEqual(0, done.returncode, done.stderr)
            self.assertIn("qa: started: http://127.0.0.1:", done.stdout)
            self.assertIn("check passed", done.stdout)
            self.assertFalse((Path(tmp) / "data" / "state.json").exists())     # a start check does not use up "new since the last visit"

    def test_the_list_parser_is_the_one_of_ARC_DEV_2186(self) -> None:
        self.assertIs(qa.owner_checks, owner_checks)


if __name__ == "__main__":
    unittest.main()
