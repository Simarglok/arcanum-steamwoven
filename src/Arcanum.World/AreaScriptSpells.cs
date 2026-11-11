using Arcanum.Session;
using Arcanum.Simulation.Magic;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-030 (MT21b, ledger SC1–SC6): what the AREA script hosts do for the spell commands of set S31 — the four conditions
/// (obj has body spell 14, obj is influenced by spell 35, knows spell 48, mastered spell college 49) read the object's spell book, spell flags
/// and the session's spell runs, and the action give spell mastery in college (112) writes the mastery. This class holds the parts both hosts
/// share: the mastery write and the spell-book facts of one object. Godot-free; typed results, no exception for data.
/// </summary>
public static class AreaScriptSpells
{
    /// <summary>
    /// The mastery write of give spell mastery in college (0x4EF080 → 0x4B1CF0, SC5): the hero (<paramref name="npc"/> null) or an NPC whose loaded
    /// advancement the caller read gets <paramref name="college"/> as its mastered college when the spell-book rule allows it (no mastery yet and
    /// all five ranks, <see cref="SpellBookRules.Master"/>); otherwise nothing changes and the run goes on, as in the original (a college outside
    /// 0..15 is not range-checked there; here it changes nothing). Returns false only when the session could not take a change it should have
    /// (the record budget, the revision, a stale version), which stops the later steps of the run as any refused request does.
    /// </summary>
    public static bool GiveMastery(GameSessionOwner session, SessionNpcAdvancement? npc, int college)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = npc is null ? session.MasterHeroCollege(session.Version, college) : session.MasterNpcCollege(session.Version, npc, college);
        return result.Disposition != GameSessionDisposition.Rejected || result.Failure is GameSessionFailure.SpellBookUnavailable or GameSessionFailure.InvalidSpellBook
            or GameSessionFailure.SpellBookRefused or GameSessionFailure.InvalidAdvancement;
    }
}
