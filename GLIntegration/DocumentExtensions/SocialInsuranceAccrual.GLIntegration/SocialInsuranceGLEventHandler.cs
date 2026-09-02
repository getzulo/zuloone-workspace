#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение HR: взносы на соцстрах разносятся в главную книгу ДВУМЯ ногами —
// реклассификация удержанной доли (Dr задолженность перед сотрудниками /
// Cr задолженность перед фондом) и расход работодателя (Dr расход на соцстрах /
// Cr задолженность перед фондом). Четвёртый потребитель GeneralLedgerService:
// механика та же, что у ФОТ, продаж и закупок.
//
// Юрлицо берётся по цепочке Подразделение → Юрлицо, как и у PayrollGLEventHandler.
public partial class SocialInsuranceGLEventHandler : TypedDocumentEventHandler<SocialInsuranceAccrual>
{
    public override async Task<EventResult> OnAfterPostAsync(SocialInsuranceAccrual document, EventContext context)
    {
        if (document.Subtype != "Posted") return EventResult.Ok();

        try
        {
            await PostToLedgerAsync(document, context);
        }
        catch
        {
            // Разноска GL зависит от настройки счетов и не должна ронять начисление
            // взносов: на стенде без заполненного профиля соцстрах обязан
            // начисляться как прежде.
        }

        return EventResult.Ok();
    }

    private async Task PostToLedgerAsync(SocialInsuranceAccrual header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return;

        var accrual = await context.GetService<IDocumentManager>().GetDocumentAsync<SocialInsuranceAccrual>(header.MetaId);
        if (accrual == null) return;

        // Суммы уже посчитаны SocialInsuranceService и сохранены на строки ДО
        // перевода документа в Posted — здесь их только читаем.
        var employee = accrual.Lines.Sum(l => l.EmployeeContribution);
        var employer = accrual.Lines.Sum(l => l.EmployerContribution);

        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(accrual.Division);
        if (div == null) return;
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (le == null) return;

        var date = DateTime.UtcNow.Date;
        var documentManager = context.GetService<IDocumentManager>();

        if (employee > 0m)
        {
            var jeId = await gl.PostAsync(
                date, le.MetaId, le.Currency, employee,
                settings.PayrollLiabilityAccountCode, settings.SocialInsurancePayableAccountCode,
                "Social insurance withholding " + header.MetaId,
                "Задолженность перед сотрудниками (удержано)", "Задолженность перед фондом соцстраха");
            if (jeId.HasValue)
                await documentManager.AddLinkAsync(header.MetaId, jeId.Value);
        }

        if (employer > 0m)
        {
            var jeId = await gl.PostAsync(
                date, le.MetaId, le.Currency, employer,
                settings.SocialInsuranceExpenseAccountCode, settings.SocialInsurancePayableAccountCode,
                "Social insurance employer cost " + header.MetaId,
                "Расходы на соцстрах (работодатель)", "Задолженность перед фондом соцстраха");
            if (jeId.HasValue)
                await documentManager.AddLinkAsync(header.MetaId, jeId.Value);
        }
    }
}
