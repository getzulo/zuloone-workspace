using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

// ═══ ПОДАТКОВИЙ КРЕДИТ ПО ПЕРШІЙ ПОДІЇ (ПКУ 198.2) ═══════════════════════════
//
// Зеркало продаж. Право на зачёт входящего ПДВ возникает на дату того из
// событий, что случилось РАНЬШЕ: списание денег поставщику или получение товара.
// Значит предоплата поставщику даёт кредит СРАЗУ, а пришедшая следом поставка в
// её пределах — уже нет.
//
// ЧТО ЗДЕСЬ ЛЕГКО СЛОМАТЬ. Драйвер молчит во всех отказах: нет ставки — нет
// движений, режим не тот — нет движений. Поэтому кейсы, которые ждут НОЛЬ,
// сначала доказывают, что события вообще записались: иначе «кредит 0» одинаково
// выглядит и при верном правиле, и при полностью неработающем пакете. Ровно так
// эти два кейса и были зелёными, пока транзакционные скрипты не были привязаны к
// подтипам и не исполнялись вовсе.
public class UkrainePurchaseVatTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid LegalEntity;
        public Guid Supplier;
    }

    private async Task<Setup> SetupAsync(UaTaxRegime regime = UaTaxRegime.VatPayer, bool configureTax = true)
    {
        await Db.SetAccountingPeriodsAsync(null, null);

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = $"U{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "₴";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "380";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "Kyiv Buyer";
        legalEntity.RegistrationNumber = $"REG-UAP-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        // Режим — поле расширения чужого справочника: пишется мешком и ЧИСЛОМ,
        // потому что Db.UpdateAsync идёт в базу мимо разбора имён перечисления.
        await Db.UpdateAsync("LegalEntity", legalEntity.MetaId,
            new Dictionary<string, object?>
            {
                ["UaTaxRegime"] = (int)regime,
                ["Country"] = country.MetaId,
                ["Currency"] = currency.MetaId,
            });

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
        store.Name = "WH";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Zone";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"RCV-{Db.NewId():N}"[..12];
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

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "Piece";
        unit.Code = $"P{Db.NewId():N}"[..8];
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G{Db.NewId():N}"[..8];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsRawMaterial = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Vendor Ltd";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        if (configureTax) await TaxCircuitAsync();

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            LegalEntity = legalEntity.MetaId,
            Supplier = supplier.MetaId,
        };
    }

    private async Task TaxCircuitAsync()
    {
        var from = new DateTime(2020, 1, 1);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"AU-{Db.NewId():N}"[..10];
        authority.Name = "DPS";
        authority.CountryCode = "UA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"JU-{Db.NewId():N}"[..10];
        jurisdiction.Name = "Ukraine";
        jurisdiction.CountryCode = "UA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"VT-{Db.NewId():N}"[..10];
        tax.Name = "Ukraine VAT";
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"STD-{Db.NewId():N}"[..10];
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var rate = DictionaryManager.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.TaxCategory = category.MetaId;
        rate.Code = $"R-{Db.NewId():N}"[..10];
        rate.Rate = 0.20m;
        rate.EffectiveFrom = from;
        rate = await DictionaryManager.SaveRecordAsync(rate);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"IN-{Db.NewId():N}"[..10];
        code.Name = "Standard 20%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        code = await DictionaryManager.SaveRecordAsync(code);

        foreach (var name in new[] { "INPUT", "OUTPUT" })
        {
            if ((await DictionaryManager.GetRecordsAsync<TaxDirection>($"Code = '{name}'", take: 1)).Count == 0)
            {
                var direction = DictionaryManager.NewRecord<TaxDirection>();
                direction.Code = name;
                direction.Name = name;
                await DictionaryManager.SaveRecordAsync(direction);
            }
        }

        var rows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    // ── замеры ───────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> Key(Setup s)
        => new() { ["LegalEntity"] = s.LegalEntity, ["Supplier"] = s.Supplier };

    private static Task<decimal> CreditAsync(Setup s)
        => TotalsManager.GetBalanceAsync("UaVatCredit", "Amount", Key(s));

    private static Task<decimal> CreditedBaseAsync(Setup s)
        => TotalsManager.GetBalanceAsync("UaPurchaseFirstEvent", "Credited", Key(s));

    private static Task<decimal> ReceivedBaseAsync(Setup s)
        => TotalsManager.GetBalanceAsync("UaPurchaseFirstEvent", "Received", Key(s));

    private async Task<PurchaseOrder> ReceiveAsync(Setup s, decimal qty, decimal price)
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
        return (await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId))!;
    }

    private async Task PayAsync(Setup s, decimal amount)
    {
        var pay = await DocumentManager.NewDocumentAsync<VendorPayment>();
        pay.LegalEntity = s.LegalEntity;
        pay.Lines.Add(new VendorPaymentLinesTablePartRow { Supplier = s.Supplier, Amount = amount });
        await DocumentManager.SaveDocumentAsync(pay);
        pay.Subtype = VendorPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(pay);
    }

    // ── правило ──────────────────────────────────────────────────────────────

    [IntegrationTest("Приход без оплаты даёт налоговый кредит сразу")]
    public async Task ReceiptAloneCredits()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 10m);

        Assert.IsTrue(await CreditAsync(s) == 20m,
            "кредит 20 при базе 100, факт {0}", await CreditAsync(s));
    }

    [IntegrationTest("Предоплата поставщику даёт кредит ДО поставки, поставка не даёт второй раз")]
    public async Task PrepaymentIsTheFirstEventOnInput()
    {
        var s = await SetupAsync();

        // Деньги уходят С налогом: 120 при ставке 20% закрывают базу 100.
        await PayAsync(s, 120m);
        Assert.IsTrue(await CreditAsync(s) == 20m,
            "предоплата 120 обязана дать кредит 20 сразу, факт {0}", await CreditAsync(s));

        // Поставка в пределах аванса — НЕ первое событие, кредит уже взят.
        await ReceiveAsync(s, 10m, 10m);
        Assert.IsTrue(await CreditAsync(s) == 20m,
            "поставка, закрытая авансом, не зачитывает повторно: ждали 20, факт {0}",
            await CreditAsync(s));
    }

    [IntegrationTest("Оплата после поставки не зачитывает кредит повторно")]
    public async Task PaymentAfterReceiptDoesNotCreditAgain()
    {
        var s = await SetupAsync();

        await ReceiveAsync(s, 10m, 10m);
        Assert.IsTrue(await CreditAsync(s) == 20m, "поставка дала 20, факт {0}", await CreditAsync(s));

        await PayAsync(s, 120m);
        Assert.IsTrue(await CreditAsync(s) == 20m,
            "оплата после поставки — вторая подія, кредит не повторяется: ждали 20, факт {0}",
            await CreditAsync(s));
    }

    [IntegrationTest("Частичный аванс зачитывается дважды и в сумме даёт полный кредит")]
    public async Task PartialPrepaymentCreditsInTwoSteps()
    {
        var s = await SetupAsync();

        // 60 брутто = база 50 при 20%.
        await PayAsync(s, 60m);
        Assert.IsTrue(await CreditAsync(s) == 10m,
            "частичный аванс 60 даёт кредит 10, факт {0}", await CreditAsync(s));

        // Поставка на базу 100 поднимает max до 100 — доначисляется только разница.
        await ReceiveAsync(s, 10m, 10m);
        Assert.IsTrue(await CreditAsync(s) == 20m,
            "после поставки суммарный кредит 20, а не 30: факт {0}", await CreditAsync(s));
    }

    [IntegrationTest("Спрощенець без ПДВ налогового кредита не получает вовсе")]
    public async Task NonVatRegimeGetsNoCredit()
    {
        var s = await SetupAsync(UaTaxRegime.SimplifiedNoVat);

        await ReceiveAsync(s, 10m, 10m);
        await PayAsync(s, 120m);

        // СНАЧАЛА доказываем, что события ВООБЩЕ записались: без этой строки
        // «кредит 0» одинаково выглядит и при верном правиле, и при пакете,
        // который не работает совсем.
        Assert.IsTrue(await ReceivedBaseAsync(s) == 100m,
            "поставка обязана попасть в регистр даже у неплательщика, факт {0}",
            await ReceivedBaseAsync(s));

        Assert.IsTrue(await CreditAsync(s) == 0m,
            "неплательщик ПДВ входящий налог не зачитывает, факт {0}", await CreditAsync(s));

        // И база НЕ помечается зачтённой: иначе переход на общую систему
        // обнаружил бы «уже зачтённые» 100, которых никогда не зачитывали, и
        // первая же поставка после перехода не дала бы кредита.
        Assert.IsTrue(await CreditedBaseAsync(s) == 0m,
            "Credited обязан остаться нулевым, факт {0}", await CreditedBaseAsync(s));
    }

    [IntegrationTest("Без налогового контура приход не пишет кредит")]
    public async Task NoTaxCircuitCreditsNothing()
    {
        var s = await SetupAsync(configureTax: false);
        await ReceiveAsync(s, 10m, 10m);

        Assert.IsTrue(await ReceivedBaseAsync(s) == 100m,
            "поставка обязана попасть в регистр и без контура, факт {0}",
            await ReceivedBaseAsync(s));

        Assert.IsTrue(await CreditAsync(s) == 0m,
            "без контура кредита нет, факт {0}", await CreditAsync(s));
    }
}
