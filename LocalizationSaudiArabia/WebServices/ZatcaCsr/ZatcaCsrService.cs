#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.WebServices;
using ZuloOne.Services.Contracts;

// Mints a P-256 keypair and a ZATCA-templated CSR. Returns the PRIVATE KEY — developer stands only, explicit grant required, never persisted.
//
// Moved out of the kernel's ZatcaDevController: a route named after one country
// has no place in a platform that ships everywhere. The crypto followed it —
// this model's own ZatcaCsr service mints the keypair now that
// System.Security.Cryptography is in the script reference set, so nothing about
// ZATCA certificates is left in the kernel.
public partial class ZatcaCsrService
{
    public override Task<object?> Any(ZatcaCsrRequest request)
    {
        try
        {
            // IZatcaCsr is this model's service contract. The name is shared
            // with this web service, but the generated DTO is ZatcaCsrRequest
            // and the service class is ZatcaCsrService, so nothing collides.
            var result = ScriptServices.Get<IZatcaCsr>().Create(
                request.VatNumber ?? "",
                request.Organization ?? "",
                request.OrganizationUnit,
                request.CommonName,
                string.IsNullOrWhiteSpace(request.Environment) ? "sandbox" : request.Environment);

            return Task.FromResult<object?>(new ZatcaCsrResponse
            {
                Ok = true,
                Csr = Value(result, "csrPem"),
                PrivateKey = Value(result, "privateKeyPem"),
                Template = Value(result, "template"),
                CommonName = Value(result, "commonName"),
            });
        }
        catch (ArgumentException ex)
        {
            // The controller answered 400 here; so does this.
            throw new WebServiceStatusException(400, new { error = ex.Message });
        }
    }

    private static string Value(Dictionary<string, string> result, string key)
        => result.TryGetValue(key, out var value) ? value : string.Empty;
}
