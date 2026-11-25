using Arcanum.Data.Scripts;
using Arcanum.Data.Technology;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2900 (ledger IE3, TQ20; exe read 0x56E720 → 0x43CCA0 → 0x466E00 → 0x467860): a component the hero's assembly deletes runs its remove-item script (28) with the hero as the triggerer, through the standing item script host; with the script and no host the assembly is
// refused whole. Two parts (2120) make a product (2320); numbers, scripts and sizes are invented.
[TestClass]
public sealed class AssemblyRemoveScriptTests
{
    private const int RemoveScript = 702;
    private static readonly SessionItemKind Part = new(SessionItemType.Generic, 2120, 1, 2);
    private static readonly SessionItemKind Product = new(SessionItemType.Generic, 2320, 5, 6);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionCraftLookup InBag = _ => new SessionCraftFacts(10);

    private static ComponentFacts? Facts(int prototype) => prototype == 2120 ? new(0, 3) : null;
    private static SessionItemKind? Kinds(int prototype) => prototype == 2320 ? Product : null;

    private static SchematicRecord Schematic() => new()
    {
        Number = 2000, Kind = SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3,
        Component1 = new(Array.AsReadOnly(new[] { 2100 }), 1), Component2 = new(Array.AsReadOnly(new[] { 2100 }), 1),
        Product = new(Array.AsReadOnly(new[] { 2300 }), 1), Quantity = 1, MissingLines = Array.AsReadOnly(new bool[7]),
    };

    private static GameSessionOwner Hero()
    {
        var owner = CreateWith(HalfElf() with { Gold = 0, Kit = [new(Part), new(Part)] });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, new([], [])).Disposition);
        owner.AttachItemScripts(prototype => prototype == 2120 ? new SessionPrototypeScript[] { new(SessionScriptPoint.RemoveItem, RemoveScript, 0, 0) } : null);
        return owner;
    }

    [TestMethod]
    public void The_components_an_assembly_deletes_run_their_remove_item_script_with_the_hero_as_the_triggerer_in_the_components_order()
    {
        using var owner = Hero();
        var host = new RemoveItemScriptTests.Host(owner);
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        owner.StandingItemScripts = () => host;
        var lines = new List<string>();
        owner.ItemRunOutput = (messages, _) => lines.AddRange(messages.Select(message => message.Text));
        var result = owner.AssembleSchematic(owner.Version, Schematic(), InBag, Facts, Kinds, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        CollectionAssert.AreEqual(new[] { $"line 2 of {RemoveScript}", $"line 2 of {RemoveScript}" }, lines, "one line a component, the hero's");
        Assert.IsTrue(owner.IsItemDestroyed("kit:1") && owner.IsItemDestroyed("kit:2") && ReadFlag(owner, 3) == true);
    }

    [TestMethod]
    public void A_scripted_component_with_no_host_refuses_the_assembly_and_keeps_the_components()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var refused = owner.AssembleSchematic(owner.Version, Schematic(), InBag, Facts, Kinds, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, revision), (refused.Disposition, refused.Failure, owner.Version.Revision));
        Assert.IsFalse(owner.IsItemDestroyed("kit:1"));
    }
}
