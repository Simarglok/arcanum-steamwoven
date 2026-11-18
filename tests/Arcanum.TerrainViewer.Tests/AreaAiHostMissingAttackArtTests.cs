using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-COMBAT-1932 (ledger VC38, VCG11): an NPC whose attack ART exists in no file after the missing-art repair fails the attack's start node
/// (0x42B9C0): no swing notice, no attack animation, no bark; an ART the scene cannot read keeps the fixed human timing (a stated bound).
/// Invented sectors and NPCs, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostMissingAttackArtTests
{
    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Open(Func<AreaAiNpc, AreaAiAttackArtRequest, AreaAiAttackArt?>? art)
    {
        var f = AreaAiHostRebindTests.Open();
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f), new AreaAiNpc(f.B, f.RowB, new CellCoord(130, 10))]) with { AttackArt = art };
        return (f, f.Build(scene));
    }

    [TestMethod]
    public void A_scene_answer_of_no_file_fails_the_start_with_no_swing_and_no_animation()
    {
        var (f, host) = Open((_, wanted) => AreaAiAttackArt.NoFile(wanted));
        using var fixture = f;
        var swing = host.BeginAttack(f.A, AiSchedulerRules.Pc);
        Assert.IsTrue(swing.Failed);
        Assert.IsNull(host.AttackPose(f.A, host.NowMs), "no attack animation plays");
        Assert.IsFalse(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Swing), "no swing notice (so no sound, no hit)");
    }

    [TestMethod]
    public void An_art_the_scene_cannot_read_keeps_the_fixed_timing_and_the_swing()
    {
        var (f, host) = Open((_, _) => null);
        using var fixture = f;
        var swing = host.BeginAttack(f.A, AiSchedulerRules.Pc);
        Assert.IsFalse(swing.Failed);
        Assert.IsNotNull(host.AttackPose(f.A, host.NowMs));
        Assert.IsTrue(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Swing));
    }
}
