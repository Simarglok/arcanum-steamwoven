"""Safe non-shell ordinary-launch preparation and duplicate protection."""
from __future__ import annotations

import fcntl
import os
from pathlib import Path
import subprocess
from typing import Any

from . import config, manifests

# Single source of the ordinary launcher's original-input roles. Keep it in step with the viewer's
# `TerrainViewerArguments` (tools/local_dev/test_launcher_sync.py fails on any drift):
# a role -> (relative path inside the installed game root, expected bytes, SHA-256); directories carry None.
GAME_ROLES: dict[str, tuple[str, int | None, str | None]] = {
    "module": ("modules/Arcanum.dat", 355938305, "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f"),
    "actorResource": ("arcanum1.dat", 447470816, "6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576"),
    "terrainResource": ("arcanum2.dat", 147991122, "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d"),
    "areaEffects": ("arcanum3.dat", 9605011, "cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499"),
    "areaPatch": ("modules/Arcanum.PATCH0", 18804, "d9a3313f71d78115dcac489296bf3d8a5801dd55b20267bd6017b7a898364bce"),
    "areaMobile": ("modules/Arcanum/maps/qrmvs-420-1zjcnpgn", 1823280, "a548f611ff5de9842bba1f700f6614c0b9651cf3ab640a23d19efc7626dde6a5"),
    "caveMobile": ("modules/Arcanum/maps/rknp rivh uhngp", 6698, "aa29827de32c5cb6bf7aa03ea049f9f01d7698b003097172cd26ab80c9658318"),
    "prototypes": ("data/proto", None, None),
}
# Viewer flag of each role, and the roles each terrain mode requires (sourceId is always required too).
ROLE_FLAGS = {"module": "--module", "terrainResource": "--resource", "prototypes": "--prototypes", "actorResource": "--actor-resource",
              "areaMobile": "--area-mobile", "areaEffects": "--area-effects", "areaPatch": "--area-patch",
              "caveMobile": "--area-cave-mobile"}
MODE_ROLES: dict[str, tuple[str, ...]] = {
    "terrain-only": ("module", "terrainResource"),
    "combined": ("module", "terrainResource", "prototypes"),
    "actor-diagnostic": ("module", "terrainResource", "prototypes", "actorResource"),
    "world-diagnostic": ("module", "terrainResource", "prototypes", "actorResource"),
    "crash-site-area": ("module", "terrainResource", "prototypes", "actorResource", "areaMobile", "areaEffects", "areaPatch"),
}
# Roles only builds of one exact task understand: older AREA builds reject the cave flag, so it is passed only to
# an ARC-WORLD-009 build (the task named by the launched manifest), never to the mode as a whole.
TASK_ROLES: dict[str, tuple[str, ...]] = {"ARC-WORLD-009": ("caveMobile",)}
TASK_MODES: dict[str, str] = {"ARC-WORLD-009": "crash-site-area"}

FORBIDDEN = ("--self-test", "--native-smoke", "--smoke", "--matrix", "--capture", "--screenshot", "--headless", "--fullscreen", "--automated", "--resize", "--input")


def assert_ordinary(arguments: list[str]) -> None:
    lowered = [argument.lower() for argument in arguments]
    if any(any(flag in argument for flag in FORBIDDEN) for argument in lowered):
        raise ValueError("ordinary launch refuses automation/test/capture flags")


def argv_from_manifest(manifest: dict[str, Any]) -> tuple[Path, list[str]]:
    app = manifest.get("app")
    if not isinstance(app, dict):
        raise ValueError("manifest lacks app")
    executable = app.get("executable") or app.get("executableRelativePath")
    if not isinstance(executable, str) or not executable:
        raise ValueError("manifest lacks executable")
    engine = app.get("engineArgs", [])
    user = app.get("ordinaryArgs", [])
    if not isinstance(engine, list) or not isinstance(user, list) or not all(isinstance(value, str) for value in engine + user):
        raise ValueError("manifest launch arguments are malformed")
    assert_ordinary(engine + user)
    return Path(executable), [*engine, "--", *user]


ACTOR_DEFAULTS: dict[str, str] = {
    "actor_map_x": "0", "actor_map_y": "0", "actor_camera_origin_x": "0", "actor_camera_origin_y": "0",
    "actor_offset_x": "0", "actor_offset_y": "0", "actor_render_branch": "normal-uncached",
    "actor_scale_percent": "100", "actor_shrunk": "false", "actor_action": "idle",
    "actor_facing": "0", "actor_frame": "0",
}


