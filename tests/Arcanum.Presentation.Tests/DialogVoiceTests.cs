using Arcanum.Presentation.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-043: when the dialogue voice starts and stops, against docs/research/audio-evidence.md SN22 (each NPC line
/// plays its voice once; an unvoiced line leaves the previous voice; every picked reply stops it; closing does not).</summary>
[TestClass]
public sealed class DialogVoiceTests
{
    private static string Kinds(IReadOnlyList<DialogVoiceCommand> commands) => string.Join(",", commands.Select(c => c.Kind == DialogVoiceCommandKind.Play ? $"play {c.Identity}" : "stop"));

    [TestMethod]
    public void Each_line_plays_its_voice_once()
    {
        var player = new DialogVoicePlayer();
        var conversation = new object();
        Assert.AreEqual("play 65546", Kinds(player.Update(conversation, 1, 65546)), "the first line speaks");
        Assert.AreEqual("", Kinds(player.Update(conversation, 1, 65546)), "once per line, not every frame");
        Assert.AreEqual("play 65547", Kinds(player.Update(conversation, 2, 65547)), "the next line speaks");
        Assert.AreEqual("", Kinds(player.Update(conversation, 3, -1)), "an unvoiced next line plays nothing");
        Assert.AreEqual("play 5", Kinds(player.Update(conversation, 5, 5)), "two turns between frames still give one line");
        Assert.AreEqual("", Kinds(player.Update(conversation, 5, 5)), "a reply that ends the dialog keeps the turn: the line does not speak again");
    }

    [TestMethod]
    public void Every_pick_stops_the_voice_and_closing_does_not()
    {
        Assert.AreEqual(DialogVoiceCommandKind.Stop, DialogVoicePlayer.Picked().Kind);
        var player = new DialogVoicePlayer();
        var first = new object();
        Assert.AreEqual("play 7", Kinds(player.Update(first, 3, 7)));
        player.Release();
        Assert.AreEqual("", Kinds(player.Update(new object(), 1, -1)), "a new conversation's unvoiced line leaves the voice playing (no pick stopped it)");
        Assert.AreEqual("play 9", Kinds(player.Update(new object(), 1, 9)), "a new conversation's voiced line replaces it (the host stops the old one)");
        Assert.AreEqual("play 9", Kinds(new DialogVoicePlayer().Update(first, 5, 9)), "the first turn seen speaks");
        Assert.ThrowsExactly<ArgumentNullException>(() => player.Update(null!, 0, 1));
    }

    [TestMethod]
    public void A_blank_final_line_without_a_voice_leaves_the_pick_to_stop_the_voice_of_the_node_before()
    {
        // ARC-SCRIPT-2807: a reply whose final line names a node absent from the DLG ends the talk with a blank line and voice −1 (ledger FLM1). The voice of the node before stops
        // with the pick of that reply (every pick stops it), not with the blank line, which plays nothing and so needs no voice command of its own.
        var player = new DialogVoicePlayer();
        var conversation = new object();
        Assert.AreEqual("play 7", Kinds(player.Update(conversation, 1, 7)));
        Assert.AreEqual(DialogVoiceCommandKind.Stop, DialogVoicePlayer.Picked().Kind);
        Assert.AreEqual("", Kinds(player.Update(conversation, 2, -1)), "the blank final line: no voice, no command");
    }
}
