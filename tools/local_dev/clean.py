"""ARC-DEV-028: which local builds and outputs are protected and which are disposable.

The rule is docs/workflow/build-retention.md. The report is read-only; `apply` removes only the `.app` payloads of
registered builds that a newer current build of the same task supersedes (or that are already retired), through the
retention plan/apply path, which rechecks every protection under the registry lock and the launcher/export leases.
ARC-DEV-055: `apply` also removes the outdated player packages of `./arcanum package` (each platform's previous/ and an
interrupted install's leftover); the current package of each platform always stays.
Everything else (unregistered or unmanaged builds, worktrees, caches) is reported for the owner, never removed here.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import stat
import subprocess
from typing import Any

from . import config, retention
from .manifests import SUPPORTED_TOOLS, resolve_pointer
from .package import PLATFORMS, Platform, remove

MARKER = "README-DO-NOT-DELETE.md"
MARKER_TEXT = """# Do not delete this folder by hand

The launcher (`./arcanum area|world|map`) and every export need builds that live here: the build named by each task's
`current.json`/`current-v2.json` pointer, the launch selection, accepted and rollback builds, and the measured build
the export size estimate is taken from. Deleting this folder (or a build in it) in Finder or with `rm` breaks the
launcher and the next export; ARC-DEV-026 had to repair that once (2026-10-20).

See what is protected and what can go:

    ./arcanum clean            # dry run: every build with its class and reason, nothing is removed
    ./arcanum clean --apply    # removes only superseded or retired build payloads (manifests and evidence stay)
                               # and outdated player packages (previous/); the current packages stay

The rule: docs/workflow/build-retention.md in the repository. This file is written by `./arcanum setup` and
`./arcanum clean`; it is not tracked in git.
"""
# Build outputs that a build or an editor import recreates by itself; reported next to their project file only.
REGENERABLE = {"bin": "*.csproj", "obj": "*.csproj", "TestResults": "*.csproj", ".godot": "project.godot"}
SOURCE_FOLDERS = ("src", "tests", "tools", "experiments")


def disk_bytes(path: Path) -> int | None:
    """Bytes of the regular files under path, links not followed (advisory; builds use the retention inventory)."""
    total = 0
    try:
        for folder, _, files in os.walk(path, followlinks=False, onerror=lambda error: None):
            for name in files:
                node = os.lstat(os.path.join(folder, name))
                total += node.st_size if stat.S_ISREG(node.st_mode) else 0
    except OSError:
        return None
    return total


def write_marker(artifacts: Path) -> bool:
    """Best effort: write the local do-not-delete marker into an existing artifacts folder; never creates the folder.
    A link, a hard-linked or special node, or any read/write failure leaves it as it is."""
    path = artifacts / MARKER
    try:
        if not artifacts.is_dir() or artifacts.is_symlink():
            return False
        if path.exists() or path.is_symlink():
            node = os.lstat(path)
            if not stat.S_ISREG(node.st_mode) or node.st_nlink != 1:
                return False
            if path.read_bytes() == MARKER_TEXT.encode("utf-8"):
                return True
        temporary = artifacts / f".{MARKER}.tmp"
        temporary.unlink(missing_ok=True)
        temporary.write_bytes(MARKER_TEXT.encode("utf-8"))
        os.replace(temporary, path)
        return True
    except (OSError, ValueError):
        return False


def _currents(conf: dict[str, Any]) -> dict[tuple[str, str], set[str]]:
    """(root, task) -> candidate ids named by that task's valid pointers; unsafe pointers are left to the protection."""
    found: dict[tuple[str, str], set[str]] = {}
    roots = conf.get("artifactRoots", {})
    for root_id, text in sorted(roots.items() if isinstance(roots, dict) else ()):
        try:
            root = config.canonical_existing(text, directory=True)
        except (OSError, ValueError):
            continue
        for tool, tasks in SUPPORTED_TOOLS.items():
            for task in tasks:
                for name in ("current.json", "current-v2.json"):
                    pointer = root / task / name
                    try:
                        if config.optional_authority_bytes(pointer) is None:
                            continue
                        manifest, _, _ = resolve_pointer(root, pointer, tool, root_id=root_id)
                        relative = manifest.relative_to(root)
                    except (OSError, ValueError):
                        continue
                    if len(relative.parts) == 3 and relative.parts[0] == task:
                        found.setdefault((root_id, task), set()).add(f"{root_id}:{task}:{relative.parts[1]}")
    return found


