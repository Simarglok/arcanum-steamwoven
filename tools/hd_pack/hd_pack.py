#!/usr/bin/env python3
"""ARC-DEV-329: safe maintenance of the player's local HD interface pack (docs/workflow/hd-pack.md).

Pure standard library; works on a pack directory only (manifest.json, the files it lists, a few
unlisted extras). Nothing here contains or produces original art; it moves local files.

  status     what the installed pack is (manifest hash, entries per folder, extras)
  verify     every manifest file present with the declared size (--deep: SHA-256), no symlinks
  plan       what a diff-only update of INSTALLED from STAGED would change (writes nothing)
  apply      that update, with a backup outside hd-packs and a receipt (--dry-run first)
  rollback   undo one apply or relayout from its backup folder (--dry-run first)
  mirror     copy missing/different files from one pack to another (the iCloud copy, ARC-DEV-349); never deletes
  stage      run the importer into a staging folder that is never an installed pack
  fonts-plan write the importer plan for the traced font frames of tools/hd_font (ARC-DEV-361); files only, no pack touched
  relayout   move every entry to its place of the layout map (interface/<screen>/<kind>/<art>-f<NNN>.png, ARC-DEV-376), with backup and receipt
  index      rewrite the pack's INDEX.md and index.tsv from its manifest and layout-map.tsv
  refresh    take edited PNGs into the manifest (new size and SHA-256 of the listed files that changed, ARC-DEV-376)

Every command that writes takes --dry-run, which prints the same plan and changes nothing.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import time

try:
    from tools.hd_pack import layout
except ImportError:  # run as a script: python3 tools/hd_pack/hd_pack.py
    import layout  # type: ignore[no-redef]

GAME_SUPPORT = Path.home() / "Library" / "Application Support" / "Arcanum Steamwoven Game"
PACK_ID = "interface-redraw-4x"
DEFAULT_INSTALLED = GAME_SUPPORT / "hd-packs" / PACK_ID
DEFAULT_BACKUPS = GAME_SUPPORT / "hd-pack-backups"
# ARC-DEV-349: the cross-device copy lives in the owner's Obsidian vault in iCloud (it replaced Google Drive); local/hd-packs links to its hd_pack/
DEFAULT_COPY = Path.home() / "Library" / "Mobile Documents" / "iCloud~md~obsidian" / "Documents" / "Arcanum Steamwoven" / "hd_pack" / PACK_ID
DEFAULT_MODULE = Path("/Applications/Arcanum/modules/Arcanum.dat")
REPO = Path(__file__).resolve().parents[2]
REPLACEABLE = "interface/"  # only entries whose file lives here are replaced; other folders are extras of other tasks (unless --folders names them)
IGNORED = {".DS_Store"}


class PackError(Exception):
    pass


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_manifest(pack: Path) -> tuple[dict, str]:
    path = pack / "manifest.json"
    try:
        raw = path.read_bytes()
        manifest = json.loads(raw.decode("utf-8"))
    except (OSError, ValueError) as e:
        raise PackError(f"{pack}: manifest.json is unreadable ({e})") from e
    if manifest.get("kind") != "arcanum-steamwoven-hd-pack" or not isinstance(manifest.get("entries"), list):
        raise PackError(f"{pack}: manifest.json is not an HD pack manifest")
    return manifest, hashlib.sha256(raw).hexdigest()


MAP_KINDS = ("worldmap", "map-zoomed")  # ARC-RENDER-3079: the entry kinds of the world map's 64 tiles and of Map_Zoomed
MAP_ROW = ("world-map", "maps", False)  # their default layout row: interface/world-map/maps/<art>-f000.png
TOWN_KIND = "townmap"  # ARC-RENDER-3106: the entry kind of a town-map tile redraw (docs/formats/hd-pack.md#town-map-entries)


def entry_key(e: dict) -> tuple:
    if e.get("kind") == "portrait":  # ARC-UI-2865: a portrait redraw is keyed by the original bitmap's path (docs/formats/hd-pack.md#portrait-entries)
        return ("portrait", e["portrait"]["path"])
    if e.get("kind") == TOWN_KIND:  # ARC-RENDER-3106: a town-map tile redraw is keyed by the original tile's path (docs/formats/hd-pack.md#town-map-entries)
        return (TOWN_KIND, e[TOWN_KIND]["path"])
    if e.get("kind") in MAP_KINDS:  # ARC-RENDER-3079: a world-map bitmap redraw is keyed by the original bitmap's path too (docs/formats/hd-pack.md#world-map-entries)
        return (e["kind"], e[e["kind"]]["path"])
    return (e["art"]["sha256"], e["group"], e["frame"], e["palette"])


def keyed(manifest: dict, where: str) -> dict:
    out: dict = {}
    for e in manifest["entries"]:
        k = entry_key(e)
        if k in out:
            raise PackError(f"{where}: duplicate entry key for {e['file']}")
        out[k] = e
    return out


def safe_rel(rel: str) -> str:
    if rel.startswith("/") or "\\" in rel or ".." in rel.split("/") or not rel.endswith(".png"):
        raise PackError(f"unsafe manifest file path: {rel}")
    return rel


def snapshot(pack: Path) -> dict[str, tuple[int, int]]:
    snap = {}
    for root, dirs, files in os.walk(pack):
        for name in files:
            if name in IGNORED:
                continue
            p = Path(root) / name
            st = p.lstat()
            snap[p.relative_to(pack).as_posix()] = (st.st_size, st.st_mtime_ns)
    return snap


def symlinks_in(pack: Path) -> list[str]:
    found = []
    for root, dirs, files in os.walk(pack):
        for name in dirs + files:
            p = Path(root) / name
            if p.is_symlink():
                found.append(p.relative_to(pack).as_posix())
    return found


def guard_installed(installed: Path, allow_symlink: bool) -> None:
    if installed.is_symlink() and not allow_symlink:
        raise PackError(f"{installed} is a symbolic link (a link into a cloud folder?); give the real folder or --allow-symlink")
    if not (installed / "manifest.json").is_file():
        raise PackError(f"{installed}: no manifest.json")
    inner = symlinks_in(installed)
    if inner:
        raise PackError(f"{installed}: symbolic links inside the pack (the game refuses them): {inner[:3]}")


def guard_backup(backup_root: Path, installed: Path) -> None:
    real_b, real_i = backup_root.resolve(), installed.resolve()
    for guarded in (real_i, real_i.parent):  # the pack and its collection, where the game looks for packs
        if real_b == guarded or guarded in real_b.parents:
            raise PackError(f"backup folder {backup_root} must be outside {guarded} (the game would see it as a pack)")


def atomic_copy(src: Path, dst: Path, preserve: bool = True) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    tmp = dst.with_name(f".{dst.name}.part")
    (shutil.copy2 if preserve else shutil.copyfile)(src, tmp)
    os.replace(tmp, dst)


def atomic_write(dst: Path, data: bytes) -> None:
    tmp = dst.with_name(f".{dst.name}.part")
    with open(tmp, "wb") as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, dst)


class Lock:
    """One writer at a time (Claude and Codex may both run); a lock of a dead process is taken over."""

    def __init__(self, root: Path):
        self.path = root / ".hd-pack.lock"

    def __enter__(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        for _ in range(2):
            try:
                fd = os.open(self.path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            except FileExistsError:
                try:
                    pid = int(self.path.read_text().split()[0])
                    os.kill(pid, 0)
                except (ValueError, IndexError, ProcessLookupError):
                    self.path.unlink(missing_ok=True)  # stale
                    continue
                except PermissionError:
                    pass
                raise PackError(f"another hd_pack write is running ({self.path}); wait for it, never delete the lock while it runs")
            with os.fdopen(fd, "w") as f:
                f.write(f"{os.getpid()} {time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}\n")
            return self
        raise PackError(f"cannot take {self.path}")

    def __exit__(self, *exc):
        try:
            if self.path.read_text().split()[0] == str(os.getpid()):
                self.path.unlink(missing_ok=True)
        except (OSError, IndexError):
            pass


# ---- verify / status -------------------------------------------------------------------------

def verify(pack: Path, deep: bool) -> tuple[list[str], dict]:
    manifest, msha = load_manifest(pack)
    problems = [f"symbolic link: {s}" for s in symlinks_in(pack)]
    listed = set()
    for e in manifest["entries"]:
        rel = safe_rel(e["file"])
        listed.add(rel)
        p = pack / rel
        if not p.is_file():
            problems.append(f"missing: {rel}")
        elif p.stat().st_size != e["bytes"]:
            problems.append(f"size {p.stat().st_size} != {e['bytes']}: {rel}")
        elif deep and sha256(p) != e["sha256"]:
            problems.append(f"sha256 mismatch: {rel}")
    recipe = manifest.get("recipe", {}).get("file")
    if recipe:
        listed.add(recipe)
        if not (pack / recipe).is_file():
            problems.append(f"missing recipe: {recipe}")
    files = set(snapshot(pack)) - {".hd-pack.lock"}
    info = {"manifestSha256": msha, "entries": len(manifest["entries"]), "id": manifest.get("id"), "scale": manifest.get("scale"),
            "perFolder": {}, "unlisted": sorted(files - listed - {"manifest.json"})}
    laid_out = (pack / layout.MAP_FILE).is_file()
    for e in manifest["entries"]:
        folder = "/".join(e["file"].split("/")[:2]) if laid_out else e["file"].split("/")[0]
        info["perFolder"][folder] = info["perFolder"].get(folder, 0) + 1
    return problems, info


def cmd_status(a) -> int:
    problems, info = verify(a.installed, a.deep)
    print(f"pack {a.installed}\n  id {info['id']} scale {info['scale']} entries {info['entries']} manifest sha256 {info['manifestSha256']}")
    print("  entries per folder:", ", ".join(f"{k} {v}" for k, v in sorted(info["perFolder"].items())))
    unlisted = info["unlisted"]
    print(f"  files not in the manifest ({len(unlisted)}):", ", ".join(unlisted[:8]) + (" ..." if len(unlisted) > 8 else ""))
    print(f"  integrity ({'deep' if a.deep else 'size'}): " + ("OK" if not problems else f"{len(problems)} problems, first: {problems[0]}"))
    return 1 if problems else 0


def cmd_verify(a) -> int:
    problems, info = verify(a.installed, a.deep)
    for p in problems[:40]:
        print("FAIL", p)
    print(f"{'FAIL' if problems else 'PASS'} verify ({'deep' if a.deep else 'size'}): {info['entries']} entries, manifest {info['manifestSha256'][:12]}, "
          f"{len(info['unlisted'])} unlisted files, {len(problems)} problems")
    return 1 if problems else 0


# ---- plan / apply / rollback -----------------------------------------------------------------

def load_base(path: Path | None) -> dict | None:
    """The manifest the staging build was last synchronised with: a pack folder or a manifest.json (a backup's staged-manifest.json)."""
    if path is None:
        return None
    file = path / "manifest.json" if path.is_dir() else path
    try:
        manifest = json.loads(file.read_text())
        return keyed(manifest, f"base {file}")
    except (OSError, ValueError, KeyError, TypeError) as e:
        raise PackError(f"--base {path} is not a readable pack manifest ({e})") from e


def folder_prefixes(names: str | None) -> tuple[str, ...]:
    """`--folders character-hd114,refinement02`: extra folders of the installed pack whose entries a repair may replace (ARC-DEV-345).
    `interface/` is always replaceable; a name is one plain folder, never a path."""
    out = [REPLACEABLE]
    for name in (names or "").split(","):
        name = name.strip()
        if not name:
            continue
        if name in (".", "..") or "/" in name or "\\" in name or name.startswith("-"):
            raise PackError(f"--folders: {name!r} is not a plain folder name of the pack")
        if name + "/" not in out:
            out.append(name + "/")
    return tuple(out)


def slice_names(names: str | None) -> tuple[str, ...]:
    """`--slices character-hd114,interface-refinement02-soft`: extra slices a laid-out pack may replace besides `interface` (the Codex repairs, ARC-DEV-376)."""
    out = ["interface"]
    for name in (names or "").split(","):
        name = name.strip()
        if not name:
            continue
        if not all(c.isalnum() or c in "-_" for c in name):
            raise PackError(f"--slices: {name!r} is not a slice name")
        if name not in out:
            out.append(name)
    return tuple(out)


def load_map_file(path: Path) -> "layout.LayoutMap":
    try:
        return layout.LayoutMap.parse(path.read_text(encoding="utf-8"), str(path))
    except (OSError, ValueError, layout.LayoutError) as e:
        raise PackError(f"layout map {path}: {e}") from e


def pack_map(pack: Path) -> "layout.LayoutMap | None":
    """The pack's own layout map, or None for a pack still in the old folder layout (character-hd114/, interface/ flat)."""
    return load_map_file(pack / layout.MAP_FILE) if (pack / layout.MAP_FILE).is_file() else None


def portrait_rows(keyed_entries: dict) -> dict:
    """The default layout rows (screen `portraits`, kind `heroes` or `npcs`) of the portrait entries among `keyed_entries`, by the ART name `art_of` finds in their staged file."""
    rows = {}
    for e in keyed_entries.values():
        if e.get("kind") == "portrait":
            stem = e["portrait"]["path"].rsplit("/", 1)[-1].removesuffix(".bmp")
            try:
                rows[layout.art_of(e["file"])] = ("portraits", "npcs" if stem.startswith("npc") else "heroes", False)
            except layout.LayoutError as err:
                raise PackError(str(err)) from err
    return rows


def map_rows(keyed_entries: dict) -> dict:
    """ARC-RENDER-3079: the default layout row (screen `world-map`, kind `maps`) of every `worldmap` and `map-zoomed` entry among `keyed_entries`, by the ART name `art_of` finds in its staged file."""
    rows = {}
    for e in keyed_entries.values():
        if e.get("kind") in MAP_KINDS:
            try:
                rows[layout.art_of(e["file"])] = MAP_ROW
            except layout.LayoutError as err:
                raise PackError(str(err)) from err
    return rows


def town_rows(keyed_entries: dict) -> dict:
    """ARC-RENDER-3106: the default layout row (screen `town-map`, kind = the town's file name part) of every `townmap` entry among `keyed_entries`, by the ART name `art_of` finds in its staged
    file (`townmap-<town>-NNNNNN`): the thousands of tiles of one town share a folder, interface/town-map/<town>/."""
    rows = {}
    for e in keyed_entries.values():
        if e.get("kind") == TOWN_KIND:
            try:
                art = layout.art_of(e["file"])
            except layout.LayoutError as err:
                raise PackError(str(err)) from err
            m = re.fullmatch(r"townmap-(?P<town>.+)-\d{6}", art)
            if not m:
                raise PackError(f"{e['file']}: not a town-map tile file name (townmap-<town>-NNNNNN-f0-<hash>.png)")
            rows[art] = ("town-map", m["town"], False)
    return rows


def place_entry(e: dict, lm: "layout.LayoutMap") -> str:
    try:
        return layout.place(layout.art_of(e["file"]), e, lm)
    except layout.LayoutError as err:
        if str(err) == layout.art_of(e["file"]):
            raise PackError(f"the layout map has no row for the ART {err}; add `{err}<TAB>screen<TAB>kind<TAB>folder` to {layout.MAP_FILE} (or pass --map FILE with it)") from None
        raise PackError(str(err)) from err


def write_index(pack: Path, manifest: dict) -> list[str]:
    tsv, md = layout.render_index(manifest)
    atomic_write(pack / layout.INDEX_TSV, tsv)
    atomic_write(pack / layout.INDEX_MD, md)
    return list(layout.INDEX_FILES)


def append_map_rows(path: Path, rows: dict) -> bytes:
    """The pack's own map file with the new rows appended, every other byte kept (comments, notes the owner wrote)."""
    raw = path.read_bytes()
    header = [h.strip() for h in raw.decode("utf-8").splitlines()[0].split("\t")] if raw else []
    if header[:4] != ["art", "screen", "kind", "folder"]:
        raise PackError(f"{path} has no header art, screen, kind, folder as its first four columns; cannot add rows to it (fix the header, or add the rows by hand)")
    tail = b"" if raw.endswith(b"\n") else b"\n"
    return raw + tail + "".join(f"{a}\t{s}\t{k}\t{1 if f else 0}\n" for a, (s, k, f) in sorted(rows.items())).encode()


def link_or_copy(src: Path, dst: Path) -> None:
    """A second name for the same bytes: a hard link where the file system allows it (no extra space), else a copy."""
    dst.parent.mkdir(parents=True, exist_ok=True)
    tmp = dst.with_name(f".{dst.name}.part")
    tmp.unlink(missing_ok=True)
    try:
        os.link(src, tmp)
    except OSError:
        shutil.copy2(src, tmp)
    os.replace(tmp, dst)


def prune_empty_dirs(root: Path, rels: list[str]) -> list[str]:
    """Remove the directories that the given (removed) files leave empty, deepest first; a lone .DS_Store goes with its folder."""
    dirs = set()
    for rel in rels:
        for parent in Path(rel).parents:
            if str(parent) != ".":
                dirs.add(parent.as_posix())
    gone = []
    for d in sorted(dirs, key=lambda x: -x.count("/")):
        p = root / d
        if not p.is_dir() or p.is_symlink():
            continue
        left = [c.name for c in p.iterdir()]
        if left == [".DS_Store"]:
            (p / ".DS_Store").unlink()
            left = []
        if not left:
            p.rmdir()
            gone.append(d)
    return gone


def compute_plan(installed: Path, staged: Path, add_new: bool, base: dict | None = None, conflicts: str = "refuse",
                 folders: tuple[str, ...] = (REPLACEABLE,), extra_map: "layout.LayoutMap | None" = None, slices: tuple[str, ...] = ("interface",)) -> dict:
    """Three-way: an entry is replaced only when the installed file is still the base's (nobody repaired it locally) and the
    staging build differs. Without a base nothing can tell a stale staging from a repair, so every difference is taken and apply refuses.

    A pack with a `layout-map.tsv` (ARC-DEV-376) keeps every entry where the map puts it: a replaced entry stays at its installed path, a new
    one is placed by the map, and what may be replaced is chosen by the entry's slice (`slices`) instead of its folder (`folders`)."""
    mi, isha = load_manifest(installed)
    ms, ssha = load_manifest(staged)
    ki, ks = keyed(mi, "installed"), keyed(ms, "staged")
    lm = pack_map(installed)
    if lm is None and extra_map is not None:
        raise PackError("--map only applies to a pack with a layout-map.tsv (run relayout first)")
    pack_rows = dict(lm.rows) if lm is not None else {}
    if lm is not None:  # ARC-UI-2865: a staged portrait needs no row in the map: it goes to interface/portraits/<heroes|npcs>/ and apply appends the row
        lm = layout.LayoutMap({**portrait_rows(ks), **map_rows(ks), **town_rows(ks), **lm.rows})  # ARC-RENDER-3079: a staged world-map bitmap likewise goes to interface/world-map/maps/
    if lm is not None and extra_map is not None:  # the pack's own rows win; the file only adds ARTs the pack's map lacks
        lm = layout.LayoutMap({**{a: r for a, r in extra_map.rows.items() if a not in lm.rows}, **lm.rows})

    def replaceable(e: dict, n: dict) -> bool:
        if lm is None:
            return e["file"].startswith(folders) and n["file"].startswith(folders)
        return e.get("slice") in slices and n["file"].startswith(REPLACEABLE)

    def new_ok(n: dict) -> bool:
        return n["file"].startswith(folders) if lm is None else n["file"].startswith(REPLACEABLE)

    replace, entries, conflicted, copies = [], [], [], []
    used_arts: set = set()
    for e in mi["entries"]:
        n = ks.get(entry_key(e))
        differs = n is not None and replaceable(e, n) and n["sha256"] != e["sha256"]
        if differs and base is not None:
            b = base.get(entry_key(e))
            if b is None or b["sha256"] != e["sha256"]:  # the installed entry is not the base's: a local repair (staging equal to the base would revert it)
                conflicted.append((e, n, "repair kept by installed" if b is not None and b["sha256"] == n["sha256"] else "changed on both sides" if b is not None else "not in the base"))
                if conflicts != "overwrite":
                    entries.append(e)
                    continue
        if differs:
            final = n if lm is None else dict(n, file=e["file"])  # a laid-out pack keeps the entry where it is
            replace.append((e, final))
            copies.append((n["file"], final))
            entries.append(final)
        else:
            entries.append(e)
    new = [n for k, n in ks.items() if k not in ki and not (base is not None and k in base)]  # one the base had and the installed pack dropped stays dropped
    if add_new:
        for n in new:
            if not new_ok(n):
                continue
            final = n if lm is None else dict(n, file=place_entry(n, lm))
            if lm is not None:
                used_arts.add(layout.art_of(n["file"]))
            entries.append(final)
            copies.append((n["file"], final))
    for old, n in replace:
        safe_rel(old["file"])
        safe_rel(n["file"])
    for src, final in copies:
        safe_rel(src)
        safe_rel(final["file"])
    new_rows = {a: lm.rows[a] for a in sorted(used_arts) if lm is not None and a not in pack_rows}  # only the ARTs of the entries this update adds
    if new_rows:
        append_map_rows(installed / layout.MAP_FILE, new_rows)  # a map that cannot take rows is refused here, before anything is written
    listed_now = {e["file"] for e in mi["entries"]}
    targets = [final["file"] for _, final in copies]
    if len(set(targets)) != len(targets) or len({e["file"] for e in entries}) != len(entries):
        raise PackError("two entries would land on the same file path; check the layout map")
    for src, final in copies:  # a staged file must exist and match before anything is touched; a target must not be an unlisted file
        if final["file"] not in listed_now and (installed / final["file"]).exists():
            raise PackError(f"{final['file']} exists in the installed pack but is not in its manifest; refusing to overwrite it (move it away first)")
        p = staged / src
        if not p.is_file() or p.stat().st_size != final["bytes"] or sha256(p) != final["sha256"]:
            raise PackError(f"staged file is missing or does not match its manifest entry: {src}")
    manifest = dict(mi, entries=entries)
    new_manifest = json.dumps(manifest, separators=(",", ":")).encode()
    return {"old": mi, "oldSha": isha, "stagedSha": ssha, "replace": replace, "conflicts": conflicted, "baseGiven": base is not None, "conflictMode": conflicts,
            "stagedManifest": json.dumps(ms, separators=(",", ":")).encode(), "copies": copies, "newEntries": new, "added": add_new,
            "manifestBytes": new_manifest, "newManifestSha": hashlib.sha256(new_manifest).hexdigest(), "map": lm, "newMapRows": new_rows,
            "keptExtra": sum(1 for e in mi["entries"] if not e["file"].startswith(folders)) if lm is None else sum(1 for e in mi["entries"] if e.get("slice") not in slices),
            "folders": folders, "slices": slices,
            "stagedOnly": len(new), "installedOnly": sum(1 for k in ki if k not in ks)}


def print_plan(plan: dict, verb: str) -> None:
    print(f"installed manifest {plan['oldSha'][:12]} -> {plan['newManifestSha'][:12]} (staged build {plan['stagedSha'][:12]})")
    extra = [f.rstrip("/") for f in plan["folders"] if f != REPLACEABLE] if plan["map"] is None else [x for x in plan["slices"] if x != "interface"]
    where = "interface entries" if not extra else "entries (interface and " + ", ".join(extra) + ")"
    print(f"  {len(plan['replace'])} {where} differ and {verb} replaced; {plan['keptExtra']} entries of other folders and all unlisted files stay")
    print(f"  staged-only entries: {plan['stagedOnly']} ({'added' if plan['added'] else 'ignored; --add-new adds them'}); installed-only entries kept: {plan['installedOnly']}")
    if plan["map"] is not None:
        print(f"  layout map: entries keep their place; {len(plan['copies']) - len(plan['replace'])} new entries are placed by it (slices that may be replaced: {', '.join(plan['slices'])})")
    for old, new in plan["replace"][:12]:
        print(f"  replace {old['file']} -> {new['file']}")
    if len(plan["replace"]) > 12:
        print(f"  ... and {len(plan['replace']) - 12} more")
    if not plan["baseGiven"]:
        print("  WARNING: no --base, so a local repair in the installed pack cannot be told from a stale staging build; the differences above"
              " may revert repairs. Give --base (the manifest the staging was last applied from; see docs/workflow/hd-pack.md).")
    for old, new, why in plan["conflicts"][:20]:
        print(f"  CONFLICT {old['file']}: {why}; " + ("overwritten (--conflicts overwrite)" if plan["conflictMode"] == "overwrite" else "kept as installed"))


def cmd_plan(a) -> int:
    guard_installed(a.installed, True)
    print_plan(compute_plan(a.installed, a.staged, a.add_new, load_base(a.base), a.conflicts, folder_prefixes(a.folders), load_map_file(a.map) if a.map else None, slice_names(a.slices)), "would be")
    return 0


def cmd_apply(a) -> int:
    guard_installed(a.installed, a.allow_symlink)
    guard_backup(a.backup_root, a.installed)
    plan = compute_plan(a.installed, a.staged, a.add_new, load_base(a.base), a.conflicts, folder_prefixes(a.folders), load_map_file(a.map) if a.map else None, slice_names(a.slices))
    backup = a.backup_root / f"{a.task}-{plan['oldSha'][:12]}"
    print_plan(plan, "would be" if a.dry_run else "will be")
    if backup.exists():
        raise PackError(f"backup folder exists, refusing to reuse it: {backup}")
    if not plan["baseGiven"] and plan["copies"] and not a.no_base_check:
        raise PackError("no --base: a local repair cannot be told from a stale staging build. Give --base MANIFEST, or --no-base-check if you know the installed entries are untouched")
    if plan["conflicts"] and a.conflicts == "refuse":
        raise PackError(f"{len(plan['conflicts'])} entries were repaired in the installed pack after the base (listed above); nothing was changed. Rebuild staging from the repaired pack, or pass --conflicts skip to apply only the rest, or --conflicts overwrite to revert them on purpose")
    if not plan["copies"]:
        print("nothing to do")
        return 0
    if a.dry_run:
        print(f"DRY-RUN: would back up the manifest and {len(plan['replace'])} old files to {backup}, copy {len(plan['copies'])} files, write the manifest last")
        return 0
    inst = a.installed
    with Lock(inst.parent):
        before = snapshot(inst)
        if sha256(inst / "manifest.json") != plan["oldSha"]:
            raise PackError("the installed manifest changed while planning; run again")
        pre, _ = verify(inst, deep=False)
        if pre:
            raise PackError(f"the installed pack is already damaged ({pre[0]}; {len(pre)} problems); repair it first, nothing was changed")
        (backup / "interface").mkdir(parents=True)
        shutil.copy2(inst / "manifest.json", backup / "manifest.json")
        removed = []
        for old, new in plan["replace"]:
            if (inst / old["file"]).is_file():
                atomic_copy(inst / old["file"], backup / old["file"])
            removed.append({"file": old["file"], "sha256": old["sha256"]})
        atomic_write(backup / "staged-manifest.json", plan["stagedManifest"])  # the --base of the next update from this staging line
        receipt = {"kind": "update", "task": a.task, "createdUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "status": "started",
                   "oldManifestSha256": plan["oldSha"], "newManifestSha256": plan["newManifestSha"], "stagedManifestSha256": plan["stagedSha"],
                   "replaced": removed, "added": [final["file"] for _, final in plan["copies"]],
                   "mapBackup": bool(plan["newMapRows"])}
        if receipt["mapBackup"] and (inst / layout.MAP_FILE).is_file():
            atomic_copy(inst / layout.MAP_FILE, backup / layout.MAP_FILE)
        atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
        gone: list[str] = []
        extras: list[str] = []
        try:
            for src, final in plan["copies"]:
                atomic_copy(a.staged / src, inst / final["file"])
            if plan["newMapRows"]:  # new ARTs were named by --map: the pack's own map gets their rows (appended; nothing else in the file changes)
                atomic_write(inst / layout.MAP_FILE, append_map_rows(inst / layout.MAP_FILE, plan["newMapRows"]))
                extras.append(layout.MAP_FILE)
            atomic_write(inst / "manifest.json", plan["manifestBytes"])
            if plan["map"] is not None:
                extras += write_index(inst, json.loads(plan["manifestBytes"]))
            referenced = {e["file"] for e in json.loads(plan["manifestBytes"])["entries"]}
            gone = [r["file"] for r in removed if r["file"] not in referenced and (inst / r["file"]).is_file()]
            for rel in gone:
                (inst / rel).unlink()
        except (OSError, KeyboardInterrupt) as e:
            receipt["status"] = "failed"
            atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
            raise PackError(f"apply was interrupted ({e!r}); the pack may be half updated. Roll back with: python3 -B tools/hd_pack/hd_pack.py rollback {backup} --installed {inst}") from e
        after = snapshot(inst)
        allowed = {"manifest.json"} | set(receipt["added"]) | set(gone) | set(extras)
        stray = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k) and k not in allowed)
        problems, info = verify(inst, deep=False)
        bad = [p for p in problems] + [f"changed outside the plan: {k}" for k in stray]
        written = set(receipt["added"])
        for e in json.loads(plan["manifestBytes"])["entries"]:  # every entry on a written file, replaced or not
            if e["file"] in written and sha256(inst / e["file"]) != e["sha256"]:
                bad.append(f"sha256 mismatch after copy: {e['file']}")
        if sha256(inst / "manifest.json") != plan["newManifestSha"]:
            bad.append("manifest hash differs from the plan")
        receipt["status"] = "applied" if not bad else "failed"
        atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
    for b in bad[:20]:
        print("FAIL", b)
    if not bad and (a.base is None or (a.base.resolve() != inst.resolve() and a.base.resolve() != (inst / "manifest.json").resolve())):
        print(f"next time use: --base {backup / 'staged-manifest.json'}")
    print(f"{'FAIL' if bad else 'PASS'} apply: {len(plan['copies'])} files written, {len(gone)} old files removed, manifest {plan['newManifestSha'][:12]}, backup {backup}")
    if bad:
        print(f"roll back with: python3 -B tools/hd_pack/hd_pack.py rollback {backup} --installed {inst}")
    return 1 if bad else 0


