#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.WebServices;

// Stand stub for Fatoora — Compliance CSID.
//
// Moved out of IntegrationStubController in the kernel. The rest of that
// controller stays there on purpose: it exercises the PLATFORM's own outbound
// machinery (idempotency, leasing, the attempt journal, the SSRF redirect
// trap). These four routes did not — they imitate one country's tax authority,
// which is this model's business.
//
// It is not a mock that says yes to everything. That is the whole point: a stub
// that accepts anything would prove only that we can reach it.
//
// isDevOnly, so the whole thing is 404 on a production stand. No explicit-role
// gate: the outbound sender calls it unauthenticated, exactly as it would call
// the real gateway.

public partial class FatooraStubComplianceService
{
    public override Task<object?> Any(FatooraStubComplianceRequest request)
    {
        // The OTP rides as a HEADER, the way the portal specifies — which is
        // why this endpoint could not be a web service until the host started
        // handing headers to the script.
        var otp = Headers.TryGetValue("OTP", out var value) ? value : string.Empty;
        if (string.IsNullOrWhiteSpace(otp))
            throw new WebServiceStatusException(400, new { error = "OTP header is required" });

        // Checked against the RAW body, not the bound DTO: the real gateway
        // reads a JSON field named csr, and a stub that accepted an empty
        // request would hide a client that forgot to send one.
        var body = RawBody ?? string.Empty;
        if (!body.Contains("\"csr\"", StringComparison.Ordinal))
            throw new WebServiceStatusException(400, new { error = "JSON body with csr is required" });

        var token = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("stub-csid-" + otp.Trim()));
        return Task.FromResult<object?>(new FatooraStubComplianceResponse
        {
            Ok = true,
            StatusCode = 200,
            RequestID = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            DispositionMessage = "ISSUED",
            BinarySecurityToken = token,
            Secret = "stub-secret",
        });
    }
}
