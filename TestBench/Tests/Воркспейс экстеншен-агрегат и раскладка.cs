using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Поле ExtNote (модель TestBenchExt) на словаре TBWarehouse (TestBench):
// экспорт кладёт его в TestBenchExt/DictionaryExtensions/TBWarehouse.TestBenchExt/
// *.extension.json, а object.json владельца поля НЕ содержит; папка Core
// девственно чиста. Повторный импорт идемпотентен (round-trip тест).
//
// НАМЕРЕННО на харнессе: единственный вызов платформы здесь —
// Db.ExportWorkspaceAsync, и проверяется РАСКЛАДКА выгруженных файлов, то есть
// сам экспорт метаданных. Бизнес-данных тест не создаёт, менеджеру тут нечего
// делать, а менеджерского фасада у конвейера воркспейса и не существует.
public class ExtensionLayoutTest : IntegrationTestScriptBase
{
    [IntegrationTest("Workspace: extension aggregate layout")]
    public async Task ForeignFieldLivesInExtensionFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "zuloone-ws-extlayout");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        try
        {
            await Db.ExportWorkspaceAsync(root);

            var extFile = Path.Combine(root, "TestBenchExt", "DictionaryExtensions",
                "TBWarehouse.TestBenchExt", "TBWarehouse.TestBenchExt.extension.json");
            Assert.IsTrue(File.Exists(extFile), "конверт экстеншена на месте: {0}", extFile);
            var extJson = await File.ReadAllTextAsync(extFile);
            Assert.IsTrue(extJson.Contains("\"ExtNote\""), "поле ExtNote внутри экстеншена");

            var ownerFile = Path.Combine(root, "TestBench", "Dictionaries", "TBWarehouse", "TBWarehouse.object.json");
            var ownerJson = await File.ReadAllTextAsync(ownerFile);
            Assert.IsTrue(!ownerJson.Contains("\"ExtNote\""), "object.json владельца НЕ содержит чужое поле");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}