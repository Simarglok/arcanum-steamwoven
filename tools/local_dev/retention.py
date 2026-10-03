"""Conservative local candidate inventory, plan, lifecycle, and budget helpers."""
from __future__ import annotations

from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import re
import stat
from typing import Any

from . import config
from . import launch
from .manifests import SUPPORTED_TOOLS, _manifest, _root_relative, manifest_candidate, resolve_pointer, sha256

LIFECYCLES = {"exported", "open-review", "unmerged", "accepted", "retired"}
REQUIRED_TASK_SUCCESS_GATES = {
    "ARC-WORLD-001": {"focused-tests", "source-world-original", "exported-world-original"},
    "ARC-GPAD-004": {"focused-tests", "source-world-original", "exported-world-original", "source-world-persistence", "exported-world-persistence", "dual-hostfxr"},
    "ARC-RENDER-006": {"focused-tests", "source-world-original", "exported-world-original", "source-world-persistence", "exported-world-persistence", "dual-hostfxr", "human-metadata", "source-human-original", "exported-human-original"},
    "ARC-WORLD-003": {"focused-tests", "source-world-original", "exported-world-original", "source-world-persistence", "exported-world-persistence", "dual-hostfxr", "human-metadata", "source-human-original", "exported-human-original", "source-grid-original", "exported-grid-original"},
}
REQUIRED_SUCCESS_GATES = {
    "gamepad": {"core-harness", "godot-self-test", "export", "exported-smoke", "codesign-deep-strict"},
    "display": {"locked-build", "source-layout-matrix", "export", "exported-layout-matrix", "exported-native-smoke", "codesign-deep-strict"},
    "terrain": {"locked-build", "source-headless", "source-graphical", "export", "exported-headless", "exported-graphical", "codesign-deep-strict"},
}


def _tool_for_task(task: str) -> str | None:
    return next((tool for tool, tasks in SUPPORTED_TOOLS.items() if task in tasks), None)


def _try_exclusive(path: Path) -> int | None:
    """Return a held lease descriptor, or None if another actor holds it."""
    descriptor = config.open_lock_file(path)
    try:
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        os.fchmod(descriptor, 0o600)
        return descriptor
    except BlockingIOError:
        os.close(descriptor)
        return None
    except BaseException:
        os.close(descriptor)
        raise


def _release(descriptor: int | None) -> None:
    if descriptor is not None:
        fcntl.flock(descriptor, fcntl.LOCK_UN)
        os.close(descriptor)


def _registered_manifest(*, conf: dict[str, Any], tool: str, candidate_id: str, manifest: Path) -> tuple[dict[str, Any], str, str, str]:
    """Prove exact root/task/commit/digest ownership before lifecycle mutation."""
    configured = config.candidate_path(conf, tool, candidate_id)
    parts = candidate_id.split(":", 2)
    if len(parts) != 3 or not all(parts):
        raise ValueError("candidate id must bind root, task, and candidate")
    root_id, task, candidate_name = parts
    if manifest.absolute() != configured / "manifest.json":
        raise ValueError("manifest is not the exact configured candidate")
    if (task not in SUPPORTED_TOOLS.get(tool, ()) or manifest.name != "manifest.json"
            or manifest.parent.name != candidate_name or not re.fullmatch(r"[0-9a-f]{40}", candidate_name)):
        raise ValueError("candidate id does not match manifest location or tool")
    _, payload = _manifest(manifest, tool)
    candidate = payload.get("candidate", {})
    digest = sha256(manifest)
    if (payload.get("schema") != "arcanum-build-manifest-v2" or payload.get("rootId") != root_id
            or payload.get("task") != task or not isinstance(candidate, dict)
            or not isinstance(candidate.get("commit"), str) or not re.fullmatch(r"[0-9a-f]{40}", candidate["commit"])
            or candidate["commit"] != candidate_name):
        raise ValueError("candidate manifest lacks exact v2 root/task/commit binding")
    return payload, root_id, task, digest


def _tree_bytes(path: Path) -> int:
    total = 0
    for item in path.rglob("*"):
        if item.is_symlink():
            raise ValueError("candidate contains symlink")
        if item.is_file():
            total += item.stat().st_size
    return total


def _safe_tree_bytes(path: Path) -> int | None:
    """Inventory is advisory: unsafe/unreadable nodes are protected, not fatal."""
    try:
        return _tree_bytes(path)
    except (OSError, ValueError):
        return None


def _identity(path: Path) -> dict[str, int]:
    if path.is_symlink():
        raise ValueError("candidate identity is a symlink")
    stat = path.stat()
    return {"device": stat.st_dev, "inode": stat.st_ino, "bytes": _tree_bytes(path) if path.is_dir() else stat.st_size}


def _node_identity(path: Path) -> dict[str, int]:
    if path.is_symlink():
        raise ValueError("candidate identity is a symlink")
    stat = path.stat()
    return {"device": stat.st_dev, "inode": stat.st_ino}


def _safe_payload(candidate: Path, manifest: Path) -> Path | None:
    """Return only a direct, package payload named by an immutable manifest."""
    try:
        payload = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    app = payload.get("app")
    raw = app.get("relativePath") if isinstance(app, dict) else None
    if not isinstance(raw, str) or Path(raw).is_absolute():
        return None
    path = candidate / raw
    try:
        # resolve() only tells us where the final target lands.  It cannot make
        # an intermediate symlink safe, including one introduced after plan.
        if config._has_symlink_component(path.absolute()):
            return None
        resolved = path.resolve(strict=True)
        resolved.relative_to(candidate.resolve(strict=True))
    except (OSError, ValueError):
        return None
    if (resolved.is_symlink() or config._has_symlink_component(resolved) or not resolved.is_dir()
            or resolved.suffix != ".app"):
        return None
    return resolved


