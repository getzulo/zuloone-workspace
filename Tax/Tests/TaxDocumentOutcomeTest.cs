using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Исход подачи на самом конверте, а не только в журнале рядом.
//
// ЧТО ЭТО ЛОВИТ. Поля TaxDocument.Authority и TaxDocument.RejectionReason были
// объявлены и не заполнялись НИКЕМ: подтип ставился, строка журнала писалась, а
// конверт — первое, что открывает бухгалтер, — про причину отказа молчал.
// Ловится это только проверкой самого конверта: журнал-то как раз писался
// всегда, и тест «есть ли строка в TaxSubmission» был зелёным всё это время.
//
// ВТОРОЕ, ЧТО ЗДЕСЬ ЗАКРЕПЛЕНО, — ПОРЯДОК. Cleared и Reported это подтипы с
// isReadOnly, и охранник пропускает в них только Subtype со StatusValue.
// Значит поля обязаны лечь ДО перехода. Ошибка в порядке не роняет ничего: она
// просто оставляет Authority пустым на принятом конверте.
public class TaxDocumentOutcomeTest : IntegrationTestScriptBase
{
    private static readonly Guid TaxDocumentType = Guid.Parse("cee833d7-fa4c-43fe-bef6-e95633ece906");

    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();
    private static ITaxDocumentOutcome Outcome => GetService<ITaxDocumentOutcome>();

    [IntegrationTest("Отказ органа: причина ложится на конверт, а не только в журнал")]
    public async Task RejectionReasonLandsOnTheEnvelope()
    {
        var env = await SetupAsync();
        var envelope = await IssuedEnvelopeAsync(env.LegalEntity);

        var applied = await Outcome.ApplyAsync(
            envelope.MetaId, "Rejected", "KVT-0001",
            "Документ не прийнято: не заповнено обов'язковий реквізит.");
        Assert.IsTrue(applied, "исход должен примениться");

        var after = await Documents.GetDocumentAsync<TaxDocument>(envelope.MetaId);
        Assert.IsTrue(after!.Subtype == "Rejected", "подтип Rejected, факт {0}", after.Subtype);
        Assert.IsTrue(after.RejectionReason.Contains("не прийнято"),
            "причина отказа обязана быть на конверте, факт «{0}»", after.RejectionReason ?? "");
        Assert.IsTrue(after.Authority == env.Authority,
            "орган обязан быть проставлен из подключения, факт {0}", after.Authority);

        var journal = await Dict.GetRecordsAsync<TaxSubmission>($"SourceId = '{envelope.MetaId}'");
        Assert.IsTrue(journal.Count == 1, "одна строка журнала, факт {0}", journal.Count);
        Assert.IsTrue(journal[0].Status == "Rejected", "журнал говорит Rejected, факт {0}", journal[0].Status);
        Assert.IsTrue(journal[0].Receipt == "KVT-0001", "квитанция в журнале, факт {0}", journal[0].Receipt);
    }

    [IntegrationTest("Приём органа: орган проставлен, причина отказа пуста")]
    public async Task AcceptanceStampsAuthorityAndLeavesNoReason()
    {
        var env = await SetupAsync();
        var envelope = await IssuedEnvelopeAsync(env.LegalEntity);

        var applied = await Outcome.ApplyAsync(envelope.MetaId, "Cleared", "REG-777", "OK");
        Assert.IsTrue(applied, "исход должен примениться");

        var after = await Documents.GetDocumentAsync<TaxDocument>(envelope.MetaId);
        Assert.IsTrue(after!.Subtype == "Cleared", "подтип Cleared, факт {0}", after.Subtype);

        // Вот ради чего порядок: Cleared уже readOnly, и если бы орган писался
        // после перехода, охранник отбросил бы запись молча.
        Assert.IsTrue(after.Authority == env.Authority,
            "орган обязан быть проставлен ДО перехода в readOnly-подтип, факт {0}", after.Authority);
        Assert.IsTrue(string.IsNullOrEmpty(after.RejectionReason),
            "у принятого конверта причины отказа быть не должно, факт «{0}»", after.RejectionReason ?? "");
    }

