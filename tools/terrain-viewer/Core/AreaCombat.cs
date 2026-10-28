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

/// <summary>ARC-WORLD-106: an item an NPC wears now (<see cref="AreaCombatHost.Worn"/>): its slot, entity, combat fields and durability
/// values.</summary>
public readonly record struct AreaNpcWornItem(Arcanum.Simulation.Equipment.EquipmentSlot Slot, string Entity, CombatFields Fields, SessionWearItem Wear);

/// <summary>
/// ARC-COMBAT-005: the AREA viewer's combat targets. Every NPC of a map with an inventory seed (ARC-UI-004; every NPC of the map
/// is a holder there) is a target; its combat values come from its effective fields (<see cref="CombatFieldReader.Read"/>) and
/// the fields of the items it wears. Since ARC-WORLD-106 the worn items are the session's (<see cref="Worn"/>: the NPC's list as the
/// session shows it, with a regeneration's best-items record, the items given to or made for it and without the taken, dropped,
/// destroyed or broken ones), rebuilt when that set changes; <see cref="Critter(int, string)"/> keeps the map data's worn items for the
/// values that do not depend on them (wearer and best-items chooser). The same reader serves the pinned exterior, the cave and every
/// whole map. It holds no session state: life, party and wear come from the session it is given (the event world's questions from
/// <c>session</c>, the viewer's shown session).
/// </summary>
public sealed class AreaCombatHost : IAreaEventObjects
{
    private readonly Dictionary<int, AreaInventoryMap> _maps;
    private readonly Func<int, CombatFields?>? _prototypes;
    private readonly Func<GameSessionOwner?>? _session;
    private readonly Dictionary<(int Map, string Entity), SessionCritterCombatBuild> _builds = [];
    // ARC-WORLD-106: the NPC's build over the session's worn set it was made for (slot:item:prototype, in slot order), for one session (a
    // loaded save may reuse a made item's number for another item); and the worn sets of one session revision, kept only when every
    // worn item had fields (a prototype may load later without a revision).
    private readonly Dictionary<(int Map, string Entity), (string Worn, SessionCritterCombatBuild Build)> _wornBuilds = [];
    private GameSessionOwner? _buildSession;
    private readonly Dictionary<(int Map, string Entity), IReadOnlyList<AreaNpcWornItem>?> _wornNow = [];
    private (GameSessionOwner Session, long Revision)? _wornStamp;
    // The combat fields of an item's object row, by entity (null: the row does not read), read once.
    private readonly Dictionary<string, CombatFields?> _rowFields = new(StringComparer.Ordinal);
    // The NPC holders of one map as the session last showed them, and the NPC holding each of their items (ARC-UI-031), rebuilt when
    // the session or its revision changes.
    private (GameSessionOwner Session, long Revision, int Map, Dictionary<string, SessionHolderInventory> Holders, Dictionary<string, string> NpcItems)? _holders;

    /// <param name="inventory">The maps' seeds and object rows.</param>
    /// <param name="prototypes">ARC-WORLD-106: an item prototype's stored fields (the facts of an item the session made or a regeneration
    /// created for an NPC, which has no object row), or null while they are not loaded; null: such an item has no facts.</param>
    /// <param name="session">ARC-WORLD-106: the session the event world's questions (<see cref="Facts(int, string)"/>) read the NPC's wear
    /// from; null or no session: its map data's.</param>
    public AreaCombatHost(AreaInventoryHost inventory, Func<int, CombatFields?>? prototypes = null, Func<GameSessionOwner?>? session = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        _maps = inventory.Maps.ToDictionary(map => map.RuntimeMap);
        _prototypes = prototypes;
        _session = session;
    }

    public bool Covers(int runtimeMap) => _maps.ContainsKey(runtimeMap);

    /// <summary>ARC-MAGIC-014: the session entity id of a map object (its inventory seed's), or null.</summary>
    public string? Entity(int runtimeMap, AreaObjectKey key) =>
        _maps.TryGetValue(runtimeMap, out var map) && map.TryGetEntity(key, out var entity) ? entity : null;

    /// <summary>ARC-WORLD-039: whether an object of the map's T0 data has this session entity id (a deferred call's role).</summary>
    public bool HasObject(int runtimeMap, string entity) => _maps.TryGetValue(runtimeMap, out var map) && map.TryGetRow(entity, out _);

