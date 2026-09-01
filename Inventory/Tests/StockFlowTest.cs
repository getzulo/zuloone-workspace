using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Currency, StockAdjustment, StockAdjustmentLinesTablePartRow…).
// Тест-скрипты НЕ получают это пространство имён глобальным using — без него
// генерённые классы не находятся, а Currency вдобавок связывается с посторонним
// недоступным типом, и ошибка (CS0122) описывает не ту причину.
using ZuloOne.Runtime.Generated;

// Integration coverage for the single-entry Stock ledger (MIQS-style): an
// adjustment brings stock in as one positive movement (no External counterparty),
// a transfer moves it between two cells as a balanced pair, and a write-off beyond
// on-hand is rejected by the StockAdjustment precheck. On-hand is read per
// (Cell,Item); the register sums to the real on-hand quantity.
//
// Написано так, как пишется сервис MIQS — типизированными сущностями через
// менеджеры: справочник это NewRecord<T> → поля → SaveRecordAsync, документ —
// NewDocumentAsync<T> → Lines → SaveDocumentAsync, а проведение — присваивание:
//
//     doc.Subtype = StockAdjustment.Subtypes.Posted;
//     await DocumentManager.SaveDocumentAsync(doc);
//
// Остатки и движения читаются ITotalsManager'ом — тем же, что зовут обработчики
// событий Inventory.
public class StockFlowTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private async Task<(Guid Loc1, Guid Loc2, Guid Item)> SetupAsync()
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
        legalEntity.RegistrationNumber = "REG-INV-1";
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

        var loc1 = await NewCellAsync("A-01", cellType.MetaId, zone.MetaId, cellNumber: 1);
        var loc2 = await NewCellAsync("A-02", cellType.MetaId, zone.MetaId, cellNumber: 2);

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

        return (loc1.MetaId, loc2.MetaId, item.MetaId);
    }

    private static async Task<StoreCell> NewCellAsync(string name, Guid type, Guid zone, int cellNumber)
    {
        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = name;
        cell.Type = type;
        cell.StoreZone = zone;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = cellNumber;
        return await DictionaryManager.SaveRecordAsync(cell);
    }

    /// <summary>Черновик корректировки на одну строку — ещё НЕ проведён.</summary>
    private static async Task<StockAdjustment> NewAdjustmentAsync(Guid location, Guid item, decimal qty)
    {
        // Подтип не передаём: NewDocumentAsync обязан взять НАЧАЛЬНЫЙ подтип типа
        // документа (Draft) сам.
        var doc = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        doc.Cell = location;
        doc.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = item, Quantity = qty });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }

    private async Task PostAdjustmentAsync(Guid location, Guid item, decimal qty)
    {
        var doc = await NewAdjustmentAsync(location, item, qty);
        doc.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);
    }

    /// <summary>Остаток по паре (ячейка, товар): у Stock ровно эти два физических
    /// измерения, так что срез задаётся полным ключом.</summary>
    private static Task<decimal> OnHandAsync(Guid location, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = location, ["Item"] = item });

    [IntegrationTest("Корректировка вводит остаток на склад")]
    public async Task AdjustmentAddsStock()
    {
        var s = await SetupAsync();

        // Состояние ДО перехода: черновик остатка не создаёт. Без этого проверка
        // ниже проходит и тогда, когда документ разнёсся сам при сохранении, — и
        // о переходе Draft → Posted тест не говорит ничего.
        var draft = await NewAdjustmentAsync(s.Loc1, s.Item, 10m);
        Assert.IsTrue(await OnHandAsync(s.Loc1, s.Item) == 0m, "черновик корректировки не должен двигать остаток");

        draft.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(draft);

        Assert.IsTrue(await OnHandAsync(s.Loc1, s.Item) == 10m, "на ячейке должно быть 10");
    }

    [IntegrationTest("Перемещение делит остаток между ячейками")]
    public async Task TransferSplitsStock()
    {
        var s = await SetupAsync();
        await PostAdjustmentAsync(s.Loc1, s.Item, 10m);

        var doc = await DocumentManager.NewDocumentAsync<StockTransfer>();
        doc.FromCell = s.Loc1;
        doc.ToCell = s.Loc2;
        doc.Lines.Add(new StockTransferLinesTablePartRow { Item = s.Item, Quantity = 4m });
        await DocumentManager.SaveDocumentAsync(doc);

        // Черновик перемещения ничего не переносит — остаток пока весь на Loc1.
        Assert.IsTrue(await OnHandAsync(s.Loc1, s.Item) == 10m, "до проведения весь остаток на исходной ячейке");
        Assert.IsTrue(await OnHandAsync(s.Loc2, s.Item) == 0m, "до проведения целевая ячейка пуста");

        doc.Subtype = StockTransfer.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);

        Assert.IsTrue(await OnHandAsync(s.Loc1, s.Item) == 6m, "на исходной ячейке осталось 6");
        Assert.IsTrue(await OnHandAsync(s.Loc2, s.Item) == 4m, "на целевой ячейке 4");

        // Три движения: приход +10 на Loc1 (одиночная проводка) и перемещение
        // (Loc1 −4 / Loc2 +4). Считаем ТОЛЬКО движения по своему товару: регистр
        // общий, и незакрытые строки соседних прогонов попадут в безусловный
        // QueryMovementsAsync.
        var moves = await TotalsManager.QueryMovementsAsync("Stock", $"[Item] = '{s.Item}'");
        decimal sum = 0m;
        foreach (var m in moves) sum += Convert.ToDecimal(m["Qty"]);
        Assert.IsTrue(moves.Count == 3, "ожидалось 3 движения (приход + пара перемещения), а не {0}", moves.Count);
        Assert.IsTrue(sum == 10m, "одинарная запись: сумма движений равна остатку (10), а не {0}", sum);
    }

    [IntegrationTest("Балансовая строка разрезана ЯЧЕЙКОЙ, а не только товаром")]
    public async Task BalanceRowsArePartitionedByCell()
    {
        var s = await SetupAsync();

        // Один и тот же товар в двух ячейках разными количествами. Числа выбраны
        // так, чтобы схлопывание было ВИДНО: 7 и 3 схлопнутся в 10, и ни одна
        // проверка «остаток положительный» этого не заметит.
        await PostAdjustmentAsync(s.Loc1, s.Item, 7m);
        await PostAdjustmentAsync(s.Loc2, s.Item, 3m);

        // Точечный срез по полному ключу и так работал — здесь проверяется ДРУГОЕ:
        // что адресность есть в самой балансовой таблице, то есть её можно читать
        // списком и фильтровать SQL-ом. Именно от этого зависит адресный склад:
        // «что лежит в этой ячейке» и «в каких ячейках лежит этот товар» — запросы
        // к балансу, а не перебор движений.
        var rows = await TotalsManager.QueryBalancesAsync("Stock", $"[Item] = '{s.Item}'");
        Assert.IsTrue(rows.Count == 2,
            "две ячейки — две балансовые строки, факт {0} (одна строка означала бы схлопывание по товару)", rows.Count);

        var byCell = new Dictionary<Guid, decimal>();
        foreach (var row in rows)
            byCell[Guid.Parse(row["Cell"]!.ToString()!)] = Convert.ToDecimal(row["Qty"]);

        Assert.IsTrue(byCell.TryGetValue(s.Loc1, out var first) && first == 7m,
            "в первой ячейке 7, факт {0}", byCell.TryGetValue(s.Loc1, out var f) ? f : -1m);
        Assert.IsTrue(byCell.TryGetValue(s.Loc2, out var second) && second == 3m,
            "во второй ячейке 3, факт {0}", byCell.TryGetValue(s.Loc2, out var sec) ? sec : -1m);

        // И обратный разрез: «что лежит в этой ячейке» — фильтр по ячейке отдаёт
        // только её строку.
        var inCell = await TotalsManager.QueryBalancesAsync("Stock", $"[Cell] = '{s.Loc1}' AND [Item] = '{s.Item}'");
        Assert.IsTrue(inCell.Count == 1, "фильтр по ячейке отдаёт одну строку, факт {0}", inCell.Count);
        Assert.IsTrue(Convert.ToDecimal(inCell[0]["Qty"]) == 7m,
            "и это остаток именно первой ячейки, факт {0}", Convert.ToDecimal(inCell[0]["Qty"]));
    }

    [IntegrationTest("Списание сверх наличия отклоняется")]
    public async Task OverWithdrawIsRejected()
    {
        var s = await SetupAsync();
        await PostAdjustmentAsync(s.Loc1, s.Item, 5m);

        // Списание сверх наличия обязано СОХРАНИТЬСЯ черновиком — черновику
        // позволено быть неверным. Запрет принадлежит ПРОВЕДЕНИЮ.
        var wo = await NewAdjustmentAsync(s.Loc1, s.Item, -8m);
        Assert.IsTrue(await OnHandAsync(s.Loc1, s.Item) == 5m, "черновик списания не должен трогать остаток");

        // После пойманного отказа к БД НЕ обращаемся: событие отказывает
        // исключением, а исключение рушит окружающую транзакцию раннера —
        // следующий запрос упал бы вместо самой проверки.
        var rejected = false;
        try
        {
            wo.Subtype = StockAdjustment.Subtypes.Posted;
            await DocumentManager.SaveDocumentAsync(wo);
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "списание 8 при остатке 5 должно быть отклонено событием");
    }
}
