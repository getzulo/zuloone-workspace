using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Вставка 'chaincheck': база аперкейсит → CHAINCHECK, расширение после
// дописывает '-ext' В НИЖНЕМ регистре (если бы оно шло первым, база бы его
// аперкейснула — порядок доказуем по регистру суффикса) и только при
// PreviousResult.Success. FORBIDDEN отклоняется базой — цепочка рвётся.
public class EventChainTest : IntegrationTestScriptBase
{
    [IntegrationTest("Workspace: layered event handler chain")]
    public async Task ChainRunsBaseThenExtension()
    {
        var id = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = "chaincheck" });
        var row = await Db.GetAsync("TBWarehouse", id);
        Assert.IsTrue(row != null, "запись создана");
        var name = row["Name"] as string;
        Assert.IsTrue(name == "CHAINCHECK-ext",
            "база аперкейснула, расширение ПОСЛЕ дописало '-ext': '{0}'", name);

        var rejected = false;
        try { await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = "FORBIDDEN" }); }
        catch (Exception) { rejected = true; }
        Assert.IsTrue(rejected, "FORBIDDEN отклонён базовым звеном цепочки");
    }
}