using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Magic;

/// <summary>ARC-MAGIC-001: the spell record reader on synthetic MES tables (no game text), in both reader modes.</summary>
[TestClass]
public sealed class SpellRecordTableTests
{
    private static readonly SpellRecordMode[] Modes = [SpellRecordMode.Strict, SpellRecordMode.LikeOriginal];

    /// <summary>The 25 component type names in their installed order (type names are identifiers, not game text).</summary>
    internal static readonly string[] Types =
        ["NoOp", "AGoal", "AGoalTerminate", "AIRedirect", "Cast", "ChargeNBranch", "Damage", "Destroy", "Dispel", "Effect", "EnvFlag",
         "EyeCandy", "Heal", "Identify", "Interrupt", "Movement", "ObjFlag", "Recharge", "Summon", "Terminate", "TestNBranch", "Trait",
         "TraitIdx", "Trait64", "Use"];

    /// <summary>A record table with line +0 <c>AoE: Tgt_Self</c> and an empty line +1 for every record, then the given lines
    /// (number → text; null removes).</summary>
    internal static MesFile List(params (int Number, string Text)[] lines)
    {
        var all = new SortedDictionary<int, string>();
        for (var spell = 0; spell < SpellRecord.Count; spell++)
        {
            all[SpellRecordTable.FirstMessage(spell)] = "AoE: Tgt_Self";
            all[SpellRecordTable.FirstMessage(spell) + 1] = "";
        }
        foreach (var (number, text) in lines)
            if (text is null) all.Remove(number); else all[number] = text;
        return Mes(string.Concat(all.Select(p => $"{{{p.Key}}}{{{p.Value}}}\n")));
    }

