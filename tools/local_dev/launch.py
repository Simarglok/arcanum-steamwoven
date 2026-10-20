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
# a role -> (relative path inside the installed game root, pinned bytes, pinned SHA-256); directories carry None.
# ARC-DEV-015: the bytes and SHA-256 are the pinned Crash Site profiles' identities. Only the pinned modes (every mode
# but map mode) require them; map mode needs its roles present (UNIVERSAL_ROLES), whatever their identity.
GAME_ROLES: dict[str, tuple[str, int | None, str | None]] = {
    "module": ("modules/Arcanum.dat", 355938305, "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f"),
    "actorResource": ("arcanum1.dat", 447470816, "6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576"),
    "terrainResource": ("arcanum2.dat", 147991122, "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d"),
    "areaEffects": ("arcanum3.dat", 9605011, "cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499"),
    "areaPatch": ("modules/Arcanum.PATCH0", 18804, "d9a3313f71d78115dcac489296bf3d8a5801dd55b20267bd6017b7a898364bce"),
    "areaMobile": ("modules/Arcanum/maps/qrmvs-420-1zjcnpgn", 1823280, "a548f611ff5de9842bba1f700f6614c0b9651cf3ab640a23d19efc7626dde6a5"),
    "caveMobile": ("modules/Arcanum/maps/rknp rivh uhngp", 6698, "aa29827de32c5cb6bf7aa03ea049f9f01d7698b003097172cd26ab80c9658318"),
    "prototypes": ("data/proto", None, None),
    # ARC-RENDER-017: the installed game folder itself; map mode reads its module content stack.
    "gameRoot": ("", None, None),
}
# Viewer flag of each role, and the roles each terrain mode requires (sourceId is always required too).
ROLE_FLAGS = {"module": "--module", "terrainResource": "--resource", "prototypes": "--prototypes", "actorResource": "--actor-resource",
              "areaMobile": "--area-mobile", "areaEffects": "--area-effects", "areaPatch": "--area-patch",
              "caveMobile": "--area-cave-mobile", "gameRoot": "--game-root"}
MODE_ROLES: dict[str, tuple[str, ...]] = {
    "terrain-only": ("module", "terrainResource"),
    "combined": ("module", "terrainResource", "prototypes"),
    "actor-diagnostic": ("module", "terrainResource", "prototypes", "actorResource"),
    "world-diagnostic": ("module", "terrainResource", "prototypes", "actorResource"),
    "crash-site-area": ("module", "terrainResource", "prototypes", "actorResource", "areaMobile", "areaEffects", "areaPatch"),
}
# Roles only builds of one exact task understand: older AREA builds reject the cave flag, so it is passed only to
# an ARC-WORLD-009 or later ARC-RENDER-008 build (the task named by the launched manifest), never to the mode as a whole.
TASK_ROLES: dict[str, tuple[str, ...]] = {"ARC-WORLD-009": ("caveMobile",), "ARC-RENDER-008": ("caveMobile",)}
TASK_MODES: dict[str, str] = {"ARC-WORLD-009": "crash-site-area", "ARC-RENDER-008": "crash-site-area"}
# ARC-RENDER-017: `crash-site-area --area-map NAME` shows one whole interior; its inputs come from the game folder's
# content stack, so it takes these roles (no prototype folder, Crash Site mobile, effects, patch or cave).
MAP_ROLES: tuple[str, ...] = ("module", "terrainResource", "actorResource", "gameRoot")
# ARC-DEV-015: the roles setup requires (map mode's); every other role is optional and pinned.
UNIVERSAL_ROLES: tuple[str, ...] = MAP_ROLES
# ARC-DEV-015: the terrain export task of universal builds: exported with the synthetic gates only (no pinned Crash Site
# run), launched in map mode only.
UNIVERSAL_TASK = "ARC-DEV-015"
MAX_MAP_NAME = 128
# ARC-WORLD-027: `--hero NAME` starts the AREA session as one original pre-made hero (main-menu order, basic prototypes
# 16067..16078). Keep in step with `AreaStartingHero.Premade` in tools/terrain-viewer/Core/AreaStartingHero.cs
# (tools/local_dev/test_launcher_sync.py fails on any drift). Only AREA builds exported with ARC-WORLD-027 accept it.
PREMADE_HEROES: tuple[str, ...] = ("halfling-male", "gnome-male", "half-elf-male", "half-ogre-male", "elf-male", "human-male",
                                   "half-orc-male", "dwarf-male", "human-female", "elf-female", "half-orc-female", "half-elf-female")

