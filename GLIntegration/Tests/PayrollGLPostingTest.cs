using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Разноска начисления ФОТ в главную книгу: Dr расход на оплату труда /
// Cr задолженность перед сотрудниками. Третий потребитель GeneralLedgerService
// после продаж и закупок — проверяем, что механика повторяется на подсистеме
// без склада и контрагента, где юрлицо берётся через подразделение.
public class PayrollGLPostingTest : IntegrationTestScriptBase
{
    [IntegrationTest("Начисление ФОТ разносится в GL: расход = задолженность")]
    public async Task AccrualPostsToLedger()
    {
        var today = DateTime.UtcNow.Date;

        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-PGL-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?>
            { ["Code"] = $"OPS-{Db.NewId():N}"[..12], ["Name"] = "Operations" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?>
            { ["Name"] = "Цех", ["LegalEntity"] = le, ["DivisionType"] = dt });

        var pos = await Db.InsertAsync("Position", new Dictionary<string, object?>
            { ["Name"] = "Мастер", ["HourlyRate"] = 50m });
        var emp = await Db.InsertAsync("Employee", new Dictionary<string, object?>
            { ["Name"] = "Иванов", ["Division"] = div, ["Position"] = pos, ["HireDate"] = today, ["IsActive"] = true });

        // Счета профиля: расход на оплату труда и задолженность перед сотрудниками.
        await Db.InsertAsync("ChartOfAccounts", new Dictionary<string, object?>
            { ["Code"] = "7000", ["Name"] = "Payroll expense", ["AccountType"] = (int)ZuloOne.Runtime.Generated.AccountType.Expense, ["IsPostable"] = true, ["Currency"] = currency });
        await Db.InsertAsync("ChartOfAccounts", new Dictionary<string, object?>
            { ["Code"] = "2100", ["Name"] = "Payroll liability", ["AccountType"] = (int)ZuloOne.Runtime.Generated.AccountType.Liability, ["IsPostable"] = true, ["Currency"] = currency });
        await Db.InsertAsync("AccountingSettings", new Dictionary<string, object?>
        {
            ["ArAccountCode"] = "1200", ["RevenueAccountCode"] = "4000",
            ["InventoryAccountCode"] = "1400", ["PayableAccountCode"] = "2000",
            ["PayrollExpenseAccountCode"] = "7000", ["PayrollLiabilityAccountCode"] = "2100",
        });

        var fy = await Db.InsertAsync("FiscalYear", new Dictionary<string, object?>
            { ["Code"] = "FY", ["StartDate"] = today.AddMonths(-6), ["EndDate"] = today.AddMonths(6), ["IsClosed"] = false });
        await Db.InsertAsync("FiscalPeriod", new Dictionary<string, object?>
            { ["Code"] = "P1", ["FiscalYear"] = fy, ["FromDate"] = today.AddDays(-15), ["ToDate"] = today.AddDays(15), ["Status"] = "Open" });

        var accrual = await Db.CreateDocumentAsync("PayrollAccrual",
            new Dictionary<string, object?> { ["Division"] = div },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Employee"] = emp, ["Amount"] = 700m } } },
            subtype: "Draft");
        await Db.ChangeSubtypeAsync("PayrollAccrual", accrual, "Posted");

        // GL несёт динамические аналитики — баланс схлопывается, поэтому суммируем.
        decimal debit = 0m, credit = 0m;
        foreach (var r in await Db.QueryBalancesAsync("GL"))
        {
            debit += Convert.ToDecimal(r["Debit"]);
            credit += Convert.ToDecimal(r["Credit"]);
        }
        Assert.IsTrue(debit == 700m, "дебет GL = 700 (расход на оплату труда), факт {0}", debit);
        Assert.IsTrue(credit == 700m, "кредит GL = 700 (задолженность перед сотрудниками), факт {0}", credit);

        // Проводка должна быть привязана к начислению — родословная документов.
        var edges = await Db.GetDocumentFamilyEdgesAsync((Guid)accrual);
        Assert.IsTrue(edges.Count > 0, "начисление связано с порождённой проводкой ГК");
    }
}
