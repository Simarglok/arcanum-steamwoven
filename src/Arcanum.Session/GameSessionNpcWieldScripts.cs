using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1644: an NPC as the wearer of the item event scripts it runs (wield on 24, wield off 25; <c>ItemScriptRuns</c>): its life target (the effect list the runs change and the life rules the changes meet) and its
/// stored stats as the runs read them (the 28 values of the setter's array with any store of the move already laid over, ledger RM3c).
/// </summary>
internal sealed record NpcWearer(string Entity, SessionLifeTarget Target, IReadOnlyList<int> Stored);

public sealed partial class GameSessionOwner
{
    // The NPC's stats as the character owner holds them for the scripts' reads: the stored primary stats and magick and tech points, each clamped to its race's range, the entries of the given effect list laid over them.
    // Null when the stored values or the list do not fit the character rules.
    private CharacterStatsOwner? NpcScriptOwner(NpcWearer npc, IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions)
    {
        var stored = npc.Stored;
        if (stored.Count != SessionCritterIndexedWrites.StatCount || stored[27] is < 0 or > 10 || stored[26] is not (0 or 1)) return null;
        var created = CharacterStatsOwner.Create(new((CharacterRace)stored[27], stored[26] == 0 ? CharacterGender.Female : CharacterGender.Male, InnateEffectTable.Empty,
            CharacterStatLimits.Default with { MaxAppliedEffects = SessionNpcEffectLimits.MaxEntries }));
        if (created.Owner is not { } owner) return null;
        for (var index = 0; index < 8; index++)
            if (!SetStored(owner, (CharacterStat)index, stored[index])) return null;
        if (!SetStored(owner, CharacterStat.MagickPoints, stored[22]) || !SetStored(owner, CharacterStat.TechPoints, stored[23])) return null;
        foreach (var entry in entries)
            if (definitions.TryGetValue(entry.Number, out var rule) && EffectDomain.Stats(rule) is { } modifiers
                && owner.AddEffect(modifiers, EffectCause.Spell).Disposition == CharacterStatsDisposition.Rejected) return null;
        return owner;

        static bool SetStored(CharacterStatsOwner owner, CharacterStat stat, int value)
        {
            var range = owner.RangeOf(stat);
            return owner.SetBase(stat, Math.Clamp(value, range.Minimum, range.Maximum)).Disposition != CharacterStatsDisposition.Rejected;
        }
    }

    /// <summary>
    /// ARC-MAGIC-1644 (GS1, WE2): the stat <paramref name="stat"/> of an NPC wearer as get stat reads it (the stat getter, ledger GS1), by the original's number: the primary stats (0..7) and the aptitude (16) are the effective values under the
    /// given effect list with the NPC's poison and crippled legs, the level (17) and the alignment (19) the stored values; null for any other stat (GS3) and for stored values or a list the character rules refuse.
    /// </summary>
    private int? NpcScriptStat(NpcWearer npc, int stat, IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions)
    {
        if (stat == ScriptStatLevel) return npc.Stored.Count == SessionCritterIndexedWrites.StatCount ? npc.Stored[17] : null;
        if (stat == ScriptStatAlignment) return npc.Stored.Count == SessionCritterIndexedWrites.StatCount ? npc.Stored[19] : null;
        if (stat is not (>= 0 and <= 7 or ScriptStatAptitude)) return null;
        return NpcScriptSnapshot(npc, entries, definitions)?.GetEffective(stat == ScriptStatAptitude ? CharacterStat.MagickTechAptitude : (CharacterStat)stat);
    }

    // The NPC wearer's effective stats under the given effect list, with its poison and crippled legs; null for stored values or a list the character rules refuse.
    private CharacterStatsSnapshot? NpcScriptSnapshot(NpcWearer npc, IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions)
    {
        if (NpcScriptOwner(npc, entries, definitions) is not { } owner) return null;
        var state = QueryLife(npc.Target).View?.Record.State;
        var poison = state is { } life ? Math.Clamp(life.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel) : 0;
        var crippled = state is { } legs && legs.Conditions.HasFlag(LifeConditions.CrippledLegsBoth);
        return owner.Query(new(0, poison, crippled)).Snapshot;
    }

