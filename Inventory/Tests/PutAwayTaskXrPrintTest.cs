using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class PutAwayTaskXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid PutAwayTaskXrScriptId = Guid.Parse("9c3f7f17-1e06-4204-a38b-4860325eca50");
    private static readonly Guid PutAwayTaskTypeId = Guid.Parse("57100701-0000-4000-8000-000000000000");

    [IntegrationTest("PutAwayTaskXr печатает раскладку: ячейки, количества, без денег")]
    public async Task PutAwayTaskXrPrintsQuantityOnlyPutAwayList()
    {
        var script = await Metadata.GetScriptAsync(PutAwayTaskXrScriptId);
        Assert.IsTrue(script != null, "скрипт PutAwayTaskXrPrintForm есть в метаданных");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<PutAwayTask>", StringComparison.Ordinal),
            "форма печатает раскладку, не отбор");
        Assert.IsTrue(
            script.Code.Contains("FormatAsync", StringComparison.Ordinal),
            "имена через IReferenceDisplay, не Guid.ToString");
        Assert.IsTrue(
            script.Code.Contains("FromCell", StringComparison.Ordinal),
            "шапка — ячейка приёмки");
        Assert.IsTrue(
            script.Code.Contains("ToCell", StringComparison.Ordinal),
            "строка — ячейка хранения");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "лист раскладки без сумм");
        Assert.IsTrue(
            !script.Code.Contains("BuyerReleaseBlockAsync", StringComparison.Ordinal),
            "раскладка — не налоговая фактура");

        var onPutAway = await Metadata.GetScriptsByObjectAsync("Document", PutAwayTaskTypeId);
        Assert.IsTrue(
            onPutAway.Any(x => x.MetaId == PutAwayTaskXrScriptId),
            "скрипт привязан к PutAwayTask");
    }
}
