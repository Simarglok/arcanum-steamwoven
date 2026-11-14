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
            Assert.AreEqual(rules.Name, rules.Orders.Name);
            Assert.AreSame(rules.Orders, Arcanum.Simulation.Party.CompanionOrderRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Dialog.Name);
            Assert.AreSame(rules.Dialog, Arcanum.Scripting.Dialog.DialogRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Written.Name);
            Assert.AreSame(rules.Written, Arcanum.Data.Written.WrittenRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Navigation.Name);
            Assert.AreSame(rules.Navigation, Arcanum.Simulation.Navigation.NavigationRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Creation.Name);
            Assert.AreSame(rules.Creation, Arcanum.Simulation.Character.CharacterCreationRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Lifecycle.Name);
            Assert.AreSame(rules.Lifecycle, LifecycleRuleProfile.FromName(rules.Name));
            Assert.AreEqual(rules.Name, rules.Interface.Name);
            Assert.AreSame(rules.Interface, InterfaceRuleProfile.FromName(rules.Name));
        }
        Assert.AreSame(Arcanum.Simulation.Items.ItemRuleProfile.Steamwoven, GameRules.Steamwoven.Items);
        Assert.AreSame(Arcanum.Simulation.Items.ItemRuleProfile.Original, GameRules.Original.Items);
        Assert.AreSame(Arcanum.Simulation.Technology.TechnologyRuleProfile.Steamwoven, GameRules.Steamwoven.Technology);
        Assert.AreSame(Arcanum.Simulation.Technology.TechnologyRuleProfile.Original, GameRules.Original.Technology);
        Assert.AreSame(Arcanum.Simulation.Magic.MagicAnomalyProfile.Steamwoven, GameRules.Steamwoven.Magic);
        Assert.AreSame(Arcanum.Simulation.Magic.MagicAnomalyProfile.Original, GameRules.Original.Magic);
        Assert.AreSame(Arcanum.Simulation.Travel.TravelRuleProfile.Steamwoven, GameRules.Steamwoven.Travel);
        Assert.AreSame(Arcanum.Simulation.Travel.TravelRuleProfile.Original, GameRules.Original.Travel);
        Assert.AreSame(Arcanum.Simulation.Party.CompanionOrderRuleProfile.Steamwoven, GameRules.Steamwoven.Orders);
        Assert.AreSame(Arcanum.Simulation.Party.CompanionOrderRuleProfile.Original, GameRules.Original.Orders);
        Assert.AreSame(Arcanum.Scripting.Dialog.DialogRuleProfile.Steamwoven, GameRules.Steamwoven.Dialog);
        Assert.AreSame(Arcanum.Scripting.Dialog.DialogRuleProfile.Original, GameRules.Original.Dialog);
        Assert.IsTrue(GameRules.Original.Dialog.GeneratedLinePickSpansGaps);
        Assert.IsFalse(GameRules.Steamwoven.Dialog.GeneratedLinePickSpansGaps);
        Assert.AreSame(Arcanum.Data.Written.WrittenRuleProfile.Steamwoven, GameRules.Steamwoven.Written);
        Assert.AreSame(Arcanum.Data.Written.WrittenRuleProfile.Original, GameRules.Original.Written);
        // ARC-WORLD-072: LR4/LR7, a far click does not move under Original.
        Assert.IsTrue(GameRules.Original.Navigation.FarClickDoesNotMove);
        Assert.IsFalse(GameRules.Steamwoven.Navigation.FarClickDoesNotMove);
        // ARC-WORLD-073: LR5, the walk ends before a segment's last step onto a living critter under Original only.
        Assert.IsTrue(GameRules.Original.Navigation.SegmentEndCritterEndsWalk);
        Assert.IsFalse(GameRules.Steamwoven.Navigation.SegmentEndCritterEndsWalk);
        // ARC-TECHNOLOGY-009: WRD3 and AMA5 are Steamwoven fixes with Original switches.
        Assert.IsTrue(GameRules.Original.Written.UnprefixedMessageLosesFirstCharacter);
        Assert.IsFalse(GameRules.Steamwoven.Written.UnprefixedMessageLosesFirstCharacter);
        Assert.IsTrue(GameRules.Original.Items.ContainerStackLookupFails);
        Assert.IsFalse(GameRules.Steamwoven.Items.ContainerStackLookupFails);
        // ARC-WORLD-087: WEA1 is a Steamwoven fix with an Original switch.
        Assert.IsTrue(GameRules.Original.Items.EncumbranceLagsOnStackMerge);
        Assert.IsFalse(GameRules.Steamwoven.Items.EncumbranceLagsOnStackMerge);
        // ARC-UI-030: NW4 is a Steamwoven fix with an Original switch.
        Assert.IsTrue(GameRules.Original.Creation.RefusedGenderClearsBackground);
        Assert.IsFalse(GameRules.Steamwoven.Creation.RefusedGenderClearsBackground);
        // ARC-UI-028: the death window is a Steamwoven addition; Original resets to the main menu (PD8).
        Assert.IsTrue(GameRules.Original.Lifecycle.DeathResetsToMainMenu);
        Assert.IsFalse(GameRules.Steamwoven.Lifecycle.DeathResetsToMainMenu);
        // ARC-DEV-077: GI6, Steamwoven restores the saved generator; Original goes on with the running one (PS32).
        Assert.IsTrue(GameRules.Original.Lifecycle.LoadKeepsRunningRandom);
        Assert.IsFalse(GameRules.Steamwoven.Lifecycle.LoadKeepsRunningRandom);
        // ARC-UI-056: CUA1 is a Steamwoven fix with an Original switch.
        Assert.IsTrue(GameRules.Original.Interface.CalledShotCursorsSwapped);
        Assert.IsFalse(GameRules.Steamwoven.Interface.CalledShotCursorsSwapped);
        Assert.IsTrue(GameRules.Original.Interface.TurnOrderListHidden, "CH6: the original shows no turn order");
        Assert.IsFalse(GameRules.Steamwoven.Interface.TurnOrderListHidden, "the turn-order list is a Steamwoven addition");
        // ARC-UI-2019: numbered replies and the digit choice are a Steamwoven addition; the original's dialog shows no number and reads no key.
        Assert.IsTrue(GameRules.Original.Interface.DialogRepliesUnnumbered);
        Assert.IsFalse(GameRules.Steamwoven.Interface.DialogRepliesUnnumbered);
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
