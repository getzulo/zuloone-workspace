#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// ZATCA QR payload — TLV records, then Base64, capped at 500 characters
// (QRCodeCreation.pdf; tag one byte, length one byte, value bytes). Tags 1-6
// are UTF-8 text, tags 7-9 raw DER.
//
// This lives in the Saudi localization, not in the platform: tag numbers, the
// 500-character cap and the clipping rule are Saudi tax format, and a kernel
// that ships to every country has no business carrying them. Moving it here
// also means a change to the format no longer needs a platform release and a
// pin bump — exactly the cost paid when it lived in Core.
//
// What stays in the host is what genuinely cannot come here:
//
//   ZatcaTlvQr.Sha256Base64   System.Security.Cryptography is NOT in the script
//                             reference set, so a script cannot hash at all.
//   IZatcaCsid                resolves the credential and signs; the PEM never
//                             leaves the host.
//
// A signature over the invoice hash is not a secret — it is printed on the
// invoice — so assembling it into the payload belongs here.
//
// Signatures are deliberately scalars and byte arrays: every type in a service
// contract has to be nameable from a business script, and a tuple list is not
// worth the risk.
public partial class ZatcaQr
{
    public const byte SellerName = 1;
    public const byte VatNumber = 2;
    public const byte Timestamp = 3;
    public const byte InvoiceTotal = 4;
    public const byte VatTotal = 5;
    public const byte InvoiceHash = 6;
    public const byte EcdsaSignature = 7;
    public const byte EcdsaPublicKey = 8;
    public const byte CryptographicStamp = 9;

    /// <summary>The resolution's cap on the Base64 payload.</summary>
    public const int MaxBase64Length = 500;

    /// <summary>Tags 1-6: seller, VAT number, timestamp, totals, invoice hash.</summary>
    public string Encode(
        string sellerName, string vatNumber, string timestamp,
        string invoiceTotal, string vatTotal, string invoiceHash)
        => EncodeTags(TextTags(sellerName, vatNumber, timestamp, invoiceTotal, vatTotal, invoiceHash));

    /// <summary>
    /// Tags 1-6 plus the CSID stamp: 7 (ECDSA over the hash), 8 (SPKI),
    /// 9 (the signature inside the certificate). The three stamp values come
    /// from IZatcaCsid — this service never sees a key.
    /// </summary>
    public string EncodeStamped(
        string sellerName, string vatNumber, string timestamp,
        string invoiceTotal, string vatTotal, string invoiceHash,
        string signatureBase64, byte[]? publicKeyDer, byte[]? certificateSignature)
    {
        var tags = TextTags(sellerName, vatNumber, timestamp, invoiceTotal, vatTotal, invoiceHash);
        tags.Add((EcdsaSignature, Convert.FromBase64String(signatureBase64)));
        tags.Add((EcdsaPublicKey, publicKeyDer ?? Array.Empty<byte>()));
        tags.Add((CryptographicStamp, certificateSignature ?? Array.Empty<byte>()));
        return EncodeTags(tags);
    }

    /// <summary>One tag's text, or "" when the payload does not carry it.</summary>
    public string DecodeTag(string base64, int tag)
    {
        foreach (var (t, value) in DecodeRaw(base64))
            if (t == tag) return Encoding.UTF8.GetString(value);
        return string.Empty;
    }

    /// <summary>How many TLV records the payload holds — 6 unsigned, 9 stamped.</summary>
    public int TagCount(string base64) => DecodeRaw(base64).Count;

    private static string EncodeTags(IReadOnlyList<(byte Tag, byte[] Value)> tags)
    {
        using var buffer = new MemoryStream();
        foreach (var (tag, value) in tags)
        {
            if (tag == 0)
                throw new ArgumentOutOfRangeException(nameof(tags), "ZATCA TLV tag 0 is not used");
            var bytes = value ?? Array.Empty<byte>();
            if (bytes.Length > 255)
                throw new ArgumentOutOfRangeException(nameof(tags),
                    $"Length of tag {tag} is {bytes.Length}; TLV length is one byte");
            buffer.WriteByte(tag);
            buffer.WriteByte((byte)bytes.Length);
            buffer.Write(bytes);
        }

        var encoded = Convert.ToBase64String(buffer.ToArray());
        if (encoded.Length > MaxBase64Length)
            throw new InvalidOperationException(
                $"ZATCA QR Base64 is {encoded.Length} characters; the resolution cap is {MaxBase64Length}");
        return encoded;
    }

    private static List<(byte Tag, byte[] Value)> DecodeRaw(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new ArgumentException("QR payload is empty", nameof(base64));

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("QR payload is not Base64", nameof(base64), ex);
        }

        var tags = new List<(byte Tag, byte[] Value)>();
        var i = 0;
        while (i < raw.Length)
        {
            if (i + 2 > raw.Length)
                throw new ArgumentException("Truncated TLV tag/length", nameof(base64));
            var tag = raw[i];
            var length = raw[i + 1];
            i += 2;
            if (i + length > raw.Length)
                throw new ArgumentException($"Truncated TLV value for tag {tag}", nameof(base64));
            tags.Add((tag, raw.AsSpan(i, length).ToArray()));
            i += length;
        }

        return tags;
    }

    private static List<(byte Tag, byte[] Value)> TextTags(
        string sellerName, string vatNumber, string timestamp,
        string invoiceTotal, string vatTotal, string invoiceHash)
        => new()
        {
            (SellerName, Utf8(sellerName)),
            (VatNumber, Utf8(vatNumber)),
            (Timestamp, Utf8(timestamp)),
            (InvoiceTotal, Utf8(invoiceTotal)),
            (VatTotal, Utf8(vatTotal)),
            (InvoiceHash, Utf8(invoiceHash)),
        };

    private static byte[] Utf8(string? value) => Encoding.UTF8.GetBytes(ClipUtf8(value));

    /// <summary>TLV length is one byte, so a long Arabic seller name is clipped
    /// rather than allowed to abort issuance.</summary>
    private static string ClipUtf8(string? value)
    {
        var text = value ?? string.Empty;
        while (Encoding.UTF8.GetByteCount(text) > 255 && text.Length > 0)
            text = text[..^1];
        return text;
    }
}
