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
        public Guid PriceType;
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

        var list = DictionaryManager.NewRecord<PriceType>();
        list.Name = $"Retail {Db.NewId():N}"[..16];
        list.Direction = PriceDirection.Sale;
        list = await DictionaryManager.SaveRecordAsync(list);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer.PriceType = list.MetaId;
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Setup
        {
            Item = item.MetaId,
            Piece = piece.MetaId,
            Box = box.MetaId,
            Customer = customer.MetaId,
            PriceType = list.MetaId,
        };
    }

    private async Task<PriceListItem> PriceAsync(
        Setup s, Guid unit, decimal price, DateTime? from = null, DateTime? to = null, Guid? list = null)
    {
        var row = DictionaryManager.NewRecord<PriceListItem>();
        row.PriceType = list ?? s.PriceType;
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

        var purchaseList = DictionaryManager.NewRecord<PriceType>();
        purchaseList.Name = $"Vendor {Db.NewId():N}"[..16];
        purchaseList.Direction = PriceDirection.Purchase;
        purchaseList = await DictionaryManager.SaveRecordAsync(purchaseList);
        await PriceAsync(s, s.Piece, 40m, list: purchaseList.MetaId);

        // Клиенту по ошибке назначен закупочный прайс. Взять из него цену
        // продажи — продать по себестоимости поставщика; сервис обязан его
        // пропустить и уйти на умолчание товара.
        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = purchaseList.MetaId;
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

        var list = await DictionaryManager.GetRecordAsync<PriceType>(s.PriceType);
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

        var list = DictionaryManager.NewRecord<PriceType>();
        list.Name = $"Vendor {Db.NewId():N}"[..16];
        list.Direction = PriceDirection.Purchase;
        list = await DictionaryManager.SaveRecordAsync(list);
        await PriceAsync(s, s.Piece, 45m, list: list.MetaId);

        supplier.PriceType = list.MetaId;
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

    [IntegrationTest("Динамический тип цены: положительная наценка над базовым")]
    public async Task CalculatedTypeAppliesPositiveMarkup()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();
        await PriceAsync(s, s.Piece, 100m);

        var retail = DictionaryManager.NewRecord<PriceType>();
        retail.Name = $"Retail+20 {Db.NewId():N}"[..20];
        retail.Direction = PriceDirection.Sale;
        retail.Kind = PriceTypeKind.Calculated;
        retail.BasePriceType = s.PriceType;
        retail.MarkupPercent = 20m;
        retail = await DictionaryManager.SaveRecordAsync(retail);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = retail.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 120m, "100 + 20% = 120, факт {0}", price);
    }

    [IntegrationTest("Динамический тип цены: отрицательная наценка работает как скидка")]
    public async Task CalculatedTypeAppliesNegativeMarkupAsDiscount()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();
        await PriceAsync(s, s.Piece, 100m);

        var discounted = DictionaryManager.NewRecord<PriceType>();
        discounted.Name = $"Discount-10 {Db.NewId():N}"[..20];
        discounted.Direction = PriceDirection.Sale;
        discounted.Kind = PriceTypeKind.Calculated;
        discounted.BasePriceType = s.PriceType;
        discounted.MarkupPercent = -10m;
        discounted = await DictionaryManager.SaveRecordAsync(discounted);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = discounted.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 90m, "100 − 10% = 90, факт {0}", price);
    }

    [IntegrationTest("Цепочка из нескольких Calculated считается по шагам, а не суммой процентов")]
    public async Task ChainOfCalculatedTypesResolvesThroughMultipleLevels()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();
        await PriceAsync(s, s.Piece, 100m);

        var wholesale = DictionaryManager.NewRecord<PriceType>();
        wholesale.Name = $"Wholesale {Db.NewId():N}"[..20];
        wholesale.Direction = PriceDirection.Sale;
        wholesale.Kind = PriceTypeKind.Calculated;
        wholesale.BasePriceType = s.PriceType;
        wholesale.MarkupPercent = 20m; // 100 -> 120
        wholesale = await DictionaryManager.SaveRecordAsync(wholesale);

        var dealer = DictionaryManager.NewRecord<PriceType>();
        dealer.Name = $"Dealer {Db.NewId():N}"[..20];
        dealer.Direction = PriceDirection.Sale;
        dealer.Kind = PriceTypeKind.Calculated;
        dealer.BasePriceType = wholesale.MetaId;
        dealer.MarkupPercent = -5m; // 120 -> 114 (не 115 — считаем по шагам, а не суммой процентов)
        dealer = await DictionaryManager.SaveRecordAsync(dealer);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = dealer.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 114m, "100 →+20%→ 120 →−5%→ 114 (не 115), факт {0}", price);
    }

    [IntegrationTest("Динамический тип цены без базового отклоняется на сохранении")]
    public async Task CalculatedTypeWithoutBaseIsRejected()
    {
        var message = await RejectedAsync(async () =>
        {
            var calc = DictionaryManager.NewRecord<PriceType>();
            calc.Name = $"NoBase {Db.NewId():N}"[..16];
            calc.Direction = PriceDirection.Sale;
            calc.Kind = PriceTypeKind.Calculated;
            await DictionaryManager.SaveRecordAsync(calc);
        }, "Calculated без BasePriceType обязан быть отклонён");
        Assert.IsTrue(message.Contains("обязан ссылаться на базовый тип цены"),
            "ожидался отказ по отсутствию базового типа, факт: {0}", message);
    }

    [IntegrationTest("Цикл в цепочке базовых типов цены отклоняется")]
    public async Task CycleInBaseChainIsRejected()
    {
        var a = DictionaryManager.NewRecord<PriceType>();
        a.Name = $"ChainA {Db.NewId():N}"[..16];
        a.Direction = PriceDirection.Sale;
        a = await DictionaryManager.SaveRecordAsync(a);

        var b = DictionaryManager.NewRecord<PriceType>();
        b.Name = $"ChainB {Db.NewId():N}"[..16];
        b.Direction = PriceDirection.Sale;
        b.Kind = PriceTypeKind.Calculated;
        b.BasePriceType = a.MetaId;
        b.MarkupPercent = 5m;
        b = await DictionaryManager.SaveRecordAsync(b);

        // A уже базовый для B; попытка сделать A calculated-от-B замыкает A→B→A.
        var message = await RejectedAsync(async () =>
        {
            a.Kind = PriceTypeKind.Calculated;
            a.BasePriceType = b.MetaId;
            a.MarkupPercent = 3m;
            await DictionaryManager.SaveRecordAsync(a);
        }, "A→B→A обязан быть отклонён как цикл");
        Assert.IsTrue(message.Contains("зациклилась"),
            "ожидался отказ по циклу, факт: {0}", message);
    }

    [IntegrationTest("Тип цены не может ссылаться сам на себя")]
    public async Task SelfReferenceIsRejected()
    {
        // Само-ссылка на ЕЩЁ НЕ сохранённую запись непроверяема (и недостижима
        // через UI: справочник-пикер не может выбрать не существующую пока
        // запись) — self.MetaId до первого SaveRecordAsync ещё не тот Guid,
        // что окажется в БД. Поэтому сценарий тот же, что у цикла A→B→A выше:
        // сначала обычное сохранение, затем апдейт со ссылкой на самого себя.
        var self = DictionaryManager.NewRecord<PriceType>();
        self.Name = $"SelfRef {Db.NewId():N}"[..16];
        self.Direction = PriceDirection.Sale;
        self = await DictionaryManager.SaveRecordAsync(self);

        var message = await RejectedAsync(async () =>
        {
            self.Kind = PriceTypeKind.Calculated;
            self.BasePriceType = self.MetaId;
            self.MarkupPercent = 10m;
            await DictionaryManager.SaveRecordAsync(self);
        }, "self-reference обязан быть отклонён");
        Assert.IsTrue(message.Contains("зациклилась"),
            "ожидался отказ по циклу (self-reference), факт: {0}", message);
    }

    [IntegrationTest("Базовый тип цены не может ссылаться на другой тип цены")]
    public async Task BaseTypeWithBasePriceTypeIsRejected()
    {
        var s = await SeedAsync();
        var message = await RejectedAsync(async () =>
        {
            var invalid = DictionaryManager.NewRecord<PriceType>();
            invalid.Name = $"BadBase {Db.NewId():N}"[..16];
            invalid.Direction = PriceDirection.Sale;
            invalid.Kind = PriceTypeKind.Base;
            invalid.BasePriceType = s.PriceType;
            await DictionaryManager.SaveRecordAsync(invalid);
        }, "Base с BasePriceType обязан быть отклонён");
        Assert.IsTrue(message.Contains("не может ссылаться на другой тип цены"),
            "ожидался отказ по несогласованности Base, факт: {0}", message);
    }

    [IntegrationTest("Базовый тип цены не может иметь наценку")]
    public async Task BaseTypeWithNonZeroMarkupIsRejected()
    {
        var message = await RejectedAsync(async () =>
        {
            var invalid = DictionaryManager.NewRecord<PriceType>();
            invalid.Name = $"BadMarkup {Db.NewId():N}"[..16];
            invalid.Direction = PriceDirection.Sale;
            invalid.Kind = PriceTypeKind.Base;
            invalid.MarkupPercent = 5m;
            await DictionaryManager.SaveRecordAsync(invalid);
        }, "Base с ненулевой наценкой обязан быть отклонён");
        Assert.IsTrue(message.Contains("не может иметь наценку"),
            "ожидался отказ по наценке у Base, факт: {0}", message);
    }

    [IntegrationTest("Динамический тип цены вправе считаться от базового с ДРУГИМ направлением")]
    public async Task CalculatedTypeMayBaseOnDifferentDirection()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        // Закупочный базовый тип с ценой 50 у ЭТОГО ЖЕ товара.
        var purchaseBase = DictionaryManager.NewRecord<PriceType>();
        purchaseBase.Name = $"PurchBase {Db.NewId():N}"[..20];
        purchaseBase.Direction = PriceDirection.Purchase;
        purchaseBase = await DictionaryManager.SaveRecordAsync(purchaseBase);
        await PriceAsync(s, s.Piece, 50m, list: purchaseBase.MetaId);

        // Дилерская цена ПРОДАЖИ считается от закупочной — ключевой сценарий фичи.
        var dealerSale = DictionaryManager.NewRecord<PriceType>();
        dealerSale.Name = $"DealerSale {Db.NewId():N}"[..20];
        dealerSale.Direction = PriceDirection.Sale;
        dealerSale.Kind = PriceTypeKind.Calculated;
        dealerSale.BasePriceType = purchaseBase.MetaId;
        dealerSale.MarkupPercent = 20m;
        dealerSale = await DictionaryManager.SaveRecordAsync(dealerSale);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = dealerSale.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 60m, "закупочная 50 + 20% = 60 для дилерской продажи, факт {0}", price);
    }

    [IntegrationTest("Строку цены нельзя завести у расчётного (Calculated) типа цены")]
    public async Task PriceListItemUnderCalculatedTypeIsRejected()
    {
        var s = await SeedAsync();
        var calc = DictionaryManager.NewRecord<PriceType>();
        calc.Name = $"CalcOnly {Db.NewId():N}"[..16];
        calc.Direction = PriceDirection.Sale;
        calc.Kind = PriceTypeKind.Calculated;
        calc.BasePriceType = s.PriceType;
        calc.MarkupPercent = 10m;
        calc = await DictionaryManager.SaveRecordAsync(calc);

        var message = await RejectedAsync(
            () => PriceAsync(s, s.Piece, 50m, list: calc.MetaId),
            "строка цены под Calculated-типом обязана быть отклонена");
        Assert.IsTrue(message.Contains("не задаётся строками"),
            "ожидался отказ по Calculated-типу, факт: {0}", message);
    }

    [IntegrationTest("Тип цены с уже существующими строками нельзя переключить в Calculated")]
    public async Task SwitchingToCalculatedWithExistingRowsIsRejected()
    {
        var s = await SeedAsync();
        await PriceAsync(s, s.Piece, 100m);

        var basePriceType = DictionaryManager.NewRecord<PriceType>();
        basePriceType.Name = $"NeedsBase {Db.NewId():N}"[..16];
        basePriceType.Direction = PriceDirection.Sale;
        basePriceType = await DictionaryManager.SaveRecordAsync(basePriceType);

        var message = await RejectedAsync(async () =>
        {
            var list = await DictionaryManager.GetRecordAsync<PriceType>(s.PriceType);
            list.Kind = PriceTypeKind.Calculated;
            list.BasePriceType = basePriceType.MetaId;
            list.MarkupPercent = 10m;
            await DictionaryManager.SaveRecordAsync(list);
        }, "переключение в Calculated при существующих строках обязано быть отклонено");
        Assert.IsTrue(message.Contains("уже есть строки"),
            "ожидался отказ по существующим строкам, факт: {0}", message);
    }

    [IntegrationTest("Наценка -100% и меньше у Calculated отклоняется на сохранении")]
    public async Task MarkupAtOrBelowMinus100PercentIsRejected()
    {
        var s = await SeedAsync();
        var message = await RejectedAsync(async () =>
        {
            var calc = DictionaryManager.NewRecord<PriceType>();
            calc.Name = $"ZeroFloor {Db.NewId():N}"[..16];
            calc.Direction = PriceDirection.Sale;
            calc.Kind = PriceTypeKind.Calculated;
            calc.BasePriceType = s.PriceType;
            calc.MarkupPercent = -100m;
            await DictionaryManager.SaveRecordAsync(calc);
        }, "наценка -100% обнуляет цену базового типа и обязана быть отклонена");
        Assert.IsTrue(message.Contains("обнулится или уйдёт в минус"),
            "ожидался отказ по наценке -100% и ниже, факт: {0}", message);
    }

    [IntegrationTest("Наценка чуть выше -100% всё ещё разрешена и даёт малую положительную цену")]
    public async Task MarkupJustAboveMinus100PercentResolvesToSmallPositivePrice()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();
        await PriceAsync(s, s.Piece, 100m);

        var almostFree = DictionaryManager.NewRecord<PriceType>();
        almostFree.Name = $"AlmostFree {Db.NewId():N}"[..20];
        almostFree.Direction = PriceDirection.Sale;
        almostFree.Kind = PriceTypeKind.Calculated;
        almostFree.BasePriceType = s.PriceType;
        almostFree.MarkupPercent = -99m;
        almostFree = await DictionaryManager.SaveRecordAsync(almostFree);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = almostFree.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 1m, "100 − 99% = 1, факт {0}", price);
    }

    [IntegrationTest("Несколько строк на разные единицы товара, сходящиеся после пересчёта: цена разрешается")]
    public async Task MultipleConvertibleUnitRowsThatAgreeResolve()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        var palletClass = DictionaryManager.NewRecord<UnitClass>();
        palletClass.Code = $"PC{Db.NewId():N}"[..10];
        palletClass.Name = "PalletClass";
        palletClass = await DictionaryManager.SaveRecordAsync(palletClass);

        var pallet = DictionaryManager.NewRecord<UnitOfMeasure>();
        pallet.Name = "Pallet";
        pallet.Code = $"PL{Db.NewId():N}"[..8];
        pallet.DecimalPlaces = 0;
        pallet.UnitClass = palletClass.MetaId;
        pallet = await DictionaryManager.SaveRecordAsync(pallet);

        var pack = DictionaryManager.NewRecord<ItemUnit>();
        pack.Item = s.Item;
        pack.Unit = pallet.MetaId;
        pack.QtyInBaseUnit = 120m; // 10 ящиков
        await DictionaryManager.SaveRecordAsync(pack);

        // Ни одной строки на штуку — только ящик и паллета, обе дают одну и ту же
        // цену за штуку после пересчёта: 120/12 = 1200/120 = 10.
        await PriceAsync(s, s.Box, 120m);
        await PriceAsync(s, pallet.MetaId, 1200m);

        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 10m, "ящик и паллета согласованно дают 10 за штуку, факт {0}", price);
    }

    [IntegrationTest("Несколько строк на разные единицы товара, расходящиеся после пересчёта: цена не подбирается (null), а не угадывается")]
    public async Task MultipleConvertibleUnitRowsThatDisagreeReturnNull()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();

        var palletClass = DictionaryManager.NewRecord<UnitClass>();
        palletClass.Code = $"PC{Db.NewId():N}"[..10];
        palletClass.Name = "PalletClass";
        palletClass = await DictionaryManager.SaveRecordAsync(palletClass);

        var pallet = DictionaryManager.NewRecord<UnitOfMeasure>();
        pallet.Name = "Pallet";
        pallet.Code = $"PL{Db.NewId():N}"[..8];
        pallet.DecimalPlaces = 0;
        pallet.UnitClass = palletClass.MetaId;
        pallet = await DictionaryManager.SaveRecordAsync(pallet);

        var pack = DictionaryManager.NewRecord<ItemUnit>();
        pack.Item = s.Item;
        pack.Unit = pallet.MetaId;
        pack.QtyInBaseUnit = 120m;
        await DictionaryManager.SaveRecordAsync(pack);

        // Ящик 120 (10/шт) и паллета 1300 (10.83/шт, объёмная наценка) —
        // расходятся. Порядок строк из базы ничем не гарантирован: угадывать
        // одну из них молча нельзя, честный ответ — null, как при отсутствии цены.
        await PriceAsync(s, s.Box, 120m);
        await PriceAsync(s, pallet.MetaId, 1300m);

        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == null, "расходящиеся цены за разные единицы не подбираются, факт {0}", price);
    }

    [IntegrationTest("Наценка, обнулившая цену после округления, не выдаётся как 0 — лестница уходит на следующую ступень")]
    public async Task MarkupRoundingToZeroFallsThroughToItemDefault()
    {
        var s = await SeedAsync(defaultSalePrice: 5m);
        var pricing = GetService<IPricingService>();
        await PriceAsync(s, s.Piece, 0.03m);

        var nearlyFree = DictionaryManager.NewRecord<PriceType>();
        nearlyFree.Name = $"NearlyFree {Db.NewId():N}"[..18];
        nearlyFree.Direction = PriceDirection.Sale;
        nearlyFree.Kind = PriceTypeKind.Calculated;
        nearlyFree.BasePriceType = s.PriceType;
        nearlyFree.MarkupPercent = -91m;
        nearlyFree = await DictionaryManager.SaveRecordAsync(nearlyFree);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = nearlyFree.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        // 0.03 × 0.09 = 0.0027 → округление до 0.00 — цена схлопнулась в ноль
        // самим округлением этой ступени, а не по вине конкретных чисел где-то
        // ещё, и это не значит «ступень ответила 0», а значит «не ответила».
        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 5m,
            "цена, обнулившаяся округлением, обязана уступить умолчанию товара 5, факт {0}", price);
    }

    [IntegrationTest("Округление на КАЖДОЙ ступени цепочки — не то же самое, что округлить один раз в конце")]
    public async Task ChainRoundsAtEveryStepNotOnlyAtTheEnd()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();
        await PriceAsync(s, s.Piece, 1.00m);

        var stepOne = DictionaryManager.NewRecord<PriceType>();
        stepOne.Name = $"Step1 {Db.NewId():N}"[..18];
        stepOne.Direction = PriceDirection.Sale;
        stepOne.Kind = PriceTypeKind.Calculated;
        stepOne.BasePriceType = s.PriceType;
        stepOne.MarkupPercent = 0.5m; // 1.00 -> 1.005 -> округляется здесь же до 1.01
        stepOne = await DictionaryManager.SaveRecordAsync(stepOne);

        var stepTwo = DictionaryManager.NewRecord<PriceType>();
        stepTwo.Name = $"Step2 {Db.NewId():N}"[..18];
        stepTwo.Direction = PriceDirection.Sale;
        stepTwo.Kind = PriceTypeKind.Calculated;
        stepTwo.BasePriceType = stepOne.MetaId;
        stepTwo.MarkupPercent = 0.5m;
        stepTwo = await DictionaryManager.SaveRecordAsync(stepTwo);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = stepTwo.MetaId;
        await DictionaryManager.SaveRecordAsync(customer);

        // Пошагово: 1.00 →+0.5%→ 1.005 →округл.→ 1.01 →+0.5%→ 1.01505 →округл.→
        // 1.02. Если бы наценки сначала перемножили (1.005×1.005=1.010025) и
        // округлили один раз в конце, получилось бы 1.01 — на цент меньше. Тест
        // ловит именно эту развилку, а не то, что цепочка вообще посчиталась.
        var price = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(price == 1.02m,
            "пошаговое округление обязано дать 1.02 (не 1.01 от умножения одним разом), факт {0}", price);
    }

    [IntegrationTest("Отключённый тип цены В СЕРЕДИНЕ цепочки (не тот, что назначен клиенту напрямую) даёт null")]
    public async Task DisabledBasePriceTypeMidChainResolvesToNull()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();
        await PriceAsync(s, s.Piece, 100m);

        var wholesale = DictionaryManager.NewRecord<PriceType>();
        wholesale.Name = $"Wholesale {Db.NewId():N}"[..18];
        wholesale.Direction = PriceDirection.Sale;
        wholesale.Kind = PriceTypeKind.Calculated;
        wholesale.BasePriceType = s.PriceType;
        wholesale.MarkupPercent = 20m;
        wholesale = await DictionaryManager.SaveRecordAsync(wholesale);

        var dealer = DictionaryManager.NewRecord<PriceType>();
        dealer.Name = $"Dealer {Db.NewId():N}"[..18];
        dealer.Direction = PriceDirection.Sale;
        dealer.Kind = PriceTypeKind.Calculated;
        dealer.BasePriceType = wholesale.MetaId;
        dealer.MarkupPercent = -5m;
        dealer = await DictionaryManager.SaveRecordAsync(dealer);

        var customer = await DictionaryManager.GetRecordAsync<Customer>(s.Customer);
        customer.PriceType = dealer.MetaId; // клиенту назначена верхушка, не Wholesale
        await DictionaryManager.SaveRecordAsync(customer);

        var before = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(before == 114m, "цепочка исправна до отключения звена, факт {0}", before);

        // Wholesale выключается НЕ у клиента напрямую (это уже ловит
        // DisabledPriceListIsIgnored), а как промежуточное звено, куда
        // PriceTypeOfAsync вообще не заглядывает, — проверка внутри
        // ResolvePriceForTypeAsync обязана поймать его сама.
        wholesale.IsDisabled = true;
        await DictionaryManager.SaveRecordAsync(wholesale);

        var afterDisable = await pricing.ResolveSalePriceAsync(s.Item, s.Piece, s.Customer, March);
        Assert.IsTrue(afterDisable == null,
            "отключение звена в середине цепочки обязано дать null, а не старую или нулевую цену, факт {0}", afterDisable);
    }

    [IntegrationTest("SetPriceAsync пишет строку, ResolveForType находит её; Calculated отклоняется")]
    public async Task SetPriceWritesAndResolveForTypeFinds()
    {
        var s = await SeedAsync(defaultSalePrice: 99m);
        var pricing = GetService<IPricingService>();

        var id = await pricing.SetPriceAsync(s.PriceType, s.Item, s.Piece, 42m, March, May);
        Assert.IsTrue(id != Guid.Empty, "SetPrice обязан вернуть id строки");

        var byType = await pricing.ResolveForTypeAsync(s.Item, s.Piece, s.PriceType, March);
        Assert.IsTrue(byType == 42m, "ResolveForType обязан найти поставленную цену, факт {0}", byType);

        // Умолчание карточки в разрешение типа не входит — иначе Calculated без
        // своей строки тихо сошёлся бы к 99 мимо наценки.
        var emptyType = DictionaryManager.NewRecord<PriceType>();
        emptyType.Name = $"Bare {Db.NewId():N}"[..14];
        emptyType.Direction = PriceDirection.Sale;
        emptyType = await DictionaryManager.SaveRecordAsync(emptyType);
        var bare = await pricing.ResolveForTypeAsync(s.Item, s.Piece, emptyType.MetaId, March);
        Assert.IsTrue(bare == null, "тип без строк не должен брать умолчание товара, факт {0}", bare);

        var calc = DictionaryManager.NewRecord<PriceType>();
        calc.Name = $"Calc {Db.NewId():N}"[..14];
        calc.Direction = PriceDirection.Sale;
        calc.Kind = PriceTypeKind.Calculated;
        calc.BasePriceType = s.PriceType;
        calc.MarkupPercent = 10m;
        calc = await DictionaryManager.SaveRecordAsync(calc);

        var rejected = await RejectedAsync(
            () => pricing.SetPriceAsync(calc.MetaId, s.Item, s.Piece, 10m, null, null),
            "строка под Calculated");
        Assert.IsTrue(rejected.Contains("расчётный"), "отказ SetPrice под Calculated, факт: {0}", rejected);
    }

    [IntegrationTest("Окно цены закрывается календарным днём: 31 марта 15:00 ещё покрыто EffectiveTo=31.03")]
    public async Task LastDayAfternoonStillCovered()
    {
        var s = await SeedAsync();
        var pricing = GetService<IPricingService>();
        await PriceAsync(s, s.Piece, 7m, from: new DateTime(2026, 3, 1), to: new DateTime(2026, 3, 31));

        var afternoon = await pricing.ResolveSalePriceAsync(
            s.Item, s.Piece, s.Customer, new DateTime(2026, 3, 31, 15, 0, 0));
        Assert.IsTrue(afternoon == 7m, "последний день окна обязан покрывать любой час, факт {0}", afternoon);

        var nextDay = await pricing.ResolveSalePriceAsync(
            s.Item, s.Piece, s.Customer, new DateTime(2026, 4, 1, 0, 0, 0));
        Assert.IsTrue(nextDay == null, "1 апреля уже вне окна, факт {0}", nextDay);
    }
}
