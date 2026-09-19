using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class PurchaseReturnXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid PurchaseReturnXrScriptId = Guid.Parse("c4f27a69-ce61-4f89-8d6a-56dd6cd8010f");
    private static readonly Guid PurchaseReturnTypeId = Guid.Parse("014d66aa-d70e-4895-817d-5d79047f77e4");

    [IntegrationTest("PurchaseReturnXr печатает PurchaseReturn: имена, прайсинг, исходный заказ; не налоговый гейт")]
    public async Task PurchaseReturnXrPrintsGoodsReturnNotTaxInvoice()
    {
        var script = await Metadata.GetScriptAsync(PurchaseReturnXrScriptId);
        Assert.IsTrue(script != null, "скрипт PurchaseReturnXrPrintForm есть в метаданных");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<PurchaseReturn>", StringComparison.Ordinal),
            "форма печатает возврат поставщику, не заказ");
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
            !script.Code.Contains("BuyerReleaseBlockAsync", StringComparison.Ordinal),
            "возврат поставщику — не налоговая фактура, гейт Cleared не зовём");

        var onReturn = await Metadata.GetScriptsByObjectAsync("Document", PurchaseReturnTypeId);
        Assert.IsTrue(
            onReturn.Any(x => x.MetaId == PurchaseReturnXrScriptId),
            "скрипт привязан к PurchaseReturn");
    }
}