def _bound_payload_inventory(payload: dict[str, Any], candidate: Path, app_path: Path) -> int:
    """Prove an exact canonical file set, including zero-byte package files."""
    config.canonical_existing(str(app_path), directory=True)
    inventory = payload.get("payloadInventory")
    if not isinstance(inventory, list) or not inventory:
        raise ValueError("payload inventory is missing")
    total = 0
    seen: set[str] = set()
    for entry in inventory:
        if (not isinstance(entry, dict) or not isinstance(entry.get("relativePath"), str)
                or not isinstance(entry.get("bytes"), int) or not isinstance(entry.get("sha256"), str)):
            raise ValueError("payload inventory is invalid")
        relative = _root_relative(entry["relativePath"], field="payload inventory path")
        item = candidate / relative
        try:
            item.relative_to(app_path)
        except ValueError as error:
            raise ValueError("payload inventory entry escapes package") from error
        if (relative in seen or config._has_symlink_component(item.absolute()) or not item.is_file()
                or item.is_symlink() or item.stat().st_size != entry["bytes"] or sha256(item) != entry["sha256"]):
            raise ValueError("payload inventory does not bind the on-disk export")
        seen.add(relative)
        total += entry["bytes"]
    actual: set[str] = set()
    def unreadable(error: OSError) -> None:
        raise error
    for directory, directories, files in os.walk(app_path, followlinks=False, onerror=unreadable):
        for name in directories + files:
            item = Path(directory) / name
            mode = item.lstat().st_mode
            if stat.S_ISREG(mode):
                actual.add(item.relative_to(candidate).as_posix())
            elif not stat.S_ISDIR(mode):
                raise ValueError("payload inventory contains unsafe package node")
    if actual != seen:
        raise ValueError("payload inventory omits or duplicates package files")
    return total


def _bound_evidence(payload: dict[str, Any], candidate: Path) -> None:
    """Prove every immutable evidence record names an unchanged local regular file."""
    evidence = payload.get("evidence")
    if not isinstance(evidence, list) or not evidence:
        raise ValueError("export evidence is missing")
    seen: set[str] = set()
    for entry in evidence:
        if not isinstance(entry, dict):
            raise ValueError("export evidence is invalid")
        relative, size, digest = entry.get("relativePath"), entry.get("bytes"), entry.get("sha256")
        relative = _root_relative(relative, field="evidence path")
        if (not isinstance(relative, str) or not relative or Path(relative).is_absolute() or ".." in Path(relative).parts
                or not isinstance(size, int) or size < 0 or not isinstance(digest, str) or len(digest) != 64):
            raise ValueError("export evidence is invalid")
        path = candidate / relative
        if (relative in seen or config._has_symlink_component(path.absolute()) or not path.is_file()
                or path.is_symlink() or path.stat().st_size != size or sha256(path) != digest):
            raise ValueError("export evidence does not bind an on-disk file")
        seen.add(relative)


def _matching_intent(intent: dict[str, Any], item: dict[str, Any], accepted_id: str) -> bool:
    return (intent.get("schema") == "prune-intent-v1" and intent.get("id") == accepted_id
            and intent.get("relativePayload") == item.get("payloadRelativePath")
            and intent.get("priorIdentity") == item.get("payloadIdentity")
            and intent.get("manifestSha256") == item.get("manifestSha256")
            and intent.get("reason") == item.get("reason"))


def _matching_receipt(receipt: dict[str, Any], item: dict[str, Any], accepted_id: str) -> bool:
    return (receipt.get("schema") == "payload-pruned-v1" and receipt.get("planId") == accepted_id
            and receipt.get("relativePayload") == item.get("payloadRelativePath")
            and receipt.get("priorIdentity") == item.get("payloadIdentity")
            and receipt.get("reason") == item.get("reason") and isinstance(receipt.get("prunedAt"), str)
            and bool(receipt["prunedAt"]))


