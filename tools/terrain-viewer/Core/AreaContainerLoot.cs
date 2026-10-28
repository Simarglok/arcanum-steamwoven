using Arcanum.Data.Combat;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Locks;

namespace Arcanum.TerrainViewer.Core;

/// <summary>What a container's loot open did (ARC-WORLD-101): the window opens, or why it does not.</summary>
public enum AreaContainerOpenOutcome
{
    /// <summary>Admitted: the window opens on the container.</summary>
    Opened,
    /// <summary>The open attempt (0x4AED80) found it locked and the party holds no key (the session holds no keys yet).</summary>
    Locked,
    Jammed,
    MagicallyHeld,
    /// <summary>The container has an unlock script (SAP 3), which the original's attempt runs and the viewer does not (Not ready).</summary>
    UnlockScriptUnsupported,
    /// <summary>The container's use script (SAP 1), run after the attempt, ended with skip default: the original refuses the open with the
    /// locked sound.</summary>
    UseScriptStopped,
    /// <summary>The container's use script is a native trap script (30000..30007, ScrStop.TrapScript), which the original's trap code
    /// handles on the use (not read, not modelled): the viewer does not open a trapped container.</summary>
    TrapScriptUnsupported,
    /// <summary>The container has a use script that the viewer could not run (no script host, or the dispatcher refused the run, which
    /// then applied nothing): the open is refused (Not ready).</summary>
    UseScriptUnsupported,
    /// <summary>The object is no container of the map's seed, it broke, or its fields do not read.</summary>
    Unavailable,
    /// <summary>The session refused the generation of a container with flag 0x200 (<see cref="GameSessionOwner.GenerateContainerOnce"/>:
    /// the restock host not ready yet, a full budget), so the window does not open on a list the generation would replace.</summary>
    GenerationRefused,
}

/// <summary>A container's loot open: the outcome, the use script's run (null when none ran), the session's generation (null unless the container has flag 0x200 at T0 and the session
/// had not generated it), whether the container was closed and
/// opened now (its open sound plays) and a detail (identifiers and statuses only).</summary>
public sealed record AreaContainerOpen(AreaContainerOpenOutcome Outcome, SessionGenerateOnceResult? Generation, bool Opening, string Detail)
{
    public AreaScriptedUse? UseScript { get; init; }
    /// <summary>The refusals the original answers with the container's locked sound (0x572370): the attempt's, with the inventory window's
    /// message (<c>mes\inven_ui.mes</c>, the attempt's code), and the use script's skip default.</summary>
    public bool LockedSound => Outcome is AreaContainerOpenOutcome.Locked or AreaContainerOpenOutcome.Jammed or AreaContainerOpenOutcome.MagicallyHeld
        or AreaContainerOpenOutcome.UseScriptStopped;
}

/// <summary>
/// ARC-WORLD-101: the AREA viewer's loot window on a container (the inventory open 0x572240 in loot mode 2, ledger LW1–LW4 of
/// docs/world/barter-runtime.md): the window's admission (0x572370: the player's open attempt, then the container's use script), the
/// partner setup (0x572640: a closed container opens one ART frame with its open sound, then a container with flag 0x200 is generated
/// once, RK12) and the close (0x573440: a container on frame 1 without flag 0x100 closes with its close sound). The restock schedule
/// after the setup (RK2) and the window are the caller's. It holds no state: every answer reads the session.
/// </summary>
public static class AreaContainerLoot
{
    /// <summary>The container flag that keeps an open container open when its window closes (field 0x38, flag 0x100; its name was not
    /// read).</summary>
    public const int StaysOpenFlag = 0x100;

