using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Magic.SpellRecordTableTests;

namespace Arcanum.Data.Tests.Magic;

/// <summary>ARC-MAGIC-017: the eye-candy art id and the <c>rules\SpellEyeCandy.mes</c> reader on synthetic tables (no game text).</summary>
[TestClass]
public sealed class SpellEyeCandyTableTests
{
    private static readonly Func<uint, bool> AllLoad = _ => true;

    [TestMethod]
    public void ArtIdPacksAndReadsEveryPart()
    {
        var aid = EyeCandyArt.Make(27, 5, 3, true, 1, 2, 6)!.Value;
        Assert.AreEqual(14u, aid >> 28);
        Assert.AreEqual(27, EyeCandyArt.Number(aid));
        Assert.AreEqual(5, EyeCandyArt.Frame(aid));
        Assert.AreEqual(3, EyeCandyArt.Rotation(aid));
        Assert.IsTrue(EyeCandyArt.Translucent(aid));
        Assert.AreEqual(EyeCandyVariant.Background, EyeCandyArt.Variant(aid));
        Assert.AreEqual(2, EyeCandyArt.Palette(aid));
        Assert.AreEqual(6, EyeCandyArt.Scale(aid));
        Assert.AreEqual(0u, aid & 1);
        Assert.AreEqual((14u << 28) | (27u << 19) | (5u << 12) | (3u << 9) | (1u << 8) | (1u << 6) | (2u << 4) | (6u << 1), aid);
        var other = EyeCandyArt.WithFrame(EyeCandyArt.WithVariant(aid, EyeCandyVariant.Underlay), 9);
        Assert.AreEqual(EyeCandyVariant.Underlay, EyeCandyArt.Variant(other));
        Assert.AreEqual(9, EyeCandyArt.Frame(other));
        Assert.AreEqual(27, EyeCandyArt.Number(other));
        // The original's error 12 for a part out of range.
        Assert.IsNull(EyeCandyArt.Make(512, 0, 0, false, 0, 0, 4));
        Assert.IsNull(EyeCandyArt.Make(1, 128, 0, false, 0, 0, 4));
        Assert.IsNull(EyeCandyArt.Make(1, 0, 8, false, 0, 0, 4));
        Assert.IsNull(EyeCandyArt.Make(1, 0, 0, false, 4, 0, 4));
        Assert.IsNull(EyeCandyArt.Make(1, 0, 0, false, 0, 4, 4));
        Assert.IsNull(EyeCandyArt.Make(1, 0, 0, false, 0, 0, 8));
        Assert.AreEqual("art/eye_candy/Blip_F.art", EyeCandyArt.FilePath("Blip", EyeCandyVariant.Foreground));
        Assert.AreEqual("art/eye_candy/Blip_U.art", EyeCandyArt.FilePath("Blip", EyeCandyVariant.Underlay));
    }

    [TestMethod]
    public void EyeCandyArtResolvesThroughItsNameTable()
    {
        var aid = EyeCandyArt.Make(10, 3, 0, false, 1, 0, 4)!.Value;
        var resolved = AreaObjectArt.Resolve(aid, (role, key) => role == AreaMappingRole.EyeCandy && key == 10 ? "Glow" : null);
        Assert.AreEqual(AreaObjectArtStatus.Resolved, resolved.Status);
        Assert.AreEqual("art/eye_candy/Glow_B.art", resolved.Path!.Path);
        Assert.AreEqual(3, resolved.Path.Frame);
        Assert.AreEqual(AreaObjectArtStatus.UnnamedNumber, AreaObjectArt.Resolve(EyeCandyArt.Make(11, 0, 0, false, 0, 0, 4)!.Value, (_, _) => null).Status);
        Assert.AreEqual("art/eye_candy/eye_candy.mes", AreaObjectArt.MappingPath(AreaMappingRole.EyeCandy));
    }

