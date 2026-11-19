"""ARC-DEV-399: the stage list, each stage's time and the queue wait of a smoke step, recorded beside the queue history."""
from __future__ import annotations

import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from tools.local_dev import smoke, smoke_history

LOG = """ARC-RENDER-004 load stage=complete generation=1
ARC-DEV-367 native-smoke perf PASS
ARC-DEV-399 stage-time cast ms=1500
ARC-MAGIC-015 native-smoke cast PASS
ARC-DEV-399 stage-time ai ms=2250
ARC-DEV-399 stage-time cast ms=500
not a stage-time line: ARC-DEV-399 stage-time bad ms=x
ARC-DEV-399 stage-time Upper ms=5
"""


class StageHistoryTests(unittest.TestCase):
    def setUp(self) -> None:
        pin = patch.object(smoke.run_queue, "playing_state", return_value=None)  # ARC-DEV-2902: never the live owner-playing state
        pin.start()
        self.addCleanup(pin.stop)
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.queue = self.root / "run-queue"
        self.queue.mkdir()
        self.env = patch.dict(os.environ, {"ARCANUM_LOCAL_HOME": str(self.root)})
        self.env.start()

    def tearDown(self) -> None:
        self.env.stop()
        self.temp.cleanup()

    def log(self, seq: int, text: str = LOG, kind: str = "window") -> Path:
        path = self.root / f"run-{seq:012d}-{kind}.log"
        path.write_text(text, encoding="utf-8")
        return path

    def history(self, *entries: dict) -> None:
        (self.queue / "history.jsonl").write_text("".join(json.dumps(item) + "\n" for item in entries), encoding="utf-8")

    def test_the_ticket_comes_from_the_log_name_and_only_queue_logs_have_one(self) -> None:
        self.assertEqual(smoke_history.ticket_of(self.log(42)), 42)
        self.assertIsNone(smoke_history.ticket_of(self.root / "main.log"))
        self.assertIsNone(smoke_history.ticket_of(None))

    def test_stage_times_keep_the_run_order_add_a_repeated_stage_and_ignore_other_lines(self) -> None:
        self.assertEqual(smoke_history.stage_times(self.log(1)), {"cast": 2000, "ai": 2250})
        self.assertEqual(list(smoke_history.stage_times(self.log(1))), ["cast", "ai"])
        self.assertEqual(smoke_history.stage_times(self.root / "missing.log"), {})
        self.assertEqual(smoke_history.stage_times(None), {})

    def test_an_entry_joins_the_queue_wait_and_run_time_to_the_stages(self) -> None:
        self.history({"seq": 6, "cwd": "/x/other", "enqueuedAt": 1.0, "startedAt": 2.0, "endedAt": 3.0},
                     {"seq": 7, "cwd": "/x/lane-a", "enqueuedAt": 1000.0, "startedAt": 1022.0, "endedAt": 1030.5, "exit": 0})
        entry = smoke_history.entry_of("main-cast", "window", ("cast", "ai"), 0, self.log(7))
        assert entry is not None
        self.assertEqual((entry["seq"], entry["step"], entry["kind"], entry["checkout"], entry["exit"]),
                         (7, "main-cast", "window", "lane-a", 0))
        self.assertEqual(entry["stages"], ["cast", "ai"])
        self.assertEqual(entry["stageMs"], {"cast": 2000, "ai": 2250})
        self.assertEqual((entry["waitSeconds"], entry["ranSeconds"]), (22.0, 8.5))
        self.assertEqual(entry["outsideStagesSeconds"], 4.2)  # 8.5 s of the run less 4.25 s in the stages

    def test_an_entry_records_what_it_was_asked_and_the_files_it_ran_on(self) -> None:  # ARC-DEV-2223
        entry = smoke_history.entry_of("main-cast", "window", ("cast", "ai"), 0, self.log(7), tree=lambda: "abc123")
        assert entry is not None
        self.assertEqual((entry["asked"], entry["tree"]), (["cast", "ai"], "abc123"))
        entry = smoke_history.entry_of("main", "window", (), 0, self.log(9), tree=lambda: None)
        assert entry is not None
        self.assertEqual((entry["asked"], entry["stages"], entry["tree"]), ([], ["cast", "ai"], None))  # a battery asks for no list

    def test_a_run_without_requested_stages_lists_the_stages_the_viewer_timed_and_a_missing_history_line_leaves_times_unknown(self) -> None:
        entry = smoke_history.entry_of("main", "window", (), 0, self.log(9))
        assert entry is not None
        self.assertEqual(entry["stages"], ["cast", "ai"])
        self.assertIsNone(entry["waitSeconds"])
        self.assertIsNone(entry["ranSeconds"])
        self.assertIsNone(entry["outsideStagesSeconds"])
        self.assertIsNone(smoke_history.entry_of("main", "window", (), 0, self.root / "main.log"))

    def test_append_keeps_the_newest_lines_and_recent_reads_them_back(self) -> None:
        for seq in range(1, 4):
            smoke_history.append({"schema": smoke_history.SCHEMA, "seq": seq, "step": "s", "stageMs": {}})
        self.assertEqual([item["seq"] for item in smoke_history.recent(2)], [2, 3])
        with patch.object(smoke_history, "FILE_BYTES", 100), patch.object(smoke_history, "FILE_KEEP", 2):
            smoke_history.append({"schema": smoke_history.SCHEMA, "seq": 4, "step": "s", "stageMs": {}})
        self.assertEqual([item["seq"] for item in smoke_history.recent(10)], [3, 4])
        self.assertEqual(oct((self.queue / smoke_history.FILE).stat().st_mode & 0o777), "0o600")

    def test_the_table_names_the_wait_the_run_and_every_stage(self) -> None:
        self.history({"seq": 7, "cwd": "/x/lane-a", "enqueuedAt": 0.0, "startedAt": 22.0, "endedAt": 30.0})
        entry = smoke_history.entry_of("main-cast", "window", ("cast", "ai"), 0, self.log(7))
        assert entry is not None
        lines = smoke_history.table(entry)
        self.assertEqual(lines[0], "  main-cast: waited 22s, ran 8s (outside the stages 4s)")
        self.assertEqual([line.split()[0] for line in lines[1:]], ["cast", "ai"])
        self.assertIn("2.2s", lines[2])

    def test_a_smoke_records_each_step_and_prints_the_times_and_a_failed_write_is_only_a_note(self) -> None:
        self.history({"seq": 11, "cwd": "/x/lane-a", "enqueuedAt": 100.0, "startedAt": 130.0, "endedAt": 140.0})
        lines: list[str] = []

        def runner(kind, command, *, label, timeout, quiet, start, log_sink, estimate=None) -> int:
            log_sink(self.log(11))
            return 0

        step = smoke.Step("main-cast", "window", ("g",), requires=())
        self.assertEqual(smoke.execute([step], runner, lines.append, known=()), 0)
        self.assertEqual([item["seq"] for item in smoke_history.recent(5)], [11])
        text = "\n".join(lines)
        self.assertIn("SMOKE times", text)
        self.assertIn("main-cast: waited 30s, ran 10s", text)
        self.assertIn("cast", text)
        with patch.object(smoke_history, "append", side_effect=OSError("disk full")):
            lines.clear()
            self.assertEqual(smoke.execute([step], runner, lines.append, known=()), 0)
        self.assertIn("were not recorded: disk full", "\n".join(lines))
        self.assertNotIn("SMOKE times", "\n".join(lines))

    def test_a_malformed_history_line_is_a_note_and_the_smoke_still_passes(self) -> None:
        self.history({"seq": 12, "cwd": None, "enqueuedAt": "soon", "startedAt": 1.0, "endedAt": 2.0})
        lines: list[str] = []

        def runner(kind, command, *, label, timeout, quiet, start, log_sink, estimate=None) -> int:
            log_sink(self.log(12))
            return 0

        step = smoke.Step("main-cast", "window", ("g",))
        self.assertEqual(smoke.execute([step], runner, lines.append, known=()), 0)
        self.assertIn("were not recorded", "\n".join(lines))


if __name__ == "__main__":
    unittest.main()
