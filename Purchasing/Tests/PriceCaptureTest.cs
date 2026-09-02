using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Сам алгоритм захвата цены уже полностью проверен в
// Inventory/Tests/PriceCaptureTest.cs; здесь доказывается только то, что
// PurchaseOrderEventHandler.OnAfterPostAsync реально его вызывает при
// оприходовании и не дублирует строку при перепроведении.
public class PriceCaptureTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Unit;
        public Guid Supplier;
        public Guid PriceList;
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
        legalEntity.RegistrationNumber = "REG-PRC-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "WH";
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
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"RCV-{Db.NewId():N}"[..12];
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
        uom.Code = $"PCS{Db.NewId():N}"[..8];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"RAW{Db.NewId():N}"[..8];
        group.Name = "Raw material";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bolt";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item = await DictionaryManager.SaveRecordAsync(item);

        var priceList = DictionaryManager.NewRecord<PriceList>();
        priceList.Name = $"Purchase {Db.NewId():N}"[..16];
        priceList.Direction = PriceDirection.Purchase;
        priceList = await DictionaryManager.SaveRecordAsync(priceList);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier.PriceList = priceList.MetaId;
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            Unit = uom.MetaId,
            Supplier = supplier.MetaId,
            PriceList = priceList.MetaId,
        };
    }

    // Подтип не передаём: NewDocumentAsync обязан подставить начальный подтип
    // (Draft) сам.
    private async Task<PurchaseOrder> NewOrderAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Unit = s.Unit, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    /// <summary>Заказ идёт объявленным маршрутом: Draft → Ordered → Received.</summary>
    private async Task ReceiveAsync(PurchaseOrder order)
    {
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
    }

    private Task<List<PriceListItem>> RowsAsync(Setup s)
        => DictionaryManager.GetRecordsAsync<PriceListItem>(
            $"PriceList = '{s.PriceList}' AND Item = '{s.Item}' AND Unit = '{s.Unit}'");

    [IntegrationTest("Оприходование с закупочным типом цен Base пишет цену строки в PriceListItem")]
    public async Task ReceivingCapturesLinePrice()
    {
        var s = await SetupAsync();
        var order = await NewOrderAsync(s, qty: 10m, price: 12.5m);

        await ReceiveAsync(order);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 1, "оприходование обязано создать ровно одну строку истории цены, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].Price == 12.5m, "цена в истории обязана быть 12.5, факт {0}", rows[0].Price);
        Assert.IsTrue(rows[0].EffectiveTo == null, "единственная строка обязана остаться открытой, факт {0}", rows[0].EffectiveTo);
    }

    [IntegrationTest("Перепроведение (Received → Ordered → Received) не плодит вторую строку истории")]
    public async Task RepostingDoesNotDuplicateRow()
    {
        var s = await SetupAsync();
        var order = await NewOrderAsync(s, qty: 10m, price: 12.5m);
        await ReceiveAsync(order);

        // Перепроведение — КРУГОВОЙ переход подтипа (выйти и вернуться в
        // Received), а не повторное сохранение уже проведённого документа: именно
        // так реально перепроводят приход (см. прецедент
        // WarehouseTaskFlowTest.RepostDoesNotDuplicatePutAwayTask) — только так
        // повторно срабатывает OnAfterPostAsync.
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 1, "перепроведение той же цены не должно плодить вторую строку, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].Price == 12.5m, "цена обязана остаться 12.5, факт {0}", rows[0].Price);
    }
}
