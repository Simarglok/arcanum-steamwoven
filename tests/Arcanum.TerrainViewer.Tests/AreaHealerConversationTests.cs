using Arcanum.Scripting.Dialog;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1732: the AREA conversation host's healer seam (the <c>h:</c> reply). Synthetic content: the invented SCR of
/// <see cref="AreaReactionConversationTests"/> and an invented DLG whose one reply is the healer marker. The menu itself is the dialog runtime's
/// (<c>DialogHealerTests</c>) and the speaker's facts and effects are the AI host's (<c>AreaAiSkillTests</c>).</summary>
[TestClass]
public sealed class AreaHealerConversationTests
{
    private const string Dialog = "{1}{Well?}{}{}{}{}{}" + "{2}{h:}{}{1}{}{3}{}" + "{3}{Done.}{}{}{}{}{}";

    [TestMethod]
    public void The_h_reply_stays_blocked_without_an_ai_host_or_the_modules_texts_and_the_seams_survive_a_rebind()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual((DialogOptionBlockKind.UnsupportedInteraction, DialogOptionTransition.Unsupported), (talk.Options[0].Block!.Kind, talk.Options[0].Transition), "no AI host, no skill texts");
        Assert.IsTrue(f.Host.EndReaction());

        Func<AreaAiHost?> none = () => null;
        Func<int, string?> names = spell => $"Spell{spell}";
        f.Host.HealerHost = none;
        f.Host.SpellName = names;
        var asked = f.Host.Begin(f.Speaker).Conversation!;
        Assert.IsNotNull(asked.Options[0].Block, "an AI host that answers none does not admit the reply either");
        Assert.IsTrue(f.Host.EndReaction());
        var rebound = f.Host.Rebind(f.Session);
        Assert.AreSame(none, rebound.HealerHost);
        Assert.AreSame(names, rebound.SpellName);
    }
}
