namespace Arcanum.Data.Magic;

/// <summary>ARC-MAGIC-002: the target areas of line +0 and the effect components of lines +5 onward (contract
/// docs/formats/spell-records.md, checked in the 1.0.7.4 executable, ledger "Spell components").</summary>
public sealed partial class SpellRecordTable
{
    private static readonly string[] AreaKeys = ["AoE:", "AoE_SF:", "AoE_NO_SF:", "Radius:", "Count:"];
    private static readonly string[] Line0Keys = [.. AreaKeys, .. SpellComponentTables.Actions.SelectMany(a => AreaKeys.Select(k => a + k))];
    private static readonly string[] ComponentKeys =
        [.. AreaKeys, "Apply_AoE:", "ItemTriggers:", "Type:", "SubGoal:", "MinIQ:", "Spell:", "Cost:", "Branch:", "DmgType:", "Dmg:",
         "Dmg_Flags:", "Count:", "Cause:", "Scaled:", "Play:", "MagicTech:", "Move_Location:", "Tile_Radius:", "Max:", "Proto:",
         "Clear_Faction:", "Palette:", "List:", "TestVal:"];
    private static readonly HashSet<int> PlainAreaRecords = [1, 2, 9];

    /// <summary>
    /// Line +0: <c>AoE:</c> (the original logs an error when it is missing) gives every action's target mask, with the
    /// not-invulnerable bit added except for records 1, 2 and 9; <c>AoE_SF:</c> and <c>AoE_NO_SF:</c> give every action's flag
    /// filters (0 when absent); <c>Radius:</c> and <c>Count:</c> every action's radius and count when present. Then per action
    /// in order the same five keys with the action's prefix: masks and filters are OR-ed in, radius and count replace.
    /// </summary>
    [OriginalFunction(0x457650, OriginalFunctionScope.Full)]
    private static SpellDataRefusal? ParseLine0(NamedKeyCursor cursor, Draft d, SpellRecordMode mode)
    {
        var check = new ValueCheck(mode, []);
        var targets = check.Targets(cursor, "AoE:");
        if (targets is null) check.Missing("AoE:");
        var mask = (targets ?? 0) | (PlainAreaRecords.Contains(d.Number) ? 0 : SpellComponentTables.NotInvulnerableBit);
        var required = check.Bits(cursor, "AoE_SF:", SpellNameTables.SpellFlags) ?? 0;
        var excluded = check.Bits(cursor, "AoE_NO_SF:", SpellNameTables.SpellFlags) ?? 0;
        var radius = check.Int(cursor, "Radius:");
        var count = check.Int(cursor, "Count:");
        for (var a = 0; a < SpellRecord.Actions; a++)
            d.Areas[a] = new(mask, required, excluded, radius ?? d.Areas[a].Radius, count ?? d.Areas[a].Count);
        for (var a = 0; a < SpellRecord.Actions; a++)
        {
            var prefix = SpellComponentTables.Actions[a];
            var area = d.Areas[a];
            if (check.Targets(cursor, prefix + "AoE:") is { } more) area = area with { Targets = area.Targets | more };
            if (check.Bits(cursor, prefix + "AoE_SF:", SpellNameTables.SpellFlags) is { } sf) area = area with { RequiredSpellFlags = area.RequiredSpellFlags | sf };
            if (check.Bits(cursor, prefix + "AoE_NO_SF:", SpellNameTables.SpellFlags) is { } nsf) area = area with { ExcludedSpellFlags = area.ExcludedSpellFlags | nsf };
            if (check.Int(cursor, prefix + "Radius:") is { } r) area = area with { Radius = r };
            if (check.Int(cursor, prefix + "Count:") is { } c) area = area with { Count = c };
            d.Areas[a] = area;
        }
        return check.Refusal;
    }

