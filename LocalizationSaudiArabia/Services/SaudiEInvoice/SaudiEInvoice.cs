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
// The invoice hash is IZatcaXades.InvoiceHash: C14N 1.0 minus UBLExtensions,
// cac:Signature and the QR reference. SHA-256 itself is in the script
// reference set; System.Xml is not, so C14N stays on the host. What the host
// also owns is the KEY — ICredentialSigner answers with the signature and the
// public half. No PEM in this script, and none possible: a script cannot
// resolve ICredentialResolver. QR (always) and XAdES (when CertificateDer is
// present) are sealed into Payload after the hash, so hashing the sealed XML
// yields the same InvoiceHash.
//
// Channels Enabled=false: row is written, nothing is posted to Fatoora.
public partial class SaudiEInvoice
{
    private static readonly Guid TaxDocumentType = Guid.Parse("cee833d7-fa4c-43fe-bef6-e95633ece906");
    private static readonly Guid SalesInvoiceType = Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3");
    private static readonly Guid SalesCreditNoteType = Guid.Parse("f53f1b8a-8458-4f27-ab5e-b75f7228d263");
    private static readonly Guid SalesDebitNoteType = Guid.Parse("465e8b08-5939-4c30-99ea-fef5a4fbc44a");
    private static readonly string FirstPih = Sha256Base64("");
    private static readonly string[] FatooraChannels = { "fatoora-clearance", "fatoora-reporting" };

    /// <summary>
    /// Credential holding the CSID PEM when a legal entity names none of its
    /// own. The DEFAULT moved here with the rest: ICredentialSigner is
    /// country-neutral and must not know that "fatoora-csid" is the usual name
    /// in Saudi Arabia.
    /// </summary>
    private const string DefaultCsidCredential = "fatoora-csid";
    private const string IcvCounter = "zatca-icv";

    public Task<Guid?> EnsureForInvoiceAsync(Guid invoiceId)
        => EnsureAsync(invoiceId, "SalesRealization", "INVOICE");

    public Task<Guid?> EnsureForCreditNoteAsync(Guid creditNoteId)
        => EnsureAsync(creditNoteId, "SalesCreditNote", "CREDIT_NOTE");

    public Task<Guid?> EnsureForDebitNoteAsync(Guid debitNoteId)
        => EnsureAsync(debitNoteId, "SalesDebitNote", "DEBIT_NOTE");

    /// <summary>
    /// Null — счёт/кредит-/дебет-ноту можно отдать покупателю. Иначе причина,
    /// почему нельзя: Standard ждёт Cleared. Simplified отдаётся сразу
    /// (reporting постфактум). Флаг выключен или конверта нет — не
    /// блокируем: это не контур ZATCA.
    /// </summary>
    public async Task<string?> BuyerReleaseBlockAsync(Guid sourceId)
    {
        if (sourceId == Guid.Empty) return null;
        var dict = ScriptServices.Get<IDictionaryManager>();
        var settingsRows = await dict.GetRecordsAsync<LocalizationSaudiArabiaSettings>(null, 1);
        if (settingsRows.Count == 0 || !settingsRows[0].EInvoiceEnabled)
            return null;

        var envelopes = await ScriptServices.Get<IDocumentManager>()
            .QueryDocumentsAsync<TaxDocument>($"SourceDocumentId = '{sourceId}'");
        if (envelopes.Count == 0) return null;

        var envelope = envelopes[0];
        if (!string.Equals(envelope.InvoiceType, "Standard", StringComparison.Ordinal))
            return null;
        if (string.Equals(envelope.Subtype, "Cleared", StringComparison.Ordinal))
            return null;
        return "Standard e-invoice cannot be given to the buyer until TaxDocument is Cleared";
    }

