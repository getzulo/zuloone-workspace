#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.WebServices;
using ZuloOne.Services.Contracts;

// Exchanges an OTP and a CSR for a Compliance CSID. Returns a secret — developer stands only, explicit grant required.
//
// Moved out of the kernel's ZatcaDevController: a route named after one country
// has no place in a platform that ships everywhere. The exchange ITSELF is now
// this model's ZatcaOnboarding service too — the host keeps only IOutboundCall,
// which turns a channel name into a URL, a credential and a signature. The
// script never sees any of the three.
public partial class ZatcaComplianceService
{
    public override async Task<object?> Any(ZatcaComplianceRequest request)
    {
        try
        {
            var outcome = await ScriptServices.Get<IZatcaOnboarding>()
                .RequestComplianceCsidAsync(request.Otp ?? "", request.Csr ?? "", request.ConnectionRef);

            var status = int.TryParse(Value(outcome, "status"), out var parsed) ? parsed : 0;
            var error = Value(outcome, "error");
            if (!string.IsNullOrEmpty(error))
            {
                // The controller passed a remote 4xx straight through and
                // collapsed a transport failure to 502. Without a status
                // exception that nuance became a 200 with an error field.
                throw new WebServiceStatusException(status >= 400 ? status : 502, new
                {
                    error = error ?? "compliance CSID was not issued",
                    statusCode = status,
                });
            }

            return new ZatcaComplianceResponse
            {
                Ok = true,
                StatusCode = status,
                RequestID = Value(outcome, "requestId"),
                BinarySecurityToken = Value(outcome, "binarySecurityToken"),
                Secret = Value(outcome, "secret"),
                Hint = "Store token:secret as Integration__Credentials__fatoora-csid. Do not commit it.",
            };
        }
        catch (ArgumentException ex)
        {
            throw new WebServiceStatusException(400, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            throw new WebServiceStatusException(400, new { error = ex.Message });
        }
    }

    private static string? Value(Dictionary<string, string> outcome, string key)
        => outcome.TryGetValue(key, out var value) ? value : null;
}
