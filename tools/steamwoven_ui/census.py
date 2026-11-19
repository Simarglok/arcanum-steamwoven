"""ARC-DEV-2454: read the Steamwoven interface census (docs/ui) as plain data: the arts table and the windows table.

Both files hold identifiers, sizes and verdicts only (no pixels). The kit builder (build_kit.py) makes kit.json from them and the
validator (kit_check.py) checks the manifest against them.
"""
from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
CENSUS_TSV = ROOT / "docs" / "ui" / "steamwoven-interface-census-arts.tsv"
CENSUS_MD = ROOT / "docs" / "ui" / "steamwoven-interface-census.md"

WINDOW_ROW = re.compile(r"^\| ([HGM]\d\d) \| (.+?) \| (.+?) \| (.+?) \| (.+?) \| (\d+) \| (\d+) \|$")


@dataclass(frozen=True)
class CensusArt:
    ident: str  # the art number, or "file:NAME" for the 25 files that have no message number
    name: str
    size: tuple[int, int]  # one frame, original units
    frames: int
    kind: str  # kit kind, "" for arts outside the kit
    batch: str
    windows: tuple[str, ...]
    status: str


@dataclass(frozen=True)
class CensusWindow:
    ident: str
    name: str
    anchor: str  # the census' anchor text
    arts: int


def load_arts(path: Path = CENSUS_TSV) -> list[CensusArt]:
    rows = []
    with open(path, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle, delimiter="\t"):
            width, height = row["size"].split("x")
            rows.append(CensusArt(
                ident=row["art_id"] or f"file:{row['name']}", name=row["name"], size=(int(width), int(height)),
                frames=int(row["frames"]), kind=row["kit_kind"], batch=row["batch"],
                windows=tuple(w.strip() for w in row["windows"].split(",") if w.strip()), status=row["status"]))
    return rows


def load_windows(path: Path = CENSUS_MD) -> list[CensusWindow]:
    windows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        match = WINDOW_ROW.match(line)
        if match:
            windows.append(CensusWindow(match.group(1), match.group(2), match.group(3), int(match.group(6))))
    return windows


def excluded_reason(art: CensusArt) -> str | None:
    """Why an art is not a kit part: bitmap fonts (slice #2462), network play (out of scope) and the unused extra tabs."""
    if art.kind == "font":
        return "font"
    if art.status == "out of scope":
        return "out-of-scope"
    if art.status == "unused":
        return "unused"
    return None