    private async Task<Guid?> EnsureAsync(Guid sourceId, string sourceType, string documentType)
    {
        var dict = ScriptServices.Get<IDictionaryManager>();
        var settingsRows = await dict.GetRecordsAsync<LocalizationSaudiArabiaSettings>(null, 1);
        if (settingsRows.Count == 0 || !settingsRows[0].EInvoiceEnabled)
            return null;

        try { await ApplyChannelOutcomesAsync(); }
        catch (Exception ex)
        {
            ScriptServices.Get<ZuloOne.Log.IZuloOneLogger<SaudiEInvoice>>()
                .Warn(ex, "ApplyChannelOutcomes before envelope {0}", sourceId);
        }

        var docs = ScriptServices.Get<IDocumentManager>();
        var existing = await docs.QueryDocumentsAsync<TaxDocument>($"SourceDocumentId = '{sourceId}'");
        if (existing.Count > 0)
            return existing[0].MetaId;

        Guid customerId;
        Guid legalEntity;
        SalesRealization? invoice = null;
        SalesCreditNote? note = null;
        SalesDebitNote? debit = null;
        if (sourceType == "SalesRealization")
        {
            invoice = await docs.GetDocumentAsync<SalesRealization>(sourceId);
            if (invoice is null) return null;
            customerId = invoice.Customer;
            legalEntity = invoice.LegalEntity;
        }
        else if (sourceType == "SalesCreditNote")
        {
            note = await docs.GetDocumentAsync<SalesCreditNote>(sourceId);
            if (note is null) return null;
            customerId = note.Customer;
            legalEntity = note.LegalEntity;
        }
        else
        {
            debit = await docs.GetDocumentAsync<SalesDebitNote>(sourceId);
            if (debit is null) return null;
            customerId = debit.Customer;
            legalEntity = debit.LegalEntity;
        }

        var customer = customerId == Guid.Empty
            ? null
            : await ScriptServices.Get<IDictionaryManager<Customer>>().GetRecordAsync(customerId);
        var invoiceType = !string.IsNullOrWhiteSpace(customer?.TaxRegistrationNumber)
            ? "Standard"
            : "Simplified";

        var take = legalEntity == Guid.Empty
            ? new KeyedCounterTake(1, null)
            : await ScriptServices.Get<IKeyedCounterService>().TakeAsync(IcvCounter, legalEntity);

        var envelope = await docs.NewDocumentAsync<TaxDocument>();
        envelope.LegalEntity = legalEntity;
        envelope.SourceDocumentType = sourceType;
        envelope.SourceDocumentId = sourceId;
        envelope.EInvoiceKind = documentType;
        envelope.InvoiceType = invoiceType;
        envelope.Uuid = Guid.NewGuid();
        envelope.InvoiceCounter = (int)take.Value;
        envelope.PreviousInvoiceHash = take.PreviousToken ?? FirstPih;
        var draft = await BuildUblAsync(envelope, invoice, note, debit, customer, documentType, invoiceType);
        var xades = ScriptServices.Get<IZatcaXades>();
        envelope.InvoiceHash = xades.InvoiceHash(draft.Xml);
        var (qr, stamp, cred) = await StampAsync(draft, envelope, legalEntity);
        var signingTime = DateTime.UtcNow;
        string? siSig = null;
        string? certDer = null;
        if (stamp?.CertificateDer is { Length: > 0 })
        {
            var digest = xades.SignedInfoDigest(
                envelope.InvoiceHash, Convert.ToBase64String(stamp.CertificateDer), signingTime);
            var siStamp = ScriptServices.Get<ICredentialSigner>().SignHash(cred, digest);
            if (siStamp != null)
            {
                siSig = Convert.ToBase64String(siStamp.Signature);
                certDer = Convert.ToBase64String(stamp.CertificateDer);
            }
        }
        envelope.Payload = xades.Seal(draft.Xml, qr, siSig, certDer, signingTime);
        await docs.SaveDocumentAsync(envelope);
        if (legalEntity != Guid.Empty)
            await ScriptServices.Get<IKeyedCounterService>().SetTokenAsync(IcvCounter, legalEntity, envelope.InvoiceHash);

        var posting = ScriptServices.Get<IDocumentPostingService>();
        await posting.SetSubtypeAsync(TaxDocumentType, envelope.MetaId, "Issued");

        await StampSourceAsync(docs, sourceType, sourceId, envelope, qr, invoiceType);
        await EnqueueAsync(envelope, invoiceType, legalEntity);

        // Cleared/Reported come from the outbound channel, not from the mock.
        // IsMockReject stays a stand knob that rejects without waiting for Fatoora.
        if (await IsMockRejectAsync(legalEntity))
        {
            await ScriptServices.Get<ITaxAuthoritySubmitService>()
                .SubmitDocumentAsync(envelope.MetaId);
            await posting.SetSubtypeAsync(TaxDocumentType, envelope.MetaId, "Rejected");
        }
        return envelope.MetaId;
    }

