using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (Item, ProductionOrder, ProductionOrderComponentsTablePartRow…).
// Тест-скрипты НЕ получают это пространство имён глобальным using — без него
// генерённые классы просто не находятся.
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Производственный контур целиком через менеджеры: справочники —
// NewRecord<T> → поля → SaveRecordAsync, заказ — NewDocumentAsync<T> → строки →
// SaveDocumentAsync, а выпуск — ПРИСВОЕНИЕ подтипа плюс сохранение
// (MIQS doc.SubtypeID = …; SaveDocument(doc)). Остатки читаются
// ITotalsManager'ом — тем же, что зовёт обработчик события ProductionOrder.
public class ProductionFlowTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    // Item требует ItemGroup и UnitOfMeasure — заводим их и фабрику номенклатуры.
    private async Task<Func<string, Task<Item>>> ItemFactoryAsync()
    {
        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "PROD";
        group.Name = "Производство";
        group = await DictionaryManager.SaveRecordAsync(group);

        return async name =>
        {
            var item = DictionaryManager.NewRecord<Item>();
            item.Name = name;
            item.ItemGroup = group.MetaId;
            item.UnitOfMeasure = uom.MetaId;
            return await DictionaryManager.SaveRecordAsync(item);
        };
    }

    /// <summary>Остаток Stock по паре (ячейка, товар): у регистра ровно эти два
    /// физических измерения, так что срез задаётся полным ключом.</summary>
    private static Task<decimal> OnHandAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    [IntegrationTest("Сервис разворачивает BOM в потребность компонентов")]
    public async Task BomServiceExpands()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-А");
        var c1 = await newItem("Компонент-1");
        var c2 = await newItem("Компонент-2");

        var bom = DictionaryManager.NewRecord<BillOfMaterials>();
        bom.Name = "BOM-А";
        bom.Product = product.MetaId;
        bom.OutputQty = 1m;
        bom = await DictionaryManager.SaveRecordAsync(bom);

        await NewBomComponentAsync(bom.MetaId, c1.MetaId, 2m);
        await NewBomComponentAsync(bom.MetaId, c2.MetaId, 3m);

        var need = await GetService<IBomService>().ExpandByProductAsync(product.MetaId, 5m);
        Assert.IsTrue(need.Count == 2, "две позиции потребности, факт {0}", need.Count);
        Assert.IsTrue(need[c1.MetaId] == 10m, "Компонент-1 = 2×5 = 10, факт {0}", need.TryGetValue(c1.MetaId, out var v1) ? v1 : -1m);
        Assert.IsTrue(need[c2.MetaId] == 15m, "Компонент-2 = 3×5 = 15, факт {0}", need.TryGetValue(c2.MetaId, out var v2) ? v2 : -1m);
    }

    private static async Task NewBomComponentAsync(Guid bom, Guid component, decimal qtyPer)
    {
        var row = DictionaryManager.NewRecord<BomComponent>();
        row.Bom = bom;
        row.Component = component;
        row.QtyPer = qtyPer;
        await DictionaryManager.SaveRecordAsync(row);
    }

    [IntegrationTest("Выпуск списывает компоненты и приходует изделие")]
    public async Task FinishConsumesAndProduces()
    {
        var loc = Db.NewId();
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-Б");
        var comp = await newItem("Компонент-Б");

        // Заводим 20 ед. компонента на ячейку.
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

        var order = await NewOrderAsync(product.MetaId, 5m, loc, comp.MetaId, 10m);

        // Состояние ДО перехода: черновик заказа ничего не двигает — компонент
        // ещё 20, изделия ещё нет. Без этого утверждения проверки после перехода
        // проходят и тогда, когда заказ разнёсся сам при сохранении.
        Assert.IsTrue(await OnHandAsync(loc, comp.MetaId) == 20m, "черновик не должен списывать компонент");
        Assert.IsTrue(await OnHandAsync(loc, product.MetaId) == 0m, "черновик не должен приходовать изделие");

        // Номер выдаёт последовательность на ВСТАВКЕ и обратно на сущность не
        // пишет — значит повторное сохранение отправляет в базу пустой Number
        // экземпляра. Номер обязан пережить смену подтипа, иначе это тихая потеря
        // данных: документ теряет своё единственное человекочитаемое имя.
        var numberBefore = (await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId))!.Number;
        Assert.IsTrue(!string.IsNullOrWhiteSpace(numberBefore),
            "последовательность обязана выдать номер при создании, факт «{0}»", numberBefore ?? "");

        // Выпуск — переход Draft → Finished, то есть присваивание плюс save.
        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        var numberAfter = (await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId))!.Number;
        Assert.IsTrue(numberAfter == numberBefore,
            "номер обязан пережить смену подтипа: было «{0}», стало «{1}»", numberBefore ?? "", numberAfter ?? "");

        // Stock — односторонний регистр с физическими измерениями: остаток по (ячейка, товар)
        // Компонент: 20 заведено − 10 списано = 10; изделие: выпуск +5.
        var onHandComp = await OnHandAsync(loc, comp.MetaId);
        var onHandProduct = await OnHandAsync(loc, product.MetaId);
        Assert.IsTrue(onHandComp == 10m, "компонент 20 − 10 = 10, факт {0}", onHandComp);
        Assert.IsTrue(onHandProduct == 5m, "изделие выпущено +5, факт {0}", onHandProduct);
    }

    [IntegrationTest("Нехватка компонента отклоняет выпуск (проверка в событии)")]
    public async Task ShortageRejected()
    {
        var loc = Db.NewId();
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-В");
        var comp = await newItem("Компонент-В");

        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 5m });

        var order = await NewOrderAsync(product.MetaId, 3m, loc, comp.MetaId, 10m);

        // Наличие ДО отказа: на ячейке ровно 5, то есть отказ будет именно про
        // нехватку, а не про пустой регистр.
        Assert.IsTrue(await OnHandAsync(loc, comp.MetaId) == 5m, "перед выпуском на ячейке 5 ед. компонента");

        // После пойманного отказа к БД НЕ обращаемся: событие отказывает
        // исключением, а исключение рушит окружающую транзакцию раннера —
        // следующий запрос упал бы вместо самой проверки.
        var rejected = false;
        try
        {
            order.Subtype = ProductionOrder.Subtypes.Finished;
            await DocumentManager.SaveDocumentAsync(order);
        }
        catch (Exception) { rejected = true; }
        Assert.IsTrue(rejected, "выпуск при нехватке компонента (нужно 10, есть 5) должен быть отклонён");
    }

    [IntegrationTest("Выпуск без компонентов отклоняется событием")]
    public async Task EmptyComponentsRejected()
    {
        var loc = Db.NewId();
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-Г");

        // Заказ БЕЗ строк компонентов сохраняется как черновик спокойно —
        // черновику позволено быть неполным; отказывать обязан именно выпуск.
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product.MetaId;
        order.Quantity = 1m;
        order.Location = loc;
        await DocumentManager.SaveDocumentAsync(order);

        var stored = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
        Assert.IsTrue(stored!.Components.Count == 0, "у черновика нет строк компонентов, факт {0}", stored.Components.Count);

        var rejected = false;
        try
        {
            order.Subtype = ProductionOrder.Subtypes.Finished;
            await DocumentManager.SaveDocumentAsync(order);
        }
        catch (Exception) { rejected = true; }
        Assert.IsTrue(rejected, "выпуск без компонентов должен быть отклонён событием");
    }

    /// <summary>Черновик заказа с одной строкой компонента.</summary>
    private static async Task<ProductionOrder> NewOrderAsync(Guid product, decimal quantity, Guid location, Guid component, decimal qtyRequired)
    {
        // Подтип не передаём: NewDocumentAsync обязан взять НАЧАЛЬНЫЙ подтип типа
        // документа (Draft) сам.
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product;
        order.Quantity = quantity;
        order.Location = location;
        order.Components.Add(new ProductionOrderComponentsTablePartRow { Component = component, QtyRequired = qtyRequired });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }
}
