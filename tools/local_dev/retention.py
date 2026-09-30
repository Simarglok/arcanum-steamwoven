"""Conservative local candidate inventory, plan, lifecycle, and budget helpers."""
from __future__ import annotations

from datetime import datetime, timezone
import fcntl
import json
import os
from pathlib import Path
import secrets
import shutil
import re
from typing import Any

from . import config
from . import launch
from .manifests import SUPPORTED_TOOLS, _manifest, manifest_candidate, sha256

LIFECYCLES = {"exported", "open-review", "unmerged", "accepted", "retired"}
REQUIRED_SUCCESS_GATES = {
    "gamepad": {"core-harness", "godot-self-test", "export", "exported-smoke", "codesign-deep-strict"},
    "display": {"locked-build", "source-layout-matrix", "export", "exported-layout-matrix", "exported-native-smoke", "codesign-deep-strict"},
    "terrain": {"locked-build", "source-headless", "source-graphical", "export", "exported-headless", "exported-graphical", "codesign-deep-strict"},
}


def _tool_for_task(task: str) -> str | None:
    return next((tool for tool, tasks in SUPPORTED_TOOLS.items() if task in tasks), None)


def _try_exclusive(path: Path) -> int | None:
    """Return a held lease descriptor, or None if another actor holds it."""
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    descriptor = os.open(path, os.O_CREAT | os.O_RDWR, 0o600)
    try:
        os.fchmod(descriptor, 0o600)
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        return descriptor
    except BlockingIOError:
        os.close(descriptor)
        return None


def _release(descriptor: int | None) -> None:
    if descriptor is not None:
        fcntl.flock(descriptor, fcntl.LOCK_UN)
        os.close(descriptor)


def _registered_manifest(*, tool: str, candidate_id: str, manifest: Path) -> tuple[dict[str, Any], str, str, str]:
    """Prove exact root/task/commit/digest ownership before lifecycle mutation."""
    parts = candidate_id.split(":", 2)
    if len(parts) != 3 or not all(parts):
        raise ValueError("candidate id must bind root, task, and candidate")
    root_id, task, candidate_name = parts
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
    """Prove every declared payload byte is a regular in-package on-disk file."""
    inventory = payload.get("payloadInventory")
    if not isinstance(inventory, list) or not inventory:
        raise ValueError("payload inventory is missing")
    total = 0
    seen: set[str] = set()
    for entry in inventory:
        if (not isinstance(entry, dict) or not isinstance(entry.get("relativePath"), str)
                or not isinstance(entry.get("bytes"), int) or not isinstance(entry.get("sha256"), str)):
            raise ValueError("payload inventory is invalid")
        item = candidate / entry["relativePath"]
        try:
            item.relative_to(app_path)
        except ValueError as error:
            raise ValueError("payload inventory entry escapes package") from error
        if (entry["relativePath"] in seen or config._has_symlink_component(item.absolute()) or not item.is_file()
                or item.is_symlink() or item.stat().st_size != entry["bytes"] or sha256(item) != entry["sha256"]):
            raise ValueError("payload inventory does not bind the on-disk export")
        seen.add(entry["relativePath"])
        total += entry["bytes"]
    if _tree_bytes(app_path) != total:
        raise ValueError("payload inventory omits package bytes")
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


def inventory(conf: dict[str, Any], registry: dict[str, Any], *, show_paths: bool = False) -> list[dict[str, Any]]:
    results: list[dict[str, Any]] = []
    roots = conf.get("artifactRoots", {})
    if not isinstance(roots, dict):
        raise ValueError("invalid artifact roots")
    protected = set(registry.get("accepted", {}).values()) | set(registry.get("rollback", {}).values())
    builds = registry.get("builds", {})
    for root_id, root_text in sorted(roots.items()):
        root = config.canonical_existing(root_text, directory=True)
        known_tasks = {task for tasks in SUPPORTED_TOOLS.values() for task in tasks}
        try:
            task_dirs = sorted(root.iterdir())
        except OSError:
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
                    reason = "KEEP: accepted current or rollback"
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
            if pointer.exists() and pointer.is_file() and not pointer.is_symlink():
                pointer_hashes[pointer.name] = sha256(pointer)
            else:
                # Absence is authority too: a current pointer added after the
                # plan must be treated as drift, never silently ignored.
                pointer_hashes[pointer.name] = None
        payloads.append({"candidateId": entry["candidateId"], "path": str(path), "candidateIdentity": _identity(path),
                         "rootIdentity": _node_identity(path.parents[1]), "payloadRelativePath": entry["payloadRelativePath"],
                         "payloadIdentity": _identity(payload), "manifestSha256": entry["manifestSha256"], "pointerHashes": pointer_hashes,
                         "reason": entry["reason"]})
    return {"schema": "arcanum-prune-plan-v1", "id": secrets.token_urlsafe(18), "createdAt": datetime.now(timezone.utc).isoformat(),
            "configGeneration": conf["generation"], "registryGeneration": registry["generation"], "configSha256": config.digest(conf),
            "registrySha256": config.digest(registry), "payloads": payloads, "dryRun": True}


