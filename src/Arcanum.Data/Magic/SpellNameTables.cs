namespace Arcanum.Data.Magic;

/// <summary>The kind of a spell for the AI and the spell UI (<c>Info:</c>); the value is the original's flag bits (1 friendly, 2 aggressive).</summary>
public enum SpellInfo
{
    Neutral = 0,
    Friendly = 1,
    Aggressive = 2,
    MayBeAggressive = 3,
}

/// <summary>The five actions of a spell run, in the order the original keeps their per-action data.</summary>
public enum SpellAction
{
    Begin,
    Maintain,
    End,
    Callback,
    EndCallback,
}

/// <summary>
/// ARC-MAGIC-001: the name tables the original's spell-record parse matches values against (read in the 1.0.7.4 executable,
/// ledger "Spell records"). Names are compared ASCII case-insensitively. A flag name stands for the bit of its index unless a
/// table of values says otherwise (<see cref="ItemTriggerValues"/>).
/// </summary>
public static class SpellNameTables
{
    /// <summary>The 32 object spell flags (<c>Cancels_SF:</c>, <c>Cancels_EnvSF:</c>, <c>Disallowed_SF:</c>, <c>Disallowed_TSF:</c>).</summary>
    public static IReadOnlyList<string> SpellFlags { get; } = Array.AsReadOnly(new[]
    {
        "OSF_INVISIBLE", "OSF_FLOATING", "OSF_BODY_OF_AIR", "OSF_BODY_OF_EARTH", "OSF_BODY_OF_FIRE", "OSF_BODY_OF_WATER",
        "OSF_DETECTING_MAGIC", "OSF_DETECTING_ALIGNMENT", "OSF_DETECTING_TRAPS", "OSF_DETECTING_INVISIBLE", "OSF_SHIELDED",
        "OSF_ANTI_MAGIC_SHELL", "OSF_BONDS_OF_MAGIC", "OSF_FULL_REFLECTION", "OSF_SUMMONED", "OSF_ILLUSION", "OSF_STONED",
        "OSF_POLYMORPHED", "OSF_MIRRORED", "OSF_SHRUNK", "OSF_PASSWALLED", "OSF_WATER_WALKING", "OSF_MAGNETIC_INVERSION",
        "OSF_CHARMED", "OSF_ENTANGLED", "OSF_SPOKEN_WITH_DEAD", "OSF_TEMPUS_FUGIT", "OSF_MIND_CONTROLLED", "OSF_DRUNK",
        "OSF_ENSHROUDED", "OSF_FAMILIAR", "OSF_HARDENED_HANDS",
    });

    /// <summary>The 32 critter flags (<c>Disallowed_TCF:</c>).</summary>
    public static IReadOnlyList<string> CritterFlags { get; } = Array.AsReadOnly(new[]
    {
        "OCF_IS_CONCEALED", "OCF_MOVING_SILENTLY", "OCF_UNDEAD", "OCF_ANIMAL", "OCF_FLEEING", "OCF_STUNNED", "OCF_PARALYZED",
        "OCF_BLINDED", "OCF_CRIPPLED_ARMS_ONE", "OCF_CRIPPLED_ARMS_BOTH", "OCF_CRIPPLED_LEGS_BOTH", "OCF_UNUSED", "OCF_SLEEPING",
        "OCF_MUTE", "OCF_SURRENDERED", "OCF_MONSTER", "OCF_SPELL_FLEE", "OCF_ENCOUNTER", "OCF_COMBAT_MODE_ACTIVE", "OCF_LIGHT_SMALL",
        "OCF_LIGHT_MEDIUM", "OCF_LIGHT_LARGE", "OCF_LIGHT_XLARGE", "OCF_UNREVIVIFIABLE", "OCF_UNRESSURECTABLE", "OCF_DEMON",
        "OCF_FATIGUE_IMMUNE", "OCF_NO_FLEE", "OCF_NON_LETHAL_COMBAT", "OCF_MECHANICAL", "OCF_ANIMAL_ENSHROUD", "OCF_FATIGUE_LIMITING",
    });

    /// <summary>The 26 item trigger names (<c>ItemTriggers:</c>); each stands for the value of the same index in
    /// <see cref="ItemTriggerValues"/>.</summary>
    public static IReadOnlyList<string> ItemTriggers { get; } = Array.AsReadOnly(new[]
    {
        "None", "User_Activate", "Wear", "Unwear", "Pickup", "Drop", "Parent_Hit", "Parent_Stunned", "Parent_Going_Unconscious",
        "Parent_Dying", "Parent_Hit_By_Atk_Spell", "Parent_Atks_Opponent", "Parent_Atks_Location", "Parent_Dmgs_Opponent",
        "Parent_Dmgs_Opponent_W_Item", "Target_Hit", "Target_Going_Unconscious", "Item_Used", "Target_Attacker",
        "Target_Attacker_Weapon", "Target_Attacker_Armor", "Target_Attacker_Weapon_Melee", "Random_Chance_Rare",
        "Random_Chance_Uncommon", "Random_Chance_Common", "Random_Chance_Frequent",
    });

    /// <summary>The trigger bits of <see cref="ItemTriggers"/>; <c>None</c> is 0 and <c>Item_Used</c> two bits.</summary>
    public static IReadOnlyList<uint> ItemTriggerValues { get; } = Array.AsReadOnly(new uint[]
    {
        0, 0x10000000, 0x20000000, 0x40000000, 0x80000000, 0x1000000, 0x2000000, 0x4000000, 0x8000000, 0x100000, 0x200000,
        0x400000, 0x800000, 0x10000, 0x20000, 0x40000, 0x80000, 0x3000, 0x2000, 0x4000, 0x8000, 0x100, 0x200, 0x400, 0x800, 0x10,
    });

    /// <summary>
    /// The stat keys <c>Resist:</c> names (index = the stored stat number). The original's table has 28 entries whose last
    /// five are null; a name that matches none of the 23 crashes it. Entries 21 and 22 carry the names of the poison level and
    /// the age while the stats at those numbers are others (LM17); which stat a number means at run time is the resolver's.
    /// </summary>
    public static IReadOnlyList<string> StatKeys { get; } = Array.AsReadOnly(new[]
    {
        "stat_strength", "stat_dexterity", "stat_constitution", "stat_beauty", "stat_intelligence", "stat_perception",
        "stat_willpower", "stat_charisma", "stat_carry_weight", "stat_damage_bonus", "stat_ac_adjustment", "stat_speed",
        "stat_heal_rate", "stat_poison_recovery_rate", "stat_reaction_modifier", "stat_max_followers", "stat_magic_tech_aptitude",
        "stat_level", "stat_experience_points", "stat_alignment", "stat_fate_points", "stat_poison_level", "stat_age",
    });

    /// <summary>The <c>Info:</c> names; the index is the <see cref="SpellInfo"/> value.</summary>
    public static IReadOnlyList<string> Info { get; } = Array.AsReadOnly(new[] { "neutral", "friendly", "aggressive", "may_be_aggressive" });

    /// <summary>The ten <c>AI_*</c> keys of line +4 in their order; <c>AI_Defensive:</c> (index 2) is a pair.</summary>
    public static IReadOnlyList<string> AiKeys { get; } = Array.AsReadOnly(new[]
    {
        "AI_Flee:", "AI_Summon:", "AI_Defensive:", "AI_Offensive:", "AI_HealingLight:", "AI_HealingMedium:", "AI_HealingHeavy:",
        "AI_CurePoison:", "AI_FatigueRecover:", "AI_Resurrect:",
    });
}
