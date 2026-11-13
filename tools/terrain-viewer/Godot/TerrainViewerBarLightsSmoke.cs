using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-1364 (ledger GB15), part of the bar smoke (ARC_AREA_MAP_SMOKE_ONLY=bar): the top strip's buttons light from game events through the
    // production route (the session's writes and the level-up hook), the art on the button is the lit art, the button's own window puts the light
    // out and a light is not stored while that window is open; the Map button shows the world map art in a sector without a town map.
    private async Task RunAreaBarLightsSmokeAsync(AreaGameBarPanel panel, Func<int, Task> frames, Func<AreaBarButton, Task> click)
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-1364 bar lights smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-1364 bar lights smoke: " + why); } }
        var session = _areaSession ?? throw new InvalidOperationException("ARC-UI-1364 bar lights smoke: no session");
        if (AnyWindowOpen) { CloseOtherInterfaceWindows(); await frames(2); }
        await frames(2);
        // The Map button's face follows the hero's sector: no town map shows the world map art 194, a town map the town art 193 (0x564F60).
        var townMap = PcTownMapNumber() > 0;
        var idleMap = townMap ? 193 : 194;
        Require(panel.ArtForSelfTest(AreaBarButton.Map) == idleMap && _gameBar.Lights.MapMode == (townMap ? AreaLightSlot.TownMap : AreaLightSlot.WorldMap),
            $"the Map button shows art {idleMap} (town map {townMap}), drew {panel.ArtForSelfTest(AreaBarButton.Map)}");
        // Start from a clean state: every light out (the earlier steps of this smoke opened and closed the windows).
        foreach (var slot in new[] { AreaLightSlot.Character, AreaLightSlot.Logbook, AreaLightSlot.Inventory, AreaLightSlot.WorldMap }) _gameBar.Lights.Light(slot, false, true);
        _gameBarPanel!.Render();
        Require(panel.ArtForSelfTest(AreaBarButton.Character) == 169 && panel.ArtForSelfTest(AreaBarButton.Logbook) == 187 && panel.ArtForSelfTest(AreaBarButton.Inventory) == 186,
            "the buttons start idle");
        var lit = _barLightsLit;

        // Character: a level-up lights it (Char_ON, art 561) until the sheet is opened; opening the sheet puts it out; a level-up while it is open lights nothing.
        var award = session.AwardHeroExperience(session.Version, 4000).Award;
        Require(award?.LevelUps is { Count: > 0 }, "an award of 4000 writes a level");
        LevelAreaFollowers(award);
        await frames(2);
        Require(_gameBar.Lights.IsLit(AreaLightSlot.Character) && panel.ArtForSelfTest(AreaBarButton.Character) == 561, $"a level-up lit the Character button: art {panel.ArtForSelfTest(AreaBarButton.Character)}");
        await click(AreaBarButton.Character);
        Require(SheetOpen, "the Character button opened the sheet: " + _gameBarNotice);
        await frames(2);
        Require(!_gameBar.Lights.IsLit(AreaLightSlot.Character) && panel.ArtForSelfTest(AreaBarButton.Character) == 169, $"the open sheet put it out: art {panel.ArtForSelfTest(AreaBarButton.Character)}");
        LevelAreaFollowers(session.AwardHeroExperience(session.Version, 20000).Award);
        await frames(2);
        Require(!_gameBar.Lights.IsLit(AreaLightSlot.Character) && panel.ArtForSelfTest(AreaBarButton.Character) == 169, "a level-up with the sheet open lit nothing");
        await click(AreaBarButton.Character);
        Require(!SheetOpen && panel.ArtForSelfTest(AreaBarButton.Character) == 169, "closing the sheet left it idle");

        // Logbook: a quest written lights it (art 560) until the logbook is opened; a quest written with the logbook open lights nothing.
        var quests = Enumerable.Range(1000, 40).Where(number => session.QueryQuest(number) is null).Take(2).ToArray();
        Require(quests.Length == 2, "two quests the hero has no record of");
        Require(session.SetQuestState(session.Version, quests[0], SessionQuestState.Accepted).Disposition == GameSessionDisposition.Changed, "a quest was accepted");
        await frames(2);
        Require(_gameBar.Lights.IsLit(AreaLightSlot.Logbook) && panel.ArtForSelfTest(AreaBarButton.Logbook) == 560, $"a quest lit the Logbook button: art {panel.ArtForSelfTest(AreaBarButton.Logbook)}");
        await click(AreaBarButton.Logbook);
        Require(LogbookOpen, "the Logbook button opened the logbook: " + _gameBarNotice);
        await frames(2);
        Require(panel.ArtForSelfTest(AreaBarButton.Logbook) == 187, "the open logbook put it out");
        Require(session.SetQuestState(session.Version, quests[1], SessionQuestState.Accepted).Disposition == GameSessionDisposition.Changed, "a second quest was accepted");
        await frames(2);
        Require(!_gameBar.Lights.IsLit(AreaLightSlot.Logbook) && panel.ArtForSelfTest(AreaBarButton.Logbook) == 187, "a quest with the logbook open lit nothing");
        await click(AreaBarButton.Logbook);
        Require(!LogbookOpen && panel.ArtForSelfTest(AreaBarButton.Logbook) == 187, "closing the logbook left it idle");

        // Inventory: an object taken into the hero's list lights it (art 559) until the inventory is opened.
        var ground = session.QueryMapInventory(new(CurrentAreaMap))?.Ground ?? [];
        Require(ground.Count > 0, "the ground holds an object for the hero to take");
        Require(session.PickUpItem(session.Version, ground[0].Item.Entity, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed, "the hero took an object");
        await frames(2);
        Require(_gameBar.Lights.IsLit(AreaLightSlot.Inventory) && panel.ArtForSelfTest(AreaBarButton.Inventory) == 559, $"a taken object lit the Inventory button: art {panel.ArtForSelfTest(AreaBarButton.Inventory)}");
        await click(AreaBarButton.Inventory);
        Require(InventoryOpen, "the Inventory button opened the inventory: " + _gameBarNotice);
        await frames(2);
        Require(panel.ArtForSelfTest(AreaBarButton.Inventory) == 186, "the open inventory put it out");
        await click(AreaBarButton.Inventory);
        Require(!InventoryOpen && panel.ArtForSelfTest(AreaBarButton.Inventory) == 186, "closing the inventory left it idle");

        // Map: a newly known area lights the world map face (art 195, drawn while that face shows) until the map is opened.
        var area = Enumerable.Range(1, SessionStoryLimits.LastArea).FirstOrDefault(number => !session.IsAreaKnown(number));
        Require(area != 0 && session.DiscoverArea(session.Version, area).Disposition == GameSessionDisposition.Changed, "an unknown area became known");
        await frames(2);
        Require(_gameBar.Lights.IsLit(AreaLightSlot.WorldMap), "the new area lit the world map slot");
        Require(panel.ArtForSelfTest(AreaBarButton.Map) == (townMap ? 193 : 195), $"the Map button draws art {(townMap ? 193 : 195)} (the town face stays idle), drew {panel.ArtForSelfTest(AreaBarButton.Map)}");
        await click(AreaBarButton.Map);
        await frames(1);
        Require(WorldMapOpen, "the Map button opened the map: " + _gameBarNotice);
        CloseWorldMapByPlayer();
        await frames(2);
        Require(!_gameBar.Lights.IsLit(AreaLightSlot.WorldMap) && panel.ArtForSelfTest(AreaBarButton.Map) == idleMap, $"the opened map put it out: art {panel.ArtForSelfTest(AreaBarButton.Map)}");

        Require(_barLightsLit == lit + 4, $"four lights in all (Character, Logbook, Inventory, world map; none with a window open): {_barLightsLit - lit}");
        GD.Print($"ARC-UI-1364 native-smoke bar-lights PASS character=561 logbook=560 inventory=559 map={(townMap ? "town 193" : "world 194 lit 195")}");
    }
}
