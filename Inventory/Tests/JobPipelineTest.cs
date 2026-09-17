using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Задание — третий хост, исполняющий пользовательские скрипты, наравне с
// HTTP-запросом и тестовым кейсом. У первых двух есть scope, которому они
// принадлежат; у JobExecutor его не было, и модельный сервис, запрошенный из
// скрипта задания, приходил через запасной путь резолвера: тот создавал scope
// на каждый резолв и тут же уничтожал, отдавая наружу объект с мёртвыми
// scoped-зависимостями.
//
// МОДЕЛЬ ЗАДАНИЯ РЕШАЕТ, ЧТО СКРИПТ ВИДИТ. Скрипт компилируется в области
// сборок своей модели, поэтому назвать I<Имя> он может только из модели,
// которая этим контрактом владеет или зависит от него. Задание в TestBench
// (умолчание харнеса) не видит ни одного бизнес-контракта — первая версия
// этого теста падала на CS0246 именно поэтому, а НЕ потому, что заданиям
// «не подключены контракты»: ссылки на сборку контрактов добавляются
// безусловно, независимо от withScriptFramework.
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

    [IntegrationTest("Скрипт задания получает ЖИВОЙ модельный сервис")]
    public async Task JobScriptResolvesWorkingModelService()
    {
        // Регрессия на JobExecutor: без собственного scope у задания резолвер
        // отдавал сюда экземпляр с уже уничтоженным IServiceProvider.
        //
        // Зовётся ScriptServices.TryGet, а НЕ context.GetService: второй идёт
        // прямо в DI, где модельных контрактов нет вовсе, и ветку
        // IScriptServiceResolver — ту самую — не задевает.
        //
        // 4 × 25 со скидкой 20% = 80. Считает СЕРВИС, не тест: важно, что до
        // арифметики дожил живой экземпляр, а не то, сколько получилось.
        var jobId = await Db.CreateJobAsync("TBJobServiceProbe",
            "using System.Threading.Tasks;\n" +
            "using ZuloOne.Runtime.Tasks;\n" +
            "using ZuloOne.Services.Contracts;\n" +
            "\n" +
            "public class TBJobServiceProbeTask : TaskScriptBase\n" +
            "{\n" +
            "    public override async Task ExecuteAsync(TaskContext context)\n" +
            "    {\n" +
            "        var pricing = ZuloOne.Runtime.ScriptServices.TryGet<IPricingService>();\n" +
            "        if (pricing is null) { context.Log(\"NO-SERVICE\"); return; }\n" +
            "        context.Log(\"AMOUNT=\" + pricing.LineAmount(4m, 25m, 20m));\n" +
            "        await pricing.ResolveSalePriceAsync(System.Guid.Empty, System.Guid.Empty, null, System.DateTime.UtcNow);\n" +
            "        context.Log(\"DBOK\");\n" +
            "    }\n" +
            "}\n",
            ownerModelName: "Inventory");

        var run = await Db.RunJobAsync(jobId);

        Assert.IsTrue(run.Success, "задание отработало без ошибки, факт: {0}", run.Output);
        Assert.IsTrue(!run.Output.Contains("NO-SERVICE"),
            "модельный сервис резолвится из скрипта задания, факт: {0}", run.Output);
        Assert.IsTrue(run.Output.Contains("AMOUNT=80"),
            "сервис дожил до вызова и посчитал 4 × 25 − 20% = 80, факт: {0}", run.Output);
        // Решающая проверка: метод, который ХОДИТ В БАЗУ через scoped-зависимость.
        // Арифметика выше отработала бы и на экземпляре с мёртвым провайдером —
        // она его не касается. Этот вызов касается.
        Assert.IsTrue(run.Output.Contains("DBOK"),
            "сервис сходил в базу своей scoped-зависимостью, факт: {0}", run.Output);
        Log("Задание → скрипт → модельный сервис: экземпляр живой, scope у задания свой.");
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
