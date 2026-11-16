"""ARC-DEV-033: the launch queue runs Godot runs one at a time in arrival order, quiets the Mac for an export and frees
the place of a run that crashed, was killed or ran out of time."""
from __future__ import annotations

import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

from tools.local_dev import run_queue

REPO = Path(__file__).resolve().parents[2]
ARCANUM = REPO / "arcanum"


def ticket(seq: int, kind: str, started: bool = False, priority: int = run_queue.NORMAL, at: float | None = None,
           estimate: float | None = None, cwd: str | None = None, lane: str | None = None) -> dict:
    """A ticket; `at` is its arrival time (default: its number, so equal estimates keep the arrival order)."""
    result = {"seq": seq, "kind": kind, "startedAt": 1.0 if started else None, "priority": priority,
              "enqueuedAt": float(seq) if at is None else at}
    if estimate is not None:
        result["estimateSeconds"] = estimate
    if cwd is not None:
        result["cwd"] = cwd  # a ticket of the tests has no `.git` above its folder: the checkout is the folder
    if lane is not None:
        result["lane"] = lane  # none: a ticket of a wrapper from before ARC-DEV-400, which runs alone
    return result


class AdmissionRuleTests(unittest.TestCase):
    def setUp(self) -> None:  # ARC-DEV-1353: a settings file of the owner's real queue folder must not reach the test
        self.temp = tempfile.TemporaryDirectory()
        self.env = patch.dict(os.environ, {"ARCANUM_LOCAL_HOME": str(Path(self.temp.name) / "home")})
        self.env.start()
        os.environ.pop(run_queue.LANES_ENV, None)

    def tearDown(self) -> None:
        self.env.stop()
        self.temp.cleanup()

    def test_godot_kinds_share_one_slot_in_arrival_order(self) -> None:
        live = [ticket(1, "headless", started=True), ticket(2, "window"), ticket(3, "export")]
        self.assertEqual(run_queue.blocker(live[1], live)["seq"], 1)
        self.assertEqual(run_queue.blocker(live[2], live)["seq"], 1)
        self.assertIsNone(run_queue.blocker(live[1], live[1:]))
        self.assertEqual(run_queue.blocker(live[2], live[1:])["seq"], 2)

    # ARC-DEV-2272: the owner-is-playing mode
    def playing_ticket(self, until: float | None = None) -> dict:
        return {**ticket(0, "window", started=True, priority=run_queue.FRONT, lane=run_queue.SINGLE), "playing": True,
                "since": time.time(), "until": until}

    def test_playing_holds_ordinary_window_runs_only(self) -> None:
        playing = self.playing_ticket()
        window, headless, export = ticket(5, "window"), ticket(6, "headless"), ticket(7, "export")
        for item in (ticket(8, "dotnet"), ticket(9, "quick")):
            self.assertIsNone(run_queue.blocker(item, [playing, item]), item["kind"])
        self.assertIs(run_queue.blocker(window, [playing, window]), playing)
        self.assertIsNone(run_queue.blocker(headless, [playing, headless]))  # a run with no window goes on
        self.assertIsNone(run_queue.blocker(export, [playing, export]))
        self.assertIsNone(run_queue.blocker(window, [window]))  # no mode: unchanged
        owner = ticket(4, "window", priority=run_queue.FRONT)  # ./arcanum area|world|map and package
        self.assertIsNone(run_queue.blocker(owner, [playing, owner]))
        low = ticket(3, "window", priority=run_queue.LOW)  # main-smoke
        self.assertIs(run_queue.blocker(low, [playing, low]), playing)

    def test_a_window_run_the_mode_holds_does_not_block_the_runs_behind_it(self) -> None:
        playing = self.playing_ticket()
        live = [playing, ticket(5, "window", at=1.0), ticket(6, "headless", at=2.0), ticket(7, "window", at=3.0)]
        self.assertIsNone(run_queue.blocker(live[2], live))  # the held window ahead of it fills no Godot place
        self.assertIs(run_queue.blocker(live[3], live), playing)

    def test_the_mode_does_not_count_as_a_running_godot_run(self) -> None:
        playing = self.playing_ticket()
        self.assertEqual(run_queue._peers([playing, ticket(5, "window")], 5), 0)  # no peer for the lane accounting or the load guard
        self.assertIn("owner playing", run_queue._describe(playing))
        self.assertIn("--not-playing", run_queue._describe(playing))

    def test_an_expired_mode_holds_nothing(self) -> None:
        window = ticket(5, "window")
        gone = self.playing_ticket(until=time.time() - 1)  # its holder takes a few seconds more to end
        self.assertIsNone(run_queue.blocker(window, [gone, window]))
        self.assertIsNotNone(run_queue.blocker(window, [self.playing_ticket(until=time.time() + 3600), window]))

    def test_the_playing_ticket_reads_as_a_running_exclusive_godot_run_to_a_wrapper_of_before_the_mode(self) -> None:
        # the contract that binds the wrappers of other checkouts: a readable ticket of this schema, started, a Godot kind,
        # in the exclusive single lane; those wrappers do not know the flag, so every Godot run waits behind it
        playing = self.playing_ticket()
        self.assertIn(playing["kind"], run_queue.GODOT_KINDS)
        self.assertEqual(run_queue.lane_of(playing), run_queue.SINGLE)
        self.assertIn(run_queue.SINGLE, run_queue.EXCLUSIVE_LANES)
        self.assertTrue(playing["startedAt"])

    def test_the_mode_still_holds_the_windows_ahead_of_an_export_when_dotnet_runs_check_the_drain(self) -> None:
        playing = self.playing_ticket()
        base = [ticket(1, "dotnet", started=True), ticket(3, "export", at=2.0), ticket(4, "dotnet", at=3.0)]
        without = [item for item in base]
        self.assertEqual(run_queue.blocker(base[2], without)["seq"], 3)  # the export is draining: the dotnet run waits for it
        held = [playing, *base, ticket(2, "window", at=1.0)]  # a window the mode holds, ahead of the export
        self.assertEqual(run_queue.blocker(held[3], held)["seq"], 3)  # it does not hide the drain
        self.assertEqual(run_queue.blocker(held[-1], held)["seq"], 0)

    def test_playing_text_names_the_time_and_the_end(self) -> None:
        now = 1_000_000.0
        self.assertEqual(run_queue.playing_text({"since": now - 90, "until": now + 7500}, now), "for 1m30s, ends in 2h05m")
        self.assertEqual(run_queue.playing_text({"since": now - 5, "until": None}, now), "for 0m05s, no time limit")

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


def lane_ticket(seq: int, kind: str = "window", lane: str | None = None, started: bool = False, estimate: float = 20.0,
                cwd: str | None = None, at: float | None = None) -> dict:
    """A ticket of a wrapper with lanes; the lane defaults to what its length gives."""
    return ticket(seq, kind, started=started, estimate=estimate, cwd=cwd or f"/work/{seq}", at=at,
                  lane=lane or run_queue.lane_for(kind, estimate, 2))


