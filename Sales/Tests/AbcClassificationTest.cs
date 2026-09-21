using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class AbcClassificationTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dictionaries => GetService<IDictionaryManager>();
    private static IAbcClassifier Classifier => GetService<IAbcClassifier>();
    private static IInformationRegisterService Info => GetService<IInformationRegisterService>();
    private static ISqlService Sql => GetService<ISqlService>();

    private static readonly Guid SalesModel = Guid.Parse("47861dd2-1009-4926-9ea3-c506cae5118d");

    private static readonly DateTime AsOf = new DateTime(2026, 9, 1);

    [IntegrationTest("Парето 80/15/5 даёт A/B/C по накопленной доле")]
    public async Task ParetoAssignsAbc()
    {
        var profile = await ProfileAsync("Item");
        await ParetoBandsAsync(profile.MetaId);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        await Classifier.RecalcWithScoresAsync(profile.MetaId, AsOf, new Dictionary<Guid, decimal>
        {
            [a] = 80m,
            [b] = 15m,
            [c] = 5m,
        });

        Assert.AreEqual("A", await AbcOf(profile.MetaId, a));
        Assert.AreEqual("B", await AbcOf(profile.MetaId, b));
        Assert.AreEqual("C", await AbcOf(profile.MetaId, c));
    }

    [IntegrationTest("Пересечение полос отклоняет пересчёт, журнал не пишется")]
    public async Task OverlappingBandsFail()
    {
        var profile = await ProfileAsync("Item");
        await BandAsync(profile.MetaId, "Abc", "A", 0m, 80m);
        await BandAsync(profile.MetaId, "Abc", "B", 0m, 50m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Classifier.RecalcWithScoresAsync(profile.MetaId, AsOf, new Dictionary<Guid, decimal>
            {
                [Guid.NewGuid()] = 10m,
            }));

        var slice = await Info.SliceLastAsync("AbcClassification", AsOf,
            new Dictionary<string, object?> { ["Profile"] = profile.MetaId });
        Assert.AreEqual(0, slice.Count);
    }

    [IntegrationTest("ManualAbc сохраняет класс при втором пересчёте")]
    public async Task ManualAbcSurvives()
    {
        var profile = await ProfileAsync("Item");
        await ParetoBandsAsync(profile.MetaId);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var scores = new Dictionary<Guid, decimal> { [a] = 80m, [b] = 15m, [c] = 5m };
        await Classifier.RecalcWithScoresAsync(profile.MetaId, AsOf, scores);

        await Info.SetAsync("AbcClassification", AsOf,
            new Dictionary<string, object?> { ["Profile"] = profile.MetaId, ["Subject"] = c },
            new Dictionary<string, object?>
            {
                ["AbcClass"] = "A",
                ["XyzClass"] = "",
                ["Score"] = 5m,
                ["Share"] = 5m,
                ["Cv"] = 0m,
                ["ManualAbc"] = true,
                ["ManualXyz"] = false,
            });

        await Classifier.RecalcWithScoresAsync(profile.MetaId, AsOf, scores);
        Assert.AreEqual("A", await AbcOf(profile.MetaId, c));
        Assert.AreEqual("A", await AbcOf(profile.MetaId, a));
        Assert.AreEqual("B", await AbcOf(profile.MetaId, b));
    }

    [IntegrationTest("Профили Item и Customer не смешивают Subject")]
    public async Task ItemAndCustomerStayApart()
    {
        var items = await ProfileAsync("Item");
        var customers = await ProfileAsync("Customer");
        await ParetoBandsAsync(items.MetaId);
        await ParetoBandsAsync(customers.MetaId);
        var item = Guid.NewGuid();
        var customer = Guid.NewGuid();

        await Classifier.RecalcWithScoresAsync(items.MetaId, AsOf,
            new Dictionary<Guid, decimal> { [item] = 100m });
        await Classifier.RecalcWithScoresAsync(customers.MetaId, AsOf,
            new Dictionary<Guid, decimal> { [customer] = 100m });

        var itemSlice = await Info.SliceLastAsync("AbcClassification", AsOf,
            new Dictionary<string, object?> { ["Profile"] = items.MetaId });
        var customerSlice = await Info.SliceLastAsync("AbcClassification", AsOf,
            new Dictionary<string, object?> { ["Profile"] = customers.MetaId });
        Assert.AreEqual(1, itemSlice.Count);
        Assert.AreEqual(1, customerSlice.Count);
        Assert.AreEqual(item, AsGuid(itemSlice[0], "Subject"));
        Assert.AreEqual(customer, AsGuid(customerSlice[0], "Subject"));
    }

    [IntegrationTest("Customer + StockQty на пересчёте — ошибка")]
    public async Task CustomerStockQtyRefused()
    {
        var profile = Dictionaries.NewRecord<AbcProfile>();
        profile.Name = $"ABC stock {Guid.NewGuid():N}"[..28];
        profile.Subject = "Customer";
        profile.Measure = "StockQty";
        profile.WindowMonths = 12;
        profile.BucketCount = 12;
        profile.AbcMethod = "CumulativeShare";
        profile.XyzMethod = "None";
        profile = await Dictionaries.SaveRecordAsync(profile);
        await ParetoBandsAsync(profile.MetaId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Classifier.RecalcAsync(profile.MetaId, AsOf));
    }

    [IntegrationTest("Ночной пересчёт пропускает отключённый профиль")]
    public async Task RecalcAllEnabledSkipsDisabled()
    {
        var live = await ProfileAsync("Item");
        await ParetoBandsAsync(live.MetaId);
        var dead = await ProfileAsync("Item");
        dead.IsDisabled = true;
        dead = await Dictionaries.SaveRecordAsync(dead);
        await ParetoBandsAsync(dead.MetaId);

        await Classifier.RecalcAllEnabledAsync(AsOf);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Classifier.RecalcAsync(dead.MetaId, AsOf));
    }

    [IntegrationTest("Задание RecalcAbcClassifications принадлежит Sales и исполняется")]
    public async Task RecalcJobIsOwnedAndRuns()
    {
        var rows = await Sql.SelectAsync(
            "SELECT [MetaId], [Name], [ModelId], [IsActive], [CronExpression], [ExecuteSingle] FROM [MetaJobs] WHERE [Name] = 'RecalcAbcClassifications'");
        Assert.IsTrue(rows.Count == 1, "задание на месте; факт {0}", rows.Count);
        Assert.IsTrue(Convert.ToString(rows[0]["ModelId"])!.ToLowerInvariant() == SalesModel.ToString("D"),
            "владелец — Sales; факт {0}", rows[0]["ModelId"]);
        Assert.IsTrue(Convert.ToBoolean(rows[0]["IsActive"]), "задание активно");
        Assert.IsTrue(Convert.ToBoolean(rows[0]["ExecuteSingle"]), "не два прогона сразу");
        Assert.IsTrue(Convert.ToString(rows[0]["CronExpression"]) == "0 2 * * *",
            "в 02:00 UTC; факт '{0}'", rows[0]["CronExpression"]);

        var jobId = Guid.Parse(Convert.ToString(rows[0]["MetaId"])!);
        var run = await Db.RunJobAsync(jobId);
        Assert.IsTrue(run.Success, "задание отработало; факт: {0}", run.Output);
        Assert.IsTrue(run.Output.Contains("rows=", StringComparison.Ordinal),
            "вывод несёт число строк; факт: {0}", run.Output);
    }

    private async Task<AbcProfile> ProfileAsync(string subject)
    {
        var profile = Dictionaries.NewRecord<AbcProfile>();
        profile.Name = $"ABC {subject} {Guid.NewGuid():N}"[..28];
        profile.Subject = subject;
        profile.Measure = "Revenue";
        profile.WindowMonths = 12;
        profile.BucketCount = 12;
        profile.AbcMethod = "CumulativeShare";
        profile.XyzMethod = "None";
        return await Dictionaries.SaveRecordAsync(profile);
    }

    private async Task ParetoBandsAsync(Guid profileId)
    {
        await BandAsync(profileId, "Abc", "A", 0m, 80m);
        await BandAsync(profileId, "Abc", "B", 80m, 95m);
        await BandAsync(profileId, "Abc", "C", 95m, null);
    }

    private async Task BandAsync(Guid profileId, string axis, string code, decimal from, decimal? to)
    {
        var band = Dictionaries.NewRecord<AbcClassBand>();
        band.Profile = profileId;
        band.Axis = axis;
        band.ClassCode = code;
        band.BoundFrom = from;
        if (to is decimal bound) band.BoundTo = bound;
        await Dictionaries.SaveRecordAsync(band);
    }

    private async Task<string> AbcOf(Guid profileId, Guid subject)
    {
        var slice = await Info.SliceLastAsync("AbcClassification", AsOf,
            new Dictionary<string, object?> { ["Profile"] = profileId, ["Subject"] = subject });
        Assert.IsTrue(slice.Count == 1, "нет среза для субъекта");
        return slice[0]["AbcClass"]?.ToString() ?? "";
    }

    private static Guid AsGuid(IDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var v) || v is null) return Guid.Empty;
        return v is Guid g ? g : Guid.TryParse(v.ToString(), out var p) ? p : Guid.Empty;
    }
}
