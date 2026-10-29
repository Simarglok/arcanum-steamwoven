using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-056: the original's cursor art in crash-site-area (docs/rendering/terrain-viewer.md#original-cursor-art-arc-ui-056;
// ledger CU1–CU6). The art follows the interface mode, the hero's combat mode and a held called-shot key, never the object under
// the pointer, and replaces the system cursor shapes of the action cursor (ARC-UI-005) while it is read; without it (the self-test,
// an unreadable game folder) those shapes stay. One cursor serves the world and the viewer's windows, as the original's does.
public partial class TerrainViewerApp
{
    private AreaCursorContent? _cursorLoaded;
    private string? _cursorModule;
    // The textures made from this content's frames, per art number, on first use.
    private AreaCursorContent? _cursorTexturesOf;
    private readonly Dictionary<int, ImageTexture[]> _cursorTextures = [];
    // The art and frame shown, or null while the system shapes are.
    private (int Art, int Frame)? _cursorShown;
    private int _cursorArt = AreaCursorArt.Gauntlet;
    private ulong _cursorSince;
    private ulong? _cursorClockForSelfTest;
    private AreaCursorContent? _cursorContentForSelfTest;

    internal int CursorArtForSelfTest => _cursorArt;
    internal (int Art, int Frame)? CursorShownForSelfTest => _cursorShown;

    /// <summary>Self-test: the cursor clock stands at <paramref name="at"/> ms and the shown art's animation starts there.</summary>
    internal void SetCursorClockForSelfTest(ulong? at, bool restart = false)
    {
        _cursorClockForSelfTest = at;
        if (restart && at is { } now) _cursorSince = now;
    }

    private ulong CursorClock => _cursorClockForSelfTest ?? Time.GetTicksMsec();

    /// <summary>The interface mode that picks the cursor: the spell cursor while it is open, a window while one covers the game.</summary>
    private AreaCursorMode CursorMode => _spellTargeting is not null ? AreaCursorMode.SpellTarget : WorldModal ? AreaCursorMode.Window : AreaCursorMode.Game;

    /// <summary>Per frame after the hover: picks the art, steps an animated one and shows it (cheap unless one of them changed).</summary>
    private void SyncCursorArt()
    {
        if (_exiting || _readyMode != TerrainViewerMode.CrashSiteArea || CursorContent() is not { Ready: true } content)
        {
            if (_cursorShown is not null)
            {
                _cursorShown = null;
                Godot.Input.SetCustomMouseCursor(null);
                Godot.Input.SetDefaultCursorShape(_hoverCursor);
            }
            return;
        }
        var art = AreaCursorArt.Art(CursorMode, HeroInCombatMode, HeroInCombatMode ? HeldAim() : null,
            _areaSession?.Rules.Interface.CalledShotCursorsSwapped == true);
        if (art != _cursorArt)
        {
            _cursorArt = art; _cursorSince = CursorClock;
            GD.Print($"ARC-UI-056 cursor art={art} mode={CursorMode}");
        }
        var load = content.Art[art];
        var frame = AreaCursorArt.Frame((long)(CursorClock - Math.Min(_cursorSince, CursorClock)), load.Frames.Count, load.FramesPerSecond);
        if (_cursorShown == (art, frame) && ReferenceEquals(_cursorTexturesOf, content)) return;
        if (!ReferenceEquals(_cursorTexturesOf, content)) { _cursorTextures.Clear(); _cursorTexturesOf = content; }
        if (!_cursorTextures.TryGetValue(art, out var textures))
            _cursorTextures[art] = textures = load.Frames
                .Select(f => ImageTexture.CreateFromImage(Image.CreateFromData(f.Width, f.Height, false, Image.Format.Rgba8, f.Rgba))).ToArray();
        var shown = load.Frames[frame];
        // The frame's hot point goes on the pointer (the mouse library, 0x4FFEC0).
        Godot.Input.SetCustomMouseCursor(textures[frame], Godot.Input.CursorShape.Arrow, new Vector2(shown.HotX, shown.HotY));
        if (_cursorShown is null) Godot.Input.SetDefaultCursorShape(Godot.Input.CursorShape.Arrow);
        _cursorShown = (art, frame);
    }

    /// <summary>Synthetic cursor art for the self-test: a 2 × 2 frame for each art, the spell cursor three frames at 10 fps.</summary>
    private static AreaCursorContent SyntheticCursorContent() => new(AreaCursorArt.ArtNumbers.ToDictionary(number => number,
        number => new AreaInterfaceArtLoad(number, AreaInterfaceArtStatus.Ready, $"synthetic {number}",
            Enumerable.Range(0, number == AreaCursorArt.Spell ? 3 : 1).Select(i => new AreaInterfaceFrame(2, 2, [.. Enumerable.Repeat((byte)(40 * i + 80), 16)], 1, 1)).ToArray(),
            "synthetic", 10)), "synthetic");

    /// <summary>The cursor art of this module, read once a run (the self-test gets none unless it injects its own).</summary>
    private AreaCursorContent? CursorContent()
    {
        if (_cursorContentForSelfTest is { } injected) return injected;
        var module = _arguments?.ModulePath;
        if (_cursorLoaded is null || module != _cursorModule)
        {
            _cursorModule = module;
            _cursorLoaded = SelfTestBootstrap || module is null ? AreaCursorContent.Empty("self-test: no game folder") : AreaCursorContent.Load(module);
            GD.Print($"ARC-UI-056 cursor art ready={_cursorLoaded.Ready} {_cursorLoaded.Detail}");
        }
        return _cursorLoaded;
    }
}
