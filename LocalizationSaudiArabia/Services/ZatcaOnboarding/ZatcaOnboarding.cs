#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Core.Services.Integration;
using ZuloOne.Runtime;

// ZATCA onboarding: OTP + CSR -> Compliance CSID, then Compliance CSID ->
// Production CSID.
//
// This was ZatcaOnboardingService in the kernel — 224 lines named after one
// country inside a platform that installs everywhere. Almost all of it was
// generic plumbing (resolve the channel, the connection, the credential, sign,
// send) wrapped around four Saudi facts: two channel names, a one-shot OTP
// header, the JSON shape, and the field names in the reply. The plumbing became
// IOutboundCall; the four facts are here.
//
// The script still cannot reach the network. It names a CHANNEL and the host
// turns that into a URL, a credential and a signature — HttpClient, the SSRF
// allow-list and ICredentialResolver all stay on the other side.
//
// Why this is NOT queued through IOutboundGateway: the reply carries a secret
// that is valid once. An outbox row would journal it, and a retry an hour later
// would present a spent OTP.
public partial class ZatcaOnboarding
{
    public const string ComplianceChannel = "fatoora-compliance";
    public const string ProductionCsidChannel = "fatoora-production-csid";
    public const string ComplianceInvoicesChannel = "fatoora-compliance-invoices";

    // Result keys — a service contract may only name types every script can see.
    public const string KeyStatus = "status";
    public const string KeyRequestId = "requestId";
    public const string KeyToken = "binarySecurityToken";
    public const string KeySecret = "secret";
    public const string KeyError = "error";
    public const string KeyReportingStatus = "reportingStatus";
    public const string KeyClearanceStatus = "clearanceStatus";

    /// <summary>
    /// Exchanges a one-shot OTP and a CSR for a Compliance CSID.
    ///
    /// <para>The OTP travels as a header on THIS call only. It is deliberately
    /// not part of the signing profile: an OTP on a queued or re-signed request
    /// would be journalled and replayed, and it is valid once.</para>
    /// </summary>
    public async Task<Dictionary<string, string>> RequestComplianceCsidAsync(
        string otp, string csrPem, string? connectionRef = null)
    {
        if (string.IsNullOrWhiteSpace(otp))
            throw new ArgumentException("OTP is required", nameof(otp));
        if (string.IsNullOrWhiteSpace(csrPem))
            throw new ArgumentException("CSR is required", nameof(csrPem));

        var payload = "{\"csr\":\"" + EscapeJson(Base64Csr(csrPem)) + "\"}";
        var result = await ScriptServices.Get<IOutboundCall>().SendAsync(
            ComplianceChannel,
            payload,
            idempotencyKey: "zatca-compliance",
            connectionRef: connectionRef,
            headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["OTP"] = otp.Trim() },
            requireCredential: false,
            cancellationToken: CancellationToken.None);

