"""ARC-DEV-376: the path layout of the player's HD interface pack (docs/workflow/hd-pack.md#pack-layout).

Pure standard library. The pack format does not constrain where a PNG lives (docs/formats/hd-pack.md: any path under the
portable rules), so the layout is a convention kept by this tool and the pack's own `layout-map.tsv`:

    interface/<screen>/<kind>/<art>-f<NNN>.png              one frame of an ART (a frame of an ART with a folder: below)
    interface/<screen>/<kind>/<art>/f<NNN>.png              an ART the map marks `folder` (many frames: fonts, animations)
    ...-g<G> / ...-p<P> before `.png`                       only when the group or palette is not 0

`screen` is the window or area where the art is drawn, `kind` what it is (buttons, icons, tabs, gauges, panels, spells,
schematics, fonts, cursors, indicators). The map decides both per ART; this module never guesses them.
No art name, path or pixel of the original is stored here: the map is a local file in the pack.
"""
from __future__ import annotations

import re

MAP_FILE = "layout-map.tsv"
INDEX_TSV = "index.tsv"
INDEX_MD = "INDEX.md"
INDEX_FILES = (INDEX_TSV, INDEX_MD)
ROOT = "interface"
FOLDER_FRAMES = 8  # a `frames` column above this means `folder` when the map has no `folder` column

_SEGMENT = re.compile(r"^[a-z0-9_-]+(\.[a-z0-9_-]+)*$")  # docs/formats/hd-pack.md: lowercase ASCII segments with inner dots
_OLD_NAME = re.compile(r"^(?P<art>.+?)-f(?P<frame>\d+)(?:-g\d+)?(?:-p\d+)?(?:-[0-9a-f]{8,64})?\.png$")
_FOLDER_NAME = re.compile(r"^f\d+(?:-g\d+)?(?:-p\d+)?\.png$")
_MAP_COLUMNS = ("art", "screen", "kind", "folder")


class LayoutError(Exception):
    pass


class LayoutMap:
    """art -> (screen, kind, folder). Loaded from `layout-map.tsv`: a header line, then `art<TAB>screen<TAB>kind<TAB>folder`.
    The `folder` column is 0 or 1; a file with a `frames` column instead (the draft the owner reviews) takes folder = frames > 8.
    Further columns are ignored."""

    def __init__(self, rows: dict[str, tuple[str, str, bool]] | None = None):
        self.rows: dict[str, tuple[str, str, bool]] = dict(rows or {})

    @classmethod
    def parse(cls, text: str, where: str = "layout map") -> "LayoutMap":
        lines = [ln for ln in text.splitlines() if ln.strip() and not ln.startswith("#")]
        if not lines:
            raise LayoutError(f"{where}: empty")
        header = [h.strip() for h in lines[0].split("\t")]
        if header[:3] != ["art", "screen", "kind"]:
            raise LayoutError(f"{where}: the first line must be a header starting with art, screen, kind")
        col = {name: i for i, name in enumerate(header)}
        rows: dict[str, tuple[str, str, bool]] = {}
        for n, line in enumerate(lines[1:], 2):
            cells = line.split("\t")
            if len(cells) < 3:
                raise LayoutError(f"{where}: line {n} has fewer than 3 columns")
            art, screen, kind = (c.strip() for c in cells[:3])
            for label, value in (("art", art), ("screen", screen), ("kind", kind)):
                if not _SEGMENT.match(value) or len(value) > 64:
                    raise LayoutError(f"{where}: line {n}: {label} {value!r} is not a lowercase path segment [a-z0-9_-]")
            if art in rows:
                raise LayoutError(f"{where}: line {n}: art {art!r} is listed twice")
            folder = False
            try:
                if "folder" in col and col["folder"] < len(cells):
                    folder = cells[col["folder"]].strip() in ("1", "true", "yes")
                elif "frames" in col and col["frames"] < len(cells):
                    folder = int(cells[col["frames"]]) > FOLDER_FRAMES
            except ValueError as e:
                raise LayoutError(f"{where}: line {n}: frames is not a number") from e
            rows[art] = (screen, kind, folder)
        return cls(rows)

    def merged(self, other: "LayoutMap") -> "LayoutMap":
        return LayoutMap({**self.rows, **other.rows})

    def render(self) -> bytes:
        lines = ["\t".join(_MAP_COLUMNS)] + [f"{a}\t{s}\t{k}\t{1 if f else 0}" for a, (s, k, f) in sorted(self.rows.items())]
        return ("\n".join(lines) + "\n").encode()