    /// <summary>ARC-WORLD-039: the NPC's life facts from its combat fields on that map, or null. ARC-WORLD-106: with the items the
    /// session's NPC wears (its normal resistance counts its armour) when the host was given the session; the map data's when that build
    /// fails (only the normal resistance depends on the worn items, and a null answer would drop the NPC's life timers).</summary>
    public LifeFacts? Facts(int runtimeMap, string entity) =>
        (_session?.Invoke() is { } session ? Build(session, runtimeMap, entity).Combat?.Facts : null) ?? Build(runtimeMap, entity).Combat?.Facts;

    /// <summary>ARC-WORLD-103: the NPC's combat values from its combat fields on that map, or null. Its worn items are the map data's: the
    /// regeneration asks it for the wearer and the best-items chooser, which do not depend on them, while the session's lists change.</summary>
    public SessionCritterCombat? Critter(int runtimeMap, string entity) => Build(runtimeMap, entity).Combat;

    /// <summary>The NPC drawn as <paramref name="key"/> on <paramref name="runtimeMap"/>, or null for anything else.</summary>
    public AreaCombatTarget? Target(GameSessionOwner session, int runtimeMap, AreaObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(runtimeMap, out var map) || !map.TryGetEntity(key, out var entity)
            || !Holders(session, runtimeMap).TryGetValue(entity, out var holder) || holder.Kind != SessionHolderKind.Npc)
            return null;
        return new(entity, map.Row(entity), holder.Dead, session.QueryCompanion(entity).LedByPc, Build(session, map, entity));
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
    /// map's holders (a dead NPC's too: its dying fires its worn items, IC5): the item's fields (<see cref="ItemFields"/>: its object row,
    /// or since ARC-WORLD-106 a made item's prototype) through <see cref="SessionItemSpells.FromCombatFields"/> with the NPC as its holder
    /// (the parent field 0x58). The cast host's item lookup asks it for anything that is not the hero's; null for an item no NPC of that
    /// map holds (a container's, the hero's, a visiting companion's from its own map), a destroyed one, or one whose fields do not read as
    /// an item.
    /// </summary>
    public SessionItemSpells? NpcItemSpells(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        if (!_maps.TryGetValue(runtimeMap, out var map)) return null;
        var (holders, npcItems) = HolderIndex(session, runtimeMap);
        if (!npcItems.TryGetValue(entity, out var holder) || session.IsItemDestroyed(entity)
            || holders[holder].Items.FirstOrDefault(item => item.Entity == entity) is not { Entity: not null } listed || ItemFields(map, listed) is not { } fields)
            return null;
        return SessionItemSpells.FromCombatFields(entity, holder, fields);
    }

    /// <summary>
    /// ARC-WORLD-108: the items an NPC of a map this host covers holds now, listed or worn, in its list's order as the session shows the
    /// map's holders (the AI's inventory, fields 0xED/0xEE), each with its fields (<see cref="ItemFields"/>); a destroyed item and one
    /// without fields (NOT READY: an item without a row before the prototypes load) are left out. Empty for an NPC the session does
    /// not list.
    /// </summary>
    public IReadOnlyList<(string Entity, CombatFields Fields)> NpcItems(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        if (!_maps.TryGetValue(runtimeMap, out var map) || !Holders(session, runtimeMap).TryGetValue(entity, out var holder) || holder.Kind != SessionHolderKind.Npc)
            return [];
        var items = new List<(string, CombatFields)>();
        foreach (var item in holder.Items)
            if (!session.IsItemDestroyed(item.Entity) && ItemFields(map, item) is { Item: not null } fields) items.Add((item.Entity, fields));
        return items;
    }

    /// <summary>ARC-WORLD-108: the fields of an item an NPC of a map this host covers holds now (<see cref="ItemFields"/>), or null for
    /// an item no NPC of that map holds, a destroyed one, or one without fields.</summary>
    public CombatFields? NpcItemFields(GameSessionOwner session, int runtimeMap, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        if (!_maps.TryGetValue(runtimeMap, out var map)) return null;
        var (holders, npcItems) = HolderIndex(session, runtimeMap);
        return npcItems.TryGetValue(item, out var holder) && !session.IsItemDestroyed(item)
            && holders[holder].Items.FirstOrDefault(listed => listed.Entity == item) is { Entity: not null } found
            && ItemFields(map, found) is { Item: not null } fields ? fields : null;
    }

