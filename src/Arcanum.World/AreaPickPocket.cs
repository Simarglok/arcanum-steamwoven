using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Skills;

namespace Arcanum.World;

/// <summary>What a pick-pocket start found (ARC-SKILLS-010): the rule's answer (null when the viewer cannot answer it) and a detail
/// (identifiers and statuses only).</summary>
public sealed record AreaPickPocketStart(PickPocketOpen? Open, string Detail);

/// <summary>How a noticed failure's consequences went (PP7): where they stopped, the victim's catching-thief run (null without a script
/// at point 11) and whether the attack event ran.</summary>
public sealed record AreaPickPocketCaught(PickPocketCaughtStop? Stop, AreaScriptedUse? Script, bool AttackEvent, string Detail);

/// <summary>
/// ARC-SKILLS-010: the AREA viewer's facts for the pick-pocket skill (contract docs/world/pick-pocket-runtime.md). The start reads the
/// target's critter fields (the "no pickpocket" and mechanical flags, the stoned spell flag), its life and the hero's sight of it through
/// the map's AI host (<see cref="AreaAiHost.SeeToward"/>); the victim's facts for the session's attempt come from the same host's critter
/// view (perception, alignment, state, awareness of the hero). Godot-free; it holds no state.
/// </summary>
public static class AreaPickPocket
{
    /// <summary>The hero's id in the AI host's world.</summary>
    public const string Pc = AiSchedulerRules.Pc;

    /// <summary>ARC-DEV-304 (GT115): the status of the loot click a pick-pocket start on a dead target sends. The click goes through the click rules, so a body that carries the
    /// spoken-with-dead spell flag resolves to the talk (goal 21) and not the loot (goal 81); the line names the goal the click resolved to.</summary>
    public static string LootStatus(string name, HeroRequestResult click)
    {
        ArgumentNullException.ThrowIfNull(click);
        if (click.Outcome != HeroRequestOutcome.Done) return click.Click == Arcanum.Simulation.Goals.HeroClickGoal.Talk ? $"{name}: the talk order was refused" : $"{name}: the loot order was refused";
        return click.Click == Arcanum.Simulation.Goals.HeroClickGoal.Talk ? $"{name}: walking to talk, goal 21 started" : $"{name}: walking to loot, goal 81 started";
    }

    /// <summary>
    /// The start on <paramref name="npc"/> (0x57A320 through <see cref="PickPocketRules.Open"/>): null when the critter's fields do not read
    /// or the AI host does not know it (then nothing opens). The hero's sight is taken turned toward the target.
    /// </summary>
    public static AreaPickPocketStart Start(GameSessionOwner session, AreaAiHost ai, CombatFields? fields, string npc)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(npc);
        if (fields is not { Critter: { } critter, Object: { } obj }) return new(null, "the critter's fields do not read");
        if (ai.Find(npc) is not { } target) return new(null, "the AI host does not know the critter");
        var heroDead = session.QueryHeroLife()?.Status.IsDead ?? true;
        var sight = ai.SeeToward(Pc, npc);
        var facts = new PickPocketOpenFacts(heroDead, npc == Pc, ai.Stoned(npc),
            (critter.Flags2 & PickPocketRules.NoPickpocketFlag2) != 0, (critter.Flags & PickPocketRules.MechanicalFlag) != 0, target.Dead, sight.Excess == 0);
        return new(PickPocketRules.Open(facts), $"sight={sight.Excess}/{sight.Reason} flags=0x{critter.Flags:X8} flags2=0x{critter.Flags2:X8} dead={target.Dead}");
    }

    /// <summary>
    /// The victim's facts for <see cref="GameSessionOwner.PickPocket"/>: the AI host's effective perception and stored alignment of the
    /// critter, its state (unconscious, asleep, stunned; a paralysis the host folds into stunned reads as stunned), and whether it is unaware
    /// of the hero (out of combat mode and neither seeing nor hearing it at normal loudness, SC6). The viewer has no summoned critters, so a
    /// victim is never the hero's summon. Null when the host does not know it.
    /// </summary>
    public static SessionPickPocketVictim? Victim(AreaAiHost ai, string npc)
    {
        ArgumentNullException.ThrowIfNull(ai);
        if (ai.Find(npc) is not { } critter) return null;
        var conditions = (critter.Unconscious ? SkillTargetConditions.Unconscious : 0) | (critter.Asleep ? SkillTargetConditions.Sleeping : 0) |
            (critter.Stunned ? SkillTargetConditions.Stunned : 0);
        var unaware = !critter.InCombatMode && ai.See(npc, Pc).Excess != 0 && ai.Hear(npc, Pc, Loudness.Normal).Excess != 0;
        return new(npc)
        {
            Perception = Math.Clamp(critter.Perception, -GameSessionOwner.MaxPickPocketStat, GameSessionOwner.MaxPickPocketStat),
            Alignment = Math.Clamp(critter.Alignment, SessionHeroLimits.MinAlignment, SessionHeroLimits.MaxAlignment),
            Conditions = conditions,
            Unaware = unaware,
        };
    }

    /// <summary>
    /// A noticed failure's consequences (PP7) after the session's attempt: the PC thief has no script of its own here (point 33 is not
    /// run); the victim's script at point 11 runs through <paramref name="catchingThief"/> when its row has one (any answer but run
    /// default stops; a run the host cannot finish stops too, as Not ready, and applies nothing); then a victim the hero leads stops; else
    /// the attack event of the hero on the victim runs through <paramref name="attack"/> (the AI host's <see cref="AreaAiHost.HeroAttacked"/>).
    /// </summary>
    public static AreaPickPocketCaught Caught(GameSessionOwner session, AreaObjectRecord victim, string npc, Func<AreaObjectRecord, AreaScriptedUse>? catchingThief,
        Action<string> attack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(victim);
        ArgumentNullException.ThrowIfNull(attack);
        AreaScriptedUse? ran = null;
        var runsDefault = true;
        if (AreaUseScriptHost.LoadedSlot(victim, AreaUseScriptHost.CatchingThiefPoint) is { Script: > 0 })
        {
            if (catchingThief is null) return new(null, null, false, "catching-thief script: no script host (Not ready)");
            ran = catchingThief(victim);
            if (ran.Outcome != AreaScriptedUseOutcome.NoTeleport) return new(null, ran, false, $"catching-thief script {ran.Outcome}: {ran.Detail} (Not ready)");
            runsDefault = ran.Run?.Outcome != Arcanum.Scripting.Scr.ScrOutcome.SkipDefault;
        }
        var stop = PickPocketRules.Caught(runsDefault, session.QueryCompanion(npc).LedByPc);
        if (stop != PickPocketCaughtStop.None) return new(stop, ran, false, $"stop={stop}");
        attack(npc);
        return new(stop, ran, true, "attack event");
    }
}
