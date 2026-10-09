"""ARC-RES-020 synthetic table conformance, NOT a production clock or CE port.

Literal expectations live in the adjacent JSON. This independent, stateless
relation checker reads no original resources or production helper constants.
It checks one bounded supplied row at a time; no world authority, timer, event
queue, persistence, mutable consumer or reusable clock service is implemented.
Python 3.10+, standard library only. Run from root with --self-test.
"""
import argparse
import copy
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from typing import Any


MAX_I64 = 9223372036854775807
PIN = "a7ff41b300ef712f0e7d088183a3d08957110cdd"
POLICY = "steamwoven-normalized-clock-v1"
FIXTURE = Path(__file__).with_name("game_clock_policy_cases.json")
DOCUMENT = Path(__file__).resolve().parents[2] / "docs/world/game-clock-policy.md"
PAUSES = {"None", "User", "Menu", "TurnBased"}
REQUIRED = frozenset("""
boundary-dawn-before boundary-dawn-at boundary-dawn-after
boundary-dusk-before boundary-dusk-at boundary-dusk-after
boundary-midnight-before boundary-midnight-at boundary-midnight-after
calendar-0 calendar-29 calendar-30 calendar-359 calendar-360 calendar-361
raw-equality raw-overday carry-equal carry-after
start-missing-file start-missing-key start-static start-zero start-negative start-upper
boot-missing boot-zero boot-hour24 init
admit-4 admit-5 admit-6 admit-249 admit-250 admit-251 admit-tb admit-closed
admit-closed-tb admit-reference gate-nested gate-lower gate-upper gate-options gate-menu
delay-zero cadence-repeat due-equal due-future
query query-detached zero paused-zero paused-positive negative negative-paused
advance-one advance-multiday pause pause-repeat pause-reason resume
jump-forward jump-backward jump-paused jump-equal jump-negative
time-edge time-overflow revision-noop revision-overflow generation-overflow generation-advance
restore-same restore-different restore-profile restore-schema restore-invalid
restore-import-counters restore-max-revision foreign-profile stale-generation stale-revision
invalid-schema invalid-state invalid-reason invalid-duration
consumer-initial consumer-repeat consumer-resume consumer-jump consumer-restore
consumer-version consumer-profile consumer-generation consumer-revision consumer-invalid
""".split())
# Fixed coverage anchors prevent renaming an unrelated example into a required
# before/at/after or saturation case. They do not supply expected outputs.
ANCHORS = {
    "boundary-dawn-before": [21599999, 1885],
    "boundary-dawn-at": [21600000, 1885],
    "boundary-dawn-after": [21600001, 1885],
    "boundary-dusk-before": [64799999, 1885],
    "boundary-dusk-at": [64800000, 1885],
    "boundary-dusk-after": [64800001, 1885],
    "boundary-midnight-before": [86399999, 1885],
    "boundary-midnight-at": [86400000, 1885],
    "boundary-midnight-after": [86400001, 1885],
    **{f"calendar-{day}": [day, 1885] for day in (0, 29, 30, 359, 360, 361)},
    **{f"admit-{delta}": [0, delta, 0, False] for delta in (4, 5, 6, 249, 250, 251)},
    "admit-tb": [0, 5, 0, True], "admit-closed": [0, 5, 1, False],
    "admit-closed-tb": [0, 5, 1, True], "admit-reference": [0, [4, 5, 256]],
    "gate-nested": [0, ["inc", "inc", "dec", "dec"]],
    "gate-lower": [0, ["dec"]], "gate-upper": [29, ["inc", "inc"]],
    "gate-options": [1, ["options", "close"]], "gate-menu": [0, ["menu", "close"]],
    "raw-equality": [0, 86400000], "raw-overday": [2, 172800001],
    "carry-equal": [0, 86399999, 1], "carry-after": [0, 86399999, 2],
    "delay-zero": [21600000, 0], "cadence-repeat": [21600001, 3600000],
    "due-equal": [21600000, 21600000, 5, 1],
    "due-future": [21600000, 21600001, 5, 1],
    "start-missing-file": [False, None, None, 1900, 36000000],
    "start-missing-key": [True, None, None, 1900, 72000000],
    "start-static": [True, None, None, 1885, 36000000],
    "start-zero": [True, 1885, 0, 1900, 36000000],
    "start-negative": [True, -1, -1, 1900, 72000000],
    "start-upper": [True, 1885, 24, 1900, 36000000],
    "boot-missing": [1885, None], "boot-zero": [1885, 0],
    "boot-hour24": [1885, 24], "init": [1885, 15],
}
SOURCE_REFS = {
    "calendar": {"calendar"}, "raw": {"wire"}, "carry": {"calendar"},
    "startup": {"startup", "static-start", "init"},
    "admission": {"admission"}, "references": {"admission"},
    "gate": {"admission", "menu-open", "menu-close"},
    "delay": {"calendar", "ambient-callback"}, "due": {"admission"},
}
PROPOSAL_REFS = {
    "normalized": {"P1"}, "bootstrap": {"P1", "P2"},
    "transition": {"P2"}, "detached": {"P2"}, "consumer": {"P3"},
}


