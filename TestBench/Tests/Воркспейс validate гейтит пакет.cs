using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Validate ничего не пишет: чистый экспорт не блокируется и ничего не создаст;
// даунгрейд версии модели (стенд 1.2.0, пакет 0.9.0) блокируется; апгрейд
// проходит и виден направлением; висячая metaId-ссылка блокирует пакет.
//
// НАМЕРЕННО целиком на харнессе. Здесь нечего переводить на менеджеры: тест не
// трогает бизнес-данные вообще, он проверяет платформенный конвейер воркспейса
// (export → validate → apply) над МЕТАданными, а менеджера у этого конвейера нет.
// Db.ExportWorkspaceAsync / ValidateWorkspaceAsync / ApplyWorkspaceFileAsync — его
// единственный фасад; альтернатива — доставать из DI сами WorkspaceExportService
// и WorkspaceValidationService, то есть спускаться НИЖЕ харнесса, а не выше.
public class WorkspaceValidateTest : IntegrationTestScriptBase
{
    [IntegrationTest("Workspace: validate gates the package")]
    public async Task ValidateGatesThePackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "zuloone-ws-validate");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        try
        {
            await Db.ExportWorkspaceAsync(root);
            var modelFile = Path.Combine(root, "TestBench", "model.json");
            var original = await File.ReadAllTextAsync(modelFile);

            var clean = await Db.ValidateWorkspaceAsync(root);
            Assert.IsTrue(clean.Verdict != "blocked", "чистый экспорт не блокируется: {0}",
                string.Join("; ", clean.Errors.Concat(clean.UnresolvedReferences)));
            Assert.IsTrue(clean.WouldCreate == 0, "идемпотентный пакет ничего не создаёт, wouldCreate={0}", clean.WouldCreate);

            // Стенд получает версию 1.2.0 через live-apply; пакет с 0.9.0 — даунгрейд.
            var versioned = original.Replace("\"name\": \"TestBench\"", "\"name\": \"TestBench\",\n  \"modelVersion\": \"1.2.0\"");
            Assert.IsTrue(versioned != original, "якорь name найден в model.json");
            await File.WriteAllTextAsync(modelFile, versioned);
            var applied = await Db.ApplyWorkspaceFileAsync(root, modelFile);
            Assert.IsTrue(applied.Errors.Count == 0 && applied.Updated >= 1, "версия 1.2.0 применилась к стенду");

            await File.WriteAllTextAsync(modelFile, versioned.Replace("\"modelVersion\": \"1.2.0\"", "\"modelVersion\": \"0.9.0\""));
            var downgrade = await Db.ValidateWorkspaceAsync(root);
            Assert.IsTrue(downgrade.Verdict == "blocked", "даунгрейд 1.2.0 → 0.9.0 блокируется, verdict={0}", downgrade.Verdict);
            Assert.IsTrue(downgrade.Errors.Any(e => e.Contains("downgrade")), "ошибка называет даунгрейд: {0}", string.Join("; ", downgrade.Errors));

            await File.WriteAllTextAsync(modelFile, versioned.Replace("\"modelVersion\": \"1.2.0\"", "\"modelVersion\": \"1.3.0\""));
            var upgrade = await Db.ValidateWorkspaceAsync(root);
            Assert.IsTrue(upgrade.Verdict != "blocked", "апгрейд не блокируется: {0}", string.Join("; ", upgrade.Errors));
            Assert.IsTrue(upgrade.Models.Any(m => m.Contains("upgrade")), "направление upgrade видно: {0}", string.Join("; ", upgrade.Models));

            // Висячая ссылка: reference-EDT указывает на несуществующий словарь.
            await File.WriteAllTextAsync(modelFile, original);
            var edtFile = Directory.GetFiles(root, "TBWarehouseRef*.json", SearchOption.AllDirectories).First();
            var edtJson = await File.ReadAllTextAsync(edtFile);
            var marker = "\"referenceDictionaryMetaId\": \"";
            var at = edtJson.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "у TBWarehouseRef есть ссылка на словарь");
            var broken = edtJson.Remove(at + marker.Length, 36).Insert(at + marker.Length, Guid.NewGuid().ToString());
            await File.WriteAllTextAsync(edtFile, broken);
            var dangling = await Db.ValidateWorkspaceAsync(root);
            Assert.IsTrue(dangling.Verdict == "blocked", "висячая ссылка блокирует, verdict={0}", dangling.Verdict);
            Assert.IsTrue(dangling.UnresolvedReferences.Count >= 1, "unresolved reference зарепорчен");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}