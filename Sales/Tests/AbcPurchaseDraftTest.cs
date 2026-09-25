using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Черновик заказа из предложения ABC. Поставщик, ячейка, единица и цена
// копируются с последней строки прихода в подтипе Принят. Ожидаемый приход
// остаётся пустым. Повтор не создаёт второй черновик той же группы.
public class AbcPurchaseDraftTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dictionaries => GetService<IDictionaryManager>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();
    private static IAbcPolicy Policy => GetService<IAbcPolicy>();
    private static IInformationRegisterService Info => GetService<IInformationRegisterService>();
    private static IDataService Data => GetService<IDataService>();

    private static readonly DateTime AsOf = new DateTime(2026, 9, 1);

    [IntegrationTest("Черновик заказа берёт поставщика и цену с принятого прихода и не дублируется")]
    public async Task DraftCopiesLastReceiptAndSkipsASecondClick()
    {
        var profile = await ProfileAsync();
        var cell = await CellAsync();
        var piece = await UnitAsync();
        var bolt = await ItemAsync(piece, "Bolt");
        var nut = await ItemAsync(piece, "Nut");
        var idle = await ItemAsync(piece, "Idle");
        var cold = await ItemAsync(piece, "Cold");
        var mill = await SupplierAsync("Mill");
        var forge = await SupplierAsync("Forge");

        await ReceiveAsync(mill, cell, bolt, piece, 10m, 12.5m, new DateTime(2026, 8, 1));
        await ReceiveAsync(mill, cell, bolt, piece, 3m, 99m, new DateTime(2026, 7, 1));
        await ReceiveAsync(forge, cell, nut, piece, 8m, 4m, new DateTime(2026, 8, 2));
        await SuggestAsync(profile, bolt, 4m);
        await SuggestAsync(profile, nut, 2m);
        await SuggestAsync(profile, idle, 0m);
        await SuggestAsync(profile, cold, 6m);
        var parked = await Documents.NewDocumentAsync<PurchaseOrder>();
        parked.Supplier = mill;
        parked.Location = cell;
        parked.DocumentDate = new DateTime(2026, 8, 20);
        parked.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = cold, Quantity = 6m, UnitPrice = 1m });
        await Documents.SaveDocumentAsync(parked);

        var created = await Policy.CreatePurchaseDraftsAsync(profile, AsOf);
        Assert.IsTrue(created.Count == 2, "два поставщика — два черновика; факт {0}", created.Count);

        var millDraft = await DraftForAsync(mill, profile);
        Assert.IsTrue(millDraft.Location == cell, "ячейка приёмки с прихода");
        Assert.IsTrue(millDraft.ExpectedReceipt.Year < 1902, "ожидаемый приход пустой, черновик не обещает остаток");
        Assert.IsTrue(millDraft.Lines.Count == 1, "нулевое и непринятое в заказ не попали; факт {0}", millDraft.Lines.Count);
        var line = millDraft.Lines[0];
        Assert.IsTrue(line.Item == bolt, "в черновике болт с принятого прихода");
        Assert.IsTrue(line.Quantity == 4m, "количество к заказу 4; факт {0}", line.Quantity);
        Assert.IsTrue(line.UnitPrice == 12.5m, "цена последнего прихода 12.5, не 99; факт {0}", line.UnitPrice);
        Assert.IsTrue(line.Unit == piece, "единица с прихода");

        var stamped = await Data.GetByIdAsync("PurchaseOrder", millDraft.MetaId);
        Assert.IsTrue(stamped != null && stamped["AbcProfile"] is Guid p && p == profile, "профиль записан, строки на месте");

        var again = await Policy.CreatePurchaseDraftsAsync(profile, AsOf);
        Assert.IsTrue(again.Count == 0, "повтор не создаёт второй черновик; факт {0}", again.Count);
        var still = await DraftForAsync(mill, profile);
        Assert.IsTrue(still.Lines.Count == 1 && still.Lines[0].Quantity == 4m, "строка пережила повтор");
    }

    private async Task<Guid> ProfileAsync()
    {
        var profile = Dictionaries.NewRecord<AbcProfile>();
        profile.Name = $"PO {Db.NewId():N}"[..16];
        profile.Subject = "Item";
        profile.Measure = "Revenue";
        profile.WindowMonths = 12;
        profile.BucketCount = 12;
        profile.AbcMethod = "CumulativeShare";
        profile.XyzMethod = "None";
        return (await Dictionaries.SaveRecordAsync(profile)).MetaId;
    }

    private async Task<Guid> CellAsync()
    {
        var currency = Dictionaries.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = $"E{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "€";
        currency = await Dictionaries.SaveRecordAsync(currency);

        var country = Dictionaries.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await Dictionaries.SaveRecordAsync(country);

        var legal = Dictionaries.NewRecord<LegalEntity>();
        legal.Name = "ACME GmbH";
        legal.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        legal.Country = country.MetaId;
        legal.Currency = currency.MetaId;
        legal = await Dictionaries.SaveRecordAsync(legal);

        var divisionType = Dictionaries.NewRecord<DivisionType>();
        divisionType.Code = $"W{Db.NewId():N}"[..4];
        divisionType.Name = "Warehouse";
        divisionType = await Dictionaries.SaveRecordAsync(divisionType);

        var division = Dictionaries.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legal.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await Dictionaries.SaveRecordAsync(division);

        var store = Dictionaries.NewRecord<Store>();
        store.Name = "Central";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await Dictionaries.SaveRecordAsync(store);

        var zone = Dictionaries.NewRecord<StoreZone>();
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await Dictionaries.SaveRecordAsync(zone);

        var cellType = Dictionaries.NewRecord<StoreCellType>();
        cellType.Code = $"R{Db.NewId():N}"[..8];
        cellType.Name = "Receiving";
        cellType = await Dictionaries.SaveRecordAsync(cellType);

        var cell = Dictionaries.NewRecord<StoreCell>();
        cell.Name = "R-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        return (await Dictionaries.SaveRecordAsync(cell)).MetaId;
    }

    private async Task<Guid> UnitAsync()
    {
        var uom = Dictionaries.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"P{Db.NewId():N}"[..6];
        return (await Dictionaries.SaveRecordAsync(uom)).MetaId;
    }

    private async Task<Guid> ItemAsync(Guid unit, string name)
    {
        var group = Dictionaries.NewRecord<ItemGroup>();
        group.Code = $"G{Db.NewId():N}"[..8];
        group.Name = name;
        group = await Dictionaries.SaveRecordAsync(group);

        var item = Dictionaries.NewRecord<Item>();
        item.Name = name;
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit;
        item.IsRawMaterial = true;
        return (await Dictionaries.SaveRecordAsync(item)).MetaId;
    }

    private async Task<Guid> SupplierAsync(string name)
    {
        var supplier = Dictionaries.NewRecord<Supplier>();
        supplier.Name = name;
        return (await Dictionaries.SaveRecordAsync(supplier)).MetaId;
    }

    private async Task ReceiveAsync(Guid supplier, Guid cell, Guid item, Guid unit, decimal qty, decimal price, DateTime date)
    {
        var order = await Documents.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = supplier;
        order.Location = cell;
        order.DocumentDate = date;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow
        {
            Item = item,
            Quantity = qty,
            Unit = unit,
            UnitPrice = price
        });
        await Documents.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await Documents.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await Documents.SaveDocumentAsync(order);
    }

    private async Task SuggestAsync(Guid profile, Guid item, decimal qty)
    {
        await Info.SetAsync("AbcSuggestion", AsOf,
            new Dictionary<string, object?> { ["Profile"] = profile, ["Subject"] = item },
            new Dictionary<string, object?>
            {
                ["AbcClass"] = "A",
                ["XyzClass"] = "X",
                ["Mode"] = "Keep",
                ["CoverDays"] = 14,
                ["IssuedQty"] = qty,
                ["OnHandQty"] = 0m,
                ["SuggestQty"] = qty
            });
    }

    private async Task<PurchaseOrder> DraftForAsync(Guid supplier, Guid profile)
    {
        var rows = await Documents.QueryDocumentsAsync<PurchaseOrder>(
            $"Supplier = '{supplier}' AND Subtype = 'Draft'");
        PurchaseOrder? ours = null;
        var count = 0;
        foreach (var row in rows)
        {
            var bag = await Data.GetByIdAsync("PurchaseOrder", row.MetaId);
            if (bag == null || bag["AbcProfile"] is not Guid stamped || stamped != profile) continue;
            count++;
            ours = await Documents.GetDocumentAsync<PurchaseOrder>(row.MetaId);
        }
        Assert.IsTrue(count == 1 && ours != null, "один черновик профиля; факт {0}", count);
        return ours!;
    }
}
