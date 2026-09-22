#nullable enable
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Tasks;
using ZuloOne.Services.Contracts;

// Виписка ПриватБанка не приходить колбеком.
//
// Autoclient віддає GET /statements/transactions, і тільки якщо його запитати.
// Тому це окреме завдання, а не хвіст проведення оплати. Раз на ранок:
// підсумкова виписка — за минулий опердень, а ліміт банку 1 запит на секунду.
public class PullUaBankStatementsTask : TaskScriptBase
{
    public override async Task ExecuteAsync(TaskContext context)
    {
        var imported = await ScriptServices.Get<IUaBank>().PullAllAsync();
        context.Log($"ПриватБанк: імпортовано рядків виписки {imported}.");
    }
}
