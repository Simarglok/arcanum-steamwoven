using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    private readonly List<(Button Minus, Label Text, Button Plus)> _techRows = [];
    private AreaStatsPageView? _statsView;
    private readonly bool _creation;
    private Button? _accept;
    private AreaSpellsPageView? _spellsView;
    private TabContainer _pages = null!;
    private readonly List<Button> _collegeTabs = [];
    private Label _collegeLine = null!;
    private readonly List<Label> _spellRows = [];
    private Button _spellMinus = null!, _spellPlus = null!;
    private AreaCharacterSheetArtView? _artView;
    private ScrollContainer? _host;
    private Label? _notBuilt;
    private Texture2D? _portrait;
    /// <summary>Close, or in creation mode Cancel.</summary>
    public event Action? CloseRequested;
    /// <summary>ARC-UI-075: the original look asks the text bar for a pointer text (null: the pointer left).</summary>
    public event Action<string?>? HoverTextRequested;
    /// <summary>ARC-UI-075: the original look shows a refusal in the text bar, not in the window.</summary>
    public event Action<string>? MessageRequested;
    /// <summary>ARC-UI-076: the pages in the original's look ask the text bar for an entry (null: the pointer left).</summary>
    public event Action<AreaMessageEntry?>? HoverEntryRequested;
    /// <summary>ARC-UI-076: + or − of a tech discipline (0..7).</summary>
    public event Action<int>? TechSpendRequested;
    public event Action<int>? TechRefundRequested;
    private bool _pagesReady, _spellsReady, _schemesReady;
    private AreaSchemesPageView? _schemesView;
    /// <summary>ARC-UI-1742: a row of the scheme page released over itself (the scheme's number).</summary>
    public event Action<int>? SchemeChosen;
    /// <summary>ARC-UI-077: the text bar entry of a spell 0..79 for the spells page's hover (the app builds it).</summary>
    public Func<int, AreaMessageEntry?>? SpellEntry { get => _artView?.Spells.SpellEntry; set { if (_artView is not null) _artView.Spells.SpellEntry = value; } }
    /// <summary>ARC-UI-075: an interface sound of the art look (3000 / 3001).</summary>
    public event Action<int>? SoundRequested;
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

    // ARC-UI-2694: the lens's round view of the game, on a layer under the window (the component the inventory, barter and logbook windows share).
    private AreaRoundView? _roundView;

    /// <summary>The lens's holder, the hero: its foot point in canvas units and the game view's zoom, or null for none (no lens view).</summary>
    public Func<(Vector2 Foot, float Zoom)?>? HolderView { get; set; }

    /// <summary>The lens's round view is shown under the window.</summary>
    internal bool RoundViewShown => _roundView?.Visible == true;

    /// <summary>The lens shows the game view around the hero while the original look is up (the sheet's open turns the view-window mode 1 on, art 198).</summary>
    private void UpdateRoundView()
    {
        if (_artView is null || _roundView is null) return;
        var holder = IsOpen && ArtMode && !_creation ? HolderView?.Invoke() : null;
        var (origin, side) = _artView.LensOnScreen;
        _roundView.Update(origin, side, holder);
        _artView.LensCut = holder is not null;
    }
    /// <summary>ARC-UI-088: the NPC sheet of a dialog (the original's mode 2): no + or − button on any page, the next spell grey. Set before the open.</summary>
    public bool ReadOnly
    {
        get => _artView?.ReadOnly ?? false;
        set { if (_artView is not null) _artView.ReadOnly = _artView.Pages.ReadOnly = _artView.Spells.ReadOnly = _artView.Schemes.ReadOnly = value; }
    }
    /// <summary>ARC-UI-075: whether the main page shows in the original's look (its art read). ARC-UI-147: the creation editor too.</summary>
    internal bool PagesReadyForSelfTest => _pagesReady;
    internal bool SpellsReadyForSelfTest => _spellsReady;
    internal bool SchemesReadyForSelfTest => _schemesReady;
    public bool ArtMode => _artView?.ArtSet == true;

    /// <summary>ARC-UI-2071 (ledger DH10): the lone left release after the reply that opened the sheet (screen point); only the original look takes it (a release in its close
    /// rectangle closes the sheet). True when the sheet took it into account.</summary>
    public bool LoneRelease(Vector2 screen)
    {
        if (!IsOpen || !ArtMode || _artView is null) return false;
        _artView.LoneRelease(screen);
        return true;
    }
    internal AreaCharacterSheetArtView? ArtViewForSelfTest => _artView;
    /// <summary>ARC-UI-3035: the smoke shows the page of a tab as a click on the tab does.</summary>
    internal void SelectTabForSelfTest(AreaSheetTab tab) { _artView?.SelectTab(tab); ApplyTab(tab); }
    /// <summary>ARC-UI-147: the diagnostic panel is drawn (the editor's art look hides it).</summary>
    internal bool PanelVisibleForSelfTest => _panel.Visible;
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
    internal Button TechPlusForSelfTest(int discipline) => _techRows[discipline].Plus;
    internal Button TechMinusForSelfTest(int discipline) => _techRows[discipline].Minus;
    internal string TechTextForSelfTest(int discipline) => _techRows[discipline].Text.Text;
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
        BuildArtView();
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
        _pages.AddChild(BuildTechPage());
        _skillMessage = new Label { Modulate = new Color(.85f, .9f, .95f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _close = new Button { Name = "Close", Text = _creation ? "Cancel" : "Close", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _close.Pressed += () => CloseRequested?.Invoke();
        _hint = new Label
        {
            Modulate = new Color(.55f, .6f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = _creation
                ? "Return accepts (the opening shop), Escape cancels (back to the new-character window, which starts a new character) | PROJECT diagnostic window: stat, skill, spell and tech degree points; original UI parity NOT_VERIFIED"
                : "C, Escape or cancel closes | PROJECT diagnostic window: stat, skill, spell and tech degree points; original UI parity NOT_VERIFIED",
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

    /// <summary>ARC-MAGIC-043: the diagnostic tech page, the original's degree page of the same editor (CM8): one row for each of the eight
    /// disciplines with its − and +; the − is refused below the snapshot the editor opened on (TC10).</summary>
    private VBoxContainer BuildTechPage()
    {
        var page = new VBoxContainer { Name = "Tech", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        for (var discipline = 0; discipline < AreaTechPage.Labels.Count; discipline++)
        {
            var d = discipline;
            var line = new HBoxContainer();
            var minus = new Button { Text = "−", FocusMode = Control.FocusModeEnum.All, TooltipText = "give a degree back (down to the value when the editor opened)" };
            var plus = new Button { Text = "+", FocusMode = Control.FocusModeEnum.All, TooltipText = "buy the next degree for one unspent point (needs the intelligence)" };
            var text = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
            minus.Pressed += () => TechRefundRequested?.Invoke(d);
            plus.Pressed += () => TechSpendRequested?.Invoke(d);
            line.AddChild(minus); line.AddChild(text); line.AddChild(plus);
            page.AddChild(line);
            _techRows.Add((minus, text, plus));
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

    public void Open(AreaCharacterSheetView view, AreaSkillsPageView? skills = null, AreaSpellsPageView? spells = null, AreaStatsPageView? stats = null,
        AreaTechPageView? tech = null, AreaSchemesPageView? schemes = null)
    {
        Show(); Render(view, skills, spells, stats, tech, schemes);
        if (ArtMode) return;
        (_accept ?? _close).GrabFocus();
    }

    public void CloseView()
    {
        _view = null; _skillsView = null; _spellsView = null; _statsView = null; _schemesView = null; _skillMessage.Text = string.Empty; Hide();
        if (_artView is { RegionForSelfTest: not null }) HoverTextRequested?.Invoke(null);
        _artView?.CloseView();
        _roundView?.Update(Vector2.Zero, 0, null);
        // The sheet always opens on the skills page (CHW7).
        _artView?.Pages.CloseView();
        _artView?.Spells.CloseView();
        _artView?.Schemes.CloseView();
        ApplyTab(AreaSheetTab.Skills);
        GetViewport().GuiReleaseFocus();
    }

    public void Render(AreaCharacterSheetView view, AreaSkillsPageView? skills = null, AreaSpellsPageView? spells = null, AreaStatsPageView? stats = null,
        AreaTechPageView? tech = null, AreaSchemesPageView? schemes = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view; _skillsView = skills; _spellsView = spells; _statsView = stats; _schemesView = schemes;
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
        for (var row = 0; row < _techRows.Count; row++)
        {
            var (minus, text, plus) = _techRows[row];
            var discipline = tech?.Rows.FirstOrDefault(r => r.Discipline == row);
            text.Text = discipline is null ? "" : AreaTechPage.Line(discipline);
            minus.Disabled = plus.Disabled = tech is not { EditorOpen: true };
        }
        RenderSpells(spells);
        RenderArt(view, skills, tech);
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

    /// <summary>ARC-UI-075: gives the sheet the original's art, fonts and texts, and the portrait's texture (null: the slot stays empty).</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaCharacterSheetFonts fonts, AreaCharacterSheetTexts texts, (int X, int Y)[] alignmentHot,
        bool pages = false, bool spells = false, bool schemes = false)
    {
        _artView!.SetArt(art, fonts, texts, alignmentHot);
        // ARC-UI-076: the skills and tech pages in the original's look when their arts read.
        if (pages) _artView.Pages.SetArt(art, fonts, texts);
        // ARC-UI-077: the spells page in the original's look when its arts read too.
        if (spells) _artView.Spells.SetArt(art, fonts, texts);
        // ARC-UI-1742: the scheme page in the original's look when its arts read too (the creation editor shows it as well, ARC-UI-1792).
        if (schemes) _artView.Schemes.SetArt(art, fonts);
        _pagesReady = pages;
        _spellsReady = pages && spells;
        _schemesReady = pages && schemes && texts.HasSchemeNames;
        // The diagnostic pages stay in the host for a game folder whose pages art does not read.
        if (_pages.GetParent() != _host) { _pages.Reparent(_host!); _pages.SetTabHidden(0, true); _pages.CurrentTab = 1; _pages.TabsVisible = false; _pages.CustomMinimumSize = new(300, 0); }
        ApplyTab(_artView.Tab);
        _panel.Visible = false;
    }

    public void SetPortrait(Texture2D? portrait) => _portrait = portrait;

    private void BuildArtView()
    {
        _artView = new AreaCharacterSheetArtView { MenuScreen = _creation };
        AddChild(_artView);
        if (!_creation) { _roundView = new AreaRoundView(); AddChild(_roundView); }
        var (px, py, pw, ph) = AreaCharacterSheetArtLayout.Panel;
        _host = new ScrollContainer { Name = "RightPanel", Position = new(px, py), Size = new(pw, ph), CustomMinimumSize = new(pw, ph) };
        _notBuilt = new Label { Name = "NotBuilt", Position = new(px, py), Size = new(pw, ph), AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false,
            Text = "NOT READY: this page is built by ARC-UI-076 (tech); the auto-level scheme page is built by ARC-UI-1742 and ARC-UI-1792 (only in the original look, when its art reads)." };
        _artView.AddChild(_host); _artView.AddChild(_notBuilt);
        _artView.StatPressed += (row, plus) => { if (plus) StatSpendRequested?.Invoke(row); else StatRefundRequested?.Invoke(row); };
        _artView.CloseRequested += () => CloseRequested?.Invoke();
        _artView.SoundRequested += id => SoundRequested?.Invoke(id);
        _artView.HoverChanged += id => HoverTextRequested?.Invoke(id is { } value ? HoverText(value) : null);
        _artView.TabSelected += ApplyTab;
        _artView.Pages.SkillPressed += (row, plus) => { if (plus) SkillSpendRequested?.Invoke(row); else SkillRefundRequested?.Invoke(row); };
        _artView.Pages.TechPressed += (discipline, plus) => { if (plus) TechSpendRequested?.Invoke(discipline); else TechRefundRequested?.Invoke(discipline); };
        _artView.Pages.HoverEntryRequested += entry => HoverEntryRequested?.Invoke(entry);
        _artView.Pages.SoundRequested += id => SoundRequested?.Invoke(id);
        _artView.Spells.CollegeSelected += college => CollegeSelected?.Invoke(college);
        _artView.Spells.SpellPressed += (college, learn) => { if (learn) SpellLearnRequested?.Invoke(college); else SpellUnlearnRequested?.Invoke(college); };
        _artView.Spells.HoverEntryRequested += entry => HoverEntryRequested?.Invoke(entry);
        _artView.Spells.SoundRequested += id => SoundRequested?.Invoke(id);
        _artView.Schemes.SchemeChosen += scheme => SchemeChosen?.Invoke(scheme);
        _artView.Schemes.SoundRequested += id => SoundRequested?.Invoke(id);
    }

    /// <summary>Shows the page of a tab: the skills and tech pages in the original's look when their art read, else the diagnostic skills page
    /// (tab Skills) or a NOT READY note (tab Tech); the spells page in the original's look when its art read (ARC-UI-077), else the diagnostic one; the schemes' tab shows the note.</summary>
    private void ApplyTab(AreaSheetTab tab)
    {
        if (_host is null || _notBuilt is null || _artView is null) return;
        var original = _pagesReady && tab is AreaSheetTab.Skills or AreaSheetTab.Tech;
        var spells = _spellsReady && tab == AreaSheetTab.Spells;
        var schemes = _schemesReady && tab == AreaSheetTab.Schemes;
        _artView.Pages.Visible = false;
        if (_artView.Spells.Visible) _artView.Spells.CloseView();
        // The scheme list keeps its scroll while the sheet stays open (the exe's offset is reset at each open only): the page is hidden, not closed.
        _artView.Schemes.Visible = false;
        if (original) _artView.Pages.ShowPage(tab == AreaSheetTab.Skills);
        if (spells) { _artView.Spells.ShowPage(); _artView.Spells.Render(_spellsView); }
        if (schemes) { _artView.Schemes.ShowPage(); _artView.Schemes.Render(_schemesView); }
        _host.Visible = !original && !spells && !schemes && tab is AreaSheetTab.Skills or AreaSheetTab.Spells;
        _notBuilt.Visible = !original && !spells && !schemes && !_host.Visible;
        if (_host.Visible) _pages.CurrentTab = tab == AreaSheetTab.Skills ? 1 : 2;
    }

    /// <summary>The text bar's text for a message id: charedit.mes of that number; a stat's adds the cost of its next point (PROJECT: the
    /// exe sends type 12 with the stat and the cost, the layout of that message is not read, ledger CHW8).</summary>
    private string? HoverText(int id)
    {
        var text = _artView!.Texts.Hint(id);
        if (text is null) return null;
        var row = id switch { 106 => 0, 107 => 1, >= 109 and <= 116 => id - 107, _ => -1 };
        if (row >= 0 && _statsView?.Rows[row] is { } stat) text += stat.NextCost == 0 ? "\n(at its maximum)" : $"\nNext point costs {stat.NextCost}";
        return text;
    }

    private void RenderArt(AreaCharacterSheetView view, AreaSkillsPageView? skills, AreaTechPageView? tech)
    {
        if (!ArtMode) return;
        _panel.Visible = false;
        _artView!.Render(view, _portrait);
        _artView.Pages.Render(skills, tech);
        _artView.Spells.Render(_spellsView);
        _artView.Schemes.Render(_schemesView);
        // A stat's text carries the cost of its next point: it is written again after a press changed the cost.
        if (_artView.RegionForSelfTest is { } region) HoverTextRequested?.Invoke(HoverText(region));
    }

    /// <summary>ARC-UI-034: the creation mode's controller focus: 0 Accept, 1 Cancel (highlighted as the menu's rows are).</summary>
    public void ShowCreationFocus(int focus)
    {
        if (_accept is null) return;
        _accept.Modulate = focus == 0 ? new Color(1f, .85f, .6f) : Colors.White;
        _close.Modulate = focus == 1 ? new Color(1f, .85f, .6f) : Colors.White;
    }

    /// <summary>The message of the last + or − press (empty clears it). In the art look the message window shows <paramref name="artText"/> when
    /// given (ARC-UI-078: the original's text, empty for none) instead of the project's words, which stay on the diagnostic window.</summary>
    public void ShowSkillMessage(string message, string? artText = null)
    {
        _skillMessage.Text = message;
        var shown = artText ?? message;
        if (ArtMode && shown.Length > 0) MessageRequested?.Invoke(shown);
    }

    /// <summary>Confirm presses the close button (the pages' buttons take the mouse or focus).</summary>
    public void Confirm() { if (!ArtMode) _close.EmitSignal(BaseButton.SignalName.Pressed); }

    private void ResizePanel()
    {
        if (_panel is null) return;
        if (ArtMode) { _artView!.Place(); return; }
        // ARC-UI-018: inside the part the interface bars leave free.
        var (top, size) = AreaGameBarPanel.FreeArea(GetViewport().GetVisibleRect().Size);
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new(AreaGameBarPanel.FreeLeft + (size.X - width) / 2, top + Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) { ResizePanel(); UpdateRoundView(); } }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
