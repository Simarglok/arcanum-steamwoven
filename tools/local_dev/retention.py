"""Conservative local candidate inventory, plan, lifecycle, and budget helpers."""
from __future__ import annotations

from datetime import datetime, timezone
import json
import os
from pathlib import Path
import secrets
import shutil
from typing import Any

from . import config
from .manifests import SUPPORTED_TOOLS, manifest_candidate, resolve_current, sha256

LIFECYCLES = {"exported", "open-review", "unmerged", "accepted", "retired"}


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
                payload = _safe_payload(candidate_dir, manifest) if manifest.is_file() and not manifest.is_symlink() else None
                if lifecycle == "retired" and candidate_id not in protected and payload is not None:
                    reason = "eligible retired registered package payload; manifests/evidence retained"
                    eligible = True
                if lifecycle in {"open-review", "unmerged", "accepted", "exported"}:
                    reason = f"KEEP: lifecycle {lifecycle}"
                entry = {"rootId": root_id, "task": task_dir.name, "candidate": candidate_dir.name,
                         "candidateId": candidate_id, "bytes": _tree_bytes(candidate_dir), "reason": reason, "eligible": eligible,
                         "manifestSha256": sha256(manifest) if manifest.is_file() and not manifest.is_symlink() else None}
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
        payloads.append({"candidateId": entry["candidateId"], "path": str(path), "candidateIdentity": _identity(path),
                         "rootIdentity": _node_identity(path.parents[1]), "payloadRelativePath": entry["payloadRelativePath"],
                         "payloadIdentity": _identity(payload), "manifestSha256": entry["manifestSha256"], "reason": entry["reason"]})
    return {"schema": "arcanum-prune-plan-v1", "id": secrets.token_urlsafe(18), "createdAt": datetime.now(timezone.utc).isoformat(),
            "configGeneration": conf["generation"], "registryGeneration": registry["generation"], "configSha256": config.digest(conf),
            "registrySha256": config.digest(registry), "payloads": payloads, "dryRun": True}


def apply_plan(plan: dict[str, Any], conf: dict[str, Any], registry: dict[str, Any], *, accepted_id: str) -> dict[str, Any]:
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
        if receipt.exists():
            if receipt.is_symlink():
                raise ValueError("candidate prune receipt is unsafe")
            existing = json.loads(receipt.read_text(encoding="utf-8"))
            if existing.get("planId") != accepted_id:
                raise ValueError("candidate already has a different prune receipt")
            removed.append(candidate_id)
            continue
        if _identity(candidate) != item.get("candidateIdentity"):
            raise ValueError("candidate changed since planning")
        if registry.get("builds", {}).get(candidate_id, {}).get("lifecycle") != "retired":
            raise ValueError("candidate is no longer explicitly retired")
        manifest = candidate / "manifest.json"
        payload = _safe_payload(candidate, manifest)
        if payload is None or sha256(manifest) != item.get("manifestSha256") or str(payload.relative_to(candidate)) != item.get("payloadRelativePath"):
            raise ValueError("manifest or payload changed since planning")
        if _identity(payload) != item.get("payloadIdentity"):
            raise ValueError("payload changed since planning")
        intent = candidate / "prune-intent-v1.json"
        intent_payload = {"schema": "prune-intent-v1", "id": accepted_id, "relativePayload": item["payloadRelativePath"],
                          "priorIdentity": item["payloadIdentity"], "reason": item["reason"]}
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
    digest = sha256(manifest)
    prior = registry.setdefault("accepted", {}).get(tool)
    registry.setdefault("rollback", {})[tool] = prior if prior else registry.setdefault("rollback", {}).get(tool)
    registry["accepted"][tool] = candidate_id
    registry.setdefault("builds", {})[candidate_id] = {"lifecycle": "accepted", "manifestSha256": digest,
        "authority": authority, "review": review}


def retire(registry: dict[str, Any], candidate_id: str) -> None:
    if candidate_id in set(registry.get("accepted", {}).values()) | set(registry.get("rollback", {}).values()):
        raise ValueError("accepted current or rollback cannot be retired")
    registry.setdefault("builds", {}).setdefault(candidate_id, {})["lifecycle"] = "retired"


def register_export(registry: dict[str, Any], *, tool: str, candidate_id: str, manifest: Path) -> None:
    """Record only an already complete local export; this never accepts it for launch."""
    if tool not in SUPPORTED_TOOLS or not manifest.is_file() or manifest.is_symlink():
        raise ValueError("register-export requires a supported tool and regular manifest")
    try:
        payload = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError("register-export manifest is invalid") from error
    checks = payload.get("checks")
    app = payload.get("app")
    if not isinstance(checks, list) or not checks or not isinstance(app, dict):
        raise ValueError("register-export requires a complete app manifest")
    outcomes = [entry.get("outcome", entry.get("status")) for entry in checks if isinstance(entry, dict)]
    if len(outcomes) != len(checks) or any(outcome != "PASS" for outcome in outcomes):
        raise ValueError("register-export requires all existing verification checks to pass")
    registry.setdefault("builds", {})[candidate_id] = {"lifecycle": "exported", "tool": tool,
        "manifestSha256": sha256(manifest), "registeredAt": datetime.now(timezone.utc).isoformat()}


def export_preflight(registry: dict[str, Any], tool: str, output_bytes: int | None, temporary_bytes: int | None) -> dict[str, Any]:
    if tool not in SUPPORTED_TOOLS:
        raise ValueError("unknown tool")
    if output_bytes is None or output_bytes <= 0:
        raise ValueError("no trusted output measurement/estimate; provide --output-bytes")
    temporary = temporary_bytes if temporary_bytes and temporary_bytes > 0 else output_bytes * 2
    reserve = max(2 * 1024**3, output_bytes)
    required = output_bytes + temporary + reserve
    return {"tool": tool, "outputBytes": output_bytes, "temporaryBytes": temporary, "reserveBytes": reserve, "requiredBytes": required}
