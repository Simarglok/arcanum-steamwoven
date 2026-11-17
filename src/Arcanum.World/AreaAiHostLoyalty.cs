using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Skills;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-2130: the loyalty step of the non-combat perform (0x4AC380) for a follower whose DIRECT leader (0x45DE00) is an NPC. The original runs the same step for any leader: the follow check
/// (0x4AD950) with the follower limit ignored, a refusal gives an unforced leave (0x45DFC0), and a leave that went through sets JILTED and says the refusal line. The hero's followers
/// run it through the party (<see cref="GameSessionOwner.RunLoyaltyStep"/>, <c>AreaCompanions.Loyalty</c>), so the host leaves a hero-led follower to that.
/// </summary>
public sealed partial class AreaAiHost
{
    /// <summary>The reaction a non-PC target gives (0x4C0CC0 → 0x4C0D00): a fixed 50, or 0 when the target is in the NPC's enemy list (0x4AFB30).</summary>
    internal const int NpcLeaderReaction = 50;

    /// <summary>
    /// The loyalty check after the follow request (<see cref="IAiPerformWorld.LoyaltyCheck"/>, 0x4AC380) of an NPC with a NPC leader that it can read: no step while the follow request just started the
    /// follow goal or another goal of the action class runs (OL18, the overlay goals do not count); otherwise <see cref="NpcLoyaltyStep"/> with the leader link's forced bit. The check-leader step (0x4ADB50)
    /// does not run: every setter of its flag (0x4C0DE0, 0x4B0980) needs a PC leader, so an NPC leader never arms it. NOT READY: whether the original reads another map's NPC as a leader (a handle that
    /// does not resolve answers no leader, 0x406FB0, and the pass takes the free branch); a leader this host cannot read here gives no step.
    /// </summary>
    public void LoyaltyCheck(string npc, bool followStarted)
    {
        if (followStarted || AnimationRunning(npc)) return;
        if (Leader(npc) is not { } leader || leader == Pc) return;
        if (_session.QueryNpcLink(npc) is not { } link) return;
        NpcLoyaltyStep(npc, leader, link.Forced);
    }

    /// <summary>
    /// The step itself: the follow check of <paramref name="npc"/> against the NPC <paramref name="leader"/> (<see cref="NpcLedConsent"/>); a refusal gives the unforced leave
    /// (<see cref="LeaveUnforced"/>: an extraordinary-charisma leader refuses it, and so does a mind-control controller record, <see cref="MindController"/>: any controller refuses, whoever it is, 0x45DFC0 via 0x459040; the step goes on as if accepted); a leave that went through sets JILTED
    /// (the session's) and, when the NPC is active and a line callback exists, says the refusal line of its reason (<see cref="CompanionLoyaltyRules.RefusalLines"/>, 0x413A90). Returns the consent, or
    /// null when the leader or the NPC could not be read here (nothing ran).
    /// </summary>
    internal FollowConsent? NpcLoyaltyStep(string npc, string leader, bool forced)
    {
        if (NpcLedConsent(npc, leader, forced) is not { } consent) { Trace(npc).Event = "loyalty: leader not readable"; return null; }
        if (consent == FollowConsent.Accepted) return consent;
        var left = LeaveUnforced(npc, MindController(npc) is not null, jilt: true);
        Trace(npc).Event = $"loyalty: {consent} {(left.Disposition == GameSessionDisposition.Changed ? "left " + leader : "stays (" + left.Failure + ")")}";
        if (left.Disposition == GameSessionDisposition.Changed && CompanionLoyaltyRules.RefusalLines(consent) is { } lines && FollowerSpeaks is not null && Active(npc)) Speak(npc, lines);
        return consent;
    }

