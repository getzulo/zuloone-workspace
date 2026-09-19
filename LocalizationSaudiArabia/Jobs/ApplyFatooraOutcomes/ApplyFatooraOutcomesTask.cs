#nullable enable
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Tasks;
using ZuloOne.Services.Contracts;

public class ApplyFatooraOutcomesTask : TaskScriptBase
{
    public override async Task ExecuteAsync(TaskContext context)
    {
        var n = await ScriptServices.Get<ISaudiEInvoice>().ApplyChannelOutcomesAsync();
        context.Log("applied=" + n);
    }
}
