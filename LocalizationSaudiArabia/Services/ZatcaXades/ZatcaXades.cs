#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ZuloOne.Core.Services;

// ZATCA invoice hash and the two things that must sit in the UBL AFTER it:
// the QR AdditionalDocumentReference, and (when a CSID DER is on hand) an
// enveloped XAdES-BES.
//
// Hashing here is not SHA-256 of the author's string. ZATCA hashes C14N 1.0
// of the document minus UBLExtensions, cac:Signature and the QR reference —
// so sealing the XML does not change InvoiceHash or the PIH chain. Scripts
// have no System.Xml; IXmlCanonicalizer is the host seam.
//
// C14N 1.1 (the Algorithm URI in the official SDK) is not what the host
// computes. The SignedInfo we emit names C14N 1.0 so the digest and the
// declaration agree. Live Fatoora schematron against 1.1 is still out.
public partial class ZatcaXades
{
    private static readonly string[] StripNames = { "UBLExtensions", "Signature" };
    private static readonly string[] StripQr = { "QR" };
    private const string CacSignature =
        "<cac:Signature><cbc:ID>urn:oasis:names:specification:ubl:signature:Invoice</cbc:ID>" +
        "<cbc:SignatureMethod>urn:oasis:names:specification:ubl:dsig:enveloped:xades</cbc:SignatureMethod></cac:Signature>";

    private readonly IXmlCanonicalizer _c14n;

    public ZatcaXades(IXmlCanonicalizer c14n) => _c14n = c14n;

    /// <summary>
    /// Base64(SHA-256(C14N(xml minus UBLExtensions, Signature, QR))).
    /// </summary>
    public string InvoiceHash(string xml)
        => Convert.ToBase64String(SHA256.HashData(_c14n.Canonicalize(xml, StripNames, StripQr)));

