using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-1732 (ledger HLR1–HLR12): the talk of a Player <c>h:</c> reply, the original's healer menu. Picking the reply (state 11 of 0x414810, 0x419830) runs its
/// action, then the NPC's line is the class line 14000 and the replies are generated from the NPC's rank in the healing college 12 (0x4B1AB0): the NPC's skill service
/// when it may use its heal skill on the PC (0x4AE570, 0x419A00), two submenus (states 12 and 13, PC lines 1100..1199 and 1200..1299) and the spell 64, and in a submenu the
/// spells 60, 61 and 62 (0x419AC0, PC lines 1000..1099 with the spell's name); the way out is the PC line 800..899 with the reply's own response. A service or spell
/// asks a price on the reaction scale (0x418A40; free when the NPC's own leader is the PC), is paid (0x418B30) and then runs (0x419CB0, 0x419D50), the NPC's line 15000 and
/// the PC's class line 1000 as the only way on. The talk reuses the trainer's screens (<see cref="TrainerTalk"/>): the original overlays the dialog state the same way.
/// </summary>
public sealed partial class DialogConversation
{
    // The NPC lines of the talk (0x419830: 14000; 0x419CB0 and 0x419D50: 15000), the healing college (0x4B1AB0 index 12) and the skill the service uses (0x419830: 10).
    private const int HealerMenuGroup = 14000, HealerDoneGroup = 15000, HealingCollege = 12, HealingSkill = 10;

    // The spells of the college 12 the menus offer (0x419830: 0x3C, 0x3D, 0x3E, 0x40) and the rank each needs (more than 0, 1, 2; exactly 5).
    private const int FirstHealingSpell = 60, SecondHealingSpell = 61, ThirdHealingSpell = 62, FifthHealingSpell = 64, FullHealingRank = 5;

    private bool HealerAvailable => _ports.Healer is not null && _ports.Greeting is not null && _ports.World is not null && _ports.Gold is not null
        && (_ports.Purse ?? _ports.Trainer) is not null;

