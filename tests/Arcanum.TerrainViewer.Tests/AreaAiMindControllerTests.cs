using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-2144 (0x459040 with the mask OSF_MIND_CONTROLLED, read in the executable): the AI host's controller of a mind-controlled NPC is the parent of the spell run that sets the flag and holds the NPC. The NPC is
/// the invented human of the rebind tests' fixture, the spell an invented one whose record sets the flag (its set-flags word); the hero starts the run on the NPC and the session writes the flag as the cast host would.
/// </summary>
[TestClass]
public sealed class AreaAiMindControllerTests
{
    private const int Spell = 61, MindControlled = 0x8000000;
    private static readonly Arcanum.Simulation.Movement.CellCoord HeroCell = new(58, 12), NpcCell = new(59, 14);

    private static SessionSpellRecord Record() => AreaHealerCastTests.Record() with { };

    [TestMethod]
    public void The_host_names_the_parent_of_the_run_that_sets_the_flag_as_the_controller_and_none_without_it()
    {
        using var f = AreaAiHostRebindTests.Open();
        var steps = AreaAiHostRebindTests.Sector(0, 0);
        AreaAiHost? host = null;
        var spellMap = new AreaSpellCastMap(f.Session, 1, steps, [f.RowA, f.RowB], f.Combat, () => HeroCell, () => null,
            placed: () => host?.Movers.All.ToDictionary(m => m.Key, m => ((long X, long Y)?)(m.Cell.X, m.Cell.Y)) ?? []);
        var events = new AreaEventWorld(() => f.Combat, () => null, ai: () => host);
        var record = AreaHealerCastTests.Record();
        record = record with { Data = record.Data with { Number = Spell, SpellFlagsSet = MindControlled } };
        var caster = new SessionSpellCaster(f.Session, n => n == Spell ? record : null, spellMap, events);
        host = f.Build(f.Scene(0, steps, [new(f.B, f.RowB, NpcCell)]) with { Spells = () => caster });

        Assert.IsNull(host.MindController(f.B), "no flag, no run");
        var hero = caster.Facts(SessionSpellCaster.Hero)!;
        var at = caster.Facts(f.B)!;
        var started = f.Session.StartSpellRun(f.Session.Version, new(new(Spell, hero, hero, at, at.Location, ParentIsLocalPlayer: true)), record.Run, caster.InvocationWorld(), f.Session.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        Assert.IsNull(host.MindController(f.B), "the run holds the NPC, yet the NPC does not carry the flag bit");

        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteObjectFlag(f.Session.Version, f.B, SessionObjectFlagField.Spell, MindControlled, true, dataHas: false).Disposition);
        Assert.AreEqual(AiSchedulerRules.Pc, host.MindController(f.B), "the hero is the run's parent");
        Assert.IsNull(host.MindController(f.A), "another NPC is held by no run");

        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteObjectFlag(f.Session.Version, f.B, SessionObjectFlagField.Spell, MindControlled, false, dataHas: false).Disposition);
        Assert.IsNull(host.MindController(f.B), "the flag cleared");
    }
}
