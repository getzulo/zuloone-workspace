using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Testing;
using ZuloOne.Runtime.Generated;

// Сам алгоритм захвата цены уже полностью проверен в
// Inventory/Tests/PriceCaptureTest.cs; здесь доказывается только то, что
// SalesInvoiceEventHandler.OnAfterPostAsync реально его вызывает при
// выставлении счёта и не дублирует строку при перепроведении.
public class PriceCaptureTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Piece;
        public Guid Customer;
        public Guid PriceList;
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

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Db.NewId():N}"[..12];
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = piece.MetaId;
        item = await DictionaryManager.SaveRecordAsync(item);

        var list = DictionaryManager.NewRecord<PriceList>();
        list.Name = $"Retail {Db.NewId():N}"[..16];
        list.Direction = PriceDirection.Sale;
        list = await DictionaryManager.SaveRecordAsync(list);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer.PriceList = list.MetaId;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            Piece = piece.MetaId,
            Customer = customer.MetaId,
            PriceList = list.MetaId,
        };
    }

    private async Task<SalesInvoice> NewInvoiceAsync(Setup s, decimal qty, decimal price)
    {
        var inv = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        inv.Customer = s.Customer;
        inv.Location = s.Location;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Unit = s.Piece, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(inv);
        return inv;
    }

    // Товар на складе, иначе выставление не пройдёт проверку остатка.
    private Task SeedStockAsync(Setup s, decimal qty)
        => TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = qty });

    private Task<List<PriceListItem>> RowsAsync(Setup s)
        => DictionaryManager.GetRecordsAsync<PriceListItem>(
            $"PriceList = '{s.PriceList}' AND Item = '{s.Item}' AND Unit = '{s.Piece}'");

    [IntegrationTest("Выставление счёта с продажным типом цен Base пишет цену строки в PriceListItem")]
    public async Task IssuingCapturesLinePrice()
    {
        var s = await SetupAsync();
        var inv = await NewInvoiceAsync(s, qty: 1m, price: 15m);
        await SeedStockAsync(s, 10m);

        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 1, "выставление обязано создать ровно одну строку истории цены, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].Price == 15m, "цена в истории обязана быть 15, факт {0}", rows[0].Price);
        Assert.IsTrue(rows[0].EffectiveTo == null, "единственная строка обязана остаться открытой, факт {0}", rows[0].EffectiveTo);
    }

    [IntegrationTest("Перепроведение (Issued → Draft → Issued) не плодит вторую строку истории")]
    public async Task RepostingDoesNotDuplicateRow()
    {
        var s = await SetupAsync();
        var inv = await NewInvoiceAsync(s, qty: 1m, price: 15m);
        await SeedStockAsync(s, 10m);

        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);

        // Перепроведение — КРУГОВОЙ переход подтипа (выйти и вернуться в
        // Issued): при выходе разносящие Issued транзакционные скрипты (в
        // т.ч. списание Stock) разворачиваются платформой, поэтому повторный
        // вход снова проходит проверку остатка без досева склада.
        inv.Subtype = SalesInvoice.Subtypes.Draft;
        await DocumentManager.SaveDocumentAsync(inv);
        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 1, "перепроведение той же цены не должно плодить вторую строку, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].Price == 15m, "цена обязана остаться 15, факт {0}", rows[0].Price);
    }
}
