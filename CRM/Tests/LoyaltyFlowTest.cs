using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Currency, Item, SalesInvoice, LoyaltyRedemption, LoyaltyTier…).
// Тестовым скриптам этот namespace НЕ приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// Покрытие CRM: выставление Sales-инвойса начисляет баллы лояльности
// (расширение чужой модели через tx-скрипт на подтипе SalesInvoice.Issued),
// списание уменьшает баланс, переспис отклоняется (allowNegativeBalance=false).
public class LoyaltyFlowTest : IntegrationTestScriptBase
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
        legalEntity.RegistrationNumber = "REG-CRM-1";
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
        cellType.Code = $"PICK-{Guid.NewGuid():N}"[..12];
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
        unit.Code = "PCS";
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "GOODS";
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Gadget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        return new Setup { Location = cell.MetaId, Item = item.MetaId, Customer = customer.MetaId };
    }

    // Customer — ФИЗИЧЕСКОЕ измерение регистра, поэтому баланс спрашивается по
    // конкретному клиенту: баллы — это его лицевой счёт, а не общий котёл.
    private static Task<decimal> PointsBalanceAsync(Guid customer)
        => TotalsManager.GetBalanceAsync("LoyaltyPoints", "Points",
            new Dictionary<string, object?> { ["Customer"] = customer });

    private static async Task SeedPointsAsync(Guid customer, decimal points)
        => await TotalsManager.PostMovementAsync("LoyaltyPoints", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Customer"] = customer },
            new Dictionary<string, decimal> { ["Points"] = points });

    private static async Task TierAsync(string name, decimal minPoints, decimal maxPerDoc)
    {
        var tier = DictionaryManager.NewRecord<LoyaltyTier>();
        tier.Name = name;
        tier.MinPoints = minPoints;
        tier.MaxRedemptionPerDocument = maxPerDoc;
        tier.DiscountPercent = 0m;
        await DictionaryManager.SaveRecordAsync(tier);
    }

    /// <summary>Черновик погашения: подтип не передаём — NewDocumentAsync обязан
    /// подставить НАЧАЛЬНЫЙ подтип типа (Draft).</summary>
    private static async Task<LoyaltyRedemption> NewRedemptionAsync(Guid customer, decimal points)
    {
        var redemption = await DocumentManager.NewDocumentAsync<LoyaltyRedemption>();
        redemption.Customer = customer;
        redemption.Points = points;
        await DocumentManager.SaveDocumentAsync(redemption);
        return redemption;
    }

    [IntegrationTest("Выставление счёта начисляет баллы лояльности (расширение Sales)")]
    public async Task IssueEarnsPoints()
    {
        var s = await SetupAsync();
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 3m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(invoice);

        // Черновик баллов не начисляет. Проверка ДО перехода обязательна:
        // SalesInvoice объявлен postOnSave, и без неё «15 баллов» ниже подтвердилось
        // бы даже если переход Draft → Issued не сделал ничего.
        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 0m,
            "черновик счёта не должен начислять баллы, факт {0}", await PointsBalanceAsync(s.Customer));

        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        // 3 × 5 = 15 баллов.
        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 15m, "начислено 15 баллов, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Списание уменьшает баланс баллов")]
    public async Task RedeemReducesPoints()
    {
        var customer = Db.NewId();
        await SeedPointsAsync(customer, 15m);

        var redemption = await NewRedemptionAsync(customer, 10m);
        Assert.IsTrue(await PointsBalanceAsync(customer) == 15m,
            "черновик погашения баланс не трогает, факт {0}", await PointsBalanceAsync(customer));

        redemption.Subtype = LoyaltyRedemption.Subtypes.Redeemed;
        await DocumentManager.SaveDocumentAsync(redemption);

        Assert.IsTrue(await PointsBalanceAsync(customer) == 5m, "остаток 15 − 10 = 5, факт {0}", await PointsBalanceAsync(customer));
    }

    [IntegrationTest("Списание сверх баланса отклоняется (allowNegativeBalance=false)")]
    public async Task OverRedeemRejected()
    {
        var customer = Db.NewId();
        await SeedPointsAsync(customer, 15m);

        var redemption = await NewRedemptionAsync(customer, 20m);

        // Причина проверяется по тексту, а не просто «что-то упало». Голый
        // catch (Exception) зеленел бы от ЛЮБОЙ поломки — несуществующего
        // клиента, опечатки в имени регистра, падения внутри события — и
        // подтверждал бы защиту баланса, которой могло не быть вовсе.
        var reason = string.Empty;
        try
        {
            redemption.Subtype = LoyaltyRedemption.Subtypes.Redeemed;
            await DocumentManager.SaveDocumentAsync(redemption);
        }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Length > 0, "списание 20 при балансе 15 должно быть отклонено");
        Assert.IsTrue(reason.Contains("15") && reason.Contains("20"),
            "отказ обязан назвать и остаток, и запрошенное, факт: {0}", reason);
    }

    [IntegrationTest("Уровень ограничивает списание за один документ")]
    public async Task TierCapsRedemption()
    {
        var customer = Db.NewId();
        await SeedPointsAsync(customer, 500m);

        // Клиент с балансом 500 попадает на Silver (порог 100), а не на Gold (1000):
        // берётся САМЫЙ ВЫСОКИЙ достигнутый уровень, значит лимит 50, не 500.
        await TierAsync("Bronze", 0m, 10m);
        await TierAsync("Silver", 100m, 50m);
        await TierAsync("Gold", 1000m, 500m);

        var redemption = await NewRedemptionAsync(customer, 200m);
        // Баланс фиксируется ДО попытки, а не только после: «не тронул» — это
        // утверждение о РАЗНИЦЕ, и без опорной точки оно недоказуемо.
        var before = await PointsBalanceAsync(customer);
        Assert.IsTrue(before == 500m, "перед попыткой на счету 500, факт {0}", before);

        var reason = "";
        try
        {
            redemption.Subtype = LoyaltyRedemption.Subtypes.Redeemed;
            await DocumentManager.SaveDocumentAsync(redemption);
        }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Length > 0, "200 баллов сверх лимита уровня Silver (50) — должно быть отклонено");
        // Причина важна: без этой проверки тест зеленел бы от любой поломки внутри события.
        Assert.IsTrue(reason.Contains("Silver"), "отказ должен ссылаться на лимит уровня Silver, а не на другую ошибку: {0}", reason);
        // Чтение ПОСЛЕ отказа здесь законно: этот охранник отказывает до того, как
        // открылся вложенный scope, поэтому объемлющая транзакция прогона цела.
        Assert.IsTrue(await PointsBalanceAsync(customer) == before,
            "отклонённое погашение не трогает баланс, факт {0}", await PointsBalanceAsync(customer));
    }

    [IntegrationTest("Списание в пределах лимита уровня проходит")]
    public async Task WithinTierCapRedeems()
    {
        var customer = Db.NewId();
        await SeedPointsAsync(customer, 500m);

        await TierAsync("Bronze", 0m, 10m);
        await TierAsync("Silver", 100m, 50m);

        var redemption = await NewRedemptionAsync(customer, 50m);
        redemption.Subtype = LoyaltyRedemption.Subtypes.Redeemed;
        await DocumentManager.SaveDocumentAsync(redemption);

        Assert.IsTrue(await PointsBalanceAsync(customer) == 450m,
            "ровно лимит уровня списывается: 500 − 50 = 450, факт {0}", await PointsBalanceAsync(customer));
    }

    /// <summary>Настройки модуля — singleton: правим запись, если она есть, иначе заводим.</summary>
    private static async Task ConfigureLoyaltyAsync(bool enabled, decimal pointsPerCurrencyUnit)
    {
        var rows = await DictionaryManager.GetRecordsAsync<CRMSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<CRMSettings>();
        settings.LoyaltyEnabled = enabled;
        settings.PointsPerCurrencyUnit = pointsPerCurrencyUnit;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    /// <summary>Счёт на 3 × 5 = 15 единиц выручки, проведённый по-настоящему.</summary>
    private async Task<Setup> IssueInvoiceAsync()
    {
        var s = await SetupAsync();
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 3m, UnitPrice = 5m });
        await DocumentManager.SaveDocumentAsync(invoice);

        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
        return s;
    }

    [IntegrationTest("Курс начисления берётся из настроек, а не зашит в код")]
    public async Task EarnRateComesFromSettings()
    {
        // Два балла за единицу валюты. Пока PointsPerCurrencyUnit не читался,
        // ответ был бы 15 при любом значении настройки.
        await ConfigureLoyaltyAsync(enabled: true, pointsPerCurrencyUnit: 2m);

        var s = await IssueInvoiceAsync();

        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 30m,
            "3 × 5 × курс 2 = 30 баллов, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Выключенная лояльность не начисляет баллы")]
    public async Task DisabledLoyaltyEarnsNothing()
    {
        // Рубильник обязан выключать начисление. Пока LoyaltyEnabled не читался,
        // баллы начислялись независимо от него.
        await ConfigureLoyaltyAsync(enabled: false, pointsPerCurrencyUnit: 1m);

        var s = await IssueInvoiceAsync();

        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 0m,
            "при выключенной лояльности баллы не начисляются, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Запись настроек без заданного курса не выключает лояльность")]
    public async Task SettingsWithoutRateKeepLoyaltyOn()
    {
        // ЛОВУШКА НЕОБЯЗАТЕЛЬНОГО BOOLEAN. LoyaltyEnabled не nullable: «не
        // заполнено» неотличимо от «выключено». Записи CRMSettings заводились до
        // того, как флаг начал читаться, поэтому у всех существующих он false.
        // Доверяй код флагу буквально — начисление молча пропало бы на каждом
        // стенде, где кто-то однажды сохранил форму настроек CRM.
        //
        // Здесь именно этот случай: запись есть, курс не задан, флаг false по
        // умолчанию. Модуль лояльности никто не настраивал — значит работаем как
        // раньше.
        await ConfigureLoyaltyAsync(enabled: false, pointsPerCurrencyUnit: 0m);

        var s = await IssueInvoiceAsync();

        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 15m,
            "ненастроенный модуль начисляет как раньше: 15, факт {0}", await PointsBalanceAsync(s.Customer));
    }

    [IntegrationTest("Без записи настроек начисление работает как раньше — 1:1")]
    public async Task NoSettingsKeepsLegacyRate()
    {
        // Совместимость: ненастроенный модуль не должен молча перестать
        // начислять баллы на стендах, где записи настроек никогда не заводили.
        var s = await IssueInvoiceAsync();

        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 15m,
            "без настроек 1 балл за единицу валюты: 15, факт {0}", await PointsBalanceAsync(s.Customer));
    }
}
