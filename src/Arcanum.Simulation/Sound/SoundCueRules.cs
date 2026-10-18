namespace Arcanum.Simulation.Sound;

/// <summary>
/// A sound to play: <see cref="Id"/> plus a draw of 0..<see cref="Variants"/> − 1 the presentation makes (1 = exactly
/// <see cref="Id"/>). When the drawn id is in no sound table and <see cref="Unresolved"/> is set, that cue plays instead
/// (a weapon hit without its own sound). An id no table holds plays nothing.
/// </summary>
public sealed record SoundCue
{
    public SoundCue(int id, int variants = 1, SoundCue? unresolved = null)
    {
        if (variants is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(variants));
        Id = id; Variants = variants; Unresolved = unresolved;
    }

    public int Id { get; }
    public int Variants { get; }
    public SoundCue? Unresolved { get; }
}

/// <summary>Item and weapon sounds in the original's order (its sound field offsets differ; see <see cref="SoundCueRules.Item"/>).</summary>
public enum ItemSoundAction { Pickup, Drop, Use, OutOfAmmo, Hit, Miss, CriticalHit }

/// <summary>Critter sounds: 0..6 are offsets from the critter's sound base; <see cref="Footsteps"/> is chosen by armour and ground.</summary>
public enum CritterSoundAction { CriticallyHit, Dying, DyingGruesome, Fidget, Attacking, Alerted, Agitated, Footsteps }

/// <summary>Container and portal sounds, offsets from the object's sound base.</summary>
public enum OpenableSoundAction { Open, Close, Locked }

/// <summary>Sounds of walls, scenery, projectiles, weapons and traps.</summary>
public enum MiscSoundAction { Busting, Destroying, Animating }

/// <summary>Footstep sound class of a ground tile (the tile name table's number).</summary>
public enum TileSoundClass { Dirt, Sand, Snow, Stone, Water, Wood }

/// <summary>Whether the local PC exists and is switched off (flag OFF, as during world-map travel): critter sounds need it
/// present and on.</summary>
public enum SoundLocalPc { Present, Off, Absent }

/// <summary>An item as its sounds see it: the sound base, the material, the weight <see cref="SoundCueRules.ItemWeight"/>
/// gives, and the unknown-description number (<see cref="SoundCueRules.GoldDescription"/> marks gold).</summary>
public sealed record SoundItem(int SoundEffect, int Material, int Weight, int UnknownDescription);

/// <summary>What a hit lands on: the target's material, or the material of the armour a critter target wears.</summary>
public sealed record SoundHitTarget(int Material);

/// <summary>A critter as its sounds see it: its object type (15 PC, 16 NPC), sound base, current art (armour for footsteps)
/// and the sound class of the ground tile it stands on (null when the tile has none).</summary>
public sealed record SoundCritter(int Type, int SoundEffect, uint CurrentArt = 0, int? TileSound = null);

/// <summary>Where a combat sound plays.</summary>
public enum SoundCueSource { Attacker, Target }

public sealed record PlacedSoundCue(SoundCue Cue, SoundCueSource Source);

/// <summary>How an attack ended, for its sounds.</summary>
public enum AttackSoundOutcome { Hit, CriticalHit, Miss }

/// <summary>
/// ARC-WORLD-041: the sound id rules of world events (docs/audio/sound-cues.md; checked in the 1.0.7.4 executable,
/// docs/research/audio-evidence.md SN23–SN27 and SN34). Pure functions over stored values: the host reads the fields
/// (<c>Arcanum.Data.Audio.ObjectSoundReader</c>), plays the cue on the object or location and makes the variant draw with the
/// presentation's generator, so sound never moves a game roll (the original draws from the game generator, SN33).
/// A material or tile class outside the original's table, which the original reads past, gives no cue.
/// </summary>
public static class SoundCueRules
{
    public const int PcType = 15, NpcType = 16, PortalType = 1, ContainerType = 2, WeaponType = 5, AmmoType = 6, GoldType = 8, GenericType = 14;
    /// <summary>The unknown-description number that marks gold.</summary>
    public const int GoldDescription = 9056;
    public const int PickupBase = 5950, DropBase = 5960, GoldPickup = 5958, GoldDrop = 5968, MissWithoutSound = 4010, MaterialHitBase = 7000,
        PlateFootsteps = 2900;
    /// <summary>A heavy-metal item this light or lighter sounds like light metal.</summary>
    public const int LightMetalWeight = 2000;
    public const int MaterialCount = 14;

    private enum Pick { Flesh, LightMetal, HeavyMetal, Stone, Wood, Glass, Cloth, Paper }

