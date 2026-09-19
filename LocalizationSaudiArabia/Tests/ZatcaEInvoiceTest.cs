using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
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

    [IntegrationTest("B2B Issued ставит TaxDocument Issued — Cleared только после ответа канала")]
    public async Task B2bStaysIssuedUntilChannelAnswers()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);
        var invoice = await IssueAsync(s);

        var envelopes = await EnvelopesAsync(invoice.MetaId);
        Assert.IsTrue(envelopes.Count == 1, "один конверт, факт {0}", envelopes.Count);
        Assert.IsTrue(envelopes[0].InvoiceType == "Standard" && envelopes[0].Subtype == "Issued",
            "Standard/Issued до Fatoora, факт {0}/{1}", envelopes[0].InvoiceType, envelopes[0].Subtype);
        Assert.IsTrue(envelopes[0].EInvoiceKind == "INVOICE", "INVOICE, факт {0}", envelopes[0].EInvoiceKind);
        Assert.IsTrue(envelopes[0].Uuid != Guid.Empty, "UUID конверта выдан, факт {0}", envelopes[0].Uuid);

        var rows = await DictionaryManager.GetRecordsAsync<TaxSubmission>($"SourceId = '{envelopes[0].MetaId}'");
        Assert.IsTrue(rows.Count == 0,
            "мок больше не пишет Accepted; журнал пуст, факт {0}/{1}",
            rows.Count, rows.FirstOrDefault()?.Status);
    }

    [IntegrationTest("B2C Issued ставит TaxDocument Issued — Reported только после ответа канала")]
    public async Task B2cStaysIssuedUntilChannelAnswers()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2C");
        await StockAsync(s);
        var invoice = await IssueAsync(s);

        var envelopes = await EnvelopesAsync(invoice.MetaId);
        Assert.IsTrue(envelopes.Count == 1, "один конверт, факт {0}", envelopes.Count);
        Assert.IsTrue(envelopes[0].InvoiceType == "Simplified" && envelopes[0].Subtype == "Issued",
            "Simplified/Issued до Fatoora, факт {0}/{1}", envelopes[0].InvoiceType, envelopes[0].Subtype);
    }

    [IntegrationTest("Проведённая кредит-нота несёт QR, UUID и хеш, как счёт")]
    public async Task PostedCreditNoteCarriesQr()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);
        await VatCircuitAsync();
        var invoice = await IssueAsync(s);

        var note = await DocumentManager.NewDocumentAsync<SalesCreditNote>();
        note.Customer = s.Customer;
        note.Outlet = s.Outlet;
        note.Contract = s.Contract;
        note.OriginalInvoice = invoice.MetaId;
        await DocumentManager.SaveDocumentAsync(note);

        var commandId = await Db.FindCommandIdAsync("document", "PostSalesCreditNote");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, note.MetaId);
        Assert.IsTrue(run.Success, "PostSalesCreditNote: {0}",
            run.Message ?? string.Join("; ", run.ClientMessages));

        var posted = await DocumentManager.GetDocumentAsync<SalesCreditNote>(note.MetaId);
        Assert.IsTrue(posted!.Subtype == SalesCreditNote.Subtypes.Posted,
            "нота Posted, факт {0}", posted.Subtype);

        var envelopes = await EnvelopesAsync(note.MetaId);
        Assert.IsTrue(envelopes.Count == 1, "один конверт на ноту, факт {0}", envelopes.Count);
        Assert.IsTrue(envelopes[0].EInvoiceKind == "CREDIT_NOTE" && envelopes[0].Subtype == "Issued",
            "CREDIT_NOTE/Issued, факт {0}/{1}", envelopes[0].EInvoiceKind, envelopes[0].Subtype);
        var xml = Convert.ToString(envelopes[0].Payload) ?? string.Empty;
        Assert.IsTrue(xml.Contains(">381<", StringComparison.Ordinal),
            "UBL кредит-ноты 381, длина {0}", xml.Length);

        var row = await Db.GetAsync("SalesCreditNote", note.MetaId);
        var qr = Convert.ToString(row?["QrCode"]) ?? string.Empty;
        var hash = Convert.ToString(row?["InvoiceHash"]) ?? string.Empty;
        Assert.IsTrue(qr.Length > 20 && hash == Convert.ToString(envelopes[0].InvoiceHash),
            "QR и хеш на ноте, qr={0} hash={1}", qr.Length, hash);
        Assert.IsTrue(GetService<IZatcaQr>().TagCount(qr) == 6,
            "без PEM — теги 1–6, факт {0}", GetService<IZatcaQr>().TagCount(qr));
    }

    [IntegrationTest("Черновик кредит-ноты не создаёт TaxDocument")]
    public async Task DraftCreditNoteDoesNotCreateEnvelope()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);
        await VatCircuitAsync();
        var invoice = await IssueAsync(s);

        var note = await DocumentManager.NewDocumentAsync<SalesCreditNote>();
        note.Customer = s.Customer;
        note.Outlet = s.Outlet;
        note.Contract = s.Contract;
        note.OriginalInvoice = invoice.MetaId;
        await DocumentManager.SaveDocumentAsync(note);

        var envelopes = await EnvelopesAsync(note.MetaId);
        Assert.IsTrue(envelopes.Count == 0,
            "конверт только после проведения, факт {0}", envelopes.Count);
    }

    [IntegrationTest("ApplyChannelOutcomes не трогает Issued, пока очередь пуста")]
    public async Task ApplyChannelOutcomesIsNoOpWhenQueueEmpty()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);
        var invoice = await IssueAsync(s);
        var before = (await EnvelopesAsync(invoice.MetaId))[0].Subtype;

        await GetService<ISaudiEInvoice>().ApplyChannelOutcomesAsync();

        var after = (await EnvelopesAsync(invoice.MetaId))[0].Subtype;
        Assert.IsTrue(before == "Issued" && after == "Issued",
            "без ответа канала подтип не двигается, факт {0}→{1}", before, after);
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

    /// <summary>
    /// A 15% output VAT circuit: authority → jurisdiction → tax → rate →
    /// category → code, plus TaxSettings pointing at it.
    ///
    /// <para>Needed because SetupAsync builds no tax circuit, so every invoice
    /// it issues carries TaxRateApplied = 0. A rounding test on a zero rate
    /// compares 0 with 0 and proves nothing — which is exactly what the first
    /// version of HeaderVatEqualsTheSumOfLines did until the guard caught
    /// it.</para>
    /// </summary>
    private async Task<decimal> VatCircuitAsync()
    {
        var from = new DateTime(2020, 1, 1);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"AU-{Db.NewId():N}"[..10];
        authority.Name = "ZATCA";
        authority.CountryCode = "SA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"JU-{Db.NewId():N}"[..10];
        jurisdiction.Name = "Saudi Arabia";
        jurisdiction.CountryCode = "SA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"VT-{Db.NewId():N}"[..10];
        tax.Name = "Saudi VAT";
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var rate = DictionaryManager.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.Code = $"R-{Db.NewId():N}"[..10];
        rate.Rate = 0.15m;
        rate.EffectiveFrom = from;
        rate = await DictionaryManager.SaveRecordAsync(rate);

        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"STD-{Db.NewId():N}"[..10];
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"OUT-{Db.NewId():N}"[..10];
        code.Name = "Standard 15%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        code = await DictionaryManager.SaveRecordAsync(code);

        if ((await DictionaryManager.GetRecordsAsync<TaxDirection>("Code = 'OUTPUT'", take: 1)).Count == 0)
        {
            var direction = DictionaryManager.NewRecord<TaxDirection>();
            direction.Code = "OUTPUT";
            direction.Name = "Output";
            await DictionaryManager.SaveRecordAsync(direction);
        }

        // TaxSettings is a SINGLETON and cached; the cache outlives a case
        // rollback, so the existing row is edited rather than a new one added.
        var rows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
        return 0.15m;
    }

    [IntegrationTest("Дата в XML и QR — дата документа, а не момент проведения")]
    public async Task IssueDateIsTheDocumentDate()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);

        // Backdated on purpose: this is the case that used to put one date on
        // paper and another in the XML and QR tag 3.
        var backdated = DateTime.UtcNow.Date.AddDays(-18);
        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Location;
        invoice.DocumentDate = backdated;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 10m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(invoice);
        invoice.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        var envelopes = await EnvelopesAsync(invoice.MetaId);
        Assert.IsTrue(envelopes.Count == 1, "конверт создан, факт {0}", envelopes.Count);
        var xml = Convert.ToString(envelopes[0].Payload) ?? string.Empty;
        var expected = backdated.ToString("yyyy-MM-dd");
        Assert.IsTrue(xml.Contains("<cbc:IssueDate>" + expected + "</cbc:IssueDate>", StringComparison.Ordinal),
            "IssueDate = дата документа {0}", expected);

        var row = await Db.GetAsync("SalesRealization", invoice.MetaId);
        var qr = Convert.ToString(row?["QrCode"]) ?? string.Empty;
        var tag3 = ScriptServices.Get<IZatcaQr>().DecodeTag(qr, 3);
        Assert.IsTrue(tag3.StartsWith(expected, StringComparison.Ordinal),
            "тег 3 QR начинается с даты документа; факт '{0}'", tag3);
    }

    [IntegrationTest("cbc:CityName — город из справочника, а не адресная строка")]
    public async Task CityNameComesFromTheCityDictionary()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);

        var legal = await DictionaryManager.GetRecordAsync<LegalEntity>(s.LegalEntity);
        var addr = await DictionaryManager.GetRecordAsync<Address>(legal!.LegalAddress);

        var city = DictionaryManager.NewRecord<City>();
        city.Name = "Riyadh";
        city.Country = addr!.Country;
        city = await DictionaryManager.SaveRecordAsync(city);
        addr.City = city.MetaId;
        await DictionaryManager.SaveRecordAsync(addr);

        var invoice = await IssueAsync(s);
        var xml = Convert.ToString((await EnvelopesAsync(invoice.MetaId))[0].Payload) ?? string.Empty;

        Assert.IsTrue(xml.Contains("<cbc:CityName>Riyadh</cbc:CityName>", StringComparison.Ordinal),
            "город из справочника City");
        Assert.IsTrue(!xml.Contains("<cbc:CityName>Olaya HQ</cbc:CityName>", StringComparison.Ordinal),
            "адресная строка больше не выдаётся за город");
    }

    [IntegrationTest("У покупателя свой код страны, а не код страны продавца")]
    public async Task BuyerKeepsItsOwnCountry()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);

        // Export: the buyer sits in another country. The seller's code used to
        // be written into both parties, so this always read SA.
        var foreign = DictionaryManager.NewRecord<Country>();
        foreign.Name = "United Arab Emirates";
        foreign.CodeISO2 = "AE";
        foreign.CodeISO3 = "ARE";
        foreign.PhoneCode = "971";
        foreign = await DictionaryManager.SaveRecordAsync(foreign);

        var buyerAddress = DictionaryManager.NewRecord<Address>();
        buyerAddress.Name = "Dubai office";
        buyerAddress.Street = "Sheikh Zayed Rd";
        buyerAddress.Building = "9999";
        buyerAddress.District = "Al Quoz";
        buyerAddress.PostalCode = "00000";
        buyerAddress.Country = foreign.MetaId;
        buyerAddress = await DictionaryManager.SaveRecordAsync(buyerAddress);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer!.Address = buyerAddress.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        var invoice = await IssueAsync(s);
        var xml = Convert.ToString((await EnvelopesAsync(invoice.MetaId))[0].Payload) ?? string.Empty;

        var buyerBlock = Between(xml, "<cac:AccountingCustomerParty>", "</cac:AccountingCustomerParty>");
        Assert.IsTrue(buyerBlock.Contains("<cbc:IdentificationCode>AE</cbc:IdentificationCode>", StringComparison.Ordinal),
            "страна покупателя AE; блок покупателя: {0}", buyerBlock.Length);
    }

    [IntegrationTest("НДС шапки равен сумме НДС строк до копейки")]
    public async Task HeaderVatEqualsTheSumOfLines()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);
        await VatCircuitAsync();

        // Three tiny lines: the case where rounding the SUM and summing the
        // ROUNDED lines disagree.
        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Location;
        for (var i = 0; i < 3; i++)
            invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 1m, UnitPrice = 0.10m });
        await DocumentManager.SaveDocumentAsync(invoice);
        invoice.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        var xml = Convert.ToString((await EnvelopesAsync(invoice.MetaId))[0].Payload) ?? string.Empty;
        var amounts = TaxAmounts(xml);
        Assert.IsTrue(amounts.Count >= 2, "шапка и строки присутствуют, факт {0}", amounts.Count);

        var header = amounts[0];
        decimal lines = 0m;
        for (var i = 1; i < amounts.Count; i++) lines += amounts[i];

        // Guard against a vacuous pass: with a zero rate both sides are 0 and
        // the equality below would prove nothing.
        var issued = await DocumentManager.GetDocumentAsync<SalesRealization>(invoice.MetaId);
        Assert.IsTrue(issued!.TaxRateApplied > 0m,
            "ставка ненулевая, иначе сверка бессмысленна; факт {0}", issued.TaxRateApplied);
        Assert.IsTrue(header > 0m, "НДС шапки ненулевой; факт {0}", header);

        Assert.IsTrue(header == lines,
            "НДС шапки {0} равен сумме строк {1}", header, lines);

        // And the arithmetic itself: 3 x 0.10 at 15% is 0.02 a line.
        var expected = decimal.Round(0.10m * issued.TaxRateApplied, 2, MidpointRounding.AwayFromZero) * 3m;
        Assert.IsTrue(header == expected,
            "шапка равна построчному округлению {0}; факт {1}", expected, header);
    }

    /// <summary>Every cbc:TaxAmount in document order — header first, then lines.</summary>
    private static System.Collections.Generic.List<decimal> TaxAmounts(string xml)
    {
        var found = new System.Collections.Generic.List<decimal>();
        var at = 0;
        while (true)
        {
            var open = xml.IndexOf("<cbc:TaxAmount", at, StringComparison.Ordinal);
            if (open < 0) break;
            var gt = xml.IndexOf('>', open);
            var close = xml.IndexOf("</cbc:TaxAmount>", gt, StringComparison.Ordinal);
            if (gt < 0 || close < 0) break;
            var text = xml[(gt + 1)..close];
            if (decimal.TryParse(text, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
                found.Add(value);
            at = close + 1;
        }
        return found;
    }

    private static string Between(string text, string open, string close)
    {
        var a = text.IndexOf(open, StringComparison.Ordinal);
        if (a < 0) return string.Empty;
        var b = text.IndexOf(close, a, StringComparison.Ordinal);
        return b < 0 ? string.Empty : text[a..b];
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

    [IntegrationTest("Standard/Simplified — по VAT покупателя, не по CustomerType")]
    public async Task InvoiceTypeFollowsBuyerVatNotCustomerType()
    {
        await EnableEInvoiceAsync(true);

        var withVat = await SetupAsync("B2C");
        await StockAsync(withVat);
        var buyer = await DictionaryManager.GetRecordAsync<Customer>(withVat.Customer);
        buyer!.TaxRegistrationNumber = "300000000000003";
        await DictionaryManager.SaveRecordAsync(buyer);
        var standard = await IssueAsync(withVat);
        var standardEnv = (await EnvelopesAsync(standard.MetaId))[0];
        Assert.IsTrue(standardEnv.InvoiceType == "Standard"
            && (Convert.ToString(standardEnv.Payload) ?? "").Contains("name=\"0100000\"", StringComparison.Ordinal),
            "VAT есть — Standard, факт {0}", standardEnv.InvoiceType);

        var noVat = await SetupAsync("B2B");
        await StockAsync(noVat);
        var unmarked = await DictionaryManager.GetRecordAsync<Customer>(noVat.Customer);
        unmarked!.TaxRegistrationNumber = "";
        await DictionaryManager.SaveRecordAsync(unmarked);
        var simplified = await IssueAsync(noVat);
        var simplifiedEnv = (await EnvelopesAsync(simplified.MetaId))[0];
        Assert.IsTrue(simplifiedEnv.InvoiceType == "Simplified"
            && (Convert.ToString(simplifiedEnv.Payload) ?? "").Contains("name=\"0200000\"", StringComparison.Ordinal),
            "VAT нет — Simplified, даже если CustomerType=B2B; факт {0}", simplifiedEnv.InvoiceType);
    }

    [IntegrationTest("Нулевая ставка в UBL — категория Z, не S")]
    public async Task ZeroRateUsesCategoryZ()
    {
        await EnableEInvoiceAsync(true);
        var s = await SetupAsync("B2B");
        await StockAsync(s);
        var invoice = await IssueAsync(s);
        var xml = Convert.ToString((await EnvelopesAsync(invoice.MetaId))[0].Payload) ?? string.Empty;
        Assert.IsTrue(xml.Contains("<cbc:ID>Z</cbc:ID>", StringComparison.Ordinal),
            "нулевая ставка — Z, не S");
        Assert.IsTrue(!xml.Contains("<cbc:ID>S</cbc:ID>", StringComparison.Ordinal),
            "S не должен стоять при ставке 0");
    }
}
