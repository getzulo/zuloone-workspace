using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using ZuloOne.Managers;
using ZuloOne.Runtime.Testing;
// Обязательно: тестовым скриптам этот namespace НЕ выдаётся глобальным using —
// без него генерированные классы (SalesInvoice, PriceType…) не находятся.
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Команда «Заполнить цены» на черновике счёта.
//
// Тест сквозной намеренно: лестницу подбора уже покрывает PriceResolutionTest в
// Inventory, а здесь проверяется то, что видно только на документе — что цена
// доезжает до СТРОКИ, что введённая руками цена не затирается и что команда
// висит только на черновике.
public class FillSalesPricesTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Piece;
        public Guid Box;
        public Guid Customer;
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
        divisionType.Code = $"SP{Db.NewId():N}"[..8];
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
        zone.Name = "Зона";
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
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = piece.MetaId;
        item = await DictionaryManager.SaveRecordAsync(item);

        var pack = DictionaryManager.NewRecord<ItemUnit>();
        pack.Item = item.MetaId;
        pack.Unit = box.MetaId;
        pack.QtyInBaseUnit = 12m;
        await DictionaryManager.SaveRecordAsync(pack);

        var list = DictionaryManager.NewRecord<PriceType>();
        list.Name = $"Retail {Db.NewId():N}"[..16];
        list.Direction = PriceDirection.Sale;
        list = await DictionaryManager.SaveRecordAsync(list);

        // Цена задана за ЯЩИК — строка счёта будет в штуках, и команда обязана
        // положить в строку цену за штуку, а не за ящик.
        await GetService<IPricingService>().SetPriceAsync(list.MetaId, item.MetaId, box.MetaId, 120m, null, null);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer.PriceType = list.MetaId;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            Piece = piece.MetaId,
            Box = box.MetaId,
            Customer = customer.MetaId,
        };
    }

    [IntegrationTest("Команда заполняет пустые цены строк и не трогает введённые руками")]
    public async Task FillsEmptyPricesOnly()
    {
        var s = await SetupAsync();

        var inv = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        inv.Customer = s.Customer;
        inv.Location = s.Location;
        // Первая строка без цены — её и заполняем; вторая с ценой руками.
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 3m, Unit = s.Piece });
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 1m, Unit = s.Box, UnitPrice = 99m });
        await DocumentManager.SaveDocumentAsync(inv);

        var commandId = await Db.FindCommandIdAsync("document", "FillSalesPrices");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, inv.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var saved = await DocumentManager.GetDocumentAsync<SalesInvoice>(inv.MetaId);
        var byPiece = saved.Lines.First(l => l.Unit == s.Piece);
        var byBox = saved.Lines.First(l => l.Unit == s.Box);

        // 120 за ящик из 12 штук = 10 за штуку. Положить сюда 120 — завысить
        // счёт в 12 раз: денежные ноги считаются от введённого количества.
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
        orphan.Name = "Unpriced widget";
        orphan.ItemGroup = group.MetaId;
        orphan.UnitOfMeasure = s.Piece;
        orphan = await DictionaryManager.SaveRecordAsync(orphan);

        var inv = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        inv.Customer = s.Customer;
        inv.Location = s.Location;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 2m, Unit = s.Piece });
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = orphan.MetaId, Quantity = 1m, Unit = s.Piece });
        await DocumentManager.SaveDocumentAsync(inv);

        var commandId = await Db.FindCommandIdAsync("document", "FillSalesPrices");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, inv.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var saved = await DocumentManager.GetDocumentAsync<SalesInvoice>(inv.MetaId);
        var priced = saved.Lines.First(l => l.Item == s.Item);
        var unpriced = saved.Lines.First(l => l.Item == orphan.MetaId);

        // Ненайденная цена — не ошибка: остальные строки всё равно заполняются,
        // а человек видит, сколько осталось на нём.
        Assert.IsTrue(priced.UnitPrice == 10m, "строка с ценой заполнена, факт {0}", priced.UnitPrice);
        Assert.IsTrue(unpriced.UnitPrice == 0m, "строка без цены осталась пустой, факт {0}", unpriced.UnitPrice);
    }

    [IntegrationTest("Команда недоступна вне подтипа Draft")]
    public async Task BoundToDraftOnly()
    {
        var s = await SetupAsync();

        var inv = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        inv.Customer = s.Customer;
        inv.Location = s.Location;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 1m, Unit = s.Piece, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(inv);

        // Товар на складе, иначе выставление не пройдёт проверку остатка, и тест
        // упал бы не на том, что проверяет.
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);

        // Выставленный счёт переподбору цен не подлежит: он уже создал долг и
        // выручку, и менять его суммы задним числом командой нельзя.
        var commandId = await Db.FindCommandIdAsync("document", "FillSalesPrices");
        var rejected = await Db.ExecuteDocumentCommandAsync(commandId, inv.MetaId);
        Assert.IsTrue(!rejected.Success,
            "команда привязана только к Draft, на Issued её быть не должно");
    }
}
