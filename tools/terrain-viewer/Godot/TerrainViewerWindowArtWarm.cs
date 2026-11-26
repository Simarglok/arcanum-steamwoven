using System.Diagnostics;
using Arcanum.Data.Hd;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-369 (perf diagnosis O2): the art of the inventory, the character sheet and the logbook is made ready in the background after the map is
// published, so their first opening is a warm one. The performance diagnosis ARC-RES-102 (deleted) measured 0.25-2.2 s for that first opening: the
// window's art files were read and decoded on the main thread (about half), and each HD frame was resolved (pack file read, SHA-256 of the Legacy
// pixels, PNG decode, mask check) and given mipmaps inside the first draw (the other half).
//
// Per window, one step at a time (so the HD images of one window are held, never all three): a worker thread reads the window's art
// (the same loader the window used, shared through a Lazy so a window opened meanwhile joins it instead of reading twice) and, when the HD interface is on,
// resolves every HD frame the window will draw into a finished image (InterfaceHd.Prepare); the main thread then builds the window's textures the way
// the opening would, and creates the prepared HD textures a few milliseconds a frame (InterfaceHd.WarmSlice). The result is what the opening builds,
// made earlier: the same art, the same pixels, the same HD rule (ResolveSprite, premultiplication for art-font canvases, mipmaps). A window the player opens
// first reads on demand exactly as before. ARC_WINDOW_ART_WARMUP=0 turns the warm-up off (for measuring the cold opening).
public partial class TerrainViewerApp
{
    private const int WarmStartFrames = 30;
    private const double WarmSliceMilliseconds = 3.0;
    private static readonly bool WindowArtWarmDisabled = System.Environment.GetEnvironmentVariable("ARC_WINDOW_ART_WARMUP") == "0";
    private static readonly string[] WarmStepNames = ["inventory", "sheet", "logbook"];

    /// <summary>The windows' art content of one module, each read once by whoever asks first (the warm-up's worker or the window's opening).</summary>
    private sealed record WindowArtSlots(string Module, Lazy<AreaInventoryArtContent> Inventory, Lazy<AreaCharacterSheetArtContent> Sheet, Lazy<AreaLogbookArtContent> Logbook);

    /// <summary>What a worker step did: the frames it prepared HD images for (to drop what no texture took) and its timings.</summary>
    private sealed record WarmWorkerResult(IReadOnlyList<AreaInterfaceFrame> Frames, double ContentMilliseconds, double HdMilliseconds, int HdPrepared);

    private WindowArtSlots? _windowArtSlots;
    private int _warmStep;
    private int _warmDelay;
    private Task<WarmWorkerResult>? _warmTask;
    private (HdPack? Pack, bool Enabled) _warmKey;
    private bool _warmBuilt;
    private double _warmBuildMilliseconds;
    private int _warmSlices;
    private double _warmSliceMilliseconds, _warmSliceWorstMilliseconds;

    /// <summary>The module's art content of a window when the warm-up holds it (waiting for its worker when that is reading it right now); null when it does not,
    /// so the caller reads it as it always did.</summary>
    private T? WarmedWindowArt<T>(string module, Func<WindowArtSlots, Lazy<T>> pick) where T : class
    {
        if (_windowArtSlots is not { } slots || slots.Module != module) return null;
        try { return pick(slots).Value; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            GD.Print($"ARC-DEV-369 window art content {typeof(T).Name} not taken from the warm-up: {error.GetType().Name}");
            return null;
        }
    }

    private bool WarmLookBuilt(int step, string module) => step switch
    {
        0 => _inventoryArtContent is not null && _inventoryArtModule == module,
        1 => _sheetArtContent is not null && _sheetArtModule == module,
        _ => _logbookArtContent is not null && _logbookArtModule == module,
    };

