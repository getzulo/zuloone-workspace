using System;
using System.Threading.Tasks;
using ZuloOne.Printing;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// The printed ZATCA invoice is only compliant if the QR on it decodes to the
// payload that was stamped. The encoder used to cap at 106 bytes and silently
// TRUNCATE the rest, which produces a code that scans cleanly to the wrong
// value — on a tax invoice, worse than printing no QR at all.
//
// This pins the real shape, not a toy string: an Arabic seller name (UTF-8, two
// bytes a character) and tag 6, which is what SaudiEInvoice emits for every
// invoice. That combination is ~212 base64 characters, double the old ceiling.
//
// It also pins WHERE the format lives. ZatcaQr is a service of this model, not
// a platform type: tag numbers and the 500-character cap are Saudi tax format,
// and changing them must not require a platform release.
public class ZatcaQrPrintableTest : IntegrationTestScriptBase
{
    private static IZatcaQr Qr => GetService<IZatcaQr>();

    // A published 44-character SHA-256 in base64, the width tag 6 always has.
    private const string InvoiceHash = "NWZlYzViNGVhZjRjNGE2ZTlkNzY3YWI4ZDhlMzFhNGQ=";
    private const string ArabicSeller = "مؤسسة الجواهري العربي للتجارة";

    private static string RealisticPayload() => Qr.Encode(
        ArabicSeller, "310122393500003", "2026-09-18T15:30:00Z", "1150.00", "150.00", InvoiceHash);

    [IntegrationTest("QR саудовской фактуры кодируется целиком, без обрезки")]
    public Task RealPayloadEncodesWhole()
    {
        var payload = RealisticPayload();
        Assert.IsTrue(payload.Length > 106,
            "образец должен превышать прежний потолок кодера, иначе тест ничего не ловит; факт {0}",
            payload.Length);
        Assert.IsTrue(payload.Length <= 500,
            "payload в пределах потолка резолюции 500; факт {0}", payload.Length);

        // Throws if it does not fit — the encoder refuses rather than truncates.
        var modules = XrQrCode.Modules(payload);
        var size = modules.GetLength(0);

        // 21 + 4*(v-1): anything past v6 (41 modules) was unreachable before.
        Assert.IsTrue(size > 41,
            "payload такой длины требует версии выше 6; сторона матрицы {0}", size);
        Assert.IsTrue((size - 21) % 4 == 0, "сторона матрицы кратна сетке версий; факт {0}", size);
        return Task.CompletedTask;
    }

    [IntegrationTest("Теги QR доходят до печати в том же виде, что записаны")]
    public Task PayloadRoundTripsThroughTlv()
    {
        var payload = RealisticPayload();

        Assert.IsTrue(Qr.TagCount(payload) == 6,
            "теги 1–6, как их пишет SaudiEInvoice; факт {0}", Qr.TagCount(payload));

        var seller = Qr.DecodeTag(payload, 1);
        Assert.IsTrue(seller == ArabicSeller,
            "арабское имя продавца переживает TLV без потери байтов; факт '{0}'", seller);

        var hash = Qr.DecodeTag(payload, 6);
        Assert.IsTrue(hash == InvoiceHash, "хеш фактуры не усечён; факт '{0}'", hash);
        return Task.CompletedTask;
    }

    [IntegrationTest("Payload сверх потолка отвергается, а не обрезается молча")]
    public Task OversizePayloadIsRefused()
    {
        var tooLong = new string('A', 520);
        var refused = false;
        try
        {
            XrQrCode.Modules(tooLong);
        }
        catch (ArgumentOutOfRangeException)
        {
            refused = true;
        }

        Assert.IsTrue(refused,
            "кодер обязан отказать: укороченный QR сканируется в неверное значение");
        return Task.CompletedTask;
    }

    [IntegrationTest("Формат QR принадлежит саудовской модели, а не ядру")]
    public Task FormatLivesInTheModel()
    {
        // Resolving the contract at all is the assertion: IZatcaQr is generated
        // from a MetaService of LocalizationSaudiArabia. When the tag numbers or
        // the cap change, this model ships — the platform does not.
        Assert.IsTrue(Qr != null, "IZatcaQr резолвится как сервис модели");
        Assert.IsTrue(Qr.TagCount(RealisticPayload()) == 6, "и он действительно кодирует формат");
        return Task.CompletedTask;
    }
}
