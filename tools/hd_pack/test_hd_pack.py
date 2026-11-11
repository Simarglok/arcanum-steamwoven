"""ARC-DEV-329: tools/hd_pack/hd_pack.py on synthetic packs (no original or derived art; the PNG bodies are random bytes)."""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
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
        self.base = self.tmp / "base-manifest.json"  # what installed and staging started from: installed is untouched
        self.base.write_bytes((self.inst / "manifest.json").read_bytes())

    def apply(self, *extra):
        base = [] if any(x in extra for x in ("--no-base-check", "--base")) else ["--base", self.base]
        return run("apply", "--installed", self.inst, "--staged", self.stage, "--backup-root", self.backups, "--task", "ARC-TEST-001", *base, *extra)

    def repair(self, art: str) -> str:
        """A local repair: the installed file of `art` gets new content under the same name and the manifest follows."""
        manifest = json.loads((self.inst / "manifest.json").read_text())
        e = next(x for x in manifest["entries"] if x["file"].startswith(f"interface/{art}-"))
        body = fake(f"repair:{art}", 64)
        (self.inst / e["file"]).write_bytes(body)
        e["sha256"], e["bytes"] = hashlib.sha256(body).hexdigest(), len(body)
        (self.inst / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))
        return e["file"]

    def test_plan_and_dry_run_change_nothing(self):
        before = tree(self.inst)
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--base", self.base)
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

    def test_no_base_is_refused_and_warned_about(self):
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage)
        self.assertIn("WARNING: no --base", out)
        before = tree(self.inst)
        rc, _, err = self.apply("--base", self.tmp / "missing.json")
        self.assertEqual(rc, 2)
        self.assertIn("not a readable pack manifest", err)
        rc, _, err = run("apply", "--installed", self.inst, "--staged", self.stage, "--backup-root", self.backups, "--task", "T")
        self.assertEqual(rc, 2)
        self.assertIn("no --base", err)
        self.assertEqual(tree(self.inst), before)
        self.assertEqual(self.apply("--no-base-check")[0], 0)

    def test_local_repair_is_not_reverted_by_a_staging_equal_to_the_base(self):
        self.repair("b")  # installed b is repaired; staging b is still the base's
        before = tree(self.inst)
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--base", self.base)
        self.assertIn("CONFLICT interface/b-", out)
        self.assertIn("repair kept by installed", out)
        rc, _, err = self.apply()
        self.assertEqual(rc, 2)
        self.assertIn("repaired in the installed pack", err)
        self.assertEqual(tree(self.inst), before)
        self.assertFalse(self.backups.exists())
        # skip: only a is updated, the repaired b stays byte for byte
        repaired_body = fake("repair:b", 64)
        self.assertEqual(self.apply("--conflicts", "skip")[0], 0)
        after = json.loads((self.inst / "manifest.json").read_text())
        b = next(e for e in after["entries"] if e["file"].startswith("interface/b-"))
        self.assertEqual(b["sha256"], hashlib.sha256(repaired_body).hexdigest())
        self.assertEqual((self.inst / b["file"]).read_bytes(), repaired_body)
        a = next(e for e in after["entries"] if e["file"].startswith("interface/a-"))
        self.assertEqual((self.inst / a["file"]).read_bytes(), fake("a:v2"))

    def test_overwrite_conflicts_reverts_on_purpose_and_rolls_back(self):
        repaired = self.repair("a")  # both sides changed a: installed repaired, staging rebuilt
        before = tree(self.inst)
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--base", self.base, "--conflicts", "overwrite")
        self.assertIn("changed on both sides", out)
        self.assertEqual(self.apply("--conflicts", "overwrite")[0], 0)
        self.assertNotEqual(tree(self.inst), before)
        self.assertEqual(run("rollback", next(self.backups.iterdir()), "--installed", self.inst)[0], 0)
        self.assertEqual(tree(self.inst), before)
        self.assertTrue((self.inst / repaired).is_file())

    def test_applied_staging_manifest_is_the_next_base(self):
        self.assertEqual(self.apply()[0], 0)
        backup = next(self.backups.iterdir())
        nxt = backup / "staged-manifest.json"
        self.assertTrue(nxt.is_file())
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--base", nxt)
        self.assertEqual(rc, 0)
        self.assertIn("0 interface entries differ", out)
        self.assertNotIn("CONFLICT", out)

    def test_repair_staging_with_the_installed_pack_as_base_changes_only_that_entry(self):
        self.repair("b")  # an earlier local repair that the new repair must not touch
        stage = self.tmp / "repair-stage"
        shutil.copytree(self.inst, stage)
        manifest = json.loads((stage / "manifest.json").read_text())
        e = next(x for x in manifest["entries"] if x["file"].startswith("interface/a-"))
        body = fake("repair-a", 70)
        e["file"] = "interface/a-repaired.png"
        e["sha256"], e["bytes"] = hashlib.sha256(body).hexdigest(), len(body)
        (stage / e["file"]).write_bytes(body)
        (stage / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))
        before = tree(self.inst)
        rc, out, err = run("apply", "--installed", self.inst, "--staged", stage, "--base", self.inst, "--backup-root", self.backups, "--task", "T")
        self.assertEqual(rc, 0, out + err)
        after = tree(self.inst)
        changed = {k for k in set(before) | set(after) if before.get(k) != after.get(k)}
        self.assertIn("interface/a-repaired.png", changed)
        self.assertEqual(sum(1 for k in changed if k.startswith("interface/b-")), 0)
        self.assertEqual(fake("repair-a", 70), (self.inst / "interface/a-repaired.png").read_bytes())
        self.assertNotIn("next time use", out)  # a repair apply is not the main line's base

    def test_folders_flag_lets_a_repair_replace_an_entry_of_a_codex_folder_and_nothing_else(self):
        # ARC-DEV-345: a repair of an entry that lives in character-hd114/ (a Codex folder) is invisible to apply unless the folder is named
        stage = self.tmp / "repair-stage"
        shutil.copytree(self.inst, stage)
        manifest = json.loads((stage / "manifest.json").read_text())
        e = next(x for x in manifest["entries"] if x["file"].startswith("character-hd114/c-"))
        body = fake("repair-c", 70)
        e["file"] = "character-hd114/c-repaired.png"
        e["sha256"], e["bytes"] = hashlib.sha256(body).hexdigest(), len(body)
        (stage / e["file"]).write_bytes(body)
        (stage / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))
        args = ("--installed", self.inst, "--staged", stage, "--base", self.inst)
        before = tree(self.inst)
        rc, out, _ = run("plan", *args)
        self.assertEqual(rc, 0)
        self.assertIn("0 interface entries differ", out)  # without --folders the Codex folder is an extra and stays
        rc, out, _ = run("plan", *args, "--folders", "character-hd114")
        self.assertIn("1 entries (interface and character-hd114) differ", out)
        rc, out, err = run("apply", *args, "--folders", "character-hd114", "--backup-root", self.backups, "--task", "T")
        self.assertEqual(rc, 0, out + err)
        after = tree(self.inst)
        changed = {k for k in set(before) | set(after) if before.get(k) != after.get(k)}
        old_c = next(k for k in before if k.startswith("character-hd114/c-") and k != "character-hd114/c-repaired.png")
        self.assertEqual(changed, {"manifest.json", old_c, "character-hd114/c-repaired.png"})
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 0)
        rc, _, err = run("rollback", next(self.backups.iterdir()), "--installed", self.inst)
        self.assertEqual(rc, 0, err)
        self.assertEqual(tree(self.inst), before)
        for bad in ("../x", "a/b", "-x", ".."):
            rc, _, err = run("plan", *args, f"--folders={bad}")
            self.assertNotEqual(rc, 0, bad)

    def test_mirror_destination_defaults_to_the_icloud_copy(self):
        # ARC-DEV-349: the cross-device copy is in the Obsidian vault in iCloud, no longer in Google Drive
        args = hp.parser().parse_args(["mirror"])
        self.assertEqual(args.dst, hp.DEFAULT_COPY)
        parts = hp.DEFAULT_COPY.parts
        self.assertEqual(parts[-3:], ("Arcanum Steamwoven", "hd_pack", "interface-redraw-4x"))
        self.assertIn("iCloud~md~obsidian", parts)
        self.assertFalse(any("GoogleDrive" in x for x in parts))
        self.assertEqual(hp.parser().parse_args(["mirror", "--dst", "/x/y"]).dst, Path("/x/y"))

    def test_installed_pack_as_base_takes_every_difference(self):
        # the installed pack is a valid base only for a staging folder built from it (the repair flow); against a rebuilt
        # line it makes installed equal base everywhere, so nothing can be a conflict (the doc says so)
        self.repair("b")
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--base", self.inst)
        self.assertEqual(rc, 0)
        self.assertNotIn("CONFLICT", out)
        self.assertIn("2 interface entries differ", out)

    def test_entry_missing_from_the_base_is_a_conflict_and_a_dropped_staged_only_entry_stays_dropped(self):
        base = json.loads(self.base.read_text())
        base["entries"] = [e for e in base["entries"] if not e["file"].startswith("interface/a-")]
        self.base.write_text(json.dumps(base))
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--base", self.base)
        self.assertEqual(rc, 0)
        self.assertIn("CONFLICT interface/a-", out)
        self.assertIn("not in the base", out)
        # a staged-only entry that the base had (and the installed pack dropped) is not added back by --add-new
        manifest = json.loads((self.inst / "manifest.json").read_text())
        manifest["entries"] = [e for e in manifest["entries"] if not e["file"].startswith("interface/b-")]
        (self.inst / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))
        self.base.write_bytes((self.inst / "manifest.json").read_bytes())
        full = json.loads(self.base.read_text())
        full["entries"].append(next(e for e in json.loads((self.stage / "manifest.json").read_text())["entries"] if e["file"].startswith("interface/b-")))
        self.base.write_text(json.dumps(full))
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--base", self.base, "--add-new")
        self.assertIn("staged-only entries: 0", out)

    def test_stage_refuses_installed_collection_and_dry_run_runs_nothing(self):
        plan = self.tmp / "plan.json"
        plan.write_text("{}")
        for bad in (self.inst, self.tmp / "local" / "hd-packs" / "x", hp.GAME_SUPPORT / "anything"):
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