def protection_snapshot(conf: dict[str, Any], registry: dict[str, Any]) -> dict[str, Any]:
    """Bind every pointer, including absence, without resolving ambiguity away."""
    protected: set[str] = set()
    blocked: set[tuple[str, str]] = set()
    bindings: dict[str, Any] = {}
    roots = conf.get("artifactRoots", {})
    if not isinstance(roots, dict):
        raise ValueError("invalid artifact roots")
    for root_id, text in sorted(roots.items()):
        for tool, tasks in SUPPORTED_TOOLS.items():
            try:
                root = config.canonical_existing(text, directory=True)
            except (OSError, ValueError):
                blocked.add((root_id, tool))
                bindings[f"{root_id}:{tool}"] = "unsafe-root"
                continue
            for task in tasks:
                for name in ("current.json", "current-v2.json"):
                    pointer = root / task / name
                    key = f"{root_id}:{task}:{name}"
                    try:
                        encoded = config.optional_authority_bytes(pointer)
                        if encoded is None:
                            bindings[key] = None
                            continue
                        bindings[key] = hashlib.sha256(encoded).hexdigest()
                        manifest, _, _ = resolve_pointer(root, pointer, tool, root_id=root_id)
                        relative = manifest.relative_to(root)
                        if len(relative.parts) != 3 or relative.parts[0] not in tasks:
                            raise ValueError("pointer candidate mapping is ambiguous")
                        protected.add(f"{root_id}:{relative.parts[0]}:{relative.parts[1]}")
                    except (OSError, ValueError):
                        bindings.setdefault(key, "unsafe-pointer")
                        blocked.add((root_id, tool))
    for field in ("accepted", "rollback"):
        values = registry.get(field, {})
        if not isinstance(values, dict) or any(not isinstance(value, str) for value in values.values()):
            blocked.update((root_id, tool) for root_id in roots for tool in SUPPORTED_TOOLS)
        else:
            protected.update(values.values())
            for tool, selected in values.items():
                try:
                    config.candidate_path(conf, tool, selected)
                except (OSError, ValueError):
                    blocked.update((root_id, name) for root_id in roots for name in SUPPORTED_TOOLS
                                   if tool not in SUPPORTED_TOOLS or name == tool)
    selections = conf.get("launchSelection", {})
    if not isinstance(selections, dict):
        blocked.update((root_id, tool) for root_id in roots for tool in SUPPORTED_TOOLS)
    else:
        for tool, selected in selections.items():
            parts = selected.split(":") if isinstance(selected, str) else []
            if (tool not in SUPPORTED_TOOLS or len(parts) != 3 or not all(parts)
                    or parts[0] not in roots or parts[1] not in SUPPORTED_TOOLS[tool]
                    or "/" in parts[2] or "\\" in parts[2] or parts[2] in (".", "..")):
                blocked.update((root_id, name) for root_id in roots for name in SUPPORTED_TOOLS
                               if tool not in SUPPORTED_TOOLS or name == tool)
            else:
                protected.add(selected)
    return {"pointers": bindings, "selectionSha256": config.digest({"selection": selections}),
            "protected": sorted(value for value in protected if isinstance(value, str)),
            "blockedDomains": [list(value) for value in sorted(blocked)]}


def _assert_unprotected(snapshot: dict[str, Any], tool: str, candidate_id: str) -> None:
    if (candidate_id in snapshot["protected"]
            or [candidate_id.split(":", 1)[0], tool] in snapshot["blockedDomains"]):
        raise ValueError("candidate is current/selected/rollback or has ambiguous pointer authority")


def inventory(conf: dict[str, Any], registry: dict[str, Any], *, show_paths: bool = False) -> list[dict[str, Any]]:
    results: list[dict[str, Any]] = []
    roots = conf.get("artifactRoots", {})
    if not isinstance(roots, dict):
        raise ValueError("invalid artifact roots")
    protection = protection_snapshot(conf, registry)
    protected = set(protection["protected"])
    builds = registry.get("builds", {})
    for root_id, root_text in sorted(roots.items()):
        known_tasks = {task for tasks in SUPPORTED_TOOLS.values() for task in tasks}
        try:
            root = config.canonical_existing(root_text, directory=True)
            task_dirs = sorted(root.iterdir())
        except (OSError, ValueError):
            results.append({"rootId": root_id, "task": None, "candidate": None, "candidateId": None,
                            "bytes": None, "reason": "KEEP/SKIP: artifact root is unreadable", "eligible": False,
                            "manifestSha256": None})
            continue
        for task_dir in task_dirs:
            if task_dir.name not in known_tasks:
                continue
            if not task_dir.is_dir() or task_dir.is_symlink() or config._has_symlink_component(task_dir.absolute()):
                results.append({"rootId": root_id, "task": task_dir.name, "candidate": None, "candidateId": None,
                                "bytes": None, "reason": "KEEP/SKIP: unsafe task directory", "eligible": False,
                                "manifestSha256": None})
                continue
            try:
                candidate_dirs = sorted(task_dir.iterdir())
            except OSError:
                results.append({"rootId": root_id, "task": task_dir.name, "candidate": None, "candidateId": None,
                                "bytes": None, "reason": "KEEP/SKIP: task directory is unreadable", "eligible": False,
                                "manifestSha256": None})
                continue
            for candidate_dir in candidate_dirs:
                candidate_id = f"{root_id}:{task_dir.name}:{candidate_dir.name}"
                if (not candidate_dir.is_dir() or candidate_dir.is_symlink()
                        or config._has_symlink_component(candidate_dir.absolute())):
                    results.append({"rootId": root_id, "task": task_dir.name, "candidate": candidate_dir.name,
                                    "candidateId": candidate_id, "bytes": None,
                                    "reason": "KEEP/SKIP: candidate directory is unsafe", "eligible": False,
                                    "manifestSha256": None})
                    continue
                state = builds.get(candidate_id, {}) if isinstance(builds, dict) else {}
                lifecycle = state.get("lifecycle") if isinstance(state, dict) else None
                reason = "KEEP: unknown ownership or lifecycle"
                eligible = False
                manifest = candidate_dir / "manifest.json"
                payload = None
                tool = _tool_for_task(task_dir.name)
                manifest_digest = None
                ownership_proven = False
                if tool is not None and manifest.is_file() and not manifest.is_symlink():
                    try:
                        _, parsed = _manifest(manifest, tool)
                        manifest_digest = sha256(manifest)
                        candidate = parsed.get("candidate", {})
                        candidate_commit = candidate.get("commit") if isinstance(candidate, dict) else None
                        ownership_proven = (
                            parsed.get("schema") == "arcanum-build-manifest-v2"
                            and parsed.get("rootId") == root_id
                            and isinstance(candidate, dict)
                            and re.fullmatch(r"[0-9a-f]{40}", candidate_commit or "") is not None
                            and candidate_dir.name == candidate_commit
                            and isinstance(state, dict)
                            and state.get("rootId") == root_id
                            and state.get("tool") == tool
                            and parsed.get("task") == task_dir.name
                            and state.get("task") == task_dir.name
                            and state.get("commit") == candidate.get("commit")
                            and state.get("manifestSha256") == manifest_digest
                        )
                        if ownership_proven:
                            payload = _safe_payload(candidate_dir, manifest)
                            if payload is not None:
                                _bound_payload_inventory(parsed, candidate_dir, payload)
                    except (OSError, ValueError):
                        reason = "KEEP/SKIP: unsupported, unsafe, or unproven manifest ownership"
                        payload = None
                if lifecycle == "retired" and candidate_id not in protected and ownership_proven and payload is not None:
                    reason = "eligible retired registered v2 package payload; manifests/evidence retained"
                    eligible = True
                if lifecycle in {"open-review", "unmerged", "accepted", "exported"}:
                    reason = f"KEEP: lifecycle {lifecycle}"
                if candidate_id in protected:
                    reason, eligible = "KEEP: current, explicit selection, accepted or rollback", False
                if [root_id, tool] in protection["blockedDomains"]:
                    reason, eligible = "KEEP/SKIP: ambiguous current pointer or selection authority", False
                candidate_bytes = _safe_tree_bytes(candidate_dir)
                if candidate_bytes is None:
                    reason, eligible, payload = "KEEP/SKIP: candidate is unsafe or unreadable", False, None
                entry = {"rootId": root_id, "task": task_dir.name, "candidate": candidate_dir.name,
                         "candidateId": candidate_id, "bytes": candidate_bytes, "reason": reason, "eligible": eligible,
                         "manifestSha256": manifest_digest}
                if payload is not None:
                    entry["payloadRelativePath"] = str(payload.relative_to(candidate_dir))
                    payload_bytes = _safe_tree_bytes(payload)
                    if payload_bytes is None:
                        entry["reason"], entry["eligible"] = "KEEP/SKIP: payload is unsafe or unreadable", False
                    else:
                        entry["payloadBytes"] = payload_bytes
                if show_paths:
                    entry["path"] = str(candidate_dir)
                results.append(entry)
    return results


