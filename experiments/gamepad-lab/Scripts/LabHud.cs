using Godot;
using Arcanum.Input;
using GamepadLab.Core;

namespace GamepadLab;

public enum ModalKind { None, Context, Settings, Slot, Assign, Hub, Battle, Follower, Bindings, BindingButton }

/// <summary>Responsive HUD: only the visible modal captures pointer input.</summary>
public partial class LabHud : Control
{
    private Label _banner = null!; private Label _diagnostics = null!; private Label _prompt = null!; private Label _log = null!;
    private PanelContainer _modal = null!; private VBoxContainer _modalRows = null!;
    private readonly List<Label> _choiceRows = [];
    private string[] _options = []; private int _selected;
    private string[] _labels = []; private string _title = "";
    private bool _selectionScrollPending;
    private int _selectionScrollPasses;
    private long _modalGeneration;
    private long _selectionScrollGeneration;
    private ulong _selectionScrollRow;
    public string SelectionScrollStatus { get; private set; } = "idle";
    private readonly List<(Label Label, int Size)> _fonts = [];
    private ScrollContainer _modalScroll = null!;
    private ScrollContainer _diagnosticScroll = null!; private ScrollContainer _logScroll = null!;
    private ScrollContainer _promptScroll = null!;
    public Rect2 ModalClip => _modalScroll.GetGlobalRect();
    public Rect2 UsableModalClip
    {
        get
        {
            var rect = ModalClip; var panel = _modalScroll.GetThemeStylebox("panel");
            var padding = (MarginContainer)_modalRows.GetParent();
            var start = new Vector2(padding.GetThemeConstant("margin_left"), padding.GetThemeConstant("margin_top"));
            var end = new Vector2(padding.GetThemeConstant("margin_right"), padding.GetThemeConstant("margin_bottom"));
            // A hidden native bar retains its last Size but occupies no content
            // width. Match the ScrollContainer's actual rendered allocation.
            var bar = _modalScroll.GetVScrollBar();
            var scrollbarWidth = bar.Visible ? bar.Size.X : 0;
            return new Rect2(rect.Position + new Vector2(panel.GetMargin(Side.Left), panel.GetMargin(Side.Top)) + start,
                rect.Size - panel.GetMinimumSize() - start - end - new Vector2(scrollbarWidth, 0));
        }
    }
    private float _uiScale = 1;
    private PresetState _preset = new();
    public int OptionCount => _options.Length;
    public Label ModalTitle => _modalRows.GetChild<Label>(0);
    public ScrollContainer ModalScroll => _modalScroll;
    public IReadOnlyList<Label> ChoiceRows => _choiceRows.AsReadOnly();
    public ModalKind ModalKind { get; private set; }
    public bool ModalVisible => ModalKind != ModalKind.None;
    public int SelectedIndex => _selected;
    public string SelectedOption => ModalVisible ? _options[_selected] : "";
    public string PromptText => _prompt.Text;
    public string DiagnosticsText => _diagnostics.Text;
    /// <summary>The reticle is drawn in this CanvasLayer, not in world space.</summary>
    public Vector2 ReticleCanvasCenter => GetGlobalTransformWithCanvas() * (Size * .5f);
    public IReadOnlyDictionary<string, Rect2> LayoutBounds => new Dictionary<string, Rect2>
    {
        ["banner"] = _banner.GetGlobalRect(), ["diagnostics"] = _diagnosticScroll.GetGlobalRect(),
        ["modal"] = _modal.GetGlobalRect(), ["log"] = _logScroll.GetGlobalRect(), ["prompt"] = _promptScroll.GetGlobalRect()
    };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var margins = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margins.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margins.AddThemeConstantOverride("margin_left", 16); margins.AddThemeConstantOverride("margin_top", 12);
        margins.AddThemeConstantOverride("margin_right", 16); margins.AddThemeConstantOverride("margin_bottom", 12); AddChild(margins);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 10); margins.AddChild(column);

        _banner = TextLabel("SYNTHETIC GAMEPAD INPUT LAB — NO ORIGINAL DATA", 20); _banner.AutowrapMode = TextServer.AutowrapMode.WordSmart; _banner.CustomMinimumSize = new Vector2(0, 30); column.AddChild(_banner);
        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 16); column.AddChild(body);
        var diagnosticPanel = new PanelContainer { CustomMinimumSize = new Vector2(360, 260), MouseFilter = MouseFilterEnum.Ignore };
        _diagnostics = TextLabel("Diagnostics pending", 16); _diagnostics.AutowrapMode = TextServer.AutowrapMode.WordSmart; _diagnosticScroll = Scrolled(_diagnostics); diagnosticPanel.AddChild(_diagnosticScroll); body.AddChild(diagnosticPanel);
        body.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _modal = new PanelContainer { CustomMinimumSize = new Vector2(360, 260), SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _modalRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        _modalRows.AddThemeConstantOverride("separation", 6); _modalScroll = Scrolled(_modalRows); _modal.AddChild(_modalScroll); body.AddChild(_modal);

        var logPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 130), MouseFilter = MouseFilterEnum.Ignore };
        _log = TextLabel("Synthetic log", 15); _log.AutowrapMode = TextServer.AutowrapMode.WordSmart; _logScroll = Scrolled(_log); logPanel.AddChild(_logScroll); column.AddChild(logPanel);
        _prompt = TextLabel("[A / Enter] Select a target", 18); _prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart; _promptScroll = Scrolled(_prompt); _promptScroll.CustomMinimumSize = new Vector2(0, 100); _promptScroll.SizeFlagsVertical = SizeFlags.Fill; column.AddChild(_promptScroll);
        RenderingServer.FramePostDraw += SettleSelectedScroll;
    }
    public override void _ExitTree() { RenderingServer.FramePostDraw -= SettleSelectedScroll; DisableD4Trace(); }

    public void UpdateView(string diagnostics, string prompt, IEnumerable<string> logs) { _diagnostics.Text = diagnostics; _prompt.Text = prompt; _log.Text = string.Join("\n", logs.TakeLast(8)); QueueRedraw(); }
    public void ShowContext(string primary) => ShowSurface(ModalKind.Context, "CONTEXTUAL ACTIONS", [primary, "Inspect", "Cancel"]);
    public void ShowSettings() => ShowSurface(ModalKind.Settings, "SETTINGS — apply only at this safe boundary", ["Camera deadzone −", "Camera deadzone +", "Actor deadzone −", "Actor deadzone +", "Camera sensitivity −", "Camera sensitivity +", "Actor sensitivity −", "Actor sensitivity +", "Toggle invert X", "Toggle invert Y", "Toggle comparison mode", "Recenter camera", "Reset defaults", "Close", "Preset Contextual", "Preset QuickSlots", "Preset Radial", "Create user copy", "Use user copy", "Reset user copy", "Edit bindings", "Swap shoulder roles", "UI scale"]);
    public void ShowSlots(LabSlots slots)
    {
        var ids = Enumerable.Range(1, 10).Select(n => $"slot-{n:00}").Append("Catalog").Append("Close").ToArray();
        ShowSurface(ModalKind.Context, "FULL TEN QUICK SLOTS — A use / assign, B back", ids,
            ids.Select(id => slots.Assignments.TryGetValue(id, out var a) ? $"{id} — {(a is null ? "EMPTY" : LabActionCatalog.Get(a)?.Label)}" : id).ToArray());
    }
    public void ShowSurface(ModalKind kind, string title, string[] ids, string[]? labels = null)
    { ModalKind = kind; _title = title; _options = ids; _labels = labels ?? ids; _selected = 0; RenderModal(); }
    public void Navigate(int direction) { if (ModalVisible && direction != 0) { _selected = (_selected + Math.Sign(direction) + _options.Length) % _options.Length; RenderModal(); } }
    public string Activate() => SelectedOption;
    public void SelectAt(Vector2 position)
    {
        if (!ModalVisible || !_modal.GetGlobalRect().HasPoint(position)) return;
        for (var index = 0; index < _choiceRows.Count; index++)
            if (_choiceRows[index].GetGlobalRect().HasPoint(position)) { _selected = index; RenderModal(); return; }
    }
    public void HideModal() { _selectionScrollPending = false; SelectionScrollStatus = "idle"; ModalKind = ModalKind.None; _modal.Visible = false; _options = []; ClearRows(); }
    public void SetScale(int percent)
    {
        if (percent is not (100 or 150 or 200)) throw new ArgumentException("unsupported UI scale");
        _uiScale = percent / 100f;
        foreach (var (label, size) in _fonts) label.AddThemeFontSizeOverride("font_size", (int)Math.Round(size * _uiScale));
    }
    public void SetPreset(PresetState state) { _preset = state; QueueRedraw(); }
    public bool HasReadableLayout(Rect2 viewport)
    {
        var bounds = LayoutBounds;
        return bounds.Values.All(rect => rect.Size.X >= 20 && rect.Size.Y >= 20 && viewport.Encloses(rect))
            && (!ModalVisible || !bounds["diagnostics"].Intersects(bounds["modal"]))
            && bounds["log"].Position.Y < bounds["prompt"].Position.Y;
    }
    public override void _Draw()
    {
        var center = Size * .5f;
        DrawCircle(center, 11, Colors.Transparent, false, 2);
        DrawLine(center - new Vector2(16, 0), center + new Vector2(16, 0), Colors.White);
        DrawLine(center - new Vector2(0, 16), center + new Vector2(0, 16), Colors.White);
        if (_preset.WheelOpen)
        {
            var radius = Math.Min(Size.X, Size.Y) * .23f;
            for (var i = 0; i < 10; i++)
            {
                var angle = -Mathf.Pi / 2 + i * Mathf.Tau / 10; var at = center + Vector2.FromAngle(angle) * radius;
                var selected = _preset.WheelSector == i;
                DrawCircle(at, 24 * _uiScale, selected ? Colors.DarkGoldenrod : Colors.DarkSlateGray);
                DrawString(ThemeDB.FallbackFont, at + new Vector2(-12, 7) * _uiScale, $"{i + 1:00}", HorizontalAlignment.Left, -1, (int)(18 * _uiScale), Colors.White);
            }
            DrawString(ThemeDB.FallbackFont, center + new Vector2(-140, 60), _preset.WheelSector is null ? "No selection — RS then A" : $"Latched {_preset.WheelSector + 1:00} — A / B", HorizontalAlignment.Left, -1, (int)(16 * _uiScale), Colors.White);
        }
    }
    private void RenderModal()
    {
        BeginD4Rebuild();
        ClearRows();
        var title = TextLabel(_title + "\n[A/Enter] choose  [B/Esc] cancel; A/B override QuickSlots", 16);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart; _modalRows.AddChild(title);
        for (var index = 0; index < _options.Length; index++)
        {
            var row = TextLabel($"{(index == _selected ? ">" : " ")} {_labels[index]}", 18);
            row.AutowrapMode = TextServer.AutowrapMode.WordSmart; row.CustomMinimumSize = new Vector2(0, 24 * _uiScale); _modalRows.AddChild(row); _choiceRows.Add(row);
        }
        _modal.Visible = true;
        ScrollSelected();
        ObserveD4Scroll("RenderModal.after", "production RenderModal");
    }
    public void ScrollSelected()
    {
        _selectionScrollPending = ModalVisible && _selected >= 0 && _selected < _choiceRows.Count;
        _selectionScrollPasses = 0; _selectionScrollGeneration = _modalGeneration;
        _selectionScrollRow = _selectionScrollPending ? _choiceRows[_selected].GetInstanceId() : 0;
        SelectionScrollStatus = _selectionScrollPending ? "pending" : "idle";
        ObserveD4Scroll("ScrollSelected.pending", "production ScrollSelected");
    }
    private void SettleSelectedScroll()
    {
        // Rebuilding rows temporarily invalidates container geometry/range. A
        // deferred call can still run before native sorting and scroll to stale
        // coordinates. Use the rendered allocation, with a finite settling budget.
        if (!IsInsideTree() || !_selectionScrollPending || !ModalVisible || _selected < 0 || _selected >= _choiceRows.Count
            || _selectionScrollGeneration != _modalGeneration) return;
        var row = _choiceRows[_selected];
        if (!GodotObject.IsInstanceValid(row) || !row.IsInsideTree() || row.GetInstanceId() != _selectionScrollRow) return;
        ObserveD4Scroll("FramePostDraw.enter", "production SettleSelectedScroll");
        var clip = UsableModalClip; var rect = row.GetGlobalRect();
        // At the first choice, retain the full title as well when both fit.
        // This uses native allocation, not guessed line heights or padding.
        var desired = _selected == 0 ? rect.Merge(ModalTitle.GetGlobalRect()) : rect;
        if (desired.Size.Y > clip.Size.Y) desired = rect;
        if (rect.Size.Y > clip.Size.Y || rect.Size.X > clip.Size.X || row.Size.Y < row.GetMinimumSize().Y)
        {
            SelectionScrollStatus = "unreadable"; _selectionScrollPending = false;
            ObserveD4Scroll("FramePostDraw.unreadable", "production SettleSelectedScroll"); return;
        }
        // A scroll write translates children on a later rendered frame. Only a
        // subsequent native readback may report readiness, never the postwrite rect.
        if (clip.Encloses(desired))
        {
            SelectionScrollStatus = "readable"; _selectionScrollPending = false;
            ObserveD4Scroll("FramePostDraw.readable", "production SettleSelectedScroll"); return;
        }
        if (_selectionScrollPasses >= 4)
        { SelectionScrollStatus = "budget-exhausted"; return; }
        _selectionScrollPasses++;
        var displacement = desired.Position.Y < clip.Position.Y ? desired.Position.Y - clip.Position.Y
            : desired.End.Y > clip.End.Y ? desired.End.Y - clip.End.Y : 0;
        var bar = _modalScroll.GetVScrollBar();
        var requested = Math.Clamp(bar.Value + displacement, bar.MinValue, Math.Max(bar.MinValue, bar.MaxValue - bar.Page));
        ObserveD4Scroll("PaddedReconcile.before", "production SettleSelectedScroll");
        _d4ScrollWriter = "production SettleSelectedScroll.PaddedReconcile";
        bar.Value = requested;
        _d4ScrollWriter = "outside production reconciliation; native allocation/input owner not inferred";
        ObserveD4Scroll("PaddedReconcile.after", "production SettleSelectedScroll");
    }
    private void ClearRows()
    {
        _modalGeneration++;
        foreach (var child in _modalRows.GetChildren()) { _modalRows.RemoveChild(child); if (child is Label label) _fonts.RemoveAll(p => p.Label == label); child.Free(); }
        _choiceRows.Clear();
    }
    private Label TextLabel(string text, int fontSize) { var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill }; label.AddThemeFontSizeOverride("font_size", (int)(fontSize * _uiScale)); _fonts.Add((label, fontSize)); return label; }
    private static ScrollContainer Scrolled(Control child)
    { var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; scroll.AddChild(Padded(child)); return scroll; }
    private static MarginContainer Padded(Control child)
    {
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 10); margin.AddThemeConstantOverride("margin_top", 10); margin.AddThemeConstantOverride("margin_right", 10); margin.AddThemeConstantOverride("margin_bottom", 10); margin.AddChild(child); return margin;
    }
}