    /// <summary>
    /// ARC-MAGIC-1684: the NPC's stored stats with its advancement record's sheet laid over the ones the sheet holds (primary stats, level, unspent points, magick and tech points, race): a follower with a record keeps those
    /// there (ledger RM3c), so the scripts read the record's values.
    /// </summary>
    internal IReadOnlyList<int> NpcStatsOverSheet(string npc, IReadOnlyList<int> stats)
    {
        if (QueryNpcAdvancement(npc) is not { } advanced || stats.Count != SessionCritterIndexedWrites.StatCount) return stats;
        var values = stats.ToArray();
        foreach (var index in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 17, 21, 22, 23, 27 }) values[index] = SheetStat(advanced.Sheet, index)!.Value;
        return Array.AsReadOnly(values);
    }

    // The NPC wearer's aptitude as the power ratio reads it (0x461540): the effective aptitude within the equipment rules' range.
    private int? NpcScriptAptitude(NpcWearer npc, IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions) =>
        NpcScriptStat(npc, ScriptStatAptitude, entries, definitions) is { } aptitude ? Math.Clamp(aptitude, EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude) : null;

    /// <summary>
    /// ARC-MAGIC-1644 (ledger RM3c, IE7, WE6; exe read 0x465530, 0x441980): the item event scripts of the rewield 0x465530 for an NPC, planned and not applied. The rewield walks the wear slots 1000 to 1008 and for each worn item
    /// runs wield off (point 25) and then wield on (24), the item as the attachee and the NPC as the triggerer, with the condition "items are being rewielded" true (0x5E87E8); an item without a script on a point does nothing.
    /// The runs read the NPC's stats and aptitude as <paramref name="stored"/> gives them (the setter's array with the store of the move laid over) under the effect changes the earlier runs made, and change its effect list
    /// (cause item). Ready (<see cref="GameSessionFailure.None"/>, <c>Runs</c> null when no worn item has a wield script, so nothing runs) when the session lists the NPC (the holder of its map's inventory view, a dressed NPC's wear
    /// record laid over the map data); with a wield script and no <paramref name="host"/>, an NPC the session does not list, or a run the session cannot apply (a script outside the admitted set, an effect on another object, a rule the
    /// host does not know, a read that an earlier effect change of the same run would have moved, a list the life rules refuse), the failure that names it and nothing is applied. The light recalculation 0x467E80 is the host's
    /// (RM3d); the printed lines and eye candy of the runs are not shown here.
    /// </summary>
    [OriginalFunction(0x465530, OriginalFunctionScope.Partial)]
    internal (GameSessionFailure Failure, ItemScriptRuns? Runs) PlanNpcRewield(SessionMapId map, SessionLifeTarget target, string npc, IReadOnlyList<int> stored, ISessionItemScriptHost? host)
    {
        if (QueryMapInventory(map)?.Holders.FirstOrDefault(holder => holder.Entity == npc && holder.Kind == SessionHolderKind.Npc) is not { } listed) return (GameSessionFailure.ItemScriptUnsupported, null);
        var worn = listed.Items.Where(item => item.Facts.Worn && item.Facts.InventoryLocation is >= (int)EquipmentSlot.Helmet and <= (int)EquipmentSlot.Boots)
            .OrderBy(item => item.Facts.InventoryLocation).ToArray();
        var scripted = worn.Where(item => item.Facts.HasScript(SessionScriptPoint.WieldOn) || item.Facts.HasScript(SessionScriptPoint.WieldOff)).ToArray();
        if (scripted.Length == 0) return (GameSessionFailure.None, null);
        if (host is null) return (GameSessionFailure.ItemScriptUnsupported, null);
        if (_revision > long.MaxValue - 2) return (GameSessionFailure.RevisionExhausted, null);
        var revision = _revision;
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in worn) items[item.Entity] = item.Facts.Complexity;
        var runs = new ItemScriptRuns(this, host, map.RuntimeMap, items, new NpcWearer(npc, target, stored)) { Rewielding = true };
        foreach (var item in scripted)
        {
            if (item.Facts.HasScript(SessionScriptPoint.WieldOff)) runs.Run(SessionScriptPoint.WieldOff, item.Entity, npc, null);
            if (item.Facts.HasScript(SessionScriptPoint.WieldOn)) runs.Run(SessionScriptPoint.WieldOn, item.Entity, npc, null);
            if (runs.Failure != GameSessionFailure.None) return (runs.Failure, null);
        }
        ThrowIfDisposed();
        if (_revision != revision) return (GameSessionFailure.StaleVersion, null);
        runs.Prepare();
        return runs.Failure != GameSessionFailure.None ? (runs.Failure, null) : (GameSessionFailure.None, runs);
    }

    /// <summary>ARC-MAGIC-1644: applies the runs <see cref="PlanNpcRewield"/> prepared (ARC-MAGIC-1744: or <see cref="PlanHeroRewield"/>): their flags, script stores, kept slots, the wearer's effect changes and the deferred calls, one revision when they change anything.</summary>
    internal void CommitRewield(ItemScriptRuns? runs)
    {
        if (runs is null || !runs.Changes) return;
        runs.Commit();
        Revise();
    }
}
