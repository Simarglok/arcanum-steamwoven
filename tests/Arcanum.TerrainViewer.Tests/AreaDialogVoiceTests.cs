using System.Text;
using Arcanum.Data;
using Arcanum.Data.Audio;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-043: the voice identity of an NPC line from its DLG (audio study SN21: script number and the number in the
/// line's condition field; an empty or white-space field has no voice).</summary>
[TestClass]
public sealed class AreaDialogVoiceTests
{
    [TestMethod]
    public void A_line_is_voiced_by_its_condition_field_and_the_dialog_script()
    {
        var document = DlgReader.Read(Encoding.ASCII.GetBytes(
            "{10}{Hello}{Hello}{}{ 42x}{}{}\r\n{20}{Quiet}{Quiet}{}{ \t }{}{}\r\n{30}{Bare}{Bare}{}{}{}{}\r\n{40}{Zero}{Zero}{}{abc}{}{}\r\n{50}{Neg}{Neg}{}{-1}{}{}\r\n{60}{Big}{Big}{}{70000}{}{}\r\n"),
            new DlgReaderOptions { Mode = DlgReadMode.Original });
        var start = new AreaConversationStart(AreaConversationStartStatus.Started, null, null, "", Document: document, DialogScript: 0x18005);
        Assert.AreEqual((5 << 16) | 42, start.VoiceIdentity(new DialogNpcLine(new(10), "")), "script & 0x7FFF, atoi of the field");
        Assert.AreEqual(-1, start.VoiceIdentity(new DialogNpcLine(new(20), "")), "white space only: no voice");
        Assert.AreEqual(-1, start.VoiceIdentity(new DialogNpcLine(new(30), "")), "empty: no voice");
        Assert.AreEqual(5 << 16, start.VoiceIdentity(new DialogNpcLine(new(40), "")), "text without a number reads 0 and still names a voice");
        Assert.AreEqual((5 << 16) | 0xFFFF, start.VoiceIdentity(new DialogNpcLine(new(50), "")), "a negative number keeps its low 16 bits");
        Assert.AreEqual((5 << 16) | (70000 & 0xFFFF), start.VoiceIdentity(new DialogNpcLine(new(60), "")));
        Assert.AreEqual(-1, start.VoiceIdentity(new DialogNpcLine(new(99), "")), "no such record");
        Assert.AreEqual(-1, start.VoiceIdentity(null));
        Assert.AreEqual(-1, (start with { Document = null }).VoiceIdentity(new DialogNpcLine(new(10), "")));
        Assert.AreEqual(-1, (start with { DialogScript = 0 }).VoiceIdentity(new DialogNpcLine(new(10), "")), "script 0: no voice");
        Assert.IsNull(SoundCatalog.SpeechConditionField(Encoding.ASCII.GetBytes("\r\n\v\f")));
        Assert.AreEqual("é", SoundCatalog.SpeechConditionField([0xE9]), "a high byte is not white space");
    }

    [TestMethod]
    public void A_script_float_line_has_the_same_voice_identity_as_the_dialog_line_of_that_number()
    {
        // ARC-SCRIPT-3124 (exe read 0x44B1F0 → 0x413A30 → 0x416840 → 0x4189C0): the float line's voice is the running script's number and the condition field of the line, as for an NPC line.
        var document = DlgReader.Read(Encoding.ASCII.GetBytes("{10}{Hello}{Hello}{}{ 42x}{}{}\r\n{20}{Quiet}{Quiet}{}{}{}{}\r\n"), new DlgReaderOptions { Mode = DlgReadMode.Original });
        Assert.AreEqual((5 << 16) | 42, AreaScriptFloat.VoiceOf(document, 0x18005, 10));
        Assert.AreEqual(-1, AreaScriptFloat.VoiceOf(document, 0x18005, 20), "no condition field: no voice");
        Assert.AreEqual(-1, AreaScriptFloat.VoiceOf(document, 0x18005, 99), "no such line: no voice");
        Assert.AreEqual(-1, AreaScriptFloat.VoiceOf(document, 0, 10), "script 0: no voice");
    }
}
