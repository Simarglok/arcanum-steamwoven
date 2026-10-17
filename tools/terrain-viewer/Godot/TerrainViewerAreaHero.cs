using Arcanum.Scripting.GlobalFlags;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-027: with --hero the AREA session starts as that original pre-made hero (read during the background load):
// the session's player character, gold and starting kit, the hero's main-menu flag, and the hero's own DLG facts in
// conversations. The status line names the hero; without --hero it says the diagnostic PC profile is in use.
public partial class TerrainViewerApp
{
    private (Arcanum.Session.GameSessionOwner Session, long Revision, string Text)? _heroStatus;

    private string HeroStatusText()
    {
        if (_areaSession is not { } session) return "";
        if (_heroStatus is not { } cached || !ReferenceEquals(cached.Session, session) || cached.Revision != session.Version.Revision)
            _heroStatus = cached = (session, session.Version.Revision, AreaStartingHero.Summary(session));
        return " | " + cached.Text;
    }

    // Native observation, run after the inventory smoke when --hero was given: the session holds the loaded hero, its
    // gold and kit; the hero's flag is true; the conversation host gates replies with the hero's facts; the window lists
    // the kit first; the status line names the hero.
    private Task RunAreaHeroSmokeAsync(TerrainLoadResult result)
    {
        if (result.Hero is not { Definition: { } definition, Flag: { } flag } hero) return Task.CompletedTask;
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-WORLD-027 hero smoke: " + why); }
        var session = _areaSession ?? throw new InvalidOperationException("ARC-WORLD-027 hero smoke: no area session");
        var state = session.QueryHero();
        Require(state is not null && state.Prototype == definition.Prototype && state.Level == definition.Level && state.UnspentPoints == definition.UnspentPoints,
            "session hero " + state?.Prototype);
        Require(session.QueryGold() == definition.Gold && session.QueryHeroKit().Count == definition.Kit.Count, "gold and kit");
        var flags = session.Flags;
        var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(flag)));
        Require(read.Status == GlobalFlagReadStatus.Known && read.Value == true, $"flag {flag} {read.Status} {read.Value}");
        var facts = session.QueryHeroDialogFacts();
        Require(facts is not null && (_conversationHost is null || _conversationHost.Profile.Pc == facts), "conversation PC facts " + _conversationHost?.Profile.Pc);
        if (_inventoryHost is not null)
        {
            var view = _inventoryHost.View(session, _hoverNames?.Catalog, _readyArea!.Snapshot.RuntimeMap);
            var kit = view.Pc.Take(definition.Kit.Count).ToArray();
            Require(kit.Length == definition.Kit.Count && kit.All(line => line.Entity.StartsWith("kit:", StringComparison.Ordinal) && line.Text.EndsWith("(starting kit)", StringComparison.Ordinal)),
                "window kit lines");
            Require(view.Gold == definition.Gold, "window gold " + view.Gold);
        }
        RefreshAreaStatus();
        Require(GetSelfTestSnapshot().Hud.Contains("hero: " + hero.Hero.Name + " level ", StringComparison.Ordinal), "HUD hero line");
        GD.Print("ARC-WORLD-027 native-smoke hero PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            hero.Hero.Name, definition.Prototype, Flag = flag, definition.Gold, Kit = definition.Kit.Count, hero.DroppedChanges,
            Facts = facts!.ToString(), Conversation = _conversationHost is not null }));
        return Task.CompletedTask;
    }
}
