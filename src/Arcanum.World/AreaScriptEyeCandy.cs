using Arcanum.Scripting.Scr;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-1592 (ledger WE4, WE5): a script's play-eye-candy action (120) a host applied: the entry of the script eye-candy list (<c>Rules\ScriptEyeCandy.mes</c>) and the object it plays on. The hosts keep them
/// in action order (<see cref="AreaScriptHost.TakeEyeCandies"/>, <see cref="AreaUseScriptHost.TakeEyeCandies"/>) for the viewer to play through <see cref="AreaEyeCandyHost.PlayScript"/>; the run itself goes on at once, as the
/// original's action does (0x44C7A0 only starts the effect). ARC-SCRIPT-1828 (ledger WE12): <paramref name="Stop"/> marks the stop script eye candy action (124, 0x44C800) of the same entry and object, which the viewer takes off through
/// <see cref="AreaEyeCandyHost.StopScript"/> in the same order as the plays.
/// </summary>
public readonly record struct AreaScriptEyeCandy(ScrObjectRef Object, int Number, bool Stop = false)
{
    /// <summary>The requests of a run's effects, in action order.</summary>
    public static IEnumerable<AreaScriptEyeCandy> Of(ScrEffects effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return Of(effects.EyeCandies);
    }

    /// <summary>
    /// ARC-MAGIC-1830: the script-list requests among the eye candy requests a host holds (the session hands an NPC's rewield and best-items wield scripts' as they ran), in order: the plays (120) and, since ARC-SCRIPT-1828, the stops (124,
    /// <see cref="Stop"/>). Play spell (104) names the spell list (<see cref="ScrEyeCandyKind"/>), so it is neither.
    /// </summary>
    public static IEnumerable<AreaScriptEyeCandy> Of(IEnumerable<ScrEyeCandy> candies)
    {
        ArgumentNullException.ThrowIfNull(candies);
        return candies.Where(eye => eye.Kind is ScrEyeCandyKind.PlayScript or ScrEyeCandyKind.StopScript).Select(eye => new AreaScriptEyeCandy(eye.Object, eye.Number, eye.Kind == ScrEyeCandyKind.StopScript));
    }
}
