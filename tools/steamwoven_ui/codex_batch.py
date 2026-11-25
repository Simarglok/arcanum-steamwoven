"""ARC-DEV-3001: runs one Steamwoven inbox batch through the owner's subscription Codex CLI and writes the outbox.

The batch is `local/steamwoven-ui/inbox/<batch>/batch.json` (written by `prompt_library.py inbox`; the format is in docs/ui/steamwoven-interface-trd.md,
"Codex batch runner"). One Codex session serves the whole batch, so the style stays consistent: the first item is `codex exec`, every later item
`codex exec resume <thread_id>` from the same working directory, strictly in order. The image comes from Codex's built-in `image_gen` tool (no API
key, no API fallback). Standard library only. Never writes into the inbox, never touches git.

    python3 -B tools/steamwoven_ui/codex_batch.py check
    python3 -B tools/steamwoven_ui/codex_batch.py run BATCH --dry-run [--only ID,ID]
    python3 -B tools/steamwoven_ui/codex_batch.py run BATCH [--only ID,ID] [--timeout SECONDS] [--overwrite] [--crop-band] [--local-root DIR] [--codex PATH] [--model M]
    python3 -B tools/steamwoven_ui/codex_batch.py crop IMAGE --size WxH --out OUT.png [--aspect W:H] [--fuzz PERCENT]

The model is pinned (owner decision 2026-11-20): `gpt-6.1-sol` at reasoning effort `medium`, passed on every `exec` and `exec resume`. `--model` is an explicit override and
needs the owner's approval for any other model; the effort is never changed. `~/.codex/config.toml` is not read or written here.

Extreme aspects (ARC-UI-3083, `band_crop.py`, needs ImageMagick): for 19.5:1 and 5:1 the generator returns a 2103 x 748 canvas with white or black bars and the art drawn at the
right proportion inside. With `--crop-band` an item judged `wrong-aspect` is cropped to that band (fuzz trim, a shave of 2 to 4 px for the light anti-aliasing line, the band's aspect
within 1 %) and resized to the item's size; the item is then `ok`, the outbox file is the cropped image and the provenance record holds the `crop` section and the raw file's hash.
`crop` does the same for one file (a canvas left in a work folder) and writes `OUT.crop.json` beside it.

Exit codes: 0 every item ok, 1 at least one item did not produce a good image, 2 the pre-check or the batch file failed (nothing was sent).
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import os
import re
import shlex
import shutil
import signal
import struct
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

try:
    from tools.steamwoven_ui import band_crop, image_archive, prompt_library
except ImportError:  # run as a script from its own folder
    import band_crop  # type: ignore[no-redef]
    import image_archive  # type: ignore[no-redef]
    import prompt_library  # type: ignore[no-redef]

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
ASPECT_TOLERANCE = 0.01  # TRD SWI-GEN-8: the result's aspect is within 1 % of the asked one
# Owner decision 2026-11-20 (#3043): the model of the good #2455 trial; newer models are not used without the owner's approval.
DEFAULT_MODEL = "gpt-6.1-sol"
DEFAULT_EFFORT = "medium"
DEFAULT_TIMEOUT = 900  # seconds per item; one generation can take minutes
BUDGET_PIXELS = 1_570_000  # the tool keeps a budget of about 1.57 MP per image
OUTCOMES = ("ok", "refused", "failed", "timeout", "bad-result", "missing-file", "not-png", "wrong-aspect", "skipped")
SESSION_RULES = """SESSION RULES (batch {batch}, {count} image(s), this one session, items strictly in order)
- Use $imagegen and the built-in image_gen tool only. No API fallback, no scripts/image_gen.py, no OPENAI_API_KEY.
- Save each result in the current working directory under the exact file name asked, as a PNG.
- Keep the asked aspect ratio. Render as large as the tool allows. Never upscale afterwards.
- Reference images are attached to the message and also stored under refs/ of the working directory; open them with view_image when needed.
- Keep one consistent look across the items of this session.
- Your last message is only this JSON, nothing else: {{"ok": true or false, "path": "<file name>", "error": "<reason or null>"}}
  If you refuse or cannot make the image, put ok false and the reason in error.
