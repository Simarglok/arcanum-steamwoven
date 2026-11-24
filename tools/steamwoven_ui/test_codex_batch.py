"""ARC-DEV-3001: unit tests of the Codex batch runner with a fake `codex` executable (a small Python script; synthetic prompts and tiny images,
no Codex quota, no game data)."""
from __future__ import annotations

import hashlib
import json
import os
import shutil
import stat
import sys
import tempfile
import unittest
from pathlib import Path

from tools.steamwoven_ui import band_crop, pngio
from tools.steamwoven_ui import codex_batch as cb

FAKE = r'''#!{python}
import json, os, re, struct, sys, time, zlib

args = sys.argv[1:]
plan = json.load(open(os.environ["FAKE_PLAN"]))
log = open(os.environ["FAKE_LOG"], "a")


def png(w, h):
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))
    raw = b"".join(b"\x00" + b"\x80\x40\x20\xff" * w for _ in range(h))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def padded_png(w, h, bar_rows, bar):
    """A canvas of `h` rows with `bar_rows` bar rows above and below the band: a light anti-aliasing line, art rows, a light line (ARC-UI-3083)."""
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))
    art_rows = h - 2 * bar_rows
    rows = [bar] * bar_rows + [(200, 200, 200, 255)] + [(60, 40, 20, 255)] * (art_rows - 2) + [(200, 200, 200, 255)] + [bar] * bar_rows
    raw = b"".join(b"\x00" + bytes(row) * w for row in rows)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def say(event):
    print(json.dumps(event), flush=True)


if args[:1] == ["--version"]:
    print("codex-cli 9.9.9")
elif args[:2] == ["features", "list"]:
    print("view_image                               stable             true")
    print("image_generation                         stable             " + ("false" if plan.get("feature_off") else "true"))
elif args[:2] == ["login", "status"]:
    text = plan.get("login", "Logged in using ChatGPT")
    print(text)
    sys.exit(0 if "Logged in" in text else 1)
elif args[:1] == ["exec"]:
    prompt = sys.stdin.read()
    resume = args[1:2] == ["resume"]
    log.write(json.dumps({"argv": args, "cwd": os.getcwd(), "has_key": any(k.startswith("OPENAI_") or k == "CODEX_API_KEY" for k in os.environ), "prompt": prompt}) + "\n")
    log.flush()
    out_file = args[args.index("-o") + 1]
    item = re.search(r"^ITEM: (\S+)", prompt, re.M).group(1)
    thread = "thread-0001"
    if resume:
        if args[-2] != thread:
            print("unknown session", file=sys.stderr)
            sys.exit(1)
    spec = plan["items"][item]
    mode = spec["mode"]
    if mode == "early_fail":
        print("no session", file=sys.stderr)
        sys.exit(2)
    say({"type": "thread.started", "thread_id": thread})
    say({"type": "turn.started"})
    if mode == "hang":
        open(os.environ["FAKE_LOG"] + ".pid", "w").write(str(os.getpid()))
        time.sleep(60)
    if mode == "exit1":
        print("boom", file=sys.stderr)
        sys.exit(3)
    if mode in ("ok", "nofile_ok", "aspect", "notpng", "badjson", "truncated", "padded"):
        if mode == "padded":
            open(item + ".png", "wb").write(padded_png(420, 153, 64, tuple(spec.get("bar", [255, 255, 255, 255]))))
        if mode == "ok" or mode == "aspect":
            w, h = spec.get("size", [64, 48])
            open(item + ".png", "wb").write(png(w, h))
        if mode == "truncated":
            open(item + ".png", "wb").write(png(64, 48)[:-20])
        if mode == "notpng":
            open(item + ".png", "wb").write(b"not a png at all")
        message = "I made it." if mode == "badjson" else json.dumps({"ok": True, "path": item + ".png", "error": None})
    elif mode == "nofile":
        message = json.dumps({"ok": True, "path": item + ".png", "error": None})
    else:
        message = json.dumps({"ok": False, "path": item + ".png", "error": "I cannot create this image: policy"})
    say({"type": "item.completed", "item": {"id": "item_0", "type": "agent_message", "text": message}})
    open(out_file, "w").write(message)
    say({"type": "turn.completed", "usage": {}})
else:
    sys.exit(64)
'''


def tree_hashes(root: Path) -> dict[str, str]:
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob("*")) if p.is_file()}