def cmd_rollback(a) -> int:
    guard_installed(a.installed, a.allow_symlink)
    try:
        receipt = json.loads((a.backup / "RECEIPT.json").read_text())
    except (OSError, ValueError) as e:
        raise PackError(f"{a.backup}: no readable RECEIPT.json ({e})") from e
    inst = a.installed
    if receipt.get("kind") == "refresh":
        raise PackError("a refresh cannot be rolled back: the edited PNGs are the only copy of your edits. Put an older PNG back from your own copy and run refresh again")
    if receipt.get("kind") == "menu":
        raise PackError("a menu-art receipt cannot be rolled back: hd_pack.py no longer installs main-menu-16x9.png (ARC-DEV-3030). The game reads that file (ARC-UI-3483): restore it by hand if you want the older one back")
    if receipt.get("status") not in ("applied", "failed", "started", "rolling-back", "rolled-back") or receipt.get("kind") not in ("update", "relayout"):
        raise PackError("receipt has an unknown kind or status")
    if receipt["kind"] == "relayout":
        return rollback_relayout(a, receipt)
    current = sha256(inst / "manifest.json")
    interrupted = receipt["status"] in ("started", "failed") and current == receipt["oldManifestSha256"]  # files copied, manifest not yet
    if current != receipt["newManifestSha256"] and not interrupted:
        raise PackError("the installed manifest is not the one this update produced (another update followed); refusing to roll back over it")
    if sha256(a.backup / "manifest.json") != receipt["oldManifestSha256"]:
        raise PackError("the backup manifest does not match its receipt")
    old = receipt["replaced"]
    for r in old:
        src = a.backup / r["file"]
        if not src.is_file() or sha256(src) != r["sha256"]:
            if not (inst / r["file"]).is_file() or sha256(inst / r["file"]) != r["sha256"]:
                raise PackError(f"backup lacks a valid copy of {r['file']}")
    print(f"rollback update {receipt['task']}: restore manifest {receipt['oldManifestSha256'][:12]}, {len(old)} old files; drop the {len(receipt['added'])} added files not in the old manifest")
    if a.dry_run:
        print("DRY-RUN: nothing changed")
        return 0
    with Lock(inst.parent):
        for r in old:
            if not (inst / r["file"]).is_file() or sha256(inst / r["file"]) != r["sha256"]:
                atomic_copy(a.backup / r["file"], inst / r["file"])
        atomic_write(inst / "manifest.json", (a.backup / "manifest.json").read_bytes())
        keep = {e["file"] for e in json.loads((a.backup / "manifest.json").read_text())["entries"]}
        for rel in receipt["added"]:
            if rel not in keep and (inst / rel).is_file():
                (inst / rel).unlink()
        if receipt.get("mapBackup") and (a.backup / layout.MAP_FILE).is_file():
            atomic_copy(a.backup / layout.MAP_FILE, inst / layout.MAP_FILE)
        if (inst / layout.MAP_FILE).is_file():
            write_index(inst, json.loads((a.backup / "manifest.json").read_text()))
        if (inst / layout.MAP_FILE).is_file():  # a laid-out pack has no folders of its own to keep; an old-layout pack keeps its folders
            prune_empty_dirs(inst, [rel for rel in receipt["added"] if rel not in keep])
    problems, _ = verify(inst, deep=False)
    for p in problems[:20]:
        print("FAIL", p)
    ok = not problems and sha256(inst / "manifest.json") == receipt["oldManifestSha256"]
    print(f"{'PASS' if ok else 'FAIL'} rollback")
    return 0 if ok else 1


