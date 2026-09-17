using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Новый иерархический словарь заводится файлом: применение создаёт строку И
// материализует системные поля (ID + ParentId) — не дожидаясь рестарта
// сервера; повторный экспорт отражает их в object.json.
public class WorkspaceInvariantsTest : IntegrationTestScriptBase
{
    [IntegrationTest("Workspace: created dictionary gets system fields")]
    public async Task FileCreatedDictionaryGetsSystemFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "zuloone-ws-invariants");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        try
        {
            await Db.ExportWorkspaceAsync(root);
            var modelJson = await File.ReadAllTextAsync(Path.Combine(root, "TestBench", "model.json"));
            var marker = "\"metaId\": \"";
            var at = modelJson.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "у model.json есть metaId");
            var modelId = modelJson.Substring(at + marker.Length, 36);

            var dir = Path.Combine(root, "TestBench", "Dictionaries", "TBWsInvDict");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "TBWsInvDict.object.json");
            await File.WriteAllTextAsync(file,
                "{ \"kind\": \"Dictionary\", \"object\": { " +
                $"\"metaId\": \"{Guid.NewGuid()}\", " +
                "\"name\": \"TBWsInvDict\", \"caption\": \"TBWsInvDict\", \"isHierarchical\": true, " +
                $"\"modelId\": \"{modelId}\", \"layerId\": 1 }} }}");

            var apply = await Db.ApplyWorkspaceFileAsync(root, file);
            Assert.IsTrue(apply.Errors.Count == 0, "применение без ошибок: {0}", string.Join("; ", apply.Errors));
            Assert.IsTrue(apply.Created >= 1, "словарь создан из файла, created={0}", apply.Created);
            Assert.IsTrue(apply.InvariantsAdded >= 2,
                "инварианты дизайнера отработали (ID + ParentId), invariantsAdded={0}", apply.InvariantsAdded);

            await Db.ExportWorkspaceAsync(root);
            var exported = await File.ReadAllTextAsync(file);
            Assert.IsTrue(exported.Contains("\"fieldName\": \"ID\""), "экспорт содержит системное поле ID");
            Assert.IsTrue(exported.Contains("\"fieldName\": \"ParentId\""), "экспорт содержит ParentId иерархии");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}