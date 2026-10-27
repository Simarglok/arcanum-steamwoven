#!/usr/bin/env python3
"""Bounded WORLD validation children. Private original argv/output never leave memory.

ARC-DEV-043: the synthetic and persistence kinds read no original data; with --raw-log their whole Godot
output also goes to that owner-only file (the export's git-ignored evidence), so a timed-out child shows the
stage it waited in. The printed summary stays closed either way."""
from __future__ import annotations

import argparse
import contextlib
from datetime import datetime, timezone
import io
import json
import os
import re
from pathlib import Path
import signal
import subprocess
import sys
import time
import tempfile


class ChildFailure(Exception):
    def __init__(self, stage: str, error: Exception, child_exit: int | None):
        self.stage, self.error, self.child_exit = stage, error, child_exit
        self.reap_failure: dict | None = None


def error_facts(error: Exception) -> dict:
    # Neither arbitrary exception names/messages nor subprocess argv are public.
    category = next((name for cls, name in (
        (subprocess.CalledProcessError, "CalledProcessError"),
        (subprocess.TimeoutExpired, "TimeoutExpired"), (OSError, "OSError"),
        (ValueError, "ValueError"), (TypeError, "TypeError"), (KeyError, "KeyError"),
        (subprocess.SubprocessError, "SubprocessError")) if isinstance(error, cls)), "Exception")
    facts: dict = {"exceptionClass": category}
    for field in ("errno", "returncode"):
        value = getattr(error, field, None)
        if type(value) is int and -4096 <= value <= 4096:
            facts[field] = value
    return facts


def safe_diagnostics(output: str) -> list[str]:
    # Project categories, never echo an allowed-looking prefix's arbitrary tail.
    # Success markers here are context only; complete reports and exits remain gates.
    categories = (
        "ARC-WORLD-001 native GUI assertion FAIL", "ARC-WORLD-001 native-smoke PASS",
        "ARC-RENDER-004 native-smoke failure", "ARC-RENDER-004 native-smoke readback",
        "ARC-RENDER-006 human mismatch", "ARC-RENDER-006 human sample",
        "ARC-WORLD-003 retirement/ownership PASS", "ARC-WORLD-003 original reload PASS",
        "ARC-WORLD-003 production observation PASS", "ARC-WORLD-001 self-test PASS",
        "ARC-WORLD-001 settings-test PASS", "ARC-GPAD-004 settings-test PASS",
        "ARC-INPUT-001 palette chord matrix PASS", "ARC-INPUT-001 palette UI PASS",
        "ARC-RENDER-008 original overlay PASS", "ARC-RENDER-008 overlay matrix PASS", "ARC-RENDER-008 overlay invalidation PASS",
    )
    result = []
    for line in output.splitlines():
        for category in categories:
            if line == category or line.startswith(category + " "):
                if category not in result:
                    result.append(category)
                break
        else:
            # Recognize failure category without trusting a code supplied by stdout.
            if line.startswith("ARC-WORLD-003 observation-failure="):
                category = "ARC-WORLD-003 observation-failure"
                if category not in result:
                    result.append(category)
    return result


def run_child(argv: list[str], env: dict[str, str], timeout: int) -> tuple[int, str]:
    try:
        child = subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                 env=env, start_new_session=True)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        raise ChildFailure("child-spawn", error, None) from error
    stage = "child-communication"
    try:
        try:
            output, _ = child.communicate(timeout=timeout)
            return child.returncode, output
        except subprocess.TimeoutExpired:
            stage = "child-termination"
            os.killpg(child.pid, signal.SIGTERM)
            try:
                output, _ = child.communicate(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, signal.SIGKILL)
                output, _ = child.communicate(timeout=5)
            return 124, output
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        # Best-effort bounded reap; a process-exit/signal race is still a failure,
        # not fabricated timeout/success. Preserve the primary failing operation.
        secondary = None
        if child.poll() is None:
            try:
                os.killpg(child.pid, signal.SIGKILL)
                child.communicate(timeout=5)
            except (OSError, ValueError, subprocess.SubprocessError) as reap_error:
                secondary = error_facts(reap_error)
        failure = ChildFailure(stage, error, child.returncode)
        failure.reap_failure = secondary
        raise failure from error


class RawLog:
    """ARC-DEV-043: one owner-only file per validation; each child is framed by start and exit lines."""

    def __init__(self, path: Path):
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        self.stream = os.fdopen(descriptor, "w", encoding="utf-8")

    def begin(self, label: str, timeout: int) -> None:
        # Written before the child starts, so even a validator stopped from outside names the running child.
        self.stream.write(f"world-validate child {label} start timeout={timeout}s\n")
        self.stream.flush()

    def end(self, label: str, code: int, output: str, elapsed: float) -> None:
        self.stream.write(output if not output or output.endswith("\n") else output + "\n")
        outcome = "timeout" if code == 124 else "exit"
        self.stream.write(f"world-validate child {label} {outcome} code={code} elapsed={elapsed:.3f}s\n")
        self.stream.flush()

    def cleanup(self) -> None:
        self.stream.close()


