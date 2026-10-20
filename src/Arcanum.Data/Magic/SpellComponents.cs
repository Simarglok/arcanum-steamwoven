namespace Arcanum.Data.Magic;

/// <summary>The 25 effect component types, in the order of their <c>Type:</c> names (<c>rules\magictech.mes</c> 10..34); the
/// original's component parse switches on this index.</summary>
public enum SpellComponentType
{
    NoOp, AGoal, AGoalTerminate, AIRedirect, Cast, ChargeNBranch, Damage, Destroy, Dispel, Effect, EnvFlag, EyeCandy, Heal,
    Identify, Interrupt, Movement, ObjFlag, Recharge, Summon, Terminate, TestNBranch, Trait, TraitIdx, Trait64, Use,
}

/// <summary>
/// ARC-MAGIC-002: a target area: the 64-bit target mask (OR of <see cref="SpellComponentTables.TargetValues"/>), the spell
/// flags a target must have (<c>AoE_SF:</c>) and must not have (<c>AoE_NO_SF:</c>), the radius and the count. An action's
/// area comes from line +0 of its record; a component's from its own line.
/// </summary>
public sealed record SpellArea(ulong Targets, uint RequiredSpellFlags, uint ExcludedSpellFlags, int Radius, int Count);

/// <summary>The typed data of one component type; the types without data (NoOp, Destroy, Dispel, Identify, Terminate, Use)
/// have none.</summary>
public abstract record SpellComponentData;

/// <summary>AGoal: an animation goal value (<see cref="SpellComponentTables.AnimationGoalValues"/>) and <c>SubGoal:</c> (0).</summary>
public sealed record AnimationGoalData(int Goal, int SubGoal) : SpellComponentData;

/// <summary>AGoalTerminate: the animation goal value whose goals end.</summary>
public sealed record AnimationGoalTerminateData(int Goal) : SpellComponentData;

/// <summary>AIRedirect: one critter-flag bit and <c>MinIQ:</c> (−1 when absent).</summary>
public sealed record AiRedirectData(uint CritterFlag, int MinimumIntelligence) : SpellComponentData;

/// <summary>Cast (<c>Spell:</c>) and Interrupt (<c>MagicTech:</c>): a spell number, 10000 when the key is absent (the original
/// logs an error).</summary>
public sealed record SpellReferenceData(int Spell) : SpellComponentData;

/// <summary>ChargeNBranch: <c>Cost:</c> (0) and <c>Branch:</c> (−1).</summary>
public sealed record ChargeBranchData(int Cost, int Branch) : SpellComponentData;

/// <summary>Damage and Heal: <c>DmgType:</c> index (0 normal when absent), <c>Dmg:</c> minimum and maximum (0, 0 when
/// absent), <c>Dmg_Flags:</c> bits (<see cref="SpellComponentTables.DamageFlagValues"/>).</summary>
public sealed record DamageData(int DamageType, int Minimum, int Maximum, uint Flags) : SpellComponentData;

/// <summary>Effect: the effect number, add or remove, <c>Count:</c> (1), <c>Cause:</c> index (6, spell) and <c>Scaled:</c> (0).</summary>
public sealed record EffectData(int Effect, bool Add, int Count, int Cause, int Scaled) : SpellComponentData;

/// <summary>EnvFlag: one spell-flag bit and on or off.</summary>
public sealed record EnvironmentFlagData(uint SpellFlag, bool On) : SpellComponentData;

/// <summary>EyeCandy: the eye-candy number, add or remove, <c>Play:</c> bits (<see cref="SpellComponentTables.PlayValues"/>).</summary>
public sealed record EyeCandyData(int EyeCandy, bool Add, uint Play) : SpellComponentData;

/// <summary>Movement: <c>Move_Location:</c> index (0 when absent) and <c>Tile_Radius:</c>, read only for index 1 (null otherwise).</summary>
public sealed record MovementData(int Location, int? TileRadius) : SpellComponentData;

/// <summary>ObjFlag: the object field number (<see cref="SpellComponentTables.ObjectFlagFieldIds"/>), one flag bit of it and on or off.</summary>
public sealed record ObjectFlagData(int Field, uint Flag, bool On) : SpellComponentData;

