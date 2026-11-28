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
from unittest import mock
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
        # installed: two interface entries and one extra-folder entry (a Codex repair), plus an unlisted extra file
        self.inst = make_pack(self.tmp / "game" / "hd-packs" / "interface-redraw-4x",
                              [("a", "interface", "v1"), ("b", "interface", "v1"), ("c", "character-hd114", "v1")], {"extra-unlisted.png": png(1664, 936)})
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
        self.assertEqual(after["extra-unlisted.png"], before["extra-unlisted.png"])  # unlisted file untouched
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
        self.assertIn("extra-unlisted.png", out)
        self.assertIn("character-hd114 1", out)

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

    def test_old_menu_art_receipt_is_refused(self):
        # ARC-DEV-3030: menu-art is gone; a receipt from the old command is refused, nothing is touched
        backup = self.backups / "OLD-menu-none"
        backup.mkdir(parents=True)
        (backup / "RECEIPT.json").write_text('{"kind":"menu","task":"OLD","status":"applied","oldSha256":null,"newSha256":"0"}')
        before = tree(self.inst)
        rc, _, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 2)
        self.assertIn("menu-art receipt cannot be rolled back", err)
        self.assertEqual(tree(self.inst), before)
        self.assertNotIn("menu-art", hp.parser().format_help())  # the command is gone


