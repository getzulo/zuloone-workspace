using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Скидка уровня лояльности на счёте продажи.
//
// Главное, что проверяется, — НЕ «скидка посчиталась», а что её увидели ВСЕ
// денежные ноги документа. Дебиторка, выручка и баллы обязаны сойтись от одной
// базы: разойдись они, клиент заплатил бы одну сумму, выручка признала бы
// другую, а баллы начислились бы от третьей, и поймать это в проде было бы уже
// нечем.
//
// Второй предмет — момент штамповки. Скидка ставится на СОХРАНЕНИИ, а не на
// проведении: проводки читают шапку, прочитанную из базы до начала проведения,
// и запись из события проведения до них не доезжает. Тест это и ловит —
// поставь штамповку в OnBeforePost, и суммы ниже разъедутся.
public class LoyaltyDiscountTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Customer;
    }

    private async Task<Setup> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = $"Euro-{Db.NewId():N}"[..12];
        currency.Code = $"E{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = $"Germany-{Db.NewId():N}"[..14];
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = $"ACME-{Db.NewId():N}"[..12];
        legalEntity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"SP{Db.NewId():N}"[..8];
        divisionType.Name = "SalesPoint";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Shop";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Shop WH";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"PICK-{Db.NewId():N}"[..12];
        cellType.Name = "Picking";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "P-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "Piece";
        unit.Code = $"P{Db.NewId():N}"[..8];
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Db.NewId():N}"[..12];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = cell.MetaId, ["Item"] = item.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 100m });

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Customer = customer.MetaId };
    }

    private static async Task TierAsync(string name, decimal minPoints, decimal discountPercent)
    {
        var tier = DictionaryManager.NewRecord<LoyaltyTier>();
        tier.Name = name;
        tier.MinPoints = minPoints;
        tier.MaxRedemptionPerDocument = 1000m;
        tier.DiscountPercent = discountPercent;
        await DictionaryManager.SaveRecordAsync(tier);
    }

    private static Task<decimal> PointsAsync(Guid customer)
        => TotalsManager.GetBalanceAsync("LoyaltyPoints", "Points",
            new Dictionary<string, object?> { ["Customer"] = customer });

    // Receivable и Revenue несут только динамическую аналитику — баланс каждого
    // схлопывается в строки с одним ресурсом Amount; суммируем его.
    private static async Task<decimal> SumAsync(string register)
    {
        decimal total = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync(register))
            total += Convert.ToDecimal(r["Amount"]);
        return total;
    }

    private async Task<SalesInvoice> IssueAsync(Setup s, decimal qty, decimal price, decimal? manualDiscount = null)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Location;
        if (manualDiscount.HasValue) invoice.DiscountPercent = manualDiscount.Value;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(invoice);

        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
        return invoice;
    }

    [IntegrationTest("Скидка уровня применяется ко всем денежным ногам от одной базы")]
    public async Task TierDiscountReachesEveryMoneyLeg()
    {
        var s = await SetupAsync();
        await TierAsync($"Bronze-{Db.NewId():N}"[..14], 0m, 0m);
        await TierAsync($"Silver-{Db.NewId():N}"[..14], 100m, 10m);

        // Баланс 500 достаёт до Silver: скидка 10%.
        await TotalsManager.PostMovementAsync("LoyaltyPoints", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Customer"] = s.Customer },
            new Dictionary<string, decimal> { ["Points"] = 500m });

        var invoice = await IssueAsync(s, qty: 10m, price: 100m);

        // Скидка обязана оказаться В БАЗЕ, а не только в памяти обработчика:
        // проводки читают шапку оттуда.
        var stored = await DocumentManager.GetDocumentAsync<SalesInvoice>(invoice.MetaId);
        Assert.IsTrue(stored.DiscountPercent == 10m,
            "уровень Silver даёт 10%, в документе {0}", stored.DiscountPercent);

        // 10 × 100 = 1000, минус 10% = 900. Один и тот же ответ у всех трёх.
        Assert.IsTrue(await SumAsync("Receivable") == 900m,
            "долг со скидкой 900, факт {0}", await SumAsync("Receivable"));
        Assert.IsTrue(await SumAsync("Revenue") == 900m,
            "выручка со скидкой 900, факт {0}", await SumAsync("Revenue"));
        // Баллы начислялись бы 1000 при незамеченной скидке — 500 стартовых плюс 900.
        Assert.IsTrue(await PointsAsync(s.Customer) == 1400m,
            "баллы 500 + 900 = 1400, факт {0}", await PointsAsync(s.Customer));
    }

    [IntegrationTest("Скидка, введённая вручную, уровнем не переписывается")]
    public async Task ManualDiscountWins()
    {
        var s = await SetupAsync();
        await TierAsync($"Silver-{Db.NewId():N}"[..14], 100m, 10m);
        await TotalsManager.PostMovementAsync("LoyaltyPoints", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Customer"] = s.Customer },
            new Dictionary<string, decimal> { ["Points"] = 500m });

        // Договорённость по сделке — 25%, и уровень её не отменяет.
        var invoice = await IssueAsync(s, qty: 10m, price: 100m, manualDiscount: 25m);

        var stored = await DocumentManager.GetDocumentAsync<SalesInvoice>(invoice.MetaId);
        Assert.IsTrue(stored.DiscountPercent == 25m,
            "ручные 25% сохраняются, факт {0}", stored.DiscountPercent);
        Assert.IsTrue(await SumAsync("Revenue") == 750m,
            "выручка 1000 − 25% = 750, факт {0}", await SumAsync("Revenue"));
    }

    [IntegrationTest("Без достигнутого уровня счёт выставляется по полной цене")]
    public async Task NoTierNoDiscount()
    {
        var s = await SetupAsync();
        await TierAsync($"Gold-{Db.NewId():N}"[..14], 1000m, 20m);

        // Баллов нет — до уровня клиент не достаёт, и это не ошибка.
        var invoice = await IssueAsync(s, qty: 10m, price: 100m);

        var stored = await DocumentManager.GetDocumentAsync<SalesInvoice>(invoice.MetaId);
        Assert.IsTrue(stored.DiscountPercent == 0m,
            "недостигнутый уровень скидки не даёт, факт {0}", stored.DiscountPercent);
        Assert.IsTrue(await SumAsync("Revenue") == 1000m,
            "выручка без скидки 1000, факт {0}", await SumAsync("Revenue"));
    }
}
