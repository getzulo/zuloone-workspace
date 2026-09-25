using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Чужой резерв на ячейке уменьшает то, что можно увезти расходом,
// перемещением или списанием. Свой черновик отбора в этот чужой резерв
// не входит: подтверждение забирает ровно его.
public class WarehouseReserveGuardTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private static Task<decimal> OnHandAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    private static Task<decimal> ReservedAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("ReservedStock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    [IntegrationTest("Чужой резерв отклоняет расход сверх свободного и пропускает остаток")]
    public async Task ForeignHoldLimitsGoodsIssue()
    {
        var loc = Db.NewId();
        var item = await NewItemAsync("Товар-расход");
        await SeedAsync(loc, item.MetaId, 10m);
        await HoldAsync(loc, item.MetaId, 8m);

        var blocked = await DocumentManager.NewDocumentAsync<GoodsIssue>();
        blocked.FromCell = loc;
        blocked.Lines.Add(new GoodsIssueLinesTablePartRow { Item = item.MetaId, Quantity = 4m });
        await DocumentManager.SaveDocumentAsync(blocked);
        var detail = await RejectAsync(async () =>
        {
            blocked.Subtype = GoodsIssue.Subtypes.Posted;
            await DocumentManager.SaveDocumentAsync(blocked);
        });
        Assert.IsTrue(detail.Contains("требуется 4, свободно 2"),
            "нужно 4, свободно 2, факт {0}", detail);

        var ok = await DocumentManager.NewDocumentAsync<GoodsIssue>();
        ok.FromCell = loc;
        ok.Lines.Add(new GoodsIssueLinesTablePartRow { Item = item.MetaId, Quantity = 2m });
        await DocumentManager.SaveDocumentAsync(ok);
        ok.Subtype = GoodsIssue.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(ok);
        Assert.IsTrue(await OnHandAsync(loc, item.MetaId) == 8m, "расход 2 оставляет 8");
        Assert.IsTrue(await ReservedAsync(loc, item.MetaId) == 8m,
            "чужой резерв на месте, факт {0}", await ReservedAsync(loc, item.MetaId));
    }

    [IntegrationTest("Чужой резерв отклоняет перемещение сверх свободного")]
    public async Task ForeignHoldLimitsTransfer()
    {
        var from = Db.NewId();
        var to = Db.NewId();
        var item = await NewItemAsync("Товар-перенос");
        await SeedAsync(from, item.MetaId, 10m);
        await HoldAsync(from, item.MetaId, 8m);

        var blocked = await DocumentManager.NewDocumentAsync<StockTransfer>();
        blocked.FromCell = from;
        blocked.ToCell = to;
        blocked.Lines.Add(new StockTransferLinesTablePartRow { Item = item.MetaId, Quantity = 4m });
        await DocumentManager.SaveDocumentAsync(blocked);
        var detail = await RejectAsync(async () =>
        {
            blocked.Subtype = StockTransfer.Subtypes.Posted;
            await DocumentManager.SaveDocumentAsync(blocked);
        });
        Assert.IsTrue(detail.Contains("требуется 4, свободно 2"),
            "перемещение 4 при свободных 2, факт {0}", detail);

        var ok = await DocumentManager.NewDocumentAsync<StockTransfer>();
        ok.FromCell = from;
        ok.ToCell = to;
        ok.Lines.Add(new StockTransferLinesTablePartRow { Item = item.MetaId, Quantity = 2m });
        await DocumentManager.SaveDocumentAsync(ok);
        ok.Subtype = StockTransfer.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(ok);
        Assert.IsTrue(await OnHandAsync(from, item.MetaId) == 8m, "источник отдал 2");
        Assert.IsTrue(await OnHandAsync(to, item.MetaId) == 2m, "приёмник получил 2");
        Assert.IsTrue(await ReservedAsync(from, item.MetaId) == 8m,
            "резерв остался на источнике, факт {0}", await ReservedAsync(from, item.MetaId));
    }

    [IntegrationTest("Чужой резерв отклоняет списание сверх свободного")]
    public async Task ForeignHoldLimitsWriteOff()
    {
        var loc = Db.NewId();
        var item = await NewItemAsync("Товар-списание");
        await SeedAsync(loc, item.MetaId, 10m);
        await HoldAsync(loc, item.MetaId, 8m);

        var blocked = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        blocked.Cell = loc;
        blocked.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = item.MetaId, Quantity = -4m });
        await DocumentManager.SaveDocumentAsync(blocked);
        var detail = await RejectAsync(async () =>
        {
            blocked.Subtype = StockAdjustment.Subtypes.Posted;
            await DocumentManager.SaveDocumentAsync(blocked);
        });
        Assert.IsTrue(detail.Contains("требуется 4, свободно 2"),
            "списание 4 при свободных 2, факт {0}", detail);

        var ok = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        ok.Cell = loc;
        ok.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = item.MetaId, Quantity = -2m });
        await DocumentManager.SaveDocumentAsync(ok);
        ok.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(ok);
        Assert.IsTrue(await OnHandAsync(loc, item.MetaId) == 8m, "списание 2 оставляет 8");
        Assert.IsTrue(await ReservedAsync(loc, item.MetaId) == 8m,
            "чужой резерв на месте, факт {0}", await ReservedAsync(loc, item.MetaId));
    }

    [IntegrationTest("Свой черновик отбора не мешает подтверждению, чужой — мешает")]
    public async Task OwnPickHoldDoesNotBlockConfirm()
    {
        var was = await DisciplineAsync();
        try
        {
            await SetDisciplineAsync(false);
            var from = Db.NewId();
            var mineTo = Db.NewId();
            var item = await NewItemAsync("Товар-отбор");
            await SeedAsync(from, item.MetaId, 10m);
            await HoldAsync(from, item.MetaId, 6m);

            var mine = await DocumentManager.NewDocumentAsync<PickTask>();
            mine.FromCell = from;
            mine.Lines.Add(new PickTaskLinesTablePartRow { Item = item.MetaId, Quantity = 4m, ToCell = mineTo });
            await DocumentManager.SaveDocumentAsync(mine);
            Assert.IsTrue(await ReservedAsync(from, item.MetaId) == 10m, "свой 4 и чужой 6");

            mine.Subtype = PickTask.Subtypes.Confirmed;
            await DocumentManager.SaveDocumentAsync(mine);
            Assert.IsTrue(await OnHandAsync(from, item.MetaId) == 6m, "подтверждение забрало свои 4");
            Assert.IsTrue(await ReservedAsync(from, item.MetaId) == 6m,
                "чужой резерв на хранении цел, факт {0}", await ReservedAsync(from, item.MetaId));

            var other = await DocumentManager.NewDocumentAsync<PickTask>();
            other.FromCell = from;
            other.Lines.Add(new PickTaskLinesTablePartRow { Item = item.MetaId, Quantity = 4m, ToCell = Db.NewId() });
            await DocumentManager.SaveDocumentAsync(other);
            var detail = await RejectAsync(async () =>
            {
                other.Subtype = PickTask.Subtypes.Confirmed;
                await DocumentManager.SaveDocumentAsync(other);
            });
            Assert.IsTrue(detail.Contains("требуется 4, свободно 0"),
                "чужие 6 занимают весь остаток, факт {0}", detail);
        }
        finally
        {
            await SetDisciplineAsync(was);
        }
    }

    private async Task HoldAsync(Guid cell, Guid item, decimal qty)
    {
        var hold = await DocumentManager.NewDocumentAsync<PickTask>();
        hold.FromCell = cell;
        hold.Lines.Add(new PickTaskLinesTablePartRow { Item = item, Quantity = qty, ToCell = Db.NewId() });
        await DocumentManager.SaveDocumentAsync(hold);
    }

    private static Task SeedAsync(Guid cell, Guid item, decimal qty)
        => TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item },
            new Dictionary<string, decimal> { ["Qty"] = qty });

    private static async Task<string> RejectAsync(Func<Task> action)
    {
        try
        {
            await action();
            return "";
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }

    private static async Task<bool> DisciplineAsync()
    {
        var manager = GetService<IDictionaryManager<InventorySettings>>();
        var rows = await manager.GetRecordsAsync("1 = 1");
        return rows.Count > 0 && rows[0].EnforceWarehouseTasks;
    }

    private static async Task SetDisciplineAsync(bool on)
    {
        var manager = GetService<IDictionaryManager<InventorySettings>>();
        var rows = await manager.GetRecordsAsync("1 = 1");
        var settings = rows.Count > 0 ? rows[0] : await manager.NewRecordAsync();
        settings.EnforceWarehouseTasks = on;
        await manager.SaveRecordAsync(settings);
    }

    private async Task<Item> NewItemAsync(string name)
    {
        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"RG-{Db.NewId():N}"[..12];
        group.Name = "Резерв";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = name;
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        return await DictionaryManager.SaveRecordAsync(item);
    }
}
