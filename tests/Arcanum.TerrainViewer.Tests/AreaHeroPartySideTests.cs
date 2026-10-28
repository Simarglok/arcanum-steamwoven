using Arcanum.Data.Characters;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-067: the viewer builds the PC side of the party rules from the session hero when there is one, else from the labelled
// diagnostic profile (charisma 8, level 1, no Persuasion). The hero is invented (charisma 13, alignment 40).
[TestClass]
public sealed class AreaHeroPartySideTests
{
    private static GameSessionOwner Start(bool hero)
    {
        var definition = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 13, 0, 0], 1, 5, 40,
            new(67, [new(CharacterStat.Dexterity, StatModifierOperator.Add, 1)]), null, 385, []);
        var load = hero ? new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, definition, 2352, 0, null, "synthetic", null) : null;
        var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load);
        return created.Owner ?? throw new InvalidOperationException("SESSION_" + created.Failure);
    }

    [TestMethod]
    public void The_follow_facts_and_wait_facts_take_the_hero_or_the_diagnostic_profile()
    {
        using (var session = Start(hero: true))
        {
            var side = session.QueryHeroPartySide()!;
            var facts = AreaCompanionRoster.Facts(AreaCompanionRoster.Virgil, pcAlignment: -500, reaction: 50, hero: side);
            Assert.AreEqual((13, 1, 40, PersuasionTraining.None, 3), (facts.PcCharisma, facts.PcLevel, facts.PcAlignment, facts.PcPersuasion, facts.MaxFollowers),
                "the hero's charisma, level, alignment and the max followers 13 / 4 = 3, not the conversation host's alignment");
            Assert.AreEqual(new PartyWaitFacts(AreaCompanionRoster.Virgil.WaitIndex, PersuasionTraining.None, 13, false),
                AreaCompanionRoster.WaitFacts(AreaCompanionRoster.Virgil, side));
            Assert.AreEqual("PC side: hero (charisma 13, level 1, alignment 40, persuasion None, max followers 3)", AreaCompanionRoster.PartySideLabel(side));
        }
        using var plain = Start(hero: false);
        Assert.IsNull(plain.QueryHeroPartySide());
        var diagnostic = AreaCompanionRoster.Facts(AreaCompanionRoster.Virgil, pcAlignment: -500, reaction: 50, hero: plain.QueryHeroPartySide());
        Assert.AreEqual((AreaCompanionRoster.DiagnosticPcCharisma, AreaCompanionRoster.DiagnosticPcLevel, -500, PersuasionTraining.None, 2),
            (diagnostic.PcCharisma, diagnostic.PcLevel, diagnostic.PcAlignment, diagnostic.PcPersuasion, diagnostic.MaxFollowers));
        StringAssert.StartsWith(AreaCompanionRoster.PartySideLabel(null), "PC side: diagnostic profile (charisma 8, level 1, no persuasion");
    }

    [TestMethod]
    public void A_dialog_alignment_change_writes_the_heros_alignment()
    {
        using var session = Start(hero: true);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, "oid:a").Disposition);
        var state = new AreaConversationState(session, new(1), ["oid:a"], 50);
        Assert.AreEqual(40, state.Alignment, "the hero's stored alignment, not the diagnostic 0");
        Assert.AreEqual(DialogCommandResult.Applied, state.Execute(new AlignmentCommand(AreaConversationState.PcParticipant, DialogReactionMode.Adjust, 7)));
        Assert.AreEqual((47, 47), (state.Alignment, session.QueryHero()!.Alignment));
        Assert.AreEqual(CompanionLoyalty.CheckLeader | CompanionLoyalty.TestTooGood, session.QueryCompanion("oid:a").Loyalty);
        Assert.AreEqual(DialogCommandResult.Applied, state.Execute(new AlignmentCommand(AreaConversationState.PcParticipant, DialogReactionMode.Target, 5000)));
        Assert.AreEqual(1000, session.QueryHero()!.Alignment, "the stat's range bounds it");
    }
}
