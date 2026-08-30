using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей (PurchaseOrder, Currency, StoreCell,
// PurchaseOrderLinesTablePartRow…). Тестовые скрипты НЕ получают это пространство
// имён глобальным using — без него `Currency` цепляется за посторонний недоступный
// тип, и ошибка компилятора описывает не ту причину.
using ZuloOne.Runtime.Generated;

// Integration coverage for the Purchasing core: receiving a purchase order adds stock
// and recognizes a payable; a zero-quantity order is rejected by the validation event.
//
// Написано так, как пишется прикладной сервис: типизированные сущности через
// менеджеры. Оприходование — это ПРИСВОЕНИЕ подтипа плюс сохранение, и идти оно
// обязано ОБЪЯВЛЕННЫМ маршрутом Draft → Ordered → Received: документ теперь
// стартует в начальном подтипе, поэтому таблица переходов реально применяется,
// и прыжок Draft → Received отклоняется движком.
public class PurchaseFlowTest : IntegrationTestScriptBase
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
        legalEntity.RegistrationNumber = "REG-PUR-1";
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
        cellType.Code = $"RCV-{Db.NewId():N}"[..12]; // Db.NewId() — законный остаток: генерация id.
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

    // Подтип не передаём намеренно: NewDocumentAsync обязан подставить НАЧАЛЬНЫЙ
    // подтип типа (Draft) — если он подставит NULL, цепочка проведения сузится не
    // туда и «черновик» проведётся сам.
    private async Task<PurchaseOrder> NewOrderAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
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

    [IntegrationTest("Приход добавляет в Stock и признаёт кредиторку")]
    public async Task ReceiptAddsStockAndPayable()
    {
        var s = await SetupAsync();
        var order = await NewOrderAsync(s, qty: 10m, price: 3m);

        // Состояние ДО оприходования: заказ (даже размещённый) ни склад, ни
        // кредиторку не двигает — движения принадлежат подтипу Received. Без этой
        // проверки утверждения ниже проходят и тогда, когда заказ провёлся сам.
        Assert.IsTrue(await StockAsync(s.Location, s.Item) == 0m, "черновик заказа не двигает склад");
        Assert.IsTrue(await PayableAsync() == 0m, "черновик заказа не признаёт кредиторку");

        await ReceiveAsync(order);

        var stock = await StockAsync(s.Location, s.Item);
        var payable = await PayableAsync();
        Assert.IsTrue(stock == 10m, "остаток ячейки должен стать 10, а не {0}", stock);
        Assert.IsTrue(payable == 30m, "кредиторка должна быть 30 (10 × 3), а не {0}", payable);
    }

    [IntegrationTest("Заказ с нулевым количеством отклоняется")]
    public async Task ZeroQuantityIsRejected()
    {
        var s = await SetupAsync();

        // Черновик с нулевым количеством обязан СОХРАНИТЬСЯ: черновику позволено
        // быть неправильным, проверка принадлежит ПРОВЕДЕНИЮ.
        var order = await NewOrderAsync(s, qty: 0m, price: 3m);
        Assert.IsTrue(await StockAsync(s.Location, s.Item) == 0m, "неверный черновик склад не двигает");

        // Обработчик отказывает ИСКЛЮЧЕНИЕМ, а бросок происходит внутри окружающей
        // транзакции прогона и обрекает её — поэтому после catch к базе больше не
        // обращаемся, а утверждаем сам факт отказа.
        var rejected = false;
        try
        {
            await ReceiveAsync(order);
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "заказ с нулевым количеством должен быть отклонён событием");
    }

    // Срез регистра адресуется измерениями, а не SQL-строкой.
    private Task<decimal> StockAsync(Guid location, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = location, ["Item"] = item });

    // У Payable физических измерений нет — разрез несут динамические аналитики,
    // поэтому итог собирается суммой по строкам баланса.
    private async Task<decimal> PayableAsync()
    {
        decimal payable = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("Payable")) payable += Convert.ToDecimal(r["Amount"]);
        return payable;
    }
}
