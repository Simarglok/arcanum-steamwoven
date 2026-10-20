using Arcanum.Data.Magic;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Magic.SpellRecordTableTests;

namespace Arcanum.Data.Tests.Magic;

/// <summary>ARC-MAGIC-002: target areas (line +0) and effect components (lines +5 onward) on synthetic MES tables.</summary>
[TestClass]
public sealed class SpellComponentTests
{
    private static readonly SpellRecordMode[] Modes = [SpellRecordMode.Strict, SpellRecordMode.LikeOriginal];
    private const ulong Invulnerable = SpellComponentTables.NotInvulnerableBit;

    private static int L(int spell, int offset) => SpellRecordTable.FirstMessage(spell) + offset;

    private static SpellRecordTable Read(MesFile list, SpellRecordMode mode)
    {
        var read = SpellRecordTable.Read(list, Types, mode);
        Assert.AreEqual(SpellDataStatus.Read, read.Status, $"{mode}: {read.Refusal}");
        return read.Table!;
    }

    private static void Refused(MesFile list, SpellRecordMode mode, SpellDataStatus status, int entry, string? token = null)
    {
        var read = SpellRecordTable.Read(list, Types, mode);
        Assert.AreEqual((status, (int?)entry), (read.Status, read.Refusal?.Entry), $"{mode}: {read.Refusal}");
        if (token is not null) Assert.AreEqual(token, read.Refusal!.Token, $"{mode}");
    }

    /// <summary>The single component of record 20 read from <paramref name="line"/>.</summary>
    private static SpellComponent Component(string line, SpellRecordMode mode = SpellRecordMode.Strict) =>
        Read(List((L(20, 5), line)), mode)[20].Components.Single();

    private static ulong T(string name) => SpellComponentTables.TargetValues[SpellComponentTables.Targets.ToList().IndexOf(name)];

    [TestMethod]
    public void The_target_tables_hold_65_names_and_mg18_lacks_the_critter_bit()
    {
        Assert.AreEqual((65, 65), (SpellComponentTables.Targets.Count, SpellComponentTables.TargetValues.Count));
        Assert.AreEqual("Tgt_Obj_No_ST_Critter_Dead", SpellComponentTables.Targets[SpellComponentTables.NotDeadCritterTarget]);
        Assert.AreEqual(0x80000004UL, T("Tgt_Obj_No_ST_Critter_Dead"));
        Assert.AreEqual(SpellComponentTables.CritterTargetBit, T("Tgt_Obj_No_ST_Critter_Animal") & SpellComponentTables.CritterTargetBit);
        Assert.AreEqual(0x8000000000000000UL, T("Tgt_List"));
        Assert.AreEqual(21, SpellComponentTables.ObjectFlagNames.Count);
        Assert.IsTrue(SpellComponentTables.ObjectFlagNames.All(t => t.Count > 0));
    }

    [TestMethod]
    public void Line_zero_sets_every_action_then_each_action_adds_to_it()
    {
        const string line = "AoE: Tgt_Object | Tgt_Obj_T_Critter, AoE_SF: OSF_INVISIBLE, AoE_NO_SF: OSF_STONED, Radius: 3, Count: 2, " +
            "[Begin]AoE: Tgt_Self, [Begin]Radius: 5, [End]AoE_SF: OSF_SHIELDED, [End]Count: 7, [EndCallback]AoE_NO_SF: OSF_CHARMED";
        foreach (var mode in Modes)
        {
            var table = Read(List((L(20, 0), line), (L(1, 0), "AoE: Tgt_Self")), mode);
            var a = table[20].Areas;
            var basic = T("Tgt_Object") | T("Tgt_Obj_T_Critter") | Invulnerable;
            Assert.AreEqual(new SpellArea(basic | T("Tgt_Self"), 1u, 1u << 16, 5, 2), a[(int)SpellAction.Begin]);
            Assert.AreEqual(new SpellArea(basic, 1u, 1u << 16, 3, 2), a[(int)SpellAction.Maintain]);
            Assert.AreEqual(new SpellArea(basic, 1u | 1u << 10, 1u << 16, 3, 7), a[(int)SpellAction.End]);
            Assert.AreEqual(new SpellArea(basic, 1u, (1u << 16) | (1u << 23), 3, 2), a[(int)SpellAction.EndCallback]);
            // Records 1, 2 and 9 get no not-invulnerable bit.
            Assert.AreEqual(new SpellArea(T("Tgt_Self"), 0, 0, 0, -1), table[1].Areas[0]);
            Assert.AreEqual(T("Tgt_Self") | Invulnerable, table[3].Areas[0].Targets);
            Refused(List((L(20, 0), "AoE: Tgt_Nothing")), mode, SpellDataStatus.UnknownFlag, L(20, 0), "Tgt_Nothing");
        }
        // Without AoE the original logs an error and reads on: the mask holds only the not-invulnerable bit.
        var missing = List((L(20, 0), "Radius: 4"));
        Assert.AreEqual(new SpellArea(Invulnerable, 0, 0, 4, -1), Read(missing, SpellRecordMode.LikeOriginal)[20].Areas[2]);
        Refused(missing, SpellRecordMode.Strict, SpellDataStatus.MissingKey, L(20, 0), "AoE:");
        Refused(List((L(20, 0), "AoE: Tgt_Self, [Begin]Radius: 5, Radius: 3")), SpellRecordMode.Strict, SpellDataStatus.SkippedKey, L(20, 0), "Radius:");
    }

