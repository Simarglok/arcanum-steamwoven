using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Path-free selected static-frame key used by the diagnostic planner.</summary>
public readonly record struct StaticSceneryTextureKey(StaticObjectResourceIdentity ResourceIdentity, int Group, int Frame, int Palette);

/// <summary>Public, synthetic-safe input to the static scenery geometry seam.</summary>
public sealed record StaticSceneryPlanInput(int SourceIndex, int? LocalX, int? LocalY, int? DirectOffsetX, int? DirectOffsetY,
    StaticObjectResourceIdentity ResourceIdentity, int Group, int Frame, int Palette, int Width, int Height, int HotX, int HotY,
    int ArtOffsetX, int ArtOffsetY, bool FlipX);

public sealed record StaticSceneryDrawCommand(int SourceIndex, StaticSceneryTextureKey TextureKey, TerrainRect Destination,
    int AnchorX, int AnchorY, bool FlipX, int ArtOffsetX, int ArtOffsetY);

public sealed class StaticSceneryScenePlan
{
    private StaticSceneryScenePlan(IReadOnlyList<StaticSceneryDrawCommand> commands) => Commands = commands;
    public IReadOnlyList<StaticSceneryDrawCommand> Commands { get; }

    public static StaticSceneryScenePlan CreateSynthetic(IEnumerable<StaticSceneryPlanInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var commands = inputs.Select(StaticSceneryGeometry.Place).OrderBy(command => command.AnchorY).ThenBy(command => command.AnchorX)
            .ThenBy(command => command.SourceIndex).ToArray();
        if (commands.Length == 0) throw new ArgumentException("A static scenery plan needs commands.", nameof(inputs));
        if (commands.Select(command => command.SourceIndex).Distinct().Count() != commands.Length)
            throw new ArgumentException("Static scenery source indexes must be unique.", nameof(inputs));
        return new StaticSceneryScenePlan(Array.AsReadOnly(commands));
    }

    public StaticSceneryScenePlan Isolate(int sourceIndex)
    {
        var command = Commands.SingleOrDefault(item => item.SourceIndex == sourceIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        return new StaticSceneryScenePlan(Array.AsReadOnly([command]));
    }

    /// <summary>Admits exactly the authenticated selected five records.</summary>
    public static StaticSceneryScenePlan FromAuthenticatedInspection(StaticObjectInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (inspection.TerminalRecordCount != 317 || inspection.ResourceResolvedCount != 5 || inspection.ResourceOutsideScopeCount != 312 ||
            inspection.ResourceNotEvaluatedCount != 0)
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticCensus,
                Stage: TerrainViewerFailureStage.StaticCensus));
        var selected = inspection.Records.Where(record => record.SourceIndex is >= 0 and <= 4).ToArray();
        if (selected.Length != 5 || selected.Any(record => record.ResourceStatus != StaticObjectResourceStatus.Resolved || record.FieldStatus != StaticObjectFieldStatus.Resolved ||
            record.IsStatic != true || record.LocalX is null || record.LocalY is null || record.DirectOffsetX is null || record.DirectOffsetY is null || record.ArtMetadata is null))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticResource,
                Stage: TerrainViewerFailureStage.StaticResource));
        return CreateSynthetic(selected.Select(record =>
        {
            var metadata = record.ArtMetadata!;
            return new StaticSceneryPlanInput(record.SourceIndex, record.LocalX, record.LocalY, record.DirectOffsetX, record.DirectOffsetY,
                metadata.ResourceIdentity, metadata.RotationGroup, metadata.Frame, metadata.EffectivePalette, metadata.Width, metadata.Height,
                metadata.HotX, metadata.HotY, metadata.ArtOffsetX, metadata.ArtOffsetY, false);
        }));
    }
}

public static class StaticSceneryGeometry
{
    public static StaticSceneryDrawCommand Place(StaticSceneryPlanInput input)
    {
        if (input.SourceIndex < 0 || input.LocalX is null || input.LocalY is null || input.DirectOffsetX is null || input.DirectOffsetY is null ||
            input.Width <= 0 || input.Height <= 0 || input.Group < 0 || input.Frame < 0 || input.Palette < 0 ||
            input.ResourceIdentity.DatOrdinal < 0 || string.IsNullOrWhiteSpace(input.ResourceIdentity.NormalizedLogicalNameSha256))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticResource,
                Stage: TerrainViewerFailureStage.Plan));
        try
        {
            var anchorX = checked(40 * checked(input.LocalY.Value - input.LocalX.Value));
            var anchorY = checked(20 * checked(input.LocalY.Value + input.LocalX.Value));
            anchorX = checked(anchorX + input.DirectOffsetX.Value);
            anchorY = checked(anchorY + input.DirectOffsetY.Value);
            var left = checked(anchorX - input.HotX);
            var top = checked(anchorY - input.HotY);
            _ = checked(left + input.Width);
            _ = checked(top + input.Height);
            return new StaticSceneryDrawCommand(input.SourceIndex, new StaticSceneryTextureKey(input.ResourceIdentity, input.Group, input.Frame, input.Palette),
                new TerrainRect(left, top, input.Width, input.Height), anchorX, anchorY, input.FlipX, input.ArtOffsetX, input.ArtOffsetY);
        }
        catch (OverflowException)
        {
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.StaticPlan,
                Stage: TerrainViewerFailureStage.Plan));
        }
    }
}
