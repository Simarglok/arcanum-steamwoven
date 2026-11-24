"""ARC-DEV-3115: windowserver_report.py reads a (cut-off) WindowServer watchdog stackshot; synthetic fixture only, no real report."""
from __future__ import annotations

from contextlib import redirect_stderr, redirect_stdout
import io
import json
from pathlib import Path
import tempfile
import unittest

from tools.local_dev import windowserver_report as ws

EVENT = [7001, 7002]
FRAMES = [[0, 599200], [0, 593904], [0, 6429656], [0, 6429112], [0, 6428256], [0, 6506048], [0, 746152], [0, 160372], [0, 99]]


def _thread(name: str, state: list[str], wait_event=None, wait_info: str = "", frames=None) -> dict:
    entry: dict = {"name": name, "state": state}
    if wait_event is not None:
        entry["waitEvent"] = wait_event
    if wait_info:
        entry["turnstileInfo"] = wait_info
    if frames is not None:
        entry["kernelFrames"] = frames
    return entry


def _process(name: str, parent: int, rss: int, threads: dict) -> dict:
    return {"procname": name, "parentPid": parent, "residentMemoryBytes": rss, "threadById": threads}


def fixture_body() -> dict:
    """WindowServer main thread -> runningboardd client thread -> its call-out thread (TH_UNINT) on the same event as a forked child."""
    return {"stackshot": {"processByPid": {
        "1": _process("launchd", 0, 9000000, {"11": _thread("main", ["TH_WAIT"])}),
        "400": _process("WindowServer", 1, 80000000, {
            "500": _thread("ws_main_thread", ["TH_WAIT"], wait_info="thread 600: blocked on 0xabc, 3 hops"),
            "501": _thread("ws_other", ["TH_RUN"])}),
        "410": _process("runningboardd", 1, 7000000, {
            "600": _thread("client-workloop", ["TH_WAIT"], wait_info="thread 601: blocked on 0xdef, 1 hops"),
            "601": _thread("RBAssertionManagerCallOut", ["TH_WAIT", "TH_UNINT"], EVENT, frames=FRAMES)}),
        "90": _process("codex", 80, 1000, {"90": _thread("main", ["TH_WAIT"])}),
        "80": _process("Codex Helper", 1, 1000, {"80": _thread("main", ["TH_WAIT"])}),
        "91": _process("bash", 90, 1000, {"91": _thread("main", ["TH_WAIT"])}),
        "92": _process("Python", 91, 1000, {"92": _thread("main", ["TH_WAIT"])}),
        "-93270": _process("Python", 92, 49200, {"700": _thread("", ["TH_WAIT", "TH_UNINT"], EVENT, frames=FRAMES)}),
        "300": _process("spindump", 1, 5000, {"800": _thread("main", ["TH_WAIT", "TH_UNINT"], [9, 9], frames=[[0, 1]])}),
    }}}


def fixture_text(cut: bool = False) -> str:
    header = json.dumps({"bug_type": "409", "os_version": "macOS 99.9 (SYNTH)", "timestamp": "2026-01-01 00:00:00.00 +0000"})
    body = json.dumps(fixture_body(), indent=1)
    if cut:
        # Cut inside a thread entry of the last-but-one process, as a watchdog report is cut off.
        body = body[:body.index('"spindump"') + 10]
    return header + "\n" + body


class RepairTests(unittest.TestCase):
    def test_valid_json_is_not_repaired(self) -> None:
        value, repaired = ws.repair_json('{"a": [1, {"b": 2}]}')
        self.assertEqual({"a": [1, {"b": 2}]}, value)
        self.assertFalse(repaired)

    def test_cut_off_json_closes_open_brackets_after_the_last_complete_object(self) -> None:
        value, repaired = ws.repair_json('{"a": {"x": 1}, "list": [{"k": 1}, {"k": 2}, {"k": 3, "tru')
        self.assertTrue(repaired)
        self.assertEqual([{"k": 1}, {"k": 2}], value["list"])

    def test_braces_and_commas_inside_strings_are_not_cut_points(self) -> None:
        value, repaired = ws.repair_json('{"s": [{"t": "},{"}, {"t": "x')
        self.assertTrue(repaired)
        self.assertEqual([{"t": "},{"}], value["s"])

    def test_hopeless_text_raises(self) -> None:
        with self.assertRaises(ValueError):
            ws.repair_json("not json at all")


