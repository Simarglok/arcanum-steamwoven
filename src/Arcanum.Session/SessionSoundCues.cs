using Arcanum.Data.Audio;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Sound;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-041: the sound cues of session events (docs/audio/sound-cues.md). The session holds no object fields, so the host
/// passes the <see cref="ObjectSoundFields"/> it read for the objects involved; these turn them into the facts of
/// <see cref="SoundCueRules"/> and an attack's result into its cues. Pure; the host plays the cues and makes their variant
/// draws with the presentation's generator.
/// </summary>
public static class SessionSoundCues
{
    /// <summary>The object flag OFF: an object switched off (the local PC during world-map travel).</summary>
    public const uint OffFlag = 0x2;

    /// <summary>An item's sound facts, its weight by <see cref="SoundCueRules.ItemWeight"/> without a holder's magic
    /// adjustment (no aptitude reader yet); null for an object that is not an item.</summary>
    public static SoundItem? Item(ObjectSoundFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.Item is { } item
            ? new(fields.SoundEffect, fields.Material, SoundCueRules.ItemWeight(fields.Type, item.Weight, item.AmmoQuantity ?? 0), item.UnknownDescription)
            : null;
    }

    /// <summary>A critter's sound facts; <paramref name="tileSound"/> is the class of the ground tile under it
    /// (<see cref="TileSoundTable.SoundClass"/>), needed for footsteps only.</summary>
    public static SoundCritter Critter(ObjectSoundFields fields, int? tileSound = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new(fields.Type, fields.SoundEffect, fields.CurrentArt, tileSound);
    }

    /// <summary>What a hit lands on: the armour a critter target wears, else the target itself.</summary>
    public static SoundHitTarget HitTarget(ObjectSoundFields target, ObjectSoundFields? wornArmor = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var critter = target.Type is SoundCueRules.PcType or SoundCueRules.NpcType;
        return new(critter && wornArmor is not null ? wornArmor.Material : target.Material);
    }

    /// <summary>The local PC's state for critter sounds: absent without one, off when its OFF flag is set.</summary>
    public static SoundLocalPc LocalPc(ObjectSoundFields? localPc) =>
        localPc is null ? SoundLocalPc.Absent : (localPc.Flags & OffFlag) != 0 ? SoundLocalPc.Off : SoundLocalPc.Present;

    /// <summary>
    /// The cues of a resolved attack (SN27), in order: the weapon's use sound when the attack starts, then the result's. A
    /// refused attack, or one refused before its rolls, has none. A critical miss plays the miss sound; the sounds of the
    /// self-hit it can turn into are not chosen here.
    /// </summary>
    /// <param name="weapon">The attacker's weapon, null unarmed.</param>
    /// <param name="target">The critter attacked.</param>
    /// <param name="targetArmor">The armour the target wears, if any.</param>
    public static IReadOnlyList<PlacedSoundCue> Attack(AttackResolution? resolution, ObjectSoundFields? weapon, ObjectSoundFields target,
        ObjectSoundFields? targetArmor, SoundLocalPc localPc)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (resolution is null) return [];
        AttackSoundOutcome? outcome = resolution.Outcome switch
        {
            AttackOutcome.Hit => AttackSoundOutcome.Hit,
            AttackOutcome.CriticalHit => AttackSoundOutcome.CriticalHit,
            AttackOutcome.Miss or AttackOutcome.CriticalMiss => AttackSoundOutcome.Miss,
            _ => null,
        };
        if (outcome is not { } result) return [];
        var item = weapon is null ? null : Item(weapon);
        var cues = new List<PlacedSoundCue>();
        if (SoundCueRules.AttackStart(false, item) is { } start) cues.Add(start);
        var critter = target.Type is SoundCueRules.PcType or SoundCueRules.NpcType ? Critter(target) : null;
        cues.AddRange(SoundCueRules.AttackResult(result, item, critter, HitTarget(target, targetArmor), localPc));
        return cues.AsReadOnly();
    }
}
