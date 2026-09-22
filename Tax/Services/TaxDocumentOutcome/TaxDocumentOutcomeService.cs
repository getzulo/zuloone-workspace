#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

// Service "TaxDocumentOutcome": исход подачи ложится на конверт РОВНО ЗДЕСЬ.
//
// ЗАЧЕМ ОТДЕЛЬНЫЙ СЕРВИС. Конверт TaxDocument принадлежит модели Tax, а
// записывают в него исход страновые пакеты — сегодня саудовский, завтра
// украинский. Пока каждый делал это сам, получилось ровно то, чего и следовало
// ждать: подтип ставился, строка журнала писалась, а два поля самого конверта —
// Authority и RejectionReason — не заполнял НИКТО. Они были объявлены и мертвы.
//
// ЧЕМ ЭТО ПЛОХО НА ПРАКТИКЕ. Причина отказа жила только в TaxSubmission и в
// строке очереди. Бухгалтер открывает отклонённую накладную и видит подтип
// «Відхилено» без единого слова почему; чтобы узнать причину, надо знать про
// существование журнала подач и уметь его сопоставить. Один исход был размазан
// по трём объектам, и тот из них, который человек открывает первым, молчал.
//
// ПОРЯДОК ЗАПИСИ ЗДЕСЬ НЕ СЛУЧАЕН. Cleared и Reported — подтипы с isReadOnly:
// после перехода в них документ закрыт, и охранник пропустит только Subtype и
// StatusValue. Поэтому поля пишутся ДО перехода, пока конверт ещё Issued.
// Поменяй порядок — и Authority на успешно поданном конверте останется пустым,
// причём молча: запись не упадёт, её просто отклонит охранник.
public partial class TaxDocumentOutcome
{
    private static readonly Guid TaxDocumentType = Guid.Parse("cee833d7-fa4c-43fe-bef6-e95633ece906");

    /// <summary>Подтипы, означающие «орган принял»: конверт закрыт и больше не меняется.</summary>
    private static bool IsAccepted(string target)
        => target is "Cleared" or "Reported";

    /// <summary>
    /// Положить исход на конверт: орган, причина отказа, подтип и строка
    /// журнала. Возвращает false, если применять нечего — конверта нет, он не в
    /// Issued (значит исход уже применён, и второй раз не надо) или переход
    /// неизвестен.
    ///
    /// ИДЕМПОТЕНТНОСТЬ ДЕРЖИТСЯ НА ПОДТИПЕ, а не на флаге: единственный вход в
    /// Cleared/Reported/Rejected — из Issued, и он же единственное состояние,
    /// из которого мы работаем. Повторная доставка того же исхода (а очередь
    /// доставляет как минимум один раз) застаёт конверт уже закрытым и молча
    /// проходит мимо.
    /// </summary>
    public async Task<bool> ApplyAsync(
        Guid envelopeId, string target, string receipt, string? message)
    {
        if (envelopeId == Guid.Empty || string.IsNullOrWhiteSpace(target)) return false;
        if (!IsAccepted(target) && target != "Rejected") return false;

        var docs = ScriptServices.Get<IDocumentManager>();
        var envelope = await docs.GetDocumentAsync<TaxDocument>(envelopeId);
        if (envelope is null || envelope.Subtype != "Issued") return false;

        var connection = await ConnectionOfAsync(envelope.LegalEntity);

        // Орган пишем и на успехе тоже. Соблазн заполнять его только при отказе
        // понятен и неверен: «кто принял» нужно ровно так же, как «кто отказал»,
        // а у юрлица может быть не один орган.
        var header = new Dictionary<string, object?>();
        if (envelope.Authority == Guid.Empty && connection?.Authority is Guid authority && authority != Guid.Empty)
            header["Authority"] = authority;

        // Причина — только у отказа, и обрезается по длине колонки. Орган умеет
        // вернуть простыню; потерять из-за неё ВСЮ запись было бы хуже, чем
        // сохранить начало. Полный текст остаётся в строке очереди.
        if (!IsAccepted(target))
            header["RejectionReason"] = Trim(message, 1024);

        if (header.Count > 0)
            await docs.UpdateDocumentAsync(TaxDocumentType, envelopeId, header);

        await ScriptServices.Get<IDocumentPostingService>()
            .SetSubtypeAsync(TaxDocumentType, envelopeId, target);

        await JournalAsync(envelope, connection, target, receipt, message);
        return true;
    }

    /// <summary>
    /// Append-only журнал подач. Остаётся отдельным объектом и после того, как
    /// конверт научился хранить исход: конверт держит ПОСЛЕДНЕЕ состояние, а
    /// подач по одному документу бывает несколько — отказ, исправление, приём.
    /// Историю по конверту не восстановить.
    /// </summary>
    private static async Task JournalAsync(
        TaxDocument envelope, TaxAuthorityConnection? connection,
        string target, string receipt, string? message)
    {
        var dict = ScriptServices.Get<IDictionaryManager>();
        var row = dict.NewRecord<TaxSubmission>();
        row.Kind = "EINVOICE";
        row.SourceId = envelope.MetaId;
        row.LegalEntity = envelope.LegalEntity;
        row.Authority = connection?.Authority ?? envelope.Authority;
        row.ConnectionCode = connection?.Code ?? string.Empty;
        row.Environment = connection == null ? string.Empty : connection.Environment.ToString();
        row.SubmittedAt = DateTime.UtcNow;
        row.Status = IsAccepted(target) ? "Accepted" : "Rejected";
        row.Receipt = Trim(receipt, 128) ?? string.Empty;
        row.ResponseMessage = Trim(message, 1024) ?? string.Empty;
        await dict.SaveRecordAsync(row);
    }

    /// <summary>
    /// Подключение юрлица. Пусто — не ошибка: мок и тесты работают без него, и
    /// тогда орган с кодом подключения просто остаются незаполненными.
    /// </summary>
    private static async Task<TaxAuthorityConnection?> ConnectionOfAsync(Guid legalEntity)
    {
        if (legalEntity == Guid.Empty) return null;
        var rows = await ScriptServices.Get<IDictionaryManager>()
            .GetRecordsAsync<TaxAuthorityConnection>($"LegalEntity = '{legalEntity}'", take: 1);
        return rows.FirstOrDefault();
    }

    private static string? Trim(string? value, int max)
        => string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);
}