    [TestMethod]
    public void A_component_reads_its_action_area_triggers_and_type()
    {
        foreach (var mode in Modes)
        {
            var table = Read(List((L(20, 0), "AoE: Tgt_Self, [End]Radius: 6"),
                (L(20, 5), "[End], AoE: Tgt_Obj_Radius, AoE_SF: OSF_STONED, AoE_NO_SF: OSF_CHARMED, Count: 4, Apply_AoE: Tgt_Self, ItemTriggers: Wear, Type: dispel"),
                (L(20, 6), "[Begin], Radius: 2, Type: Destroy")), mode);
            var c = table[20].Components;
            Assert.AreEqual(new SpellComponent(SpellAction.End, SpellComponentType.Dispel, new(T("Tgt_Obj_Radius"), 1u << 16, 1u << 23, 6, 4),
                T("Tgt_Self"), 0x20000000u, null), c[0]);
            Assert.AreEqual(new SpellComponent(SpellAction.Begin, SpellComponentType.Destroy, new(0, 0, 0, 2, -1), 0, 0, null), c[1]);
            Refused(List((L(20, 5), "[Start], Type: Dispel")), mode, SpellDataStatus.UnknownName, L(20, 5), "[Start]");
            Refused(List((L(20, 5), "[Begin], Type: Explode")), mode, SpellDataStatus.UnknownName, L(20, 5), "Explode");
        }
        var noType = List((L(20, 5), "[Begin], AoE: Tgt_Self"));
        Assert.AreEqual(SpellComponentType.NoOp, Read(noType, SpellRecordMode.LikeOriginal)[20].Components.Single().Type);
        Refused(noType, SpellRecordMode.Strict, SpellDataStatus.MissingKey, L(20, 5), "Type:");
    }

