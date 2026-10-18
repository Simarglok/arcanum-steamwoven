"""Synthetic ARC-RES-013 worksheet; not source code or a production movement API.

Run: python3 -B tools/research/grid_movement_probe.py --self-test
Exact fractions, fixture-only eight-unit steps, no devices/assets/engine imports.
Literal oracles below are separate from the event evaluator. This checks the
specified examples, not original-runtime behavior or real traversal admission.
"""
import argparse
from fractions import Fraction as F
from itertools import groupby


class Worksheet:
    """Small event evaluator restricted to immutable synthetic Open/Unknown context."""

    def __init__(self, origin=(0, 0), coverage="Open", duration: int | F | float = 8):
        if not isinstance(duration, (int, F)) or duration <= 0:
            raise ValueError("invalid duration")
        self.now = F(0)
        self.cell = origin
        self.anchor = tuple(map(F, origin))
        self.phase = "Settled"
        self.intent = None
        self.direction = (0, 0)
        self.start = F(0)
        self.from_cell = origin
        self.duration = F(duration)
        self.return_at = F(0)
        self.return_progress = F(0)
        self.end = F(0)
        self.events = []
        self.coverage = coverage
        self.sequence = -1

    def event(self, time, name, cell=None):
        self.events.append((F(time), name, self.cell if cell is None else cell))

    def advance(self, target, inputs=()):
        # Validate the entire request before mutating even the clock/intent.
        if not isinstance(target, (int, F)) or target < self.now:
            raise ValueError("invalid time")
        previous_key = (self.now, self.sequence)
        for time, sequence, direction in inputs:
            if not isinstance(time, (int, F)) or not self.now <= time <= target:
                raise ValueError("invalid event time")
            if (time, sequence) <= previous_key:
                raise ValueError("unordered event")
            if direction is not None and direction not in (
                (-1, -1), (-1, 0), (-1, 1), (0, -1),
                (0, 1), (1, -1), (1, 0), (1, 1),
            ):
                raise ValueError("invalid direction")
            previous_key = (time, sequence)
        batches = {F(t): list(g) for t, g in groupby(inputs, lambda e: e[0])}
        while True:
            candidates = [F(target)] + [t for t in batches if t >= self.now]
            if self.phase != "Settled":
                candidates.append(self.end)
                if self.phase == "OutboundBeforeEntry":
                    candidates.append(self.start + self.duration / 2)
            time = min(t for t in candidates if t >= self.now)
            arrived = False
            if self.phase != "Settled":
                if self.phase == "Returning":
                    progress = self.return_progress - (time - self.return_at) / self.duration
                else:
                    progress = (time - self.start) / self.duration
                self.anchor = tuple(F(c) + progress * d for c, d in zip(self.from_cell, self.direction))
                if self.phase == "OutboundBeforeEntry" and progress >= F(1, 2):
                    self.cell = tuple(c + d for c, d in zip(self.from_cell, self.direction))
                    self.phase = "OutboundAfterEntry"
                    self.event(time, "Commit")
                if time == self.end:
                    self.anchor = tuple(map(F, self.cell))
                    self.phase = "Settled"
                    self.event(time, "Arrive")
                    arrived = True
            self.now = time
            changed = False
            for _, sequence, direction in batches.pop(time, []):
                changed |= self.intent != direction
                self.intent = direction
                self.sequence = sequence
                if direction is None and self.phase == "OutboundBeforeEntry":
                    self.return_progress = (time - self.start) / self.duration
                    self.return_at = time
                    self.end = time + self.return_progress * self.duration
                    self.event(time, "Cancel")
                    self.phase = "Returning"
                    if self.end == time:
                        self.phase = "Settled"
                        self.event(time, "Arrive")
                        arrived = True
            if self.phase == "Settled" and self.intent is not None and (arrived or changed):
                if self.coverage != "Open":
                    self.event(time, "Reject" + self.coverage)
                else:
                    self.from_cell = self.cell
                    self.direction = self.intent
                    self.start = time
                    self.end = time + self.duration
                    self.phase = "OutboundBeforeEntry"
                    self.event(time, "Start")
            if time == target:
                break
        return self


def snapshot(w):
    return w.now, w.cell, w.anchor, w.phase, w.intent, tuple(w.events)


