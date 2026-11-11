using Arcanum.Data.Terrain;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-045 (ledger OD1–OD6): how a critter's object flags change the draw of its body, as the original's draw-list builder (0x43B390), its blit set-up (0x442520, 0x443170)
/// and its frame rectangle (0x443620) read them: the flags a spell writes at run time (<c>obj_f_flags</c>: don't-draw 0x100, shrunk 0x80, translucent 0x40, invisible 0x200) over
/// the value the map data stored. A body the data already hides or shrinks is placed so by the scene plan (<see cref="AreaScenePlan"/>); this applies the change a run made since.
/// </summary>
/// <param name="Drawn">Whether the body is drawn at all: false for don't-draw, and for invisible when the local player cannot see invisible. Overlays and eye candy are not the body and stay.</param>
/// <param name="Shrunk">Whether the body is drawn at half size (the frame and its hotspot halved, 0x443620).</param>
/// <param name="Alpha">The constant alpha of the body: 255, or <see cref="AreaBodyStyleRules.TranslucentAlpha"/> for a translucent (or seen invisible) body.</param>
public readonly record struct AreaBodyStyle(bool Drawn, bool Shrunk, int Alpha)
{
    /// <summary>The body as the data draws it.</summary>
    public static AreaBodyStyle Plain { get; } = new(true, false, 255);
}

/// <summary>The draw rules of the object flags; see <see cref="AreaBodyStyle"/>.</summary>
public static class AreaBodyStyleRules
{
    /// <summary>The object flags (<c>obj_f_flags</c>): translucent, shrunk, don't-draw, invisible.</summary>
    public const int Translucent = 0x40, Shrunk = 0x80, DontDraw = 0x100, Invisible = 0x200;

    /// <summary>The blit-flag bits (<c>obj_f_blit_flags</c> 0x10, 0x20, 0x40) that keep the blit set-up from choosing the constant alpha itself (0x442520 returns early when any is set).</summary>
    public const int OwnBlitModes = 0x70;

    /// <summary>The constant alpha the blit set-up gives a translucent or invisible body (0x442520 writes 0x80 as the first blit alpha byte).</summary>
    public const int TranslucentAlpha = 0x80;

    /// <summary>The spell flag of the local player that lets it see invisible objects (<c>OSF_DETECTS_INVISIBLE</c>, 0x200, tested by 0x458A80 with the mask 0x200).</summary>
    public const int DetectsInvisibleSpell = 0x200;

    /// <summary>
    /// The style of a body whose object flags now are <paramref name="objectFlags"/> and blit flags <paramref name="blitFlags"/>. <paramref name="seesInvisible"/> says whether the local player
    /// can see invisible objects (<see cref="Drawn"/>, <see cref="ConstantAlpha"/>, the shrink flag).
    /// </summary>
    public static AreaBodyStyle Of(int objectFlags, int blitFlags, bool seesInvisible) =>
        new(Drawn(objectFlags, seesInvisible), (objectFlags & Shrunk) != 0, ConstantAlpha(objectFlags, blitFlags));

    /// <summary>The draw-list builder (0x43B390) skips the body of a don't-draw object (0x100) and of an invisible one (0x200) when the local player cannot see invisible; the overlays beside it are drawn either way.</summary>
    [OriginalFunction(0x43B390, OriginalFunctionScope.Partial)]
    public static bool Drawn(int objectFlags, bool seesInvisible) => (objectFlags & DontDraw) == 0 && ((objectFlags & Invisible) == 0 || seesInvisible);

    /// <summary>The blit set-up (0x442520) gives a translucent (0x40) or invisible (0x200) body the constant alpha 0x80 unless the blit flags (0x70) choose their own mode; 255 otherwise.</summary>
    [OriginalFunction(0x442520, OriginalFunctionScope.Partial)]
    public static int ConstantAlpha(int objectFlags, int blitFlags) =>
        (objectFlags & (Translucent | Invisible)) != 0 && (blitFlags & OwnBlitModes) == 0 ? TranslucentAlpha : 255;

    /// <summary>The style that changes a body drawn as <paramref name="data"/> says (the data's own object flags) into the one <paramref name="now"/> asks for: what is left to apply to the commands the scene plan made.</summary>
    public static AreaBodyStyle Delta(AreaBodyStyle data, AreaBodyStyle now) =>
        new(now.Drawn, now.Shrunk && !data.Shrunk, now.Alpha);

    /// <summary>
    /// The command with <paramref name="style"/> applied: its constant alpha (a body that already blends another way keeps its blend) and the shrink (<see cref="Shrink"/>). The caller drops an undrawn body.
    /// </summary>
    public static AreaDrawCommand Apply(AreaDrawCommand command, AreaBodyStyle style)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = command;
        if (style.Alpha != 255 && !command.Additive && !command.Multiply && command.Alpha == 255) result = result with { Alpha = style.Alpha, Unlit = true };
        return style.Shrunk ? Shrink(result) : result;
    }

    /// <summary>The frame rectangle of a shrunk body (0x443620): the frame width, height and hotspot are each shifted right by one, so the rectangle halves about the hotspot. A command without a hotspot (<see cref="AreaDrawCommand.Hot"/>) keeps its size.</summary>
    [OriginalFunction(0x443620, OriginalFunctionScope.Partial)]
    public static AreaDrawCommand Shrink(AreaDrawCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Hot is not { } hot) return command;
        var at = command.Destination;
        var half = (hot.X >> 1, hot.Y >> 1);
        return command with
        {
            Destination = new TerrainRect(checked(at.X + hot.X - half.Item1), checked(at.Y + hot.Y - half.Item2), Math.Max(1, at.Width >> 1), Math.Max(1, at.Height >> 1)),
            Hot = half,
            WadeRows = command.WadeRows >> 1,
        };
    }
}
