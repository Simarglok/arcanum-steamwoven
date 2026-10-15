using Arcanum.Data.Area;

namespace Arcanum.TerrainViewer;

/// <summary>Private first-rejection census, never a successful coverage report.
/// TargetOnlyEligible is overlapping diagnostic information, not a partition.
/// UniqueScreenCenters = Rejections.Values.Sum() + Admitted on normal return.</summary>
internal sealed class AreaPixelDiagnostics
{
    public int UniqueScreenCenters { get; set; }
    public int TargetOnlyEligible { get; set; }
    public int Admitted { get; set; }
    public string? RawTransform { get; set; }
    public string? RasterTransform { get; set; }
    public AreaVisualKey? LastOccluder { get; set; }
    public Dictionary<string,int> Rejections { get; } = new();
    public void Reject(string reason) => Rejections[reason] = Rejections.GetValueOrDefault(reason) + 1;
}
