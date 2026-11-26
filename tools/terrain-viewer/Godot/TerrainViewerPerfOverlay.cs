using System.Diagnostics;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-3389 (perf epic #3377): the in-game performance overlay of the WORLD and AREA modes, off at every start, shown and hidden by
/// Ctrl+F. While hidden it costs one null test per frame. While shown it records each frame without allocating, writes its text twice a
/// second and measures the main viewport's render time. It takes no click and reads only: nothing of the session or the save changes.
/// External tools (Metal HUD, Instruments) are issue #3378's subject.
/// </summary>
public partial class TerrainViewerApp
{
    private readonly PerfOverlayChord _perfChord = new();
    private PerfOverlayPanel? _perfPanel;
    /// <summary>Null while the overlay is hidden.</summary>
    private PerfOverlayMeter? _perfMeter;
    private PerfRate _perfAllocated, _perfProcessor;

    /// <summary>After the bug report and the owner checks in the WORLD input route (their windows own every key). True when the event is
    /// the overlay's.</summary>
    private bool RoutePerfOverlayInput(InputEvent input)
    {
        if (input is not InputEventKey key) return false;
        var route = _perfChord.Route(key.Keycode == Key.F || key.PhysicalKeycode == Key.F, key.Pressed, key.Echo,
            key.CtrlPressed, key.ShiftPressed, key.AltPressed, key.MetaPressed);
        if (route == PerfOverlayRoute.PassThrough) return false;
        GetViewport().SetInputAsHandled();
        if (route == PerfOverlayRoute.Toggle) TogglePerfOverlay();
        return true;
    }

    private void TogglePerfOverlay()
    {
        var show = _perfMeter is null;
        _perfMeter = show ? new PerfOverlayMeter() : null;
        _perfAllocated = default; _perfProcessor = default;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), show);
        if (_perfPanel is null) { _perfPanel = new PerfOverlayPanel(); AddChild(_perfPanel); }
        _perfPanel.Set(show ? "Measuring..." : null, AreaGameBarPanel.TopInset);
        GD.Print($"ARC-DEV-3389 performance overlay {(show ? "shown" : "hidden")}");
    }

    /// <summary>Once per frame while shown, after the viewer's update that began at <paramref name="startedMicros"/>.</summary>
    private void TickPerfOverlay(long startedMicros)
    {
        if (_perfMeter is not { } meter) return;
        var now = (long)Time.GetTicksUsec();
        meter.Record(startedMicros, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), (now - startedMicros) / 1000f);
        if (meter.TakeRefresh(now)) _perfPanel!.Set(ReadPerfOverlay(meter, now).Text(), AreaGameBarPanel.TopInset);
    }

    private PerfOverlayReading ReadPerfOverlay(PerfOverlayMeter meter, long now)
    {
        static long? Count(Performance.Monitor monitor) => (long)Performance.GetMonitor(monitor);
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
            Count(Performance.Monitor.RenderTotalDrawCallsInFrame), Count(Performance.Monitor.RenderTotalObjectsInFrame),
            Count(Performance.Monitor.RenderTotalPrimitivesInFrame),
            resident, Count(Performance.Monitor.RenderVideoMemUsed) is > 0 and var video ? video : null,
            GC.GetTotalMemory(false), _perfAllocated.Sample(now, GC.GetTotalAllocatedBytes()),
            processor is { } used ? used * 100 : null,
            Count(Performance.Monitor.ObjectNodeCount), Count(Performance.Monitor.ObjectCount),
            size.X, size.Y);
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
