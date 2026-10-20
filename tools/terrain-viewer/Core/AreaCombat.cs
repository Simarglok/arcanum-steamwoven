using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// An NPC the hero can click in combat mode (ARC-COMBAT-005): its entity id, its row, whether it is dead now (its map data or
/// the session), whether it follows the hero (the original wants left Alt to attack a follower) and its combat values (or why
/// its stored fields give none).
/// </summary>
public sealed record AreaCombatTarget(string Entity, AreaObjectRecord Row, bool Dead, bool Follower, SessionCritterCombatBuild Build);

/// <summary>
/// ARC-COMBAT-005: the AREA viewer's combat targets. Every NPC of a map with an inventory seed (ARC-UI-004; every NPC of the map
/// is a holder there) is a target; its combat values come from its effective fields (<see cref="CombatFieldReader.Read"/>) and
/// the fields of the items its seed lists (<see cref="SessionCritterCombat.From"/>), read once per entity. The same reader serves
/// the pinned exterior, the cave and every whole map. It holds no session state: life and party come from the session it is
/// given.
/// </summary>
public sealed class AreaCombatHost
{
    private readonly Dictionary<int, AreaInventoryMap> _maps;
    private readonly Dictionary<(int Map, string Entity), SessionCritterCombatBuild> _builds = [];
    // The NPC holders of one map as the session last showed them (rebuilt when the session or its revision changes).
    private (GameSessionOwner Session, long Revision, int Map, Dictionary<string, SessionHolderInventory> Holders)? _holders;

    public AreaCombatHost(AreaInventoryHost inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        _maps = inventory.Maps.ToDictionary(map => map.RuntimeMap);
    }

    public bool Covers(int runtimeMap) => _maps.ContainsKey(runtimeMap);

    /// <summary>The NPC drawn as <paramref name="key"/> on <paramref name="runtimeMap"/>, or null for anything else.</summary>
    public AreaCombatTarget? Target(GameSessionOwner session, int runtimeMap, AreaObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(runtimeMap, out var map) || !map.TryGetEntity(key, out var entity)
            || !Holders(session, runtimeMap).TryGetValue(entity, out var holder) || holder.Kind != SessionHolderKind.Npc)
            return null;
        return new(entity, map.Row(entity), holder.Dead, session.QueryCompanion(entity).LedByPc, Build(map, entity));
    }

    private Dictionary<string, SessionHolderInventory> Holders(GameSessionOwner session, int runtimeMap)
    {
        if (_holders is { } cached && ReferenceEquals(cached.Session, session) && cached.Revision == session.Version.Revision && cached.Map == runtimeMap)
            return cached.Holders;
        var holders = session.QueryMapInventory(new(runtimeMap))?.Holders.ToDictionary(holder => holder.Entity, StringComparer.Ordinal) ?? [];
        _holders = (session, session.Version.Revision, runtimeMap, holders);
        return holders;
    }

    /// <summary>ARC-WORLD-036: an NPC's stored fields on a map this host covers (a follower's loaded sheet), or null.</summary>
    public CombatFields? Fields(int runtimeMap, string entity) =>
        _maps.TryGetValue(runtimeMap, out var map) && map.TryGetRow(entity, out var row) ? CombatFieldReader.Read(row).Fields : null;

    /// <summary>ARC-UI-042: the body armour an NPC wears on a map this host covers: the first item its seed lists whose stored
    /// inventory location is the armour slot (1006), or null. A hit on the NPC sounds as a hit on it (audio study SN23).</summary>
    public AreaObjectRecord? WornArmor(int runtimeMap, string entity)
    {
        if (!_maps.TryGetValue(runtimeMap, out var map) || map.Seed.Holders.FirstOrDefault(h => h.Entity == entity) is not { } holder) return null;
        foreach (var item in holder.Items)
            if (map.TryGetRow(item, out var row) && CombatFieldReader.Read(row).Fields?.Item?.InventoryLocation == (int)Arcanum.Simulation.Equipment.EquipmentSlot.Armor)
                return row;
        return null;
    }

    /// <summary>ARC-WORLD-036: the map whose stored objects hold the NPC (its home map, wherever it stands now), or null when
    /// no covered map does.</summary>
    public int? HomeMap(string entity) =>
        _maps.Values.OrderBy(map => map.RuntimeMap).FirstOrDefault(map => map.TryGetRow(entity, out _))?.RuntimeMap;

    /// <summary>The NPC's combat values, read once per entity and map.</summary>
    public SessionCritterCombatBuild Build(int runtimeMap, string entity) => _maps.TryGetValue(runtimeMap, out var map)
        ? Build(map, entity) : new(SessionCritterCombatFailure.NotAnNpc, null);

    private SessionCritterCombatBuild Build(AreaInventoryMap map, string entity)
    {
        if (_builds.TryGetValue((map.RuntimeMap, entity), out var cached)) return cached;
        SessionCritterCombatBuild build;
        if (!map.TryGetRow(entity, out var row) || CombatFieldReader.Read(row).Fields is not { } fields) build = new(SessionCritterCombatFailure.NotAnNpc, null);
        else
        {
            var holder = map.Seed.Holders.First(h => h.Entity == entity);
            var carried = new List<CombatFields>();
            foreach (var item in holder.Items)
                if (map.TryGetRow(item, out var itemRow) && CombatFieldReader.Read(itemRow).Fields is { } itemFields) carried.Add(itemFields);
            build = SessionCritterCombat.From(entity, fields, carried);
        }
        _builds[(map.RuntimeMap, entity)] = build;
        return build;
    }

    /// <summary>The direction the hero faces when it attacks from <paramref name="from"/> toward <paramref name="to"/>: the
    /// neighbour direction of a step toward it, or null on the same cell.</summary>
    public static NeighborDirection? Facing(CellCoord from, CellCoord to)
    {
        var dx = Math.Sign((long)to.X - from.X); var dy = Math.Sign((long)to.Y - from.Y);
        return (dx, dy) switch
        {
            (-1, -1) => NeighborDirection.XMinusYMinus, (-1, 0) => NeighborDirection.XMinus, (-1, 1) => NeighborDirection.XMinusYPlus,
            (0, 1) => NeighborDirection.YPlus, (1, 1) => NeighborDirection.XPlusYPlus, (1, 0) => NeighborDirection.XPlus,
            (1, -1) => NeighborDirection.XPlusYMinus, (0, -1) => NeighborDirection.YMinus, _ => null,
        };
    }

    /// <summary>One line for the status bar about an attack's result (no original text): the outcome, the damage and the
    /// target's hit points after it, a knockout or death, and the experience it paid.</summary>
    public static string Describe(SessionAttackResult result, int? hitPoints, int? maxHitPoints)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Disposition == GameSessionDisposition.Rejected) return $"attack refused: {result.Failure}";
        if (result.Resolution is not { } resolution) return "attack: nothing happened";
        if (resolution.Refusal != AttackRefusal.None) return $"attack: {resolution.Refusal}";
        var where = resolution.Aimed ? $" (aimed at the {resolution.HitLocation.ToString().ToLowerInvariant()})" : string.Empty;
        var text = resolution.Outcome switch
        {
            AttackOutcome.Hit => "hit",
            AttackOutcome.CriticalHit => "critical hit",
            AttackOutcome.Miss => resolution.MissCause == AttackMissCause.Dodged ? "dodged" : "miss",
            AttackOutcome.CriticalMiss => "critical miss",
            _ => resolution.Outcome.ToString(),
        } + where;
        if (result.Life is { } life)
        {
            var self = result.Recipient == DamageRecipient.Attacker;
            text += self ? $": the hero takes {life.HitPointDamage} damage" : $": {life.HitPointDamage} damage";
            var fatigue = life.After.FatigueDamage - life.Before.FatigueDamage;
            if (fatigue > 0) text += $", {fatigue} fatigue";
            if (!self && hitPoints is { } hp && maxHitPoints is { } max) text += $", HP {Math.Max(hp, 0)}/{max}";
            if (life.Events.HasFlag(LifeEvents.Died)) text += self ? ", the hero dies" : ", killed";
            else if (life.Events.HasFlag(LifeEvents.LostConsciousness) || life.Events.HasFlag(LifeEvents.KnockedOut)) text += ", knocked out";
            else if (life.Events.HasFlag(LifeEvents.KnockedDown)) text += ", knocked down";
            if (life.Events.HasFlag(LifeEvents.Stunned)) text += ", stunned";
        }
        else if (resolution.Damage is { Prevented: not DamagePrevention.None } prevented) text += $": no damage ({prevented.Prevented})";
        var experience = (result.HitAward?.Award ?? 0) + (result.KillAward?.Award ?? 0);
        if (experience != 0) text += $", +{experience} XP";
        if (result.StepFailure != GameSessionFailure.None) text += $" (step refused: {result.StepFailure})";
        return text;
    }
}

