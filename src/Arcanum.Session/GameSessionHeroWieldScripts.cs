namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-MAGIC-1744 (ledger RM3c, IE7, WE6; exe read 0x465530, 0x441980): the item event scripts of the rewield 0x465530 for the hero, planned and not applied: the hero as the wearer of
    /// <see cref="PlanNpcRewield"/>'s runs. The rewield walks the wear slots 1000 to 1008 and for each worn item runs wield off (point 25) and then wield on (24), the item as the attachee and the hero as the triggerer,
    /// with the condition "items are being rewielded" true (0x5E87E8); an item without a script on a point does nothing. The runs read the hero's stats and aptitude over <paramref name="stored"/>, the hero's stored stats with the
    /// store of the move laid in (the setter stores before it rewields), under the effect changes the earlier runs made, and change its effect list (cause item). Ready (<see cref="GameSessionFailure.None"/>, <c>Runs</c> null
    /// when no worn item has a wield script, so nothing runs) for a hero with no worn wield script; with one and no <paramref name="host"/>, without a hero, or with a run the session cannot apply (a script outside the admitted
    /// set, an effect on another object, a rule the host does not know, a read that an earlier effect change of the same run would have moved, a list the effect rules refuse), the failure that names it and nothing is applied.
    /// The light recalculation 0x467E80 is derived from the wear (<see cref="QueryHeroLight"/>); the printed lines and eye candy of the runs are not shown here.
    /// </summary>
    internal (GameSessionFailure Failure, ItemScriptRuns? Runs) PlanHeroRewield(IReadOnlyList<int> stored, ISessionItemScriptHost? host)
    {
        ThrowIfDisposed();
        if (_hero is null) return (GameSessionFailure.InvalidHero, null);
        var worn = _equipment.Worn.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
        var scripted = worn.Where(item => item.ScriptPoints.Contains(SessionScriptPoint.WieldOn) || item.ScriptPoints.Contains(SessionScriptPoint.WieldOff)).ToArray();
        if (scripted.Length == 0) return (GameSessionFailure.None, null);
        if (host is null) return (GameSessionFailure.ItemScriptUnsupported, null);
        if (_revision > long.MaxValue - 2) return (GameSessionFailure.RevisionExhausted, null);
        var (revision, map, flagVersion) = (_revision, _actor.Map, _flags.Version);
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in worn) items[item.Entity] = item.Item.MagickTechComplexity;
        var runs = new ItemScriptRuns(this, host, map.RuntimeMap, items) { Rewielding = true, HeroStored = stored };
        foreach (var item in scripted)
        {
            if (item.ScriptPoints.Contains(SessionScriptPoint.WieldOff)) runs.Run(SessionScriptPoint.WieldOff, item.Entity, SessionEventKinds.PlayerEntity, null);
            if (item.ScriptPoints.Contains(SessionScriptPoint.WieldOn)) runs.Run(SessionScriptPoint.WieldOn, item.Entity, SessionEventKinds.PlayerEntity, null);
            if (runs.Failure != GameSessionFailure.None) return (runs.Failure, null);
        }
        ThrowIfDisposed();
        if (_revision != revision || _actor.Map != map || _flags.Version != flagVersion) return (GameSessionFailure.StaleVersion, null);
        runs.Prepare();
        return runs.Failure != GameSessionFailure.None ? (runs.Failure, null) : (GameSessionFailure.None, runs);
    }
}
