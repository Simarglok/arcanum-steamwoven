namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-1534 (ledger DSV1–DSV3): the Player reply <c>u:N</c>, the original's skill service reply (0x416C10, prefix 0x5A0AC4, either case). The builder reads N with the C library's
/// atoi (0x589CAD), asks 0x4C91F0 for the NPC's tool and 0x4AE570 whether the NPC can do skill N for the PC, and drops the reply when it cannot. For skill 12 (repair) 0x4AE570 wants the
/// target, which the dialog passes as the PC (field 0x14F = 15), to be an item (type 5..14): a critter always fails it, so the reply is never offered. The skills 10 (heal) and 14
/// (pick locks) are other cases of the same test and are not built here.
/// </summary>
public sealed partial class DialogConversation
{
    private const int RepairSkillNumber = 12;

    /// <summary>Whether the text of a <c>u:</c> reply reads, as the original's atoi does, as skill 12 (repair), whose reply is never offered.</summary>
    [OriginalFunction(0x4AE570, OriginalFunctionScope.Partial)]
    internal static bool IsRepairServiceReply(string text) => text.Length >= 2 && ReadAtoi(text, 2) == RepairSkillNumber;

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