# ---- relayout / index / refresh (ARC-DEV-376) --------------------------------------------------

def relayout_plan(inst: Path, lm: "layout.LayoutMap") -> dict:
    manifest, msha = load_manifest(inst)
    old_paths = {e["file"] for e in manifest["entries"]}
    entries, moves, targets = [], [], {}
    for e in manifest["entries"]:
        target = safe_rel(place_entry(e, lm))
        if target in targets:
            raise PackError(f"{e['file']} and {targets[target]} would both land on {target}; check the layout map")
        targets[target] = e["file"]
        if target != e["file"]:
            if target in old_paths:
                raise PackError(f"{target} is the current path of another entry; a swap of two entries cannot be done in one step (move one aside first)")
            if (inst / target).exists():
                raise PackError(f"{target} exists in the pack but is not in its manifest; refusing to overwrite it (move it away first)")
            moves.append((e["file"], target, e["sha256"]))
        entries.append(dict(e, file=target))
    new_manifest = dict(manifest, entries=entries)
    raw = json.dumps(new_manifest, separators=(",", ":")).encode()
    return {"old": manifest, "oldSha": msha, "new": new_manifest, "manifestBytes": raw, "newSha": hashlib.sha256(raw).hexdigest(), "moves": moves}


# The packaged game's app names: the player package and the profiling package (ARC-DEV-3379), which reads packs the same way.
GAME_APPS = ("Arcanum Steamwoven", "Arcanum Steamwoven Profiling")


