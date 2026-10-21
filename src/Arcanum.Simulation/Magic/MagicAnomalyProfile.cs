namespace Arcanum.Simulation.Magic;

/// <summary>
/// The magic rule settings of owner decision D8 (ARC-MAGIC-005, the first magic slice with a switch): <see cref="Steamwoven"/>
/// (the original with the fixes the owner accepted on 2026-10-20, the default) and <see cref="Original"/> (the retail 1.0.7.4
/// behaviour). Each switch is one bug of the original confirmed in the executable and decided in the magic plan's anomaly
/// decisions (docs/research/magic-implementation-plan.md). The profile holds no state, so it may change between casts.
/// </summary>
public sealed record MagicAnomalyProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// MG4: the resistance term (a tenth of the margin the d100 gave, or of the resistance in the predictor) is subtracted from
    /// the target's saving throw, so resisting the magic first makes the save harder. Steamwoven adds it, as the manual says
    /// (+1 per 10 %).
    /// </summary>
    public bool SaveSubtractsResistance { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly MagicAnomalyProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly MagicAnomalyProfile Original = new() { Name = "original", SaveSubtractsResistance = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<MagicAnomalyProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static MagicAnomalyProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