class LaneTests(unittest.TestCase):
    """ARC-DEV-400: the Godot slot as one heavy lane and two short lanes; an export, a rollback run and a run of an old
    wrapper run alone; two runs of one checkout never run at once."""

    def setUp(self) -> None:  # ARC-DEV-1353: a settings file of the owner's real queue folder must not reach the test
        self.temp = tempfile.TemporaryDirectory()
        self.env = patch.dict(os.environ, {"ARCANUM_LOCAL_HOME": str(Path(self.temp.name) / "home")})
        self.env.start()
        os.environ.pop(run_queue.LANES_ENV, None)

    def tearDown(self) -> None:
        self.env.stop()
        self.temp.cleanup()

    def waits_for(self, live: list[dict], seq: int) -> int | None:
        mine = next(item for item in live if item["seq"] == seq)
        found = run_queue.blocker(mine, live)
        return None if found is None else found["seq"]

    def test_a_run_takes_the_lane_its_declared_length_gives(self) -> None:
        self.assertEqual(run_queue.lane_for("window", 20.0, 2), "short")
        self.assertEqual(run_queue.lane_for("window", run_queue.HEAVY_SECONDS, 2), "short")
        self.assertEqual(run_queue.lane_for("headless", run_queue.HEAVY_SECONDS + 1, 2), "heavy")
        self.assertEqual(run_queue.lane_for("export", 5.0, 2), "export")
        self.assertEqual(run_queue.lane_for("window", 20.0, 0), "single")
        self.assertEqual(run_queue.lane_for("export", 5.0, 0), "export")
        self.assertIsNone(run_queue.lane_for("dotnet", 20.0, 2))
        self.assertIsNone(run_queue.lane_for("quick", 20.0, 2))

    def test_the_rollback_variable_sets_the_number_of_short_lanes(self) -> None:
        for value, lanes in ((None, 2), ("", 2), ("2", 2), ("3", 3), ("0", 0), ("off", 0), (" OFF ", 0), ("9", 0), ("-1", 0),
                             ("two", 0)):
            environment = {} if value is None else {run_queue.LANES_ENV: value}
            with patch.object(run_queue, "_LANE_NOTE", [True]):  # the note is printed once: not in the test output
                self.assertEqual(run_queue.short_lanes(environment), lanes, value)

    def test_a_heavy_run_and_two_short_runs_run_side_by_side_and_the_next_of_each_lane_waits(self) -> None:
        live = [lane_ticket(1, estimate=200.0, started=True), lane_ticket(2, started=True), lane_ticket(3, started=True),
                lane_ticket(4, estimate=300.0), lane_ticket(5), lane_ticket(6)]
        self.assertEqual(self.waits_for(live, 4), 1)  # one heavy lane
        self.assertEqual(self.waits_for(live, 5), 2)  # the oldest running short run frees the first short place
        self.assertEqual(self.waits_for(live, 6), 3)
        self.assertIsNone(self.waits_for(live[:3], 2))
        self.assertIsNone(self.waits_for([lane_ticket(1, estimate=200.0, started=True), lane_ticket(2)], 2))
        self.assertIsNone(self.waits_for([lane_ticket(1, started=True), lane_ticket(2, estimate=200.0)], 2))

    def test_a_short_run_does_not_wait_for_the_heavy_lane_nor_the_reverse(self) -> None:
        heavy = [lane_ticket(1, estimate=500.0, started=True), lane_ticket(2, estimate=500.0), lane_ticket(3)]
        self.assertEqual(self.waits_for(heavy, 2), 1)
        self.assertIsNone(self.waits_for(heavy, 3))

    def test_the_short_places_follow_the_number_of_lanes(self) -> None:
        live = [lane_ticket(1, started=True), lane_ticket(2, started=True), lane_ticket(4)]
        self.assertEqual(self.waits_for(live, 4), 1)  # two short places, both taken
        with patch.dict(os.environ, {run_queue.LANES_ENV: "3"}):
            self.assertIsNone(self.waits_for(live, 4))  # a third place is free
            full = [lane_ticket(1, started=True), lane_ticket(2, started=True), lane_ticket(3, started=True), lane_ticket(4)]
            self.assertEqual(self.waits_for(full, 4), 1)

    def test_two_runs_of_one_checkout_never_run_at_once_in_any_lane(self) -> None:
        live = [lane_ticket(1, estimate=200.0, started=True, cwd="/work/a"), lane_ticket(2, cwd="/work/a"),
                lane_ticket(3, cwd="/work/b")]
        self.assertEqual(self.waits_for(live, 2), 1)
        self.assertIsNone(self.waits_for(live, 3))

    def test_a_run_held_back_by_its_checkout_fills_no_place_of_another_checkouts_run(self) -> None:
        # a (short, running) and b (short, running) hold both short places; c waits for the place of a
        live = [lane_ticket(1, started=True, cwd="/work/a"), lane_ticket(2, started=True, cwd="/work/b"),
                lane_ticket(3, estimate=200.0, started=True, cwd="/work/c"), lane_ticket(4, cwd="/work/c"),
                lane_ticket(5, cwd="/work/d")]
        self.assertEqual(self.waits_for(live, 4), 3)  # its own checkout's heavy run, whatever the places
        # d: the short places are full (a, b); c's second run (4) waits for c's heavy run and fills none, so d is next after a
        self.assertEqual(self.waits_for(live, 5), 1)
        free = [lane_ticket(1, started=True, cwd="/work/a"), lane_ticket(3, estimate=200.0, started=True, cwd="/work/c"),
                lane_ticket(4, cwd="/work/c"), lane_ticket(5, cwd="/work/d")]
        self.assertIsNone(self.waits_for(free, 5))  # one short place is free and 4, held by c, does not take it

    def test_an_export_runs_alone_and_waits_for_every_lane(self) -> None:
        live = [lane_ticket(1, estimate=200.0, started=True), lane_ticket(2, started=True), lane_ticket(3, "export"),
                lane_ticket(4, at=100000.0), lane_ticket(5, estimate=200.0, at=100000.0)]
        self.assertEqual(self.waits_for(live, 3), 1)
        self.assertEqual(self.waits_for(live, 4), 3)  # a run that arrives well after an export waits for it
        self.assertEqual(self.waits_for(live, 5), 3)
        self.assertIsNone(self.waits_for([lane_ticket(3, "export")], 3))
        running = [lane_ticket(3, "export", started=True), lane_ticket(4)]
        self.assertEqual(self.waits_for(running, 4), 3)
        two = [lane_ticket(2, started=True), lane_ticket(3, "export", started=True)]
        self.assertEqual(run_queue.blocker(two[1], two)["seq"], 2)  # (never admitted together; the rule is symmetric)

    def test_a_front_export_waits_only_for_the_runs_that_hold_a_place(self) -> None:
        # ARC-DEV-400: `./arcanum package` queues its export at the front, so no smoke that arrives before it passes it
        export = lane_ticket(9, "export", at=500.0, estimate=300.0)
        export["priority"] = run_queue.FRONT
        live = [lane_ticket(1, started=True, at=1.0), lane_ticket(2, estimate=5.0, at=490.0, lane="short"),
                lane_ticket(3, estimate=5.0, at=495.0, lane="short"), export]
        self.assertEqual(run_queue.blocker(export, live)["seq"], 1)  # only the running run
        self.assertEqual(run_queue.blocker(live[1], live)["seq"], 9)  # a smoke queued earlier now waits for it
        self.assertIsNone(run_queue.blocker(export, [export]))
        self.assertIsNone(run_queue.blocker(export, [lane_ticket(1, kind="dotnet", started=True), export][1:]))

    def test_an_export_waits_for_the_runs_ahead_of_it_and_for_running_side_runs(self) -> None:
        live = [lane_ticket(1, estimate=5.0), lane_ticket(2, "export", estimate=5.0), lane_ticket(3, "dotnet", started=True)]
        self.assertIsNone(self.waits_for(live[:1], 1))
        self.assertEqual(self.waits_for(live, 2), 1)
        self.assertEqual(self.waits_for([live[1], live[2]], 2), 3)

    def test_a_rollback_run_and_a_run_of_an_old_wrapper_run_alone(self) -> None:
        for alone in (lane_ticket(1, lane="single", started=True), ticket(1, "window", started=True)):
            live = [alone, lane_ticket(2, cwd="/work/2")]
            self.assertEqual(self.waits_for(live, 2), 1)
        live = [lane_ticket(1, started=True), lane_ticket(2, lane="single", cwd="/work/2")]
        self.assertEqual(self.waits_for(live, 2), 1)
        live = [lane_ticket(1, lane="single", started=True), lane_ticket(2, lane="single", cwd="/work/2")]
        self.assertEqual(self.waits_for(live, 2), 1)

    def test_a_quick_run_of_the_same_checkout_still_goes_before_a_later_lane_run(self) -> None:
        live = [ticket(1, "quick", started=True, cwd="/work/a"), lane_ticket(2, cwd="/work/a"), lane_ticket(3, cwd="/work/b")]
        self.assertEqual(self.waits_for(live, 2), 1)
        self.assertIsNone(self.waits_for(live, 3))

    def test_the_queue_never_stalls_whatever_arrives(self) -> None:
        # a model of the queue: random runs in every kind and lane arrive; whenever nothing runs, some waiter may start
        import random
        for seed in range(60):
            rng = random.Random(seed)
            live, seq = [], 0
            for step in range(120):
                if rng.random() < 0.55 and len([item for item in live if not item.get("startedAt")]) < 10:
                    seq += 1
                    kind = rng.choice(["window", "window", "headless", "export", "quick", "dotnet"])
                    estimate = rng.choice([5.0, 20.0, 60.0, 120.0, 600.0])
                    lane = None if rng.random() < 0.1 and kind != "quick" else run_queue.lane_for(kind, estimate, rng.choice([2, 2, 0]))
                    live.append(ticket(seq, kind, estimate=estimate, cwd=f"/work/{rng.randint(1, 3)}", lane=lane,
                                       at=float(step) + rng.random()))
                waiting = [item for item in live if not item.get("startedAt")]
                startable = [item for item in waiting if run_queue.blocker(item, live) is None]
                if waiting and not any(item.get("startedAt") for item in live):
                    self.assertTrue(startable, f"seed {seed} step {step}: nothing can start")
                for item in startable[:rng.randint(0, len(startable))]:
                    if run_queue.blocker(item, live) is None:  # admitted one at a time, as under the queue lock
                        item["startedAt"] = 1.0
                running = [item for item in live if item.get("startedAt")]
                for item in rng.sample(running, rng.randint(0, len(running))):
                    live.remove(item)
            running = [item for item in live if item.get("startedAt") and item["kind"] in run_queue.GODOT_KINDS]
            for name, cap in (("heavy", 1), ("short", 2)):
                self.assertLessEqual(sum(1 for item in running if run_queue.lane_of(item) == name and item.get("lane") != "single"), cap)
            checkouts = [run_queue.checkout_of(item) for item in running]
            self.assertEqual(len(checkouts), len(set(checkouts)), f"seed {seed}: two runs of one checkout")
            for first in live:  # the admitted set holds the rules: at most one heavy, the short places, exports and old runs alone
                if first.get("startedAt") and run_queue.lane_of(first) in run_queue.EXCLUSIVE_LANES and first["kind"] in run_queue.GODOT_KINDS:
                    others = [item for item in live if item.get("startedAt") and item is not first and item["kind"] in run_queue.GODOT_KINDS]
                    self.assertEqual(others, [], f"seed {seed}: {first} runs beside {others}")


class LaneAccountingTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.env = patch.dict(os.environ, {"ARCANUM_LOCAL_HOME": str(Path(self.temp.name) / "home")})
        self.env.start()

    def tearDown(self) -> None:
        self.env.stop()
        self.temp.cleanup()

    def entry(self, seq: int, exit_code: int, lane: str | None = "short", peers: int | None = 0, **fields) -> dict:
        result = {"seq": seq, "kind": "window", "label": f"run {seq}", "cwd": "/work/a", "estimateSeconds": 20.0,
                  "enqueuedAt": 1000.0 + seq, "startedAt": 1001.0 + seq, "endedAt": 1010.0 + seq, "exit": exit_code}
        if lane is not None:
            result["lane"] = lane
        if peers is not None:
            result.update({"peersStart": peers, "peersEnd": peers, "loadStart": 12.5, "loadEnd": 37.0})
        result.update(fields)
        return result

    def test_a_stop_is_not_red_and_every_other_non_zero_exit_is(self) -> None:
        self.assertFalse(run_queue.is_red(0))
        for stopped in (129, 130, 143):
            self.assertFalse(run_queue.is_red(stopped), stopped)
        for red in (1, 3, 124, 125, 139, 245):
            self.assertTrue(run_queue.is_red(red), red)
        self.assertFalse(run_queue.is_red(None))
        self.assertFalse(run_queue.is_red(True))

    def test_an_entry_from_before_the_lanes_goes_to_the_lane_its_length_would_take(self) -> None:
        old = {"kind": "window", "estimateSeconds": 540.0}
        self.assertEqual(run_queue.lane_of_entry(old), "heavy")
        self.assertEqual(run_queue.lane_of_entry({"kind": "headless", "estimateSeconds": 30.0}), "short")
        self.assertEqual(run_queue.lane_of_entry({"kind": "export"}), "export")
        self.assertEqual(run_queue.lane_of_entry({"kind": "window", "lane": "single"}), "single")

    def test_the_accounting_counts_red_runs_alone_and_beside_others(self) -> None:
        entries = [self.entry(1, 0), self.entry(2, 1), self.entry(3, 0, peers=2), self.entry(4, 1, peers=1),
                   self.entry(5, 1, peers=1), self.entry(6, 0, lane="heavy", peers=None), self.entry(7, 130),
                   self.entry(8, 1, peers=0, startedAt=None)]
        entries += [self.entry(20, 1, lane=None, kind="dotnet"), self.entry(21, 1, lane="quick", kind="quick")]
        lanes = run_queue.lane_accounting(entries)
        self.assertEqual(sorted(lanes), ["heavy", "short"])  # dotnet and quick runs have no lane
        self.assertEqual((lanes["short"]["runs"], lanes["short"]["red"], lanes["short"]["stopped"]), (6, 3, 1))
        self.assertEqual((lanes["short"]["alone"], lanes["short"]["beside"]), ([3, 1], [3, 2]))
        self.assertEqual(lanes["heavy"]["unknown"], [1, 0])

    def test_the_report_lists_the_lanes_and_the_latest_red_runs_with_their_conditions(self) -> None:
        self.assertEqual(run_queue.lane_report(), ["no finished runs yet"])
        folder = run_queue._open_folder()
        (folder / "history.jsonl").write_text("\n".join(json.dumps(item) for item in [
            self.entry(1, 0), self.entry(2, 1, peers=2)]) + "\nnot json\n" + json.dumps(self.entry(3, 0, lane="heavy", peers=0)) + "\n")
        lines = run_queue.lane_report()
        text = "\n".join(lines)
        self.assertIn("lanes over the last 3 Godot runs", lines[0])
        self.assertRegex(lines[2], r"^heavy +1 +0 +0.0%")
        self.assertRegex(lines[3], r"^short +2 +1 +50.0%")
        self.assertIn("red ", text)
        self.assertIn("#2 short exit=1 peers 2/2 load 12.5/37", text)
        self.assertEqual(len(run_queue.lane_report(history=1)), 3)  # the last run only: one lane, no red run

    def test_a_finished_run_records_its_lane_peers_and_load(self) -> None:
        with patch.dict(os.environ, {run_queue.SHUTDOWN_ENV: "0", "ARCANUM_QUEUE_LEGACY_LOCK": str(Path(self.temp.name) / "legacy.lock")}):
            os.environ.pop(run_queue.TICKET_ENV, None)
            self.assertEqual(run_queue.run("window", [sys.executable, "-c", "pass"], estimate=500.0), 0)
            self.assertEqual(run_queue.run("window", [sys.executable, "-c", "raise SystemExit(3)"], estimate=20.0), 3)
            self.assertEqual(run_queue.run("dotnet", [sys.executable, "-c", "pass"]), 0)
        entries = [json.loads(line) for line in (run_queue.folder() / "history.jsonl").read_text().splitlines()]
        self.assertEqual([item["lane"] for item in entries[:2]], ["heavy", "short"])
        self.assertEqual([item["peersStart"] for item in entries[:2]], [0, 0])
        self.assertEqual([item["peersEnd"] for item in entries[:2]], [0, 0])
        self.assertIn("loadStart", entries[0])
        self.assertNotIn("lane", entries[2])  # a dotnet run has none