def pack_users(pack: Path) -> list[str]:
    """Processes that name the pack folder in their command line (a viewer or smoke started with ARC_HD_PACK=..., a launcher run on it) or are the packaged game:
    a relayout moves every file, so a reader running at the same time would see missing frames. Best effort: the environment of a process is not read."""
    try:
        out = subprocess.run(["ps", "-axo", "pid=,args="], capture_output=True, text=True, timeout=20).stdout
    except (OSError, subprocess.SubprocessError):
        return []
    needles = {str(pack), str(pack.resolve())}
    found = []
    for line in out.splitlines():
        pid, _, args = line.strip().partition(" ")
        if pid == str(os.getpid()) or "hd_pack.py" in args:
            continue
        if any(n in args for n in needles) or any(f"{app}.app/Contents/MacOS" in args for app in GAME_APPS):
            found.append(f"{pid} {args[:110]}")
    return found


def cmd_relayout(a) -> int:
    guard_installed(a.installed, a.allow_symlink)
    guard_backup(a.backup_root, a.installed)
    inst = a.installed
    lm = load_map_file(a.map) if a.map else pack_map(inst)
    if lm is None:
        raise PackError(f"no layout map: pass --map FILE (columns art, screen, kind, folder) or put {layout.MAP_FILE} in the pack")
    plan = relayout_plan(inst, lm)
    backup = a.backup_root / f"{a.task}-{plan['oldSha'][:12]}"
    screens = {}
    for e in plan["new"]["entries"]:
        screens[layout.screen_kind(e["file"])[0]] = screens.get(layout.screen_kind(e["file"])[0], 0) + 1
    print(f"relayout {inst}: manifest {plan['oldSha'][:12]} -> {plan['newSha'][:12]}, {len(plan['moves'])} of {len(plan['old']['entries'])} entries move; no PNG changes")
    print("  frames per screen:", ", ".join(f"{k} {v}" for k, v in sorted(screens.items())))
    for old, new, _ in plan["moves"][:6]:
        print(f"  move {old} -> {new}")
    if backup.exists():
        raise PackError(f"backup folder exists, refusing to reuse it: {backup}")
    own = (inst / layout.MAP_FILE).is_file() and not a.map  # without --map the pack's own map is the source and stays byte for byte
    index_ok = (inst / layout.INDEX_TSV).is_file() and (inst / layout.INDEX_TSV).read_bytes() == layout.render_index(plan["new"])[0]
    if not plan["moves"] and index_ok and (own or ((inst / layout.MAP_FILE).is_file() and (inst / layout.MAP_FILE).read_bytes() == lm.render())):
        print("nothing to do")
        return 0
    if a.dry_run:
        users = pack_users(inst)
        if users:
            print(f"WARNING: {len(users)} process(es) name this pack; the real run refuses until they end:", *users[:4], sep="\n  ")
        print(f"DRY-RUN: would back up the manifest and a receipt of the {len(plan['moves'])} moves to {backup}, link or copy every file to its new path, write {layout.MAP_FILE}, "
              f"{', '.join(layout.INDEX_FILES)} and the manifest, then remove the old paths and the folders they leave empty")
        return 0
    users = pack_users(inst)
    if users and not a.allow_running:
        raise PackError("a process is using this pack (close it, or pass --allow-running when it is known not to read frames):\n  " + "\n  ".join(users[:6]))
    with Lock(inst.parent):
        before = snapshot(inst)
        if sha256(inst / "manifest.json") != plan["oldSha"]:
            raise PackError("the installed manifest changed while planning; run again")
        pre, _ = verify(inst, deep=True)
        if pre:
            raise PackError(f"the installed pack is already damaged ({pre[0]}; {len(pre)} problems); repair it first, nothing was changed")
        backup.mkdir(parents=True)
        shutil.copy2(inst / "manifest.json", backup / "manifest.json")
        extras_before = {}
        for name in (layout.MAP_FILE, *layout.INDEX_FILES):
            if (inst / name).is_file():
                extras_before[name] = sha256(inst / name)
                atomic_copy(inst / name, backup / name)
        receipt = {"kind": "relayout", "task": a.task, "createdUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "status": "started",
                   "oldManifestSha256": plan["oldSha"], "newManifestSha256": plan["newSha"], "moves": [list(m) for m in plan["moves"]],
                   "extrasBefore": extras_before}
        atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
        gone: list[str] = []
        try:
            for old, new, _ in plan["moves"]:
                link_or_copy(inst / old, inst / new)
            if not own:
                atomic_write(inst / layout.MAP_FILE, lm.render())  # the draft or an edited copy, normalised to the four columns
            write_index(inst, plan["new"])
            atomic_write(inst / "manifest.json", plan["manifestBytes"])
            gone = [old for old, _, _ in plan["moves"] if (inst / old).is_file()]
            for rel in gone:
                (inst / rel).unlink()
            pruned = prune_empty_dirs(inst, gone)
        except (OSError, KeyboardInterrupt) as e:
            receipt["status"] = "failed"
            atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
            raise PackError(f"relayout was interrupted ({e!r}); the pack may be half moved. Roll back with: python3 -B tools/hd_pack/hd_pack.py rollback {backup} --installed {inst}") from e
        after = snapshot(inst)
        allowed = {"manifest.json", layout.MAP_FILE, *layout.INDEX_FILES} | {n for _, n, _ in plan["moves"]} | set(gone)
        stray = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k) and k not in allowed)
        problems, _ = verify(inst, deep=True)
        bad = problems + [f"changed outside the plan: {k}" for k in stray]
        if sha256(inst / "manifest.json") != plan["newSha"]:
            bad.append("manifest hash differs from the plan")
        receipt["status"] = "applied" if not bad else "failed"
        atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
    for b in bad[:20]:
        print("FAIL", b)
    print(f"{'FAIL' if bad else 'PASS'} relayout: {len(plan['moves'])} files moved, {len(pruned)} old folders removed, manifest {plan['newSha'][:12]}, backup {backup}")
    if bad:
        print(f"roll back with: python3 -B tools/hd_pack/hd_pack.py rollback {backup} --installed {inst}")
    return 1 if bad else 0