    /// <summary>
    /// One component line: the action name (no key; an unknown one ends the original's process), then <c>AoE:</c>,
    /// <c>AoE_SF:</c>, <c>AoE_NO_SF:</c>, <c>Radius:</c> (the action's line +0 radius when absent), <c>Count:</c> (−1),
    /// <c>Apply_AoE:</c>, <c>ItemTriggers:</c> and <c>Type:</c> (NoOp when absent), then the type's data. A Damage component
    /// marks the record as damaging; an ObjFlag turning a spell flag on records that flag.
    /// </summary>
    [OriginalFunction(0x458060, OriginalFunctionScope.Partial)]
    private static SpellDataRefusal? ParseComponent(NamedKeyCursor cursor, Draft d, IReadOnlyList<string> types, SpellRecordMode mode)
    {
        var check = new ValueCheck(mode, []);
        if (cursor.NameValue(SpellComponentTables.Actions, out _) is not { } action) return null;
        var targets = check.Targets(cursor, "AoE:") ?? 0;
        var required = check.Bits(cursor, "AoE_SF:", SpellNameTables.SpellFlags) ?? 0;
        var excluded = check.Bits(cursor, "AoE_NO_SF:", SpellNameTables.SpellFlags) ?? 0;
        var radius = check.Int(cursor, "Radius:") ?? d.Areas[action].Radius;
        var count = check.Int(cursor, "Count:") ?? -1;
        var apply = check.Targets(cursor, "Apply_AoE:") ?? 0;
        var triggers = check.Triggers(cursor, "ItemTriggers:") ?? 0;
        var type = SpellComponentType.NoOp;
        if (cursor.NamedName("Type:", types, out _) is { } index) type = (SpellComponentType)index;
        else check.Missing("Type:");
        var data = cursor.Fault == NamedKeyFault.None ? ComponentData(cursor, check, d, type) : null;
        d.Components.Add(new((SpellAction)action, type, new(targets, required, excluded, radius, count), apply, triggers, data));
        return check.Refusal;
    }