        return Parse(result);
    }

    /// <summary>
    /// Exchanges a Compliance CSID for a Production CSID. Live Fatoora also
    /// wants compliance sample invoices first (<see cref="SubmitComplianceInvoiceAsync"/>);
    /// the stand stub does not.
    /// </summary>
    public async Task<Dictionary<string, string>> RequestProductionCsidAsync(
        string? complianceRequestId = null, string? connectionRef = null)
    {
        var payload = string.IsNullOrWhiteSpace(complianceRequestId)
            ? "{}"
            : "{\"complianceRequestId\":\"" + EscapeJson(complianceRequestId!.Trim()) + "\"}";

        var result = await ScriptServices.Get<IOutboundCall>().SendAsync(
            ProductionCsidChannel,
            payload,
            idempotencyKey: "zatca-production-csid",
            connectionRef: connectionRef,
            headers: null,
            // The Compliance CSID must already be stored, or this call is
            // unauthenticated and Fatoora answers 401 with no useful message.
            requireCredential: true,
            cancellationToken: CancellationToken.None);

        return Parse(result);
    }

    /// <summary>
    /// Posts one compliance sample invoice. Live Fatoora will not issue a
    /// Production CSID until the six document kinds have been accepted here
    /// (see IZatcaComplianceSamples.SubmitAllAsync). The stand stub
    /// accepts a well-formed payload.
    /// </summary>
    public async Task<Dictionary<string, string>> SubmitComplianceInvoiceAsync(
        string uuid, string invoiceHash, string invoiceXml, string? connectionRef = null)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            throw new ArgumentException("UUID is required", nameof(uuid));
        if (string.IsNullOrWhiteSpace(invoiceHash))
            throw new ArgumentException("Invoice hash is required", nameof(invoiceHash));
        if (string.IsNullOrWhiteSpace(invoiceXml))
            throw new ArgumentException("Invoice XML is required", nameof(invoiceXml));

        var invoiceB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(invoiceXml));
        var payload = "{\"uuid\":\"" + EscapeJson(uuid.Trim())
            + "\",\"invoiceHash\":\"" + EscapeJson(invoiceHash.Trim())
            + "\",\"invoice\":\"" + EscapeJson(invoiceB64) + "\"}";

        var result = await ScriptServices.Get<IOutboundCall>().SendAsync(
            ComplianceInvoicesChannel,
            payload,
            idempotencyKey: "zatca-compliance-invoice:" + uuid.Trim(),
            connectionRef: connectionRef,
            headers: null,
            requireCredential: true,
            cancellationToken: CancellationToken.None);

        return ParseInvoice(result);
    }

    /// <summary>Field names Fatoora answers with. requestID / requestId both occur.</summary>
    public Dictionary<string, string> Parse(OutboundCallResult result)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KeyStatus] = result.StatusCode.ToString(),
        };

        if (!string.IsNullOrEmpty(result.Error))
        {
            parsed[KeyError] = result.Error!;
            return parsed;
        }
        if (string.IsNullOrWhiteSpace(result.Body))
        {
            parsed[KeyError] = "empty CSID response";
            return parsed;
        }
        if (!result.Ok)
        {
            parsed[KeyError] = Trim(result.Body!);
            return parsed;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body!);
            var root = doc.RootElement;
            Put(parsed, KeyRequestId, Text(root, "requestID") ?? Text(root, "requestId"));
            Put(parsed, KeyToken, Text(root, "binarySecurityToken"));
            Put(parsed, KeySecret, Text(root, "secret"));
            if (!parsed.ContainsKey(KeyToken))
                parsed[KeyError] = "CSID response carries no binarySecurityToken";
        }
        catch (JsonException)
        {
            parsed[KeyError] = "CSID response is not JSON: " + Trim(result.Body!);
        }

        return parsed;
    }

    /// <summary>Compliance-invoice reply: status + reporting/clearance, no CSID secret.</summary>
    public Dictionary<string, string> ParseInvoice(OutboundCallResult result)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KeyStatus] = result.StatusCode.ToString(),
        };

        if (!string.IsNullOrEmpty(result.Error))
        {
            parsed[KeyError] = result.Error!;
            return parsed;
        }
        if (string.IsNullOrWhiteSpace(result.Body))
        {
            parsed[KeyError] = "empty compliance-invoice response";
            return parsed;
        }
        if (!result.Ok)
        {
            parsed[KeyError] = Trim(result.Body!);
            return parsed;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body!);
            var root = doc.RootElement;
            Put(parsed, KeyRequestId, Text(root, "requestID") ?? Text(root, "requestId"));
            Put(parsed, KeyReportingStatus, Text(root, "reportingStatus"));
            Put(parsed, KeyClearanceStatus, Text(root, "clearanceStatus"));
            if (!parsed.ContainsKey(KeyReportingStatus) && !parsed.ContainsKey(KeyClearanceStatus))
                parsed[KeyError] = "compliance-invoice response carries no reportingStatus";
        }
        catch (JsonException)
        {
            parsed[KeyError] = "compliance-invoice response is not JSON: " + Trim(result.Body!);
        }

        return parsed;
    }

    private static void Put(Dictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) target[key] = value!;
    }

    private static string? Text(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Base64 of the WHOLE PEM, armour lines included — carried over verbatim
    /// from the kernel implementation this replaced.
    ///
    /// <para>Whether Fatoora wants the armour stripped is not settled in our
    /// sources, and a move is the wrong moment to find out: change it here and a
    /// failed onboarding gets debugged against code that no longer matches what
    /// was tested. If it does need stripping, that is a fix with its own test.</para>
    /// </summary>
    private static string Base64Csr(string csrPem)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(csrPem));

    private static string Trim(string body)
        => body.Length <= 500 ? body : body[..500];

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
