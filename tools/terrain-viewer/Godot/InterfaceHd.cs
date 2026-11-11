using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arcanum.World;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-089: the HD interface art switch of the viewer. The active pack and the HD interface setting (ARC-UI-095, saved with the
/// game settings, independent of the Legacy/HD visual profile) decide whether the interface textures draw their HD redraws; <see cref="HdInterfaceTexture"/> keeps its Legacy size and Legacy
/// pixels for everything that reads them, so a window's layout, hit tests and sizes never change. Off, or without a pack entry for a frame,
/// every texture draws exactly the Legacy pixels it drew before.
/// </summary>
public static class InterfaceHd
{
    private static readonly List<WeakReference<HdInterfaceTexture>> Textures = [];
    private static int _pruneAt = 1024;
    // ARC-DEV-369: HD frames a worker resolved ahead of their first draw, per Legacy frame, until the texture takes them or a warm-up drops them.
    private static readonly ConditionalWeakTable<AreaInterfaceFrame, PreparedHd> Prepared = new();
    private static readonly Queue<WeakReference<HdInterfaceTexture>> WarmQueue = new();

    /// <summary>The result of resolving one frame ahead of time: <see cref="Image"/> is the finished HD image (premultiplied when asked, with mipmaps) or null
    /// when the pack has no usable redraw for the frame (the texture then draws Legacy without asking the pack again).</summary>
    internal sealed record PreparedHd(HdPack Pack, bool Premultiplied, Image? Image);

    /// <summary>True while a warm-up builds textures: the textures it creates that have a prepared HD frame are queued for <see cref="WarmSlice"/>.</summary>
    internal static bool CollectForWarm { get; set; }

    /// <summary>The active pack, or null (no pack found or none opened).</summary>
    public static HdPack? Pack { get; private set; }

    /// <summary>True while the HD interface setting is on.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>Raised on the main thread after <see cref="Pack"/> or <see cref="Enabled"/> changed; nodes that draw by hand redraw.</summary>
    public static event Action? Changed;

    /// <summary>Sets the pack and the profile; textures repaint only when one of them changed.</summary>
    public static bool Configure(HdPack? pack, bool enabled)
    {
        if (ReferenceEquals(pack, Pack) && enabled == Enabled) return false;
        Pack = pack; Enabled = enabled;
        HdOptimizedStore.Attach(pack, enabled);
        lock (Textures)
        {
            for (var i = Textures.Count - 1; i >= 0; i--)
                if (Textures[i].TryGetTarget(out var texture)) texture.Refresh(); else Textures.RemoveAt(i);
        }
        Changed?.Invoke();
        GD.Print($"ARC-UI-089 interface HD enabled={enabled} pack={(pack is null ? "none" : $"{pack.Id} {pack.Scale}x")}");
        return true;
    }

    /// <summary>Asks every canvas item under <paramref name="node"/> to draw again (views that draw interface textures in their own
    /// <c>_Draw</c> do not listen to a texture's change); called once after <see cref="Configure"/> changed something.</summary>
    public static void RedrawAll(Node node)
    {
        if (node is CanvasItem item) item.QueueRedraw();
        foreach (var child in node.GetChildren()) RedrawAll(child);
    }

    /// <summary>The Legacy pixels of an interface texture as an image (a scripted texture has no <c>GetImage</c> of its own).</summary>
    public static Image? LegacyImage(Texture2D texture) => texture is HdInterfaceTexture hd ? hd.LegacyImage() : texture.GetImage();

    /// <summary>A grey copy that keeps the active HD source, pack changes, alpha and Legacy layout size.</summary>
    public static Texture2D Greyed(Texture2D texture)
    {
        if (texture is not HdInterfaceTexture hd) return ImageTexture.CreateFromImage(GreyImage(texture.GetImage()));
        var grey = hd.Greyed();
        lock (Textures) Textures.Add(new(grey));
        return grey;
    }

    internal static Image GreyImage(Image image)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        for (var x = 0; x < image.GetWidth(); x++)
        {
            var c = image.GetPixel(x, y);
            var grey = c.R * .299f + c.G * .587f + c.B * .114f;
            image.SetPixel(x, y, new Color(grey, grey, grey, c.A));
        }
        return image;
    }

    /// <summary>The texture of an interface frame on an ordinary straight-alpha canvas, retaining Legacy size.</summary>
    public static Texture2D Texture(AreaInterfaceFrame frame) => Texture(frame, false);

    /// <summary>Set <paramref name="premultipliedAlpha"/> on art-font canvases prepared by <see cref="AreaArtTextDraw.Prepare"/>.</summary>
    public static Texture2D Texture(AreaInterfaceFrame frame, bool premultipliedAlpha)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var legacy = ImageTexture.CreateFromImage(Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Rgba));
        if (frame.Hd is null) return legacy;
        var texture = new HdInterfaceTexture(legacy, frame, premultipliedAlpha: premultipliedAlpha);
        lock (Textures)
        {
            if (Textures.Count >= _pruneAt)
            {
                Textures.RemoveAll(reference => !reference.TryGetTarget(out _));
                _pruneAt = Math.Max(1024, Textures.Count * 2);
            }
            Textures.Add(new(texture));
        }
        if (CollectForWarm && Enabled && Pack is { } pack && Prepared.TryGetValue(frame, out var prepared) && ReferenceEquals(prepared.Pack, pack))
            lock (WarmQueue) WarmQueue.Enqueue(new(texture));
        return texture;
    }

    /// <summary>ARC-DEV-369: resolves the HD redraws of <paramref name="frames"/> (pack file read, SHA-256 of the Legacy pixels, PNG decode, size and mask checks, then
    /// the image with premultiplication and mipmaps as <see cref="HdInterfaceTexture"/> would build it) so the first draw only uploads. Runs on any thread;
    /// a frame whose redraw is refused is remembered as refused. Does nothing without a pack, with the HD interface off, or for a frame that has no HD
    /// question. Returns the number of frames prepared (refusals included).</summary>
    internal static int Prepare(IEnumerable<(AreaInterfaceFrame Frame, bool Premultiplied)> frames, HdPack? pack, int parallelism)
    {
        if (pack is null) return 0;
        // One question per frame object: records with equal pixels and query are still separate frames that each own a texture.
        var seen = new HashSet<AreaInterfaceFrame>(ReferenceEqualityComparer.Instance);
        var work = frames.Where(entry => entry.Frame.Hd is not null && seen.Add(entry.Frame)).ToArray();
        var count = 0;
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism) }, entry =>
        {
            var (frame, premultiplied) = entry;
            if (Prepared.TryGetValue(frame, out var known) && ReferenceEquals(known.Pack, pack) && known.Premultiplied == premultiplied) return;
            Image? image = null;
            try
            {
                // ARC-DEV-376: a plain frame the optimized cache holds is already decoded with its mip chain.
                image = premultiplied ? null : HdOptimizedStore.TryBind(pack, frame.Hd!, frame.Rgba);
                if (image is null && pack.ResolveSprite(frame.Hd!, frame.Rgba) is { Status: HdFrameStatus.Hd, Rgba: { } rgba } result)
                {
                    image = Image.CreateFromData(result.Width, result.Height, false, Image.Format.Rgba8, rgba);
                    if (premultiplied) image.PremultiplyAlpha();
                    image.GenerateMipmaps();
                }
            }
            // A frame that cannot be prepared draws as it does without a warm-up: the first draw asks the pack itself.
            catch (Exception error) when (error is not OutOfMemoryException) { return; }
            Prepared.AddOrUpdate(frame, new PreparedHd(pack, premultiplied, image));
            Interlocked.Increment(ref count);
        });
        return count;
    }

    /// <summary>The prepared HD frame of <paramref name="frame"/> for <paramref name="pack"/> with this premultiplication, taken out of the table (the caller owns its
    /// image); null when none matches (a stale one is dropped).</summary>
    internal static PreparedHd? TakePrepared(AreaInterfaceFrame frame, HdPack pack, bool premultiplied)
    {
        if (!Prepared.TryGetValue(frame, out var found)) return null;
        Prepared.Remove(frame);
        if (ReferenceEquals(found.Pack, pack) && found.Premultiplied == premultiplied) return found;
        found.Image?.Dispose();
        return null;
    }

    /// <summary>Drops whatever prepared HD frames of <paramref name="frames"/> no texture took (a window opened before its warm-up got to it, or the pack changed).</summary>
    internal static void DropPrepared(IEnumerable<AreaInterfaceFrame> frames)
    {
        foreach (var frame in frames)
            if (Prepared.TryGetValue(frame, out var found)) { Prepared.Remove(frame); found.Image?.Dispose(); }
    }

    /// <summary>Creates the HD textures of queued, prepared frames until <paramref name="budgetMilliseconds"/> is spent (at least one), so the upload is spread over
    /// frames; returns how many are still queued.</summary>
    internal static int WarmSlice(double budgetMilliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            HdInterfaceTexture? texture = null;
            lock (WarmQueue)
            {
                while (WarmQueue.Count > 0 && texture is null)
                    if (!WarmQueue.Dequeue().TryGetTarget(out texture)) texture = null;
                if (texture is null) return 0;
            }
            texture.Warm();
            if (clock.Elapsed.TotalMilliseconds >= budgetMilliseconds) break;
        }
        lock (WarmQueue) return WarmQueue.Count;
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
/// and are scaled by the pack's factor; tiling uses the Legacy texture. The HD image is decoded on first draw, once per pack, and dropped
/// when the switch goes off. Its RID is empty: only <c>Draw*</c> calls (a TextureRect, <c>DrawTextureRect</c>) can use it, not a NinePatch
/// or a polygon that takes the raw RID.
/// <para>ARC-UI-112: the HD image is drawn through its own <see cref="CanvasTexture"/> with trilinear mip sampling
/// (<see cref="CanvasItem.TextureFilterEnum.LinearWithMipmaps"/>; a canvas texture's filter overrides the canvas item's), so a 4× redraw shown
/// at a zoom below 4× (200 % of 800 × 600 is 2×) is filtered down instead of sampled nearest, whatever the parent node's filter. The Legacy
/// texture, the tile case and the bitmap fonts keep the parent's Nearest. The mip chain costs about a third more memory per decoded HD frame, and
/// a magnification above 4× would now be soft rather than nearest.</para>
/// </summary>
public partial class HdInterfaceTexture : Texture2D
{
    private readonly ImageTexture _legacy = null!;
    private readonly AreaInterfaceFrame _frame = null!;
    private ImageTexture? _hd;
    private CanvasTexture? _hdCanvas;
    private HdPack? _hdFor;
    private bool _failed;
    private readonly bool _greyscale;
    private readonly bool _premultipliedAlpha;

