using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Пакет с новой моделью БЕЗ зависимостей, чей EDT ссылается на словарь
// TestBench → validate репортит dependencyViolation (нестрогий режим —
// warnings, не blocked). Та же ссылка из TestBenchExt (зависимость
// задекларирована) — чисто.
//
// ОСОЗНАННОЕ ИСКЛЮЧЕНИЕ: тест целиком остаётся на Db. Здесь нет ни записей, ни
// документов, ни движений — предмет проверки это ИНСТРУМЕНТ воркспейса
// (export-all + validate над каталогом файлов), которого нет ни у одного
// менеджера: менеджеры работают с бизнес-данными, а не с пакетом метаданных.
public class WorkspaceDependencyGateTest : IntegrationTestScriptBase
{
    [IntegrationTest("Workspace: dependency closure gate")]
    public async Task CrossModelReferenceOutsideClosureIsReported()
    {
        var root = Path.Combine(Path.GetTempPath(), "zuloone-ws-depgate");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        try
        {
            await Db.ExportWorkspaceAsync(root);
            var whJson = await File.ReadAllTextAsync(
                Path.Combine(root, "TestBench", "Dictionaries", "TBWarehouse", "TBWarehouse.object.json"));
            var marker = "\"metaId\": \"";
            var at = whJson.IndexOf(marker, StringComparison.Ordinal);
            var warehouseId = whJson.Substring(at + marker.Length, 36);

            // Новая модель без зависимостей + её EDT-ссылка на чужой словарь.
            var modelId = Guid.NewGuid();
            var noDepsDir = Path.Combine(root, "TBNoDeps");
            Directory.CreateDirectory(Path.Combine(noDepsDir, "EDTs"));
            await File.WriteAllTextAsync(Path.Combine(noDepsDir, "model.json"),
                "{ \"kind\": \"Model\", \"object\": { " +
                $"\"metaId\": \"{modelId}\", \"name\": \"TBNoDeps\", \"layerId\": 2, " +
                "\"modelType\": \"Custom\", \"isEnabled\": true, \"isSystem\": false, " +
                "\"modelId\": \"00000000-0000-0000-0000-000000000000\" } }");
            await File.WriteAllTextAsync(Path.Combine(noDepsDir, "EDTs", "TBNoDepsRef.json"),
                "{ \"kind\": \"EDT\", \"object\": { " +
                $"\"metaId\": \"{Guid.NewGuid()}\", \"name\": \"TBNoDepsRef\", \"edtType\": \"Reference\", " +
                $"\"referenceDictionaryMetaId\": \"{warehouseId}\", " +
                $"\"modelId\": \"{modelId}\", \"layerId\": 2 }} }}");

            var report = await Db.ValidateWorkspaceAsync(root);
            Assert.IsTrue(report.DependencyViolations.Any(v => v.Contains("TBNoDeps")),
                "ссылка вне замыкания зарепорчена: {0}", string.Join("; ", report.DependencyViolations));
            Assert.IsTrue(report.Verdict != "blocked",
                "нестрогий режим не блокирует, verdict={0}", report.Verdict);
            Assert.IsTrue(!report.DependencyViolations.Any(v => v.Contains("TestBenchExt")),
                "задекларированная зависимость TestBenchExt→TestBench чиста: {0}",
                string.Join("; ", report.DependencyViolations.Where(v => v.Contains("TestBenchExt"))));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}