using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-248: the cast smoke's spell learning, shared with the stages that cast (moved unchanged from the cast smoke).
    internal static bool DamagingSmokeSpell(SessionSpellRecord r) => r.Data.Number < SpellRecord.CollegeSpells && r.Data.Cost > 0 && r.Data.Enabled
        && r.Areas.Length > 0 && (r.Areas[0].Targets & 0x40) != 0 && !SpellUiRules.CastsAtOnce(r.Areas[0].Targets)
        && r.Of(SpellRunAction.Begin).Any(c => c.Type == SpellComponentType.Damage && c.Damage!.Maximum > 0);
    private static SessionSpellRecord? KnownSmokeSpell(GameSessionOwner session, IReadOnlyList<SessionSpellRecord> records, Func<SessionSpellRecord, bool> wanted) => session.QueryHeroSpellBook() is { } book
        ? records.FirstOrDefault(r => wanted(r) && r.Data.Number % SpellRecord.Ranks < book.Ranks[r.Data.Number / SpellRecord.Ranks]) : null;
    // The hero knows a spell <paramref name="wanted"/> picks, or learns the first rank-1 one it does not know yet.
    private static SessionSpellRecord? LearnSmokeSpell(GameSessionOwner session, IReadOnlyList<SessionSpellRecord> records, Func<SessionSpellRecord, bool> wanted, Action<bool, string> Require)
    {
        if (KnownSmokeSpell(session, records, wanted) is { } known) return known;
        if (session.QueryHeroExperience() is { UnspentPoints: not > 0 } before)
        {
            // The award must write a level whose points stay unspent: a hero's auto-level scheme (ARC-WORLD-036) would spend them,
            // so the hero has none while the award lands and gets its scheme back after. A third more covers the Hard difficulty's
            // quarter off (ExperienceRules.AdjustForDifficulty).
            var scheme = session.QueryHero()!.Advancement.AutoLevelScheme;
            if (scheme != 0) session.SetAutoLevelScheme(session.Version, SessionSchemeTarget.Hero, 0);
            var award = session.AwardHeroExperience(session.Version, Math.Max(2000, (before.ToNextLevel ?? 0) * 4 / 3 + 1));
            if (scheme != 0)
                Require(session.SetAutoLevelScheme(session.Version, SessionSchemeTarget.Hero, scheme).Disposition == GameSessionDisposition.Changed,
                    "the hero gets its auto-level scheme back");
            var after = session.QueryHeroExperience();
            Require(after?.UnspentPoints > 0, $"an experience award buys a point: {award.Disposition} {award.Failure}, scheme {scheme}, "
                + $"level {before.Level} -> {after?.Level}, experience {before.Experience} -> {after?.Experience}, to next {before.ToNextLevel}");
        }
        var book = session.QueryHeroSpellBook()!;
        var first = records.FirstOrDefault(r => wanted(r) && r.Data.Number % SpellRecord.Ranks == 0 && book.Ranks[r.Data.Number / SpellRecord.Ranks] == 0);
        Require(first is not null, "a rank-1 college spell to learn");
        // Points are spent only in the open character editor (0x5597C0, CP1), as the sheet's + does; the editor closes again
        // unless it was already open.
        var opened = !session.HeroEditorOpen;
        var editor = session.OpenHeroEditor();
        Require(editor.Failure == GameSessionFailure.None, "the hero editor opens: " + editor.Failure);
        var spent = session.SpendHeroPoint(session.Version, AdvancementTrack.College, first!.Data.Number / SpellRecord.Ranks);
        if (opened) session.CloseHeroEditor();
        Require(spent.Disposition == GameSessionDisposition.Changed, "the hero learns the spell: " + spent.Failure + " " + spent.Change?.Status);
        return KnownSmokeSpell(session, records, wanted);
    }
}