def _verifies(conf: dict[str, Any], registered: dict[str, Any], candidate_id: str) -> bool:
    tool = registered[candidate_id].get("tool")
    try:
        retention._verified_output(conf, tool, candidate_id, registered[candidate_id].get("manifestSha256"))
        return True
    except (OSError, ValueError, KeyError, TypeError):
        return False


def builds(conf: dict[str, Any], registry: dict[str, Any]) -> list[dict[str, Any]]:
    """Every candidate under the configured roots with its class: protected, keep, disposable or pruned."""
    protection = retention.protection_snapshot(conf, registry)
    protected = set(protection["protected"])
    currents = _currents(conf)
    registered = registry.get("builds", {}) if isinstance(registry.get("builds"), dict) else {}
    roles: dict[str, list[str]] = {}
    for (_, task), values in currents.items():
        for value in values:
            roles.setdefault(value, []).append("current build of " + task)
    for source, field, label in ((conf, "launchSelection", "launch selection"), (registry, "accepted", "accepted"),
                                 (registry, "rollback", "rollback"), (registry, "measurements", "export measurement")):
        values = source.get(field)
        for tool, value in values.items() if isinstance(values, dict) else ():
            value = value.get("candidateId") if isinstance(value, dict) else value
            roles.setdefault(str(value), []).append(f"{label} of {tool}")
    verified: dict[str, bool] = {}  # a current payload is hashed once, not once per build of its task
    results = []
    for entry in retention.inventory(conf, registry, show_paths=True):
        candidate_id = entry["candidateId"]
        if entry["candidate"] == ".DS_Store":
            continue
        if entry["candidate"] in ("current.json", "current-v2.json"):
            results.append({"candidateId": candidate_id, "rootId": entry["rootId"], "task": entry["task"], "bytes": None,
                            "payloadBytes": None, "lifecycle": None, "path": entry.get("path"), "class": "protected",
                            "reason": "current pointer: the launcher and the export estimate find their build through it",
                            "action": None})
            continue
        state = registered.get(candidate_id) if candidate_id else None
        lifecycle = state.get("lifecycle") if isinstance(state, dict) else None
        path = Path(entry["path"]) if "path" in entry else None
        item = {"candidateId": candidate_id, "rootId": entry["rootId"], "task": entry["task"], "bytes": entry["bytes"],
                "payloadBytes": entry.get("payloadBytes"), "lifecycle": lifecycle, "path": entry.get("path")}
        # Superseded means a current build of the same task that was registered later; a build registered after the
        # current one (a publication that never moved the pointer) or without a registration time is kept.
        # The superseding current must itself verify (manifest, evidence and every payload file), or the export would be
        # left with no measurable build once the older one is pruned.
        mine = state.get("registeredAt") if isinstance(state, dict) else None
        later = [value for value in currents.get((entry["rootId"], entry["task"]), set()) - {candidate_id}
                 if isinstance(mine, str) and isinstance(registered.get(value), dict)
                 and isinstance(registered[value].get("registeredAt"), str) and registered[value]["registeredAt"] > mine]
        for value in later:
            if value not in verified:
                verified[value] = _verifies(conf, registered, value)
        newer = sorted(value for value in later if verified[value])
        if candidate_id in protected:
            item.update({"class": "protected", "reason": "; ".join(roles.get(candidate_id, ["current, selected, accepted, rollback or measurement"])),
                         "action": None})
        elif entry["eligible"]:
            item.update({"class": "disposable", "reason": "retired", "action": "prune"})
        elif path is not None and (path / "payload-pruned-v1.json").is_file():
            item.update({"class": "pruned", "reason": "payload already removed; manifest and evidence kept", "action": None})
        elif entry["reason"] == "KEEP: lifecycle exported" and newer and "payloadRelativePath" in entry:
            item.update({"class": "disposable", "reason": "superseded by current " + ", ".join(newer), "action": "retire-and-prune"})
        else:
            reason = entry["reason"].removeprefix("KEEP: ").removeprefix("KEEP/SKIP: ")
            if lifecycle == "exported" and later and not newer:
                reason = "the later current build of its task does not verify; kept as the export's measurable build"
            elif lifecycle == "exported" and not newer:
                reason = "lifecycle exported and no later current build of its task in this root supersedes it"
            item.update({"class": "keep", "reason": reason, "action": None})
        results.append(item)
    return results


