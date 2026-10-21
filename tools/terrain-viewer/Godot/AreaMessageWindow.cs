using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-046: the AREA viewer's in-game message window, the smallest bounded stand-in for the original's: the newest entry
/// shown with its title and lines, a history of the last <see cref="HistorySize"/> entries browsed with ‹ and › (buttons that
/// take no focus), and × hiding the window until the next entry. A diagnostic panel: no original art.
/// </summary>
public partial class AreaMessageWindow : CanvasLayer
{
    public const int HistorySize = 10;
    private readonly List<AreaMessageEntry> _history = [];
    private int _shown = -1;
    private PanelContainer _panel = null!;
    private Label _title = null!, _line = null!, _detail = null!, _position = null!;
    private Button _older = null!, _newer = null!;

    internal IReadOnlyList<AreaMessageEntry> HistoryForSelfTest => _history.AsReadOnly();
    internal AreaMessageEntry? ShownForSelfTest => _shown >= 0 && _panel.Visible ? _history[_shown] : null;

    public override void _Ready()
    {
        Layer = 2;
        _panel = new PanelContainer { Name = "MessageWindow", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .88f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        AddChild(_panel);
        var column = new VBoxContainer(); _panel.AddChild(column);
        var bar = new HBoxContainer(); column.AddChild(bar);
        _title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; bar.AddChild(_title);
        _older = Small("‹", () => Browse(-1)); bar.AddChild(_older);
        _position = new Label(); bar.AddChild(_position);
        _newer = Small("›", () => Browse(1)); bar.AddChild(_newer);
        bar.AddChild(Small("×", () => _panel.Hide()));
        _line = new Label(); column.AddChild(_line);
        _detail = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(420, 0) }; column.AddChild(_detail);
        GetViewport().SizeChanged += Place;
    }

    private static Button Small(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }

    /// <summary>Adds an entry (the oldest one leaves past <see cref="HistorySize"/>) and shows it.</summary>
    public void Post(AreaMessageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _history.Add(entry);
        if (_history.Count > HistorySize) _history.RemoveAt(0);
        _shown = _history.Count - 1;
        Render();
    }

    /// <summary>Empties the history and hides the window (a new session).</summary>
    public void Clear() { _history.Clear(); _shown = -1; _panel.Hide(); }

    private void Browse(int step)
    {
        if (_history.Count == 0) return;
        _shown = Math.Clamp(_shown + step, 0, _history.Count - 1);
        Render();
    }

    private void Render()
    {
        if (_shown < 0) { _panel.Hide(); return; }
        var entry = _history[_shown];
        _title.Text = entry.Title; _line.Text = entry.Line; _detail.Text = entry.Detail;
        _position.Text = $"{_shown + 1}/{_history.Count}";
        _older.Disabled = _shown == 0; _newer.Disabled = _shown == _history.Count - 1;
        _panel.Show();
        Place();
    }

    private void Place()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _panel.ResetSize();
        var panel = _panel.GetCombinedMinimumSize();
        _panel.Position = new(Math.Max(12, (size.X - panel.X) / 2), Math.Max(12, size.Y - panel.Y - 12));
    }
}
