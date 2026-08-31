using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Обязательно: тестовым скриптам этот namespace НЕ выдаётся глобальным using —
// без него генерированные классы (Currency, Item, PurchaseOrder…) не находятся.
using ZuloOne.Runtime.Generated;

// Покрытие Costing: оприходование заказа поставщику наполняет регистр стоимости
// запасов (Value = Σ количество × цена, Qty = Σ количество); средняя
// себестоимость товара = Value / Qty.
//
// Написано так, как пишется бизнес-код: типизированные записи через
// IDictionaryManager, документ через IDocumentManager, регистр через
// ITotalsManager. Проведение — это ПРИСВОЕНИЕ подтипа плюс сохранение.
public class InventoryValueFlowTest : IntegrationTestScriptBase
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
        legalEntity.RegistrationNumber = "REG-COST-1";
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
        cellType.Code = $"STG-{Db.NewId():N}"[..12];
        cellType.Name = "Storage";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "A-01";
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
        group.Code = $"MERCH-{Db.NewId():N}"[..12];
        group.Name = "Merchandise";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item = await DictionaryManager.SaveRecordAsync(item);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Supplier = supplier.MetaId };
    }

    // InventoryValue несёт одну динамическую аналитику (Item) — баланс
    // схлопывается в одну строку; суммируем оба ресурса.
    private static async Task<(decimal Value, decimal Qty)> InventoryValueAsync()
    {
        decimal value = 0m, qty = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("InventoryValue"))
        {
            value += Convert.ToDecimal(r["Value"]);
            qty += Convert.ToDecimal(r["Qty"]);
        }
        return (value, qty);
    }

    [IntegrationTest("Оприходование заказа наполняет стоимость запасов; средняя = Value/Qty")]
    public async Task ReceiptFillsInventoryValue()
    {
        var s = await SetupAsync();

        // Подтип не передаём: документ обязан стартовать в НАЧАЛЬНОМ подтипе типа (Draft).
        var po = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        po.Supplier = s.Supplier;
        po.Location = s.Location;
        po.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 10m, UnitPrice = 7m });
        await DocumentManager.SaveDocumentAsync(po);

        // Черновик стоимости не создаёт. Проверяем ДО перехода — тип помечен
        // postOnSave, и без этой проверки тест зеленел бы независимо от того,
        // сделал ли переход хоть что-нибудь.
        var draft = await InventoryValueAsync();
        Assert.IsTrue(draft.Value == 0m && draft.Qty == 0m,
            "черновик не наполняет InventoryValue, факт {0}/{1}", draft.Value, draft.Qty);

        // Тип объявляет маршрут Draft → Ordered → Received: прыжок сразу в приход
        // отклоняется картой переходов, поэтому проходим промежуточный подтип.
        po.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(po);

        var ordered = await InventoryValueAsync();
        Assert.IsTrue(ordered.Value == 0m && ordered.Qty == 0m,
            "заказ ещё не приход — стоимость не движется, факт {0}/{1}", ordered.Value, ordered.Qty);

        po.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(po);

        var (value, qty) = await InventoryValueAsync();
        Assert.IsTrue(value == 70m, "стоимость 10 × 7 = 70, факт {0}", value);
        Assert.IsTrue(qty == 10m, "количество 10, факт {0}", qty);
        Assert.IsTrue(qty > 0m && value / qty == 7m, "средняя себестоимость 7, факт {0}", qty > 0m ? value / qty : -1m);
    }
}
