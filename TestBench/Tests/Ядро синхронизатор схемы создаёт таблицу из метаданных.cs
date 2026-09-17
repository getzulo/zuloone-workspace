// Ядро платформы: синхронизатор схемы. Метаданные нового справочника
// превращаются в физическую таблицу (DDL), данные пишутся и читаются — и всё
// это внутри транзакции кейса, так что DDL откатывается вместе с ней.
public partial class TbKernelSchemaSyncTests
{
    [IntegrationTest("Ядро: синхронизатор схемы создаёт таблицу из метаданных")]
    public async Task SchemaSyncCreatesTable()
    {
        await Db.CreateDictionaryAsync("TBKernelSchemaProbe", new Dictionary<string, string>
        {
            ["Code"] = "String",
            ["Qty"] = "Decimal",
        });
        await Db.SyncSchemaAsync();

        var id = await Db.InsertAsync("TBKernelSchemaProbe", new Dictionary<string, object?> { ["Code"] = "PROBE-1", ["Qty"] = 5m });
        var row = await Db.GetAsync("TBKernelSchemaProbe", id);
        Assert.IsNotNull(row, "запись читается из свежесозданной таблицы");
        Assert.AreEqual("PROBE-1", row!["Code"], "строковое поле сохранилось");
        Assert.AreEqual(5m, System.Convert.ToDecimal(row["Qty"]), "десятичное поле сохранилось");
        Log("Метаданные → DDL → вставка → чтение: весь конвейер живой; таблица откатится вместе с транзакцией.");
    }
}