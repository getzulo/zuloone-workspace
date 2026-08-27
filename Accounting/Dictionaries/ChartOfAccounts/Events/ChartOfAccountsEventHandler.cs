#nullable enable
namespace ZuloOne.Runtime.Generated;

// Chart of accounts validation: the account type is the basis of normal-balance
// and reporting rules, so it must be one of the five standard classes.
public partial class ChartOfAccountsEventHandler : TypedDictionaryEventHandler<ChartOfAccounts>
{
    public override Task<EventResult> OnBeforeSaveAsync(ChartOfAccounts record, bool isNew, EventContext context)
    {
        var valid = record.AccountType is "Asset" or "Liability" or "Equity" or "Income" or "Expense";
        if (!valid)
            return Task.FromResult(EventResult.Cancel("Тип счёта должен быть Asset/Liability/Equity/Income/Expense"));
        return Task.FromResult(EventResult.Ok());
    }
}
