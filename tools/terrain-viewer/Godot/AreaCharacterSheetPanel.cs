using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-007 diagnostic character sheet: the session hero's values as the original sheet shows them
/// (<see cref="AreaCharacterSheet.Lines"/>), with a close button. ARC-UI-009 adds the skills page: the sixteen skills in the
/// original's four groups, each row with − and + buttons (<see cref="SkillRefundRequested"/>, <see cref="SkillSpendRequested"/>)
/// and a hover hint (the description and the key stat the next point needs). ARC-MAGIC-008 adds the spells page: the sixteen
/// college tabs, the shown college's five spells (name, state and minimum level, the original's hover hint) and the college's − and
/// + (<see cref="SpellUnlearnRequested"/>, <see cref="SpellLearnRequested"/>). The two pages are tabs. The app rebuilds it whenever
/// the session changes. ARC-UI-034 adds the stats page (hit points, fatigue and the eight primary stats, each with − and +,
/// <see cref="StatRefundRequested"/>, <see cref="StatSpendRequested"/>) and the creation mode of the character editor (window 14):
/// Accept and Cancel instead of Close, over the main menu.
/// </summary>
public partial class AreaCharacterSheetPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _body = null!;
    private Label _hint = null!;
    private Button _close = null!;
    private AreaCharacterSheetView? _view;
    private AreaSkillsPageView? _skillsView;
    private Label _skillMessage = null!;
    private readonly List<(Button Minus, Label Text, Button Plus)> _skillRows = [];
    private readonly List<(Button Minus, Label Text, Button Plus)> _statRows = [];
    private AreaStatsPageView? _statsView;
    private readonly bool _creation;
    private Button? _accept;
    private AreaSpellsPageView? _spellsView;
    private TabContainer _pages = null!;
    private readonly List<Button> _collegeTabs = [];
    private Label _collegeLine = null!;
    private readonly List<Label> _spellRows = [];
    private Button _spellMinus = null!, _spellPlus = null!;
    /// <summary>Close, or in creation mode Cancel.</summary>
    public event Action? CloseRequested;
    /// <summary>ARC-UI-034: the creation mode's Accept.</summary>
    public event Action? AcceptRequested;
    /// <summary>ARC-UI-034: + or − of a stats row (0 hit points, 1 fatigue, 2..9 the stats).</summary>
    public event Action<int>? StatSpendRequested;
    public event Action<int>? StatRefundRequested;
    public event Action<int>? SkillSpendRequested;
    public event Action<int>? SkillRefundRequested;
    /// <summary>A college tab was pressed (0..15).</summary>
    public event Action<int>? CollegeSelected;
    /// <summary>+ or − of the shown college (its number).</summary>
    public event Action<int>? SpellLearnRequested;
    public event Action<int>? SpellUnlearnRequested;
    public bool IsOpen => Visible;
    internal AreaCharacterSheetView? ViewForSelfTest => _view;
    internal string TextForSelfTest => _body.Text;
    internal AreaSkillsPageView? SkillsForSelfTest => _skillsView;
    internal Button SkillPlusForSelfTest(int row) => _skillRows[row].Plus;
    internal Button SkillMinusForSelfTest(int row) => _skillRows[row].Minus;
    internal string SkillTextForSelfTest(int row) => _skillRows[row].Text.Text;
    internal string SkillHintForSelfTest(int row) => _skillRows[row].Text.TooltipText;
    internal string SkillMessageForSelfTest => _skillMessage.Text;
    internal AreaSpellsPageView? SpellsForSelfTest => _spellsView;
    internal Button CollegeTabForSelfTest(int college) => _collegeTabs[college];
    internal Button SpellPlusForSelfTest => _spellPlus;
    internal Button SpellMinusForSelfTest => _spellMinus;
    internal string SpellTextForSelfTest(int rank) => _spellRows[rank].Text;
    internal string SpellHintForSelfTest(int rank) => _spellRows[rank].TooltipText;
    internal string CollegeHintForSelfTest(int college) => _collegeTabs[college].TooltipText;
    internal AreaStatsPageView? StatsForSelfTest => _statsView;
    internal Button StatPlusForSelfTest(int row) => _statRows[row].Plus;
    internal Button StatMinusForSelfTest(int row) => _statRows[row].Minus;
    internal string StatTextForSelfTest(int row) => _statRows[row].Text.Text;
    internal Button? AcceptForSelfTest => _accept;
    internal Button CloseForSelfTest => _close;

    public AreaCharacterSheetPanel() { }

    /// <param name="creation">ARC-UI-034: the character editor in creation mode (Accept and Cancel, above the main menu).</param>
    public AreaCharacterSheetPanel(bool creation) => _creation = creation;

    public override void _Ready()
    {
        // ARC-UI-034: the creation editor sits above the main menu's backdrop (layer 4).
        Layer = _creation ? 5 : 4;
        _panel = new PanelContainer { Name = _creation ? "AreaCharacterEditor" : "AreaCharacterSheet", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        var header = new Label { Text = _creation ? "Character editor (new character)" : "Character sheet", Modulate = new Color(1f, .85f, .55f) };
        _body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pages = new TabContainer { Name = "Pages", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pages.AddChild(BuildStatsPage());
        var skills = new GridContainer { Name = "Skills", Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        for (var group = 0; group < AreaSkillsPage.Rows / AreaSkillsPage.RowsPerGroup; group++)
        {
            var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            column.AddChild(new Label { Text = AreaSkillsPage.GroupLabels[group], Modulate = new Color(1f, .85f, .55f) });
            for (var i = 0; i < AreaSkillsPage.RowsPerGroup; i++)
            {
                var row = group * AreaSkillsPage.RowsPerGroup + i;
                var line = new HBoxContainer();
                var minus = new Button { Text = "−", FocusMode = Control.FocusModeEnum.All, TooltipText = "refund (down to the value when the sheet opened)" };
                var plus = new Button { Text = "+", FocusMode = Control.FocusModeEnum.All, TooltipText = "spend one unspent point" };
                var text = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart };
                minus.Pressed += () => SkillRefundRequested?.Invoke(row);
                plus.Pressed += () => SkillSpendRequested?.Invoke(row);
                line.AddChild(minus); line.AddChild(text); line.AddChild(plus);
                column.AddChild(line);
                _skillRows.Add((minus, text, plus));
            }
            skills.AddChild(column);
        }
        _pages.AddChild(skills);
        _pages.AddChild(BuildSpellsPage());
        _skillMessage = new Label { Modulate = new Color(.85f, .9f, .95f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _close = new Button { Name = "Close", Text = _creation ? "Cancel" : "Close", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _close.Pressed += () => CloseRequested?.Invoke();
        _hint = new Label
        {
            Modulate = new Color(.55f, .6f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = _creation
                ? "Return accepts, Escape cancels (back to the new-character window, which starts a new character) | PROJECT diagnostic window: stat, skill and spell points, no tech discipline buttons; the opening shop is not built; original UI parity NOT_VERIFIED"
                : "C, Escape or cancel closes | PROJECT diagnostic window: stat, skill and spell points (no tech discipline buttons); original UI parity NOT_VERIFIED",
        };
        var buttons = new HBoxContainer();
        if (_creation)
        {
            // The original's window 14: Cancel on the left, Accept on the right (CM2).
            _accept = new Button { Name = "Accept", Text = "Accept", FocusMode = Control.FocusModeEnum.All };
            _accept.Pressed += () => AcceptRequested?.Invoke();
            buttons.AddChild(_close); buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); buttons.AddChild(_accept);
        }
        else buttons.AddChild(_close);
        foreach (var control in new Control[] { header, _body, _pages, _skillMessage, buttons, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    private VBoxContainer BuildStatsPage()
    {
        var page = new VBoxContainer { Name = "Stats", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        for (var row = 0; row < AreaStatsPage.Rows; row++)
        {
            var r = row;
            var line = new HBoxContainer();
            var minus = new Button { Text = "−", FocusMode = Control.FocusModeEnum.All, TooltipText = "refund (down to the value when the sheet opened)" };
            var plus = new Button { Text = "+", FocusMode = Control.FocusModeEnum.All, TooltipText = "spend unspent points" };
            var text = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
            minus.Pressed += () => StatRefundRequested?.Invoke(r);
            plus.Pressed += () => StatSpendRequested?.Invoke(r);
            line.AddChild(minus); line.AddChild(text); line.AddChild(plus);
            page.AddChild(line);
            _statRows.Add((minus, text, plus));
        }
        return page;
    }

    private VBoxContainer BuildSpellsPage()
    {
        var page = new VBoxContainer { Name = "Spells", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        // The sixteen college tabs, one shown at a time (the original's radio group).
        var tabs = new GridContainer { Columns = 8 };
        var group = new ButtonGroup();
        for (var college = 0; college < AreaSpellsPage.Colleges; college++)
        {
            var c = college;
            var tab = new Button { ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.All, Text = $"{college + 1}" };
            tab.Pressed += () => CollegeSelected?.Invoke(c);
            tabs.AddChild(tab); _collegeTabs.Add(tab);
        }
        page.AddChild(tabs);
        _collegeLine = new Label { Modulate = new Color(1f, .85f, .55f), MouseFilter = Control.MouseFilterEnum.Pass };
        page.AddChild(_collegeLine);
        for (var rank = 0; rank < AreaSpellsPage.Ranks; rank++)
        {
            var row = new Label { MouseFilter = Control.MouseFilterEnum.Pass, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            page.AddChild(row); _spellRows.Add(row);
        }
        var buttons = new HBoxContainer();
        _spellMinus = new Button { Text = "− unlearn", FocusMode = Control.FocusModeEnum.All };
        _spellPlus = new Button { Text = "+ learn", FocusMode = Control.FocusModeEnum.All };
        _spellMinus.Pressed += () => { if (_spellsView is { } view) SpellUnlearnRequested?.Invoke(view.College); };
        _spellPlus.Pressed += () => { if (_spellsView is { } view) SpellLearnRequested?.Invoke(view.College); };
        buttons.AddChild(_spellMinus); buttons.AddChild(_spellPlus);
        page.AddChild(buttons);
        return page;
    }

    public void Open(AreaCharacterSheetView view, AreaSkillsPageView? skills = null, AreaSpellsPageView? spells = null, AreaStatsPageView? stats = null)
    {
        Show(); Render(view, skills, spells, stats);
        (_accept ?? _close).GrabFocus();
    }

    public void CloseView()
    {
        _view = null; _skillsView = null; _spellsView = null; _statsView = null; _skillMessage.Text = string.Empty; Hide();
        GetViewport().GuiReleaseFocus();
    }

    public void Render(AreaCharacterSheetView view, AreaSkillsPageView? skills = null, AreaSpellsPageView? spells = null, AreaStatsPageView? stats = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view; _skillsView = skills; _spellsView = spells; _statsView = stats;
        _body.Text = string.Join("\n", AreaCharacterSheet.Lines(view));
        for (var row = 0; row < _statRows.Count; row++)
        {
            var (minus, text, plus) = _statRows[row];
            var stat = stats?.Rows[row];
            text.Text = stat is null ? "" : AreaStatsPage.Line(stat);
            minus.Disabled = plus.Disabled = stats is not { EditorOpen: true };
        }
        for (var row = 0; row < _skillRows.Count; row++)
        {
            var (minus, text, plus) = _skillRows[row];
            var skill = skills?.Rows[row];
            text.Text = skill is null ? "" : AreaSkillsPage.Line(skill);
            // The original's hover hint: the name, the description and the key stat the next point needs.
            text.TooltipText = skill is null ? "" : string.Join("\n", new[] { skill.Name, skill.Description,
                skill.NextPointKeyStat == 0 ? null : $"{skill.KeyStat}: {skill.NextPointKeyStat}" }.Where(part => !string.IsNullOrEmpty(part)));
            minus.Disabled = plus.Disabled = skills is not { EditorOpen: true };
        }
        RenderSpells(spells);
        ResizePanel();
    }

    private void RenderSpells(AreaSpellsPageView? spells)
    {
        for (var college = 0; college < _collegeTabs.Count; college++)
        {
            var tab = spells?.Colleges[college];
            _collegeTabs[college].Text = tab?.Name ?? $"{college + 1}";
            _collegeTabs[college].TooltipText = tab is null ? "" : AreaSpellsPage.Hint(tab);
            _collegeTabs[college].SetPressedNoSignal(spells?.College == college);
        }
        var shown = spells?.Colleges[spells.College];
        _collegeLine.Text = shown is null ? "" : $"{shown.Name}: {shown.Rank} of {AreaSpellsPage.Ranks} known" + (shown.Mastered ? " (mastered)" : "");
        _collegeLine.TooltipText = shown is null ? "" : AreaSpellsPage.Hint(shown);
        for (var rank = 0; rank < _spellRows.Count; rank++)
        {
            var row = spells?.Spells[rank];
            _spellRows[rank].Text = row is null ? "" : AreaSpellsPage.Line(row);
            _spellRows[rank].TooltipText = row is null ? "" : AreaSpellsPage.Hint(row);
            // The original's look (SG4): the known ranks white, the next one gold, the ranks above it grey.
            _spellRows[rank].Modulate = row?.State switch
            {
                AreaSpellState.Known => new Color(1f, 1f, 1f), AreaSpellState.Next => new Color(1f, 210 / 255f, 0f), _ => new Color(.5f, .5f, .5f),
            };
        }
        // + over the next rank and − over the highest known show those spells' hints, as the original's buttons do.
        _spellPlus.Disabled = spells is not { CanLearn: true };
        _spellMinus.Disabled = spells is not { CanUnlearn: true };
        _spellPlus.TooltipText = spells?.NextSpell is { } next ? AreaSpellsPage.Hint(next) : "";
        _spellMinus.TooltipText = spells?.HighestSpell is { } highest ? AreaSpellsPage.Hint(highest) : "";
    }

    /// <summary>ARC-UI-034: the creation mode's controller focus: 0 Accept, 1 Cancel (highlighted as the menu's rows are).</summary>
    public void ShowCreationFocus(int focus)
    {
        if (_accept is null) return;
        _accept.Modulate = focus == 0 ? new Color(1f, .85f, .6f) : Colors.White;
        _close.Modulate = focus == 1 ? new Color(1f, .85f, .6f) : Colors.White;
    }

    /// <summary>The message of the last + or − press (empty clears it).</summary>
    public void ShowSkillMessage(string message) => _skillMessage.Text = message;

    /// <summary>Confirm presses the close button (the pages' buttons take the mouse or focus).</summary>
    public void Confirm() => _close.EmitSignal(BaseButton.SignalName.Pressed);

    private void ResizePanel()
    {
        if (_panel is null) return;
        // ARC-UI-018: inside the part the interface bars leave free.
        var (top, size) = AreaGameBarPanel.FreeArea(GetViewport().GetVisibleRect().Size);
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, top + Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
