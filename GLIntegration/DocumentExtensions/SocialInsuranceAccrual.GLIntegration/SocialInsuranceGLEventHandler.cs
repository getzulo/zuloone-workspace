#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// HR extension: social-insurance contributions are posted to the general ledger
// in TWO legs — reclassification of the withheld share (Dr employee payable /
// Cr fund payable) and the employer expense (Dr social-insurance expense /
// Cr fund payable). Fourth consumer of GeneralLedgerService:
// same mechanics as payroll, sales, and purchasing.
//
// Legal entity is taken along Division → LegalEntity, same as PayrollGLEventHandler.
public partial class SocialInsuranceGLEventHandler : TypedDocumentEventHandler<SocialInsuranceAccrual>
{
    public override async Task<EventResult> OnAfterPostAsync(SocialInsuranceAccrual document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Posted") return EventResult.Ok();

        await PostToLedgerAsync(document, context);

        return EventResult.Ok();
    }

    private async Task PostToLedgerAsync(SocialInsuranceAccrual header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return;

        var accrual = await context.GetService<IDocumentManager>().GetDocumentAsync<SocialInsuranceAccrual>(header.MetaId);
        if (accrual == null) return;

        // Amounts are already computed by SocialInsuranceService and saved on the lines BEFORE
        // the document moves to Posted — here they are only read.
        var employee = accrual.Lines.Sum(l => l.EmployeeContribution);
        var employer = accrual.Lines.Sum(l => l.EmployerContribution);

        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(accrual.Division);
        if (div == null) return;
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (le == null) return;

        var date = accrual.DocumentDate;
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
