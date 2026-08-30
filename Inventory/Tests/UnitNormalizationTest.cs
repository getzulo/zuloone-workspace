using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (UnitOfMeasure, UnitConversion, Item, StockAdjustment,
// StockAdjustmentLinesTablePartRow…). Тест-скрипты НЕ получают это пространство
// имён глобальным using'ом — без строки ниже каждый из них CS0246.
using ZuloOne.Runtime.Generated;

// ЕДИНИЦЫ ИЗМЕРЕНИЯ ОТ НАСТРОЙКИ ДО ОСТАТКА — сквозное покрытие механизма
// пересчёта количества, которого до сих пор не существовало ни одного.
//
// Что именно проверяется. На поле Quantity каждой складской табличной части
// объявлена тройка UnitFieldName / BaseUnitPath / NormalizedFieldName; платформа
// (QuantityNormalizer) читает её на единственной воронке записи строки и кладёт
// пересчитанное значение в BaseQuantity, а проводки уносят в регистр именно его.
// Всё это уже было построено — не было ДАННЫХ: на стенде лежала ровно одна
// единица («Piece») и НОЛЬ правил пересчёта, так что «ящик из 12 штук» нельзя
// было выразить в принципе, и ни один тест по этой дороге не ходил.
//
// Правила пересчёта создаются здесь же, внутри отката раннера, и это не деталь
// оформления: платформа читает их через request.Reader — соединение, которое она
// уже держит, ВНУТРИ транзакции вызывающего. Конвертер, открывший бы своё
// соединение, не увидел бы этих правил (а на SQL Server ещё и заблокировался бы
// на строках, залоченных текущей транзакцией). Тест ходит ровно этой дорогой,
// поэтому он же и охраняет её.
public class UnitNormalizationTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Item;
        /// <summary>Базовая единица товара — в ней ведётся регистр остатков.</summary>
        public Guid Piece;
        /// <summary>Единица ввода: 1 ящик = 12 штук.</summary>
        public Guid Box;
    }

    private async Task<Guid> NewUnitAsync(string name, string code, int decimals)
    {
        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = name;
        // Код единицы уникален в пределах прогона: справочник общий, а соседние
        // прогоны других агентов идут по тому же стенду.
        unit.Code = $"{code}-{Db.NewId():N}"[..12];
        unit.DecimalPlaces = decimals;
        return (await DictionaryManager.SaveRecordAsync(unit)).MetaId;
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
        legalEntity.RegistrationNumber = "REG-UOM-1";
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
        cellType.Code = $"STG-{Db.NewId():N}"[..12];
        cellType.Name = "Storage";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "A-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        // «Штука / ящик из 12 / …» — ровно та настройка, которую пользователь
        // хотел уметь задавать: две единицы и ОДНО правило между ними.
        var piece = await NewUnitAsync("Piece", "PCS", 0);
        var box = await NewUnitAsync("Box", "BOX", 0);

        var rule = DictionaryManager.NewRecord<UnitConversion>();
        rule.FromUnit = box;
        rule.ToUnit = piece;
        rule.Factor = 12m;                 // 1 ящик = 12 штук
        await DictionaryManager.SaveRecordAsync(rule);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"MERCH-{Db.NewId():N}"[..12];
        group.Name = "Merchandise";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bottled water";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = piece;        // БАЗОВАЯ единица товара — цель пересчёта
        item = await DictionaryManager.SaveRecordAsync(item);

        return new Setup { Cell = cell.MetaId, Item = item.MetaId, Piece = piece, Box = box };
    }

    /// <summary>Черновик корректировки на одну строку: количество в заданной
    /// единице. Guid.Empty в Unit значит «единица не указана».</summary>
    private static async Task<StockAdjustment> NewAdjustmentAsync(Setup s, decimal quantity, Guid unit)
    {
        var doc = await DocumentManager.NewDocumentAsync<StockAdjustment>();
        doc.Cell = s.Cell;
        doc.Lines.Add(new StockAdjustmentLinesTablePartRow { Item = s.Item, Quantity = quantity, Unit = unit });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }

    /// <summary>BaseQuantity ЕДИНСТВЕННОЙ строки, перечитанной ИЗ БАЗЫ. Читать
    /// её из объекта в руках нельзя: пересчёт делает платформа при записи строки,
    /// а не сеттер сущности, — экземпляр в памяти о нём не знает.</summary>
    private static async Task<decimal> StoredBaseQuantityAsync(Guid documentId)
    {
        var stored = await DocumentManager.GetDocumentAsync<StockAdjustment>(documentId);
        Assert.IsNotNull(stored, "документ должен читаться из базы");
        Assert.IsTrue(stored!.Lines.Count == 1, "ожидалась ровно одна строка, а не {0}", stored.Lines.Count);
        return stored.Lines[0].BaseQuantity;
    }

    private static Task<decimal> OnHandAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = s.Cell, ["Item"] = s.Item });

    [IntegrationTest("Ящики пересчитываются в штуки и в регистр уходят штуки")]
    public async Task BoxesNormalizeToPieces()
    {
        var s = await SetupAsync();

        // 5 ЯЩИКОВ товара, базовая единица которого — ШТУКА.
        var doc = await NewAdjustmentAsync(s, quantity: 5m, unit: s.Box);

        // Введённое количество НЕ трогается — это и делает пересохранение
        // идемпотентным; результат лежит отдельной колонкой.
        var stored = await DocumentManager.GetDocumentAsync<StockAdjustment>(doc.MetaId);
        Assert.IsTrue(stored!.Lines[0].Quantity == 5m,
            "введённое количество должно остаться 5 ящиков, а стало {0}", stored.Lines[0].Quantity);
        Assert.IsTrue(stored.Lines[0].BaseQuantity == 60m,
            "5 ящиков по 12 = 60 штук в BaseQuantity, а не {0}", stored.Lines[0].BaseQuantity);

        // Состояние ДО проведения: черновик остатка не двигает. Без этой проверки
        // утверждение ниже проходит и тогда, когда документ провёлся сам на save.
        Assert.IsTrue(await OnHandAsync(s) == 0m, "черновик остатка не двигает");

        doc.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);

        // Главное утверждение всей фичи: в регистре ШТУКИ, а не ящики.
        var onHand = await OnHandAsync(s);
        Assert.IsTrue(onHand == 60m,
            "остаток должен быть 60 штук (5 ящиков × 12), а не {0} — в регистр ушли ящики", onHand);
    }

    [IntegrationTest("Пересохранение не пересчитывает уже пересчитанное")]
    public async Task ResaveIsIdempotent()
    {
        var s = await SetupAsync();
        var doc = await NewAdjustmentAsync(s, quantity: 5m, unit: s.Box);
        Assert.IsTrue(await StoredBaseQuantityAsync(doc.MetaId) == 60m, "первая запись даёт 60");

        // Второе сохранение того же черновика. Если бы результат ложился в само
        // поле количества, здесь получилось бы 720 (60 × 12) — ровно поэтому
        // приёмник обязан быть ОТДЕЛЬНОЙ колонкой.
        await DocumentManager.SaveDocumentAsync(doc);
        var again = await StoredBaseQuantityAsync(doc.MetaId);
        Assert.IsTrue(again == 60m, "пересохранение обязано оставить 60, а дало {0}", again);

        // И третий раз — на случай, если идемпотентность держится только один шаг.
        await DocumentManager.SaveDocumentAsync(doc);
        Assert.IsTrue(await StoredBaseQuantityAsync(doc.MetaId) == 60m, "третье сохранение тоже 60");
    }

    [IntegrationTest("Строка без единицы пересчёт не запускает — введённое и есть базовое")]
    public async Task BlankUnitSkipsConversion()
    {
        var s = await SetupAsync();

        // Единица не указана: платформа такую строку намеренно пропускает (за
        // обязательность отвечает IsRequired, а не пересчёт), BaseQuantity
        // остаётся нулём, и проводка берёт введённое количество. Это ровно тот
        // случай, в котором работают ВСЕ существующие складские тесты — их
        // поведение фича менять не должна.
        var doc = await NewAdjustmentAsync(s, quantity: 7m, unit: Guid.Empty);
        Assert.IsTrue(await StoredBaseQuantityAsync(doc.MetaId) == 0m,
            "без единицы пересчёта нет, BaseQuantity остаётся нулём");

        doc.Subtype = StockAdjustment.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(doc);

        var onHand = await OnHandAsync(s);
        Assert.IsTrue(onHand == 7m, "введённое количество и есть базовое: ожидалось 7, а не {0}", onHand);
    }

    [IntegrationTest("Единица без правила пересчёта — запись отклонена, а не занулена")]
    public async Task MissingRuleIsRejected()
    {
        var s = await SetupAsync();

        // Кега: правила «кега → штука» нет. Пропустить такую строку молча значило
        // бы оставить НОЛЬ в колонке, которую проводка унесёт в регистр остатков, —
        // отклонённое сохранение восстановимо, тихая ошибка склада находится
        // через месяцы.
        var keg = await NewUnitAsync("Keg", "KEG", 0);

        var rejected = false;
        try
        {
            await NewAdjustmentAsync(s, quantity: 2m, unit: keg);
        }
        catch
        {
            rejected = true;
        }

        // После пойманного отказа к базе НЕ обращаемся: бросок обрекает
        // окружающую транзакцию раннера, и следующий запрос упал бы вместо самой
        // проверки. Утверждаем сам факт отказа.
        Assert.IsTrue(rejected, "строка в единице без правила пересчёта должна быть отклонена платформой");
    }
}
