using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

public class FieldDeskTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static IInformationRegisterService Registers => GetService<IInformationRegisterService>();
    private static IFieldDeskService Desk => GetService<IFieldDeskService>();

    [IntegrationTest("Пульт поля видит последнюю точку GPS агента и сегодняшний визит")]
    public async Task DeskShowsLastFixAndTodaysVisit()
    {
        var now = DateTime.UtcNow;
        var day = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
        var agent = DictionaryManager.NewRecord<SalesPerson>();
        agent.Name = "Field desk Ivan";
        agent.UserId = Guid.NewGuid();
        agent = await DictionaryManager.SaveRecordAsync(agent);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Desk kiosk";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var route = await DocumentManager.NewDocumentAsync<VisitRoute>();
        route.Agent = agent.MetaId;
        route.RouteDate = day;
        route.DocumentDate = now;
        route.Stops.Add(new VisitRouteStopsTablePartRow
        {
            Customer = customer.MetaId,
            Sequence = 1,
            StopStatus = "Planned",
        });
        await DocumentManager.SaveDocumentAsync(route);
        await Db.ChangeSubtypeAsync("VisitRoute", route.MetaId, VisitRoute.Subtypes.Open);

        var visit = await DocumentManager.NewDocumentAsync<AgentVisit>();
        visit.Customer = customer.MetaId;
        visit.Route = route.MetaId;
        visit.StopSequence = 1;
        visit.DocumentDate = now;
        visit.CheckedInAt = now.AddMinutes(-10);
        visit.CheckedOutAt = now.AddMinutes(-2);
        visit.GeoStatus = "Ok";
        visit.Lat = 50.45m;
        visit.Lng = 30.52m;
        await DocumentManager.SaveDocumentAsync(visit);

        await Registers.SetAsync(
            "AgentGpsLog",
            now.AddMinutes(-3),
            new Dictionary<string, object?> { ["Agent"] = agent.MetaId },
            new Dictionary<string, object?> { ["Lat"] = 50.451m, ["Lng"] = 30.523m, ["Accuracy"] = 12m });

        var snap = await Desk.GetSnapshotAsync(now);
        var row = snap.Agents.SingleOrDefault(a => a.Id == agent.MetaId);
        Assert.IsTrue(row != null, "агент есть на пульте");
        Assert.IsTrue(row!.LastFix != null, "последняя точка GPS есть");
        Assert.IsTrue(row.LastFix!.Lat == 50.451m, "широта с трека, факт {0}", row.LastFix.Lat);
        Assert.IsTrue(row.OnShift, "точка свежее 15 минут — смена открыта");
        Assert.IsTrue(row.TodayRoutes == 1, "сегодняшний маршрут, факт {0}", row.TodayRoutes);
        Assert.IsTrue(row.TodayVisits == 1, "сегодняшний визит, факт {0}", row.TodayVisits);
        Assert.IsTrue(snap.Visits.Any(v => v.Id == visit.MetaId && v.CustomerName == "Desk kiosk"),
            "визит на пульте назван именем клиента");
    }

    [IntegrationTest("Визит без маршрута есть в visits, не на карточке агента")]
    public async Task DeskKeepsVisitWithoutRouteOffAgentCard()
    {
        var now = DateTime.UtcNow;
        var agent = DictionaryManager.NewRecord<SalesPerson>();
        agent.Name = "Orphan desk agent";
        agent.UserId = Guid.NewGuid();
        agent = await DictionaryManager.SaveRecordAsync(agent);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Walk-in kiosk";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var visit = await DocumentManager.NewDocumentAsync<AgentVisit>();
        visit.Customer = customer.MetaId;
        visit.DocumentDate = now;
        visit.CheckedInAt = now.AddMinutes(-5);
        visit.CheckedOutAt = now.AddMinutes(-1);
        visit.GeoStatus = "Ok";
        visit.Lat = 50.45m;
        visit.Lng = 30.52m;
        await DocumentManager.SaveDocumentAsync(visit);

        var snap = await Desk.GetSnapshotAsync(now);
        var row = snap.Visits.SingleOrDefault(v => v.Id == visit.MetaId);
        Assert.IsTrue(row != null, "визит без маршрута в списке дня");
        Assert.IsTrue(row!.AgentId == null, "агент не проставлен — нет маршрута");
        var agentCard = snap.Agents.SingleOrDefault(a => a.Id == agent.MetaId);
        Assert.IsTrue(agentCard != null, "агент на пульте есть");
        Assert.IsTrue(agentCard!.TodayVisits == 0, "карточка агента этот визит не считает, факт {0}", agentCard.TodayVisits);
    }

    [IntegrationTest("Точка старше 15 минут — смена закрыта")]
    public async Task DeskMarksStaleFixOffShift()
    {
        var now = DateTime.UtcNow;
        var agent = DictionaryManager.NewRecord<SalesPerson>();
        agent.Name = "Stale GPS agent";
        agent.UserId = Guid.NewGuid();
        agent = await DictionaryManager.SaveRecordAsync(agent);

        await Registers.SetAsync(
            "AgentGpsLog",
            now.AddMinutes(-16),
            new Dictionary<string, object?> { ["Agent"] = agent.MetaId },
            new Dictionary<string, object?> { ["Lat"] = 50.45m, ["Lng"] = 30.52m });

        var snap = await Desk.GetSnapshotAsync(now);
        var row = snap.Agents.SingleOrDefault(a => a.Id == agent.MetaId);
        Assert.IsTrue(row != null && row.LastFix != null, "последняя точка есть");
        Assert.IsTrue(!row!.OnShift, "16 минут — уже не смена");
    }

    [IntegrationTest("Окно from/asOf не тащит вчерашний визит в сегодняшний день")]
    public async Task DeskWindowExcludesYesterdayVisit()
    {
        var now = DateTime.UtcNow;
        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Yesterday kiosk";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var visit = await DocumentManager.NewDocumentAsync<AgentVisit>();
        visit.Customer = customer.MetaId;
        visit.DocumentDate = now.AddDays(-1);
        visit.CheckedInAt = now.AddDays(-1);
        visit.CheckedOutAt = now.AddDays(-1).AddMinutes(20);
        visit.GeoStatus = "Ok";
        visit.Lat = 50.45m;
        visit.Lng = 30.52m;
        await DocumentManager.SaveDocumentAsync(visit);

        var snap = await Desk.GetSnapshotAsync(now, now.Date);
        Assert.IsTrue(!snap.Visits.Any(v => v.Id == visit.MetaId),
            "вчерашний визит не в окне сегодняшнего дня");
    }

    [IntegrationTest("Диспетчерская отдаёт трек агента по порядку точек")]
    public async Task DeskReturnsOrderedTrackForAgent()
    {
        var now = DateTime.UtcNow;
        var agent = DictionaryManager.NewRecord<SalesPerson>();
        agent.Name = "Track Ivan";
        agent.UserId = Guid.NewGuid();
        agent = await DictionaryManager.SaveRecordAsync(agent);

        await Registers.SetAsync(
            "AgentGpsLog",
            now.AddMinutes(-12),
            new Dictionary<string, object?> { ["Agent"] = agent.MetaId },
            new Dictionary<string, object?> { ["Lat"] = 50.450m, ["Lng"] = 30.520m });
        await Registers.SetAsync(
            "AgentGpsLog",
            now.AddMinutes(-7),
            new Dictionary<string, object?> { ["Agent"] = agent.MetaId },
            new Dictionary<string, object?> { ["Lat"] = 50.452m, ["Lng"] = 30.524m });
        await Registers.SetAsync(
            "AgentGpsLog",
            now.AddMinutes(-2),
            new Dictionary<string, object?> { ["Agent"] = agent.MetaId },
            new Dictionary<string, object?> { ["Lat"] = 50.455m, ["Lng"] = 30.530m });

        var tracks = await Desk.GetTracksAsync(now.Date, now, agent.MetaId);
        var mine = tracks.Tracks.SingleOrDefault(t => t.AgentId == agent.MetaId);
        Assert.IsTrue(mine != null, "трек агента есть");
        Assert.IsTrue(mine!.Points.Count == 3, "три точки, факт {0}", mine.Points.Count);
        Assert.IsTrue(mine.Points[0].Lat == 50.450m, "первая точка самая ранняя, факт {0}", mine.Points[0].Lat);
        Assert.IsTrue(mine.Points[2].Lat == 50.455m, "последняя точка самая свежая, факт {0}", mine.Points[2].Lat);
    }
}
