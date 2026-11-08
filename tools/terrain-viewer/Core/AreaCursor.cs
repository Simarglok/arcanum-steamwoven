using Arcanum.Data.Content;
using Arcanum.Simulation.Combat;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-UI-056: the interface mode on top of the original's mode stack, as far as it picks the cursor (ledger CU1): the
/// game (mode 0), the spell targeting cursor (mode 1), the skill targeting cursor (mode 2), or any other mode (a window).</summary>
public enum AreaCursorMode { Game, SpellTarget, SkillTarget, Window, Quill }

/// <summary>
/// ARC-UI-056: the original's cursor art, as the 1.0.7.4 executable picks it (ledger CU1–CU6). The cursor follows the interface
/// mode and, in the game, the PC's combat mode and a called-shot key; it never changes with the object under it. Godot-free.
/// </summary>
public static class AreaCursorArt
{
    /// <summary>The interface art numbers (<c>art\interface\interface.mes</c>): <c>cursor.art</c>, <c>spellcur.art</c>,
    /// <c>skillcur.art</c>, <c>battlecur.art</c> and the called-shot cursors <c>Cursor-Called-Head/Leg/Arm.ART</c>.</summary>
    public const int Gauntlet = 0, Spell = 21, Skill = 352, Battle = 353, CalledHead = 818, CalledLeg = 819, CalledArm = 820;

    public static IReadOnlyList<int> ArtNumbers { get; } = [Gauntlet, Spell, Skill, Battle, CalledHead, CalledLeg, CalledArm];

    /// <summary>
    /// The art of the cursor refresh (0x553990, CU1–CU3): the mode table at 0x5C7178 gives the spell (mode 1) and skill (mode 2)
    /// cursors and the gauntlet for every window mode; in the game a PC whose art carries a weapon, which the combat-mode toggle
    /// sets on entering and clears on leaving (0x4B3F80), gets the battle cursor, or with a called-shot key held (comma first, then
    /// period, then slash, as <paramref name="aim"/> is read) that key's cursor; any other PC the gauntlet.
    /// </summary>
    /// <param name="aim">The location the held key aims at (ledger FA4: comma the head, period the arm, slash the leg).</param>
    /// <param name="calledShotCursorsSwapped">CUA1 (Original): the period shows the leg picture and the slash the arm picture, as
    /// the executable picks them by key; Steamwoven shows the aimed location's picture.</param>
    public static int Art(AreaCursorMode mode, bool combatMode, HitLocation? aim, bool calledShotCursorsSwapped) => mode switch
    {
        AreaCursorMode.SpellTarget => Spell,
        AreaCursorMode.SkillTarget => Skill,
        AreaCursorMode.Window => Gauntlet,
        AreaCursorMode.Quill => Spell,
        _ when !combatMode => Gauntlet,
        _ => aim switch
        {
            HitLocation.Head => CalledHead,
            HitLocation.Arm => calledShotCursorsSwapped ? CalledLeg : CalledArm,
            HitLocation.Leg => calledShotCursorsSwapped ? CalledArm : CalledLeg,
            _ => Battle,
        },
    };

    /// <summary>
    /// ARC-DEV-109 (MIG16 S2): the cursor mode of the cursor family the interface mode stack gives for its top mode (SA41,
    /// <see cref="InterfaceModes.Cursor"/>): the game cursor for mode 0 (and the follower order), the spell cursor, the skill cursor, and
    /// the gauntlet for every window mode (<see cref="AreaCursorMode.Window"/>).
    /// </summary>
    public static AreaCursorMode Mode(InterfaceCursor cursor) => cursor switch
    {
        InterfaceCursor.Spell => AreaCursorMode.SpellTarget,
        InterfaceCursor.Skill => AreaCursorMode.SkillTarget,
        InterfaceCursor.Gauntlet => AreaCursorMode.Window,
        _ => AreaCursorMode.Game,
    };

    /// <summary>The frame an animated cursor shows <paramref name="elapsedMs"/> after its art was set (CU5): the mouse library
    /// steps to the next frame, wrapping to the first, once 1000 / fps milliseconds have passed; one frame or no rate stays on 0.
    /// This is the original's count with a ping every frame: after a long hitch the original steps one frame, this jumps to the
    /// frame of the elapsed time.</summary>
    public static int Frame(long elapsedMs, int frames, int framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMs);
        if (frames <= 1 || framesPerSecond <= 0) return 0;
        var step = 1000 / framesPerSecond;
        return step <= 0 ? 0 : (int)(elapsedMs / step % frames);
    }
}

/// <summary>ARC-UI-056: the cursor art read from the game folder (<see cref="AreaCursorArt.ArtNumbers"/>). Godot-free.</summary>
public sealed record AreaCursorContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string Detail)
{
    /// <summary>The largest cursor image a system cursor takes (Godot's custom cursor limit).</summary>
    public const int MaxSide = 256;

    /// <summary>True when every cursor art read with at least one frame, each at most <see cref="MaxSide"/> square with its hot
    /// point inside it; otherwise the system shapes stay.</summary>
    public bool Ready => AreaCursorArt.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready
        && load.Frames.Count > 0 && load.Frames.All(f => f.Width is > 0 and <= MaxSide && f.Height is > 0 and <= MaxSide
            && f.HotX >= 0 && f.HotX < f.Width && f.HotY >= 0 && f.HotY < f.Height));

    /// <summary>No game folder (the self-test) or an unreadable one: no art, the system cursor shapes stay.</summary>
    public static AreaCursorContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), reason);

    /// <summary>Reads the cursor art from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown.</summary>
    public static AreaCursorContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, AreaCursorArt.ArtNumbers);
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, failed.Length == 0 ? $"{art.Count} cursor arts" : "failed: " + string.Join("; ", failed));
    }
}
