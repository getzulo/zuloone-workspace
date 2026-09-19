using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class WarehouseSlipXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid TransferScriptId = Guid.Parse("c9e79a11-078b-4b61-9aa7-4f97486acd18");
    private static readonly Guid TransferTypeId = Guid.Parse("242aad95-3647-45b4-bcf7-e3e969d233ba");
    private static readonly Guid AdjustmentScriptId = Guid.Parse("b69661ca-8a39-433d-8769-d06aa4ec44c2");
    private static readonly Guid AdjustmentTypeId = Guid.Parse("31619a66-935d-4e19-a792-dcfe2e5f85eb");
    private static readonly Guid CountScriptId = Guid.Parse("db645e16-a41b-4299-a7e2-23642cc365d9");
    private static readonly Guid CountTypeId = Guid.Parse("57100901-0000-4000-8000-000000000000");

    [IntegrationTest("StockTransferXr печатает перемещение: FromCell/ToCell шапки, количества")]
    public async Task TransferXrPrintsCellsAndQuantity()
    {
        await AssertQuantitySlipAsync(
            TransferScriptId, TransferTypeId, "StockTransfer",
            "GetDocumentAsync<StockTransfer>", "FromCell", "ToCell");
    }

    [IntegrationTest("StockAdjustmentXr печатает корректировку: ячейка, причина, количество")]
    public async Task AdjustmentXrPrintsCellReasonAndQuantity()
    {
        await AssertQuantitySlipAsync(
            AdjustmentScriptId, AdjustmentTypeId, "StockAdjustment",
            "GetDocumentAsync<StockAdjustment>", "Cell", "Reason");
    }

    [IntegrationTest("StockCountXr печатает инвентаризацию: ячейка, факт CountedQty")]
    public async Task CountXrPrintsCellAndCountedQty()
    {
        await AssertQuantitySlipAsync(
            CountScriptId, CountTypeId, "StockCount",
            "GetDocumentAsync<StockCount>", "Cell", "CountedQty");
    }

    private static async Task AssertQuantitySlipAsync(
        Guid scriptId, Guid typeId, string name, string loadCall, string firstField, string secondField)
    {
        var script = await Metadata.GetScriptAsync(scriptId);
        Assert.IsTrue(script != null, "скрипт {0}XrPrintForm есть в метаданных", name);
        Assert.IsTrue(
            script!.Code.Contains(loadCall, StringComparison.Ordinal),
            "форма печатает {0}", name);
        Assert.IsTrue(
            script.Code.Contains("FormatAsync", StringComparison.Ordinal),
            "имена через IReferenceDisplay");
        Assert.IsTrue(
            script.Code.Contains(firstField, StringComparison.Ordinal),
            "{0} на бумаге", firstField);
        Assert.IsTrue(
            script.Code.Contains(secondField, StringComparison.Ordinal),
            "{0} на бумаге", secondField);
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "{0} без сумм", name);
        var bound = await Metadata.GetScriptsByObjectAsync("Document", typeId);
        Assert.IsTrue(
            bound.Any(x => x.MetaId == scriptId),
            "скрипт привязан к {0}", name);
    }
}