    [TestMethod]
    public void MessagesMapToSixEntriesPerGroupWhileTheGroupsFirstMessageExists()
    {
        var table = Read("{0}{Art: 3, Flags: overlay_f | animates}\n{4}{Art: 4, Flags: overlay_f}\n{5}{Art: 5, Flags: overlay_f}\n{6}{Art: 6, Flags: overlay_f}\n"
            + "{10}{}\n{12}{Art: 7, Flags: underlay}\n{30}{Art: 9, Flags: overlay_f}\n");
        // Messages 0 and 10 exist, 20 does not: two groups; 30 is past the gap and never read; 6 is not one of a group's six.
        Assert.AreEqual(12, table.Entries.Count);
        Assert.AreEqual(3, EyeCandyArt.Number(table.Entry(0, 0).Art!.Value));
        Assert.AreEqual(4, EyeCandyArt.Number(table.At(4).Art!.Value));
        Assert.AreEqual(5, EyeCandyArt.Number(table.At(5).Art!.Value));
        Assert.IsNull(table.At(6).Art, "message 10 is empty");
        Assert.AreEqual(7, EyeCandyArt.Number(table.Entry(1, 2).Art!.Value));
        Assert.AreEqual(EyeCandyVariant.Underlay, EyeCandyArt.Variant(table.Entry(1, 2).Art!.Value));
        Assert.AreEqual(SpellEyeCandyEntry.Empty, table.Entry(2, 0));
        Assert.AreEqual(SpellEyeCandyFlags.Foreground | SpellEyeCandyFlags.Animates, table.Entry(0, 0).Flags);
        Assert.AreEqual(SpellDataStatus.FileEmpty, SpellEyeCandyTable.Read(Mes("{1}{Art: 1}\n"), AllLoad).Status);
    }

    [TestMethod]
    public void TheLineIsParsedInTheOriginalsKeyOrderWithItsDefaults()
    {
        var table = Read("{0}{Art: 27, Palette: 1, Scale: 50%, Blend: Add, Sound: 9010, Flags: overlay_fb | anim_forever, Light: 1, Light Color:  (187 @ 0 @ 215), Proj Speed: 6}\n");
        var entry = table.At(0);
        var aid = entry.Art!.Value;
        Assert.AreEqual(1, EyeCandyArt.Palette(aid));
        Assert.AreEqual(0, EyeCandyArt.Scale(aid), "50 % is index 0");
        Assert.AreEqual(EyeCandyVariant.Foreground, EyeCandyArt.Variant(aid));
        Assert.IsTrue(EyeCandyArt.Translucent(aid), "a blend other than none is translucent");
        Assert.AreEqual(SpellEyeCandyFlags.Foreground | SpellEyeCandyFlags.Background | SpellEyeCandyFlags.Animates | SpellEyeCandyFlags.Forever
            | SpellEyeCandyFlags.Translucent, entry.Flags);
        Assert.AreEqual(9010, entry.Sound);
        Assert.AreEqual(1, entry.Light);
        Assert.AreEqual(((byte)187, (byte)0, (byte)215), entry.LightColor);
        Assert.AreEqual(6, entry.ProjectileSpeed);

        var plain = Read("{0}{Art: 2}\n").At(0);
        Assert.AreEqual(0, EyeCandyArt.Palette(plain.Art!.Value));
        Assert.AreEqual(EyeCandyArt.UnscaledIndex, EyeCandyArt.Scale(plain.Art!.Value));
        Assert.AreEqual(EyeCandyVariant.Underlay, EyeCandyArt.Variant(plain.Art!.Value), "no foreground or background flag: the underlay variant");
        Assert.IsFalse(EyeCandyArt.Translucent(plain.Art!.Value));
        Assert.AreEqual(SpellEyeCandyFlags.None, plain.Flags);
        Assert.AreEqual(-1, plain.Sound);
        Assert.IsNull(plain.Light);
        Assert.AreEqual(0, plain.ProjectileSpeed);
        // Light without a colour is white.
        Assert.AreEqual(((byte)255, (byte)255, (byte)255), Read("{0}{Art: 2, Light: 4}\n").At(0).LightColor);
    }

    [TestMethod]
    public void ScaleAndBlendShareOneValueSoAScaleWithoutBlendIsTranslucent()
    {
        Assert.IsTrue(EyeCandyArt.Translucent(Read("{0}{Art: 2, Scale: 100%, Flags: overlay_f}\n").At(0).Art!.Value));
        Assert.IsFalse(EyeCandyArt.Translucent(Read("{0}{Art: 2, Scale: 100%, Blend: none, Flags: overlay_f}\n").At(0).Art!.Value));
        Assert.IsFalse(EyeCandyArt.Translucent(Read("{0}{Art: 2, Flags: overlay_f}\n").At(0).Art!.Value));
        // A percent outside the table is logged by the original and takes index 0.
        Assert.AreEqual(0, EyeCandyArt.Scale(Read("{0}{Art: 2, Scale: 90%, Flags: overlay_f}\n").At(0).Art!.Value));
        Assert.AreEqual(7, EyeCandyArt.Scale(Read("{0}{Art: 2, Scale: 200%, Flags: overlay_f}\n").At(0).Art!.Value));
    }

