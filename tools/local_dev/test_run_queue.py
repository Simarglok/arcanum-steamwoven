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


def ticket(seq: int, kind: str, started: bool = False, priority: int = run_queue.NORMAL, at: float | None = None,
           estimate: float | None = None) -> dict:
    """A ticket; `at` is its arrival time (default: its number, so equal estimates keep the arrival order)."""
    result = {"seq": seq, "kind": kind, "startedAt": 1.0 if started else None, "priority": priority,
              "enqueuedAt": float(seq) if at is None else at}
    if estimate is not None:
        result["estimateSeconds"] = estimate
    return result


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

    def test_dotnet_runs_fill_the_dotnet_places_in_arrival_order(self) -> None:
        # ARC-DEV-300: DOTNET_SLOTS places; a Godot run never counts against them.
        live = [ticket(1, "dotnet", started=True), ticket(2, "window", started=True), ticket(3, "dotnet", started=True),
                ticket(4, "dotnet"), ticket(5, "dotnet")]
        self.assertEqual(run_queue.DOTNET_SLOTS, 2)
        self.assertEqual(run_queue.blocker(live[3], live)["seq"], 1)  # the oldest running run frees the first place
        self.assertEqual(run_queue.blocker(live[4], live)["seq"], 3)
        self.assertIsNone(run_queue.blocker(live[3], [live[1], live[2], live[3]]))  # seq 1 ended: a place is free
        self.assertEqual(run_queue.blocker(live[4], [live[1], live[2], live[3], live[4]])["seq"], 3)

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


class OrderTests(unittest.TestCase):
    """ARC-DEV-390: a short run is not queued behind a long one that arrived a little before it, and no run waits for ever.
    The lists hold waiting tickets only (a running Godot run blocks every waiter, whatever the order)."""

    def test_a_short_run_passes_a_long_run_that_arrived_before_it(self) -> None:
        long_run, short_run = ticket(2, "window", at=100.0, estimate=600.0), ticket(3, "window", at=110.0, estimate=20.0)
        live = [long_run, short_run]
        self.assertEqual(run_queue.blocker(long_run, live)["seq"], 3)  # the long run waits for the short one
        self.assertIsNone(run_queue.blocker(short_run, live))
        self.assertEqual([item["seq"] for item in sorted(live, key=run_queue._key)], [3, 2])
        running = ticket(1, "window", started=True)
        self.assertEqual(run_queue.blocker(short_run, [running, *live])["seq"], 1)

    def test_a_long_run_is_passed_only_by_runs_that_arrive_within_its_delay(self) -> None:
        delay = run_queue.ORDER_WEIGHT * 600.0
        long_run = ticket(2, "window", at=100.0, estimate=600.0)
        in_time = ticket(3, "window", at=100.0 + delay - run_queue.ORDER_WEIGHT * 20.0 - 1, estimate=20.0)
        too_late = ticket(4, "window", at=100.0 + delay + 1, estimate=1.0)
        self.assertEqual(run_queue.blocker(long_run, [long_run, in_time])["seq"], 3)  # arrived in time to pass it
        self.assertEqual(run_queue.blocker(too_late, [long_run, too_late])["seq"], 2)  # too late: the long run is first
        self.assertIsNone(run_queue.blocker(long_run, [long_run, too_late]))

    def test_equal_estimates_keep_the_arrival_order_and_the_owners_launch_still_goes_first(self) -> None:
        first, second = ticket(2, "window", at=5.0, estimate=30.0), ticket(3, "window", at=6.0, estimate=30.0)
        owner = ticket(4, "window", at=900.0, estimate=9000.0, priority=run_queue.FRONT)
        self.assertEqual(run_queue.blocker(second, [first, second])["seq"], 2)
        self.assertIsNone(run_queue.blocker(first, [first, second]))
        self.assertEqual(run_queue.blocker(first, [first, second, owner])["seq"], 4)  # FRONT first however long it is
        self.assertIsNone(run_queue.blocker(owner, [first, second, owner]))

    def test_a_ticket_without_an_estimate_counts_by_its_kinds_default(self) -> None:
        old = {"seq": 9, "kind": "dotnet", "startedAt": None, "enqueuedAt": 10.0}
        self.assertEqual(run_queue.estimate_of(old), run_queue.DEFAULT_ESTIMATE_SECONDS["dotnet"])
        self.assertEqual(run_queue.estimate_of({**old, "estimateSeconds": -3}), run_queue.DEFAULT_ESTIMATE_SECONDS["dotnet"])
        self.assertEqual(run_queue.estimate_of({**old, "estimateSeconds": True}), run_queue.DEFAULT_ESTIMATE_SECONDS["dotnet"])
        self.assertEqual(run_queue.estimate_of({**old, "estimateSeconds": 0}), 0.0)
        self.assertEqual(run_queue._key(old)[1], 10.0 + run_queue.ORDER_WEIGHT * run_queue.DEFAULT_ESTIMATE_SECONDS["dotnet"])


