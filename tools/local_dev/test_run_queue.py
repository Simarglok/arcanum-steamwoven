"""ARC-DEV-033: the launch queue runs Godot runs one at a time in arrival order, quiets the Mac for an export and frees
the place of a run that crashed, was killed or ran out of time."""
from __future__ import annotations

import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

from tools.local_dev import run_queue

REPO = Path(__file__).resolve().parents[2]
ARCANUM = REPO / "arcanum"


def ticket(seq: int, kind: str, started: bool = False, priority: int = run_queue.NORMAL) -> dict:
    return {"seq": seq, "kind": kind, "startedAt": 1.0 if started else None, "priority": priority}


class AdmissionRuleTests(unittest.TestCase):
    def test_godot_kinds_share_one_slot_in_arrival_order(self) -> None:
        live = [ticket(1, "headless", started=True), ticket(2, "window"), ticket(3, "export")]
        self.assertEqual(run_queue.blocker(live[1], live)["seq"], 1)
        self.assertEqual(run_queue.blocker(live[2], live)["seq"], 1)
        self.assertIsNone(run_queue.blocker(live[1], live[1:]))
        self.assertEqual(run_queue.blocker(live[2], live[1:])["seq"], 2)

    def test_dotnet_runs_alongside_godot_and_each_other(self) -> None:
        live = [ticket(1, "window", started=True), ticket(2, "dotnet", started=True), ticket(3, "dotnet")]
        self.assertIsNone(run_queue.blocker(live[2], live))

    def test_an_export_waits_for_running_dotnet_and_blocks_new_dotnet_only_once_it_is_next(self) -> None:
        live = [ticket(1, "dotnet", started=True), ticket(2, "export"), ticket(3, "dotnet")]
        self.assertEqual(run_queue.blocker(live[1], live)["seq"], 1)
        self.assertEqual(run_queue.blocker(live[2], live)["seq"], 2)
        waiting = [ticket(1, "window", started=True), ticket(2, "export"), ticket(3, "dotnet")]
        self.assertIsNone(run_queue.blocker(waiting[2], waiting))  # the export still waits for the Godot slot
        running = [ticket(2, "export", started=True), ticket(3, "dotnet")]
        self.assertEqual(run_queue.blocker(running[1], running)["seq"], 2)

    def test_the_owners_launch_goes_first_but_after_the_running_one(self) -> None:
        live = [ticket(1, "window", started=True), ticket(2, "window"), ticket(3, "window", priority=run_queue.FRONT)]
        self.assertEqual(run_queue.blocker(live[2], live)["seq"], 1)
        self.assertEqual(run_queue.blocker(live[1], live[1:])["seq"], 3)


class QueueRunTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        self.log = self.root / "events.log"
        self.env = patch.dict(os.environ, {"ARCANUM_LOCAL_HOME": str(self.root / "home"),
                                           "ARCANUM_QUEUE_LEGACY_LOCK": str(self.root / "legacy.lock")})
        self.env.start()
        os.environ.pop(run_queue.TICKET_ENV, None)
        self.processes: list[subprocess.Popen] = []

    def tearDown(self) -> None:
        for process in self.processes:
            if process.poll() is None:
                process.kill()
                process.wait()
        self.env.stop()
        self.temp.cleanup()

    def queued(self, kind: str, name: str, seconds: float, *, timeout: str | None = None) -> subprocess.Popen:
        code = (f"import time; open({str(self.log)!r}, 'a').write('start {name} %f\\n' % time.time()); time.sleep({seconds}); "
                f"open({str(self.log)!r}, 'a').write('end {name} %f\\n' % time.time())")
        return self.wrapper(kind, name, [sys.executable, "-c", code], timeout=timeout)

    def live(self) -> list[dict]:
        with run_queue._QueueLock():
            return [item for _, item in run_queue.tickets(prune=False)]

    def wrapper(self, kind: str, name: str, command: list[str], *, timeout: str | None = None,
                environment: dict | None = None) -> subprocess.Popen:
        before = len(self.live())
        argv = [sys.executable, "-B", str(ARCANUM), "run", "--kind", kind, "--label", name, "--start-within", "0",
                *(["--timeout", timeout] if timeout else []), "--", *command]
        process = subprocess.Popen(argv, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                   env={**os.environ, **(environment or {})})
        self.processes.append(process)
        self.until(lambda: len(self.live()) > before or process.poll() is not None)
        return process

    def until(self, condition, limit: float = 20.0) -> None:
        deadline = time.monotonic() + limit
        while not condition():
            self.assertLess(time.monotonic(), deadline, "condition not reached")
            time.sleep(0.05)

    def events(self) -> dict[str, float]:
        result = {}
        for line in self.log.read_text().splitlines():
            what, name, at = line.split()
            result[f"{what} {name}"] = float(at)
        return result

    def finish(self) -> None:
        for process in self.processes:
            process.wait(60)

    def test_godot_runs_start_one_at_a_time_in_arrival_order_without_gaps(self) -> None:
        self.queued("window", "a", 1.0)
        self.until(lambda: self.log.exists())
        names = ["b", "c", "d", "e"]
        for index, name in enumerate(names):
            self.queued("headless" if index % 2 else "window", name, 0.3)
        self.finish()
        found = self.events()
        order = sorted(["a", *names], key=lambda name: found[f"start {name}"])
        self.assertEqual(order, ["a", *names])
        for earlier, later in zip(order, order[1:]):
            gap = found[f"start {later}"] - found[f"end {earlier}"]
            self.assertGreaterEqual(gap, 0.0)
            self.assertLess(gap, 2.5)  # the poll interval plus interpreter start, not a lock-retry back-off

    def test_dotnet_runs_alongside_a_godot_run(self) -> None:
        self.queued("window", "w", 2.0)
        self.until(lambda: self.log.exists())
        self.queued("dotnet", "d", 0.2)
        self.finish()
        found = self.events()
        self.assertLess(found["end d"], found["end w"])

    def test_an_export_runs_quiet_after_running_dotnet_and_before_newer_dotnet(self) -> None:
        self.queued("dotnet", "old", 1.5)
        self.until(lambda: self.log.exists())
        self.queued("export", "x", 0.5)
        self.queued("dotnet", "new", 0.2)
        self.finish()
        found = self.events()
        self.assertGreaterEqual(found["start x"], found["end old"])
        self.assertGreaterEqual(found["start new"], found["end x"])

    def test_a_killed_wrapper_frees_the_place_when_its_command_ends_even_if_a_descendant_kept_the_lock(self) -> None:
        # The command leaves a background process that inherits the ticket's descriptor and outlives everything.
        first = self.wrapper("window", "crash", ["sh", "-c", f"echo started >> {self.root / 'crash'}; sleep 8 & sleep 1"])
        self.until(lambda: (self.root / "crash").exists())
        os.kill(first.pid, signal.SIGKILL)
        first.wait()
        began = time.time()
        self.queued("window", "next", 0.1)
        self.finish()
        self.assertLess(self.events()["start next"] - began, 5.0)

    def test_a_run_out_of_time_has_its_process_group_stopped(self) -> None:
        pid_file = self.root / "bg.pid"
        process = self.wrapper("headless", "slow", ["sh", "-c", f"sleep 30 & echo $! > {pid_file}; wait"], timeout="0.01")
        self.assertEqual(process.wait(30), run_queue.TIMEOUT_EXIT)
        self.until(lambda: pid_file.exists())
        background = int(pid_file.read_text())
        self.until(lambda: subprocess.run(["ps", "-p", str(background)], capture_output=True).returncode != 0)
        self.assertEqual(run_queue.tickets(), [])

    def test_a_stopped_waiter_leaves_the_queue(self) -> None:
        self.queued("window", "a", 1.5)
        self.until(lambda: self.log.exists())
        waiter = self.queued("window", "gone", 0.1)
        self.queued("window", "b", 0.1)
        waiter.send_signal(signal.SIGTERM)
        self.assertEqual(waiter.wait(10), 128 + signal.SIGTERM)
        self.finish()
        found = self.events()
        self.assertNotIn("start gone", found)
        self.assertLess(found["start b"] - found["end a"], 2.5)

    def test_the_lockf_rule_and_the_queue_exclude_each_other(self) -> None:
        legacy = os.environ["ARCANUM_QUEUE_LEGACY_LOCK"]
        holder = subprocess.Popen(["/usr/bin/lockf", legacy, sys.executable, "-c",
                                   f"import time; open({str(self.log)!r}, 'a').write('start lockf %f\\n' % time.time()); "
                                   f"time.sleep(1.2); open({str(self.log)!r}, 'a').write('end lockf %f\\n' % time.time())"])
        self.processes.append(holder)
        self.until(lambda: self.log.exists())
        self.queued("window", "q", 1.0)
        self.until(lambda: "start q" in self.log.read_text())
        late = subprocess.Popen(["/usr/bin/lockf", legacy, sys.executable, "-c",
                                 f"import time; open({str(self.log)!r}, 'a').write('start late %f\\n' % time.time())"])
        self.processes.append(late)
        self.finish()
        found = self.events()
        self.assertGreaterEqual(found["start q"], found["end lockf"])
        self.assertGreaterEqual(found["start late"], found["end q"])

    def test_a_nested_run_uses_its_parents_place(self) -> None:
        inner = f"{sys.executable} -B {ARCANUM} run --kind window -- {sys.executable} -c pass"
        process = self.wrapper("window", "outer", ["sh", "-c", inner])
        self.assertEqual(process.wait(30), 0)

    def test_a_nested_kind_the_parent_does_not_cover_is_refused(self) -> None:
        for outer, inner_kind in (("dotnet", "window"), ("window", "export")):
            inner = f"{sys.executable} -B {ARCANUM} run --kind {inner_kind} -- {sys.executable} -c pass"
            process = self.wrapper(outer, f"{outer}-{inner_kind}", ["sh", "-c", inner])
            self.assertEqual(process.wait(30), 2, (outer, inner_kind))  # the inner ./arcanum refuses with exit 2

    def test_lanes_with_another_locale_and_time_zone_see_the_same_holder(self) -> None:
        self.wrapper("window", "ru", ["sleep", "3"], environment={"LC_ALL": "ru_RU.UTF-8", "LANG": "ru_RU.UTF-8", "TZ": "Asia/Tokyo"})
        with patch.dict(os.environ, {"LC_ALL": "C", "TZ": "UTC"}):
            self.assertTrue(run_queue.status()[0].startswith("running #"), run_queue.status())
        self.assertEqual(len(self.live()), 1)

    def test_an_orphaned_godot_command_is_stopped_after_its_limit(self) -> None:
        first = self.wrapper("window", "orphan", ["sleep", "60"], timeout="0.01")
        self.until(lambda: any(item.get("childPid") for item in self.live()))
        child = next(item["childPid"] for item in self.live())
        os.kill(first.pid, signal.SIGKILL)
        first.wait()
        began = time.time()
        self.queued("window", "after", 0.1)
        self.finish()
        self.assertLess(self.events()["start after"] - began, run_queue.KILL_GRACE_SECONDS + 5)
        self.until(lambda: subprocess.run(["ps", "-p", str(child)], capture_output=True).returncode != 0)

    def test_numbering_survives_a_lost_counter(self) -> None:
        self.queued("window", "a", 1.0)
        self.until(lambda: self.log.exists())
        (run_queue.folder() / "counter").write_text("")
        self.queued("window", "b", 0.1)
        self.assertEqual(sorted(item["seq"] for item in self.live()), [1, 2])
        self.finish()

    def test_status_names_the_holder_and_the_waiters(self) -> None:
        self.queued("window", "holder", 1.5)
        self.until(lambda: self.log.exists())
        self.queued("window", "waiter", 0.1)
        lines = run_queue.status()
        self.assertTrue(lines[0].startswith("running #") and '"holder"' in lines[0], lines)
        self.assertTrue(lines[1].startswith("waiting #") and '"waiter"' in lines[1] and "behind #" in lines[1], lines)
        self.finish()
        history = run_queue.status(history=5)
        self.assertEqual(len(history), 2)
        self.assertIn("exit=0", history[0])


if __name__ == "__main__":
    unittest.main()
