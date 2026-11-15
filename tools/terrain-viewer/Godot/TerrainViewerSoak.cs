using System.Diagnostics;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-2279: the soak run. With <c>ARC_SOAK_LOG</c> set the viewer writes its resource counters (Godot's render texture and video
/// memory, object, node and orphan counts, its static memory, the .NET heap and collections, the working set, the thread count) to a
/// CSV file at the start and every <c>ARC_SOAK_INTERVAL</c> seconds (30 by default); <c>ARC_SOAK_WINDOW</c> opens the New Character
/// window or the character editor over the main menu and leaves it idle; <c>ARC_SOAK_MINUTES</c> ends the viewer by itself with a
/// summary line (the log's last row, after a <c>#</c>). Development opt-in through the environment: no argument, no change for the
/// player. How to run it: docs/workflow/mac-crash-forensics.md ("Soak in the creation window").
/// </summary>
public partial class TerrainViewerApp
{
    private ViewerSoakSettings? _soak;
    private readonly List<ViewerSoakSample> _soakSamples = [];
    private long _soakStartMsec;
    private bool _soakWindowOpened;

    /// <summary>Reads the soak environment and starts the counters (once, at the start; a self-test starts none).</summary>
    private void StartSoak()
    {
        if (SelfTestBootstrap || _soak is not null) return;
        var settings = ViewerSoakSettings.Read(System.Environment.GetEnvironmentVariable, out var problem);
        if (problem is not null) { GD.Print("ARC-DEV-2279 soak refused: " + problem); return; }
        if (settings is null) return;
        try { System.IO.File.WriteAllText(settings.LogPath, ViewerSoakSample.Header + "\n"); }
        catch (Exception failure) when (failure is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { GD.Print($"ARC-DEV-2279 soak refused: the log '{settings.LogPath}' cannot be written ({failure.Message})"); return; }
        _soak = settings; _soakStartMsec = (long)Time.GetTicksMsec();
        GD.Print($"ARC-DEV-2279 soak start pid={System.Environment.ProcessId} log={settings.LogPath} interval={settings.IntervalSeconds}s minutes={(settings.RunMinutes?.ToString() ?? "-")} window={settings.Window}");
        var timer = new Godot.Timer { WaitTime = settings.IntervalSeconds, OneShot = false, Autostart = true, ProcessCallback = Godot.Timer.TimerProcessCallback.Idle };
        timer.Timeout += SoakTick;
        AddChild(timer);
        SoakTick();
    }

    private void SoakTick()
    {
        if (_soak is not { } soak) return;
        var elapsed = (int)(((long)Time.GetTicksMsec() - _soakStartMsec) / 1000);
        try
        {
            var sample = ReadSoakSample(elapsed);
            _soakSamples.Add(sample);
            System.IO.File.AppendAllText(soak.LogPath, sample.Line() + "\n");
        }
        // A reading must never end the viewer (the process's thread list can fail while a thread exits): the row is skipped.
        catch (Exception failure) { GD.Print("ARC-DEV-2279 soak reading skipped: " + failure.Message); }
        if (soak.RunMinutes is { } minutes && elapsed >= minutes * 60) EndSoak();
    }

    private ViewerSoakSample ReadSoakSample(int elapsed)
    {
        long Monitor(Performance.Monitor monitor) => (long)Performance.GetMonitor(monitor);
        using var process = Process.GetCurrentProcess();
        return new(elapsed, _mainMenu?.Window.ToString() ?? "none", Performance.GetMonitor(Performance.Monitor.TimeFps),
            Monitor(Performance.Monitor.RenderTextureMemUsed), Monitor(Performance.Monitor.RenderVideoMemUsed), Monitor(Performance.Monitor.RenderBufferMemUsed),
            Monitor(Performance.Monitor.ObjectCount), Monitor(Performance.Monitor.ObjectResourceCount), Monitor(Performance.Monitor.ObjectNodeCount),
            Monitor(Performance.Monitor.ObjectOrphanNodeCount), Monitor(Performance.Monitor.MemoryStatic), GC.GetTotalMemory(false), GcLiveBytes(),
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), process.WorkingSet64, process.Threads.Count);
    }

    /// <summary>The bytes that survived the latest full collection (blocking or background), 0 before the first.</summary>
    private static long GcLiveBytes()
    {
        var blocking = GC.GetGCMemoryInfo(GCKind.FullBlocking); var background = GC.GetGCMemoryInfo(GCKind.Background);
        return Math.Max(0, (background.Index > blocking.Index ? background : blocking).PromotedBytes);
    }

    private void EndSoak()
    {
        if (_soak is not { } soak) return;
        _soak = null;
        var summary = ViewerSoakReport.Summary(_soakSamples);
        try { System.IO.File.AppendAllText(soak.LogPath, "# " + summary + "\n"); }
        catch (Exception failure) when (failure is System.IO.IOException or UnauthorizedAccessException) { GD.Print("ARC-DEV-2279 soak log write failed: " + failure.Message); }
        GD.Print(summary);
        GD.Print("ARC-DEV-2279 soak end: the viewer quits");
        GetTree().Quit(0);
    }

    /// <summary>Once the main menu stands (early or after the scene), opens the soak's window by the player's own route: Single Player,
    /// New Game, New Character, and for the editor its Accept (held until the start scene is ready, as for the player).</summary>
    private void SoakOpenWindow()
    {
        if (_soak is not { Window: not ViewerSoakWindow.None } soak || _soakWindowOpened || _mainMenu is null) return;
        _soakWindowOpened = true;
        foreach (var message in new[] { 10, 50, 421 })
        {
            if (_mainMenu is null) return;
            var before = _mainMenu.Window;
            var index = _mainMenu.Shown.ToList().FindIndex(button => button.Message == message);
            if (index < 0) { GD.Print($"ARC-DEV-2279 soak: the menu shows no button {message} in window {before}"); return; }
            MainMenuCommand(() => _mainMenu!.Activate(index));
            if (_mainMenu is not null && _mainMenu.Window == before) { GD.Print($"ARC-DEV-2279 soak: button {message} did not leave window {before} (menu busy or held)"); return; }
        }
        if (_mainMenu?.Window != AreaMenuWindow.NewCharacter) { GD.Print($"ARC-DEV-2279 soak: the New Character window did not open (window {_mainMenu?.Window.ToString() ?? "none"}: {_mainMenu?.Notice})"); return; }
        if (soak.Window == ViewerSoakWindow.Editor) MainMenuCommand(() => new(AreaMenuCommandKind.OpenCharacterEditor));
        GD.Print($"ARC-DEV-2279 soak window {soak.Window} requested; menu window now {_mainMenu?.Window.ToString() ?? "none"}");
    }
}
