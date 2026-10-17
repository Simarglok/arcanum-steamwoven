using System.Text.Json;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;

namespace Arcanum.Session.Tests;

/// <summary>
/// Opt-in check of ARC-WORLD-029 on the installed original (<c>DAT_ROOT</c>, the game folder): every effect rule of
/// <c>rules\effect.mes</c> and the module's <c>rules\gameeffect.mes</c> is read, and each one is added to the session fixture
/// hero (a half-elf man) as a spell effect and removed again. Only numbers are compared and printed, never original text.
/// </summary>
[TestClass]
public sealed class EffectRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_effect_rules_become_hero_effects()
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
        var module = Table("rules/gameeffect.mes");

        var rules = new List<EffectRule>();
        var statuses = new Dictionary<string, int>();
        for (var number = 0; number <= SessionEffectLimits.LastNumber; number++)
        {
            EffectRule? rule;
            if (number < EffectRules.FirstNumber)
            {
                // The module's effects 0..49 use the same grammar; the reader admits the effect.mes range only.
                if (module.Find(number) is not { } message) continue;
                var status = EffectRules.Parse(message.Bytes.Span, out var changes);
                statuses[status.ToString()] = statuses.GetValueOrDefault(status.ToString()) + 1;
                rule = status == EffectRuleStatus.Parsed ? new(number, true, changes) : null;
            }
            else
            {
                var read = EffectRules.Read(effects, number);
                if (read.Rule is { Defined: false }) continue;
                statuses[read.Status.ToString()] = statuses.GetValueOrDefault(read.Status.ToString()) + 1;
                rule = read.Rule;
            }
            if (rule is not null) rules.Add(rule);
        }
        Console.WriteLine(JsonSerializer.Serialize(statuses.OrderBy(p => p.Key)));

        var outcomes = new Dictionary<string, int>();
        var modelled = 0;
        foreach (var rule in rules)
        {
            using var owner = CreateWith(HalfElf());
            var definition = SessionEffectDefinition.FromRule(rule);
            var added = owner.AddHeroEffect(owner.Version, definition, EffectCause.Spell);
            outcomes[added.Failure.ToString()] = outcomes.GetValueOrDefault(added.Failure.ToString()) + 1;
            if (added.Disposition != GameSessionDisposition.Changed) continue;
            if (owner.QueryHeroStats(new(0))!.Value.Snapshot!.AppliedEffects.Length > 1) modelled++;
            var removed = owner.RemoveHeroEffect(owner.Version, rule.Number);
            Assert.AreEqual(GameSessionDisposition.Changed, removed.Disposition, $"effect {rule.Number}");
            Assert.AreEqual(0, owner.QueryHeroEffects()!.Effects.Count);
        }
        Console.WriteLine(JsonSerializer.Serialize(outcomes.OrderBy(p => p.Key)));
        int Special(int attribute) => rules.Count(r => r.Changes.Any(c => c.Kind == EffectAttributeKind.Special && c.Attribute == attribute));
        var specials = (MaxHp: Special(0), MaxFatigue: Special(1), Reaction: Special(2), Bad: Special(7), Good: Special(8));
        Console.WriteLine($"rules {rules.Count}, with modelled stat changes {modelled}, specials {specials}");
        // 309 effect.mes entries and the module's one; five use * or /, which the effect rules refuse (SH-07, decision open).
        Assert.AreEqual((305, 5), (statuses["Parsed"], statuses["UnsupportedOperator"]));
        // Every parsed rule is admitted, but the fixture hero's own race effect 67 (innate).
        Assert.AreEqual((304, 1, 2), (outcomes["None"], outcomes["InnateEffect"], outcomes.Count));
        // 191 rules change a stat the character owner models; 7 the maximum hit points, 7 the maximum fatigue, 5 the reaction,
        // 4 the bad and 3 the good reaction adjustment.
        Assert.AreEqual((191, 7, 7, 5, 4, 3), (modelled, specials.MaxHp, specials.MaxFatigue, specials.Reaction, specials.Bad, specials.Good));
        // No rule uses −1 as a minimum or maximum, the aggregation's "unset" mark (EF6).
        Assert.AreEqual(0, rules.Sum(r => r.Changes.Count(c => c.Operator is EffectChangeOperator.Minimum or EffectChangeOperator.Maximum && c.Value == -1)));
    }

    [TestMethod, TestCategory("RealData")]
    public void Installed_blessings_and_curses_are_received_and_taken_back()
    {
        // ARC-WORLD-035: every blessing and curse of the module, each given to the fixture hero alone and taken back.
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var opened = ContentStack.Open(ContentMountPlan.Discover(root).Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        MesFile Table(string path) => MesFile.Parse(stack.Read(stack.Resolve(path).Candidates[0]).Bytes).File!;
        var effects = Table("rules/effect.mes");
        var module = Table("rules/gameeffect.mes");
        var outcomes = new Dictionary<string, int>();
        foreach (var (path, kind) in new[] { ("mes/gamebless.mes", SessionBlessingKind.Blessing), ("mes/gamecurse.mes", SessionBlessingKind.Curse) })
        {
            var table = Table(path);
            foreach (var number in BlessingRules.Numbers(table))
            {
                using var owner = CreateWith(HalfElf());
                var effect = BlessingRules.EffectOf(table, number).Effect!.Value;
                var rule = EffectRules.ReadAny(effects, module, effect).Rule!;
                var added = owner.AddHeroBlessing(owner.Version, kind, number, SessionEffectDefinition.FromRule(rule));
                outcomes[$"{kind} {added.Failure}"] = outcomes.GetValueOrDefault($"{kind} {added.Failure}") + 1;
                if (added.Disposition != GameSessionDisposition.Changed) continue;
                Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveHeroBlessing(owner.Version, kind, number).Disposition);
                Assert.AreEqual((0, 0), (owner.QueryHeroBlessings(kind)!.Count, owner.QueryHeroEffects()!.Effects.Count));
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(outcomes.OrderBy(p => p.Key)));
        Assert.AreEqual((18, 17, 2), (outcomes["Blessing None"], outcomes["Curse None"], outcomes.Count));
    }
}
