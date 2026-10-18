using Arcanum.Data.Maps;

namespace Arcanum.Data.Area;

/// <summary>
/// ARC-DATA-036: the use-click transitions out of an area by one rule for every plan. A transition is an unconditional
/// use site of the map's transition data (<see cref="MapTransitions"/>, ARC-DATA-025: the object's use script reaches a
/// teleport on its forced path) whose trigger is a static object of the area's core sectors. The owner publishes a
/// plan's <see cref="AreaSourcePlan.Endpoints"/> only when each one names a present static core object with its own id,
/// prototype and use script (<see cref="Check"/>).
/// </summary>
public static class AreaEndpoints
{
    /// <summary>The attachment point a use click runs (the use slot of the object's script field).</summary>
    public const int UseSap = 1;
    private const int ScriptsField = 31;

    /// <summary>The endpoints of the unconditional use sites in <paramref name="transitions"/> whose trigger is a static
    /// object of a sector <paramref name="core"/> accepts, ordered by sector and ordinal.</summary>
    public static IReadOnlyList<AreaEndpoint> FromTransitions(MapTransitionSet transitions, Func<AreaSectorCoordinate, bool> core)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(core);
        return transitions.Unconditional
            .Where(s => s.Trigger == MapTransitionTrigger.Use && s.Object is { Sector: { } sector, Prototype: not null } && core(new(sector.X, sector.Y)))
            .Select(s => new AreaEndpoint(s.Object!.Sector!.Value.Id, s.Object.Index, s.Object.Prototype!.Value, UseSap, s.Script, s.DestinationCatalogue,
                s.DestinationRuntime, s.Arrival.X, s.Arrival.Y, s.Object.OwnIdSha256))
            .OrderBy(e => e.Sector).ThenBy(e => e.Ordinal).ToArray();
    }

    /// <summary>The script a use click on <paramref name="item"/> runs: word 2 of the use element of its effective script
    /// field, or null without one.</summary>
    public static int? UseScript(AreaObjectRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var field = item.EffectiveFields.SingleOrDefault(f => f.Slot == ScriptsField);
        return field?.Elements.SingleOrDefault(e => e.Key == UseSap) is { Words.Count: 3 } use && use.Words[2] is > 0 and <= int.MaxValue ? (int)use.Words[2] : null;
    }

    /// <summary>True when every endpoint names a present static core object with the endpoint's own id and prototype
    /// whose use script is the endpoint's script.</summary>
    internal static bool Check(IReadOnlyList<AreaEndpoint> endpoints, IEnumerable<AreaObjectRecord> objects)
    {
        var statics = objects.Where(o => o.Key.Kind == AreaSourceKind.Static && o.Scope == AreaScope.Core && o.Key.Sector is not null)
            .ToDictionary(o => (o.Key.Sector!.Value, o.Key.SourceIndex));
        return endpoints.All(e => statics.TryGetValue((e.Sector, e.Ordinal), out var item) && item.Key.ObjectIdSha256 == e.SourceObjectIdSha256 &&
            item.PrototypeNumber == e.Prototype && UseScript(item) == e.Script);
    }
}
