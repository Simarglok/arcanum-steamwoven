namespace Arcanum.Simulation.Magic;

/// <summary>
/// ARC-MAGIC-007: the invocation check's world (<see cref="ISpellInvocationWorld"/>, MT7) with its BEGIN-area questions answered
/// by the target rules (<see cref="SpellTargetRules"/>) as the check (0x4564E0) asks them: the context has the parent as self,
/// the invocation's source, the parent's location as origin, the target object and tile as both the candidate and the
/// action's target, the record's BEGIN target mask alone (no spell flags) with radius 2 and no count. The fallback marks the
/// target's location as candidate and target tile, adds the tile-radius and indoor/outdoor bits (high 0x1200000), gathers the
/// list and answers the first tile of it that passes the same test. The line of fire is the caller's (the AI awareness stage
/// I2 owns it). Objects are resolved through the target world by id; one it does not know keeps the invocation's facts.
/// </summary>
public sealed class SpellInvocationAreas(ISpellTargetWorld targets, Func<int, ulong> beginTargets,
    Func<SpellObjectFacts, SpellLocation, SpellLineOfFire> lineOfFire, MagicAnomalyProfile profile) : ISpellInvocationWorld
{
    /// <summary>The bits the fallback adds to the mask's high word: tile radius (0x200000) and indoor/outdoor match (0x1000000).</summary>
    public const uint FallbackBits = 0x1200000;

    public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => lineOfFire(parent, tile);

    public bool TargetMatchesBeginArea(SpellInvocation invocation) =>
        SpellTargetRules.Test(Context(invocation, SpellInvocationRules.FallbackRadius), targets, profile).Passes;

    public bool BeginAreaTakesTiles(int spell) => ((beginTargets(spell) >> 32) & 0x8000) != 0;

    public SpellLocation FirstTileInBeginArea(SpellInvocation invocation, int radius)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var context = Context(invocation, radius);
        if (context.Target is null) return SpellLocation.None;
        var location = context.Target.Location;
        context = context with
        {
            Area = context.Area with { Targets = context.Area.Targets | ((ulong)FallbackBits << 32) },
            CandidateTile = location,
            TargetTile = location,
        };
        foreach (var entry in SpellTargetRules.Collect(context, targets, profile).Entries)
            if (!entry.Tile.IsNone && SpellTargetRules.Test(context with { CandidateTile = entry.Tile }, targets, profile).Passes) return entry.Tile;
        return SpellLocation.None;
    }

    private SpellTargetContext Context(SpellInvocation invocation, int radius)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var target = Resolve(invocation.TargetObject);
        return new(new SpellTargetArea(beginTargets(invocation.Spell), 0, 0, radius, 0), Resolve(invocation.Parent), Resolve(invocation.Source),
            invocation.Parent?.Location ?? SpellLocation.None, target, invocation.TargetTile, target, invocation.TargetTile);
    }

    private SpellTargetObject? Resolve(SpellObjectFacts? facts) => facts is null ? null
        : targets.Object(facts.Id) ?? new SpellTargetObject(facts.Id, facts.Type, facts.Location, facts.ObjectFlags, facts.SpellFlags, facts.CritterFlags);
}
