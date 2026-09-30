"""Fail-closed reader for the explicitly supported local artifact pointers."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

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
    if not path.is_file() or path.is_symlink():
        raise ValueError("pointer or manifest must be a non-symlink regular file")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError("invalid pointer or manifest JSON") from error
    if not isinstance(value, dict):
        raise ValueError("pointer or manifest must be an object")
    return value


def _contained(root: Path, candidate: Path) -> Path:
    resolved = candidate.resolve(strict=True)
    try:
        resolved.relative_to(root.resolve(strict=True))
    except ValueError as error:
        raise ValueError("pointer escapes registered artifact root") from error
    if resolved.is_symlink():
        raise ValueError("pointer resolves through a symlink")
    return resolved


def _manifest(path: Path, expected_tool: str, expected_digest: str | None = None) -> tuple[Path, dict[str, Any]]:
    manifest = _json(path)
    if expected_digest and sha256(path) != expected_digest:
        raise ValueError("manifest hash mismatch")
    schema = manifest.get("schema") or manifest.get("schemaVersion")
    if schema not in ("arcanum-build-manifest-v2", 1, "*-v1"):
        raise ValueError("unsupported manifest schema")
    tool = manifest.get("logicalTool") or manifest.get("task") or manifest.get("taskId")
    if tool and tool not in SUPPORTED_TOOLS[expected_tool]:
        raise ValueError("manifest ownership does not match requested tool")
    app = manifest.get("app")
    if not isinstance(app, dict):
        raise ValueError("manifest lacks an app record")
    return path, manifest


def resolve_current(root: Path, tool: str) -> tuple[Path, dict[str, Any], dict[str, Any]]:
    """Resolve only recognized current pointers; never use timestamps or directory order."""
    if tool not in SUPPORTED_TOOLS:
        raise ValueError("unknown logical tool")
    root = root.resolve(strict=True)
    candidates = []
    for task in SUPPORTED_TOOLS[tool]:
        candidates.extend((root / task / "current.json", root / task / "current-v2.json"))
    pointers = [candidate for candidate in candidates if candidate.exists()]
    if len(pointers) != 1:
        raise ValueError("expected exactly one supported current pointer")
    pointer_path = _contained(root, pointers[0])
    pointer = _json(pointer_path)
    schema = pointer.get("schema") or pointer.get("schemaVersion")
    if schema == "arcanum-build-current-v2":
        relative = pointer.get("manifestRelativePath")
        expected_digest = pointer.get("manifestSha256")
        if not isinstance(relative, str) or not isinstance(expected_digest, str):
            raise ValueError("v2 pointer lacks manifest binding")
        manifest_path = _contained(root, pointer_path.parent / relative)
    elif schema == 1:
        # Observed ART/UI uses an absolute local pointer; Gamepad uses a root-relative pointer.
        raw = pointer.get("manifest") or pointer.get("manifestRelativePath")
        if not isinstance(raw, str):
            raise ValueError("legacy pointer lacks manifest path")
        candidate = Path(raw).expanduser() if Path(raw).is_absolute() else pointer_path.parent / raw
        manifest_path = _contained(root, candidate)
        expected_digest = pointer.get("manifestSha256")
    elif schema == "*-v1":
        raw = pointer.get("manifestRelativePath")
        if not isinstance(raw, str):
            raise ValueError("ARC-RENDER-005 pointer lacks manifest path")
        manifest_path = _contained(root, pointer_path.parent / raw)
        expected_digest = pointer.get("manifestSha256")
    else:
        raise ValueError("unsupported or mixed pointer schema")
    manifest_path, manifest = _manifest(manifest_path, tool, expected_digest)
    return manifest_path, manifest, pointer


def manifest_candidate(manifest: dict[str, Any]) -> str:
    candidate = manifest.get("candidate")
    if isinstance(candidate, dict):
        candidate = candidate.get("commit")
    if not isinstance(candidate, str) or not candidate:
        raise ValueError("manifest lacks immutable candidate identity")
    return candidate