class OwnerCapTests(unittest.TestCase):
    """ARC-DEV-1353: the owner's caps live in the queue folder, so one setting caps the runs of every thread."""

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.env = patch.dict(os.environ, {"ARCANUM_LOCAL_HOME": str(Path(self.temp.name) / "home")})
        self.env.start()
        os.environ.pop(run_queue.LANES_ENV, None)

    def tearDown(self) -> None:
        self.env.stop()
        self.temp.cleanup()

    def test_no_file_means_the_default_and_reading_creates_nothing(self) -> None:
        self.assertEqual(run_queue.lanes_setting(), (run_queue.DEFAULT_SHORT_LANES, "default"))
        self.assertIsNone(run_queue.max_load())
        self.assertFalse(run_queue.folder().exists())

    def test_the_file_sets_the_lanes_for_every_wrapper_and_default_removes_it(self) -> None:
        run_queue.write_settings({"shortLanes": 1})
        self.assertEqual(run_queue.lanes_setting(), (1, "settings file"))
        self.assertEqual(run_queue.lane_for("window", 200.0), "heavy")
        run_queue.write_settings({"shortLanes": 0})
        self.assertEqual(run_queue.lane_for("window", 20.0), "single")  # one Godot run at a time
        run_queue.write_settings({"shortLanes": None})
        self.assertEqual(run_queue.lanes_setting(), (run_queue.DEFAULT_SHORT_LANES, "default"))

    def test_the_variable_wins_over_the_file_and_a_given_mapping_never_reads_the_file(self) -> None:
        run_queue.write_settings({"shortLanes": 1})
        with patch.dict(os.environ, {run_queue.LANES_ENV: "3"}):
            self.assertEqual(run_queue.lanes_setting(), (3, "environment"))
        self.assertEqual(run_queue.short_lanes({}), run_queue.DEFAULT_SHORT_LANES)

    def test_an_unreadable_or_out_of_range_file_value_is_ignored(self) -> None:
        for text in ("not json", "[]", '{"shortLanes": 99}', '{"shortLanes": "2"}', '{"shortLanes": true}', '{"maxLoad": -1}',
                     '{"maxLoad": "x"}'):
            (run_queue._open_folder() / run_queue.SETTINGS_FILE).write_text(text)
            self.assertEqual(run_queue.lanes_setting(), (run_queue.DEFAULT_SHORT_LANES, "default"), text)
            self.assertIsNone(run_queue.max_load(), text)

    def test_the_load_guard_holds_a_run_only_while_another_godot_run_runs_and_the_load_is_above_it(self) -> None:
        self.assertIsNone(run_queue.too_loaded(2, 99.0))  # no guard set
        run_queue.write_settings({"maxLoad": 12.0})
        self.assertEqual(run_queue.too_loaded(1, 13.0), 12.0)
        self.assertIsNone(run_queue.too_loaded(1, 12.0))
        self.assertIsNone(run_queue.too_loaded(0, 99.0))  # a lone run always starts
        with patch.object(run_queue, "_load", return_value=None):
            self.assertIsNone(run_queue.too_loaded(1))  # the system gives no load figure: no guard
        with patch.object(run_queue, "_load", return_value=40.0):
            self.assertEqual(run_queue.too_loaded(1), 12.0)  # no figure given: it reads the system's
        run_queue.write_settings({"maxLoad": None})
        self.assertIsNone(run_queue.too_loaded(1, 99.0))
        (run_queue._open_folder() / run_queue.SETTINGS_FILE).write_text('{"maxLoad": 1' + "0" * 400 + "}")
        self.assertIsNone(run_queue.max_load())  # too large for a float: no guard, no crash

    def test_the_status_line_names_the_caps_and_their_source(self) -> None:
        run_queue.write_settings({"shortLanes": 1, "maxLoad": 10})
        self.assertIn("short lanes 1 from settings file", run_queue.settings_line())
        self.assertIn("load guard 10", run_queue.settings_line())

    def test_the_command_sets_and_clears_the_caps(self) -> None:
        environment = {**os.environ}
        def run(*args: str) -> subprocess.CompletedProcess:
            return subprocess.run([sys.executable, "-B", str(ARCANUM), "queue", *args], capture_output=True, text=True,
                                  env=environment, cwd=REPO, timeout=60)
        done = run("--set-lanes", "1", "--set-max-load", "12")
        self.assertEqual(done.returncode, 0, done.stderr)
        self.assertEqual(json.loads((run_queue.folder() / run_queue.SETTINGS_FILE).read_text()), {"maxLoad": 12.0, "shortLanes": 1})
        self.assertIn("short lanes 1 from settings file", done.stdout)
        self.assertNotEqual(run("--set-lanes", "9").returncode, 0)
        self.assertNotEqual(run("--set-max-load", "0").returncode, 0)
        self.assertNotEqual(run("--set-max-load", "1_0").returncode, 0)
        self.assertNotEqual(run("--set-lanes", "1", "--history", "3").returncode, 0)
        self.assertEqual(run("--set-lanes", "default", "--set-max-load", "off").returncode, 0)
        self.assertEqual(json.loads((run_queue.folder() / run_queue.SETTINGS_FILE).read_text()), {})


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


    def test_while_a_ticket_of_an_older_wrapper_waits_everyone_orders_by_number(self) -> None:
        # a wrapper from before ARC-DEV-390 (no estimate) waits for every smaller number: ordering the others by length
        # would let a new run wait for an older-numbered one that waits for it (A passed by B, B behind A for ever)
        a = ticket(4, "window", at=100.0, estimate=600.0)
        b = ticket(6, "window", at=110.0, estimate=20.0)
        old = ticket(5, "window")
        del old["enqueuedAt"]
        self.assertEqual(run_queue.blocker(a, [a, b])["seq"], 6)  # by length alone: b first
        live = [a, old, b]
        self.assertIsNone(run_queue.blocker(a, live))  # with the old ticket waiting: by number, a is first
        self.assertEqual(run_queue.blocker(old, live)["seq"], 4)
        self.assertEqual(run_queue.blocker(b, live)["seq"], 4)  # behind both, the first of them
        self.assertEqual([item["seq"] for _, item in self.sorted(live)], [4, 5, 6])
        running_old = [a, b, {**old, "startedAt": 1.0}]
        self.assertEqual(run_queue.blocker(a, running_old)["seq"], 5)  # a running old ticket blocks, but orders nothing
        self.assertEqual(run_queue.order_of(running_old), run_queue._key)

    def sorted(self, live):
        order = run_queue.order_of(live)
        return [(None, item) for item in sorted(live, key=order)]

    def test_an_estimate_is_capped_and_a_non_finite_one_refused(self) -> None:
        for bad in (float("inf"), float("nan"), -1.0):
            with self.assertRaisesRegex(ValueError, "--estimate"):
                run_queue.run("dotnet", ["true"], estimate=bad)


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

    def test_a_checkouts_project_folder_is_never_imported_or_run_twice_at_once(self) -> None:
        # two sessions in one checkout, an import and a window run (or two imports) of the same project folder: one at a time
        first, second = ticket(1, "quick", started=True, cwd="/a"), ticket(2, "quick", cwd="/a")
        other = ticket(3, "quick", cwd="/b")
        self.assertEqual(run_queue.blocker(second, [first, second, other])["seq"], 1)
        self.assertIsNone(run_queue.blocker(other, [first, other]))  # another checkout's folder: its own place
        window_running = ticket(4, "window", started=True, cwd="/a")
        self.assertEqual(run_queue.blocker(ticket(5, "quick", cwd="/a"), [window_running])["seq"], 4)
        self.assertIsNone(run_queue.blocker(ticket(6, "quick", cwd="/b"), [window_running]))
        self.assertEqual(run_queue.blocker(ticket(7, "window", cwd="/a"), [first])["seq"], 1)  # the window waits for that import
        self.assertIsNone(run_queue.blocker(ticket(8, "window", cwd="/b"), [first]))

    def test_an_export_and_a_queued_quick_run_of_one_checkout_do_not_wait_for_each_other(self) -> None:
        quick, export = ticket(1, "quick", cwd="/a"), ticket(2, "export", cwd="/a")
        self.assertEqual(run_queue.blocker(export, [quick, export])["seq"], 1)  # the quick run goes first (same project folder)
        self.assertIsNone(run_queue.blocker(quick, [quick, export]))  # and it is not held by the export that waits for it
        elsewhere = ticket(3, "quick", cwd="/b")
        self.assertEqual(run_queue.blocker(elsewhere, [elsewhere, export])["seq"], 2)  # another checkout's: the export is draining
        holder = ticket(6, "window", started=True, cwd="/z")
        self.assertIsNone(run_queue.blocker(elsewhere, [holder, elsewhere, export]))  # the export still waits for the slot
        running = ticket(4, "quick", started=True, cwd="/c")
        self.assertEqual(run_queue.blocker(ticket(5, "quick", cwd="/d"), [running, export])["seq"], 2)  # draining: the export is next

    def test_a_quick_run_held_back_by_its_own_checkout_does_not_hold_up_another_checkouts(self) -> None:
        window = ticket(1, "window", started=True, cwd="/a")
        waiting_a1, waiting_a2 = ticket(2, "quick", cwd="/a"), ticket(3, "quick", cwd="/a")
        other = ticket(4, "quick", cwd="/b")
        live = [window, waiting_a1, waiting_a2, other]
        self.assertEqual(run_queue.blocker(waiting_a2, live)["seq"], 1)
        self.assertIsNone(run_queue.blocker(other, live))  # /b's import starts at once, behind nothing of /a's
        # the checkout's own quick run goes before a later Godot run of that checkout, so a chain of windows cannot hold it back
        later_window = ticket(5, "window", cwd="/a")
        self.assertEqual(run_queue.blocker(later_window, [waiting_a1, later_window])["seq"], 2)
        self.assertIsNone(run_queue.blocker(ticket(6, "window", cwd="/b"), [waiting_a1]))

    def test_the_checkout_is_the_folder_with_git_above_the_queued_one(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder).resolve()
            (root / "repo" / "tools" / "x").mkdir(parents=True)
            (root / "repo" / ".git").write_text("gitdir: elsewhere\n")  # a worktree has a .git file
            (root / "plain").mkdir()
            self.assertEqual(run_queue._find_checkout(str(root / "repo" / "tools" / "x")), str(root / "repo"))
            self.assertEqual(run_queue._find_checkout(str(root / "plain")), str(root / "plain"))
        self.assertEqual(run_queue.checkout_of({"checkout": "/r", "cwd": "/r/x"}), "/r")
        self.assertEqual(run_queue.checkout_of({"cwd": "/r/x"}), "/r/x")  # a ticket of an older wrapper

    def test_the_default_limit_of_a_quick_run_is_the_headless_runs(self) -> None:
        self.assertEqual(run_queue.DEFAULT_TIMEOUT_MINUTES["quick"], run_queue.DEFAULT_TIMEOUT_MINUTES["headless"])

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


