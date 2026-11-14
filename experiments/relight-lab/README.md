# Relight Lab (ARC-RES-1978)

Godot-free C# generator and measuring tool for the [relighting experiment](../../docs/research/relighting-experiment.md). The
viewer's relit shader (`tools/terrain-viewer/Godot/AreaRelight.cs`) derives a normal per sprite pixel on the GPU; `Core/NormalMapGenerator.cs`
is its CPU twin, so a frame can be checked, measured or batch-generated for a later HD pack. It reads no game data by itself.

- Height is the luminance (the original sprites have their light baked in, so there is no height map). The normal is the 3×3 Sobel of
  the height (optionally box-blurred first), alpha aware: an empty neighbour takes the centre's height, and the silhouette adds an
  outward bevel. Presets `ground` (flat), `object` (walls, scenery), `critter` (blur 1 to calm palette dither between animation frames).
- Green is OpenGL style by default (Y+ up, what Godot's `CanvasTexture` expects); `--convention image` keeps image space (Y down, the
  viewer's shader).
- Deterministic: the same pixels and options give the same bytes.

## Commands

From the repository root, through the [launch queue](../../docs/workflow/toolchain.md#launch-queue-arc-dev-033):

    L=experiments/relight-lab
    ./arcanum run --kind dotnet -- sh -c '$HOME/.local/share/dotnet/dotnet restore '$L'/Arcanum.Experiments.RelightLab.csproj --locked-mode && $HOME/.local/share/dotnet/dotnet restore '$L'/tests/Arcanum.Experiments.RelightLab.Tests.csproj --locked-mode && $HOME/.local/share/dotnet/dotnet build --no-restore '$L'/Arcanum.Experiments.RelightLab.csproj && $HOME/.local/share/dotnet/dotnet test --no-restore '$L'/tests/Arcanum.Experiments.RelightLab.Tests.csproj'
    ./arcanum run --kind dotnet -- $HOME/.local/share/dotnet/dotnet $L/bin/Debug/net8.0/Arcanum.Experiments.RelightLab.dll normals IN.png OUT.png --kind critter
    ./arcanum run --kind dotnet -- $HOME/.local/share/dotnet/dotnet $L/bin/Debug/net8.0/Arcanum.Experiments.RelightLab.dll bench DIR --kind object
    ./arcanum run --kind dotnet -- $HOME/.local/share/dotnet/dotnet $L/bin/Debug/net8.0/Arcanum.Experiments.RelightLab.dll shimmer DIR --kind critter

Inputs are 8-bit RGBA PNGs (`magick in.png PNG32:out.png` converts a Godot capture). `normals` writes a normal map, `bench` prints
milliseconds and bytes per frame, `shimmer` runs a fixed-light Lambert comparison over consecutive same-size frames of one animation.
Frames cut from the player's art are derived from it: keep them and the output outside git (`ARC_RELIGHT_CAPTURE` of the smoke stage
writes such frames).

Not part of the root solution (like the other labs); `tests/` is an MSTest project over `Core/`.
