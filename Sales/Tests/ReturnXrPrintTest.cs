using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class ReturnXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid ReturnXrScriptId = Guid.Parse("2115ce67-abb9-466d-815a-4f55a23e0896");
    private static readonly Guid SalesReturnTypeId = Guid.Parse("fac9ef45-4371-41f5-a0a1-8d185a185714");
    private static readonly Guid CreditNoteXrScriptId = Guid.Parse("74805cfd-6dc3-493d-a37a-4420ee4df31d");
    private static readonly Guid DebitNoteXrScriptId = Guid.Parse("c66daecc-92ae-4042-bd44-e95362a0a593");

    [IntegrationTest("ReturnXr печатает SalesReturn: имена, прайсинг, исходный счёт; не налоговый гейт")]
    public async Task ReturnXrPrintsGoodsReturnNotTaxInvoice()
    {
        var script = await Metadata.GetScriptAsync(ReturnXrScriptId);
        Assert.IsTrue(script != null, "скрипт ReturnXrPrintForm есть в метаданных");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<SalesReturn>", StringComparison.Ordinal),
            "форма печатает возврат, не кредит-ноту");
        Assert.IsTrue(
            script.Code.Contains("FormatAsync", StringComparison.Ordinal),
            "имена через IReferenceDisplay, не Guid.ToString");
        Assert.IsTrue(
            script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "сумма через тот же прайсинг, не второй движок");
        Assert.IsTrue(
            script.Code.Contains("OriginalInvoice", StringComparison.Ordinal),
            "на бумаге исходный счёт");
        Assert.IsTrue(
            !script.Code.Contains("BuyerReleaseBlockAsync", StringComparison.Ordinal),
            "возврат товара — не налоговая фактура, гейт Cleared не зовём");

        var onReturn = await Metadata.GetScriptsByObjectAsync("Document", SalesReturnTypeId);
        Assert.IsTrue(
            onReturn.Any(x => x.MetaId == ReturnXrScriptId),
            "скрипт привязан к SalesReturn");
    }

    [IntegrationTest("CreditNoteXr и DebitNoteXr кладут OriginalInvoice в Notes на бумаге")]
    public async Task NotePrintFormsShowOriginalInvoice()
    {
        var credit = await Metadata.GetScriptAsync(CreditNoteXrScriptId);
        Assert.IsTrue(credit != null, "скрипт CreditNoteXrPrintForm есть");
        Assert.IsTrue(
            credit!.Code.Contains("OriginalInvoice", StringComparison.Ordinal),
            "кредит-нота печатает исходный счёт");
        Assert.IsTrue(
            credit.Code.Contains("IEInvoiceRelease", StringComparison.Ordinal),
            "кредит-нота по-прежнему зовёт IEInvoiceRelease");

        var debit = await Metadata.GetScriptAsync(DebitNoteXrScriptId);
        Assert.IsTrue(debit != null, "скрипт DebitNoteXrPrintForm есть");
        Assert.IsTrue(
            debit!.Code.Contains("OriginalInvoice", StringComparison.Ordinal),
            "дебет-нота печатает исходный счёт");
        Assert.IsTrue(
            debit.Code.Contains("IEInvoiceRelease", StringComparison.Ordinal),
            "дебет-нота по-прежнему зовёт IEInvoiceRelease");
    }
}
