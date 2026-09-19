using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class ProductionOrderXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid ScriptId = Guid.Parse("caa45c2d-95c3-456f-a212-87f201b20e1d");
    private static readonly Guid TypeId = Guid.Parse("3a0b2c8f-5e9d-4f1a-8b2c-2d6e7f3a8b10");

    [IntegrationTest("ProductionOrderXr печатает наряд: изделие, компоненты, без прайсинга")]
    public async Task ProductionOrderXrPrintsComponentsNotMoney()
    {
        var script = await Metadata.GetScriptAsync(ScriptId);
        Assert.IsTrue(script != null, "скрипт ProductionOrderXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<ProductionOrder>", StringComparison.Ordinal),
            "форма печатает производственный заказ");
        Assert.IsTrue(script.Code.Contains("FormatAsync", StringComparison.Ordinal), "имена через IReferenceDisplay");
        Assert.IsTrue(script.Code.Contains("Product", StringComparison.Ordinal), "изделие в шапке");
        Assert.IsTrue(script.Code.Contains("QtyRequired", StringComparison.Ordinal), "потребность компонента");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "наряд без сумм");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", TypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == ScriptId), "скрипт привязан к ProductionOrder");
    }
}
