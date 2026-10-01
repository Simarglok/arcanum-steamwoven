from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import unittest
import sys
import argparse
import hashlib
import runpy
import socket
from unittest.mock import patch

from tools.local_dev import config, export_endpoint, launch, manifests, retention


class LocalDevTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.previous = os.environ.get("ARCANUM_LOCAL_HOME")
        os.environ["ARCANUM_LOCAL_HOME"] = str(self.root / "local")

    def tearDown(self) -> None:
        if self.previous is None:
            os.environ.pop("ARCANUM_LOCAL_HOME", None)
        else:
            os.environ["ARCANUM_LOCAL_HOME"] = self.previous
        self.temporary.cleanup()

    def manifest(self, task: str = "ARC-GPAD-003") -> tuple[Path, str]:
        path = self.root / "manifest.json"
        payload = {"schemaVersion": 1, "task": task, "candidate": {"commit": "a" * 40},
                   "app": {"executable": "/bin/true", "engineArgs": ["--rendering-driver", "gl_compatibility"], "ordinaryArgs": ["--ordinary"]}}
        path.write_text(json.dumps(payload), encoding="utf-8")
        return path, manifests.sha256(path)

    def conf(self) -> dict:
        conf = config.default_config()
        conf["artifactRoots"] = {"test": str(self.root / "artifacts")}
        return conf

    def v2_manifest(self, *, root_id: str = "test", task: str = "ARC-GPAD-003", name: str = "a" * 40) -> tuple[Path, str]:
        candidate = self.root / "artifacts" / task / name
        executable = candidate / "GamepadLab.app" / "Contents" / "MacOS" / "GamepadLab"
        executable.parent.mkdir(parents=True); executable.write_bytes(b"fixture")
        evidence = candidate / "evidence" / "check.log"
        evidence.parent.mkdir(parents=True, exist_ok=True); evidence.write_bytes(b"PASS\n")
        preflight = {"schema": "arcanum-export-preflight-v1", "tool": "gamepad",
                     "candidateId": f"{root_id}:{task}:{name}", "candidateCommit": name,
                     "outputBytes": 7, "temporaryBytes": 14, "temporaryBasis": "fallback-two-times-output",
                     "reserveBytes": 2 * 1024**3, "requiredBytes": 7 + 14 + 2 * 1024**3,
                     "observedFreeBytes": 3 * 1024**3, "outputBasis": "explicit-estimate"}
        preflight_path = candidate / "evidence" / "export-preflight.json"
        preflight_path.write_text(json.dumps(preflight), encoding="utf-8")
        observation = {"schema": "arcanum-export-observation-v1", "tool": "gamepad",
                       "candidateId": f"{root_id}:{task}:{name}", "candidateCommit": name,
                       "successfulValidation": True, "temporaryMeasurementStatus": "unavailable",
                       "observedTemporaryBytes": None,
                       "samples": [{"phase": "baseline", "freeBytes": {"export": 3 * 1024**3, "temporary": 3 * 1024**3}},
                                   {"phase": "final", "freeBytes": {"export": 3 * 1024**3, "temporary": 3 * 1024**3}}],
                       "freeLowWaterBytes": {"export": 3 * 1024**3, "temporary": 3 * 1024**3}}
        observation_path = candidate / "evidence/export-observation.json"
        observation_path.write_text(json.dumps(observation), encoding="utf-8")
        path = candidate / "manifest.json"
        payload = {"schema": "arcanum-build-manifest-v2", "project": "arcanum-steamwoven", "logicalTool": "gamepad",
                   "rootId": root_id, "task": task, "candidate": {"commit": name, "tree": "b" * 40},
                   "app": {"relativePath": "GamepadLab.app", "executableRelativePath": "GamepadLab.app/Contents/MacOS/GamepadLab"},
                   "payloadInventory": [{"relativePath": "GamepadLab.app/Contents/MacOS/GamepadLab", "bytes": 7, "sha256": manifests.sha256(executable)}],
                   "evidence": [{"relativePath": "evidence/check.log", "bytes": 5, "sha256": manifests.sha256(evidence)},
                                {"relativePath": "evidence/export-preflight.json", "bytes": preflight_path.stat().st_size, "sha256": manifests.sha256(preflight_path)},
                                {"relativePath": "evidence/export-observation.json", "bytes": observation_path.stat().st_size, "sha256": manifests.sha256(observation_path)}],
                   "exportBudget": {"outputBytes": 7, "temporaryLowWaterBytes": 0, "preflight": preflight, "observation": observation}, "acceptance": "not-accepted"}
        path.write_text(json.dumps(payload), encoding="utf-8")
        return path, manifests.sha256(path)

    def authority_snapshot(self) -> dict:
        """Independent lstat/byte oracle: never follow links or read special nodes."""
        result = {}
        def visit(path: Path) -> None:
            node = path.lstat()
            record: list[int | str] = [stat.S_IFMT(node.st_mode), stat.S_IMODE(node.st_mode)]
            if stat.S_ISLNK(node.st_mode):
                record.append(os.readlink(path))
            elif stat.S_ISREG(node.st_mode):
                try:
                    record.append(hashlib.sha256(path.read_bytes()).hexdigest())
                except PermissionError:
                    record.append("unreadable")
            result[path.relative_to(self.root).as_posix()] = record
            if stat.S_ISDIR(node.st_mode):
                try:
                    children = sorted(path.iterdir())
                except PermissionError:
                    return
                for child in children:
                    visit(child)
        visit(self.root)
        return result

    def authority_fixture(self, name: str = "current.json", task: str = "ARC-GPAD-003", tool: str = "gamepad") -> Path:
        manifest = self.root / "artifacts" / task / ("a" * 40) / "manifest.json"
        if not manifest.exists():
            manifest, _ = self.v2_manifest(task=task)
        payload = json.loads(manifest.read_bytes())
        payload["logicalTool"] = tool
        payload["exportBudget"]["preflight"]["tool"] = tool
        payload["exportBudget"]["observation"]["tool"] = tool
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        pointer = manifest.parent.parent / name
        pointer.write_text(json.dumps({"schema": "arcanum-build-current-v2",
            "manifestRelativePath": "a" * 40 + "/manifest.json",
            "manifestSha256": hashlib.sha256(manifest.read_bytes()).hexdigest()}), encoding="utf-8")
        conf = self.conf()
        conf["launchSelection"] = {tool: f"test:{task}:" + "a" * 40}
        config.save_config(conf)
        config.save_registry(config.default_registry())
        return pointer

    def assert_authority_rejection(self, tool: str = "gamepad") -> None:
        before = self.authority_snapshot()
        with self.assertRaises(ValueError):
            manifests.resolve_current(self.root / "artifacts", tool, root_id="test")
        cli = Path(__file__).resolve().parents[1] / "arcanum-local"
        result = subprocess.run([sys.executable, "-B", str(cli), "launch", tool, "--dry-run"],
                                capture_output=True, text=True, timeout=5)
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertNotIn(str(self.root), result.stderr)
        # Non-dry-run and duplicate opt-in must never reach launch/lock/Popen.
        module = runpy.run_path(str(cli))
        for duplicate in (False, True):
            args = module["parser"]().parse_args(["launch", tool] + (["--allow-duplicate"] if duplicate else []))
            with patch.object(launch, "start") as start, patch.object(config, "open_lock_file") as lock, patch.object(launch.subprocess, "Popen") as child:
                with self.assertRaises(ValueError):
                    module["command_launch"](args)
                start.assert_not_called(); lock.assert_not_called(); child.assert_not_called()
        self.assertEqual(self.authority_snapshot(), before)

    def test_authority_dangling_sibling_four_cli_variants(self) -> None:
        for good, bad in (("current.json", "current-v2.json"), ("current-v2.json", "current.json")):
            for task in ("ARC-GPAD-003", "ARC-GPAD-002"):
                with self.subTest(good=good, task=task):
                    pointer = self.authority_fixture(good)
                    unsafe = self.root / "artifacts" / task / bad
                    unsafe.parent.mkdir(exist_ok=True)
                    unsafe.symlink_to(self.root / "missing-sentinel")
                    self.assert_authority_rejection()
                    snapshot = retention.protection_snapshot(config.load_config(), config.load_registry())
                    self.assertIn(["test", "gamepad"], snapshot["blockedDomains"])
                    self.assertEqual(snapshot["pointers"][f"test:{task}:{bad}"], "unsafe-pointer")
                    self.assertEqual(retention.create_plan(config.load_config(), config.load_registry(),
                        retention.inventory(config.load_config(), config.load_registry(), show_paths=True))["payloads"], [])
                    unsafe.unlink(); pointer.unlink()

    def test_authority_all_literal_tasks_and_names_are_inspected(self) -> None:
        # Literal contract, deliberately not generated from SUPPORTED_TOOLS.
        groups = {"art": ("ARC-RENDER-001", "ARC-RENDER-002"),
                  "terrain": ("ARC-RENDER-003", "ARC-RENDER-004", "ARC-RENDER-005", "ARC-DEV-001", "ARC-WORLD-001"),
                  "gamepad": ("ARC-GPAD-001", "ARC-GPAD-002", "ARC-GPAD-003"), "display": ("ARC-UI-001",)}
        for tool, tasks in groups.items():
            for task in tasks:
                for good, bad in (("current.json", "current-v2.json"), ("current-v2.json", "current.json")):
                    with self.subTest(tool=tool, task=task, good=good):
                        pointer = self.authority_fixture(good, task, tool)
                        found, payload, _ = manifests.resolve_current(self.root / "artifacts", tool, root_id="test")
                        self.assertEqual(found, pointer.parent / ("a" * 40) / "manifest.json")
                        self.assertEqual(payload["task"], task)
                        unsafe = pointer.parent / bad
                        unsafe.symlink_to(self.root / "missing")
                        self.assert_authority_rejection(tool)
                        unsafe.unlink(); pointer.unlink()

    def test_authority_leaf_and_parent_types_cli_and_retention(self) -> None:
        pointer = self.authority_fixture()
        unsafe = pointer.parent / "current-v2.json"
        sentinel = self.root / "sentinel"; sentinel.write_bytes(b"outside"); sentinel.chmod(0o644)
        for kind in ("existing-link", "loop", "directory", "fifo", "socket", "unreadable"):
            with self.subTest(kind=kind):
                server = None
                if kind == "existing-link": unsafe.symlink_to(sentinel)
                elif kind == "loop": unsafe.symlink_to(unsafe.name)
                elif kind == "directory": unsafe.mkdir()
                elif kind == "fifo": os.mkfifo(unsafe)
                elif kind == "socket":
                    server = socket.socket(socket.AF_UNIX)
                    previous = Path.cwd()
                    try:
                        os.chdir(unsafe.parent); server.bind(unsafe.name)
                    finally:
                        os.chdir(previous)
                else:
                    self.assertNotEqual(os.geteuid(), 0, "permission oracle requires nonprivileged uid")
                    unsafe.write_bytes(b"{}"); unsafe.chmod(0)
                try:
                    self.assert_authority_rejection()
                    self.assertIn(["test", "gamepad"], retention.protection_snapshot(config.load_config(), config.load_registry())["blockedDomains"])
                finally:
                    if server: server.close()
                    if kind == "directory": unsafe.rmdir()
                    else: unsafe.unlink()
        parent = self.root / "artifacts/ARC-GPAD-001"
        target = self.root / "target-dir"; target.mkdir()
        for kind in ("link", "dangling", "file", "fifo", "unreadable"):
            with self.subTest(parent=kind):
                if kind == "link": parent.symlink_to(target, target_is_directory=True)
                elif kind == "dangling": parent.symlink_to(self.root / "missing-dir", target_is_directory=True)
                elif kind == "file": parent.write_bytes(b"not-directory")
                elif kind == "fifo": os.mkfifo(parent)
                else: parent.mkdir(); parent.chmod(0)
                try:
                    self.assert_authority_rejection()
                    proof = retention.protection_snapshot(config.load_config(), config.load_registry())
                    self.assertIn(["test", "gamepad"], proof["blockedDomains"])
                    for name in ("current.json", "current-v2.json"):
                        self.assertEqual(proof["pointers"][f"test:ARC-GPAD-001:{name}"], "unsafe-pointer")
                finally:
                    if kind == "unreadable": parent.chmod(0o700); parent.rmdir()
                    else: parent.unlink()
        self.assertEqual(sentinel.read_bytes(), b"outside"); self.assertEqual(stat.S_IMODE(sentinel.stat().st_mode), 0o644)

    def test_authority_absence_valid_and_ambiguous_cli_controls(self) -> None:
        pointer = self.authority_fixture()
        cli = Path(__file__).resolve().parents[1] / "arcanum-local"
        before = self.authority_snapshot()
        control = subprocess.run([sys.executable, "-B", str(cli), "launch", "gamepad", "--dry-run"], capture_output=True, text=True, timeout=5)
        self.assertEqual(control.returncode, 0); self.assertFalse(json.loads(control.stdout)["started"])
        self.assertEqual(before, self.authority_snapshot())
        sibling = pointer.parent / "current-v2.json"
        for encoded in (pointer.read_bytes(), b"{", b'{"schema":"unknown"}'):
            sibling.write_bytes(encoded)
            self.assert_authority_rejection()
            sibling.unlink()
        other = self.root / "artifacts/ARC-GPAD-001"
        other.mkdir(); (other / "current.json").write_bytes(pointer.read_bytes())
        self.assert_authority_rejection()
        (other / "current.json").unlink(); other.rmdir()
        pointer.unlink(); self.assert_authority_rejection()

    def test_optional_state_authority_cli_and_no_stale_fallback(self) -> None:
        self.assertEqual(config.load_config(), config.default_config())
        self.assertFalse(config.home().exists(), "read must not create missing home")
        self.authority_fixture()
        cli = Path(__file__).resolve().parents[1] / "arcanum-local"
        for path, loader in ((config.config_path(), config.load_config), (config.registry_path(), config.load_registry)):
            original = path.read_bytes()
            for kind in ("dangling", "fifo", "unreadable"):
                path.unlink()
                if kind == "dangling": path.symlink_to(self.root / "missing-state")
                elif kind == "fifo": os.mkfifo(path)
                else: path.write_bytes(original); path.chmod(0)
                before = self.authority_snapshot()
                with self.assertRaises(ValueError): loader()
                for command in (["launch", "gamepad", "--dry-run"], ["builds", "inventory"]):
                    result = subprocess.run([sys.executable, "-B", str(cli), *command], capture_output=True, text=True, timeout=5)
                    self.assertEqual(result.returncode, 2); self.assertEqual(result.stdout, "")
                    self.assertNotIn(str(self.root), result.stderr)
                with patch.object(config, "registry_lock"):
                    with self.assertRaises(ValueError):
                        retention.apply_plan({}, self.conf(), config.default_registry(), accepted_id="test")
                with self.assertRaises(ValueError):
                    retention._recheck_protection({}, self.conf(), config.default_registry(), "gamepad", "test:ARC-GPAD-003:" + "a" * 40)
                self.assertEqual(before, self.authority_snapshot())
                path.unlink(); path.write_bytes(original); path.chmod(0o600)
        local = config.home(); moved = self.root / "moved-local"; local.rename(moved)
        for kind in ("link", "dangling", "file"):
            if kind == "link": local.symlink_to(moved, target_is_directory=True)
            elif kind == "dangling": local.symlink_to(self.root / "missing-home", target_is_directory=True)
            else: local.write_bytes(b"not-directory")
            before = self.authority_snapshot()
            with self.assertRaises(ValueError): config.load_config()
            result = subprocess.run([sys.executable, "-B", str(cli), "builds", "inventory"], capture_output=True, text=True, timeout=5)
            self.assertEqual(result.returncode, 2); self.assertEqual(before, self.authority_snapshot())
            local.unlink()
        moved.rename(local)

    def test_optional_reader_races_errors_and_descriptor_release(self) -> None:
        parent = self.root / "reader"; parent.mkdir()
        path = parent / "authority"; path.write_bytes(b"original")
        sentinel = self.root / "sentinel"; sentinel.write_bytes(b"outside"); sentinel.chmod(0o644)
        real_open, real_close = os.open, os.close
        for swap in ("leaf", "parent"):
            held = set()
            def opening(name, flags, *args, **kwargs):
                if name == "authority":
                    if swap == "leaf": path.unlink(); path.symlink_to(sentinel)
                    else:
                        parent.rename(self.root / "old-reader"); parent.symlink_to(self.root / "missing-parent", target_is_directory=True)
                descriptor = real_open(name, flags, *args, **kwargs); held.add(descriptor); return descriptor
            def closing(descriptor): held.remove(descriptor); real_close(descriptor)
            with patch.object(config.os, "open", side_effect=opening), patch.object(config.os, "close", side_effect=closing):
                with self.assertRaises(ValueError): config.optional_authority_bytes(path)
            self.assertEqual(held, set())
            if swap == "leaf": path.unlink(); path.write_bytes(b"original")
            else: parent.unlink(); (self.root / "old-reader").rename(parent)
        real_stat = os.stat
        for error in (PermissionError(), NotADirectoryError(), OSError("inspection failed")):
            def denied(name, *args, **kwargs):
                if name == "authority": raise error
                return real_stat(name, *args, **kwargs)
            with patch.object(config.os, "stat", side_effect=denied):
                with self.assertRaises(ValueError): config.optional_authority_bytes(path)
        self.assertEqual(config.optional_authority_bytes(path), b"original")
        self.assertIsNone(config.optional_authority_bytes(parent / "missing" / "authority"))
        self.assertEqual(sentinel.read_bytes(), b"outside"); self.assertEqual(stat.S_IMODE(sentinel.stat().st_mode), 0o644)

    def test_unsafe_authority_drift_blocks_apply_recovery_and_retire(self) -> None:
        manifest, digest = self.v2_manifest()
        identity = "test:ARC-GPAD-003:" + "a" * 40
        conf, registry = self.conf(), config.default_registry()
        registry["builds"][identity] = {"lifecycle": "retired", "tool": "gamepad", "rootId": "test",
            "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}
        config.save_config(conf); config.save_registry(registry)
        with config.registry_lock(): pass
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        self.assertEqual(len(plan["payloads"]), 1)
        for recovery in (False, True):
            if recovery:
                item = plan["payloads"][0]
                config.write_atomic(manifest.parent / "prune-intent-v1.json", {"schema": "prune-intent-v1", "id": plan["id"],
                    "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"],
                    "manifestSha256": item["manifestSha256"], "reason": item["reason"]})
                shutil.rmtree(manifest.parent / item["payloadRelativePath"])
            for kind in ("dangling-leaf", "fifo-leaf", "file-parent", "dangling-parent"):
                parent = self.root / "artifacts/ARC-GPAD-001"
                if kind.endswith("leaf"):
                    parent.mkdir(); unsafe = parent / "current-v2.json"
                    if kind == "dangling-leaf": unsafe.symlink_to(self.root / "missing")
                    else: os.mkfifo(unsafe)
                else:
                    unsafe = parent
                    if kind == "file-parent": parent.write_bytes(b"not-directory")
                    else: parent.symlink_to(self.root / "missing-dir", target_is_directory=True)
                before = self.authority_snapshot(); prior_registry = json.dumps(registry, sort_keys=True)
                with self.assertRaises(ValueError): retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
                with self.assertRaises(ValueError): retention.retire(registry, identity, conf=conf)
                self.assertEqual(json.dumps(registry, sort_keys=True), prior_registry)
                self.assertEqual(self.authority_snapshot(), before)
                self.assertFalse((manifest.parent / "payload-pruned-v1.json").exists())
                if not recovery: self.assertFalse((manifest.parent / "prune-intent-v1.json").exists())
                unsafe.unlink()
                if kind.endswith("leaf"): parent.rmdir()

    def prune_authority_fixture(self, *, recovery: bool = False) -> tuple[Path, dict]:
        count = getattr(self, "prune_fixture_count", 0) + 1
        self.prune_fixture_count = count
        manifest, digest = self.v2_manifest(name=f"{count:040x}")
        candidate = manifest.parent
        identity = f"test:ARC-GPAD-003:{candidate.name}"
        registry = config.default_registry()
        registry["builds"][identity] = {"lifecycle": "retired", "tool": "gamepad", "rootId": "test",
            "task": "ARC-GPAD-003", "commit": candidate.name, "manifestSha256": digest}
        config.save_config(self.conf()); config.save_registry(registry)
        # Precreate ordinary leases so exact no-mutation oracles include them.
        for path in (config.home() / "build-registry-v1.lock", config.home() / "locks/gamepad.lock", candidate / ".export-lease.lock"):
            lease = retention._try_exclusive(path); self.assertIsNotNone(lease); retention._release(lease)
        conf, registry = config.load_config(), config.load_registry()
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        self.assertEqual(len(plan["payloads"]), 1)
        item = plan["payloads"][0]
        if recovery:
            config.write_atomic(candidate / "prune-intent-v1.json", {"schema": "prune-intent-v1", "id": plan["id"],
                "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"],
                "manifestSha256": item["manifestSha256"], "reason": item["reason"]})
            shutil.rmtree(candidate / "GamepadLab.app")
        return candidate, plan

    def prune_cli(self, plan: dict) -> subprocess.CompletedProcess:
        path = self.root / "plan.json"
        path.write_text(json.dumps(plan), encoding="utf-8")
        cli = Path(__file__).resolve().parents[1] / "arcanum-local"
        return subprocess.run([sys.executable, "-B", str(cli), "builds", "apply", "--plan", str(path),
            "--accept-plan=" + plan["id"]], capture_output=True, text=True, timeout=3)

    def unsafe_node(self, path: Path, kind: str) -> socket.socket | None:
        if kind == "dangling": path.symlink_to(self.root / "missing-sentinel")
        elif kind == "link": path.symlink_to(self.root / "sentinel")
        elif kind == "loop": path.symlink_to(path.name)
        elif kind == "directory": path.mkdir()
        elif kind == "fifo": os.mkfifo(path)
        elif kind == "socket":
            server = socket.socket(socket.AF_UNIX)
            previous = Path.cwd()
            try:
                os.chdir(path.parent); server.bind(path.name)
            finally:
                os.chdir(previous)
            return server
        elif kind == "unreadable":
            self.assertNotEqual(os.geteuid(), 0)
            path.write_bytes(b"{}"); path.chmod(0)
        elif kind == "invalid-json": path.write_bytes(b"{")
        elif kind == "invalid-unicode": path.write_bytes(b"\xff")
        elif kind == "not-object": path.write_bytes(b"[]")
        else: self.fail("unknown synthetic node kind")
        return None

    def assert_prune_leases_released(self, candidate: Path) -> None:
        for path in (config.home() / "build-registry-v1.lock", config.home() / "locks/gamepad.lock", candidate / ".export-lease.lock"):
            descriptor = retention._try_exclusive(path)
            self.assertIsNotNone(descriptor); retention._release(descriptor)

    def test_prune_record_unsafe_types_actual_cli_present_and_recovery(self) -> None:
        sentinel = self.root / "sentinel"; sentinel.write_bytes(b"outside"); sentinel.chmod(0o644)
        for recovery in (False, True):
            for name in ("payload-pruned-v1.json", "prune-intent-v1.json"):
                for kind in ("dangling", "link", "loop", "directory", "fifo", "socket", "unreadable",
                             "invalid-json", "invalid-unicode", "not-object"):
                    with self.subTest(recovery=recovery, name=name, kind=kind):
                        candidate, plan = self.prune_authority_fixture(recovery=recovery)
                        path = candidate / name
                        if path.exists(): path.unlink()
                        server = self.unsafe_node(path, kind)
                        (self.root / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
                        before = self.authority_snapshot()
                        try:
                            result = self.prune_cli(plan)
                            self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
                            self.assertEqual(result.stdout, ""); self.assertNotIn(str(self.root), result.stderr)
                            self.assertEqual(self.authority_snapshot(), before)
                            self.assert_prune_leases_released(candidate)
                            self.assertEqual(self.authority_snapshot(), before)
                        finally:
                            if server: server.close()
                            if kind == "directory": path.rmdir()
                            else: path.unlink()

    def test_prune_record_absence_recovery_and_idempotence_actual_cli(self) -> None:
        for recovery in (False, True):
            with self.subTest(recovery=recovery):
                # URL-safe IDs may start with '-'. Bind an option value with '='
                # instead of letting argparse mistake the token for an option.
                with patch.object(retention.secrets, "token_urlsafe", return_value="-fixture-plan-token"):
                    candidate, plan = self.prune_authority_fixture(recovery=recovery)
                immutable = {p.relative_to(candidate).as_posix(): p.read_bytes() for p in candidate.rglob("*")
                             if p.is_file() and (p.name == "manifest.json" or "evidence" in p.parts)}
                conf_bytes, registry_bytes = config.config_path().read_bytes(), config.registry_path().read_bytes()
                result = self.prune_cli(plan)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(json.loads(result.stdout)["status"], "applied")
                self.assertFalse((candidate / "GamepadLab.app").exists())
                receipt = candidate / "payload-pruned-v1.json"
                self.assertEqual(stat.S_IMODE(receipt.stat().st_mode), 0o600)
                record = json.loads(receipt.read_bytes())
                self.assertEqual((record["schema"], record["planId"], record["relativePayload"]),
                                 ("payload-pruned-v1", plan["id"], "GamepadLab.app"))
                before = self.authority_snapshot()
                self.assertEqual(self.prune_cli(plan).returncode, 0)
                self.assertEqual(self.authority_snapshot(), before)
                self.assertEqual(config.config_path().read_bytes(), conf_bytes)
                self.assertEqual(config.registry_path().read_bytes(), registry_bytes)
                for name, encoded in immutable.items(): self.assertEqual((candidate / name).read_bytes(), encoded)
                self.assert_prune_leases_released(candidate)

    def test_prune_record_substitution_and_descriptor_release(self) -> None:
        sentinel = self.root / "sentinel"; sentinel.write_bytes(b"outside"); sentinel.chmod(0o644)
        for recovery in (False, True):
            for name in ("payload-pruned-v1.json", "prune-intent-v1.json"):
                for stage in ("open", "lease", "mutation"):
                    with self.subTest(recovery=recovery, name=name, stage=stage):
                        candidate, plan = self.prune_authority_fixture(recovery=recovery)
                        path = candidate / name
                        if stage == "open" and not path.exists(): path.write_bytes(b"{}")
                        real_open, real_close = os.open, os.close
                        real_lease, real_recheck = retention._try_exclusive, retention._recheck_protection
                        held = set(); injected = []
                        def substitute():
                            if not injected:
                                if path.exists(): path.unlink()
                                path.symlink_to(sentinel)
                                injected.append(self.authority_snapshot())
                        def opening(node, flags, *args, **kwargs):
                            if stage == "open" and node == name: substitute()
                            fd = real_open(node, flags, *args, **kwargs); held.add(fd); return fd
                        # ps subprocess pipes are not created through os.open;
                        # still close them without counting them as reader fds.
                        def closing(fd): held.discard(fd); real_close(fd)
                        def leasing(node):
                            fd = real_lease(node)
                            if stage == "lease" and node.name == ".export-lease.lock": substitute()
                            return fd
                        def rechecking(*args):
                            real_recheck(*args)
                            if stage == "mutation": substitute()
                        with patch.object(config.os, "open", side_effect=opening), patch.object(config.os, "close", side_effect=closing), \
                             patch.object(retention, "_try_exclusive", side_effect=leasing), \
                             patch.object(retention, "_recheck_protection", side_effect=rechecking), \
                             patch.object(config, "write_atomic") as write, patch.object(retention.shutil, "rmtree") as remove:
                            with self.assertRaises(ValueError):
                                retention.apply_plan(plan, config.load_config(), config.load_registry(), accepted_id=plan["id"])
                            write.assert_not_called(); remove.assert_not_called()
                        self.assertEqual(len(injected), 1); self.assertEqual(held, set())
                        self.assertEqual(self.authority_snapshot(), injected[0])
                        self.assert_prune_leases_released(candidate)
                        path.unlink()

    def test_prune_record_drift_after_intent_or_removal_is_not_overwritten(self) -> None:
        sentinel = self.root / "sentinel"; sentinel.write_bytes(b"outside"); sentinel.chmod(0o644)
        for stage in ("intent", "removal"):
            with self.subTest(stage=stage):
                candidate, plan = self.prune_authority_fixture()
                intent, receipt = candidate / "prune-intent-v1.json", candidate / "payload-pruned-v1.json"
                real_write, real_remove = config.write_atomic, shutil.rmtree
                injected = []
                def write(path, value):
                    real_write(path, value)
                    if stage == "intent" and path == intent:
                        path.write_bytes(b'{"replaced":true}')
                        injected.append(self.authority_snapshot())
                def remove(path):
                    real_remove(path)
                    if stage == "removal":
                        receipt.symlink_to(sentinel)
                        injected.append(self.authority_snapshot())
                with patch.object(config, "write_atomic", side_effect=write), patch.object(retention.shutil, "rmtree", side_effect=remove):
                    with self.assertRaises(ValueError):
                        retention.apply_plan(plan, config.load_config(), config.load_registry(), accepted_id=plan["id"])
                self.assertEqual(len(injected), 1); self.assertEqual(self.authority_snapshot(), injected[0])
                self.assertEqual((candidate / "GamepadLab.app").is_dir(), stage == "intent")
                self.assert_prune_leases_released(candidate)
                if stage == "removal":
                    self.assertEqual(self.prune_cli(plan).returncode, 2)
                    receipt.unlink()
                    self.assertEqual(self.prune_cli(plan).returncode, 0)

    def test_publication_snapshot_unsafe_types_and_parent_actual_subprocess(self) -> None:
        sentinel = self.root / "sentinel"; sentinel.write_bytes(b"outside"); sentinel.chmod(0o644)
        parent = self.root / "publication"; parent.mkdir()
        path = parent / "current.json"
        probe = self.root / "snapshot-probe.py"
        probe.write_text("from pathlib import Path\nimport sys\nfrom tools.local_dev.export_endpoint import _snapshot\n"
                         "try: value = _snapshot(Path(sys.argv[1]))\n"
                         "except ValueError: sys.exit(2)\n"
                         "print('ABSENT' if value is None else value.decode())\n", encoding="utf-8")
        env = {**os.environ, "PYTHONPATH": str(Path(__file__).resolve().parents[2])}
        def check(expected: int):
            before = self.authority_snapshot()
            result = subprocess.run([sys.executable, "-B", str(probe), str(path)], capture_output=True, text=True, env=env, timeout=3)
            self.assertEqual(result.returncode, expected, result.stderr)
            self.assertEqual(before, self.authority_snapshot())
            return result
        self.assertEqual(check(0).stdout, "ABSENT\n")
        path.write_bytes(b"original"); self.assertEqual(check(0).stdout, "original\n"); path.unlink()
        for kind in ("dangling", "link", "loop", "directory", "fifo", "socket", "unreadable"):
            with self.subTest(kind=kind):
                server = self.unsafe_node(path, kind)
                try: self.assertEqual(check(2).stdout, "")
                finally:
                    if server: server.close()
                    if kind == "directory": path.rmdir()
                    else: path.unlink()
        moved = self.root / "moved-publication"; parent.rename(moved)
        for kind in ("link", "dangling", "fifo", "unreadable"):
            with self.subTest(parent=kind):
                if kind == "link": parent.symlink_to(moved, target_is_directory=True)
                elif kind == "dangling": parent.symlink_to(self.root / "missing-parent", target_is_directory=True)
                elif kind == "fifo": os.mkfifo(parent)
                else: parent.mkdir(); parent.chmod(0)
                try: check(2)
                finally:
                    if kind == "unreadable": parent.chmod(0o700); parent.rmdir()
                    else: parent.unlink()
        moved.rename(parent)

    def test_publication_snapshot_fifo_rejects_real_positive_finalizer_without_publication(self) -> None:
        args = self.synthetic_endpoint()
        pointer = Path(args.manifest).parent.parent / "current.json"
        os.mkfifo(pointer)
        config.save_registry(config.default_registry())
        before_registry = config.registry_path().read_bytes()
        result = subprocess.run([sys.executable, "-B", "-m", "tools.local_dev.export_endpoint", "--tool", "gamepad",
            "--root-id", "test", "--task", "ARC-GPAD-003", "--candidate", args.candidate, "--manifest", args.manifest,
            "--temporary-root", args.temporary_root, "--output-bytes", "7", "--estimate-manifest", args.estimate_manifest,
            "--", *args.command], capture_output=True, text=True, timeout=5)
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertTrue(stat.S_ISFIFO(pointer.lstat().st_mode))
        self.assertEqual(config.registry_path().read_bytes(), before_registry)
        self.assertTrue(Path(args.manifest).is_file(), "real finalize path must execute before snapshot")
        candidate = Path(args.manifest).parent
        self.assertTrue((candidate / "GamepadLab.app").is_dir())
        descriptor = retention._try_exclusive(candidate / ".export-lease.lock")
        self.assertIsNotNone(descriptor); retention._release(descriptor)

    def test_atomic_local_files_are_owner_only(self) -> None:
        value = config.default_config()
        config.save_config(value)
        self.assertEqual(stat.S_IMODE(config.config_path().stat().st_mode), 0o600)
        self.assertEqual(config.load_config()["schema"], config.CONFIG_SCHEMA)

    def test_argv_has_literal_separator_and_spaces_unicode(self) -> None:
        executable, argv = launch.argv_from_manifest({"app": {"executable": "/bin/true", "engineArgs": ["--title", "Привет мир"], "ordinaryArgs": ["--safe-name", "two words"]}})
        self.assertEqual(executable, Path("/bin/true"))
        self.assertEqual(argv, ["--title", "Привет мир", "--", "--safe-name", "two words"])

    def test_forbidden_automation_and_no_implicit_export(self) -> None:
        with self.assertRaisesRegex(ValueError, "ordinary launch"):
            launch.assert_ordinary(["--self-test"])
        self.assertNotIn("export", launch.argv_from_manifest({"app": {"executable": "/bin/true"}})[1])

    def test_terrain_args_use_explicit_inputs_and_actor_defaults(self) -> None:
        inputs = {}
        for role in ("module", "terrainResource", "actorResource", "prototypes"):
            path = self.root / f"{role} input"
            path.mkdir() if role == "prototypes" else path.write_bytes(b"fixture")
            inputs[role] = str(path)
        inputs["sourceId"] = "synthetic-local"
        combined = launch.terrain_arguments(inputs, "combined")
        self.assertEqual(combined[:2], ["--mode", "combined"])
        self.assertIn("--prototypes", combined)
        actor = launch.terrain_arguments(inputs, "actor-diagnostic")
        self.assertIn("--actor-resource", actor)
        self.assertEqual(actor[actor.index("--actor-render-branch") + 1], "normal-uncached")
        self.assertEqual(actor[actor.index("--actor-scale-percent") + 1], "100")
        self.assertNotIn("--self-test", actor)
        _, base = launch.argv_from_manifest({"app": {"executable": "/bin/true", "engineArgs": ["--rendering-driver", "gl_compatibility"], "ordinaryArgs": ["--ignored"]}})
        argv = [*base[:base.index("--") + 1], *actor]
        self.assertEqual(argv[2], "--")
        self.assertEqual(argv[3:5], ["--mode", "actor-diagnostic"])
        self.assertNotIn("--ignored", argv)

    def test_actor_preview_overrides_are_bounded_and_action_frame_aware(self) -> None:
        inputs = {}
        for role in ("module", "terrainResource", "actorResource", "prototypes"):
            path = self.root / role
            path.mkdir() if role == "prototypes" else path.write_bytes(b"fixture")
            inputs[role] = str(path)
        inputs["sourceId"] = "synthetic"
        actor = launch.terrain_arguments(inputs, "actor-diagnostic", {"actor_action": "walk", "actor_facing": 7, "actor_frame": 7})
        self.assertEqual(actor[actor.index("--actor-action") + 1], "walk")
        self.assertEqual(actor[actor.index("--actor-facing") + 1], "7")
        with self.assertRaisesRegex(ValueError, "invalid for the chosen action"):
            launch.terrain_arguments(inputs, "actor-diagnostic", {"actor_action": "walk", "actor_frame": 8})
        with self.assertRaisesRegex(ValueError, "fixed normal-uncached"):
            launch.terrain_arguments(inputs, "actor-diagnostic", {"actor_scale_percent": 101})

    def test_terrain_args_fail_closed_for_missing_role_or_symlink(self) -> None:
        with self.assertRaisesRegex(ValueError, "explicit terrain"):
            launch.terrain_arguments({}, "terrain-only")
        target = self.root / "target"; target.write_bytes(b"fixture")
        linked = self.root / "linked"; linked.symlink_to(target)
        inputs = {"module": str(linked), "terrainResource": str(target), "actorResource": str(target), "prototypes": str(target), "sourceId": "fixture"}
        with self.assertRaisesRegex(ValueError, "unavailable or unsafe"):
            launch.terrain_arguments(inputs, "terrain-only")

    def test_v2_pointer_requires_manifest_hash_and_containment(self) -> None:
        root = self.root / "artifacts"; current = root / "ARC-GPAD-003" / "current-v2.json"
        _, digest = self.manifest(); bound, _ = self.v2_manifest()
        executable = bound.parent / "GamepadLab.app" / "Contents" / "MacOS" / "GamepadLab"
        current.parent.mkdir(parents=True, exist_ok=True)
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": f"{'a' * 40}/manifest.json", "manifestSha256": manifests.sha256(bound)}), encoding="utf-8")
        found, payload, _ = manifests.resolve_current(root, "gamepad")
        self.assertEqual(found, bound.resolve()); self.assertEqual(manifests.manifest_candidate(payload), "a" * 40)
        self.assertEqual(Path(payload["app"]["executable"]), executable.resolve())
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": "../../manifest.json", "manifestSha256": digest}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "escapes"):
            manifests.resolve_current(root, "gamepad")

    def test_v2_rejects_mixed_schema_and_incomplete_immutable_bindings(self) -> None:
        manifest, _ = self.v2_manifest()
        payload = json.loads(manifest.read_text(encoding="utf-8")); payload["schemaVersion"] = 1
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "unsupported"):
            manifests._manifest(manifest, "gamepad")
        payload.pop("schemaVersion"); payload.pop("payloadInventory")
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "immutable"):
            manifests._manifest(manifest, "gamepad")

    def test_v2_rejects_unbound_evidence_budget_and_configured_root(self) -> None:
        manifest, digest = self.v2_manifest()
        payload = json.loads(manifest.read_text(encoding="utf-8"))
        payload["evidence"] = [{"relativePath": "../private.log", "bytes": 1}]
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "evidence"):
            manifests._manifest(manifest, "gamepad")
        payload["evidence"] = [{"relativePath": "evidence/check.log", "bytes": 1, "sha256": "a" * 64}]
        preflight = payload["exportBudget"]["preflight"]
        payload["exportBudget"] = {"outputBytes": 7}
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "budget"):
            manifests._manifest(manifest, "gamepad")
        payload["exportBudget"] = {"outputBytes": 7, "temporaryLowWaterBytes": 14, "preflight": preflight}
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        root = self.root / "artifacts"; current = root / "ARC-GPAD-003" / "current-v2.json"
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": f"{'a' * 40}/manifest.json", "manifestSha256": manifests.sha256(manifest)}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "root identity"):
            manifests.resolve_current(root, "gamepad", root_id="other")

    def test_v2_rejects_empty_or_digestless_evidence(self) -> None:
        manifest, _ = self.v2_manifest()
        payload = json.loads(manifest.read_text(encoding="utf-8")); payload["evidence"] = []
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "immutable"):
            manifests._manifest(manifest, "gamepad")
        payload["evidence"] = [{"relativePath": "evidence/check.log", "bytes": 5}]
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "measured"):
            manifests._manifest(manifest, "gamepad")

    def test_legacy_pointer_schemas_are_tool_specific(self) -> None:
        root = self.root / "artifacts"
        app = root / "ARC-UI-001" / "candidate" / "Display.app" / "Contents" / "MacOS" / "Display"
        app.parent.mkdir(parents=True); app.write_bytes(b"fixture")
        manifest = app.parents[3] / "manifest.json"
        manifest.write_text(json.dumps({"schemaVersion": 1, "task": "ARC-UI-001", "candidate": {"commit": "b" * 40},
                                        "app": {"executableRelativePath": "Display.app/Contents/MacOS/Display"}}), encoding="utf-8")
        pointer = root / "ARC-UI-001" / "current.json"
        pointer.write_text(json.dumps({"schemaVersion": 1, "manifest": str(manifest)}), encoding="utf-8")
        _, payload, _ = manifests.resolve_current(root, "display")
        self.assertEqual(Path(payload["app"]["executable"]), app.resolve())
        pointer.write_text(json.dumps({"schemaVersion": 1, "manifestRelativePath": f"{'a' * 40}/manifest.json"}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "absolute"):
            manifests.resolve_current(root, "display")

    def test_missing_mixed_and_stale_pointer_fail_closed(self) -> None:
        root = self.root / "root"; root.mkdir()
        with self.assertRaisesRegex(ValueError, "exactly one"):
            manifests.resolve_current(root, "gamepad")
        for task in ("ARC-GPAD-001", "ARC-GPAD-003"):
            pointer = root / task / "current.json"; pointer.parent.mkdir(parents=True)
            pointer.write_text(json.dumps({"schemaVersion": 1, "manifest": "/missing"}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "exactly one"):
            manifests.resolve_current(root, "gamepad")

    def test_duplicate_guard_is_exact_and_opt_in(self) -> None:
        with patch("tools.local_dev.launch.exact_executable_in_use", return_value=True):
            with self.assertRaisesRegex(ValueError, "already in use"):
                launch.start(Path("/usr/bin/true"), ["--", "--ordinary"], allow_duplicate=False, lock_root=self.root, tool="gamepad", dry_run=True)
            result = launch.start(Path("/usr/bin/true"), ["--", "--ordinary"], allow_duplicate=True, lock_root=self.root, tool="gamepad", dry_run=True)
        self.assertFalse(result["started"])

    def test_exact_process_path_preserves_bundle_spaces(self) -> None:
        executable = self.root / "Bundle With Spaces.app" / "Contents" / "MacOS" / "Tool"
        executable.parent.mkdir(parents=True); executable.write_bytes(b"fixture")
        result = subprocess.CompletedProcess([], 0, stdout=f"123 {executable}\n124 /unrelated Tool\n", stderr="")
        with patch("tools.local_dev.launch.subprocess.run", return_value=result):
            self.assertTrue(launch.exact_executable_in_use(executable))

    def test_inventory_keeps_active_open_review_and_unknown(self) -> None:
        artifact = self.root / "artifacts" / "ARC-GPAD-003"
        for name in ("accepted", "review", "unknown"):
            (artifact / name).mkdir(parents=True); (artifact / name / "payload").write_bytes(b"x")
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(self.root / "artifacts")}
        registry = config.default_registry(); registry["accepted"] = {"gamepad": "test:ARC-GPAD-003:accepted"}; registry["builds"] = {
            "test:ARC-GPAD-003:accepted": {"lifecycle": "accepted"}, "test:ARC-GPAD-003:review": {"lifecycle": "open-review"}}
        entries = retention.inventory(conf, registry)
        self.assertTrue(all(not entry["eligible"] for entry in entries))
        self.assertTrue(any(entry["candidate"] == "unknown" and not entry["eligible"] for entry in entries))

    def test_plan_binds_generations_and_apply_is_noop_safe(self) -> None:
        conf, registry = config.default_config(), config.default_registry()
        plan = retention.create_plan(conf, registry, [])
        result = retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertEqual(result["status"], "no-op-safe")
        conf["generation"] += 1
        with self.assertRaisesRegex(ValueError, "changed"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])

    def test_retired_registered_app_is_pruned_with_idempotent_receipt(self) -> None:
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest()
        candidate = manifest.parent
        candidate_id = "test:ARC-GPAD-003:" + "a" * 40
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        self.assertEqual(len(plan["payloads"]), 1)
        result = retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertEqual(result["status"], "applied")
        self.assertFalse((candidate / "GamepadLab.app").exists())
        self.assertTrue((candidate / "manifest.json").exists())
        self.assertTrue((candidate / "payload-pruned-v1.json").is_file())
        self.assertEqual(retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])["removed"], [candidate_id])

    def test_apply_rejects_forged_receipt_while_payload_exists(self) -> None:
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest(); candidate = manifest.parent
        candidate_id = "test:ARC-GPAD-003:" + "a" * 40
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True)); item = plan["payloads"][0]
        config.write_atomic(candidate / "payload-pruned-v1.json", {"schema": "payload-pruned-v1", "planId": plan["id"], "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"], "reason": item["reason"], "prunedAt": "test"})
        with self.assertRaisesRegex(ValueError, "payload remains"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertTrue((candidate / item["payloadRelativePath"]).is_dir())

    def test_apply_rejects_malformed_existing_intent_before_deletion(self) -> None:
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest(); candidate = manifest.parent
        candidate_id = "test:ARC-GPAD-003:" + "a" * 40
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True)); item = plan["payloads"][0]
        config.write_atomic(candidate / "prune-intent-v1.json", {"schema": "prune-intent-v1", "id": plan["id"], "relativePayload": item["payloadRelativePath"], "priorIdentity": {"forged": True}, "manifestSha256": "0" * 64, "reason": item["reason"]})
        with self.assertRaisesRegex(ValueError, "different prune intent"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertTrue((candidate / item["payloadRelativePath"]).is_dir())

    def test_apply_finalizes_crash_intent_and_rejects_pointer_drift(self) -> None:
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest()
        candidate = manifest.parent; candidate_id = "test:ARC-GPAD-003:" + "a" * 40
        current = candidate.parent / "current-v2.json"
        current.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": f"{'a' * 40}/manifest.json", "manifestSha256": digest}), encoding="utf-8")
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        current.write_text("{}", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "pointer changed"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertEqual(plan["payloads"], [])
        # Crash recovery is an UNSELECTED fixture, never a current candidate.
        current.unlink()
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        item = plan["payloads"][0]
        config.write_atomic(candidate / "prune-intent-v1.json", {"schema": "prune-intent-v1", "id": plan["id"], "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"], "manifestSha256": item["manifestSha256"], "reason": item["reason"]})
        shutil.rmtree(candidate / item["payloadRelativePath"])
        recovered = retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertEqual(recovered["removed"], [candidate_id])
        self.assertTrue((candidate / "payload-pruned-v1.json").is_file())

    def test_apply_rejects_pointer_created_after_plan_and_tampered_recovery_manifest(self) -> None:
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest(); candidate = manifest.parent
        candidate_id = "test:ARC-GPAD-003:" + "a" * 40
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True)); item = plan["payloads"][0]
        (candidate.parent / "current-v2.json").write_text("{}", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "pointer changed"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        (candidate.parent / "current-v2.json").unlink()
        config.write_atomic(candidate / "prune-intent-v1.json", {"schema": "prune-intent-v1", "id": plan["id"], "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"], "manifestSha256": item["manifestSha256"], "reason": item["reason"]})
        shutil.rmtree(candidate / item["payloadRelativePath"])
        payload = json.loads(manifest.read_text(encoding="utf-8")); payload["candidate"]["tree"] = "c" * 40; manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "manifest changed"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])

    def test_inventory_keeps_intermediate_payload_symlink_and_unsafe_candidate(self) -> None:
        root = self.root / "artifacts"; manifest, digest = self.v2_manifest(); candidate = manifest.parent
        candidate_id = "test:ARC-GPAD-003:" + "a" * 40
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
        registry = config.default_registry(); registry["builds"] = {candidate_id: {"lifecycle": "retired", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "a" * 40, "manifestSha256": digest}}
        moved = candidate / "actual"; (candidate / "GamepadLab.app").rename(moved)
        (candidate / "GamepadLab.app").symlink_to("actual", target_is_directory=True)
        entries = retention.inventory(conf, registry)
        self.assertEqual(entries[0]["reason"], "KEEP/SKIP: candidate is unsafe or unreadable")
        self.assertFalse(entries[0]["eligible"])
        unsafe = root / "ARC-GPAD-003" / "linked"; unsafe.symlink_to(candidate, target_is_directory=True)
        entries = retention.inventory(conf, registry)
        self.assertTrue(any(entry["candidate"] == "linked" and "KEEP/SKIP" in entry["reason"] for entry in entries))

    def test_accept_rotates_rollback_and_protects_it_from_retire(self) -> None:
        old, old_digest = self.v2_manifest(name="b" * 40); new, new_digest = self.v2_manifest(name="c" * 40)
        old_id, new_id = "test:ARC-GPAD-003:" + "b" * 40, "test:ARC-GPAD-003:" + "c" * 40
        registry = config.default_registry(); registry["builds"] = {
            old_id: {"lifecycle": "exported", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "b" * 40, "manifestSha256": old_digest},
            new_id: {"lifecycle": "exported", "tool": "gamepad", "rootId": "test", "task": "ARC-GPAD-003", "commit": "c" * 40, "manifestSha256": new_digest}}
        retention.accept(registry, conf=self.conf(), tool="gamepad", candidate_id=old_id, manifest=old, authority="owner", review="review-1")
        retention.accept(registry, conf=self.conf(), tool="gamepad", candidate_id=new_id, manifest=new, authority="owner", review="review-2")
        self.assertEqual(registry["accepted"]["gamepad"], new_id); self.assertEqual(registry["rollback"]["gamepad"], old_id)
        with self.assertRaisesRegex(ValueError, "rollback"):
            retention.retire(registry, old_id, conf=self.conf())

    def test_preflight_requires_measurement_and_reserves_two_gib(self) -> None:
        manifest, _ = self.v2_manifest()
        destinations = {"export": manifest.parent, "temporary": self.root}
        with self.assertRaisesRegex(ValueError, "trusted"):
            retention.export_preflight({}, "gamepad", None, None, conf=self.conf(), destinations=destinations)
        budget = retention.export_preflight({}, "gamepad", 7, None, conf=self.conf(), destinations=destinations, estimate_manifest=manifest)
        self.assertEqual(budget["reserveBytes"], 2 * 1024**3)
        self.assertEqual(budget["temporaryBytes"], 14)

    def test_preflight_rejects_negative_and_boolean_estimates(self) -> None:
        manifest, _ = self.v2_manifest()
        for output, temporary in ((-1, None), (True, None), (7, -1), (7, True)):
            with self.subTest(output=output, temporary=temporary), self.assertRaises(ValueError):
                retention.export_preflight({}, "gamepad", output, temporary, conf=self.conf(),
                    destinations={"export": manifest.parent, "temporary": self.root}, estimate_manifest=manifest)

    def test_pointer_added_during_inventory_recheck_prevents_intent_and_delete(self) -> None:
        manifest, _ = self.v2_manifest()
        registry, candidate_id = self.retired(manifest)
        conf = self.conf()
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        self.assertEqual(len(plan["payloads"]), 1)
        original = retention._bound_payload_inventory
        def concurrent_pointer(*args, **kwargs):
            result = original(*args, **kwargs)
            (manifest.parent.parent / "current-v2.json").write_text(json.dumps({
                "schema": "arcanum-build-current-v2", "manifestRelativePath": f"{manifest.parent.name}/manifest.json",
                "manifestSha256": manifests.sha256(manifest)}))
            return result
        with patch("tools.local_dev.retention._bound_payload_inventory", side_effect=concurrent_pointer):
            with self.assertRaisesRegex(ValueError, "current pointer"):
                retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertTrue((manifest.parent / "GamepadLab.app").is_dir())
        self.assertFalse((manifest.parent / "prune-intent-v1.json").exists())
        self.assertFalse((manifest.parent / "payload-pruned-v1.json").exists())

    def test_export_endpoint_rejects_preflight_before_child_execution(self) -> None:
        root = self.root / "artifacts"; root.mkdir()
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}; config.save_config(conf)
        commit = "d" * 40
        args = type("Args", (), {"tool": "gamepad", "root_id": "test", "task": "ARC-GPAD-003", "candidate": commit,
                                  "manifest": str(root / "ARC-GPAD-003" / commit / "manifest.json"),
                                  "output_bytes": 7, "temporary_bytes": None, "command": ["/usr/bin/true"]})()
        with patch("tools.local_dev.export_endpoint.retention.export_preflight", side_effect=ValueError("observed free space is below the measured export budget")), patch("tools.local_dev.export_endpoint.subprocess.run") as command:
            with self.assertRaisesRegex(ValueError, "free space"):
                export_endpoint.run_export(args)
        command.assert_not_called()
        self.assertFalse((root / "ARC-GPAD-003" / "current.json").exists())
        self.assertFalse(config.load_registry()["builds"])

    def test_registration_rejects_commit_candidate_id_and_manifest_parent_mismatch(self) -> None:
        manifest, _ = self.v2_manifest()
        payload = json.loads(manifest.read_text(encoding="utf-8"))
        payload["checks"] = [{"name": name, "outcome": "PASS"} for name in retention.REQUIRED_SUCCESS_GATES["gamepad"]]
        payload["candidate"]["commit"] = "b" * 40
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "identity|exact v2 root/task/commit"):
            retention.register_export(config.default_registry(), conf=self.conf(), tool="gamepad", candidate_id="test:ARC-GPAD-003:" + "a" * 40, manifest=manifest)

    def test_export_registration_requires_passing_existing_manifest(self) -> None:
        manifest, _ = self.v2_manifest()
        payload = json.loads(manifest.read_text(encoding="utf-8")); payload["checks"] = [{"name": name, "outcome": "PASS"} for name in retention.REQUIRED_SUCCESS_GATES["gamepad"]]
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        registry = config.default_registry()
        candidate_id = "test:ARC-GPAD-003:" + "a" * 40
        retention.register_export(registry, conf=self.conf(), tool="gamepad", candidate_id=candidate_id, manifest=manifest)
        self.assertEqual(registry["builds"][candidate_id]["lifecycle"], "exported")
        payload["checks"] = [{"name": "codesign-deep-strict", "outcome": "FAIL"}]; manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "conflicting immutable"):
            retention.register_export(registry, conf=self.conf(), tool="gamepad", candidate_id=candidate_id, manifest=manifest)

    def test_export_registration_binds_evidence_digests_and_budget_to_disk(self) -> None:
        manifest, _ = self.v2_manifest(); candidate_id = "test:ARC-GPAD-003:" + "a" * 40
        payload = json.loads(manifest.read_text(encoding="utf-8")); payload["checks"] = [{"name": name, "outcome": "PASS"} for name in retention.REQUIRED_SUCCESS_GATES["gamepad"]]
        payload["exportBudget"]["outputBytes"] = 8
        manifest.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "budget"):
            retention.register_export(config.default_registry(), conf=self.conf(), tool="gamepad", candidate_id=candidate_id, manifest=manifest)
        payload["exportBudget"]["outputBytes"] = 7; manifest.write_text(json.dumps(payload), encoding="utf-8")
        (manifest.parent / "evidence/check.log").write_bytes(b"FAIL\n")
        with self.assertRaisesRegex(ValueError, "evidence"):
            retention.register_export(config.default_registry(), conf=self.conf(), tool="gamepad", candidate_id=candidate_id, manifest=manifest)

    def synthetic_endpoint(self) -> argparse.Namespace:
        """Real two-phase subprocess + production writer; synthetic gate INPUTS only."""
        estimate, _ = self.v2_manifest()
        main = self.root / "main"; (main / "artifacts").mkdir(parents=True)
        (self.root / "artifacts").rename(main / "artifacts/builds")
        estimate = main / "artifacts/builds/ARC-GPAD-003" / ("a" * 40) / "manifest.json"
        repo = self.root / "repo"; repo.mkdir()
        def git(*args: str) -> str:
            return subprocess.run(["git", "-C", str(repo), *args], check=True, text=True,
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout.strip()
        git("init"); git("-c", "user.name=Synthetic", "-c", "user.email=synthetic@example.invalid",
                          "commit", "--allow-empty", "-m", "synthetic fixture")
        git("update-ref", "refs/remotes/origin/main", "HEAD")
        head = git("rev-parse", "HEAD")
        candidate = main / "artifacts/builds/ARC-GPAD-003" / head
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(main / "artifacts/builds")}
        config.save_config(conf)
        source = Path(__file__).resolve().parents[2]
        writer = source / "experiments/gamepad-lab/tools/write_manifest.py"
        code = """
import os, sys, json, subprocess
from pathlib import Path
candidate, repo, main, writer, source, head = sys.argv[1:]
candidate = Path(candidate); evidence = candidate / 'evidence'
app = candidate / 'GamepadLab.app/Contents/MacOS/GamepadLab'
if os.environ['ARCANUM_EXPORT_PHASE'] == 'validate':
    assert not (evidence / 'export-observation.json').exists()
    app.parent.mkdir(parents=True); app.write_bytes(b'fixture')
    names = ('sdk-info.log', 'locked-restore.log', 'locked-tests-restore.log', 'release-build.log',
             'core-harness.log', 'editor-import.log', 'debug-build.log', 'godot-self-test.log',
             'export.log', 'exported-smoke.log', 'persistence-write.log',
             'persistence-verify-nondefault.log', 'persistence-verify-default.log',
             'automated-capture.log', 'plist-lint.log', 'codesign.log')
    for name in names: (evidence / name).write_text('SYNTHETIC GATE INPUT\\n')
    for name in ('exported-smoke.json', 'persistence-write.json',
                 'persistence-verify-nondefault.json', 'persistence-verify-default.json'):
        (evidence / name).write_text(json.dumps({'status':'passed','isolatedTestStorage':True}))
    (evidence / 'automated-capture.png').write_bytes(b'SYNTHETIC PNG INPUT')
    (evidence / 'ordinary-launch-not-verified.json').write_text('{}')
else:
    assert (evidence / 'export-observation.json').is_file()
    frozen = (evidence / 'export-observation.json').read_bytes()
    env = {**os.environ, 'PYTHONPATH':source, 'ARCANUM_LOCAL_ROOT_ID':'test'}
    subprocess.run([sys.executable, '-B', writer, repo, main, str(candidate), head, str(app)], check=True, env=env)
    assert frozen == (evidence / 'export-observation.json').read_bytes()
"""
        return argparse.Namespace(tool="gamepad", root_id="test", task="ARC-GPAD-003", candidate=head,
             manifest=str(candidate / "manifest.json"), temporary_root=str(self.root),
             output_bytes=7, temporary_bytes=None, estimate_manifest=str(estimate), write_root=[str(repo)],
             command=[sys.executable, "-B", "-c", code, str(candidate), str(repo), str(main), str(writer), str(source), head])

    def test_export_endpoint_registers_only_after_success_and_writes_v2_pointer(self) -> None:
        args = self.synthetic_endpoint()
        self.assertEqual(export_endpoint.run_export(args), 0)
        candidate = Path(args.manifest).parent
        pointer = json.loads((candidate.parent / "current.json").read_text())
        payload = json.loads(Path(args.manifest).read_text())
        self.assertEqual(pointer["manifestSha256"], manifests.sha256(Path(args.manifest)))
        self.assertEqual(pointer["schema"], "arcanum-build-current-v2")
        identity = f"test:ARC-GPAD-003:{args.candidate}"
        self.assertEqual(config.load_registry()["builds"][identity]["lifecycle"], "exported")
        self.assertEqual(payload["exportBudget"]["temporaryLowWaterBytes"], 0)
        self.assertIsNone(payload["exportBudget"]["observation"]["observedTemporaryBytes"])
        retention._bound_evidence(payload, candidate)
        self.assertEqual(retention._bound_payload_inventory(payload, candidate, candidate / "GamepadLab.app"), 7)
        self.assertFalse(config.load_registry().get("measurements"))
        files = {str(path.relative_to(candidate)): path.read_bytes() for path in candidate.rglob("*") if path.is_file()}
        registry_bytes = config.registry_path().read_bytes()
        for command in (["/usr/bin/true"], ["/usr/bin/false"]):
            args.command = command
            with patch("tools.local_dev.export_endpoint.subprocess.run") as child:
                with self.assertRaisesRegex(ValueError, "immutable finalized"):
                    export_endpoint.run_export(args)
                child.assert_not_called()
            self.assertEqual(files, {str(path.relative_to(candidate)): path.read_bytes() for path in candidate.rglob("*") if path.is_file()})
            self.assertEqual(config.registry_path().read_bytes(), registry_bytes)

    def test_export_endpoint_pointer_failure_restores_publication(self) -> None:
        self.publication_failure("tools.local_dev.export_endpoint._write_current")

    def test_export_endpoint_registry_failure_restores_publication(self) -> None:
        self.publication_failure("tools.local_dev.export_endpoint.config.save_registry")

    def publication_failure(self, patch_target: str) -> None:
        args = self.synthetic_endpoint()
        pointer = Path(args.manifest).parent.parent / "current.json"
        pointer.write_text('{"prior":"pointer"}\n', encoding="utf-8")
        registry = config.default_registry()
        registry["accepted"] = {"gamepad": "accepted"}; registry["rollback"] = {"gamepad": "rollback"}
        config.save_registry(registry)
        expected_pointer = pointer.read_bytes(); expected_registry = config.registry_path().read_bytes()
        with patch(patch_target, side_effect=OSError("forced publication failure")):
            with self.assertRaisesRegex(OSError, "forced publication failure"):
                export_endpoint.run_export(args)
        self.assertEqual(pointer.read_bytes(), expected_pointer)
        self.assertEqual(config.registry_path().read_bytes(), expected_registry)
        restored = config.load_registry()
        self.assertEqual(restored["accepted"], {"gamepad": "accepted"})
        self.assertEqual(restored["rollback"], {"gamepad": "rollback"})

    def retired(self, manifest: Path) -> tuple[dict, str]:
        payload = json.loads(manifest.read_text())
        identity = f"{payload['rootId']}:{payload['task']}:{manifest.parent.name}"
        registry = config.default_registry()
        registry["builds"][identity] = {"lifecycle": "retired", "tool": "gamepad", "rootId": payload["rootId"],
              "task": payload["task"], "commit": manifest.parent.name, "manifestSha256": manifests.sha256(manifest)}
        return registry, identity

    def test_all_current_names_and_stale_explicit_selection_protect_payloads(self) -> None:
        manifest, digest = self.v2_manifest(); registry, identity = self.retired(manifest)
        conf = self.conf()
        for name in ("current.json", "current-v2.json"):
            pointer = manifest.parent.parent / name
            pointer.write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": f"{'a' * 40}/manifest.json", "manifestSha256": digest}))
            self.assertEqual(retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))["payloads"], [])
        with self.assertRaisesRegex(ValueError, "exactly one"):
            manifests.resolve_current(manifest.parents[2], "gamepad")
        for pointer in manifest.parent.parent.glob("current*.json"): pointer.unlink()
        conf["launchSelection"] = {"gamepad": identity}
        self.assertEqual(retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))["payloads"], [])
        before = config.digest(registry)
        with self.assertRaisesRegex(ValueError, "selected"):
            retention.retire(registry, identity, conf=conf)
        self.assertEqual(config.digest(registry), before)

    def test_legacy_pointer_protection_all_admitted_schemas(self) -> None:
        root = self.root / "artifacts"; root.mkdir()
        conf = self.conf(); registry = config.default_registry()
        fixtures = (("art", "ARC-RENDER-001", None), ("display", "ARC-UI-001", None),
                    ("gamepad", "ARC-GPAD-001", None), ("terrain", "ARC-RENDER-005", "ARC-RENDER-005-v1"))
        for tool, task, schema in fixtures:
            candidate = root / task / ("a" * 40); candidate.mkdir(parents=True)
            app = candidate / "Tool.app/Contents/MacOS/Tool"; app.parent.mkdir(parents=True); app.write_bytes(b"x")
            manifest = candidate / "manifest.json"
            payload = {"task": task, "candidate": {"commit": "a" * 40}, "app": {"executableRelativePath": "Tool.app/Contents/MacOS/Tool"}}
            payload.update({"schema": schema} if schema else {"schemaVersion": 1})
            manifest.write_text(json.dumps(payload))
            pointer: dict = {"schema": schema} if schema else {"schemaVersion": 1}
            if tool in ("art", "display"): pointer["manifest"] = str(manifest)
            else: pointer.update({"manifestRelativePath": f"{'a' * 40}/manifest.json", "manifestSha256": manifests.sha256(manifest)})
            (candidate.parent / "current.json").write_text(json.dumps(pointer))
        snapshot = retention.protection_snapshot(conf, registry)
        self.assertEqual(snapshot["blockedDomains"], [])
        self.assertEqual(set(snapshot["protected"]), {f"test:{task}:{'a' * 40}" for _, task, _ in fixtures})

    def test_multiple_tasks_roots_currents_protect_union_and_unknown_authority_blocks(self) -> None:
        first, _ = self.v2_manifest(); second, _ = self.v2_manifest(task="ARC-GPAD-002", name="b" * 40)
        conf = self.conf(); registry = config.default_registry()
        for manifest in (first, second):
            record, identity = self.retired(manifest); registry["builds"].update(record["builds"])
            (manifest.parent.parent / "current.json").write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": f"{manifest.parent.name}/manifest.json", "manifestSha256": manifests.sha256(manifest)}))
        other = self.root / "other"; shutil.copytree(first.parents[2], other)
        conf["artifactRoots"]["other"] = str(other)
        # Root mismatch is ambiguous authority, never an excuse to prune either root.
        self.assertEqual(retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))["payloads"], [])
        self.assertIn(["other", "gamepad"], retention.protection_snapshot(conf, registry)["blockedDomains"])
        for invalid in ("{}", '{"schema":"unsupported"}', '{"schema":"arcanum-build-current-v2","manifestRelativePath":"/missing","manifestSha256":"x"}'):
            (first.parent.parent / "current.json").write_text(invalid)
            self.assertEqual(retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))["payloads"], [])

    def test_missing_proof_forged_plan_and_new_selection_reject(self) -> None:
        manifest, digest = self.v2_manifest(); registry, identity = self.retired(manifest); conf = self.conf()
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        missing = dict(plan); missing.pop("protectionBindings")
        with self.assertRaisesRegex(ValueError, "protection proof"):
            retention.apply_plan(missing, conf, registry, accepted_id=plan["id"])
        conf["launchSelection"] = {"gamepad": identity}
        forged = dict(plan); forged["configSha256"] = config.digest(conf)
        forged["protectionBindings"] = retention.protection_snapshot(conf, registry)
        with self.assertRaisesRegex(ValueError, "selected"):
            retention.apply_plan(forged, conf, registry, accepted_id=plan["id"])
        self.assertTrue((manifest.parent / "GamepadLab.app").exists())

    def test_lifecycle_wrong_configured_identity_never_mutates_registry(self) -> None:
        manifest, _ = self.v2_manifest(); registry, identity = self.retired(manifest); conf = self.conf()
        before = config.digest(registry)
        for wrong in ("missing:ARC-GPAD-003:" + "a" * 40, "test:ARC-UI-001:" + "a" * 40,
                      "test:ARC-GPAD-003:" + "A" * 40, "test:ARC-GPAD-003:a", "test:ARC-GPAD-003:" + "g" * 40,
                      "test:ARC-GPAD-003:" + "b" * 40):
            for action in ("register", "accept", "retire"):
                with self.subTest(wrong=wrong, action=action), self.assertRaises(ValueError):
                    if action == "register": retention.register_export(registry, conf=conf, tool="gamepad", candidate_id=wrong, manifest=manifest)
                    elif action == "accept": retention.accept(registry, conf=conf, tool="gamepad", candidate_id=wrong, manifest=manifest, authority="synthetic", review="synthetic")
                    else: retention.retire(registry, wrong, conf=conf)
                self.assertEqual(config.digest(registry), before)
        registry["builds"][identity]["manifestSha256"] = "0" * 64; before = config.digest(registry)
        with self.assertRaisesRegex(ValueError, "immutable identity"):
            retention.retire(registry, identity, conf=conf)
        self.assertEqual(config.digest(registry), before)

    def test_repeat_registration_preserves_retired_lifecycle(self) -> None:
        manifest, _ = self.v2_manifest(); registry, identity = self.retired(manifest)
        before = config.digest(registry)
        retention.register_export(registry, conf=self.conf(), tool="gamepad", candidate_id=identity, manifest=manifest)
        self.assertEqual(config.digest(registry), before)

    def test_partial_candidate_rejects_before_preflight_child_and_writes(self) -> None:
        root = self.root / "artifacts"; candidate = root / "ARC-GPAD-003" / ("d" * 40)
        candidate.mkdir(parents=True); evidence = candidate / "evidence"; evidence.mkdir(); (evidence / "old.log").write_bytes(b"old")
        config.save_config(self.conf())
        args = argparse.Namespace(tool="gamepad", root_id="test", task="ARC-GPAD-003", candidate="d" * 40,
                                  manifest=str(candidate / "manifest.json"), command=["/usr/bin/true"])
        before = {str(path.relative_to(candidate)): path.read_bytes() for path in candidate.rglob("*") if path.is_file()}
        with patch("tools.local_dev.export_endpoint.subprocess.run") as child, patch("tools.local_dev.retention.export_preflight") as preflight:
            with self.assertRaisesRegex(ValueError, "partial"):
                export_endpoint.run_export(args)
            child.assert_not_called(); preflight.assert_not_called()
        self.assertEqual(before, {str(path.relative_to(candidate)): path.read_bytes() for path in candidate.rglob("*") if path.is_file()})

    def test_destination_budget_checks_distinct_devices_not_config_home(self) -> None:
        manifest, _ = self.v2_manifest(); conf = self.conf()
        temporary = self.root / "temporary"; temporary.mkdir()
        destinations = {"export": manifest.parent, "temporary": temporary}
        actual_proof = retention.destination_proof
        def proof(path):
            ancestor, identity = actual_proof(path)
            return ancestor, {**identity, "device": 2 if path == temporary else 1}
        for low in (manifest.parent, temporary):
            def capacity(path): return argparse.Namespace(free=1 if path == low else 9 * 1024**3)
            with patch("tools.local_dev.retention.destination_proof", side_effect=proof), patch("tools.local_dev.retention.shutil.disk_usage", side_effect=capacity) as probe:
                with self.assertRaisesRegex(ValueError, "destination free space"):
                    retention.export_preflight({}, "gamepad", 7, None, conf=conf, destinations=destinations, estimate_manifest=manifest)
                self.assertNotIn(config.home(), [call.args[0] for call in probe.call_args_list])
        with patch("tools.local_dev.retention.shutil.disk_usage", return_value=argparse.Namespace(free=9 * 1024**3)) as probe:
            budget = retention.export_preflight({}, "gamepad", 7, None, conf=conf, destinations=destinations, estimate_manifest=manifest)
            self.assertEqual(probe.call_count, 1)
            self.assertEqual(budget["requiredBytes"], 2147483669)
        moved = temporary.with_name("old-temporary"); proofs = {role: actual_proof(path) for role, path in destinations.items()}
        temporary.rename(moved); temporary.mkdir()
        with self.assertRaisesRegex(ValueError, "destination changed"):
            retention.verify_destinations(destinations, proofs)

    def test_all_endpoint_mappings_low_destination_reject_before_child(self) -> None:
        manifest, _ = self.v2_manifest(); config.save_config(self.conf())
        for tool, task in (("display", "ARC-UI-001"), ("gamepad", "ARC-GPAD-003"), ("terrain", "ARC-DEV-001")):
            payload = json.loads(manifest.read_text()); payload["logicalTool"] = tool; payload["task"] = task
            for value in (payload["exportBudget"]["preflight"], payload["exportBudget"]["observation"]):
                value["tool"] = tool; value["candidateId"] = f"test:{task}:{'a' * 40}"
            target = manifest.parents[2] / task / ("a" * 40) / "manifest.json"
            if target != manifest: shutil.copytree(manifest.parent, target.parent)
            target.write_text(json.dumps(payload))
            for name in ("preflight", "observation"):
                path = target.parent / f"evidence/export-{name}.json"; path.write_text(json.dumps(payload["exportBudget"][name]))
                entry = next(entry for entry in payload["evidence"] if entry["relativePath"] == f"evidence/export-{name}.json")
                entry.update(bytes=path.stat().st_size, sha256=manifests.sha256(path))
            target.write_text(json.dumps(payload))
            candidate = target.parent.parent / ("d" * 40)
            args = argparse.Namespace(tool=tool, root_id="test", task=task, candidate="d" * 40,
                    manifest=str(candidate / "manifest.json"), temporary_root=str(self.root), output_bytes=7,
                    estimate_manifest=str(target), command=["/usr/bin/true"])
            with patch("tools.local_dev.retention.shutil.disk_usage", return_value=argparse.Namespace(free=1)), patch("tools.local_dev.export_endpoint.subprocess.run") as child:
                with self.assertRaisesRegex(ValueError, "destination free space"):
                    export_endpoint.run_export(args)
                child.assert_not_called()
            self.assertFalse(candidate.exists())

    def test_sampler_transient_temp_output_separation_and_shutdown(self) -> None:
        temporary = self.root / "temporary"; temporary.mkdir()
        output = self.root / "output"; output.mkdir()
        clock = iter((0, 1, 2, 3, 4))
        sampler = export_endpoint.ObservationSampler({"export": output, "temporary": temporary}, temporary,
                     coverage=True, clock=lambda: next(clock), capacity=lambda _: argparse.Namespace(free=10000))
        sampler.sample("baseline")
        transient = temporary / "transient"; transient.write_bytes(b"t" * 8192)
        (output / "not-temporary").write_bytes(b"o" * 32768)
        allocated = transient.stat().st_blocks * 512
        sampler.sample("periodic"); transient.unlink()
        observation = sampler.finish("test:ARC-GPAD-003:" + "a" * 40, "gamepad", True)
        self.assertEqual(observation["observedTemporaryBytes"], allocated)
        self.assertEqual([sample["phase"] for sample in observation["samples"]], ["baseline", "periodic", "final"])
        self.assertEqual(observation["freeLowWaterBytes"], {"export": 10000, "temporary": 10000})
        self.assertEqual(observation["samples"][-1]["isolatedTemporaryAllocatedBytes"], 0)
        sampler = export_endpoint.ObservationSampler({"export": output, "temporary": temporary}, temporary, interval=0.01)
        sampler.start(); unavailable = sampler.finish("test:ARC-GPAD-003:" + "a" * 40, "gamepad", False)
        self.assertFalse(sampler.thread.is_alive()); self.assertIsNone(unavailable["observedTemporaryBytes"])
        self.assertEqual(unavailable["temporaryMeasurementStatus"], "unavailable")
        sampler = export_endpoint.ObservationSampler({"export": output, "temporary": temporary}, temporary, coverage=True)
        sampler.sample("baseline"); zero = sampler.finish("test:ARC-GPAD-003:" + "a" * 40, "gamepad", True)
        self.assertEqual(zero["temporaryMeasurementStatus"], "qualified"); self.assertEqual(zero["observedTemporaryBytes"], 0)

    def test_export_endpoint_new_child_failure_freezes_unavailable_observation(self) -> None:
        args = self.synthetic_endpoint(); args.command = ["/usr/bin/false"]
        self.assertEqual(export_endpoint.run_export(args), 1)
        candidate = Path(args.manifest).parent
        observation = json.loads((candidate / "evidence/export-observation.json").read_text())
        self.assertEqual(observation["successfulValidation"], False)
        self.assertEqual(observation["samples"][-1]["phase"], "final")
        self.assertIsNone(observation["observedTemporaryBytes"])
        self.assertFalse(config.load_registry()["builds"])
        self.assertFalse((candidate.parent / "current.json").exists())

    def test_export_endpoint_new_finalizer_failure_never_registers(self) -> None:
        args = self.synthetic_endpoint()
        args.command[3] += "\nif os.environ['ARCANUM_EXPORT_PHASE'] == 'finalize': sys.exit(9)\n"
        self.assertEqual(export_endpoint.run_export(args), 9)
        self.assertFalse(config.load_registry()["builds"])
        self.assertFalse(config.load_registry().get("measurements"))

    def test_sampler_failure_and_sample_cap_never_qualify_measurement(self) -> None:
        paths = {"export": self.root, "temporary": self.root}
        for capacity, limit in ((lambda _: (_ for _ in ()).throw(OSError("forced sample error")), 4096),
                                (lambda _: argparse.Namespace(free=10000), 1)):
            sampler = export_endpoint.ObservationSampler(paths, self.root, coverage=True, capacity=capacity, limit=limit)
            sampler.start(); sampler.sample("periodic")
            observation = sampler.finish("test:ARC-GPAD-003:" + "a" * 40, "gamepad", True)
            self.assertFalse(sampler.thread.is_alive())
            self.assertEqual(observation["temporaryMeasurementStatus"], "unavailable")
            self.assertIsNone(observation["observedTemporaryBytes"])

    def test_current_output_margin_and_qualified_history_are_estimates_not_observations(self) -> None:
        manifest, _ = self.v2_manifest(); payload = json.loads(manifest.read_text())
        observation = payload["exportBudget"]["observation"]
        observation.update(temporaryMeasurementStatus="qualified", observedTemporaryBytes=8192,
                           temporaryAttribution="all temporary writes isolated")
        path = manifest.parent / "evidence/export-observation.json"; path.write_text(json.dumps(observation))
        entry = next(entry for entry in payload["evidence"] if entry["relativePath"] == "evidence/export-observation.json")
        entry.update(bytes=path.stat().st_size, sha256=manifests.sha256(path))
        payload["exportBudget"]["temporaryLowWaterBytes"] = 8192
        payload["checks"] = [{"name": name, "outcome": "PASS"} for name in ("core-harness", "godot-self-test", "export", "exported-smoke", "codesign-deep-strict")]
        manifest.write_text(json.dumps(payload)); registry = config.default_registry(); identity = "test:ARC-GPAD-003:" + "a" * 40
        retention.register_export(registry, conf=self.conf(), tool="gamepad", candidate_id=identity, manifest=manifest)
        (manifest.parent.parent / "current.json").write_text(json.dumps({"schema": "arcanum-build-current-v2", "manifestRelativePath": f"{'a' * 40}/manifest.json", "manifestSha256": manifests.sha256(manifest)}))
        budget = retention.export_preflight(registry, "gamepad", None, None, conf=self.conf(), destinations={"export": manifest.parent, "temporary": self.root})
        self.assertEqual(budget["outputBytes"], 8)
        self.assertEqual(budget["temporaryBytes"], 8192)
        self.assertEqual(budget["temporaryBasis"], "qualified-history-estimate")
        self.assertNotIn("observation", budget)
        registry["measurements"]["gamepad"]["observationSha256"] = "0" * 64
        fallback = retention.export_preflight(registry, "gamepad", None, None, conf=self.conf(), destinations={"export": manifest.parent, "temporary": self.root})
        self.assertEqual(fallback["temporaryBytes"], 16)
        self.assertEqual(fallback["temporaryBasis"], "fallback-two-times-output")

    def test_apply_rejects_export_lease_and_in_use_executable(self) -> None:
        manifest, _ = self.v2_manifest(); registry, identity = self.retired(manifest); conf = self.conf()
        # Lock file is part of the fixture before planning; it is not a drift shortcut.
        lease = retention._try_exclusive(manifest.parent / ".export-lease.lock")
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        with self.assertRaisesRegex(ValueError, "leased"):
            retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        retention._release(lease)
        with patch("tools.local_dev.launch.exact_executable_in_use", return_value=True):
            with self.assertRaisesRegex(ValueError, "in use"):
                retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
        self.assertTrue((manifest.parent / "GamepadLab.app").is_dir())

    def test_existing_candidate_rejects_low_disk_and_conflicting_registry_before_child(self) -> None:
        manifest, _ = self.v2_manifest(); config.save_config(self.conf())
        registry, identity = self.retired(manifest); registry["builds"][identity]["manifestSha256"] = "0" * 64
        config.save_registry(registry)
        files = {str(path.relative_to(manifest.parent)): path.read_bytes() for path in manifest.parent.rglob("*") if path.is_file()}
        args = argparse.Namespace(tool="gamepad", root_id="test", task="ARC-GPAD-003", candidate="a" * 40,
                                  manifest=str(manifest), command=["/usr/bin/false"])
        with patch("tools.local_dev.retention.shutil.disk_usage", return_value=argparse.Namespace(free=0)) as capacity, patch("tools.local_dev.export_endpoint.subprocess.run") as child:
            with self.assertRaisesRegex(ValueError, "immutable finalized"):
                export_endpoint.run_export(args)
            capacity.assert_not_called(); child.assert_not_called()
        self.assertEqual(files, {str(path.relative_to(manifest.parent)): path.read_bytes() for path in manifest.parent.rglob("*") if path.is_file()})

    def test_new_observation_and_registration_failure_never_publish(self) -> None:
        args = self.synthetic_endpoint()
        expected_registry = config.load_registry()
        with patch("tools.local_dev.export_endpoint.ObservationSampler.finish", side_effect=OSError("forced observation failure")):
            with self.assertRaisesRegex(OSError, "forced observation"):
                export_endpoint.run_export(args)
        self.assertEqual(config.load_registry(), expected_registry)
        self.assertFalse((Path(args.manifest).parent.parent / "current.json").exists())
        # Even injected freeze failure must not strand the independently owned sampler.
        for thread in __import__("threading").enumerate():
            self.assertNotEqual(thread.name, "export-capacity-sampler", "sampler must stop even when freezing observation fails")

    def test_required_pointer_hashes_fail_closed_through_resolver_cli_and_protection(self) -> None:
        source = Path(__file__).resolve().parents[2]
        missing = object()
        fixtures = (("gamepad", "ARC-GPAD-003", "v2"), ("gamepad", "ARC-GPAD-001", "legacy"),
                    ("terrain", "ARC-RENDER-005", "terrain"), ("art", "ARC-RENDER-001", "absolute"),
                    ("display", "ARC-UI-001", "absolute"))
        for tool, task, schema in fixtures:
            with self.subTest(tool=tool, schema=schema):
                root = self.root / schema / task; candidate = root / task / ("a" * 40)
                candidate.mkdir(parents=True)
                executable = candidate / "Tool.app/Contents/MacOS/Tool"
                executable.parent.mkdir(parents=True); executable.write_bytes(b"fixture")
                path = candidate / "manifest.json"
                if schema == "v2":
                    original, _ = self.v2_manifest()
                    shutil.copytree(original.parent, candidate, dirs_exist_ok=True)
                    path = candidate / "manifest.json"
                else:
                    payload: dict = {"task": task, "candidate": {"commit": "a" * 40},
                               "app": {"executableRelativePath": "Tool.app/Contents/MacOS/Tool"}}
                    payload.update({"schema": "ARC-RENDER-005-v1"} if schema == "terrain" else {"schemaVersion": 1})
                    path.write_text(json.dumps(payload))
                digest = manifests.sha256(path)
                pointer: dict = {"schema": "arcanum-build-current-v2"} if schema == "v2" else (
                    {"schema": "ARC-RENDER-005-v1"} if schema == "terrain" else {"schemaVersion": 1})
                pointer.update({"manifest": str(path)} if schema == "absolute" else
                               {"manifestRelativePath": f"{'a' * 40}/manifest.json"})
                current = candidate.parent / "current.json"
                conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}
                conf["launchSelection"] = {tool: f"test:{task}:{'a' * 40}"}
                conf["inputs"] = {"module": str(executable), "terrainResource": str(executable), "sourceId": "synthetic"}
                config.save_config(conf)
                # Valid controls genuinely reach dry-run; hashless ART/UI is admitted only on absence.
                pointer["manifestSha256"] = digest
                current.write_text(json.dumps(pointer))
                result = subprocess.run([sys.executable, "-B", str(source / "tools/arcanum-local"), "launch", tool, "--dry-run"], capture_output=True, text=True)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertFalse(json.loads(result.stdout)["started"])
                for invalid in (missing, None, "", "a" * 63, "g" * 64, "A" * 64, "0" * 64):
                    if invalid is missing:
                        pointer.pop("manifestSha256", None)
                    else:
                        pointer["manifestSha256"] = invalid
                    current.write_text(json.dumps(pointer))
                    if invalid is missing and schema == "absolute":
                        manifests.resolve_current(root, tool)
                        continue
                    with self.assertRaises(ValueError):
                        manifests.resolve_current(root, tool)
                    result = subprocess.run([sys.executable, "-B", str(source / "tools/arcanum-local"), "launch", tool, "--dry-run"], capture_output=True, text=True)
                    self.assertEqual(result.returncode, 2, result.stdout)
                    self.assertNotIn(str(root), result.stderr)
                    snapshot = retention.protection_snapshot(conf, config.default_registry())
                    self.assertIn(["test", tool], snapshot["blockedDomains"])
                    self.assertEqual(retention.create_plan(conf, config.default_registry(), retention.inventory(conf, config.default_registry()))["payloads"], [])
        manifest, _ = self.v2_manifest(name="e" * 40)
        with self.assertRaisesRegex(ValueError, "full lowercase SHA256"):
            manifests._manifest(manifest, "gamepad", "")

    def test_payload_inventory_requires_exact_canonical_file_set_for_all_consumers(self) -> None:
        for index, flaw in enumerate(("dot-alias", "separator-alias", "duplicate", "zero-omitted", "equal-omitted")):
            with self.subTest(flaw=flaw):
                name = f"{index + 1:040x}"
                manifest, _ = self.v2_manifest(name=name)
                registry, identity = self.retired(manifest); conf = self.conf()
                plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
                self.assertEqual(len(plan["payloads"]), 1)
                payload = json.loads(manifest.read_text())
                payload["checks"] = [{"name": gate, "outcome": "PASS"} for gate in
                    ("core-harness", "godot-self-test", "export", "exported-smoke", "codesign-deep-strict")]
                extra = manifest.parent / "GamepadLab.app/Contents/MacOS/unknown"
                extra.write_bytes(b"" if flaw == "zero-omitted" else b"UNKNOWN")
                if flaw in ("dot-alias", "separator-alias", "duplicate"):
                    entry = dict(payload["payloadInventory"][0])
                    if flaw == "dot-alias": entry["relativePath"] = "GamepadLab.app/Contents/MacOS/./GamepadLab"
                    if flaw == "separator-alias": entry["relativePath"] = "GamepadLab.app//Contents/MacOS/GamepadLab"
                    payload["payloadInventory"].append(entry)
                payload["exportBudget"]["outputBytes"] = 7 if flaw == "zero-omitted" else 14
                manifest.write_text(json.dumps(payload))
                registry, identity = self.retired(manifest)
                before = config.digest(registry)
                with self.assertRaises(ValueError):
                    retention._bound_payload_inventory(payload, manifest.parent, manifest.parent / "GamepadLab.app")
                with self.assertRaises(ValueError):
                    retention.register_export(registry, conf=conf, tool="gamepad", candidate_id=identity, manifest=manifest)
                with self.assertRaises(ValueError):
                    retention.register_export(config.default_registry(), conf=conf, tool="gamepad", candidate_id=identity, manifest=manifest)
                self.assertEqual(config.digest(registry), before)
                with self.assertRaises(ValueError):
                    retention.export_preflight({}, "gamepad", payload["exportBudget"]["outputBytes"], None, conf=conf,
                        destinations={"export": manifest.parent, "temporary": self.root}, estimate_manifest=manifest)
                self.assertFalse(any(entry["eligible"] for entry in retention.inventory(conf, registry)))
                # Even a forged refreshed plan cannot authorize incomplete payload proof.
                plan["registrySha256"] = config.digest(registry)
                item = plan["payloads"][0]
                item["manifestSha256"] = manifests.sha256(manifest)
                item["candidateIdentity"] = retention._identity(manifest.parent)
                item["payloadIdentity"] = retention._identity(manifest.parent / "GamepadLab.app")
                with self.assertRaises(ValueError):
                    retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
                self.assertTrue(extra.exists())
                self.assertFalse((manifest.parent / "prune-intent-v1.json").exists())
        # Independent complete control includes both equal-sized sibling and empty file.
        manifest, _ = self.v2_manifest(name="f" * 40); payload = json.loads(manifest.read_text())
        for relative, data in (("GamepadLab.app/extra", b"UNKNOWN"), ("GamepadLab.app/empty", b"")):
            path = manifest.parent / relative; path.write_bytes(data)
            payload["payloadInventory"].append({"relativePath": relative, "bytes": len(data), "sha256": manifests.sha256(path)})
        payload["exportBudget"]["outputBytes"] = 14
        payload["checks"] = [{"name": gate, "outcome": "PASS"} for gate in
            ("core-harness", "godot-self-test", "export", "exported-smoke", "codesign-deep-strict")]
        manifest.write_text(json.dumps(payload)); registry = config.default_registry()
        self.assertEqual(retention._bound_payload_inventory(payload, manifest.parent, manifest.parent / "GamepadLab.app"), 14)
        retention.register_export(registry, conf=self.conf(), tool="gamepad", candidate_id="test:ARC-GPAD-003:" + "f" * 40, manifest=manifest)
        self.assertEqual(registry["builds"]["test:ARC-GPAD-003:" + "f" * 40]["payloadBytes"], 14)

    def test_unsafe_export_lock_only_candidates_reject_before_child_and_all_writes(self) -> None:
        estimate, _ = self.v2_manifest(); config.save_config(self.conf())
        sentinel = self.root / "outside-sentinel"; sentinel.write_bytes(b"UNCHANGED"); sentinel.chmod(0o644)
        before_config = config.config_path().read_bytes()
        for index, kind in enumerate(("symlink", "dangling", "fifo", "directory", "hardlink")):
            with self.subTest(kind=kind):
                commit = f"{index + 16:040x}"
                candidate = estimate.parent.parent / commit; candidate.mkdir()
                lock = candidate / ".export-lease.lock"
                if kind == "symlink": lock.symlink_to(sentinel)
                elif kind == "dangling": lock.symlink_to(self.root / "absent-sentinel")
                elif kind == "fifo": os.mkfifo(lock, 0o644)
                elif kind == "directory": lock.mkdir()
                else: os.link(sentinel, lock)
                args = argparse.Namespace(tool="gamepad", root_id="test", task="ARC-GPAD-003", candidate=commit,
                    manifest=str(candidate / "manifest.json"), temporary_root=str(self.root), output_bytes=7,
                    estimate_manifest=str(estimate), command=["/usr/bin/false"])
                before = lock.lstat()
                with patch("tools.local_dev.export_endpoint.subprocess.run") as child, patch("tools.local_dev.retention.export_preflight") as preflight:
                    with self.assertRaises((OSError, ValueError)):
                        export_endpoint.run_export(args)
                    child.assert_not_called(); preflight.assert_not_called()
                self.assertEqual(set(path.name for path in candidate.iterdir()), {".export-lease.lock"})
                self.assertEqual((lock.lstat().st_ino, lock.lstat().st_mode), (before.st_ino, before.st_mode))
                self.assertEqual(sentinel.read_bytes(), b"UNCHANGED")
                self.assertEqual(stat.S_IMODE(sentinel.stat().st_mode), 0o644)
                self.assertFalse((self.root / "absent-sentinel").exists())
                self.assertEqual(config.config_path().read_bytes(), before_config)
                self.assertFalse(config.registry_path().exists())
                if kind == "hardlink": lock.unlink()

    def test_shared_lock_paths_reject_unsafe_nodes_and_parent_hops(self) -> None:
        sentinel = self.root / "outside"; sentinel.write_bytes(b"UNCHANGED"); sentinel.chmod(0o644)
        for family in ("export", "launch", "registry"):
            for kind in ("symlink", "dangling", "fifo", "directory", "hardlink", "parent"):
                with self.subTest(family=family, kind=kind):
                    root = self.root / f"{family}-{kind}"; root.mkdir()
                    outside = root
                    os.environ["ARCANUM_LOCAL_HOME"] = str(root)
                    lock = root / ("locks/gamepad.lock" if family == "launch" else "build-registry-v1.lock" if family == "registry" else ".export-lease.lock")
                    lock.parent.mkdir(exist_ok=True)
                    if kind == "parent":
                        outside = self.root / f"parent-outside-{family}"; outside.mkdir()
                        linked = root / "linked"; linked.symlink_to(outside, target_is_directory=True)
                        if family == "launch": root = linked; lock = root / "locks/gamepad.lock"
                        elif family == "registry": os.environ["ARCANUM_LOCAL_HOME"] = str(linked); lock = linked / "build-registry-v1.lock"
                        else: lock = linked / ".export-lease.lock"
                    elif kind == "symlink": lock.symlink_to(sentinel)
                    elif kind == "dangling": lock.symlink_to(self.root / "absent")
                    elif kind == "fifo": os.mkfifo(lock)
                    elif kind == "directory": lock.mkdir()
                    else: os.link(sentinel, lock)
                    with self.assertRaises((OSError, ValueError)):
                        if family == "export": retention._try_exclusive(lock)
                        elif family == "launch": launch.lock_for(root, "gamepad")
                        else:
                            with config.registry_lock(): self.fail("unsafe registry lease was acquired")
                    self.assertEqual(sentinel.read_bytes(), b"UNCHANGED")
                    self.assertEqual(stat.S_IMODE(sentinel.stat().st_mode), 0o644)
                    self.assertFalse((self.root / "absent").exists())
                    if kind == "parent": self.assertEqual(list(outside.iterdir()), [])
                    if kind == "hardlink": lock.unlink()

    def test_regular_leases_preserve_duplicate_guard_and_release_after_failed_acquisition(self) -> None:
        root = self.root / "locks-control"
        handle = launch.lock_for(root, "gamepad")
        try:
            self.assertEqual(stat.S_IMODE((root / "locks/gamepad.lock").stat().st_mode), 0o600)
            with self.assertRaisesRegex(ValueError, "already active"):
                launch.lock_for(root, "gamepad")
            self.assertIsNone(retention._try_exclusive(root / "locks/gamepad.lock"))
        finally:
            handle.close()
        lease = retention._try_exclusive(root / "locks/gamepad.lock")
        self.assertIsNotNone(lease); retention._release(lease)

    def test_lock_leaf_replacement_rejects_without_mutating_target(self) -> None:
        lock = self.root / "raced.lock"; lock.write_bytes(b"prior"); lock.chmod(0o644)
        sentinel = self.root / "outside"; sentinel.write_bytes(b"UNCHANGED"); sentinel.chmod(0o644)
        actual_open = os.open
        def raced_open(path, flags, *args, **kwargs):
            if path == "raced.lock":
                lock.unlink(); lock.symlink_to(sentinel)
            return actual_open(path, flags, *args, **kwargs)
        with patch("tools.local_dev.config.os.open", side_effect=raced_open):
            with self.assertRaises(OSError):
                retention._try_exclusive(lock)
        self.assertEqual(sentinel.read_bytes(), b"UNCHANGED")
        self.assertEqual(stat.S_IMODE(sentinel.stat().st_mode), 0o644)

    def test_export_endpoint_new_registration_failure_preserves_old_publication(self) -> None:
        self.publication_failure("tools.local_dev.export_endpoint.retention.register_export")

    def test_export_endpoint_rejects_traversal_candidate_before_command_or_mutation(self) -> None:
        root = self.root / "artifacts"; root.mkdir()
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}; config.save_config(conf)
        escaped = self.root / "escaped-candidate"
        args = type("Args", (), {"tool": "gamepad", "root_id": "test", "task": "ARC-GPAD-003", "candidate": "../../escaped-candidate",
                                  "manifest": str(escaped / "manifest.json"), "command": ["/usr/bin/false"]})()
        with patch("tools.local_dev.export_endpoint.subprocess.run") as command:
            with self.assertRaisesRegex(ValueError, "one safe path component"):
                export_endpoint.run_export(args)
        command.assert_not_called()
        self.assertFalse((escaped / ".export-lease.lock").exists())
        self.assertFalse(escaped.exists())

    def test_export_endpoint_rejects_absolute_candidate_before_command_or_mutation(self) -> None:
        root = self.root / "artifacts"; root.mkdir()
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}; config.save_config(conf)
        escaped = self.root / "escaped-candidate"
        args = type("Args", (), {"tool": "gamepad", "root_id": "test", "task": "ARC-GPAD-003", "candidate": str(escaped),
                                  "manifest": str(escaped / "manifest.json"), "command": ["/usr/bin/false"]})()
        with patch("tools.local_dev.export_endpoint.subprocess.run") as command:
            with self.assertRaisesRegex(ValueError, "one safe path component"):
                export_endpoint.run_export(args)
        command.assert_not_called()
        self.assertFalse((escaped / ".export-lease.lock").exists())
        self.assertFalse(escaped.exists())

    def test_export_endpoint_rejects_candidate_symlink_before_command_or_mutation(self) -> None:
        root = self.root / "artifacts"; outside = self.root / "outside"; root.mkdir(); outside.mkdir()
        candidate = root / "ARC-GPAD-003" / ("a" * 40); candidate.parent.mkdir()
        candidate.symlink_to(outside, target_is_directory=True)
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}; config.save_config(conf)
        args = type("Args", (), {"tool": "gamepad", "root_id": "test", "task": "ARC-GPAD-003", "candidate": "a" * 40,
                                  "manifest": str(outside / "manifest.json"), "command": ["/usr/bin/false"]})()
        with patch("tools.local_dev.export_endpoint.subprocess.run") as command:
            with self.assertRaisesRegex(ValueError, "must not traverse a symlink"):
                export_endpoint.run_export(args)
        command.assert_not_called()
        self.assertFalse((outside / ".export-lease.lock").exists())

    def test_export_endpoint_rejects_task_symlink_before_command_or_mutation(self) -> None:
        root = self.root / "artifacts"; outside = self.root / "outside"; root.mkdir(); outside.mkdir()
        task = root / "ARC-GPAD-003"; task.symlink_to(outside, target_is_directory=True)
        conf = config.default_config(); conf["artifactRoots"] = {"test": str(root)}; config.save_config(conf)
        args = type("Args", (), {"tool": "gamepad", "root_id": "test", "task": "ARC-GPAD-003", "candidate": "a" * 40,
                                  "manifest": str(outside / ("a" * 40) / "manifest.json"), "command": ["/usr/bin/false"]})()
        with patch("tools.local_dev.export_endpoint.subprocess.run") as command:
            with self.assertRaisesRegex(ValueError, "must not traverse a symlink"):
                export_endpoint.run_export(args)
        command.assert_not_called()
        self.assertFalse((outside / ("a" * 40) / ".export-lease.lock").exists())


if __name__ == "__main__":
    unittest.main()
