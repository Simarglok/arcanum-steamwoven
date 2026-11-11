using Arcanum.Data.Content;
using Arcanum.Data.Reputations;
using Arcanum.Simulation.Reputation;

namespace Arcanum.World;

public enum AreaReputationsStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, TableFailed }

/// <summary>The module's reputation table as the session reads it, or why there is none (the session then has an empty one).</summary>
public sealed record AreaReputationsLoad(AreaReputationsStatus Status, ReputationCatalog? Catalog, string Detail);

/// <summary>
/// ARC-WORLD-044: the reputation table of the module the area was read from (<see cref="ReputationTable.Load"/> through the
/// game folder's content stack, <see cref="AreaContentRoot.Discover"/>), as the session's <see cref="ReputationCatalog"/>.
/// Universal: any module. The names and greetings are read with it and not used yet (greetings are not built).
/// </summary>
public static class AreaReputations
{
    public static AreaReputationsLoad Load(string modulePath)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return new(found.Status == AreaContentRootStatus.MountFailed ? AreaReputationsStatus.MountFailed : AreaReputationsStatus.ModuleOutsideGameRoot, null, found.Detail);
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(AreaReputationsStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var loaded = ReputationTable.Load(stack);
        if (loaded.Table is not { } table) return new(AreaReputationsStatus.TableFailed, null, $"{loaded.Status} {loaded.Path} {loaded.Read} {loaded.Parse?.Status} {loaded.Number}");
        return Catalog(table) is { } catalog
            ? new(AreaReputationsStatus.Ready, catalog, $"{table.Records.Count} reputations, {table.Records.Sum(record => record.Effects.Count)} effects"
                + (table.RulesLoaded ? "" : " (no rules/gamerep.mes)") + (table.Aborted is { } aborted ? $" (parse stopped at {aborted})" : ""))
            : new(AreaReputationsStatus.TableFailed, null, "an effect beyond the session's bounds");
    }

    /// <summary>The session's view of a table: every record with its factions and effects (the stopped entry's partial factions too).</summary>
    public static ReputationCatalog? Catalog(ReputationTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return ReputationCatalog.Create(table.Records.Select(record => new ReputationDefinition(record.Number, record.Factions,
            record.Effects.Select(effect => new ReputationEffectDefinition(effect.Adjustment, effect.Origin, effect.Faction)).ToArray())));
    }
}
