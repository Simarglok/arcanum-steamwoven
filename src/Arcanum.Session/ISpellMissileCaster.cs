namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-139 (MIG13 S8): what a spell missile's host needs of the cast host that launched it (goal 28's release and end node, ledger SM rows): the session, the log the host's lines are read from, and the
/// missile's three ends on the cast's run. <see cref="SessionSpellCaster"/> is the one implementation; a test gives a fake.
/// </summary>
public interface ISpellMissileCaster
{
    /// <summary>The session the cast host casts in.</summary>
    GameSessionOwner Owner { get; }
    /// <summary>The host's log of cast events, oldest first.</summary>
    IReadOnlyList<SpellCastEvent> Log { get; }
    /// <summary>Whether a missile of the run flies (it was launched and has neither arrived nor been lost).</summary>
    bool MissileFlies(int run);
    /// <summary>The missile arrived: the target re-check and the run's BEGIN (null when the re-check failed or no missile of the run flies).</summary>
    SessionSpellRunResult? ArriveMissile(int run);
    /// <summary>The missile's goal ended without its release: the run is freed unpaid (null when no missile of the run flies).</summary>
    SessionSpellRunResult? LoseMissile(int run);
}