# ARC-DEV-007: `./arcanum oracle` runs the function oracle (tools/original-oracle) on the installed executable, then the
# OriginalExe tests that compare Steamwoven with its outputs. Function address -> its OriginalExe test method; keep in step
# with tools/original-oracle/oracle_functions.py FUNCTIONS and the test classes (tools/local_dev/test_launcher_sync.py).
ORIGINAL_EXE_NAME = "Arcanum.exe"
ORIGINAL_EXE_SHA256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52"
ORACLE_TESTS = "tests/Arcanum.Simulation.Tests/Arcanum.Simulation.Tests.csproj"
ORACLE_TEST_CLASSES = ("tests/Arcanum.Simulation.Tests/Character/ExperienceOriginalExeTests.cs",
                       "tests/Arcanum.Simulation.Tests/Skills/SkillOriginalExeTests.cs")
# ARC-DEV-008: where `./arcanum oracle` has the tests write their receipts (checked against docs/verification/functions.csv).
ORACLE_RECEIPTS = "docs/verification/receipts"
ORACLE_FUNCTIONS: dict[str, str] = {
    "0x4A68B0": "ToNextLevelMatchesTheOriginal",
    "0x4A6900": "ProgressMatchesTheOriginal",
    "0x4A6980": "BonusPointsMatchTheOriginal",
    "0x4C5EB0": "SkillLevelMatchesTheOriginal",
    "0x4C6170": "TrainingSetMatchesTheOriginal",
}

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


def hero_arguments(hero: str | None) -> list[str]:
    """ARC-WORLD-027: the optional `--hero NAME` pair; None keeps the diagnostic PC profile."""
    if hero is None:
        return []
    if hero not in PREMADE_HEROES:
        raise ValueError("unknown hero (choose one of: " + ", ".join(PREMADE_HEROES) + ")")
    return ["--hero", hero]


def map_arguments(inputs: dict[str, Any], name: str, hero: str | None = None) -> list[str]:
    """ARC-RENDER-017: the explicit argument set for one interior in crash-site-area map mode."""
    if not isinstance(name, str) or not name.strip() or len(name) > MAX_MAP_NAME or any(
            character in "/\\" or not character.isprintable() for character in name):
        raise ValueError("map name must be one maps/ folder name")
    if not isinstance(inputs, dict):
        raise ValueError("configure explicit terrain input roles before launch")
    missing = [key for key in MAP_ROLES + ("sourceId",) if not isinstance(inputs.get(key), str)]
    if missing:
        raise ValueError("configure explicit terrain input roles before launch (missing " + ", ".join(missing)
                         + "; run ./arcanum setup again)")
    arguments = ["--mode", "crash-site-area", "--module", _local(inputs, "module"), "--resource", _local(inputs, "terrainResource"),
                 "--source-id", _source_id(inputs), "--actor-resource", _local(inputs, "actorResource"),
                 "--game-root", _local(inputs, "gameRoot", directory=True), "--area-map", name, *hero_arguments(hero)]
    assert_ordinary(arguments)
    return arguments


def _source_id(inputs: dict[str, Any]) -> str:
    source_id = inputs["sourceId"]
    if not source_id or len(source_id) > 80 or any(character in source_id for character in "/\\\n\r\t"):
        raise ValueError("configured source-id is unsafe")
    return source_id


def _local(inputs: dict[str, Any], role: str, *, directory: bool = False) -> str:
    path = Path(inputs[role]).expanduser().absolute()
    if not (path.is_dir() if directory else path.is_file()) or path.is_symlink() or config._has_symlink_component(path):
        raise ValueError("configured terrain input is unavailable or unsafe")
    return str(path.resolve(strict=True))


