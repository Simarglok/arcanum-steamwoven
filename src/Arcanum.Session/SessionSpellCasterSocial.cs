using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-035 (magic audit MA-4): the part of the social and AI flags of a spell that needs the shown map's AI host (the facts of its reaction, danger and goal rules). The cast host asks it
/// in the original's order, as the ObjFlag component's function (0x454A10) calls the reaction change, peace, flee and stop-fleeing functions; each answers whether it ran (an NPC this host does not
/// run, or one the rules refuse, answers false and the cast host logs it). The hero is always the player character of the reaction.
/// </summary>
public interface ISpellSocialHost
{
    /// <summary>The reaction change of <paramref name="npc"/> toward the hero by <paramref name="delta"/> (0x4C0DE0, <see cref="Arcanum.Simulation.Reaction.ReactionRules.Adjust"/>).</summary>
    bool ChangeReaction(string npc, int delta);

    /// <summary>Peace (0x4AA300) of <paramref name="npc"/> with the side of <paramref name="with"/> (an animal's charm): its focus and who-hit-me-last cleared, the danger state NONE, the attack goals at the side ended.</summary>
    bool Peace(string npc, string with);

    /// <summary>Flee (0x4AF130): the danger setter asked for FLEE from <paramref name="from"/> (the fear spell's caster).</summary>
    bool Flee(string npc, string from);

    /// <summary>Stop fleeing (0x4AF170): an NPC in FLEE goes to NONE, then every flee goal (25) of it is ended whatever its state was.</summary>
    bool StopFleeing(string npc);

    /// <summary>ARC-AI-1513: the flee-goal interrupt that stop fleeing (0x4AF170) runs for an NPC in FLEE, which set no-flee (0x4AF1D0, the start of mind control) runs first: every flee goal (25) of the NPC is ended. The session has written the state and the bit; no answer is needed (an NPC this host does not run has no goals here).</summary>
    void EndFleeGoals(string npc) { }
}

