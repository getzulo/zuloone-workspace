#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.WebServices;

// Stand stub for Fatoora — Production CSID.
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

public partial class FatooraStubProductionCsidService
{
    public override Task<object?> Any(FatooraStubProductionCsidRequest request)
    {
        // Basic auth from the Compliance CSID. Refusing without it is what
        // makes "the signer attached the credential" a testable claim.
        var auth = Headers.TryGetValue("Authorization", out var value) ? value : string.Empty;
        if (string.IsNullOrWhiteSpace(auth)
            || !auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            throw new WebServiceStatusException(401,
                new { error = "Compliance CSID Basic auth is required" });
        }

        // A DIFFERENT token than the compliance one, so mixing the two up is
        // visible rather than silently working.
        return Task.FromResult<object?>(new FatooraStubProductionCsidResponse
        {
            Ok = true,
            StatusCode = 200,
            RequestID = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            DispositionMessage = "ISSUED",
            BinarySecurityToken = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes("stub-pcsid")),
            Secret = "stub-pcsid-secret",
        });
    }
}
