# ARC-UI-001 display/UI validation

## Boundary and source policy

ARC-UI-001 adds an isolated experimental surface at `experiments/display-ui-lab/`. It does not alter the viewer, data/presentation APIs, root solution, game data, display settings, or final product UI. Its world geometry and bilingual text are independently authored synthetic material; it imports no original-game content, external fonts, images, binaries, or asset packs.

Technical references: Godot 4.7 documentation for [multiple resolutions and HiDPI](https://docs.godotengine.org/en/4.7/tutorials/rendering/multiple_resolutions.html), [Control layout](https://docs.godotengine.org/en/4.7/tutorials/ui/size_and_anchors.html), [containers](https://docs.godotengine.org/en/4.7/tutorials/ui/gui_containers.html), [Window](https://docs.godotengine.org/en/4.7/classes/class_window.html), [DisplayServer](https://docs.godotengine.org/en/4.7/classes/class_displayserver.html), [Camera2D](https://docs.godotengine.org/en/4.7/classes/class_camera2d.html), and [macOS export](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_for_macos.html). Installed runtime/release provenance is Godot 4.7.2 stable mono and the repository's existing third-party notice/license; no root notice is changed here.

## Policy, coordinate spaces, and invariants

- Initial window: ordinary resizable 1280×800, HiDPI allowed, never exclusive fullscreen. Window content scaling is disabled; there is no fixed virtual canvas.
- World: procedural `Node2D` geometry transformed solely by `Camera2D`. UI: viewport-coordinate Controls under `CanvasLayer`, independent of camera zoom.
- UI scale: deterministic theme metrics at 100/125/150/175/200%; it changes fonts, padding, separation, and 44×44 base targets rather than `Window.ContentScaleFactor` or `Control.Scale`.
- Responsive effective space is viewport/UI-scale: Wide >=1200×680, Medium >=800×500, Compact otherwise. Wide reserves a right panel and visible world; Medium bounds the panel; Compact bounds and scrolls the active panel while title/Close/Reset stay sticky and action controls scroll horizontally.
- One labelled active Dialogue/Inventory/Settings surface at once. Controls support focus, keyboard and mouse. Escape exits fullscreen first; F11 and focused F1 are application-local shortcuts. Contrast pairs target >=4.5:1, not a readability claim.
- Diagnostics labels viewport/window/client/usable/screen quantities and Godot-reported scale/DPI. It includes `reported by Godot; physical pixel mapping/readability not verified` and never reports user paths or inferred backing resolution.

## Reproducible validation

From `experiments/display-ui-lab` with the canonical process-local environment:

    export DOTNET_ROOT="$HOME/.local/share/dotnet"
    "$DOTNET_ROOT/dotnet" restore Arcanum.Experiments.DisplayUiLab.csproj --locked-mode
    "$DOTNET_ROOT/dotnet" build Arcanum.Experiments.DisplayUiLab.csproj --no-restore
    /usr/bin/env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT="$DOTNET_ROOT" PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin" /Applications/Godot_mono.app/Contents/MacOS/Godot --verbose --headless --path . --editor --build-solutions --quit
    tools/validate-macos.sh

The matrix invokes the actual `LabSurface.tscn` in sequential SubViewports for eight logical sizes (1280×720, 1280×800, 1920×1080, 1920×1200, 2560×1440, 2560×1600, 3440×1440, 3840×2160), each at 100/150/200 and all panels, plus 125/175 smoke at 1280×800. It records finite nonnegative actual control rectangles, sticky controls, reserved-region separation, responsive class, and A→B→A / 100→200→100 transitions. It is explicitly `simulated-layout`.

The evidence capture index is: 1280×720@200 dialogue; 1920×1080@100 inventory; 3440×1440@150 dialogue; 3840×2160@200 settings/diagnostics. Files are ignored local artifacts, never documentation assets. The manifest and `current.json` point to the exact final candidate only after local export gates pass.

### Recorded local result (pre-documentation candidate)

The pre-documentation candidate `d789d19fde08a11a9d97a618034c8da338fd95bb` passed `tools/validate-macos.sh` with the canonical .NET SDK 8.0.425 and Godot 4.7.2 stable mono. That run completed locked restore/build, the verbose Godot editor solution bridge, the source and exported simulated-layout matrices, four simulated screenshot captures, macOS debug export, exported-app matrix/launch, `plutil -lint`, and strict deep `codesign` verification. Both matrix reports recorded `PASS`.

The unchanged merged root also passed locked restore/build and non-real-data tests: 14 Presentation tests and 24 Data tests passed with zero failures. Final candidate validation is rerun after this results documentation is committed; its ignored artifact directory and `current.json` identify the exact post-documentation head.

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