def create_plan(conf: dict[str, Any], registry: dict[str, Any], entries: list[dict[str, Any]]) -> dict[str, Any]:
    payloads = []
    for entry in entries:
        if not entry["eligible"]:
            continue
        path = Path(entry["path"]) if "path" in entry else None
        if path is None:
            continue
        payload = path / entry["payloadRelativePath"]
        pointer_hashes: dict[str, str | None] = {}
        for pointer in (path.parent / "current.json", path.parent / "current-v2.json"):
            encoded = config.optional_authority_bytes(pointer)
            pointer_hashes[pointer.name] = hashlib.sha256(encoded).hexdigest() if encoded is not None else None
        payloads.append({"candidateId": entry["candidateId"], "path": str(path), "candidateIdentity": _identity(path),
                         "rootIdentity": _node_identity(path.parents[1]), "payloadRelativePath": entry["payloadRelativePath"],
                         "payloadIdentity": _identity(payload), "manifestSha256": entry["manifestSha256"], "pointerHashes": pointer_hashes,
                         "reason": entry["reason"]})
    return {"schema": "arcanum-prune-plan-v1", "id": secrets.token_urlsafe(18), "createdAt": datetime.now(timezone.utc).isoformat(),
            "configGeneration": conf["generation"], "registryGeneration": registry["generation"], "configSha256": config.digest(conf),
            "registrySha256": config.digest(registry), "protectionBindings": protection_snapshot(conf, registry),
            "payloads": payloads, "dryRun": True}


def apply_plan(plan: dict[str, Any], conf: dict[str, Any], registry: dict[str, Any], *, accepted_id: str) -> dict[str, Any]:
    """Apply under the registry lock after reloading every mutable authority."""
    with config.registry_lock():
        # CLI callers have persisted authorities; tests may use in-memory
        # fixtures, so retain those only when no local authority exists yet.
        fresh_conf = config._read(config.config_path(), config.CONFIG_SCHEMA, lambda: conf)
        fresh_registry = config._read(config.registry_path(), config.REGISTRY_SCHEMA, lambda: registry)
        return _apply_plan_locked(plan, fresh_conf, fresh_registry, accepted_id=accepted_id)


