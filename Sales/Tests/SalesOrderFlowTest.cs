using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Testing;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public class SalesOrderFlowTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();
    private static ISalesFulfillmentService Fulfillment => GetService<ISalesFulfillmentService>();

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
        zone.Name = "Зона";
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

    private static async Task<decimal> ReceivableAsync()
    {
        decimal sum = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("Receivable"))
            sum += Convert.ToDecimal(r["Amount"]);
        return sum;
    }

    private static async Task<decimal> RevenueAsync()
    {
        decimal sum = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("Revenue"))
            sum += Convert.ToDecimal(r["Amount"]);
        return sum;
    }

    private async Task StockInAsync(Setup s, decimal qty)
    {
        var adjustment = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        adjustment.Cell = s.Location;
        adjustment.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = qty });
        await DocumentManager.SaveDocumentAsync(adjustment);
        adjustment.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(adjustment);
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

    [IntegrationTest("Черновик заказа не резервирует; подтверждение занимает свободный остаток")]
    public async Task ConfirmReservesAndDraftDoesNot()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);

        var order = await NewOrderAsync(s, 4m, 5m);
        Assert.IsTrue(await ReservedAsync(s) == 0m, "черновик не резервирует, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await Fulfillment.AvailableQtyAsync(s.Location, s.Item) == 10m,
            "свободно 10, факт {0}", await Fulfillment.AvailableQtyAsync(s.Location, s.Item));

        order.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(order);

        Assert.IsTrue(await ReservedAsync(s) == 4m, "резерв 4, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await StockAsync(s) == 10m, "склад не списан, факт {0}", await StockAsync(s));
        Assert.IsTrue(await Fulfillment.AvailableQtyAsync(s.Location, s.Item) == 6m,
            "свободно 6, факт {0}", await Fulfillment.AvailableQtyAsync(s.Location, s.Item));
        Assert.IsTrue(await ReceivableAsync() == 0m, "долга ещё нет");
    }

    [IntegrationTest("Подтверждение сверх свободного остатка отклоняется")]
    public async Task ConfirmBeyondFreeStockIsRejected()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 5m);
        var first = await NewOrderAsync(s, 4m, 5m);
        first.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(first);

        var second = await NewOrderAsync(s, 3m, 5m);
        var rejected = false;
        try
        {
            second.Subtype = SalesOrder.Subtypes.Confirmed;
            await DocumentManager.SaveDocumentAsync(second);
            rejected = await ReservedAsync(s) == 4m;
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "второй заказ на 3 при свободных 1 должен быть отклонён");
    }

    [IntegrationTest("Доставка заказа выставляет счёт: склад −, резерв 0, долг и выручка +")]
    public async Task DeliverIssuesInvoice()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);
        var order = await NewOrderAsync(s, 3m, 5m);
        order.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = SalesOrder.Subtypes.Delivered;
        await DocumentManager.SaveDocumentAsync(order);

        Assert.IsTrue(await ReservedAsync(s) == 0m, "резерв снят, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await StockAsync(s) == 7m, "склад 7, факт {0}", await StockAsync(s));
        Assert.IsTrue(await ReceivableAsync() == 15m, "долг 15, факт {0}", await ReceivableAsync());
        Assert.IsTrue(await RevenueAsync() == 15m, "выручка 15, факт {0}", await RevenueAsync());

        var invoices = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 1, "один счёт, факт {0}", invoices.Count);
        Assert.IsTrue(invoices[0].Subtype == SalesInvoice.Subtypes.Issued, "счёт выставлен");
    }

    [IntegrationTest("Повтор доставки не плодит второй счёт")]
    public async Task DeliverIsIdempotent()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);
        var order = await NewOrderAsync(s, 2m, 5m);
        order.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = SalesOrder.Subtypes.Delivered;
        await DocumentManager.SaveDocumentAsync(order);

        var again = await Fulfillment.InvoiceOrderAsync(order.MetaId);
        var invoices = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 1 && invoices[0].MetaId == again,
            "повтор вернул тот же счёт, счетов {0}", invoices.Count);
    }

    [IntegrationTest("Отмена подтверждённого заказа снимает резерв")]
    public async Task CancelReleasesReserve()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);
        var order = await NewOrderAsync(s, 4m, 5m);
        order.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = SalesOrder.Subtypes.Cancelled;
        await DocumentManager.SaveDocumentAsync(order);

        Assert.IsTrue(await ReservedAsync(s) == 0m, "резерв снят отменой, факт {0}", await ReservedAsync(s));
        Assert.IsTrue(await StockAsync(s) == 10m, "склад на месте");
        Assert.IsTrue((await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{order.MetaId}'")).Count == 0,
            "отказ без счёта");
    }

    [IntegrationTest("Рейс: доставка, недовоз и отказ")]
    public async Task TripDeliversPartialAndRefuses()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 20m);

        var full = await NewOrderAsync(s, 4m, 5m);
        full.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(full);

        var shortShip = await NewOrderAsync(s, 6m, 5m);
        shortShip.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(shortShip);

        var refused = await NewOrderAsync(s, 3m, 5m);
        refused.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(refused);

        var trip = await DocumentManager.NewDocumentAsync<DeliveryTrip>();
        trip.DeliveryDate = DateTime.UtcNow.Date;
        trip.Lines.Add(new DeliveryTripLinesTablePartRow { SalesOrder = full.MetaId, StopSequence = 1, Outcome = "Delivered" });
        trip.Lines.Add(new DeliveryTripLinesTablePartRow { SalesOrder = shortShip.MetaId, StopSequence = 2, Outcome = "Partial", QtyShipped = 2m });
        trip.Lines.Add(new DeliveryTripLinesTablePartRow { SalesOrder = refused.MetaId, StopSequence = 3, Outcome = "Refused" });
        await DocumentManager.SaveDocumentAsync(trip);
        trip.Subtype = DeliveryTrip.Subtypes.Completed;
        await DocumentManager.SaveDocumentAsync(trip);

        var fullReloaded = await DocumentManager.GetDocumentAsync<SalesOrder>(full.MetaId);
        var shortReloaded = await DocumentManager.GetDocumentAsync<SalesOrder>(shortShip.MetaId);
        var refusedReloaded = await DocumentManager.GetDocumentAsync<SalesOrder>(refused.MetaId);

        Assert.IsTrue(fullReloaded!.Subtype == SalesOrder.Subtypes.Delivered, "полная доставка");
        Assert.IsTrue(shortReloaded!.Subtype == SalesOrder.Subtypes.Delivered, "недовоз закрывает заказ");
        Assert.IsTrue(refusedReloaded!.Subtype == SalesOrder.Subtypes.Cancelled, "отказ отменяет заказ");

        var fullInv = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{full.MetaId}'");
        var shortInv = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{shortShip.MetaId}'");
        var refusedInv = await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{refused.MetaId}'");

        Assert.IsTrue(fullInv.Count == 1, "счёт на полную доставку, факт {0}", fullInv.Count);
        var fullDoc = await DocumentManager.GetDocumentAsync<SalesInvoice>(fullInv[0].MetaId);
        Assert.IsTrue(fullDoc!.Lines.Sum(l => l.Quantity) == 4m, "счёт на 4, факт {0}", fullDoc.Lines.Sum(l => l.Quantity));

        Assert.IsTrue(shortInv.Count == 1, "счёт на недовоз, факт {0}", shortInv.Count);
        var shortDoc = await DocumentManager.GetDocumentAsync<SalesInvoice>(shortInv[0].MetaId);
        Assert.IsTrue(shortDoc!.Lines.Sum(l => l.Quantity) == 2m, "счёт на 2, факт {0}", shortDoc.Lines.Sum(l => l.Quantity));
        Assert.IsTrue(refusedInv.Count == 0, "отказ без счёта");
        Assert.IsTrue(await ReservedAsync(s) == 0m, "резерв пуст после рейса");
        Assert.IsTrue(await StockAsync(s) == 14m, "склад 20−4−2, факт {0}", await StockAsync(s));
    }

    [IntegrationTest("Возврат возвращает товар и сторнирует долг с выручкой")]
    public async Task ReturnRestoresStockAndDebt()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);
        var order = await NewOrderAsync(s, 4m, 5m);
        order.Subtype = SalesOrder.Subtypes.Confirmed;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = SalesOrder.Subtypes.Delivered;
        await DocumentManager.SaveDocumentAsync(order);

        var invoice = (await DocumentManager.QueryDocumentsAsync<SalesInvoice>($"SourceOrder = '{order.MetaId}'")).Single();

        var ret = await DocumentManager.NewDocumentAsync<SalesReturn>();
        ret.Customer = s.Customer;
        ret.Location = s.Location;
        ret.OriginalInvoice = invoice.MetaId;
        ret.Lines.Add(new SalesReturnLinesTablePartRow { Item = s.Item, Quantity = 4m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(ret);
        ret.Subtype = SalesReturn.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(ret);

        Assert.IsTrue(await StockAsync(s) == 10m, "товар вернулся, факт {0}", await StockAsync(s));
        Assert.IsTrue(await ReceivableAsync() == 0m, "долг закрыт возвратом, факт {0}", await ReceivableAsync());
        Assert.IsTrue(await RevenueAsync() == 0m, "выручка сторнирована, факт {0}", await RevenueAsync());
    }
}
