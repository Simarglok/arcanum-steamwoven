"""Where the owner-local HD redraw work lives (ARC-DEV-341).

The scripts of this folder read and write only the ignored `local/` of the MAIN checkout (never a worktree's own
folder, which is empty), so a thread working in a worktree still finds the original frames and sheets.
Override with ARC_MAIN_CHECKOUT=<path of the main checkout> if the git lookup does not fit.
Nothing here is original art; the folders it names are not in git.
"""
from __future__ import annotations

import os
from pathlib import Path
import subprocess

REPO = Path(__file__).resolve().parents[3]


def main_checkout() -> Path:
    env = os.environ.get("ARC_MAIN_CHECKOUT")
    if env:
        return Path(env).expanduser().resolve()
    r = subprocess.run(["git", "-C", str(REPO), "rev-parse", "--path-format=absolute", "--git-common-dir"], capture_output=True, text=True)
    if r.returncode == 0 and r.stdout.strip():
        return Path(r.stdout.strip()).parent
    return REPO


MAIN = str(main_checkout())
LOCAL = f"{MAIN}/local"
W94 = f"{LOCAL}/res094"   # RES-094: decoded frames, census, style refs, the res094 venv
OUT = f"{LOCAL}/res095"   # RES-095: groups of sheets, results, ready/ (fit, QA, plan), pack-out/ (staging)
