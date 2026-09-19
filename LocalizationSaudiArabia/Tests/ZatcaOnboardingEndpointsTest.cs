using System;
using System.Linq;
using System.Threading.Tasks;
// A test script is not a PARTIAL script, so RuntimeCompiler prepends no global
// usings to it — every namespace it needs has to be named here. And it must be
// THIS one: ZuloOne.Metadata declares a second, legacy IMetadataService with no
// web-service accessors, so importing that namespace binds the name silently to
// the wrong interface.
using ZuloOne.Core.Services;
using ZuloOne.Core.Services.Integration;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// The ZATCA onboarding endpoints used to be ZatcaDevController in the kernel —
// a route named after one country compiled into a platform that ships
// everywhere. They are now web services of THIS model.
//
// What must not silently regress is the posture, not just the plumbing. These
// endpoints mint a private key and return CSID secrets, so they are
// developer-stand-only and refuse until someone has deliberately granted
// Execute. A future edit that flips either flag turns a developer tool into an
// open door, and nothing else in the tree would notice.
public class ZatcaOnboardingEndpointsTest : IntegrationTestScriptBase
{
    private static IMetadataService Metadata => GetService<IMetadataService>();

    private static readonly Guid SaudiModel = Guid.Parse("8d2f5a41-9c6b-4e3f-8a7d-1b4c6e2f9a50");
    private static readonly Guid CsrService = Guid.Parse("d747898f-fba4-4f51-a533-5716dc582b66");
    private static readonly Guid ComplianceService = Guid.Parse("385d939a-bc36-430a-a1af-b10fd6af7157");
    private static readonly Guid ProductionCsidService = Guid.Parse("5e327b4d-01d9-4dcf-848f-5ccd38b194e8");

    [IntegrationTest("Онбординг ZATCA живёт в модели, а не в ядре")]
    public async Task EndpointsAreOwnedByThisModel()
    {
        foreach (var id in new[] { CsrService, ComplianceService, ProductionCsidService })
        {
            var ws = await Metadata.GetWebServiceAsync(id);
            Assert.IsTrue(ws != null, "веб-сервис {0} существует", id);
            Assert.IsTrue(ws!.ModelId == SaudiModel,
                "владелец — LocalizationSaudiArabia, факт {0}", ws.ModelId);
            Assert.IsTrue(ws.ScriptMetaId != null, "у {0} есть тело", ws.Name);

            // The generated DTOs come from the designed parameters, so an empty
            // contract is not a cosmetic loss — it is the endpoint's signature
            // gone. A designer PUT that omits "parameters" replaces the list
            // wholesale, which is exactly how it would happen.
            var parameters = (await Metadata.GetWebServiceParametersAsync(id)).ToList();
            Assert.IsTrue(parameters.Any(p => !p.IsResponse), "у {0} есть запрос", ws.Name);
            Assert.IsTrue(parameters.Any(p => p.IsResponse), "у {0} есть ответ", ws.Name);
        }
    }

    [IntegrationTest("Эндпоинты с секретами закрыты: dev-стенд и явный грант")]
    public async Task SecretBearingEndpointsStayGated()
    {
        foreach (var id in new[] { CsrService, ComplianceService, ProductionCsidService })
        {
            var ws = await Metadata.GetWebServiceAsync(id);
            Assert.IsTrue(ws != null, "веб-сервис {0} существует", id);

            // A CSR response carries a PKCS#8 private key; the other two carry a
            // CSID secret. Neither may be reachable on a production stand.
            Assert.IsTrue(ws!.IsDevOnly, "{0} обязан быть dev-only: он отдаёт ключ или секрет", ws.Name);
            Assert.IsTrue(ws.RequireExplicitRoles, "{0} обязан требовать явный грант Execute", ws.Name);
            Assert.IsTrue(ws.IsEnabled, "{0} включён", ws.Name);
        }
    }

    [IntegrationTest("CSR собирается хостом: формат ZATCA, настоящий PKCS#10")]
    public Task CsrIsMintedByTheHost()
    {
        // ZatcaCsr stays in the kernel because ECDsa and CertificateRequest come
        // from System.Security.Cryptography, which is NOT in the script
        // reference set. Its signature is crypto-free, which is exactly why the
        // web service in this model can call it.
        var result = ZatcaCsr.Create(new ZatcaCsrRequest
        {
            VatNumber = "310122393500003",
            Organization = "ACME KSA",
            Environment = "sandbox",
        });

        Assert.IsTrue(result.CsrPem.Contains("BEGIN CERTIFICATE REQUEST", StringComparison.Ordinal),
            "настоящий PKCS#10, а не строка-заглушка");
        Assert.IsTrue(result.PrivateKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal),
            "приватный ключ в PEM");
        Assert.IsTrue(result.Template == "PREZATCA-Code-Signing",
            "песочница использует PREZATCA-шаблон, факт '{0}'", result.Template);
        Assert.IsTrue(result.CommonName.StartsWith("1-310122393500003|2-", StringComparison.Ordinal),
            "CN по шаблону ZATCA, факт '{0}'", result.CommonName);
        return Task.CompletedTask;
    }

    [IntegrationTest("Продакшен-шаблон отличается от песочницы")]
    public Task ProductionUsesItsOwnTemplate()
    {
        var prod = ZatcaCsr.Create(new ZatcaCsrRequest
        {
            VatNumber = "310122393500003",
            Organization = "ACME KSA",
            Environment = "production",
        });
        Assert.IsTrue(prod.Template == "ZATCA-Code-Signing",
            "продакшен без префикса PRE, факт '{0}'", prod.Template);
        return Task.CompletedTask;
    }
}
