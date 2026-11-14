using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-AI-1527 (FR-DIALOG-003, VB4-1..VB4-5): the followers' comment lines in the shown map: the combat taunt at the end of the hero's hit (0x4B6B90 → 0x4AE9E0), the near-death line of a follower an attack brought below a fifth of
// its hit points (0x4AEAB0, from the damage step 0x4B4390) and the upset line of a complaint (0x414370). The AI host chooses the comment and names the speaker and the message range (AreaAiHost.FollowerSpeaks); the conversation host
// reads a random line of the range from the player's generated files and floats it over the follower (AreaConversationHost.SayFollowerLine). The texts are read at run time and never stored; no voice is played.
public partial class TerrainViewerApp
{
    /// <summary>
    /// The follower <paramref name="entity"/> says a line of <paramref name="lines"/>: the can-speak test first (0x4AD800 with its second argument 1, its head and the unconscious test; its tail is not modelled), then the
    /// conversation host's float. Nothing is said without a conversation host, a speaker of it or line texts (a diagnostic run without the player's game).
    /// </summary>
    private void SayFollowerLine(string entity, LoyaltyLines lines) => SayLine(entity, lines, Arcanum.Simulation.Ai.AiSchedulerRules.Pc, refusesFightOrFlee: false);

    /// <summary>
    /// ARC-UI-2079 (ledger TB28): an AI bark: the speaker's line of <paramref name="lines"/> and the listener the speech display turns it to (an entity id, the hero's <c>"pc"</c>, or null for none). The original's builders write a blank
    /// line when their can-speak test fails and the callback is called all the same, so the speaker still turns to its listener then (<see cref="FaceListenerOf"/>, which has its own gates); the turn is not made when the speaker is not
    /// a speaker of the map or the host has no line texts (a diagnostic run). <paramref name="refusesFightOrFlee"/>: the can-speak test with flag 0 (the order refusals and farewells), which also refuses an NPC in combat focus or fleeing.
    /// </summary>
    private void SayLine(string entity, LoyaltyLines lines, string? listener, bool refusesFightOrFlee)
    {
        if (_conversationHost is not { } talk || _areaSession is not { } session)
        {
            GD.Print($"ARC-AI-1527 follower-line speaker={entity} lines={lines.First}-{lines.Last} nothing said (no conversation host)");
            return;
        }
        var speaker = talk.Speakers.FirstOrDefault(candidate => AreaConversationState.EntityId(candidate.Row) == entity);
        if (speaker is null)
        {
            GD.Print($"ARC-AI-1527 follower-line speaker={entity} lines={lines.First}-{lines.Last} nothing said (not a speaker of the map)");
            return;
        }
        if (!FollowerCanSpeak(session, speaker, entity) || refusesFightOrFlee && _aiHost?.ReadDanger(entity) is Arcanum.Simulation.Ai.DangerState.CombatFocus or Arcanum.Simulation.Ai.DangerState.Flee)
        {
            GD.Print($"ARC-AI-1527 follower-line speaker={entity} lines={lines.First}-{lines.Last} nothing said (cannot speak)");
            if (listener is not null) FaceListenerOf(entity, listener);
            return;
        }
        var text = talk.SayFollowerLine(speaker, lines, listener);
        _followerLinesSaid += text is null ? 0 : 1;
        GD.Print($"ARC-AI-1527 follower-line speaker={entity} lines={lines.First}-{lines.Last} listener={listener ?? "-"} said={(text is null ? "nothing" : text)}");
        // The line pick found nothing (a range without an entry, a refused roll): the original's display still turns the speaker to its listener.
        if (text is null && listener is not null) FaceListenerOf(entity, listener);
    }

    private int _followerLinesSaid;

