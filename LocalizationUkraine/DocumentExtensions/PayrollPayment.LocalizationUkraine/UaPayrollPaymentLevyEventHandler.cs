#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Events;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Виплата ФОТ → графа 3 4ДФ (виплачений дохід).
//
// Рядок PayrollPayment — нетто на руки, а 4ДФ G03 ≈ G03A, коли зарплату за
// місяць виплатили. Тому сюди йде залишок Base ПДФО, а не сума рядка.
// GetTransactions синхронний і не бачить код податку з налаштувань — тому
// подієвий обробник, як і нарахування утримань. Mix платформи ці рухи не
// зніме: на Voided видаляємо самі, і тільки UaPayrollLevy — PayrollLiability
// пише HR.
[ExtensionOf("PayrollPayment")]
public partial class UaPayrollPaymentLevyEventHandler : TypedDocumentEventHandler<PayrollPayment>
{
    public override async Task<EventResult> OnAfterPostAsync(
        PayrollPayment document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype == PayrollPayment.Subtypes.Voided
            || document.Subtype == PayrollPayment.Subtypes.Draft)
        {
            await ReleaseAsync(document, context);
            return EventResult.Ok();
        }

        if (document.Subtype != PayrollPayment.Subtypes.Paid) return EventResult.Ok();

        await MarkPaidAsync(document, context);
        return EventResult.Ok();
    }

    private async Task MarkPaidAsync(PayrollPayment document, EventContext context)
    {
        var movements = context.GetService<IRegisterMovementService>();
        var levyId = await LevyRegisterIdAsync(context);

        var already = await movements.QueryMovementsAsync(
            levyId, $"DocumentMetaId = '{document.MetaId}'", take: 1);
        if (already.Count > 0) return;

        var full = await context.GetService<IDocumentManager>()
            .GetDocumentAsync<PayrollPayment>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;
        if (lines.Count == 0) return;

        var pdfoId = await PdfoCodeIdAsync(context);
        if (pdfoId == Guid.Empty) return;

        var levies = context.GetService<IUaPayrollLevies>();
        var employees = context.GetService<IDictionaryManager<Employee>>();
        var date = document.DocumentDate == default ? DateTime.UtcNow : document.DocumentDate;

        var seen = new HashSet<Guid>();
        foreach (var line in lines)
        {
            if (line.Employee == Guid.Empty || !seen.Add(line.Employee)) continue;

            var employee = await employees.GetRecordAsync(line.Employee);
            if (employee == null || employee.Division == Guid.Empty) continue;
            var entity = await levies.LegalEntityOfDivisionAsync(employee.Division);
            if (entity == Guid.Empty) continue;

            var remaining = await RemainingPaidBaseAsync(
                movements, levyId, entity, line.Employee, pdfoId);
            if (remaining <= 0m) continue;

            await movements.PostMovementAsync(
                levyId, document.MetaId, date,
                new Dictionary<string, object?>
                {
                    ["LegalEntity"] = entity,
                    ["Employee"] = line.Employee,
                    ["TaxCode"] = pdfoId,
                },
                new Dictionary<string, decimal> { ["PaidBase"] = remaining });
        }
    }

    private static async Task ReleaseAsync(PayrollPayment document, EventContext context)
    {
        var movements = context.GetService<IRegisterMovementService>();
        var levyId = await LevyRegisterIdAsync(context);
        await movements.DeleteDocumentMovementsAsync(levyId, document.MetaId);
    }

    private static async Task<decimal> RemainingPaidBaseAsync(
        IRegisterMovementService movements,
        Guid levyId,
        Guid entity,
        Guid employee,
        Guid pdfoId)
    {
        var rows = await movements.QueryMovementsAsync(
            levyId,
            $"[LegalEntity] = '{entity}' AND [Employee] = '{employee}' AND [TaxCode] = '{pdfoId}'");
        decimal accrued = 0m, paid = 0m;
        foreach (var row in rows)
        {
            accrued += AsDecimal(row, "Base");
            paid += AsDecimal(row, "PaidBase");
        }
        return accrued - paid;
    }

    private static async Task<Guid> PdfoCodeIdAsync(EventContext context)
    {
        var settings = await context.GetService<IDictionaryManager<LocalizationUkraineSettings>>()
            .GetRecordsAsync(take: 1);
        var code = settings.Count > 0 ? settings[0].IncomeTaxCode : null;
        if (string.IsNullOrWhiteSpace(code)) return Guid.Empty;
        var escaped = code.Replace("'", "''");
        var rows = await context.GetService<IDictionaryManager<TaxCode>>()
            .GetRecordsAsync($"Code = '{escaped}'", take: 1);
        return rows.Count > 0 ? rows[0].MetaId : Guid.Empty;
    }

    private static async Task<Guid> LevyRegisterIdAsync(EventContext context)
    {
        var all = await context.GetService<IMetadataService>().GetAllRegistersAsync();
        return all.First(r =>
            string.Equals(r.Name, "UaPayrollLevy", StringComparison.OrdinalIgnoreCase)).MetaId;
    }

    private static decimal AsDecimal(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var raw) || raw is null) return 0m;
        return raw is decimal d ? d : Convert.ToDecimal(raw);
    }
}
