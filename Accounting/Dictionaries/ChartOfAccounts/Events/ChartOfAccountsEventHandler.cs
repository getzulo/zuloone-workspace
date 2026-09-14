#nullable enable
namespace ZuloOne.Runtime.Generated;

// Chart of accounts validation. The account-class set is no longer checked
// here: it is closed by the AccountType enum, and an invalid value cannot be
// expressed. What remains is a rule metadata cannot express: postings accept
// only LEAVES. An account that has gained children is a group and cannot be
// postable, otherwise its own balance and the sum of children would drift.
public partial class ChartOfAccountsEventHandler : TypedDictionaryEventHandler<ChartOfAccounts>
{
    public override async Task<EventResult> OnBeforeSaveAsync(ChartOfAccounts record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (!record.IsPostable) return EventResult.Ok();

        var accounts = context.GetService<IDictionaryManager<ChartOfAccounts>>();
        var children = await accounts.GetRecordsAsync($"ParentId = '{record.MetaId}'");
        if (children.Count > 0)
        {
            return EventResult.Cancel(
                $"Счёт «{record.Name}» — группа ({children.Count} подчинённых): проводки принимают только конечные счета.");
        }

        return EventResult.Ok();
    }
}