    private static SpellComponentData? ComponentData(NamedKeyCursor cursor, ValueCheck check, Draft d, SpellComponentType type)
    {
        switch (type)
        {
            case SpellComponentType.AGoal:
            {
                var goal = Positional(cursor, SpellComponentTables.AnimationGoals);
                return new AnimationGoalData(goal is { } g ? SpellComponentTables.AnimationGoalValues[g] : 0, check.Int(cursor, "SubGoal:") ?? 0);
            }
            case SpellComponentType.AGoalTerminate:
                return Positional(cursor, SpellComponentTables.AnimationGoals) is { } terminate
                    ? new AnimationGoalTerminateData(SpellComponentTables.AnimationGoalValues[terminate]) : null;
            case SpellComponentType.AIRedirect:
            {
                var flag = Positional(cursor, SpellNameTables.CritterFlags);
                return new AiRedirectData(flag is { } f ? 1u << f : 0, check.Int(cursor, "MinIQ:") ?? -1);
            }
            case SpellComponentType.Cast:
            case SpellComponentType.Interrupt:
            {
                var key = type == SpellComponentType.Cast ? "Spell:" : "MagicTech:";
                var spell = check.Int(cursor, key);
                if (spell is null) check.Missing(key);
                return new SpellReferenceData(spell ?? 10000);
            }
            case SpellComponentType.ChargeNBranch:
                return new ChargeBranchData(check.Int(cursor, "Cost:") ?? 0, check.Int(cursor, "Branch:") ?? -1);
            case SpellComponentType.Damage:
            case SpellComponentType.Heal:
            {
                var names = type == SpellComponentType.Damage
                    ? SpellComponentTables.DamageTypes : SpellComponentTables.DamageTypes.Take(SpellComponentTables.HealDamageTypes).ToArray();
                var damageType = check.Name(cursor, "DmgType:", names) ?? 0;
                var range = check.Range(cursor, "Dmg:");
                var flags = check.Triggers(cursor, "Dmg_Flags:", SpellComponentTables.DamageFlags, SpellComponentTables.DamageFlagValues) ?? 0;
                if (type == SpellComponentType.Damage) d.HasDamage = true;
                return new DamageData(damageType, range?.First ?? 0, range?.Second ?? 0, flags);
            }
            case SpellComponentType.Effect:
            {
                var effect = PositionalInt(cursor, check);
                var add = Positional(cursor, SpellComponentTables.AddRemove);
                return new EffectData(effect ?? 0, add == 1, check.Int(cursor, "Count:") ?? 1,
                    check.Name(cursor, "Cause:", SpellComponentTables.Causes) ?? SpellComponentTables.SpellCause, check.Int(cursor, "Scaled:") ?? 0);
            }
            case SpellComponentType.EnvFlag:
            {
                var flag = Positional(cursor, SpellNameTables.SpellFlags);
                var on = Positional(cursor, SpellComponentTables.OnOff);
                return new EnvironmentFlagData(flag is { } f ? 1u << f : 0, on == 1);
            }
            case SpellComponentType.EyeCandy:
            {
                var number = PositionalInt(cursor, check);
                var add = Positional(cursor, SpellComponentTables.AddRemove);
                return new EyeCandyData(number ?? 0, add == 1,
                    check.Triggers(cursor, "Play:", SpellComponentTables.Play, SpellComponentTables.PlayValues) ?? 0);
            }
            case SpellComponentType.Movement:
            {
                var location = check.Name(cursor, "Move_Location:", SpellComponentTables.MoveLocations);
                int? radius = location == SpellComponentTables.RandomTileInRadius ? check.Int(cursor, "Tile_Radius:") ?? 0 : null;
                return new MovementData(location ?? 0, radius);
            }
            case SpellComponentType.ObjFlag:
            {
                var field = Positional(cursor, SpellComponentTables.ObjectFlagFields);
                var bit = field is { } f ? Positional(cursor, SpellComponentTables.ObjectFlagNames[f]) : null;
                var on = Positional(cursor, SpellComponentTables.OnOff);
                if (field is null || bit is null || on is null) return null;
                var id = SpellComponentTables.ObjectFlagFieldIds[field.Value];
                if (id == SpellComponentTables.SpellFlagsField && on == 1) d.SpellFlagsSet |= 1u << bit.Value;
                return new ObjectFlagData(id, 1u << bit.Value, on == 1);
            }
            case SpellComponentType.Recharge:
            {
                var amount = PositionalInt(cursor, check);
                return new RechargeData(amount ?? 0, check.Int(cursor, "Max:") ?? 9999);
            }
            case SpellComponentType.Summon:
            {
                var proto = check.Int(cursor, "Proto:");
                if (proto is null) check.Missing("Proto:");
                return new SummonData(proto ?? 0, check.Int(cursor, "Clear_Faction:") ?? 0, check.Int(cursor, "Palette:") ?? 0,
                    check.Int(cursor, "List:") ?? -1);
            }
            case SpellComponentType.TestNBranch:
            {
                var field = Positional(cursor, SpellComponentTables.TestFields);
                var comparison = Positional(cursor, SpellComponentTables.Comparisons);
                return new TestBranchData(field is { } f ? SpellComponentTables.TestFieldValues[f] : 0, comparison ?? 0,
                    check.Int(cursor, "TestVal:") ?? 0, check.Int(cursor, "Branch:") ?? -1);
            }
            case SpellComponentType.Trait:
            {
                var field = Positional(cursor, SpellComponentTables.TestFields);
                var value = PositionalInt(cursor, check);
                return new TraitData(field is { } f ? SpellComponentTables.TestFieldValues[f] : 0, value ?? 0, check.Int(cursor, "Palette:") ?? 0);
            }
            case SpellComponentType.TraitIdx:
            {
                var field = Positional(cursor, SpellComponentTables.IndexedFields);
                var index = field is { } f ? Positional(cursor, SpellComponentTables.IndexedFieldNames[f]) : null;
                var value = PositionalInt(cursor, check);
                return field is null ? null : new TraitIndexData(SpellComponentTables.IndexedFieldIds[field.Value], index ?? 0, value ?? 0);
            }
            case SpellComponentType.Trait64:
            {
                var field = Positional(cursor, SpellComponentTables.TestFields);
                var source = Positional(cursor, SpellComponentTables.Trait64Sources);
                return new Trait64Data(field is { } f ? SpellComponentTables.TestFieldValues[f] : 0, source ?? 0);
            }
            default:
                return null;
        }
    }

    private static int? Positional(NamedKeyCursor cursor, IReadOnlyList<string?> names) => cursor.NameValue(names, out _);

    private static int? PositionalInt(NamedKeyCursor cursor, ValueCheck check) => check.Positional(cursor);
}
