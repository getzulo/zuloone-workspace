using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Вставка 'chaincheck': база аперкейсит → CHAINCHECK, расширение после
// дописывает '-ext' В НИЖНЕМ регистре (если бы оно шло первым, база бы его
// аперкейснула — порядок доказуем по регистру суффикса) и только при
// PreviousResult.Success. FORBIDDEN отклоняется базой — цепочка рвётся.
//
// Запись создаётся ТИПИЗИРОВАННО через IDictionaryManager: цепочка обработчиков
// должна отрабатывать на той же двери, которой пользуется продакшн-код.
public class EventChainTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Workspace: layered event handler chain")]
    public async Task ChainRunsBaseThenExtension()
    {
        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = "chaincheck";
        warehouse = await DictionaryManager.SaveRecordAsync(warehouse);
        Assert.IsTrue(warehouse.MetaId != Guid.Empty, "запись создана");

        var stored = await DictionaryManager.GetRecordAsync<TBWarehouse>(warehouse.MetaId);
        Assert.IsTrue(stored != null, "запись читается обратно");
        Assert.IsTrue(stored!.Name == "CHAINCHECK-ext",
            "база аперкейснула, расширение ПОСЛЕ дописало '-ext': '{0}'", stored.Name);

        var forbidden = DictionaryManager.NewRecord<TBWarehouse>();
        forbidden.Name = "FORBIDDEN";
        var rejected = false;
        try { await DictionaryManager.SaveRecordAsync(forbidden); }
        catch (Exception) { rejected = true; }
        Assert.IsTrue(rejected, "FORBIDDEN отклонён базовым звеном цепочки");
    }
}