/// <summary>Recharge: the amount and <c>Max:</c> (9999).</summary>
public sealed record RechargeData(int Amount, int Maximum) : SpellComponentData;

/// <summary>Summon: <c>Proto:</c> (0 when absent: no prototype; the original resolves a positive number to its prototype at
/// load), <c>Clear_Faction:</c> (0), <c>Palette:</c> (0), <c>List:</c> (−1).</summary>
public sealed record SummonData(int Prototype, int ClearFaction, int Palette, int List) : SpellComponentData;

/// <summary>TestNBranch: the tested field value (<see cref="SpellComponentTables.TestFieldValues"/>), the comparison index,
/// <c>TestVal:</c> (0) and <c>Branch:</c> (−1).</summary>
public sealed record TestBranchData(int Field, int Comparison, int Value, int Branch) : SpellComponentData;

/// <summary>Trait: the field value, the value set and <c>Palette:</c> (0).</summary>
public sealed record TraitData(int Field, int Value, int Palette) : SpellComponentData;

/// <summary>TraitIdx: the indexed field number (<see cref="SpellComponentTables.IndexedFieldIds"/>), the index in it and the value.</summary>
public sealed record TraitIndexData(int Field, int Index, int Value) : SpellComponentData;

/// <summary>Trait64: the field value and the source index (<see cref="SpellComponentTables.Trait64Sources"/>).</summary>
public sealed record Trait64Data(int Field, int Source) : SpellComponentData;

/// <summary>
/// ARC-MAGIC-002: one effect component of a spell record (a component line, +5 onward): its action, area, <c>Apply_AoE:</c>
/// mask, <c>ItemTriggers:</c> bits, type and typed data (null for the types without data).
/// </summary>
public sealed record SpellComponent(SpellAction Action, SpellComponentType Type, SpellArea Area, ulong ApplyTargets, uint ItemTriggers,
    SpellComponentData? Data);

/// <summary>
/// ARC-MAGIC-002: the name tables of the target areas and the component types (read in the 1.0.7.4 executable, ledger
/// "Spell components"). Names are compared ASCII case-insensitively; a null entry is a hole in the original's table (a name
/// that matches nothing reaches it and crashes the original).
/// </summary>
public static class SpellComponentTables
{
    /// <summary>The target name <c>Tgt_Obj_No_ST_Critter_Dead</c>: its value lacks the critter bit (0x40) its siblings carry (MG18).</summary>
    public const int NotDeadCritterTarget = 32;
    /// <summary>The critter type bit of the target values.</summary>
    public const ulong CritterTargetBit = 0x40;
    /// <summary>The bit line +0 adds to every action area except records 1, 2 and 9 (the high word's 0x2000, the
    /// <c>Tgt_Obj_No_Invulnerable</c> test).</summary>
    public const ulong NotInvulnerableBit = 0x2000UL << 32;
    /// <summary>The object field number of <c>obj_f_spell_flags</c>: an ObjFlag turning one of its bits on records the bit in
    /// <see cref="SpellRecord.SpellFlagsSet"/>.</summary>
    public const int SpellFlagsField = 20;

    /// <summary>The five action names a component line starts with, in <see cref="SpellAction"/> order.</summary>
    public static IReadOnlyList<string> Actions { get; } = Array.AsReadOnly(new[] { "[Begin]", "[Maintain]", "[End]", "[Callback]", "[EndCallback]" });