def _actor_preview(overrides: dict[str, Any] | None) -> dict[str, str]:
    values = dict(ACTOR_DEFAULTS)
    for key, value in (overrides or {}).items():
        if value is not None:
            values[key] = str(value)
    integer_keys = ("actor_map_x", "actor_map_y", "actor_camera_origin_x", "actor_camera_origin_y",
                    "actor_offset_x", "actor_offset_y", "actor_scale_percent", "actor_facing", "actor_frame")
    try:
        parsed = {key: int(values[key]) for key in integer_keys}
    except (KeyError, ValueError) as error:
        raise ValueError("actor diagnostic preview values must be integers") from error
    if values["actor_render_branch"] != "normal-uncached" or parsed["actor_scale_percent"] != 100 or values["actor_shrunk"] != "false":
        raise ValueError("actor diagnostic only supports the fixed normal-uncached, scale-100, non-shrunk profile")
    if values["actor_action"] not in {"idle", "walk"} or not 0 <= parsed["actor_facing"] <= 7 or parsed["actor_frame"] < 0:
        raise ValueError("actor diagnostic action, facing, or frame is outside the fixed profile")
    # The fixed source profile has 15 idle and 8 walk frames.  Rejecting a
    # syntactically nonnegative but unavailable frame here avoids handing the
    # viewer an invalid caller policy.
    if parsed["actor_frame"] >= (15 if values["actor_action"] == "idle" else 8):
        raise ValueError("actor diagnostic frame is invalid for the chosen action")
    return values


def terrain_arguments(inputs: dict[str, Any], mode: str, overrides: dict[str, Any] | None = None, *, task: str | None = None) -> list[str]:
    """Build the complete, explicit ordinary terrain diagnostic argument set.

    Inputs are revalidated on every launch because local configuration is not a
    trust boundary.  The actor values are the documented diagnostic defaults,
    never recovered world state.
    """
    if mode not in MODE_ROLES:
        raise ValueError("unsupported terrain diagnostic mode")
    if mode in {"world-diagnostic", "crash-site-area"} and any(value is not None for value in (overrides or {}).values()):
        raise ValueError("world diagnostic rejects actor preview overrides")
    task_roles = TASK_ROLES.get(task, ()) if isinstance(task, str) and TASK_MODES.get(task) == mode else ()
    required = MODE_ROLES[mode] + task_roles + ("sourceId",)
    if not isinstance(inputs, dict):
        raise ValueError("configure explicit terrain input roles before launch")
    missing = [key for key in required if not isinstance(inputs.get(key), str)]
    if missing:
        raise ValueError("configure explicit terrain input roles before launch (missing " + ", ".join(missing)
                         + "; run ./arcanum setup again)")

    def local_file(role: str, *, directory: bool = False) -> str:
        path = Path(inputs[role]).expanduser().absolute()
        if not (path.is_dir() if directory else path.is_file()) or path.is_symlink() or config._has_symlink_component(path):
            raise ValueError("configured terrain input is unavailable or unsafe")
        return str(path.resolve(strict=True))

    source_id = inputs["sourceId"]
    if not source_id or len(source_id) > 80 or any(character in source_id for character in "/\\\n\r\t"):
        raise ValueError("configured source-id is unsafe")
    arguments = ["--mode", mode, "--module", local_file("module"), "--resource", local_file("terrainResource"), "--source-id", source_id]
    if mode in {"combined", "actor-diagnostic", "world-diagnostic", "crash-site-area"}:
        arguments.extend(("--prototypes", local_file("prototypes", directory=True)))
    if mode in {"world-diagnostic", "crash-site-area"}:
        arguments.extend(("--actor-resource", local_file("actorResource")))
    if mode == "crash-site-area":
        for role in ("areaMobile", "areaEffects", "areaPatch"):
            arguments.extend((ROLE_FLAGS[role], local_file(role)))
    for role in task_roles:
        arguments.extend((ROLE_FLAGS[role], local_file(role)))
    if mode == "actor-diagnostic":
        actor = _actor_preview(overrides)
        arguments.extend((
            "--actor-resource", local_file("actorResource"),
            "--actor-map-x", actor["actor_map_x"], "--actor-map-y", actor["actor_map_y"],
            "--actor-camera-origin-x", actor["actor_camera_origin_x"], "--actor-camera-origin-y", actor["actor_camera_origin_y"],
            "--actor-offset-x", actor["actor_offset_x"], "--actor-offset-y", actor["actor_offset_y"],
            "--actor-render-branch", actor["actor_render_branch"], "--actor-scale-percent", actor["actor_scale_percent"],
            "--actor-shrunk", actor["actor_shrunk"], "--actor-action", actor["actor_action"], "--actor-facing", actor["actor_facing"], "--actor-frame", actor["actor_frame"],
        ))
    assert_ordinary(arguments)
    return arguments