    /// <summary>ARC-WORLD-108: the NPC of a map this host covers that holds the item now (0x4612A0 for an NPC's item), or null.</summary>
    public string? NpcHolder(GameSessionOwner session, int runtimeMap, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        return _maps.ContainsKey(runtimeMap) && HolderIndex(session, runtimeMap).NpcItems.TryGetValue(item, out var holder) && !session.IsItemDestroyed(item)
            ? holder : null;
    }

    /// <summary>ARC-WORLD-036: an NPC's stored fields on a map this host covers (a follower's loaded sheet), or null.</summary>
    public CombatFields? Fields(int runtimeMap, string entity) =>
        _maps.TryGetValue(runtimeMap, out var map) && map.TryGetRow(entity, out var row) ? CombatFieldReader.Read(row).Fields : null;

    /// <summary>ARC-UI-042: the body armour an NPC wears on a map this host covers: since ARC-WORLD-106 the session's (<see cref="Worn"/>,
    /// the armour slot 1006) by its object row, or null (none, or a made armour without a row). A hit on the NPC sounds as a hit on it
    /// (audio study SN23).</summary>
    public AreaObjectRecord? WornArmor(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(runtimeMap, out var map) || Worn(session, runtimeMap, entity) is not { } worn) return null;
        var armor = worn.FirstOrDefault(item => item.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Armor);
        if (armor.Entity is null) return null;
        if (map.TryGetRow(armor.Entity, out var row)) return row;
        return _maps.Values.Select(other => other.TryGetRow(armor.Entity, out var found) ? found : null).FirstOrDefault(found => found is not null);
    }

    /// <summary>
    /// ARC-COMBAT-008: the items in the NPC's wear slots with their durability values, for the attack's wear: <see cref="Worn"/>, each
    /// through <see cref="SessionWearItem.FromCombatFields"/>. Null when the map is not covered, the session does not list the NPC, a worn
    /// item does not read or two share a slot (the attack then wears none of the NPC's items).
    /// </summary>
    public IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? WornItems(GameSessionOwner session, int runtimeMap, string entity) =>
        Worn(session, runtimeMap, entity)?.ToDictionary(item => item.Slot, item => item.Wear);

    /// <summary>
    /// ARC-WORLD-106: the items the NPC wears now, in slot order: the items of its list as the session shows it (a regeneration's
    /// best-items record over its map data's inventory locations, ARC-WORLD-103; the items given to or made for it; never the taken, dropped
    /// or destroyed ones) whose location is a wear slot (1000..1008) and that are not broken (the original takes an item off at its first
    /// break; for an NPC that is the host's, so a broken item is no longer worn). An item's fields are <see cref="ItemFields"/>; one
    /// without any (an item without a row before the prototypes load: NOT READY) is skipped. Null when the map is not covered, the
    /// session does not list the NPC, a worn item's fields give no durability values, or two items share a slot. Kept per session
    /// revision.
    /// </summary>
    public IReadOnlyList<AreaNpcWornItem>? Worn(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_wornStamp is not { } stamp || !ReferenceEquals(stamp.Session, session) || stamp.Revision != session.Version.Revision)
        {
            _wornNow.Clear();
            _wornStamp = (session, session.Version.Revision);
        }
        if (_wornNow.TryGetValue((runtimeMap, entity), out var known)) return known;
        if (!_maps.TryGetValue(runtimeMap, out var map) || !Holders(session, runtimeMap).TryGetValue(entity, out var holder)) return null;
        var worn = new SortedDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, AreaNpcWornItem>();
        var complete = true;
        IReadOnlyList<AreaNpcWornItem>? Keep(IReadOnlyList<AreaNpcWornItem>? found)
        {
            if (complete) _wornNow[(runtimeMap, entity)] = found;
            return found;
        }
        foreach (var item in holder.Items)
        {
            var slot = (Arcanum.Simulation.Equipment.EquipmentSlot)item.Facts.InventoryLocation;
            if (!Enum.IsDefined(slot) || session.IsItemDestroyed(item.Entity)) continue;
            if (!ItemSource(map, item, out var fields)) { complete = false; continue; }
            if (fields?.Item is null || SessionWearItem.FromCombatFields(item.Entity, fields) is not { } wear) return Keep(null);
            if (session.QueryItemCondition(item.Entity, wear.Baseline) is not { Broken: false }) continue;
            if (!worn.TryAdd(slot, new(slot, item.Entity, fields, wear))) return Keep(null);
        }
        return Keep(Array.AsReadOnly(worn.Values.ToArray()));
    }

    /// <summary>
    /// ARC-WORLD-106: an item's combat fields: its object row's on the map, else on another covered map (a taken item the hero gave
    /// away), else (an item the session made, an item a regeneration created, a hero kit item given away) its prototype's stored fields
    /// from <c>prototypes</c> (the item's kind names the prototype), as the regeneration's wielder reads such an item; null without any.
    /// </summary>
    public CombatFields? ItemFields(AreaInventoryMap map, SessionInventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(map);
        return ItemSource(map, item, out var fields) ? fields : null;
    }

    // Whether the item has a source of fields (a row, which may not read, or a loaded prototype) and its fields.
    private bool ItemSource(AreaInventoryMap map, SessionInventoryItem item, out CombatFields? fields)
    {
        if (RowFields(map, item.Entity, out fields)) return true;
        foreach (var other in _maps.Values)
            if (!ReferenceEquals(other, map) && RowFields(other, item.Entity, out fields)) return true;
        fields = _prototypes?.Invoke(item.Facts.Kind.Prototype);
        return fields is not null;
    }

    private bool RowFields(AreaInventoryMap map, string entity, out CombatFields? fields)
    {
        fields = null;
        if (!map.TryGetRow(entity, out var row)) return false;
        if (!_rowFields.TryGetValue(entity, out fields)) _rowFields[entity] = fields = CombatFieldReader.Read(row).Fields;
        return true;
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

    /// <summary>The NPC's combat values with its map data's worn items (the items its seed lists in a wear slot by their stored
    /// location), read once per entity and map: for the values that do not depend on what it wears (<see cref="Critter(int, string)"/>).</summary>
    public SessionCritterCombatBuild Build(int runtimeMap, string entity) => _maps.TryGetValue(runtimeMap, out var map)
        ? Build(map, entity) : new(SessionCritterCombatFailure.NotAnNpc, null);

    /// <summary>
    /// ARC-WORLD-106: the NPC's combat values with the items the session's NPC wears now (<see cref="Worn"/>, each in the slot the session
    /// names: <see cref="SessionCritterCombat.FromWorn"/>), rebuilt when that set changes: what it attacks with, defends with (armour class,
    /// resistances, light) and its life facts. An NPC the session does not list (no inventory for its map yet) keeps its map data's worn
    /// items; a worn item without durability values or two in one slot is <see cref="SessionCritterCombatFailure.InvalidWornItem"/>.
    /// </summary>
    public SessionCritterCombatBuild Build(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _maps.TryGetValue(runtimeMap, out var map) ? Build(session, map, entity) : new(SessionCritterCombatFailure.NotAnNpc, null);
    }

    private SessionCritterCombatBuild Build(GameSessionOwner session, AreaInventoryMap map, string entity)
    {
        if (!map.TryGetRow(entity, out _) || !Holders(session, map.RuntimeMap).ContainsKey(entity)) return Build(map, entity);
        if (!RowFields(map, entity, out var fields) || fields is null) return new(SessionCritterCombatFailure.NotAnNpc, null);
        if (Worn(session, map.RuntimeMap, entity) is not { } worn) return new(SessionCritterCombatFailure.InvalidWornItem, null);
        if (!ReferenceEquals(_buildSession, session)) { _wornBuilds.Clear(); _buildSession = session; }
        var key = string.Join('|', worn.Select(item => $"{(int)item.Slot}:{item.Entity}:{item.Fields.Object.Prototype}"));
        if (_wornBuilds.TryGetValue((map.RuntimeMap, entity), out var cached) && cached.Worn == key) return cached.Build;
        var build = SessionCritterCombat.FromWorn(entity, fields, worn.Select(item => KeyValuePair.Create(item.Slot, item.Fields)));
        _wornBuilds[(map.RuntimeMap, entity)] = (key, build);
        return build;
    }

    private SessionCritterCombatBuild Build(AreaInventoryMap map, string entity)
    {
        if (_builds.TryGetValue((map.RuntimeMap, entity), out var cached)) return cached;
        SessionCritterCombatBuild build;
        if (!RowFields(map, entity, out var fields) || fields is null) build = new(SessionCritterCombatFailure.NotAnNpc, null);
        else
        {
            var holder = map.Seed.Holders.First(h => h.Entity == entity);
            var carried = new List<CombatFields>();
            foreach (var item in holder.Items)
                if (RowFields(map, item, out var itemFields) && itemFields is not null) carried.Add(itemFields);
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
