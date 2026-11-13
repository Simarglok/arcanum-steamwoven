using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Events;

namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-042: an item's script attachment the session keeps because a run changed it (a wield-on script counting the effects it
/// added in a counter, ledger WE3): the item's entity, the attachment point and the slot as the run left it. Session item runs read it
/// instead of the host's data slot.
/// </summary>
public sealed record SessionItemScriptSlot(string Entity, int Point, int Script, uint LocalFlags, uint Counters);

/// <summary>
/// ARC-DEV-097 (MIG8, TM9, GR7, SA4): one element of a prototype's script array, as the host's prototype data holds it: the attachment point, the
/// script number (0 is "no script"; the element stays present, SA1), the 32 local flags and the four counters. An object created from the
/// prototype reads these until its first write.
/// </summary>
public readonly record struct SessionPrototypeScript(int Point, int Script, uint LocalFlags, uint Counters);

public sealed partial class GameSessionOwner
{
    /// <summary>ARC-SCRIPT-042: the item script slots the session keeps, by entity in ordinal order and then by point.</summary>
    public IReadOnlyList<SessionItemScriptSlot> QueryItemScriptSlots()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_inventory.CaptureScriptSlots());
    }

    private Func<int, IReadOnlyList<SessionPrototypeScript>?>? _prototypeScripts;

    /// <summary>
    /// ARC-DEV-097 (MIG8, TM9, GR7, SA4): attaches the host's prototype data for the script arrays of the objects the session creates from a
    /// prototype (<c>made:n</c> items): a created object owns no array and reads its prototype's until its first write, which copies every point
    /// of the prototype's array onto it (SA3). Not state: it is never saved and is the host's to attach again after a restore (the way
    /// <see cref="AttachItemWeights"/> is; the viewer attaches the scripts' source wherever it builds or restores a session, ARC-DEV-099, and both again when a script creates an item); null detaches it. Without one, or for a prototype it answers null or an invalid array for (a point
    /// outside 0..35 or twice, the dialog-override point 31 the store refuses, a script number outside 0..99999), a created item has no attachment
    /// until a run writes one, which is the older behaviour.
    /// </summary>
    public void AttachItemScripts(Func<int, IReadOnlyList<SessionPrototypeScript>?>? scripts)
    {
        ThrowIfDisposed();
        _prototypeScripts = scripts;
    }

    /// <summary>ARC-DEV-099: whether a prototype source is attached now (<see cref="AttachItemScripts"/>); a host check that it attached the source
    /// where it builds or restores a session. A read of the current state, in no revision.</summary>
    public bool HasItemScriptSource
    {
        get
        {
            ThrowIfDisposed();
            return _prototypeScripts is not null;
        }
    }

    /// <summary>
    /// ARC-DEV-097 (MIG8, SA2–SA4): the attachment an item has at a point now, as a script run reads it: the slot the session kept for the
    /// item (a run changed it), else, for an item the session created from a prototype that owns no kept slot, the prototype's element from
    /// <see cref="AttachItemScripts"/>; null for any other item (a map item's loaded slot is the host's data) and for a point with no element.
    /// A read of the current state, in no revision.
    /// </summary>
    public SessionItemScriptSlot? QueryItemScriptSlot(string entity, int point)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        if (_inventory.ScriptSlots.TryGetValue((entity, point), out var kept)) return new(entity, point, kept.Script, kept.LocalFlags, kept.Counters);
        return InheritedSlot(entity, point) is { } slot ? new(entity, point, slot.Script, slot.LocalFlags, slot.Counters) : null;
    }

    /// <summary>
    /// ARC-UI-1458 (FR-ITEM-008, FR-MAGIC-007): the saved kind of an item the session holds that stands on a prototype and owns no object row in any
    /// map: a made item (<c>made:n</c>) or one of the starting kit (<c>kit:n</c>), wherever it is now: the hero's lists, the ground, or an NPC's
    /// or container's list. A viewer describes such an item (the item window of an Identify, of a hover) from it, since no map row names its
    /// prototype. Null for any other entity (an item with a row, an unknown one, a deleted one nobody holds). A read of the current state, in no
    /// revision. Null for an invalid entity id too.
    /// </summary>
    public SessionItemKind? QueryPrototypeItemKind(string entity)
    {
        ThrowIfDisposed();
        return SessionValidation.Entity(entity) ? MadeKind(entity) : null;
    }

    // The kind of an item made from a prototype (a made item or one of the starting kit, ARC-UI-1458: the original makes every object as a copy of its prototype (docs/architecture/original-object-model.md),
    // so a kit item owns no row and reads its prototype's script array; the kit's creation itself was not read separately, F0) wherever the session holds it: the hero's
    // made items, the hero's kit, the ground, or an NPC's or container's list.
    private SessionItemKind? MadeKind(string entity)
    {
        var origin = ObjectOriginOf(entity);
        if (origin is not (SessionObjectOrigin.Made or SessionObjectOrigin.Kit)) return null;
        foreach (var made in _inventory.Made)
            if (made.Entity == entity) return made.Kind;
        foreach (var kit in _inventory.Kit)
            if (kit.Entity == entity) return kit.Kind;
        if (_inventory.PlacedItem(entity) is { } placed) return placed.Kind;
        if (_inventory.NpcOf(entity) is { } holder)
            foreach (var added in _inventory.Lists.Of(holder))
                if (added.Entity == entity) return added.Kind;
        return null;
    }

    // SA4: the prototype's array of an item the session created, when the item owns no slot of its own yet (the array is read through the
    // prototype until the first write); empty otherwise. The array is the host's answer checked against what the store can keep.
    private IReadOnlyList<SessionPrototypeScript> InheritedSlots(string entity)
    {
        if (_prototypeScripts is null || MadeKind(entity) is not { } kind || _inventory.ScriptSlots.Keys.Any(key => key.Entity == entity)) return [];
        var array = _prototypeScripts(kind.Prototype);
        ThrowIfDisposed();
        if (array is null) return [];
        var points = new HashSet<int>();
        foreach (var element in array)
            if (element.Point is < 0 or >= ScrPoint.Count || element.Point == ScrPoint.DialogOverride || element.Script is < 0 or > Arcanum.Data.Scripts.ScrCatalogue.MaxScriptNumber ||
                !points.Add(element.Point)) return [];
        return array;
    }

    private ScrSlot? InheritedSlot(string entity, int point)
    {
        foreach (var element in InheritedSlots(entity))
            if (element.Point == point) return new(element.Script, element.LocalFlags, element.Counters);
        return null;
    }

    // ARC-SCRIPT-041: a gate script returned skip default, so the item stays where it is. What the run did still happens, as in the
    // original: its global flag writes (one revision when there are any), its printed lines and (ARC-SCRIPT-042) its effects and slots.
    private InventoryMutationResult Declined(int point, ItemScriptRuns runs)
    {
        runs.Prepare();
        if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure) with { Scripts = runs.Runs };
        var changed = runs.Changes;
        if (changed)
        {
            runs.Commit();
            Revise();
        }
        return new(changed ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, new(_identity, _revision))
            { DeclinedBy = point, Scripts = runs.Runs, Messages = runs.Messages };
    }

    /// <summary>
    /// ARC-SCRIPT-041: the item script runs of one transfer or (ARC-SCRIPT-042) one equipment move, in order, under
    /// <see cref="SessionItemScripts.Admission"/>. Each run reads what the earlier runs of the move did (the original applies a run's
    /// changes before the next event): their global flags, the item slots they changed and, for the magick-tech adjustment, the hero's
    /// aptitude under their effect changes. Nothing is applied before <see cref="Commit"/>. The first run whose effects the session cannot
    /// apply sets <see cref="Failure"/>, and later runs are not made.
    /// </summary>
    internal sealed class ItemScriptRuns
    {
        private readonly GameSessionOwner _owner;
        private readonly ISessionItemScriptHost _host;
        private readonly int _map;
        private readonly SessionFlagReads _reads;
        private readonly List<SessionItemScriptRun> _runs = [];
        private readonly List<SessionScriptMessage> _messages = [];
        private readonly List<ScrFlagWrite> _writes = [];
        // ARC-MAGIC-041 (TI8): the deferred call requests of the runs (call script in, remove script call), in action order; the commit puts
        // them in the session's event queue.
        private readonly List<ScrCallRequest> _calls = [];
        // ARC-SCRIPT-042: the items of the move with their magick/tech complexity (the objects whose slots and power ratios the runs may
        // use), the slots the runs changed, and the hero's effect list as the runs leave it with the steps that lead there.
        private readonly IReadOnlyDictionary<string, int> _items;
        private readonly Dictionary<(string Entity, int Point), ScrSlot> _slots = [];
        private readonly List<SessionEffect> _entries;
        private readonly List<EffectStep> _steps = [];
        private readonly Dictionary<int, SessionEffectDefinition> _definitions;
        private PreparedEffects? _prepared;
        private Dictionary<(string Entity, int Point), ScrSlot>? _planned;
        // ARC-MAGIC-1644: the wearer when it is an NPC (the hero otherwise): its list, stats and aptitude are the runs' reads and its effect list their target. The actions on the NPC's
        // list in order, as they apply.
        private readonly NpcWearer? _npc;
        private readonly List<NpcEffectOp> _npcOps = [];

        internal ItemScriptRuns(GameSessionOwner owner, ISessionItemScriptHost host, int map, IReadOnlyDictionary<string, int> items, NpcWearer? npc = null)
        {
            _owner = owner; _host = host; _map = map; _items = items; _npc = npc;
            if (npc is null)
            {
                _entries = [.. owner._effects.Entries];
                _definitions = new(owner._effects.Definitions);
            }
            else
            {
                var view = owner.QueryNpcEffects(npc.Target);
                _entries = [.. view?.Entries ?? []];
                _definitions = view is null ? [] : view.Definitions.ToDictionary(rule => rule.Number);
            }
            _reads = new(owner, host, this);
        }

        /// <summary>ARC-AI-1655, ARC-MAGIC-1684: the runs' triggerer is an NPC (its ground pickup, its best-items wield): a printed line is read for an NPC triggerer (<see cref="ISessionItemScriptHost.PrintedLineForNpc"/>). Every other move keeps the hero's reading, whatever its triggerer.</summary>
        internal bool NpcTriggerer { get; set; }

        /// <summary>ARC-MAGIC-1644 (IE7, WE6): the runs are the rewield's: the condition "items are being rewielded" answers true (0x5E87E8).</summary>
        internal bool Rewielding { get; init; }

        /// <summary>ARC-MAGIC-1744 (ledger RM3c, IE7): for the hero's rewield, the hero's stored stats as the runs read them: the state's array with the store of the magick or tech points that caused the rewield laid in (the setter stores
        /// before it rewields). Null for every other move: the runs read the hero as the session holds it.</summary>
        internal IReadOnlyList<int>? HeroStored { get; init; }

        /// <summary>Whether the wearer is the hero (an NPC wearer's runs meet the hero as another object, which the session holds no stats of).</summary>
        internal bool WearsHero => _npc is null;

        /// <summary>The wearer's entity: the hero's, or the NPC's.</summary>
        private string Wearer => _npc?.Entity ?? SessionEventKinds.PlayerEntity;

        /// <summary>ARC-MAGIC-1684: an effect the runs added to or removed from an NPC wearer's list changes a basic or tech skill, which the best-items choice reads (the weapon skill levels) and the session does not model.</summary>
        internal bool NpcSkillEffects { get; private set; }

        private static bool TouchesSkills(SessionEffectDefinition definition) => definition.Changes.Any(c => c.Kind is Arcanum.Data.Characters.EffectAttributeKind.BasicSkill or Arcanum.Data.Characters.EffectAttributeKind.TechSkill);

        /// <summary>ARC-MAGIC-1684: the number of actions the runs so far have made on an NPC wearer's effect list (0 for the hero).</summary>
        internal int NpcEffectChanges => _npcOps.Count;

        /// <summary>
        /// ARC-MAGIC-1684: what the best-items choice reads of an NPC wearer (<see cref="EquipmentChooser"/>: its damage bonus, aptitude and extraordinary intelligence) under the effect list the runs so far left, as the character
        /// rules derive them from the NPC's stored stats; null for the hero as wearer and for stored values or a list the rules refuse.
        /// </summary>
        internal (int DamageBonus, int Aptitude, bool ExtraordinaryIntelligence)? ChooserReads() =>
            _npc is { } npc && _owner.NpcScriptSnapshot(npc, _entries, _definitions) is { } snapshot
                ? (snapshot.GetEffective(CharacterStat.DamageBonus), snapshot.GetEffective(CharacterStat.MagickTechAptitude), snapshot.GetEffective(CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold)
                : null;

        internal GameSessionFailure Failure { get; private set; }
        internal IReadOnlyList<SessionItemScriptRun> Runs => _runs.AsReadOnly();
        internal IReadOnlyList<SessionScriptMessage> Messages => _messages.AsReadOnly();
        /// <summary>Whether committing changes the session: a flag write, a script store cell that differs from the session's (ARC-DEV-152),
        /// a slot or an effect change, a deferred call to schedule or one to remove that is pending (ARC-MAGIC-041).</summary>
        internal bool Changes => _writes.Count > 0 || _slots.Count > 0 || _steps.Count > 0 || _npcOps.Count > 0 || _owner.ScriptCallsChange(_calls) || _reads.StoreChanges;

        internal ScrRunResult? Run(int point, string attachee, string triggerer, string? extra)
        {
            if (Failure != GameSessionFailure.None) return null;
            _reads.RatioReads = 0;
            _reads.StatReads.Clear();
            var run = ScrDispatcher.RunObject(new(point, new ScrObjectRef(_map, attachee), new ScrObjectRef(_map, triggerer),
                extra is null ? null : new ScrObjectRef(_map, extra)), _reads, new() { Rules = _owner._rules.Scripts, Admission = SessionItemScripts.Admission });
            _owner.ThrowIfDisposed();
            _runs.Add(new(point, attachee, run));
            Failure = _reads.UnknownFlag || _reads.UnknownObject ? GameSessionFailure.ItemScriptUnsupported : _owner.ItemScriptEffects(run, _host, _messages, NpcTriggerer);
            if (Failure == GameSessionFailure.None) Failure = Accept(run);
            return run;
        }

        // The run's changes join the move's: flags, the item slots (call-ex runs' first, then the event's own), and the effect changes.
        private GameSessionFailure Accept(ScrRunResult run)
        {
            var effects = run.Effects;
            var slots = new List<ScrWriteback>(effects.SlotWrites);
            if (effects.Writeback is { } own) slots.Add(own);
            // A changed attachment is kept only for an item of the move; the PC's or any other object's has no home in the session.
            foreach (var write in slots)
                if (write.Source != ScrSource.Object || write.Attachee is not { } item || item.Map != _map || !_items.ContainsKey(item.Code))
                    return GameSessionFailure.ItemScriptUnsupported;
            var aptitude = HazardOf(run, _reads.RatioReads, Arcanum.Data.Scripts.ScrCatalogue.Action.MagickTechAdjustment) ? Aptitude() : (int?)null;
            // ARC-DEV-366: the same for get stat, which reads the hero's stat; the stats it read are asked again after the run's changes.
            var statReads = HazardOf(run, _reads.StatReads.Count, Arcanum.Data.Scripts.ScrCatalogue.Action.GetStat) ? _reads.StatReads.ToArray() : [];
            foreach (var change in effects.EffectChanges)
            {
                var failure = Plan(change);
                if (failure != GameSessionFailure.None) return failure;
            }
            // A ratio read after an effect change of the same run (or after a call-ex, whose order the trace does not show) would have
            // read the changed aptitude in the original; the run read the aptitude before the change. A stat read is the same.
            if (aptitude is { } before && Aptitude() != before) return GameSessionFailure.ItemScriptUnsupported;
            foreach (var (stat, value) in statReads)
                if (WearerStat(stat) != value) return GameSessionFailure.ItemScriptUnsupported;
            foreach (var write in effects.GlobalFlags)
            {
                _writes.Add(write);
                _reads.Pending[write.Index] = write.Value;
            }
            // ARC-DEV-152: the script stores, read by the later runs and applied at the commit (each change is a value differing from what
            // the run read, which already saw the earlier runs').
            foreach (var write in effects.GlobalVariables) _reads.PendingGlobalVariables[write.Index] = write.Value;
            // ARC-DEV-265: the session holds the arrays of the hero alone; another PC's cells are dropped (SessionScriptLimits.IsHero).
            foreach (var write in effects.PcVariables) if (SessionScriptLimits.IsHero(write.Pc)) _reads.PendingPcVariables[write.Index] = write.Value;
            foreach (var write in effects.PcFlags) if (SessionScriptLimits.IsHero(write.Pc)) _reads.PendingPcFlags[write.Index] = write.Value;
            foreach (var write in slots) _slots[(write.Attachee!.Value.Code, write.Point)] = write.Slot;
            _calls.AddRange(effects.Calls);
            return GameSessionFailure.None;
        }

        // A read (a power ratio, or get stat) that may come after an effect change of the same run: the trace shows the outer run's order,
        // but a call-ex callee's reads and changes are not in it, so a run with a call-ex and any such read counts too.
        private static bool HazardOf(ScrRunResult run, int reads, int readAction)
        {
            var trace = run.Trace;
            var lastRead = -1; var firstChange = int.MaxValue; var callEx = false;
            for (var i = 0; i < trace.Count; i++)
            {
                var action = trace[i].Action;
                if (action == readAction) lastRead = i;
                if (action is Arcanum.Data.Scripts.ScrCatalogue.Action.AddEffect or Arcanum.Data.Scripts.ScrCatalogue.Action.RemoveEffect) firstChange = Math.Min(firstChange, i);
                callEx |= action == Arcanum.Data.Scripts.ScrCatalogue.Action.CallScriptEx;
            }
            return run.Effects.EffectChanges.Count > 0 && reads > 0 && (callEx || firstChange < lastRead);
        }

        // One effect action on the hero, checked as AddHeroEffect and RemoveHeroEffect check theirs (0x4E9F70, 0x4EA100); another object is
        // not the session's.
        private GameSessionFailure Plan(ScrEffectRequest change)
        {
            if (change.Object.Code != Wearer || change.Object.Map != _map) return GameSessionFailure.ItemScriptUnsupported;
            if (_npc is not null) return PlanNpc(change);
            var number = change.Effect;
            SessionEffectDefinition? definition = null;
            if (change.Change == ScrEffectChange.Add)
            {
                definition = _host.EffectDefinition(number);
                _owner.ThrowIfDisposed();
                if (definition is null) return GameSessionFailure.ItemScriptUnsupported;
            }
            if (_owner._hero is not { } hero) return GameSessionFailure.EffectsUnavailable;
            if (definition is null)
            {
                if (EffectDomain.IsInnate(hero.State, number)) return GameSessionFailure.InnateEffect;
                var index = _entries.FindIndex(entry => entry.Number == number);
                if (index < 0) return GameSessionFailure.None;
                _entries.RemoveAt(index);
                _steps.Add(new(null, index));
                return GameSessionFailure.None;
            }
            if (!EffectDomain.IsValid(definition) || definition.Number != number || SessionItemScripts.Cause(change.Cause) is not { } cause || !EffectDomain.OwnCause(cause))
                return GameSessionFailure.InvalidEffect;
            if (EffectDomain.IsInnate(hero.State, number)) return GameSessionFailure.InnateEffect;
            if (_definitions.TryGetValue(number, out var known) && !known.SameAs(definition)) return GameSessionFailure.EffectMismatch;
            if (_entries.Count >= SessionEffectLimits.MaxHeroEffects) return GameSessionFailure.EffectCapacityExceeded;
            _definitions[number] = definition;
            var entry = new SessionEffect(number, cause);
            _entries.Add(entry);
            if (!EffectDomain.Bounded(_entries, _definitions, innate: _owner.HeroInnateRules())) return GameSessionFailure.InvalidEffect;
            _steps.Add(new(entry, -1));
            return GameSessionFailure.None;
        }

        // ARC-MAGIC-1644: one effect action on the NPC wearer, checked as its list admits it (0x4E9F70, 0x4EA100: the item cause, the number in use with the same rule, the capacity; the NPC's list has no innate guard); the life rules'
        // checks (the maxima, the budgets) are the commit's dry run (<see cref="Prepare"/>).
        private GameSessionFailure PlanNpc(ScrEffectRequest change)
        {
            var number = change.Effect;
            if (change.Change != ScrEffectChange.Add)
            {
                var index = _entries.FindIndex(entry => entry.Number == number);
                if (index < 0) return GameSessionFailure.None;
                if (_definitions.TryGetValue(number, out var leaving)) NpcSkillEffects |= TouchesSkills(leaving);
                _entries.RemoveAt(index);
                _npcOps.Add(new(null, default, 0, number, 1, null));
                return GameSessionFailure.None;
            }
            var definition = _host.EffectDefinition(number);
            _owner.ThrowIfDisposed();
            if (definition is null) return GameSessionFailure.ItemScriptUnsupported;
            if (!EffectDomain.IsValid(definition) || definition.Number != number || SessionItemScripts.Cause(change.Cause) is not { } cause || !EffectDomain.OwnCause(cause))
                return GameSessionFailure.InvalidEffect;
            if (_definitions.TryGetValue(number, out var known) && !known.Changes.IsEmpty && !known.SameAs(definition)) return GameSessionFailure.EffectMismatch;
            if (_entries.Count >= SessionNpcEffectLimits.MaxEntries) return GameSessionFailure.EffectCapacityExceeded;
            _definitions[number] = definition;
            NpcSkillEffects |= TouchesSkills(definition);
            _entries.Add(new SessionEffect(number, cause));
            _npcOps.Add(new(definition, cause, 1, null, 0, null));
            return GameSessionFailure.None;
        }

        // The wearer's magick/tech aptitude under the effect list the runs left.
        private int Aptitude() => _npc is { } npc ? _owner.NpcScriptAptitude(npc, _entries, _definitions) ?? 0
            : _steps.Count == 0 && HeroStored is null ? _owner.HeroAptitude() : _owner.TrialAptitude(_entries, _definitions, HeroStored);

        /// <summary>
        /// ARC-DEV-366 (GS1, GS3): the stat <paramref name="stat"/> of an object as get stat reads it (0x4B0490): the hero's, under the effect
        /// list the move's earlier runs left; null for any other object and for a stat the session does not hold (GS3), which refuses the run.
        /// </summary>
        internal int? Stat(ScrObjectRef obj, int stat) =>
            obj.Map != _map || obj.Code != Wearer ? null : WearerStat(stat);

        private int? WearerStat(int stat) => _npc is { } npc ? _owner.NpcScriptStat(npc, stat, _entries, _definitions)
            : _steps.Count == 0 && HeroStored is null ? _owner.QueryHeroScriptStat(stat) : _owner.TrialScriptStat(stat, _entries, _definitions, HeroStored);

        /// <summary>WE2: an item of the move and its owner's power ratio (0x461540): the hero's aptitude for the PC, 100 for an owner that is
        /// one of the move's items (not a critter); null for anything else.</summary>
        internal int? PowerRatio(ScrObjectRef item, ScrObjectRef owner)
        {
            // A complexity beyond the wear rules' bound is not one the rules compute (none installed: items are within ±100).
            if (item.Map != _map || !_items.TryGetValue(item.Code, out var complexity) || owner.Map != _map ||
                complexity is < -EquipmentLimits.MaxMagnitude or > EquipmentLimits.MaxMagnitude) return null;
            if (_npc is not null && owner.Code == _npc.Entity) return _owner.NpcScriptAptitude(_npc, _entries, _definitions) is { } npcAptitude ? EquipmentRules.EffectivePowerRatio(complexity, npcAptitude) : null;
            if (owner.Code == SessionEventKinds.PlayerEntity && _npc is null) return _owner._hero is null ? null : EquipmentRules.EffectivePowerRatio(complexity, Aptitude());
            return _items.ContainsKey(owner.Code) ? 100 : null;
        }

        /// <summary>
        /// ARC-DEV-097 (SA3): the slots the commit keeps. The first write to an item the session created from a prototype copies every point of the
        /// prototype's array onto the item (the original's setter deep-copies the whole array into an instance that owns none), then the written
        /// points replace their copies; any other item keeps just the points a run wrote.
        /// </summary>
        private Dictionary<(string Entity, int Point), ScrSlot> Writes()
        {
            var writes = new Dictionary<(string Entity, int Point), ScrSlot>();
            foreach (var entity in _slots.Keys.Select(key => key.Entity).Distinct(StringComparer.Ordinal))
                foreach (var element in _owner.InheritedSlots(entity))
            {
                // An all-zero element reads exactly as an absent one (the interpreter's default is the zero record), so it is not kept: the
                // copy of a sparse array costs the slots that say something.
                if (element is { Script: 0, LocalFlags: 0, Counters: 0 }) continue;
                writes[(entity, element.Point)] = new(element.Script, element.LocalFlags, element.Counters);
            }
            foreach (var (key, slot) in _slots) writes[key] = slot;
            return writes;
        }

        /// <summary>The slot a run of the move changed, or null.</summary>
        internal ScrSlot? Slot(ScrObjectRef attachee, int point) =>
            attachee.Map == _map && _slots.TryGetValue((attachee.Code, point), out var slot) ? slot : null;

        /// <summary>Checks what the commit needs: the flag revisions, the room for the kept slots and the effect steps on a trial hero.</summary>
        internal void Prepare()
        {
            if (Failure != GameSessionFailure.None) return;
            if (_writes.Count > 0 && _owner._flags.Version.Revision > GlobalFlagLimits.MaxRevision - _writes.Count) { Failure = GameSessionFailure.RevisionExhausted; return; }
            // The slots are planned once, here: the commit applies this plan and asks the host nothing more after the first flag write.
            _planned = Writes();
            if (_planned.Keys.Count(key => !_owner._inventory.ScriptSlots.ContainsKey(key)) > SessionItemScripts.MaxStoredSlots - _owner._inventory.ScriptSlots.Count)
            {
                Failure = GameSessionFailure.BudgetExceeded;
                return;
            }
            if (_steps.Count > 0) Failure = _owner.PrepareEffects([.. _steps], _definitions, 0, out _prepared);
            // ARC-MAGIC-1644: an NPC wearer's actions are checked as one sequence on its list (nothing written).
            if (Failure == GameSessionFailure.None && _npc is { } npc && _npcOps.Count > 0)
                Failure = _owner.ChangeNpcOps(_owner.Version, npc.Target, _npcOps, dryRun: true, revise: false).Failure;
            if (Failure == GameSessionFailure.None && _calls.Count > 0) Failure = _owner.CheckScriptCalls(_calls);
        }

        /// <summary>Applies the prepared changes, without a revision: the flags, the script stores (ARC-DEV-152), the kept slots and the
        /// effect steps.</summary>
        internal void Commit()
        {
            if (Failure != GameSessionFailure.None || (_steps.Count > 0 && _prepared is null)) throw new InvalidOperationException("ITEM_SCRIPT_COMMIT_UNPREPARED");
            foreach (var write in _writes)
            {
                var receipt = _owner._flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), _owner._flags.Version, new FlagId(write.Index), write.Value));
                // Checked before the commit; a refusal here would be a broken invariant of the flag owner.
                if (receipt.Disposition is not (GlobalFlagWriteDisposition.Applied or GlobalFlagWriteDisposition.NoChange))
                    throw new InvalidOperationException("ITEM_SCRIPT_FLAG_WRITE_" + receipt.Disposition);
            }
            _reads.CommitStores();
            foreach (var (key, slot) in _planned ?? throw new InvalidOperationException("ITEM_SCRIPT_COMMIT_UNPREPARED")) _owner._inventory.ScriptSlots[key] = slot;
            if (_prepared is not null) _owner.ApplyEffects(_prepared);
            if (_npc is { } npc && _npcOps.Count > 0 && _owner.ChangeNpcOps(_owner.Version, npc.Target, _npcOps, dryRun: false, revise: false).Failure != GameSessionFailure.None)
                throw new InvalidOperationException("ITEM_SCRIPT_NPC_EFFECTS_COMMIT");
            if (_calls.Count > 0) _owner.ApplyScriptCalls(_calls);
        }
    }

    // What the session applies from an item script run: global flags, printed lines and (ARC-SCRIPT-042) effect changes, slots and eye
    // candy, which ItemScriptRuns checks. A script outside the admitted set, a trap object to destroy and anything else refuse the move; a
    // flag outside the session's domain and a line the host cannot show do too. Host reads only.
    private GameSessionFailure ItemScriptEffects(ScrRunResult run, ISessionItemScriptHost scripts, List<SessionScriptMessage> messages, bool npcTriggerer)
    {
        // A script file that exists but cannot be read is not the original's missing file: refused, not skipped. ARC-DEV-366: a question the
        // session cannot answer (get stat of a stat it does not hold, GS3) ends the dispatch as not modelled with nothing applied: refused too.
        if (run.Outcome == ScrOutcome.Refused || run.Stop is ScrStop.UnreadableScript or ScrStop.NotModelled) return GameSessionFailure.ItemScriptUnsupported;
        var effects = run.Effects;
        // ARC-MAGIC-041: the deferred calls are the session's now (the move puts them in the event queue as it commits).
        if (effects.DestroyTrapObject is not null || effects.Dialog is not null || effects.StoryState is not null || effects.AmmoUses.Count > 0)
            return GameSessionFailure.ItemScriptUnsupported;
        foreach (var write in effects.GlobalFlags)
        {
            var read = _flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), _flags.Version, new FlagId(write.Index)));
            if (read.Status != GlobalFlagReadStatus.Known) return GameSessionFailure.ItemScriptUnsupported;
        }
        foreach (var line in effects.Lines)
        {
            var printed = (npcTriggerer ? scripts.PrintedLineForNpc(line.Script, line.Line) : scripts.PrintedLine(line.Script, line.Line)) ?? throw new InvalidOperationException("ITEM_SCRIPT_LINE_NULL");
            if (printed.Status == SessionPrintedLineStatus.Unsupported) return GameSessionFailure.ItemScriptUnsupported;
            if (printed.Status == SessionPrintedLineStatus.Shown) messages.Add(new(line.Script, line.Line, line.Kind, printed.Text ?? string.Empty));
        }
        return GameSessionFailure.None;
    }

    /// <summary>ARC-MAGIC-041: whether applying these call requests would change the queue: any schedule, or a removal of a call that is pending now.</summary>
    private bool ScriptCallsChange(IReadOnlyList<ScrCallRequest> calls) => calls.Any(call => call.Kind == ScrCallKind.Schedule ||
        _events.QueryPending().Any(e => e.Kind == SessionEventKinds.ScriptCall && SessionEventKinds.Call(e.Payload) is var pending && pending.Script == call.Script &&
            pending.Attachee?.Entity == RoleOf(call.Attachee)?.Entity));

    private static SessionScriptRole? RoleOf(ScrObjectRef? obj) => obj is { } o ? new SessionScriptRole(new SessionMapId(o.Map), o.Code) : null;

    /// <summary>
    /// ARC-MAGIC-041 (TI8): checks, before anything is applied, that the session can take an item run's deferred call requests: the roles and
    /// delays as <see cref="ScheduleScriptCall"/> checks them and the room in the event queue for the calls the move schedules (the calls it
    /// removes are not counted as free room: a move that needs the room fails, never half-applies).
    /// </summary>
    private GameSessionFailure CheckScriptCalls(IReadOnlyList<ScrCallRequest> calls)
    {
        var scheduled = 0;
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        foreach (var call in calls)
        {
            if (!ValidRole(RoleOf(call.Triggerer)) || !ValidRole(RoleOf(call.Attachee))) return GameSessionFailure.InvalidEvents;
            if (call.Kind != ScrCallKind.Schedule) continue;
            if (call.DelayMs is < 1 or > SessionEventKinds.MaxScriptCallDelayMs) return GameSessionFailure.InvalidEvents;
            if (now > long.MaxValue - call.DelayMs) return GameSessionFailure.EventBudgetExceeded;
            scheduled++;
        }
        if (scheduled == 0) return GameSessionFailure.None;
        return _events.QueryPending().Count(e => e.Kind == SessionEventKinds.ScriptCall) + scheduled > SessionEventKinds.MaxScriptCalls ||
            _events.Count + scheduled > _events.Limits.MaxPending ? GameSessionFailure.EventBudgetExceeded : GameSessionFailure.None;
    }

    /// <summary>
    /// ARC-MAGIC-041 (TI8): puts an item run's deferred call requests in the event queue, in action order, with no revision of their own (the
    /// move takes one): a schedule as <see cref="ScheduleScriptCall"/> does, a removal as <see cref="RemoveScriptCall"/> does (the first
    /// pending call of the script whose attachee is the named object, none matching: nothing). <see cref="CheckScriptCalls"/> checked them.
    /// </summary>
    private void ApplyScriptCalls(IReadOnlyList<ScrCallRequest> calls)
    {
        foreach (var call in calls)
        {
            var attachee = RoleOf(call.Attachee);
            if (call.Kind == ScrCallKind.Remove)
            {
                _events.RemoveFirst(SessionEventKinds.ScriptCall, e => SessionEventKinds.Call(e.Payload) is var pending && pending.Script == call.Script && pending.Attachee?.Entity == attachee?.Entity);
                continue;
            }
            var now = _clock.QueryClockSnapshot().GameTimestampMs;
            var scheduled = _events.ScheduleAfter(SessionEventKinds.ScriptCall,
                SessionEventKinds.Payload(new SessionScriptCall(call.Script, call.Line, RoleOf(call.Triggerer), attachee), now, call.DelayMs, _actor.Map.RuntimeMap),
                call.DelayMs, EventClocks(now));
            // Checked before the commit; a refusal here would be a broken invariant of the event queue.
            if (scheduled.Disposition != GameEventScheduleDisposition.Added) throw new InvalidOperationException("ITEM_SCRIPT_CALL_" + scheduled.Disposition);
        }
    }

    /// <summary>ARC-SCRIPT-042: the hero's magick/tech aptitude, as <see cref="HeroAptitude"/> reads it, with the hero's effect list
    /// replaced by <paramref name="entries"/>.</summary>
    private int TrialAptitude(IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions, IReadOnlyList<int>? stored = null)
    {
        var trial = TrialHero(stored);
        foreach (var entry in entries) ApplyStats(trial.Stats, entry, definitions);
        return Math.Clamp(trial.Stats.Query(HeroStatContext()).Snapshot!.GetEffective(CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
    }

    /// <summary>
    /// ARC-DEV-366: <see cref="QueryHeroScriptStat"/> with the hero's effect list replaced by <paramref name="entries"/> (a move's earlier runs
    /// left it): the effective stat under them with the life's poison and crippled legs. The level and the alignment are the stored values
    /// (effects on them are not read, GS3); null for a stat the session does not hold and without a hero.
    /// </summary>
    private int? TrialScriptStat(int stat, IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions, IReadOnlyList<int>? stored = null)
    {
        if (_hero is null) return null;
        if (stat is ScriptStatLevel or ScriptStatAlignment) return QueryHeroScriptStat(stat);
        if (stat is not (>= 0 and <= 7 or ScriptStatAptitude) || QueryHeroLife() is not { } life) return null;
        var trial = TrialHero(stored);
        foreach (var entry in entries) ApplyStats(trial.Stats, entry, definitions);
        var state = life.Record.State;
        var read = trial.Stats.Query(new(0, Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel), state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth)));
        return read.Snapshot?.GetEffective(stat == ScriptStatAptitude ? CharacterStat.MagickTechAptitude : (CharacterStat)stat);
    }

    /// <summary>
    /// ARC-SCRIPT-013: the item script host with global flags read from the session; an unknown flag answers false and is recorded, and
    /// the session then refuses the move. ARC-SCRIPT-042: slots come from the move's earlier runs, then from the session's kept slots, then
    /// from the host; the identified test is the host's, the power ratio the session's, and the items are being rewielded only
    /// in the runs of an NPC's rewield (ARC-MAGIC-1644).
    /// </summary>
    private sealed class SessionFlagReads(GameSessionOwner session, ISessionItemScriptHost host, ItemScriptRuns runs) : IScrHost, IScrItemQueries, IScrStoreQueries, IScrStatQueries
    {
        internal bool UnknownFlag { get; private set; }
        /// <summary>An item question about an object the session and the host do not know.</summary>
        internal bool UnknownObject { get; private set; }

        /// <summary>ARC-SCRIPT-041: writes of earlier runs of the same transfer, not applied yet; a read sees them first.</summary>
        internal Dictionary<int, bool> Pending { get; } = [];

        public bool GlobalFlag(int index)
        {
            if (Pending.TryGetValue(index, out var pending)) return pending;
            var flags = session.Flags;
            var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(index)));
            if (read.Status == GlobalFlagReadStatus.Known && read.Value is { } value) return value;
            UnknownFlag = true;
            return false;
        }

        /// <summary>ARC-DEV-152: the script store writes of earlier runs of the move, not applied yet; a read sees them first.</summary>
        internal Dictionary<int, int> PendingGlobalVariables { get; } = [];
        internal Dictionary<int, int> PendingPcVariables { get; } = [];
        internal Dictionary<int, bool> PendingPcFlags { get; } = [];

        public int GlobalVariable(int index) => PendingGlobalVariables.TryGetValue(index, out var pending) ? pending : session.QueryGlobalVariable(index);
        // A session without a hero has no PC store: its PC cells read 0 even after an earlier run of the move wrote them (SA14). ARC-DEV-265:
        // a PC that is not the hero has no arrays in the session and reads 0.
        public int PcVariable(ScrObjectRef pc, int index) => !SessionScriptLimits.IsHero(pc) ? 0
            : session._hero is not null && PendingPcVariables.TryGetValue(index, out var pending) ? pending : session.QueryPcVariable(index);
        public bool PcFlag(ScrObjectRef pc, int index) => SessionScriptLimits.IsHero(pc)
            && (session._hero is not null && PendingPcFlags.TryGetValue(index, out var pending) ? pending : session.QueryPcFlag(index));

        /// <summary>Whether a pending cell differs from the session's (a later run may have written the value back).</summary>
        internal bool StoreChanges =>
            PendingGlobalVariables.Any(pair => pair.Value != session.QueryGlobalVariable(pair.Key))
            || session._hero is not null && (PendingPcVariables.Any(pair => pair.Value != session.QueryPcVariable(pair.Key))
                || PendingPcFlags.Any(pair => pair.Value != session.QueryPcFlag(pair.Key)));

        /// <summary>
        /// Writes the pending cells into the session's stores, without a revision (the move takes one). A session without a hero keeps no
        /// PC store, and the original ignores a PC write to an object that is not a PC (SA14): its PC cells are dropped.
        /// </summary>
        internal void CommitStores()
        {
            foreach (var (index, value) in PendingGlobalVariables) ScriptStores.Write(session._scripts.GlobalVariables, index, value);
            // ARC-DEV-266: the PC's arrays are the hero's own.
            if (session._hero is not { } hero) return;
            foreach (var (index, value) in PendingPcVariables) ScriptStores.Write(hero.PcVariables, index, value);
            foreach (var (flag, value) in PendingPcFlags)
            {
                var word = ScriptStores.Read(hero.PcFlags, flag >> 5);
                ScriptStores.Write(hero.PcFlags, flag >> 5, value ? word | 1 << (flag & 31) : word & ~(1 << (flag & 31)));
            }
        }

        public ScrSlot? Slot(ScrObjectRef attachee, int point) =>
            runs.Slot(attachee, point) ?? (session._inventory.ScriptSlots.TryGetValue((attachee.Code, point), out var kept) ? kept : session.InheritedSlot(attachee.Code, point) ?? host.Slot(attachee, point));
        public ScrScriptLookup Script(int number) => host.Script(number);
        public int StoryState() => host.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => host.IsMonsterGenerator(attachee);
        public ScrDialogState DialogState(ScrObjectRef attachee) => host.DialogState(attachee);
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) =>
            host.CheckTrapBeforeUse(triggerer, attachee);
        public bool IsTrapObject(ScrObjectRef attachee) => host.IsTrapObject(attachee);
        public ScrObjectRef? Player() => host.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => host.Leader(critter);
        public bool IsJilted(ScrObjectRef critter) => host.IsJilted(critter);
        public bool IsPc(ScrObjectRef critter) => host.IsPc(critter);

        public bool? IsIdentified(ScrObjectRef obj)
        {
            var identified = host.IsIdentified(obj);
            UnknownObject |= identified is null;
            return identified;
        }

        /// <summary>How many power ratios the current run read (call-ex callees included).</summary>
        internal int RatioReads { get; set; }

        public int? PowerRatio(ScrObjectRef item, ScrObjectRef owner)
        {
            RatioReads++;
            var ratio = runs.PowerRatio(item, owner);
            UnknownObject |= ratio is null;
            return ratio;
        }

        public bool ItemsBeingRewielded() => runs.Rewielding;

        /// <summary>The stats (number, value as the host answered) the current run read through get stat, in order.</summary>
        internal List<(int Stat, int? Value)> StatReads { get; } = [];

        /// <summary>ARC-DEV-366 (GS1): the hero's stat under the move's effect changes so far; any other object, a stat the session does not hold
        /// and a session without a hero answer null, which ends the run as not modelled and refuses the move.</summary>
        public int? EffectiveStat(ScrObjectRef obj, int stat)
        {
            var value = runs.Stat(obj, stat);
            if (value is not null) StatReads.Add((stat, value));
            return value;
        }

        /// <summary>GS2: the dumb test's background rule, asked only for the hero (the one object <see cref="EffectiveStat"/> answers for).</summary>
        public bool IsDumbByBackground(ScrObjectRef obj) => obj.Code == SessionEventKinds.PlayerEntity && runs.WearsHero && session.QueryHeroDumbBackground();
    }
}