def rollback_relayout(a, receipt: dict) -> int:
    inst, backup = a.installed, a.backup
    current = sha256(inst / "manifest.json")
    status = receipt["status"]
    resumable = status in ("started", "failed", "rolling-back")  # an interrupted relayout or an interrupted rollback: every step below can run again
    if status == "rolled-back" or current not in (receipt["newManifestSha256"], receipt["oldManifestSha256"]) \
            or (current == receipt["oldManifestSha256"] and not resumable):
        raise PackError("the installed manifest is not the one this relayout produced (another change followed, or it was already rolled back); refusing to roll back over it")
    if sha256(backup / "manifest.json") != receipt["oldManifestSha256"]:
        raise PackError("the backup manifest does not match its receipt")
    moves = receipt["moves"]
    for old, new, digest in moves:
        have = [p for p in (inst / old, inst / new) if p.is_file() and sha256(p) == digest]
        if not have:
            raise PackError(f"neither {old} nor {new} holds the file the receipt names; cannot roll back")
    print(f"rollback relayout {receipt['task']}: restore manifest {receipt['oldManifestSha256'][:12]}, {len(moves)} files back to their old paths; remove the layout's new paths")
    if a.dry_run:
        print("DRY-RUN: nothing changed")
        return 0
    old_manifest = json.loads((backup / "manifest.json").read_text())
    keep = {e["file"] for e in old_manifest["entries"]}
    with Lock(inst.parent):
        receipt["status"] = "rolling-back"
        atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
        for old, new, digest in moves:
            if not (inst / old).is_file() or sha256(inst / old) != digest:
                link_or_copy(inst / new, inst / old)
        atomic_write(inst / "manifest.json", (backup / "manifest.json").read_bytes())  # the old paths all exist again; the new ones are only leftovers now
        removed = []
        for old, new, _ in moves:
            if new not in keep and (inst / new).is_file():
                (inst / new).unlink()
                removed.append(new)
        prune_empty_dirs(inst, removed)
        for name in (layout.MAP_FILE, *layout.INDEX_FILES):
            if name in receipt.get("extrasBefore", {}):
                atomic_copy(backup / name, inst / name)
            elif (inst / name).is_file():
                (inst / name).unlink()
        receipt["status"] = "rolled-back"
        atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
    problems, _ = verify(inst, deep=False)
    for p in problems[:20]:
        print("FAIL", p)
    ok = not problems and sha256(inst / "manifest.json") == receipt["oldManifestSha256"]
    print(f"{'PASS' if ok else 'FAIL'} rollback")
    return 0 if ok else 1


def cmd_index(a) -> int:
    guard_installed(a.installed, a.allow_symlink)
    if pack_map(a.installed) is None:
        raise PackError(f"{a.installed} has no {layout.MAP_FILE}; run relayout first")
    manifest, _ = load_manifest(a.installed)
    tsv, md = layout.render_index(manifest)
    same = all((a.installed / n).is_file() and (a.installed / n).read_bytes() == data for n, data in ((layout.INDEX_TSV, tsv), (layout.INDEX_MD, md)))
    print(f"index {a.installed}: {len(manifest['entries'])} frames; " + ("up to date" if same else "would be rewritten" if a.dry_run else "rewritten"))
    if same or a.dry_run:
        return 0
    with Lock(a.installed.parent):
        write_index(a.installed, manifest)
    return 0


def cmd_refresh(a) -> int:
    """Take PNGs the owner edited in place into the manifest: every listed file whose size or SHA-256 differs gets the new values.
    The file must still be an 8-bit RGBA PNG of exactly the entry's HD size; whether its alpha passes the entry's rule is checked by the game."""
    guard_installed(a.installed, a.allow_symlink)
    guard_backup(a.backup_root, a.installed)
    inst = a.installed
    manifest, msha = load_manifest(inst)
    scale = manifest["scale"]
    changed, entries = [], []
    for e in manifest["entries"]:
        rel = safe_rel(e["file"])
        p = inst / rel
        if not p.is_file():
            raise PackError(f"missing: {rel}; refresh only takes edits of listed files")
        size = p.stat().st_size
        digest = sha256(p)
        if size == e["bytes"] and digest == e["sha256"]:
            entries.append(e)
            continue
        w, h, depth, ctype, interlace = png_header(p)
        want = (scale * e["legacy"]["width"], scale * e["legacy"]["height"])
        if (w, h) != want or depth != 8 or ctype != 6 or interlace != 0:
            raise PackError(f"{rel}: {w}x{h} depth {depth} type {ctype} interlace {interlace}; needs 8-bit RGBA, not interlaced, exactly {want[0]}x{want[1]}")
        changed.append((rel, e["sha256"], digest))
        entries.append(dict(e, sha256=digest, bytes=size))
    print(f"refresh {inst}: {len(changed)} edited files")
    for rel, old, new in changed[:12]:
        print(f"  {rel}: {old[:12]} -> {new[:12]}")
    if not changed:
        print("nothing to do")
        return 0
    new_manifest = dict(manifest, entries=entries)
    raw = json.dumps(new_manifest, separators=(",", ":")).encode()
    backup = a.backup_root / f"{a.task}-refresh-{msha[:12]}"
    if backup.exists():
        raise PackError(f"backup folder exists, refusing to reuse it: {backup}")
    if a.dry_run:
        print(f"DRY-RUN: would back up the old manifest to {backup} and write the manifest {hashlib.sha256(raw).hexdigest()[:12]}; the PNG bytes are the owner's and stay")
        return 0
    with Lock(inst.parent):
        if sha256(inst / "manifest.json") != msha:
            raise PackError("the installed manifest changed while planning; run again")
        for rel, _, digest in changed:  # a file edited again since the hash above is not written into the manifest with a stale hash
            if sha256(inst / rel) != digest:
                raise PackError(f"{rel} changed while refreshing; run again")
        backup.mkdir(parents=True)
        shutil.copy2(inst / "manifest.json", backup / "manifest.json")
        atomic_write(backup / "RECEIPT.json", json.dumps({"kind": "refresh", "status": "applied", "task": a.task, "oldManifestSha256": msha,
                                                           "newManifestSha256": hashlib.sha256(raw).hexdigest(), "changed": [list(c) for c in changed]}, indent=1).encode())
        atomic_write(inst / "manifest.json", raw)
        if (inst / layout.MAP_FILE).is_file():
            write_index(inst, new_manifest)
    problems, _ = verify(inst, deep=False)
    for p in problems[:20]:
        print("FAIL", p)
    print(f"{'FAIL' if problems else 'PASS'} refresh: {len(changed)} entries updated, manifest {hashlib.sha256(raw).hexdigest()[:12]}, backup {backup} (the old manifest only: the edited PNGs are the new truth)")
    return 1 if problems else 0


# ---- PNG header / mirror / stage ---------------------------------------------------------------

def png_header(path: Path) -> tuple[int, int, int, int, int]:
    with open(path, "rb") as f:
        head = f.read(33)
    if len(head) < 33 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        raise PackError(f"{path} is not a PNG")
    w, h, depth, ctype, _comp, _filt, interlace = struct.unpack(">IIBBBBB", head[16:29])
    return w, h, depth, ctype, interlace


def cmd_mirror(a) -> int:
    problems, _ = verify(a.src, deep=False)
    if problems:
        raise PackError(f"the source pack is damaged ({problems[0]}; {len(problems)} problems); refusing to copy it")
    real_dst = a.dst.resolve()
    if real_dst == a.src.resolve() or GAME_SUPPORT.resolve() in (real_dst, *real_dst.parents):
        raise PackError(f"{a.dst} is the source or inside the game's own folder; mirror writes only to a copy elsewhere (iCloud), never to an installed pack")
    if a.dst.is_dir() and symlinks_in(a.dst):
        raise PackError(f"{a.dst} holds symbolic links; refusing to write through them")
    src_manifest_sha = load_manifest(a.src)[1]
    if a.dst.exists() and a.dst.is_symlink():
        print(f"note: {a.dst} is a symbolic link; writing through it to {a.dst.resolve()}")
    todo, orphans = [], []
    src_files = {k for k in snapshot(a.src) if k != ".hd-pack.lock"}
    try:  # what the destination's own manifest says its files are: a listed file with the same hash and size is not re-read
        dst_listed = {e["file"]: (e["sha256"], e["bytes"]) for e in load_manifest(a.dst)[0]["entries"]}
    except PackError:
        dst_listed = {}
    src_listed = {e["file"]: (e["sha256"], e["bytes"]) for e in load_manifest(a.src)[0]["entries"]}
    for rel in sorted(src_files):
        s, d = a.src / rel, a.dst / rel
        if not d.is_file():
            todo.append(rel)
        elif rel in src_listed and not a.deep:  # a file name is not its content hash after a local repair: compare the manifest's hash
            if dst_listed.get(rel) != src_listed[rel] or d.stat().st_size != src_listed[rel][1]:
                todo.append(rel)
        elif sha256(s) != sha256(d):
            todo.append(rel)
    if a.dst.is_dir():
        orphans = sorted(set(snapshot(a.dst)) - src_files - {".hd-pack.lock"})
    todo.sort(key=lambda r: r == "manifest.json")  # the manifest last, so a partial copy never names a missing file
    print(f"mirror {a.src} -> {a.dst}: {len(todo)} files to copy, {len(orphans)} files only in the destination (kept, never deleted)")
    for rel in todo[:12]:
        print("  copy", rel)
    if a.dry_run or not todo:
        print("DRY-RUN: nothing changed" if a.dry_run else "up to date")
        return 0
    with Lock(a.src.parent):
        if load_manifest(a.src)[1] != src_manifest_sha:
            raise PackError("the source pack changed while planning the mirror; run again")
        for rel in todo:
            atomic_copy(a.src / rel, a.dst / rel)
    bad = [r for r in todo if sha256(a.src / r) != sha256(a.dst / r)]
    for r in bad[:20]:
        print("FAIL differs after copy:", r)
    print(f"{'FAIL' if bad else 'PASS'} mirror: {len(todo)} files copied")
    return 1 if bad else 0