def verified_roles(conf: dict[str, Any]) -> list[str] | None:
    """ARC-DEV-015: the roles setup's installation record verified, None when the configuration has no record (older or
    explicit inputs). A record of any other shape verifies nothing, so a damaged record fails closed."""
    if "installation" not in conf:
        return None
    installation = conf["installation"]
    verified = installation.get("pinnedVerified") if isinstance(installation, dict) else None
    if not isinstance(verified, list) or not all(isinstance(role, str) for role in verified):
        return []
    return verified


def pinned_roles(mode: str, task: str | None = None) -> tuple[str, ...]:
    """ARC-DEV-015: the roles of a pinned mode whose pinned identity it requires (files with a pinned size and SHA-256)."""
    roles = MODE_ROLES[mode] + (TASK_ROLES.get(task, ()) if TASK_MODES.get(task) == mode else ())
    return tuple(role for role in roles if GAME_ROLES[role][1] is not None)


def terrain_arguments(inputs: dict[str, Any], mode: str, overrides: dict[str, Any] | None = None, *, task: str | None = None,
                      hero: str | None = None, verified: Any = None) -> list[str]:
    """Build the complete, explicit ordinary terrain diagnostic argument set.

    Inputs are revalidated on every launch because local configuration is not a
    trust boundary.  The actor values are the documented diagnostic defaults,
    never recovered world state.  ARC-DEV-015: `verified` is the roles whose pinned identity setup confirmed (the
    configuration's installation record); when given, every pinned role of the mode must be among them.  None keeps a
    configuration written before the record existed, whose inputs were all verified when written.
    """
    if mode not in MODE_ROLES:
        raise ValueError("unsupported terrain diagnostic mode")
    if hero is not None and mode != "crash-site-area":
        raise ValueError("--hero applies to crash-site-area only")
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
    if verified is not None:
        unverified = [role for role in pinned_roles(mode, task if isinstance(task, str) else None) if role not in verified]
        if unverified:
            raise ValueError("this mode needs the pinned Crash Site inputs; these differ from the pinned identity: " + ", ".join(unverified)
                             + " (map mode works with any installation)")

    def local_file(role: str, *, directory: bool = False) -> str:
        return _local(inputs, role, directory=directory)

    source_id = _source_id(inputs)
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
    arguments.extend(hero_arguments(hero))
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


def discover_inputs(game_root: Path) -> tuple[dict[str, str], list[str], dict[str, Any]]:
    """Map every GAME_ROLES role present inside an installed game root, and record what was found.

    ARC-DEV-015: a role is found when its file is regular and link-free (the prototype directory must hold .pro
    files, the game root a modules folder); its size and SHA-256 are recorded as evidence, and a pinned role is
    "pinned" when they equal the pinned identity. Returns (role -> canonical path, missing roles, installation
    record); nothing is guessed and nothing is required to match.
    """
    root = config.canonical_existing(str(game_root), directory=True)
    found: dict[str, str] = {}
    missing: list[str] = []
    roles: dict[str, dict[str, Any]] = {}
    for role, (relative, size, sha256) in GAME_ROLES.items():
        candidate = (root / relative).absolute()
        if candidate.is_symlink() or config._has_symlink_component(candidate):
            missing.append(role); continue
        if role == "gameRoot":
            ok = candidate.is_dir() and (candidate / "modules").is_dir()
            record: dict[str, Any] = {"path": relative}
        elif size is None:
            files = sorted(candidate.glob("*.pro")) if candidate.is_dir() else []
            ok = bool(files)
            record = {"path": relative, "files": len(files)}
        else:
            ok = candidate.is_file()
            record = {"path": relative}
            if ok:
                measured, digest = candidate.stat().st_size, manifests.sha256(candidate)
                record.update(bytes=measured, sha256=digest, pinned=measured == size and digest == sha256)
        if ok:
            found[role] = str(candidate.resolve(strict=True))
            roles[role] = record
        else:
            missing.append(role)
    verified = sorted(role for role, record in roles.items() if record.get("pinned") is True)
    installation = {"schema": "arcanum-installation-v1", "gameRoot": str(root), "roles": roles, "pinnedVerified": verified}
    return found, missing, installation


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