    public static IReadOnlyList<string> Targets { get; } = Array.AsReadOnly(new[]
    {
        "Tgt_None", "Tgt_Self", "Tgt_Source", "Tgt_Object", "Tgt_Obj_Self", "Tgt_Obj_Radius", "Tgt_Obj_T_PC",
        "Tgt_Obj_T_Critter", "Tgt_Obj_ST_Critter_Animal", "Tgt_Obj_ST_Critter_Dead", "Tgt_Obj_ST_Critter_Undead",
        "Tgt_Obj_ST_Critter_Demon", "Tgt_Obj_ST_Critter_Mechanical", "Tgt_Obj_ST_Critter_Good", "Tgt_Obj_ST_Critter_Evil",
        "Tgt_Obj_ST_Critter_Unrevivifiable", "Tgt_Obj_ST_Critter_Unresurrectable", "Tgt_Obj_T_Portal", "Tgt_Obj_T_Container",
        "Tgt_Obj_ST_Openable_Locked", "Tgt_Obj_T_Wall", "Tgt_Obj_Damaged", "Tgt_Obj_Damaged_Poisoned", "Tgt_Obj_Poisoned",
        "Tgt_Obj_M_Stone", "Tgt_Obj_M_Flesh", "Tgt_Obj_Inven", "Tgt_Obj_Weight_Below_5", "Tgt_Obj_In_Wall", "Tgt_Obj_No_Self",
        "Tgt_Obj_No_T_PC", "Tgt_Obj_No_ST_Critter_Animal", "Tgt_Obj_No_ST_Critter_Dead", "Tgt_Obj_No_ST_Critter_Undead",
        "Tgt_Obj_No_ST_Critter_Demon", "Tgt_Obj_No_ST_Critter_Mechanical", "Tgt_Obj_No_ST_Critter_Good",
        "Tgt_Obj_No_ST_Critter_Evil", "Tgt_Obj_No_ST_Critter_Unrevivifiable", "Tgt_Obj_No_ST_Critter_Unresurrectable",
        "Tgt_Obj_No_ST_Openable_Locked", "Tgt_Obj_No_ST_Magically_Held", "Tgt_Obj_No_T_Wall", "Tgt_Obj_No_Damaged",
        "Tgt_Obj_No_M_Stone", "Tgt_Obj_No_Inven", "Tgt_Obj_No_Invulnerable", "Tgt_Summoned", "Tgt_Tile", "Tgt_Tile_Self",
        "Tgt_Tile_Pathable_To", "Tgt_Tile_Empty", "Tgt_Tile_Empty_Immobiles", "Tgt_Tile_No_Empty", "Tgt_Tile_Radius",
        "Tgt_Tile_Radius_Wall", "Tgt_Tile_Offscreen", "Tgt_Tile_Indoor_Or_Outdoor_Match", "Tgt_Cone", "Tgt_All_Party_Critters",
        "Tgt_Party_Critter", "Tgt_Non_Party_Critters", "Tgt_Parent_Obj", "Tgt_Attacker_Obj", "Tgt_List",
    });

    public static IReadOnlyList<ulong> TargetValues { get; } = Array.AsReadOnly(new ulong[]
    {
        0x0UL, 0x1UL, 0x2UL, 0x4UL, 0xCUL, 0x10UL, 0x24UL, 0x44UL, 0xC4UL, 0x144UL, 0x244UL, 0x444UL, 0x844UL, 0x1044UL,
        0x2044UL, 0x4044UL, 0x8044UL, 0x10004UL, 0x20004UL, 0x40004UL, 0x80004UL, 0x100004UL, 0x200004UL, 0x400004UL,
        0x800004UL, 0x1000004UL, 0x2000004UL, 0x4000004UL, 0x8000004UL, 0x10000004UL, 0x20000004UL, 0x40000044UL, 0x80000004UL,
        0x100000044UL, 0x200000044UL, 0x400000044UL, 0x800000044UL, 0x1000000044UL, 0x2000000044UL, 0x4000000044UL,
        0x8000000004UL, 0x10000000004UL, 0x20000000004UL, 0x40000000004UL, 0x80000000004UL, 0x100000000004UL, 0x200000000004UL,
        0x400000000004UL, 0x800000000000UL, 0x1000000000000UL, 0x2800000000000UL, 0x4800000000000UL, 0x8800000000000UL,
        0x10800000000000UL, 0x20800000000000UL, 0x40800000000000UL, 0x80800000000000UL, 0x100800000000000UL,
        0x200000000000000UL, 0x400000000000044UL, 0x800000000000044UL, 0x1000000000000004UL, 0x2000000000000004UL,
        0x4000000000000004UL, 0x8000000000000000UL,
    });

