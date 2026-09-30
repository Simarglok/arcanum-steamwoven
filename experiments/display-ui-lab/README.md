# Display UI Lab

A disposable, standalone Godot 4.7.2 / C# laboratory for ARC-UI-001. It is deliberately synthetic: procedural geometry, independently authored English/Russian strings, and Godot's built-in fallback font only. It is not game UI, an original-game compatibility layer, or a source of reusable gameplay architecture.

## Controls

- Panel buttons select Dialogue, Inventory, or Settings; compact layouts retain the active panel and the action row scrolls horizontally.
- Settings and the action row select deterministic UI metrics at 100/125/150/175/200%. These change font/padding/separation/target metrics; they do not use `Control.Scale` or window content scaling.
- Zoom buttons and Settings select camera zoom (0.50–2.00; presets include 0.75/1.00/1.25/1.50). The Camera2D transforms only the synthetic world.
- F11 toggles ordinary fullscreen, Escape leaves fullscreen first, and F1 toggles diagnostics while the app is focused. Reset returns to windowed 1280×800 clamped to the current usable screen and resets lab state.

Important buttons use keyboard focus and a 44×44 base target. The configured foreground/background pairs are selected for a 4.5:1-or-greater contrast target; this is not a claim that rendering is readable on a physical display.

## Layout policy

The initial window is resizable 1280×800 with HiDPI allowed and window content scaling disabled. Controls lay out in viewport coordinates beneath a `CanvasLayer`; only `Camera2D` transforms the procedural world. Responsive class is based on viewport divided by UI scale: Wide >=1200×680, Medium >=800×500, Compact otherwise. Wide reserves a right panel, Medium uses a bounded panel, and Compact uses a bounded scrolling panel with sticky Close/Reset.

The diagnostics text labels Godot-reported sizes, scale, and DPI explicitly. It does not infer backing resolution. Reported screen scale and DPI are not physical-Retina or readability evidence.

## Process-local commands

Use only the project registry's canonical tools:

    export DOTNET_ROOT="$HOME/.local/share/dotnet"
    "$DOTNET_ROOT/dotnet" restore Arcanum.Experiments.DisplayUiLab.csproj --locked-mode
    "$DOTNET_ROOT/dotnet" build Arcanum.Experiments.DisplayUiLab.csproj --no-restore
    /usr/bin/env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT="$DOTNET_ROOT" PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin" /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . -- --run-layout-matrix --report /absolute/artifact/layout-matrix.json

After a clean committed candidate, configure the ignored artifact root with `tools/arcanum-local configure add-artifact-root --id ID PATH`, then run `ARCANUM_LOCAL_ROOT_ID=ID tools/validate-macos.sh`. It uses the literal direct locked restore/build commands above (without property overrides), validates the installed registry/tool versions/templates, and re-execs beneath the exact candidate lease before any export. Its measured free-space preflight is bound as immutable evidence with the full candidate identity, output/temporary/reserve budget, and observed free space. It records named logs for source/export/root/hygiene gates, writes a nonempty digest-and-byte-bound v2 evidence manifest, then—only after every gate including deep/strict codesign—registers lifecycle `exported` and atomically writes the task-scoped v2 `current.json`. Native smoke remains automated evidence; ordinary launch is explicitly `NOT_VERIFIED`, not relabelled smoke output. A failed validation neither registers nor replaces the pointer.

The endpoint requires effective `TMPDIR`. Output budget uses a verified accepted/current payload or explicit single-payload `ARCANUM_OUTPUT_BYTES`/`ARCANUM_ESTIMATE_MANIFEST`, never historical/source `du`. Export/temp/build/cache devices are checked independently. Validation exits without a manifest; frozen baseline/periodic/final observation is hashed during manifest-only finalization. Godot/.NET cache attribution is incomplete, so production temporary usage is explicitly unavailable (`null`), not a budget estimate or measured zero. Finalized/ambiguous partial same-head candidates reject intact. The finite full-card hygiene/literal gates are unchanged. See [local workflow](../../docs/workflow/local-development.md).

## Evidence boundaries

The matrix uses the actual `LabSurface.tscn` in logical `SubViewport`s. Its matrix and screenshots are therefore classified **simulated layout**, not monitor testing. Physical Retina/backing-pixel mapping, visual readability, genuine user drag resizing, physical 4K/ultrawide hardware, Windows/Linux, handheld/controller behavior, and performance are not verified by this lab. Screenshot PNGs, reports, and the exported app remain ignored under `artifacts/builds/ARC-UI-001/`.

Godot is MIT licensed; see the existing repository release/license notice. This project commits no third-party font, image, or asset.
