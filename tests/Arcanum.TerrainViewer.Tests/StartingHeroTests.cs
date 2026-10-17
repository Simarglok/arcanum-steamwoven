using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-015: an original pre-made hero becomes the session's player character and decides which of Virgil's original
/// DLG replies are offered. The viewer still talks with its diagnostic profile; this checks the Data → Session → Scripting
/// seam on real data, with the conversation host given the hero's facts. Opt-in: ARC_AREA_CONVERSATION_GAME_ROOT names the
/// user-owned installation (read-only). Structural identifiers only; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class StartingHeroTests
{
    [TestMethod, TestCategory("RealData")]
    public void Original_pre_made_heroes_start_the_session_and_gate_virgils_replies()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT is required.");
        string P(string relative) => Path.Combine(root, relative);
        var sources = Sources(root);
        using var data = AreaOriginalValidator.OpenFiles(P("modules/Arcanum.dat"), P("arcanum1.dat"), P("arcanum2.dat"), P("modules/Arcanum/maps/qrmvs-420-1zjcnpgn"), P("data/proto"));
        var snapshot = data.LoadStrict();
        var document = VirgilDialog(P("arcanum3.dat"));

        // 16069, the half-elf man: race effect 67 raises DX, BE and magick points, lowers CN; its four tech-skill changes and
        // age are outside the character owner. Set 20: 385 gold, a weapon and two armours.
        using var halfElf = Start(sources, 16069, snapshot.RuntimeMap, out var dropped);
        Assert.AreEqual(5, dropped, "four tech skills and age");
        var stats = halfElf.QueryHeroStats(new(0))!.Value.Snapshot!;
        Assert.AreEqual((9, 9, 7, 8, 1), (stats.GetEffective(CharacterStat.Dexterity), stats.GetEffective(CharacterStat.Beauty),
            stats.GetEffective(CharacterStat.Constitution), stats.GetEffective(CharacterStat.Intelligence), stats.GetEffective(CharacterStat.MagickPoints)));
        Assert.AreEqual((16089, 1, 5, 0), (halfElf.QueryHero()!.Prototype, halfElf.QueryHero()!.Level, halfElf.QueryHero()!.UnspentPoints,
            halfElf.QueryHero()!.Alignment));
        Assert.AreEqual(385, halfElf.QueryGold());
        CollectionAssert.AreEqual(new[] { ("kit:1", SessionItemType.Weapon, 6049), ("kit:2", SessionItemType.Armor, 8188), ("kit:3", SessionItemType.Armor, 8069) },
            halfElf.QueryHeroKit().Select(item => (item.Entity, item.Kind.Type, item.Kind.Prototype)).ToArray());
        Assert.IsTrue(halfElf.QueryHeroKit().All(item => item.Kind.Name > 0));
        Assert.AreEqual(0, halfElf.QueryPcInventory().Count, "nothing taken from the world");
        Assert.AreEqual(new DialogPcFacts(DialogPcGender.Male, 8, false, 3), halfElf.QueryHeroDialogFacts());

        // 16070, the half-ogre man: intelligence 8 − 4 = 4 is at the low-intelligence threshold.
        using var halfOgre = Start(sources, 16070, snapshot.RuntimeMap, out _);
        Assert.AreEqual(new DialogPcFacts(DialogPcGender.Male, 4, false, 7), halfOgre.QueryHeroDialogFacts());
        Assert.AreEqual(12, halfOgre.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Strength));

        // 16075, the human woman: no race change, female effect 330 (ST −1, CN +1), set 23 with 300 gold.
        using var woman = Start(sources, 16075, snapshot.RuntimeMap, out _);
        Assert.IsNull(woman.QueryHero()!.RaceEffect);
        Assert.AreEqual(330, woman.QueryHero()!.GenderEffect!.EffectId);
        Assert.AreEqual((7, 9), (woman.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Strength),
            woman.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Constitution)));
        Assert.AreEqual((300, 3), (woman.QueryGold(), woman.QueryHeroKit().Count));

        // Virgil's first talk (SCR line 4 → DLG node 1) walked with each hero's facts, choosing as the viewer test does.
        CollectionAssert.AreEqual(new[] { "1:4,7", "9:10,11", "15:16,17", "19:20,21,22,23", "24:25", "30:31", "32:33", "35:36,40,48,53",
            "57:58,59,63", "71:72,73", "120:121,122", "210:211,212", "74:75,76" }, Walk(document, snapshot, root, halfElf));
        CollectionAssert.AreEqual(new[] { "1:5", "9:12,13", "220:221", "222:223", "225:226,227", "228:229,230", "74:77" },
            Walk(document, snapshot, root, halfOgre), "the low-intelligence replies");
        var womanWalk = Walk(document, snapshot, root, woman);
        CollectionAssert.Contains(womanWalk, "24:26", "a reply for women");
        CollectionAssert.Contains(womanWalk, "35:36,37,45,53", "replies for a human woman");

        // The hero survives a save and load with its stats, gold and kit.
        using var loaded = ConversationSessionFixture.SaveAndLoad(halfElf);
        Assert.AreEqual(halfElf.QueryHeroDialogFacts(), loaded.QueryHeroDialogFacts());
        Assert.AreEqual(9, loaded.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Dexterity));
        Assert.AreEqual(385, loaded.QueryGold());
        CollectionAssert.AreEqual(halfElf.QueryHeroKit().ToArray(), loaded.QueryHeroKit().ToArray());
    }

    /// <summary>A new session at the area spawn with the pre-made hero <paramref name="basic"/> (fresh identities, the
    /// viewer's synthetic clock, the original 3,200 new-game flags).</summary>
    private static GameSessionOwner Start(PremadeHeroSources sources, int basic, int runtimeMap, out int dropped)
    {
        var read = PremadeHeroReader.Read(sources, basic);
        Assert.AreEqual(PremadeHeroFailure.None, read.Failure, $"{basic} {read.Detail}");
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var spawn = AreaGridSession.Cell(AreaGridSession.Spawn);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, OriginalGlobalFlags.NewGame(identity, new OwnerEpoch(Guid.NewGuid())),
            new(new(runtimeMap), new(checked((int)spawn.X), checked((int)spawn.Y)), NeighborDirection.XPlus), Hero: StartingHeroMapping.Definition(read.Hero!, out dropped)));
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    // Each node with its offered replies; the first unblocked Direct reply is chosen, else the first unblocked one.
    private static string[] Walk(DlgDocument document, AreaSnapshot snapshot, string root, GameSessionOwner session)
    {
        var host = new AreaConversationHost(snapshot, null, session,
            AreaConversationProfile.Diagnostic with { Pc = session.QueryHeroDialogFacts()! });
        var talk = DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(1),
                new DialogParticipants(new DialogId(1324), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                new DialogScriptLine(4), host.Profile.Pc, host.Profile.Names),
            new DialogConversationPorts(host.State, host.State, session.Flags, host.State));
        var nodes = new List<string>();
        while (talk.Phase == DialogConversationPhase.AwaitingSelection && nodes.Count < 64)
        {
            nodes.Add($"{talk.CurrentNode.Value}:{string.Join(",", talk.Options.Select(option => option.ResponseId.Value))}");
            var option = talk.Options.FirstOrDefault(o => o.Block is null && o.Transition == DialogOptionTransition.Direct)
                ?? talk.Options.First(o => o.Block is null);
            talk.Select(option.Token);
        }
        Assert.AreEqual(DialogConversationPhase.Ended, talk.Phase);
        return nodes.ToArray();
    }

    private static PremadeHeroSources Sources(string root)
    {
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        MesFile? Table(string path)
        {
            var resolution = stack.Resolve(path);
            if (resolution.Status != ContentResolutionStatus.Resolved) return null;
            var parsed = MesFile.Parse(stack.Read(resolution.Candidates[0]).Bytes);
            Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, path);
            return parsed.File;
        }
        return new(PremadeHeroReader.DirectoryPrototypes(Path.Combine(root, "data", "proto")), Table("rules/effect.mes")!,
            Table("rules/InvenSource.mes")!, Table("rules/InvenSourceBuy.mes"));
    }

    // Virgil's DLG (script 1324) from the pinned dialog archive; ARC-RES-026 records its payload hash 34a49a2c…5e53a.
    private static DlgDocument VirgilDialog(string archivePath)
    {
        using var archive = DatArchive.Open(archivePath, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        var member = archive.Entries.Single(entry =>
            Encoding.Latin1.GetString(entry.RawNameBytes.ToArray()).Contains("01324", StringComparison.Ordinal));
        var bytes = archive.ReadEntry(member);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.IsTrue(hash.StartsWith("34a49a2c", StringComparison.Ordinal) && hash.EndsWith("5e53a", StringComparison.Ordinal), "pinned Virgil DLG");
        return DlgReader.Read(bytes);
    }
}

