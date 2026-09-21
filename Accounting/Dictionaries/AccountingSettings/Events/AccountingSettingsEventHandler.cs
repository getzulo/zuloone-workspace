#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for AccountingSettings records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed AccountingSettings entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
//
// ═══ ACCOUNT CODES ARE CHECKED HERE, NOT AT POSTING ═════════════════════════
//
// The profile names accounts by CODES, and until now the field could hold a
// GROUP-account code. Posting on such a profile silently does nothing — it is
// best-effort and must not fail document posting — so a settings error becomes
// missing journal entries that nobody notices until reconciliation. Meanwhile
// the field looks filled and the account exists in the chart.
//
// The check sits on PROFILE SAVE: the person is in the settings form, sees the
// field and can fix it. The rule itself is not duplicated — GeneralLedgerService
// knows it (AccountCodeProblemAsync), and the same function filters unpostable
// accounts at posting as the last line of defence.
//
// A NON-EXISTENT account code is NOT rejected here on purpose: that is "this
// leg is not configured yet", as lawful as an empty field. The profile is
// filled before the chart is finished, and requiring all twelve accounts just
// to edit one field would lock the form. A filled WIP code is the same rule.
public partial class AccountingSettingsEventHandler : TypedDictionaryEventHandler<AccountingSettings>
{
    // Building a new record server-side: seed default field values here.
    public override async Task<EventResult> OnBeforeCreateAsync(AccountingSettings record, EventContext context){
        var prior = await next(record, context);
        if (!prior.Success) return prior;

        // record.CreatedOn = DateTime.UtcNow;
        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(AccountingSettings record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        var gl = context.GetService<IGeneralLedgerService>();
        var accounts = context.GetService<IDictionaryManager<ChartOfAccounts>>();
        await StampCodesFromRefsAsync(record, accounts);

        // Empty code = "this leg is not configured", and that is lawful: posting
        // will skip it quietly. Only a FILLED code is checked.
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
            ["Оприходование излишка"] = record.InventorySurplusAccountCode,
            ["НДС к уплате"] = record.VatPayableAccountCode,
            ["НДС к возмещению"] = record.VatReceivableAccountCode,
            ["Расходы на соцстрах"] = record.SocialInsuranceExpenseAccountCode,
            ["Задолженность перед фондом"] = record.SocialInsurancePayableAccountCode,
            ["Незавершёнка"] = record.WipAccountCode,
        };

        foreach (var kv in codes)
        {
            var problem = await gl.AccountCodeProblemAsync(kv.Value ?? string.Empty);
            if (problem != null)
                return EventResult.Cancel($"{kv.Key}: {problem}");
        }

        return EventResult.Ok();
    }

    /// <summary>
    /// A filled ChartOfAccounts ref is the profile the person sees. Posting still
    /// resolves by CODE, so the matching *AccountCode is stamped here. Empty ref
    /// leaves the string — tests and stands that typed codes keep working.
    /// </summary>
    private static async Task StampCodesFromRefsAsync(
        AccountingSettings record, IDictionaryManager<ChartOfAccounts> accounts)
    {
        record.ArAccountCode = await CodeOfAsync(accounts, record.ArAccount, record.ArAccountCode);
        record.CashAccountCode = await CodeOfAsync(accounts, record.CashAccount, record.CashAccountCode);
        record.CogsAccountCode = await CodeOfAsync(accounts, record.CogsAccount, record.CogsAccountCode);
        record.InventoryAccountCode = await CodeOfAsync(accounts, record.InventoryAccount, record.InventoryAccountCode);
        record.InventorySurplusAccountCode = await CodeOfAsync(accounts, record.InventorySurplusAccount, record.InventorySurplusAccountCode);
        record.InventoryWriteOffAccountCode = await CodeOfAsync(accounts, record.InventoryWriteOffAccount, record.InventoryWriteOffAccountCode);
        record.PayableAccountCode = await CodeOfAsync(accounts, record.PayableAccount, record.PayableAccountCode);
        record.PayrollExpenseAccountCode = await CodeOfAsync(accounts, record.PayrollExpenseAccount, record.PayrollExpenseAccountCode);
        record.PayrollLiabilityAccountCode = await CodeOfAsync(accounts, record.PayrollLiabilityAccount, record.PayrollLiabilityAccountCode);
        record.RevenueAccountCode = await CodeOfAsync(accounts, record.RevenueAccount, record.RevenueAccountCode);
        record.SocialInsuranceExpenseAccountCode = await CodeOfAsync(accounts, record.SocialInsuranceExpenseAccount, record.SocialInsuranceExpenseAccountCode);
        record.SocialInsurancePayableAccountCode = await CodeOfAsync(accounts, record.SocialInsurancePayableAccount, record.SocialInsurancePayableAccountCode);
        record.VatPayableAccountCode = await CodeOfAsync(accounts, record.VatPayableAccount, record.VatPayableAccountCode);
        record.VatReceivableAccountCode = await CodeOfAsync(accounts, record.VatReceivableAccount, record.VatReceivableAccountCode);
        record.WipAccountCode = await CodeOfAsync(accounts, record.WipAccount, record.WipAccountCode);
    }

    private static async Task<string> CodeOfAsync(
        IDictionaryManager<ChartOfAccounts> accounts, Guid accountId, string? fallback)
    {
        if (accountId == Guid.Empty) return fallback ?? string.Empty;
        var row = await accounts.GetRecordAsync(accountId);
        return string.IsNullOrWhiteSpace(row?.Code) ? (fallback ?? string.Empty) : row!.Code;
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(AccountingSettings record, bool isNew, EventContext context)
        => next(record, isNew, context);

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
        => next(recordId, context);

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(AccountingSettings record, EventContext context)
        => next(record, context);

    // After a record is loaded: compute transient/derived property values.
    public override Task<EventResult> OnAfterLoadAsync(AccountingSettings record, EventContext context)
        => next(record, context);

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(AccountingSettings record, string fieldName, object? value, EventContext context)
        => next(record, fieldName, value, context);

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(AccountingSettings record, string errorMessage, EventContext context)
        => next(record, errorMessage, context);

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
