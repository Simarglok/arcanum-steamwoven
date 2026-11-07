using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-227: the pa condition's facts (an NPC's stored name number and mind-controlled flag by identity, ledger PN2) and the nk action's kill port in the AREA conversation host
/// (ledger PN4). Synthetic content: an invented map of three NPC rows and an invented DLG.
/// </summary>
[TestClass]
public sealed class AreaFollowerConversationTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    private static AreaFieldValue Number(int slot, long number) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, number, [], null);

    private static AreaObjectRecord Row(int index, IReadOnlyList<AreaFieldValue> fields) =>
        new(Key(index), 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 31 + index, 25, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);

    // 0 name 6014; 1 name 8 and the mind-controlled spell flag (bit 27, field 20 = slot 19); 2 no name field.
    private static AreaInventoryMap Map() => new(14, new SessionMapInventorySeed(
            [new(Entity(0), SessionHolderKind.Npc, false, true, [], []), new(Entity(1), SessionHolderKind.Npc, false, true, [], []), new(Entity(2), SessionHolderKind.Npc, false, true, [], [])],
            []),
        new[] { Row(0, [Number(21, 6014)]), Row(1, [Number(21, 8), Number(19, 0x0800_0000 | 0x1)]), Row(2, []) }.ToDictionary(row => Entity(row.Key.SourceIndex)));

    [TestMethod]
    public void The_facts_are_the_rows_name_number_and_its_mind_controlled_bit_and_an_unknown_entity_has_none()
    {
        using var session = ConversationSessionFixture.Create(14);
        var host = new AreaInventoryHost([Map()]);
        Assert.AreEqual((6014, false), host.FollowerNameFacts(session, Entity(0)));
        Assert.AreEqual((8, true), host.FollowerNameFacts(session, Entity(1)));
        Assert.AreEqual((0, false), host.FollowerNameFacts(session, Entity(2)), "a row with no name field: name 0, like the script query");
        Assert.IsNull(host.FollowerNameFacts(session, "oid:" + new string('f', 64)));
    }

    [TestMethod]
    public void The_follower_port_over_those_facts_skips_the_mind_controlled_follower()
    {
        using var session = ConversationSessionFixture.Create(14);
        var host = new AreaInventoryHost([Map()]);
        var port = new SessionDialogFollowerNamePort(session, AreaConversationState.PcParticipant, entity => host.FollowerNameFacts(session, entity));
        Assert.IsFalse(port.HasFollowerNamed(new(AreaConversationState.PcParticipant, 6014)), "nobody follows yet");
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, Entity(0)).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, Entity(1)).Disposition);
        Assert.IsTrue(port.HasFollowerNamed(new(AreaConversationState.PcParticipant, 6014)));
        Assert.IsFalse(port.HasFollowerNamed(new(AreaConversationState.PcParticipant, 8)), "controlled by a spell: not counted");
    }

    [TestMethod]
    public void Nk_kills_the_speaker_through_the_viewers_kill_and_the_talk_goes_on()
    {
        using var f = AreaReactionConversationTests.Fixture.Create("{1}{Hello.}{}{}{}{}{}" + "{2}{Die.}{}{1}{}{0}{nk}");
        var killed = new List<string>();
        f.Host.KillNpc = entity => { killed.Add(entity); return true; };
        f.Host.EndReaction();
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual(0, killed.Count);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options.Single(option => option.Text == "Die.").Token).Status);
        CollectionAssert.AreEqual(new[] { f.Entity }, killed);
        StringAssert.EndsWith(AreaConversationText.Effect(talk.Effects.Last()), "NK NPC killed [Applied]");
    }

    [TestMethod]
    public void Nk_stays_unadmitted_without_the_kill_and_a_refused_kill_fails_the_talk()
    {
        using var f = AreaReactionConversationTests.Fixture.Create("{1}{Hello.}{}{}{}{}{}" + "{2}{Die.}{}{1}{}{0}{nk}");
        f.Host.EndReaction();
        var without = f.Host.Begin(f.Speaker).Conversation!;
        Assert.IsTrue(without.Frontier is not null || without.Options.Any(option => option.Block is not null), "no kill wired: the reply is blocked or the talk stops at the action");
        f.Host.EndReaction();
        f.Host.KillNpc = _ => false;
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        talk.Select(talk.Options.Single(option => option.Text == "Die.").Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, talk.Failure!.Kind);
    }
}
