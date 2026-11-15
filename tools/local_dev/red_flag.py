"""ARC-DEV-2226: the "red on main" flag that `./arcanum main-smoke` writes and `./arcanum smoke pr` prints.

The flag is one JSON file in the main-smoke folder of the launcher home (beside the launch queue's folder, so every
checkout and worktree of the Mac reads the same one): while the rolling smoke on origin/main is red it names the first
red commit, the failing stages and the GitHub issue, so a thread cites it instead of re-running the smoke on a clean main.
The next green run clears it. A missing or unreadable file means no flag (a PR run is never stopped by it).
"""
from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any

from . import config

SCHEMA = "arcanum-red-on-main-v1"
FLAG = "red-on-main.json"


def directory() -> Path:
    return config.home() / "main-smoke"


def read(folder: Path | None = None) -> dict[str, Any] | None:
    try:
        flag = json.loads(((folder or directory()) / FLAG).read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, ValueError):
        return None
    return flag if isinstance(flag, dict) and flag.get("schema") == SCHEMA and isinstance(flag.get("firstRed"), str) else None


def write(flag: dict[str, Any], folder: Path | None = None) -> None:
    folder = folder or directory()
    folder.mkdir(parents=True, exist_ok=True)
    temporary = folder / (FLAG + ".tmp")
    temporary.write_text(json.dumps({"schema": SCHEMA, **flag}, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, folder / FLAG)


def clear(folder: Path | None = None) -> bool:
    """True when a flag was there and is gone."""
    try:
        (folder or directory()).joinpath(FLAG).unlink()
    except FileNotFoundError:
        return False
    return True


def text(flag: dict[str, Any]) -> str:
    """The line(s) `./arcanum smoke pr` prints: what is red, since which commit, and what to do with it."""
    issue = flag.get("issue")
    stages = ", ".join(str(stage) for stage in flag.get("stages") or []) or "unknown"
    checked = str(flag.get("checked") or flag["firstRed"])
    return (f"SMOKE red on main since {str(flag['firstRed'])[:10]}, "
            + (f"#{issue}" if issue else "issue not filed yet")
            + f" (rolling smoke, last red run on {checked[:10]}; failing: {stages})\n"
            "SMOKE a failure of those stages that also fails on a clean origin/main is that issue's, not this change's: "
            "cite it instead of re-running the smoke on main")
