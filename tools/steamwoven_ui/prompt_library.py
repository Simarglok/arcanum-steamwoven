"""ARC-RES-2455: the text prompt library of the Steamwoven kit and its inbox/outbox handoff.

Prompts are text only (TRD SWI-GEN-3, docs/ui/steamwoven-interface-trd.md); images, references and results stay under the ignored
`local/steamwoven-ui/` (SWI-PRV-1). Codex image generation runs through the owner's subscription Codex CLI (SWI-GEN-7): this tool prepares the batch inbox,
`codex_batch.py` runs it and writes the outbox with its provenance records (SWI-GEN-6); `provenance` below records an outbox the owner filled by hand. Standard library only; the layout references use ImageMagick `magick`.

    python3 -B tools/steamwoven_ui/prompt_library.py check
    python3 -B tools/steamwoven_ui/prompt_library.py show ID [--part-id P --part-note N --width W --height H]
    python3 -B tools/steamwoven_ui/prompt_library.py refs [--shots DIR] [--out DIR]
    python3 -B tools/steamwoven_ui/prompt_library.py inbox BATCH [--local-root DIR]
    python3 -B tools/steamwoven_ui/prompt_library.py provenance BATCH --generator NAME --generator-version V [--run-by owner] [--local-root DIR]
    python3 -B tools/steamwoven_ui/prompt_library.py self-test
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import re
import shutil
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROMPTS = HERE / "prompts"
ROOT = HERE.parents[1]
TRD = ROOT / "docs" / "ui" / "steamwoven-interface-trd.md"

KINDS = ("material", "board", "backdrop", "cap", "hardware", "plate", "glyph", "state", "sheet", "grid", "picture", "gauge", "decor", "cursor")
GROUPS = ("context", "kind", "material")
PLACEHOLDER = re.compile(r"\{([a-z_]+)\}")
MASTER_GRID = 6  # SWI-LAY-7: every master length is a multiple of 6 px
MASTER_WIDTH, MASTER_HEIGHT = 2880, 2160


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def load(prompts_dir: Path = PROMPTS) -> dict:
    return read_json(prompts_dir / "library.json")


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def trd_kinds(trd_text: str) -> list[str]:
    """The kind names of the asset kit table in the TRD (the drift guard of the KINDS constant)."""
    kinds: list[str] = []
    in_table = False
    for line in trd_text.splitlines():
        if line.startswith("| Kind | What"):
            in_table = True
            continue
        if in_table:
            if not line.startswith("|"):
                break
            cell = line.split("|")[1].strip()
            if cell and not set(cell) <= {"-", " "}:
                kinds.append(cell)
    return kinds


def check(library: dict, prompts_dir: Path = PROMPTS, trd_text: str | None = None) -> list[str]:
    """Problems of the library; empty means valid."""
    problems: list[str] = []
    prompts = library.get("prompts", [])
    ids = [p.get("id") for p in prompts]
    for dup in sorted({i for i in ids if ids.count(i) > 1}):
        problems.append(f"duplicate prompt id {dup}")
    slots = set(library.get("reference_slots", {}))
    allowed = set(library.get("placeholders", []))
    common = prompts_dir / library.get("common", "common.txt")
    texts = {"common": common.read_text(encoding="utf-8") if common.is_file() else None}
    if texts["common"] is None:
        problems.append(f"common file {common.name} missing")
    for p in prompts:
        pid = p.get("id", "?")
        if p.get("group") not in GROUPS:
            problems.append(f"{pid}: unknown group {p.get('group')}")
        if p.get("kind") is not None and p["kind"] not in KINDS:
            problems.append(f"{pid}: unknown kind {p['kind']}")
        if p.get("group") == "kind" and p.get("kind") is None:
            problems.append(f"{pid}: a kind prompt needs a kind")
        for ref in p.get("references", []):
            if ref not in slots:
                problems.append(f"{pid}: unknown reference slot {ref}")
        size = p.get("size")
        if size is not None:
            if len(size) != 2:
                problems.append(f"{pid}: size must have two entries")
            else:
                for side in size:
                    if side is not None and not isinstance(side, int):
                        problems.append(f"{pid}: size side {side!r} is not an integer")
                    elif side is not None and side % MASTER_GRID:
                        problems.append(f"{pid}: size side {side} is not a multiple of {MASTER_GRID}")
        if p.get("group") == "context" and p.get("size") != [MASTER_WIDTH, MASTER_HEIGHT]:
            problems.append(f"{pid}: a context image is {MASTER_WIDTH} x {MASTER_HEIGHT}")
        path = prompts_dir / p.get("file", "")
        if not path.is_file():
            problems.append(f"{pid}: file {p.get('file')} missing")
            continue
        text = path.read_text(encoding="utf-8")
        if not text.strip():
            problems.append(f"{pid}: empty prompt")
        if not text.isascii():
            problems.append(f"{pid}: non-ASCII text (prompts are plain ASCII)")
        for name in sorted(set(PLACEHOLDER.findall(text)) - allowed):
            problems.append(f"{pid}: unknown placeholder {{{name}}}")
    if texts["common"] is not None and not texts["common"].isascii():
        problems.append("common: non-ASCII text")
    for pid in library.get("context_screens", []):
        if pid not in ids:
            problems.append(f"context screen {pid} has no prompt")
    listed = {p.get("file") for p in prompts}
    for path in sorted(prompts_dir.glob("*.txt")):
        if path.name != library.get("common", "common.txt") and path.name not in listed:
            problems.append(f"orphan prompt file {path.name}")
    covered_screens = {p["id"] for p in prompts if p.get("group") == "context"}
    for pid in sorted(covered_screens - set(library.get("context_screens", []))):
        problems.append(f"context prompt {pid} is not in context_screens")
    covered = {p.get("kind") for p in prompts if p.get("group") == "kind"}
    for kind in KINDS:
        if kind not in covered and kind != "material":
            problems.append(f"kind {kind} has no prompt")
    if "kind-material" not in ids:
        problems.append("kind material has no prompt")
    for batch, spec in library.get("batches", {}).items():
        for pid in spec.get("prompts", []):
            if pid not in ids:
                problems.append(f"batch {batch}: unknown prompt {pid}")
    if trd_text is not None and trd_kinds(trd_text) != list(KINDS):
        problems.append(f"TRD asset kit kinds {trd_kinds(trd_text)} differ from KINDS {list(KINDS)}")
    return problems


def compose(library: dict, prompt_id: str, values: dict[str, str] | None = None, prompts_dir: Path = PROMPTS) -> str:
    """The full text for one generation: the common rules, the prompt, the reference list; placeholders filled."""
    entry = next((p for p in library["prompts"] if p["id"] == prompt_id), None)
    if entry is None:
        raise KeyError(f"unknown prompt id {prompt_id}")
    values = dict(values or {})
    size = entry.get("size") or [None, None]
    values.setdefault("width", str(size[0]) if size[0] else "(see part note)")
    values.setdefault("height", str(size[1]) if size[1] else "(see part note)")
    values.setdefault("part_id", entry["id"])
    values.setdefault("part_note", entry["title"])
    body = (prompts_dir / entry["file"]).read_text(encoding="utf-8").strip()
    for name in set(PLACEHOLDER.findall(body)):
        body = body.replace("{" + name + "}", values.get(name, "{" + name + "}"))
    common = (prompts_dir / library["common"]).read_text(encoding="utf-8").strip()
    refs = "\n".join(f"- {slot.upper()}: {library['reference_slots'][slot]}" for slot in entry.get("references", []))
    return f"{common}\n\n{body}\n\nREFERENCE SLOTS FOR THIS PROMPT\n{refs}\n"


def main_checkout(start: Path = ROOT) -> Path:
    """The main checkout that owns `local/` (git ignores it, so a worktree has none of its own)."""
    out = subprocess.run(["git", "rev-parse", "--git-common-dir"], cwd=start, capture_output=True, text=True, check=True).stdout.strip()
    common = (start / out).resolve()
    return common.parent


def default_local_root() -> Path:
    return main_checkout() / "local" / "steamwoven-ui"


def png_size(path: Path) -> tuple[int, int]:
    head = path.read_bytes()[:24]
    if head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        raise ValueError(f"{path.name} is not a PNG")
    return struct.unpack(">II", head[16:24])


def reference_files(local_root: Path, prompt_id: str, slot: str, batches: dict) -> list[Path]:
    """Where the reference of a slot lives for the prompts of the style anchors (their own screen or the approved contexts)."""
    if slot == "original":
        return sorted((local_root / "refs").glob(f"{prompt_id}-*.png"))
    if slot == "context":
        return sorted((local_root / "outbox" / "style-anchors-contexts").glob("ctx-*.png"))
    return []


def build_inbox(library: dict, batch: str, local_root: Path, prompts_dir: Path = PROMPTS) -> dict:
    spec = library.get("batches", {}).get(batch)
    if spec is None:
        raise KeyError(f"unknown batch {batch}")
    inbox = local_root / "inbox" / batch
    missing = [f"{pid}: reference {slot}" for pid in spec["prompts"]
               for slot in next(p for p in library["prompts"] if p["id"] == pid).get("references", [])
               if not reference_files(local_root, pid, slot, library["batches"])]
    if missing:
        raise FileNotFoundError("missing references: " + "; ".join(missing))
    (inbox / "prompts").mkdir(parents=True, exist_ok=True)
    items = []
    for pid in spec["prompts"]:
        entry = next(p for p in library["prompts"] if p["id"] == pid)
        text = compose(library, pid, prompts_dir=prompts_dir)
        (inbox / "prompts" / f"{pid}.txt").write_text(text, encoding="utf-8")
        refs = []
        for slot in entry.get("references", []):
            found = reference_files(local_root, pid, slot, library["batches"])
            target = inbox / "refs" / pid
            for src in found:
                target.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(src, target / src.name)
                refs.append({"slot": slot, "file": f"refs/{pid}/{src.name}", "sha256": sha256_file(src)})
        items.append({"id": pid, "title": entry["title"], "prompt_file": f"prompts/{pid}.txt", "prompt_sha256": sha256_text(text),
                      "size": entry.get("size"), "references": refs, "outbox_file": f"outbox/{batch}/{pid}.png"})
    (inbox / "batch.json").write_text(json.dumps({"batch": batch, "after": spec.get("after"), "items": items}, indent=2) + "\n", encoding="utf-8")
    lines = [f"Batch {batch}: {len(items)} image(s). Run it with: python3 -B tools/steamwoven_ui/codex_batch.py run {batch} (the owner's Codex subscription, SWI-GEN-7).", "",
             "By hand instead: for each item give Codex the prompt text and the listed reference images, then save the result as the outbox file name.",
             f"The results go into {local_root / 'outbox' / batch}/.", ""]
    for item in items:
        size = item["size"] or [None, None]
        shown = " x ".join(str(side) if side else "(see part note)" for side in size)
        lines.append(f"- {item['id']}: {item['title']}; size {shown}; prompt {item['prompt_file']}; "
                     f"references {', '.join(r['file'] for r in item['references']) or 'none'}; result outbox/{batch}/{item['id']}.png")
    (inbox / "README.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return {"inbox": str(inbox), "items": len(items)}


def write_provenance(library: dict, batch: str, local_root: Path, generator: str, version: str, run_by: str,
                     date: str | None = None) -> list[str]:
    """One record per image in the batch's outbox (SWI-GEN-6): generator, version, date, prompt id and hash, reference ids, who ran it."""
    inbox = local_root / "inbox" / batch
    manifest = read_json(inbox / "batch.json")
    day = date or datetime.date.today().isoformat()
    out_dir = local_root / "provenance" / batch
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []
    for item in manifest["items"]:
        image = local_root / item["outbox_file"]
        if not image.is_file():
            raise FileNotFoundError(f"{item['id']}: result {item['outbox_file']} not in the outbox")
        width, height = png_size(image)
        want = item.get("size") or [None, None]
        # The generator may return a smaller size of the same aspect (common rule 7); the fit tool brings it to the master size.
        if width <= 0 or height <= 0:
            raise ValueError(f"{item['id']}: result has no size ({width} x {height})")
        if want[0] and want[1] and abs(width * want[1] - height * want[0]) > max(want):
            raise ValueError(f"{item['id']}: result is {width} x {height}, the prompt asks for the aspect of {want[0]} x {want[1]}")
        record = {"image": item["outbox_file"], "image_sha256": sha256_file(image), "image_size": [width, height],
                  "prompt_id": item["id"], "prompt_sha256": item["prompt_sha256"],
                  "references": [{"file": r["file"], "sha256": r["sha256"]} for r in item["references"]],
                  "generator": generator, "generator_version": version, "date": day, "run_by": run_by}
        path = out_dir / f"{item['id']}.json"
        path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        written.append(str(path))
    return written


