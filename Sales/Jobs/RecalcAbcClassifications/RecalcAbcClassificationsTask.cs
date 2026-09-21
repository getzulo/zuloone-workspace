#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Tasks;
using ZuloOne.Services.Contracts;

public class RecalcAbcClassificationsTask : TaskScriptBase
{
    public override async Task ExecuteAsync(TaskContext context)
    {
        var n = await ScriptServices.Get<IAbcClassifier>().RecalcAllEnabledAsync(DateTime.UtcNow.Date);
        context.Log("rows=" + n);
    }
}
