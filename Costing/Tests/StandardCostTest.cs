using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Dated standard beats a missing cover (zero). Overlapping windows of
// the same item are refused on input. Variance is qty × standard − actual.
public class StandardCostTest : IntegrationTestScriptBase
{
    private static IStandardCostService Svc => GetService<IStandardCostService>();
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private static readonly DateTime Origin = new(2026, 1, 1);
    private static readonly DateTime Mid = new(2026, 6, 15);

    [IntegrationTest("Норматив на дату бьёт ноль, когда окна нет")]
    public async Task CoveringWindowBeatsMissingZero()
    {
        var item = await NewItemAsync();
        Assert.IsTrue(await Svc.OfAsync(item, Mid) == 0m, "без окна — ноль");

        await NewCostAsync(item, 12.5m, Origin);
        Assert.IsTrue(await Svc.OfAsync(item, Mid) == 12.5m,
            "открытое окно покрывает дату, факт {0}", await Svc.OfAsync(item, Mid));
        Assert.IsTrue(await Svc.OfAsync(item, Origin.AddDays(-1)) == 0m,
            "до начала окна — ноль");
    }

    [IntegrationTest("Истёкшее окно не подменяет следующее")]
    public async Task ClosedWindowYieldsToTheNext()
    {
        var item = await NewItemAsync();
        await NewCostAsync(item, 10m, Origin, new DateTime(2026, 3, 31));
        await NewCostAsync(item, 14m, new DateTime(2026, 4, 1));

        Assert.IsTrue(await Svc.OfAsync(item, new DateTime(2026, 3, 31)) == 10m,
            "последний день старого окна");
        Assert.IsTrue(await Svc.OfAsync(item, new DateTime(2026, 4, 1)) == 14m,
            "первое число нового окна");
    }

    [IntegrationTest("Пересечение окон одной номенклатуры отклоняется при вводе")]
    public async Task OverlappingWindowIsRejected()
    {
        var item = await NewItemAsync();
        await NewCostAsync(item, 10m, Origin);

        var clash = DictionaryManager.NewRecord<StandardCost>();
        clash.Item = item;
        clash.Amount = 11m;
        clash.EffectiveFrom = new DateTime(2026, 4, 1);

        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(clash); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("уже есть норматив"),
            "пересечение обязано быть отклонено на вводе, факт: {0}", reason);

        var found = await Svc.FindOverlappingAsync(
            item, Guid.Empty, new DateTime(2026, 4, 1), null);
        Assert.IsNotNull(found, "сервис видит то же пересечение, что и обработчик");
    }

    [IntegrationTest("План-факт: количество × норматив минус факт")]
    public async Task VarianceIsPlanMinusActual()
    {
        var item = await NewItemAsync();
        await NewCostAsync(item, 8m, Origin);

        var cheaper = await Svc.VarianceAsync(item, 10m, 70m, Mid);
        Assert.IsTrue(cheaper == 10m, "10×8 − 70 = 10, факт {0}", cheaper);

        var dearer = await Svc.VarianceAsync(item, 10m, 90m, Mid);
        Assert.IsTrue(dearer == -10m, "10×8 − 90 = −10, факт {0}", dearer);

        var unknown = await Svc.VarianceAsync(Db.NewId(), 10m, 70m, Mid);
        Assert.IsTrue(unknown == -70m, "без норматива план ноль, факт {0}", unknown);
    }

    private async Task<Guid> NewItemAsync()
    {
        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PCS-{Db.NewId():N}"[..12];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"GRP-{Db.NewId():N}"[..12];
        group.Name = "Merchandise";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item = await DictionaryManager.SaveRecordAsync(item);
        return item.MetaId;
    }

    private async Task NewCostAsync(
        Guid item, decimal amount, DateTime from, DateTime? to = null)
    {
        var row = DictionaryManager.NewRecord<StandardCost>();
        row.Item = item;
        row.Amount = amount;
        row.EffectiveFrom = from;
        row.EffectiveTo = to;
        await DictionaryManager.SaveRecordAsync(row);
    }
}