    public static IReadOnlyList<string> ObjectFlagFields { get; } = Array.AsReadOnly(new[]
    {
        "obj_f_flags", "obj_f_spell_flags", "obj_f_wall_flags", "obj_f_portal_flags", "obj_f_container_flags",
        "obj_f_scenery_flags", "obj_f_item_flags", "obj_f_weapon_flags", "obj_f_ammo_flags", "obj_f_armor_flags",
        "obj_f_gold_flags", "obj_f_food_flags", "obj_f_scroll_flags", "obj_f_key_ring_flags", "obj_f_written_flags",
        "obj_f_generic_flags", "obj_f_critter_flags", "obj_f_critter_flags2", "obj_f_pc_flags", "obj_f_npc_flags",
        "obj_f_trap_flags",
    });

    public static IReadOnlyList<int> ObjectFlagFieldIds { get; } = Array.AsReadOnly(new[] { 19, 20, 39, 46, 56, 69, 87, 112, 141, 150, 164, 172, 179, 193, 201, 211, 218, 219, 253, 280, 307 });

    public static IReadOnlyList<IReadOnlyList<string?>> ObjectFlagNames { get; } = Array.AsReadOnly(new IReadOnlyList<string?>[]
    {
        // obj_f_flags
        Array.AsReadOnly(new string?[]
        {
            "OF_DESTROYED", "OF_OFF", "OF_FLAT", "OF_TEXT", "OF_SEE_THROUGH", "OF_SHOOT_THROUGH", "OF_TRANSLUCENT", "OF_SHRUNK",
            "OF_DONTDRAW", "OF_INVISIBLE", "OF_NO_BLOCK", "OF_CLICK_THROUGH", "OF_INVENTORY", "OF_DYNAMIC", "OF_PROVIDES_COVER",
            "OF_HAS_OVERLAYS", "OF_HAS_UNDERLAYS", "OF_WADING", "OF_WATER_WALKING", "OF_STONED", "OF_DONTLIGHT", "OF_TEXT_FLOATER",
            "OF_INVULNERABLE", "OF_EXTINCT", "OF_TRAP_PC", "OF_TRAP_SPOTTED", "OF_DISALLOW_WADING", "OF_MULTIPLAYER_LOCK",
            "OF_FROZEN", "OF_ANIMATED_DEAD", "OF_TELEPORTED",
        }),
        // obj_f_spell_flags
        Array.AsReadOnly(new string?[]
        {
            "OSF_INVISIBLE", "OSF_FLOATING", "OSF_BODY_OF_AIR", "OSF_BODY_OF_EARTH", "OSF_BODY_OF_FIRE", "OSF_BODY_OF_WATER",
            "OSF_DETECTING_MAGIC", "OSF_DETECTING_ALIGNMENT", "OSF_DETECTING_TRAPS", "OSF_DETECTING_INVISIBLE", "OSF_SHIELDED",
            "OSF_ANTI_MAGIC_SHELL", "OSF_BONDS_OF_MAGIC", "OSF_FULL_REFLECTION", "OSF_SUMMONED", "OSF_ILLUSION", "OSF_STONED",
            "OSF_POLYMORPHED", "OSF_MIRRORED", "OSF_SHRUNK", "OSF_PASSWALLED", "OSF_WATER_WALKING", "OSF_MAGNETIC_INVERSION",
            "OSF_CHARMED", "OSF_ENTANGLED", "OSF_SPOKEN_WITH_DEAD", "OSF_TEMPUS_FUGIT", "OSF_MIND_CONTROLLED", "OSF_DRUNK",
            "OSF_ENSHROUDED", "OSF_FAMILIAR", "OSF_HARDENED_HANDS",
        }),
        // obj_f_wall_flags
        Array.AsReadOnly(new string?[]
        {
            "OWAF_TRANS_DISALLOW", "OWAF_TRANS_LEFT", "OWAF_TRANS_RIGHT", "OWAF_TRANS_ALL",
        }),
        // obj_f_portal_flags
        Array.AsReadOnly(new string?[]
        {
            "OPF_LOCKED", "OPF_JAMMED", "OPF_MAGICALLY_HELD", "OPF_NEVER_LOCKED", "OPF_ALWAYS_LOCKED", "OPF_LOCKED_DAY",
            "OPF_LOCKED_NIGHT", "OPF_BUSTED", "OPF_STICKY",
        }),
        // obj_f_container_flags
        Array.AsReadOnly(new string?[]
        {
            "OCOF_LOCKED", "OCOF_JAMMED", "OCOF_MAGICALLY_HELD", "OCOF_NEVER_LOCKED", "OCOF_ALWAYS_LOCKED", "OCOF_LOCKED_DAY",
            "OCOF_LOCKED_NIGHT", "OCOF_BUSTED", "OCOF_STICKY",
        }),
        // obj_f_scenery_flags
        Array.AsReadOnly(new string?[]
        {
            "OSCF_NO_AUTO_ANIMATE", "OSCF_BUSTED", "OSCF_NOCTURNAL", "OSCF_MARKS_TOWNMAP", "OSCF_IS_FIRE", "OSCF_RESPAWNABLE",
            "OSCF_SOUND_SMALL", "OSCF_SOUND_MEDIUM", "OSCF_SOUND_EXTRA_LARGE",
        }),
        // obj_f_item_flags
        Array.AsReadOnly(new string?[]
        {
            "OIF_IDENTIFIED", "OIF_WONT_SELL", "OIF_IS_MAGICAL", "OIF_TRANSFER_LIGHT", "OIF_NO_DISPLAY", "OIF_NO_DROP",
            "OIF_HEXED", "OIF_CAN_USE_BOX", "OIF_NEEDS_TARGET", "OIF_LIGHT_SMALL", "OIF_LIGHT_MEDIUM", "OIF_LIGHT_LARGE",
            "OIF_LIGHT_XLARGE", "OIF_PERSISTENT", "OIF_MT_TRIGGERED", "OIF_STOLEN", "OIF_USE_IS_THROW", "OIF_NO_DECAY", "OIF_UBER",
            "OIF_NO_NPC_PICKUP", "OIF_NO_RANGED_USE", "OIF_VALID_AI_ACTION", "OIF_MP_INSERTED",
        }),
        // obj_f_weapon_flags
        Array.AsReadOnly(new string?[]
        {
            "OWF_LOUD", "OWF_SILENT", "OWF_TWO_HANDED", "OWF_HAND_COUNT_FIXED", "OWF_THROWABLE", "OWF_TRANS_PROJECTILE",
            "OWF_BOOMERANGS", "OWF_IGNORE_RESISTANCE", "OWF_DAMAGE_ARMOR", "OWF_DEFAULT_THROWS",
        }),
        // obj_f_ammo_flags
        Array.AsReadOnly(new string?[]
        {
            "OAF_NONE",
        }),
        // obj_f_armor_flags
        Array.AsReadOnly(new string?[]
        {
            "OARF_SIZE_SMALL", "OARF_SIZE_MEDIUM", "OARF_SIZE_LARGE", "OARF_MALE_ONLY", "OARF_FEMALE_ONLY",
        }),
        // obj_f_gold_flags
        Array.AsReadOnly(new string?[]
        {
            "OGOF_NONE",
        }),
        // obj_f_food_flags
        Array.AsReadOnly(new string?[]
        {
            "OFF_NONE",
        }),
        // obj_f_scroll_flags
        Array.AsReadOnly(new string?[]
        {
            "OSRF_NONE",
        }),
        // obj_f_key_ring_flags
        Array.AsReadOnly(new string?[]
        {
            "OKRF_PRIMARY_RING",
        }),
        // obj_f_written_flags
        Array.AsReadOnly(new string?[]
        {
            "OWRF_NONE",
        }),
        // obj_f_generic_flags
        Array.AsReadOnly(new string?[]
        {
            "OGF_USES_TORCH_SHIELD_LOCATION", "OGF_IS_LOCKPICK", "OGF_IS_TRAP_DEVICE", null, null,
        }),
        // obj_f_critter_flags
        Array.AsReadOnly(new string?[]
        {
            "OCF_IS_CONCEALED", "OCF_MOVING_SILENTLY", "OCF_UNDEAD", "OCF_ANIMAL", "OCF_FLEEING", "OCF_STUNNED", "OCF_PARALYZED",
            "OCF_BLINDED", "OCF_CRIPPLED_ARMS_ONE", "OCF_CRIPPLED_ARMS_BOTH", "OCF_CRIPPLED_LEGS_BOTH", "OCF_UNUSED",
            "OCF_SLEEPING", "OCF_MUTE", "OCF_SURRENDERED", "OCF_MONSTER", "OCF_SPELL_FLEE", "OCF_ENCOUNTER",
            "OCF_COMBAT_MODE_ACTIVE", "OCF_LIGHT_SMALL", "OCF_LIGHT_MEDIUM", "OCF_LIGHT_LARGE", "OCF_LIGHT_XLARGE",
            "OCF_UNREVIVIFIABLE", "OCF_UNRESSURECTABLE", "OCF_DEMON", "OCF_FATIGUE_IMMUNE", "OCF_NO_FLEE", "OCF_NON_LETHAL_COMBAT",
            "OCF_MECHANICAL", "OCF_ANIMAL_ENSHROUD", "OCF_FATIGUE_LIMITING",
        }),
        // obj_f_critter_flags2
        Array.AsReadOnly(new string?[]
        {
            "OCF2_ITEM_STOLEN", "OCF2_AUTO_ANIMATES", "OCF2_USING_BOOMERANG", "OCF2_FATIGUE_DRAINING", "OCF2_SLOW_PARTY",
            "OCF2_COMBAT_TOGGLE_FX", "OCF2_NO_DECAY", "OCF2_NO_PICKPOCKET", "OCF2_NO_BLOOD_SPLOTCHES", "OCF2_NIGH_INVULNERABLE",
            "OCF2_ELEMENTAL", "OCF2_DARK_SIGHT", "OCF2_NO_SLIP", "OCF2_NO_DISINTEGRATE", "OCF2_REACTION_0", "OCF2_REACTION_1",
            "OCF2_REACTION_2", "OCF2_REACTION_3", "OCF2_REACTION_4", "OCF2_REACTION_5", "OCF2_REACTION_6", "OCF2_TARGET_LOCK",
            "OCF2_PERMA_POLYMORPH", "OCF2_SAFE_OFF", "OCF2_CHECK_REACTION_BAD", "OCF2_CHECK_ALIGN_GOOD", "OCF2_CHECK_ALIGN_BAD",
        }),
        // obj_f_pc_flags
        Array.AsReadOnly(new string?[]
        {
            "OPCF_unused_1", "OPCF_unused_2", "OPCF_USE_ALT_DATA", "OPCF_unused_4", "OPCF_unused_5", "OPCF_FOLLOWER_SKILLS_ON",
        }),
        // obj_f_npc_flags
        Array.AsReadOnly(new string?[]
        {
            "ONF_FIGHTING", "ONF_WAYPOINTS_DAY", "ONF_WAYPOINTS_NIGHT", "ONF_AI_WAIT_HERE", "ONF_AI_SPREAD_OUT", "ONF_JILTED",
            "ONF_CHECK_WIELD", "ONF_CHECK_WEAPON", "ONF_KOS", "ONF_WAYPOINTS_BED", "ONF_FORCED_FOLLOWER", "ONF_KOS_OVERRIDE",
            "ONF_WANDERS", "ONF_WANDERS_IN_DARK", "ONF_FENCE", "ONF_FAMILIAR", "ONF_CHECK_LEADER", "ONF_ALOOF", "ONF_CAST_HIGHEST",
            "ONF_GENERATOR", "ONF_GENERATED", "ONF_GENERATOR_RATE1", "ONF_GENERATOR_RATE2", "ONF_GENERATOR_RATE3",
            "ONF_DEMAINTAIN_SPELLS", "ONF_LOOK_FOR_WEAPON", "ONF_LOOK_FOR_ARMOR", "ONF_LOOK_FOR_AMMO", "ONF_BACKING_OFF",
            "ONF_NO_ATTACK", null,
        }),
        // obj_f_trap_flags
        Array.AsReadOnly(new string?[]
        {
            "OTF_BUSTED",
        }),
    });


