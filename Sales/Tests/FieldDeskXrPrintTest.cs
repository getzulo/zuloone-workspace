using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class FieldDeskXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid RouteScriptId = Guid.Parse("c147e05d-1d2d-414e-8399-58aae465dc94");
    private static readonly Guid RouteTypeId = Guid.Parse("8f3a1c20-6d4b-4e91-a2c7-1b9e0d5f4a31");
    private static readonly Guid VisitScriptId = Guid.Parse("72194e53-c168-4fee-9ce6-4c81f10a73c3");
    private static readonly Guid VisitTypeId = Guid.Parse("9a4b2d32-7e5c-4f13-b3d8-2c0f1e7b5b43");

    [IntegrationTest("VisitRouteXr печатает план дня: агент, точки, без сумм")]
    public async Task VisitRouteXrPrintsAgentAndStops()
    {
        var script = await Metadata.GetScriptAsync(RouteScriptId);
        Assert.IsTrue(script != null, "скрипт VisitRouteXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<VisitRoute>", StringComparison.Ordinal),
            "форма печатает маршрут визитов");
        Assert.IsTrue(script.Code.Contains("FormatAsync", StringComparison.Ordinal), "имена через IReferenceDisplay");
        Assert.IsTrue(script.Code.Contains("Agent", StringComparison.Ordinal), "агент в шапке");
        Assert.IsTrue(script.Code.Contains("Outlet", StringComparison.Ordinal), "точка на остановке");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "план дня без сумм продажи");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", RouteTypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == RouteScriptId), "скрипт привязан к VisitRoute");
    }

    [IntegrationTest("AgentVisitXr печатает визит: клиент, точка, чек-ин")]
    public async Task AgentVisitXrPrintsCustomerAndCheckIn()
    {
        var script = await Metadata.GetScriptAsync(VisitScriptId);
        Assert.IsTrue(script != null, "скрипт AgentVisitXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<AgentVisit>", StringComparison.Ordinal),
            "форма печатает визит");
        Assert.IsTrue(script.Code.Contains("FormatAsync", StringComparison.Ordinal), "имена через IReferenceDisplay");
        Assert.IsTrue(script.Code.Contains("Customer", StringComparison.Ordinal), "клиент");
        Assert.IsTrue(script.Code.Contains("CheckedInAt", StringComparison.Ordinal), "чек-ин");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "визит без сумм продажи");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", VisitTypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == VisitScriptId), "скрипт привязан к AgentVisit");
    }
}