def cmd_stage(a) -> int:
    staging = a.staging
    real = staging.resolve()
    if staging.is_symlink() or "local" not in real.parts or "pack-out" not in real.parts or GAME_SUPPORT.resolve() in (real, *real.parents) \
            or "hd-packs" in real.parts or "hd_pack" in real.parts:  # hd_pack: the copy folder's own name, seen after resolving local/hd-packs
        raise PackError(f"{staging} must be a plain folder under a local/**/pack-out/ directory, never an installed pack, a pack collection or a link; use local/res095/pack-out/<id>")
    cmd = [str(REPO / "arcanum"), "run", "--kind", "dotnet", "--", "dotnet", "run", "--project", "tools/hd-interface-import", "--",
           "--module", str(a.module), "--plan", str(a.plan), "--pack", str(staging)]
    if staging.exists():
        manifest = staging / "manifest.json"
        if not a.fresh:
            raise PackError(f"{staging} exists; add --fresh to rebuild it (only a staging folder holding a pack manifest is ever wiped)")
        load_manifest(staging)  # refuses a folder that is not a pack
        print(f"will wipe the staging folder {staging} (manifest {manifest.stat().st_size} bytes)")
    print("importer command:", " ".join(cmd))
    if a.dry_run:
        print("DRY-RUN: nothing run")
        return 0
    if staging.exists():
        shutil.rmtree(staging)
    staging.parent.mkdir(parents=True, exist_ok=True)
    rc = subprocess.run(cmd, cwd=REPO).returncode
    print(f"{'PASS' if rc == 0 else 'FAIL'} stage (importer exit {rc}); next: plan --installed ... --staged {staging}")
    return rc


FONT_PLAN_KIND = "arcanum-steamwoven-hd-interface-plan"


def font_plan(fonts: Path, out: Path, pack_id: str, arts: set[int] | None) -> tuple[dict, dict]:
    """ARC-DEV-361: the importer plan (`ready` items with `"coverage": true`, one per font) for the font folders `tools/hd_font trace` wrote under
    FONTS. A font folder is `<art>-<name>/` with its `font.json` and `frame-NNN.png`; every PNG is checked against the SHA-256 in `font.json`
    when the plan is written (the importer reads the files later and checks only that they are grey coverage within the `masked` rule). Frames that carry no traced ink (control-character placeholders, a
    glyph that vanished in the trace) and the coloured frame 0 are left out: the viewer draws their Legacy frame. Image paths are
    relative to OUT, so the plan is movable inside `local/`."""
    items, counts, scales = [], {}, set()
    def order(p: Path) -> tuple[int, str]:  # by ART number (the folder name starts with it), so 27 comes before 229
        head = p.name.split("-")[0]
        return (int(head) if head.isdigit() else 1 << 30, p.name)

    for folder in sorted((p for p in fonts.iterdir() if p.is_dir() and (p / "font.json").is_file()), key=order):
        try:
            meta = json.loads((folder / "font.json").read_text())
            number, file, scale, frames = meta["art"], meta["file"], meta["scale"], meta["frames"]
        except (OSError, ValueError, KeyError, TypeError) as e:
            raise PackError(f"{folder}/font.json is not a hd_font font file ({e})") from e
        if meta.get("tool") != "hd_font" or not isinstance(number, int) or not isinstance(file, str) or not isinstance(scale, int) or not isinstance(frames, list):
            raise PackError(f"{folder}/font.json is not a hd_font font file")
        if arts is not None and number not in arts:
            continue
        if "/" in file or "\\" in file or not file.lower().endswith(".art"):
            raise PackError(f"{folder}/font.json: {file!r} is not an ART file name")
        scales.add(scale)
        images, tiles = [], []
        for f in frames:
            if not isinstance(f, dict):
                raise PackError(f"{folder.name}/font.json: a frame entry is not an object")
            if not (f.get("ink") and not f.get("vanished") and f.get("grey")):
                continue
            name, frame, digest = f.get("file"), f.get("frame"), f.get("pngSha256")
            if not isinstance(name, str) or not isinstance(frame, int) or isinstance(frame, bool) or not isinstance(digest, str):
                raise PackError(f"{folder.name}/font.json: a frame entry lacks file, frame or pngSha256")
            png = folder / name
            if os.path.basename(name) != name or not png.is_file() or png.is_symlink():
                raise PackError(f"{folder.name}: frame file {name!r} is missing or not a plain file")
            if sha256(png) != digest:
                raise PackError(f"{folder.name}/{name} differs from the SHA-256 in font.json; trace the font again")
            images.append(os.path.relpath(png, out.parent).replace(os.sep, "/"))
            tiles.append({"art": file, "frame": frame})
        if not tiles:
            raise PackError(f"{folder.name}: no traced frame with ink")
        items.append({"mode": "ready", "coverage": True, "images": images, "tiles": tiles})
        counts[f"{number} {file}"] = len(tiles)
    if not items:
        raise PackError(f"{fonts}: no font folder with a font.json" + ("" if arts is None else " for the named arts"))
    if len(scales) != 1:
        raise PackError(f"the font folders have different scales {sorted(scales)}")
    plan = {"schema": 1, "kind": FONT_PLAN_KIND, "packId": pack_id, "scale": scales.pop(), "items": items}
    return plan, counts


def cmd_fonts_plan(a) -> int:
    fonts, out = a.fonts.resolve(), a.out
    real = out.resolve()
    # under a checkout's own local/ (the folder git ignores: its parent holds .git), never inside a pack, a staging pack or a pack collection
    anchored = any(p.name == "local" and (p.parent / ".git").exists() for p in real.parents)
    if out.is_symlink() or not anchored or GAME_SUPPORT.resolve() in (real, *real.parents) \
            or {"hd-packs", "hd_pack", "pack-out"} & set(real.parts):
        raise PackError(f"{out} must be a plain file under a checkout's local/ directory, never inside an installed pack, a staging pack or a pack collection")
    if out.suffix != ".json":
        raise PackError(f"{out} must be a .json file")
    if out.exists():
        try:
            old = json.loads(out.read_text())
            if old.get("kind") != FONT_PLAN_KIND or not all(i.get("coverage") is True for i in old["items"]):
                raise ValueError("not a font plan")
        except (OSError, ValueError, AttributeError, KeyError, TypeError) as e:
            raise PackError(f"{out} exists and is not a font plan of this tool; refusing to overwrite it ({e})") from e
    if not fonts.is_dir() or not (fonts / "receipt.json").is_file():
        raise PackError(f"{fonts} is not a hd_font output folder (no receipt.json)")
    arts = None
    if a.art:
        try:
            arts = {int(x) for x in a.art.split(",")}
        except ValueError as e:
            raise PackError(f"--art {a.art!r} is not a comma list of numbers") from e
    plan, counts = font_plan(fonts, real, a.pack_id, arts)
    for name, n in counts.items():
        print(f"  font {name}: {n} frames")
    print(f"plan {out}: {sum(counts.values())} frames in {len(counts)} fonts, scale {plan['scale']}x, pack id {plan['packId']}")
    if a.dry_run:
        print("DRY-RUN: nothing written")
        return 0
    real.parent.mkdir(parents=True, exist_ok=True)
    atomic_write(real, (json.dumps(plan, indent=1) + "\n").encode())
    print(f"PASS fonts-plan; next: stage --plan {out} --staging local/res095/pack-out/{a.pack_id} --dry-run")
    return 0


PORTRAIT_PLAN_KIND = "arcanum-steamwoven-hd-portrait-plan"


def read_sums(folder: Path) -> dict:
    """`SHA256SUMS.txt` of a delivery folder (`<sha256>  ./<relative path>` per line)."""
    path = folder / "SHA256SUMS.txt"
    try:
        lines = path.read_text().splitlines()
    except OSError as e:
        raise PackError(f"{folder} has no readable SHA256SUMS.txt ({e})") from e
    sums = {}
    for line in lines:
        digest, _, name = line.partition("  ")
        if len(digest) == 64 and name:
            sums[name.removeprefix("./")] = digest
    return sums


def portrait_plan(hero: Path | None, npc: Path | None, out: Path, pack_id: str) -> tuple[dict, dict]:
    """ARC-UI-2865: the importer plan for the portrait deliveries. A hero delivery (ARC-UI-2864) holds `large/<id>_b.png` (512, for the 128 bitmap
    `portrait/<id>_b.bmp`) and `small/<id>.png` (256, for `portrait/<id>.bmp`); an NPC delivery (ARC-UI-2884) holds `<id>.png` (256, the small bitmap
    only: an NPC has no large one). Every file used is checked against the delivery's SHA256SUMS.txt, its PNG header (8-bit, not interlaced, the size
    above) and, for a hero, that its large and small image exist for the same ids. Image paths are relative to OUT. The importer checks each size
    against the original bitmap it redraws."""
    items, counts = [], {}

    def add(folder: Path, rel: str, bmp: str, side: int, sums: dict, group: str) -> None:
        png = folder / rel
        if not png.is_file() or png.is_symlink():
            raise PackError(f"{png} is missing or not a plain file")
        if sums.get(rel) != sha256(png):
            raise PackError(f"{png} is not listed in {folder}/SHA256SUMS.txt or differs from it; the delivery changed after it was packed")
        w, h, depth, ctype, interlace = png_header(png)
        if (w, h) != (side, side) or depth != 8 or ctype not in (2, 6) or interlace:
            raise PackError(f"{png}: {w}x{h}, depth {depth}, colour type {ctype}; expected {side}x{side}, 8-bit RGB or RGBA, not interlaced")
        items.append({"bmp": bmp, "image": os.path.relpath(png, out.parent).replace(os.sep, "/")})
        counts[group] = counts.get(group, 0) + 1

    if hero is not None:
        sums = read_sums(hero)
        ids = sorted(p.name.removesuffix("_b.png") for p in (hero / "large").glob("*_b.png"))
        small = sorted(p.name.removesuffix(".png") for p in (hero / "small").glob("*.png"))
        if ids != small:
            raise PackError(f"{hero}: the large and the small folder name different portraits ({sorted(set(ids) ^ set(small))[:6]})")
        for i in ids:
            add(hero, f"large/{i}_b.png", f"portrait/{i}_b.bmp", 512, sums, "hero large")
            add(hero, f"small/{i}.png", f"portrait/{i}.bmp", 256, sums, "hero small")
    if npc is not None:
        sums = read_sums(npc)
        for p in sorted(npc.glob("*.png")):
            add(npc, p.name, f"portrait/{p.stem}.bmp", 256, sums, "npc small")
    if not items:
        raise PackError("no portrait delivery given (--hero DIR, --npc DIR)")
    for it in items:
        if not all(c.islower() or c.isdigit() or c in "_-/." for c in it["bmp"]):
            raise PackError(f"{it['bmp']} is not a lowercase portrait name")
    return {"schema": 1, "kind": PORTRAIT_PLAN_KIND, "packId": pack_id, "scale": 4, "items": items}, counts