    // By material (stone, brick, wood, plant, flesh, metal, glass, cloth, liquid, paper, gas, force, fire, powder): the pickup
    // and drop class, and the hit class (the first five of the same list).
    private static readonly Pick[] PickClass =
    [
        Pick.Stone, Pick.Stone, Pick.Wood, Pick.Wood, Pick.Flesh, Pick.HeavyMetal, Pick.Glass, Pick.Cloth, Pick.Flesh, Pick.Paper,
        Pick.Flesh, Pick.LightMetal, Pick.Flesh, Pick.Flesh,
    ];
    private static readonly Pick[] HitClass =
    [
        Pick.Stone, Pick.Stone, Pick.Wood, Pick.Wood, Pick.Flesh, Pick.HeavyMetal, Pick.LightMetal, Pick.Flesh, Pick.Flesh, Pick.Flesh,
        Pick.Flesh, Pick.LightMetal, Pick.Flesh, Pick.Flesh,
    ];
    private static readonly int[] FootstepBase = [2904, 2912, 2916, 2920, 2928, 2932];

    /// <summary>The armour a critter's art shows: bits 20–23 of a type-2 art id, bits 20–22 of a type-12 one, else 0
    /// (3 chain, 4 and 6 plate).</summary>
    [OriginalFunction(0x504030, OriginalFunctionScope.Full)]
    public static int CritterArtArmor(uint art) => (art >> 28) switch
    {
        2 => (int)((art >> 20) & 0xF),
        12 => (int)((art >> 20) & 0x7),
        _ => 0,
    };

    /// <summary>An item's weight for its sounds: gold weighs 0; otherwise the stored weight plus the wielder's magic
    /// adjustment, and ammunition is that times its quantity / 4 (32-bit, truncating toward zero).</summary>
    /// <param name="magicAdjustment">The stored magic weight adjustment scaled by the holder's aptitude; 0 without a holder.</param>
    [OriginalFunction(0x461370, OriginalFunctionScope.Partial)]
    public static int ItemWeight(int type, int storedWeight, int ammoQuantity = 0, int magicAdjustment = 0)
    {
        if (type == GoldType) return 0;
        var weight = unchecked(storedWeight + magicAdjustment);
        return type == AmmoType ? unchecked(ammoQuantity * weight) / 4 : weight;
    }

