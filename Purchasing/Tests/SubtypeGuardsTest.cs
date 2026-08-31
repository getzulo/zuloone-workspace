using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Обязательно: тестовым скриптам этот namespace НЕ выдаётся глобальным using —
// без него генерированные классы (Currency, PurchaseOrder…) не находятся.
using ZuloOne.Runtime.Generated;

// Защита от дурака вокруг подтипов: карта разрешённых переходов и подтип,
// замораживающий данные документа.
//
// Карта переходов у PurchaseOrder задана как Draft → только Ordered: прыгнуть из
// черновика сразу в приход нельзя, сначала надо заказать. Пустая карта у
// остальных подтипов ничего не ограничивает — так старые модели продолжают
// работать без изменений.
//
// Всё идёт через менеджеры: справочники — IDictionaryManager, документ —
// IDocumentManager (переход подтипа = присвоение плюс сохранение), строки
// табличной части — ITablePartManager.
public class SubtypeGuardsTest : IntegrationTestScriptBase
{
    private static readonly Guid PurchaseOrderType = Guid.Parse("6935af7d-5f73-45d5-ad4c-d4a21dbe0b67");

    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITablePartManager TablePartManager => GetService<ITablePartManager>();

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
        legalEntity.RegistrationNumber = "REG-GRD-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"WH-{Db.NewId():N}"[..12];
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

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PCS-{Db.NewId():N}"[..12];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"RAW-{Db.NewId():N}"[..12];
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

    // Подтип не передаём: документ обязан стартовать в НАЧАЛЬНОМ подтипе типа,
    // и именно из него карта переходов начинает что-то значить.
    private async Task<PurchaseOrder> DraftOrderAsync(Setup s)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 5m, UnitPrice = 3m });
        await DocumentManager.SaveDocumentAsync(order);

        Assert.IsTrue(order.Subtype == PurchaseOrder.Subtypes.Draft,
            "новый заказ стартует в начальном подтипе Draft, факт {0}", order.Subtype);
        return order;
    }

    private static async Task<string?> StoredSubtypeAsync(Guid orderId)
        => (await DocumentManager.GetDocumentAsync<PurchaseOrder>(orderId))?.Subtype;

    [IntegrationTest("Переход по карте разрешён: Draft → Ordered")]
    public async Task AllowedTransitionPasses()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);

        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);

        Assert.IsTrue(await StoredSubtypeAsync(order.MetaId) == "Ordered",
            "документ должен оказаться в Ordered, факт {0}", await StoredSubtypeAsync(order.MetaId));
    }

    [IntegrationTest("Переход вне карты отклоняется с указанием допустимых")]
    public async Task DisallowedTransitionRejected()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);

        var reason = "";
        try
        {
            order.Subtype = PurchaseOrder.Subtypes.Received;
            await DocumentManager.SaveDocumentAsync(order);
        }
        catch (Exception ex) { reason = ex.Message; }

        Assert.IsTrue(reason.Length > 0, "прыжок Draft → Received должен быть отклонён картой переходов");
        // Причина обязана называть допустимые цели — иначе тест зеленел бы от
        // любой поломки внутри проведения.
        Assert.IsTrue(reason.Contains("не разрешён"), "отказ должен быть от карты переходов, факт: {0}", reason);
        Assert.IsTrue(reason.Contains("Ordered"), "отказ называет допустимую цель Ordered, факт: {0}", reason);

        // Читать БД после отказа обычно нельзя (отказ изнутри проведения приговаривает
        // транзакцию кейса), но карта переходов проверяется ДО того, как движок
        // откроет свой TransactionScope: сюда не дошло ни одной записи, поэтому
        // объемлющая транзакция цела и «документ не сдвинулся» проверяемо.
        Assert.IsTrue(await StoredSubtypeAsync(order.MetaId) == "Draft",
            "отклонённый переход не двигает документ, факт {0}", await StoredSubtypeAsync(order.MetaId));
    }

    [IntegrationTest("Пустая карта ничего не ограничивает")]
    public async Task EmptyMapAllowsEverything()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);

        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);

        // У Ordered карта не задана — значит из него можно куда угодно.
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);

        Assert.IsTrue(await StoredSubtypeAsync(order.MetaId) == "Received",
            "из подтипа без карты переход свободен, факт {0}", await StoredSubtypeAsync(order.MetaId));
    }

    [IntegrationTest("В запертом подтипе строку изменить нельзя")]
    public async Task LockedSubtypeRejectsLineEdit()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);

        var rows = await TablePartManager.GetRowsAsync(PurchaseOrderType, "Lines", order.MetaId);
        Assert.IsTrue(rows.Count == 1, "строка должна быть одна, факт {0}", rows.Count);

        // Отказ приходит НЕ исключением: ITablePartManager проверяет заморозку
        // до записи и сообщает о ней флагом (переписывать строки наполовину,
        // а потом упереться в гард IDataService — хуже). Правило то же самое.
        rows[0]["Quantity"] = 999m;
        var result = await TablePartManager.ReplaceRowsAsync(PurchaseOrderType, "Lines", order.MetaId, rows);
        Assert.IsTrue(result.SkippedLocked,
            "правка строки в подтипе Received должна быть отклонена, факт {0}", result);
        Assert.IsTrue(result.Inserted == 0 && result.Updated == 0 && result.Deleted == 0,
            "отклонённая правка не трогает ни одной строки, факт {0}", result);

        var after = await TablePartManager.GetRowsAsync(PurchaseOrderType, "Lines", order.MetaId);
        Assert.IsTrue(Convert.ToDecimal(after[0]["Quantity"]) == 5m,
            "количество не изменилось, факт {0}", after[0]["Quantity"]);
    }

    [IntegrationTest("В незапертом подтипе строка правится свободно")]
    public async Task UnlockedSubtypeAllowsLineEdit()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);

        order.Lines[0].Quantity = 7m;
        await DocumentManager.SaveDocumentAsync(order);

        var stored = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsNotNull(stored, "заказ читается после правки");
        Assert.IsTrue(stored!.Lines.Count == 1, "строка по-прежнему одна, факт {0}", stored.Lines.Count);
        Assert.IsTrue(stored.Lines[0].Quantity == 7m, "в Draft правка проходит, факт {0}", stored.Lines[0].Quantity);
    }

    [IntegrationTest("Из запертого подтипа документ всё ещё можно вывести")]
    public async Task LockedSubtypeStillAllowsTransitionOut()
    {
        var s = await SetupAsync();
        var order = await DraftOrderAsync(s);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);

        // Блокировка данных не должна запирать сам документ: иначе из Received
        // не было бы выхода вообще.
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);

        Assert.IsTrue(await StoredSubtypeAsync(order.MetaId) == "Ordered",
            "выход из запертого подтипа разрешён, факт {0}", await StoredSubtypeAsync(order.MetaId));
    }
}
