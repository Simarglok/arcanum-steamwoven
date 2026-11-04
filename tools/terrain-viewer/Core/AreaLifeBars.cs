using Arcanum.Presentation.Hud;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-020: the values one critter's health and fatigue bars show: current and maximum hit points and fatigue (the life
/// rules' evaluation, so a dead critter's health is 0 or less) and its poison level. The PC's gauges, the follower portraits and
/// the hovered critter's bars all read it; <see cref="LifeBarRules"/> turns it into fills and texts.
/// </summary>
public readonly record struct AreaLife(int Health, int MaxHealth, int Fatigue, int MaxFatigue, int Poison)
{
    /// <summary>The original's poisoned art (stat 24, the poison level, above 0).</summary>
    public bool Poisoned => Poison > 0;

    public int HealthPercent => LifeBarRules.Percent(Health, MaxHealth);
    public int FatiguePercent => LifeBarRules.Percent(Fatigue, MaxFatigue);

    public static AreaLife From(SessionLifeView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var status = view.Status;
        return new(status.HitPoints, status.MaxHitPoints, status.Fatigue, status.MaxFatigue, view.Record.State.PoisonLevel);
    }

    /// <summary>The life of an NPC on a map the combat host covers (its combat values' life target under the session), or null when
    /// the host has no combat values for it there.</summary>
    public static AreaLife? Of(GameSessionOwner session, AreaCombatHost host, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(host);
        return host.Build(session, runtimeMap, entity).Combat is { } combat && session.QueryLife(combat.Target).View is { } view ? From(view) : null;
    }

    /// <summary>ARC-DEV-076 (MIG3): <see cref="Of(GameSessionOwner, AreaCombatHost, int, string)"/> by identity: the life of an NPC wherever it
    /// stands (a companion on a map other than its home map too), or null when the host has no combat values for it.</summary>
    public static AreaLife? Of(GameSessionOwner session, AreaCombatHost host, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(host);
        return host.Build(session, entity).Combat is { } combat && session.QueryLife(combat.Target).View is { } view ? From(view) : null;
    }

    /// <summary>ARC-DEV-076 (MIG3): <see cref="Unconscious(GameSessionOwner, AreaCombatHost, int, string)"/> by identity.</summary>
    public static bool? Unconscious(GameSessionOwner session, AreaCombatHost host, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(host);
        return host.Build(session, entity).Combat is { } combat && session.QueryLife(combat.Target).View is { } view ? view.Status.IsUnconscious : null;
    }

    /// <summary>ARC-DEV-073: whether the NPC is unconscious (the original's 0x45D800: not undead and no fatigue left) on a map the combat
    /// host covers; null when the host has no combat values for it there.</summary>
    public static bool? Unconscious(GameSessionOwner session, AreaCombatHost host, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(host);
        return host.Build(session, runtimeMap, entity).Combat is { } combat && session.QueryLife(combat.Target).View is { } view ? view.Status.IsUnconscious : null;
    }
}