    /// <summary>
    /// Inserts the QR AdditionalDocumentReference before AccountingSupplierParty.
    /// Idempotent if a QR id is already present.
    /// </summary>
    public string EmbedQr(string xml, string qr)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("UBL is empty.", nameof(xml));
        if (xml.Contains("<cbc:ID>QR</cbc:ID>", StringComparison.Ordinal))
            return xml;
        return InsertBefore(xml, "<cac:AccountingSupplierParty>", QrBlock(qr));
    }

    /// <summary>
    /// SHA-256 of C14N(SignedInfo) for this invoice hash and certificate.
    /// The caller SignHash's this — it is NOT the invoice hash (QR tag 7).
    /// </summary>
    public string SignedInfoDigest(string invoiceHash, string certificateDerBase64, DateTime signingTime)
    {
        var props = BuildSignedProperties(certificateDerBase64, signingTime);
        var propsHash = Convert.ToBase64String(SHA256.HashData(_c14n.Canonicalize(props)));
        var signedInfo = BuildSignedInfo(invoiceHash, propsHash);
        return Convert.ToBase64String(SHA256.HashData(_c14n.Canonicalize(signedInfo)));
    }

    /// <summary>
    /// QR always; XAdES when both the SignedInfo signature and the certificate DER
    /// are present. Hash of the result equals <see cref="InvoiceHash"/> of
    /// <paramref name="xml"/> — the extras are exactly what the hash strips.
    /// </summary>
    public string Seal(
        string xml, string qr,
        string? signedInfoSignatureBase64, string? certificateDerBase64,
        DateTime signingTime)
    {
        var withQr = EmbedQr(xml, qr);
        if (string.IsNullOrWhiteSpace(signedInfoSignatureBase64)
            || string.IsNullOrWhiteSpace(certificateDerBase64))
            return withQr;

        var invoiceHash = InvoiceHash(xml);
        var props = BuildSignedProperties(certificateDerBase64, signingTime);
        var propsHash = Convert.ToBase64String(SHA256.HashData(_c14n.Canonicalize(props)));
        var signedInfo = BuildSignedInfo(invoiceHash, propsHash);
        var extensions = BuildUblExtensions(signedInfo, signedInfoSignatureBase64, certificateDerBase64, props);
        var withSig = InsertBefore(withQr, "<cac:AccountingSupplierParty>", CacSignature);
        return withSig.Insert(RootOpenEnd(withSig), extensions);
    }

    private static string QrBlock(string qr)
        => "<cac:AdditionalDocumentReference><cbc:ID>QR</cbc:ID><cac:Attachment>" +
           "<cbc:EmbeddedDocumentBinaryObject mimeCode=\"text/plain\">" + Esc(qr) +
           "</cbc:EmbeddedDocumentBinaryObject></cac:Attachment></cac:AdditionalDocumentReference>";

    private static string BuildSignedProperties(string certificateDerBase64, DateTime signingTime)
    {
        using var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificateDerBase64));
        var certDigest = Convert.ToBase64String(SHA256.HashData(cert.RawData));
        var issuer = Esc(cert.IssuerName.Decode(
            X500DistinguishedNameFlags.UseCommas
            | X500DistinguishedNameFlags.Reversed
            | X500DistinguishedNameFlags.DoNotUseQuotes));
        var serial = UnsignedLittleEndianToDecimal(cert.GetSerialNumber());
        var time = signingTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("<xades:SignedProperties xmlns:xades=\"http://uri.etsi.org/01903/v1.3.2#\" xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\" Id=\"xadesSignedProperties\">");
        sb.Append("<xades:SignedSignatureProperties>");
        sb.Append("<xades:SigningTime>").Append(time).Append("</xades:SigningTime>");
        sb.Append("<xades:SigningCertificate><xades:Cert><xades:CertDigest>");
        sb.Append("<ds:DigestMethod Algorithm=\"http://www.w3.org/2001/04/xmlenc#sha256\"/>");
        sb.Append("<ds:DigestValue>").Append(certDigest).Append("</ds:DigestValue>");
        sb.Append("</xades:CertDigest><xades:IssuerSerial>");
        sb.Append("<ds:X509IssuerName>").Append(issuer).Append("</ds:X509IssuerName>");
        sb.Append("<ds:X509SerialNumber>").Append(serial).Append("</ds:X509SerialNumber>");
        sb.Append("</xades:IssuerSerial></xades:Cert></xades:SigningCertificate>");
        sb.Append("</xades:SignedSignatureProperties></xades:SignedProperties>");
        return sb.ToString();
    }

    private static string BuildSignedInfo(string invoiceHash, string signedPropertiesHash)
    {
        var sb = new StringBuilder();
        sb.Append("<ds:SignedInfo xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">");
        sb.Append("<ds:CanonicalizationMethod Algorithm=\"http://www.w3.org/TR/2001/REC-xml-c14n-20010315\"/>");
        sb.Append("<ds:SignatureMethod Algorithm=\"http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256\"/>");
        sb.Append("<ds:Reference Id=\"invoiceSignedData\" URI=\"\">");
        sb.Append("<ds:Transforms>");
        sb.Append("<ds:Transform Algorithm=\"http://www.w3.org/TR/2001/REC-xml-c14n-20010315\"/>");
        sb.Append("</ds:Transforms>");
        sb.Append("<ds:DigestMethod Algorithm=\"http://www.w3.org/2001/04/xmlenc#sha256\"/>");
        sb.Append("<ds:DigestValue>").Append(Esc(invoiceHash)).Append("</ds:DigestValue>");
        sb.Append("</ds:Reference>");
        sb.Append("<ds:Reference Type=\"http://www.w3.org/2000/09/xmldsig#SignatureProperties\" URI=\"#xadesSignedProperties\">");
        sb.Append("<ds:DigestMethod Algorithm=\"http://www.w3.org/2001/04/xmlenc#sha256\"/>");
        sb.Append("<ds:DigestValue>").Append(Esc(signedPropertiesHash)).Append("</ds:DigestValue>");
        sb.Append("</ds:Reference></ds:SignedInfo>");
        return sb.ToString();
    }

    private static string BuildUblExtensions(
        string signedInfo, string signatureValueBase64, string certificateDerBase64, string signedProperties)
    {
        var sb = new StringBuilder();
        sb.Append("<ext:UBLExtensions xmlns:ext=\"urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2\">");
        sb.Append("<ext:UBLExtension><ext:ExtensionURI>urn:oasis:names:specification:ubl:dsig:enveloped:xades</ext:ExtensionURI>");
        sb.Append("<ext:ExtensionContent>");
        sb.Append("<sig:UBLDocumentSignatures xmlns:sig=\"urn:oasis:names:specification:ubl:schema:xsd:CommonSignatureComponents-2\"");
        sb.Append(" xmlns:sac=\"urn:oasis:names:specification:ubl:schema:xsd:SignatureAggregateComponents-2\"");
        sb.Append(" xmlns:sbc=\"urn:oasis:names:specification:ubl:schema:xsd:SignatureBasicComponents-2\">");
        sb.Append("<sac:SignatureInformation>");
        sb.Append("<cbc:ID>urn:oasis:names:specification:ubl:signature:1</cbc:ID>");
        sb.Append("<sbc:ReferencedSignatureID>urn:oasis:names:specification:ubl:signature:Invoice</sbc:ReferencedSignatureID>");
        sb.Append("<ds:Signature xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\" Id=\"signature\">");
        sb.Append(signedInfo);
        sb.Append("<ds:SignatureValue>").Append(Esc(signatureValueBase64)).Append("</ds:SignatureValue>");
        sb.Append("<ds:KeyInfo><ds:X509Data><ds:X509Certificate>").Append(Esc(certificateDerBase64)).Append("</ds:X509Certificate></ds:X509Data></ds:KeyInfo>");
        sb.Append("<ds:Object>");
        sb.Append("<xades:QualifyingProperties xmlns:xades=\"http://uri.etsi.org/01903/v1.3.2#\" Target=\"signature\">");
        sb.Append(signedProperties);
        sb.Append("</xades:QualifyingProperties></ds:Object></ds:Signature>");
        sb.Append("</sac:SignatureInformation></sig:UBLDocumentSignatures>");
        sb.Append("</ext:ExtensionContent></ext:UBLExtension></ext:UBLExtensions>");
        return sb.ToString();
    }

    private static string InsertBefore(string xml, string marker, string block)
    {
        var i = xml.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0)
            throw new InvalidOperationException("UBL has no " + marker);
        return xml.Insert(i, block);
    }

    private static int RootOpenEnd(string xml)
    {
        var invoice = xml.IndexOf("<Invoice", StringComparison.Ordinal);
        var note = xml.IndexOf("<CreditNote", StringComparison.Ordinal);
        int start;
        if (invoice >= 0 && (note < 0 || invoice < note)) start = invoice;
        else if (note >= 0) start = note;
        else throw new InvalidOperationException("UBL root is neither Invoice nor CreditNote");
        var end = xml.IndexOf('>', start);
        if (end < 0) throw new InvalidOperationException("UBL root is not closed");
        return end + 1;
    }

    private static string UnsignedLittleEndianToDecimal(byte[] le)
    {
        var digits = new List<int> { 0 };
        for (var i = le.Length - 1; i >= 0; i--)
        {
            var carry = (int)le[i];
            for (var j = 0; j < digits.Count; j++)
            {
                var v = digits[j] * 256 + carry;
                digits[j] = v % 10;
                carry = v / 10;
            }
            while (carry > 0)
            {
                digits.Add(carry % 10);
                carry /= 10;
            }
        }
        var sb = new StringBuilder(digits.Count);
        for (var i = digits.Count - 1; i >= 0; i--)
            sb.Append((char)('0' + digits[i]));
        return sb.ToString();
    }

    private static string Esc(string? value)
        => (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
