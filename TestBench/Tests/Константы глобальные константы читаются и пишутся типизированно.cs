// «Ядерные тесты.Константы»: реестр глобальных констант (MIQS Constant/ConstantGroup)
// через генерённый класс GlobalConstants — типизированные свойства, вложенные
// группы, динамический Get/SetAsync, легаси-контракт ошибок.
public partial class TbGlobalConstantTests
{
    [IntegrationTest("типизированные свойства: Decimal / String / Flag и группа")]
    public async Task TypedReads()
    {
        Assert.AreEqual(20m, GlobalConstants.TBVatRate, "Decimal через типизированное свойство");
        Assert.AreEqual("hello", GlobalConstants.TBGreeting, "String через типизированное свойство");
        Assert.IsTrue(GlobalConstants.TBFeatureOn == true, "Flag через типизированное свойство");
        Assert.AreEqual(20m, GlobalConstants.TBBench.TBVatRate, "вложенный доступ через класс группы");
        Assert.AreEqual(20m, GlobalConstants.Get<decimal>("TBVatRate"), "динамический Get<T>");
        Assert.AreEqual(20m, (decimal)GlobalConstants.Get("TBVatRate")!, "легаси-индексер (Get по имени)");
        Log("Типизированные свойства и группа TBBench прочитаны: 20 / hello / true.");
        await Task.CompletedTask;
    }

    [IntegrationTest("SetAsync меняет значение и восстанавливается")]
    public async Task WriteRoundTrip()
    {
        var original = GlobalConstants.TBVatRate ?? 0m;
        await GlobalConstants.SetAsync("TBVatRate", original + 1.5m);
        try
        {
            Assert.AreEqual(original + 1.5m, GlobalConstants.TBVatRate, "после SetAsync кэш инвалидирован и свойство видит новое значение");
        }
        finally
        {
            await GlobalConstants.SetAsync("TBVatRate", original);
        }
        Assert.AreEqual(original, GlobalConstants.TBVatRate, "значение восстановлено");
        Log("Запись и восстановление прошли: " + original + " → " + (original + 1.5m) + " → " + original + ".");
    }

    [IntegrationTest("неизвестное имя бросает (легаси-контракт)")]
    public async Task UnknownThrows()
    {
        Assert.Throws<ApplicationException>(
            () => { var _ = GlobalConstants.Get("TBNoSuchConstant"); },
            "легаси ConstantManager бросал ApplicationException «Constant not found»");
        await Task.CompletedTask;
    }
}