#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Core.Services.Integration;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// UBL 2.1 XML on TaxDocument.Payload; queue via IOutboundGateway.
//
// The invoice hash is computed HERE: System.Security.Cryptography is in the
// script reference set, so there is no longer a reason for a kernel helper to
// own SHA-256 on behalf of one country. What the host still owns is the KEY —
// ICredentialSigner resolves the CSID credential and answers with the signature
// and the public half. No PEM in this script, and none possible: a script
// cannot resolve ICredentialResolver.
//
// Channels Enabled=false: row is written, nothing is posted to Fatoora.
public partial class SaudiEInvoice
{
    private static readonly Guid TaxDocumentType = Guid.Parse("cee833d7-fa4c-43fe-bef6-e95633ece906");
    private static readonly Guid SalesInvoiceType = Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3");
    private static readonly string FirstPih = Sha256Base64("");

    /// <summary>
    /// Credential holding the CSID PEM when a legal entity names none of its
    /// own. The DEFAULT moved here with the rest: ICredentialSigner is
    /// country-neutral and must not know that "fatoora-csid" is the usual name
    /// in Saudi Arabia.
    /// </summary>
    private const string DefaultCsidCredential = "fatoora-csid";

    public Task<Guid?> EnsureForInvoiceAsync(Guid invoiceId)
        => EnsureAsync(invoiceId, "SalesRealization", "INVOICE");

    public Task<Guid?> EnsureForCreditNoteAsync(Guid creditNoteId)
        => EnsureAsync(creditNoteId, "SalesCreditNote", "CREDIT_NOTE");

    private async Task<Guid?> EnsureAsync(Guid sourceId, string sourceType, string documentType)
    {
        var dict = ScriptServices.Get<IDictionaryManager>();
        var settingsRows = await dict.GetRecordsAsync<LocalizationSaudiArabiaSettings>(null, 1);
        if (settingsRows.Count == 0 || !settingsRows[0].EInvoiceEnabled)
            return null;

        var docs = ScriptServices.Get<IDocumentManager>();
        var existing = await docs.QueryDocumentsAsync<TaxDocument>($"SourceDocumentId = '{sourceId}'");
        if (existing.Count > 0)
            return existing[0].MetaId;

        Guid customerId;
        Guid legalEntity;
        SalesRealization? invoice = null;
        SalesCreditNote? note = null;
        if (sourceType == "SalesRealization")
        {
            invoice = await docs.GetDocumentAsync<SalesRealization>(sourceId);
            if (invoice is null) return null;
            customerId = invoice.Customer;
            legalEntity = invoice.LegalEntity;
        }
        else
        {
            note = await docs.GetDocumentAsync<SalesCreditNote>(sourceId);
            if (note is null) return null;
            customerId = note.Customer;
            legalEntity = note.LegalEntity;
        }

        var customer = customerId == Guid.Empty
            ? null
            : await ScriptServices.Get<IDictionaryManager<Customer>>().GetRecordAsync(customerId);
        var invoiceType = string.Equals(customer?.CustomerType, "B2B", StringComparison.OrdinalIgnoreCase)
            ? "Standard"
            : "Simplified";

        var icv = await NextInvoiceCounterAsync(docs, legalEntity);
        var pih = await PreviousHashAsync(docs, legalEntity) ?? FirstPih;

        var envelope = await docs.NewDocumentAsync<TaxDocument>();
        envelope.LegalEntity = legalEntity;
        envelope.SourceDocumentType = sourceType;
        envelope.SourceDocumentId = sourceId;
        envelope.EInvoiceKind = documentType;
        envelope.InvoiceType = invoiceType;
        envelope.Uuid = Guid.NewGuid();
        envelope.InvoiceCounter = icv;
        envelope.PreviousInvoiceHash = pih;
        var draft = await BuildUblAsync(envelope, invoice, note, customer, documentType, invoiceType);
        envelope.Payload = draft.Xml;
        envelope.InvoiceHash = Sha256Base64(draft.Xml);
        await docs.SaveDocumentAsync(envelope);

        var posting = ScriptServices.Get<IDocumentPostingService>();
        await posting.SetSubtypeAsync(TaxDocumentType, envelope.MetaId, "Issued");

        if (sourceType == "SalesRealization")
        {
            await docs.UpdateDocumentAsync(SalesInvoiceType, sourceId,
                new Dictionary<string, object?>
                {
                    ["InvoiceUuid"] = envelope.Uuid,
                    ["ZatcaInvoiceType"] = invoiceType,
                    ["InvoiceHash"] = envelope.InvoiceHash,
                    ["PreviousInvoiceHash"] = envelope.PreviousInvoiceHash,
                    ["QrCode"] = await QrCodeAsync(draft, envelope, legalEntity),
                });
        }

        await EnqueueAsync(envelope, invoiceType, legalEntity);

        var receipt = await ScriptServices.Get<ITaxAuthoritySubmitService>()
            .SubmitDocumentAsync(envelope.MetaId);
        var accepted = receipt.StartsWith("MOCK-OK:", StringComparison.Ordinal);
        var target = !accepted
            ? "Rejected"
            : invoiceType == "Standard" ? "Cleared" : "Reported";
        await posting.SetSubtypeAsync(TaxDocumentType, envelope.MetaId, target);
        return envelope.MetaId;
    }

