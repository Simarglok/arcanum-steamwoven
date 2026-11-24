using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-3136: the popup of <see cref="AreaArtTip"/>: one panel on a top canvas layer, shown for the control the pointer rests on.</summary>
public partial class AreaArtTipPopup : CanvasLayer
{
    internal const string NodeName = "AreaArtTip";
    private const double Delay = .5;

    private PanelContainer _panel = null!;
    private AreaArtTextBlock _text = null!;
    private Control? _target;
    private double _elapsed;
    private string _shown = "";

    internal string Shown => _panel.Visible ? _shown : "";

    internal static void Enter(Control control)
    {
        if (!GodotObject.IsInstanceValid(control) || !control.IsInsideTree()) return;
        var root = control.GetTree().Root;
        var popup = root.GetNodeOrNull<AreaArtTipPopup>(NodeName);
        if (popup is null)
        {
            popup = new AreaArtTipPopup { Name = NodeName };
            root.AddChild(popup);
        }
        popup._target = control; popup._elapsed = 0; popup._panel.Visible = false;
    }

    internal static void Leave(Control control)
    {
        if (!GodotObject.IsInstanceValid(control) || !control.IsInsideTree()) return;
        if (control.GetTree().Root.GetNodeOrNull<AreaArtTipPopup>(NodeName) is { } popup && popup._target == control) popup.Stop();
    }

    public override void _Ready()
    {
        Layer = 100;
        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .9f),
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        _text = new AreaArtTextBlock { Name = "TipText", FitContent = true };
        _panel.AddChild(_text);
        AddChild(_panel);
    }

    private void Stop()
    {
        _target = null; _shown = ""; _panel.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_target is null) return;
        if (!IsInstanceValid(_target) || !_target.IsVisibleInTree()) { Stop(); return; }
        // A press ends the hint, as Godot's tooltip did; entering the control again brings it back.
        if (Godot.Input.IsMouseButtonPressed(MouseButton.Left) || Godot.Input.IsMouseButtonPressed(MouseButton.Right)) { Stop(); return; }
        var hint = AreaArtTip.Get(_target);
        var fonts = AreaFallbackLook.Fonts?.Invoke();
        if (hint.Length == 0 || fonts is null) { _panel.Visible = false; return; }
        _elapsed += delta;
        if (_elapsed < Delay && !_panel.Visible) return;
        if (_shown != hint || !_panel.Visible)
        {
            _shown = hint;
            var (placed, _, _) = AreaTextFontLayout.Block(fonts.Text.Font, fonts.TextLineHeight, hint);
            _text.Set([new(fonts.Text, fonts.TextLineHeight, placed.Lines, 0, 0, (255, 255, 255))]);
            _panel.ResetSize();
        }
        var at = GetViewport().GetMousePosition();
        var size = _panel.GetCombinedMinimumSize(); var view = GetViewport().GetVisibleRect().Size;
        var x = at.X + 16 + size.X > view.X ? at.X - 8 - size.X : at.X + 16;
        var y = at.Y + 20 + size.Y > view.Y ? at.Y - 8 - size.Y : at.Y + 20;
        _panel.Position = new Vector2(Mathf.Round(Math.Max(0, x)), Mathf.Round(Math.Max(0, y)));
        _panel.Visible = true;
    }
}
