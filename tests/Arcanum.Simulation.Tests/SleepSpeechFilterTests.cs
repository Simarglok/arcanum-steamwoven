using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests;

// ARC-WORLD-2310 (ledger BM5f, exe 0x4F6840, 0x4F6B70, 0x4F6BB0): the sleeping speech transform, with invented filler and kept words.
[TestClass]
public sealed class SleepSpeechFilterTests
{
    [TestMethod]
    public void The_entry_applies_to_an_unconscious_or_sleeping_speaker_only()
    {
        Assert.IsFalse(SleepSpeechFilter.Applies(false, false));
        Assert.IsTrue(SleepSpeechFilter.Applies(true, false));
        Assert.IsTrue(SleepSpeechFilter.Applies(false, true));
        var filter = new SleepSpeechFilter("zq", []);
        Assert.AreEqual("hello there", filter.Speech("hello there", false, false), "awake: the text as typed");
        Assert.AreEqual(0, filter.Counter, "and the counter does not move");
        Assert.AreEqual("zqzqz qzqzq", filter.Speech("hello there", false, true));
    }

    [TestMethod]
    public void Non_letters_are_copied_and_letters_take_the_filler_in_turn()
    {
        var filter = new SleepSpeechFilter("abc", []);
        Assert.AreEqual("abc, 12 abca!", filter.Transform("Xyz, 12 Wxyz!"));
        Assert.AreEqual("", filter.Transform(""));
        Assert.AreEqual("   ", filter.Transform("   "));
    }

    [TestMethod]
    public void The_filler_counter_runs_on_across_texts_and_is_never_reset()
    {
        var filter = new SleepSpeechFilter("abc", []);
        Assert.AreEqual("ab", filter.Transform("xx"));
        Assert.AreEqual(2, filter.Counter);
        Assert.AreEqual("cab", filter.Transform("yyy"), "the next text goes on where the last stopped");
        Assert.AreEqual(2, filter.Counter);
    }

    [TestMethod]
    public void Kept_words_are_copied_as_typed_by_prefix_case_insensitively_at_any_letter()
    {
        var filter = new SleepSpeechFilter("z", ["help", "no"]);
        Assert.AreEqual("HeLp zz", filter.Transform("HeLp me"), "case kept as typed");
        Assert.AreEqual("zzzz zz", filter.Transform("hexp me"), "a miss turns every letter into filler");
        Assert.AreEqual("zhelpzz", filter.Transform("ahelpxy"), "a kept word inside a word, no boundary check");
        Assert.AreEqual("nozz", filter.Transform("noxy"), "a kept word is a prefix match: the rest is filler");
    }

    [TestMethod]
    public void The_first_fitting_kept_word_wins_and_a_word_at_the_end_of_the_text_is_not_kept()
    {
        var filter = new SleepSpeechFilter("z", ["he", "help"]);
        Assert.AreEqual("hezz zz", filter.Transform("help me"), "the earlier word wins, the rest is filler");
        Assert.AreEqual("zz", filter.Transform("he"), "no value for a word that ends where the text ends: not kept");
        Assert.AreEqual("hez", filter.Transform("hex"), "one letter more and it is kept");
    }

    [TestMethod]
    public void An_empty_filler_is_refused_and_an_empty_kept_word_is_dropped()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SleepSpeechFilter("", []));
        var filter = new SleepSpeechFilter("z", ["", "hi"]);
        Assert.AreEqual(1, filter.KeepWords.Count);
        Assert.AreEqual("hi zz", filter.Transform("hi me"));
    }
}
