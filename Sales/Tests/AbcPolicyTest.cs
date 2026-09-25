using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class AbcPolicyTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dictionaries => GetService<IDictionaryManager>();
    private static IAbcPolicy Policy => GetService<IAbcPolicy>();
    private static IInformationRegisterService Info => GetService<IInformationRegisterService>();

    private static readonly DateTime AsOf = new DateTime(2026, 9, 1);

    [IntegrationTest("Держать 14 дней: расход 365 за 365 дней и остаток 10 дают к заказу 4")]
    public async Task KeepCoverSubtractsOnHand()
    {
        var profile = await ItemProfileAsync();
        var item = Guid.NewGuid();
        await ClassAsync(profile.MetaId, item, "A", "X");
        await CellAsync(profile.MetaId, "A", "X", AbcStockMode.Keep, 14);

        var n = await Policy.BuildWithDemandAsync(profile.MetaId, AsOf,
            new Dictionary<Guid, decimal> { [item] = 365m },
            new Dictionary<Guid, decimal> { [item] = 10m });

        Assert.AreEqual(1, n);
        var row = await SuggestionAsync(profile.MetaId, item);
        Assert.AreEqual("Keep", row["Mode"]?.ToString());
        Assert.AreEqual(14, Convert.ToInt32(row["CoverDays"], CultureInfo.InvariantCulture));
        Assert.IsTrue(Dec(row, "SuggestQty") == 4m, "к заказу 4; факт {0}", row["SuggestQty"]);
        Assert.IsTrue(Dec(row, "IssuedQty") == 365m, "расход 365; факт {0}", row["IssuedQty"]);
        Assert.IsTrue(Dec(row, "OnHandQty") == 10m, "остаток 10; факт {0}", row["OnHandQty"]);
    }

    [IntegrationTest("Под заказ: к заказу ноль, даже если остатка нет")]
    public async Task OrderSuggestsZero()
    {
        var profile = await ItemProfileAsync();
        var item = Guid.NewGuid();
        await ClassAsync(profile.MetaId, item, "C", "Z");
        await CellAsync(profile.MetaId, "C", "Z", AbcStockMode.Order, 0);

        await Policy.BuildWithDemandAsync(profile.MetaId, AsOf,
            new Dictionary<Guid, decimal> { [item] = 365m },
            new Dictionary<Guid, decimal> { [item] = 0m });

        var row = await SuggestionAsync(profile.MetaId, item);
        Assert.AreEqual("Order", row["Mode"]?.ToString());
        Assert.IsTrue(Dec(row, "SuggestQty") == 0m, "под заказ не держит запас; факт {0}", row["SuggestQty"]);
    }

    [IntegrationTest("Нет ячейки на пару классов — строки предложения нет")]
    public async Task MissingCellWritesNothing()
    {
        var profile = await ItemProfileAsync();
        var item = Guid.NewGuid();
        await ClassAsync(profile.MetaId, item, "B", "Y");

        var n = await Policy.BuildWithDemandAsync(profile.MetaId, AsOf,
            new Dictionary<Guid, decimal> { [item] = 100m },
            new Dictionary<Guid, decimal>());

        Assert.AreEqual(0, n);
        var slice = await Info.SliceLastAsync("AbcSuggestion", AsOf,
            new Dictionary<string, object?> { ["Profile"] = profile.MetaId });
        Assert.AreEqual(0, slice.Count);
    }

    [IntegrationTest("Режим «не задано» не пишет предложение и снимает прежнее")]
    public async Task UnspecifiedDropsTheRow()
    {
        var profile = await ItemProfileAsync();
        var item = Guid.NewGuid();
        await ClassAsync(profile.MetaId, item, "A", "Y");
        var cell = await CellAsync(profile.MetaId, "A", "Y", AbcStockMode.Keep, 14);
        await Policy.BuildWithDemandAsync(profile.MetaId, AsOf,
            new Dictionary<Guid, decimal> { [item] = 365m },
            new Dictionary<Guid, decimal> { [item] = 0m });

        cell.Mode = AbcStockMode.Unspecified;
        await Dictionaries.SaveRecordAsync(cell);
        var n = await Policy.BuildWithDemandAsync(profile.MetaId, AsOf,
            new Dictionary<Guid, decimal> { [item] = 365m },
            new Dictionary<Guid, decimal> { [item] = 0m });

        Assert.AreEqual(0, n);
        var slice = await Info.SliceLastAsync("AbcSuggestion", AsOf,
            new Dictionary<string, object?> { ["Profile"] = profile.MetaId, ["Subject"] = item });
        Assert.AreEqual(0, slice.Count);
    }

    [IntegrationTest("Остаток выше покрытия даёт ноль, не отрицательное количество")]
    public async Task SurplusSuggestsZero()
    {
        var profile = await ItemProfileAsync();
        var item = Guid.NewGuid();
        await ClassAsync(profile.MetaId, item, "A", "X");
        await CellAsync(profile.MetaId, "A", "X", AbcStockMode.Keep, 14);

        await Policy.BuildWithDemandAsync(profile.MetaId, AsOf,
            new Dictionary<Guid, decimal> { [item] = 0m },
            new Dictionary<Guid, decimal> { [item] = 10m });

        var row = await SuggestionAsync(profile.MetaId, item);
        Assert.IsTrue(Dec(row, "SuggestQty") == 0m, "не заказываем лишнее; факт {0}", row["SuggestQty"]);
    }

    [IntegrationTest("Профиль покупателя пополнение не считает")]
    public async Task CustomerProfileRefuses()
    {
        var profile = await ItemProfileAsync();
        profile.Subject = "Customer";
        profile = await Dictionaries.SaveRecordAsync(profile);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Policy.BuildWithDemandAsync(profile.MetaId, AsOf,
                new Dictionary<Guid, decimal>(),
                new Dictionary<Guid, decimal>()));
    }

    [IntegrationTest("Ночной проход пишет предложение товарам и пропускает покупателя")]
    public async Task EnabledItemProfilesAreBuiltTogether()
    {
        var itemProfile = await ItemProfileAsync();
        var subject = Guid.NewGuid();
        await ClassAsync(itemProfile.MetaId, subject, "A", "X");
        await CellAsync(itemProfile.MetaId, "A", "X", AbcStockMode.Keep, 14);

        var buyer = await ItemProfileAsync();
        buyer.Subject = "Customer";
        buyer = await Dictionaries.SaveRecordAsync(buyer);

        var off = await ItemProfileAsync();
        off.IsDisabled = true;
        off = await Dictionaries.SaveRecordAsync(off);
        await ClassAsync(off.MetaId, Guid.NewGuid(), "A", "X");
        await CellAsync(off.MetaId, "A", "X", AbcStockMode.Keep, 14);

        var n = await Policy.BuildEnabledItemProfilesAsync(AsOf);
        Assert.IsTrue(n >= 1, "живой товарный профиль пишет строку, факт {0}", n);

        var row = await SuggestionAsync(itemProfile.MetaId, subject);
        Assert.IsTrue(Dec(row, "SuggestQty") == 0m,
            "без счетов к заказу 0, факт {0}", row["SuggestQty"]);

        var buyerSlice = await Info.SliceLastAsync("AbcSuggestion", AsOf,
            new Dictionary<string, object?> { ["Profile"] = buyer.MetaId });
        Assert.AreEqual(0, buyerSlice.Count);

        var offSlice = await Info.SliceLastAsync("AbcSuggestion", AsOf,
            new Dictionary<string, object?> { ["Profile"] = off.MetaId });
        Assert.AreEqual(0, offSlice.Count);
    }

    [IntegrationTest("Без счетов расход ноль и к заказу ноль")]
    public async Task LiveBuildWithNoInvoicesSuggestsZero()
    {
        var profile = await ItemProfileAsync();
        var item = Guid.NewGuid();
        await ClassAsync(profile.MetaId, item, "A", "X");
        await CellAsync(profile.MetaId, "A", "X", AbcStockMode.Keep, 14);

        var n = await Policy.BuildAsync(profile.MetaId, AsOf);
        Assert.AreEqual(1, n);
        var row = await SuggestionAsync(profile.MetaId, item);
        Assert.IsTrue(Dec(row, "IssuedQty") == 0m, "счетов нет; факт {0}", row["IssuedQty"]);
        Assert.IsTrue(Dec(row, "SuggestQty") == 0m, "к заказу 0; факт {0}", row["SuggestQty"]);
    }

    private async Task<AbcProfile> ItemProfileAsync()
    {
        var profile = Dictionaries.NewRecord<AbcProfile>();
        profile.Name = $"POL {Guid.NewGuid():N}"[..20];
        profile.Subject = "Item";
        profile.Measure = "Revenue";
        profile.WindowMonths = 12;
        profile.BucketCount = 12;
        profile.AbcMethod = "CumulativeShare";
        profile.XyzMethod = "None";
        return await Dictionaries.SaveRecordAsync(profile);
    }

    private async Task ClassAsync(Guid profileId, Guid subject, string abc, string xyz)
    {
        await Info.SetAsync("AbcClassification", AsOf,
            new Dictionary<string, object?> { ["Profile"] = profileId, ["Subject"] = subject },
            new Dictionary<string, object?>
            {
                ["AbcClass"] = abc,
                ["XyzClass"] = xyz,
                ["Score"] = 1m,
                ["Share"] = 100m,
                ["Cv"] = 0m,
                ["ManualAbc"] = false,
                ["ManualXyz"] = false,
            });
    }

    private async Task<AbcPolicyCell> CellAsync(Guid profileId, string abc, string xyz, AbcStockMode mode, int coverDays)
    {
        var cell = Dictionaries.NewRecord<AbcPolicyCell>();
        cell.Profile = profileId;
        cell.AbcClass = abc;
        cell.XyzClass = xyz;
        cell.Mode = mode;
        cell.CoverDays = coverDays;
        return await Dictionaries.SaveRecordAsync(cell);
    }

    private async Task<Dictionary<string, object?>> SuggestionAsync(Guid profileId, Guid subject)
    {
        var slice = await Info.SliceLastAsync("AbcSuggestion", AsOf,
            new Dictionary<string, object?> { ["Profile"] = profileId, ["Subject"] = subject });
        Assert.IsTrue(slice.Count == 1, "нет предложения; факт {0}", slice.Count);
        return slice[0];
    }

    private static decimal Dec(IDictionary<string, object?> row, string column)
        => Convert.ToDecimal(row[column], CultureInfo.InvariantCulture);
}