    /// <summary>
    /// Copies finished Fatoora outcomes onto TaxDocument. The job
    /// ApplyFatooraOutcomes runs this every minute; Ensure also piggybacks it
    /// at the start of the next posting. Enqueue only writes a row — the sender
    /// runs outside this transaction.
    ///
    /// What lands on the envelope is NOT decided here. Saudi knows which channel
    /// means cleared and which means reported, and that is the whole of its
    /// country knowledge; stamping the authority, the rejection reason, the
    /// subtype and the journal row belongs to Tax, which owns TaxDocument.
    /// While every pack did that itself, Authority and RejectionReason were
    /// written by nobody at all.
    /// </summary>
    public async Task<int> ApplyChannelOutcomesAsync()
    {
        var gateway = ScriptServices.Get<IOutboundGateway>();
        var outcome = ScriptServices.Get<ITaxDocumentOutcome>();
        var applied = 0;
        foreach (var channel in FatooraChannels)
        {
            var batch = await gateway.TakeCompletedAsync(channel, 100);
            var ack = new List<Guid>();
            foreach (var status in batch)
            {
                var target = TargetFromChannel(status);
                // InDoubt deliberately stays unacknowledged: the request reached
                // the wire and no answer came back, so it is re-offered until a
                // human resolves it. Acking it here would bury it.
                if (target == null)
                    continue;

                if (status.SourceRecordId is Guid id && id != Guid.Empty
                    && await outcome.ApplyAsync(
                        id, target,
                        status.ExternalReference ?? status.MessageId.ToString("N"),
                        status.LastError ?? status.Status))
                    applied++;

                ack.Add(status.MessageId);
            }
            if (ack.Count > 0)
                await gateway.AcknowledgeAsync(ack);
        }
        return applied;
    }

    private static string? TargetFromChannel(OutboundStatus status)
    {
        if (string.Equals(status.Status, "Succeeded", StringComparison.Ordinal))
            return string.Equals(status.Channel, "fatoora-clearance", StringComparison.Ordinal)
                ? "Cleared"
                : "Reported";
        if (status.Status is "Failed" or "Abandoned" or "Cancelled")
            return "Rejected";
        return null;
    }

    private static async Task StampSourceAsync(
        IDocumentManager docs, string sourceType, Guid sourceId,
        TaxDocument envelope, string qr, string invoiceType)
    {
        var typeId = sourceType == "SalesRealization" ? SalesInvoiceType
            : sourceType == "SalesCreditNote" ? SalesCreditNoteType
            : sourceType == "SalesDebitNote" ? SalesDebitNoteType
            : Guid.Empty;
        if (typeId == Guid.Empty) return;
        await docs.UpdateDocumentAsync(typeId, sourceId,
            new Dictionary<string, object?>
            {
                ["InvoiceUuid"] = envelope.Uuid,
                ["ZatcaInvoiceType"] = invoiceType,
                ["InvoiceHash"] = envelope.InvoiceHash,
                ["PreviousInvoiceHash"] = envelope.PreviousInvoiceHash,
                ["QrCode"] = qr,
            });
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
        catch (Exception ex)
        {
            // Channel missing, or a second connection inside the posting scope
            // (MSDTC). XML stays on Payload; ApplyChannelOutcomesAsync / a later
            // enqueue can still send it.
            ScriptServices.Get<ZuloOne.Log.IZuloOneLogger<SaudiEInvoice>>()
                .Warn(ex, "ZATCA enqueue failed for TaxDocument {0}", envelope.MetaId);
        }
    }

