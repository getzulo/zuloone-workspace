using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Воркспейс: биекция Meta*-таблиц на раскладку папки. Экспорт кладёт дизайн
// стендовых объектов по каноническим путям (json = структура, cs = код), а
// повторный импорт той же папки ничего не создаёт и не падает.
public class WorkspaceRoundTripTest : IntegrationTestScriptBase
{
    [IntegrationTest("Workspace: export-all → import-all round-trip")]
    public async Task ExportImportRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "zuloone-ws-roundtrip");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        try
        {
            var export = await Db.ExportWorkspaceAsync(root);
            Assert.IsTrue(export.Files > 0, "экспорт должен записать файлы");
            var bench = Path.Combine(root, "TestBench");
            Assert.IsTrue(File.Exists(Path.Combine(bench, "model.json")),
                "манифест модели TestBench на месте");
            Assert.IsTrue(File.Exists(Path.Combine(bench, "Dictionaries", "TBWarehouse", "TBWarehouse.object.json")),
                "дизайн справочника TBWarehouse на месте");
            Assert.IsTrue(File.Exists(Path.Combine(bench, "Documents", "TBStockDoc", "TBStockDoc.object.json")),
                "дизайн документа TBStockDoc на месте");
            var csFiles = Directory.GetFiles(bench, "*.cs", SearchOption.AllDirectories);
            Assert.IsTrue(csFiles.Any(f => f.EndsWith("TBReceiptTx.cs", StringComparison.Ordinal)),
                "код транзакционного скрипта TBReceiptTx.cs лежит у своего документа");

            var import = await Db.ImportWorkspaceAsync(root);
            Assert.IsTrue(import.Errors.Count == 0,
                "импорт без ошибок: {0}", string.Join("; ", import.Errors));
            Assert.IsTrue(import.Created == 0,
                "повторный импорт идемпотентен, а создал строк: {0}", import.Created);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}