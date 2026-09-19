using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;
using ZuloOne.Core.Services.Integration;

// Slice 1 ZATCA: envelope + mock. No XML, QR, CSID, HTTPS.
public class ZatcaEInvoiceTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid LegalEntity;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
    }

    private async Task EnableEInvoiceAsync(bool enabled)
    {
        var rows = await DictionaryManager.GetRecordsAsync<LocalizationSaudiArabiaSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<LocalizationSaudiArabiaSettings>();
        settings.EInvoiceEnabled = enabled;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    private async Task<Setup> SetupAsync(string customerType = "B2B")
    {
        await Db.SetAccountingPeriodsAsync(null, null);

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Riyal";
        currency.Code = $"R{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "﷼";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Saudi Arabia";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "966";
        country = await DictionaryManager.SaveRecordAsync(country);

        var address = DictionaryManager.NewRecord<Address>();
        address.Name = "Olaya HQ";
        address.Street = "King Fahd Rd";
        address.Building = "1234";
        address.District = "Al Olaya";
        address.PostalCode = "12211";
        address.Country = country.MetaId;
        address = await DictionaryManager.SaveRecordAsync(address);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "Riyadh Trading";
        legalEntity.RegistrationNumber = $"REG-SA-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity.TaxRegistrationNumber = "310122393500003";
        legalEntity.LegalAddress = address.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);
        await Db.UpdateAsync("LegalEntity", legalEntity.MetaId,
            new Dictionary<string, object?>
            {
                ["CommercialRegistration"] = "1010010000",
                ["Country"] = country.MetaId,
                ["Currency"] = currency.MetaId,
            });

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"SP-{Db.NewId():N}"[..8];
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
        unit.Code = $"U{Db.NewId():N}"[..3].ToUpperInvariant();
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G{Db.NewId():N}"[..8];
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Gadget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = customerType;
        customer.Address = address.MetaId;
        if (customerType == "B2B")
            customer.TaxRegistrationNumber = "300000000000003";
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
            LegalEntity = legalEntity.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
        };
    }

    private async Task StockAsync(Setup s)
        => await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

    private async Task<SalesRealization> IssueAsync(Setup s)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 10m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(invoice);
        invoice.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
        return (await DocumentManager.GetDocumentAsync<SalesRealization>(invoice.MetaId))!;
    }

    private Task<System.Collections.Generic.List<TaxDocument>> EnvelopesAsync(Guid sourceId)
        => DocumentManager.QueryDocumentsAsync<TaxDocument>($"SourceDocumentId = '{sourceId}'");

    [IntegrationTest("B2B Issued при включённой э-фактуре даёт TaxDocument Cleared")]
    public async Task B2bCleared()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);
        var invoice = await IssueAsync(s);

        var envelopes = await EnvelopesAsync(invoice.MetaId);
        Assert.IsTrue(envelopes.Count == 1, "один конверт, факт {0}", envelopes.Count);
        Assert.IsTrue(envelopes[0].InvoiceType == "Standard" && envelopes[0].Subtype == "Cleared",
            "Standard/Cleared, факт {0}/{1}", envelopes[0].InvoiceType, envelopes[0].Subtype);
        Assert.IsTrue(envelopes[0].EInvoiceKind == "INVOICE", "INVOICE, факт {0}", envelopes[0].EInvoiceKind);
        Assert.IsTrue(envelopes[0].Uuid != Guid.Empty, "UUID конверта выдан, факт {0}", envelopes[0].Uuid);

        var rows = await DictionaryManager.GetRecordsAsync<TaxSubmission>($"SourceId = '{envelopes[0].MetaId}'");
        Assert.IsTrue(rows.Count == 1 && rows[0].Kind == "EINVOICE" && rows[0].Status == "Accepted",
            "журнал EINVOICE/Accepted, факт {0}/{1}/{2}", rows.Count, rows.FirstOrDefault()?.Kind, rows.FirstOrDefault()?.Status);
    }

    [IntegrationTest("B2C Issued даёт TaxDocument Reported (simplified)")]
    public async Task B2cReported()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2C");
        await StockAsync(s);
        var invoice = await IssueAsync(s);

        var envelopes = await EnvelopesAsync(invoice.MetaId);
        Assert.IsTrue(envelopes.Count == 1, "один конверт, факт {0}", envelopes.Count);
        Assert.IsTrue(envelopes[0].InvoiceType == "Simplified" && envelopes[0].Subtype == "Reported",
            "Simplified/Reported, факт {0}/{1}", envelopes[0].InvoiceType, envelopes[0].Subtype);
    }

    [IntegrationTest("Выключенный флаг э-фактуры не создаёт TaxDocument")]
    public async Task FlagOffSkips()
    {
        await EnableEInvoiceAsync(false);
        var s = await SetupAsync();
        await StockAsync(s);
        var invoice = await IssueAsync(s);
        var envelopes = await EnvelopesAsync(invoice.MetaId);
        Assert.IsTrue(envelopes.Count == 0, "без флага конверта нет, факт {0}", envelopes.Count);
    }

    [IntegrationTest("Повторное сохранение Issued не плодит второй TaxDocument")]
    public async Task IssuedSaveIsIdempotent()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync();
        await StockAsync(s);
        var invoice = await IssueAsync(s);
        var first = (await EnvelopesAsync(invoice.MetaId))[0].MetaId;
        await GetService<ISaudiEInvoice>().EnsureForInvoiceAsync(invoice.MetaId);
        var envelopes = await EnvelopesAsync(invoice.MetaId);
        Assert.IsTrue(envelopes.Count == 1 && envelopes[0].MetaId == first,
            "повторный Ensure один конверт, факт {0}", envelopes.Count);
    }

    [IntegrationTest("IsMockReject пишет Rejected и строку журнала")]
    public async Task MockReject()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync();
        await StockAsync(s);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"TA-{Db.NewId():N}"[..8];
        authority.Name = "ZATCA";
        authority.CountryCode = "SA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var conn = DictionaryManager.NewRecord<TaxAuthorityConnection>();
        conn.Code = $"CX-{Db.NewId():N}"[..10];
        conn.Authority = authority.MetaId;
        conn.LegalEntity = s.LegalEntity;
        conn.BaseUrl = "https://mock.zatca.local";
        conn.Environment = TaxConnectionEnvironment.Sandbox;
        conn.Status = TaxConnectionStatus.Active;
        conn.IsMockReject = true;
        await DictionaryManager.SaveRecordAsync(conn);

        var invoice = await IssueAsync(s);
        var envelopes = await EnvelopesAsync(invoice.MetaId);
        Assert.IsTrue(envelopes.Count == 1 && envelopes[0].Subtype == "Rejected",
            "Rejected, факт {0}/{1}", envelopes.Count, envelopes.FirstOrDefault()?.Subtype);

        var rows = await DictionaryManager.GetRecordsAsync<TaxSubmission>($"SourceId = '{envelopes[0].MetaId}'");
        Assert.IsTrue(rows.Count == 1 && rows[0].Status == "Rejected" && rows[0].Kind == "EINVOICE",
            "журнал Rejected/EINVOICE, факт {0}/{1}", rows.FirstOrDefault()?.Status, rows.FirstOrDefault()?.Kind);
    }

    [IntegrationTest("Продавец: НДС, CRN, Country+District на адресе; ICV конверта = 1")]
    public async Task SellerPartyAndFirstIcv()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);

        var legal = await DictionaryManager.GetRecordAsync<LegalEntity>(s.LegalEntity);
        Assert.IsTrue(legal?.TaxRegistrationNumber == "310122393500003",
            "VAT продавца, факт {0}", legal?.TaxRegistrationNumber);
        var crn = await Db.GetAsync("LegalEntity", s.LegalEntity);
        Assert.IsTrue(Convert.ToString(crn?["CommercialRegistration"]) == "1010010000",
            "CRN продавца, факт {0}", crn?["CommercialRegistration"]);
        Assert.IsTrue(legal?.LegalAddress != Guid.Empty, "юридический адрес задан");

        var addr = await DictionaryManager.GetRecordAsync<Address>(legal!.LegalAddress);
        Assert.IsTrue(addr?.Country != Guid.Empty && addr?.District == "Al Olaya" && addr?.Building == "1234",
            "Country/District/Building, факт {0}/{1}/{2}", addr?.Country, addr?.District, addr?.Building);

        var invoice = await IssueAsync(s);
        var envelopes = await EnvelopesAsync(invoice.MetaId);
        Assert.IsTrue(envelopes.Count == 1 && envelopes[0].InvoiceCounter == 1,
            "первый ICV=1, факт {0}/{1}", envelopes.Count, envelopes.FirstOrDefault()?.InvoiceCounter);
        var xml = Convert.ToString(envelopes[0].Payload) ?? string.Empty;
        Assert.IsTrue(xml.Contains("schemeID=\"VAT\"", StringComparison.Ordinal)
            && xml.Contains("310122393500003", StringComparison.Ordinal),
            "UBL продавца VAT, длина {0}", xml.Length);
        Assert.IsTrue(xml.Contains("<cbc:UUID>1</cbc:UUID>", StringComparison.Ordinal)
            && xml.Contains("name=\"0100000\"", StringComparison.Ordinal),
            "ICV и тип Standard 0100000");
        Assert.IsTrue(xml.Contains("Al Olaya", StringComparison.Ordinal) && xml.Contains("1234", StringComparison.Ordinal),
            "район и дом в PostalAddress");
        Assert.IsTrue(xml.Contains(envelopes[0].Uuid.ToString(), StringComparison.Ordinal), "UUID конверта в XML");
        var hash = Convert.ToString(envelopes[0].InvoiceHash) ?? string.Empty;
        Assert.IsTrue(hash.Length > 20 && hash != "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=",
            "InvoiceHash хоста, факт {0}", hash);
        var invRow = await Db.GetAsync("SalesRealization", invoice.MetaId);
        var qr = Convert.ToString(invRow?["QrCode"]) ?? string.Empty;
        Assert.IsTrue(qr.Length > 20 && Convert.ToString(invRow?["InvoiceHash"]) == hash,
            "QR и хеш на счёте, qr={0}", qr.Length);
        // The TLV format moved to this model's ZatcaQr service; the host now
        // only hashes and signs.
        var tagCount = GetService<IZatcaQr>().TagCount(qr);
        Assert.IsTrue(tagCount == 6, "без PEM CSID — теги 1–6, факт {0}", tagCount);
    }

    [IntegrationTest("B2C UBL — тип 0200000 Simplified")]
    public async Task SimplifiedUblTypeCode()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2C");
        await StockAsync(s);
        var invoice = await IssueAsync(s);
        var xml = Convert.ToString((await EnvelopesAsync(invoice.MetaId))[0].Payload) ?? string.Empty;
        Assert.IsTrue(xml.Contains("name=\"0200000\"", StringComparison.Ordinal) && xml.Contains(">388<", StringComparison.Ordinal),
            "Simplified 0200000 / 388, длина {0}", xml.Length);
    }

    [IntegrationTest("Второй счёт того же юрлица получает ICV=2")]
    public async Task IcvIncrementsPerLegalEntity()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);
        var first = await IssueAsync(s);
        var second = await IssueAsync(s);

        var a = await EnvelopesAsync(first.MetaId);
        var b = await EnvelopesAsync(second.MetaId);
        Assert.IsTrue(a.Count == 1 && a[0].InvoiceCounter == 1, "первый ICV=1, факт {0}", a.FirstOrDefault()?.InvoiceCounter);
        Assert.IsTrue(b.Count == 1 && b[0].InvoiceCounter == 2, "второй ICV=2, факт {0}", b.FirstOrDefault()?.InvoiceCounter);
        Assert.IsTrue(a[0].LegalEntity == s.LegalEntity && b[0].LegalEntity == s.LegalEntity,
            "оба конверта того же юрлица");
        Assert.IsTrue((Convert.ToString(b[0].Payload) ?? string.Empty).Contains("<cbc:UUID>2</cbc:UUID>", StringComparison.Ordinal),
            "второй UBL несёт ICV=2");
        Assert.IsTrue(!string.IsNullOrWhiteSpace(Convert.ToString(a[0].InvoiceHash))
            && Convert.ToString(b[0].PreviousInvoiceHash) == Convert.ToString(a[0].InvoiceHash),
            "PIH второго = hash первого, факт {0} / {1}",
            b[0].PreviousInvoiceHash, a[0].InvoiceHash);
    }
}
