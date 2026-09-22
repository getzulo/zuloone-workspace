#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Core.Services.Integration;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Service "UaEInvoice": податкова накладна до ЄРПН — ЧЕРЕЗ ОПЕРАТОРА.
//
// ЦЕПОЧКА ЦІЛКОМ: рахунок проведено → тут постає конверт TaxDocument → черга
// відправлення → оператор (M.E.Doc, кабінет банку) → ДПС → квитанція
// повертається ЗГОДОМ і закриває конверт. Ключ КЕП залишається в оператора,
// ZuloOne не підписує нічого: саме тому ДСТУ 4145 нам і не потрібен, хоча
// ICredentialSigner уміє лише ECDSA.
//
// КОНВЕРТ БЕРЕМО ГОТОВИЙ, СВОГО ДОКУМЕНТА НЕ ЗАВОДИМО. TaxDocument у моделі Tax
// навмисно знеособлений від Саудівської Аравії — у нього вже є підтипи
// Draft→Issued→Cleared|Rejected, дедуплікація за джерелом і журнал подач.
// Другий «український конверт» означав би другу реалізацію тих самих граблів.
//
// ЧОМУ PAYLOAD — ПОЛЯ, А НЕ XML. З оператором облікова система XML не будує:
// вона називає КОД ФОРМИ ДПС і заповнює ПОЛЯ, а XML складає оператор. Тому тут
// не буде жодного рядка розмітки — буде плоский набір фактів накладної.
//
// І ЧЕСНА МЕЖА, ЯКУ ТРЕБА БАЧИТИ. Імена полів у формі ДПС (R01G3, HTIN тощо)
// тут НЕ вигадані і не проставлені. Payload іменує факти по-людськи; зіставити
// їх з полями конкретної форми — робота того, у кого на руках специфікація
// оператора. Вигадане ім'я поля гірше за відсутнє: воно проходить валідацію
// нашого боку і падає вже в кабінеті, коли строк реєстрації спливає.
public partial class UaEInvoice
{
    private const string SendChannel = "erpn-send";
    private static readonly Guid TaxDocumentType = Guid.Parse("cee833d7-fa4c-43fe-bef6-e95633ece906");
    private static readonly Guid SalesRealizationType = Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3");

    /// <summary>
    /// Конверт для проведеного рахунку. Повертає його ідентифікатор або null,
    /// якщо контур вимкнений, режим юрособи не передбачає ПДВ або накладна вже
    /// існує.
    ///
    /// ПОВТОРНИЙ ВИКЛИК НІЧОГО НЕ ДУБЛЮЄ і це не зайва обережність: проведення
    /// відпрацьовує по документу не один раз, а друга накладна на той самий
    /// рахунок — це друга реєстрація в ЄРПН.
    /// </summary>
    public async Task<Guid?> EnsureForInvoiceAsync(Guid salesInvoiceId)
    {
        if (salesInvoiceId == Guid.Empty) return null;

        var settings = (await ScriptServices.Get<IDictionaryManager<LocalizationUkraineSettings>>()
            .GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (settings is null || !settings.EInvoiceEnabled) return null;

        // Код форми — ДАНІ. Порожньо означає «контур не налаштований», і це
        // нормальний стан, а не помилка: вигадати номер форми не можна.
        var formCode = Convert.ToString(settings.TaxInvoiceFormCode) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(formCode)) return null;

        var docs = ScriptServices.Get<IDocumentManager>();
        var invoice = await docs.GetDocumentAsync<SalesRealization>(salesInvoiceId);
        if (invoice is null || invoice.LegalEntity == Guid.Empty) return null;

        // Накладну виписує ЛИШЕ платник ПДВ. Спрощенець на 5% її не складає
        // взагалі — і це та сама ознака, якою вже керується весь контур ПДВ
        // цього пакета.
        var firstEvent = ScriptServices.Get<IUaFirstEvent>();
        if (!firstEvent.ChargesVat(await firstEvent.RegimeOfAsync(invoice.LegalEntity)))
            return null;

        var existing = await docs.QueryDocumentsAsync<TaxDocument>(
            $"SourceDocumentId = '{salesInvoiceId}'");
        if (existing.Count > 0) return existing[0].MetaId;

        var envelope = await docs.NewDocumentAsync<TaxDocument>();
        envelope.LegalEntity = invoice.LegalEntity;
        envelope.SourceDocumentType = "SalesRealization";
        envelope.SourceDocumentId = salesInvoiceId;
        envelope.EInvoiceKind = "INVOICE";
        envelope.InvoiceType = formCode;
        envelope.Uuid = Guid.NewGuid();
        envelope.Payload = await BuildPayloadAsync(invoice, formCode);
        await docs.SaveDocumentAsync(envelope);

        await ScriptServices.Get<IDocumentPostingService>()
            .SetSubtypeAsync(TaxDocumentType, envelope.MetaId, "Issued");

        await EnqueueAsync(envelope.MetaId, envelope.Uuid, envelope.LegalEntity, envelope.Payload);
        return envelope.MetaId;
    }

