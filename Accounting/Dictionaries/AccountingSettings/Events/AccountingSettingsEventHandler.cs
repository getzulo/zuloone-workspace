#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for AccountingSettings records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed AccountingSettings entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
//
// ═══ КОДЫ СЧЕТОВ ПРОВЕРЯЮТСЯ ЗДЕСЬ, А НЕ ПРИ РАЗНОСКЕ ═══════════════════════
//
// Профиль называет счета КОДАМИ, и до сих пор в поле можно было написать код
// счёта-ГРУППЫ. Разноска на таком профиле молча ничего не делает — она
// best-effort и не должна ронять проведение документа, — то есть ошибка
// настройки оборачивается пропавшими проводками, о которых никто не узнает до
// сверки. При этом поле выглядит заполненным, а счёт в плане есть.
//
// Проверка стоит на СОХРАНЕНИИ ПРОФИЛЯ: человек в форме настроек, видит поле и
// может его исправить. Само правило не дублируется — его знает
// GeneralLedgerService (AccountCodeProblemAsync), и та же функция отсеивает
// непроводимые счета на разноске как последний рубеж.
//
// Код НЕСУЩЕСТВУЮЩЕГО счёта здесь НЕ отвергается намеренно: это «нога ещё не
// настроена», такое же законное состояние, как пустое поле. Профиль заполняют
// до того, как достроен план счетов, и требовать наличия всех двенадцати счетов
// ради правки одного поля — значит запереть форму.
public partial class AccountingSettingsEventHandler : TypedDictionaryEventHandler<AccountingSettings>
{
    // Building a new record server-side: seed default field values here.
    public override Task<EventResult> OnBeforeCreateAsync(AccountingSettings record, EventContext context)
    {
        // record.CreatedOn = DateTime.UtcNow;
        return Task.FromResult(EventResult.Ok());
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(AccountingSettings record, bool isNew, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();

        // Пустой код = «эта нога не настроена», и это законно: разноска её тихо
        // пропустит. Проверяется только ЗАПОЛНЕННОЕ.
        var codes = new Dictionary<string, string?>
        {
            ["Дебиторка"] = record.ArAccountCode,
            ["Выручка"] = record.RevenueAccountCode,
            ["Запасы"] = record.InventoryAccountCode,
            ["Кредиторка"] = record.PayableAccountCode,
            ["Себестоимость продаж"] = record.CogsAccountCode,
            ["Расход на оплату труда"] = record.PayrollExpenseAccountCode,
            ["Задолженность перед сотрудниками"] = record.PayrollLiabilityAccountCode,
            ["Денежные средства"] = record.CashAccountCode,
            ["Списание запасов"] = record.InventoryWriteOffAccountCode,
            ["НДС к уплате"] = record.VatPayableAccountCode,
            ["Расходы на соцстрах"] = record.SocialInsuranceExpenseAccountCode,
            ["Задолженность перед фондом"] = record.SocialInsurancePayableAccountCode,
        };

        foreach (var kv in codes)
        {
            var problem = await gl.AccountCodeProblemAsync(kv.Value ?? string.Empty);
            if (problem != null)
                return EventResult.Cancel($"{kv.Key}: {problem}");
        }

        return EventResult.Ok();
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(AccountingSettings record, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(AccountingSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(AccountingSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(AccountingSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(AccountingSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(AccountingSettings record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After a record is loaded: compute transient/derived property values.
    public override Task<EventResult> OnAfterLoadAsync(AccountingSettings record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(AccountingSettings record, string fieldName, object? value, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(AccountingSettings record, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