    public HdInterfaceTexture() { }

    internal HdInterfaceTexture(ImageTexture legacy, AreaInterfaceFrame frame, bool greyscale = false, bool premultipliedAlpha = false) { _legacy = legacy; _frame = frame; _greyscale = greyscale; _premultipliedAlpha = premultipliedAlpha; }

    internal HdInterfaceTexture Greyed() => new(ImageTexture.CreateFromImage(InterfaceHd.GreyImage(_legacy.GetImage())), _frame, true, _premultipliedAlpha);

    /// <summary>True when the next draw uses the HD redraw.</summary>
    public bool IsHd => Active() is not null;

    /// <summary>ARC-DEV-369: builds the HD texture now (the warm-up spreads these over frames) instead of at the first draw.</summary>
    internal void Warm() => Active();

    internal int HdScale => InterfaceHd.Pack?.Scale ?? 1;

    /// <summary>True when the HD image is premultiplied for an art-font (PremultAlpha) canvas.</summary>
    internal bool Premultiplied => _premultipliedAlpha;

    /// <summary>Legacy pixels as a new image (the texture is a script object, so <c>Texture2D.GetImage</c> has nothing to return).</summary>
    internal Image? LegacyImage() => _legacy?.GetImage();

    // The HD image is kept only while HD draws, so a switch back to Legacy frees what the windows decoded.
    internal void Refresh()
    {
        if (!InterfaceHd.Enabled) { _hd = null; _hdCanvas = null; _hdFor = null; _failed = false; }
        EmitChanged();
    }

