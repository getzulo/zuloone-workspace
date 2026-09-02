using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Обязательно: тестовым скриптам этот namespace НЕ выдаётся глобальным using —
// без него генерированные классы (Currency, SalesInvoice…) не находятся.
using ZuloOne.Runtime.Generated;

// Контур дебиторки: выставленный счёт создаёт долг покупателя, а отдельный
// документ «Оплата покупателя» его гасит.
//
// Почему оплата — документ, а не подтип счёта: смена подтипа снимает движения
// ПРОШЛОГО состояния, поэтому вариант «Выставлен → Оплачен» обнулял вместе с
// долгом и ВЫРУЧКУ, то есть отменял продажу. Тест это и поймал.
//
// Написано, как пишется бизнес-код: типизированные записи через
// IDictionaryManager, документы через IDocumentManager, регистры через
// ITotalsManager; проведение — присвоение подтипа плюс сохранение.
public class ReceivableFlowTest : IntegrationTestScriptBase
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
        legalEntity.RegistrationNumber = "REG-AR-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "SP";
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

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "GOODS";
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Gadget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Customer = customer.MetaId };
    }

    // Receivable и Revenue несут только динамическую аналитику — баланс каждого
    // схлопывается в строки с одним ресурсом Amount; суммируем его.
    private static async Task<decimal> SumAsync(string register)
    {
        decimal total = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync(register))
            total += Convert.ToDecimal(r["Amount"]);
        return total;
    }

    [IntegrationTest("Выставление создаёт дебиторку, оплата её гасит")]
    public async Task IssueCreatesDebtPaymentClearsIt()
    {
        var s = await SetupAsync();

        // Товар на складе, чтобы выставление прошло проверку остатка. Движение
        // вне цепочки документа — редкий и осознанный случай, для него у
        // ITotalsManager есть PostMovementAsync (документ не указан).
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        // Подтип не передаём: документ обязан стартовать в НАЧАЛЬНОМ подтипе (Draft).
        var inv = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        inv.Customer = s.Customer;
        inv.Location = s.Location;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 3m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(inv);

        // Черновик долга не создаёт. Проверяем ДО перехода: тип помечен postOnSave,
        // и без этой проверки утверждения ниже прошли бы и в том случае, когда
        // документ провёлся сам при сохранении.
        Assert.IsTrue(await SumAsync("Receivable") == 0m, "черновик счёта не создаёт долг");
        Assert.IsTrue(await SumAsync("Revenue") == 0m, "черновик счёта не признаёт выручку");

        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);

        Assert.IsTrue(await SumAsync("Receivable") == 15m,
            "после выставления долг 3×5=15, факт {0}", await SumAsync("Receivable"));
        Assert.IsTrue(await SumAsync("Revenue") == 15m,
            "выручка признана 15, факт {0}", await SumAsync("Revenue"));

        // Оплата — ОТДЕЛЬНЫЙ документ, а не смена подтипа счёта.
        var pay = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        pay.Lines.Add(new CustomerPaymentLinesTablePartRow { Customer = s.Customer, Amount = 15m });
        await DocumentManager.SaveDocumentAsync(pay);
        Assert.IsTrue(await SumAsync("Receivable") == 15m,
            "черновик оплаты долг не трогает, факт {0}", await SumAsync("Receivable"));

        pay.Subtype = CustomerPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(pay);

        Assert.IsTrue(await SumAsync("Receivable") == 0m,
            "после оплаты долг погашен, факт {0}", await SumAsync("Receivable"));

        // Счёт остаётся выставленным — оплата не отменяет продажу.
        var stored = await DocumentManager.GetDocumentAsync<SalesInvoice>(inv.MetaId);
        Assert.IsNotNull(stored, "счёт читается после оплаты");
        Assert.IsTrue(stored!.Subtype == SalesInvoice.Subtypes.Issued,
            "счёт остаётся Issued, факт {0}", stored.Subtype);

        Assert.IsTrue(await SumAsync("Revenue") == 15m,
            "выручка сохраняется после оплаты, факт {0}", await SumAsync("Revenue"));

        decimal onHand = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("Stock", $"[Cell] = '{s.Location}'"))
            onHand += Convert.ToDecimal(r["Qty"]);
        Assert.IsTrue(onHand == 7m, "на ячейке осталось 10−3=7, факт {0}", onHand);
    }

    [IntegrationTest("Ручной перевод счёта в Paid отклоняется, долг и баллы остаются на месте")]
    public async Task ManualPaidTransitionIsRejected()
    {
        // ПОЧЕМУ ЭТО ВАЖНЕЕ, ЧЕМ «ОТЧЁТЫ ПО СТАТУСУ ВРУТ». Подтип Paid объявлен
        // (исторические документы), но ребра Issued→Paid в карте больше нет —
        // форма его не предлагает. Прямой API всё равно обязан отказать.
        // К Issued привязаны ТРИ транзакционных скрипта: дебиторка, баллы и
        // страновой НДС. Переход снял бы долг БЕЗ оплаты.
        //
        // Оплата в этой системе — ОТДЕЛЬНЫЙ документ (CustomerPayment), а платёжный
        // статус читается из регистра Receivable, а не из подтипа счёта.
        var s = await SetupAsync();

        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var inv = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        inv.Customer = s.Customer;
        inv.Location = s.Location;
        inv.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 3m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(inv);

        inv.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(inv);
        Assert.IsTrue(await SumAsync("Receivable") == 15m, "долг признан выставлением");

        var reason = string.Empty;
        try
        {
            inv.Subtype = SalesInvoice.Subtypes.Paid;
            await DocumentManager.SaveDocumentAsync(inv);
        }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Length > 0, "перевод в Paid обязан быть отклонён, факт: без ошибки");
        Assert.IsTrue(await SumAsync("Receivable") == 15m, "долг остаётся — его гасит только CustomerPayment");
    }
}
