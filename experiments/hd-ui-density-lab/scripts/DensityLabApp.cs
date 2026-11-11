using System.Globalization;
using Arcanum.Experiments.HdUiDensityLab.Core;
using Godot;

namespace Arcanum.Experiments.HdUiDensityLab;

/// <summary>ARC-RES-122 lab: generated interface chrome sized by the OS display scale. Keys: +/- player scale, S forced
/// scale, R ring width, L light direction, G glass tint, D diagnostics, F11 fullscreen. <c>-- --self-test [--capture DIR]</c>
/// renders the mock screen at fixed scales off screen, checks the device-pixel grid on the read-back frames and quits.</summary>
public partial class DensityLabApp : Control
{
    private static readonly double?[] ForcedChoices = [null, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.75];

    private MockCanvas _canvas = null!;
    private int _userPercent = 100;
    private int _forcedIndex;
    private bool _diagnostics = true;
    private Interaction _interaction = Interaction.None;
    private (Vector2I Size, int Screen, float Scale, int Dpi) _lastReading;
    private ScaleDecision? _decision;
    private HostKind _host;

    public override void _Ready()
    {
        _host = DetectHost();
        MouseFilter = MouseFilterEnum.Ignore;
        _canvas = new MockCanvas();
        AddChild(_canvas);
        SetAnchorsPreset(LayoutPreset.FullRect);
        _canvas.SetAnchorsPreset(LayoutPreset.FullRect);
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--self-test"))
        {
            var captureIndex = Array.IndexOf(args, "--capture");
            var capture = captureIndex >= 0 && captureIndex + 1 < args.Length ? args[captureIndex + 1] : null;
            _ = SelfTest.RunAsync(this, _host, capture);
            return;
        }
        SizeWindowToPoints();
        GetWindow().DpiChanged += Recompute;
        GetViewport().SizeChanged += Recompute;
        Recompute();
        GD.Print($"ARC-RES-122 lab start host={_host} driver={DisplayServer.GetName()} {Describe()}");
        for (var i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var size = DisplayServer.ScreenGetSize(i);
            GD.Print($"ARC-RES-122 lab screen {i} size={size.X}x{size.Y} reportedScale={F(DisplayServer.ScreenGetScale(i))} dpi={DisplayServer.ScreenGetDpi(i)} maxScale={F(DisplayServer.ScreenGetMaxScale())}");
        }
    }