def _main(state: dict) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--candidate-root", type=Path, required=True)
    parser.add_argument("--app", type=Path)
    parser.add_argument("--kind", choices=("original", "synthetic", "persistence", "human", "human-metadata", "grid-original"), default="original")
    parser.add_argument("--task", choices=("ARC-RENDER-006", "ARC-WORLD-003"), default="ARC-RENDER-006")
    parser.add_argument("--headless", action="store_true")
    parser.add_argument("--timeout", type=int, default=60)
    parser.add_argument("--raw-log", type=Path)
    args = parser.parse_args()
    state.update(stage="argument-policy", kind=args.kind, host="exported" if args.app else "source")
    if not 1 <= args.timeout <= 180:
        raise ValueError("bounded timeout outside permitted range")
    if args.raw_log is not None and args.kind not in ("synthetic", "persistence"):
        raise ValueError("raw output is kept only for kinds that read no original data")
    state["stage"] = "root-resolution"
    root = args.candidate_root.resolve(strict=True)
    sys.path.insert(0, str(root))
    from tools.local_dev import config, launch
    state["stage"] = "executable-resolution"
    executable = str(args.app.resolve(strict=True)) if args.app else "/Applications/Godot_mono.app/Contents/MacOS/Godot"
    prefix = [executable] + ([] if args.app else ["--path", str(root / "tools/terrain-viewer/Godot")])
    if args.headless:
        prefix += ["--headless"]
    env = dict(os.environ)
    # Every child (including original/synthetic) is isolated before initialization.
    state["stage"] = "owned-temp"
    owned = tempfile.TemporaryDirectory(prefix="world-validation-", dir=os.environ.get("TMPDIR"))
    state["owned"].append(owned)
    env["WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH"] = str(Path(owned.name) / "input.json")
    env["WORLD_DIAGNOSTIC_ZOOM_TEST_PATH"] = str(Path(owned.name) / "zoom.json")
    raw = None
    if args.raw_log is not None:
        state["stage"] = "raw-log"
        raw = RawLog(args.raw_log)
        state["owned"].append(raw)

    def child(argv: list[str], timeout: int, label: str) -> tuple[int, str]:
        if raw is None:
            return run_child(argv, env, timeout)
        raw.begin(label, timeout)
        started = time.monotonic()
        code, output = run_child(argv, env, timeout)
        raw.end(label, code, output, time.monotonic() - started)
        return code, output
    env.update(__GODOT_SHELL_ENV_SET="1", DOTNET_ROOT=str(Path.home() / ".local/share/dotnet"),
               PATH=str(Path.home() / ".local/share/dotnet") + ":/usr/bin:/bin:/usr/sbin:/sbin")
    state["stage"] = "git-head"
    head = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"], text=True, capture_output=True, check=True).stdout.strip()
    if not re.fullmatch(r"[0-9a-f]{40}", head):
        raise ValueError("invalid candidate head")
    state["head"] = head
    began = datetime.now(timezone.utc).isoformat(); start = time.monotonic()
    stage_results = []
    if args.kind in ("original", "human", "human-metadata", "grid-original"):
        state["stage"] = "private-reference-admission"
        if args.headless:
            raise ValueError("original readback requires graphical rendering")
        if args.kind != "original":
            reference = env.get("RENDER006_HUMAN_ORACLE")
            if not reference or not config.regular_file(Path(reference)).is_file():
                raise ValueError("explicit independent private human reference required")
            env["RENDER006_CANDIDATE"] = head
            env["RENDER006_HOST"] = "exported" if args.app else "source"
            env["RENDER006_TASK"] = args.task
            if args.kind == "grid-original":
                if args.task != "ARC-WORLD-003":
                    raise ValueError("grid-original requires the literal WORLD003 task")
                env["WORLD003_GRID_VALIDATE"] = "1"
        state["stage"] = "config-roles"
        # ARC-DEV-015: no installation-record gate; only the pinned regression exports run this validator, on the pinned installation.
        argv = prefix + ["--", "--native-smoke", *launch.terrain_arguments(config.load_config().get("inputs", {}), "world-diagnostic")]
        state["stage"] = "child-launch"
        code, output = child(argv, args.timeout, "native-smoke")
        state["childExit"] = code
        state["stage"] = "report-processing"
        # ARC-RENDER-008: the original overlay check (Open/Blocked/reverse/outside) runs in every native smoke.
        passed = (code == 0 and any(line.startswith("ARC-WORLD-001 native-smoke PASS ")
                                    and "fingerprints=immutable rendered=PASS" in line for line in output.splitlines())
                  and "ARC-RENDER-008 original overlay PASS " in output)
        if args.kind == "grid-original":
            from tools.local_dev.grid_validation import validate_report as validate_grid_report
            report_lines = [line.removeprefix("ARC-WORLD-003 grid-readback ")
                            for line in output.splitlines() if line.startswith("ARC-WORLD-003 grid-readback ")]
            # Report decoding/validation is a separate quality gate from child
            # exit. Never lose that distinction to the outer input-error catch.
            failure = None
            grid = None
            if not passed:
                failure = "child-timeout" if code == 124 else "child-outcome"
            elif len(report_lines) != 1:
                failure = "report-count"
            else:
                try:
                    grid = json.loads(report_lines[0])
                except ValueError:
                    failure = "report-json"
                else:
                    try:
                        validate_grid_report(grid, head, env["RENDER006_HOST"])
                    except (ValueError, TypeError, KeyError):
                        # Untrusted JSON or exception messages can contain paths
                        # or protected values. Emit only the closed stage code.
                        failure = "report-contract"
            if failure is not None:
                # Keep the actual child outcome and the same closed diagnostic
                # prefixes used below. Do not misclassify a graphical assertion
                # as an input failure or lose its evidence before diagnosis.
                print(json.dumps({"schema": "arcanum-world-validation-v1",
                                  "kind": args.kind, "candidateCommit": head,
                                  "host": env["RENDER006_HOST"], "outcome": "FAIL",
                                  "exitCode": code, "graphical": True, "failureStage": failure,
                                  "startedAt": began, "elapsedSeconds": round(time.monotonic()-start,3),
                                  "diagnostics": safe_diagnostics(output)}, sort_keys=True))
                return 1
            assert isinstance(grid, dict)
            grid.update(startedAt=began, elapsedSeconds=round(time.monotonic()-start,3))
            print(json.dumps(grid, sort_keys=True))
            return 0
        if args.kind not in ("original", "grid-original"):
            from tools.local_dev.human_validation import validate_report
            reports = [json.loads(line.removeprefix("ARC-RENDER-006 human-readback "))
                       for line in output.splitlines() if line.startswith("ARC-RENDER-006 human-readback ")]
            if len(reports) != 1:
                passed = False
            else:
                human = reports[0]
                validate_report(human, head, env["RENDER006_HOST"], "human", args.task)
                human.update(kind=args.kind, outcome="PASS" if passed else "FAIL", exitCode=code,
                             startedAt=began, elapsedSeconds=round(time.monotonic()-start,3))
                validate_report(human, head, env["RENDER006_HOST"], args.kind, args.task)
                print(json.dumps(human, sort_keys=True))
                return 0 if passed else 1
    elif args.kind == "synthetic":
        state["stage"] = "child-launch"
        code, output = child(prefix + ["--", "--self-test"], args.timeout, "self-test")
        state["childExit"] = code
        # ARC-INPUT-001: the palette chord matrix and the palette UI at both required viewports.
        passed = (code == 0 and "ARC-WORLD-001 self-test PASS" in output
                  and "ARC-INPUT-001 palette chord matrix PASS " in output
                  and all(f"ARC-INPUT-001 palette UI PASS viewport={size} " in output for size in ("1280x800", "2560x1440"))
                  # ARC-RENDER-008: overlay matrix (rasters when graphical) and failure/republish invalidation.
                  and "ARC-RENDER-008 overlay matrix PASS mode=world-diagnostic " in output
                  and "ARC-RENDER-008 overlay invalidation PASS " in output)
    else:
        # This file is created by this runner alone, never ordinary preferences.
        state["stage"] = "owned-temp"
        persistence = tempfile.TemporaryDirectory(prefix="world-persistence-", dir=os.environ.get("TMPDIR"))
        state["owned"].append(persistence)
        directory = persistence.name
        code, output, passed = 0, "", False
        if directory:
            env["WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH"] = str(Path(directory) / "owned.json")
            env["WORLD_DIAGNOSTIC_ZOOM_TEST_PATH"] = str(Path(directory) / "zoom.json")
            passed = True; code = 0
            for stage in ("first-launch", "write-00", "read-00", "write-01", "read-01",
                          "write-10", "read-10", "write-11", "read-11", "reset", "defaults"):
                state["stage"] = "child-launch"
                code, output = child(prefix + ["--", "--world-settings-test", stage], min(args.timeout, 30), f"settings-test {stage}")
                state["childExit"] = code
                passed &= code == 0 and f"ARC-WORLD-001 settings-test PASS {stage}" in output
                stage_results.append({"stage": stage, "exitCode": code, "outcome": "PASS" if passed else "FAIL"})
                if not passed:
                    break
            if passed:
                def zoom_stage(stage: str, label: str | None = None) -> bool:
                    nonlocal code, output
                    state["stage"] = "child-launch"
                    code, output = child(prefix + ["--", "--world-settings-test", stage], min(args.timeout, 30), f"settings-test {label or stage}")
                    state["childExit"] = code
                    ok = code == 0 and f"ARC-GPAD-004 settings-test PASS {stage}" in output
                    stage_results.append({"stage": label or stage, "exitCode": code, "outcome": "PASS" if ok else "FAIL"})
                    return ok
                for stage in ("zoom-first", "zoom-write", "zoom-read", "zoom-reset", "zoom-defaults", "zoom-pending", "zoom-read-pending", "zoom-reset"):
                    passed = zoom_stage(stage)
                    if not passed:
                        break
                malformed = ("broken", "null", "[]", "{}", '{"zoom":3}', '{"version":2,"zoom":3}',
                             '{"version":"1","zoom":3}', '{"version":1,"zoom":"3"}',
                             '{"version":1,"zoom":null}', '{"version":1,"zoom":0.124}',
                             '{"version":1,"zoom":8.001}', '{"version":1,"zoom":1e999}')
                for index, value in enumerate(malformed):
                    if not passed:
                        break
                    state["stage"] = "owned-preferences-write"
                    Path(env["WORLD_DIAGNOSTIC_ZOOM_TEST_PATH"]).write_text(value, encoding="utf-8")
                    passed = zoom_stage("zoom-malformed", f"zoom-malformed-{index}")
                if passed:
                    env["WORLD_DIAGNOSTIC_ZOOM_TEST_PATH"] = str(Path(directory) / "missing-parent" / "zoom.json")
                    passed = zoom_stage("zoom-failure")
    # No raw output, argv, input path, pixel or protected content in durable logs.
    summary = {"schema": "arcanum-world-validation-v1", "kind": args.kind,
               "candidateCommit": head, "host": "exported" if args.app else "source", "startedAt": began,
               "elapsedSeconds": round(time.monotonic() - start, 3), "exitCode": code,
               "graphical": not args.headless, "outcome": "PASS" if passed else "FAIL",
               "stages": stage_results,
               "diagnostics": safe_diagnostics(output)}
    print(json.dumps(summary, sort_keys=True))
    return 0 if passed else 1