def _apply_plan_locked(plan: dict[str, Any], conf: dict[str, Any], registry: dict[str, Any], *, accepted_id: str) -> dict[str, Any]:
    if plan.get("schema") != "arcanum-prune-plan-v1" or plan.get("id") != accepted_id:
        raise ValueError("apply requires this exact accepted plan id")
    if plan.get("configGeneration") != conf["generation"] or plan.get("registryGeneration") != registry["generation"]:
        raise ValueError("config or registry changed since planning")
    if plan.get("configSha256") != config.digest(conf) or plan.get("registrySha256") != config.digest(registry):
        raise ValueError("config or registry digest changed since planning")
    if plan.get("protectionBindings") != protection_snapshot(conf, registry):
        raise ValueError("current pointer changed or protection proof missing; replan")
    roots = conf.get("artifactRoots", {})
    removed = []
    for item in plan.get("payloads", []):
        if not isinstance(item, dict):
            raise ValueError("invalid prune payload record")
        candidate = Path(item.get("path", ""))
        candidate_id = item.get("candidateId")
        if not isinstance(candidate_id, str) or not candidate.is_dir() or candidate.is_symlink():
            raise ValueError("candidate changed since planning")
        root_id = candidate_id.split(":", 1)[0]
        root_text = roots.get(root_id) if isinstance(roots, dict) else None
        if not isinstance(root_text, str):
            raise ValueError("registered artifact root changed since planning")
        root = config.canonical_existing(root_text, directory=True)
        try:
            candidate.resolve(strict=True).relative_to(root)
        except ValueError as error:
            raise ValueError("candidate escapes registered artifact root") from error
        if _node_identity(root) != item.get("rootIdentity"):
            raise ValueError("artifact root changed since planning")
        receipt = candidate / "payload-pruned-v1.json"
        intent = candidate / "prune-intent-v1.json"
        receipt_bytes = config.optional_authority_bytes(receipt)
        intent_bytes = config.optional_authority_bytes(intent)
        existing_receipt = None
        if receipt_bytes is not None:
            try:
                existing_receipt = json.loads(receipt_bytes)
            except (UnicodeError, json.JSONDecodeError) as error:
                raise ValueError("candidate prune receipt is invalid") from error
            if not isinstance(existing_receipt, dict) or not _matching_receipt(existing_receipt, item, accepted_id):
                raise ValueError("candidate already has a different prune receipt")
        existing_intent = None
        if intent_bytes is not None:
            try:
                existing_intent = json.loads(intent_bytes)
            except (UnicodeError, json.JSONDecodeError) as error:
                raise ValueError("candidate prune intent is invalid") from error
            if not isinstance(existing_intent, dict) or not _matching_intent(existing_intent, item, accepted_id):
                raise ValueError("candidate has a different prune intent")
        if existing_intent is None and existing_receipt is None and _identity(candidate) != item.get("candidateIdentity"):
            raise ValueError("candidate changed since planning")
        manifest = candidate / "manifest.json"
        pointer_hashes = item.get("pointerHashes", {})
        if not isinstance(pointer_hashes, dict) or set(pointer_hashes) != {"current.json", "current-v2.json"}:
            raise ValueError("plan pointer bindings are invalid")
        for name, digest in pointer_hashes.items():
            pointer = candidate.parent / name
            if not isinstance(name, str) or name not in {"current.json", "current-v2.json"}:
                raise ValueError("current pointer changed since planning")
            encoded = config.optional_authority_bytes(pointer)
            actual_digest = hashlib.sha256(encoded).hexdigest() if encoded is not None else None
            if actual_digest != digest:
                raise ValueError("current pointer changed since planning")
        task = candidate_id.split(":", 2)
        tool = _tool_for_task(task[1]) if len(task) == 3 else None
        if tool is None:
            raise ValueError("candidate task is unknown")
        if sha256(manifest) != item.get("manifestSha256"):
            raise ValueError("manifest changed since planning")
        parsed, bound_root, bound_task, digest = _registered_manifest(conf=conf, tool=tool, candidate_id=candidate_id, manifest=manifest)
        state = registry.get("builds", {}).get(candidate_id, {})
        candidate_commit = parsed["candidate"]["commit"]
        if (bound_root != root_id or bound_task != task[1] or not isinstance(state, dict)
                or (state.get("lifecycle"), state.get("tool"), state.get("rootId"), state.get("task"), state.get("commit"), state.get("manifestSha256"))
                != ("retired", tool, bound_root, bound_task, candidate_commit, digest)):
            raise ValueError("candidate authority changed since planning")
        launch_lease = _try_exclusive(config.home() / "locks" / f"{tool}.lock")
        try:
            export_lease = _try_exclusive(candidate / ".export-lease.lock")
        except BaseException:
            _release(launch_lease)
            raise
        if launch_lease is None or export_lease is None:
            _release(launch_lease); _release(export_lease)
            raise ValueError("candidate is leased by launcher or exporter")
        try:
            payload = _safe_payload(candidate, manifest)
            def recheck_records() -> None:
                if (config.optional_authority_bytes(receipt) != receipt_bytes
                        or config.optional_authority_bytes(intent) != intent_bytes):
                    raise ValueError("candidate prune records changed before payload mutation")

            recheck_records()
            fresh_protection = protection_snapshot(conf, registry)
            if fresh_protection != plan["protectionBindings"]:
                raise ValueError("current pointer changed since planning")
            _assert_unprotected(fresh_protection, tool, candidate_id)
            removed_path = candidate / item["payloadRelativePath"]
            if existing_receipt is not None:
                if (payload is not None or removed_path.exists() or removed_path.is_symlink()
                        or config._has_symlink_component(removed_path.absolute())):
                    raise ValueError("prune receipt exists while payload remains")
                removed.append(candidate_id)
                continue
            if payload is None:
                # A crash after removal leaves the intent but not the receipt.
                # Finalization requires the exact immutable manifest, all fresh
                # authority facts, pointer absence/presence bindings, and a
                # complete matching intent—not merely an absent payload.
                if (existing_intent is None or existing_intent.get("priorIdentity") != item["payloadIdentity"]
                        or existing_intent.get("manifestSha256") != item["manifestSha256"]
                        or removed_path.exists() or removed_path.is_symlink()
                        or config._has_symlink_component(removed_path.absolute())):
                    raise ValueError("manifest or payload changed since planning")
                _recheck_protection(plan, conf, registry, tool, candidate_id)
                recheck_records()
                config.write_atomic(receipt, {"schema": "payload-pruned-v1", "planId": accepted_id,
                                             "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"],
                                             "reason": item["reason"], "prunedAt": datetime.now(timezone.utc).isoformat()})
                removed.append(candidate_id)
                continue
            if (str(payload.relative_to(candidate)) != item.get("payloadRelativePath")
                    or _bound_payload_inventory(parsed, candidate, payload) != item["payloadIdentity"].get("bytes")
                    or _identity(payload) != item.get("payloadIdentity")):
                raise ValueError("payload changed since planning")
            executable = candidate / parsed["app"].get("executableRelativePath", "")
            if (config._has_symlink_component(executable.absolute()) or not executable.is_file()
                    or executable.is_symlink() or launch.exact_executable_in_use(executable)):
                raise ValueError("candidate executable is in use or unsafe")
            intent_payload = {"schema": "prune-intent-v1", "id": accepted_id, "relativePayload": item["payloadRelativePath"],
                              "priorIdentity": item["payloadIdentity"], "manifestSha256": item["manifestSha256"], "reason": item["reason"]}
            if existing_intent is None:
                _recheck_protection(plan, conf, registry, tool, candidate_id)
                recheck_records()
                config.write_atomic(intent, intent_payload)
                intent_bytes = (json.dumps(intent_payload, indent=2, sort_keys=True) + "\n").encode("utf-8")
            _recheck_protection(plan, conf, registry, tool, candidate_id)
            recheck_records()
            shutil.rmtree(payload)
            recheck_records()
            config.write_atomic(receipt, {"schema": "payload-pruned-v1", "planId": accepted_id,
                                          "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"],
                                          "reason": item["reason"], "prunedAt": datetime.now(timezone.utc).isoformat()})
            removed.append(candidate_id)
        finally:
            _release(launch_lease); _release(export_lease)
    return {"planId": accepted_id, "status": "applied" if removed else "no-op-safe", "removed": removed}


