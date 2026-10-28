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
public sealed class AreaCombatHost : IAreaEventObjects
{
    private readonly Dictionary<int, AreaInventoryMap> _maps;
    private readonly Dictionary<(int Map, string Entity), SessionCritterCombatBuild> _builds = [];
    // The NPC holders of one map as the session last showed them, and the NPC holding each of their items (ARC-UI-031), rebuilt when
    // the session or its revision changes.
    private (GameSessionOwner Session, long Revision, int Map, Dictionary<string, SessionHolderInventory> Holders, Dictionary<string, string> NpcItems)? _holders;

    public AreaCombatHost(AreaInventoryHost inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        _maps = inventory.Maps.ToDictionary(map => map.RuntimeMap);
    }

    public bool Covers(int runtimeMap) => _maps.ContainsKey(runtimeMap);

    /// <summary>ARC-MAGIC-014: the session entity id of a map object (its inventory seed's), or null.</summary>
    public string? Entity(int runtimeMap, AreaObjectKey key) =>
        _maps.TryGetValue(runtimeMap, out var map) && map.TryGetEntity(key, out var entity) ? entity : null;

    /// <summary>ARC-WORLD-039: whether an object of the map's T0 data has this session entity id (a deferred call's role).</summary>
    public bool HasObject(int runtimeMap, string entity) => _maps.TryGetValue(runtimeMap, out var map) && map.TryGetRow(entity, out _);

    /// <summary>ARC-WORLD-039: the NPC's life facts from its combat fields on that map, or null.</summary>
    public LifeFacts? Facts(int runtimeMap, string entity) => Build(runtimeMap, entity).Combat?.Facts;

    /// <summary>ARC-WORLD-103: the NPC's combat values from its combat fields on that map, or null.</summary>
    public SessionCritterCombat? Critter(int runtimeMap, string entity) => Build(runtimeMap, entity).Combat;

