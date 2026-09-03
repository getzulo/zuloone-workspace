#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Tax моделью GLIntegration: ОПЛАТА НАЛОГА попадает в главную
// книгу — Dr НДС к уплате / Cr денежные средства.
//
// Зачем: начисление НДС кредитует счёт обязательства (TaxCalculationGL), а
// дебетовать его было нечем. В леджере налог остаётся начисленным (это факт
// декларации), а в книге обязательство росло бесконечно — то же расхождение,
// что уже закрыто у VendorPayment и PayrollPayment.
//
// ЮРЛИЦО — ПОЛЕМ ШАПКИ. Не задано — платёж проводится как прежде, проводки нет
// (best-effort: отсутствие настройки не ломает существующие документы).
public partial class TaxPaymentGLEventHandler : TypedDocumentEventHandler<TaxPayment>
{
    public override async Task<EventResult> OnAfterPostAsync(TaxPayment document, EventContext context)
    {
        if (document.Subtype != "Paid") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
        {
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

            // Мок госоргана не должен отменять уже разнесённую проводку.
            try
            {
                await context.GetService<ITaxAuthoritySubmitService>().SubmitPaymentAsync(document.MetaId);
            }
            catch
            {
            }
        }

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(TaxPayment header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<TaxPayment>(header.MetaId);
        if (payment == null) return null;
        if (payment.LegalEntity == Guid.Empty) return null;

        var total = payment.Lines.Sum(l => l.Amount);
        if (total <= 0m) return null;

        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(payment.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            payment.DocumentDate, le.MetaId, le.Currency, total,
            settings.VatPayableAccountCode, settings.CashAccountCode,
            "Tax payment " + header.MetaId,
            "НДС к уплате", "Денежные средства",
            "FIN,TAX");
    }
}
