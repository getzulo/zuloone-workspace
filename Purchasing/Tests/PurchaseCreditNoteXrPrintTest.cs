using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class PurchaseCreditNoteXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid PrintScriptId = Guid.Parse("3f110aaf-c012-4087-a0d3-d18afd9029f7");
    private static readonly Guid DocumentTypeId = Guid.Parse("10f6b734-1288-40f2-aa0f-3717a4e3995f");

    [IntegrationTest("PurchaseCreditNoteXr печатает PurchaseCreditNote: имена, прайсинг, исходный заказ, НДС; не налоговый гейт")]
    public async Task PurchaseCreditNoteXrPrintsTaxCreditNotZatca()
    {
        var script = await Metadata.GetScriptAsync(PrintScriptId);
        Assert.IsTrue(script != null, "скрипт PurchaseCreditNoteXrPrintForm есть в метаданных");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<PurchaseCreditNote>", StringComparison.Ordinal),
            "форма печатает кредит-ноту закупки");
        Assert.IsTrue(
            script.Code.Contains("FormatAsync", StringComparison.Ordinal),
            "имена через IReferenceDisplay, не Guid.ToString");
        Assert.IsTrue(
            script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "сумма через тот же прайсинг, не второй движок");
        Assert.IsTrue(
            script.Code.Contains("OriginalOrder", StringComparison.Ordinal),
            "на бумаге исходный заказ поставщику");
        Assert.IsTrue(
            script.Code.Contains("ITaxService", StringComparison.Ordinal),
            "НДС той же ставкой, что проводки");
        Assert.IsTrue(
            !script.Code.Contains("BuyerReleaseBlockAsync", StringComparison.Ordinal),
            "входной НДС — не исходящий ZATCA, гейт Cleared не зовём");

        var onNote = await Metadata.GetScriptsByObjectAsync("Document", DocumentTypeId);
        Assert.IsTrue(
            onNote.Any(x => x.MetaId == PrintScriptId),
            "скрипт привязан к PurchaseCreditNote");
    }
}
