using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-008 logbook over invented values and texts (no original data): entry order, date line, state labels and
/// styles, the texts by intelligence, the notes and the refusal, and RQ through the conversation state.</summary>
[TestClass]
public sealed class AreaLogbookTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    private static LogbookTexts Texts() => LogbookTexts.Create(true, Mes("{1000}{Quest A}\n{1001}{Quest B}"), Mes("{1000}{Low A}"),
        Mes("{20020}{Rumor 1001}\n{20040}{Rumor é 1002}"), Mes("{20020}{Low 1001}"));

    private static GameSessionOwner Hero(int intelligence)
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, intelligence, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load);
        return created.Owner ?? throw new InvalidOperationException("SESSION_" + created.Failure);
    }

    private static AreaLogbookView View(GameSessionOwner session, AreaLogbookTab tab, LogbookTexts? texts, MesTextProfile profile = MesTextProfile.Windows1252)
    {
        var build = AreaLogbook.Build(session, texts, tab, profile);
        Assert.AreEqual(AreaLogbookRefusal.None, build.Refusal);
        return build.View!;
    }

    private static void Advance(GameSessionOwner session, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, session.JumpClocks(session.QueryClockSnapshot().Token, ms).Disposition);

    [TestMethod]
    public void The_date_line_follows_the_original_layout_and_twelve_hour_clock()
    {
        Assert.AreEqual("January 1, 1885  12:00am    ", AreaLogbook.Date(0, 1885));
        Assert.AreEqual("January 1, 1885  12:05pm    ", AreaLogbook.Date(12 * 3_600_000 + 5 * 60_000, 1885));
        Assert.AreEqual("January 1, 1885  1:09pm    ", AreaLogbook.Date(13 * 3_600_000 + 9 * 60_000 + 59_999, 1885));
        Assert.AreEqual("January 1, 1885  11:59am    ", AreaLogbook.Date(11 * 3_600_000 + 59 * 60_000, 1885));
        // 30-day months, 12-month years.
        Assert.AreEqual("February 1, 1885  12:00am    ", AreaLogbook.Date(30L * 86_400_000, 1885));
        Assert.AreEqual("December 30, 1885  12:00am    ", AreaLogbook.Date(359L * 86_400_000, 1885));
        Assert.AreEqual("January 1, 1886  12:00am    ", AreaLogbook.Date(360L * 86_400_000, 1885));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaLogbook.Date(-1, 1885));
    }

    [TestMethod]
    public void Quests_and_rumors_are_listed_in_time_order_with_their_labels_texts_and_styles()
    {
        using var session = ConversationSessionFixture.Create();
        Assert.AreEqual(AreaLogbookRefusal.None, AreaLogbook.Refusal(session), "no hero: the logbook still opens");
        session.SetQuestState(session.Version, 1001, SessionQuestState.Completed);
        session.LearnRumor(session.Version, 1002);
        Advance(session, 3_600_000);
        session.SetQuestState(session.Version, 1000, SessionQuestState.Accepted);
        session.SetQuestState(session.Version, 1500, SessionQuestState.Mentioned);
        session.LearnRumor(session.Version, 1001);
        session.QuellRumor(session.Version, 1002);
        session.SetQuestState(session.Version, 1600, SessionQuestState.OtherCompleted);
        session.SetQuestState(session.Version, 1700, SessionQuestState.Achieved);
        session.SetQuestState(session.Version, 1800, SessionQuestState.Accepted);
        session.SetQuestState(session.Version, 1800, SessionQuestState.Botched);
        var revision = session.Version.Revision;

        var quests = View(session, AreaLogbookTab.Quests, Texts());
        CollectionAssert.AreEqual(new[]
        {
            new AreaLogbookEntry(1001, 54_000_000, "January 1, 1885  3:00pm    ", "completed", "Quest B", AreaLogbookStyle.Completed),
            new AreaLogbookEntry(1000, 57_600_000, "January 1, 1885  4:00pm    ", "accepted", "Quest A", AreaLogbookStyle.Active),
            new AreaLogbookEntry(1500, 57_600_000, "January 1, 1885  4:00pm    ", "mentioned", "", AreaLogbookStyle.Plain),
            new AreaLogbookEntry(1600, 57_600_000, "January 1, 1885  4:00pm    ", "completed by someone else", "", AreaLogbookStyle.Failed),
            new AreaLogbookEntry(1700, 57_600_000, "January 1, 1885  4:00pm    ", "achieved", "", AreaLogbookStyle.Active),
            new AreaLogbookEntry(1800, 57_600_000, "January 1, 1885  4:00pm    ", "botched", "", AreaLogbookStyle.Failed),
        }, quests.Entries.ToArray());
        Assert.AreEqual((AreaLogbookTab.Quests, false, "no hero: normal texts"), (quests.Tab, quests.LowIntelligence, quests.Note));
        Assert.AreEqual("January 1, 1885  3:00pm    completed\nQuest B", AreaLogbook.Line(quests.Entries[0]));

        var rumors = View(session, AreaLogbookTab.Rumors, Texts());
        CollectionAssert.AreEqual(new[]
        {
            new AreaLogbookEntry(1002, 54_000_000, "January 1, 1885  3:00pm    ", null, "Rumor é 1002", AreaLogbookStyle.Struck),
            new AreaLogbookEntry(1001, 57_600_000, "January 1, 1885  4:00pm    ", null, "Rumor 1001", AreaLogbookStyle.Plain),
        }, rumors.Entries.ToArray());
        Assert.AreEqual("January 1, 1885  3:00pm    \nRumor é 1002", AreaLogbook.Line(rumors.Entries[0]));
        Assert.AreEqual(revision, session.Version.Revision, "building the logbook changes nothing");
    }

    [TestMethod]
    public void The_hero_intelligence_chooses_the_texts_and_a_dead_hero_is_refused()
    {
        using var low = Hero(4);
        low.SetQuestState(low.Version, 1000, SessionQuestState.Accepted);
        low.SetQuestState(low.Version, 1001, SessionQuestState.Accepted);
        low.LearnRumor(low.Version, 1001);
        low.LearnRumor(low.Version, 1002);
        var quests = View(low, AreaLogbookTab.Quests, Texts());
        Assert.AreEqual((true, (string?)null), (quests.LowIntelligence, quests.Note));
        CollectionAssert.AreEqual(new[] { "Low A", "Quest B" }, quests.Entries.Select(e => e.Text).ToArray(), "a low description where one exists");
        CollectionAssert.AreEqual(new[] { "Low 1001", "" }, View(low, AreaLogbookTab.Rumors, Texts()).Entries.Select(e => e.Text).ToArray(),
            "the low rumor file only, no fallback");

        using var normal = Hero(5);
        normal.SetQuestState(normal.Version, 1000, SessionQuestState.Accepted);
        Assert.AreEqual("Quest A", View(normal, AreaLogbookTab.Quests, Texts()).Entries.Single().Text);

        Assert.AreEqual(GameSessionDisposition.Changed, low.Kill(low.Version, SessionLifeTarget.Hero).Disposition);
        Assert.AreEqual(new AreaLogbookBuild(AreaLogbookRefusal.Dead, null), AreaLogbook.Build(low, Texts(), AreaLogbookTab.Rumors, MesTextProfile.Windows1252));
    }

    [TestMethod]
    public void Missing_or_undecodable_texts_are_named_in_the_note()
    {
        using var session = ConversationSessionFixture.Create();
        session.SetQuestState(session.Version, 1000, SessionQuestState.Accepted);
        session.LearnRumor(session.Version, 1002);
        Assert.AreEqual("no hero: normal texts; no logbook texts loaded", View(session, AreaLogbookTab.Quests, null).Note);
        Assert.AreEqual("no hero: normal texts; the module has no quest table: no descriptions",
            View(session, AreaLogbookTab.Quests, LogbookTexts.Create(false, Mes("{1000}{Quest A}"))).Note);
        var ascii = View(session, AreaLogbookTab.Rumors, Texts(), MesTextProfile.Ascii);
        Assert.AreEqual("", ascii.Entries.Single().Text);
        Assert.AreEqual("no hero: normal texts; 1 text(s) not decodable as Ascii", ascii.Note);
    }

    [TestMethod]
    public void Rq_in_a_conversation_quells_the_rumor_in_the_session()
    {
        using var session = ConversationSessionFixture.Create();
        var state = new AreaConversationState(session, new(1), [], 50);
        Assert.AreEqual(DialogCommandResult.Applied, state.Execute(new QuellRumorCommand(1500)));
        Assert.IsTrue(session.QueryRumorQuelled(1500));
        Assert.AreEqual(DialogCommandResult.Applied, state.Execute(new QuellRumorCommand(1500)), "a quelled rumor stays quelled");
        Assert.AreEqual(DialogCommandResult.Rejected, state.Execute(new QuellRumorCommand(3000)));
        Assert.AreEqual("#0 turn 0: RQ 1500 quelled [Applied]",
            AreaConversationText.Effect(new DialogEffectReceipt(0, 0, new QuellRumorCommand(1500), default, DialogCommandAttemptOutcome.Applied)));
    }
}
