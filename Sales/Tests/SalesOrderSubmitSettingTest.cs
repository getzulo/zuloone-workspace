using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

public class SalesOrderSubmitSettingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
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
        legalEntity.RegistrationNumber = $"REG-SOS-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"SP-{Db.NewId():N}"[..12];
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
        cellType.Code = $"PICK-{Db.NewId():N}"[..12];
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
        unit.Code = $"PCS-{Db.NewId():N}"[..12];
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Db.NewId():N}"[..12];
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bread";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Image = TinyPng;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Store 12";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop A";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "A-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
        };
    }

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
        order.Outlet = s.Outlet;
        order.Contract = s.Contract;
        order.Location = s.Location;
        order.DeliveryDate = DateTime.UtcNow.Date.AddDays(1);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    private static async Task SetConfirmOnSubmitAsync(bool value)
    {
        var rows = await DictionaryManager.GetRecordsAsync<SalesSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<SalesSettings>();
        settings.ConfirmOrderOnSubmit = value;
        settings.AllowBackorder = false;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    [IntegrationTest("По умолчанию Submit оставляет Submitted, счёта нет")]
    public async Task DefaultSubmitStaysSubmitted()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);
        await SetConfirmOnSubmitAsync(false);

        var order = await NewOrderAsync(s, 4m, 5m);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);

        var stored = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(stored!.Subtype == SalesOrder.Subtypes.Submitted,
            "Submitted, факт {0}", stored.Subtype);
        var invoices = await DocumentManager.QueryDocumentsAsync<SalesRealization>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 0, "без Approve счёта нет, факт {0}", invoices.Count);
    }

    [IntegrationTest("Флаг вкл: Submit сразу Confirmed и Reserved-счёт")]
    public async Task FlagOnSubmitConfirmsAndInvoices()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);
        await SetConfirmOnSubmitAsync(true);

        var order = await NewOrderAsync(s, 4m, 5m);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);

        var stored = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(stored!.Subtype == SalesOrder.Subtypes.Confirmed,
            "Confirmed, факт {0}", stored.Subtype);
        var invoices = await DocumentManager.QueryDocumentsAsync<SalesRealization>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 1, "один счёт, факт {0}", invoices.Count);
        Assert.IsTrue(invoices[0].Subtype == SalesRealization.Subtypes.Reserved,
            "счёт Reserved, факт {0}", invoices[0].Subtype);
    }

    [IntegrationTest("Флаг вкл и нехватка остатка: заказ остаётся Draft, счёта нет")]
    public async Task FlagOnOverstockLeavesDraft()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 1m);
        await SetConfirmOnSubmitAsync(true);

        var order = await NewOrderAsync(s, 4m, 5m);
        var commandId = await Db.FindCommandIdAsync("document", "SubmitSalesOrder");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("остатка"),
            "пользователь видит отказ: {0}", string.Join("; ", run.ClientMessages));

        var stored = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(stored!.Subtype == SalesOrder.Subtypes.Draft,
            "остался Draft, факт {0}", stored.Subtype);
        var invoices = await DocumentManager.QueryDocumentsAsync<SalesRealization>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 0, "счёта нет, факт {0}", invoices.Count);
    }

    [IntegrationTest("Флаг выкл: Approve по-прежнему Confirmed и счёт")]
    public async Task FlagOffApproveStillInvoices()
    {
        var s = await SetupAsync();
        await StockInAsync(s, 10m);
        await SetConfirmOnSubmitAsync(false);

        var order = await NewOrderAsync(s, 4m, 5m);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);
        await RunCommandAsync("ApproveSalesOrder", order.MetaId);

        var stored = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(stored!.Subtype == SalesOrder.Subtypes.Confirmed, "Confirmed, факт {0}", stored.Subtype);
        var invoices = await DocumentManager.QueryDocumentsAsync<SalesRealization>($"SourceOrder = '{order.MetaId}'");
        Assert.IsTrue(invoices.Count == 1, "один счёт, факт {0}", invoices.Count);
    }
}