"""
ITEM_HEADER = """ITEM: {id}
FILE: {file}
ASPECT: {aspect}
REFERENCES: {refs}

PROMPT:
"""


class BatchError(Exception):
    """The batch file or the pre-check is not usable; nothing was sent to Codex."""


@dataclass
class Item:
    id: str
    prompt_file: str
    prompt_sha256: str
    outbox_file: str
    size: list[int] | None
    aspect: list[int] | None
    references: list[dict]
    title: str = ""


@dataclass
class ItemResult:
    id: str
    outcome: str = "skipped"
    detail: str = ""
    pixel_size: list[int] | None = None
    thread_id: str | None = None
    exit_code: int | None = None
    seconds: float = 0.0
    codex_text: str = ""
    commands: list[str] = field(default_factory=list)
    crop: dict | None = None  # ARC-UI-3083: the band crop report of an item that came back padded (--crop-band)


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    return sha256_bytes(path.read_bytes())


PNG_END = b"\x00\x00\x00\x00IEND\xaeB`\x82"


def png_size(path: Path) -> tuple[int, int]:
    """The pixel size of a whole PNG (signature, IHDR first, IEND last); a symlink, a truncated or a foreign file is refused."""
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"{path.name} is not a plain file")
    blob = path.read_bytes()
    if blob[:8] != PNG_SIGNATURE or blob[12:16] != b"IHDR" or not blob.endswith(PNG_END):
        raise ValueError(f"{path.name} is not a whole PNG")
    return struct.unpack(">II", blob[16:24])


def aspect_within(size: tuple[int, int], want: list[int], tolerance: float = ASPECT_TOLERANCE) -> bool:
    if size[0] <= 0 or size[1] <= 0:
        return False
    ratio, target = size[0] / size[1], want[0] / want[1]
    return abs(ratio - target) / target <= tolerance


def load_batch(local_root: Path, batch: str) -> tuple[Path, list[Item]]:
    if not re.fullmatch(r"[A-Za-z0-9._-]+", batch) or batch in (".", ".."):
        raise BatchError(f"bad batch name {batch!r}")
    inbox = local_root / "inbox" / batch
    manifest_path = inbox / "batch.json"
    if not manifest_path.is_file():
        raise BatchError(f"no {manifest_path}")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        raise BatchError(f"{manifest_path} is not JSON: {error}") from error
    if manifest.get("batch") != batch or not isinstance(manifest.get("items"), list) or not manifest["items"]:
        raise BatchError(f"{manifest_path}: needs \"batch\": \"{batch}\" and a non-empty \"items\" list")
    items, seen = [], set()
    for raw in manifest["items"]:
        if not isinstance(raw, dict):
            raise BatchError(f"{manifest_path}: every item must be an object")
        for key in ("id", "prompt_file", "outbox_file", "prompt_sha256"):
            if not isinstance(raw.get(key), str) or not raw[key]:
                raise BatchError(f"{manifest_path}: an item lacks \"{key}\"")
        refs = raw.get("references") or []
        if not isinstance(refs, list) or not all(isinstance(r, dict) and isinstance(r.get("file"), str) and r["file"]
                                                 and isinstance(r.get("sha256"), str) and r["sha256"] for r in refs):
            raise BatchError(f"{raw['id']}: every reference needs \"file\" and \"sha256\"")
        if not re.fullmatch(r"[A-Za-z0-9._-]+", raw["id"]) or raw["id"] in seen:
            raise BatchError(f"{manifest_path}: item id {raw['id']!r} is not a plain unique name")
        seen.add(raw["id"])
        size, aspect = raw.get("size"), raw.get("aspect")
        if size is not None and not (isinstance(size, list) and len(size) == 2 and all(isinstance(v, int) and v > 0 for v in size)):
            raise BatchError(f"{raw['id']}: \"size\" must be [width, height] or null")
        if aspect is not None and not (isinstance(aspect, list) and len(aspect) == 2 and all(isinstance(v, int) and v > 0 for v in aspect)):
            raise BatchError(f"{raw['id']}: \"aspect\" must be [w, h] or null")
        want = outbox_target(local_root, batch, raw)
        if want.name != f"{raw['id']}.png":
            raise BatchError(f"{raw['id']}: outbox_file must be outbox/{batch}/{raw['id']}.png")
        items.append(Item(raw["id"], raw["prompt_file"], raw.get("prompt_sha256", ""), raw["outbox_file"], size, aspect,
                          list(raw.get("references") or []), raw.get("title", "")))
    return inbox, items


def outbox_target(local_root: Path, batch: str, raw: dict) -> Path:
    target = (local_root / raw["outbox_file"]).resolve()
    outbox = (local_root / "outbox" / batch).resolve()
    if target.parent != outbox:
        raise BatchError(f"{raw['id']}: outbox_file {raw['outbox_file']} is not inside outbox/{batch}/")
    return target


def asked_aspect(item: Item) -> list[int] | None:
    if item.aspect:
        return item.aspect
    return list(item.size) if item.size else None


def inside_inbox(inbox: Path, item_id: str, relative: str) -> Path:
    path = (inbox / relative).resolve()
    if inbox.resolve() not in path.parents:
        raise BatchError(f"{item_id}: {relative} is outside the inbox")
    return path


def item_inputs(inbox: Path, item: Item) -> tuple[str, list[Path]]:
    """The prompt text and the reference files of an item, read from the inbox (confined to it, checked against the recorded hashes)."""
    prompt_path = inside_inbox(inbox, item.id, item.prompt_file)
    if not prompt_path.is_file():
        raise BatchError(f"{item.id}: prompt file {item.prompt_file} is missing")
    text = prompt_path.read_text(encoding="utf-8")
    if prompt_library.sha256_text(text) != item.prompt_sha256:
        raise BatchError(f"{item.id}: the prompt text does not match prompt_sha256 of batch.json")
    refs = []
    for ref in item.references:
        path = inside_inbox(inbox, item.id, ref["file"])
        if not path.is_file():
            raise BatchError(f"{item.id}: reference {ref['file']} is missing")
        if sha256_file(path) != ref["sha256"]:
            raise BatchError(f"{item.id}: reference {ref['file']} does not match its sha256")
        refs.append(path)
    return text, refs


# ---- the Codex CLI ----

def codex_env() -> dict[str, str]:
    env = dict(os.environ)
    for name in [n for n in env if n.startswith("OPENAI_") or n in ("CODEX_API_KEY",)]:
        del env[name]  # subscription only: no API key may reach the session
    return env


def run_plain(codex: str, args: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run([codex, *args], capture_output=True, text=True, timeout=60, env=codex_env())


def precheck(codex: str) -> str:
    """Returns the CLI version string; raises BatchError with the reason when the CLI cannot make images."""
    try:
        version = run_plain(codex, ["--version"])
    except (FileNotFoundError, subprocess.TimeoutExpired) as error:
        raise BatchError(f"codex CLI not usable ({error})") from error
    if version.returncode != 0:
        raise BatchError(f"codex --version failed: {version.stderr.strip()[:200]}")
    shown = version.stdout.strip()
    try:
        features = run_plain(codex, ["features", "list"])
        login = run_plain(codex, ["login", "status"])
    except subprocess.TimeoutExpired as error:
        raise BatchError(f"codex did not answer in time ({' '.join(error.cmd[1:3])})") from error
    enabled = [line.split() for line in features.stdout.splitlines() if line.split()[:1] == ["image_generation"]]
    if features.returncode != 0 or not enabled or enabled[0][-1] != "true":
        raise BatchError("the codex feature image_generation is not enabled (`codex features list`)")
    text = (login.stdout + login.stderr).strip()
    if login.returncode != 0 or "logged in" not in text.lower():
        raise BatchError(f"codex is not logged in ({text[:200] or 'no output'}); the owner signs in with `codex login`")
    if "api key" in text.lower():
        raise BatchError("codex is logged in with an API key; this runner uses the subscription login only (`codex login` with ChatGPT)")
    return shown


def version_number(shown: str) -> str:
    found = re.search(r"\d+(?:\.\d+)+\S*", shown)
    return found.group(0) if found else shown


def exec_command(codex: str, workdir: Path, result_file: Path, images: list[Path], model: str | None, thread_id: str | None) -> list[str]:
    common = ["--skip-git-repo-check", "--enable", "image_generation", "--json", "-o", str(result_file)]
    common += ["-m", model or DEFAULT_MODEL, "-c", f'model_reasoning_effort="{DEFAULT_EFFORT}"']
    common += [f"--image={path}" for path in images]
    if thread_id is None:
        return [codex, "exec", "--sandbox", "workspace-write", "-C", str(workdir), *common, "-"]
    # `exec resume` has neither --sandbox nor -C: the working directory is the process directory, the sandbox is set by config
    return [codex, "exec", "resume", "-c", 'sandbox_mode="workspace-write"', *common, thread_id, "-"]


def parse_events(stdout: str) -> tuple[str | None, str]:
    """The thread id of the `thread.started` event and the text of the agent messages (Codex's own words)."""
    thread_id, texts = None, []
    for line in stdout.splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        if not isinstance(event, dict):
            continue
        if event.get("type") == "thread.started" and isinstance(event.get("thread_id"), str):
            thread_id = event["thread_id"]
        item = event.get("item")
        if event.get("type") == "item.completed" and isinstance(item, dict) and item.get("type") == "agent_message" and isinstance(item.get("text"), str):
            texts.append(item["text"])
    return thread_id, "\n".join(texts)


def run_codex(command: list[str], prompt: str, cwd: Path, timeout: float) -> tuple[int | None, str, str, bool]:
    """Runs one command with the prompt on stdin; a time-out kills the whole process group. Returns (exit code, stdout, stderr, timed out)."""
    process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, cwd=cwd,
                               env=codex_env(), start_new_session=True)
    def kill_group() -> None:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass

    try:
        out, err = process.communicate(prompt, timeout=timeout)
        return process.returncode, out, err, False
    except subprocess.TimeoutExpired:
        kill_group()
        try:
            out, err = process.communicate(timeout=10)  # a descendant that left the group could hold the pipes open
        except subprocess.TimeoutExpired:
            out, err = "", ""
        return None, out or "", err or "", True
    except BaseException:  # Ctrl-C or SIGTERM of the runner: the child leads its own session and would run on
        kill_group()
        raise


