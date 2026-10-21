using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Technology;

/// <summary>ARC-TECHNOLOGY-008: the engine's MAINTAIN (ARC-MAGIC-009, 0x452F20 from 0x453168) charges a maintained technology
/// record through the charge-cell source of ARC-TECHNOLOGY-007 (ledger CC7, TD rows): each tick pays the cells in both TC1
/// settings, an emptied source ends the run, and BEGIN pays nothing for a technology record. A worn device (invented numbers:
/// complexity −80, maintenance 3 every 5 seconds) whose holder is a technologist (aptitude −60) paying from 12 cells; the host
/// writes each applied plan (the rules write nothing themselves).</summary>
[TestClass]
public sealed class TechMaintainCellsTests
{
    private static readonly SpellRunRecord Device = new(Technology: true, Cost: 9, MaintainCost: 3, MaintainPeriod: 5, ChargesFromCells: true);
    private static readonly SpellStageFacts Facts = new(SourcePayer: new SpellPayer(SpellPayerKind.Item, ItemCharges: 1));

    private sealed class NoStacking : ISpellRunStacking
    {
        public int? Blocking(SpellRun run, SpellRunObject target) => null;
        public void Cancel(SpellRun run) { }
    }

    private static SpellRun Run(SpellRunAction action) =>
        new(0, 168, action, new("armour", 7), new("hero", SpellObjectFacts.NpcType, -60), SpellLocation.None, null, SpellLocation.None, null, SpellRunFlags.Active);

    private static List<int> Ticks(TechnologyRuleProfile profile, out SpellStageResult last)
    {
        var units = 12;
        var paid = new List<int>();
        while (true)
        {
            var cells = new ChargeCellSource(new CellPayer(SpellPayerKind.Item, CellOwnerKind.Critter, units > 0 ? new CellStack("cells", units) : null, -80, -60), profile);
            last = SpellRunRules.Stage(Run(SpellRunAction.Maintain), Device, Facts, cells, new NoStacking(), MagicAnomalyProfile.Steamwoven);
            if (!last.Proceeds) return paid;
            Assert.AreEqual(3, last.Costs.Single(), "the maintenance asks its cost");
            var plan = cells.LastPlan!;
            Assert.IsTrue(plan.Writes);
            paid.Add(plan.Need);
            units = plan.StackRemoved ? 0 : plan.UnitsAfter;
            if (paid.Count > 10) Assert.Fail("the cells never ran out");
        }
    }

    [TestMethod]
    public void Each_maintain_tick_pays_cells_in_both_settings_and_an_empty_source_ends_the_run()
    {
        // Power trunc((−80 − 60) / 2) = −70; share trunc(3 × −70 / −80) = 2. Original 3 + 2 = 5 cells a tick, Steamwoven 2 × 3 − 2 = 4.
        CollectionAssert.AreEqual(new[] { 5, 5 }, Ticks(TechnologyRuleProfile.Original, out var original));
        CollectionAssert.AreEqual(new[] { 4, 4, 4 }, Ticks(TechnologyRuleProfile.Steamwoven, out var steamwoven));
        foreach (var end in new[] { original, steamwoven })
        {
            Assert.AreEqual((false, SpellStageStatus.MaintainCost, true), (end.Proceeds, end.Status, end.Ends), "too few cells: the run turns to END");
            Assert.AreEqual(SpellPaymentStatus.CellsRefused, end.Payments.Single().Status);
        }
    }