# Complete P2 coverage inputs: [op,arg,before timestamp,pause,generation,revision].
# These identify scenarios, not their expected-result oracle.
TRANSITION_ANCHORS = {
    "query": ["Query", None, 54000000, "None", 1, 0],
    "zero": ["Advance", 0, 54000000, "None", 1, 0],
    "paused-zero": ["Advance", 0, 54000000, "Menu", 1, 1],
    "paused-positive": ["Advance", 100, 54000000, "Menu", 1, 1],
    "negative": ["Advance", -1, 54000000, "None", 1, 0],
    "negative-paused": ["Advance", -1, 54000000, "Menu", 1, 1],
    "advance-one": ["Advance", 1, 86399999, "None", 1, 0],
    "advance-multiday": ["Advance", 172800000, 0, "None", 1, 0],
    "pause": ["Pause", "Menu", 54000000, "None", 1, 0],
    "pause-repeat": ["Pause", "Menu", 54000000, "Menu", 1, 1],
    "pause-reason": ["Pause", "TurnBased", 54000000, "Menu", 1, 1],
    "resume": ["Pause", "None", 54000000, "Menu", 1, 1],
    "jump-forward": ["Jump", 172800000, 0, "None", 1, 0],
    "jump-backward": ["Jump", 0, 54000000, "None", 1, 0],
    "jump-paused": ["Jump", 0, 54000000, "Menu", 1, 1],
    "jump-equal": ["Jump", 54000000, 54000000, "None", 1, 0],
    "jump-negative": ["Jump", -1, 54000000, "None", 1, 0],
    "time-edge": ["Advance", 1, MAX_I64 - 1, "None", 1, 0],
    "time-overflow": ["Advance", 1, MAX_I64, "None", 1, 0],
    "revision-noop": ["Advance", 0, 0, "None", 1, MAX_I64],
    "revision-overflow": ["Pause", "User", 0, "None", 1, MAX_I64],
    "generation-overflow": ["Restore", [1, "A", 0, "None"], 0, "None", MAX_I64, 0],
    "generation-advance": ["Advance", 1, 0, "None", MAX_I64, 0],
    "restore-same": ["Restore", [1, "A", 54000000, "None"], 54000000, "None", 7, 9],
    "restore-different": ["Restore", [1, "A", 0, "User"], 54000000, "None", 7, 9],
    "restore-profile": ["Restore", [1, "B", 0, "None"], 54000000, "None", 7, 9],
    "restore-schema": ["Restore", [2, "A", 0, "None"], 54000000, "None", 7, 9],
    "restore-invalid": ["Restore", [1, "A", -1, "None"], 54000000, "None", 7, 9],
    "restore-import-counters": ["Restore", [1, "A", 0, "None", 7, 0], 54000000, "None", 7, 9],
    "restore-max-revision": ["Restore", [1, "A", 0, "None"], 0, "None", 7, MAX_I64],
    "foreign-profile": ["Advance", 0, 0, "None", 8, 0],
    "stale-generation": ["Restore", [1, "A", 0, "None"], 0, "None", 8, 0],
    "stale-revision": ["Advance", 0, 0, "None", 8, 1],
    "invalid-schema": ["Query", None, 0, "None", 8, 0],
    "invalid-state": ["Query", None, 0, "AlienPause", 8, 0],
    "invalid-reason": ["Advance", 0, 0, "Menu", 8, 0],
    "invalid-duration": ["Advance", "1", 0, "Menu", 8, 0],
}


