using System.Collections.Frozen;
using Arcanum.Data.Scripts;

namespace Arcanum.Scripting.Scr;

/// <summary>Why a script was refused before it could run (docs/scripting/scr-program-admission.md).</summary>
public enum ScrRefusalCode
{
    /// <summary>A condition type outside the catalogue (0..52).</summary>
    UnknownCondition,
    /// <summary>An action type outside the catalogue (0..141).</summary>
    UnknownAction,
    /// <summary>A known condition type the admitted set does not run.</summary>
    ConditionNotAdmitted,
    /// <summary>A known action type the admitted set does not run.</summary>
    ActionNotAdmitted,
    /// <summary>A number operand whose kind byte is not one of the seven value kinds.</summary>
    UnknownValueKind,
    /// <summary>A known value kind the admitted set does not read.</summary>
    ValueKindNotAdmitted,
    /// <summary>An object operand whose kind byte is not one of the 24 focus kinds.</summary>
    UnknownFocusKind,
    /// <summary>A known focus kind the admitted set does not resolve.</summary>
    FocusKindNotAdmitted,
    /// <summary>A literal index outside its store: local flag 0..31, counter 0..3, local variable 0..9,
    /// global flag 0..3199, global variable 0..1999.</summary>
    IndexOutOfRange,
    /// <summary>An action stores into a number literal.</summary>
    WriteToNumber,
    /// <summary>A goto whose target is not a number literal.</summary>
    ComputedGoto,
    /// <summary>A literal script number for change this script outside 0..99999 (the original stores it as is).</summary>
    ScriptNumberOutOfRange,
    /// <summary>A literal goto below line 0 (a source reading takes −1..−4 as control codes, anomaly X6).</summary>
    NegativeGoto,
    /// <summary>A literal goto past the line after the last one.</summary>
    GotoBeyondEnd,
}

/// <summary>The first thing that keeps a script from being admitted: where it is and what it is.</summary>
/// <param name="Slot">The operand slot, for a refusal of one operand.</param>
/// <param name="Type">The clause's type number.</param>
/// <param name="Kind">The operand's kind byte, for an operand refusal.</param>
/// <param name="Value">The operand's value, for an index or goto refusal.</param>
public sealed record ScrRefusal(int Line, ScrClauseSide Side, ScrRefusalCode Code, int Type, int? Slot = null, byte? Kind = null, int? Value = null);

/// <summary>The condition, action, value and focus kinds an interpreter version runs. A set is
/// data: a later slice widens it by adding types, never by changing what admission checks.</summary>
public sealed class ScrAdmissionSet
{
    /// <param name="counterIndexWraps">ARC-WORLD-081: whether a counter operand may name any index, read as the original's counter
    /// reader takes it (PG6: index mod 4); otherwise an index outside 0..3 refuses the script.</param>
    public ScrAdmissionSet(string id, IEnumerable<int> conditions, IEnumerable<int> actions, IEnumerable<ScrValueKind> valueKinds, IEnumerable<ScrFocusKind> focusKinds,
        bool counterIndexWraps = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id; CounterIndexWraps = counterIndexWraps;
        Conditions = Freeze(conditions, ScrCatalogue.IsKnownCondition);
        Actions = Freeze(actions, ScrCatalogue.IsKnownAction);
        ValueKinds = Freeze(valueKinds, k => Enum.IsDefined(k));
        FocusKinds = Freeze(focusKinds, k => Enum.IsDefined(k));
    }

    private static IReadOnlySet<T> Freeze<T>(IEnumerable<T> values, Func<T, bool> known)
    {
        ArgumentNullException.ThrowIfNull(values);
        var set = values.ToHashSet();
        if (set.Any(v => !known(v))) throw new ArgumentOutOfRangeException(nameof(values), "An admitted type must be in the SCR catalogue.");
        return set.ToFrozenSet();
    }

    public string Id { get; }
    public IReadOnlySet<int> Conditions { get; }
    public IReadOnlySet<int> Actions { get; }
    public IReadOnlySet<ScrValueKind> ValueKinds { get; }
    public IReadOnlySet<ScrFocusKind> FocusKinds { get; }
    /// <summary>ARC-WORLD-081: a counter operand with an index outside 0..3 is admitted and reads counter (index mod 4), as the original's
    /// counter reader does (ledger PG6; the plant generator 30285 names counter 2,012,780,696, which reads counter 0).</summary>
    public bool CounterIndexWraps { get; }

    /// <summary>The first admitted set (ARC-SCRIPT-007, from the ARC-RES-045 census): control flow
    /// and the two returns, goto, local flags and counters, local variables, arithmetic, global
    /// flags and story state. No object operand.</summary>
    public static ScrAdmissionSet S1 { get; } = new("S1",
        [ScrCatalogue.Condition.True, ScrCatalogue.Condition.LocalFlag, ScrCatalogue.Condition.Equal, ScrCatalogue.Condition.LessOrEqual, ScrCatalogue.Condition.GlobalFlag],
        [ScrCatalogue.Action.DoNothing, ScrCatalogue.Action.ReturnSkipDefault, ScrCatalogue.Action.ReturnRunDefault, ScrCatalogue.Action.Goto,
         ScrCatalogue.Action.SetLocalFlag, ScrCatalogue.Action.ClearLocalFlag, ScrCatalogue.Action.Assign, ScrCatalogue.Action.Add,
         ScrCatalogue.Action.Subtract, ScrCatalogue.Action.Multiply, ScrCatalogue.Action.Divide, ScrCatalogue.Action.GetStoryState,
         ScrCatalogue.Action.SetStoryState, ScrCatalogue.Action.SetGlobalFlag, ScrCatalogue.Action.ClearGlobalFlag],
        [ScrValueKind.Counter, ScrValueKind.LocalVariable, ScrValueKind.Number, ScrValueKind.GlobalFlag], []);

    /// <summary>The set the dispatcher core runs (ARC-SCRIPT-008): <see cref="S1"/> plus remove this script and change
    /// this script, whose new number takes effect after the run.</summary>
    public static ScrAdmissionSet S2 { get; } = new("S2", S1.Conditions,
        S1.Actions.Append(ScrCatalogue.Action.RemoveThisScript).Append(ScrCatalogue.Action.ChangeThisScript), S1.ValueKinds, S1.FocusKinds);

    /// <summary>The dialogue set (ARC-SCRIPT-009): <see cref="S2"/> plus the follower and jilted conditions and the
    /// dialog action, with the single-object focus kinds triggerer, attachee, extra object and player.</summary>
    public static ScrAdmissionSet S3 { get; } = new("S3", S2.Conditions.Append(ScrCatalogue.Condition.FollowingPc).Append(ScrCatalogue.Condition.Jilted),
        S2.Actions.Append(ScrCatalogue.Action.Dialog), S2.ValueKinds,
        [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject, ScrFocusKind.Player]);

    /// <summary>The deferred-call set (ARC-SCRIPT-010): <see cref="S3"/> plus call script in (54) and remove script call
    /// (125). Call script at (55) stays outside: one installed line uses it, none a run reaches (ARC-RES-045), and its
    /// time-of-day arithmetic (TE2) is not settled.</summary>
    public static ScrAdmissionSet S5 { get; } = new("S5", S3.Conditions,
        S3.Actions.Append(ScrCatalogue.Action.CallScriptIn).Append(ScrCatalogue.Action.RemoveScriptCall), S3.ValueKinds, S3.FocusKinds);

    /// <summary>The print-line set (ARC-SCRIPT-013): <see cref="S5"/> plus print line (24), which hands a line of the running
    /// script's DLG and a number to the host as a <see cref="ScrPrintLine"/> effect (ledger IS1–IS3,
    /// docs/research/item-insert-script.md).</summary>
    public static ScrAdmissionSet S6 { get; } = new("S6", S5.Conditions, S5.Actions.Append(ScrCatalogue.Action.PrintLine), S5.ValueKinds, S5.FocusKinds);

    /// <summary>The sound set (ARC-SCRIPT-040): <see cref="S6"/> plus play sound (82), play sound on (83) and play sound scheme
    /// (87), which hand <see cref="ScrSound"/> effects to the host (audio study SN28).</summary>
    public static ScrAdmissionSet S7 { get; } = new("S7", S6.Conditions,
        S6.Actions.Append(ScrCatalogue.Action.PlaySound).Append(ScrCatalogue.Action.PlaySoundOn).Append(ScrCatalogue.Action.PlaySoundScheme), S6.ValueKinds, S6.FocusKinds);

