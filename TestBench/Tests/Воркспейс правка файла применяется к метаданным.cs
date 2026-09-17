using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Live-синк, направление файл → ZuloOne: экспорт, правка caption в
// object.json, применение (updated ≥ 1), эхоподавление (повторное применение
// ничего не меняет), и экспорт после правки отражает новое состояние БД.
public class WorkspaceLiveApplyTest : IntegrationTestScriptBase
{
    [IntegrationTest("Workspace: file edit applies to metadata")]
    public async Task FileEditAppliesAndIsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "zuloone-ws-liveapply");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var file = Path.Combine(root, "TestBench", "Dictionaries", "TBWarehouse", "TBWarehouse.object.json");
        string original = null;
        try
        {
            await Db.ExportWorkspaceAsync(root);
            Assert.IsTrue(File.Exists(file), "экспортированный файл словаря на месте");
            original = await File.ReadAllTextAsync(file);
            var marked = original.Replace("\"caption\": \"TBWarehouse\"", "\"caption\": \"TBWarehouse WS\"");
            Assert.IsTrue(marked != original, "якорь caption найден в файле");
            await File.WriteAllTextAsync(file, marked);

            var first = await Db.ApplyWorkspaceFileAsync(root, file);
            Assert.IsTrue(first.Errors.Count == 0, "применение без ошибок: {0}", string.Join("; ", first.Errors));
            Assert.IsTrue(first.Updated >= 1, "правка файла обновляет строку, updated={0}", first.Updated);

            var second = await Db.ApplyWorkspaceFileAsync(root, file);
            Assert.IsTrue(second.Updated == 0 && second.Created == 0,
                "повторное применение — эхо подавлено (updated={0}, created={1})", second.Updated, second.Created);

            await Db.ExportWorkspaceAsync(root);
            var exported = await File.ReadAllTextAsync(file);
            Assert.IsTrue(exported.Contains("TBWarehouse WS"), "экспорт отражает применённый caption");
        }
        finally
        {
            if (original != null)
            {
                await File.WriteAllTextAsync(file, original);
                await Db.ApplyWorkspaceFileAsync(root, file);
            }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}