    public static IReadOnlyList<string> AnimationGoals { get; } = Array.AsReadOnly(new[]
    {
        "anim_goal_animate", "anim_goal_knockback", "anim_goal_follow", "anim_goal_floating", "anim_goal_destroy_obj", "anim_goal_knock_down",
    });
    public static IReadOnlyList<int> AnimationGoalValues { get; } = Array.AsReadOnly(new[] { 0, 43, 24, 44, 32, 62 });

    /// <summary><c>DmgType:</c> names; Heal accepts the first five only.</summary>
    public static IReadOnlyList<string> DamageTypes { get; } = Array.AsReadOnly(new[] { "Dmg_Normal", "Dmg_Poison", "Dmg_Electrical", "Dmg_Fire", "Dmg_Magic", "Dmg_Acid" });
    public const int HealDamageTypes = 5;

    public static IReadOnlyList<string> DamageFlags { get; } = Array.AsReadOnly(new[]
    {
        "None", "Full", "Resurrect", "Reanimate", "Death", "Stunned", "Scars", "Scaled", "Weap_Dropped", "Crippling",
    });
    public static IReadOnlyList<uint> DamageFlagValues { get; } = Array.AsReadOnly(new uint[] { 0, 0x1, 0x2, 0x400002, 0x4, 0x40, 0x1000, 0x800000, 0x2000, 0xE00 });

