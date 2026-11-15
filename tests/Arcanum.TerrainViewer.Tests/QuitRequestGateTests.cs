using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class QuitRequestGateTests
{
    [TestMethod]
    public void First_request_asks_and_the_question_stays_until_answered()
    {
        var gate = new QuitRequestGate();
        Assert.IsFalse(gate.Asking);
        Assert.AreEqual(QuitDecision.Ask, gate.Request(1000, holdToQuit: true));
        Assert.IsTrue(gate.Asking);
        Assert.AreEqual(QuitDecision.None, gate.Request(1000 + 5000, true), "a later single request is no answer and no hold");
        gate.Answered();
        Assert.IsFalse(gate.Asking);
        Assert.AreEqual(QuitDecision.Ask, gate.Request(20_000, true), "after no the next request asks again");
    }

    [TestMethod]
    public void A_held_key_quits_after_the_repeat_stream_lasts()
    {
        var gate = new QuitRequestGate();
        long now = 1000;
        Assert.AreEqual(QuitDecision.Ask, gate.Request(now, true));
        now += 450;   // the key's initial repeat delay is a gap, not a repeat
        Assert.AreEqual(QuitDecision.None, gate.Request(now, true));
        var repeats = 0;
        var decision = QuitDecision.None;
        while (decision == QuitDecision.None && repeats < 500) { now += 30; repeats++; decision = gate.Request(now, true); }
        Assert.AreEqual(QuitDecision.Quit, decision);
        Assert.IsTrue(repeats >= QuitRequestGate.HoldMs / 30, "the stream lasted the hold time: " + repeats);
    }

    [TestMethod]
    public void The_hold_setting_off_only_asks()
    {
        var gate = new QuitRequestGate();
        long now = 0;
        Assert.AreEqual(QuitDecision.Ask, gate.Request(now, false));
        for (var i = 0; i < 500; i++) { now += 30; Assert.AreNotEqual(QuitDecision.Quit, gate.Request(now, false)); }
        Assert.IsTrue(gate.Asking);
    }

    [TestMethod]
    public void Slow_presses_one_after_the_other_never_add_up_to_a_hold()
    {
        var gate = new QuitRequestGate();
        long now = 0;
        Assert.AreEqual(QuitDecision.Ask, gate.Request(now, true));
        for (var i = 0; i < 50; i++) { now += 800; Assert.AreEqual(QuitDecision.None, gate.Request(now, true)); }
    }

    [TestMethod]
    public void A_pause_in_the_stream_starts_the_count_again()
    {
        var gate = new QuitRequestGate();
        long now = 0;
        gate.Request(now, true);
        for (var i = 0; i < 40; i++) { now += 30; gate.Request(now, true); }   // 1.2 s of repeats: not yet
        now += 900;                                                           // a pause
        for (var i = 0; i < 20; i++) { now += 30; Assert.AreEqual(QuitDecision.None, gate.Request(now, true), "0.6 s of a new stream is no hold"); }
    }

    [TestMethod]
    public void A_clock_that_goes_back_is_not_a_repeat()
    {
        var gate = new QuitRequestGate();
        gate.Request(5000, true);
        Assert.AreEqual(QuitDecision.None, gate.Request(4000, true));
    }
}