def check_coverage(row):
    case_id, kind, value = row["id"], row["kind"], row["input"]
    if case_id in ANCHORS:
        require(equal(value, ANCHORS[case_id]), "coverage-anchor")
        expected_kind = ("normalized" if case_id.startswith("boundary-") else
                         "calendar" if case_id.startswith("calendar-") else
                         "admission" if case_id.startswith("admit-") and case_id != "admit-reference" else
                         "references" if case_id == "admit-reference" else
                         "bootstrap" if case_id.startswith("boot-") or case_id == "init" else
                         "startup" if case_id.startswith("start-") else
                         "gate" if case_id.startswith("gate-") else
                         "raw" if case_id.startswith("raw-") else
                         "carry" if case_id.startswith("carry-") else
                         "due" if case_id.startswith("due-") else "delay")
        require(kind == expected_kind, "coverage-kind")
    if case_id in TRANSITION_ANCHORS:
        require(kind == "transition", "coverage-kind")
        keys(value, {"before", "token", "op", "arg", "reason"})
        before = vector(value["before"], 6)
        signature = [value["op"], value["arg"], *before[2:]]
        require(equal(signature, TRANSITION_ANCHORS[case_id]), "coverage-anchor")
        require(before[1] == "A" and before[0] == (2 if case_id == "invalid-schema" else 1),
                "coverage-anchor")
        expected_token = {"foreign-profile": ["B", 8, 0], "stale-generation": ["A", 7, 9],
                          "stale-revision": ["A", 8, 0]}.get(case_id, token_of(before))
        require(equal(value["token"], expected_token), "coverage-token")
        reason = {"Advance": "OrdinaryAdvance", "Jump": "ExplicitJump"}.get(value["op"], "None")
        require(value["reason"] == ("ExplicitJump" if case_id == "invalid-reason" else reason),
                "coverage-reason")
    if case_id == "query-detached":
        require(kind == "detached" and equal(value, [[1, "A", 54000000, "None", 1, 0],
                                                     [1, "A", 54000001, "None", 1, 1]]), "coverage-anchor")
    if case_id.startswith("consumer-") and case_id in REQUIRED:
        require(kind == "consumer", "coverage-kind")
        keys(value, {"snapshot", "current", "binding", "previous"})
        time = 21600001 if case_id in {"consumer-jump", "consumer-invalid"} else 21600000
        generation = 8 if case_id == "consumer-restore" else 7
        revision = 0 if case_id == "consumer-restore" else 10 if case_id in {
            "consumer-resume", "consumer-jump", "consumer-invalid"} else 9
        require(equal(value["snapshot"], [1, "A", time, "None", generation, revision]), "coverage-anchor")
        token = {"consumer-profile": ["B", 7, 9], "consumer-generation": ["A", 8, 0],
                 "consumer-revision": ["A", 7, 10]}.get(case_id, ["A", generation, revision])
        require(equal(value["current"], token), "coverage-token")
        version = 2 if case_id == "consumer-version" else -1 if case_id == "consumer-invalid" else 1
        require(equal(value["binding"], ["SYNTHETIC_SCHEME", version]), "coverage-anchor")
        if case_id == "consumer-initial":
            require(value["previous"] is None, "coverage-anchor")
        else:
            previous_generation = 8 if case_id == "consumer-generation" else 7
            previous_revision = 0 if case_id == "consumer-generation" else 10 if case_id == "consumer-revision" else 9
            require(equal(value["previous"], [1, "A", previous_generation, previous_revision,
                                             21600000, 6, True, ["SYNTHETIC_SCHEME", 1]]), "coverage-anchor")


class InvalidMatrix(ValueError):
    """Safe diagnostic: never emit arbitrary caller contents or local paths."""


def require(condition, code="invalid-shape"):
    if not condition:
        raise InvalidMatrix(code)


def integer(value, lower=0, upper=MAX_I64):
    return type(value) is int and lower <= value <= upper