def _owned_homes(checkout: Path) -> dict[Path, str]:
    """Artifact roots configured by an owned ARCANUM_LOCAL_HOME kept under local/ (for example local/gpad005-pr-home),
    mapped to that home's absolute path (a relative root is taken relative to the home)."""
    roots: dict[Path, str] = {}
    local = checkout / "local"
    for path in sorted(local.glob("*/config-v1.json")) if local.is_dir() else ():
        try:
            values = json.loads(path.read_text(encoding="utf-8")).get("artifactRoots", {})
        except (OSError, ValueError, AttributeError):
            continue
        for text in values.values() if isinstance(values, dict) else ():
            if isinstance(text, str):
                roots[(path.parent / text).resolve()] = str(path.parent.resolve())
    return roots


def _within(path: Path, other: Path) -> bool:
    return path == other or other in path.parents or path in other.parents


# ARC-DEV-047: the player builds of `./arcanum package` (docs/workflow/packaging.md), classified one by one in `packages`
# (ARC-DEV-055); only a link or a file in place of the folder is reported here.
PACKAGES = "packages"
PACKAGES_REASON = "artifacts/packages is not the folder ./arcanum package writes (a link or a file); owner decides"
PLATFORM_NAMES = {"mac": "macOS", "win": "Windows"}
PACKAGE_BUILDER = ("local_dev/package.py", "local_dev.package")  # the builder run as a script or as a module
PACKAGE_FILES = ("BUILD.txt", "README.md", ".DS_Store")


def _build_line(folder: Path) -> str:
    """'main <first 10 digits>, <date>' from the BUILD.txt that ./arcanum package writes beside a package."""
    try:
        fields = dict(line.split(" ", 1) for line in (folder / "BUILD.txt").read_text(encoding="utf-8").splitlines() if " " in line)
    except (OSError, ValueError):
        return "no readable BUILD.txt"
    return f"main {fields.get('commit', '?')[:10]}, {fields.get('date', '?')}"


def _bytes(path: Path) -> int | None:
    try:
        node = os.lstat(path)
    except OSError:
        return None
    return disk_bytes(path) if stat.S_ISDIR(node.st_mode) else node.st_size if stat.S_ISREG(node.st_mode) else None


def _platform_packages(folder: Path, platform: Platform) -> list[dict[str, Any]]:
    name = PLATFORM_NAMES[platform.key]
    current = folder / platform.installed
    present = not current.is_symlink() and (current.is_dir() if platform.key == "mac" else current.is_file())
    results = []
    for child in sorted(folder.iterdir()):
        if child.name in PACKAGE_FILES:
            continue
        if child == current:
            item = {"class": "keep", "reason": f"current {name} package ({_build_line(folder)}) with its BUILD.txt and README.md; never removed"
                    if present else f"not a {name} package in the expected form; owner decides"}
        elif child.is_symlink():
            item = {"class": "keep", "reason": "a link; owner decides"}
        elif child.name == "previous" and child.is_dir():
            # A packaged game writes its bug reports beside its app (ARC-DEV-051), so an app started from previous/ may have
            # left the player's reports there: only the build itself is disposable.
            extra = sorted(entry.name for entry in child.iterdir() if entry.name not in (platform.installed, *PACKAGE_FILES))
            item = ({"class": "keep", "reason": f"previous {name} package that also holds {', '.join(extra[:3])}"
                     f"{'…' if len(extra) > 3 else ''} (for example bug reports of that build); owner decides"} if extra
                    else {"class": "disposable", "reason": f"previous {name} package ({_build_line(child)}), superseded by the current one"}
                    if present else {"class": "keep", "reason": f"previous {name} package with no current package beside it; owner decides"})
        elif child.name == f".incoming-{platform.installed}":
            item = ({"class": "disposable", "reason": "left by an interrupted ./arcanum package install; the current package is untouched"}
                    if present else {"class": "keep", "reason": f"interrupted install with no current {name} package; owner decides"})
        else:
            item = {"class": "keep", "reason": "not written by ./arcanum package; owner decides"}
        results.append({"path": str(child), "bytes": _bytes(child), **item})
    return results


