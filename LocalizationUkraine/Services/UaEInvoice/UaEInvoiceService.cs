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

// Service "UaEInvoice": податкова накладна до ЄРПН — ЧЕРЕЗ M.E.Doc REST API.
//
// Контракт: https://medoc.ua/faq/opis-metodv-web-api
// Приклад ПН: https://medoc.ua/faq/priklad-stvorennja-pn-za-dopomogoju-medoc-web-api
// Статуси: https://medoc.ua/faq/statusi-dokumenta
// Відповідь GetDocInfo: https://medoc.ua/faq/opis-vdpovd-getdocinfo
//
// ЦЕПОЧКА: рахунок → конверт TaxDocument → черга MakeDoc → CardCode → черга
// ToGov (КЕП залишається в M.E.Doc на ПК) → квитанцію ЗАБИРАЄМО GetDocInfo.
// ZuloOne XML і ДСТУ не будує.
//
// Імена клітинок — з офіційного прикладу MakeDoc (FIRM_NAME, TAB1_A13, …),
// не вигадані R01G3. Клітин, яких у прикладі ПН немає (поля бланка РК), сюди
// не додаємо.
public partial class UaEInvoice
{
    private const string SendChannel = "erpn-send";
    private const string GovChannel = "erpn-gov";
    private const string ReceiptChannel = "erpn-receipt";
    private static readonly Guid TaxDocumentType = Guid.Parse("cee833d7-fa4c-43fe-bef6-e95633ece906");