class LayoutTests(unittest.TestCase):
    """ARC-DEV-376: relayout, the layout map, index, refresh and the update of a laid-out pack, on synthetic names."""

    MAP = "art\tscreen\tkind\tframes\nbtn\twindow-a\tbuttons\t3\nback\twindow-a\tpanels\t1\nico\twindow-b\ticons\t1\nfnt\tfonts\tfonts\t12\n"

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        self.inst = self.tmp / "game" / "hd-packs" / "interface-redraw-4x"
        self.backups = self.tmp / "game" / "hd-pack-backups"
        self.map = self.tmp / "map.tsv"
        self.map.write_text(self.MAP)
        real_users = hp.pack_users  # the owner's game or a smoke may be running while the tests run
        hp.pack_users = lambda pack: []
        self.addCleanup(setattr, hp, "pack_users", real_users)
        # old layout: two folders (one a Codex extra), a frame 0 and 1 of btn, a font frame, an unlisted extra file
        self.inst.mkdir(parents=True)
        entries = []
        for art, folder, frame, slice_ in (("btn", "interface", 0, "interface"), ("btn", "interface", 1, "interface"), ("back", "refinement02", 0, "interface-refinement02-soft"),
                                           ("ico", "character-hd114", 0, "character-hd114"), ("fnt", "interface", 31, "interface")):
            body = png(8, 8) + fake(f"{art}{frame}")
            rel = f"{folder}/{art}-f{frame}-{hashlib.sha256(body).hexdigest()[:8]}.png"
            (self.inst / folder).mkdir(exist_ok=True)
            (self.inst / rel).write_bytes(body)
            e = entry(art, rel, body, frame)
            e["slice"] = slice_
            e["legacy"] = {"width": 2, "height": 2, "rgbaSha256": "0" * 64}
            entries.append(e)
        recipe = b'{"kind":"arcanum-steamwoven-hd-recipe"}'
        (self.inst / "recipe.json").write_bytes(recipe)
        (self.inst / "extra-unlisted.png").write_bytes(png(1664, 936))
        manifest = {"schema": 1, "kind": "arcanum-steamwoven-hd-pack", "id": "interface-redraw-4x", "scale": 4,
                    "recipe": {"file": "recipe.json", "sha256": hashlib.sha256(recipe).hexdigest()}, "entries": entries}
        (self.inst / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))

    def relayout(self, *extra):
        return run("relayout", "--installed", self.inst, "--map", self.map, "--backup-root", self.backups, "--task", "ARC-TEST-376", *extra)

    def manifest(self):
        return json.loads((self.inst / "manifest.json").read_text())

    def test_a_running_process_on_the_pack_blocks_the_move_until_allowed(self):
        before = tree(self.inst)
        hp.pack_users = lambda pack: ["4242 viewer ARC_HD_PACK=" + str(pack)]
        try:
            rc, _, err = self.relayout()
            self.assertEqual(rc, 2)
            self.assertIn("a process is using this pack", err)
            self.assertEqual(tree(self.inst), before)
            rc, out, _ = self.relayout("--dry-run")
            self.assertEqual(rc, 0)
            self.assertIn("WARNING", out)
            self.assertEqual(self.relayout("--allow-running")[0], 0)
        finally:
            hp.pack_users = lambda pack: []

    def test_names_and_places(self):
        self.assertEqual(hp.layout.art_of("interface/magic-tech-penalty-f3-0123456789ab.png"), "magic-tech-penalty")
        self.assertEqual(hp.layout.art_of("interface/screen/kind/btn-f001.png"), "btn")
        self.assertEqual(hp.layout.art_of("interface/fonts/fonts/fnt/f031.png"), "fnt")
        lm = hp.layout.LayoutMap.parse(self.MAP)
        self.assertEqual(hp.layout.place("btn", {"frame": 2, "group": 0, "palette": 0}, lm), "interface/window-a/buttons/btn-f002.png")
        self.assertEqual(hp.layout.place("fnt", {"frame": 31, "group": 0, "palette": 2}, lm), "interface/fonts/fonts/fnt/f031-p2.png")
        with self.assertRaises(hp.layout.LayoutError):
            hp.layout.LayoutMap.parse("art\tscreen\tkind\nBad\ta\tb\n")
        with self.assertRaises(hp.layout.LayoutError):
            hp.layout.LayoutMap.parse("art\tscreen\tkind\nx\ta\tb\nx\ta\tc\n")

    def test_dry_run_changes_nothing(self):
        before = tree(self.inst)
        rc, out, _ = self.relayout("--dry-run")
        self.assertEqual(rc, 0)
        self.assertIn("5 of 5 entries move", out)
        self.assertEqual(tree(self.inst), before)
        self.assertFalse(self.backups.exists())

    def test_relayout_moves_every_entry_and_keeps_unlisted_files_then_is_idempotent(self):
        old = {e["sha256"]: e["file"] for e in self.manifest()["entries"]}
        rc, out, err = self.relayout()
        self.assertEqual(rc, 0, out + err)
        files = {e["file"] for e in self.manifest()["entries"]}
        self.assertEqual(files, {"interface/window-a/buttons/btn-f000.png", "interface/window-a/buttons/btn-f001.png", "interface/window-a/panels/back-f000.png",
                                 "interface/window-b/icons/ico-f000.png", "interface/fonts/fonts/fnt/f031.png"})
        self.assertEqual({e["sha256"] for e in self.manifest()["entries"]}, set(old))  # content unchanged
        for folder in ("refinement02", "character-hd114"):
            self.assertFalse((self.inst / folder).exists())
        self.assertTrue((self.inst / "extra-unlisted.png").is_file() and (self.inst / "recipe.json").is_file())
        self.assertTrue((self.inst / "layout-map.tsv").is_file())
        self.assertIn("window-a", (self.inst / "INDEX.md").read_text())
        self.assertIn("btn", (self.inst / "index.tsv").read_text())
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 0)
        rc, out, _ = run("relayout", "--installed", self.inst, "--backup-root", self.backups, "--task", "ARC-TEST-376b")
        self.assertEqual(rc, 0)
        self.assertIn("nothing to do", out)
        rc, out, _ = run("status", "--installed", self.inst)
        self.assertIn("interface/window-a 3", out)

    def test_rollback_restores_a_byte_identical_tree(self):
        before = tree(self.inst)
        self.assertEqual(self.relayout()[0], 0)
        backup = next(self.backups.iterdir())
        self.assertEqual(run("rollback", backup, "--installed", self.inst, "--dry-run")[0], 0)
        self.assertNotEqual(tree(self.inst), before)
        rc, out, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)
        self.assertEqual(run("rollback", backup, "--installed", self.inst)[0], 2)  # already rolled back

    def test_art_missing_from_the_map_collision_and_unlisted_target_are_refused_before_any_write(self):
        before = tree(self.inst)
        self.map.write_text("art\tscreen\tkind\nbtn\ta\tb\nback\ta\tb\n")
        rc, _, err = self.relayout()
        self.assertEqual(rc, 2)
        self.assertIn("no row for the ART", err)
        self.assertEqual(tree(self.inst), before)
        self.map.write_text(self.MAP)
        (self.inst / "interface" / "window-a").mkdir()
        (self.inst / "interface" / "window-a" / "buttons").mkdir()
        (self.inst / "interface" / "window-a" / "buttons" / "btn-f000.png").write_bytes(b"x")
        rc, _, err = self.relayout()
        self.assertEqual(rc, 2)
        self.assertIn("not in its manifest", err)

    def test_a_damaged_pack_is_not_relaid_out(self):
        e = self.manifest()["entries"][0]
        (self.inst / e["file"]).write_bytes(fake("other", len(fake("x"))))
        rc, out, err = self.relayout()
        self.assertEqual(rc, 2)
        self.assertIn("already damaged", err)

    def test_apply_on_a_laid_out_pack_keeps_places_and_places_new_entries_by_the_map(self):
        self.assertEqual(self.relayout()[0], 0)
        base = self.tmp / "base.json"
        base.write_bytes((self.inst / "manifest.json").read_bytes())
        stage = self.tmp / "local" / "pack-out" / "s"
        (stage / "interface").mkdir(parents=True)
        entries = []
        for art, frame, v in (("btn", 0, "v2"), ("ico", 0, "v1"), ("newart", 0, "v1")):
            body = png(8, 8) + fake(f"{art}{frame}{v}")
            rel = f"interface/{art}-f{frame}-{hashlib.sha256(body).hexdigest()[:8]}.png"
            (stage / rel).write_bytes(body)
            entries.append(entry(art, rel, body, frame))
        # staging has the same keys as the installed pack for btn f0 (changed) and ico f0 (the Codex slice: unchanged content differs only by slice rule)
        (stage / "manifest.json").write_text(json.dumps({"schema": 1, "kind": "arcanum-steamwoven-hd-pack", "id": "interface-redraw-4x", "scale": 4,
                                                          "recipe": {"file": "recipe.json", "sha256": "0" * 64}, "entries": entries}))
        common = ("--installed", self.inst, "--staged", stage, "--backup-root", self.backups, "--task", "ARC-TEST-377", "--base", base, "--add-new")
        rc, out, err = run("apply", *common)
        self.assertEqual(rc, 2)  # newart has no row
        self.assertIn("no row for the ART newart", err)
        more = self.tmp / "more.tsv"
        more.write_text("art\tscreen\tkind\nnewart\twindow-c\ticons\n")
        before = tree(self.inst)
        self.assertEqual(run("apply", *common, "--map", more, "--dry-run")[0], 0)
        self.assertEqual(tree(self.inst), before)
        rc, out, err = run("apply", *common, "--map", more)
        self.assertEqual(rc, 0, out + err)
        by_art = {e["file"]: e for e in self.manifest()["entries"]}
        self.assertIn("interface/window-c/icons/newart-f000.png", by_art)
        self.assertIn("interface/window-a/buttons/btn-f000.png", by_art)  # replaced in place
        self.assertEqual(by_art["interface/window-a/buttons/btn-f000.png"]["sha256"], next(e for e in entries if e["file"].startswith("interface/btn-"))["sha256"])
        self.assertIn("newart", (self.inst / "INDEX.md").read_text())
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 0)
        # the Codex slice entry (ico, slice character-hd114) is kept unless --slices names it
        self.assertEqual(by_art["interface/window-b/icons/ico-f000.png"]["slice"], "character-hd114")
        rc, out, err = run("rollback", next(p for p in self.backups.iterdir() if "377" in p.name), "--installed", self.inst)
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)

    def test_an_interrupted_rollback_can_be_run_again(self):
        before = tree(self.inst)
        self.assertEqual(self.relayout()[0], 0)
        backup = next(self.backups.iterdir())
        # a rollback that stopped after writing the old manifest: old paths back, new files and the map still there
        receipt = json.loads((backup / "RECEIPT.json").read_text())
        for old, new, _ in receipt["moves"]:
            hp.link_or_copy(self.inst / new, self.inst / old)
        (self.inst / "manifest.json").write_bytes((backup / "manifest.json").read_bytes())
        receipt["status"] = "rolling-back"
        (backup / "RECEIPT.json").write_text(json.dumps(receipt))
        rc, out, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)
        self.assertEqual(json.loads((backup / "RECEIPT.json").read_text())["status"], "rolled-back")
        self.assertEqual(run("rollback", backup, "--installed", self.inst)[0], 2)

    def test_the_packs_own_map_is_kept_byte_for_byte_and_apply_only_appends_rows(self):
        self.assertEqual(self.relayout()[0], 0)
        own = (self.inst / "layout-map.tsv")
        own.write_text(own.read_text() + "# owner note\n")
        rc, out, _ = run("relayout", "--installed", self.inst, "--backup-root", self.backups, "--task", "ARC-TEST-380")
        self.assertEqual(rc, 0)
        self.assertIn("nothing to do", out)
        self.assertTrue(own.read_text().endswith("# owner note\n"))
        more = self.tmp / "more.tsv"
        more.write_text("art\tscreen\tkind\nbtn\tother\tother\nnewart\twindow-c\ticons\nunusedart\twindow-d\ticons\n")  # btn is in the pack's map (its row wins); unusedart is no entry's
        stage = self.tmp / "local" / "pack-out" / "m"
        (stage / "interface").mkdir(parents=True)
        body = png(8, 8) + fake("newart")
        rel = f"interface/newart-f0-{hashlib.sha256(body).hexdigest()[:8]}.png"
        (stage / rel).write_bytes(body)
        e = entry("newart", rel, body)
        e["legacy"] = {"width": 2, "height": 2, "rgbaSha256": "0" * 64}
        (stage / "manifest.json").write_text(json.dumps({"schema": 1, "kind": "arcanum-steamwoven-hd-pack", "id": "x", "scale": 4, "recipe": {"file": "recipe.json", "sha256": "0" * 64}, "entries": [e]}))
        old_text = own.read_text()
        rc, out, err = run("apply", "--installed", self.inst, "--staged", stage, "--backup-root", self.backups, "--task", "ARC-TEST-381", "--no-base-check", "--add-new", "--map", more)
        self.assertEqual(rc, 0, out + err)
        self.assertTrue(own.read_text().startswith(old_text))
        self.assertIn("newart\twindow-c\ticons\t0", own.read_text())
        self.assertNotIn("other", own.read_text())
        self.assertNotIn("unusedart", own.read_text())

    def test_a_pack_map_that_cannot_take_rows_stops_apply_before_any_write(self):
        self.assertEqual(self.relayout()[0], 0)
        (self.inst / "layout-map.tsv").write_text("art\tscreen\tkind\nbtn\ta\tb\n")  # no folder column
        stage = self.tmp / "local" / "pack-out" / "h"
        (stage / "interface").mkdir(parents=True)
        body = png(8, 8) + fake("zzz")
        rel = f"interface/zzz-f0-{hashlib.sha256(body).hexdigest()[:8]}.png"
        (stage / rel).write_bytes(body)
        e = entry("zzz", rel, body)
        e["legacy"] = {"width": 2, "height": 2, "rgbaSha256": "0" * 64}
        (stage / "manifest.json").write_text(json.dumps({"schema": 1, "kind": "arcanum-steamwoven-hd-pack", "id": "x", "scale": 4, "recipe": {"file": "recipe.json", "sha256": "0" * 64}, "entries": [e]}))
        more = self.tmp / "m.tsv"
        more.write_text("art\tscreen\tkind\nzzz\ta\tb\n")
        before = tree(self.inst)
        rc, _, err = run("apply", "--installed", self.inst, "--staged", stage, "--backup-root", self.backups, "--task", "ARC-TEST-383", "--no-base-check", "--add-new", "--map", more)
        self.assertEqual(rc, 2)
        self.assertIn("cannot add rows", err)
        self.assertEqual(tree(self.inst), before)
        self.assertFalse(self.backups.exists() and any("383" in p.name for p in self.backups.iterdir()))

    def test_map_on_an_old_layout_pack_and_a_refresh_rollback_are_refused(self):
        stage = self.tmp / "s"
        stage.mkdir()
        shutil.copy2(self.inst / "manifest.json", stage / "manifest.json")
        rc, _, err = run("plan", "--installed", self.inst, "--staged", stage, "--map", self.map)
        self.assertEqual(rc, 2)
        self.assertIn("run relayout first", err)
        self.assertEqual(self.relayout()[0], 0)
        e = self.manifest()["entries"][0]
        (self.inst / e["file"]).write_bytes(png(8, 8) + b"edit")
        self.assertEqual(run("refresh", "--installed", self.inst, "--backup-root", self.backups, "--task", "ARC-TEST-382")[0], 0)
        backup = next(p for p in self.backups.iterdir() if "refresh" in p.name)
        rc, _, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 2)
        self.assertIn("cannot be rolled back", err)

    def test_refresh_takes_an_edited_png_into_the_manifest(self):
        self.assertEqual(self.relayout()[0], 0)
        e = self.manifest()["entries"][0]
        edited = png(8, 8) + b"edited by the owner"
        (self.inst / e["file"]).write_bytes(edited)
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 1)
        rc, out, _ = run("refresh", "--installed", self.inst, "--backup-root", self.backups, "--task", "ARC-TEST-378", "--dry-run")
        self.assertEqual(rc, 0)
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 1)
        rc, out, err = run("refresh", "--installed", self.inst, "--backup-root", self.backups, "--task", "ARC-TEST-378")
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 0)
        self.assertEqual(self.manifest()["entries"][0]["sha256"], hashlib.sha256(edited).hexdigest())
        self.assertIn("nothing to do", run("refresh", "--installed", self.inst, "--backup-root", self.backups, "--task", "ARC-TEST-379")[1])

    def test_refresh_refuses_a_png_of_the_wrong_size(self):
        e = json.loads((self.inst / "manifest.json").read_text())["entries"][0]
        (self.inst / e["file"]).write_bytes(png(9, 8))
        rc, _, err = run("refresh", "--installed", self.inst, "--backup-root", self.backups, "--task", "ARC-TEST-378")
        self.assertEqual(rc, 2)
        self.assertIn("needs 8-bit RGBA", err)


class SplashTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        self.inst = make_pack(self.tmp / "game" / "hd-packs" / "interface-redraw-4x", [("a", "interface", "v1")])
        self.backups = self.tmp / "game" / "hd-pack-backups"
        self.pictures = self.tmp / "pictures"
        self.pictures.mkdir()

    def splash(self, *extra):
        return run("splash", "--pictures", self.pictures, "--task", "ARC-UI-3501", "--installed", self.inst, "--backup-root", self.backups, *extra)

    def test_adds_new_pictures_and_touches_nothing_else(self):
        (self.pictures / "splash1-16x9.png").write_bytes(png(3840, 2160))
        (self.pictures / "splash3-16x9.png").write_bytes(png(16, 9))
        before = tree(self.inst)
        rc, out, err = self.splash("--dry-run")
        self.assertEqual(rc, 0, out + err)
        self.assertIn("DRY-RUN", out)
        self.assertEqual(tree(self.inst), before)
        rc, out, err = self.splash()
        self.assertEqual(rc, 0, out + err)
        after = tree(self.inst)
        self.assertEqual(sorted(set(after) - set(before)), ["splash/splash1-16x9.png", "splash/splash3-16x9.png"])
        self.assertEqual({k: v for k, v in after.items() if k in before}, before)
        rc, out, _ = self.splash()
        self.assertEqual(rc, 0)
        self.assertIn("up to date", out)

    def test_a_different_picture_needs_replace_and_keeps_the_old_one(self):
        (self.pictures / "splash2-16x9.png").write_bytes(png(16, 9))
        self.assertEqual(self.splash()[0], 0)
        old = (self.inst / "splash" / "splash2-16x9.png").read_bytes()
        (self.pictures / "splash2-16x9.png").write_bytes(png(32, 18))
        rc, _, err = self.splash()
        self.assertEqual(rc, 2)
        self.assertIn("--replace", err)
        self.assertEqual((self.inst / "splash" / "splash2-16x9.png").read_bytes(), old)
        rc, out, err = self.splash("--replace")
        self.assertEqual(rc, 0, out + err)
        self.assertEqual([p.read_bytes() for p in self.backups.rglob("splash2-16x9.png")], [old])

    def test_wrong_pictures_are_refused(self):
        rc, _, err = self.splash()
        self.assertEqual(rc, 2)
        self.assertIn("none of", err)
        before = tree(self.inst)
        for bad in (png(1920, 1200), png(3856, 2169), png(16, 9, ctype=2), png(16, 9, interlace=1), b"not a png"):
            (self.pictures / "splash1-16x9.png").write_bytes(bad)
            self.assertEqual(self.splash()[0], 2)
        self.assertEqual(tree(self.inst), before)