/// <summary>
/// ARC-MAGIC-035 (magic audit MA-4, owner decisions D1–D6 of 2026-11-11; the function 0x454A10 re-read branch by branch against the disassembly, ledger SO1 to SO8): the social and AI flags of the
/// ObjFlag component. They are the bits whose set or clear the original does more than write: **charmed** (spell flag 0x800000), **mind controlled** (spell flag 0x8000000), **undead**, **fleeing** and
/// **blinded** (critter flags 0x4, 0x10, 0x80). The function runs its side effects with the run's parent (<c>run +0x58</c>, the caster) as the one who acts, and for the flags that
/// do more than a write it **returns before the closing write** (fleeing, blinded set, and mind control of anything that is no NPC) or writes the flag itself in the middle of its work (mind control).
/// <list type="bullet">
/// <item><b>Charmed</b>, set, on an NPC: an animal (critter flag 0x8) makes peace with the caster (0x4AA300); any other NPC gets <c>+30</c> toward the hero when the caster is the hero (0x4C0DE0);
/// then the NPC flag 0x800 (<c>ONF_KOS_OVERRIDE</c>) is added, which the session keeps in its NPC flag layer (SO2, ARC-MAGIC-1142). Clear, on an NPC: <c>−30</c> toward the hero when the caster is the hero; the NPC flag stays.
/// Any other object: the plain write.</item>
/// <item><b>Mind controlled</b>, set: without a caster the plain write; with one, **nothing at all** for an object that is no NPC (the flag is not written, SO3); for an NPC the reaction <c>−50</c> toward the hero when the
/// caster is the hero and the NPC does not hold the summoned spell flag (0x4000), then the flag, then the NPC joins the caster as a forced follower (0x45DE90), then no-flee for good (0x4AF1D0). Clear, on an NPC: the flag, then a forced
/// leave (0x45DFC0); the reaction is not given back. The party has the hero as the one leader: a caster that is no hero gets no follower (named).</item>
/// <item><b>Fleeing</b>, on an NPC: the flee function (0x4AF130); off: the stop-fleeing function (0x4AF170); **neither writes the critter flag in the original's closing write** (the setter keeps the state; the session's danger
/// record is that state), and on the hero nothing happens.</item>
/// <item><b>Blinded</b>: set, the function 0x4B81B0 writes the critter flag when it is not set (the session's blinded life condition, <see cref="GameSessionOwner.SetBlinded"/>); clear, the plain write.</item>
/// <item><b>Undead</b>: the object flag 0x20000000 is set or cleared (0x43D0E0 / 0x43D280), then the critter flag is written as always.</item>
/// </list>
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The social host of the shown map (<see cref="ISpellSocialHost"/>); null when there is none: the flags that need it stay not ready on an NPC.</summary>
    public ISpellSocialHost? Social { get; set; }

    /// <summary>The detail codes of a <see cref="SpellCastEventKind.SocialEffect"/> line (its <see cref="SpellCastEvent.Value"/>): the reaction change (detail: the delta), peace, flee, stop fleeing, a forced join, a forced leave, no-flee for good, a blinded
    /// condition written (detail 1 on, 0 off), the original's no-ops (<see cref="SocialIgnored"/>) and a step the session does not model (<see cref="SocialNotModelled"/>, detail: 1 a leader that is no hero) or a host that did not run
    /// the step (<see cref="SocialRefused"/>, detail: the effect it was asked).</summary>
    public const int SocialReaction = 1, SocialPeace = 2, SocialFlee = 3, SocialStopFleeing = 4, SocialJoin = 5, SocialLeave = 6, SocialNoFlee = 7, SocialBlinded = 8, SocialIgnored = 9, SocialNotModelled = 10, SocialRefused = 11;

    /// <summary>ARC-MAGIC-1142: the NPC flag 0x800 (<c>ONF_KOS_OVERRIDE</c>, field 0x118) a charm adds and the attack event clears on an NPC victim.</summary>
    public const int KosOverrideNpcFlag = 0x800;

    /// <summary>ARC-MAGIC-035: the critter flag 0x8 (animal), the object flag 0x20000000 the undead bit sets, and the reactions the spells give: charm +30 / −30, mind control −50.</summary>
    public const int AnimalCritterFlag = 0x8, ObjectUndeadMark = 0x20000000, CharmReaction = 30, MindControlReaction = -50;

    // Whether the flag is one of the social and AI ones: charmed and mind controlled among the spell flags, undead, fleeing and blinded among the critter flags.
    private static bool IsSocialFlag(SessionObjectFlagField field, int mask) => field switch
    {
        SessionObjectFlagField.Spell => (mask & (CharmedSpellFlag | MindControlledSpellFlag)) != 0,
        SessionObjectFlagField.Critter => (mask & (UndeadCritterFlag | FleeingCritterFlag | BlindedCritterFlag)) != 0,
        _ => false,
    };

    // Whether the flag needs the social host on this target: charmed, mind controlled and fleeing on an NPC (the hero's are plain writes or no-ops that need no rule).
    private bool SocialHostMissing(SessionObjectFlagField field, int mask, string id) =>
        Social is null && id != Hero && (field == SessionObjectFlagField.Spell || (mask & FleeingCritterFlag) != 0 && field == SessionObjectFlagField.Critter);

    // The one special bit of a mask the original handles first (SU17: only the first special bit; a record names one bit). Charmed and mind controlled are checked in the original's order
    // (mind controlled before charmed in both branches); the critter flags: undead, then fleeing, then blinded.
    private bool SocialFlag(SpellRun run, string id, SessionObjectFlagField field, int mask, bool on)
    {
        var parent = run.Parent?.Id;
        if (field == SessionObjectFlagField.Spell) return (mask & MindControlledSpellFlag) != 0 ? MindControlled(run, id, parent, mask, on) : Charmed(run, id, parent, mask, on);
        if ((mask & UndeadCritterFlag) != 0) return Undead(run, id, mask, on);
        if ((mask & FleeingCritterFlag) != 0) return Fleeing(run, id, parent, on);
        return Blinded(run, id, mask, on);
    }

    private bool IsNpc(string id) => id != Hero;

    private void SocialLine(SpellRun run, int effect, string id, int detail = 0) => _log.Add(new(SpellCastEventKind.SocialEffect, run.Id, effect, id, detail));

    // The reaction change toward the hero (0x4C0DE0): the host's answer.
    private void Reaction(SpellRun run, string npc, int delta)
    {
        if (Social?.ChangeReaction(npc, delta) == true) SocialLine(run, SocialReaction, npc, delta);
        else SocialLine(run, SocialRefused, npc, SocialReaction);
    }

    // 0x454A10 with the spell flag 0x800000 (charmed): only an NPC has side effects; the closing write follows for every object.
    private bool Charmed(SpellRun run, string id, string? parent, int mask, bool on)
    {
        if (IsNpc(id))
        {
            if (on)
            {
                if ((CurrentFlags(id, SessionObjectFlagField.Critter) & AnimalCritterFlag) != 0)
                {
                    // The animal charm: peace with the caster (0x4AA300); the original asks it of a null caster too, which finds no side and clears what the NPC's focus names; a run with no parent has no caster to make peace with.
                    if (parent is not null)
                    {
                        if (Social?.Peace(id, parent) == true) SocialLine(run, SocialPeace, id);
                        else SocialLine(run, SocialRefused, id, SocialPeace);
                    }
                }
                else if (parent == Hero) Reaction(run, id, CharmReaction);
                // NPC flag 0x800 (ONF_KOS_OVERRIDE) is added after it (0x454C74, SO2): the session's NPC flag layer (ARC-MAGIC-1142), a bit the clear branch never takes back.
                WriteFlag(run, id, SessionObjectFlagField.Npc, KosOverrideNpcFlag, true, sideEffect: true);
            }
            else if (parent == Hero) Reaction(run, id, -CharmReaction);
        }
        WriteFlag(run, id, SessionObjectFlagField.Spell, mask, on);
        return true;
    }

    // 0x454A10 with the spell flag 0x8000000 (mind controlled).
    private bool MindControlled(SpellRun run, string id, string? parent, int mask, bool on)
    {
        if (on)
        {
            // No caster: the plain write (a script's or an item's flag with no parent).
            if (parent is null)
            {
                WriteFlag(run, id, SessionObjectFlagField.Spell, mask, true);
                return true;
            }
            // A caster and an object that is no NPC: the function returns before any write (SO3).
            if (!IsNpc(id))
            {
                SocialLine(run, SocialIgnored, id, mask);
                return true;
            }
            // −50 toward the hero comes first, from a caster that is the hero, unless the NPC is a summoned creature (spell flag 0x4000); it reads the NPC before the flag is written.
            if (parent == Hero && (CurrentFlags(id, SessionObjectFlagField.Spell) & SpellTargetObject.Summoned) == 0) Reaction(run, id, MindControlReaction);
            if (!WriteFlag(run, id, SessionObjectFlagField.Spell, mask, true)) return true;
            // The forced join (0x45DE90 with force 1) and no-flee for good (0x4AF1D0). The party has the hero as its one leader.
            if (parent == Hero)
            {
                if (_owner.ForceJoinParty(_owner.Version, id).Disposition != GameSessionDisposition.Rejected) SocialLine(run, SocialJoin, id);
                else SocialLine(run, SocialRefused, id, SocialJoin);
            }
            else SocialLine(run, SocialNotModelled, id, 1);
            // No-flee first stops fleeing (ARC-AI-1513): an NPC in FLEE goes to NONE in the same revision and the host ends its flee goals.
            var fleeing = _owner.QueryDanger(id).State == DangerState.Flee;
            if (_owner.SetNoFlee(_owner.Version, id).Disposition != GameSessionDisposition.Rejected)
            {
                SocialLine(run, SocialNoFlee, id);
                if (fleeing) Social?.EndFleeGoals(id);
            }
            else SocialLine(run, SocialRefused, id, SocialNoFlee);
            return true;
        }
        if (!IsNpc(id))
        {
            WriteFlag(run, id, SessionObjectFlagField.Spell, mask, false);
            return true;
        }
        // An NPC: the flag, then the forced leave (0x45DFC0 with force 1: the unforced refusals of an extraordinary-charisma leader and of mind control do not apply). The reaction is not given back.
        if (!WriteFlag(run, id, SessionObjectFlagField.Spell, mask, false)) return true;
        if (_owner.LeaveParty(_owner.Version, id, new PartyLeave(Force: true)).Disposition != GameSessionDisposition.Rejected) SocialLine(run, SocialLeave, id);
        else SocialLine(run, SocialRefused, id, SocialLeave);
        return true;
    }

    // 0x454A10 with the critter flag 0x4 (undead): the object flag 0x20000000 first, then the closing write of the critter flag.
    private bool Undead(SpellRun run, string id, int mask, bool on)
    {
        WriteFlag(run, id, SessionObjectFlagField.Object, ObjectUndeadMark, on, sideEffect: true);
        WriteFlag(run, id, SessionObjectFlagField.Critter, mask, on);
        return true;
    }

    // 0x454A10 with the critter flag 0x10 (fleeing): the flee or stop-fleeing function and a return before the closing write; the function itself tests that the object is an NPC.
    private bool Fleeing(SpellRun run, string id, string? parent, bool on)
    {
        if (!IsNpc(id))
        {
            SocialLine(run, SocialIgnored, id, FleeingCritterFlag);
            return true;
        }
        if (on)
        {
            // The caster is the one the NPC flees from; a run with no parent gives the setter no source (0x4AF130 passes the handle on as it is).
            if (parent is not null && Social?.Flee(id, parent) == true) SocialLine(run, SocialFlee, id);
            else SocialLine(run, SocialRefused, id, SocialFlee);
        }
        else if (Social?.StopFleeing(id) == true) SocialLine(run, SocialStopFleeing, id);
        else SocialLine(run, SocialRefused, id, SocialStopFleeing);
        return true;
    }

    // 0x454A10 with the critter flag 0x80 (blinded): the set is 0x4B81B0 (the flag when it is not set) and returns; the clear is the plain write. Both are the session's blinded life condition.
    private bool Blinded(SpellRun run, string id, int mask, bool on)
    {
        GameSessionMutationResult? written = LifeTarget(id) is { } life ? _owner.SetBlinded(_owner.Version, life, on)
            : !on && IsNpc(id) ? _owner.ClearRecordedBlinded(_owner.Version, id) : null;
        if (written is not { } result)
        {
            SocialLine(run, SocialRefused, id, SocialBlinded);
            return true;
        }
        if (result.Disposition == GameSessionDisposition.Rejected) _log.Add(new(SpellCastEventKind.ObjectFlagNotSaved, run.Id, (int)result.Failure, id, FlagDetail(SessionObjectFlagField.Critter, on)));
        else SocialLine(run, SocialBlinded, id, on ? 1 : 0);
        return true;
    }
}
