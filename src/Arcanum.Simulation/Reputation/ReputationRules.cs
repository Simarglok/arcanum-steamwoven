namespace Arcanum.Simulation.Reputation;

/// <summary>One effect of a reputation: <see cref="Adjustment"/> for an NPC whose origin and faction match (0 = any).</summary>
public readonly record struct ReputationEffectDefinition(int Adjustment, int Origin, int Faction);

/// <summary>A reputation's rules: the factions it makes the holder a member of (0 = none) and its reaction effects.</summary>
public sealed record ReputationDefinition(int Number, IReadOnlyList<int> Factions, IReadOnlyList<ReputationEffectDefinition> Effects);

/// <summary>
/// The module's reputations by number (the host builds it from <c>rules\gamerep.mes</c>). A number outside 1000..1999, a repeated
/// number, more than three factions or five effects, or an adjustment beyond ±1,000,000 is refused (project bounds; the
/// original's table has 1000 records of three factions and five effects).
/// </summary>
public sealed class ReputationCatalog
{
    private readonly Dictionary<int, ReputationDefinition> _definitions;

    private ReputationCatalog(Dictionary<int, ReputationDefinition> definitions) => _definitions = definitions;

    public static ReputationCatalog Empty { get; } = new([]);

    public int Count => _definitions.Count;

    /// <summary>The catalogue, or null when a definition is out of the bounds above.</summary>
    public static ReputationCatalog? Create(IEnumerable<ReputationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var map = new Dictionary<int, ReputationDefinition>();
        foreach (var definition in definitions)
        {
            if (definition is null || !ReputationRules.IsNumber(definition.Number) || definition.Factions is null || definition.Effects is null
                || definition.Factions.Count > ReputationRules.FactionCount || definition.Effects.Count > ReputationRules.MaxEffects
                || definition.Effects.Any(effect => effect.Adjustment is < -ReputationRules.MaxAdjustment or > ReputationRules.MaxAdjustment)
                || !map.TryAdd(definition.Number, definition with { Factions = definition.Factions.ToArray(), Effects = definition.Effects.ToArray() }))
                return null;
        }
        return new(map);
    }

    public ReputationDefinition? Find(int number) => _definitions.GetValueOrDefault(number);
}

/// <summary>
/// A party to the faction test (0x45D110): an object id, whether it is a PC, its faction (an NPC's faction field; 0 for a PC,
/// which has none), its direct leader and its PC leader (the end of its leader chain when that is a PC), both null when absent.
/// </summary>
public sealed record FactionMember(string Id, bool IsPc, int Faction, string? Leader, string? PcLeader);

/// <summary>
/// ARC-WORLD-044: the PC reputation rules, read in the retail 1.0.7.4 executable by two readers (ledger "Reputations"). Pure
/// functions over the PC's held numbers and the module's <see cref="ReputationCatalog"/>; the session owns the list. The AI
/// slices consume <see cref="GrantsFaction"/>, <see cref="FactionSame"/> and <see cref="OriginSame"/> from here.
/// </summary>
public static class ReputationRules
{
    public const int First = 1000, Last = 1999, FactionCount = 3, MaxEffects = 5;
    /// <summary>Largest magnitude of one effect's adjustment (project bound, as the reaction owner's).</summary>
    public const int MaxAdjustment = 1_000_000;

    /// <summary>Whether a reputation number is in the table's range (the original stores any number, A5).</summary>
    public static bool IsNumber(int number) => number is >= First and <= Last;

    /// <summary>
    /// The reputation term of a PC's reaction to an NPC (0x4C1AC0): for every held reputation and every effect whose origin is 0
    /// or the NPC's origin and whose faction is 0 or the NPC's faction, its adjustment; all of them add up. A number the catalogue
    /// does not hold contributes nothing (the original's record of it is empty). Not skipped for aloof or monstrous NPCs.
    /// </summary>
    [OriginalFunction(0x4C1AC0, OriginalFunctionScope.Full)]
    public static long ReactionSum(IReadOnlyList<int> held, ReputationCatalog catalog, int npcOrigin, int npcFaction)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(catalog);
        long sum = 0;
        foreach (var number in held)
        {
            if (catalog.Find(number) is not { } definition) continue;
            foreach (var effect in definition.Effects)
                if ((effect.Origin == 0 || effect.Origin == npcOrigin) && (effect.Faction == 0 || effect.Faction == npcFaction)) sum += effect.Adjustment;
        }
        return sum;
    }

    /// <summary>Whether the held reputations make the PC a member of <paramref name="faction"/> (0x4C21E0): never for faction 0,
    /// else when one of the three factions of a held reputation is it.</summary>
    [OriginalFunction(0x4C21E0, OriginalFunctionScope.Full)]
    public static bool GrantsFaction(IReadOnlyList<int> held, ReputationCatalog catalog, int faction)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(catalog);
        if (faction == 0) return false;
        foreach (var number in held)
            if (catalog.Find(number) is { } definition && definition.Factions.Contains(faction)) return true;
        return false;
    }

    /// <summary>
    /// Whether two critters are of one faction (0x45D110). Two PCs never are. A PC and a non-PC are when the non-PC's PC leader is
    /// that PC, or its faction is not 0 and the PC's reputations grant it (<paramref name="pcReputations"/>, the session PC's: a
    /// single-PC game). Two non-PCs are when they share a non-null PC leader, one is the other's leader, they share a non-null
    /// leader, or they have the same non-zero faction.
    /// </summary>
    [OriginalFunction(0x45D110, OriginalFunctionScope.Partial)]
    public static bool FactionSame(FactionMember a, FactionMember b, IReadOnlyList<int> pcReputations, ReputationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.IsPc && b.IsPc) return false;
        if (a.IsPc || b.IsPc)
        {
            var (pc, other) = a.IsPc ? (a, b) : (b, a);
            return other.PcLeader == pc.Id || GrantsFaction(pcReputations, catalog, other.Faction);
        }
        return a.PcLeader is not null && a.PcLeader == b.PcLeader
            || a.Leader == b.Id || b.Leader == a.Id
            || a.Leader is not null && a.Leader == b.Leader
            || a.Faction != 0 && a.Faction == b.Faction;
    }

    /// <summary>Whether two critters share an origin (0x45D320): both non-zero and equal (a PC has origin 0, so never).</summary>
    [OriginalFunction(0x45D320, OriginalFunctionScope.Full)]
    public static bool OriginSame(int a, int b) => a != 0 && a == b;
}
