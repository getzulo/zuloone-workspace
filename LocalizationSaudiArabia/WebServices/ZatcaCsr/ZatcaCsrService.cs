#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Core.Services.Integration;
using ZuloOne.Runtime;
using ZuloOne.Runtime.WebServices;

// Mints a P-256 keypair and a ZATCA-templated CSR. Returns the PRIVATE KEY — developer stands only, explicit grant required, never persisted.
//
// Moved out of the kernel's ZatcaDevController: a route named after one country
// has no place in a platform that ships everywhere. The capability stayed —
// crypto, secrets and the wire are still the host's — only the surface is here.
public partial class ZatcaCsrService
{
    public override Task<object?> Any(ZatcaCsrRequest request)
    {
        // ZatcaCsr lives in the host: ECDsa, CertificateRequest and PEM export
        // all come from System.Security.Cryptography, which is NOT in the script
        // reference set. Its SIGNATURE is crypto-free, so the call compiles here
        // while the crypto stays compiled into the kernel — the same arrangement
        // SaudiEInvoice uses for the invoice hash.
        try
        {
            // Fully qualified: this service is named ZatcaCsr, so its own
            // generated DTO is ZatcaCsrRequest and shadows the host type of
            // the same name.
            var result = ZatcaCsr.Create(new ZuloOne.Core.Services.Integration.ZatcaCsrRequest
            {
                VatNumber = request.VatNumber ?? "",
                Organization = request.Organization ?? "",
                OrganizationUnit = request.OrganizationUnit,
                CommonName = request.CommonName,
                Environment = string.IsNullOrWhiteSpace(request.Environment) ? "sandbox" : request.Environment,
            });
            return Task.FromResult<object?>(new ZatcaCsrResponse
            {
                Ok = true,
                Csr = result.CsrPem,
                PrivateKey = result.PrivateKeyPem,
                Template = result.Template,
                CommonName = result.CommonName,
            });
        }
        catch (ArgumentException ex)
        {
            // The controller answered 400 here; so does this.
            throw new WebServiceStatusException(400, new { error = ex.Message });
        }
    }
}
