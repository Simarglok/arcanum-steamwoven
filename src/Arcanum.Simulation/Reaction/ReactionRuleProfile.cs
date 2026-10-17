namespace Arcanum.Simulation.Reaction;

/// <summary>
/// The two reaction rule settings of owner decision D8 (contract docs/world/rule-modes.md): <see cref="Steamwoven"/> (the
/// original with the recorded fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour). Each switch is
/// a bug of the original game confirmed in its executable (verification ledger). The profile holds no state, so it may
/// change between any two reaction decisions.
/// </summary>
public sealed record ReactionRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Reputation A2: a deliberate hit by the PC on its own follower <b>adds</b> AI parameter 9 to the follower's reaction, as
    /// the retail executable does (<c>ai_attack</c> 0x4A9650). The field's own description (how much the hit lowers the
    /// reaction) is the evidence for intent; Steamwoven lowers it by parameter 9 (owner decision 2026-10-17).
    /// </summary>
    public bool FollowerHitRaisesReaction { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly ReactionRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly ReactionRuleProfile Original = new() { Name = "original", FollowerHitRaisesReaction = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<ReactionRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static ReactionRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
