#nullable enable
using System;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// JournalEntry is the foundation of double-entry: a posting is accepted only when it
// balances. This guard makes «trial balance sums to zero» an invariant of the ledger
// rather than a report-time check.
//
// The header event receives the document WITHOUT its table-part rows loaded, so the
// lines are re-loaded through IDocumentManager (the sanctioned pattern for reading
// table parts in an event handler).
public partial class JournalEntryEventHandler : TypedDocumentEventHandler<JournalEntry>
{
    /// <summary>
    /// Курс документа к функциональной валюте юрлица, штампуется на КАЖДОМ
    /// сохранении — переход подтипа тоже сохранение, а иначе поле не доедет до
    /// транзакционного скрипта: OnBeforePost шапку не пишет, и tx читает
    /// снимок ДО проведения. Тот же приём, что у PurchaseOrder.DueDate.
    ///
    /// ЗАЧЕМ ВООБЩЕ. До этого JournalEntry.Currency не читал НИКТО: GLPostingTx
    /// клал line.Debit в книгу как есть. Поставь валюту EUR, введи 100 — в
    /// леджер уезжало 100 гривен. Поле было объявлено и молча врало, как
    /// когда-то TaxDocument.RejectionReason.
    ///
    /// Валюта совпала с функциональной — курс 1, поведение прежнее. Поэтому
    /// правка обратно совместима: все существующие вызовы
    /// IGeneralLedgerService.PostAsync передают валюту юрлица.
    /// </summary>
    public override async Task<EventResult> OnBeforeSaveAsync(JournalEntry document, bool isNew, EventContext context)
    {
        var prior = await next(document, isNew, context);
        if (!prior.Success) return prior;

        // Событие обновления несёт ТОЛЬКО пишущиеся колонки, поэтому валюту,
        // юрлицо и дату добираем из перечитанного документа, если их нет.
        var currency = document.Currency;
        var legalEntity = document.LegalEntity;
        var date = document.DocumentDate;
        if (!isNew && (currency == Guid.Empty || legalEntity == Guid.Empty || date == default))
        {
            var stored = await context.GetService<IDocumentManager>()
                .GetDocumentAsync<JournalEntry>(document.MetaId);
            if (stored != null)
            {
                if (currency == Guid.Empty) currency = stored.Currency;
                if (legalEntity == Guid.Empty) legalEntity = stored.LegalEntity;
                if (date == default) date = stored.DocumentDate;
            }
        }

        document.ExchangeRate = await FactorAsync(currency, legalEntity, date, context);
        return EventResult.Ok();
    }

    /// <summary>
    /// Множитель к функциональной валюте. Ноль означает «курса на эту дату
    /// нет» — его ловит OnBeforePost, потому что иначе в книгу уехали бы нули.
    /// </summary>
    private static async Task<decimal> FactorAsync(
        Guid currency, Guid legalEntity, DateTime documentDate, EventContext context)
    {
        if (legalEntity == Guid.Empty) return 1m;

        var entity = await context.GetService<IDictionaryManager<LegalEntity>>()
            .GetRecordAsync(legalEntity);
        var functional = entity?.Currency ?? Guid.Empty;

        // Валюта не задана или совпала — пересчитывать нечего. Одновалютный
        // тенант не обязан заводить курс самому себе.
        if (currency == Guid.Empty || functional == Guid.Empty || currency == functional) return 1m;

        var on = documentDate == default ? DateTime.UtcNow.Date : documentDate.Date;
        var factor = await context.GetService<ICurrencyRateService>()
            .FactorAsync(currency, functional, on);
        return factor ?? 0m;
    }

    public override async Task<EventResult> OnBeforePostAsync(JournalEntry document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<JournalEntry>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Проводка без строк не проводится");

        decimal debit = 0m, credit = 0m;
        foreach (var line in lines)
        {
            if (line.Debit != 0m && line.Credit != 0m)
                return EventResult.Cancel("Строка не может быть одновременно дебетовой и кредитовой");
            debit += line.Debit;
            credit += line.Credit;
        }

        if (debit != credit)
            return EventResult.Cancel($"Проводка не сбалансирована: дебет {debit} ≠ кредит {credit}");

        var date = document.DocumentDate == default ? DateTime.UtcNow.Date : document.DocumentDate.Date;
        var closed = await context.GetService<IFiscalPeriodService>().ClosedReasonAsync(date);
        if (closed != null) return EventResult.Cancel(closed);

        // Курс ноль = валюта документа чужая, а окна на эту дату нет. Провести
        // нельзя: без множителя в книгу уехала бы сумма в ЧУЖОЙ валюте, и
        // разошлась бы не проводка, а весь леджер. Отказ называет дату, потому
        // что курс заводят именно на неё.
        var stamped = full?.ExchangeRate ?? document.ExchangeRate;
        if (stamped <= 0m)
            return EventResult.Cancel(
                $"Нет курса валюты документа к валюте юрлица на {date:yyyy-MM-dd}. "
                + "Заведите курс в справочнике «Курсы валют» или поставьте валюту юрлица.");

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeUnpostAsync(JournalEntry document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        var date = document.DocumentDate == default ? DateTime.UtcNow.Date : document.DocumentDate.Date;
        var closed = await context.GetService<IFiscalPeriodService>().ClosedReasonAsync(date);
        if (closed != null) return EventResult.Cancel(closed);

        return EventResult.Ok();
    }
}
