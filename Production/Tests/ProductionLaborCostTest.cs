using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные сущности (Item, ProductionOrder, …TablePartRow). Тестовый
// скрипт НЕ получает global usings — все пространства имён названы явно.
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// ЦЕХОВАЯ ОТМЕТКА ОПЕРАЦИЙ И ТРУД В СЕБЕСТОИМОСТИ.
//
// До этого среза операции маршрута были СНИМКОМ: RoutingService штамповал их на
// заказ, и больше их не читал никто — ни проведение, ни оценка. Отработанный цех
// нигде не отмечался, а выпуск стоил ровно списанные материалы.
//
// Главный инвариант, который ловят эти тесты, — РАЗНИЦА С МАТЕРИАЛАМИ.
// Материалы Costing ПЕРЕНОСИТ (InventoryValue value-neutral: −100 компонент,
// +100 изделие), а труд ДОБАВЛЯЕТ: запас дорожает на поглощённые часы, потому
// что расход на оплату труда начислен отдельно и эта сумма лишь переносится
// в запас. Тест на первый сценарий проверяет именно дельту InventoryValue: она
// обязана равняться стоимости труда, а не нулю.
public class ProductionLaborCostTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Кнопка отмечает операции по норме, повтор не двоит и не затирает ручной факт")]
    public async Task ConfirmFillsStandardTimeAndIsIdempotent()
    {
        var fixture = await FixtureAsync("Отметка");
        var order = await NewOrderAsync(fixture, qtyRequired: 10m,
            ops: new[] { (seq: 1, setup: 15, run: 45m), (seq: 2, setup: 0, run: 30m) });

        order.Subtype = ProductionOrder.Subtypes.Released;
        await DocumentManager.SaveDocumentAsync(order);

        var commandId = await Db.FindCommandIdAsync("document", "ConfirmOperations");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var after = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
        var first = after!.Operations.OrderBy(o => o.Sequence).ToList();
        Assert.IsTrue(first.Count == 2, "операций должно остаться две, факт {0}", first.Count);
        Assert.IsTrue((first[0].ActualMinutes ?? 0m) == 60m,
            "первая операция: наладка 15 + работа 45 = 60, факт {0}", first[0].ActualMinutes);
        Assert.IsTrue((first[1].ActualMinutes ?? 0m) == 30m,
            "вторая операция: 0 + 30 = 30, факт {0}", first[1].ActualMinutes);
        Assert.IsTrue(first.All(o => o.CompletedOn != null), "обе операции должны быть отмечены датой");

        // Цех поправил факт руками: первая операция шла дольше нормы.
        first[0].ActualMinutes = 90m;
        await DocumentManager.SaveDocumentAsync(after);

        var again = await Db.ExecuteDocumentCommandAsync(commandId, order.MetaId);
        Assert.IsTrue(again.Success, "повтор команды не должен падать: {0}", again.Message ?? "");

        var second = (await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId))!
            .Operations.OrderBy(o => o.Sequence).ToList();
        Assert.IsTrue(second.Count == 2, "повтор не плодит строки, факт {0}", second.Count);
        Assert.IsTrue((second[0].ActualMinutes ?? 0m) == 90m,
            "повтор НЕ возвращает норму поверх ручного факта, факт {0}", second[0].ActualMinutes);
    }

    [IntegrationTest("Отмеченные часы по ставке рабочего центра дорожают выпуск — и это НЕ перенос стоимости")]
    public async Task ConfirmedMinutesAbsorbWorkCenterRate()
    {
        var fixture = await FixtureAsync("СтавкаЦентра", centerRate: 120m);
        var order = await NewOrderAsync(fixture, qtyRequired: 10m,
            ops: new[] { (seq: 1, setup: 0, run: 0m) });

        var valueBefore = await InventoryValueTotalAsync("Value");

        // 90 минут = 1,5 часа × 120 = 180.
        Confirm(order, minutes: 90m, workCenter: fixture.WorkCenter, employee: Guid.Empty);
        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        // Материалы: 200/20 × 10 = 100. Плюс труд 180 = 280.
        var amount = await FifoAsync("Amount", fixture.Product);
        Assert.IsTrue(amount == 280m,
            "выпуск = материалы 100 + труд 180 = 280, факт {0}", amount);

        // ВОТ ГЛАВНОЕ ОТЛИЧИЕ ОТ МАТЕРИАЛОВ: перенос давал бы ноль, труд даёт +180.
        var valueDelta = await InventoryValueTotalAsync("Value") - valueBefore;
        Assert.IsTrue(valueDelta == 180m,
            "труд ДОБАВЛЯЕТ стоимость запаса (материалы её только переносят), дельта {0}", valueDelta);
    }

    [IntegrationTest("Ставка должности исполнителя перебивает ставку рабочего центра")]
    public async Task OperatorPositionRateBeatsWorkCenterRate()
    {
        var fixture = await FixtureAsync("СтавкаЧеловека", centerRate: 120m, positionRate: 300m);
        var order = await NewOrderAsync(fixture, qtyRequired: 10m,
            ops: new[] { (seq: 1, setup: 0, run: 0m) });

        // Те же 90 минут, но у исполнителя ставка 300: 1,5 × 300 = 450.
        Confirm(order, minutes: 90m, workCenter: fixture.WorkCenter, employee: fixture.Employee);
        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        var amount = await FifoAsync("Amount", fixture.Product);
        Assert.IsTrue(amount == 550m,
            "материалы 100 + труд по ставке ДОЛЖНОСТИ 450 = 550 (по центру вышло бы 280), факт {0}", amount);
    }

    [IntegrationTest("Неотмеченная операция стоит ноль: заказы без цеховой отметки не дорожают")]
    public async Task UnconfirmedOperationCostsNothing()
    {
        var fixture = await FixtureAsync("БезОтметки", centerRate: 120m);
        var order = await NewOrderAsync(fixture, qtyRequired: 10m,
            ops: new[] { (seq: 1, setup: 15, run: 45m) });

        // Норма на строке есть, отметки нет — труд не поглощается.
        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        var amount = await FifoAsync("Amount", fixture.Product);
        Assert.IsTrue(amount == 100m,
            "без отметки выпуск стоит только материалы 100, факт {0}", amount);
    }

    [IntegrationTest("Отрицательный факт минут отклоняется на проведении с именем операции")]
    public async Task NegativeActualMinutesIsRejected()
    {
        var fixture = await FixtureAsync("МинусМинуты", centerRate: 120m);
        var order = await NewOrderAsync(fixture, qtyRequired: 10m,
            ops: new[] { (seq: 7, setup: 0, run: 0m) });

        Confirm(order, minutes: -30m, workCenter: fixture.WorkCenter, employee: Guid.Empty);
        await DocumentManager.SaveDocumentAsync(order);

        var failed = false;
        var message = "";
        try
        {
            var reread = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
            reread!.Subtype = ProductionOrder.Subtypes.Finished;
            await DocumentManager.SaveDocumentAsync(reread);
        }
        catch (Exception ex)
        {
            failed = true;
            message = ex.Message;
        }

        Assert.IsTrue(failed, "отрицательный факт минут обязан отклонить проведение");
        Assert.IsTrue(message.Contains("отрицательный"),
            "в отказе должна быть причина, факт: {0}", message);
        // Именно НОМЕР операции, а не «одна из строк неверна»: иначе её не найти.
        Assert.IsTrue(message.Contains("7"),
            "в отказе должен быть номер операции, факт: {0}", message);
    }

    // ── фикстура ────────────────────────────────────────────────────────────

    private sealed class Fixture
    {
        public Guid Product;
        public Guid Component;
        public Guid Location;
        public Guid WorkCenter;
        public Guid Employee;
    }

    /// <summary>
    /// Изделие, компонент с партией 20 по 200, рабочий центр и (при ставке
    /// должности) сотрудник. Партия компонента заводится движением: этот тест
    /// про ТРУД, а материальная нога уже покрыта ProductionOutputCostTest.
    /// </summary>
    private async Task<Fixture> FixtureAsync(string tag, decimal centerRate = 0m, decimal positionRate = 0m)
    {
        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PC{Db.NewId():N}"[..8];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"LAB{Db.NewId():N}"[..10];
        group.Name = "Труд-себестоимость";
        group = await DictionaryManager.SaveRecordAsync(group);

        async Task<Guid> NewItemAsync(string name)
        {
            var item = DictionaryManager.NewRecord<Item>();
            item.Name = name;
            item.ItemGroup = group.MetaId;
            item.UnitOfMeasure = uom.MetaId;
            return (await DictionaryManager.SaveRecordAsync(item)).MetaId;
        }

        var f = new Fixture
        {
            Product = await NewItemAsync($"Изделие-{tag}"),
            Component = await NewItemAsync($"Компонент-{tag}"),
            Location = Db.NewId(),
        };

        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = f.Location, ["Item"] = f.Component },
            new Dictionary<string, decimal> { ["Qty"] = 20m });
        await TotalsManager.PostMovementAsync("ItemCostFifo", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Item"] = f.Component },
            new Dictionary<string, decimal> { ["Quantity"] = 20m, ["Amount"] = 200m });

        var center = DictionaryManager.NewRecord<WorkCenter>();
        center.Name = $"Участок-{tag}";
        center.HourlyRate = centerRate;
        f.WorkCenter = (await DictionaryManager.SaveRecordAsync(center)).MetaId;

        if (positionRate > 0m)
        {
            var position = DictionaryManager.NewRecord<Position>();
            position.Name = $"Оператор-{tag}";
            position.HourlyRate = positionRate;
            position = await DictionaryManager.SaveRecordAsync(position);

            var employee = DictionaryManager.NewRecord<Employee>();
            employee.Name = $"Исполнитель-{tag}";
            employee.Position = position.MetaId;
            employee.Division = await NewDivisionAsync(tag);
            employee.HireDate = DateTime.UtcNow.Date.AddYears(-1);
            f.Employee = (await DictionaryManager.SaveRecordAsync(employee)).MetaId;
        }

        return f;
    }

    /// <summary>
    /// Подразделение сотрудника обязательно, а оно принадлежит юрлицу, а то —
    /// стране и валюте: цепочку приходится сеять целиком (как в ExpandBomCommandTest).
    /// Не static: Db — член экземпляра базового класса.
    /// </summary>
    private async Task<Guid> NewDivisionAsync(string tag)
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = $"E{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = $"Завод-{tag}";
        legalEntity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"DT{Db.NewId():N}"[..10];
        divisionType.Name = $"Производственное-{tag}";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = $"Цех-{tag}";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        return (await DictionaryManager.SaveRecordAsync(division)).MetaId;
    }

    private static async Task<ProductionOrder> NewOrderAsync(
        Fixture f, decimal qtyRequired, (int seq, int setup, decimal run)[] ops)
    {
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = f.Product;
        order.Quantity = 5m;
        order.Location = f.Location;
        order.Components.Add(new ProductionOrderComponentsTablePartRow
        {
            Component = f.Component,
            QtyRequired = qtyRequired,
        });
        foreach (var (seq, setup, run) in ops)
        {
            order.Operations.Add(new ProductionOrderOperationsTablePartRow
            {
                Sequence = seq,
                Name = $"Операция {seq}",
                WorkCenter = f.WorkCenter,
                SetupMinutes = setup,
                RunMinutes = run,
            });
        }
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    private static void Confirm(ProductionOrder order, decimal minutes, Guid workCenter, Guid employee)
    {
        foreach (var op in order.Operations)
        {
            op.WorkCenter = workCenter;
            op.Employee = employee;
            op.ActualMinutes = minutes;
            op.CompletedOn = DateTime.UtcNow.Date;
        }
    }

    private static Task<decimal> FifoAsync(string resource, Guid item)
        => TotalsManager.GetBalanceAsync("ItemCostFifo", resource,
            new Dictionary<string, object?> { ["Item"] = item });

    /// <summary>InventoryValue разрезан ДИНАМИЧЕСКОЙ аналитикой Item — точечного
    /// среза нет, поэтому сравнивается сумма по всему регистру до/после.</summary>
    private static async Task<decimal> InventoryValueTotalAsync(string resource)
    {
        decimal sum = 0m;
        foreach (var row in await TotalsManager.QueryBalancesAsync("InventoryValue"))
            if (row.TryGetValue(resource, out var v) && v != null) sum += Convert.ToDecimal(v);
        return sum;
    }
}