    private static async Task<string?> CredentialRefAsync(Guid legalEntity)
        => (await FindConnectionAsync(legalEntity)).CredentialRef;

    private static async Task<bool> IsMockRejectAsync(Guid legalEntity)
    {
        try
        {
            if (legalEntity == Guid.Empty) return false;
            var rows = await ScriptServices.Get<IDictionaryManager>()
                .GetRecordsAsync<TaxAuthorityConnection>($"LegalEntity = '{legalEntity}'", take: 1);
            return rows.Count > 0 && rows[0].IsMockReject;
        }
        catch
        {
            return false;
        }
    }

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
        SalesDebitNote? debit,
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

        // Resolved PER PARTY. The seller's country used to be handed to both
        // parties, so an export invoice always claimed the buyer was in SA —
        // and a wrong buyer country on an export invoice is grounds for an
        // assessment.
        var seller = await PlaceAsync(dict, sellerAddr);
        var buyer = await PlaceAsync(dict, buyerAddr);

        var currencyCode = "SAR";
        if (legal != null && legal.Currency != Guid.Empty)
        {
            var cur = await dict.GetRecordAsync<Currency>(legal.Currency);
            if (!string.IsNullOrWhiteSpace(cur?.Code)) currencyCode = cur!.Code;
        }

        // The issue date is the DOCUMENT's date, not the moment of posting. The
        // printed form has always shown DocumentDate, so UtcNow here meant a
        // backdated invoice carried one date on paper and another in the XML
        // and in QR tag 3 — and the VAT period followed the document while the
        // e-invoice followed the clock.
        //
        // The TIME has no equivalent on the document (DocumentDate is a day),
        // and ZATCA wants IssueTime. Where the document carries no time of day,
        // the posting moment fills it: the date — the part that is printed,
        // reconciled and visible in the QR — matches, and the unprinted time
        // stays truthful about when the envelope was produced.
        var documentDate = invoice?.DocumentDate ?? note?.DocumentDate ?? debit?.DocumentDate ?? DateTime.UtcNow;
        var issue = documentDate.TimeOfDay == TimeSpan.Zero
            ? documentDate.Date + DateTime.UtcNow.TimeOfDay
            : documentDate;
        var idText = invoice?.ID ?? note?.ID ?? debit?.ID ?? envelope.ID;
        var rate = invoice?.TaxRateApplied ?? note?.TaxRateApplied ?? debit?.TaxRateApplied ?? 0m;
        var discount = invoice?.DiscountPercent ?? 0m;
        var ksaType = invoiceType == "Standard" ? "0100000" : "0200000";
        var ublCode = documentType == "CREDIT_NOTE" ? "381"
            : documentType == "DEBIT_NOTE" ? "383" : "388";
        var root = documentType == "CREDIT_NOTE" ? "CreditNote"
            : documentType == "DEBIT_NOTE" ? "DebitNote" : "Invoice";
        var lineTag = documentType == "CREDIT_NOTE" ? "CreditNoteLine"
            : documentType == "DEBIT_NOTE" ? "DebitNoteLine" : "InvoiceLine";
        var qtyTag = documentType == "CREDIT_NOTE" ? "CreditedQuantity"
            : documentType == "DEBIT_NOTE" ? "DebitedQuantity" : "InvoicedQuantity";
        var sourceId = invoice?.MetaId ?? note?.MetaId ?? debit?.MetaId ?? Guid.Empty;
        var letter = await ResolveVatLetterAsync(sourceId, rate);

