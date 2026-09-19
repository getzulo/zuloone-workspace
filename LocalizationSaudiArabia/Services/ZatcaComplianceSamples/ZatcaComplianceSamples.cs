#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ZuloOne.Core.Services.Integration;
using ZuloOne.Runtime;
using ZuloOne.Services.Contracts;

// Six Fatoora compliance sample kinds. Live Fatoora will not issue a
// Production CSID until these have been accepted on /compliance/invoices.
//
// These are SYNTHETIC UBL documents for onboarding, not posted ERP
// documents. SalesDebitNote exists in Sales, but samples must not
// consume the live ICV counter: a sample batch that called TakeAsync
// would skip six numbers on the next real invoice.
//
// Hash / QR / XAdES reuse IZatcaXades and IZatcaQr. Submit goes through
// IZatcaOnboarding so the channel, credential and signature stay on the host.
public partial class ZatcaComplianceSamples
{
    public const string StandardInvoice = "standard-invoice";
    public const string StandardCredit = "standard-credit";
    public const string StandardDebit = "standard-debit";
    public const string SimplifiedInvoice = "simplified-invoice";
    public const string SimplifiedCredit = "simplified-credit";
    public const string SimplifiedDebit = "simplified-debit";
    public const string DefaultCsidCredential = "fatoora-csid";

    public const string KeyKind = "kind";
    public const string KeyUuid = "uuid";
    public const string KeyHash = "invoiceHash";
    public const string KeyXml = "xml";
    public const string KeyUbl = "ublCode";
    public const string KeyKsa = "ksaType";
    public const string KeyRoot = "root";

