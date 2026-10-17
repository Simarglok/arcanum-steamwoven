using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-008 diagnostic logbook: the rumors and quests tabs of the session's PC (<see cref="AreaLogbook"/>), read only, as a
/// scrolled list instead of the original's book pages. Each entry is drawn in the original's colour for its state (struck
/// through for a quelled rumor, a completed, botched or elsewhere completed quest). The app rebuilds it whenever the session
/// or the tab changes.
/// </summary>
public partial class AreaLogbookPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private RichTextLabel _body = null!;
    private Label _hint = null!;
    private Button _rumors = null!;
    private Button _quests = null!;
    private Button _close = null!;
    private AreaLogbookView? _view;
    public event Action? CloseRequested;
    public event Action<AreaLogbookTab>? TabRequested;
    public bool IsOpen => Visible;
    internal AreaLogbookView? ViewForSelfTest => _view;
    internal string TextForSelfTest => _body.GetParsedText();

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaLogbook", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .97f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Text = "Logbook", Modulate = new Color(.2f, .12f, .05f) };
        var tabs = new HBoxContainer();
        _rumors = TabButton("Rumors", AreaLogbookTab.Rumors);
        _quests = TabButton("Quests", AreaLogbookTab.Quests);
        _close = new Button { Name = "Close", Text = "Close", FocusMode = Control.FocusModeEnum.All };
        _close.Pressed += () => CloseRequested?.Invoke();
        foreach (var button in new[] { _rumors, _quests, _close }) tabs.AddChild(button);
        _body = new RichTextLabel
        {
            BbcodeEnabled = true, ScrollActive = true, FitContent = false, SelectionEnabled = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 360),
        };
        _body.AddThemeColorOverride("default_color", Colors.Black);
        _hint = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "L, Escape or cancel closes; Tab or confirm switches the tab | PROJECT diagnostic window, read only: rumors and quests only " +
                "(no reputations, blessings and curses, kills and injuries, background or keys); original book pages and UI parity NOT_VERIFIED",
        };
        foreach (var control in new Control[] { _header, tabs, _body, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private Button TabButton(string text, AreaLogbookTab tab)
    {
        var button = new Button { Name = text, Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.All };
        button.Pressed += () => TabRequested?.Invoke(tab);
        return button;
    }

    public void Open(AreaLogbookView view)
    {
        Show(); Render(view);
        _close.GrabFocus();
    }

    public void CloseView()
    {
        _view = null; Hide();
        GetViewport().GuiReleaseFocus();
    }

    public void Render(AreaLogbookView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        _rumors.SetPressedNoSignal(view.Tab == AreaLogbookTab.Rumors);
        _quests.SetPressedNoSignal(view.Tab == AreaLogbookTab.Quests);
        _header.Text = $"Logbook | {AreaLogbook.TabName(view.Tab)} | {view.Entries.Count} entr{(view.Entries.Count == 1 ? "y" : "ies")}" +
            (view.LowIntelligence ? " | low-intelligence texts" : string.Empty) + (view.Note is null ? string.Empty : " | " + view.Note);
        _body.Text = view.Entries.Count == 0 ? "(no entries)" : string.Join("\n\n", view.Entries.Select(Entry));
        _body.ScrollToLine(0);
        ResizePanel();
    }

    /// <summary>Confirm on the focused tab button switches to it; anywhere else it switches to the other tab.</summary>
    public void Confirm()
    {
        if (_view is null) return;
        TabRequested?.Invoke(_view.Tab == AreaLogbookTab.Rumors ? AreaLogbookTab.Quests : AreaLogbookTab.Rumors);
    }

    private static string Entry(AreaLogbookEntry entry)
    {
        var text = Escape(AreaLogbook.Line(entry));
        return entry.Style switch
        {
            AreaLogbookStyle.Struck => $"[s]{text}[/s]",
            AreaLogbookStyle.Active => $"[color=#000096]{text}[/color]",
            AreaLogbookStyle.Completed => $"[color=#007800][s]{text}[/s][/color]",
            AreaLogbookStyle.Failed => $"[color=#960000][s]{text}[/s][/color]",
            _ => text,
        };
    }

    // Data text is shown literally: an opening bracket would start a BBCode tag.
    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(900, size.X - 48));
        var height = Math.Max(1, Math.Min(640, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
