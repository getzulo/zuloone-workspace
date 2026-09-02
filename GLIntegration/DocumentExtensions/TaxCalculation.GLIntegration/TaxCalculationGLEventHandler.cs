#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Tax моделью GLIntegration: ИСХОДЯЩИЙ НДС становится обязательством
// в главной книге — Dr дебиторка / Cr НДС к уплате.
//
// ═══ ПОЧЕМУ ИМЕННО ЭТОТ ДОКУМЕНТ ИСТОЧНИК ИСТИНЫ ════════════════════════════
//
// Налог в системе считают ДВА независимых механизма: универсальный движок правил
// (этот документ → регистр TaxLedger) и страновой контур КСА (SaudiVatTx →
// регистр VatPayable). В книгу разносится ТОЛЬКО первый.
//
// Причина не в предпочтениях: страновой скрипт сам объявляет себя срезом для
// отчётности ZATCA поверх универсального контура. Провести оба — удвоить
// обязательство по НДС в главной книге на ровном месте. Универсальный движок
// выбран источником истины потому, что он общий: он работает в любой стране, а
// страновой контур существует не везде.
//
// ═══ ПОЧЕМУ ДЕБЕТУЕТСЯ ДЕБИТОРКА ════════════════════════════════════════════
//
// Счёт продажи разносит Dr дебиторка / Cr выручка на сумму БЕЗ налога — и
// регистр Receivable тоже ведётся без него. Полная проводка продажи с НДС
// выглядит как Dr дебиторка (с налогом) / Cr выручка (без) / Cr НДС. Эта
// проводка добавляет недостающие две ноги: доводит дебиторку до суммы с налогом
// и создаёт обязательство. Итог тот же, что у трёхногой проводки, но счёт
// продажи трогать не пришлось.
//
// ═══ ВХОДЯЩИЙ НДС ЗДЕСЬ НЕ РАЗНОСИТСЯ ═══════════════════════════════════════
//
// Строки с направлением INPUT (закупки) сознательно пропущены. Их учёт зависит
// от возместимости (поля Recoverability/RecoverablePct на строке): возместимая
// часть — это актив «НДС к возмещению», невозместимая обязана лечь в стоимость
// запаса или в расход. Это отдельное учётное решение и отдельные счета, а не
// зеркало этой проводки. Пока входящий налог живёт только в TaxLedger.
public partial class TaxCalculationGLEventHandler : TypedDocumentEventHandler<TaxCalculation>
{
    private const string OutputDirection = "OUTPUT";

    public override async Task<EventResult> OnAfterPostAsync(TaxCalculation document, EventContext context)
    {
        if (document.Subtype != "Finalized") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(TaxCalculation header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;
        if (string.IsNullOrWhiteSpace(settings.VatPayableAccountCode)) return null;

        var calc = await context.GetService<IDocumentManager>().GetDocumentAsync<TaxCalculation>(header.MetaId);
        if (calc == null) return null;

        // Направление строки — ССЫЛКА на справочник TaxDirection, а не строка:
        // сравнивать надо резолвнутый Code, как это делает TaxReturnService.
        var directions = context.GetService<IDictionaryManager<TaxDirection>>();
        var tax = 0m;
        foreach (var line in calc.Lines)
        {
            var code = (await directions.GetRecordAsync(line.Direction))?.Code;
            if (string.Equals(code, OutputDirection, StringComparison.OrdinalIgnoreCase))
                tax += line.TaxAmount;
        }
        if (tax <= 0m) return null;

        // Юрлицо у расчёта в шапке: его зафиксировал счёт продажи при выставлении.
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(header.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            calc.DocumentDate, le.MetaId, le.Currency, tax,
            settings.ArAccountCode, settings.VatPayableAccountCode,
            "Output VAT " + header.MetaId,
            "Дебиторка (налог с покупателя)", "НДС к уплате");
    }
}
