#!/usr/bin/env python3
"""Audio-lab-only command receipts. Logs live outside exported payloads."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

ROOT = Path(__file__).resolve().parents[3]


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def tree_digest():
    paths = sorted(p for p in (ROOT / "experiments/audio-lab").rglob("*")
                   if p.is_file() and not any(x in {"bin", "obj", ".godot", "__pycache__"} for x in p.parts))
    docs = ROOT / "docs/audio/audio-lab.md"
    if docs.exists():
        paths.append(docs)
    h = hashlib.sha256()
    for p in paths:
        h.update(str(p.relative_to(ROOT)).encode() + b"\0" + p.read_bytes())
    return h.hexdigest()


def rss_tree(pid):
    rows = subprocess.check_output(["/bin/ps", "-axo", "pid=,ppid=,rss="], text=True)
    processes = {}
    for line in rows.splitlines():
        fields = line.split()
        if len(fields) == 3:
            processes[int(fields[0])] = (int(fields[1]), int(fields[2]))
    selected = {pid}
    while True:
        expanded = selected | {p for p, (parent, _) in processes.items() if parent in selected}
        if expanded == selected:
            break
        selected = expanded
    return sum(processes.get(p, (0, 0))[1] for p in selected) * 1024


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--label", required=True)
    parser.add_argument("--task", default="t_7dac6703")
    parser.add_argument("--run", required=True)
    parser.add_argument("--timeout", type=float, default=180)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    a = parser.parse_args()
    evidence = a.evidence.resolve()
    allowed = ROOT / "artifacts/audio-lab"
    if not evidence.is_relative_to(allowed) or not (evidence / "ownership.json").is_file():
        raise SystemExit("Needs owned ignored audio-lab evidence directory")
    if not a.label.replace("-", "").isalnum():
        raise SystemExit("Invalid label")
    command = a.command[1:] if a.command[:1] == ["--"] else a.command
    log = evidence / (a.label + ".log")
    receipt = evidence / (a.label + ".json")
    if log.exists() or receipt.exists():
        raise SystemExit("Never overwrite earlier evidence; use a fresh label/run")
    stamp = datetime.datetime.now(datetime.timezone.utc).isoformat()
    head = git("rev-parse", "HEAD")
    before_digest = tree_digest()
    env = os.environ.copy()
    dotnet = Path.home() / ".local/share/dotnet"
    env.update(DOTNET_ROOT=str(dotnet), PATH=str(dotnet) + ":/usr/bin:/bin:/usr/sbin:/sbin", __GODOT_SHELL_ENV_SET="1")
    start = time.monotonic()
    peak = 0
    timeout = False
    with log.open("x") as stream:
        process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=stream, stderr=subprocess.STDOUT)
        while process.poll() is None:
            peak = max(peak, rss_tree(process.pid))
            if time.monotonic() - start > a.timeout:
                process.terminate()
                timeout = True
                try:
                    process.wait(5)
                except subprocess.TimeoutExpired:
                    process.kill()
                break
            time.sleep(.15)
        exit_code = process.wait()
    record = dict(task=a.task, run=a.run, evidence_session=evidence.name,
                  workspace=str(ROOT), head=head, source_digest=before_digest,
                  source_unchanged=before_digest == tree_digest(), timestamp=stamp,
                  command=command, canonical_dotnet=str(dotnet / "dotnet"),
                  pid=process.pid, exit=exit_code, timed_out=timeout,
                  seconds=time.monotonic() - start, sampled_process_tree_peak_rss_bytes=peak,
                  rss_sample_interval_seconds=.15, log=str(log),
                  log_sha256=hashlib.sha256(log.read_bytes()).hexdigest(),
                  status=git("status", "--short"))
    receipt.write_text(json.dumps(record, indent=2) + "\n")
    print(json.dumps(record, indent=2))
    # Output is not a native observation pass: callers inspect reports/logs.
    return exit_code if not timeout else 124


if __name__ == "__main__":
    raise SystemExit(main())