def art_of(rel: str) -> str:
    """The ART name a pack path stands for: the old `interface/<art>-f<N>-<hash>.png` or the layout's two forms."""
    parts = rel.split("/")
    name = parts[-1]
    if len(parts) >= 2 and _FOLDER_NAME.match(name):
        return parts[-2]
    m = _OLD_NAME.match(name)
    if not m:
        raise LayoutError(f"{rel}: not a frame file name this tool knows (<art>-f<N>[-<hash>].png)")
    return m.group("art")


def suffix(entry: dict) -> str:
    out = f"f{entry.get('frame', 0):03d}"  # a portrait entry (ARC-UI-2865) has no frame: 0
    if entry.get("group"):
        out += f"-g{entry['group']}"
    if entry.get("palette"):
        out += f"-p{entry['palette']}"
    return out


def place(art: str, entry: dict, lm: LayoutMap) -> str:
    """The layout path of one entry; refuses an ART the map does not know and a path the pack format would refuse."""
    if art not in lm.rows:
        raise LayoutError(art)
    screen, kind, folder = lm.rows[art]
    base = f"{ROOT}/{screen}/{kind}"
    rel = f"{base}/{art}/{suffix(entry)}.png" if folder else f"{base}/{art}-{suffix(entry)}.png"
    segments = rel.split("/")
    if len(segments) > 8 or len(rel) > 200 or any(not _SEGMENT.match(s.removesuffix(".png")) or len(s) > 64 for s in segments):
        raise LayoutError(f"{rel}: not a portable pack path (lowercase [a-z0-9_-] segments, at most 8 segments, 200 characters)")
    return rel


def screen_kind(rel: str) -> tuple[str, str]:
    parts = rel.split("/")
    return (parts[1], parts[2]) if len(parts) >= 4 and parts[0] == ROOT else ("-", "-")


def render_index(manifest: dict) -> tuple[bytes, bytes]:
    """INDEX.tsv (one line per entry) and INDEX.md (screens > kinds > ARTs), both deterministic from the manifest."""
    scale = manifest.get("scale", 1)
    rows = []
    for e in manifest["entries"]:
        screen, kind = screen_kind(e["file"])
        try:
            art = art_of(e["file"])
        except LayoutError:
            art = e["file"]
        legacy = e.get("legacy") or {}
        lw, lh = legacy.get("width", 0), legacy.get("height", 0)
        rows.append((screen, kind, art, e.get("frame", 0), e["file"], lw, lh, lw * scale, lh * scale, e.get("slice", ""), e.get("alpha") or "", e.get("review", ""), e["sha256"][:12]))
    rows.sort(key=lambda r: (r[0], r[1], r[2], r[3], r[4]))
    head = ("screen", "kind", "art", "frame", "path", "legacy_w", "legacy_h", "hd_w", "hd_h", "slice", "alpha", "review", "sha12")
    tsv = "\n".join("\t".join(str(c) for c in r) for r in [head] + rows) + "\n"
    out = [f"# HD pack index: {manifest.get('id', '?')}", "",
           f"{len(rows)} frames, scale {scale}x. Generated by `hd_pack.py index`; edit `{MAP_FILE}` to move an ART, then run `relayout`. `{INDEX_TSV}` has one line per frame.", ""]
    screens: dict[str, dict[str, dict[str, list[tuple]]]] = {}
    for r in rows:
        screens.setdefault(r[0], {}).setdefault(r[1], {}).setdefault(r[2], []).append(r)
    for screen in sorted(screens):
        count = sum(len(v) for kinds in screens[screen].values() for v in kinds.values())
        out += [f"## {screen} ({count} frames)", ""]
        for kind in sorted(screens[screen]):
            arts = screens[screen][kind]
            out.append(f"### {screen}/{kind} ({sum(len(v) for v in arts.values())} frames)")
            out.append("")
            for art in sorted(arts):
                frames = arts[art]
                first = frames[0]
                out.append(f"- `{art}`: {len(frames)} frame{'s' if len(frames) != 1 else ''}, {first[5]}x{first[6]} Legacy, {first[7]}x{first[8]} HD")
            out.append("")
    return tsv.encode(), "\n".join(out).encode()