    // ARC-MAGIC-010: the run system over game time — its host holds the cells, writes each applied plan and fires each tick.
    private sealed class DeviceHost(TechnologyRuleProfile profile) : ISpellRunHost
    {
        public int Units { get; set; } = 12;
        public List<int> Paid { get; } = [];
        private ChargeCellSource? _cells;
        public SpellRunRecord Record(int spell) => Device with { Duration = new(0) } with { ActionComponents = [1, 1, 0, 0, 0] };
        public SpellStageFacts StageFacts(SpellRun run) => Facts;
        public ISpellCellSource Cells => _cells = new ChargeCellSource(
            new CellPayer(SpellPayerKind.Item, CellOwnerKind.Critter, Units > 0 ? new CellStack("cells", Units) : null, -80, -60), profile);
        public ISpellRunStacking Stacking { get; } = new NoStacking();
        public bool FlagsAllow(SpellRun run) => true;
        public int SourceSpellFlags(SpellRun run) => 0;
        public bool IsLocalPlayer(SpellRunObject? who) => false;
        public IReadOnlyList<SpellRunTarget> Collect(SpellRun run) => [new(run.Parent, SpellLocation.None)];
        public bool UseSpellAtMaximum(SpellRunObject pc) => false;
        public bool PerTarget(SpellRun run, SpellRunTarget target) => true;
        public bool TakesEffect(SpellRun run, SpellRunTarget target) => true;
        public SpellComponentGate Gate(SpellRun run, int component, SpellRunTarget target) => new(true, target);
        public SpellComponentOutcome Apply(SpellRun run, int component, SpellRunTarget target) => SpellComponentOutcome.None;
        public SpellTimeFacts TimeFacts(SpellRun run) => new(true);
        public int? Stat(SpellRunObject who, int stat) => 12;
        public bool MaintainBarHasSlot(SpellRun run) => true;
        public IReadOnlyList<SpellPayment> ChargeBegin(SpellRun run) => [];

        // The host writes the plan the last stage made (the rules write nothing themselves).
        public void Write()
        {
            if (_cells?.LastPlan is not { Writes: true } plan) return;
            Paid.Add(plan.Need);
            Units = plan.StackRemoved ? 0 : plan.UnitsAfter;
            _cells = null;
        }
    }

    [TestMethod]
    public void Over_game_time_the_run_system_pays_cells_each_tick_until_they_run_out()
    {
        foreach (var (profile, perTick) in new[] { (TechnologyRuleProfile.Original, new[] { 5, 5 }), (TechnologyRuleProfile.Steamwoven, new[] { 4, 4, 4 }) })
        {
            var host = new DeviceHost(profile);
            var system = new SpellRunSystem(SpellRunTable.Empty.With(Run(SpellRunAction.Begin)), host, MagicAnomalyProfile.Steamwoven);
            system.Begin(0);
            host.Write();
            Assert.AreEqual(0, host.Paid.Count, "BEGIN pays nothing for a technology record");
            var ticks = 0;
            var elapsed = 0L;
            while (system.Table[0] is not null && ticks < 10)
            {
                // The tick the run system queued: 5 seconds of maintenance, × 1000 ms, × 8 game time.
                elapsed += system.Queued.Last(e => e.Code == SpellRunEventCode.Tick).DelayMs;
                system.Fire(0, SpellRunEventCode.Tick);
                host.Write();
                ticks++;
            }
            CollectionAssert.AreEqual(perTick, host.Paid.ToArray(), $"{profile}: the cells each tick");
            Assert.AreEqual((perTick.Length + 1, 40_000L * (perTick.Length + 1)), (ticks, elapsed),
                "one more tick finds too few cells: the run ends and is freed");
        }
    }

    [TestMethod]
    public void A_cell_record_with_maintenance_0_pays_nothing_at_the_stage()
    {
        // The installed cell records 168 and 187 have maintenance 0 (ledger DV7): the stage's payment returns before the cells.
        var cells = new ChargeCellSource(new CellPayer(SpellPayerKind.Item, CellOwnerKind.Critter, new CellStack("cells", 12), -80, -60), TechnologyRuleProfile.Original);
        var tick = SpellRunRules.Stage(Run(SpellRunAction.Maintain), Device with { MaintainCost = 0 }, Facts, cells, new NoStacking(), MagicAnomalyProfile.Steamwoven);
        Assert.IsTrue(tick.Proceeds);
        Assert.AreEqual(SpellPaymentStatus.Paid, tick.Payments.Single().Status);
        Assert.IsNull(cells.LastPlan, "no cell was asked");
    }

    [TestMethod]
    public void Begin_pays_nothing_for_a_technology_record()
    {
        var cells = new ChargeCellSource(new CellPayer(SpellPayerKind.Item, CellOwnerKind.Critter, new CellStack("cells", 12), -80, -60), TechnologyRuleProfile.Original);
        var begin = SpellRunRules.Stage(Run(SpellRunAction.Begin), Device, Facts, cells, new NoStacking(), MagicAnomalyProfile.Steamwoven);
        Assert.AreEqual((true, 0), (begin.Proceeds, begin.Payments.Count), "the technology flag skips the checks and the BEGIN cost");
        Assert.IsNull(cells.LastPlan, "no cell was asked");
    }
}