    /// <summary>The add-or-remove word of Effect and EyeCandy: index 1 adds.</summary>
    public static IReadOnlyList<string> AddRemove { get; } = Array.AsReadOnly(new[] { "Remove", "Add" });

    /// <summary><c>Cause:</c> names (effect causes).</summary>
    public static IReadOnlyList<string> Causes { get; } = Array.AsReadOnly(new[]
    {
        "Race", "Background", "Class", "Bless", "Curse", "Item", "Spell", "Injury", "Tech", "Gender",
    });
    public const int SpellCause = 6;

    /// <summary>The on-or-off word of EnvFlag and ObjFlag: index 1 turns on.</summary>
    public static IReadOnlyList<string> OnOff { get; } = Array.AsReadOnly(new[] { "FLAG_OFF", "FLAG_ON" });

    public static IReadOnlyList<string> Play { get; } = Array.AsReadOnly(new[]
    {
        "reverse", "stack", "destroy", "callback", "end_callback", "random_start", "fire_dmg", "check_already", "no_id", "ice_dmg",
    });
    public static IReadOnlyList<uint> PlayValues { get; } = Array.AsReadOnly(new uint[] { 0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x240 });

    public static IReadOnlyList<string> MoveLocations { get; } = Array.AsReadOnly(new[]
    {
        "Target_Tile", "Random_Tile_In_Radius_Self", "Teleport_Tile", "Teleport_Area_Portal",
    });
    public const int RandomTileInRadius = 1;

