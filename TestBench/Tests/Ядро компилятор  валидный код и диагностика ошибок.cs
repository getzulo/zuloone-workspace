// Ядро платформы: скриптовый компилятор. Валидный код компилируется, битый
// даёт диагностику с кодом ошибки и строкой; полная генерация классов ВСЕХ
// метаданных системы собирается начисто (в одноразовую сборку, без подмены
// живой entities-сборки).
//
// НАМЕРЕННО на харнессе: предмет теста — САМ компилятор и генератор классов,
// то есть ядро платформы под менеджерами. Db.CheckCodeAsync / CheckMetadataAsync
// — единственная дверь к RuntimeCompiler из скрипта; никакой менеджер её не
// перекрывает, и данных, которые можно было бы завести типизированно, тут нет.
public partial class TbKernelCompilerTests
{
    [IntegrationTest("Ядро: компилятор — валидный код и диагностика ошибок")]
    public async Task CompilerDiagnostics()
    {
        var ok = await Db.CheckCodeAsync("public class KernelProbeOk { public int Answer() { return 42; } }");
        Assert.IsTrue(ok.Success, "валидный сниппет компилируется: {0}", string.Join("; ", ok.Errors));

        var bad = await Db.CheckCodeAsync("public class KernelProbeBad { public int Answer() { return \"not a number\"; } }");
        Assert.IsFalse(bad.Success, "битый сниппет обязан провалить компиляцию");
        Assert.IsTrue(bad.Errors.Count > 0, "диагностика не пуста");
        Assert.IsTrue(bad.Errors[0].Contains("CS"), "ошибка несёт код компилятора: {0}", bad.Errors[0]);
        Log("Диагностика: " + bad.Errors[0]);
    }

    [IntegrationTest("Ядро: генерация классов всех метаданных зелёная")]
    public async Task MetadataGenerationCompiles()
    {
        var all = await Db.CheckMetadataAsync();
        Assert.IsTrue(all.Success, "классы всех метаданных генерируются и компилируются: {0}", string.Join("; ", all.Errors));
        Log("Полная генерация метаданных компилируется начисто.");
    }
}