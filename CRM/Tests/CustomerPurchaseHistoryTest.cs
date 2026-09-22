using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

// Источник отчёта CustomerPurchaseHistoryReport — тот же JOIN. Черновик
// не попадает: оператор смотрит историю продаж, а не корзину.
public class CustomerPurchaseHistoryTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();
    private static ISqlService Sql => GetService<ISqlService>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
    }

    private async Task<Setup> SetupAsync()
    {
        var tag = $"{Db.NewId():N}"[..8];

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = $"E{tag}"[..3];
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = $"{tag}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{tag}"[..3].ToUpperInvariant();
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "History GmbH";
        legalEntity.RegistrationNumber = $"REG-{tag}";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"DT-{tag}"[..10];
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
        zone.Name = "Zone";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"PICK-{tag}"[..12];
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

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "Piece";
        unit.Code = $"U{tag}"[..5];
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G{tag}"[..8];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Gadget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop A";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "H-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
        };
    }

    private async Task<decimal> HistoryAmountAsync(Guid customer)
    {
        var rows = await Sql.SelectAsync(@"
SELECT l.[Quantity] * l.[UnitPrice] AS Amount
FROM [SalesRealization] h
INNER JOIN [TP_SalesInvoiceLines] l ON l.[OwnerMetaId] = h.[MetaId]
WHERE h.[Customer] = @customer AND h.[Subtype] NOT IN (N'Draft', N'Cancelled')",
            new Dictionary<string, object?> { ["customer"] = customer });
        return rows.Sum(r => Convert.ToDecimal(r["Amount"]));
    }

    [IntegrationTest("Выставленная реализация попадает в историю, черновик — нет")]
    public async Task IssuedLineShowsDraftDoesNot()
    {
        var s = await SetupAsync();
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

        var draft = await DocumentManager.NewDocumentAsync<SalesRealization>();
        draft.Customer = s.Customer;
        draft.Outlet = s.Outlet;
        draft.Contract = s.Contract;
        draft.Location = s.Location;
        draft.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 2m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(draft);

        Assert.IsTrue(await HistoryAmountAsync(s.Customer) == 0m,
            "черновик не история покупок, факт {0}", await HistoryAmountAsync(s.Customer));

        var issued = await DocumentManager.NewDocumentAsync<SalesRealization>();
        issued.Customer = s.Customer;
        issued.Outlet = s.Outlet;
        issued.Contract = s.Contract;
        issued.Location = s.Location;
        issued.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 3m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(issued);
        issued.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(issued);

        Assert.IsTrue(await HistoryAmountAsync(s.Customer) == 15m,
            "3 × 5 = 15, факт {0}", await HistoryAmountAsync(s.Customer));
    }
}