class RunnerCase(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        base = Path(self.tmp.name)
        self.local = base / "local"
        self.log = base / "fake.log"
        self.plan_file = base / "plan.json"
        self.fake = base / "codex"
        self.fake.write_text(FAKE.replace("{python}", sys.executable), encoding="utf-8")
        self.fake.chmod(self.fake.stat().st_mode | stat.S_IXUSR)
        os.environ["FAKE_PLAN"], os.environ["FAKE_LOG"] = str(self.plan_file), str(self.log)
        self.addCleanup(lambda: [os.environ.pop(k, None) for k in ("FAKE_PLAN", "FAKE_LOG", "OPENAI_API_KEY")])
        self.plan({})

    def plan(self, items: dict, **extra) -> None:
        self.plan_file.write_text(json.dumps({"items": items, **extra}), encoding="utf-8")

    def make_batch(self, ids=("a", "b"), size=(64, 48), refs: bool = False, batch: str = "t-batch") -> Path:
        inbox = self.local / "inbox" / batch
        (inbox / "prompts").mkdir(parents=True, exist_ok=True)
        items = []
        for pid in ids:
            text = f"Draw the synthetic item {pid}.\n"
            (inbox / "prompts" / f"{pid}.txt").write_text(text, encoding="utf-8")
            entry = {"id": pid, "title": pid, "prompt_file": f"prompts/{pid}.txt", "prompt_sha256": hashlib.sha256(text.encode()).hexdigest(),
                     "size": list(size), "references": [], "outbox_file": f"outbox/{batch}/{pid}.png"}
            if refs:
                (inbox / "refs" / pid).mkdir(parents=True, exist_ok=True)
                (inbox / "refs" / pid / "r.png").write_bytes(b"\x89PNG\r\n\x1a\nref-" + pid.encode())
                entry["references"] = [{"slot": "context", "file": f"refs/{pid}/r.png", "sha256": hashlib.sha256(b"\x89PNG\r\n\x1a\nref-" + pid.encode()).hexdigest()}]
            items.append(entry)
        (inbox / "batch.json").write_text(json.dumps({"batch": batch, "items": items}), encoding="utf-8")
        return inbox

    def run_batch(self, **kwargs):
        lines: list[str] = []
        code, results = cb.run_batch(self.local, "t-batch", str(self.fake), out=lines.append, **kwargs)
        return code, {r.id: r for r in results}, lines

    def calls(self) -> list[dict]:
        return [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []


class SuccessAndOrder(RunnerCase):
    def test_two_items_one_session_resumed_and_recorded(self) -> None:
        inbox = self.make_batch(refs=True)
        before = tree_hashes(inbox)
        self.plan({"a": {"mode": "ok"}, "b": {"mode": "ok", "size": [128, 96]}})
        os.environ["OPENAI_API_KEY"] = "sk-must-not-pass"
        os.environ["CODEX_API_KEY"] = "sk-must-not-pass-either"
        self.addCleanup(os.environ.pop, "CODEX_API_KEY", None)
        code, results, _ = self.run_batch()
        self.assertEqual(0, code)
        self.assertEqual(["ok", "ok"], [results["a"].outcome, results["b"].outcome])
        calls = self.calls()
        self.assertEqual(2, len(calls))
        first, second = calls
        self.assertEqual(["exec"], first["argv"][:1])
        for flag in ("--skip-git-repo-check", "--json", "--enable", "image_generation", "--sandbox", "workspace-write", "-C"):
            self.assertIn(flag, first["argv"])
        self.assertEqual("-", first["argv"][-1])  # the prompt comes on stdin
        self.assertNotIn("--last", first["argv"] + second["argv"])
        self.assertNotIn("--ephemeral", first["argv"] + second["argv"])
        self.assertEqual(["exec", "resume"], second["argv"][:2])
        self.assertEqual("thread-0001", second["argv"][-2])  # the thread id of the first item's thread.started event
        self.assertEqual(first["cwd"], second["cwd"])
        for call in (first, second):  # the pinned model and effort reach the real argv of the first and of the resumed call
            self.assertEqual("gpt-6.1-sol", call["argv"][call["argv"].index("-m") + 1])
            self.assertIn('model_reasoning_effort="medium"', call["argv"])
        self.assertFalse(first["has_key"] or second["has_key"])  # no API key reaches the session
        self.assertIn("SESSION RULES", first["prompt"])
        self.assertNotIn("SESSION RULES", second["prompt"])
        self.assertTrue(any(arg.startswith("--image=") for arg in first["argv"]))
        self.assertTrue(any(arg.startswith("--image=") for arg in second["argv"]))  # references are attached on a resumed item too
        self.assertEqual("-", second["argv"][-1])
        self.assertIn("ASPECT: 64:48", first["prompt"])
        self.assertEqual(before, tree_hashes(inbox))  # the inbox is never touched
        outbox = self.local / "outbox" / "t-batch"
        self.assertEqual((64, 48), cb.png_size(outbox / "a.png"))
        self.assertEqual((128, 96), cb.png_size(outbox / "b.png"))
        record = json.loads((outbox / "a.provenance.json").read_text())
        self.assertEqual("codex-cli 9.9.9 image_gen", record["generator"])
        self.assertEqual("thread-0001", record["thread_id"])
        self.assertEqual([64, 48], record["pixel_size"])
        self.assertEqual("ok", record["outcome"])
        self.assertEqual(hashlib.sha256(b"Draw the synthetic item a.\n").hexdigest(), record["prompt_sha256"])
        self.assertEqual(hashlib.sha256((outbox / "a.png").read_bytes()).hexdigest(), record["image_sha256"])
        self.assertEqual(1, len(record["references"]))
        self.assertTrue(len(record["references"][0]["sha256"]) == 64)

    def test_only_runs_a_sample_and_an_existing_outbox_file_is_kept(self) -> None:
        self.make_batch()
        self.plan({"a": {"mode": "ok"}, "b": {"mode": "ok"}})
        code, results, _ = self.run_batch(only=["b"])
        self.assertEqual(0, code)
        self.assertEqual(1, len(self.calls()))
        self.assertNotIn("a", results)
        existing = self.local / "outbox" / "t-batch" / "b.png"
        keep = existing.read_bytes()
        code, results, _ = self.run_batch(only=["b"])
        self.assertEqual("skipped", results["b"].outcome)
        self.assertEqual(1, len(self.calls()))
        self.assertEqual(keep, existing.read_bytes())
        code, results, _ = self.run_batch(only=["b"], overwrite=True)
        self.assertEqual("ok", results["b"].outcome)
        self.assertEqual(2, len(self.calls()))


class FailuresDoNotStopTheBatch(RunnerCase):
    def outcomes(self, first_mode: str, **kw) -> tuple[int, dict, list[str]]:
        self.make_batch()
        self.plan({"a": {"mode": first_mode, **kw}, "b": {"mode": "ok"}})
        (self.local / "outbox" / "t-batch" / "b.png").unlink(missing_ok=True)
        return self.run_batch()

    def test_refusal_is_logged_with_the_text_and_the_next_item_resumes(self) -> None:
        code, results, lines = self.outcomes("refuse")
        self.assertEqual(1, code)
        self.assertEqual("refused", results["a"].outcome)
        self.assertIn("policy", results["a"].detail)
        self.assertEqual("ok", results["b"].outcome)
        outbox = self.local / "outbox" / "t-batch"
        self.assertFalse((outbox / "a.png").exists())
        record = json.loads((outbox / "a.provenance.json").read_text())
        self.assertEqual("refused", record["outcome"])
        self.assertIn("policy", record["refusal_text"])
        self.assertEqual(["exec", "resume"], self.calls()[1]["argv"][:2])
        self.assertTrue(any("ITEM a: refused" in line for line in lines))

    def test_missing_file(self) -> None:
        code, results, _ = self.outcomes("nofile")
        self.assertEqual("missing-file", results["a"].outcome)
        self.assertFalse((self.local / "outbox" / "t-batch" / "a.png").exists())

    def test_not_a_png(self) -> None:
        code, results, _ = self.outcomes("notpng")
        self.assertEqual("not-png", results["a"].outcome)

    def test_wrong_aspect_is_rejected_and_one_percent_is_accepted(self) -> None:
        code, results, _ = self.outcomes("aspect", size=[64, 64])
        self.assertEqual("wrong-aspect", results["a"].outcome)
        self.assertEqual([64, 64], results["a"].pixel_size)
        self.assertFalse((self.local / "outbox" / "t-batch" / "a.png").exists())
        self.assertTrue(cb.aspect_within((3840, 2160), [16, 9]))
        self.assertTrue(cb.aspect_within((1920, 1088), [16, 9]))
        self.assertFalse(cb.aspect_within((1920, 1000), [16, 9]))

    def test_exit_code_and_bad_json_are_failures(self) -> None:
        code, results, _ = self.outcomes("exit1")
        self.assertEqual("failed", results["a"].outcome)
        self.assertIn("boom", results["a"].detail)
        code, results, _ = self.outcomes("badjson")
        self.assertEqual("bad-result", results["a"].outcome)

    def test_timeout_stops_codex_and_the_next_item_resumes_the_session(self) -> None:
        self.make_batch()
        self.plan({"a": {"mode": "hang"}, "b": {"mode": "ok"}})
        code, results, _ = self.run_batch(timeout=2)
        self.assertEqual(1, code)
        self.assertEqual("timeout", results["a"].outcome)
        self.assertEqual("thread-0001", results["a"].thread_id)  # thread.started came before the hang
        pid = int(Path(str(self.log) + ".pid").read_text())
        with self.assertRaises(ProcessLookupError):
            os.kill(pid, 0)  # the stopped codex is really gone
        self.assertEqual("ok", results["b"].outcome)
        self.assertEqual(["exec", "resume"], self.calls()[1]["argv"][:2])


class MoreFailureShapes(RunnerCase):
    def test_truncated_png_is_not_accepted(self) -> None:
        self.make_batch()
        self.plan({"a": {"mode": "truncated"}, "b": {"mode": "ok"}})
        code, results, _ = self.run_batch()
        self.assertEqual("not-png", results["a"].outcome)
        self.assertFalse((self.local / "outbox" / "t-batch" / "a.png").exists())

    def test_a_first_item_without_a_session_makes_the_next_start_a_fresh_one_with_the_rules(self) -> None:
        self.make_batch()
        self.plan({"a": {"mode": "early_fail"}, "b": {"mode": "ok"}})
        code, results, _ = self.run_batch()
        self.assertEqual("failed", results["a"].outcome)
        self.assertEqual("ok", results["b"].outcome)
        calls = self.calls()
        self.assertEqual("exec", calls[1]["argv"][0])
        self.assertNotEqual("resume", calls[1]["argv"][1])
        self.assertIn("SESSION RULES", calls[1]["prompt"])

    def test_overwrite_after_a_refusal_keeps_the_old_image_and_its_record(self) -> None:
        self.make_batch(ids=("a",))
        self.plan({"a": {"mode": "ok"}})
        self.run_batch()
        outbox = self.local / "outbox" / "t-batch"
        old = (outbox / "a.png").read_bytes()
        record = (outbox / "a.provenance.json").read_text()
        self.plan({"a": {"mode": "refuse"}})
        code, results, _ = self.run_batch(overwrite=True)
        self.assertEqual("refused", results["a"].outcome)
        self.assertEqual(old, (outbox / "a.png").read_bytes())
        self.assertEqual(record, (outbox / "a.provenance.json").read_text())
        self.assertEqual(1, len(list(outbox.glob("a.provenance.*.refused.json"))))


class BatchFileIsConfinedAndComplete(RunnerCase):
    def edit(self, change) -> None:
        inbox = self.make_batch(refs=True)
        manifest = json.loads((inbox / "batch.json").read_text())
        change(manifest, inbox)
        (inbox / "batch.json").write_text(json.dumps(manifest))
        with self.assertRaises(cb.BatchError):
            self.run_batch()
        self.assertEqual([], self.calls())

    def test_prompt_and_reference_paths_cannot_leave_the_inbox(self) -> None:
        secret = Path(self.tmp.name) / "secret.txt"
        secret.write_text("secret", encoding="utf-8")
        self.edit(lambda m, i: m["items"][0].update(prompt_file="../../../secret.txt", prompt_sha256=hashlib.sha256(b"secret").hexdigest()))
        self.edit(lambda m, i: m["items"][0]["references"][0].update(file=str(secret), sha256=hashlib.sha256(b"secret").hexdigest()))

    def test_hashes_are_required(self) -> None:
        self.edit(lambda m, i: m["items"][0].pop("prompt_sha256"))
        self.edit(lambda m, i: m["items"][0]["references"][0].pop("sha256"))
        self.edit(lambda m, i: m["items"][0]["references"][0].update(sha256="0" * 64))

    def test_malformed_items_are_batch_errors_not_tracebacks(self) -> None:
        self.edit(lambda m, i: m["items"].append("not an object"))
        self.edit(lambda m, i: m["items"][0].update(references=["x"]))
        self.edit(lambda m, i: m["items"][0]["references"].append({"sha256": "a"}))

    def test_an_empty_only_runs_nothing(self) -> None:
        self.make_batch()
        self.assertEqual(2, cb.main(["run", "t-batch", "--local-root", str(self.local), "--codex", str(self.fake), "--only", ""]))
        self.assertEqual([], self.calls())


class StopsBeforeSending(RunnerCase):
    def assert_nothing_sent(self, message: str) -> None:
        self.make_batch()
        with self.assertRaises(cb.BatchError) as raised:
            self.run_batch()
        self.assertIn(message, str(raised.exception))
        self.assertEqual([], self.calls())
        self.assertFalse((self.local / "outbox").exists())

    def test_feature_off(self) -> None:
        self.plan({}, feature_off=True)
        self.assert_nothing_sent("image_generation")

    def test_not_logged_in(self) -> None:
        self.plan({}, login="Not logged in")
        self.assert_nothing_sent("not logged in")

    def test_api_key_login_is_refused(self) -> None:
        self.plan({}, login="Logged in using an API key - sk-***")
        self.assert_nothing_sent("API key")

    def test_missing_cli(self) -> None:
        with self.assertRaises(cb.BatchError):
            cb.precheck(str(self.fake.parent / "no-such-codex"))

    def test_prompt_changed_after_the_inbox_was_built(self) -> None:
        inbox = self.make_batch()
        (inbox / "prompts" / "a.txt").write_text("edited", encoding="utf-8")
        with self.assertRaises(cb.BatchError):
            self.run_batch()
        self.assertEqual([], self.calls())

    def test_bad_batch_files(self) -> None:
        with self.assertRaises(cb.BatchError):
            cb.load_batch(self.local, "nothing")
        with self.assertRaises(cb.BatchError):
            cb.load_batch(self.local, "../x")
        inbox = self.make_batch()
        manifest = json.loads((inbox / "batch.json").read_text())
        manifest["items"][0]["outbox_file"] = "outbox/elsewhere/a.png"
        (inbox / "batch.json").write_text(json.dumps(manifest))
        with self.assertRaises(cb.BatchError):
            cb.load_batch(self.local, "t-batch")


class DryRunAndCli(RunnerCase):
    def test_dry_run_prints_the_commands_and_writes_nothing(self) -> None:
        inbox = self.make_batch(refs=True)
        before = tree_hashes(self.local)
        self.plan({"a": {"mode": "ok"}, "b": {"mode": "ok"}})
        code, results, lines = self.run_batch(dry_run=True)
        self.assertEqual(0, code)
        self.assertEqual([], self.calls())
        self.assertEqual(before, tree_hashes(self.local))
        self.assertFalse((self.local / "codex-work").exists())
        text = "\n".join(lines)
        self.assertIn("DRY-RUN item a", text)
        self.assertIn("exec resume", text)  # item b resumes
        self.assertIn("<thread_id>", text)

    def test_cli_exit_codes(self) -> None:
        self.make_batch()
        self.plan({"a": {"mode": "ok"}, "b": {"mode": "refuse"}})
        base = ["run", "t-batch", "--local-root", str(self.local), "--codex", str(self.fake)]
        self.assertEqual(1, cb.main(base))
        self.assertEqual(2, cb.main(["run", "missing", "--local-root", str(self.local), "--codex", str(self.fake)]))
        self.assertEqual(0, cb.main(["check", "--codex", str(self.fake)]))


class BandCropGeometry(unittest.TestCase):
    """ARC-UI-3083: the pure geometry of the crop, with the numbers measured on the two real Codex canvases (2103 x 748) of #2457; no ImageMagick."""

    def test_strip_19_5_to_1_with_white_bars(self) -> None:
        plan = band_crop.plan((2103, 748), (0, 306, 2103, 115), [2880, 148])
        self.assertEqual(7, plan["shave"]["top"] + plan["shave"]["bottom"])
        self.assertEqual([2102, 108], plan["band"])  # one column cut to land on the aspect exactly
        self.assertEqual(1, plan["columns_cut"])
        self.assertLess(abs(plan["aspect_error"]), 0.001)

    def test_strip_5_to_1_with_black_bars(self) -> None:
        plan = band_crop.plan((2103, 748), (0, 141, 2103, 421), [2880, 572])
        self.assertEqual({"top": 2, "bottom": 2, "left": 0, "right": 0}, plan["shave"])
        self.assertEqual([2100, 417], plan["band"])
        self.assertLessEqual(abs(plan["aspect_error"]), band_crop.TOLERANCE)

    def test_shave_stays_inside_two_to_four_pixels_per_edge(self) -> None:
        for height in range(100, 130):
            try:
                plan = band_crop.plan((2103, 748), (0, 300, 2103, height), [2880, 148])
            except band_crop.CropError:
                continue
            self.assertTrue(all(2 <= plan["shave"][edge] <= 4 for edge in ("top", "bottom")), (height, plan["shave"]))
            self.assertLessEqual(abs(plan["aspect_error"]), band_crop.TOLERANCE)

    def test_a_band_that_is_too_tall_is_refused_not_stretched(self) -> None:
        with self.assertRaises(band_crop.CropError) as caught:
            band_crop.plan((2103, 748), (0, 141, 2103, 421), [2880, 148])
        self.assertIn("cannot reach the asked aspect", str(caught.exception))

    def test_a_band_much_wider_than_asked_is_refused_not_cut_down(self) -> None:
        with self.assertRaises(band_crop.CropError) as caught:
            band_crop.plan((420, 153), (0, 64, 420, 25), [200, 100])
        self.assertIn("much wider", str(caught.exception))

    def test_a_canvas_without_bars_is_refused(self) -> None:
        with self.assertRaises(band_crop.CropError) as caught:
            band_crop.plan((1774, 887), (0, 0, 1774, 887), [2880, 1440])
        self.assertIn("not padded", str(caught.exception))

    def test_only_wide_bands_are_cropped(self) -> None:
        with self.assertRaises(band_crop.CropError):
            band_crop.plan((748, 2103), (141, 0, 421, 2103), [1, 5])

    def test_trim_box_text(self) -> None:
        self.assertEqual((0, 306, 2103, 115), band_crop.parse_box("2103x115+0+306\n"))
        with self.assertRaises(band_crop.CropError):
            band_crop.parse_box("")


@unittest.skipUnless(shutil.which("magick"), "ImageMagick is needed")
class BandCropRun(RunnerCase):
    """ARC-UI-3083: a padded canvas from the fake Codex (420 x 153, bars of 64 rows, a 25-row dark band with a light line at each edge) asked at 20:1."""

    def padded_batch(self, **kw):
        self.make_batch(size=(200, 10))
        self.plan({"a": {"mode": "padded", **kw}, "b": {"mode": "ok", "size": [200, 10]}})
        return self.run_batch(crop_band=True)

    def test_padded_canvas_is_cropped_resized_and_recorded(self) -> None:
        code, results, lines = self.padded_batch()
        self.assertEqual("ok", results["a"].outcome)
        out = self.local / "outbox" / "t-batch" / "a.png"
        self.assertEqual((200, 10), cb.png_size(out))
        image = pngio.read(out)
        for y in (0, 9):  # the light anti-aliasing line is gone at both edges
            r, g, b, _ = image.pixel(100, y)
            self.assertLess(abs(r - 60) + abs(g - 40) + abs(b - 20), 12, (y, (r, g, b)))
        record = json.loads((self.local / "outbox" / "t-batch" / "a.provenance.json").read_text())
        self.assertEqual("ok", record["outcome"])
        self.assertEqual([420, 153], record["raw_pixel_size"])
        self.assertEqual(cb.sha256_file(out), record["image_sha256"])
        self.assertEqual(cb.sha256_file(next((self.local / "codex-work" / "t-batch").glob("*/a.png"))), record["crop"]["raw_sha256"])
        self.assertEqual(record["crop"]["cropped_sha256"], record["image_sha256"])
        self.assertTrue(record["raw_file"].endswith("/a.png"))
        self.assertLessEqual(abs(record["crop"]["aspect_error"]), band_crop.TOLERANCE)
        self.assertTrue(any("ITEM a: ok" in line and "band" in line for line in lines))

    def test_black_bars_are_trimmed_too(self) -> None:
        code, results, _ = self.padded_batch(bar=[0, 0, 0, 255])
        self.assertEqual("ok", results["a"].outcome)

    def test_without_the_option_the_canvas_stays_wrong_aspect(self) -> None:
        self.make_batch(size=(200, 10))
        self.plan({"a": {"mode": "padded"}, "b": {"mode": "ok", "size": [200, 10]}})
        code, results, _ = self.run_batch()
        self.assertEqual("wrong-aspect", results["a"].outcome)
        self.assertFalse((self.local / "outbox" / "t-batch" / "a.png").exists())

    def test_a_canvas_without_bars_stays_wrong_aspect_with_the_reason(self) -> None:
        self.make_batch(size=(64, 64))
        self.plan({"a": {"mode": "aspect", "size": [64, 48]}, "b": {"mode": "ok", "size": [64, 64]}})
        code, results, _ = self.run_batch(crop_band=True)
        self.assertEqual("wrong-aspect", results["a"].outcome)
        self.assertIn("band crop refused", results["a"].detail)
        self.assertFalse((self.local / "outbox" / "t-batch" / "a.png").exists())

    def test_crop_command_writes_the_image_and_a_record(self) -> None:
        source = Path(self.tmp.name) / "canvas.png"
        source.write_bytes(padded_png_bytes())
        out = Path(self.tmp.name) / "strip.png"
        lines: list[str] = []
        self.assertEqual(0, cb.crop_command(source, out, "200x10", None, band_crop.DEFAULT_FUZZ))
        self.assertEqual((200, 10), cb.png_size(out))
        record = json.loads((Path(self.tmp.name) / "strip.crop.json").read_text())
        self.assertEqual(cb.sha256_file(source), record["crop"]["raw_sha256"])
        self.assertEqual([420, 153], record["raw_pixel_size"])
        self.assertEqual(1, cb.crop_command(source, Path(self.tmp.name) / "no.png", "200x100", None, band_crop.DEFAULT_FUZZ))
        self.assertFalse((Path(self.tmp.name) / "no.png").exists())


def padded_png_bytes() -> bytes:
    import struct
    import zlib

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))
    rows = [(255, 255, 255, 255)] * 64 + [(200, 200, 200, 255)] + [(60, 40, 20, 255)] * 23 + [(200, 200, 200, 255)] + [(255, 255, 255, 255)] * 64
    raw = b"".join(b"\x00" + bytes(row) * 420 for row in rows)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 420, 153, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


