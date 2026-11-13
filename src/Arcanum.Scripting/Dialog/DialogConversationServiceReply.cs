using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-1534 (ledger DSV1–DSV3): the Player reply <c>u:N</c>, the original's skill service reply (0x416C10, prefix 0x5A0AC4, either case). The builder reads N with the C library's
/// atoi (0x589CAD), asks 0x4C91F0 for the NPC's tool and 0x4AE570 whether the NPC can do skill N for the PC, and drops the reply when it cannot. For skill 12 (repair) 0x4AE570 wants the
/// target, which the dialog passes as the PC (field 0x14F = 15), to be an item (type 5..14): a critter always fails it, so the reply is never offered. Skill 14 (pick locks) wants a
/// target that 0x441CF0 accepts (a portal or a container, type 1 or 2): the PC fails it too, so that reply is never offered either (ARC-SCRIPT-1798, ledger DSV4).
/// Skill 10 (heal) is offered when the NPC may heal the PC (0x4AE570: the host's <see cref="DialogHealerFacts.SkillAllowed"/>), any other skill always; the pick runs the skill
/// service of the healer menu (ledger HLR7, HLR9, HLR10) and <c>z:N</c> the spell sale (HLR8, HLR9, HLR11): the replies are built only with the healer menu's ports.
/// </summary>
public sealed partial class DialogConversation
{
    private const int RepairSkillNumber = 12, PickLocksSkillNumber = 14;

    /// <summary>
    /// Whether the text of a <c>u:</c> reply reads, as the original's atoi does, as skill 12 (repair) or 14 (pick locks), whose replies are never offered: 0x4AE570 wants an item or a
    /// portal or container as the target and the dialog passes the PC (ledger DSV2, DSV4).
    /// </summary>
    [OriginalFunction(0x4AE570, OriginalFunctionScope.Partial)]
    internal static bool IsUnreachableServiceReply(string text) =>
        text.Length >= 2 && ReadAtoi(text, 2) is RepairSkillNumber or PickLocksSkillNumber;

    /// <summary>
    /// The pick of a <c>u:N</c> or <c>z:N</c> reply (0x414810 states 0xE and 0xF, ledger DSV3 and DSV4): its action has run, and the skill or spell is offered as the healer menu's option
    /// is (0x419B50, 0x419C30): at once and free for a follower of the PC, else on the price screen.
    /// </summary>
    private void OpenService(OfferPlan plan)
    {
        var record = plan.Reply.Record;
        var source = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text, record.Text.PayloadOffset, 0, 2, 0);
        _talk = new TrainerTalk(plan.Reply, plan.Target, source);
        PresentHealerChoice(plan.Reply.Service!.Value);
    }

    /// <summary>
    /// The builder's test of a <c>u:N</c> reply (0x4AE570 for the skill the reply names): skill 10 asks the host whether the NPC holds a healing item and may heal the PC, every other
    /// skill passes. Null when a port failed the conversation.
    /// </summary>
    private bool? ServiceOffered(TrainerChoice service)
    {
        if (service.Kind != TrainerChoiceKind.HealService || service.Skill != HealingSkill) return true;
        return ReadHealerFacts(HealingSkill, out var facts) ? facts.SkillAllowed : null;
    }

    /// <summary>The line of a service reply: the PC line 900..999 (1000..1099) with the skill's (spell's) name (0x419A00, 0x419AC0); false when the name port failed the conversation.</summary>
    private bool ServiceText(TrainerChoice service, string text, out string formatted)
    {
        var name = service.Kind == TrainerChoiceKind.HealService ? SafeName(() => _ports.Healer!.SkillName(service.Skill)) : SafeName(() => _ports.Healer!.SpellName(service.Spell));
        formatted = FormatName(text, name ?? string.Empty);
        return Phase != DialogConversationPhase.Failed;
    }

    // The C library's atoi: blanks, an optional sign, then decimal digits up to the first other character; nothing readable is 0.
    private static int ReadAtoi(string text, int start)
    {
        var at = start;
        while (at < text.Length && text[at] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r')
        {
            at++;
        }
        var negative = false;
        if (at < text.Length && text[at] is '+' or '-')
        {
            negative = text[at] == '-';
            at++;
        }
        var value = 0;
        while (at < text.Length && text[at] is >= '0' and <= '9')
        {
            // No cap: the original's loop wraps modulo 2^32 (0x589C22).
            value = unchecked(value * 10 + (text[at] - '0'));
            at++;
        }
        return negative ? unchecked(-value) : value;
    }
}