    [TestMethod]
    public void AKeyOutOfOrderIsNotReadAndAValueSwallowsTheNextKey()
    {
        // "Sound:" without a value reads the next key's text as its number: the flags are then never reached (an installed line has
        // this form).
        var swallowed = Read("{0}{Art: 2, Sound: Flags: overlay_fb | animates, Light: 1}\n").At(0);
        Assert.AreEqual(0, swallowed.Sound);
        Assert.AreEqual(SpellEyeCandyFlags.None, swallowed.Flags);
        Assert.AreEqual(1, swallowed.Light);
        // A key after its place (Palette: after Flags:) is never read.
        Assert.AreEqual(SpellEyeCandyFlags.Foreground, Read("{0}{Art: 2, Flags: overlay_f, Palette: 3}\n").At(0).Flags);
    }

    [TestMethod]
    public void AnEntryWithoutSoundTakesItsGroupsFirstSoundPlusItsOffset()
    {
        var table = Read("{0}{Art: 1, Sound: 9010, Flags: overlay_f}\n{1}{Art: 1, Flags: overlay_f}\n{2}{Art: 1, Flags: overlay_f}\n{3}{Art: 1, Flags: overlay_f}\n"
            + "{4}{Art: 1, Flags: overlay_f}\n{5}{Art: 1, Flags: overlay_f}\n{10}{Art: 1, Flags: overlay_f}\n{11}{Art: 1, Flags: overlay_f}\n{12}{Sound: 77}\n");
        CollectionAssert.AreEqual(new[] { 9010, 9014, 9015, 9016, 9013, 9017 }, table.Entries.Take(6).Select(e => e.Sound).ToArray());
        Assert.AreEqual(-1, table.At(6).Sound);
        Assert.AreEqual(-1, table.At(7).Sound, "the group's first entry has no sound");
        Assert.AreEqual(77, table.At(8).Sound, "a line without Art: reads Sound:");
        Assert.IsNull(table.At(8).Art);
    }

    [TestMethod]
    public void ArtEffectAndAMissingPartLeaveNoArt()
    {
        Assert.AreEqual(SpellEyeCandyFlags.Translucent, Read("{0}{ArtEffect: Translucency}\n").At(0).Flags);
        Assert.AreEqual(SpellEyeCandyFlags.Tinting, Read("{0}{ArtEffect: Tinting}\n").At(0).Flags);
        // The background ART does not load: no art, no flags; the sound stays.
        var asked = new List<uint>();
        var table = SpellEyeCandyTable.Read(Mes("{0}{Art: 5, Sound: 3, Flags: overlay_fb}\n"), aid => { asked.Add(aid); return EyeCandyArt.Variant(aid) == EyeCandyVariant.Foreground; });
        Assert.IsNull(table.Table!.At(0).Art);
        Assert.AreEqual(SpellEyeCandyFlags.None, table.Table.At(0).Flags);
        Assert.AreEqual(3, table.Table.At(0).Sound);
        CollectionAssert.AreEqual(new[] { EyeCandyVariant.Foreground, EyeCandyVariant.Background }, asked.Select(EyeCandyArt.Variant).ToArray());
        // A palette out of range builds no art id.
        Assert.IsNull(Read("{0}{Art: 5, Palette: 4, Flags: overlay_f}\n").At(0).Art);
    }

    [TestMethod]
    public void ProcessEndingValuesAreRefused()
    {
        Assert.AreEqual(SpellDataStatus.UnknownName, SpellEyeCandyTable.Read(Mes("{0}{Art: 1, Blend: Glow}\n"), AllLoad).Status);
        Assert.AreEqual(SpellDataStatus.UnknownFlag, SpellEyeCandyTable.Read(Mes("{0}{Art: 1, Flags: overlay_x}\n"), AllLoad).Status);
        Assert.AreEqual(SpellDataStatus.MissingParenthesis, SpellEyeCandyTable.Read(Mes("{0}{Art: 1, Light: 1, Light Color: 4 @ 5}\n"), AllLoad).Status);
        Assert.AreEqual(SpellDataStatus.NonAsciiByte, SpellEyeCandyTable.Read(Mes("{0}{Art: 1é}\n"), AllLoad).Status);
    }

    [TestMethod]
    public void TripleValueReadsLikeThePairWithDefaults()
    {
        Assert.AreEqual(((byte)4, (byte)5, (byte)4), Read("{0}{Art: 1, Light: 1, Light Color: (4 @ 5)}\n").At(0).LightColor);
        Assert.AreEqual(((byte)4, (byte)4, (byte)4), Read("{0}{Art: 1, Light: 1, Light Color: (4)}\n").At(0).LightColor);
    }

    private static SpellEyeCandyTable Read(string text)
    {
        var read = SpellEyeCandyTable.Read(Mes(text), AllLoad);
        Assert.AreEqual(SpellDataStatus.Read, read.Status, read.Refusal?.ToString());
        return read.Table!;
    }
}