class PackUsersTests(unittest.TestCase):
    def test_both_packaged_apps_count_as_readers_of_a_pack(self):
        """ARC-DEV-3379: a running player or profiling package reads the pack, so a relayout refuses."""
        lines = ["101 /a/macos/Arcanum Steamwoven.app/Contents/MacOS/Arcanum Steamwoven",
                 "102 /a/macos-profiling/Arcanum Steamwoven Profiling.app/Contents/MacOS/Arcanum Steamwoven Profiling",
                 "103 /usr/bin/python3 other.py"]
        done = mock.Mock(stdout="\n".join(lines))
        with mock.patch.object(hp.subprocess, "run", return_value=done):
            found = hp.pack_users(Path("/nowhere/pack"))
        self.assertEqual([item.split()[0] for item in found], ["101", "102"])


class FontsPlanTests(unittest.TestCase):
    """ARC-DEV-361: `fonts-plan` names traced font frames for the importer; synthetic font folders only."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        (self.tmp / ".git").mkdir()  # the checkout root: local/ beside .git is the ignored folder
        self.fonts = self.tmp / "local" / "hd-font" / "out"
        self.out = self.tmp / "local" / "hd-font" / "fonts-plan.json"
        (self.fonts).mkdir(parents=True)
        (self.fonts / "receipt.json").write_text("{}")
        self.font(27, "morph15font.art", [(0, True, False, False), (1, True, False, True), (2, False, False, True), (3, True, True, True)])
        self.font(229, "Flare12Font.art", [(0, True, False, True)])

    def font(self, number: int, file: str, frames: list[tuple[int, bool, bool, bool]]) -> Path:
        """frames: (frame, ink, vanished, grey)"""
        folder = self.fonts / f"{number}-{file.split('.')[0].lower()}"
        folder.mkdir()
        listed = []
        for frame, ink, vanished, grey in frames:
            body = fake(f"{number}:{frame}", 64)
            (folder / f"frame-{frame:03d}.png").write_bytes(body)
            listed.append({"frame": frame, "file": f"frame-{frame:03d}.png", "ink": ink, "vanished": vanished, "grey": grey, "pngSha256": hashlib.sha256(body).hexdigest()})
        (folder / "font.json").write_text(json.dumps({"tool": "hd_font", "art": number, "file": file, "scale": 4, "frames": listed}))
        return folder

    def test_plan_names_only_grey_frames_with_ink_and_relative_paths(self):
        rc, out, _ = run("fonts-plan", "--fonts", self.fonts, "--out", self.out, "--dry-run")
        self.assertEqual(rc, 0)
        self.assertIn("DRY-RUN", out)
        self.assertFalse(self.out.exists())
        rc, out, _ = run("fonts-plan", "--fonts", self.fonts, "--out", self.out)
        self.assertEqual(rc, 0, out)
        plan = json.loads(self.out.read_text())
        self.assertEqual((plan["kind"], plan["packId"], plan["scale"]), (hp.FONT_PLAN_KIND, "interface-fonts", 4))
        first, second = plan["items"]
        self.assertTrue(first["mode"] == "ready" and first["coverage"] is True)
        self.assertEqual([t["frame"] for t in first["tiles"]], [1])  # frame 0 is coloured, 2 has no ink, 3 vanished in the trace
        self.assertEqual(first["tiles"][0]["art"], "morph15font.art")
        self.assertEqual(first["images"], ["out/27-morph15font/frame-001.png"])
        self.assertEqual((second["tiles"], second["images"]), ([{"art": "Flare12Font.art", "frame": 0}], ["out/229-flare12font/frame-000.png"]))

    def test_plan_keys_are_the_importers(self):
        run("fonts-plan", "--fonts", self.fonts, "--out", self.out)
        plan = json.loads(self.out.read_text())
        self.assertEqual(set(plan), {"schema", "kind", "packId", "scale", "items"})
        self.assertEqual(set(plan["items"][0]), {"mode", "coverage", "images", "tiles"})  # HdInterfacePlan.Parse reads exactly these
        self.assertEqual(set(plan["items"][0]["tiles"][0]), {"art", "frame"})
        run("fonts-plan", "--fonts", self.fonts, "--out", self.out)  # an own plan is replaced
        self.assertEqual(run("fonts-plan", "--fonts", self.fonts, "--out", self.out)[0], 0)

    def test_malformed_frame_entries_are_refused_not_a_traceback(self):
        meta = self.fonts / "27-morph15font" / "font.json"
        data = json.loads(meta.read_text())
        for bad in (["x"], [{"ink": True, "grey": True, "file": "frame-001.png", "frame": 1}], [{"ink": True, "grey": True, "file": 5, "frame": 1, "pngSha256": "a"}]):
            meta.write_text(json.dumps(dict(data, frames=bad)))
            self.assertEqual(run("fonts-plan", "--fonts", self.fonts, "--out", self.out)[0], 2, bad)

    def test_art_filter_and_pack_id(self):
        rc, _, _ = run("fonts-plan", "--fonts", self.fonts, "--out", self.out, "--art", "229", "--pack-id", "fonts-x")
        self.assertEqual(rc, 0)
        plan = json.loads(self.out.read_text())
        self.assertEqual((plan["packId"], [t["tiles"][0]["art"] for t in plan["items"]]), ("fonts-x", ["Flare12Font.art"]))
        self.assertEqual(run("fonts-plan", "--fonts", self.fonts, "--out", self.out, "--art", "999")[0], 2)
        self.assertEqual(run("fonts-plan", "--fonts", self.fonts, "--out", self.out, "--art", "x")[0], 2)

    def test_a_changed_frame_a_bad_location_and_a_foreign_file_are_refused(self):
        next(self.fonts.glob("27-*/frame-001.png")).write_bytes(b"edited")
        rc, _, err = run("fonts-plan", "--fonts", self.fonts, "--out", self.out)
        self.assertEqual(rc, 2)
        self.assertIn("SHA-256", err)
        self.assertFalse(self.out.exists())
        self.assertEqual(run("fonts-plan", "--fonts", self.fonts, "--out", self.tmp / "elsewhere" / "plan.json")[0], 2)  # not under local/
        self.assertEqual(run("fonts-plan", "--fonts", self.fonts, "--out", self.tmp / "local" / "plan.txt")[0], 2)
        self.assertEqual(run("fonts-plan", "--fonts", self.fonts, "--out", self.tmp / "local" / "game" / "hd-packs" / "p.json")[0], 2)
        self.assertEqual(run("fonts-plan", "--fonts", self.fonts, "--out", self.tmp / "local" / "res095" / "pack-out" / "x" / "p.json")[0], 2)
        self.assertEqual(run("fonts-plan", "--fonts", self.fonts, "--out", self.tmp / "src" / "local" / "p.json")[0], 2)  # a local/ that is not beside .git
        main_plan = self.tmp / "local" / "ready" / "plan.json"  # the pipeline's own plan: right kind, no coverage items
        main_plan.parent.mkdir()
        main_plan.write_text(json.dumps({"kind": hp.FONT_PLAN_KIND, "items": [{"mode": "sheet"}]}))
        rc, _, err = run("fonts-plan", "--fonts", self.fonts, "--out", main_plan)
        self.assertEqual(rc, 2)
        self.assertIn("refusing to overwrite", err)
        self.assertIn("sheet", main_plan.read_text())
        foreign = self.tmp / "local" / "other.json"
        foreign.write_text('{"kind":"something-else"}')
        rc, _, err = run("fonts-plan", "--fonts", self.fonts, "--out", foreign)
        self.assertEqual(rc, 2)
        self.assertIn("refusing to overwrite", err)
        no_receipt = self.tmp / "local" / "plain"
        no_receipt.mkdir()
        self.assertEqual(run("fonts-plan", "--fonts", no_receipt, "--out", self.out)[0], 2)


def portrait_entry(path: str, rel: str, body: bytes) -> dict:
    return {"kind": "portrait", "file": rel, "sha256": hashlib.sha256(body).hexdigest(), "bytes": len(body), "slice": "interface",
            "portrait": {"path": path}, "legacy": {"width": 2, "height": 2, "rgbaSha256": hashlib.sha256(path.encode()).hexdigest()}, "review": "unreviewed"}


class BitmapPackFixture(unittest.TestCase):
    """An installed pack with a layout map and one button, an empty staging folder, and the helpers of the bitmap-entry tests (portraits, ARC-UI-2865; world maps, ARC-RENDER-3079)."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        real_users = hp.pack_users
        hp.pack_users = lambda pack: []
        self.addCleanup(setattr, hp, "pack_users", real_users)
        self.inst = self.tmp / "game" / "hd-packs" / "interface-redraw-4x"
        self.backups = self.tmp / "game" / "hd-pack-backups"
        self.inst.mkdir(parents=True)
        body = png(8, 8) + fake("btn")
        rel = "interface/window-a/buttons/btn-f000.png"
        (self.inst / "interface" / "window-a" / "buttons").mkdir(parents=True)
        (self.inst / rel).write_bytes(body)
        e = entry("btn", rel, body)
        e["legacy"] = {"width": 2, "height": 2, "rgbaSha256": "0" * 64}
        self.write_pack(self.inst, [e])
        (self.inst / "layout-map.tsv").write_text("art\tscreen\tkind\tfolder\nbtn\twindow-a\tbuttons\t0\n")
        self.assertEqual(run("index", "--installed", self.inst)[0], 0)  # a laid-out pack has its INDEX.md and index.tsv, which apply rewrites and rollback restores
        self.stage = self.tmp / "local" / "res095" / "pack-out" / "interface-portraits"
        self.stage.mkdir(parents=True)
        (self.stage / "interface").mkdir()

    @staticmethod
    def write_pack(root: Path, entries: list) -> None:
        recipe = b'{"kind":"arcanum-steamwoven-hd-recipe"}'
        (root / "recipe.json").write_bytes(recipe)
        manifest = {"schema": 1, "kind": "arcanum-steamwoven-hd-pack", "id": root.name, "scale": 4,
                    "recipe": {"file": "recipe.json", "sha256": hashlib.sha256(recipe).hexdigest()}, "entries": entries}
        (root / "manifest.json").write_text(json.dumps(manifest, separators=(",", ":")))

    def manifest(self):
        return json.loads((self.inst / "manifest.json").read_text())

    def apply(self, *extra):
        return run("apply", "--installed", self.inst, "--staged", self.stage, "--backup-root", self.backups, "--task", "ARC-TEST-2865", "--no-base-check", "--add-new", *extra)


