using System.Diagnostics;
using Arcanum.Data.Hd;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-376: the viewer's side of the optimized interface texture cache (<see cref="HdOptimizedCache"/>, device-local under
/// <c>user://hd-cache/&lt;pack id&gt;/</c>, never inside a pack). It does two things:
/// <list type="bullet">
/// <item>Conversion: shortly after an HD pack with the HD interface setting on is attached, a background worker (two threads, below normal
/// priority) turns every interface PNG of the pack that has no entry yet into one (full mip chain made by <c>Image.GenerateMipmaps</c>, exactly the
/// texture the draw path used to build itself) and, when the pass finished, removes the entries no PNG of the pack names any more (an edited PNG
/// has another SHA-256, so it is converted again and its old entry goes). One log line: <c>ARC-DEV-376 hd cache ...</c>.</item>
/// <item>Binding: <see cref="TryBind"/> hands <see cref="HdInterfaceTexture"/> the cached chain for a frame, after the pack's own key, review,
/// size and alpha checks (<see cref="HdPack.CheckPrepared"/>); no file read, SHA-256 of the PNG, PNG decode or mip generation is left on the draw.</item>
/// </list>
/// An automated run (smoke, self-test) reads the cache but never converts, so it never writes gigabytes into the user's folder; <c>ARC_HD_CACHE=0</c>
/// turns both off. Plain frames only: the premultiplied and grey variants (bitmap-font canvases, grey spell icons) keep the old path.
/// </summary>
public static class HdOptimizedStore
{
    private static readonly object Gate = new();
    private static HdPack? _pack;
    private static HdOptimizedCache? _cache;
    private static CancellationTokenSource? _cancel;
    private static Thread? _coordinator;

    /// <summary>Set by the app: conversion runs only in a persistent (non-automated) run.</summary>
    public static bool ConversionAllowed { get; set; }

    /// <summary>The cache of the attached pack, or null.</summary>
    public static HdOptimizedCache? Cache { get { lock (Gate) return _cache; } }

    private static bool Off => System.Environment.GetEnvironmentVariable("ARC_HD_CACHE") == "0";

    /// <summary>Follows the active pack and the HD interface setting (called from <see cref="InterfaceHd.Configure"/>).</summary>
    public static void Attach(HdPack? pack, bool enabled, string? rootOverride = null)
    {
        lock (Gate)
        {
            var wanted = enabled && pack is not null && !Off ? pack : null;
            if (ReferenceEquals(wanted, _pack) && rootOverride is null) return;
            _cancel?.Cancel();
            _cancel = null; _pack = wanted; _cache = null;
            if (wanted is null) return;
            _cache = new HdOptimizedCache(rootOverride ?? System.IO.Path.Combine(OS.GetUserDataDir(), "hd-cache", wanted.Id));
            if (!ConversionAllowed) return;
            var cancel = _cancel = new CancellationTokenSource();
            var cache = _cache;
            var thread = _coordinator = new Thread(() => Convert(wanted, cache, cancel.Token, 3000)) { IsBackground = true, Name = "hd-cache-convert", Priority = System.Threading.ThreadPriority.BelowNormal };
            thread.Start();
        }
    }

    /// <summary>Stops a running conversion and waits (at most two seconds) for it, so no worker is inside an engine call while the engine shuts down.</summary>
    public static void Stop()
    {
        Thread? thread;
        lock (Gate) { _cancel?.Cancel(); thread = _coordinator; _coordinator = null; }
        thread?.Join(2000);
    }

    /// <summary>The cached texture of one frame, or null (no cache entry, or the pack's checks do not pass: the caller then resolves the PNG as before).</summary>
    internal static Image? TryBind(HdPack pack, HdSpriteQuery query, byte[] legacyRgba)
    {
        HdOptimizedCache? cache;
        lock (Gate) cache = ReferenceEquals(pack, _pack) ? _cache : null;
        if (cache is null || pack.SpriteFileSha256(query) is not { } sha) return null;
        int width = pack.Scale * query.Legacy.Width, height = pack.Scale * query.Legacy.Height;
        if (cache.TryRead(sha, width, height) is not { } chain) return null;
        if (pack.CheckPrepared(query, legacyRgba, chain.AsSpan(0, width * height * 4)).Status != HdFrameStatus.Hd) return null;
        var image = Image.CreateFromData(width, height, true, Image.Format.Rgba8, chain);
        return image is { } && image.GetWidth() == width && image.GetHeight() == height && image.HasMipmaps() ? image : null;
    }

    internal static void Convert(HdPack pack, HdOptimizedCache cache, CancellationToken token, int startDelayMs)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            if (startDelayMs > 0) token.WaitHandle.WaitOne(startDelayMs); // let the start finish first
            var scale = pack.Scale;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var todo = new List<HdPackEntry>();
            int present = 0;
            foreach (var entry in pack.InterfaceSprites)
            {
                if (!seen.Add(entry.Sha256)) continue;
                if (cache.Has(entry.Sha256, scale * entry.Legacy.Width, scale * entry.Legacy.Height)) present++; else todo.Add(entry);
            }
            int converted = 0, failed = 0, next = -1;
            // Two dedicated below-normal threads (a thread-pool worker would run at normal priority and compete with the game's own work at start).
            var workers = Enumerable.Range(0, 2).Select(index => new Thread(() =>
            {
                int at;
                while (!token.IsCancellationRequested && (at = Interlocked.Increment(ref next)) < todo.Count)
                {
                    var entry = todo[at];
                    int width = scale * entry.Legacy.Width, height = scale * entry.Legacy.Height;
                    var rgba = pack.DecodeFrame(entry, out _);
                    var ok = false;
                    if (rgba is not null && !token.IsCancellationRequested)
                    {
                        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
                        image.GenerateMipmaps();
                        var data = image.GetData();
                        ok = data.Length == HdOptimizedCache.MipBytes(width, height) && cache.Write(entry.Sha256, width, height, data);
                    }
                    if (ok) Interlocked.Increment(ref converted); else Interlocked.Increment(ref failed);
                }
            }) { IsBackground = true, Name = $"hd-cache-worker-{index}", Priority = System.Threading.ThreadPriority.BelowNormal }).ToArray();
            foreach (var worker in workers) worker.Start();
            foreach (var worker in workers) worker.Join();
            token.ThrowIfCancellationRequested();
            // `seen` holds the hash of every PNG the pack names, failed ones included, so what it does not hold is stale whatever failed.
            var removed = cache.RemoveUnreferenced(seen);
            GD.Print($"ARC-DEV-376 hd cache pack={pack.Id} entries={seen.Count} present={present} converted={converted} failed={failed} removed={removed} size={cache.SizeBytes() >> 20}MiB ms={clock.ElapsedMilliseconds}");
        }
        catch (OperationCanceledException) { GD.Print($"ARC-DEV-376 hd cache pack={pack.Id} conversion stopped ms={clock.ElapsedMilliseconds}"); }
        catch (Exception error) { GD.PrintErr($"ARC-DEV-376 hd cache conversion failed: {error.GetType().Name}"); }
    }
}
