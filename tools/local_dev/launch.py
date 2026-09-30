"""Safe non-shell ordinary-launch preparation and duplicate protection."""
from __future__ import annotations

import fcntl
import os
from pathlib import Path
import subprocess
from typing import Any

FORBIDDEN = ("--self-test", "--native-smoke", "--smoke", "--matrix", "--capture", "--screenshot", "--headless", "--fullscreen", "--automated", "--resize", "--input")


def assert_ordinary(arguments: list[str]) -> None:
    lowered = [argument.lower() for argument in arguments]
    if any(any(flag in argument for flag in FORBIDDEN) for argument in lowered):
        raise ValueError("ordinary launch refuses automation/test/capture flags")


def argv_from_manifest(manifest: dict[str, Any]) -> tuple[Path, list[str]]:
    app = manifest.get("app")
    if not isinstance(app, dict):
        raise ValueError("manifest lacks app")
    executable = app.get("executable") or app.get("executableRelativePath")
    if not isinstance(executable, str) or not executable:
        raise ValueError("manifest lacks executable")
    engine = app.get("engineArgs", [])
    user = app.get("ordinaryArgs", [])
    if not isinstance(engine, list) or not isinstance(user, list) or not all(isinstance(value, str) for value in engine + user):
        raise ValueError("manifest launch arguments are malformed")
    assert_ordinary(engine + user)
    return Path(executable), [*engine, "--", *user]


def exact_executable_in_use(executable: Path) -> bool:
    """Compare the executable token exactly; no broad process-name matching or killing."""
    completed = subprocess.run(["ps", "-axo", "pid=,command="], check=True, text=True, capture_output=True)
    target = str(executable.resolve())
    for line in completed.stdout.splitlines():
        fields = line.strip().split(maxsplit=1)
        if len(fields) != 2:
            continue
        command = fields[1]
        first = command.split(maxsplit=1)[0]
        if first == target:
            return True
    return False


def lock_for(root: Path, tool: str) -> Any:
    locks = root / "locks"
    locks.mkdir(mode=0o700, parents=True, exist_ok=True)
    handle = (locks / f"{tool}.lock").open("a+", encoding="utf-8")
    try:
        fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError as error:
        handle.close()
        raise ValueError("an ordinary launch for this tool is already active") from error
    return handle


def start(executable: Path, arguments: list[str], *, allow_duplicate: bool, lock_root: Path, tool: str, dry_run: bool) -> dict[str, Any]:
    executable = executable.resolve(strict=True)
    if not executable.is_file() or executable.is_symlink():
        raise ValueError("manifest executable is not a safe regular file")
    assert_ordinary(arguments)
    if not allow_duplicate and exact_executable_in_use(executable):
        raise ValueError("exact configured executable is already in use; pass --allow-duplicate to opt in")
    argv = [str(executable), *arguments]
    if dry_run:
        return {"argv": argv, "started": False}
    handle = lock_for(lock_root, tool)
    try:
        process = subprocess.Popen(argv, close_fds=True, start_new_session=True)
    except Exception:
        handle.close()
        raise
    # Keep the lock descriptor with the child only if it inherited it (it does not);
    # a running process is independently detected by exact executable identity.
    handle.close()
    return {"argv": argv, "started": True, "pid": process.pid}