    [IntegrationTest("Повторная доставка того же исхода ничего не меняет и не плодит журнал")]
    public async Task SecondDeliveryIsIgnored()
    {
        var env = await SetupAsync();
        var envelope = await IssuedEnvelopeAsync(env.LegalEntity);

        await Outcome.ApplyAsync(envelope.MetaId, "Cleared", "REG-777", "OK");

        // Очередь доставляет КАК МИНИМУМ один раз, поэтому второй заход по тому
        // же конверту — норма, а не сбой. Идемпотентность держится на подтипе:
        // войти в Cleared можно только из Issued, и конверт там уже не стоит.
        var again = await Outcome.ApplyAsync(
            envelope.MetaId, "Rejected", "KVT-0002", "Пізніше відхилено");
        Assert.IsTrue(!again, "второй исход по закрытому конверту применяться не должен");

        var after = await Documents.GetDocumentAsync<TaxDocument>(envelope.MetaId);
        Assert.IsTrue(after!.Subtype == "Cleared", "подтип остался Cleared, факт {0}", after.Subtype);
        Assert.IsTrue(string.IsNullOrEmpty(after.RejectionReason),
            "причина отказа не должна была появиться, факт «{0}»", after.RejectionReason ?? "");

        var journal = await Dict.GetRecordsAsync<TaxSubmission>($"SourceId = '{envelope.MetaId}'");
        Assert.IsTrue(journal.Count == 1, "журнал не должен раздваиваться, факт {0}", journal.Count);
    }

    [IntegrationTest("Слишком длинная причина обрезается, а не теряет всю запись")]
    public async Task OverlongReasonIsTrimmed()
    {
        var env = await SetupAsync();
        var envelope = await IssuedEnvelopeAsync(env.LegalEntity);

        // Орган умеет вернуть простыню. Потерять из-за неё ВЕСЬ исход было бы
        // хуже, чем сохранить начало: полный текст остаётся в строке очереди.
        var huge = new string('я', 3000);
        var applied = await Outcome.ApplyAsync(envelope.MetaId, "Rejected", "KVT-0003", huge);
        Assert.IsTrue(applied, "исход должен примениться даже с длинным текстом");

        var after = await Documents.GetDocumentAsync<TaxDocument>(envelope.MetaId);
        Assert.IsTrue(after!.RejectionReason.Length == 1024,
            "причина обрезается по длине колонки, факт {0}", after.RejectionReason.Length);
    }

    // ---- обстановка -------------------------------------------------------

    private async Task<(Guid LegalEntity, Guid Authority)> SetupAsync()
    {
        var currency = Dict.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "₴";
        currency = await Dict.SaveRecordAsync(currency);

        var country = Dict.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = "UA";
        country.CodeISO3 = "UKR";
        country.PhoneCode = "380";
        country = await Dict.SaveRecordAsync(country);

        var entity = Dict.NewRecord<LegalEntity>();
        entity.Name = "ТОВ Конверт";
        entity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await Dict.SaveRecordAsync(entity);

        var authority = Dict.NewRecord<TaxAuthority>();
        authority.Code = $"A{Db.NewId():N}"[..8];
        authority.Name = "Тестовий орган";
        authority.CountryCode = "UA";
        authority.IsActive = true;
        authority = await Dict.SaveRecordAsync(authority);

        var connection = Dict.NewRecord<TaxAuthorityConnection>();
        connection.Authority = authority.MetaId;
        connection.LegalEntity = entity.MetaId;
        connection.Code = $"CONN-{Db.NewId():N}"[..12];
        connection.BaseUrl = "https://operator.example/api";
        await Dict.SaveRecordAsync(connection);

        return (entity.MetaId, authority.MetaId);
    }

    /// <summary>
    /// Конверт в Issued — единственном состоянии, из которого исход применяется.
    /// Собирается руками, а не проведением счёта: проверяется сам сервис, и
    /// тащить сюда продажи значило бы проверять заодно и их.
    /// </summary>
    private async Task<TaxDocument> IssuedEnvelopeAsync(Guid legalEntity)
    {
        var envelope = await Documents.NewDocumentAsync<TaxDocument>();
        envelope.LegalEntity = legalEntity;
        envelope.SourceDocumentType = "SalesRealization";
        envelope.SourceDocumentId = Db.NewId();
        envelope.EInvoiceKind = "INVOICE";
        envelope.Uuid = Db.NewId();
        envelope.Payload = "{}";
        await Documents.SaveDocumentAsync(envelope);

        await GetService<IDocumentPostingService>()
            .SetSubtypeAsync(TaxDocumentType, envelope.MetaId, "Issued");

        return (await Documents.GetDocumentAsync<TaxDocument>(envelope.MetaId))!;
    }
}
