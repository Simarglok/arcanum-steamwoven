using System.Diagnostics;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-3389 (perf epic #3377): the in-game performance overlay of the WORLD and AREA modes, off at every start. Ctrl+F cycles off,
/// the panel and, on a macOS that can switch it, Apple's Metal Performance HUD. While the panel is hidden it costs one
/// null test per frame. While shown it records each frame without allocating, writes its text twice a second and measures the main
/// viewport's render time. It takes no click and reads only: nothing of the session or the save changes.
/// </summary>
public partial class TerrainViewerApp
{
    private readonly PerfOverlayChord _perfChord = new();
    private PerfOverlayPanel? _perfPanel;
    /// <summary>Null while the overlay is hidden.</summary>
    private PerfOverlayMeter? _perfMeter;
    private PerfRate _perfAllocated, _perfProcessor;
    private PerfOverlayMode _perfMode;
    /// <summary>The Metal HUD of this run, null where there is none to switch (headless, another OS, a macOS without the tool).</summary>
    private MetalHudSwitch? _metalHud;
    /// <summary>Why the latest step to the HUD could not load it, null otherwise.</summary>
    private string? _metalHudProblem;
    /// <summary>The window size and top bar height the shown HUD was last placed for.</summary>
    private (Vector2I Window, float TopInset) _metalHudPlace;

    /// <summary>Once, at the start: whether the HUD joins the cycle, and whether the start's environment already shows it.</summary>
    private void StartPerfOverlay()
    {
        _metalHud = DisplayServer.GetName() == "headless" ? null
            : MetalHudSwitch.Find(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowView));
        // A start that already shows the HUD is placed by its first frame (no window has this size).
        _perfMode = PerfOverlayCycle.Start(System.Environment.GetEnvironmentVariable, _metalHud is not null);
    }

    /// <summary>Puts the HUD where the panel sits (right edge, under the top bar) for the window as it is now; with
    /// <paramref name="load"/> (a step of the cycle) also loads it when it is not.</summary>
    private void PlaceMetalHud(bool load)
    {
        var window = DisplayServer.WindowGetSize();
        _metalHudPlace = (window, AreaGameBarPanel.TopInset);
        // The bar's height is in the viewport's units; the HUD is placed in the window's.
        var perUnit = window.Y / Math.Max(1.0, GetViewport().GetVisibleRect().Size.Y);
        var (x, y) = PerfOverlayCycle.HudPosition(window.X, AreaGameBarPanel.TopInset * perUnit, _metalHud!.Scale);
        if (load) _metalHudProblem = _metalHud.Show(x, y);
        else _metalHud.Place(x, y);
    }

    /// <summary>Per frame while the HUD is shown: it is placed again when the window or the top bar changed.</summary>
    private void FollowMetalHud()
    {
        if (_metalHudPlace != (DisplayServer.WindowGetSize(), AreaGameBarPanel.TopInset)) PlaceMetalHud(load: false);
    }

    /// <summary>After the bug report and the owner checks in the WORLD input route (their windows own every key). True when the event is
    /// the overlay's.</summary>
    private bool RoutePerfOverlayInput(InputEvent input)
    {
        if (input is not InputEventKey key) return false;
        var route = _perfChord.Route(key.Keycode == Key.F || key.PhysicalKeycode == Key.F, key.Pressed, key.Echo,
            key.CtrlPressed, key.ShiftPressed, key.AltPressed, key.MetaPressed);
        if (route == PerfOverlayRoute.PassThrough) return false;
        GetViewport().SetInputAsHandled();
        if (route == PerfOverlayRoute.Toggle) ShowPerf(PerfOverlayCycle.Next(_perfMode, _metalHud is not null));
        return true;
    }

    private void ShowPerf(PerfOverlayMode mode)
    {
        var previous = _perfMode;
        _perfMode = mode;
        var show = mode == PerfOverlayMode.Overlay;
        if (show != (_perfMeter is not null))
        {
            _perfMeter = show ? new PerfOverlayMeter() : null;
            _perfAllocated = default; _perfProcessor = default;
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), show);
            if (_perfPanel is null) { _perfPanel = new PerfOverlayPanel(); AddChild(_perfPanel); }
            _perfPanel.Set(show ? "Measuring..." : null, AreaGameBarPanel.TopInset);
        }
        _metalHudProblem = null;
        if (mode == PerfOverlayMode.MetalHud) PlaceMetalHud(load: true);
        else if (previous == PerfOverlayMode.MetalHud) _metalHud!.Hide();
        GD.Print($"ARC-DEV-3389 performance overlay mode={mode}" + (_metalHudProblem is null ? "" : $" metal-hud-problem={_metalHudProblem}"));
    }

    /// <summary>The viewer's frame update while the overlay is shown: timed, recorded and, when due, written to the panel.</summary>
    private void ProcessTimedViewerFrame(double delta)
    {
        var startedMicros = (long)Time.GetTicksUsec();
        ProcessViewerFrame(delta);
        if (_perfMeter is not { } meter) return;
        var now = (long)Time.GetTicksUsec();
        meter.Record(startedMicros, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), (now - startedMicros) / 1000f);
        if (meter.TakeRefresh(now)) _perfPanel!.Set(ReadPerfOverlay(meter, now).Text(), AreaGameBarPanel.TopInset);
    }

    private PerfOverlayReading ReadPerfOverlay(PerfOverlayMeter meter, long now)
    {
        static long Count(Performance.Monitor monitor) => (long)Performance.GetMonitor(monitor);
        // With the panel on screen a real frame has draws and the renderer holds memory: 0 is a counter this renderer does not offer.
        static long? Drawn(Performance.Monitor monitor) => Count(monitor) is var value && value > 0 ? value : null;
        // A time of exactly 0 is a timer the driver does not offer (or no frame yet), not a free frame.
        static double? Milliseconds(double ms) => ms > 0 ? ms : null;
        // Another observer may have switched the measurement off (a smoke stage does): ask again with each reading.
        var viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        long? resident = null; double? processorSeconds = null;
        try
        {
            using var process = Process.GetCurrentProcess();
            resident = process.WorkingSet64 is > 0 and var bytes ? bytes : null;
            processorSeconds = process.TotalProcessorTime.TotalSeconds;
        }
        catch (Exception failure) when (failure is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or PlatformNotSupportedException) { }
        var size = DisplayServer.WindowGetSize();
        var processor = _perfProcessor.Sample(now, processorSeconds);
        return new(meter.Read(),
            Milliseconds(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000),
            Milliseconds(RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu()),
            Milliseconds(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport)),
            Drawn(Performance.Monitor.RenderTotalDrawCallsInFrame), Drawn(Performance.Monitor.RenderTotalObjectsInFrame),
            Drawn(Performance.Monitor.RenderTotalPrimitivesInFrame),
            resident, Drawn(Performance.Monitor.RenderVideoMemUsed),
            GC.GetTotalMemory(false), _perfAllocated.Sample(now, GC.GetTotalAllocatedBytes()),
            processor is { } used ? used * 100 : null,
            Count(Performance.Monitor.ObjectNodeCount), Count(Performance.Monitor.ObjectCount),
            size.X, size.Y,
            // The pacing's target, not Engine.MaxFps: that is the target with the measured correction on it.
            PacingAutomated || _pacingTarget <= 0 ? null : _pacingTarget,
            _metalHud is not null);
    }
}