    public override void _Process(double delta)
    {
        if (_decision is null) return;
        var reading = Reading();
        if (reading != _lastReading) Recompute();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_decision is null || _canvas.Model is null) return;
        switch (@event)
        {
            case InputEventMouseMotion motion:
                SetInteraction(_interaction with { Hover = HitId(motion.Position) });
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                SetInteraction(_interaction with { Down = button.Pressed ? HitId(button.Position) : null });
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                OnKey(key.Keycode);
                break;
        }
    }

    internal static HostKind DetectHost()
    {
        var os = OS.GetName();
        if (os == "macOS") return HostKind.MacOS;
        if (os == "Windows") return HostKind.Windows;
        if (os != "Linux") return HostKind.Other;
        if (IsSteamOs()) return HostKind.SteamOS;
        return DisplayServer.GetName() == "Wayland" ? HostKind.LinuxWayland : HostKind.LinuxX11;
    }

    private static bool IsSteamOs()
    {
        try
        {
            return File.Exists("/etc/os-release")
                   && File.ReadLines("/etc/os-release").Any(l => l.Trim() is "ID=steamos" or "ID=\"steamos\"");
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static (Vector2I Size, int Screen, float Scale, int Dpi) Reading()
    {
        var screen = DisplayServer.WindowGetCurrentScreen();
        return (DisplayServer.WindowGetSize(), screen, DisplayServer.ScreenGetScale(), DisplayServer.ScreenGetDpi(screen));
    }

    /// <summary>Opens the window at 1280 × 800 logical points of its screen, within the usable area.</summary>
    private void SizeWindowToPoints()
    {
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;
        var (_, screen, scale, dpi) = Reading();
        var k = DensityScale.OsScale(new DisplayReading(_host, scale, dpi, 1, 1));
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var size = new Vector2I(
            Math.Min(DeviceGrid.Round(1280 * k), usable.Size.X - 32),
            Math.Min(DeviceGrid.Round(800 * k), usable.Size.Y - 32));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
    }

    private void Recompute()
    {
        _lastReading = Reading();
        var (size, _, scale, dpi) = _lastReading;
        var viewport = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, (int)viewport.X);
        var height = Math.Max(1, (int)viewport.Y);
        _decision = DensityScale.Decide(new DisplayReading(_host, scale, dpi, width, height), _userPercent, ForcedChoices[_forcedIndex]);
        Rebuild();
        GD.Print($"ARC-RES-122 lab scale {Describe()} window={size.X}x{size.Y}");
    }

    private void SetInteraction(Interaction next)
    {
        if (next == _interaction) return;
        _interaction = next;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_decision is null) return;
        var viewport = GetViewport().GetVisibleRect().Size;
        var model = MockScreen.Build(Math.Max(1, (int)viewport.X), Math.Max(1, (int)viewport.Y), _decision.Scale, _interaction);
        _canvas.Model = _diagnostics ? WithDiagnostics(model) : model;
    }

    private ScreenModel WithDiagnostics(ScreenModel model)
    {
        var size = Math.Max(12, DeviceGrid.Round(model.Scale * 8));
        var lines = DiagnosticLines();
        var texts = model.Texts.ToList();
        var ops = model.Ops.ToList();
        var lineHeight = DeviceGrid.Round(size * 1.3);
        var box = new DeviceRect(model.PlayArea.X + size, model.PlayArea.Y + size / 2, size * 34, lineHeight * lines.Count + size / 2);
        ops.Add(new ChromeOp(box, Token.Socket));
        for (var i = 0; i < lines.Count; i++)
            texts.Add(new TextOp(lines[i], box.X + size / 2, box.Y + size / 4 + i * lineHeight, size, Token.Diagnostics));
        return model with { Ops = ops, Texts = texts };
    }

    private List<string> DiagnosticLines()
    {
        var d = _decision!;
        var (size, screen, scale, dpi) = _lastReading;
        return
        [
            $"host {_host}, driver {DisplayServer.GetName()}, screen {screen}",
            $"reported scale {F(scale)}, DPI {dpi}, window {size.X}x{size.Y} px",
            $"OS scale k {F(d.OsScale)}, player {_userPercent}%",
            $"density {F(d.DensityScale)}, fit {F(d.FitScale)}",
            $"s = {F(d.Scale)} ({d.Source})",
            $"ring {F(_interaction.Lens.RingUnits)} u, light {_interaction.Lens.LightAngleDegrees} deg",
            "+/- player  S forced  R ring  L light  G glass  D diag",
        ];
    }

    private string Describe()
    {
        var d = _decision;
        return d is null
            ? "s=?"
            : $"k={F(d.OsScale)} u={_userPercent} density={F(d.DensityScale)} fit={F(d.FitScale)} s={F(d.Scale)} source={d.Source}";
    }

    private string? HitId(Vector2 position)
    {
        var model = _canvas.Model;
        if (model is null) return null;
        var x = (int)Math.Floor(position.X);
        var y = (int)Math.Floor(position.Y);
        return model.Buttons.FirstOrDefault(b => b.Enabled && b.Rect.Contains(x, y))?.Id;
    }

    private void OnKey(Key key)
    {
        var lens = _interaction.Lens;
        switch (key)
        {
            case Key.Equal or Key.Plus or Key.KpAdd:
                _userPercent = Math.Min(DensityScale.MaxUserPercent, _userPercent + 25);
                break;
            case Key.Minus or Key.KpSubtract:
                _userPercent = Math.Max(DensityScale.MinUserPercent, _userPercent - 25);
                break;
            case Key.S:
                _forcedIndex = (_forcedIndex + 1) % ForcedChoices.Length;
                break;
            case Key.R:
                var next = (Array.IndexOf(LensStyle.RingChoices, lens.RingUnits) + 1) % LensStyle.RingChoices.Length;
                _interaction = _interaction with { Lens = lens with { RingUnits = LensStyle.RingChoices[next] } };
                break;
            case Key.L:
                _interaction = _interaction with { Lens = lens with { LightAngleDegrees = (lens.LightAngleDegrees + 45) % 360 } };
                break;
            case Key.G:
                _interaction = _interaction with { Lens = lens with { GlassIndex = (lens.GlassIndex + 1) % LensStyle.GlassChoices } };
                break;
            case Key.D:
                _diagnostics = !_diagnostics;
                break;
            case Key.F11:
                var mode = DisplayServer.WindowGetMode();
                DisplayServer.WindowSetMode(mode == DisplayServer.WindowMode.Fullscreen ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
                return;
            default:
                return;
        }
        Recompute();
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
