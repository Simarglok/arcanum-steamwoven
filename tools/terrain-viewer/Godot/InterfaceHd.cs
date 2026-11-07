using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-089: the HD interface art switch of the viewer. The active pack and the visual profile (ARC-UI-006, the saved Legacy/HD
/// setting) decide whether the interface textures draw their HD redraws; <see cref="HdInterfaceTexture"/> keeps its Legacy size and Legacy
/// pixels for everything that reads them, so a window's layout, hit tests and sizes never change. Off, or without a pack entry for a frame,
/// every texture draws exactly the Legacy pixels it drew before.
/// </summary>
public static class InterfaceHd
{
    private static readonly List<WeakReference<HdInterfaceTexture>> Textures = [];

    /// <summary>The active pack, or null (no pack found or none opened).</summary>
    public static HdPack? Pack { get; private set; }

    /// <summary>True while the HD visual profile is the active one.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>Raised on the main thread after <see cref="Pack"/> or <see cref="Enabled"/> changed; nodes that draw by hand redraw.</summary>
    public static event Action? Changed;

    /// <summary>Sets the pack and the profile; textures repaint only when one of them changed.</summary>
    public static void Configure(HdPack? pack, bool enabled)
    {
        if (ReferenceEquals(pack, Pack) && enabled == Enabled) return;
        Pack = pack; Enabled = enabled;
        lock (Textures)
        {
            for (var i = Textures.Count - 1; i >= 0; i--)
                if (Textures[i].TryGetTarget(out var texture)) texture.Refresh(); else Textures.RemoveAt(i);
        }
        Changed?.Invoke();
        GD.Print($"ARC-UI-089 interface HD enabled={enabled} pack={(pack is null ? "none" : $"{pack.Id} {pack.Scale}x")}");
    }

    /// <summary>The texture of an interface frame: Legacy pixels, with its HD redraw behind the same size when the frame has a key.</summary>
    public static Texture2D Texture(AreaInterfaceFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var legacy = ImageTexture.CreateFromImage(Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Rgba));
        if (frame.Hd is null) return legacy;
        var texture = new HdInterfaceTexture(legacy, frame);
        lock (Textures) Textures.Add(new(texture));
        return texture;
    }

    /// <summary>Frames that currently draw their HD redraw (for the self-test and the status line).</summary>
    public static int ActiveCount()
    {
        lock (Textures) return Textures.Count(t => t.TryGetTarget(out var texture) && texture.IsHd);
    }
}

/// <summary>
/// A texture that reports its Legacy size and draws its HD redraw (<see cref="InterfaceHd.Pack"/>'s <c>interface</c> entry for the frame,
/// bound by <see cref="HdPack.ResolveSprite"/> against the Legacy pixels) when the HD profile is on. Source rectangles stay in Legacy texels
/// and are scaled by the pack's factor; tiling uses the Legacy texture. The HD image is decoded on first draw, once per pack.
/// </summary>
public partial class HdInterfaceTexture : Texture2D
{
    private readonly ImageTexture _legacy = null!;
    private readonly AreaInterfaceFrame _frame = null!;
    private ImageTexture? _hd;
    private HdPack? _hdFor;
    private bool _failed;

    public HdInterfaceTexture() { }

    internal HdInterfaceTexture(ImageTexture legacy, AreaInterfaceFrame frame) { _legacy = legacy; _frame = frame; }

    /// <summary>True when the next draw uses the HD redraw.</summary>
    public bool IsHd => Active() is not null;

    internal int HdScale => InterfaceHd.Pack?.Scale ?? 1;

    internal void Refresh() => EmitChanged();

    private ImageTexture? Active()
    {
        if (!InterfaceHd.Enabled || InterfaceHd.Pack is not { } pack || _frame?.Hd is not { } query) return null;
        if (!ReferenceEquals(_hdFor, pack)) { _hdFor = pack; _hd = null; _failed = false; }
        if (_hd is null && !_failed)
        {
            var result = pack.ResolveSprite(query, _frame.Rgba);
            if (result.Status == HdFrameStatus.Hd && result.Rgba is { } rgba)
                _hd = ImageTexture.CreateFromImage(Image.CreateFromData(result.Width, result.Height, false, Image.Format.Rgba8, rgba));
            else _failed = true;
        }
        return _hd;
    }

    public override int _GetWidth() => _frame?.Width ?? 1;
    public override int _GetHeight() => _frame?.Height ?? 1;
    public override bool _HasAlpha() => true;
    public override bool _IsPixelOpaque(int x, int y) => _frame is not null && x >= 0 && y >= 0 && x < _frame.Width && y < _frame.Height && _frame.Rgba[(y * _frame.Width + x) * 4 + 3] > 0;

    public override void _Draw(Rid toCanvasItem, Vector2 pos, Color modulate, bool transpose)
        => RenderingServer.CanvasItemAddTextureRect(toCanvasItem, new Rect2(pos, new Vector2(_frame.Width, _frame.Height)), (Active() ?? _legacy).GetRid(), false, modulate, transpose);

    public override void _DrawRect(Rid toCanvasItem, Rect2 rect, bool tile, Color modulate, bool transpose)
        => RenderingServer.CanvasItemAddTextureRect(toCanvasItem, rect, (tile ? _legacy : Active() ?? _legacy).GetRid(), tile, modulate, transpose);

    public override void _DrawRectRegion(Rid toCanvasItem, Rect2 rect, Rect2 srcRect, Color modulate, bool transpose, bool clipUv)
    {
        var hd = Active();
        var source = hd is null ? srcRect : new Rect2(srcRect.Position * HdScale, srcRect.Size * HdScale);
        RenderingServer.CanvasItemAddTextureRectRegion(toCanvasItem, rect, (hd ?? _legacy).GetRid(), source, modulate, transpose, clipUv);
    }
}
