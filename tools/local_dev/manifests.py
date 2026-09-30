"""Fail-closed reader for the explicitly supported local artifact pointers."""
from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path
from typing import Any

from . import config

SUPPORTED_TOOLS = {
    "art": ("ARC-RENDER-001", "ARC-RENDER-002"),
    "terrain": ("ARC-RENDER-003", "ARC-RENDER-004", "ARC-RENDER-005", "ARC-DEV-001"),
    "gamepad": ("ARC-GPAD-001", "ARC-GPAD-002", "ARC-GPAD-003"),
    "display": ("ARC-UI-001",),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _json(path: Path) -> dict[str, Any]:
    if not path.is_file() or path.is_symlink() or config._has_symlink_component(path.absolute()):
        raise ValueError("pointer or manifest must be a non-symlink regular file")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError("invalid pointer or manifest JSON") from error
    if not isinstance(value, dict):
        raise ValueError("pointer or manifest must be an object")
    return value


def _contained(root: Path, candidate: Path) -> Path:
    if config._has_symlink_component(candidate.absolute()):
        raise ValueError("pointer resolves through a symlink")
    resolved = candidate.resolve(strict=True)
    try:
        resolved.relative_to(root.resolve(strict=True))
    except ValueError as error:
        raise ValueError("pointer escapes registered artifact root") from error
    if resolved.is_symlink() or config._has_symlink_component(resolved):
        raise ValueError("pointer resolves through a symlink")
    return resolved


def _manifest(path: Path, expected_tool: str, expected_digest: str | None = None) -> tuple[Path, dict[str, Any]]:
    manifest = _json(path)
    if expected_digest and sha256(path) != expected_digest:
        raise ValueError("manifest hash mismatch")
    schema = manifest.get("schema")
    schema_version = manifest.get("schemaVersion")
    is_v1 = schema is None and schema_version == 1
    is_terrain_v1 = schema == "ARC-RENDER-005-v1" and schema_version is None
    is_v2 = schema == "arcanum-build-manifest-v2" and schema_version is None
    if not is_v2 and not is_v1 and not (expected_tool == "terrain" and is_terrain_v1):
        raise ValueError("unsupported manifest schema")
    if is_v2:
        _validate_v2_manifest(manifest, expected_tool)
    task = manifest.get("task") or manifest.get("taskId")
    if not isinstance(task, str) or task not in SUPPORTED_TOOLS[expected_tool]:
        raise ValueError("manifest ownership does not match requested tool")
    app = manifest.get("app")
    if not isinstance(app, dict):
        raise ValueError("manifest lacks an app record")
    return path, manifest


def _root_relative(value: object, *, field: str) -> str:
    if not isinstance(value, str) or not value or Path(value).is_absolute() or ".." in Path(value).parts:
        raise ValueError(f"v2 {field} must be root-relative")
    return value


def _validate_v2_manifest(manifest: dict[str, Any], expected_tool: str) -> None:
    """Validate every immutable v2 ownership/binding field before use."""
    if manifest.get("project") != "arcanum-steamwoven" or manifest.get("logicalTool") != expected_tool:
        raise ValueError("v2 manifest project or logical-tool binding is invalid")
    if not isinstance(manifest.get("rootId"), str) or not manifest["rootId"]:
        raise ValueError("v2 manifest lacks root identity")
    task = manifest.get("task")
    candidate = manifest.get("candidate")
    if not isinstance(task, str) or task not in SUPPORTED_TOOLS[expected_tool] or not isinstance(candidate, dict):
        raise ValueError("v2 manifest task or candidate binding is invalid")
    for key in ("commit", "tree"):
        if not isinstance(candidate.get(key), str) or not re.fullmatch(r"[0-9a-f]{40}", candidate[key]):
            raise ValueError("v2 manifest requires full candidate commit and tree")
    app = manifest.get("app")
    if not isinstance(app, dict) or not isinstance(app.get("relativePath"), str) or not isinstance(app.get("executableRelativePath"), str):
        raise ValueError("v2 manifest app binding is invalid")
    for key in ("relativePath", "executableRelativePath"):
        _root_relative(app[key], field="app path")
    inventory = manifest.get("payloadInventory")
    evidence = manifest.get("evidence")
    budget = manifest.get("exportBudget")
    if not isinstance(inventory, list) or not inventory or not isinstance(evidence, list) or not evidence or not isinstance(budget, dict) or manifest.get("acceptance") != "not-accepted":
        raise ValueError("v2 manifest lacks immutable inventory, evidence, budget, or acceptance binding")
    for entry in inventory:
        if (not isinstance(entry, dict) or not isinstance(entry.get("bytes"), int) or entry["bytes"] < 0
                or not isinstance(entry.get("sha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", entry["sha256"])):
            raise ValueError("v2 payload inventory is invalid")
        _root_relative(entry.get("relativePath"), field="payload inventory path")
    for entry in evidence:
        if not isinstance(entry, dict):
            raise ValueError("v2 evidence entry is invalid")
        _root_relative(entry.get("relativePath"), field="evidence path")
        if (not isinstance(entry.get("bytes"), int) or entry["bytes"] < 0
                or not isinstance(entry.get("sha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", entry["sha256"])):
            raise ValueError("v2 evidence entry lacks measured bytes")
    for key in ("outputBytes", "temporaryLowWaterBytes"):
        if not isinstance(budget.get(key), int) or budget[key] < 0:
            raise ValueError("v2 export budget lacks measured byte fields")


def resolve_current(root: Path, tool: str, *, root_id: str | None = None) -> tuple[Path, dict[str, Any], dict[str, Any]]:
    """Resolve only recognized current pointers; never use timestamps or directory order."""
    if tool not in SUPPORTED_TOOLS:
        raise ValueError("unknown logical tool")
    root = config.canonical_existing(str(root), directory=True)
    candidates = []
    for task in SUPPORTED_TOOLS[tool]:
        candidates.extend((root / task / "current.json", root / task / "current-v2.json"))
    pointers = [candidate for candidate in candidates if candidate.exists()]
    if len(pointers) != 1:
        raise ValueError("expected exactly one supported current pointer")
    pointer_path = _contained(root, pointers[0])
    pointer = _json(pointer_path)
    schema, schema_version = pointer.get("schema"), pointer.get("schemaVersion")
    if schema == "arcanum-build-current-v2" and schema_version is None:
        relative = pointer.get("manifestRelativePath")
        expected_digest = pointer.get("manifestSha256")
        if not isinstance(relative, str) or not isinstance(expected_digest, str):
            raise ValueError("v2 pointer lacks manifest binding")
        manifest_path = _contained(root, pointer_path.parent / relative)
    elif schema is None and schema_version == 1 and tool in {"art", "display"}:
        # Observed ART/UI pointers bind an absolute, local manifest path.  Do
        # not accept a relative lookalike here: schema mixing is unsafe.
        raw = pointer.get("manifest")
        if not isinstance(raw, str) or not Path(raw).is_absolute():
            raise ValueError("legacy ART/UI pointer requires an absolute manifest path")
        manifest_path = _contained(root, Path(raw).expanduser())
        expected_digest = None
    elif schema is None and schema_version == 1 and tool == "gamepad":
        raw, expected_digest = pointer.get("manifestRelativePath"), pointer.get("manifestSha256")
        if not isinstance(raw, str) or Path(raw).is_absolute() or not isinstance(expected_digest, str):
            raise ValueError("legacy Gamepad pointer requires relative manifest and hash")
        manifest_path = _contained(root, pointer_path.parent / raw)
    elif tool == "terrain" and schema == "ARC-RENDER-005-v1" and schema_version is None:
        raw = pointer.get("manifestRelativePath")
        expected_digest = pointer.get("manifestSha256")
        if not isinstance(raw, str) or Path(raw).is_absolute() or not isinstance(expected_digest, str):
            raise ValueError("ARC-RENDER-005 pointer lacks manifest path")
        manifest_path = _contained(root, pointer_path.parent / raw)
        expected_digest = pointer.get("manifestSha256")
    else:
        raise ValueError("unsupported or mixed pointer schema")
    manifest_path, manifest = _manifest(manifest_path, tool, expected_digest)
    if root_id is not None and manifest.get("schema") == "arcanum-build-manifest-v2" and manifest.get("rootId") != root_id:
        raise ValueError("v2 manifest root identity does not match configured artifact root")
    app = manifest["app"]
    executable = app.get("executableRelativePath") or app.get("executable")
    if not isinstance(executable, str) or not executable:
        raise ValueError("manifest lacks executable")
    executable_path = Path(executable).expanduser()
    if not executable_path.is_absolute():
        executable_path = manifest_path.parent / executable_path
    # Pass a resolved local executable to the launcher rather than allowing a
    # relative path to depend on the caller's working directory.
    app["executable"] = str(_contained(root, executable_path))
    return manifest_path, manifest, pointer


def manifest_candidate(manifest: dict[str, Any]) -> str:
    candidate = manifest.get("candidate")
    if isinstance(candidate, dict):
        candidate = candidate.get("commit")
    if not isinstance(candidate, str) or not candidate:
        raise ValueError("manifest lacks immutable candidate identity")
    return candidate
