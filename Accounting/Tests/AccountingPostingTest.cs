using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Integration coverage for the Accounting foundation: a balanced journal entry posts
// to the general ledger, and the double-entry guard rejects an unbalanced one.
public class AccountingPostingTest : IntegrationTestScriptBase
{
    // Shared master data for a posting scenario (each case runs in its own rolled-back
    // transaction, so every case builds its own).
    private async Task<(Guid LegalEntity, Guid Currency, Guid FiscalPeriod, Guid Cash, Guid Revenue)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-ACC-1", ["Country"] = country, ["Currency"] = currency });
        var fy = await Db.InsertAsync("FiscalYear", new Dictionary<string, object?>
            { ["Code"] = "FY2026", ["StartDate"] = new DateTime(2026, 1, 1), ["EndDate"] = new DateTime(2026, 12, 31), ["IsClosed"] = false });
        var fp = await Db.InsertAsync("FiscalPeriod", new Dictionary<string, object?>
            { ["FiscalYear"] = fy, ["Code"] = "2026-08", ["FromDate"] = new DateTime(2026, 8, 1), ["ToDate"] = new DateTime(2026, 8, 31), ["Status"] = "Open" });
        var cash = await Db.InsertAsync("ChartOfAccounts", new Dictionary<string, object?>
            { ["Code"] = "1000", ["Name"] = "Cash", ["AccountType"] = "Asset", ["IsPostable"] = true });
        var revenue = await Db.InsertAsync("ChartOfAccounts", new Dictionary<string, object?>
            { ["Code"] = "4000", ["Name"] = "Revenue", ["AccountType"] = "Income", ["IsPostable"] = true });
        return (le, currency, fp, cash, revenue);
    }

    private async Task<Guid> NewEntryAsync(
        (Guid LegalEntity, Guid Currency, Guid FiscalPeriod, Guid Cash, Guid Revenue) s,
        decimal debit, decimal credit, string description)
        => await Db.CreateDocumentAsync("JournalEntry",
            new Dictionary<string, object?> { ["LegalEntity"] = s.LegalEntity, ["Currency"] = s.Currency, ["FiscalPeriod"] = s.FiscalPeriod, ["Description"] = description },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[]
                {
                    new Dictionary<string, object?> { ["Account"] = s.Cash, ["Debit"] = debit, ["Credit"] = 0m },
                    new Dictionary<string, object?> { ["Account"] = s.Revenue, ["Debit"] = 0m, ["Credit"] = credit },
                },
            });

    [IntegrationTest("Сбалансированная проводка разносится в GL")]
    public async Task BalancedEntryPostsToLedger()
    {
        var s = await SetupAsync();
        var doc = await NewEntryAsync(s, debit: 100m, credit: 100m, "Balanced");

        // The posting runs on the state transition Draft → Posted (the subtype's
        // transaction chain), not on plain creation.
        await Db.ChangeSubtypeAsync("JournalEntry", doc, "Posted");

        var movements = await Db.QueryMovementsAsync("GL");
        Assert.IsTrue(movements.Count == 2, "ожидалось 2 движения GL (по строке на счёт), а не {0}", movements.Count);

        decimal debit = 0m, credit = 0m;
        foreach (var m in movements)
        {
            debit += Convert.ToDecimal(m["Debit"]);
            credit += Convert.ToDecimal(m["Credit"]);
        }
        Assert.IsTrue(debit == 100m, "сумма дебета GL должна быть 100, а не {0}", debit);
        Assert.IsTrue(credit == 100m, "сумма кредита GL должна быть 100, а не {0}", credit);
    }

    [IntegrationTest("Откат из Posted снимает движения GL")]
    public async Task UnpostReversesLedger()
    {
        var s = await SetupAsync();
        var doc = await NewEntryAsync(s, debit: 100m, credit: 100m, "Balanced");
        await Db.ChangeSubtypeAsync("JournalEntry", doc, "Posted");
        Assert.IsTrue((await Db.QueryMovementsAsync("GL")).Count == 2, "проводка должна была разнестись перед откатом");

        await Db.ChangeSubtypeAsync("JournalEntry", doc, "Draft");
        Assert.IsTrue((await Db.QueryMovementsAsync("GL")).Count == 0, "возврат в Draft должен снять движения GL");
    }

    [IntegrationTest("Несбалансированная проводка отклоняется")]
    public async Task UnbalancedEntryIsRejected()
    {
        var s = await SetupAsync();
        var doc = await NewEntryAsync(s, debit: 100m, credit: 60m, "Unbalanced");

        var rejected = false;
        try
        {
            await Db.ChangeSubtypeAsync("JournalEntry", doc, "Posted");
            // No throw → the guard must at least have blocked the movements.
            rejected = (await Db.QueryMovementsAsync("GL")).Count == 0;
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "несбалансированная проводка (Дт 100 / Кт 60) должна быть отклонена");
    }
}
