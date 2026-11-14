using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Skills;

namespace Arcanum.World;

/// <summary>
/// What the healer menu (ledger HLR2–HLR6) reads of an NPC: its rank in the healing college 12 (0x4B1AB0), whether its skill service may run (0x4AE570 for skill 10),
/// its heal skill's level and training tier (0x4C5EB0, 0x4C60C0), and its leaders (0x45DE00: the PC leads it; 0x45DDA0: no PC leads it at all).
/// </summary>
public readonly record struct AreaHealerFacts(int Rank, bool SkillAllowed, int SkillLevel, int SkillTraining, bool LedByPc, bool NoPcLeader);

/// <summary>
/// ARC-SCRIPT-1732 (ledger HLR1–HLR12): the healer's talk through the shown map's AI host. The dialog's <c>h:</c> menu reads the speaker's facts here and asks for the two
/// effects, which start as the original starts them (0x419CB0: the skill starter 0x4350F0 with the NPC's tool item; 0x419D50: the spell invocation 0x455A20 and its dispatcher
/// 0x455AC0), through the same entry points a script's cast and a skill goal use.
/// </summary>
public sealed partial class AreaAiHost
{
    /// <summary>The healing college whose rank sets the healer menu (0x419830 reads 0x4B1AB0 with 12).</summary>
    public const int HealerCollege = 12;

    /// <summary>The facts of a speaker the host runs, an NPC or a companion of the shown map (ARC-SCRIPT-1996: a follower is run through the same host, in its follower list); null for an object the host does not run (the hero's own facts are not asked).</summary>
    public AreaHealerFacts? HealerFacts(string npc, int skillNumber = AiPassChoice.HealSkill)
    {
        if (npc == Pc || !Walks(npc)) return null;
        // ARC-SCRIPT-1798 (ledger DSV2, DSV5): the test of 0x4AE570 is the heal skill's (a healing item the NPC holds and may use on the PC); any other skill the dialog offers passes it. The level and
        // tier are the NPC's own in that skill (0 for a number outside the 16 skills, as the getters 0x4C5EB0 and 0x4C65E0 answer).
        var skill = Enum.IsDefined((Skill)skillNumber) && skillNumber >= 0 ? NpcSkill(npc, (Skill)skillNumber) : null;
        var allowed = skillNumber != AiPassChoice.HealSkill || HealingItem(npc) is { } item && HealSkillAccepts(npc, item, Pc);
        // 0x45DE00 for the free services, 0x45DDA0 for "no player character leads it at all" (ARC-WORLD-1840: a follower of an NPC has a leader but may have no PC at the top).
        return new(CollegeLevel(npc, HealerCollege), allowed, skill?.Level ?? 0, (int?)skill?.Training ?? 0, Leader(npc) == Pc, PcLeader(npc) is null);
    }

    /// <summary>
    /// The skill service (0x419CB0 → 0x4350F0 with skill 10 on the PC and the NPC's healing item): the skill starter's goal 33 starts on the NPC as the script's steal starts its own;
    /// a starter that refuses changes nothing and the talk goes on, as in the original (the answer of 0x4350F0 is not read). <paramref name="noPcLeader"/> is the extra start flag 0x2000 the original
    /// passes when no PC leads the NPC (0x45DDA0 is 0): the starter takes it, and none of the goals the starter queues reads it (ARC-SCRIPT-1789, ledger HLR10), so the run is the same either way.
    /// False only for an object the host does not run (an NPC or a companion of the shown map: ARC-SCRIPT-1996).
    /// </summary>
    public bool HealerService(string npc, int skillNumber = AiPassChoice.HealSkill, bool noPcLeader = false)
    {
        if (npc == Pc || !Walks(npc)) return false;
        // ARC-SCRIPT-1798 (ledger DSV5): the starter takes any skill number the reply names; the tool is the heal item for skill 10 and none for the others (0x4C91F0). A number outside the 16 skills
        // is passed on by the original's starter and is a no-op here (no goal is started for it); the goal's attempt of a skill other than heal and pick pocket is not built (AttemptSkill).
        if (skillNumber < 0 || !Enum.IsDefined((Skill)skillNumber)) return true;
        var current = RunOf(npc);
        var result = AiExecutorRules.StartUseSkill(this, npc, Pc, skillNumber, skillNumber == AiPassChoice.HealSkill ? HealingItem(npc) : null, current,
            noPcLeader ? AiExecutorRules.NoPcLeaderRequestFlag : 0);
        Started(npc, current, result);
        return true;
    }

    /// <summary>
    /// A healing spell of the menu (0x419D50: the invocation of the spell by the NPC at the PC, flag 0x2 when no PC leads the NPC, then 0x455AC0): the cast host's invocation with the
    /// NPC as the source, as a script's cast of an NPC starts it (<see cref="ScriptCast"/>), the free flag as the original sets it and no unresistable flag. False only for an object
    /// the host does not run (an NPC or a companion of the shown map: ARC-SCRIPT-1996) or without a cast host.
    /// </summary>
    public bool HealerCast(string npc, int spell, bool free)
    {
        if (npc == Pc || !Walks(npc) || _scene.Spells?.Invoke() is null) return false;
        ScriptCast(npc, Pc, spell, free, unresistable: false);
        return true;
    }
}
