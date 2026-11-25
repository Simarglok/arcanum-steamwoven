using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-043: the AREA conversation host opens the speaker's reaction conversation slot for every talk and the caller
/// closes it at its end, so ME is false in the first talk and true in the next one, and an insult's change lands in the talk.
/// Synthetic content: an invented SCR whose dialog action starts DLG node 1, and an invented DLG.
/// </summary>
[TestClass]
public sealed class AreaReactionConversationTests
{
    private const string Dialog = "{1}{Hello.}{}{}{}{}{RP 1001}"
        + "{2}{First time.}{}{1}{ME 0}{0}{}"
        + "{3}{Again.}{}{1}{ME 1}{0}{}"
        + "{4}{i:}{}{1}{}{0}{}";

    [TestMethod]
    public void A_speakers_lines_are_named_by_the_hero_and_the_name_source_and_a_rebind_keeps_the_source()
    {
        // ARC-SCRIPT-2787 (0x416B00): the host's names for a speaker are the hero's player name and the speaker's shown name; without a source the placeholder stays (tests).
        using var f = Fixture.Create(hero: true);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.SetHeroPlayerName(f.Session.Version, Encoding.Latin1.GetBytes("Zed")).Disposition);
        Assert.AreEqual(new DialogSpeakerNames("Zed", "[NPC]"), f.Host.NamesFor(f.Speaker));
        f.Host.NpcName = row => row == f.Speaker.Row ? "Wren" : null;
        Assert.AreEqual(new DialogSpeakerNames("Zed", "Wren"), f.Host.NamesFor(f.Speaker));
        Assert.AreSame(f.Host.NpcName, f.Host.Rebind(f.Session).NpcName);
    }

    [TestMethod]
    public void Me_is_false_in_the_first_talk_and_true_in_the_second()
    {
        using var f = Fixture.Create();
        var first = f.Host.Begin(f.Speaker);
        Assert.AreEqual(AreaConversationStartStatus.Started, first.Status, first.Detail);
        var talk = first.Conversation!;
        CollectionAssert.AreEqual(new[] { "First time.", "" }, talk.Options.Select(o => o.Text).ToArray());
        Assert.AreEqual(new DialogGeneratedMarker('i'), talk.Options[1].Marker);
        Assert.AreEqual(0, f.Host.ReactionOpen);
        Assert.AreEqual(new SessionReaction(f.Entity, null, 50), f.Session.QueryReactionRecord(f.Entity), "the talk opened the conversation slot");
        Assert.IsFalse(f.Host.State.MetBefore(0));
        Assert.IsTrue(f.Session.HeroHasReputation(1001), "ARC-WORLD-044: the host passes the reputation port (RP in the NPC's line)");

        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options[0].Token).Status);
        Assert.IsTrue(f.Host.EndReaction());
        Assert.IsNull(f.Host.ReactionOpen);
        Assert.IsFalse(f.Host.EndReaction(), "a second end has no open slot");
        Assert.AreEqual(new SessionReaction(f.Entity, 50, null), f.Session.QueryReactionRecord(f.Entity), "the end remembers the PC");
        Assert.IsTrue(f.Host.State.MetBefore(0));
        Assert.AreEqual(AreaConversationStartStatus.ScriptEnded, f.Host.Resume(f.Speaker, talk.End!.ScriptLine!.Value).Status);

        var second = f.Host.Begin(f.Speaker);
        CollectionAssert.AreEqual(new[] { "Again.", "" }, second.Conversation!.Options.Select(o => o.Text).ToArray());
        Assert.AreEqual(new SessionReaction(f.Entity, 50, 50), f.Session.QueryReactionRecord(f.Entity));
        Assert.IsTrue(f.Host.State.MetBefore(0), "the open talk keeps the remembered slot");
    }

    [TestMethod]
    public void An_insult_changes_the_talk_slot_and_ends_the_talk_once_the_reaction_is_20()
    {
        using var f = Fixture.Create();
        foreach (var expected in new[] { 40, 30 })
        {
            var talk = f.Host.Begin(f.Speaker).Conversation!;
            Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options.Single(o => o.Marker is not null).Token).Status);
            Assert.AreEqual(DialogEndKind.ReturnToScript, talk.End!.Kind, $"{expected} stays above 20");
            Assert.AreEqual(expected, f.Session.QueryReactionRecord(f.Entity)!.Value.Conversation, "the change lands in the open talk");
            Assert.IsTrue(f.Host.EndReaction());
            Assert.AreEqual(new SessionReaction(f.Entity, expected, null), f.Session.QueryReactionRecord(f.Entity));
        }
        var last = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual(DialogConversationSelectStatus.Ended, last.Select(last.Options.Single(o => o.Marker is not null).Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.Insulted, null), last.End);
        Assert.AreEqual("[the insult ended the dialog with the NPC's last line]", AreaConversationText.Outcome(last));
        StringAssert.EndsWith(AreaConversationText.Effect(last.Effects.Last()), "RE adjust -10 (insult) [Applied]");
        Assert.AreEqual(20, f.Host.State.Reaction(0));
        f.Host.EndReaction();
        Assert.AreEqual(new SessionReaction(f.Entity, 20, null), f.Session.QueryReactionRecord(f.Entity));
    }

    [TestMethod]
    public void A_talk_that_was_not_ended_is_closed_before_the_next_begins_and_a_rebind_starts_closed()
    {
        using var f = Fixture.Create();
        Assert.AreEqual(AreaConversationStartStatus.Started, f.Host.Begin(f.Speaker).Status);
        var again = f.Host.Begin(f.Speaker);
        CollectionAssert.AreEqual(new[] { "Again.", "" }, again.Conversation!.Options.Select(o => o.Text).ToArray(),
            "the missed end was made up before the next talk, so the NPC remembers the PC");
        Assert.AreEqual(new SessionReaction(f.Entity, 50, 50), f.Session.QueryReactionRecord(f.Entity));
        Assert.IsNull(f.Host.Rebind(f.Session).ReactionOpen);
        Assert.IsTrue(f.Host.EndReaction());
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _root;
        private readonly ScriptContent _content;

        private Fixture(DirectoryInfo root, ScriptContent content, GameSessionOwner session, AreaSpeaker speaker, AreaConversationHost host)
        {
            _root = root; _content = content; Session = session; Speaker = speaker; Host = host;
        }

        public GameSessionOwner Session { get; }
        public AreaSpeaker Speaker { get; }
        public AreaConversationHost Host { get; }
        public string Entity => AreaConversationState.EntityId(Speaker.Row);

        public static Fixture Create(string? dialog = null, bool npc = false, bool hero = false, bool deathSpeech = false, int dialogSlotScript = 777)
        {
            var root = Directory.CreateTempSubdirectory("arc-world-043-");
            void Write(string relative, byte[] bytes)
            {
                var path = Path.Combine(root.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            Write("tig.dat", AreaOwnerFixture.Dat());
            Write("modules/Syn.dat", AreaOwnerFixture.Dat(("dlg\\00777talk.dlg", Encoding.Latin1.GetBytes(dialog ?? Dialog)), ("scr\\00777talk.scr", Program(deathSpeech))));
            var load = AreaContentRoot.OpenScripts(Path.Combine(root.FullName, "modules", "Syn.dat"));
            Assert.AreEqual(AreaContentRootStatus.Ready, load.Status, load.Detail);
            using var source = new AreaOwnerFixture(motionBodies: true);
            var area = source.Owner.LoadStrict();
            var row = area.Objects.First(o => o.WorldX is not null && o.WorldY is not null);
            var scripts = new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [4, 0x0201, dialogSlotScript])], null);
            var speaker = new AreaSpeaker(0, row with { Type = npc ? 16 : row.Type, EffectiveFields = [.. row.EffectiveFields.Where(field => field.Slot != 31), scripts] }, 777, row.WorldX!.Value, row.WorldY!.Value);
            var session = hero ? ConversationSessionFixture.CreateWithHero(area.RuntimeMap) : ConversationSessionFixture.Create(area.RuntimeMap);
            return new(root, load.Content!, session, speaker, new AreaConversationHost(area, [speaker], load.Content, session));
        }

        // Line 0: dialog, node 1 (the DLG named after the script); line 1: return, skip default.
        private static byte[] Program(bool deathSpeech = false)
        {
            var bytes = new byte[64 + 2 * 132];
            // ARC-DEV-304: the file flags at 48 (the death-speech flag 0x8, SU15).
            if (deathSpeech) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), ScrCatalogue.FileFlag.DeathSpeech);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 2);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), 2);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 44), ScrCatalogue.Action.Dialog);
            bytes[64 + 44 + 4] = 3;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 44 + 12), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 132 + 44), ScrCatalogue.Action.ReturnSkipDefault);
            return bytes;
        }

        public void Dispose()
        {
            Session.Dispose();
            _content.Dispose();
            _root.Delete(true);
        }
    }
}
