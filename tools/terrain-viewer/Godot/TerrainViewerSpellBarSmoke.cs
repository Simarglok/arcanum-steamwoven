using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-053 native observation, inside the hotkey-bar smoke of the cast stage (the bars show; a hero who knows <spell>): M opens the
    // spell pane in the bottom strip with the background art 8 and the diagnostic pane lists no spell; the college buttons are those the hero
    // has a rank in, with their arts; a click on the spell's college selects it (drawn down) and shows its known spells with their icons;
    // entering the spell's icon names it in the mode's text area; a click on it plays 3000 and 3001 and activates it (its cursor opens, or a
    // self spell is cast); the pointer above the strip gives the window back to the log (ML8) and the pane hides.
    private async Task RunSpellPaneStripSmokeAsync(GameSessionOwner session, int spell, Func<int, Task> frames, Func<Vector2, Task> click,
        Func<Vector2, Vector2?, Task> hover)
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-053 spell-pane smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-053 spell-pane smoke: " + why); } }
        CancelSpellTargeting(null);
        _spellPane?.HidePane();
        await frames(2);
        // Another college first, so the click on the spell's college is seen to select it.
        var college = spell / SpellPaneRules.Ranks;
        _spellCollege = college == 0 ? 1 : 0;
        ShowSpellPane();
        await frames(3);
        var view = _spellBarView;
        Require(view is { Visible: true } && _messageLog.PaneMode && _spellPane is { Shown: true, ListInStrip: true }, "M shows the pane in the bottom strip");
        Require(view!.BackgroundForSelfTest == Art(SpellPaneRules.BackgroundArt)?[0], "the pane draws the background art 8");
        // ARC-UI-1200: the opt-in window-art part (ARC_AREA_WINDOW_ART): the pane's background draws as the mode under test says.
        if (WindowArtSmokeRequested) { await PaneArtSmokeAsync("spell-pane", () => view.BackgroundForSelfTest, SpellPaneRules.BackgroundArt); await frames(3); }
        Require(!_spellPane!.DrawnForSelfTest || WeaponPaneNow() is { ButtonShown: true }, "the diagnostic pane lists no spell");
        var ranks = session.QueryHeroSpellBook()!.Ranks.ToArray();
        var colleges = SpellPaneRules.ShownColleges(ranks);
        Require(colleges.Contains(college) && view.ShownCollegesForSelfTest.SequenceEqual(colleges), $"the colleges with a rank show: {string.Join(",", view.ShownCollegesForSelfTest)}");
        Require(colleges.All(c => view.CollegeArtForSelfTest(c) is { } art && Art(SpellPaneRules.CollegeButtonArt(c)!.Value) is { } frames3 && frames3.Contains(art)),
            "every college button draws its art");
        Require(view.ShownSpellsForSelfTest.SequenceEqual(SpellPaneRules.ShownSpells(ranks, _spellCollege)), "the other college's known spells show");

        _gameBarSoundsForSelfTest.Clear();
        await click(view.CollegeCentreForSelfTest(college)!.Value);
        await frames(1);
        var spells = SpellPaneRules.ShownSpells(ranks, college);
        Require(_spellCollege == college && view.ShownSpellsForSelfTest.SequenceEqual(spells), $"the click selected college {college}: {string.Join(",", view.ShownSpellsForSelfTest)}");
        Require(Art(SpellPaneRules.CollegeButtonArt(college)!.Value) is { Length: 3 } collegeFrames && view.CollegeArtForSelfTest(college) == collegeFrames[1], "the selected college is drawn down");
        Require(_gameBarSoundsForSelfTest.Select(s => s.Id).SequenceEqual([AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound]),
            "the college button played 3000 then 3001: " + string.Join(",", _gameBarSoundsForSelfTest.Select(s => s.Id)));
        Require(spells.Select((s, rank) => view.SpellArtForSelfTest(rank) is { } art && Art(HotkeyBarRules.SpellIcon(s)!.Value) is { } icon && icon.Contains(art)).All(drawn => drawn),
            "every known spell shows its icon");

        // From the strip's empty part below the buttons (inside the strip, so the pane keeps the window), onto the spell's icon.
        var icon = view.SpellCentreForSelfTest(spell)!.Value;
        await hover(icon, icon + new Vector2(0, 40));
        Require(_messageLog.PaneMode && _gameBarPanel!.MessageLog.PaneNameForSelfTest == AreaSpellsPage.SpellName(_spellData?.Data, spell),
            $"entering the icon names the spell: '{_gameBarPanel!.MessageLog.PaneNameForSelfTest}'");

        _gameBarSoundsForSelfTest.Clear();
        var casts = _spellCasts;
        await click(icon);
        Require(_spellTargeting?.Spell == spell || _spellCasts > casts, $"the click activated spell {spell}: {_spellNotice}");
        Require(_gameBarSoundsForSelfTest.Select(s => s.Id).SequenceEqual([AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound]),
            "the spell button played 3000 then 3001: " + string.Join(",", _gameBarSoundsForSelfTest.Select(s => s.Id)));
        Require(session.QueryHotkeys().Recent[0] == Arcanum.Simulation.Hotkeys.HotkeyEntry.ForSpell(spell), "the press became the latest recent action");
        CancelSpellTargeting(null);

        await hover(new Vector2(40, 120), null);
        Require(!_messageLog.PaneMode && !view.Visible && _spellPane.Shown, "the pointer above the strip gives the window back to the log; the pane stays open");
        _spellPane.HidePane();
        await frames(2);
        GD.Print($"ARC-UI-053 native-smoke spell-pane PASS spell={spell} colleges={string.Join(",", colleges)} college={college} spells={string.Join(",", spells)}");
    }
}