    /// <summary>
    /// Факти накладної плоским JSON. Іменування — ділове й самоописове саме
    /// тому, що зіставлення з формою ДПС робиться окремо: читач payload має
    /// розуміти, що перед ним, без специфікації.
    /// </summary>
    private async Task<string> BuildPayloadAsync(SalesRealization invoice, string formCode)
    {
        var dict = ScriptServices.Get<IDictionaryManager>();
        var seller = await dict.GetRecordAsync<LegalEntity>(invoice.LegalEntity);
        var buyer = invoice.Customer != Guid.Empty
            ? await dict.GetRecordAsync<Customer>(invoice.Customer)
            : null;

        var text = new StringBuilder();
        text.Append("{\"formCode\":").Append(Quote(formCode));
        text.Append(",\"documentNumber\":").Append(Quote(Convert.ToString(invoice.ID)));
        text.Append(",\"documentDate\":").Append(Quote(invoice.DocumentDate.ToString("yyyy-MM-dd")));
        text.Append(",\"sellerName\":").Append(Quote(seller?.Name));
        text.Append(",\"sellerTaxNumber\":").Append(Quote(Convert.ToString(seller?.TaxRegistrationNumber)));
        text.Append(",\"buyerName\":").Append(Quote(buyer?.Name));
        text.Append(",\"buyerTaxNumber\":").Append(Quote(Convert.ToString(buyer?.TaxRegistrationNumber)));
        text.Append(",\"vatRate\":").Append(Num(invoice.TaxRateApplied));

        text.Append(",\"lines\":[");
        var first = true;
        foreach (var line in invoice.Lines)
        {
            var item = line.Item != Guid.Empty ? await dict.GetRecordAsync<Item>(line.Item) : null;
            if (!first) text.Append(',');
            first = false;
            text.Append("{\"name\":").Append(Quote(item?.Name));
            text.Append(",\"unit\":").Append(Quote(Convert.ToString(line.Unit)));
            text.Append(",\"quantity\":").Append(Num(line.Quantity));
            text.Append(",\"unitPrice\":").Append(Num(line.UnitPrice));
            text.Append(",\"amount\":").Append(Num(line.Quantity * line.UnitPrice));
            text.Append('}');
        }
        text.Append("]}");
        return text.ToString();
    }

    /// <summary>
    /// У чергу. Рядок пишеться в транзакції викликача, а відправляє її окремий
    /// фоновий сервіс — тому відкочене проведення не відправить нічого.
    ///
    /// Ключ ідемпотентності — UUID конверта. Унікальний індекс у черзі саме на
    /// ньому, і саме він, а не наша перевірка, фізично не дає піти другій
    /// подачі тієї самої накладної.
    /// </summary>
    private static async Task EnqueueAsync(Guid envelopeId, Guid uuid, Guid legalEntity, string payload)
    {
        var connectionRef = await ConnectionRefAsync(legalEntity);
        try
        {
            var messageId = await ScriptServices.Get<IOutboundGateway>().EnqueueAsync(new OutboundRequest
            {
                Channel = SendChannel,
                ConnectionRef = connectionRef,
                IdempotencyKey = "erpn:" + uuid.ToString("N"),
                Payload = payload,
                PayloadContentType = "application/json",
                SourceTable = "TaxDocument",
                SourceRecordId = envelopeId,
            });

            await ScriptServices.Get<IDocumentManager>().UpdateDocumentAsync(
                TaxDocumentType, envelopeId,
                new Dictionary<string, object?> { ["ExternalId"] = messageId.ToString("N") });
        }
        catch (Exception ex)
        {
            // Каналу немає, підключення не заведене, або друге з'єднання всередині
            // транзакції проведення. Конверт лишається Issued із заповненим
            // Payload — його можна поставити в чергу пізніше, нічого не втрачено.
            ScriptServices.Get<ZuloOne.Log.IZuloOneLogger<UaEInvoice>>()
                .Warn(ex, "ЄРПН: не вдалося поставити конверт {0} у чергу", envelopeId);
        }
    }

    private static async Task<string?> ConnectionRefAsync(Guid legalEntity)
    {
        if (legalEntity == Guid.Empty) return null;
        var rows = await ScriptServices.Get<IDictionaryManager>()
            .GetRecordsAsync<TaxAuthorityConnection>($"LegalEntity = '{legalEntity}'", take: 1);
        return rows.Count > 0 ? rows[0].Code : null;
    }

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    private static string Num(decimal value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);
}
