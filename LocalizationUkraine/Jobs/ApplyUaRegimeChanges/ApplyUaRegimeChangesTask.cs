#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Tasks;
using ZuloOne.Services.Contracts;

// Принудительный переход на общую систему наступает ПО ДАТЕ, а не по событию:
// превышение предела случилось в одном квартале, а режим меняется с первого
// числа месяца, следующего за этим кварталом (ПКУ 293.8). Между этими двумя
// моментами недели, поэтому кто-то должен прийти в нужный день — это задание.
//
// Ежедневно, а не раз в квартал: пропустить первое число нельзя, а лишний
// прогон ничего не стоит — применённые строки помечены AppliedOn и второй раз
// не берутся.
public class ApplyUaRegimeChangesTask : TaskScriptBase
{
    public override async Task ExecuteAsync(TaskContext context)
    {
        var applied = await ScriptServices.Get<IUaFirstEvent>()
            .ApplyDueRegimeChangesAsync(DateTime.UtcNow.Date);
        context.Log("переведено юрлиц: " + applied);
    }
}