def self_test():
    checks = 0
    # Literal anchors, independent of Worksheet's geometry/neighbor constants.
    rows = [
        ((1, 0), (F(1, 4), 0), (F(1, 2), 0), (F(3, 4), 0)),
        ((-1, 0), (F(-1, 4), 0), (F(-1, 2), 0), (F(-3, 4), 0)),
        ((0, 1), (0, F(1, 4)), (0, F(1, 2)), (0, F(3, 4))),
        ((0, -1), (0, F(-1, 4)), (0, F(-1, 2)), (0, F(-3, 4))),
        ((1, 1), (F(1, 4), F(1, 4)), (F(1, 2), F(1, 2)), (F(3, 4), F(3, 4))),
        ((1, -1), (F(1, 4), F(-1, 4)), (F(1, 2), F(-1, 2)), (F(3, 4), F(-3, 4))),
        ((-1, 1), (F(-1, 4), F(1, 4)), (F(-1, 2), F(1, 2)), (F(-3, 4), F(3, 4))),
        ((-1, -1), (F(-1, 4), F(-1, 4)), (F(-1, 2), F(-1, 2)), (F(-3, 4), F(-3, 4))),
    ]
    for direction, before, tie, after in rows:
        for release, anchor in ((2, before), (4, tie), (6, after)):
            w = Worksheet().advance(release, [(0, 0, direction), (release, 1, None)])
            assert w.anchor == anchor
            assert w.cell == ((0, 0) if release == 2 else direction)
            assert w.phase == ("Returning" if release == 2 else "OutboundAfterEntry")
            w.advance(8)
            assert w.phase == "Settled" and w.anchor == w.cell
            commits = [e for e in w.events if e[1] == "Commit"]
            assert commits == ([] if release == 2 else [(F(4), "Commit", direction)])
            checks += 1
    # Negative chart origin, direction toward zero. Generic floor gives wrong owner.
    w = Worksheet((-2, -3)).advance(4, [(0, 0, (1, 1)), (4, 1, None)])
    assert w.anchor == (F(-3, 2), F(-5, 2)) and w.cell == (-1, -2)
    checks += 1
    # Literal transcripts: no oracle calls evaluator helpers to derive expected events.
    cases = [
        ("start-release", [(0, 0, (1, 0)), (0, 1, None)], 8,
         [(8, "Settled", (0, 0))], []),
        ("zero-distance-release", [(0, 0, (1, 0)), (0, 1, None)], 0,
         [(0, "Settled", (0, 0))], []),
        ("arrival-release", [(0, 0, (1, 0)), (8, 1, None)], 16,
         [(16, "Settled", (1, 0))], [(0, "Start", (0, 0)), (4, "Commit", (1, 0)), (8, "Arrive", (1, 0))]),
        ("repeated-held-release", [(0, 0, (1, 0)), (1, 1, (1, 0)), (2, 2, None), (3, 3, None)], 8,
         [(8, "Settled", (0, 0))], [(0, "Start", (0, 0)), (2, "Cancel", (0, 0)), (4, "Arrive", (0, 0))]),
        ("return-reinput", [(0, 0, (1, 0)), (2, 1, None), (3, 2, (0, 1)), (12, 3, None)], 12,
         [(12, "Settled", (0, 1))], [(0, "Start", (0, 0)), (2, "Cancel", (0, 0)), (4, "Arrive", (0, 0)), (4, "Start", (0, 0)), (8, "Commit", (0, 1)), (12, "Arrive", (0, 1))]),
        ("return-reinput-neutral", [(0, 0, (1, 0)), (2, 1, None), (3, 2, (0, 1)), (3, 3, None)], 8,
         [(8, "Settled", (0, 0))], [(0, "Start", (0, 0)), (2, "Cancel", (0, 0)), (4, "Arrive", (0, 0))]),
        ("opposite-latest-turn", [(0, 0, (1, 0)), (2, 1, (0, 1)), (3, 2, (-1, 0)), (16, 3, None)], 16,
         [(16, "Settled", (0, 0))], [(0, "Start", (0, 0)), (4, "Commit", (1, 0)), (8, "Arrive", (1, 0)), (8, "Start", (1, 0)), (12, "Commit", (0, 0)), (16, "Arrive", (0, 0))]),
        ("finish-reinput", [(0, 0, (1, 0)), (4, 1, None), (6, 2, (0, 1)), (16, 3, None)], 16,
         [(16, "Settled", (1, 1))], [(0, "Start", (0, 0)), (4, "Commit", (1, 0)), (8, "Arrive", (1, 0)), (8, "Start", (1, 0)), (12, "Commit", (1, 1)), (16, "Arrive", (1, 1))]),
        ("finish-reinput-neutral", [(0, 0, (1, 0)), (6, 1, None), (7, 2, (0, 1)), (7, 3, None)], 16,
         [(16, "Settled", (1, 0))], [(0, "Start", (0, 0)), (4, "Commit", (1, 0)), (8, "Arrive", (1, 0))]),
        ("release-press-same-time", [(0, 0, (1, 0)), (2, 1, None), (2, 2, (0, 1)), (12, 3, None)], 12,
         [(12, "Settled", (0, 1))], [(0, "Start", (0, 0)), (2, "Cancel", (0, 0)), (4, "Arrive", (0, 0)), (4, "Start", (0, 0)), (8, "Commit", (0, 1)), (12, "Arrive", (0, 1))]),
        ("press-release-same-time", [(0, 0, (1, 0)), (2, 1, (0, 1)), (2, 2, None)], 12,
         [(12, "Settled", (0, 0))], [(0, "Start", (0, 0)), (2, "Cancel", (0, 0)), (4, "Arrive", (0, 0))]),
        ("continuous-multi-step", [(0, 0, (1, 0)), (24, 1, None)], 24,
         [(24, "Settled", (3, 0))], [(0, "Start", (0, 0)), (4, "Commit", (1, 0)), (8, "Arrive", (1, 0)), (8, "Start", (1, 0)), (12, "Commit", (2, 0)), (16, "Arrive", (2, 0)), (16, "Start", (2, 0)), (20, "Commit", (3, 0)), (24, "Arrive", (3, 0))]),
    ]
    for name, inputs, end, expected_state, expected_events in cases:
        w = Worksheet().advance(end, inputs)
        assert [(w.now, w.phase, w.cell)] == expected_state, name
        assert w.events == expected_events, (name, w.events)
        # Split at EVERY integer time, including equality and arrival batches.
        split = Worksheet()
        for t in range(end + 1):
            split.advance(t, [e for e in inputs if e[0] == t])
        assert snapshot(split) == snapshot(w), name
        checks += 1
    w = Worksheet().advance(0, [(0, 0, (1, 0))])
    w.advance(0, [(0, 1, None)])
    assert w.phase == "Settled" and w.anchor == (0, 0)
    assert w.events == [(0, "Start", (0, 0)), (0, "Cancel", (0, 0)), (0, "Arrive", (0, 0))]
    checks += 1
    w = Worksheet().advance(2, [(0, 0, (1, 0)), (2, 1, None)])
    assert w.anchor == (F(1, 4), 0)
    w.advance(3)
    assert w.anchor == (F(1, 8), 0)  # Return duration 2, not a new duration 8.
    checks += 1
    for bad_target, events in [(-1, []), (float("nan"), []), (float("inf"), []),
                               (2, [(3, 0, None)]), (2, [(1, 2, None), (1, 1, None)]),
                               (2, [(0, 0, (2, 0))])]:
        w = Worksheet()
        old = snapshot(w)
        try:
            w.advance(bad_target, events)
        except ValueError:
            pass
        else:
            raise AssertionError("invalid request accepted")
        assert snapshot(w) == old
        checks += 1
    w = Worksheet().advance(2)
    old = snapshot(w)
    try:
        w.advance(1)
    except ValueError:
        pass
    else:
        raise AssertionError("nonmonotonic request accepted")
    assert snapshot(w) == old
    checks += 1
    for coverage in ("Unknown", "Unsupported", "Blocked"):
        w = Worksheet(coverage=coverage).advance(8, [(0, 0, (1, 1))])
        assert w.cell == (0, 0) and w.anchor == (0, 0) and w.phase == "Settled"
        assert w.events == [(0, "Reject" + coverage, (0, 0))]
        checks += 1
    for duration in (0, -1, float("nan"), float("inf")):
        try:
            Worksheet(duration=duration)
        except ValueError:
            pass
        else:
            raise AssertionError("invalid duration accepted")
        checks += 1
    print(f"PASS: {checks} synthetic geometry/timeline/rejection cases; literal oracles and frame partitions")
    print("Return worksheet: release t=2, anchor=(1/4,0); t=3 anchor=(1/8,0); settled t=4")
    print("NOT_VERIFIED: original runtime, actual passability, source speed, UX comfort; no production API")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true", required=True)
    parser.parse_args()
    self_test()
