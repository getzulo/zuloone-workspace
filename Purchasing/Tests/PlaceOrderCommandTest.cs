using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы (PurchaseOrder, PurchaseOrderLinesTablePartRow, Currency…).
// Тест-скрипты НЕ получают это пространство имён глобальным using'ом.
using ZuloOne.Runtime.Generated;

// Покрытие команды «Заказать» (Draft → Ordered) и того, что новое промежуточное
// состояние не сломало приход: из Ordered документ по-прежнему переводится в
// Received и проводит движения склада.
//
// Сценарий собран менеджерами и типизированными сущностями — той же дверью, что
// и продакшен-код: справочники через IDictionaryManager, документ через
// IDocumentManager (подтип — присваивание плюс сохранение), остатки и движения
// через ITotalsManager.
public class PlaceOrderCommandTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private async Task<(Guid Location, Guid Item, Guid Supplier)> SetupAsync()
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
        legalEntity.RegistrationNumber = "REG-ORD-1";
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
        // Db.NewId() остаётся: код типа ячейки обязан быть уникальным в прогоне.
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

        return (cell.MetaId, item.MetaId, supplier.MetaId);
    }

    private async Task<decimal> CellStockAsync(Guid location)
    {
        decimal total = 0m;
        foreach (var row in await TotalsManager.QueryBalancesAsync("Stock", $"[Cell] = '{location}'"))
            total += Convert.ToDecimal(row["Qty"]);
        return total;
    }

    [IntegrationTest("Команда «Заказать» переводит заполненный заказ в Ordered")]
    public async Task PlacesFilledOrder()
    {
        var s = await SetupAsync();

        // Подтип не передаётся намеренно: документ обязан стартовать в НАЧАЛЬНОМ
        // подтипе своего типа (Draft), а не в переданном руками.
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 4m, UnitPrice = 3m });
        await DocumentManager.SaveDocumentAsync(order);

        Assert.IsTrue(order.Subtype == PurchaseOrder.Subtypes.Draft,
            "новый заказ стартует в начальном подтипе Draft, факт {0}", order.Subtype ?? "<null>");

        // Исполнение команд — единственный шаг сценария БЕЗ менеджера: платформа
        // не публикует ICommandManager, запускать команду умеет только харнесс.
        var commandId = await Db.FindCommandIdAsync("document", "PlaceOrder");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var placed = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsNotNull(placed, "заказ читается менеджером после команды");
        Assert.IsTrue(placed!.Subtype == PurchaseOrder.Subtypes.Ordered,
            "подтип стал Ordered, факт {0}", placed.Subtype ?? "<null>");

        // «Заказано» — это обязательство, а не приход: движений склада быть НЕ должно.
        // Проверка не формальная: две складские проводки закупки привязаны в
        // метаданных к ДОКУМЕНТУ, а не к подтипу Received, поэтому важно убедиться,
        // что появление промежуточного состояния не начало приходовать товар раньше
        // времени. Это же и снимок ДО перехода: без него проверка после перехода
        // проходит даже когда переход ничего не сделал.
        var atOrdered = await CellStockAsync(s.Location);
        Assert.IsTrue(atOrdered == 0m, "на «Заказано» склад не двигается, факт {0}", atOrdered);

        // Приход из UI — команда ReceiveOrder на Ordered, не ручной Subtype.
        var receiveId = await Db.FindCommandIdAsync("document", "ReceiveOrder");
        var received = await Db.ExecuteDocumentCommandAsync(receiveId, order.MetaId);
        Assert.IsTrue(received.Success, "приход должен выполниться: {0}", received.Message ?? "");

        var onHand = await CellStockAsync(s.Location);
        Assert.IsTrue(onHand == 4m, "после прихода на ячейке 4, факт {0}", onHand);

        // Провенанс: движение помнит, какой ДОКУМЕНТ его породил.
        var moves = await TotalsManager.QueryMovementsAsync("Stock", $"[Cell] = '{s.Location}'");
        Assert.IsTrue(moves.Count > 0, "движения прихода записаны");
        Assert.IsTrue(moves.All(m => Convert.ToString(m["DocumentMetaId"]) == order.MetaId.ToString()),
            "каждое движение ссылается на заказ-источник");
    }

    [IntegrationTest("Команда «Заказать» отклоняет пустой заказ")]
    public async Task RejectsEmptyOrder()
    {
        var s = await SetupAsync();

        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        await DocumentManager.SaveDocumentAsync(order);

        var commandId = await Db.FindCommandIdAsync("document", "PlaceOrder");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);

        var after = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsNotNull(after, "заказ читается менеджером после отказа команды");
        Assert.IsTrue(after!.Subtype == PurchaseOrder.Subtypes.Draft,
            "пустой заказ остаётся черновиком, факт {0}", after.Subtype ?? "<null>");
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("пуст"),
            "пользователь получил причину отказа: {0}", string.Join("; ", run.ClientMessages));
    }
}