def _recheck_protection(plan: dict[str, Any], conf: dict[str, Any], registry: dict[str, Any], tool: str, candidate_id: str) -> None:
    fresh_conf = config._read(config.config_path(), config.CONFIG_SCHEMA, lambda: conf)
    fresh_registry = config._read(config.registry_path(), config.REGISTRY_SCHEMA, lambda: registry)
    if config.digest(fresh_conf) != plan["configSha256"] or config.digest(fresh_registry) != plan["registrySha256"]:
        raise ValueError("config or registry changed before payload mutation")
    snapshot = protection_snapshot(fresh_conf, fresh_registry)
    if snapshot != plan["protectionBindings"]:
        raise ValueError("current pointer changed before payload mutation")
    _assert_unprotected(snapshot, tool, candidate_id)


def accept(registry: dict[str, Any], *, conf: dict[str, Any], tool: str, candidate_id: str, manifest: Path, authority: str, review: str) -> None:
    if tool not in SUPPORTED_TOOLS or not authority or not review:
        raise ValueError("acceptance requires supported tool, authority, and review reference")
    payload, root_id, task, digest = _registered_manifest(conf=conf, tool=tool, candidate_id=candidate_id, manifest=manifest)
    existing = registry.get("builds", {}).get(candidate_id)
    candidate = payload["candidate"]
    if not isinstance(existing, dict) or (existing.get("tool"), existing.get("rootId"), existing.get("task"),
                                         existing.get("commit"), existing.get("manifestSha256")) != (
        tool, root_id, task, candidate["commit"], digest):
        raise ValueError("acceptance requires an exactly registered exported candidate")
    if existing.get("lifecycle") not in {"exported", "open-review", "unmerged"}:
        raise ValueError("acceptance candidate is not in an acceptable lifecycle")
    prior = registry.setdefault("accepted", {}).get(tool)
    registry.setdefault("rollback", {})[tool] = prior if prior else registry.setdefault("rollback", {}).get(tool)
    registry["accepted"][tool] = candidate_id
    registry.setdefault("builds", {})[candidate_id] = {**existing, "lifecycle": "accepted", "manifestSha256": digest,
        "authority": authority, "review": review}


def retire(registry: dict[str, Any], candidate_id: str, *, conf: dict[str, Any]) -> None:
    existing = registry.get("builds", {}).get(candidate_id)
    if not isinstance(existing, dict):
        raise ValueError("retire requires an existing registered candidate")
    tool = existing.get("tool")
    if not isinstance(tool, str) or tool not in SUPPORTED_TOOLS:
        raise ValueError("retire requires supported registered tool")
    manifest = config.candidate_path(conf, tool, candidate_id) / "manifest.json"
    payload, root_id, task, digest = _registered_manifest(conf=conf, tool=tool, candidate_id=candidate_id, manifest=manifest)
    if (existing.get("rootId"), existing.get("task"), existing.get("commit"), existing.get("manifestSha256")) != (
            root_id, task, payload["candidate"]["commit"], digest):
        raise ValueError("retire requires exact registered immutable identity")
    _assert_unprotected(protection_snapshot(conf, registry), tool, candidate_id)
    existing["lifecycle"] = "retired"


