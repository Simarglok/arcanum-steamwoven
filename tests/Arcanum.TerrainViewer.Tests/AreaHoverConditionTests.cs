using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-107 (B37, ledger OH6, OH10): the fourth line of a hovered portal's, container's and scenery's window — a portal's and
/// container's lock state at the left and the hit points "current/maximum" at the right edge, both red (0x555EC0, 0x556040, 0x555D80;
/// 0x43D600 over 0x43D5A0; 0x441D40) — over invented rows (no original data).</summary>
[TestClass]
public sealed class AreaHoverConditionTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // As AreaSpellCastObjectDamageTests: the common block (hit points 26, adjustment 27, damage 28, material 29, resistances 30) and the type
    // block's flags (36); the object flags are `AreaHoverIndex.FlagsSlot` (invulnerable 0x400000).
    private static AreaObjectRecord Row(int index, int type, int points, long objectFlags = 0, int adjustment = 0, long x = 10) =>
        new(Key(index), 0, type, 1000, "p", "p", "r", "s", AreaScope.Core, null, x, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x30000400), Field(AreaHoverIndex.FlagsSlot, objectFlags), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, points), Field(27, adjustment), Field(28, 0),
            Field(29, 2), new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(0, [25])], null),
            new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null), Field(36, 0), Field(38, 0), Field(24, -1),
        ], true, true, true);

    private static readonly SessionOpenableBaseline DoorLock = new(OpenableKind.Portal, 0, 0, 0);

    private sealed record Fixture(GameSessionOwner Session, AreaObjectTargets Targets, AreaObjectRecord[] Rows)
    {
        public AreaObjectAttackTarget Target(int row) => Targets.Target(Session, Rows[row].Key)!;
        public AreaHoverCondition? Condition(int row) => AreaHoverWindow.Condition(Session, Target(row));
    }

    // A door (0, 12 hit points), a chest (1, 30 plus an adjustment of 5), a barrel (2, scenery, 200) and an invulnerable pillar (3, scenery, 200).
    private static Fixture Create()
    {
        var session = ConversationSessionFixture.Create();
        AreaObjectRecord[] rows = [Row(0, 1, 12), Row(1, 2, 30, adjustment: 5, x: 12), Row(2, 3, 200, x: 14), Row(3, 3, 200, objectFlags: AreaHoverWindow.InvulnerableFlag, x: 16)];
        var targets = new AreaObjectTargets(1, rows, rows.Where(r => r.Type == 1).ToDictionary(r => r.Key, r => ($"sector:1:{r.Key.SourceIndex}", DoorLock)), null);
        return new(session, targets, rows);
    }

    private static string Of(AreaMessageDraw draw) =>
        string.Join(" | ", draw.Texts.Select(t => $"{t.Rect} {t.Font} '{t.Text}'{(t.Right ? " right" : "")} {t.Colour}"));

    [TestMethod]
    public void TheHitPointTextIsCurrentSlashMaximumAndMayBeNegative()
    {
        Assert.AreEqual("200/200", AreaHoverWindow.HitPointText(200, 200));
        Assert.AreEqual("0/35", AreaHoverWindow.HitPointText(0, 35));
        Assert.AreEqual("-4/12", AreaHoverWindow.HitPointText(-4, 12), "0x43D600 does not clamp");
    }

    [TestMethod]
    public void OnlyAPortalShowsHitPointsWhateverItsFlagsAndAPortalOrContainerShowsALockLine()
    {
        Assert.IsTrue(AreaHoverWindow.ShowsHitPoints(AreaHoverWindowKind.Portal, invulnerable: true), "0x555EC0 tests no object flag");
        Assert.IsTrue(AreaHoverWindow.ShowsHitPoints(AreaHoverWindowKind.Portal, invulnerable: false));
        foreach (var kind in new[] { AreaHoverWindowKind.Container, AreaHoverWindowKind.Scenery })
        {
            Assert.IsTrue(AreaHoverWindow.ShowsHitPoints(kind, invulnerable: false), $"{kind}");
            Assert.IsFalse(AreaHoverWindow.ShowsHitPoints(kind, invulnerable: true), $"{kind}: object flag 0x400000 hides it");
        }
        foreach (var kind in new[] { AreaHoverWindowKind.Critter, AreaHoverWindowKind.Item, AreaHoverWindowKind.Trap, AreaHoverWindowKind.None })
            Assert.IsFalse(AreaHoverWindow.ShowsHitPoints(kind, invulnerable: false), $"{kind}: its own window");
        Assert.IsTrue(AreaHoverWindow.ShowsLock(AreaHoverWindowKind.Portal) && AreaHoverWindow.ShowsLock(AreaHoverWindowKind.Container));
        Assert.IsFalse(AreaHoverWindow.ShowsLock(AreaHoverWindowKind.Scenery), "scenery has no lock line");
    }

    [TestMethod]
    public void TheLockLineIsJammedThenHeldThenLockedOrUnlocked()
    {
        Assert.AreEqual(26, AreaHoverWindow.LockMessage(LockRules.JammedBit | LockRules.LockedBit, locked: true), "jammed wins");
        Assert.AreEqual(26, AreaHoverWindow.LockMessage(LockRules.JammedBit | LockRules.MagicallyHeldBit, locked: false), "bit 0x2 is tested before bit 0x4");
        Assert.AreEqual(27, AreaHoverWindow.LockMessage(LockRules.MagicallyHeldBit, locked: false));
        Assert.AreEqual(27, AreaHoverWindow.LockMessage(LockRules.MagicallyHeldBit | LockRules.LockedBit, locked: true));
        Assert.AreEqual(9, AreaHoverWindow.LockMessage(LockRules.LockedBit, locked: true));
        Assert.AreEqual(10, AreaHoverWindow.LockMessage(0, locked: false));
        Assert.AreEqual(10, AreaHoverWindow.LockMessage(LockRules.LockedBit, locked: false), "0x441D40 said not locked (a junk pile, a day-only lock at night)");
    }

    [TestMethod]
    public void TheWindowDrawsTheFourthLineInRedWithTheHitPointsAtTheRightEdge()
    {
        var scenery = AreaHoverWindow.Window(AreaHoverWindowKind.Scenery, "Control Box", 437, new(200, 200, false, null, false))!;
        Assert.AreEqual("200/200", scenery.Value);
        Assert.AreEqual("", scenery.Line, "scenery has no lock line");
        Assert.AreEqual("(290, 504, 291, 55) Title 'Control Box' White | (290, 564, 291, 18) Text '200/200' right Red", Of(AreaMessageLayout.Draw(scenery)));

        var door = AreaHoverWindow.Window(AreaHoverWindowKind.Portal, "Door", 436, new(7, 12, true, 1, true))!;
        Assert.AreEqual("(290, 504, 291, 19) Title 'Door' White | (290, 564, 291, 18) Text 'Locked' Red | (290, 564, 291, 18) Text '7/12' right Red",
            Of(AreaMessageLayout.Draw(door)), "an invulnerable portal still shows its hit points; the lock state is drawn left, the hit points right on the same rectangle");

        var chest = AreaHoverWindow.Window(AreaHoverWindowKind.Container, "Chest", 435, new(35, 35, true, 0, false))!;
        Assert.AreEqual("(290, 504, 291, 19) Title 'Chest' White | (290, 564, 291, 18) Text 'Unlocked' Red",
            Of(AreaMessageLayout.Draw(chest)), "an invulnerable container shows no hit points");

        var plain = AreaHoverWindow.Window(AreaHoverWindowKind.Scenery, "Control Box", 437)!;
        Assert.AreEqual("(290, 504, 291, 55) Title 'Control Box' White", Of(AreaMessageLayout.Draw(plain)), "without a condition only the image and the name");
        Assert.AreEqual("", AreaHoverWindow.Window(AreaHoverWindowKind.Critter, "Virgil", null, new(5, 5, false, null, false))!.Value, "a critter's lines are its bars'");
    }

    [TestMethod]
    public void TheConditionIsReadFromTheSession()
    {
        var f = Create();
        Assert.AreEqual(new AreaHoverCondition(12, 12, false, 0, false), f.Condition(0), "a door: whole, not locked");
        var chest = f.Condition(1)!.Value;
        Assert.AreEqual((35, 35, false, false), (chest.HitPoints, chest.MaximumHitPoints, chest.Invulnerable, chest.Locked), "a chest: points plus the adjustment");
        Assert.IsNotNull(chest.LockFlags, "a container has a lock line too");
        Assert.AreEqual(new AreaHoverCondition(200, 200, false, null, false), f.Condition(2), "the Control Box: scenery of 200 hit points (ARC-DEV-161), no lock");
        Assert.AreEqual(new AreaHoverCondition(200, 200, true, null, false), f.Condition(3), "invulnerable scenery");
        Assert.IsNull(AreaHoverWindow.Condition(f.Session, new("e", f.Rows[0], DamageableObjectKind.Portal, null, null, null, "refused")), "no baseline, no lines");
        Assert.AreEqual("200/200", AreaHoverWindow.Window(AreaHoverWindowKind.Scenery, "Control Box", 437, f.Condition(2))!.Value);
    }

    [TestMethod]
    public void DamageAndLockWritesShowAtTheNextHover()
    {
        var f = Create();
        var door = f.Target(0).Object!;
        var openable = new SessionOpenable(door.Map, door.Entity);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.SetOpenableLocked(f.Session.Version, openable, DoorLock, true).Disposition);
        Assert.AreEqual(new AreaHoverCondition(12, 12, false, LockRules.LockedBit, true), f.Condition(0));
        Assert.AreEqual(9, AreaHoverWindow.LockMessage(f.Condition(0)!.Value.LockFlags!.Value, f.Condition(0)!.Value.Locked));

        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.SetOpenableJammed(f.Session.Version, openable, DoorLock, true).Disposition);
        Assert.AreEqual(26, AreaHoverWindow.LockMessage(f.Condition(0)!.Value.LockFlags!.Value, f.Condition(0)!.Value.Locked), "jammed");

        var barrel = f.Target(2).Object!;
        var hit = f.Session.DamageObject(f.Session.Version, barrel, 30);
        Assert.AreEqual(GameSessionDisposition.Changed, hit.Disposition, hit.Failure.ToString());
        var after = f.Condition(2)!.Value;
        Assert.IsTrue(after.HitPoints is < 200 and > 0 && after.MaximumHitPoints == 200, $"{after.HitPoints}/{after.MaximumHitPoints}");
    }
}