def packages(checkout: Path) -> list[dict[str, Any]]:
    """ARC-DEV-055: artifacts/packages per platform. The current package (with BUILD.txt and README.md) and the last run's
    step logs stay; previous/ (holding only the build) and an interrupted install's .incoming-* are disposable only while
    the current package is there; anything else, and every link, is kept for the owner. A folder that cannot be read is
    reported as KEEP."""
    artifacts = checkout / "artifacts"
    base = artifacts / PACKAGES
    if artifacts.is_symlink() or not base.is_dir() or base.is_symlink():
        return []
    folders = {platform.folder: platform for platform in PLATFORMS.values()}
    results = []
    try:
        children = sorted(base.iterdir())
    except OSError as error:
        return [{"path": str(base), "bytes": None, "class": "keep", "reason": f"cannot be read ({error.strerror}); owner decides"}]
    for child in children:
        if child.name == ".DS_Store":
            continue
        if child.name in folders and child.is_dir() and not child.is_symlink():
            try:
                results += _platform_packages(child, folders[child.name])
            except OSError as error:
                results.append({"path": str(child), "bytes": None, "class": "keep", "reason": f"cannot be read ({error.strerror}); owner decides"})
            continue
        reason = ("step logs of the last ./arcanum package run per platform and of the last ./arcanum daily run; the next run replaces them"
                  if child.name == "logs" and child.is_dir() and not child.is_symlink() else "not written by ./arcanum package; owner decides")
        results.append({"path": str(child), "bytes": _bytes(child), "class": "keep", "reason": reason})
    return results


def _processes() -> list[str]:
    """Command lines of every process (ps), to see a package run or a packaged app started from the folder."""
    return subprocess.run(["ps", "-axww", "-o", "command="], check=True, capture_output=True, text=True).stdout.splitlines()


def apply_packages(checkout: Path) -> dict[str, Any]:
    """Remove the disposable packages. Refuses all while a package run (local_dev/package.py) is running, and skips a
    package whose app runs by a command line under its path; each path is classified again right before its removal,
    and a failed removal stops with what was already removed."""
    disposable = [item for item in packages(checkout) if item["class"] == "disposable"]
    removed, freed, skipped = [], 0, []
    if not disposable:
        return {"status": "nothing-to-remove", "removed": removed, "freedBytes": freed}
    try:
        running = _processes()
    except (OSError, subprocess.CalledProcessError) as error:
        return {"status": f"stopped: cannot list processes ({error})", "removed": removed, "freedBytes": freed}
    if any(builder in line for line in running for builder in PACKAGE_BUILDER):
        return {"status": "stopped: ./arcanum package is running; run clean --apply again after it", "removed": removed, "freedBytes": freed}
    for item in disposable:
        path = Path(item["path"])
        if any(f"{path}/" in line for line in running):
            skipped.append(path.name)
            continue
        try:
            again = next((other for other in _platform_packages(path.parent, _platform(path.parent)) if other["path"] == item["path"]), None)
            if again is None or again["class"] != "disposable" or path.is_symlink():
                continue
            remove(path)
        except OSError as error:
            return {"status": f"stopped: cannot remove {path} ({error})", "removed": removed, "freedBytes": freed}
        removed.append(str(path))
        freed += item["bytes"] or 0
    status = f"skipped running {', '.join(skipped)}" if skipped else "done" if removed else "nothing-to-remove"
    return {"status": status, "removed": removed, "freedBytes": freed}


def _platform(folder: Path) -> Platform:
    return next(platform for platform in PLATFORMS.values() if platform.folder == folder.name)


def unmanaged(conf: dict[str, Any], checkout: Path) -> list[dict[str, Any]]:
    """Entries of artifacts/ and task folders of configured roots that the configured roots' inventory does not cover."""
    roots = conf.get("artifactRoots", {})
    configured = [Path(text).resolve() for text in (roots.values() if isinstance(roots, dict) else ()) if isinstance(text, str)]
    owned = _owned_homes(checkout)
    known_tasks = {task for tasks in SUPPORTED_TOOLS.values() for task in tasks}
    results = []
    artifacts = checkout / "artifacts"
    for child in sorted(artifacts.iterdir()) if artifacts.is_dir() and not artifacts.is_symlink() else ():
        if child.name in (MARKER, f".{MARKER}.tmp", ".DS_Store"):
            continue
        if child.resolve() in configured and child.is_dir() and not child.is_symlink():
            results += [{"path": str(task), "bytes": disk_bytes(task), "class": "keep",
                         "reason": "task folder the launcher does not know; owner decides"}
                        for task in sorted(child.iterdir()) if task.is_dir() and task.name not in known_tasks]
            continue
        if any(_within(child.resolve(), root) for root in configured):
            continue
        if child.name == PACKAGES and child.is_dir() and not child.is_symlink():
            continue  # classified by packages()
        home = next((name for root, name in owned.items() if _within(child.resolve(), root)), None)
        reason = (f"artifact root of the owned home {home}; run ARCANUM_LOCAL_HOME={home} ./arcanum clean" if home
                  else PACKAGES_REASON if child.name == PACKAGES
                  else "not a configured artifact root (older layout or a lab output); owner decides")
        results.append({"path": str(child), "bytes": disk_bytes(child), "class": "keep", "reason": reason})
    return results


