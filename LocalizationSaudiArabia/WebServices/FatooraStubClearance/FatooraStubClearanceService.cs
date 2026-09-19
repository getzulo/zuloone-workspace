#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.WebServices;

// Stand stub for Fatoora — B2B clearance.
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

public partial class FatooraStubClearanceService
{
    public override async Task<object?> Any(FatooraStubClearanceRequest request)
    {
        var key = Headers.TryGetValue("X-Idempotency-Key", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "fatoora:" + Guid.NewGuid().ToString("N");

        var dict = ScriptServices.Get<IDictionaryManager>();
        var seen = await dict.GetRecordsAsync<FatooraStubDelivery>(
            $"IdempotencyKey = '{key.Replace("'", "''")}'", take: 1);

        // Replaying the first answer — without recording a second delivery — is
        // what makes the sender's idempotency claim mean anything.
        if (seen.Count > 0)
        {
            return new FatooraStubClearanceResponse
            {
                Ok = true,
                StatusCode = seen[0].StatusCode,
                RequestID = seen[0].ResponseBody,
                ClearanceStatus = "CLEARED",
                ReportingStatus = "CLEARED",
                Replay = true,
            };
        }

        var requestId = "REQ-" + Guid.NewGuid().ToString("N")[..12];
        var row = dict.NewRecord<FatooraStubDelivery>();
        row.IdempotencyKey = key;
        row.StatusCode = 200;
        row.ResponseBody = requestId;
        row.RequestBody = Clip(RawBody);
        await dict.SaveRecordAsync(row);

        return new FatooraStubClearanceResponse
        {
            Ok = true,
            StatusCode = 200,
            RequestID = requestId,
            ClearanceStatus = "CLEARED",
            ReportingStatus = "CLEARED",
            Replay = false,
        };
    }

    /// <summary>The field holds 4000; a whole UBL invoice does not fit.</summary>
    private static string Clip(string? body)
        => string.IsNullOrEmpty(body) ? string.Empty
           : body!.Length <= 4000 ? body! : body![..4000];
}
