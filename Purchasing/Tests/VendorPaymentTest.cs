using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей (VendorPayment, Supplier, StoreCell,
// VendorPaymentLinesTablePartRow…). Тестовые скрипты НЕ получают это пространство
// имён глобальным using.
using ZuloOne.Runtime.Generated;

// Замыкание цикла закупки: до появления VendorPayment регистр Payable умел
// только расти — долг перед поставщиком признавался приходом и не закрывался
// ничем. Тест гоняет ПОЛНУЮ реальную цепочку: заказ → приход (долг признан) →
// оплата отдельным документом (долг закрыт). Прямых движений в Payable нет
// нигде — иначе тест доказывал бы работу регистра, а не бизнес-цепочки.
//
// Оплата намеренно сделана ОТДЕЛЬНЫМ документом, а не подтипом заказа: смена
// подтипа снимает движения прошлого состояния и вместе с долгом обнулила бы
// приход на склад. Тест это фиксирует, проверяя, что склад после оплаты цел.
public class VendorPaymentTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid Supplier;
    }

    private async Task<Setup> SetupAsync()
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
        legalEntity.RegistrationNumber = $"REG-VP-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "WH";
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

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"VP-{Db.NewId():N}"[..12];
        cellType.Name = "Receiving";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "R-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "RAW";
        group.Name = "Raw material";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bolt";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item.IsRawMaterial = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Supplier = supplier.MetaId };
    }

    /// <summary>Заказ идёт объявленным маршрутом Draft → Ordered → Received.</summary>
    private async Task ReceiveAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
    }

    // У Payable физических измерений нет — разрез несут динамические аналитики,
    // поэтому итог собирается суммой по строкам баланса.
    private async Task<decimal> PayableAsync()
    {
        decimal payable = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("Payable")) payable += Convert.ToDecimal(r["Amount"]);
        return payable;
    }

    private Task<decimal> StockAsync(Guid location, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = location, ["Item"] = item });

    [IntegrationTest("Оплата поставщику закрывает кредиторку, приход при этом цел")]
    public async Task PaymentClearsPayable()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, qty: 10m, price: 3m);

        Assert.IsTrue(await PayableAsync() == 30m, "приход обязан признать долг 30, факт {0}", await PayableAsync());

        var payment = await DocumentManager.NewDocumentAsync<VendorPayment>();
        payment.Lines.Add(new VendorPaymentLinesTablePartRow { Supplier = s.Supplier, Amount = 30m });
        await DocumentManager.SaveDocumentAsync(payment);

        // Черновик оплаты ничего не гасит: движения принадлежат подтипу Paid. Без
        // этой проверки тест прошёл бы и в случае, если оплата проводится сама.
        Assert.IsTrue(await PayableAsync() == 30m,
            "черновик оплаты не должен гасить долг, факт {0}", await PayableAsync());

        payment.Subtype = VendorPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var payable = await PayableAsync();
        Assert.IsTrue(payable == 0m, "после оплаты долг должен быть 0, факт {0}", payable);

        // Ключевое отличие от подтипа-флипа на самом заказе: приход остаётся на
        // месте — оплата трогает только кредиторку.
        var stock = await StockAsync(s.Location, s.Item);
        Assert.IsTrue(stock == 10m, "оплата не должна трогать склад, остаток {0}", stock);
    }

    [IntegrationTest("Частичная оплата уменьшает долг на свою сумму")]
    public async Task PartialPaymentReducesDebt()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, qty: 10m, price: 3m);

        var payment = await DocumentManager.NewDocumentAsync<VendorPayment>();
        payment.Lines.Add(new VendorPaymentLinesTablePartRow { Supplier = s.Supplier, Amount = 12m });
        await DocumentManager.SaveDocumentAsync(payment);
        payment.Subtype = VendorPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var payable = await PayableAsync();
        Assert.IsTrue(payable == 18m, "30 − 12 = 18, факт {0}", payable);
    }

    [IntegrationTest("Оплата без строк отклоняется")]
    public async Task EmptyPaymentRejected()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, qty: 10m, price: 3m);

        var payment = await DocumentManager.NewDocumentAsync<VendorPayment>();
        await DocumentManager.SaveDocumentAsync(payment);

        // Обработчик отказывает исключением, а бросок обрекает окружающую
        // транзакцию прогона — поэтому после catch к базе больше не обращаемся.
        var rejected = false;
        try
        {
            payment.Subtype = VendorPayment.Subtypes.Paid;
            await DocumentManager.SaveDocumentAsync(payment);
        }
        catch (Exception ex) when (ex.Message.Contains("строки"))
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "пустая оплата обязана быть отклонена с внятной причиной");
    }

    [IntegrationTest("Оплата с неположительной суммой отклоняется")]
    public async Task NonPositiveAmountRejected()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, qty: 10m, price: 3m);

        var payment = await DocumentManager.NewDocumentAsync<VendorPayment>();
        payment.Lines.Add(new VendorPaymentLinesTablePartRow { Supplier = s.Supplier, Amount = 0m });
        await DocumentManager.SaveDocumentAsync(payment);

        var rejected = false;
        try
        {
            payment.Subtype = VendorPayment.Subtypes.Paid;
            await DocumentManager.SaveDocumentAsync(payment);
        }
        catch (Exception ex) when (ex.Message.Contains("больше нуля"))
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "нулевая сумма обязана быть отклонена с внятной причиной");
    }
}
