using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-TECHNOLOGY-005 diagnostic schematic window (<see cref="AreaSchematics"/>): the learned and found tabs, the eight
/// discipline tabs, the previous and next page, the record's name and description, the two components and the product, the
/// assemble button and its message. A plain layout instead of the original's window art (interface art 365 and its buttons,
/// not drawn here). The app rebuilds it whenever the session, the tab, the discipline or the page changes.
/// ARC-UI-142: when the window's art reads, the original's window (<see cref="AreaSchematicWindowView"/>, with the round view's lens) replaces this
/// layout; the diagnostic layout stays as the fallback and for the synthetic self-tests.
/// </summary>
public partial class AreaSchematicsPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private RichTextLabel _body = null!;
    private Label _message = null!;
    private Button _learned = null!, _found = null!, _previous = null!, _next = null!, _assemble = null!, _close = null!;
    private readonly Button[] _disciplines = new Button[AreaSchematics.Disciplines];
    private readonly ButtonGroup _tabs = new() { AllowUnpress = false };
    private readonly ButtonGroup _disciplineGroup = new() { AllowUnpress = false };
    private AreaSchematicsView? _view;
    private Func<int, string>? _names;
    private AreaSchematicWindowView _artView = null!;
    private AreaRoundView _roundView = null!;

    public event Action? CloseRequested;
    public event Action<AreaSchematicTab>? TabRequested;
    public event Action<int>? DisciplineRequested;
    public event Action<int>? PageRequested;
    public event Action? AssembleRequested;
    /// <summary>ARC-UI-142: an interface sound of the art look (3000 press, 3001 release) and the text bar entry of the part under the pointer.</summary>
    public event Action<int>? SoundRequested;
    public event Action<AreaMessageEntry?>? HoverEntryRequested;
    /// <summary>ARC-UI-142: the round view's holder (the foot point and the zoom of the game view around the hero), or null for none.</summary>
    public Func<(Vector2 Foot, float Zoom)?>? HolderView { get; set; }
    /// <summary>ARC-UI-142: whether the window shows in the original's look (its art read).</summary>
    public bool ArtMode => !DiagnosticLook && _artView.ArtSet;
    /// <summary>PROJECT: shows the diagnostic layout although the art has read (the smoke's before-and-after capture); set before the next render.</summary>
    public bool DiagnosticLook { get; set; }
    internal AreaSchematicWindowView ArtViewForSelfTest => _artView;
    internal bool RoundViewShown => _roundView.Visible;
    public AreaSchematicWindowView ArtView => _artView;
    public bool IsOpen => Visible;
    public AreaSchematicsView? View => _view;
    internal AreaSchematicsView? ViewForSelfTest => _view;
    internal string TextForSelfTest => _body.GetParsedText();
    internal string MessageForSelfTest => _message.Text;

    public override void _Ready()
    {
        Layer = 4;
        _artView = new AreaSchematicWindowView(); AddChild(_artView);
        _artView.TabRequested += tab => TabRequested?.Invoke(tab);
        _artView.DisciplineRequested += discipline => DisciplineRequested?.Invoke(discipline);
        _artView.PageRequested += by => PageRequested?.Invoke(by);
        _artView.AssembleRequested += () => AssembleRequested?.Invoke();
        _artView.CloseRequested += () => CloseRequested?.Invoke();
        _artView.SoundRequested += id => SoundRequested?.Invoke(id);
        _artView.HoverEntryRequested += entry => HoverEntryRequested?.Invoke(entry);
        _roundView = new AreaRoundView(); AddChild(_roundView);
        _panel = new PanelContainer { Name = "AreaSchematics", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(214 / 255f, 205 / 255f, 186 / 255f, .97f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _header = new Label { Text = "Schematics", Modulate = new Color(.15f, .1f, .05f) };
        var tabs = new HBoxContainer();
        _learned = Toggle("Learned", _tabs, () => TabRequested?.Invoke(AreaSchematicTab.Learned));
        _found = Toggle("Found", _tabs, () => TabRequested?.Invoke(AreaSchematicTab.Found));
        _previous = Plain("Previous", () => PageRequested?.Invoke(-1));
        _next = Plain("Next", () => PageRequested?.Invoke(1));
        _close = Plain("Close", () => CloseRequested?.Invoke());
        foreach (var button in new[] { _learned, _found, _previous, _next, _close }) tabs.AddChild(button);
        var disciplines = new HBoxContainer();
        for (var d = 0; d < _disciplines.Length; d++)
        {
            var index = d;
            _disciplines[d] = Toggle($"D{d}", _disciplineGroup, () => DisciplineRequested?.Invoke(index));
            disciplines.AddChild(_disciplines[d]);
        }
        _body = new RichTextLabel
        {
            BbcodeEnabled = true, ScrollActive = true, FitContent = false, SelectionEnabled = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 300),
        };
        _body.AddThemeColorOverride("default_color", Colors.Black);
        _assemble = Plain("Assemble", () => AssembleRequested?.Invoke());
        _message = new Label { Modulate = new Color(.35f, .05f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        var hint = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "T, Escape or cancel closes; Tab or confirm assembles | PROJECT diagnostic window: the original's window art, slot pictures " +
                "and discipline icons are not drawn; texts are the data's; layout parity NOT_VERIFIED",
        };
        foreach (var control in new Control[] { _header, tabs, disciplines, _body, _assemble, _message, hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        AreaGameBarPanel.InsetsChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private static Button Plain(string text, Action pressed)
    {
        var button = new Button { Name = text, Text = text, FocusMode = Control.FocusModeEnum.All };
        button.Pressed += pressed;
        return button;
    }

    private static Button Toggle(string text, ButtonGroup group, Action pressed)
    {
        var button = new Button { Name = text, Text = text, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.All };
        button.Pressed += pressed;
        return button;
    }

    public void Open(AreaSchematicsView view, Func<int, string> names)
    {
        _names = names;
        Show(); Render(view, null);
        if (!ArtMode) _close.GrabFocus();
    }

    public void CloseView()
    {
        Hide(); _view = null; _message.Text = ""; _artView.CloseView(); _roundView.Visible = false;
        GetViewport().GuiReleaseFocus();
    }

    /// <summary>Renders a page; <paramref name="message"/> replaces the message line (null keeps it empty).</summary>
    public void Render(AreaSchematicsView view, string? message)
    {
        _view = view;
        _body.Text = Body(view);
        if (ArtMode)
        {
            _panel.Visible = false;
            _artView.Render(view);
            UpdateRoundView();
            return;
        }
        _panel.Visible = true; _artView.Visible = false; UpdateRoundView();
        _learned.SetPressedNoSignal(view.Tab == AreaSchematicTab.Learned);
        _found.SetPressedNoSignal(view.Tab == AreaSchematicTab.Found);
        for (var d = 0; d < _disciplines.Length; d++) _disciplines[d].SetPressedNoSignal(d == view.Discipline);
        // The original disables Previous on the first page and Next on the last.
        _previous.Disabled = view.Page <= 0;
        _next.Disabled = view.Page >= view.Pages - 1;
        _header.Text = $"Schematics | {AreaSchematics.TabName(view.Tab)} | {view.DisciplineName} (expertise {view.Expertise}) | " +
            (view.Pages == 0 ? "no schematic" : $"page {view.Page + 1}/{view.Pages}") + $" | record {view.Record}";
        _assemble.Text = view.Button switch
        {
            AssemblyButton.CanAssemble => "Assemble",
            AssemblyButton.ItemMissing => "Assemble (an item is missing)",
            _ => "Assemble (expertise lacking)",
        };
        _assemble.TooltipText = view.HoverText;
        _message.Text = message ?? "";
    }

    private string Body(AreaSchematicsView view)
    {
        string Name(AreaSchematicSlot slot) => Escape(_names?.Invoke(slot.Description) ?? $"prototype {slot.Prototype}");
        string Component(string label, AreaSchematicSlot? slot) => slot is null ? "" :
            $"\n{label}: {Name(slot)} — {(slot.Found ? "[color=#006000]in your inventory[/color]" : "[color=#900000]missing[/color]")}" +
            (slot.ExpertiseOk ? "" : $" — [color=#900000]needs expertise {-slot.Complexity} in discipline {slot.Discipline}[/color]");
        var text = $"[b]{Escape(view.Name)}[/b]\n{Escape(view.Description)}\n";
        if (view.Note is { } note) text += $"\n[i]{Escape(note)}[/i]";
        text += Component("Component 1", view.Component1) + Component("Component 2", view.Component2);
        if (view.Product is { } product) text += $"\nProduct: {Name(product)} × {view.Quantity}";
        return text;
    }

    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    /// <summary>Controller confirm or Tab: the assemble button.</summary>
    public void Confirm() => AssembleRequested?.Invoke();

    // ARC-UI-018: the bar panel's inset event is static; a freed panel must leave it.
    public override void _ExitTree() => AreaGameBarPanel.InsetsChanged -= ResizePanel;

    /// <summary>ARC-UI-2071 (ledger DH10, SW12): the lone left release after the reply that opened the window (screen point); only the original look takes it (its lens closes the window).
    /// True when the window took it into account.</summary>
    public bool LoneRelease(Vector2 screen)
    {
        if (!IsOpen || !ArtMode) return false;
        _artView.LoneRelease(screen);
        return true;
    }

    /// <summary>ARC-UI-142: the lens shows the game view around the hero while the original look is up (the view-window mode 1 of 0x550DA0).</summary>
    private void UpdateRoundView()
    {
        var holder = IsOpen && ArtMode && _view is not null ? HolderView?.Invoke() : null;
        var (origin, side) = _artView.LensOnScreen;
        _roundView.Update(origin, side, holder);
        _artView.LensCut = holder is not null;
    }

    public override void _Process(double delta) { if (IsOpen && ArtMode) { _artView.Place(); UpdateRoundView(); } }

    private void ResizePanel()
    {
        if (_panel is null) return;
        // ARC-UI-018: inside the part the interface bars leave free.
        var (top, size) = AreaGameBarPanel.FreeArea(GetViewport().GetVisibleRect().Size);
        _panel.Position = new Vector2(size.X * .1f, top + size.Y * .08f);
        _panel.Size = new Vector2(size.X * .8f, size.Y * .8f);
    }
}
