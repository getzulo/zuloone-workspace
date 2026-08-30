using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Выставление счёта порождает расчёт ВЫХОДНОГО налога отдельным документом,
// связанным со счётом. Проверяем и сам факт порождения, и то, что налоговый
// контур необязателен: без кода налога по умолчанию счёт выставляется как
// раньше — иначе включение налогов сломало бы все существующие продажи.
public class SalesOutputTaxTest : IntegrationTestScriptBase
{
    private async Task<(Guid Cell, Guid Item, Guid Customer, Guid Currency)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Saudi Riyal", ["Code"] = "SAR", ["Symbol"] = "﷼" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Saudi Arabia", ["CodeISO2"] = "SA", ["CodeISO3"] = "SAU", ["PhoneCode"] = "966" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME KSA", ["RegistrationNumber"] = "REG-OUT-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?>
            { ["Code"] = $"SP-{Db.NewId():N}"[..12], ["Name"] = "SalesPoint" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?>
            { ["Name"] = "Shop", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var store = await Db.InsertAsync("Store", new Dictionary<string, object?>
            { ["Name"] = "Shop WH", ["Division"] = div, ["IsSimple"] = true });
        var zone = await Db.InsertAsync("StoreZone", new Dictionary<string, object?>
            { ["Name"] = "Зона", ["Store"] = store, ["IsBarcodeTracking"] = false });
        var ct = await Db.InsertAsync("StoreCellType", new Dictionary<string, object?>
            { ["Code"] = $"PICK-{Db.NewId():N}"[..12], ["Name"] = "Picking" });
        var cell = await Db.InsertAsync("StoreCell", new Dictionary<string, object?>
            { ["Name"] = "P-01", ["Type"] = ct, ["StoreZone"] = zone, ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 1 });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?>
            { ["Name"] = "Piece", ["Code"] = $"PCS-{Db.NewId():N}"[..12] });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?>
            { ["Code"] = $"GOODS-{Db.NewId():N}"[..12], ["Name"] = "Finished goods" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Gadget", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom, ["IsSellable"] = true });
        var customer = await Db.InsertAsync("Customer", new Dictionary<string, object?>
            { ["Name"] = "Buyer Ltd", ["CustomerType"] = "B2B" });

        await Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item },
            new Dictionary<string, decimal> { ["Qty"] = 100m });

        return ((Guid)cell, (Guid)item, (Guid)customer, (Guid)currency);
    }

    /// <summary>Налоговый контур: справочники + код налога по умолчанию в настройках.</summary>
    private async Task ConfigureTaxAsync()
    {
        var from = new DateTime(2020, 1, 1);
        var authority = await Db.InsertAsync("TaxAuthority", new Dictionary<string, object?>
            { ["Code"] = $"ZAT-{Db.NewId():N}"[..10], ["Name"] = "ZATCA", ["CountryCode"] = "SA", ["IsActive"] = true });
        var type = await Db.InsertAsync("TaxType", new Dictionary<string, object?>
            { ["Code"] = $"VAT-{Db.NewId():N}"[..10], ["Name"] = "Value added tax", ["Category"] = "VAT" });
        var jur = await Db.InsertAsync("TaxJurisdiction", new Dictionary<string, object?>
            { ["Code"] = $"SA-{Db.NewId():N}"[..10], ["Name"] = "Saudi Arabia", ["CountryCode"] = "SA", ["Level"] = 0 });
        var tax = await Db.InsertAsync("Tax", new Dictionary<string, object?>
            { ["Code"] = $"VT-{Db.NewId():N}"[..10], ["Name"] = "Saudi VAT", ["TaxType"] = type, ["Authority"] = authority, ["Jurisdiction"] = jur, ["EffectiveFrom"] = from });
        var rate = await Db.InsertAsync("TaxRate", new Dictionary<string, object?>
            { ["Tax"] = tax, ["Code"] = $"R-{Db.NewId():N}"[..10], ["Rate"] = 0.15m, ["EffectiveFrom"] = from });
        var category = await Db.InsertAsync("TaxCategory", new Dictionary<string, object?>
            { ["Tax"] = tax, ["Code"] = $"STD-{Db.NewId():N}"[..10], ["Treatment"] = "STANDARD" });

        var codeValue = $"OUT-{Db.NewId():N}"[..10];
        await Db.InsertAsync("TaxCode", new Dictionary<string, object?>
            { ["Code"] = codeValue, ["Name"] = "Standard 15%", ["Tax"] = tax, ["TaxCategory"] = category, ["TaxRate"] = rate, ["EffectiveFrom"] = from });
        await Db.InsertAsync("TaxDirection", new Dictionary<string, object?>
            { ["Code"] = "OUTPUT", ["Name"] = "Output" });
        await Db.InsertAsync("TaxSettings", new Dictionary<string, object?>
            { ["DefaultTaxCode"] = codeValue, ["PricesIncludeTax"] = false });
    }

    [IntegrationTest("Выставление счёта порождает расчёт выходного налога")]
    public async Task IssueCreatesOutputTax()
    {
        var s = await SetupAsync();
        await ConfigureTaxAsync();

        // 4 × 25 = 100 базы, ставка 15% → налог 15.
        var inv = await Db.CreateDocumentAsync("SalesInvoice",
            new Dictionary<string, object?> { ["Customer"] = s.Customer, ["Location"] = s.Cell },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 4m, ["UnitPrice"] = 25m } } });
        await Db.ChangeSubtypeAsync("SalesInvoice", inv, "Issued");

        var calcs = await Db.QueryAsync("TaxCalculation", null);
        Assert.IsTrue(calcs.Count == 1, "счёт должен породить один расчёт налога, факт {0}", calcs.Count);

        var lines = await Db.QueryAsync("TP_TaxCalculationLines", $"OwnerMetaId = '{calcs[0]["MetaId"]}'");
        Assert.IsTrue(lines.Count == 1, "одна строка налога, факт {0}", lines.Count);
        Assert.IsTrue(Convert.ToDecimal(lines[0]["TaxBase"]) == 100m,
            "база = 4 × 25 = 100, факт {0}", lines[0]["TaxBase"]);
        Assert.IsTrue(Convert.ToDecimal(lines[0]["TaxAmount"]) == 15m,
            "налог = 100 × 15% = 15, факт {0}", lines[0]["TaxAmount"]);

        // Расчёт связан со счётом — родословная документов, а не поле-указатель.
        var edges = await Db.GetDocumentFamilyEdgesAsync((Guid)inv);
        Assert.IsTrue(edges.Count > 0, "расчёт налога связан со счётом");
    }

    [IntegrationTest("Без настроенного налога счёт выставляется как раньше")]
    public async Task NoTaxConfigStillIssues()
    {
        var s = await SetupAsync();
        // ConfigureTaxAsync НЕ вызываем: кода налога по умолчанию нет.

        var inv = await Db.CreateDocumentAsync("SalesInvoice",
            new Dictionary<string, object?> { ["Customer"] = s.Customer, ["Location"] = s.Cell },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 2m, ["UnitPrice"] = 10m } } });
        await Db.ChangeSubtypeAsync("SalesInvoice", inv, "Issued");

        var doc = await Db.GetAsync("SalesInvoice", inv);
        Assert.IsTrue((doc?["Subtype"] as string) == "Issued",
            "счёт выставлен несмотря на ненастроенный налог, факт {0}", doc?["Subtype"]);
        var calcs = await Db.QueryAsync("TaxCalculation", null);
        Assert.IsTrue(calcs.Count == 0, "расчёт налога не создан, факт {0}", calcs.Count);
    }
}
