using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class PickTaskXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid PickTaskXrScriptId = Guid.Parse("33f29dc3-cc2f-43ae-bb64-c95f5910910b");
    private static readonly Guid PickTaskTypeId = Guid.Parse("57100801-0000-4000-8000-000000000000");

    [IntegrationTest("PickTaskXr печатает отбор: ячейки, количества, без денег и без IEInvoiceRelease")]
    public async Task PickTaskXrPrintsQuantityOnlyPickList()
    {
        var script = await Metadata.GetScriptAsync(PickTaskXrScriptId);
        Assert.IsTrue(script != null, "скрипт PickTaskXrPrintForm есть в метаданных");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<PickTask>", StringComparison.Ordinal),
            "форма печатает отбор, не расход");
        Assert.IsTrue(
            script.Code.Contains("FormatAsync", StringComparison.Ordinal),
            "имена через IReferenceDisplay, не Guid.ToString");
        Assert.IsTrue(
            script.Code.Contains("FromCell", StringComparison.Ordinal),
            "шапка — ячейка хранения");
        Assert.IsTrue(
            script.Code.Contains("ToCell", StringComparison.Ordinal),
            "строка — ячейка отбора");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "лист отбора без сумм");
        Assert.IsTrue(
            !script.Code.Contains("BuyerReleaseBlockAsync", StringComparison.Ordinal),
            "отбор — не налоговая фактура");

        var onPick = await Metadata.GetScriptsByObjectAsync("Document", PickTaskTypeId);
        Assert.IsTrue(
            onPick.Any(x => x.MetaId == PickTaskXrScriptId),
            "скрипт привязан к PickTask");
    }
}