def cmd_portraits_plan(a) -> int:
    out = a.out
    real = out.resolve()
    anchored = any(p.name == "local" and (p.parent / ".git").exists() for p in real.parents)
    if out.is_symlink() or not anchored or GAME_SUPPORT.resolve() in (real, *real.parents) \
            or {"hd-packs", "hd_pack", "pack-out"} & set(real.parts):
        raise PackError(f"{out} must be a plain file under a checkout's local/ directory, never inside an installed pack, a staging pack or a pack collection")
    if out.suffix != ".json":
        raise PackError(f"{out} must be a .json file")
    if out.exists():
        try:
            if json.loads(out.read_text()).get("kind") != PORTRAIT_PLAN_KIND:
                raise ValueError("not a portrait plan")
        except (OSError, ValueError, AttributeError) as e:
            raise PackError(f"{out} exists and is not a portrait plan of this tool; refusing to overwrite it ({e})") from e
    plan, counts = portrait_plan(a.hero.resolve() if a.hero else None, a.npc.resolve() if a.npc else None, real, a.pack_id)
    for group, n in sorted(counts.items()):
        print(f"  {group}: {n} images")
    print(f"plan {out}: {len(plan['items'])} portraits, scale 4x, pack id {plan['packId']}")
    if a.dry_run:
        print("DRY-RUN: nothing written")
        return 0
    real.parent.mkdir(parents=True, exist_ok=True)
    atomic_write(real, (json.dumps(plan, indent=1) + "\n").encode())
    print(f"PASS portraits-plan; next: stage --plan {out} --staging local/res095/pack-out/{a.pack_id} --dry-run")
    return 0


MAP_PLAN_KIND = "arcanum-steamwoven-hd-map-plan"
TILE_SIDE, ZOOMED_SIDE, TILE_COUNT = 250, 365, 64  # the original's world-map bitmaps (docs/formats/hd-pack.md#world-map-entries)
_TILE_NAME = re.compile(r"^(?P<base>[a-z0-9_-]*?)(?P<n>\d{3})$")


def map_plan(maps: Path, out: Path, pack_id: str, scale: int, zoomed_name: str, partial: bool) -> tuple[dict, dict]:
    """ARC-RENDER-3079: the importer plan for a delivery of HD world-map pictures: `<base>001.png` .. `<base>064.png` (the 64 tiles, `250 x scale` square) and `<zoomed_name>.png`
    (`365 x scale`, optional). Every file used is a plain PNG (8-bit RGB or RGBA, not interlaced) of exactly that size, checked against SHA256SUMS.txt when the folder has one; the 64 tiles
    must be there with one base name and no gap unless `partial`. The plan names the original bitmaps `worldmap/<name>.bmp`; the importer checks each size against the bitmap it
    reads from the game. Image paths are relative to OUT."""
    if scale not in (2, 4):
        raise PackError(f"scale {scale} is not an accepted pack scale (2 or 4)")
    sums = read_sums(maps) if (maps / "SHA256SUMS.txt").is_file() else None
    items, tiles, bases = [], {}, set()

    def add(png: Path, bmp: str, side: int) -> None:
        if not png.is_file() or png.is_symlink():
            raise PackError(f"{png} is missing or not a plain file")
        if sums is not None and sums.get(png.name) != sha256(png):
            raise PackError(f"{png} is not listed in {maps}/SHA256SUMS.txt or differs from it; the delivery changed after it was packed")
        w, h, depth, ctype, interlace = png_header(png)
        expect = side * scale
        if (w, h) != (expect, expect) or depth != 8 or ctype not in (2, 6) or interlace:
            raise PackError(f"{png}: {w}x{h}, depth {depth}, colour type {ctype}; expected {expect}x{expect}, 8-bit RGB or RGBA, not interlaced")
        items.append({"bmp": bmp, "image": os.path.relpath(png, out.parent).replace(os.sep, "/")})

    for png in sorted(maps.glob("*.png")):
        if png.stem == zoomed_name:
            continue
        m = _TILE_NAME.match(png.stem)
        if not m:
            raise PackError(f"{png.name}: not a tile name (<base>NNN.png) nor the continent picture ({zoomed_name}.png)")
        bases.add(m["base"])
        tiles[int(m["n"])] = png
    if len(bases) > 1:
        raise PackError(f"{maps}: the tiles carry more than one base name ({sorted(bases)})")
    if not partial and sorted(tiles) != list(range(1, TILE_COUNT + 1)):
        missing = [n for n in range(1, TILE_COUNT + 1) if n not in tiles]
        raise PackError(f"{maps}: expected the {TILE_COUNT} tiles 001..{TILE_COUNT:03d}, missing {missing[:8]}{'...' if len(missing) > 8 else ''} (--partial stages what is there)")
    for n, png in sorted(tiles.items()):
        if not 1 <= n <= TILE_COUNT:
            raise PackError(f"{png.name}: tile number {n} is outside 001..{TILE_COUNT:03d}")
        add(png, f"worldmap/{png.stem}.bmp", TILE_SIDE)
    zoomed = maps / f"{zoomed_name}.png"
    if zoomed.exists():
        add(zoomed, f"worldmap/{zoomed_name}.bmp", ZOOMED_SIDE)
    if not items:
        raise PackError(f"no world-map picture in {maps}")
    for it in items:
        if not all(c.islower() or c.isdigit() or c in "_-/." for c in it["bmp"]):
            raise PackError(f"{it['bmp']} is not a lowercase world-map bitmap name")
    return {"schema": 1, "kind": MAP_PLAN_KIND, "packId": pack_id, "scale": scale, "items": items}, {"tiles": len(tiles), "map-zoomed": 1 if zoomed.exists() else 0}


def cmd_maps_plan(a) -> int:
    out = a.out
    real = out.resolve()
    anchored = any(p.name == "local" and (p.parent / ".git").exists() for p in real.parents)
    if out.is_symlink() or not anchored or GAME_SUPPORT.resolve() in (real, *real.parents) \
            or {"hd-packs", "hd_pack", "pack-out"} & set(real.parts):
        raise PackError(f"{out} must be a plain file under a checkout's local/ directory, never inside an installed pack, a staging pack or a pack collection")
    if out.suffix != ".json":
        raise PackError(f"{out} must be a .json file")
    if out.exists():
        try:
            if json.loads(out.read_text()).get("kind") != MAP_PLAN_KIND:
                raise ValueError("not a world-map plan")
        except (OSError, ValueError, AttributeError) as e:
            raise PackError(f"{out} exists and is not a world-map plan of this tool; refusing to overwrite it ({e})") from e
    plan, counts = map_plan(a.maps.resolve(), real, a.pack_id, a.scale, a.zoomed_name, a.partial)
    for group, n in sorted(counts.items()):
        print(f"  {group}: {n} images")
    print(f"plan {out}: {len(plan['items'])} world-map bitmaps, scale {a.scale}x, pack id {plan['packId']}")
    if a.dry_run:
        print("DRY-RUN: nothing written")
        return 0
    real.parent.mkdir(parents=True, exist_ok=True)
    atomic_write(real, (json.dumps(plan, indent=1) + "\n").encode())
    print(f"PASS maps-plan; next: stage --plan {out} --staging local/res095/pack-out/{a.pack_id} --dry-run")
    return 0


TOWN_PLAN_KIND = "arcanum-steamwoven-hd-townmap-plan"
TOWN_TILE_W, TOWN_TILE_H = 64, 32  # the original's town-map tile (docs/formats/hd-pack.md#town-map-entries)
_TOWN_CHARS = re.compile(r"^[a-z0-9 '._-]{1,40}$")


def townmap_plan(tiles: Path, out: Path, town: str, pack_id: str, scale: int, partial_ok: bool, expect: int | None) -> tuple[dict, int]:
    """ARC-RENDER-3106: the importer plan for the tiles of one rendered town (`tools/hd_pack/town_render.py stitch`, 24-bit or RGBA PNG `<Town>NNNNNN.png` at `64 x 4 x render scale`).
    Every file is a plain PNG (8-bit RGB or RGBA, not interlaced) of exactly `64 x scale` by `32 x scale` (256 x 128 at the 4x pack: render scale 1.0), checked against SHA256SUMS.txt when the
    folder has one. The plan names the original tiles `townmap/<town>/<town>NNNNNN.bmp` (lowercase, as the viewer reads them); the importer checks each against the tile it reads from the game.
    Image paths are relative to OUT. `expect` is the town's tile count (`.tmi` tiles that exist): a gap is refused unless `partial_ok`."""
    if scale not in (2, 4):
        raise PackError(f"scale {scale} is not an accepted pack scale (2 or 4)")
    low = town.strip().lower()
    if not _TOWN_CHARS.match(low) or low[0] in " ." or low[-1] in " .":
        raise PackError(f"town {town!r}: an HD entry names a town of at most 40 characters of a-z 0-9 space ' . _ - (no leading or trailing space or dot)")
    sums = read_sums(tiles) if (tiles / "SHA256SUMS.txt").is_file() else None
    name_re = re.compile(rf"^{re.escape(low)}(?P<n>\d{{6}})\.png$", re.IGNORECASE)
    found: dict[int, Path] = {}
    for png in sorted(tiles.glob("*.png")):
        m = name_re.match(png.name)
        if not m:
            raise PackError(f"{png.name}: not a tile of {town} ({town}NNNNNN.png)")
        if int(m["n"]) in found:
            raise PackError(f"{png.name} and {found[int(m['n'])].name} are the same tile (tile names are matched without regard to case)")
        found[int(m["n"])] = png
    if not found:
        raise PackError(f"no tile of {town} in {tiles}")
    if expect is not None and not partial_ok and len(found) != expect:
        raise PackError(f"{tiles}: {len(found)} tiles, the town has {expect} (--partial stages what is there)")
    items = []
    for n, png in sorted(found.items()):
        if png.is_symlink() or not png.is_file():
            raise PackError(f"{png} is missing or not a plain file")
        if sums is not None and sums.get(png.name) != sha256(png):
            raise PackError(f"{png} is not listed in {tiles}/SHA256SUMS.txt or differs from it; the delivery changed after it was packed")
        w, h, depth, ctype, interlace = png_header(png)
        ew, eh = TOWN_TILE_W * scale, TOWN_TILE_H * scale
        if (w, h) != (ew, eh) or depth != 8 or ctype not in (2, 6) or interlace:
            raise PackError(f"{png}: {w}x{h}, depth {depth}, colour type {ctype}; expected {ew}x{eh}, 8-bit RGB or RGBA, not interlaced")
        items.append({"bmp": f"townmap/{low}/{low}{n:06d}.bmp", "image": os.path.relpath(png, out.parent).replace(os.sep, "/")})
    return {"schema": 1, "kind": TOWN_PLAN_KIND, "packId": pack_id, "scale": scale, "items": items}, len(found)