def apply_plan(plan: dict[str, Any], conf: dict[str, Any], registry: dict[str, Any], *, accepted_id: str) -> dict[str, Any]:
    """Apply under the registry lock after reloading every mutable authority."""
    with config.registry_lock():
        # CLI callers have persisted authorities; tests may use in-memory
        # fixtures, so retain those only when no local authority exists yet.
        fresh_conf = config.load_config() if config.config_path().exists() else conf
        fresh_registry = config.load_registry() if config.registry_path().exists() else registry
        return _apply_plan_locked(plan, fresh_conf, fresh_registry, accepted_id=accepted_id)


def _apply_plan_locked(plan: dict[str, Any], conf: dict[str, Any], registry: dict[str, Any], *, accepted_id: str) -> dict[str, Any]:
    if plan.get("schema") != "arcanum-prune-plan-v1" or plan.get("id") != accepted_id:
        raise ValueError("apply requires this exact accepted plan id")
    if plan.get("configGeneration") != conf["generation"] or plan.get("registryGeneration") != registry["generation"]:
        raise ValueError("config or registry changed since planning")
    if plan.get("configSha256") != config.digest(conf) or plan.get("registrySha256") != config.digest(registry):
        raise ValueError("config or registry digest changed since planning")
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
        existing_receipt = None
        if receipt.exists():
            if receipt.is_symlink():
                raise ValueError("candidate prune receipt is unsafe")
            try:
                existing_receipt = json.loads(receipt.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError) as error:
                raise ValueError("candidate prune receipt is invalid") from error
            if not isinstance(existing_receipt, dict) or not _matching_receipt(existing_receipt, item, accepted_id):
                raise ValueError("candidate already has a different prune receipt")
        existing_intent = None
        if intent.exists():
            if intent.is_symlink():
                raise ValueError("candidate prune intent is unsafe")
            try:
                existing_intent = json.loads(intent.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError) as error:
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
            if digest is None:
                if pointer.exists() or pointer.is_symlink():
                    raise ValueError("current pointer changed since planning")
            elif not isinstance(digest, str) or not pointer.is_file() or pointer.is_symlink() or sha256(pointer) != digest:
                raise ValueError("current pointer changed since planning")
        task = candidate_id.split(":", 2)
        tool = _tool_for_task(task[1]) if len(task) == 3 else None
        if tool is None:
            raise ValueError("candidate task is unknown")
        if sha256(manifest) != item.get("manifestSha256"):
            raise ValueError("manifest changed since planning")
        parsed, bound_root, bound_task, digest = _registered_manifest(tool=tool, candidate_id=candidate_id, manifest=manifest)
        state = registry.get("builds", {}).get(candidate_id, {})
        candidate_commit = parsed["candidate"]["commit"]
        if (bound_root != root_id or bound_task != task[1] or not isinstance(state, dict)
                or (state.get("lifecycle"), state.get("tool"), state.get("rootId"), state.get("task"), state.get("commit"), state.get("manifestSha256"))
                != ("retired", tool, bound_root, bound_task, candidate_commit, digest)):
            raise ValueError("candidate authority changed since planning")
        launch_lease = _try_exclusive(config.home() / "locks" / f"{tool}.lock")
        export_lease = _try_exclusive(candidate / ".export-lease.lock")
        if launch_lease is None or export_lease is None:
            _release(launch_lease); _release(export_lease)
            raise ValueError("candidate is leased by launcher or exporter")
        payload = _safe_payload(candidate, manifest)
        try:
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
                config.write_atomic(intent, intent_payload)
            shutil.rmtree(payload)
            config.write_atomic(receipt, {"schema": "payload-pruned-v1", "planId": accepted_id,
                                          "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"],
                                          "reason": item["reason"], "prunedAt": datetime.now(timezone.utc).isoformat()})
            removed.append(candidate_id)
        finally:
            _release(launch_lease); _release(export_lease)
    return {"planId": accepted_id, "status": "applied" if removed else "no-op-safe", "removed": removed}