    internal static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.Latin1.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return parsed.File!;
    }

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
        Assert.AreEqual(status, read.Status, $"{mode}: {read.Refusal}");
        Assert.IsNull(read.Table);
        Assert.AreEqual((status, SpellRecordTable.Path, (int?)entry), (read.Refusal!.Status, read.Refusal.File, read.Refusal.Entry), $"{mode}");
        if (token is not null) Assert.AreEqual(token, read.Refusal.Token, $"{mode}");
    }

    [TestMethod]
    public void Records_start_at_1000_plus_50n_and_from_140_at_3000_plus_50n()
    {
        Assert.AreEqual((1000, 1050, 7950, 10000, 14100), (SpellRecordTable.FirstMessage(0), SpellRecordTable.FirstMessage(1),
            SpellRecordTable.FirstMessage(139), SpellRecordTable.FirstMessage(140), SpellRecordTable.FirstMessage(222)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpellRecordTable.FirstMessage(223));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpellRecordTable.FirstMessage(-1));
        var table = Read(List((1001, "Cost: 4"), (7951, "Cost: 5"), (8001, "Cost: 6"), (10001, "Cost: 7"), (14101, "Cost: 8")), SpellRecordMode.Strict);
        Assert.AreEqual((4, 5, 7, 8), (table[0].Cost, table[139].Cost, table[140].Cost, table[222].Cost));
        Assert.IsTrue(table.Records.All(r => r.Cost is 0 or 4 or 5 or 7 or 8), "message 8001 belongs to no record");
        Assert.AreEqual(SpellRecord.Count, table.Records.Count);
    }

    [TestMethod]
    public void Every_field_starts_at_its_default()
    {
        foreach (var mode in Modes)
        {
            var table = Read(List(), mode);
            var r = table[7];
            Assert.AreEqual((7, (int?)1, (int?)3, 12), (r.Number, r.College, r.Rank, r.MinimumWillpower));
            Assert.AreEqual((0, 0, 0, 0, -1, 0, 1, 0), (r.Cost, r.MaintainCost, r.MaintainPeriod, r.DurationSeconds, r.DurationStat,
                r.DurationStatLevel, r.DurationStatModifier, r.DurationTriggerCount));
            Assert.AreEqual((-1, 0, 99, false, false, false, SpellInfo.Neutral, true, false, false),
                (r.ResistStat, r.ResistModifier, r.Range, r.IsTechnology, r.ChargesFromCells, r.SkipsBeginCost, r.Info, r.Enabled, r.NoResist, r.NoReflect));
            Assert.AreEqual((-1, -1, 0, 0u, 0u, 0u, 0u, 0u, 0u, 0), (r.CastingAnimation, r.Missile, r.NoStack, r.CancelsSpellFlags,
                r.CancelsEnvironmentSpellFlags, r.DisallowedCasterSpellFlags, r.DisallowedTargetSpellFlags, r.DisallowedTargetCritterFlags,
                r.ItemTriggers, r.AiDefensiveSecond));
            CollectionAssert.AreEqual(Enumerable.Repeat(-1, 5).ToArray(), r.CasterEyeCandy.ToArray());
            CollectionAssert.AreEqual(Enumerable.Repeat(-1, 5).ToArray(), r.TargetEyeCandy.ToArray());
            CollectionAssert.AreEqual(Enumerable.Repeat(-1, 10).ToArray(), r.AiPriorities.ToArray());
            CollectionAssert.AreEqual(new[] { true, true, false, false, false }, r.LinesPresent.ToArray());
            Assert.AreEqual(("AoE: Tgt_Self", 0, 0, 0), (r.AreaLine, r.ComponentLines.Count, r.Components.Count, r.KeysRead.Count));
            // Willpower {6, 9, 12, 15, 18} by rank for the college spells; 0 from record 80; technology forced from 140.
            CollectionAssert.AreEqual(new[] { 6, 9, 12, 15, 18, 6 }, table.Records.Take(6).Select(x => x.MinimumWillpower).ToArray());
            Assert.IsTrue(table.Records.Skip(80).All(x => x.MinimumWillpower == 0 && x.College is null && x.Rank is null));
            Assert.AreEqual(83, table.Records.Count(x => x.IsTechnology));
            Assert.IsTrue(table.Records.Skip(140).All(x => x.IsTechnology));
            Assert.AreEqual(((int?)15, (int?)5), (table[79].College, table[79].Rank));
        }
    }

    [TestMethod]
    public void Line_one_reads_every_key_in_its_order()
    {
        const string line = " IQ: 14, Cost: 5, Maintain: (2 @ 30), Duration: (60 @ 6), DurationStatInfo: (12 @ 4), DurationTriggerCount: 3, " +
            "Resist: (stat_willpower @ -2), Range: 8, ChargesFromCells: 1, ChargeBeginCost: 0, Info: aggressive, Disabled: 1";
        foreach (var mode in Modes)
        {
            var r = Read(List((L(3, 1), line), (L(81, 1), "Info: Friendly"), (L(82, 1), "Info: may_be_aggressive")), mode)[3];
            Assert.AreEqual((14, 5, 2, 30, 60, 6, 12, 4, 3), (r.MinimumWillpower, r.Cost, r.MaintainCost, r.MaintainPeriod, r.DurationSeconds,
                r.DurationStat, r.DurationStatLevel, r.DurationStatModifier, r.DurationTriggerCount));
            Assert.AreEqual((6, -2, 8, true, true, SpellInfo.Aggressive, false), (r.ResistStat, r.ResistModifier, r.Range, r.ChargesFromCells,
                r.SkipsBeginCost, r.Info, r.Enabled));
            Assert.AreEqual(12, r.KeysRead.Count);
            var table = Read(List((L(80, 1), mode == SpellRecordMode.Strict ? "IQ: 0" : "IQ: 30"), (L(81, 1), "Info: Friendly"), (L(82, 1), "Info: may_be_aggressive"), (L(83, 1), "Disabled: 0")), mode);
            Assert.AreEqual(0, table[80].MinimumWillpower, "the willpower of a record from 80 is forced to 0 after its line is read");
            Assert.AreEqual((SpellInfo.Friendly, SpellInfo.MayBeAggressive, true), (table[81].Info, table[82].Info, table[83].Enabled));
        }
    }

    [TestMethod]
    public void The_cursor_skips_a_key_out_of_order_and_strict_mode_refuses_it()
    {
        var outOfOrder = List((L(5, 1), "Cost: 2, IQ: 30, Range: 4"));
        var r = Read(outOfOrder, SpellRecordMode.LikeOriginal)[5];
        Assert.AreEqual((2, 6, 99), (r.Cost, r.MinimumWillpower, r.Range), "IQ and every key after it are never reached");
        Refused(outOfOrder, SpellRecordMode.Strict, SpellDataStatus.SkippedKey, L(5, 1), "IQ:");
        Refused(List((L(5, 1), "Cost: 2, Cost: 3")), SpellRecordMode.Strict, SpellDataStatus.SkippedKey, L(5, 1), "Cost:");

        // A Duration whose stat is -1 never tries DurationStatInfo, so it and every later key stay unread.
        var noStat = List((L(6, 1), "Duration: (30 @ -1), DurationStatInfo: (5 @ 2), Range: 4"));
        r = Read(noStat, SpellRecordMode.LikeOriginal)[6];
        Assert.AreEqual((30, -1, 0, 1, 99), (r.DurationSeconds, r.DurationStat, r.DurationStatLevel, r.DurationStatModifier, r.Range));
        Refused(noStat, SpellRecordMode.Strict, SpellDataStatus.SkippedKey, L(6, 1), "DurationStatInfo:");

        var unknown = List((L(7, 1), "Cost: 2, Speed: 4"));
        Assert.AreEqual(2, Read(unknown, SpellRecordMode.LikeOriginal)[7].Cost);
        Refused(unknown, SpellRecordMode.Strict, SpellDataStatus.UnknownKey, L(7, 1), "Speed:");
        Refused(List((L(7, 3), "No_Stack: 1, ItemTrigger: Wear")), SpellRecordMode.Strict, SpellDataStatus.UnknownKey, L(7, 3), "ItemTrigger:");
        Assert.AreEqual(0, Read(List((L(7, 1), "cost: 2")), SpellRecordMode.LikeOriginal)[7].Cost, "keys are case-sensitive");
        // An empty value segment stops the cursor; strict mode names the first key it leaves unread.
        var empty = List((L(7, 1), "Cost: 5,, Range: 3"));
        Assert.AreEqual((5, 99), (Read(empty, SpellRecordMode.LikeOriginal)[7].Cost, Read(empty, SpellRecordMode.LikeOriginal)[7].Range));
        Refused(empty, SpellRecordMode.Strict, SpellDataStatus.SkippedKey, L(7, 1), "Range:");
    }

    [TestMethod]
    public void A_missing_required_line_reads_as_no_keys_like_the_original_and_is_refused_in_strict_mode()
    {
        foreach (var offset in new[] { 0, 1 })
        {
            var list = List((L(9, offset), null!), (L(9, 2), "Missile: 4"));
            var r = Read(list, SpellRecordMode.LikeOriginal)[9];
            Assert.AreEqual((offset != 0, offset != 1), (r.LinesPresent[0], r.LinesPresent[1]));
            Assert.AreEqual((18, 0, true), (r.MinimumWillpower, r.Cost, r.Enabled));
            Refused(list, SpellRecordMode.Strict, SpellDataStatus.MissingRequiredLine, L(9, offset));
        }
        Assert.IsNull(Read(List((L(9, 0), null!)), SpellRecordMode.LikeOriginal)[9].AreaLine);
    }

    [TestMethod]
    public void Numbers_are_c_atoi_like_the_original_and_must_be_plain_integers_in_strict_mode()
    {
        foreach (var (text, value) in new[] { ("12abc", 12), ("high", 0), ("4294967297", 1), ("", 0), (" -7 ", -7), ("+3", 3) })
        {
            var list = List((L(2, 1), $"Cost: {text}, Range: 5"));
            Assert.AreEqual((value, 5), (Read(list, SpellRecordMode.LikeOriginal)[2].Cost, Read(list, SpellRecordMode.LikeOriginal)[2].Range), text);
            if (text is " -7 " or "+3") Assert.AreEqual(value, Read(list, SpellRecordMode.Strict)[2].Cost);
            else Refused(list, SpellRecordMode.Strict, SpellDataStatus.InvalidNumber, L(2, 1));
        }
        Refused(List((L(2, 4), "AI_Flee: x")), SpellRecordMode.Strict, SpellDataStatus.InvalidNumber, L(2, 4), "AI_Flee:x");
    }

    [TestMethod]
    public void Pairs_need_their_parenthesis_and_in_strict_mode_their_second_value()
    {
        foreach (var mode in Modes)
            Refused(List((L(4, 1), "Maintain: 2 @ 30")), mode, SpellDataStatus.MissingParenthesis, L(4, 1), "2 @ 30");
        // Without '@' the second value is the first: a Duration "(30)" has stat 30.
        var single = List((L(4, 1), "Maintain: (2), Duration: (30)"));
        var r = Read(single, SpellRecordMode.LikeOriginal)[4];
        Assert.AreEqual((2, 2, 30, 30), (r.MaintainCost, r.MaintainPeriod, r.DurationSeconds, r.DurationStat));
        Refused(single, SpellRecordMode.Strict, SpellDataStatus.MalformedPair, L(4, 1), "Maintain:(2)");
        var trailing = List((L(4, 1), "Maintain: (2 @ 3) x"));
        Assert.AreEqual((2, 3), (Read(trailing, SpellRecordMode.LikeOriginal)[4].MaintainCost, Read(trailing, SpellRecordMode.LikeOriginal)[4].MaintainPeriod));
        Refused(trailing, SpellRecordMode.Strict, SpellDataStatus.MalformedPair, L(4, 1));
        var overflow = List((L(4, 1), "Maintain: (2 @ 4294967297)"));
        Assert.AreEqual(1, Read(overflow, SpellRecordMode.LikeOriginal)[4].MaintainPeriod);
        Refused(overflow, SpellRecordMode.Strict, SpellDataStatus.InvalidNumber, L(4, 1), "Maintain:(2 @ 4294967297)");
    }

    [TestMethod]
    public void Resist_names_a_stat_key_case_insensitively_and_an_unknown_name_is_refused_in_both_modes()
    {
        foreach (var mode in Modes)
        {
            var table = Read(List((L(1, 1), "Resist: (STAT_Constitution @ -3)"), (L(2, 1), "Resist: (stat_age@1)"), (L(3, 1), "Resist: (stat_strength @ 5)")), mode);
            Assert.AreEqual((2, -3, 22, 1, 0, 5), (table[1].ResistStat, table[1].ResistModifier, table[2].ResistStat, table[2].ResistModifier,
                table[3].ResistStat, table[3].ResistModifier));
            Refused(List((L(1, 1), "Resist: (stat_luck @ -3)")), mode, SpellDataStatus.UnknownName, L(1, 1), "stat_luck");
            Refused(List((L(1, 1), "Resist: ( stat_willpower @ -3)")), mode, SpellDataStatus.UnknownName, L(1, 1), "");
            Refused(List((L(1, 1), "Info: hostile")), mode, SpellDataStatus.UnknownName, L(1, 1), "hostile");
            Refused(List((L(1, 1), "Info: friendly , Disabled: 1")), mode, SpellDataStatus.UnknownName, L(1, 1), "friendly ");
        }
        // The name ends at its first space, or at '@'; without '@' the closing parenthesis is part of the name, which matches
        // nothing, and the stat number stands for the value when the name has a space before ')'.
        var r = Read(List((L(1, 1), "Resist: (stat_willpower extra @ -3), Range: 2"), (L(2, 1), "Resist: (stat_dexterity )")), SpellRecordMode.LikeOriginal);
        Assert.AreEqual((6, -3, 2, 1, 1), (r[1].ResistStat, r[1].ResistModifier, r[1].Range, r[2].ResistStat, r[2].ResistModifier));
        foreach (var mode in Modes)
            Refused(List((L(2, 1), "Resist: (stat_dexterity)")), mode, SpellDataStatus.UnknownName, L(2, 1), "stat_dexterity)");
        Refused(List((L(1, 1), "Resist: (stat_willpower extra @ -3)")), SpellRecordMode.Strict, SpellDataStatus.MalformedPair, L(1, 1));
    }

    [TestMethod]
    public void Line_one_values_the_original_drops_are_refused_in_strict_mode()
    {
        var list = List((L(1, 1), "DurationTriggerCount: -2, ChargesFromCells: 0, ChargeBeginCost: 4"));
        var r = Read(list, SpellRecordMode.LikeOriginal)[1];
        Assert.AreEqual((0, false, false), (r.DurationTriggerCount, r.SkipsBeginCost, r.ChargesFromCells));
        Refused(list, SpellRecordMode.Strict, SpellDataStatus.IgnoredValue, L(1, 1), "DurationTriggerCount:");
        Refused(List((L(1, 1), "ChargeBeginCost: 4")), SpellRecordMode.Strict, SpellDataStatus.IgnoredValue, L(1, 1), "ChargeBeginCost:");
        Assert.AreEqual(0, Read(List((L(1, 1), "DurationTriggerCount: 0")), SpellRecordMode.Strict)[1].DurationTriggerCount);
        Assert.AreEqual((false, 3), (Read(list, SpellRecordMode.LikeOriginal)[1].SkipsBeginCost, Read(list, SpellRecordMode.LikeOriginal)[1].KeysRead.Count));
        // A willpower minimum given to a record from 80 is read and then forced to 0.
        var forced = List((L(80, 1), "IQ: 30"));
        Assert.AreEqual(0, Read(forced, SpellRecordMode.LikeOriginal)[80].MinimumWillpower);
        Refused(forced, SpellRecordMode.Strict, SpellDataStatus.IgnoredValue, L(80, 1), "IQ:");
        Assert.AreEqual(0, Read(List((L(80, 1), "IQ: 0")), SpellRecordMode.Strict)[80].MinimumWillpower);
    }

    [TestMethod]
    public void Line_two_reads_eye_candy_and_is_tech()
    {
        const string line = "CastingAnim: 3, Missile: 4, [Begin]Caster: 10, [Begin]Target: 11, [Maintain]Caster: 12, [Maintain]Target: 13, " +
            "[End]Caster: 14, [End]Target: 15, [Callback]Caster: 16, [Callback]Target: 17, [EndCallback]Caster: 18, [EndCallback]Target: 19, Is_Tech: 1";
        foreach (var mode in Modes)
        {
            var table = Read(List((L(100, 2), line), (L(101, 2), "Missile: 7, Is_Tech: 0"), (L(141, 2), "Is_Tech: 1")), mode);
            Assert.IsTrue(table[141].IsTechnology);
            var r = table[100];
            Assert.AreEqual((3, 4, true, true), (r.CastingAnimation, r.Missile, r.IsTechnology, r.LinesPresent[2]));
            CollectionAssert.AreEqual(new[] { 10, 12, 14, 16, 18 }, r.CasterEyeCandy.ToArray());
            CollectionAssert.AreEqual(new[] { 11, 13, 15, 17, 19 }, r.TargetEyeCandy.ToArray());
            Assert.AreEqual((-1, 7, false), (table[101].CastingAnimation, table[101].Missile, table[101].IsTechnology));
        }
        // Is_Tech: 0 cannot clear the flag forced from 140.
        var cleared = List((L(140, 2), "Is_Tech: 0"));
        Assert.IsTrue(Read(cleared, SpellRecordMode.LikeOriginal)[140].IsTechnology);
        Refused(cleared, SpellRecordMode.Strict, SpellDataStatus.IgnoredValue, L(140, 2), "Is_Tech:");
    }

    [TestMethod]
    public void Line_three_reads_flag_lists_by_name()
    {
        const string line = "No_Stack: 1, Cancels_SF: OSF_BODY_OF_AIR | osf_body_of_fire, Cancels_EnvSF: OSF_INVISIBLE, " +
            "Disallowed_SF: OSF_BONDS_OF_MAGIC|OSF_STONED, Disallowed_TSF: OSF_HARDENED_HANDS OSF_FLOATING, Disallowed_TCF: OCF_UNDEAD, " +
            "ItemTriggers: Item_Used | Wear";
        foreach (var mode in Modes)
        {
            var r = Read(List((L(12, 3), line)), mode)[12];
            Assert.AreEqual((1, 0b10100u, 1u, (1u << 12) | (1u << 16), 0x80000002u, 0b100u, 0x20003000u),
                (r.NoStack, r.CancelsSpellFlags, r.CancelsEnvironmentSpellFlags, r.DisallowedCasterSpellFlags, r.DisallowedTargetSpellFlags,
                 r.DisallowedTargetCritterFlags, r.ItemTriggers));
            Refused(List((L(12, 3), "Cancels_SF: OSF_BODY_OF_AIR | OSF_NOTHING")), mode, SpellDataStatus.UnknownFlag, L(12, 3), "OSF_NOTHING");
            Refused(List((L(12, 3), "Cancels_SF: , ItemTriggers: Wear")), mode, SpellDataStatus.UnknownFlag, L(12, 3), "");
            // A separator right before the end of the value: the original reads through a null cursor.
            Refused(List((L(12, 3), "Cancels_SF: OSF_BODY_OF_AIR , ItemTriggers: Wear")), mode, SpellDataStatus.TrailingSeparator, L(12, 3));
            Refused(List((L(12, 3), "ItemTriggers: Wear|")), mode, SpellDataStatus.TrailingSeparator, L(12, 3));
        }
        // A space-separated name followed later by '|': the walk resumes after the '|', so the name between is never read.
        foreach (var mode in Modes)
        {
            // '|' first: every name is read, and white space after a separator is skipped.
            Assert.AreEqual(0b11100u, Read(List((L(12, 3), "Cancels_SF: OSF_BODY_OF_AIR|OSF_BODY_OF_EARTH OSF_BODY_OF_FIRE")), mode)[12].CancelsSpellFlags);
            Assert.AreEqual(0b1100u, Read(List((L(12, 3), "Cancels_SF: OSF_BODY_OF_AIR|  OSF_BODY_OF_EARTH")), mode)[12].CancelsSpellFlags);
            Refused(List((L(12, 3), "Cancels_SF: OSF_BODY_OF_AIR||OSF_BODY_OF_EARTH")), mode, SpellDataStatus.UnknownFlag, L(12, 3), "");
            Refused(List((L(12, 3), "Cancels_SF: OSF_BODY_OF_AIR|  , No_Stack: 1")), mode, SpellDataStatus.UnknownFlag, L(12, 3), "");
        }
        var skipping = List((L(12, 3), "Cancels_SF: OSF_BODY_OF_AIR OSF_BODY_OF_EARTH|OSF_BODY_OF_FIRE"));
        Assert.AreEqual(0b10100u, Read(skipping, SpellRecordMode.LikeOriginal)[12].CancelsSpellFlags);
        Refused(skipping, SpellRecordMode.Strict, SpellDataStatus.AmbiguousFlagList, L(12, 3));
    }

    [TestMethod]
    public void Line_four_reads_ai_values_and_the_presence_keys()
    {
        const string line = "AI_Flee: 1, AI_Summon: 2, AI_Defensive: (3 @ 1), AI_Offensive: 4, AI_HealingLight: 5, AI_HealingMedium: 6, " +
            "AI_HealingHeavy: 7, AI_CurePoison: 8, AI_FatigueRecover: 9, AI_Resurrect: 10, No_Resist: 1, No_Reflect: 1";
        foreach (var mode in Modes)
        {
            var table = Read(List((L(150, 4), line), (L(151, 4), "AI_Offensive: 20, No_Reflect: 1"), (L(152, 4), "")), mode);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, table[150].AiPriorities.ToArray());
            Assert.AreEqual((1, true, true), (table[150].AiDefensiveSecond, table[150].NoResist, table[150].NoReflect));
            CollectionAssert.AreEqual(new[] { -1, -1, -1, 20, -1, -1, -1, -1, -1, -1 }, table[151].AiPriorities.ToArray());
            Assert.AreEqual((false, true, true), (table[151].NoResist, table[151].NoReflect, table[152].LinesPresent[4]));
        }
        var zero = List((L(150, 4), "No_Resist: 0"));
        Assert.IsTrue(Read(zero, SpellRecordMode.LikeOriginal)[150].NoResist, "by presence, whatever the value");
        Refused(zero, SpellRecordMode.Strict, SpellDataStatus.IgnoredValue, L(150, 4), "No_Resist:");
        var zeroReflect = List((L(150, 4), "No_Reflect: 0"));
        Assert.IsTrue(Read(zeroReflect, SpellRecordMode.LikeOriginal)[150].NoReflect);
        Refused(zeroReflect, SpellRecordMode.Strict, SpellDataStatus.IgnoredValue, L(150, 4), "No_Reflect:");
    }

    [TestMethod]
    public void A_byte_above_0x7f_is_refused_in_both_modes()
    {
        foreach (var mode in Modes)
            Refused(List((L(30, 3), "No_Stack: 1é")), mode, SpellDataStatus.NonAsciiByte, L(30, 3), "é");
    }

    [TestMethod]
    public void Component_lines_run_from_plus_5_to_the_first_gap_and_at_most_44()
    {
        // Lines +5..+48 of record 40 (message 3050 is record 41's line +0); the 45th line is never read.
        var lines = Enumerable.Range(0, 45).Select(i => (L(40, 5 + i), $"[Begin], Count: {i}, Type: NoOp"))
            .Concat([(L(41, 5), "[End], Type: Destroy"), (L(41, 6), "[End], Type: Dispel"), (L(41, 8), "Bad")]).ToArray();
        foreach (var mode in Modes)
        {
            var table = Read(List(lines), mode);
            Assert.AreEqual((44, 43), (table[40].Components.Count, table[40].Components[^1].Area.Count));
            CollectionAssert.AreEqual(new[] { SpellComponentType.Destroy, SpellComponentType.Dispel }, table[41].Components.Select(c => c.Type).ToArray());
            Assert.AreEqual(2, table[41].ComponentLines.Count);
        }
    }

    [TestMethod]
    public void The_auto_level_willpower_read_matches_the_record_reader()
    {
        var list = List((L(0, 1), "  IQ: 14, Cost: 5"), (L(7, 1), "Cost: 2, IQ: 30"), (L(79, 1), "IQ:-2"));
        var willpower = AutoLevelTables.ReadSpellWillpower(list);
        var table = SpellRecordTable.Read(list, Types, SpellRecordMode.LikeOriginal).Table!;
        CollectionAssert.AreEqual(willpower.Minima!.ToArray(), table.Records.Take(80).Select(r => r.MinimumWillpower).ToArray());
    }

    [TestMethod]
    public void Spell_texts_and_component_names()
    {
        var texts = new StringBuilder();
        for (var n = 0; n < SpellRecord.Count; n++) texts.Append($"{{{n}}}{{spell {n}}}\n");
        for (var c = 0; c < 16; c++) texts.Append($"{{{500 + c}}}{{college {c}}}{{{8000 + c}}}{{about {c}}}{{{9000 + c}}}{{master {c}}}\n");
        for (var n = 0; n < 80; n++) texts.Append($"{{{700 + n}}}{{does {n}}}\n");
        var components = Mes(string.Concat(Enumerable.Range(10, 25).Select(n => $"{{{n}}}{{type {n}}}\n")));
        foreach (var mode in Modes)
        {
            var load = SpellData.Create(components, Mes(texts.ToString()), List(), mode);
            Assert.AreEqual(SpellDataStatus.Read, load.Status, $"{load.Refusal}");
            var data = load.Data!;
            Assert.AreEqual(("type 10", "type 34", 25), (data.ComponentTypeNames[0], data.ComponentTypeNames[24], data.ComponentTypeNames.Count));
            Assert.AreEqual(("spell 0", "spell 222", "college 15", "does 79", "about 3", "master 4"), (data.Texts.Names[0], data.Texts.Names[222],
                data.Texts.CollegeNames[15], data.Texts.SpellDescriptions[79], data.Texts.CollegeDescriptions[3], data.Texts.MasteryDescriptions[4]));
        }
        var gap = Mes(texts.ToString().Replace("{9007}{master 7}", ""));
        Assert.IsNull(SpellData.Create(components, gap, List(), SpellRecordMode.LikeOriginal).Data!.Texts.MasteryDescriptions[7]);
        var strict = SpellData.Create(components, gap, List(), SpellRecordMode.Strict);
        Assert.AreEqual((SpellDataStatus.MissingText, SpellTexts.Path, (int?)9007), (strict.Status, strict.Refusal!.File, strict.Refusal.Entry));
        foreach (var mode in Modes)
        {
            var missing = SpellData.Create(Mes("{10}{a}{11}{b}{13}{d}"), Mes(texts.ToString()), List(), mode);
            Assert.AreEqual((SpellDataStatus.MissingComponentName, (int?)12), (missing.Status, missing.Refusal!.Entry));
            var record = SpellData.Create(components, Mes(texts.ToString()), List((L(3, 1), "Info: none")), mode);
            Assert.AreEqual((SpellDataStatus.UnknownName, (int?)L(3, 1)), (record.Status, record.Refusal!.Entry));
        }
    }

    private static ContentStack Stack(string root)
    {
        var discovery = ContentMountPlan.Discover(root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }

    [TestMethod]
    public void Arguments_are_checked_before_any_read()
    {
        var mes = Mes("{10}{a}");
        Assert.ThrowsExactly<ArgumentNullException>(() => SpellData.Create(mes, null!, mes, SpellRecordMode.Strict));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpellData.Create(mes, mes, mes, (SpellRecordMode)7));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpellRecordTable.Read(mes, Types, (SpellRecordMode)7));
    }

    [TestMethod]
    public void The_files_come_from_the_first_base_source_and_never_from_the_module()
    {
        var root = Path.Combine(Path.GetTempPath(), "arc-spells-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "modules", "Mod"));
            File.WriteAllBytes(Path.Combine(root, "tig.dat"), Dat());
            var components = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(10, 25).Select(n => $"{{{n}}}{{type {n}}}\n")));
            var texts = Encoding.ASCII.GetBytes("{0}{base name}");
            var list = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, SpellRecord.Count)
                .Select(n => $"{{{SpellRecordTable.FirstMessage(n)}}}{{AoE: Tgt_Self}}{{{SpellRecordTable.FirstMessage(n) + 1}}}{{Cost: {(n == 0 ? 3 : 0)}}}\n")));
            File.WriteAllBytes(Path.Combine(root, "arcanum1.dat"), Dat(("mes/spell.mes", texts), ("rules/magictech.mes", components), ("rules/spelllist.mes", list)));
            File.WriteAllBytes(Path.Combine(root, "modules", "Mod.dat"), Dat(("mes/spell.mes", Encoding.ASCII.GetBytes("{0}{module name}")),
                ("rules/spelllist.mes", Encoding.ASCII.GetBytes("{1001}{Cost: 9}"))));
            using (var stack = Stack(root))
            {
                var load = SpellData.Open(stack, SpellRecordMode.LikeOriginal);
                Assert.AreEqual(SpellDataStatus.Read, load.Status, $"{load.Refusal}");
                Assert.AreEqual((3, "base name"), (load.Data!.Records[0].Cost, load.Data.Texts.Names[0]));
                Assert.AreEqual(SpellDataStatus.MissingText, SpellData.Open(stack, SpellRecordMode.Strict).Status);
            }
            // Two base sources: the first in search order wins (Arcanum4 before arcanum1).
            File.WriteAllBytes(Path.Combine(root, "Arcanum4.dat"), Dat(("mes/spell.mes", Encoding.ASCII.GetBytes("{0}{first base name}"))));
            using (var stack = Stack(root))
                Assert.AreEqual("first base name", SpellData.Open(stack, SpellRecordMode.LikeOriginal).Data!.Texts.Names[0]);
            File.WriteAllBytes(Path.Combine(root, "Arcanum4.dat"), Dat(("mes/spell.mes", Encoding.ASCII.GetBytes("no message here"))));
            using (var stack = Stack(root))
                Assert.AreEqual((SpellDataStatus.FileEmpty, SpellTexts.Path), (SpellData.Open(stack, SpellRecordMode.LikeOriginal).Status,
                    SpellData.Open(stack, SpellRecordMode.LikeOriginal).Refusal!.File));
            File.WriteAllBytes(Path.Combine(root, "Arcanum4.dat"), Dat(("mes/spell.mes", Encoding.ASCII.GetBytes("{0}{a}{0}{b}"))));
            using (var stack = Stack(root))
                Assert.AreEqual(SpellDataStatus.FileRefused, SpellData.Open(stack, SpellRecordMode.LikeOriginal).Status);
            File.Delete(Path.Combine(root, "Arcanum4.dat"));
            File.WriteAllBytes(Path.Combine(root, "arcanum1.dat"), Dat(("mes/spell.mes", texts), ("rules/magictech.mes", components)));
            using (var stack = Stack(root))
            {
                var load = SpellData.Open(stack, SpellRecordMode.LikeOriginal);
                Assert.AreEqual((SpellDataStatus.FileMissing, SpellRecordTable.Path), (load.Status, load.Refusal!.File), "a module copy is never read");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
