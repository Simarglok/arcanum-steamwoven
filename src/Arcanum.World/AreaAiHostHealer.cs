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

    /// <summary>The facts of a speaker the host holds; null for an object that is no NPC of this host (the hero's own facts are not asked).</summary>
    public AreaHealerFacts? HealerFacts(string npc)
    {
        if (npc == Pc || !_npcs.ContainsKey(npc)) return null;
        var skill = NpcSkill(npc, Skill.Heal);
        var item = HealingItem(npc);
        var allowed = skill is not null && item is not null && HealSkillAccepts(npc, item, Pc);
        var leader = Leader(npc);
        return new(CollegeLevel(npc, HealerCollege), allowed, skill?.Level ?? 0, (int?)skill?.Training ?? 0, leader == Pc, leader is null);
    }

    /// <summary>
    /// The skill service (0x419CB0 → 0x4350F0 with skill 10 on the PC and the NPC's healing item): the skill starter's goal 33 starts on the NPC as the script's steal starts its own;
    /// a starter that refuses changes nothing and the talk goes on, as in the original (the answer of 0x4350F0 is not read). False only for an object that is no NPC of this host.
    /// </summary>
    public bool HealerService(string npc)
    {
        if (npc == Pc || !_npcs.ContainsKey(npc)) return false;
        var current = RunOf(npc);
        var result = AiExecutorRules.StartUseSkill(this, npc, Pc, AiPassChoice.HealSkill, HealingItem(npc), current);
        Started(npc, current, result);
        return true;
    }

    /// <summary>
    /// A healing spell of the menu (0x419D50: the invocation of the spell by the NPC at the PC, flag 0x2 when no PC leads the NPC, then 0x455AC0): the cast host's invocation with the
    /// NPC as the source, as a script's cast of an NPC starts it (<see cref="ScriptCast"/>), the free flag as the original sets it and no unresistable flag. False only for an object
    /// that is no NPC of this host or without a cast host.
    /// </summary>
    public bool HealerCast(string npc, int spell, bool free)
    {
        if (npc == Pc || !_npcs.ContainsKey(npc) || _scene.Spells?.Invoke() is null) return false;
        ScriptCast(npc, Pc, spell, free, unresistable: false);
        return true;
    }
}