def accept(registry: dict[str, Any], *, tool: str, candidate_id: str, manifest: Path, authority: str, review: str) -> None:
    if tool not in SUPPORTED_TOOLS or not authority or not review:
        raise ValueError("acceptance requires supported tool, authority, and review reference")
    payload, root_id, task, digest = _registered_manifest(tool=tool, candidate_id=candidate_id, manifest=manifest)
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


def retire(registry: dict[str, Any], candidate_id: str) -> None:
    if candidate_id in set(registry.get("accepted", {}).values()) | set(registry.get("rollback", {}).values()):
        raise ValueError("accepted current or rollback cannot be retired")
    registry.setdefault("builds", {}).setdefault(candidate_id, {})["lifecycle"] = "retired"


def register_export(registry: dict[str, Any], *, tool: str, candidate_id: str, manifest: Path, lease_held: bool = False) -> None:
    """Record only an already complete local export; this never accepts it for launch."""
    if tool not in SUPPORTED_TOOLS or not manifest.is_file() or manifest.is_symlink():
        raise ValueError("register-export requires a supported tool and regular manifest")
    payload, root_id, task, digest = _registered_manifest(tool=tool, candidate_id=candidate_id, manifest=manifest)
    checks = payload.get("checks")
    app = payload.get("app")
    if not isinstance(checks, list) or not checks or not isinstance(app, dict):
        raise ValueError("register-export requires a complete app manifest")
    named_outcomes = {entry.get("name"): entry.get("outcome", entry.get("status")) for entry in checks if isinstance(entry, dict)}
    if len(named_outcomes) != len(checks) or any(outcome != "PASS" for outcome in named_outcomes.values()):
        raise ValueError("register-export requires all named verification checks to pass")
    required = REQUIRED_SUCCESS_GATES.get(tool, set())
    if not required.issubset(named_outcomes):
        raise ValueError("register-export requires the complete tool-specific success gates")
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
        if preflight.get("temporaryBasis") == "measured-low-water":
            registry.setdefault("measurements", {}).setdefault(tool, {})["temporaryLowWaterBytes"] = preflight["temporaryBytes"]
    finally:
        _release(lease)


def export_preflight(registry: dict[str, Any], tool: str, output_bytes: int | None, temporary_bytes: int | None) -> dict[str, Any]:
    if tool not in SUPPORTED_TOOLS:
        raise ValueError("unknown tool")
    accepted = {value for key, value in registry.get("accepted", {}).items() if key == tool}
    measured_outputs: list[int] = [int(state["payloadBytes"]) for candidate, state in registry.get("builds", {}).items()
                                   if candidate in accepted and isinstance(state, dict) and isinstance(state.get("payloadBytes"), int) and state["payloadBytes"] > 0]
    measured = max(measured_outputs, default=0)
    if measured:
        output = (measured * 110 + 99) // 100
    elif output_bytes is not None and output_bytes > 0:
        output = output_bytes
    else:
        raise ValueError("no trusted output measurement/estimate; provide --output-bytes")
    measurements = registry.get("measurements", {}).get(tool, {}) if isinstance(registry.get("measurements", {}), dict) else {}
    low_water = measurements.get("temporaryLowWaterBytes") if isinstance(measurements, dict) else None
    if temporary_bytes is not None and temporary_bytes > 0:
        temporary, temporary_basis = temporary_bytes, "explicit-estimate"
    elif isinstance(low_water, int) and low_water > 0:
        temporary, temporary_basis = low_water, "measured-low-water"
    else:
        temporary, temporary_basis = output * 2, "fallback-two-times-output"
    reserve = max(2 * 1024**3, output)
    required = output + temporary + reserve
    config.home().mkdir(mode=0o700, parents=True, exist_ok=True)
    free = shutil.disk_usage(config.home()).free
    if free < required:
        raise ValueError("observed free space is below the measured export budget")
    return {"tool": tool, "outputBytes": output, "temporaryBytes": temporary, "reserveBytes": reserve,
            "requiredBytes": required, "observedFreeBytes": free, "outputBasis": "accepted-measurement-plus-10-percent" if measured else "explicit-estimate",
            "temporaryBasis": temporary_basis}
