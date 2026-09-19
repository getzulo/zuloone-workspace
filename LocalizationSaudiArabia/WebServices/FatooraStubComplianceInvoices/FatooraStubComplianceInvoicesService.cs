#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.WebServices;

// Stand stub for Fatoora — compliance sample invoices.
//
// Live Fatoora will not issue a Production CSID until this path has accepted
// the six document kinds. The stub accepts each well-formed payload so the
// onboarding service can be tested without gw-fatoora.
//
// isDevOnly, so the whole thing is 404 on a production stand. No explicit-role
// gate: the outbound sender calls it unauthenticated, exactly as it would call
// the real gateway.

public partial class FatooraStubComplianceInvoicesService
{
    public override Task<object?> Any(FatooraStubComplianceInvoicesRequest request)
    {
        var auth = Headers.TryGetValue("Authorization", out var value) ? value : string.Empty;
        if (string.IsNullOrWhiteSpace(auth)
            || !auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            throw new WebServiceStatusException(401,
                new { error = "Compliance CSID Basic auth is required" });
        }

        var body = RawBody ?? string.Empty;
        if (!body.Contains("\"uuid\"", StringComparison.Ordinal)
            || !body.Contains("\"invoiceHash\"", StringComparison.Ordinal)
            || !body.Contains("\"invoice\"", StringComparison.Ordinal))
        {
            throw new WebServiceStatusException(400,
                new { error = "JSON body with uuid, invoiceHash and invoice is required" });
        }

        return Task.FromResult<object?>(new FatooraStubComplianceInvoicesResponse
        {
            Ok = true,
            StatusCode = 200,
            RequestID = "REQ-" + Guid.NewGuid().ToString("N")[..12],
            ReportingStatus = "REPORTED",
            ClearanceStatus = "CLEARED",
        });
    }
}