def discover_inputs(game_root: Path) -> tuple[dict[str, str], list[str]]:
    """Map every GAME_ROLES role found with its exact identity inside an installed game root.

    Files must be regular, link-free and match bytes and SHA-256; the prototype directory must
    hold .pro files. Returns (role -> canonical path, missing roles); nothing is guessed.
    """
    root = config.canonical_existing(str(game_root), directory=True)
    found: dict[str, str] = {}
    missing: list[str] = []
    for role, (relative, size, sha256) in GAME_ROLES.items():
        candidate = (root / relative).absolute()
        if candidate.is_symlink() or config._has_symlink_component(candidate):
            missing.append(role); continue
        if size is None:
            ok = candidate.is_dir() and any(candidate.glob("*.pro"))
        else:
            ok = candidate.is_file() and candidate.stat().st_size == size and manifests.sha256(candidate) == sha256
        if ok:
            found[role] = str(candidate.resolve(strict=True))
        else:
            missing.append(role)
    return found, missing


def exact_executable_in_use(executable: Path) -> bool:
    """Use ps' executable column, preserving macOS bundle paths with spaces."""
    completed = subprocess.run(["ps", "-axo", "pid=,comm="], check=True, text=True, capture_output=True)
    target = str(executable.resolve())
    for line in completed.stdout.splitlines():
        fields = line.strip().split(maxsplit=1)
        if len(fields) != 2:
            continue
        executable_path = fields[1].strip()
        try:
            observed = str(Path(executable_path).resolve(strict=True))
        except OSError:
            continue
        if observed == target:
            return True
    return False


def lock_for(root: Path, tool: str) -> Any:
    if tool not in {"art", "terrain", "gamepad", "display"}:
        raise ValueError("unsupported launch lock tool")
    handle = os.fdopen(config.open_lock_file(root / "locks" / f"{tool}.lock"), "a+", encoding="utf-8")
    try:
        fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        os.fchmod(handle.fileno(), 0o600)
    except BlockingIOError as error:
        handle.close()
        raise ValueError("an ordinary launch for this tool is already active") from error
    except BaseException:
        handle.close()
        raise
    return handle


def start(executable: Path, arguments: list[str], *, allow_duplicate: bool, lock_root: Path, tool: str, dry_run: bool,
          show_paths: bool = False) -> dict[str, Any]:
    executable = executable.resolve(strict=True)
    if not executable.is_file() or executable.is_symlink():
        raise ValueError("manifest executable is not a safe regular file")
    assert_ordinary(arguments)
    if not allow_duplicate and exact_executable_in_use(executable):
        raise ValueError("exact configured executable is already in use; pass --allow-duplicate to opt in")
    argv = [str(executable), *arguments]
    if dry_run:
        result: dict[str, Any] = {"started": False, "tool": tool, "argumentCount": len(arguments)}
        if show_paths:
            result["argv"] = argv
        return result
    # Explicit opt-in deliberately bypasses both duplicate barriers.  It is
    # still shell-free and never affects another process.
    if allow_duplicate:
        process = subprocess.Popen(argv, close_fds=True, start_new_session=True)
        return {"started": True, "pid": process.pid, "tool": tool}
    handle = lock_for(lock_root, tool)
    try:
        # The child retains the advisory lock for its entire lifetime.  The
        # exact-executable check remains the cross-process fallback; neither
        # mechanism broad-matches or terminates a process.
        process = subprocess.Popen(argv, close_fds=True, pass_fds=(handle.fileno(),), start_new_session=True)
    except Exception:
        handle.close()
        raise
    handle.close()
    result = {"started": True, "pid": process.pid, "tool": tool}
    if show_paths:
        result["argv"] = argv
    return result
