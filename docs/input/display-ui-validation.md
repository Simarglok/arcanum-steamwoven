# ARC-UI-001 display/UI validation

## Boundary and source policy

ARC-UI-001 adds an isolated experimental surface at `experiments/display-ui-lab/`. It does not alter the viewer, data/presentation APIs, root solution, game data, display settings, or final product UI. Its world geometry and bilingual text are independently authored synthetic material; it imports no original-game content, external fonts, images, binaries, or asset packs.

Technical references: Godot 4.7 documentation for [multiple resolutions and HiDPI](https://docs.godotengine.org/en/4.7/tutorials/rendering/multiple_resolutions.html), [Control layout](https://docs.godotengine.org/en/4.7/tutorials/ui/size_and_anchors.html), [containers](https://docs.godotengine.org/en/4.7/tutorials/ui/gui_containers.html), [Window](https://docs.godotengine.org/en/4.7/classes/class_window.html), [DisplayServer](https://docs.godotengine.org/en/4.7/classes/class_displayserver.html), [Camera2D](https://docs.godotengine.org/en/4.7/classes/class_camera2d.html), and [macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html). Installed runtime/release provenance is Godot 4.7.2 stable mono and the repository's existing third-party notice/license; no root notice is changed here.

## Policy, coordinate spaces, and invariants

- Initial window: ordinary resizable 1280×800, HiDPI allowed, never exclusive fullscreen. Window content scaling is disabled; there is no fixed virtual canvas.
- World: procedural `Node2D` geometry transformed solely by `Camera2D`. UI: viewport-coordinate Controls under `CanvasLayer`, independent of camera zoom.
- UI scale: deterministic theme metrics at 100/125/150/175/200%; it changes fonts, style-box padding, Container separation, and 44×44 base targets rather than `Window.ContentScaleFactor` or `Control.Scale`.
- Responsive effective space is viewport/UI-scale: Wide >=1200×680, Medium >=800×500, Compact otherwise. Wide reserves a right panel and visible world; Medium bounds the panel; Compact bounds and scrolls the active panel while title/Close/Reset stay sticky and action controls scroll horizontally.
- One labelled active Dialogue/Inventory/Settings surface at once. Compact layouts retain an accessible labelled panel selector in the horizontally scrollable action row; Close and Escape (outside fullscreen) genuinely close the active panel. The procedural world supports middle-button drag panning and zoom. Controls support focus, keyboard and mouse. Escape exits fullscreen first; F11 and focused F1 are application-local shortcuts. Configured foreground/background pairs are asserted at >=4.5:1, not a readability claim.
- Diagnostics labels viewport/window/client/usable/screen quantities and Godot-reported scale/DPI. It includes `reported by Godot; physical pixel mapping/readability not verified` and never reports user paths or inferred backing resolution.

## Reproducible validation

From `experiments/display-ui-lab` with the canonical process-local environment:

    export DOTNET_ROOT="$HOME/.local/share/dotnet"
    "$DOTNET_ROOT/dotnet" restore Arcanum.Experiments.DisplayUiLab.csproj --locked-mode
    "$DOTNET_ROOT/dotnet" build Arcanum.Experiments.DisplayUiLab.csproj --no-restore
    /usr/bin/env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT="$DOTNET_ROOT" PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin" /Applications/Godot_mono.app/Contents/MacOS/Godot --verbose --headless --path . --editor --build-solutions --quit
    tools/validate-macos.sh

The matrix invokes the actual `LabSurface.tscn` in sequential SubViewports for eight logical sizes (1280×720, 1280×800, 1920×1080, 1920×1200, 2560×1440, 2560×1600, 3440×1440, 3840×2160), each at 100/150/200 and all panels, plus 125/175 smoke at 1280×800. It records finite nonnegative actual global control rectangles, sticky controls, intentional-scroll ancestry, wrapping/multiline/visible-line checks, reserved-region separation, responsive class, configured contrast and fallback Cyrillic glyphs. One scene is driven through A→B→A and 100→200→100 snapshots (0.5 viewport-unit tolerance), camera/UI independence, routed `PushInput(..., true)` hover/click/signal evidence, and keyboard focus traversal. It is explicitly `simulated-layout`.

The evidence capture index is: 1280×720@200 dialogue; 1920×1080@100 inventory; 3440×1440@150 dialogue; 3840×2160@200 settings/diagnostics. Files are ignored local artifacts, never documentation assets. The manifest and `current.json` point to the exact final candidate only after local export gates pass.

### Candidate-bound result records

This pipeline deliberately makes no tracked-file result claim for a moving candidate. A successful run records its exact full commit, base, ordinary-diff hash, observed toolchain/template data, named checks, app/evidence paths, classifications, and delivery state in the ignored candidate `manifest.json`; only then does it atomically replace `artifacts/builds/ARC-UI-001/current.json`. The later final-validation continuation will record the observed final-head outcome here after its documentation commit and rerun.

## Classification and manual checklist

| Area | Classification | Meaning |
|---|---|---|
| Direct locked restore/build and editor bridge | actual | Process-local canonical SDK/editor checks. |
| Matrix control geometry and scene state | simulated | Actual scene instances in logical SubViewports, not physical displays. |
| Screenshot rendering | simulated; visual inspection NOT_VERIFIED | Geometry cannot establish glyph clipping/readability. |
| Exported launch, plist, architecture, signature | actual when `validate-macos.sh` succeeds | Exact exported app artifact. |
| Native resize/fullscreen/restore/root mouse | actual only when observed by a display-backed smoke | Never inferred from headless/SubViewport results. |
| Physical Retina/backing mapping, physical 4K/ultrawide, drag resize, focus recovery | NOT_VERIFIED unless observed | Reported Godot DPI/scale is insufficient. |
| Windows/Linux, controller/handheld, performance | deferred | Outside this macOS laboratory. |

Manual follow-up: inspect each labelled screenshot; use a display-backed exported app to resize twice, enter/leave ordinary fullscreen, confirm restored window bounds and root mouse alignment, then observe focus loss/recovery. Record only observed results.

## Integration decisions left open

This lab validates an independently transformable UI/world coordinate model and container/scrolling boundaries. It does not decide the game UI design, persistence, platform support, final font/art direction, simulation APIs, controller UX, or physical-display compatibility. Any reuse needs a separately accepted integration task.