/// <summary>
/// ARC-COMBAT-005: one attack animation of the hero in real time (<see cref="RealTimeCombatRules"/> playback on the viewer's
/// clock): it starts on frame 0 and every due update is returned in order; the host runs the hit when an update is
/// <see cref="AttackUpdateKind.Hit"/> and stops on <see cref="AttackUpdateKind.Ended"/>.
/// </summary>
public sealed class AreaAttackPlayer
{
    public AreaAttackPlayer(AttackClip clip, long startMs)
    {
        ArgumentNullException.ThrowIfNull(clip);
        Clip = clip; Playback = RealTimeCombatRules.Begin(clip); NextUpdateMs = startMs + RealTimeCombatRules.FirstUpdateDelayMs;
    }

    public AttackClip Clip { get; }
    public AttackPlayback Playback { get; private set; }
    public long NextUpdateMs { get; private set; }
    public bool Playing => Playback.Playing;

    /// <summary>Every update due by <paramref name="nowMs"/>, at most <paramref name="limit"/> (a long stall plays at most that
    /// many frames at once), each with the time it was due. <paramref name="canAct"/> is asked before each.</summary>
    public IReadOnlyList<(long AtMs, AttackUpdate Update)> Advance(long nowMs, Func<bool> canAct, int limit = 64)
    {
        ArgumentNullException.ThrowIfNull(canAct);
        var updates = new List<(long, AttackUpdate)>();
        while (Playback.Playing && NextUpdateMs <= nowMs && updates.Count < limit)
        {
            var at = NextUpdateMs;
            var update = RealTimeCombatRules.Update(Clip, Playback, canAct(), false, false);
            Playback = update.Playback;
            if (update.NextUpdateMs is { } delay) NextUpdateMs = at + delay;
            updates.Add((at, update));
        }
        return updates;
    }

    /// <summary>The hit found the target gone or out of reach: the animation ends without damage.</summary>
    public void Abort() => Playback = RealTimeCombatRules.Abort(Playback);
}
