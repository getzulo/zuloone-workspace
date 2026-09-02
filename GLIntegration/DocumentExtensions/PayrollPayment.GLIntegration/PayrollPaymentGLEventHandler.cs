#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение HR: ВЫПЛАТА ФОТ разносится в главную книгу —
// Dr задолженность перед сотрудниками / Cr денежные средства.
//
// Зачем: начисление кредитует счёт задолженности (PayrollGLEventHandler), а
// дебетовать его было нечем — в регистре PayrollLiability долг гасился
// PayrollPaymentTx, а в книге рос бесконечно. Эта пара закрывает расхождение:
// после выплаты счёт задолженности в GL сходится с остатком регистра.
//
// Юрлицо у выплаты не лежит в шапке (там только ID), поэтому цепочка длиннее,
// чем у начисления: Строка → Сотрудник → Подразделение → Юрлицо. Выплата может
// охватывать сотрудников РАЗНЫХ юрлиц, поэтому суммы группируются по юрлицу и
// на каждое пишется своя проводка — одна общая исказила бы обе книги.
//
// Имя класса намеренно отличается от PayrollGLEventHandler: имена классов
// скриптов уникальны во всём воркспейсе, и совпадение вытеснило бы обработчик
// начисления.
public partial class PayrollPaymentGLEventHandler : TypedDocumentEventHandler<PayrollPayment>
{
    public override async Task<EventResult> OnAfterPostAsync(PayrollPayment document, EventContext context)
    {
        if (document.Subtype != "Paid") return EventResult.Ok();

        try
        {
            var docs = context.GetService<IDocumentManager>();
            foreach (var jeId in await PostToLedgerAsync(document, context))
                await docs.AddLinkAsync(document.MetaId, jeId);
        }
        catch
        {
            // Разноска GL зависит от настройки счетов и не должна ронять выплату:
            // на стенде без заполненного профиля ФОТ обязан выплачиваться как прежде.
        }

        return EventResult.Ok();
    }

    private async Task<List<Guid>> PostToLedgerAsync(PayrollPayment header, EventContext context)
    {
        var posted = new List<Guid>();

        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return posted;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<PayrollPayment>(header.MetaId);
        if (payment == null) return posted;

        var employees = context.GetService<IDictionaryManager<Employee>>();
        var divisions = context.GetService<IDictionaryManager<Division>>();
        var entities = context.GetService<IDictionaryManager<LegalEntity>>();

        // Сумма к разноске — по юрлицу сотрудника, а не общий итог документа.
        var byLegalEntity = new Dictionary<Guid, decimal>();
        foreach (var line in payment.Lines)
        {
            if (line.Amount <= 0m) continue;

            var emp = await employees.GetRecordAsync(line.Employee);
            if (emp == null) continue;
            var div = await divisions.GetRecordAsync(emp.Division);
            if (div == null) continue;

            byLegalEntity[div.LegalEntity] =
                (byLegalEntity.TryGetValue(div.LegalEntity, out var acc) ? acc : 0m) + line.Amount;
        }

        foreach (var kv in byLegalEntity)
        {
            if (kv.Value <= 0m) continue;

            var le = await entities.GetRecordAsync(kv.Key);
            if (le == null) continue;

            // Описание несёт и документ, и юрлицо: идемпотентность GeneralLedgerService
            // построена на нём, а на одну выплату проводок может быть несколько.
            var jeId = await gl.PostAsync(
                DateTime.UtcNow.Date, le.MetaId, le.Currency, kv.Value,
                settings.PayrollLiabilityAccountCode, settings.CashAccountCode,
                $"Payroll payment {header.MetaId} / {le.MetaId}",
                "Задолженность перед сотрудниками", "Денежные средства");

            if (jeId.HasValue) posted.Add(jeId.Value);
        }

        return posted;
    }
}
