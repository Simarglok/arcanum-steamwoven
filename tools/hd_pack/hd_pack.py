#!/usr/bin/env python3
"""ARC-DEV-329: safe maintenance of the player's local HD interface pack (docs/workflow/hd-pack.md).

Pure standard library; works on a pack directory only (manifest.json, the files it lists, a few
unlisted extras). Nothing here contains or produces original art; it moves local files.

  status     what the installed pack is (manifest hash, entries per folder, extras)
  verify     every manifest file present with the declared size (--deep: SHA-256), no symlinks
  plan       what a diff-only update of INSTALLED from STAGED would change (writes nothing)
  apply      that update, with a backup outside hd-packs and a receipt (--dry-run first)
  rollback   undo one apply or menu-art from its backup folder (--dry-run first)
  menu-art   replace the unlisted main-menu-16x9.png (checked header, backup, receipt)
  mirror     copy missing/different files from one pack to another (Drive copy); never deletes
  stage      run the importer into a staging folder that is never an installed pack

Every command that writes takes --dry-run, which prints the same plan and changes nothing.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import time

GAME_SUPPORT = Path.home() / "Library" / "Application Support" / "Arcanum Steamwoven Game"
PACK_ID = "interface-redraw-4x"
DEFAULT_INSTALLED = GAME_SUPPORT / "hd-packs" / PACK_ID
DEFAULT_BACKUPS = GAME_SUPPORT / "hd-pack-backups"
DEFAULT_MODULE = Path("/Applications/Arcanum/modules/Arcanum.dat")
REPO = Path(__file__).resolve().parents[2]
REPLACEABLE = "interface/"  # only entries whose file lives here are replaced; other folders are extras of other tasks (unless --folders names them)
MENU_FILE = "main-menu-16x9.png"
MENU_MAX_BYTES, MENU_MAX_W, MENU_MAX_H = 40 * 1024 * 1024, 3840, 2160
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


def entry_key(e: dict) -> tuple:
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
        raise PackError(f"{installed} is a symbolic link (a Google Drive link?); give the real folder or --allow-symlink")
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
    for e in manifest["entries"]:
        folder = e["file"].split("/")[0]
        info["perFolder"][folder] = info["perFolder"].get(folder, 0) + 1
    return problems, info


def cmd_status(a) -> int:
    problems, info = verify(a.installed, a.deep)
    print(f"pack {a.installed}\n  id {info['id']} scale {info['scale']} entries {info['entries']} manifest sha256 {info['manifestSha256']}")
    print("  entries per folder:", ", ".join(f"{k} {v}" for k, v in sorted(info["perFolder"].items())))
    unlisted = info["unlisted"]
    print(f"  files not in the manifest ({len(unlisted)}):", ", ".join(unlisted[:8]) + (" ..." if len(unlisted) > 8 else ""))
    menu = a.installed / MENU_FILE
    print(f"  {MENU_FILE}: " + (f"{menu.stat().st_size} bytes sha256 {sha256(menu)}" if menu.is_file() else "absent"))
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


def compute_plan(installed: Path, staged: Path, add_new: bool, base: dict | None = None, conflicts: str = "refuse",
                 folders: tuple[str, ...] = (REPLACEABLE,)) -> dict:
    """Three-way: an entry is replaced only when the installed file is still the base's (nobody repaired it locally) and the
    staging build differs. Without a base nothing can tell a stale staging from a repair, so every difference is taken and apply refuses."""
    mi, isha = load_manifest(installed)
    ms, ssha = load_manifest(staged)
    ki, ks = keyed(mi, "installed"), keyed(ms, "staged")
    replace, entries, conflicted = [], [], []
    for e in mi["entries"]:
        n = ks.get(entry_key(e))
        differs = n is not None and e["file"].startswith(folders) and n["file"].startswith(folders) and n["sha256"] != e["sha256"]
        if differs and base is not None:
            b = base.get(entry_key(e))
            if b is None or b["sha256"] != e["sha256"]:  # the installed entry is not the base's: a local repair (staging equal to the base would revert it)
                conflicted.append((e, n, "repair kept by installed" if b is not None and b["sha256"] == n["sha256"] else "changed on both sides" if b is not None else "not in the base"))
                if conflicts != "overwrite":
                    entries.append(e)
                    continue
        if differs:
            replace.append((e, n))
            entries.append(n)
        else:
            entries.append(e)
    new = [n for k, n in ks.items() if k not in ki and not (base is not None and k in base)]  # one the base had and the installed pack dropped stays dropped
    if add_new:
        entries += [n for n in new if n["file"].startswith(folders)]
    for old, n in replace:
        safe_rel(old["file"])
        safe_rel(n["file"])
    for n in new if add_new else []:
        safe_rel(n["file"])
    copies = [n for _, n in replace] + ([n for n in new if n["file"].startswith(folders)] if add_new else [])
    listed_now = {e["file"] for e in mi["entries"]}
    for n in copies:  # a staged file must exist and match before anything is touched; a target must not be an unlisted file
        if n["file"] not in listed_now and (installed / n["file"]).exists():
            raise PackError(f"{n['file']} exists in the installed pack but is not in its manifest; refusing to overwrite it (move it away first)")
        p = staged / n["file"]
        if not p.is_file() or p.stat().st_size != n["bytes"] or sha256(p) != n["sha256"]:
            raise PackError(f"staged file is missing or does not match its manifest entry: {n['file']}")
    manifest = dict(mi, entries=entries)
    new_manifest = json.dumps(manifest, separators=(",", ":")).encode()
    return {"old": mi, "oldSha": isha, "stagedSha": ssha, "replace": replace, "conflicts": conflicted, "baseGiven": base is not None, "conflictMode": conflicts,
            "stagedManifest": json.dumps(ms, separators=(",", ":")).encode(), "copies": copies, "newEntries": new, "added": add_new,
            "manifestBytes": new_manifest, "newManifestSha": hashlib.sha256(new_manifest).hexdigest(),
            "keptExtra": sum(1 for e in mi["entries"] if not e["file"].startswith(folders)), "folders": folders,
            "stagedOnly": len(new), "installedOnly": sum(1 for k in ki if k not in ks)}


def print_plan(plan: dict, verb: str) -> None:
    print(f"installed manifest {plan['oldSha'][:12]} -> {plan['newManifestSha'][:12]} (staged build {plan['stagedSha'][:12]})")
    extra = [f.rstrip("/") for f in plan["folders"] if f != REPLACEABLE]
    where = "interface entries" if not extra else "entries (interface and " + ", ".join(extra) + ")"
    print(f"  {len(plan['replace'])} {where} differ and {verb} replaced; {plan['keptExtra']} entries of other folders and all unlisted files stay")
    print(f"  staged-only entries: {plan['stagedOnly']} ({'added' if plan['added'] else 'ignored; --add-new adds them'}); installed-only entries kept: {plan['installedOnly']}")
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
    print_plan(compute_plan(a.installed, a.staged, a.add_new, load_base(a.base), a.conflicts, folder_prefixes(a.folders)), "would be")
    return 0


def cmd_apply(a) -> int:
    guard_installed(a.installed, a.allow_symlink)
    guard_backup(a.backup_root, a.installed)
    plan = compute_plan(a.installed, a.staged, a.add_new, load_base(a.base), a.conflicts, folder_prefixes(a.folders))
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
                   "replaced": removed, "added": [n["file"] for n in plan["copies"]]}
        atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
        gone: list[str] = []
        try:
            for n in plan["copies"]:
                atomic_copy(a.staged / n["file"], inst / n["file"])
            atomic_write(inst / "manifest.json", plan["manifestBytes"])
            referenced = {e["file"] for e in json.loads(plan["manifestBytes"])["entries"]}
            gone = [r["file"] for r in removed if r["file"] not in referenced and (inst / r["file"]).is_file()]
            for rel in gone:
                (inst / rel).unlink()
        except (OSError, KeyboardInterrupt) as e:
            receipt["status"] = "failed"
            atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
            raise PackError(f"apply was interrupted ({e!r}); the pack may be half updated. Roll back with: python3 -B tools/hd_pack/hd_pack.py rollback {backup} --installed {inst}") from e
        after = snapshot(inst)
        allowed = {"manifest.json"} | set(receipt["added"]) | set(gone)
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
    if receipt.get("status") not in ("applied", "failed", "started") or receipt.get("kind") not in ("update", "menu"):
        raise PackError("receipt has an unknown kind or status")
    if receipt["kind"] == "menu":
        current = sha256(inst / MENU_FILE) if (inst / MENU_FILE).is_file() else None
        if current not in (receipt["newSha256"], receipt["oldSha256"]) or (current == receipt["oldSha256"] and receipt["status"] == "applied"):
            raise PackError("the installed main-menu art changed after that install; refusing to roll back over it")
        if receipt["oldSha256"] and (not (a.backup / MENU_FILE).is_file() or sha256(a.backup / MENU_FILE) != receipt["oldSha256"]):
            raise PackError("the backup of the main-menu art does not match its receipt")
        steps = [f"restore {MENU_FILE}" if receipt["oldSha256"] else f"remove {MENU_FILE}"]
        print("rollback menu-art:", steps[0])
        if a.dry_run:
            print("DRY-RUN: nothing changed")
            return 0
        with Lock(inst.parent):
            if receipt["oldSha256"]:
                atomic_copy(a.backup / MENU_FILE, inst / MENU_FILE)
            elif (inst / MENU_FILE).is_file():
                (inst / MENU_FILE).unlink()
        print("PASS rollback")
        return 0
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
    problems, _ = verify(inst, deep=False)
    for p in problems[:20]:
        print("FAIL", p)
    ok = not problems and sha256(inst / "manifest.json") == receipt["oldManifestSha256"]
    print(f"{'PASS' if ok else 'FAIL'} rollback")
    return 0 if ok else 1


# ---- menu art / mirror / stage ---------------------------------------------------------------

def png_header(path: Path) -> tuple[int, int, int, int, int]:
    with open(path, "rb") as f:
        head = f.read(33)
    if len(head) < 33 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        raise PackError(f"{path} is not a PNG")
    w, h, depth, ctype, _comp, _filt, interlace = struct.unpack(">IIBBBBB", head[16:29])
    return w, h, depth, ctype, interlace


def cmd_menu_art(a) -> int:
    guard_installed(a.installed, a.allow_symlink)
    guard_backup(a.backup_root, a.installed)
    w, h, depth, ctype, interlace = png_header(a.image)
    size = a.image.stat().st_size
    if w == 0 or h == 0 or depth != 8 or ctype not in (2, 6) or interlace != 0 or w > MENU_MAX_W or h > MENU_MAX_H or size > MENU_MAX_BYTES:
        raise PackError(f"{a.image}: needs 8-bit RGB/RGBA, not interlaced, at most {MENU_MAX_W}x{MENU_MAX_H} and 40 MiB (has {w}x{h}, depth {depth}, type {ctype}, interlace {interlace}, {size} bytes)")
    if abs(w / h - 16 / 9) > 0.02:
        print(f"WARNING: {w}x{h} is not 16:9; the viewer crops the side art for narrower screens only")
    target = a.installed / MENU_FILE
    new_sha = sha256(a.image)
    old_sha = sha256(target) if target.is_file() else None
    backup = a.backup_root / f"{a.task}-menu-{(old_sha or 'none')[:12]}"
    print(f"{MENU_FILE}: {old_sha and old_sha[:12]} -> {new_sha[:12]} ({w}x{h}); backup {backup}")
    if old_sha == new_sha:
        print("nothing to do (identical)")
        return 0
    if backup.exists():
        raise PackError(f"backup folder exists, refusing to reuse it: {backup}")
    if a.dry_run:
        print("DRY-RUN: nothing changed")
        return 0
    with Lock(a.installed.parent):
        backup.mkdir(parents=True)
        if old_sha:
            atomic_copy(target, backup / MENU_FILE)
        receipt = {"kind": "menu", "task": a.task, "status": "started", "oldSha256": old_sha, "newSha256": new_sha}
        atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
        atomic_copy(a.image, target, preserve=False)
        ok = sha256(target) == new_sha
        receipt["status"] = "applied" if ok else "failed"
        atomic_write(backup / "RECEIPT.json", json.dumps(receipt, indent=1).encode())
    print(f"{'PASS' if ok else 'FAIL'} menu-art installed; roll back with: rollback {backup}")
    return 0 if ok else 1


def cmd_mirror(a) -> int:
    problems, _ = verify(a.src, deep=False)
    if problems:
        raise PackError(f"the source pack is damaged ({problems[0]}; {len(problems)} problems); refusing to copy it")
    real_dst = a.dst.resolve()
    if real_dst == a.src.resolve() or GAME_SUPPORT.resolve() in (real_dst, *real_dst.parents):
        raise PackError(f"{a.dst} is the source or inside the game's own folder; mirror writes only to a copy elsewhere (Drive), never to an installed pack")
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
            or "hd-packs" in real.parts or "hd_pack" in real.parts:  # hd_pack: the Drive folder's own name, seen after resolving local/hd-packs
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
        s.add_argument("--folders", help="comma list of extra folders whose entries may be replaced besides interface/ (a repair of a Codex entry, e.g. character-hd114); default: interface only")

    s = add("plan", cmd_plan, "show the diff-only update, write nothing"); installed(s); update_args(s)
    s = add("apply", cmd_apply, "diff-only update with backup"); installed(s); write(s); update_args(s)
    s.add_argument("--no-base-check", action="store_true", help="apply without --base (only when the installed entries are known untouched)")
    s = add("rollback", cmd_rollback, "undo one apply or menu-art"); installed(s)
    s.add_argument("backup", type=Path); s.add_argument("--dry-run", action="store_true")
    s = add("menu-art", cmd_menu_art, "replace main-menu-16x9.png"); installed(s); write(s)
    s.add_argument("--image", type=Path, required=True)
    s = add("mirror", cmd_mirror, "copy a pack's missing files to another folder (Drive copy)")
    s.add_argument("--src", type=Path, default=DEFAULT_INSTALLED); s.add_argument("--dst", type=Path, required=True)
    s.add_argument("--dry-run", action="store_true"); s.add_argument("--deep", action="store_true", help="hash content-named files too (reads the destination fully)")
    s = add("stage", cmd_stage, "run the importer into a staging folder")
    s.add_argument("--plan", type=Path, required=True); s.add_argument("--staging", type=Path, required=True)
    s.add_argument("--module", type=Path, default=DEFAULT_MODULE); s.add_argument("--fresh", action="store_true"); s.add_argument("--dry-run", action="store_true")
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
