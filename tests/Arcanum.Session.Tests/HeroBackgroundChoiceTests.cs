using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-097: setting and clearing the hero's background (background_set 0x4C25E0, its clear 0x4C2650, ledger BD4/BD5/BD9). Block,
// description and effect numbers and the effects' changes are invented; the half-elf fixture hero has strength 8 and race effect 67.
[TestClass]
public sealed class HeroBackgroundChoiceTests
{
    // Strength +2 (stat 0) and a rule with no change the session models (dodge +1).
    private static readonly SessionEffectDefinition Strong = new(86, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 2)]);
    private static readonly SessionEffectDefinition Nimble = new(87, [new(EffectAttributeKind.BasicSkill, 1, EffectChangeOperator.Add, 1)]);

    private static GameSessionOwner Owner(int background = SessionBackgrounds.None)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Background = background } });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionEffect[] Effects(GameSessionOwner owner) => owner.QueryHeroEffects()!.Effects.ToArray();

    private static int Strength(GameSessionOwner owner) => owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Strength);

    private static (int?, int?) Stored(GameSessionOwner owner) => (owner.QueryHeroBackground(), owner.QueryHeroBackgroundDescription());

    private static void Changed(SessionEffectResult result, GameSessionOwner owner, int entries, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, entries), (result.Disposition, result.Failure, result.Entries));
        Assert.AreEqual((revision, owner.Version), (owner.Version.Revision, result.Version), "one revision");
    }

    private static void Refused(SessionEffectResult result, GameSessionOwner owner, GameSessionFailure failure)
    {
        var revision = owner.Version.Revision;
        Assert.AreEqual((GameSessionDisposition.Rejected, failure, 0), (result.Disposition, result.Failure, result.Entries));
        Assert.AreEqual(revision, result.Version.Revision);
    }

    [TestMethod]
    public void Setting_stores_both_numbers_and_adds_one_background_effect_in_one_revision()
    {
        using var owner = Owner();
        Assert.AreEqual(((int?)0, (int?)0), Stored(owner));
        Changed(owner.SetHeroBackground(owner.Version, new(7, 1007, 86), Strong), owner, 1, 1);
        Assert.AreEqual(((int?)7, (int?)1007), Stored(owner));
        CollectionAssert.AreEqual(new[] { new SessionEffect(86, EffectCause.Background) }, Effects(owner));
        Assert.AreEqual(10, Strength(owner), "the effect applies");
        // An effect with no modelled change still enters the list.
        Changed(owner.SetHeroBackground(owner.Version, new(8, 1008, 87), Nimble), owner, 1, 2);
        Assert.AreEqual(((int?)8, (int?)1008), Stored(owner));
    }

    [TestMethod]
    public void A_second_set_keeps_both_effects_and_each_clear_removes_only_the_first_background_entry()
    {
        using var owner = Owner();
        owner.SetHeroBackground(owner.Version, new(7, 1007, 86), Strong);
        owner.SetHeroBackground(owner.Version, new(8, 1008, 87), Nimble);
        CollectionAssert.AreEqual(new[] { new SessionEffect(86, EffectCause.Background), new SessionEffect(87, EffectCause.Background) }, Effects(owner),
            "the set clears nothing (0x4C25E0)");
        // An entry of the same number from another cause is not the clear's.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, Strong, EffectCause.Item).Disposition);
        Changed(owner.ClearHeroBackground(owner.Version), owner, 1, 4);
        Assert.AreEqual(((int?)0, (int?)0), Stored(owner));
        CollectionAssert.AreEqual(new[] { new SessionEffect(87, EffectCause.Background), new SessionEffect(86, EffectCause.Item) }, Effects(owner),
            "the first background entry, whatever its number (0x4EA2E0)");
        Changed(owner.ClearHeroBackground(owner.Version), owner, 1, 5);
        CollectionAssert.AreEqual(new[] { new SessionEffect(86, EffectCause.Item) }, Effects(owner));
        var none = owner.ClearHeroBackground(owner.Version);
        Assert.AreEqual((GameSessionDisposition.NoChange, 0, 5L), (none.Disposition, none.Entries, owner.Version.Revision), "nothing left to clear");
    }

    [TestMethod]
    public void A_clear_zeroes_a_background_that_has_no_effect_entry()
    {
        using var owner = Owner(SessionBackgrounds.Technophobia);
        Changed(owner.ClearHeroBackground(owner.Version), owner, 0, 1);
        Assert.AreEqual(((int?)0, (int?)0), Stored(owner));
        Assert.AreEqual(0, Effects(owner).Length);
    }

    [TestMethod]
    public void The_creation_buttons_clear_then_set_so_a_sole_choice_pressed_again_keeps_one_effect()
    {
        // BD4: block 0 for any race, block 1 for elves only; a human man has block 0 alone, so "previous" lands on it again.
        var table = BackgroundTable.Create(Mes("{0}{1000}{1}{86}{2}{}{3}{0}{4}{}\n{10}{1001}{11}{87}{12}{ELF}{13}{0}{14}{}"), Mes("{1000}{Plain\ntext}"));
        using var owner = Owner();
        for (var press = 0; press < 3; press++)
        {
            var next = table.ButtonPrevious(owner.QueryHeroBackground()!.Value, 0, BackgroundTable.MaleGender);
            Assert.AreNotEqual(GameSessionDisposition.Rejected, owner.ClearHeroBackground(owner.Version).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetHeroBackground(owner.Version, table.Choice(next)!, Strong).Disposition);
        }
        Assert.AreEqual(((int?)0, (int?)1000), Stored(owner));
        CollectionAssert.AreEqual(new[] { new SessionEffect(86, EffectCause.Background) }, Effects(owner), "no accumulation");
        Assert.AreEqual(10, Strength(owner));
    }

    [TestMethod]
    public void Refusals_change_nothing()
    {
        using var owner = Owner();
        Refused(owner.SetHeroBackground(owner.Version, new(1000, 1, 86), Strong), owner, GameSessionFailure.InvalidBackground);
        Refused(owner.SetHeroBackground(owner.Version, new(-1, 1, 86), Strong), owner, GameSessionFailure.InvalidBackground);
        Refused(owner.SetHeroBackground(owner.Version, new(7, 1007, 87), Strong), owner, GameSessionFailure.InvalidBackground);
        Refused(owner.SetHeroBackground(owner.Version, new(7, 1007, 67), new(67, [])), owner, GameSessionFailure.InnateEffect);
        Refused(owner.SetHeroBackground(new(owner.Identity, 5), new(7, 1007, 86), Strong), owner, GameSessionFailure.StaleVersion);
        owner.SetHeroBackground(owner.Version, new(7, 1007, 86), Strong);
        Refused(owner.ClearHeroBackground(new(owner.Identity, 0)), owner, GameSessionFailure.StaleVersion);
        Assert.AreEqual(((int?)7, (int?)1007), Stored(owner));
        using var heroless = Create();
        Refused(heroless.SetHeroBackground(heroless.Version, new(7, 1007, 86), Strong), heroless, GameSessionFailure.InvalidHero);
        Refused(heroless.ClearHeroBackground(heroless.Version), heroless, GameSessionFailure.InvalidHero);
        Assert.AreEqual(((int?)null, (int?)null), Stored(heroless));
    }

    [TestMethod]
    public void The_background_description_and_effect_are_saved()
    {
        using var owner = Owner();
        owner.SetHeroBackground(owner.Version, new(7, 1007, 86), Strong);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"background\":7,\"backgroundDescription\":1007}");
        using var restored = GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot!, RestoreOptions()).Owner!;
        Assert.AreEqual(((int?)7, (int?)1007), Stored(restored));
        CollectionAssert.AreEqual(Effects(owner), Effects(restored));
        Changed(restored.ClearHeroBackground(restored.Version), restored, 1, 2);
        Assert.AreEqual(8, Strength(restored));
    }

    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.Latin1.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, $"line {parsed.Line}");
        return parsed.File!;
    }
}