    /// <summary>The <c>h:</c> reply was picked and its action has run: the healer menu opens (ledger HLR1).</summary>
    private void OpenHealer(OfferPlan plan)
    {
        var record = plan.Reply.Record;
        var source = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text, record.Text.PayloadOffset, 0, 2, 0);
        _talk = new TrainerTalk(plan.Reply, plan.Target, source);
        PresentHealerMenu(0);
    }

    /// <summary>The menu of a level (0: the main menu, 1 and 2: the submenus of states 12 and 13; ledger HLR2 to HLR4).</summary>
    [OriginalFunction(0x419830, OriginalFunctionScope.Partial)]
    private void PresentHealerMenu(int level)
    {
        if (!ReadHealerFacts(out var facts)) return;
        if (!NpcTalkLine(HealerMenuGroup, null, out var line)) return;
        var options = new List<TrainerOption>();
        if (level == 0)
        {
            if (facts.SkillAllowed)
            {
                if (!HealerSkillOption(HealingSkill, options)) return;
            }
            if (facts.Rank > 0 && !HealerSubmenuOption(1100, 1199, 1, options)) return;
            if (facts.Rank > 1 && !HealerSubmenuOption(1200, 1299, 2, options)) return;
            if (facts.Rank == FullHealingRank && !HealerSpellOption(FifthHealingSpell, options)) return;
        }
        else if (level == 1)
        {
            if (facts.Rank > 0 && !HealerSpellOption(FirstHealingSpell, options)) return;
            if (facts.Rank > 2 && !HealerSpellOption(ThirdHealingSpell, options)) return;
        }
        else if (level == 2 && facts.Rank > 1 && !HealerSpellOption(SecondHealingSpell, options))
        {
            return;
        }
        // The way out (0x419830 ends with the PC line 800..899 and the reply's own response).
        if (!PcTalkLine(800, 899, classLine: false, out var back)) return;
        options.Add(back with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) });
        Present(line, options);
    }

    private bool HealerSubmenuOption(int first, int last, int level, List<TrainerOption> options)
    {
        if (!PcTalkLine(first, last, classLine: false, out var option)) return false;
        options.Add(option with { Choice = new TrainerChoice(TrainerChoiceKind.HealMenu, level) });
        return true;
    }

    // A skill option (0x419A00): the PC line 900..999 with the skill's name.
    [OriginalFunction(0x419A00, OriginalFunctionScope.Partial)]
    private bool HealerSkillOption(int skill, List<TrainerOption> options) =>
        HealerNamedOption(900, 999, SafeName(() => _ports.Healer!.SkillName(skill)), new TrainerChoice(TrainerChoiceKind.HealService, skill), options);

    // A spell option (0x419AC0): the PC line 1000..1099 with the spell's name.
    [OriginalFunction(0x419AC0, OriginalFunctionScope.Partial)]
    private bool HealerSpellOption(int spell, List<TrainerOption> options) =>
        HealerNamedOption(1000, 1099, SafeName(() => _ports.Healer!.SpellName(spell)), new TrainerChoice(TrainerChoiceKind.HealSpell, Spell: spell), options);

    private bool HealerNamedOption(int first, int last, string? name, TrainerChoice choice, List<TrainerOption> options)
    {
        if (Phase != DialogConversationPhase.AwaitingSelection) return false;
        if (!PcTalkLine(first, last, classLine: false, out var option)) return false;
        options.Add(option with { Text = FormatName(option.Text, name ?? string.Empty), Choice = choice });
        return true;
    }

    // A name the port gives; a throwing port fails the conversation.
    private string? SafeName(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, _talk?.Source));
            return null;
        }
    }

    private bool ReadHealerFacts(out DialogHealerFacts facts)
    {
        facts = new DialogHealerFacts(0, false, 0, 0, false, true);
        try
        {
            facts = _ports.Healer!.Facts(new DialogHealerQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, _talk?.Source));
            return false;
        }
        if (facts.Rank is < 0 or > FullHealingRank || facts.SkillLevel < 0 || facts.SkillTraining < 0)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, _talk?.Source));
            return false;
        }
        return true;
    }

    /// <summary>The skill service or a spell was picked (ledger HLR5, HLR6; states 14 and 15).</summary>
    private void PresentHealerChoice(TrainerChoice choice)
    {
        if (!ReadHealerFacts(out var facts)) return;
        if (choice.Kind == TrainerChoiceKind.HealService) OfferHealerService(choice, facts);
        else OfferHealerSpell(choice, facts);
    }

    // 0x419B50: free for a follower of the PC (the service runs at once), else the price (level + 2) × (tier + 1) of the NPC's heal skill on the reaction scale (0x4C62D0).
    [OriginalFunction(0x419B50, OriginalFunctionScope.Partial)]
    private void OfferHealerService(TrainerChoice choice, DialogHealerFacts facts)
    {
        if (facts.LedByPc) RunHealerEffect(true, choice.Skill, 0, facts.NoPcLeader);
        else PresentHealerQuote((facts.SkillLevel + 2) * (facts.SkillTraining + 1), choice with { NoLeader = facts.NoPcLeader });
    }

    // 0x419C30: free for a follower of the PC, else 100 (0x4B1740) on the reaction scale.
    [OriginalFunction(0x419C30, OriginalFunctionScope.Partial)]
    private void OfferHealerSpell(TrainerChoice choice, DialogHealerFacts facts)
    {
        if (facts.LedByPc) RunHealerEffect(false, 0, choice.Spell, facts.NoPcLeader);
        else PresentHealerQuote(HealerSpellBasePrice, choice with { NoLeader = facts.NoPcLeader });
    }

    // The price screen (0x418A40): the base price on the reaction scale in the NPC's class line 1000, and the yes and no replies.
    private void PresentHealerQuote(int basePrice, TrainerChoice choice)
    {
        if (ReadReactionValue(_talk!.Source) is not { } reaction) return;
        var price = ReactionPrice(reaction, basePrice);
        if (!NpcTalkLine(QuoteGroup, price, out var line)) return;
        if (!PcTalkLine(1, 99, classLine: false, out var yes) || !PcTalkLine(100, 199, classLine: false, out var no)) return;
        var service = choice.Kind == TrainerChoiceKind.HealService;
        var pay = new TrainerChoice(service ? TrainerChoiceKind.PayHealService : TrainerChoiceKind.PayHealSpell, choice.Skill, price, Spell: choice.Spell, NoLeader: choice.NoLeader);
        Present(line, [yes with { Choice = pay }, no with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }

    // 0x4B1740 returns 100 for every spell.
    private const int HealerSpellBasePrice = 100;

    /// <summary>The payment (ledger HLR7: as DTR6) and then the effect.</summary>
    private void PayAndHeal(TrainerChoice choice)
    {
        var talk = _talk!;
        if (!EnoughGold(choice.Price)) return;
        if ((long)_effects.Count + 2 > _options.MaxEffects)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.EffectLimit, CurrentNode), null);
            return;
        }
        var pay = new GoldCommand(_request.Participants.PlayerCharacter, _request.Participants.Npc, -choice.Price);
        if (!RunTalkCommand(pay, () => _ports.Gold!.Execute(pay), talk.Source)) return;
        RunHealerEffect(choice.Kind == TrainerChoiceKind.PayHealService, choice.Skill, choice.Spell, choice.NoLeader);
    }

    /// <summary>The effect, then the NPC's line 15000 and the PC's class line 1000 as the only way on (ledger HLR8, HLR9).</summary>
    private void RunHealerEffect(bool service, int skill, int spell, bool noPcLeader)
    {
        if ((long)_effects.Count + 1 > _options.MaxEffects)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.EffectLimit, CurrentNode), null);
            return;
        }
        if (!(service ? RunHealerSkill(skill, noPcLeader) : RunHealerSpell(spell, noPcLeader))) return;
        PresentNotice(HealerDoneGroup, classAnswer: true);
    }

    // 0x419CB0: the heal skill used by the NPC on the PC (the skill starter 0x4350F0 with the NPC's tool; flag 0x2000 when no PC leads it).
    [OriginalFunction(0x419CB0, OriginalFunctionScope.Partial)]
    private bool RunHealerSkill(int skill, bool noPcLeader)
    {
        var command = new HealerSkillCommand(_request.Participants.PlayerCharacter, _request.Participants.Npc, skill, noPcLeader);
        return RunTalkCommand(command, () => _ports.Healer!.Execute(command), _talk!.Source);
    }

    // 0x419D50: the spell cast by the NPC at the PC (the invocation 0x455A20, flag 2 when no PC leads the NPC, then 0x455AC0).
    [OriginalFunction(0x419D50, OriginalFunctionScope.Partial)]
    private bool RunHealerSpell(int spell, bool noPcLeader)
    {
        var command = new HealerSpellCommand(_request.Participants.PlayerCharacter, _request.Participants.Npc, spell, noPcLeader);
        return RunTalkCommand(command, () => _ports.Healer!.Execute(command), _talk!.Source);
    }

    /// <summary>Replaces the first <c>%s</c> of a generated line with a name as the original's printf does (0x589BD0); <c>%%</c> is one percent sign.</summary>
    internal static string FormatName(string line, string name)
    {
        var builder = new System.Text.StringBuilder();
        var replaced = false;
        for (var at = 0; at < line.Length; at++)
        {
            if (line[at] == '%' && at + 1 < line.Length && line[at + 1] == '%') { builder.Append('%'); at++; }
            else if (!replaced && line[at] == '%' && at + 1 < line.Length && line[at + 1] == 's')
            {
                builder.Append(name);
                replaced = true;
                at++;
            }
            else builder.Append(line[at]);
        }
        return builder.ToString();
    }
}