def cmd_townmap_plan(a) -> int:
    out = a.out
    real = out.resolve()
    anchored = any(p.name == "local" and (p.parent / ".git").exists() for p in real.parents)
    if out.is_symlink() or not anchored or GAME_SUPPORT.resolve() in (real, *real.parents) \
            or {"hd-packs", "hd_pack", "pack-out"} & set(real.parts):
        raise PackError(f"{out} must be a plain file under a checkout's local/ directory, never inside an installed pack, a staging pack or a pack collection")
    if out.suffix != ".json":
        raise PackError(f"{out} must be a .json file")
    if out.exists():
        try:
            if json.loads(out.read_text()).get("kind") != TOWN_PLAN_KIND:
                raise ValueError("not a town-map plan")
        except (OSError, ValueError, AttributeError) as e:
            raise PackError(f"{out} exists and is not a town-map plan of this tool; refusing to overwrite it ({e})") from e
    plan, count = townmap_plan(a.tiles.resolve(), real, a.town, a.pack_id, a.scale, a.partial, a.expect)
    print(f"plan {out}: {count} tiles of {a.town}, scale {a.scale}x (render scale {a.scale / 4:g}), pack id {plan['packId']}")
    if a.dry_run:
        print("DRY-RUN: nothing written")
        return 0
    real.parent.mkdir(parents=True, exist_ok=True)
    atomic_write(real, (json.dumps(plan, indent=1) + "\n").encode())
    print(f"PASS townmap-plan; next: stage --plan {out} --staging local/res095/pack-out/{a.pack_id} --dry-run")
    return 0


def parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="hd_pack.py", description=__doc__.split("\n\n")[0])
    sub = p.add_subparsers(dest="cmd", required=True)

    def add(name, fn, helptext):
        s = sub.add_parser(name, help=helptext)
        s.set_defaults(fn=fn)
        return s

    def installed(s):
        s.add_argument("--installed", type=Path, default=DEFAULT_INSTALLED, help=f"the pack the game reads (default {PACK_ID} in the game's hd-packs)")
        s.add_argument("--allow-symlink", action="store_true", help="accept a pack folder that is itself a symbolic link")

    def write(s, task=True):
        s.add_argument("--dry-run", action="store_true", help="print the plan, change nothing")
        s.add_argument("--backup-root", type=Path, default=DEFAULT_BACKUPS, help="outside hd-packs; one folder per change")
        if task:
            s.add_argument("--task", required=True, help="task code in the backup folder name, e.g. ARC-UI-125")

    s = add("status", cmd_status, "describe the installed pack"); installed(s); s.add_argument("--deep", action="store_true")
    s = add("verify", cmd_verify, "check the installed pack's files"); installed(s); s.add_argument("--deep", action="store_true")
    def update_args(s):
        s.add_argument("--staged", type=Path, required=True)
        s.add_argument("--add-new", action="store_true", help="also add entries only the staging build has")
        s.add_argument("--base", type=Path, help="the manifest the staging was last applied from (a pack folder or a manifest.json, e.g. a backup's staged-manifest.json or the oldest pre-repair backup)")
        s.add_argument("--conflicts", choices=("refuse", "skip", "overwrite"), default="refuse", help="entries repaired in the installed pack after the base: refuse the update, keep them, or overwrite them")
        s.add_argument("--folders", help="comma list of extra folders whose entries may be replaced besides interface/ (a repair of a Codex entry, e.g. character-hd114); default: interface only (a pack with a layout map chooses by --slices instead)")
        s.add_argument("--slices", help="a pack with a layout map: comma list of extra slices whose entries may be replaced besides interface (e.g. character-hd114,interface-refinement02-soft)")
        s.add_argument("--map", type=Path, help="a layout map with the rows of ARTs the update adds; merged into the pack's layout-map.tsv by apply")

    s = add("plan", cmd_plan, "show the diff-only update, write nothing"); installed(s); update_args(s)
    s = add("apply", cmd_apply, "diff-only update with backup"); installed(s); write(s); update_args(s)
    s.add_argument("--no-base-check", action="store_true", help="apply without --base (only when the installed entries are known untouched)")
    s = add("rollback", cmd_rollback, "undo one apply or relayout"); installed(s)
    s.add_argument("backup", type=Path); s.add_argument("--dry-run", action="store_true")
    s = add("mirror", cmd_mirror, "copy a pack's missing files to another folder (the iCloud copy)")
    s.add_argument("--src", type=Path, default=DEFAULT_INSTALLED); s.add_argument("--dst", type=Path, default=DEFAULT_COPY, help=f"the copy folder (default: the iCloud/Obsidian hd_pack/{PACK_ID})")
    s.add_argument("--dry-run", action="store_true"); s.add_argument("--deep", action="store_true", help="hash content-named files too (reads the destination fully)")
    s = add("stage", cmd_stage, "run the importer into a staging folder")
    s.add_argument("--plan", type=Path, required=True); s.add_argument("--staging", type=Path, required=True)
    s.add_argument("--module", type=Path, default=DEFAULT_MODULE); s.add_argument("--fresh", action="store_true"); s.add_argument("--dry-run", action="store_true")
    s = add("relayout", cmd_relayout, "move every entry to its place of the layout map (ARC-DEV-376)"); installed(s); write(s)
    s.add_argument("--map", type=Path, help=f"the layout map (art, screen, kind, folder); default: the pack's own {layout.MAP_FILE}")
    s.add_argument("--allow-running", action="store_true", help="relayout even when a process names the pack folder in its command line")
    s = add("index", cmd_index, "rewrite INDEX.md and index.tsv in the pack"); installed(s); s.add_argument("--dry-run", action="store_true")
    s = add("refresh", cmd_refresh, "take edited PNGs into the manifest (new size and SHA-256)"); installed(s); write(s)
    s = add("fonts-plan", cmd_fonts_plan, "write the importer plan for traced font frames (tools/hd_font)")
    s.add_argument("--fonts", type=Path, required=True, help="the hd_font output folder (local/hd-font/out)")
    s.add_argument("--out", type=Path, required=True, help="the plan file, a .json under local/")
    s.add_argument("--pack-id", default="interface-fonts", help="id of the staging pack the plan builds (default interface-fonts)")
    s.add_argument("--art", help="only these font ART numbers, e.g. 27,229 (default: every font folder)")
    s.add_argument("--dry-run", action="store_true", help="read and check the frames, print the counts, write nothing")
    s = add("portraits-plan", cmd_portraits_plan, "write the importer plan for portrait deliveries (ARC-UI-2865)")
    s.add_argument("--hero", type=Path, help="the hero portrait delivery folder (large/<id>_b.png 512, small/<id>.png 256, SHA256SUMS.txt)")
    s.add_argument("--npc", type=Path, help="the NPC portrait delivery folder (<id>.png 256, SHA256SUMS.txt)")
    s.add_argument("--out", type=Path, required=True, help="the plan file, a .json under local/")
    s.add_argument("--pack-id", default="interface-portraits", help="id of the staging pack the plan builds (default interface-portraits)")
    s.add_argument("--dry-run", action="store_true", help="read and check the images, print the counts, write nothing")
    s = add("maps-plan", cmd_maps_plan, "write the importer plan for HD world-map pictures (ARC-RENDER-3079)")
    s.add_argument("--maps", type=Path, required=True, help="the delivery folder: <base>001.png .. <base>064.png (250 x scale) and the continent picture (365 x scale), SHA256SUMS.txt optional")
    s.add_argument("--out", type=Path, required=True, help="the plan file, a .json under local/")
    s.add_argument("--pack-id", default="interface-maps", help="id of the staging pack the plan builds (default interface-maps)")
    s.add_argument("--scale", type=int, default=4, help="the pack scale the pictures are drawn at (2 or 4; default 4)")
    s.add_argument("--zoomed-name", default="map_zoomed", help="the continent picture's file stem, the original's ZoomedName lowercased (default map_zoomed)")
    s.add_argument("--partial", action="store_true", help="accept fewer than the 64 tiles")
    s.add_argument("--dry-run", action="store_true", help="read and check the images, print the counts, write nothing")
    s = add("townmap-plan", cmd_townmap_plan, "write the importer plan for the rendered tiles of one town map (ARC-RENDER-3106)")
    s.add_argument("--tiles", type=Path, required=True, help="the folder of the town render's tiles: <Town>NNNNNN.png at 64 x scale by 32 x scale, SHA256SUMS.txt optional")
    s.add_argument("--town", required=True, help="the town's name as the original names its folder (rules/townmap.mes, e.g. Ashbury)")
    s.add_argument("--out", type=Path, required=True, help="the plan file, a .json under local/")
    s.add_argument("--pack-id", default="interface-townmaps", help="id of the staging pack the plan builds (default interface-townmaps)")
    s.add_argument("--scale", type=int, default=4, help="the pack scale the tiles are rendered at (2 or 4; 4 = render scale 1.0; default 4)")
    s.add_argument("--expect", type=int, default=None, help="the number of tiles the town has (the .tmi tiles that exist); fewer is refused unless --partial")
    s.add_argument("--partial", action="store_true", help="accept fewer tiles than --expect")
    s.add_argument("--dry-run", action="store_true", help="read and check the images, print the count, write nothing")
    return p


def main(argv=None) -> int:
    args = parser().parse_args(argv)
    try:
        return args.fn(args)
    except PackError as e:
        print(f"FAIL {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
