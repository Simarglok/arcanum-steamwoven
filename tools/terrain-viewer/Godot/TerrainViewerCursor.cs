using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    // The system shape last given to Godot as the default (the display server's own answer is Arrow when headless).
    private Godot.Input.CursorShape _cursorShapeSet = Godot.Input.CursorShape.Arrow;
    private AreaCursorContent? _cursorContentForSelfTest;
    // ARC-UI-063: the held item shown on the cursor (null: none) and its composed image for the art and frame shown.
    private string? _cursorHeldShown;
    private (string Item, int Art, int Frame, AreaCursorContent Content, ImageTexture? Texture, Vector2 Hot)? _heldCursorImage;

    internal int CursorArtForSelfTest => _cursorArt;
    internal (int Art, int Frame)? CursorShownForSelfTest => _cursorShown;
    internal Godot.Input.CursorShape CursorShapeSetForSelfTest => _cursorShapeSet;
    internal string? CursorHeldShownForSelfTest => _cursorHeldShown;

    /// <summary>The default system cursor shape; every change goes through here so the self-test can read it.</summary>
    private void SetCursorShape(Godot.Input.CursorShape shape)
    {
        _cursorShapeSet = shape;
        Godot.Input.SetDefaultCursorShape(shape);
    }

    /// <summary>Self-test: the cursor clock stands at <paramref name="at"/> ms and the shown art's animation starts there.</summary>
    internal void SetCursorClockForSelfTest(ulong? at, bool restart = false)
    {
        _cursorClockForSelfTest = at;
        if (restart && at is { } now) _cursorSince = now;
    }

    private ulong CursorClock => _cursorClockForSelfTest ?? Time.GetTicksMsec();

    /// <summary>The interface mode that picks the cursor. ARC-DEV-109 (MIG16 S2) and ARC-DEV-110 (S3): the cursor family of the stack's top
    /// mode (SA41: the spell cursor in spell targeting 1 and in item use 16, the skill cursor in skill targeting 2, the game cursor in 0
    /// and the follower order 18, the gauntlet in a window mode and in the quantity picker); a panel outside the stack (palette, main
    /// menu, save list, bug report, hero-death cover) covers any mode with the gauntlet.</summary>
    private AreaCursorMode CursorMode
    {
        get
        {
            // A panel outside the stack (palette, main menu, save list, bug report, death cover) covers whatever mode is on top.
            if (!PanelOutsideStackOpen && WorldMapOpen && _worldMapPanel is { QuillActive: true }) return AreaCursorMode.Quill; // ARC-UI-106 (WM30): the quill shows cursor art 21.
            return WorldModal ? AreaCursorMode.Window : AreaCursorArt.Mode(_interfaceWindows.Stack.Cursor);
        }
    }

    /// <summary>Per frame after the hover: picks the art, steps an animated one and shows it (cheap unless one of them changed).</summary>
    private void SyncCursorArt()
    {
        if (_exiting || _readyMode != TerrainViewerMode.Area || CursorContent() is not { Ready: true } content)
        {
            if (_cursorShown is not null)
            {
                _cursorShown = null; _cursorHeldShown = null;
                Godot.Input.SetCustomMouseCursor(null);
                SetCursorShape(_hoverCursor);
            }
            return;
        }
        var art = AreaCursorArt.Art(CursorMode, HeroInCombatMode, HeroInCombatMode ? HeldAim() : null,
            _areaSession?.Rules.Interface.CalledShotCursorsSwapped == true);
        var now = CursorClock;
        if (art != _cursorArt)
        {
            _cursorArt = art; _cursorSince = now;
            GD.Print($"ARC-UI-056 cursor art={art} mode={CursorMode}");
        }
        var load = content.Art[art];
        var frame = AreaCursorArt.Frame((long)(now - Math.Min(_cursorSince, now)), load.Frames.Count, load.FramesPerSecond);
        // ARC-UI-063: an item held on the game cursor is the cursor's image (CU7).
        var held = HeldCursorImage(art, frame, content, load.Frames[frame]);
        if (_cursorShown == (art, frame) && _cursorHeldShown == held?.Item && ReferenceEquals(_cursorTexturesOf, content)) return;
        if (!ReferenceEquals(_cursorTexturesOf, content)) { _cursorTextures.Clear(); _cursorTexturesOf = content; }
        if (!_cursorTextures.TryGetValue(art, out var textures))
            _cursorTextures[art] = textures = load.Frames
                .Select(f => ImageTexture.CreateFromImage(Image.CreateFromData(f.Width, f.Height, false, Image.Format.Rgba8, f.Rgba))).ToArray();
        var shown = load.Frames[frame];
        // The frame's hot point goes on the pointer (the mouse library, 0x4FFEC0).
        if (held is { } image) Godot.Input.SetCustomMouseCursor(image.Texture, Godot.Input.CursorShape.Arrow, image.Hot);
        else Godot.Input.SetCustomMouseCursor(textures[frame], Godot.Input.CursorShape.Arrow, new Vector2(shown.HotX, shown.HotY));
        if (_cursorShown is null) SetCursorShape(Godot.Input.CursorShape.Arrow);
        if (_cursorHeldShown != held?.Item) GD.Print($"ARC-UI-063 cursor held={held?.Item ?? "none"} art={art}");
        _cursorShown = (art, frame); _cursorHeldShown = held?.Item;
    }

    /// <summary>ARC-UI-063: the item held on the game cursor composed with the cursor art's frame (<see cref="AreaHeldCursor.Compose"/>);
    /// null when nothing is held or its inventory art does not read (a failure is kept for the art and frame, not tried every frame).
    /// PROJECT: the inventory window keeps its own drag image at its scale and grab offset, which its drop rule (IW10–IW13) reads.</summary>
    private (string Item, ImageTexture Texture, Vector2 Hot)? HeldCursorImage(int art, int frame, AreaCursorContent content, AreaInterfaceFrame cursor)
    {
        var item = _gameCursorItem?.Item;
        if (item is null) { _heldCursorImage = null; return null; }
        if (_heldCursorImage is { } cached && cached.Item == item && cached.Art == art && cached.Frame == frame && ReferenceEquals(cached.Content, content))
            return cached.Texture is { } kept ? (item, kept, cached.Hot) : null;
        _heldCursorImage = (item, art, frame, content, null, default);
        if (InventoryItemArt(item) is not { } itemArt || InterfaceHd.LegacyImage(itemArt) is not { } image) return null;
        if (image.IsCompressed() && image.Decompress() != Error.Ok) return null;
        if (image.HasMipmaps()) image.ClearMipmaps();
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        if (AreaHeldCursor.Compose(image.GetWidth(), image.GetHeight(), image.GetData(), cursor) is not { } composed) return null;
        var texture = ImageTexture.CreateFromImage(Image.CreateFromData(composed.Width, composed.Height, false, Image.Format.Rgba8, composed.Rgba));
        var hot = new Vector2(composed.HotX, composed.HotY);
        _heldCursorImage = (item, art, frame, content, texture, hot);
        return (item, texture, hot);
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
