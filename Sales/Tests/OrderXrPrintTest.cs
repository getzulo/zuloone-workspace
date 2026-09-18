using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Documents;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

public class OrderXrPrintTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    private static readonly Guid SalesOrderTypeId = Guid.Parse("23643b1b-b959-4206-83ab-948c713276c9");

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
        legalEntity.RegistrationNumber = "REG-OX-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "SP";
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
        cellType.Code = $"PICK-{Guid.NewGuid():N}"[..12];
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
        unit.Code = "PCS";
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "GOODS";
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Print Bread";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Image = TinyPng;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Print Kiosk";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Front door";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "Print-2026";
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

    [IntegrationTest("OrderXr пишет имя покупателя и товара, не их id")]
    public async Task PrintFormNamesCustomerAndItem()
    {
        var s = await SetupAsync();
        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = s.Customer;
        order.Outlet = s.Outlet;
        order.Contract = s.Contract;
        order.Location = s.Location;
        order.DeliveryDate = DateTime.UtcNow.Date.AddDays(1);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = s.Item, Quantity = 4m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(order);

        var table = await new OrderXrPrintForm().GetDataAsync(new PrintFormContext
        {
            DocumentTypeId = SalesOrderTypeId,
            RecordId = order.MetaId,
            Services = ScriptServices.Root,
        });

        Assert.IsTrue(table.Count == 1, "одна строка заказа, факт {0}", table.Count);
        var row = table[0];
        Assert.IsTrue(
            string.Equals(row["Customer"]?.ToString(), "Print Kiosk", StringComparison.Ordinal),
            "покупатель именем, факт {0}", row["Customer"]);
        Assert.IsTrue(
            string.Equals(row["Item"]?.ToString(), "Print Bread", StringComparison.Ordinal),
            "номенклатура именем, факт {0}", row["Item"]);
        Assert.IsTrue(
            Convert.ToDecimal(row["Amount"]) == 20m,
            "4×5 через IPricingService = 20, факт {0}", row["Amount"]);
        Assert.IsTrue(
            string.Equals(row["Number"]?.ToString(), order.ID, StringComparison.Ordinal),
            "номер документа, не MetaId, факт {0}", row["Number"]);
    }
}