def extract_json(text: str) -> dict | None:
    """The result JSON of the last message: the whole text, or the last {...} block in it."""
    text = text.strip()
    candidates = [text] + re.findall(r"\{[^{}]*\}", text)[::-1]
    for candidate in candidates:
        try:
            value = json.loads(candidate)
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict) and "ok" in value:
            return value
    return None


# ---- one batch ----

def build_prompt(batch: str, count: int, item: Item, text: str, refs: list[Path], first: bool) -> str:
    want = asked_aspect(item)
    aspect = f"{want[0]}:{want[1]}" + (f" (the kit master is {item.size[0]} x {item.size[1]} px; deliver as large as the tool allows at this aspect)" if item.size else "") if want else "as the prompt says"
    head = SESSION_RULES.format(batch=batch, count=count) + "\n" if first else ""
    names = ", ".join(f"refs/{item.id}/{path.name}" for path in refs) or "none"
    return head + ITEM_HEADER.format(id=item.id, file=f"{item.id}.png", aspect=aspect, refs=names) + text


def stage_references(workdir: Path, item: Item, refs: list[Path]) -> list[Path]:
    staged = []
    for path in refs:
        target = workdir / "refs" / item.id / path.name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        staged.append(target)
    return staged


def judge(item: Item, workdir: Path, result_file: Path, exit_code: int | None, timed_out: bool, codex_text: str) -> ItemResult:
    result = ItemResult(item.id, exit_code=exit_code, codex_text=codex_text)
    if timed_out:
        result.outcome, result.detail = "timeout", "the item did not finish within the time-out; Codex was stopped"
        return result
    if exit_code != 0:
        result.outcome, result.detail = "failed", f"codex exited with code {exit_code}"
        return result
    raw = result_file.read_text(encoding="utf-8") if result_file.is_file() else ""
    parsed = extract_json(raw) or extract_json(codex_text)
    if parsed is None:
        result.outcome, result.detail = "bad-result", "the last message is not the result JSON"
        return result
    if parsed.get("ok") is not True:
        result.outcome, result.detail = "refused", str(parsed.get("error") or "Codex reported ok false without a reason")
        return result
    image = workdir / f"{item.id}.png"
    if not image.is_file():
        result.outcome, result.detail = "missing-file", f"the result JSON says ok but {image.name} is not in the working directory"
        return result
    try:
        size = png_size(image)
    except ValueError:
        result.outcome, result.detail = "not-png", f"{image.name} is not a PNG"
        return result
    result.pixel_size = list(size)
    want = asked_aspect(item)
    if want and not aspect_within(size, want):
        result.outcome, result.detail = "wrong-aspect", f"{size[0]} x {size[1]} is not within 1 % of the asked aspect {want[0]}:{want[1]}"
        return result
    result.outcome = "ok"
    return result


