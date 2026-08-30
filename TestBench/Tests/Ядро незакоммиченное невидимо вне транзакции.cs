// Ядро СУБД-слоя: транзакционная изоляция. Запись видна ВНУТРИ транзакции
// кейса и не видна снаружи (READPAST пропускает незакоммиченные строки) —
// а значит откат раннера действительно не оставляет следов.
public partial class TbKernelIsolationTests
{
    [IntegrationTest("Ядро: незакоммиченное невидимо вне транзакции")]
    public async Task UncommittedInvisibleOutside()
    {
        var probe = "ISO-" + System.Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
        var filter = "[Name] = '" + probe + "'";

        Assert.AreEqual(0, await Db.CountAsync("TBWarehouse", filter), "до вставки записи нет нигде");
        await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = probe });

        Assert.AreEqual(1, await Db.CountAsync("TBWarehouse", filter), "внутри транзакции запись видна");
        Assert.AreEqual(0, await Db.CountCommittedAsync("TBWarehouse", filter), "снаружи транзакции записи НЕТ — она не закоммичена");
        Log("Изоляция подтверждена: внутри 1, снаружи (committed) 0.");
    }
}