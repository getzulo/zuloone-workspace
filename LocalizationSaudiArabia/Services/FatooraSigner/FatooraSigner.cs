#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using ZuloOne.Core.Services.Integration;

// Fatoora request headers from the portal manual: Accept-Version V2, language,
// optional Clearance-Status, Basic binarySecurityToken:secret.
//
// This used to be ZatcaFatooraSigner in the kernel, and there was never any
// crypto in it — four headers and a Base64. It sat there because a model had no
// way to contribute a signing profile: OutboundSignerRegistry was built once
// from DI at startup. It now also scans model services implementing the
// platform seam IOutboundSigner, so one authority's request format lives with
// that authority's localization.
//
// Implementing a PLATFORM interface is what makes this findable. The generated
// contract IFatooraSigner is irrelevant here — the registry looks for
// IOutboundSigner, and a model service that declares it is picked up by name of
// its Profile, not by the name of the service.
public partial class FatooraSigner : IOutboundSigner
{
    public const string ProfileName = "fatoora";
    public const string AcceptVersion = "V2";

    /// <summary>
    /// One-shot header on the onboarding call. It is NOT added by Sign: an OTP
    /// on a queued invoice would be journalled and retried, and it is valid
    /// once.
    /// </summary>
    public const string OtpHeader = "OTP";

    public string Profile => ProfileName;

    public IReadOnlyDictionary<string, string> Sign(OutboundSigningContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept-Version"] = AcceptVersion,
            ["Accept-Language"] = "en",
        };
        if (context.PathAndQuery.Contains("clearance", StringComparison.OrdinalIgnoreCase))
            headers["Clearance-Status"] = "1";
        if (!string.IsNullOrEmpty(context.Secret))
            headers["Authorization"] = "Basic " + BasicToken(context.Secret);
        return headers;
    }

    /// <summary>
    /// The credential value is <c>binarySecurityToken:secret</c> in plain text;
    /// Basic encoding is UTF-8 Base64 of that pair, matching the Fatoora
    /// examples. The secret arrives on the context — this service never
    /// resolves it, and could not: ICredentialResolver is on the script
    /// deny-list.
    /// </summary>
    public string BasicToken(string tokenAndSecret)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(tokenAndSecret));
}