class QuickKindTests(unittest.TestCase):
    """ARC-DEV-390: a `quick` run (project import, headless self-test) takes a place beside the Godot slot."""

    def test_a_quick_run_does_not_wait_for_the_godot_slot_nor_does_it_hold_it(self) -> None:
        live = [ticket(1, "window", started=True), ticket(2, "window"), ticket(3, "quick")]
        self.assertIsNone(run_queue.blocker(live[2], live))
        running = [ticket(1, "quick", started=True), ticket(2, "window")]
        self.assertIsNone(run_queue.blocker(running[1], running))

    def test_quick_runs_fill_their_own_places_in_arrival_order(self) -> None:
        self.assertEqual(run_queue.QUICK_SLOTS, 2)
        live = [ticket(1, "quick", started=True), ticket(2, "dotnet", started=True), ticket(3, "quick", started=True),
                ticket(4, "quick"), ticket(5, "quick")]
        self.assertEqual(run_queue.blocker(live[3], live)["seq"], 1)  # a dotnet run takes no quick place
        self.assertEqual(run_queue.blocker(live[4], live)["seq"], 3)
        self.assertIsNone(run_queue.blocker(live[3], live[1:]))

    def test_an_export_waits_for_a_running_quick_run_and_a_quick_run_waits_for_a_running_or_draining_export(self) -> None:
        live = [ticket(1, "quick", started=True), ticket(2, "export")]
        self.assertEqual(run_queue.blocker(live[1], live)["seq"], 1)
        running = [ticket(1, "export", started=True), ticket(2, "quick")]
        self.assertEqual(run_queue.blocker(running[1], running)["seq"], 1)
        draining = [ticket(1, "quick", started=True), ticket(2, "export"), ticket(3, "quick")]
        self.assertEqual(run_queue.blocker(draining[2], draining)["seq"], 2)  # the export is next for the slot: no new quick run
        waiting = [ticket(1, "window", started=True), ticket(2, "export"), ticket(3, "quick")]
        self.assertIsNone(run_queue.blocker(waiting[2], waiting))  # the export still waits for the Godot slot

    def test_a_quick_run_can_nest_in_a_godot_run_but_not_in_a_dotnet_run(self) -> None:
        for parent in ("window", "headless", "export", "quick"):
            self.assertIn("quick", run_queue.COVERS[parent])
        self.assertNotIn("quick", run_queue.COVERS["dotnet"])


class BuildServerTests(unittest.TestCase):
    """ARC-DEV-300: no dotnet build server outlives its run, and `dotnet build-server shutdown` uses the pinned SDK."""

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.sdk = Path(self.temp.name) / "sdk"
        self.sdk.mkdir()
        (self.sdk / "dotnet").write_text("#!/bin/sh\n")
        (self.sdk / "dotnet").chmod(0o755)
        self.environment = {"DOTNET_ROOT": str(self.sdk), "PATH": "/usr/bin:/bin"}
        self.calls: list[tuple] = []

    def tearDown(self) -> None:
        self.temp.cleanup()

    def call(self, code: int = 0, stderr: str = ""):
        def fake(argv, **options):
            self.calls.append((argv, options))
            return subprocess.CompletedProcess(argv, code, "", stderr)
        return fake

    def test_every_kind_gets_the_no_build_server_environment(self) -> None:
        for kind in run_queue.KINDS:
            environment = run_queue.command_environment(kind, 7, {"UseSharedCompilation": "true", "PATH": "/usr/bin"})
            for name, value in run_queue.BUILD_ENVIRONMENT.items():
                self.assertEqual(environment[name], value, (kind, name))

    def test_shutdown_runs_the_pinned_sdk_from_the_repository(self) -> None:
        environment = run_queue.command_environment("dotnet", 7, self.environment)
        self.assertIsNone(run_queue.shutdown_build_servers(environment, [ticket(7, "dotnet")], 7, self.call()))
        argv, options = self.calls[0]
        self.assertEqual(argv, [str(self.sdk / "dotnet"), "build-server", "shutdown"])
        self.assertEqual(options["cwd"], REPO)
        self.assertEqual(options["env"]["DOTNET_ROOT"], str(self.sdk))

    def test_shutdown_waits_while_another_dotnet_run_holds_a_place(self) -> None:
        live = [ticket(6, "dotnet", started=True), ticket(7, "dotnet"), ticket(8, "window", started=True)]
        self.assertIsNone(run_queue.shutdown_build_servers(self.environment, live[:1], 7, self.call()))
        self.assertEqual(self.calls, [])
        self.assertIsNone(run_queue.shutdown_build_servers(self.environment, live[2:], 7, self.call()))
        self.assertEqual(len(self.calls), 1)  # a Godot run does not hold it back

    def test_shutdown_can_be_switched_off_for_tests(self) -> None:
        environment = {**self.environment, run_queue.SHUTDOWN_ENV: "0"}
        self.assertIsNone(run_queue.shutdown_build_servers(environment, [], 7, self.call()))
        self.assertEqual(self.calls, [])

    def test_a_missing_pinned_sdk_a_failure_and_an_error_are_notes_not_exceptions(self) -> None:
        missing = {"DOTNET_ROOT": str(self.sdk / "nowhere"), "PATH": "/usr/bin:/bin"}
        self.assertIn("no pinned SDK", run_queue.shutdown_build_servers(missing, [], 7, self.call()))
        note = run_queue.shutdown_build_servers(self.environment, [], 7, self.call(1, "A compatible .NET SDK was not found.\nmore"))
        self.assertEqual(note, "build servers not shut down: exit 1 A compatible .NET SDK was not found.")

        def broken(argv, **options):
            raise subprocess.TimeoutExpired(argv, 60)
        self.assertIn("not shut down", run_queue.shutdown_build_servers(self.environment, [], 7, broken))


class QueueRunTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        self.log = self.root / "events.log"
        self.env = patch.dict(os.environ, {"ARCANUM_LOCAL_HOME": str(self.root / "home"),
                                           "ARCANUM_QUEUE_LEGACY_LOCK": str(self.root / "legacy.lock"),
                                           run_queue.SHUTDOWN_ENV: "0"})
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

    def queued(self, kind: str, name: str, seconds: float, *, timeout: str | None = None,
               estimate: float | None = None) -> subprocess.Popen:
        code = (f"import time; open({str(self.log)!r}, 'a').write('start {name} %f\\n' % time.time()); time.sleep({seconds}); "
                f"open({str(self.log)!r}, 'a').write('end {name} %f\\n' % time.time())")
        return self.wrapper(kind, name, [sys.executable, "-c", code], timeout=timeout, estimate=estimate)

    def live(self) -> list[dict]:
        with run_queue._QueueLock():
            return [item for _, item in run_queue.tickets(prune=False)]

    def wrapper(self, kind: str, name: str, command: list[str], *, timeout: str | None = None,
                environment: dict | None = None, estimate: float | None = None) -> subprocess.Popen:
        before = len(self.live())
        argv = [sys.executable, "-B", str(ARCANUM), "run", "--kind", kind, "--label", name, "--start-within", "0",
                *(["--timeout", timeout] if timeout else []), *(["--estimate", str(estimate)] if estimate is not None else []),
                "--", *command]
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

    def test_a_short_run_that_arrived_later_starts_before_a_long_one_and_the_ticket_keeps_the_estimate(self) -> None:
        self.queued("window", "running", 1.5)
        self.until(lambda: self.log.exists())
        self.queued("window", "long", 0.2, estimate=600.0)
        self.queued("window", "short", 0.2, estimate=5.0)
        self.until(lambda: len(self.live()) == 3)
        self.assertEqual(sorted(item.get("estimateSeconds") for item in self.live() if item["label"] != "running"), [5.0, 600.0])
        self.finish()
        found = self.events()
        self.assertLess(found["start short"], found["start long"])
        self.assertGreaterEqual(found["start short"], found["end running"])

    def test_a_quick_run_starts_while_a_godot_run_holds_the_slot(self) -> None:
        self.queued("window", "w", 2.0)
        self.until(lambda: self.log.exists())
        self.queued("quick", "q", 0.2)
        self.finish()
        found = self.events()
        self.assertLess(found["end q"], found["end w"])

    def test_an_estimate_must_not_be_negative(self) -> None:
        with self.assertRaisesRegex(ValueError, "--estimate"):
            run_queue.run("dotnet", ["true"], estimate=-1.0)

    def test_dotnet_runs_alongside_a_godot_run(self) -> None:
        self.queued("window", "w", 2.0)
        self.until(lambda: self.log.exists())
        self.queued("dotnet", "d", 0.2)
        self.finish()
        found = self.events()
        self.assertLess(found["end d"], found["end w"])

    def test_a_third_dotnet_run_waits_for_a_free_dotnet_place(self) -> None:
        self.queued("dotnet", "a", 1.2)
        self.until(lambda: self.log.exists())
        self.queued("dotnet", "b", 1.2)
        self.queued("dotnet", "c", 0.2)
        self.finish()
        found = self.events()
        self.assertLess(found["start b"], found["end a"])  # two places: a and b overlap
        self.assertGreaterEqual(found["start c"], min(found["end a"], found["end b"]))

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
