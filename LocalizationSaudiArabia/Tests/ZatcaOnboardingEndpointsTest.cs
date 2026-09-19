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
using ZuloOne.Runtime;
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

    // MetaOutboundChannels has no typed accessor; the row is what the claim is
    // about, so it is read directly.
    private static ISqlService Sql => GetService<ISqlService>();

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

    [IntegrationTest("CSR собирается в модели: формат ZATCA, настоящий PKCS#10")]
    public Task CsrIsMintedByThisModel()
    {
        // ZatcaCsr USED to be a kernel type, for one reason only: ECDsa and
        // CertificateRequest come from System.Security.Cryptography, which was
        // absent from the script reference set. It is there now, so the
        // certificate profile of one country — C=SA, the ZATCA template names,
        // the "1-VAT|2-…|3-…" common name — lives with that country.
        var result = ScriptServices.Get<IZatcaCsr>()
            .Create("310122393500003", "ACME KSA", null, null, "sandbox");

        Assert.IsTrue(result["csrPem"].Contains("BEGIN CERTIFICATE REQUEST", StringComparison.Ordinal),
            "настоящий PKCS#10, а не строка-заглушка");
        Assert.IsTrue(result["privateKeyPem"].Contains("PRIVATE KEY", StringComparison.Ordinal),
            "приватный ключ в PEM");
        Assert.IsTrue(result["template"] == "PREZATCA-Code-Signing",
            "песочница использует PREZATCA-шаблон, факт '{0}'", result["template"]);
        Assert.IsTrue(result["commonName"].StartsWith("1-310122393500003|2-", StringComparison.Ordinal),
            "CN по шаблону ZATCA, факт '{0}'", result["commonName"]);
        return Task.CompletedTask;
    }

    [IntegrationTest("Продакшен-шаблон отличается от песочницы")]
    public Task ProductionUsesItsOwnTemplate()
    {
        var prod = ScriptServices.Get<IZatcaCsr>()
            .Create("310122393500003", "ACME KSA", null, null, "production");
        Assert.IsTrue(prod["template"] == "ZATCA-Code-Signing",
            "продакшен без префикса PRE, факт '{0}'", prod["template"]);
        return Task.CompletedTask;
    }

    [IntegrationTest("Каналы Fatoora принадлежат модели, а не настройкам платформы")]
    public async Task ChannelsAreOwnedByThisModel()
    {
        // Four channels naming gw-fatoora.zatca.gov.sa used to sit in the
        // platform's own appsettings.json — one country's addresses compiled
        // into a product that installs everywhere. They are MetaOutboundChannel
        // rows of this model now.
        var rows = await Sql.SelectAsync(
            "SELECT Name, Path, Signer, ModelId FROM MetaOutboundChannels WHERE Name LIKE 'fatoora-%'");

        Assert.IsTrue(rows.Count == 4, "четыре канала на месте; факт {0}", rows.Count);
        foreach (var r in rows)
        {
            Assert.IsTrue(Convert.ToString(r["ModelId"])!.ToLowerInvariant() == SaudiModel.ToString("D"),
                "{0} принадлежит саудовской модели; факт {1}", r["Name"], r["ModelId"]);
            Assert.IsTrue(Convert.ToString(r["Signer"]) == "fatoora",
                "{0} подписывается профилем fatoora, который тоже в этой модели", r["Name"]);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(Convert.ToString(r["Path"])),
                "{0} знает свой путь", r["Name"]);
        }
    }

    [IntegrationTest("Онбординг действительно ходит по каналу и разбирает ответ")]
    public async Task OnboardingReachesTheChannelAndParsesTheReply()
    {
        // End-to-end over the moved path: this model's ZatcaOnboarding builds
        // the JSON, IOutboundCall turns the CHANNEL NAME into a URL, a
        // credential and a signature, and the reply is parsed here. The script
        // names none of those three and could not — HttpClient is outside its
        // reference set and ICredentialResolver is deny-listed.
        var outcome = await ScriptServices.Get<IZatcaOnboarding>()
            .RequestComplianceCsidAsync(
                "123456",
                "-----BEGIN CERTIFICATE REQUEST-----MIIBtest-----END CERTIFICATE REQUEST-----");

        var error = outcome.TryGetValue("error", out var e) ? e : null;
        Assert.IsTrue(string.IsNullOrEmpty(error), "обмен без ошибки; факт '{0}'", error ?? "");
        Assert.IsTrue(outcome.TryGetValue("binarySecurityToken", out var token)
                      && !string.IsNullOrWhiteSpace(token),
            "CSID выдан и разобран");
        Assert.IsTrue(outcome.TryGetValue("status", out var status) && status == "200",
            "статус 200; факт '{0}'", outcome.TryGetValue("status", out var s2) ? s2 : "");
    }

    [IntegrationTest("OTP не попадает в подпись — он одноразовый")]
    public async Task OtpIsRejectedWhenMissing()
    {
        // The stub demands the OTP header, which is exactly the point: it rides
        // on THIS call only and is never handed to the signing profile, because
        // a signed-and-journalled OTP would be replayed after it is spent.
        var threw = false;
        try
        {
            await ScriptServices.Get<IZatcaOnboarding>().RequestComplianceCsidAsync("", "csr");
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        Assert.IsTrue(threw, "пустой OTP отвергается до выхода на канал");
    }

    [IntegrationTest("Ключ не покидает хост: подписывает ICredentialSigner")]
    public Task HostSignsWithoutHandingOverTheKey()
    {
        // The counterpart of the move. Hashing and signing are ordinary script
        // work now; reading a CONFIGURED key is not, and never becomes so.
        // Without a CSID credential on the stand the signer answers null, and
        // the invoice still gets a valid QR with tags 1-6.
        var stamp = ScriptServices.Get<ICredentialSigner>()
            .SignHash("fatoora-csid-absent-on-this-stand",
                Convert.ToBase64String(new byte[32]));
        Assert.IsTrue(stamp == null, "нет сертификата — нет штампа, а не исключение");
        return Task.CompletedTask;
    }
}