/// <summary>ARC-DEV-3389: the overlay's panel at the right edge below the interface bars' top strip (the top corner without bars),
/// above the game's windows; the pointer passes through it.</summary>
public partial class PerfOverlayPanel : CanvasLayer
{
    private const int Margin = 8;
    private PanelContainer _panel = null!;
    private Label _label = null!;
    public bool IsShown => _panel.Visible;
    internal string TextForSelfTest => _label.Text;
    internal Rect2 RectForSelfTest => _panel.GetGlobalRect();
    internal bool TakesNoPointerForSelfTest => _panel.MouseFilter == Control.MouseFilterEnum.Ignore && _label.MouseFilter == Control.MouseFilterEnum.Ignore;

    public override void _Ready() => Build();

    private void Build()
    {
        if (_panel is not null) return;
        Layer = 95;
        _panel = new PanelContainer { Name = "PerfOverlay", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false,
            GrowHorizontal = Control.GrowDirection.Begin };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, .65f), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        _label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _label.AddThemeFontOverride("font", new SystemFont { FontNames = ["Menlo", "Consolas", "DejaVu Sans Mono", "monospace"] });
        _label.AddThemeFontSizeOverride("font_size", 12);
        _label.AddThemeColorOverride("font_color", new Color(.9f, 1f, .85f));
        _panel.AddChild(_label);
        AddChild(_panel);
    }

    /// <summary>Shows <paramref name="text"/> with the panel's top <paramref name="topInset"/> down (the height the top bar takes), or
    /// hides the panel when the text is null. The panel keeps its right edge at the window's.</summary>
    public void Set(string? text, float topInset = 0)
    {
        Build();
        _panel.Visible = text is not null;
        _label.Text = text ?? "";
        _panel.Size = Vector2.Zero;
        _panel.OffsetRight = -Margin; _panel.OffsetLeft = -Margin - _panel.GetCombinedMinimumSize().X;
        _panel.OffsetTop = topInset + Margin; _panel.OffsetBottom = topInset + Margin + _panel.GetCombinedMinimumSize().Y;
    }
}
