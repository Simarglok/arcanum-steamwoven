"""Conservative local candidate inventory, plan, lifecycle, and budget helpers."""
from __future__ import annotations

from datetime import datetime, timezone
import fcntl
import json
import os
from pathlib import Path
import secrets
import shutil
from typing import Any

from . import config
from . import launch
from .manifests import SUPPORTED_TOOLS, _manifest, manifest_candidate, sha256

LIFECYCLES = {"exported", "open-review", "unmerged", "accepted", "retired"}


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
    if task not in SUPPORTED_TOOLS.get(tool, ()) or manifest.name != "manifest.json" or manifest.parent.name != candidate_name:
        raise ValueError("candidate id does not match manifest location or tool")
    _, payload = _manifest(manifest, tool)
    candidate = payload.get("candidate", {})
    digest = sha256(manifest)
    if (payload.get("schema") != "arcanum-build-manifest-v2" or payload.get("rootId") != root_id
            or payload.get("task") != task or not isinstance(candidate, dict)
            or not isinstance(candidate.get("commit"), str) or len(candidate["commit"]) != 40):
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
        resolved = path.resolve(strict=True)
        resolved.relative_to(candidate.resolve(strict=True))
    except (OSError, ValueError):
        return None
    if resolved.is_symlink() or not resolved.is_dir() or resolved.suffix != ".app":
        return None
    return resolved


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
        for task_dir in sorted(root.iterdir()):
            if task_dir.name not in known_tasks:
                continue
            if not task_dir.is_dir() or task_dir.is_symlink():
                continue
            for candidate_dir in sorted(task_dir.iterdir()):
                if not candidate_dir.is_dir() or candidate_dir.is_symlink():
                    continue
                candidate_id = f"{root_id}:{task_dir.name}:{candidate_dir.name}"
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
                        ownership_proven = (
                            parsed.get("schema") == "arcanum-build-manifest-v2"
                            and parsed.get("rootId") == root_id
                            and isinstance(candidate, dict)
                            and isinstance(state, dict)
                            and state.get("rootId") == root_id
                            and state.get("task") == task_dir.name
                            and state.get("commit") == candidate.get("commit")
                            and state.get("manifestSha256") == manifest_digest
                        )
                        if ownership_proven:
                            payload = _safe_payload(candidate_dir, manifest)
                    except (OSError, ValueError):
                        reason = "KEEP/SKIP: unsupported, unsafe, or unproven manifest ownership"
                if lifecycle == "retired" and candidate_id not in protected and ownership_proven and payload is not None:
                    reason = "eligible retired registered v2 package payload; manifests/evidence retained"
                    eligible = True
                if lifecycle in {"open-review", "unmerged", "accepted", "exported"}:
                    reason = f"KEEP: lifecycle {lifecycle}"
                if candidate_id in protected:
                    reason = "KEEP: accepted current or rollback"
                entry = {"rootId": root_id, "task": task_dir.name, "candidate": candidate_dir.name,
                         "candidateId": candidate_id, "bytes": _tree_bytes(candidate_dir), "reason": reason, "eligible": eligible,
                         "manifestSha256": manifest_digest}
                if payload is not None:
                    entry["payloadRelativePath"] = str(payload.relative_to(candidate_dir))
                    entry["payloadBytes"] = _tree_bytes(payload)
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
        pointer_hashes = {}
        for pointer in (path.parent / "current.json", path.parent / "current-v2.json"):
            if pointer.exists() and pointer.is_file() and not pointer.is_symlink():
                pointer_hashes[pointer.name] = sha256(pointer)
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
        if receipt.exists():
            if receipt.is_symlink():
                raise ValueError("candidate prune receipt is unsafe")
            existing = json.loads(receipt.read_text(encoding="utf-8"))
            if existing.get("planId") != accepted_id:
                raise ValueError("candidate already has a different prune receipt")
            removed.append(candidate_id)
            continue
        existing_intent = None
        if intent.exists():
            if intent.is_symlink():
                raise ValueError("candidate prune intent is unsafe")
            existing_intent = json.loads(intent.read_text(encoding="utf-8"))
            if existing_intent.get("id") != accepted_id or existing_intent.get("relativePayload") != item.get("payloadRelativePath"):
                raise ValueError("candidate has a different prune intent")
        if existing_intent is None and _identity(candidate) != item.get("candidateIdentity"):
            raise ValueError("candidate changed since planning")
        if registry.get("builds", {}).get(candidate_id, {}).get("lifecycle") != "retired":
            raise ValueError("candidate is no longer explicitly retired")
        manifest = candidate / "manifest.json"
        pointer_hashes = item.get("pointerHashes", {})
        if not isinstance(pointer_hashes, dict):
            raise ValueError("plan pointer bindings are invalid")
        for name, digest in pointer_hashes.items():
            pointer = candidate.parent / name
            if (not isinstance(name, str) or name not in {"current.json", "current-v2.json"} or not isinstance(digest, str)
                    or not pointer.is_file() or pointer.is_symlink() or sha256(pointer) != digest):
                raise ValueError("current pointer changed since planning")
        payload = _safe_payload(candidate, manifest)
        if payload is None:
            # A crash after removal leaves the intent but not the receipt.
            # Finalizing the same intent is idempotent; any other absence is
            # unsafe and never treated as a successful prune.
            if existing_intent is None or (candidate / item["payloadRelativePath"]).exists():
                raise ValueError("manifest or payload changed since planning")
            config.write_atomic(receipt, {"schema": "payload-pruned-v1", "planId": accepted_id,
                                         "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"],
                                         "reason": item["reason"], "prunedAt": datetime.now(timezone.utc).isoformat()})
            removed.append(candidate_id)
            continue
        if sha256(manifest) != item.get("manifestSha256") or str(payload.relative_to(candidate)) != item.get("payloadRelativePath"):
            raise ValueError("manifest or payload changed since planning")
        if _identity(payload) != item.get("payloadIdentity"):
            raise ValueError("payload changed since planning")
        tool = _tool_for_task(candidate_id.split(":", 2)[1])
        if tool is None:
            raise ValueError("candidate task is unknown")
        launch_lease = _try_exclusive(config.home() / "locks" / f"{tool}.lock")
        export_lease = _try_exclusive(candidate / ".export-lease.lock")
        if launch_lease is None or export_lease is None:
            _release(launch_lease); _release(export_lease)
            raise ValueError("candidate is leased by launcher or exporter")
        try:
            app = json.loads(manifest.read_text(encoding="utf-8")).get("app", {})
            executable = candidate / app.get("executableRelativePath", "")
            if not executable.is_file() or launch.exact_executable_in_use(executable):
                raise ValueError("candidate executable is in use or unsafe")
        finally:
            _release(launch_lease); _release(export_lease)
        intent_payload = {"schema": "prune-intent-v1", "id": accepted_id, "relativePayload": item["payloadRelativePath"],
                          "priorIdentity": item["payloadIdentity"], "reason": item["reason"]}
        if existing_intent is None:
            config.write_atomic(intent, intent_payload)
        shutil.rmtree(payload)
        config.write_atomic(receipt, {"schema": "payload-pruned-v1", "planId": accepted_id,
                                      "relativePayload": item["payloadRelativePath"], "priorIdentity": item["payloadIdentity"],
                                      "reason": item["reason"], "prunedAt": datetime.now(timezone.utc).isoformat()})
        removed.append(candidate_id)
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


