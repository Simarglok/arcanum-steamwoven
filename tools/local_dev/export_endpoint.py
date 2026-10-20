#!/usr/bin/env python3
"""Run one real export under its candidate lease and publish it only after success."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import subprocess
import sys
import json
import tempfile
import threading
import time
import shutil
from datetime import datetime, timezone
from typing import Sequence

from . import config, retention
from .manifests import SUPPORTED_TOOLS, sha256


def _candidate_path(root_id: str, task: str, candidate: str) -> tuple[Path, str]:
    if not root_id or not task or not candidate:
        raise ValueError("export endpoint requires root, task, and candidate identity")
    if (Path(candidate).is_absolute() or candidate in (".", "..")
            or "/" in candidate or "\\" in candidate):
        raise ValueError("export endpoint candidate must be one safe path component")
    if not re.fullmatch(r"[0-9a-f]{40}", candidate):
        raise ValueError("export endpoint candidate must be a full lowercase-hex commit")
    tool = next((tool for tool, tasks in SUPPORTED_TOOLS.items() if task in tasks), "")
    identity = f"{root_id}:{task}:{candidate}"
    return config.candidate_path(config.load_config(), tool, identity), identity


class ObservationSampler:
    """Bounded capacity sampling; free-space change is NEVER temporary usage."""
    def __init__(self, paths: dict[str, Path], temporary: Path, *, coverage: bool = False,
                 interval: float = 0.25, clock=time.monotonic, capacity=shutil.disk_usage,
                 limit: int = 4096):
        self.paths, self.temporary, self.coverage = paths, temporary, coverage
        self.interval, self.clock, self.capacity, self.limit = interval, clock, capacity, limit
        self.started = clock()
        self.samples: list[dict] = []
        self.peak = 0
        self.error: str | None = None
        self.truncated = False
        self.stop_event = threading.Event()
        self.thread: threading.Thread | None = None

    def sample(self, phase: str) -> None:
        try:
            free = {role: self.capacity(retention.destination_proof(path)[0]).free
                    for role, path in self.paths.items()}
            allocated = 0
            for path in self.temporary.rglob("*"):
                if path.is_symlink():
                    raise ValueError("temporary scope contains symlink")
                if path.is_file():
                    allocated += path.stat().st_blocks * 512
            self.peak = max(self.peak, allocated)
            sample = {"phase": phase, "elapsedSeconds": self.clock() - self.started,
                      "freeBytes": free, "isolatedTemporaryAllocatedBytes": allocated}
            if len(self.samples) < self.limit or phase == "final":
                self.samples.append(sample)
            else:
                self.truncated = True
        except (OSError, ValueError):
            self.error = "destination or isolated temporary scope could not be sampled"

    def start(self) -> None:
        self.sample("baseline")
        def poll() -> None:
            while not self.stop_event.wait(self.interval):
                self.sample("periodic")
        self.thread = threading.Thread(target=poll, name="export-capacity-sampler", daemon=True)
        self.thread.start()

    def close(self) -> None:
        self.stop_event.set()
        if self.thread is not None:
            self.thread.join()

    def finish(self, candidate_id: str, tool: str, success: bool) -> dict:
        self.close()
        self.sample("final")
        qualified = self.coverage and self.error is None and not self.truncated and success
        return {"schema": "arcanum-export-observation-v1", "candidateId": candidate_id,
                "candidateCommit": candidate_id.split(":")[-1], "tool": tool,
                "samplingIntervalSeconds": self.interval, "sampleCount": len(self.samples),
                "destinationBindings": {role: retention.destination_proof(path)[1] for role, path in self.paths.items()},
                "finishedAt": datetime.now(timezone.utc).isoformat(), "successfulValidation": success,
                "temporaryMeasurementStatus": "qualified" if qualified else "unavailable",
                "observedTemporaryBytes": self.peak if qualified else None,
                "temporaryAttribution": "all temporary writes isolated" if self.coverage else "coverage incomplete: build/Godot/NuGet caches may write outside TMPDIR",
                "limitation": self.error or ("bounded sample limit reached" if self.truncated else "sampled peak; between-sample allocations not observed"),
                "samples": self.samples,
                "freeLowWaterBytes": {role: min(sample["freeBytes"][role] for sample in self.samples)
                                      for role in self.paths} if self.samples else {}}


def bound_budget(candidate: Path, tool: str, candidate_id: str, output_bytes: int) -> dict:
    """Shared writer contract: frozen observation must exist BEFORE hashing."""
    preflight = json.loads(config.regular_file(candidate / "evidence/export-preflight.json").read_text())
    observation = json.loads(config.regular_file(candidate / "evidence/export-observation.json").read_text())
    if (preflight.get("schema") != "arcanum-export-preflight-v1"
            or observation.get("schema") != "arcanum-export-observation-v1"
            or any(value.get("tool") != tool or value.get("candidateId") != candidate_id
                   or value.get("candidateCommit") != candidate.name for value in (preflight, observation))
            or observation.get("successfulValidation") is not True
            or observation.get("temporaryMeasurementStatus") not in ("qualified", "unavailable")
            or not observation.get("samples") or not observation.get("freeLowWaterBytes")):
        raise ValueError("export observation/preflight is not bound to successful candidate")
    observed = observation.get("observedTemporaryBytes")
    qualified = observation["temporaryMeasurementStatus"] == "qualified"
    if ((qualified and (type(observed) is not int or observed < 0)) or (not qualified and observed is not None)):
        raise ValueError("temporary observation must be attributed or explicitly unavailable")
    samples = observation["samples"]
    if (not isinstance(samples, list) or len(samples) < 2 or len(samples) > 4097
            or not isinstance(samples[0], dict) or samples[0].get("phase") != "baseline"
            or not isinstance(samples[-1], dict) or samples[-1].get("phase") != "final"):
        raise ValueError("observation needs bounded baseline/final samples")
    roles = set(observation["freeLowWaterBytes"])
    if not {"export", "temporary"}.issubset(roles):
        raise ValueError("observation needs actual export/temporary capacity")
    for sample in samples:
        if (not isinstance(sample, dict) or not isinstance(sample.get("freeBytes"), dict)
                or set(sample["freeBytes"]) != roles
                or any(type(value) is not int or value < 0 for value in sample["freeBytes"].values())):
            raise ValueError("invalid destination capacity sample")
    if any(observation["freeLowWaterBytes"][role] != min(sample["freeBytes"][role] for sample in samples)
           for role in roles):
        raise ValueError("free low-water does not match sampled capacity")
    if qualified and observation.get("temporaryAttribution") != "all temporary writes isolated":
        raise ValueError("qualified observation requires all temporary writes attributed")
    return {"outputBytes": output_bytes, "temporaryLowWaterBytes": observed if qualified else 0,
            "preflight": preflight, "observation": observation}


def _reject_existing(candidate: Path, candidate_id: str, tool: str, conf: dict, registry: dict,
                     *, lease_held: bool = False) -> None:
    manifest = candidate / "manifest.json"
    if manifest.exists():
        retention._registered_manifest(conf=conf, tool=tool, candidate_id=candidate_id, manifest=manifest)
        raise ValueError("immutable finalized candidate cannot be exported again")
    if candidate_id in registry.get("builds", {}):
        raise ValueError("registered candidate cannot be exported again")
    allowed = {".export-lease.lock"}
    if candidate.exists() and any(path.name not in allowed for path in candidate.iterdir()):
        raise ValueError("ambiguous partial candidate cannot be overwritten")
    lease_path = candidate / ".export-lease.lock"
    if lease_path.exists() or lease_path.is_symlink():
        config.validate_lock_file(lease_path)


def _write_current(candidate: Path, manifest: Path) -> None:
    pointer = candidate.parent / "current.json"
    config.write_atomic(pointer, {"schema": "arcanum-build-current-v2",
                                  "manifestRelativePath": f"{candidate.name}/manifest.json",
                                  "manifestSha256": sha256(manifest)})


def _snapshot(path: Path) -> bytes | None:
    return config.optional_authority_bytes(path)


def _restore_publication(pointer: Path, pointer_snapshot: bytes | None,
                         registry_snapshot: bytes | None) -> None:
    """Return both published bindings to their exact pre-endpoint snapshots."""
    restore_error: OSError | None = None
    for path, snapshot in ((config.registry_path(), registry_snapshot), (pointer, pointer_snapshot)):
        try:
            config.restore_atomic_bytes(path, snapshot)
        except OSError as error:
            restore_error = error
    if restore_error is not None:
        raise RuntimeError("export endpoint publication recovery failed") from restore_error


def run_export(args: argparse.Namespace) -> int:
    if args.tool not in SUPPORTED_TOOLS or args.task not in SUPPORTED_TOOLS[args.tool]:
        raise ValueError("export endpoint tool/task mapping is unsupported")
    candidate, candidate_id = _candidate_path(args.root_id, args.task, args.candidate)
    manifest = Path(args.manifest).absolute()
    if manifest != candidate / "manifest.json" or config._has_symlink_component(manifest):
        raise ValueError("export endpoint manifest must be the exact candidate manifest")
    conf, registry = config.load_config(), config.load_registry()
    _reject_existing(candidate, candidate_id, args.tool, conf, registry)
    temporary_text = getattr(args, "temporary_root", None) or os.environ.get("TMPDIR")
    if not temporary_text:
        raise ValueError("explicit effective temporary destination is required")
    temporary_root = config.canonical_existing(temporary_text, directory=True)
    paths = {"export": candidate, "temporary": temporary_root}
    for index, path in enumerate(getattr(args, "write_root", []) or []):
        paths[f"build-cache-{index}"] = Path(path).absolute()
    proofs = {role: retention.destination_proof(path) for role, path in paths.items()}
    with config.registry_lock():
        preflight = retention.export_preflight(registry, args.tool, getattr(args, "output_bytes", None),
                    getattr(args, "temporary_bytes", None), conf=conf, destinations=paths,
                    estimate_manifest=Path(args.estimate_manifest) if getattr(args, "estimate_manifest", None) else None,
                    first_export=getattr(args, "first_export", False))
    # The shared no-follow opener creates/pins missing parents; mkdir on the
    # original pathname would follow a concurrently introduced parent symlink.
    lease = retention._try_exclusive(candidate / ".export-lease.lock")
    if lease is None:
        raise ValueError("candidate is already exporting")
    try:
        with config.registry_lock():
            conf, registry = config.load_config(), config.load_registry()
            if config.candidate_path(conf, args.tool, candidate_id) != candidate:
                raise ValueError("configured export root changed before export")
            _reject_existing(candidate, candidate_id, args.tool, conf, registry, lease_held=True)
            retention.verify_destinations(paths, proofs)
            preflight = retention.export_preflight(registry, args.tool, getattr(args, "output_bytes", None),
                        getattr(args, "temporary_bytes", None), conf=conf, destinations=paths,
                        estimate_manifest=Path(args.estimate_manifest) if getattr(args, "estimate_manifest", None) else None,
                        first_export=getattr(args, "first_export", False))
        preflight = {"schema": "arcanum-export-preflight-v1", **preflight,
                     "candidateId": candidate_id, "candidateCommit": args.candidate}
        evidence = candidate / "evidence" / "export-preflight.json"
        config.write_atomic(evidence, preflight)
        isolated = Path(tempfile.mkdtemp(prefix="arcanum-export-", dir=temporary_root))
        environment = {**os.environ, "TMPDIR": str(isolated), "ARCANUM_EXPORT_LEASE_HELD": "1",
                       "ARCANUM_EXPORT_PHASE": "validate"}
        sampler = ObservationSampler(paths, isolated)
        success = False
        try:
            sampler.start()
            completed = subprocess.run(args.command, check=False, env=environment)
            success = completed.returncode == 0
        finally:
            sampler.close()
            observation = sampler.finish(candidate_id, args.tool, success)
            config.write_atomic(candidate / "evidence/export-observation.json", observation)
        if completed.returncode:
            return completed.returncode
        retention.verify_destinations(paths, proofs)
        finalized = subprocess.run(args.command, check=False, env={**environment, "ARCANUM_EXPORT_PHASE": "finalize"})
        if finalized.returncode:
            return finalized.returncode
        with config.registry_lock():
            # The root can change while an export is running; re-derive it
            # under the lifecycle lock and reject rather than registering an
            # artifact outside its explicitly configured canonical root.
            verified_candidate, verified_id = _candidate_path(args.root_id, args.task, args.candidate)
            if verified_candidate != candidate or verified_id != candidate_id:
                raise ValueError("configured export root changed during export")
            registry = config.load_registry()
            conf = config.load_config()
            retention.verify_destinations(paths, proofs)
            registry_snapshot = _snapshot(config.registry_path())
            pointer = candidate.parent / "current.json"
            pointer_snapshot = _snapshot(pointer)
            retention.register_export(registry, conf=conf, tool=args.tool, candidate_id=candidate_id,
                                      manifest=manifest, lease_held=True)
            try:
                # A pointer failure must not publish the new lifecycle. If
                # persisting the registry subsequently fails, restore both
                # exact snapshots while the registry lock and candidate lease
                # are still held.
                _write_current(candidate, manifest)
                config.save_registry(registry)
            except (OSError, ValueError):
                _restore_publication(pointer, pointer_snapshot, registry_snapshot)
                raise
        return 0
    finally:
        retention._release(lease)


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--tool", required=True, choices=SUPPORTED_TOOLS)
    result.add_argument("--root-id", required=True)
    result.add_argument("--task", required=True)
    result.add_argument("--candidate", required=True)
    result.add_argument("--manifest", required=True)
    result.add_argument("--output-bytes", type=int,
                        help="explicit measured output estimate when no accepted measurement exists")
    result.add_argument("--temporary-bytes", type=int,
                        help="optional explicit measured temporary low-water estimate")
    result.add_argument("--estimate-manifest")
    result.add_argument("--first-export", action="store_true",
                        help="ARC-DEV-026: no payload survives; --output-bytes is the owner's estimate")
    result.add_argument("--temporary-root")
    result.add_argument("--write-root", action="append", default=[])
    result.add_argument("command", nargs=argparse.REMAINDER)
    return result


def main(argv: Sequence[str] | None = None) -> int:
    args = parser().parse_args(argv)
    if not args.command or args.command[0] != "--" or len(args.command) == 1:
        raise ValueError("export endpoint requires a command after --")
    args.command = args.command[1:]
    return run_export(args)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        print(f"export-endpoint: {error}", file=sys.stderr)
        raise SystemExit(2)