    /// <summary>
    /// The teleport set (ARC-SCRIPT-014, the dispatcher part of plan slice S4): <see cref="S7"/> plus teleport (44) and
    /// fade-and-teleport (102), which hand <see cref="ScrTeleport"/> effects to the host, and the guard conditions of the
    /// conditional map transitions (ARC-RES-035): PC quest state (6), holds an item named (17), is named (20), wields an
    /// item named (21) and has a reputation (33), which the host answers through <see cref="IScrObjectQueries"/> (ledger
    /// SG1; the teleports TR1, TR2). Focus stays one object, so the conditions over focus groups are not admitted yet.
    /// </summary>
    public static ScrAdmissionSet S4 { get; } = new("S4", S7.Conditions.Concat([ScrCatalogue.Condition.PcQuestState, ScrCatalogue.Condition.HasItemNamed,
            ScrCatalogue.Condition.IsNamed, ScrCatalogue.Condition.IsWieldingItem, ScrCatalogue.Condition.HasReputation]),
        S7.Actions.Append(ScrCatalogue.Action.Teleport).Append(ScrCatalogue.Action.FadeAndTeleport), S7.ValueKinds, S7.FocusKinds);

    /// <summary>
    /// The scheme set (ARC-SCRIPT-016): <see cref="S4"/> plus set auto-level scheme (139), which hands one
    /// <see cref="ScrSchemeRequest"/> per object of its object operand to the host (ledger OL30). Its focus kinds stay the four
    /// single objects of <see cref="S3"/>; focus groups are not admitted yet.
    /// </summary>
    public static ScrAdmissionSet S8 { get; } = new("S8", S4.Conditions, S4.Actions.Append(ScrCatalogue.Action.SetAutoLevelScheme), S4.ValueKinds, S4.FocusKinds);

    /// <summary>
    /// The schematic set (ARC-TECHNOLOGY-005): <see cref="S8"/> plus start schematic UI (129), which hands one
    /// <see cref="ScrSchematicRequest"/> for its object operand to the host (ledger SW8; TC2, both fixed).
    /// </summary>
    public static ScrAdmissionSet S9 { get; } = new("S9", S8.Conditions, S8.Actions.Append(ScrCatalogue.Action.StartSchematicUi), S8.ValueKinds, S8.FocusKinds);

    /// <summary>
    /// The reading set (ARC-UI-049): <see cref="S9"/> plus show book (64), show picture (65) and show plaque (114), each handing one
    /// <see cref="ScrReadingRequest"/> (its number and its viewer object) to the host (ledger WR rows).
    /// </summary>
    public static ScrAdmissionSet S10 { get; } = new("S10", S9.Conditions,
        S9.Actions.Concat([ScrCatalogue.Action.ShowBook, ScrCatalogue.Action.ShowPicture, ScrCatalogue.Action.ShowPlaque]), S9.ValueKinds, S9.FocusKinds);

    /// <summary>
    /// The skill set (ARC-SKILLS-009, ledger SX1–SX4): <see cref="S10"/> plus get skill (47), which stores an object's effective skill
    /// level and needs a host that implements <see cref="IScrSkillQueries"/>, and use item with skill (79), which hands one request to
    /// the host.
    /// </summary>
    public static ScrAdmissionSet S11 { get; } = new("S11", S10.Conditions,
        S10.Actions.Concat([ScrCatalogue.Action.GetSkill, ScrCatalogue.Action.UseItemWithSkill]), S10.ValueKinds, S10.FocusKinds);

    /// <summary>
    /// The object-variable set (ARC-SCRIPT-018): <see cref="S11"/> plus call script (7), assign object (15) and get object type
    /// (34), and the run's ten object variables as a focus kind (11). Call script runs another script at once with the same
    /// set (ledger OV1); assign object writes an object into the run's triggerer, attachee, extra object or an object variable
    /// (OV2); get object type writes an object's type into a number store, which the host answers through
    /// <see cref="IScrObjectQueries.ObjectType"/> (OV3).
    /// </summary>
    public static ScrAdmissionSet S12 { get; } = new("S12", S11.Conditions,
        S11.Actions.Concat([ScrCatalogue.Action.CallScript, ScrCatalogue.Action.AssignObject, ScrCatalogue.Action.GetObjectType]), S11.ValueKinds,
        S11.FocusKinds.Append(ScrFocusKind.LocalObject));

    /// <summary>
    /// The story set (ARC-SCRIPT-019): <see cref="S12"/> plus set PC quest state (16), mark map location (49), set rumor (50), add
    /// reputation (72) and remove reputation (73), which hand <see cref="ScrStateRequest"/> effects to the host (ledger SS2–SS5).
    /// </summary>
    public static ScrAdmissionSet S13 { get; } = new("S13", S12.Conditions,
        S12.Actions.Concat([ScrCatalogue.Action.SetPcQuestState, ScrCatalogue.Action.MarkMapLocation, ScrCatalogue.Action.SetRumor,
            ScrCatalogue.Action.AddReputation, ScrCatalogue.Action.RemoveReputation]), S12.ValueKinds, S12.FocusKinds);

    /// <summary>
    /// The item-effect set (ARC-SCRIPT-042, ledger WE1–WE6): <see cref="S13"/> plus the conditions identified (47) and items being
    /// rewielded (50), which the host answers through <see cref="IScrItemQueries"/>, and add effect (77) and remove effect (78), which hand
    /// <see cref="ScrEffectRequest"/> effects to the host, call-ex (81), which runs an object's attachment at once (its slot change comes
    /// back in <see cref="ScrEffects.SlotWrites"/>), the magick-tech adjustment (91), which stores a value scaled by an item's power ratio
    /// for its owner, and play script eye candy (120), a presentation effect.
    /// </summary>
    public static ScrAdmissionSet S14 { get; } = new("S14", S13.Conditions.Concat([ScrCatalogue.Condition.Identified, ScrCatalogue.Condition.ItemsBeingRewielded]),
        S13.Actions.Concat([ScrCatalogue.Action.AddEffect, ScrCatalogue.Action.RemoveEffect, ScrCatalogue.Action.CallScriptEx,
            ScrCatalogue.Action.MagickTechAdjustment, ScrCatalogue.Action.PlayScriptEyeCandy]), S13.ValueKinds, S13.FocusKinds);

    /// <summary>
    /// The world-object set (ARC-WORLD-081, the plant generator of template sectors, docs/research/template-sector-plants.md):
    /// <see cref="S14"/> plus random (37, PG5), loop for (18) and loop end (19, PG7), and create object (52, PG1), which hands one
    /// <see cref="ScrCreateRequest"/> to the host; the focus kinds "current looped object" (10) and "every scenery in vicinity" (15, PG3),
    /// which the host answers through <see cref="IScrWorldQueries"/>; and a counter operand with any index (<see cref="CounterIndexWraps"/>,
    /// PG6).
    /// </summary>
    public static ScrAdmissionSet S15 { get; } = new("S15", S14.Conditions,
        S14.Actions.Concat([ScrCatalogue.Action.Random, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd, ScrCatalogue.Action.CreateObject]), S14.ValueKinds,
        S14.FocusKinds.Concat([ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EverySceneryInVicinity]), counterIndexWraps: true);

