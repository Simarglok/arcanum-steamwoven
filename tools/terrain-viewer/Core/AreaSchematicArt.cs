using Arcanum.Data.Content;
using Arcanum.Data.Technology;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-142: what the schematic window's original look draws from the game folder: the window's interface art (<see cref="AreaSchematicWindowLayout.ArtNumbers"/>,
/// its fonts included) and the art of every schematic record. The window keeps its diagnostic look when a required art does not read; a record art that does
/// not read leaves the record's picture out.
/// </summary>
public sealed record AreaSchematicArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string Detail)
{
    /// <summary>True when every art the window itself needs read.</summary>
    public bool Ready => AreaSchematicWindowLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load)
        && load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0);

    public static AreaSchematicArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), reason);

    public static AreaSchematicArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var catalog = SchematicCatalog.Open(stack).Catalog;
        var numbers = AreaSchematicWindowLayout.ArtNumbers.Concat(catalog?.Records.Select(record => record.Art).Where(art => art > 0) ?? []).Distinct().ToArray();
        // The fonts have 225 frames.
        var art = AreaInterfaceArt.Load(stack, numbers, new AreaInterfaceArtLimits(MaxFrames: 256, MaxRgbaBytes: 32L * 1048576));
        var required = AreaSchematicWindowLayout.ArtNumbers.ToHashSet();
        var failed = art.Values.Where(load => required.Contains(load.Number) && load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, $"art {art.Count - art.Values.Count(load => load.Status != AreaInterfaceArtStatus.Ready)}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}");
    }
}
