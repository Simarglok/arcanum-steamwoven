using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-053: the original's spell pane in the bottom strip (docs/world/spell-cast-host.md#the-spell-pane-in-the-bottom-strip-arc-ui-053;
// ledger SPN1–SPN6). While the bars show and the spell pane is open (M or the Spells button) and holds the message window (ML8), the
// window draws bar mode 1: the background art 8, a college button for each college the hero has a rank in, and the selected college's
// known spells as icon buttons. A college is selected on its button's press (0x54BE1F) and its spells replace the old college's (0x550C60);
// a spell is activated on the release over its button (0x54BAF5: the latest recent action first, then 0x57BC70); a right press on a spell
// starts the hotkey drag (ARC-UI-027); the pointer entering a button writes its college's or spell's name in the mode's text area (0x550860,
// 0x5507E0). The selection starts on college 0 and lasts the run (the original keeps it in the save, not done). Without the bars or the
// pane's art the diagnostic pane lists the spells as before; the weapon's pane stays diagnostic.
public partial class TerrainViewerApp
{
    private AreaSpellBarView? _spellBarView;
    // The selected college (0x64C530): the original's interface init sets it to 0.
    private int _spellCollege;
    private (GameSessionOwner Session, GameSessionVersion Version, int College, object? Data)? _spellBarShown;
    private int _spellBarPressesForSelfTest;

    /// <summary>Per frame while the bars show (after the hotkey bar, whose art the pane shares): the view on the bottom strip, shown in bar
    /// mode 1, redrawn when the session, the selection or the spell data changed.</summary>
    private void SyncSpellBar()
    {
        if (_gameBarPanel is null || _areaSession is not { } session) return;
        var art = SpellListInStrip;
        if (_spellPane is not null) _spellPane.ListInStrip = art;
        if (_spellBarView is null)
        {
            _spellBarView = new AreaSpellBarView { Visible = false };
            // Under the message window's drawing: a hover's name is written over the mode's background (0x5509C0).
            _gameBarPanel.AddPartUnderMessageLog(_spellBarView);
            _spellBarView.CollegePressed += SpellBarCollegePressed;
            _spellBarView.CollegeReleased += (_, over) => { if (over) PlayBarSound(AreaGameBarLayout.ReleaseSound, "spell pane college"); };
            _spellBarView.SpellPressed += _ => PlayBarSound(AreaGameBarLayout.PressSound, "spell pane spell");
            _spellBarView.SpellReleased += SpellBarSpellReleased;
            _spellBarView.SpellRightPressed += HotkeyPaneDrag;
            _spellBarView.Entered += SpellBarEntered;
        }
        var shown = art && _spellPane?.Shown == true && _messageLog.PaneMode && _itemPaneItem is null;
        if (_spellBarView.Visible != shown) _spellBarView.Visible = shown;
        if (!shown) return;
        var stamp = (session, session.Version, _spellCollege, (object?)_spellData?.Data);
        if (_spellBarShown == stamp) return;
        _spellBarShown = stamp;
        _spellBarView.Bind(SpellBarLook(session));
    }

    /// <summary>Whether the bottom strip draws the spell list: the bars show and the pane's art read.</summary>
    private bool SpellListInStrip => _gameBarPanel is { Visible: true } && Art(SpellPaneRules.BackgroundArt) is not null;

    /// <summary>The bars went away: the diagnostic pane lists the spells again.</summary>
    private void HideSpellBar()
    {
        if (_spellPane is not null) _spellPane.ListInStrip = false;
        if (_spellBarView is not null) _spellBarView.Visible = false;
    }

    private AreaSpellBarLook SpellBarLook(GameSessionOwner session)
    {
        var ranks = session.QueryHeroSpellBook()?.Ranks.ToArray() ?? new int[SpellPaneRules.Colleges];
        var colleges = SpellPaneRules.ShownColleges(ranks)
            .Select(college => new AreaSpellBarButton(college, SpellPaneRules.CollegeButtonArt(college) is { } number ? Art(number) : null, college == _spellCollege))
            .ToArray();
        var spells = SpellPaneRules.ShownSpells(ranks, _spellCollege)
            .Select(spell => new AreaSpellBarButton(spell, HotkeyBarRules.SpellIcon(spell) is { } number ? Art(number) : null)).ToArray();
        return new(First(Art(SpellPaneRules.BackgroundArt)), colleges, spells);
    }

    /// <summary>0x54BE1F: a college's press selects it; its spells replace the old college's.</summary>
    private void SpellBarCollegePressed(int college)
    {
        PlayBarSound(AreaGameBarLayout.PressSound, "spell pane college");
        _spellBarPressesForSelfTest++;
        if (_spellCollege == college) return;
        _spellCollege = college;
        _spellBarShown = null;
    }

    /// <summary>0x54BAF5: the release over a spell's button activates it, the latest recent action first.</summary>
    private void SpellBarSpellReleased(int spell, bool over)
    {
        if (!over) return;
        PlayBarSound(AreaGameBarLayout.ReleaseSound, "spell pane spell");
        _spellBarPressesForSelfTest++;
        PressPaneSpell(spell);
        RefreshAreaStatus();
    }

    /// <summary>The pointer entered a button (0x550860 for a college, 0x5507E0 for a spell): in the spells mode only the name, in the
    /// mode's text area (0x5509C0). Leaving keeps it (0x550720 acts in the message mode only).</summary>
    private void SpellBarEntered(bool college, int number)
    {
        if (!_messageLog.PaneMode) return;
        AdoptSpellData();
        var data = _spellData?.Data;
        var name = college ? AreaSpellsPage.CollegeName(data, number) : AreaSpellsPage.SpellName(data, number);
        if (_messageLog.ShowPaneName(SpellPaneRules.BarMode, name)) _gameBarPanel?.MessageLog.Render();
    }
}
