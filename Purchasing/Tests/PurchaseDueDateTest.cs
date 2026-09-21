using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

public class PurchaseDueDateTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static IMetadataService Metadata => GetService<IMetadataService>();

    [IntegrationTest("Заказ поставщику: условие 10 дней → срок = дата + 10")]
    public async Task TermDaysStampDueDate()
    {
        var loc = await LocationAsync();
        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Net-10 Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        var term = DictionaryManager.NewRecord<PaymentTerm>();
        term.Name = "Net 10";
        term.Days = 10;
        term = await DictionaryManager.SaveRecordAsync(term);

        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = supplier.MetaId;
        order.Location = loc;
        order.DocumentDate = new DateTime(2026, 9, 1);
        order.PaymentTerm = term.MetaId;
        await DocumentManager.SaveDocumentAsync(order);
        var stored = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsTrue(stored != null, "заказ должен сохраниться");
        Assert.IsTrue(stored!.DueDate.Date == new DateTime(2026, 9, 11),
            "срок 01.09 + 10 = 11.09, факт {0:yyyy-MM-dd}", stored.DueDate);
    }

    [IntegrationTest("PurchaseOrderXr печатает DueDate")]
    public async Task PurchaseOrderXrMentionsDueDate()
    {
        var scripts = await Metadata.GetScriptsByObjectAsync("Document",
            Guid.Parse("6935af7d-5f73-45d5-ad4c-d4a21dbe0b67"));
        var script = scripts.FirstOrDefault(x => x.Name == "PurchaseOrderXrPrintForm");
        Assert.IsTrue(script != null, "скрипт PurchaseOrderXrPrintForm есть");
        Assert.IsTrue(script!.Code.Contains("DueDate", StringComparison.Ordinal),
            "форма читает DueDate");
    }

    private async Task<Guid> LocationAsync()
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
        legalEntity.RegistrationNumber = $"REG-PDU-{tag}";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"PDU-{tag}";
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
        zone.Name = "Zone";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"RCV-{tag}";
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
        return cell.MetaId;
    }
}