def register_export(registry: dict[str, Any], *, tool: str, candidate_id: str, manifest: Path) -> None:
    """Record only an already complete local export; this never accepts it for launch."""
    if tool not in SUPPORTED_TOOLS or not manifest.is_file() or manifest.is_symlink():
        raise ValueError("register-export requires a supported tool and regular manifest")
    payload, root_id, task, digest = _registered_manifest(tool=tool, candidate_id=candidate_id, manifest=manifest)
    checks = payload.get("checks")
    app = payload.get("app")
    if not isinstance(checks, list) or not checks or not isinstance(app, dict):
        raise ValueError("register-export requires a complete app manifest")
    outcomes = [entry.get("outcome", entry.get("status")) for entry in checks if isinstance(entry, dict)]
    if len(outcomes) != len(checks) or any(outcome != "PASS" for outcome in outcomes):
        raise ValueError("register-export requires all existing verification checks to pass")
    app_path = manifest.parent / app["relativePath"]
    executable = manifest.parent / app["executableRelativePath"]
    if not app_path.is_dir() or not executable.is_file() or launch.exact_executable_in_use(executable):
        raise ValueError("register-export requires a complete, inactive package payload")
    lease = _try_exclusive(manifest.parent / ".export-lease.lock")
    if lease is None:
        raise ValueError("register-export candidate is already exporting")
    try:
        registry.setdefault("builds", {})[candidate_id] = {"lifecycle": "exported", "tool": tool, "rootId": root_id,
            "task": task, "commit": payload["candidate"]["commit"], "manifestSha256": digest,
            "registeredAt": datetime.now(timezone.utc).isoformat()}
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
    temporary = temporary_bytes if temporary_bytes and temporary_bytes > 0 else low_water if isinstance(low_water, int) and low_water > 0 else output * 2
    reserve = max(2 * 1024**3, output)
    required = output + temporary + reserve
    config.home().mkdir(mode=0o700, parents=True, exist_ok=True)
    free = shutil.disk_usage(config.home()).free
    if free < required:
        raise ValueError("observed free space is below the measured export budget")
    return {"tool": tool, "outputBytes": output, "temporaryBytes": temporary, "reserveBytes": reserve,
            "requiredBytes": required, "observedFreeBytes": free, "outputBasis": "accepted-measurement-plus-10-percent" if measured else "explicit-estimate"}