    [TestMethod]
    public void Every_type_reads_its_data()
    {
        Assert.AreEqual(new AnimationGoalData(43, 2), Component("[Begin], Type: AGoal, anim_goal_knockback, SubGoal: 2").Data);
        Assert.AreEqual(new AnimationGoalData(62, 0), Component("[Begin], Type: AGoal, anim_goal_knock_down").Data);
        Assert.AreEqual(new AnimationGoalTerminateData(24), Component("[Begin], Type: AGoalTerminate, anim_goal_follow").Data);
        Assert.AreEqual(new AiRedirectData(1u << 2, 7), Component("[Begin], Type: AIRedirect, OCF_UNDEAD, MinIQ: 7").Data);
        Assert.AreEqual(new AiRedirectData(1u << 3, -1), Component("[Begin], Type: AIRedirect, OCF_ANIMAL").Data);
        Assert.AreEqual(new SpellReferenceData(55), Component("[Begin], Type: Cast, Spell: 55").Data);
        Assert.AreEqual(new SpellReferenceData(12), Component("[Begin], Type: Interrupt, MagicTech: 12").Data);
        Assert.AreEqual(new ChargeBranchData(3, 9), Component("[Begin], Type: ChargeNBranch, Cost: 3, Branch: 9").Data);
        Assert.AreEqual(new ChargeBranchData(0, -1), Component("[Begin], Type: ChargeNBranch").Data);
        Assert.AreEqual(new DamageData(5, 2, 8, 0x1u | 0x800000u), Component("[Begin], Type: Damage, DmgType: Dmg_Acid, Dmg: 2-8, Dmg_Flags: Full | Scaled").Data);
        Assert.AreEqual(new DamageData(0, 4, 4, 0), Component("[Begin], Type: Heal, Dmg: 4").Data);
        Assert.AreEqual(new EffectData(52, true, 3, 7, 10), Component("[End], Type: Effect, 52, Add, Count: 3, Cause: Injury, Scaled: 10").Data);
        Assert.AreEqual(new EffectData(9, false, 1, 6, 0), Component("[End], Type: Effect, 9, Remove").Data);
        Assert.AreEqual(new EnvironmentFlagData(1u << 4, true), Component("[Begin], Type: EnvFlag, OSF_BODY_OF_FIRE, FLAG_ON").Data);
        Assert.AreEqual(new EyeCandyData(17, true, 0x8u | 0x2u), Component("[Begin], Type: EyeCandy, 17, Add, Play: callback | stack").Data);
        Assert.AreEqual(new MovementData(1, 5), Component("[Begin], Type: Movement, Move_Location: Random_Tile_In_Radius_Self, Tile_Radius: 5").Data);
        Assert.AreEqual(new MovementData(2, null), Component("[Begin], Type: Movement, Move_Location: Teleport_Tile").Data);
        Assert.AreEqual(new ObjectFlagData(218, 1u << 6, false), Component("[End], Type: ObjFlag, obj_f_critter_flags, OCF_PARALYZED, FLAG_OFF").Data);
        Assert.AreEqual(new RechargeData(20, 9999), Component("[Begin], Type: Recharge, 20").Data);
        Assert.AreEqual(new RechargeData(20, 50), Component("[Begin], Type: Recharge, 20, Max: 50").Data);
        Assert.AreEqual(new SummonData(4029, 1, 2, 3), Component("[Begin], Type: Summon, Proto: 4029, Clear_Faction: 1, Palette: 2, List: 3").Data);
        Assert.AreEqual(new SummonData(4029, 0, 0, -1), Component("[Begin], Type: Summon, Proto: 4029").Data);
        Assert.AreEqual(new TestBranchData(29, 2, 10, 4), Component("[Maintain], Type: TestNBranch, HP_Damage, GreaterThanOrEqual, TestVal: 10, Branch: 4").Data);
        Assert.AreEqual(new TraitData(1, 600, 2), Component("[Begin], Type: Trait, art_num, 600, Palette: 2").Data);
        Assert.AreEqual(new TraitIndexData(31, 4, 50), Component("[Begin], Type: TraitIdx, obj_f_resistance_idx, resist_magic, 50").Data);
        Assert.AreEqual(new TraitIndexData(220, 6, 3), Component("[Begin], Type: TraitIdx, obj_f_critter_stat_base_idx, stat_willpower, 3").Data);
        Assert.AreEqual(new Trait64Data(242, 1), Component("[Begin], Type: Trait64, Teleport_Tile, Caster_Obj").Data);
        foreach (var type in new[] { "NoOp", "Destroy", "Dispel", "Identify", "Terminate", "Use" })
            Assert.IsNull(Component($"[Begin], Type: {type}").Data, type);
    }

    [TestMethod]
    public void Damage_marks_the_record_and_obj_flag_collects_the_spell_flags_it_turns_on()
    {
        var table = Read(List((L(20, 5), "[Begin], Type: Heal, Dmg: 2"), (L(21, 5), "[Begin], Type: Damage, Dmg: 1-2"),
            (L(22, 5), "[Begin], Type: ObjFlag, obj_f_spell_flags, OSF_STONED, FLAG_ON"),
            (L(22, 6), "[End], Type: ObjFlag, obj_f_spell_flags, OSF_SHRUNK, FLAG_OFF"),
            (L(22, 7), "[End], Type: ObjFlag, obj_f_flags, OF_OFF, FLAG_ON")), SpellRecordMode.Strict);
        Assert.AreEqual((false, true, false), (table[20].HasDamage, table[21].HasDamage, table[22].HasDamage));
        Assert.AreEqual((0u, 1u << 16), (table[21].SpellFlagsSet, table[22].SpellFlagsSet));
    }