def write_provenance(local_root: Path, batch: str, item: Item, result: ItemResult, prompt_text: str, refs: list[Path], version: str,
                     thread_id: str | None, run_stamp: str) -> Path:
    outbox = local_root / "outbox" / batch
    outbox.mkdir(parents=True, exist_ok=True)
    record = {"generator": f"codex-cli {version} image_gen", "date": datetime.datetime.now(datetime.timezone.utc).date().isoformat(), "run": run_stamp, "batch": batch, "id": item.id,
              "prompt_sha256": prompt_library.sha256_text(prompt_text),
              "references": [{"file": ref["file"], "sha256": ref.get("sha256") or sha256_file(path)} for ref, path in zip(item.references, refs)],
              "thread_id": thread_id, "pixel_size": result.pixel_size, "asked_size": item.size, "asked_aspect": asked_aspect(item),
              "outcome": result.outcome, "detail": result.detail, "refusal_text": result.detail if result.outcome == "refused" else None,
              "codex_text": result.codex_text if result.outcome != "ok" else None, "run_by": "agent via the owner's Codex subscription"}
    if result.outcome == "ok":
        record["image"] = item.outbox_file
        record["image_sha256"] = sha256_file(local_root / item.outbox_file)
    if result.crop:
        record["crop"] = result.crop  # the image above is the cropped band; the canvas Codex returned stays in the work folder
        record["raw_file"] = f"codex-work/{batch}/{run_stamp}/{item.id}.png"
        record["raw_pixel_size"] = result.crop["raw_pixel_size"]
        record["detail"] = "padded canvas cropped to the band: " + band_crop.describe(result.crop)
    path = outbox / f"{item.id}.provenance.json"
    if result.outcome != "ok" and (outbox / f"{item.id}.png").exists():
        path = outbox / f"{item.id}.provenance.{run_stamp}.{result.outcome}.json"  # the old image keeps its own record
    path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    return path


