using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Оприходование заказа порождает расчёт ВХОДНОГО налога — зеркало выходного
// у счёта продажи. Проверяем направление (INPUT, а не OUTPUT: перепутанное
// направление молча превратит возмещаемый налог в налог к уплате) и то, что
// налоговый контур остаётся необязательным.
public class PurchaseInputTaxTest : IntegrationTestScriptBase
{
    private async Task<(Guid Location, Guid Item, Guid Supplier)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Saudi Riyal", ["Code"] = "SAR", ["Symbol"] = "﷼" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Saudi Arabia", ["CodeISO2"] = "SA", ["CodeISO3"] = "SAU", ["PhoneCode"] = "966" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME KSA", ["RegistrationNumber"] = "REG-IN-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?>
            { ["Code"] = $"WH-{Db.NewId():N}"[..12], ["Name"] = "Warehouse" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?>
            { ["Name"] = "Main", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var store = await Db.InsertAsync("Store", new Dictionary<string, object?>
            { ["Name"] = "Central", ["Division"] = div, ["IsSimple"] = true });
        var zone = await Db.InsertAsync("StoreZone", new Dictionary<string, object?>
            { ["Name"] = "Зона", ["Store"] = store, ["IsBarcodeTracking"] = false });
        var ct = await Db.InsertAsync("StoreCellType", new Dictionary<string, object?>
            { ["Code"] = $"RCV-{Db.NewId():N}"[..12], ["Name"] = "Receiving" });
        var loc = await Db.InsertAsync("StoreCell", new Dictionary<string, object?>
            { ["Name"] = "R-01", ["Type"] = ct, ["StoreZone"] = zone, ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 1 });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?>
            { ["Name"] = "Piece", ["Code"] = $"PCS-{Db.NewId():N}"[..12] });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?>
            { ["Code"] = $"RAW-{Db.NewId():N}"[..12], ["Name"] = "Raw material" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Bolt", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom, ["IsRawMaterial"] = true });
        var supplier = await Db.InsertAsync("Supplier", new Dictionary<string, object?> { ["Name"] = "Bolt Supply Co" });

        return ((Guid)loc, (Guid)item, (Guid)supplier);
    }

    /// <summary>Налоговый контур: справочники, ОБА направления и код по умолчанию.</summary>
    private async Task<Guid> ConfigureTaxAsync()
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

        var codeValue = $"IN-{Db.NewId():N}"[..10];
        await Db.InsertAsync("TaxCode", new Dictionary<string, object?>
            { ["Code"] = codeValue, ["Name"] = "Standard 15%", ["Tax"] = tax, ["TaxCategory"] = category, ["TaxRate"] = rate, ["EffectiveFrom"] = from });
        var input = await Db.InsertAsync("TaxDirection", new Dictionary<string, object?>
            { ["Code"] = "INPUT", ["Name"] = "Input" });
        await Db.InsertAsync("TaxDirection", new Dictionary<string, object?>
            { ["Code"] = "OUTPUT", ["Name"] = "Output" });
        await Db.InsertAsync("TaxSettings", new Dictionary<string, object?>
            { ["DefaultTaxCode"] = codeValue, ["PricesIncludeTax"] = false });
        return (Guid)input;
    }

    private async Task<Guid> NewOrderAsync((Guid Location, Guid Item, Guid Supplier) s, decimal qty, decimal price)
        => (Guid)await Db.CreateDocumentAsync("PurchaseOrder",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = qty, ["UnitPrice"] = price } },
            });

    [IntegrationTest("Оприходование порождает расчёт входного налога")]
    public async Task ReceiptCreatesInputTax()
    {
        var s = await SetupAsync();
        var input = await ConfigureTaxAsync();

        // 10 × 3 = 30 базы, ставка 15% → налог 4.5.
        var po = await NewOrderAsync(s, qty: 10m, price: 3m);
        await Db.ChangeSubtypeAsync("PurchaseOrder", po, "Received");

        var calcs = await Db.QueryAsync("TaxCalculation", null);
        Assert.IsTrue(calcs.Count == 1, "приход должен породить один расчёт налога, факт {0}", calcs.Count);

        var lines = await Db.QueryAsync("TP_TaxCalculationLines", $"OwnerMetaId = '{calcs[0]["MetaId"]}'");
        Assert.IsTrue(lines.Count == 1, "одна строка налога, факт {0}", lines.Count);
        Assert.IsTrue(Convert.ToDecimal(lines[0]["TaxBase"]) == 30m,
            "база = 10 × 3 = 30, факт {0}", lines[0]["TaxBase"]);
        Assert.IsTrue(Convert.ToDecimal(lines[0]["TaxAmount"]) == 4.5m,
            "налог = 30 × 15% = 4.5, факт {0}", lines[0]["TaxAmount"]);

        // Направление именно ВХОДНОЕ: перепутанное молча превратит возмещаемый
        // налог в налог к уплате, и декларация сойдётся с обратным знаком.
        Assert.IsTrue((Guid)lines[0]["Direction"]! == input,
            "направление расчёта должно быть INPUT");

        var edges = await Db.GetDocumentFamilyEdgesAsync(po);
        Assert.IsTrue(edges.Count > 0, "расчёт налога связан с заказом");
    }

    [IntegrationTest("Без настроенного налога приход проводится как раньше")]
    public async Task NoTaxConfigStillReceives()
    {
        var s = await SetupAsync();
        // ConfigureTaxAsync НЕ вызываем: кода налога по умолчанию нет.

        var po = await NewOrderAsync(s, qty: 4m, price: 5m);
        await Db.ChangeSubtypeAsync("PurchaseOrder", po, "Received");

        var doc = await Db.GetAsync("PurchaseOrder", po);
        Assert.IsTrue((doc?["Subtype"] as string) == "Received",
            "приход проведён несмотря на ненастроенный налог, факт {0}", doc?["Subtype"]);
        Assert.IsTrue((await Db.QueryAsync("TaxCalculation", null)).Count == 0, "расчёт налога не создан");

        // И сам приход при этом отработал полностью.
        decimal stock = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Stock", $"[Cell] = '{s.Location}'")) stock += Convert.ToDecimal(r["Qty"]);
        Assert.IsTrue(stock == 4m, "остаток ячейки 4, факт {0}", stock);
    }
}
