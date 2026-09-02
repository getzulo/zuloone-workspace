#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение HR: начисление ФОТ разносится в главную книгу —
// Dr расход на оплату труда / Cr задолженность перед сотрудниками.
// Третий потребитель GeneralLedgerService: механика та же, что у продаж и
// закупок, отличаются только счета из профиля и подписи строк.
//
// Юрлицо берётся по цепочке Подразделение → Юрлицо: у начисления нет ни склада,
// ни контрагента, только Division.
public partial class PayrollGLEventHandler : TypedDocumentEventHandler<PayrollAccrual>
{
    public override async Task<EventResult> OnAfterPostAsync(PayrollAccrual document, EventContext context)
    {
        if (document.Subtype != "Posted") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(PayrollAccrual header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var accrual = await context.GetService<IDocumentManager>().GetDocumentAsync<PayrollAccrual>(header.MetaId);
        if (accrual == null) return null;

        // Сумма проводки — итог начисления по строкам; сами суммы уже посчитаны
        // командой «Начислить ФОТ» из часов и ставки должности.
        var total = accrual.Lines.Sum(l => l.Amount);
        if (total <= 0m) return null;

        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(accrual.Division);
        if (div == null) return null;
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            accrual.DocumentDate, le.MetaId, le.Currency, total,
            settings.PayrollExpenseAccountCode, settings.PayrollLiabilityAccountCode,
            "Payroll accrual " + header.MetaId,
            "Расход на оплату труда", "Задолженность перед сотрудниками");
    }
}
