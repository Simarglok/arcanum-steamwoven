using Arcanum.Data.Audio;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Sound;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-041: the session's cue bridge from stored sound fields and attack results (docs/audio/sound-cues.md);
/// every field value is invented.</summary>
[TestClass]
public sealed class SoundCueTests
{
    private static ObjectSoundFields Object(int type, int sound = 0, int material = 4, uint art = 0, uint flags = 0) => new(type, 1000 + type, sound, material, art, flags);

    private static ObjectSoundFields Weapon(int sound, int weight) => Object(5, sound, 5) with { Item = new(weight, 0, null) };

    private static AttackResolution Resolution(AttackOutcome outcome) =>
        new(outcome == AttackOutcome.Refused ? AttackRefusal.TargetDead : AttackRefusal.None, outcome, AttackMissCause.None, default, false, false,
            null, null, null, default, false, null, []);

    [TestMethod]
    public void Fields_become_rule_facts_with_the_item_weight_and_the_local_pc_state()
    {
        var ammo = Object(6, 0, 5) with { Item = new(3, 0, 1000) };
        Assert.AreEqual(new SoundItem(0, 5, 750, 0), SessionSoundCues.Item(ammo), "ammunition weighs weight × quantity / 4");
        var gold = Object(8, 0, 5) with { Item = new(500, 9056, null) };
        Assert.AreEqual(5958, SoundCueRules.Item(ItemSoundAction.Pickup, SessionSoundCues.Item(gold))!.Id);
        Assert.IsNull(SessionSoundCues.Item(Object(16)));
        Assert.AreEqual(new SoundCritter(16, 4500, 0x20300000, 3), SessionSoundCues.Critter(Object(16, 4500, art: 0x20300000), 3));
        Assert.AreEqual(SoundLocalPc.Absent, SessionSoundCues.LocalPc(null));
        Assert.AreEqual(SoundLocalPc.Off, SessionSoundCues.LocalPc(Object(15, flags: 0x3)));
        Assert.AreEqual(SoundLocalPc.Present, SessionSoundCues.LocalPc(Object(15, flags: 0x1)));
        Assert.AreEqual(5, SessionSoundCues.HitTarget(Object(16, material: 4), Object(7, material: 5)).Material, "a critter's armour is hit");
        Assert.AreEqual(4, SessionSoundCues.HitTarget(Object(16, material: 4)).Material);
        Assert.AreEqual(2, SessionSoundCues.HitTarget(Object(2, material: 2), Object(7, material: 5)).Material, "only a critter wears armour");
    }

    [TestMethod]
    public void An_attack_result_gives_the_use_sound_then_its_outcome_sounds()
    {
        var sword = Weapon(7300, 3000);
        var target = Object(16, 4500, 4);
        var armor = Object(7, 0, 2);
        CollectionAssert.AreEqual(new[] { (7300, SoundCueSource.Attacker), (4500, SoundCueSource.Target), (7306, SoundCueSource.Attacker) },
            SessionSoundCues.Attack(Resolution(AttackOutcome.CriticalHit), sword, target, armor, SoundLocalPc.Present).Select(c => (c.Cue.Id, c.Source)).ToArray());
        var hit = SessionSoundCues.Attack(Resolution(AttackOutcome.Hit), sword, target, armor, SoundLocalPc.Present);
        Assert.AreEqual((7304, 7000 + 20 * 2 + 3 * 4), (hit[1].Cue.Id, hit[1].Cue.Unresolved!.Id), "heavy metal on the worn wooden armour");
        Assert.AreEqual(7303, SessionSoundCues.Attack(Resolution(AttackOutcome.CriticalMiss), sword, target, null, SoundLocalPc.Present)[1].Cue.Id, "a critical miss is a miss");
        var unarmed = SessionSoundCues.Attack(Resolution(AttackOutcome.Hit), null, target, null, SoundLocalPc.Present);
        Assert.AreEqual((7000 + 3 * 0, 2), (unarmed.Single().Cue.Id, unarmed.Single().Cue.Variants), "fists on flesh, no use sound");
        Assert.AreEqual(0, SessionSoundCues.Attack(Resolution(AttackOutcome.Refused), sword, target, null, SoundLocalPc.Present).Count);
        Assert.AreEqual(0, SessionSoundCues.Attack(null, sword, target, null, SoundLocalPc.Present).Count);
    }
}