def register_export(registry: dict[str, Any], *, conf: dict[str, Any], tool: str, candidate_id: str, manifest: Path, lease_held: bool = False) -> None:
    """Record only an already complete local export; this never accepts it for launch."""
    if tool not in SUPPORTED_TOOLS or not manifest.is_file() or manifest.is_symlink():
        raise ValueError("register-export requires a supported tool and regular manifest")
    payload, root_id, task, digest = _registered_manifest(conf=conf, tool=tool, candidate_id=candidate_id, manifest=manifest)
    existing = registry.get("builds", {}).get(candidate_id)
    if existing is not None:
        if not isinstance(existing, dict) or (existing.get("tool"), existing.get("rootId"), existing.get("task"),
                existing.get("commit"), existing.get("manifestSha256")) != (tool, root_id, task, payload["candidate"]["commit"], digest):
            raise ValueError("registered candidate has conflicting immutable identity")
        _bound_evidence(payload, manifest.parent)
        _bound_payload_inventory(payload, manifest.parent, manifest.parent / payload["app"]["relativePath"])
        return
    checks = payload.get("checks")
    app = payload.get("app")
    if not isinstance(checks, list) or not checks or not isinstance(app, dict):
        raise ValueError("register-export requires a complete app manifest")
    named_outcomes = {entry.get("name"): entry.get("outcome", entry.get("status")) for entry in checks if isinstance(entry, dict)}
    if len(named_outcomes) != len(checks) or any(outcome != "PASS" for outcome in named_outcomes.values()):
        raise ValueError("register-export requires all named verification checks to pass")
    required = REQUIRED_SUCCESS_GATES.get(tool, set()) | REQUIRED_TASK_SUCCESS_GATES.get(task, set())
    if not required.issubset(named_outcomes):
        raise ValueError("register-export requires the complete tool-specific success gates")
    if task in ("ARC-WORLD-001", "ARC-GPAD-004", "ARC-RENDER-006", "ARC-WORLD-003") and payload.get("delivery", {}).get("originalInputModes") != "PASS":
        raise ValueError("world export requires verified original integrated inputs")
    if task in ("ARC-RENDER-006", "ARC-WORLD-003"):
        from .human_validation import validate_reports
        validate_reports(manifest.parent / "evidence", payload["candidate"]["commit"], task)
    if task == "ARC-WORLD-003":
        from .grid_export_gates import GATES as grid_gates, validate_reports as validate_gate_reports
        validate_gate_reports(manifest.parent / "evidence", payload["candidate"]["commit"])
        required_evidence = {"evidence/" + name + suffix for name in grid_gates.values()
                             for suffix in ("", ".result.json")}
        recorded_evidence = payload.get("evidence")
        if (not isinstance(recorded_evidence, list) or not required_evidence.issubset(
                entry.get("relativePath") for entry in recorded_evidence if isinstance(entry, dict))):
            raise ValueError("grid success-gate logs and exit receipts must be bound by the manifest inventory")
        from .grid_validation import validate_reports as validate_grid_reports
        validate_grid_reports(manifest.parent / "evidence", payload["candidate"]["commit"])
    app_path = manifest.parent / app["relativePath"]
    executable = manifest.parent / app["executableRelativePath"]
    try:
        measured_inventory = _bound_payload_inventory(payload, manifest.parent, app_path)
        _bound_evidence(payload, manifest.parent)
    except (OSError, ValueError) as error:
        raise ValueError("payload inventory or evidence does not bind the on-disk export") from error
    budget = payload.get("exportBudget", {})
    preflight = budget.get("preflight") if isinstance(budget, dict) else None
    preflight_path = manifest.parent / "evidence" / "export-preflight.json"
    try:
        recorded_preflight = json.loads(preflight_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError("register-export requires bound export preflight evidence") from error
    if (budget.get("outputBytes") != measured_inventory or not isinstance(preflight, dict)
            or recorded_preflight != preflight or preflight.get("candidateId") != candidate_id
            or preflight.get("candidateCommit") != payload["candidate"]["commit"]):
        raise ValueError("export budget output bytes do not match measured package inventory")
    from .export_endpoint import bound_budget
    if budget != bound_budget(manifest.parent, tool, candidate_id, measured_inventory):
        raise ValueError("export observation does not bind immutable budget")
    if not {"evidence/export-preflight.json", "evidence/export-observation.json"}.issubset(
            entry["relativePath"] for entry in payload["evidence"]):
        raise ValueError("export observation evidence digest is missing")
    if (not app_path.is_dir() or app_path.is_symlink() or not executable.is_file() or executable.is_symlink()
            or launch.exact_executable_in_use(executable)):
        raise ValueError("register-export requires a complete, inactive package payload")
    lease = None if lease_held else _try_exclusive(manifest.parent / ".export-lease.lock")
    if not lease_held and lease is None:
        raise ValueError("register-export candidate is already exporting")
    try:
        registry.setdefault("builds", {})[candidate_id] = {"lifecycle": "exported", "tool": tool, "rootId": root_id,
            "task": task, "commit": payload["candidate"]["commit"], "manifestSha256": digest,
            "payloadBytes": measured_inventory, "registeredAt": datetime.now(timezone.utc).isoformat()}
        observation = budget["observation"]
        if observation["temporaryMeasurementStatus"] == "qualified":
            registry.setdefault("measurements", {})[tool] = {
                "temporaryLowWaterBytes": observation["observedTemporaryBytes"],
                "candidateId": candidate_id, "manifestSha256": digest,
                "observationSha256": sha256(manifest.parent / "evidence/export-observation.json")}
    finally:
        _release(lease)


def destination_proof(path: Path) -> tuple[Path, dict[str, int]]:
    """Prove the actual destination's existing canonical ancestor, not config home."""
    path = path.expanduser().absolute()
    if ".." in path.parts or config._has_symlink_component(path):
        raise ValueError("unsafe export write destination")
    ancestor = path
    distance = 0
    while not ancestor.exists():
        ancestor = ancestor.parent
        distance += 1
    ancestor = config.canonical_existing(str(ancestor), directory=True)
    return ancestor, {**_node_identity(ancestor), "ancestorDistance": distance}


def verify_destinations(paths: dict[str, Path], proofs: dict[str, tuple[Path, dict[str, int]]]) -> None:
    for role, path in paths.items():
        ancestor, proof = proofs[role]
        if (config._has_symlink_component(path.absolute()) or not ancestor.is_dir()
                or _node_identity(ancestor) != {key: proof[key] for key in ("device", "inode")}
                or path.resolve(strict=False) != path.absolute()):
            raise ValueError("export write destination changed during export")
        # Mounts introduced beneath the original ancestor must not change device.
        if destination_proof(path)[1]["device"] != proof["device"]:
            raise ValueError("export write destination device changed during export")


def _verified_output(conf: dict[str, Any], tool: str, candidate_id: str, expected_digest: str | None = None) -> int:
    path = config.candidate_path(conf, tool, candidate_id) / "manifest.json"
    payload, _, _, digest = _registered_manifest(conf=conf, tool=tool, candidate_id=candidate_id, manifest=path)
    if expected_digest is not None and expected_digest != digest:
        raise ValueError("output estimate manifest digest changed")
    _bound_evidence(payload, path.parent)
    return _bound_payload_inventory(payload, path.parent, path.parent / payload["app"]["relativePath"])


def export_preflight(registry: dict[str, Any], tool: str, output_bytes: int | None, temporary_bytes: int | None,
                     *, conf: dict[str, Any], destinations: dict[str, Path], estimate_manifest: Path | None = None) -> dict[str, Any]:
    if tool not in SUPPORTED_TOOLS:
        raise ValueError("unknown tool")
    if output_bytes is not None and (type(output_bytes) is not int or output_bytes <= 0):
        raise ValueError("output estimate must be a positive integer")
    if temporary_bytes is not None and (type(temporary_bytes) is not int or temporary_bytes < 0):
        raise ValueError("temporary estimate must be a nonnegative integer")
    if not {"export", "temporary"}.issubset(destinations):
        raise ValueError("export and temporary destinations are required")
    measured_outputs = []
    references = set()
    accepted = registry.get("accepted", {}).get(tool)
    if isinstance(accepted, str):
        references.add(accepted)
    snapshot = protection_snapshot(conf, registry)
    for root_id, root_text in conf.get("artifactRoots", {}).items():
        root = config.canonical_existing(root_text, directory=True)
        for task in SUPPORTED_TOOLS[tool]:
            for name in ("current.json", "current-v2.json"):
                pointer = root / task / name
                if pointer.exists() and [root_id, tool] not in snapshot["blockedDomains"]:
                    manifest, _, _ = resolve_pointer(root, pointer, tool, root_id=root_id)
                    relative = manifest.relative_to(root)
                    references.add(f"{root_id}:{relative.parts[0]}:{relative.parts[1]}")
    for candidate_id in references:
        try:
            state = registry.get("builds", {}).get(candidate_id, {})
            measured_outputs.append(_verified_output(conf, tool, candidate_id, state.get("manifestSha256")))
        except (OSError, ValueError):
            continue
    measured = max(measured_outputs, default=0)
    if measured:
        output = (measured * 110 + 99) // 100
    elif output_bytes is not None and output_bytes > 0 and estimate_manifest is not None:
        # Explicit estimate is of ONE complete immutable payload, not history/source du.
        _, payload = _manifest(estimate_manifest, tool)
        candidate_id = f"{payload.get('rootId')}:{payload.get('task')}:{manifest_candidate(payload)}"
        if config.regular_file(estimate_manifest) != config.candidate_path(conf, tool, candidate_id) / "manifest.json":
            raise ValueError("estimate must be the exact configured immutable payload manifest")
        if output_bytes != _verified_output(conf, tool, candidate_id, sha256(estimate_manifest)):
            raise ValueError("explicit estimate must equal one verified complete payload")
        output = output_bytes
    else:
        raise ValueError("no trusted output measurement/estimate; provide --output-bytes and --estimate-manifest")
    measurements = registry.get("measurements", {}).get(tool, {})
    low_water = None
    if isinstance(measurements, dict) and isinstance(measurements.get("candidateId"), str):
        try:
            path = config.candidate_path(conf, tool, measurements["candidateId"]) / "manifest.json"
            payload, _, _, digest = _registered_manifest(conf=conf, tool=tool, candidate_id=measurements["candidateId"], manifest=path)
            _bound_evidence(payload, path.parent)
            observation = payload["exportBudget"]["observation"]
            if (digest == measurements.get("manifestSha256")
                    and sha256(path.parent / "evidence/export-observation.json") == measurements.get("observationSha256")
                    and observation.get("temporaryMeasurementStatus") == "qualified"
                    and observation.get("observedTemporaryBytes") == measurements.get("temporaryLowWaterBytes")):
                low_water = observation["observedTemporaryBytes"]
        except (OSError, ValueError, KeyError):
            pass
    if temporary_bytes is not None and temporary_bytes >= 0:
        temporary, temporary_basis = temporary_bytes, "explicit-estimate"
    elif isinstance(low_water, int) and low_water >= 0:
        temporary, temporary_basis = low_water, "qualified-history-estimate"
    else:
        temporary, temporary_basis = output * 2, "fallback-two-times-output"
    reserve = max(2 * 1024**3, output)
    required = output + temporary + reserve
    observations = []
    capacities: dict[int, int] = {}
    for role, path in sorted(destinations.items()):
        ancestor, proof = destination_proof(path)
        device = proof["device"]
        if device not in capacities:
            capacities[device] = shutil.disk_usage(ancestor).free
        free = capacities[device]
        if free < required:
            raise ValueError("observed destination free space is below the measured export budget")
        observations.append({"role": role, **proof, "observedFreeBytes": free, "requiredBytes": required})
    return {"tool": tool, "outputBytes": output, "temporaryBytes": temporary, "reserveBytes": reserve,
            "requiredBytes": required, "observedFreeBytes": min(capacities.values()), "destinations": observations,
            "outputBasis": "accepted-measurement-plus-10-percent" if measured else "explicit-estimate",
            "temporaryBasis": temporary_basis}