    /// <summary>The can-speak test (0x4AD800 with flag 1) as far as it is modelled: its head (<see cref="CanSpeakRules.Head"/>: destroyed or off, stunned, paralysed or mute, asleep, stoned, dead) and the spent fatigue (0x45D800).</summary>
    private bool FollowerCanSpeak(GameSessionOwner session, AreaSpeaker speaker, string entity)
    {
        var fields = _combatHost?.Fields(entity);
        var written = session.QueryObjectFlags(entity);
        var critter = unchecked((uint)written.Critter(unchecked((int)(fields?.Critter?.Flags ?? 0))));
        var spell = unchecked((uint)written.Spell(unchecked((int)(fields?.Object.SpellFlags ?? 0))));
        var unconscious = _combatHost?.Build(session, CurrentAreaMap, entity).Combat is { } combat && session.QueryLife(combat.Target).View is { } view && view.Status.IsUnconscious;
        var head = CanSpeakRules.Head(new CanSpeakFacts(StunnedParalysedOrMute: (critter & 0x2060) != 0, Asleep: (critter & 0x1000) != 0, Stoned: (spell & 0x10000) != 0,
            Dead: SpeakerDead(speaker), SpokenWithDead: (spell & unchecked((uint)SessionSpellCaster.SpokenWithDeadSpellFlag)) != 0, Unconscious: unconscious));
        return head is null or CanSpeakRules.Free;
    }

    /// <summary>
    /// ARC-AI-1527 (0x4AEAB0): what a hit did to <paramref name="id"/> as the damage step's near-death test reads it: a follower of the PC that fell from 20 % or more of its hit points to below says its near-death line. Returns
    /// the comment, or null.
    /// </summary>
    private FollowerComment? FollowerHurt(string id, LifeOutcome? life)
    {
        if (life is null || id == Arcanum.Simulation.Ai.AiSchedulerRules.Pc || _areaSession is not { } session || _aiHost is not { } host || !session.QueryCompanion(id).LedByPc
            || _combatHost?.Build(session, CurrentAreaMap, id).Combat is not { } combat || session.QueryLife(combat.Target).View is not { } view) return null;
        // ARC-AI-1638: the hit points before and after are the outcome's own (maximum minus the state's damage), so a second step queued in the same frame does not skew the first one's ratios.
        var max = view.Status.MaxHitPoints;
        return host.Hurt(id, max - life.Before.HitPointDamage, max - life.After.HitPointDamage, max, life.Events.HasFlag(LifeEvents.Died));
    }

    /// <summary>
    /// ARC-AI-1527: the followers' comments on one resolved attack on a critter: the near-death line of whoever took the damage (the attack damage path of 0x4B4390) and, for the hero's own hit, the combat taunt of the end of the attack
    /// (0x4B6B90). <paramref name="aimed"/> is whether the struck critter is the one the attack was aimed at; the stray taunt kind needs attack-record flag 0x8, which only the critical-miss self-hit writes (ARC-AI-1639), and
    /// <paramref name="selfHitReachesTaunt"/> is whether the attack's end calls the taunt at all (only for an attack without a flight).
    /// </summary>
    private void FollowerAttackComments(SessionAttackResult result, string attacker, string target, bool aimed = true, bool selfHitReachesTaunt = false)
    {
        if (result.Disposition == GameSessionDisposition.Rejected || result.Resolution?.Refusal != AttackRefusal.None) return;
        var damaged = result.Recipient == DamageRecipient.Attacker ? attacker : target;
        var hurt = FollowerHurt(damaged == HeroFloater ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : damaged, result.Life);
        if (hurt is { } near) GD.Print($"ARC-AI-1527 near-death follower={near.Speaker} lines={near.Lines.First}-{near.Lines.Last} after-attack");
        if (attacker != HeroFloater || _aiHost is not { } host || _areaSession is not { } session) return;
        var outcome = result.Resolution?.Outcome;
        // ARC-AI-1639: a critical miss is the hero's self-hit, the one writer of record flag 0x8 (kind 1, a stray hit) when the attack's end calls 0x4B6B90 (FollowerRules.HeroAttackTauntFacts).
        var taunt = host.AttackEnded(FollowerRules.HeroAttackTauntFacts(_gameSettings.CombatTaunts, outcome, session.QueryCompanion(target).LedByPc, aimed, selfHitReachesTaunt));
        if (taunt is { } comment) GD.Print($"ARC-AI-1527 taunt follower={comment.Speaker} lines={comment.Lines.First}-{comment.Lines.Last} target={target} critical={outcome is AttackOutcome.CriticalHit}");
    }
}
