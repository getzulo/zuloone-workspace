using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Автозахват истории цен: CaptureSalePriceAsync/CapturePurchasePriceAsync
// (PricingService) строят настоящую историю PriceListItem из фактических цен
// проведённых документов. Тест вызывает сервис напрямую — так же, как
// PriceResolutionTest проверяет Resolve*PriceAsync, — без документа: проводка
// вызова из OnAfterPostAsync проверяется отдельно, в Purchasing/Sales.
public class PriceCaptureTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private static readonly DateTime EarlierDay = new DateTime(2026, 2, 1);
    private static readonly DateTime Day1 = new DateTime(2026, 3, 1);
    private static readonly DateTime Day2 = new DateTime(2026, 3, 15);

    private sealed class Setup
    {
        public Guid Item;
        public Guid Piece;
        public Guid Customer;
        public Guid PriceType;
    }

    private async Task<Setup> SeedAsync()
    {
        var unitClass = DictionaryManager.NewRecord<UnitClass>();
        unitClass.Code = $"C{Db.NewId():N}"[..10];
        unitClass.Name = "Count";
        unitClass = await DictionaryManager.SaveRecordAsync(unitClass);

        var piece = DictionaryManager.NewRecord<UnitOfMeasure>();
        piece.Name = "Piece";
        piece.Code = $"P{Db.NewId():N}"[..8];
        piece.DecimalPlaces = 0;
        piece.UnitClass = unitClass.MetaId;
        piece.RatioToBase = 1m;
        piece = await DictionaryManager.SaveRecordAsync(piece);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Db.NewId():N}"[..12];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = piece.MetaId;
        item = await DictionaryManager.SaveRecordAsync(item);

        var list = DictionaryManager.NewRecord<PriceType>();
        list.Name = $"Retail {Db.NewId():N}"[..16];
        list.Direction = PriceDirection.Sale;
        list = await DictionaryManager.SaveRecordAsync(list);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer.PriceType = list.MetaId;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Setup { Item = item.MetaId, Piece = piece.MetaId, Customer = customer.MetaId, PriceType = list.MetaId };
    }

    private Task<List<PriceListItem>> RowsAsync(Setup s)
        => DictionaryManager.GetRecordsAsync<PriceListItem>(
            $"PriceType = '{s.PriceType}' AND Item = '{s.Item}' AND Unit = '{s.Piece}'");

    [IntegrationTest("Свежий захват без предыдущих строк создаёт открытую строку")]
    public async Task FreshCaptureCreatesOpenRow()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 1, "ожидалась ровно одна строка, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].Price == 100m, "цена обязана быть 100, факт {0}", rows[0].Price);
        Assert.IsTrue(rows[0].EffectiveFrom == Day1, "EffectiveFrom обязан быть {0}, факт {1}", Day1, rows[0].EffectiveFrom);
        Assert.IsTrue(rows[0].EffectiveTo == null, "новая строка обязана быть открытой (EffectiveTo=null), факт {0}", rows[0].EffectiveTo);
    }

    [IntegrationTest("Ролл-форвард: следующий захват другой ценой закрывает старую строку и открывает новую")]
    public async Task RollForwardClosesOldRowAndOpensNew()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 110m, Day2);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 2, "ожидались две строки, факт {0}", rows.Count);

        var old = rows.First(r => r.Price == 100m);
        var fresh = rows.First(r => r.Price == 110m);
        Assert.IsTrue(old.EffectiveTo == Day2.AddDays(-1),
            "старая строка обязана закрыться днём раньше новой, факт {0}", old.EffectiveTo);
        Assert.IsTrue(fresh.EffectiveFrom == Day2, "новая строка обязана начаться {0}, факт {1}", Day2, fresh.EffectiveFrom);
        Assert.IsTrue(fresh.EffectiveTo == null, "новая строка обязана остаться открытой, факт {0}", fresh.EffectiveTo);
    }

    [IntegrationTest("Повторный захват в тот же день той же ценой идемпотентен")]
    public async Task SameDaySamePriceIsIdempotent()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 1, "повтор той же цены в тот же день не должен плодить строки, факт {0}", rows.Count);
    }

    [IntegrationTest("Повторный захват в тот же день другой ценой правит строку на месте")]
    public async Task SameDayDifferentPriceUpdatesInPlace()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 105m, Day1);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 1, "правка в тот же день не должна плодить вторую строку, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].Price == 105m, "цена обязана обновиться на 105, факт {0}", rows[0].Price);
        Assert.IsTrue(rows[0].EffectiveFrom == Day1, "дата начала не должна сдвигаться, факт {0}", rows[0].EffectiveFrom);
    }

    [IntegrationTest("Backdate до самой ранней существующей строки вставляет новую, ограниченную день в день")]
    public async Task BackdateBeforeEarliestRowInsertsBoundedRow()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 90m, EarlierDay);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 2, "ожидались две строки, факт {0}", rows.Count);

        var earlier = rows.First(r => r.Price == 90m);
        var later = rows.First(r => r.Price == 100m);
        Assert.IsTrue(earlier.EffectiveFrom == EarlierDay, "ранняя строка обязана начаться {0}, факт {1}", EarlierDay, earlier.EffectiveFrom);
        Assert.IsTrue(earlier.EffectiveTo == Day1.AddDays(-1),
            "ранняя строка обязана закончиться днём раньше существующей, факт {0}", earlier.EffectiveTo);
        Assert.IsTrue(later.EffectiveFrom == Day1, "существующая строка не должна сдвинуться, факт {0}", later.EffectiveFrom);
        Assert.IsTrue(later.EffectiveTo == null, "существующая строка обязана остаться открытой, факт {0}", later.EffectiveTo);
    }

    [IntegrationTest("Backdate в уже закрытую строку с другой ценой молча пропускается")]
    public async Task BackdateIntoSealedRowWithDifferentPriceIsSkipped()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 110m, Day2); // закрывает Day1-строку

        // Backdate внутрь уже ЗАКРЫТОГО окна [Day1, Day2-1] с другой ценой —
        // прошлое не переписывается, новая строка не создаётся, исключений нет.
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 95m, Day1);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 2, "backdate в запечатанную строку не должен менять их количество, факт {0}", rows.Count);
        Assert.IsTrue(rows.Any(r => r.Price == 100m), "закрытая строка обязана сохранить исходную цену 100");
        Assert.IsTrue(!rows.Any(r => r.Price == 95m), "цена 95 не должна была попасть в историю");
    }

    [IntegrationTest("Два захвата в один день (имитация двух строк документа) — выигрывает последний")]
    public async Task TwoCapturesSameDayLastWins()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 120m, Day1);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 1, "два захвата в один день должны схлопнуться в одну строку, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].Price == 120m, "последний захват обязан победить, факт {0}", rows[0].Price);
    }

    [IntegrationTest("Тип цены Kind=Calculated не хранит строк — захват no-op")]
    public async Task CalculatedPriceTypeIsNoOp()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        var basePriceType = DictionaryManager.NewRecord<PriceType>();
        basePriceType.Name = $"Base {Db.NewId():N}"[..16];
        basePriceType.Direction = PriceDirection.Sale;
        basePriceType = await DictionaryManager.SaveRecordAsync(basePriceType);

        var calc = DictionaryManager.NewRecord<PriceType>();
        calc.Name = $"Calc {Db.NewId():N}"[..16];
        calc.Direction = PriceDirection.Sale;
        calc.Kind = PriceTypeKind.Calculated;
        calc.BasePriceType = basePriceType.MetaId;
        calc.MarkupPercent = 10m;
        calc = await DictionaryManager.SaveRecordAsync(calc);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = calc.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);

        var rows = await DictionaryManager.GetRecordsAsync<PriceListItem>(
            $"PriceType = '{calc.MetaId}' AND Item = '{s.Item}'");
        Assert.IsTrue(rows.Count == 0, "Calculated-тип не должен получить ни одной строки, факт {0}", rows.Count);
    }

    [IntegrationTest("У стороны нет применимого типа цены (нет/выключен/не то направление) — захват no-op")]
    public async Task NoApplicablePriceTypeIsNoOp()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        // Клиент вообще без прайса.
        var bareCustomer = DictionaryManager.NewRecord<Customer>();
        bareCustomer.Name = "No List Ltd";
        bareCustomer.CustomerType = "B2B";
        bareCustomer = await DictionaryManager.SaveRecordAsync(bareCustomer);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, bareCustomer.MetaId, 100m, Day1);

        // Прайс выключен.
        var list = await DictionaryManager.GetRecordAsync<PriceType>(s.PriceType);
        list.IsDisabled = true;
        await DictionaryManager.SaveRecordAsync(list);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);

        // Прайс закупочный — не подходит для продажи.
        list.IsDisabled = false;
        list.Direction = PriceDirection.Purchase;
        await DictionaryManager.SaveRecordAsync(list);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 100m, Day1);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 0, "ни один из трёх случаев не должен создать строку, факт {0}", rows.Count);
    }

    [IntegrationTest("Цена ≤ 0 или пустой товар/единица — защитный no-op без исключений")]
    public async Task InvalidInputIsNoOp()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, 0m, Day1);
        await pricing.CaptureSalePriceAsync(s.Item, s.Piece, s.Customer, -5m, Day1);
        await pricing.CaptureSalePriceAsync(Guid.Empty, s.Piece, s.Customer, 100m, Day1);
        await pricing.CaptureSalePriceAsync(s.Item, Guid.Empty, s.Customer, 100m, Day1);

        var rows = await RowsAsync(s);
        Assert.IsTrue(rows.Count == 0, "защитные случаи не должны создавать строк, факт {0}", rows.Count);
    }
}
