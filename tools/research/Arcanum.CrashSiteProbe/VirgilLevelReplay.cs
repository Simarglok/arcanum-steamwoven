using System.Buffers.Binary;
using System.Text;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-032 research only: a replay of what an NPC's auto-level rules buy at each
// level-up, over an explicit character sheet, plus the script call-edge reach used to
// bound which scripts can act on Virgil. Independently written from the pinned CE
// locators cited in docs/research/virgil-level-replay.md (level.c purchase helpers,
// skill.c / stat.c / spell.c / object.c / critter.c / effect.c formulas); no CE code or
// structure is reused. It is a model for research evidence, not a level-up runtime.
internal static partial class Program
{
    // Effect vocabulary per category, in the pinned source's search order (effect.c 87-185).
    internal static readonly (string Category, string[] Tokens)[] AlEffectTokens =
    [
        ("stat", CcEffectStatSymbols),
        ("basic_skill", ["bow", "dodge", "melee", "throwing", "backstab", "pickpocket", "prowling", "spottrap", "gambling", "haggle", "heal", "persuasion"]),
        ("tech_skill", ["repair", "firearms", "picklock", "armtrap"]),
        ("resistance", ["resistdamage", "resistfire", "resistelectrical", "resistpoison", "resistmagic"]),
        ("tech", ["expertiseanatomical", "expertisechemistry", "expertiseelectric", "expertiseexplosives", "expertisegun_smithy", "expertisemechanical", "expertisesmithy", "expertisetherapeutics"]),
        ("special", ["maxhps", "maxfatigue", "reaction", "crithitchance", "crithiteffect", "critfailchance", "critfaileffect", "badreactionadj", "goodreactionadj", "xpgain"]),
    ];

    internal enum AlOp { Add, Percent, Multiply, Divide, Min, Max }
    internal sealed record AlChange(string Category, int Index, AlOp Op, int Param);
    internal sealed record AlEffect(IReadOnlyList<AlChange> Changes, bool Complete, string? Stop);

