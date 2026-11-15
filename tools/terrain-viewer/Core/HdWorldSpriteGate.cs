namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-RENDER-2175 (owner decision 2026-11-15): under the one HD option the world is drawn from Legacy art; the HD pack's
/// sprite and terrain path is dormant (code, format and identity oracle stay). A developer sets <see cref="EnvironmentName"/>=1 to
/// bring the old path back for a run, for example the identity smokes on a sprite pack. It is not a setting.</summary>
public static class HdWorldSpriteGate
{
    public const string EnvironmentName = "ARC_HD_WORLD_SPRITES";

    /// <summary>True only for the value "1" (surrounding blanks ignored); anything else, or nothing, keeps the path dormant.</summary>
    public static bool Parse(string? value) => value?.Trim() == "1";

    public static bool FromEnvironment() => Parse(System.Environment.GetEnvironmentVariable(EnvironmentName));
}
