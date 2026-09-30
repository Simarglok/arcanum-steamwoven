"""Private local configuration with atomic, owner-only JSON files."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import tempfile
import re
import stat
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


def write_atomic_bytes(path: Path, encoded: bytes) -> None:
    """Atomically replace an owner-only local file with already serialized bytes."""
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    os.chmod(path.parent, 0o700)
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


def restore_atomic_bytes(path: Path, encoded: bytes | None) -> None:
    """Restore a file snapshot, including an originally absent pointer."""
    if encoded is not None:
        write_atomic_bytes(path, encoded)
        return
    try:
        path.unlink()
    except FileNotFoundError:
        return
    directory = os.open(path.parent, os.O_RDONLY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def write_atomic(path: Path, value: dict[str, Any]) -> None:
    write_atomic_bytes(path, (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8"))


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


def candidate_path(conf: dict[str, Any], tool: str, candidate_id: str) -> Path:
    """Prove the complete configured tuple before any candidate mutation."""
    from .manifests import SUPPORTED_TOOLS
    if not isinstance(tool, str) or not isinstance(candidate_id, str):
        raise ValueError("candidate requires supported tool and string identity")
    parts = candidate_id.split(":")
    if len(parts) != 3:
        raise ValueError("candidate id must bind root, task, and candidate")
    root_id, task, commit = parts
    if (not re.fullmatch(r"[A-Za-z0-9_-]+", root_id) or task not in SUPPORTED_TOOLS.get(tool, ())
            or not re.fullmatch(r"[0-9a-f]{40}", commit)):
        raise ValueError("candidate requires supported tool/task and full lowercase-hex commit")
    roots = conf.get("artifactRoots", {})
    if not isinstance(roots, dict) or not isinstance(roots.get(root_id), str):
        raise ValueError("candidate root is not explicitly configured")
    root = canonical_existing(roots[root_id], directory=True)
    candidate = root / task / commit
    if _has_symlink_component(candidate):
        raise ValueError("candidate path must not traverse a symlink")
    for node in (candidate.parent, candidate):
        if node.exists() and not node.is_dir():
            raise ValueError("candidate path must be a directory")
    return candidate


def _validate_lock_stat(node: os.stat_result) -> None:
    if not stat.S_ISREG(node.st_mode) or node.st_nlink != 1 or node.st_uid != os.geteuid():
        raise ValueError("lock must be an owned, non-linked regular file")


def validate_lock_file(path: Path) -> None:
    """Read-only admission check, including dangling links and special nodes."""
    if _has_symlink_component(path.absolute()):
        raise ValueError("lock path must not traverse a symlink")
    _validate_lock_stat(path.lstat())


def open_lock_file(path: Path) -> int:
    """Open without following any caller-controlled hop or mutating a target.

    Directory-relative opens keep every ancestor pinned while creating missing
    private directories. No chmod occurs until the caller holds the lock.
    """
    path = path.expanduser().absolute()
    if ".." in path.parts or _has_symlink_component(path):
        raise ValueError("lock path must not traverse a symlink")
    # Admit only macOS's documented /var alias, as canonical_existing does.
    if path.parts[:2] == ("/", "var"):
        path = Path("/private/var").joinpath(*path.parts[2:])
    parent = os.open(path.anchor, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    descriptor = None
    try:
        for component in path.parts[1:-1]:
            try:
                child = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=parent)
            except FileNotFoundError:
                try:
                    os.mkdir(component, 0o700, dir_fd=parent)
                except FileExistsError:
                    pass
                child = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=parent)
            os.close(parent)
            parent = child
        try:
            before = os.stat(path.name, dir_fd=parent, follow_symlinks=False)
        except FileNotFoundError:
            before = None
        if before is not None:
            _validate_lock_stat(before)
        descriptor = os.open(path.name, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW | os.O_NONBLOCK,
                             0o600, dir_fd=parent)
        opened = os.fstat(descriptor)
        _validate_lock_stat(opened)
        current = os.stat(path.name, dir_fd=parent, follow_symlinks=False)
        _validate_lock_stat(current)
        if ((opened.st_dev, opened.st_ino) != (current.st_dev, current.st_ino)
                or (before is not None and (opened.st_dev, opened.st_ino) != (before.st_dev, before.st_ino))):
            raise ValueError("lock identity changed during acquisition")
        return descriptor
    except BaseException:
        if descriptor is not None:
            os.close(descriptor)
        raise
    finally:
        os.close(parent)


@contextmanager
def registry_lock() -> Any:
    """Serialize lifecycle mutations with an owner-only local lock file."""
    lock_path = home() / "build-registry-v1.lock"
    descriptor = open_lock_file(lock_path)
    try:
        fcntl.flock(descriptor, fcntl.LOCK_EX)
        os.fchmod(descriptor, 0o600)
        yield
    finally:
        fcntl.flock(descriptor, fcntl.LOCK_UN)
        os.close(descriptor)


def safe_source_id(value: str) -> str:
    if not value or len(value) > 80 or any(character in value for character in "/\\\n\r\t"):
        raise ValueError("source-id must be a non-path label of at most 80 characters")
    return value