class ProcessStartTests(unittest.TestCase):
    """ARC-DEV-1401: the process start time of a ticket is read without spawning `ps`."""

    def ps_start(self, pid: int) -> str:
        return subprocess.run(["ps", "-o", "lstart=", "-p", str(pid)], capture_output=True, text=True,
                              env=run_queue._PS_ENVIRONMENT).stdout.strip()

    @unittest.skipUnless(sys.platform == "darwin", "libproc is macOS only")
    def test_libproc_text_equals_ps(self) -> None:
        self.assertIsNotNone(run_queue._LIBPROC)
        self.assertEqual(run_queue._process_start(os.getpid()), self.ps_start(os.getpid()))
        child = subprocess.Popen(["sleep", "30"])
        try:
            self.assertEqual(run_queue._process_start(child.pid), self.ps_start(child.pid))
        finally:
            child.kill()
            child.wait()

    @unittest.skipUnless(sys.platform == "darwin", "libproc is macOS only")
    def test_no_process_is_spawned(self) -> None:
        with patch.object(subprocess, "run", side_effect=AssertionError("spawned")), \
                patch.object(subprocess, "Popen", side_effect=AssertionError("spawned")):
            start = run_queue._process_start(os.getpid())
            self.assertRegex(start, r"^\w{3} \w{3} [ \d]\d \d\d:\d\d:\d\d \d{4}$")
            self.assertTrue(run_queue._alive(os.getpid(), start))
            self.assertFalse(run_queue._alive(os.getpid(), "Mon Jan  1 00:00:00 2001"))  # a reused pid
            self.assertTrue(run_queue._alive(os.getpid(), run_queue.UNKNOWN))

    @unittest.skipUnless(sys.platform == "darwin", "libproc is macOS only")
    def test_gone_process_is_none(self) -> None:
        child = subprocess.Popen(["true"])
        child.wait()
        self.assertIsNone(run_queue._process_start(child.pid))
        self.assertFalse(run_queue._alive(child.pid, "Thu Nov 12 17:07:19 2026"))

    @unittest.skipUnless(sys.platform == "darwin" and os.geteuid() != 0, "libproc is macOS only; root may read launchd")
    def test_another_users_process_is_unknown_not_gone(self) -> None:
        self.assertEqual(run_queue._process_start(1), run_queue.UNKNOWN)  # launchd: root's, the kernel refuses

    def test_start_text_ignores_the_locale(self) -> None:
        self.assertEqual(run_queue._start_text(1794503239), "Thu Nov 12 17:07:19 2026")
        self.assertEqual(run_queue._start_text(1794503239 - 10 * 86400 + 3600 * 7), "Tue Nov  3 00:07:19 2026")

    def test_fallback_asks_ps_and_caches(self) -> None:
        run_queue._START_CACHE.clear()
        answer = subprocess.CompletedProcess([], 0, stdout="Thu Nov 12 17:07:19 2026\n", stderr="")
        with patch.object(run_queue, "_LIBPROC", None), patch.object(subprocess, "run", return_value=answer) as run:
            self.assertEqual(run_queue._process_start(424242), "Thu Nov 12 17:07:19 2026")
            self.assertEqual(run_queue._process_start(424242), "Thu Nov 12 17:07:19 2026")
        self.assertEqual(run.call_count, 1)
        run_queue._START_CACHE.clear()