/// <summary>
/// The host mapping from a pre-made hero (Arcanum.Data) to the session's hero definition, kept with the tests until the
/// viewer is wired (as the ARC-WORLD-014 seed mapping is). Level-1 defaults per <see cref="PremadeHero"/>; effect changes
/// on stats the character owner models (primary, derived, magick and tech points) are kept, the others (skills,
/// resistances, tech disciplines, special values, level, alignment, age …) are dropped and counted.
/// </summary>
internal static class StartingHeroMapping
{
    private static readonly CharacterRace[] Races =
    [
        CharacterRace.Human, CharacterRace.Dwarf, CharacterRace.Elf, CharacterRace.HalfElf, CharacterRace.Gnome, CharacterRace.Halfling,
        CharacterRace.HalfOrc, CharacterRace.HalfOgre, CharacterRace.DarkElf, CharacterRace.Ogre, CharacterRace.Orc,
    ];

    // Source stat index → owner stat: 0..7 primary, 8..16 derived, 22 magick points, 23 tech points.
    private static CharacterStat? Stat(int source) => source switch
    {
        0 => CharacterStat.Strength, 1 => CharacterStat.Dexterity, 2 => CharacterStat.Constitution, 3 => CharacterStat.Beauty,
        4 => CharacterStat.Intelligence, 5 => CharacterStat.Perception, 6 => CharacterStat.Willpower, 7 => CharacterStat.Charisma,
        8 => CharacterStat.CarryWeight, 9 => CharacterStat.DamageBonus, 10 => CharacterStat.AcAdjustment, 11 => CharacterStat.Speed,
        12 => CharacterStat.HealRate, 13 => CharacterStat.PoisonRecovery, 14 => CharacterStat.ReactionModifier,
        15 => CharacterStat.MaxFollowers, 16 => CharacterStat.MagickTechAptitude, 22 => CharacterStat.MagickPoints,
        23 => CharacterStat.TechPoints, _ => null,
    };