class ReportTests(unittest.TestCase):
    def test_header_and_body_are_split_and_threads_are_read(self) -> None:
        report = ws.parse_ips(fixture_text())
        self.assertEqual("409", report.header["bug_type"])
        self.assertFalse(report.repaired)
        self.assertEqual(9, len(report.processes))
        self.assertEqual("49200", report.processes["-93270"].rss)

    def test_wait_chain_follows_thread_ids_to_the_uninterruptible_thread(self) -> None:
        report = ws.parse_ips(fixture_text())
        start = ws.main_thread(report)
        assert start is not None
        self.assertEqual("500", start.tid)
        chain = ws.wait_chain(report, start)
        self.assertEqual(["500", "600", "601"], [thread.tid for thread in chain])
        self.assertTrue(chain[-1].uninterruptible)

    def test_wait_chain_stops_on_a_loop_and_on_an_unknown_thread(self) -> None:
        body = {"processByPid": {"1": _process("WindowServer", 0, 1, {
            "1": _thread("ws_main_thread", ["TH_WAIT"], wait_info="thread 2: x"),
            "2": _thread("a", ["TH_WAIT"], wait_info="thread 1: x, thread 99: y")})}}
        report = ws.build_report(body)
        start = ws.main_thread(report)
        assert start is not None
        self.assertEqual(["1", "2"], [thread.tid for thread in ws.wait_chain(report, start)])

    def test_ancestry_of_the_forked_child(self) -> None:
        report = ws.parse_ips(fixture_text())
        self.assertEqual(["Python[-93270]", "Python[92]", "bash[91]", "codex[90]", "Codex Helper[80]", "launchd[1]"],
                         ws.ancestry(report, "-93270"))

    def test_cut_off_report_is_repaired_and_still_gives_the_chain(self) -> None:
        text = fixture_text(cut=True)
        with self.assertRaises(json.JSONDecodeError):
            json.loads(text.partition("\n")[2])
        report = ws.parse_ips(text)
        self.assertTrue(report.repaired)
        start = ws.main_thread(report)
        assert start is not None
        self.assertEqual("601", ws.wait_chain(report, start)[-1].tid)

    def test_missing_fields_are_printed_not_raised(self) -> None:
        report = ws.build_report({"stackshot": {"processByPid": {"5": {"threadById": {"6": {}}}}}})
        text = ws.render(report)
        self.assertIn("no WindowServer process", text)
        self.assertIn("uninterruptible (TH_UNINT) threads: 0", text)

    def test_missing_stackshot_is_an_error(self) -> None:
        with self.assertRaises(ValueError):
            ws.parse_ips('{"bug_type": "409"}\n{"other": 1}')


class RenderTests(unittest.TestCase):
    def test_render_lists_chain_unint_threads_ancestry_and_shared_event(self) -> None:
        text = ws.render(ws.parse_ips(fixture_text()), frames=3)
        self.assertIn("bug_type=409", text)
        self.assertIn("0: thread 500 'ws_main_thread' in WindowServer[400]", text)
        self.assertIn("2: thread 601 'RBAssertionManagerCallOut' in runningboardd[410]", text)
        self.assertIn("uninterruptible (TH_UNINT) threads: 3", text)
        self.assertIn("kernel[599200,593904,6429656]", text)
        self.assertIn("ancestry: Python[-93270] <- Python[92] <- bash[91] <- codex[90] <- Codex Helper[80] <- launchd[1]; rss=49200", text)
        self.assertEqual(2, text.count("[same wait event as chain end]"))
        self.assertIn("distinct kernel signatures (first 3 frames) among them: 2", text)

    def test_cli_reads_files_only_and_reports_unreadable_ones(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            good = Path(folder) / ".WindowServer-synthetic.ips"
            good.write_text(fixture_text(cut=True), encoding="utf-8")
            bad = Path(folder) / "bad.ips"
            bad.write_text("nonsense", encoding="utf-8")
            before = sorted(path.name for path in Path(folder).iterdir())
            out, err = io.StringIO(), io.StringIO()
            with redirect_stdout(out), redirect_stderr(err):
                status = ws.main([str(good), str(bad)])
            self.assertEqual(1, status)
            self.assertIn("JSON was cut off and repaired", out.getvalue())
            self.assertIn("cannot read", err.getvalue())
            self.assertEqual(before, sorted(path.name for path in Path(folder).iterdir()))
            out2 = io.StringIO()
            with redirect_stdout(out2):
                self.assertEqual(0, ws.main([str(good)]))


if __name__ == "__main__":
    unittest.main()
