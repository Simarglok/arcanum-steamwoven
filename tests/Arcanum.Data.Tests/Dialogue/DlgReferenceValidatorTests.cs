using System.Text;
using Arcanum.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class DlgReferenceValidatorTests
{
    [TestMethod]
    public void Validate_preserves_player_order_and_classifies_all_reference_forms()
    {
        var document = Read(
            "{20}{}{}{}{}{}{}" +
            "{1}{}{}{1}{}{20}{}" +
            "{2}{}{}{1}{}{}{}" +
            "{3}{}{}{1}{}{0}{}" +
            "{4}{}{}{1}{}{-1}{}" +
            "{5}{}{}{1}{}{99}{}" +
            "{6}{}{}{1}{}{7}{}" +
            "{7}{}{}{1}{}{}{}");

        var result = DlgReferenceValidator.Validate(document);

        Assert.AreEqual(7, result.References.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7 }, result.References.Select(reference => reference.SourceRecordId).ToArray());
        CollectionAssert.AreEqual(
            new[] { DlgReferenceStatus.Valid, DlgReferenceStatus.Valid, DlgReferenceStatus.Valid, DlgReferenceStatus.Valid, DlgReferenceStatus.MissingTarget, DlgReferenceStatus.WrongTargetRole, DlgReferenceStatus.Valid },
            result.References.Select(reference => reference.Status).ToArray());
        Assert.AreEqual(DlgResponseKind.Direct, result.References[0].ResponseKind);
        Assert.AreEqual(20, result.References[0].TargetId);
        Assert.AreEqual(DlgResponseKind.Exit, result.References[1].ResponseKind);
        Assert.IsNull(result.References[1].ResponseValue);
        Assert.IsNull(result.References[1].TargetId);
        Assert.AreEqual(0, result.References[2].ResponseValue);
        Assert.AreEqual(DlgResponseKind.Special, result.References[3].ResponseKind);
        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void Validate_uses_id_index_on_a_large_document_without_runtime_traversal()
    {
        var builder = new StringBuilder("{1}{}{}{}{}{}{}");
        for (var id = 2; id <= 1_002; id++)
        {
            builder.Append('{').Append(id).Append("}{}{}{1}{}{1}{}");
        }
        var document = Read(builder.ToString());

        var result = DlgReferenceValidator.Validate(document);

        Assert.AreEqual(1_001, result.References.Count);
        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.References.All(reference => reference.Status == DlgReferenceStatus.Valid));
    }

    private static DlgDocument Read(string value) => DlgReader.Read(Encoding.Latin1.GetBytes(value));
}
