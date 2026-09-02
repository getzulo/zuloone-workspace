using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Разрешение цены: какая цена у товара в ЭТОЙ единице на ЭТУ дату.
//
// Каждый кейс здесь проверяет ступень лестницы или её ограничение, а не «цена
// нашлась». Три вещи, которые лестница обязана делать правильно и которые
// молча ломаются, если сделать наивно:
//
//   * цена возвращается за ЕДИНИЦУ СТРОКИ. Денежные ноги документа считаются от
//     введённого Quantity, а не от BaseQuantity, — цена за базовую единицу,
//     подставленная в строку в ящиках, занизит выручку ровно во вложенность раз;
//   * прайс продажи не подставляется в закупку и наоборот;
//   * на дату действует ровно одна строка — пересечение окон отклоняется на
//     вводе, а не разруливается «возьмём последнюю» при подборе.
public class PriceResolutionTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private static readonly DateTime March = new DateTime(2026, 3, 15);
    private static readonly DateTime May = new DateTime(2026, 5, 15);

    private sealed class Setup
    {
        public Guid Item;
        public Guid Piece;
        public Guid Box;      // 12 штук, задан упаковкой товара
        public Guid Customer;
        public Guid PriceList;
    }

    // Товар со штучной базой и ящиком по 12, клиент со своим прайсом продажи.
    private async Task<Setup> SeedAsync(decimal defaultSalePrice = 0m)
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

        // У ящика коэффициента вида величины НЕТ намеренно: «ящик» — не единица
        // счёта вообще, а упаковка конкретного товара. Так он и заводится ниже.
        var box = DictionaryManager.NewRecord<UnitOfMeasure>();
        box.Name = "Box";
        box.Code = $"B{Db.NewId():N}"[..8];
        box.DecimalPlaces = 0;
        box.UnitClass = unitClass.MetaId;
        box = await DictionaryManager.SaveRecordAsync(box);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Db.NewId():N}"[..12];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = piece.MetaId;
        if (defaultSalePrice > 0m) item.DefaultSalePrice = defaultSalePrice;
        item = await DictionaryManager.SaveRecordAsync(item);

        var pack = DictionaryManager.NewRecord<ItemUnit>();
        pack.Item = item.MetaId;
        pack.Unit = box.MetaId;
        pack.QtyInBaseUnit = 12m;
        await DictionaryManager.SaveRecordAsync(pack);

        var list = DictionaryManager.NewRecord<PriceList>();
        list.Name = $"Retail {Db.NewId():N}"[..16];
        list.Direction = PriceDirection.Sale;
        list = await DictionaryManager.SaveRecordAsync(list);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer.PriceList = list.MetaId;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Setup
        {
            Item = item.MetaId,
            Piece = piece.MetaId,
            Box = box.MetaId,
            Customer = customer.MetaId,
            PriceList = list.MetaId,
        };
    }

    private async Task<PriceListItem> PriceAsync(
        Setup s, Guid unit, decimal price, DateTime? from = null, DateTime? to = null, Guid? list = null)
    {
        var row = DictionaryManager.NewRecord<PriceListItem>();
        row.PriceList = list ?? s.PriceList;
        row.Item = s.Item;
        row.Unit = unit;
        row.Price = price;
        row.EffectiveFrom = from;
        row.EffectiveTo = to;
        return await DictionaryManager.SaveRecordAsync(row);
    }

    private static async Task<string> RejectedAsync(Func<Task> action, string because)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        Assert.IsTrue(false, "сохранение обязано быть отклонено: {0}", because);
        return string.Empty;
    }

    [IntegrationTest("Лестница: прайс клиента важнее умолчания товара, без прайса берётся умолчание, без обоих — null")]
    public async Task LadderPrefersPriceListOverItemDefault()
    {
        var s = await SeedAsync(defaultSalePrice: 100m);
        var pricing = GetService<IPricingService>();

        // Ступень 3: строк прайса нет — работает умолчание карточки.
        var byDefault = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(byDefault == 100m, "без строк прайса ожидалось умолчание товара 100, факт {0}", byDefault);

        // Ступень 1: появилась строка прайса — она перебивает умолчание.
        await PriceAsync(s, s.Piece, 80m);
        var byList = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(byList == 80m, "строка прайса обязана перебить умолчание 100, факт {0}", byList);

        // Ступень 4: клиента нет и умолчания нет — null, и это не ошибка.
        var bare = await SeedAsync();
        var none = await pricing.ResolveSalePriceAsync(bare.Item, bare.Piece, null, March);
        Assert.IsTrue(none == null, "цены нет ниоткуда — ожидался null, факт {0}", none);
    }

    [IntegrationTest("Единицы: цена за ящик пересчитывается в цену за штуку и обратно")]
    public async Task PriceIsPerLineUnit()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        // В прайсе только ящик по 120; в ящике 12 штук.
        await PriceAsync(s, s.Box, 120m);

        var perBox = await pricing.ResolveSalePriceAsync(s.Item, s.Box, s.Customer, March);
        Assert.IsTrue(perBox == 120m, "цена за ящик задана как есть — 120, факт {0}", perBox);

        // Цена переводится ОБРАТНО количеству: штук больше, чем ящиков, значит
        // цена за штуку меньше. Вернуть здесь 120 — занизить выручку в 12 раз.
        var perPiece = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(perPiece == 10m, "120 за ящик из 12 штук = 10 за штуку, факт {0}", perPiece);

        // И умолчание товара тоже задано за базовую единицу, значит тоже переводится.
        var d = await SeedAsync(defaultSalePrice: 7m);
        var defaultPerBox = await pricing.ResolveSalePriceAsync(d.Item, d.Box, d.Customer, March);
        Assert.IsTrue(defaultPerBox == 84m, "7 за штуку × 12 = 84 за ящик, факт {0}", defaultPerBox);
    }

    [IntegrationTest("Окна дат: на каждую дату действует своя цена, пересечение окон отклоняется")]
    public async Task DateWindowsSelectOnePriceAndForbidOverlap()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        await PriceAsync(s, s.Piece, 80m, to: new DateTime(2026, 3, 31));
        await PriceAsync(s, s.Piece, 90m, from: new DateTime(2026, 4, 1));

        var inMarch = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        var inMay = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, May);
        Assert.IsTrue(inMarch == 80m, "в марте действует 80, факт {0}", inMarch);
        Assert.IsTrue(inMay == 90m, "в мае действует 90, факт {0}", inMay);

        // Третья строка залезает на оба окна — подбор перестал бы быть однозначным.
        var message = await RejectedAsync(
            () => PriceAsync(s, s.Piece, 85m, from: new DateTime(2026, 3, 20), to: new DateTime(2026, 4, 20)),
            "окно 20.03–20.04 пересекает оба существующих");
        // Сверяем ТЕКСТ: тест на отказ обязан падать по своей причине, а не по
        // случайной — «товар не найден» тоже даёт исключение.
        Assert.IsTrue(message.Contains("пересекающийся период"),
            "ожидался отказ по пересечению периодов, факт: {0}", message);

        // Касание границами — тоже пересечение: 31 марта иначе имело бы две цены.
        var touching = await RejectedAsync(
            () => PriceAsync(s, s.Piece, 85m, from: new DateTime(2026, 3, 31), to: new DateTime(2026, 3, 31)),
            "31 марта уже накрыто первой строкой");
        Assert.IsTrue(touching.Contains("пересекающийся период"),
            "ожидался отказ по пересечению периодов, факт: {0}", touching);
    }

    [IntegrationTest("Направление: прайс закупки не подставляется в продажу")]
    public async Task PurchaseListIsNotUsedForSale()
    {
        var s = await SeedAsync(defaultSalePrice: 100m);
        var pricing = GetService<IPricingService>();

        var purchaseList = DictionaryManager.NewRecord<PriceList>();
        purchaseList.Name = $"Vendor {Db.NewId():N}"[..16];
        purchaseList.Direction = PriceDirection.Purchase;
        purchaseList = await DictionaryManager.SaveRecordAsync(purchaseList);
        await PriceAsync(s, s.Piece, 40m, list: purchaseList.MetaId);

        // Клиенту по ошибке назначен закупочный прайс. Взять из него цену
        // продажи — продать по себестоимости поставщика; сервис обязан его
        // пропустить и уйти на умолчание товара.
        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceList = purchaseList.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 100m, "закупочный прайс в продаже не применяется, ожидалось 100, факт {0}", price);
    }

    [IntegrationTest("Выключенный прайс-лист не применяется")]
    public async Task DisabledPriceListIsIgnored()
    {
        var s = await SeedAsync(defaultSalePrice: 100m);
        var pricing = GetService<IPricingService>();
        await PriceAsync(s, s.Piece, 80m);

        var list = await DictionaryManager.GetRecordAsync<PriceList>(s.PriceList);
        list.IsDisabled = true;
        await DictionaryManager.SaveRecordAsync(list);

        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 100m, "выключенный прайс игнорируется, ожидалось умолчание 100, факт {0}", price);
    }

    [IntegrationTest("Закупка: прайс поставщика важнее умолчания карточки")]
    public async Task PurchaseSideUsesSupplierListAndItsOwnFallback()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        // Закупочное умолчание — ОТДЕЛЬНОЕ поле карточки: продать по цене
        // закупки и купить по цене продажи одинаково недопустимо.
        var card = await DictionaryManager.GetRecordAsync<Item>(s.Item);
        card.DefaultPurchasePrice = 60m;
        await DictionaryManager.SaveRecordAsync(card);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = $"Vendor {Db.NewId():N}"[..16];
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        var byDefault = await pricing.ResolvePurchasePriceAsync(s.Item, s.Piece, supplier.MetaId, March);
        Assert.IsTrue(byDefault == 60m, "без прайса поставщика ожидалось умолчание 60, факт {0}", byDefault);

        var list = DictionaryManager.NewRecord<PriceList>();
        list.Name = $"Vendor {Db.NewId():N}"[..16];
        list.Direction = PriceDirection.Purchase;
        list = await DictionaryManager.SaveRecordAsync(list);
        await PriceAsync(s, s.Piece, 45m, list: list.MetaId);

        supplier.PriceList = list.MetaId;
        await DictionaryManager.SaveRecordAsync(supplier);

        var byList = await pricing.ResolvePurchasePriceAsync(s.Item, s.Piece, supplier.MetaId, March);
        Assert.IsTrue(byList == 45m, "прайс поставщика обязан перебить умолчание 60, факт {0}", byList);

        // И продажная сторона его не видит — умолчание продажи не задано, значит null.
        var asSale = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, supplier.MetaId, March);
        Assert.IsTrue(asSale == null, "закупочный прайс в продаже не виден, факт {0}", asSale);
    }

    [IntegrationTest("Скидка: сумма строки со скидкой считается от той же базы")]
    public async Task LineAmountAppliesDiscountPercent()
    {
        await Task.CompletedTask;
        var pricing = GetService<IPricingService>();

        // Скидка — ПРОЦЕНТ (15 = 15%), а не доля: так она задана в LoyaltyTier.
        Assert.IsTrue(pricing.LineAmount(10m, 100m, 15m) == 850m,
            "10 × 100 − 15% = 850, факт {0}", pricing.LineAmount(10m, 100m, 15m));
        Assert.IsTrue(pricing.LineAmount(10m, 100m, 0m) == 1000m,
            "нулевая скидка ничего не меняет, факт {0}", pricing.LineAmount(10m, 100m, 0m));
        // Старая двухаргументная перегрузка обязана остаться эквивалентной нулю:
        // её зовут девять существующих проводок.
        Assert.IsTrue(pricing.LineAmount(3m, 5m) == pricing.LineAmount(3m, 5m, 0m),
            "перегрузки разошлись: {0} против {1}", pricing.LineAmount(3m, 5m), pricing.LineAmount(3m, 5m, 0m));
    }
}
