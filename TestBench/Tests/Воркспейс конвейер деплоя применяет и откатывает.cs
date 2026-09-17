using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Пакет с новым словарём: validate видит create, apply кладёт снапшот и
// создаёт строку (validate после — ноль create), rollback по манифесту
// удаляет созданное (validate снова видит create). Блокированный пакет
// (даунгрейд версии) apply отклоняет без записи.
public class WorkspaceDeployTest : IntegrationTestScriptBase
{
    [IntegrationTest("Workspace: deploy pipeline applies and rolls back")]
    public async Task DeployAppliesAndRollsBack()
    {
        var root = Path.Combine(Path.GetTempPath(), "zuloone-ws-deploy");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        try
        {
            await Db.ExportWorkspaceAsync(root);
            var modelJson = await File.ReadAllTextAsync(Path.Combine(root, "TestBench", "model.json"));
            var marker = "\"metaId\": \"";
            var at = modelJson.IndexOf(marker, StringComparison.Ordinal);
            var modelId = modelJson.Substring(at + marker.Length, 36);

            var dir = Path.Combine(root, "TestBench", "Dictionaries", "TBWsDeployDict");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "TBWsDeployDict.object.json"),
                "{ \"kind\": \"Dictionary\", \"object\": { " +
                $"\"metaId\": \"{Guid.NewGuid()}\", " +
                "\"name\": \"TBWsDeployDict\", \"caption\": \"TBWsDeployDict\", " +
                $"\"modelId\": \"{modelId}\", \"layerId\": 1 }} }}");

            var before = await Db.ValidateWorkspaceAsync(root);
            Assert.IsTrue(before.WouldCreate >= 1, "пакет несёт новый объект, wouldCreate={0}", before.WouldCreate);

            var deploy = await Db.DeployWorkspaceAsync(root);
            Assert.IsTrue(deploy.Applied, "деплой применился: {0}", string.Join("; ", deploy.Errors));
            Assert.IsTrue(deploy.Created >= 1, "строка создана, created={0}", deploy.Created);
            Assert.IsTrue(deploy.SnapshotPath != null && File.Exists(Path.Combine(deploy.SnapshotPath, "apply-manifest.json")),
                "снапшот и манифест на месте: {0}", deploy.SnapshotPath);

            var after = await Db.ValidateWorkspaceAsync(root);
            Assert.IsTrue(after.WouldCreate == 0, "после деплоя пакет идемпотентен, wouldCreate={0}", after.WouldCreate);

            var rollback = await Db.RollbackWorkspaceAsync(deploy.SnapshotPath);
            Assert.IsTrue(rollback.RolledBack, "откат прошёл: {0}", string.Join("; ", rollback.Errors));

            var restored = await Db.ValidateWorkspaceAsync(root);
            Assert.IsTrue(restored.WouldCreate >= 1, "созданное удалено откатом, wouldCreate={0}", restored.WouldCreate);

            if (deploy.SnapshotPath != null && Directory.Exists(deploy.SnapshotPath))
                Directory.Delete(deploy.SnapshotPath, true);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}