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
        var asOf = DateTime.UtcNow.Date;
        var classes = await ScriptServices.Get<IAbcClassifier>().RecalcAllEnabledAsync(asOf);
        var suggestions = await ScriptServices.Get<IAbcPolicy>().BuildEnabledItemProfilesAsync(asOf);
        context.Log("classes=" + classes + " suggestions=" + suggestions);
    }
}
