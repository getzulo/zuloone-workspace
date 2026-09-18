using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class SalesQuotationTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();
    private static IQuotationService Quotations => GetService<IQuotationService>();

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

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
        legalEntity.RegistrationNumber = "REG-SQ-1";
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
        item.Name = "Bread";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Image = TinyPng;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Store 12";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop A";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "A-2026";
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

    private async Task RunCommandAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        Assert.IsTrue(run.Success, "команда {0}: {1}", name, run.Message ?? string.Join("; ", run.ClientMessages));
    }

    private async Task<SalesQuotation> NewQuoteAsync(Setup s, decimal qty, decimal price)
    {
        var quote = await DocumentManager.NewDocumentAsync<SalesQuotation>();
        quote.Customer = s.Customer;
        quote.Outlet = s.Outlet;
        quote.Contract = s.Contract;
        quote.Location = s.Location;
        quote.DeliveryDate = DateTime.UtcNow.Date.AddDays(1);
        quote.Lines.Add(new SalesQuotationLinesTablePartRow
        {
            Item = s.Item,
            Quantity = qty,
            UnitPrice = price,
        });
        await DocumentManager.SaveDocumentAsync(quote);
        return (await DocumentManager.GetDocumentAsync<SalesQuotation>(quote.MetaId))!;
    }

    private static Task<decimal> StockAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item });

    [IntegrationTest("Пустое КП нельзя выставить")]
    public async Task IssueEmptyRejected()
    {
        var s = await SetupAsync();
        var quote = await DocumentManager.NewDocumentAsync<SalesQuotation>();
        quote.Customer = s.Customer;
        quote.Outlet = s.Outlet;
        quote.Contract = s.Contract;
        quote.Location = s.Location;
        quote.DeliveryDate = DateTime.UtcNow.Date.AddDays(1);
        await DocumentManager.SaveDocumentAsync(quote);

        var commandId = await Db.FindCommandIdAsync("document", "IssueQuotation");
        await Db.ExecuteDocumentCommandAsync(commandId, quote.MetaId);
        var stored = await DocumentManager.GetDocumentAsync<SalesQuotation>(quote.MetaId);
        Assert.AreEqual(SalesQuotation.Subtypes.Draft, stored!.Subtype, "пустое КП остаётся Draft");
    }

    [IntegrationTest("Convert копирует шапку и строки в Draft-заказ")]
    public async Task ConvertCopiesHeaderAndLines()
    {
        var s = await SetupAsync();
        var quote = await NewQuoteAsync(s, 3m, 12.5m);
        await RunCommandAsync("IssueQuotation", quote.MetaId);

        var orderId = await Quotations.ConvertToOrderAsync(quote.MetaId);
        Assert.AreNotEqual(Guid.Empty, orderId, "заказ создан");

        var order = await DocumentManager.GetDocumentAsync<SalesOrder>(orderId);
        Assert.IsNotNull(order);
        Assert.AreEqual(SalesOrder.Subtypes.Draft, order!.Subtype, "заказ остаётся Draft");
        Assert.AreEqual(s.Customer, order.Customer);
        Assert.AreEqual(s.Contract, order.Contract);
        Assert.AreEqual(s.Location, order.Location);
        Assert.AreEqual(quote.MetaId, order.SourceQuotation);
        Assert.AreEqual(1, order.Lines.Count);
        Assert.AreEqual(s.Item, order.Lines[0].Item);
        Assert.AreEqual(3m, order.Lines[0].Quantity);
        Assert.AreEqual(12.5m, order.Lines[0].UnitPrice);

        var converted = await DocumentManager.GetDocumentAsync<SalesQuotation>(quote.MetaId);
        Assert.AreEqual(SalesQuotation.Subtypes.Converted, converted!.Subtype);
    }

    [IntegrationTest("Повтор Convert не плодит второй заказ")]
    public async Task ConvertIsIdempotent()
    {
        var s = await SetupAsync();
        var quote = await NewQuoteAsync(s, 1m, 10m);
        await RunCommandAsync("IssueQuotation", quote.MetaId);
        var first = await Quotations.ConvertToOrderAsync(quote.MetaId);
        var second = await Quotations.ConvertToOrderAsync(quote.MetaId);
        Assert.AreEqual(first, second);
        var count = await DocumentManager.CountDocumentsAsync<SalesOrder>($"SourceQuotation = '{quote.MetaId}'");
        Assert.AreEqual(1, count);
    }

    [IntegrationTest("Выставление КП не двигает склад")]
    public async Task IssueDoesNotMoveStock()
    {
        var s = await SetupAsync();
        var before = await StockAsync(s);
        var quote = await NewQuoteAsync(s, 5m, 8m);
        await RunCommandAsync("IssueQuotation", quote.MetaId);
        var after = await StockAsync(s);
        Assert.AreEqual(before, after, "Issued КП не пишет Stock");
    }

    [IntegrationTest("Просроченный ValidUntil не конвертируется")]
    public async Task ExpiredValidUntilRejectsConvert()
    {
        var s = await SetupAsync();
        var quote = await NewQuoteAsync(s, 2m, 9m);
        quote.ValidUntil = DateTime.UtcNow.Date.AddDays(-1);
        await DocumentManager.SaveDocumentAsync(quote);
        await RunCommandAsync("IssueQuotation", quote.MetaId);

        var commandId = await Db.FindCommandIdAsync("document", "ConvertQuotation");
        await Db.ExecuteDocumentCommandAsync(commandId, quote.MetaId);
        var orderId = await Quotations.ConvertToOrderAsync(quote.MetaId);
        Assert.AreEqual(Guid.Empty, orderId, "просроченное КП не даёт заказ");
        var stored = await DocumentManager.GetDocumentAsync<SalesQuotation>(quote.MetaId);
        Assert.AreEqual(SalesQuotation.Subtypes.Issued, stored!.Subtype, "остаётся Issued");
    }
}