def main() -> int:
    state = {"stage": "argument-policy", "owned": [], "childExit": None}
    buffered = io.StringIO()
    failure: dict | None = None
    result = 2
    try:
        with contextlib.redirect_stdout(buffered), contextlib.redirect_stderr(io.StringIO()):
            result = _main(state)
    except SystemExit as error:
        if error.code == 0:
            result = 0
        else:
            failure = {"failureStage": "argument-policy", "exceptionClass": "ArgumentError"}
    except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError, ChildFailure) as error:
        stage = state["stage"]
        if isinstance(error, ChildFailure):
            stage, state["childExit"] = error.stage, error.child_exit
            failure = {"failureStage": stage, **error_facts(error.error)}
            if getattr(error, "reap_failure", None):
                failure["reapFailure"] = error.reap_failure
        else:
            failure = {"failureStage": stage, **error_facts(error)}
    finally:
        cleanup = []
        for owned in reversed(state["owned"]):
            try:
                owned.cleanup()
            except (OSError, ValueError, subprocess.SubprocessError) as error:
                cleanup.append(error_facts(error))
        if cleanup:
            if failure is None and result == 0:
                failure = {"failureStage": "cleanup", **cleanup[0]}
            elif failure is None:
                # Keep the existing quality failure and append cleanup facts.
                report = json.loads(buffered.getvalue())
                report["cleanupFailures"] = cleanup
                buffered = io.StringIO(json.dumps(report, sort_keys=True) + "\n")
            if failure is not None:
                failure["cleanupFailures"] = cleanup
    if failure is not None:
        report = {"schema": "arcanum-world-validation-v1", "outcome": "FAIL",
                  "exitCode": state["childExit"], "wrapperExitCode": 2, **failure}
        for source, target in (("head", "candidateCommit"), ("kind", "kind"), ("host", "host")):
            if source in state:
                report[target] = state[source]
        print(json.dumps(report, sort_keys=True))
        return 2
    # Acceptance is emitted only after successful scope cleanup. No PASS followed
    # by cleanup FAIL, and no private exception/output leaks through the fallback.
    print(buffered.getvalue(), end="")
    return result


if __name__ == "__main__":
    raise SystemExit(main())
