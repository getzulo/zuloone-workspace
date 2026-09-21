using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

// PaymentTerm.Days and SalesSettings.DefaultPaymentTermDays existed, but the
// invoice never got a calendar due date. Stamp is date + days; empty term uses
// the setting; a filled term with 0 days is due on the document date.
public class PaymentDueDateTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
    }

    [IntegrationTest("Условие 14 дней даёт срок = дата счёта + 14")]
    public async Task TermDaysStampDueDate()
    {
        var s = await SetupAsync();
        var term = DictionaryManager.NewRecord<PaymentTerm>();
        term.Name = "Net 14";
        term.Days = 14;
        term = await DictionaryManager.SaveRecordAsync(term);

        var inv = await InvoiceAsync(s, new DateTime(2026, 9, 1), term.MetaId);
        Assert.IsTrue(inv.DueDate.Date == new DateTime(2026, 9, 15),
            "срок 01.09 + 14 = 15.09, факт {0:yyyy-MM-dd}", inv.DueDate);
    }

    [IntegrationTest("Без условия берётся DefaultPaymentTermDays")]
    public async Task SettingsDaysWhenTermEmpty()
    {
        var s = await SetupAsync();
        var rows = await DictionaryManager.GetRecordsAsync<SalesSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<SalesSettings>();
        settings.DefaultPaymentTermDays = 30;
        await DictionaryManager.SaveRecordAsync(settings);

        var inv = await InvoiceAsync(s, new DateTime(2026, 9, 1), Guid.Empty);
        Assert.IsTrue(inv.DueDate.Date == new DateTime(2026, 10, 1),
            "срок 01.09 + 30 = 01.10, факт {0:yyyy-MM-dd}", inv.DueDate);
    }

    [IntegrationTest("InvoiceXr печатает DueDate рядом с условием")]
    public async Task InvoiceXrMentionsDueDate()
    {
        var scripts = await Metadata.GetScriptsByObjectAsync("Document",
            Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3"));
        var script = scripts.FirstOrDefault(x => x.Name == "InvoiceXrPrintForm");
        Assert.IsTrue(script != null, "скрипт InvoiceXrPrintForm есть");
        Assert.IsTrue(script!.Code.Contains("DueDate", StringComparison.Ordinal),
            "форма читает DueDate");
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
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = $"REG-DUE-{tag}";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"DUE-{tag}";
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
        cellType.Code = $"PICK-{tag}";
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

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Net-1";
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
        contract.EffectiveFrom = new DateTime(2026, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        return new Setup
        {
            Location = cell.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
        };
    }

    private static async Task<SalesRealization> InvoiceAsync(Setup s, DateTime date, Guid term)
    {
        var inv = await DocumentManager.NewDocumentAsync<SalesRealization>();
        inv.Customer = s.Customer;
        inv.Outlet = s.Outlet;
        inv.Contract = s.Contract;
        inv.Location = s.Location;
        inv.DocumentDate = date;
        inv.PaymentTerm = term;
        await DocumentManager.SaveDocumentAsync(inv);
        var stored = await DocumentManager.GetDocumentAsync<SalesRealization>(inv.MetaId);
        Assert.IsTrue(stored != null, "счёт должен сохраниться");
        return stored!;
    }
}
