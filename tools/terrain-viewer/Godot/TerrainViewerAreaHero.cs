using Arcanum.Scripting.GlobalFlags;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-027: with --hero the AREA session starts as that original pre-made hero (read during the background load):
// the session's player character, gold and starting kit, the hero's main-menu flag, and the hero's own DLG facts in
// conversations. The status line names the hero; without --hero it says the diagnostic PC profile is in use.
public partial class TerrainViewerApp
{
    private (Arcanum.Session.GameSessionOwner Session, long Revision, string Text)? _heroStatus;

    private string HeroStatusText()
    {
        if (_areaSession is not { } session) return "";
        if (_heroStatus is not { } cached || !ReferenceEquals(cached.Session, session) || cached.Revision != session.Version.Revision)
            _heroStatus = cached = (session, session.Version.Revision, AreaStartingHero.Summary(session));
        return " | " + cached.Text;
    }

    // Native observation, run after the inventory smoke when --hero was given: the session holds the loaded hero, its
    // gold and kit; the hero's flag is true; the conversation host gates replies with the hero's facts; the window lists
    // the kit first; the status line names the hero.
    private Task RunAreaHeroSmokeAsync(TerrainLoadResult result)
    {
        if (result.Hero is not { Definition: { } definition, Flag: { } flag } hero) return Task.CompletedTask;
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-WORLD-027 hero smoke: " + why); }
        var session = _areaSession ?? throw new InvalidOperationException("ARC-WORLD-027 hero smoke: no area session");
        var state = session.QueryHero();
        // ARC-WORLD-036: a new hero's scheme spends its starting points at the start, as a new PC's level set does.
        var schemed = hero.AutoLevel?.Scheme(definition.Advancement?.AutoLevelScheme ?? 0) is not null;
        Require(state is not null && state.Prototype == definition.Prototype && state.Level == definition.Level
            && (schemed ? state.UnspentPoints < definition.UnspentPoints : state.UnspentPoints == definition.UnspentPoints),
            "session hero " + state?.Prototype + " unspent " + state?.UnspentPoints);
        Require(session.QueryGold() == definition.Gold && session.QueryHeroKit().Count == definition.Kit.Count, "gold and kit");
        var flags = session.Flags;
        var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(flag)));
        Require(read.Status == GlobalFlagReadStatus.Known && read.Value == true, $"flag {flag} {read.Status} {read.Value}");
        var facts = session.QueryHeroDialogFacts();
        Require(facts is not null && (_conversationHost is null || _conversationHost.Profile.Pc == facts), "conversation PC facts " + _conversationHost?.Profile.Pc);
        if (_inventoryHost is not null)
        {
            var view = _inventoryHost.View(session, _hoverNames?.Catalog, _readyArea!.Snapshot.RuntimeMap);
            var kit = view.Pc.Take(definition.Kit.Count).ToArray();
            Require(kit.Length == definition.Kit.Count && kit.All(line => line.Entity.StartsWith("kit:", StringComparison.Ordinal) && line.Text.Contains("(starting kit)", StringComparison.Ordinal)),
                "window kit lines");
            Require(view.Gold == definition.Gold, "window gold " + view.Gold);
        }
        RefreshAreaStatus();
        Require(GetSelfTestSnapshot().Hud.Contains("hero: " + hero.Hero.Name + " level ", StringComparison.Ordinal), "HUD hero line");
        GD.Print("ARC-WORLD-027 native-smoke hero PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            hero.Hero.Name, definition.Prototype, Flag = flag, definition.Gold, Kit = definition.Kit.Count, hero.DroppedChanges,
            Facts = facts!.ToString(), Conversation = _conversationHost is not null }));
        RunAreaSheetSmoke(hero);
        return Task.CompletedTask;
    }

    // ARC-UI-009 native observation on the open sheet: the original skill texts name all sixteen rows, a + press on the first
    // row the pool accepts spends a point, − refunds it and a second − is refused at the editor's floor. Counts and outcomes only.
    private object RunAreaSkillsSmoke(Arcanum.Session.GameSessionOwner session)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-009 skills smoke: " + why); }
        _skillTextsTask?.Wait(TimeSpan.FromSeconds(60));
        AdoptSkillTexts();
        Require(_skillTexts is { Status: AreaSkillTextsStatus.Ready }, "skill texts " + _skillTexts?.Status + " " + _skillTexts?.Detail);
        // Texts that finished loading after C opened the sheet: render the page with them now.
        _sheetPanel!.Render(_sheetPanel.ViewForSelfTest!, BuildSkillsPage(), BuildSpellsPage());
        var page = _sheetPanel.SkillsForSelfTest!;
        Require(session.HeroEditorOpen, "the open sheet opened the hero's editor");
        var described = page.Rows.Count(row => row.Description is not null);
        var original = page.Rows.Count(row => row.Name != AreaSkillsPage.Label(row.Skill));
        Require(page.EditorOpen && page.Rows.Count == 16 && described == 16, $"page rows described {described}");
        var unspent = page.UnspentPoints;
        int? spentRow = null;
        for (var row = 0; row < page.Rows.Count && unspent > 0 && spentRow is null; row++)
        {
            var points = _sheetPanel.SkillsForSelfTest!.Rows[row].Points;
            _sheetPanel.SkillPlusForSelfTest(row).EmitSignal(BaseButton.SignalName.Pressed);
            if (_sheetPanel.SkillsForSelfTest!.Rows[row].Points == points + 1) spentRow = row;
        }
        var refusedAtFloor = false;
        if (spentRow is { } spent)
        {
            Require(_sheetPanel.SkillsForSelfTest!.UnspentPoints < unspent, "+ took unspent points");
            _sheetPanel.SkillMinusForSelfTest(spent).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SkillsForSelfTest!.UnspentPoints == unspent, "− gave them back");
            _sheetPanel.SkillMinusForSelfTest(spent).EmitSignal(BaseButton.SignalName.Pressed);
            refusedAtFloor = _sheetPanel.SkillMessageForSelfTest == "refused: nothing bought since the sheet opened to refund";
            Require(refusedAtFloor, "a second − at the floor: " + _sheetPanel.SkillMessageForSelfTest);
        }
        else
        {
            var revision = session.Version.Revision;
            _sheetPanel.SkillPlusForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SkillMessageForSelfTest == "refused: not enough unspent points" && session.Version.Revision == revision,
                "+ without unspent points: " + _sheetPanel.SkillMessageForSelfTest);
            // The points of every row equal the editor's floor: − is refused there.
            _sheetPanel.SkillMinusForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            refusedAtFloor = _sheetPanel.SkillMessageForSelfTest is "refused: nothing bought since the sheet opened to refund" or "refused: no points to refund";
            Require(refusedAtFloor && session.Version.Revision == revision, "− at the floor: " + _sheetPanel.SkillMessageForSelfTest);
        }
        GD.Print("ARC-UI-009 native-smoke skills page PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Rows = page.Rows.Count, OriginalNames = original, Described = described, Unspent = unspent, SpentRow = spentRow, RefusedAtFloor = refusedAtFloor }));
        return new { OriginalNames = original, SpentRow = spentRow };
    }

    // ARC-MAGIC-008 native observation on the open sheet: the original spell data names the sixteen colleges and their spells
    // and gives each spell's hint its cost; + on the shown college learns its next rank when the pool allows it (then − unlearns
    // it and is disabled at the floor again), otherwise it is refused with the failing check. Counts and outcomes only.
    private object RunAreaSpellsSmoke(Arcanum.Session.GameSessionOwner session)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-MAGIC-008 spells smoke: " + why); }
        _spellDataTask?.Wait(TimeSpan.FromSeconds(60));
        AdoptSpellData();
        Require(_spellData is { Status: AreaSpellDataStatus.Ready }, "spell data " + _spellData?.Status + " " + _spellData?.Detail);
        var named = 0; var costed = 0; var maintained = 0; var described = 0;
        for (var college = 0; college < AreaSpellsPage.Colleges; college++)
        {
            _sheetPanel!.CollegeTabForSelfTest(college).EmitSignal(BaseButton.SignalName.Pressed);
            var shown = _sheetPanel.SpellsForSelfTest!;
            Require(shown.College == college && shown.EditorOpen, "college tab " + college);
            named += shown.Spells.Count(row => row.Name != AreaSpellsPage.SpellName(null, row.Spell));
            costed += shown.Spells.Count(row => row.Cost is not null);
            maintained += shown.Spells.Count(row => row.MaintainPeriod is > 0);
            described += shown.Spells.Count(row => row.Description is not null);
        }
        var colleges = _sheetPanel!.SpellsForSelfTest!.Colleges.Count(tab => tab.Name != AreaSpellsPage.CollegeName(null, tab.College) && tab.Description is not null);
        Require(named == 80 && costed == 80 && described == 80 && colleges == 16, $"original texts named={named} costed={costed} described={described} colleges={colleges}");
        _sheetPanel.CollegeTabForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
        var page = _sheetPanel.SpellsForSelfTest!;
        var revision = session.Version.Revision;
        _sheetPanel.SpellPlusForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
        var learned = _sheetPanel.SpellsForSelfTest!.Rank == page.Rank + 1;
        var message = _sheetPanel.SkillMessageForSelfTest;
        if (learned)
        {
            Require(message.StartsWith("learned ", StringComparison.Ordinal) && _sheetPanel.SpellsForSelfTest.CanUnlearn, "+ learned: " + message);
            _sheetPanel.SpellMinusForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(_sheetPanel.SpellsForSelfTest!.Rank == page.Rank && _sheetPanel.SpellMinusForSelfTest.Disabled, "− unlearned it: " + _sheetPanel.SkillMessageForSelfTest);
        }
        else Require(message.StartsWith("refused: ", StringComparison.Ordinal) && session.Version.Revision == revision, "+ refused: " + message);
        Require(_sheetPanel.SpellMinusForSelfTest.Disabled == !_sheetPanel.SpellsForSelfTest!.CanUnlearn, "− follows the floor");
        GD.Print("ARC-MAGIC-008 native-smoke spells page PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Colleges = colleges, Named = named, Costed = costed, Maintained = maintained, Learned = learned, Refusal = learned ? null : message,
            Unspent = page.UnspentPoints }));
        return new { Named = named, Maintained = maintained, Learned = learned };
    }

    // ARC-UI-007 native observation with the original hero: the production C key opens the sheet over the session values
    // (experience data loaded with the hero), the open sheet owns the keys and Escape closes it. Prints outcomes and counts
    // only, no values of the original data.
    private void RunAreaSheetSmoke(AreaHeroLoad hero)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-007 sheet smoke: " + why); }
        var session = _areaSession!;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            Require(hero.Experience is not null && ReferenceEquals(session.Experience, hero.Experience), "the hero's experience data is the session's");
            var revision = session.Version.Revision;
            InventoryKey(Key.C);
            Require(SheetOpen && WorldModal && _sheetPanel!.ViewForSelfTest is not null, "C opened the sheet: " + _sheetStatus);
            var state = session.QueryHeroLife()!.Record.State;
            var stats = session.QueryHeroStats(new(0, state.PoisonLevel, state.Conditions.HasFlag(Arcanum.Simulation.Combat.LifeConditions.CrippledLegsBoth)))!.Value.Snapshot!;
            Arcanum.Simulation.Character.CharacterStat[] order = [Arcanum.Simulation.Character.CharacterStat.Strength, Arcanum.Simulation.Character.CharacterStat.Constitution,
                Arcanum.Simulation.Character.CharacterStat.Dexterity, Arcanum.Simulation.Character.CharacterStat.Beauty, Arcanum.Simulation.Character.CharacterStat.Intelligence,
                Arcanum.Simulation.Character.CharacterStat.Willpower, Arcanum.Simulation.Character.CharacterStat.Perception, Arcanum.Simulation.Character.CharacterStat.Charisma];
            var shown = _sheetPanel!.ViewForSelfTest!;
            var experience = session.QueryHeroExperience()!;
            var life = session.QueryHeroLife()!.Status;
            Require(shown.Name == hero.Hero.Name && shown.Level == experience.Level && shown.ToNextLevel == experience.ToNextLevel && shown.ToNextLevel is > 0
                && shown.UnspentPoints == experience.UnspentPoints && shown.MaxHitPoints == life.MaxHitPoints && shown.MaxFatigue == life.MaxFatigue
                && shown.Stats.Count == 8 && shown.Secondary.Count == 8 && shown.Resistances.Count == 5
                && shown.Stats.Select(v => v.Value).SequenceEqual(order.Select(stats.GetEffective)), "the sheet shows the session hero");
            Require(_sheetPanel.TextForSelfTest.Split('\n').Length == AreaCharacterSheet.Lines(shown).Count, "sheet text rows");
            var skills = RunAreaSkillsSmoke(session);
            var spells = RunAreaSpellsSmoke(session);
            revision = session.Version.Revision;
            InventoryKey(Key.I);
            Require(SheetOpen && !InventoryOpen, "the open sheet owns the I key");
            InventoryKey(Key.Escape);
            Require(!SheetOpen && session.Version.Revision == revision && !session.HeroEditorOpen, "Escape closed the sheet and its editor; nothing else changed");
            GD.Print("ARC-UI-007 native-smoke character sheet PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                hero.Hero.Name, Rows = AreaCharacterSheet.Lines(shown).Count, NextLevelKnown = shown.ToNextLevel is not null,
                Marked = shown.Stats.Count(v => v.Mark != AreaSheetMark.None), Skills = skills, Spells = spells }));
        }
        finally
        {
            if (SheetOpen) EndSheet();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
