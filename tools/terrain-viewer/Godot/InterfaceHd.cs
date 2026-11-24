using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arcanum.World;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-089: the HD interface art switch of the viewer. The active pack and the HD interface setting (ARC-UI-095, saved with the
/// game settings; since ARC-UI-2174 the same one HD value that switches the visual profile and the relit look) decide whether the interface textures draw their HD redraws; <see cref="HdInterfaceTexture"/> keeps its Legacy size and Legacy
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

    /// <summary>ARC-UI-140: true while the interface setting is "Generated (experimental)": frames with a recipe
    /// (<see cref="InterfaceChromeRecipes"/>) draw generated chrome, their pictures from the HD redraw when <see cref="Enabled"/> and the
    /// pack have one, else from the Legacy frame; frames without a recipe draw as before.</summary>
    public static bool Generated { get; private set; }

    /// <summary>Raised on the main thread after <see cref="Pack"/> or <see cref="Enabled"/> changed; nodes that draw by hand redraw.</summary>
    public static event Action? Changed;

    /// <summary>Sets the pack and the profile; textures repaint only when one of them changed.</summary>
    public static bool Configure(HdPack? pack, bool enabled, bool generated = false)
    {
        if (ReferenceEquals(pack, Pack) && enabled == Enabled && generated == Generated) return false;
        var sameCopies = ReferenceEquals(pack, Pack) && enabled == Enabled;
        Pack = pack; Enabled = enabled; Generated = generated;
        if (!sameCopies) DropDirectFrames(); // the generated setting alone does not change a direct frame's copy
        AreaLensPlace.Unified = generated; // ARC-UI-2714: the generated window arts carry the lens at the common place
        HdOptimizedStore.Attach(pack, enabled);
        RefreshTextures();
        Changed?.Invoke();
        GD.Print($"ARC-UI-089 interface HD enabled={enabled} generated={generated} pack={(pack is null ? "none" : $"{pack.Id} {pack.Scale}x")}");
        return true;
    }

    private static void RefreshTextures()
    {
        lock (Textures)
        {
            for (var i = Textures.Count - 1; i >= 0; i--)
                if (Textures[i].TryGetTarget(out var texture)) texture.Refresh(); else Textures.RemoveAt(i);
        }
    }

    // ---- ARC-UI-141: generated chrome at the window's exact device scale, painted off the main thread ----

    /// <summary>The whole device scale (device pixels per Legacy pixel) the generated chrome is painted at: the factor the bars are drawn at times
    /// the viewport's own scale (<see cref="AreaGameBarPanel"/> sets it). A texture draws 1:1 on the device grid at this scale.</summary>
    public static int GeneratedScale { get; private set; } = 1;

    /// <summary>True while the real device scale is a whole number equal to <see cref="GeneratedScale"/>: the texture then draws Nearest, one texel per
    /// device pixel; otherwise (a fractional content scale) it is sampled Linear.</summary>
    public static bool GeneratedExact { get; private set; } = true;

    /// <summary>ARC-UI-148: the scale a window's generated frames are painted at: its real device scale (the window's own scale times the viewport's) clamped to
    /// 1..<see cref="InterfaceChromeRenderer.MaxScale"/> and rounded to 1/1000, so noise in the last float digit does not paint a frame again.</summary>
    public static double PaintScale(double deviceScale) => !double.IsFinite(deviceScale) ? 1 : Math.Clamp(Math.Round(deviceScale, 3), 1, InterfaceChromeRenderer.MaxScale);

    /// <summary>Sets <see cref="GeneratedScale"/> (rounded and clamped to 1..<see cref="InterfaceChromeRenderer.MaxScale"/>); generated textures paint again for a new
    /// scale (once per scale; the finished pixels of a frame are kept for its greyed copies). Returns whether it changed.</summary>
    public static bool SetGeneratedScale(float deviceScale)
    {
        var scale = Math.Clamp((int)MathF.Round(deviceScale), 1, InterfaceChromeRenderer.MaxScale);
        var exact = MathF.Abs(deviceScale - scale) < 0.01f;
        if (scale == GeneratedScale && exact == GeneratedExact) return false;
        GeneratedScale = scale; GeneratedExact = exact;
        if (Generated) { RefreshTextures(); Changed?.Invoke(); }
        GD.Print($"ARC-UI-141 generated interface scale={scale}");
        return true;
    }

    /// <summary>ARC-UI-2452: the fractional device scale the strips of the Steamwoven layout are drawn at (null: the bars' whole <see cref="GeneratedScale"/>). Strip textures that claim no
    /// scale source paint at this scale (<see cref="PaintScale"/>) and are sampled Linear unless the scale is whole and the sizes come out in whole device pixels.</summary>
    public static double? FractionalScale { get; private set; }

    /// <summary>ARC-UI-2452: sets or clears <see cref="FractionalScale"/>; generated textures paint again for a new scale. Returns whether it changed.</summary>
    public static bool SetFractionalScale(double? deviceScale)
    {
        var next = deviceScale is { } value && double.IsFinite(value) && value > 0 ? Math.Round(value, 3) : (double?)null;
        if (FractionalScale == next) return false;
        FractionalScale = next;
        if (Generated) { RefreshTextures(); Changed?.Invoke(); }
        GD.Print($"ARC-UI-2452 steamwoven strip scale={(next is { } shown ? shown.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "off")}");
        return true;
    }

    /// <summary>One painted frame: straight RGBA at (Legacy size × <see cref="Scale"/>), kept per Legacy frame so a greyed copy and a rebuilt texture
    /// reuse it. <see cref="Pixels"/> is null while it is painted (<see cref="Failed"/> when painting threw).</summary>
    internal sealed class GeneratedFrameEntry(HdPack? pack, bool hdOn, double scale)
    {
        public HdPack? Pack { get; } = pack;
        public bool HdOn { get; } = hdOn;
        /// <summary>Device pixels per Legacy pixel the frame is painted at (ARC-UI-148: a window's own scale may be fractional; the bars' is whole).</summary>
        public double Scale { get; } = scale;
        public byte[]? Pixels { get; set; }
        public bool Failed { get; set; }
    }

    private static readonly ConditionalWeakTable<AreaInterfaceFrame, GeneratedFrameEntry> GeneratedFrames = new();
    // ARC-RENDER-3002: direct frames wait on their own gate (two workers), so a grid of icons at a new scale never queues ahead of a window's generated chrome.
    private static readonly SemaphoreSlim DirectGate = new(Math.Max(1, Math.Min(2, System.Environment.ProcessorCount / 4)));
    private static readonly SemaphoreSlim GeneratedGate = new(Math.Max(1, Math.Min(4, System.Environment.ProcessorCount / 2)));
    private static int _generatedPending;

    /// <summary>Frames still being painted on a worker (the self-test waits for 0, then two frames for the deferred swap).</summary>
    public static int GeneratedPending => Volatile.Read(ref _generatedPending);

    /// <summary>The entry of the frame for the current pack, HD setting and <see cref="GeneratedScale"/>: its <c>Pixels</c> are set when ready,
    /// <c>Failed</c> when painting threw, neither while a worker paints it (this call starts the worker the first time). Main thread.</summary>
    internal static GeneratedFrameEntry GeneratedFor(AreaInterfaceFrame frame, HdSpriteQuery query, ChromeRecipe recipe, double scale)
    {
        var (pack, hdOn) = (Pack, Enabled);
        lock (GeneratedFrames)
        {
            if (GeneratedFrames.TryGetValue(frame, out var known) && ReferenceEquals(known.Pack, pack) && known.HdOn == hdOn && known.Scale == scale)
                return known;
            var entry = new GeneratedFrameEntry(pack, hdOn, scale);
            GeneratedFrames.AddOrUpdate(frame, entry);
            Interlocked.Increment(ref _generatedPending);
            Task.Run(() => PaintGeneratedAsync(entry, frame, query, recipe));
            return entry;
        }
    }

    private static async Task PaintGeneratedAsync(GeneratedFrameEntry entry, AreaInterfaceFrame frame, HdSpriteQuery query, ChromeRecipe recipe)
    {
        // The gate is awaited, so queued frames hold no pool thread; a frame whose entry was replaced meanwhile (another pack, HD setting
        // or scale) is not painted at all.
        await GeneratedGate.WaitAsync();
        var clock = Stopwatch.StartNew();
        try
        {
            lock (GeneratedFrames)
                if (!GeneratedFrames.TryGetValue(frame, out var current) || !ReferenceEquals(current, entry)) { entry.Failed = true; return; }
            var scale = entry.Scale;
            var (pixelWidth, pixelHeight) = InterfaceChromeRenderer.PixelSize(frame.Width, frame.Height, scale);
            byte[] picture = [];
            // The pictures the chrome keeps (vials, clock, cogs) come from the pack's redraw while the HD interface is on, averaged to the render scale.
            if (entry.HdOn && entry.Pack is { } pack
                && pack.ResolveSprite(query, frame.Rgba) is { Status: HdFrameStatus.Hd, Rgba: { } rgba } hd
                && hd.Width == frame.Width * pack.Scale && hd.Height == frame.Height * pack.Scale)
                picture = recipe.WholePicture
                    ? InterfaceChromeRenderer.ResampleStraight(rgba, hd.Width, hd.Height, pixelWidth, pixelHeight)
                    : InterfaceChromeRenderer.ResamplePicture(rgba, hd.Width, hd.Height, pixelWidth, pixelHeight);
            entry.Pixels = InterfaceChromeRenderer.Render(recipe, frame.Width, frame.Height, frame.Rgba, picture, query.Key.Frame, scale);
            // The big strips only: the log line says how long a worker painted (the main thread did not wait for it).
            if ((long)pixelWidth * pixelHeight > 200_000)
                GD.Print($"ARC-UI-141 generated frame {recipe.File} {pixelWidth}x{pixelHeight} painted off the main thread in {clock.ElapsedMilliseconds} ms (scale {scale}, picture {(picture.Length > 0 ? "pack" : "legacy")})");
        }
        catch (Exception e)
        {
            // Any failure (an out-of-memory at a big scale included) draws the frame as HD or Legacy from now on, logged once.
            entry.Failed = true;
            GD.PrintErr($"ARC-UI-140 generated interface frame {recipe.File} {query.Key.Frame} failed: {e.GetType().Name}");
        }
        finally
        {
            GeneratedGate.Release();
            Interlocked.Decrement(ref _generatedPending);
            Callable.From(() => GeneratedPainted(frame)).CallDeferred();
        }
    }

    // Main thread: the textures of the painted frame (and its greyed copies) take it at their next draw.
    private static void GeneratedPainted(AreaInterfaceFrame frame)
    {
        // Index loop: a texture's changed handler may add textures (as RefreshTextures).
        lock (Textures)
            for (var i = 0; i < Textures.Count; i++)
                if (Textures[i].TryGetTarget(out var texture) && texture.IsFrame(frame)) texture.Refresh();
    }

    // ---- ARC-RENDER-3002: direct pack frames at a fractional device scale, area-resampled once per scale on a worker ----

    /// <summary>One direct frame resampled to a device scale: straight RGBA of <see cref="Width"/> × <see cref="Height"/> (the Legacy size times the scale, rounded per edge).
    /// <see cref="Pixels"/> is null while a worker resamples it (<see cref="Failed"/> when the redraw is not usable or the work threw: the frame then draws as before).</summary>
    internal sealed class DirectFrameEntry(HdPack pack, double scale)
    {
        private byte[]? _pixels;
        public HdPack Pack { get; } = pack;
        /// <summary>Device pixels per Legacy pixel the frame is resampled to (rounded to 1/1000).</summary>
        public double Scale { get; } = scale;
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[]? Pixels { get => Volatile.Read(ref _pixels); set => Volatile.Write(ref _pixels, value); }
        public volatile bool Failed;
        /// <summary>ARC-RENDER-3045: set (before <see cref="Failed"/>) when the pack has no usable redraw for the frame: no scale is worth another worker.</summary>
        public volatile bool Unusable;
    }

    // Up to DirectKeep scales per frame, least recently asked for first out (a frame shared by views at up to four different scales does not resample again on every draw).
    private const int DirectKeep = 4;
    private static readonly ConditionalWeakTable<AreaInterfaceFrame, List<DirectFrameEntry>> DirectFrames = new();
    private static int _directPending;
    private static long _directTicks;

    /// <summary>Direct frames still being resampled on a worker (a self-test waits for 0).</summary>
    public static int DirectPending => Volatile.Read(ref _directPending);

    /// <summary>Totals of the resampled direct frames kept now (how many, their bytes as straight RGBA) and the worker time spent since the pack or the HD setting last changed.</summary>
    public static (long Frames, long Bytes, double Milliseconds) DirectTotals
    {
        get
        {
            long frames = 0, bytes = 0;
            lock (DirectFrames)
                foreach (var pair in DirectFrames)
                    foreach (var entry in pair.Value)
                        if (entry.Pixels is { } pixels) { frames++; bytes += pixels.Length; }
            return (frames, bytes, Interlocked.Read(ref _directTicks) * 1000.0 / Stopwatch.Frequency);
        }
    }

    /// <summary>The scale (rounded to 1/1000) a direct frame of a pack of <paramref name="packScale"/> is resampled to for the real device scale <paramref name="device"/>,
    /// or null when it draws as before: a scale at or above the pack's, and a whole scale that divides the pack's (1 and 2 for a 4× pack: an exact mip level).
    /// ARC-RENDER-3045: a whole scale that does not divide it (3 for a 4× pack, which the trilinear draw over-blurs) is resampled to exactly that whole scale (within 0.01).</summary>
    internal static double? DirectScale(double device, int packScale)
    {
        if (!double.IsFinite(device) || device <= 0 || packScale < 2) return null;
        var scale = Math.Round(device, 3);
        if (scale <= 0 || scale >= packScale - 0.001) return null;
        var whole = Math.Round(scale);
        if (Math.Abs(scale - whole) >= 0.01) return scale;
        return whole >= 1 && packScale % (int)whole != 0 ? whole : null;
    }

    /// <summary>The entry of <paramref name="frame"/> for <paramref name="pack"/> at <paramref name="scale"/> (<see cref="DirectScale"/>): <c>Pixels</c> are set when ready, <c>Failed</c>
    /// when the pack has no usable redraw, neither while a worker resamples it (this call starts the worker the first time). Main thread.</summary>
    internal static DirectFrameEntry DirectFor(AreaInterfaceFrame frame, HdSpriteQuery query, HdPack pack, double scale)
    {
        lock (DirectFrames)
        {
            var entries = DirectFrames.GetOrCreateValue(frame);
            entries.RemoveAll(e => !ReferenceEquals(e.Pack, pack));
            // ARC-RENDER-3045: a pack with no usable redraw for the frame has none at any scale; the failed entry answers every scale (no worker per new scale).
            var unusable = entries.Find(e => e.Unusable);
            if (unusable is not null) return unusable;
            var known = entries.Find(e => e.Scale == scale);
            if (known is not null)
            {
                // Least recently used: the scale asked for last is the last to go.
                if (entries[^1] != known) { entries.Remove(known); entries.Add(known); }
                return known;
            }
            if (entries.Count >= DirectKeep) entries.RemoveAt(0);
            var entry = new DirectFrameEntry(pack, scale);
            entries.Add(entry);
            Interlocked.Increment(ref _directPending);
            Task.Run(() => ResampleDirectAsync(entries, entry, frame, query));
            return entry;
        }
    }

    private static async Task ResampleDirectAsync(List<DirectFrameEntry> entries, DirectFrameEntry entry, AreaInterfaceFrame frame, HdSpriteQuery query)
    {
        // Queued frames hold no pool thread; one whose entry went meanwhile (another scale, pack or setting) is not resampled at all.
        await DirectGate.WaitAsync();
        try
        {
            lock (DirectFrames) if (!entries.Contains(entry)) { entry.Failed = true; return; }
            var clock = Stopwatch.StartNew();
            var pack = entry.Pack;
            if (pack.ResolveSprite(query, frame.Rgba) is not { Status: HdFrameStatus.Hd, Rgba: { } rgba } hd
                || hd.Width != frame.Width * pack.Scale || hd.Height != frame.Height * pack.Scale) { entry.Unusable = true; entry.Failed = true; return; }
            var (width, height) = InterfaceChromeRenderer.PixelSize(frame.Width, frame.Height, entry.Scale);
            var pixels = InterfaceChromeRenderer.ResampleStraight(rgba, hd.Width, hd.Height, width, height);
            lock (DirectFrames) if (!entries.Contains(entry)) { entry.Failed = true; return; }
            entry.Width = width; entry.Height = height; entry.Pixels = pixels;
            Interlocked.Add(ref _directTicks, clock.ElapsedTicks);
            if ((long)width * height > 200_000)
                GD.Print($"ARC-RENDER-3002 direct frame {hd.Width}x{hd.Height} -> {width}x{height} resampled off the main thread in {clock.ElapsedMilliseconds} ms (scale {entry.Scale})");
        }
        catch (Exception e)
        {
            // Any failure (an out-of-memory at a big scale included) draws the frame with the mip chain as before. A pack that rejects the frame (a Legacy hash mismatch)
            // rejects it at every scale: no worker per scale (ARC-RENDER-3045).
            if (e is ArgumentException) entry.Unusable = true;
            entry.Failed = true;
            GD.PrintErr($"ARC-RENDER-3002 direct interface frame resample failed: {e.GetType().Name}");
        }
        finally
        {
            DirectGate.Release();
            Interlocked.Decrement(ref _directPending);
            Callable.From(() => GeneratedPainted(frame)).CallDeferred();
        }
    }

    // A new pack or HD setting forgets every resampled frame (a worker still running notices and drops its result).
    private static void DropDirectFrames()
    {
        lock (DirectFrames)
        {
            foreach (var pair in DirectFrames) pair.Value.Clear();
            Interlocked.Exchange(ref _directTicks, 0);
        }
    }

    /// <summary>Straight-alpha RGBA with each colour turned to its luma (the weights of <see cref="GreyImage"/>), a copy.</summary>
    internal static byte[] GreyBytes(byte[] rgba)
    {
        var grey = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var luma = (byte)Math.Clamp((int)MathF.Round(rgba[i] * .299f + rgba[i + 1] * .587f + rgba[i + 2] * .114f), 0, 255);
            grey[i] = grey[i + 1] = grey[i + 2] = luma; grey[i + 3] = rgba[i + 3];
        }
        return grey;
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
        lock (Textures) return Textures.Count(t => t.TryGetTarget(out var texture) && texture.IsHd && !texture.IsGenerated);
    }

    /// <summary>ARC-UI-141: frames that currently draw generated chrome (<see cref="ActiveCount"/> leaves them out).</summary>
    public static int GeneratedCount()
    {
        lock (Textures) return Textures.Count(t => t.TryGetTarget(out var texture) && texture.IsGenerated);
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
/// <para>ARC-RENDER-3002: at a fractional device scale below the pack's (a view's claimed scale source) a plain frame is resampled once per
/// scale with the area filter on a worker (<see cref="InterfaceHd.DirectFor"/>) and drawn 1:1 from that copy; the mip chain it replaces is freed. Whole scales, scales
/// from the pack's up, art-font canvases and textures with no claimed scale keep the trilinear draw.</para>
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
    // ARC-UI-140/141: the generated frame (uploaded from the painted pixels of InterfaceHd.GeneratedFor) and the scale of what draws.
    private ImageTexture? _generated;
    private CanvasTexture? _generatedCanvas;
    private InterfaceHd.GeneratedFrameEntry? _generatedFrom;
    // ARC-RENDER-3002: the pack frame area-resampled to the device scale (straight RGBA, drawn 1:1) instead of the mip chain.
    private ImageTexture? _area;
    private CanvasTexture? _areaCanvas;
    private InterfaceHd.DirectFrameEntry? _areaFrom;
    private int _activeScale = 1;
    // ARC-UI-148: device pixels per Legacy pixel of what draws, for a region draw (a generated frame at a fractional scale is not a whole multiple).
    private float _activeRatio = 1;
    /// <summary>ARC-UI-148: the device scale of the window this texture is drawn in (the window view that draws it claims it); null: the bars' whole scale.</summary>
    internal Func<float>? ScaleSource { get; set; }

    public HdInterfaceTexture() { }

    internal HdInterfaceTexture(ImageTexture legacy, AreaInterfaceFrame frame, bool greyscale = false, bool premultipliedAlpha = false) { _legacy = legacy; _frame = frame; _greyscale = greyscale; _premultipliedAlpha = premultipliedAlpha; }

    internal HdInterfaceTexture Greyed() => new(ImageTexture.CreateFromImage(InterfaceHd.GreyImage(_legacy.GetImage())), _frame, true, _premultipliedAlpha) { ScaleSource = ScaleSource };

    /// <summary>True when the next draw uses the HD redraw.</summary>
    public bool IsHd => Active() is not null;

    /// <summary>ARC-UI-140: true when what draws is the generated chrome of a recipe (a self-test reads it).</summary>
    internal bool IsGenerated => Active() is { } active && ReferenceEquals(active, _generatedCanvas);

    /// <summary>ARC-RENDER-3002: true when what draws is the pack frame area-resampled to the device scale (a self-test reads it).</summary>
    internal bool IsAreaResampled => Active() is { } active && ReferenceEquals(active, _areaCanvas);

    /// <summary>ARC-RENDER-3002: the pixel size of the uploaded area-resampled copy (zero while none is uploaded; a self-test reads it).</summary>
    internal Vector2I AreaPixelSize => _area is { } image && _areaCanvas is not null ? new(image.GetWidth(), image.GetHeight()) : default;

    /// <summary>ARC-RENDER-3002: the uploaded area-resampled copy as straight RGBA bytes (null while none is uploaded; a self-test compares it).</summary>
    internal byte[]? AreaImageBytes() => _area?.GetImage()?.GetData();

    /// <summary>ARC-UI-141: true when this texture (or a greyed copy of it) is made from <paramref name="frame"/>.</summary>
    internal bool IsFrame(AreaInterfaceFrame frame) => ReferenceEquals(_frame, frame);

    /// <summary>ARC-UI-141: the pixel size of the uploaded generated frame (zero while none is uploaded; a self-test reads it).</summary>
    internal Vector2I GeneratedPixelSize => _generated is { } image && _generatedCanvas is not null ? new(image.GetWidth(), image.GetHeight()) : default;

    /// <summary>ARC-DEV-369: builds the HD texture now (the warm-up spreads these over frames) instead of at the first draw.</summary>
    internal void Warm() => Active();

    internal int HdScale => _activeScale;

    /// <summary>ARC-UI-2452: the Legacy frame behind the texture (its RGBA and art identity), null before it is bound.</summary>
    internal AreaInterfaceFrame? FrameForBoard => _frame;

    /// <summary>True when the HD image is premultiplied for an art-font (PremultAlpha) canvas.</summary>
    internal bool Premultiplied => _premultipliedAlpha;

    /// <summary>Legacy pixels as a new image (the texture is a script object, so <c>Texture2D.GetImage</c> has nothing to return).</summary>
    internal Image? LegacyImage() => _legacy?.GetImage();

    // The HD image is kept only while HD draws, so a switch back to Legacy frees what the windows decoded.
    internal void Refresh()
    {
        if (!InterfaceHd.Enabled) { _hd = null; _hdCanvas = null; _hdFor = null; _failed = false; DropArea(); }
        // The generated frame is looked up again at the next draw (its entry says whether pack, HD setting or scale changed); the uploaded image of
        // the old entry keeps drawing while the new one is painted, and goes when the generated interface is off.
        if (!InterfaceHd.Generated) { _generated = null; _generatedCanvas = null; _generatedFrom = null; }
        EmitChanged();
    }

    // The canvas texture of the HD image (null while Legacy draws); built with the image, so the two are dropped together.
    private CanvasTexture? Active()
    {
        if (InterfaceHd.Generated && _frame?.Hd is { } generatedQuery && InterfaceChromeRecipes.For(generatedQuery) is { } recipe)
        {
            if (GeneratedCanvas(recipe, generatedQuery, out var painting) is { } generated)
            {
                _activeRatio = (float)_generated!.GetWidth() / Math.Max(1, _frame.Width);
                _activeScale = Math.Max(1, (int)MathF.Round(_activeRatio));
                return generated;
            }
            // ARC-UI-141: while a worker paints the frame the Legacy pixels draw (no HD decode on the main thread, no stall); the paint swaps in by itself.
            if (painting) { _activeScale = 1; _activeRatio = 1; return null; }
        }
        _activeScale = InterfaceHd.Pack?.Scale ?? 1;
        _activeRatio = _activeScale;
        if (!InterfaceHd.Enabled || InterfaceHd.Pack is not { } pack || _frame?.Hd is not { } query) return null;
        if (!ReferenceEquals(_hdFor, pack)) { _hdFor = pack; _hd = null; _hdCanvas = null; _failed = false; DropArea(); }
        // ARC-RENDER-3002: a plain frame at a fractional device scale below the pack's draws from its area-resampled copy once a worker has it (the mip chain is freed then).
        if (!_premultipliedAlpha && AreaCanvas(pack, query) is { } area) return area;
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

    private void DropArea() { _area = null; _areaCanvas = null; _areaFrom = null; }

    // ARC-RENDER-3002: the area-resampled frame for the device scale this texture is drawn at (its scale source); null while the scale is whole
    // or at least the pack's, the pack has no usable redraw, or nothing is ready yet and no earlier scale draws (the mip chain draws then, as before).
    private CanvasTexture? AreaCanvas(HdPack pack, HdSpriteQuery query)
    {
        // Only a view that claims its device scale: a texture with no claim may be drawn at any scale, and a copy for the wrong one is blurrier than the mip chain.
        if (ScaleSource is not { } source || InterfaceHd.DirectScale(source(), pack.Scale) is not { } scale) { DropArea(); return null; }
        var real = (double)source();
        // Settled: the copy of this pack and scale is uploaded, so the draw takes no lock and no lookup.
        if (_areaCanvas is not null && _areaFrom is { } held && ReferenceEquals(held.Pack, pack) && held.Scale == scale && !held.Failed)
        {
            _activeRatio = (float)(_area?.GetWidth() ?? 1) / Math.Max(1, _frame.Width);
            _activeScale = Math.Max(1, (int)MathF.Round(_activeRatio));
            return _areaCanvas;
        }
        var entry = InterfaceHd.DirectFor(_frame, query, pack, scale);
        if (entry.Failed) { DropArea(); return null; }
        if (entry.Pixels is not { } pixels)
        {
            // Still resampling: an earlier scale's copy keeps drawing, stretched into the same Legacy rectangle, until the new one is ready.
            if (_areaCanvas is null) return null;
        }
        else if (!ReferenceEquals(_areaFrom, entry) || _areaCanvas is null)
        {
            using var image = Image.CreateFromData(entry.Width, entry.Height, false, Image.Format.Rgba8, _greyscale ? InterfaceHd.GreyBytes(pixels) : pixels);
            _area = ImageTexture.CreateFromImage(image);
            _areaFrom = entry;
            // One texel per device pixel (Nearest) only when the copy is exactly what the view shows; otherwise Linear on an almost 1:1 sample.
            var exact = Math.Abs(real - scale) < 0.001 && Math.Abs(_frame.Width * scale - entry.Width) < 0.001 && Math.Abs(_frame.Height * scale - entry.Height) < 0.001;
            _areaCanvas = new CanvasTexture { DiffuseTexture = _area, TextureFilter = exact ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear };
            _hd = null; _hdCanvas = null; _failed = false;
        }
        _activeRatio = (float)(_area?.GetWidth() ?? 1) / Math.Max(1, _frame.Width);
        _activeScale = Math.Max(1, (int)MathF.Round(_activeRatio));
        return _areaCanvas;
    }

    // ARC-UI-141: the generated frame at the exact device scale. The pixels are painted once per frame on a worker (InterfaceHd.GeneratedFor);
    // this texture only greys them when it is a greyed copy, premultiplies for art fonts and uploads, drawn 1:1 (Nearest, no mip chain).
    private CanvasTexture? GeneratedCanvas(ChromeRecipe recipe, HdSpriteQuery query, out bool painting)
    {
        painting = false;
        // ARC-UI-2452: a fractional strip scale paints like a window's own scale (no scale source: the strips').
        var bySource = ScaleSource is not null || InterfaceHd.FractionalScale is not null;
        var device = ScaleSource is { } source ? source() : InterfaceHd.FractionalScale ?? (double)InterfaceHd.GeneratedScale;
        var scale = !bySource ? InterfaceHd.GeneratedScale : InterfaceHd.PaintScale(device);
        var entry = InterfaceHd.GeneratedFor(_frame, query, recipe, scale);
        if (entry.Failed) return null; // drawn as HD or Legacy
        if (entry.Pixels is not { } pixels)
        {
            // Still painting. A previous generated image (an earlier scale or setting) keeps drawing, stretched into the same Legacy rectangle,
            // until the new one is ready; with none, the Legacy pixels draw.
            painting = _generatedCanvas is null;
            return _generatedCanvas;
        }
        if (ReferenceEquals(_generatedFrom, entry) && _generatedCanvas is not null) return _generatedCanvas;
        var (pixelWidth, pixelHeight) = InterfaceChromeRenderer.PixelSize(_frame.Width, _frame.Height, scale);
        using var image = Image.CreateFromData(pixelWidth, pixelHeight, false, Image.Format.Rgba8, _greyscale ? InterfaceHd.GreyBytes(pixels) : pixels);
        if (_premultipliedAlpha) image.PremultiplyAlpha();
        _generated = ImageTexture.CreateFromImage(image);
        _generatedFrom = entry;
        // One texel per device pixel (Nearest) only when the painted size is exactly what the window shows: the bars' whole scale, or a window whose
        // device scale is the painted one and whose frame sizes come out in whole device pixels.
        var exact = !bySource ? InterfaceHd.GeneratedExact
            : Math.Abs(device - scale) < 0.001 && Math.Abs(_frame.Width * scale - pixelWidth) < 0.001 && Math.Abs(_frame.Height * scale - pixelHeight) < 0.001;
        _generatedCanvas = new CanvasTexture { DiffuseTexture = _generated, TextureFilter = exact ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear };
        return _generatedCanvas;
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
        var source = hd is null ? srcRect : new Rect2(srcRect.Position * _activeRatio, srcRect.Size * _activeRatio);
        RenderingServer.CanvasItemAddTextureRectRegion(toCanvasItem, rect, (hd is null ? _legacy : (Texture2D)hd).GetRid(), source, modulate, transpose, clipUv);
    }
}
