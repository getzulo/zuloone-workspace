using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

public class PurchaseReturnTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Supplier;
    }

    private async Task<Setup> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = $"E{Guid.NewGuid():N}"[..2].ToUpperInvariant();
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = $"{Guid.NewGuid():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Guid.NewGuid():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = $"REG-PR-{Guid.NewGuid():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"WH-{Guid.NewGuid():N}"[..10];
        divisionType.Name = "Warehouse";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Central";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Zone";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"RCV-{Guid.NewGuid():N}"[..12];
        cellType.Name = "Receiving";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "R-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "RAW";
        group.Name = "Raw material";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bolt";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item.IsRawMaterial = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Supplier = supplier.MetaId };
    }

    private async Task<PurchaseOrder> ReceiveAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    [IntegrationTest("Возврат снимает склад и нетто кредиторки")]
    public async Task ReturnClearsStockAndPayable()
    {
        var s = await SetupAsync();
        var order = await ReceiveAsync(s, 10m, 3m);
        Assert.IsTrue(await StockAsync(s) == 10m, "после прихода склад 10");
        Assert.IsTrue(await PayableAsync(s) == 30m, "после прихода долг 30");

        var ret = await DocumentManager.NewDocumentAsync<PurchaseReturn>();
        ret.OriginalOrder = order.MetaId;
        await DocumentManager.SaveDocumentAsync(ret);
        var stamped = await DocumentManager.GetDocumentAsync<PurchaseReturn>(ret.MetaId);
        Assert.IsTrue(stamped!.Supplier == s.Supplier, "поставщик штампуется");
        Assert.IsTrue(stamped.Lines.Count == 1, "строки с заказа, факт {0}", stamped.Lines.Count);

        await RunCommandAsync("PostPurchaseReturn", ret.MetaId);
        var posted = await DocumentManager.GetDocumentAsync<PurchaseReturn>(ret.MetaId);
        Assert.IsTrue(posted!.Subtype == PurchaseReturn.Subtypes.Posted, "возврат Posted, факт {0}", posted.Subtype ?? "<null>");

        Assert.IsTrue(await StockAsync(s) == 0m, "склад пуст, факт {0}", await StockAsync(s));
        Assert.IsTrue(await PayableAsync(s) == 0m, "кредиторка закрыта, факт {0}", await PayableAsync(s));
    }

    [IntegrationTest("Возврат больше остатка отклоняется")]
    public async Task ReturnOverStockIsRejected()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 2m, 3m);

        var ret = await DocumentManager.NewDocumentAsync<PurchaseReturn>();
        ret.Supplier = s.Supplier;
        ret.Location = s.Location;
        ret.Lines.Add(new PurchaseReturnLinesTablePartRow { Item = s.Item, Quantity = 5m, UnitPrice = 3m });
        await DocumentManager.SaveDocumentAsync(ret);

        var commandId = await Db.FindCommandIdAsync("document", "PostPurchaseReturn");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, ret.MetaId);
        var after = await DocumentManager.GetDocumentAsync<PurchaseReturn>(ret.MetaId);
        Assert.IsTrue(after!.Subtype == PurchaseReturn.Subtypes.Draft,
            "без остатка остаётся Draft, факт {0}", after.Subtype ?? "<null>");
        Assert.IsTrue(!run.Success || string.Join("; ", run.ClientMessages).Contains("остатка"),
            "отказ про остаток: {0}", string.Join("; ", run.ClientMessages));
        Assert.IsTrue(await StockAsync(s) == 2m, "склад не списан, факт {0}", await StockAsync(s));
    }

    private Task<decimal> StockAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item });

    private Task<decimal> PayableAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Payable", "Amount",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier });

    private async Task RunCommandAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        Assert.IsTrue(run.Success, "команда {0}: {1}", name, run.Message ?? string.Join("; ", run.ClientMessages));
    }
}
