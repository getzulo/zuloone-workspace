#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Core.Services.Integration;
using ZuloOne.Runtime;
using ZuloOne.Runtime.WebServices;

// Exchanges an OTP and a CSR for a Compliance CSID. Returns a secret — developer stands only, explicit grant required.
//
// Moved out of the kernel's ZatcaDevController: a route named after one country
// has no place in a platform that ships everywhere. The capability stayed —
// crypto, secrets and the wire are still the host's — only the surface is here.
public partial class ZatcaComplianceService
{
    public override async Task<object?> Any(ZatcaComplianceRequest request)
    {
        // The work stays in the host: IZatcaOnboarding holds the outbound
        // transport and the credential resolver, both of which a script is
        // refused by name. Only the HTTP surface moved here.
        try
        {
            var outcome = await ScriptServices.Get<IZatcaOnboarding>()
                .RequestComplianceCsidAsync(request.Otp ?? "", request.Csr ?? "", request.ConnectionRef, default);

            if (!outcome.Ok)
            {
                // The controller passed a remote 4xx straight through and
                // collapsed a transport failure to 502. Without a status
                // exception that nuance became a 200 with an error field.
                var status = outcome.StatusCode >= 400 ? outcome.StatusCode : 502;
                throw new WebServiceStatusException(status, new
                {
                    error = outcome.Error ?? "compliance CSID was not issued",
                    statusCode = outcome.StatusCode,
                });
            }

            return new ZatcaComplianceResponse
            {
                Ok = true,
                StatusCode = outcome.StatusCode,
                RequestID = outcome.RequestId,
                BinarySecurityToken = outcome.BinarySecurityToken,
                Secret = outcome.Secret,
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
}