        decimal exclusive = 0m;
        // The header total is ACCUMULATED FROM THE LINES rather than computed
        // from the sum. Rounding at two levels does not agree: three lines of
        // 0.10 at 15% round to 0.02 each — 0.06 — while the header rounded
        // 0.30 x 0.15 to 0.05. The XML then carried a TaxTotal that no sum of
        // its own lines produces, and the printed form showed both numbers.
        decimal tax = 0m;
        var lineXml = new StringBuilder();
        var n = 0;
        if (invoice != null)
        {
            foreach (var line in invoice.Lines)
            {
                n++;
                var amount = pricing.LineAmount(line.Quantity, line.UnitPrice, discount);
                exclusive += amount;
                tax += LineTax(amount, rate);
                lineXml.Append(LineXml(lineTag, n, line.Quantity, amount, rate, await ItemNameAsync(dict, line.Item), currencyCode, qtyTag, letter));
            }
        }
        else if (note != null)
        {
            foreach (var line in note.Lines)
            {
                n++;
                var amount = pricing.LineAmount(line.Quantity, line.UnitPrice);
                exclusive += amount;
                tax += LineTax(amount, rate);
                lineXml.Append(LineXml(lineTag, n, line.Quantity, amount, rate, await ItemNameAsync(dict, line.Item), currencyCode, qtyTag, letter));
            }
        }
        else if (debit != null)
        {
            foreach (var line in debit.Lines)
            {
                n++;
                var amount = pricing.LineAmount(line.Quantity, line.UnitPrice);
                exclusive += amount;
                tax += LineTax(amount, rate);
                lineXml.Append(LineXml(lineTag, n, line.Quantity, amount, rate, await ItemNameAsync(dict, line.Item), currencyCode, qtyTag, letter));
            }
        }

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
        var originalId = note?.OriginalInvoice ?? debit?.OriginalInvoice ?? Guid.Empty;
        sb.Append(await NoteReferenceXmlAsync(documentType, originalId));
        sb.Append("<cac:AdditionalDocumentReference><cbc:ID>ICV</cbc:ID><cbc:UUID>");
        sb.Append(envelope.InvoiceCounter.ToString(CultureInfo.InvariantCulture));
        sb.Append("</cbc:UUID></cac:AdditionalDocumentReference>");
        sb.Append("<cac:AdditionalDocumentReference><cbc:ID>PIH</cbc:ID><cac:Attachment>");
        sb.Append("<cbc:EmbeddedDocumentBinaryObject mimeCode=\"text/plain\">");
        sb.Append(Esc(envelope.PreviousInvoiceHash));
        sb.Append("</cbc:EmbeddedDocumentBinaryObject></cac:Attachment></cac:AdditionalDocumentReference>");
        sb.Append(PartyXml("AccountingSupplierParty", legal?.Name ?? string.Empty, sellerId, sellerScheme, sellerAddr, seller, sellerVat, sellerCrn));
        sb.Append(PartyXml("AccountingCustomerParty", customer?.Name ?? string.Empty, buyerVat, "VAT", buyerAddr, buyer, buyerVat, string.Empty));
        var percent = Math.Round(rate * 100m, 2, MidpointRounding.AwayFromZero);
        sb.Append("<cac:TaxTotal><cbc:TaxAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">");
        sb.Append(Money(tax)).Append("</cbc:TaxAmount>");
        if (letter.Id is "E" or "O")
        {
            sb.Append("<cac:TaxSubtotal>");
            sb.Append("<cbc:TaxableAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">").Append(Money(exclusive)).Append("</cbc:TaxableAmount>");
            sb.Append("<cbc:TaxAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">").Append(Money(tax)).Append("</cbc:TaxAmount>");
            sb.Append(TaxCategoryXml(letter, percent, classified: false));
            sb.Append("</cac:TaxSubtotal>");
        }
        sb.Append("</cac:TaxTotal>");
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

    /// <summary>Country code and city NAME of one address, resolved once.</summary>
    private readonly struct Place
    {
        public Place(string countryCode, string city) { CountryCode = countryCode; City = city; }
        public string CountryCode { get; }
        public string City { get; }
    }

    /// <summary>
    /// Resolves an address into the two values UBL needs by NAME.
    ///
    /// <para><c>cbc:CityName</c> used to receive <c>Address.Name</c> — the
    /// address LINE, so "Olaya HQ" travelled where "Riyadh" belongs. The
    /// address carries a City reference; it was simply never read. The printed
    /// form already resolved it and said so in a comment, which is how a bug
    /// gets documented in one file and left alone in another.</para>
    /// </summary>
    private static async Task<Place> PlaceAsync(IDictionaryManager dict, Address? addr)
    {
        if (addr == null) return new Place("SA", string.Empty);

        var countryCode = "SA";
        if (addr.Country != Guid.Empty)
        {
            var c = await dict.GetRecordAsync<Country>(addr.Country);
            if (!string.IsNullOrWhiteSpace(c?.CodeISO2)) countryCode = c!.CodeISO2;
        }

        var city = string.Empty;
        if (addr.City != Guid.Empty)
        {
            var c = await dict.GetRecordAsync<City>(addr.City);
            city = c?.Name ?? string.Empty;
        }
        return new Place(countryCode, city);
    }

    /// <summary>
    /// Credit/debit notes must point at the original invoice. Same shape as
    /// IZatcaComplianceSamples: BillingReference then PaymentMeans with an
    /// InstructionNote. The original UUID is the one SaudiEInvoice stamped on
    /// the invoice row, not the note's own envelope UUID.
    /// </summary>
    private static async Task<string> NoteReferenceXmlAsync(string documentType, Guid originalId)
    {
        if (documentType is not ("CREDIT_NOTE" or "DEBIT_NOTE"))
            return string.Empty;

        var sb = new StringBuilder();
        if (originalId != Guid.Empty)
        {
            var original = await ScriptServices.Get<IDocumentManager>()
                .GetDocumentAsync<SalesRealization>(originalId);
            if (original != null)
            {
                sb.Append("<cac:BillingReference><cac:InvoiceDocumentReference>");
                sb.Append("<cbc:ID>").Append(Esc(original.ID)).Append("</cbc:ID>");
                try
                {
                    var bag = await ScriptServices.Get<IDataService>()
                        .GetByIdAsync("SalesRealization", original.MetaId);
                    var uuidText = Convert.ToString(bag?["InvoiceUuid"]) ?? string.Empty;
                    if (Guid.TryParse(uuidText, out var uuid) && uuid != Guid.Empty)
                        sb.Append("<cbc:UUID>").Append(uuid.ToString()).Append("</cbc:UUID>");
                }
                catch { /* bag path optional */ }
                sb.Append("</cac:InvoiceDocumentReference></cac:BillingReference>");
            }
        }

        var reason = documentType == "CREDIT_NOTE" ? "CANCELLATION" : "ADDITIONAL_CHARGES";
        sb.Append("<cac:PaymentMeans><cbc:PaymentMeansCode>10</cbc:PaymentMeansCode>");
        sb.Append("<cbc:InstructionNote>").Append(reason).Append("</cbc:InstructionNote></cac:PaymentMeans>");
        return sb.ToString();
    }

    private static string PartyXml(
        string tag, string name, string partyId, string scheme,
        Address? addr, Place place, string vat, string crn)
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
            sb.Append("<cbc:CityName>").Append(Esc(place.City)).Append("</cbc:CityName>");
            sb.Append("<cbc:PostalZone>").Append(Esc(addr.PostalCode)).Append("</cbc:PostalZone>");
            sb.Append("<cac:Country><cbc:IdentificationCode>").Append(Esc(place.CountryCode)).Append("</cbc:IdentificationCode></cac:Country>");
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

    /// <summary>
    /// Tax of ONE line. The single place it is computed, so the header total
    /// and the line amounts cannot drift apart.
    /// </summary>
    private static decimal LineTax(decimal amount, decimal rate)
        => Math.Round(amount * rate, Scale, MidpointRounding.AwayFromZero);

    private static string LineXml(
        string tag, int n, decimal qty, decimal amount, decimal rate,
        string itemName, string currencyCode, string qtyTag, VatLetter letter)
    {
        var tax = LineTax(amount, rate);
        var percent = Math.Round(rate * 100m, 2, MidpointRounding.AwayFromZero);
        var sb = new StringBuilder();
        sb.Append("<cac:").Append(tag).Append('>');
        sb.Append("<cbc:ID>").Append(n.ToString(CultureInfo.InvariantCulture)).Append("</cbc:ID>");
        sb.Append("<cbc:").Append(qtyTag).Append(" unitCode=\"PCE\">").Append(Plain(qty)).Append("</cbc:").Append(qtyTag).Append('>');
        sb.Append("<cbc:LineExtensionAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">").Append(Money(amount)).Append("</cbc:LineExtensionAmount>");
        sb.Append("<cac:TaxTotal><cbc:TaxAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">").Append(Money(tax)).Append("</cbc:TaxAmount></cac:TaxTotal>");
        sb.Append("<cac:Item><cbc:Name>").Append(Esc(itemName)).Append("</cbc:Name>");
        sb.Append(TaxCategoryXml(letter, percent, classified: true));
        sb.Append("</cac:Item>");
        sb.Append("<cac:Price><cbc:PriceAmount currencyID=\"").Append(Esc(currencyCode)).Append("\">").Append(Money(qty == 0m ? 0m : amount / qty)).Append("</cbc:PriceAmount></cac:Price>");
        sb.Append("</cac:").Append(tag).Append('>');
        return sb.ToString();
    }

    private sealed class VatLetter
    {
        public string Id = "Z";
        public string ReasonCode = string.Empty;
        public string ReasonText = string.Empty;
    }

    /// <summary>
    /// Letter from the posted TaxCalculation's category. No calc (no tax
    /// circuit) — S if the stamped rate is positive, Z if it is zero, same
    /// as before this slice. E/O then read the SA exemption fields.
    /// </summary>
    private static async Task<VatLetter> ResolveVatLetterAsync(Guid sourceId, decimal rate)
    {
        var letter = new VatLetter { Id = rate > 0m ? "S" : "Z" };
        if (sourceId == Guid.Empty) return letter;

        var docs = ScriptServices.Get<IDocumentManager>();
        foreach (var child in await docs.GetDocumentChildrenAsync(sourceId))
        {
            var calc = await docs.GetDocumentAsync<TaxCalculation>(child);
            if (calc == null || calc.Lines.Count == 0) continue;
            var catId = calc.Lines[0].TaxCategory;
            if (catId == Guid.Empty) continue;
            var cat = await ScriptServices.Get<IDictionaryManager<TaxCategory>>().GetRecordAsync(catId);
            if (cat == null) continue;
            letter.Id = LetterOf(cat.Treatment, rate);
            if (letter.Id is "E" or "O")
            {
                try
                {
                    var bag = await ScriptServices.Get<IDataService>().GetByIdAsync("TaxCategory", catId);
                    letter.ReasonCode = Convert.ToString(bag?["ExemptionReasonCode"]) ?? string.Empty;
                    letter.ReasonText = Convert.ToString(bag?["ExemptionReasonText"]) ?? string.Empty;
                }
                catch { /* bag path optional */ }
            }
            return letter;
        }
        return letter;
    }

    private static string LetterOf(string? treatment, decimal rate)
    {
        if (string.Equals(treatment, "EXEMPT", StringComparison.OrdinalIgnoreCase)) return "E";
        if (string.Equals(treatment, "OUT_OF_SCOPE", StringComparison.OrdinalIgnoreCase)) return "O";
        if (string.Equals(treatment, "ZERO_RATED", StringComparison.OrdinalIgnoreCase)) return "Z";
        if (string.Equals(treatment, "STANDARD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment, "REVERSE_CHARGE", StringComparison.OrdinalIgnoreCase))
            return "S";
        return rate > 0m ? "S" : "Z";
    }

    private static string TaxCategoryXml(VatLetter letter, decimal percent, bool classified)
    {
        var tag = classified ? "ClassifiedTaxCategory" : "TaxCategory";
        var sb = new StringBuilder();
        sb.Append("<cac:").Append(tag).Append('>');
        sb.Append("<cbc:ID>").Append(Esc(letter.Id)).Append("</cbc:ID>");
        sb.Append("<cbc:Percent>").Append(Plain(percent)).Append("</cbc:Percent>");
        if (letter.Id is "E" or "O")
        {
            if (letter.ReasonCode.Length > 0)
                sb.Append("<cbc:TaxExemptionReasonCode>").Append(Esc(letter.ReasonCode)).Append("</cbc:TaxExemptionReasonCode>");
            if (letter.ReasonText.Length > 0)
                sb.Append("<cbc:TaxExemptionReason>").Append(Esc(letter.ReasonText)).Append("</cbc:TaxExemptionReason>");
        }
        sb.Append("<cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme>");
        sb.Append("</cac:").Append(tag).Append('>');
        return sb.ToString();
    }

    /// <summary>
    /// First PIH: Base64(SHA-256(UTF-8 of empty)). The invoice hash itself is
    /// C14N, not this — <see cref="IZatcaXades.InvoiceHash"/>.
    /// </summary>
    private static string Sha256Base64(string? value)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    /// <summary>
    /// The QR the invoice carries, plus the CSID stamp if the host could sign.
    /// The PEM never comes here. No certificate configured means tags 1-6.
    /// Tag 7 is ECDSA of the invoice hash; XAdES SignatureValue is a second
    /// SignHash, over SignedInfo, done by the caller.
    /// </summary>
    private static async Task<(string Qr, CredentialSignature? Stamp, string Credential)> StampAsync(
        UblDraft draft, TaxDocument envelope, Guid legalEntity)
    {
        var hash = Convert.ToString(envelope.InvoiceHash) ?? string.Empty;
        var qr = ScriptServices.Get<IZatcaQr>();
        var credential = await CredentialRefAsync(legalEntity);
        var cred = string.IsNullOrWhiteSpace(credential) ? DefaultCsidCredential : credential;
        var stamp = ScriptServices.Get<ICredentialSigner>().SignHash(cred, hash);
        var payload = stamp == null
            ? qr.Encode(draft.SellerName, draft.VatNumber, draft.Timestamp,
                        draft.InvoiceTotal, draft.VatTotal, hash)
            : qr.EncodeStamped(draft.SellerName, draft.VatNumber, draft.Timestamp,
                               draft.InvoiceTotal, draft.VatTotal, hash,
                               Convert.ToBase64String(stamp.Signature),
                               stamp.PublicKeyDer, stamp.CertificateSignature);
        return (payload, stamp, cred);
    }

    private static async Task<string> ItemNameAsync(IDictionaryManager dict, Guid itemId)
    {
        if (itemId == Guid.Empty) return "Item";
        var item = await dict.GetRecordAsync<Item>(itemId);
        return string.IsNullOrWhiteSpace(item?.Name) ? "Item" : item!.Name;
    }

    /// <summary>
    /// Money precision of the tenant. Everything else in the business layer
    /// reads it — PricingService, TaxService and therefore the printed form —
    /// so a hardcoded 2 here meant that at AmountScale = 3 the paper and the
    /// XML disagreed while the default hid it on every stand.
    /// </summary>
    private static int Scale => GlobalConstants.Get<int?>("AmountScale") ?? 2;

    private static string Money(decimal value)
        => Math.Round(value, Scale, MidpointRounding.AwayFromZero)
            .ToString("F" + Scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Quantities and percentages are not money; they keep two places
    /// whatever the tenant's money scale is.</summary>
    private static string Plain(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Esc(string? value)
        => (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

}
