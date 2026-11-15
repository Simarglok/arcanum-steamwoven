using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-2190 owner-checks overlay: the steps of the owner QA list (current step first; filters open / this section / all), the
/// step's text, Same / Differs / Cannot test, a note and Send report (a Ctrl+B-style report tagged with the step), over a dimmed screen
/// that takes every click, below the bug report window. A PROJECT window (the original has no such window); no original art; all text
/// is English. The app owns the files and the report; the window shows <see cref="OwnerQaSession"/> and reports the buttons.
/// </summary>
public partial class AreaOwnerQaPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private Label _hint = null!;
    private Button _open = null!, _section = null!, _all = null!;
    private ItemList _list = null!;
    private Label _detail = null!;
    private TextEdit _note = null!;
    private Label _notice = null!;
    private Button _same = null!, _differs = null!, _cannot = null!, _next = null!, _send = null!, _close = null!;
    private OwnerQaSession? _session;
    private IReadOnlyList<OwnerQaStep> _rows = [];
    public event Action<string>? MarkRequested;
    public event Action? NextRequested;
    public event Action? SendRequested;
    public event Action? CloseRequested;
    public event Action<string>? StepChosen;
    public event Action? FilterChanged;
    public bool IsOpen => Visible;
    public string Note => _note.Text;
    internal int RowCountForSelfTest => _list.ItemCount;
    internal string RowTextForSelfTest(int index) => _list.GetItemText(index);
    internal string NoticeForSelfTest => _notice.Text;
    internal string DetailForSelfTest => _detail.Text;
    internal Rect2 PanelRectForSelfTest => _panel.GetRect();
    internal void SetNoteForSelfTest(string text) => _note.Text = text;
    internal void PressForSelfTest(string name)
    {
        var button = name switch { "same" => _same, "differs" => _differs, "cannot-test" => _cannot, "next" => _next, "send" => _send, "close" => _close,
            "open" => _open, "section" => _section, _ => _all };
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    public override void _Ready()
    {
        // Above every window of the viewer (the highest is 7) and the HUD, below the bug report window (20).
        Layer = 19;
        var shade = new ColorRect { Name = "OwnerQaShade", Color = new Color(0, 0, 0, .45f), MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        _panel = new PanelContainer { Name = "AreaOwnerQa", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .98f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        var ink = new Color(.2f, .12f, .05f);
        _header = new Label { Text = "Owner checks (the game is paused)", Modulate = ink };
        _hint = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Pick a step, then Same / Differs / Cannot test. Send report first when a difference needs a screenshot, log and save. Ctrl+Q or Escape closes; Ctrl+Shift+Q hides the plate.",
        };
        var filters = new HBoxContainer();
        _open = Make("OwnerQaFilterOpen", "Open", () => { _session!.Filter = OwnerQaFilter.Open; FilterChanged?.Invoke(); });
        _section = Make("OwnerQaFilterSection", "This section", () => { _session!.Filter = OwnerQaFilter.Section; FilterChanged?.Invoke(); });
        _all = Make("OwnerQaFilterAll", "All", () => { _session!.Filter = OwnerQaFilter.All; FilterChanged?.Invoke(); });
        foreach (var button in new[] { _open, _section, _all }) { button.ToggleMode = true; filters.AddChild(button); }
        _list = new ItemList
        {
            Name = "OwnerQaList", CustomMinimumSize = new Vector2(480, 150), SizeFlagsVertical = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.All,
            AllowReselect = true,
        };
        _list.ItemSelected += index => { if (index >= 0 && index < _rows.Count) StepChosen?.Invoke(_rows[(int)index].Id); };
        _detail = new Label { Name = "OwnerQaDetail", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = ink, CustomMinimumSize = new Vector2(480, 90) };
        _note = new TextEdit
        {
            Name = "OwnerQaNote", PlaceholderText = "note (what you saw; goes into the result and the report)", WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(480, 56), FocusMode = Control.FocusModeEnum.All,
        };
        _notice = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.45f, .1f, .05f) };
        var buttons = new HBoxContainer();
        _same = Make("OwnerQaSame", "Same", () => MarkRequested?.Invoke("same"));
        _differs = Make("OwnerQaDiffers", "Differs", () => MarkRequested?.Invoke("differs"));
        _cannot = Make("OwnerQaCannotTest", "Cannot test", () => MarkRequested?.Invoke("cannot-test"));
        _next = Make("OwnerQaNext", "Next open", () => NextRequested?.Invoke());
        _send = Make("OwnerQaSend", "Send report", () => SendRequested?.Invoke());
        _close = Make("OwnerQaClose", "Close", () => CloseRequested?.Invoke());
        foreach (var button in new[] { _same, _differs, _cannot, _next, _send, _close }) buttons.AddChild(button);
        foreach (var control in new Control[] { _header, _hint, filters, _list, _detail, _note, _notice, buttons }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        _panel.MinimumSizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private static Button Make(string name, string text, Action pressed)
    {
        var button = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += () => pressed();
        return button;
    }

    /// <summary>Shows the window over <paramref name="session"/>; <paramref name="notice"/> says what the report could not take.</summary>
    public void Open(OwnerQaSession session, string notice)
    {
        _session = session; _notice.Text = notice;
        _note.Text = "";
        Show(); Refresh(null); ResizePanel();
    }

    /// <summary>Redraws the list and the detail from the session; <paramref name="notice"/> (when given) replaces the message.</summary>
    public void Refresh(string? notice)
    {
        if (_session is not { } session) return;
        if (notice is not null) _notice.Text = notice;
        _rows = session.Rows();
        _list.Clear();
        foreach (var row in _rows)
        {
            var mark = session.EffectiveStatus(row) switch { "same" => "[same]", "differs" => "[differs]", "cannot-test" => "[cannot]", _ => "[open]" };
            var text = row.Step.Length > 70 ? row.Step[..70] + "..." : row.Step;
            _list.AddItem((row.Id == session.CurrentId ? "> " : "  ") + row.Id + " " + mark + " " + text);
        }
        if (_rows.Count > 0) _list.Select(0);
        _open.ButtonPressed = session.Filter == OwnerQaFilter.Open; _section.ButtonPressed = session.Filter == OwnerQaFilter.Section; _all.ButtonPressed = session.Filter == OwnerQaFilter.All;
        _header.Text = $"Owner checks (the game is paused) - build {(session.Build.Length == 0 ? "?" : session.Build)}, list {(session.ListCommit.Length == 0 ? "?" : session.ListCommit)}";
        var current = session.Current;
        if (current is null)
        {
            _detail.Text = session.Problem ?? "No step is selected.";
        }
        else
        {
            var result = session.ResultOf(current.Id);
            _detail.Text = $"{current.Id} - {current.Section}\nStep: {current.Step}\nExpected: {current.Expected}\nResult: {session.EffectiveStatus(current)}"
                + (result?.Report is { } report ? $", report {report}" : "") + (result?.Note is { Length: > 0 } note ? $"\nNote: {note}" : "");
            if (_note.Text.Length == 0 && result?.Note is { Length: > 0 } saved) _note.Text = saved;
        }
        ResizePanel();
    }

    public void ClearNote() => _note.Text = "";

    public void ShowNotice(string notice) { _notice.Text = notice; ResizePanel(); }

    public void CloseView()
    {
        Hide();
        GetViewport().GuiReleaseFocus();
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(760, size.X - 32));
        _panel.Size = new(width, 0);
        _panel.Position = new((size.X - width) / 2, Math.Max(16, (size.Y - _panel.GetCombinedMinimumSize().Y) / 2));
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= ResizePanel;
        _panel.MinimumSizeChanged -= ResizePanel;
    }
}

/// <summary>ARC-DEV-2190: the read-only plate with the current owner check, drawn top left over the game; it takes no click and no key,
/// and draws no original art.</summary>
public partial class AreaOwnerQaPlate : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _label = null!;
    public bool IsShown => _panel.Visible;
    internal string TextForSelfTest => _label.Text;
    internal Rect2 RectForSelfTest => _panel.GetRect();
    internal Control.MouseFilterEnum MouseFilterForSelfTest => _panel.MouseFilter;

    public override void _Ready()
    {
        Layer = 8;
        _panel = new PanelContainer { Name = "OwnerQaPlate", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Position = new(8, 8) };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .85f),
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        _label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(.2f, .12f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) };
        _panel.AddChild(_label);
        AddChild(_panel);
    }

    /// <summary>Shows <paramref name="step"/> (id and step text) or hides the plate when null.</summary>
    public void Set(OwnerQaStep? step, bool visible)
    {
        _panel.Visible = visible && step is not null;
        _label.Text = step is null ? "" : $"Check {step.Id}: {step.Step}";
        _panel.Size = Vector2.Zero;
    }
}
