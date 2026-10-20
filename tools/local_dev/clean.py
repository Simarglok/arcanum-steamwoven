"""ARC-DEV-028: which local builds and outputs are protected and which are disposable.

The rule is docs/workflow/build-retention.md. The report is read-only; `apply` removes only the `.app` payloads of
registered builds that a newer current build of the same task supersedes (or that are already retired), through the
retention plan/apply path, which rechecks every protection under the registry lock and the launcher/export leases.
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

MARKER = "README-DO-NOT-DELETE.md"
MARKER_TEXT = """# Do not delete this folder by hand

The launcher (`./arcanum area|world|map`) and every export need builds that live here: the build named by each task's
`current.json`/`current-v2.json` pointer, the launch selection, accepted and rollback builds, and the measured build
the export size estimate is taken from. Deleting this folder (or a build in it) in Finder or with `rm` breaks the
launcher and the next export; ARC-DEV-026 had to repair that once (2026-10-20).

See what is protected and what can go:

    ./arcanum clean            # dry run: every build with its class and reason, nothing is removed
    ./arcanum clean --apply    # removes only superseded or retired build payloads; manifests and evidence stay

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
    """Write the local do-not-delete marker into an existing artifacts folder; never creates the folder."""
    if not artifacts.is_dir() or artifacts.is_symlink():
        return False
    path = artifacts / MARKER
    if path.is_symlink() or (path.exists() and not path.is_file()):
        return False
    if not path.exists() or path.read_text(encoding="utf-8") != MARKER_TEXT:
        path.write_text(MARKER_TEXT, encoding="utf-8")
    return True


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
        mine = state.get("registeredAt") if isinstance(state, dict) else None
        newer = sorted(value for value in currents.get((entry["rootId"], entry["task"]), set()) - {candidate_id}
                       if isinstance(mine, str) and isinstance(registered.get(value), dict)
                       and isinstance(registered[value].get("registeredAt"), str) and registered[value]["registeredAt"] > mine)
        if candidate_id in protected:
            item.update({"class": "protected", "reason": "; ".join(roles.get(candidate_id, ["current, selected, accepted or rollback"])),
                         "action": None})
        elif entry["eligible"]:
            item.update({"class": "disposable", "reason": "retired", "action": "prune"})
        elif path is not None and (path / "payload-pruned-v1.json").is_file():
            item.update({"class": "pruned", "reason": "payload already removed; manifest and evidence kept", "action": None})
        elif entry["reason"] == "KEEP: lifecycle exported" and newer and "payloadRelativePath" in entry:
            item.update({"class": "disposable", "reason": "superseded by current " + ", ".join(newer), "action": "retire-and-prune"})
        else:
            reason = entry["reason"].removeprefix("KEEP: ").removeprefix("KEEP/SKIP: ")
            if lifecycle == "exported" and not newer:
                reason = "lifecycle exported and no later current build of its task supersedes it"
            item.update({"class": "keep", "reason": reason, "action": None})
        results.append(item)
    return results


def _owned_homes(checkout: Path) -> dict[Path, str]:
    """Artifact roots configured by an owned ARCANUM_LOCAL_HOME kept under local/ (for example local/gpad005-pr-home)."""
    roots: dict[Path, str] = {}
    local = checkout / "local"
    for path in sorted(local.glob("*/config-v1.json")) if local.is_dir() else ():
        try:
            values = json.loads(path.read_text(encoding="utf-8")).get("artifactRoots", {})
        except (OSError, ValueError, AttributeError):
            continue
        for text in values.values() if isinstance(values, dict) else ():
            if isinstance(text, str):
                roots[Path(text).resolve()] = str(path.parent.relative_to(checkout))
    return roots


def _within(path: Path, other: Path) -> bool:
    return path == other or other in path.parents or path in other.parents