    // An effect entry as the pinned parser reads it: split on space and comma; an attribute
    // token, then an operator token (min/max take the next token; +n/-n add, a trailing %
    // makes it a percentage; *n and /n are read with atoi of the whole token, so their
    // parameter is 0). An unknown attribute ends the entry; an unknown operator or a
    // missing token is refused here rather than reproduced.
    internal static AlEffect AlParseEffect(string text)
    {
        var tokens = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<AlChange>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var match = AlEffectTokens.Select(c => (c.Category, Index: Array.FindIndex(c.Tokens, t => t.Equals(tokens[i], StringComparison.OrdinalIgnoreCase))))
                .FirstOrDefault(m => m.Index >= 0);
            if (match.Category is null) return new(changes, false, "unknown_attribute");
            if (++i >= tokens.Length) return new(changes, false, "operator_missing");
            var op = tokens[i];
            int Atoi(string s) => CcAtoi(Encoding.Latin1.GetBytes(s)).Value;
            if (op.Equals("min", StringComparison.OrdinalIgnoreCase) || op.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= tokens.Length) return new(changes, false, "parameter_missing");
                changes.Add(new(match.Category, match.Index, char.ToLowerInvariant(op[1]) == 'i' ? AlOp.Min : AlOp.Max, Atoi(tokens[i])));
            }
            else if (op[0] is '+' or '-') changes.Add(new(match.Category, match.Index, op[^1] == '%' ? AlOp.Percent : AlOp.Add, Atoi(op)));
            else if (op[0] == '*') changes.Add(new(match.Category, match.Index, AlOp.Multiply, Atoi(op)));
            else if (op[0] == '/') changes.Add(new(match.Category, match.Index, AlOp.Divide, Atoi(op)));
            else return new(changes, false, "unknown_operator");
        }
        return new(changes, true, null);
    }

    // Folds every change for one attribute: percentages, then multiply, divide, add, then
    // the largest minimum and the smallest maximum (effect.c 741-850).
    internal static int AlAdjust(IEnumerable<AlChange> changes, string category, int index, int value)
    {
        int adds = 0, mults = 1, divs = 1, percents = 0, min = -1, max = -1;
        foreach (var c in changes.Where(c => c.Category == category && c.Index == index))
            switch (c.Op)
            {
                case AlOp.Add: adds += c.Param; break;
                case AlOp.Percent: percents += c.Param; break;
                case AlOp.Multiply: mults *= c.Param; break;
                case AlOp.Divide: divs *= c.Param; break;
                case AlOp.Min: if (min == -1 || min < c.Param) min = c.Param; break;
                case AlOp.Max: if (max == -1 || max > c.Param) max = c.Param; break;
            }
        if (percents != 0) value = value * (percents + 100) / 100;
        if (mults != 1) value *= mults;
        if (divs != 1) value /= divs;
        value += adds;
        if (min != -1 && value < min) value = min;
        if (max != -1 && value > max) value = max;
        return value;
    }

    // Key stats (skill.c 37-62), the skill cap per key-stat value (skill.c 145-166), the
    // character level each spell rank needs (spell.c 31-37) and its default willpower
    // (magictech.c 468-474).
    internal static readonly int[] AlBasicKeyStat = [1, 1, 1, 1, 1, 1, 5, 5, 4, 6, 4, 7];
    internal static readonly int[] AlTechKeyStat = [4, 5, 1, 5];
    internal static readonly int[] AlSkillCap = [3, 3, 3, 3, 3, 7, 7, 7, 11, 11, 11, 15, 15, 15, 19, 19, 19, 20, 20, 20];
    internal static readonly int[] AlSpellMinLevel = [1, 1, 5, 10, 15];
    internal static readonly int[] AlSpellDefaultWillpower = [6, 9, 12, 15, 18];
    private const int AlSpellMinIntelligence = 5, AlStatStr = 0, AlStatCon = 2, AlStatInt = 4, AlStatWil = 6, AlUnspentMax = 56;

    internal enum AlResult { Ok, Insufficient, UpdateFailed }

    /// <summary>A human NPC's sheet: base stats and the raw arrays the purchase helpers
    /// touch. Primary stats clamp to 1..20, level 0..51, unspent points 0..56.</summary>
    internal sealed class AlSheet
    {
        public required int[] Stats { get; init; }          // 28 base stats
        public required int[] BasicPoints { get; init; }    // 12, low six bits
        public required int[] TechPoints { get; init; }     // 4, low six bits
        public required int[] Colleges { get; init; }       // 16 college levels
        public int HpPts, HpAdj, FatiguePts, FatigueAdj;
        public required IReadOnlyList<AlChange> Effects { get; init; }
        public required int[] SpellWillpower { get; init; } // per spell id (college * 5 + rank)
        public int Wasted;                                  // points paid without a value change

        public int Level => StatLevel(17);
        public int Unspent => Stats[21];
        public int StatLevel(int stat)
        {
            var value = AlAdjust(Effects, "stat", stat, Stats[stat]);
            var (min, max) = StatRange(stat);
            return Math.Clamp(value, min, max);
        }
        public static (int Min, int Max) StatRange(int stat) => stat switch
        {
            < 8 => (1, 20), 17 => (0, 51), 21 => (0, AlUnspentMax), 22 or 23 => (0, 210),
            _ => throw new InvalidDataException("AUTOLEVEL_REPLAY_STAT"),
        };
        public void SetBase(int stat, int value) { var (min, max) = StatRange(stat); Stats[stat] = Math.Clamp(value, min, max); }
        public static int Cap(int keyStat) => AlSkillCap[Math.Clamp(keyStat, 1, 20) - 1];
        public int BasicLevel(int skill) => Math.Min(Math.Clamp(AlAdjust(Effects, "basic_skill", skill, 4 * BasicPoints[skill]), 0, 20), Cap(StatLevel(AlBasicKeyStat[skill])));
        public int TechLevel(int skill) => Math.Min(Math.Clamp(AlAdjust(Effects, "tech_skill", skill, 4 * TechPoints[skill]), 0, 20), Cap(StatLevel(AlTechKeyStat[skill])));
        public int HpMax => AlAdjust(Effects, "special", 0, StatLevel(AlStatWil) + 2 * (StatLevel(AlStatStr) + Level) + 4 + HpAdj + 4 * HpPts);
        public int FatigueMax => AlAdjust(Effects, "special", 1, 4 * FatiguePts + FatigueAdj + 2 * (Level + StatLevel(AlStatCon)) + StatLevel(AlStatWil) + 4);
        public AlSheet Clone() => new()
        {
            Stats = (int[])Stats.Clone(), BasicPoints = (int[])BasicPoints.Clone(), TechPoints = (int[])TechPoints.Clone(), Colleges = (int[])Colleges.Clone(),
            HpPts = HpPts, HpAdj = HpAdj, FatiguePts = FatiguePts, FatigueAdj = FatigueAdj, Effects = Effects, SpellWillpower = SpellWillpower, Wasted = Wasted,
        };
    }

    private const int AlLoopGuard = 1000;

    // One stat unit at a time until the effective value reaches the target: the cost is
    // stat_cost(current + 1) (0 to reach 1, else 1); a clamped set is an update failure.
    // After a purchase "current" is the effective value, so an effect that lowers the stat
    // keeps the loop paying for the same base value until the points run out.
    internal static AlResult AlBuyStat(AlSheet s, int stat, int target)
    {
        var current = s.Stats[stat];
        for (var guard = 0; current < target; guard++)
        {
            if (guard > AlLoopGuard) throw new InvalidDataException("AUTOLEVEL_REPLAY_LOOP");
            var cost = Math.Clamp(current + 1, 1, 20) == 1 ? 0 : 1;
            var unspent = s.StatLevel(21);
            if (cost > unspent) return AlResult.Insufficient;
            var before = s.Stats[stat];
            s.SetBase(stat, current + 1);
            if (s.Stats[stat] < current + 1) return AlResult.UpdateFailed;
            if (s.Stats[stat] == before) s.Wasted += cost;
            s.SetBase(21, unspent - cost);
            current = s.StatLevel(stat);
        }
        return AlResult.Ok;
    }

    // A basic (tech) skill point costs 1; a point the key stat does not allow is refused
    // and the helper buys one more base unit of the key stat instead.
    internal static AlResult AlBuySkill(AlSheet s, bool tech, int skill, int target)
    {
        var points = tech ? s.TechPoints : s.BasicPoints;
        int Level() => tech ? s.TechLevel(skill) : s.BasicLevel(skill);
        var key = tech ? AlTechKeyStat[skill] : AlBasicKeyStat[skill];
        var current = Level();
        for (var guard = 0; current < target; guard++)
        {
            if (guard > AlLoopGuard) throw new InvalidDataException("AUTOLEVEL_REPLAY_LOOP");
            var unspent = s.StatLevel(21);
            if (1 > unspent) return AlResult.Insufficient;
            var wanted = points[skill] + 1;
            if (4 * wanted <= AlSheet.Cap(s.StatLevel(key)))
            {
                points[skill] = wanted;
                if (tech) s.SetBase(23, s.Stats[23] + 1);
                s.SetBase(21, unspent - 1);
                current = Level();
            }
            else
            {
                var rc = AlBuyStat(s, key, s.Stats[key] + 1);
                if (rc != AlResult.Ok) return rc;
            }
        }
        return AlResult.Ok;
    }

    // A college rank costs 1; the next spell needs its character level, intelligence 5 and
    // its willpower. Too low a level is an update failure; otherwise the helper buys one
    // base unit of intelligence (when that is short) or willpower and tries again.
    internal static AlResult AlBuySpell(AlSheet s, int college, int target)
    {
        var current = s.Colleges[college];
        for (var guard = 0; current < target; guard++)
        {
            if (guard > AlLoopGuard) throw new InvalidDataException("AUTOLEVEL_REPLAY_LOOP");
            var spell = current + 5 * college;
            var unspent = s.StatLevel(21);
            if (1 > unspent) return AlResult.Insufficient;
            if (current >= AlSpellMinLevel.Length) return AlResult.UpdateFailed;
            var allowed = AlSpellMinLevel[current] <= s.Level && AlSpellMinIntelligence <= s.StatLevel(AlStatInt) && s.SpellWillpower[spell] <= s.StatLevel(AlStatWil);
            if (allowed)
            {
                s.SetBase(22, s.Stats[22] + 1);
                s.Colleges[college] = current + 1;
                s.SetBase(21, unspent - 1);
                current++;
                continue;
            }
            if (AlSpellMinLevel[current] > s.Level) return AlResult.UpdateFailed;
            var stat = AlSpellMinIntelligence > s.StatLevel(AlStatInt) ? AlStatInt : AlStatWil;
            var rc = AlBuyStat(s, stat, s.Stats[stat] + 1);
            if (rc != AlResult.Ok) return rc;
        }
        return AlResult.Ok;
    }

    // One hit (fatigue) point per character point, read from the base unspent value; a
    // purchase that does not raise the effective maximum is an update failure.
    internal static AlResult AlBuyMisc(AlSheet s, int type, int target)
    {
        int Max() => type == 0 ? s.HpMax : s.FatigueMax;
        var current = Max();
        for (var guard = 0; current < target; guard++)
        {
            if (guard > AlLoopGuard) throw new InvalidDataException("AUTOLEVEL_REPLAY_LOOP");
            var unspent = s.Stats[21];
            if (unspent < 1) return AlResult.Insufficient;
            if (type == 0) s.HpPts++; else s.FatiguePts++;
            var next = Max();
            if (next < current + 1) return AlResult.UpdateFailed;
            s.SetBase(21, unspent - 1);
            current = next;
        }
        return AlResult.Ok;
    }

    internal sealed record AlRuleRun(bool Completed, int? StoppedAt);

    // One rule: each step in order; running out of points stops it, any other failure
    // skips the step.
    internal static AlRuleRun AlRunRule(AlSheet s, IReadOnlyList<AlStep> steps)
    {
        for (var k = 0; k < steps.Count; k++)
        {
            var step = steps[k];
            var rc = step.Category switch
            {
                "stat" => AlBuyStat(s, step.Index, step.Target),
                "basic_skill" => AlBuySkill(s, false, step.Index, step.Target),
                "tech_skill" => AlBuySkill(s, true, step.Index, step.Target),
                "college" => AlBuySpell(s, step.Index, step.Target),
                "misc" => AlBuyMisc(s, step.Index, step.Target),
                _ => throw new InvalidDataException("AUTOLEVEL_REPLAY_CATEGORY"),
            };
            if (rc == AlResult.Insufficient) return new(false, k);
        }
        return new(true, null);
    }

    internal sealed record AlLevelUp(int Level, int PointsGranted, int UnspentBefore, AlRuleRun Scheme, AlRuleRun? Standard, int UnspentAfter, int WastedTotal, AlSheet After);

    // Level-up of a follower who matches the PC one level at a time: set the level, add the
    // bonus points (clamped to 56), run the scheme, then the standard NPC rule if the
    // scheme completed (level.c 567-605, 772-821).
    internal static List<AlLevelUp> AlReplay(AlSheet start, IReadOnlyList<AlStep> scheme, IReadOnlyList<AlStep>? standard, int toLevel)
    {
        var s = start.Clone(); var rows = new List<AlLevelUp>();
        while (s.Stats[17] < toLevel)
        {
            var from = s.Stats[17];
            s.SetBase(17, from + 1);
            var granted = AlBonusPoints(from, from + 1);
            s.SetBase(21, s.Stats[21] + granted);
            var before = s.Stats[21];
            var run = AlRunRule(s, scheme);
            var std = run.Completed && standard is not null ? AlRunRule(s, standard) : null;
            rows.Add(new(s.Stats[17], granted, before, run, std, s.Stats[21], s.Wasted, s.Clone()));
        }
        return rows;
    }

    // Breadth-first reach over literal script-number edges; call-ex and computed edges
    // are counted as unresolved.
    internal static (SortedSet<int> Reached, int Unresolved, SortedSet<int> Missing) AlReach(IEnumerable<int> roots, Func<int, (IReadOnlyList<CaveScriptEdge> Edges, int Unresolved)?> census)
    {
        var reached = new SortedSet<int>(); var missing = new SortedSet<int>(); var unresolved = 0;
        var queue = new Queue<int>(roots);
        while (queue.Count > 0)
        {
            var script = queue.Dequeue();
            if (reached.Contains(script) || missing.Contains(script)) continue;
            if (census(script) is not { } c) { missing.Add(script); continue; }
            reached.Add(script); unresolved += c.Unresolved;
            foreach (var edge in c.Edges) if (edge.Target > 0) queue.Enqueue(edge.Target);
        }
        return (reached, unresolved, missing);
    }

    private const int AlAssignAction = 10, AlSetFlagAction = 100, AlClearFlagAction = 101, AlFlagOperand = 4;
    internal readonly record struct AlSlot(int Line, bool Condition, int Type, byte[] Kinds, int[] Values);

    // Every condition and both actions of every line, as type, kinds and values.
    internal static List<AlSlot> AlActionsAndConditions(ReadOnlySpan<byte> bytes)
    {
        var (_, count, _) = CaveScriptFraming(bytes);
        var slots = new List<AlSlot>();
        for (var line = 0; line < count; line++)
            foreach (var (offset, condition) in new[] { (0, true), (44, false), (88, false) })
            {
                var slot = bytes.Slice(ScriptHeaderBytes + line * ScriptLineBytes + offset, 44);
                var values = new int[8];
                for (var k = 0; k < 8; k++) values[k] = BinaryPrimitives.ReadInt32LittleEndian(slot[(12 + 4 * k)..]);
                slots.Add(new(line, condition, BinaryPrimitives.ReadInt32LittleEndian(slot), slot.Slice(4, 8).ToArray(), values));
            }
        return slots;
    }

    private static int? InvDialogKeyFromName(string name)
    {
        var digits = name.StartsWith("dlg/", StringComparison.Ordinal) ? name[4..].TakeWhile(char.IsAsciiDigit).ToArray() : [];
        return digits.Length is > 0 and <= 5 ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    // Every line of a script as numbers: condition type and operands, then both branches.
    internal static object[] AlDecode(ReadOnlySpan<byte> bytes)
    {
        var (_, count, _) = CaveScriptFraming(bytes);
        var lines = new object[count];
        for (var line = 0; line < count; line++)
        {
            var row = bytes.Slice(ScriptHeaderBytes + line * ScriptLineBytes, ScriptLineBytes);
            object Slot(ReadOnlySpan<byte> slot)
            {
                var values = new int[8];
                for (var k = 0; k < 8; k++) values[k] = BinaryPrimitives.ReadInt32LittleEndian(slot[(12 + 4 * k)..]);
                return new { type = BinaryPrimitives.ReadInt32LittleEndian(slot), kinds = slot.Slice(4, 8).ToArray().Select(b => (int)b).ToArray(), values };
            }
            lines[line] = new { line, condition = Slot(row[..44]), on_true = Slot(row.Slice(44, 44)), on_false = Slot(row.Slice(88, 44)) };
        }
        return lines;
    }

    private static void VirgilLevelReplaySelfTest()
    {
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("autolevel self-test failed: " + label); }

        // Effect grammar and fold order.
        var e = AlParseEffect("ST +1, in -1 maxhps +10% dodge min 8, heal max 4, dx *2");
        Check(e.Complete && e.Changes.Count == 6 && e.Changes[2] is { Category: "special", Index: 0, Op: AlOp.Percent, Param: 10 } && e.Changes[5] is { Op: AlOp.Multiply, Param: 0 }, "effect grammar");
        Check(AlParseEffect("st") is { Complete: false, Stop: "operator_missing" } && AlParseEffect("foo +1") is { Complete: false, Stop: "unknown_attribute" } && AlParseEffect("st x") is { Stop: "unknown_operator" }, "effect refusals");
        Check(AlAdjust(e.Changes, "stat", 0, 8) == 9 && AlAdjust(e.Changes, "stat", 4, 8) == 7 && AlAdjust(e.Changes, "special", 0, 50) == 55 && AlAdjust(e.Changes, "stat", 1, 8) == 0, "effect fold");
        Check(AlAdjust(e.Changes, "basic_skill", 1, 4) == 8 && AlAdjust(e.Changes, "basic_skill", 10, 12) == 4, "min and max");

        AlSheet Sheet(params AlChange[] effects) => new()
        {
            Stats = Enumerable.Range(0, 28).Select(i => i < 8 ? 8 : i == 17 ? 1 : 0).ToArray(), BasicPoints = new int[12], TechPoints = new int[4], Colleges = new int[16],
            Effects = effects, SpellWillpower = Enumerable.Range(0, 80).Select(i => AlSpellDefaultWillpower[i % 5]).ToArray(),
        };
        // Formulas: cap 7 at key stat 8, hit points WP + 2(ST + level) + 4 + 4 pts, fatigue 2(level + CN) + WP + 4 + 4 pts.
        var s = Sheet(); s.BasicPoints[10] = 2;
        Check(s.BasicLevel(10) == 7 && s.HpMax == 8 + 18 + 4 && s.FatigueMax == 18 + 8 + 4, "formulas");

        // A capped skill buys its key stat first; a lowered stat drains the points.
        s = Sheet(); s.Stats[21] = 3; s.BasicPoints[2] = 1;
        Check(AlBuySkill(s, false, 2, 8) == AlResult.Ok && s.Stats[1] == 9 && s.BasicPoints[2] == 2 && s.Stats[21] == 1 && s.Wasted == 0, "key stat purchase");
        s = Sheet(new AlChange("stat", 4, AlOp.Add, -1)); s.Stats[21] = 5; s.BasicPoints[10] = 1;
        Check(AlBuySkill(s, false, 10, 8) == AlResult.Insufficient && s.Stats[4] == 9 && s.Stats[21] == 0 && s.Wasted == 4 && s.BasicPoints[10] == 1, "lowered-stat drain");

        // Tech skill: a point also raises tech points; a hit-point purchase capped by an effect fails.
        s = Sheet(); s.Stats[21] = 2;
        Check(AlBuySkill(s, true, 2, 4) == AlResult.Ok && s.TechPoints[2] == 1 && s.Stats[23] == 1 && s.Stats[21] == 1, "tech skill purchase");
        s = Sheet(new AlChange("special", 0, AlOp.Max, 30)); s.Stats[21] = 2;
        Check(AlBuyMisc(s, 0, 40) == AlResult.UpdateFailed && s.HpPts == 1 && s.Stats[21] == 2, "capped hit points fail without paying");

        // Spells: level gate is a skip, willpower is bought.
        s = Sheet(); s.Stats[21] = 3; s.Colleges[12] = 1;
        Check(AlBuySpell(s, 12, 2) == AlResult.Ok && s.Stats[6] == 9 && s.Colleges[12] == 2 && s.Stats[21] == 1, "willpower purchase");
        Check(AlBuySpell(s, 12, 3) == AlResult.UpdateFailed && s.Colleges[12] == 2, "level gate");

        // Rule run and replay: stop on points, skip on failure, standard rule after completion.
        s = Sheet(); s.Colleges[12] = 1;
        var rule = AlParseRule("necro_good 3, maxhps 40, melee 4").Steps;
        var rows = AlReplay(s, rule, AlParseRule("maxfatigue 40").Steps, 4);
        Check(rows.Count == 3 && rows[0] is { Level: 2, PointsGranted: 1, Scheme: { Completed: false, StoppedAt: 0 } } && rows[0].After.Stats[6] == 9, "first level-up buys willpower and stops");
        Check(rows[1].After.Colleges[12] == 2 && rows[2].Scheme.StoppedAt == 2 && rows[2].After.HpMax == 41, "skipped level gate, then hit points");
        Check(AlReplay(Sheet(), AlParseRule("st 8").Steps, AlParseRule("maxhps 10").Steps, 2)[0] is { Scheme.Completed: true, Standard.Completed: true, UnspentAfter: 1 }, "standard rule after completion");

        // Reach: literal edges, unresolved count, missing scripts.
        var graph = new Dictionary<int, (IReadOnlyList<CaveScriptEdge>, int)> { [1] = ([new(7, 2), new(99, 3)], 1), [2] = ([new(7, 1)], 0) };
        var reach = AlReach([1], n => graph.TryGetValue(n, out var g) ? g : null);
        Check(reach.Reached.SequenceEqual([1, 2]) && reach.Unresolved == 1 && reach.Missing.SequenceEqual([3]), "reach");
        Console.WriteLine("virgil-level-replay self-test: PASS (effect grammar and fold, formulas, purchases incl. tech and capped hit points, drain, spell gates, rule run, replay, reach)");
    }
}
