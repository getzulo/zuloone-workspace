#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Tasks;
using ZuloOne.Services.Contracts;

public class ExpireCertificatesTask : TaskScriptBase
{
    public override async Task ExecuteAsync(TaskContext context)
    {
        var expired = await ScriptServices.Get<IExamSession>().ExpireDueAsync(DateTime.UtcNow.Date);
        context.Log("expired=" + expired);
    }
}
