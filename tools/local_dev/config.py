"""Private local configuration with atomic, owner-only JSON files."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import tempfile
from typing import Any
import fcntl
from contextlib import contextmanager

CONFIG_SCHEMA = "arcanum-local-config-v1"
REGISTRY_SCHEMA = "build-registry-v1"


def home() -> Path:
    override = os.environ.get("ARCANUM_LOCAL_HOME")
    return Path(override).expanduser() if override else Path.home() / "Library/Application Support/Arcanum Steamwoven"


def config_path() -> Path:
    return home() / "config-v1.json"


def registry_path() -> Path:
    return home() / "build-registry-v1.json"


def default_config() -> dict[str, Any]:
    return {"schema": CONFIG_SCHEMA, "generation": 0, "artifactRoots": {}, "inputs": {}, "preferences": {}, "launchSelection": {}}


def default_registry() -> dict[str, Any]:
    return {"schema": REGISTRY_SCHEMA, "generation": 0, "builds": {}, "accepted": {}, "rollback": {}}


def _read(path: Path, schema: str, factory: Any) -> dict[str, Any]:
    if not path.exists():
        return factory()
    if path.is_symlink():
        raise ValueError(f"local configuration is a symlink: {path.name}")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"invalid local configuration: {path.name}") from error
    if not isinstance(value, dict) or value.get("schema") != schema or not isinstance(value.get("generation"), int):
        raise ValueError(f"unsupported local configuration schema: {path.name}")
    return value


def load_config() -> dict[str, Any]:
    return _read(config_path(), CONFIG_SCHEMA, default_config)


def load_registry() -> dict[str, Any]:
    return _read(registry_path(), REGISTRY_SCHEMA, default_registry)


def write_atomic(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    os.chmod(path.parent, 0o700)
    encoded = (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")
    descriptor, temporary_name = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
    temporary = Path(temporary_name)
    try:
        os.fchmod(descriptor, 0o600)
        with os.fdopen(descriptor, "wb", closefd=True) as stream:
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        os.chmod(path, 0o600)
        directory = os.open(path.parent, os.O_RDONLY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if temporary.exists():
            temporary.unlink()


def save_config(value: dict[str, Any]) -> None:
    value["schema"] = CONFIG_SCHEMA
    value["generation"] = int(value.get("generation", 0)) + 1
    write_atomic(config_path(), value)


def save_registry(value: dict[str, Any]) -> None:
    value["schema"] = REGISTRY_SCHEMA
    value["generation"] = int(value.get("generation", 0)) + 1
    write_atomic(registry_path(), value)


def digest(value: dict[str, Any]) -> str:
    return hashlib.sha256(json.dumps(value, separators=(",", ":"), sort_keys=True).encode("utf-8")).hexdigest()


def canonical_existing(path_text: str, *, directory: bool | None = None) -> Path:
    path = Path(path_text).expanduser().absolute()
    if not path.exists() or path.is_symlink() or _has_symlink_component(path):
        raise ValueError("path must exist and must not be a symlink")
    if directory is True and not path.is_dir():
        raise ValueError("path must be a directory")
    if directory is False and not path.is_file():
        raise ValueError("path must be a regular file")
    return path.resolve(strict=True)


def _has_symlink_component(path: Path) -> bool:
    """Reject every supplied path component, not merely a symlink leaf."""
    current = Path(path.anchor)
    for component in path.parts[1:]:
        current /= component
        try:
            # macOS exposes /var as the documented alias of /private/var.
            # It is not a caller-controlled hop; preserve it while rejecting
            # every subsequent supplied component.
            if current.is_symlink() and current != Path("/var"):
                return True
        except OSError:
            return True
    return False


def regular_file(path: Path) -> Path:
    """Return a non-symlink file whose supplied path has no symlink hop."""
    return canonical_existing(str(path), directory=False)


@contextmanager
def registry_lock() -> Any:
    """Serialize lifecycle mutations with an owner-only local lock file."""
    lock_path = home() / "build-registry-v1.lock"
    lock_path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    os.chmod(lock_path.parent, 0o700)
    descriptor = os.open(lock_path, os.O_CREAT | os.O_RDWR, 0o600)
    try:
        os.fchmod(descriptor, 0o600)
        fcntl.flock(descriptor, fcntl.LOCK_EX)
        yield
    finally:
        fcntl.flock(descriptor, fcntl.LOCK_UN)
        os.close(descriptor)


def safe_source_id(value: str) -> str:
    if not value or len(value) > 80 or any(character in value for character in "/\\\n\r\t"):
        raise ValueError("source-id must be a non-path label of at most 80 characters")
    return value
