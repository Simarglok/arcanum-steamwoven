using System.Text.Json;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

/// <summary>
/// Opt-in check of ARC-WORLD-035 on the installed original (<c>DAT_ROOT</c>): the module's blessing and curse tables, their
/// effect numbers and the effect rules those name. Only numbers are compared and printed, never original text.
/// </summary>
[TestClass]
public sealed class BlessingRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_blessings_and_curses_name_effect_rules()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        MesFile Table(string path)
        {
            var resolution = stack.Resolve(path);
            Assert.AreEqual(ContentResolutionStatus.Resolved, resolution.Status, path);
            var bytes = stack.Read(resolution.Candidates[0]);
            Assert.AreEqual(ContentReadStatus.Read, bytes.Status, path);
            var parsed = MesFile.Parse(bytes.Bytes);
            Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, path);
            return parsed.File!;
        }
        var effects = Table("rules/effect.mes");

        // Literal numbers of the SCR blessing and curse operations: actions 25..28 (operand 0) and the conditions 8 and 9
        // (operand 1), value kind 3 (a number).
        var literals = new SortedDictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        var computed = 0;
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal)))
        {
            var bytes = stack.Read(stack.Resolve(key).Candidates[0]);
            if (bytes.Status != ContentReadStatus.Read || Arcanum.Data.Scripts.ScrProgram.Read(bytes.Bytes).Program is not { } program) continue;
            void Note(string name, Arcanum.Data.Scripts.ScrClause clause, int slot)
            {
                if (clause.Kind(slot) != 3) { computed++; return; }
                if (!literals.TryGetValue(name, out var set)) literals[name] = set = [];
                set.Add(clause.Value(slot));
            }
            foreach (var line in program.Lines)
            {
                if (line.Condition.Type is 8 or 9) Note(line.Condition.Type == 8 ? "has-bless" : "has-curse", line.Condition, 1);
                foreach (var action in new[] { line.OnTrue, line.OnFalse })
                    if (action.Type is >= 25 and <= 28) Note(action.Type switch { 25 => "add-bless", 26 => "remove-bless", 27 => "add-curse", _ => "remove-curse" }, action, 0);
            }
        }
        foreach (var (name, set) in literals) Console.WriteLine($"{name}: {string.Join(",", set)}");
        Console.WriteLine($"non-literal operands {computed}");
        // Literal numbers reached: blessings 52, 53, 54, 66, 67 given, 54..66 (and a 670 that no table has) taken, 66 tested;
        // curses 50 and 51 given; 62 operands are computed.
        CollectionAssert.AreEqual(new[] { 52, 53, 54, 66, 67 }, literals["add-bless"].ToArray());
        CollectionAssert.AreEqual(new[] { 50, 51 }, literals["add-curse"].ToArray());
        Assert.AreEqual(62, computed);

        var module = Table("rules/gameeffect.mes");
        var summary = new List<string>();
        foreach (var path in new[] { "mes/gamebless.mes", "mes/gamecurse.mes" })
        {
            var table = Table(path);
            var numbers = BlessingRules.Numbers(table);
            var read = numbers.Select(n => (Number: n, Effect: BlessingRules.EffectOf(table, n))).ToArray();
            var rules = read.Select(r => EffectRules.ReadAny(effects, module, r.Effect.Effect!.Value)).ToArray();
            Assert.IsTrue(rules.All(r => r.Status == EffectRuleStatus.Parsed), path);
            var line = $"{numbers[0]}..{numbers[^1]} {numbers.Count}; numbers {read.Count(r => r.Effect.Source == BlessingEffectSource.Number)}, " +
                $"not a number {read.Count(r => r.Effect.Source == BlessingEffectSource.NotANumber)}; effects below 50 " +
                $"{string.Join(",", read.Where(r => r.Effect.Effect < 50).Select(r => $"{r.Number}->{r.Effect.Effect}"))}; " +
                $"innate {read.Count(r => r.Effect.Effect is >= 64 and <= 74 or 330)}; received {table.Find(BlessingRules.ReceivedMessage) is not null}";
            Console.WriteLine($"{path}: {line}");
            summary.Add(line);
        }
        // 18 blessings, each with a number: 50 and 51 name module effects 4 and 8, which the module does not define (no changes).
        // 17 curses: 52, 53 and 54 hold a placeholder that is no number, so the original adds the module's effect 0.
        CollectionAssert.AreEqual(new[]
        {
            "50..67 18; numbers 18, not a number 0; effects below 50 50->4,51->8; innate 0; received True",
            "50..66 17; numbers 14, not a number 3; effects below 50 52->0,53->0,54->0; innate 0; received True",
        }, summary);
    }
}