    /// <summary>
    /// The follow check (0x4AD950, <see cref="FollowConsentRules.Evaluate"/>) of <paramref name="npc"/> against its NPC <paramref name="leader"/>, the limit ignored: mind control accepts when the controller is the leader
    /// or, with no controller record (<see cref="MindController"/> answers null: the spell run that sets the flag holds the NPC, ARC-WORLD-2144), a PC leads the NPC up its chain (0x45DDA0); the leader's Persuasion training, alignment and level are its own; the reaction is <see cref="NpcLeaderReaction"/> (0 for a
    /// leader in the NPC's enemy list); the follow thresholds are the NPC's AI row fields 5 to 8; the forced bit is the link's. The alignment is the stat getter's value (<see cref="AlignmentOf"/>: the stored base with TraitIdx writes laid over it, then the effect list's changes of the stat); the level is the stored one (TraitIdx writes laid over), without the effect list's modifiers (NOT READY, as the hostility facts, #2155). Never "already in group" (the leader is the NPC's own). Null when the leader's or the NPC's stats
    /// or AI row are not readable here.
    /// </summary>
    internal FollowConsent? NpcLedConsent(string npc, string leader, bool forced)
    {
        if (Fields(npc)?.Critter is null || Build(npc) is not { } build || Row(npc) is not { } row) return null;
        if (CharismaOf(leader) is not { } charisma || Fields(leader)?.Critter is null || Build(leader) is not { } leaderBuild) return null;
        var controller = MindController(npc);
        var mindControlled = (SpellFlagsOf(npc, Fields(npc)!.Object.SpellFlags) & MindControlledSpell) != 0
            && (controller is null ? PcLeader(npc) is not null : controller == leader);
        var persuasion = (PersuasionTraining)(int)(NpcSkill(leader, Skill.Persuasion)?.Training ?? SkillTraining.None);
        var reaction = _session.QueryDanger(npc).Enemies.Contains(SessionCombatant.Critter(leader)) ? 0 : NpcLeaderReaction;
        return FollowConsentRules.Evaluate(new(mindControlled, persuasion, charisma, AlignmentOf(leader) ?? 0, AreaAdvancement.Level(_session, leader, leaderBuild.Level), OtherLeaderCharisma: null,
            forced, reaction, new(row.FollowReaction, row.FollowAlignmentAbove, row.FollowAlignmentBelow, row.JoinLevelGap), AlignmentOf(npc) ?? 0, AreaAdvancement.Level(_session, npc, build.Level),
            MaxFollowers: 0, NonForcedFollowers: 0, IgnoreCharismaLimit: true));
    }

    /// <summary>
    /// ARC-WORLD-2148 (0x4B0490 for stat 0x13, read in the executable): the NPC's alignment as the follow check reads it, through the stat getter: the stored base (the value a TraitIdx spell wrote lies over it,
    /// <see cref="AreaCombatHost.Fields(string)"/>), then every change of stat 19 in the effect list the session holds for it (or the loaded one), as the getter aggregates them (0x4EA6C0), clamped to the
    /// stat's range. Null when the NPC's fields do not read here.
    /// </summary>
    internal int? AlignmentOf(string npc)
    {
        if (Fields(npc)?.Critter is not { } critter) return null;
        var stored = critter.BaseStats.Count > AreaCompanionData.AlignmentStat ? critter.BaseStats[AreaCompanionData.AlignmentStat] : 0;
        var changes = new List<EffectModifier>();
        if (RawBuild(npc) is { } build && _session.QueryNpcEffects(build.Target) is { } view)
        {
            var rules = view.Definitions.ToDictionary(definition => definition.Number);
            foreach (var entry in view.Entries)
            {
                if (!rules.TryGetValue(entry.Number, out var rule)) continue;
                foreach (var change in rule.Changes)
                    if (change.Kind == EffectAttributeKind.Stat && change.Attribute == AreaCompanionData.AlignmentStat)
                        changes.Add(new(AreaCompanionData.Operator(change.Operator), change.Value));
            }
        }
        return Math.Clamp(EffectModifiers.Apply(stored, changes), AreaCompanionData.MinAlignment, AreaCompanionData.MaxAlignment);
    }
}