def equal(left, right):
    # Python bool == int is unsuitable for a literal JSON oracle.
    return json.dumps(left, sort_keys=True) == json.dumps(right, sort_keys=True)


def keys(value, names):
    require(type(value) is dict and set(value) == set(names))


def vector(value, size):
    require(type(value) is list and len(value) == size)
    return value


def bounded_tree(value, depth=0):
    require(depth <= 12, "depth-cap")
    if type(value) is dict:
        require(len(value) <= 16, "object-cap")
        for key, child in value.items():
            require(type(key) is str and 0 < len(key) <= 256, "identity-cap")
            bounded_tree(child, depth + 1)
    elif type(value) is list:
        require(len(value) <= 256, "array-cap")
        for child in value:
            bounded_tree(child, depth + 1)
    elif type(value) is str:
        require(len(value) <= 256, "identity-cap")
        require(not any(ch in value for ch in ("/", "\\", "\0")), "unsafe-identity")
    elif type(value) is int:
        require(-MAX_I64 <= value <= MAX_I64, "integer-cap")
    else:
        require(value is None or type(value) is bool, "invalid-scalar")


def normalized_values(timestamp, year):
    require(integer(timestamp) and integer(year, 0, 2147483647))
    days, ms = divmod(timestamp, 86400000)
    hours = ms // 3600000
    return [days, ms, hours, ms // 60000 % 60, ms // 1000 % 60,
            days % 30 + 1, days // 30 % 12 + 1, year + days // 360,
            6 <= hours < 18]


def source_admission(value):
    previous, current, gate, tb = vector(value, 4)
    require(integer(previous) and integer(current) and current >= previous)
    require(integer(gate, 0, 30) and type(tb) is bool)
    elapsed = current - previous
    if elapsed < 5:
        return [0, 0, 0, previous]
    admitted = min(elapsed, 250)
    return [admitted, admitted * 8 if gate == 0 and not tb else 0,
            admitted * 8 if gate == 0 else 0, current]


def state_error(state, descriptor=False):
    if type(state) is not list or len(state) != (4 if descriptor else 6):
        return "InvalidState"
    schema, profile, timestamp, pause = state[:4]
    if not integer(schema, 1, 1):
        return "InvalidSchema"
    if profile != "A":
        return "InvalidProfile"
    if not integer(timestamp) or type(pause) is not str or pause not in PAUSES:
        return "InvalidState"
    if not descriptor and (not integer(state[4], 1) or not integer(state[5])):
        return "InvalidState"
    return "None"


def token_of(state):
    return [state[1], state[4], state[5]]


def check_transition(value, expected):
    """Declarative tuple relation only; does not expose an authority operation."""
    keys(value, {"before", "token", "op", "arg", "reason"})
    before, token, op, arg, reason = (value[k] for k in
                                    ("before", "token", "op", "arg", "reason"))
    vector(before, 6)
    vector(token, 3)
    require(op in {"Query", "Advance", "Pause", "Jump", "Restore"})
    disposition, error, after = vector(expected, 3)
    vector(after, 6)
    failure = state_error(before)
    if failure == "None":
        if op == "Advance" and not integer(arg):
            failure = "InvalidDuration"
        elif op == "Jump" and not integer(arg):
            failure = "InvalidState"
        elif op == "Pause" and (type(arg) is not str or arg not in PAUSES):
            failure = "InvalidState"
        elif op == "Restore":
            failure = state_error(arg, descriptor=True)
        elif op == "Query" and arg is not None:
            failure = "InvalidState"
    required_reason = {"Advance": "OrdinaryAdvance", "Jump": "ExplicitJump"}.get(op, "None")
    if failure == "None" and reason != required_reason:
        failure = "InvalidReason"
    if failure == "None" and not equal(token, token_of(before)):
        failure = "StaleToken"
    if failure != "None":
        require(equal(expected, ["Rejected", failure, before]), "transition-rejection")
        return
    unchanged = (op == "Query" or
                 op == "Advance" and (arg == 0 or before[3] != "None") or
                 op == "Pause" and arg == before[3] or
                 op == "Jump" and arg == before[2])
    if unchanged:
        require(equal(expected, ["NoChange", "None", before]), "transition-noop")
        return
    if op == "Advance" and arg > MAX_I64 - before[2]:
        failure = "TimeOverflow"
    elif op == "Restore" and before[4] == MAX_I64:
        failure = "GenerationExhausted"
    elif op != "Restore" and before[5] == MAX_I64:
        failure = "RevisionExhausted"
    if failure != "None":
        require(equal(expected, ["Rejected", failure, before]), "transition-capacity")
        return
    require(disposition == "Changed" and error == "None", "transition-disposition")
    require(state_error(after) == "None", "transition-after-shape")
    require(after[:2] == before[:2], "immutable-profile")
    if op == "Restore":
        require(equal(after[:4], arg) and after[4] == before[4] + 1 and after[5] == 0,
                "restore-relation")
    else:
        require(after[4] == before[4] and after[5] == before[5] + 1, "revision-relation")
        timestamp = before[2] + arg if op == "Advance" else arg if op == "Jump" else before[2]
        pause = arg if op == "Pause" else before[3]
        require(after[2] == timestamp and after[3] == pause, "state-relation")


