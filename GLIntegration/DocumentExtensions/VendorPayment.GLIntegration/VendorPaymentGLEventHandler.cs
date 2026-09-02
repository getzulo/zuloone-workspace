#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Purchasing моделью GLIntegration: ОПЛАТА ПОСТАВЩИКУ попадает в
// главную книгу — Dr кредиторка / Cr денежные средства.
//
// Зачем: оприходование заказа кредитует счёт кредиторки (PurchaseGLEventHandler),
// а дебетовать его было нечем. В регистре Payable долг гасился оплатой, а в книге
// рос бесконечно — ровно то расхождение, которое уже закрыто у выплаты ФОТ и у
// платежа в фонд соцстраха. Документ оплаты завели, а его ногу в книге пропустили.
//
// ЮРЛИЦО — ПОЛЕМ ШАПКИ, А НЕ ВЫВОДОМ ИЗ ССЫЛОК. У оплаты нет ни склада, ни
// подразделения, а у поставщика нет связи с юрлицом — вывести адресата проводки
// не из чего. Поэтому платёж несёт юрлицо сам, необязательным полем: не задано —
// платёж как и раньше гасит регистр, проводки просто нет (та же best-effort
// политика, что у всех прочих ног).
public partial class VendorPaymentGLEventHandler : TypedDocumentEventHandler<VendorPayment>
{
    public override async Task<EventResult> OnAfterPostAsync(VendorPayment document, EventContext context)
    {
        if (document.Subtype != "Paid") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(VendorPayment header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<VendorPayment>(header.MetaId);
        if (payment == null) return null;
        if (payment.LegalEntity == Guid.Empty) return null;

        var total = payment.Lines.Sum(l => l.Amount);
        if (total <= 0m) return null;

        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(payment.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            payment.DocumentDate, le.MetaId, le.Currency, total,
            settings.PayableAccountCode, settings.CashAccountCode,
            "Vendor payment " + header.MetaId,
            "Кредиторка перед поставщиком", "Денежные средства");
    }
}