def crop_padded(item: Item, workdir: Path, result: ItemResult) -> Path | None:
    """ARC-UI-3083: a `wrong-aspect` canvas with bars is cropped to its band (and resized to the item's size); on success `result` becomes `ok` and the cropped file is
    returned, else the item stays `wrong-aspect` with the reason added to its detail."""
    want = asked_aspect(item)
    try:
        size = list(item.size) if item.size else None
        if size and not aspect_within((size[0], size[1]), want):
            raise band_crop.CropError(f"the asked size {size[0]} x {size[1]} is not at the asked aspect {want[0]}:{want[1]}")
        target = workdir / f"{item.id}.cropped.png"
        report = band_crop.crop_file(workdir / f"{item.id}.png", target, (result.pixel_size[0], result.pixel_size[1]), want, size)
        final = png_size(target)
        if not aspect_within(final, want):
            raise band_crop.CropError(f"the cropped file is {final[0]} x {final[1]}, not within 1 % of {want[0]}:{want[1]}")
    except (band_crop.CropError, ValueError, OSError) as error:
        result.detail += f"; band crop refused: {error}"
        return None
    report["raw_pixel_size"] = result.pixel_size
    result.crop = report
    result.pixel_size = list(final)  # the size of the image the record hashes; the canvas size stays in crop.raw_pixel_size
    result.outcome, result.detail = "ok", band_crop.describe(report)
    return target