class PortraitTests(BitmapPackFixture):
    """ARC-UI-2865: portrait entries (kind portrait, keyed by the original bitmap's path) through plan, apply, verify, mirror and rollback, and the portrait plan."""

    def setUp(self):
        super().setUp()
        entries = []
        for path, stem in (("portrait/hum1_b.bmp", "hum1_b"), ("portrait/npcdog.bmp", "npcdog")):
            body = png(8, 8) + fake(stem)
            rel = f"interface/portrait-{stem}-f0-{hashlib.sha256(body).hexdigest()[:8]}.png"
            (self.stage / rel).write_bytes(body)
            entries.append(portrait_entry(path, rel, body))
        self.write_pack(self.stage, entries)

    def test_the_key_of_a_portrait_is_its_bitmap_path(self):
        a = portrait_entry("portrait/hum1_b.bmp", "interface/x.png", b"a")
        b = portrait_entry("portrait/hum1.bmp", "interface/y.png", b"b")
        self.assertEqual(hp.entry_key(a), ("portrait", "portrait/hum1_b.bmp"))
        self.assertNotEqual(hp.entry_key(a), hp.entry_key(b))
        self.assertEqual(len(hp.keyed({"entries": [a, b]}, "t")), 2)

    def test_apply_adds_portraits_by_default_rows_and_appends_them_to_the_map(self):
        before = tree(self.inst)
        rc, out, err = self.apply("--dry-run")
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)
        self.assertIn("2 new entries are placed by it", out)
        rc, out, err = self.apply()
        self.assertEqual(rc, 0, out + err)
        files = {e["file"] for e in self.manifest()["entries"]}
        self.assertEqual(files, {"interface/window-a/buttons/btn-f000.png", "interface/portraits/heroes/portrait-hum1_b-f000.png", "interface/portraits/npcs/portrait-npcdog-f000.png"})
        rows = (self.inst / "layout-map.tsv").read_text()
        self.assertIn("portrait-hum1_b\tportraits\theroes\t0", rows)
        self.assertIn("portrait-npcdog\tportraits\tnpcs\t0", rows)
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 0)
        self.assertIn("portrait-npcdog", (self.inst / "INDEX.md").read_text())
        # the same staging again changes nothing; the backup rolls the pack back byte for byte
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--add-new")
        self.assertIn("staged-only entries: 0", out)
        backup = next(self.backups.iterdir())
        rc, out, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)

    def test_a_changed_portrait_replaces_only_that_entry(self):
        self.assertEqual(self.apply()[0], 0)
        stage = {e["portrait"]["path"]: e for e in json.loads((self.stage / "manifest.json").read_text())["entries"]}
        new = png(8, 8) + fake("hum1_b v2")
        rel = f"interface/portrait-hum1_b-f0-{hashlib.sha256(new).hexdigest()[:8]}.png"
        (self.stage / rel).write_bytes(new)
        entries = [portrait_entry("portrait/hum1_b.bmp", rel, new), stage["portrait/npcdog.bmp"]]
        self.write_pack(self.stage, entries)
        rc, out, err = run("plan", "--installed", self.inst, "--staged", self.stage, "--add-new")
        self.assertEqual(rc, 0, out + err)
        self.assertIn("1 interface entries differ", out)
        self.assertIn("replace interface/portraits/heroes/portrait-hum1_b-f000.png", out)

    def test_portraits_plan_checks_sums_sizes_and_pairs_large_with_small(self):
        local = self.tmp / "local"
        hero, npc = local / "hero", local / "npc"
        for folder in (hero / "large", hero / "small", npc):
            folder.mkdir(parents=True)
        sums = {}
        for folder, rel, side in ((hero, "large/hum1_b.png", 512), (hero, "small/hum1.png", 256), (npc, "npcdog.png", 256)):
            data = png(side, side, ctype=2) + fake(rel)
            (folder / rel).write_bytes(data)
            sums.setdefault(folder, []).append(f"{hashlib.sha256(data).hexdigest()}  ./{rel}")
        for folder, lines in sums.items():
            (folder / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n")
        (self.tmp / ".git").mkdir()  # local/ must sit beside a .git
        out = local / "plans" / "p.json"
        rc, text, err = run("portraits-plan", "--hero", hero, "--npc", npc, "--out", out)
        self.assertEqual(rc, 0, text + err)
        plan = json.loads(out.read_text())
        self.assertEqual(plan["kind"], hp.PORTRAIT_PLAN_KIND)
        self.assertEqual(plan["scale"], 4)
        self.assertEqual({i["bmp"] for i in plan["items"]}, {"portrait/hum1_b.bmp", "portrait/hum1.bmp", "portrait/npcdog.bmp"})
        self.assertTrue(all((out.parent / i["image"]).is_file() for i in plan["items"]))
        # a delivery that changed after it was packed, a wrong size and a large image without its small one are refused
        (npc / "npcdog.png").write_bytes(png(256, 256, ctype=2) + b"changed")
        self.assertEqual(run("portraits-plan", "--npc", npc, "--out", out)[0], 2)
        (npc / "npcdog.png").write_bytes(png(128, 128, ctype=2))
        (npc / "SHA256SUMS.txt").write_text(f"{hashlib.sha256((npc / 'npcdog.png').read_bytes()).hexdigest()}  ./npcdog.png\n")
        rc, _, err = run("portraits-plan", "--npc", npc, "--out", out)
        self.assertEqual(rc, 2)
        self.assertIn("expected 256x256", err)
        (hero / "small" / "hum1.png").unlink()
        self.assertEqual(run("portraits-plan", "--hero", hero, "--out", out)[0], 2)
        self.assertEqual(run("portraits-plan", "--out", out)[0], 2)


def map_entry(kind: str, path: str, rel: str, body: bytes) -> dict:
    side = 365 if kind == "map-zoomed" else 250
    return {"kind": kind, "file": rel, "sha256": hashlib.sha256(body).hexdigest(), "bytes": len(body), "slice": "interface",
            kind: {"path": path}, "legacy": {"width": side, "height": side, "rgbaSha256": hashlib.sha256(path.encode()).hexdigest()}, "review": "unreviewed"}


class WorldMapTests(BitmapPackFixture):
    """ARC-RENDER-3079: `worldmap` and `map-zoomed` entries (keyed by the original bitmap's path) through plan (dry run first), apply, verify and rollback, and the world-map plan.
    The staging build of the portrait tests is replaced by one tile and the continent picture."""

    def setUp(self):
        super().setUp()
        entries = []
        for kind, path, stem in (("worldmap", "worldmap/smallmapchunks001.bmp", "smallmapchunks001"), ("map-zoomed", "worldmap/map_zoomed.bmp", "map_zoomed")):
            body = png(8, 8) + fake(stem)
            rel = f"interface/{kind}-{stem}-f0-{hashlib.sha256(body).hexdigest()[:8]}.png"
            (self.stage / rel).write_bytes(body)
            entries.append(map_entry(kind, path, rel, body))
        self.write_pack(self.stage, entries)

    def test_the_key_of_a_map_entry_is_its_kind_and_bitmap_path(self):
        a = map_entry("worldmap", "worldmap/smallmapchunks001.bmp", "interface/x.png", b"a")
        b = map_entry("map-zoomed", "worldmap/map_zoomed.bmp", "interface/y.png", b"b")
        self.assertEqual(hp.entry_key(a), ("worldmap", "worldmap/smallmapchunks001.bmp"))
        self.assertEqual(hp.entry_key(b), ("map-zoomed", "worldmap/map_zoomed.bmp"))
        self.assertEqual(len(hp.keyed({"entries": [a, b]}, "t")), 2)

    def test_apply_adds_map_entries_by_default_rows_and_rolls_back(self):
        before = tree(self.inst)
        rc, out, err = self.apply("--dry-run")
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)
        self.assertIn("2 new entries are placed by it", out)
        rc, out, err = self.apply()
        self.assertEqual(rc, 0, out + err)
        files = {e["file"] for e in self.manifest()["entries"]}
        self.assertEqual(files, {"interface/window-a/buttons/btn-f000.png", "interface/world-map/maps/worldmap-smallmapchunks001-f000.png", "interface/world-map/maps/map-zoomed-map_zoomed-f000.png"})
        rows = (self.inst / "layout-map.tsv").read_text()
        self.assertIn("worldmap-smallmapchunks001\tworld-map\tmaps\t0", rows)
        self.assertIn("map-zoomed-map_zoomed\tworld-map\tmaps\t0", rows)
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 0)
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--add-new")
        self.assertIn("staged-only entries: 0", out)
        backup = next(self.backups.iterdir())
        rc, out, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)

    def test_a_changed_tile_replaces_only_that_entry(self):
        self.assertEqual(self.apply()[0], 0)
        stage = {e["kind"]: e for e in json.loads((self.stage / "manifest.json").read_text())["entries"]}
        new = png(8, 8) + fake("tile v2")
        rel = f"interface/worldmap-smallmapchunks001-f0-{hashlib.sha256(new).hexdigest()[:8]}.png"
        (self.stage / rel).write_bytes(new)
        self.write_pack(self.stage, [map_entry("worldmap", "worldmap/smallmapchunks001.bmp", rel, new), stage["map-zoomed"]])
        rc, out, err = run("plan", "--installed", self.inst, "--staged", self.stage, "--add-new")
        self.assertEqual(rc, 0, out + err)
        self.assertIn("1 interface entries differ", out)
        self.assertIn("replace interface/world-map/maps/worldmap-smallmapchunks001-f000.png", out)

    def test_maps_plan_checks_names_sizes_sums_and_completeness(self):
        local = self.tmp / "local"
        maps = local / "maps"
        maps.mkdir(parents=True)
        (self.tmp / ".git").mkdir()  # local/ must sit beside a .git
        out = local / "plans" / "m.json"
        for n in range(1, 65):
            (maps / f"smallmapchunks{n:03d}.png").write_bytes(png(1000, 1000, ctype=2) + fake(str(n)))
        (maps / "map_zoomed.png").write_bytes(png(1460, 1460, ctype=6) + fake("z"))
        rc, text, err = run("maps-plan", "--maps", maps, "--out", out, "--dry-run")
        self.assertEqual(rc, 0, text + err)
        self.assertIn("tiles: 64 images", text)
        self.assertIn("DRY-RUN", text)
        self.assertFalse(out.exists())
        rc, text, err = run("maps-plan", "--maps", maps, "--out", out)
        self.assertEqual(rc, 0, text + err)
        plan = json.loads(out.read_text())
        self.assertEqual((plan["kind"], plan["scale"], len(plan["items"])), (hp.MAP_PLAN_KIND, 4, 65))
        self.assertIn({"bmp": "worldmap/map_zoomed.bmp", "image": "../maps/map_zoomed.png"}, plan["items"])
        self.assertEqual(plan["items"][0]["bmp"], "worldmap/smallmapchunks001.bmp")
        # a delivery with a sums file that no longer matches, a wrong size, a missing tile and a stray name are refused
        (maps / "SHA256SUMS.txt").write_text("".join(f"{hashlib.sha256(p.read_bytes()).hexdigest()}  ./{p.name}\n" for p in sorted(maps.glob("*.png"))))
        self.assertEqual(run("maps-plan", "--maps", maps, "--out", out, "--dry-run")[0], 0)
        (maps / "smallmapchunks010.png").write_bytes(png(1000, 1000, ctype=2) + b"changed")
        self.assertEqual(run("maps-plan", "--maps", maps, "--out", out, "--dry-run")[0], 2)
        (maps / "SHA256SUMS.txt").unlink()
        (maps / "smallmapchunks010.png").write_bytes(png(500, 500, ctype=2))
        rc, _, err = run("maps-plan", "--maps", maps, "--out", out, "--dry-run")
        self.assertEqual(rc, 2)
        self.assertIn("expected 1000x1000", err)
        self.assertEqual(run("maps-plan", "--maps", maps, "--out", out, "--scale", "2", "--dry-run")[0], 2)
        (maps / "smallmapchunks010.png").unlink()
        rc, _, err = run("maps-plan", "--maps", maps, "--out", out, "--dry-run")
        self.assertEqual(rc, 2)
        self.assertIn("missing [10]", err)
        self.assertEqual(run("maps-plan", "--maps", maps, "--out", out, "--partial", "--dry-run")[0], 0)
        (maps / "extra.png").write_bytes(png(1000, 1000, ctype=2))
        self.assertEqual(run("maps-plan", "--maps", maps, "--out", out, "--partial", "--dry-run")[0], 2)


