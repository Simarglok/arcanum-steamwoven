using Arcanum.Simulation;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1169 (E1-f of the missing-systems plan; ledger AR1–AR4): the AIRedirect component (type 3, ProcFunc 0x451700), which Animal Lure (spell 183, its BEGIN and MAINTAIN lines, critter flag animal) uses. The component builds the
/// redirect record of its target object (0x4A9530: the target is both the one the NPCs are turned on and the centre of the search) with its critter-flag bit and <c>MinIQ:</c> and runs the AI redirect (0x4A9560). The rule is
/// <see cref="Arcanum.Simulation.Ai.AiGroundRules.Redirect"/> and its writes are the AI host's (<see cref="ISpellSocialHost.Redirect"/>: the attack event of each NPC, then its focus and who-hit-me-last, 0x4A94C0); this file is only
/// the component's step in the cast host's table.
/// </summary>
public sealed partial class SessionSpellCaster
{
    // 0x451700: nothing for a component with no target object (the record's first handle is 0: 0x4A9560 tests it before anything else); the AI host answers how many NPCs it turned on the target, or null when it does not run it.
    [OriginalFunction(0x451700, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? AiRedirectComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.AiRedirect is not { } data || Social is null) return null;
        if (target.Object is not { } obj) return SpellComponentOutcome.None;
        if (Social.Redirect(obj.Id, data.CritterFlag, data.MinimumIntelligence) is { } count) SocialLine(run, SocialRedirect, obj.Id, count);
        else SocialLine(run, SocialRefused, obj.Id, SocialRedirect);
        return SpellComponentOutcome.None;
    }
}
