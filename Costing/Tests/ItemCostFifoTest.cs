using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// The generated entity classes (Currency, PurchaseOrder, …TablePartRow). A test
// script does NOT get this namespace as a global using, so it must be named.
using ZuloOne.Runtime.Generated;

// Покрытие Costing FIFO: приход создаёт слои себестоимости, расход списывает по
// старейшим лотам (FIFO), движок отклоняет перерасход слоёв.
//
// Мастер-данные и заказ заводятся типизированными сущностями через менеджеры,
// регистр читается и двигается через ITotalsManager.
public class ItemCostFifoTest : IntegrationTestScriptBase
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
        legalEntity.RegistrationNumber = "REG-FIFO-1";
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

        return (cell.MetaId, item.MetaId, supplier.MetaId);
    }

    private async Task ReceiveAsync((Guid Location, Guid Item, Guid Supplier) s, decimal qty, decimal price)
    {
        var before = await FifoBalanceAsync(s.Item);

        // Подтип не передаём: заказ заводится в НАЧАЛЬНОМ подтипе своего типа.
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);

        // Черновик слоёв не создаёт. Проверяем ДО перевода: без этого утверждения
        // о приходе проходят и тогда, когда документ разнёсся сам при сохранении.
        var afterDraft = await FifoBalanceAsync(s.Item);
        Assert.IsTrue(afterDraft.Qty == before.Qty && afterDraft.Amount == before.Amount,
            "черновик заказа не должен трогать слои FIFO: было {0}/{1}, стало {2}/{3}",
            before.Qty, before.Amount, afterDraft.Qty, afterDraft.Amount);

        // Тип объявляет Draft → Ordered → Received, и таблица переходов
        // принудительна: промежуточный подтип пропускать нельзя.
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
    }

    private async Task<(decimal Qty, decimal Amount)> FifoBalanceAsync(Guid item)
    {
        var rows = await TotalsManager.QueryBalancesAsync("ItemCostFifo", "[Item] = '" + item + "'");
        decimal q = 0m, a = 0m;
        foreach (var r in rows) { q += Convert.ToDecimal(r["Quantity"]); a += Convert.ToDecimal(r["Amount"]); }
        return (q, a);
    }

    [IntegrationTest("FIFO: расход списывает старейшие лоты, остаток по FIFO-цене")]
    public async Task IssueConsumesOldestLots()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);   // лот 1: 10 шт по 7 = 70
        await ReceiveAsync(s, 10m, 9m);   // лот 2: 10 шт по 9 = 90

        var afterReceipts = await FifoBalanceAsync(s.Item);
        Assert.IsTrue(afterReceipts.Qty == 20m, "после прихода 20 шт, факт {0}", afterReceipts.Qty);
        Assert.IsTrue(afterReceipts.Amount == 160m, "после прихода стоимость 160, факт {0}", afterReceipts.Amount);

        // Расход 15 шт: FIFO снимает 10×7 + 5×9 = 115. Остаток 5 шт по 9 = 45.
        // Движение вне цепочки документа — документа-хозяина у него нет.
        await TotalsManager.PostMovementAsync("ItemCostFifo", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Quantity"] = -15m, ["Amount"] = 0m });

        var afterIssue = await FifoBalanceAsync(s.Item);
        Assert.IsTrue(afterIssue.Qty == 5m, "остаток 5 шт, факт {0}", afterIssue.Qty);
        Assert.IsTrue(afterIssue.Amount == 45m, "остаток по FIFO 45 (5×9), факт {0} (среднее дало бы 40)", afterIssue.Amount);
    }

    [IntegrationTest("FIFO: перерасход слоёв отклоняется движком")]
    public async Task OverdrawRejected()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 5m, 7m);   // всего 5 шт

        // Отказ приходит ИСКЛЮЧЕНИЕМ, а исключение обрекает окружающую
        // транзакцию рантайма: после catch к базе не обращаемся, иначе
        // следующий запрос упадёт «the operation is not valid for the state of
        // the transaction» и замаскирует настоящую проверку.
        var rejected = false;
        try
        {
            await TotalsManager.PostMovementAsync("ItemCostFifo", null, DateTime.UtcNow.Date,
                new Dictionary<string, object?> { ["Item"] = s.Item },
                new Dictionary<string, decimal> { ["Quantity"] = -6m, ["Amount"] = 0m });
        }
        catch (Exception)
        {
            rejected = true;
        }
        Assert.IsTrue(rejected, "расход 6 шт при 5 в наличии должен быть отклонён FIFO-движком");
    }
}
