using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Тестовый скрипт НЕ получает global usings — пространства имён названы явно.
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// ЗАГРУЗКА РАБОЧЕГО ЦЕНТРА.
//
// До этого среза очередь у участка была невыразима: операции штамповались на
// заказ, но спросить «сколько минут стоит на этом участке» было не у кого, а у
// самого участка не было мощности.
//
// Два инварианта, которые здесь и проверяются:
//  1) очередь — это ЗАПУЩЕННЫЕ и ещё НЕ отмеченные операции. Черновик в неё не
//     входит, отмеченная операция из неё уходит — то есть цеховая отметка
//     разгружает участок;
//  2) перегрузка ПРЕДУПРЕЖДАЕТ, а не запрещает: мощность это план, и запуск
//     сверх неё законен.
public class WorkCenterLoadTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IDocumentManager Docs => GetService<IDocumentManager>();
    private static ITotalsManager Totals => GetService<ITotalsManager>();
    private static IWorkCenterLoadService Load => GetService<IWorkCenterLoadService>();

    [IntegrationTest("Очередь считает только запущенные и неотмеченные операции")]
    public async Task QueueCountsReleasedAndUnconfirmedOnly()
    {
        var f = await FixtureAsync("Очередь", capacity: 0m);
        var day = DateTime.UtcNow.Date;

        // Черновик в очередь не входит.
        var draft = await NewOrderAsync(f, day, setup: 30, run: 90m);
        var q0 = await Load.QueuedMinutesAsync(f.WorkCenter, day, Guid.Empty);
        Assert.IsTrue(q0 == 0m, "черновик не стоит в очереди, факт {0}", q0);

        // Запустили — 30 + 90 = 120 минут встали на участок.
        draft.Subtype = ProductionOrder.Subtypes.Released;
        await Docs.SaveDocumentAsync(draft);
        var q1 = await Load.QueuedMinutesAsync(f.WorkCenter, day, Guid.Empty);
        Assert.IsTrue(q1 == 120m, "запущенный заказ даёт 30+90=120, факт {0}", q1);

        // Отметили операцию — она ушла из очереди, хотя заказ ещё Released.
        var live = await Docs.GetDocumentAsync<ProductionOrder>(draft.MetaId);
        live!.Operations[0].ActualMinutes = 120m;
        live.Operations[0].CompletedOn = day;
        await Docs.SaveDocumentAsync(live);

        var q2 = await Load.QueuedMinutesAsync(f.WorkCenter, day, Guid.Empty);
        Assert.IsTrue(q2 == 0m,
            "отмеченная операция разгружает участок, хотя заказ ещё в работе, факт {0}", q2);
    }

    [IntegrationTest("Мощность ноль = потолок не объявлен, перегрузки не бывает")]
    public async Task ZeroCapacityNeverWarns()
    {
        var f = await FixtureAsync("БезПотолка", capacity: 0m);
        var day = DateTime.UtcNow.Date;
        var order = await NewOrderAsync(f, day, setup: 600, run: 600m);
        order.Subtype = ProductionOrder.Subtypes.Released;
        await Docs.SaveDocumentAsync(order);

        var warn = await Load.OverloadWarningAsync(order.MetaId);
        Assert.IsTrue(warn == null,
            "незаполненная мощность не должна давать перегрузку, факт: {0}", warn ?? "<null>");
    }

    [IntegrationTest("Сверх мощности предупреждает, называя участок и числа, но не запрещает")]
    public async Task OverCapacityWarnsWithoutBlocking()
    {
        var f = await FixtureAsync("Потолок", capacity: 480m);
        var day = DateTime.UtcNow.Date;

        // 300 минут — в мощность 480 влезает.
        var first = await NewOrderAsync(f, day, setup: 0, run: 300m);
        first.Subtype = ProductionOrder.Subtypes.Released;
        await Docs.SaveDocumentAsync(first);
        var ok = await Load.OverloadWarningAsync(first.MetaId);
        Assert.IsTrue(ok == null, "300 при мощности 480 — не перегрузка, факт: {0}", ok ?? "<null>");

        // Второй заказ на 300: вместе 600 > 480.
        var second = await NewOrderAsync(f, day, setup: 0, run: 300m);
        second.Subtype = ProductionOrder.Subtypes.Released;
        await Docs.SaveDocumentAsync(second);

        var warn = await Load.OverloadWarningAsync(second.MetaId);
        Assert.IsTrue(warn != null, "600 при мощности 480 обязано предупредить");
        Assert.IsTrue(warn!.Contains("600"), "в тексте должна быть загрузка 600, факт: {0}", warn);
        Assert.IsTrue(warn.Contains("480"), "и мощность 480, факт: {0}", warn);
        Assert.IsTrue(warn.Contains("Потолок"), "и ИМЯ участка, иначе его не найти, факт: {0}", warn);

        // И всё же запущен: мощность — план, а не запрет.
        var live = await Docs.GetDocumentAsync<ProductionOrder>(second.MetaId);
        Assert.IsTrue(live!.Subtype == ProductionOrder.Subtypes.Released,
            "перегрузка не отменяет запуск, факт {0}", live.Subtype ?? "<null>");
    }

    [IntegrationTest("Последовательность сдвигает операцию на следующий день, только когда в этот она не влезает")]
    public async Task SequenceSpillsOnlyWhenTheDayIsFull()
    {
        var f = await FixtureAsync("Срок", capacity: 100m);
        var day = new DateTime(2026, 9, 24);

        var tight = await NewOrderAsync(f, day, setup: 0, run: 80m);
        tight = await AddOpAsync(tight, 20, 0, 80m, f.WorkCenter);
        var n = await Load.ScheduleAsync(tight.MetaId);
        Assert.IsTrue(n == 2, "расставлены обе операции, факт {0}", n);

        var live = await Docs.GetDocumentAsync<ProductionOrder>(tight.MetaId);
        var first = live!.Operations.Single(o => o.Sequence == 1);
        var second = live.Operations.Single(o => o.Sequence == 20);
        Assert.IsTrue(first.ScheduledOn is DateTime a && a.Date == day,
            "первая операция остаётся на дате заказа, факт {0}", first.ScheduledOn);
        Assert.IsTrue(second.ScheduledOn is DateTime b && b.Date == day.AddDays(1),
            "80+80 при мощности 100 сажает вторую на следующий день, факт {0}", second.ScheduledOn);

        live.Subtype = ProductionOrder.Subtypes.Released;
        await Docs.SaveDocumentAsync(live);
        var q0 = await Load.QueuedMinutesAsync(f.WorkCenter, day, Guid.Empty);
        var q1 = await Load.QueuedMinutesAsync(f.WorkCenter, day.AddDays(1), Guid.Empty);
        Assert.IsTrue(q0 == 80m, "в день заказа очередь 80, не весь заказ, факт {0}", q0);
        Assert.IsTrue(q1 == 80m, "на следующий день очередь 80, факт {0}", q1);

        var fits = await NewOrderAsync(f, day.AddDays(3), setup: 0, run: 30m);
        fits = await AddOpAsync(fits, 20, 0, 30m, f.WorkCenter);
        await Load.ScheduleAsync(fits.MetaId);
        var both = await Docs.GetDocumentAsync<ProductionOrder>(fits.MetaId);
        Assert.IsTrue(both!.Operations.All(o => o.ScheduledOn is DateTime d && d.Date == day.AddDays(3)),
            "30+30 при мощности 100 остаются в один день");
    }

    [IntegrationTest("Необъявленная мощность не сдвигает сроки, чужая очередь сдвигает")]
    public async Task UndeclaredCapacityStaysPutAndForeignQueuePushes()
    {
        var open = await FixtureAsync("БезСрока", capacity: 0m);
        var day = new DateTime(2026, 9, 24);
        var order = await NewOrderAsync(open, day, setup: 0, run: 80m);
        order = await AddOpAsync(order, 20, 0, 80m, open.WorkCenter);
        await Load.ScheduleAsync(order.MetaId);
        var live = await Docs.GetDocumentAsync<ProductionOrder>(order.MetaId);
        Assert.IsTrue(live!.Operations.All(o => o.ScheduledOn is DateTime d && d.Date == day),
            "мощность 0 не разносит операции по дням");

        var f = await FixtureAsync("Чужая", capacity: 100m);
        var taken = await NewOrderAsync(f, day, setup: 0, run: 80m);
        taken.Subtype = ProductionOrder.Subtypes.Released;
        await Docs.SaveDocumentAsync(taken);

        var mine = await NewOrderAsync(f, day, setup: 0, run: 80m);
        await Load.ScheduleAsync(mine.MetaId);
        var pushed = await Docs.GetDocumentAsync<ProductionOrder>(mine.MetaId);
        var when = pushed!.Operations.Single().ScheduledOn;
        Assert.IsTrue(when is DateTime p && p.Date == day.AddDays(1),
            "80 чужих минут при мощности 100 сдвигают срок на следующий день, факт {0}", when);
    }

    // ── фикстура ────────────────────────────────────────────────────────────

    private sealed class Fixture
    {
        public Guid Product;
        public Guid Component;
        public Guid Location;
        public Guid WorkCenter;
    }

    private async Task<Fixture> FixtureAsync(string tag, decimal capacity)
    {
        var uom = Dict.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PC{Db.NewId():N}"[..8];
        uom = await Dict.SaveRecordAsync(uom);

        var group = Dict.NewRecord<ItemGroup>();
        group.Code = $"CAP{Db.NewId():N}"[..10];
        group.Name = "Мощность";
        group = await Dict.SaveRecordAsync(group);

        async Task<Guid> NewItemAsync(string name)
        {
            var item = Dict.NewRecord<Item>();
            item.Name = name;
            item.ItemGroup = group.MetaId;
            item.UnitOfMeasure = uom.MetaId;
            return (await Dict.SaveRecordAsync(item)).MetaId;
        }

        var f = new Fixture
        {
            Product = await NewItemAsync($"Изделие-{tag}"),
            Component = await NewItemAsync($"Компонент-{tag}"),
            Location = Db.NewId(),
        };

        await Totals.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = f.Location, ["Item"] = f.Component },
            new Dictionary<string, decimal> { ["Qty"] = 500m });

        var center = Dict.NewRecord<WorkCenter>();
        center.Name = tag;
        center.CapacityMinutesPerDay = capacity;
        f.WorkCenter = (await Dict.SaveRecordAsync(center)).MetaId;
        return f;
    }

    private static async Task<ProductionOrder> NewOrderAsync(
        Fixture f, DateTime day, int setup, decimal run)
    {
        var order = await Docs.NewDocumentAsync<ProductionOrder>();
        order.Product = f.Product;
        order.Quantity = 1m;
        order.Location = f.Location;
        order.DocumentDate = day;
        order.Components.Add(new ProductionOrderComponentsTablePartRow
        {
            Component = f.Component,
            QtyRequired = 1m,
        });
        order.Operations.Add(new ProductionOrderOperationsTablePartRow
        {
            Sequence = 1,
            Name = "Операция",
            WorkCenter = f.WorkCenter,
            SetupMinutes = setup,
            RunMinutes = run,
        });
        await Docs.SaveDocumentAsync(order);
        return (await Docs.GetDocumentAsync<ProductionOrder>(order.MetaId))!;
    }

    private static async Task<ProductionOrder> AddOpAsync(
        ProductionOrder order, int sequence, int setup, decimal run, Guid center)
    {
        order.Operations.Add(new ProductionOrderOperationsTablePartRow
        {
            Sequence = sequence,
            Name = $"Оп{sequence}",
            WorkCenter = center,
            SetupMinutes = setup,
            RunMinutes = run,
        });
        await Docs.SaveDocumentAsync(order);
        return (await Docs.GetDocumentAsync<ProductionOrder>(order.MetaId))!;
    }
}