    /// <summary>
    /// The admission and partner setup of a loot open on <paramref name="container"/> of <paramref name="map"/>, in the original's order
    /// but for the generation, which runs before the frame write so that a refusal changes nothing (the two do not read each other). The
    /// open attempt is the session's at its hour with the container's T0 lock baseline and its recorded lock state; then the container's
    /// use script runs through <paramref name="useScript"/> (the viewer's use-script host, with the PC as the triggerer): skip default
    /// refuses the open, and so do a native trap script (its trap is not modelled), a run the host refuses (it applies nothing) and no
    /// host; then, for a container with flag 0x200 the session has not generated, <see cref="GameSessionOwner.GenerateContainerOnce"/>
    /// through <paramref name="world"/>; then a container on
    /// frame 0 steps to its next frame (the ART's <paramref name="frameCount"/>, wrapping to 0 past the last: an ART of one frame keeps
    /// frame 0) and <see cref="AreaContainerOpen.Opening"/> asks for the open sound.
    /// </summary>
    public static AreaContainerOpen Open(GameSessionOwner session, AreaInventoryMap map, string container, int frameCount, ISessionRestockWorld world,
        Func<Arcanum.Data.Area.AreaObjectRecord, AreaScriptedUse>? useScript = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(world);
        if (Facts(session, map, container) is not { } facts) return new(AreaContainerOpenOutcome.Unavailable, null, false, "not a standing container of the map's seed");
        var (openable, baseline, row, holder) = facts;
        if (AreaStepInspector.HasUnlockScript(row)) return new(AreaContainerOpenOutcome.UnlockScriptUnsupported, null, false, "unlock script not run");
        var attempt = session.AttemptOpen(openable, baseline, scriptRefused: false);
        switch (attempt)
        {
            case OpenAttempt.Locked: return new(AreaContainerOpenOutcome.Locked, null, false, "attempt=Locked");
            case OpenAttempt.Jammed: return new(AreaContainerOpenOutcome.Jammed, null, false, "attempt=Jammed");
            case OpenAttempt.MagicallyHeld: return new(AreaContainerOpenOutcome.MagicallyHeld, null, false, "attempt=MagicallyHeld");
            case not OpenAttempt.Opens: return new(AreaContainerOpenOutcome.Unavailable, null, false, $"attempt={attempt}");
        }
        AreaScriptedUse? ran = null;
        if (holder.ScriptPoints.Contains(SessionScriptPoint.Use))
        {
            if (useScript is null) return new(AreaContainerOpenOutcome.UseScriptUnsupported, null, false, "use script: no script host");
            ran = useScript(row);
            if (ran.Outcome != AreaScriptedUseOutcome.NoTeleport)
                return new(AreaContainerOpenOutcome.UseScriptUnsupported, null, false, $"use script {ran.Outcome}: {ran.Detail}") { UseScript = ran };
            if (ran.Run?.Stop == Arcanum.Scripting.Scr.ScrStop.TrapScript)
                return new(AreaContainerOpenOutcome.TrapScriptUnsupported, null, false, "use script: a native trap script") { UseScript = ran };
            if (ran.Run?.Outcome == Arcanum.Scripting.Scr.ScrOutcome.SkipDefault)
                return new(AreaContainerOpenOutcome.UseScriptStopped, null, false, "use script: skip default") { UseScript = ran };
        }
        // Only a container with flag 0x200 (its T0 field 0x38) the session has not generated asks the session: any other opens while the
        // restock host still loads.
        SessionGenerateOnceResult? generation = null;
        if ((map.RestockSeed(container).ContainerFlags & Arcanum.Simulation.Items.RestockRules.GeneratedOnceFlag) != 0 &&
            !session.QueryContainerGenerated(openable.Map, container))
        {
            generation = session.GenerateContainerOnce(session.Version, container, world);
            if (generation.Disposition == GameSessionDisposition.Rejected)
                return new(AreaContainerOpenOutcome.GenerationRefused, generation, false, $"generation={generation.Failure}") { UseScript = ran };
        }
        var frame = session.QueryOpenable(openable, baseline).Frame;
        var opening = frame == 0;
        var next = opening && frameCount > 1 ? 1 : frame;
        var detail = $"generation={generation?.Disposition.ToString() ?? "none"}/{generation?.Regeneration?.Outcome.ToString() ?? "none"} frame={frame}";
        if (next != frame)
        {
            var written = session.SetOpenableFrame(session.Version, openable, baseline, next);
            detail += $"->{next} write={written.Disposition}/{written.Failure}";
        }
        return new(AreaContainerOpenOutcome.Opened, generation, opening, detail + (ran is null ? "" : " use-script=run-default")) { UseScript = ran };
    }

    /// <summary>
    /// The close of a container's loot window (0x573440 in mode 2): a container on frame 1 whose recorded flags lack
    /// <see cref="StaysOpenFlag"/> steps back to frame 0 (one revision); true when it did (its close sound plays). Anything else changes
    /// nothing.
    /// </summary>
    public static bool Close(GameSessionOwner session, AreaInventoryMap map, string container)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(container);
        if (Facts(session, map, container) is not { } facts) return false;
        var state = session.QueryOpenable(facts.Openable, facts.Baseline);
        if (state.Frame != 1 || (state.Flags & StaysOpenFlag) != 0) return false;
        return session.SetOpenableFrame(session.Version, facts.Openable, facts.Baseline, 0).Disposition == GameSessionDisposition.Changed;
    }

    /// <summary>Whether a left press on the object opens a container's loot window: a container holder of the map's seed that has not
    /// broken (a broken one spilled its items, ARC-COMBAT-008).</summary>
    public static bool Lootable(GameSessionOwner session, AreaInventoryMap map, string entity) =>
        map.Seed.Holders.Any(holder => holder.Entity == entity && holder.Kind == SessionHolderKind.Container) &&
        session.QueryObjectCondition(new(map.RuntimeMap), entity, Arcanum.Simulation.Combat.DamageableObjectKind.Container) is not
            (SessionObjectCondition.Destroyed or SessionObjectCondition.Off);

    private static (SessionOpenable Openable, SessionOpenableBaseline Baseline, Arcanum.Data.Area.AreaObjectRecord Row, SessionSeedHolder Holder)? Facts(
        GameSessionOwner session, AreaInventoryMap map, string container)
    {
        if (!Lootable(session, map, container) || !map.TryGetRow(container, out var row) || CombatFieldReader.ReadBreakable(row).Fields is not { } fields) return null;
        var holder = map.Seed.Holders.First(h => h.Entity == container);
        return (new(new(map.RuntimeMap), container), AreaObjectTargets.ContainerBaseline(row, fields), row, holder);
    }
}
