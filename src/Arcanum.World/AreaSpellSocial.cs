using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-035 (magic audit MA-4): the cast host's social host over the shown map's AI host (<see cref="ISpellSocialHost"/>): a reaction change, peace, flee and stop fleeing of an NPC the AI host runs. The AI host is
/// asked each time (it is rebuilt with the map); none answers false for every step, which the cast host logs as refused.
/// </summary>
public sealed class AreaSpellSocial(Func<AreaAiHost?> host) : ISpellSocialHost
{
    public bool ChangeReaction(string npc, int delta) => host()?.SpellReaction(npc, delta) == true;

    public bool Peace(string npc, string with) => host()?.SpellPeace(npc, with) == true;

    public bool Flee(string npc, string from) => host()?.SpellFlee(npc, from) == true;

    public bool StopFleeing(string npc) => host()?.SpellStopFleeing(npc) == true;
}
