#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// PKCS#10 CSR for a Fatoora CSID. Template names come from the
// Fatoora_Portal_User_Manual: sandbox uses PREZATCA-Code-Signing, simulation
// and production use ZATCA-Code-Signing.
//
// This used to be ZatcaCsr in the kernel, and the reason was never that minting
// a CSR is a platform concern — it is that System.Security.Cryptography was
// absent from the script reference set, so no business script could call
// ECDsa.Create at all. That absence has been fixed, and with it the last
// argument for keeping one country's certificate profile inside a platform that
// ships everywhere. C=SA, the ZATCA template names and the "1-VAT|2-…|3-…"
// common name are Saudi tax format; they belong here.
//
// Nothing here reads a host secret: the keypair is MINTED on the call, and both
// halves go back to the caller. That is also why the web service in front of it
// is developer-stand-only and demands an explicit Execute grant — the response
// carries a private key.
public partial class ZatcaCsr
{
    public const string SandboxTemplate = "PREZATCA-Code-Signing";
    public const string ProductionTemplate = "ZATCA-Code-Signing";

    // Result keys. A service contract can only name types every script can see,
    // so the four outputs travel as one BCL dictionary rather than a class of
    // this model's own — the same reason ZatcaQr returns scalars.
    public const string KeyCsrPem = "csrPem";
    public const string KeyPrivateKeyPem = "privateKeyPem";
    public const string KeyTemplate = "template";
    public const string KeyCommonName = "commonName";

    /// <summary>
    /// Mints a P-256 keypair and the matching CSR. Returns four PEM/text values
    /// under the Key* names above.
    /// </summary>
    public Dictionary<string, string> Create(
        string vatNumber,
        string organization,
        string? organizationUnit = null,
        string? commonName = null,
        string environment = "sandbox")
    {
        if (string.IsNullOrWhiteSpace(vatNumber))
            throw new ArgumentException("VAT number is required", nameof(vatNumber));
        if (string.IsNullOrWhiteSpace(organization))
            throw new ArgumentException("Organization is required", nameof(organization));

        var template = IsProductionTemplate(environment) ? ProductionTemplate : SandboxTemplate;
        var cn = string.IsNullOrWhiteSpace(commonName)
            ? $"1-{vatNumber.Trim()}|2-ZuloOne|3-{Guid.NewGuid():N}"
            : commonName.Trim();
        var ou = string.IsNullOrWhiteSpace(organizationUnit)
            ? organization.Trim()
            : organizationUnit.Trim();

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var dn = new X500DistinguishedName(
            $"C=SA, O={EscapeDn(organization.Trim())}, OU={EscapeDn(ou)}, CN={EscapeDn(cn)}");
        var csr = new CertificateRequest(dn, ecdsa, HashAlgorithmName.SHA256);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KeyCsrPem] = csr.CreateSigningRequestPem(),
            [KeyPrivateKeyPem] = ecdsa.ExportPkcs8PrivateKeyPem(),
            [KeyTemplate] = template,
            [KeyCommonName] = cn,
        };
    }

    /// <summary>
    /// Simulation and production share the production template; only the
    /// sandbox carries the PRE prefix.
    /// </summary>
    public bool IsProductionTemplate(string? environment)
        => environment is not null
           && (environment.Equals("production", StringComparison.OrdinalIgnoreCase)
               || environment.Equals("simulation", StringComparison.OrdinalIgnoreCase)
               || environment.Equals("core", StringComparison.OrdinalIgnoreCase));

    private static string EscapeDn(string value)
        => value.Contains(',') || value.Contains('+') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