    // The canvas texture of the HD image (null while Legacy draws); built with the image, so the two are dropped together.
    private CanvasTexture? Active()
    {
        if (!InterfaceHd.Enabled || InterfaceHd.Pack is not { } pack || _frame?.Hd is not { } query) return null;
        if (!ReferenceEquals(_hdFor, pack)) { _hdFor = pack; _hd = null; _hdCanvas = null; _failed = false; }
        if (_hdCanvas is null && !_failed)
        {
            // ARC-DEV-369: a worker may have resolved this frame already; only the upload is left.
            if (!_greyscale && InterfaceHd.TakePrepared(_frame, pack, _premultipliedAlpha) is { } prepared)
            {
                if (prepared.Image is { } ready)
                {
                    using (ready) _hd = ImageTexture.CreateFromImage(ready);
                    _hdCanvas = new CanvasTexture { DiffuseTexture = _hd, TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
                }
                else _failed = true;
                return _hdCanvas;
            }
            // ARC-DEV-376: a plain frame comes ready-made (decoded, with its mip chain) from the optimized cache when the cache holds its PNG.
            var cached = !_greyscale && !_premultipliedAlpha ? HdOptimizedStore.TryBind(pack, query, _frame.Rgba) : null;
            if (cached is not null)
            {
                using (cached) _hd = ImageTexture.CreateFromImage(cached);
                _hdCanvas = new CanvasTexture { DiffuseTexture = _hd, TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
                return _hdCanvas;
            }
            var result = pack.ResolveSprite(query, _frame.Rgba);
            if (result.Status == HdFrameStatus.Hd && result.Rgba is { } rgba)
            {
                using var image = Image.CreateFromData(result.Width, result.Height, false, Image.Format.Rgba8, rgba);
                if (_greyscale) InterfaceHd.GreyImage(image);
                // Art-font canvases use PremultAlpha; straight RGB in transparent texels would add a square background.
                if (_premultipliedAlpha) image.PremultiplyAlpha();
                image.GenerateMipmaps();
                _hd = ImageTexture.CreateFromImage(image);
                _hdCanvas = new CanvasTexture { DiffuseTexture = _hd, TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
            }
            else _failed = true;
        }
        return _hdCanvas;
    }

    private Texture2D Source() => Active() is { } hd ? hd : _legacy;

    public override int _GetWidth() => _frame?.Width ?? 1;
    public override int _GetHeight() => _frame?.Height ?? 1;
    public override bool _HasAlpha() => true;
    public override bool _IsPixelOpaque(int x, int y) => _frame is not null && x >= 0 && y >= 0 && x < _frame.Width && y < _frame.Height && _frame.Rgba[(y * _frame.Width + x) * 4 + 3] > 0;

    public override void _Draw(Rid toCanvasItem, Vector2 pos, Color modulate, bool transpose)
        => RenderingServer.CanvasItemAddTextureRect(toCanvasItem, new Rect2(pos, new Vector2(_frame.Width, _frame.Height)), Source().GetRid(), false, modulate, transpose);

    public override void _DrawRect(Rid toCanvasItem, Rect2 rect, bool tile, Color modulate, bool transpose)
        => RenderingServer.CanvasItemAddTextureRect(toCanvasItem, rect, (tile ? _legacy : Source()).GetRid(), tile, modulate, transpose);

    public override void _DrawRectRegion(Rid toCanvasItem, Rect2 rect, Rect2 srcRect, Color modulate, bool transpose, bool clipUv)
    {
        var hd = Active();
        var source = hd is null ? srcRect : new Rect2(srcRect.Position * HdScale, srcRect.Size * HdScale);
        RenderingServer.CanvasItemAddTextureRectRegion(toCanvasItem, rect, (hd is null ? _legacy : (Texture2D)hd).GetRid(), source, modulate, transpose, clipUv);
    }
}