    /// <summary>
    /// An item or weapon sound (SN23). Pickup and drop: gold 5958/5968, else 5950/5960 plus the material class, a heavy-metal
    /// item at most 2,000 heavy counting as light metal. Use: base + 0, out of ammo: base + 7, miss: base + 3 (4010 without an
    /// item or base). Hit: base + 4 or + 5, critical: base + 6; when that id is in no table, or without an item or base, the
    /// material cue <c>7000 + 20 × weapon class + 3 × target class</c> + 0 or 1, with flesh for no weapon. Nothing for a
    /// base of 0 on use and out of ammo, for pickup and drop without an item, or for a hit without a target.
    /// </summary>
    [OriginalFunction(0x4F0BF0, OriginalFunctionScope.Partial)]
    public static SoundCue? Item(ItemSoundAction action, SoundItem? item, SoundHitTarget? target = null)
    {
        var sound = item?.SoundEffect ?? 0;
        switch (action)
        {
            case ItemSoundAction.Pickup:
            case ItemSoundAction.Drop:
                if (item is null) return null;
                var pickup = action == ItemSoundAction.Pickup;
                if (item.UnknownDescription == GoldDescription) return new(pickup ? GoldPickup : GoldDrop);
                if (Class(PickClass, item) is not { } pick) return null;
                return new((pickup ? PickupBase : DropBase) + (int)pick);
            case ItemSoundAction.Use:
                return sound != 0 ? new(sound) : null;
            case ItemSoundAction.OutOfAmmo:
                return sound != 0 ? new(unchecked(sound + 7)) : null;
            case ItemSoundAction.Miss:
                return item is not null && sound != 0 ? new(unchecked(sound + 3)) : new(MissWithoutSound);
            case ItemSoundAction.Hit:
            case ItemSoundAction.CriticalHit:
                if (target is null) return null;
                var material = MaterialHit(item, target);
                if (sound == 0) return material;
                return action == ItemSoundAction.CriticalHit ? new(unchecked(sound + 6), 1, material) : new(unchecked(sound + 4), 2, material);
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <summary>
    /// A critter sound (SN24): nothing unless <paramref name="critter"/> is a PC or NPC and the local PC is present and on.
    /// Offsets 0..6 from its base; footsteps 2900 in plate armour, else by the tile's class (2904 dirt, 2912 sand, 2916 snow,
    /// 2920 stone, 2928 water, 2932 wood; + 4 in chain armour on dirt, stone or wood), each with four variants.
    /// </summary>
    [OriginalFunction(0x4F0ED0, OriginalFunctionScope.Partial)]
    public static SoundCue? Critter(CritterSoundAction action, SoundCritter critter, SoundLocalPc localPc)
    {
        ArgumentNullException.ThrowIfNull(critter);
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (critter.Type is not (PcType or NpcType) || localPc != SoundLocalPc.Present) return null;
        if (action != CritterSoundAction.Footsteps) return new(unchecked(critter.SoundEffect + (int)action));
        var armor = CritterArtArmor(critter.CurrentArt);
        if (armor is 4 or 6) return new(PlateFootsteps, 4);
        if (critter.TileSound is not { } tile || tile is < 0 or > 5) return null;
        var chain = armor == 3 && (TileSoundClass)tile is TileSoundClass.Dirt or TileSoundClass.Stone or TileSoundClass.Wood;
        return new(FootstepBase[tile] + (chain ? 4 : 0), 4);
    }

    /// <summary>A container sound (SN25): base + 0 open, + 1 close, + 2 locked; nothing for another type.</summary>
    [OriginalFunction(0x4F0FD0, OriginalFunctionScope.Full)]
    public static SoundCue? Container(int type, int soundEffect, OpenableSoundAction action) =>
        type == ContainerType ? new(unchecked(soundEffect + (int)Defined(action))) : null;

    /// <summary>A portal sound (SN25): base + 0 open, + 1 close, + 2 locked; nothing for another type.</summary>
    [OriginalFunction(0x4F1010, OriginalFunctionScope.Full)]
    public static SoundCue? Portal(int type, int soundEffect, OpenableSoundAction action) =>
        type == PortalType ? new(unchecked(soundEffect + (int)Defined(action))) : null;

    /// <summary>
    /// A wall, scenery, projectile, weapon or trap sound (SN25): busting base + 0 (weapon + 1), destroying base + 1 (weapon + 2),
    /// animating base + 2; nothing for a base of 0 or another type. The original drops a busting or destroying id no table
    /// holds, which plays nothing either way.
    /// </summary>
    [OriginalFunction(0x4F1050, OriginalFunctionScope.Full)]
    public static SoundCue? Misc(int type, int soundEffect, MiscSoundAction action)
    {
        Defined(action);
        var sounding = type is not (PcType or NpcType or ContainerType or PortalType) && (type is < WeaponType or > GenericType || type == WeaponType);
        if (!sounding || soundEffect == 0) return null;
        var offset = action switch
        {
            MiscSoundAction.Busting => type == WeaponType ? 1 : 0,
            MiscSoundAction.Destroying => type == WeaponType ? 2 : 1,
            _ => 2,
        };
        return new(unchecked(soundEffect + offset));
    }

    /// <summary>The sound when an attack starts (SN27): out of ammunition, or the weapon's use sound, on the attacker.</summary>
    public static PlacedSoundCue? AttackStart(bool outOfAmmo, SoundItem? weapon) =>
        Item(outOfAmmo ? ItemSoundAction.OutOfAmmo : ItemSoundAction.Use, weapon) is { } cue ? new(cue, SoundCueSource.Attacker) : null;

    /// <summary>
    /// The sounds of an attack's result, in order (SN27): a critical hit plays the target's "critically hit" sound on the
    /// target, then the weapon's critical sound on the attacker; a hit the weapon's hit sound and a miss its miss sound, on the
    /// attacker. <paramref name="target"/> is the critter attacked (null for an object), <paramref name="hit"/> what the hit lands on.
    /// </summary>
    public static IReadOnlyList<PlacedSoundCue> AttackResult(AttackSoundOutcome outcome, SoundItem? weapon, SoundCritter? target, SoundHitTarget? hit,
        SoundLocalPc localPc)
    {
        var cues = new List<PlacedSoundCue>();
        void Add(SoundCue? cue, SoundCueSource source) { if (cue is not null) cues.Add(new(cue, source)); }
        switch (outcome)
        {
            case AttackSoundOutcome.CriticalHit:
                if (target is not null) Add(Critter(CritterSoundAction.CriticallyHit, target, localPc), SoundCueSource.Target);
                Add(Item(ItemSoundAction.CriticalHit, weapon, hit), SoundCueSource.Attacker);
                break;
            case AttackSoundOutcome.Hit:
                Add(Item(ItemSoundAction.Hit, weapon, hit), SoundCueSource.Attacker);
                break;
            case AttackSoundOutcome.Miss:
                Add(Item(ItemSoundAction.Miss, weapon), SoundCueSource.Attacker);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        return cues.AsReadOnly();
    }

    /// <summary>Footsteps sound on frames 3 and 8 of a walk or run of a critter that is not monstrous (SN26); sneaking and
    /// knock-back are silent.</summary>
    public static bool FootstepFrame(int frame) => frame is 3 or 8;

    private static SoundCue? MaterialHit(SoundItem? weapon, SoundHitTarget target)
    {
        if (target.Material is < 0 or >= MaterialCount) return null;
        var targetClass = HitClass[target.Material];
        var weaponClass = Pick.Flesh;
        if (weapon is not null)
        {
            if (Class(HitClass, weapon) is not { } w) return null;
            weaponClass = w;
        }
        return new(MaterialHitBase + 20 * (int)weaponClass + 3 * (int)targetClass, 2);
    }

    private static Pick? Class(Pick[] table, SoundItem item)
    {
        if (item.Material is < 0 or >= MaterialCount) return null;
        var pick = table[item.Material];
        return pick == Pick.HeavyMetal && item.Weight <= LightMetalWeight ? Pick.LightMetal : pick;
    }

    private static T Defined<T>(T value) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
