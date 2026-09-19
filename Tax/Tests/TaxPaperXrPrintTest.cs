using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class TaxPaperXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid ReturnScriptId = Guid.Parse("c363b42c-5f2d-457f-9063-76e8e0f662bb");
    private static readonly Guid ReturnTypeId = Guid.Parse("fdba6c82-e480-4aea-8ca3-1cb91e04c6df");
    private static readonly Guid PaymentScriptId = Guid.Parse("a980787b-0cd3-4553-9a38-1c0556a365a3");
    private static readonly Guid PaymentTypeId = Guid.Parse("8f3c1a67-2d90-4e45-b8c1-5a7e9d0f2b34");

    [IntegrationTest("TaxReturnXr печатает декларацию: период, коды, суммы с шапки")]
    public async Task TaxReturnXrPrintsPeriodAndCodes()
    {
        var script = await Metadata.GetScriptAsync(ReturnScriptId);
        Assert.IsTrue(script != null, "скрипт TaxReturnXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<TaxReturn>", StringComparison.Ordinal),
            "форма печатает декларацию");
        Assert.IsTrue(script.Code.Contains("PeriodFrom", StringComparison.Ordinal), "период с");
        Assert.IsTrue(script.Code.Contains("TaxCode", StringComparison.Ordinal), "код на строке");
        Assert.IsTrue(script.Code.Contains("NetPayable", StringComparison.Ordinal), "к уплате с шапки");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "суммы декларации не из прайсинга продаж");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", ReturnTypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == ReturnScriptId), "скрипт привязан к TaxReturn");
    }

    [IntegrationTest("TaxPaymentXr печатает оплату налога: юрлицо, код, сумма строки")]
    public async Task TaxPaymentXrPrintsCodeAndAmount()
    {
        var script = await Metadata.GetScriptAsync(PaymentScriptId);
        Assert.IsTrue(script != null, "скрипт TaxPaymentXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<TaxPayment>", StringComparison.Ordinal),
            "форма печатает оплату налога");
        Assert.IsTrue(script.Code.Contains("TaxCode", StringComparison.Ordinal), "код на строке");
        Assert.IsTrue(script.Code.Contains("line.Amount", StringComparison.Ordinal), "сумма строки");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", PaymentTypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == PaymentScriptId), "скрипт привязан к TaxPayment");
    }
}