def unmanaged(conf: dict[str, Any], checkout: Path) -> list[dict[str, Any]]:
    """Entries of artifacts/ and task folders of configured roots that the configured roots' inventory does not cover."""
    roots = conf.get("artifactRoots", {})
    configured = [Path(text).resolve() for text in (roots.values() if isinstance(roots, dict) else ()) if isinstance(text, str)]
    owned = _owned_homes(checkout)
    known_tasks = {task for tasks in SUPPORTED_TOOLS.values() for task in tasks}
    results = []
    artifacts = checkout / "artifacts"
    for child in sorted(artifacts.iterdir()) if artifacts.is_dir() and not artifacts.is_symlink() else ():
        if child.name in (MARKER, ".DS_Store"):
            continue
        if child.resolve() in configured and child.is_dir() and not child.is_symlink():
            results += [{"path": str(task), "bytes": disk_bytes(task), "class": "keep",
                         "reason": "task folder the launcher does not know; owner decides"}
                        for task in sorted(child.iterdir()) if task.is_dir() and task.name not in known_tasks]
            continue
        if any(_within(child.resolve(), root) for root in configured):
            continue
        home = next((name for root, name in owned.items() if _within(child.resolve(), root)), None)
        reason = (f"artifact root of the owned home {home}; run ARCANUM_LOCAL_HOME={home} ./arcanum clean" if home
                  else "not a configured artifact root (older layout or a lab output); owner decides")
        results.append({"path": str(child), "bytes": disk_bytes(child), "class": "keep", "reason": reason})
    return results


def _git(*arguments: str) -> str | None:
    try:
        return subprocess.run(["git", *arguments], check=True, capture_output=True, text=True).stdout
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
            if not merged:
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
            "unmanaged": unmanaged(conf, checkout), "worktrees": worktrees(checkout), "caches": caches(checkout)}


def apply(checkout: Path) -> dict[str, Any]:
    """Retire superseded registered builds, then prune every retired payload through one retention plan."""
    with config.registry_lock():
        conf, registry = config.load_config(), config.load_registry()
        retired = [item["candidateId"] for item in builds(conf, registry) if item["action"] == "retire-and-prune"]
        for candidate_id in retired:
            retention.retire(registry, candidate_id, conf=conf)
        if retired:
            config.save_registry(registry)
    conf, registry = config.load_config(), config.load_registry()
    plan = retention.create_plan(conf, registry, retention.inventory(conf, registry, show_paths=True))
    if not plan["payloads"]:
        return {"retired": retired, "planId": None, "status": "nothing-to-remove", "removed": []}
    # apply_plan reloads the authorities under the registry lock and rechecks every binding and lease before removal.
    result = retention.apply_plan(plan, conf, registry, accepted_id=plan["id"])
    freed = sum(item["payloadIdentity"]["bytes"] for item in plan["payloads"] if item["candidateId"] in result["removed"])
    return {"retired": retired, **result, "freedBytes": freed}


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
    for title, key in (("Not managed by this home", "unmanaged"), ("Worktrees (report only)", "worktrees"),
                       ("Regenerable build outputs (report only)", "caches")):
        if result[key]:
            lines.append(f"{title}:")
            lines += [f"  {item['class'].upper():<11} {_size(item['bytes']):>9}  {item['path']}: {item['reason']}" for item in result[key]]
    disposable = sum(item["payloadBytes"] or 0 for item in result["builds"] if item["class"] == "disposable")
    lines.append(f"`./arcanum clean --apply` would remove {_size(disposable)} of DISPOSABLE build payloads; "
                 "PROTECTED and KEEP builds, manifests and evidence stay. Never delete artifacts/ by hand "
                 "(docs/workflow/build-retention.md).")
    return "\n".join(lines)


def run(checkout: Path, *, do_apply: bool, as_json: bool) -> None:
    write_marker(checkout / "artifacts")
    if do_apply:
        result = apply(checkout)
        print(json.dumps(result, indent=2, sort_keys=True) if as_json else
              f"Retired {len(result['retired'])}, removed {len(result['removed'])} payload(s), "
              f"freed {_size(result.get('freedBytes', 0))} ({result['status']}).")
        return
    result = report(config.load_config(), config.load_registry(), checkout)
    print(json.dumps(result, indent=2, sort_keys=True) if as_json else render(result))