def metadata_valid(meta):
    if type(meta) is not list or len(meta) != 8:
        return False
    schema, profile, generation, revision, timestamp, hour, is_day, binding = meta
    state = [schema, profile, timestamp, "None", generation, revision]
    return (state_error(state) == "None" and integer(hour, 0, 23) and
            type(is_day) is bool and hour == timestamp // 3600000 % 24 and
            is_day == (6 <= hour < 18) and binding_valid(binding))


def binding_valid(binding):
    return (type(binding) is list and len(binding) == 2 and
            binding[0] == "SYNTHETIC_SCHEME" and integer(binding[1]))


def check_consumer(value, expected):
    keys(value, {"snapshot", "current", "binding", "previous"})
    snapshot, token, binding, previous = (value[k] for k in
                                          ("snapshot", "current", "binding", "previous"))
    vector(snapshot, 6)
    vector(token, 3)
    require(previous is None or metadata_valid(previous), "invalid-previous-metadata")
    failure = state_error(snapshot)
    if failure == "None" and not binding_valid(binding):
        failure = "InvalidState"
    if failure == "None" and not equal(token, token_of(snapshot)):
        failure = "StaleToken"
    if failure != "None":
        require(equal(expected, ["Rejected", failure, previous]), "consumer-atomic-retention")
        return
    schema, profile, timestamp, _, generation, revision = snapshot
    hour = timestamp // 3600000 % 24
    metadata = [schema, profile, generation, revision, timestamp, hour, 6 <= hour < 18, binding]
    require(equal(expected, ["NoChange" if equal(metadata, previous) else "Changed",
                             "None", metadata]), "consumer-relation")


