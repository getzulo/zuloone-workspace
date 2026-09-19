using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class CrmPaperXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid LeadScriptId = Guid.Parse("c14a2f8d-d47f-4e8c-b739-1fa6372b012b");
    private static readonly Guid LeadTypeId = Guid.Parse("64daae1f-09b3-47f7-b5b5-c5544a102d98");
    private static readonly Guid LoyaltyScriptId = Guid.Parse("a7ac41eb-418a-45b5-90d9-bb1b8fb03ebf");
    private static readonly Guid LoyaltyTypeId = Guid.Parse("2a5b0d98-4c3e-4f7a-9b8d-9e2f3a0b6c10");

    [IntegrationTest("SalesLeadXr печатает лид: тема, клиент, без прайсинга")]
    public async Task SalesLeadXrPrintsSubjectNotMoney()
    {
        var script = await Metadata.GetScriptAsync(LeadScriptId);
        Assert.IsTrue(script != null, "скрипт SalesLeadXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<SalesLead>", StringComparison.Ordinal),
            "форма печатает лид");
        Assert.IsTrue(script.Code.Contains("FormatAsync", StringComparison.Ordinal), "имена через IReferenceDisplay");
        Assert.IsTrue(script.Code.Contains("Subject", StringComparison.Ordinal), "тема в шапке");
        Assert.IsTrue(script.Code.Contains("Customer", StringComparison.Ordinal), "клиент");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "лид без сумм продажи");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", LeadTypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == LeadScriptId), "скрипт привязан к SalesLead");
    }

    [IntegrationTest("LoyaltyRedemptionXr печатает списание: клиент и баллы, без прайсинга")]
    public async Task LoyaltyRedemptionXrPrintsCustomerAndPoints()
    {
        var script = await Metadata.GetScriptAsync(LoyaltyScriptId);
        Assert.IsTrue(script != null, "скрипт LoyaltyRedemptionXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<LoyaltyRedemption>", StringComparison.Ordinal),
            "форма печатает списание баллов");
        Assert.IsTrue(script.Code.Contains("FormatAsync", StringComparison.Ordinal), "имена через IReferenceDisplay");
        Assert.IsTrue(script.Code.Contains("Customer", StringComparison.Ordinal), "клиент");
        Assert.IsTrue(script.Code.Contains("doc.Points", StringComparison.Ordinal), "баллы с шапки");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "списание баллов не считает цену продажи");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", LoyaltyTypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == LoyaltyScriptId), "скрипт привязан к LoyaltyRedemption");
    }
}
