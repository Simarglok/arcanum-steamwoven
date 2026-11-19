"""ARC-DEV-2280: no tool can end a process it did not start (the owner's running game, another thread's viewer).

The packaged game opened with `open` belongs to launchd, never to a queue process group. The queue, the smoke runner and
the other tools may signal only a process group that a child they started leads (`start_new_session`), and never kill by name.
"""
from __future__ import annotations

import ast
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import unittest
from unittest.mock import patch

from tools.local_dev import run_queue

REPO = Path(__file__).resolve().parents[2]
TOOLS = REPO / "tools"
SUFFIXES = {".py", ".sh", ".yml", ".yaml", ".cs"}
SCANNED_ROOTS = (TOOLS, REPO / "src")
# What may never appear in a tool or in the game code: killing or quitting a process by name, by a fixed pid or through an Apple
# event (a shell `kill` with a flag, a `["kill", ...]` argument list, os.system, a C# Process.Kill, an NSRunningApplication).
FORBIDDEN = re.compile(r"\b(pkill|killall|taskkill)\b|\bkill\s+-\w|\[\s*[\"']kill[\"']\s*,|osascript|tell application|psutil|NSRunningApplication"
                       r"|os\.system\(|pthread_kill|Process\.Kill|Process\.GetProcess")
SIGNAL_CALL = re.compile(r"os\.(killpg|kill)\(|\.send_signal\(|\.terminate\(\)|\.kill\(\)")
THIS = Path(__file__).resolve()


def has_shebang(path: Path) -> bool:
    with path.open("rb") as handle:
        return handle.read(2) == b"#!"


def sources() -> list[Path]:
    """Every script and C# file of the tools and the game, the extensionless launchers included; tests are left out (they may
    start and stop their own children)."""
    skipped = {"node_modules", "__pycache__", ".venv", "obj", "bin", ".godot"}
    found = [path for root in SCANNED_ROOTS for path in root.rglob("*") if path.is_file()
             and (path.suffix in SUFFIXES or (not path.suffix and path.stat().st_size < 1_000_000 and has_shebang(path)))
             and not any(part in skipped for part in path.parts)]
    found += [REPO / "arcanum"]
    return [path for path in sorted(found) if path.resolve() != THIS and not path.name.startswith("test_") and "Tests" not in path.parts]


class SourceScanTests(unittest.TestCase):
    def test_no_tool_kills_by_name_or_quits_an_app(self) -> None:
        hits = [f"{path.relative_to(REPO)}:{number}: {line.strip()}" for path in sources()
                for number, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1)
                if FORBIDDEN.search(line) and not line.lstrip().startswith("#")]
        self.assertEqual([], hits)

    def test_only_known_sites_send_a_signal_and_each_is_a_started_group_or_a_liveness_check(self) -> None:
        # The allowed sites (Python only): the queue's _signal_group, the world validator's own child group, the HD pack's liveness
        # check and the Steamwoven bake lock's (os.kill with signal 0 signals nothing), the queue's lock holder, a single process it started itself, and the Codex batch runner's
        # time-out (ARC-DEV-3001), which kills the group of the `codex exec` child it started with start_new_session.
        allowed = {
            "tools/local_dev/run_queue.py": {"os.killpg(pid, number)", "os.kill(pid, 0)", "holder.kill()"},
            "tools/terrain-viewer/Godot/tools/world_validate.py": {"os.killpg(child.pid, signal.SIGTERM)", "os.killpg(child.pid, signal.SIGKILL)"},
            "tools/hd_pack/hd_pack.py": {"os.kill(pid, 0)"},
            "tools/steamwoven_ui/bake.py": {"os.kill(pid, 0)"},
            "tools/steamwoven_ui/codex_batch.py": {"os.killpg(process.pid, signal.SIGKILL)"},
        }
        seen: dict[str, set[str]] = {}
        for path in sources():
            if path.suffix == ".cs":
                continue
            relative = path.relative_to(REPO).as_posix()
            for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
                code = line.split("#", 1)[0]
                if SIGNAL_CALL.search(code):
                    seen.setdefault(relative, set()).add(code.strip())
        for relative, lines in seen.items():
            self.assertIn(relative, allowed, f"{relative} sends a signal: {sorted(lines)}")
            for line in lines:
                self.assertTrue(any(permitted in line for permitted in allowed[relative]), f"{relative}: {line}")

    def test_every_command_the_queue_signals_leads_its_own_session(self) -> None:
        tree = ast.parse((REPO / "tools/local_dev/run_queue.py").read_text(encoding="utf-8"))
        popens = [node for node in ast.walk(tree) if isinstance(node, ast.Call) and ast.unparse(node.func) == "subprocess.Popen"]
        self.assertTrue(popens)
        for call in popens:
            options = {keyword.arg: ast.unparse(keyword.value) for keyword in call.keywords}
            self.assertEqual("True", options.get("start_new_session"), f"line {call.lineno}")


class SignalGroupGuardTests(unittest.TestCase):
    def test_a_started_child_group_is_signalled(self) -> None:
        child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"], start_new_session=True)
        try:
            run_queue._signal_group(child.pid, signal.SIGTERM)
            self.assertEqual(-signal.SIGTERM, child.wait(10))
        finally:
            if child.poll() is None:
                child.kill()
                child.wait()

    def test_a_process_that_does_not_lead_its_group_is_never_signalled(self) -> None:
        # A sibling started without a new session (like any app launched from the owner's login session) shares our group.
        sibling = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"])
        try:
            self.assertNotEqual(sibling.pid, os.getpgid(sibling.pid))
            run_queue._signal_group(sibling.pid, signal.SIGKILL)
            self.assertIsNone(sibling.poll())
        finally:
            sibling.kill()
            sibling.wait()

    def test_init_and_bad_pids_are_refused(self) -> None:
        with patch.object(run_queue.os, "killpg") as killpg:
            for pid in (1, 0, -5, None, "7"):
                run_queue._signal_group(pid, signal.SIGKILL)  # type: ignore[arg-type]
            killpg.assert_not_called()

    def test_the_queues_own_group_is_refused_even_when_its_pid_leads_it(self) -> None:
        own = os.getpgrp()
        with patch.object(run_queue.os, "getpgid", return_value=own), patch.object(run_queue.os, "killpg") as killpg:
            run_queue._signal_group(own, signal.SIGKILL)
            killpg.assert_not_called()


if __name__ == "__main__":
    unittest.main()