def run_batch(local_root: Path, batch: str, codex: str = "codex", only: list[str] | None = None, timeout: float = DEFAULT_TIMEOUT,
              overwrite: bool = False, model: str | None = None, dry_run: bool = False, out=print, crop_band: bool = False) -> tuple[int, list[ItemResult]]:
    inbox, items = load_batch(local_root, batch)
    if only is not None:
        unknown = [name for name in only if name not in {item.id for item in items}]
        if unknown:
            raise BatchError(f"--only names items not in the batch: {', '.join(unknown)}")
        items = [item for item in items if item.id in only]
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    workdir = local_root / "codex-work" / batch / stamp
    todo, results = [], []
    for item in items:
        if (local_root / item.outbox_file).exists() and not overwrite:
            skipped = ItemResult(item.id, "skipped", "outbox file exists (use --overwrite to replace it)")
            results.append(skipped)
            continue
        todo.append(item)
    inputs = {item.id: item_inputs(inbox, item) for item in todo}
    version = "unknown"
    try:
        shown = precheck(codex)
        version = version_number(shown)
        out(f"PRECHECK ok: {shown}, image_generation on, logged in")
    except BatchError as error:
        if not dry_run:
            raise
        out(f"PRECHECK would FAIL: {error}")
    out(f"BATCH {batch}: {len(todo)} to run, {len(results)} skipped; workdir {workdir}; time-out {timeout:g} s per item")
    if dry_run:
        for index, item in enumerate(todo):
            text, refs = inputs[item.id]
            staged = [workdir / "refs" / item.id / path.name for path in refs]
            command = exec_command(codex, workdir, workdir / f"result-{item.id}.json", staged, model, None if index == 0 else "<thread_id>")
            out(f"DRY-RUN item {item.id}: cd {shlex.quote(str(workdir))} && {shlex.join(command)} < prompt ({len(text)} chars)")
        return 0, results
    workdir.mkdir(parents=True, exist_ok=True)
    thread_id: str | None = None
    for item in todo:
        text, refs = inputs[item.id]
        staged = stage_references(workdir, item, refs)
        result_file = workdir / f"result-{item.id}.json"
        command = exec_command(codex, workdir, result_file, staged, model, thread_id)
        prompt = build_prompt(batch, len(todo), item, text, refs, first=thread_id is None)
        (workdir / f"prompt-{item.id}.txt").write_text(prompt, encoding="utf-8")
        started = datetime.datetime.now()
        code, stdout, stderr, timed_out = run_codex(command, prompt, workdir, timeout)
        seen_thread, codex_text = parse_events(stdout)
        (workdir / f"events-{item.id}.jsonl").write_text(stdout, encoding="utf-8")
        (workdir / f"stderr-{item.id}.txt").write_text(stderr, encoding="utf-8")
        result = judge(item, workdir, result_file, code, timed_out, codex_text)
        cropped = crop_padded(item, workdir, result) if crop_band and result.outcome == "wrong-aspect" else None
        result.seconds = (datetime.datetime.now() - started).total_seconds()
        result.commands = [shlex.join(command)]
        if thread_id is None:
            thread_id = seen_thread  # a session that failed before thread.started leaves the next item to start a session of its own
        result.thread_id = seen_thread or thread_id
        if result.outcome == "failed" and stderr.strip():
            result.detail += f": {stderr.strip()[-300:]}"
        if result.outcome == "ok":
            target = local_root / item.outbox_file
            target.parent.mkdir(parents=True, exist_ok=True)
            temp = target.with_name(target.name + ".part")
            shutil.copyfile(cropped or workdir / f"{item.id}.png", temp)
            os.replace(temp, target)
        write_provenance(local_root, batch, item, result, text, refs, version, result.thread_id, stamp)
        size = f" {result.pixel_size[0]} x {result.pixel_size[1]} px ({result.pixel_size[0] * result.pixel_size[1] / 1e6:.2f} MP)" if result.pixel_size else ""
        shown = result.detail if result.detail and (result.outcome != "ok" or result.crop) else ""
        out(f"ITEM {item.id}: {result.outcome}{size} in {result.seconds:.0f} s{': ' + shown if shown else ''}")
        results.append(result)
    (workdir / "summary.json").write_text(json.dumps([r.__dict__ for r in results], indent=2) + "\n", encoding="utf-8")
    failed = [r for r in results if r.outcome not in ("ok", "skipped")]
    out(f"CODEX BATCH {batch}: {sum(r.outcome == 'ok' for r in results)} ok, {len(failed)} not ok, {sum(r.outcome == 'skipped' for r in results)} skipped; outbox {local_root / 'outbox' / batch}")
    return (1 if failed else 0), results


