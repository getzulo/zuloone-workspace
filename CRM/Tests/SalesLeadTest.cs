using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class SalesLeadTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();
    private static ILeadService Leads => GetService<ILeadService>();

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
        legalEntity.RegistrationNumber = "REG-SL-1";
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

    private async Task<SalesLead> NewLeadAsync(Setup s, bool commercial)
    {
        var lead = await DocumentManager.NewDocumentAsync<SalesLead>();
        lead.Subject = "Retail chain inquiry";
        if (commercial)
        {
            lead.Customer = s.Customer;
            lead.Outlet = s.Outlet;
            lead.Contract = s.Contract;
            lead.Location = s.Location;
            lead.DeliveryDate = DateTime.UtcNow.Date.AddDays(3);
            lead.Notes = "From field visit";
        }
        await DocumentManager.SaveDocumentAsync(lead);
        return (await DocumentManager.GetDocumentAsync<SalesLead>(lead.MetaId))!;
    }

    private static Task<decimal> StockAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item });

    [IntegrationTest("Qualify переводит New в Qualified")]
    public async Task QualifyMovesToQualified()
    {
        var s = await SetupAsync();
        var lead = await NewLeadAsync(s, commercial: false);
        Assert.AreEqual(SalesLead.Subtypes.New, lead.Subtype);
        await RunCommandAsync("QualifyLead", lead.MetaId);
        var stored = await DocumentManager.GetDocumentAsync<SalesLead>(lead.MetaId);
        Assert.AreEqual(SalesLead.Subtypes.Qualified, stored!.Subtype);
    }

    [IntegrationTest("Convert копирует шапку в Draft-КП")]
    public async Task ConvertCopiesHeaderToDraftQuotation()
    {
        var s = await SetupAsync();
        var lead = await NewLeadAsync(s, commercial: true);
        await RunCommandAsync("QualifyLead", lead.MetaId);

        var quoteId = await Leads.CreateQuotationAsync(lead.MetaId);
        Assert.AreNotEqual(Guid.Empty, quoteId, "КП создано");

        var quote = await DocumentManager.GetDocumentAsync<SalesQuotation>(quoteId);
        Assert.IsNotNull(quote);
        Assert.AreEqual(SalesQuotation.Subtypes.Draft, quote!.Subtype, "КП остаётся Draft");
        Assert.AreEqual(s.Customer, quote.Customer);
        Assert.AreEqual(s.Contract, quote.Contract);
        Assert.AreEqual(s.Location, quote.Location);
        Assert.AreEqual(lead.MetaId, quote.SourceLead);
        Assert.AreEqual(0, quote.Lines.Count, "у лида нет строк — КП пустое");

        var quoted = await DocumentManager.GetDocumentAsync<SalesLead>(lead.MetaId);
        Assert.AreEqual(SalesLead.Subtypes.Quoted, quoted!.Subtype);
    }

    [IntegrationTest("Повтор Convert не плодит второе КП")]
    public async Task ConvertIsIdempotent()
    {
        var s = await SetupAsync();
        var lead = await NewLeadAsync(s, commercial: true);
        await RunCommandAsync("QualifyLead", lead.MetaId);
        var first = await Leads.CreateQuotationAsync(lead.MetaId);
        var second = await Leads.CreateQuotationAsync(lead.MetaId);
        Assert.AreEqual(first, second);
        var count = await DocumentManager.CountDocumentsAsync<SalesQuotation>($"SourceLead = '{lead.MetaId}'");
        Assert.AreEqual(1, count);
    }

    [IntegrationTest("Без клиента КП не создаётся")]
    public async Task ConvertWithoutCustomerRejected()
    {
        var s = await SetupAsync();
        var lead = await NewLeadAsync(s, commercial: false);
        await RunCommandAsync("QualifyLead", lead.MetaId);

        var commandId = await Db.FindCommandIdAsync("document", "ConvertLead");
        await Db.ExecuteDocumentCommandAsync(commandId, lead.MetaId);
        var quoteId = await Leads.CreateQuotationAsync(lead.MetaId);
        Assert.AreEqual(Guid.Empty, quoteId, "без коммерческих полей КП нет");
        var stored = await DocumentManager.GetDocumentAsync<SalesLead>(lead.MetaId);
        Assert.AreEqual(SalesLead.Subtypes.Qualified, stored!.Subtype, "остаётся Qualified");
    }

    [IntegrationTest("Convert лида не двигает склад")]
    public async Task ConvertDoesNotMoveStock()
    {
        var s = await SetupAsync();
        var before = await StockAsync(s);
        var lead = await NewLeadAsync(s, commercial: true);
        await RunCommandAsync("QualifyLead", lead.MetaId);
        await Leads.CreateQuotationAsync(lead.MetaId);
        var after = await StockAsync(s);
        Assert.AreEqual(before, after, "лид и Draft-КП не пишут Stock");
    }
}
