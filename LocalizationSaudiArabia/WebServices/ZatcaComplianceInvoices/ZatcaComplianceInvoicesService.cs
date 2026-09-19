#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.WebServices;
using ZuloOne.Services.Contracts;

// Posts one compliance sample invoice. Developer stands only, explicit grant required.
//
// Live Fatoora will not issue a Production CSID until this path has accepted
// the six document kinds. The stand stub accepts a well-formed payload.
// Building the six kinds is ZatcaComplianceSamples.
public partial class ZatcaComplianceInvoicesService
{
    public override async Task<object?> Any(ZatcaComplianceInvoicesRequest request)
    {
        try
        {
            var outcome = await ScriptServices.Get<IZatcaOnboarding>()
                .SubmitComplianceInvoiceAsync(
                    request.Uuid ?? "",
                    request.InvoiceHash ?? "",
                    request.Invoice ?? "",
                    request.ConnectionRef);

            var status = int.TryParse(Value(outcome, "status"), out var parsed) ? parsed : 0;
            var error = Value(outcome, "error");
            if (!string.IsNullOrEmpty(error))
            {
                throw new WebServiceStatusException(status >= 400 ? status : 502, new
                {
                    error = error ?? "compliance sample invoice was not accepted",
                    statusCode = status,
                });
            }

            return new ZatcaComplianceInvoicesResponse
            {
                Ok = true,
                StatusCode = status,
                RequestID = Value(outcome, "requestId"),
                ReportingStatus = Value(outcome, "reportingStatus"),
                ClearanceStatus = Value(outcome, "clearanceStatus"),
                Hint = "Or POST /api/rest/ZatcaComplianceSamples to send all six kinds. Live Fatoora still needs a CSID PEM for XAdES. Do not commit secrets.",
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
