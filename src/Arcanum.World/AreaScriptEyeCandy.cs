using Arcanum.Scripting.Scr;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-1592 (ledger WE4, WE5): a script's play-eye-candy action (120) a host applied: the entry of the script eye-candy list (<c>Rules\ScriptEyeCandy.mes</c>) and the object it plays on. The hosts keep them
/// in action order (<see cref="AreaScriptHost.TakeEyeCandies"/>, <see cref="AreaUseScriptHost.TakeEyeCandies"/>) for the viewer to play through <see cref="AreaEyeCandyHost.PlayScript"/>; the run itself goes on at once, as the
/// original's action does (0x44C7A0 only starts the effect).
/// </summary>
public readonly record struct AreaScriptEyeCandy(ScrObjectRef Object, int Number)
{
    /// <summary>The requests of a run's effects, in action order.</summary>
    public static IEnumerable<AreaScriptEyeCandy> Of(ScrEffects effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return effects.EyeCandies.Select(eye => new AreaScriptEyeCandy(eye.Object, eye.Number));
    }
}
