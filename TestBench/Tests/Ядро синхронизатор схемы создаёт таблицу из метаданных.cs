// Ядро платформы: синхронизатор схемы. Метаданные нового справочника
// превращаются в физическую таблицу (DDL), данные пишутся и читаются — и всё
// это внутри транзакции кейса, так что DDL откатывается вместе с ней.
//
// Справочник рождается в РАНТАЙМЕ, поэтому генерённого класса у него нет и
// типизировать запись нечем. Но менеджер умеет и по имени: NewRecordAsync даёт
// шаблон полей ИЗ МЕТАДАННЫХ, Save/GetRecordAsync ходят туда же, куда и
// типизированные перегрузки. На Db остаются только CreateDictionaryAsync и
// SyncSchemaAsync — метаданные и DDL менеджеры не заводят, и ровно они здесь
// и есть предмет проверки.
public partial class TbKernelSchemaSyncTests
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Ядро: синхронизатор схемы создаёт таблицу из метаданных")]
    public async Task SchemaSyncCreatesTable()
    {
        await Db.CreateDictionaryAsync("TBKernelSchemaProbe", new Dictionary<string, string>
        {
            ["Code"] = "String",
            ["Qty"] = "Decimal",
        });
        await Db.SyncSchemaAsync();

        // Шаблон собирается по МЕТАДАННЫМ: если синхронизатор не увидел полей,
        // ошибка вылезет здесь, а не в невнятном отказе вставки.
        var record = await DictionaryManager.NewRecordAsync("TBKernelSchemaProbe");
        Assert.IsTrue(record.ContainsKey("Code") && record.ContainsKey("Qty"),
            "метаданные свежесозданного справочника несут оба поля: {0}", string.Join(", ", record.Keys));
        record["Code"] = "PROBE-1";
        record["Qty"] = 5m;

        var id = await DictionaryManager.SaveRecordAsync("TBKernelSchemaProbe", record);
        var row = await DictionaryManager.GetRecordAsync("TBKernelSchemaProbe", id);
        Assert.IsNotNull(row, "запись читается из свежесозданной таблицы");
        Assert.AreEqual("PROBE-1", row!["Code"], "строковое поле сохранилось");
        Assert.AreEqual(5m, System.Convert.ToDecimal(row["Qty"]), "десятичное поле сохранилось");
        Log("Метаданные → DDL → вставка → чтение: весь конвейер живой; таблица откатится вместе с транзакцией.");
    }
}
