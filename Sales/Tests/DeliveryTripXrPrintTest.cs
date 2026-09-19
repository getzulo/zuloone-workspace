using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class DeliveryTripXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid ScriptId = Guid.Parse("b9272259-54ea-46a3-a36e-973282675e37");
    private static readonly Guid TypeId = Guid.Parse("4e480847-88a3-4af8-8604-6325d12fe7b9");

    [IntegrationTest("DeliveryTripXr печатает рейс: водитель, маршрут, точки")]
    public async Task DeliveryTripXrPrintsStops()
    {
        var script = await Metadata.GetScriptAsync(ScriptId);
        Assert.IsTrue(script != null, "скрипт DeliveryTripXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<DeliveryTrip>", StringComparison.Ordinal),
            "форма печатает рейс");
        Assert.IsTrue(script.Code.Contains("FormatAsync", StringComparison.Ordinal), "имена через IReferenceDisplay");
        Assert.IsTrue(script.Code.Contains("Driver", StringComparison.Ordinal), "водитель в шапке");
        Assert.IsTrue(script.Code.Contains("Outlet", StringComparison.Ordinal), "точка на остановке");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "рейс без сумм продажи");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", TypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == ScriptId), "скрипт привязан к DeliveryTrip");
    }
}
