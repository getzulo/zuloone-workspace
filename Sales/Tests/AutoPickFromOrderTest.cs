using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Автоотбор: задание появляется от ПОДТВЕРЖДЁННОГО заказа, не от счёта.
// Счёт уже списывает — порождать отбор из него некому. Зеркало раскладки
// у прихода: черновик, идемпотентно по графу, только при включённой дисциплине.
public class AutoPickFromOrderTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();
    private static ISalesFulfillmentService Fulfillment => GetService<ISalesFulfillmentService>();
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    private sealed class Yard
    {
        public Guid Store;
        public Guid Zone;
        public Guid Receiving;
        public Guid Storage;
        public Guid Picking;
        public Guid Item;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
    }

    private async Task<Yard> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = "EUR";
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = $"REG-AP-{Db.NewId():N}"[..18];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"AP-{Db.NewId():N}"[..12];
        divisionType.Name = "Warehouse";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Central";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PCS-{Db.NewId():N}"[..12];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"GOODS-{Db.NewId():N}"[..12];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item.IsSellable = true;
        item.Image = TinyPng;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop A";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "A-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        return new Yard
        {
            Store = store.MetaId,
            Zone = zone.MetaId,
            Receiving = await NewCellAsync(zone.MetaId, StoreCellPurpose.Receiving, "R-01", 1),
            Storage = await NewCellAsync(zone.MetaId, StoreCellPurpose.Storage, "S-01", 2),
            Picking = await NewCellAsync(zone.MetaId, StoreCellPurpose.Picking, "P-01", 3),
            Item = item.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
        };
    }

    private async Task<Guid> NewCellAsync(Guid zone, StoreCellPurpose purpose, string name, int number)
    {
        var type = DictionaryManager.NewRecord<StoreCellType>();
        type.Code = $"{purpose}-{Db.NewId():N}"[..12];
        type.Name = purpose.ToString();
        type.Purpose = purpose;
        type = await DictionaryManager.SaveRecordAsync(type);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = name;
        cell.Type = type.MetaId;
        cell.StoreZone = zone;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = number;
        return (await DictionaryManager.SaveRecordAsync(cell)).MetaId;
    }

    private static async Task SetDisciplineAsync(bool on)
    {
        var manager = GetService<IDictionaryManager<InventorySettings>>();
        var rows = await manager.GetRecordsAsync("1 = 1");
        var settings = rows.Count > 0 ? rows[0] : await manager.NewRecordAsync();
        settings.EnforceWarehouseTasks = on;
        await manager.SaveRecordAsync(settings);
    }

    private static Task SeedAsync(Guid cell, Guid item, decimal qty)
        => TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item },
            new Dictionary<string, decimal> { ["Qty"] = qty });

    private static async Task<SalesOrder> NewOrderAsync(Yard y, Guid location, decimal qty)
    {
        var order = await DocumentManager.NewDocumentAsync<SalesOrder>();
        order.Customer = y.Customer;
        order.Outlet = y.Outlet;
        order.Contract = y.Contract;
        order.Location = location;
        order.DeliveryDate = DateTime.UtcNow.Date.AddDays(1);
        order.Lines.Add(new SalesOrderLinesTablePartRow { Item = y.Item, Quantity = qty, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    private async Task RunCommandAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        Assert.IsTrue(run.Success, "команда {0}: {1}", name, run.Message ?? string.Join("; ", run.ClientMessages));
    }

    private async Task SubmitAndApproveAsync(Guid orderId)
    {
        await RunCommandAsync("SubmitSalesOrder", orderId);
        await RunCommandAsync("ApproveSalesOrder", orderId);
    }

    private static async Task<List<Guid>> PickIdsAsync(Guid orderId)
    {
        var family = await DocumentManager.GetDocumentFamilyAsync(orderId);
        return family.Nodes.Where(n => n.DocTypeName == "PickTask").Select(n => n.DocId).ToList();
    }

    private static async Task<List<StockTransfer>> TransfersAsync(Guid orderId)
    {
        var family = await DocumentManager.GetDocumentFamilyAsync(orderId);
        var docs = new List<StockTransfer>();
        foreach (var node in family.Nodes.Where(n => n.DocTypeName == "StockTransfer"))
        {
            var doc = await DocumentManager.GetDocumentAsync<StockTransfer>(node.DocId);
            Assert.IsNotNull(doc, "перемещение читается");
            docs.Add(doc!);
        }
        return docs;
    }

    private static async Task<StockTransfer> SingleTransferAsync(Guid orderId)
    {
        var docs = await TransfersAsync(orderId);
        Assert.IsTrue(docs.Count == 1, "перемещение ровно одно, факт {0}", docs.Count);
        return docs[0];
    }

    private static async Task AssertParentIsOrderAsync(Guid orderId, Guid transferId)
    {
        var family = await DocumentManager.GetDocumentFamilyAsync(transferId);
        Assert.IsTrue(family.Edges.Any(e => e.ParentDocId == orderId && e.ChildDocId == transferId),
            "родитель перемещения — заказ, который его породил");

        var moves = await TotalsManager.QueryMovementsAsync("Stock", $"[DocumentMetaId] = '{transferId}'");
        Assert.IsTrue(moves.Count == 2, "у проведённого перемещения пара движений, факт {0}", moves.Count);
        foreach (var move in moves)
        {
            var owner = move["DocumentMetaId"]?.ToString();
            Assert.IsTrue(string.Equals(owner, transferId.ToString(), StringComparison.OrdinalIgnoreCase),
                "движение принадлежит перемещению");
        }
    }

    private static Task<decimal> OnHandAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    private static async Task SetSchemeAsync(WarehouseFulfillmentScheme scheme)
    {
        var manager = GetService<IDictionaryManager<InventorySettings>>();
        var rows = await manager.GetRecordsAsync("1 = 1");
        var settings = rows.Count > 0 ? rows[0] : await manager.NewRecordAsync();
        settings.FulfillmentScheme = scheme;
        await manager.SaveRecordAsync(settings);
    }

    private static async Task SetBackorderAsync(bool on)
    {
        var manager = GetService<IDictionaryManager<SalesSettings>>();
        var rows = await manager.GetRecordsAsync("1 = 1");
        var settings = rows.Count > 0 ? rows[0] : await manager.NewRecordAsync();
        settings.AllowBackorder = on;
        await manager.SaveRecordAsync(settings);
    }

    [IntegrationTest("Дисциплина выключена: подтверждение заданий не плодит")]
    public async Task DisciplineOffSpawnsNoPick()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(false);
        await SeedAsync(y.Picking, y.Item, 8m);

        var order = await NewOrderAsync(y, y.Picking, 3m);
        await SubmitAndApproveAsync(order.MetaId);

        var family = await DocumentManager.GetDocumentFamilyAsync(order.MetaId);
        Assert.IsTrue(!family.Nodes.Any(n => n.DocTypeName == "StockTransfer"),
            "без дисциплины перемещение не порождается");
    }

    [IntegrationTest("Подтверждение переносит нужное количество на ячейку списания")]
    public async Task ConfirmPostsTransferOntoTheWriteOffCell()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SetSchemeAsync(WarehouseFulfillmentScheme.Simple);
        await SeedAsync(y.Storage, y.Item, 10m);

        var order = await NewOrderAsync(y, y.Picking, 4m);
        await SubmitAndApproveAsync(order.MetaId);

        var transfer = await SingleTransferAsync(order.MetaId);
        Assert.IsTrue(transfer.Subtype == StockTransfer.Subtypes.Posted, "перемещение проведено");
        Assert.IsTrue(transfer.FromCell == y.Storage && transfer.ToCell == y.Picking,
            "из хранения на ячейку списания заказа");
        Assert.IsTrue(transfer.Lines.Count == 1 && transfer.Lines[0].Quantity == 4m,
            "переносится 4, сколько в строке заказа");
        Assert.IsTrue(await OnHandAsync(y.Storage, y.Item) == 6m, "в хранении осталось 6");
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 4m, "на ячейке списания 4");
        await AssertParentIsOrderAsync(order.MetaId, transfer.MetaId);
    }

    [IntegrationTest("20 из ячеек 10, 5 и 7 собираются на ячейку списания")]
    public async Task ConfirmGathersThreeCellsOntoTheWriteOffCell()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SetSchemeAsync(WarehouseFulfillmentScheme.Simple);
        var second = await NewCellAsync(y.Zone, StoreCellPurpose.Storage, "S-02", 4);
        var third = await NewCellAsync(y.Zone, StoreCellPurpose.Storage, "S-03", 5);
        await SeedAsync(y.Storage, y.Item, 10m);
        await SeedAsync(second, y.Item, 5m);
        await SeedAsync(third, y.Item, 7m);

        var order = await NewOrderAsync(y, y.Picking, 20m);
        await SubmitAndApproveAsync(order.MetaId);

        var transfers = await TransfersAsync(order.MetaId);
        Assert.IsTrue(transfers.Count == 3, "три ячейки-источника — три перемещения, факт {0}", transfers.Count);

        decimal moved = 0m;
        foreach (var transfer in transfers)
        {
            Assert.IsTrue(transfer.Subtype == StockTransfer.Subtypes.Posted, "каждое перемещение проведено");
            Assert.IsTrue(transfer.ToCell == y.Picking, "все садятся на ячейку списания");
            Assert.IsTrue(transfer.Lines.Count == 1, "одна строка на документ");
            moved += transfer.Lines[0].Quantity;
            await AssertParentIsOrderAsync(order.MetaId, transfer.MetaId);
        }

        Assert.IsTrue(moved == 20m, "перенесено ровно 20, факт {0}", moved);
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 20m, "на ячейке списания 20");
        var left = await OnHandAsync(y.Storage, y.Item)
                 + await OnHandAsync(second, y.Item)
                 + await OnHandAsync(third, y.Item);
        Assert.IsTrue(left == 2m, "в хранении осталось 2, факт {0}", left);

        var family = await DocumentManager.GetDocumentFamilyAsync(order.MetaId);
        Assert.IsTrue(!family.Nodes.Any(n => n.DocTypeName == "PickTask"),
            "сбор идёт перемещением, не заданием отбора");
    }

    [IntegrationTest("Если по хранению не хватает, перемещение не создаётся")]
    public async Task ShortStorageCreatesNoTransfer()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SetSchemeAsync(WarehouseFulfillmentScheme.Simple);
        await SetBackorderAsync(true);
        var other = await NewCellAsync(y.Zone, StoreCellPurpose.Storage, "S-02", 4);
        await SeedAsync(y.Storage, y.Item, 3m);
        await SeedAsync(other, y.Item, 2m);

        var order = await NewOrderAsync(y, y.Picking, 8m);
        await SubmitAndApproveAsync(order.MetaId);

        var family = await DocumentManager.GetDocumentFamilyAsync(order.MetaId);
        Assert.IsTrue(!family.Nodes.Any(n => n.DocTypeName == "StockTransfer"),
            "5 по хранению на заказ 8 — перемещения нет");
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 0m, "ячейка списания пустая");
    }

    [IntegrationTest("Повтор подтверждения не переносит остаток второй раз")]
    public async Task RepostDoesNotDuplicateTransfer()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SetSchemeAsync(WarehouseFulfillmentScheme.Simple);
        await SeedAsync(y.Storage, y.Item, 6m);

        var order = await NewOrderAsync(y, y.Picking, 2m);
        await SubmitAndApproveAsync(order.MetaId);
        var first = await SingleTransferAsync(order.MetaId);

        var again = await Fulfillment.PrepareShipmentAsync(order.MetaId);
        Assert.IsTrue(again == first.MetaId,
            "повтор вернул то же перемещение: было {0}, стало {1}", first.MetaId, again);
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 2m, "второй перенос не удвоил ячейку списания");
    }

    [IntegrationTest("Расширенная схема оставляет черновик отбора и не двигает остаток")]
    public async Task ExtendedSchemeLeavesADraftPick()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SetSchemeAsync(WarehouseFulfillmentScheme.Extended);
        await SeedAsync(y.Storage, y.Item, 10m);

        var order = await NewOrderAsync(y, y.Picking, 4m);
        await SubmitAndApproveAsync(order.MetaId);

        var family = await DocumentManager.GetDocumentFamilyAsync(order.MetaId);
        Assert.IsTrue(!family.Nodes.Any(n => n.DocTypeName == "StockTransfer"),
            "расширенная схема перемещение не проводит");
        var picks = family.Nodes.Where(n => n.DocTypeName == "PickTask").ToList();
        Assert.IsTrue(picks.Count == 1, "черновик отбора один, факт {0}", picks.Count);
        var pick = await DocumentManager.GetDocumentAsync<PickTask>(picks[0].DocId);
        Assert.IsTrue(pick!.Subtype == PickTask.Subtypes.Draft, "кладовщик подтверждает сам");
        Assert.IsTrue(pick.FromCell == y.Storage && pick.Lines[0].Quantity == 4m && pick.Lines[0].ToCell == y.Picking,
            "отбор из хранения на ячейку заказа, всё количество строки");
        Assert.IsTrue(await OnHandAsync(y.Storage, y.Item) == 10m, "остаток ещё в хранении");
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 0m, "ячейка списания пустая, пока отбор не подтверждён");
    }

    [IntegrationTest("20 из ячеек 10, 5 и 7 — три черновика отбора, остаток на месте")]
    public async Task ExtendedSchemeSplitsDraftsAcrossStorageCells()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SetSchemeAsync(WarehouseFulfillmentScheme.Extended);
        var second = await NewCellAsync(y.Zone, StoreCellPurpose.Storage, "S-02", 4);
        var third = await NewCellAsync(y.Zone, StoreCellPurpose.Storage, "S-03", 5);
        await SeedAsync(y.Storage, y.Item, 10m);
        await SeedAsync(second, y.Item, 5m);
        await SeedAsync(third, y.Item, 7m);

        var order = await NewOrderAsync(y, y.Picking, 20m);
        await SubmitAndApproveAsync(order.MetaId);

        var family = await DocumentManager.GetDocumentFamilyAsync(order.MetaId);
        Assert.IsTrue(!family.Nodes.Any(n => n.DocTypeName == "StockTransfer"),
            "расширенная схема перемещение не проводит");
        var picks = new List<PickTask>();
        foreach (var id in await PickIdsAsync(order.MetaId))
        {
            var pick = await DocumentManager.GetDocumentAsync<PickTask>(id);
            Assert.IsNotNull(pick, "черновик читается");
            picks.Add(pick!);
        }
        Assert.IsTrue(picks.Count == 3, "три ячейки-источника — три задания, факт {0}", picks.Count);
        decimal qty = 0m;
        foreach (var pick in picks)
        {
            Assert.IsTrue(pick.Subtype == PickTask.Subtypes.Draft, "кладовщик подтверждает сам");
            Assert.IsTrue(pick.Lines.Count == 1 && pick.Lines[0].ToCell == y.Picking,
                "строка садится на ячейку заказа");
            qty += pick.Lines[0].Quantity;
        }
        Assert.IsTrue(qty == 20m, "в заданиях ровно 20, факт {0}", qty);
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 0m, "ячейка списания пустая");
        var left = await OnHandAsync(y.Storage, y.Item)
                 + await OnHandAsync(second, y.Item)
                 + await OnHandAsync(third, y.Item);
        Assert.IsTrue(left == 22m, "хранение не тронуто, факт {0}", left);
    }

    [IntegrationTest("Расширенная схема: по хранению не хватает — черновика нет")]
    public async Task ExtendedShortStorageCreatesNoPick()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SetSchemeAsync(WarehouseFulfillmentScheme.Extended);
        await SetBackorderAsync(true);
        var other = await NewCellAsync(y.Zone, StoreCellPurpose.Storage, "S-02", 4);
        await SeedAsync(y.Storage, y.Item, 3m);
        await SeedAsync(other, y.Item, 2m);

        var order = await NewOrderAsync(y, y.Picking, 8m);
        await SubmitAndApproveAsync(order.MetaId);

        var picks = await PickIdsAsync(order.MetaId);
        Assert.IsTrue(picks.Count == 0, "5 по хранению на заказ 8 — задания нет, факт {0}", picks.Count);
        Assert.IsTrue(await OnHandAsync(y.Picking, y.Item) == 0m, "ячейка списания пустая");
    }

    [IntegrationTest("Два черновика с одной ячейки хранения становятся одним заданием")]
    public async Task PickWaveFoldsDraftsFromTheSameCell()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SetSchemeAsync(WarehouseFulfillmentScheme.Extended);
        await SeedAsync(y.Storage, y.Item, 20m);

        var first = await NewOrderAsync(y, y.Picking, 4m);
        var second = await NewOrderAsync(y, y.Picking, 6m);
        await SubmitAndApproveAsync(first.MetaId);
        await SubmitAndApproveAsync(second.MetaId);

        var commandId = await Db.FindCommandIdAsync("user", "PlanPickWave");
        var run = await Db.ExecuteUserCommandAsync(commandId);
        Assert.IsTrue(run.Success, "волна отбора: {0}", run.Message ?? "");

        var left = await PickIdsAsync(first.MetaId);
        var right = await PickIdsAsync(second.MetaId);
        Assert.IsTrue(left.Count == 1 && right.Count == 1 && left[0] == right[0],
            "оба заказа смотрят на одно задание");
        var pick = await DocumentManager.GetDocumentAsync<PickTask>(left[0]);
        Assert.IsTrue(pick!.Subtype == PickTask.Subtypes.Draft, "волна остаётся черновиком");
        Assert.IsTrue(pick.FromCell == y.Storage, "ячейка хранения та же");
        decimal qty = 0m;
        foreach (var line in pick.Lines) qty += line.Quantity;
        Assert.IsTrue(qty == 10m, "строки обоих заказов, факт {0}", qty);
        Assert.IsTrue(await OnHandAsync(y.Storage, y.Item) == 20m, "волна остаток не двигает");

        var again = await Fulfillment.PlanPickWaveAsync();
        Assert.IsTrue(again == 0, "повтор не снимает единственный черновик, факт {0}", again);
        var still = await PickIdsAsync(first.MetaId);
        Assert.IsTrue(still.Count == 1 && still[0] == left[0], "задание то же");
    }

    [IntegrationTest("Свободный остаток при дисциплине — по складу: второй заказ сверх хранения отклонён")]
    public async Task ConfirmBeyondStoreFreeIsRejected()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SeedAsync(y.Storage, y.Item, 5m);

        var first = await NewOrderAsync(y, y.Picking, 4m);
        await SubmitAndApproveAsync(first.MetaId);

        var second = await NewOrderAsync(y, y.Picking, 3m);
        await RunCommandAsync("SubmitSalesOrder", second.MetaId);
        var approveId = await Db.FindCommandIdAsync("document", "ApproveSalesOrder");
        var run = await Db.ExecuteDocumentCommandAsync(approveId, second.MetaId);
        var after = await DocumentManager.GetDocumentAsync<SalesOrder>(second.MetaId);

        Assert.IsTrue(after!.Subtype == SalesOrder.Subtypes.Submitted,
            "второй остаётся Submitted, факт {0}", after.Subtype ?? "<null>");
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("остатка") || !run.Success,
            "пользователь видит отказ: {0}", string.Join("; ", run.ClientMessages));
    }

    [IntegrationTest("При дисциплине заказ из ячейки хранения не подтверждается")]
    public async Task ConfirmFromStorageCellIsRejected()
    {
        var y = await SetupAsync();
        await SetDisciplineAsync(true);
        await SeedAsync(y.Storage, y.Item, 8m);

        var order = await NewOrderAsync(y, y.Storage, 2m);
        await RunCommandAsync("SubmitSalesOrder", order.MetaId);
        var approveId = await Db.FindCommandIdAsync("document", "ApproveSalesOrder");
        var run = await Db.ExecuteDocumentCommandAsync(approveId, order.MetaId);
        var after = await DocumentManager.GetDocumentAsync<SalesOrder>(order.MetaId);

        Assert.IsTrue(after!.Subtype == SalesOrder.Subtypes.Submitted,
            "заказ из хранения остаётся Submitted, факт {0}", after.Subtype ?? "<null>");
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("ОТБОРА") || !run.Success,
            "отказ про ячейку отбора: {0}", string.Join("; ", run.ClientMessages));
    }
}
