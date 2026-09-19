using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

public class HrPaperXrPrintTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid TimeSheetScriptId = Guid.Parse("8abbdac3-bcd4-4247-85c9-5dae96712e3a");
    private static readonly Guid TimeSheetTypeId = Guid.Parse("0f1a2b3c-4d5e-4f6a-8b7c-9d0e1f2a3b4f");
    private static readonly Guid AccrualScriptId = Guid.Parse("b7b04b57-e261-49d5-b4a7-91c52a4cb281");
    private static readonly Guid AccrualTypeId = Guid.Parse("a0d03063-af77-4fd0-886b-223a9731f105");
    private static readonly Guid PaymentScriptId = Guid.Parse("846f22ae-0192-427f-9ed0-d82b9fedde82");
    private static readonly Guid PaymentTypeId = Guid.Parse("aa4abe6c-0f27-42f0-9284-5f084e6b7274");

    [IntegrationTest("TimeSheetXr печатает табель: подразделение, период, часы")]
    public async Task TimeSheetXrPrintsHoursNotMoney()
    {
        var script = await Metadata.GetScriptAsync(TimeSheetScriptId);
        Assert.IsTrue(script != null, "скрипт TimeSheetXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<TimeSheet>", StringComparison.Ordinal),
            "форма печатает табель");
        Assert.IsTrue(script.Code.Contains("FormatAsync", StringComparison.Ordinal), "имена через IReferenceDisplay");
        Assert.IsTrue(script.Code.Contains("PeriodFrom", StringComparison.Ordinal), "период с");
        Assert.IsTrue(script.Code.Contains("line.Hours", StringComparison.Ordinal), "часы строки");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "табель без прайсинга продаж");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", TimeSheetTypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == TimeSheetScriptId), "скрипт привязан к TimeSheet");
    }

    [IntegrationTest("SocialInsuranceAccrualXr печатает начисление: сотрудник, доли, без прайсинга")]
    public async Task SocialInsuranceAccrualXrPrintsContributions()
    {
        var script = await Metadata.GetScriptAsync(AccrualScriptId);
        Assert.IsTrue(script != null, "скрипт SocialInsuranceAccrualXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<SocialInsuranceAccrual>", StringComparison.Ordinal),
            "форма печатает начисление соцстраха");
        Assert.IsTrue(script.Code.Contains("EmployeeContribution", StringComparison.Ordinal), "доля работника");
        Assert.IsTrue(script.Code.Contains("EmployerContribution", StringComparison.Ordinal), "доля работодателя");
        Assert.IsTrue(
            !script.Code.Contains("IPricingService", StringComparison.Ordinal),
            "взносы не из прайсинга продаж");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", AccrualTypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == AccrualScriptId), "скрипт привязан к SocialInsuranceAccrual");
    }

    [IntegrationTest("SocialInsurancePaymentXr печатает платёж в фонд: сотрудник и доли")]
    public async Task SocialInsurancePaymentXrPrintsContributions()
    {
        var script = await Metadata.GetScriptAsync(PaymentScriptId);
        Assert.IsTrue(script != null, "скрипт SocialInsurancePaymentXrPrintForm есть");
        Assert.IsTrue(
            script!.Code.Contains("GetDocumentAsync<SocialInsurancePayment>", StringComparison.Ordinal),
            "форма печатает платёж в фонд");
        Assert.IsTrue(script.Code.Contains("EmployeeContribution", StringComparison.Ordinal), "доля работника");
        var bound = await Metadata.GetScriptsByObjectAsync("Document", PaymentTypeId);
        Assert.IsTrue(bound.Any(x => x.MetaId == PaymentScriptId), "скрипт привязан к SocialInsurancePayment");
    }
}