def parse_pair(text: str, separators: str) -> list[int]:
    found = re.fullmatch(rf"(\d+)[{separators}](\d+)", text.strip())
    if not found or not all(int(v) > 0 for v in found.groups()):
        raise BatchError(f"{text!r} is not two positive numbers")
    return [int(v) for v in found.groups()]


def crop_command(image: Path, out_file: Path, size: str, aspect: str | None, fuzz: float) -> int:
    """`crop`: one padded canvas (for example one left in a work folder) to its band at the zone size, with a provenance sidecar `OUT.crop.json`."""
    want_size = parse_pair(size, "xX,")
    want = parse_pair(aspect, ":xX") if aspect else want_size
    image_archive.warn_if_ephemeral(out_file, "codex_batch.py crop")
    try:
        canvas = png_size(image)
        report = band_crop.crop_file(image, out_file, canvas, want, want_size, fuzz)
    except (band_crop.CropError, ValueError) as error:
        print(f"CODEX CROP FAIL: {error}")
        return 1
    final = png_size(out_file)
    if not aspect_within(final, want):
        print(f"CODEX CROP FAIL: the result is {final[0]} x {final[1]}, not within 1 % of {want[0]}:{want[1]}")
        return 1
    sidecar = out_file.with_name(out_file.stem + ".crop.json")
    sidecar.write_text(json.dumps({"date": datetime.datetime.now(datetime.timezone.utc).date().isoformat(), "source": image.name, "image": out_file.name,
                                   "asked_size": want_size, "asked_aspect": want, "raw_pixel_size": list(canvas), "crop": report}, indent=2) + "\n", encoding="utf-8")
    print(f"CODEX CROP ok: {out_file.name} {final[0]} x {final[1]}: {band_crop.describe(report)}; record {sidecar.name}")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("check", "run"):
        p = sub.add_parser(name)
        p.add_argument("--codex", default="codex", help="the Codex CLI executable")
        if name == "run":
            p.add_argument("batch")
            p.add_argument("--local-root", type=Path)
            p.add_argument("--only", help="comma separated item ids (a small sample)")
            p.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT, help="seconds per item")
            p.add_argument("--overwrite", action="store_true", help="replace an outbox file that already exists")
            p.add_argument("--model", help=f"override the pinned model {DEFAULT_MODEL} (only with the owner's approval)")
            p.add_argument("--dry-run", action="store_true", help="print the commands, send nothing")
            p.add_argument("--crop-band", action="store_true", help="crop a canvas with bars (extreme aspects) to its band instead of refusing it (needs ImageMagick)")
    crop = sub.add_parser("crop", help="crop one padded canvas to its band and resize it to the zone")
    crop.add_argument("image", type=Path)
    crop.add_argument("--size", required=True, help="the zone size WxH the band is resized to")
    crop.add_argument("--aspect", help="the asked aspect W:H (default: the aspect of --size)")
    crop.add_argument("--out", type=Path, required=True)
    crop.add_argument("--fuzz", type=float, default=band_crop.DEFAULT_FUZZ, help="trim fuzz in percent")
    args = parser.parse_args(argv)
    try:
        if args.command == "crop":
            return crop_command(args.image, args.out, args.size, args.aspect, args.fuzz)
        if args.command == "check":
            print(f"PRECHECK ok: {precheck(args.codex)}, image_generation on, logged in")
            return 0
        local = args.local_root or prompt_library.default_local_root()
        only = [name for name in args.only.split(",") if name] if args.only is not None else None
        if only is not None and not only:
            raise BatchError("--only names no item")
        code, _ = run_batch(local, args.batch, args.codex, only, args.timeout, args.overwrite, args.model, args.dry_run, crop_band=args.crop_band)
        return code
    except BatchError as error:
        print(f"CODEX BATCH FAIL: {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