def town_entry(path: str, rel: str, body: bytes) -> dict:
    return {"kind": "townmap", "file": rel, "sha256": hashlib.sha256(body).hexdigest(), "bytes": len(body), "slice": "interface",
            "townmap": {"path": path}, "legacy": {"width": 64, "height": 32, "rgbaSha256": hashlib.sha256(path.encode()).hexdigest()}, "review": "unreviewed"}


class TownMapTests(BitmapPackFixture):
    """ARC-RENDER-3106: `townmap` entries (keyed by the original tile's path) through plan (dry run first), apply into a folder per town, verify and rollback, and the town-map plan."""

    TILES = (("townmap/ashbury/ashbury000000.bmp", "ashbury-000000"), ("townmap/ashbury/ashbury000748.bmp", "ashbury-000748"),
             ("townmap/ashbury crematorium - lev 1/ashbury crematorium - lev 1000001.bmp", "ashbury-crematorium---lev-1-000001"))

    def setUp(self):
        super().setUp()
        entries = []
        for path, stem in self.TILES:
            body = png(8, 8) + fake(stem)
            rel = f"interface/townmap-{stem}-f0-{hashlib.sha256(body).hexdigest()[:8]}.png"
            (self.stage / rel).write_bytes(body)
            entries.append(town_entry(path, rel, body))
        self.write_pack(self.stage, entries)

    def test_the_key_of_a_town_tile_is_its_kind_and_tile_path(self):
        a = town_entry("townmap/ashbury/ashbury000748.bmp", "interface/x.png", b"a")
        b = town_entry("townmap/ashbury/ashbury000749.bmp", "interface/y.png", b"b")
        self.assertEqual(hp.entry_key(a), ("townmap", "townmap/ashbury/ashbury000748.bmp"))
        self.assertEqual(len(hp.keyed({"entries": [a, b]}, "t")), 2)

    def test_default_rows_put_a_towns_tiles_in_one_folder(self):
        rows = hp.town_rows(hp.keyed(json.loads((self.stage / "manifest.json").read_text()), "stage"))
        self.assertEqual(rows["townmap-ashbury-000748"], ("town-map", "ashbury", False))
        self.assertEqual(rows["townmap-ashbury-crematorium---lev-1-000001"], ("town-map", "ashbury-crematorium---lev-1", False))

    def test_apply_adds_town_entries_by_default_rows_and_rolls_back(self):
        before = tree(self.inst)
        rc, out, err = self.apply("--dry-run")
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)
        self.assertIn("3 new entries are placed by it", out)
        rc, out, err = self.apply()
        self.assertEqual(rc, 0, out + err)
        files = {e["file"] for e in self.manifest()["entries"]}
        self.assertEqual(files, {"interface/window-a/buttons/btn-f000.png", "interface/town-map/ashbury/townmap-ashbury-000000-f000.png", "interface/town-map/ashbury/townmap-ashbury-000748-f000.png",
                                 "interface/town-map/ashbury-crematorium---lev-1/townmap-ashbury-crematorium---lev-1-000001-f000.png"})
        rows = (self.inst / "layout-map.tsv").read_text()
        self.assertIn("townmap-ashbury-000748\ttown-map\tashbury\t0", rows)
        self.assertEqual(run("verify", "--installed", self.inst, "--deep")[0], 0)
        rc, out, _ = run("plan", "--installed", self.inst, "--staged", self.stage, "--add-new")
        self.assertIn("staged-only entries: 0", out)
        backup = next(self.backups.iterdir())
        rc, out, err = run("rollback", backup, "--installed", self.inst)
        self.assertEqual(rc, 0, out + err)
        self.assertEqual(tree(self.inst), before)

    def test_townmap_plan_checks_names_sizes_sums_and_completeness(self):
        local = self.tmp / "local"
        tiles = local / "tiles"
        tiles.mkdir(parents=True)
        (self.tmp / ".git").mkdir()  # local/ must sit beside a .git
        out = local / "plans" / "t.json"
        for n in (0, 748, 749):
            (tiles / f"Ashbury{n:06d}.png").write_bytes(png(256, 128, ctype=2) + fake(str(n)))
        rc, text, err = run("townmap-plan", "--tiles", tiles, "--town", "Ashbury", "--out", out, "--expect", "3", "--dry-run")
        self.assertEqual(rc, 0, text + err)
        self.assertIn("3 tiles of Ashbury", text)
        self.assertIn("render scale 1", text)
        self.assertFalse(out.exists())
        rc, text, err = run("townmap-plan", "--tiles", tiles, "--town", "Ashbury", "--out", out, "--expect", "3")
        self.assertEqual(rc, 0, text + err)
        plan = json.loads(out.read_text())
        self.assertEqual((plan["kind"], plan["scale"], len(plan["items"])), (hp.TOWN_PLAN_KIND, 4, 3))
        self.assertEqual(plan["items"][1], {"bmp": "townmap/ashbury/ashbury000748.bmp", "image": "../tiles/Ashbury000748.png"})
        # a count other than the town's, a sums file that no longer matches, a wrong size (a 0.5 render for a 4x pack), a bad town name and a stray file are refused
        self.assertEqual(run("townmap-plan", "--tiles", tiles, "--town", "Ashbury", "--out", out, "--expect", "4", "--dry-run")[0], 2)
        self.assertEqual(run("townmap-plan", "--tiles", tiles, "--town", "Ashbury", "--out", out, "--expect", "4", "--partial", "--dry-run")[0], 0)
        (tiles / "SHA256SUMS.txt").write_text("".join(f"{hashlib.sha256(p.read_bytes()).hexdigest()}  ./{p.name}\n" for p in sorted(tiles.glob("*.png"))))
        self.assertEqual(run("townmap-plan", "--tiles", tiles, "--town", "Ashbury", "--out", out, "--dry-run")[0], 0)
        (tiles / "Ashbury000749.png").write_bytes(png(256, 128, ctype=2) + b"changed")
        self.assertEqual(run("townmap-plan", "--tiles", tiles, "--town", "Ashbury", "--out", out, "--dry-run")[0], 2)
        (tiles / "SHA256SUMS.txt").unlink()
        (tiles / "Ashbury000749.png").write_bytes(png(128, 64, ctype=2))
        rc, _, err = run("townmap-plan", "--tiles", tiles, "--town", "Ashbury", "--out", out, "--dry-run")
        self.assertEqual(rc, 2)
        self.assertIn("expected 256x128", err)
        self.assertEqual(run("townmap-plan", "--tiles", tiles, "--town", "Ashbury", "--out", out, "--scale", "2", "--dry-run")[0], 2)
        (tiles / "Ashbury000749.png").write_bytes(png(256, 128, ctype=2))
        self.assertEqual(run("townmap-plan", "--tiles", tiles, "--town", "Ash#bury", "--out", out, "--dry-run")[0], 2)
        self.assertEqual(run("townmap-plan", "--tiles", tiles, "--town", "Tarant", "--out", out, "--dry-run")[0], 2)
        (tiles / "extra.png").write_bytes(png(256, 128, ctype=2))
        self.assertEqual(run("townmap-plan", "--tiles", tiles, "--town", "Ashbury", "--out", out, "--dry-run")[0], 2)


if __name__ == "__main__":
    unittest.main()
