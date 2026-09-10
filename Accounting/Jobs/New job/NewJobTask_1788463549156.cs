// A scheduled task. The scheduler runs ExecuteAsync when the job's cron matches
// (minute resolution); use context.GetService<T>() for system services.
public partial class MyTask : TaskScriptBase
{
    public override async Task ExecuteAsync(TaskContext context)
    {
        // var data = context.GetService<IDataService>();
        context.Log($"task ran {DateTime.Now.ToString()}");
        await Task.CompletedTask;
    }
}