    /// <summary>
    /// The object state set (ARC-WORLD-114): <see cref="S15"/> plus toggle state (56) and kill (58), which hand one
    /// <see cref="ScrObjectStateRequest"/> per object of their object operand to the host (ledger SU6, PM1).
    /// </summary>
    public static ScrAdmissionSet S16 { get; } = new("S16", S15.Conditions, S15.Actions.Concat([ScrCatalogue.Action.ToggleState, ScrCatalogue.Action.Kill]),
        S15.ValueKinds, S15.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The critter vicinity set (ARC-SCRIPT-047): <see cref="S16"/> plus the focus kinds everyone (8) and anyone (9) in the vicinity, the
    /// PCs and NPCs around the attachee (<see cref="IScrWorldQueries.CrittersInVicinity"/>, ledger TG4), in actions only (a condition
    /// over a list is not modelled). With call-ex of an NPC's dialog point (TG5) it runs the heartbeat that opens a talk (1100).
    /// </summary>
    public static ScrAdmissionSet S17 { get; } = new("S17", S16.Conditions, S16.Actions, S16.ValueKinds,
        S16.FocusKinds.Concat([ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity]), counterIndexWraps: true);

    /// <summary>
    /// The heartbeat world set (ARC-SCRIPT-049, ledger HW1–HW4): <see cref="S17"/> plus the conditions obj is dead (22) and obj is switched
    /// off (27), which the host answers through <see cref="IScrObjectStateQueries"/>, distance between (71), which stores the tile distance
    /// of two objects (<see cref="IScrWorldQueries.Location"/>), and set faction (134), which hands one <see cref="ScrObjectStateRequest"/>
    /// to the host.
    /// </summary>
    public static ScrAdmissionSet S18 { get; } = new("S18", S17.Conditions.Concat([ScrCatalogue.Condition.IsDead, ScrCatalogue.Condition.IsSwitchedOff]),
        S17.Actions.Concat([ScrCatalogue.Action.DistanceBetween, ScrCatalogue.Action.SetFaction]), S17.ValueKinds, S17.FocusKinds, counterIndexWraps: true);

    /// <summary>The conditions of <see cref="S18"/> a host answers through <see cref="IScrObjectStateQueries"/>.</summary>
    public static IReadOnlySet<int> ObjectStateQueryConditions { get; } = new HashSet<int> { ScrCatalogue.Condition.IsDead, ScrCatalogue.Condition.IsSwitchedOff };

    /// <summary>
    /// The talk set (ARC-SCRIPT-050, slice 2 of the script trigger audit): <see cref="S18"/> plus float line (23), which hands one
    /// <see cref="ScrFloatLine"/> per object of its object operand to the host after finding the PC in the game-window rectangle around the
    /// triggerer through <see cref="IScrWorldQueries.CrittersInVicinity"/> (ledger FL1–FL5), and get area (84), which stores the area the
    /// host answers through <see cref="IScrAreaQueries"/> (GA1–GA3).
    /// </summary>
    public static ScrAdmissionSet S19 { get; } = new("S19", S18.Conditions, S18.Actions.Concat([ScrCatalogue.Action.FloatLine, ScrCatalogue.Action.GetArea]),
        S18.ValueKinds, S18.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The tile-script actor set (ARC-SCRIPT-052, slice 8 of the script trigger audit, ledger TD1–TD3): <see cref="S19"/> plus destroy (68)
    /// and stop attacking (109), which hand one <see cref="ScrObjectStateRequest"/> for the first object of their operand to the host.
    /// </summary>
    public static ScrAdmissionSet S20 { get; } = new("S20", S19.Conditions, S19.Actions.Concat([ScrCatalogue.Action.Destroy, ScrCatalogue.Action.StopAttacking]),
        S19.ValueKinds, S19.FocusKinds, counterIndexWraps: true);

    /// <summary>The value kinds of <see cref="S21"/> a host answers through <see cref="IScrStoreQueries"/>.</summary>
    public static IReadOnlySet<ScrValueKind> StoreValueKinds { get; } = new HashSet<ScrValueKind>
        { ScrValueKind.GlobalVariable, ScrValueKind.PcVariable, ScrValueKind.PcFlag };

    /// <summary>
    /// The script store set (ARC-DEV-100, MIG8 slice 4, GR29, ledger GV1–GV4): <see cref="S20"/> plus the value kinds global variable (1), PC
    /// variable (5) and PC flag (6), which a host answers through <see cref="IScrStoreQueries"/> (the session's stores, SA13, SA14).
    /// </summary>
    public static ScrAdmissionSet S21 { get; } = new("S21", S20.Conditions, S20.Actions, S20.ValueKinds.Concat(StoreValueKinds), S20.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The stat set (ARC-DEV-157, bug B23, ledger GS1–GS4): <see cref="S21"/> plus get stat (33), which stores an object's effective stat
    /// (the stat getter 0x4B0490, intelligence read as 1 for a dumb background) and needs a host that implements
    /// <see cref="IScrStatQueries"/>. The commands of <see cref="S21"/> are the same; only tile scripts that read the triggerer's stat
    /// (608 refused rows in the audit) need it.
    /// </summary>
    public static ScrAdmissionSet S22 { get; } = new("S22", S21.Conditions, S21.Actions.Concat([ScrCatalogue.Action.GetStat]), S21.ValueKinds, S21.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The reaction set (ARC-DEV-178, bug B19, ledger GR1): <see cref="S22"/> plus get reaction (29), which stores the standard effective reaction
    /// (0x4C0CC0) of the first object of operand 0 toward the first object of operand 1 and needs a host that implements
    /// <see cref="IScrReactionQueries"/>. Dialog scripts that read the speaker's reaction (the talk set of the AREA viewer) need it.
    /// </summary>
    public static ScrAdmissionSet S23 { get; } = new("S23", S22.Conditions, S22.Actions.Concat([ScrCatalogue.Action.GetReaction]), S22.ValueKinds, S22.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The town-map set (ARC-DEV-205, bug B22, ledger UF1): <see cref="S23"/> plus unfog town map (113), which asks to reveal every cell of the
    /// town map its number operand names (<see cref="ScrEffects.TownMaps"/>) and needs a host that applies that request. The wreck's sector
    /// script (3036) and one use script need it (the census counts it in two scripts).
    /// </summary>
    public static ScrAdmissionSet S24 { get; } = new("S24", S23.Conditions, S23.Actions.Concat([ScrCatalogue.Action.UnfogTownMap]), S23.ValueKinds, S23.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The goal-starting set (ARC-DEV-231, MIG13 S14a, GT154–GT161): <see cref="S24"/> plus walk to (69) and run to (74), which hand one
    /// <see cref="ScrObjectStateRequest"/> (the first object of operand 0 and the tile of operands 1 and 2) to the host; the script goes on with
    /// the next line whatever the host does with it (fire and forget, GT154).
    /// </summary>
    public static ScrAdmissionSet S25 { get; } = new("S25", S24.Conditions, S24.Actions.Concat([ScrCatalogue.Action.WalkTo, ScrCatalogue.Action.RunTo]), S24.ValueKinds, S24.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The fatigue set (ARC-DEV-233, MIG13 S14b, GT162): <see cref="S25"/> plus perform animation (62) of the first object and heal fatigue (76), which hand one <see cref="ScrObjectStateRequest"/> per object of its operand (in the list's order) with the
    /// amount to the host.
    /// </summary>
    public static ScrAdmissionSet S26 { get; } = new("S26", S25.Conditions, S25.Actions.Concat([ScrCatalogue.Action.HealFatigue, ScrCatalogue.Action.PerformAnimation]), S25.ValueKinds, S25.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The steal set (ARC-DEV-234, MIG13 S14c, GT164): <see cref="S26"/> plus try to steal 100 coins (115), which hands one <see cref="ScrObjectStateRequest"/> with the thief (the first object of operand 0) and the
    /// victim (the first of operand 1) to the host.
    /// </summary>
    public static ScrAdmissionSet S27 { get; } = new("S27", S26.Conditions, S26.Actions.Concat([ScrCatalogue.Action.StealCoins]), S26.ValueKinds, S26.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The prone set (ARC-DEV-236, MIG13 S14e, GT187): <see cref="S27"/> plus become prone (93), which hands one <see cref="ScrObjectStateRequest"/> per object of its operand (in the list's order) to the host.
    /// </summary>
    public static ScrAdmissionSet S28 { get; } = new("S28", S27.Conditions, S27.Actions.Concat([ScrCatalogue.Action.BecomeProne]), S27.ValueKinds, S27.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The cast set (ARC-DEV-241, MIG13 S14f, GT183/GT184): <see cref="S28"/> plus the six cast actions (48, 61, 118, 121, 122, 136), which hand one <see cref="ScrObjectStateRequest"/> (<see cref="ScrObjectStateChange.CastSpell"/>)
    /// to the host.
    /// </summary>
    public static ScrAdmissionSet S29 { get; } = new("S29", S28.Conditions,
        S28.Actions.Concat([ScrCatalogue.Action.CastSpell, ScrCatalogue.Action.CastSpellOn, ScrCatalogue.Action.CastFreeSpell, ScrCatalogue.Action.CastUnresistableSpellBy,
            ScrCatalogue.Action.CastFreeUnresistableSpell, ScrCatalogue.Action.CastUnresistableSpellOn]), S28.ValueKinds, S28.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The damage set (ARC-DEV-243, MIG13 S14g, GT194–GT197): <see cref="S29"/> plus damage (60), apply unresistable damage (138) and heal hit points (75), which hand one <see cref="ScrObjectStateRequest"/> per object of their
    /// operand (in the list's order) to the host.
    /// </summary>
    public static ScrAdmissionSet S30 { get; } = new("S30", S29.Conditions,
        S29.Actions.Concat([ScrCatalogue.Action.Damage, ScrCatalogue.Action.DamageUnresistable, ScrCatalogue.Action.HealHitPoints]), S29.ValueKinds, S29.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The spell condition set (ARC-MAGIC-029, magic plan slice MT21a, ledger SC1–SC6): <see cref="S30"/> plus the conditions obj has body spell (14), obj is influenced by spell (35), knows spell (48) and mastered spell
    /// college (49), which a host answers through <see cref="IScrSpellQueries"/>, and give spell mastery in college (112), which hands one <see cref="ScrObjectStateRequest"/> (<see cref="ScrObjectStateChange.GiveSpellMastery"/>) to the host.
    /// </summary>
    public static ScrAdmissionSet S31 { get; } = new("S31", S30.Conditions.Concat([ScrCatalogue.Condition.HasBodySpell, ScrCatalogue.Condition.InfluencedBySpell, ScrCatalogue.Condition.KnowsSpell,
            ScrCatalogue.Condition.MasteredSpellCollege]),
        S30.Actions.Append(ScrCatalogue.Action.GiveSpellMastery), S30.ValueKinds, S30.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The ammunition set (ARC-MAGIC-041, ledger TI8): <see cref="S31"/> plus use ammunition units (135), which hands one <see cref="ScrAmmoUse"/> (the count of operand 0, the power-cell type 2, the
    /// first object of operand 1) to the host; the script goes on whatever the host does with it. The Electric degree-3 shield's script (29016) needs it.
    /// </summary>
    public static ScrAdmissionSet S32 { get; } = new("S32", S31.Conditions, S31.Actions.Append(ScrCatalogue.Action.UseAmmoUnits), S31.ValueKinds, S31.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The thrown creature item set (ARC-MAGIC-042, technology queue slice T-m1, ledger TI4–TI6): <see cref="S32"/> plus transform into a basic prototype (40), force follow (21) and attack (36), which hand one
    /// <see cref="ScrObjectStateRequest"/> each (<see cref="ScrObjectStateChange.Transform"/>, <see cref="ScrObjectStateChange.MakeFollow"/>, <see cref="ScrObjectStateChange.Attack"/>) to the host in action order.
    /// The interpreter keeps the run's attachee as the object the event named: the host applies the requests in order and reads the attachee in a later request as the object a transform made (the original makes the new object the run's attachee).
    /// </summary>
    public static ScrAdmissionSet S33 { get; } = new("S33", S32.Conditions,
        S32.Actions.Concat([ScrCatalogue.Action.TransformObject, ScrCatalogue.Action.MakeFollow, ScrCatalogue.Action.Attack]), S32.ValueKinds, S32.FocusKinds, counterIndexWraps: true);

    /// <summary>The conditions <see cref="S34"/> adds to <see cref="S33"/>: daytime (1), global quest state (7), obj has blessing (8), obj has curse (9), rumor known (40), rumor quelled (41); a host with its own set adds them with
    /// <see cref="StoryActions"/> and implements <see cref="IScrStoryQueries"/>.</summary>
    public static IReadOnlyList<int> StoryConditions { get; } = [ScrCatalogue.Condition.Daytime, ScrCatalogue.Condition.GlobalQuestState, ScrCatalogue.Condition.HasBlessing, ScrCatalogue.Condition.HasCurse,
        ScrCatalogue.Condition.RumorKnown, ScrCatalogue.Condition.RumorQuelled];

    /// <summary>The actions <see cref="S34"/> adds to <see cref="S33"/>: set quest global state (17), add and remove blessing (25, 26), add and remove curse (27, 28), quell rumor (51), give quest XP (63), the four clock actions
    /// (96, 97, 98, 105), grant one fate point (117) and set PC quest unbotched (119).</summary>
    public static IReadOnlyList<int> StoryActions { get; } = [ScrCatalogue.Action.SetGlobalQuestState, ScrCatalogue.Action.AddBlessing, ScrCatalogue.Action.RemoveBlessing, ScrCatalogue.Action.AddCurse,
        ScrCatalogue.Action.RemoveCurse, ScrCatalogue.Action.QuellRumor, ScrCatalogue.Action.GiveQuestExperience, ScrCatalogue.Action.GetDaySinceStartup, ScrCatalogue.Action.GetCurrentHour,
        ScrCatalogue.Action.GetCurrentMinute, ScrCatalogue.Action.GetHoursSinceStartup, ScrCatalogue.Action.GrantFatePoint, ScrCatalogue.Action.SetPcQuestUnbotched];

    /// <summary>
    /// The story and time set (ARC-SCRIPT-1165, plan slice E5-c, ledger SQ1–SQ12): <see cref="S33"/> plus the conditions daytime (1), global quest state (7), obj has blessing (8), obj has curse (9), rumor known (40) and
    /// rumor quelled (41), which a host answers through <see cref="IScrStoryQueries"/>, the clock actions get day since startup (96), get current hour (97), get current minute (98) and get hours since startup (105), which store
    /// into their number operand from <see cref="IScrStoryQueries.Time"/>, and the story actions set quest global state (17), add and remove blessing (25, 26), add and remove curse (27, 28), quell rumor (51), give quest XP (63), grant one
    /// fate point (117) and set PC quest unbotched (119), which hand <see cref="ScrStateRequest"/> effects to the host. The run reads its own changes to the global quest states, the quelled rumors, blessings and curses back.
    /// </summary>
    public static ScrAdmissionSet S34 { get; } = new("S34", S33.Conditions.Concat(StoryConditions), S33.Actions.Concat(StoryActions), S33.ValueKinds, S33.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The fade set (ARC-SCRIPT-1241, <c>docs/research/bink-movies.md</c> MV5): <see cref="S34"/> plus fade (103), which hands one <see cref="ScrFade"/> (seconds, sound, movie, flag) to the host in action order;
    /// the script goes on whatever the host does with it. The host jumps the game clock by the seconds, plays the movie or the sound and brings the screen back. Six installed scripts carry a movie in it
    /// (1093, 1105, 1109, 2862, 2863, 3061).
    /// </summary>
    public static ScrAdmissionSet S35 { get; } = new("S35", S34.Conditions, S34.Actions.Append(ScrCatalogue.Action.Fade), S34.ValueKinds, S34.FocusKinds, counterIndexWraps: true);

    /// <summary>The actions <see cref="S36"/> adds to <see cref="S35"/>: critter disband (22), set and adjust reaction (30, 31), set day and night standpoint (45, 46) and their extended forms (140, 141), change art number (59) and change script (99).
    /// A host with its own set adds the ones it applies (<see cref="NpcStateHostActions"/> are the ones the area hosts apply).</summary>
    public static IReadOnlyList<int> NpcStateActions { get; } = [ScrCatalogue.Action.CritterDisband, ScrCatalogue.Action.SetReaction, ScrCatalogue.Action.AdjustReaction, ScrCatalogue.Action.SetDayStandpoint,
        ScrCatalogue.Action.SetNightStandpoint, ScrCatalogue.Action.SetDayStandpointEx, ScrCatalogue.Action.SetNightStandpointEx, ScrCatalogue.Action.ChangeArtNumber, ScrCatalogue.Action.ChangeScript];

    /// <summary>The actions of <see cref="NpcStateActions"/> the area hosts apply (all but change art number 59, which needs a per-object art overlay the viewer does not have).</summary>
    public static IReadOnlyList<int> NpcStateHostActions { get; } = [.. NpcStateActions.Where(a => a != ScrCatalogue.Action.ChangeArtNumber)];

    /// <summary>
    /// The NPC state set (ARC-SCRIPT-1163, plan slice E5-a, ledger NS1–NS8): <see cref="S35"/> plus critter disband (22), set reaction (30), adjust reaction (31), set day standpoint (45), set night standpoint (46), change art number (59), change script (99) and the extended
    /// standpoints (140, 141). Disband, the reactions, the standpoints and change art number hand <see cref="ScrObjectStateRequest"/>s to the host in action order; change script (99) changes the slot of an attachment point of any object
    /// (<see cref="ScrEffects.SlotWrites"/>, or the run's own result when it names its own slot).
    /// </summary>
    public static ScrAdmissionSet S36 { get; } = new("S36", S35.Conditions, S35.Actions.Concat(NpcStateActions), S35.ValueKinds, S35.FocusKinds, counterIndexWraps: true);

    /// <summary>The conditions <see cref="S37"/> adds to <see cref="S36"/>: obj is open (36), sector is blocked (45) and monster generator disabled (46); a host with its own set adds them and implements <see cref="IScrWorldObjectQueries"/>.</summary>
    public static IReadOnlyList<int> WorldObjectConditions { get; } = [ScrCatalogue.Condition.ObjIsOpen, ScrCatalogue.Condition.SectorIsBlocked, ScrCatalogue.Condition.MonsterGenDisabled];

    /// <summary>The actions <see cref="S37"/> adds to <see cref="S36"/>: transfer item (41), set lock state (53), create item (66), toggle open or closed (88), set written start (94), toggle sector blocked (106), toggle monster generator (110), touch art (123),
    /// destroy item named (126) and toggle item inventory display (127).</summary>
    public static IReadOnlyList<int> WorldObjectActions { get; } = [ScrCatalogue.Action.TransferItem, ScrCatalogue.Action.SetLockState, ScrCatalogue.Action.CreateItem, ScrCatalogue.Action.ToggleOpenClosed, ScrCatalogue.Action.SetWrittenStart,
        ScrCatalogue.Action.ToggleSectorBlocked, ScrCatalogue.Action.ToggleMonsterGenerator, ScrCatalogue.Action.TouchArt, ScrCatalogue.Action.DestroyItemNamed, ScrCatalogue.Action.ToggleItemInventoryDisplay];

    /// <summary>The actions of <see cref="WorldObjectActions"/> the area hosts apply: every one of them. ARC-SCRIPT-1164 left out transfer item (41), set written start (94), destroy item named (126) and toggle item inventory display (127) for lack of
    /// a per-item field write and removal; ARC-SCRIPT-1279 added the session's named-item move and destroy and its item field writes, and the hosts apply them (ledger WO14, WO16).</summary>
    public static IReadOnlyList<int> WorldObjectHostActions { get; } = [ScrCatalogue.Action.TransferItem, ScrCatalogue.Action.SetLockState, ScrCatalogue.Action.CreateItem, ScrCatalogue.Action.ToggleOpenClosed, ScrCatalogue.Action.SetWrittenStart,
        ScrCatalogue.Action.ToggleSectorBlocked, ScrCatalogue.Action.ToggleMonsterGenerator, ScrCatalogue.Action.TouchArt, ScrCatalogue.Action.DestroyItemNamed, ScrCatalogue.Action.ToggleItemInventoryDisplay];

    /// <summary>
    /// The world-object set (ARC-SCRIPT-1164, plan slice E5-b, ledger WO1–WO10): <see cref="S36"/> plus the conditions obj is open (36), sector is blocked (45) and monster generator disabled (46), which a host answers through
    /// <see cref="IScrWorldObjectQueries"/>, and the actions transfer item (41), set lock state (53), create item (66), toggle open or closed (88), set written start (94), toggle sector blocked (106), toggle monster generator (110), touch art (123),
    /// destroy item named (126) and toggle item inventory display (127). The object actions hand <see cref="ScrObjectStateRequest"/>s and the sector and generator toggles <see cref="ScrWorldRequest"/>s to the host in action order; touch art
    /// reads its operand and hands nothing. The run reads its own toggles of the sectors, the generators and the open states back.
    /// </summary>
    public static ScrAdmissionSet S37 { get; } = new("S37", S36.Conditions.Concat(WorldObjectConditions), S36.Actions.Concat(WorldObjectActions), S36.ValueKinds, S36.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The get-location set (ARC-SCRIPT-1359, ledger GL1–GL2): <see cref="S37"/> plus get location (95), which writes the first object's tile x and y into its number operands 1 and 2 through <see cref="IScrWorldQueries.Location"/>
    /// (the Crash Site Molochean agent's heartbeat 1717 reads where it stands).
    /// </summary>
    public static ScrAdmissionSet S38 { get; } = new("S38", S37.Conditions, S37.Actions.Append(ScrCatalogue.Action.GetLocation), S37.ValueKinds, S37.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The loop-break set (ARC-SCRIPT-1166, plan slice E5-e, ledger PG15): <see cref="S38"/> plus loop break (20), which ends the run's running loop at once and goes on after the first loop end following the loop for (the same
    /// search an empty list does); without a running loop it does nothing. It needs no host answer of its own. Of the 22 installed scripts that carry it, 17 are reached by a live run (68 placed sites, 65 on the main map; census ARC-RES-1101). S39 is left to another task. Call script at (55) is not part of the slice:
    /// it stands on one line no run reaches.
    /// </summary>
    public static ScrAdmissionSet S40 { get; } = new("S40", S38.Conditions, S38.Actions.Append(ScrCatalogue.Action.LoopBreak), S38.ValueKinds, S38.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The spell-stop set (ARC-SCRIPT-1294, ledger RM6): <see cref="S40"/> plus stop spell on object (130, 0x4593F0), which hands the spell number and the first object of its operand to the host; the host finds the first active
    /// run of that spell that holds the object (0x459290) and interrupts it (0x457110). It needs no answer: the run goes on whether a run was found or not. Four installed scripts carry it (1054, 2789, 2792, 3022).
    /// </summary>
    public static ScrAdmissionSet S42 { get; } = new("S42", S40.Conditions, S40.Actions.Append(ScrCatalogue.Action.StopSpellOnObject), S40.ValueKinds, S40.FocusKinds, counterIndexWraps: true);

    /// The vitals set (ARC-SCRIPT-1481, ledger VT1–VT4): <see cref="S42"/> plus get hit points (107) and get fatigue points (108), which store an object's current and maximum points into their number operands 1 and 2 through
    /// <see cref="IScrVitalQueries"/>, and heal poison (128), which lowers the poison level of every object of its operand (the food use script 30282 reads and heals the eater's vitals and poison).
    /// </summary>
    public static ScrAdmissionSet S43 { get; } = new("S43", S42.Conditions, S42.Actions.Concat([ScrCatalogue.Action.GetHitPoints, ScrCatalogue.Action.GetFatiguePoints, ScrCatalogue.Action.HealPoison]),
        S42.ValueKinds, S42.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The combat-point set (ARC-SCRIPT-1562, ledger CP7–CP9): <see cref="S43"/> plus the condition obj is animal (37, <see cref="IScrCritterQueries"/>), get magick-tech adjustment (80, the cancel chance of an attack's item, attacker and target, <see cref="IScrMagickTechQueries"/>), the magick-tech
    /// adjustment (91, an item's power ratio for its wielder, <see cref="IScrItemQueries"/>) and play script eye candy (120, a presentation effect): the commands the installed poison weapons' hit and critical-hit scripts (30032, 30076,
    /// 30199) and the hunting weapons' (28007) need. The shown map's use host runs the combat points under the placed set plus the three actions and the condition.
    /// </summary>
    public static ScrAdmissionSet S51 { get; } = new("S51", S43.Conditions.Append(ScrCatalogue.Condition.IsAnimal), S43.Actions.Concat([ScrCatalogue.Action.GetMagickTechAdjustment, ScrCatalogue.Action.MagickTechAdjustment, ScrCatalogue.Action.PlayScriptEyeCandy]),
        S43.ValueKinds, S43.FocusKinds, counterIndexWraps: true);

    /// <summary>
    /// The combat-point tail set (ARC-SCRIPT-1661, ledger CP11–CP12): <see cref="S51"/> plus the condition obj is in combat (31, 0x4451B0 case 0x1f: the critter's combat-mode bit, <see cref="IScrCritterQueries"/>) and adjust stat (137, 0x447220 case 0x89: the base stat plus a
    /// number through the stat setter 0x4B0980, <see cref="IScrStatAdjustQueries"/>): the commands the area weapon scripts 30064 and 30074 (a spell on every critter around the wielder that is in combat) and the alignment weapon 30252 (the wielder's alignment falls by 200) still stopped at.
    /// </summary>
    public static ScrAdmissionSet S52 { get; } = new("S52", S51.Conditions.Append(ScrCatalogue.Condition.IsInCombat), S51.Actions.Append(ScrCatalogue.Action.AdjustStat),
        S51.ValueKinds, S51.FocusKinds, counterIndexWraps: true);

    /// <summary>The critter-state conditions the set <see cref="S53"/> adds to <see cref="S52"/> (ARC-SCRIPT-1733, ledger CS1–CS12), which a host answers through <see cref="IScrCritterStateQueries"/>: obj met PC before (10), obj is invisible (15), obj has surrendered (25), obj is in dialog (26), obj can see obj (28), obj can hear obj (29), obj is invulnerable (30), obj is undead (38) and prowling (51).</summary>
    public static IReadOnlyList<int> CritterStateConditions { get; } =
    [
        ScrCatalogue.Condition.MetPcBefore, ScrCatalogue.Condition.IsInvisible, ScrCatalogue.Condition.IsSurrendered, ScrCatalogue.Condition.IsInDialog, ScrCatalogue.Condition.CanSeeObject, ScrCatalogue.Condition.CanHearObject,
        ScrCatalogue.Condition.IsInvulnerable, ScrCatalogue.Condition.IsUndead, ScrCatalogue.Condition.IsProwling,
    ];

    /// <summary>
    /// The critter-state set (ARC-SCRIPT-1733, ledger CS1–CS12; the script census group CRQ, ARC-RES-1101): <see cref="S52"/> plus the nine critter-state and social conditions of <see cref="CritterStateConditions"/>, each a read of a field or a call the session and the AI host already hold
    /// (exe 0x4451B0 cases 10, 15, 25, 26, 28, 29, 30, 38 and 51). Obj is animal (37) and obj is in combat (31) were in the combat-point sets only; this set gives them to every host that answers them.
    /// </summary>
    public static ScrAdmissionSet S53 { get; } = new("S53", S52.Conditions.Concat(CritterStateConditions), S52.Actions, S52.ValueKinds, S52.FocusKinds, counterIndexWraps: true);

    /// <summary>The conditions the set <see cref="S54"/> adds to <see cref="S53"/> (ARC-SCRIPT-1771, ledger CS12–CS16): obj has bad associates (11), obj is monster of type (19) and obj is within range (34).</summary>
    public static IReadOnlyList<int> CritterTailConditions { get; } = [ScrCatalogue.Condition.BadAssociates, ScrCatalogue.Condition.MonsterOfType, ScrCatalogue.Condition.WithinRange];

    /// <summary>The conditions of <see cref="CritterTailConditions"/> a host answers through <see cref="IScrCritterStateQueries"/> (obj is within range reads only the object's location, through <see cref="IScrWorldQueries"/>).</summary>
    public static IReadOnlySet<int> CritterTailQueryConditions { get; } = new HashSet<int> { ScrCatalogue.Condition.BadAssociates, ScrCatalogue.Condition.MonsterOfType };

    /// <summary>
    /// The critter-state tail set (ARC-SCRIPT-1771, ledger CS12–CS16; the script census group CRQ, ARC-RES-1101): <see cref="S53"/> plus obj has bad associates (11, exe 0x4451B0 case 11 → 0x45F460), obj is monster of type (19, case 19: the art id of object field 0x18) and
    /// obj is within range (34, case 34 → 0x4B96F0).
    /// </summary>
    public static ScrAdmissionSet S54 { get; } = new("S54", S53.Conditions.Concat(CritterTailConditions), S53.Actions, S53.ValueKinds, S53.FocusKinds, counterIndexWraps: true);

    /// <summary>The conditions of <see cref="S51"/> and <see cref="S52"/> a host answers through <see cref="IScrCritterQueries"/>.</summary>
    public static IReadOnlySet<int> CritterQueryConditions { get; } = new HashSet<int> { ScrCatalogue.Condition.IsAnimal, ScrCatalogue.Condition.IsInCombat };

    /// <summary>The actions of <see cref="S43"/> a host answers through <see cref="IScrVitalQueries"/>.</summary>
    public static IReadOnlySet<int> VitalQueryActions { get; } = new HashSet<int> { ScrCatalogue.Action.GetHitPoints, ScrCatalogue.Action.GetFatiguePoints };

    /// <summary>The conditions of <see cref="S37"/> a host answers through <see cref="IScrWorldObjectQueries"/>.</summary>
    public static IReadOnlySet<int> WorldObjectQueryConditions { get; } = new HashSet<int>(WorldObjectConditions);

    /// <summary>The actions of <see cref="S37"/> a host answers through <see cref="IScrWorldObjectQueries"/> first: the sector and generator toggles read the present state, the open toggle reads whether the object can open.</summary>
    public static IReadOnlySet<int> WorldObjectQueryActions { get; } = new HashSet<int> { ScrCatalogue.Action.ToggleSectorBlocked, ScrCatalogue.Action.ToggleMonsterGenerator, ScrCatalogue.Action.ToggleOpenClosed };

    /// <summary>The conditions of <see cref="S34"/> a host answers through <see cref="IScrStoryQueries"/>.</summary>
    public static IReadOnlySet<int> StoryQueryConditions { get; } = new HashSet<int> { ScrCatalogue.Condition.Daytime, ScrCatalogue.Condition.GlobalQuestState, ScrCatalogue.Condition.HasBlessing,
        ScrCatalogue.Condition.HasCurse, ScrCatalogue.Condition.RumorKnown, ScrCatalogue.Condition.RumorQuelled };

    /// <summary>The actions of <see cref="S34"/> a host answers through <see cref="IScrStoryQueries"/>: the four clock actions and the two that read the game-wide quest state first (set global quest state, unbotch).</summary>
    public static IReadOnlySet<int> StoryQueryActions { get; } = new HashSet<int> { ScrCatalogue.Action.GetDaySinceStartup, ScrCatalogue.Action.GetCurrentHour, ScrCatalogue.Action.GetCurrentMinute,
        ScrCatalogue.Action.GetHoursSinceStartup, ScrCatalogue.Action.SetGlobalQuestState, ScrCatalogue.Action.SetPcQuestUnbotched };

    /// <summary>The conditions of <see cref="S31"/> a host answers through <see cref="IScrSpellQueries"/>.</summary>
    public static IReadOnlySet<int> SpellQueryConditions { get; } = new HashSet<int> { ScrCatalogue.Condition.HasBodySpell, ScrCatalogue.Condition.InfluencedBySpell, ScrCatalogue.Condition.KnowsSpell,
        ScrCatalogue.Condition.MasteredSpellCollege };

    /// <summary>The actions of <see cref="S15"/>, <see cref="S18"/> and <see cref="S19"/> a host answers through <see cref="IScrWorldQueries"/>.</summary>
    public static IReadOnlySet<int> WorldQueryActions { get; } = new HashSet<int>
        { ScrCatalogue.Action.Random, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.DistanceBetween, ScrCatalogue.Action.FloatLine, ScrCatalogue.Action.GetLocation };

    /// <summary>The focus kinds of <see cref="S15"/> a host answers through <see cref="IScrWorldQueries"/>.</summary>
    public static IReadOnlySet<ScrFocusKind> WorldQueryFocusKinds { get; } = new HashSet<ScrFocusKind>
        { ScrFocusKind.EverySceneryInVicinity, ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity };

    /// <summary>The conditions of <see cref="S14"/> a host answers through <see cref="IScrItemQueries"/>.</summary>
    public static IReadOnlySet<int> ItemQueryConditions { get; } = new HashSet<int> { ScrCatalogue.Condition.Identified, ScrCatalogue.Condition.ItemsBeingRewielded };

    /// <summary>The conditions of <see cref="S4"/> a host answers through <see cref="IScrObjectQueries"/>.</summary>
    public static IReadOnlySet<int> ObjectQueryConditions { get; } = new HashSet<int> { ScrCatalogue.Condition.PcQuestState, ScrCatalogue.Condition.HasItemNamed,
        ScrCatalogue.Condition.IsNamed, ScrCatalogue.Condition.IsWieldingItem, ScrCatalogue.Condition.HasReputation };

    /// <summary>The actions of <see cref="S11"/> a host answers through <see cref="IScrObjectQueries"/>.</summary>
    public static IReadOnlySet<int> ObjectQueryActions { get; } = new HashSet<int> { ScrCatalogue.Action.GetObjectType };

    /// <summary>Whether every type and kind of this set is also in <paramref name="other"/>.</summary>
    public bool IsSubsetOf(ScrAdmissionSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Conditions.All(other.Conditions.Contains) && Actions.All(other.Actions.Contains) && ValueKinds.All(other.ValueKinds.Contains) && FocusKinds.All(other.FocusKinds.Contains)
            && (!CounterIndexWraps || other.CounterIndexWraps);
    }
}

/// <summary>A script admitted as a whole for one set: every clause it could run is inside it.
/// Only <see cref="ScrAdmission.Admit"/> creates one, so holding it is the proof of admission.</summary>
public sealed class ScrAdmittedProgram
{
    internal ScrAdmittedProgram(ScrProgram program, ScrAdmissionSet set, int? entryLine = null, IReadOnlyList<int>? region = null)
    {
        Program = program; Set = set; EntryLine = entryLine; Region = region ?? Array.AsReadOnly(Enumerable.Range(0, program.Count).ToArray());
    }

    public ScrProgram Program { get; }
    public ScrAdmissionSet Set { get; }
    /// <summary>The line a region admission started from, or null for the whole script.</summary>
    public int? EntryLine { get; }
    /// <summary>The admitted lines in ascending order: every line, or the lines a run from <see cref="EntryLine"/> can reach.</summary>
    public IReadOnlyList<int> Region { get; }
}

/// <summary>Exactly one of <see cref="Admitted"/> and <see cref="Refusal"/>.</summary>
public sealed class ScrAdmissionResult
{
    private ScrAdmissionResult(ScrAdmittedProgram? admitted, ScrRefusal? refusal) { Admitted = admitted; Refusal = refusal; }
    internal static ScrAdmissionResult Of(ScrAdmittedProgram admitted) => new(admitted, null);
    internal static ScrAdmissionResult Of(ScrRefusal refusal) => new(null, refusal);
    public ScrAdmittedProgram? Admitted { get; }
    public ScrRefusal? Refusal { get; }
    public bool IsAdmitted => Admitted is not null;
}

/// <summary>Static whole-script admission: a script runs only if every line can run under the
/// set, so no run ever stops half-way on something unsupported. It checks every line, not only
/// those an entry reaches, and reports the first refusal in line order: the condition, then the
/// true action, then the false action; within a clause its type, then its operands left to right
/// (slot 0's index, write and jump rules right after its kind).</summary>
public static class ScrAdmission
{
    public static ScrAdmissionResult Admit(ScrProgram program, ScrAdmissionSet set)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(set);
        foreach (var line in program.Lines)
        {
            var refusal = Check(line, ScrClauseSide.Condition, line.Condition, set, program.Count)
                ?? Check(line, ScrClauseSide.OnTrue, line.OnTrue, set, program.Count)
                // The always-true condition never runs its false action, so its bytes are not read.
                ?? (line.Condition.Type == ScrCatalogue.Condition.True ? null : Check(line, ScrClauseSide.OnFalse, line.OnFalse, set, program.Count));
            if (refusal is not null) return ScrAdmissionResult.Of(refusal);
        }
        return ScrAdmissionResult.Of(new ScrAdmittedProgram(program, set));
    }

    /// <summary>
    /// Region admission (ARC-SCRIPT-009): the lines a run that starts at <paramref name="entryLine"/> can reach, found
    /// by following each admitted action (a literal goto, the next line, or the end of the run for a return or a
    /// dialog action), all run under the set. The run of an admitted region can only reach lines inside it, so it still
    /// never stops half-way on something unsupported; lines outside it are never read. The refusal reported is the
    /// lowest-numbered refused line among those reached through admitted lines (a refused line's successors are not
    /// explored), with the clause order of <see cref="Admit"/>. An entry outside the script admits an empty region
    /// (such a run ends on an unreadable line).
    /// </summary>
    public static ScrAdmissionResult AdmitFrom(ScrProgram program, ScrAdmissionSet set, int entryLine)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(set);
        var count = program.Count;
        var region = new SortedSet<int>();
        var work = new Stack<int>();
        if (entryLine >= 0 && entryLine < count) { region.Add(entryLine); work.Push(entryLine); }
        var refusals = new List<ScrRefusal>();
        while (work.TryPop(out var at))
        {
            var line = program.Lines[at];
            var always = line.Condition.Type == ScrCatalogue.Condition.True;
            var refusal = Check(line, ScrClauseSide.Condition, line.Condition, set, count)
                ?? Check(line, ScrClauseSide.OnTrue, line.OnTrue, set, count)
                ?? (always ? null : Check(line, ScrClauseSide.OnFalse, line.OnFalse, set, count));
            if (refusal is not null) { refusals.Add(refusal); continue; }
            foreach (var action in always ? new[] { line.OnTrue } : [line.OnTrue, line.OnFalse])
                foreach (var next in Successors(program, action, at))
                    if (region.Add(next)) work.Push(next);
        }
        if (refusals.Count > 0) return ScrAdmissionResult.Of(refusals.OrderBy(r => r.Line).First());
        return ScrAdmissionResult.Of(new ScrAdmittedProgram(program, set, entryLine, Array.AsReadOnly(region.ToArray())));
    }

    // Successor lines of an admitted action; a line at the count or past it is the end of the run, not a line.
    private static IEnumerable<int> Successors(ScrProgram program, ScrClause action, int line)
    {
        var count = program.Count;
        IEnumerable<int> lines = action.Type switch
        {
            ScrCatalogue.Action.ReturnSkipDefault or ScrCatalogue.Action.ReturnRunDefault or ScrCatalogue.Action.Dialog => [],
            ScrCatalogue.Action.Goto => [action.Value(0)],
            // PG7: a loop for goes on with the next line, or with an empty list after its loop end.
            ScrCatalogue.Action.LoopFor => [line + 1, AfterLoopEnd(program, line)],
            // A loop end goes on with the next line, or jumps back to the line after the loop for that filled the list: any loop for's.
            ScrCatalogue.Action.LoopEnd => program.Lines.Where(l => IsLoopFor(l.OnTrue) || l.Condition.Type != ScrCatalogue.Condition.True && IsLoopFor(l.OnFalse))
                .Select(l => l.Number + 1).Prepend(line + 1),
            // PG15: a loop break goes on with the next line when no loop runs, or after the first loop end following the running loop's loop for: any loop for's.
            ScrCatalogue.Action.LoopBreak => program.Lines.Where(l => IsLoopFor(l.OnTrue) || l.Condition.Type != ScrCatalogue.Condition.True && IsLoopFor(l.OnFalse))
                .Select(l => AfterLoopEnd(program, l.Number)).Prepend(line + 1),
            _ => [line + 1],
        };
        return lines.Where(next => next < count);

        static bool IsLoopFor(ScrClause clause) => clause.Type == ScrCatalogue.Action.LoopFor;
    }

    /// <summary>
    /// PG7 (0x44BC60): where a loop for with an empty list goes on: the line after the first line after it whose condition is "true" and
    /// whose true action is loop end; without one, the line after the line count (the original's scan stops at the first line it cannot
    /// read and returns the line after it), which ends the run as a line outside the script.
    /// </summary>
    [OriginalFunction(0x44BC60, OriginalFunctionScope.Full)]
    internal static int AfterLoopEnd(ScrProgram program, int loopFor)
    {
        for (var at = loopFor + 1; at < program.Count; at++)
        {
            var row = program.Lines[at];
            if (row.Condition.Type == ScrCatalogue.Condition.True && row.OnTrue.Type == ScrCatalogue.Action.LoopEnd) return at + 1;
        }
        return program.Count + 1;
    }

    private static ScrRefusal? Check(ScrLine line, ScrClauseSide side, ScrClause clause, ScrAdmissionSet set, int count)
    {
        var condition = side == ScrClauseSide.Condition;
        var type = clause.Type;
        if (condition ? !ScrCatalogue.IsKnownCondition(type) : !ScrCatalogue.IsKnownAction(type))
            return new(line.Number, side, condition ? ScrRefusalCode.UnknownCondition : ScrRefusalCode.UnknownAction, type);
        if (!(condition ? set.Conditions : set.Actions).Contains(type))
            return new(line.Number, side, condition ? ScrRefusalCode.ConditionNotAdmitted : ScrRefusalCode.ActionNotAdmitted, type);
        var signature = ScrCatalogue.Signature(side, type)!;
        for (var slot = 0; slot < ScrClause.Operands; slot++)
        {
            var kind = clause.Kind(slot); var value = clause.Value(slot);
            ScrRefusal Refuse(ScrRefusalCode code, bool withValue = false) => new(line.Number, side, code, type, slot, kind, withValue ? value : null);
            switch (signature[slot])
            {
                case ScrOperandRole.Number:
                    if (!ScrCatalogue.IsKnownValueKind(kind)) return Refuse(ScrRefusalCode.UnknownValueKind);
                    if (!set.ValueKinds.Contains((ScrValueKind)kind)) return Refuse(ScrRefusalCode.ValueKindNotAdmitted);
                    if (!(set.CounterIndexWraps && (ScrValueKind)kind == ScrValueKind.Counter) && !InStore((ScrValueKind)kind, value))
                        return Refuse(ScrRefusalCode.IndexOutOfRange, true);
                    // ARC-DEV-100 (GV3, GV4): a PC variable or flag operand packs a focus kind into its high 16 bits (the object the store belongs
                    // to, the first of the kind); the kind must be one the set admits. The low 16 bits are the index, any of the 65,536.
                    if ((ScrValueKind)kind is ScrValueKind.PcVariable or ScrValueKind.PcFlag)
                    {
                        var focus = value >> 16;
                        if (focus is < 0 or > byte.MaxValue || !ScrCatalogue.IsKnownFocusKind((byte)focus)) return Refuse(ScrRefusalCode.UnknownFocusKind, true);
                        if (!set.FocusKinds.Contains((ScrFocusKind)focus)) return Refuse(ScrRefusalCode.FocusKindNotAdmitted, true);
                    }
                    break;
                case ScrOperandRole.Object:
                    if (!ScrCatalogue.IsKnownFocusKind(kind)) return Refuse(ScrRefusalCode.UnknownFocusKind);
                    if (!set.FocusKinds.Contains((ScrFocusKind)kind)) return Refuse(ScrRefusalCode.FocusKindNotAdmitted);
                    if ((ScrFocusKind)kind == ScrFocusKind.LocalObject && value is < 0 or >= ScrCatalogue.LocalSlots) return Refuse(ScrRefusalCode.IndexOutOfRange, true);
                    // ARC-SCRIPT-047: a condition tests every object of a list focus kind (the "any" kinds some of them, SG6); the
                    // interpreter reads a condition's object operand as one object, so the critter vicinity kinds are admitted in
                    // actions only (an action's single object is the list's first, 0x44B960; loop for takes the list).
                    if (condition && (ScrFocusKind)kind is ScrFocusKind.EveryoneInVicinity or ScrFocusKind.AnyoneInVicinity) return Refuse(ScrRefusalCode.FocusKindNotAdmitted);
                    break;
            }
            // Get skill stores into its third operand (SX1).
            if (!condition && type == ScrCatalogue.Action.GetSkill && slot == 2 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Get reaction stores into its number operand 2 (ARC-DEV-178, GR1).
            if (!condition && type == ScrCatalogue.Action.GetReaction && slot == 2 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Get stat stores into its third operand (ARC-DEV-157, GS1).
            if (!condition && type == ScrCatalogue.Action.GetStat && slot == 2 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Get object type stores into its number operand 1 (ARC-SCRIPT-018).
            if (!condition && type == ScrCatalogue.Action.GetObjectType && slot == 1 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Random stores into its number operand 2 (ARC-WORLD-081, PG5).
            if (!condition && type == ScrCatalogue.Action.Random && slot == 2 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Get magick-tech adjustment stores into its number operand 4 (ARC-SCRIPT-1562, CP7).
            if (!condition && type == ScrCatalogue.Action.GetMagickTechAdjustment && slot == 4 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // The magick-tech adjustment stores into its number operand 3 (ARC-SCRIPT-042, WE2).
            if (!condition && type == ScrCatalogue.Action.MagickTechAdjustment && slot == 3 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Distance between stores into its number operand 2 (ARC-SCRIPT-049, HW3).
            if (!condition && type == ScrCatalogue.Action.DistanceBetween && slot == 2 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Get location stores into its number operands 1 and 2 (ARC-SCRIPT-1359, GL1).
            if (!condition && type == ScrCatalogue.Action.GetLocation && slot is 1 or 2 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Get hit points and get fatigue points store into their number operands 1 and 2 (ARC-SCRIPT-1481, VT1, VT2).
            if (!condition && type is ScrCatalogue.Action.GetHitPoints or ScrCatalogue.Action.GetFatiguePoints && slot is 1 or 2 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Get area stores into its number operand 1 (ARC-SCRIPT-050, GA1).
            if (!condition && type == ScrCatalogue.Action.GetArea && slot == 1 && (ScrValueKind)kind == ScrValueKind.Number) return Refuse(ScrRefusalCode.WriteToNumber);
            // Slot 0 of these types is itself an index, a store or a jump target.
            if (slot != 0) continue;
            var literal = (ScrValueKind)kind == ScrValueKind.Number;
            if (FlagIndexLimit(condition, type) is { } limit && literal && (value < 0 || value >= limit)) return Refuse(ScrRefusalCode.IndexOutOfRange, true);
            if (!condition && Writes(type) && literal) return Refuse(ScrRefusalCode.WriteToNumber);
            if (!condition && type == ScrCatalogue.Action.ChangeThisScript && literal && value is < 0 or > ScrCatalogue.MaxScriptNumber) return Refuse(ScrRefusalCode.ScriptNumberOutOfRange, true);
            if (!condition && type == ScrCatalogue.Action.Goto)
            {
                if (!literal) return Refuse(ScrRefusalCode.ComputedGoto);
                // A goto to exactly the line count is the end of the script: skip default, without auto-removal.
                if (value < 0) return Refuse(ScrRefusalCode.NegativeGoto, true);
                if (value > count) return Refuse(ScrRefusalCode.GotoBeyondEnd, true);
            }
        }
        return null;
    }

    private static bool InStore(ScrValueKind kind, int index) => kind switch
    {
        ScrValueKind.Counter => index is >= 0 and < ScrCatalogue.Counters,
        ScrValueKind.GlobalVariable => index is >= 0 and < ScrCatalogue.GlobalVariables,
        ScrValueKind.LocalVariable => index is >= 0 and < ScrCatalogue.LocalSlots,
        ScrValueKind.GlobalFlag => index is >= 0 and < ScrCatalogue.GlobalFlags,
        _ => true,
    };

    private static int? FlagIndexLimit(bool condition, int type) => condition
        ? type switch { ScrCatalogue.Condition.LocalFlag => ScrCatalogue.LocalFlags, ScrCatalogue.Condition.GlobalFlag => ScrCatalogue.GlobalFlags, _ => null }
        : type switch { ScrCatalogue.Action.SetLocalFlag or ScrCatalogue.Action.ClearLocalFlag => ScrCatalogue.LocalFlags, ScrCatalogue.Action.SetGlobalFlag or ScrCatalogue.Action.ClearGlobalFlag => ScrCatalogue.GlobalFlags, _ => null };

    // Actions whose operand 0 is the store they write.
    private static bool Writes(int type) => type is ScrCatalogue.Action.Assign or ScrCatalogue.Action.Add or ScrCatalogue.Action.Subtract
        or ScrCatalogue.Action.Multiply or ScrCatalogue.Action.Divide or ScrCatalogue.Action.GetStoryState
        or ScrCatalogue.Action.GetDaySinceStartup or ScrCatalogue.Action.GetCurrentHour or ScrCatalogue.Action.GetCurrentMinute or ScrCatalogue.Action.GetHoursSinceStartup;
}
