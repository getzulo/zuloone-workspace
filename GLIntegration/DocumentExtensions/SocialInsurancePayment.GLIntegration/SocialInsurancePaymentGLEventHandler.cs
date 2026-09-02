#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение HR: ПЛАТЁЖ В ФОНД разносится в главную книгу —
// Dr задолженность перед фондом соцстраха / Cr денежные средства.
//
// Зачем: начисление взносов кредитует счёт задолженности перед фондом дважды
// (удержанное у работника и доля работодателя — см. SocialInsuranceGLEventHandler),
// а дебетовать его было нечем. Эта пара закрывает расхождение: после платежа
// счёт задолженности перед фондом сходится с остатком регистра SocialInsurance.
//
// Юрлицо берётся по цепочке Подразделение → Юрлицо, как у начисления: у платежа
// подразделение лежит прямо в шапке.
//
// Сумма проводки — ОБЕ доли взноса: в фонд уходит один платёж, разделение на
// удержанное и начисленное работодателем существует только для отчётности.
public partial class SocialInsurancePaymentGLEventHandler : TypedDocumentEventHandler<SocialInsurancePayment>
{
    public override async Task<EventResult> OnAfterPostAsync(SocialInsurancePayment document, EventContext context)
    {
        if (document.Subtype != "Paid") return EventResult.Ok();

        try
        {
            var jeId = await PostToLedgerAsync(document, context);
            if (jeId.HasValue)
                await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);
        }
        catch
        {
            // Разноска GL зависит от настройки счетов и не должна ронять платёж:
            // на стенде без заполненного профиля платёж обязан проводиться как прежде.
        }

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(SocialInsurancePayment header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<SocialInsurancePayment>(header.MetaId);
        if (payment == null) return null;

        var total = payment.Lines.Sum(l => l.EmployeeContribution + l.EmployerContribution);
        if (total <= 0m) return null;

        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(payment.Division);
        if (div == null) return null;
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            payment.DocumentDate, le.MetaId, le.Currency, total,
            settings.SocialInsurancePayableAccountCode, settings.CashAccountCode,
            "Social insurance payment " + header.MetaId,
            "Задолженность перед фондом соцстраха", "Денежные средства");
    }
}
