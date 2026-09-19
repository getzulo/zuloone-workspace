#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.WebServices;
using ZuloOne.Services.Contracts;

// Builds and posts the six ZATCA compliance sample kinds. Developer stands
// only, explicit grant required. Does not persist CSID secrets.
public partial class ZatcaComplianceSamplesService
{
    public override async Task<object?> Any(ZatcaComplianceSamplesRequest request)
    {
        try
        {
            var outcomes = await ScriptServices.Get<IZatcaComplianceSamples>()
                .SubmitAllAsync(request.ConnectionRef);
            var accepted = outcomes.Count(kv => kv.Value == "200");
            var failed = outcomes.Where(kv => kv.Value != "200").ToList();
            var results = string.Join(";", outcomes.Select(kv => kv.Key + "=" + kv.Value));
            if (failed.Count > 0)
            {
                throw new WebServiceStatusException(502, new
                {
                    error = "not all six kinds were accepted",
                    accepted,
                    results,
                });
            }

            return new ZatcaComplianceSamplesResponse
            {
                Ok = true,
                StatusCode = 200,
                Accepted = accepted,
                Results = results,
                Hint = "Live Fatoora still needs a real Compliance CSID PEM so the samples carry XAdES. Do not commit secrets.",
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
