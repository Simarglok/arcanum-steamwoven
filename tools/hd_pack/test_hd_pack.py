"""ARC-DEV-329: tools/hd_pack/hd_pack.py on synthetic packs (no original or derived art; the PNG bodies are random bytes)."""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

from tools.hd_pack import hd_pack as hp


def fake(seed: str, n: int = 40) -> bytes:
    return hashlib.sha256(seed.encode()).digest() * (n // 32 + 1)


def png(w: int, h: int, depth: int = 8, ctype: int = 6, interlace: int = 0) -> bytes:
    def chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, depth, ctype, 0, 0, interlace)) + chunk(b"IEND", b"")


def entry(art: str, rel: str, body: bytes, frame: int = 0) -> dict:
    return {"kind": "sprite", "file": rel, "sha256": hashlib.sha256(body).hexdigest(), "bytes": len(body), "slice": "interface",
            "art": {"sha256": hashlib.sha256(art.encode()).hexdigest(), "view": 1, "nameSha256": "0" * 64}, "group": 0, "frame": frame, "palette": 0}


def make_pack(root: Path, items: list[tuple[str, str, str]], extras: dict[str, bytes] | None = None) -> Path:
    """items: (art, folder, version) -> file <folder>/<art>-f0-<hash8>.png"""
    root.mkdir(parents=True, exist_ok=True)
    entries = []
    for art, folder, version in items:
        body = fake(f"{art}:{version}")
        rel = f"{folder}/{art}-f0-{hashlib.sha256(body).hexdigest()[:8]}.png"
        (root / folder).mkdir(exist_ok=True)
        (root / rel).write_bytes(body)
        entries.append(entry(art, rel, body))
    recipe = b'{"kind":"arcanum-steamwoven-hd-recipe"}'
    (root / "recipe.json").write_bytes(recipe)
    manifest = {"schema": 1, "kind": "arcanum-steamwoven-hd-pack", "id": "interface-redraw-4x", "scale": 4,
                "recipe": {"file": "recipe.json", "sha256": hashlib.sha256(recipe).hexdigest()}, "entries": entries}
    (root / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))
    for name, data in (extras or {}).items():
        (root / name).write_bytes(data)
    return root


def run(*argv) -> tuple[int, str, str]:
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        rc = hp.main([str(a) for a in argv])
    return rc, out.getvalue(), err.getvalue()


def tree(root: Path) -> dict[str, str]:
    return {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob("*")) if p.is_file() and p.name != ".DS_Store"}


class HdPackTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        # installed: two interface entries and one extra-folder entry (a Codex repair), plus an unlisted menu file
        self.inst = make_pack(self.tmp / "game" / "hd-packs" / "interface-redraw-4x",
                              [("a", "interface", "v1"), ("b", "interface", "v1"), ("c", "character-hd114", "v1")], {"main-menu-16x9.png": png(1664, 936)})
        # staged: a rebuilt pack in which a changed, b unchanged, c rebuilt in the interface folder (the repair must survive)
        self.stage = make_pack(self.tmp / "local" / "pack-out" / "interface-redraw-4x", [("a", "interface", "v2"), ("b", "interface", "v1"), ("c", "interface", "v9")])
        self.backups = self.tmp / "game" / "hd-pack-backups"

    def apply(self, *extra):
        return run("apply", "--installed", self.inst, "--staged", self.stage, "--backup-root", self.backups, "--task", "ARC-TEST-001", *extra)

    def test_plan_and_dry_run_change_nothing(self):
        before = tree(self.inst)
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage)
        self.assertEqual(rc, 0)
        self.assertIn("1 interface entries differ", out)
        rc, out, _ = self.apply("--dry-run")
        self.assertEqual(rc, 0)
        self.assertIn("DRY-RUN", out)
        self.assertEqual(tree(self.inst), before)
        self.assertFalse(self.backups.exists())

    def test_apply_replaces_only_differing_interface_entries(self):
        before = tree(self.inst)
        rc, out, err = self.apply()
        self.assertEqual(rc, 0, out + err)
        after = tree(self.inst)
        changed = {k for k in set(before) | set(after) if before.get(k) != after.get(k)}
        manifest = json.loads((self.inst / "manifest.json").read_text())
        new_a = next(e["file"] for e in manifest["entries"] if e["file"].startswith("interface/a-"))
        old_a = next(k for k in before if k.startswith("interface/a-"))
        self.assertEqual(changed, {"manifest.json", old_a, new_a})
        self.assertEqual(after["main-menu-16x9.png"], before["main-menu-16x9.png"])  # unlisted file untouched
        self.assertTrue(any(e["file"].startswith("character-hd114/") for e in manifest["entries"]))  # the repair stays
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 0)
        backup = next(self.backups.iterdir())
        self.assertTrue((backup / old_a).is_file() and (backup / "manifest.json").is_file())
        self.assertEqual(json.loads((backup / "RECEIPT.json").read_text())["status"], "applied")

    def test_rollback_restores_byte_identical_tree(self):
        before = tree(self.inst)
        self.assertEqual(self.apply()[0], 0)
        backup = next(self.backups.iterdir())
        rc, out, _ = run("rollback", backup, "--installed", self.inst, "--dry-run")
        self.assertEqual(rc, 0)
        self.assertNotEqual(tree(self.inst), before)
        rc, out, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)

    def test_rollback_refuses_after_a_later_change(self):
        self.assertEqual(self.apply()[0], 0)
        backup = next(self.backups.iterdir())
        manifest = json.loads((self.inst / "manifest.json").read_text())
        manifest["scale"] = 4
        manifest["build"] = {"later": True}
        (self.inst / "manifest.json").write_text(json.dumps(manifest))
        rc, _, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 2)
        self.assertIn("another update followed", err)

    def test_corrupt_staged_file_is_refused_before_any_write(self):
        victim = next(self.stage.glob("interface/a-*.png"))
        victim.write_bytes(b"x" * victim.stat().st_size)
        before = tree(self.inst)
        rc, _, err = self.apply()
        self.assertEqual(rc, 2)
        self.assertIn("does not match", err)
        self.assertEqual(tree(self.inst), before)
        self.assertFalse(self.backups.exists())

    def test_symlinked_installed_pack_and_backup_inside_collection_are_refused(self):
        link = self.tmp / "link"
        link.symlink_to(self.inst)
        rc, _, err = run("apply", "--installed", link, "--staged", self.stage, "--backup-root", self.backups, "--task", "T", "--dry-run")
        self.assertEqual(rc, 2)
        self.assertIn("symbolic link", err)
        rc, _, err = run("apply", "--installed", self.inst, "--staged", self.stage, "--backup-root", self.inst.parent / "bak", "--task", "T", "--dry-run")
        self.assertEqual(rc, 2)
        self.assertIn("outside", err)

    def test_symlink_inside_pack_is_refused(self):
        (self.inst / "interface" / "evil.png").symlink_to(self.stage / "manifest.json")
        rc, _, err = self.apply("--dry-run")
        self.assertEqual(rc, 2)
        self.assertIn("symbolic links inside", err)

    def test_existing_backup_folder_is_never_reused(self):
        self.assertEqual(self.apply()[0], 0)
        # a second pack state with the same old manifest hash cannot occur normally; recreate the folder name to prove the refusal
        old_sha = hp.load_manifest(self.inst)[1]
        (self.backups / f"ARC-TEST-001-{old_sha[:12]}").mkdir()
        manifest = json.loads((self.inst / "manifest.json").read_text())
        self.assertTrue(manifest)  # still a pack
        stage2 = make_pack(self.tmp / "stage2", [("a", "interface", "v3"), ("b", "interface", "v1"), ("c", "interface", "v9")])
        rc, _, err = run("apply", "--installed", self.inst, "--staged", stage2, "--backup-root", self.backups, "--task", "ARC-TEST-001")
        self.assertEqual(rc, 2)
        self.assertIn("exists", err)

    def test_live_lock_blocks_and_stale_lock_is_taken_over(self):
        lock = self.inst.parent / ".hd-pack.lock"
        lock.write_text(f"{os.getpid()} now\n")
        rc, _, err = self.apply()
        self.assertEqual(rc, 2)
        self.assertIn("another hd_pack write", err)
        lock.write_text("999999999 old\n")
        self.assertEqual(self.apply()[0], 0)
        self.assertFalse(lock.exists())

    def test_shared_old_file_is_kept_while_another_entry_references_it(self):
        manifest = json.loads((self.inst / "manifest.json").read_text())
        a = next(e for e in manifest["entries"] if e["file"].startswith("interface/a-"))
        twin = dict(a, art={**a["art"], "sha256": hashlib.sha256(b"twin").hexdigest()})  # a second entry on the same file
        manifest["entries"].append(twin)
        (self.inst / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))
        self.assertEqual(self.apply()[0], 0)
        self.assertTrue((self.inst / a["file"]).is_file())

    def test_verify_finds_missing_wrong_size_and_bad_hash(self):
        a = next(self.inst.glob("interface/a-*.png"))
        b = next(self.inst.glob("interface/b-*.png"))
        b_bytes = b.read_bytes()
        b.write_bytes(b_bytes[:-1] + bytes([b_bytes[-1] ^ 1]))
        self.assertEqual(run("verify", "--installed", self.inst)[0], 0)  # same size
        rc, out, _ = run("verify", "--installed", self.inst, "--deep")
        self.assertEqual(rc, 1)
        self.assertIn("sha256 mismatch", out)
        a.unlink()
        self.assertIn("missing", run("verify", "--installed", self.inst)[1])

    def test_status_lists_unlisted_files(self):
        rc, out, _ = run("status", "--installed", self.inst)
        self.assertEqual(rc, 0)
        self.assertIn("main-menu-16x9.png", out)
        self.assertIn("character-hd114 1", out)

    def test_menu_art_checks_header_and_rolls_back(self):
        before = tree(self.inst)
        good = self.tmp / "menu.png"
        good.write_bytes(png(1664, 936))
        good.write_bytes(good.read_bytes() + b"\0")  # a different file of the same header
        args = ("menu-art", "--installed", self.inst, "--image", good, "--backup-root", self.backups, "--task", "ARC-TEST-002")
        self.assertEqual(run(*args, "--dry-run")[0], 0)
        self.assertEqual(tree(self.inst), before)
        self.assertEqual(run(*args)[0], 0)
        self.assertNotEqual(tree(self.inst)["main-menu-16x9.png"], before["main-menu-16x9.png"])
        backup = next(self.backups.iterdir())
        self.assertEqual(run("rollback", backup, "--installed", self.inst)[0], 0)
        self.assertEqual(tree(self.inst), before)
        for name, data in (("big.png", png(4000, 2250)), ("interlaced.png", png(1664, 936, interlace=1)), ("rgb16.png", png(1664, 936, depth=16)), ("bad.png", b"nope" * 20)):
            f = self.tmp / name
            f.write_bytes(data)
            rc, _, err = run("menu-art", "--installed", self.inst, "--image", f, "--backup-root", self.backups, "--task", "ARC-TEST-003")
            self.assertEqual(rc, 2, name)
        self.assertEqual(tree(self.inst), before)

    def test_mirror_copies_manifest_last_and_never_deletes(self):
        dst = self.tmp / "drive" / "interface-redraw-4x"
        dst.mkdir(parents=True)
        (dst / "orphan.txt").write_text("keep me")
        rc, out, _ = run("mirror", "--src", self.inst, "--dst", dst, "--dry-run")
        self.assertEqual(rc, 0)
        self.assertFalse((dst / "manifest.json").exists())
        self.assertIn("1 files only in the destination", out)
        self.assertEqual(run("mirror", "--src", self.inst, "--dst", dst)[0], 0)
        self.assertEqual({k: v for k, v in tree(dst).items() if k != "orphan.txt"}, tree(self.inst))
        self.assertTrue((dst / "orphan.txt").exists())
        self.assertIn("up to date", run("mirror", "--src", self.inst, "--dst", dst)[1])

    def test_mirror_notices_same_name_same_size_content_change(self):
        dst = self.tmp / "drive" / "interface-redraw-4x"
        self.assertEqual(run("mirror", "--src", self.inst, "--dst", dst)[0], 0)
        a = next(self.inst.glob("interface/a-*.png"))
        body = a.read_bytes()
        a.write_bytes(bytes(b ^ 1 for b in body))  # a local repair keeps the name and the size
        manifest = json.loads((self.inst / "manifest.json").read_text())
        for e in manifest["entries"]:
            if e["file"] == f"interface/{a.name}":
                e["sha256"] = hashlib.sha256(a.read_bytes()).hexdigest()
        (self.inst / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))
        rc, out, _ = run("mirror", "--src", self.inst, "--dst", dst)
        self.assertEqual(rc, 0)
        self.assertIn(f"copy interface/{a.name}", out)
        self.assertEqual(tree(dst), tree(self.inst))

    def test_stage_refuses_installed_collection_and_dry_run_runs_nothing(self):
        plan = self.tmp / "plan.json"
        plan.write_text("{}")
        for bad in (self.inst, self.tmp / "local" / "hd_pack" / "x", hp.GAME_SUPPORT / "anything"):
            rc, _, err = run("stage", "--plan", plan, "--staging", bad, "--dry-run")
            self.assertEqual(rc, 2, str(bad))
        rc, out, _ = run("stage", "--plan", plan, "--staging", self.tmp / "local" / "pack-out" / "x", "--dry-run")
        self.assertEqual(rc, 0)
        self.assertIn("hd-interface-import", out)
        rc, _, err = run("stage", "--plan", plan, "--staging", self.stage, "--dry-run")
        self.assertEqual(rc, 2)
        self.assertIn("--fresh", err)
        other = make_pack(self.tmp / "elsewhere" / "copy", [("a", "interface", "v1")])  # a valid pack outside local/**/pack-out
        rc, _, err = run("stage", "--plan", plan, "--staging", other, "--fresh", "--dry-run")
        self.assertEqual(rc, 2)
        self.assertIn("pack-out", err)

    def test_interrupted_apply_is_rolled_back_from_the_old_manifest(self):
        before = tree(self.inst)
        real = hp.atomic_write
        def failing(dst, data):
            if dst.name == "manifest.json":
                raise OSError("disk full")
            real(dst, data)
        hp.atomic_write = failing
        try:
            rc, _, err = self.apply()
        finally:
            hp.atomic_write = real
        self.assertEqual(rc, 2)
        self.assertIn("rollback", err)
        backup = next(self.backups.iterdir())
        self.assertEqual(json.loads((backup / "RECEIPT.json").read_text())["status"], "failed")
        self.assertEqual(run("rollback", backup, "--installed", self.inst)[0], 0)
        self.assertEqual(tree(self.inst), before)

    def test_unlisted_file_with_a_target_name_is_not_overwritten(self):
        a = next(self.stage.glob("interface/a-*.png"))
        (self.inst / "interface" / a.name).write_bytes(b"owner file")
        before = tree(self.inst)
        rc, _, err = self.apply()
        self.assertEqual(rc, 2)
        self.assertIn("not in its manifest", err)
        self.assertEqual(tree(self.inst), before)

    def test_damaged_installed_pack_is_not_updated(self):
        next(self.inst.glob("character-hd114/c-*.png")).unlink()
        rc, _, err = self.apply()
        self.assertEqual(rc, 2)
        self.assertIn("already damaged", err)
        self.assertFalse(self.backups.exists())

    def test_rollback_after_shared_file_update_keeps_the_shared_file(self):
        manifest = json.loads((self.inst / "manifest.json").read_text())
        a = next(e for e in manifest["entries"] if e["file"].startswith("interface/a-"))
        manifest["entries"].append(dict(a, art={**a["art"], "sha256": hashlib.sha256(b"twin").hexdigest()}))
        (self.inst / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))
        before = tree(self.inst)
        self.assertEqual(self.apply()[0], 0)
        self.assertEqual(run("rollback", next(self.backups.iterdir()), "--installed", self.inst)[0], 0)
        self.assertEqual(tree(self.inst), before)

    def test_mirror_refuses_damaged_source_game_folder_destination_and_symlinks(self):
        dst = self.tmp / "drive" / "interface-redraw-4x"
        self.assertEqual(run("mirror", "--src", self.inst, "--dst", self.inst)[0], 2)
        self.assertEqual(run("mirror", "--src", self.stage, "--dst", hp.GAME_SUPPORT / "hd-packs" / "x", "--dry-run")[0], 2)
        dst.mkdir(parents=True)
        (dst / "sub").symlink_to(self.stage)
        self.assertEqual(run("mirror", "--src", self.inst, "--dst", dst, "--dry-run")[0], 2)
        (dst / "sub").unlink()
        next(self.inst.glob("interface/a-*.png")).unlink()
        self.assertEqual(run("mirror", "--src", self.inst, "--dst", dst, "--dry-run")[0], 2)

    def test_menu_rollback_checks_the_backup_and_zero_size_png_is_refused(self):
        img = self.tmp / "m.png"
        img.write_bytes(png(0, 0))
        rc, _, err = run("menu-art", "--installed", self.inst, "--image", img, "--backup-root", self.backups, "--task", "T")
        self.assertEqual(rc, 2)
        img.write_bytes(png(1664, 936) + b"\0")
        self.assertEqual(run("menu-art", "--installed", self.inst, "--image", img, "--backup-root", self.backups, "--task", "T")[0], 0)
        backup = next(self.backups.iterdir())
        (backup / "main-menu-16x9.png").write_bytes(b"tampered")
        rc, _, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 2)
        self.assertIn("does not match", err)
        (backup / "RECEIPT.json").write_text('{"status":"applied"}')
        self.assertEqual(run("rollback", backup, "--installed", self.inst)[0], 2)


if __name__ == "__main__":
    unittest.main()
