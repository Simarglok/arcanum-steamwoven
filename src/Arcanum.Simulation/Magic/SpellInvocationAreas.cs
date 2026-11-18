namespace Arcanum.Simulation.Magic;

/// <summary>
/// ARC-MAGIC-007: the invocation check's world (<see cref="ISpellInvocationWorld"/>, MT7) with its BEGIN-area questions answered
/// by the target rules (<see cref="SpellTargetRules"/>) as the check (0x4564E0) asks them: the context has the parent as self,
/// the invocation's source, the source's location as origin (the invocation's +0x38, filled from the source by 0x455A20), the target object and tile as both the candidate and the
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

    /// <summary>
    /// ARC-MAGIC-2655 (diagnostic only, no original function): the BEGIN-area mask bits whose removal alone lets the invocation's
    /// target pass the target area test, so a refused check can name the bits that failed. Empty when the target passes, or
    /// when no single bit explains the refusal (two bits fail together). Bit 0 of the answer is the mask's bit 0 (low word first).
    /// </summary>
    public ulong FailingBeginBits(SpellInvocation invocation)
    {
        var context = Context(invocation, SpellInvocationRules.FallbackRadius);
        if (SpellTargetRules.Test(context, targets, profile).Passes) return 0;
        var mask = context.Area.Targets;
        ulong failing = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            var one = 1UL << bit;
            if ((mask & one) != 0 && SpellTargetRules.Test(context with { Area = context.Area with { Targets = mask & ~one } }, targets, profile).Passes) failing |= one;
        }
        return failing;
    }

    /// <summary>
    /// ARC-MAGIC-2655 (diagnostic only): the 5 x 5 tiles around the target's location as the fallback sees them, rows from y-2 to y+2:
    /// B blocked tile, O blocking object on it, . free; "no-target-location" for the whole note when the target has no location.
    /// </summary>
    public string FallbackTilesNote(SpellInvocation invocation)
    {
        if (Resolve(invocation.TargetObject) is not { } target || target.Location.IsNone) return "no-target-location";
        var rows = new List<string>();
        var detail = new List<string>();
        for (var dy = -2; dy <= 2; dy++)
        {
            var row = "";
            for (var dx = -2; dx <= 2; dx++)
            {
                var tile = new SpellLocation((uint)(target.Location.X + dx), (uint)(target.Location.Y + dy));
                var blocking = targets.ObjectsAt(tile, 0x3FFEF).Where(o => !o.IsItem && (o.ObjectFlags & SpellTargetObject.NoBlock) == 0).ToArray();
                row += targets.TileBlocked(tile) ? 'B' : blocking.Length > 0 ? 'O' : '.';
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && blocking.Length > 0) detail.Add($"{dx},{dy}:" + string.Join("+", blocking.Select(o => $"t{o.Type}f{o.ObjectFlags:X}")));
            }
            rows.Add(row);
        }
        return "tiles=" + string.Join("/", rows) + " blockers=" + string.Join(";", detail);
    }

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
            invocation.Origin, target, invocation.TargetTile, target, invocation.TargetTile);
    }

    private SpellTargetObject? Resolve(SpellObjectFacts? facts) => facts is null ? null
        : targets.Object(facts.Id) ?? new SpellTargetObject(facts.Id, facts.Type, facts.Location, facts.ObjectFlags, facts.SpellFlags, facts.CritterFlags);
}
