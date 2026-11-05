using Arcanum.Data.Maps;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.ChangeOfPlace;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-113 (MIG11 slice S2): the request the viewer's transfers make to the session's change of place. A jump point the player enters
/// and an unconditional tile teleport request with flags 0 (ledger TR3), a script's plain teleport too (action 44, TR1); a script's
/// fade-and-teleport (action 102, TR2, read again for this slice at 0x449A85) requests the fade-out and the fade-in (0x6), the clock
/// advance when its seconds are positive (0x8), the sound when its number is positive (0x10) and the first movie when its number is not
/// zero, an unsigned test (0x1). Seconds past <see cref="ChangeOfPlaceRules.MaxAdvanceSeconds"/> are cut to it.
/// </summary>
public static class AreaChangeOfPlace
{
    /// <summary>The caller of a transfer set off by a site of <paramref name="trigger"/>: a jump point, or a script's teleport otherwise
    /// (a use, tile or sector script; an unconditional tile or use site is its script's teleport read ahead).</summary>
    public static ChangeOfPlaceCaller Caller(MapTransitionTrigger trigger) =>
        trigger == MapTransitionTrigger.JumpPoint ? ChangeOfPlaceCaller.JumpPoint : ChangeOfPlaceCaller.Script;

    /// <summary>The flags and the clock seconds of a request for a teleport with <paramref name="fade"/> (null: flags 0).</summary>
    public static (ChangeOfPlaceFlags Flags, int Seconds) Flags(ScrTeleportFade? fade)
    {
        if (fade is null) return (ChangeOfPlaceFlags.None, 0);
        var flags = ChangeOfPlaceFlags.FadeOut | ChangeOfPlaceFlags.FadeIn;
        if (fade.Seconds > 0) flags |= ChangeOfPlaceFlags.AdvanceClock;
        if (fade.Sound > 0) flags |= ChangeOfPlaceFlags.SoundEffect;
        if (fade.Movie != 0) flags |= ChangeOfPlaceFlags.FirstMovie;
        // The original multiplies the seconds by 1000 in 32 bits and would wrap; the project bounds them (decision CD3).
        return (flags, Math.Clamp(fade.Seconds, 0, ChangeOfPlaceRules.MaxAdvanceSeconds));
    }
}
