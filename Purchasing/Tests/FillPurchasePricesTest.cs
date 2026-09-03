using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Testing;
// Обязательно: тестовым скриптам этот namespace НЕ выдаётся глобальным using —
// без него генерированные классы (PurchaseOrder, PriceType…) не находятся.
using ZuloOne.Runtime.Generated;

// Команда «Заполнить цены» на черновике заказа поставщику — зеркало
// FillSalesPricesTest (Sales) на закупочной стороне.
//
// Тест сквозной намеренно: лестницу подбора и цепочку Base/Calculated уже
// покрывает PriceResolutionTest в Inventory, а здесь проверяется то, что видно
// только на документе — что цена доезжает до СТРОКИ (с пересчётом ящик→штука),
// что введённая руками цена не затирается, что тупиковая Calculated-цепочка не
// роняет команду, и что она висит только на черновике.
public class FillPurchasePricesTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Piece;
        public Guid Box;
        public Guid Supplier;
    }

    private async Task<Setup> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = $"Euro-{Db.NewId():N}"[..12];
        currency.Code = $"E{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = $"Germany-{Db.NewId():N}"[..14];
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = $"ACME-{Db.NewId():N}"[..12];
        legalEntity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"WH{Db.NewId():N}"[..8];
        divisionType.Name = "Warehouse";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Central WH";
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

        var unitClass = DictionaryManager.NewRecord<UnitClass>();
        unitClass.Code = $"C{Db.NewId():N}"[..10];
        unitClass.Name = "Count";
        unitClass = await DictionaryManager.SaveRecordAsync(unitClass);

        var piece = DictionaryManager.NewRecord<UnitOfMeasure>();
        piece.Name = "Piece";
        piece.Code = $"P{Db.NewId():N}"[..8];
        piece.DecimalPlaces = 0;
        piece.UnitClass = unitClass.MetaId;
        piece.RatioToBase = 1m;
        piece = await DictionaryManager.SaveRecordAsync(piece);

        var box = DictionaryManager.NewRecord<UnitOfMeasure>();
        box.Name = "Box";
        box.Code = $"B{Db.NewId():N}"[..8];
        box.DecimalPlaces = 0;
        box.UnitClass = unitClass.MetaId;
        box = await DictionaryManager.SaveRecordAsync(box);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Db.NewId():N}"[..12];
        group.Name = "Raw material";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bolt";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = piece.MetaId;
        item = await DictionaryManager.SaveRecordAsync(item);

        var pack = DictionaryManager.NewRecord<ItemUnit>();
        pack.Item = item.MetaId;
        pack.Unit = box.MetaId;
        pack.QtyInBaseUnit = 12m;
        await DictionaryManager.SaveRecordAsync(pack);

        var list = DictionaryManager.NewRecord<PriceType>();
        list.Name = $"Purchase {Db.NewId():N}"[..16];
        list.Direction = PriceDirection.Purchase;
        list = await DictionaryManager.SaveRecordAsync(list);

        // Цена задана за ЯЩИК — строка заказа будет в штуках, и команда обязана
        // положить в строку цену за штуку, а не за ящик.
        var row = DictionaryManager.NewRecord<PriceListItem>();
        row.PriceType = list.MetaId;
        row.Item = item.MetaId;
        row.Unit = box.MetaId;
        row.Price = 120m;
        await DictionaryManager.SaveRecordAsync(row);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier.PriceType = list.MetaId;
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            Piece = piece.MetaId,
            Box = box.MetaId,
            Supplier = supplier.MetaId,
        };
    }

    [IntegrationTest("Команда заполняет пустые цены строк и не трогает введённые руками")]
    public async Task FillsEmptyPricesOnly()
    {
        var s = await SetupAsync();

        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        // Первая строка без цены — её и заполняем; вторая с ценой руками.
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 3m, Unit = s.Piece });
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 1m, Unit = s.Box, UnitPrice = 99m });
        await DocumentManager.SaveDocumentAsync(order);

        var commandId = await Db.FindCommandIdAsync("document", "FillPurchasePrices");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var saved = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        var byPiece = saved.Lines.First(l => l.Unit == s.Piece);
        var byBox = saved.Lines.First(l => l.Unit == s.Box);

        // 120 за ящик из 12 штук = 10 за штуку. Положить сюда 120 — занизить бы
        // строку в 12 раз: денежные ноги считаются от введённого количества.
        Assert.IsTrue(byPiece.UnitPrice == 10m,
            "пустой строке в штуках ожидалась цена 10, факт {0}", byPiece.UnitPrice);
        Assert.IsTrue(byBox.UnitPrice == 99m,
            "введённая руками цена 99 не должна затираться, факт {0}", byBox.UnitPrice);
    }

    [IntegrationTest("Строка без цены в прайсе остаётся пустой, команда сообщает об этом")]
    public async Task ReportsLinesWithoutPrice()
    {
        var s = await SetupAsync();

        // Товар, которого нет ни в прайсе, ни с умолчанием в карточке.
        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G2-{Db.NewId():N}"[..12];
        group.Name = "Unpriced";
        group = await DictionaryManager.SaveRecordAsync(group);

        var orphan = DictionaryManager.NewRecord<Item>();
        orphan.Name = "Unpriced bolt";
        orphan.ItemGroup = group.MetaId;
        orphan.UnitOfMeasure = s.Piece;
        orphan = await DictionaryManager.SaveRecordAsync(orphan);

        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 2m, Unit = s.Piece });
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = orphan.MetaId, Quantity = 1m, Unit = s.Piece });
        await DocumentManager.SaveDocumentAsync(order);

        var commandId = await Db.FindCommandIdAsync("document", "FillPurchasePrices");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var saved = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        var priced = saved.Lines.First(l => l.Item == s.Item);
        var unpriced = saved.Lines.First(l => l.Item == orphan.MetaId);

        // Ненайденная цена — не ошибка: остальные строки всё равно заполняются,
        // а человек видит, сколько осталось на нём.
        Assert.IsTrue(priced.UnitPrice == 10m, "строка с ценой заполнена, факт {0}", priced.UnitPrice);
        Assert.IsTrue(unpriced.UnitPrice == 0m, "строка без цены осталась пустой, факт {0}", unpriced.UnitPrice);
    }

    [IntegrationTest("Тупиковая цепочка Calculated (у базового типа нет строки на товар) оставляет цену пустой, а не роняет команду")]
    public async Task DeadEndCalculatedChainLeavesPriceEmpty()
    {
        var s = await SetupAsync();

        // Базовый тип БЕЗ единой строки на s.Item — тупик цепочки: разрешение
        // обязано вернуть null и остановиться, а не бросить исключение или
        // подставить что-то постороннее (умолчание карточки товара сюда не
        // входит — оно ступень ResolveAsync, а не ResolvePriceForTypeAsync).
        var deadBase = DictionaryManager.NewRecord<PriceType>();
        deadBase.Name = $"DeadBase {Db.NewId():N}"[..16];
        deadBase.Direction = PriceDirection.Purchase;
        deadBase = await DictionaryManager.SaveRecordAsync(deadBase);

        var dealer = DictionaryManager.NewRecord<PriceType>();
        dealer.Name = $"Dealer {Db.NewId():N}"[..16];
        dealer.Direction = PriceDirection.Purchase;
        dealer.Kind = PriceTypeKind.Calculated;
        dealer.BasePriceType = deadBase.MetaId;
        dealer.MarkupPercent = 10m;
        dealer = await DictionaryManager.SaveRecordAsync(dealer);

        var supplier2 = DictionaryManager.NewRecord<Supplier>();
        supplier2.Name = "Dead End Supply Co";
        supplier2.PriceType = dealer.MetaId;
        supplier2 = await DictionaryManager.SaveRecordAsync(supplier2);

        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = supplier2.MetaId;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 1m, Unit = s.Piece });
        await DocumentManager.SaveDocumentAsync(order);

        var commandId = await Db.FindCommandIdAsync("document", "FillPurchasePrices");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success,
            "команда обязана завершиться без ошибок даже когда цепочка не даёт ответа: {0}", run.Message ?? "");

        var saved = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsTrue(saved.Lines.First().UnitPrice == 0m,
            "тупиковая цепочка не должна подставить цену, факт {0}", saved.Lines.First().UnitPrice);
    }

    [IntegrationTest("Команда недоступна вне подтипа Draft")]
    public async Task BoundToDraftOnly()
    {
        var s = await SetupAsync();

        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 1m, Unit = s.Piece, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);

        // На Ordered заказ уже размещён у поставщика: переподбирать цены
        // командой, привязанной только к Draft, нельзя.
        var commandId = await Db.FindCommandIdAsync("document", "FillPurchasePrices");
        var rejected = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(!rejected.Success,
            "команда привязана только к Draft, на Ordered её быть не должно");
    }
}
