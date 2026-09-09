using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

/// <summary>
/// Master-data coverage: PaymentTerm, Brand, CustomerContact creation;
/// extra fields on Customer and Item; automatic copy of PaymentTerm and
/// primary Contact onto new SalesOrder and SalesInvoice.
/// </summary>
public class SalesMasterDataTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private async Task<Guid> MakeLocationAsync()
    {
        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "MDTest-WH";
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "MDTest-Zone";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"MD-{Guid.NewGuid():N}"[..10];
        cellType.Name = "MDTest-Type";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "MDTest-Cell";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        return cell.MetaId;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 1: PaymentTerm, Brand, CustomerContact basics + Customer/Item fields
    // ──────────────────────────────────────────────────────────────────────

    [IntegrationTest("PaymentTerm, Brand, CustomerContact и расширения Customer и Item создаются")]
    public async Task MasterDataRecordsCreated()
    {
        // PaymentTerm
        var pt = DictionaryManager.NewRecord<PaymentTerm>();
        pt.Name = "Net 30";
        pt.Days = 30;
        pt = await DictionaryManager.SaveRecordAsync(pt);
        Assert.IsTrue(pt.MetaId != Guid.Empty, "PaymentTerm сохранён");
        Assert.IsTrue(pt.Days == 30, "PaymentTerm.Days == 30, факт {0}", pt.Days);

        // Brand
        var brand = DictionaryManager.NewRecord<Brand>();
        brand.Name = "TestBrand";
        brand = await DictionaryManager.SaveRecordAsync(brand);
        Assert.IsTrue(brand.MetaId != Guid.Empty, "Brand сохранён");

        // Address
        var addr = DictionaryManager.NewRecord<Address>();
        addr.Name = "123 Test St";
        addr = await DictionaryManager.SaveRecordAsync(addr);

        // Customer with extended fields
        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "MDG";
        group.Name = "MD Group";
        group = await DictionaryManager.SaveRecordAsync(group);

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "MD-Pcs";
        unit.Code = "MDU";
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "MD Customer";
        customer.CustomerType = "B2B";
        customer.Address = addr.MetaId;
        customer.PaymentTerm = pt.MetaId;
        customer.CreditLimit = 5000m;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var loaded = await DictionaryManager.GetRecordAsync<Customer>(customer.MetaId);
        Assert.IsTrue(loaded is not null, "Customer загружен");
        Assert.IsTrue(loaded!.Address == addr.MetaId, "Customer.Address сохранён");
        Assert.IsTrue(loaded.PaymentTerm == pt.MetaId, "Customer.PaymentTerm сохранён");
        Assert.IsTrue(loaded.CreditLimit == 5000m, "Customer.CreditLimit == 5000, факт {0}", loaded.CreditLimit);

        // Item with Description + Image + Brand
        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "MD Item";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Description = "Test description";
        item.Brand = brand.MetaId;
        // Minimal 1×1 PNG bytes (enough to test round-trip)
        item.Image = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==";
        item = await DictionaryManager.SaveRecordAsync(item);

        var loadedItem = await DictionaryManager.GetRecordAsync<Item>(item.MetaId);
        Assert.IsTrue(loadedItem is not null, "Item загружен");
        Assert.IsTrue(loadedItem!.Description == "Test description", "Item.Description сохранён");
        Assert.IsTrue(loadedItem.Brand == brand.MetaId, "Item.Brand сохранён");

        // CustomerContact — primary
        var cc = DictionaryManager.NewRecord<CustomerContact>();
        cc.Customer = customer.MetaId;
        cc.Name = "John Doe";
        cc.Phone = "+1-555-0001";
        cc.Email = "john@test.com";
        cc.IsPrimary = true;
        cc = await DictionaryManager.SaveRecordAsync(cc);
        Assert.IsTrue(cc.IsPrimary, "CustomerContact IsPrimary=true сохранён");

        // Second non-primary contact
        var cc2 = DictionaryManager.NewRecord<CustomerContact>();
        cc2.Customer = customer.MetaId;
        cc2.Name = "Jane Doe";
        cc2.IsPrimary = false;
        cc2 = await DictionaryManager.SaveRecordAsync(cc2);

        // Third contact set as primary — first should be cleared
        var cc3 = DictionaryManager.NewRecord<CustomerContact>();
        cc3.Customer = customer.MetaId;
        cc3.Name = "Bob Smith";
        cc3.IsPrimary = true;
        cc3 = await DictionaryManager.SaveRecordAsync(cc3);

        // Reload cc — its IsPrimary should now be false
        var reloadedCc = await DictionaryManager.GetRecordAsync<CustomerContact>(cc.MetaId);
        Assert.IsTrue(reloadedCc is not null, "Первый контакт существует");
        Assert.IsTrue(!reloadedCc!.IsPrimary,
            "Первый контакт потерял флаг IsPrimary после назначения нового основного контакта");

        // Reload cc3 — it should be primary
        var reloadedCc3 = await DictionaryManager.GetRecordAsync<CustomerContact>(cc3.MetaId);
        Assert.IsTrue(reloadedCc3!.IsPrimary, "Новый основной контакт имеет IsPrimary=true");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 2: New SalesOrder copies PaymentTerm and Contact from Customer
    // ──────────────────────────────────────────────────────────────────────

    [IntegrationTest("Новый SalesOrder копирует PaymentTerm и Contact из клиента")]
    public async Task SalesOrderCopiesPaymentTermAndContact()
    {
        var location = await MakeLocationAsync();

        var pt = DictionaryManager.NewRecord<PaymentTerm>();
        pt.Name = "Net 15";
        pt.Days = 15;
        pt = await DictionaryManager.SaveRecordAsync(pt);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "SO-Copy Customer";
        customer.CustomerType = "B2B";
        customer.PaymentTerm = pt.MetaId;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var cc = DictionaryManager.NewRecord<CustomerContact>();
        cc.Customer = customer.MetaId;
        cc.Name = "Primary Person";
        cc.IsPrimary = true;
        cc = await DictionaryManager.SaveRecordAsync(cc);

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "SO-Pcs";
        unit.Code = $"SUP{Guid.NewGuid():N}"[..6];
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"SOG{Guid.NewGuid():N}"[..6];
        group.Name = "SO Group";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "SO Item";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        // Create SalesOrder — OnBeforeSave(isNew=true) should copy PaymentTerm + Contact
        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = customer.MetaId;
        order.Location = location;
        order.DeliveryDate = DateTime.UtcNow.AddDays(7);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = item.MetaId, Quantity = 1m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(order);

        // Reload to check fields written to DB
        var loaded = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);
        Assert.IsTrue(loaded is not null, "SalesOrder загружен");
        Assert.IsTrue(loaded!.PaymentTerm == pt.MetaId,
            "SalesOrder.PaymentTerm скопирован из клиента, факт {0}", loaded.PaymentTerm);
        Assert.IsTrue(loaded.Contact == cc.MetaId,
            "SalesOrder.Contact скопирован из основного контакта клиента, факт {0}", loaded.Contact);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test 3: New SalesInvoice copies PaymentTerm and Contact from Customer
    // ──────────────────────────────────────────────────────────────────────

    [IntegrationTest("Новый SalesInvoice копирует PaymentTerm и Contact из клиента")]
    public async Task SalesInvoiceCopiesPaymentTermAndContact()
    {
        var location = await MakeLocationAsync();

        var pt = DictionaryManager.NewRecord<PaymentTerm>();
        pt.Name = "Net 45";
        pt.Days = 45;
        pt = await DictionaryManager.SaveRecordAsync(pt);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "SI-Copy Customer";
        customer.CustomerType = "B2B";
        customer.PaymentTerm = pt.MetaId;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var cc = DictionaryManager.NewRecord<CustomerContact>();
        cc.Customer = customer.MetaId;
        cc.Name = "Invoice Person";
        cc.IsPrimary = true;
        cc = await DictionaryManager.SaveRecordAsync(cc);

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "SI-Pcs";
        unit.Code = $"SIP{Guid.NewGuid():N}"[..6];
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"SIG{Guid.NewGuid():N}"[..6];
        group.Name = "SI Group";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "SI Item";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        // Create SalesInvoice — OnBeforeSave(isNew=true) should copy PaymentTerm + Contact
        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = customer.MetaId;
        invoice.Location = location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = item.MetaId, Quantity = 1m, UnitPrice = 20m });
        await DocumentManager.SaveDocumentAsync(invoice);

        var loaded = await DocumentManager.GetDocumentAsync<SalesInvoice>(invoice.MetaId);
        Assert.IsTrue(loaded is not null, "SalesInvoice загружен");
        Assert.IsTrue(loaded!.PaymentTerm == pt.MetaId,
            "SalesInvoice.PaymentTerm скопирован из клиента, факт {0}", loaded.PaymentTerm);
        Assert.IsTrue(loaded.Contact == cc.MetaId,
            "SalesInvoice.Contact скопирован из основного контакта клиента, факт {0}", loaded.Contact);
    }
}
