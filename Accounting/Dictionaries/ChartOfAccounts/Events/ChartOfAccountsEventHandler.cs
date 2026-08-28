#nullable enable
namespace ZuloOne.Runtime.Generated;

// Валидация плана счетов. Набор классов счёта здесь больше не проверяется: он
// закрыт перечислением AccountType, и невалидное значение просто не выражается.
// Остаётся правило, которое метаданными не выразить: проводки принимают только
// ЛИСТЬЯ. Счёт, у которого появились подчинённые, — группа, и проводимым быть
// не может, иначе его собственный остаток и сумма детей разъезжаются.
public partial class ChartOfAccountsEventHandler : TypedDictionaryEventHandler<ChartOfAccounts>
{
    public override async Task<EventResult> OnBeforeSaveAsync(ChartOfAccounts record, bool isNew, EventContext context)
    {
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