    /// <summary>Per frame, after the interface HD switch is synced: starts, builds and uploads the next window's art; never throws.</summary>
    private void TickWindowArtWarm()
    {
        if (WindowArtWarmDisabled || SelfTestBootstrap || _arguments?.ModulePath is not { } module || _readyArea is null || _inventoryArtLookForSelfTest is not null
            || _logbookArtForSelfTest is not null) return;
        try
        {
            if (_windowArtSlots?.Module != module) StartWindowArtWarm(module);
            if (_warmDelay > 0) { _warmDelay--; return; }
            if (_warmStep >= WarmStepNames.Length) { TickPortraitWarmSafely(module); TickInventoryOpenWarm(); return; }
            if (_warmTask is null)
            {
                // A window the player opened first has its art already; nothing to warm.
                if (WarmLookBuilt(_warmStep, module)) { _warmStep++; return; }
                var (pack, enabled) = (InterfaceHd.Pack, InterfaceHd.Enabled);
                _warmKey = (pack, enabled);
                (_warmBuilt, _warmBuildMilliseconds, _warmSlices, _warmSliceMilliseconds, _warmSliceWorstMilliseconds) = (false, 0, 0, 0, 0);
                var (slots, step) = (_windowArtSlots!, _warmStep);
                _warmTask = Task.Run(() => WarmWorker(slots, step, enabled ? pack : null));
                return;
            }
            if (!_warmTask.IsCompleted) return;
            var result = _warmTask.IsCompletedSuccessfully ? _warmTask.Result : new WarmWorkerResult([], 0, 0, 0);
            if (!_warmTask.IsCompletedSuccessfully)
                GD.Print($"ARC-DEV-369 window art warm {WarmStepNames[_warmStep]} worker failed: {_warmTask.Exception?.GetBaseException().GetType().Name ?? "cancelled"}");
            if (WarmLookBuilt(_warmStep, module) && !_warmBuilt)
            {
                // The player opened it while the worker was still at it: it joined the worker's read; what was prepared for HD is dropped.
                InterfaceHd.DropPrepared(result.Frames);
                FinishWarmStep(result, joined: true);
                return;
            }
            if (_warmKey != (InterfaceHd.Pack, InterfaceHd.Enabled) && !_warmBuilt)
            {
                // The pack or the HD switch changed under the worker: its images belong to the old pack; the content stays, the HD part runs again.
                InterfaceHd.DropPrepared(result.Frames);
                _warmTask = null;
                return;
            }
            if (!_warmBuilt)
            {
                var started = Stopwatch.GetTimestamp();
                InterfaceHd.CollectForWarm = true;
                try { BuildWarmLook(_warmStep); }
                finally { InterfaceHd.CollectForWarm = false; }
                _warmBuildMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _warmBuilt = true;
                return;
            }
            var slice = Stopwatch.GetTimestamp();
            var remaining = InterfaceHd.WarmSlice(WarmSliceMilliseconds);
            var spent = Stopwatch.GetElapsedTime(slice).TotalMilliseconds;
            _warmSlices++; _warmSliceMilliseconds += spent; _warmSliceWorstMilliseconds = Math.Max(_warmSliceWorstMilliseconds, spent);
            if (remaining > 0) return;
            InterfaceHd.DropPrepared(result.Frames);
            FinishWarmStep(result, joined: false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The warm-up is an optimization: whatever it could not do the window does on demand.
            GD.Print($"ARC-DEV-369 window art warm stopped at {(_warmStep < WarmStepNames.Length ? WarmStepNames[_warmStep] : "end")}: {error.GetType().Name} {error.Message}");
            InterfaceHd.CollectForWarm = false;
            if (_warmTask is { } running) _ = running.ContinueWith(task => { if (task.IsCompletedSuccessfully) InterfaceHd.DropPrepared(task.Result.Frames); }, TaskScheduler.Default);
            _warmTask = null; _warmStep = WarmStepNames.Length;
        }
    }

    // The hero's portrait, which the sheet draws in its slot: read and adopted as LoadPortraits would, but on a worker, once the hero is known (a new game
    // starts its session after the map is published; a changed hero is warmed again).
    private Task<AreaPortraitContent>? _warmPortraitTask;
    private (string Module, int Id, AreaPortraitContent? Known) _warmPortraitKey;
    private int _warmPortraitDoneId;

    private void TickPortraitWarmSafely(string module)
    {
        try { TickPortraitWarm(module); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Not retried: the sheet reads the portrait on demand as before.
            GD.Print($"ARC-DEV-369 portrait warm stopped: {error.GetType().Name} {error.Message}");
            _warmPortraitTask = null; _warmPortraitDoneId = int.MaxValue;
        }
    }

    private void TickPortraitWarm(string module)
    {
        if (_warmPortraitTask is { } task)
        {
            if (!task.IsCompleted) return;
            _warmPortraitTask = null;
            if (task.IsCompletedSuccessfully) AdoptWarmedPortrait(module, task.Result);
            else { _warmPortraitDoneId = _warmPortraitKey.Id; GD.Print($"ARC-DEV-369 portrait warm failed: {task.Exception?.GetBaseException().GetType().Name ?? "cancelled"}"); }
            return;
        }
        if (_areaSession?.QueryHero() is not { Portrait: > 0 } hero || hero.Portrait == _warmPortraitDoneId) return;
        var known = _portraitContent is not null && _portraitModule == module ? _portraitContent : null;
        if (known?.Portraits.ContainsKey(hero.Portrait) == true) { _warmPortraitDoneId = hero.Portrait; return; }
        var id = hero.Portrait;
        _warmPortraitKey = (module, id, known);
        _warmPortraitTask = Task.Run(() => AreaPortraitContent.Load(module, [id], known));
    }

    private void AdoptWarmedPortrait(string module, AreaPortraitContent warmed)
    {
        var (keyModule, id, known) = _warmPortraitKey;
        if (module != keyModule) return;
        var current = _portraitContent is not null && _portraitModule == module ? _portraitContent : null;
        // The portraits changed on the main thread meanwhile: this read is not built on them; the next frame asks again.
        if (!ReferenceEquals(current, known)) return;
        if (current is null)
        {
            _portraitModule = module; _portraitTextures.Clear();
            // The follower frames' panel may not exist yet; LoadFollowerPortraits binds the art when it first meets the panel (BindPanelArt).
            _portraitContent = warmed;
            BindPanelArt();
        }
        else _portraitContent = warmed;
        _warmPortraitDoneId = id;
        PortraitTexture(id);
        GD.Print($"ARC-DEV-369 portrait warm {id}: {warmed.Portraits.GetValueOrDefault(id)?.Status.ToString() ?? "not read"}");
    }

    private void StartWindowArtWarm(string module)
    {
        // A worker of another module's slots still running only drops what it prepared once it ends.
        if (_warmTask is { } old) _ = old.ContinueWith(task => { if (task.IsCompletedSuccessfully) InterfaceHd.DropPrepared(task.Result.Frames); }, TaskScheduler.Default);
        _windowArtSlots = new(module,
            new(() => AreaInventoryArtContent.Load(module), LazyThreadSafetyMode.ExecutionAndPublication),
            new(() => AreaCharacterSheetArtContent.Load(module), LazyThreadSafetyMode.ExecutionAndPublication),
            new(() => AreaLogbookArtContent.Load(module), LazyThreadSafetyMode.ExecutionAndPublication));
        (_warmStep, _warmTask, _warmBuilt, _warmDelay) = (0, null, false, WarmStartFrames);
    }

    /// <summary>The worker side of a step: reads the window's art, then resolves its HD frames when <paramref name="hdPack"/> is given.</summary>
    private static WarmWorkerResult WarmWorker(WindowArtSlots slots, int step, HdPack? hdPack)
    {
        var started = Stopwatch.GetTimestamp();
        IEnumerable<(AreaInterfaceFrame Frame, bool Premultiplied)> frames;
        switch (step)
        {
            case 0:
                var inventory = slots.Inventory.Value;
                frames = inventory.ArtReady ? InventoryLookLoads(inventory).Select(load => (load.Frames[0], false)) : [];
                break;
            case 1:
                var sheet = slots.Sheet.Value;
                frames = sheet.ArtReady ? SheetLookLoads(sheet).SelectMany(load => load.Frames.Select(frame => (frame, true))) : [];
                break;
            default:
                var logbook = slots.Logbook.Value;
                frames = logbook.ArtReady ? LogbookLookLoads(logbook).SelectMany(load => load.Frames.Select(frame => (frame, true))) : [];
                break;
        }
        var list = frames.ToArray();
        var content = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var hdStarted = Stopwatch.GetTimestamp();
        var prepared = hdPack is null ? 0 : InterfaceHd.Prepare(list, hdPack, Math.Max(1, System.Environment.ProcessorCount / 2));
        return new([.. list.Select(entry => entry.Frame)], content, Stopwatch.GetElapsedTime(hdStarted).TotalMilliseconds, prepared);
    }

    // ARC-UI-3419: the first opening of the inventory still built its panel, asked for the first view and made the held items' pictures inside the key press and the
    // frame after it. With the art ready, no window open and a hero in play, the same work is done ahead, once per session: one frame builds the panel, one asks for the
    // hero's view, and the following ones make the pictures (with their HD redraws) of what the grid and the doll show, a few milliseconds a frame. Nothing is shown,
    // and the opening reads the view anew. An opening that comes first does all of it as before.
    private GameSessionOwner? _inventoryOpenWarmSession;
    // The pictures still to make: null before the view was asked, empty once the session is done.
    private Queue<string>? _inventoryOpenWarmPictures;

    private void TickInventoryOpenWarm()
    {
        if (!InventoryActive || InventoryOpen || WorldModal || _areaSession is not { } session) return;
        if (!ReferenceEquals(session, _inventoryOpenWarmSession)) (_inventoryOpenWarmSession, _inventoryOpenWarmPictures) = (session, null);
        if (_inventoryOpenWarmPictures is { Count: 0 }) return;
        try
        {
            if (_inventoryOpenWarmPictures is null && session.QueryHero() is null) return;
            if (InventoryArtLook() is not { } look) { _inventoryOpenWarmPictures = new(); return; }
            if (_inventoryPanel is null) { EnsureInventoryPanel(); _inventoryPanel.SetArtLook(look); return; }
            if (_inventoryOpenWarmPictures is null)
            {
                var view = InventoryView(null);
                _inventoryOpenWarmPictures = new(view.Grid.Select(item => item.Entity).Concat(view.Doll.Select(slot => slot.Entity).OfType<string>()));
                GD.Print($"ARC-UI-3419 inventory opening warm view items={_inventoryOpenWarmPictures.Count}");
                return;
            }
            var started = Stopwatch.GetTimestamp();
            do (look.ItemArt(_inventoryOpenWarmPictures.Dequeue()) as HdInterfaceTexture)?.Warm();
            while (_inventoryOpenWarmPictures.Count > 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds < WarmSliceMilliseconds);
            if (_inventoryOpenWarmPictures.Count == 0) GD.Print("ARC-UI-3419 inventory opening warmed");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _inventoryOpenWarmPictures = new();
            GD.Print($"ARC-UI-3419 inventory opening not warmed: {error.GetType().Name} {error.Message}");
        }
    }

    // The unbound session is let go.
    private void DropInventoryOpenWarm() => (_inventoryOpenWarmSession, _inventoryOpenWarmPictures) = (null, null);

    private void BuildWarmLook(int step)
    {
        switch (step)
        {
            case 0: InventoryArtLook(); break;
            case 1: SheetArtLookOfModule(); break;
            default: LogbookArtLook(); break;
        }
    }

    private void FinishWarmStep(WarmWorkerResult result, bool joined)
    {
        GD.Print($"ARC-DEV-369 window art warm {WarmStepNames[_warmStep]}: " + (joined
            ? $"opened by the player meanwhile; worker content {result.ContentMilliseconds:F0} ms, hd {result.HdPrepared} frames {result.HdMilliseconds:F0} ms"
            : $"worker content {result.ContentMilliseconds:F0} ms, hd {result.HdPrepared} frames {result.HdMilliseconds:F0} ms; main build {_warmBuildMilliseconds:F0} ms, " +
              $"upload {_warmSlices} slices {_warmSliceMilliseconds:F0} ms (worst {_warmSliceWorstMilliseconds:F0} ms)"));
        _warmTask = null; _warmBuilt = false; _warmStep++;
    }
}
