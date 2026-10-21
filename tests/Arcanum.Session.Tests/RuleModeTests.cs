using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

[TestClass]
public sealed class RuleModeTests
{
    [TestMethod]
    public void EachModeComposesTheDomainProfilesOfTheSameName()
    {
        Assert.AreSame(GameRules.Steamwoven, GameRules.For(RuleMode.Steamwoven));
        Assert.AreSame(GameRules.Original, GameRules.For(RuleMode.Original));
        CollectionAssert.AreEqual(new[] { GameRules.Steamwoven, GameRules.Original }, GameRules.All.ToArray());
        Assert.AreSame(CombatAnomalyProfile.Steamwoven, GameRules.Steamwoven.Combat);
        Assert.AreSame(CombatAnomalyProfile.Original, GameRules.Original.Combat);
        Assert.AreSame(GameEventQueueProfile.Steamwoven, GameRules.Steamwoven.Events);
        Assert.AreSame(GameEventQueueProfile.Original, GameRules.Original.Events);
        Assert.AreSame(ScrRuleProfile.Steamwoven, GameRules.Steamwoven.Scripts);
        Assert.AreSame(ScrRuleProfile.Original, GameRules.Original.Scripts);
        Assert.AreSame(ReactionRuleProfile.Steamwoven, GameRules.Steamwoven.Reaction);
        Assert.AreSame(ReactionRuleProfile.Original, GameRules.Original.Reaction);
        Assert.AreSame(Arcanum.Simulation.Character.ExperienceRuleProfile.Steamwoven, GameRules.Steamwoven.Experience);
        Assert.AreSame(Arcanum.Simulation.Character.ExperienceRuleProfile.Original, GameRules.Original.Experience);
        foreach (var rules in GameRules.All)
        {
            Assert.AreEqual(RuleModeNames.Name(rules.Mode), rules.Name);
            // Every domain profile carries the mode's stable name, so traces and settings agree.
            Assert.AreEqual(rules.Name, rules.Combat.Name);
            Assert.AreSame(rules.Combat, CombatAnomalyProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Events.Name);
            Assert.AreSame(rules.Events, GameEventQueueProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Scripts.Name);
            Assert.AreSame(rules.Scripts, ScrRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Reaction.Name);
            Assert.AreSame(rules.Reaction, ReactionRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Data.Name);
            Assert.AreSame(rules.Data, Arcanum.Data.Fixes.DataFixProfile.FromName(rules.Name));
            Assert.AreSame(rules.Mode == RuleMode.Steamwoven ? Arcanum.Data.Fixes.DataFixProfile.Steamwoven : Arcanum.Data.Fixes.DataFixProfile.Original, rules.Data);
            Assert.AreEqual(rules.Name, rules.Experience.Name);
            Assert.AreSame(rules.Experience, Arcanum.Simulation.Character.ExperienceRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Items.Name);
            Assert.AreSame(rules.Items, Arcanum.Simulation.Items.ItemRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Technology.Name);
            Assert.AreSame(rules.Technology, Arcanum.Simulation.Technology.TechnologyRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Magic.Name);
            Assert.AreSame(rules.Magic, Arcanum.Simulation.Magic.MagicAnomalyProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Travel.Name);
            Assert.AreSame(rules.Travel, Arcanum.Simulation.Travel.TravelRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Dialog.Name);
            Assert.AreSame(rules.Dialog, Arcanum.Scripting.Dialog.DialogRuleProfile.FromName(rules.Name));
        }
        Assert.AreSame(Arcanum.Simulation.Items.ItemRuleProfile.Steamwoven, GameRules.Steamwoven.Items);
        Assert.AreSame(Arcanum.Simulation.Items.ItemRuleProfile.Original, GameRules.Original.Items);
        Assert.AreSame(Arcanum.Simulation.Technology.TechnologyRuleProfile.Steamwoven, GameRules.Steamwoven.Technology);
        Assert.AreSame(Arcanum.Simulation.Technology.TechnologyRuleProfile.Original, GameRules.Original.Technology);
        Assert.AreSame(Arcanum.Simulation.Magic.MagicAnomalyProfile.Steamwoven, GameRules.Steamwoven.Magic);
        Assert.AreSame(Arcanum.Simulation.Magic.MagicAnomalyProfile.Original, GameRules.Original.Magic);
        Assert.AreSame(Arcanum.Simulation.Travel.TravelRuleProfile.Steamwoven, GameRules.Steamwoven.Travel);
        Assert.AreSame(Arcanum.Simulation.Travel.TravelRuleProfile.Original, GameRules.Original.Travel);
        Assert.AreSame(Arcanum.Scripting.Dialog.DialogRuleProfile.Steamwoven, GameRules.Steamwoven.Dialog);
        Assert.AreSame(Arcanum.Scripting.Dialog.DialogRuleProfile.Original, GameRules.Original.Dialog);
        Assert.IsTrue(GameRules.Original.Dialog.GeneratedLinePickSpansGaps);
        Assert.IsFalse(GameRules.Steamwoven.Dialog.GeneratedLinePickSpansGaps);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GameRules.For((RuleMode)7));
    }

    [TestMethod]
    public void NamesAreExactAndCaseSensitive()
    {
        Assert.AreEqual("steamwoven", RuleModeNames.Name(RuleMode.Steamwoven));
        Assert.AreEqual("original", RuleModeNames.Name(RuleMode.Original));
        Assert.IsTrue(RuleModeNames.TryParse("original", out var mode));
        Assert.AreEqual(RuleMode.Original, mode);
        Assert.IsTrue(RuleModeNames.TryParse("steamwoven", out mode));
        Assert.AreEqual(RuleMode.Steamwoven, mode);
        foreach (var text in new[] { null, "", "Original", "ORIGINAL", " original", "source", "default", "1" })
            Assert.IsFalse(RuleModeNames.TryParse(text, out _), text ?? "null");
    }

    [TestMethod]
    public void SessionStartsSteamwovenAndSwitchesWithoutTouchingState()
    {
        using var owner = Create();
        Assert.AreSame(GameRules.Steamwoven, owner.Rules);
        var version = owner.Version;
        var before = GameSessionSnapshotCodec.Encode(owner.Capture());

        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Original));
        Assert.AreSame(GameRules.Original, owner.Rules);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SelectRules(RuleMode.Original));
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.SelectRules((RuleMode)(-1)));
        Assert.AreSame(GameRules.Original, owner.Rules);

        // A setting, not game state: the revision and the snapshot do not change.
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(before, GameSessionSnapshotCodec.Encode(owner.Capture()));

        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Steamwoven));
        Assert.AreSame(GameRules.Steamwoven, owner.Rules);
    }

    [TestMethod]
    public void CreateAndRestoreTakeTheModeFromTheCaller()
    {
        var created = GameSessionOwner.Create(Descriptor() with { Rules = RuleMode.Original });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        using var original = created.Owner!;
        Assert.AreSame(GameRules.Original, original.Rules);

        // The same save loads under either mode.
        var snapshot = original.Capture();
        using (var restored = Restore(snapshot))
            Assert.AreSame(GameRules.Steamwoven, restored.Rules);
        using (var restored = Restore(snapshot, RestoreOptions() with { Rules = RuleMode.Original }))
            Assert.AreSame(GameRules.Original, restored.Rules);

        Assert.AreEqual(GameSessionFailure.InvalidDescriptor,
            GameSessionOwner.Create(Descriptor() with { Rules = (RuleMode)2 }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidDescriptor,
            GameSessionOwner.Restore(snapshot, RestoreOptions() with { Rules = (RuleMode)2 }).Failure);
    }

    [TestMethod]
    public void DisposedSessionRefusesRuleCalls()
    {
        var owner = Create();
        owner.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.Rules);
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.SelectRules(RuleMode.Original));
    }

    [TestMethod]
    public void SettingRoundTripsAndFallsBackToSteamwoven()
    {
        Assert.AreEqual("{\"schema\":1,\"rules\":\"steamwoven\"}", RuleModeSettingCodec.Serialize(RuleMode.Steamwoven));
        Assert.AreEqual("{\"schema\":1,\"rules\":\"original\"}", RuleModeSettingCodec.Serialize(RuleMode.Original));
        foreach (var mode in Enum.GetValues<RuleMode>())
            Assert.AreEqual(mode, RuleModeSettingCodec.DeserializeOrSteamwoven(RuleModeSettingCodec.Serialize(mode)));
        Assert.AreEqual(RuleMode.Original, RuleModeSettingCodec.DeserializeOrSteamwoven("{ \"rules\": \"original\", \"schema\": 1 }"));
        foreach (var json in new[]
        {
            null, "", "not json", "[]", "\"original\"", "{}", "{\"schema\":1}",
            "{\"schema\":2,\"rules\":\"original\"}", "{\"schema\":\"1\",\"rules\":\"original\"}",
            "{\"schema\":1,\"rules\":\"Original\"}", "{\"schema\":1,\"rules\":1}",
            "{\"schema\":1,\"rules\":\"original\",\"extra\":0}", "{\"schema\":1,\"rules\":\"original\",\"rules\":\"original\"}",
            "{\"schema\":1.5,\"rules\":\"original\"}", "{\"schema\":1,\"rules\":\"\\ud800\"}",
            "{\"schema\":1,\"rules\":\"\ud800\"}", "{\"schema\":1,\"rules\":\"original\",}", "{\"schema\":1,\"rules\":{\"a\":[[[]]]}}",
            new string('[', 100) + new string(']', 100),
        })
            Assert.AreEqual(RuleMode.Steamwoven, RuleModeSettingCodec.DeserializeOrSteamwoven(json), json ?? "null");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RuleModeSettingCodec.Serialize((RuleMode)9));
    }
}
