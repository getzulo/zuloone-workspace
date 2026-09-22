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
    private const string ReceiptChannel = "erpn-receipt";
    private static readonly Guid TaxDocumentType = Guid.Parse("cee833d7-fa4c-43fe-bef6-e95633ece906");

    /// <summary>
    /// Конверт для проведеного рахунку. Повертає його ідентифікатор або null,
    /// якщо контур вимкнений, режим юрособи не передбачає ПДВ або накладна вже
    /// існує.
    ///
    /// ПОВТОРНИЙ ВИКЛИК НІЧОГО НЕ ДУБЛЮЄ і це не зайва обережність: проведення
    /// відпрацьовує по документу не один раз, а друга накладна на той самий
    /// рахунок — це друга реєстрація в ЄРПН.
    /// </summary>
    public Task<Guid?> EnsureForInvoiceAsync(Guid salesInvoiceId)
        => EnsureAsync(salesInvoiceId, "SalesRealization", "INVOICE");

    /// <summary>
    /// Розрахунок коригування до проведеної кредит-ноти. Окрема форма ДПС,
    /// не «накладна зі знаком мінус»: код форми інший, і без посилання на
    /// початковий рахунок ДПС такий документ не прийме.
    /// </summary>
    public Task<Guid?> EnsureForCreditNoteAsync(Guid creditNoteId)
        => EnsureAsync(creditNoteId, "SalesCreditNote", "ADJUSTMENT");

    private async Task<Guid?> EnsureAsync(Guid sourceId, string sourceType, string kind)
    {
        if (sourceId == Guid.Empty) return null;

        var settings = (await ScriptServices.Get<IDictionaryManager<LocalizationUkraineSettings>>()
            .GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (settings is null || !settings.EInvoiceEnabled) return null;

        var formCode = kind == "ADJUSTMENT"
            ? Convert.ToString(settings.TaxInvoiceAdjustmentFormCode) ?? string.Empty
            : Convert.ToString(settings.TaxInvoiceFormCode) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(formCode)) return null;

        var docs = ScriptServices.Get<IDocumentManager>();
        Guid legalEntity;
        string payload;

        if (sourceType == "SalesCreditNote")
        {
            var note = await docs.GetDocumentAsync<SalesCreditNote>(sourceId);
            if (note is null || note.LegalEntity == Guid.Empty) return null;
            if (note.OriginalInvoice == Guid.Empty) return null;

            var firstEvent = ScriptServices.Get<IUaFirstEvent>();
            if (!firstEvent.ChargesVat(await firstEvent.RegimeOfAsync(note.LegalEntity)))
                return null;

            var original = await docs.GetDocumentAsync<SalesRealization>(note.OriginalInvoice);
            if (original is null) return null;

            legalEntity = note.LegalEntity;
            payload = await BuildPayloadAsync(
                kind, formCode, Convert.ToString(note.ID), note.DocumentDate,
                note.LegalEntity, note.Customer, note.TaxRateApplied,
                Convert.ToString(original.ID), original.DocumentDate,
                note.Lines.Select(l => (l.Item, (string?)null, l.Quantity, l.UnitPrice)));
        }
        else
        {
            var invoice = await docs.GetDocumentAsync<SalesRealization>(sourceId);
            if (invoice is null || invoice.LegalEntity == Guid.Empty) return null;

            var firstEvent = ScriptServices.Get<IUaFirstEvent>();
            if (!firstEvent.ChargesVat(await firstEvent.RegimeOfAsync(invoice.LegalEntity)))
                return null;

            legalEntity = invoice.LegalEntity;
            payload = await BuildPayloadAsync(
                kind, formCode, Convert.ToString(invoice.ID), invoice.DocumentDate,
                invoice.LegalEntity, invoice.Customer, invoice.TaxRateApplied,
                null, null,
                invoice.Lines.Select(l => (l.Item, Convert.ToString(l.Unit), l.Quantity, l.UnitPrice)));
        }

        var existing = await docs.QueryDocumentsAsync<TaxDocument>(
            $"SourceDocumentId = '{sourceId}'");
        if (existing.Count > 0) return existing[0].MetaId;

        var envelope = await docs.NewDocumentAsync<TaxDocument>();
        envelope.LegalEntity = legalEntity;
        envelope.SourceDocumentType = sourceType;
        envelope.SourceDocumentId = sourceId;
        envelope.EInvoiceKind = kind;
        envelope.InvoiceType = formCode;
        envelope.Uuid = Guid.NewGuid();
        envelope.Payload = payload;
        await docs.SaveDocumentAsync(envelope);

        await ScriptServices.Get<IDocumentPostingService>()
            .SetSubtypeAsync(TaxDocumentType, envelope.MetaId, "Issued");

        await EnqueueAsync(envelope.MetaId, envelope.Uuid, envelope.LegalEntity,
            Convert.ToString(envelope.Payload) ?? string.Empty);
        return envelope.MetaId;
    }

    /// <summary>
    /// Розібрати ВЛАСНУ чергу: що сталося з відправленням. Повертає кількість
    /// застосованих ісходів.
    ///
    /// І ТУТ ГОЛОВНА ВІДМІННІСТЬ ВІД САУДІВСЬКОГО КОНТУРУ. Успішна відправка —
    /// це НЕ реєстрація. Оператор відповів «прийняв до обробки», а квитанція
    /// від ДПС прийде згодом, окремою подією. Тому на успіху конверт лишається
    /// Issued і чекає квитанції; закривають його PullReceiptsAsync або
    /// ResolveInDoubtAsync. Переведи його тут у Cleared — і в системі буде
    /// зареєстрована накладна, якої ДПС не бачила.
    /// </summary>
    public async Task<int> ApplyOutcomesAsync()
    {
        var gateway = ScriptServices.Get<IOutboundGateway>();
        var outcome = ScriptServices.Get<ITaxDocumentOutcome>();

        var applied = 0;
        var ack = new List<Guid>();
        foreach (var status in await gateway.TakeCompletedAsync(SendChannel, 100))
        {
            // InDoubt свідомо НЕ підтверджуємо: запит пішов, відповіді немає, і
            // підтвердити його тут означало б поховати. Хай повертається, доки
            // ResolveInDoubtAsync не з'ясує долю накладної в оператора.
            if (string.Equals(status.Status, "InDoubt", StringComparison.Ordinal))
                continue;

            if (string.Equals(status.Status, "Succeeded", StringComparison.Ordinal))
            {
                ack.Add(status.MessageId);
                continue; // прийнято до обробки — чекаємо квитанцію
            }

            if (status.SourceRecordId is Guid id && id != Guid.Empty
                && await outcome.ApplyAsync(
                    id, "Rejected",
                    status.ExternalReference ?? status.MessageId.ToString("N"),
                    status.LastError ?? status.Status))
                applied++;

            ack.Add(status.MessageId);
        }

        if (ack.Count > 0) await gateway.AcknowledgeAsync(ack);
        return applied;
    }

    /// <summary>
    /// Запитати в оператора долю ще не закритих накладних. Це і є той «другий
    /// бік», якого в платформі немає: вхідного виклику ззовні не існує, і
    /// квитанцію ніхто нам не принесе — її треба ЗАБРАТИ.
    /// </summary>
    public async Task<int> PullReceiptsAsync()
    {
        var docs = ScriptServices.Get<IDocumentManager>();
        var pending = await docs.QueryDocumentsAsync<TaxDocument>("Subtype = 'Issued'");

        var applied = 0;
        foreach (var envelope in pending)
        {
            if (envelope.SourceDocumentType != "SalesRealization"
                && envelope.SourceDocumentType != "SalesCreditNote") continue;
            var payload = Convert.ToString(envelope.Payload) ?? string.Empty;
            if (!payload.Contains("\"contour\":\"erpn\"", StringComparison.Ordinal)) continue;
            if (await AskOperatorAsync(envelope.MetaId)) applied++;
        }
        return applied;
    }

    /// <summary>
    /// Розв'язати «відправлено, відповіді немає» — ЗАПИТОМ, а не повторною
    /// подачею.
    ///
    /// ЧОМУ ПОВТОРИТИ НЕ МОЖНА. InDoubt означає, що запит пішов у дріт, а
    /// відповідь не повернулася: накладна могла зареєструватися. Сліпий повтор
    /// у такому стані — це друга реєстрація тієї самої накладної, а це вже
    /// штраф, а не незручність. Платформа тому й відмовляє в retry для InDoubt.
    /// Єдиний правильний хід — спитати, що там сталося.
    /// </summary>
    public Task<bool> ResolveInDoubtAsync(Guid envelopeId)
        => AskOperatorAsync(envelopeId);

    /// <summary>
    /// Один запит до оператора про одну накладну.
    ///
    /// ФОРМАТ ВІДПОВІДІ — НЕЙТРАЛЬНИЙ КОНТРАКТ, І ЦЕ ЧЕСНА МЕЖА, А НЕ ЛІНОЩІ.
    /// Очікується {"status":"...","registrationNumber":"...","reason":"..."}.
    /// У кожного оператора свій формат (у M.E.Doc це взагалі XML-квитанція), і
    /// вгадувати його не можна: розбір, зроблений «на око», мовчки визнає
    /// накладну зареєстрованою там, де ДПС її відхилила. Оператор з іншою
    /// відповіддю потребує адаптера РІВНО ТУТ — місце для нього одне й видиме.
    ///
    /// externalReferenceField черги тут не допоміг би: він дістає лише поле
    /// верхнього рівня з JSON, а квитанції бувають XML.
    /// </summary>
    private static async Task<bool> AskOperatorAsync(Guid envelopeId)
    {
        var docs = ScriptServices.Get<IDocumentManager>();
        var envelope = await docs.GetDocumentAsync<TaxDocument>(envelopeId);
        if (envelope is null || envelope.Subtype != "Issued") return false;

        var connectionRef = await ConnectionRefAsync(envelope.LegalEntity);
        var request = "{\"uuid\":\"" + envelope.Uuid.ToString("N") + "\"}";

        OutboundCallResult result;
        try
        {
            result = await ScriptServices.Get<IOutboundCall>().SendAsync(
                ReceiptChannel, request,
                idempotencyKey: "erpn-kvt:" + envelope.Uuid.ToString("N"),
                connectionRef: connectionRef);
        }
        catch (Exception ex)
        {
            // Оператор недоступний — це не привід чіпати конверт. Він лишається
            // Issued і запитається наступного разу.
            ScriptServices.Get<ZuloOne.Log.IZuloOneLogger<UaEInvoice>>()
                .Warn(ex, "ЄРПН: не вдалося запитати квитанцію по конверту {0}", envelopeId);
            return false;
        }

        if (!result.Ok || string.IsNullOrWhiteSpace(result.Body)) return false;

        var status = JsonValue(result.Body!, "status");
        var registration = JsonValue(result.Body!, "registrationNumber");
        var reason = JsonValue(result.Body!, "reason");

        // Невідомий статус НЕ означає відмову. «Ще в обробці» і «відхилено» —
        // різні речі, і мовчазне перетворення першого на друге зняло б накладну
        // з реєстрації без жодної підстави.
        var target = status switch
        {
            "registered" or "accepted" => "Cleared",
            "rejected" or "refused" => "Rejected",
            _ => null,
        };
        if (target is null) return false;

        return await ScriptServices.Get<ITaxDocumentOutcome>().ApplyAsync(
            envelopeId, target,
            string.IsNullOrWhiteSpace(registration) ? envelope.Uuid.ToString("N") : registration,
            string.IsNullOrWhiteSpace(reason) ? status : reason);
    }

    /// <summary>
    /// Значення поля верхнього рівня з пласкої JSON-відповіді. Свій розбір, бо
    /// повноцінний парсер скриптам недоступний, а відповідь тут за контрактом
    /// пласка. Не знайшли — порожньо, і викликач сам вирішує, що це значить.
    /// </summary>
    private static string JsonValue(string json, string key)
    {
        var marker = "\"" + key + "\"";
        var at = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return string.Empty;

        var colon = json.IndexOf(':', at + marker.Length);
        if (colon < 0) return string.Empty;

        var open = json.IndexOf('"', colon + 1);
        if (open < 0) return string.Empty;

        var close = open + 1;
        var sb = new StringBuilder();
        while (close < json.Length && json[close] != '"')
        {
            if (json[close] == '\\' && close + 1 < json.Length) close++;
            sb.Append(json[close]);
            close++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Факти накладної плоским JSON. Іменування — ділове й самоописове саме
    /// тому, що зіставлення з формою ДПС робиться окремо: читач payload має
    /// розуміти, що перед ним, без специфікації.
    /// contour=erpn відрізняє наш конверт від саудівського TaxDocument на
    /// спільному стенді: опит квитанції інакше спитав би Fatoora в оператора ЄРПН.
    /// </summary>
    private async Task<string> BuildPayloadAsync(
        string kind, string formCode, string? documentNumber, DateTime documentDate,
        Guid legalEntity, Guid customerId, decimal vatRate,
        string? originalNumber, DateTime? originalDate,
        IEnumerable<(Guid Item, string? Unit, decimal Quantity, decimal UnitPrice)> lines)
    {
        var dict = ScriptServices.Get<IDictionaryManager>();
        var seller = await dict.GetRecordAsync<LegalEntity>(legalEntity);
        var buyer = customerId != Guid.Empty
            ? await dict.GetRecordAsync<Customer>(customerId)
            : null;

        var text = new StringBuilder();
        text.Append("{\"contour\":\"erpn\"");
        text.Append(",\"kind\":").Append(Quote(kind));
        text.Append(",\"formCode\":").Append(Quote(formCode));
        text.Append(",\"documentNumber\":").Append(Quote(documentNumber));
        text.Append(",\"documentDate\":").Append(Quote(documentDate.ToString("yyyy-MM-dd")));
        if (originalNumber != null)
        {
            text.Append(",\"originalDocumentNumber\":").Append(Quote(originalNumber));
            text.Append(",\"originalDocumentDate\":").Append(Quote(
                originalDate?.ToString("yyyy-MM-dd") ?? string.Empty));
        }
        text.Append(",\"sellerName\":").Append(Quote(seller?.Name));
        text.Append(",\"sellerTaxNumber\":").Append(Quote(Convert.ToString(seller?.TaxRegistrationNumber)));
        text.Append(",\"buyerName\":").Append(Quote(buyer?.Name));
        text.Append(",\"buyerTaxNumber\":").Append(Quote(Convert.ToString(buyer?.TaxRegistrationNumber)));
        text.Append(",\"vatRate\":").Append(Num(vatRate));

        text.Append(",\"lines\":[");
        var first = true;
        foreach (var line in lines)
        {
            var item = line.Item != Guid.Empty ? await dict.GetRecordAsync<Item>(line.Item) : null;
            if (!first) text.Append(',');
            first = false;
            text.Append("{\"name\":").Append(Quote(item?.Name));
            text.Append(",\"unit\":").Append(Quote(line.Unit));
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
