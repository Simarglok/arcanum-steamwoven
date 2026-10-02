# Toolchain and verification entry points

Configuration is authoritative: [global.json](../../global.json) pins .NET SDK `8.0.425` with roll-forward disabled; [Directory.Build.props](../../Directory.Build.props) targets `net8.0`; [Directory.Packages.props](../../Directory.Packages.props) centralizes package versions. Godot adapters/labs use `Godot.NET.Sdk/4.7.2`, Godot **4.7.2 .NET** and matching .NET export templates. Development currently targets macOS. Version changes require a separate scoped decision; do not replace retained shared installations implicitly.

## Process-local environment

Use the existing canonical tools. These paths are the established macOS convention, not an installation command. Operational installation/ownership records remain local, outside Git.

```sh
DOTNET_ROOT="$HOME/.local/share/dotnet"
DOTNET="$DOTNET_ROOT/dotnet"
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
godot_canonical() {
  env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT="$DOTNET_ROOT" \
    PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin" "$GODOT" "$@"
}
"$DOTNET" --info
```

The sentinel prevents Godot editor shell startup from replacing the launch PATH. Before accepting an export, inspect the verbose editor build bridge: SDK `8.0.425` and the canonical dotnet executable must be selected, with no Homebrew build/publish path. A direct dotnet build alone does not prove editor/export selection. Use the component's export recipe, including its fresh build/import and exact-candidate evidence requirements.

For Gamepad, Display and the bounded Terrain endpoint, configure the ignored artifact root once with `tools/arcanum-local configure add-artifact-root --id ID PATH`, then export with `ARCANUM_LOCAL_ROOT_ID=ID`. Their validation commands acquire the exact candidate's `.export-lease.lock` before export and retain it through every successful gate, v2 manifest/evidence inventory validation, local registry mutation, and v2 current-pointer update. Do not call `builds register-export` as a substitute for an endpoint; that command validates an already complete candidate but cannot retroactively cover an export that ran without the lease. A failed validation does not register or select an artifact.

Supply effective `TMPDIR` explicitly for these endpoints. Preflight proves export/temp/workspace/NuGet/Godot-cache destinations and checks the full budget per distinct device; it does not measure the configuration volume as a proxy. Output comes from a verified accepted/current payload plus 10% or `ARCANUM_OUTPUT_BYTES` and `ARCANUM_ESTIMATE_MANIFEST` for ONE complete configured payload, never aggregate history/source `du`. Validation exports/tests/codesigns without writing a manifest; the endpoint freezes baseline/periodic/final capacity observation, then invokes only the manifest writer. Temporary cache attribution remains unavailable unless all temporary writes are proved isolated; a fallback/history estimate is not an observation. Do not rerun a finalized same-head endpoint: it rejects intact. Inspect immutable artifacts read-only. See [local workflow](local-development.md) for exact observation/protection semantics.

If the editor bridge selects another executable or SDK, stop before export. Do not replace retained SDKs, change global shell startup files, use `TERM` as a workaround, or weaken macOS security. Resolve the mismatch within the accepted toolchain scope and local installation registry.

Godot references: [C# prerequisites](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html), [renderer selection](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html), [macOS export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_macos.html).

## Root solution synthetic checks

ARC-WORLD-001 uses the terrain endpoint with
`ARCANUM_TERRAIN_EXPORT_TASK=ARC-WORLD-001`, a separate configured ignored
`artifacts/world-builds` root and explicit WORLD selection. It retains the
seven terrain gates plus focused tests and graphical original source/export
integration gates. Source corrections precede the one exact-head export;
later ordinary evidence stays outside the sealed bundle. See the
[WORLD policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy).

Run from repository root:

```sh
"$DOTNET" restore Arcanum.Steamwoven.sln --locked-mode
"$DOTNET" build Arcanum.Steamwoven.sln --no-restore
"$DOTNET" test Arcanum.Steamwoven.sln --no-build --filter "TestCategory!=RealData"
python3 -B tools/research/dat_probe.py --self-test
```

The root solution includes Data, Presentation, Scripting, [headless Simulation movement](../world/grid-movement-runtime.md), the ART viewer and terrain-inspect. It does **not** include the standalone terrain viewer/tests, research C# probes, or display/gamepad labs. Use their specific commands: [ART viewer](../Arcanum.Godot.md), [terrain viewer](../rendering/terrain-viewer.md), [display lab](../../experiments/display-ui-lab/README.md), [gamepad lab](../../experiments/gamepad-lab/README.md). Launch Godot projects with Godot; `dotnet run` applies to executable CLIs/harnesses, not the Godot adapter projects.

## Opt-in original data

Original inputs remain read-only and local. Set only the inputs needed for the selected check; never commit actual paths or content. Missing required inputs fail or make an invocation inconclusive, not verified. Use `--no-build` only after building the corresponding project/configuration.

| Check | Required local configuration | Narrow filter / source contract |
| --- | --- | --- |
| DAT R1 | `DAT_ROOT`, eight documented candidates | Data project, `FullyQualifiedName~DatArchiveTests&TestCategory=RealData`; [DAT API/evidence](../Arcanum.Data.md) |
| DLG reader | `DAT_ROOT` | Data project, `TestCategory=DlgRealData`; [DLG reader](../formats/dlg-reader.md) |
| Dialog transition | `DAT_ROOT` | Scripting project, `TestCategory=DlgRuntimeRealData`; [runtime](../scripting/dialog-transition-runtime.md) |
| Terrain | `ARCANUM_TERRAIN_MODULE`, `ARCANUM_TERRAIN_RESOURCE` | Data project, `FullyQualifiedName~TerrainLoaderTests&TestCategory=RealData`; [loader](../world/terrain-loader.md) |
| Static objects | `ARCANUM_STATIC_MODULE`, `ARCANUM_STATIC_RESOURCE`, `ARCANUM_STATIC_PROTOTYPES` | Data project, `FullyQualifiedName~StaticObjectLoaderTests&TestCategory=RealData`; [loader](../world/static-object-loader.md) |

Example, with `DAT_ROOT` already set locally:

```sh
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build \
  --filter "FullyQualifiedName~DatArchiveTests&TestCategory=RealData"
```

A solution-wide `TestCategory=RealData` run selects all these input families; `DAT_ROOT` alone is insufficient. Viewers also require their documented explicit arguments and original-data fingerprint gates. Synthetic tests, original-data structural checks, graphical readback, human visual observations and physical device evidence are separate results. Headless dummy rendering cannot establish graphical pixel readback. Documentation refreshes validate links/configuration, not runtime behavior.
