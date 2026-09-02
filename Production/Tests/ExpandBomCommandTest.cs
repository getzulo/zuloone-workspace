using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;
// The generated entity classes (Item, ProductionOrder, …TablePartRow). A test
// script does NOT get this namespace as a global using, so it must be named.
using ZuloOne.Runtime.Generated;

// Покрытие команды «Развернуть спецификацию»: на черновике она наполняет
// табличную часть Components потребностью из BOM, а на подтипе Finished
// платформа её не пускает (команда привязана только к Draft).
//
// Мастер-данные, заказ и его строки — типизированными сущностями через
// менеджеры. На Db остаются только выполнение команды и генерация id: менеджера
// семейства команд в платформе нет, а Db.FindCommandIdAsync/
// ExecuteDocumentCommandAsync зовут тот же CommandFamilyService.ExecuteAsync, что
// и /api/commands2/execute — вместе с проверкой IsEnabled и привязкой к подтипу,
// которую тест и проверяет.
public class ExpandBomCommandTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private async Task<Func<string, Task<Item>>> ItemFactoryAsync()
    {
        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "MAT";
        group.Name = "Materials";
        group = await DictionaryManager.SaveRecordAsync(group);

        var uomId = uom.MetaId;
        var groupId = group.MetaId;
        return async name =>
        {
            var item = DictionaryManager.NewRecord<Item>();
            item.Name = name;
            item.ItemGroup = groupId;
            item.UnitOfMeasure = uomId;
            return await DictionaryManager.SaveRecordAsync(item);
        };
    }

    private async Task<Guid> NewLocationAsync()
    {
        // Подразделение обязано принадлежать юрлицу (проверяется событием), а
        // юрлицо — стране и валюте: цепочку приходится сеять целиком.
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
        legalEntity.RegistrationNumber = "REG-BOM-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "PRD";
        divisionType.Name = "Production";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Цех";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        // Склад переименован: Warehouse → Store, WarehouseLocation → StoreCell
        // (metaId сохранены, поэтому ссылки живы), LocationType → StoreCellType.
        // Иерархия трёхуровневая: Store → StoreZone → StoreCell, ячейка знает
        // только свою зону и свои координаты (стеллаж/полка/линия/ячейка).
        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Склад цеха";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Зона цеха";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"PRD-{Db.NewId():N}"[..12];
        cellType.Name = "Production";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.StoreZone = zone.MetaId;
        cell.Type = cellType.MetaId;
        cell.Name = "P-01";
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);
        return cell.MetaId;
    }

    private async Task<Guid> NewBomAsync(string name, Guid product, decimal outputQty)
    {
        var bom = DictionaryManager.NewRecord<BillOfMaterials>();
        bom.Name = name;
        bom.Product = product;
        bom.OutputQty = outputQty;
        bom = await DictionaryManager.SaveRecordAsync(bom);
        return bom.MetaId;
    }

    private async Task AddComponentAsync(Guid bom, Guid component, decimal qtyPer, Guid unit = default)
    {
        var row = DictionaryManager.NewRecord<BomComponent>();
        row.Bom = bom;
        row.Component = component;
        row.QtyPer = qtyPer;
        row.Unit = unit;
        await DictionaryManager.SaveRecordAsync(row);
    }

    /// <summary>Строки заказа, перечитанные из базы: команда и событие пишут их мимо нашего экземпляра.</summary>
    private async Task<List<ProductionOrderComponentsTablePartRow>> ComponentsAsync(Guid orderId)
        => (await DocumentManager.GetDocumentAsync<ProductionOrder>(orderId))!.Components;

    [IntegrationTest("Команда разворачивает спецификацию в компоненты заказа")]
    public async Task CommandFillsComponents()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Бутерброд");
        var c1 = await newItem("Колбаса");
        var c2 = await newItem("Булка");
        var loc = await NewLocationAsync();

        var bom = await NewBomAsync("BOM-бутерброд", product.MetaId, 1m);
        await AddComponentAsync(bom, c1.MetaId, 2m);
        await AddComponentAsync(bom, c2.MetaId, 1m);

        // Заказ создаётся БЕЗ строк — их и должна проставить команда. Подтип не
        // передаём: документ заводится в НАЧАЛЬНОМ подтипе типа (Draft), а
        // команда привязана именно к нему.
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product.MetaId;
        order.Quantity = 10m;
        order.Location = loc;
        await DocumentManager.SaveDocumentAsync(order);

        // Пустой заказ ДО команды: без этой проверки утверждения ниже проходят и
        // тогда, когда строки проставила не команда, а что-то ещё.
        Assert.IsTrue((await ComponentsAsync(order.MetaId)).Count == 0,
            "до команды заказ должен быть без компонентов");

        var commandId = await Db.FindCommandIdAsync("document", "ExpandBom");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var rows = await ComponentsAsync(order.MetaId);
        Assert.IsTrue(rows.Count == 2, "развёрнуто 2 строки, факт {0}", rows.Count);

        decimal Qty(Item item) => rows
            .Where(r => r.Component == item.MetaId)
            .Select(r => r.QtyRequired).FirstOrDefault();

        Assert.IsTrue(Qty(c1) == 20m, "Колбаса: 2 × 10 = 20, факт {0}", Qty(c1));
        Assert.IsTrue(Qty(c2) == 10m, "Булка: 1 × 10 = 10, факт {0}", Qty(c2));
    }

    [IntegrationTest("Команда недоступна вне подтипа Draft")]
    public async Task CommandBoundToDraftOnly()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-Б");
        var comp = await newItem("Компонент-Б");
        var loc = await NewLocationAsync();

        var bom = await NewBomAsync("BOM-Б", product.MetaId, 1m);
        await AddComponentAsync(bom, comp.MetaId, 1m);

        // Заказ со строкой и остатком — чтобы перевод в Finished прошёл.
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product.MetaId;
        order.Quantity = 1m;
        order.Location = loc;
        order.Components.Add(new ProductionOrderComponentsTablePartRow { Component = comp.MetaId, QtyRequired = 1m });
        await DocumentManager.SaveDocumentAsync(order);

        // Движение вне цепочки документа — хозяина у него нет.
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 5m });

        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        var commandId = await Db.FindCommandIdAsync("document", "ExpandBom");
        var rejected = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsFalse(rejected.Success, "на подтипе Finished команда обязана быть отклонена привязкой");
    }

    [IntegrationTest("Рецепт нормируется на партию и переводится в складскую единицу")]
    public async Task BatchAndUnitAreHonoured()
    {
        // Сценарий-бутерброд: рецепт даёт 10 бутербродов и требует 20 ГРАММОВ
        // колбасы на партию, а колбаса хранится в КИЛОГРАММАХ. Правильный ответ на
        // заказ в 10 штук — 0.020 кг. Две ошибки, которые тест обязан ловить:
        // умножить на заказ вместо деления на выход (было бы 200) и посчитать
        // граммы килограммами (было бы 20).
        // Грамм и килограмм — одна ВЕЛИЧИНА (масса), выраженная через базовую
        // единицу: коэффициент, а не попарное правило. Поэтому «килограмм в грамм»
        // считается само, а «килограмм в метр» невыразим в принципе.
        var mass = DictionaryManager.NewRecord<UnitClass>();
        mass.Code = $"MASS-{Db.NewId():N}"[..12];
        mass.Name = "Mass";
        mass = await DictionaryManager.SaveRecordAsync(mass);

        var kg = DictionaryManager.NewRecord<UnitOfMeasure>();
        kg.Name = "Kilogram";
        kg.Code = "KG";
        kg.DecimalPlaces = 3;
        kg.UnitClass = mass.MetaId;
        kg.RatioToBase = 1000m;            // 1 кг = 1000 базовых (граммов)
        kg = await DictionaryManager.SaveRecordAsync(kg);

        var g = DictionaryManager.NewRecord<UnitOfMeasure>();
        g.Name = "Gram";
        g.Code = "G";
        g.DecimalPlaces = 0;
        g.UnitClass = mass.MetaId;
        g.RatioToBase = 1m;                // базовая единица массы
        g = await DictionaryManager.SaveRecordAsync(g);

        var pcs = DictionaryManager.NewRecord<UnitOfMeasure>();
        pcs.Name = "Piece";
        pcs.Code = "PCS";
        pcs.DecimalPlaces = 0;
        pcs = await DictionaryManager.SaveRecordAsync(pcs);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "MAT";
        group.Name = "Materials";
        group = await DictionaryManager.SaveRecordAsync(group);

        var product = DictionaryManager.NewRecord<Item>();
        product.Name = "Бутерброд";
        product.ItemGroup = group.MetaId;
        product.UnitOfMeasure = pcs.MetaId;
        product = await DictionaryManager.SaveRecordAsync(product);

        var sausage = DictionaryManager.NewRecord<Item>();
        sausage.Name = "Колбаса";
        sausage.ItemGroup = group.MetaId;
        sausage.UnitOfMeasure = kg.MetaId;
        sausage = await DictionaryManager.SaveRecordAsync(sausage);

        var bun = DictionaryManager.NewRecord<Item>();
        bun.Name = "Булка";
        bun.ItemGroup = group.MetaId;
        bun.UnitOfMeasure = pcs.MetaId;
        bun = await DictionaryManager.SaveRecordAsync(bun);

        var bom = await NewBomAsync("BOM-партия", product.MetaId, 10m);
        await AddComponentAsync(bom, sausage.MetaId, 20m, g.MetaId);
        await AddComponentAsync(bom, bun.MetaId, 10m, pcs.MetaId);

        var bomService = GetService<IBomService>();
        var need = await bomService.ExpandByProductAsync(product.MetaId, 10m);

        Assert.IsTrue(need[sausage.MetaId] == 0.020m,
            "20 г на партию из 10 при заказе 10 = 0.020 кг, факт {0}", need[sausage.MetaId]);
        Assert.IsTrue(need[bun.MetaId] == 10m,
            "булки уже в штуках: 10 на партию из 10 при заказе 10 = 10, факт {0}", need[bun.MetaId]);

        // Удвоенный заказ — удвоенная потребность: нормировка линейна.
        var doubled = await bomService.ExpandByProductAsync(product.MetaId, 20m);
        Assert.IsTrue(doubled[sausage.MetaId] == 0.040m,
            "на 20 бутербродов нужно 0.040 кг, факт {0}", doubled[sausage.MetaId]);
    }

    [IntegrationTest("Настройка AutoExpandBom разворачивает спецификацию сама")]
    public async Task AutoExpandFillsOnCreate()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Бутерброд");
        var c1 = await newItem("Колбаса");
        var loc = await NewLocationAsync();

        var bom = await NewBomAsync("BOM-авто", product.MetaId, 1m);
        await AddComponentAsync(bom, c1.MetaId, 3m);

        // Настройки модуля — одиночный справочник; без записи настройка выключена,
        // поэтому остальные тесты создают заказы без автоподстановки.
        var settings = DictionaryManager.NewRecord<ProductionSettings>();
        settings.AutoExpandBom = true;
        await DictionaryManager.SaveRecordAsync(settings);

        // Заказ создаётся ТОЙ ЖЕ типизированной дверью, что и везде: строки
        // проставит обработчик OnAfterSave, и пустая коллекция в памяти их больше
        // не затирает — на пути СОЗДАНИЯ табличная часть не переписывается.
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product.MetaId;
        order.Quantity = 4m;
        order.Location = loc;
        await DocumentManager.SaveDocumentAsync(order);

        // Команду НЕ вызываем: строки должно проставить событие создания. Это же и
        // проверка платформенного фикса — чтение второго справочника в after-insert.
        var rows = await ComponentsAsync(order.MetaId);
        Assert.IsTrue(rows.Count == 1, "автоподстановка должна дать 1 строку, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].QtyRequired == 12m,
            "3 на изделие × 4 = 12, факт {0}", rows[0].QtyRequired);
    }

    [IntegrationTest("Без записи настроек автоподстановка не срабатывает")]
    public async Task NoSettingsNoAutoExpand()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Бутерброд");
        var c1 = await newItem("Колбаса");
        var loc = await NewLocationAsync();

        var bom = await NewBomAsync("BOM-ручной", product.MetaId, 1m);
        await AddComponentAsync(bom, c1.MetaId, 3m);

        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product.MetaId;
        order.Quantity = 4m;
        order.Location = loc;
        await DocumentManager.SaveDocumentAsync(order);

        var rows = await ComponentsAsync(order.MetaId);
        Assert.IsTrue(rows.Count == 0, "без настройки строки не подставляются, факт {0}", rows.Count);
    }

    [IntegrationTest("Заказ в неосновной единице разворачивается по базовому количеству")]
    public async Task NonBaseUnitOrderExpandsByBaseQuantity()
    {
        // Разворот обязан идти по BaseQuantity, а не по введённому Quantity: рецепт
        // нормирован на складскую единицу изделия, а в шапке количество может быть в
        // любой (ящик = 12 штук). Заказ «2 ящика» — это 24 изделия, значит булок надо
        // 48, а не 4. Пока разворот брал сырое Quantity, потребность занижалась ровно
        // в boxFactor раз, а выпуск при этом оприходовался верно (ProductionOutputTx
        // уже считал по BaseQuantity) — расхождение было молчаливым.
        const decimal boxFactor = 12m;

        var pcs = DictionaryManager.NewRecord<UnitOfMeasure>();
        pcs.Name = "Piece";
        pcs.Code = "PCS";
        pcs.DecimalPlaces = 0;
        pcs = await DictionaryManager.SaveRecordAsync(pcs);

        var box = DictionaryManager.NewRecord<UnitOfMeasure>();
        box.Name = "Box";
        box.Code = "BOX";
        box.DecimalPlaces = 0;
        box = await DictionaryManager.SaveRecordAsync(box);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"MAT-{Db.NewId():N}"[..12];
        group.Name = "Materials";
        group = await DictionaryManager.SaveRecordAsync(group);

        var product = DictionaryManager.NewRecord<Item>();
        product.Name = "Бутерброд";
        product.ItemGroup = group.MetaId;
        product.UnitOfMeasure = pcs.MetaId;
        product = await DictionaryManager.SaveRecordAsync(product);

        var bun = DictionaryManager.NewRecord<Item>();
        bun.Name = "Булка";
        bun.ItemGroup = group.MetaId;
        bun.UnitOfMeasure = pcs.MetaId;
        bun = await DictionaryManager.SaveRecordAsync(bun);

        // Упаковка ИМЕННО этого изделия: 1 ящик = 12 штук.
        var pack = DictionaryManager.NewRecord<ItemUnit>();
        pack.Item = product.MetaId;
        pack.Unit = box.MetaId;
        pack.QtyInBaseUnit = boxFactor;
        await DictionaryManager.SaveRecordAsync(pack);

        var loc = await NewLocationAsync();
        var bom = await NewBomAsync("BOM-ящик", product.MetaId, 1m);
        await AddComponentAsync(bom, bun.MetaId, 2m, pcs.MetaId);

        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product.MetaId;
        order.Quantity = 2m;
        order.Unit = box.MetaId;
        order.Location = loc;
        await DocumentManager.SaveDocumentAsync(order);

        // Нормализатор обязан был посчитать базовое количество на записи — без этого
        // проверка ниже проверяла бы совсем не то, что заявлено.
        var stored = (await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId))!;
        Assert.IsTrue(stored.BaseQuantity == 2m * boxFactor,
            "2 ящика по {0} = {1} штук, факт {2}", boxFactor, 2m * boxFactor, stored.BaseQuantity);

        var commandId = await Db.FindCommandIdAsync("document", "ExpandBom");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var rows = await ComponentsAsync(order.MetaId);
        Assert.IsTrue(rows.Count == 1, "развёрнута 1 строка, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].QtyRequired == 48m,
            "2 булки × 24 изделия = 48 (по сырому Quantity вышло бы 4), факт {0}", rows[0].QtyRequired);
    }
}
