using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Testing;
using ZuloOne.Runtime.Generated;

// Проведение счёта НЕ пишет историю цен. Захват — явный вызов сервиса
// (Inventory/Tests/PriceCaptureTest), не побочный эффект Issued.
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

    [IntegrationTest("Выставление счёта не пишет цену строки в историю типа цен")]
    public async Task IssuingDoesNotCaptureLinePrice()
    {
        var s = await SetupAsync();
        var inv = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        inv.Customer = s.Customer;
        inv.Location = s.Location;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Unit = s.Piece, Quantity = 1m, UnitPrice = 15m });
        await DocumentManager.SaveDocumentAsync(inv);

        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);

        var rows = await DictionaryManager.GetRecordsAsync<PriceListItem>(
            $"PriceList = '{s.PriceList}' AND Item = '{s.Item}' AND Unit = '{s.Piece}'");
        Assert.IsTrue(rows.Count == 0, "счёт не должен писать историю цен, факт {0} строк", rows.Count);
    }
}