    /// <summary>The field words of TestNBranch, Trait and Trait64, and their values.</summary>
    public static IReadOnlyList<string> TestFields { get; } = Array.AsReadOnly(new[] { "art_num", "Teleport_Tile", "obj_f_critter_fleeing_from", "HP_Damage" });
    public static IReadOnlyList<int> TestFieldValues { get; } = Array.AsReadOnly(new[] { 1, 242, 230, 29 });

    public static IReadOnlyList<string> Comparisons { get; } = Array.AsReadOnly(new[] { "Equal", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual" });

    /// <summary>The indexed fields of TraitIdx, their field numbers and the name table of each (the stat keys with their five
    /// null entries, then the five resistances).</summary>
    public static IReadOnlyList<string> IndexedFields { get; } = Array.AsReadOnly(new[] { "obj_f_critter_stat_base_idx", "obj_f_resistance_idx" });
    public static IReadOnlyList<int> IndexedFieldIds { get; } = Array.AsReadOnly(new[] { 220, 31 });
    public static IReadOnlyList<IReadOnlyList<string?>> IndexedFieldNames { get; } = Array.AsReadOnly(new IReadOnlyList<string?>[]
    {
        Array.AsReadOnly([.. SpellNameTables.StatKeys, null, null, null, null, null]),
        Array.AsReadOnly(new string?[] { "resist_damage", "resist_fire", "resist_electrical", "resist_poison", "resist_magic" }),
    });

    public static IReadOnlyList<string> Trait64Sources { get; } = Array.AsReadOnly(new[] { "Target_Tile", "Caster_Obj", "Caster_Owner_Obj" });
}
