using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Party;

namespace Arcanum.World;

/// <summary>
/// The NPCs whose follow consent and follow movement run through the session party (ARC-WORLD-013, ARC-WORLD-016).
/// Only measured NPCs are listed; every other speaker keeps the diagnostic follow key of ARC-SCRIPT-005. The PC side
/// is the AREA diagnostic profile (charisma 8, level 1, no Persuasion), not a character sheet; with a session hero
/// (`--hero`) the session replaces it with the hero's persuasion, charisma, level and max followers (ARC-SKILLS-002).
/// </summary>
public static class AreaCompanionRoster
{
    /// <summary>Virgil at T0 (ARC-RES-026): prototype 17102, script 1324, AI row 5 (−1000, 2001, 2001, 30), level 1, alignment 1,
    /// wait index 6 (no wait timer, ledger row Wait timer).</summary>
    public static AreaCompanionFacts Virgil { get; } = new(17102, 1324, new(-1000, 2001, 2001, 30), 1, 1, 6);
    public const int DiagnosticPcCharisma = 8, DiagnosticPcLevel = 1;

    public static AreaCompanionFacts? For(AreaSpeaker speaker)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        return speaker.Row.PrototypeNumber == Virgil.Prototype && speaker.ScriptId == Virgil.ScriptId ? Virgil : null;
    }

    /// <summary>The follow-check facts for <paramref name="npc"/>: the PC side is the session hero's (ARC-WORLD-067,
    /// <see cref="GameSessionOwner.QueryHeroPartySide"/>: Persuasion training, effective charisma, level, alignment, max followers)
    /// when <paramref name="hero"/> is given, else the diagnostic PC (charisma 8, level 1, no Persuasion) with
    /// <paramref name="pcAlignment"/>, the conversation host's alignment. <paramref name="level"/> is the NPC's level now when the
    /// session levelled it (ARC-WORLD-036, <see cref="AreaAdvancement.Level"/>), else its T0 level.</summary>
    public static PartyConsentFacts Facts(AreaCompanionFacts npc, int pcAlignment, int reaction, int? level = null, SessionPartySide? hero = null) => new(
        MindControlledByPc: false, PcPersuasion: hero?.Persuasion ?? PersuasionTraining.None, PcCharisma: hero?.Charisma ?? DiagnosticPcCharisma,
        PcAlignment: hero?.Alignment ?? pcAlignment, PcLevel: hero?.Level ?? DiagnosticPcLevel, OtherLeaderCharisma: null, Reaction: reaction,
        Thresholds: npc.Thresholds, NpcAlignment: npc.Alignment, NpcLevel: level ?? npc.Level,
        MaxFollowers: hero?.MaxFollowers ?? FollowConsentRules.MaxFollowers(DiagnosticPcCharisma, PersuasionTraining.None));

    /// <summary>ARC-WORLD-045: the wait timer facts of <paramref name="npc"/>: the leader's training and charisma are the session
    /// hero's when <paramref name="hero"/> is given (ARC-WORLD-067), else the diagnostic PC's.</summary>
    public static PartyWaitFacts WaitFacts(AreaCompanionFacts npc, SessionPartySide? hero = null) =>
        new(npc.WaitIndex, hero?.Persuasion ?? PersuasionTraining.None, hero?.Charisma ?? DiagnosticPcCharisma, false);

    /// <summary>The HUD's line for the PC side of the party rules: the hero's values, or the labelled diagnostic profile.</summary>
    public static string PartySideLabel(SessionPartySide? hero) => hero is { } pc
        ? $"PC side: hero (charisma {pc.Charisma}, level {pc.Level}, alignment {pc.Alignment}, persuasion {pc.Persuasion}, max followers {pc.MaxFollowers})"
        : $"PC side: diagnostic profile (charisma {DiagnosticPcCharisma}, level {DiagnosticPcLevel}, no persuasion; start with --hero for the hero)";
}