def _git(*arguments: str) -> str | None:
    try:
        # --no-optional-locks: a report must not refresh (and lock) the index of a worktree a session works in.
        return subprocess.run(["git", "--no-optional-locks", *arguments], check=True, capture_output=True, text=True).stdout
    except (OSError, subprocess.CalledProcessError):
        return None


def worktrees(checkout: Path) -> list[dict[str, Any]]:
    """Claude Code worktrees (.claude/worktrees) and retired task-board worktrees (.worktrees); report only."""
    listing = _git("-C", str(checkout), "worktree", "list", "--porcelain") or ""
    results = []
    for block in listing.strip().split("\n\n") if listing.strip() else ():
        fields = dict(line.split(" ", 1) if " " in line else (line, "") for line in block.splitlines())
        path = Path(fields.get("worktree", ""))
        if path.parent == checkout / ".worktrees":
            results.append({"path": str(path), "bytes": None, "class": "keep",
                            "reason": "retired task-board worktree, kept as evidence (CLAUDE.md); only on the owner's word"})
            continue
        if path.parent != checkout / ".claude" / "worktrees":
            continue
        branch = fields.get("branch", "").removeprefix("refs/heads/") or "detached"
        merged = _git("-C", str(path), "merge-base", "--is-ancestor", "HEAD", "origin/main") is not None
        status = _git("-C", str(path), "status", "--porcelain", "--ignored")
        if status is None:
            reason, kind = "git status failed", "keep"
        else:
            lines = status.splitlines()
            changed = [line for line in lines if not line.startswith("!!")]
            ignored = [line[3:].rstrip("/") for line in lines if line.startswith("!!")]
            extra = [name for name in ignored if Path(name).name not in REGENERABLE and Path(name).name != "__pycache__"]
            if "locked" in fields:
                reason, kind = "locked worktree", "keep"
            elif not merged:
                reason, kind = f"branch {branch} is not in origin/main (as of the last fetch)", "keep"
            elif changed:
                reason, kind = f"{len(changed)} uncommitted or untracked paths", "keep"
            elif extra:
                reason, kind = "ignored local files besides build outputs: " + ", ".join(extra[:3]) + ("…" if len(extra) > 3 else ""), "keep"
            else:
                reason, kind = f"branch {branch} merged, clean; remove with `git worktree remove` once no thread works in it", "removable"
        results.append({"path": str(path), "bytes": disk_bytes(path), "class": kind, "reason": reason})
    return results


def caches(checkout: Path) -> list[dict[str, Any]]:
    """Build outputs of the main checkout that the next build or editor import recreates; report only."""
    results = []
    seen: set[Path] = set()
    for name, pattern in REGENERABLE.items():
        for folder in SOURCE_FOLDERS:
            base = checkout / folder
            for project in sorted(base.rglob(pattern)) if base.is_dir() else ():
                output = project.parent / name
                if output in seen or not output.is_dir() or output.is_symlink():
                    continue
                seen.add(output)
                results.append({"path": str(output), "bytes": disk_bytes(output), "class": "regenerable",
                                "reason": "rebuilt by the next build or import; delete only while no thread builds, exports or tests"})
    return results


def report(conf: dict[str, Any], registry: dict[str, Any], checkout: Path) -> dict[str, Any]:
    return {"schema": "arcanum-clean-report-v1", "home": str(config.home()), "builds": builds(conf, registry),
            "unmanaged": unmanaged(conf, checkout), "packages": packages(checkout), "worktrees": worktrees(checkout),
            "caches": caches(checkout)}


