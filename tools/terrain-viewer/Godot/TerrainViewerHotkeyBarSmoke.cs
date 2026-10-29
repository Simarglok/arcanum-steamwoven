using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-027 native observation, inside the cast stage (a hero who knows <spell>; an automated run, which draws the bars only because
    // this smoke asks): pointer events go through the window's GUI. The bar shows the game's slot art; a right press on the spell's button
    // in the pane and a right release over slot 2 put the spell there with its icon; a left click on slot 2 opens the spell's cursor; a right
    // drag from slot 2 to slot 6 moves it and one from slot 6 off the bar empties it; an item entry shows its icon (ARC-UI-032: the
    // inventory art or the type icon, no name) and its count, and every item the hero holds has an icon; the latest recent action's button
    // shows its icon and a click on it opens its cursor with the bar's button sounds.
    private async Task RunAreaHotkeyBarSmokeAsync(GameSessionOwner session, int spell)
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-027 hotkey-bar smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-027 hotkey-bar smoke: " + why); } }
        var barForSelfTest = _gameBarForSelfTest;
        _gameBarForSelfTest = true; _gameBarSoundsForSelfTest.Clear();
        try
        {
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            async Task Button(Vector2 at, MouseButton button, bool pressed)
            {
                GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                GetViewport().PushInput(new InputEventMouseButton
                {
                    ButtonIndex = button, Pressed = pressed, Position = at, GlobalPosition = at,
                    ButtonMask = pressed ? (button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0,
                });
                await Frames(1);
            }
            async Task Drag(Vector2 from, Vector2 to) { await Button(from, MouseButton.Right, true); await Button(to, MouseButton.Right, false); }
            async Task Click(Vector2 at) { await Button(at, MouseButton.Left, true); await Button(at, MouseButton.Left, false); }
            // ARC-UI-035: a pointer move onto a slot or button (its enter message: from off the bar first, as the pointer may rest on it
            // already) after the log's quiet time, and one away from the bar.
            var away = new Vector2(40, 120);
            async Task Hover(Vector2 at, Vector2? from = null)
            {
                var start = from ?? away;
                GetViewport().PushInput(new InputEventMouseMotion { Position = start, GlobalPosition = start });
                await Frames(1);
                _messageClockShiftForSelfTest += AreaMessageLog.QuietMilliseconds + 1;
                if (at != away) { GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }); await Frames(2); }
            }
            var hovers = new List<string>();
            Texture2D? SpellArt(int number) => HotkeyBarRules.SpellIcon(number) is { } art ? Art(art)?[0] : null;

            CancelSpellTargeting(null);
            foreach (var slot in Enumerable.Range(0, HotkeyBarRules.Entries)) session.ClearHotkey(session.Version, slot);
            await Frames(3);
            Require(_gameBarPanel is { Visible: true } && _hotkeyBarView is { } && _hotkeyBarContent is { } content && content.ReadyCount == AreaHotkeyBarContent.ArtNumbers.Count,
                "the bar shows with its art: " + _hotkeyBarContent?.Detail);
            var view = _hotkeyBarView!;
            // The bars exist only from here in an automated run (they show because this smoke asks).
            var log = _gameBarPanel!.MessageLog;
            Require(Enumerable.Range(0, HotkeyBarRules.Entries).All(i => view.SlotArtForSelfTest(i) == Art(HotkeyBarRules.SlotArt)![0]), "every slot draws HKSlt1");

            // ARC-UI-053: the spell pane in the bottom strip (bar mode 1): its background, the colleges the hero has a rank in, the press of
            // the spell's college shows that college's known spells as icons, entering a button names it in the mode's text area, and the
            // release over the spell's icon activates it (the bar's button sounds).
            await RunSpellPaneStripSmokeAsync(session, spell, Frames, Click, Hover);

            // A right drag from the spell pane onto slot 2.
            ShowSpellPane();
            await Frames(3);
            var paneButton = _spellBarView!.SpellCentreForSelfTest(spell);
            Require(paneButton is not null && _spellPane!.SpellButtonCentreForSelfTest(spell) is null, $"the strip, not the diagnostic pane, shows spell {spell}");
            await Button(paneButton!.Value, MouseButton.Right, true);
            Require(_hotkeyDrag is { Kind: HotkeyDragSourceKind.Spell } && view.GhostShownForSelfTest, "a right press on the pane's spell starts a drag with its icon");
            await Button(view.SlotCentreForSelfTest(2), MouseButton.Right, false);
            Require(_hotkeyDrag is null && session.QueryHotkeys().Entries[2] == HotkeyEntry.ForSpell(spell), "the release over slot 2 put the spell there: " + _hotkeyNotice);
            await Frames(1);
            Require(view.IconForSelfTest(2) is { } icon && icon == SpellArt(spell), "slot 2 shows the spell's icon");
            _spellPane!.HidePane();
            await Frames(2);

            // ARC-UI-035: entering slot 2 shows the spell's hint (college icon, name, willpower and cost lines, description); leaving takes it away.
            await Hover(view.SlotCentreForSelfTest(2));
            Require(!_messageLog.PaneMode && log.ShownKindForSelfTest == AreaMessageKind.SpellHint && log.IconForSelfTest == Arcanum.Presentation.Hud.HotkeyBarRules.CollegeIcon(spell / 5)
                && log.DrawnTextsForSelfTest >= 3, $"entering slot 2 shows the spell's hint: {log.ShownKindForSelfTest} texts={log.DrawnTextsForSelfTest}");
            hovers.Add($"spell texts={log.DrawnTextsForSelfTest}");
            await Hover(away);
            Require(log.ShownKindForSelfTest != AreaMessageKind.SpellHint, "leaving slot 2 takes the hint away");
            // A skill-bar button's entry: its skill's hint; in the spells pane's mode only its name, in the pane's text area.
            Require(session.AssignHotkey(session.Version, 4, HotkeyEntry.ForSkill(SkillBarButton.Prowling)).Disposition == GameSessionDisposition.Changed, "prowling goes on entry 4");
            await Frames(2);
            await Hover(view.SlotCentreForSelfTest(4));
            Require(log.ShownKindForSelfTest == AreaMessageKind.SkillHint && log.DrawnTextsForSelfTest >= 1 && log.IconForSelfTest == 444,
                $"entering slot 4 shows the skill's hint: {log.ShownKindForSelfTest} texts={log.DrawnTextsForSelfTest}");
            hovers.Add($"skill texts={log.DrawnTextsForSelfTest}");
            await Hover(away);
            ShowSpellPane();
            await Frames(3);
            Require(_messageLog.PaneMode, "the spell pane holds the window");
            // From the gap between slots 4 and 5: a move above the strip would give the window back to the log (ML8).
            await Hover(view.SlotCentreForSelfTest(4), (view.SlotCentreForSelfTest(4) + view.SlotCentreForSelfTest(5)) / 2);
            Require(_messageLog.PaneMode, "the pane still holds the window");
            Require(log.PaneNameForSelfTest == _gameBarContent!.Texts.SkillButtonName(0) && log.ShownKindForSelfTest is null,
                $"in the spells mode only the name is written: '{log.PaneNameForSelfTest}'");
            // Leaving to the gap (inside the strip) keeps the name (0x550720 acts in the message mode only); hiding the pane takes it.
            var gap = (view.SlotCentreForSelfTest(4) + view.SlotCentreForSelfTest(5)) / 2;
            GetViewport().PushInput(new InputEventMouseMotion { Position = gap, GlobalPosition = gap });
            await Frames(2);
            Require(_messageLog.PaneMode && log.PaneNameForSelfTest.Length > 0, "leaving the slot in the pane mode keeps the name");
            _spellPane.HidePane();
            await Frames(3);
            Require(!_messageLog.PaneMode && log.PaneNameForSelfTest.Length == 0, "the pane's name goes with the pane");
            await Hover(away);
            session.ClearHotkey(session.Version, 4);

            // While slot 2 is dragged its key skips it (HK8, HK15); a right release on the slot itself ends the drag with no change.
            var keyed = _hotkeyActivations;
            await Button(view.SlotCentreForSelfTest(2), MouseButton.Right, true);
            HotkeyKey(Key.Key3);
            Require(_hotkeyDrag is not null && _hotkeyActivations == keyed && _spellTargeting is null, "the key 3 skipped the dragged entry: " + _hotkeyNotice);
            await Button(view.SlotCentreForSelfTest(2), MouseButton.Right, false);
            Require(_hotkeyDrag is null && session.QueryHotkeys().Entries[2] == HotkeyEntry.ForSpell(spell), "the drop on its own slot changed nothing");

            // A left click on slot 2 activates it.
            var activations = _hotkeyActivations;
            await Click(view.SlotCentreForSelfTest(2));
            Require(_spellTargeting?.Spell == spell && _hotkeyActivations == activations + 1, "a click on slot 2 opened the spell's cursor: " + _hotkeyNotice);
            CancelSpellTargeting(null);

            // A right drag between slots moves the entry; one off the bar empties it.
            await Drag(view.SlotCentreForSelfTest(2), view.SlotCentreForSelfTest(6));
            Require(session.QueryHotkeys().Entries[6] == HotkeyEntry.ForSpell(spell) && session.QueryHotkeys().Entries[2].IsEmpty, "the drag moved slot 2 to slot 6: " + _hotkeyNotice);
            await Frames(1);
            Require(view.IconForSelfTest(6) == SpellArt(spell) && view.IconForSelfTest(2) is null, "the icon moved with it");
            await Drag(view.SlotCentreForSelfTest(6), new Vector2(40, 120));
            Require(session.QueryHotkeys().Entries[6].IsEmpty && !view.GhostShownForSelfTest, "the drag off the bar emptied slot 6: " + _hotkeyNotice);

            // An item entry: its name stands in for the icon (not ready) and its count is printed.
            // A hero whose kit is all worn lends a worn piece other than the weapon for the check and wears it again after.
            var worn = session.QueryEquipment().Worn.ToDictionary(w => w.Entity, w => w.Slot, StringComparer.Ordinal);
            var held = session.QueryHeroKit().Select(kit => (kit.Entity, kit.Kind)).Concat(session.QueryPcInventory().Select(pc => (pc.Entity, pc.Kind)))
                .Where(item => item.Kind.Type is not (SessionItemType.Ammo or SessionItemType.Gold)).ToArray();
            var item = held.FirstOrDefault(h => !worn.ContainsKey(h.Entity)).Entity;
            Arcanum.Simulation.Equipment.EquipmentSlot? lent = null;
            if (item is null && held.FirstOrDefault(h => worn.TryGetValue(h.Entity, out var at) && at != Arcanum.Simulation.Equipment.EquipmentSlot.Weapon).Entity is { } piece)
            {
                lent = worn[piece];
                Require(session.Unequip(session.Version, lent.Value, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed, $"{piece} taken off for the item entry");
                item = piece;
            }
            string? itemCount = null, itemIcon = null;
            // ARC-UI-032: every item the hero holds (kit, taken, made) gets an icon: its inventory art or its type icon.
            var icons = session.QueryHeroKit().Select(h => h.Entity).Concat(session.QueryPcInventory().Select(h => h.Entity))
                .Concat(session.QueryMadeItems().Select(h => h.Entity)).Select(entity => (entity, HotkeyItemIcon(entity))).ToArray();
            var missing = icons.Where(pair => pair.Item2.Icon is not { Length: > 0 }).Select(pair => $"{pair.entity} ({pair.Item2.Detail})").ToArray();
            Require(icons.Length > 0 && missing.Length == 0, $"every held item has an icon ({icons.Length} items): {string.Join("; ", missing)}");
            var inventoryArts = icons.Count(pair => !pair.Item2.Detail.StartsWith("type icon", StringComparison.Ordinal));
            if (item is not null)
            {
                Require(session.AssignHotkey(session.Version, 3, HotkeyEntry.ForItem(item)).Disposition == GameSessionDisposition.Changed, $"item {item} goes on entry 3");
                await Frames(2);
                itemCount = view.CountForSelfTest(3);
                Require(itemCount == session.QueryHotkeyCount(3)?.ToString(System.Globalization.CultureInfo.InvariantCulture), $"slot 3 shows the count '{itemCount}'");
                // ARC-UI-032: the item's icon, not its name (HK17, HK18).
                var shown = HotkeyItemIcon(item);
                var slotIcon = view.IconForSelfTest(3);
                Require(slotIcon is not null && shown.Icon is { Length: > 0 } frames && slotIcon == frames[0] && view.StandInForSelfTest(3).Length == 0,
                    $"slot 3 shows the item's icon, not its name '{view.StandInForSelfTest(3)}': {shown.Detail}");
                Require(slotIcon!.GetWidth() <= HotkeyBarRules.IconSize && slotIcon.GetHeight() <= HotkeyBarRules.IconSize || shown.Detail.StartsWith("type icon", StringComparison.Ordinal),
                    $"an icon larger than a slot is a type icon: {slotIcon.GetWidth()}×{slotIcon.GetHeight()} {shown.Detail}");
                itemIcon = shown.Detail;
                // ARC-UI-035: entering the item's slot draws its item window (its type icon and its name).
                await Hover(view.SlotCentreForSelfTest(3));
                Require(log.ShownKindForSelfTest == AreaMessageKind.Item && log.IconForSelfTest is not null && log.DrawnTextsForSelfTest == 1,
                    $"entering slot 3 draws the item window: {log.ShownKindForSelfTest} icon={log.IconForSelfTest is not null} texts={log.DrawnTextsForSelfTest}");
                hovers.Add($"item colour={log.FirstTextColourForSelfTest.ToHtml(false)}");
                await Hover(away);
                Require(log.ShownKindForSelfTest != AreaMessageKind.Item, "leaving slot 3 takes the item window away");
                session.ClearHotkey(session.Version, 3);
                if (lent is { } slot) Require(WearFromHotkey(session, item, slot) is null, $"{item} worn again");
            }
            else GD.Print("ARC-UI-027 hotkey-bar smoke: the hero holds no item for an item entry");

            // The latest recent action's button: its icon, and a click activates it with the button sounds.
            var latest = session.QueryHotkeys().Recent[0];
            Require(latest.Kind == HotkeyKind.Spell, "the latest recent action is a spell: " + latest);
            await Frames(1);
            Require(view.RecentArtForSelfTest(0) == SpellArt(latest.Index), "the recent-action button shows the spell's icon");
            // ARC-UI-035: entering the recent-action button shows its spell's hint.
            await Hover(view.RecentCentreForSelfTest(0));
            Require(log.ShownKindForSelfTest == AreaMessageKind.SpellHint, "entering the recent action shows its hint: " + log.ShownKindForSelfTest);
            hovers.Add("recent spell");
            await Hover(away);
            _gameBarSoundsForSelfTest.Clear();
            activations = _hotkeyActivations;
            await Click(view.RecentCentreForSelfTest(0));
            Require(_spellTargeting?.Spell == latest.Index && _hotkeyActivations == activations + 1, "a click on the recent action opened its cursor: " + _hotkeyNotice);
            Require(_gameBarSoundsForSelfTest.Select(s => s.Id).SequenceEqual([AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound]),
                "the button played 3000 then 3001: " + string.Join(",", _gameBarSoundsForSelfTest.Select(s => s.Id)));
            CancelSpellTargeting(null);
            GD.Print($"ARC-UI-027 native-smoke hotkey-bar PASS spell={spell} item={item ?? "-"} count={itemCount ?? "-"} recent={latest.Index} art={_hotkeyBarContent!.ReadyCount}");
            GD.Print($"ARC-UI-035 native-smoke hotkey-hover PASS {string.Join("; ", hovers)} described={_hotkeyDescriptionsForSelfTest}");
            GD.Print($"ARC-UI-032 native-smoke hotkey-item-icons PASS items={icons.Length} inventory-art={inventoryArts} type-icon={icons.Length - inventoryArts} slot3={itemIcon ?? "-"}");
        }
        finally
        {
            HideHotkeyBar(); _spellPane?.HidePane();
            _gameBarForSelfTest = barForSelfTest; _messageClockShiftForSelfTest = 0;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
