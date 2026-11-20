using System.Text.Json;
using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2461 (SWI-RUN-2, SWI-RUN-3, SWI-RUN-5, SWI-VER-6): the variant "kit" of the Steamwoven bar smoke (ARC_STEAMWOVEN_VARIANTS=kit, ARC_AREA_BAR_STEAMWOVEN=1 with ARC_HD_PACK). No baked kit exists yet (the production batches
    // have not run), so the smoke writes a synthetic one: every part the runtime composes, flat colours, baked at the step of each size (a manifest and the PNGs, as bake.py writes them), into a temporary folder it points the kit store at.
    // For each size of ARC_STEAMWOVEN_SIZES (the TRD's verification sizes) it checks that the strips and their caps are the kit's parts in the plan's rectangles (read back by pixel), that a window's kit background (shown with no window
    // over it, by the host's smoke override) tiles the zone and is the right pieces, that only that background is resident and it goes with the window, and the bar button's layers. At the first size it also removes a cap and checks the
    // fallback (that strip is the pack's, the other the kit's), and at the end the HD option off leaves no trace of the kit. Captures: PREFIX-hud-steamwoven-kit-WxH[-window].png (local, never in git).
    private (SteamwovenBackdropFamily Family, SteamwovenKitWindow? Window)? _smokeKitBackdrop;

    private static readonly Color KitTop = new(0.85f, 0.15f, 0.15f), KitBottom = new(0.15f, 0.15f, 0.85f), KitCapLeft = new(0.95f, 0.8f, 0.1f), KitCapRight = new(0.1f, 0.8f, 0.95f),
        KitDoll = new(0.15f, 0.6f, 0.15f), KitInventory = new(0.6f, 0.15f, 0.6f), KitPlate = new(0.9f, 0.5f, 0.1f), KitOptionsPlate = new(0.1f, 0.5f, 0.5f), KitHover = new(0.9f, 0.9f, 0.9f);

    /// <summary>The synthetic kit: part, master size and colour. The strips are the manifest's provisional 150 and 570 px; the caps are 120 px wide.</summary>
    private static IReadOnlyList<(string Id, int Width, int Height, Color Colour)> SyntheticKitParts { get; } =
    [
        ("board.inttop", 2880, 150, KitTop), ("board.intbotom", 2880, 570, KitBottom),
        ("cap.top_strip_left", 120, 150, KitCapLeft), ("cap.top_strip_right", 120, 150, KitCapRight),
        ("cap.bottom_strip_left", 120, 570, KitCapLeft), ("cap.bottom_strip_right", 120, 570, KitCapRight),
        ("backdrop.pdoll", 1290, 1440, KitDoll), ("backdrop.inventor", 1590, 1440, KitInventory),
        ("backdrop.mainmenuback", 2880, 2160, KitPlate), ("backdrop.optionsmenuback", 2880, 2160, KitOptionsPlate),
        ("plate.char_but", 120, 120, KitPlate), ("plate.char_but_hover", 120, 120, KitHover),
    ];

    /// <summary>Writes the synthetic set of <paramref name="step"/> under <paramref name="root"/> (parts named in <paramref name="without"/> left out, their records too).</summary>
    private static void WriteSyntheticKitSet(string root, SteamwovenStep step, params string[] without)
    {
        var directory = System.IO.Path.Combine(root, SteamwovenKitSet.FolderName(step));
        System.IO.Directory.CreateDirectory(directory);
        var scale = SteamwovenGrid.StepScale(step);
        var records = new List<object>();
        foreach (var (id, width, height, colour) in SyntheticKitParts)
        {
            if (without.Contains(id)) continue;
            var (baked, bakedHeight) = (SteamwovenGrid.BakedLength(width, step), SteamwovenGrid.BakedLength(height, step));
            using var image = Image.CreateEmpty(baked, bakedHeight, false, Image.Format.Rgba8);
            image.Fill(colour);
            if (image.SavePng(System.IO.Path.Combine(directory, id + ".png")) != Error.Ok) throw new InvalidOperationException("ARC-UI-2461 synthetic kit part not saved: " + id);
            records.Add(new Dictionary<string, object> { ["id"] = id, ["bake_version"] = 1, ["master"] = new Dictionary<string, object> { ["size"] = new[] { width, height }, ["sha256"] = "synthetic" },
                ["size"] = new[] { baked, bakedHeight }, ["bytes"] = 0, ["sha256"] = "synthetic", ["pixels_sha256"] = "synthetic" });
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new Dictionary<string, object>
        { ["schema"] = 1, ["bake_version"] = 1, ["step"] = SteamwovenKitSet.FolderName(step), ["ratio"] = new[] { scale.Numerator, scale.Denominator }, ["parts"] = records }));
    }

    /// <summary>ARC-UI-2461 (SWI-BDR-6): the full-screen plates of the column menu pass (ARC_STEAMWOVEN_KIT_SYNTHETIC=1 on the menu smoke): the synthetic kit's main and options plates are the ones drawn, at the plate's corners, and
    /// the plate is nearest-sampled exactly where its baked pixels are the device pixels.</summary>
    private async Task CheckKitPlateAsync(string tag, SteamwovenFullScreenPlan layout, string screen, AreaMenuArtView art, Func<int, Task> frames, Action<bool, string> require)
    {
        var (expectedId, colour) = screen == "main" ? ("backdrop.mainmenuback", KitPlate) : ("backdrop.optionsmenuback", KitOptionsPlate);
        art.Place(); art.QueueRedraw();
        await frames(3);
        require(art.KitPlateForSelfTest == expectedId, $"[{tag}] the {screen} window draws the kit plate {expectedId}, got {art.KitPlateForSelfTest}");
        var exact = SteamwovenKitStore.SetFor(layout.Layout.Step) is { } set && set.TryGet(expectedId, out var part) && part.Width == layout.Zone.Width && part.Height == layout.Zone.Height;
        require(art.TextureFilter == (exact ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear), $"[{tag}] the plate is sampled {(exact ? "nearest" : "linear")} as its draw is {(exact ? "exact" : "scaled")}");
        using var image = GetViewport().GetTexture().GetImage();
        var ratio = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
        Color Pixel(int x, int y) => image.GetPixel(Math.Clamp((int)(x * ratio.X), 0, image.GetWidth() - 1), Math.Clamp((int)(y * ratio.Y), 0, image.GetHeight() - 1));
        var (topLeft, bottomRight) = (Pixel(layout.Zone.X + 8, layout.Zone.Y + 8), Pixel(layout.Zone.Right - 8, layout.Zone.Bottom - 8));
        require(Near(topLeft, colour) && Near(bottomRight, colour), $"[{tag}] the plate's corners are the kit plate's colour (got {topLeft} and {bottomRight})");
    }

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < 0.02f && Math.Abs(a.G - b.G) < 0.02f && Math.Abs(a.B - b.B) < 0.02f;

    private async Task CaptureSteamwovenKitAsync(AreaGameBarPanel panel, Window window, Func<int, Task> frames, Action<bool, string> require)
    {
        var root = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-").FullName;
        var fallbackRoot = System.IO.Directory.CreateTempSubdirectory("arc-steamwoven-kit-fallback-").FullName;
        var (savedHd, savedGenerated) = (_gameSettings.HdInterface, _gameSettings.GeneratedInterface);
        var done = new HashSet<(string, SteamwovenStep)>();
        void Ensure(string where, string folder, SteamwovenStep step, params string[] without)
        {
            if (done.Add((where, step))) WriteSyntheticKitSet(folder, step, without);
        }
        try
        {
            SteamwovenHud.Options = SteamwovenLayoutOptions.ColumnFitted;
            var first = true;
            foreach (var size in SteamwovenSizes())
            {
                var plan0 = SteamwovenHud.Plan(size.X, size.Y, SteamwovenLayoutOptions.ColumnFitted);
                require(plan0 is not null, $"[kit {size.X}x{size.Y}] the layout is supported");
                var step = plan0!.Layout.Step;
                Ensure("kit", root, step);
                SteamwovenKitStore.UseRoot(root);
                window.ContentScaleSize = size;
                await FitWindowToRenderTargetAsync(panel, window, size, frames, require);
                for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                require((Vector2I)window.GetVisibleRect().Size == size, $"[kit {size.X}x{size.Y}] the viewport is {size}, got {window.GetVisibleRect().Size}");
                panel.Relayout();
                for (var wait = 0; wait < 1800 && (InterfaceHd.GeneratedPending > 0 || panel.BoardsPendingForSelfTest || !AllStripArtGenerated(panel)); wait++) await frames(1);
                await frames(3);
                var tag = $"kit-{size.X}x{size.Y}";
                var plan = panel.PlanForSelfTest;
                require(plan is not null && plan.Layout == plan0.Layout, $"[{tag}] the panel is laid out by the column plan");
                var column = plan!.Column!;
                // AC1 (SWI-LAY-4): the set of the step the layout chose is the one read; the plan's step is the smallest set at least f.
                require(SteamwovenKitStore.SetFor(step) is { Step: var readStep } && readStep == step, $"[{tag}] the kit's {SteamwovenKitSet.FolderName(step)} set is read");
                var (top, bottom, _, boards, caps) = panel.KitForSelfTest;
                require(top is not null && bottom is not null, $"[{tag}] both strips are drawn from the kit");
                require(top!.Board.Rect == column.TopStrip && bottom!.Board.Rect == column.BottomStrip, $"[{tag}] the strips' art is the plan's rectangles {column.TopStrip} and {column.BottomStrip}");
                require(!panel.GetNode<CanvasItem>("TopStrip/Art").Visible && !panel.GetNode<CanvasItem>("BottomStrip/Art").Visible && boards.Visible, $"[{tag}] the pack's strip art gives way to the kit's");
                // SWI-LAY-5: the filter follows exactness (the strips' 150 and 570 master px are not the 148 and 572 device rows: scaled, linear).
                var exactBoards = boards.AllExact;
                require(boards.TextureFilter == (exactBoards ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear), $"[{tag}] the strip art is sampled {(exactBoards ? "nearest" : "linear")} as its draw is {(exactBoards ? "exact" : "scaled")}");
                await frames(2);
                using (var image = GetViewport().GetTexture().GetImage())
                {
                    Color Pixel(int x, int y) => image.GetPixel(Math.Clamp(x, 0, image.GetWidth() - 1), Math.Clamp(y, 0, image.GetHeight() - 1));
                    var (t, b) = (column.TopStrip, column.BottomStrip);
                    require(Near(Pixel(t.X + 1, t.Y + t.Height / 2), KitTop), $"[{tag}] the top strip's pixels are the kit art's colour (got {Pixel(t.X + 1, t.Y + t.Height / 2)})");
                    require(Near(Pixel(b.X + 1, b.Y + b.Height / 2), KitBottom), $"[{tag}] the bottom strip's pixels are the kit art's colour (got {Pixel(b.X + 1, b.Y + b.Height / 2)})");
                    // The caps stand beside the strips on the world; at 4:3 there is no side and they are clipped away (SWI-LAY-3).
                    foreach (var draw in caps.DrawsForSelfTest)
                    {
                        var clipped = draw.Clip.Width == 0;
                        var left = draw.PartId.EndsWith("_left", StringComparison.Ordinal);
                        if (clipped) { require(column.Column.X <= 0 && column.Column.Right >= size.X, $"[{tag}] {draw.PartId} is clipped away only where the composition has no side"); continue; }
                        var probe = Pixel(left ? draw.Clip.Right - 1 : draw.Clip.X, draw.Clip.Y + draw.Clip.Height / 2);
                        require(Near(probe, left ? KitCapLeft : KitCapRight), $"[{tag}] {draw.PartId}'s pixels are the kit cap's colour (got {probe})");
                    }
                }
                // The bar button: the Character button's plate is the kit's face (the buttons' look overlays need a look the smoke does not press).
                require(panel.KitButtonForSelfTest(AreaBarButton.Character).Count >= 1, $"[{tag}] the Character button is drawn from the kit's plate");
                // AC: the covered window's background, with no window over it: the pieces tile the zone and meet at the 1290 px split; the world beside is dimmed by the layer as with a window.
                _smokeKitBackdrop = (SteamwovenBackdropFamily.Inventory, SteamwovenKitWindow.Inventory);
                await frames(3);
                var background = panel.KitForSelfTest.Background;
                var zone = SteamwovenWindowZone.ZoneOf(plan);
                require(background.Count == 2 && background[0].Rect.X == zone.X && background[1].Rect.Right == zone.Right && background[0].Rect.Right == background[1].Rect.X
                    && background.All(piece => piece.Rect.Y == zone.Y && piece.Rect.Bottom == zone.Bottom), $"[{tag}] the inventory's kit background tiles the zone {zone} in two pieces, got {string.Join(" + ", background.Select(piece => piece.Rect))}");
                var shown = panel.BackdropsForSelfTest;
                require(shown[0].Visible && shown[1].Visible && !shown[2].Visible && !shown[3].Visible, $"[{tag}] two backdrop pieces show");
                for (var i = 0; i < 2; i++)
                    require(shown[i].Position == new Vector2(background[i].Rect.X, background[i].Rect.Y) && shown[i].Size == new Vector2(background[i].Rect.Width, background[i].Rect.Height)
                        && shown[i].Pixels == new Vector2I(SteamwovenGrid.BakedLength(i == 0 ? 1290 : 1590, step), SteamwovenGrid.BakedLength(1440, step)), $"[{tag}] backdrop piece {i} is the baked part at the draw's rectangle");
                require(shown.Take(2).All(piece => piece.Filter == (background.All(draw => draw.Exact) ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear)), $"[{tag}] the pieces' sampling follows exactness");
                await CaptureHudAsync("steamwoven-" + tag + "-window", frames);
                using (var image = GetViewport().GetTexture().GetImage())
                {
                    Color Pixel(int x, int y) => image.GetPixel(Math.Clamp(x, 0, image.GetWidth() - 1), Math.Clamp(y, 0, image.GetHeight() - 1));
                    // The generated brass edge of the open menu's middle band (the kit has no cap for it) lies over the outermost few pixels: the probes stand inside it.
                    var y0 = zone.Y + zone.Height / 2;
                    require(Near(Pixel(zone.X + 16, y0), KitDoll), $"[{tag}] the zone's left is the paper-doll piece (got {Pixel(zone.X + 16, y0)})");
                    require(Near(Pixel(zone.Right - 16, y0), KitInventory), $"[{tag}] the zone's right is the inventory piece (got {Pixel(zone.Right - 16, y0)})");
                    require(Near(Pixel(background[1].Rect.X - 1, y0), KitDoll) && Near(Pixel(background[1].Rect.X, y0), KitInventory), $"[{tag}] the pieces meet at the split with no gap");
                }
                // SWI-RUN-5: only the open window's background is in memory.
                require(SteamwovenKitStore.ResidentForSelfTest.Count(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)) == 2, $"[{tag}] only the two pieces of the open window are resident");
                _smokeKitBackdrop = null;
                await frames(3);
                require(panel.KitForSelfTest.Background.Count == 0 && !SteamwovenKitStore.ResidentForSelfTest.Any(path => System.IO.Path.GetFileName(path).StartsWith("backdrop.", StringComparison.Ordinal)), $"[{tag}] the background goes with the window and is released");
                // The real window path: the inventory on the interface stack names the kit window through the host.
                OpenInventory(); await frames(4);
                require(InventoryOpen && panel.KitForSelfTest.Background.Count == 2, $"[{tag}] the open inventory takes its kit background (got {panel.KitForSelfTest.Background.Count} pieces)");
                EndInventory(); await frames(3);
                await CaptureHudAsync("steamwoven-" + tag, frames);
                if (first)
                {
                    first = false;
                    // SWI-RUN-3: a strip with a part missing is the generated chrome's, the other strip is the kit's.
                    Ensure("fallback", fallbackRoot, step, "cap.top_strip_left");
                    SteamwovenKitStore.UseRoot(fallbackRoot);
                    panel.Relayout();
                    await frames(4);
                    var (fallbackTop, fallbackBottom, _, _, _) = panel.KitForSelfTest;
                    require(fallbackTop is null && fallbackBottom is not null, $"[{tag}] with a top cap missing the top strip is the pack's and the bottom strip the kit's");
                    require(panel.GetNode<CanvasItem>("TopStrip/Art").Visible && !panel.GetNode<CanvasItem>("BottomStrip/Art").Visible, $"[{tag}] the fallback shows the pack's top art and the kit's bottom art");
                    SteamwovenKitStore.UseRoot(null);
                    panel.Relayout();
                    await frames(4);
                    require(panel.KitForSelfTest is { Top: null, Bottom: null }, $"[{tag}] with no kit folder both strips are the generated chrome");
                    SteamwovenKitStore.UseRoot(root);
                    panel.Relayout();
                    await frames(4);
                }
                GD.Print($"ARC-UI-2461 steamwoven {tag} step={step} exact={plan.Layout.ExactStep} runtime={plan.Layout.RuntimeScale.ToDouble():F4} top={column.TopStrip} bottom={column.BottomStrip} zone={zone}");
            }
            // AC2 (SWI-RUN-4): with the HD option off the kit is not read and nothing of it shows.
            _gameSettings = _gameSettings with { HdInterface = false, GeneratedInterface = false };
            SyncInterfaceHd();
            panel.Relayout();
            await frames(4);
            require(!AreaGameBarPanel.SteamwovenOn && panel.KitForSelfTest is { Top: null, Bottom: null } kitOff && kitOff.Background.Count == 0 && !kitOff.Boards.Visible && !kitOff.Caps.Visible
                && panel.GetNode<CanvasItem>("TopStrip/Art").Visible && panel.GetNode<CanvasItem>("BottomStrip/Art").Visible && panel.KitButtonForSelfTest(AreaBarButton.Character).Count == 0,
                "[kit] with the HD option off nothing of the kit shows and the strips are the original's");
        }
        finally
        {
            _smokeKitBackdrop = null;
            SteamwovenKitStore.ClearOverride();
            _gameSettings = _gameSettings with { HdInterface = savedHd, GeneratedInterface = savedGenerated };
            SyncInterfaceHd();
            panel.Relayout();
            await frames(3);
            foreach (var folder in new[] { root, fallbackRoot })
                try { System.IO.Directory.Delete(folder, true); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { GD.Print("ARC-UI-2461 synthetic kit not removed: " + folder); }
        }
    }
}