    public Task<Guid?> EnsureForInvoiceAsync(Guid salesInvoiceId)
        => EnsureAsync(salesInvoiceId, "SalesRealization", "INVOICE");

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
        string? documentNumber;
        DateTime documentDate;
        string? edrpou;
        string? inn;

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
            documentNumber = Convert.ToString(note.ID);
            documentDate = note.DocumentDate;
            var seller = await SellerAsync(legalEntity);
            edrpou = seller.Edrpou;
            inn = seller.Inn;
            payload = await BuildMakeDocAsync(
                kind, formCode, documentNumber, documentDate,
                seller, note.Customer, note.TaxRateApplied,
                note.Lines.Select(l => (l.Item, (string?)null, l.Quantity, l.UnitPrice)),
                Convert.ToString(original.ID), original.DocumentDate);
        }
        else
        {
            var invoice = await docs.GetDocumentAsync<SalesRealization>(sourceId);
            if (invoice is null || invoice.LegalEntity == Guid.Empty) return null;

            var firstEvent = ScriptServices.Get<IUaFirstEvent>();
            if (!firstEvent.ChargesVat(await firstEvent.RegimeOfAsync(invoice.LegalEntity)))
                return null;

            legalEntity = invoice.LegalEntity;
            documentNumber = Convert.ToString(invoice.ID);
            documentDate = invoice.DocumentDate;
            var seller = await SellerAsync(legalEntity);
            edrpou = seller.Edrpou;
            inn = seller.Inn;
            payload = await BuildMakeDocAsync(
                kind, formCode, documentNumber, documentDate,
                seller, invoice.Customer, invoice.TaxRateApplied,
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

        await EnqueueMakeDocAsync(envelope.MetaId, envelope.Uuid, envelope.LegalEntity,
            formCode, edrpou ?? inn ?? "", payload);
        return envelope.MetaId;
    }

    public async Task<int> ApplyOutcomesAsync()
    {
        var gateway = ScriptServices.Get<IOutboundGateway>();
        var outcome = ScriptServices.Get<ITaxDocumentOutcome>();
        var docs = ScriptServices.Get<IDocumentManager>();

        var applied = 0;
        var ack = new List<Guid>();
        foreach (var status in await gateway.TakeCompletedAsync(SendChannel, 100))
        {
            if (string.Equals(status.Status, "InDoubt", StringComparison.Ordinal))
                continue;

            if (string.Equals(status.Status, "Succeeded", StringComparison.Ordinal))
            {
                var body = status.ResponseBody ?? "";
                var remoteCode = JsonToken(body, "Code");
                if (!string.IsNullOrEmpty(remoteCode) && remoteCode != "0")
                {
                    if (status.SourceRecordId is Guid failed && failed != Guid.Empty
                        && await outcome.ApplyAsync(
                            failed, "Rejected",
                            status.ExternalReference ?? status.MessageId.ToString("N"),
                            JsonToken(body, "Message") is { Length: > 0 } msg ? msg : body))
                        applied++;
                    ack.Add(status.MessageId);
                    continue;
                }

                var cardCode = CardCodeOf(body) ?? status.ExternalReference;
                if (status.SourceRecordId is Guid id && id != Guid.Empty
                    && !string.IsNullOrWhiteSpace(cardCode))
                {
                    await docs.UpdateDocumentAsync(
                        TaxDocumentType, id,
                        new Dictionary<string, object?> { ["ExternalId"] = cardCode });
                    var envelope = await docs.GetDocumentAsync<TaxDocument>(id);
                    if (envelope is not null)
                        await EnqueueToGovAsync(id, envelope.Uuid, envelope.LegalEntity, cardCode);
                }
                ack.Add(status.MessageId);
                continue;
            }

            if (status.SourceRecordId is Guid rejectedId && rejectedId != Guid.Empty
                && await outcome.ApplyAsync(
                    rejectedId, "Rejected",
                    status.ExternalReference ?? status.MessageId.ToString("N"),
                    status.LastError ?? status.Status))
                applied++;

            ack.Add(status.MessageId);
        }

        foreach (var status in await gateway.TakeCompletedAsync(GovChannel, 100))
        {
            if (string.Equals(status.Status, "InDoubt", StringComparison.Ordinal))
                continue;
            if (string.Equals(status.Status, "Succeeded", StringComparison.Ordinal))
            {
                ack.Add(status.MessageId);
                continue;
            }
            if (status.SourceRecordId is Guid govFail && govFail != Guid.Empty
                && await outcome.ApplyAsync(
                    govFail, "Rejected",
                    status.ExternalReference ?? status.MessageId.ToString("N"),
                    status.LastError ?? status.Status))
                applied++;
            ack.Add(status.MessageId);
        }

        if (ack.Count > 0) await gateway.AcknowledgeAsync(ack);
        return applied;
    }

    public async Task<int> PullReceiptsAsync()
    {
        var docs = ScriptServices.Get<IDocumentManager>();
        var pending = await docs.QueryDocumentsAsync<TaxDocument>("Subtype = 'Issued'");

        var applied = 0;
        foreach (var envelope in pending)
        {
            if (!IsUaEnvelope(envelope)) continue;
            if (await AskOperatorAsync(envelope.MetaId)) applied++;
        }
        return applied;
    }

    public Task<bool> ResolveInDoubtAsync(Guid envelopeId)
        => AskOperatorAsync(envelopeId);

    private static bool IsUaEnvelope(TaxDocument envelope)
    {
        if (envelope.EInvoiceKind == "INVOICE" || envelope.EInvoiceKind == "ADJUSTMENT")
            return true;
        var payload = Convert.ToString(envelope.Payload) ?? "";
        return payload.Contains("\"contour\":\"erpn\"", StringComparison.Ordinal)
            || payload.Contains("\"NAME\":\"FIRM_NAME\"", StringComparison.Ordinal);
    }

    private static async Task<bool> AskOperatorAsync(Guid envelopeId)
    {
        var docs = ScriptServices.Get<IDocumentManager>();
        var envelope = await docs.GetDocumentAsync<TaxDocument>(envelopeId);
        if (envelope is null || envelope.Subtype != "Issued") return false;

        var connectionRef = await ConnectionRefAsync(envelope.LegalEntity);
        var call = ScriptServices.Get<IOutboundCall>();

        try
        {
            await call.SendAsync(
                ReceiptChannel, "[]",
                idempotencyKey: "erpn-mail:" + envelope.Uuid.ToString("N"),
                connectionRef: connectionRef,
                headers: PathHeader("/api/SignSendReceive/ReceiveCorrespondence"));
        }
        catch (Exception ex)
        {
            ScriptServices.Get<ZuloOne.Log.IZuloOneLogger<UaEInvoice>>()
                .Warn(ex, "ЄРПН: ReceiveCorrespondence не вдався для {0}", envelopeId);
        }

        var query = "?exDocId=" + EscapeQuery(envelope.Uuid.ToString());
        var card = Convert.ToString(envelope.ExternalId);
        if (!string.IsNullOrWhiteSpace(card) && card.All(char.IsDigit))
            query += "&cardCode=" + EscapeQuery(card);

        var info = await TryCallAsync(
            call, connectionRef, query, "/api/Info/GetDocInfo",
            "erpn-kvt:" + envelope.Uuid.ToString("N"), envelopeId,
            "ЄРПН: не вдалося запитати GetDocInfo по конверту {0}") ?? "";
        var kvt = await TryCallAsync(
            call, connectionRef, query, "/api/Info/GetDocKVT",
            "erpn-kvt-list:" + envelope.Uuid.ToString("N"), envelopeId,
            "ЄРПН: GetDocKVT не вдався для {0}");
        if (string.IsNullOrWhiteSpace(info) && string.IsNullOrWhiteSpace(kvt))
            return false;

        var sendStt = JsonToken(info, "SENDSTT");
        var status = JsonToken(info, "STATUS");
        var target = TargetFromMedoc(sendStt, status);
        if (target is null && !string.IsNullOrWhiteSpace(kvt))
        {
            foreach (var operType in JsonTokens(kvt, "OPERTYPE"))
            {
                var decided = TargetFromMedoc(operType, "");
                if (decided != null) target = decided;
            }
        }

        if (target is null) return false;

        var reason = FirstNonEmpty(
            string.IsNullOrWhiteSpace(kvt) ? "" : JsonToken(kvt, "REASON"),
            string.IsNullOrWhiteSpace(kvt) ? "" : JsonToken(kvt, "KVT_TEXT"),
            JsonToken(info, "SENDSTTNAME"),
            JsonToken(info, "STATUSNAME"),
            JsonToken(info, "NOTATION"));
        var registration = FirstNonEmpty(
            string.IsNullOrWhiteSpace(kvt) ? "" : JsonToken(kvt, "REGNUM"),
            string.IsNullOrWhiteSpace(kvt) ? "" : JsonToken(kvt, "REGDATE"),
            JsonToken(info, "REGDATE"),
            CardCodeOf(info) ?? "",
            envelope.Uuid.ToString("N"));

        return await ScriptServices.Get<ITaxDocumentOutcome>().ApplyAsync(
            envelopeId, target, registration,
            string.IsNullOrWhiteSpace(reason) ? sendStt : reason);
    }

    private static async Task<string?> TryCallAsync(
        IOutboundCall call, string? connectionRef, string query, string path,
        string idempotencyKey, Guid envelopeId, string warn)
    {
        try
        {
            var result = await call.SendAsync(
                ReceiptChannel, "[]",
                idempotencyKey: idempotencyKey,
                connectionRef: connectionRef,
                headers: PathHeader(path + query));
            return result.Ok ? result.Body : null;
        }
        catch (Exception ex)
        {
            ScriptServices.Get<ZuloOne.Log.IZuloOneLogger<UaEInvoice>>()
                .Warn(ex, warn, envelopeId);
            return null;
        }
    }

    /// <summary>
    /// SendStt з https://medoc.ua/faq/statusi-dokumenta :
    /// 9 здано, 111 СМКОР зареєстрував — Cleared;
    /// 10 не прийнято, 16 відхилено, 112 СМКОР відмова — Rejected.
    /// 7/8 «отримана квитанція» ще не рішення ДПС.
    /// </summary>
    private static string? TargetFromMedoc(string sendStt, string status)
    {
        if (sendStt is "9" or "111") return "Cleared";
        if (sendStt is "10" or "16" or "112") return "Rejected";
        if (status == "3") return "Rejected";
        var old = sendStt switch
        {
            "registered" or "accepted" => "Cleared",
            "rejected" or "refused" => "Rejected",
            _ => (string?)null,
        };
        if (old != null) return old;
        return status switch
        {
            "registered" or "accepted" => "Cleared",
            "rejected" or "refused" => "Rejected",
            _ => null,
        };
    }

    private async Task<(string? Edrpou, string? Inn, string? Name, string? Address)> SellerAsync(Guid legalEntity)
    {
        var seller = await ScriptServices.Get<IDictionaryManager>().GetRecordAsync<LegalEntity>(legalEntity);
        return (
            Convert.ToString(seller?.RegistrationNumber),
            Convert.ToString(seller?.TaxRegistrationNumber),
            seller?.Name,
            Convert.ToString(seller?.LegalAddress));
    }

    /// <summary>
    /// Тіло MakeDoc: масив {TAB, LINE, NAME, VALUE}.
    /// ПН — https://medoc.ua/faq/priklad-stvorennja-pn-za-dopomogoju-medoc-web-api
    /// РК — https://medoc.ua/faq/priklad-stvorennja-rk-do-pn-za-dopomogoju-medoc-web-api
    /// charCode форми тенанта їде в query, не сюди.
    /// </summary>
    private async Task<string> BuildMakeDocAsync(
        string kind, string formCode, string? documentNumber, DateTime documentDate,
        (string? Edrpou, string? Inn, string? Name, string? Address) seller, Guid customerId, decimal vatRate,
        IEnumerable<(Guid Item, string? Unit, decimal Quantity, decimal UnitPrice)> lines,
        string? originalNumber = null, DateTime? originalDate = null)
    {
        var dict = ScriptServices.Get<IDictionaryManager>();
        var buyer = customerId != Guid.Empty
            ? await dict.GetRecordAsync<Customer>(customerId)
            : null;

        var percent = vatRate <= 1m ? vatRate * 100m : vatRate;
        var rateFactor = vatRate <= 1m ? vatRate : vatRate / 100m;
        var cells = new List<string>();
        var adjustment = kind == "ADJUSTMENT";

        void H(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            cells.Add(Cell(0, 0, name, value));
        }

        H("FIRM_NAME", seller.Name);
        H("FIRM_EDRPOU", seller.Edrpou);
        H("EDRPOU", seller.Edrpou);
        H("FIRM_INN", seller.Inn);
        H("INN", seller.Inn);
        H("FIRM_ADR", seller.Address);
        H("N3", buyer?.Name);
        H("EDR_POK", Convert.ToString(buyer?.TaxRegistrationNumber));
        H("N4", Convert.ToString(buyer?.TaxRegistrationNumber));
        H("N5", Convert.ToString(buyer?.Address));
        H("NAKL_TYPE", "1");
        H("KB", "1");
        H("KS", "1");

        if (adjustment)
        {
            H("N15", MedocDate(documentDate));
            if (originalDate is DateTime srcDate && !string.IsNullOrWhiteSpace(originalNumber))
                H("CORRCMPL", srcDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + "//" + originalNumber);
            H("N1_13", originalNumber);
            if (originalDate is DateTime src) H("N2", MedocDate(src));
        }
        else
        {
            H("SN", documentNumber);
            H("N11", MedocDate(documentDate));
            H("N2_1", documentDate.ToString("ddMM", CultureInfo.InvariantCulture));
            H("N2_11", documentDate.ToString("ddMM", CultureInfo.InvariantCulture));
            H("N2_1I", documentDate.ToString("ddMM", CultureInfo.InvariantCulture));
            H("CHECKRVS", "1");
            H("VER", "1");
        }

        decimal baseSum = 0m, vatSum = 0m;
        var lineNo = 0;
        foreach (var line in lines)
        {
            var item = line.Item != Guid.Empty ? await dict.GetRecordAsync<Item>(line.Item) : null;
            var qty = adjustment ? -line.Quantity : line.Quantity;
            var amount = qty * line.UnitPrice;
            var vat = amount * rateFactor;
            baseSum += amount;
            vatSum += vat;

            void L(string name, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                cells.Add(Cell(1, lineNo, name, value));
            }

            if (adjustment)
            {
                // TAB1_A01=1 — «зміна кількості» з прикладу РК. Кредит-нота
                // сторнує кількість; ціну не вигадуємо.
                L("TAB1_A", (lineNo + 1).ToString(CultureInfo.InvariantCulture));
                L("TAB1_A01", "1");
                L("TAB1_A011", Num(percent));
                L("TAB1_A013", Num(amount));
                L("TAB1_A020", Num(vat));
                L("TAB1_A22", "1");
                L("TAB1_A3", item?.Name);
                L("TAB1_A4", line.Unit);
                L("TAB1_A5", Num(qty));
                L("TAB1_A6", Num(line.UnitPrice));
            }
            else
            {
                L("TAB1_A1", (lineNo + 1).ToString(CultureInfo.InvariantCulture));
                L("TAB1_A13", item?.Name);
                L("TAB1_A14", line.Unit);
                L("TAB1_A15", Num(qty));
                L("TAB1_A16", Num(line.UnitPrice));
                L("TAB1_A10", Num(amount));
                L("TAB1_A8", Num(percent));
                L("TAB1_A20", Num(vat));
            }
            lineNo++;
        }

        if (adjustment)
        {
            if (baseSum != 0m) H("A1_9", Num(baseSum));
            if (vatSum != 0m)
            {
                H("A2_9", Num(vatSum));
                H("A2_92", Num(vatSum));
            }
        }
        else
        {
            if (baseSum != 0m) H("A5_7", Num(baseSum));
            if (vatSum != 0m)
            {
                H("A6_7", Num(vatSum));
                H("A6_11", Num(vatSum));
            }
            if (baseSum + vatSum != 0m) H("A7_11", Num(baseSum + vatSum));
        }

        _ = formCode;
        return "[" + string.Join(",", cells) + "]";
    }

    private static string MedocDate(DateTime date)
        => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + " 0:00:00";

    private static string Cell(int tab, int line, string name, string value)
        => "{\"TAB\":" + tab.ToString(CultureInfo.InvariantCulture)
            + ",\"LINE\":" + line.ToString(CultureInfo.InvariantCulture)
            + ",\"NAME\":" + Quote(name)
            + ",\"VALUE\":" + Quote(value) + "}";

    private static async Task EnqueueMakeDocAsync(
        Guid envelopeId, Guid uuid, Guid legalEntity, string formCode, string edrpou, string payload)
    {
        var connection = await ConnectionAsync(legalEntity);
        var userLogin = connection is null
            ? ""
            : FirstNonEmpty(connection.Value.CredentialRef, connection.Value.Code);
        var path = "/api/Document/MakeDoc"
            + "?userLogin=" + EscapeQuery(userLogin)
            + "&edrpou=" + EscapeQuery(edrpou)
            + "&shablonType=1"
            + "&charCode=" + EscapeQuery(formCode)
            + "&exDocId=" + EscapeQuery(uuid.ToString());
        await EnqueueAsync(SendChannel, envelopeId, uuid, connection?.Code, payload, "erpn:", path);
    }

    private static Task EnqueueToGovAsync(Guid envelopeId, Guid uuid, Guid legalEntity, string cardCode)
        => EnqueueOnConnectionAsync(GovChannel, envelopeId, uuid, legalEntity, "[]", "erpn-gov:",
            "/api/SignSendReceive/ToGov?cardCode=" + EscapeQuery(cardCode));

    private static async Task EnqueueOnConnectionAsync(
        string channel, Guid envelopeId, Guid uuid, Guid legalEntity,
        string payload, string keyPrefix, string relativePath)
    {
        var connectionRef = await ConnectionRefAsync(legalEntity);
        await EnqueueAsync(channel, envelopeId, uuid, connectionRef, payload, keyPrefix, relativePath);
    }

    private static async Task EnqueueAsync(
        string channel, Guid envelopeId, Guid uuid, string? connectionRef,
        string payload, string keyPrefix, string relativePath)
    {
        try
        {
            var messageId = await ScriptServices.Get<IOutboundGateway>().EnqueueAsync(new OutboundRequest
            {
                Channel = channel,
                ConnectionRef = connectionRef,
                IdempotencyKey = keyPrefix + uuid.ToString("N"),
                Payload = payload,
                PayloadContentType = "application/json",
                SourceTable = "TaxDocument",
                SourceRecordId = envelopeId,
                Headers = PathHeader(relativePath),
            });

            if (channel == SendChannel)
            {
                await ScriptServices.Get<IDocumentManager>().UpdateDocumentAsync(
                    TaxDocumentType, envelopeId,
                    new Dictionary<string, object?> { ["ExternalId"] = messageId.ToString("N") });
            }
        }
        catch (Exception ex)
        {
            ScriptServices.Get<ZuloOne.Log.IZuloOneLogger<UaEInvoice>>()
                .Warn(ex, "ЄРПН: не вдалося поставити конверт {0} у чергу {1}", envelopeId, channel);
        }
    }

    private static Dictionary<string, string> PathHeader(string relativePath)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["X-ZuloOne-RelativePath"] = relativePath,
        };

    private static async Task<string?> ConnectionRefAsync(Guid legalEntity)
        => (await ConnectionAsync(legalEntity))?.Code;

    private static async Task<(string Code, string? CredentialRef)?> ConnectionAsync(Guid legalEntity)
    {
        if (legalEntity == Guid.Empty) return null;
        var rows = await ScriptServices.Get<IDictionaryManager>()
            .GetRecordsAsync<TaxAuthorityConnection>($"LegalEntity = '{legalEntity}'", take: 1);
        return rows.Count == 0 ? null : (rows[0].Code, rows[0].CredentialRef);
    }

    private static string? CardCodeOf(string json)
    {
        var name = JsonToken(json, "Name");
        if (string.Equals(name, "CardCode", StringComparison.OrdinalIgnoreCase))
            return JsonToken(json, "Value");
        var card = JsonToken(json, "CARDCODE");
        return string.IsNullOrEmpty(card) ? null : card;
    }

    private static string JsonToken(string json, string key, int from = 0)
    {
        var marker = "\"" + key + "\"";
        var at = json.IndexOf(marker, from, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return string.Empty;

        var colon = json.IndexOf(':', at + marker.Length);
        if (colon < 0) return string.Empty;

        var i = colon + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        if (i >= json.Length || json[i] == 'n') return string.Empty;

        if (json[i] == '"')
        {
            var sb = new StringBuilder();
            i++;
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\' && i + 1 < json.Length) i++;
                sb.Append(json[i]);
                i++;
            }
            return sb.ToString();
        }

        var end = i;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] is '.' or '-'))
            end++;
        return json[i..end];
    }

    /// <summary>
    /// GetDocKVT — таблиця квитанцій (OPERTYPE як SendStt). Беремо всі,
    /// останнє рішення перемагає: 7 потім 9 — Cleared.
    /// </summary>
    private static IEnumerable<string> JsonTokens(string json, string key)
    {
        var marker = "\"" + key + "\"";
        var from = 0;
        while (from < json.Length)
        {
            var at = json.IndexOf(marker, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) yield break;
            var value = JsonToken(json, key, at);
            if (value.Length > 0) yield return value;
            from = at + marker.Length;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v!;
        return "";
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

    /// <summary>
    /// Скрипт не має System.Uri (політика пісочниці). Кодуємо query RFC 3986.
    /// </summary>
    private static string EscapeQuery(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '-' or '_' or '.' or '~')
                sb.Append(c);
            else
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
