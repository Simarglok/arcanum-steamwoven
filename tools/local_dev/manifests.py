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
    is_v1 = schema == 1
    is_terrain_v1 = expected_tool == "terrain" and isinstance(schema, str) and schema.endswith("-v1")
    if schema != "arcanum-build-manifest-v2" and not is_v1 and not is_terrain_v1:
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
    elif schema == 1 and tool in {"art", "display"}:
        # Observed ART/UI pointers bind an absolute, local manifest path.  Do
        # not accept a relative lookalike here: schema mixing is unsafe.
        raw = pointer.get("manifest")
        if not isinstance(raw, str) or not Path(raw).is_absolute():
            raise ValueError("legacy ART/UI pointer requires an absolute manifest path")
        manifest_path = _contained(root, Path(raw).expanduser())
        expected_digest = None
    elif schema == 1 and tool == "gamepad":
        raw, expected_digest = pointer.get("manifestRelativePath"), pointer.get("manifestSha256")
        if not isinstance(raw, str) or Path(raw).is_absolute() or not isinstance(expected_digest, str):
            raise ValueError("legacy Gamepad pointer requires relative manifest and hash")
        manifest_path = _contained(root, pointer_path.parent / raw)
    elif tool == "terrain" and isinstance(schema, str) and schema.endswith("-v1"):
        raw = pointer.get("manifestRelativePath")
        expected_digest = pointer.get("manifestSha256")
        if not isinstance(raw, str) or Path(raw).is_absolute() or not isinstance(expected_digest, str):
            raise ValueError("ARC-RENDER-005 pointer lacks manifest path")
        manifest_path = _contained(root, pointer_path.parent / raw)
        expected_digest = pointer.get("manifestSha256")
    else:
        raise ValueError("unsupported or mixed pointer schema")
    manifest_path, manifest = _manifest(manifest_path, tool, expected_digest)
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
