using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// «Принять сейчас» меньше количества строки. Склад и кредиторка — только на
// принятое. Остаток — новый заказ в Ordered, связанный с приходом.
public class PurchasePartialReceiptTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dictionaries => GetService<IDictionaryManager>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();
    private static ITotalsManager Totals => GetService<ITotalsManager>();
    private static IPurchaseReceiptService Receipts => GetService<IPurchaseReceiptService>();

    [IntegrationTest("Принять 4 из 10: склад и кредиторка на 4, остаток 6 заказан")]
    public async Task PartialReceiveLeavesTheRestOrdered()
    {
        var s = await SetupAsync();
        var order = await OrderAsync(s, 10m, 3m, 4m);

        var error = await Receipts.ReceiveAsync(order.MetaId);
        Assert.IsTrue(error == null, "приход 4 из 10; факт {0}", error);

        Assert.IsTrue(await StockAsync(s) == 4m, "на ячейке 4, не 10; факт {0}", await StockAsync(s));
        Assert.IsTrue(await PayableAsync(s) == 12m, "кредиторка 4 × 3 = 12; факт {0}", await PayableAsync(s));

        var received = await Documents.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsTrue(received != null && received.Subtype == PurchaseOrder.Subtypes.Received, "принятая часть в Принят");
        Assert.IsTrue(received!.Lines.Count == 1 && received.Lines[0].Quantity == 4m, "в приходе количество 4");

        var rest = await OrderedAsync(s.Supplier);
        Assert.IsTrue(rest.Lines.Count == 1 && rest.Lines[0].Quantity == 6m, "остаток 6 ещё заказан; факт {0}", rest.Lines.Count == 0 ? 0 : rest.Lines[0].Quantity);
        Assert.IsTrue(rest.Lines[0].UnitPrice == 3m, "цена остатка та же");
        Assert.IsTrue(await StockAsync(s) == 4m, "остаточный заказ склад не двигает");

        var second = await Receipts.ReceiveAsync(rest.MetaId);
        Assert.IsTrue(second == null, "остаток принимается целиком; факт {0}", second);
        Assert.IsTrue(await StockAsync(s) == 10m, "после второго прихода на ячейке 10; факт {0}", await StockAsync(s));
        Assert.IsTrue(await PayableAsync(s) == 30m, "кредиторка 30; факт {0}", await PayableAsync(s));
    }

    [IntegrationTest("Принять сейчас больше строки — отказ, склад на нуле")]
    public async Task ReceiveQtyAboveTheLineIsRefused()
    {
        var s = await SetupAsync();
        var order = await OrderAsync(s, 10m, 3m, 12m);
        var error = await Receipts.ReceiveAsync(order.MetaId);
        Assert.IsTrue(error != null && error.Contains("больше"), "отказ; факт {0}", error);
        Assert.IsTrue(await StockAsync(s) == 0m, "отказ склад не двигает");
    }

    [IntegrationTest("Ноль на одной строке, когда на другой есть количество, строку не принимает")]
    public async Task ZeroReceiveQtySkipsThatLine()
    {
        var s = await SetupAsync();
        var nut = await ItemAsync(s.Unit, "Nut");
        var order = await Documents.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 5m, UnitPrice = 3m, ReceiveQty = 2m });
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = nut, Quantity = 8m, UnitPrice = 1m, ReceiveQty = 0m });
        await Documents.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await Documents.SaveDocumentAsync(order);

        var error = await Receipts.ReceiveAsync(order.MetaId);
        Assert.IsTrue(error == null, "частичный приход двух строк; факт {0}", error);

        var received = (await Documents.GetDocumentAsync<PurchaseOrder>(order.MetaId))!;
        Assert.IsTrue(received.Lines.Count == 1 && received.Lines[0].Item == s.Item && received.Lines[0].Quantity == 2m,
            "принят только болт, 2 штуки");
        Assert.IsTrue(await StockAsync(s) == 2m, "гайка на склад не пришла; факт {0}", await StockAsync(s));

        var rest = await OrderedAsync(s.Supplier);
        Assert.IsTrue(rest.Lines.Count == 2, "в остатке две строки; факт {0}", rest.Lines.Count);
        var bolt = rest.Lines.First(l => l.Item == s.Item);
        var skipped = rest.Lines.First(l => l.Item == nut);
        Assert.IsTrue(bolt.Quantity == 3m, "от болта осталось 3; факт {0}", bolt.Quantity);
        Assert.IsTrue(skipped.Quantity == 8m, "гайка целиком в остатке; факт {0}", skipped.Quantity);
    }

    private async Task<PurchaseOrder> OrderAsync(Setup s, decimal qty, decimal price, decimal receiveNow)
    {
        var order = await Documents.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow
        {
            Item = s.Item, Quantity = qty, UnitPrice = price, ReceiveQty = receiveNow
        });
        await Documents.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await Documents.SaveDocumentAsync(order);
        return order;
    }

    private async Task<PurchaseOrder> OrderedAsync(Guid supplier)
    {
        var rows = await Documents.QueryDocumentsAsync<PurchaseOrder>($"Supplier = '{supplier}' AND Subtype = 'Ordered'");
        Assert.IsTrue(rows.Count == 1, "один остаточный заказ; факт {0}", rows.Count);
        return (await Documents.GetDocumentAsync<PurchaseOrder>(rows[0].MetaId))!;
    }

    private Task<decimal> StockAsync(Setup s)
        => Totals.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item });

    private Task<decimal> PayableAsync(Setup s)
        => Totals.GetBalanceAsync("Payable", "Amount",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier });

    private async Task<Guid> ItemAsync(Guid unit, string name)
    {
        var group = Dictionaries.NewRecord<ItemGroup>();
        group.Code = $"G{Db.NewId():N}"[..8];
        group.Name = name;
        group = await Dictionaries.SaveRecordAsync(group);
        var item = Dictionaries.NewRecord<Item>();
        item.Name = name;
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit;
        item.IsRawMaterial = true;
        return (await Dictionaries.SaveRecordAsync(item)).MetaId;
    }

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Unit;
        public Guid Supplier;
    }

    private async Task<Setup> SetupAsync()
    {
        var currency = Dictionaries.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = $"E{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "€";
        currency = await Dictionaries.SaveRecordAsync(currency);

        var country = Dictionaries.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await Dictionaries.SaveRecordAsync(country);

        var legal = Dictionaries.NewRecord<LegalEntity>();
        legal.Name = "ACME GmbH";
        legal.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        legal.Country = country.MetaId;
        legal.Currency = currency.MetaId;
        legal = await Dictionaries.SaveRecordAsync(legal);

        var divisionType = Dictionaries.NewRecord<DivisionType>();
        divisionType.Code = $"W{Db.NewId():N}"[..4];
        divisionType.Name = "Warehouse";
        divisionType = await Dictionaries.SaveRecordAsync(divisionType);

        var division = Dictionaries.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legal.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await Dictionaries.SaveRecordAsync(division);

        var store = Dictionaries.NewRecord<Store>();
        store.Name = "Central";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await Dictionaries.SaveRecordAsync(store);

        var zone = Dictionaries.NewRecord<StoreZone>();
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await Dictionaries.SaveRecordAsync(zone);

        var cellType = Dictionaries.NewRecord<StoreCellType>();
        cellType.Code = $"R{Db.NewId():N}"[..8];
        cellType.Name = "Receiving";
        cellType = await Dictionaries.SaveRecordAsync(cellType);

        var cell = Dictionaries.NewRecord<StoreCell>();
        cell.Name = "R-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await Dictionaries.SaveRecordAsync(cell);

        var uom = Dictionaries.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"P{Db.NewId():N}"[..6];
        uom = await Dictionaries.SaveRecordAsync(uom);

        var group = Dictionaries.NewRecord<ItemGroup>();
        group.Code = $"G{Db.NewId():N}"[..8];
        group.Name = "Raw";
        group = await Dictionaries.SaveRecordAsync(group);

        var item = Dictionaries.NewRecord<Item>();
        item.Name = "Bolt";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item.IsRawMaterial = true;
        item = await Dictionaries.SaveRecordAsync(item);

        var supplier = Dictionaries.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply";
        supplier = await Dictionaries.SaveRecordAsync(supplier);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Unit = uom.MetaId, Supplier = supplier.MetaId };
    }
}
