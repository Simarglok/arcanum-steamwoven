using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-1169 (E1-f, ledger AR1–AR4): the AI redirect (0x4A9560, record 0x4A9530) of a spell's AIRedirect component (0x451700) over the shown map: <see cref="AiGroundRules.Redirect"/> picks the NPCs, and for each one the
/// host runs what 0x4A94C0 does: the attack event with the target as attacker (loudness 0, no flags, 0x4A9650) and then the session's <see cref="GameSessionOwner.RedirectDanger"/> (its focus and who-hit-me-last := the target).
/// </summary>
public sealed partial class AreaAiHost : IAiRedirectWorld
{
    /// <summary>The vicinity of the redirect (0x440FC0 with the type mask 0x18000): every PC and NPC of the fixed play-view rectangle around the target (<see cref="ScrWorld.VicinityReach"/> tiles on both axes), the target included (the rule skips it).</summary>
    public IReadOnlyList<string> Vicinity(string target) => Square(target, ScrWorld.VicinityReach);

    /// <summary>The critter flags of an object (field 0xDA) as the host reads them (the data's with the session's written bits); the hero reads none.</summary>
    public uint CritterFlags(string id) => id != Pc && Fields(id)?.Critter is { } critter ? CritterFlagsOf(id, critter.Flags) : 0;

    /// <summary>
    /// The AIRedirect component's step: every NPC <see cref="AiGroundRules.Redirect"/> finds near <paramref name="target"/> is attacked by it in the host's attack event (0x4A9650, loudness 0, no flags) and then given the target as its combat
    /// focus and who-hit-me-last (0x4A94C0). Answers how many NPCs were redirected; null when this host does not know the target (a target of another map). The candidate list is read once before the first attack event, as the original's is.
    /// </summary>
    public int? SpellRedirect(string target, uint critterFlag, int minimumIntelligence)
    {
        if (Find(target) is null || Combatant(target) is not { } attacker) return null;
        var component = new AiRedirectComponent(critterFlag, minimumIntelligence);
        var count = 0;
        foreach (var npc in AiGroundRules.Redirect(this, target, component, _session.Rules.Combat))
        {
            if (npc == target || Find(npc) is not { Kind: HostilityKind.Npc, Dead: false }) continue;
            AttackEvent(target, npc, AttackEventFlags.None, Loudness.Silent);
            Trace(npc).Setter = $"redirect from {target} {_session.RedirectDanger(_session.Version, npc, attacker).Disposition}";
            count++;
        }
        return count;
    }
}
