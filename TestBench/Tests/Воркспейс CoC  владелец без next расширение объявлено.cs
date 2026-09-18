using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Events;
using ZuloOne.Runtime.Testing;

// Тот же анализатор, которым компиляция принимает или отклоняет обработчик
// (ScriptCompilationService и EventCompiler зовут ровно его) — здесь он
// проверяется на пяти формах разом, без похода в БД и без HTTP.
public class ChainDeclarationTest : IntegrationTestScriptBase
{
    private const string NoNext = """
public class H : EventHandlerBase
{
    public override Task<EventResult> OnBeforeInsertAsync(EventContext context)
        => Task.FromResult(EventResult.Ok());
}
""";

    private static string CodesOf(System.Collections.Generic.IReadOnlyList<CompilationError> errors)
        => string.Join(",", errors.Select(e => e.Code));

    [IntegrationTest("Workspace: CoC — владелец без next, расширение объявлено")]
    public Task DeclarationRulesHold()
    {
        var owner = new HandlerPlacement(true, false);
        var above = new HandlerPlacement(false, false);
        var foreign = new HandlerPlacement(false, true);

        // 1. Владелец — звено 0: next не нужен, ниже некого.
        var ownerErrors = ChainOfCommandAnalyzer.Analyze(NoNext, owner, "TBWarehouse");
        Assert.IsTrue(ownerErrors.Count == 0,
            "владелец без next чист, получено: {0}", CodesOf(ownerErrors));

        // 2. Звено ВЫШЕ владельца молча проглотило бы его — ZOCOC001.
        var aboveErrors = ChainOfCommandAnalyzer.Analyze(NoNext, above, "TBWarehouse");
        Assert.IsTrue(CodesOf(aboveErrors).Contains("ZOCOC001"),
            "звено выше владельца без next даёт ZOCOC001, получено: {0}", CodesOf(aboveErrors));

        // 3. Обработчик на ЧУЖОМ объекте обязан объявиться — ZOCOC004.
        var undeclared = ChainOfCommandAnalyzer.Analyze(NoNext, foreign, "TBWarehouse");
        Assert.IsTrue(CodesOf(undeclared).Contains("ZOCOC004"),
            "необъявленное расширение даёт ZOCOC004, получено: {0}", CodesOf(undeclared));

        // 4. Объявился — обязан звать next, даже когда метаданных нет.
        var declaredNoNext = ChainOfCommandAnalyzer.Analyze(
            "[ExtensionOf(\"TBWarehouse\")]\n" + NoNext, HandlerPlacement.Unknown, "TBWarehouse");
        Assert.IsTrue(CodesOf(declaredNoNext).Contains("ZOCOC001"),
            "объявленное расширение без next даёт ZOCOC001, получено: {0}", CodesOf(declaredNoNext));

        // 5. Имя цели сверяется с конвертом: переименование объекта ловится
        //    ошибкой, а не тихо отцепившимся обработчиком.
        var drifted = ChainOfCommandAnalyzer.Analyze(
            "[ExtensionOf(\"TBWarehouseOld\")]\n" + NoNext, foreign, "TBWarehouse");
        Assert.IsTrue(CodesOf(drifted).Contains("ZOCOC004"),
            "[ExtensionOf] с чужим именем даёт ZOCOC004, получено: {0}", CodesOf(drifted));

        return Task.CompletedTask;
    }
}