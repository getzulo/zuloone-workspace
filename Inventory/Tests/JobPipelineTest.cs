using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Первое покрытие конвейера заданий: до этого теста MetaJob → компиляция →
// TaskScriptBase → ExecuteAsync не проверялся ничем, и харнес не умел ни
// создать задание, ни запустить его.
//
// ПОЧЕМУ ЗДЕСЬ НЕТ МОДЕЛЬНОГО СЕРВИСА, хотя ради него тест и начинался.
// JobExecutor компилирует код задания без скриптового фреймворка
// (CompileAsync(code, name, modelScope) — без withScriptFramework), поэтому
// сборка сгенерированных контрактов скрипту не подключена: назвать
// I<Имя> по типу он не может, и CS0246 наступает раньше любого резолва.
// Из-за этого ветку IScriptServiceResolver — ту, где сервис возвращался с уже
// уничтоженным провайдером, — со стороны задания сейчас не достать. Привязка
// scope в JobExecutor корректна и нужна, но до неё дело дойдёт только когда
// заданиям подключат контракты; тогда сюда добавляется случай с GetService.
public class JobPipelineTest : IntegrationTestScriptBase
{
    [IntegrationTest("Задание компилируется, исполняется и возвращает свой вывод")]
    public async Task JobRunsAndReportsOutput()
    {
        var jobId = await Db.CreateJobAsync("TBJobProbe",
            "using System.Threading.Tasks;\n" +
            "using ZuloOne.Runtime.Tasks;\n" +
            "\n" +
            "public class TBJobProbeTask : TaskScriptBase\n" +
            "{\n" +
            "    public override Task ExecuteAsync(TaskContext context)\n" +
            "    {\n" +
            "        context.Log(\"AMOUNT=\" + (4m * 25m * 0.8m));\n" +
            "        return Task.CompletedTask;\n" +
            "    }\n" +
            "}\n");
        Assert.IsTrue(jobId != Guid.Empty, "задание и его скрипт созданы");

        var run = await Db.RunJobAsync(jobId);

        Assert.IsTrue(run.Success, "задание отработало без ошибки, факт: {0}", run.Output);
        Assert.IsTrue(run.Output.Contains("AMOUNT=80.0"),
            "вывод скрипта доехал до результата запуска, факт: {0}", run.Output);
        Log("MetaJob → компиляция → TaskScriptBase → ExecuteAsync → вывод: конвейер живой.");
    }

    [IntegrationTest("Незакрывающийся скрипт задания возвращает ошибку, а не падает наружу")]
    public async Task BrokenJobScriptFailsCleanly()
    {
        // Ошибка компиляции обязана стать результатом запуска: планировщик
        // исполняет задания в фоне, и исключение наружу некому ловить.
        var jobId = await Db.CreateJobAsync("TBJobBroken", "this is not C#");

        var run = await Db.RunJobAsync(jobId);

        Assert.IsTrue(!run.Success, "сломанный скрипт — неуспех, а не исключение");
        Assert.IsTrue(run.Output.Contains("compile"),
            "причина названа в выводе запуска, факт: {0}", run.Output);
    }
}