class TaskCodeTests(unittest.TestCase):
    """ARC-DEV-1408: the task code of a run, from its label or the branch its checkout has now."""

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()

    def tearDown(self) -> None:
        self.temp.cleanup()

    def checkout(self, name: str, head: str, *, worktree: bool = False) -> str:
        path = self.root / name
        git = self.root / f"{name}-gitdir" if worktree else path / ".git"
        git.mkdir(parents=True)
        path.mkdir(exist_ok=True)
        (git / "HEAD").write_text(head + "\n")
        if worktree:  # a worktree's .git is a file naming its git folder
            (path / ".git").write_text(f"gitdir: {git}\n")
        return str(path)

    def test_the_label_wins_over_the_branch(self) -> None:
        checkout = self.checkout("a", "ref: refs/heads/ARC-DEV-1403-pause")
        self.assertEqual(run_queue.task_code({"label": "ARC-AI-1315 registry test", "checkout": checkout}), "ARC-AI-1315")

    def test_the_branch_of_a_plain_checkout_and_of_a_worktree(self) -> None:
        plain = self.checkout("plain", "ref: refs/heads/ARC-DEV-1403-pause")
        tree = self.checkout("tree", "ref: refs/heads/ARC-UI-1357-hud", worktree=True)
        self.assertEqual(run_queue.task_code({"label": "smoke stages", "checkout": plain}), "ARC-DEV-1403")
        self.assertEqual(run_queue.task_code({"label": "", "checkout": tree}), "ARC-UI-1357")
        self.assertEqual(run_queue.task_code({"checkout": tree, "cwd": str(Path(tree) / "sub")}), "ARC-UI-1357")  # a ticket of an old wrapper

    def test_no_code_when_there_is_none_or_it_cannot_be_read(self) -> None:
        self.assertIsNone(run_queue.task_code({"label": "x", "checkout": self.checkout("c", "ref: refs/heads/claude/project-thread-74wi5u")}))
        self.assertIsNone(run_queue.task_code({"label": "x", "checkout": self.checkout("d", "0123456789abcdef")}))  # detached HEAD
        self.assertIsNone(run_queue.task_code({"label": "x", "checkout": str(self.root / "missing")}))
        self.assertIsNone(run_queue.task_code({"checkout": None, "cwd": None}))
        binary = Path(self.checkout("b", "x")) / ".git" / "HEAD"
        binary.write_bytes(b"\xff\xfe\x00")  # not text: never an error in a wait loop or in ./arcanum queue
        self.assertIsNone(run_queue.task_code({"label": "x", "checkout": str(binary.parent.parent)}))
        self.assertIsNone(run_queue.task_code({"label": "x", "checkout": "/tmp/a\x00b"}))

    def test_describe_shows_the_code(self) -> None:
        checkout = self.checkout("e", "ref: refs/heads/ARC-DEV-1408-code")
        text = run_queue._describe({"seq": 3, "kind": "quick", "label": "t", "cwd": checkout, "checkout": checkout})
        self.assertEqual(text, '#3 quick e ARC-DEV-1408 "t"')


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
        (self.root / "release").write_text("go")  # a held run of a failed test ends at once
        for process in self.processes:
            if process.poll() is None:
                process.kill()
                process.wait()
        self.env.stop()
        self.temp.cleanup()

    def queued(self, kind: str, name: str, seconds: float, *, timeout: str | None = None,
               estimate: float | None = None, cwd: Path | None = None) -> subprocess.Popen:
        code = (f"import time; open({str(self.log)!r}, 'a').write('start {name} %f\\n' % time.time()); "
                f"{self.ticket_note(name)}time.sleep({seconds}); "
                f"open({str(self.log)!r}, 'a').write('end {name} %f\\n' % time.time())")
        return self.wrapper(kind, name, [sys.executable, "-c", code], timeout=timeout, estimate=estimate, cwd=cwd)

    def ticket_note(self, name: str) -> str:
        """Code for a command to log its own ticket: `turn` is when the wrapper took its place (its own start event) and
        `waited` is that minus when its ticket was queued. A timed assertion on these leaves out the process start of the
        wrapper and of the command, which took seconds on a loaded Mac (ARC-DEV-2257)."""
        ticket = str(run_queue.folder())
        return (f"import json, os; t = None\n"
                f"for _ in range(100):\n"
                f"    try: t = json.load(open(os.path.join({ticket!r}, 't-%012d.json' % int(os.environ[{run_queue.TICKET_ENV!r}]))))\n"
                f"    except (OSError, ValueError): time.sleep(0.02)\n"
                f"    else: break\n"
                f"open({str(self.log)!r}, 'a').write('turn {name} %f\\nwaited {name} %f\\n' % (t['startedAt'], t['startedAt'] - t['enqueuedAt']))\n")

    def held(self, kind: str, name: str, *, cwd: Path | None = None, environment: dict | None = None,
             estimate: float | None = None) -> subprocess.Popen:
        """A run that holds its place until `release()`, so a test's overlap and order do not depend on how fast the
        machine starts the next wrapper (a loaded Mac took seconds, which broke fixed run lengths)."""
        code = (f"import os, time; open({str(self.log)!r}, 'a').write('start {name} %f\\n' % time.time()); "
                f"{self.ticket_note(name)}"
                f"deadline = time.time() + 90\n"
                f"while not os.path.exists({str(self.root / 'release')!r}) and time.time() < deadline: time.sleep(0.05)\n"
                f"open({str(self.log)!r}, 'a').write('end {name} %f\\n' % time.time())")
        return self.wrapper(kind, name, [sys.executable, "-c", code], cwd=cwd, environment=environment, estimate=estimate)

    def release(self) -> None:
        (self.root / "release").write_text("go")

    def started(self, *names: str) -> None:
        self.until(lambda: self.log.exists() and all(f"start {name} " in self.log.read_text() for name in names), 60.0)

    def live(self) -> list[dict]:
        with run_queue._QueueLock():
            return [item for _, item in run_queue.tickets(prune=False)]

    def runs(self) -> list[dict]:
        """The live tickets of runs: the pause (a ticket the queue counts as a running export) and the playing mode are not."""
        return [item for item in self.live() if not item.get("paused") and not item.get("playing")]

    def wrapper(self, kind: str, name: str, command: list[str], *, timeout: str | None = None,
                environment: dict | None = None, estimate: float | None = None, cwd: Path | None = None) -> subprocess.Popen:
        before = len(self.live())
        argv = [sys.executable, "-B", str(ARCANUM), "run", "--kind", kind, "--label", name, "--start-within", "0",
                *(["--timeout", timeout] if timeout else []), *(["--estimate", str(estimate)] if estimate is not None else []),
                "--", *command]
        process = subprocess.Popen(argv, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, cwd=cwd,
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
        self.held("window", "a")  # held until the others are queued: a fixed length ran out on a loaded Mac (ARC-DEV-2254)
        self.started("a")
        names = ["b", "c", "d", "e"]
        for index, name in enumerate(names):
            self.queued("headless" if index % 2 else "window", name, 0.3)
        self.release()
        self.finish()
        found = self.events()
        order = sorted(["a", *names], key=lambda name: found[f"start {name}"])
        self.assertEqual(order, ["a", *names])
        for earlier, later in zip(order, order[1:]):
            gap = found[f"turn {later}"] - found[f"end {earlier}"]  # to the later wrapper taking its place, not its command's start
            self.assertGreaterEqual(gap, 0.0)
            self.assertLess(gap, 2.5)  # the poll interval, not a lock-retry back-off

    def test_a_short_run_that_arrived_later_starts_before_a_long_one_and_the_ticket_keeps_the_estimate(self) -> None:
        self.held("window", "running")
        self.started("running")
        self.queued("window", "long", 0.2, estimate=600.0)
        self.queued("window", "short", 0.2, estimate=5.0)
        self.until(lambda: len(self.live()) == 3)
        self.assertEqual(sorted(item.get("estimateSeconds") for item in self.live() if item["label"] != "running"), [5.0, 600.0])
        self.release()
        self.finish()
        found = self.events()
        self.assertLess(found["start short"], found["start long"])
        self.assertGreaterEqual(found["start short"], found["end running"])

    def test_a_quick_run_starts_while_a_godot_run_holds_the_slot(self) -> None:
        other = self.root / "other-checkout"  # a quick run of the same checkout waits for its Godot run (same project folder)
        other.mkdir()
        self.held("window", "w")
        self.started("w")
        self.queued("quick", "q", 0.2, cwd=other)
        self.until(lambda: "end q " in self.log.read_text(), 60.0)  # q ends while w still holds the slot
        self.release()
        self.finish()
        found = self.events()
        self.assertLess(found["end q"], found["end w"])

    def test_a_huge_estimate_is_capped_so_a_run_cannot_put_itself_last_for_ever(self) -> None:
        self.held("window", "running")
        self.started("running")
        self.queued("window", "huge", 0.1, estimate=1e12)
        self.until(lambda: len(self.live()) == 2)
        waiting = next(item for item in self.live() if item["label"] == "huge")
        self.assertEqual(waiting["estimateSeconds"], run_queue.MAX_ESTIMATE_SECONDS)
        self.release()
        self.finish()

    def test_an_estimate_must_not_be_negative(self) -> None:
        with self.assertRaisesRegex(ValueError, "--estimate"):
            run_queue.run("dotnet", ["true"], estimate=-1.0)

    def test_dotnet_runs_alongside_a_godot_run(self) -> None:
        self.held("window", "w")
        self.started("w")
        self.queued("dotnet", "d", 0.2)
        self.until(lambda: "end d " in self.log.read_text(), 60.0)  # d ends while w still holds the slot
        self.release()
        self.finish()
        found = self.events()
        self.assertLess(found["end d"], found["end w"])

    def test_a_third_dotnet_run_waits_for_a_free_dotnet_place(self) -> None:
        self.held("dotnet", "a")
        self.held("dotnet", "b")
        self.started("a", "b")  # two places: a and b overlap, held until released
        self.queued("dotnet", "c", 0.2)
        time.sleep(1.0)
        self.assertNotIn("start c ", self.log.read_text())  # both places are taken
        self.release()
        self.finish()
        found = self.events()
        self.assertLess(found["start b"], found["end a"])
        self.assertGreaterEqual(found["start c"], min(found["end a"], found["end b"]))

    def test_an_export_runs_quiet_after_running_dotnet_and_before_newer_dotnet(self) -> None:
        self.held("dotnet", "old")
        self.started("old")
        self.queued("export", "x", 0.5)
        self.queued("dotnet", "new", 0.2)
        self.release()
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
        self.queued("window", "next", 0.1)
        self.finish()
        self.assertLess(self.events()["waited next"], 5.0)  # queued to taking its place: the wrapper's own start is left out

    def test_a_run_out_of_time_has_its_process_group_stopped(self) -> None:
        pid_file = self.root / "bg.pid"
        process = self.wrapper("headless", "slow", ["sh", "-c", f"sleep 30 & echo $! > {pid_file}; wait"], timeout="0.01")
        self.assertEqual(process.wait(30), run_queue.TIMEOUT_EXIT)
        self.until(lambda: pid_file.exists())
        background = int(pid_file.read_text())
        self.until(lambda: subprocess.run(["ps", "-p", str(background)], capture_output=True).returncode != 0)
        self.assertEqual(run_queue.tickets(), [])

    def test_a_stopped_waiter_leaves_the_queue(self) -> None:
        self.held("window", "a")
        self.started("a")
        waiter = self.queued("window", "gone", 0.1)
        self.queued("window", "b", 0.1)
        waiter.send_signal(signal.SIGTERM)
        self.assertEqual(waiter.wait(30), 128 + signal.SIGTERM)
        self.release()
        self.finish()  # b would wait for ever behind a ticket the stopped waiter left
        found = self.events()
        self.assertNotIn("start gone", found)
        self.assertGreaterEqual(found["start b"], found["end a"])

    def test_the_lockf_rule_and_the_queue_exclude_each_other(self) -> None:
        legacy = os.environ["ARCANUM_QUEUE_LEGACY_LOCK"]
        holder = subprocess.Popen(["/usr/bin/lockf", legacy, sys.executable, "-c",
                                   f"import time; open({str(self.log)!r}, 'a').write('start lockf %f\\n' % time.time()); "
                                   f"deadline = time.time() + 90\n"
                                   f"while not __import__('os').path.exists({str(self.root / 'release')!r}) and time.time() < deadline: time.sleep(0.05)\n"
                                   f"open({str(self.log)!r}, 'a').write('end lockf %f\\n' % time.time())"])
        self.processes.append(holder)
        self.until(lambda: self.log.exists())
        self.queued("window", "q", 0.5)  # q waits for the lockf holder, which ends only on release
        self.assertNotIn("start q ", self.log.read_text())
        self.release()
        self.until(lambda: "start q" in self.log.read_text(), 60.0)
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
        self.held("window", "ru", environment={"LC_ALL": "ru_RU.UTF-8", "LANG": "ru_RU.UTF-8", "TZ": "Asia/Tokyo"})
        self.started("ru")
        with patch.dict(os.environ, {"LC_ALL": "C", "TZ": "UTC"}):
            self.assertTrue(run_queue.status()[0].startswith("running #"), run_queue.status())
        self.assertEqual(len(self.live()), 1)
        self.release()
        self.finish()

    def test_an_orphaned_godot_command_is_stopped_after_its_limit(self) -> None:
        first = self.wrapper("window", "orphan", ["sleep", "60"], timeout="0.01")
        self.until(lambda: any(item.get("childPid") for item in self.live()))
        child = next(item["childPid"] for item in self.live())
        os.kill(first.pid, signal.SIGKILL)
        first.wait()
        self.queued("window", "after", 0.1)
        self.finish()
        self.assertLess(self.events()["waited after"], run_queue.KILL_GRACE_SECONDS + 5)
        self.until(lambda: subprocess.run(["ps", "-p", str(child)], capture_output=True).returncode != 0)

    def test_numbering_survives_a_lost_counter(self) -> None:
        self.held("window", "a")
        self.started("a")
        (run_queue.folder() / "counter").write_text("")
        self.queued("window", "b", 0.1)
        self.assertEqual(sorted(item["seq"] for item in self.live()), [1, 2])
        self.release()
        self.finish()

    def folder(self, name: str) -> Path:
        path = self.root / name
        path.mkdir(exist_ok=True)
        return path

    def test_runs_of_different_checkouts_share_the_lanes_and_the_third_short_run_waits(self) -> None:
        # ARC-DEV-400: two short lanes and one heavy lane, side by side; the places of a lane are taken in order
        self.held("window", "a", cwd=self.folder("a"))
        self.started("a")
        self.held("window", "b", cwd=self.folder("b"))
        self.started("b")  # beside a, held until released
        self.queued("window", "c", 0.2, cwd=self.folder("c"))
        self.queued("window", "heavy", 0.2, cwd=self.folder("d"), estimate=500.0)
        self.until(lambda: "end heavy " in self.log.read_text(), 60.0)  # the heavy lane runs while a and b hold both short places
        self.assertNotIn("start c ", self.log.read_text())
        self.release()
        self.finish()
        found = self.events()
        self.assertLess(found["start b"], found["end a"])  # beside a
        self.assertLess(found["start heavy"], found["end a"])  # the heavy lane is its own
        self.assertGreaterEqual(found["start c"], min(found["end a"], found["end b"]))  # no third short place
        entries = [json.loads(line) for line in (run_queue.folder() / "history.jsonl").read_text().splitlines()]
        by_label = {item["label"]: item for item in entries}
        self.assertEqual(by_label["heavy"]["lane"], "heavy")
        self.assertEqual({by_label[name]["lane"] for name in "abc"}, {"short"})
        self.assertGreaterEqual(max(by_label[name]["peersStart"] for name in ("b", "heavy")), 1)

    def test_two_runs_of_one_checkout_do_not_run_at_once(self) -> None:
        folder = self.folder("same")
        self.held("window", "a", cwd=folder)
        self.started("a")
        self.queued("window", "b", 0.2, cwd=folder, estimate=500.0)
        self.release()
        self.finish()
        found = self.events()
        self.assertGreaterEqual(found["start b"], found["end a"])

    def test_the_rollback_variable_returns_to_one_godot_slot(self) -> None:
        off = {run_queue.LANES_ENV: "0"}
        self.held("window", "a", environment=off, cwd=self.folder("a"))
        self.started("a")
        self.queued("window", "b", 0.2, cwd=self.folder("b"))  # a wrapper with lanes: it still waits for the single-slot run
        self.release()
        self.finish()
        found = self.events()
        self.assertGreaterEqual(found["start b"], found["end a"])
        entries = [json.loads(line) for line in (run_queue.folder() / "history.jsonl").read_text().splitlines()]
        self.assertEqual({item["label"]: item["lane"] for item in entries}, {"a": "single", "b": "short"})

    def test_an_export_waits_for_the_running_lanes_and_runs_alone(self) -> None:
        self.held("window", "a", cwd=self.folder("a"))
        self.held("window", "heavy", cwd=self.folder("b"), estimate=500.0)
        self.started("a", "heavy")
        self.queued("export", "export", 0.3, cwd=self.folder("c"))
        self.release()
        self.finish()
        found = self.events()
        self.assertGreaterEqual(found["start export"], max(found["end a"], found["end heavy"]))

    def test_status_names_the_holder_and_the_waiters(self) -> None:
        self.held("window", "holder")
        self.started("holder")
        self.queued("window", "waiter", 0.1)
        lines = run_queue.status()
        self.assertTrue(lines[0].startswith("running #") and '"holder"' in lines[0], lines)
        self.assertTrue(lines[1].startswith("waiting #") and '"waiter"' in lines[1] and "behind #" in lines[1], lines)
        self.release()
        self.finish()
        history = run_queue.status(history=5)
        self.assertEqual(len(history), 2)
        self.assertIn("exit=0", history[0])

    def test_a_paused_queue_starts_no_waiting_run_until_resume(self) -> None:
        self.assertTrue(run_queue.set_paused(True, "test"))
        self.assertFalse(run_queue.set_paused(True, "test"))  # already set
        self.assertEqual(run_queue.pause_state()["by"], "test")
        process = self.queued("dotnet", "a", 0.1)
        self.until(lambda: len(self.runs()) == 1)
        time.sleep(1.5)  # several polls: it neither starts nor leaves
        self.assertIsNone(process.poll())
        self.assertFalse(self.log.exists())
        self.assertIsNone(self.runs()[0]["startedAt"])
        lines = run_queue.status()
        self.assertTrue(lines[0].startswith("PAUSED") and "--resume" in lines[0], lines)
        self.assertTrue(lines[1].startswith("waiting #") and lines[1].endswith("(paused)"), lines)
        self.assertTrue(run_queue.set_paused(False))
        self.assertFalse(run_queue.set_paused(False))
        self.finish()
        self.assertIn("end a", self.events())
        self.assertFalse(any(line.startswith("PAUSED") for line in run_queue.status()))

    def test_pause_lets_the_running_run_finish_and_holds_the_next(self) -> None:
        self.held("window", "a")
        self.started("a")
        run_queue.set_paused(True, "test")
        self.queued("window", "b", 0.1)
        self.until(lambda: any(item.get("startedAt") is None for item in self.runs()))
        self.release()
        self.until(lambda: "end a" in self.log.read_text())
        time.sleep(1.5)  # a gap far over the poll: b still waits for the resume
        self.assertNotIn("start b", self.log.read_text())
        run_queue.set_paused(False)
        self.finish()
        found = self.events()
        self.assertGreaterEqual(found["start b"], found["end a"])

    def test_the_pause_is_a_held_ticket_no_wrapper_can_read(self) -> None:
        # the contract that makes the pause bind wrappers of every version (ARC-DEV-033 counts a held unreadable ticket as a
        # running export): the file is not a ticket of this schema, it is locked, and the queue lists it as a running export
        run_queue.set_paused(True, "test")
        try:
            path = run_queue.pause_file()
            self.assertIsNone(run_queue._read_json(path))
            self.assertTrue(run_queue._locked(path))
            with run_queue._QueueLock():
                live = [item for _, item in run_queue.tickets(prune=True)]
            self.assertEqual([(item["kind"], item.get("paused")) for item in live], [("export", True)])
            self.assertIn("PAUSED", run_queue._describe(live[0]))
            other = {"seq": 7, "kind": "quick", "enqueuedAt": time.time(), "estimateSeconds": 15}
            self.assertEqual(run_queue.blocker(other, [*live, other])["paused"], True)
        finally:
            run_queue.set_paused(False)
        self.assertFalse(run_queue.pause_file().exists())

    def test_a_pause_nobody_holds_is_stale_and_does_not_pause(self) -> None:
        run_queue._open_folder()
        run_queue.pause_file().write_text('{"schema": "x"}')
        self.assertIsNone(run_queue.pause_state())
        with run_queue._QueueLock():
            self.assertEqual(run_queue.tickets(prune=True), [])
        self.assertFalse(run_queue.pause_file().exists())  # pruned like any dead ticket

    def test_a_nested_run_is_not_held_by_the_pause(self) -> None:
        inner = (f"while [ ! -e {self.root / 'release'} ]; do sleep 0.05; done; "
                 f"{sys.executable} -B {ARCANUM} run --kind window -- {sys.executable} -c pass")  # the nested run starts only on release
        process = self.wrapper("window", "outer", ["sh", "-c", inner])
        self.until(lambda: len(self.runs()) == 1 and self.runs()[0].get("startedAt"))
        run_queue.set_paused(True, "test")
        self.release()
        try:
            self.assertEqual(process.wait(30), 0)  # a run holding a place keeps working: its nested run is not queued
        finally:
            run_queue.set_paused(False)

    # ARC-DEV-2272: the owner-is-playing mode with real wrappers
    def test_playing_holds_window_runs_while_headless_quick_and_dotnet_runs_go_on(self) -> None:
        self.assertTrue(run_queue.set_playing(True, 60.0, "test"))
        try:
            self.assertEqual(run_queue.playing_state()["by"], "test")
            window = self.queued("window", "w", 0.1)
            self.until(lambda: len(self.runs()) == 1)
            for kind in ("headless", "quick", "dotnet"):
                self.queued(kind, kind[0], 0.1)
            self.started("h", "q", "d")  # the runs without a window start and run, with the window run queued first
            time.sleep(1.5)  # several polls: the window run neither starts nor leaves
            self.assertNotIn("start w ", self.log.read_text())
            self.assertIsNone(window.poll())
            lines = run_queue.status()
            self.assertTrue(lines[0].startswith("PLAYING") and "--not-playing" in lines[0], lines)
            self.assertTrue(any(line.startswith("waiting #") and line.endswith("(owner playing)") for line in lines), lines)
        finally:
            self.assertTrue(run_queue.set_playing(False))
        self.finish()
        self.assertIn("end w", self.events())
        self.assertFalse(run_queue.playing_file().exists())
        self.assertFalse(any(line.startswith("PLAYING") for line in run_queue.status()))

    def test_a_waiting_window_run_says_the_owner_is_playing_and_starts_when_the_mode_ends(self) -> None:
        run_queue.set_playing(True, 60.0, "test")
        code = f"open({str(self.log)!r}, 'a').write('start w 0\\n')"
        process = subprocess.Popen([sys.executable, "-B", str(ARCANUM), "run", "--kind", "window", "--start-within", "0", "--",
                                    sys.executable, "-c", code], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.processes.append(process)
        self.until(lambda: len(self.runs()) == 1)
        time.sleep(1.0)
        self.assertIsNone(process.poll())
        run_queue.set_playing(False)
        _, stderr = process.communicate(timeout=60)
        self.assertEqual(process.returncode, 0, stderr)
        self.assertIn("waiting-for=owner playing", stderr)
        self.assertIn("./arcanum queue --not-playing", stderr)
        self.assertIn("start w", self.log.read_text())

    def test_the_mode_ends_by_itself_at_its_limit(self) -> None:
        run_queue.set_playing(True, 0.04, "test")  # 2.4 s
        state = run_queue.playing_state()
        process = self.queued("window", "w", 0.1)
        self.finish()  # starts once the limit has passed, nobody ended the mode
        self.assertEqual(process.returncode, 0)
        self.assertIn("end w", self.events())
        self.assertGreaterEqual(self.events()["start w"], state["until"] - 1.0)  # not before the limit
        self.assertIsNone(run_queue.playing_state())

    def test_a_window_run_already_holding_its_place_finishes_when_the_mode_starts(self) -> None:
        running = self.held("window", "a")
        self.started("a")
        run_queue.set_playing(True, 60.0, "test")
        try:
            self.release()
            self.assertEqual(running.wait(30), 0)
        finally:
            run_queue.set_playing(False)

    def test_playing_again_renews_the_limit_and_ending_it_twice_is_harmless(self) -> None:
        run_queue.set_playing(True, 10.0, "test")
        first = run_queue.playing_state()["until"]
        self.assertTrue(run_queue.set_playing(True, 0, "test"))  # no limit
        try:
            state = run_queue.playing_state()
            self.assertIsNone(state["until"])
            self.assertGreater(first, state["since"])
            self.assertIn("no time limit", run_queue.playing_text(state))
        finally:
            self.assertTrue(run_queue.set_playing(False))
        self.assertFalse(run_queue.set_playing(False))
        with self.assertRaises(ValueError):
            run_queue.set_playing(True, -1)

    def test_a_playing_file_nobody_holds_is_stale(self) -> None:
        run_queue._open_folder()
        run_queue.playing_file().write_text(json.dumps({"schema": run_queue.SCHEMA, "seq": 0, "kind": "window", "playing": True}))
        self.assertIsNone(run_queue.playing_state())
        with run_queue._QueueLock():
            self.assertEqual(run_queue.tickets(prune=True), [])
        self.assertFalse(run_queue.playing_file().exists())

    def test_the_command_turns_the_mode_on_and_off_and_refuses_bad_combinations(self) -> None:
        def run(*args: str) -> subprocess.CompletedProcess:
            return subprocess.run([sys.executable, "-B", str(ARCANUM), "queue", *args], capture_output=True, text=True,
                                  cwd=REPO, timeout=60)
        try:
            on = run("--playing", "--for", "30m")
            self.assertEqual(on.returncode, 0, on.stderr)
            self.assertIn("no window run starts", on.stdout)
            self.assertRegex(on.stdout, r"ends in (30m00|29m5\d)s")
            self.assertIn("PLAYING", on.stdout)  # the queue listing follows
            state = run_queue.playing_state()
            self.assertAlmostEqual(state["until"] - state["since"], 1800, delta=2)
        finally:
            off = run("--not-playing")
        self.assertEqual(off.returncode, 0, off.stderr)
        self.assertIn("window runs start again", off.stdout)
        self.assertIsNone(run_queue.playing_state())
        self.assertIn("was not playing", run("--not-playing").stdout)
        self.assertEqual(run("--for", "1h").returncode, 2)  # --for goes with --playing
        self.assertEqual(run("--not-playing", "--for", "1h").returncode, 2)
        self.assertEqual(run("--pause", "--for", "5").returncode, 2)
        self.assertFalse(run_queue.pause_file().exists())  # refused before the pause was set
        self.assertEqual(run("--wait", "--playing").returncode, 2)
        self.assertNotEqual(run("--playing", "--pause").returncode, 0)  # one of the two
        self.assertNotEqual(run("--playing", "--for", "soon").returncode, 0)
        self.assertIsNone(run_queue.playing_state())
        self.assertFalse(run_queue.pause_file().exists())

    def test_wait_ignores_the_playing_mode(self) -> None:
        run_queue.set_playing(True, 60.0, "test")
        try:
            done = subprocess.run([sys.executable, "-B", str(ARCANUM), "queue", "--wait"], capture_output=True, text=True,
                                  cwd=REPO, timeout=60)
            self.assertEqual((done.returncode, done.stdout.strip()), (0, "queue empty"))
        finally:
            run_queue.set_playing(False)

    def waiter(self, *args: str) -> subprocess.Popen:
        return subprocess.Popen([sys.executable, "-B", str(ARCANUM), "queue", *args], stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, text=True, cwd=REPO)

    def test_wait_blocks_until_the_run_ends_and_exits_with_its_status(self) -> None:  # ARC-DEV-1983
        code = (f"import os, sys, time\n"
                f"while not os.path.exists({str(self.root / 'release')!r}): time.sleep(0.05)\n"
                f"sys.exit(3)")
        self.wrapper("window", "exits-three", [sys.executable, "-c", code])
        seq = self.runs()[0]["seq"]
        waiting = self.waiter("--wait", str(seq))
        self.processes.append(waiting)
        time.sleep(1.5)
        self.assertIsNone(waiting.poll(), "the waiter returned while the run was still going")
        self.release()
        out, err = waiting.communicate(timeout=30)
        self.assertEqual(waiting.returncode, 3, out + err)
        self.assertIn("FAIL exit=3", out)
        self.assertIn(f"ticket={seq}", out)
        self.assertIn("log=", out)
        again = self.waiter("--wait", str(seq))  # a finished run answers from the history at once
        self.processes.append(again)
        out, _ = again.communicate(timeout=30)
        self.assertEqual(again.returncode, 3, out)

    def test_wait_for_a_passing_run_prints_pass_and_exits_zero(self) -> None:
        run = self.held("quick", "passes")
        seq = self.runs()[0]["seq"]
        threading.Timer(1.0, self.release).start()
        done = subprocess.run([sys.executable, "-B", str(ARCANUM), "queue", "--wait", str(seq)], capture_output=True, text=True,
                              cwd=REPO, timeout=60)
        run.wait(30)
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        self.assertTrue(done.stdout.startswith("PASS exit=0"), done.stdout)

    def test_wait_without_an_id_waits_until_the_queue_is_empty(self) -> None:
        self.held("window", "a")
        self.held("dotnet", "b")
        waiting = self.waiter("--wait")
        self.processes.append(waiting)
        time.sleep(1.5)
        self.assertIsNone(waiting.poll())
        self.release()
        out, err = waiting.communicate(timeout=30)
        self.assertEqual(waiting.returncode, 0, out + err)
        self.assertIn("queue empty", out)
        self.assertEqual(self.runs(), [])
        done = subprocess.run([sys.executable, "-B", str(ARCANUM), "queue", "--all"], capture_output=True, text=True,
                              cwd=REPO, timeout=60)
        self.assertEqual((done.returncode, done.stdout.strip()), (0, "queue empty"))

    def test_wait_ignores_the_pause(self) -> None:
        run_queue.set_paused(True, "test")
        try:
            done = subprocess.run([sys.executable, "-B", str(ARCANUM), "queue", "--wait"], capture_output=True, text=True,
                                  cwd=REPO, timeout=60)
            self.assertEqual((done.returncode, done.stdout.strip()), (0, "queue empty"))
        finally:
            run_queue.set_paused(False)

    def test_wait_times_out_with_its_own_exit_code_and_leaves_the_run_alone(self) -> None:
        run = self.held("window", "slow")
        seq = self.runs()[0]["seq"]
        done = subprocess.run([sys.executable, "-B", str(ARCANUM), "queue", "--wait", str(seq), "--timeout", "0.01"],
                              capture_output=True, text=True, cwd=REPO, timeout=60)
        self.assertEqual(done.returncode, run_queue.WAIT_TIMEOUT_EXIT, done.stdout + done.stderr)
        self.assertIn("TIMEOUT", done.stdout)
        self.assertNotIn(done.returncode, (0, run_queue.TIMEOUT_EXIT, run_queue.WAIT_UNKNOWN_EXIT))
        self.assertIsNone(run.poll())  # the run is not stopped
        self.assertEqual([item["seq"] for item in self.runs()], [seq])
        self.release()
        self.assertEqual(run.wait(30), 0)

    def test_wait_for_an_unknown_run_and_bad_arguments(self) -> None:
        def run(*args: str) -> subprocess.CompletedProcess:
            return subprocess.run([sys.executable, "-B", str(ARCANUM), "queue", *args], capture_output=True, text=True,
                                  cwd=REPO, timeout=60)
        self.assertEqual(run("--wait", "424242").returncode, run_queue.WAIT_UNKNOWN_EXIT)
        self.assertNotEqual(run("--wait", "abc").returncode, 0)
        self.assertNotEqual(run("--wait", "0").returncode, 0)
        self.assertEqual(run("--timeout", "5").returncode, 2)  # --timeout without --wait
        self.assertEqual(run("--wait", "--pause").returncode, 2)
        self.assertEqual(run("--all", "--wait", "5").returncode, 2)
        self.assertFalse(run_queue.pause_file().exists())

    def test_a_waiter_spawns_no_process_and_is_refused_inside_a_run(self) -> None:
        self.held("window", "watched")
        seq = self.runs()[0]["seq"]
        with patch.object(subprocess, "run", side_effect=AssertionError("spawned")), \
                patch.object(subprocess, "Popen", side_effect=AssertionError("spawned")), \
                patch("builtins.print"):
            self.assertEqual(run_queue.wait(seq, 0.0001, sleep=lambda _: None), run_queue.WAIT_TIMEOUT_EXIT)
        with patch.dict(os.environ, {run_queue.TICKET_ENV: str(seq)}):  # inside a queued run: refused, any target
            for target in (seq, None, seq + 1):
                with self.assertRaises(ValueError):
                    run_queue.wait(target)


if __name__ == "__main__":
    unittest.main()