    /// <summary>The NPC drawn as <paramref name="key"/> on <paramref name="runtimeMap"/>, or null for anything else.</summary>
    public AreaCombatTarget? Target(GameSessionOwner session, int runtimeMap, AreaObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(runtimeMap, out var map) || !map.TryGetEntity(key, out var entity)
            || !Holders(session, runtimeMap).TryGetValue(entity, out var holder) || holder.Kind != SessionHolderKind.Npc)
            return null;
        return new(entity, map.Row(entity), holder.Dead, session.QueryCompanion(entity).LedByPc, Build(map, entity));
    }

    private Dictionary<string, SessionHolderInventory> Holders(GameSessionOwner session, int runtimeMap) => HolderIndex(session, runtimeMap).Holders;

    private (Dictionary<string, SessionHolderInventory> Holders, Dictionary<string, string> NpcItems) HolderIndex(GameSessionOwner session, int runtimeMap)
    {
        if (_holders is { } cached && ReferenceEquals(cached.Session, session) && cached.Revision == session.Version.Revision && cached.Map == runtimeMap)
            return (cached.Holders, cached.NpcItems);
        var holders = session.QueryMapInventory(new(runtimeMap))?.Holders.ToDictionary(holder => holder.Entity, StringComparer.Ordinal) ?? [];
        var npcItems = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var holder in holders.Values.Where(holder => holder.Kind == SessionHolderKind.Npc))
            foreach (var item in holder.Items) npcItems.TryAdd(item.Entity, holder.Entity);
        _holders = (session, session.Version.Revision, runtimeMap, holders, npcItems);
        return (holders, npcItems);
    }

    /// <summary>
    /// ARC-UI-031: the spell facts of an item an NPC of a map this host covers holds now, listed or worn, as the session shows the
    /// map's holders (a dead NPC's too: its dying fires its worn items, IC5): the item's row through
    /// <see cref="SessionItemSpells.FromCombatFields"/> with the NPC as its holder (the parent field 0x58). The cast host's item lookup
    /// asks it for anything that is not the hero's; null for an item no NPC of that map holds (a container's, the hero's, a visiting
    /// companion's from its own map), a destroyed one, or one whose row does not read as an item.
    /// </summary>
    public SessionItemSpells? NpcItemSpells(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        if (!_maps.TryGetValue(runtimeMap, out var map) || !HolderIndex(session, runtimeMap).NpcItems.TryGetValue(entity, out var holder)
            || session.IsItemDestroyed(entity) || !map.TryGetRow(entity, out var row) || CombatFieldReader.Read(row).Fields is not { } fields)
            return null;
        return SessionItemSpells.FromCombatFields(entity, holder, fields);
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

    /// <summary>
    /// ARC-COMBAT-008: the items in the NPC's wear slots with their durability values, for the attack's wear: the items of its list as
    /// the session shows it now whose stored inventory location is a wear slot (1000..1008) and that are not broken (the original takes
    /// an item off at its first break; for an NPC that is the host's, so a broken item is no longer worn), each through
    /// <see cref="SessionWearItem.FromCombatFields"/>. Null when the map is not covered, a worn item does not read or two share a slot
    /// (the attack then wears none of the NPC's items).
    /// </summary>
    public IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? WornItems(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(runtimeMap, out var map) || !Holders(session, runtimeMap).TryGetValue(entity, out var holder)) return null;
        var worn = new Dictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>();
        foreach (var item in holder.Items)
        {
            var slot = (Arcanum.Simulation.Equipment.EquipmentSlot)item.Facts.InventoryLocation;
            if (!Enum.IsDefined(slot) || !map.TryGetRow(item.Entity, out var row)) continue;
            if (CombatFieldReader.Read(row).Fields is not { Item: not null } fields || SessionWearItem.FromCombatFields(item.Entity, fields) is not { } wear)
                return null;
            if (session.QueryItemCondition(item.Entity, wear.Baseline) is not { Broken: false }) continue;
            if (!worn.TryAdd(slot, wear)) return null;
        }
        return worn;
    }

    /// <summary>ARC-COMBAT-008: the hero's worn items with their durability values (each kit item's from
    /// <paramref name="kit"/>, by kit index), or null when one has none (the attack then wears none of the hero's items).</summary>
    public static IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? HeroWorn(GameSessionOwner session,
        IReadOnlyList<SessionWearItem?>? kit)
    {
        ArgumentNullException.ThrowIfNull(session);
        var worn = new Dictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>();
        foreach (var item in session.QueryEquipment().Worn)
        {
            if (!item.Entity.StartsWith("kit:", StringComparison.Ordinal) ||
                !int.TryParse(item.Entity.AsSpan(4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
                kit is null || number < 1 || number > kit.Count || kit[number - 1] is not { } wear || wear.Entity != item.Entity) return null;
            worn[item.Slot] = wear;
        }
        return worn;
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

    /// <summary>One log fragment for the critical effects of an attack (ARC-COMBAT-009): the table's effects and each item step.</summary>
    public static string Criticals(AttackResolution? resolution, IReadOnlyList<SessionCriticalStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var effects = resolution?.Damage is { } damage ? $"{damage.HitEffects}|{damage.MissEffects}" : "-";
        return effects + string.Concat(steps.Select(step => $",{step.Kind}:{step.Entity}:{step.Outcome}"));
    }

    /// <summary>One line for the status bar about an attack's result (no original text): the outcome, the damage and the
    /// target's hit points after it, a knockout or death, injuries and drops (ARC-COMBAT-009), and the experience it paid.</summary>
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
            if (life.Events.HasFlag(LifeEvents.Blinded)) text += ", blinded";
            if ((life.Events & (LifeEvents.CrippledArm | LifeEvents.CrippledBothArms | LifeEvents.CrippledLegs)) != 0) text += ", crippled";
            if (life.Events.HasFlag(LifeEvents.Scarred)) text += ", scarred";
        }
        else if (resolution.Damage is { Prevented: not DamagePrevention.None } prevented) text += $": no damage ({prevented.Prevented})";
        foreach (var step in result.Criticals)
            text += step.Outcome switch
            {
                SessionCriticalStepOutcome.Dropped => $", drops its {step.Slot?.ToString().ToLowerInvariant()}",
                SessionCriticalStepOutcome.HeroCellUnfit => $", would drop the {step.Slot?.ToString().ToLowerInvariant()} (no cell for it)",
                _ => string.Empty,
            };
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