def check_row(row):
    kind, value, expected = row["kind"], row["input"], row["expected"]
    if kind == "transition":
        check_transition(value, expected)
        return
    if kind == "consumer":
        check_consumer(value, expected)
        return
    if kind == "normalized":
        timestamp, year = vector(value, 2)
        actual = normalized_values(timestamp, year)
    elif kind == "calendar":
        day, year = vector(value, 2)
        require(integer(day, 0, 4294967295) and integer(year, 0, 2147483647))
        ordinary_day, month = day % 30 + 1, day // 30 % 12 + 1
        actual = [ordinary_day, month, year + day // 360, month, ordinary_day]
    elif kind == "raw":
        day, ms = vector(value, 2)
        require(integer(day, 0, 4294967295) and integer(ms, 0, 4294967295))
        actual = [day, ms, day + ms // 86400000, ms % 86400000]
    elif kind == "carry":
        day, ms, duration = vector(value, 3)
        require(all(integer(v, 0, 4294967295) for v in value))
        total = ms + duration
        require(total <= 4294967295 and day + total // 86400000 <= 4294967295)
        actual = [day, total] if total <= 86400000 else [day + total // 86400000, total % 86400000]
    elif kind == "startup":
        present, year, hour, retained_year, retained_ms = vector(value, 5)
        require(type(present) is bool and integer(retained_year, 0, 2147483647))
        require(integer(retained_ms, 0, 2147483647))
        require(year is None or integer(year, -1, 2147483647))
        require(hour is None or integer(hour, -1, 24))
        # Only explicitly bounded non-overflow setter examples; no atoi parser.
        y = (year if year is not None and year >= 0 else retained_year) if present else 1885
        ms = (hour * 3600000 if hour is not None and hour >= 0 else retained_ms) if present else 54000000
        actual = [y, ms, ms if ms else 1]
    elif kind == "bootstrap":
        year, hour = vector(value, 2)
        actual = (["Ready", "None", [hour * 3600000, 1, 0]]
                  if integer(year, 0, 2147483647) and integer(hour, 0, 23)
                  else ["Rejected", "InvalidProfile", None])
    elif kind == "admission":
        actual = source_admission(value)
    elif kind == "references":
        reference, timestamps = vector(value, 2)
        require(type(timestamps) is list and 0 < len(timestamps) <= 16)
        actual = []
        for timestamp in timestamps:
            increments = source_admission([reference, timestamp, 0, False])
            actual.append(increments)
            reference = increments[3]
    elif kind == "gate":
        counter, operations = vector(value, 2)
        require(integer(counter, 0, 30) and type(operations) is list and 0 < len(operations) <= 16)
        actual = []
        for op in operations:
            require(op in {"inc", "dec", "menu", "options", "close"})
            counter = min(30, counter + 1) if op in {"inc", "menu"} else (
                max(0, counter - 1) if op in {"dec", "close"} else counter)
            actual.append(counter)
    elif kind == "delay":
        now, delay = vector(value, 2)
        require(integer(now) and integer(delay) and now <= MAX_I64 - max(delay, 1))
        actual = [max(delay, 1), now + max(delay, 1)]
    elif kind == "due":
        now, deadline, host_delta, gate = vector(value, 4)
        require(integer(now) and integer(deadline) and integer(host_delta) and integer(gate, 0, 30))
        actual = [deadline <= now, host_delta >= 5 and deadline <= now]
    elif kind == "detached":
        historical, current = vector(value, 2)
        require(state_error(historical) == state_error(current) == "None")
        actual = [historical, equal(token_of(historical), token_of(current))]
    else:
        raise InvalidMatrix("unknown-kind")
    require(equal(actual, expected), "literal-mismatch")


def check_profiles(profiles):
    keys(profiles, {"A", "B"})
    fields = {"ClockSchemaVersion", "PolicyId", "SourceRevision", "SourceDataSha256",
              "StartupResourceSha256", "StartYear", "StartHour"}
    for alias, profile in profiles.items():
        keys(profile, fields)
        require(integer(profile["ClockSchemaVersion"], 1, 1) and profile["PolicyId"] == POLICY)
        require(profile["SourceRevision"] == PIN)
        for field in ("SourceDataSha256", "StartupResourceSha256"):
            require(type(profile[field]) is str and re.fullmatch("[0-9a-f]{64}", profile[field]) is not None)
        require(integer(profile["StartYear"], 0, 2147483647) and integer(profile["StartHour"], 0, 23))
        require(profile["StartYear"] == 1885 and profile["StartHour"] == 15)
        require(profile["SourceDataSha256"] == ("a" if alias == "A" else "b") * 64)
        require(profile["StartupResourceSha256"] == "c" * 64)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate-json-key")
        result[key] = value
    return result


def validate(path):
    with path.open("rb") as stream:
        raw = stream.read(262145)
    require(len(raw) <= 262144, "fixture-byte-cap")
    matrix = json.loads(raw, object_pairs_hook=unique_object,
                        parse_constant=lambda _: require(False, "invalid-json-number"))
    keys(matrix, {"schema", "policy", "source_worksheet", "profiles", "cases"})
    require(integer(matrix["schema"], 1, 1) and matrix["policy"] == POLICY)
    require(matrix["source_worksheet"] == "ce-a7ff41b-source-observation-v1")
    check_profiles(matrix["profiles"])
    cases: list[Any] = matrix["cases"]
    require(type(cases) is list and 0 < len(cases) <= 256, "case-cap")
    doc = DOCUMENT.read_text(encoding="utf-8")
    seen = set()
    for row in cases:
        keys(row, {"id", "kind", "class", "requirement", "reference", "input", "expected"})
        bounded_tree(row)
        case_id, kind = row["id"], row["kind"]
        require(type(case_id) is str and re.fullmatch("[a-z0-9-]{1,256}", case_id) is not None)
        require(case_id not in seen, "duplicate-case")
        seen.add(case_id)
        require(row["requirement"] in {f"AC{i}" for i in range(1, 7)}, "unknown-requirement")
        refs = SOURCE_REFS if row["class"] == "Source" else PROPOSAL_REFS if row["class"] == "Proposal" else {}
        require(kind in refs and row["reference"] in refs[kind], "classification-reference")
        if row["class"] == "Source":
            require(f'[{row["reference"]}]: https://github.com/alexbatalov/arcanum-ce/blob/{PIN}/' in doc,
                    "missing-pinned-reference")
        require(re.search(r"(?<![a-z0-9-])" + re.escape(case_id) + r"(?![a-z0-9-])", doc) is not None,
                "missing-document-case")
        check_coverage(row)
        check_row(row)
    require(REQUIRED <= seen, "missing-required-case")
    return matrix, len(cases)


def self_test(matrix):
    """Real CLI subprocess rejection, bounded inputs, no expected-output generation."""
    variants = []
    missing = copy.deepcopy(matrix)
    missing["cases"].pop()
    variants.append(("missing-required-case", missing))
    changed = copy.deepcopy(matrix)
    changed["cases"][0]["expected"][2] = 6
    variants.append(("literal-mismatch", changed))
    malformed = copy.deepcopy(matrix)
    malformed["cases"][0]["input"][0] = True
    variants.append(("coverage-anchor", malformed))
    bad_type = copy.deepcopy(matrix)
    bad_type["cases"][0]["input"][0] = 21599999.0
    variants.append(("invalid-scalar", bad_type))
    for case_id, expected_index, incorrect, code in (
            ("restore-same", 4, 7, "restore-relation"),
            ("paused-positive", 2, 54000100, "transition-noop"),
            ("time-overflow", 2, 0, "transition-capacity"),
            ("consumer-invalid", 4, 21600001, "consumer-atomic-retention")):
        mutated = copy.deepcopy(matrix)
        row = next(row for row in mutated["cases"] if row["id"] == case_id)
        row["expected"][2][expected_index] = incorrect
        variants.append((code, mutated))
    duplicate = copy.deepcopy(matrix)
    duplicate["cases"].append(copy.deepcopy(matrix["cases"][0]))
    variants.append(("duplicate-case", duplicate))
    unclassified = copy.deepcopy(matrix)
    unclassified["cases"][0]["class"] = "Actual"
    variants.append(("classification-reference", unclassified))
    oversized = copy.deepcopy(matrix)
    oversized["cases"] = [matrix["cases"][0]] * 257
    variants.append(("case-cap", oversized))
    # tempfile follows the process TMPDIR; no repo fixtures or shared data removed.
    with tempfile.TemporaryDirectory(prefix="clock-policy-", dir=os.environ.get("TMPDIR")) as directory:
        for index, (code, variant) in enumerate(variants):
            path = Path(directory) / f"negative-{index}.json"
            path.write_text(json.dumps(variant), encoding="utf-8")
            command = [sys.executable, "-B", str(Path(__file__).resolve()), "--matrix", str(path)]
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            require(result.returncode == 1 and result.stdout == "" and result.stderr.strip() == f"FAIL: {code}",
                    "negative-cli-did-not-reject")
            print(f"REJECT verified: {code} (CLI exit 1)")
    return len(variants)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--matrix", type=Path, default=FIXTURE,
                        help="bounded synthetic matrix only; never original data")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    try:
        matrix, count = validate(args.matrix)
        rejected = self_test(matrix) if args.self_test else 0
    except (InvalidMatrix, OSError, ValueError, TypeError, KeyError, RecursionError, OverflowError) as error:
        code = str(error) if isinstance(error, InvalidMatrix) else "malformed-matrix"
        print(f"FAIL: {code}", file=sys.stderr)
        return 1
    print(f"PASS: {count} synthetic table cases; {rejected} negative CLI checks; NOT production conformance")
    return 0


if __name__ == "__main__":
    sys.exit(main())