    private static readonly string FirstPih = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("")));
    private static readonly string[] KindOrder =
    {
        StandardInvoice, StandardCredit, StandardDebit,
        SimplifiedInvoice, SimplifiedCredit, SimplifiedDebit,
    };

    public Dictionary<string, string> KindCodes()
        => new(StringComparer.Ordinal)
        {
            [StandardInvoice] = "388:0100000",
            [StandardCredit] = "381:0100000",
            [StandardDebit] = "383:0100000",
            [SimplifiedInvoice] = "388:0200000",
            [SimplifiedCredit] = "381:0200000",
            [SimplifiedDebit] = "383:0200000",
        };

    public Dictionary<string, string> Build(string kind)
        => Build(kind, FirstPih);

    public async Task<Dictionary<string, string>> SubmitAllAsync(string? connectionRef = null)
    {
        var onboarding = ScriptServices.Get<IZatcaOnboarding>();
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        var previousHash = FirstPih;
        foreach (var kind in KindOrder)
        {
            var sample = Build(kind, previousHash);
            var outcome = await onboarding.SubmitComplianceInvoiceAsync(
                sample[KeyUuid], sample[KeyHash], sample[KeyXml], connectionRef);
            outcome.TryGetValue("error", out var error);
            outcome.TryGetValue("status", out var status);
            results[kind] = string.IsNullOrEmpty(error) ? (status ?? "") : error!;
            previousHash = sample[KeyHash];
        }
        return results;
    }

    private Dictionary<string, string> Build(string kind, string previousHash)
    {
        if (!KindCodes().TryGetValue(kind, out var codes))
            throw new ArgumentException("Unknown compliance sample kind: " + kind, nameof(kind));

        var parts = codes.Split(':');
        var ublCode = parts[0];
        var ksaType = parts[1];
        var root = ublCode == "381" ? "CreditNote" : ublCode == "383" ? "DebitNote" : "Invoice";
        var standard = ksaType.StartsWith("01", StringComparison.Ordinal);
        var uuid = Guid.NewGuid();
        var issue = DateTime.UtcNow;
        var timestamp = issue.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        const string exclusive = "100.00";
        const string tax = "15.00";
        const string inclusive = "115.00";
        const string seller = "Sample Seller";
        const string sellerVat = "310122393500003";

        var unsigned = UnsignedUbl(
            root, ublCode, ksaType, uuid, issue, previousHash, standard, exclusive, tax, inclusive,
            seller, sellerVat);

        var xades = ScriptServices.Get<IZatcaXades>();
        var hash = xades.InvoiceHash(unsigned);
        var qrSvc = ScriptServices.Get<IZatcaQr>();
        var stamp = ScriptServices.Get<ICredentialSigner>().SignHash(DefaultCsidCredential, hash);
        var qr = stamp == null
            ? qrSvc.Encode(seller, sellerVat, timestamp, inclusive, tax, hash)
            : qrSvc.EncodeStamped(seller, sellerVat, timestamp, inclusive, tax, hash,
                Convert.ToBase64String(stamp.Signature),
                stamp.PublicKeyDer, stamp.CertificateSignature);

        var signingTime = DateTime.UtcNow;
        string? siSig = null;
        string? certDer = null;
        if (stamp?.CertificateDer is { Length: > 0 })
        {
            var digest = xades.SignedInfoDigest(hash, Convert.ToBase64String(stamp.CertificateDer), signingTime);
            var siStamp = ScriptServices.Get<ICredentialSigner>().SignHash(DefaultCsidCredential, digest);
            if (siStamp != null)
            {
                siSig = Convert.ToBase64String(siStamp.Signature);
                certDer = Convert.ToBase64String(stamp.CertificateDer);
            }
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KeyKind] = kind,
            [KeyUuid] = uuid.ToString(),
            [KeyHash] = hash,
            [KeyXml] = xades.Seal(unsigned, qr, siSig, certDer, signingTime),
            [KeyUbl] = ublCode,
            [KeyKsa] = ksaType,
            [KeyRoot] = root,
        };
    }

    private static string UnsignedUbl(
        string root, string ublCode, string ksaType, Guid uuid, DateTime issue,
        string previousHash, bool standard, string exclusive, string tax, string inclusive,
        string seller, string sellerVat)
    {
        var lineTag = root == "CreditNote" ? "CreditNoteLine"
            : root == "DebitNote" ? "DebitNoteLine"
            : "InvoiceLine";
        var qtyTag = root == "CreditNote" ? "CreditedQuantity"
            : root == "DebitNote" ? "DebitedQuantity"
            : "InvoicedQuantity";
        var buyerVat = standard ? "300000000000003" : "";
        var buyerName = standard ? "Sample Buyer" : "Sample Consumer";
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append('<').Append(root);
        sb.Append(" xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:").Append(root).Append("-2\"");
        sb.Append(" xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\"");
        sb.Append(" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">");
        sb.Append("<cbc:ProfileID>reporting:1.0</cbc:ProfileID>");
        sb.Append("<cbc:ID>SAMPLE-").Append(ublCode).Append('-').Append(ksaType).Append("</cbc:ID>");
        sb.Append("<cbc:UUID>").Append(uuid.ToString()).Append("</cbc:UUID>");
        sb.Append("<cbc:IssueDate>").Append(issue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</cbc:IssueDate>");
        sb.Append("<cbc:IssueTime>").Append(issue.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("Z</cbc:IssueTime>");
        sb.Append("<cbc:InvoiceTypeCode name=\"").Append(ksaType).Append("\">").Append(ublCode).Append("</cbc:InvoiceTypeCode>");
        sb.Append("<cbc:DocumentCurrencyCode>SAR</cbc:DocumentCurrencyCode>");
        if (root != "Invoice")
        {
            sb.Append("<cac:BillingReference><cac:InvoiceDocumentReference>");
            sb.Append("<cbc:ID>SAMPLE-388-").Append(ksaType).Append("</cbc:ID>");
            sb.Append("</cac:InvoiceDocumentReference></cac:BillingReference>");
            sb.Append("<cac:PaymentMeans><cbc:PaymentMeansCode>10</cbc:PaymentMeansCode>");
            sb.Append("<cbc:InstructionNote>CANCELLATION</cbc:InstructionNote></cac:PaymentMeans>");
        }
        sb.Append("<cac:AdditionalDocumentReference><cbc:ID>ICV</cbc:ID><cbc:UUID>1</cbc:UUID></cac:AdditionalDocumentReference>");
        sb.Append("<cac:AdditionalDocumentReference><cbc:ID>PIH</cbc:ID><cac:Attachment>");
        sb.Append("<cbc:EmbeddedDocumentBinaryObject mimeCode=\"text/plain\">");
        sb.Append(Esc(previousHash));
        sb.Append("</cbc:EmbeddedDocumentBinaryObject></cac:Attachment></cac:AdditionalDocumentReference>");
        sb.Append(PartyXml("AccountingSupplierParty", seller, sellerVat, sellerVat, crn: "1010010000"));
        sb.Append(PartyXml("AccountingCustomerParty", buyerName, buyerVat, buyerVat, crn: ""));
        sb.Append("<cac:TaxTotal><cbc:TaxAmount currencyID=\"SAR\">").Append(tax).Append("</cbc:TaxAmount></cac:TaxTotal>");
        sb.Append("<cac:LegalMonetaryTotal>");
        sb.Append("<cbc:LineExtensionAmount currencyID=\"SAR\">").Append(exclusive).Append("</cbc:LineExtensionAmount>");
        sb.Append("<cbc:TaxExclusiveAmount currencyID=\"SAR\">").Append(exclusive).Append("</cbc:TaxExclusiveAmount>");
        sb.Append("<cbc:TaxInclusiveAmount currencyID=\"SAR\">").Append(inclusive).Append("</cbc:TaxInclusiveAmount>");
        sb.Append("<cbc:PayableAmount currencyID=\"SAR\">").Append(inclusive).Append("</cbc:PayableAmount>");
        sb.Append("</cac:LegalMonetaryTotal>");
        sb.Append("<cac:").Append(lineTag).Append('>');
        sb.Append("<cbc:ID>1</cbc:ID>");
        sb.Append("<cbc:").Append(qtyTag).Append(" unitCode=\"PCE\">1.00</cbc:").Append(qtyTag).Append('>');
        sb.Append("<cbc:LineExtensionAmount currencyID=\"SAR\">").Append(exclusive).Append("</cbc:LineExtensionAmount>");
        sb.Append("<cac:TaxTotal><cbc:TaxAmount currencyID=\"SAR\">").Append(tax).Append("</cbc:TaxAmount></cac:TaxTotal>");
        sb.Append("<cac:Item><cbc:Name>Sample item</cbc:Name>");
        sb.Append("<cac:ClassifiedTaxCategory><cbc:ID>S</cbc:ID><cbc:Percent>15.00</cbc:Percent>");
        sb.Append("<cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:ClassifiedTaxCategory></cac:Item>");
        sb.Append("<cac:Price><cbc:PriceAmount currencyID=\"SAR\">").Append(exclusive).Append("</cbc:PriceAmount></cac:Price>");
        sb.Append("</cac:").Append(lineTag).Append('>');
        sb.Append("</").Append(root).Append('>');
        return sb.ToString();
    }

    // Compact party block — copy of the SaudiEInvoice shape, not a shared type:
    // the contract assembly cannot see a helper class from this file.
    private static string PartyXml(string tag, string name, string partyId, string vat, string crn)
    {
        var sb = new StringBuilder();
        sb.Append("<cac:").Append(tag).Append("><cac:Party>");
        if (partyId.Length > 0)
        {
            sb.Append("<cac:PartyIdentification><cbc:ID schemeID=\"VAT\">");
            sb.Append(Esc(partyId)).Append("</cbc:ID></cac:PartyIdentification>");
        }
        sb.Append("<cac:PostalAddress>");
        sb.Append("<cbc:StreetName>Olaya St</cbc:StreetName>");
        sb.Append("<cbc:BuildingNumber>1234</cbc:BuildingNumber>");
        sb.Append("<cbc:CitySubdivisionName>Al Olaya</cbc:CitySubdivisionName>");
        sb.Append("<cbc:CityName>Riyadh</cbc:CityName>");
        sb.Append("<cbc:PostalZone>12211</cbc:PostalZone>");
        sb.Append("<cac:Country><cbc:IdentificationCode>SA</cbc:IdentificationCode></cac:Country>");
        sb.Append("</cac:PostalAddress>");
        if (vat.Length > 0)
        {
            sb.Append("<cac:PartyTaxScheme><cbc:CompanyID>").Append(Esc(vat)).Append("</cbc:CompanyID>");
            sb.Append("<cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:PartyTaxScheme>");
        }
        else if (crn.Length > 0)
        {
            sb.Append("<cac:PartyTaxScheme><cbc:CompanyID>").Append(Esc(crn)).Append("</cbc:CompanyID>");
            sb.Append("<cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme></cac:PartyTaxScheme>");
        }
        sb.Append("<cac:PartyLegalEntity><cbc:RegistrationName>").Append(Esc(name)).Append("</cbc:RegistrationName></cac:PartyLegalEntity>");
        sb.Append("</cac:Party></cac:").Append(tag).Append('>');
        return sb.ToString();
    }

    private static string Esc(string? value)
        => (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