    [TestMethod]
    public void Values_the_original_ends_its_process_on_are_refused_in_both_modes()
    {
        foreach (var mode in Modes)
        {
            // A positional value after the line has ended: the original reads through a null cursor.
            Refused(List((L(20, 5), "[Begin], Type: Effect")), mode, SpellDataStatus.MissingValue, L(20, 5));
            Refused(List((L(20, 5), "[Begin], Type: Effect, 4")), mode, SpellDataStatus.MissingValue, L(20, 5));
            Refused(List((L(20, 5), "[Begin], Type: Effect, 4, Maybe")), mode, SpellDataStatus.UnknownName, L(20, 5), "Maybe");
            Refused(List((L(20, 5), "[Begin], Type: Heal, DmgType: Dmg_Acid")), mode, SpellDataStatus.UnknownName, L(20, 5), "Dmg_Acid");
            // A name table with holes: a name it does not hold reaches a null entry.
            Refused(List((L(20, 5), "[Begin], Type: ObjFlag, obj_f_generic_flags, OGF_NOTHING, FLAG_ON")), mode, SpellDataStatus.UnknownName, L(20, 5), "OGF_NOTHING");
            Refused(List((L(20, 5), "[Begin], Type: Damage, Dmg_Flags: Full |")), mode, SpellDataStatus.TrailingSeparator, L(20, 5));
            Refused(List((L(20, 5), "[Begin], Type: Effect, 4, Add, Cause: Fate")), mode, SpellDataStatus.UnknownName, L(20, 5), "Fate");
        }
    }

    [TestMethod]
    public void Text_the_original_never_reads_is_listed_in_like_original_mode_and_refused_in_strict_mode()
    {
        var list = List((L(20, 5), "[Begin], AoE: Tgt_Self, Type: Destroy, a note"),
            (L(21, 5), "[Begin], Type: Damage, Dmg_Flags: Full, Dmg_Flags: Scaled"),
            (L(22, 5), "[Begin], Type: Movement, Move_Location: Target_Tile, Tile_Radius: 4"));
        var table = Read(list, SpellRecordMode.LikeOriginal);
        Assert.AreEqual(new DamageData(0, 0, 0, 0x1u), table[21].Components[0].Data, "the second Dmg_Flags is never read");
        Assert.AreEqual(new MovementData(0, null), table[22].Components[0].Data);
        Assert.AreEqual("UnknownKey 2005 a note|SkippedKey 2055 Dmg_Flags:|SkippedKey 2105 Tile_Radius:",
            string.Join('|', table.Unread.Select(u => $"{u.Status} {u.Entry} {u.Token}")));
        Refused(list, SpellRecordMode.Strict, SpellDataStatus.UnknownKey, L(20, 5), "a note");
        Assert.AreEqual(0, Read(List(), SpellRecordMode.Strict).Unread.Count);
    }

    [TestMethod]
    public void Strict_mode_refuses_missing_keys_the_original_logs_and_malformed_numbers()
    {
        var cast = List((L(20, 5), "[Begin], Type: Cast"));
        Assert.AreEqual(new SpellReferenceData(10000), Read(cast, SpellRecordMode.LikeOriginal)[20].Components[0].Data);
        Refused(cast, SpellRecordMode.Strict, SpellDataStatus.MissingKey, L(20, 5), "Spell:");
        Refused(List((L(20, 5), "[Begin], Type: Interrupt")), SpellRecordMode.Strict, SpellDataStatus.MissingKey, L(20, 5), "MagicTech:");
        var summon = List((L(20, 5), "[Begin], Type: Summon, List: 2"));
        Assert.AreEqual(new SummonData(0, 0, 0, 2), Read(summon, SpellRecordMode.LikeOriginal)[20].Components[0].Data);
        Refused(summon, SpellRecordMode.Strict, SpellDataStatus.MissingKey, L(20, 5), "Proto:");
        var range = List((L(20, 5), "[Begin], Type: Damage, Dmg: 3x-7"));
        Assert.AreEqual(new DamageData(0, 3, 7, 0), Read(range, SpellRecordMode.LikeOriginal)[20].Components[0].Data);
        Refused(range, SpellRecordMode.Strict, SpellDataStatus.InvalidNumber, L(20, 5));
        var effect = List((L(20, 5), "[Begin], Type: Effect, 4a, Add"));
        Assert.AreEqual(new EffectData(4, true, 1, 6, 0), Read(effect, SpellRecordMode.LikeOriginal)[20].Components[0].Data);
        Refused(effect, SpellRecordMode.Strict, SpellDataStatus.InvalidNumber, L(20, 5), "4a");
    }

    [TestMethod]
    public void Components_are_read_after_every_record_in_number_order()
    {
        // A component fault of record 0 comes after a line +1 refusal of record 222: the second pass follows the first.
        var list = List((L(0, 5), "[Begin], Type: Explode"), (L(222, 1), "Info: hostile"));
        foreach (var mode in Modes) Refused(list, mode, SpellDataStatus.UnknownName, L(222, 1), "hostile");
    }
}