class Helpers(unittest.TestCase):
    def test_extract_json_reads_fenced_and_chatty_replies(self) -> None:
        self.assertEqual({"ok": True}, cb.extract_json('{"ok": true}'))
        self.assertEqual({"ok": False, "error": "x"}, cb.extract_json('Sorry.\n```json\n{"ok": false, "error": "x"}\n```'))
        self.assertIsNone(cb.extract_json("done"))

    def test_events_give_the_thread_id_and_the_text(self) -> None:
        stdout = '{"type":"thread.started","thread_id":"abc"}\nnoise\n{"type":"item.completed","item":{"type":"agent_message","text":"hi"}}\n'
        self.assertEqual(("abc", "hi"), cb.parse_events(stdout))

    def test_resume_command_has_no_sandbox_or_dir_options(self) -> None:
        command = cb.exec_command("codex", Path("/w"), Path("/w/r.json"), [], None, "tid")
        self.assertEqual(["codex", "exec", "resume"], command[:3])
        self.assertNotIn("-C", command)
        self.assertNotIn("--sandbox", command)
        self.assertEqual(["tid", "-"], command[-2:])

    def test_every_command_pins_the_model_and_the_effort(self) -> None:
        for thread_id in (None, "tid"):
            command = cb.exec_command("codex", Path("/w"), Path("/w/r.json"), [], None, thread_id)
            self.assertEqual("gpt-6.1-sol", command[command.index("-m") + 1])
            self.assertIn('model_reasoning_effort="medium"', command)

    def test_model_override_replaces_the_model_only(self) -> None:
        command = cb.exec_command("codex", Path("/w"), Path("/w/r.json"), [], "other-model", None)
        self.assertEqual("other-model", command[command.index("-m") + 1])
        self.assertEqual(1, command.count("-m"))
        self.assertIn('model_reasoning_effort="medium"', command)


if __name__ == "__main__":
    unittest.main()