    internal static SessionHeroDefinition Definition(PremadeHero hero, out int dropped)
    {
        var count = 0;
        StatEffectDefinition? Effect(EffectRule? rule)
        {
            if (rule is null) return null;
            var modifiers = new List<StatModifier>();
            foreach (var change in rule.Changes)
            {
                if (change.Kind != EffectAttributeKind.Stat || Stat(change.Attribute) is not { } stat)
                {
                    count++;
                    continue;
                }
                modifiers.Add(new(stat, change.Operator switch
                {
                    EffectChangeOperator.Add => StatModifierOperator.Add, EffectChangeOperator.Percent => StatModifierOperator.Percent,
                    EffectChangeOperator.Minimum => StatModifierOperator.Minimum, _ => StatModifierOperator.Maximum,
                }, change.Value));
            }
            return modifiers.Count == 0 ? null : new(rule.Number, [.. modifiers]);
        }
        var raceEffect = Effect(hero.RaceEffect);
        var genderEffect = Effect(hero.GenderEffect);
        dropped = count;
        return new(hero.Prototype, Races[hero.Race], hero.Gender == 0 ? CharacterGender.Female : CharacterGender.Male,
            [.. Enumerable.Repeat(PremadeHero.StartPrimaryStat, 8), 0, 0], PremadeHero.StartLevel, PremadeHero.StartUnspentPoints, hero.Alignment,
            raceEffect, genderEffect, hero.Gold,
            [.. hero.Items.Select(item => new SessionHeroKitItem(new((SessionItemType)item.Type, item.Prototype, item.Name, item.Description)))]);
    }
}
