---
name: steamwoven-codex-images
description: Run a ready Steamwoven interface image batch (a folder local/steamwoven-ui/inbox/<batch>/ with batch.json) through the owner's subscription Codex CLI on the Mac and read the outbox. Use only for such a batch; not for other images, other Codex work or game code.
---

# Steamwoven interface image batches through the Codex CLI

Scope: only a ready inbox `local/steamwoven-ui/inbox/<batch>/` of the Steamwoven interface kit. The project rules, the kit and the pipeline are in
the documents below; this skill only says how to drive the runner.

- Kit, generation, provenance: [TRD](../../../docs/ui/steamwoven-interface-trd.md) (SWI-KIT, SWI-GEN incl. SWI-GEN-7/8 and "Codex batch runner", SWI-PRV)
- Owner decisions: [decisions.md](../../../docs/architecture/decisions.md) ("Steamwoven interface" rows, 2026-11-16 and 2026-11-19)
- Where the kit lives and how it is backed up: [HD pack workflow](../../../docs/workflow/hd-pack.md), [HD pack format](../../../docs/formats/hd-pack.md)
- What the interface is made of: [census](../../../docs/ui/steamwoven-interface-census.md)
- The tool: [`tools/steamwoven_ui/codex_batch.py`](../../../tools/steamwoven_ui/codex_batch.py), [toolchain.md](../../../docs/workflow/toolchain.md#steamwoven-codex-batch-runner-arc-dev-3001)

Original art goes to Codex as input (owner decision 2026-11-16); every image stays under the ignored `local/steamwoven-ui/` of the main checkout and is never
committed. Work on the owner's Mac (the batch lives in the main checkout's `local/`, which a worktree does not have; the tool finds it).

## Model (mandatory)

The runner is pinned to **`gpt-6.1-sol` at reasoning effort `medium`** (owner decision 2026-11-20: the model of the good #2455 trial; newer models are problematic). Every `exec` and `exec resume`
carries it, whatever `~/.codex/config.toml` says. Never pass `--model` with another model, never change the effort and never edit the shared `~/.codex/config.toml`; a newer model needs the
owner's explicit approval first. The `--dry-run` commands show `-m gpt-6.1-sol` and `model_reasoning_effort="medium"`.

## Steps

1. The batch inbox exists: `python3 -B tools/steamwoven_ui/prompt_library.py inbox BATCH` builds it from the prompt library (needs its references), or write `batch.json` yourself (below).
2. `python3 -B tools/steamwoven_ui/codex_batch.py run BATCH --dry-run` : the pre-check result and the exact commands; nothing is sent. Fix a failed pre-check (not logged in, feature off) by telling the owner; never switch to an API key.
3. A real run spends the owner's subscription quota. Run a full batch only when the task or the owner asked for it; for a first look use `--only ID` (one item).
   `python3 -B tools/steamwoven_ui/codex_batch.py run BATCH [--only ID,ID] [--timeout SECONDS] [--crop-band]` (`--crop-band` crops the padded canvas the generator returns for the strips, 19.5:1 and 5:1, to its band; needs ImageMagick; a canvas Codex itself refused is not cropped); it is blocking (an item can take minutes), so run it once in the background or with a long timeout and wait; do not poll.
4. Read the summary lines and the records in `local/steamwoven-ui/outbox/<batch>/<id>.provenance.json`. Exit 0 = every item ok, 1 = some item not ok, 2 = nothing sent.
5. Report to the owner (in Russian): each file with its pixel size, each refusal or failure with Codex's own text (`detail`, `refusal_text`), what was skipped. Do not reword a refused prompt or retry it on your own; ask the owner. Show images the owner must judge as one labelled composite in the thread (English labels, never committed).
6. Then the fit tool and the rest of the pipeline per the TRD (SWI-GEN-4); the runner only produces the raw generations.

## Making a batch.json

`local/steamwoven-ui/inbox/<batch>/batch.json`, next to the prompt files and references it names (format in the TRD runner section):

```json
{"batch": "my-batch", "items": [
  {"id": "part-1", "title": "...", "prompt_file": "prompts/part-1.txt", "prompt_sha256": "<sha256 of the file text>",
   "size": [2880, 2160], "references": [{"slot": "context", "file": "refs/part-1/ctx.png", "sha256": "<sha256>"}],
   "outbox_file": "outbox/my-batch/part-1.png"}]}
```

Ids are plain names; `outbox_file` is `outbox/<batch>/<id>.png`; the prompt text and the reference files must match their hashes. Prompts are text only, ASCII, from the prompt library where one exists.

## Size and aspect

The generator keeps a budget of about 1.57 MP per image and picks the pixel size itself, so a 4:3 ask returned 1448 x 1086 in the first real run. The runner accepts the asked aspect within 1 %
(`aspect` in an item overrides the aspect of `size`) and never upscales; reaching the master size is the fit tool's job. A wrong aspect is reported as `wrong-aspect` and not copied to the outbox.