    private static async Task EnqueueAsync(TaxDocument envelope, string invoiceType, Guid legalEntity)
    {
        var channel = invoiceType == "Standard" ? "fatoora-clearance" : "fatoora-reporting";
        var xml = Convert.ToString(envelope.Payload) ?? string.Empty;
        if (xml.Length == 0) return;

        var (connectionRef, _) = await FindConnectionAsync(legalEntity);

        var body = "{\"uuid\":\"" + envelope.Uuid.ToString()
            + "\",\"invoiceHash\":\"" + (Convert.ToString(envelope.InvoiceHash) ?? string.Empty)
            + "\",\"invoice\":\""
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)) + "\"}";
        try
        {
            var id = await ScriptServices.Get<IOutboundGateway>().EnqueueAsync(new OutboundRequest
            {
                Channel = channel,
                ConnectionRef = connectionRef,
                IdempotencyKey = "zatca:" + envelope.Uuid.ToString("N"),
                Payload = body,
                PayloadContentType = "application/json",
                SourceTable = "TaxDocument",
                SourceRecordId = envelope.MetaId,
            });
            await ScriptServices.Get<IDocumentManager>().UpdateDocumentAsync(
                TaxDocumentType, envelope.MetaId,
                new Dictionary<string, object?> { ["ExternalId"] = id.ToString("N") });
        }
        catch (Exception)
        {
            // Channel missing, or a second connection inside the posting scope.
            // XML stays on Payload; the host can enqueue later.
        }
    }

    private static async Task<string?> CredentialRefAsync(Guid legalEntity)
        => (await FindConnectionAsync(legalEntity)).CredentialRef;

    private static async Task<(string? ConnectionRef, string? CredentialRef)> FindConnectionAsync(Guid legalEntity)
    {
        try
        {
            if (legalEntity == Guid.Empty) return (null, null);
            var rows = await ScriptServices.Get<IDictionaryManager>()
                .GetRecordsAsync<TaxAuthorityConnection>($"LegalEntity = '{legalEntity}'", take: 1);
            if (rows.Count == 0) return (null, null);
            var code = string.IsNullOrWhiteSpace(rows[0].Code) ? null : rows[0].Code;
            var cred = string.IsNullOrWhiteSpace(rows[0].CredentialRef) ? null : rows[0].CredentialRef;
            return (code, cred);
        }
        catch
        {
            return (null, null);
        }
    }

    private sealed class UblDraft
    {
        public string Xml = string.Empty;
        public string SellerName = string.Empty;
        public string VatNumber = string.Empty;
        public string Timestamp = string.Empty;
        public string InvoiceTotal = string.Empty;
        public string VatTotal = string.Empty;
    }

    private static async Task<UblDraft> BuildUblAsync(
        TaxDocument envelope,
        SalesRealization? invoice,
        SalesCreditNote? note,
        Customer? customer,
        string documentType,
        string invoiceType)
    {
        var dict = ScriptServices.Get<IDictionaryManager>();
        var pricing = ScriptServices.Get<IPricingService>();
        var legal = envelope.LegalEntity == Guid.Empty
            ? null
            : await dict.GetRecordAsync<LegalEntity>(envelope.LegalEntity);

        var sellerVat = legal?.TaxRegistrationNumber ?? string.Empty;
        var sellerCrn = string.Empty;
        if (legal != null)
        {
            try
            {
                var bag = await ScriptServices.Get<IDataService>().GetByIdAsync("LegalEntity", legal.MetaId);
                sellerCrn = Convert.ToString(bag?["CommercialRegistration"]) ?? string.Empty;
            }
            catch { /* bag path optional */ }
        }

        Address? sellerAddr = null;
        if (legal != null && legal.LegalAddress != Guid.Empty)
            sellerAddr = await dict.GetRecordAsync<Address>(legal.LegalAddress);
        Address? buyerAddr = null;
        if (customer != null && customer.Address != Guid.Empty)
            buyerAddr = await dict.GetRecordAsync<Address>(customer.Address);

        var countryCode = "SA";
        if (sellerAddr != null && sellerAddr.Country != Guid.Empty)
        {
            var c = await dict.GetRecordAsync<Country>(sellerAddr.Country);
            if (!string.IsNullOrWhiteSpace(c?.CodeISO2)) countryCode = c!.CodeISO2;
        }

        var currencyCode = "SAR";
        if (legal != null && legal.Currency != Guid.Empty)
        {
            var cur = await dict.GetRecordAsync<Currency>(legal.Currency);
            if (!string.IsNullOrWhiteSpace(cur?.Code)) currencyCode = cur!.Code;
        }

        var issue = DateTime.UtcNow;
        var idText = invoice?.ID ?? note?.ID ?? envelope.ID;
        var rate = invoice?.TaxRateApplied ?? note?.TaxRateApplied ?? 0m;
        var discount = invoice?.DiscountPercent ?? 0m;
        var ksaType = invoiceType == "Standard" ? "0100000" : "0200000";
        var ublCode = documentType == "CREDIT_NOTE" ? "381" : "388";
        var root = documentType == "CREDIT_NOTE" ? "CreditNote" : "Invoice";
        var lineTag = documentType == "CREDIT_NOTE" ? "CreditNoteLine" : "InvoiceLine";

        decimal exclusive = 0m;
        var lineXml = new StringBuilder();
        var n = 0;
        if (invoice != null)
        {
            foreach (var line in invoice.Lines)
            {
                n++;
                var amount = pricing.LineAmount(line.Quantity, line.UnitPrice, discount);
                exclusive += amount;
                lineXml.Append(LineXml(lineTag, n, line.Quantity, amount, rate, await ItemNameAsync(dict, line.Item), invoiced: true));
            }
        }
        else if (note != null)
        {
            foreach (var line in note.Lines)
            {
                n++;
                var amount = pricing.LineAmount(line.Quantity, line.UnitPrice);
                exclusive += amount;
                lineXml.Append(LineXml(lineTag, n, line.Quantity, amount, rate, await ItemNameAsync(dict, line.Item), invoiced: false));
            }
        }

        var tax = Math.Round(exclusive * rate, 2, MidpointRounding.AwayFromZero);
        var inclusive = exclusive + tax;
        var sellerScheme = sellerVat.Length > 0 ? "VAT" : "CRN";
        var sellerId = sellerScheme == "VAT" ? sellerVat : sellerCrn;
        var buyerVat = customer?.TaxRegistrationNumber ?? string.Empty;

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append('<').Append(root);
        sb.Append(" xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:").Append(root).Append("-2\"");
        sb.Append(" xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\"");
        sb.Append(" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">");
        sb.Append("<cbc:ProfileID>reporting:1.0</cbc:ProfileID>");
        sb.Append("<cbc:ID>").Append(Esc(idText)).Append("</cbc:ID>");
        sb.Append("<cbc:UUID>").Append(envelope.Uuid.ToString()).Append("</cbc:UUID>");
        sb.Append("<cbc:IssueDate>").Append(issue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</cbc:IssueDate>");
        sb.Append("<cbc:IssueTime>").Append(issue.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("Z</cbc:IssueTime>");
        sb.Append("<cbc:InvoiceTypeCode name=\"").Append(ksaType).Append("\">").Append(ublCode).Append("</cbc:InvoiceTypeCode>");
        sb.Append("<cbc:DocumentCurrencyCode>").Append(Esc(currencyCode)).Append("</cbc:DocumentCurrencyCode>");
        sb.Append("<cac:AdditionalDocumentReference><cbc:ID>ICV</cbc:ID><cbc:UUID>");
        sb.Append(envelope.InvoiceCounter.ToString(CultureInfo.InvariantCulture));
        sb.Append("</cbc:UUID></cac:AdditionalDocumentReference>");
        sb.Append("<cac:AdditionalDocumentReference><cbc:ID>PIH</cbc:ID><cac:Attachment>");
        sb.Append("<cbc:EmbeddedDocumentBinaryObject mimeCode=\"text/plain\">");
        sb.Append(Esc(envelope.PreviousInvoiceHash));
        sb.Append("</cbc:EmbeddedDocumentBinaryObject></cac:Attachment></cac:AdditionalDocumentReference>");
        sb.Append(PartyXml("AccountingSupplierParty", legal?.Name ?? string.Empty, sellerId, sellerScheme, sellerAddr, countryCode, sellerVat, sellerCrn));
        sb.Append(PartyXml("AccountingCustomerParty", customer?.Name ?? string.Empty, buyerVat, "VAT", buyerAddr, countryCode, buyerVat, string.Empty));
        sb.Append("<cac:TaxTotal><cbc:TaxAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">");
        sb.Append(Money(tax)).Append("</cbc:TaxAmount></cac:TaxTotal>");
        sb.Append("<cac:LegalMonetaryTotal>");
        sb.Append("<cbc:LineExtensionAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">").Append(Money(exclusive)).Append("</cbc:LineExtensionAmount>");
        sb.Append("<cbc:TaxExclusiveAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">").Append(Money(exclusive)).Append("</cbc:TaxExclusiveAmount>");
        sb.Append("<cbc:TaxInclusiveAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">").Append(Money(inclusive)).Append("</cbc:TaxInclusiveAmount>");
        sb.Append("<cbc:PayableAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">").Append(Money(inclusive)).Append("</cbc:PayableAmount>");
        sb.Append("</cac:LegalMonetaryTotal>");
        sb.Append(lineXml);
        sb.Append("</").Append(root).Append('>');
        return new UblDraft
        {
            Xml = sb.ToString(),
            SellerName = legal?.Name ?? string.Empty,
            VatNumber = sellerVat,
            Timestamp = issue.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            InvoiceTotal = Money(inclusive),
            VatTotal = Money(tax),
        };
    }

    private static string PartyXml(
        string tag, string name, string partyId, string scheme,
        Address? addr, string countryCode, string vat, string crn)
    {
        var sb = new StringBuilder();
        sb.Append("<cac:").Append(tag).Append("><cac:Party>");
        if (partyId.Length > 0)
        {
            sb.Append("<cac:PartyIdentification><cbc:ID schemeID=\"").Append(Esc(scheme)).Append("\">");
            sb.Append(Esc(partyId)).Append("</cbc:ID></cac:PartyIdentification>");
        }
        if (addr != null)
        {
            sb.Append("<cac:PostalAddress>");
            sb.Append("<cbc:StreetName>").Append(Esc(addr.Street)).Append("</cbc:StreetName>");
            sb.Append("<cbc:BuildingNumber>").Append(Esc(addr.Building)).Append("</cbc:BuildingNumber>");
            sb.Append("<cbc:CitySubdivisionName>").Append(Esc(addr.District)).Append("</cbc:CitySubdivisionName>");
            sb.Append("<cbc:CityName>").Append(Esc(addr.Name)).Append("</cbc:CityName>");
            sb.Append("<cbc:PostalZone>").Append(Esc(addr.PostalCode)).Append("</cbc:PostalZone>");
            sb.Append("<cac:Country><cbc:IdentificationCode>").Append(Esc(countryCode)).Append("</cbc:IdentificationCode></cac:Country>");
            sb.Append("</cac:PostalAddress>");
        }
        if (vat.Length > 0)
        {
            sb.Append("<cac:PartyTaxScheme><cbc:CompanyID>").Append(Esc(vat)).Append("</cbc:CompanyID>");
            sb.Append("<cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:PartyTaxScheme>");
        }
        else if (crn.Length > 0 && tag == "AccountingSupplierParty")
        {
            sb.Append("<cac:PartyTaxScheme><cbc:CompanyID>").Append(Esc(crn)).Append("</cbc:CompanyID>");
            sb.Append("<cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:PartyTaxScheme>");
        }
        sb.Append("<cac:PartyLegalEntity><cbc:RegistrationName>").Append(Esc(name)).Append("</cbc:RegistrationName></cac:PartyLegalEntity>");
        sb.Append("</cac:Party></cac:").Append(tag).Append('>');
        return sb.ToString();
    }

    private static string LineXml(string tag, int n, decimal qty, decimal amount, decimal rate, string itemName, bool invoiced)
    {
        var tax = Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
        var percent = Math.Round(rate * 100m, 2, MidpointRounding.AwayFromZero);
        var qtyTag = invoiced ? "InvoicedQuantity" : "CreditedQuantity";
        var sb = new StringBuilder();
        sb.Append("<cac:").Append(tag).Append('>');
        sb.Append("<cbc:ID>").Append(n.ToString(CultureInfo.InvariantCulture)).Append("</cbc:ID>");
        sb.Append("<cbc:").Append(qtyTag).Append(" unitCode=\"PCE\">").Append(Money(qty)).Append("</cbc:").Append(qtyTag).Append('>');
        sb.Append("<cbc:LineExtensionAmount currencyID=\"SAR\">").Append(Money(amount)).Append("</cbc:LineExtensionAmount>");
        sb.Append("<cac:TaxTotal><cbc:TaxAmount currencyID=\"SAR\">").Append(Money(tax)).Append("</cbc:TaxAmount></cac:TaxTotal>");
        sb.Append("<cac:Item><cbc:Name>").Append(Esc(itemName)).Append("</cbc:Name>");
        sb.Append("<cac:ClassifiedTaxCategory><cbc:ID>S</cbc:ID><cbc:Percent>").Append(Money(percent)).Append("</cbc:Percent>");
        sb.Append("<cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:ClassifiedTaxCategory></cac:Item>");
        sb.Append("<cac:Price><cbc:PriceAmount currencyID=\"SAR\">").Append(Money(qty == 0m ? 0m : amount / qty)).Append("</cbc:PriceAmount></cac:Price>");
        sb.Append("</cac:").Append(tag).Append('>');
        return sb.ToString();
    }

    /// <summary>
    /// ZATCA invoice hash: Base64(SHA-256(UTF-8)). Empty input yields the
    /// published first PIH from the XML Implementation Standard, where the
    /// chain starts.
    /// </summary>
    private static string Sha256Base64(string? value)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    /// <summary>
    /// The QR the invoice carries. The host signs the hash — the PEM never
    /// comes here — and the payload is assembled by this model's own ZatcaQr,
    /// because TLV tags and the 500-character cap are Saudi tax format, not
    /// something a jurisdiction-neutral kernel should own. No certificate
    /// configured means tags 1-6 and a QR that is still valid for Phase 2
    /// reporting.
    /// </summary>
    private static async Task<string> QrCodeAsync(
        UblDraft draft, TaxDocument envelope, Guid legalEntity)
    {
        var hash = Convert.ToString(envelope.InvoiceHash) ?? string.Empty;
        var qr = ScriptServices.Get<IZatcaQr>();
        var credential = await CredentialRefAsync(legalEntity);
        var stamp = ScriptServices.Get<ICredentialSigner>()
            .SignHash(string.IsNullOrWhiteSpace(credential) ? DefaultCsidCredential : credential, hash);
        return stamp == null
            ? qr.Encode(draft.SellerName, draft.VatNumber, draft.Timestamp,
                        draft.InvoiceTotal, draft.VatTotal, hash)
            : qr.EncodeStamped(draft.SellerName, draft.VatNumber, draft.Timestamp,
                               draft.InvoiceTotal, draft.VatTotal, hash,
                               Convert.ToBase64String(stamp.Signature),
                               stamp.PublicKeyDer, stamp.CertificateSignature);
    }

    private static async Task<string> ItemNameAsync(IDictionaryManager dict, Guid itemId)
    {
        if (itemId == Guid.Empty) return "Item";
        var item = await dict.GetRecordAsync<Item>(itemId);
        return string.IsNullOrWhiteSpace(item?.Name) ? "Item" : item!.Name;
    }

    private static string Money(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Esc(string? value)
        => (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static async Task<string?> PreviousHashAsync(IDocumentManager docs, Guid legalEntity)
    {
        if (legalEntity == Guid.Empty) return null;
        var prior = await docs.QueryDocumentsAsync<TaxDocument>($"LegalEntity = '{legalEntity}'");
        TaxDocument? best = null;
        foreach (var row in prior)
        {
            if (best == null || row.InvoiceCounter > best.InvoiceCounter)
                best = row;
        }
        if (best == null) return null;
        return string.IsNullOrWhiteSpace(best.InvoiceHash) ? best.PreviousInvoiceHash : best.InvoiceHash;
    }

    private static async Task<int> NextInvoiceCounterAsync(IDocumentManager docs, Guid legalEntity)
    {
        if (legalEntity == Guid.Empty)
            return 1;
        var prior = await docs.QueryDocumentsAsync<TaxDocument>($"LegalEntity = '{legalEntity}'");
        var max = 0;
        foreach (var row in prior)
        {
            if (row.InvoiceCounter > max)
                max = row.InvoiceCounter;
        }
        return max + 1;
    }
}
