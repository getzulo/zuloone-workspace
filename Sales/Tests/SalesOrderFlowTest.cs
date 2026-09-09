using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Testing;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Process B integration tests.
// Flow: Draft →(Submit)→ Submitted →(Approve)→ Confirmed → invoice Reserved.
// Invoice: Reserved →(StartPicking)→ Picking →(MarkPacked)→ Packing →(MarkShipped)→ Shipped →(ReleaseRealization)→ Issued.
// On Issued: ReservedStock=0, Stock−, Receivable+, order.Subtype=Delivered.
public class SalesOrderFlowTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();
    private static ISalesFulfillmentService Fulfillment => GetService<ISalesFulfillmentService>();
    // 1×1 white PNG — required because DataService cannot insert null into VARBINARY(MAX)
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Customer;
    }

    private async Task<Setup> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = "EUR";
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = "REG-SO-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "SP";
        divisionType.Name = "SalesPoint";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Shop";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Shop WH";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Zone";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"PICK-{Guid.NewGuid():N}"[..12];
        cellType.Name = "Picking";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "P-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "Piece";
        unit.Code = "PCS";
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "GOODS";
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bread";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Image = TinyPng; // DataService cannot insert null into VARBINARY(MAX)
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Store 12";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Customer = customer.MetaId };
    }

    private static Task<decimal> StockAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item });

    private static Task<decimal> ReservedAsync(Setup s)
        => TotalsManager.GetBalanceAsync("ReservedStock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item });

    private static Task<decimal> ReceivableAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Receivable", "Amount",
            new Dictionary<string, object?> { ["Customer"] = s.Customer });

    private static Task<decimal> RevenueAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Revenue", "Amount",
            new Dictionary<string, object?> { ["Customer"] = s.Customer });

    private async Task RunCommandAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        Assert.IsTrue(run.Success, "команда {0}: {1}", name, run.Message ?? string.Join("; ", run.ClientMessages));
    }

    private async Task StockInAsync(Setup s, decimal qty)
    {
        var adjustment = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        adjustment.Cell = s.Location;
        adjustment.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = qty });
        await DocumentManager.SaveDocumentAsync(adjustment);
        await RunCommandAsync("PostStockAdjustment", adjustment.MetaId);
    }

    private async Task<SalesOrder> NewOrderAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Location = s.Location;
        order.DeliveryDate = DateTime.UtcNow.Date.AddDays(1);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    /// <summary>Submit + Approve creates a realization in Reserved.
    /// Assertions 1 and 2 from the spec.</summary>
    [IntegrationTest("Process B: Approve создаёт реализацию в Reserved; резерв по счёту, склад и долг не тронуты")]
    public async Task ApproveCreatesReservedInvoice()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);

        var order = await NewOrderAsync(s, 4m, 5m);
        Assert.IsTrue(await ReservedAsync(s) == 0m, "до Submit резерв 0");

        await RunCommandAsync("SubmitSalesOrder", order.MetaId);
        Assert.IsTrue(await ReservedAsync(s) == 0m, "после Submit резерв ещё 0 (резерв — через счёт)");

        await RunCommandAsync("ApproveSalesOrder", order.MetaId);

        // Assert 1: invoice exists, SourceOrder set, Subtype == Reserved
        var invoices = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 1, "один счёт, факт {0}", invoices.Count);
        var inv = await DocumentManager.GetDocumentAsync<SalesInvoice>(invoices[0].MetaId);
        Assert.IsTrue(inv!.SourceOrder == order.MetaId, "SourceOrder установлен");
        Assert.IsTrue(inv.Subtype == SalesInvoice.Subtypes.Reserved,
            "счёт в Reserved, факт {0}", inv.Subtype ?? "<null>");

        // Assert 2: ReservedStock = qty; Stock unchanged; Receivable = 0
        Assert.IsTrue(await ReservedAsync(s) == 4m, "резерв 4, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await StockAsync(s) == 10m, "склад не тронут, факт {0}", await StockAsync(s));
        Assert.IsTrue(await ReceivableAsync(s) == 0m, "долг 0 на Reserved");

        var family = await DocumentManager.GetDocumentFamilyAsync(inv.MetaId);
        Assert.IsTrue(!family.Nodes.Any(n => n.DocTypeName == "TaxCalculation"),
            "Reserved не порождает TaxCalculation");
        Assert.IsTrue(await ReceivableAsync(s) == 0m, "после Reserved долг по-прежнему 0");
    }

    /// <summary>Walk invoice through Picking → Packing → Shipped keeps reserve;
    /// Issued drops reserve and writes Stock/Receivable + sets order Delivered.
    /// Assertions 3 and 4 from the spec.</summary>
    [IntegrationTest("Process B: Picking/Packing/Shipped хранят резерв; Issued списывает склад и ставит Delivered")]
    public async Task WalkToIssuedSetsDelivered()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);

        var order = await NewOrderAsync(s, 3m, 5m);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);
        await RunCommandAsync("ApproveSalesOrder", order.MetaId);

        var invoices = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{order.MetaId}'");
        var invId = invoices[0].MetaId;

        // Assert 3: Picking, Packing, Shipped — still reserved, no receivable
        await RunCommandAsync("StartPicking", invId);
        Assert.IsTrue(await ReservedAsync(s) == 3m, "Picking: резерв 3, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await ReceivableAsync(s) == 0m, "Picking: долг 0");

        await RunCommandAsync("MarkPacked", invId);
        Assert.IsTrue(await ReservedAsync(s) == 3m, "Packing: резерв 3, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await ReceivableAsync(s) == 0m, "Packing: долг 0");

        await RunCommandAsync("MarkShipped", invId);
        Assert.IsTrue(await ReservedAsync(s) == 3m, "Shipped: резерв 3, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await ReceivableAsync(s) == 0m, "Shipped: долг 0");

        // Assert 4: Issued → ReservedStock=0, Stock−, Receivable+, order=Delivered
        await RunCommandAsync("ReleaseRealization", invId);

        Assert.IsTrue(await ReservedAsync(s) == 0m, "Issued: резерв 0, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await StockAsync(s) == 7m, "Issued: склад 10−3=7, факт {0}", await StockAsync(s));
        Assert.IsTrue(await ReceivableAsync(s) == 15m, "Issued: долг 15, факт {0}", await ReceivableAsync(s));

        var orderReloaded = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(orderReloaded!.Subtype == SalesOrder.Subtypes.Delivered,
            "заказ Delivered, факт {0}", orderReloaded.Subtype ?? "<null>");
    }

    /// <summary>Second Approve / InvoiceOrderAsync does not create a second invoice.
    /// Assertion 5 from the spec.</summary>
    [IntegrationTest("Process B: повтор Approve не плодит второй счёт")]
    public async Task SecondApproveIsIdempotent()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);

        var order = await NewOrderAsync(s, 2m, 5m);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);
        await RunCommandAsync("ApproveSalesOrder", order.MetaId);

        // Programmatic repeat
        var again = await Fulfillment.InvoiceOrderAsync(order.MetaId);
        var invoices = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 1 && invoices[0].MetaId == again,
            "повтор вернул тот же счёт, счетов {0}", invoices.Count);
    }

    /// <summary>Cancel Confirmed → linked invoice (not Issued) cancelled; reserve = 0.
    /// Assertion 6 from the spec.</summary>
    [IntegrationTest("Process B: отмена Confirmed отменяет счёт и снимает резерв")]
    public async Task CancelConfirmedCancelsInvoice()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);

        var order = await NewOrderAsync(s, 4m, 5m);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);
        await RunCommandAsync("ApproveSalesOrder", order.MetaId);

        Assert.IsTrue(await ReservedAsync(s) == 4m, "перед отменой резерв 4");

        await RunCommandAsync("CancelSalesOrder", order.MetaId);

        Assert.IsTrue(await ReservedAsync(s) == 0m, "резерв снят отменой, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await StockAsync(s) == 10m, "склад цел, факт {0}", await StockAsync(s));

        var invoices = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 1, "счёт существует, факт {0}", invoices.Count);
        var inv = await DocumentManager.GetDocumentAsync<SalesInvoice>(invoices[0].MetaId);
        Assert.IsTrue(inv!.Subtype == SalesInvoice.Subtypes.Cancelled,
            "счёт Cancelled, факт {0}", inv.Subtype ?? "<null>");
    }

    /// <summary>Cancel Submitted (before invoice is created) → no invoice, reserve stays 0.</summary>
    [IntegrationTest("Process B: отмена Submitted — счёта нет, резерв 0")]
    public async Task CancelSubmittedNoInvoice()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);

        var order = await NewOrderAsync(s, 4m, 5m);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);
        await RunCommandAsync("CancelSalesOrder", order.MetaId);

        var orderReloaded = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(orderReloaded!.Subtype == SalesOrder.Subtypes.Cancelled,
            "заказ Cancelled, факт {0}", orderReloaded.Subtype ?? "<null>");
        Assert.IsTrue(await ReservedAsync(s) == 0m, "резерв 0, факт {0}", await ReservedAsync(s));
        var invoices = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 0, "счёт не создан, факт {0}", invoices.Count);
    }

    /// <summary>Approve beyond free stock is rejected.</summary>
    [IntegrationTest("Process B: Approve сверх свободного остатка отклоняется")]
    public async Task ApproveBeyondFreeStockIsRejected()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 5m);

        var first = await NewOrderAsync(s, 4m, 5m);
        await RunCommandAsync("SubmitSalesOrder", first.MetaId);
        await RunCommandAsync("ApproveSalesOrder", first.MetaId);

        var second = await NewOrderAsync(s, 3m, 5m);
        await RunCommandAsync("SubmitSalesOrder", second.MetaId);

        var approveId = await Db.FindCommandIdAsync("document", "ApproveSalesOrder");
        var run = await Db.ExecuteDocumentCommandAsync(approveId, second.MetaId);
        var afterSecond = await DocumentManager.GetDocumentAsync<SalesOrder>(second.MetaId);

        Assert.IsTrue(afterSecond!.Subtype == SalesOrder.Subtypes.Submitted,
            "второй заказ остаётся Submitted, факт {0}", afterSecond.Subtype ?? "<null>");
        Assert.IsTrue(!run.Success || string.Join("; ", run.ClientMessages).Contains("остатка"),
            "пользователь видит отказ: {0}", string.Join("; ", run.ClientMessages));
    }

    /// <summary>Return after issued realization restores stock and clears debt.</summary>
    [IntegrationTest("Возврат после выставленной реализации восстанавливает склад и сторнирует долг")]
    public async Task ReturnRestoresStockAndDebt()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);

        // Use POS path (IssueInvoice: Draft→Issued directly) for return setup.
        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 4m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(invoice);
        await RunCommandAsync("IssueInvoice", invoice.MetaId);

        var ret = await DocumentManager.NewDocumentAsync<SalesReturn>();
        ret.Customer = s.Customer;
        ret.Location = s.Location;
        ret.OriginalInvoice = invoice.MetaId;
        ret.Lines.Add(new SalesReturnLinesTablePartRow { Item = s.Item, Quantity = 4m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(ret);
        await RunCommandAsync("PostSalesReturn", ret.MetaId);

        Assert.IsTrue(await StockAsync(s) == 10m, "товар вернулся, факт {0}", await StockAsync(s));
        Assert.IsTrue(await ReceivableAsync(s) == 0m, "долг закрыт возвратом, факт {0}", await ReceivableAsync(s));
        Assert.IsTrue(await RevenueAsync(s) == 0m, "выручка сторнирована, факт {0}", await RevenueAsync(s));
    }
}
