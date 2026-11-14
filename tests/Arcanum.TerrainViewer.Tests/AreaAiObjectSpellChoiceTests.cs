using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1755 (VB2-2 gap d): an NPC whose COMBAT_FOCUS is an object that is no critter (a trap's shooter object) runs the spell and item chooser over the object as its target. The original chooser 0x4ABC70 → 0x4ABF10 reads no
/// target type; the rules it calls (the stronger test 0x4ABEB0, the influence test 0x459380, the eligibility test 0x4AE720 with the fail-chance predictor 0x453B20 and the line of fire 0x4ADE00) answer for an object as the host's do,
/// except that the predictor reads the object's stored magic resistance (the array getter 0x4072D0, field 0x1F, element 4) where the host read 0. The NPC here knows an invented fire-college offensive spell; the shooter is an invented
/// scenery. No original data.
/// </summary>
[TestClass]
public sealed class AreaAiObjectSpellChoiceTests
{
    private const int Spell = 20;
    private static readonly CellCoord HeroCell = new(58, 12), ShooterCell = new(61, 14), Adjacent = new(59, 14);

    private static AreaFieldValue Elements(int slot, params (int Key, long Value)[] elements) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. elements.Select(e => new AreaSparseElement(e.Key, [e.Value]))], null);

    // A scenery (type 3) on the shooter's cell whose stored magic resistance (element 4 of the resistance array, slot 30) is the given value.
    private static AreaObjectRecord ShooterRow(long magicResistance) => new(new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, 5, 500L, new string('f', 64), 100), 0, 3, 9999, "p", "p", "r", "s", AreaScope.Core, null,
        ShooterCell.X, ShooterCell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], magicResistance == 0 ? [] : [Elements(30, (4, magicResistance))], true, true, true);

    // An invented damaging fire spell (the college is number / 5 = 4, rank 1) of the offensive list (value 5 of the ten lists), cost 1, no save, any target.
    private static SessionSpellRecord Record() => SessionSpellRecords.Translate(new SpellRecord
    {
        Number = Spell, MinimumWillpower = 6, Cost = 1, MaintainCost = 0, MaintainPeriod = 0, DurationSeconds = 0, DurationStat = -1, DurationStatLevel = 0, DurationStatModifier = 1, DurationTriggerCount = 0,
        ResistStat = -1, ResistModifier = 0, Range = 99, IsTechnology = false, ChargesFromCells = false, SkipsBeginCost = false, Info = SpellInfo.Aggressive, Enabled = true, NoResist = false, NoReflect = true,
        CastingAnimation = -1, Missile = -1, CasterEyeCandy = [-1, -1, -1, -1, -1], TargetEyeCandy = [-1, -1, -1, -1, -1], NoStack = 0, CancelsSpellFlags = 0, CancelsEnvironmentSpellFlags = 0,
        DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, ItemTriggers = 0, AiPriorities = [-1, -1, -1, 5, -1, -1, -1, -1, -1, -1], AiDefensiveSecond = 0,
        LinesPresent = [true, true, false, false, false], KeysRead = [],
        Areas = [new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)],
        Components = [new(SpellAction.Begin, SpellComponentType.Damage, new(0, 0, 0, 0, -1), 0, 0, new DamageData(0, 3, 4, 0))],
        HasDamage = true, SpellFlagsSet = 0, AreaLine = null, ComponentLines = [],
    });

    private sealed record Open(AreaAiHostRebindTests.Fixture F, AreaAiHost Host, SessionSpellCaster Caster, string Shooter) : IDisposable
    {
        public void Dispose() => F.Dispose();
    }

    // NPC B (the fixture's) knows the college-4 spells up to rank 5 and stands at <npcAt>; the scenery shooter stands on its cell; the cast map holds the rows as the viewer's does and the AI host reads the object through it.
    private static Open Create(long magicResistance)
    {
        // Field 41: the college ranks (college 4 at rank 5); field 38 element 22: the stored magick points, which the spell list needs (0x4CC8A0 gives none to an NPC with no points).
        var f = AreaAiHostRebindTests.Open(changeB: row => row with
        {
            EffectiveFields = [.. row.EffectiveFields.Select(field => field.Slot == 41 ? Elements(41, (4, 5))
                : field.Slot == 38 ? field with { Elements = [.. field.Elements.Select(e => e.Key == 22 ? e with { Words = [20] } : e)] } : field)],
        });
        var shooterRow = ShooterRow(magicResistance);
        var steps = AreaAiHostRebindTests.Sector(0, 0);
        AreaAiHost? host = null;
        var spellMap = new AreaSpellCastMap(f.Session, 1, steps, [f.RowA, f.RowB, shooterRow], f.Combat, () => HeroCell, () => null,
            placed: () => host?.Movers.All.ToDictionary(m => m.Key, m => ((long X, long Y)?)(m.Cell.X, m.Cell.Y)) ?? []);
        var events = new AreaEventWorld(() => f.Combat, () => null, ai: () => host);
        var caster = new SessionSpellCaster(f.Session, n => n == Spell ? Record() : null, spellMap, events);
        var shooter = spellMap.Id(shooterRow);
        var scene = f.Scene(0, steps, [new(f.A, f.RowA, new(2, 2)), new(f.B, f.RowB, Adjacent)]) with
        {
            Spells = () => caster,
            ObjectFacts = id => spellMap.Object(id) is { IsCritter: false, IsItem: false } found
                ? new AreaAiObjectFacts(found.Type, new CellCoord(checked((int)found.Location.X), checked((int)found.Location.Y))) : null,
        };
        host = f.Build(scene);
        return new(f, host, caster, shooter);
    }

    // The object's attack event puts the NPC into COMBAT_FOCUS on it (0x4A9650 → 0x4AABE0).
    private static void Focus(Open o)
    {
        o.Host.SpellAttacked(o.Shooter, o.F.B);
        Assert.AreEqual(DangerState.CombatFocus, o.F.Session.QueryDanger(o.F.B).State, "the attack event gave the focus: " + o.Host.TraceOf(o.F.B)?.Event);
        Assert.AreEqual(o.Shooter, o.F.Session.QueryDanger(o.F.B).Focus?.Entity);
    }

    [TestMethod]
    public void The_npc_knows_the_spell_and_the_cast_host_describes_the_object_as_no_critter()
    {
        using var o = Create(0);
        Assert.IsTrue(o.Caster.AiKnows(o.F.B, Spell), "the fixture NPC's college ranks are read from its fields");
        CollectionAssert.Contains(o.Caster.AiLists[AiActionList.Offensive].ToArray(), Spell);
        Assert.AreEqual(0, o.Host.Find(o.Shooter)!.Level, "an object has no level (0x4B0490 answers 0 for any type but 15 and 16), so it is never the stronger one (0x4ABEB0)");
        Assert.AreEqual(HostilityKind.Object, o.Host.Find(o.Shooter)!.Kind);
    }

    [TestMethod]
    public void The_chooser_picks_the_offensive_spell_with_the_object_as_its_target_and_perform_casts_it_at_the_object()
    {
        using var o = Create(0);
        Focus(o);
        // The spell choice rolls against the chance (25: no leader, parameter row absent) at each pass; passes go on until it comes up.
        string? choice = null;
        for (var pass = 0; pass < 300 && choice is null; pass++)
        {
            o.Host.UpdateDanger(o.F.B);
            if (o.Host.TraceOf(o.F.B)?.Spell is { } spell && spell.Contains($"spell={Spell}", StringComparison.Ordinal)) choice = spell;
        }
        Assert.IsNotNull(choice, "the chooser found the spell in 300 passes: " + o.Host.TraceOf(o.F.B)?.Spell);
        StringAssert.Contains(choice, $"target={o.Shooter}", "the offensive list is aimed at the pass target, the object");

        o.Host.Perform(o.F.B);

        var run = o.Host.RunOf(o.F.B) ?? throw new AssertFailedException("no run: " + o.Host.TraceOf(o.F.B)?.Perform + " / " + o.Host.TraceOf(o.F.B)?.Cast);
        Assert.AreEqual((GoalType.CastSpell, o.Shooter), (run.Bottom.Goal, run.Bottom.Target), "the casting goal on the object: " + o.Host.TraceOf(o.F.B)?.Cast);
        var spellRun = run.Bottom.SpellRun is { } id ? o.F.Session.QuerySpellRuns()[id] : null;
        Assert.AreEqual((Spell, o.Shooter), (spellRun?.Spell ?? -1, spellRun?.Target?.Id), "the magic run is aimed at the object: " + o.Host.TraceOf(o.F.B)?.Cast);
    }

    [TestMethod]
    public void The_predictor_reads_the_stored_magic_resistance_of_the_object_and_the_eligibility_test_refuses_at_38()
    {
        using var plain = Create(0);
        Assert.AreEqual(0, plain.Host.FailChance(plain.F.B, plain.Shooter, Spell), "an object with no stored magic resistance: no chance to fail");
        Assert.AreEqual(AiCastRefusal.Accepted, AiSupportRules.Eligibility(plain.Host, plain.F.B, null, plain.Shooter, Spell));

        using var warded = Create(50);
        Assert.AreEqual(50, warded.Host.FailChance(warded.F.B, warded.Shooter, Spell), "RS5: a damaging record answers the stored value R of the object (field 0x1F element 4)");
        Assert.AreEqual(AiCastRefusal.FailChance, AiSupportRules.Eligibility(warded.Host, warded.F.B, null, warded.Shooter, Spell), "RS7: 38 or more is refused with code 8");

        using var edge = Create(38);
        Assert.AreEqual(AiCastRefusal.FailChance, AiSupportRules.Eligibility(edge.Host, edge.F.B, null, edge.Shooter, Spell), "exactly 38 is refused too");

        using var slight = Create(37);
        Assert.AreEqual(AiCastRefusal.Accepted, AiSupportRules.Eligibility(slight.Host, slight.F.B, null, slight.Shooter, Spell), "37 is below the limit");
    }

    [TestMethod]
    public void An_object_with_a_high_stored_magic_resistance_is_never_chosen_as_the_spell_target()
    {
        using var o = Create(50);
        Focus(o);
        var answered = 0;
        for (var pass = 0; pass < 200; pass++)
        {
            o.Host.UpdateDanger(o.F.B);
            var spell = o.Host.TraceOf(o.F.B)?.Spell ?? "";
            Assert.IsFalse(spell.Contains($"spell={Spell}", StringComparison.Ordinal), "the spell is refused by the predictor: " + spell);
            if (spell.Contains(nameof(AiSpellChoiceStop.NothingFound), StringComparison.Ordinal)) answered++;
        }
        Assert.IsTrue(answered > 0, "the chooser was reached and found nothing (the roll comes up one pass in four)");
    }

    [TestMethod]
    public void The_stored_magic_resistance_of_a_critter_row_is_not_read_through_the_object_hook()
    {
        using var o = Create(50);
        var map = new AreaSpellCastMap(o.F.Session, 1, AreaAiHostRebindTests.Sector(0, 0), [o.F.RowA, o.F.RowB, ShooterRow(50)], o.F.Combat, () => HeroCell, () => null);
        Assert.AreEqual(50, map.ObjectMagicResistance(map.Id(ShooterRow(50))));
        Assert.AreEqual(0, map.ObjectMagicResistance(o.F.B), "an NPC's value is its record's (SpellCastCritter), not the array of a row");
        Assert.AreEqual(0, map.ObjectMagicResistance("row:nowhere"));
        Assert.AreEqual(0, new AreaSpellCastMap(o.F.Session, 1, AreaAiHostRebindTests.Sector(0, 0), [ShooterRow(0)], o.F.Combat, () => HeroCell, () => null).ObjectMagicResistance(map.Id(ShooterRow(0))));
    }
}
