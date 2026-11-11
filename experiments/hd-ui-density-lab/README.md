# HD UI Density Lab (ARC-RES-122)

A standalone Godot 4.7.2 .NET lab for the concept in
[generated interface chrome and density-aware layout](../../docs/rendering/hd-interface-density.md). It draws a mock
screen (both HUD strips at the original's rectangles, one 800 × 400 placeholder window, a slot grid, buttons, gauges and a
vector brass ring with a glass lens) from solid-colour placeholders only, sized by the OS display scale and rounded to whole
device pixels. No game data, art or font is read or committed; the font is Godot's built-in fallback.

It is not part of the root solution and not the viewer. `Core/` is Godot-free (scale rule, device-pixel grid, components,
mock layout); `scripts/` holds the Godot side (`DensityLabApp`, `MockCanvas`, the `LensRing.gdshader` distance-field shader,
`SelfTest`); `tests/` is an MSTest project over `Core/` (Godot ignores it through `.gdignore`).

## Keys

`+`/`-` player scale (50–300 %), `S` forced scale (auto, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.75), `R` ring width,
`L` light direction, `G` glass tint, `D` diagnostics, `F11` fullscreen. Buttons show hover and down states; "Map" is
disabled. The diagnostics box shows the host, driver, reported scale and DPI, the OS scale `k`, the density and fit scales
and the chosen `s`; the log prints `ARC-RES-122 lab scale ...` on every change and one `ARC-RES-122 lab screen ...` line
per screen at start.

## Commands

From the repository root, through the [launch queue](../../docs/workflow/toolchain.md#launch-queue-arc-dev-033):

    L=experiments/hd-ui-density-lab
    ./arcanum run --kind dotnet -- sh -c '$HOME/.local/share/dotnet/dotnet restore '$L'/Arcanum.Experiments.HdUiDensityLab.csproj --locked-mode && $HOME/.local/share/dotnet/dotnet restore '$L'/tests/Arcanum.Experiments.HdUiDensityLab.Tests.csproj --locked-mode && $HOME/.local/share/dotnet/dotnet build --no-restore '$L'/Arcanum.Experiments.HdUiDensityLab.csproj && $HOME/.local/share/dotnet/dotnet test --no-restore '$L'/tests/Arcanum.Experiments.HdUiDensityLab.Tests.csproj'
    ./arcanum run --kind window --expect 'ARC-RES-122 self-test start' -- env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT=$HOME/.local/share/dotnet PATH=$HOME/.local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin /Applications/Godot_mono.app/Contents/MacOS/Godot --path $L -- --self-test [--capture ABSOLUTE_DIR]
    ./arcanum run --kind window --expect 'ARC-RES-122 lab start' -- env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT=$HOME/.local/share/dotnet PATH=$HOME/.local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin /Applications/Godot_mono.app/Contents/MacOS/Godot --path $L [--screen N]

The self-test renders the mock screen off screen at `s` = 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.75 and 4, reads each frame back
and checks that the window frame (both sides), the top strip (top and bottom) and the top of the bottom strip are exactly
their token colours for exactly their rounded widths, and that the lens was drawn (54 checks). `--capture ABSOLUTE_DIR`
saves the frames (a relative path is refused: Godot runs in the project folder); they are synthetic, keep them outside
git anyway. Any exception or a frame that cannot be read back ends the run with `result=FAIL` and exit 1. Godot's `--screen N` opens the interactive lab on another screen (a Retina panel
and a 1× monitor give different `k`).

## Not covered

A forced `s` (key `S`) is not clamped to the window: above the fit the strips run off screen, as intended for a lab.
Windows and SteamOS paths are code only (no run); physical readability and the look against the original are the owner's
judgement; performance was not measured.
