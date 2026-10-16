using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-RENDER-014: selects the visual profile and the HD pack for this run. The
/// setting, its persistence and the on-the-fly toggle are ARC-UI-006; until then a diagnostic
/// run chooses them with process environment variables:
/// <c>ARC_HD_PACK=DIR</c> (one pack directory, outside the repository; implies profile Hd),
/// <c>ARC_VISUAL_PROFILE=hd|legacy</c> (hd without a pack draws every frame Legacy as
/// <c>NoPack</c>) and <c>ARC_HD_BUDGET_PATH=FILE</c> (the device budget file, default
/// <c>user://hd_budget_v1.json</c>; missing or invalid means the contract ceilings).</summary>
public partial class TerrainViewerApp
{
    private HdFrameSource? _hdSource;

    private void ConfigureVisualProfile()
    {
        var packPath = System.Environment.GetEnvironmentVariable("ARC_HD_PACK");
        var requested = System.Environment.GetEnvironmentVariable("ARC_VISUAL_PROFILE")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(packPath) && string.IsNullOrEmpty(requested)) return;
        var profile = requested switch
        {
            "legacy" => VisualProfile.Legacy,
            "hd" or null or "" => VisualProfile.Hd,
            _ => throw new ArgumentException("Usage"),
        };
        HdPack? pack = null; var open = "none";
        if (!string.IsNullOrWhiteSpace(packPath))
        {
            var result = HdPack.Open(packPath);
            open = result.Status.ToString(); pack = result.Pack;
        }
        var budgetPath = System.Environment.GetEnvironmentVariable("ARC_HD_BUDGET_PATH") is { Length: > 0 } custom ? custom
            : ProjectSettings.GlobalizePath("user://hd_budget_v1.json");
        string? budgetText = null;
        try { if (System.IO.File.Exists(budgetPath)) budgetText = System.IO.File.ReadAllText(budgetPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        var scale = pack?.Scale ?? 4;
        var budget = HdDeviceBudgetCodec.DeserializeOrCeiling(budgetText, scale);
        _hdSource?.Dispose();
        _hdSource = new HdFrameSource(pack);
        _canvas!.SetVisualProfile(profile, _hdSource, budget);
        // No local path is printed: only the outcome, the pack id and its scale.
        GD.Print($"ARC-RENDER-014 visual profile={profile} pack={(pack is null ? "none" : $"{pack.Id} {pack.Scale}x {pack.Review} sprites={pack.SpriteCount}")} open={open} budget area-visible={budget.AreaVisibleBytes} world={budget.WorldBytes}");
    }
}