def apply(checkout: Path) -> dict[str, Any]:
    """Retire superseded registered builds and prune every retired payload through one retention plan, all under one
    registry lock. A running viewer of an affected tool refuses before anything changes; a failure part-way through
    the plan is reported with what was already retired and removed (the next report shows them as retired or pruned)."""
    with config.registry_lock():
        conf, registry = config.load_config(), config.load_registry()
        acting = [item for item in builds(conf, registry) if item["action"] is not None]
        for tool in sorted({retention._tool_for_task(item["task"]) for item in acting} - {None}):
            lease = retention._try_exclusive(config.home() / "locks" / f"{tool}.lock")
            if lease is None:
                raise ValueError(f"a {tool} viewer is running; close it and run clean --apply again")
            retention._release(lease)
        retired = [item["candidateId"] for item in acting if item["action"] == "retire-and-prune"]
        for candidate_id in retired:
            retention.retire(registry, candidate_id, conf=conf)
        if retired:
            config.save_registry(registry)
        conf, registry = config.load_config(), config.load_registry()
        plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
        if not plan["payloads"]:
            return {"retired": retired, "planId": None, "status": "nothing-to-remove", "removed": []}
        try:
            # The locked apply rechecks every binding, pointer, payload identity and lease before each removal.
            result = retention._apply_plan_locked(plan, conf, registry, accepted_id=plan["id"])
        except (OSError, ValueError) as error:
            pruned = [item["candidateId"] for item in plan["payloads"] if _pruned_by(Path(item["path"]), plan["id"])]
            return {"retired": retired, "planId": plan["id"], "status": f"stopped: {error}", "removed": pruned,
                    "freedBytes": sum(item["payloadIdentity"]["bytes"] for item in plan["payloads"] if item["candidateId"] in pruned)}
    freed = sum(item["payloadIdentity"]["bytes"] for item in plan["payloads"] if item["candidateId"] in result["removed"])
    return {"retired": retired, **result, "freedBytes": freed}


def _pruned_by(candidate: Path, plan_id: str) -> bool:
    try:
        encoded = config.optional_authority_bytes(candidate / "payload-pruned-v1.json")
        return encoded is not None and json.loads(encoded).get("planId") == plan_id
    except (OSError, ValueError, AttributeError):
        return False


def _size(value: int | None) -> str:
    if value is None:
        return "?"
    for unit in ("B", "KB", "MB", "GB"):
        if value < 1024 or unit == "GB":
            return f"{value:.0f} {unit}" if unit == "B" else f"{value:.1f} {unit}"
        value /= 1024
    return "?"


def render(result: dict[str, Any]) -> str:
    lines = [f"Builds of {result['home']} (dry run; nothing was removed):"]
    for item in result["builds"]:
        size = item["payloadBytes"] if item["class"] == "disposable" else item["bytes"]
        lines.append(f"  {item['class'].upper():<11} {_size(size):>9}  {item['candidateId']}: {item['reason']}")
    if not result["builds"]:
        lines.append("  none (no configured artifact root holds a build)")
    for title, key in (("Not managed by this home", "unmanaged"), ("Player packages (artifacts/packages)", "packages"),
                       ("Worktrees (report only)", "worktrees"),
                       ("Regenerable build outputs (report only)", "caches")):
        if result[key]:
            lines.append(f"{title}:")
            lines += [f"  {item['class'].upper():<11} {_size(item['bytes']):>9}  {item['path']}: {item['reason']}" for item in result[key]]
    disposable = sum(item["payloadBytes"] or 0 for item in result["builds"] if item["class"] == "disposable")
    outdated = sum(item["bytes"] or 0 for item in result["packages"] if item["class"] == "disposable")
    lines.append(f"`./arcanum clean --apply` would remove {_size(disposable)} of DISPOSABLE build payloads and "
                 f"{_size(outdated)} of DISPOSABLE (outdated) player packages; PROTECTED and KEEP builds, manifests, evidence "
                 "and the current packages stay. Never delete artifacts/ by hand "
                 "(docs/workflow/build-retention.md).")
    return "\n".join(lines)


def run(checkout: Path, *, do_apply: bool, as_json: bool) -> int:
    write_marker(checkout / "artifacts")
    if do_apply:
        # Packages first: they share no lock or lease with the builds, so a running viewer does not hold them back.
        pruned = apply_packages(checkout)
        try:
            result = {**apply(checkout), "packages": pruned}
        except ValueError as error:  # a running viewer refuses the builds before anything changes
            result = {"retired": [], "removed": [], "status": f"stopped: {error}", "packages": pruned}
        print(json.dumps(result, indent=2, sort_keys=True) if as_json else
              f"Retired {len(result['retired'])}, removed {len(result['removed'])} payload(s), "
              f"freed {_size(result.get('freedBytes', 0))} ({result['status']}). Player packages: removed "
              f"{len(pruned['removed'])}, freed {_size(pruned['freedBytes'])} ({pruned['status']}).")
        return 2 if result["status"].startswith("stopped") or pruned["status"].startswith("stopped") else 0
    result = report(config.load_config(), config.load_registry(), checkout)
    print(json.dumps(result, indent=2, sort_keys=True) if as_json else render(result))
    return 0