def magick_args(src: Path, spec: dict) -> list[str]:
    args = [str(src), "-crop", spec["crop"], "+repage"]
    for patch in spec.get("patch", []):
        args += ["(", "+clone", "-crop", patch["from"], "+repage", ")", "-geometry", patch["to"], "-composite"]
    if spec.get("flip"):
        args.append("-flip")
    if spec.get("resize"):
        args += ["-filter", "Lanczos", "-resize", spec["resize"]]
    return args


def build_refs(shots: Path, out: Path, prompts_dir: Path = PROMPTS) -> list[str]:
    """The 800 x 600 layout reference and its 2880 x 2160 guide of each context screen, from the census screenshots."""
    spec = read_json(prompts_dir / "references.json")
    if shutil.which("magick") is None:
        raise RuntimeError("ImageMagick `magick` is not installed")
    out.mkdir(parents=True, exist_ok=True)
    written = []
    for pid, screen in spec["screens"].items():
        native = out / f"{pid}-800x600.png"
        if "full" in screen:
            cmd = ["magick", *magick_args(shots / screen["full"]["src"], screen["full"])]
        else:
            zone = ["("]
            for piece in screen["zone"]:
                zone += ["(", *magick_args(shots / piece["src"], piece), ")"]
            zone += ["-append", ")"]
            cmd = ["magick", str(shots / spec["strips"]["top"]), *zone, str(shots / spec["strips"]["bottom"]), "-append"]
        subprocess.run([*cmd, str(native)], check=True)
        width, height = png_size(native)
        if (width, height) != (800, 600):
            raise ValueError(f"{pid}: reference is {width} x {height}, expected 800 x 600")
        guide = out / f"{pid}-guide-2880x2160.png"
        subprocess.run(["magick", str(native), "-filter", "Lanczos", "-resize", "2880x2160!", str(guide)], check=True)
        written += [str(native), str(guide)]
    return written


