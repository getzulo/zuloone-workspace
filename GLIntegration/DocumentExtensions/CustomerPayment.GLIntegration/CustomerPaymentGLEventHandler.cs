#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Sales моделью GLIntegration: ОПЛАТА ПОКУПАТЕЛЯ попадает в главную
// книгу — Dr денежные средства / Cr дебиторка.
//
// Зачем: выставление счёта дебетует дебиторку (SalesGLEventHandler), расчёт
// налога добавляет к ней сумму НДС (TaxCalculationGLEventHandler) — а кредитовать
// её было нечем. Счёт дебиторки в книге рос на всю выручку с налогом за историю,
// тогда как регистр Receivable гасился оплатой. Это самая дорогая половина того
// же расхождения, что уже закрыто у ФОТ и у фонда соцстраха.
//
// ВАЖНО ПРО СУММУ. Регистр Receivable ведётся БЕЗ налога, а дебиторка в книге —
// С налогом (счёт + нога НДС). Поэтому платёж на полную сумму с налогом закроет
// счёт в книге корректно, но в регистре уведёт остаток в минус на величину
// налога. Пока регистр и книга ведут дебиторку по разным базам, полностью сойтись
// они не могут: закрыть этот шов должен либо НДС в регистре, либо отказ от ноги
// НДС в книге. Здесь закрывается книжная сторона, о регистровой — отдельная
// запись в вики, раздел «Чего пока нет».
//
// ЮРЛИЦО — ПОЛЕМ ШАПКИ: у оплаты нет ни склада, ни счёта-основания, а у клиента
// нет связи с юрлицом. Не задано — платёж гасит регистр как прежде, проводки нет.
public partial class CustomerPaymentGLEventHandler : TypedDocumentEventHandler<CustomerPayment>
{
    public override async Task<EventResult> OnAfterPostAsync(CustomerPayment document, EventContext context)
    {
        if (document.Subtype != "Paid") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(CustomerPayment header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<CustomerPayment>(header.MetaId);
        if (payment == null) return null;
        if (payment.LegalEntity == Guid.Empty) return null;

        var total = payment.Lines.Sum(l => l.Amount);
        if (total <= 0m) return null;

        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(payment.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            payment.DocumentDate, le.MetaId, le.Currency, total,
            settings.CashAccountCode, settings.ArAccountCode,
            "Customer payment " + header.MetaId,
            "Денежные средства", "Дебиторка покупателя");
    }
}
