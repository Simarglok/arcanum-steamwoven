using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-042: an item's script attachment the session keeps because a run changed it (a wield-on script counting the effects it
/// added in a counter, ledger WE3): the item's entity, the attachment point and the slot as the run left it. Session item runs read it
/// instead of the host's data slot.
/// </summary>
public sealed record SessionItemScriptSlot(string Entity, int Point, int Script, uint LocalFlags, uint Counters);

public sealed partial class GameSessionOwner
{
    /// <summary>ARC-SCRIPT-042: the item script slots the session keeps, by entity in ordinal order and then by point.</summary>
    public IReadOnlyList<SessionItemScriptSlot> QueryItemScriptSlots()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_inventory.CaptureScriptSlots());
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
            _revision++;
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
    private sealed class ItemScriptRuns
    {
        private readonly GameSessionOwner _owner;
        private readonly ISessionItemScriptHost _host;
        private readonly int _map;
        private readonly SessionFlagReads _reads;
        private readonly List<SessionItemScriptRun> _runs = [];
        private readonly List<SessionScriptMessage> _messages = [];
        private readonly List<ScrFlagWrite> _writes = [];
        // ARC-SCRIPT-042: the items of the move with their magick/tech complexity (the objects whose slots and power ratios the runs may
        // use), the slots the runs changed, and the hero's effect list as the runs leave it with the steps that lead there.
        private readonly IReadOnlyDictionary<string, int> _items;
        private readonly Dictionary<(string Entity, int Point), ScrSlot> _slots = [];
        private readonly List<SessionEffect> _entries;
        private readonly List<EffectStep> _steps = [];
        private readonly Dictionary<int, SessionEffectDefinition> _definitions;
        private PreparedEffects? _prepared;

        internal ItemScriptRuns(GameSessionOwner owner, ISessionItemScriptHost host, int map, IReadOnlyDictionary<string, int> items)
        {
            _owner = owner; _host = host; _map = map; _items = items;
            _entries = [.. owner._effects.Entries];
            _definitions = new(owner._effects.Definitions);
            _reads = new(owner, host, this);
        }

        internal GameSessionFailure Failure { get; private set; }
        internal IReadOnlyList<SessionItemScriptRun> Runs => _runs.AsReadOnly();
        internal IReadOnlyList<SessionScriptMessage> Messages => _messages.AsReadOnly();
        /// <summary>Whether committing changes the session: a flag write, a slot or an effect change.</summary>
        internal bool Changes => _writes.Count > 0 || _slots.Count > 0 || _steps.Count > 0;

        internal ScrRunResult? Run(int point, string attachee, string triggerer, string? extra)
        {
            if (Failure != GameSessionFailure.None) return null;
            _reads.RatioReads = 0;
            var run = ScrDispatcher.RunObject(new(point, new ScrObjectRef(_map, attachee), new ScrObjectRef(_map, triggerer),
                extra is null ? null : new ScrObjectRef(_map, extra)), _reads, new() { Rules = _owner._rules.Scripts, Admission = SessionItemScripts.Admission });
            _owner.ThrowIfDisposed();
            _runs.Add(new(point, attachee, run));
            Failure = _reads.UnknownFlag || _reads.UnknownObject ? GameSessionFailure.ItemScriptUnsupported : _owner.ItemScriptEffects(run, _host, _messages);
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
            var aptitude = HazardOf(run, _reads.RatioReads) ? Aptitude() : (int?)null;
            foreach (var change in effects.EffectChanges)
            {
                var failure = Plan(change);
                if (failure != GameSessionFailure.None) return failure;
            }
            // A ratio read after an effect change of the same run (or after a call-ex, whose order the trace does not show) would have
            // read the changed aptitude in the original; the run read the aptitude before the change.
            if (aptitude is { } before && Aptitude() != before) return GameSessionFailure.ItemScriptUnsupported;
            foreach (var write in effects.GlobalFlags)
            {
                _writes.Add(write);
                _reads.Pending[write.Index] = write.Value;
            }
            foreach (var write in slots) _slots[(write.Attachee!.Value.Code, write.Point)] = write.Slot;
            return GameSessionFailure.None;
        }

        // A ratio read that may come after an effect change of the same run: the trace shows the outer run's order, but a call-ex callee's
        // reads and changes are not in it, so a run with a call-ex and any ratio read counts too.
        private static bool HazardOf(ScrRunResult run, int ratioReads)
        {
            var trace = run.Trace;
            var lastRatio = -1; var firstChange = int.MaxValue; var callEx = false;
            for (var i = 0; i < trace.Count; i++)
            {
                var action = trace[i].Action;
                if (action == Arcanum.Data.Scripts.ScrCatalogue.Action.MagickTechAdjustment) lastRatio = i;
                if (action is Arcanum.Data.Scripts.ScrCatalogue.Action.AddEffect or Arcanum.Data.Scripts.ScrCatalogue.Action.RemoveEffect) firstChange = Math.Min(firstChange, i);
                callEx |= action == Arcanum.Data.Scripts.ScrCatalogue.Action.CallScriptEx;
            }
            return run.Effects.EffectChanges.Count > 0 && ratioReads > 0 && (callEx || firstChange < lastRatio);
        }

        // One effect action on the hero, checked as AddHeroEffect and RemoveHeroEffect check theirs (0x4E9F70, 0x4EA100); another object is
        // not the session's.
        private GameSessionFailure Plan(ScrEffectRequest change)
        {
            if (change.Object.Code != SessionEventKinds.PlayerEntity || change.Object.Map != _map) return GameSessionFailure.ItemScriptUnsupported;
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
            if (!EffectDomain.Bounded(_entries, _definitions)) return GameSessionFailure.InvalidEffect;
            _steps.Add(new(entry, -1));
            return GameSessionFailure.None;
        }

        // The hero's magick/tech aptitude under the effect list the runs left.
        private int Aptitude() => _steps.Count == 0 ? _owner.HeroAptitude() : _owner.TrialAptitude(_entries, _definitions);

        /// <summary>WE2: an item of the move and its owner's power ratio (0x461540): the hero's aptitude for the PC, 100 for an owner that is
        /// one of the move's items (not a critter); null for anything else.</summary>
        internal int? PowerRatio(ScrObjectRef item, ScrObjectRef owner)
        {
            // A complexity beyond the wear rules' bound is not one the rules compute (none installed: items are within ±100).
            if (item.Map != _map || !_items.TryGetValue(item.Code, out var complexity) || owner.Map != _map ||
                complexity is < -EquipmentLimits.MaxMagnitude or > EquipmentLimits.MaxMagnitude) return null;
            if (owner.Code == SessionEventKinds.PlayerEntity) return _owner._hero is null ? null : EquipmentRules.EffectivePowerRatio(complexity, Aptitude());
            return _items.ContainsKey(owner.Code) ? 100 : null;
        }

        /// <summary>The slot a run of the move changed, or null.</summary>
        internal ScrSlot? Slot(ScrObjectRef attachee, int point) =>
            attachee.Map == _map && _slots.TryGetValue((attachee.Code, point), out var slot) ? slot : null;

        /// <summary>Checks what the commit needs: the flag revisions, the room for the kept slots and the effect steps on a trial hero.</summary>
        internal void Prepare()
        {
            if (Failure != GameSessionFailure.None) return;
            if (_writes.Count > 0 && _owner._flags.Version.Revision > GlobalFlagLimits.MaxRevision - _writes.Count) { Failure = GameSessionFailure.RevisionExhausted; return; }
            if (_slots.Keys.Count(key => !_owner._inventory.ScriptSlots.ContainsKey(key)) > SessionItemScripts.MaxStoredSlots - _owner._inventory.ScriptSlots.Count)
            {
                Failure = GameSessionFailure.BudgetExceeded;
                return;
            }
            if (_steps.Count > 0) Failure = _owner.PrepareEffects([.. _steps], _definitions, 0, out _prepared);
        }

        /// <summary>Applies the prepared changes, without a revision: the flags, the kept slots and the effect steps.</summary>
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
            foreach (var (key, slot) in _slots) _owner._inventory.ScriptSlots[key] = slot;
            if (_prepared is not null) _owner.ApplyEffects(_prepared);
        }
    }

    // What the session applies from an item script run: global flags, printed lines and (ARC-SCRIPT-042) effect changes, slots and eye
    // candy, which ItemScriptRuns checks. A script outside the admitted set, a trap object to destroy and anything else refuse the move; a
    // flag outside the session's domain and a line the host cannot show do too. Host reads only.
    private GameSessionFailure ItemScriptEffects(ScrRunResult run, ISessionItemScriptHost scripts, List<SessionScriptMessage> messages)
    {
        // A script file that exists but cannot be read is not the original's missing file: refused, not skipped.
        if (run.Outcome == ScrOutcome.Refused || run.Stop == ScrStop.UnreadableScript) return GameSessionFailure.ItemScriptUnsupported;
        var effects = run.Effects;
        if (effects.DestroyTrapObject is not null || effects.Dialog is not null || effects.StoryState is not null || effects.Calls.Count > 0)
            return GameSessionFailure.ItemScriptUnsupported;
        foreach (var write in effects.GlobalFlags)
        {
            var read = _flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), _flags.Version, new FlagId(write.Index)));
            if (read.Status != GlobalFlagReadStatus.Known) return GameSessionFailure.ItemScriptUnsupported;
        }
        foreach (var line in effects.Lines)
        {
            var printed = scripts.PrintedLine(line.Script, line.Line) ?? throw new InvalidOperationException("ITEM_SCRIPT_LINE_NULL");
            if (printed.Status == SessionPrintedLineStatus.Unsupported) return GameSessionFailure.ItemScriptUnsupported;
            if (printed.Status == SessionPrintedLineStatus.Shown) messages.Add(new(line.Script, line.Line, line.Kind, printed.Text ?? string.Empty));
        }
        return GameSessionFailure.None;
    }

    /// <summary>ARC-SCRIPT-042: the hero's magick/tech aptitude, as <see cref="HeroAptitude"/> reads it, with the hero's effect list
    /// replaced by <paramref name="entries"/>.</summary>
    private int TrialAptitude(IReadOnlyList<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions)
    {
        var trial = TrialHero();
        foreach (var entry in entries) ApplyStats(trial.Stats, entry, definitions);
        return Math.Clamp(trial.Stats.Query(new(0)).Snapshot!.GetEffective(CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
    }

    /// <summary>
    /// ARC-SCRIPT-013: the item script host with global flags read from the session; an unknown flag answers false and is recorded, and
    /// the session then refuses the move. ARC-SCRIPT-042: slots come from the move's earlier runs, then from the session's kept slots, then
    /// from the host; the identified test is the host's, the power ratio the session's, and the items are never being rewielded (the
    /// session has no rewield yet).
    /// </summary>
    private sealed class SessionFlagReads(GameSessionOwner session, ISessionItemScriptHost host, ItemScriptRuns runs) : IScrHost, IScrItemQueries
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

        public ScrSlot? Slot(ScrObjectRef attachee, int point) =>
            runs.Slot(attachee, point) ?? (session._inventory.ScriptSlots.TryGetValue((attachee.Code, point), out var kept) ? kept : host.Slot(attachee, point));
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

        public bool ItemsBeingRewielded() => false;
    }
}