def self_test() -> int:
    library = load()
    problems = check(library, trd_text=TRD.read_text(encoding="utf-8") if TRD.is_file() else None)
    assert not problems, problems
    with tempfile.TemporaryDirectory() as tmp:
        base = Path(tmp)
        prompts_dir = base / "prompts"
        shutil.copytree(PROMPTS, prompts_dir)
        text = compose(library, "kind-board", {"part_id": "B-1", "part_note": "top strip", "width": "2880", "height": "150"}, prompts_dir)
        assert "B-1" in text and "{part_id}" not in text and "REFERENCE SLOTS" in text
        broken = json.loads(json.dumps(library))
        broken["prompts"][0]["id"] = broken["prompts"][1]["id"]
        assert any("duplicate" in p for p in check(broken, prompts_dir))
        local = base / "local"
        for ctx in library["context_screens"]:
            (local / "refs").mkdir(parents=True, exist_ok=True)
            (local / "refs" / f"{ctx}-800x600.png").write_bytes(b"png")
        info = build_inbox(library, "style-anchors-contexts", local, prompts_dir)
        assert info["items"] == len(library["context_screens"])
        manifest = read_json(local / "inbox" / "style-anchors-contexts" / "batch.json")
        assert manifest["items"][0]["prompt_sha256"] == sha256_text((local / "inbox" / "style-anchors-contexts" / manifest["items"][0]["prompt_file"]).read_text(encoding="utf-8"))
        for item in manifest["items"]:
            target = local / item["outbox_file"]
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(b"\x89PNG\r\n\x1a\n" + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", 2880, 2160) + b"\x08\x06\x00\x00\x00")
        written = write_provenance(library, "style-anchors-contexts", local, "test generator", "1", "owner", "2026-11-19")
        assert len(written) == len(manifest["items"])
        record = read_json(Path(written[0]))
        assert record["image_size"] == [2880, 2160] and record["run_by"] == "owner"
    print("PROMPT LIBRARY self-test PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("check")
    sub.add_parser("self-test")
    show = sub.add_parser("show")
    show.add_argument("id")
    for name in ("part-id", "part-note", "width", "height"):
        show.add_argument(f"--{name}")
    refs = sub.add_parser("refs")
    refs.add_argument("--shots", type=Path)
    refs.add_argument("--out", type=Path)
    inbox = sub.add_parser("inbox")
    inbox.add_argument("batch")
    inbox.add_argument("--local-root", type=Path)
    prov = sub.add_parser("provenance")
    prov.add_argument("batch")
    prov.add_argument("--generator", required=True)
    prov.add_argument("--generator-version", required=True)
    prov.add_argument("--run-by", default="owner")
    prov.add_argument("--local-root", type=Path)
    args = parser.parse_args(argv)
    try:
        return run(args)
    except (KeyError, FileNotFoundError, ValueError, RuntimeError) as error:
        print(f"PROMPT LIBRARY FAIL: {error}")
        return 1


def run(args: argparse.Namespace) -> int:
    library = load()
    if args.command == "self-test":
        return self_test()
    if args.command == "check":
        problems = check(library, trd_text=TRD.read_text(encoding="utf-8"))
        for problem in problems:
            print(f"PROBLEM {problem}")
        print(f"PROMPT LIBRARY check {'FAIL' if problems else 'PASS'}: {len(library['prompts'])} prompts")
        return 1 if problems else 0
    if args.command == "show":
        values = {k: v for k, v in (("part_id", args.part_id), ("part_note", args.part_note), ("width", args.width), ("height", args.height)) if v}
        for side in ("width", "height"):
            if side in values and (not values[side].isdigit() or int(values[side]) % MASTER_GRID):
                raise ValueError(f"--{side} must be a whole number of pixels, a multiple of {MASTER_GRID}")
        text = compose(library, args.id, values)
        print(text)
        print(f"sha256 {sha256_text(text)}")
        return 0
    local = (getattr(args, "local_root", None) or default_local_root())
    if args.command == "refs":
        shots = args.shots or local / "census" / "shots"
        written = build_refs(shots, args.out or local / "refs")
        print(f"wrote {len(written)} reference files")
        return 0
    if args.command == "inbox":
        info = build_inbox(library, args.batch, local)
        print(f"INBOX {info['inbox']}: {info['items']} items")
        return 0
    written = write_provenance(library, args.batch, local, args.generator, args.generator_version, args.run_by)
    print(f"wrote {len(written)} provenance records")
    return 0


if __name__ == "__main__":
    sys.exit(main())
